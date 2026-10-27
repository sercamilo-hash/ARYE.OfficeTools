using System;
using System.Collections.Generic;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Serialization;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class ZfaModelSerializerTests
    {
        private static ZfaProjectModel SampleModel()
        {
            return new ZfaProjectModel
            {
                Zoning = new ZoningParameters
                {
                    LotAreaSqFt = 10000,
                    MaxFar = 6.02,
                    District = "R7-2"
                },
                Buildings = new List<BuildingDefinition>
                {
                    new BuildingDefinition
                    {
                        Name = "BLDG A",
                        ScopeBoxUniqueId = "sb-uid-1",
                        AreaSchemeUniqueId = "as-uid-1",
                        AtypicalLevels = new List<LevelRef>
                        {
                            new LevelRef { UniqueId = "lvl-7", Name = "L07", ElevationFt = 70 }
                        },
                        Groups = new List<FloorGroup>
                        {
                            new FloorGroup
                            {
                                Name = "Typical L03-L14",
                                Kind = FloorGroupKind.Standard,
                                SourceLevel = new LevelRef { UniqueId = "lvl-3", Name = "L03", ElevationFt = 30 },
                                TargetLevels = new List<LevelRef>
                                {
                                    new LevelRef { UniqueId = "lvl-4", Name = "L04", ElevationFt = 40 }
                                }
                            },
                            new FloorGroup
                            {
                                Name = "Duplex PH",
                                Kind = FloorGroupKind.Duplex,
                                SourceLevel = new LevelRef { UniqueId = "lvl-20", Name = "L20", ElevationFt = 200 },
                                SourceUpperLevel = new LevelRef { UniqueId = "lvl-21", Name = "L21", ElevationFt = 210 },
                                TargetPairs = new List<DuplexLevelPair>
                                {
                                    new DuplexLevelPair
                                    {
                                        Lower = new LevelRef { UniqueId = "lvl-22", Name = "L22", ElevationFt = 220 },
                                        Upper = new LevelRef { UniqueId = "lvl-23", Name = "L23", ElevationFt = 230 }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        [Fact]
        public void RoundTrip_PreservesEverything()
        {
            ZfaProjectModel original = SampleModel();
            string json = ZfaModelSerializer.Serialize(original);
            ZfaProjectModel restored = ZfaModelSerializer.Deserialize(json, ZfaProjectModel.CurrentSchemaVersion);

            Assert.Equal("R7-2", restored.Zoning.District);
            Assert.Equal(6.02, restored.Zoning.MaxFar);
            BuildingDefinition building = Assert.Single(restored.Buildings);
            Assert.Equal(original.Buildings[0].Id, building.Id);
            Assert.Equal("BLDG A", building.Name);
            Assert.Equal(2, building.Groups.Count);
            Assert.Equal(FloorGroupKind.Duplex, building.Groups[1].Kind);
            Assert.Equal("L21", building.Groups[1].SourceUpperLevel.Name);
            Assert.Equal("L23", building.Groups[1].TargetPairs[0].Upper.Name);
            Assert.Equal("L07", Assert.Single(building.AtypicalLevels).Name);
        }

        [Fact]
        public void UnknownFields_AreTolerated()
        {
            // A payload written by a NEWER minor build with extra fields must
            // still load — forward tolerance within the same schema version.
            var node = System.Text.Json.Nodes.JsonNode.Parse(
                ZfaModelSerializer.Serialize(SampleModel()));
            node["futureField"] = new System.Text.Json.Nodes.JsonObject { ["x"] = 1 };
            string json = node.ToJsonString();

            ZfaProjectModel restored = ZfaModelSerializer.Deserialize(json, ZfaProjectModel.CurrentSchemaVersion);
            Assert.Single(restored.Buildings);
        }

        [Fact]
        public void CorruptPayload_ThrowsWithReason_NeverReturnsBlank()
        {
            var ex = Assert.Throws<ZfaSerializationException>(
                () => ZfaModelSerializer.Deserialize("{ not json", 1));
            Assert.Contains("version 1", ex.Message);
        }

        [Fact]
        public void EmptyPayload_Throws()
        {
            Assert.Throws<ZfaSerializationException>(() => ZfaModelSerializer.Deserialize("  ", 1));
        }

        [Fact]
        public void NewerVersionThanBuild_ThrowsUpdateMessage()
        {
            string json = ZfaModelSerializer.Serialize(SampleModel());
            var ex = Assert.Throws<ZfaSerializationException>(
                () => ZfaModelSerializer.Deserialize(json, ZfaProjectModel.CurrentSchemaVersion + 1));
            Assert.Contains("Update BauTools", ex.Message);
        }

        [Fact]
        public void EnumsSerializeAsNames_NotNumbers()
        {
            // Numbers would make the JSON fragile against enum reordering.
            string json = ZfaModelSerializer.Serialize(SampleModel());
            Assert.Contains("\"Duplex\"", json);
            Assert.DoesNotContain("\"Kind\": 2", json);
        }
    }
}
