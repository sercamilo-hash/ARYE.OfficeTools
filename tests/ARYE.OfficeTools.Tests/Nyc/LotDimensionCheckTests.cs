using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Nyc;
using Xunit;

namespace ARYE.OfficeTools.Tests.Nyc
{
    public class LotDimensionCheckTests
    {
        /// <summary>
        /// Real case: BBL 2025770009 (438 Concord Ave, Bronx), pulled live from
        /// MapPLUTO on 2026-09-09. DOF records LotFront 77.58 / LotDepth 100,
        /// but the Digital Tax Map polygon measures ~103.3 along the depth —
        /// the exact 3.3' gap the user found in Revit. Pinned here so the tool
        /// keeps reporting it instead of ever hiding it.
        /// </summary>
        private static NycLotInfo Concord438() => new NycLotInfo
        {
            Bbl = "2025770009",
            Address = "438 CONCORD AVENUE",
            LotFrontRecordedFt = 77.58,
            LotDepthRecordedFt = 100,
            LotAreaSqFt = 7758,
            PolygonRings = new List<List<LotPoint>>
            {
                new List<LotPoint>
                {
                    new LotPoint(1009522.91892529, 234457.372996807),
                    new LotPoint(1009502.59055328, 234382.870522976),
                    new LotPoint(1009402.8732357, 234409.886860371),
                    new LotPoint(1009423.14713955, 234484.404757023),
                    new LotPoint(1009522.91892529, 234457.372996807) // GeoJSON closing vertex
                }
            }
        };

        [Fact]
        public void EdgeLengths_SkipsClosingVertex_ReturnsFourEdges()
        {
            List<double> edges = LotDimensionCheck.EdgeLengths(Concord438().PolygonRings[0]);
            Assert.Equal(4, edges.Count);
        }

        [Fact]
        public void Concord438_FrontageMatchesRecord_DepthDoesNot()
        {
            List<DimensionComparison> comparisons = LotDimensionCheck.Compare(Concord438());

            DimensionComparison frontage = comparisons.Single(c => c.Label == "Frontage");
            DimensionComparison depth = comparisons.Single(c => c.Label == "Depth");

            Assert.Equal(77.23, frontage.MeasuredFt, 2);
            Assert.False(frontage.IsDiscrepant(LotDimensionCheck.DefaultTolerancePct)); // 0.45% — fine

            Assert.Equal(103.3, depth.MeasuredFt, 1);
            Assert.True(depth.IsDiscrepant(LotDimensionCheck.DefaultTolerancePct));   // +3.3% — flagged
            Assert.True(depth.DifferenceFt > 3.0 && depth.DifferenceFt < 3.5);
        }

        [Fact]
        public void Compare_IsRotationIndependent()
        {
            // The Concord lot sits ~15° off the grid. A bounding box would report
            // ~120 x ~101 — wrong on both counts. Edge matching reports the real edges.
            NycLotInfo lot = Concord438();
            List<double> edges = LotDimensionCheck.EdgeLengths(lot.PolygonRings[0]);
            double bboxWidth = lot.MaxX - lot.MinX;
            Assert.True(bboxWidth > 115); // proves the bbox would have misled
            Assert.DoesNotContain(edges, e => e > 110); // and that no real edge is that long
        }

        [Fact]
        public void Describe_FlagsOnlyTheDiscrepantDimension()
        {
            List<string> lines = LotDimensionCheck.Describe(LotDimensionCheck.Compare(Concord438()));

            Assert.Equal(2, lines.Count);
            Assert.Contains(lines, l => l.StartsWith("Frontage") && l.Contains("✓"));
            Assert.Contains(lines, l => l.StartsWith("Depth") && l.Contains("⚠") && l.Contains("+3.3'"));
            Assert.True(LotDimensionCheck.AnyDiscrepant(LotDimensionCheck.Compare(Concord438())));
        }

        [Fact]
        public void NoRecordedDimension_SaysSoInsteadOfGuessing()
        {
            var lot = Concord438();
            lot.LotFrontRecordedFt = 0;
            List<DimensionComparison> comparisons = LotDimensionCheck.Compare(lot);
            DimensionComparison frontage = comparisons.Single(c => c.Label == "Frontage");

            Assert.False(frontage.HasRecorded);
            Assert.False(frontage.IsDiscrepant(0.01));
            Assert.Contains(LotDimensionCheck.Describe(comparisons), l => l.Contains("no recorded dimension"));
        }

        [Fact]
        public void NoGeometry_ReportsRecordedButCannotMeasure()
        {
            var lot = Concord438();
            lot.PolygonRings.Clear();
            List<DimensionComparison> comparisons = LotDimensionCheck.Compare(lot);

            Assert.All(comparisons, c => Assert.False(c.HasMeasured));
            Assert.False(LotDimensionCheck.AnyDiscrepant(comparisons));
            Assert.Contains(LotDimensionCheck.Describe(comparisons), l => l.Contains("no tax-map geometry"));
        }
    }
}
