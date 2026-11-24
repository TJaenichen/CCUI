using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CCUI.App.Controls;

/// <summary>
/// A slowly deforming landscape behind the terminals: a dotted grid in perspective, lifted here and there by a
/// travelling swell, with translucent coloured faces where it rises. Each frame is rasterised by
/// <see cref="MeshRasterizer"/> on a worker thread at a reduced resolution and shown as one bitmap, so the UI thread
/// pays for a single image per frame. Time only advances while the element is visible.
/// </summary>
public sealed class MeshBackground : FrameworkElement
{
    public static readonly DependencyProperty StrengthProperty = DependencyProperty.Register(
        nameof(Strength), typeof(double), typeof(MeshBackground), new PropertyMetadata(0.6, OnLookChanged));

    public static readonly DependencyProperty SpeedProperty = DependencyProperty.Register(
        nameof(Speed), typeof(double), typeof(MeshBackground), new PropertyMetadata(1.0, OnPaceChanged));

    public static readonly DependencyProperty IsAnimatedProperty = DependencyProperty.Register(
        nameof(IsAnimated), typeof(bool), typeof(MeshBackground), new PropertyMetadata(true, OnPaceChanged));

    public static readonly DependencyProperty FrameRateProperty = DependencyProperty.Register(
        nameof(FrameRate), typeof(int), typeof(MeshBackground), new PropertyMetadata(30, OnPaceChanged));

    public static readonly DependencyProperty PrimaryColorProperty = DependencyProperty.Register(
        nameof(PrimaryColor), typeof(Color), typeof(MeshBackground), new PropertyMetadata(Color.FromRgb(0xFF, 0x3B, 0x5C), OnLookChanged));

    public static readonly DependencyProperty SecondaryColorProperty = DependencyProperty.Register(
        nameof(SecondaryColor), typeof(Color), typeof(MeshBackground), new PropertyMetadata(Color.FromRgb(0x3B, 0xB3, 0xFF), OnLookChanged));

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(MeshBackground), new PropertyMetadata(120, OnLookChanged));

    /// <summary>The bitmap is at most this wide; a background does not need every device pixel.</summary>
    private const int MaxBitmapWidth = 1280;

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly MeshRasterizer _rasterizer = new();
    private WriteableBitmap? _bitmap;
    private TimeSpan _lastTick;
    private double _time;
    private bool _rendering;
    private bool _frameWanted;

    public MeshBackground()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) => UpdatePace();
        Loaded += (_, _) => UpdatePace();
        Unloaded += (_, _) => _timer.Stop();
        SizeChanged += (_, _) => RequestFrame();
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

    /// <summary>Renders one frame synchronously at the given size; for tests and for a static mesh.</summary>
    internal void RenderNow(double width, double height)
    {
        if (FrameFor(width, height) is { } frame)
        {
            _rasterizer.Render(frame);
            Present();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_bitmap is not null && Strength > 0 && ActualWidth > 0 && ActualHeight > 0)
        {
            dc.DrawImage(_bitmap, new Rect(0, 0, ActualWidth, ActualHeight));
        }
    }

    private MeshFrame? FrameFor(double width, double height)
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
        return new MeshFrame(pixelWidth, pixelHeight, _time, Strength, Columns, (primary.R, primary.G, primary.B), (secondary.R, secondary.G, secondary.B));
    }

    /// <summary>Copies the rasterised frame into the bitmap and shows it.</summary>
    private void Present()
    {
        var width = _rasterizer.Width;
        var height = _rasterizer.Height;
        if (width == 0 || height == 0)
        {
            return;
        }

        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), _rasterizer.Pixels, width * 4, 0);
        InvalidateVisual();
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
        RequestFrame();
    }

    /// <summary>Renders the next frame on a worker thread. A frame still in flight means this one waits its turn,
    /// so a slow machine drops frames rather than queueing them.</summary>
    private async void RequestFrame()
    {
        if (_rendering)
        {
            _frameWanted = true;
            return;
        }

        if (FrameFor(ActualWidth, ActualHeight) is not { } frame)
        {
            return;
        }

        _rendering = true;
        try
        {
            await Task.Run(() => _rasterizer.Render(frame)).ConfigureAwait(true);
            Present();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            // A bad frame is dropped; the next one tries again with the current state.
            Debug.WriteLine(ex);
        }
        finally
        {
            _rendering = false;
        }

        if (_frameWanted)
        {
            _frameWanted = false;
            RequestFrame();
        }
    }

    private void UpdatePace()
    {
        var running = IsVisible && IsLoaded && IsAnimated && Speed > 0 && FrameRate > 0;
        if (!running)
        {
            _timer.Stop();
            RequestFrame();
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

    private static void OnLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((MeshBackground)d).RequestFrame();
}
