using System;
using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Nyc
{
    public enum CompassSide
    {
        North = 0,
        South = 1,
        East = 2,
        West = 3
    }

    /// <summary>
    /// Pure 2D polygon math for the lot boundary tool: a real outward offset
    /// (the sidewalk, grown from the lot's own frontage instead of a bounding
    /// box) and cardinal-side classification (which street names attach to
    /// which side of the block).
    /// </summary>
    public static class LotGeometry
    {
        /// <summary>Positive for a counter-clockwise ring, negative for clockwise.</summary>
        public static double SignedArea(IReadOnlyList<LotPoint> ring)
        {
            double sum = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                LotPoint a = ring[i];
                LotPoint b = ring[(i + 1) % ring.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2.0;
        }

        /// <summary>
        /// True area-weighted centroid (the shoelace formula's centroid, not a
        /// plain vertex average — the two differ for any non-regular polygon,
        /// e.g. an L-shaped footprint). Falls back to the vertex average for a
        /// degenerate (zero-area) ring, which a plain building outline should
        /// never be, but a caller must never get a NaN back.
        /// </summary>
        public static LotPoint Centroid(IReadOnlyList<LotPoint> ring)
        {
            double area6 = 0, cx = 0, cy = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                LotPoint a = ring[i];
                LotPoint b = ring[(i + 1) % ring.Count];
                double cross = a.X * b.Y - b.X * a.Y;
                area6 += cross;
                cx += (a.X + b.X) * cross;
                cy += (a.Y + b.Y) * cross;
            }
            if (Math.Abs(area6) < 1e-9)
            {
                double avgX = 0, avgY = 0;
                foreach (LotPoint p in ring) { avgX += p.X; avgY += p.Y; }
                return new LotPoint(avgX / ring.Count, avgY / ring.Count);
            }
            double factor = 1.0 / (3.0 * area6);
            return new LotPoint(cx * factor, cy * factor);
        }

        /// <summary>
        /// Ray-casting point-in-polygon test (even-odd rule). A point exactly
        /// on an edge may go either way — acceptable here, where it decides
        /// "is this building's centroid inside that lot", never a legal boundary.
        /// </summary>
        public static bool PointInPolygon(LotPoint point, IReadOnlyList<LotPoint> ring)
        {
            bool inside = false;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                LotPoint pi = ring[i], pj = ring[j];
                bool crosses = (pi.Y > point.Y) != (pj.Y > point.Y);
                if (crosses)
                {
                    double xIntersect = pi.X + (point.Y - pi.Y) / (pj.Y - pi.Y) * (pj.X - pi.X);
                    if (point.X < xIntersect)
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

        /// <summary>
        /// Grows a simple polygon outward by offsetFt: each edge is pushed out
        /// along its outward normal, and new vertices are the intersections of
        /// consecutive offset edges. This is what makes the sidewalk follow the
        /// lot's real frontage instead of a single bounding-box rectangle.
        /// </summary>
        public static List<LotPoint> OffsetRingOutward(IReadOnlyList<LotPoint> ring, double offsetFt)
        {
            if (ring == null || ring.Count < 3)
            {
                throw new ArgumentException("A ring needs at least 3 points.", nameof(ring));
            }
            if (offsetFt <= 0)
            {
                return ring.ToList();
            }

            // Defensive: a GeoJSON-style closed ring (last == first) would add a
            // zero-length edge, whose "corner" can't be mitered and leaves one
            // vertex un-offset plus a stub. Never trust the caller to have opened it.
            List<LotPoint> open = ring.ToList();
            if (open.Count >= 2
                && Math.Abs(open[0].X - open[open.Count - 1].X) < 1e-6
                && Math.Abs(open[0].Y - open[open.Count - 1].Y) < 1e-6)
            {
                open.RemoveAt(open.Count - 1);
            }
            if (open.Count < 3)
            {
                throw new ArgumentException("A ring needs at least 3 distinct points.", nameof(ring));
            }

            // Work counter-clockwise so "outward normal" is a single consistent formula.
            List<LotPoint> ccw = SignedArea(open) < 0 ? open.AsEnumerable().Reverse().ToList() : open;
            int n = ccw.Count;

            var offsetEdges = new (LotPoint P0, LotPoint P1)[n];
            for (int i = 0; i < n; i++)
            {
                LotPoint a = ccw[i];
                LotPoint b = ccw[(i + 1) % n];
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9)
                {
                    offsetEdges[i] = (a, b);
                    continue;
                }
                // Outward normal for a CCW ring is the edge direction rotated -90°.
                double nx = dy / len;
                double ny = -dx / len;
                offsetEdges[i] = (
                    new LotPoint(a.X + nx * offsetFt, a.Y + ny * offsetFt),
                    new LotPoint(b.X + nx * offsetFt, b.Y + ny * offsetFt));
            }

            var result = new List<LotPoint>(n);
            for (int i = 0; i < n; i++)
            {
                (LotPoint P0, LotPoint P1) prev = offsetEdges[(i - 1 + n) % n];
                (LotPoint P0, LotPoint P1) curr = offsetEdges[i];
                LotPoint corner = IntersectLines(prev.P0, prev.P1, curr.P0, curr.P1)
                                  ?? curr.P0; // parallel edges: no miter, just butt the offset edge
                result.Add(corner);
            }
            return result;
        }

        private static LotPoint? IntersectLines(LotPoint a1, LotPoint a2, LotPoint b1, LotPoint b2)
        {
            double d1x = a2.X - a1.X, d1y = a2.Y - a1.Y;
            double d2x = b2.X - b1.X, d2y = b2.Y - b1.Y;
            double denom = d1x * d2y - d1y * d2x;
            if (Math.Abs(denom) < 1e-9)
            {
                return null;
            }
            double t = ((b1.X - a1.X) * d2y - (b1.Y - a1.Y) * d2x) / denom;
            return new LotPoint(a1.X + t * d1x, a1.Y + t * d1y);
        }

        /// <summary>
        /// Which side of a bounding box a point is closest to, as a fraction of
        /// the box's own width/height (scale-independent) — the assist used to
        /// decide which cardinal street a block-edge lot's address belongs to.
        /// </summary>
        public static CompassSide ClassifySide(LotPoint point, double minX, double minY, double maxX, double maxY)
        {
            double width = Math.Max(maxX - minX, 1e-6);
            double height = Math.Max(maxY - minY, 1e-6);
            double distNorth = (maxY - point.Y) / height;
            double distSouth = (point.Y - minY) / height;
            double distEast = (maxX - point.X) / width;
            double distWest = (point.X - minX) / width;

            double min = new[] { distNorth, distSouth, distEast, distWest }.Min();
            if (min == distNorth) return CompassSide.North;
            if (min == distSouth) return CompassSide.South;
            if (min == distEast) return CompassSide.East;
            return CompassSide.West;
        }
    }
}
