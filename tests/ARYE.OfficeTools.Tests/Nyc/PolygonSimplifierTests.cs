using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Nyc;
using Xunit;

namespace ARYE.OfficeTools.Tests.Nyc
{
    public class PolygonSimplifierTests
    {
        [Fact]
        public void Simplify_MidEdgeNoise_RemovesPointsCloseToTheLine()
        {
            // A clean 10x10 square, but the bottom edge has three extra points
            // wandering less than a foot off the straight line — exactly the
            // kind of survey/photogrammetry zigzag that makes a real footprint
            // too "thin" to extrude.
            var ring = new List<LotPoint>
            {
                new(0, 0),
                new(2.5, 0.2),
                new(5, -0.15),
                new(7.5, 0.1),
                new(10, 0),
                new(10, 10),
                new(0, 10)
            };

            List<LotPoint> simplified = PolygonSimplifier.Simplify(ring, toleranceFt: 1.0);

            Assert.Equal(4, simplified.Count); // the noisy bottom edge collapses to its two endpoints
            Assert.Contains(simplified, p => p.IsSame(new LotPoint(0, 0), 1e-6));
            Assert.Contains(simplified, p => p.IsSame(new LotPoint(10, 0), 1e-6));
            Assert.Contains(simplified, p => p.IsSame(new LotPoint(10, 10), 1e-6));
            Assert.Contains(simplified, p => p.IsSame(new LotPoint(0, 10), 1e-6));
        }

        [Fact]
        public void Simplify_RealCorner_IsNeverRemoved()
        {
            // An L-shaped footprint — the inner corner is a real architectural
            // feature (a setback), not noise, and must survive simplification
            // even at a generous tolerance.
            var ring = new List<LotPoint>
            {
                new(0, 0), new(20, 0), new(20, 10), new(10, 10), new(10, 20), new(0, 20)
            };

            List<LotPoint> simplified = PolygonSimplifier.Simplify(ring, toleranceFt: 2.0);

            Assert.Equal(6, simplified.Count);
            Assert.Contains(simplified, p => p.IsSame(new LotPoint(10, 10), 1e-6));
        }

        [Fact]
        public void Simplify_ZeroOrNegativeTolerance_ReturnsRingUnchanged()
        {
            var ring = new List<LotPoint> { new(0, 0), new(5, 0.1), new(10, 0), new(10, 10), new(0, 10) };

            Assert.Equal(ring.Count, PolygonSimplifier.Simplify(ring, 0).Count);
            Assert.Equal(ring.Count, PolygonSimplifier.Simplify(ring, -1).Count);
        }

        [Fact]
        public void Simplify_TriangleOrFewer_ReturnsUnchanged()
        {
            var triangle = new List<LotPoint> { new(0, 0), new(10, 0), new(5, 10) };

            List<LotPoint> simplified = PolygonSimplifier.Simplify(triangle, toleranceFt: 5.0);

            Assert.Equal(3, simplified.Count);
        }

        [Fact]
        public void Simplify_NeverCollapsesBelowATriangle()
        {
            // A tolerance so large it would flatten everything to a line —
            // the simplifier must fall back to the original ring rather than
            // hand the caller a degenerate 1- or 2-point result.
            var ring = new List<LotPoint> { new(0, 0), new(1, 0.01), new(2, 0), new(2, 2), new(0, 2) };

            List<LotPoint> simplified = PolygonSimplifier.Simplify(ring, toleranceFt: 1000);

            Assert.True(simplified.Count >= 3);
        }

        [Fact]
        public void Simplify_NullRing_ReturnsEmptyList()
        {
            Assert.Empty(PolygonSimplifier.Simplify(null, 1.0));
        }

        [Fact]
        public void Simplify_PreservesWinding()
        {
            var ccw = new List<LotPoint> { new(0, 0), new(10, 0.1), new(10, 10), new(0, 10) };

            List<LotPoint> simplified = PolygonSimplifier.Simplify(ccw, toleranceFt: 1.0);

            double signedArea = 0;
            for (int i = 0; i < simplified.Count; i++)
            {
                LotPoint a = simplified[i], b = simplified[(i + 1) % simplified.Count];
                signedArea += a.X * b.Y - b.X * a.Y;
            }
            Assert.True(signedArea > 0, "winding must stay counter-clockwise");
        }
    }
}
