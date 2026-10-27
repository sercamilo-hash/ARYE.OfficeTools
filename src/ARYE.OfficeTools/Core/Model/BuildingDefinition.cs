using System;
using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Model
{
    public sealed class BuildingDefinition
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Display name, e.g. "BLDG A".</summary>
        public string Name { get; set; }

        /// <summary>UniqueId of the scope box (OST_VolumeOfInterest) framing this building.</summary>
        public string ScopeBoxUniqueId { get; set; }

        /// <summary>
        /// UniqueId of the building's GROSS AreaScheme ("Gross area scheme" in
        /// Step 1) — what the Sheet Diagrammer, Thermal Boundary and QA read.
        /// </summary>
        public string AreaSchemeUniqueId { get; set; }

        /// <summary>
        /// The AreaSchemes that together hold this building's ZFA areas (for
        /// example "Gross Building" and "Deductions"), chosen in Step 2. Step 2
        /// syncs ranges on each, Step 3 harvests all of them. Empty in saves
        /// made before Sync Ranges — see <see cref="ZfaSchemeResolver"/>.
        /// </summary>
        public List<string> ZfaSchemeUniqueIds { get; set; } = new List<string>();

        public List<FloorGroup> Groups { get; set; } = new List<FloorGroup>();

        /// <summary>
        /// Explicit exclusion list: a level listed here is never a propagation
        /// target, regardless of what any group definition says.
        /// </summary>
        public List<LevelRef> AtypicalLevels { get; set; } = new List<LevelRef>();
    }
}
