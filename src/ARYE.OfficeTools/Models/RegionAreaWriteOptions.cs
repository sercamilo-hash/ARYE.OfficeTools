#nullable disable
using System;

namespace ARYE.OfficeTools.Models
{
    public enum OverwritePolicy
    {
        FillEmptyOnly,      // never touch a parameter that already has a value
        OverwriteDifferent, // write when empty or different (default)
        OverwriteAll        // write always, even when identical (forces a rewrite)
    }

    public enum TextValueMode
    {
        RevitFormatted,     // "1,234 SF" — exactly what Revit displays for that area
        NumberOnly,         // "1234.5"  — no unit suffix, no separators
        CustomFormatted     // decimals + thousands separator + suffix chosen by the user
    }

    /// <summary>How the computed area becomes the value written into the target parameter.</summary>
    public class RegionAreaWriteOptions
    {
        public OverwritePolicy Policy = OverwritePolicy.OverwriteDifferent;

        // ── Numeric targets (Area / Number / Integer) ──────────────────────
        /// <summary>
        /// Write the value rounded to the precision shown in the preview instead of the exact
        /// area. Off by default: "same values" means the exact value, so schedules that sum the
        /// parameter match schedules that sum the area.
        /// </summary>
        public bool RoundNumericToDisplay = false;
        public int NumericDecimals = 2;

        // ── Text targets ──────────────────────────────────────────────────
        public TextValueMode TextMode = TextValueMode.RevitFormatted;
        public int TextDecimals = 0;
        public bool TextThousandsSeparator = true;
        public string TextSuffix = "SF";
        public string TextPrefix = string.Empty;

        /// <summary>Tolerance (ft²) under which a stored value counts as identical to the area.</summary>
        public double EqualityToleranceFeet2 = 1e-6;
    }
}
