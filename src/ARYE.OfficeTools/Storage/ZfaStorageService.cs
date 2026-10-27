using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Serialization;

namespace ARYE.OfficeTools.Storage
{
    /// <summary>
    /// Persists the ZfaProjectModel on the document's ProjectInformation element.
    /// A read failure carries the real reason to the caller — the direct fix of
    /// the legacy bug where a failed deserialize silently produced blank state.
    /// </summary>
    internal sealed class ZfaStorageService
    {
        /// <summary>
        /// Returns the stored model, or a fresh empty model when nothing was
        /// ever stored. Throws ZfaSerializationException when data exists but
        /// cannot be read — callers must surface that, never swallow it.
        /// </summary>
        public ZfaProjectModel Load(Document doc)
        {
            Schema schema = ZfaStorageSchema.GetOrCreateProjectModelSchema();
            Entity entity = doc.ProjectInformation.GetEntity(schema);
            if (!entity.IsValid())
            {
                return new ZfaProjectModel();
            }

            int storedVersion = entity.Get<int>(ZfaStorageSchema.FieldSchemaVersion);
            string payload = entity.Get<string>(ZfaStorageSchema.FieldPayloadJson);
            return ZfaModelSerializer.Deserialize(payload, storedVersion);
        }

        /// <summary>Must be called inside an open transaction.</summary>
        public void Save(Document doc, ZfaProjectModel model)
        {
            Schema schema = ZfaStorageSchema.GetOrCreateProjectModelSchema();
            var entity = new Entity(schema);
            entity.Set(ZfaStorageSchema.FieldSchemaVersion, ZfaProjectModel.CurrentSchemaVersion);
            entity.Set(ZfaStorageSchema.FieldPayloadJson, ZfaModelSerializer.Serialize(model));
            entity.Set(ZfaStorageSchema.FieldSavedUtc, DateTime.UtcNow.ToString("o"));
            entity.Set(ZfaStorageSchema.FieldSavedBy, Environment.UserName);
            doc.ProjectInformation.SetEntity(entity);
        }
    }
}
