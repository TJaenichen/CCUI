using System.Globalization;
using System.Text;

namespace CCUI.Terminal.Input;

public enum TerminalKey
{
    Enter,
    Tab,
    Backspace,
    Escape,
    Up,
    Down,
    Left,
    Right,
    Home,
    End,
    Insert,
    Delete,
    PageUp,
    PageDown,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Control = 4,
}

/// <summary>Translates keys into the byte sequences an xterm-compatible terminal sends.</summary>
public static class KeyEncoder
{
    private const string Esc = "\e";

    /// <summary>The sequence for a non-text key, or null if the key has no encoding.</summary>
    public static string? Encode(TerminalKey key, KeyModifiers modifiers, bool applicationCursorKeys)
    {
        var alt = modifiers.HasFlag(KeyModifiers.Alt);
        var ctrl = modifiers.HasFlag(KeyModifiers.Control);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        var altPrefix = alt ? Esc : string.Empty;

        switch (key)
        {
            case TerminalKey.Enter:
                return altPrefix + "\r";
            case TerminalKey.Tab:
                return shift ? "\e[Z" : altPrefix + "\t";
            case TerminalKey.Backspace:
                return altPrefix + (ctrl ? "\b" : "\x7f");
            case TerminalKey.Escape:
                return altPrefix + Esc;
        }

        var modifierParam = ModifierParameter(modifiers);
        return key switch
        {
            TerminalKey.Up => Cursor('A'),
            TerminalKey.Down => Cursor('B'),
            TerminalKey.Right => Cursor('C'),
            TerminalKey.Left => Cursor('D'),
            TerminalKey.Home => Cursor('H'),
            TerminalKey.End => Cursor('F'),
            TerminalKey.Insert => Tilde(2),
            TerminalKey.Delete => Tilde(3),
            TerminalKey.PageUp => Tilde(5),
            TerminalKey.PageDown => Tilde(6),
            TerminalKey.F1 => Ss3OrCsi('P'),
            TerminalKey.F2 => Ss3OrCsi('Q'),
            TerminalKey.F3 => Ss3OrCsi('R'),
            TerminalKey.F4 => Ss3OrCsi('S'),
            TerminalKey.F5 => Tilde(15),
            TerminalKey.F6 => Tilde(17),
            TerminalKey.F7 => Tilde(18),
            TerminalKey.F8 => Tilde(19),
            TerminalKey.F9 => Tilde(20),
            TerminalKey.F10 => Tilde(21),
            TerminalKey.F11 => Tilde(23),
            TerminalKey.F12 => Tilde(24),
            _ => null,
        };

        string Cursor(char final) => modifierParam > 1
            ? string.Create(CultureInfo.InvariantCulture, $"\e[1;{modifierParam}{final}")
            : (applicationCursorKeys ? "\eO" : "\e[") + final;

        string Ss3OrCsi(char final) => modifierParam > 1
            ? string.Create(CultureInfo.InvariantCulture, $"\e[1;{modifierParam}{final}")
            : "\eO" + final;

        string Tilde(int number) => modifierParam > 1
            ? string.Create(CultureInfo.InvariantCulture, $"\e[{number};{modifierParam}~")
            : string.Create(CultureInfo.InvariantCulture, $"\e[{number}~");
    }

    /// <summary>
    /// The control character for Ctrl+<paramref name="c"/> (letters, space and the xterm punctuation set), prefixed with
    /// ESC when Alt is also held; null when the combination has no control character.
    /// </summary>
    public static string? EncodeControl(char c, bool alt)
    {
        int? code = char.ToUpperInvariant(c) switch
        {
            >= 'A' and <= 'Z' and var letter => letter - 'A' + 1,
            ' ' or '@' or '2' => 0,
            '[' or '3' => 0x1B,
            '\\' or '4' => 0x1C,
            ']' or '5' => 0x1D,
            '^' or '6' => 0x1E,
            '_' or '-' or '/' or '7' => 0x1F,
            '8' => 0x7F,
            _ => null,
        };

        return code is { } value ? (alt ? Esc : string.Empty) + (char)value : null;
    }

    /// <summary>Typed text; with Alt it is sent ESC-prefixed ("meta sends escape").</summary>
    public static string EncodeText(string text, bool alt) => alt ? Esc + text : text;

    /// <summary>Pasted text with line endings normalised to CR, wrapped in bracketed-paste markers when the mode is on.</summary>
    public static string EncodePaste(string text, bool bracketed)
    {
        var normalized = new StringBuilder(text.Length + 12);
        if (bracketed)
        {
            normalized.Append("\e[200~");
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                continue;
            }

            // Never let pasted text end bracketed paste early.
            if (bracketed && c == '\e')
            {
                continue;
            }

            normalized.Append(c == '\n' ? '\r' : c);
        }

        if (bracketed)
        {
            normalized.Append("\e[201~");
        }

        return normalized.ToString();
    }

    /// <summary>The focus-report sequence for DEC mode 1004.</summary>
    public static string EncodeFocus(bool focused) => focused ? "\e[I" : "\e[O";

    private static int ModifierParameter(KeyModifiers modifiers) =>
        1 + (modifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0) + (modifiers.HasFlag(KeyModifiers.Alt) ? 2 : 0) + (modifiers.HasFlag(KeyModifiers.Control) ? 4 : 0);
}
