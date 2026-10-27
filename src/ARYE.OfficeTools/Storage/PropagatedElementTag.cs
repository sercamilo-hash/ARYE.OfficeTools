using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace ARYE.OfficeTools.Storage
{
    /// <summary>
    /// The per-element mark that makes propagation non-destructive and
    /// idempotent: only elements carrying this tag (with a matching building
    /// and group) are ever deleted or replaced by a re-run. Hand-drawn
    /// elements never carry it and are never touched.
    /// </summary>
    internal static class PropagatedElementTag
    {
        public static void Write(Element element, string sourceUniqueId, Guid buildingId, Guid groupId, DateTime runUtc)
        {
            Schema schema = ZfaStorageSchema.GetOrCreatePropagatedElementSchema();
            var entity = new Entity(schema);
            entity.Set(ZfaStorageSchema.FieldSourceUniqueId, sourceUniqueId);
            entity.Set(ZfaStorageSchema.FieldBuildingId, buildingId.ToString("D"));
            entity.Set(ZfaStorageSchema.FieldGroupId, groupId.ToString("D"));
            entity.Set(ZfaStorageSchema.FieldRunUtc, runUtc.ToString("o"));
            element.SetEntity(entity);
        }

        /// <summary>True when the element was created by propagation of the given building+group.</summary>
        public static bool Matches(Element element, Guid buildingId, Guid groupId)
        {
            Schema schema = Schema.Lookup(ZfaStorageSchema.PropagatedElementGuid);
            if (schema == null)
            {
                return false;
            }
            Entity entity = element.GetEntity(schema);
            if (!entity.IsValid())
            {
                return false;
            }
            return entity.Get<string>(ZfaStorageSchema.FieldBuildingId) == buildingId.ToString("D")
                && entity.Get<string>(ZfaStorageSchema.FieldGroupId) == groupId.ToString("D");
        }

        /// <summary>
        /// Reads the full tag back. Step 4 stores its view/sheet identity in
        /// SourceUniqueId (see ARYE.OfficeTools.Core.Sheets.SheetTagIdentity) with
        /// BuildingId = building and GroupId = package id — the same schema,
        /// a parallel key, exactly like the Step 3 matrix sentinel ids.
        /// </summary>
        public static bool TryRead(Element element,
            out string sourceUniqueId, out Guid buildingId, out Guid groupId)
        {
            sourceUniqueId = null;
            buildingId = Guid.Empty;
            groupId = Guid.Empty;

            Schema schema = Schema.Lookup(ZfaStorageSchema.PropagatedElementGuid);
            if (schema == null)
            {
                return false;
            }
            Entity entity = element.GetEntity(schema);
            if (!entity.IsValid())
            {
                return false;
            }
            sourceUniqueId = entity.Get<string>(ZfaStorageSchema.FieldSourceUniqueId);
            return Guid.TryParse(entity.Get<string>(ZfaStorageSchema.FieldBuildingId), out buildingId)
                && Guid.TryParse(entity.Get<string>(ZfaStorageSchema.FieldGroupId), out groupId);
        }

        public static bool IsTagged(Element element)
        {
            Schema schema = Schema.Lookup(ZfaStorageSchema.PropagatedElementGuid);
            if (schema == null)
            {
                return false;
            }
            return element.GetEntity(schema).IsValid();
        }
    }
}
