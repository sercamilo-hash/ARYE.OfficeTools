namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>
    /// The proposed street name for one side of the block, before the user
    /// confirms or edits it — a name is never placed as 3D text without this
    /// step passing through the UI first.
    /// </summary>
    public sealed class StreetFrontage
    {
        public CompassSide Side { get; set; }

        /// <summary>Best guess, or "" if no lot address on this side could be parsed.</summary>
        public string StreetName { get; set; } = string.Empty;

        /// <summary>How many block-edge lots agreed on this name.</summary>
        public int SourceLotCount { get; set; }

        public bool IsConfident => SourceLotCount >= 2;
    }
}
