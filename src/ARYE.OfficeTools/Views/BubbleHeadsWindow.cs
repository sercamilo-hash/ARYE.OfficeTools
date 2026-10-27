#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARYE.OfficeTools.Core.Results;
// Autodesk.Revit.DB and System.Windows.Media/Controls each declare their own
// Grid/Color — alias to the WPF ones this window actually builds with.
using Color = System.Windows.Media.Color;
using Grid = System.Windows.Controls.Grid;

namespace ARYE.OfficeTools.Views
{
    /// <summary>
    /// BauTools — Bubble Heads &amp; Datum Manager. Shows or hides the End0/End1
    /// bubble on every Grid/Level in the active view. Ported from the legacy
    /// add-in — functionally complete there, only the two silent catches were
    /// replaced with a real per-element result (this project never swallows a
    /// per-element failure into a plain "done").
    /// </summary>
    public class BubbleHeadsWindow : Window
    {
        private readonly Document _doc;
        private readonly Autodesk.Revit.DB.View _activeView;
        private readonly List<Autodesk.Revit.DB.Grid> _grids;
        private readonly List<Level> _levels;

        private CheckBox _chkGrids;
        private CheckBox _chkLevels;
        private RadioButton _rbEnd0Show, _rbEnd0Hide, _rbEnd0Keep;
        private RadioButton _rbEnd1Show, _rbEnd1Hide, _rbEnd1Keep;
        private TextBlock _statusSummary;

        private static readonly Color ColBg = (Color)ColorConverter.ConvertFromString("#F1F5F9");
        private static readonly Color ColCard = Colors.White;
        private static readonly Color ColDark = (Color)ColorConverter.ConvertFromString("#0F172A");
        private static readonly Color ColAccent = (Color)ColorConverter.ConvertFromString("#0071E3");
        private static readonly Color ColAccent2 = (Color)ColorConverter.ConvertFromString("#0284C7");
        private static readonly Color ColMuted = (Color)ColorConverter.ConvertFromString("#64748B");
        private static readonly Color ColBorder = (Color)ColorConverter.ConvertFromString("#CBD5E1");
        private static readonly Color ColHeaderBg = (Color)ColorConverter.ConvertFromString("#1E293B");

        public BubbleHeadsWindow(Document doc, Autodesk.Revit.DB.View activeView)
        {
            _doc = doc;
            _activeView = activeView;

            _grids = new FilteredElementCollector(_doc, _activeView.Id)
                .OfClass(typeof(Autodesk.Revit.DB.Grid)).Cast<Autodesk.Revit.DB.Grid>().ToList();
            _levels = new FilteredElementCollector(_doc, _activeView.Id)
                .OfClass(typeof(Level)).Cast<Level>().ToList();

            Title = "BauTools — Bubble Heads & Datum Manager (Active View)";
            Height = 620;
            Width = 720;
            MinHeight = 560;
            MinWidth = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(ColBg);
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            BuildUi();
            UpdateSummary();
        }

        private void BuildUi()
        {
            var cardBrush = new SolidColorBrush(ColCard);
            var darkBrush = new SolidColorBrush(ColDark);
            var accentBrush = new SolidColorBrush(ColAccent);
            var accent2Brush = new SolidColorBrush(ColAccent2);
            var mutedBrush = new SolidColorBrush(ColMuted);
            var borderBrush = new SolidColorBrush(ColBorder);
            var headerBgBrush = new SolidColorBrush(ColHeaderBg);

            var root = new Grid();
            for (int i = 0; i < 5; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions[4].Height = new GridLength(1, GridUnitType.Star);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var headerBar = new Border { Background = headerBgBrush, Padding = new Thickness(24, 14, 24, 14) };
            var titlePanel = new StackPanel();
            var logoLine = new StackPanel { Orientation = Orientation.Horizontal };
            var badge = new Border
            {
                Background = accent2Brush, CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "BUBBLES & DATUMS", FontWeight = FontWeights.ExtraBold, FontSize = 12, Foreground = Brushes.White }
            };
            logoLine.Children.Add(badge);
            logoLine.Children.Add(new TextBlock
            {
                Text = "BauTools — Bubble Heads & Datum Visibility", FontSize = 17, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center
            });
            titlePanel.Children.Add(logoLine);
            titlePanel.Children.Add(new TextBlock
            {
                Text = "Show or Hide bubble heads for Grids and Levels exclusively in the active view.",
                FontSize = 11, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                Margin = new Thickness(0, 4, 0, 0)
            });
            headerBar.Child = titlePanel;
            Grid.SetRow(headerBar, 0);
            root.Children.Add(headerBar);

            // Active view info bar
            var viewInfoBar = new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")), Padding = new Thickness(20, 8, 20, 8) };
            var vPanel = new StackPanel { Orientation = Orientation.Horizontal };
            vPanel.Children.Add(new TextBlock { Text = "ACTIVE VIEW: ", FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = darkBrush });
            vPanel.Children.Add(new TextBlock { Text = _activeView.Name + " ", FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = accentBrush });
            vPanel.Children.Add(new TextBlock { Text = "(" + _activeView.ViewType + ")", FontSize = 11.5, Foreground = mutedBrush });
            viewInfoBar.Child = vPanel;
            Grid.SetRow(viewInfoBar, 1);
            root.Children.Add(viewInfoBar);

            // Target elements card
            var targetCard = Card(borderBrush, cardBrush, new Thickness(16, 12, 16, 6));
            var targetPanel = new StackPanel();
            targetPanel.Children.Add(SectionLabel("1. ELEMENTS TO MODIFY IN ACTIVE VIEW:", darkBrush));
            var chkRow = new StackPanel { Orientation = Orientation.Horizontal };
            _chkGrids = new CheckBox
            {
                // A disabled checkbox must say why — never a silent "0 in view".
                Content = _grids.Count > 0
                    ? $"Grids ({_grids.Count} in view)"
                    : "Grids (none visible in this view)",
                IsChecked = _grids.Count > 0, IsEnabled = _grids.Count > 0,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 24, 0)
            };
            _chkGrids.Checked += (s, e) => UpdateSummary();
            _chkGrids.Unchecked += (s, e) => UpdateSummary();
            _chkLevels = new CheckBox
            {
                // Levels are only drawn in elevations/sections, so in a plan this is
                // legitimately 0 — the label says so instead of looking broken.
                Content = _levels.Count > 0
                    ? $"Levels ({_levels.Count} in view)"
                    : "Levels (0 in view — levels only show in elevations and sections; open one to toggle level heads)",
                IsChecked = _levels.Count > 0, IsEnabled = _levels.Count > 0,
                FontWeight = FontWeights.SemiBold
            };
            _chkLevels.Checked += (s, e) => UpdateSummary();
            _chkLevels.Unchecked += (s, e) => UpdateSummary();
            chkRow.Children.Add(_chkGrids);
            chkRow.Children.Add(_chkLevels);
            targetPanel.Children.Add(chkRow);
            targetCard.Child = targetPanel;
            Grid.SetRow(targetCard, 2);
            root.Children.Add(targetCard);

            // Quick presets
            var presetCard = Card(borderBrush, cardBrush, new Thickness(16, 6, 16, 6));
            var presetPanel = new StackPanel();
            presetPanel.Children.Add(SectionLabel("2. QUICK PRESETS (1-CLICK CONFIGURATION):", darkBrush));
            var pGrid = new Grid();
            for (int i = 0; i < 4; i++) pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddPreset(pGrid, 0, "Only Left / End 0\n(Show 0, Hide 1)", () => { _rbEnd0Show.IsChecked = true; _rbEnd1Hide.IsChecked = true; });
            AddPreset(pGrid, 1, "Only Right / End 1\n(Hide 0, Show 1)", () => { _rbEnd0Hide.IsChecked = true; _rbEnd1Show.IsChecked = true; });
            AddPreset(pGrid, 2, "Both Ends\n(Show 0 & 1)", () => { _rbEnd0Show.IsChecked = true; _rbEnd1Show.IsChecked = true; });
            AddPreset(pGrid, 3, "Turn OFF All\n(Hide 0 & 1)", () => { _rbEnd0Hide.IsChecked = true; _rbEnd1Hide.IsChecked = true; }, true);
            presetPanel.Children.Add(pGrid);
            presetCard.Child = presetPanel;
            Grid.SetRow(presetCard, 3);
            root.Children.Add(presetCard);

            // Detailed manual options
            var manualCard = Card(borderBrush, cardBrush, new Thickness(16, 6, 16, 12));
            var manualPanel = new StackPanel();
            manualPanel.Children.Add(SectionLabel("3. DETAILED END CONFIGURATION:", darkBrush));
            var mGrid = new Grid();
            mGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var col0 = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            col0.Children.Add(new TextBlock { Text = "End 0 (Left / Bottom / Start):", FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = darkBrush, Margin = new Thickness(0, 0, 0, 6) });
            _rbEnd0Show = Radio("Show Bubble", "End0", true);
            _rbEnd0Hide = Radio("Hide Bubble", "End0", false);
            _rbEnd0Keep = Radio("Keep unchanged", "End0", false);
            col0.Children.Add(_rbEnd0Show); col0.Children.Add(_rbEnd0Hide); col0.Children.Add(_rbEnd0Keep);
            Grid.SetColumn(col0, 0);
            mGrid.Children.Add(col0);

            var col1 = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            col1.Children.Add(new TextBlock { Text = "End 1 (Right / Top / End):", FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = darkBrush, Margin = new Thickness(0, 0, 0, 6) });
            _rbEnd1Show = Radio("Show Bubble", "End1", false);
            _rbEnd1Hide = Radio("Hide Bubble", "End1", true);
            _rbEnd1Keep = Radio("Keep unchanged", "End1", false);
            col1.Children.Add(_rbEnd1Show); col1.Children.Add(_rbEnd1Hide); col1.Children.Add(_rbEnd1Keep);
            Grid.SetColumn(col1, 1);
            mGrid.Children.Add(col1);

            manualPanel.Children.Add(mGrid);
            manualCard.Child = manualPanel;
            Grid.SetRow(manualCard, 4);
            root.Children.Add(manualCard);

            // Footer
            var footer = new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")), Padding = new Thickness(20, 12, 20, 12) };
            var footGrid = new Grid();
            footGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _statusSummary = new TextBlock { Text = "Ready.", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = darkBrush };
            footGrid.Children.Add(_statusSummary);
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnCancel = new Button { Content = "Cancel", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 10, 0), Cursor = System.Windows.Input.Cursors.Hand };
            btnCancel.Click += (s, e) => Close();
            btnPanel.Children.Add(btnCancel);
            var btnApply = new Button
            {
                Content = "Apply Changes", Padding = new Thickness(20, 8, 20, 8), Background = accentBrush,
                Foreground = Brushes.White, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0)
            };
            btnApply.Click += (s, e) => ApplyChanges();
            btnPanel.Children.Add(btnApply);
            Grid.SetColumn(btnPanel, 1);
            footGrid.Children.Add(btnPanel);
            footer.Child = footGrid;
            Grid.SetRow(footer, 5);
            root.Children.Add(footer);

            Content = root;
        }

        private static Border Card(Brush border, Brush background, Thickness margin) => new Border
        {
            Background = background, BorderBrush = border, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Margin = margin, Padding = new Thickness(16, 12, 16, 12)
        };

        private static TextBlock SectionLabel(string text, Brush color) => new TextBlock
        {
            Text = text, FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = color, Margin = new Thickness(0, 0, 0, 8)
        };

        private RadioButton Radio(string content, string group, bool isChecked)
        {
            var rb = new RadioButton { Content = content, GroupName = group, IsChecked = isChecked, Margin = new Thickness(0, 2, 0, 4) };
            rb.Checked += (s, e) => UpdateSummary();
            return rb;
        }

        private void AddPreset(Grid parent, int column, string text, Action onClick, bool isDanger = false)
        {
            var btn = new Button
            {
                Content = text, Padding = new Thickness(8), Margin = new Thickness(3, 0, 3, 0),
                FontWeight = FontWeights.SemiBold, FontSize = 11, Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDanger ? "#FEE2E2" : "#EFF6FF")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDanger ? "#FCA5A5" : "#BFDBFE")),
                BorderThickness = new Thickness(1)
            };
            btn.Click += (s, e) => onClick();
            Grid.SetColumn(btn, column);
            parent.Children.Add(btn);
        }

        private void UpdateSummary()
        {
            if (_statusSummary == null) return;
            int targetCount = 0;
            if (_chkGrids?.IsChecked == true) targetCount += _grids.Count;
            if (_chkLevels?.IsChecked == true) targetCount += _levels.Count;
            string end0 = _rbEnd0Show?.IsChecked == true ? "Show End 0" : _rbEnd0Hide?.IsChecked == true ? "Hide End 0" : "Keep End 0";
            string end1 = _rbEnd1Show?.IsChecked == true ? "Show End 1" : _rbEnd1Hide?.IsChecked == true ? "Hide End 1" : "Keep End 1";
            _statusSummary.Text = $"{targetCount} element(s) selected. Action: {end0} | {end1}.";
        }

        private void ApplyChanges()
        {
            bool modifyGrids = _chkGrids.IsChecked == true;
            bool modifyLevels = _chkLevels.IsChecked == true;
            if (!modifyGrids && !modifyLevels)
            {
                MessageBox.Show("Please select at least one element category (Grids or Levels).", "BauTools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int end0Mode = _rbEnd0Show.IsChecked == true ? 1 : _rbEnd0Hide.IsChecked == true ? -1 : 0;
            int end1Mode = _rbEnd1Show.IsChecked == true ? 1 : _rbEnd1Hide.IsChecked == true ? -1 : 0;
            if (end0Mode == 0 && end1Mode == 0)
            {
                MessageBox.Show("Both ends are set to 'Keep unchanged'. No modifications to apply.", "BauTools", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = new OperationResult<string>();
            using (var tx = new Transaction(_doc, "BauTools: Toggle bubble heads"))
            {
                tx.Start();
                if (modifyGrids) ApplyToDatums(_grids.Cast<DatumPlane>(), end0Mode, end1Mode, "Grid", _activeView, result);
                if (modifyLevels) ApplyToDatums(_levels.Cast<DatumPlane>(), end0Mode, end1Mode, "Level", _activeView, result);
                tx.Commit();
            }

            App.Log?.Info("Bubble heads: " + result.BuildSummary());
            TaskDialog.Show("BauTools — Bubble Heads", result.BuildSummary());
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Applies the show/hide/keep decision to one end at a time so a
        /// failure on End1 doesn't hide that End0 already succeeded — each
        /// datum's outcome (not just a final headcount) lands in the result.
        /// </summary>
        private static void ApplyToDatums(
            IEnumerable<DatumPlane> datums, int end0Mode, int end1Mode, string kind,
            Autodesk.Revit.DB.View view, OperationResult<string> result)
        {
            foreach (DatumPlane datum in datums)
            {
                string label = $"{kind} '{datum.Name}'";
                try
                {
                    if (end0Mode == 1) datum.ShowBubbleInView(DatumEnds.End0, view);
                    else if (end0Mode == -1) datum.HideBubbleInView(DatumEnds.End0, view);

                    if (end1Mode == 1) datum.ShowBubbleInView(DatumEnds.End1, view);
                    else if (end1Mode == -1) datum.HideBubbleInView(DatumEnds.End1, view);

                    result.Succeeded.Add(label);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    // Datum doesn't reach both ends in this view (e.g. a grid that
                    // doesn't cross it) — Revit reports it, we surface it, never hide it.
                    result.Failed.Add(new OperationFailure(label, ex));
                }
            }
        }
    }
}
