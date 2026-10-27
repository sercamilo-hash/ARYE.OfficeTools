using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using ARYE.OfficeTools.Core.Interop;

namespace ARYE.OfficeTools.Services.Interop
{
    /// <summary>
    /// Writes and reads the interop entities. Must be called INSIDE an open
    /// transaction (the draw service owns the single transaction). Failures
    /// are returned as reasons, never swallowed: a lot without its stamp is a
    /// lot TopoGenerator cannot find, and the user must know.
    /// </summary>
    internal static class LotInteropStampService
    {
        /// <summary>Attaches a face payload to a drawn curve. Returns null on success, else the reason.</summary>
        public static string StampFace(Element curve, LotFacePayload payload)
        {
            try
            {
                Schema schema = LotInteropSchema.Face();
                var entity = new Entity(schema);
                entity.Set(schema.GetField(LotInteropContract.FieldSchemaVersion), LotInteropContract.SchemaVersion);
                entity.Set(schema.GetField(LotInteropContract.FieldPayloadJson), payload.ToJson());
                curve.SetEntity(entity);
                return null;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is InvalidOperationException)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// Creates (or replaces) the DataStorage "BauTools Lot &lt;BBL&gt;" holding the lot record.
        /// Returns the storage element, or null with the reason.
        /// </summary>
        public static DataStorage WriteRecord(Document doc, LotRecordPayload payload, out string failureReason)
        {
            failureReason = null;
            try
            {
                string name = LotInteropContract.DataStorageName(payload.Bbl);
                Schema schema = LotInteropSchema.Record();

                DataStorage storage = new FilteredElementCollector(doc)
                    .OfClass(typeof(DataStorage))
                    .Cast<DataStorage>()
                    .FirstOrDefault(ds => string.Equals(ds.Name, name, StringComparison.Ordinal));
                if (storage == null)
                {
                    storage = DataStorage.Create(doc);
                    storage.Name = name;
                }

                var entity = new Entity(schema);
                entity.Set(schema.GetField(LotInteropContract.FieldSchemaVersion), LotInteropContract.SchemaVersion);
                entity.Set(schema.GetField(LotInteropContract.FieldPayloadJson), payload.ToJson());
                storage.SetEntity(entity);
                return storage;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is InvalidOperationException)
            {
                failureReason = ex.Message;
                return null;
            }
        }

        /// <summary>Face payload on an element, or null when it carries none / it is unreadable.</summary>
        public static LotFacePayload ReadFace(Element element)
        {
            Schema schema = Schema.Lookup(LotInteropSchema.FaceGuid);
            if (schema == null || element == null)
            {
                return null;
            }
            Entity entity = element.GetEntity(schema);
            if (entity == null || !entity.IsValid())
            {
                return null;
            }
            string json = entity.Get<string>(schema.GetField(LotInteropContract.FieldPayloadJson));
            return string.IsNullOrEmpty(json) ? null : LotFacePayload.FromJson(json);
        }

        /// <summary>All lot records present in the document (one per drawn BBL).</summary>
        public static List<LotRecordPayload> ReadRecords(Document doc)
        {
            var records = new List<LotRecordPayload>();
            Schema schema = Schema.Lookup(LotInteropSchema.RecordGuid);
            if (schema == null)
            {
                return records;
            }
            foreach (DataStorage storage in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>())
            {
                Entity entity = storage.GetEntity(schema);
                if (entity == null || !entity.IsValid())
                {
                    continue;
                }
                string json = entity.Get<string>(schema.GetField(LotInteropContract.FieldPayloadJson));
                if (!string.IsNullOrEmpty(json))
                {
                    records.Add(LotRecordPayload.FromJson(json));
                }
            }
            return records;
        }
    }
}
