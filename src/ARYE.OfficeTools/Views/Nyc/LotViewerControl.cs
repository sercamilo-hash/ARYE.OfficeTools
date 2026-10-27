#nullable disable
// This file deliberately imports NO Autodesk.Revit namespaces: WPF's Line,
// Point, Ellipse and Color collide with Revit DB types of the same names.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ARYE.OfficeTools.Core.Nyc;

namespace ARYE.OfficeTools.Views.Nyc
{
    /// <summary>A closed ring to draw, in world feet (EPSG 2263).</summary>
    public sealed class ViewerRing
    {
        public IReadOnlyList<LotPoint> Points { get; set; } = new List<LotPoint>();
        public Brush Stroke { get; set; } = Brushes.Gray;
        public double Thickness { get; set; } = 1.5;
        public DoubleCollection Dash { get; set; }
        public Brush Fill { get; set; } = Brushes.Transparent;
        public string Label { get; set; }
        public bool IsSubject { get; set; }
    }

    /// <summary>Per-face annotation of the subject lot.</summary>
    public sealed class ViewerFaceLabel
    {
        public int FaceIndex { get; set; }
        public LotPoint Start { get; set; }
        public LotPoint End { get; set; }
        public string Text { get; set; } = string.Empty;
        public string StreetName { get; set; } = string.Empty;
        public bool IsWarning { get; set; }
    }

    /// <summary>
    /// 2D plan viewer: rings per source, face dimensions, face index badges,
    /// anchor marker, wheel zoom and drag pan. World Y grows north; screen Y
    /// grows down — the transform flips it. Pure WPF, no Revit types.
    /// </summary>
    public sealed class LotViewerControl : Canvas
    {
        private static readonly Brush BadgeBrush = new SolidColorBrush(Color.FromRgb(200, 16, 16));
        private static readonly Brush AnchorBrush = new SolidColorBrush(Color.FromRgb(0, 113, 227));
        private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(180, 83, 9));
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));

        private double _scale = 3.0;      // px per ft
        private double _offsetX;          // world X at screen x = 0
        private double _offsetY;          // world Y at screen y = Height
        private System.Windows.Point _lastMouse;
        private bool _panning;

        public List<ViewerRing> Rings { get; } = new List<ViewerRing>();

        public List<ViewerFaceLabel> FaceLabels { get; } = new List<ViewerFaceLabel>();

        public LotPoint? Anchor { get; set; }

        public bool ShowDimensions { get; set; } = true;

        public bool ShowFaceBadges { get; set; } = true;

        public int HighlightedFace { get; set; } = -1;

        /// <summary>Raised when the user clicks near a subject face (index) or on empty space (-1).</summary>
        public event Action<int> FaceClicked;

        public LotViewerControl()
        {
            Background = new SolidColorBrush(Color.FromRgb(250, 251, 252));
            ClipToBounds = true;
            MouseWheel += OnWheel;
            MouseLeftButtonDown += OnDown;
            MouseLeftButtonUp += OnUp;
            MouseMove += OnMove;
            SizeChanged += (s, e) => Render();
        }

        // ── View transform ────────────────────────────────────────────────

        private System.Windows.Point ToScreen(LotPoint p) =>
            new System.Windows.Point((p.X - _offsetX) * _scale, ActualHeight - (p.Y - _offsetY) * _scale);

        private LotPoint ToWorld(System.Windows.Point s) =>
            new LotPoint(s.X / _scale + _offsetX, (ActualHeight - s.Y) / _scale + _offsetY);

        /// <summary>Zooms so the given points fill the view with a margin.</summary>
        public void Fit(IEnumerable<LotPoint> points, double marginFraction = 0.35)
        {
            List<LotPoint> pts = points?.ToList() ?? new List<LotPoint>();
            if (pts.Count == 0 || ActualWidth < 10 || ActualHeight < 10)
            {
                return;
            }
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double w = Math.Max(maxX - minX, 1) * (1 + marginFraction * 2);
            double h = Math.Max(maxY - minY, 1) * (1 + marginFraction * 2);
            _scale = Math.Min(ActualWidth / w, ActualHeight / h);
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            _offsetX = cx - ActualWidth / 2 / _scale;
            _offsetY = cy - ActualHeight / 2 / _scale;
            Render();
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            System.Windows.Point mouse = e.GetPosition(this);
            LotPoint before = ToWorld(mouse);
            _scale *= e.Delta > 0 ? 1.15 : 1 / 1.15;
            _scale = Math.Max(0.05, Math.Min(200, _scale));
            LotPoint after = ToWorld(mouse);
            _offsetX += before.X - after.X;
            _offsetY += before.Y - after.Y;
            Render();
            e.Handled = true;
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            _lastMouse = e.GetPosition(this);
            _panning = true;
            CaptureMouse();
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            System.Windows.Point now = e.GetPosition(this);
            bool wasClick = (now - _lastMouse).Length < 3;
            _panning = false;
            ReleaseMouseCapture();
            if (wasClick)
            {
                FaceClicked?.Invoke(HitTestFace(now));
            }
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!_panning)
            {
                return;
            }
            System.Windows.Point now = e.GetPosition(this);
            _offsetX -= (now.X - _lastMouse.X) / _scale;
            _offsetY += (now.Y - _lastMouse.Y) / _scale;
            _lastMouse = now;
            Render();
        }

        private int HitTestFace(System.Windows.Point screen)
        {
            int best = -1;
            double bestDist = 10; // px
            foreach (ViewerFaceLabel f in FaceLabels)
            {
                System.Windows.Point a = ToScreen(f.Start), b = ToScreen(f.End);
                double d = DistancePointSegment(screen, a, b);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = f.FaceIndex;
                }
            }
            return best;
        }

        private static double DistancePointSegment(System.Windows.Point p, System.Windows.Point a, System.Windows.Point b)
        {
            Vector ab = b - a, ap = p - a;
            double len2 = ab.LengthSquared;
            if (len2 < 1e-9)
            {
                return ap.Length;
            }
            double t = Math.Max(0, Math.Min(1, (ap * ab) / len2));
            return (p - (a + ab * t)).Length;
        }

        // ── Rendering ─────────────────────────────────────────────────────

        public void Render()
        {
            Children.Clear();
            if (ActualWidth < 10 || ActualHeight < 10)
            {
                return;
            }

            // Context rings first (under), subject rings on top.
            foreach (ViewerRing ring in Rings.Where(r => !r.IsSubject).Concat(Rings.Where(r => r.IsSubject)))
            {
                if (ring.Points.Count < 2)
                {
                    continue;
                }
                var polygon = new Polygon
                {
                    Stroke = ring.Stroke,
                    StrokeThickness = ring.Thickness,
                    StrokeDashArray = ring.Dash,
                    Fill = ring.Fill,
                    StrokeLineJoin = PenLineJoin.Miter,
                    IsHitTestVisible = false
                };
                foreach (LotPoint p in ring.Points)
                {
                    polygon.Points.Add(ToScreen(p));
                }
                Children.Add(polygon);

                if (!string.IsNullOrEmpty(ring.Label) && !ring.IsSubject)
                {
                    LotPoint c = new LotPoint(ring.Points.Average(p => p.X), ring.Points.Average(p => p.Y));
                    AddText(ring.Label, ToScreen(c), MutedBrush, 10, 0, true);
                }
            }

            foreach (ViewerFaceLabel f in FaceLabels)
            {
                System.Windows.Point a = ToScreen(f.Start), b = ToScreen(f.End);
                Vector dir = b - a;
                if (dir.Length < 1e-6)
                {
                    continue;
                }
                dir.Normalize();
                // Outward for a CCW ring in world coords is to the right of the direction;
                // on screen (Y flipped) that is (dir.Y, -dir.X)... compute in world instead.
                var wd = new Vector(f.End.X - f.Start.X, f.End.Y - f.Start.Y);
                wd.Normalize();
                var outwardWorld = new Vector(wd.Y, -wd.X);
                var mid = new LotPoint((f.Start.X + f.End.X) / 2, (f.Start.Y + f.End.Y) / 2);
                double angle = -Math.Atan2(wd.Y, wd.X) * 180 / Math.PI;
                if (angle > 90 || angle < -90)
                {
                    angle += 180;
                }

                if (HighlightedFace == f.FaceIndex)
                {
                    var hl = new Line
                    {
                        X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                        Stroke = new SolidColorBrush(Color.FromArgb(120, 0, 113, 227)),
                        StrokeThickness = 9, IsHitTestVisible = false
                    };
                    Children.Add(hl);
                }

                if (ShowDimensions && !string.IsNullOrEmpty(f.Text))
                {
                    double offPx = 14;
                    var labelWorld = new LotPoint(mid.X + outwardWorld.X * offPx / _scale, mid.Y + outwardWorld.Y * offPx / _scale);
                    AddText(f.Text, ToScreen(labelWorld), f.IsWarning ? WarnBrush : TextBrush, 11, angle, true);
                }
                if (!string.IsNullOrEmpty(f.StreetName))
                {
                    double offPx = 34;
                    var nameWorld = new LotPoint(mid.X + outwardWorld.X * offPx / _scale, mid.Y + outwardWorld.Y * offPx / _scale);
                    AddText(f.StreetName.ToUpperInvariant(), ToScreen(nameWorld), MutedBrush, 12, angle, true, FontWeights.SemiBold);
                }
                if (ShowFaceBadges)
                {
                    System.Windows.Point m = ToScreen(mid);
                    var badge = new Ellipse { Width = 16, Height = 16, Fill = BadgeBrush, IsHitTestVisible = false };
                    SetLeft(badge, m.X - 8); SetTop(badge, m.Y - 8);
                    Children.Add(badge);
                    var num = new TextBlock
                    {
                        Text = f.FaceIndex.ToString(CultureInfo.InvariantCulture), Foreground = Brushes.White,
                        FontSize = 10, FontWeight = FontWeights.Bold, IsHitTestVisible = false
                    };
                    num.Measure(new Size(100, 100));
                    SetLeft(num, m.X - num.DesiredSize.Width / 2); SetTop(num, m.Y - num.DesiredSize.Height / 2);
                    Children.Add(num);
                }
            }

            if (Anchor.HasValue)
            {
                System.Windows.Point s = ToScreen(Anchor.Value);
                var dot = new Ellipse { Width = 12, Height = 12, Fill = AnchorBrush, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
                SetLeft(dot, s.X - 6); SetTop(dot, s.Y - 6);
                Children.Add(dot);
                AddText("anchor → model origin", new System.Windows.Point(s.X + 10, s.Y + 10), AnchorBrush, 10, 0, false, FontWeights.SemiBold);
            }

            // North arrow + scale text
            AddText("N ↑  State Plane NY-LI (ftUS)", new System.Windows.Point(8, 6), MutedBrush, 10, 0, false);
            AddText($"{_scale:0.0} px/ft", new System.Windows.Point(ActualWidth - 70, ActualHeight - 20), MutedBrush, 10, 0, false);
        }

        private void AddText(string text, System.Windows.Point at, Brush brush, double size, double angleDeg, bool centered,
            FontWeight? weight = null)
        {
            var tb = new TextBlock
            {
                Text = text, Foreground = brush, FontSize = size, IsHitTestVisible = false,
                FontWeight = weight ?? FontWeights.Normal,
                Background = new SolidColorBrush(Color.FromArgb(200, 250, 251, 252)),
                Padding = new Thickness(2, 0, 2, 0)
            };
            tb.Measure(new Size(2000, 200));
            double w = tb.DesiredSize.Width, h = tb.DesiredSize.Height;
            if (centered)
            {
                tb.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                tb.RenderTransform = new RotateTransform(angleDeg);
                SetLeft(tb, at.X - w / 2);
                SetTop(tb, at.Y - h / 2);
            }
            else
            {
                SetLeft(tb, at.X);
                SetTop(tb, at.Y);
            }
            Children.Add(tb);
        }
    }
}
