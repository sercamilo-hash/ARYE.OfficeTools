using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ARYE.OfficeTools.Services.Nyc
{
    /// <summary>One mapped street from DCP's Digital City Map.</summary>
    public sealed class MappedStreet
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Official width between property lines, feet (NOT curb to curb).</summary>
        public double WidthFt { get; set; }

        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// DCP Digital City Map — street center lines with the OFFICIAL mapped
    /// width (property line to property line). This is the width that
    /// matters for sidewalks and for checking a lot's street line; the DOITT
    /// centerline dataset's width is curb-to-curb and must not be confused.
    /// Verified live 2026-09-12 (Concord Ave 60, Wales Ave 65, E 145 St 50).
    /// </summary>
    public sealed class NycStreetWidthService
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        private const string Layer =
            "https://services5.arcgis.com/GfwWNkhOj9bNBqoJ/arcgis/rest/services/DCM_Street_Center_Line/FeatureServer/0/query";

        static NycStreetWidthService()
        {
            Client.DefaultRequestHeaders.Add("User-Agent", "BauTools-Revit-Addin/3.0");
        }

        /// <summary>
        /// Mapped streets intersecting the envelope (EPSG 2263 feet), distinct by
        /// name. Empty list on any failure — widths are a suggestion, never a
        /// blocker; the reason is logged.
        /// </summary>
        public async Task<List<MappedStreet>> GetMappedStreetsAsync(double minX, double minY, double maxX, double maxY)
        {
            string env = Uri.EscapeDataString($"{minX:0},{minY:0},{maxX:0},{maxY:0}");
            string url = $"{Layer}?f=json&outFields=Street_NM,Streetwidth,Feat_status&returnGeometry=false" +
                         $"&inSR=2263&geometryType=esriGeometryEnvelope&spatialRel=esriSpatialRelIntersects&geometry={env}";
            try
            {
                using HttpResponseMessage response = await Client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    App.Log?.Warn($"Digital City Map returned HTTP {(int)response.StatusCode} — no street widths.");
                    return new List<MappedStreet>();
                }
                string json = await response.Content.ReadAsStringAsync();
                return Parse(json);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
            {
                App.Log?.Warn("Digital City Map request failed — no street widths: " + ex.Message);
                return new List<MappedStreet>();
            }
        }

        internal static List<MappedStreet> Parse(string json)
        {
            var byName = new Dictionary<string, MappedStreet>(StringComparer.OrdinalIgnoreCase);
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out JsonElement features) || features.ValueKind != JsonValueKind.Array)
            {
                return new List<MappedStreet>();
            }
            foreach (JsonElement feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("attributes", out JsonElement a))
                {
                    continue;
                }
                string name = a.TryGetProperty("Street_NM", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                double width = a.TryGetProperty("Streetwidth", out JsonElement w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble() : 0;
                string status = a.TryGetProperty("Feat_status", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
                if (name.Length == 0 || width <= 0 || byName.ContainsKey(name))
                {
                    continue;
                }
                byName[name] = new MappedStreet { Name = name, WidthFt = width, Status = status };
            }
            return new List<MappedStreet>(byName.Values);
        }
    }
}
