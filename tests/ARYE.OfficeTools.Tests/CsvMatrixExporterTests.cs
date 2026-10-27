using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARYE.OfficeTools.Core.Export;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Zoning;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class CsvMatrixExporterTests
    {
        private static OfficeMatrix SampleMatrix(out ZoningParameters parameters)
        {
            parameters = new ZoningParameters
            {
                LotAreaSqFt = 5000,
                MaxFar = 2,
                District = "C4-4D",
                EnergyDeductionEnabled = true,
                EnergyDeductionPercent = 5
            };
            var areas = new List<ClassifiedArea>
            {
                new ClassifiedArea
                {
                    BuildingName = "BLDG A, Phase \"1\"", // comma + quotes force escaping
                    LevelName = "L01", LevelIndex = 1,
                    CategoryName = "Gross", Role = CategoryRole.MainGross, SquareFeet = 8506.59
                },
                new ClassifiedArea
                {
                    BuildingName = "BLDG A, Phase \"1\"",
                    LevelName = "L01", LevelIndex = 1,
                    CategoryName = "Chase Walls", Role = CategoryRole.Deduction, SquareFeet = 250
                },
                new ClassifiedArea
                {
                    BuildingName = "BLDG A, Phase \"1\"",
                    LevelName = "L01", LevelIndex = 1,
                    CategoryName = "Corridor", Role = CategoryRole.Deduction, SquareFeet = 887.60
                },
                new ClassifiedArea
                {
                    BuildingName = "BLDG A, Phase \"1\"",
                    LevelName = "L01", LevelIndex = 1,
                    CategoryName = "Refuse", Role = CategoryRole.Deduction, SquareFeet = 33
                }
            };
            return new OfficeMatrixCalculator().Calculate(areas, CategoryCatalog.Office(), parameters);
        }

        [Fact]
        public void Export_WritesOfficeFormatRowsAndTotals()
        {
            OfficeMatrix matrix = SampleMatrix(out ZoningParameters parameters);
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
            try
            {
                new CsvMatrixExporter().Export(path, matrix, parameters);
                string[] lines = File.ReadAllLines(path);

                // Dynamic deduction columns come from the category list.
                string header = lines.First(l => l.StartsWith("BUILDING,LEVEL,"));
                Assert.Contains("CHASE WALLS", header);
                Assert.Contains("CORRIDOR", header);
                Assert.Contains(",NET FLOOR AREA,ENERGY DEDUCTION,ZONING FLOOR AREA", header);

                // Escaped building name, the reference numbers, blank commercial cell.
                string levelRow = lines.First(l => l.Contains(",L01,"));
                Assert.StartsWith("\"BLDG A, Phase \"\"1\"\"\",L01,8506.59,,", levelRow);
                Assert.Contains(",7335.99,366.80,6969.19", levelRow);

                Assert.Contains(lines, l => l.Contains(",TOTALS,"));
                Assert.Contains(lines, l => l.Contains("TOTAL — ALL BUILDINGS"));
                Assert.Contains(lines, l => l.StartsWith("Allowed ZFA (lot x FAR),10000.00"));
                Assert.Contains(lines, l => l.StartsWith("Over FAR,no"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Export_EnergyDisabled_LeavesTheEnergyCellBlank()
        {
            var parameters = new ZoningParameters { EnergyDeductionEnabled = false };
            var areas = new List<ClassifiedArea>
            {
                new ClassifiedArea
                {
                    BuildingName = "B", LevelName = "L01", LevelIndex = 1,
                    CategoryName = "Gross", Role = CategoryRole.MainGross, SquareFeet = 1000
                }
            };
            OfficeMatrix matrix = new OfficeMatrixCalculator()
                .Calculate(areas, CategoryCatalog.Office(), parameters);

            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
            try
            {
                new CsvMatrixExporter().Export(path, matrix, parameters);
                string levelRow = File.ReadAllLines(path).First(l => l.Contains(",L01,"));
                // …,NET,<blank energy>,ZFA
                Assert.Contains(",1000.00,,1000.00", levelRow);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
