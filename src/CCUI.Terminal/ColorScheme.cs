using System.Globalization;

namespace CCUI.Terminal;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>Parses <c>#RRGGBB</c> (or <c>RRGGBB</c>).</summary>
    public static Rgb Parse(string value)
    {
        var hex = value.AsSpan().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            throw new FormatException($"'{value}' is not a #RRGGBB colour.");
        }

        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// A colour scheme with the same property names as a Windows Terminal "schemes" entry, so one can be pasted from
/// Windows Terminal's settings.json into appsettings.json unchanged.
/// </summary>
public sealed record ColorScheme
{
    public string Name { get; init; } = "Campbell";
    public string Foreground { get; init; } = "#CCCCCC";
    public string Background { get; init; } = "#0C0C0C";
    public string CursorColor { get; init; } = "#FFFFFF";
    public string SelectionBackground { get; init; } = "#FFFFFF";
    public string Black { get; init; } = "#0C0C0C";
    public string Red { get; init; } = "#C50F1F";
    public string Green { get; init; } = "#13A10E";
    public string Yellow { get; init; } = "#C19C00";
    public string Blue { get; init; } = "#0037DA";
    public string Purple { get; init; } = "#881798";
    public string Cyan { get; init; } = "#3A96DD";
    public string White { get; init; } = "#CCCCCC";
    public string BrightBlack { get; init; } = "#767676";
    public string BrightRed { get; init; } = "#E74856";
    public string BrightGreen { get; init; } = "#16C60C";
    public string BrightYellow { get; init; } = "#F9F1A5";
    public string BrightBlue { get; init; } = "#3B78FF";
    public string BrightPurple { get; init; } = "#B4009E";
    public string BrightCyan { get; init; } = "#61D6D6";
    public string BrightWhite { get; init; } = "#F2F2F2";

    public static ColorScheme Campbell { get; } = new();

    public static ColorScheme CampbellPowershell { get; } = new() { Name = "Campbell Powershell", Background = "#012456" };

    public static ColorScheme OneHalfDark { get; } = new()
    {
        Name = "One Half Dark",
        Foreground = "#DCDFE4", Background = "#282C34", CursorColor = "#FFFFFF", SelectionBackground = "#FFFFFF",
        Black = "#282C34", Red = "#E06C75", Green = "#98C379", Yellow = "#E5C07B",
        Blue = "#61AFEF", Purple = "#C678DD", Cyan = "#56B6C2", White = "#DCDFE4",
        BrightBlack = "#5A6374", BrightRed = "#E06C75", BrightGreen = "#98C379", BrightYellow = "#E5C07B",
        BrightBlue = "#61AFEF", BrightPurple = "#C678DD", BrightCyan = "#56B6C2", BrightWhite = "#DCDFE4",
    };

    public static ColorScheme TangoDark { get; } = new()
    {
        Name = "Tango Dark",
        Foreground = "#D3D7CF", Background = "#000000", CursorColor = "#FFFFFF", SelectionBackground = "#FFFFFF",
        Black = "#000000", Red = "#CC0000", Green = "#4E9A06", Yellow = "#C4A000",
        Blue = "#3465A4", Purple = "#75507B", Cyan = "#06989A", White = "#D3D7CF",
        BrightBlack = "#555753", BrightRed = "#EF2929", BrightGreen = "#8AE234", BrightYellow = "#FCE94F",
        BrightBlue = "#729FCF", BrightPurple = "#AD7FA8", BrightCyan = "#34E2E2", BrightWhite = "#EEEEEC",
    };

    public static IReadOnlyList<ColorScheme> BuiltIn { get; } = [Campbell, CampbellPowershell, OneHalfDark, TangoDark];

    public IReadOnlyList<string> AnsiColors =>
    [
        Black, Red, Green, Yellow, Blue, Purple, Cyan, White,
        BrightBlack, BrightRed, BrightGreen, BrightYellow, BrightBlue, BrightPurple, BrightCyan, BrightWhite,
    ];
}
