using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using ARYE.OfficeTools.Core.Interop;
using ARYE.OfficeTools.Core.Logging;
using ARYE.OfficeTools.Core.Nyc;
using ARYE.OfficeTools.Core.Results;
using ARYE.OfficeTools.Services.Interop;

namespace ARYE.OfficeTools.Services.Nyc
{
    /// <summary>
    /// Draws the subject lot, adjacent lots, the rest of the block, a real
    /// per-frontage sidewalk, 3D context masses (real height when a Building
    /// Footprints record matches, honestly labeled estimate otherwise) and
    /// confirmed street names as actual 3D Model Text — never flat annotation.
    ///
    /// The subject lot is the interop handoff to TopoGenerator: always Model
    /// Lines, line style "BauTools - Property Line", one ExtensibleStorage face
    /// payload per line and one lot record per BBL (docs/CONTRATO-LOT-TOPO.md).
    /// One transaction; every element's fate (placed / skipped / failed, with
    /// why) lands in the returned result, never just a final count.
    /// </summary>
    internal sealed class NycLotDrawService
    {
        private readonly NycMassFamilyService _massService = new NycMassFamilyService();

        /// <summary>
        /// Douglas-Peucker tolerance for roadbed/sidewalk polygons, feet. DOF's
        /// 2022 planimetric survey carries the same kind of sub-foot noise as
        /// Building Footprints — enough to make a line loop reject as
        /// discontinuous. Smaller than the mass tolerance: these are flat
        /// outlines, not extrusions, so less needs dropping to draw cleanly.
        /// </summary>
        private const double GroundLayerSimplifyToleranceFt = 0.5;

        public OperationResult<string> Draw(
            Document doc, NycBlockContext context, IReadOnlyList<NycBuildingFootprint> footprints,
            NycLotDrawOptions options, ILog log)
        {
            var result = new OperationResult<string>();
            Level level = doc.GetElement(options.LevelId) as Level;
            if (level == null)
            {
                result.Failed.Add(new OperationFailure("Target level", "no level selected or level no longer exists"));
                return result;
            }
            if (options.SubjectRing.Count < 3 || options.SubjectFaces.Count != options.SubjectRing.Count)
            {
                result.Failed.Add(new OperationFailure(context.SubjectLot.Bbl,
                    $"subject geometry not ready: {options.SubjectRing.Count} vertices for {options.SubjectFaces.Count} faces"));
                return result;
            }

            // Anchor: face 0 starts at SubjectRing[0]; that vertex lands on AnchorPointInModel.
            // Z is the LEVEL's elevation, not 0 — every drawn curve must land on the
            // sketch plane created below at that same Z, or Revit rejects it with
            // "Curve must be in the plane" (silent on Level 1 at elevation 0, but real
            // and total on any other level).
            LotPoint anchorVertex = options.SubjectRing[0];
            var translation = new XYZ(
                options.AnchorPointInModel.X - anchorVertex.X,
                options.AnchorPointInModel.Y - anchorVertex.Y,
                level.Elevation);
            string runId = Guid.NewGuid().ToString();

            var createdIds = new List<ElementId>();
            var subjectIds = new List<ElementId>();
            var contextIds = new List<ElementId>();
            // Masses get a group of their own, apart from the context lines (Sergio, 2026-10-12).
            var massIds = new List<ElementId>();

            using (var tx = new Transaction(doc, $"BauTools: Draw NYC lot {context.SubjectLot.Bbl}"))
            {
                tx.Start();
                // Revit cannot show its failure dialog behind our modal window; without
                // this, one warning at Commit leaves the transaction open and Revit
                // discards the whole draw when the command ends.
                var failures = new RevitFailureCollector(result, "lot draw");
                failures.Attach(tx);

                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation));
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                GraphicsStyle propertyStyle = TryStyle(() => LotLineStyleService.PropertyLine(doc), "Property Line", result);
                GraphicsStyle adjacentStyle = TryStyle(() => LotLineStyleService.AdjacentLot(doc), "Adjacent Lot", result);
                GraphicsStyle blockStyle = TryStyle(() => LotLineStyleService.Block(doc), "Block", result);
                GraphicsStyle sidewalkStyle = TryStyle(() => LotLineStyleService.Sidewalk(doc), "Sidewalk", result);
                GraphicsStyle otherBlockStyle = TryStyle(() => LotLineStyleService.OtherBlock(doc), "Other Block", result);

                // ── Subject lot: one Model Line per face, stamped for TopoGenerator ──
                DrawSubject(doc, sketchPlane, context.SubjectLot, options, translation, runId, propertyStyle, result, subjectIds);
                createdIds.AddRange(subjectIds);

                // ── Adjacent lots — each in the source chosen for it in "Other lots on the block" ──
                if (options.DrawAdjacentLots)
                {
                    foreach (NycLotInfo lot in context.AdjacentLots)
                    {
                        List<LotPoint> ring = RingForBlockLot(lot, options, out string src);
                        if (ring == null || ring.Count < 3)
                        {
                            result.Skipped.Add(new OperationSkip("Adjacent lot " + lot.Bbl, $"no {src} boundary geometry available"));
                            continue;
                        }
                        DrawRing(doc, sketchPlane, options.ElementType, ring, translation,
                            $"Adjacent lot {lot.Bbl} ({src})", adjacentStyle, result, contextIds);
                    }
                }

                // ── Rest of the block — same per-lot choice ─────────────────────
                if (options.DrawRemainingBlock)
                {
                    foreach (NycLotInfo lot in context.RemainingBlockLots)
                    {
                        List<LotPoint> ring = RingForBlockLot(lot, options, out string src);
                        if (ring == null || ring.Count < 3)
                        {
                            result.Skipped.Add(new OperationSkip("Block lot " + lot.Bbl, $"no {src} boundary geometry available"));
                            continue;
                        }
                        DrawRing(doc, sketchPlane, options.ElementType, ring, translation,
                            $"Block lot {lot.Bbl} ({src})", blockStyle, result, contextIds);
                    }
                }

                // ── Additional blocks: whole other blocks across a street, added as far
                // urban context — always DTM, never Legal or Survey. ───────────────────
                foreach (NycAdditionalBlock addlBlock in options.AdditionalBlocks)
                {
                    foreach (NycLotInfo lot in addlBlock.Lots)
                    {
                        if (lot.PolygonRings.Count == 0)
                        {
                            result.Skipped.Add(new OperationSkip($"Block {addlBlock.BlockNumber} lot {lot.Bbl}",
                                "no boundary geometry from MapPLUTO"));
                            continue;
                        }
                        DrawRing(doc, sketchPlane, options.ElementType, lot.PolygonRings[0], translation,
                            $"Block {addlBlock.BlockNumber} lot {lot.Bbl} (DTM)", otherBlockStyle, result, contextIds);
                    }
                }

                // ── Ground context: real DOF planimetric geometry, opt-in ────────
                if (options.DrawRoadbeds)
                {
                    GraphicsStyle roadbedStyle = TryStyle(() => LotLineStyleService.Roadbed(doc), "Roadbed", result);
                    int i = 0;
                    foreach (NycGroundPolygon poly in options.Roadbeds)
                    {
                        foreach (List<LotPoint> ring in poly.PolygonRings)
                        {
                            DrawRing(doc, sketchPlane, options.ElementType, PolygonSimplifier.Simplify(ring, GroundLayerSimplifyToleranceFt),
                                translation, $"Roadbed {++i}", roadbedStyle, result, contextIds);
                        }
                    }
                }
                if (options.DrawRealSidewalks)
                {
                    GraphicsStyle realSidewalkStyle = TryStyle(() => LotLineStyleService.RealSidewalk(doc), "Sidewalk (Real)", result);
                    int i = 0;
                    foreach (NycGroundPolygon poly in options.RealSidewalks)
                    {
                        foreach (List<LotPoint> ring in poly.PolygonRings)
                        {
                            DrawRing(doc, sketchPlane, options.ElementType, PolygonSimplifier.Simplify(ring, GroundLayerSimplifyToleranceFt),
                                translation, $"Sidewalk (real) {++i}", realSidewalkStyle, result, contextIds);
                        }
                    }
                }
                if (options.DrawCurbs)
                {
                    GraphicsStyle curbStyle = TryStyle(() => LotLineStyleService.Curb(doc), "Curb", result);
                    int i = 0;
                    foreach (NycCurbLine curb in options.Curbs)
                    {
                        DrawPolyline(doc, sketchPlane, curb.Path, translation, $"Curb {++i}", curbStyle, result, contextIds);
                    }
                }
                if (options.DrawContextTrees)
                {
                    DrawContextTrees(doc, level, options, translation, result, contextIds);
                }

                // ── Sidewalk: real outward offset of the subject's DRAWN geometry ──
                if (options.DrawSidewalk)
                {
                    try
                    {
                        List<LotPoint> offsetRing = LotGeometry.OffsetRingOutward(options.SubjectRing, options.SidewalkWidthFt);
                        DrawRing(doc, sketchPlane, options.ElementType, offsetRing, translation,
                            "Sidewalk (subject frontage +" + options.SidewalkWidthFt.ToString("0.#") + " ft)",
                            sidewalkStyle, result, contextIds);
                    }
                    catch (ArgumentException ex)
                    {
                        result.Failed.Add(new OperationFailure("Sidewalk", ex.Message));
                    }
                }

                // ── 3D context masses: editable families on the real footprint, or an honestly-labeled fallback ──
                if (options.Draw3DMasses)
                {
                    foreach (NycLotInfo lot in context.AdjacentLots.Concat(context.RemainingBlockLots))
                    {
                        List<LotPoint> ring = RingForBlockLot(lot, options, out _);
                        DrawMassesForLot(doc, lot, ring, footprints, translation, level, options, result, massIds);
                    }
                    if (options.DrawSubjectMass)
                    {
                        DrawMassesForLot(doc, context.SubjectLot, options.SubjectRing, footprints, translation, level, options, result, massIds);
                    }
                    foreach (NycAdditionalBlock addlBlock in options.AdditionalBlocks)
                    {
                        foreach (NycLotInfo lot in addlBlock.Lots)
                        {
                            List<LotPoint> ring = lot.PolygonRings.Count > 0 ? lot.PolygonRings[0] : null;
                            DrawMassesForLot(doc, lot, ring, footprints, translation, level, options, result, massIds);
                        }
                    }
                }

                // ── Street labels: confirmed names only, real 3D ModelText ──────
                if (options.DrawStreetLabels)
                {
                    DrawStreetLabels(doc, level, context, options, translation, result, contextIds);
                }

                // ── Zoning summary table (2D annotation — this one is fine flat) ──
                if (options.DrawZoningTable)
                {
                    DrawZoningTable(doc, level, context.SubjectLot, options, translation, result, contextIds);
                }

                // ── Lot record: the handoff TopoGenerator reads first ────────────
                WriteLotRecord(doc, context.SubjectLot, options, level, runId, anchorVertex, subjectIds, result);

                createdIds.AddRange(contextIds);
                createdIds.AddRange(massIds);

                // Regenerate BEFORE grouping: DirectShape geometry and line styles are
                // finalized at regeneration, and if that happens after NewGroup Revit
                // posts "A group has been changed outside group edit mode" for every
                // member (seen in the 2026 journal on 438 Concord).
                try
                {
                    doc.Regenerate();
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    result.Skipped.Add(new OperationSkip("Regenerate before grouping", ex.Message));
                }
                Group(doc, options, subjectIds, contextIds, result);
                GroupMasses(doc, options, context.SubjectLot.Bbl, massIds, result);

                TransactionStatus status = tx.Commit();
                if (status != TransactionStatus.Committed)
                {
                    result.Failed.Add(new OperationFailure("Transaction",
                        $"commit ended as {status} — nothing was kept" +
                        (failures.Errors.Count > 0 ? ": " + string.Join("; ", failures.Errors) : string.Empty)));
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        tx.RollBack();
                    }
                }
            }

            log.Info($"NYC Lot draw {context.SubjectLot.Bbl} [{options.GeometrySource}] run {runId}: {result.BuildSummary()}");
            return result;
        }

        private static GraphicsStyle TryStyle(Func<GraphicsStyle> factory, string name, OperationResult<string> result)
        {
            try
            {
                return factory();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Skipped.Add(new OperationSkip($"Line style '{name}'", "could not create — " + ex.Message));
                return null;
            }
        }

        /// <summary>
        /// Every drawn element says what it is in Comments, so a dimension placed
        /// in Revit never leaves the user guessing which polygon they measured —
        /// subject lot vs. sidewalk offset vs. a neighbor's tax-map edge.
        /// </summary>
        private static void Tag(Element element, string label)
        {
            Parameter comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (comments != null && !comments.IsReadOnly)
            {
                comments.Set("BauTools: " + label);
            }
        }

        private static XYZ ToModel(LotPoint p, XYZ translation) =>
            new XYZ(p.X + translation.X, p.Y + translation.Y, translation.Z);

        private static void DrawSubject(
            Document doc, SketchPlane sketchPlane, NycLotInfo subject, NycLotDrawOptions options,
            XYZ translation, string runId, GraphicsStyle style,
            OperationResult<string> result, List<ElementId> createdIds)
        {
            int n = options.SubjectRing.Count;
            int stamped = 0;
            for (int i = 0; i < n; i++)
            {
                NycLotFace face = options.SubjectFaces[i];
                LotPoint a = options.SubjectRing[i];
                LotPoint b = options.SubjectRing[(i + 1) % n];
                string label = $"Lot {subject.Bbl} face {i}";
                XYZ pa = ToModel(a, translation);
                XYZ pb = ToModel(b, translation);
                if (pa.DistanceTo(pb) < 0.01)
                {
                    result.Failed.Add(new OperationFailure(label, "zero-length face"));
                    continue;
                }

                ModelCurve curve;
                try
                {
                    curve = doc.Create.NewModelCurve(Line.CreateBound(pa, pb), sketchPlane);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    result.Failed.Add(new OperationFailure(label, ex));
                    continue;
                }
                createdIds.Add(curve.Id);
                Tag(curve, label);

                string styleProblem = LotLineStyleService.Apply(curve, style);
                if (styleProblem != null)
                {
                    result.Skipped.Add(new OperationSkip(label + " line style", styleProblem));
                }

                var payload = new LotFacePayload
                {
                    RunId = runId,
                    Bbl = subject.Bbl,
                    LotUuid = face.LotUuid,
                    FaceIndex = i,
                    FaceCount = n,
                    FaceRole = face.LegalLengthFt > 0 || face.IsStreetFace
                        ? (face.IsStreetFace ? LotInteropContract.FaceRoleStreet : LotInteropContract.FaceRoleInterior)
                        : LotInteropContract.FaceRoleUnknown,
                    StreetName = i < options.FaceStreetNames.Count ? options.FaceStreetNames[i] ?? string.Empty : string.Empty,
                    LegalLengthFt = face.LegalLengthFt,
                    DigitizedLengthFt = face.MeasuredLengthFt,
                    DrawnLengthFt = a.DistanceTo(b),
                    GeometrySource = options.GeometrySource.ToString(),
                    StartStatePlane = new InteropPoint { X = a.X, Y = a.Y },
                    EndStatePlane = new InteropPoint { X = b.X, Y = b.Y },
                    StartModel = new InteropPoint { X = pa.X, Y = pa.Y },
                    EndModel = new InteropPoint { X = pb.X, Y = pb.Y }
                };
                string stampProblem = LotInteropStampService.StampFace(curve, payload);
                if (stampProblem != null)
                {
                    result.Failed.Add(new OperationFailure(label + " interop stamp", stampProblem));
                }
                else
                {
                    stamped++;
                }
            }
            result.Succeeded.Add($"Subject lot {subject.Bbl}: {n} faces as Model Lines ({options.GeometrySource}), {stamped} stamped for TopoGenerator");
        }

        private static void WriteLotRecord(
            Document doc, NycLotInfo subject, NycLotDrawOptions options, Level level, string runId,
            LotPoint anchorVertex, List<ElementId> subjectIds, OperationResult<string> result)
        {
            var legal = options.SubjectFaces.Select(f => f.LegalLengthFt).ToList();
            var digitized = options.SubjectFaces.Select(f => f.MeasuredLengthFt).ToList();
            List<LotPoint> digitizedRing = options.SubjectFaces.Select(f => f.Start).ToList();

            int.TryParse(subject.Block, out int block);
            int.TryParse(subject.Lot, out int lot);

            var payload = new LotRecordPayload
            {
                RunId = runId,
                DrawnUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                BauToolsVersion = App.Version,
                Bbl = subject.Bbl,
                Address = subject.Address,
                Borough = subject.Borough,
                Block = block,
                Lot = lot,
                IsCornerLot = options.SubjectFaces.Count(f => f.IsStreetFace) >= 2,
                GeometrySource = options.GeometrySource.ToString(),
                LevelId = level.Id.Value,
                LevelName = level.Name,
                LevelElevationFt = level.Elevation,
                Anchor = new LotAnchorPayload
                {
                    Corner = options.AnchorCorner.ToString(),
                    Model = new InteropPoint { X = options.AnchorPointInModel.X, Y = options.AnchorPointInModel.Y },
                    StatePlane = new InteropPoint { X = anchorVertex.X, Y = anchorVertex.Y }
                },
                ModelToStatePlane = new ModelToStatePlanePayload
                {
                    OffsetX = anchorVertex.X - options.AnchorPointInModel.X,
                    OffsetY = anchorVertex.Y - options.AnchorPointInModel.Y,
                    RotationDeg = 0
                },
                Recorded = new LotDimensionsPayload
                {
                    FrontFt = subject.LotFrontRecordedFt,
                    DepthFt = subject.LotDepthRecordedFt,
                    AreaSqFt = subject.LotAreaSqFt
                },
                Legal = new LotFaceLengthsPayload
                {
                    Faces = legal,
                    AreaSqFt = options.GeometrySource == LotGeometrySource.TaxMapLegal
                        ? Math.Abs(LotGeometry.SignedArea(options.SubjectRing))
                        : 0
                },
                Digitized = new LotFaceLengthsPayload
                {
                    Faces = digitized,
                    AreaSqFt = digitizedRing.Count >= 3 ? Math.Abs(LotGeometry.SignedArea(digitizedRing)) : 0
                },
                FaceElementIds = subjectIds.Select(id => id.Value).ToList(),
                SidewalkWidthFt = options.SidewalkWidthFt,
                SidewalkWidthSource = options.SidewalkWidthSource,
                ZoningLotBbls = options.ZoningLotBbls
            };

            DataStorage storage = LotInteropStampService.WriteRecord(doc, payload, out string reason);
            if (storage == null)
            {
                result.Failed.Add(new OperationFailure("Lot record (TopoGenerator handoff)", reason ?? "unknown"));
            }
            else
            {
                result.Succeeded.Add($"Lot record '{storage.Name}' written (run {runId.Substring(0, 8)}…)");
            }
        }

        private static CurveLoop BuildLoop(IReadOnlyList<LotPoint> ring, XYZ translation)
        {
            var loop = new CurveLoop();
            for (int i = 0; i < ring.Count; i++)
            {
                XYZ a = ToModel(ring[i], translation);
                XYZ b = ToModel(ring[(i + 1) % ring.Count], translation);
                if (a.DistanceTo(b) < 0.01)
                {
                    continue; // degenerate segment — a repeated closing vertex from the source data
                }
                loop.Append(Line.CreateBound(a, b));
            }
            return loop;
        }

        private static void DrawRing(
            Document doc, SketchPlane sketchPlane, LotElementType elementType,
            IReadOnlyList<LotPoint> ring, XYZ translation, string label, GraphicsStyle style,
            OperationResult<string> result, List<ElementId> createdIds)
        {
            CurveLoop loop;
            try
            {
                loop = BuildLoop(ring, translation);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                result.Failed.Add(new OperationFailure(label, ex));
                return;
            }

            bool styleReported = false;
            foreach (Curve curve in loop)
            {
                try
                {
                    if (elementType == LotElementType.AreaBoundaryLines && !(doc.ActiveView is ViewPlan))
                    {
                        result.Failed.Add(new OperationFailure(label,
                            "Area Boundary Lines need an area plan as the active view"));
                        return;
                    }
                    CurveElement created = elementType switch
                    {
                        LotElementType.DetailLines => doc.Create.NewDetailCurve(doc.ActiveView, curve),
                        LotElementType.AreaBoundaryLines => doc.Create.NewAreaBoundaryLine(
                            sketchPlane, curve, (ViewPlan)doc.ActiveView),
                        _ => doc.Create.NewModelCurve(curve, sketchPlane)
                    };
                    createdIds.Add(created.Id);
                    Tag(created, label);
                    if (style != null && elementType != LotElementType.AreaBoundaryLines)
                    {
                        string problem = LotLineStyleService.Apply(created, style);
                        if (problem != null && !styleReported)
                        {
                            styleReported = true;
                            result.Skipped.Add(new OperationSkip(label + " line style", problem));
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    result.Failed.Add(new OperationFailure(label, ex));
                    return;
                }
            }
            result.Succeeded.Add(label);
        }

        /// <summary>An open path (curb) — every consecutive pair of points, never closed back to the start.</summary>
        private static void DrawPolyline(
            Document doc, SketchPlane sketchPlane, IReadOnlyList<LotPoint> path, XYZ translation,
            string label, GraphicsStyle style, OperationResult<string> result, List<ElementId> createdIds)
        {
            if (path.Count < 2)
            {
                result.Skipped.Add(new OperationSkip(label, "fewer than 2 vertices"));
                return;
            }
            bool any = false;
            for (int i = 0; i < path.Count - 1; i++)
            {
                XYZ a = ToModel(path[i], translation);
                XYZ b = ToModel(path[i + 1], translation);
                if (a.DistanceTo(b) < 0.01)
                {
                    continue;
                }
                try
                {
                    ModelCurve curve = doc.Create.NewModelCurve(Line.CreateBound(a, b), sketchPlane);
                    createdIds.Add(curve.Id);
                    Tag(curve, label);
                    any = true;
                    if (style != null)
                    {
                        string problem = LotLineStyleService.Apply(curve, style);
                        if (problem != null)
                        {
                            result.Skipped.Add(new OperationSkip(label + " line style", problem));
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    result.Failed.Add(new OperationFailure(label, ex));
                    return;
                }
            }
            if (any)
            {
                result.Succeeded.Add(label);
            }
        }

        /// <summary>Which ring a block lot (adjacent or rest-of-block) draws from: its own DTM/Legal choice, resolved in the window.</summary>
        private static List<LotPoint> RingForBlockLot(NycLotInfo lot, NycLotDrawOptions options, out string sourceLabel)
        {
            LotSourceChoice choice = options.BlockLotSources.TryGetValue(lot.Bbl, out LotSourceChoice c) ? c : LotSourceChoice.None;
            if (choice == LotSourceChoice.Legal
                && options.BlockLotLegalRings.TryGetValue(lot.Bbl, out List<LotPoint> legalRing)
                && legalRing.Count >= 3)
            {
                sourceLabel = "Legal";
                return legalRing;
            }
            sourceLabel = "DTM";
            return lot.PolygonRings.Count > 0 ? lot.PolygonRings[0] : null;
        }

        /// <summary>
        /// One editable mass per matching Building Footprint (real plan, real
        /// height), or — when none matched, or the caller asked to skip
        /// footprints — one mass on the lot's own drawn ring.
        /// </summary>
        private void DrawMassesForLot(
            Document doc, NycLotInfo lot, List<LotPoint> lotRing, IReadOnlyList<NycBuildingFootprint> footprints,
            XYZ translation, Level level, NycLotDrawOptions options, OperationResult<string> result, List<ElementId> createdIds)
        {
            List<FootprintMatch> matched = options.MassUseFootprints
                ? NycFootprintMatcher.MatchForLot(lot.Bbl, lotRing, footprints)
                : new List<FootprintMatch>();
            List<NycBuildingFootprint> matches = matched.Select(m => m.Footprint).ToList();
            bool spatialFallback = matched.Count > 0 && matched[0].Kind == FootprintMatchKind.SpatialFallback;

            if (options.MassUseFootprints && matches.Count > 0)
            {
                foreach (NycBuildingFootprint fp in matches)
                {
                    ResolvedHeight height = BuildingHeightResolver.Resolve(lot, new[] { fp });
                    PlaceMass(doc, "BIN " + fp.Bin, lot.Bbl, fp.PolygonRings[0], MassFootprintSource.Footprint,
                        height, translation, level, options, result, createdIds,
                        spatialFallback ? "matched spatially — MAPPLUTO_BBL on this footprint did not join to this lot" : null);
                }
                return;
            }

            if (lotRing == null || lotRing.Count < 3)
            {
                result.Skipped.Add(new OperationSkip($"3D mass {lot.Bbl}",
                    "no building footprint matched and the lot has no usable boundary ring to fall back on"));
                return;
            }
            ResolvedHeight lotHeight = BuildingHeightResolver.Resolve(lot, footprints);
            PlaceMass(doc, "LOT " + lot.Bbl, lot.Bbl, lotRing, MassFootprintSource.LotRing,
                lotHeight, translation, level, options, result, createdIds);
        }

        private void PlaceMass(
            Document doc, string key, string bbl, List<LotPoint> ring, MassFootprintSource source, ResolvedHeight height,
            XYZ translation, Level level, NycLotDrawOptions options, OperationResult<string> result, List<ElementId> createdIds,
            string matchNote = null)
        {
            List<XYZ> projectRing = ring
                .Select(p => ToModel(p, translation)) // translation.Z is already level.Elevation
                .ToList();
            var request = new MassPlacementRequest
            {
                Key = key,
                Bbl = bbl,
                FootprintRing = projectRing,
                Level = level,
                HeightFt = height.HeightFt,
                FootprintSource = source,
                ReplaceEdited = options.ReplaceEditedMasses,
                RunId = options.RunId
            };

            // One building's mass must never take the rest of the batch down with
            // it — this call already has its own targeted catches, but a batch of
            // 40+ buildings across several blocks is exactly where an unforeseen
            // exception type would otherwise abort every mass after it in
            // silence. Every mass ends in Succeeded, Skipped or Failed, by name.
            MassPlacementResult placement;
            try
            {
                placement = _massService.PlaceOrUpdate(doc, request);
            }
            catch (Exception ex) when (!(ex is StackOverflowException || ex is OutOfMemoryException))
            {
                result.Failed.Add(new OperationFailure($"3D mass {key}", "unexpected error while generating this mass: " + ex.Message));
                return;
            }

            string label = $"3D mass {placement.Label} ({height.SourceLabel}, {height.HeightFt:0} ft)"
                + (matchNote != null ? $" — {matchNote}" : string.Empty);
            if (!placement.Succeeded)
            {
                result.Failed.Add(new OperationFailure(label, placement.FailureReason));
                return;
            }
            if (placement.InstanceId != null && placement.InstanceId != ElementId.InvalidElementId)
            {
                createdIds.Add(placement.InstanceId);
            }
            if (placement.Kept)
            {
                result.Skipped.Add(new OperationSkip(label, "kept — edited by user since the last run"));
            }
            else
            {
                result.Succeeded.Add(label);
            }
        }

        /// <summary>
        /// Real 3D street text: ModelText itself can only be authored inside a
        /// family document (Document.FamilyCreate.NewModelText, not available
        /// on a project Document), so this places instances of a loaded
        /// "Model Text" family type instead — the standard way a project gets
        /// genuine 3D text rather than flat TextNote annotation.
        /// </summary>
        private static void DrawStreetLabels(
            Document doc, Level level, NycBlockContext context, NycLotDrawOptions options,
            XYZ translation, OperationResult<string> result, List<ElementId> createdIds)
        {
            FamilySymbol symbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Family.Name.IndexOf("Model Text", StringComparison.OrdinalIgnoreCase) >= 0);
            if (symbol == null)
            {
                result.Skipped.Add(new OperationSkip("Street labels",
                    "no 'Model Text' family is loaded in this project — load Model Text.rfa " +
                    "(Annotations library) and re-run, or add the 4 labels manually"));
                return;
            }
            if (!symbol.IsActive)
            {
                symbol.Activate();
            }
            Parameter typeTextParam = symbol.LookupParameter("Text");

            double minX = context.MinX, maxX = context.MaxX, minY = context.MinY, maxY = context.MaxY;
            foreach (StreetFrontage frontage in options.ConfirmedFrontages)
            {
                if (string.IsNullOrWhiteSpace(frontage.StreetName))
                {
                    result.Skipped.Add(new OperationSkip($"Street label ({frontage.Side})", "no name confirmed for this side"));
                    continue;
                }

                LotPoint midpoint = frontage.Side switch
                {
                    CompassSide.North => new LotPoint((minX + maxX) / 2, maxY),
                    CompassSide.South => new LotPoint((minX + maxX) / 2, minY),
                    CompassSide.East => new LotPoint(maxX, (minY + maxY) / 2),
                    _ => new LotPoint(minX, (minY + maxY) / 2)
                };
                XYZ origin = ToModel(midpoint, translation);

                try
                {
                    FamilyInstance instance = doc.Create.NewFamilyInstance(
                        origin, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    Parameter textParam = instance.LookupParameter("Text") ?? typeTextParam;
                    if (textParam != null && !textParam.IsReadOnly && textParam.StorageType == StorageType.String)
                    {
                        textParam.Set(frontage.StreetName);
                        createdIds.Add(instance.Id);
                        result.Succeeded.Add($"Street label: {frontage.StreetName} ({frontage.Side})");
                    }
                    else
                    {
                        createdIds.Add(instance.Id);
                        result.Skipped.Add(new OperationSkip($"Street label ({frontage.Side})",
                            "placed the Model Text family but it has no writable 'Text' parameter — set it by hand"));
                    }
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException
                                            || ex is Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    result.Failed.Add(new OperationFailure($"Street label ({frontage.Side})", ex));
                }
            }
        }

        /// <summary>
        /// Street trees from NYC Parks' 2025 Tree Census, placed as instances of
        /// any loaded Planting family (or one whose name says "Tree") — same
        /// honest-fallback pattern as street labels needing a Model Text family:
        /// if the project has none loaded, every tree is skipped with the reason
        /// instead of failing or inventing geometry.
        /// </summary>
        private static void DrawContextTrees(
            Document doc, Level level, NycLotDrawOptions options, XYZ translation,
            OperationResult<string> result, List<ElementId> createdIds)
        {
            if (options.ContextTrees.Count == 0)
            {
                return;
            }
            FamilySymbol symbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Planting)
                .Cast<FamilySymbol>()
                .FirstOrDefault();
            if (symbol == null)
            {
                symbol = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Family.Name.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            if (symbol == null)
            {
                result.Skipped.Add(new OperationSkip($"{options.ContextTrees.Count} street tree(s)",
                    "no Planting family loaded in this project — load one (e.g. an RPC tree from the library) and re-run"));
                return;
            }
            if (!symbol.IsActive)
            {
                symbol.Activate();
            }

            int placed = 0;
            foreach (NycStreetTree tree in options.ContextTrees)
            {
                XYZ origin = ToModel(tree.Location, translation);
                string label = tree.Species.Length > 0 ? $"Street tree ({tree.Species})" : "Street tree (species unrecorded)";
                try
                {
                    FamilyInstance instance = doc.Create.NewFamilyInstance(
                        origin, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    createdIds.Add(instance.Id);
                    Tag(instance, label + (tree.Condition.Length > 0 ? $" · condition {tree.Condition}" : string.Empty));
                    placed++;
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException
                                            || ex is Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    result.Failed.Add(new OperationFailure(label, ex));
                }
            }
            if (placed > 0)
            {
                result.Succeeded.Add($"{placed} of {options.ContextTrees.Count} street tree(s) placed ('{symbol.Family.Name}')");
            }
        }

        private static void DrawZoningTable(
            Document doc, Level level, NycLotInfo subject, NycLotDrawOptions options, XYZ translation,
            OperationResult<string> result, List<ElementId> createdIds)
        {
            TextNoteType noteType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault();
            if (noteType == null || doc.ActiveView == null)
            {
                result.Skipped.Add(new OperationSkip("Zoning table", "no text note type or active view available"));
                return;
            }

            // The drawn boundary has a declared source; the record may say otherwise.
            // Both go on the drawing so nobody reads a 103' line as the legal 100'.
            List<DimensionComparison> dimensions = LotDimensionCheck.Compare(subject);
            List<FaceComparison> faces = LotFaceComparison.Compare(options.SubjectFaces, options.FaceStreetNames);
            string summary =
                $"BBL: {subject.Bbl}\n" +
                $"Zoning: {subject.GetZoningSummary()}\n" +
                $"Lot area: {subject.LotAreaSqFt:0} sf (recorded)\n" +
                $"Drawn geometry source: {DescribeSource(options.GeometrySource)}\n" +
                string.Join("\n", LotDimensionCheck.Describe(dimensions)) + "\n" +
                string.Join("\n", LotFaceComparison.Describe(faces)) + "\n" +
                $"Built FAR: {subject.BuiltFar:0.00}  ·  Res FAR: {subject.ResFar:0.00}  ·  Comm FAR: {subject.CommFar:0.00}" +
                (LotDimensionCheck.AnyDiscrepant(dimensions) || LotFaceComparison.AnyDiscrepant(faces)
                    ? "\nTAX-MAP GEOMETRY IS NOT SURVEY-GRADE — VERIFY AGAINST SURVEY"
                    : string.Empty);

            XYZ origin = new XYZ(
                options.AnchorPointInModel.X,
                options.AnchorPointInModel.Y - 10,
                level.Elevation);

            try
            {
                TextNote note = TextNote.Create(doc, doc.ActiveView.Id, origin, summary, noteType.Id);
                createdIds.Add(note.Id);
                result.Succeeded.Add("Zoning summary table");
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException
                                        || ex is Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                result.Failed.Add(new OperationFailure("Zoning table", ex));
            }
        }

        private static string DescribeSource(LotGeometrySource source) => source switch
        {
            LotGeometrySource.Dtm => "DOF Digital Tax Map polygon (digitized, not survey-grade)",
            LotGeometrySource.TaxMapLegal => "Legal tax-map dimensions on DTM bearings",
            LotGeometrySource.Survey => "Surveyor's property line",
            LotGeometrySource.Manual => "Entered manually",
            _ => source.ToString()
        };

        /// <summary>
        /// The 3D masses go in a group of their own, never in the subject or
        /// context group, so the lines can be moved or hidden without them and
        /// vice versa. Follows the window's grouping mode only for "No grouping".
        /// A mass already in a group (from an earlier run) stays where it is —
        /// Revit cannot put an element in two groups — and is reported.
        /// </summary>
        private static void GroupMasses(Document doc, NycLotDrawOptions options, string subjectBbl,
            List<ElementId> massIds, OperationResult<string> result)
        {
            if (options.GroupingMode == LotGroupingMode.NoGrouping || massIds.Count == 0)
            {
                return;
            }

            var free = new List<ElementId>();
            int alreadyGrouped = 0;
            foreach (ElementId id in massIds.Distinct())
            {
                Element mass = doc.GetElement(id);
                if (mass == null)
                {
                    continue;
                }
                if (mass.GroupId != ElementId.InvalidElementId)
                {
                    alreadyGrouped++;
                    continue;
                }
                free.Add(id);
            }

            if (alreadyGrouped > 0)
            {
                result.Skipped.Add(new OperationSkip("Masses group",
                    $"{alreadyGrouped} mass(es) were already in a group from an earlier run and stay in it"));
            }
            if (free.Count == 0)
            {
                return;
            }

            try
            {
                Autodesk.Revit.DB.Group group = doc.Create.NewGroup(free);
                try
                {
                    group.GroupType.Name = $"BauTools Masses — {subjectBbl}";
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Name taken by an earlier run's group type: Revit's default name stays.
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                result.Skipped.Add(new OperationSkip("Masses group", "could not group — " + ex.Message));
            }
        }

        private static void Group(
            Document doc, NycLotDrawOptions options,
            List<ElementId> subjectIds, List<ElementId> contextIds, OperationResult<string> result)
        {
            if (options.GroupingMode == LotGroupingMode.NoGrouping)
            {
                return;
            }

            void MakeGroup(List<ElementId> ids, string label)
            {
                if (ids.Count == 0)
                {
                    return;
                }
                try
                {
                    doc.Create.NewGroup(ids);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    result.Skipped.Add(new OperationSkip(label, "could not group — " + ex.Message));
                }
            }

            if (options.GroupingMode == LotGroupingMode.SplitSubjectAndContext)
            {
                MakeGroup(subjectIds, "Subject group");
                MakeGroup(contextIds, "Context group");
            }
            else
            {
                MakeGroup(subjectIds.Concat(contextIds).ToList(), "Lot group");
            }
        }
    }
}
