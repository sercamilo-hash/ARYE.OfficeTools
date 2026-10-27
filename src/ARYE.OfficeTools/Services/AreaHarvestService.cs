using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Zoning;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// Step 3 input: flattens every Area of each building's area scheme into
    /// plain ClassifiedArea data for the calculator, matching the "ZFA
    /// Category" text against the project's configurable category list
    /// (case-insensitive, trimmed). Problems (unplaced areas, unknown
    /// category strings) are reported, never dropped.
    /// </summary>
    internal sealed class AreaHarvestService
    {
        public HarvestResult Harvest(Document doc, ZfaProjectModel model)
        {
            var result = new HarvestResult();
            var catalog = new CategoryCatalog(model.Categories);

            Dictionary<long, int> levelOrder = BuildLevelOrder(doc);

            // One collector for all areas; grouped per scheme afterwards.
            List<Area> allAreas = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Areas)
                .WhereElementIsNotElementType()
                .OfType<Area>()
                .ToList();

            foreach (BuildingDefinition building in model.Buildings)
            {
                // Every scheme the building's ZFA is made of (gross + deductions
                // may live in separate schemes) — the same rule Step 2 syncs by.
                var schemeIds = new HashSet<long>();
                foreach (string uniqueId in ZfaSchemeResolver.Resolve(model, building))
                {
                    if (doc.GetElement(uniqueId) is AreaScheme scheme)
                    {
                        schemeIds.Add(scheme.Id.Value);
                    }
                    else
                    {
                        result.Problems.Add(
                            $"{building.Name}: one of its ZFA area schemes no longer exists — untick it in Step 2.");
                    }
                }
                if (schemeIds.Count == 0)
                {
                    result.Problems.Add(
                        $"{building.Name}: no area scheme ticked in Step 2 · Sync Ranges — its areas were not harvested.");
                    continue;
                }

                foreach (Area area in allAreas.Where(a => a.AreaScheme != null && schemeIds.Contains(a.AreaScheme.Id.Value)))
                {
                    Level level = area.Level;
                    if (level == null)
                    {
                        result.Problems.Add(
                            $"{building.Name}: area '{area.Name}' has no level — skipped.");
                        continue;
                    }
                    if (area.Area <= 0)
                    {
                        result.Problems.Add(
                            $"{building.Name}: area '{area.Name}' on {level.Name} is not placed or " +
                            "not enclosed (0 sf) — it contributes nothing and needs fixing in the model.");
                        continue;
                    }

                    CategoryDefinition category = ReadCategory(area, catalog, out string rawValue, out bool unknown);
                    if (unknown)
                    {
                        result.Problems.Add(
                            $"{building.Name}: area '{area.Name}' on {level.Name} has ZFA Category " +
                            $"'{rawValue}', which is not in the project's category list — treated as Unclassified.");
                    }

                    levelOrder.TryGetValue(level.Id.Value, out int index);
                    result.Areas.Add(new ClassifiedArea
                    {
                        BuildingName = building.Name,
                        LevelName = level.Name,
                        LevelIndex = index,
                        LevelElevationFt = level.Elevation,
                        LevelUniqueId = level.UniqueId,
                        CategoryName = category?.Name,
                        Role = category?.Role,
                        SquareFeet = area.Area,
                        AreaName = area.Name
                    });

                    if (category == null)
                    {
                        result.Unclassified.Add(new UnclassifiedArea
                        {
                            AreaUniqueId = area.UniqueId,
                            BuildingName = building.Name,
                            LevelName = level.Name,
                            AreaName = area.Name,
                            SquareFeet = area.Area
                        });
                    }
                }

                AddDuplexWarnings(building, result);
            }

            return result;
        }

        /// <summary>Writes category names back to the ZFA Category parameter. One transaction.</summary>
        public ARYE.OfficeTools.Core.Results.OperationResult<string> Classify(
            Document doc, IReadOnlyList<(string AreaUniqueId, string Display, string CategoryName)> assignments)
        {
            var result = new ARYE.OfficeTools.Core.Results.OperationResult<string>();
            using (var tx = new Transaction(doc, "BauTools: Classify areas"))
            {
                tx.Start();
                new ZfaParameterService().EnsureZfaCategoryParameter(doc);

                foreach ((string uniqueId, string display, string categoryName) in assignments)
                {
                    var area = doc.GetElement(uniqueId) as Area;
                    if (area == null)
                    {
                        result.Failed.Add(new ARYE.OfficeTools.Core.Results.OperationFailure(
                            display, "area no longer exists in the model"));
                        continue;
                    }
                    Parameter parameter = area.LookupParameter(ZfaParameterService.ParameterName);
                    if (parameter == null || parameter.IsReadOnly)
                    {
                        result.Failed.Add(new ARYE.OfficeTools.Core.Results.OperationFailure(
                            display, "ZFA Category parameter missing or read-only"));
                        continue;
                    }
                    parameter.Set(categoryName);
                    result.Succeeded.Add(display);
                }

                tx.Commit();
            }
            return result;
        }

        private static CategoryDefinition ReadCategory(Area area, CategoryCatalog catalog,
            out string rawValue, out bool unknown)
        {
            unknown = false;
            rawValue = null;
            Parameter parameter = area.LookupParameter(ZfaParameterService.ParameterName);
            if (parameter == null || parameter.StorageType != StorageType.String)
            {
                return null;
            }
            rawValue = parameter.AsString();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }
            CategoryDefinition match = catalog.Match(rawValue);
            unknown = match == null;
            return match;
        }

        private static Dictionary<long, int> BuildLevelOrder(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select((level, index) => new { level, index })
                .ToDictionary(x => x.level.Id.Value, x => x.index);
        }

        /// <summary>
        /// The duplex "count once" contract check: a duplex upper plan whose
        /// gross nearly equals its lower suggests the double-height space was
        /// drawn as an Area on both plans.
        /// </summary>
        private void AddDuplexWarnings(BuildingDefinition building, HarvestResult result)
        {
            const double suspicionThreshold = 0.02; // within 2% → suspicious

            foreach (FloorGroup group in building.Groups.Where(g => g.Kind == FloorGroupKind.Duplex))
            {
                var pairs = new List<(string LowerName, string UpperName)>();
                if (group.SourceLevel != null && group.SourceUpperLevel != null)
                {
                    pairs.Add((group.SourceLevel.Name, group.SourceUpperLevel.Name));
                }
                pairs.AddRange(group.TargetPairs
                    .Where(p => p.Lower != null && p.Upper != null)
                    .Select(p => (p.Lower.Name, p.Upper.Name)));

                foreach ((string lowerName, string upperName) in pairs)
                {
                    double lower = SumLevel(result, building.Name, lowerName);
                    double upper = SumLevel(result, building.Name, upperName);
                    if (lower <= 0 || upper <= 0)
                    {
                        continue;
                    }
                    if (Math.Abs(upper - lower) / lower < suspicionThreshold)
                    {
                        result.Problems.Add(
                            $"{building.Name}: duplex pair {lowerName}+{upperName} has nearly identical " +
                            $"gross ({lower:0} vs {upper:0} sf) — suspected double-count. The double-height " +
                            "space should be an Area only on the lower plan.");
                    }
                }
            }
        }

        private static double SumLevel(HarvestResult result, string buildingName, string levelName)
        {
            return result.Areas
                .Where(a => a.BuildingName == buildingName
                            && a.LevelName == levelName
                            && a.Role != CategoryRole.Exempt)
                .Sum(a => a.SquareFeet);
        }
    }

    internal sealed class HarvestResult
    {
        public List<ClassifiedArea> Areas { get; } = new List<ClassifiedArea>();

        public List<UnclassifiedArea> Unclassified { get; } = new List<UnclassifiedArea>();

        /// <summary>Everything skipped or suspicious — shown to the user, never dropped.</summary>
        public List<string> Problems { get; } = new List<string>();
    }

    internal sealed class UnclassifiedArea
    {
        public string AreaUniqueId { get; set; }

        public string BuildingName { get; set; }

        public string LevelName { get; set; }

        public string AreaName { get; set; }

        public double SquareFeet { get; set; }
    }
}
