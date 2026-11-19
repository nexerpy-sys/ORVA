using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Orva.Services;
using Orva.Views;

namespace Orva.Controls
{
    /// <summary>
    /// Animated content navigation. The new view is assigned IMMEDIATELY (no async
    /// swap, so rapid clicks can never lose the final destination), then revealed
    /// with a short fade + rise on native WPF views. WebView2-hosted views bypass
    /// WPF Opacity/Translate transforms to prevent Win32 HWND airspace pop-in.
    /// </summary>
    public static class OrvaNavigation
    {
        private static readonly DependencyProperty NavStoryboardProperty =
            DependencyProperty.RegisterAttached(
                "NavStoryboard", typeof(Storyboard), typeof(OrvaNavigation),
                new PropertyMetadata(null));

        public static void SetContent(ContentControl host, object content)
        {
            if (host == null) return;

            var previous = (Storyboard)host.GetValue(NavStoryboardProperty);
            UiMotion.Stop(host, previous);

            host.Content = content;

            bool isHwndShowcase = content is HomeView || content is AboutUsView;
            if (!UiMotion.Enabled || isHwndShowcase)
            {
                host.Opacity = 1.0;
                UiMotion.SetTransformFinals(host, 0.0, 1.0, 0.0);
                return;
            }

            bool hasTransforms = UiMotion.EnsureTransforms(host);
            host.Opacity = 0.0;

            var sb = new Storyboard();
            UiMotion.Add(sb, host, "Opacity", UiMotion.Anim(0.0, 1.0, UiMotion.Normal, UiMotion.EaseOut()));
            if (hasTransforms)
            {
                UiMotion.Add(sb, host, UiMotion.TranslateYPath, UiMotion.Anim(12.0, 0.0, UiMotion.Normal, UiMotion.EaseOut()));
            }

            host.SetValue(NavStoryboardProperty, sb);
            UiMotion.Run(host, sb);
        }
    }
}
