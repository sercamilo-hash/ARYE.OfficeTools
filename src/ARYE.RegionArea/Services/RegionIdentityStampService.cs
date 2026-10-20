#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using ARYE.RegionArea.Models;

namespace ARYE.RegionArea.Services
{
    /// <summary>One discovered value for Building or Side, with how many regions already carry it.</summary>
    public class IdentityValueOption
    {
        public string Value;
        public int Count;
        public override string ToString() { return Value; }
    }

    /// <summary>
    /// Building and Side are plain instance text parameters the ARYE template already carries in
    /// Identity Data (confirmed 2026-09-07 against a real Filled Region) — there is no selector for
    /// WHICH parameter, only for WHICH VALUE to stamp, and that value applies to the whole run, not
    /// per row. Valid values are discovered from what already exists in the model, never a
    /// hardcoded list, matching how the area-target selector never assumes a category.
    /// </summary>
    public class RegionIdentityStampService
    {
        public const string BUILDING_PARAM = "Building";
        public const string SIDE_PARAM = "Side";

        private static readonly Regex SideWord =
            new Regex(@"\b(NORTH|SOUTH|EAST|WEST)\b", RegexOptions.IgnoreCase);

        private readonly Document _doc;

        public RegionIdentityStampService(Document doc)
        {
            _doc = doc;
        }

        /// <summary>Current value of a named text instance parameter, or null when absent / not text.</summary>
        public static string ReadString(Element e, string paramName)
        {
            try
            {
                Parameter p = e.LookupParameter(paramName);
                if (p == null || p.StorageType != StorageType.String) return null;
                return p.AsString();
            }
            catch { return null; }
        }

        private IEnumerable<FilledRegion> AllRegions()
        {
            try
            {
                return new FilteredElementCollector(_doc)
                    .OfClass(typeof(FilledRegion))
                    .WhereElementIsNotElementType()
                    .Cast<FilledRegion>();
            }
            catch { return Enumerable.Empty<FilledRegion>(); }
        }

        /// <summary>
        /// Distinct non-blank values already used for this parameter across every Filled Region in
        /// the document — not just the current scope — so the list reflects the whole project even
        /// when today's run only targets a handful of new regions. Most common first.
        /// </summary>
        public List<IdentityValueOption> DiscoverValues(string paramName)
        {
            try
            {
                return AllRegions()
                    .Select(fr => ReadString(fr, paramName))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim())
                    .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new IdentityValueOption { Value = g.Key, Count = g.Count() })
                    .OrderByDescending(o => o.Count)
                    .ThenBy(o => o.Value, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch { return new List<IdentityValueOption>(); }
        }

        /// <summary>
        /// A single cardinal direction mentioned in the owner-view names of the regions in scope
        /// (e.g. "EAST ELEVATION 1" -> "EAST"). Null when none is found, or more than one distinct
        /// direction shows up across the views in scope — ambiguous, so no suggestion is better
        /// than a wrong one.
        /// </summary>
        public static string SuggestSideFromViewNames(IEnumerable<string> viewNames)
        {
            HashSet<string> found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in viewNames ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(name)) continue;
                foreach (Match m in SideWord.Matches(name))
                    found.Add(m.Value.ToUpperInvariant());
            }
            return found.Count == 1 ? found.First() : null;
        }

        /// <summary>
        /// Most common value already set for this parameter among OTHER Filled Regions that share
        /// an owner view with the ones in scope — i.e. neighbours in the same energy-wall diagram
        /// that were already tagged by hand. Null when no sibling carries a value.
        /// </summary>
        public string SuggestFromSiblingViews(string paramName, IList<FilledRegion> inScope)
        {
            if (inScope == null || inScope.Count == 0) return null;

            HashSet<ElementId> scopeIds = new HashSet<ElementId>(inScope.Select(r => r.Id));
            HashSet<ElementId> viewIds = new HashSet<ElementId>(inScope.Select(r => r.OwnerViewId));

            try
            {
                var best = AllRegions()
                    .Where(fr => !scopeIds.Contains(fr.Id) && viewIds.Contains(fr.OwnerViewId))
                    .Select(fr => ReadString(fr, paramName))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim())
                    .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();

                return best != null ? best.Key : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Fills current value, proposed display and status for one identity field on one region.
        /// A null/blank target value means "do not change": the field is described but never
        /// written, matching the selector's "— (do not change) —" sentinel.
        /// </summary>
        public void Evaluate(FilledRegion region, string paramName, string targetValue,
            out string current, out string display, out IdentityFieldStatus status)
        {
            current = "—";
            display = "—";
            status = IdentityFieldStatus.NotConfigured;

            if (string.IsNullOrWhiteSpace(targetValue)) return;
            targetValue = targetValue.Trim();

            Parameter p;
            try { p = region.LookupParameter(paramName); } catch { p = null; }

            if (p == null || p.StorageType != StorageType.String)
            {
                status = IdentityFieldStatus.MissingParameter;
                display = "(no " + paramName + " parameter)";
                return;
            }

            string cur = p.AsString();
            bool empty = string.IsNullOrWhiteSpace(cur);
            current = empty ? "(empty)" : cur.Trim();

            bool equal = !empty && string.Equals(current, targetValue, StringComparison.Ordinal);

            if (equal)
            {
                status = IdentityFieldStatus.NoChange;
                display = targetValue;
            }
            else if (empty)
            {
                status = IdentityFieldStatus.New;
                display = "— → " + targetValue;
            }
            else
            {
                status = IdentityFieldStatus.Overwrite;
                display = current + " → " + targetValue + " ⚠";
            }
        }

        /// <summary>Writes one identity field on one region. The caller owns the transaction.</summary>
        public bool Write(FilledRegion region, string paramName, string targetValue, out string error)
        {
            error = null;
            try
            {
                Parameter p = region.LookupParameter(paramName);
                if (p == null || p.StorageType != StorageType.String)
                {
                    error = paramName + " parameter not found.";
                    return false;
                }
                if (p.IsReadOnly)
                {
                    error = paramName + " is read-only on this element.";
                    return false;
                }
                return p.Set(targetValue);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
