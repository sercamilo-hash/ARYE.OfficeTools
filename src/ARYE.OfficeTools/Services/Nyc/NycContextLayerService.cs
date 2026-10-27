using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ARYE.OfficeTools.Core.Nyc;

namespace ARYE.OfficeTools.Services.Nyc
{
    /// <summary>
    /// Real ground context from DOF's 2022 citywide planimetric basemap
    /// (roadbeds, sidewalks, curbs — same ArcGIS org already used for the tax
    /// map and building footprints) and NYC Parks' 2025 Tree Census (in
    /// progress). Endpoints and fields verified live 2026-09-23. Every layer
    /// is purely visual context: a failure never blocks the draw, it just
    /// means that layer has nothing to show, logged.
    /// </summary>
    public sealed class NycContextLayerService
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

        private const string RoadbedLayer =
            "https://services6.arcgis.com/yG5s3afENB5iO9fj/arcgis/rest/services/Roadbed_2022/FeatureServer/20/query";
        private const string SidewalkLayer =
            "https://services6.arcgis.com/yG5s3afENB5iO9fj/arcgis/rest/services/Sidewalk_2022/FeatureServer/22/query";
        private const string CurbLayer =
            "https://services6.arcgis.com/yG5s3afENB5iO9fj/arcgis/rest/services/Curb_2022/FeatureServer/4/query";
        private const string TreeLayer =
            "https://services6.arcgis.com/yG5s3afENB5iO9fj/arcgis/rest/services/TC25_TreePoints_SurveyZones_Joined/FeatureServer/1/query";

        static NycContextLayerService()
        {
            Client.DefaultRequestHeaders.Add("User-Agent", "BauTools-Revit-Addin/3.0");
        }

        public Task<List<NycGroundPolygon>> GetRoadbedsAsync(double minX, double minY, double maxX, double maxY) =>
            GetPolygonsAsync(RoadbedLayer, minX, minY, maxX, maxY, "roadbed");

        public Task<List<NycGroundPolygon>> GetSidewalksAsync(double minX, double minY, double maxX, double maxY) =>
            GetPolygonsAsync(SidewalkLayer, minX, minY, maxX, maxY, "sidewalk");

        public async Task<List<NycCurbLine>> GetCurbsAsync(double minX, double minY, double maxX, double maxY)
        {
            var curbs = new List<NycCurbLine>();
            string url = BuildUrl(CurbLayer, minX, minY, maxX, maxY, "OBJECTID");
            string json = await TryGetAsync(url, "curb");
            if (json == null)
            {
                return curbs;
            }
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("features", out JsonElement features) || features.ValueKind != JsonValueKind.Array)
                {
                    return curbs;
                }
                foreach (JsonElement feature in features.EnumerateArray())
                {
                    if (!feature.TryGetProperty("geometry", out JsonElement geom) || !geom.TryGetProperty("type", out JsonElement typeEl))
                    {
                        continue;
                    }
                    string type = typeEl.GetString();
                    if (!geom.TryGetProperty("coordinates", out JsonElement coords))
                    {
                        continue;
                    }
                    if (string.Equals(type, "LineString", StringComparison.OrdinalIgnoreCase))
                    {
                        var path = new List<LotPoint>();
                        AddPath(path, coords);
                        if (path.Count >= 2) curbs.Add(new NycCurbLine { Path = path });
                    }
                    else if (string.Equals(type, "MultiLineString", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (JsonElement line in coords.EnumerateArray())
                        {
                            var path = new List<LotPoint>();
                            AddPath(path, line);
                            if (path.Count >= 2) curbs.Add(new NycCurbLine { Path = path });
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                App.Log?.Warn("Curb response was not valid JSON: " + ex.Message);
            }
            return curbs;
        }

        public async Task<List<NycStreetTree>> GetStreetTreesAsync(double minX, double minY, double maxX, double maxY)
        {
            var trees = new List<NycStreetTree>();
            string url = BuildUrl(TreeLayer, minX, minY, maxX, maxY, "Species,ExistingCircumference,Condition");
            string json = await TryGetAsync(url, "street tree");
            if (json == null)
            {
                return trees;
            }
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("features", out JsonElement features) || features.ValueKind != JsonValueKind.Array)
                {
                    return trees;
                }
                foreach (JsonElement feature in features.EnumerateArray())
                {
                    if (!feature.TryGetProperty("geometry", out JsonElement geom)
                        || !geom.TryGetProperty("coordinates", out JsonElement coords)
                        || coords.GetArrayLength() < 2)
                    {
                        continue;
                    }
                    double x = coords[0].GetDouble();
                    double y = coords[1].GetDouble();
                    JsonElement props = feature.TryGetProperty("properties", out JsonElement p) ? p : default;
                    double circumferenceIn = props.ValueKind == JsonValueKind.Object
                        && props.TryGetProperty("ExistingCircumference", out JsonElement c) && c.ValueKind == JsonValueKind.Number
                        ? c.GetDouble() : 0;
                    trees.Add(new NycStreetTree
                    {
                        Location = new LotPoint(x, y),
                        Species = GetStringProp(props, "Species"),
                        Condition = GetStringProp(props, "Condition"),
                        TrunkDiameterIn = circumferenceIn > 0 ? circumferenceIn / Math.PI : 0
                    });
                }
            }
            catch (JsonException ex)
            {
                App.Log?.Warn("Street tree response was not valid JSON: " + ex.Message);
            }
            return trees;
        }

        private static string GetStringProp(JsonElement props, string name) =>
            props.ValueKind == JsonValueKind.Object && props.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty
                : string.Empty;

        private async Task<List<NycGroundPolygon>> GetPolygonsAsync(string layerUrl, double minX, double minY, double maxX, double maxY, string what)
        {
            var polygons = new List<NycGroundPolygon>();
            string url = BuildUrl(layerUrl, minX, minY, maxX, maxY, "OBJECTID");
            string json = await TryGetAsync(url, what);
            if (json == null)
            {
                return polygons;
            }
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("features", out JsonElement features) || features.ValueKind != JsonValueKind.Array)
                {
                    return polygons;
                }
                foreach (JsonElement feature in features.EnumerateArray())
                {
                    if (!feature.TryGetProperty("geometry", out JsonElement geom) || !geom.TryGetProperty("type", out JsonElement typeEl)
                        || !geom.TryGetProperty("coordinates", out JsonElement coords))
                    {
                        continue;
                    }
                    var polygon = new NycGroundPolygon();
                    string type = typeEl.GetString();
                    if (string.Equals(type, "Polygon", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (JsonElement ring in coords.EnumerateArray())
                        {
                            AddRing(polygon.PolygonRings, ring);
                        }
                    }
                    else if (string.Equals(type, "MultiPolygon", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (JsonElement poly in coords.EnumerateArray())
                        {
                            foreach (JsonElement ring in poly.EnumerateArray())
                            {
                                AddRing(polygon.PolygonRings, ring);
                            }
                        }
                    }
                    if (polygon.PolygonRings.Count > 0)
                    {
                        polygons.Add(polygon);
                    }
                }
            }
            catch (JsonException ex)
            {
                App.Log?.Warn($"{what} response was not valid JSON: " + ex.Message);
            }
            return polygons;
        }

        private static void AddRing(List<List<LotPoint>> into, JsonElement ring)
        {
            var points = new List<LotPoint>();
            AddPath(points, ring);
            if (points.Count >= 3)
            {
                into.Add(points);
            }
        }

        private static void AddPath(List<LotPoint> into, JsonElement path)
        {
            foreach (JsonElement pt in path.EnumerateArray())
            {
                JsonElement.ArrayEnumerator e = pt.EnumerateArray();
                if (e.MoveNext())
                {
                    double x = e.Current.GetDouble();
                    if (e.MoveNext())
                    {
                        into.Add(new LotPoint(x, e.Current.GetDouble()));
                    }
                }
            }
        }

        private static string BuildUrl(string layerUrl, double minX, double minY, double maxX, double maxY, string outFields)
        {
            string env = Uri.EscapeDataString($"{minX:0},{minY:0},{maxX:0},{maxY:0}");
            return $"{layerUrl}?f=geojson&outFields={outFields}&outSR=2263&inSR=2263" +
                   $"&geometryType=esriGeometryEnvelope&spatialRel=esriSpatialRelIntersects&geometry={env}";
        }

        private static async Task<string> TryGetAsync(string url, string what)
        {
            try
            {
                using HttpResponseMessage response = await Client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    App.Log?.Warn($"{what} layer returned HTTP {(int)response.StatusCode} — nothing drawn for it.");
                    return null;
                }
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                App.Log?.Warn($"{what} layer request failed — nothing drawn for it: " + ex.Message);
                return null;
            }
        }
    }
}
