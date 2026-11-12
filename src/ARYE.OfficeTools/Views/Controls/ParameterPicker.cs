#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ARYE.OfficeTools.Models;

namespace ARYE.OfficeTools.Views.Controls
{
    /// <summary>
    /// The shared BauTools parameter selector (approved mockup "BauTools Parameter Picker").
    /// A field shows the chosen parameter with its data-type chip; clicking it opens a searchable
    /// list grouped by Revit parameter group, with data-type toggles and every parameter that cannot
    /// be chosen still listed, dimmed, with its reason. Keyboard: Up/Down, Enter, Esc.
    /// Plain C# WPF, no XAML, like the windows that use it.
    /// </summary>
    public sealed class ParameterPicker : UserControl
    {
        private static readonly string[] Kinds = { "Area", "Number", "Text", "Integer" };

        // Sections follow the order a designer looks for them; unknown groups come after, A–Z.
        private static readonly string[] GroupOrder =
        {
            "Analysis Results", "Data", "Other", "Identity Data", "Text", "Dimensions", "Constraints",
            "Analytical Properties", "Energy Analysis", "IFC Parameters"
        };

        private static readonly Brush BrBorder = Hex("#CBD5E1");
        private static readonly Brush BrAccent = Hex("#2563EB");
        private static readonly Brush BrAccentSoft = Hex("#E6EEFC");
        private static readonly Brush BrPanel = Hex("#F6F8FB");
        private static readonly Brush BrText = Hex("#0F172A");
        private static readonly Brush BrMuted = Hex("#5B6778");
        private static readonly Brush BrWarn = Hex("#B45309");
        private static readonly Brush BrHighlight = Hex("#FFF2B8");

        private static readonly Dictionary<string, Brush[]> KindBrushes = new Dictionary<string, Brush[]>
        {
            { "Area", new[] { Hex("#2563EB"), Hex("#E3ECFD") } },
            { "Number", new[] { Hex("#0F8A7E"), Hex("#DDF3F0") } },
            { "Text", new[] { Hex("#7C3AED"), Hex("#EFE7FD") } },
            { "Integer", new[] { Hex("#C2410C"), Hex("#FDE9DD") } },
            { "Other", new[] { Hex("#64748B"), Hex("#EDF0F4") } },
            { "Shared", new[] { Hex("#A16207"), Hex("#FDF3D7") } }
        };

        private readonly ToggleButton _field;
        private readonly StackPanel _fieldContent;
        private readonly Popup _popup;
        private readonly TextBox _search;
        private readonly Dictionary<string, ToggleButton> _kindToggles = new Dictionary<string, ToggleButton>();
        private readonly TextBlock _count;
        private readonly StackPanel _list;
        private readonly ScrollViewer _scroll;

        private List<ParameterPickerItem> _items = new List<ParameterPickerItem>();
        private ParameterPickerItem _selected;
        private readonly HashSet<string> _folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Rows that can be chosen, in display order, for keyboard navigation.
        private readonly List<KeyValuePair<ParameterPickerItem, Border>> _choosable = new List<KeyValuePair<ParameterPickerItem, Border>>();
        private int _active = -1;

        /// <summary>Raised when the user chooses an item (not when the code selects one).</summary>
        public event EventHandler SelectionChanged;

        /// <summary>Text shown in the field when nothing is chosen.</summary>
        public string Placeholder { get; set; } = "Choose a parameter…";

        /// <summary>
        /// Extra, live reason to block an item (for example "already used for the vent total").
        /// Evaluated every time the list is drawn.
        /// </summary>
        public Func<ParameterPickerItem, string> ExtraBlock { get; set; }

        public ParameterPicker()
        {
            _fieldContent = new StackPanel { Orientation = Orientation.Horizontal };
            _field = new ToggleButton
            {
                Content = _fieldContent,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 5, 8, 5),
                Background = Brushes.White,
                BorderBrush = BrBorder,
                MinHeight = 32,
                Cursor = Cursors.Hand
            };
            _field.Checked += delegate { Open(); };
            _field.Unchecked += delegate { _popup.IsOpen = false; };
            _field.PreviewKeyDown += OnFieldKey;

            _search = new TextBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Filters as you type: parameter name, Revit group or data type."
            };
            _search.TextChanged += delegate { _active = -1; Rebuild(); };

            var searchBox = new Border
            {
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Background = BrPanel,
                Padding = new Thickness(6, 3, 6, 3)
            };
            var sg = new Grid();
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sg.Children.Add(new TextBlock { Text = "⌕", Foreground = BrMuted, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(_search, 1);
            sg.Children.Add(_search);
            var esc = new TextBlock { Text = "Esc", FontSize = 10, Foreground = BrMuted, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(esc, 2);
            sg.Children.Add(esc);
            searchBox.Child = sg;

            var toggles = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (string k in Kinds)
            {
                var t = new ToggleButton
                {
                    IsChecked = true,
                    Margin = new Thickness(0, 0, 6, 0),
                    Padding = new Thickness(8, 1, 8, 1),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = "Show or hide " + k + " parameters"
                };
                t.Checked += delegate { StyleToggle(t, k); _active = -1; Rebuild(); };
                t.Unchecked += delegate { StyleToggle(t, k); _active = -1; Rebuild(); };
                _kindToggles[k] = t;
                toggles.Children.Add(t);
            }
            _count = new TextBlock { FontSize = 11, Foreground = BrMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            toggles.Children.Add(_count);

            var head = new StackPanel { Margin = new Thickness(8) };
            head.Children.Add(searchBox);
            head.Children.Add(toggles);

            _list = new StackPanel();
            _scroll = new ScrollViewer
            {
                Content = _list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 340
            };

            var foot = new TextBlock
            {
                Text = "↑ ↓ move   ·   Enter choose   ·   Esc close   ·   Dimmed rows show why they cannot be chosen",
                FontSize = 11,
                Foreground = BrMuted,
                Margin = new Thickness(10, 5, 10, 6),
                TextWrapping = TextWrapping.Wrap
            };

            var panel = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(head, Dock.Top);
            panel.Children.Add(head);
            var footBorder = new Border { BorderBrush = BrBorder, BorderThickness = new Thickness(0, 1, 0, 0), Child = foot };
            DockPanel.SetDock(footBorder, Dock.Bottom);
            panel.Children.Add(footBorder);
            panel.Children.Add(new Border { BorderBrush = BrBorder, BorderThickness = new Thickness(0, 1, 0, 0), Child = _scroll });

            _popup = new Popup
            {
                PlacementTarget = _field,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                Child = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = BrBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(0, 3, 8, 8),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.22 },
                    Child = panel
                }
            };
            // While open, the field ignores the mouse: a click on it then only closes the list
            // (StaysOpen=false) instead of closing and immediately reopening it.
            _popup.Opened += delegate { _field.IsHitTestVisible = false; };
            _popup.Closed += delegate { _field.IsChecked = false; _field.IsHitTestVisible = true; };
            _popup.PreviewKeyDown += OnPopupKey;

            var root = new Grid();
            root.Children.Add(_field);
            root.Children.Add(_popup);
            Content = root;

            foreach (KeyValuePair<string, ToggleButton> kv in _kindToggles) StyleToggle(kv.Value, kv.Key);
            DrawField();
        }

        // ── Public API ───────────────────────────────────────────────────

        public ParameterPickerItem SelectedItem => _selected;

        /// <summary>Replaces the items, keeping the selection by key when it is still there and choosable.</summary>
        public void SetItems(IEnumerable<ParameterPickerItem> items, string keepKey)
        {
            _items = (items ?? Enumerable.Empty<ParameterPickerItem>()).ToList();
            _selected = keepKey == null ? null : _items.FirstOrDefault(i => i.Key == keepKey && Reason(i) == null);
            DrawField();
            if (_popup.IsOpen) Rebuild();
        }

        /// <summary>Selects by key without raising SelectionChanged. Returns false when not found or blocked.</summary>
        public bool SelectByKey(string key)
        {
            ParameterPickerItem it = _items.FirstOrDefault(i => i.Key == key && Reason(i) == null);
            if (it == null) return false;
            _selected = it;
            DrawField();
            return true;
        }

        /// <summary>Redraws after something <see cref="ExtraBlock"/> depends on changed.</summary>
        public void Refresh()
        {
            DrawField();
            if (_popup.IsOpen) Rebuild();
        }

        // ── Field ────────────────────────────────────────────────────────

        private void DrawField()
        {
            _fieldContent.Children.Clear();
            if (_selected == null)
            {
                _fieldContent.Children.Add(new TextBlock { Text = Placeholder, Foreground = BrMuted, VerticalAlignment = VerticalAlignment.Center });
            }
            else
            {
                _fieldContent.Children.Add(new TextBlock
                {
                    Text = _selected.Name,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = BrText,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 260,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                foreach (UIElement chip in Chips(_selected)) _fieldContent.Children.Add(chip);
            }
            _fieldContent.Children.Add(new TextBlock { Text = "  ▾", Foreground = BrMuted, VerticalAlignment = VerticalAlignment.Center });
        }

        private void OnFieldKey(object sender, KeyEventArgs e)
        {
            if (!_popup.IsOpen && (e.Key == Key.Down || e.Key == Key.Enter || e.Key == Key.Space))
            {
                _field.IsChecked = true;
                e.Handled = true;
            }
        }

        private void Open()
        {
            _popup.MinWidth = Math.Max(_field.ActualWidth, 440);
            _active = -1;
            Rebuild();
            _popup.IsOpen = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _search.Focus();
                Keyboard.Focus(_search);
                _search.SelectAll();
                ScrollSelectedIntoView();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        // ── List ─────────────────────────────────────────────────────────

        private string Reason(ParameterPickerItem it)
        {
            if (!string.IsNullOrEmpty(it.BlockedReason)) return it.BlockedReason;
            string extra = ExtraBlock?.Invoke(it);
            return string.IsNullOrEmpty(extra) ? null : extra;
        }

        private bool Matches(ParameterPickerItem it, string q) =>
            q.Length == 0
            || (it.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || (it.Group ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || (it.DataType ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        private void Rebuild()
        {
            string q = (_search.Text ?? string.Empty).Trim();
            foreach (string k in Kinds)
            {
                int n = _items.Count(i => i.Kind == k && Matches(i, q));
                _kindToggles[k].Content = k + "  " + n;
            }

            List<ParameterPickerItem> visible = _items
                .Where(i => Matches(i, q))
                .Where(i => i.Kind == "Other" || _kindToggles[i.Kind].IsChecked == true)
                .ToList();
            _count.Text = string.Format("{0} selectable · {1} shown · {2} total",
                visible.Count(i => Reason(i) == null), visible.Count, _items.Count);

            _list.Children.Clear();
            _choosable.Clear();

            if (visible.Count == 0)
            {
                _list.Children.Add(new TextBlock
                {
                    Text = q.Length > 0
                        ? "No parameter matches “" + q + "”.\nClear the search or turn a data type back on."
                        : "No parameter to show. Turn a data type back on.",
                    Foreground = BrMuted,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(12, 20, 12, 20),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            IEnumerable<IGrouping<string, ParameterPickerItem>> groups = visible
                .GroupBy(i => string.IsNullOrWhiteSpace(i.Group) ? "Other" : i.Group)
                .OrderBy(g => { int o = Array.IndexOf(GroupOrder, g.Key); return o < 0 ? 999 : o; })
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, ParameterPickerItem> g in groups)
            {
                List<ParameterPickerItem> rows = g
                    .OrderBy(i => Reason(i) == null ? 0 : 1)
                    .ThenBy(i => i.IsType ? 0 : 1)
                    .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                bool folded = _folded.Contains(g.Key) && q.Length == 0;
                _list.Children.Add(SectionHeader(g.Key, rows.Count(i => Reason(i) == null), rows.Count, folded));
                if (folded) continue;
                foreach (ParameterPickerItem it in rows) _list.Children.Add(Row(it, q));
            }
            HighlightActive();
        }

        private UIElement SectionHeader(string group, int usable, int total, bool folded)
        {
            var tb = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = BrMuted };
            tb.Inlines.Add(new Run((folded ? "▸  " : "▾  ") + group.ToUpperInvariant()));
            tb.Inlines.Add(new Run("   " + usable + "/" + total) { FontWeight = FontWeights.Normal });
            var b = new Border
            {
                Background = BrPanel,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 4, 10, 4),
                Cursor = Cursors.Hand,
                Child = tb,
                ToolTip = string.Format("{0}: {1} of {2} can be chosen. Click to fold or unfold.", group, usable, total)
            };
            b.MouseLeftButtonUp += delegate
            {
                if (!_folded.Add(group)) _folded.Remove(group);
                _active = -1;
                Rebuild();
            };
            return b;
        }

        private UIElement Row(ParameterPickerItem it, string q)
        {
            string reason = Reason(it);
            bool isSelected = _selected != null && _selected.Key == it.Key;

            var name = new TextBlock
            {
                FontWeight = reason == null ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = reason == null ? BrText : BrMuted,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            AddHighlighted(name, it.Name ?? string.Empty, q);

            var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (UIElement c in Chips(it)) chips.Children.Add(c);

            var meta = new TextBlock
            {
                Text = reason ?? it.Availability ?? string.Empty,
                FontSize = 11.5,
                Foreground = reason == null ? BrMuted : BrWarn,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(name);
            Grid.SetColumn(chips, 1);
            grid.Children.Add(chips);
            Grid.SetRow(meta, 1);
            Grid.SetColumnSpan(meta, 2);
            grid.Children.Add(meta);

            var row = new Border
            {
                Padding = new Thickness(23, 5, 10, 5),
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = isSelected ? BrAccent : Brushes.Transparent,
                Background = Brushes.Transparent,
                Child = grid,
                Cursor = reason == null ? Cursors.Hand : Cursors.No,
                Opacity = reason == null ? 1.0 : 0.75,
                ToolTip = reason == null ? "Choose " + it.Name : "Not selectable: " + reason + "."
            };
            ToolTipService.SetShowOnDisabled(row, true);

            if (reason == null)
            {
                int index = _choosable.Count;
                _choosable.Add(new KeyValuePair<ParameterPickerItem, Border>(it, row));
                row.MouseEnter += delegate { _active = index; HighlightActive(); };
                row.MouseLeftButtonUp += delegate { Choose(it); };
            }
            return row;
        }

        private static void AddHighlighted(TextBlock tb, string text, string q)
        {
            int at = q.Length == 0 ? -1 : text.IndexOf(q, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                tb.Inlines.Add(new Run(text));
                return;
            }
            if (at > 0) tb.Inlines.Add(new Run(text.Substring(0, at)));
            tb.Inlines.Add(new Run(text.Substring(at, q.Length)) { Background = BrHighlight });
            if (at + q.Length < text.Length) tb.Inlines.Add(new Run(text.Substring(at + q.Length)));
        }

        private static IEnumerable<UIElement> Chips(ParameterPickerItem it)
        {
            Brush[] kind = KindBrushes[it.Kind];
            yield return Chip(it.DataType ?? "?", kind[0], kind[1], null);
            yield return Chip(it.IsType ? "Type" : "Instance", BrMuted, Brushes.Transparent, BrBorder);
            if (it.IsShared)
            {
                Brush[] s = KindBrushes["Shared"];
                yield return Chip("Shared", s[0], s[1], null);
            }
        }

        private static UIElement Chip(string text, Brush fg, Brush bg, Brush border) =>
            new Border
            {
                Background = bg,
                BorderBrush = border ?? Brushes.Transparent,
                BorderThickness = new Thickness(border == null ? 0 : 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 0, 7, 1),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = fg }
            };

        private void StyleToggle(ToggleButton t, string kind)
        {
            Brush[] b = KindBrushes[kind];
            bool on = t.IsChecked == true;
            t.Foreground = on ? b[0] : BrMuted;
            t.Background = on ? b[1] : Brushes.White;
            t.BorderBrush = on ? b[1] : BrBorder;
            t.FontWeight = FontWeights.SemiBold;
            t.FontSize = 11;
        }

        // ── Keyboard and choosing ────────────────────────────────────────

        private void OnPopupKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    _popup.IsOpen = false;
                    _field.Focus();
                    e.Handled = true;
                    break;
                case Key.Down:
                    if (_choosable.Count > 0) { _active = Math.Min(_choosable.Count - 1, _active + 1); HighlightActive(); }
                    e.Handled = true;
                    break;
                case Key.Up:
                    if (_choosable.Count > 0) { _active = Math.Max(0, _active - 1); HighlightActive(); }
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (_active >= 0 && _active < _choosable.Count) Choose(_choosable[_active].Key);
                    e.Handled = true;
                    break;
            }
        }

        private void HighlightActive()
        {
            for (int i = 0; i < _choosable.Count; i++)
                _choosable[i].Value.Background = i == _active ? BrAccentSoft : Brushes.Transparent;
            if (_active >= 0 && _active < _choosable.Count) _choosable[_active].Value.BringIntoView();
        }

        private void ScrollSelectedIntoView()
        {
            if (_selected == null) return;
            int i = _choosable.FindIndex(kv => kv.Key.Key == _selected.Key);
            if (i >= 0) _choosable[i].Value.BringIntoView();
        }

        private void Choose(ParameterPickerItem it)
        {
            _selected = it;
            _popup.IsOpen = false;
            DrawField();
            _field.Focus();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private static SolidColorBrush Hex(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }
}
