using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CCUI.App.Hosting;
using CCUI.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

[assembly: AssemblyFixture(typeof(CCUI.App.Tests.WpfFixture))]

namespace CCUI.App.Tests;

/// <summary>
/// Runs WPF on one STA thread for the whole test run, with the real application resources loaded, and renders
/// elements to PNG files under TestResults/snapshots (CI uploads them so they can be looked at).
/// </summary>
public sealed class WpfFixture : IDisposable
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource<Dispatcher> _ready = new();

    public WpfFixture()
    {
        _thread = new Thread(() =>
        {
            var app = new App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var services = new ServiceCollection()
                .AddSingleton(Options.Create(new AppearanceOptions()))
                .AddSingleton(Options.Create(new TerminalOptions()))
                .AddSingleton<TerminalAppearance>()
                .BuildServiceProvider();
            ThemeResources.Apply(app, services);
            _ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        });
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Start();
        Dispatcher = _ready.Task.GetAwaiter().GetResult();
    }

    public Dispatcher Dispatcher { get; }

    public static string SnapshotDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "TestResults", "snapshots");

    public Task<T> Run<T>(Func<Task<T>> action) => Dispatcher.InvokeAsync(action).Task.Unwrap();

    public Task Run(Func<Task> action) => Dispatcher.InvokeAsync(action).Task.Unwrap();

    /// <summary>Lets queued layout, render and input work run (call on the UI thread).</summary>
    public static async Task Settle(int milliseconds, CancellationToken cancel)
    {
        if (milliseconds > 0)
        {
            await Task.Delay(milliseconds, cancel);
        }

        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    /// <summary>Measures, arranges and renders an element at a fixed size; saves it as a PNG and returns the pixels.</summary>
    public static Snapshot Render(FrameworkElement element, int width, int height, string name, Brush? background = null)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background ?? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Directory.CreateDirectory(SnapshotDirectory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(SnapshotDirectory, name + ".png")))
        {
            encoder.Save(file);
        }

        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return new Snapshot(width, height, pixels);
    }

    public void Dispose() => Dispatcher.InvokeShutdown();
}

/// <summary>Rendered pixels (BGRA) with helpers for simple assertions.</summary>
public sealed record Snapshot(int Width, int Height, byte[] Pixels)
{
    public Color this[int x, int y]
    {
        get
        {
            var i = ((y * Width) + x) * 4;
            return Color.FromArgb(Pixels[i + 3], Pixels[i + 2], Pixels[i + 1], Pixels[i]);
        }
    }

    /// <summary>True when some pixel in the rectangle is clearly coloured (not grey).</summary>
    public bool HasColourIn(Int32Rect area, int minimumSaturation = 60)
    {
        for (var y = area.Y; y < area.Y + area.Height && y < Height; y++)
        {
            for (var x = area.X; x < area.X + area.Width && x < Width; x++)
            {
                var c = this[x, y];
                if (Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) >= minimumSaturation)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public int CountDistinctColours() => Enumerable.Range(0, Width * Height).Select(i => BitConverter.ToInt32(Pixels, i * 4)).Distinct().Count();
}
