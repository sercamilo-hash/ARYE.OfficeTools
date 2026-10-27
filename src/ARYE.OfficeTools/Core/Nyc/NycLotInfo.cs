using System;
using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>One MapPLUTO tax lot, flattened to plain data. No Revit types.</summary>
    public sealed class NycLotInfo
    {
        public string Bbl { get; set; } = string.Empty;

        public string Borough { get; set; } = string.Empty;

        public string Block { get; set; } = string.Empty;

        public string Lot { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public string ZipCode { get; set; } = string.Empty;

        public double LotAreaSqFt { get; set; }

        public double BldgAreaSqFt { get; set; }

        /// <summary>Legal frontage from DOF assessment records (MapPLUTO LotFront) — NOT measured from the polygon.</summary>
        public double LotFrontRecordedFt { get; set; }

        /// <summary>Legal depth from DOF assessment records (MapPLUTO LotDepth) — NOT measured from the polygon.</summary>
        public double LotDepthRecordedFt { get; set; }

        public int NumFloors { get; set; }

        public int YearBuilt { get; set; }

        public double ResFar { get; set; }

        public double CommFar { get; set; }

        public double FacilFar { get; set; }

        public double BuiltFar { get; set; }

        public string ZoningDistrict1 { get; set; } = string.Empty;

        public string ZoningDistrict2 { get; set; } = string.Empty;

        public string CommercialOverlay1 { get; set; } = string.Empty;

        public string SpecialDistrict1 { get; set; } = string.Empty;

        public string LandUse { get; set; } = string.Empty;

        public string BuildingClass { get; set; } = string.Empty;

        /// <summary>Outer + hole rings, in local feet. Populated from GeoJSON Polygon/MultiPolygon.</summary>
        public List<List<LotPoint>> PolygonRings { get; set; } = new List<List<LotPoint>>();

        public bool IsAdjacent { get; set; }

        private IEnumerable<LotPoint> AllPoints => PolygonRings.SelectMany(r => r);

        public double MinX => AllPoints.Any() ? AllPoints.Min(p => p.X) : 0.0;

        public double MaxX => AllPoints.Any() ? AllPoints.Max(p => p.X) : 0.0;

        public double MinY => AllPoints.Any() ? AllPoints.Min(p => p.Y) : 0.0;

        public double MaxY => AllPoints.Any() ? AllPoints.Max(p => p.Y) : 0.0;

        public double WidthFt => Math.Max(0, MaxX - MinX);

        public double DepthFt => Math.Max(0, MaxY - MinY);

        public LotPoint Centroid
        {
            get
            {
                List<LotPoint> outer = PolygonRings.FirstOrDefault() ?? new List<LotPoint>();
                if (outer.Count == 0)
                {
                    return new LotPoint((MinX + MaxX) / 2, (MinY + MaxY) / 2);
                }
                double x = outer.Average(p => p.X);
                double y = outer.Average(p => p.Y);
                return new LotPoint(x, y);
            }
        }

        public string GetZoningSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ZoningDistrict1)) parts.Add(ZoningDistrict1);
            if (!string.IsNullOrWhiteSpace(ZoningDistrict2)) parts.Add(ZoningDistrict2);
            if (!string.IsNullOrWhiteSpace(CommercialOverlay1)) parts.Add("Overlay: " + CommercialOverlay1);
            if (!string.IsNullOrWhiteSpace(SpecialDistrict1)) parts.Add("Special: " + SpecialDistrict1);
            return parts.Count > 0 ? string.Join(" / ", parts) : "N/A";
        }
    }
}
