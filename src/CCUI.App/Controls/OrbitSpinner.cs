using System.Numerics;
using System.Windows;
using System.Windows.Media;

namespace CCUI.App.Controls;

/// <summary>
/// A "working" indicator: three dots orbiting on a plane that keeps tumbling about all three axes, like a gyroscope
/// ride. Dots nearer the viewer are drawn bigger and brighter. It only animates while visible and
/// <see cref="IsSpinning"/>; otherwise it draws a still frame.
/// </summary>
public sealed class OrbitSpinner : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(OrbitSpinner), new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsSpinningProperty = DependencyProperty.Register(
        nameof(IsSpinning), typeof(bool), typeof(OrbitSpinner), new PropertyMetadata(true, (d, _) => ((OrbitSpinner)d).UpdateAnimation()));

    private const int Dots = 3;

    // Orbit speed in turns per second, and how much nearer/farther dots grow/shrink and brighten/fade.
    private const double OrbitTurnsPerSecond = 0.9;
    private const double DepthScale = 0.4;
    private const double MinimumOpacity = 0.45;

    private readonly System.Diagnostics.Stopwatch _clock = new();
    private readonly double _phase = Random.Shared.NextDouble() * 100;
    private bool _animating;

    public OrbitSpinner()
    {
        IsVisibleChanged += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => StopAnimating();
        Loaded += (_, _) => UpdateAnimation();
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public bool IsSpinning
    {
        get => (bool)GetValue(IsSpinningProperty);
        set => SetValue(IsSpinningProperty, value);
    }

    /// <summary>
    /// Where each dot is at <paramref name="seconds"/>: X and Y in the unit disc as seen head-on, Z the depth
    /// (+1 nearest the viewer). Public for tests.
    /// </summary>
    public static Vector3[] Positions(double seconds)
    {
        // The plane's orientation: each axis turns at its own, slowly wandering rate. The rates share no common
        // period, so the tumble never visibly repeats.
        var t = seconds;
        var yaw = (0.70 * t) + (0.9 * Math.Sin(0.31 * t));
        var pitch = (0.45 * t) + (1.1 * Math.Sin(0.23 * t + 1.7));
        var roll = (0.25 * t) + (0.8 * Math.Sin(0.41 * t + 0.4));
        var orientation = Quaternion.CreateFromYawPitchRoll((float)yaw, (float)pitch, (float)roll);

        var orbit = 2 * Math.PI * OrbitTurnsPerSecond * t;
        var positions = new Vector3[Dots];
        for (var i = 0; i < Dots; i++)
        {
            var angle = orbit + (2 * Math.PI * i / Dots);
            positions[i] = Vector3.Transform(new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0), orientation);
        }

        return positions;
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 14 : Math.Min(14, availableSize.Width),
        double.IsInfinity(availableSize.Height) ? 14 : Math.Min(14, availableSize.Height));

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var dot = size * 0.13;
        var orbitRadius = (size / 2) - (dot * (1 + DepthScale));
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var seconds = _phase + _clock.Elapsed.TotalSeconds;

        // Far dots first, so near ones are drawn over them.
        foreach (var p in Positions(seconds).OrderBy(p => p.Z))
        {
            var near = (p.Z + 1) / 2;
            drawingContext.PushOpacity(MinimumOpacity + ((1 - MinimumOpacity) * near));
            var radius = dot * (1 + (DepthScale * p.Z));
            drawingContext.DrawEllipse(Fill, null, new Point(center.X + (p.X * orbitRadius), center.Y + (p.Y * orbitRadius)), radius, radius);
            drawingContext.Pop();
        }
    }

    private void UpdateAnimation()
    {
        if (IsSpinning && IsVisible && IsLoaded)
        {
            StartAnimating();
        }
        else
        {
            StopAnimating();
        }
    }

    private void StartAnimating()
    {
        if (_animating)
        {
            return;
        }

        _animating = true;
        _clock.Start();
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimating()
    {
        if (!_animating)
        {
            return;
        }

        _animating = false;
        _clock.Stop();
        CompositionTarget.Rendering -= OnRendering;
        InvalidateVisual();
    }

    private void OnRendering(object? sender, EventArgs e) => InvalidateVisual();
}
