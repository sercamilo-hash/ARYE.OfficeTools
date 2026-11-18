using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Results;
using ARYE.OfficeTools.Core.Sheets;
using ARYE.OfficeTools.Storage;

namespace ARYE.OfficeTools.Services
{
    /// <summary>The area tables of a set: planning (what room they take), naming, creating and placing.</summary>
    internal sealed partial class SheetPackageService
    {
        // ── Planning (Resolve) ───────────────────────────────────────────────

        /// <summary>
        /// Measures every table the run would make and takes their room from the views, so the scale is chosen for the
        /// space that is really left. Returns false, with the reason in <c>plan.BlockReason</c>, when nothing can be drawn.
        /// </summary>
        private static bool PlanAreaTables(Document doc, SheetPackage package, BuildingDefinition building, RunPlan plan,
            double usableWidthIn, double usableHeightIn)
        {
            plan.CellSlots = plan.Slots;
            plan.ViewAreaWidthIn = usableWidthIn;
            plan.ViewAreaHeightIn = usableHeightIn;
            if (!package.AddAreaTable)
            {
                return true;
            }

            plan.TableTemplate = string.IsNullOrEmpty(package.AreaTableTemplateUniqueId)
                ? null : doc.GetElement(package.AreaTableTemplateUniqueId) as ViewSchedule;
            if (plan.TableTemplate == null)
            {
                plan.BlockReason = string.IsNullOrEmpty(package.AreaTableTemplateUniqueId)
                    ? "This set adds an area table to each view but no table to copy is picked — choose one in the set editor, or turn the area table off."
                    : "The table this set copies no longer exists in this model — pick one again.";
                return false;
            }
            plan.TableBuildingValue = string.IsNullOrWhiteSpace(package.AreaTableBuildingValue) ? building.Name : package.AreaTableBuildingValue.Trim();

            Dictionary<string, ViewSchedule> owned = AreaTableService.CollectOwned(doc, package.Id, building.Id);
            foreach (string key in plan.DesiredKeys)
            {
                var spec = new AreaTableSpec
                {
                    Key = key, Level = doc.GetElement(key) as Level, LevelName = plan.LevelName(key),
                    Existing = owned.TryGetValue(key, out ViewSchedule existing) ? existing : null
                };
                if (spec.Level == null)
                {
                    spec.Problem = "its level no longer exists";
                }
                else if (!AreaTableService.TryMeasureCached(doc, plan.TableTemplate, spec.Level, plan.TableBuildingValue,
                    out double widthIn, out double heightIn, out int rows, out string problem))
                {
                    spec.Problem = problem ?? "the table came out empty";
                }
                else
                {
                    spec.WidthIn = widthIn;
                    spec.HeightIn = heightIn;
                    spec.Rows = rows;
                }
                plan.Tables.Add(spec);
            }
            plan.OrphanTableCount = owned.Keys.Count(k => !plan.DesiredKeys.Contains(k));

            List<AreaTableSpec> good = plan.Tables.Where(t => t.Problem == null).ToList();
            if (good.Count == 0)
            {
                return true;
            }
            double maxWidth = good.Max(t => t.WidthIn), maxHeight = good.Max(t => t.HeightIn);
            double gap = Math.Max(0, package.AreaTableGapIn);

            switch (package.AreaTablePlacement)
            {
                case AreaTablePlacement.UnderView:
                case AreaTablePlacement.BesideView:
                case AreaTablePlacement.BesideViewLeft:
                {
                    var viewSlots = new List<SlotRect>();
                    foreach (SlotRect cell in plan.CellSlots)
                    {
                        SlotRect view = AreaTableLayout.SplitCell(cell, package.AreaTablePlacement, maxWidth, maxHeight, gap).View;
                        if (view.WidthIn < 1 || view.HeightIn < 1)
                        {
                            plan.BlockReason = $"The area tables ({maxWidth:0.#} x {maxHeight:0.#} in) leave no room for the view in a "
                                + $"{cell.WidthIn:0.#} x {cell.HeightIn:0.#} in cell — use fewer columns or rows, put the tables in a cell of their own or in a fixed place, or copy a smaller table.";
                            return false;
                        }
                        viewSlots.Add(view);
                    }
                    plan.Slots = viewSlots;
                    break;
                }
                case AreaTablePlacement.FixedEdge:
                {
                    AreaReduction reduced = AreaTableLayout.ReduceArea(usableWidthIn, usableHeightIn, package.AreaTableEdge, maxWidth, maxHeight, gap);
                    if (reduced.ViewWidthIn < 1 || reduced.ViewHeightIn < 1)
                    {
                        plan.BlockReason = "The area tables take the whole view area: copy a smaller table, change its edge, or put the tables in a cell of their own.";
                        return false;
                    }
                    try
                    {
                        plan.Slots = SheetSlotLayout.ComputeGrid(reduced.ViewWidthIn, reduced.ViewHeightIn, package.Columns, package.Rows,
                            package.ColumnCumulative, package.RowCumulative);
                    }
                    catch (ArgumentException ex)
                    {
                        plan.BlockReason = ex.Message + " (after the area tables took their edge)";
                        return false;
                    }
                    plan.ViewAreaWidthIn = reduced.ViewWidthIn;
                    plan.ViewAreaHeightIn = reduced.ViewHeightIn;
                    plan.ViewOffsetXIn = reduced.OffsetXIn;
                    plan.ViewOffsetYIn = reduced.OffsetYIn;
                    break;
                }
            }

            foreach (AreaTableSpec table in good)
            {
                if (package.AreaTablePlacement == AreaTablePlacement.OwnCell
                    && AreaTableLayout.ExceedsRoom(new TableRect { WidthIn = table.WidthIn, HeightIn = table.HeightIn }, plan.CellSlots.Min(c => c.WidthIn), plan.CellSlots.Min(c => c.HeightIn)))
                {
                    plan.Notes.Add($"The table for {plan.LevelName(table.Key)} ({table.WidthIn:0.#} x {table.HeightIn:0.#} in, {table.Rows} rows) is bigger than a cell and will run past it.");
                }
                else if ((package.AreaTablePlacement == AreaTablePlacement.UnderView && table.WidthIn > plan.CellSlots.Min(c => c.WidthIn) + 1e-6)
                      || ((package.AreaTablePlacement == AreaTablePlacement.BesideView || package.AreaTablePlacement == AreaTablePlacement.BesideViewLeft)
                          && table.HeightIn > plan.CellSlots.Min(c => c.HeightIn) + 1e-6))
                {
                    plan.Notes.Add($"The table for {plan.LevelName(table.Key)} ({table.WidthIn:0.#} x {table.HeightIn:0.#} in) is bigger than its cell and will run past it.");
                }
            }
            if (package.AreaTablePlacement == AreaTablePlacement.FixedEdge)
            {
                int perSheet = Math.Max(1, package.Columns * package.Rows);
                AreaTableLayout.Stack(package.AreaTableEdge, usableWidthIn, usableHeightIn,
                    good.Take(perSheet).Select(t => (t.WidthIn, t.HeightIn)).ToList(), gap, out bool overflows);
                if (overflows)
                {
                    plan.Notes.Add($"{perSheet} tables on one sheet do not fit along the {package.AreaTableEdge.ToString().ToLowerInvariant()} edge — "
                        + "the last ones will run past the usable area. Use a bigger edge, fewer cells per sheet or a smaller table.");
                }
            }
            return true;
        }

        /// <summary>Gives every table its final name, once the model's view names are known. A name another view holds gets the building appended.</summary>
        private static void NameAreaTables(RunPlan plan, SheetPackage package, BuildingDefinition building)
        {
            if (!package.AddAreaTable || plan.Tables.Count == 0)
            {
                return;
            }
            var taken = new HashSet<string>(plan.ExistingViewNames, StringComparer.OrdinalIgnoreCase);
            foreach (AreaTableSpec table in plan.Tables.Where(t => t.Existing != null))
            {
                taken.Remove(table.Existing.Name); // a table this set owns does not collide with itself
            }
            foreach (AreaTableSpec table in plan.Tables)
            {
                string label = SheetNaming.FloorWord(plan.ViewLabel(table.Key));
                string clean = SheetNaming.ComposeTableName(package.AreaTableNamePattern, package.Name, building.Name, plan.TableBuildingValue, label);
                string name = taken.Contains(clean) ? clean + " - " + (building.Name ?? string.Empty).Trim().ToUpperInvariant() : clean;
                if (taken.Contains(name))
                {
                    table.Problem = table.Problem ?? $"another view is already named '{name}'";
                }
                table.Name = name;
                taken.Add(name);
            }
            foreach (AreaTableSpec table in plan.Tables.Where(t => t.Problem != null))
            {
                plan.Notes.Add($"No area table for {table.LevelName}: {table.Problem}.");
            }
            if (plan.OrphanTableCount > 0)
            {
                plan.Notes.Add($"{plan.OrphanTableCount} area table(s) this set made earlier are for levels it no longer draws — kept exactly as they are.");
            }
        }

        /// <summary>Tables that need a matrix cell of their own: OwnCell placement, not on a sheet yet, buildable.</summary>
        private static List<AreaTableSpec> TablesNeedingACell(Document doc, SheetPackage package, RunPlan plan)
        {
            if (!package.AddAreaTable || package.AreaTablePlacement != AreaTablePlacement.OwnCell)
            {
                return new List<AreaTableSpec>();
            }
            return plan.Tables.Where(t => t.Problem == null && (t.Existing == null || AreaTableService.FindPlaced(doc, t.Existing) == null)).ToList();
        }

        // ── Generate ─────────────────────────────────────────────────────────

        /// <summary>Creates or updates every table of the run and puts the new ones on the sheets, where the set's position says.</summary>
        private void GenerateAreaTables(Document doc, SheetPackage package, BuildingDefinition building, RunPlan plan,
            List<ViewSheet> newSheets, Dictionary<string, View> viewOf, int ownCellBaseIndex, DateTime runUtc,
            OperationResult<PlacedViewportInfo> result)
        {
            if (!package.AddAreaTable || plan.TableTemplate == null || plan.Tables.Count == 0)
            {
                return;
            }

            foreach (AreaTableSpec table in plan.Tables.Where(t => t.Problem == null))
            {
                try
                {
                    table.Schedule = EnsureTable(doc, package, building, plan, table, runUtc, result);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    result.Failed.Add(new OperationFailure("Area table " + table.Name, ex));
                }
            }
            doc.Regenerate();

            var viewportOf = new Dictionary<long, Viewport>();
            foreach (Viewport viewport in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                if (!viewportOf.ContainsKey(viewport.ViewId.Value)) viewportOf[viewport.ViewId.Value] = viewport;
            }

            bool fixedEdge = package.AreaTablePlacement == AreaTablePlacement.FixedEdge;
            List<AreaTableSpec> ownCellQueue = plan.TablesNeedingCell;

            // Where each table goes: its sheet and the cell it belongs to.
            var spots = new List<TableSpot>();
            foreach (AreaTableSpec table in plan.Tables.Where(t => t.Schedule != null))
            {
                ScheduleSheetInstance placed = AreaTableService.FindPlaced(doc, table.Schedule);
                var spot = new TableSpot { Table = table, Instance = placed, WasPlaced = placed != null };
                if (placed != null)
                {
                    spot.Sheet = doc.GetElement(placed.OwnerViewId) as ViewSheet;
                    spots.Add(spot);
                    continue;
                }
                if (package.AreaTablePlacement == AreaTablePlacement.OwnCell)
                {
                    int queued = ownCellQueue.IndexOf(table);
                    int index = ownCellBaseIndex + queued;
                    spot.Sheet = queued < 0 ? null : TargetSheet(plan, newSheets, index);
                    spot.CellIndex = queued < 0 || index >= plan.Assignments.Count ? -1 : plan.Assignments[index].SlotIndex;
                    if (spot.Sheet == null || spot.CellIndex < 0)
                    {
                        result.Skipped.Add(new OperationSkip("Area table " + table.Name, "its cell could not be assigned — the table exists but is not on a sheet"));
                        continue;
                    }
                }
                else
                {
                    if (!viewOf.TryGetValue(table.Key, out View view) || !viewportOf.TryGetValue(view.Id.Value, out Viewport viewport)
                        || !(doc.GetElement(viewport.SheetId) is ViewSheet viewSheet))
                    {
                        result.Skipped.Add(new OperationSkip("Area table " + table.Name, "its view is not on a sheet, so the table has no place — the table exists but is not on a sheet"));
                        continue;
                    }
                    spot.Sheet = viewSheet;
                    (double _, double _, XYZ usableOrigin) = MeasureUsableArea(doc, viewSheet, plan);
                    XYZ origin = fixedEdge ? OffsetBy(usableOrigin, plan.ViewOffsetXIn, plan.ViewOffsetYIn) : usableOrigin;
                    spot.CellIndex = NearestCell(origin, fixedEdge ? plan.Slots : plan.CellSlots, viewport.GetBoxCenter());
                }
                spots.Add(spot);
            }

            // Put the new tables on their sheets at the origin first, so their real size can be read.
            foreach (TableSpot spot in spots.Where(s => !s.WasPlaced))
            {
                try
                {
                    spot.Instance = ScheduleSheetInstance.Create(doc, spot.Sheet.Id, spot.Table.Schedule.Id, XYZ.Zero);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    result.Failed.Add(new OperationFailure("Area table " + spot.Table.Name, ex));
                }
            }
            doc.Regenerate();

            foreach (TableSpot spot in spots.Where(s => s.Instance != null))
            {
                (spot.WidthIn, spot.HeightIn) = AreaTableService.SizeOf(spot.Instance, spot.Sheet);
            }

            double gap = Math.Max(0, package.AreaTableGapIn);
            foreach (IGrouping<long, TableSpot> onSheet in spots.Where(s => s.Instance != null).GroupBy(s => s.Sheet.Id.Value))
            {
                ViewSheet sheet = onSheet.First().Sheet;
                (double _, double _, XYZ usableOrigin) = MeasureUsableArea(doc, sheet, plan);
                List<TableSpot> ordered = onSheet.OrderBy(s => s.CellIndex).ToList();
                IReadOnlyList<TableRect> rects;
                if (fixedEdge)
                {
                    rects = AreaTableLayout.Stack(package.AreaTableEdge, plan.UsableWidthIn, plan.UsableHeightIn,
                        ordered.Select(s => (s.WidthIn, s.HeightIn)).ToList(), gap, out bool overflows);
                    if (overflows)
                    {
                        result.Skipped.Add(new OperationSkip("Area tables on sheet " + sheet.SheetNumber,
                            $"they do not all fit along the {package.AreaTableEdge.ToString().ToLowerInvariant()} edge — the last ones run past the usable area"));
                    }
                }
                else
                {
                    rects = ordered.Select(s => RectFor(package, plan, s, gap)).ToList();
                }

                for (int i = 0; i < ordered.Count; i++)
                {
                    TableSpot spot = ordered[i];
                    if (!spot.WasPlaced)
                    {
                        var topLeft = new XYZ(
                            usableOrigin.X + UnitUtils.ConvertToInternalUnits(rects[i].LeftIn, UnitTypeId.Inches),
                            usableOrigin.Y + UnitUtils.ConvertToInternalUnits(rects[i].TopIn, UnitTypeId.Inches), 0);
                        try
                        {
                            AreaTableService.MoveTopLeftTo(doc, spot.Instance, sheet, topLeft);
                        }
                        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                        {
                            result.Failed.Add(new OperationFailure("Area table " + spot.Table.Name, ex));
                            continue;
                        }
                        if (!fixedEdge && AreaTableLayout.ExceedsRoom(rects[i], RoomWidth(package, plan, spot), RoomHeight(package, plan, spot)))
                        {
                            result.Skipped.Add(new OperationSkip(spot.Table.Name, "the table is bigger than the room its cell has for it — it runs past the cell"));
                        }
                    }
                    result.Succeeded.Add(new PlacedViewportInfo
                    {
                        ViewName = spot.Table.Name + (spot.WasPlaced ? " (table, already on the sheet — not moved)" : " (table)"),
                        SheetNumber = sheet.SheetNumber, ScaleDenominator = 0
                    });
                }
            }
        }

        private sealed class TableSpot
        {
            public AreaTableSpec Table;
            public ViewSheet Sheet;
            public ScheduleSheetInstance Instance;
            public bool WasPlaced;
            public int CellIndex;
            public double WidthIn;
            public double HeightIn;
        }

        private static TableRect RectFor(SheetPackage package, RunPlan plan, TableSpot spot, double gap)
        {
            SlotRect cell = plan.CellSlots[Math.Max(0, Math.Min(spot.CellIndex, plan.CellSlots.Count - 1))];
            switch (package.AreaTablePlacement)
            {
                case AreaTablePlacement.OwnCell:
                    return AreaTableLayout.InCell(cell, spot.WidthIn, spot.HeightIn);
                default:
                    return AreaTableLayout.SplitCell(cell, package.AreaTablePlacement, spot.WidthIn, spot.HeightIn, gap).Table;
            }
        }

        private static double RoomWidth(SheetPackage package, RunPlan plan, TableSpot spot) =>
            plan.CellSlots[Math.Max(0, Math.Min(spot.CellIndex, plan.CellSlots.Count - 1))].WidthIn;

        private static double RoomHeight(SheetPackage package, RunPlan plan, TableSpot spot) =>
            plan.CellSlots[Math.Max(0, Math.Min(spot.CellIndex, plan.CellSlots.Count - 1))].HeightIn;

        private static XYZ OffsetBy(XYZ origin, double offsetXIn, double offsetYIn) => new XYZ(
            origin.X + UnitUtils.ConvertToInternalUnits(offsetXIn, UnitTypeId.Inches),
            origin.Y + UnitUtils.ConvertToInternalUnits(offsetYIn, UnitTypeId.Inches), origin.Z);

        private static int NearestCell(XYZ origin, IReadOnlyList<SlotRect> cells, XYZ boxCenter)
        {
            int best = 0;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                var center = new XYZ(
                    origin.X + UnitUtils.ConvertToInternalUnits(cells[i].CenterXIn, UnitTypeId.Inches),
                    origin.Y + UnitUtils.ConvertToInternalUnits(cells[i].CenterYIn, UnitTypeId.Inches), 0);
                double distance = (center - new XYZ(boxCenter.X, boxCenter.Y, 0)).GetLength();
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Makes the table exist and carry what the set says: filters, name, view template, browser values. Never moves it.</summary>
        private static ViewSchedule EnsureTable(Document doc, SheetPackage package, BuildingDefinition building, RunPlan plan,
            AreaTableSpec table, DateTime runUtc, OperationResult<PlacedViewportInfo> result)
        {
            ViewSchedule schedule = table.Existing;
            bool created = false;
            if (schedule == null)
            {
                schedule = (ViewSchedule)doc.GetElement(plan.TableTemplate.Duplicate(ViewDuplicateOption.Duplicate));
                created = true;
            }

            string problem = AreaTableService.ApplyFilters(doc, schedule, table.Level.Id, plan.TableBuildingValue);
            if (problem != null)
            {
                if (created) doc.Delete(schedule.Id);
                result.Failed.Add(new OperationFailure("Area table " + table.Name, problem));
                return null;
            }

            if (!string.Equals(schedule.Name, table.Name, StringComparison.Ordinal))
            {
                string old = schedule.Name;
                try
                {
                    schedule.Name = table.Name;
                    plan.ExistingViewNames.Remove(old);
                    plan.ExistingViewNames.Add(table.Name);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    result.Skipped.Add(new OperationSkip(old, $"not renamed to '{table.Name}': {ex.Message}"));
                }
            }

            if (!string.IsNullOrEmpty(package.AreaTableViewTemplateUniqueId))
            {
                if (doc.GetElement(package.AreaTableViewTemplateUniqueId) is View template && template.IsTemplate)
                {
                    try
                    {
                        if (schedule.ViewTemplateId != template.Id) schedule.ViewTemplateId = template.Id;
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        result.Skipped.Add(new OperationSkip(table.Name, $"the table template '{template.Name}' could not be applied: {ex.Message}"));
                    }
                }
                else
                {
                    result.Skipped.Add(new OperationSkip(table.Name, "the table template no longer exists in this model — none applied"));
                }
            }

            Parameter buildingParameter = schedule.LookupParameter("Building");
            if (buildingParameter != null && !buildingParameter.IsReadOnly && buildingParameter.StorageType == StorageType.String
                && !string.IsNullOrWhiteSpace(plan.TableBuildingValue)
                && !string.Equals(buildingParameter.AsString(), plan.TableBuildingValue, StringComparison.Ordinal))
            {
                buildingParameter.Set(plan.TableBuildingValue);
            }
            ApplyParameterValues(schedule, package.ViewParameterValues, result);

            if (created)
            {
                PropagatedElementTag.Write(schedule, AreaTableService.TagIdentity(table.Key), building.Id, package.Id, runUtc);
            }
            return schedule;
        }
    }
}
