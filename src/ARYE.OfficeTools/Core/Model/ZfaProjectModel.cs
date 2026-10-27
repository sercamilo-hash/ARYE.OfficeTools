using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Model
{
    /// <summary>
    /// The root object persisted in Extensible Storage (as JSON inside the
    /// envelope schema). SchemaVersion is the payload version — bump it when
    /// the shape changes and teach ZfaModelMigrator the upgrade.
    /// </summary>
    public sealed class ZfaProjectModel
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public List<BuildingDefinition> Buildings { get; set; } = new List<BuildingDefinition>();

        public ZoningParameters Zoning { get; set; } = new ZoningParameters();

        /// <summary>
        /// The project's configurable ZFA category list — each entry's Name is
        /// the exact "ZFA Category" parameter value, its Role decides where the
        /// square feet land in the matrix. Old payloads without the list get
        /// the office defaults via this initializer (SchemaVersion stays 1).
        /// </summary>
        public List<CategoryDefinition> Categories { get; set; } = CategoryDefinition.OfficeDefaults();

        /// <summary>Step 3 state: last computation stamp and the drafting view link.</summary>
        public MatrixSettings Matrix { get; set; } = new MatrixSettings();

        /// <summary>
        /// Step 2 settings and run history. A plain property with a default:
        /// payloads written before it existed deserialize fine (SchemaVersion
        /// stays 1, ZfaModelMigrator untouched).
        /// </summary>
        public PropagationSettings Propagation { get; set; } = new PropagationSettings();

        /// <summary>
        /// Step 4 sheet package definitions. Old payloads without the list get
        /// the four office seeds via this initializer (SchemaVersion stays 1,
        /// ZfaModelMigrator untouched).
        /// </summary>
        public List<SheetPackage> SheetPackages { get; set; } = SheetPackage.DefaultSeed();

        /// <summary>One record per (package, building) — the latest successful sheet generation.</summary>
        public List<SheetPackageRunRecord> SheetRunRecords { get; set; } =
            new List<SheetPackageRunRecord>();

        public SheetPackageRunRecord FindSheetRun(System.Guid packageId, System.Guid buildingId)
        {
            foreach (SheetPackageRunRecord record in SheetRunRecords)
            {
                if (record.PackageId == packageId && record.BuildingId == buildingId)
                {
                    return record;
                }
            }
            return null;
        }

        /// <summary>Replaces the record for the run's (package, building) key, or adds it.</summary>
        public void RecordSheetRun(SheetPackageRunRecord record)
        {
            if (record == null) throw new System.ArgumentNullException(nameof(record));
            SheetRunRecords.RemoveAll(r =>
                r.PackageId == record.PackageId && r.BuildingId == record.BuildingId);
            SheetRunRecords.Add(record);
        }
    }
}
