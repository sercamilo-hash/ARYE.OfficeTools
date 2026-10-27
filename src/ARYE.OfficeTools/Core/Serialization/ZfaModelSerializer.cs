using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.Core.Serialization
{
    /// <summary>
    /// JSON persistence for ZfaProjectModel. A failed read surfaces as
    /// SerializationException with the real reason — never a silent blank model.
    /// </summary>
    public static class ZfaModelSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string Serialize(ZfaProjectModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            model.SchemaVersion = ZfaProjectModel.CurrentSchemaVersion;
            return JsonSerializer.Serialize(model, Options);
        }

        /// <summary>
        /// Deserializes a payload written by any past version: the migrator
        /// upconverts older shapes before the typed read.
        /// </summary>
        public static ZfaProjectModel Deserialize(string json, int storedSchemaVersion)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ZfaSerializationException("Stored payload is empty.");
            }

            string current;
            try
            {
                current = ZfaModelMigrator.MigrateToCurrent(json, storedSchemaVersion);
            }
            catch (JsonException ex)
            {
                throw new ZfaSerializationException(
                    $"Stored payload (version {storedSchemaVersion}) is not valid JSON: {ex.Message}", ex);
            }

            try
            {
                ZfaProjectModel model = JsonSerializer.Deserialize<ZfaProjectModel>(current, Options);
                if (model == null)
                {
                    throw new ZfaSerializationException("Stored payload deserialized to null.");
                }
                model.SchemaVersion = ZfaProjectModel.CurrentSchemaVersion;
                return model;
            }
            catch (JsonException ex)
            {
                throw new ZfaSerializationException(
                    $"Stored payload (version {storedSchemaVersion}) does not match the model: {ex.Message}", ex);
            }
        }
    }

    public sealed class ZfaSerializationException : Exception
    {
        public ZfaSerializationException(string message) : base(message) { }

        public ZfaSerializationException(string message, Exception inner) : base(message, inner) { }
    }
}
