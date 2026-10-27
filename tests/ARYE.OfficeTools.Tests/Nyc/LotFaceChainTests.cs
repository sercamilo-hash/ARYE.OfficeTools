using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Nyc;
using Xunit;

namespace ARYE.OfficeTools.Tests.Nyc
{
    /// <summary>
    /// Real DOF TAX_LOT_FACE rows for 438 Concord Ave (BBL 2025770009), fetched
    /// 2026-09-12 from Digital_Tax_Map_VIEW/FeatureServer/4 in EPSG 2263. The
    /// rows are deliberately shuffled and one is reversed, as DOF serves them.
    /// </summary>
    public static class ConcordFaces
    {
        public static readonly LotPoint V0 = new LotPoint(1009522.9191, 234457.3730);
        public static readonly LotPoint V1 = new LotPoint(1009502.5904, 234382.8704);
        public static readonly LotPoint V2 = new LotPoint(1009402.8730, 234409.8868);
        public static readonly LotPoint V3 = new LotPoint(1009423.1469, 234484.4048);

        public static List<NycLotFace> Shuffled() => new List<NycLotFace>
        {
            // E 145th St side, street face, digitized 103.37, legal 100
            Face(V3, V0, 100, 103.369, true),
            // Rear (Wales side interior), digitized 77.23, legal 77.74 — served reversed
            Face(V1, V0, 77.74, 77.226, false),
            // South side shared with lot 8, digitized 103.31, legal 100
            Face(V1, V2, 100, 103.312, false),
            // Concord Ave frontage, street face, digitized 77.23, legal 77.74
            Face(V2, V3, 77.74, 77.227, true)
        };

        private static NycLotFace Face(LotPoint a, LotPoint b, double legal, double digitized, bool street) => new NycLotFace
        {
            Bbl = "2025770009",
            LotUuid = "6c1106f3-c24b-4a8b-8f55-fe1c5642b81b",
            LegalLengthFt = legal,
            DigitizedLengthFt = digitized,
            IsStreetFace = street,
            BearingDeg = LotFaceChain.BearingFromNorth(a, b),
            Path = new List<LotPoint> { a, b }
        };
    }

    public class LotFaceChainTests
    {
        [Fact]
        public void Order_RealConcordFaces_ClosesCounterClockwiseWithFourFaces()
        {
            LotFaceChainResult chain = LotFaceChain.Order(ConcordFaces.Shuffled());

            Assert.True(chain.Succeeded, chain.FailureReason);
            Assert.Equal(4, chain.Faces.Count);
            Assert.True(LotGeometry.SignedArea(chain.Ring) > 0);

            // Every face walks into the next and the last closes on the first.
            for (int i = 0; i < 4; i++)
            {
                Assert.True(chain.Faces[i].End.IsSame(chain.Faces[(i + 1) % 4].Start, LotFaceChain.JoinToleranceFt));
            }
        }

        [Fact]
        public void Order_PreservesLegalLengthsAndStreetFlags()
        {
            LotFaceChainResult chain = LotFaceChain.Order(ConcordFaces.Shuffled());

            Assert.Equal(2, chain.Faces.Count(f => f.IsStreetFace));
            Assert.Equal(2, chain.Faces.Count(f => f.LegalLengthFt == 100));
            Assert.Equal(2, chain.Faces.Count(f => f.LegalLengthFt == 77.74));
            // The street faces are the ones whose digitized length is 103.37 (E 145th) and 77.23 (Concord).
            Assert.Contains(chain.Faces, f => f.IsStreetFace && f.LegalLengthFt == 100);
            Assert.Contains(chain.Faces, f => f.IsStreetFace && f.LegalLengthFt == 77.74);
        }

        [Fact]
        public void Order_MissingFace_FailsWithReason()
        {
            List<NycLotFace> faces = ConcordFaces.Shuffled();
            faces.RemoveAt(0);

            LotFaceChainResult chain = LotFaceChain.Order(faces);

            Assert.False(chain.Succeeded);
            Assert.False(string.IsNullOrEmpty(chain.FailureReason), "a failed chain must say why");
            Assert.Empty(chain.Faces);
        }

        [Fact]
        public void StartAt_RotatesSoFaceZeroBeginsAtNearestCorner()
        {
            LotFaceChainResult chain = LotFaceChain.Order(ConcordFaces.Shuffled());
            List<NycLotFace> rotated = LotFaceChain.StartAt(chain.Faces, ConcordFaces.V2);

            Assert.True(rotated[0].Start.IsSame(ConcordFaces.V2, 0.01));
            Assert.Equal(4, rotated.Count);
        }

        [Fact]
        public void FromRing_BarePolygon_MakesOneUnknownFacePerEdge()
        {
            var ring = new List<LotPoint>
            {
                ConcordFaces.V0, ConcordFaces.V1, ConcordFaces.V2, ConcordFaces.V3, ConcordFaces.V0 // GeoJSON closing duplicate
            };

            List<NycLotFace> faces = LotFaceChain.FromRing(ring, "2025770009");

            Assert.Equal(4, faces.Count);
            Assert.All(faces, f => Assert.Equal(0, f.LegalLengthFt));
            Assert.All(faces, f => Assert.False(f.IsStreetFace));
            Assert.True(LotGeometry.SignedArea(faces.Select(f => f.Start).ToList()) > 0);
        }

        [Theory]
        [InlineData(0, 0, 0, 10, 0)]      // due north
        [InlineData(0, 0, 10, 0, 90)]     // due east
        [InlineData(0, 0, 0, -10, 180)]   // due south
        [InlineData(0, 0, -10, 0, 270)]   // due west
        public void BearingFromNorth_MatchesEsriConvention(double ax, double ay, double bx, double by, double expected)
        {
            Assert.Equal(expected, LotFaceChain.BearingFromNorth(new LotPoint(ax, ay), new LotPoint(bx, by)), 6);
        }
    }
}
