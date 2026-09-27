using System.Text;

namespace CCUI.Terminal.Buffer;

/// <summary>A row of cells. <see cref="IsWrapped"/> marks a row that continues onto the next one (soft wrap).</summary>
public sealed class TerminalLine
{
    private Cell[] _cells;

    public TerminalLine(int columns, CellStyle fill = default)
    {
        _cells = new Cell[columns];
        if (fill != default)
        {
            Array.Fill(_cells, Cell.Blank(fill));
        }
    }

    public int Length => _cells.Length;

    public bool IsWrapped { get; set; }

    public ref Cell this[int column] => ref _cells[column];

    public ReadOnlySpan<Cell> Cells => _cells;

    public void Fill(int start, int end, Cell cell)
    {
        start = Math.Max(0, start);
        end = Math.Min(_cells.Length, end);
        if (start < end)
        {
            _cells.AsSpan(start, end - start).Fill(cell);
        }
    }

    public void Clear(CellStyle fill)
    {
        Fill(0, _cells.Length, Cell.Blank(fill));
        IsWrapped = false;
    }

    public void Resize(int columns)
    {
        if (columns == _cells.Length)
        {
            return;
        }

        Array.Resize(ref _cells, columns);
        if (columns > 0 && _cells[columns - 1].Width == CellWidth.WideLead)
        {
            _cells[columns - 1] = Cell.Blank(_cells[columns - 1].Style);
        }
    }

    /// <summary>Inserts <paramref name="count"/> blank cells at <paramref name="column"/>; cells pushed past the edge are lost.</summary>
    public void InsertCells(int column, int count, CellStyle fill)
    {
        if (column >= _cells.Length || count <= 0)
        {
            return;
        }

        count = Math.Min(count, _cells.Length - column);
        Array.Copy(_cells, column, _cells, column + count, _cells.Length - column - count);
        Fill(column, column + count, Cell.Blank(fill));
        RepairWideEdge();
    }

    /// <summary>Deletes <paramref name="count"/> cells at <paramref name="column"/>, shifting the rest left.</summary>
    public void DeleteCells(int column, int count, CellStyle fill)
    {
        if (column >= _cells.Length || count <= 0)
        {
            return;
        }

        count = Math.Min(count, _cells.Length - column);
        Array.Copy(_cells, column + count, _cells, column, _cells.Length - column - count);
        Fill(_cells.Length - count, _cells.Length, Cell.Blank(fill));
        if (_cells[column].Width == CellWidth.WideTrail)
        {
            _cells[column] = Cell.Blank(_cells[column].Style);
        }
    }

    /// <summary>
    /// Before cells [start, end) are overwritten, blanks the other half of any wide character that straddles the range edges.
    /// </summary>
    public void BreakWideCharsAt(int start, int end)
    {
        if (start > 0 && start < _cells.Length && _cells[start].Width == CellWidth.WideTrail)
        {
            _cells[start - 1] = Cell.Blank(_cells[start - 1].Style);
        }

        if (end > 0 && end < _cells.Length && _cells[end - 1].Width == CellWidth.WideLead)
        {
            _cells[end] = Cell.Blank(_cells[end].Style);
        }
    }

    public TerminalLine Clone()
    {
        var copy = new TerminalLine(0) { IsWrapped = IsWrapped };
        copy._cells = (Cell[])_cells.Clone();
        return copy;
    }

    /// <summary>The text of columns [start, end), without trailing blanks. Wide trails contribute nothing.</summary>
    public string GetText(int start = 0, int end = int.MaxValue)
    {
        end = Math.Min(end, _cells.Length);
        var sb = new StringBuilder(Math.Max(0, end - start));
        for (var i = Math.Max(0, start); i < end; i++)
        {
            if (_cells[i].Width != CellWidth.WideTrail)
            {
                sb.Append(_cells[i].DisplayText);
            }
        }

        return sb.ToString().TrimEnd(' ');
    }

    public override string ToString() => GetText();

    private void RepairWideEdge()
    {
        var last = _cells.Length - 1;
        if (last >= 0 && _cells[last].Width == CellWidth.WideLead)
        {
            _cells[last] = Cell.Blank(_cells[last].Style);
        }
    }
}
