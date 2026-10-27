using System;
using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>
    /// Pairs the faces of a surveyed property line with the tax-map faces of
    /// the same lot, so the survey inherits what the map knows (which sides
    /// front a street, the legal length to compare against) without anybody
    /// re-typing it. A face with no plausible partner stays unmatched — role
    /// Unknown, no legal length — and is reported, never guessed.
    /// </summary>
    public static class LotSurveyMatch
    {
        public const double DefaultMaxMidpointDistanceFt = 20.0;
        public const double DefaultMaxAngleDeg = 10.0;

        /// <summary>
        /// For each survey face, the index of the reference (tax-map) face it
        /// matches, or -1. Matching survey faces receive the reference face's
        /// IsStreetFace and LegalLengthFt; their own path (the survey) is kept.
        /// </summary>
        public static int[] Inherit(IList<NycLotFace> surveyFaces, IReadOnlyList<NycLotFace> referenceFaces,
            double maxMidpointDistanceFt = DefaultMaxMidpointDistanceFt, double maxAngleDeg = DefaultMaxAngleDeg)
        {
            var matches = new int[surveyFaces?.Count ?? 0];
            if (surveyFaces == null || referenceFaces == null)
            {
                return matches;
            }
            double cosLimit = Math.Cos(maxAngleDeg * Math.PI / 180.0);
            var taken = new HashSet<int>();

            for (int i = 0; i < surveyFaces.Count; i++)
            {
                NycLotFace s = surveyFaces[i];
                matches[i] = -1;
                LotPoint sMid = Midpoint(s);
                LotPoint sDir = s.Direction;
                int best = -1;
                double bestScore = double.MaxValue;
                for (int j = 0; j < referenceFaces.Count; j++)
                {
                    if (taken.Contains(j))
                    {
                        continue;
                    }
                    NycLotFace r = referenceFaces[j];
                    LotPoint rDir = r.Direction;
                    // Parallel either way (a survey may run the ring the other direction).
                    double cos = Math.Abs(sDir.X * rDir.X + sDir.Y * rDir.Y);
                    if (cos < cosLimit)
                    {
                        continue;
                    }
                    double dist = Midpoint(r).DistanceTo(sMid);
                    if (dist > maxMidpointDistanceFt)
                    {
                        continue;
                    }
                    // Prefer the closest midpoint, then the closest length.
                    double score = dist + Math.Abs(r.MeasuredLengthFt - s.MeasuredLengthFt) * 0.1;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = j;
                    }
                }
                if (best >= 0)
                {
                    taken.Add(best);
                    matches[i] = best;
                    s.IsStreetFace = referenceFaces[best].IsStreetFace;
                    s.LegalLengthFt = referenceFaces[best].LegalLengthFt;
                    s.LotUuid = referenceFaces[best].LotUuid;
                }
            }
            return matches;
        }

        private static LotPoint Midpoint(NycLotFace f) =>
            new LotPoint((f.Start.X + f.End.X) / 2, (f.Start.Y + f.End.Y) / 2);
    }
}
