using System;
using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Nyc;
using Xunit;

namespace ARYE.OfficeTools.Tests.Nyc
{
    public class LotReconstructionTests
    {
        private static List<double> Edges(LotReconstructionResult r) => LotDimensionCheck.EdgeLengths(r.Ring);

        [Fact]
        public void FromLegalFaces_Concord_RebuildsLegal77_74By100Exactly()
        {
            LotFaceChainResult chain = LotFaceChain.Order(ConcordFaces.Shuffled());
            LotReconstructionResult rebuilt = LotReconstruction.FromLegalFaces(chain.Faces);

            Assert.True(rebuilt.Succeeded, rebuilt.FailureReason);
            Assert.Equal(4, rebuilt.Ring.Count);
            Assert.True(rebuilt.ClosureErrorFt < 0.1, $"closure {rebuilt.ClosureErrorFt}");

            // Drawn lengths ARE the legal lengths — that is the whole point of this source.
            List<double> edges = Edges(rebuilt);
            Assert.Equal(2, edges.Count(e => Math.Abs(e - 77.74) < 1e-6));
            Assert.Equal(2, edges.Count(e => Math.Abs(e - 100.0) < 1e-6));
            Assert.InRange(rebuilt.AreaSqFt, 7774 * 0.999, 7774 * 1.001);
            Assert.True(rebuilt.MaxBearingChangeDeg < 0.05, $"bearings moved {rebuilt.MaxBearingChangeDeg}°");
        }

        [Fact]
        public void FromLegalFaces_StartsAtFaceZeroStart_SoAnchorIsHonored()
        {
            LotFaceChainResult chain = LotFaceChain.Order(ConcordFaces.Shuffled());
            List<NycLotFace> rotated = LotFaceChain.StartAt(chain.Faces, ConcordFaces.V2);
            LotReconstructionResult rebuilt = LotReconstruction.FromLegalFaces(rotated);

            Assert.True(rebuilt.Ring[0].IsSame(ConcordFaces.V2, 1e-9));
        }

        [Fact]
        public void FromLegalFaces_NoLegalLengths_KeepsDigitizedWithWarning()
        {
            var ring = new List<LotPoint> { new LotPoint(0, 0), new LotPoint(50, 0), new LotPoint(50, 100), new LotPoint(0, 100) };
            List<NycLotFace> faces = LotFaceChain.FromRing(ring, "x");

            LotReconstructionResult rebuilt = LotReconstruction.FromLegalFaces(faces);

            Assert.True(rebuilt.Succeeded);
            Assert.Equal(4, rebuilt.Warnings.Count(w => w.Contains("no legal length")));
            Assert.InRange(rebuilt.AreaSqFt, 4999.9, 5000.1);
        }

        /// <summary>
        /// The bug Sergio caught on 485 Fulton (3001520006): Bowditch spread a
        /// misclosure over every face, so a legal 107.5' came out 107.09'.
        /// Legal lengths must survive the closure EXACTLY; bearings absorb it.
        /// </summary>
        [Fact]
        public void FromLegalFaces_Misclosure_KeepsEveryLegalLengthExact_BearingsAbsorbIt()
        {
            var ring = new List<LotPoint> { new LotPoint(0, 0), new LotPoint(50, 0), new LotPoint(50, 100), new LotPoint(0, 100) };
            List<NycLotFace> faces = LotFaceChain.FromRing(ring, "x");
            faces[0].LegalLengthFt = 50; faces[1].LegalLengthFt = 104; faces[2].LegalLengthFt = 50; faces[3].LegalLengthFt = 100;

            LotReconstructionResult rebuilt = LotReconstruction.FromLegalFaces(faces);

            Assert.True(rebuilt.Succeeded, rebuilt.FailureReason);
            Assert.InRange(rebuilt.ClosureErrorFt, 3.99, 4.01);
            List<double> edges = Edges(rebuilt);
            Assert.Equal(4, edges.Count);
            Assert.Equal(50, edges[0], 6);
            Assert.Equal(104, edges[1], 6);
            Assert.Equal(50, edges[2], 6);
            Assert.Equal(100, edges[3], 6);
            // The closing face (index 3) runs from the last vertex back to the start: exactly 100.
            Assert.Equal(100, rebuilt.Ring[3].DistanceTo(rebuilt.Ring[0]), 6);
            Assert.True(rebuilt.MaxBearingChangeDeg > 0.5 && rebuilt.MaxBearingChangeDeg < 3, $"bearings moved {rebuilt.MaxBearingChangeDeg}°");
            Assert.Contains(rebuilt.Warnings, w => w.Contains("rotating faces"));
        }

        [Fact]
        public void FromLegalFaces_FaceWithoutLegalLength_TakesTheMisclosureAsLength()
        {
            // Legal 50 / 104 / 50 and a fourth face with no tax-map dimension (digitized 100):
            // the 4 ft gap lies exactly along that free face, so it should stretch to 104
            // and the bearings should barely move.
            var ring = new List<LotPoint> { new LotPoint(0, 0), new LotPoint(50, 0), new LotPoint(50, 100), new LotPoint(0, 100) };
            List<NycLotFace> faces = LotFaceChain.FromRing(ring, "x");
            faces[0].LegalLengthFt = 50; faces[1].LegalLengthFt = 104; faces[2].LegalLengthFt = 50; faces[3].LegalLengthFt = 0;

            LotReconstructionResult rebuilt = LotReconstruction.FromLegalFaces(faces);

            Assert.True(rebuilt.Succeeded, rebuilt.FailureReason);
            List<double> edges = Edges(rebuilt);
            Assert.Equal(50, edges[0], 6);
            Assert.Equal(104, edges[1], 6);
            Assert.Equal(50, edges[2], 6);
            Assert.InRange(edges[3], 103.9, 104.1);
            Assert.True(rebuilt.MaxBearingChangeDeg < 0.1, $"bearings moved {rebuilt.MaxBearingChangeDeg}°");
        }

        [Fact]
        public void FromLegalFaces_GrossMisclosure_FailsInsteadOfInventingAShape()
        {
            var ring = new List<LotPoint> { new LotPoint(0, 0), new LotPoint(50, 0), new LotPoint(50, 100), new LotPoint(0, 100) };
            List<NycLotFace> faces = LotFaceChain.FromRing(ring, "x");
            faces[0].LegalLengthFt = 50; faces[1].LegalLengthFt = 150; faces[2].LegalLengthFt = 50; faces[3].LegalLengthFt = 100;

            LotReconstructionResult rebuilt = LotReconstruction.FromLegalFaces(faces);

            Assert.False(rebuilt.Succeeded);
            Assert.Contains("misclosure", rebuilt.FailureReason);
            Assert.Empty(rebuilt.Ring);
        }
    }

    public class LotFaceComparisonTests
    {
        [Fact]
        public void Compare_Concord_FlagsTheTwo100FootSidesAsDiscrepant()
        {
            LotFaceChainResult chain = LotFaceChain.Order(ConcordFaces.Shuffled());
            List<FaceComparison> rows = LotFaceComparison.Compare(chain.Faces);

            Assert.Equal(4, rows.Count);
            Assert.Equal(2, rows.Count(r => r.IsDiscrepant(0.01)));
            Assert.All(rows.Where(r => r.IsDiscrepant(0.01)), r => Assert.Equal(100, r.LegalFt));
            Assert.True(LotFaceComparison.AnyDiscrepant(rows));
        }

        [Fact]
        public void Describe_NamesStreetFacesAndMarksDiscrepancy()
        {
            LotFaceChainResult chain = LotFaceChain.Order(ConcordFaces.Shuffled());
            var names = chain.Faces.Select(f => f.IsStreetFace && f.LegalLengthFt == 100 ? "E 145 ST" : f.IsStreetFace ? "CONCORD AVE" : "").ToList();
            List<string> lines = LotFaceComparison.Describe(LotFaceComparison.Compare(chain.Faces, names)).ToList();

            Assert.Equal(4, lines.Count);
            Assert.Contains(lines, l => l.Contains("E 145 ST") && l.Contains("100' legal") && l.Contains("⚠"));
            Assert.Contains(lines, l => l.Contains("CONCORD AVE") && l.Contains("77.74' legal") && !l.Contains("⚠"));
            Assert.Contains(lines, l => l.Contains("(interior)"));
        }
    }
}
