namespace ARYE.OfficeTools.Core.Model
{
    /// <summary>
    /// User-supplied zoning inputs. In v1 these are typed in by hand; a future
    /// MapPLUTO integration would populate the same object.
    /// Thresholds carry defaults from the ZR text with their citation next to
    /// the constant — every uncertain one is overridable here on purpose.
    /// </summary>
    public sealed class ZoningParameters
    {
        public double LotAreaSqFt { get; set; }

        public double MaxFar { get; set; }

        /// <summary>Zoning district label, informational (e.g. "R7-2", "C4-4D").</summary>
        public string District { get; set; }

        /// <summary>
        /// When on, the matrix takes ENERGY = EnergyDeductionPercent × NET off
        /// each level before ZFA. Off by default — claiming it is a project
        /// decision. Plain property with a default: old payloads load fine.
        /// </summary>
        public bool EnergyDeductionEnabled { get; set; }

        /// <summary>Energy deduction as a percentage of NET (office default 5).</summary>
        public double EnergyDeductionPercent { get; set; } = 5.0;

        /// <summary>
        /// Accessory parking is excluded from floor area only up to this height
        /// above curb level. Default 23 ft per ZR §12-10 "floor area" (m).
        /// TODO-VERIFY(ZR §12-10): confirm against the current text — City of Yes
        /// (2024) may have amended parking exclusions per district.
        /// </summary>
        public double ParkingExclusionMaxHeightFt { get; set; } = 23.0;

        /// <summary>
        /// Maximum exterior-wall thickness deductible per Zone Green.
        /// Default 8 inches per ZR §12-10 as amended 2012.
        /// TODO-VERIFY(ZR §12-10): confirm current text.
        /// </summary>
        public double ExteriorWallDeductionMaxIn { get; set; } = 8.0;

        /// <summary>
        /// Optional cap on the mechanical deduction as a fraction of the
        /// building's gross (0 = no cap). The 2019 mechanical-voids amendment
        /// constrains tall mechanical floors in certain districts.
        /// TODO-VERIFY(ZR §23-16): confirm applicability per district.
        /// </summary>
        public double MechanicalDeductionCapPct { get; set; }
    }
}
