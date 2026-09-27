using System.Windows;
using System.Windows.Media;
using CCUI.Terminal.Buffer;

namespace CCUI.Terminal.Wpf.Rendering;

/// <summary>Draws one row of cells: backgrounds, then text (glyph runs, built-in glyphs, colour emoji), then decorations.</summary>
internal sealed class RowRenderer
{
    private readonly FontFallback _fonts;
    private readonly BrushCache _brushes = new();
    private readonly ColorEmojiRasterizer? _emoji;
    private readonly RenderSettings _settings;
    private readonly GlyphRunBuilder _run;

    public RowRenderer(CellMetrics metrics, FontFallback fonts, ColorEmojiRasterizer? emoji, RenderSettings settings)
    {
        Metrics = metrics;
        _fonts = fonts;
        _emoji = settings.ColorEmoji ? emoji : null;
        _settings = settings;
        _run = new GlyphRunBuilder(metrics);
    }

    public CellMetrics Metrics { get; }

    public TerminalPalette Palette => _settings.Palette;

    public BrushCache Brushes => _brushes;

    public void DrawRow(DrawingContext dc, ReadOnlySpan<Cell> cells, double y)
    {
        DrawBackgrounds(dc, cells, y);
        DrawText(dc, cells, y);
        DrawDecorations(dc, cells, y);
    }

    private (Rgb Foreground, Rgb? Background) Resolve(CellStyle style)
    {
        var foreground = style.Foreground;
        if (style.Has(CellFlags.Bold)
            && _settings.IntenseTextStyle is IntenseTextStyle.All or IntenseTextStyle.Bright
            && foreground.Kind == TerminalColorKind.Indexed
            && foreground.Index < 8)
        {
            foreground = TerminalColor.FromIndex(foreground.Index + 8);
        }

        var fg = _settings.Palette.Resolve(foreground, foreground: true);
        Rgb? bg = style.Background.IsDefault ? null : _settings.Palette.Resolve(style.Background, foreground: false);
        return style.Has(CellFlags.Inverse) ? (bg ?? _settings.Palette.Background, fg) : (fg, bg);
    }

    private SolidColorBrush ForegroundBrush(CellStyle style) =>
        _brushes.Get(Resolve(style).Foreground, style.Has(CellFlags.Faint) ? (byte)140 : (byte)255);

    private void DrawBackgrounds(DrawingContext dc, ReadOnlySpan<Cell> cells, double y)
    {
        var column = 0;
        while (column < cells.Length)
        {
            var background = Resolve(cells[column].Style).Background;
            var start = column;
            while (++column < cells.Length && Resolve(cells[column].Style).Background == background)
            {
            }

            if (background is { } color)
            {
                dc.DrawRectangle(_brushes.Get(color), null, new Rect(start * Metrics.Width, y, (column - start) * Metrics.Width, Metrics.Height));
            }
        }
    }

    private void DrawText(DrawingContext dc, ReadOnlySpan<Cell> cells, double y)
    {
        var boldFont = _settings.IntenseTextStyle is IntenseTextStyle.All or IntenseTextStyle.Bold;
        _run.Begin(dc, y + Metrics.Baseline);
        for (var column = 0; column < cells.Length; column++)
        {
            var cell = cells[column];
            if (cell.Width == CellWidth.WideTrail)
            {
                continue;
            }

            var x = column * Metrics.Width;
            if (cell.IsBlank || cell.Style.Has(CellFlags.Invisible))
            {
                _run.AddSpace(x, Metrics.Width);
                continue;
            }

            var text = cell.Text!;
            var codePoint = char.ConvertToUtf32(text, 0);
            var cellWidth = (cell.Width == CellWidth.WideLead ? 2 : 1) * Metrics.Width;
            var rect = new Rect(x, y, cellWidth, Metrics.Height);
            var brush = ForegroundBrush(cell.Style);

            if (_settings.BuiltinGlyphs && BuiltinGlyphs.Handles(codePoint) && text.Length == 1
                && BuiltinGlyphs.TryDraw(dc, codePoint, rect, brush, Metrics))
            {
                _run.Break();
                continue;
            }

            if (_emoji is { IsAvailable: true } && Graphemes.IsEmojiPresentation(text)
                && _emoji.Rasterize(text, (int)Math.Round(rect.Width * Metrics.PixelsPerDip), (int)Math.Round(rect.Height * Metrics.PixelsPerDip)) is { } bitmap)
            {
                _run.Break();
                dc.DrawImage(bitmap, rect);
                continue;
            }

            var bold = boldFont && cell.Style.Has(CellFlags.Bold);
            var italic = cell.Style.Has(CellFlags.Italic);
            if (!_fonts.TryMap(codePoint, bold, italic, out var typeface, out var glyph)
                && !_fonts.TryMap(0xFFFD, bold: false, italic: false, out typeface, out glyph))
            {
                _run.Break();
                continue;
            }

            _run.Add(typeface, brush, x, glyph, cellWidth);
            AddCombiningMarks(text, typeface);
        }

        _run.Flush();
    }

    private void AddCombiningMarks(string cluster, GlyphTypeface typeface)
    {
        if (cluster.Length == 1 || (cluster.Length == 2 && char.IsSurrogatePair(cluster, 0)))
        {
            return;
        }

        var first = true;
        foreach (var rune in cluster.EnumerateRunes())
        {
            if (first)
            {
                first = false;
                continue;
            }

            if (typeface.CharacterToGlyphMap.TryGetValue(rune.Value, out var mark) && Graphemes.Width(rune.Value) == 0)
            {
                _run.AddMark(mark);
            }
        }
    }

    private void DrawDecorations(DrawingContext dc, ReadOnlySpan<Cell> cells, double y)
    {
        const CellFlags decorations = CellFlags.Underline | CellFlags.DoubleUnderline | CellFlags.CurlyUnderline | CellFlags.Strikethrough | CellFlags.Overline;
        var thickness = Metrics.LineThickness;
        for (var column = 0; column < cells.Length; column++)
        {
            var style = cells[column].Style;
            if ((style.Flags & decorations) == 0 || cells[column].Width == CellWidth.WideTrail)
            {
                continue;
            }

            var brush = ForegroundBrush(style);
            var x = column * Metrics.Width;
            var width = (cells[column].Width == CellWidth.WideLead ? 2 : 1) * Metrics.Width;
            var underline = Math.Min(y + Metrics.Baseline + Math.Max(Metrics.UnderlineOffset, thickness), y + Metrics.Height - thickness);
            if (style.Has(CellFlags.Underline) || style.Has(CellFlags.CurlyUnderline))
            {
                dc.DrawRectangle(brush, null, new Rect(x, underline, width, thickness));
            }

            if (style.Has(CellFlags.DoubleUnderline))
            {
                dc.DrawRectangle(brush, null, new Rect(x, underline - (thickness * 2), width, thickness));
                dc.DrawRectangle(brush, null, new Rect(x, underline, width, thickness));
            }

            if (style.Has(CellFlags.Strikethrough))
            {
                dc.DrawRectangle(brush, null, new Rect(x, y + Metrics.Baseline - Metrics.StrikethroughOffset, width, thickness));
            }

            if (style.Has(CellFlags.Overline))
            {
                dc.DrawRectangle(brush, null, new Rect(x, y, width, thickness));
            }
        }
    }

    /// <summary>Batches consecutive glyphs that share a font and brush into one GlyphRun.</summary>
    private sealed class GlyphRunBuilder(CellMetrics metrics)
    {
        private readonly List<ushort> _glyphs = new(256);
        private readonly List<double> _advances = new(256);
        private DrawingContext? _dc;
        private GlyphTypeface? _typeface;
        private Brush? _brush;
        private double _baseline;
        private double _startX;
        private double _nextX;

        public void Begin(DrawingContext dc, double baseline)
        {
            _dc = dc;
            _baseline = baseline;
            _typeface = null;
        }

        public void Add(GlyphTypeface typeface, Brush brush, double x, ushort glyph, double advance)
        {
            if (!ReferenceEquals(typeface, _typeface) || !ReferenceEquals(brush, _brush) || Math.Abs(x - _nextX) > 0.01)
            {
                Flush();
                _typeface = typeface;
                _brush = brush;
                _startX = x;
                _nextX = x;
            }

            _glyphs.Add(glyph);
            _advances.Add(advance);
            _nextX += advance;
        }

        /// <summary>Extends an open run over a blank cell, which keeps runs long; otherwise does nothing.</summary>
        public void AddSpace(double x, double advance)
        {
            if (_typeface is null)
            {
                return;
            }

            if (Math.Abs(x - _nextX) > 0.01 || !_typeface.CharacterToGlyphMap.TryGetValue(' ', out var space))
            {
                Flush();
                return;
            }

            _glyphs.Add(space);
            _advances.Add(advance);
            _nextX += advance;
        }

        public void AddMark(ushort glyph)
        {
            if (_typeface is not null)
            {
                _glyphs.Add(glyph);
                _advances.Add(0);
            }
        }

        public void Break() => Flush();

        public void Flush()
        {
            if (_typeface is not null && _brush is not null && _glyphs.Count > 0)
            {
                var run = new GlyphRun(
                    _typeface,
                    bidiLevel: 0,
                    isSideways: false,
                    renderingEmSize: metrics.EmSize,
                    pixelsPerDip: (float)metrics.PixelsPerDip,
                    glyphIndices: _glyphs.ToArray(),
                    baselineOrigin: new Point(_startX, _baseline),
                    advanceWidths: _advances.ToArray(),
                    glyphOffsets: null,
                    characters: null,
                    deviceFontName: null,
                    clusterMap: null,
                    caretStops: null,
                    language: null);
                _dc!.DrawGlyphRun(_brush, run);
            }

            _glyphs.Clear();
            _advances.Clear();
            _typeface = null;
            _brush = null;
        }
    }
}
