#nullable disable

namespace ARYE.OfficeTools.Models
{
    /// <summary>
    /// One entry of the shared parameter picker (Views/Controls/ParameterPicker). Each tool maps
    /// its own option type to this and keeps the original in <see cref="Tag"/>.
    /// </summary>
    public sealed class ParameterPickerItem
    {
        /// <summary>Stable identity, used to keep and restore a selection.</summary>
        public string Key { get; set; }

        public string Name { get; set; }

        /// <summary>Revit parameter group as the Properties palette shows it ("Identity Data", "Data"...).</summary>
        public string Group { get; set; }

        /// <summary>Data type label: "Area", "Number", "Text", "Integer", or anything else ("Length", "Yes/No").</summary>
        public string DataType { get; set; }

        public bool IsType { get; set; }

        public bool IsShared { get; set; }

        /// <summary>Second line under the name when the item can be chosen ("6 of 7 families").</summary>
        public string Availability { get; set; }

        /// <summary>Why the item cannot be chosen; null or empty when it can.</summary>
        public string BlockedReason { get; set; }

        /// <summary>The tool's own option object.</summary>
        public object Tag { get; set; }

        /// <summary>The four data types the picker filters and colors; everything else is "Other".</summary>
        public string Kind
        {
            get
            {
                switch (DataType)
                {
                    case "Area":
                    case "Number":
                    case "Text":
                    case "Integer":
                        return DataType;
                    default:
                        return "Other";
                }
            }
        }
    }
}
