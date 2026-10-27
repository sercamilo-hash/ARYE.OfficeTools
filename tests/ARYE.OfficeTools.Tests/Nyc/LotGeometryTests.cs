using System;
using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Nyc;
using Xunit;

namespace ARYE.OfficeTools.Tests.Nyc
{
    public class LotGeometryTests
    {
        private static List<LotPoint> Square(double side) => new List<LotPoint>
        {
            new LotPoint(0, 0),
            new LotPoint(side, 0),
            new LotPoint(side, side),
            new LotPoint(0, side)
        };

        [Fact]
        public void SignedArea_CcwSquare_IsPositive()
        {
            Assert.True(LotGeometry.SignedArea(Square(10)) > 0);
        }

        [Fact]
        public void SignedArea_CwSquare_IsNegative()
        {
            List<LotPoint> cw = Square(10);
            cw.Reverse();
            Assert.True(LotGeometry.SignedArea(cw) < 0);
        }

        [Fact]
        public void OffsetRingOutward_Square_GrowsBySymmetricAmount()
        {
            List<LotPoint> offset = LotGeometry.OffsetRingOutward(Square(100), 12);

            // A square grown by 12 ft on every side becomes a 124x124 square
            // centered on the same point — every corner should read ±12.
            foreach (LotPoint p in offset)
            {
                Assert.True(Math.Abs(p.X - (-12)) < 1e-6 || Math.Abs(p.X - 112) < 1e-6);
                Assert.True(Math.Abs(p.Y - (-12)) < 1e-6 || Math.Abs(p.Y - 112) < 1e-6);
            }
        }

        [Fact]
        public void OffsetRingOutward_IncreasesArea()
        {
            List<LotPoint> original = Square(50);
            List<LotPoint> offset = LotGeometry.OffsetRingOutward(original, 10);

            double originalArea = Math.Abs(LotGeometry.SignedArea(original));
            double offsetArea = Math.Abs(LotGeometry.SignedArea(offset));
            Assert.True(offsetArea > originalArea);

            // Exact for a square: (50 + 2*10)^2 = 4900.
            Assert.Equal(4900, offsetArea, 3);
        }

        [Fact]
        public void OffsetRingOutward_ClockwiseInput_StillGrowsOutward()
        {
            List<LotPoint> cw = Square(50);
            cw.Reverse();
            List<LotPoint> offset = LotGeometry.OffsetRingOutward(cw, 10);
            double offsetArea = Math.Abs(LotGeometry.SignedArea(offset));
            Assert.Equal(4900, offsetArea, 3);
        }

        [Fact]
        public void OffsetRingOutward_ZeroOffset_ReturnsSameShape()
        {
            List<LotPoint> original = Square(50);
            List<LotPoint> offset = LotGeometry.OffsetRingOutward(original, 0);
            Assert.Equal(original.Count, offset.Count);
        }

        [Fact]
        public void OffsetRingOutward_TooFewPoints_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                LotGeometry.OffsetRingOutward(new List<LotPoint> { new LotPoint(0, 0), new LotPoint(1, 1) }, 5));
        }

        /// <summary>
        /// Regression: real GeoJSON rings repeat the first vertex at the end. Before
        /// the fix, that zero-length closing edge left one corner un-offset and
        /// produced a 12' stub in the sidewalk — the geometry Sergio saw on 438
        /// Concord (BBL 2025770009, real coordinates below).
        /// </summary>
        [Fact]
        public void OffsetRingOutward_ClosedGeoJsonRing_ProducesCleanFourCornerRing()
        {
            var closedRing = new List<LotPoint>
            {
                new LotPoint(1009522.91892529, 234457.372996807),
                new LotPoint(1009502.59055328, 234382.870522976),
                new LotPoint(1009402.8732357, 234409.886860371),
                new LotPoint(1009423.14713955, 234484.404757023),
                new LotPoint(1009522.91892529, 234457.372996807) // closing duplicate
            };

            List<LotPoint> offset = LotGeometry.OffsetRingOutward(closedRing, 12);

            Assert.Equal(4, offset.Count); // no stub vertex, no un-offset corner

            // Lot edges are ~77.23 (frontage) and ~103.31/103.37 (depth); a 12' outward
            // offset grows each by 24. Every edge must land there — nothing short.
            List<double> edges = LotDimensionCheck.EdgeLengths(offset);
            Assert.Equal(4, edges.Count);
            Assert.DoesNotContain(edges, e => e < 100);           // the old bug produced a 12' stub
            Assert.Equal(2, edges.Count(e => Math.Abs(e - 101.23) < 0.2)); // 77.23 + 24
            Assert.Equal(2, edges.Count(e => e > 127.2 && e < 127.5));    // 103.3x + 24

            // Area of the grown lot: (77.23+24)·(103.34+24) ≈ 12,891 sf for this near-rectangle.
            double area = Math.Abs(LotGeometry.SignedArea(offset));
            Assert.InRange(area, 12891 * 0.99, 12891 * 1.01);
        }

        [Fact]
        public void OffsetRingOutward_ClosedRing_MatchesOpenRingResult()
        {
            List<LotPoint> open = Square(50);
            List<LotPoint> closed = Square(50);
            closed.Add(closed[0]);

            double openArea = Math.Abs(LotGeometry.SignedArea(LotGeometry.OffsetRingOutward(open, 10)));
            double closedArea = Math.Abs(LotGeometry.SignedArea(LotGeometry.OffsetRingOutward(closed, 10)));
            Assert.Equal(openArea, closedArea, 6);
        }

        [Theory]
        [InlineData(50, 95, CompassSide.North)]   // near the top edge
        [InlineData(50, 5, CompassSide.South)]    // near the bottom edge
        [InlineData(95, 50, CompassSide.East)]    // near the right edge
        [InlineData(5, 50, CompassSide.West)]     // near the left edge
        public void ClassifySide_PicksNearestEdge(double x, double y, CompassSide expected)
        {
            CompassSide side = LotGeometry.ClassifySide(new LotPoint(x, y), 0, 0, 100, 100);
            Assert.Equal(expected, side);
        }

        [Fact]
        public void Centroid_Square_IsItsMidpoint()
        {
            LotPoint c = LotGeometry.Centroid(Square(100));
            Assert.Equal(50, c.X, 6);
            Assert.Equal(50, c.Y, 6);
        }

        [Fact]
        public void Centroid_LShape_IsPulledTowardTheWiderArm_NotTheVertexAverage()
        {
            // An L: a 20x10 arm plus a 10x10 arm — area-weighted centroid must
            // differ from the plain vertex average, which a building-footprint
            // "is this centroid inside that lot" check cannot afford to get wrong.
            var l = new List<LotPoint>
            {
                new LotPoint(0, 0), new LotPoint(20, 0), new LotPoint(20, 10),
                new LotPoint(10, 10), new LotPoint(10, 20), new LotPoint(0, 20)
            };

            LotPoint centroid = LotGeometry.Centroid(l);
            double vertexAvgX = l.Average(p => p.X);

            Assert.NotEqual(vertexAvgX, centroid.X, 3);
            Assert.True(LotGeometry.PointInPolygon(centroid, l), "the centroid of an L must land inside the L");
        }

        [Fact]
        public void Centroid_DegenerateRing_FallsBackToVertexAverage_NeverNaN()
        {
            var line = new List<LotPoint> { new LotPoint(0, 0), new LotPoint(10, 0), new LotPoint(5, 0) };

            LotPoint c = LotGeometry.Centroid(line);

            Assert.False(double.IsNaN(c.X));
            Assert.False(double.IsNaN(c.Y));
        }

        [Fact]
        public void PointInPolygon_InsideAndOutside_Square()
        {
            List<LotPoint> square = Square(100);

            Assert.True(LotGeometry.PointInPolygon(new LotPoint(50, 50), square));
            Assert.False(LotGeometry.PointInPolygon(new LotPoint(150, 50), square));
            Assert.False(LotGeometry.PointInPolygon(new LotPoint(-1, 50), square));
        }

        [Fact]
        public void PointInPolygon_LShape_RespectsTheNotch()
        {
            // The L from the centroid test: (15,15) sits in the notch that was
            // cut away, so it must read as outside even though it is well
            // within the L's bounding box.
            var l = new List<LotPoint>
            {
                new LotPoint(0, 0), new LotPoint(20, 0), new LotPoint(20, 10),
                new LotPoint(10, 10), new LotPoint(10, 20), new LotPoint(0, 20)
            };

            Assert.True(LotGeometry.PointInPolygon(new LotPoint(5, 5), l));
            Assert.False(LotGeometry.PointInPolygon(new LotPoint(15, 15), l));
        }
    }
}
