using System.Windows;
using System.Windows.Media;

namespace Orva.Controls
{
    /// <summary>
    /// Print registration mark (crosshair + circle). Decorative print-craft motif.
    /// Pure code rendering; no XAML required. Never interactive.
    /// </summary>
    public sealed class RegistrationMark : FrameworkElement
    {
        private static readonly Brush DefaultInk;

        static RegistrationMark()
        {
            var brush = new SolidColorBrush(Color.FromArgb(140, 26, 23, 18));
            brush.Freeze();
            DefaultInk = brush;
        }

        public RegistrationMark()
        {
            Width = 12;
            Height = 12;
            IsHitTestVisible = false;
            Opacity = 0.55;
            SnapsToDevicePixels = true;
        }

        public Brush MarkBrush
        {
            get { return (Brush)GetValue(MarkBrushProperty); }
            set { SetValue(MarkBrushProperty, value); }
        }

        public static readonly DependencyProperty MarkBrushProperty =
            DependencyProperty.Register(
                "MarkBrush", typeof(Brush), typeof(RegistrationMark),
                new FrameworkPropertyMetadata(DefaultInk, FrameworkPropertyMetadataOptions.AffectsRender));

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(12, 12);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var brush = MarkBrush ?? DefaultInk;
            var pen = new Pen(brush, 0.8);
            pen.Freeze();

            double cx = ActualWidth / 2.0;
            double cy = ActualHeight / 2.0;
            double arm = 5.5;

            dc.DrawLine(pen, new Point(cx - arm, cy), new Point(cx + arm, cy));
            dc.DrawLine(pen, new Point(cx, cy - arm), new Point(cx, cy + arm));
            dc.DrawEllipse(null, pen, new Point(cx, cy), 3.0, 3.0);
            dc.DrawEllipse(brush, null, new Point(cx, cy), 0.7, 0.7);
        }
    }
}
