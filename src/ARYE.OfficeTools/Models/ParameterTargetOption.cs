#nullable disable
using System;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Services;

namespace ARYE.OfficeTools.Models
{
    public enum TargetBinding
    {
        Instance,   // lives on the FilledRegion element itself
        Type        // lives on the FilledRegionType -> writing it affects every instance of that type
    }

    public enum TargetOrigin
    {
        Shared,     // shared parameter (has a GUID, schedulable and taggable across projects)
        Project,    // project parameter
        BuiltIn     // Revit built-in parameter (Comments, Mark, ...)
    }

    /// <summary>
    /// One entry of the target-parameter selector. Everything shown in the combo box is
    /// discovered at runtime from the actual selected elements, so the list can never offer a
    /// parameter the elements do not have.
    /// </summary>
    public class ParameterTargetOption
    {
        public string Name { get; set; }
        public StorageType Storage { get; set; }

        /// <summary>Data type (SpecTypeId.Area, SpecTypeId.Number, SpecTypeId.String.Text, ...). May be null.</summary>
        public ForgeTypeId Spec { get; set; }
        public string SpecLabel { get; set; }

        public TargetBinding Binding { get; set; }
        public TargetOrigin Origin { get; set; }

        public bool IsShared { get; set; }
        public Guid SharedGuid { get; set; }
        public InternalDefinition InternalDef { get; set; }
        public BuiltInParameter BuiltIn { get; set; } = BuiltInParameter.INVALID;
        public string GroupLabel { get; set; } = string.Empty;

        /// <summary>How many of the selected elements actually expose this parameter.</summary>
        public int AvailableOn { get; set; }
        public int TotalCount { get; set; }

        /// <summary>Read-only on at least one of the selected elements.</summary>
        public bool ReadOnlySomewhere { get; set; }

        /// <summary>True when an area value can be written into this parameter at all.</summary>
        public bool CanReceiveArea
        {
            get
            {
                if (Storage == StorageType.String) return true;
                if (Storage == StorageType.Integer) return true;
                if (Storage == StorageType.Double)
                {
                    if (RevitSpecHelper.IsEmpty(Spec)) return true;
                    if (RevitSpecHelper.Is(Spec, SpecTypeId.Area)) return true;
                    if (RevitSpecHelper.Is(Spec, SpecTypeId.Number)) return true;
                    return false;                       // Length, Volume, Angle... refused on purpose
                }
                return false;
            }
        }

        public bool IsAreaSpec
        {
            get { return Storage == StorageType.Double && RevitSpecHelper.Is(Spec, SpecTypeId.Area); }
        }

        public bool IsNumberSpec
        {
            get { return Storage == StorageType.Double && !IsAreaSpec; }
        }

        public string OriginLabel
        {
            get
            {
                switch (Origin)
                {
                    case TargetOrigin.Shared: return "Shared";
                    case TargetOrigin.Project: return "Project";
                    default: return "Built-in";
                }
            }
        }

        public string BindingLabel
        {
            get { return Binding == TargetBinding.Instance ? "Instance" : "Type"; }
        }

        /// <summary>Text shown in the selector: name plus everything needed to pick the right one.</summary>
        public string Display
        {
            get
            {
                string avail = (AvailableOn == TotalCount)
                    ? string.Format("all {0}", TotalCount)
                    : string.Format("{0} of {1}", AvailableOn, TotalCount);

                string flags = string.Format("{0} · {1} · {2}", BindingLabel, OriginLabel, SpecLabel);
                if (!CanReceiveArea) flags += " · NOT COMPATIBLE";
                else if (Binding == TargetBinding.Type) flags += " · affects all instances of the type";
                if (ReadOnlySomewhere) flags += " · read-only on some";

                return string.Format("{0}   [{1}]   ({2})", Name, flags, avail);
            }
        }

        /// <summary>Stable key used to de-duplicate parameters across the selected elements.</summary>
        public string Key
        {
            get
            {
                if (IsShared) return "G:" + SharedGuid.ToString();
                if (BuiltIn != BuiltInParameter.INVALID) return "B:" + ((int)BuiltIn).ToString();
                return "N:" + Binding.ToString() + "|" + Name + "|" + Storage.ToString();
            }
        }

        public override string ToString() { return Display; }
    }
}
