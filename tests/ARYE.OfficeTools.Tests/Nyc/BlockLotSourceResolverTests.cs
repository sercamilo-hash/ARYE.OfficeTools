using System.Collections.Generic;
using ARYE.OfficeTools.Core.Nyc;
using Xunit;

namespace ARYE.OfficeTools.Tests.Nyc
{
    public class BlockLotSourceResolverTests
    {
        private static NycLotFace Face(LotPoint a, LotPoint b, double legal, bool street = false) => new NycLotFace
        {
            Bbl = "1111110001",
            LegalLengthFt = legal,
            DigitizedLengthFt = a.DistanceTo(b),
            IsStreetFace = street,
            BearingDeg = LotFaceChain.BearingFromNorth(a, b),
            Path = new List<LotPoint> { a, b }
        };

        // Clean 50x100 rectangle, all four sides with a legal length, closes exactly.
        private static List<NycLotFace> CleanRectFaces(double x0 = 50, double x1 = 100, double y0 = 0, double y1 = 100, double legalW = 50, double legalH = 100)
        {
            var v0 = new LotPoint(x0, y0);
            var v1 = new LotPoint(x1, y0);
            var v2 = new LotPoint(x1, y1);
            var v3 = new LotPoint(x0, y1);
            return new List<NycLotFace>
            {
                Face(v0, v1, legalW, true),
                Face(v1, v2, legalH),
                Face(v2, v3, legalW),
                Face(v3, v0, legalH)
            };
        }

        private static NycLotInfo Lot(string bbl, bool adjacent, List<LotPoint> dtmRing) => new NycLotInfo
        {
            Bbl = bbl,
            IsAdjacent = adjacent,
            PolygonRings = new List<List<LotPoint>> { dtmRing }
        };

        [Fact]
        public void Resolve_CleanLegalFaces_SuggestsLegalAndFillsRing()
        {
            List<NycLotFace> faces = CleanRectFaces();
            List<LotPoint> dtmRing = new() { new(50, 0), new(100, 0), new(100, 100), new(50, 100) };
            NycLotInfo lot = Lot("1111110001", true, dtmRing);

            BlockLotSourceResolution res = BlockLotSourceResolver.Resolve(lot, faces, subjectLegalReference: null);

            Assert.True(res.LegalAvailable, res.LegalUnavailableReason);
            Assert.Equal(LotSourceChoice.Legal, res.Suggested);
            Assert.Equal(0, res.MissingLegalFaceCount);
            Assert.Equal(4, res.LegalRing.Count);
            Assert.Empty(res.LegalUnavailableReason);
        }

        [Fact]
        public void Resolve_HalfFacesMissingLegal_FallsBackToDtmWithReason()
        {
            List<NycLotFace> faces = CleanRectFaces();
            faces[1].LegalLengthFt = 0; // 2 of 4 faces now have no legal length
            faces[3].LegalLengthFt = 0;
            List<LotPoint> dtmRing = new() { new(50, 0), new(100, 0), new(100, 100), new(50, 100) };
            NycLotInfo lot = Lot("1111110001", false, dtmRing);

            BlockLotSourceResolution res = BlockLotSourceResolver.Resolve(lot, faces, subjectLegalReference: null);

            Assert.False(res.LegalAvailable);
            Assert.Equal(LotSourceChoice.Dtm, res.Suggested);
            Assert.Equal(2, res.MissingLegalFaceCount);
            Assert.Contains("2 of 4", res.LegalUnavailableReason);
        }

        [Fact]
        public void Resolve_GrossMisclosure_FallsBackToDtmWithReconstructionReason()
        {
            // Digitized as a rectangle (bearings 0/90/180/270), but the legal lengths
            // on opposite sides disagree (40 vs 100, 100 vs 60) — no rectangle has
            // those four side lengths, so the traverse cannot close within 1:50.
            var v0 = new LotPoint(50, 0);
            var v1 = new LotPoint(100, 0);
            var v2 = new LotPoint(100, 100);
            var v3 = new LotPoint(50, 100);
            List<NycLotFace> faces = new()
            {
                Face(v0, v1, legal: 40, street: true),
                Face(v1, v2, legal: 100),
                Face(v2, v3, legal: 100),
                Face(v3, v0, legal: 60)
            };
            List<LotPoint> dtmRing = new() { v0, v1, v2, v3 };
            NycLotInfo lot = Lot("1111110001", false, dtmRing);

            BlockLotSourceResolution res = BlockLotSourceResolver.Resolve(lot, faces, subjectLegalReference: null);

            Assert.False(res.LegalAvailable);
            Assert.Equal(LotSourceChoice.Dtm, res.Suggested);
            Assert.NotNull(res.Reconstruction);
            Assert.False(res.Reconstruction.Succeeded);
            Assert.NotEmpty(res.LegalUnavailableReason);
        }

        [Fact]
        public void Resolve_NoFacesPublished_FallsBackToDtmWithReason()
        {
            List<LotPoint> dtmRing = new() { new(50, 0), new(100, 0), new(100, 100), new(50, 100) };
            NycLotInfo lot = Lot("1111110001", false, dtmRing);

            BlockLotSourceResolution res = BlockLotSourceResolver.Resolve(lot, new List<NycLotFace>(), subjectLegalReference: null);

            Assert.False(res.LegalAvailable);
            Assert.Equal(LotSourceChoice.Dtm, res.Suggested);
            Assert.Equal("no tax-map faces published for this lot", res.LegalUnavailableReason);
        }

        [Fact]
        public void Resolve_NoFacesAndNoDtmRing_SuggestsNone()
        {
            NycLotInfo lot = Lot("1111110001", false, new List<LotPoint>());

            BlockLotSourceResolution res = BlockLotSourceResolver.Resolve(lot, new List<NycLotFace>(), subjectLegalReference: null);

            Assert.Equal(LotSourceChoice.None, res.Suggested);
        }

        [Fact]
        public void Resolve_AdjacentLotSharingEdge_MeasuresSeamAgainstSubject()
        {
            // Subject's legal reference ring: 0..50 x 0..100.
            List<LotPoint> subjectLegal = new() { new(0, 0), new(50, 0), new(50, 100), new(0, 100) };
            // Neighbor's own legal rebuild starts 0.2 ft further out along the shared edge (x=50.2).
            List<NycLotFace> faces = CleanRectFaces(x0: 50.2, x1: 100.2);
            List<LotPoint> dtmRing = new() { new(50, 0), new(100, 0), new(100, 100), new(50, 100) };
            NycLotInfo lot = Lot("1111110002", true, dtmRing);

            BlockLotSourceResolution res = BlockLotSourceResolver.Resolve(lot, faces, subjectLegal);

            Assert.True(res.LegalAvailable);
            Assert.NotNull(res.SeamToSubjectFt);
            Assert.InRange(res.SeamToSubjectFt!.Value, 0.15, 0.25);
        }

        [Fact]
        public void SuggestAll_FillsOnlyUnchosenLots()
        {
            var already = new BlockLotSourceResolution { Suggested = LotSourceChoice.Legal, Chosen = LotSourceChoice.Dtm };
            var untouched = new BlockLotSourceResolution { Suggested = LotSourceChoice.Legal, Chosen = LotSourceChoice.None };

            BlockLotSourceResolver.SuggestAll(new[] { already, untouched });

            Assert.Equal(LotSourceChoice.Dtm, already.Chosen);   // user's pick is never overridden
            Assert.Equal(LotSourceChoice.Legal, untouched.Chosen);
        }
    }
}
