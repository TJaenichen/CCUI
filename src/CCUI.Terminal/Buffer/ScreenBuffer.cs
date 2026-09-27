namespace CCUI.Terminal.Buffer;

/// <summary>
/// The visible grid plus (for the main screen) its scrollback. Absolute line indexes run from the oldest
/// scrollback line (0) to the bottom screen row (<see cref="TotalLines"/> - 1).
/// </summary>
public sealed class ScreenBuffer
{
    private TerminalLine[] _rows;

    public ScreenBuffer(int columns, int rows, int scrollbackCapacity)
    {
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        Scrollback = scrollbackCapacity > 0 ? new Scrollback(scrollbackCapacity) : null;
        _rows = new TerminalLine[Rows];
        for (var i = 0; i < Rows; i++)
        {
            _rows[i] = new TerminalLine(Columns);
        }
    }

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    public Scrollback? Scrollback { get; }

    public int ScrollbackCount => Scrollback?.Count ?? 0;

    public int TotalLines => ScrollbackCount + Rows;

    /// <summary>A screen row (0 = top of the visible screen).</summary>
    public TerminalLine this[int row] => _rows[row];

    /// <summary>A line by absolute index across scrollback and screen.</summary>
    public TerminalLine GetLine(int absoluteIndex)
    {
        var scrollback = ScrollbackCount;
        return absoluteIndex < scrollback ? Scrollback![absoluteIndex] : _rows[absoluteIndex - scrollback];
    }

    /// <summary>Scrolls rows [top, bottom] up by <paramref name="count"/>, optionally saving the lines that leave into scrollback.</summary>
    public void ScrollUp(int top, int bottom, int count, CellStyle fill, bool saveToScrollback)
    {
        count = Math.Min(count, bottom - top + 1);
        if (count <= 0)
        {
            return;
        }

        var leaving = new TerminalLine[count];
        Array.Copy(_rows, top, leaving, 0, count);
        Array.Copy(_rows, top + count, _rows, top, bottom - top + 1 - count);
        for (var i = 0; i < count; i++)
        {
            var row = bottom - count + 1 + i;
            if (saveToScrollback && Scrollback is not null)
            {
                Scrollback.Add(leaving[i]);
                _rows[row] = new TerminalLine(Columns, fill.ForErase());
            }
            else
            {
                leaving[i].Clear(fill.ForErase());
                _rows[row] = leaving[i];
            }
        }
    }

    /// <summary>Scrolls rows [top, bottom] down by <paramref name="count"/>; lines pushed past the bottom are discarded.</summary>
    public void ScrollDown(int top, int bottom, int count, CellStyle fill)
    {
        count = Math.Min(count, bottom - top + 1);
        if (count <= 0)
        {
            return;
        }

        var leaving = new TerminalLine[count];
        Array.Copy(_rows, bottom - count + 1, leaving, 0, count);
        Array.Copy(_rows, top, _rows, top + count, bottom - top + 1 - count);
        for (var i = 0; i < count; i++)
        {
            leaving[i].Clear(fill.ForErase());
            _rows[top + i] = leaving[i];
        }
    }

    public void ClearScreen(CellStyle fill)
    {
        foreach (var row in _rows)
        {
            row.Clear(fill.ForErase());
        }
    }

    /// <summary>
    /// Resizes the grid without reflowing. When the screen shrinks, rows above the cursor move into scrollback so the
    /// cursor line stays visible; when it grows, blank rows are added at the bottom. Returns the cursor's new row.
    /// </summary>
    public int Resize(int columns, int rows, int cursorRow)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);

        if (columns != Columns)
        {
            foreach (var row in _rows)
            {
                row.Resize(columns);
            }

            Columns = columns;
        }

        if (rows < Rows)
        {
            // Drop blank rows from the bottom first, then push rows off the top into history.
            var list = new List<TerminalLine>(_rows);
            var excess = Rows - rows;
            while (excess > 0 && list.Count - 1 > cursorRow && IsBlank(list[^1]))
            {
                list.RemoveAt(list.Count - 1);
                excess--;
            }

            while (excess > 0)
            {
                Scrollback?.Add(list[0]);
                list.RemoveAt(0);
                cursorRow--;
                excess--;
            }

            _rows = [.. list];
        }
        else if (rows > Rows)
        {
            // Grow at the bottom. Pulling history back in would fight ConPTY, which repaints its viewport after a resize.
            var list = new List<TerminalLine>(_rows);
            for (var i = Rows; i < rows; i++)
            {
                list.Add(new TerminalLine(columns));
            }

            _rows = [.. list];
        }

        Rows = rows;
        return Math.Clamp(cursorRow, 0, rows - 1);
    }

    private static bool IsBlank(TerminalLine line)
    {
        foreach (var cell in line.Cells)
        {
            if (!cell.IsBlank || !cell.Style.Background.IsDefault)
            {
                return false;
            }
        }

        return true;
    }
}
