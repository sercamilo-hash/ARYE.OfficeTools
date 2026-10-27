using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>How a Building Footprint was attached to a lot.</summary>
    public enum FootprintMatchKind
    {
        /// <summary>MAPPLUTO_BBL on the footprint equals the lot's own BBL.</summary>
        ByBbl = 0,

        /// <summary>The BBL join found nothing; the footprint's centroid falls inside the lot's ring instead.</summary>
        SpatialFallback = 1
    }

    public sealed class FootprintMatch
    {
        public NycBuildingFootprint Footprint { get; set; }
        public FootprintMatchKind Kind { get; set; }
    }

    /// <summary>
    /// Which Building Footprints belong to a given lot: BBL join first, then
    /// "whose centroid lands inside this lot's ring" when the join finds
    /// nothing — a footprint with an empty or stale MAPPLUTO_BBL is otherwise
    /// invisible even when it is drawn right inside the lot. Single source of
    /// truth so mass generation and the 2D preview can never disagree about
    /// which footprint goes with which lot.
    /// </summary>
    public static class NycFootprintMatcher
    {
        public static List<FootprintMatch> MatchForLot(
            string lotBbl, IReadOnlyList<LotPoint> lotRing, IEnumerable<NycBuildingFootprint> footprints)
        {
            List<NycBuildingFootprint> withGeometry = footprints.Where(f => f.PolygonRings.Count > 0).ToList();

            List<NycBuildingFootprint> byBbl = withGeometry.Where(f => f.MapplutoBbl == lotBbl).ToList();
            if (byBbl.Count > 0)
            {
                return byBbl.Select(f => new FootprintMatch { Footprint = f, Kind = FootprintMatchKind.ByBbl }).ToList();
            }

            if (lotRing == null || lotRing.Count < 3)
            {
                return new List<FootprintMatch>();
            }

            return withGeometry
                .Where(f => LotGeometry.PointInPolygon(LotGeometry.Centroid(f.PolygonRings[0]), lotRing))
                .Select(f => new FootprintMatch { Footprint = f, Kind = FootprintMatchKind.SpatialFallback })
                .ToList();
        }
    }
}
