#nullable disable
using System;
using System.ComponentModel;
using Autodesk.Revit.DB;
using ARYE.RegionArea.Services;

namespace ARYE.RegionArea.Models
{
    public enum MigrationStatus
    {
        New,                // target parameter is empty -> will be filled
        Overwrite,          // target parameter has a different value -> will be replaced
        NoChange,           // target parameter already holds the same value
        MissingParameter,   // the chosen parameter does not exist on this element
        ReadOnlyParameter,  // exists but cannot be written (workset not owned, pinned type, etc.)
        SpecMismatch,       // parameter data type cannot receive an area
        InvalidGeometry     // area could not be computed
    }

    /// <summary>
    /// One identity field (Building or Side) is stamped with a single value for the whole run,
    /// chosen once in the IDENTITY card — never per row. NotConfigured means the user left that
    /// selector on "do not change": the field is evaluated but never written for any row.
    /// </summary>
    public enum IdentityFieldStatus
    {
        NotConfigured,
        New,
        Overwrite,
        NoChange,
        MissingParameter
    }

    /// <summary>One row of the preview grid: one FilledRegion and what will happen to it.</summary>
    public class RegionAreaMigrationItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }

        // ── Identity ───────────────────────────────────────────────────────
        public ElementId Id { get; set; }
        public FilledRegion Region { get; set; }
        public string IdText { get; set; }
        public string TypeName { get; set; }
        public string ViewName { get; set; }
        public string ViewTypeName { get; set; }
        public bool IsMasking { get; set; }
        public bool IsPinned { get; set; }
        public bool IsGroupMember { get; set; }
        public ElementId OwnerViewId { get; set; }

        // ── Source value ───────────────────────────────────────────────────
        /// <summary>Computed net area, square feet (Revit internal units).</summary>
        public double AreaFeet2 { get; set; }
        public RegionAreaResult Geometry { get; set; }

        /// <summary>Area as Revit's own read-only parameter reports it (NaN when unavailable).</summary>
        public double RevitAreaFeet2 { get; set; } = double.NaN;

        public string AreaText { get; set; }        // computed, formatted in project units
        public string RevitAreaText { get; set; }   // Revit's parameter, formatted
        public string DeltaText { get; set; }       // difference between the two, or "—"
        public bool DeltaFlagged { get; set; }      // difference above tolerance -> needs a look

        // ── Target ─────────────────────────────────────────────────────────
        public string CurrentValue { get; set; }
        public string ProposedValue { get; set; }

        /// <summary>Value actually handed to Parameter.Set for Double/Integer targets.</summary>
        public double RawWriteValue { get; set; }
        public string TextWriteValue { get; set; }

        // ── Identity stamp (Building / Side, one value for the whole run) ───
        public string BuildingCurrent { get; set; } = "—";
        public string BuildingDisplay { get; set; } = "—";
        public IdentityFieldStatus BuildingStatus { get; set; } = IdentityFieldStatus.NotConfigured;

        public string SideCurrent { get; set; } = "—";
        public string SideDisplay { get; set; } = "—";
        public IdentityFieldStatus SideStatus { get; set; } = IdentityFieldStatus.NotConfigured;

        private MigrationStatus _status;
        public MigrationStatus Status
        {
            get { return _status; }
            set { _status = value; Raise("Status"); Raise("StatusText"); Raise("IsWritable"); }
        }

        public string StatusText
        {
            get
            {
                switch (_status)
                {
                    case MigrationStatus.New: return "NEW";
                    case MigrationStatus.Overwrite: return "OVERWRITE";
                    case MigrationStatus.NoChange: return "NO CHANGE";
                    case MigrationStatus.MissingParameter: return "NO PARAM";
                    case MigrationStatus.ReadOnlyParameter: return "READ-ONLY";
                    case MigrationStatus.SpecMismatch: return "TYPE MISMATCH";
                    case MigrationStatus.InvalidGeometry: return "BAD GEOMETRY";
                    default: return "—";
                }
            }
        }

        public bool IsWritable
        {
            get { return _status == MigrationStatus.New || _status == MigrationStatus.Overwrite; }
        }

        // ── Selection & notes ──────────────────────────────────────────────
        private bool _selected;
        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Raise("Selected"); }
        }

        private string _note = string.Empty;
        public string Note
        {
            get { return _note; }
            set { _note = value ?? string.Empty; Raise("Note"); }
        }

        /// <summary>Set by the write pass: empty when the row was written without incident.</summary>
        public string WriteError { get; set; } = string.Empty;
    }
}
