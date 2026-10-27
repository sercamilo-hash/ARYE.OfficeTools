using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>
    /// A whole city block added as far context — across a street from the
    /// subject's own block, not one of its neighbors. Always DTM geometry: this
    /// is for urban massing context, not for TopoGenerator to align against.
    /// </summary>
    public sealed class NycAdditionalBlock
    {
        public string Borough { get; set; } = string.Empty;

        public string BlockNumber { get; set; } = string.Empty;

        /// <summary>Every MapPLUTO lot on this block — nothing designated "subject" here.</summary>
        public List<NycLotInfo> Lots { get; set; } = new List<NycLotInfo>();

        public string Label => $"Block {BlockNumber} · {Borough} · {Lots.Count} lot(s)";
    }
}
