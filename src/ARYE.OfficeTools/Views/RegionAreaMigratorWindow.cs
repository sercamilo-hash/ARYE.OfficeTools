#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARYE.OfficeTools.Models;
using ARYE.OfficeTools.Services;
// Autodesk.Revit.UI and Autodesk.Revit.DB each declare their own ComboBox/TextBox/Color —
// legacy in-canvas UI types unrelated to WPF. Alias to the WPF ones this window actually uses.
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;
using Grid = System.Windows.Controls.Grid;
using Binding = System.Windows.Data.Binding;

namespace ARYE.OfficeTools.Views
{
    /// <summary>
    /// BauTools — Region Area → Parameter.
    /// Migrates the computed area of one or many Filled Regions into a writable parameter,
    /// with a source selector, a target-parameter selector and a before/after preview that must
    /// be confirmed before anything is written.
    /// </summary>
    public class RegionAreaMigratorWindow : Window
    {
        private const string TOOL_KEY = "RegionAreaToParameter";

        // The ARYE/ZFA template already carries a parameter with this exact name for this
        // purpose (confirmed 2026-09-07 against a real Filled Region: "Other" group, currently
        // blank). Pre-select it over the generic "first Area-type instance parameter" fallback
        // whenever it is present and writable.
        private const string PREFERRED_PARAM_NAME = "Region Area";

        // Sentinel item in the Building/Side selectors: leaving it selected means this run does
        // not touch that parameter on any region.
        private const string NO_CHANGE = "— (do not change) —";

        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private readonly RegionAreaMigrationService _svc;
        private readonly RegionParameterDiscoveryService _params;
        private readonly RegionIdentityStampService _identity;
        private readonly RegionAreaWriteOptions _opt = new RegionAreaWriteOptions();

        private List<FilledRegion> _base = new List<FilledRegion>();          // scope, unfiltered
        private List<FilledRegion> _filtered = new List<FilledRegion>();      // after filters
        private List<ParameterTargetOption> _targets = new List<ParameterTargetOption>();
        private readonly ObservableCollection<RegionAreaMigrationItem> _rows = new ObservableCollection<RegionAreaMigrationItem>();

        private bool _building = true;

        // ── UI ────────────────────────────────────────────────────────────
        private ComboBox _cmbScope, _cmbType, _cmbView, _cmbParam, _cmbPolicy, _cmbTextMode, _cmbDecimals;
        private ComboBox _cmbBuilding, _cmbSide;
        private TextBox _txtSearch, _txtSuffix;
        private CheckBox _chkMasking, _chkShowType, _chkOnlyCompatible, _chkRound, _chkThousands;
        private Button _btnCreateParam, _btnHighlight, _btnCsv, _btnWrite;
        private TextBlock _lblScopeInfo, _lblCategory, _lblParamInfo, _lblSummary, _lblLastRun;
        private TextBlock _lblBuildingHint, _lblSideHint;
        private StackPanel _panelText;
        private DataGrid _grid;

        // ── Palette (BauTools) ────────────────────────────────────────────
        private static readonly Color COL_BG = (Color)ColorConverter.ConvertFromString("#F1F5F9");
        private static readonly Color COL_CARD = Colors.White;
        private static readonly Color COL_DARK = (Color)ColorConverter.ConvertFromString("#0F172A");
        private static readonly Color COL_ACCENT = (Color)ColorConverter.ConvertFromString("#2563EB");
        private static readonly Color COL_MUTED = (Color)ColorConverter.ConvertFromString("#64748B");
        private static readonly Color COL_BORDER = (Color)ColorConverter.ConvertFromString("#CBD5E1");
        private static readonly Color COL_HEADER_BG = (Color)ColorConverter.ConvertFromString("#1E293B");
        private static readonly Color COL_SUCCESS = (Color)ColorConverter.ConvertFromString("#16A34A");
        private static readonly Color COL_WARN = (Color)ColorConverter.ConvertFromString("#B45309");
        private static readonly Color COL_DANGER = (Color)ColorConverter.ConvertFromString("#B91C1C");

        private static readonly Brush BR_ROW_NEW = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
        private static readonly Brush BR_ROW_OVER = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF7ED"));
        private static readonly Brush BR_ROW_SAME = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
        private static readonly Brush BR_ROW_BAD = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));

        public RegionAreaMigratorWindow(UIDocument uidoc)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _svc = new RegionAreaMigrationService(_doc);
            _params = new RegionParameterDiscoveryService(_doc);
            _identity = new RegionIdentityStampService(_doc);

            Title = "BauTools — Region Area → Parameter";
            Width = 1240;
            Height = 830;
            MinWidth = 1000;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(COL_BG);
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            _opt.TextSuffix = RevitSpecHelper.ProjectAreaUnitSuffix(_doc);

            BuildUI();
            _building = false;

            ReloadScope(true);

            // Nothing pre-selected: fall back to the active view so the tool opens with content.
            if (_base.Count == 0)
                _cmbScope.SelectedIndex = 1;
        }

        // ════════════════════════════════════════════════════════════════
        //  UI
        // ════════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // step 1
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // step 2
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // step 3 (identity)
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // step 4
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // footer

            root.Children.Add(BuildHeader());
            root.Children.Add(Place(BuildSourceCard(), 1));
            root.Children.Add(Place(BuildTargetCard(), 2));
            root.Children.Add(Place(BuildIdentityCard(), 3));
            root.Children.Add(Place(BuildPreviewCard(), 4));
            root.Children.Add(Place(BuildFooter(), 5));

            Content = root;
        }

        private UIElement Place(UIElement el, int row)
        {
            Grid.SetRow(el, row);
            return el;
        }

        private UIElement BuildHeader()
        {
            Border b = new Border
            {
                Background = new SolidColorBrush(COL_HEADER_BG),
                Padding = new Thickness(18, 12, 18, 12)
            };

            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = "REGION AREA → PARAMETER",
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.Bold
            });
            sp.Children.Add(new TextBlock
            {
                Text = "Writes the computed area of the selected Filled Regions into a writable parameter. Nothing is written until you review the preview and confirm.",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            _lblLastRun = new TextBlock
            {
                Text = BauToolsRunStampService.ReadLastRun(_doc, TOOL_KEY),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60A5FA")),
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 11.5
            };
            sp.Children.Add(_lblLastRun);

            b.Child = sp;
            Grid.SetRow(b, 0);
            return b;
        }

        // ── Step 1 · source ───────────────────────────────────────────────
        private UIElement BuildSourceCard()
        {
            StackPanel body = new StackPanel();

            WrapPanel line1 = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };

            line1.Children.Add(Label("Scope"));
            _cmbScope = Combo(240);
            _cmbScope.Items.Add("Current selection");
            _cmbScope.Items.Add("Active view");
            _cmbScope.Items.Add("Entire model (all views)");
            _cmbScope.SelectedIndex = 0;
            _cmbScope.SelectionChanged += delegate { if (!_building) ReloadScope(true); };
            line1.Children.Add(Hint(_cmbScope, "Where to look for Filled Regions. 'Current selection' uses whatever you picked before launching the tool."));

            line1.Children.Add(Label("Region type"));
            _cmbType = Combo(230);
            _cmbType.SelectionChanged += delegate { if (!_building) ApplyFilters(true); };
            line1.Children.Add(Hint(_cmbType, "Filter by Filled Region type (hatch)."));

            line1.Children.Add(Label("Owner view"));
            _cmbView = Combo(230);
            _cmbView.SelectionChanged += delegate { if (!_building) ApplyFilters(true); };
            line1.Children.Add(Hint(_cmbView, "A Filled Region belongs to one view. Filter by that view."));

            line1.Children.Add(Label("Search"));
            _txtSearch = new TextBox { Width = 160, Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(4, 3, 4, 3) };
            _txtSearch.TextChanged += delegate { if (!_building) ApplyFilters(true); };
            line1.Children.Add(Hint(_txtSearch, "Matches type name and view name."));

            _chkMasking = new CheckBox
            {
                Content = "Include masking regions",
                IsChecked = true,
                Margin = new Thickness(0, 4, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _chkMasking.Checked += delegate { if (!_building) ApplyFilters(true); };
            _chkMasking.Unchecked += delegate { if (!_building) ApplyFilters(true); };
            line1.Children.Add(Hint(_chkMasking,
                "Masking regions are Filled Regions with no fill pattern — the ARYE energy-wall diagrams use them for edge/insulation " +
                "strips like SLAB EDGE, which carry real area. On by default for this tool; untick to exclude them."));

            Button reload = FlatButton("Reload", COL_MUTED, 84);
            reload.Click += delegate { ReloadScope(true); };
            line1.Children.Add(reload);

            body.Children.Add(line1);

            _lblScopeInfo = new TextBlock { Foreground = new SolidColorBrush(COL_DARK), FontWeight = FontWeights.SemiBold };
            body.Children.Add(_lblScopeInfo);

            _lblCategory = new TextBlock { Foreground = new SolidColorBrush(COL_MUTED), FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };
            body.Children.Add(_lblCategory);

            return Card("1 · SOURCE — which regions", body);
        }

        // ── Step 2 · target parameter ─────────────────────────────────────
        private UIElement BuildTargetCard()
        {
            StackPanel body = new StackPanel();

            WrapPanel line1 = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };

            line1.Children.Add(Label("Target parameter"));
            _cmbParam = Combo(520);
            _cmbParam.SelectionChanged += delegate { if (!_building) { UpdateParamInfo(); RebuildPreview(); } };
            line1.Children.Add(Hint(_cmbParam,
                "Only parameters that really exist on the selected regions and are writable are listed. " +
                "Each entry shows Instance/Type, Shared/Project/Built-in, data type and on how many of the selected regions it exists."));

            _chkOnlyCompatible = new CheckBox
            {
                Content = "Only compatible",
                IsChecked = true,
                Margin = new Thickness(0, 4, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _chkOnlyCompatible.Checked += delegate { if (!_building) RefreshTargets(); };
            _chkOnlyCompatible.Unchecked += delegate { if (!_building) RefreshTargets(); };
            line1.Children.Add(Hint(_chkOnlyCompatible, "Hides parameters that cannot receive an area (Length, Volume, Yes/No...)."));

            _chkShowType = new CheckBox
            {
                Content = "Show TYPE parameters",
                IsChecked = false,
                Margin = new Thickness(0, 4, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _chkShowType.Checked += delegate { if (!_building) RefreshTargets(); };
            _chkShowType.Unchecked += delegate { if (!_building) RefreshTargets(); };
            line1.Children.Add(Hint(_chkShowType,
                "Type parameters are shared by every instance of a Filled Region type: writing an area there overwrites itself region after region. Off by default."));

            _btnCreateParam = FlatButton("Create new…", COL_ACCENT, 116);
            _btnCreateParam.Click += OnCreateParameter;
            line1.Children.Add(Hint(_btnCreateParam, "Creates a shared parameter bound as an INSTANCE parameter to the Filled Region category of this project."));

            body.Children.Add(line1);

            _lblParamInfo = new TextBlock { Foreground = new SolidColorBrush(COL_MUTED), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            body.Children.Add(_lblParamInfo);

            // ── value & policy line ──
            WrapPanel line2 = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

            line2.Children.Add(Label("If it already has a value"));
            _cmbPolicy = Combo(210);
            _cmbPolicy.Items.Add("Overwrite when different");
            _cmbPolicy.Items.Add("Fill empty only");
            _cmbPolicy.Items.Add("Force rewrite always");
            _cmbPolicy.SelectedIndex = 0;
            _cmbPolicy.SelectionChanged += delegate
            {
                if (_building) return;
                _opt.Policy = _cmbPolicy.SelectedIndex == 1 ? OverwritePolicy.FillEmptyOnly
                            : _cmbPolicy.SelectedIndex == 2 ? OverwritePolicy.OverwriteAll
                            : OverwritePolicy.OverwriteDifferent;
                RebuildPreview();
            };
            line2.Children.Add(_cmbPolicy);

            _chkRound = new CheckBox
            {
                Content = "Write rounded value",
                IsChecked = false,
                Margin = new Thickness(0, 4, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _chkRound.Checked += delegate { if (!_building) { _opt.RoundNumericToDisplay = true; RebuildPreview(); } };
            _chkRound.Unchecked += delegate { if (!_building) { _opt.RoundNumericToDisplay = false; RebuildPreview(); } };
            line2.Children.Add(Hint(_chkRound,
                "Off: the exact area is stored, so a schedule summing the parameter matches a schedule summing the area. " +
                "On: the value is rounded to the decimals below, matching what a tag displays."));

            _cmbDecimals = Combo(70);
            _cmbDecimals.Items.Add("0");
            _cmbDecimals.Items.Add("1");
            _cmbDecimals.Items.Add("2");
            _cmbDecimals.Items.Add("3");
            _cmbDecimals.SelectedIndex = 2;
            _cmbDecimals.SelectionChanged += delegate
            {
                if (_building) return;
                _opt.NumericDecimals = _cmbDecimals.SelectedIndex;
                _opt.TextDecimals = _cmbDecimals.SelectedIndex;
                RebuildPreview();
            };
            line2.Children.Add(Hint(_cmbDecimals, "Decimals used for rounding and for text output."));

            body.Children.Add(line2);

            // ── text formatting sub-panel (only for text targets) ──
            _panelText = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = System.Windows.Visibility.Collapsed };
            WrapPanel line3 = new WrapPanel();

            line3.Children.Add(Label("Text value"));
            _cmbTextMode = Combo(230);
            _cmbTextMode.Items.Add("Revit format (project units)");
            _cmbTextMode.Items.Add("Number only");
            _cmbTextMode.Items.Add("Custom (decimals + suffix)");
            _cmbTextMode.SelectedIndex = 0;
            _cmbTextMode.SelectionChanged += delegate
            {
                if (_building) return;
                _opt.TextMode = _cmbTextMode.SelectedIndex == 1 ? TextValueMode.NumberOnly
                              : _cmbTextMode.SelectedIndex == 2 ? TextValueMode.CustomFormatted
                              : TextValueMode.RevitFormatted;
                RebuildPreview();
            };
            line3.Children.Add(_cmbTextMode);

            line3.Children.Add(Label("Suffix"));
            _txtSuffix = new TextBox { Width = 70, Text = _opt.TextSuffix, Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(4, 3, 4, 3) };
            _txtSuffix.TextChanged += delegate { if (!_building) { _opt.TextSuffix = _txtSuffix.Text; RebuildPreview(); } };
            line3.Children.Add(_txtSuffix);

            _chkThousands = new CheckBox
            {
                Content = "Thousands separator",
                IsChecked = true,
                Margin = new Thickness(0, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _chkThousands.Checked += delegate { if (!_building) { _opt.TextThousandsSeparator = true; RebuildPreview(); } };
            _chkThousands.Unchecked += delegate { if (!_building) { _opt.TextThousandsSeparator = false; RebuildPreview(); } };
            line3.Children.Add(_chkThousands);

            _panelText.Children.Add(line3);
            body.Children.Add(_panelText);

            return Card("2 · TARGET — which parameter and what value", body);
        }

        // ── Step 3 · identity stamp (Building & Side) ─────────────────────
        private UIElement BuildIdentityCard()
        {
            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel buildingCol = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            buildingCol.Children.Add(Label("Building"));
            _cmbBuilding = Combo(320);
            _cmbBuilding.IsEditable = true;
            _cmbBuilding.Margin = new Thickness(0, 2, 0, 4);
            _cmbBuilding.SelectionChanged += delegate { if (!_building) DeferRefreshIdentityRows(); };
            _cmbBuilding.LostKeyboardFocus += delegate { if (!_building) DeferRefreshIdentityRows(); };
            buildingCol.Children.Add(Hint(_cmbBuilding,
                "One Building value for every checked region in this run. Values are discovered from what " +
                "other regions already carry — editable, so you can type one that does not exist yet."));
            _lblBuildingHint = new TextBlock { Foreground = new SolidColorBrush(COL_MUTED), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            buildingCol.Children.Add(_lblBuildingHint);
            Grid.SetColumn(buildingCol, 0);
            Grid.SetRow(buildingCol, 0);
            body.Children.Add(buildingCol);

            StackPanel sideCol = new StackPanel();
            sideCol.Children.Add(Label("Side"));
            _cmbSide = Combo(320);
            _cmbSide.IsEditable = true;
            _cmbSide.Margin = new Thickness(0, 2, 0, 4);
            _cmbSide.SelectionChanged += delegate { if (!_building) DeferRefreshIdentityRows(); };
            _cmbSide.LostKeyboardFocus += delegate { if (!_building) DeferRefreshIdentityRows(); };
            sideCol.Children.Add(Hint(_cmbSide,
                "One Side value for every checked region in this run — same idea as Building, suggested from the owner view's name when it names a single cardinal direction."));
            _lblSideHint = new TextBlock { Foreground = new SolidColorBrush(COL_MUTED), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            sideCol.Children.Add(_lblSideHint);
            Grid.SetColumn(sideCol, 1);
            Grid.SetRow(sideCol, 0);
            body.Children.Add(sideCol);

            TextBlock note = new TextBlock
            {
                Text = "Leaving a selector on \"" + NO_CHANGE + "\" means this run does not touch that parameter on any region.",
                Foreground = new SolidColorBrush(COL_MUTED),
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(note, 0);
            Grid.SetColumnSpan(note, 2);
            Grid.SetRow(note, 1);
            body.Children.Add(note);

            return Card("3 · IDENTITY — Building & Side (one value for the whole run)", body);
        }

        // ── Step 3 · preview ──────────────────────────────────────────────
        private UIElement BuildPreviewCard()
        {
            Grid body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // selection helpers
            WrapPanel tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };

            Button bAll = FlatButton("Check all", COL_MUTED, 86);
            bAll.Click += delegate { SetChecks(delegate (RegionAreaMigrationItem i) { return i.IsWritable; }); };
            tools.Children.Add(bAll);

            Button bNone = FlatButton("Check none", COL_MUTED, 92);
            bNone.Click += delegate { SetChecks(delegate (RegionAreaMigrationItem i) { return false; }); };
            tools.Children.Add(bNone);

            Button bNew = FlatButton("Only NEW", COL_MUTED, 88);
            bNew.Click += delegate { SetChecks(delegate (RegionAreaMigrationItem i) { return i.Status == MigrationStatus.New; }); };
            tools.Children.Add(bNew);

            Button bFlag = FlatButton("Only flagged", COL_WARN, 100);
            bFlag.Click += delegate
            {
                SetChecks(delegate (RegionAreaMigrationItem i)
                {
                    return i.IsWritable && (i.DeltaFlagged || (i.Geometry != null && i.Geometry.OrientationAmbiguous));
                });
            };
            tools.Children.Add(Hint(bFlag, "Rows where the computed area and Revit's own Area differ, or where the boundary loops are ambiguous."));

            _btnHighlight = FlatButton("Select in model", COL_ACCENT, 122);
            _btnHighlight.Click += OnHighlight;
            tools.Children.Add(Hint(_btnHighlight, "Selects the highlighted row in Revit, and zooms to it when it belongs to the active view."));

            _btnCsv = FlatButton("Export CSV", COL_MUTED, 100);
            _btnCsv.Click += OnExportCsv;
            tools.Children.Add(_btnCsv);

            Grid.SetRow(tools, 0);
            body.Children.Add(tools);

            // grid
            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                RowHeaderWidth = 0,
                SelectionMode = DataGridSelectionMode.Single,
                ItemsSource = _rows,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(COL_BORDER),
                BorderThickness = new Thickness(1),
                FontSize = 12
            };

            _grid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "✔",
                Binding = new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 34
            });

            _grid.Columns.Add(TextCol("Status", "StatusText", 104));
            _grid.Columns.Add(TextCol("Id", "IdText", 76));
            _grid.Columns.Add(TextCol("Region type", "TypeName", 170));
            _grid.Columns.Add(TextCol("Owner view", "ViewName", 170));
            _grid.Columns.Add(TextCol("Computed area", "AreaText", 116));
            _grid.Columns.Add(TextCol("Revit Area", "RevitAreaText", 100));
            _grid.Columns.Add(TextCol("Δ", "DeltaText", 70));
            _grid.Columns.Add(TextCol("Current value", "CurrentValue", 130));
            _grid.Columns.Add(TextCol("Proposed value", "ProposedValue", 130));
            _grid.Columns.Add(TextCol("Building", "BuildingDisplay", 150));
            _grid.Columns.Add(TextCol("Side", "SideDisplay", 110));

            DataGridTextColumn note = TextCol("Notes", "Note", 190);
            note.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            _grid.Columns.Add(note);

            _grid.LoadingRow += OnLoadingRow;

            Grid.SetRow(_grid, 1);
            body.Children.Add(_grid);

            _lblSummary = new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(COL_DARK),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_lblSummary, 2);
            body.Children.Add(_lblSummary);

            return Card("4 · PREVIEW — before / after", body, true);
        }

        private UIElement BuildFooter()
        {
            Border b = new Border
            {
                Background = new SolidColorBrush(COL_CARD),
                BorderBrush = new SolidColorBrush(COL_BORDER),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock legend = new TextBlock
            {
                Text = "NEW = parameter was empty · OVERWRITE = had a different value · NO CHANGE = already equal · NO PARAM / READ-ONLY / BAD GEOMETRY are skipped.",
                Foreground = new SolidColorBrush(COL_MUTED),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(legend, 0);
            g.Children.Add(legend);

            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal };

            Button refresh = FlatButton("Refresh preview", COL_MUTED, 130);
            refresh.Click += delegate { RebuildPreview(); };
            right.Children.Add(refresh);

            _btnWrite = FlatButton("APPLY CHANGES", COL_SUCCESS, 200);
            _btnWrite.FontWeight = FontWeights.Bold;
            _btnWrite.Click += OnWrite;
            right.Children.Add(_btnWrite);

            Button close = FlatButton("Close", COL_DARK, 84);
            close.Click += delegate { Close(); };
            right.Children.Add(close);

            Grid.SetColumn(right, 1);
            g.Children.Add(right);

            b.Child = g;
            return b;
        }

        // ════════════════════════════════════════════════════════════════
        //  Data flow
        // ════════════════════════════════════════════════════════════════

        private void ReloadScope(bool rebuildCombos)
        {
            try
            {
                RegionSourceMode mode = _cmbScope.SelectedIndex == 1 ? RegionSourceMode.ActiveView
                                      : _cmbScope.SelectedIndex == 2 ? RegionSourceMode.EntireModel
                                      : RegionSourceMode.CurrentSelection;

                ICollection<ElementId> sel = null;
                try { sel = _uidoc.Selection.GetElementIds(); } catch { }

                ElementId activeView = null;
                try { activeView = _uidoc.ActiveView != null ? _uidoc.ActiveView.Id : ElementId.InvalidElementId; } catch { }

                _base = _svc.Gather(mode, sel, activeView);

                if (rebuildCombos) RebuildScopeCombos();
                ApplyFilters(true);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BauTools", "Could not read the model: " + ex.Message);
            }
        }

        private void RebuildScopeCombos()
        {
            bool prev = _building;
            _building = true;
            try
            {
                _cmbType.Items.Clear();
                _cmbType.Items.Add(new IdOption(null, "All types"));
                foreach (IdOption o in _base
                    .Select(r => new IdOption(r.GetTypeId(), _svc.TypeNameOf(r)))
                    .GroupBy(o => o.Key).Select(g => g.First())
                    .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase))
                    _cmbType.Items.Add(o);
                _cmbType.SelectedIndex = 0;

                _cmbView.Items.Clear();
                _cmbView.Items.Add(new IdOption(null, "All views"));
                foreach (IdOption o in _base
                    .Select(r => new IdOption(r.OwnerViewId, _svc.ViewNameOf(r)))
                    .GroupBy(o => o.Key).Select(g => g.First())
                    .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase))
                    _cmbView.Items.Add(o);
                _cmbView.SelectedIndex = 0;
            }
            finally { _building = prev; }
        }

        private void ApplyFilters(bool refreshTargets)
        {
            RegionSourceFilter f = new RegionSourceFilter();
            f.Mode = RegionSourceMode.CurrentSelection;   // irrelevant: filtering an already gathered set
            f.IncludeMasking = _chkMasking.IsChecked == true;
            f.Search = _txtSearch.Text;

            IdOption t = _cmbType.SelectedItem as IdOption;
            if (t != null && t.Id != null) f.TypeIds.Add(t.Id);

            IdOption v = _cmbView.SelectedItem as IdOption;
            if (v != null && v.Id != null) f.ViewIds.Add(v.Id);

            _filtered = _svc.Filter(_base, f);

            int masking = _base.Count(_svc.IsMasking);
            _lblScopeInfo.Text = string.Format("{0} Filled Region(s) in scope · {1} after filters{2}",
                _base.Count, _filtered.Count,
                masking > 0 ? string.Format(" · {0} masking region(s) found", masking) : string.Empty);

            _lblCategory.Text = _filtered.Count > 0
                ? "Category reported by Revit for these elements: " + RegionParameterDiscoveryService.DescribeCategory(_filtered[0])
                : "Nothing in scope. Pick regions in the model, or switch the scope above.";

            if (refreshTargets)
            {
                // Identity suggestions depend on _filtered, and must be in place BEFORE
                // RefreshTargets() ends by rebuilding the preview rows.
                RefreshIdentityOptions();
                RefreshTargets();
            }
            else RebuildPreview();
        }

        private void RefreshTargets()
        {
            bool prev = _building;
            _building = true;
            string keep = null;
            try
            {
                ParameterTargetOption cur = _cmbParam.SelectedItem as ParameterTargetOption;
                if (cur != null) keep = cur.Key;

                _targets = _params.Discover(_filtered, _chkShowType.IsChecked == true);
                if (_chkOnlyCompatible.IsChecked == true)
                    _targets = _targets.Where(o => o.CanReceiveArea).ToList();

                _cmbParam.Items.Clear();
                foreach (ParameterTargetOption o in _targets) _cmbParam.Items.Add(o);

                int idx = -1;
                if (keep != null) idx = _targets.FindIndex(o => o.Key == keep);
                if (idx < 0) idx = _targets.FindIndex(o => o.CanReceiveArea && o.Binding == TargetBinding.Instance
                    && string.Equals(o.Name, PREFERRED_PARAM_NAME, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) idx = _targets.FindIndex(o => o.CanReceiveArea
                    && string.Equals(o.Name, PREFERRED_PARAM_NAME, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) idx = _targets.FindIndex(o => o.IsAreaSpec && o.Binding == TargetBinding.Instance);
                if (idx < 0 && _targets.Count > 0) idx = 0;
                _cmbParam.SelectedIndex = idx;
            }
            finally { _building = prev; }

            UpdateParamInfo();
            RebuildPreview();
        }

        private void UpdateParamInfo()
        {
            ParameterTargetOption o = _cmbParam.SelectedItem as ParameterTargetOption;

            if (o == null)
            {
                _lblParamInfo.Text = _filtered.Count == 0
                    ? "No regions in scope, so no parameters to offer."
                    : "No writable parameter found on these regions. Use “Create new…” to add one, or untick “Only compatible” to see everything.";
                _lblParamInfo.Foreground = new SolidColorBrush(COL_DANGER);
                _panelText.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            string unit = RevitSpecHelper.ProjectAreaUnitSuffix(_doc);
            string how;
            if (o.Storage == StorageType.String)
                how = "The area is written as formatted text (" + unit + ").";
            else if (o.IsAreaSpec)
                how = "Area data type: the exact value is stored in Revit internal units and displayed in project units (" + unit + "). Schedulable and taggable as an area.";
            else if (o.Storage == StorageType.Integer)
                how = "Integer parameter: the area is rounded to a whole number in " + unit + ".";
            else
                how = "Number data type: unitless, so the value is written already converted to " + unit + ".";

            string extra = string.Empty;
            if (o.Binding == TargetBinding.Type)
                extra = "  ⚠ TYPE parameter: every instance of a type shares it, so only the last region written survives.";
            if (o.AvailableOn < o.TotalCount)
                extra += string.Format("  ⚠ Present on only {0} of {1} regions; the rest are reported as NO PARAM.", o.AvailableOn, o.TotalCount);

            _lblParamInfo.Text = how + extra;
            _lblParamInfo.Foreground = new SolidColorBrush(extra.Length > 0 ? COL_WARN : COL_MUTED);

            _panelText.Visibility = o.Storage == StorageType.String
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        }

        private void RebuildPreview()
        {
            ParameterTargetOption o = _cmbParam.SelectedItem as ParameterTargetOption;

            _rows.Clear();
            List<RegionAreaMigrationItem> items = _svc.BuildPreview(_filtered, o, _opt);
            foreach (RegionAreaMigrationItem it in items) _rows.Add(it);

            RefreshIdentityRows();
        }

        /// <summary>Repopulates the Building/Side selectors from what the model already carries,
        /// preserving the current selection and pre-loading a suggestion when there is a good one.
        /// Only touches the combos, never the preview rows — RefreshTargets() (called right after
        /// by ApplyFilters) ends in RebuildPreview(), which reads these combos once they settle.</summary>
        private void RefreshIdentityOptions()
        {
            bool prev = _building;
            _building = true;
            try
            {
                string suggestedSide = RegionIdentityStampService.SuggestSideFromViewNames(_filtered.Select(_svc.ViewNameOf));
                string suggestedBuilding = _identity.SuggestFromSiblingViews(RegionIdentityStampService.BUILDING_PARAM, _filtered);

                FillIdentityCombo(_cmbBuilding, _identity.DiscoverValues(RegionIdentityStampService.BUILDING_PARAM), suggestedBuilding);
                FillIdentityCombo(_cmbSide, _identity.DiscoverValues(RegionIdentityStampService.SIDE_PARAM), suggestedSide);

                _lblBuildingHint.Text = suggestedBuilding != null
                    ? "Suggested: \"" + suggestedBuilding + "\" — the most common Building already set on other regions in the same view(s)."
                    : "No suggestion available: no sibling region in these view(s) carries a Building yet.";
                _lblSideHint.Text = suggestedSide != null
                    ? "Suggested: \"" + suggestedSide + "\" — detected in the owner view's name."
                    : "No suggestion available: the owner view name does not name a single cardinal direction.";
            }
            finally { _building = prev; }
        }

        /// <summary>Fills one identity combo: the "do not change" sentinel, every value already
        /// discovered in the model, and the suggestion too when it is not already among them.
        /// Keeps whatever the user had picked; falls back to the suggestion only when nothing was
        /// picked yet.</summary>
        private void FillIdentityCombo(ComboBox combo, List<IdentityValueOption> discovered, string suggestion)
        {
            string keep = ResolveComboValue(combo) ?? string.Empty;
            bool keepIsReal = keep.Length > 0;

            combo.Items.Clear();
            combo.Items.Add(NO_CHANGE);
            foreach (IdentityValueOption o in discovered) combo.Items.Add(o.Value);
            if (!string.IsNullOrEmpty(suggestion) && !discovered.Any(o => string.Equals(o.Value, suggestion, StringComparison.OrdinalIgnoreCase)))
                combo.Items.Add(suggestion);

            string target = keepIsReal ? keep : suggestion;
            if (string.IsNullOrEmpty(target))
            {
                combo.SelectedIndex = 0;
                return;
            }

            int idx = -1;
            for (int i = 0; i < combo.Items.Count; i++)
                if (string.Equals(combo.Items[i] as string, target, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }

            if (idx >= 0) combo.SelectedIndex = idx;
            else combo.Text = target;   // a free-typed value that survived a scope change
        }

        /// <summary>
        /// Reads one identity combo as a write target: null means "do not change". Prefers
        /// SelectedItem over Text — for an editable ComboBox, Text only mirrors the selection
        /// once the control's template has been applied (after the window is shown), so reading
        /// Text right after a programmatic SelectedIndex assignment (e.g. during construction,
        /// before the window is visible) can still see the old, empty Text and silently treat a
        /// real suggestion as "do not change". SelectedItem is set synchronously regardless.
        /// Falls back to Text only for a value the user typed that is not in the list.
        /// </summary>
        private static string ResolveComboValue(ComboBox combo)
        {
            string t = combo.SelectedItem as string;
            if (string.IsNullOrEmpty(t)) t = (combo.Text ?? string.Empty).Trim();
            else t = t.Trim();
            return (t.Length == 0 || t == NO_CHANGE) ? null : t;
        }

        /// <summary>Re-evaluates Building/Side on the already-built rows against the current combo
        /// values. Cheap — no geometry recomputation — so combo edits do not re-run BuildPreview.</summary>
        private void RefreshIdentityRows()
        {
            string buildingTarget = ResolveComboValue(_cmbBuilding);
            string sideTarget = ResolveComboValue(_cmbSide);

            foreach (RegionAreaMigrationItem it in _rows)
                _svc.EvaluateIdentity(it, buildingTarget, sideTarget);

            _grid.Items.Refresh();
            UpdateSummary();
        }

        /// <summary>
        /// Runs RefreshIdentityRows() on the next dispatcher pass instead of synchronously inside
        /// the combo's own event. An editable ComboBox's dropdown Popup is still closing/tracking
        /// the mouse when SelectionChanged fires; forcing a DataGrid relayout (Items.Refresh) right
        /// then can hit a Popup with an already-invalid HWND and crash with
        /// "System.ComponentModel.Win32Exception: Invalid window handle" out of
        /// PopupControlService.OnMouseMove. Deferring lets the popup finish closing first.
        /// </summary>
        private void DeferRefreshIdentityRows()
        {
            Dispatcher.BeginInvoke(new Action(RefreshIdentityRows), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateSummary()
        {
            int n = _rows.Count;
            int nw = _rows.Count(r => r.Status == MigrationStatus.New);
            int ov = _rows.Count(r => r.Status == MigrationStatus.Overwrite);
            int nc = _rows.Count(r => r.Status == MigrationStatus.NoChange);
            int np = _rows.Count(r => r.Status == MigrationStatus.MissingParameter);
            int ro = _rows.Count(r => r.Status == MigrationStatus.ReadOnlyParameter || r.Status == MigrationStatus.SpecMismatch);
            int bad = _rows.Count(r => r.Status == MigrationStatus.InvalidGeometry);
            int flagged = _rows.Count(r => r.DeltaFlagged || (r.Geometry != null && r.Geometry.OrientationAmbiguous));
            int chk = _rows.Count(r => r.Selected);

            double total = _rows.Where(r => r.Selected && r.IsWritable).Sum(r => r.AreaFeet2);

            _lblSummary.Text = string.Format(
                "{0} row(s) · NEW {1} · OVERWRITE {2} · NO CHANGE {3} · NO PARAM {4} · READ-ONLY {5} · BAD GEOMETRY {6} · flagged {7}   |   checked {8}, total area {9}",
                n, nw, ov, nc, np, ro, bad, flagged, chk, RevitSpecHelper.FormatArea(_doc, total));

            _btnWrite.IsEnabled = chk > 0;
        }

        private void SetChecks(Func<RegionAreaMigrationItem, bool> predicate)
        {
            foreach (RegionAreaMigrationItem it in _rows) it.Selected = predicate(it);
            _grid.Items.Refresh();
            UpdateSummary();
        }

        private void OnLoadingRow(object sender, DataGridRowEventArgs e)
        {
            RegionAreaMigrationItem it = e.Row.Item as RegionAreaMigrationItem;
            if (it == null) return;

            switch (it.Status)
            {
                case MigrationStatus.New: e.Row.Background = BR_ROW_NEW; break;
                case MigrationStatus.Overwrite: e.Row.Background = BR_ROW_OVER; break;
                case MigrationStatus.NoChange: e.Row.Background = BR_ROW_SAME; break;
                default: e.Row.Background = BR_ROW_BAD; break;
            }

            if (it.DeltaFlagged || (it.Geometry != null && it.Geometry.OrientationAmbiguous))
                e.Row.Foreground = new SolidColorBrush(COL_WARN);
            else
                e.Row.Foreground = new SolidColorBrush(COL_DARK);
        }

        // ════════════════════════════════════════════════════════════════
        //  Actions
        // ════════════════════════════════════════════════════════════════

        private void OnCreateParameter(object sender, RoutedEventArgs e)
        {
            if (_filtered.Count == 0)
            {
                TaskDialog.Show("BauTools", "Put at least one Filled Region in scope first: the parameter is bound to the category those elements report.");
                return;
            }

            NewParameterDialog dlg = new NewParameterDialog(RevitSpecHelper.ProjectAreaUnitSuffix(_doc));
            dlg.Owner = this;
            if (dlg.ShowDialog() != true) return;

            RegionParameterDiscoveryService.CreateResult res =
                _params.CreateInstanceSharedParameter(dlg.ParameterName, dlg.AsAreaType, _filtered[0], "BauToolsRegions");

            TaskDialog.Show("BauTools", res.Message);

            if (res.Ok)
            {
                RefreshTargets();
                int idx = _targets.FindIndex(o => string.Equals(o.Name, dlg.ParameterName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) _cmbParam.SelectedIndex = idx;
            }
        }

        private void OnHighlight(object sender, RoutedEventArgs e)
        {
            RegionAreaMigrationItem it = _grid.SelectedItem as RegionAreaMigrationItem;
            if (it == null)
            {
                TaskDialog.Show("BauTools", "Pick a row in the preview first.");
                return;
            }

            try
            {
                _uidoc.Selection.SetElementIds(new List<ElementId> { it.Id });

                bool sameView = _uidoc.ActiveView != null && _uidoc.ActiveView.Id == it.OwnerViewId;
                if (sameView)
                {
                    _uidoc.ShowElements(it.Id);
                    _uidoc.RefreshActiveView();
                }
                else
                {
                    TaskDialog.Show("BauTools",
                        string.Format("The region is selected, but it belongs to the view “{0}”.\n\n" +
                                      "Revit cannot change the active view while this dialog is open. Close the tool, open that view, and the element stays selected.",
                            it.ViewName));
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BauTools", "Could not highlight the element: " + ex.Message);
            }
        }

        private void OnExportCsv(object sender, RoutedEventArgs e)
        {
            if (_rows.Count == 0)
            {
                TaskDialog.Show("BauTools", "Nothing to export.");
                return;
            }

            try
            {
                Microsoft.Win32.SaveFileDialog sfd = new Microsoft.Win32.SaveFileDialog();
                sfd.Filter = "CSV file (*.csv)|*.csv";
                sfd.FileName = string.Format("BauTools_RegionAreas_{0:yyyyMMdd_HHmm}.csv", DateTime.Now);
                if (sfd.ShowDialog() != true) return;

                _svc.ExportCsv(sfd.FileName, _rows.ToList(), _cmbParam.SelectedItem as ParameterTargetOption);
                TaskDialog.Show("BauTools", "Saved:\n" + sfd.FileName);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BauTools", "Export failed: " + ex.Message);
            }
        }

        private void OnWrite(object sender, RoutedEventArgs e)
        {
            ParameterTargetOption target = _cmbParam.SelectedItem as ParameterTargetOption;
            if (target == null)
            {
                TaskDialog.Show("BauTools", "Choose a target parameter first.");
                return;
            }

            string buildingTarget = ResolveComboValue(_cmbBuilding);
            string sideTarget = ResolveComboValue(_cmbSide);

            List<RegionAreaMigrationItem> checked_ = _rows.Where(r => r.Selected).ToList();
            if (checked_.Count == 0)
            {
                TaskDialog.Show("BauTools", "No row is checked.");
                return;
            }

            int nw = checked_.Count(r => r.Status == MigrationStatus.New);
            int ov = checked_.Count(r => r.Status == MigrationStatus.Overwrite);
            int flagged = checked_.Count(r => r.DeltaFlagged || (r.Geometry != null && r.Geometry.OrientationAmbiguous));

            int bNew = buildingTarget != null ? checked_.Count(r => r.BuildingStatus == IdentityFieldStatus.New) : 0;
            int bOver = buildingTarget != null ? checked_.Count(r => r.BuildingStatus == IdentityFieldStatus.Overwrite) : 0;
            int sNew = sideTarget != null ? checked_.Count(r => r.SideStatus == IdentityFieldStatus.New) : 0;
            int sOver = sideTarget != null ? checked_.Count(r => r.SideStatus == IdentityFieldStatus.Overwrite) : 0;

            if (nw + ov + bNew + bOver + sNew + sOver == 0)
            {
                TaskDialog.Show("BauTools", "Nothing to write: every checked row is already up to date, or has no target configured.");
                return;
            }

            List<string> lines = new List<string>();
            lines.Add(string.Format("{0}: {1} new, {2} overwrite", target.Name, nw, ov));
            if (buildingTarget != null) lines.Add(string.Format("Building -> \"{0}\": {1} new, {2} overwrite", buildingTarget, bNew, bOver));
            if (sideTarget != null) lines.Add(string.Format("Side -> \"{0}\": {1} new, {2} overwrite", sideTarget, sNew, sOver));
            if (flagged > 0) lines.Add(string.Format("Flagged rows included: {0}", flagged));
            if (target.Binding == TargetBinding.Type)
                lines.Add("⚠ The target is a TYPE parameter: instances of the same type will end up sharing the last value written.");

            TaskDialog td = new TaskDialog("BauTools — confirm");
            td.MainInstruction = string.Format("Apply changes to {0} checked region(s)?", checked_.Count);
            td.MainContent = string.Join("\n", lines);
            td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            td.DefaultButton = TaskDialogResult.No;
            if (td.Show() != TaskDialogResult.Yes) return;

            MigrationReport rep = _svc.Write(_rows.ToList(), target, _opt, buildingTarget, sideTarget);

            string scope = _cmbScope.SelectedIndex == 1 ? "active view"
                         : _cmbScope.SelectedIndex == 2 ? "entire model"
                         : "selection";
            string scopeDesc = scope + " → " + target.Name;
            if (buildingTarget != null) scopeDesc += " · Building=" + buildingTarget;
            if (sideTarget != null) scopeDesc += " · Side=" + sideTarget;
            BauToolsRunStampService.Stamp(_doc, TOOL_KEY, scopeDesc, checked_.Count, string.Empty);
            _lblLastRun.Text = BauToolsRunStampService.ReadLastRun(_doc, TOOL_KEY);

            string body = rep.Summary;
            if (rep.Warnings.Count > 0) body += "\n" + string.Join("\n", rep.Warnings);
            if (rep.Failures.Count > 0)
                body += "\n\nFirst failures:\n" + string.Join("\n", rep.Failures.Take(8));

            TaskDialog res = new TaskDialog("BauTools — result");
            res.MainInstruction = rep.Committed
                ? string.Format("{0} region(s) updated.", checked_.Count)
                : "Nothing was written (transaction rolled back).";
            res.MainContent = body;
            res.Show();

            RebuildPreview();
        }

        // ════════════════════════════════════════════════════════════════
        //  Small UI helpers
        // ════════════════════════════════════════════════════════════════

        private UIElement Card(string title, UIElement body, bool stretch = false)
        {
            Border card = new Border
            {
                Background = new SolidColorBrush(COL_CARD),
                BorderBrush = new SolidColorBrush(COL_BORDER),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(14, 10, 14, 0),
                Padding = new Thickness(14, 10, 14, 12)
            };

            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

            TextBlock t = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = new SolidColorBrush(COL_ACCENT),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(t, 0);
            g.Children.Add(t);

            Grid.SetRow(body, 1);
            g.Children.Add(body);

            card.Child = g;
            return card;
        }

        private TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(COL_MUTED),
                Margin = new Thickness(0, 6, 6, 0),
                FontSize = 12
            };
        }

        private ComboBox Combo(double width)
        {
            return new ComboBox
            {
                Width = width,
                Margin = new Thickness(0, 2, 12, 0),
                Padding = new Thickness(4, 3, 4, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private Button FlatButton(string text, Color color, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 28,
                Margin = new Thickness(0, 2, 8, 0),
                Background = new SolidColorBrush(color),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        /// <summary>Attaches the tooltip and returns the element, so controls can be added inline.</summary>
        private FrameworkElement Hint(FrameworkElement el, string tip)
        {
            el.ToolTip = tip;
            ToolTipService.SetShowDuration(el, 30000);
            return el;
        }

        private DataGridTextColumn TextCol(string header, string path, double width)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(path),
                Width = width,
                IsReadOnly = true
            };
        }

        private class IdOption
        {
            public ElementId Id;
            public string Name;
            public IdOption(ElementId id, string name) { Id = id; Name = name ?? "—"; }
            public string Key { get { return Id == null ? "*" : Id.ToString(); } }
            public override string ToString() { return Name; }
        }
    }

    /// <summary>Tiny modal used by “Create new…”: parameter name + data type.</summary>
    internal class NewParameterDialog : Window
    {
        private readonly TextBox _name;
        private readonly RadioButton _area;

        public string ParameterName { get { return _name.Text.Trim(); } }
        public bool AsAreaType { get { return _area.IsChecked == true; } }

        public NewParameterDialog(string unitSuffix)
        {
            Title = "BauTools — new instance parameter";
            Width = 460;
            SizeToContent = System.Windows.SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));

            StackPanel sp = new StackPanel { Margin = new Thickness(16) };

            sp.Children.Add(new TextBlock
            {
                Text = "A shared parameter is created and bound as an INSTANCE parameter to the Filled Region category of this project.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"))
            });

            sp.Children.Add(new TextBlock { Text = "Parameter name", Margin = new Thickness(0, 0, 0, 3) });
            _name = new TextBox { Text = "ZFA Region Area", Padding = new Thickness(4, 4, 4, 4) };
            sp.Children.Add(_name);

            sp.Children.Add(new TextBlock { Text = "Data type", Margin = new Thickness(0, 12, 0, 3) });

            _area = new RadioButton
            {
                Content = "Area  —  numeric, formatted by Revit in " + unitSuffix + ", schedulable and taggable as an area",
                IsChecked = true,
                Margin = new Thickness(0, 2, 0, 2)
            };
            sp.Children.Add(_area);

            sp.Children.Add(new RadioButton
            {
                Content = "Text  —  the formatted string, e.g. \"1,234 " + unitSuffix + "\"",
                Margin = new Thickness(0, 2, 0, 2)
            });

            StackPanel row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            Button ok = new Button { Content = "Create", Width = 96, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            ok.Click += delegate
            {
                if (string.IsNullOrWhiteSpace(_name.Text))
                {
                    MessageBox.Show("Type a parameter name.", "BauTools");
                    return;
                }
                DialogResult = true;
            };
            row.Children.Add(ok);

            Button cancel = new Button { Content = "Cancel", Width = 96, Height = 28, IsCancel = true };
            row.Children.Add(cancel);

            sp.Children.Add(row);
            Content = sp;
        }
    }
}
