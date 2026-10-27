using System;
using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>
    /// How far apart two independently-built lot rings are, where they actually
    /// run near each other. Used when a neighbor lot is rebuilt from its own
    /// legal tax-map lengths: each lot closes on its own, so two legal
    /// neighbors are not guaranteed to share an edge exactly the way two DTM
    /// polygons do. This measures the real gap instead of assuming one.
    /// </summary>
    public static class LotSeamMeasurement
    {
        /// <summary>
        /// Farthest a vertex of <paramref name="ring"/> sits from
        /// <paramref name="referenceRing"/>, counting only vertices that fall
        /// within <paramref name="proximityFt"/> of the reference — i.e. only
        /// the shared boundary, not the lot's far corners. Null when no vertex
        /// of either ring comes near the other (the two lots do not touch).
        /// </summary>
        public static double? MaxGapToReference(
            IReadOnlyList<LotPoint> ring, IReadOnlyList<LotPoint> referenceRing, double proximityFt = 15.0)
        {
            if (ring == null || ring.Count < 2 || referenceRing == null || referenceRing.Count < 2)
            {
                return null;
            }

            double max = -1;
            bool any = false;
            foreach (LotPoint p in ring)
            {
                double d = DistanceToRing(p, referenceRing);
                if (d <= proximityFt)
                {
                    any = true;
                    if (d > max)
                    {
                        max = d;
                    }
                }
            }
            return any ? (double?)max : null;
        }

        private static double DistanceToRing(LotPoint p, IReadOnlyList<LotPoint> ring)
        {
            double best = double.MaxValue;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                double d = DistanceToSegment(p, ring[i], ring[(i + 1) % n]);
                if (d < best)
                {
                    best = d;
                }
            }
            return best;
        }

        private static double DistanceToSegment(LotPoint p, LotPoint a, LotPoint b)
        {
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double wx = p.X - a.X, wy = p.Y - a.Y;
            double len2 = vx * vx + vy * vy;
            double t = len2 < 1e-12 ? 0.0 : Math.Max(0.0, Math.Min(1.0, (wx * vx + wy * vy) / len2));
            double cx = a.X + t * vx, cy = a.Y + t * vy;
            double dx = p.X - cx, dy = p.Y - cy;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
