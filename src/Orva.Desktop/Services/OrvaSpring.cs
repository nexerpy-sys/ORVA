using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace Orva.Services
{
    /// <summary>
    /// Lightweight, 100% offline Hooke's-law spring physics solver for native WPF.
    /// Drives GPU-composited RenderTransform properties via CompositionTarget.Rendering
    /// and automatically detaches as soon as all active springs settle (0% idle CPU).
    /// Respects UiMotion.Enabled (snaps instantly when reduced motion is active).
    /// </summary>
    public static class OrvaSpring
    {
        private sealed class SpringJob
        {
            public object OwnerKey;
            public Action<double> Apply;
            public double Value;
            public double Target;
            public double Velocity;
            public double Stiffness;
            public double Damping;
            public Action OnSettled;
        }

        private static readonly List<SpringJob> _jobs = new List<SpringJob>();
        private static readonly Stopwatch _clock = new Stopwatch();
        private static bool _hooked;
        private static double _lastSeconds;

        public static void Animate(
            object ownerKey,
            double from,
            double to,
            Action<double> apply,
            double stiffness = 280.0,
            double damping = 26.0,
            Action onSettled = null)
        {
            if (apply == null) return;

            if (!UiMotion.Enabled)
            {
                Cancel(ownerKey);
                apply(to);
                if (onSettled != null) onSettled();
                return;
            }

            SpringJob existing = null;
            for (int i = 0; i < _jobs.Count; i++)
            {
                if (ReferenceEquals(_jobs[i].OwnerKey, ownerKey))
                {
                    existing = _jobs[i];
                    break;
                }
            }

            if (existing != null)
            {
                existing.Target = to;
                existing.Apply = apply;
                existing.Stiffness = stiffness;
                existing.Damping = damping;
                existing.OnSettled = onSettled;
            }
            else
            {
                var job = new SpringJob
                {
                    OwnerKey = ownerKey ?? new object(),
                    Value = from,
                    Target = to,
                    Velocity = 0.0,
                    Stiffness = stiffness,
                    Damping = damping,
                    Apply = apply,
                    OnSettled = onSettled
                };
                _jobs.Add(job);
                apply(from);
            }

            EnsureHooked();
        }

        public static void AnimateTranslateX(
            UIElement element,
            double to,
            double stiffness = 320.0,
            double damping = 30.0,
            Action onSettled = null)
        {
            if (element == null) return;
            var tt = EnsureTranslate(element);
            Animate(
                tt,
                tt.X,
                to,
                val => { tt.X = val; },
                stiffness,
                damping,
                onSettled);
        }

        public static void AnimateTranslateY(
            UIElement element,
            double to,
            double stiffness = 320.0,
            double damping = 30.0,
            Action onSettled = null)
        {
            if (element == null) return;
            var tt = EnsureTranslate(element);
            Animate(
                tt,
                tt.Y,
                to,
                val => { tt.Y = val; },
                stiffness,
                damping,
                onSettled);
        }

        public static void Cancel(object ownerKey)
        {
            if (ownerKey == null) return;
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_jobs[i].OwnerKey, ownerKey))
                {
                    _jobs.RemoveAt(i);
                }
            }
            if (_jobs.Count == 0) Detach();
        }

        private static TranslateTransform EnsureTranslate(UIElement element)
        {
            var direct = element.RenderTransform as TranslateTransform;
            if (direct != null) return direct;

            var group = element.RenderTransform as TransformGroup;
            if (group != null)
            {
                for (int i = 0; i < group.Children.Count; i++)
                {
                    var child = group.Children[i] as TranslateTransform;
                    if (child != null) return child;
                }
                var added = new TranslateTransform();
                group.Children.Add(added);
                return added;
            }

            var created = new TranslateTransform();
            element.RenderTransform = created;
            return created;
        }

        private static void EnsureHooked()
        {
            if (_hooked) return;
            _hooked = true;
            if (!_clock.IsRunning) _clock.Start();
            _lastSeconds = _clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnRendering;
        }

        private static void Detach()
        {
            if (!_hooked) return;
            _hooked = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private static void OnRendering(object sender, EventArgs e)
        {
            if (_jobs.Count == 0)
            {
                Detach();
                return;
            }

            double now = _clock.Elapsed.TotalSeconds;
            double dt = now - _lastSeconds;
            _lastSeconds = now;

            // Clamp dt to prevent instability after window drag or GC pause
            if (dt <= 0.0) dt = 1.0 / 60.0;
            if (dt > 0.05) dt = 0.05;

            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                var job = _jobs[i];
                double displacement = job.Value - job.Target;
                double accel = (-job.Stiffness * displacement) - (job.Damping * job.Velocity);
                job.Velocity += accel * dt;
                job.Value += job.Velocity * dt;

                if (Math.Abs(job.Value - job.Target) < 0.08 && Math.Abs(job.Velocity) < 0.08)
                {
                    job.Value = job.Target;
                    job.Velocity = 0.0;
                    job.Apply(job.Target);
                    var done = job.OnSettled;
                    _jobs.RemoveAt(i);
                    if (done != null) done();
                }
                else
                {
                    job.Apply(job.Value);
                }
            }

            if (_jobs.Count == 0)
            {
                Detach();
            }
        }
    }
}
