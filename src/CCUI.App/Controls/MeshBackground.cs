using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace CCUI.App.Controls;

/// <summary>
/// A slowly deforming landscape behind the terminals: a dotted grid in perspective, lifted here and there by a
/// travelling swell, with translucent coloured faces where it rises. Drawn with 2D primitives from a small height
/// field, so one instance behind the whole window costs little, and it composes with the backdrop and the panes'
/// transparency. Time only advances while the element is visible.
/// </summary>
public sealed class MeshBackground : FrameworkElement
{
    public static readonly DependencyProperty StrengthProperty = DependencyProperty.Register(
        nameof(Strength), typeof(double), typeof(MeshBackground), new FrameworkPropertyMetadata(0.6, FrameworkPropertyMetadataOptions.AffectsRender, OnLookChanged));

    public static readonly DependencyProperty SpeedProperty = DependencyProperty.Register(
        nameof(Speed), typeof(double), typeof(MeshBackground), new PropertyMetadata(1.0, OnPaceChanged));

    public static readonly DependencyProperty IsAnimatedProperty = DependencyProperty.Register(
        nameof(IsAnimated), typeof(bool), typeof(MeshBackground), new PropertyMetadata(true, OnPaceChanged));

    public static readonly DependencyProperty FrameRateProperty = DependencyProperty.Register(
        nameof(FrameRate), typeof(int), typeof(MeshBackground), new PropertyMetadata(30, OnPaceChanged));

    public static readonly DependencyProperty PrimaryColorProperty = DependencyProperty.Register(
        nameof(PrimaryColor), typeof(Color), typeof(MeshBackground), new FrameworkPropertyMetadata(Color.FromRgb(0xFF, 0x3B, 0x5C), FrameworkPropertyMetadataOptions.AffectsRender, OnLookChanged));

    public static readonly DependencyProperty SecondaryColorProperty = DependencyProperty.Register(
        nameof(SecondaryColor), typeof(Color), typeof(MeshBackground), new FrameworkPropertyMetadata(Color.FromRgb(0x3B, 0xB3, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender, OnLookChanged));

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(MeshBackground), new FrameworkPropertyMetadata(120, FrameworkPropertyMetadataOptions.AffectsRender));

    // A low camera over a plane that reaches the horizon: the far row still spans the window, the near row overshoots it.
    private const double Near = 1.6;
    private const double Far = 6.5;
    private const double CameraHeight = 1.1;
    private const double Focal = 0.9;
    private const double Amplitude = 1.25;

    private static readonly Color Ink = Color.FromRgb(0xE6, 0xEA, 0xF2);

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<int, SolidColorBrush> _faceBrushes = [];
    private readonly Dictionary<int, SolidColorBrush> _inkBrushes = [];
    private TimeSpan _lastTick;
    private double _time;

    public MeshBackground()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) => UpdatePace();
        Loaded += (_, _) => UpdatePace();
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>Overall visibility, 0 to 1.</summary>
    public double Strength
    {
        get => (double)GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    /// <summary>Time multiplier: 1 is a slow drift, 0.05 barely moves, 0 freezes.</summary>
    public double Speed
    {
        get => (double)GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    public bool IsAnimated
    {
        get => (bool)GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    public int FrameRate
    {
        get => (int)GetValue(FrameRateProperty);
        set => SetValue(FrameRateProperty, value);
    }

    public Color PrimaryColor
    {
        get => (Color)GetValue(PrimaryColorProperty);
        set => SetValue(PrimaryColorProperty, value);
    }

    public Color SecondaryColor
    {
        get => (Color)GetValue(SecondaryColorProperty);
        set => SetValue(SecondaryColorProperty, value);
    }

    /// <summary>Grid points across the window; rows follow from it.</summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>The surface height at a point, in world units: a wandering swell shaping a few overlapping waves,
    /// over a faint ripple everywhere.</summary>
    internal static double Surface(double x, double z, double t)
    {
        var swellX = (1.6 * Math.Sin(0.11 * t)) + (0.7 * Math.Sin((0.067 * t) + 1.3));
        var swellZ = 3.9 + (1.6 * Math.Sin((0.083 * t) + 2.1));
        var dx = (x - swellX) / 2.8;
        var dz = (z - swellZ) / 2.4;
        var envelope = Math.Exp(-((dx * dx) + (dz * dz)));
        var waves = (0.55 * Math.Sin((1.3 * x) + (0.9 * t)) * Math.Cos((0.8 * z) - (0.7 * t)))
            + (0.35 * Math.Sin((2.1 * x) - (1.4 * z) + (1.3 * t)))
            + (0.2 * Math.Sin((3.1 * z) + (1.7 * t) + x));
        var ripple = 0.035 * Math.Sin((2.0 * x) + (0.5 * t)) * Math.Cos((1.5 * z) + (0.3 * t));
        return (Amplitude * envelope * waves) + ripple;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var strength = Math.Clamp(Strength, 0, 1);
        if (width < 20 || height < 20 || strength <= 0)
        {
            return;
        }

        var columns = Math.Clamp(Columns, 8, 240);
        var rows = Math.Max(6, (int)Math.Round(columns * 0.5));
        var horizon = height * 0.40;
        var centre = width / 2;
        var scale = width * Focal;
        // Wide enough that the farthest row still reaches both edges; nearer rows overshoot and are clipped.
        var halfWidth = centre / (scale / Far) * 1.1;

        var px = new double[rows + 1, columns + 1];
        var py = new double[rows + 1, columns + 1];
        var elevation = new double[rows + 1, columns + 1];
        var fade = new double[rows + 1];
        var t = _time;
        for (var r = 0; r <= rows; r++)
        {
            var depth = (double)r / rows;
            var z = Near + ((Far - Near) * depth);
            var s = scale / z;
            fade[r] = Math.Pow(1 - depth, 1.1);
            for (var c = 0; c <= columns; c++)
            {
                var x = (((double)c / columns * 2) - 1) * halfWidth;
                var y = Surface(x, z, t);
                elevation[r, c] = y;
                px[r, c] = centre + (x * s);
                py[r, c] = horizon + ((CameraHeight - y) * s);
            }
        }

        DrawFaces(dc, px, py, elevation, fade, rows, columns, strength, t);
        DrawLines(dc, px, py, fade, rows, columns, strength);
        DrawDots(dc, px, py, fade, rows, columns, strength, width);
    }

    private void DrawFaces(DrawingContext dc, double[,] px, double[,] py, double[,] elevation, double[] fade, int rows, int columns, double strength, double t)
    {
        const double threshold = 0.07;
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var lift = Math.Max(Math.Max(Math.Abs(elevation[r, c]), Math.Abs(elevation[r, c + 1])), Math.Max(Math.Abs(elevation[r + 1, c]), Math.Abs(elevation[r + 1, c + 1])));
                if (lift < threshold)
                {
                    continue;
                }

                var alpha = strength * Math.Sqrt(fade[r]) * Math.Clamp((lift - threshold) / 0.45, 0, 1) * 0.8;
                var mix = 0.5 + (0.5 * Math.Sin((1.1 * c / columns * 6) + (0.6 * r / rows * 6) + (0.4 * t)));
                var brush = FaceBrush(mix, alpha);
                var edge = InkBrush(alpha * 0.6);
                var pen = new Pen(edge, 0.8);
                pen.Freeze();

                dc.DrawGeometry(brush, pen, Triangle(px[r, c], py[r, c], px[r, c + 1], py[r, c + 1], px[r + 1, c], py[r + 1, c]));
                dc.DrawGeometry(brush, pen, Triangle(px[r, c + 1], py[r, c + 1], px[r + 1, c + 1], py[r + 1, c + 1], px[r + 1, c], py[r + 1, c]));
            }
        }
    }

    private void DrawLines(DrawingContext dc, double[,] px, double[,] py, double[] fade, int rows, int columns, double strength)
    {
        // Rows get their own alpha for the depth fade; columns span every depth and take a middle value.
        for (var r = 0; r <= rows; r++)
        {
            var pen = new Pen(InkBrush(strength * fade[r] * 0.12), 0.7);
            pen.Freeze();
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(px[r, 0], py[r, 0]), false, false);
                for (var c = 1; c <= columns; c++)
                {
                    ctx.LineTo(new Point(px[r, c], py[r, c]), true, false);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        var columnPen = new Pen(InkBrush(strength * 0.08), 0.7);
        columnPen.Freeze();
        var columnsGeometry = new StreamGeometry();
        using (var ctx = columnsGeometry.Open())
        {
            for (var c = 0; c <= columns; c++)
            {
                ctx.BeginFigure(new Point(px[0, c], py[0, c]), false, false);
                for (var r = 1; r <= rows; r++)
                {
                    ctx.LineTo(new Point(px[r, c], py[r, c]), true, false);
                }
            }
        }

        columnsGeometry.Freeze();
        dc.DrawGeometry(null, columnPen, columnsGeometry);
    }

    private void DrawDots(DrawingContext dc, double[,] px, double[,] py, double[] fade, int rows, int columns, double strength, double width)
    {
        // Dots are a dashed stroke with zero-length dashes and round caps along each row: one draw per row.
        for (var r = 0; r <= rows; r++)
        {
            var spacing = (px[r, columns] - px[r, 0]) / columns;
            var size = Math.Clamp(spacing * 0.1, 1.2, 3.2);
            if (spacing < 2)
            {
                continue;
            }

            var pen = new Pen(InkBrush(strength * fade[r] * 0.8), size)
            {
                DashCap = PenLineCap.Round,
                DashStyle = new DashStyle([0, spacing / size], 0),
            };
            pen.Freeze();
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(px[r, 0], py[r, 0]), false, false);
                for (var c = 1; c <= columns; c++)
                {
                    ctx.LineTo(new Point(px[r, c], py[r, c]), true, false);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    private static StreamGeometry Triangle(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x1, y1), true, true);
            ctx.LineTo(new Point(x2, y2), false, false);
            ctx.LineTo(new Point(x3, y3), false, false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Brushes are quantised and cached: a few hundred faces per frame would otherwise allocate as many.</summary>
    private SolidColorBrush FaceBrush(double mix, double alpha)
    {
        var mixStep = (int)Math.Round(Math.Clamp(mix, 0, 1) * 15);
        var alphaStep = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 40);
        var key = (mixStep << 8) | alphaStep;
        if (!_faceBrushes.TryGetValue(key, out var brush))
        {
            var a = PrimaryColor;
            var b = SecondaryColor;
            var m = mixStep / 15.0;
            var colour = Color.FromArgb(
                (byte)Math.Round(alphaStep / 40.0 * 255),
                (byte)Math.Round(a.R + ((b.R - a.R) * m)),
                (byte)Math.Round(a.G + ((b.G - a.G) * m)),
                (byte)Math.Round(a.B + ((b.B - a.B) * m)));
            brush = new SolidColorBrush(colour);
            brush.Freeze();
            _faceBrushes[key] = brush;
        }

        return brush;
    }

    private SolidColorBrush InkBrush(double alpha)
    {
        var step = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 60);
        if (!_inkBrushes.TryGetValue(step, out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(step / 60.0 * 255), Ink.R, Ink.G, Ink.B));
            brush.Freeze();
            _inkBrushes[step] = brush;
        }

        return brush;
    }

    private void Tick()
    {
        var now = _clock.Elapsed;
        if (Window.GetWindow(this) is { WindowState: WindowState.Minimized })
        {
            _lastTick = now;
            return;
        }

        _time += (now - _lastTick).TotalSeconds * Math.Max(0, Speed);
        _lastTick = now;
        InvalidateVisual();
    }

    private void UpdatePace()
    {
        var running = IsVisible && IsLoaded && IsAnimated && Speed > 0 && FrameRate > 0;
        if (!running)
        {
            _timer.Stop();
            InvalidateVisual();
            return;
        }

        _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(FrameRate, 1, 120));
        _lastTick = _clock.Elapsed;
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private static void OnPaceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((MeshBackground)d).UpdatePace();

    private static void OnLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var mesh = (MeshBackground)d;
        mesh._faceBrushes.Clear();
        mesh._inkBrushes.Clear();
    }
}
