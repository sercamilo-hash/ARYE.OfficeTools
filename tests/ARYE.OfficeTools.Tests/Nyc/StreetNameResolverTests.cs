using System.Collections.Generic;
using ARYE.OfficeTools.Core.Nyc;
using Xunit;

namespace ARYE.OfficeTools.Tests.Nyc
{
    public class StreetNameResolverTests
    {
        [Theory]
        [InlineData("123 MAIN ST", "MAIN ST")]
        [InlineData("123-45 QUEENS BLVD", "QUEENS BLVD")]
        [InlineData("68 WILLOUGHBY STREET", "WILLOUGHBY STREET")]
        [InlineData("485A FULTON STREET", "FULTON STREET")]
        [InlineData("MAIN ST", "MAIN ST")] // no house number at all
        [InlineData("", "")]
        [InlineData(null, "")]
        public void ParseStreetName_StripsLeadingHouseNumber(string address, string expected)
        {
            Assert.Equal(expected, StreetNameResolver.ParseStreetName(address));
        }

        private static NycLotInfo Lot(string bbl, string address, double x, double y, double size = 5)
        {
            return new NycLotInfo
            {
                Bbl = bbl,
                Address = address,
                PolygonRings = new List<List<LotPoint>>
                {
                    new List<LotPoint>
                    {
                        new LotPoint(x, y), new LotPoint(x + size, y),
                        new LotPoint(x + size, y + size), new LotPoint(x, y + size)
                    }
                }
            };
        }

        [Fact]
        public void ResolveFrontages_AlwaysReturnsAllFourSides()
        {
            var context = new NycBlockContext { SubjectLot = Lot("1", "50 MAIN ST", 45, 45) };
            List<StreetFrontage> frontages = StreetNameResolver.ResolveFrontages(context);

            Assert.Equal(4, frontages.Count);
            Assert.Contains(frontages, f => f.Side == CompassSide.North);
            Assert.Contains(frontages, f => f.Side == CompassSide.South);
            Assert.Contains(frontages, f => f.Side == CompassSide.East);
            Assert.Contains(frontages, f => f.Side == CompassSide.West);
        }

        [Fact]
        public void ResolveFrontages_MajorityNamePerSide_WithConfidence()
        {
            // A 100x100 block: two lots near the north edge agree on "MAIN ST",
            // one lot near the south edge is alone on "SOUTH AVE".
            var context = new NycBlockContext
            {
                SubjectLot = Lot("subject", "50 MAIN ST", 45, 90),
                OtherLots = new List<NycLotInfo>
                {
                    Lot("2", "10 MAIN ST", 5, 92),
                    Lot("3", "20 SOUTH AVE", 20, 2)
                }
            };

            List<StreetFrontage> frontages = StreetNameResolver.ResolveFrontages(context);
            StreetFrontage north = frontages.Find(f => f.Side == CompassSide.North);
            StreetFrontage south = frontages.Find(f => f.Side == CompassSide.South);

            Assert.Equal("MAIN ST", north.StreetName);
            Assert.True(north.IsConfident); // 2 lots agree
            Assert.Equal("SOUTH AVE", south.StreetName);
            Assert.False(south.IsConfident); // only 1 lot
        }

        [Fact]
        public void ResolveFrontages_SideWithNoData_ReturnsEmptyNameNotNull()
        {
            var context = new NycBlockContext { SubjectLot = Lot("1", "50 MAIN ST", 45, 90) };
            List<StreetFrontage> frontages = StreetNameResolver.ResolveFrontages(context);

            StreetFrontage east = frontages.Find(f => f.Side == CompassSide.East);
            Assert.NotNull(east.StreetName);
            Assert.Equal(0, east.SourceLotCount);
        }
    }
}
