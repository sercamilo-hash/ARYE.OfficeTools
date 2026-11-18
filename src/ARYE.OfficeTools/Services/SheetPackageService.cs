using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Core.Levels;
using ARYE.OfficeTools.Core.Logging;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Results;
using ARYE.OfficeTools.Core.Sheets;
using ARYE.OfficeTools.Storage;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// Sheet Diagrammer — sheet packages ("sets"). One package × one building
    /// is one run, and a run is a diff, never a rebuild:
    ///
    ///   • Every view and every sheet the generator creates carries the
    ///     Extensible Storage tag (BuildingId = building, GroupId = package,
    ///     SourceUniqueId = <see cref="SheetTagIdentity"/>). That tag is the
    ///     only thing that makes an element "ours".
    ///   • A re-run reads the tags back, so an existing view is recognised by
    ///     identity — not by name — and is updated in place. Nothing is
    ///     duplicated and nothing is moved.
    ///   • Untagged (foreign) views, sheets and viewports are never modified,
    ///     never moved, never deleted. Name/number collisions with them are
    ///     skipped and reported.
    ///   • New views only ever fill EMPTY slots of the package's last matrix
    ///     sheet, then continue onto new sheets. An occupied slot is never reused.
    ///   • A run only draws the levels the building has assigned to a Floor
    ///     Range in the ZFA tool (typical, duplex or single floor). A level with
    ///     a source view but no range is skipped and reported.
    ///   • Additional existing views (sections, 3D, legends, schedules) are
    ///     placed as they are — never duplicated, never restyled — either on a
    ///     dedicated 1×1 sheet or in the next free matrix slot.
    ///   • Sheets join the package's Sheet Collection (created on demand) and
    ///     sheets/views receive the package's browser-organization values.
    ///
    /// Counts are true: every view of the run ends in Succeeded, Skipped or
    /// Failed, and nothing is dropped silently.
    /// </summary>
    internal sealed partial class SheetPackageService
    {
        /// <summary>
        /// Every level the building's Floor Ranges cover, by level UniqueId:
        /// the union of each group's SourceLevel, SourceUpperLevel,
        /// TargetLevels and TargetPairs (lower + upper), plus the Single Floors
        /// stored in AtypicalLevels. Empty when the building has no ranges.
        /// <paramref name="sourceOnlyForRanges"/>: when true, a Standard or
        /// Duplex group contributes only its source level(s) — the one view a
        /// range needs to represent every identical target — instead of every
        /// member. Single floors (Atypical groups and AtypicalLevels) are
        /// unaffected either way: they have no propagation targets to begin with.
        /// </summary>
        public static HashSet<string> CollectFloorRangeLevelIds(BuildingDefinition building, bool sourceOnlyForRanges = false)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (building == null)
            {
                return ids;
            }
            foreach (FloorGroup group in building.Groups ?? new List<FloorGroup>())
            {
                if (group == null)
                {
                    continue;
                }
                IEnumerable<LevelRef> members = sourceOnlyForRanges
                    ? FloorRangeMath.CollectSourceMembers(group)
                    : group.Kind == FloorGroupKind.Duplex
                        ? FloorRangeMath.CollectDuplexMembers(group)
                        : FloorRangeMath.CollectStandardMembers(group);
                foreach (LevelRef level in members)
                {
                    AddLevelId(ids, level);
                }
            }
            foreach (LevelRef level in building.AtypicalLevels ?? new List<LevelRef>())
            {
                AddLevelId(ids, level);
            }
            return ids;
        }

        /// <summary>
        /// "TYPICAL {first} - {last} FLOOR" per source level, keyed by that
        /// source level's UniqueId — the title a range's one diagrammed view
        /// gets when the set draws source levels only. A Duplex group's two
        /// source levels (lower + upper — physically different plans) share
        /// the same span, suffixed so the two sheets read as a pair, not a
        /// duplicate. A one-level "range" (source with no targets) gets no
        /// label — its own level name already says everything.
        /// </summary>
        private static Dictionary<string, string> CollectRangeLabels(BuildingDefinition building)
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            if (building == null)
            {
                return labels;
            }
            foreach (FloorGroup group in building.Groups ?? new List<FloorGroup>())
            {
                if (group == null || group.Kind == FloorGroupKind.Atypical)
                {
                    continue;
                }
                bool duplex = group.Kind == FloorGroupKind.Duplex;
                List<LevelRef> allMembers = duplex ? FloorRangeMath.CollectDuplexMembers(group) : FloorRangeMath.CollectStandardMembers(group);
                if (allMembers.Count <= 1)
                {
                    continue;
                }
                string span = $"TYPICAL {allMembers.First().Name} - {allMembers.Last().Name} FLOOR";
                List<LevelRef> sourceMembers = FloorRangeMath.CollectSourceMembers(group);
                for (int i = 0; i < sourceMembers.Count; i++)
                {
                    LevelRef source = sourceMembers[i];
                    if (source == null || string.IsNullOrEmpty(source.UniqueId))
                    {
                        continue;
                    }
                    labels[source.UniqueId] = duplex && sourceMembers.Count == 2
                        ? $"{span} — {(i == 0 ? "LOWER" : "UPPER")}"
                        : span;
                }
            }
            return labels;
        }

        /// <summary>
        /// "3RD TO 7TH FL." per source level of a Typical/Duplex range, keyed by that level's UniqueId — the
        /// level part of the name of the one view that stands for the range. A Duplex pair's two source levels
        /// get "LOWER" / "UPPER" appended. A one-level "range" gets no entry: it is named like any single level.
        /// </summary>
        private static Dictionary<string, string> CollectRangeViewLabels(BuildingDefinition building)
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (FloorGroup group in building?.Groups ?? new List<FloorGroup>())
            {
                if (group == null || group.Kind == FloorGroupKind.Atypical)
                {
                    continue;
                }
                bool duplex = group.Kind == FloorGroupKind.Duplex;
                List<LevelRef> members = duplex ? FloorRangeMath.CollectDuplexMembers(group) : FloorRangeMath.CollectStandardMembers(group);
                if (members.Count <= 1)
                {
                    continue;
                }
                string span = SheetNaming.RangeLabel(members.First().Name, members.Last().Name);
                List<LevelRef> sources = FloorRangeMath.CollectSourceMembers(group);
                for (int i = 0; i < sources.Count; i++)
                {
                    if (sources[i] == null || string.IsNullOrEmpty(sources[i].UniqueId))
                    {
                        continue;
                    }
                    labels[sources[i].UniqueId] = duplex && sources.Count == 2 ? $"{span} {(i == 0 ? "LOWER" : "UPPER")}" : span;
                }
            }
            return labels;
        }

        private static void AddLevelId(HashSet<string> ids, LevelRef level)
        {
            if (level != null && !string.IsNullOrEmpty(level.UniqueId))
            {
                ids.Add(level.UniqueId);
            }
        }

        /// <summary>The block shown when the selected building has no Floor Range at all.</summary>
        public static string NoFloorRangesReason(BuildingDefinition building) =>
            "Define Floor Ranges in Zoning Floor Area › Step 1 first — a run only draws the levels this building has "
            + $"assigned to a Floor Range (typical, duplex or single floor), and '{building?.Name}' has none.";

        /// <summary>Label of a slot with nothing in it, in the preview mini-sheets.</summary>
        public const string EmptySlotLabel = "empty";

        /// <summary>Viewport centres closer than this in Y count as the same slot row.</summary>
        private const double RowToleranceFt = 0.05;

        private readonly ZfaStorageService _storage = new ZfaStorageService();

        // ── Preview: read-only, no transaction ───────────────────────────────

        public SheetPackagePreview BuildPreview(Document doc, SheetPackage package,
            BuildingDefinition building, ZfaProjectModel model, ILog log)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (building == null) throw new ArgumentNullException(nameof(building));
            if (model == null) throw new ArgumentNullException(nameof(model));

            var preview = new SheetPackagePreview();
            RunPlan plan = Resolve(doc, package, building, model);

            preview.BlockReason = plan.BlockReason;
            preview.ScaleDenominator = plan.Scale;
            preview.ScaleIsOverride = plan.ScaleIsOverride;
            preview.ScaleIsFromTemplate = plan.ScaleIsFromTemplate;
            preview.SheetCollectionName = plan.CollectionName ?? string.Empty;
            preview.SheetCollectionWillBeCreated = plan.CollectionWillBeCreated;
            foreach (string note in plan.Notes)
            {
                preview.Notes.Add(note);
            }

            if (plan.Diff == null)
            {
                return preview;
            }

            preview.NewViewCount = plan.Diff.CreateKeys.Count;
            preview.UpdateViewCount = plan.Diff.UpdateKeys.Count;
            preview.KeptViewCount = plan.Diff.OrphanKeys.Count;
            preview.ViewCount = preview.NewViewCount + preview.UpdateViewCount + preview.KeptViewCount;
            preview.AdditionalToPlaceCount = plan.ExtrasShared.Count + plan.ExtrasOwn.Count;
            preview.AdditionalPlacedCount = plan.ExtrasAlreadyPlaced.Count;

            string planKind = package.SourceKind == SheetSourceKind.AreaPlans ? "area plan" : package.SourceKind == SheetSourceKind.CeilingPlans ? "ceiling plan" : "floor plan";
            foreach (string key in plan.DesiredKeys)
            {
                bool owned = plan.TaggedViews.ContainsKey(key);
                string finalName = plan.ViewNameFor(key, building.Name, owned ? plan.TaggedViews[key] : null);
                preview.ViewRows.Add(new SheetPreviewViewRow
                {
                    ViewName = finalName,
                    LevelName = finalName,
                    Status = owned ? SheetPreviewViewRow.StatusUpdate : SheetPreviewViewRow.StatusNew,
                    Detail = owned && !string.Equals(plan.TaggedViews[key].Name, finalName, StringComparison.Ordinal)
                        ? $"renamed from '{plan.TaggedViews[key].Name}'"
                        : !owned && plan.CreatedFromScratchKeys.Contains(key)
                        ? (package.SourceKind == SheetSourceKind.AreaPlans
                            ? "no area plan for this scheme on this level — an empty one is created and placed; fill it with ZFA Step 2"
                            : $"no {planKind} exists on this level — a new one is created and placed in a free slot")
                        : null
                });
            }
            foreach (string key in plan.Diff.OrphanKeys)
            {
                preview.ViewRows.Add(new SheetPreviewViewRow
                {
                    ViewName = plan.TaggedViews.TryGetValue(key, out View orphan) ? orphan.Name : key,
                    LevelName = plan.LevelName(key),
                    Status = SheetPreviewViewRow.StatusKept
                });
            }
            foreach (ExtraView extra in plan.ExtrasShared.Concat(plan.ExtrasOwn))
            {
                preview.ViewRows.Add(new SheetPreviewViewRow
                {
                    ViewName = extra.View.Name,
                    LevelName = extra.View.Name,
                    Status = SheetPreviewViewRow.StatusNew,
                    IsAdditional = true,
                    Detail = extra.Ref.OwnSheet
                        ? $"{extra.TypeName} — placed as-is on its own sheet"
                        : $"{extra.TypeName} — placed as-is in the next free slot"
                });
            }
            foreach (ExtraView extra in plan.ExtrasAlreadyPlaced)
            {
                preview.ViewRows.Add(new SheetPreviewViewRow
                {
                    ViewName = extra.View.Name,
                    LevelName = extra.View.Name,
                    Status = SheetPreviewViewRow.StatusKept,
                    IsAdditional = true,
                    Detail = $"{extra.TypeName} — already on sheet {extra.OnSheetNumber}, left as it is"
                });
            }

            if (plan.Assignments == null || plan.Numbers == null)
            {
                return preview;
            }

            preview.NewSheetCount = plan.NewSheetCount;
            preview.SheetCount = plan.PackageSheets.Count + plan.OwnSheets.Count + plan.NewSheetCount;

            BuildPreviewSheets(doc, package, plan, preview);
            BuildContentCheck(doc, package, building, plan, preview);
            log?.Info($"Sheet package preview '{package.Name}' / {building.Name}: "
                + $"{preview.NewViewCount} new, {preview.UpdateViewCount} update, {preview.KeptViewCount} kept, "
                + $"{preview.AdditionalToPlaceCount} additional, {preview.SheetCount} sheet(s), 1/{preview.ScaleDenominator}.");
            return preview;
        }

        // ── Generate: one transaction ────────────────────────────────────────

        public OperationResult<PlacedViewportInfo> Generate(Document doc, SheetPackage package,
            BuildingDefinition building, ZfaProjectModel model, ILog log)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (building == null) throw new ArgumentNullException(nameof(building));
            if (model == null) throw new ArgumentNullException(nameof(model));

            var result = new OperationResult<PlacedViewportInfo>();
            RunPlan plan = Resolve(doc, package, building, model);
            if (plan.BlockReason != null)
            {
                result.Failed.Add(new OperationFailure(package.Name ?? "package", plan.BlockReason));
                return result;
            }

            DateTime runUtc = DateTime.UtcNow;
            var stringResult = new OperationResult<string>(); // the failure collector's channel

            using (var tx = new Transaction(doc, $"BauTools: Sheet package '{package.Name}' ({building.Name})"))
            {
                tx.Start();
                var failures = new RevitFailureCollector(stringResult, "sheet generation");
                failures.Attach(tx);

                if (plan.TemplateControlsScale && plan.ScaleIsOverride)
                {
                    result.Skipped.Add(new OperationSkip("View template '" + plan.Template.Name + "'",
                        $"the template controls View Scale — the run's 1/{plan.Scale} was not applied"));
                }
                if (plan.CoveredLevelNames.Count > 0)
                {
                    result.Skipped.Add(new OperationSkip($"{plan.CoveredLevelNames.Count} level(s) of Typical/Duplex ranges",
                        "covered by their range's source view — no separate view generated"));
                }
                foreach (string levelName in plan.SkippedLevelNames)
                {
                    result.Skipped.Add(new OperationSkip("Level '" + levelName + "'",
                        $"not in any Floor Range of '{building.Name}' — no view generated"));
                }
                foreach (string note in plan.ExtraSkips)
                {
                    result.Skipped.Add(new OperationSkip("Additional view", note));
                }
                if (package.SourceKind == SheetSourceKind.AreaPlans && plan.CreatedFromScratchKeys.Count > 0)
                {
                    result.Skipped.Add(new OperationSkip($"{plan.CreatedFromScratchKeys.Count} new area plan(s)",
                        "created EMPTY (no areas yet) — run Zoning Floor Area › Step 2 to propagate areas into them"));
                }

                // ── Sheet collection ─────────────────────────────────────────
                SheetCollection collection = ResolveOrCreateCollection(doc, plan, result);

                // ── UPDATE: existing tagged views, in place ──────────────────
                foreach (string key in plan.Diff.UpdateKeys)
                {
                    View view = plan.TaggedViews[key];
                    try
                    {
                        RenameOwnedView(view, plan.ViewNameFor(key, building.Name, view), plan.LevelName(key), plan, result);
                        ApplyTemplateAndScale(view, plan, result);
                        if (package.ReapplyScopeBoxOnUpdate)
                        {
                            ApplyScopeBox(view, plan.ScopeBox);
                        }
                        ApplyParameterValues(view, package.ViewParameterValues, result);
                        result.Succeeded.Add(new PlacedViewportInfo
                        {
                            ViewName = view.Name, SheetNumber = DescribeSheetOf(doc, view), ScaleDenominator = view.Scale
                        });
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        result.Failed.Add(new OperationFailure(view.Name, ex));
                    }
                }

                // ── KEEP: tagged views no longer in the source set ───────────
                foreach (string key in plan.Diff.OrphanKeys)
                {
                    View orphan = plan.TaggedViews[key];
                    result.Skipped.Add(new OperationSkip(orphan.Name,
                        "its level is no longer a source of this package — kept exactly as it is"));
                }
                foreach (ExtraView extra in plan.ExtrasAlreadyPlaced)
                {
                    result.Skipped.Add(new OperationSkip(extra.View.Name,
                        $"already on sheet {extra.OnSheetNumber} of this package — left as it is"));
                }

                // ── Existing package sheets: collection + parameters ─────────
                foreach (ViewSheet sheet in plan.PackageSheets.Concat(plan.OwnSheets))
                {
                    ApplyCollection(sheet, collection, result);
                    ApplyParameterValues(sheet, package.SheetParameterValues, result);
                }

                // ── CREATE: the missing views ────────────────────────────────
                var created = new List<CreatedView>();
                foreach (string key in plan.Diff.CreateKeys)
                {
                    string levelName = plan.LevelName(key);
                    string viewName = plan.ViewNameFor(key, building.Name);
                    if (plan.ExistingViewNames.Contains(viewName))
                    {
                        result.Skipped.Add(new OperationSkip(viewName,
                            "a view with this name already exists and is not owned by this package — not touched"));
                        created.Add(null);
                        continue;
                    }
                    try
                    {
                        View view;
                        if (plan.SourceViews.TryGetValue(key, out ViewPlan source))
                        {
                            view = (View)doc.GetElement(source.Duplicate(ViewDuplicateOption.WithDetailing));
                        }
                        else if (package.SourceKind == SheetSourceKind.AreaPlans && plan.Scheme != null && doc.GetElement(key) is Level areaLevel)
                        {
                            // No area plan to duplicate: the plan itself is created, empty,
                            // for the user to draw areas into by hand.
                            view = ViewPlan.CreateAreaPlan(doc, plan.Scheme.Id, areaLevel.Id);
                        }
                        else if (package.SourceKind != SheetSourceKind.AreaPlans && plan.PlanViewFamilyTypeId != null && doc.GetElement(key) is Level planLevel)
                        {
                            // No floor/ceiling plan on this level: create one of the same view type.
                            view = ViewPlan.Create(doc, plan.PlanViewFamilyTypeId, planLevel.Id);
                        }
                        else
                        {
                            result.Failed.Add(new OperationFailure(viewName,
                                "no source view exists for this level and no view type to create one from"));
                            created.Add(null);
                            continue;
                        }
                        view.Name = viewName;
                        plan.ExistingViewNames.Add(viewName);

                        ApplyScopeBox(view, plan.ScopeBox);
                        ApplyTemplateAndScale(view, plan, result);
                        SetTitleOnSheet(view, viewName);
                        ApplyParameterValues(view, package.ViewParameterValues, result);

                        PropagatedElementTag.Write(view,
                            SheetTagIdentity.ComposeView(package.SourceKind, plan.SchemeUniqueId, key),
                            building.Id, package.Id, runUtc);

                        created.Add(new CreatedView { View = view, LevelName = levelName });
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        result.Failed.Add(new OperationFailure(viewName, ex));
                        created.Add(null);
                    }
                }

                // ── MATRIX SHEETS: fill the last sheet's free slots, then add sheets ─
                var newSheets = new List<ViewSheet>();
                for (int ordinal = 0; ordinal < plan.MatrixNewSheetCount; ordinal++)
                {
                    string number = plan.Numbers.Numbers[ordinal];
                    ViewSheet sheet = CreateSheet(doc, plan, number, result);
                    if (sheet != null)
                    {
                        sheet.Name = ComposeNewSheetName(package, plan, created, ordinal);
                        PropagatedElementTag.Write(sheet, SheetTagIdentity.ComposeSheet(plan.NextSheetIndex + ordinal),
                            building.Id, package.Id, runUtc);
                        ApplyCollection(sheet, collection, result);
                        ApplyParameterValues(sheet, package.SheetParameterValues, result);
                    }
                    newSheets.Add(sheet);
                }

                // ── OWN SHEETS: one per additional view flagged "own sheet" ──
                var ownSheets = new List<ViewSheet>();
                for (int i = 0; i < plan.ExtrasOwn.Count; i++)
                {
                    int ordinal = plan.MatrixNewSheetCount + i;
                    string number = ordinal < plan.Numbers.Numbers.Count ? plan.Numbers.Numbers[ordinal] : null;
                    ViewSheet sheet = number == null ? null : CreateSheet(doc, plan, number, result);
                    if (sheet != null)
                    {
                        sheet.Name = ComposeOwnSheetName(package, plan.ExtrasOwn[i].View);
                        PropagatedElementTag.Write(sheet,
                            SheetTagIdentity.ComposeOwnSheet(plan.NextSheetIndex + ordinal, plan.ExtrasOwn[i].View.UniqueId),
                            building.Id, package.Id, runUtc);
                        ApplyCollection(sheet, collection, result);
                        ApplyParameterValues(sheet, package.SheetParameterValues, result);
                    }
                    ownSheets.Add(sheet);
                }

                // Titleblock instances must exist before their extents are read.
                doc.Regenerate();

                // Plan views → matrix slots.
                for (int i = 0; i < created.Count; i++)
                {
                    CreatedView entry = created[i];
                    if (entry == null)
                    {
                        continue;
                    }
                    ViewSheet target = TargetSheet(plan, newSheets, i);
                    if (target == null)
                    {
                        result.Failed.Add(new OperationFailure(entry.View.Name,
                            "its sheet could not be created — the view exists but is not placed"));
                        continue;
                    }
                    Place(doc, target, entry.View, plan.Assignments[i].SlotIndex, plan.Slots, plan, result);
                }
                // Owned views this set already had but that had no slot yet →
                // the next free ones, right after the newly created views.
                for (int u = 0; u < plan.UnplacedUpdateKeys.Count; u++)
                {
                    View view = plan.TaggedViews[plan.UnplacedUpdateKeys[u]];
                    int index = created.Count + u;
                    ViewSheet target = TargetSheet(plan, newSheets, index);
                    if (target == null)
                    {
                        result.Failed.Add(new OperationFailure(view.Name,
                            "its sheet could not be created — the view exists but is not placed"));
                        continue;
                    }
                    Place(doc, target, view, plan.Assignments[index].SlotIndex, plan.Slots, plan, result);
                }
                // Shared additional views → the slots after those.
                for (int j = 0; j < plan.ExtrasShared.Count; j++)
                {
                    int index = created.Count + plan.UnplacedUpdateKeys.Count + j;
                    ViewSheet target = TargetSheet(plan, newSheets, index);
                    if (target == null)
                    {
                        result.Failed.Add(new OperationFailure(plan.ExtrasShared[j].View.Name,
                            "its sheet could not be created — the view is not placed"));
                        continue;
                    }
                    Place(doc, target, plan.ExtrasShared[j].View, plan.Assignments[index].SlotIndex, plan.Slots, plan, result);
                }
                // Own-sheet additional views → slot 0 of their 1×1 sheet.
                for (int i = 0; i < plan.ExtrasOwn.Count; i++)
                {
                    if (ownSheets[i] == null)
                    {
                        result.Failed.Add(new OperationFailure(plan.ExtrasOwn[i].View.Name,
                            "its own sheet could not be created — the view is not placed"));
                        continue;
                    }
                    Place(doc, ownSheets[i], plan.ExtrasOwn[i].View, 0, plan.OwnSheetSlots, plan, result);
                }

                // ── Views this set owns that a saved position was holding off-center ─
                ReleaseOwnedSavedPositions(doc, plan, result);

                // ── Area tables, next to the views they belong to ────────────
                var viewOf = new Dictionary<string, View>(StringComparer.Ordinal);
                for (int i = 0; i < created.Count && i < plan.Diff.CreateKeys.Count; i++)
                {
                    if (created[i] != null) viewOf[plan.Diff.CreateKeys[i]] = created[i].View;
                }
                foreach (string key in plan.Diff.UpdateKeys)
                {
                    viewOf[key] = plan.TaggedViews[key];
                }
                GenerateAreaTables(doc, package, building, plan, newSheets, viewOf,
                    created.Count + plan.UnplacedUpdateKeys.Count + plan.ExtrasShared.Count, runUtc, result);

                // ── Separator lines between the matrix cells ─────────────────
                DrawSeparators(doc, package, building, plan, newSheets, runUtc, result);

                // ── Run record, persisted in the same context ────────────────
                foreach (OperationSkip warning in stringResult.Skipped)
                {
                    result.Skipped.Add(warning);
                }
                foreach (OperationFailure error in stringResult.Failed)
                {
                    result.Failed.Add(error);
                }
                if (result.Succeeded.Count > 0)
                {
                    int sheetCount = plan.PackageSheets.Count + plan.OwnSheets.Count
                        + newSheets.Count(s => s != null) + ownSheets.Count(s => s != null);
                    int viewCount = plan.Diff.UpdateKeys.Count + plan.Diff.OrphanKeys.Count + created.Count(c => c != null);
                    model.RecordSheetRun(new SheetPackageRunRecord
                    {
                        PackageId = package.Id, BuildingId = building.Id, LastRunUtc = runUtc,
                        User = Environment.UserName, SheetCount = sheetCount, ViewCount = viewCount
                    });
                    _storage.Save(doc, model);
                }

                TransactionStatus status = tx.Commit();
                if (status != TransactionStatus.Committed)
                {
                    result.Failed.Add(new OperationFailure("Transaction", $"commit ended as {status} — nothing was kept"));
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        tx.RollBack();
                    }
                }
            }

            log?.Info($"Sheet package '{package.Name}' / {building.Name}: {result.BuildSummary()}");
            return result;
        }

        private static ViewSheet TargetSheet(RunPlan plan, List<ViewSheet> newSheets, int assignmentIndex)
        {
            if (assignmentIndex >= plan.Assignments.Count)
            {
                return null;
            }
            SlotAssignment assignment = plan.Assignments[assignmentIndex];
            return assignment.OnExistingLastSheet
                ? plan.LastMatrixSheet
                : (assignment.NewSheetOrdinal >= 0 && assignment.NewSheetOrdinal < newSheets.Count
                    ? newSheets[assignment.NewSheetOrdinal] : null);
        }

        private static ViewSheet CreateSheet(Document doc, RunPlan plan, string number,
            OperationResult<PlacedViewportInfo> result)
        {
            ViewSheet sheet;
            try
            {
                sheet = ViewSheet.Create(doc, plan.TitleBlockType.Id);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Failed.Add(new OperationFailure("Sheet " + number, ex));
                return null;
            }
            TrySetSheetNumber(sheet, number, result);
            return sheet;
        }

        // ── Resolution shared by preview and run ─────────────────────────────

        private RunPlan Resolve(Document doc, SheetPackage package, BuildingDefinition building, ZfaProjectModel model)
        {
            var plan = new RunPlan();

            // 1 — cross-package overlap guard.
            PackageOverlap overlap = PackageOverlapDetector
                .Detect(model.SheetPackages ?? new List<SheetPackage>())
                .FirstOrDefault(o => o.PackageId == package.Id);
            if (overlap != null)
            {
                plan.BlockReason = overlap.Description;
                return plan;
            }

            // 2 — titleblock.
            plan.TitleBlockType = string.IsNullOrEmpty(package.TitleBlockTypeUniqueId)
                ? null : doc.GetElement(package.TitleBlockTypeUniqueId) as ElementType;
            if (plan.TitleBlockType == null)
            {
                plan.BlockReason = "This set has no titleblock type — pick one in the set editor.";
                return plan;
            }

            // 2b — sheet collection (resolved by name; created at run time if missing).
            plan.CollectionName = (package.SheetCollectionName ?? string.Empty).Trim();
            if (plan.CollectionName.Length > 0)
            {
                plan.Collection = new FilteredElementCollector(doc).OfClass(typeof(SheetCollection)).Cast<SheetCollection>()
                    .FirstOrDefault(c => string.Equals(c.Name, plan.CollectionName, StringComparison.OrdinalIgnoreCase));
                plan.CollectionWillBeCreated = plan.Collection == null;
                if (plan.CollectionWillBeCreated)
                {
                    plan.Notes.Add($"Sheet collection '{plan.CollectionName}' does not exist yet — it will be created.");
                }
            }

            // 2c — the building's Floor Ranges (still collected even when the set
            // does not restrict to them: AddMissingRangeLevels below always backfills
            // a missing plan for a range level, restricted or not).
            // Restricted to the Floor Ranges = one view per Single floor and per Typical/Duplex range
            // (its source level); every other level of a range is represented by that source view.
            plan.RangeLevelIds = CollectFloorRangeLevelIds(building, package.RestrictToFloorRangeLevels);
            plan.AllRangeLevelIds = CollectFloorRangeLevelIds(building, false);
            if (package.RestrictToFloorRangeLevels && plan.RangeLevelIds.Count == 0)
            {
                plan.BlockReason = NoFloorRangesReason(building);
                return plan;
            }

            // 3 — source views.
            if (package.SourceKind == SheetSourceKind.AreaPlans)
            {
                var scheme = string.IsNullOrEmpty(package.AreaSchemeUniqueId) ? null : doc.GetElement(package.AreaSchemeUniqueId) as AreaScheme;
                if (scheme == null)
                {
                    plan.BlockReason = "This set draws area plans but has no area scheme — pick one in the set editor.";
                    return plan;
                }
                plan.SchemeUniqueId = scheme.UniqueId;
                plan.Scheme = scheme;
                plan.CollectSources(doc, v => v.ViewType == ViewType.AreaPlan && v.AreaScheme != null && v.AreaScheme.Id == scheme.Id);
                bool hasAtypicalLevels = building.AtypicalLevels != null && building.AtypicalLevels.Count > 0;
                if (plan.SourceViews.Count == 0 && !hasAtypicalLevels)
                {
                    plan.BlockReason = $"No area plans exist for the scheme '{scheme.Name}' — "
                        + "create them (ZFA Step 2 creates them as it propagates) before generating sheets.";
                    return plan;
                }
            }
            else
            {
                // Floor / ceiling plans: existing plans are duplicated; a range level
                // WITHOUT a plan gets a new one created (ViewPlan.Create), so every
                // typical floor ends up on a sheet — never silently absent.
                bool ceiling = package.SourceKind == SheetSourceKind.CeilingPlans;
                plan.SchemeUniqueId = string.Empty;
                plan.CollectSources(doc, v => v.ViewType == (ceiling ? ViewType.CeilingPlan : ViewType.FloorPlan));
                plan.PlanViewFamilyTypeId = plan.SourceViews.Values.FirstOrDefault()?.GetTypeId()
                    ?? new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                        .FirstOrDefault(t => t.ViewFamily == (ceiling ? ViewFamily.CeilingPlan : ViewFamily.FloorPlan))?.Id;
                if (plan.SourceViews.Count == 0 && plan.PlanViewFamilyTypeId == null)
                {
                    plan.BlockReason = ceiling
                        ? "No ceiling plan views and no Ceiling Plan view type exist in this model."
                        : "No floor plan views and no Floor Plan view type exist in this model.";
                    return plan;
                }
            }

            plan.ViewNameSuffix = package.ViewNameSuffix ?? string.Empty;
            plan.RangeViewLabels = package.RestrictToFloorRangeLevels ? CollectRangeViewLabels(building) : new Dictionary<string, string>();

            int sourceLevelCount = plan.DesiredKeys.Count;
            if (package.RestrictToFloorRangeLevels)
            {
                plan.RestrictToFloorRanges();
            }
            plan.AddMissingRangeLevels(doc, building, package.SourceKind);
            if (package.RestrictToFloorRangeLevels)
            {
                // One view now stands for a whole Typical/Duplex range — its
                // title must say so, whether the view already existed or was
                // just created from scratch above.
                Dictionary<string, string> rangeLabels = CollectRangeLabels(building);
                foreach (string key in plan.DesiredKeys)
                {
                    if (rangeLabels.TryGetValue(key, out string label))
                    {
                        plan.LevelNames[key] = label;
                    }
                }
            }
            if (plan.CoveredLevelNames.Count > 0)
            {
                plan.Notes.Add($"{plan.CoveredLevelNames.Count} level(s) have no view of their own — their Typical/Duplex range's source view stands for them: "
                    + string.Join(", ", plan.CoveredLevelNames));
            }
            if (plan.SkippedLevelNames.Count > 0)
            {
                plan.Notes.Add($"{plan.SkippedLevelNames.Count} level(s) skipped — not in any Floor Range of this building: "
                    + string.Join(", ", plan.SkippedLevelNames));
            }
            if (!package.RestrictToFloorRangeLevels)
            {
                plan.Notes.Add("\"Only draw Floor Range levels\" is off — every matching view in the model draws, " +
                    "including levels with no Floor Range assignment in Zoning Floor Area › Step 1.");
            }
            if (plan.CreatedFromScratchKeys.Count > 0)
            {
                string names = string.Join(", ", plan.CreatedFromScratchKeys.Select(plan.LevelName));
                plan.Notes.Add(package.SourceKind == SheetSourceKind.AreaPlans
                    ? $"{plan.CreatedFromScratchKeys.Count} level(s) in the floor ranges have no area plan for '{plan.Scheme?.Name}' yet — "
                      + $"an EMPTY one is created for each and placed; run Zoning Floor Area › Step 2 (propagation) to fill them with areas: {names}"
                    : $"{plan.CreatedFromScratchKeys.Count} level(s) in the floor ranges have no "
                      + $"{(package.SourceKind == SheetSourceKind.CeilingPlans ? "ceiling" : "floor")} plan yet — a new one is created for each: {names}");
            }
            if (plan.UnplacedUpdateKeys.Count > 0)
            {
                string names = string.Join(", ", plan.UnplacedUpdateKeys.Select(plan.LevelName));
                plan.Notes.Add($"{plan.UnplacedUpdateKeys.Count} view(s) this set already owns had no sheet yet — "
                    + $"placed in the matrix this run: {names}");
            }
            if (plan.DesiredKeys.Count == 0)
            {
                plan.BlockReason = package.RestrictToFloorRangeLevels
                    ? $"None of the {sourceLevelCount} level(s) with source views belongs to a Floor Range of "
                      + $"'{building.Name}' — assign them in Zoning Floor Area › Step 1 first."
                    : $"No matching source view exists anywhere in the model, and '{building.Name}' has no Floor Range "
                      + "level to create one for.";
                return plan;
            }

            // 4 — what this (package, building) already owns.
            plan.CollectTagged(doc, package.Id, building.Id);
            plan.Diff = SheetDiffPlanner.Plan(plan.DesiredKeys, plan.TaggedViews.Keys);

            // 4a — an owned view can end up with no viewport at all: it never had one
            // (a rare leftover), or a level just became this set's source under
            // DiagramSourceLevelsOnly and its own view was never individually placed.
            // Either way it is recognized (it IS this set's) but must still get a slot.
            var placedViewIds = new HashSet<long>(
                new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Select(vp => vp.ViewId.Value));
            plan.UnplacedUpdateKeys = plan.Diff.UpdateKeys
                .Where(key => plan.TaggedViews.TryGetValue(key, out View v) && v != null && !placedViewIds.Contains(v.Id.Value))
                .ToList();

            // 4a2 — saved positions would keep a viewport from being centered.
            int withSavedPosition = plan.DesiredKeys.Count(k =>
            {
                View v = plan.TaggedViews.TryGetValue(k, out View tagged) ? tagged
                    : plan.SourceViews.TryGetValue(k, out ViewPlan source) ? source : null;
                return HasSavedPosition(v);
            });
            if (withSavedPosition > 0)
            {
                plan.Notes.Add($"{withSavedPosition} view(s) have a saved position, which stops their viewport from being centered in its cell. "
                    + "It is removed from them when you generate; the run reports it.");
            }

            // 4b — additional existing views.
            ResolveExtras(doc, package, plan);

            // 5 — the view template, and whether it owns the scale.
            if (!string.IsNullOrEmpty(package.ViewTemplateUniqueId))
            {
                plan.Template = doc.GetElement(package.ViewTemplateUniqueId) as View;
                if (plan.Template == null || !plan.Template.IsTemplate)
                {
                    plan.Template = null;
                    plan.Notes.Add("The stored view template no longer exists in this model — no template will be applied. Pick one again.");
                }
                else
                {
                    ICollection<ElementId> free = plan.Template.GetNonControlledTemplateParameterIds();
                    plan.TemplateControlsScale = !free.Contains(new ElementId(BuiltInParameter.VIEW_SCALE));
                    if (plan.TemplateControlsScale)
                    {
                        plan.Notes.Add($"The view template '{plan.Template.Name}' controls View Scale — its own scale (1/{plan.Template.Scale}) is what the views use.");
                    }
                }
            }
            else
            {
                plan.Notes.Add("No view template selected — created views keep whatever template their source view had. Nothing is cleared.");
            }

            // 6 — titleblock extents → slot layouts (matrix and 1×1).
            if (!TryMeasureTitleBlockInches(doc, plan.TitleBlockType, out double sheetWidthIn, out double sheetHeightIn))
            {
                plan.BlockReason = $"The size of titleblock '{plan.TitleBlockType.Name}' could not be read "
                    + "(its Sheet Width / Sheet Height are empty and no instance of it is placed). Place it on one sheet once, or pick another titleblock.";
                return plan;
            }
            plan.SheetWidthIn = sheetWidthIn;
            plan.SheetHeightIn = sheetHeightIn;
            // Usable area = titleblock extents minus the set's four margins (drawn and dragged in the window).
            plan.UsableWidthIn = sheetWidthIn - package.EffectiveMarginLeftIn - package.EffectiveMarginRightIn;
            plan.UsableHeightIn = sheetHeightIn - package.EffectiveMarginTopIn - package.EffectiveMarginBottomIn;
            plan.MarginLeftIn = package.EffectiveMarginLeftIn;
            plan.MarginBottomIn = package.EffectiveMarginBottomIn;
            double usableWidthIn = sheetWidthIn - package.EffectiveMarginLeftIn - package.EffectiveMarginRightIn;
            double usableHeightIn = sheetHeightIn - package.EffectiveMarginTopIn - package.EffectiveMarginBottomIn;
            if (usableWidthIn < 1 || usableHeightIn < 1)
            {
                plan.BlockReason = $"The margins leave no usable area on a {sheetWidthIn:0.##} x {sheetHeightIn:0.##} in sheet — drag the view area larger or reduce the margins.";
                return plan;
            }
            try
            {
                plan.Slots = SheetSlotLayout.ComputeGrid(usableWidthIn, usableHeightIn, package.Columns, package.Rows,
                    package.ColumnCumulative, package.RowCumulative);
                plan.OwnSheetSlots = SheetSlotLayout.ComputeGrid(usableWidthIn, usableHeightIn, 1, 1);
            }
            catch (ArgumentException ex)
            {
                plan.BlockReason = ex.Message;
                return plan;
            }

            // 6b — area tables take room from the views, so they are planned before the scale is chosen.
            if (!PlanAreaTables(doc, package, building, plan, usableWidthIn, usableHeightIn))
            {
                return plan;
            }

            // 7 — one scale for the whole run.
            // Scope box: the set's own choice, else the building's from ZFA Step 1. Applied to CREATED views only.
            string scopeBoxUid = !string.IsNullOrEmpty(package.ScopeBoxUniqueId) ? package.ScopeBoxUniqueId : building.ScopeBoxUniqueId;
            plan.ScopeBox = string.IsNullOrEmpty(scopeBoxUid) ? null : doc.GetElement(scopeBoxUid);
            if (!string.IsNullOrEmpty(scopeBoxUid) && plan.ScopeBox == null)
            {
                plan.Notes.Add("The stored scope box no longer exists in this model — created views are not cropped. Pick one again.");
            }
            else if (plan.ScopeBox == null)
            {
                plan.Notes.Add("No scope box: neither this set nor the building (ZFA Step 1) has one — created views keep the full extent of their source.");
            }
            if (package.ScaleDenominatorOverride > 0)
            {
                plan.Scale = package.ScaleDenominatorOverride;
                plan.ScaleIsOverride = true;
                if (plan.TemplateControlsScale)
                {
                    plan.Notes.Add($"Manual scale 1/{plan.Scale} is ignored: the view template '{plan.Template.Name}' controls View Scale "
                        + $"(it is 1/{plan.Template.Scale}). Pick a template that leaves View Scale free to use a manual scale.");
                }
            }
            else if (plan.TemplateControlsScale && plan.Template.Scale > 0)
            {
                // The template wins over any recommendation, so the run's scale IS the template's — and
                // the only useful thing left to say is whether the content fits a slot at that scale.
                plan.Scale = plan.Template.Scale;
                plan.ScaleIsFromTemplate = true;
                (double fitWidthFt, double fitHeightFt) = MeasureFootprint(plan.ScopeBox, plan.SourceViews.Values);
                if (fitWidthFt > 0 && fitHeightFt > 0)
                {
                    double cellWidthIn = plan.Slots.Min(s => s.WidthIn), cellHeightIn = plan.Slots.Min(s => s.HeightIn);
                    int recommended = SmartScaleAdvisor.RecommendScale(fitWidthFt, fitHeightFt, cellWidthIn, cellHeightIn);
                    bool fits = SmartScaleAdvisor.Fits(fitWidthFt, fitHeightFt, cellWidthIn, cellHeightIn,
                        SmartScaleAdvisor.DefaultAnnotationMarginIn, plan.Scale);
                    if (!fits)
                    {
                        plan.Notes.Add($"The view template '{plan.Template.Name}' fixes the scale at 1/{plan.Scale}, but a {fitWidthFt:0} x {fitHeightFt:0} ft "
                            + $"footprint overflows a {package.Columns}x{package.Rows} slot at that scale"
                            + (recommended > 0 ? $" — it would fit at 1/{recommended}. Use a template that leaves View Scale free, or fewer columns/rows." : "."));
                    }
                    else if (recommended > 0 && recommended < plan.Scale)
                    {
                        plan.Notes.Add($"The view template '{plan.Template.Name}' fixes the scale at 1/{plan.Scale}; the content would also fit larger, at 1/{recommended}.");
                    }
                }
            }
            else
            {
                (double footprintWidthFt, double footprintHeightFt) = MeasureFootprint(plan.ScopeBox, plan.SourceViews.Values);
                // One scale for the whole run: the largest that fits even the smallest cell.
                plan.Scale = SmartScaleAdvisor.RecommendScale(footprintWidthFt, footprintHeightFt,
                    plan.Slots.Min(s => s.WidthIn), plan.Slots.Min(s => s.HeightIn));
                if (plan.Scale == 0)
                {
                    plan.BlockReason = footprintWidthFt <= 0 || footprintHeightFt <= 0
                        ? "The footprint could not be measured — assign a scope box to the building in ZFA Step 1, or set the scale by hand."
                        : $"A {footprintWidthFt:0} x {footprintHeightFt:0} ft footprint does not fit a {package.Columns}x{package.Rows} slot "
                          + "even at 1/32\" = 1'-0\" — use fewer columns or rows, or set the scale by hand.";
                    return plan;
                }
            }

            if (package.DrawSeparatorLines && (package.Columns > 1 || package.Rows > 1))
            {
                plan.Notes.Add("Separator lines will be drawn as Detail Lines on this set's sheets; lines this set drew earlier are replaced.");
            }

            // 8 — slots: the free tail of the last MATRIX sheet, then new sheets. Shared extras queue after the plan views.
            int slotsPerSheet = package.Columns * package.Rows;
            plan.FreeSlotsOnLastSheet = plan.LastMatrixSheet == null ? 0 : Math.Max(0, slotsPerSheet - CountViewports(plan.LastMatrixSheet));
            plan.ExistingViewNames = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);
            NameAreaTables(plan, package, building);
            plan.TablesNeedingCell = TablesNeedingACell(doc, package, plan);
            plan.Assignments = SheetDiffPlanner.AssignSlots(
                plan.Diff.CreateKeys.Count + plan.UnplacedUpdateKeys.Count + plan.ExtrasShared.Count + plan.TablesNeedingCell.Count,
                slotsPerSheet, plan.FreeSlotsOnLastSheet);
            plan.MatrixNewSheetCount = SheetDiffPlanner.CountNewSheets(plan.Assignments);
            plan.NewSheetCount = plan.MatrixNewSheetCount + plan.ExtrasOwn.Count;

            // 9 — sheet numbers. Foreign numbers are skipped, never overwritten.
            var ownNumbers = new HashSet<string>(plan.PackageSheets.Concat(plan.OwnSheets).Select(s => s.SheetNumber), StringComparer.Ordinal);
            var foreignNumbers = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber).Where(n => !ownNumbers.Contains(n)),
                StringComparer.Ordinal);
            try
            {
                plan.Numbers = SheetNumberSeries.Allocate(package.NumberPrefix, package.StartNumber, package.NumberIncrement,
                    plan.NewSheetCount, ownNumbers, foreignNumbers);
            }
            catch (InvalidOperationException ex)
            {
                plan.BlockReason = ex.Message;
                return plan;
            }
            foreach (string skipped in plan.Numbers.SkippedNumbers)
            {
                plan.Notes.Add($"Sheet number {skipped} belongs to a sheet this set does not own — skipped.");
            }

            // 10 — collisions we can predict.
            plan.ExistingViewNames = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);
            foreach (string key in plan.Diff.CreateKeys)
            {
                string name = plan.ViewNameFor(key, building.Name);
                if (plan.ExistingViewNames.Contains(name))
                {
                    plan.Notes.Add($"A view named '{name}' already exists and is not owned by this set — it will be skipped, not overwritten.");
                }
                else if (!string.Equals(name, SheetNaming.ComposeViewName(plan.ViewLabel(key), plan.ViewNameSuffix), StringComparison.Ordinal))
                {
                    plan.Notes.Add($"'{SheetNaming.ComposeViewName(plan.ViewLabel(key), plan.ViewNameSuffix)}' is already the name of another view, so this one is named '{name}'.");
                }
            }
            if (plan.Diff.OrphanKeys.Count > 0)
            {
                plan.Notes.Add($"{plan.Diff.OrphanKeys.Count} view(s) this set created earlier are no longer in the source set — kept exactly as they are.");
            }

            return plan;
        }

        /// <summary>
        /// Additional views: resolved live, classified into to-place (shared
        /// slot / own sheet), already on one of the package's sheets (kept),
        /// or unplaceable (missing, on a foreign sheet) — the last reported,
        /// never dropped.
        /// </summary>
        private static void ResolveExtras(Document doc, SheetPackage package, RunPlan plan)
        {
            if (package.AdditionalViews == null || package.AdditionalViews.Count == 0)
            {
                return;
            }
            var ownSheetIds = new HashSet<ElementId>(plan.PackageSheets.Concat(plan.OwnSheets).Select(s => s.Id));
            var viewportSheet = new Dictionary<ElementId, ViewSheet>();
            foreach (Viewport vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                if (!viewportSheet.ContainsKey(vp.ViewId) && doc.GetElement(vp.SheetId) is ViewSheet s)
                {
                    viewportSheet[vp.ViewId] = s;
                }
            }
            var scheduleSheets = new Dictionary<ElementId, List<ViewSheet>>();
            foreach (ScheduleSheetInstance ssi in new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
            {
                if (doc.GetElement(ssi.OwnerViewId) is ViewSheet s)
                {
                    if (!scheduleSheets.TryGetValue(ssi.ScheduleId, out List<ViewSheet> list))
                    {
                        scheduleSheets[ssi.ScheduleId] = list = new List<ViewSheet>();
                    }
                    list.Add(s);
                }
            }

            foreach (AdditionalViewRef reference in package.AdditionalViews)
            {
                if (reference == null || string.IsNullOrEmpty(reference.ViewUniqueId))
                {
                    continue;
                }
                var view = doc.GetElement(reference.ViewUniqueId) as View;
                string label = reference.ViewName ?? reference.ViewUniqueId;
                if (view == null || view.IsTemplate || view is ViewSheet)
                {
                    plan.ExtraSkips.Add($"'{label}' no longer exists in this model — remove it from the set");
                    plan.Notes.Add($"Additional view '{label}' no longer exists — it will be skipped.");
                    continue;
                }
                var extra = new ExtraView { View = view, Ref = reference, TypeName = reference.ViewTypeName ?? view.ViewType.ToString() };

                if (view is ViewSchedule)
                {
                    if (scheduleSheets.TryGetValue(view.Id, out List<ViewSheet> placedOn) && placedOn.Any(s => ownSheetIds.Contains(s.Id)))
                    {
                        extra.OnSheetNumber = placedOn.First(s => ownSheetIds.Contains(s.Id)).SheetNumber;
                        plan.ExtrasAlreadyPlaced.Add(extra);
                        continue;
                    }
                }
                else if (viewportSheet.TryGetValue(view.Id, out ViewSheet on))
                {
                    if (ownSheetIds.Contains(on.Id))
                    {
                        extra.OnSheetNumber = on.SheetNumber;
                        plan.ExtrasAlreadyPlaced.Add(extra);
                    }
                    else
                    {
                        plan.ExtraSkips.Add($"'{view.Name}' is already on sheet {on.SheetNumber}, which this set does not own — not moved");
                        plan.Notes.Add($"Additional view '{view.Name}' is already on sheet {on.SheetNumber} (not this set's) — a view can be on one sheet only; skipped.");
                    }
                    continue;
                }

                if (reference.OwnSheet)
                {
                    plan.ExtrasOwn.Add(extra);
                }
                else
                {
                    plan.ExtrasShared.Add(extra);
                }
            }
        }

        // ── Preview mini-sheets ──────────────────────────────────────────────

        /// <summary>
        /// The verification list behind "only these views will be on the sheets": the expected views with the sheet
        /// each one is or will be on, then anything else already sitting on this set's sheets, with the reason.
        /// Read-only, recomputed with every preview.
        /// </summary>
        private static void BuildContentCheck(Document doc, SheetPackage package, BuildingDefinition building, RunPlan plan, SheetPackagePreview preview)
        {
            var sheetOfView = new Dictionary<long, string>();
            foreach (Viewport viewport in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                if (!sheetOfView.ContainsKey(viewport.ViewId.Value) && doc.GetElement(viewport.SheetId) is ViewSheet owner)
                {
                    sheetOfView[viewport.ViewId.Value] = owner.SheetNumber;
                }
            }

            string Planned(int index)
            {
                if (index < 0 || index >= plan.Assignments.Count) return "?";
                SlotAssignment assignment = plan.Assignments[index];
                if (assignment.OnExistingLastSheet) return plan.LastMatrixSheet?.SheetNumber ?? "?";
                return assignment.NewSheetOrdinal >= 0 && assignment.NewSheetOrdinal < plan.Numbers.Numbers.Count
                    ? plan.Numbers.Numbers[assignment.NewSheetOrdinal] : "?";
            }

            var expected = new List<SheetContentRow>();
            var expectedIds = new HashSet<long>();
            int created = plan.Diff.CreateKeys.Count;

            for (int i = 0; i < created; i++)
            {
                string key = plan.Diff.CreateKeys[i];
                plan.ViewSheetNumbers[key] = Planned(i);
                expected.Add(new SheetContentRow
                {
                    SheetNumber = Planned(i), IsExpected = true,
                    ViewName = plan.ViewNameFor(key, building.Name),
                    Detail = "new view, placed by this run"
                });
            }
            foreach (string key in plan.Diff.UpdateKeys)
            {
                View view = plan.TaggedViews[key];
                expectedIds.Add(view.Id.Value);
                int unplaced = plan.UnplacedUpdateKeys.IndexOf(key);
                plan.ViewSheetNumbers[key] = unplaced >= 0 ? Planned(created + unplaced)
                    : sheetOfView.TryGetValue(view.Id.Value, out string knownSheet) ? knownSheet : "?";
                expected.Add(new SheetContentRow
                {
                    ViewName = view.Name, IsExpected = true,
                    SheetNumber = unplaced >= 0 ? Planned(created + unplaced) : sheetOfView.TryGetValue(view.Id.Value, out string number) ? number : "?",
                    Detail = unplaced >= 0 ? "this set's view, had no sheet — placed by this run" : "this set's view, already on its sheet"
                });
            }
            for (int j = 0; j < plan.ExtrasShared.Count; j++)
            {
                ExtraView extra = plan.ExtrasShared[j];
                expectedIds.Add(extra.View.Id.Value);
                expected.Add(new SheetContentRow
                {
                    ViewName = extra.View.Name, IsExpected = true,
                    SheetNumber = Planned(created + plan.UnplacedUpdateKeys.Count + j), Detail = "additional view you added to the set"
                });
            }
            for (int i = 0; i < plan.ExtrasOwn.Count; i++)
            {
                ExtraView extra = plan.ExtrasOwn[i];
                expectedIds.Add(extra.View.Id.Value);
                int ordinal = plan.MatrixNewSheetCount + i;
                expected.Add(new SheetContentRow
                {
                    ViewName = extra.View.Name, IsExpected = true,
                    SheetNumber = ordinal < plan.Numbers.Numbers.Count ? plan.Numbers.Numbers[ordinal] : "?",
                    Detail = "additional view you added to the set, on its own sheet"
                });
            }
            foreach (ExtraView extra in plan.ExtrasAlreadyPlaced)
            {
                expectedIds.Add(extra.View.Id.Value);
                expected.Add(new SheetContentRow
                {
                    ViewName = extra.View.Name, IsExpected = true, SheetNumber = extra.OnSheetNumber,
                    Detail = "additional view you added to the set, already placed"
                });
            }

            if (package.AddAreaTable)
            {
                int ownBase = created + plan.UnplacedUpdateKeys.Count + plan.ExtrasShared.Count;
                foreach (AreaTableSpec table in plan.Tables)
                {
                    var row = new SheetTableRow { TableName = table.Name ?? table.LevelName };
                    if (table.Problem != null)
                    {
                        row.Status = SheetTableRow.StatusSkipped;
                        row.SheetNumber = "—";
                        row.Detail = table.Problem;
                        preview.TableSkippedCount++;
                    }
                    else
                    {
                        ScheduleSheetInstance placed = table.Existing == null ? null : AreaTableService.FindPlaced(doc, table.Existing);
                        int queued = plan.TablesNeedingCell.IndexOf(table);
                        row.Status = table.IsNew ? SheetTableRow.StatusNew : SheetTableRow.StatusUpdate;
                        row.SheetNumber = placed != null && doc.GetElement(placed.OwnerViewId) is ViewSheet placedSheet ? placedSheet.SheetNumber
                            : queued >= 0 ? Planned(ownBase + queued)
                            : plan.ViewSheetNumbers.TryGetValue(table.Key, out string viewSheet) ? viewSheet : "?";
                        row.Detail = $"{table.Rows} rows · {table.WidthIn:0.#} x {table.HeightIn:0.#} in"
                            + (placed != null ? " · already on its sheet, not moved" : string.Empty);
                        if (table.IsNew) preview.TableNewCount++; else preview.TableUpdateCount++;
                    }
                    preview.TableRows.Add(row);
                }
            }

            var unexpected = new List<SheetContentRow>();
            var builtByThisSet = new HashSet<long>(plan.TaggedViews.Values.Select(v => v.Id.Value));
            foreach (ViewSheet sheet in plan.PackageSheets.Concat(plan.OwnSheets))
            {
                foreach (ElementId id in sheet.GetAllViewports())
                {
                    if (!(doc.GetElement(id) is Viewport viewport) || !(doc.GetElement(viewport.ViewId) is View placed)
                        || expectedIds.Contains(placed.Id.Value))
                    {
                        continue;
                    }
                    unexpected.Add(new SheetContentRow
                    {
                        SheetNumber = sheet.SheetNumber, IsExpected = false, ViewName = placed.Name,
                        Detail = builtByThisSet.Contains(placed.Id.Value)
                            ? "built by this set earlier; its level is no longer a source — it stays here until you remove it"
                            : "on this set's sheet but not built by this set (placed by hand?) — left as it is"
                    });
                }
            }

            preview.ContentRows.AddRange(expected.OrderBy(r => r.SheetNumber, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.ViewName, StringComparer.OrdinalIgnoreCase));
            preview.ContentRows.AddRange(unexpected.OrderBy(r => r.SheetNumber, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.ViewName, StringComparer.OrdinalIgnoreCase));
            preview.ContentExpectedCount = expected.Count;
            preview.ContentUnexpectedCount = unexpected.Count;
        }

        private static void BuildPreviewSheets(Document doc, SheetPackage package, RunPlan plan, SheetPackagePreview preview)
        {
            int slotsPerSheet = package.Columns * package.Rows;
            List<string> createLabels = plan.Diff.CreateKeys.Select(plan.LevelName)
                .Concat(plan.UnplacedUpdateKeys.Select(plan.LevelName))
                .Concat(plan.ExtrasShared.Select(e => e.View.Name))
                .Concat(plan.TablesNeedingCell.Select(t => "TABLE " + t.Name)).ToList();

            for (int i = 0; i < plan.PackageSheets.Count; i++)
            {
                ViewSheet sheet = plan.PackageSheets[i];
                List<string> labels = ReadSlotLabels(doc, sheet, slotsPerSheet);
                int added = 0;
                if (i == plan.PackageSheets.Count - 1)
                {
                    for (int a = 0; a < plan.Assignments.Count; a++)
                    {
                        if (!plan.Assignments[a].OnExistingLastSheet)
                        {
                            continue;
                        }
                        int slot = plan.Assignments[a].SlotIndex;
                        if (slot >= 0 && slot < labels.Count && a < createLabels.Count)
                        {
                            labels[slot] = createLabels[a];
                            added++;
                        }
                    }
                }
                preview.Sheets.Add(NewPreviewSheet(plan, sheet.SheetNumber, sheet.Name,
                    added > 0 ? $"{added} view(s) added" : "existing — kept", false, labels, package.Columns, package.Rows, package.EffectiveMarginRightIn));
            }
            foreach (ViewSheet sheet in plan.OwnSheets)
            {
                preview.Sheets.Add(NewPreviewSheet(plan, sheet.SheetNumber, sheet.Name, "existing — own sheet", false,
                    ReadSlotLabels(doc, sheet, 1), 1, 1, package.EffectiveMarginRightIn));
            }

            for (int ordinal = 0; ordinal < plan.MatrixNewSheetCount; ordinal++)
            {
                var labels = new List<string>();
                for (int s = 0; s < slotsPerSheet; s++)
                {
                    labels.Add(EmptySlotLabel);
                }
                var levelsOnSheet = new List<string>();
                for (int a = 0; a < plan.Assignments.Count; a++)
                {
                    SlotAssignment assignment = plan.Assignments[a];
                    if (assignment.OnExistingLastSheet || assignment.NewSheetOrdinal != ordinal || a >= createLabels.Count)
                    {
                        continue;
                    }
                    if (a < plan.Diff.CreateKeys.Count)
                    {
                        levelsOnSheet.Add(createLabels[a]);
                    }
                    if (assignment.SlotIndex >= 0 && assignment.SlotIndex < labels.Count)
                    {
                        labels[assignment.SlotIndex] = createLabels[a];
                    }
                }
                string number = ordinal < plan.Numbers.Numbers.Count ? plan.Numbers.Numbers[ordinal] : "?";
                string name = SheetNaming.ComposeSheetName(package.Name, levelsOnSheet.FirstOrDefault(), levelsOnSheet.LastOrDefault());
                preview.Sheets.Add(NewPreviewSheet(plan, number, name, "new", true, labels, package.Columns, package.Rows, package.EffectiveMarginRightIn));
            }
            for (int i = 0; i < plan.ExtrasOwn.Count; i++)
            {
                int ordinal = plan.MatrixNewSheetCount + i;
                string number = ordinal < plan.Numbers.Numbers.Count ? plan.Numbers.Numbers[ordinal] : "?";
                preview.Sheets.Add(NewPreviewSheet(plan, number, ComposeOwnSheetName(package, plan.ExtrasOwn[i].View), "new · own sheet", true,
                    new List<string> { plan.ExtrasOwn[i].View.Name }, 1, 1, package.EffectiveMarginRightIn));
            }
        }

        private static SheetPreviewSheet NewPreviewSheet(RunPlan plan, string number, string name, string caption, bool isNew,
            List<string> labels, int columns, int rows, double stripIn) =>
            new SheetPreviewSheet
            {
                SheetNumber = number, SheetName = name, StatusCaption = caption, IsNew = isNew,
                SheetWidthIn = plan.SheetWidthIn, SheetHeightIn = plan.SheetHeightIn, TitleStripWidthIn = stripIn,
                Columns = columns, Rows = rows, SlotLabels = labels
            };

        private static List<string> ReadSlotLabels(Document doc, ViewSheet sheet, int slotCount)
        {
            var placed = new List<PlacedLabel>();
            foreach (ElementId id in sheet.GetAllViewports())
            {
                if (!(doc.GetElement(id) is Viewport viewport))
                {
                    continue;
                }
                XYZ center = viewport.GetBoxCenter();
                placed.Add(new PlacedLabel { X = center.X, Y = center.Y, Label = DescribeView(doc.GetElement(viewport.ViewId) as View) });
            }
            foreach (ScheduleSheetInstance ssi in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
            {
                XYZ p = ssi.Point;
                placed.Add(new PlacedLabel { X = p.X, Y = p.Y, Label = DescribeView(doc.GetElement(ssi.ScheduleId) as View) });
            }
            placed.Sort(CompareReadingOrder);

            var labels = new List<string>();
            for (int i = 0; i < slotCount; i++)
            {
                labels.Add(i < placed.Count ? placed[i].Label : EmptySlotLabel);
            }
            return labels;
        }

        private static int CompareReadingOrder(PlacedLabel a, PlacedLabel b)
        {
            if (Math.Abs(a.Y - b.Y) > RowToleranceFt)
            {
                return b.Y.CompareTo(a.Y);
            }
            return a.X.CompareTo(b.X);
        }

        private static string DescribeView(View view)
        {
            if (view == null)
            {
                return "?";
            }
            return view is ViewPlan plan && plan.GenLevel != null ? plan.GenLevel.Name : view.Name;
        }

        // ── Element authoring ────────────────────────────────────────────────

        private static SheetCollection ResolveOrCreateCollection(Document doc, RunPlan plan, OperationResult<PlacedViewportInfo> result)
        {
            if (string.IsNullOrEmpty(plan.CollectionName))
            {
                return null;
            }
            if (plan.Collection != null)
            {
                return plan.Collection;
            }
            try
            {
                SheetCollection created = SheetCollection.Create(doc, plan.CollectionName);
                result.Succeeded.Add(new PlacedViewportInfo { ViewName = $"Sheet collection '{plan.CollectionName}' created", SheetNumber = "—" });
                return created;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Failed.Add(new OperationFailure($"Sheet collection '{plan.CollectionName}'", ex));
                return null;
            }
        }

        private static void ApplyCollection(ViewSheet sheet, SheetCollection collection, OperationResult<PlacedViewportInfo> result)
        {
            if (sheet == null || collection == null || sheet.SheetCollectionId == collection.Id)
            {
                return;
            }
            try
            {
                sheet.SheetCollectionId = collection.Id;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Skipped.Add(new OperationSkip($"Sheet {sheet.SheetNumber} → collection '{collection.Name}'", ex.Message));
            }
        }

        /// <summary>Writes the set's text values; a parameter that is missing or read-only is reported once per element, never invented.</summary>
        private static void ApplyParameterValues(Element element, List<ParameterValueAssignment> values, OperationResult<PlacedViewportInfo> result)
        {
            if (element == null || values == null)
            {
                return;
            }
            foreach (ParameterValueAssignment assignment in values)
            {
                if (assignment == null || string.IsNullOrWhiteSpace(assignment.ParameterName) || string.IsNullOrWhiteSpace(assignment.Value))
                {
                    continue;
                }
                Parameter p = element.LookupParameter(assignment.ParameterName);
                string who = element is ViewSheet vs ? "Sheet " + vs.SheetNumber : element.Name;
                if (p == null)
                {
                    result.Skipped.Add(new OperationSkip($"{who} · {assignment.ParameterName}", "parameter does not exist on this element"));
                    continue;
                }
                if (p.IsReadOnly || p.StorageType != StorageType.String)
                {
                    result.Skipped.Add(new OperationSkip($"{who} · {assignment.ParameterName}", "parameter is read-only or not text"));
                    continue;
                }
                if (string.Equals(p.AsString(), assignment.Value, StringComparison.Ordinal))
                {
                    continue;
                }
                try
                {
                    p.Set(assignment.Value);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    result.Skipped.Add(new OperationSkip($"{who} · {assignment.ParameterName}", ex.Message));
                }
            }
        }

        private static void ApplyTemplateAndScale(View view, RunPlan plan, OperationResult<PlacedViewportInfo> result)
        {
            if (plan.Template != null && view.ViewTemplateId != plan.Template.Id)
            {
                view.ViewTemplateId = plan.Template.Id;
            }
            if (plan.TemplateControlsScale || view.Scale == plan.Scale)
            {
                return;
            }
            try
            {
                view.Scale = plan.Scale;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                result.Skipped.Add(new OperationSkip(view.Name, $"View Scale is read-only on this view — it stays at 1/{view.Scale}"));
            }
        }

        private static void ApplyScopeBox(View view, Element scopeBox)
        {
            if (scopeBox == null)
            {
                return;
            }
            Parameter parameter = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (parameter != null && !parameter.IsReadOnly)
            {
                parameter.Set(scopeBox.Id);
            }
        }

        /// <summary>
        /// Gives a view this set owns its current name, and its Title on Sheet when that still shows what an
        /// earlier run wrote (the old name or an empty value) — a title someone edited by hand is left alone.
        /// A name another view already uses is skipped and reported, never forced.
        /// </summary>
        private static void RenameOwnedView(View view, string desired, string oldLevelName, RunPlan plan, OperationResult<PlacedViewportInfo> result)
        {
            if (string.Equals(view.Name, desired, StringComparison.Ordinal))
            {
                return;
            }
            if (plan.ExistingViewNames.Contains(desired) && !string.Equals(view.Name, desired, StringComparison.OrdinalIgnoreCase))
            {
                result.Skipped.Add(new OperationSkip(view.Name, $"not renamed to '{desired}': another view already has that name"));
                return;
            }
            string oldName = view.Name;
            try
            {
                view.Name = desired;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Skipped.Add(new OperationSkip(oldName, $"not renamed to '{desired}': {ex.Message}"));
                return;
            }
            plan.ExistingViewNames.Remove(oldName);
            plan.ExistingViewNames.Add(desired);

            Parameter title = view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
            if (title != null && !title.IsReadOnly)
            {
                string current = title.AsString() ?? string.Empty;
                if (current.Length == 0 || string.Equals(current, oldName, StringComparison.Ordinal)
                    || string.Equals(current, oldLevelName, StringComparison.Ordinal))
                {
                    title.Set(desired);
                }
            }
        }

        private static void SetTitleOnSheet(View view, string levelName)
        {
            Parameter parameter = view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
            if (parameter != null && !parameter.IsReadOnly)
            {
                parameter.Set(levelName ?? string.Empty);
            }
        }

        private static bool TrySetSheetNumber(ViewSheet sheet, string number, OperationResult<PlacedViewportInfo> result)
        {
            try
            {
                sheet.SheetNumber = number;
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                result.Skipped.Add(new OperationSkip("Sheet number " + number,
                    $"already taken — the new sheet kept the number Revit gave it ({sheet.SheetNumber})"));
                return false;
            }
        }

        private static string ComposeNewSheetName(SheetPackage package, RunPlan plan, List<CreatedView> created, int ordinal)
        {
            var levels = new List<string>();
            for (int i = 0; i < created.Count && i < plan.Assignments.Count; i++)
            {
                SlotAssignment assignment = plan.Assignments[i];
                if (assignment.OnExistingLastSheet || assignment.NewSheetOrdinal != ordinal)
                {
                    continue;
                }
                levels.Add(created[i] != null ? created[i].LevelName : plan.LevelName(plan.Diff.CreateKeys[i]));
            }
            return SheetNaming.ComposeSheetName(package.Name, levels.FirstOrDefault(), levels.LastOrDefault());
        }

        private static string ComposeOwnSheetName(SheetPackage package, View view) =>
            $"{(package.Name ?? string.Empty).Trim().ToUpperInvariant()} — {(view?.Name ?? string.Empty).ToUpperInvariant()}";

        private void Place(Document doc, ViewSheet sheet, View view, int slotIndex, IReadOnlyList<SlotRect> slots,
            RunPlan plan, OperationResult<PlacedViewportInfo> result)
        {
            if (slotIndex < 0 || slotIndex >= slots.Count)
            {
                result.Failed.Add(new OperationFailure(view.Name, $"slot {slotIndex} is outside the {slots.Count}-slot matrix"));
                return;
            }
            (double _, double _, XYZ originFt) = MeasureUsableArea(doc, sheet, plan);
            if (ReferenceEquals(slots, plan.Slots))
            {
                originFt = OffsetBy(originFt, plan.ViewOffsetXIn, plan.ViewOffsetYIn); // the matrix, not an own-sheet slot
            }
            SlotRect slot = slots[slotIndex];
            var center = new XYZ(
                originFt.X + UnitUtils.ConvertToInternalUnits(slot.CenterXIn, UnitTypeId.Inches),
                originFt.Y + UnitUtils.ConvertToInternalUnits(slot.CenterYIn, UnitTypeId.Inches), 0);

            try
            {
                if (view is ViewSchedule schedule)
                {
                    // A schedule's placement point is its top-left corner, not its centre.
                    var topLeft = new XYZ(
                        originFt.X + UnitUtils.ConvertToInternalUnits(slot.CenterXIn - slot.WidthIn / 2, UnitTypeId.Inches),
                        originFt.Y + UnitUtils.ConvertToInternalUnits(slot.CenterYIn + slot.HeightIn / 2, UnitTypeId.Inches), 0);
                    ScheduleSheetInstance.Create(doc, sheet.Id, schedule.Id, topLeft);
                    result.Succeeded.Add(new PlacedViewportInfo { ViewName = view.Name, SheetNumber = sheet.SheetNumber, ScaleDenominator = 0 });
                    return;
                }
                if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                {
                    result.Skipped.Add(new OperationSkip(view.Name, "Revit reports the view cannot be added to a sheet (already placed on another one?)"));
                    return;
                }
                // A saved position (Revit 2026) controls where the viewport goes and makes Revit refuse to move it,
                // so the view would land wherever it was saved, not in its cell. A duplicated view can carry it over.
                if (TryClearSavedPosition(view, result))
                {
                    plan.SavedPositionsReleased++;
                }
                Viewport viewport = Viewport.Create(doc, sheet.Id, view.Id, center);
                viewport.SetBoxCenter(center);
                if ((viewport.GetBoxCenter() - center).GetLength() > OffCenterToleranceFt)
                {
                    viewport.SetBoxCenter(center);
                    if ((viewport.GetBoxCenter() - center).GetLength() > OffCenterToleranceFt)
                    {
                        result.Skipped.Add(new OperationSkip(view.Name,
                            $"placed on sheet {sheet.SheetNumber} but Revit would not move it to the centre of its cell — check the view for a saved position or a locked viewport"));
                    }
                }
                result.Succeeded.Add(new PlacedViewportInfo { ViewName = view.Name, SheetNumber = sheet.SheetNumber, ScaleDenominator = view.Scale });
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Failed.Add(new OperationFailure(view.Name, ex));
            }
        }

        // ── Saved positions (Revit 2026) ─────────────────────────────────────

        private const double OffCenterToleranceFt = 0.01; // about 1/8 in

        private static bool HasSavedPosition(View view)
        {
#if REVIT2026_OR_GREATER
            return view != null && view.ViewPositionId != null && view.ViewPositionId != ElementId.InvalidElementId;
#else
            return false;
#endif
        }

        /// <summary>Removes the view's saved position. False when it had none or Revit refused (the refusal is reported).</summary>
        private static bool TryClearSavedPosition(View view, OperationResult<PlacedViewportInfo> result)
        {
#if REVIT2026_OR_GREATER
            if (!HasSavedPosition(view))
            {
                return false;
            }
            try
            {
                view.ViewPositionId = ElementId.InvalidElementId;
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Skipped.Add(new OperationSkip(view.Name,
                    "its saved position could not be removed (" + ex.Message + ") — its viewport may not be centered"));
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// A view this set owns that already sits on a sheet with a saved position cannot be re-centered while the
        /// position holds it. Removing the position is the only reason these viewports move: each one is put on the
        /// centre of the cell it already occupies, and the run says how many.
        /// </summary>
        private static void ReleaseOwnedSavedPositions(Document doc, RunPlan plan, OperationResult<PlacedViewportInfo> result)
        {
            var viewportOf = new Dictionary<long, Viewport>();
            foreach (Viewport viewport in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                if (!viewportOf.ContainsKey(viewport.ViewId.Value))
                {
                    viewportOf[viewport.ViewId.Value] = viewport;
                }
            }

            int released = 0;
            foreach (string key in plan.Diff.UpdateKeys.Except(plan.UnplacedUpdateKeys))
            {
                View view = plan.TaggedViews[key];
                if (!HasSavedPosition(view) || !TryClearSavedPosition(view, result))
                {
                    continue;
                }
                released++;
                if (!viewportOf.TryGetValue(view.Id.Value, out Viewport placed) || !(doc.GetElement(placed.SheetId) is ViewSheet sheet))
                {
                    continue;
                }
                try
                {
                    (double _, double _, XYZ originFt) = MeasureUsableArea(doc, sheet, plan);
                    originFt = OffsetBy(originFt, plan.ViewOffsetXIn, plan.ViewOffsetYIn);
                    XYZ now = placed.GetBoxCenter();
                    XYZ best = null;
                    foreach (SlotRect slot in plan.Slots)
                    {
                        var centre = new XYZ(
                            originFt.X + UnitUtils.ConvertToInternalUnits(slot.CenterXIn, UnitTypeId.Inches),
                            originFt.Y + UnitUtils.ConvertToInternalUnits(slot.CenterYIn, UnitTypeId.Inches), 0);
                        if (best == null || (centre - now).GetLength() < (best - now).GetLength())
                        {
                            best = centre;
                        }
                    }
                    if (best != null)
                    {
                        placed.SetBoxCenter(best);
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    result.Skipped.Add(new OperationSkip(view.Name, "saved position removed, but the viewport could not be re-centered: " + ex.Message));
                }
            }
            if (released + plan.SavedPositionsReleased > 0)
            {
                result.Skipped.Add(new OperationSkip("Saved positions",
                    $"removed from {released + plan.SavedPositionsReleased} view(s) so their viewports could be centered in their cells"));
            }
        }

        // ── Separator lines ──────────────────────────────────────────────────

        private const string SeparatorTagKey = "sheet-separator";

        /// <summary>
        /// Draws the lines between the cells as Detail Lines on every matrix sheet of the set, tagged so a re-run
        /// replaces only these. With the option off nothing is drawn or deleted; lines from an earlier run are reported.
        /// </summary>
        private static void DrawSeparators(Document doc, SheetPackage package, BuildingDefinition building, RunPlan plan,
            List<ViewSheet> newSheets, DateTime runUtc, OperationResult<PlacedViewportInfo> result)
        {
            IEnumerable<ViewSheet> sheets = plan.PackageSheets.Concat(newSheets.Where(s => s != null)).ToList();
            bool anyCells = package.Columns > 1 || package.Rows > 1;

            if (!package.DrawSeparatorLines || !anyCells)
            {
                int left = sheets.Sum(s => OwnSeparators(doc, s, package, building).Count);
                if (left > 0)
                {
                    result.Skipped.Add(new OperationSkip("Separator lines",
                        $"{left} Detail Line(s) from an earlier run remain on this set's sheets — they are not touched while 'draw the separators' is off"));
                }
                return;
            }

            GraphicsStyle style = ResolveLineStyle(doc, package.SeparatorLineStyle);
            if (!string.IsNullOrEmpty(package.SeparatorLineStyle) && style == null)
            {
                result.Skipped.Add(new OperationSkip("Separator line style",
                    $"'{package.SeparatorLineStyle}' does not exist in this model — the lines use the default style"));
            }

            int drawn = 0, replaced = 0;
            foreach (ViewSheet sheet in sheets)
            {
                try
                {
                    (double _, double _, XYZ originFt) = MeasureUsableArea(doc, sheet, plan);
                    originFt = OffsetBy(originFt, plan.ViewOffsetXIn, plan.ViewOffsetYIn);
                    double widthIn = plan.ViewAreaWidthIn, heightIn = plan.ViewAreaHeightIn;
                    foreach (Element old in OwnSeparators(doc, sheet, package, building))
                    {
                        doc.Delete(old.Id);
                        replaced++;
                    }

                    var segments = new List<Line>();
                    foreach (double xIn in SheetSlotLayout.SeparatorPositionsIn(widthIn, package.Columns, package.ColumnCumulative))
                    {
                        double x = originFt.X + UnitUtils.ConvertToInternalUnits(xIn, UnitTypeId.Inches);
                        segments.Add(Line.CreateBound(new XYZ(x, originFt.Y, 0),
                            new XYZ(x, originFt.Y + UnitUtils.ConvertToInternalUnits(heightIn, UnitTypeId.Inches), 0)));
                    }
                    foreach (double yIn in SheetSlotLayout.SeparatorPositionsIn(heightIn, package.Rows, package.RowCumulative))
                    {
                        // Row separators are measured from the top; sheet Y grows upward.
                        double y = originFt.Y + UnitUtils.ConvertToInternalUnits(heightIn - yIn, UnitTypeId.Inches);
                        segments.Add(Line.CreateBound(new XYZ(originFt.X, y, 0),
                            new XYZ(originFt.X + UnitUtils.ConvertToInternalUnits(widthIn, UnitTypeId.Inches), y, 0)));
                    }
                    foreach (Line segment in segments)
                    {
                        DetailCurve curve = doc.Create.NewDetailCurve(sheet, segment);
                        if (style != null)
                        {
                            curve.LineStyle = style;
                        }
                        PropagatedElementTag.Write(curve, SeparatorTagKey, building.Id, package.Id, runUtc);
                        drawn++;
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    result.Failed.Add(new OperationFailure($"Separator lines on sheet {sheet.SheetNumber}", ex));
                }
            }
            if (replaced > 0)
            {
                result.Skipped.Add(new OperationSkip("Separator lines",
                    $"{replaced} line(s) this set drew earlier were replaced with the current layout"));
            }
        }

        private static List<Element> OwnSeparators(Document doc, ViewSheet sheet, SheetPackage package, BuildingDefinition building)
        {
            var found = new List<Element>();
            foreach (Element element in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(CurveElement)))
            {
                if (PropagatedElementTag.TryRead(element, out string identity, out Guid taggedBuilding, out Guid taggedPackage)
                    && identity == SeparatorTagKey && taggedBuilding == building.Id && taggedPackage == package.Id)
                {
                    found.Add(element);
                }
            }
            return found;
        }

        private static GraphicsStyle ResolveLineStyle(Document doc, string name)
        {
            string wanted = string.IsNullOrWhiteSpace(name) ? "Thin Lines" : name.Trim();
            Category lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category sub = lines?.SubCategories.Cast<Category>().FirstOrDefault(c => string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase));
            return sub?.GetGraphicsStyle(GraphicsStyleType.Projection);
        }

        // ── Measurement ──────────────────────────────────────────────────────

        private static (double WidthIn, double HeightIn, XYZ OriginFt) MeasureUsableArea(Document doc, ViewSheet sheet, RunPlan plan)
        {
            Element instance = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().FirstOrDefault();
            BoundingBoxXYZ box = instance?.get_BoundingBox(sheet);

            double widthIn = plan.SheetWidthIn;
            double heightIn = plan.SheetHeightIn;
            var originFt = XYZ.Zero;
            if (box != null)
            {
                widthIn = UnitUtils.ConvertFromInternalUnits(box.Max.X - box.Min.X, UnitTypeId.Inches);
                heightIn = UnitUtils.ConvertFromInternalUnits(box.Max.Y - box.Min.Y, UnitTypeId.Inches);
                originFt = new XYZ(box.Min.X, box.Min.Y, 0);
            }
            double left = UnitUtils.ConvertToInternalUnits(plan.MarginLeftIn, UnitTypeId.Inches);
            double bottom = UnitUtils.ConvertToInternalUnits(plan.MarginBottomIn, UnitTypeId.Inches);
            return (widthIn, heightIn, new XYZ(originFt.X + left, originFt.Y + bottom, 0));
        }

        private static bool TryMeasureTitleBlockInches(Document doc, ElementType type, out double widthIn, out double heightIn)
        {
            widthIn = ReadInches(type.get_Parameter(BuiltInParameter.SHEET_WIDTH));
            heightIn = ReadInches(type.get_Parameter(BuiltInParameter.SHEET_HEIGHT));
            if (widthIn > 0 && heightIn > 0)
            {
                return true;
            }
            foreach (FamilyInstance instance in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                if (instance.GetTypeId() != type.Id || !(doc.GetElement(instance.OwnerViewId) is ViewSheet owner))
                {
                    continue;
                }
                BoundingBoxXYZ box = instance.get_BoundingBox(owner);
                if (box == null)
                {
                    continue;
                }
                widthIn = UnitUtils.ConvertFromInternalUnits(box.Max.X - box.Min.X, UnitTypeId.Inches);
                heightIn = UnitUtils.ConvertFromInternalUnits(box.Max.Y - box.Min.Y, UnitTypeId.Inches);
                return widthIn > 0 && heightIn > 0;
            }
            return false;
        }

        private static double ReadInches(Parameter parameter) =>
            parameter == null || parameter.StorageType != StorageType.Double ? 0 : UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Inches);

        private static (double WidthFt, double HeightFt) MeasureFootprint(Element scopeBox, IEnumerable<ViewPlan> sourcePlans)
        {
            if (scopeBox != null)
            {
                BoundingBoxXYZ box = scopeBox.get_BoundingBox(null);
                if (box != null)
                {
                    return (box.Max.X - box.Min.X, box.Max.Y - box.Min.Y);
                }
            }
            double width = 0, height = 0;
            foreach (ViewPlan plan in sourcePlans)
            {
                BoundingBoxXYZ crop = plan.CropBox;
                if (crop != null)
                {
                    width = Math.Max(width, crop.Max.X - crop.Min.X);
                    height = Math.Max(height, crop.Max.Y - crop.Min.Y);
                }
            }
            return (width, height);
        }

        private static int CountViewports(ViewSheet sheet) => sheet.GetAllViewports().Count;

        private static string DescribeSheetOf(Document doc, View view)
        {
            Viewport viewport = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().FirstOrDefault(v => v.ViewId == view.Id);
            if (viewport == null)
            {
                return "(not on a sheet)";
            }
            return doc.GetElement(viewport.SheetId) is ViewSheet sheet ? sheet.SheetNumber : "(not on a sheet)";
        }

        // ── Internal shapes ──────────────────────────────────────────────────

        private sealed class PlacedLabel
        {
            public double X;
            public double Y;
            public string Label;
        }

        private sealed class CreatedView
        {
            public View View;
            public string LevelName;
        }

        private sealed class ExtraView
        {
            public View View;
            public AdditionalViewRef Ref;
            public string TypeName;
            public string OnSheetNumber;
        }

        private sealed class RunPlan
        {
            public string BlockReason;
            public ElementType TitleBlockType;
            public Element ScopeBox;
            public double MarginLeftIn;
            public double MarginBottomIn;
            public double UsableWidthIn;
            public double UsableHeightIn;
            public string SchemeUniqueId = string.Empty;
            public AreaScheme Scheme;
            public View Template;
            public bool TemplateControlsScale;
            public string CollectionName = string.Empty;
            public SheetCollection Collection;
            public bool CollectionWillBeCreated;

            public Dictionary<string, ViewPlan> SourceViews = new Dictionary<string, ViewPlan>(StringComparer.Ordinal);
            public List<string> DesiredKeys = new List<string>();
            public Dictionary<string, string> LevelNames = new Dictionary<string, string>(StringComparer.Ordinal);
            public HashSet<string> RangeLevelIds = new HashSet<string>(StringComparer.Ordinal);
            public List<string> SkippedLevelNames = new List<string>();
            public Dictionary<string, View> TaggedViews = new Dictionary<string, View>(StringComparer.Ordinal);

            /// <summary>
            /// Of Diff.UpdateKeys (views this set already owns), the ones with no
            /// viewport on any sheet right now — e.g. a level that just became this
            /// set's source under DiagramSourceLevelsOnly, whose own per-level view
            /// from an earlier run never had a slot. Placed exactly like a new view.
            /// </summary>
            public List<string> UnplacedUpdateKeys = new List<string>();

            /// <summary>Matrix sheets this (package, building) owns, in series order.</summary>
            public List<ViewSheet> PackageSheets = new List<ViewSheet>();

            /// <summary>1×1 sheets of additional views this (package, building) owns.</summary>
            public List<ViewSheet> OwnSheets = new List<ViewSheet>();

            public int NextSheetIndex;

            public List<ExtraView> ExtrasShared = new List<ExtraView>();
            public List<ExtraView> ExtrasOwn = new List<ExtraView>();
            public List<ExtraView> ExtrasAlreadyPlaced = new List<ExtraView>();
            public List<string> ExtraSkips = new List<string>();

            public SheetDiffPlan Diff;
            public IReadOnlyList<SlotRect> Slots;
            public IReadOnlyList<SlotRect> OwnSheetSlots;
            public double SheetWidthIn;
            public double SheetHeightIn;
            public int Scale;
            public bool ScaleIsOverride;
            public bool ScaleIsFromTemplate;
            public int SavedPositionsReleased;

            // Area tables
            public ViewSchedule TableTemplate;
            public string TableBuildingValue = string.Empty;
            public List<AreaTableSpec> Tables = new List<AreaTableSpec>();
            public List<AreaTableSpec> TablesNeedingCell = new List<AreaTableSpec>();
            public int OrphanTableCount;
            public IReadOnlyList<SlotRect> CellSlots;
            public double ViewAreaWidthIn;
            public double ViewAreaHeightIn;
            public double ViewOffsetXIn;
            public double ViewOffsetYIn;
            public Dictionary<string, string> ViewSheetNumbers = new Dictionary<string, string>(StringComparer.Ordinal);
            public int FreeSlotsOnLastSheet;
            public IReadOnlyList<SlotAssignment> Assignments;
            public int MatrixNewSheetCount;
            public int NewSheetCount;
            public SheetNumberAllocation Numbers;
            public HashSet<string> ExistingViewNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public List<string> Notes = new List<string>();

            public ViewSheet LastMatrixSheet => PackageSheets.Count == 0 ? null : PackageSheets[PackageSheets.Count - 1];

            public string LevelName(string levelUniqueId) =>
                LevelNames.TryGetValue(levelUniqueId, out string name) ? name : "(deleted level)";

            public string ViewNameSuffix = string.Empty;

            public Dictionary<string, string> RangeViewLabels = new Dictionary<string, string>();

            /// <summary>The level part of a view's name: the range label for a range's source level, else the level name without its sort prefix.</summary>
            public string ViewLabel(string levelUniqueId)
            {
                if (RangeViewLabels.TryGetValue(levelUniqueId, out string range))
                {
                    return range;
                }
                string levelName = LevelNames.TryGetValue(levelUniqueId, out string name) ? name : "(deleted level)";
                return SheetNaming.StripLevelNumber(levelName);
            }

            /// <summary>
            /// The name the view for this level gets: "2ND FL. GROSS AREA". When another view (not this one) already
            /// has that name, the building is appended so the two never collide.
            /// </summary>
            public string ViewNameFor(string levelUniqueId, string buildingName, View self = null)
            {
                string clean = SheetNaming.ComposeViewName(ViewLabel(levelUniqueId), ViewNameSuffix);
                bool takenByOther = ExistingViewNames.Contains(clean)
                    && !(self != null && string.Equals(self.Name, clean, StringComparison.OrdinalIgnoreCase));
                return takenByOther ? clean + " - " + (buildingName ?? string.Empty).Trim().ToUpperInvariant() : clean;
            }

            public void CollectSources(Document doc, Func<ViewPlan, bool> matches)
            {
                foreach (ViewPlan view in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                    .Where(v => !v.IsTemplate && v.GenLevel != null && matches(v))
                    .OrderBy(v => v.GenLevel.Elevation).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
                {
                    string key = view.GenLevel.UniqueId;
                    if (SourceViews.ContainsKey(key))
                    {
                        continue;
                    }
                    SourceViews.Add(key, view);
                    DesiredKeys.Add(key);
                    LevelNames[key] = view.GenLevel.Name;
                }
            }

            /// <summary>Every level any Floor Range covers (sources and targets alike).</summary>
            public HashSet<string> AllRangeLevelIds;

            /// <summary>Range levels with no view of their own because their range's source view stands for them.</summary>
            public List<string> CoveredLevelNames = new List<string>();

            public void RestrictToFloorRanges()
            {
                var kept = new List<string>();
                foreach (string key in DesiredKeys)
                {
                    if (RangeLevelIds.Contains(key))
                    {
                        kept.Add(key);
                    }
                    else
                    {
                        if (AllRangeLevelIds != null && AllRangeLevelIds.Contains(key))
                        {
                            CoveredLevelNames.Add(LevelName(key));
                        }
                        else
                        {
                            SkippedLevelNames.Add(LevelName(key));
                        }
                        SourceViews.Remove(key);
                    }
                }
                DesiredKeys = kept;
            }

            /// <summary>View type used to create a floor/ceiling plan on a range level that has none.</summary>
            public ElementId PlanViewFamilyTypeId;

            /// <summary>Range levels with no source view that this run will create a plan for.</summary>
            public List<string> CreatedFromScratchKeys = new List<string>();

            /// <summary>
            /// Every level of the building's floor ranges must end up either
            /// drawn or reported. Floor/ceiling plans: a level without a plan
            /// gets one created. Area plans: only Atypical (single) floors are
            /// created here — nothing else ever propagates into them; a typical
            /// or duplex level without an area plan means Step 2 has not run, and
            /// that is said out loud instead of leaving the level out.
            /// </summary>
            public void AddMissingRangeLevels(Document doc, BuildingDefinition building, SheetSourceKind kind)
            {
                // Every kind alike: a range level without a plan gets one. For area plans
                // it is created EMPTY — ZFA Step 2 later propagates areas into that same
                // plan instead of creating another — and the preview says so.
                bool added = false;
                foreach (string uid in RangeLevelIds)
                {
                    if (DesiredKeys.Contains(uid) || !(doc.GetElement(uid) is Level level))
                    {
                        continue;
                    }
                    DesiredKeys.Add(uid);
                    LevelNames[uid] = level.Name;
                    CreatedFromScratchKeys.Add(uid);
                    added = true;
                }
                if (added)
                {
                    DesiredKeys = DesiredKeys.OrderBy(key => doc.GetElement(key) is Level l ? l.Elevation : double.MaxValue).ToList();
                    CreatedFromScratchKeys = CreatedFromScratchKeys.OrderBy(key => doc.GetElement(key) is Level l ? l.Elevation : double.MaxValue).ToList();
                }
            }

            public void CollectTagged(Document doc, Guid packageId, Guid buildingId)
            {
                foreach (View view in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                {
                    if (view.IsTemplate
                        || !PropagatedElementTag.TryRead(view, out string identity, out Guid taggedBuilding, out Guid taggedPackage)
                        || taggedBuilding != buildingId || taggedPackage != packageId)
                    {
                        continue;
                    }
                    if (SheetTagIdentity.TryParseView(identity, out _, out _, out string levelUid) && !TaggedViews.ContainsKey(levelUid))
                    {
                        TaggedViews.Add(levelUid, view);
                        if (!LevelNames.ContainsKey(levelUid))
                        {
                            LevelNames[levelUid] = doc.GetElement(levelUid) is Level level ? level.Name : view.Name;
                        }
                    }
                }

                var matrix = new List<KeyValuePair<int, ViewSheet>>();
                var own = new List<KeyValuePair<int, ViewSheet>>();
                foreach (ViewSheet sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
                {
                    if (!PropagatedElementTag.TryRead(sheet, out string identity, out Guid taggedBuilding, out Guid taggedPackage)
                        || taggedBuilding != buildingId || taggedPackage != packageId)
                    {
                        continue;
                    }
                    if (SheetTagIdentity.TryParseSheet(identity, out int index))
                    {
                        matrix.Add(new KeyValuePair<int, ViewSheet>(index, sheet));
                    }
                    else if (SheetTagIdentity.TryParseOwnSheet(identity, out int ownIndex, out _))
                    {
                        own.Add(new KeyValuePair<int, ViewSheet>(ownIndex, sheet));
                    }
                }
                matrix.Sort((a, b) => a.Key.CompareTo(b.Key));
                own.Sort((a, b) => a.Key.CompareTo(b.Key));
                PackageSheets.AddRange(matrix.Select(p => p.Value));
                OwnSheets.AddRange(own.Select(p => p.Value));
                int maxIndex = matrix.Concat(own).Select(p => p.Key).DefaultIfEmpty(-1).Max();
                NextSheetIndex = maxIndex + 1;
            }
        }
    }
}
