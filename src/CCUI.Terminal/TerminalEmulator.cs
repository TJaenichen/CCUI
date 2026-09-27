using System.Text;
using CCUI.Terminal.Buffer;
using CCUI.Terminal.Parsing;

namespace CCUI.Terminal;

/// <summary>
/// A VT/xterm-compatible terminal: feed it the output of a pseudo console and it maintains the screen, scrollback,
/// cursor and modes. Thread model: one thread feeds, others read under <see cref="SyncRoot"/>. Events are raised on
/// the feeding thread after the lock is released.
/// </summary>
public sealed partial class TerminalEmulator : IVtHandler
{
    private const int DefaultTabWidth = 8;

    private readonly VtParser _parser;
    private readonly ScreenBuffer _main;
    private readonly ScreenBuffer _alternate;
    private readonly SavedCursor?[] _savedCursors = new SavedCursor?[2];
    private readonly Stack<string> _titleStack = new();
    private readonly StringBuilder _pendingReplies = new();
    private readonly List<(EventKind Kind, string? Text)> _pendingEvents = [];
    private bool[] _tabStops;
    private int _row;
    private int _col;
    private bool _wrapPending;
    private CellStyle _style;
    private int _top;
    private int _bottom;
    private Charset _g0;
    private Charset _g1;
    private bool _shiftOut;
    private (int Row, int Col)? _lastCell;
    private string? _lastPrinted;
    private long _version;

    public TerminalEmulator(int columns, int rows, int scrollbackLines = 10_000)
    {
        _parser = new VtParser(this);
        _main = new ScreenBuffer(columns, rows, scrollbackLines);
        _alternate = new ScreenBuffer(columns, rows, scrollbackCapacity: 0);
        Buffer = _main;
        _tabStops = CreateTabStops(Buffer.Columns);
        _bottom = Buffer.Rows - 1;
    }

    public event EventHandler<string>? Reply;

    public event EventHandler? TitleChanged;

    public event EventHandler? ProgressChanged;

    public event EventHandler? Bell;

    public event EventHandler<string>? ClipboardWriteRequested;

    public event EventHandler<string>? NotificationRequested;

    private enum Charset
    {
        Ascii,
        DecSpecialGraphics,
    }

    private enum EventKind
    {
        Title,
        Progress,
        Bell,
        Clipboard,
        Notification,
    }

    public object SyncRoot { get; } = new();

    /// <summary>The active buffer (main or alternate). Read under <see cref="SyncRoot"/>.</summary>
    public ScreenBuffer Buffer { get; private set; }

    public bool IsAlternateScreen => ReferenceEquals(Buffer, _alternate);

    public int Columns => Buffer.Columns;

    public int Rows => Buffer.Rows;

    public int CursorRow => _row;

    public int CursorColumn => _col;

    public TerminalModes Modes { get; } = new();

    public string Title { get; private set; } = string.Empty;

    public TerminalProgress Progress { get; private set; }

    /// <summary>The cursor style the application asked for, or null to use the configured default.</summary>
    public CursorStyle? RequestedCursorStyle { get; private set; }

    /// <summary>Used to answer OSC 10/11/12 colour queries.</summary>
    public TerminalPalette Palette { get; set; } = TerminalPalette.FromScheme(ColorScheme.Campbell);

    /// <summary>The renderer's cell size in pixels, used to answer XTWINOPS size queries. Zero if unknown.</summary>
    public (int Width, int Height) CellPixelSize { get; set; }

    /// <summary>Total lines ever pushed into scrollback. Lets a view keep its place while the scrollback ring drops lines.</summary>
    public long ScrolledOffCount { get; private set; }

    /// <summary>Increments whenever the screen may have changed. Safe to read without the lock.</summary>
    public long Version => Interlocked.Read(ref _version);

    public void Feed(ReadOnlySpan<byte> data)
    {
        lock (SyncRoot)
        {
            _parser.Advance(data);
            Interlocked.Increment(ref _version);
        }

        FlushEvents();
    }

    public void Feed(string text)
    {
        lock (SyncRoot)
        {
            _parser.Advance(text);
            Interlocked.Increment(ref _version);
        }

        FlushEvents();
    }

    public void Resize(int columns, int rows)
    {
        lock (SyncRoot)
        {
            if (columns == Columns && rows == Rows)
            {
                return;
            }

            var mainScrollback = _main.ScrollbackCount;
            var mainCursor = IsAlternateScreen ? _main.Rows - 1 : _row;
            var newMainRow = _main.Resize(columns, rows, mainCursor);
            ScrolledOffCount += Math.Max(0, _main.ScrollbackCount - mainScrollback);
            var newAltRow = _alternate.Resize(columns, rows, IsAlternateScreen ? _row : 0);

            _row = IsAlternateScreen ? newAltRow : newMainRow;
            _col = Math.Min(_col, Buffer.Columns - 1);
            _wrapPending = false;
            _lastCell = null;
            _top = 0;
            _bottom = Buffer.Rows - 1;
            ResizeTabStops(Buffer.Columns);
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>The screen as text, one line per row with trailing blanks trimmed. Mainly for tests and diagnostics.</summary>
    public string GetScreenText()
    {
        lock (SyncRoot)
        {
            var lines = new string[Rows];
            for (var r = 0; r < Rows; r++)
            {
                lines[r] = Buffer[r].GetText();
            }

            return string.Join('\n', lines).TrimEnd('\n');
        }
    }

    void IVtHandler.Print(int codePoint)
    {
        codePoint = Translate(codePoint);
        if (_lastCell is { } last && Graphemes.Extends(Buffer[last.Row][last.Col].Text, codePoint))
        {
            ExtendLastCell(last, codePoint);
            return;
        }

        var width = Graphemes.Width(codePoint);
        if (width <= 0)
        {
            // Unprintable, or a zero-width mark with nothing to attach to.
            return;
        }

        PrintCluster(char.ConvertFromUtf32(codePoint), width);
    }

    void IVtHandler.Execute(int controlCode)
    {
        _lastCell = null;
        switch (controlCode)
        {
            case 0x07:
                Queue(EventKind.Bell);
                break;
            case 0x08:
                _wrapPending = false;
                _col = Math.Max(0, _col - 1);
                break;
            case 0x09:
                _wrapPending = false;
                _col = NextTabStop(_col, 1);
                break;
            case 0x0A:
            case 0x0B:
            case 0x0C:
                _wrapPending = false;
                Index();
                if (Modes.LineFeedNewLine)
                {
                    _col = 0;
                }

                break;
            case 0x0D:
                _wrapPending = false;
                _col = 0;
                break;
            case 0x0E:
                _shiftOut = true;
                break;
            case 0x0F:
                _shiftOut = false;
                break;
        }
    }

    void IVtHandler.EscDispatch(ReadOnlySpan<char> intermediates, char final)
    {
        _lastCell = null;
        if (intermediates.IsEmpty)
        {
            switch (final)
            {
                case '7':
                    SaveCursor();
                    break;
                case '8':
                    RestoreCursor();
                    break;
                case 'D':
                    _wrapPending = false;
                    Index();
                    break;
                case 'E':
                    _wrapPending = false;
                    _col = 0;
                    Index();
                    break;
                case 'M':
                    _wrapPending = false;
                    ReverseIndex();
                    break;
                case 'H':
                    _tabStops[_col] = true;
                    break;
                case 'c':
                    FullReset();
                    break;
                case '=':
                    Modes.ApplicationKeypad = true;
                    break;
                case '>':
                    Modes.ApplicationKeypad = false;
                    break;
            }

            return;
        }

        if (intermediates.Length == 1)
        {
            switch (intermediates[0])
            {
                case '(':
                    _g0 = final == '0' ? Charset.DecSpecialGraphics : Charset.Ascii;
                    break;
                case ')':
                    _g1 = final == '0' ? Charset.DecSpecialGraphics : Charset.Ascii;
                    break;
                case '#' when final == '8':
                    ScreenAlignmentTest();
                    break;
            }
        }
    }

    private static bool[] CreateTabStops(int columns)
    {
        var stops = new bool[columns];
        for (var i = DefaultTabWidth; i < columns; i += DefaultTabWidth)
        {
            stops[i] = true;
        }

        return stops;
    }

    private void ResizeTabStops(int columns)
    {
        var old = _tabStops;
        _tabStops = CreateTabStops(columns);
        Array.Copy(old, _tabStops, Math.Min(old.Length, columns));
    }

    private int NextTabStop(int col, int count)
    {
        while (count-- > 0 && col < Columns - 1)
        {
            do
            {
                col++;
            }
            while (col < Columns - 1 && !_tabStops[col]);
        }

        return col;
    }

    private int PreviousTabStop(int col, int count)
    {
        while (count-- > 0 && col > 0)
        {
            do
            {
                col--;
            }
            while (col > 0 && !_tabStops[col]);
        }

        return col;
    }

    private void PrintCluster(string text, int width)
    {
        if (_wrapPending)
        {
            WrapToNextLine();
        }

        if (width == 2 && _col == Columns - 1)
        {
            if (Modes.AutoWrap && Columns > 1)
            {
                // A wide character never splits across lines: blank the last cell and wrap first.
                Buffer[_row].BreakWideCharsAt(_col, _col + 1);
                Buffer[_row][_col] = Cell.Blank(_style.ForErase());
                WrapToNextLine();
            }
            else
            {
                width = 1;
            }
        }

        var line = Buffer[_row];
        if (Modes.InsertMode)
        {
            line.InsertCells(_col, width, _style.ForErase());
        }

        line.BreakWideCharsAt(_col, _col + width);
        line[_col] = new Cell(text, _style, width == 2 ? CellWidth.WideLead : CellWidth.Normal);
        if (width == 2)
        {
            line[_col + 1] = new Cell(null, _style, CellWidth.WideTrail);
        }

        _lastCell = (_row, _col);
        _lastPrinted = text;
        AdvanceCursor(width);
    }

    private void ExtendLastCell((int Row, int Col) last, int codePoint)
    {
        var line = Buffer[last.Row];
        ref var cell = ref line[last.Col];
        var text = cell.Text + char.ConvertFromUtf32(codePoint);
        cell = cell with { Text = text };
        _lastPrinted = text;

        // An emoji presentation selector or a second regional indicator widens a one-column cluster.
        var cursorFollowsCell = !_wrapPending && _row == last.Row && _col == last.Col + 1;
        if (cell.Width == CellWidth.Normal && Graphemes.ClusterWidth(text) == 2 && cursorFollowsCell && last.Col + 1 < Columns)
        {
            line.BreakWideCharsAt(last.Col + 1, last.Col + 2);
            cell = cell with { Width = CellWidth.WideLead };
            line[last.Col + 1] = new Cell(null, cell.Style, CellWidth.WideTrail);
            AdvanceCursor(1);
        }
    }

    private void AdvanceCursor(int columns)
    {
        _col += columns;
        if (_col >= Columns)
        {
            _col = Columns - 1;
            _wrapPending = Modes.AutoWrap;
        }
    }

    private void WrapToNextLine()
    {
        _wrapPending = false;
        if (!Modes.AutoWrap)
        {
            return;
        }

        Buffer[_row].IsWrapped = true;
        _col = 0;
        Index();
    }

    private void Index()
    {
        if (_row == _bottom)
        {
            var toScrollback = !IsAlternateScreen && _top == 0 && _bottom == Rows - 1;
            Buffer.ScrollUp(_top, _bottom, 1, _style, toScrollback);
            if (toScrollback)
            {
                ScrolledOffCount++;
            }
        }
        else if (_row < Rows - 1)
        {
            _row++;
        }
    }

    private void ReverseIndex()
    {
        if (_row == _top)
        {
            Buffer.ScrollDown(_top, _bottom, 1, _style);
        }
        else if (_row > 0)
        {
            _row--;
        }
    }

    private int Translate(int codePoint)
    {
        var charset = _shiftOut ? _g1 : _g0;
        if (charset != Charset.DecSpecialGraphics || codePoint is < 0x5F or > 0x7E)
        {
            return codePoint;
        }

        return " ◆▒␉␌␍␊°±␤␋┘┐┌└┼⎺⎻─⎼⎽├┤┴┬│≤≥π≠£·"[codePoint - 0x5F];
    }

    private void SaveCursor()
    {
        _savedCursors[IsAlternateScreen ? 1 : 0] = new SavedCursor(_row, _col, _wrapPending, _style, Modes.OriginMode, Modes.AutoWrap, _g0, _g1, _shiftOut);
    }

    private void RestoreCursor()
    {
        var saved = _savedCursors[IsAlternateScreen ? 1 : 0] ?? new SavedCursor(0, 0, false, default, false, true, Charset.Ascii, Charset.Ascii, false);
        _row = Math.Min(saved.Row, Rows - 1);
        _col = Math.Min(saved.Col, Columns - 1);
        _wrapPending = saved.WrapPending;
        _style = saved.Style;
        Modes.OriginMode = saved.OriginMode;
        Modes.AutoWrap = saved.AutoWrap;
        _g0 = saved.G0;
        _g1 = saved.G1;
        _shiftOut = saved.ShiftOut;
    }

    private void SwitchScreen(bool alternate, bool clearAlternate)
    {
        if (alternate == IsAlternateScreen)
        {
            if (alternate && clearAlternate)
            {
                _alternate.ClearScreen(_style);
            }

            return;
        }

        Buffer = alternate ? _alternate : _main;
        if (alternate && clearAlternate)
        {
            _alternate.ClearScreen(_style);
        }

        _row = Math.Min(_row, Rows - 1);
        _col = Math.Min(_col, Columns - 1);
        _top = 0;
        _bottom = Rows - 1;
        _wrapPending = false;
    }

    private void ScreenAlignmentTest()
    {
        for (var r = 0; r < Rows; r++)
        {
            Buffer[r].Fill(0, Columns, new Cell("E", default));
        }

        _row = 0;
        _col = 0;
        _top = 0;
        _bottom = Rows - 1;
    }

    private void SoftReset()
    {
        Modes.CursorVisible = true;
        Modes.InsertMode = false;
        Modes.OriginMode = false;
        Modes.AutoWrap = true;
        Modes.ApplicationCursorKeys = false;
        Modes.ApplicationKeypad = false;
        _style = default;
        _top = 0;
        _bottom = Rows - 1;
        _g0 = Charset.Ascii;
        _g1 = Charset.Ascii;
        _shiftOut = false;
        _wrapPending = false;
        _savedCursors[0] = null;
        _savedCursors[1] = null;
        RequestedCursorStyle = null;
    }

    private void FullReset()
    {
        SwitchScreen(alternate: false, clearAlternate: false);
        SoftReset();
        Modes.Reset();
        _main.ClearScreen(default);
        _main.Scrollback?.Clear();
        _alternate.ClearScreen(default);
        _tabStops = CreateTabStops(Columns);
        _row = 0;
        _col = 0;
        _lastPrinted = null;
        _titleStack.Clear();
        SetTitle(string.Empty);
        SetProgress(default);
    }

    private void SetTitle(string title)
    {
        if (Title != title)
        {
            Title = title;
            Queue(EventKind.Title);
        }
    }

    private void SetProgress(TerminalProgress progress)
    {
        if (Progress != progress)
        {
            Progress = progress;
            Queue(EventKind.Progress);
        }
    }

    private void SendReply(string reply) => _pendingReplies.Append(reply);

    private void Queue(EventKind kind, string? text = null) => _pendingEvents.Add((kind, text));

    private void FlushEvents()
    {
        string? replies = null;
        (EventKind Kind, string? Text)[] events;
        lock (SyncRoot)
        {
            if (_pendingReplies.Length > 0)
            {
                replies = _pendingReplies.ToString();
                _pendingReplies.Clear();
            }

            if (_pendingEvents.Count == 0 && replies is null)
            {
                return;
            }

            events = [.. _pendingEvents];
            _pendingEvents.Clear();
        }

        if (replies is not null)
        {
            Reply?.Invoke(this, replies);
        }

        var titleRaised = false;
        var progressRaised = false;
        foreach (var (kind, text) in events)
        {
            switch (kind)
            {
                case EventKind.Title when !titleRaised:
                    titleRaised = true;
                    TitleChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case EventKind.Progress when !progressRaised:
                    progressRaised = true;
                    ProgressChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case EventKind.Bell:
                    Bell?.Invoke(this, EventArgs.Empty);
                    break;
                case EventKind.Clipboard:
                    ClipboardWriteRequested?.Invoke(this, text!);
                    break;
                case EventKind.Notification:
                    NotificationRequested?.Invoke(this, text!);
                    break;
            }
        }
    }

    private sealed record SavedCursor(int Row, int Col, bool WrapPending, CellStyle Style, bool OriginMode, bool AutoWrap, Charset G0, Charset G1, bool ShiftOut);
}
