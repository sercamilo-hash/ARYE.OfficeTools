using System;
using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Model
{
    /// <summary>
    /// Which area schemes hold a building's ZFA areas. One rule, used by Step 2
    /// (what to sync) and Step 3 (what to harvest), so the two can never read
    /// different schemes.
    /// </summary>
    public static class ZfaSchemeResolver
    {
        /// <summary>
        /// The building's own list when it has one. Otherwise, for saves made
        /// before the list existed: the old project-wide Zoning mapping, or else
        /// the building's gross scheme. Distinct, blanks dropped, order kept.
        /// </summary>
        public static List<string> Resolve(ZfaProjectModel model, BuildingDefinition building)
        {
            if (building == null) throw new ArgumentNullException(nameof(building));

            IEnumerable<string> source = building.ZfaSchemeUniqueIds != null && building.ZfaSchemeUniqueIds.Count > 0
                ? building.ZfaSchemeUniqueIds
                : new[]
                {
                    !string.IsNullOrEmpty(model?.Propagation?.SchemeMappingZoningUniqueId)
                        ? model.Propagation.SchemeMappingZoningUniqueId
                        : building.AreaSchemeUniqueId
                };

            return source
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Order-free key of a scheme set — tells whether a sync record was made with the same schemes.</summary>
        public static string Key(IEnumerable<string> schemeUniqueIds) =>
            string.Join("|", (schemeUniqueIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal));
    }
}
