using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>A real paved area (roadbed or sidewalk) from DOF's 2022 planimetric survey. Outer + hole rings, local feet.</summary>
    public sealed class NycGroundPolygon
    {
        public List<List<LotPoint>> PolygonRings { get; set; } = new List<List<LotPoint>>();
    }

    /// <summary>The curb line between a sidewalk and a roadbed, from DOF's 2022 planimetric survey.</summary>
    public sealed class NycCurbLine
    {
        public List<LotPoint> Path { get; set; } = new List<LotPoint>();
    }

    /// <summary>One street tree from NYC Parks' 2025 Tree Census (in progress — many fields are still unmeasured).</summary>
    public sealed class NycStreetTree
    {
        public LotPoint Location { get; set; }

        /// <summary>Empty when not yet surveyed.</summary>
        public string Species { get; set; } = string.Empty;

        /// <summary>Derived from the census's trunk circumference (inches) — 0 when unmeasured.</summary>
        public double TrunkDiameterIn { get; set; }

        public string Condition { get; set; } = string.Empty;
    }
}
