using System;
using Autodesk.Revit.DB.ExtensibleStorage;
using ARYE.OfficeTools.Core.Interop;

namespace ARYE.OfficeTools.Services.Interop
{
    /// <summary>
    /// The two ExtensibleStorage schemas of the BauTools ↔ TopoGenerator
    /// contract (docs/CONTRATO-LOT-TOPO.md). GUIDs, names, field names and
    /// access levels are FROZEN: another add-in rebuilds the identical
    /// definition to read them, and Revit throws if two definitions of one
    /// GUID differ. Public read is what makes cross-add-in reading possible.
    /// </summary>
    internal static class LotInteropSchema
    {
        public static readonly Guid FaceGuid = new Guid(LotInteropContract.FaceSchemaGuid);
        public static readonly Guid RecordGuid = new Guid(LotInteropContract.RecordSchemaGuid);

        public static Schema Face() => GetOrCreate(FaceGuid, LotInteropContract.FaceSchemaName);

        public static Schema Record() => GetOrCreate(RecordGuid, LotInteropContract.RecordSchemaName);

        private static Schema GetOrCreate(Guid guid, string name)
        {
            Schema existing = Schema.Lookup(guid);
            if (existing != null)
            {
                return existing;
            }
            var builder = new SchemaBuilder(guid);
            builder.SetSchemaName(name);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetVendorId(LotInteropContract.VendorId);
            builder.SetDocumentation("BauTools NYC Lot Boundary interop record. See BauTools docs/CONTRATO-LOT-TOPO.md.");
            builder.AddSimpleField(LotInteropContract.FieldSchemaVersion, typeof(int));
            builder.AddSimpleField(LotInteropContract.FieldPayloadJson, typeof(string));
            return builder.Finish();
        }
    }
}
