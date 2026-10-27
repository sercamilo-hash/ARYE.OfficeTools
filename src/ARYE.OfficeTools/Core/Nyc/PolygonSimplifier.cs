using System;
using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>
    /// Douglas-Peucker polygon simplification. Real GIS footprints (LiDAR or
    /// photogrammetry — Building Footprints, DOF's planimetric survey) carry
    /// sub-foot zigzags that describe a valid outline but turn into a
    /// degenerate or self-intersecting solid once extruded — "the extrusion is
    /// too thin", or the loop won't close. Simplifying first, within a
    /// tolerance appropriate to a massing model rather than survey precision,
    /// is what a "clean volume, few vertices" means. Never changes a ring's
    /// winding and never invents a vertex — it only drops ones that were
    /// already redundant to within the tolerance.
    /// </summary>
    public static class PolygonSimplifier
    {
        /// <summary>
        /// Simplifies a closed ring (implicitly closed: edge i runs to (i+1) %
        /// count). Returns the ring unchanged if it has 3 or fewer points, if
        /// tolerance is non-positive, or if simplifying would collapse it below
        /// a triangle — a simplifier must never leave the caller with nothing.
        /// </summary>
        public static List<LotPoint> Simplify(IReadOnlyList<LotPoint> ring, double toleranceFt)
        {
            if (ring == null)
            {
                return new List<LotPoint>();
            }
            if (ring.Count <= 3 || toleranceFt <= 0)
            {
                return ring.ToList();
            }

            // Close the ring into one open chain (first == last) so a single
            // Douglas-Peucker pass simplifies the seam between the last and
            // first vertex too, not just the interior of the chain.
            var closed = new List<LotPoint>(ring);
            if (!closed[0].IsSame(closed[closed.Count - 1], 1e-6))
            {
                closed.Add(closed[0]);
            }

            List<LotPoint> simplified = Reduce(closed, toleranceFt);

            if (simplified.Count > 1 && simplified[0].IsSame(simplified[simplified.Count - 1], 1e-6))
            {
                simplified.RemoveAt(simplified.Count - 1); // drop the duplicated closing vertex
            }
            return simplified.Count >= 3 ? simplified : ring.ToList();
        }

        private static List<LotPoint> Reduce(List<LotPoint> points, double epsilon)
        {
            if (points.Count < 3)
            {
                return new List<LotPoint>(points);
            }

            double maxDist = 0;
            int splitIndex = 0;
            LotPoint first = points[0];
            LotPoint last = points[points.Count - 1];
            for (int i = 1; i < points.Count - 1; i++)
            {
                double d = PerpendicularDistance(points[i], first, last);
                if (d > maxDist)
                {
                    maxDist = d;
                    splitIndex = i;
                }
            }

            if (maxDist <= epsilon)
            {
                return new List<LotPoint> { first, last };
            }

            List<LotPoint> left = Reduce(points.GetRange(0, splitIndex + 1), epsilon);
            List<LotPoint> right = Reduce(points.GetRange(splitIndex, points.Count - splitIndex), epsilon);

            var result = new List<LotPoint>(left.Count + right.Count - 1);
            result.AddRange(left.Take(left.Count - 1)); // splitIndex is shared — keep it once
            result.AddRange(right);
            return result;
        }

        private static double PerpendicularDistance(LotPoint p, LotPoint a, LotPoint b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12)
            {
                return p.DistanceTo(a);
            }
            double cross = (p.X - a.X) * dy - (p.Y - a.Y) * dx;
            return Math.Abs(cross) / Math.Sqrt(len2);
        }
    }
}
