#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Core.LightVent;
using ARYE.OfficeTools.Services;

namespace ARYE.OfficeTools.Models
{
    /// <summary>
    /// A parameter found at runtime on the windows (source) or on the rooms (target). Every
    /// parameter is listed; the ones that cannot hold an area are listed disabled, with the reason.
    /// Nothing is hardcoded: every option comes from the elements of the open model.
    /// </summary>
    public sealed class LightVentParamOption
    {
        public string Name { get; set; }

        /// <summary>True when the parameter lives on the element type (window type).</summary>
        public bool IsType { get; set; }

        public StorageType Storage { get; set; }

        public ForgeTypeId Spec { get; set; }

        public string SpecLabel { get; set; }

        public bool IsShared { get; set; }

        public Guid SharedGuid { get; set; }

        public BuiltInParameter BuiltIn { get; set; } = BuiltInParameter.INVALID;

        public bool IsBuiltIn => BuiltIn != BuiltInParameter.INVALID;

        /// <summary>Source: families that carry it. Target: rooms where it is writable.</summary>
        public int AvailableOn { get; set; }

        public int TotalCount { get; set; }

        /// <summary>"families" or "rooms" — used in the selector text.</summary>
        public string CountNoun { get; set; }

        /// <summary>Why the option is listed disabled; empty when it can be chosen.</summary>
        public string BlockedReason { get; set; } = string.Empty;

        public bool IsUsable => BlockedReason.Length == 0;

        /// <summary>Double with the Area spec: stored in ft² (internal units).</summary>
        public bool IsArea => Storage == StorageType.Double && RevitSpecHelper.Is(Spec, SpecTypeId.Area);

        public bool IsText => Storage == StorageType.String;

        public bool IsInteger => Storage == StorageType.Integer;

        public string Key => IsShared ? "G:" + SharedGuid
            : IsBuiltIn ? "B:" + (int)BuiltIn
            : "N:" + (IsType ? "Type" : "Instance") + "|" + Name;

        public string Display
        {
            get
            {
                string text = string.Format("{0}  —  {1} · {2}{3} · {4} of {5} {6}",
                    Name, IsType ? "Type" : "Instance", SpecLabel, IsBuiltIn ? " · built-in" : string.Empty,
                    AvailableOn, TotalCount, CountNoun);
                return IsUsable ? text : text + "  ·  " + BlockedReason;
            }
        }

        public override string ToString() => Display;
    }

    /// <summary>One window of the run (host model or a linked model), with what was decided for it.</summary>
    public sealed class LightVentWindowRow
    {
        public Element Element { get; set; }

        /// <summary>Id inside its own document (the link document for linked windows).</summary>
        public ElementId Id { get; set; }

        public string LinkName { get; set; }

        public bool IsLinked => !string.IsNullOrEmpty(LinkName);

        public string Mark { get; set; }

        public string FamilyName { get; set; }

        public string TypeName { get; set; }

        public WindowSides Sides { get; set; }

        public WindowResolution Resolution { get; set; }

        public bool VisibleInView { get; set; }

        /// <summary>Center in host-model coordinates (feet).</summary>
        public XYZ Center { get; set; }

        /// <summary>Unit vector along the wall, host-model coordinates.</summary>
        public XYZ Along { get; set; }

        public double Width { get; set; }

        public double? Light { get; set; }

        public double? Vent { get; set; }

        /// <summary>Why a value is empty ("empty", "no 'Vent Area' parameter").</summary>
        public string LightNote { get; set; }

        public string VentNote { get; set; }

        public WindowOutcome Outcome => Resolution.Outcome;

        public bool IsCounted => Outcome == WindowOutcome.Counted;

        /// <summary>Counted, but both values are empty: listed as skipped.</summary>
        public bool IsEmptySkip => IsCounted && Light == null && Vent == null;

        public string Key => (IsLinked ? "L:" + LinkName + ":" : "H:") + Id.Value;

        // ── text for the grids ───────────────────────────────────────────
        public string MarkText => string.IsNullOrWhiteSpace(Mark) ? "(no mark)" : Mark;

        public string TypeText => FamilyName + " : " + TypeName;

        public string ModelText => IsLinked ? "Link: " + LinkName : "This model";

        public string IdText => Id.Value.ToString();

        public string RoomText { get; set; }

        public string ReasonText
        {
            get
            {
                if (IsEmptySkip) return "empty light and vent value (" + LightNote + "; " + VentNote + ")";
                if (Outcome == WindowOutcome.Counted)
                {
                    string how = "room found by " + WindowRoomResolver.Describe(Resolution.FoundBy);
                    if (Light == null) return how + " · light skipped: " + LightNote;
                    if (Vent == null) return how + " · vent skipped: " + VentNote;
                    return how;
                }
                return Resolution.Reason;
            }
        }
    }

    /// <summary>One room of the run: geometry for the plan, current and proposed values.</summary>
    public sealed class LightVentRoomRow
    {
        public ElementId Id { get; set; }

        public string Number { get; set; }

        public string Name { get; set; }

        public List<List<XYZ>> Loops { get; set; } = new List<List<XYZ>>();

        public XYZ LabelPoint { get; set; }

        public double? CurrentLight { get; set; }

        public double? CurrentVent { get; set; }

        public bool CanWriteLight { get; set; }

        public bool CanWriteVent { get; set; }

        public RoomTotals Totals { get; set; }

        public List<LightVentWindowRow> Windows { get; set; } = new List<LightVentWindowRow>();

        // Display text, filled by the service in project units.
        public string RoomText => (Number + " " + Name).Trim();

        public string WindowsText { get; set; }

        public string LightText { get; set; }

        public string VentText { get; set; }

        public string StatusText { get; set; }

        public string NoteText { get; set; }

        public string DetailsText { get; set; }

        public bool WillChange => Totals != null && Totals.Status == RoomLightVentStatus.WillChange;
    }

    /// <summary>Everything the window shows, recomputed whenever a selector changes.</summary>
    public sealed class LightVentPreview
    {
        public List<LightVentRoomRow> Rooms { get; set; } = new List<LightVentRoomRow>();

        /// <summary>Windows not added to any room, with their reason.</summary>
        public List<LightVentWindowRow> Skipped { get; set; } = new List<LightVentWindowRow>();

        public int WillChange => Rooms.Count(r => r.Totals.Status == RoomLightVentStatus.WillChange);

        public int UpToDate => Rooms.Count(r => r.Totals.Status == RoomLightVentStatus.UpToDate);

        public int LeftUnchanged => Rooms.Count(r =>
            r.Totals.Status == RoomLightVentStatus.NoExteriorWindows || r.Totals.Status == RoomLightVentStatus.NoValues);

        public int CannotWrite => Rooms.Count(r => r.Totals.Status == RoomLightVentStatus.CannotWrite);

        /// <summary>Families of counted windows that lack the chosen light / vent parameter.</summary>
        public List<string> FamiliesMissingLight { get; set; } = new List<string>();

        public List<string> FamiliesMissingVent { get; set; } = new List<string>();
    }
}
