using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Zoning;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class OfficeMatrixCalculatorTests
    {
        private static readonly CategoryCatalog Catalog = CategoryCatalog.Office();
        private static readonly OfficeMatrixCalculator Calculator = new OfficeMatrixCalculator();

        private static ClassifiedArea Area(string building, string level, int levelIndex,
            string categoryName, double squareFeet)
        {
            CategoryDefinition match = Catalog.Match(categoryName);
            return new ClassifiedArea
            {
                BuildingName = building,
                LevelName = level,
                LevelIndex = levelIndex,
                CategoryName = match?.Name,
                Role = match?.Role,
                SquareFeet = squareFeet,
                AreaName = categoryName ?? "AREA"
            };
        }

        private static ZoningParameters EnergyOn5Pct() => new ZoningParameters
        {
            EnergyDeductionEnabled = true,
            EnergyDeductionPercent = 5.0
        };

        [Fact]
        public void ReferenceNumbers_WholeBuilding_MatchTheOfficeSheet()
        {
            // The approved arithmetic, straight from the office reference sheet:
            // gross 8,506.59 − (250 + 887.60 + 33) = NET 7,335.99;
            // ENERGY(5%) 366.80; ZFA 6,969.19.
            var areas = new List<ClassifiedArea>
            {
                Area("BLDG A", "L02", 2, "Gross", 8506.59),
                Area("BLDG A", "L02", 2, "Chase Walls", 250),
                Area("BLDG A", "L02", 2, "Corridor", 887.60),
                Area("BLDG A", "L02", 2, "Refuse", 33)
            };

            OfficeMatrix matrix = Calculator.Calculate(areas, Catalog, EnergyOn5Pct());

            OfficeMatrixLine line = Assert.Single(Assert.Single(matrix.Buildings).Levels);
            Assert.Equal(8506.59, line.MainGross.Value, 2);
            Assert.Equal(7335.99, line.Net, 2);
            Assert.Equal(366.80, line.Energy, 2);
            Assert.Equal(6969.19, line.Zfa, 2);
        }

        [Fact]
        public void ReferenceNumbers_FirstFloor_MatchTheOfficeSheet()
        {
            // 1st floor: 1,410.05 − 143.13 − 289.83 = 977.09 → 48.85 → 928.24.
            var areas = new List<ClassifiedArea>
            {
                Area("BLDG A", "1st FLOOR", 1, "Gross", 1410.05),
                Area("BLDG A", "1st FLOOR", 1, "Chase Walls", 143.13),
                Area("BLDG A", "1st FLOOR", 1, "Corridor", 289.83)
            };

            OfficeMatrix matrix = Calculator.Calculate(areas, Catalog, EnergyOn5Pct());

            OfficeMatrixLine line = matrix.Buildings[0].Levels[0];
            Assert.Equal(977.09, line.Net, 2);
            Assert.Equal(48.85, line.Energy, 2);
            Assert.Equal(928.24, line.Zfa, 2);
        }

        [Fact]
        public void CommercialGross_IsInformational_NeverPartOfNet()
        {
            var areas = new List<ClassifiedArea>
            {
                Area("BLDG A", "L01", 1, "Gross", 1000),
                Area("BLDG A", "L01", 1, "Commercial Gross", 400),
                Area("BLDG A", "L01", 1, "Mechanical", 100)
            };

            OfficeMatrix matrix = Calculator.Calculate(areas, Catalog, new ZoningParameters());

            OfficeMatrixLine line = matrix.Buildings[0].Levels[0];
            Assert.Equal(400, line.CommercialGross.Value, 2);
            Assert.Equal(900, line.Net, 2); // 1000 − 100; the 400 commercial never enters
            Assert.Equal(900, line.Zfa, 2); // energy disabled → ZFA = NET
            Assert.Equal(0, line.Energy, 2);
        }

        [Fact]
        public void EnergyDisabled_TakesNothingOffNet()
        {
            var areas = new List<ClassifiedArea> { Area("B", "L01", 1, "Gross", 1000) };
            var parameters = new ZoningParameters
            {
                EnergyDeductionEnabled = false,
                EnergyDeductionPercent = 5.0
            };

            OfficeMatrix matrix = Calculator.Calculate(areas, Catalog, parameters);

            Assert.Equal(1000, matrix.Buildings[0].Levels[0].Zfa, 2);
            Assert.False(matrix.EnergyEnabled);
        }

        [Fact]
        public void ExemptAreas_CountInNoTotal()
        {
            var areas = new List<ClassifiedArea>
            {
                Area("B", "L01", 1, "Gross", 1000),
                Area("B", "L01", 1, "Exempt", 500)
            };

            OfficeMatrix matrix = Calculator.Calculate(areas, Catalog, new ZoningParameters());

            Assert.Equal(1000, matrix.Buildings[0].Levels[0].MainGross.Value, 2);
            Assert.Equal(1000, matrix.GrandTotal.Zfa, 2);
        }

        [Fact]
        public void UnclassifiedAreas_CountIntoMainGross_AndFlagTheLine()
        {
            var areas = new List<ClassifiedArea>
            {
                Area("B", "L01", 1, "Gross", 1000),
                Area("B", "L01", 1, "Storage Room", 50) // unknown → unclassified
            };

            OfficeMatrix matrix = Calculator.Calculate(areas, Catalog, new ZoningParameters());

            OfficeMatrixLine line = matrix.Buildings[0].Levels[0];
            Assert.Equal(1050, line.MainGross.Value, 2); // conservative: counted, never dropped
            Assert.Equal(1, line.UnclassifiedCount);
            Assert.Equal(1, matrix.UnclassifiedAreaCount);
            Assert.Equal(50, matrix.UnclassifiedSqFt, 2);
        }

        [Fact]
        public void EmptyCells_StayNull_NotZero()
        {
            var areas = new List<ClassifiedArea>
            {
                Area("B", "L01", 1, "Mechanical", 100) // a level with only a deduction
            };

            OfficeMatrix matrix = Calculator.Calculate(areas, Catalog, new ZoningParameters());

            OfficeMatrixLine line = matrix.Buildings[0].Levels[0];
            Assert.Null(line.MainGross);
            Assert.Null(line.CommercialGross);
            Assert.Equal(-100, line.Net, 2); // honest arithmetic, visible to the user
        }

        [Fact]
        public void BuildingTotals_AndGrandTotal_SumAcross()
        {
            var areas = new List<ClassifiedArea>
            {
                Area("BLDG A", "L01", 1, "Gross", 1000),
                Area("BLDG A", "L02", 2, "Gross", 1000),
                Area("BLDG A", "L02", 2, "Mechanical", 200),
                Area("BLDG B", "L01", 1, "Gross", 500)
            };

            OfficeMatrix matrix = Calculator.Calculate(areas, Catalog, EnergyOn5Pct());

            Assert.Equal(2, matrix.Buildings.Count);
            OfficeMatrixLine totalsA = matrix.Buildings[0].Totals;
            Assert.Equal(2000, totalsA.MainGross.Value, 2);
            Assert.Equal(200, totalsA.Deductions["Mechanical"], 2);
            Assert.Equal(1800, totalsA.Net, 2);
            Assert.Equal(90, totalsA.Energy, 2);
            Assert.Equal(1710, totalsA.Zfa, 2);

            Assert.Equal(2500, matrix.GrandTotal.MainGross.Value, 2);
            Assert.Equal(2300, matrix.GrandTotal.Net, 2);
            Assert.Equal(2185, matrix.GrandTotal.Zfa, 2);
            Assert.Equal(2185, matrix.TotalZfaSqFt, 2);
            Assert.Equal(200, matrix.DeductionTotals["Mechanical"], 2);
        }

        [Fact]
        public void LevelsSortBottomUp_BuildingsKeepHarvestOrder()
        {
            var areas = new List<ClassifiedArea>
            {
                Area("BLDG B", "L05", 5, "Gross", 10), // harvest met BLDG B first
                Area("BLDG A", "L03", 3, "Gross", 10),
                Area("BLDG B", "L01", 1, "Gross", 10)
            };

            OfficeMatrix matrix = Calculator.Calculate(areas, Catalog, new ZoningParameters());

            Assert.Equal("BLDG B", matrix.Buildings[0].BuildingName);
            Assert.Equal(new[] { "L01", "L05" },
                matrix.Buildings[0].Levels.Select(l => l.LevelName));
        }

        [Fact]
        public void FarCheck_UsesLotTimesFar()
        {
            var areas = new List<ClassifiedArea> { Area("B", "L01", 1, "Gross", 12000) };
            var parameters = new ZoningParameters { LotAreaSqFt = 5000, MaxFar = 2 };

            OfficeMatrix matrix = Calculator.Calculate(areas, Catalog, parameters);

            Assert.Equal(10000, matrix.AllowedZfaSqFt, 2);
            Assert.True(matrix.IsOverFar);
            Assert.Equal(-2000, matrix.RemainingSqFt, 2);
            Assert.Equal(2.4, matrix.FarUsed, 2);
        }

        [Fact]
        public void ConsultantSpecificCategoryList_DrivesTheColumns()
        {
            var catalog = new CategoryCatalog(new[]
            {
                new CategoryDefinition("GFA", CategoryRole.MainGross),
                new CategoryDefinition("MEP", CategoryRole.Deduction),
                new CategoryDefinition("Trash", CategoryRole.Deduction)
            });
            var areas = new List<ClassifiedArea>
            {
                new ClassifiedArea
                {
                    BuildingName = "B", LevelName = "L01", LevelIndex = 1,
                    CategoryName = "GFA", Role = CategoryRole.MainGross, SquareFeet = 800
                },
                new ClassifiedArea
                {
                    BuildingName = "B", LevelName = "L01", LevelIndex = 1,
                    CategoryName = "MEP", Role = CategoryRole.Deduction, SquareFeet = 80
                }
            };

            OfficeMatrix matrix = Calculator.Calculate(areas, catalog, new ZoningParameters());

            Assert.Equal(new[] { "MEP", "Trash" }, matrix.DeductionColumns);
            Assert.Equal(720, matrix.GrandTotal.Zfa, 2);
        }
    }
}
