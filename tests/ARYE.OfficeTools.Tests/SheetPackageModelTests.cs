using System;
using System.Linq;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Serialization;
using ARYE.OfficeTools.Core.Sheets;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    /// <summary>
    /// Persistence of the Step 4 sheet packages: round-trip, old-payload seed
    /// defaults (SchemaVersion stays 1, migrator untouched), run records, and
    /// the tag identity strings that ride inside Extensible Storage.
    /// </summary>
    public class SheetPackageModelTests
    {
        [Fact]
        public void FreshModel_SeedsTheFourOfficePackages()
        {
            var model = new ZfaProjectModel();

            Assert.Equal(4, model.SheetPackages.Count);
            Assert.Equal(
                new[] { "Architectural Floor Plans", "Gross", "Deductions", "Life Safety" },
                model.SheetPackages.Select(p => p.Name));
            Assert.Equal(
                new[] { "A-", "Z-", "ZD-", "LS-" },
                model.SheetPackages.Select(p => p.NumberPrefix));
            Assert.Equal(SheetSourceKind.FloorPlans, model.SheetPackages[0].SourceKind);
            Assert.All(model.SheetPackages.Skip(1),
                p => Assert.Equal(SheetSourceKind.AreaPlans, p.SourceKind));
            Assert.All(model.SheetPackages, p => Assert.Equal(100, p.StartNumber));
            Assert.All(model.SheetPackages, p => Assert.Equal(1, p.NumberIncrement));
            // Scheme / titleblock / template deliberately unset — the user picks.
            Assert.All(model.SheetPackages, p => Assert.Null(p.TitleBlockTypeUniqueId));
            Assert.All(model.SheetPackages, p => Assert.Null(p.AreaSchemeUniqueId));
            Assert.All(model.SheetPackages, p => Assert.Null(p.ViewTemplateUniqueId));
        }

        [Fact]
        public void SheetPackages_RoundTripThroughTheSerializer()
        {
            var model = new ZfaProjectModel();
            SheetPackage gross = model.SheetPackages.First(p => p.Name == "Gross");
            gross.AreaSchemeUniqueId = "scheme-uid-1";
            gross.TitleBlockTypeUniqueId = "tb-uid-1";
            gross.ViewTemplateUniqueId = "tpl-uid-1";
            gross.Columns = 4;
            gross.Rows = 3;
            gross.StartNumber = 201;
            gross.NumberIncrement = 2;
            gross.TitleStripWidthIn = 2.5;
            gross.ScaleDenominatorOverride = 96;
            model.RecordSheetRun(new SheetPackageRunRecord
            {
                PackageId = gross.Id,
                BuildingId = model.SheetPackages[0].Id, // any guid
                LastRunUtc = new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc),
                User = "scastro",
                SheetCount = 3,
                ViewCount = 14
            });

            string json = ZfaModelSerializer.Serialize(model);
            ZfaProjectModel restored =
                ZfaModelSerializer.Deserialize(json, ZfaProjectModel.CurrentSchemaVersion);

            SheetPackage restoredGross = restored.SheetPackages.Single(p => p.Id == gross.Id);
            Assert.Equal("scheme-uid-1", restoredGross.AreaSchemeUniqueId);
            Assert.Equal("tb-uid-1", restoredGross.TitleBlockTypeUniqueId);
            Assert.Equal("tpl-uid-1", restoredGross.ViewTemplateUniqueId);
            Assert.Equal(4, restoredGross.Columns);
            Assert.Equal(3, restoredGross.Rows);
            Assert.Equal(201, restoredGross.StartNumber);
            Assert.Equal(2, restoredGross.NumberIncrement);
            Assert.Equal(2.5, restoredGross.TitleStripWidthIn);
            Assert.Equal(96, restoredGross.ScaleDenominatorOverride);

            SheetPackageRunRecord record = Assert.Single(restored.SheetRunRecords);
            Assert.Equal(gross.Id, record.PackageId);
            Assert.Equal("scastro", record.User);
            Assert.Equal(3, record.SheetCount);
            Assert.Equal(14, record.ViewCount);
            Assert.Equal(new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc), record.LastRunUtc);
        }

        [Fact]
        public void OldPayload_WithoutSheetPackages_GetsTheSeed_SchemaVersionStays1()
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(
                ZfaModelSerializer.Serialize(new ZfaProjectModel()));
            var obj = (System.Text.Json.Nodes.JsonObject)node;
            obj.Remove("SheetPackages");
            obj.Remove("SheetRunRecords");

            ZfaProjectModel restored = ZfaModelSerializer.Deserialize(
                obj.ToJsonString(), ZfaProjectModel.CurrentSchemaVersion);

            Assert.Equal(4, restored.SheetPackages.Count);
            Assert.Empty(restored.SheetRunRecords);
            Assert.Equal(ZfaProjectModel.CurrentSchemaVersion, restored.SchemaVersion);
        }

        [Fact]
        public void SourceKind_SerializesAsName_NotNumber()
        {
            string json = ZfaModelSerializer.Serialize(new ZfaProjectModel());
            Assert.Contains("\"FloorPlans\"", json);
            Assert.Contains("\"AreaPlans\"", json);
        }

        [Fact]
        public void RecordSheetRun_ReplacesThePackageBuildingKey()
        {
            var model = new ZfaProjectModel();
            Guid packageId = Guid.NewGuid();
            Guid buildingId = Guid.NewGuid();
            model.RecordSheetRun(new SheetPackageRunRecord
            {
                PackageId = packageId, BuildingId = buildingId, SheetCount = 1
            });
            model.RecordSheetRun(new SheetPackageRunRecord
            {
                PackageId = packageId, BuildingId = buildingId, SheetCount = 5
            });
            model.RecordSheetRun(new SheetPackageRunRecord
            {
                PackageId = packageId, BuildingId = Guid.NewGuid(), SheetCount = 9
            });

            Assert.Equal(2, model.SheetRunRecords.Count);
            Assert.Equal(5, model.FindSheetRun(packageId, buildingId).SheetCount);
        }

        // ── Tag identity strings ─────────────────────────────────────────────

        [Fact]
        public void ViewIdentity_RoundTrips()
        {
            string composed = SheetTagIdentity.ComposeView(
                SheetSourceKind.AreaPlans, "scheme-uid", "level-uid");

            Assert.True(SheetTagIdentity.TryParseView(composed,
                out SheetSourceKind kind, out string scheme, out string level));
            Assert.Equal(SheetSourceKind.AreaPlans, kind);
            Assert.Equal("scheme-uid", scheme);
            Assert.Equal("level-uid", level);
        }

        [Fact]
        public void ViewIdentity_FloorPlans_EmptySchemeRoundTrips()
        {
            string composed = SheetTagIdentity.ComposeView(
                SheetSourceKind.FloorPlans, null, "level-uid");

            Assert.True(SheetTagIdentity.TryParseView(composed,
                out SheetSourceKind kind, out string scheme, out string level));
            Assert.Equal(SheetSourceKind.FloorPlans, kind);
            Assert.Equal("", scheme);
            Assert.Equal("level-uid", level);
        }

        [Fact]
        public void ViewIdentity_CeilingPlans_EmptySchemeRoundTrips()
        {
            string composed = SheetTagIdentity.ComposeView(
                SheetSourceKind.CeilingPlans, null, "level-uid");

            Assert.True(SheetTagIdentity.TryParseView(composed,
                out SheetSourceKind kind, out string scheme, out string level));
            Assert.Equal(SheetSourceKind.CeilingPlans, kind);
            Assert.Equal("", scheme);
            Assert.Equal("level-uid", level);
        }

        [Fact]
        public void SheetIdentity_RoundTrips()
        {
            Assert.True(SheetTagIdentity.TryParseSheet(
                SheetTagIdentity.ComposeSheet(7), out int index));
            Assert.Equal(7, index);
        }

        [Fact]
        public void ForeignTagPayloads_DoNotParse()
        {
            // Step 2/3 tags carry source element UniqueIds here — never mistaken for ours.
            Assert.False(SheetTagIdentity.TryParseView("some-revit-unique-id",
                out _, out _, out _));
            Assert.False(SheetTagIdentity.TryParseSheet("zfa-matrix", out _));
            Assert.False(SheetTagIdentity.TryParseView(null, out _, out _, out _));
            Assert.False(SheetTagIdentity.TryParseSheet("zfa-pkg-sheet|-1", out _));
        }

        // ── Sheet naming ─────────────────────────────────────────────────────

        [Fact]
        public void SheetName_RangeIsUppercase_FirstToLast()
        {
            Assert.Equal("GROSS — CELLAR TO L05",
                SheetNaming.ComposeSheetName("Gross", "Cellar", "L05"));
        }

        [Fact]
        public void SheetName_SingleLevel_DropsTheRange()
        {
            Assert.Equal("GROSS — CELLAR",
                SheetNaming.ComposeSheetName("Gross", "Cellar", "Cellar"));
        }
    }
}
