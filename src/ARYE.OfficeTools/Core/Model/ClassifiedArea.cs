namespace ARYE.OfficeTools.Core.Model
{
    /// <summary>One Revit Area flattened to plain data — the calculator's input unit.</summary>
    public sealed class ClassifiedArea
    {
        public string BuildingName { get; set; }

        public string LevelName { get; set; }

        /// <summary>Order of the level in the building, bottom-up, for matrix sorting.</summary>
        public int LevelIndex { get; set; }

        /// <summary>Level elevation in feet — height-dependent rules (parking) need it.</summary>
        public double LevelElevationFt { get; set; }

        /// <summary>UniqueId of the level — links a matrix row back to its Step 1 range.</summary>
        public string LevelUniqueId { get; set; }

        /// <summary>
        /// Legacy enum classification, used only by the old ZoningCalculator
        /// (the ZR-validation reference). The office matrix path reads
        /// <see cref="CategoryName"/> / <see cref="Role"/> instead.
        /// </summary>
        public ZfaCategory Category { get; set; }

        /// <summary>
        /// Matched name from the project's configurable category list — the
        /// exact list spelling, not the raw parameter text. Null = Unclassified.
        /// </summary>
        public string CategoryName { get; set; }

        /// <summary>Role of the matched category. Null while unclassified.</summary>
        public CategoryRole? Role { get; set; }

        public double SquareFeet { get; set; }

        public string AreaName { get; set; }
    }
}
