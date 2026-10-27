namespace ARYE.OfficeTools.Core.Levels
{
    /// <summary>What the user asked for: a run of evenly-spaced levels.</summary>
    public sealed class LevelGenerationRequest
    {
        public double BaseElevationFt { get; set; }

        public int Count { get; set; }

        public double TypicalHeightFt { get; set; }

        public string NamePrefix { get; set; } = "L";

        public int StartNumber { get; set; } = 1;

        /// <summary>Zero-padding for the number — 2 gives "L01".."L12".</summary>
        public int NamePadding { get; set; } = 2;
    }
}
