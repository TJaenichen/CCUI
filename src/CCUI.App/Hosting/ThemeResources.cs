using System.Windows;
using System.Windows.Media;
using CCUI.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CCUI.App.Hosting;

/// <summary>Pushes the configured colours into the application resources (Themes/Theme.xaml holds the defaults).</summary>
public static class ThemeResources
{
    public static void Apply(Application app, IServiceProvider services)
    {
        var appearance = services.GetRequiredService<IOptions<AppearanceOptions>>().Value;
        var accent = ParseColor(appearance.AccentColor, Color.FromRgb(0xD9, 0x77, 0x57));
        var focus = ParseColor(appearance.FocusBorderColor, accent);

        Set(app, "AccentColor", accent);
        Set(app, "AccentBrush", Freeze(new SolidColorBrush(accent)));
        Set(app, "AccentSoftBrush", Freeze(new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B))));
        Set(app, "FocusBorderBrush", Freeze(new SolidColorBrush(focus)));
        Set(app, "FocusBorderThickness", new Thickness(Math.Max(0, appearance.FocusBorderThickness)));
        Set(app, "WindowTintBrush", Freeze(new SolidColorBrush(ParseColor(appearance.WindowTint, Color.FromArgb(0xB0, 0x10, 0x10, 0x14)))));
        Set(app, "TerminalAppearance", services.GetRequiredService<TerminalAppearance>());
    }

    public static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static void Set(Application app, string key, object value) => app.Resources[key] = value;

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
