using CCUI.Terminal.Buffer;

namespace CCUI.Terminal;

/// <summary>The resolved 256-colour palette plus default foreground, background, cursor and selection colours.</summary>
public sealed class TerminalPalette
{
    private static readonly byte[] CubeLevels = [0, 95, 135, 175, 215, 255];
    private readonly Rgb[] _colors = new Rgb[256];

    private TerminalPalette()
    {
    }

    public Rgb Foreground { get; private init; }

    public Rgb Background { get; private init; }

    public Rgb Cursor { get; private init; }

    public Rgb SelectionBackground { get; private init; }

    public Rgb this[int index] => _colors[index];

    public static TerminalPalette FromScheme(ColorScheme scheme)
    {
        var palette = new TerminalPalette
        {
            Foreground = Rgb.Parse(scheme.Foreground),
            Background = Rgb.Parse(scheme.Background),
            Cursor = Rgb.Parse(scheme.CursorColor),
            SelectionBackground = Rgb.Parse(scheme.SelectionBackground),
        };

        var ansi = scheme.AnsiColors;
        for (var i = 0; i < 16; i++)
        {
            palette._colors[i] = Rgb.Parse(ansi[i]);
        }

        for (var i = 16; i < 232; i++)
        {
            var n = i - 16;
            palette._colors[i] = new Rgb(CubeLevels[n / 36], CubeLevels[(n / 6) % 6], CubeLevels[n % 6]);
        }

        for (var i = 232; i < 256; i++)
        {
            var level = (byte)(8 + ((i - 232) * 10));
            palette._colors[i] = new Rgb(level, level, level);
        }

        return palette;
    }

    /// <summary>Maps a cell colour to RGB; default colours resolve to the scheme foreground or background.</summary>
    public Rgb Resolve(TerminalColor color, bool foreground) => color.Kind switch
    {
        TerminalColorKind.Indexed => _colors[color.Index],
        TerminalColorKind.Rgb => new Rgb(color.R, color.G, color.B),
        _ => foreground ? Foreground : Background,
    };
}
