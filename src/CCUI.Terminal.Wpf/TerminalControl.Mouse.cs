using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using CCUI.Terminal.Buffer;
using CCUI.Terminal.Input;

namespace CCUI.Terminal.Wpf;

public partial class TerminalControl
{
    private enum SelectionUnit
    {
        Character,
        Word,
        Line,
    }

    // Selection endpoints in monotonic line numbers (see the viewport fields), so they survive scrollback trimming.
    private (long Line, int Column)? _selectionAnchorStart;
    private (long Line, int Column)? _selectionAnchorEnd;
    private (long Line, int Column)? _selectionStart;
    private (long Line, int Column)? _selectionEnd;
    private SelectionUnit _selectionUnit;
    private bool _selecting;

    // Where the button went down, and whether the mouse has moved far enough from there to count as a drag.
    private Point _selectionOrigin;
    private bool _selectionDragged;
    private int _wheelDelta;

    // The link under the mouse while Ctrl is held, in monotonic line numbers like the selection.
    private (string Uri, (long Line, int Column) Start, (long Line, int Column) End)? _hoverLink;

    public bool HasSelection => _selectionStart is not null && _selectionEnd is not null;

    public void ClearSelection()
    {
        if (_selectionStart is null && _selectionAnchorStart is null)
        {
            return;
        }

        _selectionStart = _selectionEnd = _selectionAnchorStart = _selectionAnchorEnd = null;
        RenderOverlay();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (Session is null || _surface is null)
        {
            return;
        }

        var point = e.GetPosition(_surface);
        if (HitCell(point) is not { } hit)
        {
            return;
        }

        // As in Windows Terminal: Ctrl+click opens a URL instead of starting a selection.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.ClickCount == 1 && LinkAt(hit) is { } link)
        {
            OpenLink(link.Uri);
            e.Handled = true;
            return;
        }

        BeginSelection(point, e.ClickCount);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHoverLink(e);
        if (_selecting && _surface is not null)
        {
            ExtendSelection(e.GetPosition(_surface));
        }
    }

    /// <summary>The button went down at <paramref name="point"/> (relative to the text surface).</summary>
    internal void BeginSelection(Point point, int clickCount)
    {
        if (HitCell(point) is not { } hit)
        {
            return;
        }

        _selectionUnit = clickCount switch
        {
            2 => SelectionUnit.Word,
            >= 3 => SelectionUnit.Line,
            _ => SelectionUnit.Character,
        };

        var (start, end) = Expand(hit);
        _selectionAnchorStart = start;
        _selectionAnchorEnd = end;
        _selectionStart = _selectionUnit == SelectionUnit.Character ? null : start;
        _selectionEnd = _selectionUnit == SelectionUnit.Character ? null : end;
        _selectionOrigin = point;
        _selectionDragged = _selectionUnit != SelectionUnit.Character;
        _selecting = true;
        RenderOverlay();
    }

    /// <summary>The mouse moved to <paramref name="point"/> (relative to the text surface) with the button down.</summary>
    internal void ExtendSelection(Point point)
    {
        if (!_selecting || _selectionAnchorStart is not { } anchorStart || _selectionAnchorEnd is not { } anchorEnd || HitCell(point) is not { } hit)
        {
            return;
        }

        // A plain click is not a selection: WPF raises MouseMove when the mouse is captured, and a hand rarely holds
        // perfectly still. Without this, every click selected one cell, and the next Ctrl+C or right-click copied
        // that cell instead of reaching the application or pasting.
        if (!_selectionDragged)
        {
            var moved = point - _selectionOrigin;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _selectionDragged = true;
        }

        var (start, end) = Expand(hit);
        if (Compare(start, anchorStart) < 0)
        {
            _selectionStart = start;
            _selectionEnd = anchorEnd;
        }
        else
        {
            _selectionStart = anchorStart;
            _selectionEnd = end;
        }

        RenderOverlay();
    }

    /// <summary>The button was released; the selection (if any) stays.</summary>
    internal void EndSelection() => _selecting = false;

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_selecting)
        {
            return;
        }

        EndSelection();
        ReleaseMouseCapture();
        if (HasSelection && CopyOnSelect)
        {
            CopySelection();
        }

        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        Focus();

        // As in Windows Terminal: right-click copies a selection, or pastes when there is none.
        if (HasSelection)
        {
            CopySelection();
            ClearSelection();
        }
        else
        {
            PasteFromClipboard();
        }

        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Session is not { } session)
        {
            return;
        }

        // Precision touchpads send many small deltas; add them up and act on whole notches.
        _wheelDelta += e.Delta;
        var notches = _wheelDelta / 120;
        _wheelDelta -= notches * 120;
        if (notches == 0)
        {
            e.Handled = true;
            return;
        }

        var modes = session.Emulator.Modes;
        if (modes.MouseTracking != MouseTrackingMode.None && modes.SgrMouse && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && HitCell(e) is { } hit)
        {
            // SGR mouse wheel report: button 64 (up) / 65 (down), 1-based column and row.
            var row = hit.Line - (_droppedLines + _frame.TopLine) + 1;
            var button = notches > 0 ? 64 : 65;
            for (var i = 0; i < Math.Abs(notches); i++)
            {
                session.SendText(string.Create(CultureInfo.InvariantCulture, $"\e[<{button};{hit.Column + 1};{row}M"));
            }
        }
        else if (session.Emulator.IsAlternateScreen && modes.AlternateScroll)
        {
            var arrow = KeyEncoder.Encode(notches > 0 ? TerminalKey.Up : TerminalKey.Down, KeyModifiers.None, modes.ApplicationCursorKeys);
            for (var i = 0; i < Math.Abs(notches) * WheelScrollLines; i++)
            {
                session.SendText(arrow!);
            }
        }
        else
        {
            ScrollLines(notches * WheelScrollLines);
        }

        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SetHoverLink(null);
    }

    // Ctrl pressed or released over a link shows or hides its underline without waiting for the mouse to move.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            UpdateHoverLink(null);
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            UpdateHoverLink(null);
        }
    }

    private static void OpenLink(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No handler for the scheme, or the shell refused it: nothing sensible to do from inside the terminal.
            Debug.WriteLine($"Could not open {uri}: {ex.Message}");
        }
    }

    private void UpdateHoverLink(MouseEventArgs? e)
    {
        var point = _surface is null ? default : e?.GetPosition(_surface) ?? Mouse.GetPosition(_surface);
        var overText = _surface is not null && point.X >= 0 && point.Y >= 0 && point.X < _surface.ActualWidth && point.Y < _surface.ActualHeight;
        var hit = overText && !_selecting && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? HitCell(point) : null;
        SetHoverLink(hit is { } cell ? LinkAt(cell) : null);
    }

    private void SetHoverLink((string Uri, (long Line, int Column) Start, (long Line, int Column) End)? link)
    {
        if (link == _hoverLink)
        {
            return;
        }

        _hoverLink = link;
        Cursor = link is null ? null : Cursors.Hand;
        ToolTip = link is null ? null : link.Value.Uri + Environment.NewLine + "Ctrl+click to open";
        RenderOverlay();
    }

    private (string Uri, (long Line, int Column) Start, (long Line, int Column) End)? LinkAt((long Line, int Column) hit)
    {
        if (Session is not { } session)
        {
            return null;
        }

        lock (session.Emulator.SyncRoot)
        {
            var absolute = hit.Line - _droppedLines;
            if (absolute < 0 || absolute > int.MaxValue
                || BufferLinks.LinkAt(session.Emulator.Buffer, new BufferPosition((int)absolute, hit.Column)) is not { } link)
            {
                return null;
            }

            return (link.Uri, (link.Start.Line + _droppedLines, link.Start.Column), (link.End.Line + _droppedLines, link.End.Column));
        }
    }

    private List<(int Row, int StartColumn, int EndColumn)> LinkSpans() =>
        _hoverLink is { } link ? Spans(link.Start, link.End) : [];

    private static int Compare((long Line, int Column) a, (long Line, int Column) b) =>
        a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Column.CompareTo(b.Column);

    private (long Line, int Column)? HitCell(MouseEventArgs e) => _surface is null ? null : HitCell(e.GetPosition(_surface));

    private (long Line, int Column)? HitCell(Point point)
    {
        if (_surface?.Renderer is not { } renderer || _frame.Rows == 0)
        {
            return null;
        }

        var column = Math.Clamp((int)Math.Floor(point.X / renderer.Metrics.Width), 0, _frame.Columns - 1);
        var row = Math.Clamp((int)Math.Floor(point.Y / renderer.Metrics.Height), 0, _frame.Rows - 1);
        return (_droppedLines + _frame.TopLine + row, column);
    }

    /// <summary>Expands a hit cell to the current selection unit.</summary>
    private ((long Line, int Column) Start, (long Line, int Column) End) Expand((long Line, int Column) hit)
    {
        switch (_selectionUnit)
        {
            case SelectionUnit.Line:
                return ((hit.Line, 0), (hit.Line, Math.Max(0, _frame.Columns - 1)));
            case SelectionUnit.Word when Session is { } session:
                lock (session.Emulator.SyncRoot)
                {
                    var buffer = session.Emulator.Buffer;
                    var absolute = hit.Line - _droppedLines;
                    if (absolute >= 0 && absolute < buffer.TotalLines)
                    {
                        var (start, end) = BufferText.WordAt(buffer.GetLine((int)absolute), hit.Column);
                        return ((hit.Line, start), (hit.Line, end));
                    }
                }

                break;
        }

        return (hit, hit);
    }

    private string SelectedText()
    {
        if (_selectionStart is not { } start || _selectionEnd is not { } end || Session is not { } session)
        {
            return string.Empty;
        }

        lock (session.Emulator.SyncRoot)
        {
            var buffer = session.Emulator.Buffer;
            return BufferText.Extract(
                buffer,
                new BufferPosition((int)Math.Max(0, start.Line - _droppedLines), start.Column),
                new BufferPosition((int)Math.Max(0, end.Line - _droppedLines), end.Column));
        }
    }

    private List<(int Row, int StartColumn, int EndColumn)> SelectionSpans() =>
        _selectionStart is { } start && _selectionEnd is { } end ? Spans(start, end) : [];

    /// <summary>The visible row spans covering <paramref name="start"/> to <paramref name="end"/> (inclusive).</summary>
    private List<(int Row, int StartColumn, int EndColumn)> Spans((long Line, int Column) start, (long Line, int Column) end)
    {
        var spans = new List<(int, int, int)>();
        if (_frame.Rows == 0)
        {
            return spans;
        }

        var top = _droppedLines + _frame.TopLine;
        for (var row = 0; row < _frame.Rows; row++)
        {
            var line = top + row;
            if (line < start.Line || line > end.Line)
            {
                continue;
            }

            var from = line == start.Line ? start.Column : 0;
            var to = line == end.Line ? end.Column : _frame.Columns - 1;
            if (to >= from)
            {
                spans.Add((row, from, to));
            }
        }

        return spans;
    }
}
