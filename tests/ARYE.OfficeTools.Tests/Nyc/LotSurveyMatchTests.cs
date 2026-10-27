using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Nyc;
using Xunit;

namespace ARYE.OfficeTools.Tests.Nyc
{
    public class LotSurveyMatchTests
    {
        [Fact]
        public void Inherit_SurveyEqualToLegalRebuild_MatchesAllFourAndInheritsRoles()
        {
            LotFaceChainResult chain = LotFaceChain.Order(ConcordFaces.Shuffled());
            LotReconstructionResult legal = LotReconstruction.FromLegalFaces(chain.Faces);
            // A "survey" that happens to equal the legal rebuild, run the OTHER way round.
            List<LotPoint> reversed = legal.Ring.AsEnumerable().Reverse().ToList();
            List<NycLotFace> survey = LotFaceChain.FromRing(reversed, "2025770009"); // FromRing normalizes to CCW

            int[] matches = LotSurveyMatch.Inherit(survey, chain.Faces);

            Assert.Equal(4, matches.Count(m => m >= 0));
            Assert.Equal(4, matches.Distinct().Count());
            Assert.Equal(2, survey.Count(f => f.IsStreetFace));
            Assert.All(survey, f => Assert.True(f.LegalLengthFt > 0));
            // The 100' legal sides pair with the survey sides that measure 100'.
            foreach (NycLotFace f in survey)
            {
                Assert.InRange(f.MeasuredLengthFt, f.LegalLengthFt - 0.01, f.LegalLengthFt + 0.01);
            }
        }

        [Fact]
        public void Inherit_FaceFarAway_StaysUnmatched()
        {
            LotFaceChainResult chain = LotFaceChain.Order(ConcordFaces.Shuffled());
            var farRing = new List<LotPoint> { new LotPoint(0, 0), new LotPoint(50, 0), new LotPoint(50, 100), new LotPoint(0, 100) };
            List<NycLotFace> survey = LotFaceChain.FromRing(farRing, "x");

            int[] matches = LotSurveyMatch.Inherit(survey, chain.Faces);

            Assert.All(matches, m => Assert.Equal(-1, m));
            Assert.All(survey, f => Assert.False(f.IsStreetFace));
            Assert.All(survey, f => Assert.Equal(0, f.LegalLengthFt));
        }

        [Fact]
        public void Inherit_ExtraSurveyFace_OnlyPairsOnce()
        {
            LotFaceChainResult chain = LotFaceChain.Order(ConcordFaces.Shuffled());
            // Survey with a small jog: 5 faces around the same lot — one face cannot claim a partner twice.
            List<LotPoint> ring = chain.Ring.ToList();
            LotPoint a = ring[1], b = ring[2];
            ring.Insert(2, new LotPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2)); // split one side in two
            List<NycLotFace> survey = LotFaceChain.FromRing(ring, "2025770009");

            int[] matches = LotSurveyMatch.Inherit(survey, chain.Faces);

            Assert.Equal(5, matches.Length);
            Assert.Equal(matches.Where(m => m >= 0).Count(), matches.Where(m => m >= 0).Distinct().Count());
            Assert.True(matches.Count(m => m >= 0) >= 3);
        }
    }
}
