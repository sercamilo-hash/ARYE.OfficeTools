using System.Collections.Generic;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Core.Nyc;

namespace ARYE.OfficeTools.Services.Nyc
{
    public enum LotAnchorCorner { Southwest, Northwest, Southeast, Northeast, Center }

    /// <summary>Element type for CONTEXT geometry (neighbors, block, sidewalk). The subject lot is always Model Lines.</summary>
    public enum LotElementType { ModelCurves, DetailLines, AreaBoundaryLines }

    public enum LotGroupingMode { SingleGroup, SplitSubjectAndContext, NoGrouping }

    /// <summary>Everything a draw run needs, resolved before the transaction opens.</summary>
    public sealed class NycLotDrawOptions
    {
        public ElementId LevelId { get; set; }

        public LotAnchorCorner AnchorCorner { get; set; } = LotAnchorCorner.Southwest;

        /// <summary>Where the subject lot's anchor vertex (SubjectRing[0]) lands in the Revit model.</summary>
        public XYZ AnchorPointInModel { get; set; } = XYZ.Zero;

        /// <summary>Which source the SUBJECT geometry came from. Chosen explicitly by the user, never defaulted.</summary>
        public LotGeometrySource GeometrySource { get; set; } = LotGeometrySource.Dtm;

        /// <summary>
        /// Subject lot faces, counter-clockwise, face 0 starting at the anchor vertex.
        /// Carry the legal/digitized lengths and street flags that get stamped on each drawn line.
        /// </summary>
        public List<NycLotFace> SubjectFaces { get; set; } = new List<NycLotFace>();

        /// <summary>
        /// The geometry actually drawn for the subject (EPSG 2263 ft): face i runs
        /// SubjectRing[i] → SubjectRing[i+1]. Must have exactly SubjectFaces.Count vertices.
        /// </summary>
        public List<LotPoint> SubjectRing { get; set; } = new List<LotPoint>();

        /// <summary>Optional street name per face index (empty string when none).</summary>
        public List<string> FaceStreetNames { get; set; } = new List<string>();

        public LotElementType ElementType { get; set; } = LotElementType.ModelCurves;

        public LotGroupingMode GroupingMode { get; set; } = LotGroupingMode.SingleGroup;

        public bool DrawAdjacentLots { get; set; } = true;

        public bool DrawRemainingBlock { get; set; } = true;

        public bool DrawSidewalk { get; set; } = true;

        public double SidewalkWidthFt { get; set; } = 12.0;

        /// <summary>Default | User | DigitalCityMap — where the sidewalk width came from.</summary>
        public string SidewalkWidthSource { get; set; } = "Default";

        public bool Draw3DMasses { get; set; } = true;

        public bool DrawSubjectMass { get; set; }

        /// <summary>
        /// True (default): each mass extrudes a real building footprint (several
        /// masses on a lot with several buildings; a lot with none falls back to
        /// its own drawn ring). False: one mass per lot on its drawn ring, ignoring footprints.
        /// </summary>
        public bool MassUseFootprints { get; set; } = true;

        /// <summary>
        /// Off by default: a mass the user has re-heighted or reshaped since the
        /// last run is left alone and reported as kept, never silently redrawn.
        /// </summary>
        public bool ReplaceEditedMasses { get; set; }

        public bool DrawStreetLabels { get; set; } = true;

        /// <summary>The 4 names the user confirmed/edited in the window — never placed unconfirmed.</summary>
        public List<StreetFrontage> ConfirmedFrontages { get; set; } = new List<StreetFrontage>();

        public bool DrawZoningTable { get; set; } = true;

        /// <summary>BBLs of a recorded Zoning Lot Description that includes the subject, when known.</summary>
        public List<string> ZoningLotBbls { get; set; } = new List<string>();

        /// <summary>
        /// Per-lot DTM/Legal choice for every entry in NycBlockContext.OtherLots
        /// (keyed by BBL) — the window's "2b · Other lots" list. A lot missing
        /// from this dictionary, or resolved to None, draws its DTM ring.
        /// </summary>
        public Dictionary<string, LotSourceChoice> BlockLotSources { get; set; } = new Dictionary<string, LotSourceChoice>();

        /// <summary>Legal rings resolved for block lots (keyed by BBL), for the lots whose Chosen source is Legal.</summary>
        public Dictionary<string, List<LotPoint>> BlockLotLegalRings { get; set; } = new Dictionary<string, List<LotPoint>>();

        /// <summary>A stable run identity stamped on every mass, so a later run can tell "created by this add-in" from anything else.</summary>
        public string RunId { get; set; } = System.Guid.NewGuid().ToString("N");

        /// <summary>
        /// Whole other blocks added as far urban context — across a street from
        /// the subject's own block, not one of its neighbors. Always drawn from
        /// DTM (never Legal, never Survey): this is massing context, not a
        /// TopoGenerator alignment target.
        /// </summary>
        public List<NycAdditionalBlock> AdditionalBlocks { get; set; } = new List<NycAdditionalBlock>();

        // ── Ground context layers — DOF 2022 planimetric survey + NYC Parks Tree Census.
        // Off by default; the data is fetched by the window ahead of Draw and handed in
        // here already resolved, so Draw itself never makes a network call. ──────────

        public bool DrawRoadbeds { get; set; }

        public List<NycGroundPolygon> Roadbeds { get; set; } = new List<NycGroundPolygon>();

        public bool DrawRealSidewalks { get; set; }

        public List<NycGroundPolygon> RealSidewalks { get; set; } = new List<NycGroundPolygon>();

        public bool DrawCurbs { get; set; }

        public List<NycCurbLine> Curbs { get; set; } = new List<NycCurbLine>();

        public bool DrawContextTrees { get; set; }

        public List<NycStreetTree> ContextTrees { get; set; } = new List<NycStreetTree>();
    }
}
