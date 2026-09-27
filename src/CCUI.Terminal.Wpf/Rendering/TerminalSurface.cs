using System.Windows;
using System.Windows.Media;
using CCUI.Terminal.Buffer;

namespace CCUI.Terminal.Wpf.Rendering;

/// <summary>What the overlay draws on top of the text: selection spans and the cursor.</summary>
internal readonly record struct OverlayState(
    IReadOnlyList<(int Row, int StartColumn, int EndColumn)> Selection,
    CursorShape CursorShape,
    bool CursorOn,
    bool Focused);

/// <summary>
/// The drawing surface: one DrawingVisual per row, re-rendered only when that row's cells change, plus an overlay
/// visual for selection and cursor so blinking never redraws text.
/// </summary>
internal sealed class TerminalSurface : FrameworkElement
{
    private readonly VisualCollection _children;
    private readonly List<RowVisual> _rows = [];
    private readonly DrawingVisual _overlay = new();
    private RowRenderer? _renderer;
    private TerminalFrame? _lastFrame;

    public TerminalSurface()
    {
        _children = new VisualCollection(this) { _overlay };
    }

    public RowRenderer? Renderer
    {
        get => _renderer;
        set
        {
            _renderer = value;
            foreach (var row in _rows)
            {
                row.Invalidate();
            }
        }
    }

    protected override int VisualChildrenCount => _children.Count;

    public void Render(TerminalFrame frame, OverlayState overlay)
    {
        if (_renderer is null)
        {
            return;
        }

        while (_rows.Count < frame.Rows)
        {
            var row = new RowVisual();
            _rows.Add(row);
            _children.Insert(_rows.Count - 1, row);
        }

        while (_rows.Count > frame.Rows)
        {
            _children.Remove(_rows[^1]);
            _rows.RemoveAt(_rows.Count - 1);
        }

        for (var r = 0; r < frame.Rows; r++)
        {
            var row = _rows[r];
            var cells = frame.Lines[r];
            if (row.Shows(cells))
            {
                continue;
            }

            using (var dc = row.RenderOpen())
            {
                _renderer.DrawRow(dc, cells, r * _renderer.Metrics.Height);
            }

            row.Remember(cells);
        }

        _lastFrame = frame;
        RenderOverlay(overlay);
    }

    public void RenderOverlay(OverlayState overlay)
    {
        if (_renderer is not { } renderer || _lastFrame is not { } frame)
        {
            return;
        }

        var m = renderer.Metrics;
        using var dc = _overlay.RenderOpen();
        if (overlay.Selection.Count > 0)
        {
            var brush = renderer.Brushes.Get(renderer.Palette.SelectionBackground, 0x60);
            foreach (var (row, start, end) in overlay.Selection)
            {
                dc.DrawRectangle(brush, null, new Rect(start * m.Width, row * m.Height, (end - start + 1) * m.Width, m.Height));
            }
        }

        if (frame.CursorRow < 0 || !frame.CursorVisible || !overlay.CursorOn)
        {
            return;
        }

        var cursorBrush = renderer.Brushes.Get(renderer.Palette.Cursor);
        var width = (frame.CursorOnWideCell ? 2 : 1) * m.Width;
        var cell = new Rect(frame.CursorColumn * m.Width, frame.CursorRow * m.Height, width, m.Height);
        var thin = Math.Max(1 / m.PixelsPerDip, m.Snap(m.Width * 0.12));
        if (!overlay.Focused)
        {
            var half = thin / 2;
            dc.DrawRectangle(null, new Pen(cursorBrush, thin), new Rect(cell.X + half, cell.Y + half, cell.Width - thin, cell.Height - thin));
            return;
        }

        var shape = frame.RequestedCursorStyle?.Shape ?? overlay.CursorShape;
        switch (shape)
        {
            case CursorShape.Bar:
                dc.DrawRectangle(cursorBrush, null, new Rect(cell.X, cell.Y, thin, cell.Height));
                break;
            case CursorShape.Underline:
                dc.DrawRectangle(cursorBrush, null, new Rect(cell.X, cell.Bottom - thin, cell.Width, thin));
                break;
            default:
                dc.DrawRectangle(renderer.Brushes.Get(renderer.Palette.Cursor, 0xB0), null, cell);
                break;
        }
    }

    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize) => default;

    // A transparent fill makes the whole surface hit-testable for mouse selection.
    protected override void OnRender(DrawingContext drawingContext) =>
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

    private sealed class RowVisual : DrawingVisual
    {
        private Cell[]? _shown;

        public bool Shows(Cell[] cells) => _shown is not null && cells.AsSpan().SequenceEqual(_shown);

        public void Remember(Cell[] cells)
        {
            if (_shown is null || _shown.Length != cells.Length)
            {
                _shown = new Cell[cells.Length];
            }

            cells.CopyTo(_shown, 0);
        }

        public void Invalidate() => _shown = null;
    }
}
