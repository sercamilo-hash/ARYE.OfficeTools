using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ARYE.OfficeTools.Core.Nyc;

namespace ARYE.OfficeTools.Services.Nyc
{
    /// <summary>
    /// NYC Building Footprints (real roof heights) — endpoint and field names
    /// verified live against the service (2026-09): MAPPLUTO_BBL joins
    /// directly to MapPLUTO's BBL, HEIGHT_ROOF is feet above ground elevation.
    /// One batched query for the whole block context instead of one per lot.
    /// </summary>
    public sealed class NycBuildingFootprintService
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

        private const string FootprintFeatureServer =
            "https://services6.arcgis.com/yG5s3afENB5iO9fj/arcgis/rest/services/BUILDING_view/FeatureServer/0/query";

        static NycBuildingFootprintService()
        {
            Client.DefaultRequestHeaders.Add("User-Agent", "BauTools-Revit-Addin/3.0");
        }

        /// <summary>
        /// Empty result on failure or when there is nothing to query — building
        /// heights degrade to the honest estimate via BuildingHeightResolver,
        /// they never block the draw.
        /// </summary>
        public async Task<List<NycBuildingFootprint>> GetFootprintsAsync(IEnumerable<string> bbls)
        {
            List<string> distinctBbls = bbls.Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().ToList();
            if (distinctBbls.Count == 0)
            {
                return new List<NycBuildingFootprint>();
            }

            string inList = string.Join(",", distinctBbls.Select(b => $"'{b}'"));
            string where = Uri.EscapeDataString($"MAPPLUTO_BBL IN ({inList})");
            string url = $"{FootprintFeatureServer}?where={where}&outFields=*&outSR=2263&f=geojson";

            HttpResponseMessage response;
            try
            {
                response = await Client.GetAsync(url);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                App.Log?.Warn("Building Footprints request failed — heights will be estimated: " + ex.Message);
                return new List<NycBuildingFootprint>();
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    App.Log?.Warn($"Building Footprints returned {(int)response.StatusCode} — heights will be estimated.");
                    return new List<NycBuildingFootprint>();
                }

                string json = await response.Content.ReadAsStringAsync();
                var result = new List<NycBuildingFootprint>();
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("features", out JsonElement features)
                        && features.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement feature in features.EnumerateArray())
                        {
                            result.Add(ParseFeature(feature));
                        }
                    }
                }
                catch (JsonException ex)
                {
                    App.Log?.Warn("Building Footprints response was not valid JSON — heights will be estimated: " + ex.Message);
                    return new List<NycBuildingFootprint>();
                }
                return result;
            }
        }

        private static NycBuildingFootprint ParseFeature(JsonElement feature)
        {
            JsonElement props = feature.GetProperty("properties");
            var footprint = new NycBuildingFootprint
            {
                Bin = GetString(props, "BIN"),
                MapplutoBbl = GetString(props, "MAPPLUTO_BBL"),
                HeightRoofFt = GetDouble(props, "HEIGHT_ROOF"),
                GroundElevationFt = GetDouble(props, "GROUND_ELEVATION"),
                ConstructionYear = (int)GetDouble(props, "CONSTRUCTION_YEAR"),
                FeatureCode = GetString(props, "FEATURE_CODE")
            };

            if (feature.TryGetProperty("geometry", out JsonElement geom)
                && geom.TryGetProperty("type", out JsonElement typeEl)
                && geom.TryGetProperty("coordinates", out JsonElement coords))
            {
                string type = typeEl.GetString();
                if (string.Equals(type, "Polygon", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (JsonElement ring in coords.EnumerateArray())
                    {
                        AddRing(footprint.PolygonRings, ring);
                    }
                }
                else if (string.Equals(type, "MultiPolygon", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (JsonElement poly in coords.EnumerateArray())
                    {
                        foreach (JsonElement ring in poly.EnumerateArray())
                        {
                            AddRing(footprint.PolygonRings, ring);
                        }
                    }
                }
            }
            return footprint;
        }

        private static void AddRing(List<List<LotPoint>> into, JsonElement ring)
        {
            var points = new List<LotPoint>();
            foreach (JsonElement pt in ring.EnumerateArray())
            {
                JsonElement.ArrayEnumerator e = pt.EnumerateArray();
                if (e.MoveNext())
                {
                    double x = e.Current.GetDouble();
                    if (e.MoveNext())
                    {
                        points.Add(new LotPoint(x, e.Current.GetDouble()));
                    }
                }
            }
            // GeoJSON rings repeat the first vertex as the last one — drop the duplicate.
            if (points.Count >= 2
                && Math.Abs(points[0].X - points[points.Count - 1].X) < 1e-6
                && Math.Abs(points[0].Y - points[points.Count - 1].Y) < 1e-6)
            {
                points.RemoveAt(points.Count - 1);
            }
            if (points.Count >= 3)
            {
                into.Add(points);
            }
        }

        private static string GetString(JsonElement elem, string name) =>
            elem.TryGetProperty(name, out JsonElement val) && val.ValueKind == JsonValueKind.String
                ? (val.GetString() ?? string.Empty)
                : (elem.TryGetProperty(name, out val) && val.ValueKind == JsonValueKind.Number ? val.ToString() : string.Empty);

        private static double GetDouble(JsonElement elem, string name) =>
            elem.TryGetProperty(name, out JsonElement val) && val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out double d)
                ? d
                : 0.0;
    }
}
