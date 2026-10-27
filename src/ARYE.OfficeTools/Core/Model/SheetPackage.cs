using System;
using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Model
{
    /// <summary>Where a sheet package takes its source views from.</summary>
    public enum SheetSourceKind
    {
        /// <summary>Non-template FloorPlan views, first per level, bottom-up.</summary>
        FloorPlans = 0,

        /// <summary>Area plans of the package's area scheme, one per level, bottom-up.</summary>
        AreaPlans = 1,

        /// <summary>Non-template CeilingPlan views, first per level, bottom-up.</summary>
        CeilingPlans = 2
    }

    /// <summary>
    /// A text parameter value to write on every element the package creates or
    /// updates (views or sheets) — typically the parameters the project's
    /// browser organization groups by ("View Group", "Sub-Discipline"…). The
    /// name is whatever the template calls it; nothing is hardcoded.
    /// </summary>
    public sealed class ParameterValueAssignment
    {
        public string ParameterName { get; set; }

        /// <summary>Empty = leave the parameter alone.</summary>
        public string Value { get; set; }
    }

    /// <summary>
    /// An existing view (section, 3D, legend, schedule…) the package also
    /// places on its own sheets. The view is never duplicated or restyled —
    /// it is placed as it is.
    /// </summary>
    public sealed class AdditionalViewRef
    {
        public string ViewUniqueId { get; set; }

        /// <summary>Display cache; the live name is re-read from the model.</summary>
        public string ViewName { get; set; }

        public string ViewTypeName { get; set; }

        /// <summary>True = a dedicated sheet, 1×1. False = takes the next free slot of the matrix.</summary>
        public bool OwnSheet { get; set; } = true;
    }

    /// <summary>
    /// One reusable sheet series definition (a "set"): what to draw (source),
    /// how to number it, which titleblock frames it, how the slot matrix
    /// splits each sheet, which sheet collection and browser-organization
    /// values the output carries, and which extra existing views ride along.
    /// Persisted in ZfaProjectModel.SheetPackages; a run is always package ×
    /// building. Fields added after the first release default to empty so old
    /// payloads load unchanged (SchemaVersion stays 1).
    /// </summary>
    public sealed class SheetPackage
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Display name, e.g. "Gross". Also the base of created sheet names.</summary>
        public string Name { get; set; }

        /// <summary>Sheet number prefix, e.g. "Z-".</summary>
        public string NumberPrefix { get; set; } = "Z-";

        /// <summary>First number of the series, e.g. 100 → Z-100.</summary>
        public int StartNumber { get; set; } = 100;

        /// <summary>Step between consecutive sheet numbers.</summary>
        public int NumberIncrement { get; set; } = 1;

        /// <summary>UniqueId of the titleblock type framing every sheet of the package.</summary>
        public string TitleBlockTypeUniqueId { get; set; }

        public SheetSourceKind SourceKind { get; set; } = SheetSourceKind.AreaPlans;

        /// <summary>UniqueId of the AreaScheme read when SourceKind is AreaPlans. Unused for FloorPlans.</summary>
        public string AreaSchemeUniqueId { get; set; }

        /// <summary>UniqueId of the view template applied to every created/updated view. Null/empty = none.</summary>
        public string ViewTemplateUniqueId { get; set; }

        /// <summary>
        /// Text after the level in every view this set creates ("GROSS AREA", "DEDUCTION PLAN"). Empty = just the
        /// level label, e.g. "1ST FL.".
        /// </summary>
        public string ViewNameSuffix { get; set; } = string.Empty;

        /// <summary>Slot matrix columns (1–4).</summary>
        public int Columns { get; set; } = 3;

        /// <summary>Slot matrix rows (1–4).</summary>
        public int Rows { get; set; } = 2;

        /// <summary>
        /// Dragged column sizes: cumulative shares (0–1) of the matrix width left after the gutters, one per
        /// separator. Empty = equal columns (the default and the behaviour before this existed).
        /// </summary>
        public List<double> ColumnCumulative { get; set; } = new List<double>();

        /// <summary>Dragged row sizes, same convention as <see cref="ColumnCumulative"/>, measured from the top.</summary>
        public List<double> RowCumulative { get; set; } = new List<double>();

        /// <summary>When true, a run draws the matrix separators on its sheets as Detail Lines. Off = guides in the window only.</summary>
        public bool DrawSeparatorLines { get; set; }

        /// <summary>Name of the Revit line style used for the separators. Empty = Thin Lines.</summary>
        public string SeparatorLineStyle { get; set; } = string.Empty;

        /// <summary>
        /// Legacy: width of the titleblock's right-hand title strip, in inches.
        /// Superseded by the four margins; still read so old sets keep their
        /// usable area (right margin = 0.25 + strip when MarginRightIn is unset).
        /// </summary>
        public double TitleStripWidthIn { get; set; } = 2.0;

        /// <summary>Usable-area margins from the titleblock extents, inches. Negative = not set yet (use the legacy defaults).</summary>
        public double MarginLeftIn { get; set; } = -1;

        public double MarginRightIn { get; set; } = -1;

        public double MarginTopIn { get; set; } = -1;

        public double MarginBottomIn { get; set; } = -1;

        /// <summary>The margin the generator used before margins existed.</summary>
        public const double LegacyMarginIn = 0.25;

        public double EffectiveMarginLeftIn => MarginLeftIn >= 0 ? MarginLeftIn : LegacyMarginIn;

        public double EffectiveMarginRightIn => MarginRightIn >= 0 ? MarginRightIn : LegacyMarginIn + Math.Max(0, TitleStripWidthIn);

        public double EffectiveMarginTopIn => MarginTopIn >= 0 ? MarginTopIn : LegacyMarginIn;

        public double EffectiveMarginBottomIn => MarginBottomIn >= 0 ? MarginBottomIn : LegacyMarginIn;

        /// <summary>0 = recommend automatically via SmartScaleAdvisor.</summary>
        public int ScaleDenominatorOverride { get; set; }

        /// <summary>
        /// UniqueId of the scope box cropping every view this set CREATES.
        /// Null/empty = the building's scope box from Zoning Floor Area › Step 1.
        /// Applied at creation only (decision 2026-09-17).
        /// </summary>
        public string ScopeBoxUniqueId { get; set; }

        /// <summary>
        /// When true, every run also re-applies the scope box to the views the
        /// set already owns (default false: creation only, as decided 2026-09-17).
        /// </summary>
        public bool ReapplyScopeBoxOnUpdate { get; set; }

        /// <summary>
        /// When true (default — the tool's original, unconditional behavior),
        /// this set draws only the levels covered by the building's Floor
        /// Ranges in Zoning Floor Area › Step 1: Single, Typical and Duplex
        /// all included. When false, every matching source view in the model
        /// draws, whether or not its level has a Floor Range assignment.
        /// True on an old payload missing this field, so nothing already
        /// generated changes behavior.
        /// </summary>
        public bool RestrictToFloorRangeLevels { get; set; } = true;

        /// <summary>
        /// Legacy, no longer read. It was a separate "one view per range" switch; that behavior is now
        /// what <see cref="RestrictToFloorRangeLevels"/> means. Kept only so older saved sets still load.
        /// </summary>
        public bool DiagramSourceLevelsOnly { get; set; } // Legacy and unused: the restriction above now implies one view per range.

        /// <summary>
        /// Name of the Sheet Collection every sheet of the package belongs to.
        /// Created on first run if it does not exist. Null/empty = none.
        /// </summary>
        public string SheetCollectionName { get; set; }

        /// <summary>Written on every sheet the package creates or already owns.</summary>
        public List<ParameterValueAssignment> SheetParameterValues { get; set; } = new List<ParameterValueAssignment>();

        /// <summary>Written on every view the package creates or updates.</summary>
        public List<ParameterValueAssignment> ViewParameterValues { get; set; } = new List<ParameterValueAssignment>();

        /// <summary>Existing views placed on the package's sheets as they are.</summary>
        public List<AdditionalViewRef> AdditionalViews { get; set; } = new List<AdditionalViewRef>();

        /// <summary>Deep copy with a NEW identity — the copy owns no sheets until it is generated.</summary>
        public SheetPackage CloneAsNew(string newName)
        {
            var copy = new SheetPackage
            {
                Name = newName,
                NumberPrefix = NumberPrefix,
                StartNumber = StartNumber,
                NumberIncrement = NumberIncrement,
                TitleBlockTypeUniqueId = TitleBlockTypeUniqueId,
                SourceKind = SourceKind,
                AreaSchemeUniqueId = AreaSchemeUniqueId,
                ViewTemplateUniqueId = ViewTemplateUniqueId,
                ViewNameSuffix = ViewNameSuffix,
                Columns = Columns,
                Rows = Rows,
                ColumnCumulative = new List<double>(ColumnCumulative ?? new List<double>()),
                RowCumulative = new List<double>(RowCumulative ?? new List<double>()),
                DrawSeparatorLines = DrawSeparatorLines,
                SeparatorLineStyle = SeparatorLineStyle,
                TitleStripWidthIn = TitleStripWidthIn,
                MarginLeftIn = MarginLeftIn,
                MarginRightIn = MarginRightIn,
                MarginTopIn = MarginTopIn,
                MarginBottomIn = MarginBottomIn,
                ScaleDenominatorOverride = ScaleDenominatorOverride,
                SheetCollectionName = SheetCollectionName,
                ScopeBoxUniqueId = ScopeBoxUniqueId,
                ReapplyScopeBoxOnUpdate = ReapplyScopeBoxOnUpdate,
                RestrictToFloorRangeLevels = RestrictToFloorRangeLevels,
                DiagramSourceLevelsOnly = DiagramSourceLevelsOnly
            };
            foreach (ParameterValueAssignment p in SheetParameterValues ?? new List<ParameterValueAssignment>())
            {
                copy.SheetParameterValues.Add(new ParameterValueAssignment { ParameterName = p.ParameterName, Value = p.Value });
            }
            foreach (ParameterValueAssignment p in ViewParameterValues ?? new List<ParameterValueAssignment>())
            {
                copy.ViewParameterValues.Add(new ParameterValueAssignment { ParameterName = p.ParameterName, Value = p.Value });
            }
            foreach (AdditionalViewRef v in AdditionalViews ?? new List<AdditionalViewRef>())
            {
                copy.AdditionalViews.Add(new AdditionalViewRef
                {
                    ViewUniqueId = v.ViewUniqueId, ViewName = v.ViewName, ViewTypeName = v.ViewTypeName, OwnSheet = v.OwnSheet
                });
            }
            return copy;
        }

        /// <summary>
        /// The office-standard seed: what a fresh project (or an old payload
        /// without stored packages) starts with. Scheme, titleblock and
        /// template are deliberately unset — the user picks them.
        /// </summary>
        public static List<SheetPackage> DefaultSeed() => new List<SheetPackage>
        {
            new SheetPackage
            {
                Name = "Architectural Floor Plans",
                SourceKind = SheetSourceKind.FloorPlans,
                NumberPrefix = "A-",
                StartNumber = 100
            },
            new SheetPackage
            {
                Name = "Gross",
                SourceKind = SheetSourceKind.AreaPlans,
                NumberPrefix = "Z-",
                StartNumber = 100
            },
            new SheetPackage
            {
                Name = "Deductions",
                SourceKind = SheetSourceKind.AreaPlans,
                NumberPrefix = "ZD-",
                StartNumber = 100
            },
            new SheetPackage
            {
                Name = "Life Safety",
                SourceKind = SheetSourceKind.AreaPlans,
                NumberPrefix = "LS-",
                StartNumber = 100
            }
        };
    }

    /// <summary>What the last successful generation of one (package, building) did.</summary>
    public sealed class SheetPackageRunRecord
    {
        public Guid PackageId { get; set; }

        public Guid BuildingId { get; set; }

        public DateTime LastRunUtc { get; set; }

        /// <summary>Windows username of whoever ran it.</summary>
        public string User { get; set; }

        /// <summary>Total sheets the package owns for the building after the run.</summary>
        public int SheetCount { get; set; }

        /// <summary>Total views the package owns for the building after the run.</summary>
        public int ViewCount { get; set; }
    }
}
