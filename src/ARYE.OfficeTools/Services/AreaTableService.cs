using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Core.Results;
using ARYE.OfficeTools.Storage;

namespace ARYE.OfficeTools.Services
{
    /// <summary>One area table of a run: the level it lists, the table that already exists (if any) and its measured size.</summary>
    internal sealed class AreaTableSpec
    {
        /// <summary>UniqueId of the level the table lists: the source level of the view it belongs to.</summary>
        public string Key;

        public Level Level;

        public string LevelName;

        /// <summary>Final name, after collisions are resolved.</summary>
        public string Name;

        /// <summary>The table this set made on an earlier run, or null when the run has to create it.</summary>
        public ViewSchedule Existing;

        public double WidthIn;

        public double HeightIn;

        public int Rows;

        /// <summary>Why the table cannot be built, or null.</summary>
        public string Problem;

        /// <summary>The table once it exists (created or updated by this run).</summary>
        public ViewSchedule Schedule;

        public bool IsNew => Existing == null;
    }

    /// <summary>
    /// Area tables for a sheet set. Each table is a COPY of a schedule the user picked, so its columns,
    /// sorting, grouping, totals and formatting stay theirs; only the Building and Level filters, the name,
    /// the browser values and the view template change. A table is "ours" by the same Extensible Storage tag the
    /// set's views carry, so a re-run updates it in place and never touches a schedule it did not make.
    /// </summary>
    internal static class AreaTableService
    {
        public const string TagPrefix = "area-table|";

        private const string LevelFieldName = "Level";
        private const string BuildingFieldName = "Building";

        public static string TagIdentity(string levelUniqueId) => TagPrefix + levelUniqueId;

        /// <summary>The tables this set already made for this building, by the level UniqueId they list.</summary>
        public static Dictionary<string, ViewSchedule> CollectOwned(Document doc, Guid packageId, Guid buildingId)
        {
            var owned = new Dictionary<string, ViewSchedule>(StringComparer.Ordinal);
            foreach (ViewSchedule schedule in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
            {
                if (schedule.IsTemplate
                    || !PropagatedElementTag.TryRead(schedule, out string identity, out Guid taggedBuilding, out Guid taggedPackage)
                    || taggedBuilding != buildingId || taggedPackage != packageId
                    || identity == null || !identity.StartsWith(TagPrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                string key = identity.Substring(TagPrefix.Length);
                if (!owned.ContainsKey(key))
                {
                    owned.Add(key, schedule);
                }
            }
            return owned;
        }

        /// <summary>True when the schedule is a table this tool made (of any set), so it is never offered as a table to copy.</summary>
        public static bool IsOurs(ViewSchedule schedule) =>
            PropagatedElementTag.TryRead(schedule, out string identity, out _, out _)
            && identity != null && identity.StartsWith(TagPrefix, StringComparison.Ordinal);

        /// <summary>Sets the Building and Level filters of a table. Returns null on success, otherwise the reason.</summary>
        public static string ApplyFilters(Document doc, ViewSchedule schedule, ElementId levelId, string buildingValue)
        {
            ScheduleDefinition definition = schedule.Definition;
            ScheduleFieldId levelField = EnsureField(doc, definition, LevelFieldName, out string levelProblem);
            if (levelField == null) return levelProblem;
            SetOrAddFilter(definition, levelField, new ScheduleFilter(levelField, ScheduleFilterType.Equal, levelId));

            if (!string.IsNullOrWhiteSpace(buildingValue))
            {
                ScheduleFieldId buildingField = EnsureField(doc, definition, BuildingFieldName, out string buildingProblem);
                if (buildingField == null) return buildingProblem;
                SetOrAddFilter(definition, buildingField, new ScheduleFilter(buildingField, ScheduleFilterType.Equal, buildingValue));
            }
            return null;
        }

        private static void SetOrAddFilter(ScheduleDefinition definition, ScheduleFieldId field, ScheduleFilter filter)
        {
            for (int i = 0; i < definition.GetFilterCount(); i++)
            {
                if (definition.GetFilter(i).FieldId.IntegerValue == field.IntegerValue)
                {
                    definition.SetFilter(i, filter);
                    return;
                }
            }
            definition.AddFilter(filter);
        }

        /// <summary>The field of that name; when the table does not carry it, it is added hidden so it can be filtered on.</summary>
        private static ScheduleFieldId EnsureField(Document doc, ScheduleDefinition definition, string name, out string problem)
        {
            problem = null;
            for (int i = 0; i < definition.GetFieldCount(); i++)
            {
                ScheduleField field = definition.GetField(i);
                if (string.Equals(field.GetName(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return field.FieldId;
                }
            }
            SchedulableField schedulable = definition.GetSchedulableFields()
                .FirstOrDefault(f => string.Equals(f.GetName(doc), name, StringComparison.OrdinalIgnoreCase));
            if (schedulable == null)
            {
                problem = $"the table has no '{name}' field and its category does not offer one";
                return null;
            }
            ScheduleField added = definition.AddField(schedulable);
            added.IsHidden = true;
            return added.FieldId;
        }

        /// <summary>
        /// Builds one table in a scratch copy, puts it on a scratch sheet and reads its real size, then rolls the
        /// whole thing back: the model is left exactly as it was. The size is the table as it prints, in inches.
        /// </summary>
        public static bool TryMeasure(Document doc, ViewSchedule template, ElementId levelId, string buildingValue,
            out double widthIn, out double heightIn, out int rows, out string problem)
        {
            widthIn = heightIn = 0;
            rows = 0;
            problem = null;
            using (var tx = new Transaction(doc, "BauTools: measure area table (rolled back)"))
            {
                tx.Start();
                new RevitFailureCollector(new OperationResult<string>(), "area table measure").Attach(tx);
                try
                {
                    var copy = (ViewSchedule)doc.GetElement(template.Duplicate(ViewDuplicateOption.Duplicate));
                    problem = ApplyFilters(doc, copy, levelId, buildingValue);
                    if (problem != null) return false;
                    doc.Regenerate();
                    ViewSheet scratch = ViewSheet.Create(doc, ElementId.InvalidElementId);
                    ScheduleSheetInstance instance = ScheduleSheetInstance.Create(doc, scratch.Id, copy.Id, XYZ.Zero);
                    doc.Regenerate();
                    (widthIn, heightIn) = SizeOf(instance, scratch);
                    rows = BodyRows(copy);
                    return widthIn > 0 && heightIn > 0;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    problem = ex.Message;
                    return false;
                }
                finally
                {
                    tx.RollBack();
                }
            }
        }

        private static readonly Dictionary<string, (bool Ok, double WidthIn, double HeightIn, int Rows, string Problem)> MeasureCache =
            new Dictionary<string, (bool, double, double, int, string)>(StringComparer.Ordinal);

        /// <summary>
        /// <see cref="TryMeasure"/>, remembered for as long as the model does not change: the preview re-runs on every
        /// edit in the window, and copying a schedule per level each time would make it crawl. The key includes the
        /// document's version, so any change to the model (new areas, an edited table) measures again.
        /// </summary>
        public static bool TryMeasureCached(Document doc, ViewSchedule template, Level level, string buildingValue,
            out double widthIn, out double heightIn, out int rows, out string problem)
        {
            string key = Document.GetDocumentVersion(doc).VersionGUID + "|" + template.UniqueId + "|" + level.UniqueId + "|" + buildingValue;
            if (MeasureCache.TryGetValue(key, out var hit))
            {
                (bool ok, widthIn, heightIn, rows, problem) = hit;
                return ok;
            }
            bool measured = TryMeasure(doc, template, level.Id, buildingValue, out widthIn, out heightIn, out rows, out problem);
            if (MeasureCache.Count > 400)
            {
                MeasureCache.Clear();
            }
            MeasureCache[key] = (measured, widthIn, heightIn, rows, problem);
            return measured;
        }

        /// <summary>Size of a table on a sheet, inches.</summary>
        public static (double WidthIn, double HeightIn) SizeOf(ScheduleSheetInstance instance, ViewSheet sheet)
        {
            BoundingBoxXYZ box = instance.get_BoundingBox(sheet);
            if (box == null) return (0, 0);
            return (UnitUtils.ConvertFromInternalUnits(box.Max.X - box.Min.X, UnitTypeId.Inches),
                    UnitUtils.ConvertFromInternalUnits(box.Max.Y - box.Min.Y, UnitTypeId.Inches));
        }

        private static int BodyRows(ViewSchedule schedule)
        {
            try
            {
                TableSectionData body = schedule.GetTableData().GetSectionData(SectionType.Body);
                return body?.NumberOfRows ?? 0;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return 0;
            }
        }

        /// <summary>The placed instance of a table (on any sheet), or null when it is not on a sheet yet.</summary>
        public static ScheduleSheetInstance FindPlaced(Document doc, ViewSchedule schedule) =>
            new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                .FirstOrDefault(i => i.ScheduleId == schedule.Id);

        /// <summary>Moves a placed table so its top-left corner lands on <paramref name="topLeftFt"/> (sheet feet).</summary>
        public static void MoveTopLeftTo(Document doc, ScheduleSheetInstance instance, ViewSheet sheet, XYZ topLeftFt)
        {
            BoundingBoxXYZ box = instance.get_BoundingBox(sheet);
            if (box == null) return;
            var delta = new XYZ(topLeftFt.X - box.Min.X, topLeftFt.Y - box.Max.Y, 0);
            if (delta.GetLength() > 1e-6)
            {
                ElementTransformUtils.MoveElement(doc, instance.Id, delta);
            }
        }
    }
}
