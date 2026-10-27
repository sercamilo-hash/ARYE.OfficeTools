using System;
using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.Core.Zoning
{
    /// <summary>
    /// Pure engine for the office-format matrix. Per level:
    ///
    ///   NET    = MainGross − Σ deduction columns
    ///   ENERGY = EnergyDeductionPercent × NET   (only when enabled)
    ///   ZFA    = NET − ENERGY
    ///
    /// CommercialGross is tracked in its own column, informational — never
    /// part of NET. Exempt-role areas count in no total. Unclassified areas
    /// count into MainGross (conservative — no deduction can hide them) and
    /// flag the line, so the totals are visibly not final.
    ///
    /// This calculator does NOT use the old ZoningCalculator or its ZR rules;
    /// those remain as the ZR-validation reference.
    /// </summary>
    public sealed class OfficeMatrixCalculator
    {
        public OfficeMatrix Calculate(
            IReadOnlyList<ClassifiedArea> areas,
            CategoryCatalog catalog,
            ZoningParameters parameters)
        {
            if (areas == null) throw new ArgumentNullException(nameof(areas));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));

            var matrix = new OfficeMatrix
            {
                DeductionColumns = catalog.DeductionCategories.Select(c => c.Name).ToList(),
                EnergyEnabled = parameters.EnergyDeductionEnabled,
                EnergyPercent = parameters.EnergyDeductionPercent,
                LotAreaSqFt = parameters.LotAreaSqFt,
                MaxFar = parameters.MaxFar,
                AllowedZfaSqFt = parameters.LotAreaSqFt * parameters.MaxFar
            };

            // GroupBy preserves first-occurrence order, so buildings appear in
            // harvest order (the Step 1 building order) and need no re-sort.
            foreach (var buildingGroup in areas.GroupBy(a => a.BuildingName))
            {
                var building = new OfficeMatrixBuilding { BuildingName = buildingGroup.Key };

                foreach (var levelGroup in buildingGroup
                    .GroupBy(a => new { a.LevelName, a.LevelIndex, a.LevelUniqueId })
                    .OrderBy(g => g.Key.LevelIndex)
                    .ThenBy(g => g.Key.LevelName, StringComparer.OrdinalIgnoreCase))
                {
                    building.Levels.Add(BuildLevelLine(
                        levelGroup.Key.LevelName, levelGroup.Key.LevelIndex,
                        levelGroup.Key.LevelUniqueId, levelGroup.ToList(), parameters));
                }

                building.Totals = SumLines("TOTALS", building.Levels);
                matrix.Buildings.Add(building);
            }

            if (matrix.Buildings.Count > 0)
            {
                matrix.GrandTotal = SumLines(
                    "TOTAL — ALL BUILDINGS", matrix.Buildings.Select(b => b.Totals).ToList());
                matrix.TotalZfaSqFt = matrix.GrandTotal.Zfa;
                matrix.DeductionTotals = new Dictionary<string, double>(
                    matrix.GrandTotal.Deductions, StringComparer.OrdinalIgnoreCase);
                matrix.UnclassifiedAreaCount = matrix.GrandTotal.UnclassifiedCount;
            }

            matrix.UnclassifiedSqFt = areas
                .Where(a => a.CategoryName == null)
                .Sum(a => a.SquareFeet);

            return matrix;
        }

        private static OfficeMatrixLine BuildLevelLine(string levelName, int levelIndex,
            string levelUniqueId, List<ClassifiedArea> levelAreas, ZoningParameters parameters)
        {
            var line = new OfficeMatrixLine
            {
                LevelName = levelName,
                LevelIndex = levelIndex,
                LevelUniqueId = levelUniqueId
            };

            foreach (ClassifiedArea area in levelAreas)
            {
                if (area.CategoryName == null)
                {
                    // Unclassified: counts into MainGross, flagged — never dropped.
                    line.MainGross = (line.MainGross ?? 0) + area.SquareFeet;
                    line.UnclassifiedCount++;
                    continue;
                }
                switch (area.Role)
                {
                    case CategoryRole.MainGross:
                        line.MainGross = (line.MainGross ?? 0) + area.SquareFeet;
                        break;
                    case CategoryRole.CommercialGross:
                        line.CommercialGross = (line.CommercialGross ?? 0) + area.SquareFeet;
                        break;
                    case CategoryRole.Deduction:
                        line.Deductions.TryGetValue(area.CategoryName, out double sum);
                        line.Deductions[area.CategoryName] = sum + area.SquareFeet;
                        break;
                    case CategoryRole.Exempt:
                        break; // counts in no total
                }
            }

            line.DeductionTotal = line.Deductions.Values.Sum();
            line.Net = (line.MainGross ?? 0) - line.DeductionTotal;
            line.Energy = parameters.EnergyDeductionEnabled
                ? line.Net * parameters.EnergyDeductionPercent / 100.0
                : 0;
            line.Zfa = line.Net - line.Energy;
            return line;
        }

        /// <summary>Per-column sum of the given lines — a building's TOTALS or the grand total.</summary>
        private static OfficeMatrixLine SumLines(string label, IReadOnlyList<OfficeMatrixLine> lines)
        {
            var total = new OfficeMatrixLine { LevelName = label };
            foreach (OfficeMatrixLine line in lines)
            {
                if (line.MainGross.HasValue)
                {
                    total.MainGross = (total.MainGross ?? 0) + line.MainGross.Value;
                }
                if (line.CommercialGross.HasValue)
                {
                    total.CommercialGross = (total.CommercialGross ?? 0) + line.CommercialGross.Value;
                }
                foreach (KeyValuePair<string, double> deduction in line.Deductions)
                {
                    total.Deductions.TryGetValue(deduction.Key, out double sum);
                    total.Deductions[deduction.Key] = sum + deduction.Value;
                }
                total.DeductionTotal += line.DeductionTotal;
                total.Net += line.Net;
                total.Energy += line.Energy;
                total.Zfa += line.Zfa;
                total.UnclassifiedCount += line.UnclassifiedCount;
            }
            return total;
        }
    }
}
