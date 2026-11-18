#nullable disable
// No Autodesk.Revit usings here on purpose: plain WPF, no type collisions.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ARYE.OfficeTools.Infrastructure;

namespace ARYE.OfficeTools.Views
{
    /// <summary>
    /// Picks existing views to add to a sheet set. Every placeable view is
    /// listed with its type and the sheet it already sits on; views already
    /// in the set are shown checked and locked. Each pick carries its
    /// own-sheet flag (default: own sheet, except schedules and legends).
    /// </summary>
    internal sealed class ViewPickerDialog : Window
    {
        private readonly List<Row> _rows = new List<Row>();
        private readonly StackPanel _list = new StackPanel();
        private readonly TextBox _filter = new TextBox { Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 8) };
        private readonly TextBlock _count = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)) };

        public static IList<(PlaceableViewRef View, bool OwnSheet)> Pick(Window owner, IList<PlaceableViewRef> views, ISet<string> already)
        {
            var dialog = new ViewPickerDialog(views, already) { Owner = owner };
            return dialog.ShowDialog() == true ? dialog.Result : null;
        }

        public IList<(PlaceableViewRef View, bool OwnSheet)> Result { get; private set; }

        private ViewPickerDialog(IList<PlaceableViewRef> views, ISet<string> already)
        {
            Title = "Add existing views to the set";
            Width = 720;
            Height = 620;
            MinWidth = 560;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var intro = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
                Text = "Views are placed exactly as they are — never duplicated or restyled. 'Own sheet' gives the view a dedicated 1×1 sheet in the set; " +
                       "otherwise it takes the next free matrix slot after the plan views. A view already on another sheet cannot be placed twice (schedules and legends can)."
            };
            Grid.SetRow(intro, 0);
            root.Children.Add(intro);

            _filter.ToolTip = "Filter by name or type";
            _filter.TextChanged += (s, e) => ApplyFilter();
            Grid.SetRow(_filter, 1);
            root.Children.Add(_filter);

            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)), BorderThickness = new Thickness(1), Padding = new Thickness(6) };
            scroller.Content = _list;
            Grid.SetRow(scroller, 2);
            root.Children.Add(scroller);

            foreach (PlaceableViewRef view in views ?? new List<PlaceableViewRef>())
            {
                bool inSet = already != null && already.Contains(view.UniqueId);
                bool multi = view.IsSchedule || string.Equals(view.ViewTypeName, "Legend", StringComparison.OrdinalIgnoreCase);
                bool blocked = !inSet && !multi && !string.IsNullOrEmpty(view.OnSheetNumber);
                var row = new Row { View = view };
                var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                row.Select = new CheckBox { IsChecked = inSet, IsEnabled = !inSet && !blocked, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                    ToolTip = inSet ? "Already in this set" : blocked ? $"Already on sheet {view.OnSheetNumber} — a view can only be on one sheet" : "Add to the set" };
                grid.Children.Add(row.Select);
                var name = new TextBlock { Text = view.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = view.Name,
                    Opacity = blocked ? 0.55 : 1.0 };
                Grid.SetColumn(name, 1);
                grid.Children.Add(name);
                var type = new TextBlock { Text = view.ViewTypeName, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)) };
                Grid.SetColumn(type, 2);
                grid.Children.Add(type);
                var on = new TextBlock
                {
                    Text = inSet ? "in this set" : string.IsNullOrEmpty(view.OnSheetNumber) ? "not on a sheet" : "on " + view.OnSheetNumber,
                    FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(blocked ? Color.FromRgb(180, 83, 9) : Color.FromRgb(100, 116, 139))
                };
                Grid.SetColumn(on, 3);
                grid.Children.Add(on);
                row.OwnSheet = new CheckBox { Content = "own sheet", IsChecked = !multi, IsEnabled = !inSet && !blocked, VerticalAlignment = VerticalAlignment.Center, FontSize = 11,
                    ToolTip = "Checked: dedicated 1×1 sheet. Unchecked: next free matrix slot." };
                Grid.SetColumn(row.OwnSheet, 4);
                grid.Children.Add(row.OwnSheet);

                row.Element = grid;
                row.InSet = inSet;
                _rows.Add(row);
                _list.Children.Add(grid);
            }

            var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _count.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(_count);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            buttons.Children.Add(cancel);
            var ok = new Button { Content = "Add selected", Padding = new Thickness(16, 6, 16, 6), FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(0, 113, 227)), Foreground = Brushes.White };
            ok.Click += (s, e) =>
            {
                Result = _rows.Where(r => !r.InSet && r.Select.IsChecked == true)
                    .Select(r => (r.View, r.OwnSheet.IsChecked == true)).ToList();
                DialogResult = true;
                Close();
            };
            buttons.Children.Add(ok);
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string f = _filter.Text?.Trim() ?? string.Empty;
            int shown = 0;
            foreach (Row row in _rows)
            {
                bool visible = f.Length == 0
                    || row.View.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                    || row.View.ViewTypeName.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
                row.Element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (visible) shown++;
            }
            _count.Text = $"{shown} of {_rows.Count} view(s) shown";
        }

        private sealed class Row
        {
            public PlaceableViewRef View;
            public CheckBox Select;
            public CheckBox OwnSheet;
            public Grid Element;
            public bool InSet;
        }
    }
}
