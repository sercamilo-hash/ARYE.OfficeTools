using System.Collections.Generic;
using ARYE.OfficeTools.Core.Nyc;
using Xunit;

namespace ARYE.OfficeTools.Tests.Nyc
{
    public class BuildingHeightResolverTests
    {
        [Fact]
        public void RealFootprintMatch_UsesHeightRoof()
        {
            var lot = new NycLotInfo { Bbl = "1000010001", NumFloors = 10 };
            var footprints = new List<NycBuildingFootprint>
            {
                new NycBuildingFootprint { MapplutoBbl = "1000010001", HeightRoofFt = 142.5 }
            };

            ResolvedHeight result = BuildingHeightResolver.Resolve(lot, footprints);

            Assert.Equal(142.5, result.HeightFt);
            Assert.Equal(BuildingHeightSource.Real, result.Source);
        }

        [Fact]
        public void NoFootprintMatch_FallsBackToFloorsTimes12()
        {
            var lot = new NycLotInfo { Bbl = "1000010001", NumFloors = 6 };
            ResolvedHeight result = BuildingHeightResolver.Resolve(lot, new List<NycBuildingFootprint>());

            Assert.Equal(72.0, result.HeightFt);
            Assert.Equal(BuildingHeightSource.EstimatedFromFloors, result.Source);
        }

        [Fact]
        public void NoFootprintAndNoFloors_FallsBackToDefault()
        {
            var lot = new NycLotInfo { Bbl = "1000010001", NumFloors = 0 };
            ResolvedHeight result = BuildingHeightResolver.Resolve(lot, null);

            Assert.Equal(36.0, result.HeightFt);
            Assert.Equal(BuildingHeightSource.EstimatedDefault, result.Source);
        }

        [Fact]
        public void FootprintWithZeroHeight_IsIgnored_FallsBackHonestly()
        {
            var lot = new NycLotInfo { Bbl = "1000010001", NumFloors = 4 };
            var footprints = new List<NycBuildingFootprint>
            {
                new NycBuildingFootprint { MapplutoBbl = "1000010001", HeightRoofFt = 0 }
            };

            ResolvedHeight result = BuildingHeightResolver.Resolve(lot, footprints);

            Assert.Equal(BuildingHeightSource.EstimatedFromFloors, result.Source);
        }

        [Fact]
        public void MultipleFootprintsSameBbl_PicksTallest()
        {
            var lot = new NycLotInfo { Bbl = "1000010001" };
            var footprints = new List<NycBuildingFootprint>
            {
                new NycBuildingFootprint { MapplutoBbl = "1000010001", HeightRoofFt = 30 },
                new NycBuildingFootprint { MapplutoBbl = "1000010001", HeightRoofFt = 145 },
                new NycBuildingFootprint { MapplutoBbl = "9999999999", HeightRoofFt = 999 }
            };

            ResolvedHeight result = BuildingHeightResolver.Resolve(lot, footprints);
            Assert.Equal(145, result.HeightFt);
        }
    }
}
