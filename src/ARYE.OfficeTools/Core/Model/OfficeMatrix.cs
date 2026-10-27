using System;
using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Model
{
    /// <summary>
    /// The office-format ZFA matrix: per building, one line per level with
    /// GROSS | COMMERCIAL GROSS | one column per deduction category | NET |
    /// ENERGY | ZFA, a TOTALS line per building and a grand-total line.
    /// Pure data produced by OfficeMatrixCalculator — the UI table, the CSV,
    /// the XLSX and the drafting view all render exactly this object.
    /// </summary>
    public sealed class OfficeMatrix
    {
        /// <summary>Deduction column headers — the catalog's Deduction-role names, in list order.</summary>
        public IReadOnlyList<string> DeductionColumns { get; set; } = new List<string>();

        public List<OfficeMatrixBuilding> Buildings { get; set; } = new List<OfficeMatrixBuilding>();

        /// <summary>Sum across all buildings' TOTALS lines. Null until at least one building exists.</summary>
        public OfficeMatrixLine GrandTotal { get; set; }

        public bool EnergyEnabled { get; set; }

        /// <summary>Energy deduction as a percentage of NET (e.g. 5 = 5%). Applied only when enabled.</summary>
        public double EnergyPercent { get; set; }

        public double LotAreaSqFt { get; set; }

        public double MaxFar { get; set; }

        /// <summary>LotAreaSqFt × MaxFar. Zero when the lot inputs are not set.</summary>
        public double AllowedZfaSqFt { get; set; }

        public double TotalZfaSqFt { get; set; }

        /// <summary>AllowedZfa − TotalZfa. Negative means over FAR.</summary>
        public double RemainingSqFt => AllowedZfaSqFt - TotalZfaSqFt;

        public bool IsOverFar => AllowedZfaSqFt > 0 && TotalZfaSqFt > AllowedZfaSqFt;

        /// <summary>ZFA ÷ lot area — the FAR actually consumed.</summary>
        public double FarUsed => LotAreaSqFt > 0 ? TotalZfaSqFt / LotAreaSqFt : 0;

        /// <summary>The totals are not final while any area remains unclassified.</summary>
        public int UnclassifiedAreaCount { get; set; }

        public double UnclassifiedSqFt { get; set; }

        /// <summary>Grand totals per deduction column — the right panel's category bars.</summary>
        public Dictionary<string, double> DeductionTotals { get; set; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class OfficeMatrixBuilding
    {
        public string BuildingName { get; set; }

        /// <summary>One line per level, bottom-up.</summary>
        public List<OfficeMatrixLine> Levels { get; set; } = new List<OfficeMatrixLine>();

        /// <summary>Per-column sums of this building's level lines.</summary>
        public OfficeMatrixLine Totals { get; set; }
    }

    /// <summary>
    /// One line of the matrix — a level, a building's TOTALS, or the grand
    /// total. Nullable gross columns distinguish "no area of that kind"
    /// (blank cell, like the office sheet) from a real zero.
    /// </summary>
    public sealed class OfficeMatrixLine
    {
        public string LevelName { get; set; }

        public int LevelIndex { get; set; }

        /// <summary>UniqueId of the level (level lines only) — links the row to its Step 1 range.</summary>
        public string LevelUniqueId { get; set; }

        /// <summary>Sum of MainGross-role areas (unclassified areas ride here, flagged). Null = blank.</summary>
        public double? MainGross { get; set; }

        /// <summary>Sum of CommercialGross-role areas — informational, never part of NET. Null = blank.</summary>
        public double? CommercialGross { get; set; }

        /// <summary>Deducted square feet by category name (only categories with a value appear).</summary>
        public Dictionary<string, double> Deductions { get; set; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public double DeductionTotal { get; set; }

        /// <summary>MainGross − Σ deductions.</summary>
        public double Net { get; set; }

        /// <summary>EnergyPercent × NET when the energy deduction is enabled, else 0.</summary>
        public double Energy { get; set; }

        /// <summary>NET − ENERGY — this line's contribution to the zoning floor area.</summary>
        public double Zfa { get; set; }

        /// <summary>Areas on this level still unclassified — the line's numbers are not final.</summary>
        public int UnclassifiedCount { get; set; }
    }
}
