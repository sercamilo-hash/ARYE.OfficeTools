using System.Linq;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Serialization;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    /// <summary>
    /// The fields added for the standalone Sheet Diagrammer (sheet collection,
    /// browser-organization values, additional views) must round-trip and must
    /// default to empty on payloads written before they existed.
    /// </summary>
    public class SheetPackageInteropFieldsTests
    {
        [Fact]
        public void NewFields_RoundTripThroughTheSerializer()
        {
            var model = new ZfaProjectModel();
            SheetPackage gross = model.SheetPackages.First(p => p.Name == "Gross");
            gross.SheetCollectionName = "ZONING";
            gross.SheetParameterValues.Add(new ParameterValueAssignment { ParameterName = "Sheet Group", Value = "ZONING" });
            gross.ViewParameterValues.Add(new ParameterValueAssignment { ParameterName = "View Group", Value = "04 ZONING" });
            gross.ViewParameterValues.Add(new ParameterValueAssignment { ParameterName = "Sub-Discipline", Value = "ZFA DIAGRAMS" });
            gross.AdditionalViews.Add(new AdditionalViewRef { ViewUniqueId = "v-1", ViewName = "Section A-A", ViewTypeName = "Section", OwnSheet = true });
            gross.AdditionalViews.Add(new AdditionalViewRef { ViewUniqueId = "v-2", ViewName = "ZFA Matrix", ViewTypeName = "Schedule", OwnSheet = false });

            string json = ZfaModelSerializer.Serialize(model);
            ZfaProjectModel restored = ZfaModelSerializer.Deserialize(json, ZfaProjectModel.CurrentSchemaVersion);
            SheetPackage r = restored.SheetPackages.Single(p => p.Id == gross.Id);

            Assert.Equal("ZONING", r.SheetCollectionName);
            Assert.Single(r.SheetParameterValues);
            Assert.Equal("Sheet Group", r.SheetParameterValues[0].ParameterName);
            Assert.Equal(2, r.ViewParameterValues.Count);
            Assert.Equal("ZFA DIAGRAMS", r.ViewParameterValues[1].Value);
            Assert.Equal(2, r.AdditionalViews.Count);
            Assert.True(r.AdditionalViews[0].OwnSheet);
            Assert.False(r.AdditionalViews[1].OwnSheet);
            Assert.Equal("Schedule", r.AdditionalViews[1].ViewTypeName);
        }

        [Fact]
        public void OldPayload_WithoutNewFields_LoadsWithEmptyDefaults()
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(ZfaModelSerializer.Serialize(new ZfaProjectModel()));
            var packages = (System.Text.Json.Nodes.JsonArray)node["SheetPackages"];
            foreach (System.Text.Json.Nodes.JsonNode p in packages)
            {
                var obj = (System.Text.Json.Nodes.JsonObject)p;
                obj.Remove("SheetCollectionName");
                obj.Remove("SheetParameterValues");
                obj.Remove("ViewParameterValues");
                obj.Remove("AdditionalViews");
            }

            ZfaProjectModel restored = ZfaModelSerializer.Deserialize(node.ToJsonString(), ZfaProjectModel.CurrentSchemaVersion);

            Assert.Equal(4, restored.SheetPackages.Count);
            Assert.All(restored.SheetPackages, p => Assert.Null(p.SheetCollectionName));
            Assert.All(restored.SheetPackages, p => Assert.Empty(p.SheetParameterValues));
            Assert.All(restored.SheetPackages, p => Assert.Empty(p.ViewParameterValues));
            Assert.All(restored.SheetPackages, p => Assert.Empty(p.AdditionalViews));
            Assert.Equal(ZfaProjectModel.CurrentSchemaVersion, restored.SchemaVersion);
        }

        [Fact]
        public void Margins_Unset_FallBackToLegacyQuarterInchPlusTitleStrip()
        {
            var package = new SheetPackage { TitleStripWidthIn = 2.0 };

            Assert.Equal(0.25, package.EffectiveMarginLeftIn);
            Assert.Equal(0.25, package.EffectiveMarginTopIn);
            Assert.Equal(0.25, package.EffectiveMarginBottomIn);
            Assert.Equal(2.25, package.EffectiveMarginRightIn);
        }

        [Fact]
        public void Margins_Set_WinOverTheLegacyStrip_AndRoundTrip()
        {
            var model = new ZfaProjectModel();
            SheetPackage p = model.SheetPackages[0];
            p.MarginLeftIn = 0.5; p.MarginRightIn = 3.0; p.MarginTopIn = 0.75; p.MarginBottomIn = 1.0;
            p.ScopeBoxUniqueId = "sb-1";

            ZfaProjectModel restored = ZfaModelSerializer.Deserialize(ZfaModelSerializer.Serialize(model), ZfaProjectModel.CurrentSchemaVersion);
            SheetPackage r = restored.SheetPackages.Single(x => x.Id == p.Id);

            Assert.Equal(0.5, r.EffectiveMarginLeftIn);
            Assert.Equal(3.0, r.EffectiveMarginRightIn);
            Assert.Equal(0.75, r.EffectiveMarginTopIn);
            Assert.Equal(1.0, r.EffectiveMarginBottomIn);
            Assert.Equal("sb-1", r.ScopeBoxUniqueId);
        }

        [Fact]
        public void CloneAsNew_DeepCopiesEverythingButIdentity()
        {
            var source = new SheetPackage { Name = "Gross", SheetCollectionName = "ZONING", Columns = 4 };
            source.ViewParameterValues.Add(new ParameterValueAssignment { ParameterName = "View Group", Value = "04" });
            source.AdditionalViews.Add(new AdditionalViewRef { ViewUniqueId = "v", ViewName = "S", OwnSheet = false });

            SheetPackage copy = source.CloneAsNew("Gross copy");

            Assert.NotEqual(source.Id, copy.Id);
            Assert.Equal("Gross copy", copy.Name);
            Assert.Equal("ZONING", copy.SheetCollectionName);
            Assert.Equal(4, copy.Columns);
            Assert.Single(copy.ViewParameterValues);
            Assert.NotSame(source.ViewParameterValues[0], copy.ViewParameterValues[0]);
            Assert.NotSame(source.AdditionalViews[0], copy.AdditionalViews[0]);
            Assert.False(copy.AdditionalViews[0].OwnSheet);
        }

        [Fact]
        public void RestrictToFloorRangeLevels_DefaultsTrue_AndRoundTripsWhenTurnedOff()
        {
            // A brand-new package, and an old payload missing the field entirely,
            // must both behave exactly as the tool always has: restricted on.
            Assert.True(new SheetPackage().RestrictToFloorRangeLevels);

            var node = System.Text.Json.Nodes.JsonNode.Parse(ZfaModelSerializer.Serialize(new ZfaProjectModel()));
            var packages = (System.Text.Json.Nodes.JsonArray)node["SheetPackages"];
            foreach (System.Text.Json.Nodes.JsonNode p in packages)
            {
                ((System.Text.Json.Nodes.JsonObject)p).Remove("RestrictToFloorRangeLevels");
            }
            ZfaProjectModel oldPayload = ZfaModelSerializer.Deserialize(node.ToJsonString(), ZfaProjectModel.CurrentSchemaVersion);
            Assert.All(oldPayload.SheetPackages, p => Assert.True(p.RestrictToFloorRangeLevels));

            // Explicitly turning it off must round-trip and must survive CloneAsNew.
            var model = new ZfaProjectModel();
            SheetPackage gross = model.SheetPackages[0];
            gross.RestrictToFloorRangeLevels = false;

            ZfaProjectModel restored = ZfaModelSerializer.Deserialize(ZfaModelSerializer.Serialize(model), ZfaProjectModel.CurrentSchemaVersion);
            SheetPackage r = restored.SheetPackages.Single(p => p.Id == gross.Id);

            Assert.False(r.RestrictToFloorRangeLevels);
            Assert.False(gross.CloneAsNew("Gross copy").RestrictToFloorRangeLevels);
        }

        [Fact]
        public void DiagramSourceLevelsOnly_DefaultsFalse_AndRoundTripsWhenTurnedOn()
        {
            // Unchanged one-view-per-level behavior on a brand-new package and on
            // an old payload missing the field.
            Assert.False(new SheetPackage().DiagramSourceLevelsOnly);

            var node = System.Text.Json.Nodes.JsonNode.Parse(ZfaModelSerializer.Serialize(new ZfaProjectModel()));
            var packages = (System.Text.Json.Nodes.JsonArray)node["SheetPackages"];
            foreach (System.Text.Json.Nodes.JsonNode p in packages)
            {
                ((System.Text.Json.Nodes.JsonObject)p).Remove("DiagramSourceLevelsOnly");
            }
            ZfaProjectModel oldPayload = ZfaModelSerializer.Deserialize(node.ToJsonString(), ZfaProjectModel.CurrentSchemaVersion);
            Assert.All(oldPayload.SheetPackages, p => Assert.False(p.DiagramSourceLevelsOnly));

            var model = new ZfaProjectModel();
            SheetPackage gross = model.SheetPackages[0];
            gross.DiagramSourceLevelsOnly = true;

            ZfaProjectModel restored = ZfaModelSerializer.Deserialize(ZfaModelSerializer.Serialize(model), ZfaProjectModel.CurrentSchemaVersion);
            SheetPackage r = restored.SheetPackages.Single(p => p.Id == gross.Id);

            Assert.True(r.DiagramSourceLevelsOnly);
            Assert.True(gross.CloneAsNew("Gross copy").DiagramSourceLevelsOnly);
        }
    }
}
