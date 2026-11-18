using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ARYE.OfficeTools.Services
{
    /// <summary>One reference line inside the scope box preview, in feet, relative to the scope box's own min corner.</summary>
    public sealed class PreviewLine
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
    }

    public sealed class ScopeBoxPreviewData
    {
        public double WidthFt { get; set; }
        public double HeightFt { get; set; }
        public List<PreviewLine> Lines { get; set; } = new List<PreviewLine>();

        /// <summary>Set when the preview is incomplete or unavailable — shown to the user, never hidden.</summary>
        public string Note { get; set; }
    }

    /// <summary>
    /// An honestly-incomplete sanity check for the scope box a Sheet
    /// Diagrammer set will crop its created views to: the crop rectangle at
    /// scale plus whatever walls happen to fall inside it. Not a rendered
    /// view — just enough for the user to see "yes, that's the right area"
    /// before generating. Read-only; no transaction needed.
    /// </summary>
    public static class ScopeBoxPreviewService
    {
        /// <summary>Never inspects more than this many walls — a huge model must not stall the live preview.</summary>
        private const int MaxWalls = 400;

        public static ScopeBoxPreviewData Build(Document doc, string scopeBoxUniqueId)
        {
            var data = new ScopeBoxPreviewData();
            if (string.IsNullOrEmpty(scopeBoxUniqueId))
            {
                data.Note = "No scope box — neither this set nor the building has one.";
                return data;
            }
            Element scopeBox = doc.GetElement(scopeBoxUniqueId);
            if (scopeBox == null)
            {
                data.Note = "The stored scope box no longer exists in this model.";
                return data;
            }
            BoundingBoxXYZ bbox = scopeBox.get_BoundingBox(null);
            if (bbox == null)
            {
                data.Note = "The scope box has no readable extent.";
                return data;
            }

            double minX = bbox.Min.X, minY = bbox.Min.Y, maxX = bbox.Max.X, maxY = bbox.Max.Y;
            data.WidthFt = maxX - minX;
            data.HeightFt = maxY - minY;
            if (data.WidthFt <= 0 || data.HeightFt <= 0)
            {
                data.Note = "The scope box has zero plan extent.";
                data.WidthFt = 0;
                data.HeightFt = 0;
                return data;
            }

            var outline = new Outline(
                new XYZ(minX, minY, bbox.Min.Z - 1),
                new XYZ(maxX, maxY, bbox.Max.Z + 1));

            List<Wall> walls;
            try
            {
                walls = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .WherePasses(new BoundingBoxIntersectsFilter(outline))
                    .Cast<Wall>()
                    .Take(MaxWalls)
                    .ToList();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                data.Note = "Reference walls could not be read — showing the crop outline only.";
                return data;
            }

            foreach (Wall wall in walls)
            {
                if (wall.Location is LocationCurve lc && lc.Curve is Line line)
                {
                    XYZ p0 = line.GetEndPoint(0);
                    XYZ p1 = line.GetEndPoint(1);
                    data.Lines.Add(new PreviewLine
                    {
                        X1 = p0.X - minX, Y1 = p0.Y - minY,
                        X2 = p1.X - minX, Y2 = p1.Y - minY
                    });
                }
                // Curved walls are skipped — a straight-segment reference is enough
                // for a sanity check, and this never blocks the preview.
            }

            if (data.Lines.Count == 0)
            {
                data.Note = walls.Count == 0
                    ? "No walls inside this area — showing the crop outline only."
                    : "Walls here are curved — showing the crop outline only.";
            }
            else if (walls.Count >= MaxWalls)
            {
                data.Note = $"Showing the first {MaxWalls} walls found — the crop outline itself is exact.";
            }
            return data;
        }
    }
}
