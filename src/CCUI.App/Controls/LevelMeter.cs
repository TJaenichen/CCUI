using System.Windows;
using System.Windows.Media;
using CCUI.Core.Metering;

namespace CCUI.App.Controls;

/// <summary>
/// A horizontal, segmented level meter for a <see cref="DecayingLevel"/>: it pops when traffic arrives and decays,
/// with a peak marker that holds and falls. It only animates while there is something to show.
/// </summary>
public sealed class LevelMeter : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(DecayingLevel), typeof(LevelMeter), new PropertyMetadata(null, OnLevelChanged));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(LevelMeter), new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(LevelMeter), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PeakBrushProperty = DependencyProperty.Register(
        nameof(PeakBrush), typeof(Brush), typeof(LevelMeter), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(int), typeof(LevelMeter), new FrameworkPropertyMetadata(20, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _animating;

    public LevelMeter()
    {
        Unloaded += (_, _) => StopAnimating();
        Loaded += (_, _) => StartAnimating();
    }

    public DecayingLevel? Level
    {
        get => (DecayingLevel?)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>Painted across the whole width and revealed up to the level, so a gradient reads like a VU meter.</summary>
    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush PeakBrush
    {
        get => (Brush)GetValue(PeakBrushProperty);
        set => SetValue(PeakBrushProperty, value);
    }

    /// <summary>Number of LED-style segments; 0 draws a continuous bar.</summary>
    public int Segments
    {
        get => (int)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 4);

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var level = Level?.Level ?? 0;
        var peak = Level?.Peak ?? 0;
        var segments = Math.Max(0, Segments);
        if (segments == 0)
        {
            dc.DrawRectangle(TrackBrush, null, new Rect(0, 0, width, height));
            DrawBar(dc, 0, level * width, height);
        }
        else
        {
            const double gap = 1.5;
            var segmentWidth = Math.Max(1, (width - (gap * (segments - 1))) / segments);
            var lit = (int)Math.Ceiling(level * segments);
            for (var i = 0; i < segments; i++)
            {
                var x = i * (segmentWidth + gap);
                var rect = new Rect(x, 0, segmentWidth, height);
                dc.DrawRectangle(TrackBrush, null, rect);
                if (i < lit)
                {
                    DrawBar(dc, x, segmentWidth, height);
                }
            }
        }

        if (peak > 0.01)
        {
            var x = Math.Min(width - 2, peak * width);
            dc.DrawRectangle(PeakBrush, null, new Rect(x, 0, 2, height));
        }

        if (Level is null || Level.IsSilent)
        {
            StopAnimating();
        }
    }

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var meter = (LevelMeter)d;
        if (e.OldValue is DecayingLevel old)
        {
            old.Changed -= meter.OnHit;
        }

        if (e.NewValue is DecayingLevel level)
        {
            level.Changed += meter.OnHit;
        }

        meter.InvalidateVisual();
    }

    private void DrawBar(DrawingContext dc, double x, double barWidth, double height)
    {
        if (barWidth <= 0)
        {
            return;
        }

        // Clip the full-width brush so each segment shows its part of the gradient.
        dc.PushClip(new RectangleGeometry(new Rect(x, 0, barWidth, height)));
        dc.DrawRectangle(BarBrush, null, new Rect(0, 0, ActualWidth, height));
        dc.Pop();
    }

    private void OnHit(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            StartAnimating();
        }
        else
        {
            Dispatcher.BeginInvoke(StartAnimating);
        }
    }

    private void StartAnimating()
    {
        if (!_animating && IsLoaded && Level is { IsSilent: false })
        {
            _animating = true;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private void StopAnimating()
    {
        if (_animating)
        {
            _animating = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    private void OnFrame(object? sender, EventArgs e) => InvalidateVisual();
}
