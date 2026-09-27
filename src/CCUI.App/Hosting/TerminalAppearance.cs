using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CCUI.App.Input;
using CCUI.Core.Settings;
using CCUI.Terminal;
using CCUI.Terminal.Wpf;
using Microsoft.Extensions.Options;

namespace CCUI.App.Hosting;

/// <summary>The terminal look resolved from <see cref="TerminalOptions"/> into WPF values, bound by every pane.</summary>
public sealed class TerminalAppearance
{
    public TerminalAppearance(IOptions<TerminalOptions> options)
    {
        var o = options.Value;
        FontFamily = new FontFamily(o.FontFamily);
        FontSize = Math.Max(4, o.FontSize) * 96.0 / 72.0;
        FontWeight = new FontWeightConverter().ConvertFromInvariantString(o.FontWeight) is FontWeight weight ? weight : FontWeights.Normal;
        FallbackFontFamilies = o.FallbackFontFamilies;
        LineHeight = o.LineHeight > 0 ? o.LineHeight : 1;
        ColorScheme = o.Schemes.Concat(ColorScheme.BuiltIn).FirstOrDefault(s => s.Name.Equals(o.ColorScheme, StringComparison.OrdinalIgnoreCase)) ?? ColorScheme.Campbell;
        CursorShape = Enum.TryParse<CursorShape>(o.CursorShape, ignoreCase: true, out var shape) ? shape : CursorShape.Bar;
        CursorBlink = o.CursorBlink;
        IntenseTextStyle = Enum.TryParse<IntenseTextStyle>(o.IntenseTextStyle, ignoreCase: true, out var intense) ? intense : IntenseTextStyle.All;
        UseBuiltinGlyphs = o.UseBuiltinGlyphs;
        UseColorEmoji = o.UseColorEmoji;
        CopyOnSelect = o.CopyOnSelect;
        Padding = new ThicknessConverter().ConvertFromInvariantString(o.Padding) is Thickness padding ? padding : new Thickness(8, 4, 8, 4);
        KeyBindings = [.. o.SendKeys
            .Select(k => KeyChord.TryParse(k.Keys, out var chord) ? new TerminalKeyBinding(chord.Key, chord.Modifiers, k.Text) : null)
            .OfType<TerminalKeyBinding>()];

        var background = Rgb.Parse(ColorScheme.Background);
        var alpha = (byte)Math.Round(Math.Clamp(o.Opacity, 0, 100) * 2.55);
        Background = Frozen(new SolidColorBrush(Color.FromArgb(alpha, background.R, background.G, background.B)));
        BackgroundImage = LoadImage(o.BackgroundImage);
        BackgroundImageOpacity = Math.Clamp(o.BackgroundImageOpacity, 0, 1);
        BackgroundImageStretch = Enum.TryParse<Stretch>(o.BackgroundImageStretchMode, ignoreCase: true, out var stretch) ? stretch : Stretch.UniformToFill;
        (BackgroundImageHorizontalAlignment, BackgroundImageVerticalAlignment) = ParseAlignment(o.BackgroundImageAlignment);
    }

    public FontFamily FontFamily { get; }

    /// <summary>In device-independent pixels (the options use points, like Windows Terminal).</summary>
    public double FontSize { get; }

    public FontWeight FontWeight { get; }

    public string FallbackFontFamilies { get; }

    public double LineHeight { get; }

    public ColorScheme ColorScheme { get; }

    public CursorShape CursorShape { get; }

    public bool CursorBlink { get; }

    public IntenseTextStyle IntenseTextStyle { get; }

    public bool UseBuiltinGlyphs { get; }

    public bool UseColorEmoji { get; }

    public bool CopyOnSelect { get; }

    public Thickness Padding { get; }

    public IReadOnlyList<TerminalKeyBinding> KeyBindings { get; }

    /// <summary>The scheme's background at the configured opacity; the window backdrop shows through the rest.</summary>
    public Brush Background { get; }

    public ImageSource? BackgroundImage { get; }

    public double BackgroundImageOpacity { get; }

    public Stretch BackgroundImageStretch { get; }

    public HorizontalAlignment BackgroundImageHorizontalAlignment { get; }

    public VerticalAlignment BackgroundImageVerticalAlignment { get; }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static ImageSource? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var file = Environment.ExpandEnvironmentVariables(path);
        if (!File.Exists(file))
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.GetFullPath(file));
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static (HorizontalAlignment, VerticalAlignment) ParseAlignment(string value)
    {
        var v = value.ToLowerInvariant();
        var horizontal = v.Contains("left", StringComparison.Ordinal) ? HorizontalAlignment.Left : v.Contains("right", StringComparison.Ordinal) ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        var vertical = v.Contains("top", StringComparison.Ordinal) ? VerticalAlignment.Top : v.Contains("bottom", StringComparison.Ordinal) ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        return (horizontal, vertical);
    }
}
