#nullable disable
// Pure WPF — no Autodesk.Revit usings (Line/Point/Rectangle collide with Revit DB types).
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Sheets;
using ARYE.OfficeTools.Services;

namespace ARYE.OfficeTools.Views.Sheets
{
    /// <summary>
    /// The titleblock as it prints (rendered PNG) with the set's usable view
    /// area drawn over it: a rectangle defined by four margins in inches,
    /// draggable by its edges, plus the slot grid. Margins are dependency
    /// properties bound two-way to the set; they are pushed back on mouse-up
    /// only, so dragging never floods the live preview.
    /// </summary>
    public sealed class TitleBlockLayoutControl : Canvas
    {
        public static readonly DependencyProperty ImagePathProperty = DependencyProperty.Register(
            nameof(ImagePath), typeof(string), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(null, OnLayoutChanged));
        public static readonly DependencyProperty SheetWidthInProperty = DependencyProperty.Register(
            nameof(SheetWidthIn), typeof(double), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(36.0, OnLayoutChanged));
        public static readonly DependencyProperty SheetHeightInProperty = DependencyProperty.Register(
            nameof(SheetHeightIn), typeof(double), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(24.0, OnLayoutChanged));
        public static readonly DependencyProperty MarginLeftInProperty = DependencyProperty.Register(
            nameof(MarginLeftIn), typeof(double), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(0.25, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLayoutChanged));
        public static readonly DependencyProperty MarginRightInProperty = DependencyProperty.Register(
            nameof(MarginRightIn), typeof(double), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(2.25, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLayoutChanged));
        public static readonly DependencyProperty MarginTopInProperty = DependencyProperty.Register(
            nameof(MarginTopIn), typeof(double), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(0.25, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLayoutChanged));
        public static readonly DependencyProperty MarginBottomInProperty = DependencyProperty.Register(
            nameof(MarginBottomIn), typeof(double), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(0.25, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLayoutChanged));
        public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
            nameof(Columns), typeof(int), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(3, OnLayoutChanged));
        public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
            nameof(Rows), typeof(int), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(2, OnLayoutChanged));
        public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
            nameof(StatusText), typeof(string), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(null, OnLayoutChanged));
        public static readonly DependencyProperty PreviewWidthFtProperty = DependencyProperty.Register(
            nameof(PreviewWidthFt), typeof(double), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(0.0, OnLayoutChanged));
        public static readonly DependencyProperty PreviewHeightFtProperty = DependencyProperty.Register(
            nameof(PreviewHeightFt), typeof(double), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(0.0, OnLayoutChanged));
        public static readonly DependencyProperty PreviewLinesProperty = DependencyProperty.Register(
            nameof(PreviewLines), typeof(IReadOnlyList<PreviewLine>), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(null, OnLayoutChanged));
        public static readonly DependencyProperty PreviewScaleDenominatorProperty = DependencyProperty.Register(
            nameof(PreviewScaleDenominator), typeof(int), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(0, OnLayoutChanged));

        public static readonly DependencyProperty TitleBlockLinesProperty = DependencyProperty.Register(
            nameof(TitleBlockLines), typeof(IReadOnlyList<TitleBlockLine>), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(null, OnLayoutChanged));
        public static readonly DependencyProperty ColumnCumulativeProperty = DependencyProperty.Register(
            nameof(ColumnCumulative), typeof(double[]), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLayoutChanged));
        public static readonly DependencyProperty RowCumulativeProperty = DependencyProperty.Register(
            nameof(RowCumulative), typeof(double[]), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLayoutChanged));
        public static readonly DependencyProperty SnapEnabledProperty = DependencyProperty.Register(
            nameof(SnapEnabled), typeof(bool), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(true, OnLayoutChanged));
        public static readonly DependencyProperty ShowTitleBlockLinesProperty = DependencyProperty.Register(
            nameof(ShowTitleBlockLines), typeof(bool), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(true, OnLayoutChanged));
        public static readonly DependencyProperty SnapDistanceInProperty = DependencyProperty.Register(
            nameof(SnapDistanceIn), typeof(double), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(0.3));
        public static readonly DependencyProperty DrawSeparatorLinesProperty = DependencyProperty.Register(
            nameof(DrawSeparatorLines), typeof(bool), typeof(TitleBlockLayoutControl), new FrameworkPropertyMetadata(false, OnLayoutChanged));

        private const double MinUsableIn = 1.0;
        private const double MinCellIn = 2.0;
        private const double EqualMagnetIn = 0.2;
        private const double HandleHitPx = 9;

        private static readonly Brush AreaFill = new SolidColorBrush(Color.FromArgb(40, 0, 113, 227));
        private static readonly Brush AreaStroke = new SolidColorBrush(Color.FromRgb(0, 113, 227));
        private static readonly Brush GridStroke = new SolidColorBrush(Color.FromArgb(160, 0, 113, 227));
        private static readonly Brush HandleFill = Brushes.White;
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        private static readonly Brush ContentStroke = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        private static readonly Brush SnapStroke = new SolidColorBrush(Color.FromRgb(217, 119, 6));
        private static readonly Brush TitleBlockLineStroke = new SolidColorBrush(Color.FromArgb(110, 217, 119, 6));
        private static readonly Brush SlotStroke = new SolidColorBrush(Color.FromArgb(70, 0, 113, 227));
        private static readonly Brush ContentWallStroke = new SolidColorBrush(Color.FromRgb(100, 116, 139));

        private BitmapImage _bitmap;
        private string _bitmapPath;
        private Edge _dragging = Edge.None;
        private int _dragSeparator;                                  // 1-based, for ColumnSeparator / RowSeparator drags
        private double _dragLeft, _dragRight, _dragTop, _dragBottom; // live values during a drag, inches
        private double[] _liveColumns, _liveRows;                    // live cell shares during a separator drag
        private bool _guideVertical;                                 // the snap guide line currently shown
        private double _guideInches = double.NaN;                    // from the left (vertical) or from the top (horizontal)
        private string _guideCaption;

        public TitleBlockLayoutControl()
        {
            Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            ClipToBounds = true;
            MinHeight = 200;
            Cursor = Cursors.Arrow;
            SizeChanged += (s, e) => Render();
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseLeave += (s, e) => { if (_dragging == Edge.None) Cursor = Cursors.Arrow; };
        }

        public string ImagePath { get => (string)GetValue(ImagePathProperty); set => SetValue(ImagePathProperty, value); }
        public double SheetWidthIn { get => (double)GetValue(SheetWidthInProperty); set => SetValue(SheetWidthInProperty, value); }
        public double SheetHeightIn { get => (double)GetValue(SheetHeightInProperty); set => SetValue(SheetHeightInProperty, value); }
        public double MarginLeftIn { get => (double)GetValue(MarginLeftInProperty); set => SetValue(MarginLeftInProperty, value); }
        public double MarginRightIn { get => (double)GetValue(MarginRightInProperty); set => SetValue(MarginRightInProperty, value); }
        public double MarginTopIn { get => (double)GetValue(MarginTopInProperty); set => SetValue(MarginTopInProperty, value); }
        public double MarginBottomIn { get => (double)GetValue(MarginBottomInProperty); set => SetValue(MarginBottomInProperty, value); }
        public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
        public int Rows { get => (int)GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
        /// <summary>The titleblock family's own straight lines (inches from its lower-left corner) - the snap targets.</summary>
        public IReadOnlyList<TitleBlockLine> TitleBlockLines { get => (IReadOnlyList<TitleBlockLine>)GetValue(TitleBlockLinesProperty); set => SetValue(TitleBlockLinesProperty, value); }

        /// <summary>Dragged cell sizes as cumulative shares (null/empty = equal cells).</summary>
        public double[] ColumnCumulative { get => (double[])GetValue(ColumnCumulativeProperty); set => SetValue(ColumnCumulativeProperty, value); }
        public double[] RowCumulative { get => (double[])GetValue(RowCumulativeProperty); set => SetValue(RowCumulativeProperty, value); }

        public bool SnapEnabled { get => (bool)GetValue(SnapEnabledProperty); set => SetValue(SnapEnabledProperty, value); }
        public bool ShowTitleBlockLines { get => (bool)GetValue(ShowTitleBlockLinesProperty); set => SetValue(ShowTitleBlockLinesProperty, value); }
        /// <summary>How close an edge must come to a titleblock line to snap to it, inches.</summary>
        public double SnapDistanceIn { get => (double)GetValue(SnapDistanceInProperty); set => SetValue(SnapDistanceInProperty, value); }
        public bool DrawSeparatorLines { get => (bool)GetValue(DrawSeparatorLinesProperty); set => SetValue(DrawSeparatorLinesProperty, value); }

        public string StatusText { get => (string)GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }

        /// <summary>Real footprint (feet) of the sample content drawn inside the first slot — same data as the scope box preview.</summary>
        public double PreviewWidthFt { get => (double)GetValue(PreviewWidthFtProperty); set => SetValue(PreviewWidthFtProperty, value); }
        public double PreviewHeightFt { get => (double)GetValue(PreviewHeightFtProperty); set => SetValue(PreviewHeightFtProperty, value); }
        public IReadOnlyList<PreviewLine> PreviewLines { get => (IReadOnlyList<PreviewLine>)GetValue(PreviewLinesProperty); set => SetValue(PreviewLinesProperty, value); }

        /// <summary>The run's resolved scale (0 = unknown yet) — converts the preview's real feet to paper inches, same convention as SmartScaleAdvisor.</summary>
        public int PreviewScaleDenominator { get => (int)GetValue(PreviewScaleDenominatorProperty); set => SetValue(PreviewScaleDenominatorProperty, value); }

        private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TitleBlockLayoutControl)d).Render();

        // ── Geometry: sheet inches ↔ screen pixels ────────────────────────

        private Rect SheetRect()
        {
            double w = Math.Max(1, SheetWidthIn), h = Math.Max(1, SheetHeightIn);
            double pad = 10;
            double scale = Math.Min((ActualWidth - 2 * pad) / w, (ActualHeight - 2 * pad) / h);
            if (scale <= 0 || double.IsInfinity(scale)) scale = 1;
            double pw = w * scale, ph = h * scale;
            return new Rect((ActualWidth - pw) / 2, (ActualHeight - ph) / 2, pw, ph);
        }

        private double PxPerIn(Rect sheet) => sheet.Width / Math.Max(1, SheetWidthIn);

        private Rect AreaRect(Rect sheet, double l, double r, double t, double b)
        {
            double k = PxPerIn(sheet);
            return new Rect(sheet.Left + l * k, sheet.Top + t * k, Math.Max(0, sheet.Width - (l + r) * k), Math.Max(0, sheet.Height - (t + b) * k));
        }

        // ── Interaction ───────────────────────────────────────────────────

        private enum Edge { None, Left, Right, Top, Bottom, ColumnSeparator, RowSeparator }

        private double[] LiveColumns(int cols) => _dragging == Edge.ColumnSeparator && _liveColumns != null
            ? _liveColumns : SheetSlotLayout.NormalizeCumulative(ColumnCumulative, cols).ToArray();

        private double[] LiveRows(int rows) => _dragging == Edge.RowSeparator && _liveRows != null
            ? _liveRows : SheetSlotLayout.NormalizeCumulative(RowCumulative, rows).ToArray();

        private (double l, double r, double t, double b) LiveMargins() => _dragging == Edge.None || _dragging >= Edge.ColumnSeparator
            ? (MarginLeftIn, MarginRightIn, MarginTopIn, MarginBottomIn)
            : (_dragLeft, _dragRight, _dragTop, _dragBottom);

        private Edge HitEdge(Point p, out int separator)
        {
            separator = 0;
            Rect sheet = SheetRect();
            Rect area = AreaRect(sheet, MarginLeftIn, MarginRightIn, MarginTopIn, MarginBottomIn);
            bool insideY = p.Y >= area.Top - HandleHitPx && p.Y <= area.Bottom + HandleHitPx;
            bool insideX = p.X >= area.Left - HandleHitPx && p.X <= area.Right + HandleHitPx;
            if (insideY && Math.Abs(p.X - area.Left) <= HandleHitPx) return Edge.Left;
            if (insideY && Math.Abs(p.X - area.Right) <= HandleHitPx) return Edge.Right;
            if (insideX && Math.Abs(p.Y - area.Top) <= HandleHitPx) return Edge.Top;
            if (insideX && Math.Abs(p.Y - area.Bottom) <= HandleHitPx) return Edge.Bottom;

            double k = PxPerIn(sheet);
            int cols = Math.Max(1, Columns), rows = Math.Max(1, Rows);
            if (insideY && p.Y >= area.Top && p.Y <= area.Bottom)
            {
                IReadOnlyList<double> xs = SheetSlotLayout.SeparatorPositionsIn(area.Width / k, cols, ColumnCumulative);
                for (int i = 0; i < xs.Count; i++)
                {
                    if (Math.Abs(p.X - (area.Left + xs[i] * k)) <= HandleHitPx) { separator = i + 1; return Edge.ColumnSeparator; }
                }
            }
            if (insideX && p.X >= area.Left && p.X <= area.Right)
            {
                IReadOnlyList<double> ys = SheetSlotLayout.SeparatorPositionsIn(area.Height / k, rows, RowCumulative);
                for (int i = 0; i < ys.Count; i++)
                {
                    if (Math.Abs(p.Y - (area.Top + ys[i] * k)) <= HandleHitPx) { separator = i + 1; return Edge.RowSeparator; }
                }
            }
            return Edge.None;
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            Edge edge = HitEdge(e.GetPosition(this), out int separator);
            if (edge == Edge.None) return;
            _dragging = edge;
            _dragSeparator = separator;
            _dragLeft = MarginLeftIn; _dragRight = MarginRightIn; _dragTop = MarginTopIn; _dragBottom = MarginBottomIn;
            _liveColumns = SheetSlotLayout.NormalizeCumulative(ColumnCumulative, Math.Max(1, Columns)).ToArray();
            _liveRows = SheetSlotLayout.NormalizeCumulative(RowCumulative, Math.Max(1, Rows)).ToArray();
            CaptureMouse();
            e.Handled = true;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            Point p = e.GetPosition(this);
            if (_dragging == Edge.None)
            {
                Edge hover = HitEdge(p, out _);
                Cursor = hover == Edge.Left || hover == Edge.Right || hover == Edge.ColumnSeparator ? Cursors.SizeWE
                    : hover == Edge.None ? Cursors.Arrow : Cursors.SizeNS;
                return;
            }
            Rect sheet = SheetRect();
            double k = PxPerIn(sheet);
            double xIn = (p.X - sheet.Left) / k;                 // from the left edge
            double yIn = (p.Y - sheet.Top) / k;                  // from the top edge
            double w = SheetWidthIn, h = SheetHeightIn;
            _guideInches = double.NaN;
            _guideCaption = null;
            switch (_dragging)
            {
                case Edge.Left: _dragLeft = Clamp(SnapToLines(xIn, true), 0, w - _dragRight - MinUsableIn); break;
                case Edge.Right: _dragRight = Clamp(w - SnapToLines(xIn, true), 0, w - _dragLeft - MinUsableIn); break;
                case Edge.Top: _dragTop = Clamp(SnapToLines(yIn, false), 0, h - _dragBottom - MinUsableIn); break;
                case Edge.Bottom: _dragBottom = Clamp(h - SnapToLines(yIn, false), 0, h - _dragTop - MinUsableIn); break;
                case Edge.ColumnSeparator: DragSeparator(xIn - MarginLeftIn, w - MarginLeftIn - MarginRightIn, Math.Max(1, Columns), _liveColumns); break;
                case Edge.RowSeparator: DragSeparator(yIn - MarginTopIn, h - MarginTopIn - MarginBottomIn, Math.Max(1, Rows), _liveRows); break;
            }
            Render();
        }

        /// <summary>Moves the dragged separator to a position (inches along its axis), with the equal-cells magnet.</summary>
        private void DragSeparator(double positionIn, double lengthIn, int count, double[] shares)
        {
            int n = _dragSeparator;
            if (n < 1 || n > shares.Length || lengthIn <= 0) return;
            double pos = Math.Round(positionIn * 8) / 8.0;
            double equal = SheetSlotLayout.SeparatorPositionsIn(lengthIn, count, null)[n - 1];
            if (Math.Abs(pos - equal) <= EqualMagnetIn)
            {
                pos = equal;
                _guideCaption = "= equal cells";
            }
            double net = lengthIn - SheetSlotLayout.GutterIn * (count - 1);
            double minShare = MinCellIn / net;
            double lo = (n == 1 ? 0 : shares[n - 2]) + minShare;
            double hi = (n == shares.Length ? 1 : shares[n]) - minShare;
            if (lo > hi) return;
            shares[n - 1] = Clamp(SheetSlotLayout.CumulativeFromPositionIn(lengthIn, count, n, pos), lo, hi);
        }

        /// <summary>Pegs a dragged edge to the nearest titleblock line within tolerance (hold Alt to bypass); otherwise 1/8 in steps.</summary>
        private double SnapToLines(double inches, bool vertical)
        {
            if (SnapEnabled && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
            {
                double? best = null;
                foreach (double candidate in LineCandidates(vertical))
                {
                    if (Math.Abs(candidate - inches) <= SnapDistanceIn && (best == null || Math.Abs(candidate - inches) < Math.Abs(best.Value - inches)))
                    {
                        best = candidate;
                    }
                }
                if (best != null)
                {
                    _guideVertical = vertical;
                    _guideInches = best.Value;
                    _guideCaption = "snap to titleblock line, " + best.Value.ToString("0.###") + "\" from the " + (vertical ? "left" : "top");
                    return best.Value;
                }
            }
            return Snap(inches);
        }

        /// <summary>Positions of the family's vertical (inches from the left) or horizontal (inches from the top) lines, de-duplicated.</summary>
        private IEnumerable<double> LineCandidates(bool vertical)
        {
            var seen = new HashSet<long>();
            foreach (TitleBlockLine line in TitleBlockLines ?? (IReadOnlyList<TitleBlockLine>)Array.Empty<TitleBlockLine>())
            {
                if (line.IsVertical != vertical) continue;
                double value = vertical ? line.X1 : SheetHeightIn - line.Y1;
                if (seen.Add((long)Math.Round(value * 1000))) yield return value;
            }
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragging == Edge.None) return;
            Edge finished = _dragging;
            _dragging = Edge.None;
            ReleaseMouseCapture();
            _guideInches = double.NaN;
            _guideCaption = null;
            // Push once - this is what the set stores and what re-runs the preview.
            if (finished == Edge.ColumnSeparator) ColumnCumulative = _liveColumns;
            else if (finished == Edge.RowSeparator) RowCumulative = _liveRows;
            else { MarginLeftIn = _dragLeft; MarginRightIn = _dragRight; MarginTopIn = _dragTop; MarginBottomIn = _dragBottom; }
            Render();
        }

        private static double Snap(double inches) => Math.Round(inches * 8) / 8.0; // 1/8" steps

        private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));

        // ── Rendering ─────────────────────────────────────────────────────

        private void Render()
        {
            Children.Clear();
            if (ActualWidth < 20 || ActualHeight < 20) return;
            Rect sheet = SheetRect();

            // Titleblock backdrop: the rendered image, or a plain sheet rectangle.
            if (!string.IsNullOrEmpty(ImagePath) && File.Exists(ImagePath))
            {
                if (_bitmapPath != ImagePath)
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad; // release the file right away
                        bmp.UriSource = new Uri(ImagePath);
                        bmp.EndInit();
                        bmp.Freeze();
                        _bitmap = bmp;
                        _bitmapPath = ImagePath;
                    }
                    catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is UriFormatException)
                    {
                        _bitmap = null;
                        _bitmapPath = null;
                    }
                }
            }
            else
            {
                _bitmap = null;
                _bitmapPath = null;
            }

            var paper = new Rectangle { Width = sheet.Width, Height = sheet.Height, Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(148, 163, 184)), StrokeThickness = 1 };
            SetLeft(paper, sheet.Left); SetTop(paper, sheet.Top);
            Children.Add(paper);
            if (_bitmap != null)
            {
                var img = new Image { Source = _bitmap, Width = sheet.Width, Height = sheet.Height, Stretch = Stretch.Fill, IsHitTestVisible = false };
                SetLeft(img, sheet.Left); SetTop(img, sheet.Top);
                Children.Add(img);
            }

            // Usable area (live values during a drag).
            (double l, double r, double t, double b) = LiveMargins();
            Rect area = AreaRect(sheet, l, r, t, b);
            var areaShape = new Rectangle { Width = area.Width, Height = area.Height, Fill = AreaFill, Stroke = AreaStroke, StrokeThickness = 2, IsHitTestVisible = false };
            SetLeft(areaShape, area.Left); SetTop(areaShape, area.Top);
            Children.Add(areaShape);

            double k = PxPerIn(sheet);

            // The family's own lines - what the area snaps to.
            if (ShowTitleBlockLines && TitleBlockLines != null)
            {
                foreach (TitleBlockLine tl in TitleBlockLines)
                {
                    double x1 = sheet.Left + tl.X1 * k, x2 = sheet.Left + tl.X2 * k;
                    double y1 = sheet.Bottom - tl.Y1 * k, y2 = sheet.Bottom - tl.Y2 * k;
                    Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = TitleBlockLineStroke, StrokeThickness = 1, IsHitTestVisible = false });
                }
            }

            // Slot matrix: cells (equal unless dragged) with a separator in the middle of each gutter.
            int cols = Math.Max(1, Columns), rows = Math.Max(1, Rows);
            double[] colCum = LiveColumns(cols), rowCum = LiveRows(rows);
            double areaWIn = area.Width / k, areaHIn = area.Height / k;
            IReadOnlyList<SlotRect> slots = null;
            try { slots = SheetSlotLayout.ComputeGrid(areaWIn, areaHIn, cols, rows, colCum, rowCum); }
            catch (ArgumentException) { /* area too small for the matrix - the run reports it; nothing to draw */ }
            if (slots != null)
            {
                foreach (SlotRect slot in slots)
                {
                    var cell = new Rectangle { Width = slot.WidthIn * k, Height = slot.HeightIn * k, Stroke = SlotStroke, StrokeThickness = 1, IsHitTestVisible = false };
                    SetLeft(cell, area.Left + slot.LeftIn * k); SetTop(cell, area.Bottom - slot.TopIn * k);
                    Children.Add(cell);
                }
            }
            DoubleCollection dashes = DrawSeparatorLines ? null : new DoubleCollection { 4, 3 };
            double sepThickness = DrawSeparatorLines ? 2 : 1;
            foreach (double xIn in SheetSlotLayout.SeparatorPositionsIn(areaWIn, cols, colCum))
            {
                double x = area.Left + xIn * k;
                Children.Add(new Line { X1 = x, Y1 = area.Top, X2 = x, Y2 = area.Bottom, Stroke = GridStroke, StrokeThickness = sepThickness, StrokeDashArray = dashes, IsHitTestVisible = false });
                AddHandle(x, area.Top + area.Height / 2, true);
            }
            foreach (double yIn in SheetSlotLayout.SeparatorPositionsIn(areaHIn, rows, rowCum))
            {
                double y = area.Top + yIn * k;
                Children.Add(new Line { X1 = area.Left, Y1 = y, X2 = area.Right, Y2 = y, Stroke = GridStroke, StrokeThickness = sepThickness, StrokeDashArray = dashes, IsHitTestVisible = false });
                AddHandle(area.Left + area.Width / 2, y, false);
            }

            // Sample content in the first slot: the previewed view's real walls at the
            // run's actual scale - real feedback on whether it fits, not a guess. Drawn
            // deliberately unclipped to its slot: content that spills past the cell
            // line IS the point - it means this scale or matrix is too tight.
            if (slots != null) RenderContentSample(area, slots[0], k);

            // Snap guide while dragging an edge onto a titleblock line.
            if (!double.IsNaN(_guideInches))
            {
                double gx = sheet.Left + _guideInches * k, gy = sheet.Top + _guideInches * k;
                Children.Add(_guideVertical
                    ? new Line { X1 = gx, Y1 = sheet.Top, X2 = gx, Y2 = sheet.Bottom, Stroke = SnapStroke, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 5, 3 }, IsHitTestVisible = false }
                    : new Line { X1 = sheet.Left, Y1 = gy, X2 = sheet.Right, Y2 = gy, Stroke = SnapStroke, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 5, 3 }, IsHitTestVisible = false });
            }
            if (!string.IsNullOrEmpty(_guideCaption))
            {
                AddText(_guideCaption, new Point(sheet.Left + 4, sheet.Top + 20), SnapStroke, 10, true);
            }

            // Edge handles.
            foreach (Point hp in new[] { new Point(area.Left, area.Top + area.Height / 2), new Point(area.Right, area.Top + area.Height / 2),
                                          new Point(area.Left + area.Width / 2, area.Top), new Point(area.Left + area.Width / 2, area.Bottom) })
            {
                var handle = new Rectangle { Width = 10, Height = 10, Fill = HandleFill, Stroke = AreaStroke, StrokeThickness = 1.5, IsHitTestVisible = false };
                SetLeft(handle, hp.X - 5); SetTop(handle, hp.Y - 5);
                Children.Add(handle);
            }

            // Captions: margins in inches, slot size.
            double slotWIn = slots == null ? 0 : slots.Min(sl => sl.WidthIn), slotHIn = slots == null ? 0 : slots.Min(sl => sl.HeightIn);
            bool custom = !(SheetSlotLayout.IsEqualCumulative(colCum, cols) && SheetSlotLayout.IsEqualCumulative(rowCum, rows));
            AddText($"{l:0.##}\"", new Point(sheet.Left + 3, area.Top + area.Height / 2 - 8), MutedBrush, 10);
            AddText($"{r:0.##}\"", new Point(area.Right + 3, area.Top + area.Height / 2 - 8), MutedBrush, 10);
            AddText($"{t:0.##}\"", new Point(area.Left + area.Width / 2 + 8, sheet.Top + 2), MutedBrush, 10);
            AddText($"{b:0.##}\"", new Point(area.Left + area.Width / 2 + 8, area.Bottom + 2), MutedBrush, 10);
            AddText($"{cols}×{rows}{(custom ? " custom" : "")} · {(custom ? "smallest " : "")}slot {slotWIn:0.##}\" × {slotHIn:0.##}\" · sheet {SheetWidthIn:0.##}\" × {SheetHeightIn:0.##}\"",
                new Point(sheet.Left + 4, sheet.Bottom - 16), TextBrush, 10, true);
            if (!string.IsNullOrEmpty(StatusText))
            {
                AddText(StatusText, new Point(sheet.Left + 4, sheet.Top + 4), MutedBrush, 10, true);
            }
        }

        private void AddHandle(double x, double y, bool vertical)
        {
            var handle = new Rectangle
            {
                Width = vertical ? 6 : 22, Height = vertical ? 22 : 6, RadiusX = 2, RadiusY = 2,
                Fill = HandleFill, Stroke = AreaStroke, StrokeThickness = 1.5, IsHitTestVisible = false
            };
            SetLeft(handle, x - handle.Width / 2); SetTop(handle, y - handle.Height / 2);
            Children.Add(handle);
        }

        private void RenderContentSample(Rect area, SlotRect firstSlot, double k)
        {
            if (PreviewScaleDenominator <= 0 || PreviewWidthFt <= 0 || PreviewHeightFt <= 0)
            {
                return;
            }
            double contentWIn = PreviewWidthFt * 12.0 / PreviewScaleDenominator;
            double contentHIn = PreviewHeightFt * 12.0 / PreviewScaleDenominator;
            double originX = area.Left + firstSlot.CenterXIn * k - contentWIn * k / 2;
            double originY = area.Bottom - firstSlot.CenterYIn * k - contentHIn * k / 2;

            Point ToScreen(double xFt, double yFt)
            {
                double xIn = xFt * 12.0 / PreviewScaleDenominator;
                double yIn = yFt * 12.0 / PreviewScaleDenominator;
                // Model Y grows north (up); Canvas Y grows down — flip within the content's own height.
                return new Point(originX + xIn * k, originY + (contentHIn - yIn) * k);
            }

            var box = new Rectangle
            {
                Width = Math.Max(0, contentWIn * k), Height = Math.Max(0, contentHIn * k),
                Stroke = ContentStroke, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false
            };
            SetLeft(box, originX);
            SetTop(box, originY);
            Children.Add(box);

            if (PreviewLines != null)
            {
                foreach (PreviewLine line in PreviewLines)
                {
                    Point p1 = ToScreen(line.X1, line.Y1);
                    Point p2 = ToScreen(line.X2, line.Y2);
                    Children.Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = ContentWallStroke, StrokeThickness = 1, IsHitTestVisible = false });
                }
            }
        }

        private void AddText(string text, Point at, Brush brush, double size, bool boxed = false)
        {
            var tb = new TextBlock { Text = text, Foreground = brush, FontSize = size, IsHitTestVisible = false, Padding = new Thickness(3, 1, 3, 1) };
            if (boxed) tb.Background = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
            SetLeft(tb, at.X); SetTop(tb, at.Y);
            Children.Add(tb);
        }
    }
}
