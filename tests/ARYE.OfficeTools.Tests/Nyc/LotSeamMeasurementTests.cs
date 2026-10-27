using System.Collections.Generic;
using ARYE.OfficeTools.Core.Nyc;
using Xunit;

namespace ARYE.OfficeTools.Tests.Nyc
{
    public class LotSeamMeasurementTests
    {
        [Fact]
        public void MaxGapToReference_SharedEdge_ReturnsNearZero()
        {
            // Two 50x100 rectangles sharing the edge x=50.
            var subject = new List<LotPoint> { new(0, 0), new(50, 0), new(50, 100), new(0, 100) };
            var neighbor = new List<LotPoint> { new(50, 0), new(100, 0), new(100, 100), new(50, 100) };

            double? gap = LotSeamMeasurement.MaxGapToReference(neighbor, subject);

            Assert.NotNull(gap);
            Assert.True(gap < 1e-6, $"expected ~0, got {gap}");
        }

        [Fact]
        public void MaxGapToReference_OffsetNeighbor_ReturnsTheOffset()
        {
            var subject = new List<LotPoint> { new(0, 0), new(50, 0), new(50, 100), new(0, 100) };
            // Neighbor's shared edge sits 0.31 ft further out (x=50.31), far corner is irrelevant.
            var neighbor = new List<LotPoint> { new(50.31, 0), new(100, 0), new(100, 100), new(50.31, 100) };

            double? gap = LotSeamMeasurement.MaxGapToReference(neighbor, subject, proximityFt: 15);

            Assert.NotNull(gap);
            Assert.InRange(gap!.Value, 0.30, 0.32);
        }

        [Fact]
        public void MaxGapToReference_FarApart_ReturnsNull()
        {
            var subject = new List<LotPoint> { new(0, 0), new(50, 0), new(50, 100), new(0, 100) };
            var farAway = new List<LotPoint> { new(5000, 5000), new(5050, 5000), new(5050, 5100), new(5000, 5100) };

            double? gap = LotSeamMeasurement.MaxGapToReference(farAway, subject);

            Assert.Null(gap);
        }

        [Fact]
        public void MaxGapToReference_DegenerateRings_ReturnsNull()
        {
            Assert.Null(LotSeamMeasurement.MaxGapToReference(new List<LotPoint>(), new List<LotPoint> { new(0, 0), new(1, 1) }));
            Assert.Null(LotSeamMeasurement.MaxGapToReference(new List<LotPoint> { new(0, 0), new(1, 1) }, null));
        }
    }
}
