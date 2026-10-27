using System;
using System.Linq;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Core.Interop;

namespace ARYE.OfficeTools.Services.Interop
{
    /// <summary>
    /// The four BauTools line styles (subcategories of Lines). Created on demand
    /// inside the caller's transaction, never deleted or restyled if the user
    /// already customized them — their NAME is the contract, not their look.
    /// Revit has no API to create native Property Lines (verified against
    /// RevitAPI.dll 2026), so a named line style on Model Lines is the honest
    /// substitute.
    /// </summary>
    internal static class LotLineStyleService
    {
        public static GraphicsStyle PropertyLine(Document doc) =>
            GetOrCreate(doc, LotInteropContract.LineStylePropertyLine, new Color(200, 16, 16), 6);

        public static GraphicsStyle AdjacentLot(Document doc) =>
            GetOrCreate(doc, LotInteropContract.LineStyleAdjacentLot, new Color(90, 90, 90), 2);

        public static GraphicsStyle Block(Document doc) =>
            GetOrCreate(doc, LotInteropContract.LineStyleBlock, new Color(140, 140, 140), 1);

        public static GraphicsStyle Sidewalk(Document doc) =>
            GetOrCreate(doc, LotInteropContract.LineStyleSidewalk, new Color(16, 140, 60), 2);

        public static GraphicsStyle OtherBlock(Document doc) =>
            GetOrCreate(doc, LotInteropContract.LineStyleOtherBlock, new Color(124, 58, 237), 1);

        public static GraphicsStyle Roadbed(Document doc) =>
            GetOrCreate(doc, LotInteropContract.LineStyleRoadbed, new Color(60, 60, 60), 1);

        public static GraphicsStyle RealSidewalk(Document doc) =>
            GetOrCreate(doc, LotInteropContract.LineStyleRealSidewalk, new Color(180, 180, 180), 1);

        public static GraphicsStyle Curb(Document doc) =>
            GetOrCreate(doc, LotInteropContract.LineStyleCurb, new Color(120, 100, 80), 1);

        /// <summary>Applies a style to a curve element. Returns null on success, else why not.</summary>
        public static string Apply(CurveElement curve, GraphicsStyle style)
        {
            if (curve == null || style == null)
            {
                return "no curve or style";
            }
            try
            {
                curve.LineStyle = style;
                return null;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                return ex.Message;
            }
        }

        private static GraphicsStyle GetOrCreate(Document doc, string name, Color color, int weight)
        {
            Category lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category sub = lines.SubCategories
                .Cast<Category>()
                .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
            if (sub == null)
            {
                sub = doc.Settings.Categories.NewSubcategory(lines, name);
                sub.LineColor = color;
                sub.SetLineWeight(weight, GraphicsStyleType.Projection);
            }
            return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
        }
    }
}
