#nullable disable
// Pure WPF — no Autodesk.Revit usings (Line/Rectangle/Point collide with Revit DB types).
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ARYE.OfficeTools.Services;

namespace ARYE.OfficeTools.Views.Sheets
{
    /// <summary>
    /// A quick, honestly-incomplete sanity check for the scope box a set will
    /// crop its created views to: the crop rectangle at scale, with whatever
    /// walls fall inside it drawn as thin reference lines. Not a rendered
    /// view — just enough for the user to see "yes, that's the right area"
    /// before generating.
    /// </summary>
    public sealed class ScopeBoxPreviewControl : Canvas
    {
        public static readonly DependencyProperty WidthFtProperty = DependencyProperty.Register(
            nameof(WidthFt), typeof(double), typeof(ScopeBoxPreviewControl), new FrameworkPropertyMetadata(0.0, OnChanged));
        public static readonly DependencyProperty HeightFtProperty = DependencyProperty.Register(
            nameof(HeightFt), typeof(double), typeof(ScopeBoxPreviewControl), new FrameworkPropertyMetadata(0.0, OnChanged));
        public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
            nameof(Lines), typeof(IReadOnlyList<PreviewLine>), typeof(ScopeBoxPreviewControl), new FrameworkPropertyMetadata(null, OnChanged));
        public static readonly DependencyProperty NoteProperty = DependencyProperty.Register(
            nameof(Note), typeof(string), typeof(ScopeBoxPreviewControl), new FrameworkPropertyMetadata(null, OnChanged));

        private static readonly Brush BoxStroke = new SolidColorBrush(Color.FromRgb(0, 113, 227));
        private static readonly Brush BoxFill = new SolidColorBrush(Color.FromArgb(18, 0, 113, 227));
        private static readonly Brush WallStroke = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        private static readonly Brush NoteBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));

        public ScopeBoxPreviewControl()
        {
            Background = new SolidColorBrush(Color.FromRgb(250, 251, 252));
            ClipToBounds = true;
            MinHeight = 110;
            SizeChanged += (s, e) => Render();
        }

        public double WidthFt { get => (double)GetValue(WidthFtProperty); set => SetValue(WidthFtProperty, value); }
        public double HeightFt { get => (double)GetValue(HeightFtProperty); set => SetValue(HeightFtProperty, value); }
        public IReadOnlyList<PreviewLine> Lines { get => (IReadOnlyList<PreviewLine>)GetValue(LinesProperty); set => SetValue(LinesProperty, value); }

        /// <summary>Shown under the drawing (or alone, when there is nothing to draw). Never blank when the preview is incomplete.</summary>
        public string Note { get => (string)GetValue(NoteProperty); set => SetValue(NoteProperty, value); }

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ScopeBoxPreviewControl)d).Render();

        private void Render()
        {
            Children.Clear();
            double w = ActualWidth, h = ActualHeight;
            if (w < 4 || h < 4)
            {
                return;
            }

            if (WidthFt <= 0 || HeightFt <= 0)
            {
                AddNote(Note ?? "No scope box to preview.", w, h, centered: true);
                return;
            }

            const double pad = 8;
            const double noteHeight = 16;
            double availW = Math.Max(1, w - pad * 2);
            double availH = Math.Max(1, h - pad * 2 - (string.IsNullOrEmpty(Note) ? 0 : noteHeight));
            double scale = Math.Min(availW / WidthFt, availH / HeightFt);
            double drawW = WidthFt * scale, drawH = HeightFt * scale;
            double ox = (w - drawW) / 2, oy = pad + (availH - drawH) / 2;

            // Revit Y grows north (up the plan); screen Y grows down — flip.
            Point ToScreen(double xFt, double yFt) => new Point(ox + xFt * scale, oy + drawH - yFt * scale);

            var box = new Rectangle { Width = drawW, Height = drawH, Stroke = BoxStroke, StrokeThickness = 1.5, Fill = BoxFill };
            SetLeft(box, ox);
            SetTop(box, oy);
            Children.Add(box);

            if (Lines != null)
            {
                foreach (PreviewLine l in Lines)
                {
                    Point p1 = ToScreen(l.X1, l.Y1);
                    Point p2 = ToScreen(l.X2, l.Y2);
                    Children.Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = WallStroke, StrokeThickness = 1 });
                }
            }

            if (!string.IsNullOrEmpty(Note))
            {
                AddNote(Note, w, h, centered: false);
            }
        }

        private void AddNote(string text, double w, double h, bool centered)
        {
            var tb = new TextBlock
            {
                Text = text, FontSize = 10.5, Foreground = NoteBrush, TextWrapping = TextWrapping.Wrap,
                Width = Math.Max(10, w - 12), TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left
            };
            SetLeft(tb, 6);
            SetTop(tb, centered ? Math.Max(2, h / 2 - 10) : Math.Max(2, h - 16));
            Children.Add(tb);
        }
    }
}
