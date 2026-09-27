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
        if (Session is null || HitCell(e) is not { } hit)
        {
            return;
        }

        _selectionUnit = e.ClickCount switch
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
        _selecting = true;
        CaptureMouse();
        RenderOverlay();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_selecting || _selectionAnchorStart is not { } anchorStart || _selectionAnchorEnd is not { } anchorEnd || HitCell(e) is not { } hit)
        {
            return;
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

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_selecting)
        {
            return;
        }

        _selecting = false;
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

        var notches = e.Delta / 120;
        if (notches == 0)
        {
            notches = Math.Sign(e.Delta);
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

    private static int Compare((long Line, int Column) a, (long Line, int Column) b) =>
        a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Column.CompareTo(b.Column);

    private (long Line, int Column)? HitCell(MouseEventArgs e)
    {
        if (_surface?.Renderer is not { } renderer || _frame.Rows == 0)
        {
            return null;
        }

        var point = e.GetPosition(_surface);
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

    private List<(int Row, int StartColumn, int EndColumn)> SelectionSpans()
    {
        var spans = new List<(int, int, int)>();
        if (_selectionStart is not { } start || _selectionEnd is not { } end || _frame.Rows == 0)
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
