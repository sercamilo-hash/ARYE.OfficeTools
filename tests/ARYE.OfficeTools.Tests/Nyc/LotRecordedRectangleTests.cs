using System;
using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Nyc;
using Xunit;

namespace ARYE.OfficeTools.Tests.Nyc
{
    public class LotRecordedRectangleTests
    {
        [Fact]
        public void Build_Concord_Gives77_58By100OnTheConcordFrontage()
        {
            LotFaceChainResult chain = LotFaceChain.Order(ConcordFaces.Shuffled());
            List<LotPoint> ring = LotRecordedRectangle.Build(chain.Faces, 77.58, 100);

            Assert.NotNull(ring);
            Assert.Equal(4, ring.Count);
            List<double> edges = LotDimensionCheck.EdgeLengths(ring);
            Assert.Equal(2, edges.Count(e => Math.Abs(e - 77.58) < 1e-6));
            Assert.Equal(2, edges.Count(e => Math.Abs(e - 100) < 1e-6));
            Assert.InRange(Math.Abs(LotGeometry.SignedArea(ring)), 7757.9, 7758.1);
            Assert.True(LotGeometry.SignedArea(ring) > 0, "must stay counter-clockwise");

            // Vertex i is where face i starts; the frontage face keeps its exact start.
            int k = LotRecordedRectangle.FrontageIndex(chain.Faces, 77.58);
            Assert.True(chain.Faces[k].IsStreetFace);
            Assert.Equal(77.74, chain.Faces[k].LegalLengthFt);
            Assert.True(ring[k].IsSame(chain.Faces[k].Start, 1e-9));
        }

        [Fact]
        public void Build_RectangleLiesInsideTheLot_NotInTheStreet()
        {
            LotFaceChainResult chain = LotFaceChain.Order(ConcordFaces.Shuffled());
            List<LotPoint> ring = LotRecordedRectangle.Build(chain.Faces, 77.58, 100);

            // Centroid of the rectangle must be close to the DTM centroid (same lot, 3% smaller).
            LotPoint dtmCentroid = new LotPoint(chain.Ring.Average(p => p.X), chain.Ring.Average(p => p.Y));
            LotPoint recCentroid = new LotPoint(ring.Average(p => p.X), ring.Average(p => p.Y));
            Assert.True(dtmCentroid.DistanceTo(recCentroid) < 3.0, $"centroids {dtmCentroid.DistanceTo(recCentroid):0.00} ft apart");
        }

        [Fact]
        public void Build_NonQuadrilateral_ReturnsNull()
        {
            var ring = new List<LotPoint> { new LotPoint(0, 0), new LotPoint(50, 0), new LotPoint(60, 40), new LotPoint(50, 100), new LotPoint(0, 100) };
            List<NycLotFace> faces = LotFaceChain.FromRing(ring, "x");
            Assert.Null(LotRecordedRectangle.Build(faces, 50, 100));
        }

        [Fact]
        public void Build_NoRecordedDimensions_ReturnsNull()
        {
            LotFaceChainResult chain = LotFaceChain.Order(ConcordFaces.Shuffled());
            Assert.Null(LotRecordedRectangle.Build(chain.Faces, 0, 100));
        }
    }
}
