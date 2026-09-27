using CCUI.Terminal.Buffer;

namespace CCUI.Terminal.Wpf.Rendering;

/// <summary>A copy of the visible cells taken under the emulator lock, so drawing never holds the lock.</summary>
internal sealed class TerminalFrame
{
    public int Columns { get; private set; }

    public int Rows { get; private set; }

    public Cell[][] Lines { get; private set; } = [];

    /// <summary>Absolute buffer index of the first visible line.</summary>
    public int TopLine { get; set; }

    /// <summary>Cursor row within the viewport, or -1 when it is scrolled out of view.</summary>
    public int CursorRow { get; set; } = -1;

    public int CursorColumn { get; set; }

    public bool CursorVisible { get; set; }

    public bool CursorOnWideCell { get; set; }

    public CursorStyle? RequestedCursorStyle { get; set; }

    public void EnsureSize(int columns, int rows)
    {
        if (columns == Columns && rows == Rows)
        {
            return;
        }

        Columns = columns;
        Rows = rows;
        Lines = new Cell[rows][];
        for (var i = 0; i < rows; i++)
        {
            Lines[i] = new Cell[columns];
        }
    }

    public void CopyLine(int row, TerminalLine line)
    {
        var target = Lines[row];
        var cells = line.Cells;
        var count = Math.Min(cells.Length, target.Length);
        cells[..count].CopyTo(target);
        if (count < target.Length)
        {
            Array.Clear(target, count, target.Length - count);
        }
    }
}
