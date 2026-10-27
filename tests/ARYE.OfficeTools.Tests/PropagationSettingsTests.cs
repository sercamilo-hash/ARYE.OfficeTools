using System;
using System.Collections.Generic;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Serialization;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class PropagationSettingsTests
    {
        private static readonly Guid BuildingId = Guid.NewGuid();
        private static readonly Guid GroupId = Guid.NewGuid();

        private static PropagationRunRecord SampleRecord(
            PropagationPurpose purpose = PropagationPurpose.Zoning, int createdCount = 24)
        {
            return new PropagationRunRecord
            {
                BuildingId = BuildingId,
                GroupId = GroupId,
                Purpose = purpose,
                LastRunUtc = new DateTime(2026, 9, 1, 14, 30, 0, DateTimeKind.Utc),
                User = "scastro",
                FilterDescription = "Gross",
                CreatedCount = createdCount,
                RefreshExisting = true,
                SelectedCategories = new List<string> { "Gross", "Commercial Gross" }
            };
        }

        [Fact]
        public void RoundTrip_PreservesSettingsAndRunRecords()
        {
            var model = new ZfaProjectModel
            {
                Propagation = new PropagationSettings
                {
                    SchemeMappingZoningUniqueId = "scheme-zoning-uid",
                    SchemeMappingEgressUniqueId = "scheme-egress-uid",
                    BuildingParameterName = "Building",
                    RunRecords = new List<PropagationRunRecord> { SampleRecord() }
                }
            };

            string json = ZfaModelSerializer.Serialize(model);
            ZfaProjectModel restored = ZfaModelSerializer.Deserialize(json, ZfaProjectModel.CurrentSchemaVersion);

            Assert.Equal("scheme-zoning-uid", restored.Propagation.SchemeMappingZoningUniqueId);
            Assert.Equal("scheme-egress-uid", restored.Propagation.SchemeMappingEgressUniqueId);
            Assert.Equal("Building", restored.Propagation.BuildingParameterName);

            PropagationRunRecord record = Assert.Single(restored.Propagation.RunRecords);
            Assert.Equal(BuildingId, record.BuildingId);
            Assert.Equal(GroupId, record.GroupId);
            Assert.Equal(PropagationPurpose.Zoning, record.Purpose);
            Assert.Equal(new DateTime(2026, 9, 1, 14, 30, 0, DateTimeKind.Utc), record.LastRunUtc);
            Assert.Equal("scastro", record.User);
            Assert.Equal("Gross", record.FilterDescription);
            Assert.Equal(24, record.CreatedCount);
            Assert.True(record.RefreshExisting);
            Assert.Equal(2, record.SelectedCategories.Count);
            Assert.Contains("Commercial Gross", record.SelectedCategories);
        }

        [Fact]
        public void OldRecord_WithEnumCategoryNames_StillReadsAsStrings()
        {
            // Records written before the configurable list stored ZfaCategory
            // values, serialized as names ("Residential", …) — they must keep
            // loading now that the property is a plain string list.
            var node = System.Text.Json.Nodes.JsonNode.Parse(ZfaModelSerializer.Serialize(new ZfaProjectModel
            {
                Propagation = new PropagationSettings
                {
                    RunRecords = new List<PropagationRunRecord> { SampleRecord() }
                }
            }));
            node["Propagation"]["RunRecords"][0]["SelectedCategories"] =
                new System.Text.Json.Nodes.JsonArray("Residential", "Commercial", "CommunityFacility");

            ZfaProjectModel restored = ZfaModelSerializer.Deserialize(
                node.ToJsonString(), ZfaProjectModel.CurrentSchemaVersion);

            PropagationRunRecord record = Assert.Single(restored.Propagation.RunRecords);
            Assert.Equal(new List<string> { "Residential", "Commercial", "CommunityFacility" },
                record.SelectedCategories);
        }

        [Fact]
        public void PurposeAndCategories_SerializeAsNames_NotNumbers()
        {
            var model = new ZfaProjectModel
            {
                Propagation = new PropagationSettings
                {
                    RunRecords = new List<PropagationRunRecord> { SampleRecord(PropagationPurpose.Egress) }
                }
            };

            string json = ZfaModelSerializer.Serialize(model);
            Assert.Contains("\"Egress\"", json);
            Assert.Contains("\"Commercial Gross\"", json);
        }

        [Fact]
        public void OldPayload_WithoutPropagation_LoadsWithDefaults()
        {
            // A payload written before PropagationSettings existed carries no
            // "Propagation" property — it must load with usable defaults and
            // SchemaVersion 1, no migrator involvement.
            var node = System.Text.Json.Nodes.JsonNode.Parse(
                ZfaModelSerializer.Serialize(new ZfaProjectModel()));
            var obj = (System.Text.Json.Nodes.JsonObject)node;
            obj.Remove("Propagation");
            string json = obj.ToJsonString();

            ZfaProjectModel restored = ZfaModelSerializer.Deserialize(json, ZfaProjectModel.CurrentSchemaVersion);

            Assert.NotNull(restored.Propagation);
            Assert.Null(restored.Propagation.SchemeMappingZoningUniqueId);
            Assert.Null(restored.Propagation.SchemeMappingEgressUniqueId);
            Assert.Null(restored.Propagation.BuildingParameterName);
            Assert.Empty(restored.Propagation.RunRecords);
            Assert.Equal(ZfaProjectModel.CurrentSchemaVersion, restored.SchemaVersion);
        }

        [Fact]
        public void RecordRun_ReplacesTheRecordOfTheSameKey()
        {
            var settings = new PropagationSettings();
            settings.RecordRun(SampleRecord(createdCount: 10));
            settings.RecordRun(SampleRecord(createdCount: 42));

            PropagationRunRecord record = Assert.Single(settings.RunRecords);
            Assert.Equal(42, record.CreatedCount);
        }

        [Fact]
        public void RecordRun_KeepsRecordsOfOtherPurposes()
        {
            var settings = new PropagationSettings();
            settings.RecordRun(SampleRecord(PropagationPurpose.Zoning));
            settings.RecordRun(SampleRecord(PropagationPurpose.Egress));

            Assert.Equal(2, settings.RunRecords.Count);
            Assert.NotNull(settings.FindRecord(BuildingId, GroupId, PropagationPurpose.Zoning));
            Assert.NotNull(settings.FindRecord(BuildingId, GroupId, PropagationPurpose.Egress));
        }

        [Fact]
        public void FindRecord_UnknownKey_ReturnsNull()
        {
            var settings = new PropagationSettings();
            settings.RecordRun(SampleRecord());

            Assert.Null(settings.FindRecord(Guid.NewGuid(), GroupId, PropagationPurpose.Zoning));
            Assert.Null(settings.FindRecord(BuildingId, Guid.NewGuid(), PropagationPurpose.Zoning));
        }
    }
}
