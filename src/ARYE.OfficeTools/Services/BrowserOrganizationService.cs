using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Infrastructure;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// Reads how the ACTIVE Project Browser organization groups views and
    /// sheets — the parameters the office template actually uses ("View
    /// Group", "Sub-Discipline", …) — and the values already in use. Nothing
    /// is hardcoded: a template that groups by other parameters shows those.
    /// Also inventories Sheet Collections and the views a package may place.
    /// Read-only; no transaction.
    /// </summary>
    internal static class BrowserOrganizationService
    {
        public static void Fill(Document doc, RevitContext context)
        {
            List<View> allViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && !(v is ViewSheet) && IsUserView(v))
                .ToList();
            List<ViewSheet> allSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsTemplate)
                .ToList();

            // ── Views ──
            BrowserOrganization viewOrg = BrowserOrganization.GetCurrentBrowserOrganizationForViews(doc);
            context.ViewBrowserOrganizationName = viewOrg?.Name ?? string.Empty;
            context.ViewGroupingParameters = CollectGrouping(doc, viewOrg, allViews.Cast<Element>().ToList(),
                BuiltInCategory.OST_Views);

            // ── Sheets ──
            BrowserOrganization sheetOrg = BrowserOrganization.GetCurrentBrowserOrganizationForSheets(doc);
            context.SheetBrowserOrganizationName = sheetOrg?.Name ?? string.Empty;
            context.SheetGroupingParameters = CollectGrouping(doc, sheetOrg, allSheets.Cast<Element>().ToList(),
                BuiltInCategory.OST_Sheets);

            // ── Sheet collections ──
            context.SheetCollections = new FilteredElementCollector(doc)
                .OfClass(typeof(SheetCollection)).Cast<SheetCollection>()
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new NamedElementRef { UniqueId = c.UniqueId, ElementIdValue = c.Id.Value, Name = c.Name })
                .ToList();

            // ── Placeable views ──
            var onSheet = new Dictionary<ElementId, string>();
            foreach (Viewport vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                if (doc.GetElement(vp.SheetId) is ViewSheet s && !onSheet.ContainsKey(vp.ViewId))
                {
                    onSheet[vp.ViewId] = s.SheetNumber;
                }
            }
            foreach (ScheduleSheetInstance ssi in new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
            {
                if (doc.GetElement(ssi.OwnerViewId) is ViewSheet s && !onSheet.ContainsKey(ssi.ScheduleId))
                {
                    onSheet[ssi.ScheduleId] = s.SheetNumber;
                }
            }
            context.PlaceableViews = allViews
                .OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .Select(v => new PlaceableViewRef
                {
                    UniqueId = v.UniqueId,
                    Name = v.Name,
                    ViewTypeName = DescribeType(v),
                    IsSchedule = v is ViewSchedule,
                    OnSheetNumber = onSheet.TryGetValue(v.Id, out string number) ? number : string.Empty
                })
                .ToList();
        }

        /// <summary>
        /// The grouping parameters come from GetFolderItems on a sample
        /// element (the API exposes the folders, not the rules). Only text
        /// parameters the tool can actually write are offered. With no sample
        /// element yet (no sheets in a new project) the text project
        /// parameters bound to the category are offered instead, and the
        /// caller sees that in the organization name.
        /// </summary>
        private static List<GroupingParameterInfo> CollectGrouping(Document doc, BrowserOrganization org,
            List<Element> samples, BuiltInCategory category)
        {
            var names = new List<string>();
            if (org != null && samples.Count > 0)
            {
                foreach (FolderItemInfo item in org.GetFolderItems(samples[0].Id))
                {
#if REVIT2026_OR_GREATER
                    // 2026: the grouping parameter is the last id of the path (FolderItemInfo.ElementId is deprecated).
                    IList<ElementId> path = item.GetGroupingParameterIdPath();
                    ElementId parameterId = path != null && path.Count > 0 ? path[path.Count - 1] : null;
#else
                    ElementId parameterId = item.ElementId;
#endif
                    if (parameterId == null || parameterId.Value < 0)
                    {
                        continue; // built-in grouping (Family and Type, Discipline…) — not a text parameter we should write
                    }
                    if (doc.GetElement(parameterId) is ParameterElement pe)
                    {
                        string name = pe.GetDefinition()?.Name;
                        if (!string.IsNullOrEmpty(name) && !names.Contains(name))
                        {
                            names.Add(name);
                        }
                    }
                }
            }
            if (names.Count == 0)
            {
                names.AddRange(TextProjectParametersBoundTo(doc, category));
            }

            var result = new List<GroupingParameterInfo>();
            foreach (string name in names)
            {
                var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                bool writable = samples.Count == 0;
                foreach (Element e in samples)
                {
                    Parameter p = e.LookupParameter(name);
                    if (p == null || p.StorageType != StorageType.String)
                    {
                        continue;
                    }
                    writable |= !p.IsReadOnly;
                    string v = p.AsString();
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        values.Add(v.Trim());
                    }
                }
                if (writable)
                {
                    result.Add(new GroupingParameterInfo { Name = name, Values = values.ToList() });
                }
            }
            return result;
        }

        private static IEnumerable<string> TextProjectParametersBoundTo(Document doc, BuiltInCategory category)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            Category cat = Category.GetCategory(doc, category);
            if (cat == null)
            {
                return names;
            }
            DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext())
            {
                Definition def = it.Key;
                if (def == null || def.GetDataType() != SpecTypeId.String.Text)
                {
                    continue;
                }
                if (it.Current is InstanceBinding binding && binding.Categories.Contains(cat))
                {
                    names.Add(def.Name);
                }
            }
            return names;
        }

        private static bool IsUserView(View v)
        {
            switch (v.ViewType)
            {
                case ViewType.ProjectBrowser:
                case ViewType.SystemBrowser:
                case ViewType.Internal:
                case ViewType.Undefined:
                case ViewType.DrawingSheet:
                    return false;
                default:
                    return v.CanBePrinted || v is ViewSchedule;
            }
        }

        private static string DescribeType(View v)
        {
            if (v is ViewSchedule schedule)
            {
                return schedule.IsTitleblockRevisionSchedule ? "Revision schedule" : "Schedule";
            }
            switch (v.ViewType)
            {
                case ViewType.FloorPlan: return "Floor plan";
                case ViewType.CeilingPlan: return "Ceiling plan";
                case ViewType.AreaPlan: return "Area plan";
                case ViewType.EngineeringPlan: return "Structural plan";
                case ViewType.Section: return "Section";
                case ViewType.Elevation: return "Elevation";
                case ViewType.Detail: return "Detail";
                case ViewType.ThreeD: return "3D";
                case ViewType.DraftingView: return "Drafting";
                case ViewType.Legend: return "Legend";
                case ViewType.Rendering: return "Rendering";
                case ViewType.Walkthrough: return "Walkthrough";
                default: return v.ViewType.ToString();
            }
        }
    }
}
