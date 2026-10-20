#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARYE.OfficeTools.Core.LightVent;
using ARYE.OfficeTools.Core.Results;
using ARYE.OfficeTools.Models;
using ARYE.OfficeTools.Services;
// Autodesk.Revit.UI / Autodesk.Revit.DB declare their own ComboBox, Color, Grid... — alias to WPF.
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using Grid = System.Windows.Controls.Grid;
using Line = System.Windows.Shapes.Line;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using TextBox = System.Windows.Controls.TextBox;

namespace ARYE.OfficeTools.Views
{
    /// <summary>
    /// BauTools — Room Light &amp; Vent. Settings on the left; on the right a plan preview of the
    /// rooms with every window colored by what happens to it, and the before/after table.
    /// Nothing is written until Apply is confirmed.
    /// </summary>
    public sealed class RoomLightVentWindow : Window
    {
        private const string TOOL_KEY = "RoomLightVent";
        private const double PLAN_W = 1000;
        private const double PLAN_H = 520;
        private const double PLAN_MARGIN = 24;

        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private readonly RoomLightVentService _svc;

        private readonly List<LightVentRoomRow> _rooms;
        private readonly List<LightVentWindowRow> _hostWindows;
        private readonly List<LightVentWindowRow> _linkWindows;
        private readonly bool _fromSelection;
        private readonly int _notEnclosed;
        private readonly int _unloadedLinks;
        private readonly int _visibleRoomCount;
        private bool _includeLinks;
        private LightVentPreview _preview;

        private bool _building = true;
        private bool _syncing;
        private long? _selectedRoom;

        // ── UI ────────────────────────────────────────────────────────────
        private ComboBox _cmbLightSrc, _cmbVentSrc, _cmbLightDst, _cmbVentDst;
        private TextBlock _lblSourceHint, _lblMissing, _lblSkipped, _lblSelInfo, _lblLastRun;
        private TextBlock _statChange, _statSame, _statUnchanged, _statSkipped;
        private Button _btnApply, _btnLinksNo, _btnLinksYes;
        private Canvas _plan;
        private DataGrid _gridRooms, _gridSkipped;
        private TabItem _tabSkipped;
        private readonly Dictionary<long, Path> _roomShapes = new Dictionary<long, Path>();
        private readonly List<KeyValuePair<LightVentWindowRow, Shape>> _windowShapes = new List<KeyValuePair<LightVentWindowRow, Shape>>();

        // ── Palette (same as the other BauTools windows) ──────────────────
        private static readonly Color COL_BG = Hex("#F1F5F9");
        private static readonly Color COL_CARD = Colors.White;
        private static readonly Color COL_DARK = Hex("#0F172A");
        private static readonly Color COL_ACCENT = Hex("#2563EB");
        private static readonly Color COL_MUTED = Hex("#64748B");
        private static readonly Color COL_BORDER = Hex("#CBD5E1");
        private static readonly Color COL_HEADER_BG = Hex("#1E293B");
        private static readonly Color COL_SUCCESS = Hex("#16A34A");
        private static readonly Color COL_WARN = Hex("#B45309");
        private static readonly Color COL_CURTAIN = Hex("#7C3AED");
        private static readonly Color COL_SKIP = Hex("#94A3B8");
        private static readonly Color COL_ROOM = Hex("#E8F0FB");
        private static readonly Color COL_ROOM_SEL = Hex("#BFD7F5");

        private static readonly Brush BR_ROW_CHANGE = new SolidColorBrush(Hex("#ECFDF5"));
        private static readonly Brush BR_ROW_SAME = new SolidColorBrush(Hex("#F8FAFC"));
        private static readonly Brush BR_ROW_WARN = new SolidColorBrush(Hex("#FFF7ED"));
        private static readonly Brush BR_ROW_BAD = new SolidColorBrush(Hex("#FEF2F2"));

        public RoomLightVentWindow(UIDocument uidoc, ICollection<ElementId> preselected)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _svc = new RoomLightVentService(_doc, uidoc.ActiveView);

            _rooms = _svc.LoadRooms(preselected, out _notEnclosed, out _fromSelection);
            _visibleRoomCount = new FilteredElementCollector(_doc, uidoc.ActiveView.Id)
                .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().GetElementCount();
            var roomIds = new HashSet<long>(_rooms.Select(r => r.Id.Value));
            _hostWindows = _svc.LoadHostWindows(roomIds, !_fromSelection);
            _linkWindows = _svc.LoadLinkedWindows(roomIds, out _unloadedLinks);

            Title = "BauTools — Room Light & Vent";
            Width = 1440;
            Height = 900;
            MinWidth = 1100;
            MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(COL_BG);
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            BuildUI();
            FillSelectors();
            _building = false;
            Recompute();
        }

        /// <summary>False when the run has no room: the command reports it instead of opening an empty window.</summary>
        public bool HasRooms => _rooms.Count > 0;

        public int NotEnclosedRooms => _notEnclosed;

        // ════════════════════════════════════════════════════════════════
        //  Layout
        // ════════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(Place(BuildHeader(), 0, 0));

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(370) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.Children.Add(Place(BuildLeft(), 0, 0));
            body.Children.Add(Place(BuildRight(), 0, 1));
            root.Children.Add(Place(body, 1, 0));

            root.Children.Add(Place(BuildFooter(), 2, 0));
            Content = root;
        }

        private UIElement BuildHeader()
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = "ROOM LIGHT & VENT",
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.Bold
            });
            sp.Children.Add(new TextBlock
            {
                Text = "Adds up the light area and the vent area of each room's exterior windows and writes the two totals to the room. " +
                       "Nothing is written until you review the preview and click Apply.",
                Foreground = new SolidColorBrush(Hex("#94A3B8")),
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            _lblLastRun = new TextBlock
            {
                Text = BauToolsRunStampService.ReadLastRun(_doc, TOOL_KEY, "room(s)"),
                Foreground = new SolidColorBrush(Hex("#60A5FA")),
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 11.5
            };
            Hint(_lblLastRun, "Saved in the project file. Still shown after you close and reopen the model.");
            sp.Children.Add(_lblLastRun);

            return new Border { Background = new SolidColorBrush(COL_HEADER_BG), Padding = new Thickness(18, 12, 18, 12), Child = sp };
        }

        // ── Left column: settings ─────────────────────────────────────────
        private UIElement BuildLeft()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(BuildRoomsCard());
            sp.Children.Add(BuildSourceCard());
            sp.Children.Add(BuildTargetCard());
            sp.Children.Add(BuildRulesCard());
            if (_linkWindows.Count > 0 || _unloadedLinks > 0) sp.Children.Add(BuildLinksCard());

            return new ScrollViewer
            {
                Content = sp,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Hex("#E9EEF4"))
            };
        }

        private UIElement BuildRoomsCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(Line2("View", _svc.ViewName));
            sp.Children.Add(Hint(Line2("Phase", _svc.PhaseName),
                "FromRoom / ToRoom are read in the phase of the active view."));

            string using_ = _fromSelection
                ? string.Format("{0} selected room(s) → using only those", _rooms.Count)
                : string.Format("{0} room(s) visible → using all {1}", _visibleRoomCount, _rooms.Count);
            sp.Children.Add(new TextBlock { Text = using_, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });

            if (_notEnclosed > 0)
            {
                sp.Children.Add(Warn(string.Format("{0} room(s) are not placed or not enclosed (area 0) and are left out.", _notEnclosed)));
            }
            return Card("1 · ROOMS", sp,
                "Rooms visible in the active view. If you selected rooms before clicking the button, only the selected rooms are used.");
        }

        private UIElement BuildSourceCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(Label("Light area from"));
            _cmbLightSrc = Combo();
            Hint(_cmbLightSrc, "Window parameter that holds the light (glazed) area. Every parameter of the window families in this model is listed, " +
                               "type parameters first: each entry shows Type or Instance, the data type, and how many families carry it. " +
                               "Area, Number, Integer and Text parameters can be chosen (text such as \"9.80 SF\" is read as a number); the rest are shown disabled.");
            _cmbLightSrc.SelectionChanged += delegate { OnPairChanged(_cmbLightSrc, _cmbVentSrc, "light area"); };
            sp.Children.Add(_cmbLightSrc);

            sp.Children.Add(Label("Vent area from"));
            _cmbVentSrc = Combo();
            Hint(_cmbVentSrc, "Window parameter that holds the openable (ventilation) area.");
            _cmbVentSrc.SelectionChanged += delegate { OnPairChanged(_cmbVentSrc, _cmbLightSrc, "vent area"); };
            sp.Children.Add(_cmbVentSrc);

            _lblSourceHint = Muted("If a value is empty, it is not added and the window is listed with that reason. The other value of the same window still counts.");
            sp.Children.Add(_lblSourceHint);
            _lblMissing = Warn(string.Empty);
            _lblMissing.Visibility = System.Windows.Visibility.Collapsed;
            sp.Children.Add(_lblMissing);

            return Card("2 · WINDOW PARAMETERS", sp,
                "Every parameter found on the window families in this model, type parameters first. Nothing is hardcoded. Your choice is saved in the project when you apply.");
        }

        private UIElement BuildTargetCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(Label("Write light total to"));
            _cmbLightDst = Combo();
            Hint(_cmbLightDst, "Room parameter that receives the sum of the light area of the room's exterior windows.");
            _cmbLightDst.SelectionChanged += delegate { OnPairChanged(_cmbLightDst, _cmbVentDst, "light total"); };
            sp.Children.Add(_cmbLightDst);

            sp.Children.Add(Label("Write vent total to"));
            _cmbVentDst = Combo();
            Hint(_cmbVentDst, "Room parameter that receives the sum of the vent area of the room's exterior windows.");
            _cmbVentDst.SelectionChanged += delegate { OnPairChanged(_cmbVentDst, _cmbLightDst, "vent total"); };
            sp.Children.Add(_cmbVentDst);

            sp.Children.Add(Muted("Rooms with no exterior windows are left unchanged."));
            return Card("3 · ROOM PARAMETERS", sp,
                "Every instance parameter of the rooms. Area and Number receive the exact value; Integer receives it rounded; Text receives it formatted " +
                "like Revit shows it (\"56.00 SF\"), which a schedule cannot add up. Read-only parameters, other data types and the room's Name and Number " +
                "are listed disabled, with the reason.");
        }

        private UIElement BuildRulesCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(Rule(COL_SUCCESS, "Counted", "windows with a room on one side only"));
            sp.Children.Add(Rule(COL_CURTAIN, "Counted", "window panels in curtain walls"));
            sp.Children.Add(Rule(COL_SKIP, "Skipped", "a room on both sides, or no room at all"));
            sp.Children.Add(Rule(COL_SKIP, "Skipped", "skylights (windows hosted in roofs)"));
            return Card("4 · WHAT COUNTS", sp,
                "Fixed rules of this tool. A curtain wall panel with no FromRoom/ToRoom is located by testing a point on each side of it.");
        }

        private UIElement BuildLinksCard()
        {
            var sp = new StackPanel();
            int links = _linkWindows.Select(w => w.LinkName).Distinct().Count();
            if (_linkWindows.Count > 0)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = string.Format("{0} window(s) in {1} linked model(s) touch these rooms. Include them?", _linkWindows.Count, links),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6)
                });
                var row = new WrapPanel();
                _btnLinksNo = FlatButton("This model only", COL_ACCENT, 130);
                _btnLinksNo.Click += delegate { SetIncludeLinks(false); };
                row.Children.Add(Hint(_btnLinksNo, string.Format("Use only windows in this model. The {0} linked window(s) are left out.", _linkWindows.Count)));
                _btnLinksYes = FlatButton("Include links", COL_MUTED, 110);
                _btnLinksYes.Click += delegate { SetIncludeLinks(true); };
                row.Children.Add(Hint(_btnLinksYes,
                    "Also read windows from linked models. Their room is found by testing a point on each side of the window, " +
                    "because FromRoom/ToRoom do not cross between models. Linked windows cannot be selected from this tool."));
                sp.Children.Add(row);
            }
            if (_unloadedLinks > 0)
            {
                sp.Children.Add(Warn(string.Format("{0} link(s) are not loaded; their windows cannot be read.", _unloadedLinks)));
            }
            return Card("LINKED MODELS", sp, "Shown only when a linked model has windows that touch these rooms.");
        }

        // ── Right column: preview ─────────────────────────────────────────
        private UIElement BuildRight()
        {
            var g = new Grid { Margin = new Thickness(14, 10, 14, 0) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.15, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var stats = new UniformGrid4();
            stats.Add(Stat(out _statChange, "rooms will change", "Rooms whose light or vent total will change.", COL_DARK));
            stats.Add(Stat(out _statSame, "already up to date", "Rooms whose current values already match.", COL_DARK));
            stats.Add(Stat(out _statUnchanged, "left unchanged", "Rooms with no exterior windows, or whose windows have only empty values. They are not written to.", COL_DARK));
            stats.Add(Stat(out _statSkipped, "windows skipped", "Windows not added to any room. The 'Skipped windows' tab lists each one with its reason.", COL_WARN));
            g.Children.Add(Place(stats.Panel, 0, 0));

            _lblSkipped = Muted(string.Empty);
            _lblSkipped.Margin = new Thickness(0, 6, 0, 8);
            g.Children.Add(Place(_lblSkipped, 1, 0));

            g.Children.Add(Place(BuildPlanCard(), 2, 0));
            g.Children.Add(Place(BuildTables(), 3, 0));
            return g;
        }

        private UIElement BuildPlanCard()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });

            _plan = new Canvas { Width = PLAN_W, Height = PLAN_H, Background = Brushes.White };
            var box = new Viewbox { Stretch = Stretch.Uniform, Child = _plan };
            var planBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(COL_BORDER),
                BorderThickness = new Thickness(1),
                Child = box
            };
            Hint(planBorder, "Drawn from the room boundaries and window locations. Click a room, here or in the table, to highlight the windows that count for it.");
            g.Children.Add(Place(planBorder, 0, 0));

            var side = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            side.Children.Add(Swatch(COL_SUCCESS, false, "Counted · wall window"));
            side.Children.Add(Swatch(COL_CURTAIN, false, "Counted · curtain wall panel"));
            side.Children.Add(Swatch(COL_WARN, false, "Skipped · empty values"));
            side.Children.Add(Swatch(COL_SKIP, false, "Skipped · interior / no room"));
            side.Children.Add(Swatch(COL_SKIP, true, "Skipped · skylight"));

            _lblSelInfo = Muted("Click a room to highlight its windows.");
            _lblSelInfo.Margin = new Thickness(0, 10, 0, 8);
            side.Children.Add(_lblSelInfo);

            Button all = FlatButton("Show all", COL_MUTED, 190);
            all.Click += delegate { SelectRoom(null, true); };
            side.Children.Add(Hint(all, "Clear the highlight and show every room and window again."));

            Button sel = FlatButton("Select in Revit", COL_ACCENT, 190);
            sel.Margin = new Thickness(0, 6, 0, 0);
            sel.Click += OnSelectInRevit;
            side.Children.Add(Hint(sel, "Select the highlighted room and its counted windows in Revit and zoom the active view to them. This window stays open."));

            g.Children.Add(Place(side, 0, 1));

            return Card("PLAN PREVIEW", g, null, stretch: true, margin: new Thickness(0, 0, 0, 8));
        }

        private UIElement BuildTables()
        {
            var tabs = new TabControl { Margin = new Thickness(0, 0, 0, 8) };

            _gridRooms = NewGrid();
            _gridRooms.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.VisibleWhenSelected;
            var details = new FrameworkElementFactory(typeof(TextBlock));
            details.SetBinding(TextBlock.TextProperty, new Binding("DetailsText"));
            details.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas"));
            details.SetValue(TextBlock.FontSizeProperty, 11.5);
            details.SetValue(TextBlock.MarginProperty, new Thickness(24, 4, 8, 8));
            details.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(COL_DARK));
            _gridRooms.RowDetailsTemplate = new DataTemplate { VisualTree = details };
            _gridRooms.Columns.Add(TextCol("Room", "RoomText", 190));
            _gridRooms.Columns.Add(TextCol("Ext. windows", "WindowsText", 170));
            _gridRooms.Columns.Add(TextCol("Light area (current → new)", "LightText", 210));
            _gridRooms.Columns.Add(TextCol("Vent area (current → new)", "VentText", 210));
            _gridRooms.Columns.Add(TextCol("Status", "StatusText", 110));
            _gridRooms.Columns.Add(TextCol("Notes", "NoteText", 1, star: true));
            _gridRooms.LoadingRow += OnRoomRowLoading;
            _gridRooms.SelectionChanged += delegate
            {
                if (_syncing) return;
                SelectRoom((_gridRooms.SelectedItem as LightVentRoomRow)?.Id.Value, false);
            };
            tabs.Items.Add(new TabItem
            {
                Header = "Rooms",
                Content = _gridRooms,
                ToolTip = "One row per room. Click a row to see its windows and highlight them in the plan."
            });

            _gridSkipped = NewGrid();
            _gridSkipped.Columns.Add(TextCol("Mark", "MarkText", 90));
            _gridSkipped.Columns.Add(TextCol("Family : Type", "TypeText", 260));
            _gridSkipped.Columns.Add(TextCol("Model", "ModelText", 140));
            _gridSkipped.Columns.Add(TextCol("Rooms on its sides", "RoomText", 140));
            _gridSkipped.Columns.Add(TextCol("Id", "IdText", 90));
            _gridSkipped.Columns.Add(TextCol("Reason", "ReasonText", 1, star: true));
            _tabSkipped = new TabItem
            {
                Header = "Skipped windows",
                Content = _gridSkipped,
                ToolTip = "Every window that was not added to a room, with the reason."
            };
            tabs.Items.Add(_tabSkipped);
            return tabs;
        }

        private UIElement BuildFooter()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock legend = Muted("WILL CHANGE = a total differs · UP TO DATE = already equal · UNCHANGED = no exterior windows or only empty values · " +
                                     "CANNOT WRITE = the room parameter is missing or read-only.");
            legend.VerticalAlignment = VerticalAlignment.Center;
            legend.Margin = new Thickness(0, 0, 12, 0);
            g.Children.Add(Place(legend, 0, 0));

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            Button csv = FlatButton("Export CSV", COL_MUTED, 110);
            csv.Click += OnExportCsv;
            right.Children.Add(Hint(csv, "Save the preview, with every window and its result or skip reason, as a CSV file."));

            Button close = FlatButton("Cancel", COL_DARK, 90);
            close.Click += delegate { Close(); };
            right.Children.Add(Hint(close, "Close without writing anything."));

            _btnApply = FlatButton("Apply", COL_SUCCESS, 200);
            _btnApply.FontWeight = FontWeights.Bold;
            _btnApply.Click += OnApply;
            right.Children.Add(_btnApply);
            g.Children.Add(Place(right, 0, 1));

            return new Border
            {
                Background = new SolidColorBrush(COL_CARD),
                BorderBrush = new SolidColorBrush(COL_BORDER),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10),
                Child = g
            };
        }

        // ════════════════════════════════════════════════════════════════
        //  Selectors
        // ════════════════════════════════════════════════════════════════

        private void FillSelectors()
        {
            List<LightVentParamOption> sources = _svc.DiscoverWindowParameters(_hostWindows.Concat(_linkWindows));
            List<LightVentParamOption> targets = _svc.DiscoverRoomParameters(_rooms);

            FillCombo(_cmbLightSrc, sources, "lightSource");
            FillCombo(_cmbVentSrc, sources, "ventSource");
            FillCombo(_cmbLightDst, targets, "lightTarget");
            FillCombo(_cmbVentDst, targets, "ventTarget");
            RefreshBlocked(_cmbLightSrc, _cmbVentSrc, "light area");
            RefreshBlocked(_cmbVentSrc, _cmbLightSrc, "vent area");
            RefreshBlocked(_cmbLightDst, _cmbVentDst, "light total");
            RefreshBlocked(_cmbVentDst, _cmbLightDst, "vent total");

            if (!sources.Any(o => o.IsUsable))
            {
                _lblSourceHint.Text = "No Area, Number, Integer or Text parameter was found on the windows of these rooms.";
                _lblSourceHint.Foreground = new SolidColorBrush(COL_WARN);
            }
        }

        private void FillCombo(ComboBox combo, List<LightVentParamOption> options, string settingName)
        {
            string saved = BauToolsRunStampService.ReadSetting(_doc, TOOL_KEY, settingName);
            combo.Items.Clear();
            foreach (LightVentParamOption o in options)
            {
                var item = new ComboBoxItem
                {
                    Content = o.Display,
                    Tag = o,
                    IsEnabled = o.IsUsable,
                    ToolTip = o.IsUsable ? null : "Not selectable: " + o.BlockedReason + "."
                };
                ToolTipService.SetShowOnDisabled(item, true);
                combo.Items.Add(item);
                if (saved != null && o.Key == saved && o.IsUsable) combo.SelectedItem = item;
            }
            if (options.Count == 0)
            {
                combo.Items.Add(new ComboBoxItem { Content = "(none found)", IsEnabled = false });
            }
        }

        private static LightVentParamOption Selected(ComboBox combo) =>
            (combo.SelectedItem as ComboBoxItem)?.Tag as LightVentParamOption;

        /// <summary>
        /// One parameter cannot feed both values (or receive both totals): the option taken by the
        /// partner selector is shown disabled with a hint, never silently swapped.
        /// </summary>
        private void OnPairChanged(ComboBox changed, ComboBox partner, string role)
        {
            if (_building) return;
            RefreshBlocked(partner, changed, role);
            Recompute();
        }

        private static void RefreshBlocked(ComboBox target, ComboBox owner, string ownerRole)
        {
            LightVentParamOption taken = Selected(owner);
            foreach (ComboBoxItem item in target.Items.OfType<ComboBoxItem>())
            {
                if (!(item.Tag is LightVentParamOption o)) continue;
                if (!o.IsUsable) continue;   // stays disabled with its own reason
                bool blocked = taken != null && o.Key == taken.Key;
                item.IsEnabled = !blocked;
                item.ToolTip = blocked ? "Already used for the " + ownerRole + "." : null;
                ToolTipService.SetShowOnDisabled(item, true);
            }
        }

        private void SetIncludeLinks(bool include)
        {
            _includeLinks = include;
            _btnLinksNo.Background = new SolidColorBrush(include ? COL_MUTED : COL_ACCENT);
            _btnLinksYes.Background = new SolidColorBrush(include ? COL_ACCENT : COL_MUTED);
            Recompute();
        }

        // ════════════════════════════════════════════════════════════════
        //  Preview
        // ════════════════════════════════════════════════════════════════

        private List<LightVentWindowRow> ActiveWindows() =>
            _includeLinks ? _hostWindows.Concat(_linkWindows).ToList() : _hostWindows;

        private void Recompute()
        {
            LightVentParamOption ls = Selected(_cmbLightSrc), vs = Selected(_cmbVentSrc);
            LightVentParamOption lt = Selected(_cmbLightDst), vt = Selected(_cmbVentDst);

            _preview = _svc.BuildPreview(_rooms, ActiveWindows(), ls, vs, lt, vt);

            _statChange.Text = _preview.WillChange.ToString();
            _statSame.Text = _preview.UpToDate.ToString();
            _statUnchanged.Text = _preview.LeftUnchanged.ToString();
            _statSkipped.Text = _preview.Skipped.Count.ToString();

            List<LightVentWindowRow> sk = _preview.Skipped;
            var parts = new List<string>
            {
                Count(sk, WindowOutcome.Interior) + " room on both sides",
                Count(sk, WindowOutcome.NoRoom) + " no room on either side",
                sk.Count(w => w.IsEmptySkip) + " empty light and vent value",
                Count(sk, WindowOutcome.Skylight) + " skylight"
            };
            int outside = Count(sk, WindowOutcome.OutsideScope);
            if (outside > 0) parts.Add(outside + " whose room is not in this run");
            string text = "Skipped: " + string.Join(" · ", parts);
            if (_linkWindows.Count > 0 && !_includeLinks)
                text += string.Format(" · {0} in linked models, not included", _linkWindows.Count);
            if (_preview.CannotWrite > 0)
                text += string.Format("   ·   {0} room(s) CANNOT WRITE (parameter missing or read-only)", _preview.CannotWrite);
            _lblSkipped.Text = text;

            var missing = new List<string>();
            if (_preview.FamiliesMissingLight.Count > 0)
                missing.Add(string.Format("{0} family(ies) without '{1}': {2}", _preview.FamiliesMissingLight.Count, ls.Name, string.Join(", ", _preview.FamiliesMissingLight)));
            if (_preview.FamiliesMissingVent.Count > 0)
                missing.Add(string.Format("{0} family(ies) without '{1}': {2}", _preview.FamiliesMissingVent.Count, vs.Name, string.Join(", ", _preview.FamiliesMissingVent)));
            _lblMissing.Text = string.Join("\n", missing);
            _lblMissing.Visibility = missing.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            _syncing = true;
            _gridRooms.ItemsSource = null;
            _gridRooms.ItemsSource = _preview.Rooms;
            _gridSkipped.ItemsSource = null;
            _gridSkipped.ItemsSource = _preview.Skipped;
            _tabSkipped.Header = string.Format("Skipped windows ({0})", _preview.Skipped.Count);
            _syncing = false;

            DrawPlan();
            SelectRoom(_selectedRoom, true);

            bool ready = ls != null && vs != null && lt != null && vt != null;
            _btnApply.IsEnabled = ready && _preview.WillChange > 0;
            _btnApply.Content = ready
                ? string.Format("Apply to {0} room(s)", _preview.WillChange)
                : "Choose the 4 parameters";
            _btnApply.ToolTip = ready
                ? string.Format("Write the totals to the {0} room(s) that change, as one undo step. A report with the final counts follows.", _preview.WillChange)
                : "Choose the two window parameters and the two room parameters first.";
            ToolTipService.SetShowOnDisabled(_btnApply, true);
        }

        private static int Count(List<LightVentWindowRow> list, WindowOutcome o) => list.Count(w => w.Outcome == o);

        // ── Plan ──────────────────────────────────────────────────────────
        private void DrawPlan()
        {
            _plan.Children.Clear();
            _roomShapes.Clear();
            _windowShapes.Clear();

            List<XYZ> pts = _rooms.SelectMany(r => r.Loops.SelectMany(l => l)).ToList();
            List<LightVentWindowRow> drawn = _preview.Rooms.SelectMany(r => r.Windows).Concat(_preview.Skipped.Where(w => !w.IsCounted)).Distinct().ToList();
            pts.AddRange(drawn.Select(w => w.Center));
            if (pts.Count == 0) return;

            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double s = Math.Min((PLAN_W - 2 * PLAN_MARGIN) / Math.Max(maxX - minX, 1), (PLAN_H - 2 * PLAN_MARGIN) / Math.Max(maxY - minY, 1));
            double ox = (PLAN_W - (maxX - minX) * s) / 2, oy = (PLAN_H - (maxY - minY) * s) / 2;
            Func<XYZ, Point> map = p => new Point(ox + (p.X - minX) * s, oy + (maxY - p.Y) * s);

            foreach (LightVentRoomRow room in _preview.Rooms)
            {
                var geo = new PathGeometry { FillRule = FillRule.EvenOdd };
                foreach (List<XYZ> loop in room.Loops)
                {
                    var fig = new PathFigure { StartPoint = map(loop[0]), IsClosed = true, IsFilled = true };
                    fig.Segments.Add(new PolyLineSegment(loop.Skip(1).Select(map), true));
                    geo.Figures.Add(fig);
                }
                var path = new Path
                {
                    Data = geo,
                    Fill = new SolidColorBrush(COL_ROOM),
                    Stroke = new SolidColorBrush(COL_DARK),
                    StrokeThickness = 1,
                    Cursor = Cursors.Hand,
                    ToolTip = string.Format("{0}\nLight: {1}\nVent: {2}\n{3}", room.RoomText, room.LightText, room.VentText, room.StatusText)
                };
                long id = room.Id.Value;
                path.MouseLeftButtonDown += delegate { SelectRoom(id, true); };
                _plan.Children.Add(path);
                _roomShapes[id] = path;
            }

            foreach (LightVentRoomRow room in _preview.Rooms.Where(r => r.LabelPoint != null))
            {
                Point p = map(room.LabelPoint);
                var label = new TextBlock
                {
                    Text = room.Number,
                    Width = 80,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(COL_DARK),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(label, p.X - 40);
                Canvas.SetTop(label, p.Y - 8);
                _plan.Children.Add(label);
            }

            foreach (LightVentWindowRow w in drawn)
            {
                Shape shape;
                double len = Math.Max(w.Width * s, 6);
                Point c = map(w.Center);
                if (w.Outcome == WindowOutcome.Skylight)
                {
                    var r = new Rectangle
                    {
                        Width = len,
                        Height = len,
                        Stroke = new SolidColorBrush(COL_SKIP),
                        StrokeThickness = 1.5,
                        StrokeDashArray = new DoubleCollection { 3, 2 },
                        Fill = Brushes.Transparent
                    };
                    Canvas.SetLeft(r, c.X - len / 2);
                    Canvas.SetTop(r, c.Y - len / 2);
                    shape = r;
                }
                else
                {
                    double dx = w.Along.X * len / 2, dy = -w.Along.Y * len / 2;
                    shape = new Line
                    {
                        X1 = c.X - dx,
                        Y1 = c.Y - dy,
                        X2 = c.X + dx,
                        Y2 = c.Y + dy,
                        Stroke = new SolidColorBrush(WindowColor(w)),
                        StrokeThickness = 6
                    };
                }
                shape.ToolTip = string.Format("{0} · {1}\n{2}\nLight {3} · Vent {4}\n{5}",
                    w.MarkText, w.TypeText, w.ModelText, _svc.Area(w.Light), _svc.Area(w.Vent),
                    w.IsCounted && !w.IsEmptySkip ? "Counted for " + w.RoomText + " (" + w.ReasonText + ")" : "Skipped: " + w.ReasonText);
                if (w.IsCounted)
                {
                    long rid = w.Resolution.RoomId.Value;
                    shape.Cursor = Cursors.Hand;
                    shape.MouseLeftButtonDown += delegate { SelectRoom(rid, true); };
                }
                _plan.Children.Add(shape);
                _windowShapes.Add(new KeyValuePair<LightVentWindowRow, Shape>(w, shape));
            }
        }

        private static Color WindowColor(LightVentWindowRow w)
        {
            if (!w.IsCounted) return COL_SKIP;
            if (w.IsEmptySkip) return COL_WARN;
            return w.Sides.IsCurtainPanel ? COL_CURTAIN : COL_SUCCESS;
        }

        /// <summary>Highlights a room and its windows in the plan, and keeps the table in step.</summary>
        private void SelectRoom(long? roomId, bool syncGrid)
        {
            if (roomId != null && !_roomShapes.ContainsKey(roomId.Value)) roomId = null;
            _selectedRoom = roomId;

            foreach (KeyValuePair<long, Path> kv in _roomShapes)
            {
                bool sel = kv.Key == roomId;
                kv.Value.Fill = new SolidColorBrush(sel ? COL_ROOM_SEL : COL_ROOM);
                kv.Value.Opacity = roomId == null || sel ? 1 : 0.45;
            }
            foreach (KeyValuePair<LightVentWindowRow, Shape> kv in _windowShapes)
            {
                bool mine = roomId != null && kv.Key.IsCounted && kv.Key.Resolution.RoomId == roomId;
                kv.Value.Opacity = roomId == null || mine ? 1 : 0.25;
                if (kv.Value is Line line) line.StrokeThickness = mine ? 10 : 6;
            }

            LightVentRoomRow row = roomId == null ? null : _preview.Rooms.FirstOrDefault(r => r.Id.Value == roomId);
            if (row == null)
            {
                _lblSelInfo.Text = "Click a room to highlight its windows.";
            }
            else
            {
                int counted = row.Windows.Count(w => !w.IsEmptySkip);
                _lblSelInfo.Text = string.Format("{0}\n{1} window(s) counted · {2} skipped\nLight: {3}\nVent: {4}",
                    row.RoomText, counted, row.Windows.Count - counted, row.LightText, row.VentText);
            }

            if (syncGrid && !_syncing)
            {
                _syncing = true;
                _gridRooms.SelectedItem = row;
                if (row != null) _gridRooms.ScrollIntoView(row);
                _syncing = false;
            }
        }

        private void OnRoomRowLoading(object sender, DataGridRowEventArgs e)
        {
            if (!(e.Row.Item is LightVentRoomRow r) || r.Totals == null) return;
            switch (r.Totals.Status)
            {
                case RoomLightVentStatus.WillChange: e.Row.Background = BR_ROW_CHANGE; break;
                case RoomLightVentStatus.CannotWrite: e.Row.Background = BR_ROW_BAD; break;
                case RoomLightVentStatus.UpToDate: e.Row.Background = BR_ROW_SAME; break;
                default: e.Row.Background = r.Totals.WindowsWithEmptyValues > 0 ? BR_ROW_WARN : Brushes.White; break;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Actions
        // ════════════════════════════════════════════════════════════════

        private void OnSelectInRevit(object sender, RoutedEventArgs e)
        {
            LightVentRoomRow row = _selectedRoom == null ? null : _preview.Rooms.FirstOrDefault(r => r.Id.Value == _selectedRoom);
            if (row == null)
            {
                TaskDialog.Show("BauTools", "Click a room in the plan or in the table first.");
                return;
            }

            var ids = new List<ElementId> { row.Id };
            ids.AddRange(row.Windows.Where(w => !w.IsLinked).Select(w => w.Id));
            int linked = row.Windows.Count(w => w.IsLinked);
            try
            {
                _uidoc.Selection.SetElementIds(ids);
                _uidoc.ShowElements(ids);
                _uidoc.RefreshActiveView();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                TaskDialog.Show("BauTools", "Could not select the elements: " + ex.Message);
                return;
            }
            if (linked > 0)
            {
                TaskDialog.Show("BauTools", string.Format(
                    "{0} selected with {1} window(s). {2} linked window(s) cannot be selected from here: Revit selects elements of linked models only with Tab in the view.",
                    row.RoomText, ids.Count - 1, linked));
            }
        }

        private void OnExportCsv(object sender, RoutedEventArgs e)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV file (*.csv)|*.csv",
                FileName = string.Format("BauTools_RoomLightVent_{0:yyyyMMdd_HHmm}.csv", DateTime.Now)
            };
            if (sfd.ShowDialog() != true) return;
            try
            {
                _svc.ExportCsv(sfd.FileName, _preview);
                TaskDialog.Show("BauTools", "Saved:\n" + sfd.FileName);
            }
            catch (System.IO.IOException ex)
            {
                TaskDialog.Show("BauTools", "Export failed: " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                TaskDialog.Show("BauTools", "Export failed: " + ex.Message);
            }
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            LightVentParamOption ls = Selected(_cmbLightSrc), vs = Selected(_cmbVentSrc);
            LightVentParamOption lt = Selected(_cmbLightDst), vt = Selected(_cmbVentDst);
            if (ls == null || vs == null || lt == null || vt == null || _preview.WillChange == 0) return;

            List<LightVentRoomRow> changing = _preview.Rooms.Where(r => r.WillChange).ToList();
            var lines = new List<string>
            {
                string.Format("{0} → {1}: {2} room(s)", ls.Name, lt.Name, changing.Count(r => r.Totals.LightChanges)),
                string.Format("{0} → {1}: {2} room(s)", vs.Name, vt.Name, changing.Count(r => r.Totals.VentChanges)),
                string.Format("Left unchanged: {0} room(s) · up to date: {1}", _preview.LeftUnchanged, _preview.UpToDate),
                string.Format("Windows skipped: {0} (listed in the 'Skipped windows' tab)", _preview.Skipped.Count)
            };
            if (_preview.CannotWrite > 0)
                lines.Add(string.Format("⚠ {0} room(s) cannot be written (parameter missing or read-only).", _preview.CannotWrite));
            if (_includeLinks)
                lines.Add(string.Format("Linked windows included: {0}", _linkWindows.Count));

            var td = new TaskDialog("BauTools — confirm")
            {
                MainInstruction = string.Format("Write light and vent totals to {0} room(s)?", changing.Count),
                MainContent = string.Join("\n", lines),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (td.Show() != TaskDialogResult.Yes) return;

            OperationResult<string> result = _svc.Write(_preview, lt, vt, out bool committed);

            if (committed)
            {
                BauToolsRunStampService.Stamp(_doc, TOOL_KEY,
                    _svc.ViewName + " → " + lt.Name + " / " + vt.Name,
                    result.Succeeded.Count, string.Empty,
                    new Dictionary<string, string>
                    {
                        { "lightSource", ls.Key },
                        { "ventSource", vs.Key },
                        { "lightTarget", lt.Key },
                        { "ventTarget", vt.Key }
                    });
                _lblLastRun.Text = BauToolsRunStampService.ReadLastRun(_doc, TOOL_KEY, "room(s)");
            }

            var res = new TaskDialog("BauTools — result")
            {
                MainInstruction = committed
                    ? string.Format("{0} room(s) written.", result.Succeeded.Count)
                    : "Nothing was written (the transaction was rolled back).",
                MainContent = result.BuildSummary()
            };
            res.Show();

            Recompute();
        }

        // ════════════════════════════════════════════════════════════════
        //  Small UI helpers
        // ════════════════════════════════════════════════════════════════

        private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        private static UIElement Place(UIElement el, int row, int col)
        {
            Grid.SetRow(el, row);
            Grid.SetColumn(el, col);
            return el;
        }

        private UIElement Card(string title, UIElement body, string tip, bool stretch = false, Thickness? margin = null)
        {
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 12, Foreground = new SolidColorBrush(COL_ACCENT) });
            if (tip != null) head.Children.Add(Info(tip));
            g.Children.Add(Place(head, 0, 0));
            g.Children.Add(Place(body, 1, 0));

            return new Border
            {
                Background = new SolidColorBrush(COL_CARD),
                BorderBrush = new SolidColorBrush(COL_BORDER),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = margin ?? new Thickness(12, 10, 12, 0),
                Padding = new Thickness(12, 10, 12, 12),
                Child = g
            };
        }

        private FrameworkElement Info(string tip)
        {
            var b = new Border
            {
                Width = 15,
                Height = 15,
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(COL_MUTED),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Help,
                Child = new TextBlock { Text = "i", FontSize = 10, Foreground = new SolidColorBrush(COL_MUTED), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            return Hint(b, tip);
        }

        private TextBlock Label(string text) =>
            new TextBlock { Text = text, Foreground = new SolidColorBrush(COL_DARK), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 3) };

        private TextBlock Muted(string text) =>
            new TextBlock { Text = text, Foreground = new SolidColorBrush(COL_MUTED), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

        private TextBlock Warn(string text) =>
            new TextBlock { Text = text, Foreground = new SolidColorBrush(COL_WARN), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

        private TextBlock Line2(string label, string value)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
            tb.Inlines.Add(new System.Windows.Documents.Run(label + ": ") { Foreground = new SolidColorBrush(COL_MUTED) });
            tb.Inlines.Add(new System.Windows.Documents.Run(value) { FontWeight = FontWeights.SemiBold });
            return tb;
        }

        private UIElement Rule(Color color, string verdict, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            sp.Children.Add(new Border
            {
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock { Text = verdict, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold }
            });
            sp.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 250 });
            return sp;
        }

        private UIElement Swatch(Color color, bool dashed, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            sp.Children.Add(new Rectangle
            {
                Width = 22,
                Height = dashed ? 10 : 7,
                Fill = dashed ? Brushes.Transparent : new SolidColorBrush(color),
                Stroke = dashed ? new SolidColorBrush(color) : null,
                StrokeDashArray = dashed ? new DoubleCollection { 3, 2 } : null,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock { Text = text, FontSize = 12 });
            return sp;
        }

        private UIElement Stat(out TextBlock number, string label, string tip, Color color)
        {
            number = new TextBlock { FontSize = 22, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(color) };
            var sp = new StackPanel();
            sp.Children.Add(number);
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(COL_MUTED), TextWrapping = TextWrapping.Wrap });
            var b = new Border
            {
                Background = new SolidColorBrush(COL_CARD),
                BorderBrush = new SolidColorBrush(color == COL_WARN ? COL_WARN : COL_BORDER),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Child = sp
            };
            return Hint(b, tip);
        }

        private ComboBox Combo() =>
            new ComboBox { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(4, 3, 4, 3), HorizontalAlignment = HorizontalAlignment.Stretch };

        private Button FlatButton(string text, Color color, double width) =>
            new Button
            {
                Content = text,
                Width = width,
                Height = 28,
                Margin = new Thickness(0, 2, 8, 0),
                Background = new SolidColorBrush(color),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

        /// <summary>Attaches the tooltip and returns the element, so controls can be added inline.</summary>
        private static T Hint<T>(T el, string tip) where T : FrameworkElement
        {
            el.ToolTip = tip;
            ToolTipService.SetShowDuration(el, 30000);
            return el;
        }

        private DataGrid NewGrid() =>
            new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                RowHeaderWidth = 0,
                SelectionMode = DataGridSelectionMode.Single,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(COL_BORDER),
                BorderThickness = new Thickness(1),
                FontSize = 12
            };

        private static DataGridTextColumn TextCol(string header, string path, double width, bool star = false) =>
            new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(path),
                Width = star ? new DataGridLength(width, DataGridLengthUnitType.Star) : new DataGridLength(width),
                IsReadOnly = true
            };

        /// <summary>Four equal columns for the stat tiles.</summary>
        private sealed class UniformGrid4
        {
            public readonly Grid Panel = new Grid();
            private int _n;

            public UniformGrid4()
            {
                for (int i = 0; i < 4; i++) Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            public void Add(UIElement el)
            {
                Grid.SetColumn(el, _n++);
                Panel.Children.Add(el);
            }
        }
    }
}
