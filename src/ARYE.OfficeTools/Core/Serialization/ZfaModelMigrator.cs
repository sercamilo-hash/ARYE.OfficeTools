using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.Core.Serialization
{
    /// <summary>
    /// Upconverts a stored JSON payload from an older SchemaVersion to the
    /// current shape. The Extensible Storage schema itself never changes; all
    /// evolution happens here, one step per version bump.
    /// </summary>
    public static class ZfaModelMigrator
    {
        public static string MigrateToCurrent(string json, int storedVersion)
        {
            if (storedVersion > ZfaProjectModel.CurrentSchemaVersion)
            {
                throw new ZfaSerializationException(
                    $"Stored payload is version {storedVersion}, but this build understands up to " +
                    $"{ZfaProjectModel.CurrentSchemaVersion}. Update BauTools before opening this model.");
            }

            // Version 1 is the first shape; nothing to migrate yet. When version 2
            // exists, add: if (storedVersion == 1) json = MigrateV1ToV2(json);
            return json;
        }
    }
}
