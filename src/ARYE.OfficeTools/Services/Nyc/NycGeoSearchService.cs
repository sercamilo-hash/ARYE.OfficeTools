using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ARYE.OfficeTools.Core.Nyc;

namespace ARYE.OfficeTools.Services.Nyc
{
    public sealed class NycAddressCandidate
    {
        public string Label { get; set; } = string.Empty;

        public string Bbl { get; set; } = string.Empty;

        public override string ToString() => Label;
    }

    /// <summary>
    /// NYC Planning GeoSearch (address autocomplete) and MapPLUTO (lot + block
    /// context) — pure HTTP client, no Revit types anywhere. Every failure
    /// throws <see cref="NycApiException"/> with the real reason: a search
    /// that times out must never look identical to "no such lot".
    /// </summary>
    public sealed class NycGeoSearchService
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

        private const string PlutoFeatureServer =
            "https://services5.arcgis.com/GfwWNkhOj9bNBqoJ/arcgis/rest/services/MAPPLUTO/FeatureServer/0/query";

        static NycGeoSearchService()
        {
            Client.DefaultRequestHeaders.Add("User-Agent", "BauTools-Revit-Addin/3.0");
        }

        public async Task<List<NycAddressCandidate>> SearchAddressAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<NycAddressCandidate>();
            }

            string url = "https://geosearch.planninglabs.nyc/v1/autocomplete?text=" + Uri.EscapeDataString(query);
            JsonDocument doc = await GetJsonAsync(url, "address search");

            var results = new List<NycAddressCandidate>();
            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("features", out JsonElement features)
                    || features.ValueKind != JsonValueKind.Array)
                {
                    return results;
                }
                foreach (JsonElement feature in features.EnumerateArray())
                {
                    if (!feature.TryGetProperty("properties", out JsonElement props))
                    {
                        continue;
                    }
                    string bbl = GetString(props, "pad_bbl");
                    if (string.IsNullOrEmpty(bbl)
                        && props.TryGetProperty("addendum", out JsonElement addendum)
                        && addendum.TryGetProperty("pad", out JsonElement pad))
                    {
                        bbl = GetString(pad, "bbl");
                    }
                    if (string.IsNullOrEmpty(bbl))
                    {
                        continue; // an address with no BBL can't be drawn — skip rather than show a dead end.
                    }
                    results.Add(new NycAddressCandidate { Label = GetString(props, "label"), Bbl = bbl });
                }
            }
            return results;
        }

        public async Task<NycLotInfo> GetLotByBblAsync(string bbl)
        {
            string clean = NormalizeBbl(bbl);
            if (clean.Length == 0)
            {
                throw new NycApiException($"'{bbl}' is not a valid 10-digit BBL.");
            }

            string url = $"{PlutoFeatureServer}?where=BBL%3D{clean}&outFields=*&outSR=2263&f=geojson";
            JsonDocument doc = await GetJsonAsync(url, "MapPLUTO lookup");
            using (doc)
            {
                JsonElement first = FirstFeatureOrNull(doc);
                if (first.ValueKind == JsonValueKind.Undefined)
                {
                    throw new NycApiException($"MapPLUTO has no lot for BBL {clean}.");
                }
                return ParseLotFeature(first);
            }
        }

        public async Task<NycBlockContext> GetBlockContextAsync(NycLotInfo subject)
        {
            var context = new NycBlockContext
            {
                Borough = subject.Borough,
                BlockNumber = subject.Block,
                SubjectLot = subject
            };
            if (string.IsNullOrWhiteSpace(subject.Block))
            {
                return context;
            }

            List<NycLotInfo> lots = await GetLotsByBlockAsync(subject.Borough, subject.Block);
            foreach (NycLotInfo lot in lots)
            {
                if (lot.Bbl != subject.Bbl)
                {
                    context.OtherLots.Add(lot);
                }
            }
            context.CalculateAdjacency();
            return context;
        }

        /// <summary>
        /// Every MapPLUTO lot on one block, by borough + block number — no
        /// subject, no adjacency. Used both for the subject's own block context
        /// and for an entirely different block added as far urban context.
        /// </summary>
        public async Task<List<NycLotInfo>> GetLotsByBlockAsync(string borough, string block)
        {
            var lots = new List<NycLotInfo>();
            if (string.IsNullOrWhiteSpace(block) || string.IsNullOrWhiteSpace(borough))
            {
                return lots;
            }

            string boro = GetBoroughCode(borough);
            string url = $"{PlutoFeatureServer}?where=Block%3D{block}+AND+Borough%3D%27{boro}%27" +
                         "&outFields=*&outSR=2263&f=geojson";
            JsonDocument doc = await GetJsonAsync(url, "block lookup");
            using (doc)
            {
                if (doc.RootElement.TryGetProperty("features", out JsonElement features)
                    && features.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement feature in features.EnumerateArray())
                    {
                        lots.Add(ParseLotFeature(feature));
                    }
                }
            }
            return lots;
        }

        // ── parsing ────────────────────────────────────────────────────────

        private static NycLotInfo ParseLotFeature(JsonElement feature)
        {
            JsonElement props = feature.GetProperty("properties");
            var lot = new NycLotInfo
            {
                Bbl = GetString(props, "BBL"),
                Address = GetString(props, "Address"),
                Borough = GetBoroughName(GetString(props, "Borough")),
                Block = GetString(props, "Block"),
                Lot = GetString(props, "Lot"),
                ZipCode = GetString(props, "ZipCode"),
                ZoningDistrict1 = GetString(props, "ZoneDist1"),
                ZoningDistrict2 = GetString(props, "ZoneDist2"),
                CommercialOverlay1 = GetString(props, "Overlay1"),
                SpecialDistrict1 = GetString(props, "SPDist1"),
                ResFar = GetDouble(props, "ResidFAR"),
                CommFar = GetDouble(props, "CommFAR"),
                FacilFar = GetDouble(props, "FacilFAR"),
                BuiltFar = GetDouble(props, "BuiltFAR"),
                LotAreaSqFt = GetDouble(props, "LotArea"),
                BldgAreaSqFt = GetDouble(props, "BldgArea"),
                LotFrontRecordedFt = GetDouble(props, "LotFront"),
                LotDepthRecordedFt = GetDouble(props, "LotDepth"),
                NumFloors = (int)GetDouble(props, "NumFloors"),
                YearBuilt = (int)GetDouble(props, "YearBuilt"),
                LandUse = GetString(props, "LandUse"),
                BuildingClass = GetString(props, "BldgClass")
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
                        AddRing(lot.PolygonRings, ring);
                    }
                }
                else if (string.Equals(type, "MultiPolygon", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (JsonElement poly in coords.EnumerateArray())
                    {
                        foreach (JsonElement ring in poly.EnumerateArray())
                        {
                            AddRing(lot.PolygonRings, ring);
                        }
                    }
                }
            }
            return lot;
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
            // GeoJSON rings repeat the first vertex as the last one. Drop it here so
            // every consumer (offset, centroid, edge lengths) sees a clean open ring
            // — a zero-length closing edge corrupts the sidewalk offset corner.
            if (points.Count >= 2 && SamePoint(points[0], points[points.Count - 1]))
            {
                points.RemoveAt(points.Count - 1);
            }
            if (points.Count >= 3)
            {
                into.Add(points);
            }
        }

        private static bool SamePoint(LotPoint a, LotPoint b) =>
            Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;

        private static JsonElement FirstFeatureOrNull(JsonDocument doc)
        {
            if (doc.RootElement.TryGetProperty("features", out JsonElement features)
                && features.ValueKind == JsonValueKind.Array)
            {
                JsonElement.ArrayEnumerator e = features.EnumerateArray();
                if (e.MoveNext())
                {
                    return e.Current;
                }
            }
            return default;
        }

        private static async Task<JsonDocument> GetJsonAsync(string url, string what)
        {
            HttpResponseMessage response;
            try
            {
                response = await Client.GetAsync(url);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                throw new NycApiException($"Could not reach the NYC {what} service — check your internet connection.", ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new NycApiException(
                        $"The NYC {what} service returned {(int)response.StatusCode} {response.ReasonPhrase}.");
                }
                string json = await response.Content.ReadAsStringAsync();
                try
                {
                    return JsonDocument.Parse(json);
                }
                catch (JsonException ex)
                {
                    throw new NycApiException($"The NYC {what} service returned a response that isn't valid JSON.", ex);
                }
            }
        }

        public static string NormalizeBbl(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }
            string digits = new string(raw.Where(char.IsDigit).ToArray());
            return digits.Length == 10 ? digits : string.Empty;
        }

        public static string GetBoroughCode(string nameOrAbbr)
        {
            if (string.IsNullOrWhiteSpace(nameOrAbbr)) return "MN";
            string upper = nameOrAbbr.Trim().ToUpperInvariant();
            if (upper.Contains("MANHATTAN") || upper == "1" || upper == "MN") return "MN";
            if (upper.Contains("BRONX") || upper == "2" || upper == "BX") return "BX";
            if (upper.Contains("BROOKLYN") || upper == "3" || upper == "BK") return "BK";
            if (upper.Contains("QUEENS") || upper == "4" || upper == "QN") return "QN";
            if (upper.Contains("STATEN") || upper == "5" || upper == "SI") return "SI";
            return "MN";
        }

        private static string GetBoroughName(string codeOrAbbr)
        {
            if (string.IsNullOrWhiteSpace(codeOrAbbr)) return "Unknown";
            string upper = codeOrAbbr.Trim().ToUpperInvariant();
            switch (upper)
            {
                case "1": case "MN": case "MANHATTAN": return "Manhattan";
                case "2": case "BX": case "BRONX": return "Bronx";
                case "3": case "BK": case "BROOKLYN": return "Brooklyn";
                case "4": case "QN": case "QUEENS": return "Queens";
                case "5": case "SI": case "STATEN ISLAND": return "Staten Island";
                default: return codeOrAbbr;
            }
        }

        private static string GetString(JsonElement elem, string name)
        {
            if (!elem.TryGetProperty(name, out JsonElement val))
            {
                return string.Empty;
            }
            if (val.ValueKind == JsonValueKind.String)
            {
                return (val.GetString() ?? string.Empty).Trim();
            }
            if (val.ValueKind == JsonValueKind.Number)
            {
                return val.ToString();
            }
            return string.Empty;
        }

        private static double GetDouble(JsonElement elem, string name)
        {
            if (!elem.TryGetProperty(name, out JsonElement val))
            {
                return 0.0;
            }
            if (val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out double d))
            {
                return d;
            }
            if (val.ValueKind == JsonValueKind.String
                && double.TryParse(val.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
            }
            return 0.0;
        }
    }
}
