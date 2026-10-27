using System;
using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>
    /// The assessment record knows two numbers, LotFront and LotDepth. For a
    /// regular four-sided lot that is a rectangle laid on the frontage
    /// bearing; for anything else it is meaningless, and this says so by
    /// returning null instead of a shape.
    /// </summary>
    public static class LotRecordedRectangle
    {
        /// <summary>
        /// Rectangle ring aligned with <paramref name="orderedFaces"/>: vertex i
        /// is where face i starts, so the result can be drawn with the same
        /// face payloads as the other sources. Null when the lot is not a
        /// simple quadrilateral or the record has no dimensions.
        /// </summary>
        public static List<LotPoint> Build(IReadOnlyList<NycLotFace> orderedFaces, double frontFt, double depthFt)
        {
            if (orderedFaces == null || orderedFaces.Count != 4 || frontFt <= 0 || depthFt <= 0)
            {
                return null;
            }

            int k = FrontageIndex(orderedFaces, frontFt);
            NycLotFace frontage = orderedFaces[k];
            LotPoint d = frontage.Direction;
            if (Math.Abs(d.X) < 1e-12 && Math.Abs(d.Y) < 1e-12)
            {
                return null;
            }
            // Ring is counter-clockwise, so the interior lies to the LEFT of each face direction.
            var inward = new LotPoint(-d.Y, d.X);
            LotPoint s = frontage.Start;

            var fromFrontage = new List<LotPoint>
            {
                s,
                new LotPoint(s.X + d.X * frontFt, s.Y + d.Y * frontFt),
                new LotPoint(s.X + d.X * frontFt + inward.X * depthFt, s.Y + d.Y * frontFt + inward.Y * depthFt),
                new LotPoint(s.X + inward.X * depthFt, s.Y + inward.Y * depthFt)
            };

            // Rotate so index i lines up with orderedFaces[i].
            var aligned = new List<LotPoint>(4);
            for (int i = 0; i < 4; i++)
            {
                aligned.Add(fromFrontage[((i - k) % 4 + 4) % 4]);
            }
            return aligned;
        }

        /// <summary>
        /// The street face whose length is closest to the recorded frontage;
        /// falls back to any face when no face is flagged as street.
        /// </summary>
        public static int FrontageIndex(IReadOnlyList<NycLotFace> orderedFaces, double frontFt)
        {
            IEnumerable<int> candidates = Enumerable.Range(0, orderedFaces.Count);
            List<int> street = candidates.Where(i => orderedFaces[i].IsStreetFace).ToList();
            if (street.Count > 0)
            {
                candidates = street;
            }
            return candidates
                .OrderBy(i => Math.Abs(Reference(orderedFaces[i]) - frontFt))
                .First();
        }

        private static double Reference(NycLotFace f) => f.LegalLengthFt > 0 ? f.LegalLengthFt : f.MeasuredLengthFt;
    }
}
