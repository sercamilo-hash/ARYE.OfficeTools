using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Nyc
{
    public enum BuildingHeightSource
    {
        /// <summary>HEIGHT_ROOF came back from the Building Footprints dataset.</summary>
        Real = 0,

        /// <summary>No footprint matched this BBL — estimated from MapPLUTO NumFloors.</summary>
        EstimatedFromFloors = 1,

        /// <summary>No footprint and no floor count either — flat default.</summary>
        EstimatedDefault = 2
    }

    /// <summary>One NYC Building Footprints record. No Revit types.</summary>
    public sealed class NycBuildingFootprint
    {
        public string Bin { get; set; } = string.Empty;

        /// <summary>MAPPLUTO_BBL field — joins directly to <see cref="NycLotInfo.Bbl"/>.</summary>
        public string MapplutoBbl { get; set; } = string.Empty;

        public double HeightRoofFt { get; set; }

        public double GroundElevationFt { get; set; }

        public int ConstructionYear { get; set; }

        public string FeatureCode { get; set; } = string.Empty;

        public List<List<LotPoint>> PolygonRings { get; set; } = new List<List<LotPoint>>();
    }
}
