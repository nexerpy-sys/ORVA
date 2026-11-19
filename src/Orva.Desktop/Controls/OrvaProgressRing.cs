using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Orva.Services;

namespace Orva.Controls
{
    /// <summary>
    /// 100% offline, zero-dependency Swiss clockwork vector arc ring for native WPF.
    /// Animates sweep angle smoothly via OrvaSpring (GPU-friendly, 0% idle CPU).
    /// </summary>
    public sealed class OrvaProgressRing : Grid
    {
        private static readonly Brush DefaultTrackBrush = CreateFrozenBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));

        private static Brush CreateFrozenBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static readonly DependencyProperty PercentageProperty =
            DependencyProperty.Register(
                "Percentage", typeof(double), typeof(OrvaProgressRing),
                new PropertyMetadata(0.0, OnVisualChanged));

        public static readonly DependencyProperty RingBrushProperty =
            DependencyProperty.Register(
                "RingBrush", typeof(Brush), typeof(OrvaProgressRing),
                new PropertyMetadata(Brushes.White, OnVisualChanged));

        public static readonly DependencyProperty TrackBrushProperty =
            DependencyProperty.Register(
                "TrackBrush", typeof(Brush), typeof(OrvaProgressRing),
                new PropertyMetadata(DefaultTrackBrush, OnVisualChanged));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(
                "StrokeThickness", typeof(double), typeof(OrvaProgressRing),
                new PropertyMetadata(2.5, OnVisualChanged));

        public double Percentage
        {
            get { return (double)GetValue(PercentageProperty); }
            set { SetValue(PercentageProperty, value); }
        }

        public Brush RingBrush
        {
            get { return (Brush)GetValue(RingBrushProperty); }
            set { SetValue(RingBrushProperty, value); }
        }

        public Brush TrackBrush
        {
            get { return (Brush)GetValue(TrackBrushProperty); }
            set { SetValue(TrackBrushProperty, value); }
        }

        public double StrokeThickness
        {
            get { return (double)GetValue(StrokeThicknessProperty); }
            set { SetValue(StrokeThicknessProperty, value); }
        }

        private readonly Ellipse _track;
        private readonly System.Windows.Shapes.Path _arc;
        private double _displayedPct;

        public OrvaProgressRing()
        {
            _track = new Ellipse { Fill = Brushes.Transparent };
            _arc = new System.Windows.Shapes.Path
            {
                Fill = Brushes.Transparent,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            Children.Add(_track);
            Children.Add(_arc);
            SizeChanged += (s, e) => RenderArc(_displayedPct);
            Loaded += (s, e) => AnimateTo(Percentage);
            Unloaded += (s, e) => OrvaSpring.Cancel(this);
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ring = d as OrvaProgressRing;
            if (ring == null) return;
            if (e.Property == PercentageProperty)
            {
                ring.AnimateTo(ring.Percentage);
            }
            else
            {
                ring.RenderArc(ring._displayedPct);
            }
        }

        private void AnimateTo(double target)
        {
            double clamped = Math.Max(0.0, Math.Min(100.0, target));
            OrvaSpring.Animate(
                this,
                _displayedPct,
                clamped,
                val =>
                {
                    _displayedPct = val;
                    RenderArc(val);
                },
                180.0,
                22.0);
        }

        private void RenderArc(double pct)
        {
            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;
            if (double.IsNaN(w) || double.IsNaN(h) || w < 10 || h < 10) return;

            double thickness = Math.Max(1.0, StrokeThickness);
            _track.Stroke = TrackBrush;
            _track.StrokeThickness = thickness;
            _arc.Stroke = RingBrush;
            _arc.StrokeThickness = thickness;

            if (pct <= 0.01)
            {
                _arc.Visibility = Visibility.Collapsed;
                return;
            }
            _arc.Visibility = Visibility.Visible;

            double size = Math.Min(w, h);
            double r = Math.Max(2.0, (size - thickness) * 0.5);
            double cx = w * 0.5;
            double cy = h * 0.5;

            double clamped = Math.Max(0.5, Math.Min(99.9, pct));
            double angleRad = (clamped / 100.0) * Math.PI * 2.0;

            double startX = cx;
            double startY = cy - r;
            double endX = cx + r * Math.Sin(angleRad);
            double endY = cy - r * Math.Cos(angleRad);

            var fig = new PathFigure { StartPoint = new Point(startX, startY), IsClosed = false };
            fig.Segments.Add(new ArcSegment
            {
                Point = new Point(endX, endY),
                Size = new Size(r, r),
                IsLargeArc = clamped > 50.0,
                SweepDirection = SweepDirection.Clockwise
            });

            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            _arc.Data = geo;
        }
    }
}
