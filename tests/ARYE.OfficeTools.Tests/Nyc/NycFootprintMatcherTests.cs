using System.Collections.Generic;
using ARYE.OfficeTools.Core.Nyc;
using Xunit;

namespace ARYE.OfficeTools.Tests.Nyc
{
    public class NycFootprintMatcherTests
    {
        private static List<LotPoint> Square(double x0, double y0, double side) => new List<LotPoint>
        {
            new LotPoint(x0, y0),
            new LotPoint(x0 + side, y0),
            new LotPoint(x0 + side, y0 + side),
            new LotPoint(x0, y0 + side)
        };

        private static NycBuildingFootprint Footprint(string bin, string bbl, List<LotPoint> ring) => new NycBuildingFootprint
        {
            Bin = bin,
            MapplutoBbl = bbl,
            PolygonRings = new List<List<LotPoint>> { ring }
        };

        [Fact]
        public void MatchForLot_BblJoins_ReturnsByBblKind()
        {
            var fp = Footprint("1001", "3021280015", Square(0, 0, 40));
            List<FootprintMatch> matches = NycFootprintMatcher.MatchForLot("3021280015", Square(-5, -5, 50), new[] { fp });

            Assert.Single(matches);
            Assert.Equal(FootprintMatchKind.ByBbl, matches[0].Kind);
        }

        [Fact]
        public void MatchForLot_BblEmptyButCentroidInsideRing_FallsBackSpatially()
        {
            // The footprint's BBL points at a different (or empty) lot, but its
            // centroid sits inside THIS lot's ring — the 438 Concord case.
            var fp = Footprint("1002", "", Square(10, 10, 20));
            List<FootprintMatch> matches = NycFootprintMatcher.MatchForLot("3021280015", Square(0, 0, 50), new[] { fp });

            Assert.Single(matches);
            Assert.Equal(FootprintMatchKind.SpatialFallback, matches[0].Kind);
        }

        [Fact]
        public void MatchForLot_BblEmptyAndCentroidOutsideRing_NoMatch()
        {
            var fp = Footprint("1003", "", Square(1000, 1000, 20));
            List<FootprintMatch> matches = NycFootprintMatcher.MatchForLot("3021280015", Square(0, 0, 50), new[] { fp });

            Assert.Empty(matches);
        }

        [Fact]
        public void MatchForLot_BblMatchTakesPriorityOverSpatialFallback()
        {
            // Even though its centroid also lands inside the lot, a footprint
            // that already joins by BBL is never re-labeled as a fallback.
            var fp = Footprint("1004", "3021280015", Square(0, 0, 40));
            List<FootprintMatch> matches = NycFootprintMatcher.MatchForLot("3021280015", Square(-5, -5, 50), new[] { fp });

            Assert.Equal(FootprintMatchKind.ByBbl, matches[0].Kind);
        }

        [Fact]
        public void MatchForLot_NullOrTooSmallRing_NoSpatialFallbackAttempted()
        {
            var fp = Footprint("1005", "", Square(0, 0, 10));
            List<FootprintMatch> matches = NycFootprintMatcher.MatchForLot("3021280015", null, new[] { fp });

            Assert.Empty(matches);
        }

        [Fact]
        public void MatchForLot_FootprintWithNoPolygon_NeverMatches()
        {
            var fp = new NycBuildingFootprint { Bin = "1006", MapplutoBbl = "3021280015" };
            List<FootprintMatch> matches = NycFootprintMatcher.MatchForLot("3021280015", Square(0, 0, 50), new[] { fp });

            Assert.Empty(matches);
        }
    }
}
