namespace ARYE.OfficeTools.Core.Model
{
    /// <summary>
    /// How an Area participates in the ZFA calculation. Stored on each Area as
    /// the "ZFA Category" parameter; docs/ZONING-RULES.md carries the ZR basis
    /// of each deductible category.
    /// </summary>
    public enum ZfaCategory
    {
        /// <summary>Not yet classified — blocks the matrix until a human decides.</summary>
        Unclassified = 0,

        Residential = 1,
        Commercial = 2,
        CommunityFacility = 3,

        /// <summary>Floor space used for mechanical equipment — ZR §12-10 exclusion.</summary>
        Mechanical = 10,

        /// <summary>Accessory off-street parking — ZR §12-10 exclusion subject to height rules.</summary>
        AccessoryParking = 11,

        /// <summary>Accessory off-street loading berths — ZR §12-10 exclusion.</summary>
        LoadingBerth = 12,

        /// <summary>Exterior wall thickness areas measured for the Zone Green deduction.</summary>
        ExteriorWall = 13,

        /// <summary>Cellar space — excluded from floor area by definition, ZR §12-10.</summary>
        Cellar = 14,

        /// <summary>Counts in no total: voids, shafts, the un-area'd upper half of a duplex.</summary>
        Exempt = 99
    }
}
