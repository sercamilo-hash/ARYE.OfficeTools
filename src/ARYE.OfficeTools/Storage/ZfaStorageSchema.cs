using System;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace ARYE.OfficeTools.Storage
{
    /// <summary>
    /// Frozen identity of BauTools' Extensible Storage schemas.
    /// THE GUIDS MUST NEVER CHANGE — they are the identity of data already
    /// stored in office models. Schema shape evolution happens inside the JSON
    /// payload (SchemaVersion + ZfaModelMigrator), never by editing a schema.
    /// </summary>
    internal static class ZfaStorageSchema
    {
        // ── Schema A: project-level ZFA model, attached to ProjectInformation ──
        public static readonly Guid ProjectModelGuid = new Guid("077f4bcb-7f3a-4476-9ccb-2829acb2f807");
        public const string ProjectModelName = "BauToolsZfaModel";
        public const string FieldSchemaVersion = "SchemaVersion";
        public const string FieldPayloadJson = "PayloadJson";
        public const string FieldSavedUtc = "SavedUtc";
        public const string FieldSavedBy = "SavedBy";

        // ── Schema B: tag on every propagated Area / boundary line ────────────
        public static readonly Guid PropagatedElementGuid = new Guid("f5a3f36a-e2b0-4fa9-a624-2eed05d04b3c");
        public const string PropagatedElementName = "BauToolsPropagatedElement";
        public const string FieldSourceUniqueId = "SourceUniqueId";
        public const string FieldBuildingId = "BuildingId";
        public const string FieldGroupId = "GroupId";
        public const string FieldRunUtc = "RunUtc";

        private const string VendorId = "ARYE";

        public static Schema GetOrCreateProjectModelSchema()
        {
            Schema existing = Schema.Lookup(ProjectModelGuid);
            if (existing != null)
            {
                return existing;
            }

            var builder = new SchemaBuilder(ProjectModelGuid);
            builder.SetSchemaName(ProjectModelName);
            builder.SetVendorId(VendorId);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetDocumentation("BauTools ZFA project configuration (versioned JSON payload).");
            builder.AddSimpleField(FieldSchemaVersion, typeof(int));
            builder.AddSimpleField(FieldPayloadJson, typeof(string));
            builder.AddSimpleField(FieldSavedUtc, typeof(string));
            builder.AddSimpleField(FieldSavedBy, typeof(string));
            return builder.Finish();
        }

        public static Schema GetOrCreatePropagatedElementSchema()
        {
            Schema existing = Schema.Lookup(PropagatedElementGuid);
            if (existing != null)
            {
                return existing;
            }

            var builder = new SchemaBuilder(PropagatedElementGuid);
            builder.SetSchemaName(PropagatedElementName);
            builder.SetVendorId(VendorId);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetDocumentation("Marks an Area or boundary line as created by BauTools propagation.");
            builder.AddSimpleField(FieldSourceUniqueId, typeof(string));
            builder.AddSimpleField(FieldBuildingId, typeof(string));
            builder.AddSimpleField(FieldGroupId, typeof(string));
            builder.AddSimpleField(FieldRunUtc, typeof(string));
            return builder.Finish();
        }
    }
}
