using System.Globalization;
using CCUI.Terminal.Buffer;
using CCUI.Terminal.Parsing;

namespace CCUI.Terminal;

public sealed partial class TerminalEmulator
{
    void IVtHandler.CsiDispatch(VtParams parameters, char privateMarker, ReadOnlySpan<char> intermediates, char final)
    {
        _lastCell = null;
        if (intermediates.Length > 1)
        {
            return;
        }

        var intermediate = intermediates.Length == 1 ? intermediates[0] : '\0';
        switch (privateMarker, intermediate)
        {
            case ('\0', '\0'):
                DispatchStandardCsi(parameters, final);
                break;
            case ('?', '\0'):
                DispatchPrivateCsi(parameters, final);
                break;
            case ('?', '$') when final == 'p':
                ReportMode(parameters.Get(0, 0), dec: true);
                break;
            case ('\0', '$') when final == 'p':
                ReportMode(parameters.Get(0, 0), dec: false);
                break;
            case ('>', '\0') when final == 'c':
                SendReply("\e[>0;10;1c");
                break;
            case ('>', '\0') when final == 'q':
                SendReply("\eP>|CCUI\e\\");
                break;
            case ('\0', ' ') when final == 'q':
                SetCursorStyle(parameters.Get(0, 0));
                break;
            case ('\0', '!') when final == 'p':
                SoftReset();
                break;
        }
    }

    private void DispatchStandardCsi(VtParams p, char final)
    {
        var n = p.GetNonZero(0, 1);
        switch (final)
        {
            case '@':
                Buffer[_row].BreakWideCharsAt(_col, _col);
                Buffer[_row].InsertCells(_col, n, _style.ForErase());
                break;
            case 'A':
                MoveCursor(-n, 0);
                break;
            case 'B':
            case 'e':
                MoveCursor(n, 0);
                break;
            case 'C':
            case 'a':
                MoveCursor(0, n);
                break;
            case 'D':
                MoveCursor(0, -n);
                break;
            case 'E':
                MoveCursor(n, 0);
                _col = 0;
                break;
            case 'F':
                MoveCursor(-n, 0);
                _col = 0;
                break;
            case 'G':
            case '`':
                _wrapPending = false;
                _col = Math.Clamp(n - 1, 0, Columns - 1);
                break;
            case 'H':
            case 'f':
                SetCursorPosition(p.GetNonZero(0, 1), p.GetNonZero(1, 1));
                break;
            case 'I':
                _wrapPending = false;
                _col = NextTabStop(_col, n);
                break;
            case 'Z':
                _wrapPending = false;
                _col = PreviousTabStop(_col, n);
                break;
            case 'J':
                EraseInDisplay(p.Get(0, 0));
                break;
            case 'K':
                EraseInLine(p.Get(0, 0));
                break;
            case 'L':
                if (_row >= _top && _row <= _bottom)
                {
                    Buffer.ScrollDown(_row, _bottom, n, _style);
                    _col = 0;
                    _wrapPending = false;
                }

                break;
            case 'M':
                if (_row >= _top && _row <= _bottom)
                {
                    Buffer.ScrollUp(_row, _bottom, n, _style, saveToScrollback: false);
                    _col = 0;
                    _wrapPending = false;
                }

                break;
            case 'P':
                Buffer[_row].BreakWideCharsAt(_col, _col);
                Buffer[_row].DeleteCells(_col, n, _style.ForErase());
                break;
            case 'S':
                Buffer.ScrollUp(_top, _bottom, n, _style, saveToScrollback: false);
                break;
            case 'T' when p.Count <= 1:
                Buffer.ScrollDown(_top, _bottom, n, _style);
                break;
            case 'X':
                var end = Math.Min(Columns, _col + n);
                Buffer[_row].BreakWideCharsAt(_col, end);
                Buffer[_row].Fill(_col, end, Cell.Blank(_style.ForErase()));
                break;
            case 'b':
                RepeatLastCharacter(n);
                break;
            case 'c' when p.Get(0, 0) == 0:
                // VT220 with ANSI colour and OSC 52 clipboard support.
                SendReply("\e[?62;22;52c");
                break;
            case 'd':
                _wrapPending = false;
                _row = Modes.OriginMode ? Math.Clamp(_top + n - 1, _top, _bottom) : Math.Clamp(n - 1, 0, Rows - 1);
                break;
            case 'g':
                ClearTabStops(p.Get(0, 0));
                break;
            case 'h':
            case 'l':
                for (var i = 0; i < p.Count; i++)
                {
                    SetAnsiMode(p[i], final == 'h');
                }

                break;
            case 'm':
                SelectGraphicRendition(p);
                break;
            case 'n':
                DeviceStatusReport(p.Get(0, 0), dec: false);
                break;
            case 'r':
                SetScrollRegion(p.GetNonZero(0, 1), p.GetNonZero(1, Rows));
                break;
            case 's':
                SaveCursor();
                break;
            case 'u':
                RestoreCursor();
                break;
            case 't':
                WindowOperation(p);
                break;
        }
    }

    private void DispatchPrivateCsi(VtParams p, char final)
    {
        switch (final)
        {
            case 'h':
            case 'l':
                for (var i = 0; i < p.Count; i++)
                {
                    SetDecMode(p[i], final == 'h');
                }

                break;
            case 'n':
                DeviceStatusReport(p.Get(0, 0), dec: true);
                break;
            case 'J':
                EraseInDisplay(p.Get(0, 0));
                break;
            case 'K':
                EraseInLine(p.Get(0, 0));
                break;

            // CSI ? u (kitty keyboard query) is deliberately unanswered: no reply means "not supported".
        }
    }

    private void MoveCursor(int rows, int columns)
    {
        _wrapPending = false;
        if (rows != 0)
        {
            var min = _row >= _top ? _top : 0;
            var max = _row <= _bottom ? _bottom : Rows - 1;
            _row = Math.Clamp(_row + rows, min, max);
        }

        _col = Math.Clamp(_col + columns, 0, Columns - 1);
    }

    private void SetCursorPosition(int row, int column)
    {
        _wrapPending = false;
        _row = Modes.OriginMode ? Math.Clamp(_top + row - 1, _top, _bottom) : Math.Clamp(row - 1, 0, Rows - 1);
        _col = Math.Clamp(column - 1, 0, Columns - 1);
    }

    private void EraseInDisplay(int mode)
    {
        var fill = _style.ForErase();
        switch (mode)
        {
            case 0:
                EraseInLine(0);
                for (var r = _row + 1; r < Rows; r++)
                {
                    Buffer[r].Clear(fill);
                }

                break;
            case 1:
                for (var r = 0; r < _row; r++)
                {
                    Buffer[r].Clear(fill);
                }

                EraseInLine(1);
                break;
            case 2:
                Buffer.ClearScreen(_style);
                break;
            case 3:
                Buffer.Scrollback?.Clear();
                break;
        }
    }

    private void EraseInLine(int mode)
    {
        var line = Buffer[_row];
        var (start, end) = mode switch
        {
            0 => (_col, Columns),
            1 => (0, _col + 1),
            _ => (0, Columns),
        };

        line.BreakWideCharsAt(start, end);
        line.Fill(start, end, Cell.Blank(_style.ForErase()));
        if (end == Columns)
        {
            line.IsWrapped = false;
        }
    }

    private void RepeatLastCharacter(int count)
    {
        if (_lastPrinted is null)
        {
            return;
        }

        var width = Graphemes.ClusterWidth(_lastPrinted);
        for (var i = 0; i < Math.Min(count, Columns * Rows); i++)
        {
            PrintCluster(_lastPrinted, width);
        }

        _lastCell = null;
    }

    private void ClearTabStops(int mode)
    {
        if (mode == 0)
        {
            _tabStops[_col] = false;
        }
        else if (mode == 3)
        {
            Array.Clear(_tabStops);
        }
    }

    private void SetScrollRegion(int top, int bottom)
    {
        bottom = Math.Min(bottom, Rows);
        if (top >= bottom)
        {
            return;
        }

        _top = top - 1;
        _bottom = bottom - 1;
        SetCursorPosition(1, 1);
    }

    private void SetAnsiMode(int mode, bool on)
    {
        switch (mode)
        {
            case 4:
                Modes.InsertMode = on;
                break;
            case 20:
                Modes.LineFeedNewLine = on;
                break;
        }
    }

    private void SetDecMode(int mode, bool on)
    {
        switch (mode)
        {
            case 1:
                Modes.ApplicationCursorKeys = on;
                break;
            case 5:
                Modes.ReverseVideo = on;
                break;
            case 6:
                Modes.OriginMode = on;
                SetCursorPosition(1, 1);
                break;
            case 7:
                Modes.AutoWrap = on;
                _wrapPending &= on;
                break;
            case 9:
                Modes.MouseTracking = on ? MouseTrackingMode.X10 : MouseTrackingMode.None;
                break;
            case 12:
                Modes.CursorBlinking = on;
                break;
            case 25:
                Modes.CursorVisible = on;
                break;
            case 47:
                SwitchScreen(on, clearAlternate: false);
                break;
            case 1000:
                Modes.MouseTracking = on ? MouseTrackingMode.Normal : MouseTrackingMode.None;
                break;
            case 1002:
                Modes.MouseTracking = on ? MouseTrackingMode.ButtonEvent : MouseTrackingMode.None;
                break;
            case 1003:
                Modes.MouseTracking = on ? MouseTrackingMode.AnyEvent : MouseTrackingMode.None;
                break;
            case 1004:
                Modes.FocusEvents = on;
                break;
            case 1006:
                Modes.SgrMouse = on;
                break;
            case 1007:
                Modes.AlternateScroll = on;
                break;
            case 1047:
                if (!on)
                {
                    _alternate.ClearScreen(_style);
                }

                SwitchScreen(on, clearAlternate: false);
                break;
            case 1048:
                if (on)
                {
                    SaveCursor();
                }
                else
                {
                    RestoreCursor();
                }

                break;
            case 1049:
                if (on)
                {
                    SaveCursor();
                    SwitchScreen(alternate: true, clearAlternate: true);
                }
                else
                {
                    SwitchScreen(alternate: false, clearAlternate: false);
                    RestoreCursor();
                }

                break;
            case 2004:
                Modes.BracketedPaste = on;
                break;
            case 2026:
                Modes.SynchronizedOutput = on;
                break;
            case 9001:
                Modes.Win32InputMode = on;
                break;
        }
    }

    private bool? GetDecMode(int mode) => mode switch
    {
        1 => Modes.ApplicationCursorKeys,
        5 => Modes.ReverseVideo,
        6 => Modes.OriginMode,
        7 => Modes.AutoWrap,
        9 => Modes.MouseTracking == MouseTrackingMode.X10,
        12 => Modes.CursorBlinking,
        25 => Modes.CursorVisible,
        47 or 1047 or 1049 => IsAlternateScreen,
        1000 => Modes.MouseTracking == MouseTrackingMode.Normal,
        1002 => Modes.MouseTracking == MouseTrackingMode.ButtonEvent,
        1003 => Modes.MouseTracking == MouseTrackingMode.AnyEvent,
        1004 => Modes.FocusEvents,
        1006 => Modes.SgrMouse,
        1007 => Modes.AlternateScroll,
        2004 => Modes.BracketedPaste,
        2026 => Modes.SynchronizedOutput,
        _ => null,
    };

    private void ReportMode(int mode, bool dec)
    {
        var state = dec
            ? GetDecMode(mode)
            : mode switch { 4 => Modes.InsertMode, 20 => Modes.LineFeedNewLine, _ => (bool?)null };
        var value = state switch { true => 1, false => 2, null => 0 };
        SendReply(string.Create(CultureInfo.InvariantCulture, $"\e[{(dec ? "?" : string.Empty)}{mode};{value}$y"));
    }

    private void DeviceStatusReport(int request, bool dec)
    {
        if (request == 5 && !dec)
        {
            SendReply("\e[0n");
        }
        else if (request == 6)
        {
            var row = (Modes.OriginMode ? _row - _top : _row) + 1;
            SendReply(string.Create(CultureInfo.InvariantCulture, $"\e[{(dec ? "?" : string.Empty)}{row};{_col + 1}R"));
        }
    }

    private void WindowOperation(VtParams p)
    {
        var (cellWidth, cellHeight) = CellPixelSize;
        switch (p.Get(0, 0))
        {
            case 14 when cellWidth > 0:
                SendReply(string.Create(CultureInfo.InvariantCulture, $"\e[4;{cellHeight * Rows};{cellWidth * Columns}t"));
                break;
            case 16 when cellWidth > 0:
                SendReply(string.Create(CultureInfo.InvariantCulture, $"\e[6;{cellHeight};{cellWidth}t"));
                break;
            case 18:
                SendReply(string.Create(CultureInfo.InvariantCulture, $"\e[8;{Rows};{Columns}t"));
                break;
            case 22:
                if (_titleStack.Count < 10)
                {
                    _titleStack.Push(Title);
                }

                break;
            case 23:
                if (_titleStack.TryPop(out var title))
                {
                    SetTitle(title);
                }

                break;
        }
    }

    private void SetCursorStyle(int value)
    {
        RequestedCursorStyle = value switch
        {
            1 => new CursorStyle(CursorShape.Block, true),
            2 => new CursorStyle(CursorShape.Block, false),
            3 => new CursorStyle(CursorShape.Underline, true),
            4 => new CursorStyle(CursorShape.Underline, false),
            5 => new CursorStyle(CursorShape.Bar, true),
            6 => new CursorStyle(CursorShape.Bar, false),
            _ => null,
        };
    }

    private void SelectGraphicRendition(VtParams p)
    {
        if (p.Count == 0)
        {
            _style = default;
            return;
        }

        var i = 0;
        while (i < p.Count)
        {
            var code = p.Get(i, 0);
            var next = i + 1;
            while (next < p.Count && p.IsSubParameter(next))
            {
                next++;
            }

            switch (code)
            {
                case 0:
                    _style = default;
                    break;
                case 1:
                    _style = _style.With(CellFlags.Bold, true);
                    break;
                case 2:
                    _style = _style.With(CellFlags.Faint, true);
                    break;
                case 3:
                    _style = _style.With(CellFlags.Italic, true);
                    break;
                case 4:
                    SetUnderline(next > i + 1 ? p.Get(i + 1, 1) : 1);
                    break;
                case 5:
                case 6:
                    _style = _style.With(CellFlags.Blink, true);
                    break;
                case 7:
                    _style = _style.With(CellFlags.Inverse, true);
                    break;
                case 8:
                    _style = _style.With(CellFlags.Invisible, true);
                    break;
                case 9:
                    _style = _style.With(CellFlags.Strikethrough, true);
                    break;
                case 21:
                    SetUnderline(2);
                    break;
                case 22:
                    _style = _style.With(CellFlags.Bold | CellFlags.Faint, false);
                    break;
                case 23:
                    _style = _style.With(CellFlags.Italic, false);
                    break;
                case 24:
                    SetUnderline(0);
                    break;
                case 25:
                    _style = _style.With(CellFlags.Blink, false);
                    break;
                case 27:
                    _style = _style.With(CellFlags.Inverse, false);
                    break;
                case 28:
                    _style = _style.With(CellFlags.Invisible, false);
                    break;
                case 29:
                    _style = _style.With(CellFlags.Strikethrough, false);
                    break;
                case >= 30 and <= 37:
                    _style = _style with { Foreground = TerminalColor.FromIndex(code - 30) };
                    break;
                case 38:
                    if (ReadExtendedColor(p, i, ref next) is { } fg)
                    {
                        _style = _style with { Foreground = fg };
                    }

                    break;
                case 39:
                    _style = _style with { Foreground = TerminalColor.Default };
                    break;
                case >= 40 and <= 47:
                    _style = _style with { Background = TerminalColor.FromIndex(code - 40) };
                    break;
                case 48:
                    if (ReadExtendedColor(p, i, ref next) is { } bg)
                    {
                        _style = _style with { Background = bg };
                    }

                    break;
                case 49:
                    _style = _style with { Background = TerminalColor.Default };
                    break;
                case 53:
                    _style = _style.With(CellFlags.Overline, true);
                    break;
                case 55:
                    _style = _style.With(CellFlags.Overline, false);
                    break;
                case 58:
                    // Underline colour: parsed so its arguments are skipped, not rendered.
                    ReadExtendedColor(p, i, ref next);
                    break;
                case >= 90 and <= 97:
                    _style = _style with { Foreground = TerminalColor.FromIndex(code - 90 + 8) };
                    break;
                case >= 100 and <= 107:
                    _style = _style with { Background = TerminalColor.FromIndex(code - 100 + 8) };
                    break;
            }

            i = next;
        }
    }

    private void SetUnderline(int kind)
    {
        const CellFlags all = CellFlags.Underline | CellFlags.DoubleUnderline | CellFlags.CurlyUnderline;
        var flag = kind switch
        {
            0 => CellFlags.None,
            2 => CellFlags.DoubleUnderline,
            3 => CellFlags.CurlyUnderline,
            _ => CellFlags.Underline,
        };
        _style = _style with { Flags = (_style.Flags & ~all) | flag };
    }

    /// <summary>
    /// Reads the colour after SGR 38/48/58 in either form: <c>38;5;n</c> / <c>38;2;r;g;b</c> or the colon form
    /// <c>38:5:n</c> / <c>38:2:[colourspace]:r:g:b</c>. Moves <paramref name="next"/> past the consumed arguments.
    /// </summary>
    private static TerminalColor? ReadExtendedColor(VtParams p, int i, ref int next)
    {
        if (next > i + 1)
        {
            // Colon form: the arguments are the sub-parameters i+1 .. next-1.
            var subCount = next - i - 1;
            return p.Get(i + 1, -1) switch
            {
                5 when subCount >= 2 => TerminalColor.FromIndex(p.Get(i + 2, 0)),
                2 when subCount >= 5 => ToRgb(p.Get(i + 3, 0), p.Get(i + 4, 0), p.Get(i + 5, 0)),
                2 when subCount == 4 => ToRgb(p.Get(i + 2, 0), p.Get(i + 3, 0), p.Get(i + 4, 0)),
                _ => null,
            };
        }

        switch (p.Get(i + 1, -1))
        {
            case 5 when i + 2 < p.Count:
                next = i + 3;
                return TerminalColor.FromIndex(p.Get(i + 2, 0));
            case 2 when i + 4 < p.Count:
                next = i + 5;
                return ToRgb(p.Get(i + 2, 0), p.Get(i + 3, 0), p.Get(i + 4, 0));
            default:
                next = p.Count;
                return null;
        }

        static TerminalColor ToRgb(int r, int g, int b) =>
            TerminalColor.FromRgb((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
    }
}
