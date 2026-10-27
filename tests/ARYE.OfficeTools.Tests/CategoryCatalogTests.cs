using System.Linq;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Zoning;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class CategoryCatalogTests
    {
        [Fact]
        public void OfficeDefaults_SeedTheApprovedList()
        {
            CategoryCatalog catalog = CategoryCatalog.Office();

            Assert.Equal(new[] { "Gross", "Commercial Gross" },
                catalog.GrossCategories.Select(c => c.Name));
            Assert.Equal(
                new[] { "Chase Walls", "Mechanical", "Loading Dock", "Bicycle Parking", "Amenities", "Corridor", "Refuse" },
                catalog.DeductionCategories.Select(c => c.Name));
            Assert.Contains(catalog.All, c => c.Name == "Exempt" && c.Role == CategoryRole.Exempt);
        }

        [Fact]
        public void FilterableCategories_IsGrossThenDeductions_NeverExempt()
        {
            CategoryCatalog catalog = CategoryCatalog.Office();

            Assert.Equal(9, catalog.FilterableCategories.Count);
            Assert.Equal(
                catalog.GrossCategories.Concat(catalog.DeductionCategories),
                catalog.FilterableCategories);
            Assert.DoesNotContain(catalog.FilterableCategories, c => c.Role == CategoryRole.Exempt);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Storage Room")] // unknown text — never guessed
        public void Match_BlankOrUnknown_IsNull(string raw)
        {
            Assert.Null(CategoryCatalog.Office().Match(raw));
        }

        [Theory]
        [InlineData("Gross", "Gross")]
        [InlineData("gross", "Gross")]                 // case-insensitive
        [InlineData(" Chase Walls ", "Chase Walls")]   // trimmed
        [InlineData("COMMERCIAL GROSS", "Commercial Gross")]
        public void Match_MirrorsTheHarvestRules(string raw, string expectedName)
        {
            CategoryDefinition match = CategoryCatalog.Office().Match(raw);
            Assert.NotNull(match);
            Assert.Equal(expectedName, match.Name);
        }

        [Fact]
        public void Match_ReturnsTheListSpelling_NotTheRawText()
        {
            CategoryDefinition match = CategoryCatalog.Office().Match("  mechanical ");
            Assert.Equal("Mechanical", match.Name);
            Assert.Equal(CategoryRole.Deduction, match.Role);
        }

        [Fact]
        public void ClassifyFilter_ExactGrossSet_IsGross()
        {
            CategoryCatalog catalog = CategoryCatalog.Office();
            Assert.Equal(ZfaFilterState.Gross,
                catalog.ClassifyFilter(catalog.GrossCategories.Select(c => c.Name)));
        }

        [Fact]
        public void ClassifyFilter_ExactDeductionSet_IsDeductions()
        {
            CategoryCatalog catalog = CategoryCatalog.Office();
            Assert.Equal(ZfaFilterState.Deductions,
                catalog.ClassifyFilter(catalog.DeductionCategories.Select(c => c.Name)));
        }

        [Fact]
        public void ClassifyFilter_EveryFilterable_IsAll()
        {
            CategoryCatalog catalog = CategoryCatalog.Office();
            Assert.Equal(ZfaFilterState.All,
                catalog.ClassifyFilter(catalog.FilterableCategories.Select(c => c.Name)));
        }

        [Fact]
        public void ClassifyFilter_MixedOrPartialOrEmpty_IsCustom()
        {
            CategoryCatalog catalog = CategoryCatalog.Office();
            Assert.Equal(ZfaFilterState.Custom,
                catalog.ClassifyFilter(new[] { "Gross", "Mechanical" }));
            Assert.Equal(ZfaFilterState.Custom,
                catalog.ClassifyFilter(new[] { "Gross" }));
            Assert.Equal(ZfaFilterState.Custom, catalog.ClassifyFilter(new string[0]));
        }

        [Fact]
        public void ClassifyFilter_IsCaseInsensitive()
        {
            CategoryCatalog catalog = CategoryCatalog.Office();
            Assert.Equal(ZfaFilterState.Gross,
                catalog.ClassifyFilter(new[] { "GROSS", "commercial gross" }));
        }

        [Fact]
        public void Constructor_SkipsBlankNames_AndKeepsFirstOfDuplicates()
        {
            var catalog = new CategoryCatalog(new[]
            {
                new CategoryDefinition("Gross", CategoryRole.MainGross),
                new CategoryDefinition("  ", CategoryRole.Deduction),
                new CategoryDefinition(null, CategoryRole.Deduction),
                new CategoryDefinition("gross", CategoryRole.Deduction) // duplicate name, other role
            });

            CategoryDefinition single = Assert.Single(catalog.All);
            Assert.Equal(CategoryRole.MainGross, single.Role);
        }

        [Fact]
        public void CustomConsultantList_WorksLikeTheDefaults()
        {
            // The point of the configurable list: a consultant's own names.
            var catalog = new CategoryCatalog(new[]
            {
                new CategoryDefinition("GFA", CategoryRole.MainGross),
                new CategoryDefinition("Retail GFA", CategoryRole.CommercialGross),
                new CategoryDefinition("MEP", CategoryRole.Deduction),
                new CategoryDefinition("Void", CategoryRole.Exempt)
            });

            Assert.Equal("MEP", Assert.Single(catalog.DeductionCategories).Name);
            Assert.Equal(CategoryRole.MainGross, catalog.Match("gfa").Role);
            Assert.Null(catalog.Match("Mechanical")); // not on this list
            Assert.Equal(ZfaFilterState.All,
                catalog.ClassifyFilter(new[] { "GFA", "Retail GFA", "MEP" }));
        }
    }
}
