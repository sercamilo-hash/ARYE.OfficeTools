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
    /// DOF Digital Tax Map — TAX_LOT_FACE layer: one polyline per lot side with
    /// the legal dimension written on the official tax map sheet
    /// (LOT_FACE_LENGTH), DOF's digitized length (Distance), bearing
    /// (Direction) and whether the side is on the block perimeter
    /// (BLOCK_FACE_FLAG). Endpoint and fields verified live 2026-09-12; see
    /// docs/NYC-LOT-DATA-SOURCES.md.
    /// </summary>
    public sealed class NycTaxLotFaceService
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

        private const string FaceLayer =
            "https://services6.arcgis.com/yG5s3afENB5iO9fj/arcgis/rest/services/Digital_Tax_Map_VIEW/FeatureServer/4/query";

        static NycTaxLotFaceService()
        {
            Client.DefaultRequestHeaders.Add("User-Agent", "BauTools-Revit-Addin/3.0");
        }

        /// <summary>
        /// Faces of one lot in EPSG 2263 feet, unordered (use LotFaceChain).
        /// Throws NycApiException with the real reason when the service fails;
        /// returns an empty list when DOF simply has no faces for the BBL.
        /// </summary>
        public async Task<List<NycLotFace>> GetFacesAsync(string bbl)
        {
            string where = Uri.EscapeDataString($"BBL='{bbl}'");
            string url = $"{FaceLayer}?where={where}&outFields=*&outSR=2263&f=json";

            HttpResponseMessage response;
            try
            {
                response = await Client.GetAsync(url);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                throw new NycApiException("Tax lot faces request failed: " + ex.Message, ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new NycApiException($"Tax lot faces service returned HTTP {(int)response.StatusCode}.");
                }
                string json = await response.Content.ReadAsStringAsync();
                try
                {
                    return Parse(json, bbl);
                }
                catch (JsonException ex)
                {
                    throw new NycApiException("Tax lot faces response was not valid JSON: " + ex.Message, ex);
                }
            }
        }

        /// <summary>
        /// Faces for many lots in one request, grouped by BBL. A BBL absent
        /// from the result simply has no rows published — never an error on
        /// its own; the caller decides what that means. Throws NycApiException
        /// only when the whole request fails (network, HTTP, malformed JSON).
        /// </summary>
        public async Task<Dictionary<string, List<NycLotFace>>> GetFacesForBblsAsync(IEnumerable<string> bbls)
        {
            List<string> distinct = bbls.Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().ToList();
            var result = new Dictionary<string, List<NycLotFace>>();
            if (distinct.Count == 0)
            {
                return result;
            }

            string inList = string.Join(",", distinct.Select(b => $"'{b}'"));
            string where = Uri.EscapeDataString($"BBL IN ({inList})");
            string url = $"{FaceLayer}?where={where}&outFields=*&outSR=2263&f=json";

            HttpResponseMessage response;
            try
            {
                response = await Client.GetAsync(url);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                throw new NycApiException("Tax lot faces (batch) request failed: " + ex.Message, ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new NycApiException($"Tax lot faces (batch) service returned HTTP {(int)response.StatusCode}.");
                }
                string json = await response.Content.ReadAsStringAsync();
                List<NycLotFace> all;
                try
                {
                    all = Parse(json, bbl: string.Empty); // BBL comes from each feature's own attribute
                }
                catch (JsonException ex)
                {
                    throw new NycApiException("Tax lot faces (batch) response was not valid JSON: " + ex.Message, ex);
                }
                foreach (NycLotFace face in all)
                {
                    if (!result.TryGetValue(face.Bbl, out List<NycLotFace> list))
                    {
                        list = new List<NycLotFace>();
                        result[face.Bbl] = list;
                    }
                    list.Add(face);
                }
                return result;
            }
        }

        internal static List<NycLotFace> Parse(string json, string bbl)
        {
            var faces = new List<NycLotFace>();
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out JsonElement error))
            {
                string message = error.TryGetProperty("message", out JsonElement m) ? m.GetString() : "unknown error";
                throw new NycApiException("Tax lot faces service error: " + message);
            }
            if (!doc.RootElement.TryGetProperty("features", out JsonElement features)
                || features.ValueKind != JsonValueKind.Array)
            {
                return faces;
            }

            foreach (JsonElement feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("attributes", out JsonElement attrs))
                {
                    continue;
                }
                var face = new NycLotFace
                {
                    Bbl = GetString(attrs, "BBL", bbl),
                    LotUuid = GetString(attrs, "LOT_UUID", string.Empty),
                    LegalLengthFt = GetDouble(attrs, "LOT_FACE_LENGTH"),
                    DigitizedLengthFt = GetDouble(attrs, "Distance"),
                    BearingDeg = GetDouble(attrs, "Direction"),
                    IsStreetFace = GetDouble(attrs, "BLOCK_FACE_FLAG") >= 1
                };

                if (feature.TryGetProperty("geometry", out JsonElement geom)
                    && geom.TryGetProperty("paths", out JsonElement paths)
                    && paths.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement path in paths.EnumerateArray())
                    {
                        foreach (JsonElement pt in path.EnumerateArray())
                        {
                            JsonElement.ArrayEnumerator e = pt.EnumerateArray();
                            if (e.MoveNext())
                            {
                                double x = e.Current.GetDouble();
                                if (e.MoveNext())
                                {
                                    face.Path.Add(new LotPoint(x, e.Current.GetDouble()));
                                }
                            }
                        }
                    }
                }

                if (face.Path.Count >= 2)
                {
                    faces.Add(face);
                }
            }
            return faces;
        }

        private static string GetString(JsonElement elem, string name, string fallback)
        {
            if (!elem.TryGetProperty(name, out JsonElement val))
            {
                return fallback;
            }
            return val.ValueKind switch
            {
                JsonValueKind.String => val.GetString() ?? fallback,
                JsonValueKind.Number => val.ToString(),
                _ => fallback
            };
        }

        private static double GetDouble(JsonElement elem, string name) =>
            elem.TryGetProperty(name, out JsonElement val) && val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out double d)
                ? d
                : 0.0;
    }
}
