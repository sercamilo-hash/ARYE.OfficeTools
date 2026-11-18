using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Model
{
    /// <summary>
    /// Everything the Step 4 preview panel shows for one package × building —
    /// plain data computed in a read-only pass, so the UI never holds Revit
    /// objects. A blocked preview carries the reason; nothing is guessed.
    /// </summary>
    public sealed class SheetPackagePreview
    {
        /// <summary>Non-null when the run cannot proceed (missing scheme, duplicate package, footprint too big…).</summary>
        public string BlockReason { get; set; }

        public bool IsBlocked => !string.IsNullOrEmpty(BlockReason);

        /// <summary>The one scale of the run (recommended or override). 0 when blocked before scale.</summary>
        public int ScaleDenominator { get; set; }

        /// <summary>True when the package's manual override decided the scale.</summary>
        public bool ScaleIsOverride { get; set; }

        /// <summary>True when the set's view template controls View Scale, so the template's own scale decided it.</summary>
        public bool ScaleIsFromTemplate { get; set; }

        /// <summary>Total sheets the package will own for this building after the run.</summary>
        public int SheetCount { get; set; }

        /// <summary>Total views the package will own for this building after the run.</summary>
        public int ViewCount { get; set; }

        public int NewViewCount { get; set; }

        public int UpdateViewCount { get; set; }

        public int KeptViewCount { get; set; }

        public int NewSheetCount { get; set; }

        /// <summary>Additional existing views this run will place.</summary>
        public int AdditionalToPlaceCount { get; set; }

        /// <summary>Additional views already sitting on one of the package's sheets.</summary>
        public int AdditionalPlacedCount { get; set; }

        /// <summary>Sheet collection the sheets will belong to ("" = none) and whether the run creates it.</summary>
        public string SheetCollectionName { get; set; } = string.Empty;

        public bool SheetCollectionWillBeCreated { get; set; }

        /// <summary>One row per view, NEW / UPDATE / KEPT.</summary>
        public List<SheetPreviewViewRow> ViewRows { get; set; } = new List<SheetPreviewViewRow>();

        /// <summary>
        /// The verification list: every view that will be on this set's sheets after the run, and every view that is
        /// on them without being expected (earlier views of covered levels, views placed by hand).
        /// </summary>
        public List<SheetContentRow> ContentRows { get; set; } = new List<SheetContentRow>();

        public int ContentExpectedCount { get; set; }

        public int ContentUnexpectedCount { get; set; }

        /// <summary>One row per area table the set would make or update.</summary>
        public List<SheetTableRow> TableRows { get; set; } = new List<SheetTableRow>();

        public int TableNewCount { get; set; }

        public int TableUpdateCount { get; set; }

        public int TableSkippedCount { get; set; }

        /// <summary>One mini-sheet drawing per sheet the run touches or creates.</summary>
        public List<SheetPreviewSheet> Sheets { get; set; } = new List<SheetPreviewSheet>();

        /// <summary>Everything worth telling that is not a block: skipped numbers, kept orphans, predicted name collisions.</summary>
        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>One area table of the preview: its name, what happens to it, the sheet it goes on and its size.</summary>
    public sealed class SheetTableRow
    {
        public const string StatusNew = "NEW";
        public const string StatusUpdate = "UPDATE";
        public const string StatusSkipped = "SKIPPED";

        public string TableName { get; set; }

        public string Status { get; set; }

        public string SheetNumber { get; set; }

        public string Detail { get; set; }
    }

    /// <summary>One line of the sheet-contents check: a view, the sheet it is (or will be) on, and whether it belongs there.</summary>
    public sealed class SheetContentRow
    {
        public string SheetNumber { get; set; }

        public string ViewName { get; set; }

        /// <summary>True when the view is in the expected list; false when it sits on a sheet of this set without being expected.</summary>
        public bool IsExpected { get; set; }

        public string Detail { get; set; }
    }

    /// <summary>One view line of the preview: "ZFA SHEET - … - L02" + its diff status.</summary>
    public sealed class SheetPreviewViewRow
    {
        public const string StatusNew = "NEW";
        public const string StatusUpdate = "UPDATE";
        public const string StatusKept = "KEPT";

        public string ViewName { get; set; }

        public string LevelName { get; set; }

        public string Status { get; set; }

        /// <summary>Optional explanation overriding the default text for the status (used by additional views).</summary>
        public string Detail { get; set; }

        /// <summary>True for an existing view placed as-is (not generated from a level).</summary>
        public bool IsAdditional { get; set; }
    }

    /// <summary>One mini-sheet of the preview: proportions, slot labels, status.</summary>
    public sealed class SheetPreviewSheet
    {
        public string SheetNumber { get; set; }

        public string SheetName { get; set; }

        /// <summary>"new" / "existing — kept" / "N view(s) added".</summary>
        public string StatusCaption { get; set; }

        public bool IsNew { get; set; }

        /// <summary>Titleblock extents in inches — the mini drawing keeps these proportions.</summary>
        public double SheetWidthIn { get; set; }

        public double SheetHeightIn { get; set; }

        /// <summary>Right-hand title strip width in inches.</summary>
        public double TitleStripWidthIn { get; set; }

        public int Columns { get; set; }

        public int Rows { get; set; }

        /// <summary>Columns × Rows labels in reading order: a level name or "empty".</summary>
        public List<string> SlotLabels { get; set; } = new List<string>();
    }
}
