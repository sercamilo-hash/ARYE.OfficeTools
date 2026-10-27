using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ARYE.OfficeTools.ViewModels;

namespace ARYE.OfficeTools.Views.Steps
{
    /// <summary>
    /// Code-behind exists for two things XAML cannot do on its own:
    ///
    ///   • The DataGrid columns are built dynamically — the deduction columns
    ///     come from the project's configurable category list.
    ///   • The DEDUCTIONS group band above the grid is kept aligned with the
    ///     columns it spans. WPF's DataGrid has no grouped headers, so the
    ///     band is a separate strip whose three segments (left block /
    ///     deductions / right block) are resized to the summed ActualWidth of
    ///     the underlying columns after every layout pass, and translated by
    ///     the grid's horizontal scroll offset so they never drift apart.
    /// </summary>
    public partial class Step3Matrix : UserControl
    {
        private static readonly Brush EnergyFill = Frozen(Color.FromRgb(0xFF, 0xF2, 0xCC));

        /// <summary>Grid border while the band is shown: the band carries the top edge.</summary>
        private static readonly Thickness GridBorderUnderBand = new Thickness(1, 0, 1, 1);

        /// <summary>Grid border when there is no band (no deduction category, or no matrix yet).</summary>
        private static readonly Thickness GridBorderAlone = new Thickness(1);

        private readonly List<KeyValuePair<DataGridColumn, MatrixColumnViewModel>> _boundColumns =
            new List<KeyValuePair<DataGridColumn, MatrixColumnViewModel>>();

        private ZoningMatrixViewModel _viewModel;

        public Step3Matrix()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;

            // ScrollChanged bubbles out of the grid's internal ScrollViewer; the
            // DataGrid scrolls horizontally in pixels, so the band follows 1:1.
            MatrixGrid.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler(OnMatrixScrolled));
            MatrixGrid.LayoutUpdated += OnMatrixLayoutUpdated;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.MatrixColumnsChanged -= OnColumnsChanged;
            }
            _viewModel = e.NewValue as ZoningMatrixViewModel;
            if (_viewModel != null)
            {
                _viewModel.MatrixColumnsChanged += OnColumnsChanged;
            }
            RebuildColumns();
        }

        private void OnColumnsChanged(object sender, EventArgs e) => RebuildColumns();

        private void RebuildColumns()
        {
            MatrixGrid.Columns.Clear();
            _boundColumns.Clear();
            if (_viewModel != null)
            {
                foreach (MatrixColumnViewModel column in _viewModel.Columns)
                {
                    DataGridColumn gridColumn = column.IsLevel
                        ? BuildLevelColumn(column)
                        : column.IsSync
                            ? BuildSyncColumn(column)
                            : BuildNumberColumn(column);
                    MatrixGrid.Columns.Add(gridColumn);
                    _boundColumns.Add(new KeyValuePair<DataGridColumn, MatrixColumnViewModel>(gridColumn, column));
                }
            }
            SyncHeaderBand();
        }

        // ── DEDUCTIONS band ──────────────────────────────────────────────────

        private void OnMatrixLayoutUpdated(object sender, EventArgs e) => SyncHeaderBand();

        private void OnMatrixScrolled(object sender, ScrollChangedEventArgs e)
        {
            if (Math.Abs(BandScroll.X + e.HorizontalOffset) > 0.5)
            {
                BandScroll.X = -e.HorizontalOffset;
            }
        }

        /// <summary>
        /// Left segment = columns before the first deduction, middle = the
        /// deduction columns, right = everything after. Widths are only
        /// written when they actually changed, so the layout pass this
        /// triggers settles immediately instead of looping.
        /// </summary>
        private void SyncHeaderBand()
        {
            double left = 0;
            double deductions = 0;
            double right = 0;
            bool seenDeduction = false;
            foreach (KeyValuePair<DataGridColumn, MatrixColumnViewModel> pair in _boundColumns)
            {
                double width = pair.Key.ActualWidth;
                if (double.IsNaN(width) || width < 0)
                {
                    width = 0;
                }
                if (pair.Value.IsDeduction)
                {
                    deductions += width;
                    seenDeduction = true;
                }
                else if (!seenDeduction)
                {
                    left += width;
                }
                else
                {
                    right += width;
                }
            }

            bool showBand = deductions > 0;
            Visibility visibility = showBand ? Visibility.Visible : Visibility.Collapsed;
            if (HeaderBand.Visibility != visibility)
            {
                HeaderBand.Visibility = visibility;
            }
            Thickness border = showBand ? GridBorderUnderBand : GridBorderAlone;
            if (MatrixGrid.BorderThickness != border)
            {
                MatrixGrid.BorderThickness = border;
            }
            SetWidth(LeftBand, left);
            SetWidth(DeductionBand, deductions);
            SetWidth(RightBand, right);
        }

        private static void SetWidth(FrameworkElement element, double width)
        {
            if (double.IsNaN(element.Width) || Math.Abs(element.Width - width) > 0.5)
            {
                element.Width = width;
            }
        }

        // ── Column factories ─────────────────────────────────────────────────

        /// <summary>LEVEL column: range color dot + level name.</summary>
        private DataGridColumn BuildLevelColumn(MatrixColumnViewModel column)
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var dot = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
            dot.SetValue(FrameworkElement.WidthProperty, 8.0);
            dot.SetValue(FrameworkElement.HeightProperty, 8.0);
            dot.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            dot.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            dot.SetBinding(System.Windows.Shapes.Shape.FillProperty, new Binding("RangeBrush"));
            dot.SetBinding(UIElement.VisibilityProperty, new Binding("HasRangeDot")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            panel.AppendChild(dot);

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding("Level"));
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            panel.AppendChild(text);

            return new DataGridTemplateColumn
            {
                Header = column.Header,
                HeaderStyle = BuildHeaderStyle(column),
                CellTemplate = new DataTemplate { VisualTree = panel },
                Width = new DataGridLength(150),
                MinWidth = 110
            };
        }

        /// <summary>SYNC column: the Step 2 status chip of the row, with the difference as its tooltip.</summary>
        private DataGridColumn BuildSyncColumn(MatrixColumnViewModel column)
        {
            var chip = new FrameworkElementFactory(typeof(Border));
            chip.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            chip.SetValue(Border.PaddingProperty, new Thickness(7, 1, 7, 1));
            chip.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            chip.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            chip.SetBinding(Border.BackgroundProperty, new Binding("Sync.Background"));
            chip.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Sync.ToolTip"));
            chip.SetBinding(UIElement.VisibilityProperty, new Binding("HasSync")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding("Sync.Text"));
            text.SetBinding(TextBlock.ForegroundProperty, new Binding("Sync.Foreground"));
            text.SetValue(TextBlock.FontSizeProperty, 10.5);
            text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            chip.AppendChild(text);

            return new DataGridTemplateColumn
            {
                Header = column.Header,
                HeaderStyle = BuildHeaderStyle(column),
                CellTemplate = new DataTemplate { VisualTree = chip },
                Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                MinWidth = 120
            };
        }

        /// <summary>Numeric column: right-aligned N2 text; the ENERGY column gets the pale yellow fill.</summary>
        private DataGridTextColumn BuildNumberColumn(MatrixColumnViewModel column)
        {
            var elementStyle = new Style(typeof(TextBlock));
            elementStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));

            var gridColumn = new DataGridTextColumn
            {
                Header = column.Header,
                HeaderStyle = BuildHeaderStyle(column),
                Binding = new Binding($"Cells[{column.CellIndex}]"),
                ElementStyle = elementStyle,
                Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                MinWidth = 90
            };

            if (column.IsEnergy)
            {
                var cellStyle = new Style(typeof(DataGridCell),
                    (Style)TryFindResource(typeof(DataGridCell)));
                cellStyle.Setters.Add(new Setter(Control.BackgroundProperty, EnergyFill));
                gridColumn.CellStyle = cellStyle;
            }
            return gridColumn;
        }

        /// <summary>Theme header style plus the column's ToolTip (office rule: everything explains itself).</summary>
        private Style BuildHeaderStyle(MatrixColumnViewModel column)
        {
            var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
                (Style)TryFindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, column.ToolTip));
            if (column.IsEnergy)
            {
                style.Setters.Add(new Setter(Control.BackgroundProperty, EnergyFill));
            }
            return style;
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
