using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Nyc
{
    public sealed class ResolvedHeight
    {
        public double HeightFt { get; set; }

        public BuildingHeightSource Source { get; set; }

        public string SourceLabel => Source switch
        {
            BuildingHeightSource.Real => "real (NYC Building Footprints)",
            BuildingHeightSource.EstimatedFromFloors => "estimated (floors × 12 ft)",
            _ => "estimated (default)"
        };
    }

    /// <summary>
    /// Real height when a Building Footprints record matches this lot's BBL;
    /// otherwise an honest fallback estimate. Never silently mislabels an
    /// estimate as real — that was the whole point of doing this at all.
    /// </summary>
    public static class BuildingHeightResolver
    {
        public static ResolvedHeight Resolve(NycLotInfo lot, IReadOnlyList<NycBuildingFootprint> footprints)
        {
            NycBuildingFootprint match = footprints?
                .Where(f => f.MapplutoBbl == lot.Bbl && f.HeightRoofFt > 0)
                .OrderByDescending(f => f.HeightRoofFt) // tallest footprint on the lot, e.g. main tower over an annex
                .FirstOrDefault();

            if (match != null)
            {
                return new ResolvedHeight { HeightFt = match.HeightRoofFt, Source = BuildingHeightSource.Real };
            }
            if (lot.NumFloors > 0)
            {
                return new ResolvedHeight
                {
                    HeightFt = lot.NumFloors * 12.0,
                    Source = BuildingHeightSource.EstimatedFromFloors
                };
            }
            return new ResolvedHeight { HeightFt = 36.0, Source = BuildingHeightSource.EstimatedDefault };
        }
    }
}
