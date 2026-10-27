using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Core.Logging;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Results;
using ARYE.OfficeTools.Core.Zoning;
using ARYE.OfficeTools.Storage;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// Step 2, "Sync Ranges": every copy level of a Typical or Duplex range is
    /// a mirror of its source, on every area scheme that holds the building's
    /// ZFA areas (for example "Gross Building" and "Deductions"). Inspect
    /// reads and measures (no transaction); Sync deletes what sits on each
    /// copy's area plans and copies the source again, then records the source
    /// fingerprint so the next inspection can tell "source changed" from "a
    /// copy was edited by hand".
    ///
    /// What a sync may delete on a copy plan — its scope — is everything
    /// except elements tagged by ANOTHER building. When another building
    /// uses the same area scheme, untagged (hand-drawn) elements are only in
    /// scope inside this building's scope box; without a scope box they are
    /// left alone and the inspection says so.
    /// </summary>
    internal sealed class RangeSyncService
    {
        private const string ZfaCategoryParameterName = "ZFA Category";

        /// <summary>Every sync record is filed under this purpose; the scheme set lives in SchemeKey.</summary>
        private const PropagationPurpose RecordPurpose = PropagationPurpose.Zoning;

        // ── Inspect ─────────────────────────────────────────────────────────

        /// <summary>Reads and measures one range on every ZFA scheme of the building. Never changes the model.</summary>
        public RangeInspection Inspect(Document doc, ZfaProjectModel model, BuildingDefinition building,
            FloorGroup group)
        {
            var inspection = new RangeInspection { GroupId = group.Id, GroupName = group.Name };
            var catalog = new CategoryCatalog(model.Categories);

            List<string> schemeIds = ZfaSchemeResolver.Resolve(model, building);
            if (schemeIds.Count == 0)
            {
                inspection.BlockReason =
                    $"Tick the area schemes that hold {building.Name}'s ZFA areas, at the top of this tab.";
                return inspection;
            }
            List<AreaScheme> schemes = schemeIds.Select(id => ResolveScheme(doc, id)).ToList();
            if (schemes.Any(s => s == null))
            {
                inspection.BlockReason =
                    "One of the ticked area schemes no longer exists in the model. Untick it at the top of this tab.";
                return inspection;
            }
            inspection.SchemeNames.AddRange(schemes.Select(s => s.Name));

            List<SubRange> subs = ResolveSubRanges(doc, building, group, inspection.Notes);
            if (subs.Count == 0)
            {
                inspection.BlockReason = "This range has no copy levels to sync.";
                return inspection;
            }

            foreach (AreaScheme scheme in schemes)
            {
                Scope scope = BuildScope(doc, model, building, scheme, inspection.Notes);
                Dictionary<long, ViewPlan> areaPlans = CollectAreaPlans(doc, scheme);

                foreach (SubRange sub in subs)
                {
                    areaPlans.TryGetValue(sub.Source.Id.Value, out ViewPlan sourceView);
                    if (sourceView == null)
                    {
                        inspection.BlockReason = $"{scheme.Name} has no area plan on the source level "
                            + $"{sub.Source.Name}. Create it and draw its areas, or untick {scheme.Name}.";
                        return inspection;
                    }

                    PlanContent source = ReadPlan(doc, sourceView, scope);
                    var snapshot = new SubRangeSnapshot { Source = ToSnapshot(scheme, sub.Source, source, true) };
                    inspection.SourceAreas.AddRange(source.Areas.Select(a => new SourceAreaInfo
                    {
                        SchemeName = scheme.Name,
                        LevelName = sub.Source.Name,
                        Name = a.Area.Name,
                        Category = ReadZfaCategory(a.Area),
                        SquareFeet = a.Area.Area
                    }));
                    inspection.SourceLineCounts.TryGetValue(scheme.Name, out int lines);
                    inspection.SourceLineCounts[scheme.Name] = lines + source.Lines.Count;

                    foreach (Level target in sub.Targets)
                    {
                        areaPlans.TryGetValue(target.Id.Value, out ViewPlan targetView);
                        PlanContent content = targetView != null ? ReadPlan(doc, targetView, scope) : PlanContent.Empty;
                        snapshot.Targets.Add(ToSnapshot(scheme, target, content, targetView != null));

                        inspection.EarlierCopiesToDelete += content.Areas.Count(a => a.IsTagged)
                            + content.Lines.Count(l => l.IsTagged);
                        int handAreas = content.Areas.Count(a => !a.IsTagged);
                        int handLines = content.Lines.Count(l => !l.IsTagged);
                        if (handAreas + handLines > 0)
                        {
                            inspection.HandDrawnToDelete.Add(new HandDrawnCount
                            {
                                SchemeName = scheme.Name,
                                LevelName = target.Name,
                                Areas = handAreas,
                                Lines = handLines
                            });
                        }
                    }
                    inspection.Snapshots.Add(snapshot);
                }
            }

            PropagationRunRecord lastRun = model.Propagation.FindRecord(building.Id, group.Id, RecordPurpose);
            if (lastRun?.SchemeKey != null && lastRun.SchemeKey != ZfaSchemeResolver.Key(schemeIds))
            {
                // Synced with another set of schemes: that record says nothing about this one.
                lastRun = null;
            }
            inspection.Report = RangeSyncAnalyzer.Analyze(inspection.Snapshots, catalog, lastRun);
            inspection.LastRun = lastRun;
            return inspection;
        }

        // ── Sync ────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds every copy of each range from its source, on every ZFA
        /// scheme, inside ONE TransactionGroup, so a single Undo reverts the
        /// whole sync. A Revit error in one range rolls back that range only
        /// and is reported.
        /// </summary>
        public List<RangeSyncOutcome> Sync(Document doc, ZfaProjectModel model, BuildingDefinition building,
            IReadOnlyList<FloorGroup> groups, ILog log)
        {
            var outcomes = new List<RangeSyncOutcome>();
            string buildingParameter = model.Propagation.BuildingParameterName;
            List<string> schemeIds = ZfaSchemeResolver.Resolve(model, building);

            using (var txGroup = new TransactionGroup(doc,
                groups.Count == 1 ? $"BauTools: Sync {groups[0].Name}" : $"BauTools: Sync {groups.Count} ranges"))
            {
                txGroup.Start();

                foreach (FloorGroup group in groups)
                {
                    var outcome = new RangeSyncOutcome { GroupId = group.Id, GroupName = group.Name };
                    outcomes.Add(outcome);

                    RangeInspection before = Inspect(doc, model, building, group);
                    if (before.BlockReason != null)
                    {
                        outcome.Result.Failed.Add(new OperationFailure(group.Name, before.BlockReason));
                        continue;
                    }

                    DateTime runUtc = DateTime.UtcNow;
                    using (var tx = new Transaction(doc, $"BauTools: Sync {group.Name}"))
                    {
                        tx.Start();
                        var failures = new RevitFailureCollector(outcome.Result, $"sync of {group.Name}");
                        failures.Attach(tx);

                        List<SubRange> subs = ResolveSubRanges(doc, building, group, new List<string>());
                        foreach (AreaScheme scheme in schemeIds.Select(id => ResolveScheme(doc, id)))
                        {
                            Scope scope = BuildScope(doc, model, building, scheme, new List<string>());
                            Dictionary<long, ViewPlan> areaPlans = CollectAreaPlans(doc, scheme);
                            foreach (SubRange sub in subs)
                            {
                                PlanContent source = ReadPlan(doc, areaPlans[sub.Source.Id.Value], scope);
                                foreach (Level target in sub.Targets)
                                {
                                    RebuildCopy(doc, building, group, scheme, areaPlans, scope, sub.Source,
                                        source, target, buildingParameter, runUtc, outcome);
                                }
                            }
                        }

                        if (tx.Commit() != TransactionStatus.Committed)
                        {
                            outcome.Result.Failed.Add(new OperationFailure(group.Name,
                                "Revit rolled the sync back — nothing changed on this range"));
                            continue;
                        }
                    }

                    outcome.Committed = true;
                    // Recorded before the re-inspection, so the state it reports
                    // compares against this sync and not the previous one.
                    model.Propagation.RecordRun(new PropagationRunRecord
                    {
                        BuildingId = building.Id,
                        GroupId = group.Id,
                        Purpose = RecordPurpose,
                        LastRunUtc = runUtc,
                        User = Environment.UserName,
                        FilterDescription = "All",
                        CreatedCount = outcome.AreasCreated,
                        RefreshExisting = true,
                        SourceFingerprint = before.Report.CurrentFingerprint,
                        LevelCount = before.Report.CopyCount,
                        SchemeKey = ZfaSchemeResolver.Key(schemeIds)
                    });

                    // Measure what the model holds now, never what we meant to create.
                    RangeInspection after = Inspect(doc, model, building, group);
                    outcome.After = after;
                    log.Info($"Sync {building.Name}/{group.Name} [{string.Join(", ", before.SchemeNames)}]: "
                        + $"{outcome.PlansRebuilt} plans, {outcome.AreasCreated} areas, "
                        + $"{after.Report?.MismatchCount ?? -1} mismatches");
                }

                if (outcomes.Any(o => o.Committed))
                {
                    // Persist the fingerprints (and the building's scheme list) in the same undo step as the geometry.
                    using (var tx = new Transaction(doc, "BauTools: Record sync"))
                    {
                        tx.Start();
                        new ZfaStorageService().Save(doc, model);
                        tx.Commit();
                    }
                    txGroup.Assimilate();
                }
                else
                {
                    txGroup.RollBack();
                }
            }
            return outcomes;
        }

        private static void RebuildCopy(Document doc, BuildingDefinition building, FloorGroup group,
            AreaScheme scheme, Dictionary<long, ViewPlan> areaPlans, Scope scope, Level sourceLevel,
            PlanContent source, Level target, string buildingParameter, DateTime runUtc, RangeSyncOutcome outcome)
        {
            OperationResult<string> result = outcome.Result;
            areaPlans.TryGetValue(target.Id.Value, out ViewPlan targetView);
            if (targetView == null)
            {
                targetView = ViewPlan.CreateAreaPlan(doc, scheme.Id, target.Id);
                TrySetViewName(targetView, $"{scheme.Name} - {building.Name} - {target.Name}", result);
                areaPlans[target.Id.Value] = targetView;
            }
            else
            {
                PlanContent existing = ReadPlan(doc, targetView, scope);
                List<ElementId> doomed = existing.Areas.Select(a => a.Area.Id)
                    .Concat(existing.Lines.Select(l => l.Line.Id))
                    .ToList();
                if (doomed.Count > 0)
                {
                    doc.Delete(doomed);
                }
            }

            double deltaZ = target.Elevation - sourceLevel.Elevation;
            Transform shift = Transform.CreateTranslation(new XYZ(0, 0, deltaZ));
            SketchPlane sketchPlane = SketchPlane.Create(doc,
                Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, target.Elevation)));

            foreach (TaggedLine line in source.Lines)
            {
                try
                {
                    ModelCurve copy = doc.Create.NewAreaBoundaryLine(
                        sketchPlane, line.Line.GeometryCurve.CreateTransformed(shift), targetView);
                    PropagatedElementTag.Write(copy, line.Line.UniqueId, building.Id, group.Id, runUtc);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    result.Failed.Add(new OperationFailure($"Boundary line on {target.Name} ({scheme.Name})", ex));
                }
            }

            foreach (TaggedArea sourceArea in source.Areas.Where(a => a.Area.Area > 0))
            {
                var location = (LocationPoint)sourceArea.Area.Location;
                string subject = $"Area '{sourceArea.Area.Name}' on {target.Name} ({scheme.Name})";
                try
                {
                    Area created = doc.Create.NewArea(targetView, new UV(location.Point.X, location.Point.Y));
                    created.Name = sourceArea.Area.Name;
                    CopyZfaCategory(sourceArea.Area, created);
                    StampBuildingParameter(created, buildingParameter, building.Name, result, subject);
                    PropagatedElementTag.Write(created, sourceArea.Area.UniqueId, building.Id, group.Id, runUtc);
                    outcome.AreasCreated++;
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    result.Failed.Add(new OperationFailure(subject, ex));
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    result.Failed.Add(new OperationFailure(subject, ex));
                }
            }

            outcome.PlansRebuilt++;
            result.Succeeded.Add($"{target.Name} ({scheme.Name})");
        }

        // ── Scope ───────────────────────────────────────────────────────────

        /// <summary>Which elements of a plan belong to this building.</summary>
        private sealed class Scope
        {
            public Guid BuildingId;

            /// <summary>When set, untagged elements count only inside this plan box.</summary>
            public BoundingBoxXYZ Box;

            /// <summary>Shared scheme and no scope box: untagged elements are never in scope.</summary>
            public bool TaggedOnly;

            public bool Includes(Element element, XYZ point)
            {
                if (PropagatedElementTag.TryRead(element, out _, out Guid buildingId, out _))
                {
                    return buildingId == BuildingId;
                }
                if (TaggedOnly)
                {
                    return false;
                }
                if (Box == null || point == null)
                {
                    return true;
                }
                return point.X >= Box.Min.X && point.X <= Box.Max.X
                    && point.Y >= Box.Min.Y && point.Y <= Box.Max.Y;
            }
        }

        private static Scope BuildScope(Document doc, ZfaProjectModel model, BuildingDefinition building,
            AreaScheme scheme, List<string> notes)
        {
            var scope = new Scope { BuildingId = building.Id };
            List<string> sharing = model.Buildings
                .Where(b => b.Id != building.Id && ZfaSchemeResolver.Resolve(model, b).Contains(scheme.UniqueId))
                .Select(b => b.Name)
                .ToList();
            if (sharing.Count == 0)
            {
                return scope;
            }

            Element scopeBox = string.IsNullOrEmpty(building.ScopeBoxUniqueId)
                ? null
                : doc.GetElement(building.ScopeBoxUniqueId);
            BoundingBoxXYZ box = scopeBox?.get_BoundingBox(null);
            if (box != null)
            {
                scope.Box = box;
                notes.Add($"{scheme.Name} is shared with {string.Join(", ", sharing)}: hand-drawn areas count "
                    + $"for {building.Name} only inside its scope box.");
            }
            else
            {
                scope.TaggedOnly = true;
                notes.Add($"{scheme.Name} is shared with {string.Join(", ", sharing)} and {building.Name} has no "
                    + "scope box, so hand-drawn areas on the copies are left alone. Set a scope box in Step 1.");
            }
            return scope;
        }

        // ── Reading plans ───────────────────────────────────────────────────

        private sealed class TaggedArea
        {
            public Area Area;
            public bool IsTagged;
        }

        private sealed class TaggedLine
        {
            public CurveElement Line;
            public bool IsTagged;
        }

        private sealed class PlanContent
        {
            public static readonly PlanContent Empty = new PlanContent();

            public readonly List<TaggedArea> Areas = new List<TaggedArea>();
            public readonly List<TaggedLine> Lines = new List<TaggedLine>();
        }

        private static PlanContent ReadPlan(Document doc, ViewPlan view, Scope scope)
        {
            var content = new PlanContent();
            foreach (Area area in new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Areas)
                .WhereElementIsNotElementType()
                .OfType<Area>())
            {
                XYZ point = (area.Location as LocationPoint)?.Point;
                if (scope.Includes(area, point))
                {
                    content.Areas.Add(new TaggedArea { Area = area, IsTagged = PropagatedElementTag.IsTagged(area) });
                }
            }
            foreach (CurveElement line in new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_AreaSchemeLines)
                .WhereElementIsNotElementType()
                .OfType<CurveElement>())
            {
                Curve curve = line.GeometryCurve;
                if (curve == null)
                {
                    continue;
                }
                if (scope.Includes(line, curve.Evaluate(0.5, true)))
                {
                    content.Lines.Add(new TaggedLine { Line = line, IsTagged = PropagatedElementTag.IsTagged(line) });
                }
            }
            return content;
        }

        private static LevelSnapshot ToSnapshot(AreaScheme scheme, Level level, PlanContent content, bool hasPlan)
        {
            return new LevelSnapshot
            {
                SchemeName = scheme.Name,
                LevelUniqueId = level.UniqueId,
                LevelName = level.Name,
                HasAreaPlan = hasPlan,
                HandDrawnAreaCount = content.Areas.Count(a => !a.IsTagged),
                Areas = content.Areas.Select(a => new AreaSignature
                {
                    Name = a.Area.Name,
                    Category = ReadZfaCategory(a.Area),
                    SquareFeet = a.Area.Area
                }).ToList(),
                Lines = content.Lines.Select(l =>
                {
                    Curve c = l.Line.GeometryCurve;
                    XYZ p0 = c.GetEndPoint(0);
                    XYZ pm = c.Evaluate(0.5, true);
                    XYZ p1 = c.GetEndPoint(1);
                    return new LineSignature { X1 = p0.X, Y1 = p0.Y, Xm = pm.X, Ym = pm.Y, X2 = p1.X, Y2 = p1.Y };
                }).ToList()
            };
        }

        // ── Range resolution ────────────────────────────────────────────────

        private sealed class SubRange
        {
            public Level Source;
            public readonly List<Level> Targets = new List<Level>();
        }

        /// <summary>Standard: one source → its targets. Duplex: lower source → pair lowers, upper source → pair uppers.</summary>
        private static List<SubRange> ResolveSubRanges(Document doc, BuildingDefinition building,
            FloorGroup group, List<string> notes)
        {
            var subs = new List<SubRange>();
            var atypical = new HashSet<string>(building.AtypicalLevels.Select(a => a.UniqueId));

            if (group.Kind == FloorGroupKind.Standard)
            {
                AddSub(doc, subs, notes, atypical, group.SourceLevel, group.TargetLevels);
            }
            else if (group.Kind == FloorGroupKind.Duplex)
            {
                AddSub(doc, subs, notes, atypical, group.SourceLevel,
                    group.TargetPairs.Select(p => p.Lower).ToList());
                AddSub(doc, subs, notes, atypical, group.SourceUpperLevel,
                    group.TargetPairs.Select(p => p.Upper).ToList());
            }
            return subs;
        }

        private static void AddSub(Document doc, List<SubRange> subs, List<string> notes,
            HashSet<string> atypical, LevelRef sourceRef, List<LevelRef> targetRefs)
        {
            if (sourceRef == null)
            {
                notes.Add("The range has no source level. Set it in Step 1.");
                return;
            }
            if (!(doc.GetElement(sourceRef.UniqueId) is Level source))
            {
                notes.Add($"Source level '{sourceRef.Name}' no longer exists in the model.");
                return;
            }

            var sub = new SubRange { Source = source };
            foreach (LevelRef targetRef in targetRefs.Where(t => t != null))
            {
                if (atypical.Contains(targetRef.UniqueId))
                {
                    notes.Add($"{targetRef.Name} is marked atypical and is not synced.");
                    continue;
                }
                if (targetRef.UniqueId == sourceRef.UniqueId)
                {
                    continue;
                }
                if (!(doc.GetElement(targetRef.UniqueId) is Level target))
                {
                    notes.Add($"Level '{targetRef.Name}' no longer exists in the model.");
                    continue;
                }
                sub.Targets.Add(target);
            }
            if (sub.Targets.Count > 0)
            {
                subs.Add(sub);
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static AreaScheme ResolveScheme(Document doc, string schemeUniqueId) =>
            string.IsNullOrEmpty(schemeUniqueId) ? null : doc.GetElement(schemeUniqueId) as AreaScheme;

        /// <summary>All area plans of the scheme, keyed by level id; the first plan per level wins.</summary>
        private static Dictionary<long, ViewPlan> CollectAreaPlans(Document doc, AreaScheme scheme)
        {
            var plans = new Dictionary<long, ViewPlan>();
            foreach (ViewPlan view in new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate
                            && v.ViewType == ViewType.AreaPlan
                            && v.GenLevel != null
                            && v.AreaScheme != null
                            && v.AreaScheme.Id == scheme.Id))
            {
                long levelId = view.GenLevel.Id.Value;
                if (!plans.ContainsKey(levelId))
                {
                    plans[levelId] = view;
                }
            }
            return plans;
        }

        private static void TrySetViewName(ViewPlan view, string desiredName, OperationResult<string> result)
        {
            try
            {
                view.Name = desiredName;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                result.Skipped.Add(new OperationSkip(desiredName,
                    $"view name taken — the new area plan keeps its default name '{view.Name}'"));
            }
        }

        private static string ReadZfaCategory(Area area)
        {
            Parameter parameter = area.LookupParameter(ZfaCategoryParameterName);
            return parameter != null && parameter.StorageType == StorageType.String ? parameter.AsString() : null;
        }

        private static void CopyZfaCategory(Area source, Area created)
        {
            string value = ReadZfaCategory(source);
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            Parameter parameter = created.LookupParameter(ZfaCategoryParameterName);
            if (parameter != null && !parameter.IsReadOnly)
            {
                parameter.Set(value);
            }
        }

        private static void StampBuildingParameter(Area created, string parameterName, string buildingName,
            OperationResult<string> result, string subject)
        {
            if (string.IsNullOrEmpty(parameterName))
            {
                return;
            }
            Parameter parameter = created.LookupParameter(parameterName);
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
            {
                result.Skipped.Add(new OperationSkip(subject,
                    $"building parameter '{parameterName}' is missing or not a writable text parameter — not stamped"));
                return;
            }
            parameter.Set(buildingName);
        }
    }

    /// <summary>What Inspect found for one range — plain data the ViewModel binds against.</summary>
    internal sealed class RangeInspection
    {
        public Guid GroupId { get; set; }

        public string GroupName { get; set; }

        /// <summary>The ZFA schemes inspected, in the building's order.</summary>
        public List<string> SchemeNames { get; } = new List<string>();

        /// <summary>Why the range cannot be synced at all; null when it can.</summary>
        public string BlockReason { get; set; }

        public RangeSyncReport Report { get; set; }

        public PropagationRunRecord LastRun { get; set; }

        public List<SubRangeSnapshot> Snapshots { get; } = new List<SubRangeSnapshot>();

        public List<SourceAreaInfo> SourceAreas { get; } = new List<SourceAreaInfo>();

        /// <summary>Boundary lines on the source plan(s), per scheme name.</summary>
        public Dictionary<string, int> SourceLineCounts { get; } = new Dictionary<string, int>();

        public int SourceLineCount => SourceLineCounts.Values.Sum();

        /// <summary>Tagged areas and lines on the copies that a sync replaces.</summary>
        public int EarlierCopiesToDelete { get; set; }

        /// <summary>Untagged elements in scope on the copies, per scheme and level — deleted by a sync, listed before it runs.</summary>
        public List<HandDrawnCount> HandDrawnToDelete { get; } = new List<HandDrawnCount>();

        public List<string> Notes { get; } = new List<string>();
    }

    internal sealed class SourceAreaInfo
    {
        public string SchemeName { get; set; }

        public string LevelName { get; set; }

        public string Name { get; set; }

        public string Category { get; set; }

        public double SquareFeet { get; set; }
    }

    internal sealed class HandDrawnCount
    {
        public string SchemeName { get; set; }

        public string LevelName { get; set; }

        public int Areas { get; set; }

        public int Lines { get; set; }
    }

    internal sealed class RangeSyncOutcome
    {
        public Guid GroupId { get; set; }

        public string GroupName { get; set; }

        public bool Committed { get; set; }

        /// <summary>Copy plans rebuilt — one per copy level per scheme.</summary>
        public int PlansRebuilt { get; set; }

        public int AreasCreated { get; set; }

        /// <summary>Re-inspection after the commit — the measured truth.</summary>
        public RangeInspection After { get; set; }

        /// <summary>Succeeded = "LEVEL (Scheme)" plans rebuilt; skips and failures carry their reason.</summary>
        public OperationResult<string> Result { get; } = new OperationResult<string>();
    }
}
