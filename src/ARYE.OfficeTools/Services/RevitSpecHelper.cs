#nullable disable
using System;
using Autodesk.Revit.DB;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// Null-safe comparison of ForgeTypeId values.
    /// Comparing ForgeTypeId with == against a possibly null / empty value is a known trap
    /// (Definition.GetDataType() can return an empty id), so every comparison goes through here.
    /// </summary>
    public static class RevitSpecHelper
    {
        public static string SafeTypeId(ForgeTypeId id)
        {
            if (ReferenceEquals(id, null)) return string.Empty;
            try { return id.TypeId ?? string.Empty; }
            catch { return string.Empty; }
        }

        public static bool Is(ForgeTypeId a, ForgeTypeId b)
        {
            string sa = SafeTypeId(a);
            string sb = SafeTypeId(b);
            if (sa.Length == 0 || sb.Length == 0) return false;
            return string.Equals(sa, sb, StringComparison.Ordinal);
        }

        public static bool IsEmpty(ForgeTypeId a)
        {
            return SafeTypeId(a).Length == 0;
        }

        /// <summary>Short human label for the selector ("Area", "Number", "Text", "Length"...).</summary>
        public static string SpecLabel(ForgeTypeId spec, StorageType storage)
        {
            if (Is(spec, SpecTypeId.Area)) return "Area";
            if (Is(spec, SpecTypeId.Number)) return "Number";
            if (Is(spec, SpecTypeId.Length)) return "Length";
            if (Is(spec, SpecTypeId.Volume)) return "Volume";
            if (Is(spec, SpecTypeId.String.Text)) return "Text";
            if (Is(spec, SpecTypeId.String.MultilineText)) return "Multiline text";
            if (Is(spec, SpecTypeId.Boolean.YesNo)) return "Yes/No";
            if (Is(spec, SpecTypeId.Int.Integer)) return "Integer";

            if (!IsEmpty(spec))
            {
                try
                {
                    string label = LabelUtils.GetLabelForSpec(spec);
                    if (!string.IsNullOrWhiteSpace(label)) return label;
                }
                catch { }
            }

            switch (storage)
            {
                case StorageType.Double: return "Number (unitless?)";
                case StorageType.Integer: return "Integer";
                case StorageType.String: return "Text";
                case StorageType.ElementId: return "Element";
                default: return "Unknown";
            }
        }

        /// <summary>The area unit the project currently displays (ft2, m2, ...).</summary>
        public static ForgeTypeId ProjectAreaUnit(Document doc)
        {
            try
            {
                FormatOptions fo = doc.GetUnits().GetFormatOptions(SpecTypeId.Area);
                ForgeTypeId u = fo.GetUnitTypeId();
                if (!IsEmpty(u)) return u;
            }
            catch { }
            return UnitTypeId.SquareFeet;
        }

        public static string ProjectAreaUnitSuffix(Document doc)
        {
            ForgeTypeId u = ProjectAreaUnit(doc);
            if (Is(u, UnitTypeId.SquareMeters)) return "m²";
            if (Is(u, UnitTypeId.SquareCentimeters)) return "cm²";
            if (Is(u, UnitTypeId.SquareMillimeters)) return "mm²";
            if (Is(u, UnitTypeId.SquareInches)) return "in²";
            return "SF";
        }

        /// <summary>Formats an internal (ft2) area the way Revit itself would, in project units.</summary>
        public static string FormatArea(Document doc, double areaFeet2)
        {
            try
            {
                return UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Area, areaFeet2, false);
            }
            catch
            {
                return string.Format("{0:N2} ft²", areaFeet2);
            }
        }

        public static double ToProjectAreaUnits(Document doc, double areaFeet2)
        {
            try
            {
                return UnitUtils.ConvertFromInternalUnits(areaFeet2, ProjectAreaUnit(doc));
            }
            catch
            {
                return areaFeet2;
            }
        }

        public static double FromProjectAreaUnits(Document doc, double value)
        {
            try
            {
                return UnitUtils.ConvertToInternalUnits(value, ProjectAreaUnit(doc));
            }
            catch
            {
                return value;
            }
        }
    }
}
