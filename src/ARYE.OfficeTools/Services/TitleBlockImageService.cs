using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Core.Results;

namespace ARYE.OfficeTools.Services
{
    /// <summary>A straight, axis-aligned line of the titleblock family, in inches from the titleblock's lower-left corner (Y up).</summary>
    public sealed class TitleBlockLine
    {
        public double X1 { get; set; }

        public double Y1 { get; set; }

        public double X2 { get; set; }

        public double Y2 { get; set; }

        public bool IsVertical => Math.Abs(X1 - X2) < 1e-6;
    }

    /// <summary>Result of rendering one titleblock type to a PNG.</summary>
    internal sealed class TitleBlockImage
    {
        public string TitleBlockTypeUniqueId { get; set; }

        /// <summary>PNG on disk, or null when rendering failed (see FailureReason).</summary>
        public string ImagePath { get; set; }

        public double SheetWidthIn { get; set; }

        public double SheetHeightIn { get; set; }

        public string FailureReason { get; set; }

        /// <summary>The family's own straight horizontal/vertical lines — what the usable area can snap to.</summary>
        public List<TitleBlockLine> Lines { get; set; } = new List<TitleBlockLine>();

        /// <summary>Lines the family has that are neither horizontal nor vertical (not snap targets) or are curves.</summary>
        public int IgnoredCurveCount { get; set; }
    }

    /// <summary>
    /// Renders a titleblock type exactly as it prints: a temporary sheet is
    /// created, exported to PNG and the transaction is ROLLED BACK, so the
    /// model is never changed (no sheet, no number consumed, nothing to undo).
    /// Cached per type in %TEMP%\BauTools\titleblocks. The image is only a
    /// backdrop for placing the usable area; slot math stays in inches.
    /// </summary>
    internal static class TitleBlockImageService
    {
        private const int PixelWidth = 1800;

        private static readonly Dictionary<string, TitleBlockImage> Cache = new Dictionary<string, TitleBlockImage>(StringComparer.Ordinal);

        private const double MinLineLengthIn = 0.25;
        private const double AxisToleranceFt = 1e-4;

        /// <summary>
        /// Reads the titleblock family's own geometry as it shows on the sheet. Only straight horizontal and
        /// vertical lines become snap targets; everything else is counted, not silently ignored.
        /// </summary>
        private static void CollectLines(Element instance, ViewSheet sheet, BoundingBoxXYZ box, TitleBlockImage result)
        {
            try
            {
                GeometryElement geometry = instance.get_Geometry(new Options { View = sheet, ComputeReferences = false });
                if (geometry != null)
                {
                    Walk(geometry, box, result);
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // No readable geometry: the window shows the image alone and says so.
            }
        }

        private static void Walk(IEnumerable<GeometryObject> objects, BoundingBoxXYZ box, TitleBlockImage result)
        {
            foreach (GeometryObject geometryObject in objects)
            {
                if (geometryObject is GeometryInstance nested)
                {
                    Walk(nested.GetInstanceGeometry(), box, result);
                }
                else if (geometryObject is Line line)
                {
                    XYZ a = line.GetEndPoint(0), b = line.GetEndPoint(1);
                    bool vertical = Math.Abs(a.X - b.X) < AxisToleranceFt;
                    bool horizontal = Math.Abs(a.Y - b.Y) < AxisToleranceFt;
                    double lengthIn = UnitUtils.ConvertFromInternalUnits(line.Length, UnitTypeId.Inches);
                    if (lengthIn < MinLineLengthIn)
                    {
                        continue;
                    }
                    if (vertical == horizontal)
                    {
                        result.IgnoredCurveCount++; // diagonal
                        continue;
                    }
                    result.Lines.Add(new TitleBlockLine
                    {
                        X1 = UnitUtils.ConvertFromInternalUnits(a.X - box.Min.X, UnitTypeId.Inches),
                        Y1 = UnitUtils.ConvertFromInternalUnits(a.Y - box.Min.Y, UnitTypeId.Inches),
                        X2 = UnitUtils.ConvertFromInternalUnits(b.X - box.Min.X, UnitTypeId.Inches),
                        Y2 = UnitUtils.ConvertFromInternalUnits(b.Y - box.Min.Y, UnitTypeId.Inches)
                    });
                }
                else if (geometryObject is Curve)
                {
                    result.IgnoredCurveCount++; // arcs, splines
                }
            }
        }

        public static TitleBlockImage Render(Document doc, string titleBlockTypeUniqueId)
        {
            if (string.IsNullOrEmpty(titleBlockTypeUniqueId))
            {
                return new TitleBlockImage { FailureReason = "no titleblock selected" };
            }
            if (Cache.TryGetValue(titleBlockTypeUniqueId, out TitleBlockImage cached) && File.Exists(cached.ImagePath ?? string.Empty))
            {
                return cached;
            }

            var result = new TitleBlockImage { TitleBlockTypeUniqueId = titleBlockTypeUniqueId };
            if (!(doc.GetElement(titleBlockTypeUniqueId) is ElementType type))
            {
                result.FailureReason = "the titleblock type no longer exists in this model";
                return result;
            }

            string folder = Path.Combine(Path.GetTempPath(), "BauTools", "titleblocks");
            Directory.CreateDirectory(folder);
            string stem = "tb_" + Math.Abs(titleBlockTypeUniqueId.GetHashCode()).ToString("x");
            foreach (string stale in Directory.GetFiles(folder, stem + "*.png"))
            {
                try { File.Delete(stale); } catch (IOException) { /* still in use by a previous window — a new stem suffix below */ }
            }
            string basePath = Path.Combine(folder, stem + "_" + DateTime.UtcNow.Ticks.ToString("x"));

            using (var tx = new Transaction(doc, "BauTools: render titleblock (rolled back)"))
            {
                tx.Start();
                var warnings = new RevitFailureCollector(new OperationResult<string>(), "titleblock render");
                warnings.Attach(tx);
                try
                {
                    ViewSheet sheet = ViewSheet.Create(doc, type.Id);
                    doc.Regenerate();

                    Element instance = new FilteredElementCollector(doc, sheet.Id)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().FirstOrDefault();
                    BoundingBoxXYZ box = instance?.get_BoundingBox(sheet);
                    if (box != null)
                    {
                        result.SheetWidthIn = UnitUtils.ConvertFromInternalUnits(box.Max.X - box.Min.X, UnitTypeId.Inches);
                        result.SheetHeightIn = UnitUtils.ConvertFromInternalUnits(box.Max.Y - box.Min.Y, UnitTypeId.Inches);
                    }

                    if (instance != null && box != null)
                    {
                        CollectLines(instance, sheet, box, result);
                    }

                    var options = new ImageExportOptions
                    {
                        ExportRange = ExportRange.SetOfViews,
                        FilePath = basePath,
                        ZoomType = ZoomFitType.FitToPage,
                        PixelSize = PixelWidth,
                        FitDirection = FitDirectionType.Horizontal,
                        ImageResolution = ImageResolution.DPI_150,
                        HLRandWFViewsFileType = ImageFileType.PNG,
                        ShadowViewsFileType = ImageFileType.PNG
                    };
                    options.SetViewsAndSheets(new List<ElementId> { sheet.Id });
                    doc.ExportImage(options);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    result.FailureReason = ex.Message;
                }
                finally
                {
                    tx.RollBack(); // the temporary sheet never existed as far as the model is concerned
                }
            }

            // Revit appends " - Sheet - <number> - <name>" to the base path.
            string produced = Directory.GetFiles(folder, Path.GetFileName(basePath) + "*.png")
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (produced == null)
            {
                result.FailureReason = result.FailureReason ?? "Revit produced no image file";
                return result;
            }
            result.ImagePath = produced;
            Cache[titleBlockTypeUniqueId] = result;
            return result;
        }
    }
}
