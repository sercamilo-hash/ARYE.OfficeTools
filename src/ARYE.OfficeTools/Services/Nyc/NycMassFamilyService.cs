using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using ARYE.OfficeTools.Core.Nyc;
using ARYE.OfficeTools.Core.Results;
using ARYE.OfficeTools.Services;

namespace ARYE.OfficeTools.Services.Nyc
{
    /// <summary>Where the mass's plan came from — printed on the mass and in the result.</summary>
    public enum MassFootprintSource
    {
        /// <summary>Building Footprints (BIN) polygon — the real plan of an actual building.</summary>
        Footprint,

        /// <summary>The lot's own drawn ring, used when no footprint matched.</summary>
        LotRing,

        /// <summary>No footprint and no usable ring — a generic estimate box. Not currently produced, kept for callers that may add one later.</summary>
        Estimate
    }

    public sealed class MassPlacementRequest
    {
        /// <summary>Stable identity across re-runs, e.g. "BIN 2004131" or "LOT 2025770008". Used both as the family name and the edit-detection key.</summary>
        public string Key { get; set; } = string.Empty;

        public string Bbl { get; set; } = string.Empty;

        /// <summary>Closed-or-open footprint polygon, real model coordinates (already translated), constant Z.</summary>
        public List<XYZ> FootprintRing { get; set; } = new List<XYZ>();

        public Level Level { get; set; }

        public double HeightFt { get; set; }

        public MassFootprintSource FootprintSource { get; set; }

        /// <summary>When false (default) an instance the user has reshaped or re-heighted is left untouched.</summary>
        public bool ReplaceEdited { get; set; }

        public string RunId { get; set; } = string.Empty;
    }

    public sealed class MassPlacementResult
    {
        public bool Succeeded { get; set; }

        /// <summary>True when an edited instance was left alone instead of being refreshed.</summary>
        public bool Kept { get; set; }

        public string Label { get; set; } = string.Empty;

        public string FailureReason { get; set; }

        public ElementId InstanceId { get; set; }
    }

    /// <summary>
    /// Builds one real, editable loadable family per building — a solid
    /// extrusion on the footprint with height as an instance parameter — and
    /// places or refreshes an instance of it. Replaces the frozen DirectShape
    /// masses: the user can drag the height in Properties or open the family
    /// to reshape the plan, same as any other family in the project.
    ///
    /// A re-run never silently overwrites a mass the user reshaped: the
    /// instance's Height parameter and solid volume are stamped at creation
    /// time (<see cref="NycMassStampSchema"/>); if either has since moved away
    /// from the stamp, the instance is left alone unless the caller explicitly
    /// allows a replace.
    /// </summary>
    public sealed class NycMassFamilyService
    {
        private const double HeightEditToleranceFt = 0.01;
        private const double VolumeEditToleranceRatio = 0.01;
        private const string FamilyNamePrefix = "BauTools Mass — ";
        private const string HeightParameterName = "Height";

        /// <summary>
        /// Consecutive vertices closer than this merge into one. GIS-sourced
        /// footprints carry sub-inch coordinate noise that a 0.01 ft tolerance
        /// does not absorb; those near-duplicates become hairline edges that
        /// Revit's solid engine rejects as "too thin".
        /// </summary>
        private const double MergeToleranceFt = 0.1;

        /// <summary>
        /// Douglas-Peucker tolerance for mass footprints, feet. A real
        /// footprint (Building Footprints, LiDAR-derived) is far noisier than
        /// what a valid extrusion needs — every "Extrusion is too thin"
        /// failure across a whole block traced back to this being too tight.
        /// 1.5 ft drops sub-2ft parapets, AC enclosures and digitizing jitter
        /// while keeping real setbacks and wings (see PolygonSimplifierTests
        /// for the line between "noise" and "real corner").
        /// </summary>
        private const double MassSimplifyToleranceFt = 1.5;

        public MassPlacementResult PlaceOrUpdate(Document doc, MassPlacementRequest req)
        {
            string familyName = FamilyNamePrefix + req.Key;

            if (req.FootprintRing == null || req.FootprintRing.Count < 3)
            {
                return Fail(familyName, "footprint has fewer than 3 usable vertices");
            }
            if (req.Level == null)
            {
                return Fail(familyName, "no level to place the mass on");
            }

            FamilyInstance existing = FindExisting(doc, req.Key);
            if (existing != null)
            {
                MassStampData stamp = ReadStamp(existing);
                bool edited = stamp != null && IsEdited(existing, stamp);
                if (edited && !req.ReplaceEdited)
                {
                    return new MassPlacementResult
                    {
                        Succeeded = true,
                        Kept = true,
                        InstanceId = existing.Id,
                        Label = $"{familyName} — kept (edited by user)"
                    };
                }

                string refreshFailure = RebuildFamilyDefinition(doc, familyName, req, out _);
                if (refreshFailure != null)
                {
                    return Fail(familyName, refreshFailure);
                }
                SetHeightParameter(existing, req.HeightFt);
                doc.Regenerate();
                StampInstance(existing, req, ComputeVolumeCuFt(existing));
                return new MassPlacementResult
                {
                    Succeeded = true,
                    InstanceId = existing.Id,
                    Label = $"{familyName} — {(edited ? "replaced (was edited)" : "updated")}"
                };
            }

            string createFailure = RebuildFamilyDefinition(doc, familyName, req, out Family family);
            if (createFailure != null)
            {
                return Fail(familyName, createFailure);
            }
            FamilySymbol symbol = ActivateFirstSymbol(family);
            if (symbol == null)
            {
                return Fail(familyName, "family loaded but has no usable type");
            }

            FamilyInstance instance;
            try
            {
                instance = doc.Create.NewFamilyInstance(
                    req.FootprintRing[0], symbol, req.Level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException
                                        || ex is Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                return Fail(familyName, "could not place an instance: " + ex.Message);
            }
            SetHeightParameter(instance, req.HeightFt);
            doc.Regenerate();
            StampInstance(instance, req, ComputeVolumeCuFt(instance));
            return new MassPlacementResult { Succeeded = true, InstanceId = instance.Id, Label = familyName };
        }

        private static MassPlacementResult Fail(string label, string reason) =>
            new MassPlacementResult { Succeeded = false, Label = label, FailureReason = reason };

        // ── Family definition (build + load) ────────────────────────────────

        /// <summary>
        /// Creates a fresh Generic Model family with one solid extrusion on the
        /// footprint, height associated to an instance parameter, and loads it
        /// into <paramref name="doc"/> under <paramref name="familyName"/>
        /// (overwriting a same-named family already there). Returns null on
        /// success; the loaded Family is not returned when refreshing an
        /// existing instance (the caller only needs the side effect).
        /// </summary>
        private static string RebuildFamilyDefinition(Document doc, string familyName, MassPlacementRequest req, out Family family)
        {
            family = null;
            string templatePath = ResolveGenericModelTemplatePath(doc);
            if (templatePath == null)
            {
                return "Generic Model.rft template not found — cannot build an editable mass on this machine";
            }

            Document famDoc;
            try
            {
                famDoc = doc.Application.NewFamilyDocument(templatePath);
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is IOException)
            {
                return "could not open the family template: " + ex.Message;
            }

            try
            {
                XYZ anchor = req.FootprintRing[0];
                var localPoints = req.FootprintRing.Select(p => new XYZ(p.X - anchor.X, p.Y - anchor.Y, 0)).ToList();
                localPoints = SimplifyRing(localPoints);

                var loop = new CurveArray();
                for (int i = 0; i < localPoints.Count; i++)
                {
                    XYZ a = localPoints[i];
                    XYZ b = localPoints[(i + 1) % localPoints.Count];
                    if (a.DistanceTo(b) < MergeToleranceFt)
                    {
                        continue;
                    }
                    loop.Append(Line.CreateBound(a, b));
                }
                if (loop.Size < 3)
                {
                    return "footprint self-intersects or degenerates to fewer than 3 usable edges";
                }
                var profile = new CurveArrArray();
                profile.Append(loop);

                // Any geometry failure Revit raises here would otherwise surface as a
                // dialog no one can dismiss from behind this modeless window, so it
                // is intercepted and turned into a reported failure instead — same
                // pattern as the main lot draw.
                var geometryProblems = new OperationResult<string>();
                var failures = new RevitFailureCollector(geometryProblems, "mass geometry");

                using (var t = new Transaction(famDoc, "Build mass geometry"))
                {
                    t.Start();
                    failures.Attach(t);
                    FamilyManager fm = famDoc.FamilyManager;
                    if (fm.CurrentType == null)
                    {
                        fm.NewType("Default");
                    }
                    FamilyParameter heightParam = fm.get_Parameter(HeightParameterName)
                        ?? fm.AddParameter(HeightParameterName, GroupTypeId.Geometry, SpecTypeId.Length, isInstance: true);

                    // A new parameter starts at 0. Associating it to the extrusion end
                    // copies that 0 onto the solid, so it must hold the real height
                    // first — otherwise every mass regenerates as a 0-ft slab and
                    // Revit rejects it as "Extrusion is too thin".
                    double heightFt = Math.Max(req.HeightFt, 1.0);
                    fm.Set(heightParam, heightFt);

                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
                    SketchPlane sketchPlane = SketchPlane.Create(famDoc, plane);
                    Extrusion extrusion = famDoc.FamilyCreate.NewExtrusion(true, profile, sketchPlane, heightFt);
                    fm.AssociateElementParameterToFamilyParameter(extrusion.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), heightParam);
                    TransactionStatus status = t.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        return failures.Errors.Count > 0
                            ? "geometry rejected by Revit: " + string.Join("; ", failures.Errors)
                            : $"geometry transaction ended as {status}";
                    }
                }

                // Loaded through a temporary .rfa, never famDoc.LoadFamily(doc): that
                // overload must be called on the FAMILY document and refuses a target
                // with an open transaction — and masses are placed inside the lot
                // draw's transaction. (The earlier doc.LoadFamily(famDoc, …) called it
                // on the project, which is what failed every mass with "This method
                // can only be used with family documents".) The path overload works
                // inside a transaction; the file name becomes the family name.
                string rfaPath = SaveToTempFamilyFile(famDoc, familyName);
                famDoc.Close(false);
                try
                {
                    if (!doc.LoadFamily(rfaPath, new OverwriteFamilyLoadOptions(), out family) || family == null)
                    {
                        // Returns false when a same-named family is already loaded and
                        // nothing changed; the loaded one is the one to use.
                        family = FindFamily(doc, Path.GetFileNameWithoutExtension(rfaPath));
                    }
                }
                finally
                {
                    TryDelete(rfaPath);
                }
                return family == null ? "Revit declined to load the generated family" : null;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException
                                        || ex is Autodesk.Revit.Exceptions.InvalidOperationException
                                        || ex is Autodesk.Revit.Exceptions.ApplicationException
                                        || ex is IOException
                                        || ex is UnauthorizedAccessException)
            {
                return "could not build the mass geometry: " + ex.Message;
            }
            finally
            {
                if (famDoc.IsValidObject)
                {
                    famDoc.Close(false);
                }
            }
        }

        /// <summary>
        /// Drops near-duplicate vertices, then simplifies with Douglas-Peucker
        /// (Core's PolygonSimplifier) at a tolerance sized for a clean massing
        /// volume, not survey precision. Real Building Footprints / DOF
        /// planimetric geometry (LiDAR, photogrammetry) carries sub-foot
        /// zigzags — parapets, mechanical enclosures, digitizing noise — that
        /// make a technically-valid outline but a degenerate or
        /// self-intersecting extrusion once built as a solid ("too thin",
        /// "loop discontinuous"). Neither pass can change the polygon's
        /// winding or invent a vertex — only remove ones already redundant to
        /// within the tolerance.
        /// </summary>
        private static List<XYZ> SimplifyRing(List<XYZ> ring)
        {
            if (ring == null || ring.Count < 3)
            {
                return ring ?? new List<XYZ>();
            }

            var merged = new List<XYZ>();
            foreach (XYZ p in ring)
            {
                if (merged.Count == 0 || merged[merged.Count - 1].DistanceTo(p) > MergeToleranceFt)
                {
                    merged.Add(p);
                }
            }
            if (merged.Count >= 2 && merged[0].DistanceTo(merged[merged.Count - 1]) <= MergeToleranceFt)
            {
                merged.RemoveAt(merged.Count - 1); // closing vertex duplicated the first
            }
            if (merged.Count < 3)
            {
                return merged;
            }

            List<LotPoint> asLotPoints = merged.Select(p => new LotPoint(p.X, p.Y)).ToList();
            List<LotPoint> simplified = PolygonSimplifier.Simplify(asLotPoints, MassSimplifyToleranceFt);
            return simplified.Select(p => new XYZ(p.X, p.Y, 0)).ToList();
        }

        /// <summary>
        /// Saves the built family as %TEMP%\BauTools\masses\&lt;familyName&gt;.rfa —
        /// the name of the file is the name Revit gives the loaded family.
        /// </summary>
        private static string SaveToTempFamilyFile(Document famDoc, string familyName)
        {
            string folder = Path.Combine(Path.GetTempPath(), "BauTools", "masses");
            Directory.CreateDirectory(folder);
            string safeName = string.Concat(familyName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string path = Path.Combine(folder, safeName + ".rfa");
            famDoc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
            return path;
        }

        private static Family FindFamily(Document doc, string familyName) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.Name == familyName);

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file is harmless; the next run overwrites it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string ResolveGenericModelTemplatePath(Document doc)
        {
            string version = doc.Application.VersionNumber; // e.g. "2026"
            string[] candidates =
            {
                $@"C:\ProgramData\Autodesk\RVT {version}\Family Templates\English-Imperial\Generic Model.rft",
                $@"C:\ProgramData\Autodesk\RVT {version}\Family Templates\English\Generic Model.rft"
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        private static FamilySymbol ActivateFirstSymbol(Family family)
        {
            ElementId symbolId = family.GetFamilySymbolIds().FirstOrDefault();
            if (symbolId == null || symbolId == ElementId.InvalidElementId)
            {
                return null;
            }
            if (!(family.Document.GetElement(symbolId) is FamilySymbol symbol))
            {
                return null;
            }
            if (!symbol.IsActive)
            {
                symbol.Activate();
            }
            return symbol;
        }

        private static void SetHeightParameter(FamilyInstance instance, double heightFt)
        {
            Parameter p = instance.LookupParameter(HeightParameterName);
            if (p != null && !p.IsReadOnly)
            {
                p.Set(heightFt);
            }
        }

        /// <summary>Always overwrite — our family names are our own (BauTools Mass — …), so a name collision is only ever our own prior run.</summary>
        private sealed class OverwriteFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }

        // ── Volume ───────────────────────────────────────────────────────

        private static double ComputeVolumeCuFt(FamilyInstance instance)
        {
            try
            {
                var options = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
                GeometryElement geomElem = instance.get_Geometry(options);
                if (geomElem == null)
                {
                    return 0;
                }
                double volume = 0;
                foreach (GeometryObject obj in geomElem)
                {
                    volume += VolumeOf(obj);
                }
                return volume;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return 0;
            }
        }

        private static double VolumeOf(GeometryObject obj)
        {
            switch (obj)
            {
                case Solid solid:
                    return solid.Volume;
                case GeometryInstance gi:
                    double sum = 0;
                    foreach (GeometryObject inner in gi.GetInstanceGeometry())
                    {
                        sum += VolumeOf(inner);
                    }
                    return sum;
                default:
                    return 0;
            }
        }

        // ── Identity + edit detection (ExtensibleStorage stamp) ─────────────

        private static FamilyInstance FindExisting(Document doc, string key)
        {
            Schema schema = Schema.Lookup(NycMassStampSchema.Guid);
            if (schema == null)
            {
                return null;
            }
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .Cast<FamilyInstance>()
                .FirstOrDefault(fi =>
                {
                    Entity e = fi.GetEntity(schema);
                    return e != null && e.IsValid() && e.Get<string>(schema.GetField(NycMassStampSchema.FieldKey)) == key;
                });
        }

        private static bool IsEdited(FamilyInstance instance, MassStampData stamp)
        {
            double? currentHeight = instance.LookupParameter(HeightParameterName)?.AsDouble();
            if (currentHeight.HasValue && Math.Abs(currentHeight.Value - stamp.HeightFt) > HeightEditToleranceFt)
            {
                return true;
            }
            if (stamp.VolumeCuFt > 1.0)
            {
                double currentVolume = ComputeVolumeCuFt(instance);
                if (Math.Abs(currentVolume - stamp.VolumeCuFt) / stamp.VolumeCuFt > VolumeEditToleranceRatio)
                {
                    return true;
                }
            }
            return false;
        }

        private static void StampInstance(FamilyInstance instance, MassPlacementRequest req, double volumeCuFt)
        {
            Schema schema = NycMassStampSchema.GetOrCreate();
            var entity = new Entity(schema);
            entity.Set(schema.GetField(NycMassStampSchema.FieldKey), req.Key);
            entity.Set(schema.GetField(NycMassStampSchema.FieldBbl), req.Bbl ?? string.Empty);
            entity.Set(schema.GetField(NycMassStampSchema.FieldHeightFt), req.HeightFt);
            entity.Set(schema.GetField(NycMassStampSchema.FieldVolumeCuFt), volumeCuFt);
            entity.Set(schema.GetField(NycMassStampSchema.FieldFootprintSource), req.FootprintSource.ToString());
            entity.Set(schema.GetField(NycMassStampSchema.FieldRunId), req.RunId ?? string.Empty);
            instance.SetEntity(entity);
        }

        private static MassStampData ReadStamp(FamilyInstance instance)
        {
            Schema schema = Schema.Lookup(NycMassStampSchema.Guid);
            if (schema == null)
            {
                return null;
            }
            Entity entity = instance.GetEntity(schema);
            if (entity == null || !entity.IsValid())
            {
                return null;
            }
            return new MassStampData
            {
                HeightFt = entity.Get<double>(schema.GetField(NycMassStampSchema.FieldHeightFt)),
                VolumeCuFt = entity.Get<double>(schema.GetField(NycMassStampSchema.FieldVolumeCuFt))
            };
        }

        private sealed class MassStampData
        {
            public double HeightFt { get; set; }
            public double VolumeCuFt { get; set; }
        }
    }

    /// <summary>
    /// ExtensibleStorage schema stamped on every BauTools mass instance:
    /// identity (so a re-run finds "its own" instance) plus the height and
    /// volume at stamp time (so a re-run can tell whether the user reshaped
    /// it since). Internal to this add-in — not part of the TopoGenerator
    /// interop contract.
    /// </summary>
    internal static class NycMassStampSchema
    {
        public static readonly Guid Guid = new Guid("D2E6B8B1-9F3D-4C2E-9E2C-5B7A6F0C1A2E");

        public const string FieldKey = "Key";
        public const string FieldBbl = "Bbl";
        public const string FieldHeightFt = "HeightFt";
        public const string FieldVolumeCuFt = "VolumeCuFt";
        public const string FieldFootprintSource = "FootprintSource";
        public const string FieldRunId = "RunId";

        public static Schema GetOrCreate()
        {
            Schema existing = Schema.Lookup(Guid);
            if (existing != null)
            {
                return existing;
            }
            var builder = new SchemaBuilder(Guid);
            builder.SetSchemaName("BauToolsMassStamp");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetVendorId("ARYE");
            builder.SetDocumentation("BauTools NYC Lot Boundary — identity and edit-detection stamp for generated context masses.");
            builder.AddSimpleField(FieldKey, typeof(string));
            builder.AddSimpleField(FieldBbl, typeof(string));
            builder.AddSimpleField(FieldHeightFt, typeof(double));
            builder.AddSimpleField(FieldVolumeCuFt, typeof(double));
            builder.AddSimpleField(FieldFootprintSource, typeof(string));
            builder.AddSimpleField(FieldRunId, typeof(string));
            return builder.Finish();
        }
    }
}
