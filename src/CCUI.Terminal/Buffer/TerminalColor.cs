namespace CCUI.Terminal.Buffer;

public enum TerminalColorKind : byte
{
    Default = 0,
    Indexed = 1,
    Rgb = 2,
}

/// <summary>A cell colour as the application specified it: the theme default, a palette index, or a true colour.</summary>
public readonly record struct TerminalColor
{
    private readonly uint _value;

    private TerminalColor(uint value) => _value = value;

    public static TerminalColor Default => default;

    public TerminalColorKind Kind => (TerminalColorKind)(_value >> 24);

    public byte Index => (byte)_value;

    public byte R => (byte)(_value >> 16);

    public byte G => (byte)(_value >> 8);

    public byte B => (byte)_value;

    public bool IsDefault => Kind == TerminalColorKind.Default;

    public static TerminalColor FromIndex(int index) =>
        new(((uint)TerminalColorKind.Indexed << 24) | (byte)Math.Clamp(index, 0, 255));

    public static TerminalColor FromRgb(byte r, byte g, byte b) =>
        new(((uint)TerminalColorKind.Rgb << 24) | ((uint)r << 16) | ((uint)g << 8) | b);

    public override string ToString() => Kind switch
    {
        TerminalColorKind.Default => "default",
        TerminalColorKind.Indexed => $"#{Index}",
        _ => $"rgb({R},{G},{B})",
    };
}
