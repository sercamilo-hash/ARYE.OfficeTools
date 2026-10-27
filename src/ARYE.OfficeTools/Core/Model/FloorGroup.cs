using System;
using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Model
{
    public sealed class FloorGroup
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Display name, e.g. "Typical L03–L14".</summary>
        public string Name { get; set; }

        public FloorGroupKind Kind { get; set; }

        /// <summary>Standard groups: the level whose areas are the template.</summary>
        public LevelRef SourceLevel { get; set; }

        /// <summary>Standard groups: ordered bottom-up. Must never contain atypical levels.</summary>
        public List<LevelRef> TargetLevels { get; set; } = new List<LevelRef>();

        /// <summary>Duplex groups only: upper floor of the source unit pair (SourceLevel is the lower).</summary>
        public LevelRef SourceUpperLevel { get; set; }

        /// <summary>Duplex groups only.</summary>
        public List<DuplexLevelPair> TargetPairs { get; set; } = new List<DuplexLevelPair>();
    }
}
