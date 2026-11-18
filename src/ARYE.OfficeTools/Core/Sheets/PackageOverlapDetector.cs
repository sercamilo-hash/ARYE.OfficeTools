using System;
using System.Collections.Generic;
using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.Core.Sheets
{
    /// <summary>One package that would duplicate another package's output.</summary>
    public sealed class PackageOverlap
    {
        public Guid PackageId { get; set; }

        public Guid OtherPackageId { get; set; }

        public string OtherPackageName { get; set; }

        /// <summary>Ready-to-show explanation of the conflict and how to break it.</summary>
        public string Description { get; set; }
    }

    /// <summary>
    /// Detects packages that would generate identical information for the same
    /// building run: same SourceKind, same scheme (for AreaPlans) and the same
    /// view template. Same source with a different template is allowed — that
    /// is a legitimate second presentation of the same plans.
    /// </summary>
    public static class PackageOverlapDetector
    {
        /// <summary>
        /// One entry per package that overlaps at least one other (both sides
        /// of a pair are reported). AreaPlans packages without a scheme are
        /// not comparable yet — they are blocked elsewhere for the missing
        /// scheme, not flagged as duplicates.
        /// </summary>
        public static IReadOnlyList<PackageOverlap> Detect(IReadOnlyList<SheetPackage> packages)
        {
            if (packages == null) throw new ArgumentNullException(nameof(packages));

            var overlaps = new List<PackageOverlap>();
            for (int i = 0; i < packages.Count; i++)
            {
                for (int j = 0; j < packages.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }
                    if (Overlaps(packages[i], packages[j]))
                    {
                        overlaps.Add(new PackageOverlap
                        {
                            PackageId = packages[i].Id,
                            OtherPackageId = packages[j].Id,
                            OtherPackageName = packages[j].Name,
                            Description = Describe(packages[i], packages[j])
                        });
                        break; // first conflict per package is enough to block it
                    }
                }
            }
            return overlaps;
        }

        public static bool Overlaps(SheetPackage a, SheetPackage b)
        {
            if (a == null || b == null || a.Id == b.Id)
            {
                return false;
            }
            if (a.SourceKind != b.SourceKind)
            {
                return false;
            }
            if (a.SourceKind == SheetSourceKind.AreaPlans)
            {
                string schemeA = Normalize(a.AreaSchemeUniqueId);
                string schemeB = Normalize(b.AreaSchemeUniqueId);
                if (schemeA.Length == 0 || schemeB.Length == 0 || schemeA != schemeB)
                {
                    return false; // unset schemes are handled as a missing-scheme block, not a duplicate
                }
            }
            return Normalize(a.ViewTemplateUniqueId) == Normalize(b.ViewTemplateUniqueId);
        }

        private static string Describe(SheetPackage package, SheetPackage other)
        {
            return package.SourceKind == SheetSourceKind.AreaPlans
                ? $"Would duplicate '{other.Name}': same source, scheme and template — change the template, scheme or source."
                : $"Would duplicate '{other.Name}': same source and template — change the template or source.";
        }

        private static string Normalize(string uniqueId) => (uniqueId ?? string.Empty).Trim();
    }
}
