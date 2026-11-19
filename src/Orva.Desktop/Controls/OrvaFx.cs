using System;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Orva.Services;

namespace Orva.Controls
{
    public enum OrvaReveal
    {
        None = 0,
        Fade = 1,
        Rise = 2,
        Stamp = 3,
        Dialog = 4
    }

    /// <summary>
    /// Attached UI behaviours for ORVA. All motion is gated through UiMotion.
    /// Every behaviour degrades gracefully: if a target shape is unexpected the
    /// behaviour simply does nothing rather than throwing.
    /// </summary>
    public static class OrvaFx
    {
        // ================= Reveal (entrance / success / dialog) =================

        public static readonly DependencyProperty RevealProperty =
            DependencyProperty.RegisterAttached(
                "Reveal", typeof(OrvaReveal), typeof(OrvaFx),
                new PropertyMetadata(OrvaReveal.None, OnRevealChanged));

        public static void SetReveal(DependencyObject d, OrvaReveal value) { d.SetValue(RevealProperty, value); }
        public static OrvaReveal GetReveal(DependencyObject d) { return (OrvaReveal)d.GetValue(RevealProperty); }

        private static readonly DependencyProperty RevealStoryboardProperty =
            DependencyProperty.RegisterAttached(
                "RevealStoryboard", typeof(Storyboard), typeof(OrvaFx),
                new PropertyMetadata(null));

        private static void OnRevealChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var fe = d as FrameworkElement;
            if (fe == null) return;
            fe.Loaded -= Reveal_OnLoaded;
            fe.IsVisibleChanged -= Reveal_OnIsVisibleChanged;
            if ((OrvaReveal)e.NewValue != OrvaReveal.None)
            {
                fe.Loaded += Reveal_OnLoaded;
                fe.IsVisibleChanged += Reveal_OnIsVisibleChanged;
            }
        }

        private static void Reveal_OnLoaded(object sender, RoutedEventArgs e)
        {
            PlayReveal((FrameworkElement)sender);
        }

        private static void Reveal_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool && (bool)e.NewValue) PlayReveal((FrameworkElement)sender);
        }

        private static void PlayReveal(FrameworkElement fe)
        {
            if (fe == null) return;
            var mode = GetReveal(fe);
            if (mode == OrvaReveal.None) return;

            var previous = (Storyboard)fe.GetValue(RevealStoryboardProperty);
            UiMotion.Stop(fe, previous);

            if (!UiMotion.Enabled)
            {
                UiMotion.SetTransformFinals(fe, mode == OrvaReveal.Stamp ? -3.0 : 0.0, 1.0, 0.0);
                return;
            }

            bool hasTransforms = UiMotion.EnsureTransforms(fe);
            var sb = new Storyboard();

            switch (mode)
            {
                case OrvaReveal.Fade:
                    fe.Opacity = 0.0;
                    UiMotion.Add(sb, fe, "Opacity", UiMotion.Anim(0.0, 1.0, UiMotion.Normal, UiMotion.EaseOut()));
                    break;

                case OrvaReveal.Rise:
                    fe.Opacity = 0.0;
                    UiMotion.Add(sb, fe, "Opacity", UiMotion.Anim(0.0, 1.0, UiMotion.Normal, UiMotion.EaseOut()));
                    if (hasTransforms)
                    {
                        UiMotion.Add(sb, fe, UiMotion.TranslateYPath, UiMotion.Anim(-8.0, 0.0, UiMotion.Normal, UiMotion.EaseOut()));
                    }
                    break;

                case OrvaReveal.Stamp:
                    fe.Opacity = 0.0;
                    UiMotion.Add(sb, fe, "Opacity", UiMotion.Anim(0.0, 1.0, UiMotion.Fast, null));
                    if (hasTransforms)
                    {
                        UiMotion.Add(sb, fe, UiMotion.ScaleXPath, UiMotion.Anim(1.55, 1.0, UiMotion.Slow, UiMotion.StampEase()));
                        UiMotion.Add(sb, fe, UiMotion.ScaleYPath, UiMotion.Anim(1.55, 1.0, UiMotion.Slow, UiMotion.StampEase()));
                        UiMotion.Add(sb, fe, UiMotion.RotateAnglePath, UiMotion.Anim(-9.0, -3.0, UiMotion.Slow, UiMotion.EaseOut()));
                    }
                    break;

                case OrvaReveal.Dialog:
                    fe.Opacity = 0.0;
                    UiMotion.Add(sb, fe, "Opacity", UiMotion.Anim(0.0, 1.0, UiMotion.Normal, UiMotion.EaseOut()));
                    if (hasTransforms)
                    {
                        UiMotion.Add(sb, fe, UiMotion.ScaleXPath, UiMotion.Anim(0.985, 1.0, UiMotion.Normal, UiMotion.EaseOut()));
                        UiMotion.Add(sb, fe, UiMotion.ScaleYPath, UiMotion.Anim(0.985, 1.0, UiMotion.Normal, UiMotion.EaseOut()));
                    }
                    break;
            }

            fe.SetValue(RevealStoryboardProperty, sb);
            UiMotion.Run(fe, sb);
        }

        // ================= Tactile button press =================

        public static readonly DependencyProperty TactileProperty =
            DependencyProperty.RegisterAttached(
                "Tactile", typeof(bool), typeof(OrvaFx),
                new PropertyMetadata(false, OnTactileChanged));

        public static void SetTactile(DependencyObject d, bool value) { d.SetValue(TactileProperty, value); }
        public static bool GetTactile(DependencyObject d) { return (bool)d.GetValue(TactileProperty); }

        private static readonly DependencyProperty TactileStoryboardProperty =
            DependencyProperty.RegisterAttached(
                "TactileStoryboard", typeof(Storyboard), typeof(OrvaFx),
                new PropertyMetadata(null));

        private static void OnTactileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var fe = d as FrameworkElement;
            if (fe == null) return;
            fe.PreviewMouseLeftButtonDown -= Tactile_Down;
            fe.PreviewMouseLeftButtonUp -= Tactile_Up;
            fe.MouseLeave -= Tactile_Leave;
            if ((bool)e.NewValue)
            {
                fe.PreviewMouseLeftButtonDown += Tactile_Down;
                fe.PreviewMouseLeftButtonUp += Tactile_Up;
                fe.MouseLeave += Tactile_Leave;
            }
        }

        private static void Tactile_Down(object sender, MouseButtonEventArgs e)
        {
            var fe = (FrameworkElement)sender;
            if (fe is Control && !((Control)fe).IsEnabled) return;
            TactileTo(fe, 0.97, UiMotion.Fast);
        }

        private static void Tactile_Up(object sender, MouseButtonEventArgs e)
        {
            TactileTo((FrameworkElement)sender, 1.0, UiMotion.Normal);
        }

        private static void Tactile_Leave(object sender, MouseEventArgs e)
        {
            TactileTo((FrameworkElement)sender, 1.0, UiMotion.Normal);
        }

        private static void TactileTo(FrameworkElement fe, double scale, TimeSpan duration)
        {
            if (!UiMotion.Enabled) return;
            if (!UiMotion.EnsureTransforms(fe)) return;

            var previous = (Storyboard)fe.GetValue(TactileStoryboardProperty);
            UiMotion.Stop(fe, previous);

            var group = (TransformGroup)fe.RenderTransform;
            double current = ((ScaleTransform)group.Children[1]).ScaleX;

            var sb = new Storyboard();
            UiMotion.Add(sb, fe, UiMotion.ScaleXPath, UiMotion.Anim(current, scale, duration, UiMotion.EaseOut()));
            UiMotion.Add(sb, fe, UiMotion.ScaleYPath, UiMotion.Anim(current, scale, duration, UiMotion.EaseOut()));

            fe.SetValue(TactileStoryboardProperty, sb);
            UiMotion.Run(fe, sb);
        }

        // ================= Paper grain (decorative surfaces only) =================

        public static readonly DependencyProperty GrainProperty =
            DependencyProperty.RegisterAttached(
                "Grain", typeof(bool), typeof(OrvaFx),
                new PropertyMetadata(false, OnGrainChanged));

        public static void SetGrain(DependencyObject d, bool value) { d.SetValue(GrainProperty, value); }
        public static bool GetGrain(DependencyObject d) { return (bool)d.GetValue(GrainProperty); }

        private static readonly DependencyProperty GrainAppliedProperty =
            DependencyProperty.RegisterAttached(
                "GrainApplied", typeof(bool), typeof(OrvaFx),
                new PropertyMetadata(false));

        private static void OnGrainChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var fe = d as FrameworkElement;
            if (fe == null) return;
            fe.Loaded -= Grain_OnLoaded;
            if ((bool)e.NewValue) fe.Loaded += Grain_OnLoaded;
        }

        private static void Grain_OnLoaded(object sender, RoutedEventArgs e)
        {
            var fe = (FrameworkElement)sender;
            if ((bool)fe.GetValue(GrainAppliedProperty)) return;

            Brush baseBrush = null;
            var border = fe as Border;
            var panel = fe as Panel;
            if (border != null) baseBrush = border.Background;
            else if (panel != null) baseBrush = panel.Background;
            else return;

            var grained = CreateGrainBrush(baseBrush);
            if (border != null) border.Background = grained;
            else panel.Background = grained;
            fe.SetValue(GrainAppliedProperty, true);
        }

        public static Brush CreateGrainBrush(Brush baseBrush)
        {
            Color baseColor = Colors.Transparent;
            var solid = baseBrush as SolidColorBrush;
            if (solid != null) baseColor = solid.Color;

            var group = new DrawingGroup();
            var baseFill = new SolidColorBrush(baseColor);
            baseFill.Freeze();
            group.Children.Add(new GeometryDrawing(baseFill, null,
                new RectangleGeometry(new Rect(0, 0, 96, 96))));

            var random = new Random(1907);

            var inkSpecks = new GeometryGroup();
            var paperSpecks = new GeometryGroup();
            for (int i = 0; i < 130; i++)
            {
                double x = random.NextDouble() * 96.0;
                double y = random.NextDouble() * 96.0;
                double r = 0.3 + random.NextDouble() * 0.8;
                var dot = new EllipseGeometry(new Point(x, y), r, r);
                if (i % 2 == 0) inkSpecks.Children.Add(dot);
                else paperSpecks.Children.Add(dot);
            }

            var inkBrush = new SolidColorBrush(Color.FromArgb(9, 26, 23, 18));
            inkBrush.Freeze();
            var paperBrush = new SolidColorBrush(Color.FromArgb(7, 243, 234, 218));
            paperBrush.Freeze();
            group.Children.Add(new GeometryDrawing(inkBrush, null, inkSpecks));
            group.Children.Add(new GeometryDrawing(paperBrush, null, paperSpecks));

            var fibres = new GeometryGroup();
            for (int i = 0; i < 7; i++)
            {
                double x = random.NextDouble() * 96.0;
                double y = random.NextDouble() * 96.0;
                double len = 4.0 + random.NextDouble() * 9.0;
                double angle = random.NextDouble() * Math.PI;
                var line = new LineGeometry(
                    new Point(x, y),
                    new Point(x + Math.Cos(angle) * len, y + Math.Sin(angle) * len));
                fibres.Children.Add(line);
            }
            var fibrePen = new Pen(new SolidColorBrush(Color.FromArgb(6, 26, 23, 18)), 0.5);
            fibrePen.Freeze();
            group.Children.Add(new GeometryDrawing(null, fibrePen, fibres));

            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 96, 96),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            brush.Freeze();
            return brush;
        }

        // ================= Barcode scan pulse =================

        public static readonly DependencyProperty BarcodePulseProperty =
            DependencyProperty.RegisterAttached(
                "BarcodePulse", typeof(bool), typeof(OrvaFx),
                new PropertyMetadata(false, OnBarcodePulseChanged));

        public static void SetBarcodePulse(DependencyObject d, bool value) { d.SetValue(BarcodePulseProperty, value); }
        public static bool GetBarcodePulse(DependencyObject d) { return (bool)d.GetValue(BarcodePulseProperty); }

        private sealed class ScanState
        {
            public int Chars;
            public DateTime First;
            public DateTime Last;
        }

        private static readonly ConditionalWeakTable<TextBox, ScanState> ScanStates =
            new ConditionalWeakTable<TextBox, ScanState>();

        private static void OnBarcodePulseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tb = d as TextBox;
            if (tb == null) return;
            tb.PreviewTextInput -= Scan_TextInput;
            tb.PreviewKeyDown -= Scan_KeyDown;
            if ((bool)e.NewValue)
            {
                tb.PreviewTextInput += Scan_TextInput;
                tb.PreviewKeyDown += Scan_KeyDown;
            }
        }

        private static void Scan_TextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = (TextBox)sender;
            if (string.IsNullOrEmpty(e.Text)) return;
            var state = ScanStates.GetValue(tb, delegate { return new ScanState(); });
            var now = DateTime.UtcNow;
            if (state.Chars == 0 || (now - state.Last).TotalMilliseconds > 250)
            {
                state.Chars = 0;
                state.First = now;
            }
            state.Chars += e.Text.Length;
            state.Last = now;
        }

        private static void Scan_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var tb = (TextBox)sender;
            var state = ScanStates.GetValue(tb, delegate { return new ScanState(); });
            var now = DateTime.UtcNow;
            bool scannerLike = state.Chars >= 5
                && (state.Last - state.First).TotalMilliseconds <= 180
                && (now - state.Last).TotalMilliseconds <= 250;
            state.Chars = 0;
            if (scannerLike) PulseBorder(tb);
        }

        private sealed class BrushBox { public object LocalValue; public int Token; }
        private static readonly ConditionalWeakTable<TextBox, BrushBox> OriginalBorderBrushes = new ConditionalWeakTable<TextBox, BrushBox>();

        private static void PulseBorder(TextBox tb)
        {
            if (!UiMotion.Enabled) return;

            BrushBox savedBox;
            object originalLocal;
            if (!OriginalBorderBrushes.TryGetValue(tb, out savedBox))
            {
                originalLocal = tb.ReadLocalValue(TextBox.BorderBrushProperty);
                savedBox = new BrushBox { LocalValue = originalLocal, Token = 1 };
                OriginalBorderBrushes.Add(tb, savedBox);
            }
            else
            {
                savedBox.Token++;
                originalLocal = savedBox.LocalValue;
            }
            int myToken = savedBox.Token;
            var current = tb.BorderBrush as SolidColorBrush;
            Color baseColor = current != null ? current.Color : Color.FromRgb(26, 23, 18);
            var clone = new SolidColorBrush(baseColor);
            tb.BorderBrush = clone;

            var anim = new ColorAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new EasingColorKeyFrame(baseColor, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(255, 75, 31), KeyTime.FromTimeSpan(UiMotion.Fast)));
            anim.KeyFrames.Add(new EasingColorKeyFrame(baseColor, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)))
            {
                EasingFunction = UiMotion.EaseOut()
            });
            anim.Completed += delegate
            {
                if (myToken != savedBox.Token) return;
                OriginalBorderBrushes.Remove(tb);
                if (originalLocal == DependencyProperty.UnsetValue) tb.ClearValue(TextBox.BorderBrushProperty);
                else tb.BorderBrush = (Brush)originalLocal;
            };
            clone.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }

        // ================= Cart row flash on add =================

        public static readonly DependencyProperty FlashOnAddProperty =
            DependencyProperty.RegisterAttached(
                "FlashOnAdd", typeof(bool), typeof(OrvaFx),
                new PropertyMetadata(false, OnFlashOnAddChanged));

        public static void SetFlashOnAdd(DependencyObject d, bool value) { d.SetValue(FlashOnAddProperty, value); }
        public static bool GetFlashOnAdd(DependencyObject d) { return (bool)d.GetValue(FlashOnAddProperty); }

        private sealed class FlashWire
        {
            public NotifyCollectionChangedEventHandler Handler;
            public bool Subscribed;
        }

        private static readonly ConditionalWeakTable<ItemsControl, FlashWire> FlashWires =
            new ConditionalWeakTable<ItemsControl, FlashWire>();

        private static void OnFlashOnAddChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ic = d as ItemsControl;
            if (ic == null) return;
            ic.Loaded -= Flash_OnLoaded;
            ic.Unloaded -= Flash_OnUnloaded;
            if ((bool)e.NewValue)
            {
                ic.Loaded += Flash_OnLoaded;
                ic.Unloaded += Flash_OnUnloaded;
            }
        }

        private static void Flash_OnLoaded(object sender, RoutedEventArgs e)
        {
            var ic = (ItemsControl)sender;
            var wire = FlashWires.GetValue(ic, delegate { return new FlashWire(); });
            if (wire.Subscribed) return;
            wire.Handler = delegate(object s, NotifyCollectionChangedEventArgs args) { Flash_CollectionChanged(ic, args); };
            ((INotifyCollectionChanged)ic.Items).CollectionChanged += wire.Handler;
            wire.Subscribed = true;
        }

        private static void Flash_OnUnloaded(object sender, RoutedEventArgs e)
        {
            var ic = (ItemsControl)sender;
            var wire = FlashWires.GetValue(ic, delegate { return new FlashWire(); });
            if (!wire.Subscribed) return;
            if (wire.Handler != null)
                ((INotifyCollectionChanged)ic.Items).CollectionChanged -= wire.Handler;
            wire.Subscribed = false;
        }

        private static void Flash_CollectionChanged(ItemsControl ic, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null) return;
            foreach (var item in e.NewItems)
            {
                var captured = item;
                ic.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                {
                    var container = ic.ItemContainerGenerator.ContainerFromItem(captured) as FrameworkElement;
                    if (container != null) FlashBackground(container);
                }));
            }
        }

        private static void FlashBackground(FrameworkElement container)
        {
            if (!UiMotion.Enabled) return;

            DependencyProperty bgProperty = null;
            Brush current = null;
            var control = container as Control;
            var border = container as Border;
            var panel = container as Panel;
            if (control != null) { bgProperty = Control.BackgroundProperty; current = control.Background; }
            else if (border != null) { bgProperty = Border.BackgroundProperty; current = border.Background; }
            else if (panel != null) { bgProperty = Panel.BackgroundProperty; current = panel.Background; }
            else return;

            object originalLocal = container.ReadLocalValue(bgProperty);
            var solid = current as SolidColorBrush;
            Color baseColor = solid != null ? solid.Color : Colors.Transparent;
            var clone = new SolidColorBrush(Color.FromRgb(255, 227, 217));
            container.SetValue(bgProperty, clone);

            var anim = new ColorAnimation(baseColor, TimeSpan.FromMilliseconds(500))
            {
                EasingFunction = UiMotion.EaseOut()
            };
            anim.Completed += delegate
            {
                if (originalLocal == DependencyProperty.UnsetValue) container.ClearValue(bgProperty);
                else container.SetValue(bgProperty, originalLocal);
            };
            clone.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }

        // ================= Search empty state =================

        public static readonly DependencyProperty EmptyStateForProperty =
            DependencyProperty.RegisterAttached(
                "EmptyStateFor", typeof(ItemsControl), typeof(OrvaFx),
                new PropertyMetadata(null, OnEmptyStateForChanged));

        public static void SetEmptyStateFor(DependencyObject d, ItemsControl value) { d.SetValue(EmptyStateForProperty, value); }
        public static ItemsControl GetEmptyStateFor(DependencyObject d) { return (ItemsControl)d.GetValue(EmptyStateForProperty); }

        public static readonly DependencyProperty EmptyStateSearchBoxProperty =
            DependencyProperty.RegisterAttached(
                "EmptyStateSearchBox", typeof(TextBox), typeof(OrvaFx),
                new PropertyMetadata(null, OnEmptyStateSearchBoxChanged));

        public static void SetEmptyStateSearchBox(DependencyObject d, TextBox value) { d.SetValue(EmptyStateSearchBoxProperty, value); }
        public static TextBox GetEmptyStateSearchBox(DependencyObject d) { return (TextBox)d.GetValue(EmptyStateSearchBoxProperty); }

        private sealed class EmptyStateWire
        {
            public ItemsControl Target;
            public TextBox Box;
            public NotifyCollectionChangedEventHandler ItemsHandler;
            public TextChangedEventHandler TextHandler;
        }

        private static readonly ConditionalWeakTable<UIElement, EmptyStateWire> EmptyWires =
            new ConditionalWeakTable<UIElement, EmptyStateWire>();

        private static void OnEmptyStateForChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var element = d as UIElement;
            if (element == null) return;
            var wire = EmptyWires.GetValue(element, delegate { return new EmptyStateWire(); });
            if (wire.Target != null && wire.ItemsHandler != null)
                ((INotifyCollectionChanged)wire.Target.Items).CollectionChanged -= wire.ItemsHandler;
            wire.Target = e.NewValue as ItemsControl;
            if (wire.Target != null)
            {
                wire.ItemsHandler = delegate { UpdateEmptyState(element); };
                ((INotifyCollectionChanged)wire.Target.Items).CollectionChanged += wire.ItemsHandler;
            }
            UpdateEmptyState(element);
        }

        private static void OnEmptyStateSearchBoxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var element = d as UIElement;
            if (element == null) return;
            var wire = EmptyWires.GetValue(element, delegate { return new EmptyStateWire(); });
            if (wire.Box != null && wire.TextHandler != null)
                wire.Box.TextChanged -= wire.TextHandler;
            wire.Box = e.NewValue as TextBox;
            if (wire.Box != null)
            {
                wire.TextHandler = delegate { UpdateEmptyState(element); };
                wire.Box.TextChanged += wire.TextHandler;
            }
            UpdateEmptyState(element);
        }

        private static void UpdateEmptyState(UIElement element)
        {
            var wire = EmptyWires.GetValue(element, delegate { return new EmptyStateWire(); });
            if (wire.Target == null) { element.Visibility = Visibility.Collapsed; return; }
            bool empty = wire.Target.Items.Count == 0;
            bool textEntered = wire.Box == null || wire.Box.Text.Trim().Length > 0;
            element.Visibility = empty && textEntered ? Visibility.Visible : Visibility.Collapsed;
        }

        // ================= Optional urgent expiry pulse =================

        public static readonly DependencyProperty UrgentPulseProperty =
            DependencyProperty.RegisterAttached(
                "UrgentPulse", typeof(bool), typeof(OrvaFx),
                new PropertyMetadata(false, OnUrgentPulseChanged));

        public static void SetUrgentPulse(DependencyObject d, bool value) { d.SetValue(UrgentPulseProperty, value); }
        public static bool GetUrgentPulse(DependencyObject d) { return (bool)d.GetValue(UrgentPulseProperty); }

        private static void OnUrgentPulseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var fe = d as FrameworkElement;
            if (fe == null) return;
            fe.IsVisibleChanged -= Urgent_OnVisible;
            if ((bool)e.NewValue) fe.IsVisibleChanged += Urgent_OnVisible;
        }

        private static void Urgent_OnVisible(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(e.NewValue is bool) || !(bool)e.NewValue) return;
            var fe = (FrameworkElement)sender;
            if (!UiMotion.Enabled) return;

            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.55, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.55, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200))));
            fe.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        // ================= Spotlight border reveal (100% offline cursor-tracking border sheen) =================

        public static readonly DependencyProperty SpotlightProperty =
            DependencyProperty.RegisterAttached(
                "Spotlight", typeof(bool), typeof(OrvaFx),
                new PropertyMetadata(false, OnSpotlightChanged));

        public static void SetSpotlight(DependencyObject d, bool value) { d.SetValue(SpotlightProperty, value); }
        public static bool GetSpotlight(DependencyObject d) { return (bool)d.GetValue(SpotlightProperty); }

        private static readonly DependencyProperty SpotlightOriginalBrushProperty =
            DependencyProperty.RegisterAttached(
                "SpotlightOriginalBrush", typeof(Brush), typeof(OrvaFx),
                new PropertyMetadata(null));

        private static readonly DependencyProperty SpotlightCachedBrushProperty =
            DependencyProperty.RegisterAttached(
                "SpotlightCachedBrush", typeof(RadialGradientBrush), typeof(OrvaFx),
                new PropertyMetadata(null));

        private static void OnSpotlightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var border = d as Border;
            if (border == null) return;
            border.MouseEnter -= Spotlight_MouseEnter;
            border.MouseMove -= Spotlight_MouseMove;
            border.MouseLeave -= Spotlight_MouseLeave;
            border.Unloaded -= Spotlight_Unloaded;
            if (e.NewValue is bool && (bool)e.NewValue)
            {
                border.MouseEnter += Spotlight_MouseEnter;
                border.MouseMove += Spotlight_MouseMove;
                border.MouseLeave += Spotlight_MouseLeave;
                border.Unloaded += Spotlight_Unloaded;
            }
        }

        private static void Spotlight_MouseEnter(object sender, MouseEventArgs e)
        {
            var border = sender as Border;
            if (border == null || !UiMotion.Enabled) return;
            if (border.GetValue(SpotlightOriginalBrushProperty) == null)
            {
                border.SetValue(SpotlightOriginalBrushProperty, border.BorderBrush);
            }
            UpdateSpotlightBrush(border, e.GetPosition(border));
        }

        private static void Spotlight_MouseMove(object sender, MouseEventArgs e)
        {
            var border = sender as Border;
            if (border == null || !UiMotion.Enabled) return;
            UpdateSpotlightBrush(border, e.GetPosition(border));
        }

        private static void Spotlight_MouseLeave(object sender, MouseEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            var orig = border.GetValue(SpotlightOriginalBrushProperty) as Brush;
            if (orig != null) border.BorderBrush = orig;
        }

        private static void Spotlight_Unloaded(object sender, RoutedEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            var orig = border.GetValue(SpotlightOriginalBrushProperty) as Brush;
            if (orig != null) border.BorderBrush = orig;
            border.ClearValue(SpotlightCachedBrushProperty);
        }

        private static void UpdateSpotlightBrush(Border border, Point pos)
        {
            double w = border.ActualWidth;
            double h = border.ActualHeight;
            if (w <= 1.0 || h <= 1.0) return;

            double rx = Math.Max(0.0, Math.Min(1.0, pos.X / w));
            double ry = Math.Max(0.0, Math.Min(1.0, pos.Y / h));
            var center = new Point(rx, ry);

            var brush = border.GetValue(SpotlightCachedBrushProperty) as RadialGradientBrush;
            if (brush == null)
            {
                brush = new RadialGradientBrush { RadiusX = 0.55, RadiusY = 0.55 };
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x4B, 0x1F), 0.0));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x17, 0x2C, 0x30), 0.48));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xD5, 0xC9, 0xB4), 1.0));
                border.SetValue(SpotlightCachedBrushProperty, brush);
            }

            brush.GradientOrigin = center;
            brush.Center = center;
            if (!ReferenceEquals(border.BorderBrush, brush)) border.BorderBrush = brush;
        }

        // ================= ValuePulse (100% offline GPU micro-settle on numeric TextBlock changes) =================

        public static readonly DependencyProperty ValuePulseProperty =
            DependencyProperty.RegisterAttached(
                "ValuePulse", typeof(bool), typeof(OrvaFx),
                new PropertyMetadata(false, OnValuePulseChanged));

        public static void SetValuePulse(DependencyObject d, bool value) { d.SetValue(ValuePulseProperty, value); }
        public static bool GetValuePulse(DependencyObject d) { return (bool)d.GetValue(ValuePulseProperty); }

        private static void OnValuePulseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tb = d as TextBlock;
            if (tb == null) return;
            var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            if (dpd == null) return;
            dpd.RemoveValueChanged(tb, ValuePulse_TextChanged);
            tb.Unloaded -= ValuePulse_Unloaded;
            if (e.NewValue is bool && (bool)e.NewValue)
            {
                dpd.AddValueChanged(tb, ValuePulse_TextChanged);
                tb.Unloaded += ValuePulse_Unloaded;
            }
        }

        private static void ValuePulse_Unloaded(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBlock;
            if (tb == null) return;
            var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            if (dpd != null) dpd.RemoveValueChanged(tb, ValuePulse_TextChanged);
        }

        private static void ValuePulse_TextChanged(object sender, EventArgs e)
        {
            var tb = sender as TextBlock;
            if (tb == null || !UiMotion.Enabled || !tb.IsLoaded) return;
            var tt = tb.RenderTransform as TranslateTransform;
            if (tt == null)
            {
                tt = new TranslateTransform();
                tb.RenderTransform = tt;
            }
            tt.Y = 4.5;
            OrvaSpring.Animate(tt, 4.5, 0.0, v => { tt.Y = v; }, 420.0, 28.0);
        }
    }
}
