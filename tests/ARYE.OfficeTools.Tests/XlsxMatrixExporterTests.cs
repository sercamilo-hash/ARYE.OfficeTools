using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using ARYE.OfficeTools.Core.Export;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Zoning;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class XlsxMatrixExporterTests
    {
        private static ClassifiedArea Area(string building, string level, int index,
            string name, CategoryRole role, double sf) => new ClassifiedArea
            {
                BuildingName = building,
                LevelName = level,
                LevelIndex = index,
                CategoryName = name,
                Role = role,
                SquareFeet = sf
            };

        private static OfficeMatrix SampleMatrix(out ZoningParameters parameters)
        {
            parameters = new ZoningParameters
            {
                LotAreaSqFt = 5000,
                MaxFar = 2,
                District = "R7-2",
                EnergyDeductionEnabled = true,
                EnergyDeductionPercent = 5
            };
            var areas = new List<ClassifiedArea>
            {
                Area("BLDG A", "L01", 1, "Gross", CategoryRole.MainGross, 8506.59),
                Area("BLDG A", "L01", 1, "Chase Walls", CategoryRole.Deduction, 250),
                Area("BLDG A", "L01", 1, "Corridor", CategoryRole.Deduction, 887.60),
                Area("BLDG A", "L01", 1, "Refuse", CategoryRole.Deduction, 33),
                Area("BLDG B", "L01", 1, "Gross", CategoryRole.MainGross, 1000)
            };
            return new OfficeMatrixCalculator().Calculate(areas, CategoryCatalog.Office(), parameters);
        }

        private static string ExportAndReadSheet(OfficeMatrix matrix, ZoningParameters parameters,
            out string contentTypes)
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".xlsx");
            try
            {
                new XlsxMatrixExporter().Export(path, matrix, parameters);
                using (ZipArchive zip = ZipFile.OpenRead(path))
                {
                    contentTypes = ReadEntry(zip, "[Content_Types].xml");
                    Assert.NotNull(zip.GetEntry("_rels/.rels"));
                    Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
                    Assert.NotNull(zip.GetEntry("xl/styles.xml"));
                    return ReadEntry(zip, "xl/worksheets/sheet1.xml");
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string ReadEntry(ZipArchive zip, string name)
        {
            ZipArchiveEntry entry = zip.GetEntry(name);
            Assert.NotNull(entry);
            using (var reader = new StreamReader(entry.Open()))
            {
                return reader.ReadToEnd();
            }
        }

        [Fact]
        public void Export_IsAValidPackage_WithLiveFormulasAndCachedValues()
        {
            OfficeMatrix matrix = SampleMatrix(out ZoningParameters parameters);
            string sheet = ExportAndReadSheet(matrix, parameters, out string contentTypes);

            Assert.Contains("spreadsheetml.sheet.main+xml", contentTypes);

            // 7 office deduction columns → D..J deductions, K NET, L ENERGY, M ZFA.
            // BLDG A's first level row is row 11 (meta 1–6, header 8–9, name 10).
            Assert.Contains("<f>B11-SUM(D11:J11)</f>", sheet);
            Assert.Contains("<f>K11*$B$5/100</f>", sheet);
            Assert.Contains("<f>K11-L11</f>", sheet);

            // Cached values so the file opens showing numbers before recalc.
            Assert.Contains("<v>7335.99</v>", sheet);
            Assert.Contains("<v>366.7995</v>", sheet);
            Assert.Contains("<v>6969.1905</v>", sheet);

            // TOTALS row of BLDG A (row 12) sums the level rows per column.
            Assert.Contains("<f>SUM(B11:B11)</f>", sheet);

            // Grand total sums the buildings' TOTALS cells (rows 12 and 16).
            Assert.Contains("<f>B12+B16</f>", sheet);
            Assert.Contains("TOTAL — ALL BUILDINGS", sheet);

            // Two-row header: merged DEDUCTIONS span and vertical single-label merges.
            Assert.Contains("DEDUCTIONS", sheet);
            Assert.Contains("<mergeCell ref=\"D8:J8\"/>", sheet);
            Assert.Contains("<mergeCell ref=\"A8:A9\"/>", sheet);
            Assert.Contains("CHASE WALLS", sheet);

            // The labeled energy-percent cell holds the live rate.
            Assert.Contains("Energy deduction % of NET", sheet);
            Assert.Contains("<c r=\"B5\"", sheet);
        }

        [Fact]
        public void Export_EnergyColumn_CarriesTheYellowStyle()
        {
            OfficeMatrix matrix = SampleMatrix(out ZoningParameters parameters);
            string sheet = ExportAndReadSheet(matrix, parameters, out _);

            // Energy header (style 7) and energy level cells (style 3).
            Assert.Contains("<c r=\"L8\" s=\"7\"", sheet);
            Assert.Contains("<c r=\"L11\" s=\"3\"", sheet);
        }

        [Fact]
        public void Export_EnergyDisabled_ZeroesThePercentCell_FormulasStayLive()
        {
            var parameters = new ZoningParameters { EnergyDeductionEnabled = false };
            var areas = new List<ClassifiedArea>
            {
                Area("B", "L01", 1, "Gross", CategoryRole.MainGross, 1000)
            };
            OfficeMatrix matrix = new OfficeMatrixCalculator()
                .Calculate(areas, CategoryCatalog.Office(), parameters);

            string sheet = ExportAndReadSheet(matrix, parameters, out _);

            Assert.Contains("<c r=\"B5\" s=\"3\"><v>0</v></c>", sheet);
            Assert.Contains("*$B$5/100</f>", sheet); // typing a rate revives the column
        }

        [Theory]
        [InlineData(1, "A")]
        [InlineData(26, "Z")]
        [InlineData(27, "AA")]
        [InlineData(52, "AZ")]
        public void ColumnLetter_HandlesTheTwoLetterRange(int column, string expected)
        {
            Assert.Equal(expected, XlsxMatrixExporter.ColumnLetter(column));
        }
    }
}
