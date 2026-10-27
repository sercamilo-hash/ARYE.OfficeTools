using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Model
{
    /// <summary>Which side of the office ZFA arithmetic a category sits on.</summary>
    public enum CategoryRole
    {
        /// <summary>Counts into GROSS FLOOR AREA — the base of NET.</summary>
        MainGross = 0,

        /// <summary>Tracked in its own COMMERCIAL GROSS AREA column, informational — never part of NET.</summary>
        CommercialGross = 1,

        /// <summary>One matrix column each; subtracted from MainGross to reach NET.</summary>
        Deduction = 2,

        /// <summary>Counts in no total: voids, shafts, the un-area'd upper half of a duplex.</summary>
        Exempt = 3
    }

    /// <summary>
    /// One entry of the project-level, user-editable category list. Name is
    /// the exact "ZFA Category" parameter value (matched case-insensitively,
    /// trimmed); Role decides where its square feet land in the matrix.
    /// </summary>
    public sealed class CategoryDefinition
    {
        public CategoryDefinition()
        {
        }

        public CategoryDefinition(string name, CategoryRole role)
        {
            Name = name;
            Role = role;
        }

        public string Name { get; set; }

        public CategoryRole Role { get; set; }

        /// <summary>
        /// The office-standard seed list. Also what an old payload without a
        /// stored category list gets, via the ZfaProjectModel property default.
        /// </summary>
        public static List<CategoryDefinition> OfficeDefaults() => new List<CategoryDefinition>
        {
            new CategoryDefinition("Gross", CategoryRole.MainGross),
            new CategoryDefinition("Commercial Gross", CategoryRole.CommercialGross),
            new CategoryDefinition("Chase Walls", CategoryRole.Deduction),
            new CategoryDefinition("Mechanical", CategoryRole.Deduction),
            new CategoryDefinition("Loading Dock", CategoryRole.Deduction),
            new CategoryDefinition("Bicycle Parking", CategoryRole.Deduction),
            new CategoryDefinition("Amenities", CategoryRole.Deduction),
            new CategoryDefinition("Corridor", CategoryRole.Deduction),
            new CategoryDefinition("Refuse", CategoryRole.Deduction),
            new CategoryDefinition("Exempt", CategoryRole.Exempt)
        };
    }
}
