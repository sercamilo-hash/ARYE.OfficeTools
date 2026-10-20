#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// Persists "last run" stamps (date, user, scope, count) in the document via ExtensibleStorage,
    /// as agreed for every BauTools tool. One schema, one string field, one line per tool key, so
    /// adding a tool never requires a new schema (schemas cannot be altered once published).
    /// Every failure is swallowed: a stamp must never block the real work.
    /// </summary>
    public static class BauToolsRunStampService
    {
        private static readonly Guid SCHEMA_GUID = new Guid("7C1E3A20-5B44-4C6E-9F2A-8D5E1B0C7A31");
        private const string SCHEMA_NAME = "BauToolsRunStamps";
        private const string FIELD_NAME = "Payload";
        private const char REC_SEP = '\u001E';   // record separator
        private const char FLD_SEP = '\u001F';   // unit separator

        private static Schema GetOrCreateSchema()
        {
            Schema s = Schema.Lookup(SCHEMA_GUID);
            if (s != null) return s;

            SchemaBuilder sb = new SchemaBuilder(SCHEMA_GUID);
            sb.SetSchemaName(SCHEMA_NAME);
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.SetVendorId("BAUT");
            sb.AddSimpleField(FIELD_NAME, typeof(string));
            return sb.Finish();
        }

        private static Element Holder(Document doc)
        {
            try
            {
                Element pi = doc.ProjectInformation;
                if (pi != null) return pi;
            }
            catch { }
            return null;
        }

        /// <summary>Writes the stamp. Opens its own transaction, so call it outside another one.</summary>
        public static void Stamp(Document doc, string toolKey, string scope, int count, string extra)
        {
            Stamp(doc, toolKey, scope, count, extra, null);
        }

        /// <summary>
        /// Writes the stamp plus per-project settings of the tool (e.g. the parameters chosen in its
        /// selectors), each stored as its own line keyed "toolKey.setting.name" in the same entity,
        /// so remembering choices never needs a new schema. Read them back with <see cref="ReadSetting"/>.
        /// </summary>
        public static void Stamp(Document doc, string toolKey, string scope, int count, string extra,
            IDictionary<string, string> settings)
        {
            try
            {
                Element holder = Holder(doc);
                if (holder == null) return;

                Schema schema = GetOrCreateSchema();
                if (schema == null) return;

                Dictionary<string, string> lines = ReadAll(doc, schema, holder);

                string user = "unknown";
                try { user = doc.Application.Username; } catch { }

                lines[toolKey] = string.Join(FLD_SEP.ToString(), new string[]
                {
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    user,
                    (scope ?? string.Empty).Replace(FLD_SEP, ' ').Replace(REC_SEP, ' '),
                    count.ToString(),
                    (extra ?? string.Empty).Replace(FLD_SEP, ' ').Replace(REC_SEP, ' ')
                });

                if (settings != null)
                {
                    foreach (KeyValuePair<string, string> kv in settings)
                    {
                        lines[SettingKey(toolKey, kv.Key)] = (kv.Value ?? string.Empty).Replace(FLD_SEP, ' ').Replace(REC_SEP, ' ');
                    }
                }

                string payload = string.Join(REC_SEP.ToString(),
                    lines.Select(kv => kv.Key + FLD_SEP + kv.Value));

                using (Transaction t = new Transaction(doc, "BauTools · Run stamp"))
                {
                    t.Start();
                    Entity e = new Entity(schema);
                    e.Set<string>(schema.GetField(FIELD_NAME), payload);
                    holder.SetEntity(e);
                    t.Commit();
                }
            }
            catch { }
        }

        /// <summary>A setting saved with <see cref="Stamp(Document, string, string, int, string, IDictionary{string, string})"/>, or null.</summary>
        public static string ReadSetting(Document doc, string toolKey, string name)
        {
            try
            {
                Element holder = Holder(doc);
                Schema schema = Schema.Lookup(SCHEMA_GUID);
                if (holder == null || schema == null) return null;

                string value;
                return ReadAll(doc, schema, holder).TryGetValue(SettingKey(toolKey, name), out value) ? value : null;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                // A setting only pre-selects a choice; without it the tool still runs.
                App.Log?.Error("Could not read setting " + name + " of " + toolKey, ex);
                return null;
            }
        }

        private static string SettingKey(string toolKey, string name) => toolKey + ".setting." + name;

        /// <summary>Human-readable last run for one tool, or empty when never run.</summary>
        public static string ReadLastRun(Document doc, string toolKey)
        {
            return ReadLastRun(doc, toolKey, "region(s)");
        }

        /// <summary>Human-readable last run, naming what the count counts ("room(s)", "region(s)").</summary>
        public static string ReadLastRun(Document doc, string toolKey, string countLabel)
        {
            try
            {
                Element holder = Holder(doc);
                if (holder == null) return string.Empty;

                Schema schema = Schema.Lookup(SCHEMA_GUID);
                if (schema == null) return string.Empty;

                Dictionary<string, string> lines = ReadAll(doc, schema, holder);
                string raw;
                if (!lines.TryGetValue(toolKey, out raw)) return string.Empty;

                string[] f = raw.Split(FLD_SEP);
                if (f.Length < 4) return string.Empty;

                return string.Format("Last run: {0} · {1} · {2} · {3} {5}{4}",
                    f[0], f[1], f[2], f[3], f.Length > 4 && f[4].Length > 0 ? " · " + f[4] : string.Empty, countLabel);
            }
            catch { return string.Empty; }
        }

        private static Dictionary<string, string> ReadAll(Document doc, Schema schema, Element holder)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                Entity e = holder.GetEntity(schema);
                if (e == null || !e.IsValid()) return map;

                string payload = e.Get<string>(schema.GetField(FIELD_NAME));
                if (string.IsNullOrEmpty(payload)) return map;

                foreach (string rec in payload.Split(REC_SEP))
                {
                    if (rec.Length == 0) continue;
                    int i = rec.IndexOf(FLD_SEP);
                    if (i <= 0) continue;
                    map[rec.Substring(0, i)] = rec.Substring(i + 1);
                }
            }
            catch { }
            return map;
        }
    }
}
