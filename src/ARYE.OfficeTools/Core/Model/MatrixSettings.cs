using System;

namespace ARYE.OfficeTools.Core.Model
{
    /// <summary>
    /// Step 3 state persisted inside the ZfaProjectModel payload. Plain
    /// properties with defaults: payloads written before this object existed
    /// deserialize fine (SchemaVersion stays 1, ZfaModelMigrator untouched).
    /// </summary>
    public sealed class MatrixSettings
    {
        /// <summary>When the matrix was last computed — survives closing the file.</summary>
        public DateTime? LastComputedUtc { get; set; }

        /// <summary>Windows username of whoever computed it.</summary>
        public string LastComputedBy { get; set; }

        /// <summary>
        /// UniqueId of the drafting view the matrix is drawn into. UniqueId,
        /// not name — the link survives a user rename. When the view was
        /// deleted, the next run creates a fresh one and stores its id here.
        /// </summary>
        public string DraftingViewUniqueId { get; set; }
    }
}
