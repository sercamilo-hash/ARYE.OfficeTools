using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Infrastructure;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// Read-only snapshot of the document for the UI. Each collector runs once
    /// per refresh — never inside a loop.
    /// </summary>
    internal sealed class ModelInventoryService
    {
        public RevitContext BuildContext(Document doc)
        {
            var context = new RevitContext
            {
                DocumentTitle = doc.Title,
                DocumentPathName = doc.PathName
            };

            context.Levels.AddRange(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => new NamedElementRef
                    {
                        UniqueId = l.UniqueId,
                        ElementIdValue = l.Id.Value,
                        Name = l.Name,
                        ElevationFt = l.Elevation
                    }));

            context.ScopeBoxes.AddRange(
                new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType()
                    .OrderBy(e => e.Name)
                    .Select(e => new NamedElementRef
                    {
                        UniqueId = e.UniqueId,
                        ElementIdValue = e.Id.Value,
                        Name = e.Name
                    }));

            context.AreaSchemes.AddRange(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(AreaScheme))
                    .Cast<AreaScheme>()
                    .OrderBy(s => s.Name)
                    .Select(s => new NamedElementRef
                    {
                        UniqueId = s.UniqueId,
                        ElementIdValue = s.Id.Value,
                        Name = s.Name
                    }));

            context.TitleBlockTypes.AddRange(
                new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .OrderBy(t => t.FamilyName).ThenBy(t => t.Name)
                    .Select(t => new NamedElementRef
                    {
                        UniqueId = t.UniqueId,
                        ElementIdValue = t.Id.Value,
                        Name = t.FamilyName + " : " + t.Name
                    }));

            FillViewTemplates(doc, context);

            Category lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (lines != null)
            {
                context.LineStyleNames.AddRange(lines.SubCategories.Cast<Category>().Select(c => c.Name).OrderBy(n => n));
            }

            context.AreaTextParameters.AddRange(CollectAreaTextParameters(doc));

            // Sheet Diagrammer: browser-organization parameters, sheet collections, placeable views.
            BrowserOrganizationService.Fill(doc, context);

            return context;
        }

        /// <summary>
        /// Candidates for the Step 2 "Building parameter" stamp: modifiable
        /// text (string) instance parameters available on Areas. Harvested from
        /// the project parameter bindings for the Areas category plus a sample
        /// Area instance; read-only, non-string and built-in parameters are
        /// excluded, and so is "ZFA Category" (stamping it would corrupt the
        /// classification).
        /// </summary>
        private static IEnumerable<string> CollectAreaTextParameters(Document doc)
        {
            var names = new SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);

            // Project parameters bound to the Areas category.
            Category areasCategory = Category.GetCategory(doc, BuiltInCategory.OST_Areas);
            DefinitionBindingMapIterator iterator = doc.ParameterBindings.ForwardIterator();
            while (iterator.MoveNext())
            {
                Definition definition = iterator.Key;
                if (definition == null || definition.GetDataType() != SpecTypeId.String.Text)
                {
                    continue;
                }
                var binding = iterator.Current as InstanceBinding;
                if (binding == null || areasCategory == null
                    || !binding.Categories.Contains(areasCategory))
                {
                    continue;
                }
                names.Add(definition.Name);
            }

            // A sample Area instance catches parameters bindings can't see
            // (e.g. shared parameters added by other tools).
            Area sample = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Areas)
                .WhereElementIsNotElementType()
                .OfType<Area>()
                .FirstOrDefault();
            if (sample != null)
            {
                foreach (Parameter parameter in sample.Parameters)
                {
                    if (parameter.StorageType != StorageType.String
                        || parameter.IsReadOnly
                        || parameter.Id.Value < 0) // built-in (Name, Comments, …) — excluded
                    {
                        continue;
                    }
                    names.Add(parameter.Definition.Name);
                }
            }

            names.Remove(ZfaParameterService.ParameterName);
            return names;
        }

        /// <summary>
        /// A template is offered for a source kind when Revit itself says it can be assigned to a real
        /// view of that kind (<see cref="View.IsValidViewTemplate"/> against a representative view);
        /// the template's own ViewType is the fallback when the model has no view of that kind yet.
        /// Everything the model holds is counted, so the UI can say how many were left out and why.
        /// </summary>
        private static void FillViewTemplates(Document doc, RevitContext context)
        {
            List<View> all = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
            List<View> templates = all.Where(v => v.IsTemplate).OrderBy(v => v.Name).ToList();
            context.TotalViewTemplates = templates.Count;

            View floorRep = all.FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);
            View areaRep = all.FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.AreaPlan);
            View ceilingRep = all.FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.CeilingPlan);

            foreach (View template in templates)
            {
                var reference = new NamedElementRef { UniqueId = template.UniqueId, ElementIdValue = template.Id.Value, Name = template.Name };
                if (Accepts(floorRep, template, ViewType.FloorPlan)) context.FloorPlanTemplates.Add(reference);
                if (Accepts(areaRep, template, ViewType.AreaPlan)) context.AreaPlanTemplates.Add(reference);
                if (Accepts(ceilingRep, template, ViewType.CeilingPlan)) context.CeilingPlanTemplates.Add(reference);
            }

            App.Log?.Info($"View templates: {templates.Count} in the model — {context.FloorPlanTemplates.Count} fit floor plans, "
                + $"{context.AreaPlanTemplates.Count} area plans, {context.CeilingPlanTemplates.Count} ceiling plans. "
                + "By ViewType: " + string.Join(", ", templates.GroupBy(t => t.ViewType).Select(g => g.Key + "=" + g.Count())));
        }

        private static bool Accepts(View representative, View template, ViewType kind)
        {
            if (representative != null)
            {
                try
                {
                    return representative.IsValidViewTemplate(template.Id);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // fall through to the ViewType check below
                }
            }
            return template.ViewType == kind;
        }
    }
}
