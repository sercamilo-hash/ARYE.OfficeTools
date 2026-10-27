#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARYE.OfficeTools.Core.Nyc;
using ARYE.OfficeTools.Core.Results;
using ARYE.OfficeTools.Services.Nyc;
using ARYE.OfficeTools.Views.Nyc;
// Autodesk.Revit.UI / DB declare their own TextBox/ComboBox/Grid/Color — alias to WPF's.
using Color = System.Windows.Media.Color;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Panel = System.Windows.Controls.Panel;

namespace ARYE.OfficeTools.Views
{
    /// <summary>
    /// BauTools — NYC Lot Boundary (Step 1 · Site). Left: a plan viewer with
    /// every dimension source overlaid (DTM polygon, legal tax-map rebuild,
    /// assessment rectangle) and the source cards; nothing is preselected and
    /// Draw stays disabled until the user picks what gets drawn. Right: find
    /// the lot, the per-face table with editable street names, draw options.
    /// The subject lot is always drawn as stamped Model Lines (TopoGenerator
    /// handoff). One Revit transaction, true per-element result.
    /// </summary>
    public sealed class NycLotBoundaryWindow : Window
    {
        private static readonly Color ColBg = (Color)ColorConverter.ConvertFromString("#F1F5F9");
        private static readonly Color ColDark = (Color)ColorConverter.ConvertFromString("#0F172A");
        private static readonly Color ColAccent = (Color)ColorConverter.ConvertFromString("#0071E3");
        private static readonly Color ColMuted = (Color)ColorConverter.ConvertFromString("#64748B");
        private static readonly Color ColBorder = (Color)ColorConverter.ConvertFromString("#CBD5E1");
        private static readonly Color ColHeaderBg = (Color)ColorConverter.ConvertFromString("#1E293B");
        private static readonly Color ColWarn = (Color)ColorConverter.ConvertFromString("#B45309");
        private static readonly Color ColOk = (Color)ColorConverter.ConvertFromString("#047857");
        private static readonly Color ColDtm = (Color)ColorConverter.ConvertFromString("#6B7280");
        private static readonly Color ColLegal = (Color)ColorConverter.ConvertFromString("#C81010");
        private static readonly Color ColRecorded = (Color)ColorConverter.ConvertFromString("#1D4ED8");
        private static readonly Color ColSidewalk = (Color)ColorConverter.ConvertFromString("#108C3C");
        private static readonly Color ColNeighbor = (Color)ColorConverter.ConvertFromString("#9CA3AF");
        private static readonly Color ColAdjacentLegal = (Color)ColorConverter.ConvertFromString("#E0A0A0");
        private static readonly Color ColFootprintReal = (Color)ColorConverter.ConvertFromString("#EA580C");
        private static readonly Color ColFootprintFallback = (Color)ColorConverter.ConvertFromString("#FDBA74");

        private readonly Document _doc;
        private readonly NycGeoSearchService _geoSearch = new NycGeoSearchService();
        private readonly NycBuildingFootprintService _footprintService = new NycBuildingFootprintService();
        private readonly NycTaxLotFaceService _faceService = new NycTaxLotFaceService();
        private readonly NycStreetWidthService _streetWidthService = new NycStreetWidthService();
        private readonly NycLotDrawService _drawService = new NycLotDrawService();
        private readonly NycBlockLotSourceService _blockLotSourceService = new NycBlockLotSourceService();
        private readonly NycContextLayerService _contextLayerService = new NycContextLayerService();

        // ── Loaded data ──────────────────────────────────────────────────
        private NycBlockContext _context;
        private List<NycBuildingFootprint> _footprints = new List<NycBuildingFootprint>();
        private List<NycLotFace> _orderedFaces = new List<NycLotFace>();   // CCW, DOF order
        private List<NycLotFace> _anchoredFaces = new List<NycLotFace>();  // rotated so face 0 starts at the anchor
        private LotReconstructionResult _legal;
        private List<LotPoint> _recordedRing;
        private List<MappedStreet> _mappedStreets = new List<MappedStreet>();
        private List<NycGroundPolygon> _roadbeds = new List<NycGroundPolygon>();
        private List<NycGroundPolygon> _realSidewalks = new List<NycGroundPolygon>();
        private List<NycCurbLine> _curbs = new List<NycCurbLine>();
        private List<NycStreetTree> _contextTrees = new List<NycStreetTree>();
        private string _faceSourceNote = string.Empty;
        private LotGeometrySource? _selectedSource;

        // ── Survey source: property lines picked in the model, converted to State Plane ──
        private List<NycLotFace> _surveyFaces;      // CCW, face 0 at the anchor; roles/legal lengths inherited from DTM faces
        private List<LotPoint> _surveyRing;
        private int[] _surveyMatches;               // survey face index → anchored DTM face index (-1 none)
        private string _surveyNote = string.Empty;
        private Button _btnPickSurvey;

        // ── Other lots on the block: same DTM/Legal choice as the subject, one per lot ──
        private List<BlockLotSourceResolution> _blockLotSources = new List<BlockLotSourceResolution>();
        private string _highlightedBlockLotBbl;
        private StackPanel _blockLotsAdjacentPanel;
        private StackPanel _blockLotsRemainingPanel;
        private Expander _blockLotsRemainingExpander;
        private Border _blockLotsProgressFill;
        private TextBlock _blockLotsProgressLabel;
        private readonly Dictionary<string, (Button Dtm, Button Legal, Border Row)> _blockLotRowControls =
            new Dictionary<string, (Button, Button, Border)>();

        // ── Additional blocks: whole other blocks across a street, far urban context ──
        private readonly List<NycAdditionalBlock> _additionalBlocks = new List<NycAdditionalBlock>();
        private ComboBox _cmbAddBlockBorough;
        private TextBox _txtAddBlockNumber;
        private TextBox _txtAddBlockAddress;
        private StackPanel _additionalBlocksPanel;
        private TextBlock _additionalBlocksStatus;

        /// <summary>
        /// Set by the window when the user asks to pick property lines; the
        /// command sees ShowDialog return, picks in Revit, calls ApplySurveyPick
        /// and shows this same window again.
        /// </summary>
        public bool SurveyPickRequested { get; set; }

        // ── Controls ─────────────────────────────────────────────────────
        private TextBox _txtQuery;
        private ListBox _lstResults;
        private WrapPanel _chips;
        private TextBlock _lblSourcesNote;
        private Grid _facesGrid;
        private readonly Dictionary<int, TextBox> _faceNameBoxes = new Dictionary<int, TextBox>();
        private readonly List<Border> _faceRows = new List<Border>();
        private LotViewerControl _viewer;
        private CheckBox _chkDtm, _chkLegal, _chkRecorded, _chkSurvey, _chkNeighbors, _chkSidewalkPreview, _chkDims, _chkFootprintsPreview;
        private TextBlock _legend;
        private readonly Dictionary<LotGeometrySource, RadioButton> _sourceRadios = new Dictionary<LotGeometrySource, RadioButton>();
        private readonly Dictionary<LotGeometrySource, Border> _sourceCards = new Dictionary<LotGeometrySource, Border>();
        private readonly Dictionary<LotGeometrySource, TextBlock> _sourceDims = new Dictionary<LotGeometrySource, TextBlock>();
        private readonly Dictionary<LotGeometrySource, TextBlock> _sourceNotes = new Dictionary<LotGeometrySource, TextBlock>();
        private ComboBox _cmbLevel, _cmbAnchor, _cmbElementType, _cmbGrouping;
        private TextBox _txtSidewalkWidth;
        private TextBlock _lblSidewalkNote;
        private CheckBox _chkAdjacent, _chkBlock, _chkSidewalk, _chkMasses, _chkSubjectMass, _chkStreetLabels, _chkZoningTable;
        private CheckBox _chkRoadbeds, _chkRealSidewalks, _chkCurbs, _chkContextTrees;
        private RadioButton _rbMassFootprint, _rbMassLotRing;
        private CheckBox _chkReplaceEditedMasses;
        private TextBlock _statusText;
        private Button _btnDraw;

        public NycLotBoundaryWindow(Document doc)
        {
            _doc = doc;
            Title = "BauTools — NYC Lot Boundary";
            Width = 1280;
            Height = 840;
            MinWidth = 1100;
            MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(ColBg);
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            BuildUi();
        }

        // ═════════════════════════════ UI ═════════════════════════════

        private void BuildUi()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var header = new Border { Background = new SolidColorBrush(ColHeaderBg), Padding = new Thickness(22, 12, 22, 12) };
            var headerPanel = new StackPanel();
            headerPanel.Children.Add(new TextBlock { Text = "BauTools — NYC Lot Boundary", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            headerPanel.Children.Add(new TextBlock
            {
                Text = "Step 1 · Site.  Find the lot, compare every dimension source, choose what gets drawn, confirm street names, draw.",
                FontSize = 11, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), Margin = new Thickness(0, 4, 0, 0)
            });
            header.Child = headerPanel;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Body: 58 / 42
            var body = new Grid { Margin = new Thickness(12, 12, 12, 0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42, GridUnitType.Star) });

            var left = BuildLeftColumn();
            Grid.SetColumn(left, 0);
            body.Children.Add(left);

            var rightScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 0, 0, 0) };
            rightScroll.Content = BuildRightColumn();
            Grid.SetColumn(rightScroll, 1);
            body.Children.Add(rightScroll);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            // Footer
            var footer = new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")), Padding = new Thickness(20, 12, 20, 12), Margin = new Thickness(0, 10, 0, 0) };
            var footGrid = new Grid();
            footGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _statusText = new TextBlock { Text = "Search for a lot to begin.", VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(ColDark), TextWrapping = TextWrapping.Wrap };
            footGrid.Children.Add(_statusText);
            var footButtons = new StackPanel { Orientation = Orientation.Horizontal };
            var btnClose = new Button { Content = "Close", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 10, 0) };
            btnClose.Click += (s, e) => Close();
            footButtons.Children.Add(btnClose);
            _btnDraw = new Button
            {
                Content = "Draw in Revit", Padding = new Thickness(20, 8, 20, 8), Background = new SolidColorBrush(ColAccent),
                Foreground = Brushes.White, FontWeight = FontWeights.Bold, IsEnabled = false,
                ToolTip = "Enabled once a lot is loaded AND a geometry source is chosen."
            };
            _btnDraw.Click += (s, e) => OnDraw();
            footButtons.Children.Add(_btnDraw);
            Grid.SetColumn(footButtons, 1);
            footGrid.Children.Add(footButtons);
            footer.Child = footGrid;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        private UIElement BuildLeftColumn()
        {
            var card = Card();
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // title + toolbar
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // viewer
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // sources

            var top = new StackPanel();
            top.Children.Add(Label("LOT VIEWER"));
            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            _chkDtm = Toggle(toolbar, "DTM polygon", ColDtm, true, "DOF Digital Tax Map polygon, exactly as digitized. Not survey-grade.");
            _chkLegal = Toggle(toolbar, "Tax-map legal", ColLegal, true, "Lot rebuilt from the dimensions written on the official tax map, on DTM bearings.");
            _chkRecorded = Toggle(toolbar, "Recorded", ColRecorded, true, "LotFront × LotDepth from the assessment record as a rectangle on the frontage. Regular lots only.");
            _chkSurvey = Toggle(toolbar, "Survey", (Color)ColorConverter.ConvertFromString("#7C3AED"), false, "Property lines picked in the model (e.g. placed by TopoGenerator). Pick them in the Survey card.");
            _chkSurvey.IsEnabled = false;
            _chkNeighbors = Toggle(toolbar, "Neighbors", ColNeighbor, true, "Adjacent lots and the rest of the block (DTM geometry).");
            _chkFootprintsPreview = Toggle(toolbar, "Built (footprints)", ColFootprintReal, true,
                "NYC Building Footprints matched to these lots — the same match the 3D masses use. Solid orange = matched by BBL. Pale orange = the BBL join was empty and the match came from the footprint's position instead.");
            _chkSidewalkPreview = Toggle(toolbar, "Sidewalk", ColSidewalk, true, "Outward offset of the CHOSEN geometry by the sidewalk width.");
            _chkDims = Toggle(toolbar, "Dimensions", ColDark, true, "Legal / DTM length on every face.");
            var btnFit = new Button { Content = "Fit", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(12, 0, 0, 0), ToolTip = "Zoom to the subject lot" };
            btnFit.Click += (s, e) => FitViewer();
            toolbar.Children.Add(btnFit);
            top.Children.Add(toolbar);
            Grid.SetRow(top, 0);
            panel.Children.Add(top);

            var viewerBorder = new Border { BorderBrush = new SolidColorBrush(ColBorder), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), MinHeight = 300 };
            var viewerGrid = new Grid();
            _viewer = new LotViewerControl();
            _viewer.FaceClicked += index => HighlightFace(index, true);
            viewerGrid.Children.Add(_viewer);
            _legend = new TextBlock
            {
                FontSize = 11, Foreground = new SolidColorBrush(ColDark), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8), Padding = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                Text = "Load a lot to see it here.\nwheel = zoom · drag = pan · click a face = highlight its row"
            };
            viewerGrid.Children.Add(_legend);
            viewerBorder.Child = viewerGrid;
            Grid.SetRow(viewerBorder, 1);
            panel.Children.Add(viewerBorder);

            var sources = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var srcTitle = new TextBlock { Margin = new Thickness(0, 0, 0, 6) };
            srcTitle.Inlines.Add(new System.Windows.Documents.Run("CHOOSE WHAT GETS DRAWN IN REVIT") { FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = new SolidColorBrush(ColDark) });
            srcTitle.Inlines.Add(new System.Windows.Documents.Run("  — nothing is preselected; Draw stays disabled until you pick") { FontSize = 11, Foreground = new SolidColorBrush(ColMuted) });
            sources.Children.Add(srcTitle);
            var cards = new Grid();
            for (int i = 0; i < 4; i++) cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cards.Children.Add(SourceCard(LotGeometrySource.TaxMapLegal, "Tax-map legal", ColLegal, 0,
                "Rebuilds the lot from the dimensions written on the official tax map sheet, using DTM bearings. Closure error is distributed along the perimeter (compass rule)."));
            cards.Children.Add(SourceCard(LotGeometrySource.Dtm, "DTM polygon", ColDtm, 1,
                "DOF's digitized polygon exactly as served. Fits the neighbors' geometry exactly; its street lines can sit several feet off."));
            cards.Children.Add(SourceCard(LotGeometrySource.Recorded, "Recorded", ColRecorded, 2,
                "LotFront × LotDepth from the assessment record (what the Property Information Portal shows), as a rectangle on the frontage bearing. Regular lots only."));
            cards.Children.Add(SourceCard(LotGeometrySource.Survey, "Survey", (Color)ColorConverter.ConvertFromString("#7C3AED"), 3,
                "Pick property lines already placed in the model (e.g. by TopoGenerator). Not in this version."));
            sources.Children.Add(cards);
            sources.Children.Add(new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF7ED")), BorderBrush = new SolidColorBrush(ColWarn),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 8, 0, 0),
                Child = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C2D12")),
                    Text = "⚠ None of these is a survey. The chosen source is stamped on every drawn line (ExtensibleStorage + Comments) and printed in the zoning table, so nobody later reads a tax-map line as a surveyed property line."
                }
            });
            Grid.SetRow(sources, 2);
            panel.Children.Add(sources);

            card.Child = panel;
            return card;
        }

        private Border SourceCard(LotGeometrySource source, string name, Color color, int column, string tooltip)
        {
            var card = new Border
            {
                BorderBrush = new SolidColorBrush(ColBorder), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(column == 0 ? 0 : 4, 0, column == 3 ? 0 : 4, 0),
                Background = Brushes.White, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tooltip
            };
            var stack = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new Border { Width = 18, Height = 3, Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            title.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 12, Foreground = new SolidColorBrush(ColDark) });
            head.Children.Add(title);
            var radio = new RadioButton { GroupName = "GeometrySource", VerticalAlignment = VerticalAlignment.Top };
            radio.Checked += (s, e) => OnSourceChosen(source);
            Grid.SetColumn(radio, 1);
            head.Children.Add(radio);
            stack.Children.Add(head);
            var dims = new TextBlock { Text = "—", FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 5, 0, 2), Foreground = new SolidColorBrush(ColDark) };
            stack.Children.Add(dims);
            var note = new TextBlock { Text = "Load a lot.", FontSize = 11, Foreground = new SolidColorBrush(ColMuted), TextWrapping = TextWrapping.Wrap };
            stack.Children.Add(note);
            if (source == LotGeometrySource.Survey)
            {
                _btnPickSurvey = new Button
                {
                    Content = "Pick property lines in the model…", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = false, FontSize = 11.5,
                    ToolTip = "Hides this window, lets you pick the surveyed property lines (model or detail lines) in Revit, then comes back. " +
                              "Lines are read where they are — place them first (TopoGenerator “Align to Lot” does exactly that)."
                };
                _btnPickSurvey.Click += (s, e) => RequestSurveyPick();
                stack.Children.Add(_btnPickSurvey);
            }
            card.Child = stack;
            card.MouseLeftButtonUp += (s, e) => { if (radio.IsEnabled) radio.IsChecked = true; };
            Grid.SetColumn(card, column);

            _sourceRadios[source] = radio;
            _sourceCards[source] = card;
            _sourceDims[source] = dims;
            _sourceNotes[source] = note;
            SetSourceAvailable(source, false, source == LotGeometrySource.Survey ? "Pick property lines in the model. Coming with TopoGenerator \"Align to Lot\"." : "Load a lot.");
            return card;
        }

        private void SetSourceAvailable(LotGeometrySource source, bool available, string note)
        {
            _sourceRadios[source].IsEnabled = available;
            _sourceCards[source].Opacity = available ? 1.0 : 0.55;
            _sourceCards[source].Cursor = available ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.No;
            _sourceNotes[source].Text = note;
            if (!available)
            {
                _sourceDims[source].Text = "—";
            }
        }

        private UIElement BuildRightColumn()
        {
            var column = new StackPanel();

            // ── 1 · Find ──
            var findCard = Card();
            var find = new StackPanel();
            var findTitle = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
            findTitle.Inlines.Add(new System.Windows.Documents.Run("1 · FIND A LOT") { FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = new SolidColorBrush(ColDark) });
            findTitle.Inlines.Add(new System.Windows.Documents.Run("  (address or 10-digit BBL)") { FontSize = 11, Foreground = new SolidColorBrush(ColMuted) });
            find.Children.Add(findTitle);
            var searchRow = new Grid();
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _txtQuery = new TextBox { Padding = new Thickness(6), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Street address (e.g. 438 Concord Ave Bronx) or BBL (e.g. 2025770009)" };
            _txtQuery.KeyDown += async (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) await OnSearch(); };
            searchRow.Children.Add(_txtQuery);
            var btnSearch = new Button { Content = "Search", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
            btnSearch.Click += async (s, e) => await OnSearch();
            Grid.SetColumn(btnSearch, 1);
            searchRow.Children.Add(btnSearch);
            var btnLoad = new Button { Content = "Load", Padding = new Thickness(14, 5, 14, 5), Background = new SolidColorBrush(ColAccent), Foreground = Brushes.White, FontWeight = FontWeights.Bold, ToolTip = "Loads the selected result: lot, block, faces, footprints, street widths." };
            btnLoad.Click += async (s, e) => await OnLoad();
            Grid.SetColumn(btnLoad, 2);
            searchRow.Children.Add(btnLoad);
            find.Children.Add(searchRow);
            _lstResults = new ListBox { MaxHeight = 96, Margin = new Thickness(0, 6, 0, 0), DisplayMemberPath = "Label" };
            _lstResults.MouseDoubleClick += async (s, e) => await OnLoad();
            find.Children.Add(_lstResults);
            _chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            find.Children.Add(_chips);
            _lblSourcesNote = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(ColMuted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            find.Children.Add(_lblSourcesNote);
            findCard.Child = find;
            column.Children.Add(findCard);

            // ── 2 · Faces ──
            var facesCard = Card();
            var faces = new StackPanel();
            var facesTitle = new TextBlock { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            facesTitle.Inlines.Add(new System.Windows.Documents.Run("2 · FACES") { FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = new SolidColorBrush(ColDark) });
            facesTitle.Inlines.Add(new System.Windows.Documents.Run("  — counter-clockwise from the anchor; click a row to highlight it in the viewer") { FontSize = 11, Foreground = new SolidColorBrush(ColMuted) });
            faces.Children.Add(facesTitle);
            _facesGrid = new Grid();
            faces.Children.Add(_facesGrid);
            faces.Children.Add(new TextBlock
            {
                FontSize = 11, Foreground = new SolidColorBrush(ColMuted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
                Text = "Legal = LOT_FACE_LENGTH on DOF's Tax Lot Face layer · DTM = digitized line · Δ flagged over 1%. Street names are proposed for street faces and must be confirmed; they are placed as 3D Model Text and stored in each face's record."
            });
            facesCard.Child = faces;
            column.Children.Add(facesCard);

            // ── 2b · Other lots on the block ──
            column.Children.Add(BuildBlockLotsCard());

            // ── 2c · Additional blocks (whole other blocks, far urban context) ──
            column.Children.Add(BuildAdditionalBlocksCard());

            // ── 3 · Draw options ──
            var optCard = Card();
            var opt = new StackPanel();
            opt.Children.Add(Label("3 · DRAW OPTIONS"));
            var rows = new Grid();
            rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 3; i++) rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _cmbLevel = new ComboBox { Margin = new Thickness(6, 0, 12, 6), DisplayMemberPath = "Name", ToolTip = "Sketch plane of the lot lines. Z is TopoGenerator's job." };
            foreach (Level level in new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation)) _cmbLevel.Items.Add(level);
            if (_cmbLevel.Items.Count > 0) _cmbLevel.SelectedIndex = 0;
            AddField(rows, 0, 0, "Level:", _cmbLevel);

            _cmbAnchor = new ComboBox { Margin = new Thickness(6, 0, 0, 6), ToolTip = "Which lot corner lands on the model origin; face 0 starts there." };
            foreach (LotAnchorCorner c in Enum.GetValues(typeof(LotAnchorCorner))) _cmbAnchor.Items.Add(c);
            _cmbAnchor.SelectedIndex = 0;
            _cmbAnchor.SelectionChanged += (s, e) => { RebuildAnchored(); RefreshFacesTable(); RefreshViewer(); };
            AddField(rows, 0, 2, "Anchor corner:", _cmbAnchor);

            _cmbElementType = new ComboBox { Margin = new Thickness(6, 0, 12, 6), ToolTip = "Applies to neighbors, block and sidewalk. The subject lot is always Model Lines." };
            foreach (LotElementType t in Enum.GetValues(typeof(LotElementType))) _cmbElementType.Items.Add(t);
            _cmbElementType.SelectedIndex = 0;
            AddField(rows, 1, 0, "Context as:", _cmbElementType);

            _cmbGrouping = new ComboBox
            {
                Margin = new Thickness(6, 0, 0, 6),
                ToolTip = "How the drawn lines are grouped: SingleGroup puts subject and context in one group, "
                    + "SplitSubjectAndContext makes two, NoGrouping leaves everything loose. "
                    + "The 3D masses always get a group of their own (BauTools Masses — <BBL>), except with NoGrouping."
            };
            foreach (LotGroupingMode m in Enum.GetValues(typeof(LotGroupingMode))) _cmbGrouping.Items.Add(m);
            _cmbGrouping.SelectedIndex = 0;
            AddField(rows, 1, 2, "Grouping:", _cmbGrouping);

            _txtSidewalkWidth = new TextBox { Text = "12", Margin = new Thickness(6, 0, 12, 6), Padding = new Thickness(4), ToolTip = "Offset of the chosen geometry. Default 12 ft when no better data." };
            _txtSidewalkWidth.TextChanged += (s, e) => RefreshViewer();
            AddField(rows, 2, 0, "Sidewalk (ft):", _txtSidewalkWidth);
            _lblSidewalkNote = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(ColMuted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 0, 0, 6), Text = "Default 12 ft. Official street widths appear here after Load." };
            Grid.SetRow(_lblSidewalkNote, 2); Grid.SetColumn(_lblSidewalkNote, 2); Grid.SetColumnSpan(_lblSidewalkNote, 2);
            rows.Children.Add(_lblSidewalkNote);
            opt.Children.Add(rows);

            var checks = new WrapPanel();
            _chkAdjacent = Check("Adjacent lots", true);
            _chkBlock = Check("Rest of the block", true);
            _chkSidewalk = Check("Sidewalk", true);
            _chkMasses = Check("3D context masses", true);
            _chkSubjectMass = Check("Subject mass", false);
            _chkStreetLabels = Check("Street labels (3D Model Text)", true);
            _chkZoningTable = Check("Zoning summary table", true);
            _chkRoadbeds = Check("Roads (real roadbed)", false);
            _chkRoadbeds.ToolTip = "DOF 2022 planimetric survey — the actual paved street surface, not just the centerline.";
            _chkRealSidewalks = Check("Sidewalks (real)", false);
            _chkRealSidewalks.ToolTip = "DOF 2022 planimetric survey — real sidewalk polygons from aerial survey, separate from the 'Sidewalk' offset above.";
            _chkCurbs = Check("Curbs", false);
            _chkCurbs.ToolTip = "DOF 2022 planimetric survey — the curb line between sidewalk and roadbed.";
            _chkContextTrees = Check("Street trees", false);
            _chkContextTrees.ToolTip = "NYC Parks 2025 Tree Census (in progress) — placed as instances of a loaded Planting family; skipped with a reason if none is loaded.";
            foreach (CheckBox c in new[] { _chkAdjacent, _chkBlock, _chkSidewalk, _chkMasses, _chkSubjectMass, _chkStreetLabels, _chkZoningTable,
                _chkRoadbeds, _chkRealSidewalks, _chkCurbs, _chkContextTrees }) checks.Children.Add(c);
            opt.Children.Add(checks);

            var massSub = new StackPanel { Margin = new Thickness(22, 2, 0, 0) };
            var massSubRow = new StackPanel { Orientation = Orientation.Horizontal };
            _rbMassFootprint = new RadioButton
            {
                Content = "Use each building's real footprint", GroupName = "massFootprint", IsChecked = true,
                Margin = new Thickness(0, 0, 16, 0), FontSize = 11.5,
                ToolTip = "Recommended. One mass per matching Building Footprint (BIN); a lot with none matched falls back to its own drawn ring, labeled accordingly."
            };
            _rbMassLotRing = new RadioButton
            {
                Content = "Use the whole lot outline", GroupName = "massFootprint", FontSize = 11.5,
                ToolTip = "One mass per lot on the ring drawn for that lot, ignoring footprints."
            };
            massSubRow.Children.Add(_rbMassFootprint);
            massSubRow.Children.Add(_rbMassLotRing);
            massSub.Children.Add(massSubRow);
            _chkReplaceEditedMasses = new CheckBox
            {
                Content = "Overwrite masses I've already reshaped", IsChecked = false, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0),
                ToolTip = "Off by default: a mass whose height or volume differs from what BauTools last stamped is left alone and reported as kept."
            };
            massSub.Children.Add(_chkReplaceEditedMasses);
            massSub.Children.Add(new TextBlock
            {
                FontSize = 10.5, Foreground = new SolidColorBrush(ColMuted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
                Text = "Masses are real editable families ('BauTools Mass — …', category Generic Models) — drag the height in Properties or open the family to reshape the plan."
            });
            opt.Children.Add(massSub);

            opt.Children.Add(new TextBlock
            {
                FontSize = 11, Foreground = new SolidColorBrush(ColMuted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
                Text = "Subject lot: always Model Lines · line style 'BauTools - Property Line' · one interop record per face + one lot record for TopoGenerator \"Align to Lot\"."
            });
            optCard.Child = opt;
            column.Children.Add(optCard);

            return column;
        }

        private static void AddField(Grid grid, int row, int column, string label, UIElement control)
        {
            var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(tb, row); Grid.SetColumn(tb, column);
            grid.Children.Add(tb);
            Grid.SetRow(control, row); Grid.SetColumn(control, column + 1);
            grid.Children.Add(control);
        }

        private CheckBox Toggle(Panel into, string text, Color color, bool checkedState, string tooltip)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
            var cb = new CheckBox { IsChecked = checkedState, VerticalAlignment = VerticalAlignment.Center, ToolTip = tooltip };
            cb.Checked += (s, e) => RefreshViewer();
            cb.Unchecked += (s, e) => RefreshViewer();
            panel.Children.Add(cb);
            panel.Children.Add(new Border { Width = 18, Height = 3, Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
            panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, ToolTip = tooltip });
            into.Children.Add(panel);
            return cb;
        }

        private static Border Card() => new Border
        {
            Background = Brushes.White, BorderBrush = new SolidColorBrush(ColBorder), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(14, 12, 14, 12)
        };

        private static TextBlock Label(string text) => new TextBlock
        {
            Text = text, FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = new SolidColorBrush(ColDark), Margin = new Thickness(0, 0, 0, 8)
        };

        private static CheckBox Check(string content, bool isChecked) =>
            new CheckBox { Content = content, IsChecked = isChecked, Margin = new Thickness(0, 4, 18, 4) };

        private Border Chip(string text, string bold = null, string tooltip = null)
        {
            var tb = new TextBlock { FontSize = 11.5 };
            if (bold != null) tb.Inlines.Add(new System.Windows.Documents.Run(bold) { FontWeight = FontWeights.Bold });
            tb.Inlines.Add(new System.Windows.Documents.Run(text));
            return new Border
            {
                Background = new SolidColorBrush(ColBg), BorderBrush = new SolidColorBrush(ColBorder), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(9, 2, 9, 2), Margin = new Thickness(0, 0, 6, 6), Child = tb, ToolTip = tooltip
            };
        }

        // ═══════════════════════ Other lots on the block ═══════════════════════

        private UIElement BuildBlockLotsCard()
        {
            var card = Card();
            var root = new StackPanel();
            var title = new TextBlock { Margin = new Thickness(0, 0, 0, 2), TextWrapping = TextWrapping.Wrap };
            title.Inlines.Add(new System.Windows.Documents.Run("2b · OTHER LOTS ON THE BLOCK") { FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = new SolidColorBrush(ColDark) });
            root.Children.Add(title);
            root.Children.Add(new TextBlock
            {
                FontSize = 11, Foreground = new SolidColorBrush(ColMuted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
                Text = "Pick which measurement to trust for each lot — same choice as your own. Nothing is drawn from a lot until you pick."
            });

            var progressRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            var barGrid = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
            var barOuter = new Border
            {
                Height = 8, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                CornerRadius = new CornerRadius(4), Width = 220, HorizontalAlignment = HorizontalAlignment.Left
            };
            _blockLotsProgressFill = new Border
            {
                Height = 8, Width = 0, Background = new SolidColorBrush(ColAccent),
                CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left
            };
            barGrid.Children.Add(barOuter);
            barGrid.Children.Add(_blockLotsProgressFill);
            progressRow.Children.Add(barGrid);
            _blockLotsProgressLabel = new TextBlock
            {
                FontSize = 11, Foreground = new SolidColorBrush(ColMuted), Margin = new Thickness(232, 0, 0, 0), Text = "0 of 0 lots ready to draw"
            };
            progressRow.Children.Add(_blockLotsProgressLabel);
            root.Children.Add(progressRow);

            var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var btnSuggest = new Button
            {
                Content = "Suggest for all", Padding = new Thickness(10, 4, 10, 4), FontSize = 11.5, Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Legal where a lot's own tax-map faces close cleanly, DTM elsewhere. You still see and can change every pick."
            };
            btnSuggest.Click += (s, e) => { BlockLotSourceResolver.SuggestAll(_blockLotSources); RefreshBlockLotsUi(); RefreshViewer(); };
            var btnClear = new Button { Content = "Clear", Padding = new Thickness(10, 4, 10, 4), FontSize = 11.5 };
            btnClear.Click += (s, e) => { foreach (BlockLotSourceResolution r in _blockLotSources) r.Chosen = LotSourceChoice.None; RefreshBlockLotsUi(); RefreshViewer(); };
            actionRow.Children.Add(btnSuggest);
            actionRow.Children.Add(btnClear);
            root.Children.Add(actionRow);

            root.Children.Add(new TextBlock
            {
                Text = "TOUCHES YOUR LOT", FontSize = 10.5, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(ColMuted), Margin = new Thickness(0, 2, 0, 4)
            });
            _blockLotsAdjacentPanel = new StackPanel();
            root.Children.Add(_blockLotsAdjacentPanel);

            _blockLotsRemainingExpander = new Expander
            {
                Header = "Rest of the block", FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0),
                Foreground = new SolidColorBrush(ColAccent), FontWeight = FontWeights.SemiBold
            };
            _blockLotsRemainingPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            _blockLotsRemainingExpander.Content = _blockLotsRemainingPanel;
            root.Children.Add(_blockLotsRemainingExpander);

            root.Children.Add(new TextBlock
            {
                FontSize = 11, Foreground = new SolidColorBrush(ColMuted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
                Text = "\"Legal\" rebuilds a lot from its own tax-map lengths, one lot at a time — two Legal neighbors can be off by a few " +
                       "inches where they meet (shown as the gap below). Draw warns before leaving any checked lot without a pick."
            });

            card.Child = root;
            return card;
        }

        private async Task ResolveBlockLotSourcesAsync()
        {
            _highlightedBlockLotBbl = null;
            if (_context == null)
            {
                _blockLotSources = new List<BlockLotSourceResolution>();
                RefreshBlockLotsUi();
                return;
            }
            List<LotPoint> subjectLegalRing = _legal != null && _legal.Succeeded ? _legal.Ring : null;
            _blockLotSources = await _blockLotSourceService.ResolveAsync(_context, subjectLegalRing);
            RefreshBlockLotsUi();
        }

        private void RefreshBlockLotsUi()
        {
            if (_blockLotsAdjacentPanel == null)
            {
                return;
            }
            _blockLotsAdjacentPanel.Children.Clear();
            _blockLotsRemainingPanel.Children.Clear();
            _blockLotRowControls.Clear();

            List<BlockLotSourceResolution> adjacent = _blockLotSources.Where(r => r.IsAdjacent).ToList();
            List<BlockLotSourceResolution> remaining = _blockLotSources.Where(r => !r.IsAdjacent).ToList();
            foreach (BlockLotSourceResolution res in adjacent) _blockLotsAdjacentPanel.Children.Add(BuildBlockLotRow(res));
            foreach (BlockLotSourceResolution res in remaining) _blockLotsRemainingPanel.Children.Add(BuildBlockLotRow(res));

            _blockLotsRemainingExpander.Header = $"Rest of the block — {remaining.Count} lot(s), none picked yet";
            _blockLotsRemainingExpander.Visibility = remaining.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            _blockLotsAdjacentPanel.Visibility = adjacent.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            UpdateBlockLotsProgress();
        }

        private void UpdateBlockLotsProgress()
        {
            if (_blockLotsProgressLabel == null)
            {
                return;
            }
            int total = _blockLotSources.Count;
            int chosen = _blockLotSources.Count(r => r.Chosen != LotSourceChoice.None);
            _blockLotsProgressLabel.Text = $"{chosen} of {total} lot(s) ready to draw";
            double fraction = total > 0 ? (double)chosen / total : 0;
            _blockLotsProgressFill.Width = Math.Max(0, 220 * fraction);
        }

        private UIElement BuildBlockLotRow(BlockLotSourceResolution res)
        {
            var row = new Border
            {
                BorderBrush = new SolidColorBrush(ColBorder), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 5), Cursor = System.Windows.Input.Cursors.Hand,
                Background = Brushes.White
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string lotNumber = res.Bbl.Length >= 4 ? res.Bbl.Substring(res.Bbl.Length - 4).TrimStart('0') : res.Bbl;
            if (lotNumber.Length == 0) lotNumber = "0";

            var badge = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")), Margin = new Thickness(0, 0, 8, 0)
            };
            badge.Child = new TextBlock { Text = lotNumber, FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(badge);

            var main = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var l1 = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
            l1.Inlines.Add(new System.Windows.Documents.Run("Lot " + lotNumber + "  ") { FontWeight = FontWeights.Bold });
            if (res.LegalAvailable && res.DtmRing.Count >= 3)
            {
                bool bigDiff = res.DifferencePct > LotFaceComparison.DefaultTolerancePct;
                l1.Inlines.Add(new System.Windows.Documents.Run($"{res.DifferencePct:P1} different")
                { Foreground = new SolidColorBrush(bigDiff ? ColWarn : ColOk), FontSize = 10.5 });
            }
            else if (!res.LegalAvailable)
            {
                l1.Inlines.Add(new System.Windows.Documents.Run(res.LegalUnavailableReason) { Foreground = new SolidColorBrush(ColMuted), FontSize = 10.5 });
            }
            main.Children.Add(l1);

            var dimsParts = new List<string>();
            if (res.LegalAvailable) dimsParts.Add(DescribeDims(res.LegalRing) + " legal");
            if (res.DtmRing.Count >= 3) dimsParts.Add(DescribeDims(res.DtmRing) + " digitized");
            if (dimsParts.Count > 0)
            {
                main.Children.Add(new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(ColMuted), Text = string.Join(" · ", dimsParts) });
            }

            if (res.SeamToSubjectFt.HasValue)
            {
                bool warn = res.SeamToSubjectFt.Value > 0.1;
                main.Children.Add(new TextBlock
                {
                    FontSize = 10.5, Foreground = new SolidColorBrush(warn ? ColWarn : ColOk),
                    Text = (warn ? "⚠ " : "✓ ") + $"{res.SeamToSubjectFt.Value:0.##} ft gap where it meets your lot"
                });
            }
            Grid.SetColumn(main, 1);
            grid.Children.Add(main);

            var seg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            var btnDtm = new Button { Content = "Digitized", Padding = new Thickness(9, 5, 9, 5), FontSize = 11 };
            var btnLegal = new Button
            {
                Content = "Legal", Padding = new Thickness(9, 5, 9, 5), FontSize = 11, Margin = new Thickness(4, 0, 0, 0),
                IsEnabled = res.LegalAvailable, ToolTip = res.LegalAvailable ? "Rebuilt from this lot's own tax-map lengths." : res.LegalUnavailableReason
            };
            btnDtm.Click += (s, e) => SetBlockLotChoice(res, LotSourceChoice.Dtm);
            btnLegal.Click += (s, e) => SetBlockLotChoice(res, LotSourceChoice.Legal);
            seg.Children.Add(btnDtm);
            seg.Children.Add(btnLegal);
            Grid.SetColumn(seg, 2);
            grid.Children.Add(seg);

            row.Child = grid;
            row.MouseLeftButtonUp += (s, e) => { _highlightedBlockLotBbl = res.Bbl; RefreshViewer(); };

            _blockLotRowControls[res.Bbl] = (btnDtm, btnLegal, row);
            PaintBlockLotRow(res);
            return row;
        }

        private void PaintBlockLotRow(BlockLotSourceResolution res)
        {
            if (!_blockLotRowControls.TryGetValue(res.Bbl, out (Button Dtm, Button Legal, Border Row) ctrl))
            {
                return;
            }
            ctrl.Dtm.Background = res.Chosen == LotSourceChoice.Dtm ? new SolidColorBrush(ColDtm) : Brushes.White;
            ctrl.Dtm.Foreground = res.Chosen == LotSourceChoice.Dtm ? Brushes.White : new SolidColorBrush(ColDark);
            ctrl.Legal.Background = res.Chosen == LotSourceChoice.Legal ? new SolidColorBrush(ColLegal) : Brushes.White;
            ctrl.Legal.Foreground = res.Chosen == LotSourceChoice.Legal ? Brushes.White : new SolidColorBrush(ColDark);
            ctrl.Row.BorderBrush = new SolidColorBrush(_highlightedBlockLotBbl == res.Bbl ? ColAccent : ColBorder);
            ctrl.Row.BorderThickness = new Thickness(_highlightedBlockLotBbl == res.Bbl ? 2 : 1);
        }

        private void SetBlockLotChoice(BlockLotSourceResolution res, LotSourceChoice choice)
        {
            res.Chosen = res.Chosen == choice ? LotSourceChoice.None : choice; // click the same pick again to unpick
            _highlightedBlockLotBbl = res.Bbl;
            PaintBlockLotRow(res);
            UpdateBlockLotsProgress();
            RefreshViewer();
        }

        // ═══════════════════════ Additional blocks (far urban context) ═══════════════════════

        private UIElement BuildAdditionalBlocksCard()
        {
            var card = Card();
            var root = new StackPanel();
            var title = new TextBlock { Margin = new Thickness(0, 0, 0, 2), TextWrapping = TextWrapping.Wrap };
            title.Inlines.Add(new System.Windows.Documents.Run("2c · ADDITIONAL BLOCKS") { FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = new SolidColorBrush(ColDark) });
            root.Children.Add(title);
            root.Children.Add(new TextBlock
            {
                FontSize = 11, Foreground = new SolidColorBrush(ColMuted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
                Text = "Whole other blocks across a street — far urban massing context, not the subject's own block. Always drawn from the digitized polygon; each building gets an editable mass."
            });

            var byNumberRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            byNumberRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            byNumberRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            byNumberRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _cmbAddBlockBorough = new ComboBox { Margin = new Thickness(0, 0, 6, 0) };
            foreach (string b in new[] { "Manhattan", "Bronx", "Brooklyn", "Queens", "Staten Island" }) _cmbAddBlockBorough.Items.Add(b);
            _cmbAddBlockBorough.SelectedIndex = 0;
            Grid.SetColumn(_cmbAddBlockBorough, 0);
            byNumberRow.Children.Add(_cmbAddBlockBorough);
            _txtAddBlockNumber = new TextBox { Padding = new Thickness(6), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Block number, e.g. 2577" };
            Grid.SetColumn(_txtAddBlockNumber, 1);
            byNumberRow.Children.Add(_txtAddBlockNumber);
            var btnAddByNumber = new Button { Content = "Add by block", Padding = new Thickness(10, 5, 10, 5) };
            btnAddByNumber.Click += async (s, e) => await OnAddBlockByNumber();
            Grid.SetColumn(btnAddByNumber, 2);
            byNumberRow.Children.Add(btnAddByNumber);
            root.Children.Add(byNumberRow);

            var byAddressRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            byAddressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            byAddressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _txtAddBlockAddress = new TextBox { Padding = new Thickness(6), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Any address on the block you want to add" };
            _txtAddBlockAddress.KeyDown += async (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) await OnAddBlockByAddress(); };
            Grid.SetColumn(_txtAddBlockAddress, 0);
            byAddressRow.Children.Add(_txtAddBlockAddress);
            var btnAddByAddress = new Button { Content = "Add by address", Padding = new Thickness(10, 5, 10, 5) };
            btnAddByAddress.Click += async (s, e) => await OnAddBlockByAddress();
            Grid.SetColumn(btnAddByAddress, 1);
            byAddressRow.Children.Add(btnAddByAddress);
            root.Children.Add(byAddressRow);

            _additionalBlocksStatus = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(ColMuted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            root.Children.Add(_additionalBlocksStatus);

            _additionalBlocksPanel = new StackPanel();
            root.Children.Add(_additionalBlocksPanel);

            card.Child = root;
            return card;
        }

        private async Task OnAddBlockByNumber()
        {
            string borough = _cmbAddBlockBorough.SelectedItem as string ?? "Manhattan";
            string block = (_txtAddBlockNumber.Text ?? string.Empty).Trim();
            if (block.Length == 0)
            {
                _additionalBlocksStatus.Text = "Type a block number first.";
                return;
            }
            await AddBlockAsync(borough, block);
        }

        private async Task OnAddBlockByAddress()
        {
            string query = (_txtAddBlockAddress.Text ?? string.Empty).Trim();
            if (query.Length == 0)
            {
                _additionalBlocksStatus.Text = "Type an address first.";
                return;
            }
            _additionalBlocksStatus.Text = "Searching…";
            try
            {
                string bblCandidate = NycGeoSearchService.NormalizeBbl(query);
                NycLotInfo lot;
                if (bblCandidate.Length == 10)
                {
                    lot = await _geoSearch.GetLotByBblAsync(bblCandidate);
                }
                else
                {
                    List<NycAddressCandidate> results = await _geoSearch.SearchAddressAsync(query);
                    if (results.Count == 0)
                    {
                        _additionalBlocksStatus.Text = $"No address matched '{query}'.";
                        return;
                    }
                    lot = await _geoSearch.GetLotByBblAsync(results[0].Bbl);
                }
                await AddBlockAsync(lot.Borough, lot.Block);
                _txtAddBlockAddress.Text = string.Empty;
            }
            catch (NycApiException ex)
            {
                _additionalBlocksStatus.Text = "Search failed: " + ex.Message;
            }
        }

        private async Task AddBlockAsync(string borough, string block)
        {
            if (_context != null && string.Equals(block, _context.SubjectLot?.Block, StringComparison.Ordinal)
                && string.Equals(NycGeoSearchService.GetBoroughCode(borough), NycGeoSearchService.GetBoroughCode(_context.Borough), StringComparison.Ordinal))
            {
                _additionalBlocksStatus.Text = $"Block {block} is the subject's own block — already shown on the left.";
                return;
            }
            if (_additionalBlocks.Any(b => string.Equals(b.BlockNumber, block, StringComparison.Ordinal)
                && string.Equals(NycGeoSearchService.GetBoroughCode(b.Borough), NycGeoSearchService.GetBoroughCode(borough), StringComparison.Ordinal)))
            {
                _additionalBlocksStatus.Text = $"Block {block} is already added.";
                return;
            }
            _additionalBlocksStatus.Text = $"Loading block {block}…";
            try
            {
                List<NycLotInfo> lots = await _geoSearch.GetLotsByBlockAsync(borough, block);
                if (lots.Count == 0)
                {
                    _additionalBlocksStatus.Text = $"MapPLUTO has no lots for block {block} in {borough}.";
                    return;
                }
                var addlBlock = new NycAdditionalBlock { Borough = borough, BlockNumber = block, Lots = lots };
                _additionalBlocks.Add(addlBlock);

                List<NycBuildingFootprint> newFootprints = await _footprintService.GetFootprintsAsync(lots.Select(l => l.Bbl));
                _footprints.AddRange(newFootprints);

                RefreshAdditionalBlocksUi();
                RefreshViewer();
                _additionalBlocksStatus.Text = $"Block {block}: {lots.Count} lot(s), {newFootprints.Count} footprint(s) added.";
            }
            catch (NycApiException ex)
            {
                _additionalBlocksStatus.Text = $"Could not load block {block}: {ex.Message}";
            }
        }

        private void RemoveAdditionalBlock(NycAdditionalBlock block)
        {
            _additionalBlocks.Remove(block);
            RefreshAdditionalBlocksUi();
            RefreshViewer();
        }

        private void RefreshAdditionalBlocksUi()
        {
            if (_additionalBlocksPanel == null)
            {
                return;
            }
            _additionalBlocksPanel.Children.Clear();
            foreach (NycAdditionalBlock block in _additionalBlocks)
            {
                var row = new Border
                {
                    BorderBrush = new SolidColorBrush(ColBorder), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 4), Background = Brushes.White
                };
                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = new TextBlock
                {
                    Text = block.Label, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")), FontWeight = FontWeights.SemiBold
                };
                rowGrid.Children.Add(label);
                var btnRemove = new Button { Content = "Remove", Padding = new Thickness(8, 3, 8, 3), FontSize = 10.5 };
                NycAdditionalBlock captured = block;
                btnRemove.Click += (s, e) => RemoveAdditionalBlock(captured);
                Grid.SetColumn(btnRemove, 1);
                rowGrid.Children.Add(btnRemove);
                row.Child = rowGrid;
                _additionalBlocksPanel.Children.Add(row);
            }
        }

        // ═════════════════════════════ Handlers ═════════════════════════════

        private async Task OnSearch()
        {
            string query = _txtQuery.Text?.Trim();
            if (string.IsNullOrEmpty(query)) return;
            _statusText.Text = "Searching…";
            _lstResults.Items.Clear();
            try
            {
                string bblCandidate = NycGeoSearchService.NormalizeBbl(query);
                if (bblCandidate.Length == 10)
                {
                    _lstResults.Items.Add(new NycAddressCandidate { Label = "BBL " + bblCandidate, Bbl = bblCandidate });
                    _lstResults.SelectedIndex = 0;
                }
                else
                {
                    List<NycAddressCandidate> results = await _geoSearch.SearchAddressAsync(query);
                    foreach (NycAddressCandidate r in results) _lstResults.Items.Add(r);
                    if (results.Count == 0)
                    {
                        _statusText.Text = "No address matches — try a 10-digit BBL instead.";
                        return;
                    }
                    _lstResults.SelectedIndex = 0;
                }
                _statusText.Text = $"{_lstResults.Items.Count} result(s). Select one and click Load.";
            }
            catch (NycApiException ex)
            {
                _statusText.Text = "Search failed: " + ex.Message;
            }
        }

        private async Task OnLoad()
        {
            if (!(_lstResults.SelectedItem is NycAddressCandidate candidate))
            {
                _statusText.Text = "Select a result first.";
                return;
            }
            _statusText.Text = "Loading lot, block context, tax-map faces, footprints and street widths…";
            _btnDraw.IsEnabled = false;
            _selectedSource = null;
            _surveyFaces = null;
            _surveyRing = null;
            _surveyMatches = null;
            _chkSurvey.IsEnabled = false;
            _chkSurvey.IsChecked = false;
            SetSourceAvailable(LotGeometrySource.Survey, false, "Pick property lines in the model to use the survey as the drawn geometry.");
            foreach (RadioButton r in _sourceRadios.Values) r.IsChecked = false;
            _additionalBlocks.Clear();
            RefreshAdditionalBlocksUi();
            if (_additionalBlocksStatus != null) _additionalBlocksStatus.Text = string.Empty;
            try
            {
                NycLotInfo subject = await _geoSearch.GetLotByBblAsync(candidate.Bbl);
                _context = await _geoSearch.GetBlockContextAsync(subject);
                _footprints = await _footprintService.GetFootprintsAsync(_context.AllLots.Select(l => l.Bbl));
                await LoadFacesAsync(subject);
                _mappedStreets = await _streetWidthService.GetMappedStreetsAsync(
                    _context.MinX - 150, _context.MinY - 150, _context.MaxX + 150, _context.MaxY + 150);
                _roadbeds = await _contextLayerService.GetRoadbedsAsync(
                    _context.MinX - 100, _context.MinY - 100, _context.MaxX + 100, _context.MaxY + 100);
                _realSidewalks = await _contextLayerService.GetSidewalksAsync(
                    _context.MinX - 100, _context.MinY - 100, _context.MaxX + 100, _context.MaxY + 100);
                _curbs = await _contextLayerService.GetCurbsAsync(
                    _context.MinX - 100, _context.MinY - 100, _context.MaxX + 100, _context.MaxY + 100);
                _contextTrees = await _contextLayerService.GetStreetTreesAsync(
                    _context.MinX - 100, _context.MinY - 100, _context.MaxX + 100, _context.MaxY + 100);

                RebuildAnchored();
                RefreshChips(subject);
                RefreshSourceCards(subject);
                RefreshFacesTable();
                RefreshSidewalkNote();
                await ResolveBlockLotSourcesAsync();
                RefreshViewer();
                FitViewer();

                _statusText.Text = "Lot loaded. Choose a geometry source above to enable Draw.";
            }
            catch (NycApiException ex)
            {
                _statusText.Text = "Load failed: " + ex.Message;
            }
        }

        private async Task LoadFacesAsync(NycLotInfo subject)
        {
            _orderedFaces = new List<NycLotFace>();
            List<LotPoint> ring = subject.PolygonRings.Count > 0 ? subject.PolygonRings[0] : new List<LotPoint>();
            try
            {
                List<NycLotFace> raw = await _faceService.GetFacesAsync(subject.Bbl);
                LotFaceChainResult chain = LotFaceChain.Order(raw);
                if (chain.Succeeded)
                {
                    _orderedFaces = chain.Faces;
                    _faceSourceNote = $"Tax-map faces ✓ ({raw.Count}, {chain.Faces.Count(f => f.IsStreetFace)} on a street)";
                    return;
                }
                _faceSourceNote = $"Tax-map faces unusable ({chain.FailureReason}) — polygon edges, no legal dimensions";
            }
            catch (NycApiException ex)
            {
                _faceSourceNote = "Tax-map faces unavailable (" + ex.Message + ") — polygon edges, no legal dimensions";
            }
            _orderedFaces = LotFaceChain.FromRing(ring, subject.Bbl);
        }

        private static LotPoint AnchorPoint(NycLotInfo subject, LotAnchorCorner corner) => corner switch
        {
            LotAnchorCorner.Southwest => new LotPoint(subject.MinX, subject.MinY),
            LotAnchorCorner.Northwest => new LotPoint(subject.MinX, subject.MaxY),
            LotAnchorCorner.Southeast => new LotPoint(subject.MaxX, subject.MinY),
            LotAnchorCorner.Northeast => new LotPoint(subject.MaxX, subject.MaxY),
            _ => subject.Centroid
        };

        /// <summary>Recomputes every derived geometry for the current anchor corner.</summary>
        private void RebuildAnchored()
        {
            if (_context == null || _orderedFaces.Count < 3)
            {
                _anchoredFaces = new List<NycLotFace>();
                _legal = null;
                _recordedRing = null;
                return;
            }
            var corner = _cmbAnchor.SelectedItem is LotAnchorCorner c ? c : LotAnchorCorner.Southwest;
            _anchoredFaces = LotFaceChain.StartAt(_orderedFaces, AnchorPoint(_context.SubjectLot, corner));
            _legal = _anchoredFaces.Any(f => f.LegalLengthFt > 0) ? LotReconstruction.FromLegalFaces(_anchoredFaces) : null;
            _recordedRing = LotRecordedRectangle.Build(_anchoredFaces, _context.SubjectLot.LotFrontRecordedFt, _context.SubjectLot.LotDepthRecordedFt);
            if (_surveyFaces != null)
            {
                // Same ring, face 0 re-seated at the new anchor corner; roles re-matched against the re-anchored DTM faces.
                _surveyFaces = LotFaceChain.StartAt(_surveyFaces, AnchorPoint(_context.SubjectLot, corner));
                _surveyMatches = LotSurveyMatch.Inherit(_surveyFaces, _anchoredFaces);
                _surveyRing = _surveyFaces.Select(f => f.Start).ToList();
            }
            if (_btnPickSurvey != null)
            {
                _btnPickSurvey.IsEnabled = _anchoredFaces.Count >= 3;
            }
        }

        private void RefreshChips(NycLotInfo subject)
        {
            _chips.Children.Clear();
            _chips.Children.Add(Chip($" · {subject.Borough} {subject.ZipCode}".TrimEnd(), subject.Address));
            _chips.Children.Add(Chip($"Block {subject.Block} · Lot {subject.Lot}"));
            _chips.Children.Add(Chip("Zoning ", subject.GetZoningSummary()));
            if (!string.IsNullOrWhiteSpace(subject.BuildingClass)) _chips.Children.Add(Chip(subject.BuildingClass + (string.IsNullOrWhiteSpace(subject.LandUse) ? "" : " · land use " + subject.LandUse)));
            int streetFaces = _orderedFaces.Count(f => f.IsStreetFace);
            if (streetFaces >= 2) _chips.Children.Add(Chip(" Corner lot", "▣", $"{streetFaces} faces on the block perimeter"));
            _chips.Children.Add(Chip($"Lot area {subject.LotAreaSqFt:N0} sf (recorded)"));

            _lblSourcesNote.Text =
                $"Sources loaded: DTM polygon {(subject.PolygonRings.Count > 0 ? "✓" : "✗")} · {_faceSourceNote} · " +
                $"Assessment record {(subject.LotFrontRecordedFt > 0 ? "✓" : "✗ (no front/depth)")} · " +
                $"Block context: {_context.AdjacentLots.Count} adjacent, {_context.RemainingBlockLots.Count} other lots · " +
                $"{_footprints.Count} building footprint(s) matched · DCM street widths {(_mappedStreets.Count > 0 ? "✓" : "✗")} · " +
                $"Ground context: {_roadbeds.Count} roadbed(s), {_realSidewalks.Count} sidewalk(s), {_curbs.Count} curb(s), {_contextTrees.Count} tree(s)";
        }

        private void RefreshSourceCards(NycLotInfo subject)
        {
            // DTM
            List<LotPoint> dtmRing = _anchoredFaces.Select(f => f.Start).ToList();
            if (dtmRing.Count >= 3)
            {
                List<FaceComparison> rows = LotFaceComparison.Compare(_anchoredFaces);
                int off = rows.Count(r => r.IsDiscrepant(LotFaceComparison.DefaultTolerancePct));
                SetSourceAvailable(LotGeometrySource.Dtm, true,
                    $"{Math.Abs(LotGeometry.SignedArea(dtmRing)):N0} sf · {dtmRing.Count} faces · " +
                    (off > 0 ? $"⚠ {off} of {rows.Count} faces differ >1% from legal" : "all faces within 1% of legal") +
                    "\nFits the neighbors' DTM geometry exactly.");
                _sourceDims[LotGeometrySource.Dtm].Text = DescribeDims(dtmRing);
            }
            else
            {
                SetSourceAvailable(LotGeometrySource.Dtm, false, "No polygon geometry for this lot.");
            }

            // Legal
            if (_legal != null && _legal.Succeeded)
            {
                SetSourceAvailable(LotGeometrySource.TaxMapLegal, true,
                    $"{_legal.AreaSqFt:N0} sf · {_legal.Ring.Count} faces · closes within {_legal.ClosureErrorFt:0.00}'" +
                    (_legal.Warnings.Count > 0 ? $"\n{_legal.Warnings.Count} note(s): {_legal.Warnings[0]}" : "\nDimensions as written on the tax map sheet."));
                _sourceDims[LotGeometrySource.TaxMapLegal].Text = DescribeDims(_legal.Ring);
            }
            else
            {
                SetSourceAvailable(LotGeometrySource.TaxMapLegal, false,
                    _legal == null ? "No legal dimensions published for this lot." : "Cannot rebuild: " + _legal.FailureReason);
            }

            // Recorded
            if (_recordedRing != null)
            {
                SetSourceAvailable(LotGeometrySource.Recorded, true,
                    $"{subject.LotAreaSqFt:N0} sf recorded · rectangle on the frontage bearing\n⚠ Only meaningful for regular lots.");
                _sourceDims[LotGeometrySource.Recorded].Text = $"{subject.LotFrontRecordedFt:0.##} × {subject.LotDepthRecordedFt:0.##}";
            }
            else
            {
                SetSourceAvailable(LotGeometrySource.Recorded, false,
                    _anchoredFaces.Count != 4 ? $"Lot has {_anchoredFaces.Count} faces — a front × depth rectangle does not describe it." : "No recorded front/depth.");
            }
        }

        private static string DescribeDims(IReadOnlyList<LotPoint> ring)
        {
            List<double> edges = LotDimensionCheck.EdgeLengths(ring);
            if (edges.Count == 4)
            {
                double a = (edges[0] + edges[2]) / 2, b = (edges[1] + edges[3]) / 2;
                return $"{Math.Min(a, b):0.##} × {Math.Max(a, b):0.##}";
            }
            return string.Join(" · ", edges.Select(e => e.ToString("0.#")));
        }

        private void RefreshFacesTable()
        {
            _facesGrid.Children.Clear();
            _facesGrid.RowDefinitions.Clear();
            _facesGrid.ColumnDefinitions.Clear();
            _faceNameBoxes.Clear();
            _faceRows.Clear();
            foreach (double w in new[] { 26.0, 64, 58, 58, 62 })
                _facesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            _facesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _facesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] headers = { "#", "Role", "Legal", "DTM", "Δ", "Street name (editable on street faces)" };
            for (int c = 0; c < headers.Length; c++)
            {
                var h = new TextBlock { Text = headers[c], FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ColMuted), Margin = new Thickness(4, 0, 4, 4), TextAlignment = c >= 2 && c <= 4 ? TextAlignment.Right : TextAlignment.Left };
                Grid.SetRow(h, 0); Grid.SetColumn(h, c);
                _facesGrid.Children.Add(h);
            }
            if (_anchoredFaces.Count == 0)
            {
                return;
            }

            List<StreetFrontage> proposed = StreetNameResolver.ResolveFrontages(_context);
            List<FaceComparison> rows = LotFaceComparison.Compare(_anchoredFaces);
            for (int i = 0; i < _anchoredFaces.Count; i++)
            {
                NycLotFace f = _anchoredFaces[i];
                FaceComparison r = rows[i];
                _facesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int row = i + 1;

                var rowBorder = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(4), Cursor = System.Windows.Input.Cursors.Hand };
                Grid.SetRow(rowBorder, row); Grid.SetColumn(rowBorder, 0); Grid.SetColumnSpan(rowBorder, 6);
                int captured = i;
                rowBorder.MouseLeftButtonUp += (s, e) => HighlightFace(captured, false);
                _facesGrid.Children.Add(rowBorder);
                _faceRows.Add(rowBorder);

                Cell(row, 0, i.ToString(), false);
                var tag = new Border
                {
                    Background = new SolidColorBrush(f.IsStreetFace ? (Color)ColorConverter.ConvertFromString("#DBEAFE") : (Color)ColorConverter.ConvertFromString("#E2E8F0")),
                    CornerRadius = new CornerRadius(9), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(4, 3, 4, 3), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false,
                    Child = new TextBlock { Text = f.IsStreetFace ? "Street" : (f.LegalLengthFt > 0 ? "Interior" : "Unknown"), FontSize = 10.5, Foreground = new SolidColorBrush(f.IsStreetFace ? (Color)ColorConverter.ConvertFromString("#1E40AF") : ColDark) }
                };
                Grid.SetRow(tag, row); Grid.SetColumn(tag, 1);
                _facesGrid.Children.Add(tag);
                Cell(row, 2, r.LegalFt > 0 ? $"{r.LegalFt:0.##}'" : "—", true);
                Cell(row, 3, $"{r.DigitizedFt:0.##}'", true);
                var delta = Cell(row, 4, r.LegalFt > 0 ? $"{(r.DifferenceFt >= 0 ? "+" : "")}{r.DifferencePct * (r.DifferenceFt >= 0 ? 1 : -1):P1}{(r.IsDiscrepant(LotFaceComparison.DefaultTolerancePct) ? " ⚠" : "")}" : "—", true);
                delta.Foreground = new SolidColorBrush(r.IsDiscrepant(LotFaceComparison.DefaultTolerancePct) ? ColWarn : ColOk);
                if (r.LegalFt <= 0) delta.Foreground = new SolidColorBrush(ColMuted);

                if (f.IsStreetFace)
                {
                    var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 4, 2) };
                    StreetFrontage guess = GuessFrontage(f, proposed);
                    var box = new TextBox { Text = guess?.StreetName ?? string.Empty, Width = 150, Padding = new Thickness(4, 2, 4, 2), ToolTip = "Placed as 3D Model Text and stored in this face's record." };
                    box.TextChanged += (s, e) => RefreshViewer();
                    namePanel.Children.Add(box);
                    string conf = guess == null || guess.SourceLotCount == 0 ? "no data — enter manually" : guess.IsConfident ? $"{guess.SourceLotCount} block addresses agree" : "single source — verify";
                    namePanel.Children.Add(new TextBlock { Text = conf, FontSize = 10.5, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(guess != null && guess.IsConfident ? ColMuted : ColWarn) });
                    Grid.SetRow(namePanel, row); Grid.SetColumn(namePanel, 5);
                    _facesGrid.Children.Add(namePanel);
                    _faceNameBoxes[i] = box;
                }
                else
                {
                    Cell(row, 5, "—", false).Foreground = new SolidColorBrush(ColMuted);
                }
            }
        }

        private TextBlock Cell(int row, int column, string text, bool right)
        {
            var tb = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(4, 4, 4, 4), TextAlignment = right ? TextAlignment.Right : TextAlignment.Left, IsHitTestVisible = false };
            Grid.SetRow(tb, row); Grid.SetColumn(tb, column);
            _facesGrid.Children.Add(tb);
            return tb;
        }

        /// <summary>Which compass side of the block a street face sits on → the name proposed for that side.</summary>
        private StreetFrontage GuessFrontage(NycLotFace face, List<StreetFrontage> proposed)
        {
            var mid = new LotPoint((face.Start.X + face.End.X) / 2, (face.Start.Y + face.End.Y) / 2);
            CompassSide side = LotGeometry.ClassifySide(mid, _context.MinX, _context.MinY, _context.MaxX, _context.MaxY);
            return proposed.FirstOrDefault(p => p.Side == side);
        }

        private void RefreshSidewalkNote()
        {
            if (_mappedStreets.Count == 0)
            {
                _lblSidewalkNote.Text = "Default 12 ft. No Digital City Map widths available for this block.";
                return;
            }
            string widths = string.Join(" · ", _mappedStreets.OrderBy(s => s.Name).Select(s => $"{s.Name} {s.WidthFt:0}'"));
            _lblSidewalkNote.Text = $"Official (property-line to property-line) widths from DCP's Digital City Map: {widths}. Default kept at 12; edit if you want another width.";
        }

        // ═════════════════════════════ Survey pick ═════════════════════════════

        private void RequestSurveyPick()
        {
            if (_context == null || _anchoredFaces.Count < 3)
            {
                _statusText.Text = "Load a lot first — the survey is matched against its tax-map faces.";
                return;
            }
            SurveyPickRequested = true;
            _statusText.Text = "Pick the property lines in Revit, then Finish…";
            Hide(); // ShowDialog returns to the command, which picks and re-shows this window
        }

        /// <summary>
        /// Called by the command after picking. Chains the picked curves into a
        /// ring, converts model coordinates to State Plane using the same anchor
        /// the lot is drawn with, and inherits street roles / legal lengths from
        /// the matching tax-map faces. Nothing here touches the model.
        /// </summary>
        public void ApplySurveyPick(List<CurveElement> picked)
        {
            if (picked == null)
            {
                _statusText.Text = "Pick cancelled — nothing changed.";
                return;
            }
            if (_context == null || _anchoredFaces.Count < 3)
            {
                _statusText.Text = "Load a lot first.";
                return;
            }

            // Model → State Plane: the lot's anchor vertex lands on the model origin when drawn,
            // so a property line placed against the drawn lot maps back with the same offset.
            LotPoint anchorVertex = _anchoredFaces[0].Start;
            var faces = new List<NycLotFace>();
            int arcs = 0;
            foreach (CurveElement element in picked)
            {
                Curve curve = element.GeometryCurve;
                if (curve == null || !curve.IsBound)
                {
                    continue;
                }
                if (!(curve is Line))
                {
                    arcs++; // approximated by its chord — said in the note
                }
                XYZ a = curve.GetEndPoint(0), b = curve.GetEndPoint(1);
                faces.Add(new NycLotFace
                {
                    Bbl = _context.SubjectLot.Bbl,
                    Path = new List<LotPoint>
                    {
                        new LotPoint(a.X + anchorVertex.X, a.Y + anchorVertex.Y),
                        new LotPoint(b.X + anchorVertex.X, b.Y + anchorVertex.Y)
                    }
                });
            }

            LotFaceChainResult chain = LotFaceChain.Order(faces);
            if (!chain.Succeeded)
            {
                _surveyFaces = null;
                _surveyRing = null;
                _surveyMatches = null;
                SetSourceAvailable(LotGeometrySource.Survey, false, $"Picked {picked.Count} line(s) but they do not form one closed loop: {chain.FailureReason}");
                _statusText.Text = "Survey lines do not close — pick all sides of the lot, joined end to end.";
                return;
            }

            var corner = _cmbAnchor.SelectedItem is LotAnchorCorner c ? c : LotAnchorCorner.Southwest;
            List<NycLotFace> ordered = LotFaceChain.StartAt(chain.Faces, AnchorPoint(_context.SubjectLot, corner));
            int[] matches = LotSurveyMatch.Inherit(ordered, _anchoredFaces);
            int matched = matches.Count(m => m >= 0);

            _surveyFaces = ordered;
            _surveyRing = ordered.Select(f => f.Start).ToList();
            _surveyMatches = matches;
            _surveyNote = $"{ordered.Count} sides from {picked.Count} line(s) · {matched} matched to tax-map faces (street role and legal length inherited)"
                          + (matched < ordered.Count ? $" · {ordered.Count - matched} unmatched (role unknown)" : "")
                          + (arcs > 0 ? $" · {arcs} arc(s) taken as chords" : "");

            _chkSurvey.IsEnabled = true;
            _chkSurvey.IsChecked = true;
            SetSourceAvailable(LotGeometrySource.Survey, true, _surveyNote + "\nThe survey governs; every other source stays as comparison.");
            _sourceDims[LotGeometrySource.Survey].Text = DescribeDims(_surveyRing);
            _sourceRadios[LotGeometrySource.Survey].IsChecked = true;
            _statusText.Text = "Survey loaded. Review it in the viewer, then Draw.";
            RefreshViewer();
        }

        private void OnSourceChosen(LotGeometrySource source)
        {
            _selectedSource = source;
            foreach (KeyValuePair<LotGeometrySource, Border> kv in _sourceCards)
            {
                bool sel = kv.Key == source;
                kv.Value.BorderBrush = new SolidColorBrush(sel ? ColAccent : ColBorder);
            }
            _btnDraw.IsEnabled = _context != null && _anchoredFaces.Count >= 3;
            _statusText.Text = _btnDraw.IsEnabled
                ? $"Will draw the {SourceLabel(source)} geometry. Review the street names, then Draw."
                : "Load a lot first.";
            RefreshViewer();
        }

        private static string SourceLabel(LotGeometrySource s) => s switch
        {
            LotGeometrySource.Dtm => "DTM polygon",
            LotGeometrySource.TaxMapLegal => "tax-map legal",
            LotGeometrySource.Recorded => "recorded (assessment)",
            LotGeometrySource.Survey => "survey",
            _ => s.ToString()
        };

        private void HighlightFace(int index, bool fromViewer)
        {
            _viewer.HighlightedFace = index;
            for (int i = 0; i < _faceRows.Count; i++)
            {
                _faceRows[i].Background = i == index ? new SolidColorBrush(Color.FromArgb(40, 0, 113, 227)) : Brushes.Transparent;
            }
            _viewer.Render();
        }

        // ═════════════════════════════ Viewer ═════════════════════════════

        private List<LotPoint> RingFor(LotGeometrySource source)
        {
            switch (source)
            {
                case LotGeometrySource.TaxMapLegal: return _legal != null && _legal.Succeeded ? _legal.Ring : null;
                case LotGeometrySource.Recorded: return _recordedRing;
                case LotGeometrySource.Dtm: return _anchoredFaces.Count >= 3 ? _anchoredFaces.Select(f => f.Start).ToList() : null;
                case LotGeometrySource.Survey: return _surveyRing;
                default: return null;
            }
        }

        private void RefreshViewer()
        {
            if (_viewer == null) return;
            _viewer.Rings.Clear();
            _viewer.FaceLabels.Clear();
            _viewer.Anchor = null;
            _viewer.ShowDimensions = _chkDims.IsChecked == true;
            if (_context == null || _anchoredFaces.Count < 3)
            {
                _viewer.Render();
                return;
            }

            if (_chkNeighbors.IsChecked == true)
            {
                foreach (BlockLotSourceResolution res in _blockLotSources)
                {
                    List<LotPoint> ring = res.Chosen == LotSourceChoice.Legal && res.LegalAvailable ? res.LegalRing : res.DtmRing;
                    if (ring == null || ring.Count < 3) continue;
                    bool highlighted = res.Bbl == _highlightedBlockLotBbl;
                    Color stroke = res.Chosen == LotSourceChoice.Legal ? ColAdjacentLegal : ColNeighbor;
                    string lotNumber = res.Bbl.Length >= 4 ? res.Bbl.Substring(res.Bbl.Length - 4).TrimStart('0') : res.Bbl;
                    _viewer.Rings.Add(new ViewerRing
                    {
                        Points = ring,
                        Stroke = new SolidColorBrush(stroke),
                        Thickness = highlighted ? 2.5 : (res.Chosen == LotSourceChoice.None ? 1 : 1.5),
                        Dash = res.Chosen == LotSourceChoice.None ? new DoubleCollection { 4, 3 } : null,
                        Fill = highlighted
                            ? new SolidColorBrush(Color.FromArgb(30, 0, 113, 227))
                            : new SolidColorBrush(Color.FromArgb(255, 243, 244, 246)),
                        Label = "Lot " + lotNumber
                    });
                }
            }

            if (_chkNeighbors.IsChecked == true)
            {
                var otherBlockStroke = (Color)ColorConverter.ConvertFromString("#7C3AED");
                foreach (NycAdditionalBlock addlBlock in _additionalBlocks)
                {
                    foreach (NycLotInfo lot in addlBlock.Lots)
                    {
                        if (lot.PolygonRings.Count == 0) continue;
                        _viewer.Rings.Add(new ViewerRing
                        {
                            Points = lot.PolygonRings[0],
                            Stroke = new SolidColorBrush(otherBlockStroke),
                            Thickness = 1,
                            Fill = new SolidColorBrush(Color.FromArgb(18, 124, 58, 237)),
                            Label = "Blk " + addlBlock.BlockNumber
                        });
                    }
                }
            }

            List<LotPoint> dtm = RingFor(LotGeometrySource.Dtm);
            List<LotPoint> legal = RingFor(LotGeometrySource.TaxMapLegal);
            List<LotPoint> recorded = RingFor(LotGeometrySource.Recorded);
            List<LotPoint> chosen = _selectedSource.HasValue ? RingFor(_selectedSource.Value) : null;

            if (_chkFootprintsPreview.IsChecked == true && _footprints.Count > 0)
            {
                RenderFootprintsPreview(chosen ?? legal ?? dtm);
            }

            if (_chkSidewalkPreview.IsChecked == true)
            {
                List<LotPoint> basis = chosen ?? legal ?? dtm;
                if (basis != null && double.TryParse(_txtSidewalkWidth.Text, out double width) && width > 0)
                {
                    try
                    {
                        _viewer.Rings.Add(new ViewerRing { Points = LotGeometry.OffsetRingOutward(basis, width), Stroke = new SolidColorBrush(ColSidewalk), Thickness = 1.5, IsSubject = true });
                    }
                    catch (ArgumentException) { /* degenerate basis — nothing to preview */ }
                }
            }
            if (_chkDtm.IsChecked == true && dtm != null)
                _viewer.Rings.Add(new ViewerRing { Points = dtm, Stroke = new SolidColorBrush(ColDtm), Thickness = _selectedSource == LotGeometrySource.Dtm ? 3 : 2, Dash = new DoubleCollection { 5, 3 }, IsSubject = true });
            if (_chkRecorded.IsChecked == true && recorded != null)
                _viewer.Rings.Add(new ViewerRing { Points = recorded, Stroke = new SolidColorBrush(ColRecorded), Thickness = _selectedSource == LotGeometrySource.Recorded ? 3 : 1.5, Dash = new DoubleCollection { 1.5, 3 }, IsSubject = true });
            if (_chkLegal.IsChecked == true && legal != null)
                _viewer.Rings.Add(new ViewerRing { Points = legal, Stroke = new SolidColorBrush(ColLegal), Thickness = _selectedSource == LotGeometrySource.TaxMapLegal ? 3.5 : 2.5, Fill = new SolidColorBrush(Color.FromArgb(12, 200, 16, 16)), IsSubject = true });
            List<LotPoint> survey = RingFor(LotGeometrySource.Survey);
            var colSurvey = (Color)ColorConverter.ConvertFromString("#7C3AED");
            if (_chkSurvey.IsChecked == true && survey != null)
                _viewer.Rings.Add(new ViewerRing { Points = survey, Stroke = new SolidColorBrush(colSurvey), Thickness = _selectedSource == LotGeometrySource.Survey ? 3.5 : 2.5, Fill = new SolidColorBrush(Color.FromArgb(14, 124, 58, 237)), IsSubject = true });

            // Face labels follow the chosen ring (or legal, or DTM) so badges sit on what will be drawn.
            bool surveyChosen = _selectedSource == LotGeometrySource.Survey && _surveyFaces != null;
            List<LotPoint> labelRing = chosen ?? legal ?? dtm;
            List<NycLotFace> labelFaces = surveyChosen ? _surveyFaces : _anchoredFaces;
            List<FaceComparison> rows = LotFaceComparison.Compare(labelFaces);
            for (int i = 0; i < labelFaces.Count && i < labelRing.Count; i++)
            {
                FaceComparison r = rows[i];
                string measuredLabel = surveyChosen ? "survey" : "DTM";
                string text = r.LegalFt > 0
                    ? $"{r.LegalFt:0.##}' legal · {r.DigitizedFt:0.##}' {measuredLabel}" + (r.IsDiscrepant(LotFaceComparison.DefaultTolerancePct) ? $"  ⚠ {(r.DifferenceFt >= 0 ? "+" : "-")}{r.DifferencePct:P1}" : "  ✓")
                    : $"{r.DigitizedFt:0.##}' {measuredLabel} (no legal dimension)";
                int nameIndex = surveyChosen ? (_surveyMatches != null && i < _surveyMatches.Length ? _surveyMatches[i] : -1) : i;
                _viewer.FaceLabels.Add(new ViewerFaceLabel
                {
                    FaceIndex = i, Start = labelRing[i], End = labelRing[(i + 1) % labelRing.Count], Text = text,
                    IsWarning = r.IsDiscrepant(LotFaceComparison.DefaultTolerancePct),
                    StreetName = labelFaces[i].IsStreetFace && nameIndex >= 0 && _faceNameBoxes.TryGetValue(nameIndex, out TextBox box) ? box.Text?.Trim() ?? string.Empty : string.Empty
                });
            }
            _viewer.Anchor = labelRing[0];

            _legend.Text =
                (dtm != null ? $"DTM {DescribeDims(dtm)} · {Math.Abs(LotGeometry.SignedArea(dtm)):N0} sf\n" : "") +
                (legal != null ? $"Legal {DescribeDims(legal)} · {Math.Abs(LotGeometry.SignedArea(legal)):N0} sf\n" : "") +
                (recorded != null ? $"Recorded {DescribeDims(recorded)} · {Math.Abs(LotGeometry.SignedArea(recorded)):N0} sf\n" : "") +
                (survey != null ? $"Survey {DescribeDims(survey)} · {Math.Abs(LotGeometry.SignedArea(survey)):N0} sf\n" : "") +
                (chosen != null ? $"Chosen: {SourceLabel(_selectedSource.Value)}" : "No source chosen yet");
            _viewer.Render();
        }

        /// <summary>
        /// Draws the same Building Footprints match the 3D masses will use —
        /// BBL join solid, spatial fallback pale/dashed — over every lot the
        /// window currently knows about (subject, block, additional blocks),
        /// so a mismatch is visible before Draw ever runs.
        /// </summary>
        private void RenderFootprintsPreview(List<LotPoint> subjectRing)
        {
            var drawn = new HashSet<string>();

            void DrawFor(string bbl, List<LotPoint> ring)
            {
                foreach (FootprintMatch m in NycFootprintMatcher.MatchForLot(bbl, ring, _footprints))
                {
                    string key = !string.IsNullOrEmpty(m.Footprint.Bin)
                        ? m.Footprint.Bin
                        : $"{m.Footprint.PolygonRings[0][0].X:F2},{m.Footprint.PolygonRings[0][0].Y:F2}";
                    if (!drawn.Add(key)) continue;
                    bool real = m.Kind == FootprintMatchKind.ByBbl;
                    _viewer.Rings.Add(new ViewerRing
                    {
                        Points = m.Footprint.PolygonRings[0],
                        Stroke = new SolidColorBrush(real ? ColFootprintReal : ColFootprintFallback),
                        Thickness = 1.5,
                        Dash = real ? null : new DoubleCollection { 3, 2 },
                        Fill = new SolidColorBrush(real ? Color.FromArgb(40, 234, 88, 12) : Color.FromArgb(25, 253, 186, 116)),
                        Label = "BIN " + (string.IsNullOrEmpty(m.Footprint.Bin) ? "?" : m.Footprint.Bin) + (real ? string.Empty : " (est. pos.)")
                    });
                }
            }

            DrawFor(_context.SubjectLot?.Bbl, subjectRing);
            foreach (BlockLotSourceResolution res in _blockLotSources)
            {
                List<LotPoint> ring = res.Chosen == LotSourceChoice.Legal && res.LegalAvailable ? res.LegalRing : res.DtmRing;
                DrawFor(res.Bbl, ring);
            }
            foreach (NycAdditionalBlock addlBlock in _additionalBlocks)
            {
                foreach (NycLotInfo lot in addlBlock.Lots)
                {
                    if (lot.PolygonRings.Count == 0) continue;
                    DrawFor(lot.Bbl, lot.PolygonRings[0]);
                }
            }
        }

        private void FitViewer()
        {
            List<LotPoint> basis = RingFor(LotGeometrySource.Dtm);
            if (basis != null)
            {
                _viewer.Fit(basis);
            }
        }

        // ═════════════════════════════ Draw ═════════════════════════════

        private void OnDraw()
        {
            if (_context == null || _cmbLevel.SelectedItem == null)
            {
                MessageBox.Show("Load a lot and select a level first.", "BauTools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!_selectedSource.HasValue)
            {
                MessageBox.Show("Choose which geometry source to draw.", "BauTools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            List<LotPoint> ring = RingFor(_selectedSource.Value);
            bool surveyChosen = _selectedSource == LotGeometrySource.Survey && _surveyFaces != null;
            List<NycLotFace> faces = surveyChosen ? _surveyFaces : _anchoredFaces;
            if (ring == null || ring.Count != faces.Count)
            {
                MessageBox.Show("The chosen source has no usable geometry for this lot.", "BauTools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!double.TryParse(_txtSidewalkWidth.Text, out double sidewalkWidth) || sidewalkWidth < 0)
            {
                MessageBox.Show("Sidewalk width must be a non-negative number.", "BauTools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int adjacentMissing = _chkAdjacent.IsChecked == true
                ? _blockLotSources.Count(r => r.IsAdjacent && r.Chosen == LotSourceChoice.None)
                : 0;
            int remainingMissing = _chkBlock.IsChecked == true
                ? _blockLotSources.Count(r => !r.IsAdjacent && r.Chosen == LotSourceChoice.None)
                : 0;
            if (adjacentMissing + remainingMissing > 0)
            {
                MessageBox.Show(
                    $"{adjacentMissing + remainingMissing} lot(s) on the block are checked to draw but have no source picked yet " +
                    "(\"2b · Other lots on the block\"). Pick Digitized or Legal for each, or use \"Suggest for all\".",
                    "BauTools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var faceNames = new List<string>();
            var frontages = new List<StreetFrontage>();
            for (int i = 0; i < faces.Count; i++)
            {
                // Survey faces take the street name of the tax-map face they matched.
                int nameIndex = surveyChosen ? (_surveyMatches != null && i < _surveyMatches.Length ? _surveyMatches[i] : -1) : i;
                string name = nameIndex >= 0 && _faceNameBoxes.TryGetValue(nameIndex, out TextBox box) ? box.Text?.Trim() ?? string.Empty : string.Empty;
                faceNames.Add(name);
                if (faces[i].IsStreetFace)
                {
                    NycLotFace f = faces[i];
                    var mid = new LotPoint((f.Start.X + f.End.X) / 2, (f.Start.Y + f.End.Y) / 2);
                    CompassSide side = LotGeometry.ClassifySide(mid, _context.MinX, _context.MinY, _context.MaxX, _context.MaxY);
                    if (frontages.All(x => x.Side != side))
                    {
                        frontages.Add(new StreetFrontage { Side = side, StreetName = name });
                    }
                }
            }

            var level = (Level)_cmbLevel.SelectedItem;
            var options = new NycLotDrawOptions
            {
                LevelId = level.Id,
                AnchorCorner = (LotAnchorCorner)_cmbAnchor.SelectedItem,
                AnchorPointInModel = XYZ.Zero,
                GeometrySource = _selectedSource.Value,
                SubjectFaces = faces,
                SubjectRing = ring,
                FaceStreetNames = faceNames,
                ElementType = (LotElementType)_cmbElementType.SelectedItem,
                GroupingMode = (LotGroupingMode)_cmbGrouping.SelectedItem,
                DrawAdjacentLots = _chkAdjacent.IsChecked == true,
                DrawRemainingBlock = _chkBlock.IsChecked == true,
                DrawSidewalk = _chkSidewalk.IsChecked == true,
                SidewalkWidthFt = sidewalkWidth,
                SidewalkWidthSource = Math.Abs(sidewalkWidth - 12.0) < 1e-9 ? "Default" : "User",
                Draw3DMasses = _chkMasses.IsChecked == true,
                DrawSubjectMass = _chkSubjectMass.IsChecked == true,
                MassUseFootprints = _rbMassFootprint.IsChecked == true,
                ReplaceEditedMasses = _chkReplaceEditedMasses.IsChecked == true,
                DrawStreetLabels = _chkStreetLabels.IsChecked == true,
                ConfirmedFrontages = frontages,
                DrawZoningTable = _chkZoningTable.IsChecked == true,
                BlockLotSources = _blockLotSources.ToDictionary(r => r.Bbl, r => r.Chosen),
                BlockLotLegalRings = _blockLotSources
                    .Where(r => r.LegalAvailable)
                    .ToDictionary(r => r.Bbl, r => r.LegalRing),
                AdditionalBlocks = _additionalBlocks.ToList(),
                DrawRoadbeds = _chkRoadbeds.IsChecked == true,
                Roadbeds = _roadbeds,
                DrawRealSidewalks = _chkRealSidewalks.IsChecked == true,
                RealSidewalks = _realSidewalks,
                DrawCurbs = _chkCurbs.IsChecked == true,
                Curbs = _curbs,
                DrawContextTrees = _chkContextTrees.IsChecked == true,
                ContextTrees = _contextTrees
            };

            OperationResult<string> result = _drawService.Draw(_doc, _context, _footprints, options, App.Log);
            TaskDialog.Show("BauTools — NYC Lot Boundary", result.BuildSummary());
            _statusText.Text = result.HasProblems
                ? $"Drawn ({SourceLabel(_selectedSource.Value)}) with some skips/failures — see the summary."
                : $"Drawn successfully ({SourceLabel(_selectedSource.Value)}).";
        }
    }
}
