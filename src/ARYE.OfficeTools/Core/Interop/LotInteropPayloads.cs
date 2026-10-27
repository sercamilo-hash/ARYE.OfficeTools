using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ARYE.OfficeTools.Core.Interop
{
    /// <summary>
    /// JSON contract shared with TopoGenerator ("Align to Lot"). See
    /// docs/CONTRATO-LOT-TOPO.md — field names here ARE the contract. Add
    /// fields freely; never rename or remove one; bump SchemaVersion when a
    /// reader must behave differently.
    /// </summary>
    public static class LotInteropContract
    {
        public const int SchemaVersion = 1;

        public const string FaceSchemaGuid = "AFBDF6F7-4C2D-467C-9B7A-C48E8CCCF5A4";
        public const string FaceSchemaName = "BauToolsLotFace";

        public const string RecordSchemaGuid = "F025D831-95C1-4AA6-9888-D038B416CC73";
        public const string RecordSchemaName = "BauToolsLotRecord";

        public const string VendorId = "ARYE";

        public const string FieldSchemaVersion = "SchemaVersion";
        public const string FieldPayloadJson = "PayloadJson";

        public const string LineStylePropertyLine = "BauTools - Property Line";
        public const string LineStyleAdjacentLot = "BauTools - Adjacent Lot";
        public const string LineStyleBlock = "BauTools - Block";
        public const string LineStyleSidewalk = "BauTools - Sidewalk";
        public const string LineStyleOtherBlock = "BauTools - Other Block";
        public const string LineStyleRoadbed = "BauTools - Roadbed";
        public const string LineStyleRealSidewalk = "BauTools - Sidewalk (Real)";
        public const string LineStyleCurb = "BauTools - Curb";

        public const string FaceRoleStreet = "Street";
        public const string FaceRoleInterior = "Interior";
        public const string FaceRoleUnknown = "Unknown";

        public static string DataStorageName(string bbl) => "BauTools Lot " + bbl;

        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
    }

    public sealed class InteropPoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    /// <summary>Attached to every Model Line of the subject lot.</summary>
    public sealed class LotFacePayload
    {
        public string RunId { get; set; } = string.Empty;

        public string Bbl { get; set; } = string.Empty;

        public string LotUuid { get; set; } = string.Empty;

        public int FaceIndex { get; set; }

        public int FaceCount { get; set; }

        /// <summary>Street | Interior | Unknown.</summary>
        public string FaceRole { get; set; } = LotInteropContract.FaceRoleUnknown;

        public string StreetName { get; set; } = string.Empty;

        public double LegalLengthFt { get; set; }

        public double DigitizedLengthFt { get; set; }

        public double DrawnLengthFt { get; set; }

        /// <summary>DTM | TaxMapLegal | Survey | Manual.</summary>
        public string GeometrySource { get; set; } = string.Empty;

        public InteropPoint StartStatePlane { get; set; } = new InteropPoint();

        public InteropPoint EndStatePlane { get; set; } = new InteropPoint();

        public InteropPoint StartModel { get; set; } = new InteropPoint();

        public InteropPoint EndModel { get; set; } = new InteropPoint();

        public string ToJson() => JsonSerializer.Serialize(this, LotInteropContract.JsonOptions);

        public static LotFacePayload FromJson(string json) =>
            JsonSerializer.Deserialize<LotFacePayload>(json, LotInteropContract.JsonOptions);
    }

    public sealed class LotAnchorPayload
    {
        public string Corner { get; set; } = string.Empty;

        public InteropPoint Model { get; set; } = new InteropPoint();

        public InteropPoint StatePlane { get; set; } = new InteropPoint();
    }

    public sealed class ModelToStatePlanePayload
    {
        public double OffsetX { get; set; }

        public double OffsetY { get; set; }

        public double RotationDeg { get; set; }
    }

    public sealed class LotDimensionsPayload
    {
        public double FrontFt { get; set; }

        public double DepthFt { get; set; }

        public double AreaSqFt { get; set; }
    }

    public sealed class LotFaceLengthsPayload
    {
        public List<double> Faces { get; set; } = new List<double>();

        public double AreaSqFt { get; set; }
    }

    /// <summary>One per drawn lot, on a DataStorage element named "BauTools Lot &lt;BBL&gt;".</summary>
    public sealed class LotRecordPayload
    {
        public string RunId { get; set; } = string.Empty;

        public string DrawnUtc { get; set; } = string.Empty;

        public string BauToolsVersion { get; set; } = string.Empty;

        public string Bbl { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public string Borough { get; set; } = string.Empty;

        public int Block { get; set; }

        public int Lot { get; set; }

        public bool IsCornerLot { get; set; }

        public string GeometrySource { get; set; } = string.Empty;

        public long LevelId { get; set; }

        public string LevelName { get; set; } = string.Empty;

        public double LevelElevationFt { get; set; }

        public LotAnchorPayload Anchor { get; set; } = new LotAnchorPayload();

        public ModelToStatePlanePayload ModelToStatePlane { get; set; } = new ModelToStatePlanePayload();

        public LotDimensionsPayload Recorded { get; set; } = new LotDimensionsPayload();

        public LotFaceLengthsPayload Legal { get; set; } = new LotFaceLengthsPayload();

        public LotFaceLengthsPayload Digitized { get; set; } = new LotFaceLengthsPayload();

        public List<long> FaceElementIds { get; set; } = new List<long>();

        public double SidewalkWidthFt { get; set; }

        /// <summary>Default | User | DigitalCityMap.</summary>
        public string SidewalkWidthSource { get; set; } = "Default";

        public List<string> ZoningLotBbls { get; set; } = new List<string>();

        public string ToJson() => JsonSerializer.Serialize(this, LotInteropContract.JsonOptions);

        public static LotRecordPayload FromJson(string json) =>
            JsonSerializer.Deserialize<LotRecordPayload>(json, LotInteropContract.JsonOptions);
    }
}
