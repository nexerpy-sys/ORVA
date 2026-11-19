using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Orva.Services
{
    /// <summary>
    /// Central motion gate for ORVA. All UI animation must respect the operating
    /// system's "Show animations" preference (reduced motion). When motion is
    /// disabled, helpers apply final visual states instantly and return.
    /// UI thread only.
    /// </summary>
    public static class UiMotion
    {
        private static bool _enabled = SystemParameters.ClientAreaAnimation;

        static UiMotion()
        {
            SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
        }

        private static void SystemParameters_StaticPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, "ClientAreaAnimation", StringComparison.Ordinal))
            {
                _enabled = SystemParameters.ClientAreaAnimation;
            }
        }

        public static bool Enabled { get { return _enabled; } }

        public static TimeSpan Fast { get { return TimeSpan.FromMilliseconds(90); } }
        public static TimeSpan Normal { get { return TimeSpan.FromMilliseconds(180); } }
        public static TimeSpan Slow { get { return TimeSpan.FromMilliseconds(260); } }

        public static IEasingFunction EaseOut()
        {
            return new CubicEase { EasingMode = EasingMode.EaseOut };
        }

        public static IEasingFunction StampEase()
        {
            return new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        }

        /// <summary>Begins a storyboard with replace handoff so repeated triggers never stack.</summary>
        public static void Run(FrameworkElement target, Storyboard storyboard)
        {
            if (target == null || storyboard == null || !_enabled) return;
            storyboard.Begin(target, HandoffBehavior.SnapshotAndReplace, true);
        }

        public static void Stop(FrameworkElement target, Storyboard storyboard)
        {
            if (target == null || storyboard == null) return;
            try { storyboard.Stop(target); }
            catch (InvalidOperationException) { /* never started on this target */ }
        }

        /// <summary>One-time startup fade for the main window. Never delays functionality.</summary>
        public static void FadeWindow(Window window)
        {
            if (window == null) return;
            if (!_enabled) { window.Opacity = 1.0; return; }

            window.Opacity = 0.0;
            var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = EaseOut()
            };
            window.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private static readonly DependencyProperty TransformsInstalledProperty =
            DependencyProperty.RegisterAttached(
                "UiMotionTransformsInstalled", typeof(bool), typeof(UiMotion),
                new PropertyMetadata(false));

        public static bool EnsureTransforms(FrameworkElement element)
        {
            if (element == null) return false;
            if ((bool)element.GetValue(TransformsInstalledProperty)) return true;

            var current = element.RenderTransform;
            if (current != null && !current.Value.IsIdentity) return false;

            var group = new TransformGroup();
            group.Children.Add(new RotateTransform(0));
            group.Children.Add(new ScaleTransform(1, 1));
            group.Children.Add(new TranslateTransform(0, 0));
            element.RenderTransform = group;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.SetValue(TransformsInstalledProperty, true);
            return true;
        }

        public const string RotateAnglePath =
            "(UIElement.RenderTransform).(TransformGroup.Children)[0].(RotateTransform.Angle)";
        public const string ScaleXPath =
            "(UIElement.RenderTransform).(TransformGroup.Children)[1].(ScaleTransform.ScaleX)";
        public const string ScaleYPath =
            "(UIElement.RenderTransform).(TransformGroup.Children)[1].(ScaleTransform.ScaleY)";
        public const string TranslateYPath =
            "(UIElement.RenderTransform).(TransformGroup.Children)[2].(TranslateTransform.Y)";

        public static void SetTransformFinals(FrameworkElement element, double angle, double scale, double translateY)
        {
            if (element == null) return;
            if (!EnsureTransforms(element)) { element.Opacity = 1.0; return; }
            var group = (TransformGroup)element.RenderTransform;
            ((RotateTransform)group.Children[0]).Angle = angle;
            ((ScaleTransform)group.Children[1]).ScaleX = scale;
            ((ScaleTransform)group.Children[1]).ScaleY = scale;
            ((TranslateTransform)group.Children[2]).Y = translateY;
            element.Opacity = 1.0;
        }

        public static DoubleAnimation Anim(double from, double to, TimeSpan duration, IEasingFunction ease)
        {
            var a = new DoubleAnimation(from, to, duration);
            if (ease != null) a.EasingFunction = ease;
            return a;
        }

        public static void Add(Storyboard sb, FrameworkElement target, string path, DoubleAnimation animation)
        {
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath(path));
            sb.Children.Add(animation);
        }
    }
}
