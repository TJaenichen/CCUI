namespace CCUI.Terminal.Buffer;

[Flags]
public enum CellFlags : ushort
{
    None = 0,
    Bold = 1 << 0,
    Faint = 1 << 1,
    Italic = 1 << 2,
    Underline = 1 << 3,
    DoubleUnderline = 1 << 4,
    CurlyUnderline = 1 << 5,
    Blink = 1 << 6,
    Inverse = 1 << 7,
    Invisible = 1 << 8,
    Strikethrough = 1 << 9,
    Overline = 1 << 10,
}

public readonly record struct CellStyle(TerminalColor Foreground, TerminalColor Background, CellFlags Flags)
{
    public static CellStyle Default => default;

    public bool Has(CellFlags flag) => (Flags & flag) != 0;

    public CellStyle With(CellFlags flag, bool on) => this with { Flags = on ? Flags | flag : Flags & ~flag };

    /// <summary>The style erase operations fill with: only the background survives (xterm "background colour erase").</summary>
    public CellStyle ForErase() => new(TerminalColor.Default, Background, CellFlags.None);
}
