using System;
using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Model
{
    /// <summary>Which real AreaScheme a propagation run reads from and writes to.</summary>
    public enum PropagationPurpose
    {
        /// <summary>ZFA areas — category filter applies.</summary>
        Zoning = 0,

        /// <summary>Egress areas — everything on the mapped scheme propagates, no filter.</summary>
        Egress = 1
    }

    /// <summary>
    /// Step 2 settings persisted inside the ZfaProjectModel payload. Plain
    /// properties with defaults: System.Text.Json tolerates their absence in
    /// payloads written before this object existed, so SchemaVersion stays 1.
    /// </summary>
    public sealed class PropagationSettings
    {
        /// <summary>
        /// UniqueId of the AreaScheme mapped to the Zoning purpose. When null
        /// or empty, the building's own AreaSchemeUniqueId is the default —
        /// which keeps pre-mapping saves behaving exactly as before.
        /// </summary>
        public string SchemeMappingZoningUniqueId { get; set; }

        /// <summary>UniqueId of the AreaScheme mapped to the Egress purpose. No default.</summary>
        public string SchemeMappingEgressUniqueId { get; set; }

        /// <summary>
        /// Name of the text parameter on Areas that gets stamped with the
        /// building's name on every propagated area. Null/empty = no stamping.
        /// </summary>
        public string BuildingParameterName { get; set; }

        /// <summary>One record per (building, range, purpose) — the latest successful run.</summary>
        public List<PropagationRunRecord> RunRecords { get; set; } = new List<PropagationRunRecord>();

        public PropagationRunRecord FindRecord(Guid buildingId, Guid groupId, PropagationPurpose purpose) =>
            RunRecords.FirstOrDefault(r =>
                r.BuildingId == buildingId && r.GroupId == groupId && r.Purpose == purpose);

        /// <summary>Replaces the record for the run's (building, range, purpose) key, or adds it.</summary>
        public void RecordRun(PropagationRunRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            RunRecords.RemoveAll(r =>
                r.BuildingId == record.BuildingId
                && r.GroupId == record.GroupId
                && r.Purpose == record.Purpose);
            RunRecords.Add(record);
        }
    }

    /// <summary>What the last successful propagation of one (building, range, purpose) did.</summary>
    public sealed class PropagationRunRecord
    {
        public Guid BuildingId { get; set; }

        public Guid GroupId { get; set; }

        public PropagationPurpose Purpose { get; set; }

        public DateTime LastRunUtc { get; set; }

        /// <summary>Windows username of whoever ran it.</summary>
        public string User { get; set; }

        /// <summary>Human-readable filter scope, e.g. "Gross" or "Custom (Mechanical)".</summary>
        public string FilterDescription { get; set; }

        /// <summary>Areas actually created (measured, not planned).</summary>
        public int CreatedCount { get; set; }

        public bool RefreshExisting { get; set; }

        /// <summary>
        /// RangeSyncAnalyzer fingerprint of the source(s) at the moment of the
        /// sync. Null on runs recorded before Sync Ranges — those can only be
        /// compared, not told apart as "source changed" vs "copy edited".
        /// </summary>
        public string SourceFingerprint { get; set; }

        /// <summary>Copies rebuilt by the run.</summary>
        public int LevelCount { get; set; }

        /// <summary>
        /// <see cref="ZfaSchemeResolver.Key"/> of the schemes the run synced.
        /// A record made with another scheme set says nothing about the
        /// current one. Null on runs from before multi-scheme sync.
        /// </summary>
        public string SchemeKey { get; set; }

        /// <summary>
        /// Names from the project's category list that the run's filter
        /// included. Records written before the configurable list carry the
        /// old enum names ("Residential", …) — still readable as strings.
        /// </summary>
        public List<string> SelectedCategories { get; set; } = new List<string>();
    }
}
