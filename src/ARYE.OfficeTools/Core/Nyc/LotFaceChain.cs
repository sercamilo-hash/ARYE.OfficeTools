using System;
using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>Outcome of ordering the faces of one lot into a closed ring.</summary>
    public sealed class LotFaceChainResult
    {
        /// <summary>Faces in counter-clockwise order, each walking into the next. Empty when it failed.</summary>
        public List<NycLotFace> Faces { get; } = new List<NycLotFace>();

        public bool IsClosed { get; set; }

        /// <summary>Why the ring could not be built. Empty on success.</summary>
        public string FailureReason { get; set; } = string.Empty;

        public bool Succeeded => IsClosed && Faces.Count >= 3 && FailureReason.Length == 0;

        /// <summary>Start vertex of every face, in order — the polygon these faces enclose.</summary>
        public List<LotPoint> Ring => Faces.Select(f => f.Start).ToList();
    }

    /// <summary>
    /// DOF publishes a lot's sides as independent polylines with no order and
    /// arbitrary direction. This chains them end-to-start into one closed,
    /// counter-clockwise ring, so face index 0..n-1 means the same thing to
    /// BauTools, to the drawn Revit lines and to any other add-in reading them.
    /// </summary>
    public static class LotFaceChain
    {
        /// <summary>Endpoint match tolerance. DOF vertices are shared exactly; this absorbs float noise.</summary>
        public const double JoinToleranceFt = 0.05;

        public static LotFaceChainResult Order(IEnumerable<NycLotFace> faces)
        {
            var result = new LotFaceChainResult();
            List<NycLotFace> pool = faces
                .Where(f => f != null && f.Path.Count >= 2 && f.MeasuredLengthFt > JoinToleranceFt)
                .Select(f => f.Clone())
                .ToList();

            if (pool.Count < 3)
            {
                result.FailureReason = $"only {pool.Count} usable face(s) — a lot needs at least 3";
                return result;
            }

            // Start from the face whose start vertex is the lowest-left, so the
            // ordering is deterministic regardless of how DOF served the rows.
            NycLotFace first = pool
                .OrderBy(f => Math.Min(f.Start.Y, f.End.Y))
                .ThenBy(f => Math.Min(f.Start.X, f.End.X))
                .First();
            pool.Remove(first);
            var chain = new List<NycLotFace> { first };

            while (pool.Count > 0)
            {
                LotPoint tail = chain[chain.Count - 1].End;
                NycLotFace next = pool.FirstOrDefault(f => f.Start.IsSame(tail, JoinToleranceFt));
                if (next != null)
                {
                    pool.Remove(next);
                    chain.Add(next);
                    continue;
                }
                NycLotFace reversed = pool.FirstOrDefault(f => f.End.IsSame(tail, JoinToleranceFt));
                if (reversed != null)
                {
                    pool.Remove(reversed);
                    chain.Add(reversed.Reversed());
                    continue;
                }
                result.FailureReason =
                    $"faces do not connect: no face starts at {tail} (after {chain.Count} of {chain.Count + pool.Count})";
                return result;
            }

            bool closed = chain[chain.Count - 1].End.IsSame(chain[0].Start, JoinToleranceFt);
            if (!closed)
            {
                result.FailureReason =
                    $"chain does not close: last face ends at {chain[chain.Count - 1].End}, first starts at {chain[0].Start}";
                return result;
            }

            // Normalize to counter-clockwise so "outward" and "next" mean one thing everywhere.
            List<LotPoint> ring = chain.Select(f => f.Start).ToList();
            if (LotGeometry.SignedArea(ring) < 0)
            {
                chain.Reverse();
                for (int i = 0; i < chain.Count; i++)
                {
                    chain[i] = chain[i].Reversed();
                }
            }

            result.Faces.AddRange(chain);
            result.IsClosed = true;
            return result;
        }

        /// <summary>
        /// Rotates the ordered faces so the one starting nearest <paramref name="anchor"/>
        /// becomes index 0. The anchor is where the user wants the lot to land in
        /// the model, so face 0 always begins at that corner.
        /// </summary>
        public static List<NycLotFace> StartAt(IReadOnlyList<NycLotFace> orderedFaces, LotPoint anchor)
        {
            if (orderedFaces.Count == 0)
            {
                return new List<NycLotFace>();
            }
            int best = 0;
            double bestDist = double.MaxValue;
            for (int i = 0; i < orderedFaces.Count; i++)
            {
                double d = orderedFaces[i].Start.DistanceTo(anchor);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            var rotated = new List<NycLotFace>(orderedFaces.Count);
            for (int i = 0; i < orderedFaces.Count; i++)
            {
                rotated.Add(orderedFaces[(best + i) % orderedFaces.Count]);
            }
            return rotated;
        }

        /// <summary>
        /// Builds faces from a bare polygon when DOF has no TAX_LOT_FACE rows for
        /// the lot: one face per edge, no legal length, street flag unknown.
        /// </summary>
        public static List<NycLotFace> FromRing(IReadOnlyList<LotPoint> ring, string bbl)
        {
            var faces = new List<NycLotFace>();
            if (ring == null || ring.Count < 3)
            {
                return faces;
            }
            List<LotPoint> open = ring.ToList();
            if (open.Count > 3 && open[0].IsSame(open[open.Count - 1], JoinToleranceFt))
            {
                open.RemoveAt(open.Count - 1);
            }
            if (LotGeometry.SignedArea(open) < 0)
            {
                open.Reverse();
            }
            for (int i = 0; i < open.Count; i++)
            {
                LotPoint a = open[i];
                LotPoint b = open[(i + 1) % open.Count];
                if (a.DistanceTo(b) <= JoinToleranceFt)
                {
                    continue;
                }
                faces.Add(new NycLotFace
                {
                    Bbl = bbl,
                    Path = new List<LotPoint> { a, b },
                    DigitizedLengthFt = a.DistanceTo(b),
                    LegalLengthFt = 0,
                    IsStreetFace = false,
                    BearingDeg = BearingFromNorth(a, b)
                });
            }
            return faces;
        }

        /// <summary>Degrees clockwise from north, matching DOF's 'Direction' attribute.</summary>
        public static double BearingFromNorth(LotPoint a, LotPoint b)
        {
            double deg = Math.Atan2(b.X - a.X, b.Y - a.Y) * 180.0 / Math.PI;
            return deg < 0 ? deg + 360.0 : deg;
        }
    }
}
