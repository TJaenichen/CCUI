using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CCUI.App.Input;
using CCUI.Core.Settings;
using CCUI.Terminal;
using CCUI.Terminal.Wpf;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;

namespace CCUI.App.Hosting;

/// <summary>
/// The terminal look resolved from <see cref="TerminalOptions"/> into WPF values, bound by every pane. There is one
/// instance for the app; <see cref="Apply"/> replaces its values when the settings change and the panes follow
/// through the property change notifications.
/// </summary>
public sealed class TerminalAppearance : ObservableObject
{
    public TerminalAppearance(IOptions<TerminalOptions> options) => Apply(options.Value);

    public FontFamily FontFamily { get; private set => SetProperty(ref field, value); } = new("Consolas");

    /// <summary>In device-independent pixels (the options use points, like Windows Terminal).</summary>
    public double FontSize { get; private set => SetProperty(ref field, value); } = 16;

    public FontWeight FontWeight { get; private set => SetProperty(ref field, value); } = FontWeights.Normal;

    public string FallbackFontFamilies { get; private set => SetProperty(ref field, value); } = string.Empty;

    public double LineHeight { get; private set => SetProperty(ref field, value); } = 1;

    public ColorScheme ColorScheme { get; private set => SetProperty(ref field, value); } = ColorScheme.Campbell;

    public CursorShape CursorShape { get; private set => SetProperty(ref field, value); }

    public bool CursorBlink { get; private set => SetProperty(ref field, value); }

    public IntenseTextStyle IntenseTextStyle { get; private set => SetProperty(ref field, value); }

    public bool UseBuiltinGlyphs { get; private set => SetProperty(ref field, value); }

    public bool UseColorEmoji { get; private set => SetProperty(ref field, value); }

    public bool CopyOnSelect { get; private set => SetProperty(ref field, value); }

    public Thickness Padding { get; private set => SetProperty(ref field, value); }

    public IReadOnlyList<TerminalKeyBinding> KeyBindings { get; private set => SetProperty(ref field, value); } = [];

    /// <summary>The scheme's background at the configured opacity; the window backdrop shows through the rest.</summary>
    public Brush Background { get; private set => SetProperty(ref field, value); } = Brushes.Black;

    public ImageSource? BackgroundImage { get; private set => SetProperty(ref field, value); }

    public double BackgroundImageOpacity { get; private set => SetProperty(ref field, value); }

    public Stretch BackgroundImageStretch { get; private set => SetProperty(ref field, value); }

    public HorizontalAlignment BackgroundImageHorizontalAlignment { get; private set => SetProperty(ref field, value); }

    public VerticalAlignment BackgroundImageVerticalAlignment { get; private set => SetProperty(ref field, value); }

    /// <summary>Resolves the options into WPF values; every pane bound to this instance updates.</summary>
    public void Apply(TerminalOptions o)
    {
        FontFamily = new FontFamily(o.FontFamily);
        FontSize = Math.Max(4, o.FontSize) * 96.0 / 72.0;
        FontWeight = ParseWeight(o.FontWeight);
        FallbackFontFamilies = o.FallbackFontFamilies;
        LineHeight = o.LineHeight > 0 ? o.LineHeight : 1;
        ColorScheme = o.Schemes.Concat(ColorScheme.BuiltIn).FirstOrDefault(s => s.Name.Equals(o.ColorScheme, StringComparison.OrdinalIgnoreCase)) ?? ColorScheme.Campbell;
        CursorShape = Enum.TryParse<CursorShape>(o.CursorShape, ignoreCase: true, out var shape) ? shape : CursorShape.Bar;
        CursorBlink = o.CursorBlink;
        IntenseTextStyle = Enum.TryParse<IntenseTextStyle>(o.IntenseTextStyle, ignoreCase: true, out var intense) ? intense : IntenseTextStyle.All;
        UseBuiltinGlyphs = o.UseBuiltinGlyphs;
        UseColorEmoji = o.UseColorEmoji;
        CopyOnSelect = o.CopyOnSelect;
        Padding = ParsePadding(o.Padding);
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

    private static FontWeight ParseWeight(string value)
    {
        try
        {
            return new FontWeightConverter().ConvertFromInvariantString(value) is FontWeight weight ? weight : FontWeights.Normal;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException)
        {
            return FontWeights.Normal;
        }
    }

    private static Thickness ParsePadding(string value)
    {
        try
        {
            return new ThicknessConverter().ConvertFromInvariantString(value) is Thickness padding ? padding : new Thickness(8, 4, 8, 4);
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException)
        {
            return new Thickness(8, 4, 8, 4);
        }
    }

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

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Path.GetFullPath(file));
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or UnauthorizedAccessException or UriFormatException)
        {
            // Not an image, or not readable: the terminals simply have no picture behind them.
            return null;
        }
    }

    private static (HorizontalAlignment, VerticalAlignment) ParseAlignment(string value)
    {
        var v = value.ToLowerInvariant();
        var horizontal = v.Contains("left", StringComparison.Ordinal) ? HorizontalAlignment.Left : v.Contains("right", StringComparison.Ordinal) ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        var vertical = v.Contains("top", StringComparison.Ordinal) ? VerticalAlignment.Top : v.Contains("bottom", StringComparison.Ordinal) ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        return (horizontal, vertical);
    }
}
