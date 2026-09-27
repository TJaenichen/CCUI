using System.Globalization;
using System.Text;
using CCUI.Terminal.Parsing;

namespace CCUI.Terminal;

public sealed partial class TerminalEmulator
{
    void IVtHandler.OscDispatch(string data, bool bellTerminated)
    {
        _lastCell = null;
        var separator = data.IndexOf(';', StringComparison.Ordinal);
        var codeText = separator < 0 ? data : data[..separator];
        var text = separator < 0 ? string.Empty : data[(separator + 1)..];
        if (!int.TryParse(codeText, NumberStyles.None, CultureInfo.InvariantCulture, out var code))
        {
            return;
        }

        switch (code)
        {
            case 0:
            case 2:
                SetTitle(text);
                break;
            case 9:
                if (text.StartsWith("4;", StringComparison.Ordinal))
                {
                    SetProgress(ParseProgress(text[2..]));
                }
                else if (text.Length > 0 && !char.IsAsciiDigit(text[0]))
                {
                    // iTerm2-style notification: OSC 9 ; message
                    Queue(EventKind.Notification, text);
                }

                break;
            case 10:
            case 11:
            case 12:
                if (text == "?")
                {
                    var color = code switch { 10 => Palette.Foreground, 11 => Palette.Background, _ => Palette.Cursor };
                    SendReply(string.Create(
                        CultureInfo.InvariantCulture,
                        $"\e]{code};rgb:{color.R:x2}{color.R:x2}/{color.G:x2}{color.G:x2}/{color.B:x2}{color.B:x2}{(bellTerminated ? "\a" : "\e\\")}"));
                }

                break;
            case 52:
                if (DecodeClipboard(text) is { } clip)
                {
                    Queue(EventKind.Clipboard, clip);
                }

                break;
            case 777:
                // urxvt / Ghostty notification: OSC 777 ; notify ; title ; body
                var parts = text.Split(';', 3);
                if (parts.Length >= 2 && parts[0] == "notify")
                {
                    Queue(EventKind.Notification, parts.Length == 3 ? $"{parts[1]}: {parts[2]}" : parts[1]);
                }

                break;
        }
    }

    void IVtHandler.DcsDispatch(VtParams parameters, char privateMarker, ReadOnlySpan<char> intermediates, char final, string data)
    {
        _lastCell = null;
        if (intermediates.Length != 1 || final != 'q')
        {
            return;
        }

        // Answer "unsupported" rather than staying silent, so an application waiting for a reply does not hang.
        if (intermediates[0] == '$')
        {
            SendReply("\eP0$r\e\\");
        }
        else if (intermediates[0] == '+')
        {
            SendReply("\eP0+r\e\\");
        }
    }

    private static TerminalProgress ParseProgress(string text)
    {
        var parts = text.Split(';');
        var state = parts.Length > 0 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var s) && s is >= 0 and <= 4
            ? (ProgressState)s
            : ProgressState.None;
        var value = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 100) : 0;
        return new TerminalProgress(state, state == ProgressState.None ? 0 : value);
    }

    private static string? DecodeClipboard(string text)
    {
        var separator = text.IndexOf(';', StringComparison.Ordinal);
        if (separator < 0)
        {
            return null;
        }

        var payload = text[(separator + 1)..];
        if (payload.Length == 0 || payload == "?")
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
