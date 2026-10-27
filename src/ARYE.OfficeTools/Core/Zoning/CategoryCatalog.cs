using System;
using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.Core.Zoning
{
    /// <summary>How a set of selected category names reads as a quick-filter state.</summary>
    public enum ZfaFilterState
    {
        Gross = 0,
        Deductions = 1,
        All = 2,
        Custom = 3
    }

    /// <summary>
    /// The single authority over the project's configurable category list:
    /// which names exist, what role each plays, how raw "ZFA Category" text
    /// matches a definition, and how a selected-name set reads as a quick
    /// filter. Replaces the old enum-based ZfaCategoryFamilies — the Step 2
    /// filter, the Step 3 matrix and the harvest all derive from one instance.
    /// </summary>
    public sealed class CategoryCatalog
    {
        private readonly List<CategoryDefinition> _all;
        private readonly Dictionary<string, CategoryDefinition> _byName;

        public CategoryCatalog(IEnumerable<CategoryDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));

            _all = new List<CategoryDefinition>();
            _byName = new Dictionary<string, CategoryDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (CategoryDefinition definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Name))
                {
                    continue; // a nameless entry can never match anything
                }
                string key = definition.Name.Trim();
                if (_byName.ContainsKey(key))
                {
                    continue; // first definition of a duplicated name wins
                }
                _all.Add(definition);
                _byName[key] = definition;
            }

            GrossCategories = _all
                .Where(d => d.Role == CategoryRole.MainGross || d.Role == CategoryRole.CommercialGross)
                .ToList();
            DeductionCategories = _all
                .Where(d => d.Role == CategoryRole.Deduction)
                .ToList();
            FilterableCategories = GrossCategories.Concat(DeductionCategories).ToList();
        }

        /// <summary>A catalog seeded with the office defaults.</summary>
        public static CategoryCatalog Office() => new CategoryCatalog(CategoryDefinition.OfficeDefaults());

        /// <summary>Every usable definition, in the user's list order.</summary>
        public IReadOnlyList<CategoryDefinition> All => _all;

        /// <summary>Categories that count toward gross: roles MainGross and CommercialGross.</summary>
        public IReadOnlyList<CategoryDefinition> GrossCategories { get; }

        /// <summary>Categories deducted from gross: role Deduction, in list order — the matrix column order.</summary>
        public IReadOnlyList<CategoryDefinition> DeductionCategories { get; }

        /// <summary>
        /// Every category the Step 2 filter can select — gross first, then
        /// deductions. Exempt-role categories are never filterable (they count
        /// in no total and never propagate), matching the old Exempt behavior.
        /// </summary>
        public IReadOnlyList<CategoryDefinition> FilterableCategories { get; }

        /// <summary>
        /// Matches a raw "ZFA Category" parameter value the way the harvest
        /// does: trimmed, case-insensitive. Null, blank or unknown text
        /// returns null — Unclassified, never guessed.
        /// </summary>
        public CategoryDefinition Match(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }
            return _byName.TryGetValue(rawValue.Trim(), out CategoryDefinition definition)
                ? definition
                : null;
        }

        /// <summary>
        /// Classifies a selected-name set as one of the quick-filter states.
        /// Exactly the gross family → Gross; exactly the deduction family →
        /// Deductions; every filterable category → All; anything else → Custom.
        /// </summary>
        public ZfaFilterState ClassifyFilter(IEnumerable<string> selectedNames)
        {
            if (selectedNames == null) throw new ArgumentNullException(nameof(selectedNames));
            var set = new HashSet<string>(
                selectedNames.Where(n => n != null).Select(n => n.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (SetEquals(set, FilterableCategories))
            {
                return ZfaFilterState.All;
            }
            if (SetEquals(set, GrossCategories))
            {
                return ZfaFilterState.Gross;
            }
            if (SetEquals(set, DeductionCategories))
            {
                return ZfaFilterState.Deductions;
            }
            return ZfaFilterState.Custom;
        }

        private static bool SetEquals(HashSet<string> set, IReadOnlyList<CategoryDefinition> family)
        {
            return set.Count == family.Count && family.All(d => set.Contains(d.Name.Trim()));
        }
    }
}
