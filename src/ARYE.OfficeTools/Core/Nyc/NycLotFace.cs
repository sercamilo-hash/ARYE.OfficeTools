using System;
using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>
    /// Where the geometry that gets drawn in Revit came from. Stored with every
    /// drawn lot so nobody later reads a tax-map polygon as a survey.
    /// </summary>
    public enum LotGeometrySource
    {
        /// <summary>DOF Digital Tax Map polygon exactly as digitized (not survey-grade).</summary>
        Dtm = 0,

        /// <summary>Lot rebuilt from the legal face lengths drawn on the official tax map, on DTM bearings.</summary>
        TaxMapLegal = 1,

        /// <summary>Copied from a surveyor's property line already placed in the model.</summary>
        Survey = 2,

        /// <summary>Typed in by the user.</summary>
        Manual = 3,

        /// <summary>LotFront × LotDepth from the assessment record, as a rectangle on the frontage bearing. Regular lots only.</summary>
        Recorded = 4
    }

    /// <summary>
    /// One side of a tax lot as published in DOF's TAX_LOT_FACE layer: the legal
    /// length written on the official tax map sheet, the digitized length of the
    /// same line, and whether the side fronts a street. Plain data, no Revit.
    /// </summary>
    public sealed class NycLotFace
    {
        public string Bbl { get; set; } = string.Empty;

        /// <summary>DOF's LOT_UUID — stable key of the lot geometry across daily updates.</summary>
        public string LotUuid { get; set; } = string.Empty;

        /// <summary>LOT_FACE_LENGTH: the dimension drawn on the tax map. 0 when the sheet has none.</summary>
        public double LegalLengthFt { get; set; }

        /// <summary>DOF's own 'Distance' attribute (digitized length). Cross-checked against the path.</summary>
        public double DigitizedLengthFt { get; set; }

        /// <summary>DOF's 'Direction': bearing in degrees clockwise from north.</summary>
        public double BearingDeg { get; set; }

        /// <summary>BLOCK_FACE_FLAG = 1 — this side is on the block perimeter, i.e. it fronts a street.</summary>
        public bool IsStreetFace { get; set; }

        /// <summary>Digitized vertices of the face, EPSG 2263 feet. At least two.</summary>
        public List<LotPoint> Path { get; set; } = new List<LotPoint>();

        public LotPoint Start => Path.Count > 0 ? Path[0] : default;

        public LotPoint End => Path.Count > 0 ? Path[Path.Count - 1] : default;

        /// <summary>Length measured along the digitized path — what the polygon actually spans.</summary>
        public double MeasuredLengthFt
        {
            get
            {
                double total = 0;
                for (int i = 0; i < Path.Count - 1; i++)
                {
                    total += Path[i].DistanceTo(Path[i + 1]);
                }
                return total;
            }
        }

        /// <summary>Unit direction from Start to End, or (0,0) for a degenerate face.</summary>
        public LotPoint Direction
        {
            get
            {
                double dx = End.X - Start.X;
                double dy = End.Y - Start.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                return len < 1e-9 ? new LotPoint(0, 0) : new LotPoint(dx / len, dy / len);
            }
        }

        /// <summary>Returns a copy walking the same line the other way.</summary>
        public NycLotFace Reversed()
        {
            var copy = Clone();
            copy.Path.Reverse();
            copy.BearingDeg = (BearingDeg + 180.0) % 360.0;
            return copy;
        }

        public NycLotFace Clone() => new NycLotFace
        {
            Bbl = Bbl,
            LotUuid = LotUuid,
            LegalLengthFt = LegalLengthFt,
            DigitizedLengthFt = DigitizedLengthFt,
            BearingDeg = BearingDeg,
            IsStreetFace = IsStreetFace,
            Path = new List<LotPoint>(Path)
        };
    }
}
