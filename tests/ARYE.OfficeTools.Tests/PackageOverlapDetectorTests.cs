using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Sheets;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class PackageOverlapDetectorTests
    {
        private static SheetPackage AreaPackage(string name, string scheme, string template) =>
            new SheetPackage
            {
                Name = name,
                SourceKind = SheetSourceKind.AreaPlans,
                AreaSchemeUniqueId = scheme,
                ViewTemplateUniqueId = template
            };

        private static SheetPackage FloorPackage(string name, string template) =>
            new SheetPackage
            {
                Name = name,
                SourceKind = SheetSourceKind.FloorPlans,
                ViewTemplateUniqueId = template
            };

        [Fact]
        public void SameSourceSchemeAndTemplate_BothPackagesConflict()
        {
            var gross = AreaPackage("Gross", "scheme-1", "tpl-1");
            var copy = AreaPackage("Gross Copy", "scheme-1", "tpl-1");

            IReadOnlyList<PackageOverlap> overlaps =
                PackageOverlapDetector.Detect(new List<SheetPackage> { gross, copy });

            Assert.Equal(2, overlaps.Count);
            PackageOverlap first = overlaps.Single(o => o.PackageId == gross.Id);
            Assert.Equal(copy.Id, first.OtherPackageId);
            Assert.Contains("Would duplicate 'Gross Copy'", first.Description);
            Assert.Contains("scheme", first.Description);
        }

        [Fact]
        public void SameSourceAndScheme_DifferentTemplate_IsAllowed()
        {
            var gross = AreaPackage("Gross", "scheme-1", "tpl-gross");
            var lifeSafety = AreaPackage("Life Safety", "scheme-1", "tpl-ls");

            Assert.Empty(PackageOverlapDetector.Detect(
                new List<SheetPackage> { gross, lifeSafety }));
        }

        [Fact]
        public void DifferentScheme_SameTemplate_IsAllowed()
        {
            var gross = AreaPackage("Gross", "scheme-1", "tpl-1");
            var deductions = AreaPackage("Deductions", "scheme-2", "tpl-1");

            Assert.Empty(PackageOverlapDetector.Detect(
                new List<SheetPackage> { gross, deductions }));
        }

        [Fact]
        public void DifferentSourceKind_NeverConflicts()
        {
            var floor = FloorPackage("Architectural Floor Plans", null);
            var area = AreaPackage("Gross", "scheme-1", null);

            Assert.Empty(PackageOverlapDetector.Detect(
                new List<SheetPackage> { floor, area }));
        }

        [Fact]
        public void TwoFloorPlanPackages_SameTemplate_Conflict()
        {
            var a = FloorPackage("Plans A", null);
            var b = FloorPackage("Plans B", null);

            IReadOnlyList<PackageOverlap> overlaps =
                PackageOverlapDetector.Detect(new List<SheetPackage> { a, b });

            Assert.Equal(2, overlaps.Count);
            Assert.DoesNotContain("scheme", overlaps[0].Description);
        }

        [Fact]
        public void AreaPackagesWithoutScheme_AreNotComparable_TheSeedsDoNotSelfConflict()
        {
            // The default seed has three AreaPlans packages with scheme unset —
            // they are blocked for the missing scheme, not flagged as duplicates.
            Assert.Empty(PackageOverlapDetector.Detect(SheetPackage.DefaultSeed()));
        }

        [Fact]
        public void SamePackage_NeverConflictsWithItself()
        {
            var gross = AreaPackage("Gross", "scheme-1", "tpl-1");
            Assert.Empty(PackageOverlapDetector.Detect(new List<SheetPackage> { gross }));
        }
    }
}
