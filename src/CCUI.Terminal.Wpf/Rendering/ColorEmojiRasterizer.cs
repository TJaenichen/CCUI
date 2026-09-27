using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.WIC;
using D2DPixelFormat = Vortice.DCommon.PixelFormat;
using DxgiFormat = Vortice.DXGI.Format;

namespace CCUI.Terminal.Wpf.Rendering;

/// <summary>
/// Renders emoji in colour. WPF's own text stack only draws colour fonts in monochrome, so emoji clusters are
/// rasterised once with Direct2D/DirectWrite (which handles colour glyphs and ZWJ sequences) and cached as bitmaps.
/// If Direct2D is unavailable the rasteriser switches itself off and the caller falls back to plain glyphs.
/// </summary>
internal sealed class ColorEmojiRasterizer : IDisposable
{
    private const int MaxCacheEntries = 1024;

    private readonly Dictionary<(string Text, int Width, int Height), BitmapSource?> _cache = [];
    private readonly string _fontFamily;
    private ID2D1Factory? _d2d;
    private IDWriteFactory? _dwrite;
    private IWICImagingFactory? _wic;
    private bool _failed;

    public ColorEmojiRasterizer(string fontFamily = "Segoe UI Emoji") => _fontFamily = fontFamily;

    public bool IsAvailable => !_failed;

    /// <summary>The emoji as a bitmap of exactly <paramref name="pixelWidth"/> x <paramref name="pixelHeight"/> device pixels, or null.</summary>
    public BitmapSource? Rasterize(string text, int pixelWidth, int pixelHeight)
    {
        if (_failed || pixelWidth <= 0 || pixelHeight <= 0)
        {
            return null;
        }

        var key = (text, pixelWidth, pixelHeight);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (_cache.Count >= MaxCacheEntries)
        {
            _cache.Clear();
        }

        BitmapSource? bitmap;
        try
        {
            bitmap = RasterizeCore(text, pixelWidth, pixelHeight);
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or SharpGen.Runtime.SharpGenException)
        {
            _failed = true;
            return null;
        }

        _cache[key] = bitmap;
        return bitmap;
    }

    public void Dispose()
    {
        _wic?.Dispose();
        _dwrite?.Dispose();
        _d2d?.Dispose();
    }

    private BitmapSource RasterizeCore(string text, int width, int height)
    {
        _d2d ??= D2D1.D2D1CreateFactory<ID2D1Factory>(Vortice.Direct2D1.FactoryType.SingleThreaded);
        _dwrite ??= DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);
        _wic ??= new IWICImagingFactory();

        using var wicBitmap = _wic.CreateBitmap((uint)width, (uint)height, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
        var properties = new RenderTargetProperties(
            RenderTargetType.Software,
            new D2DPixelFormat(DxgiFormat.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96,
            96,
            RenderTargetUsage.None,
            FeatureLevel.Default);

        // Segoe UI Emoji's glyphs are about 1.33 em tall; size them to fill the cell height.
        var emSize = height * 0.75f;
        using (var target = _d2d.CreateWicBitmapRenderTarget(wicBitmap, properties))
        using (var format = _dwrite.CreateTextFormat(_fontFamily, FontWeight.Normal, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, emSize))
        using (var brush = target.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f)))
        {
            format.TextAlignment = TextAlignment.Center;
            format.ParagraphAlignment = ParagraphAlignment.Center;
            format.WordWrapping = WordWrapping.NoWrap;
            target.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
            target.BeginDraw();
            target.Clear(new Color4(0f, 0f, 0f, 0f));
            target.DrawText(text, format, new Rect(0, 0, width, height), brush, DrawTextOptions.EnableColorFont | DrawTextOptions.Clip);
            target.EndDraw();
        }

        var stride = width * 4;
        var pixels = new byte[stride * height];
        wicBitmap.CopyPixels((uint)stride, pixels);
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }
}
