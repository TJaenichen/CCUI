using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CCUI.App.Controls;

/// <summary>
/// A slowly deforming landscape behind the terminals: a dotted grid in perspective, lifted here and there by a
/// travelling swell, with translucent coloured faces where it rises. A dedicated thread rasterises frames with
/// <see cref="MeshRasterizer"/> at a reduced resolution and its own steady pace, into one of two buffers; the UI
/// thread only copies the latest finished frame into a bitmap. So a busy UI thread (terminals printing) neither
/// slows the mesh down nor is slowed by it, and the animation keeps time even when frames are dropped.
/// </summary>
public sealed class MeshBackground : FrameworkElement
{
    public static readonly DependencyProperty StrengthProperty = DependencyProperty.Register(
        nameof(Strength), typeof(double), typeof(MeshBackground), new PropertyMetadata(0.6, OnLookChanged));

    public static readonly DependencyProperty SpeedProperty = DependencyProperty.Register(
        nameof(Speed), typeof(double), typeof(MeshBackground), new PropertyMetadata(1.0, OnLookChanged));

    public static readonly DependencyProperty IsAnimatedProperty = DependencyProperty.Register(
        nameof(IsAnimated), typeof(bool), typeof(MeshBackground), new PropertyMetadata(true, OnLookChanged));

    public static readonly DependencyProperty FrameRateProperty = DependencyProperty.Register(
        nameof(FrameRate), typeof(int), typeof(MeshBackground), new PropertyMetadata(30, OnLookChanged));

    public static readonly DependencyProperty PrimaryColorProperty = DependencyProperty.Register(
        nameof(PrimaryColor), typeof(Color), typeof(MeshBackground), new PropertyMetadata(Color.FromRgb(0xFF, 0x3B, 0x5C), OnLookChanged));

    public static readonly DependencyProperty SecondaryColorProperty = DependencyProperty.Register(
        nameof(SecondaryColor), typeof(Color), typeof(MeshBackground), new PropertyMetadata(Color.FromRgb(0x3B, 0xB3, 0xFF), OnLookChanged));

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(MeshBackground), new PropertyMetadata(120, OnLookChanged));

    /// <summary>The bitmap is at most this wide; a background does not need every device pixel.</summary>
    private const int MaxBitmapWidth = 1280;

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly MeshRasterizer _rasterizer = new();
    private CancellationTokenSource? _running;
    private volatile Plan? _plan;
    private byte[] _front = [];
    private byte[] _back = [];
    private int _frontWidth;
    private int _frontHeight;
    private bool _frameReady;
    private bool _presentQueued;
    private WriteableBitmap? _bitmap;
    private Window? _window;

    public MeshBackground()
    {
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) => Replan();
        SizeChanged += (_, _) => Replan();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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

    /// <summary>Grid points across the far edge of the plane; rows follow from it.</summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>Renders one frame synchronously at the given size on the calling thread; for tests.</summary>
    internal void RenderNow(double width, double height)
    {
        if (MakePlan(width, height) is not { } plan)
        {
            return;
        }

        var pixels = new byte[plan.Width * plan.Height * 4];
        _rasterizer.Render(plan.Frame(0), pixels);
        lock (_gate)
        {
            _front = pixels;
            _frontWidth = plan.Width;
            _frontHeight = plan.Height;
            _frameReady = true;
        }

        Present();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_bitmap is not null && Strength > 0 && ActualWidth > 0 && ActualHeight > 0)
        {
            dc.DrawImage(_bitmap, new Rect(0, 0, ActualWidth, ActualHeight));
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.StateChanged += OnWindowStateChanged;
        }

        if (_running is null)
        {
            // Its own thread, above normal priority: the thread pool and the UI thread are busy exactly when the
            // terminals have the most to show, and that is when a stuttering background is most visible.
            _running = new CancellationTokenSource();
            var token = _running.Token;
            new Thread(() => RenderLoop(token)) { IsBackground = true, Name = "CCUI mesh", Priority = ThreadPriority.AboveNormal }.Start();
        }

        Replan();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.StateChanged -= OnWindowStateChanged;
            _window = null;
        }

        _running?.Cancel();
        _running = null;
        _wake.Set();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => Replan();

    /// <summary>Hands the render thread a fresh description of what to draw, or nothing when there is nothing to show.</summary>
    private void Replan()
    {
        var showing = IsVisible && IsLoaded && Strength > 0 && _window?.WindowState != WindowState.Minimized;
        _plan = showing ? MakePlan(ActualWidth, ActualHeight) : null;
        _wake.Set();
    }

    private Plan? MakePlan(double width, double height)
    {
        if (width < 20 || height < 20)
        {
            return null;
        }

        var dpi = PresentationSource.FromVisual(this) is not null ? VisualTreeHelper.GetDpi(this).DpiScaleX : 1;
        var pixelWidth = (int)Math.Clamp(width * dpi / 2, 320, MaxBitmapWidth);
        var pixelHeight = Math.Max(20, (int)Math.Round(pixelWidth * height / width));
        var primary = PrimaryColor;
        var secondary = SecondaryColor;
        var animated = IsAnimated && Speed > 0 && FrameRate > 0;
        return new Plan(
            pixelWidth,
            pixelHeight,
            Math.Clamp(Strength, 0, 1),
            Math.Clamp(Columns, 8, 240),
            (primary.R, primary.G, primary.B),
            (secondary.R, secondary.G, secondary.B),
            animated ? Math.Max(0, Speed) : 0,
            animated ? TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(FrameRate, 1, 120)) : TimeSpan.Zero);
    }

    /// <summary>The render thread: draws a frame whenever the plan says so, into the back buffer, then swaps it to the
    /// front and asks the UI thread to present it. Time advances by the wall clock, so dropped frames do not slow
    /// the motion down.</summary>
    private void RenderLoop(CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed;
        var time = 0.0;
        Plan? drawn = null;
        var next = clock.Elapsed;
        while (!token.IsCancellationRequested)
        {
            var plan = _plan;
            if (plan is null)
            {
                if (!Sleep(Timeout.InfiniteTimeSpan, token))
                {
                    return;
                }

                last = clock.Elapsed;
                continue;
            }

            var now = clock.Elapsed;
            time += (now - last).TotalSeconds * plan.Speed;
            last = now;

            // A still plan is drawn once; an animated one at its own pace.
            if (plan.Interval == TimeSpan.Zero && ReferenceEquals(plan, drawn))
            {
                if (!Sleep(Timeout.InfiniteTimeSpan, token))
                {
                    return;
                }

                continue;
            }

            if (now < next)
            {
                if (!Sleep(next - now, token))
                {
                    return;
                }

                if (!ReferenceEquals(_plan, plan))
                {
                    continue;
                }
            }

            next = clock.Elapsed + plan.Interval;
            drawn = plan;
            var size = plan.Width * plan.Height * 4;
            if (_back.Length < size)
            {
                _back = new byte[size];
            }

            try
            {
                _rasterizer.Render(plan.Frame(time), _back);
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException)
            {
                Debug.WriteLine(ex);
                continue;
            }

            lock (_gate)
            {
                (_front, _back) = (_back, _front);
                _frontWidth = plan.Width;
                _frontHeight = plan.Height;
                _frameReady = true;
                if (_presentQueued)
                {
                    continue;
                }

                _presentQueued = true;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Render, Present);
        }
    }

    /// <summary>Waits for the time given or until the UI thread changes the plan; false when the thread should end.</summary>
    private bool Sleep(TimeSpan duration, CancellationToken token)
    {
        try
        {
            _wake.Wait(duration, token);
            _wake.Reset();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>On the UI thread: copies the front buffer into the bitmap. Cheap, a few megabytes at most.</summary>
    private void Present()
    {
        lock (_gate)
        {
            _presentQueued = false;
            if (!_frameReady)
            {
                return;
            }

            _frameReady = false;
            if (_bitmap is null || _bitmap.PixelWidth != _frontWidth || _bitmap.PixelHeight != _frontHeight)
            {
                _bitmap = new WriteableBitmap(_frontWidth, _frontHeight, 96, 96, PixelFormats.Pbgra32, null);
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, _frontWidth, _frontHeight), _front, _frontWidth * 4, 0);
        }

        InvalidateVisual();
    }

    private static void OnLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((MeshBackground)d).Replan();

    /// <summary>Everything the render thread needs, captured on the UI thread; immutable, so swapping it is one write.</summary>
    private sealed record Plan(int Width, int Height, double Strength, int Columns, (byte R, byte G, byte B) Primary, (byte R, byte G, byte B) Secondary, double Speed, TimeSpan Interval)
    {
        public MeshFrame Frame(double time) => new(Width, Height, time, Strength, Columns, Primary, Secondary);
    }
}
