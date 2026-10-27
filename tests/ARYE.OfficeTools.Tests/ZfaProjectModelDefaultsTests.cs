using System;
using System.Linq;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Serialization;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    /// <summary>
    /// Old payloads predate the configurable category list, the energy inputs
    /// and the matrix settings. They must load with the office defaults —
    /// SchemaVersion stays 1, ZfaModelMigrator untouched.
    /// </summary>
    public class ZfaProjectModelDefaultsTests
    {
        private static ZfaProjectModel RoundTripWithout(params string[] removedProperties)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(
                ZfaModelSerializer.Serialize(new ZfaProjectModel()));
            var obj = (System.Text.Json.Nodes.JsonObject)node;
            foreach (string property in removedProperties)
            {
                obj.Remove(property);
            }
            return ZfaModelSerializer.Deserialize(obj.ToJsonString(), ZfaProjectModel.CurrentSchemaVersion);
        }

        [Fact]
        public void OldPayload_WithoutCategories_GetsTheOfficeDefaults()
        {
            ZfaProjectModel restored = RoundTripWithout("Categories");

            Assert.Equal(
                CategoryDefinition.OfficeDefaults().Select(c => c.Name),
                restored.Categories.Select(c => c.Name));
            Assert.Equal(
                CategoryDefinition.OfficeDefaults().Select(c => c.Role),
                restored.Categories.Select(c => c.Role));
            Assert.Equal(ZfaProjectModel.CurrentSchemaVersion, restored.SchemaVersion);
        }

        [Fact]
        public void OldPayload_WithoutMatrixSettings_LoadsEmptyOnes()
        {
            ZfaProjectModel restored = RoundTripWithout("Matrix");

            Assert.NotNull(restored.Matrix);
            Assert.Null(restored.Matrix.LastComputedUtc);
            Assert.Null(restored.Matrix.DraftingViewUniqueId);
        }

        [Fact]
        public void OldZoningPayload_GetsEnergyDefaults()
        {
            // A Zoning object serialized before the energy fields existed.
            var node = System.Text.Json.Nodes.JsonNode.Parse(
                ZfaModelSerializer.Serialize(new ZfaProjectModel()));
            var zoning = (System.Text.Json.Nodes.JsonObject)node["Zoning"];
            zoning.Remove("EnergyDeductionEnabled");
            zoning.Remove("EnergyDeductionPercent");

            ZfaProjectModel restored = ZfaModelSerializer.Deserialize(
                node.ToJsonString(), ZfaProjectModel.CurrentSchemaVersion);

            Assert.False(restored.Zoning.EnergyDeductionEnabled);
            Assert.Equal(5.0, restored.Zoning.EnergyDeductionPercent);
        }

        [Fact]
        public void EditedCategoryList_RoundTrips()
        {
            var model = new ZfaProjectModel();
            model.Categories.Add(new CategoryDefinition("Terrace", CategoryRole.Exempt));
            model.Categories.RemoveAll(c => c.Name == "Refuse");
            model.Matrix.LastComputedUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
            model.Matrix.LastComputedBy = "scastro";
            model.Matrix.DraftingViewUniqueId = "view-uid-1";

            string json = ZfaModelSerializer.Serialize(model);
            ZfaProjectModel restored = ZfaModelSerializer.Deserialize(json, ZfaProjectModel.CurrentSchemaVersion);

            Assert.Contains(restored.Categories, c => c.Name == "Terrace" && c.Role == CategoryRole.Exempt);
            Assert.DoesNotContain(restored.Categories, c => c.Name == "Refuse");
            Assert.Equal("scastro", restored.Matrix.LastComputedBy);
            Assert.Equal("view-uid-1", restored.Matrix.DraftingViewUniqueId);
            Assert.Equal(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
                restored.Matrix.LastComputedUtc);
        }

        [Fact]
        public void CategoryRoles_SerializeAsNames_NotNumbers()
        {
            string json = ZfaModelSerializer.Serialize(new ZfaProjectModel());
            Assert.Contains("\"MainGross\"", json);
            Assert.Contains("\"CommercialGross\"", json);
        }
    }
}
