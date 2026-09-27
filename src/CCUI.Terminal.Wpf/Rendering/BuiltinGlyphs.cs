using System.Windows;
using System.Windows.Media;

namespace CCUI.Terminal.Wpf.Rendering;

/// <summary>
/// Draws box-drawing (U+2500-U+257F) and block elements (U+2580-U+259F) as geometry instead of font glyphs, so
/// borders and bars join seamlessly whatever the line height (as Windows Terminal's "builtinGlyphs" does).
/// </summary>
internal static class BuiltinGlyphs
{
    /// <summary>
    /// Arm weights for U+2500..U+257F in the order up, down, left, right: 0 none, 1 light, 2 heavy, 3 double.
    /// "----" means "use the font" (dashed lines). Verified against the Unicode character names.
    /// </summary>
    private static readonly string[] BoxArms =
    [
        "0011", "0022", "1100", "2200", "----", "----", "----", "----",
        "----", "----", "----", "----", "0101", "0102", "0201", "0202",
        "0110", "0120", "0210", "0220", "1001", "1002", "2001", "2002",
        "1010", "1020", "2010", "2020", "1101", "1102", "2101", "1201",
        "2201", "2102", "1202", "2202", "1110", "1120", "2110", "1210",
        "2210", "2120", "1220", "2220", "0111", "0121", "0112", "0122",
        "0211", "0221", "0212", "0222", "1011", "1021", "1012", "1022",
        "2011", "2021", "2012", "2022", "1111", "1121", "1112", "1122",
        "2111", "1211", "2211", "2121", "2112", "1221", "1212", "2122",
        "1222", "2221", "2212", "2222", "----", "----", "----", "----",
        "0033", "3300", "0103", "0301", "0303", "0130", "0310", "0330",
        "1003", "3001", "3003", "1030", "3010", "3030", "1103", "3301",
        "3303", "1130", "3310", "3330", "0133", "0311", "0333", "1033",
        "3011", "3033", "1133", "3311", "3333", "----", "----", "----",
        "----", "----", "----", "----", "0010", "1000", "0001", "0100",
        "0020", "2000", "0002", "0200", "0012", "1200", "0021", "2100",
    ];

    /// <summary>Quadrants U+2596..U+259F as bits: upper-left 1, upper-right 2, lower-left 4, lower-right 8.</summary>
    private static readonly int[] Quadrants = [4, 8, 1, 1 | 4 | 8, 1 | 8, 1 | 2 | 4, 1 | 2 | 8, 2, 2 | 4, 2 | 4 | 8];

    public static bool Handles(int codePoint) => codePoint is >= 0x2500 and <= 0x259F;

    /// <summary>Draws the glyph into <paramref name="cell"/>; false when the code point is left to the font.</summary>
    public static bool TryDraw(DrawingContext dc, int codePoint, Rect cell, Brush brush, CellMetrics metrics)
    {
        if (codePoint is >= 0x2580 and <= 0x259F)
        {
            DrawBlock(dc, codePoint, cell, brush, metrics);
            return true;
        }

        if (codePoint is < 0x2500 or > 0x257F)
        {
            return false;
        }

        switch (codePoint)
        {
            case >= 0x256D and <= 0x2570:
                DrawArc(dc, codePoint, cell, brush, metrics);
                return true;
            case >= 0x2571 and <= 0x2573:
                DrawDiagonals(dc, codePoint, cell, brush, metrics);
                return true;
        }

        var arms = BoxArms[codePoint - 0x2500];
        if (arms[0] == '-')
        {
            return false;
        }

        DrawArms(dc, cell, brush, metrics, arms[0] - '0', arms[1] - '0', arms[2] - '0', arms[3] - '0');
        return true;
    }

    private static double LightThickness(CellMetrics m) => Math.Max(1 / m.PixelsPerDip, m.Snap(m.Height * 0.06));

    private static void DrawArms(DrawingContext dc, Rect cell, Brush brush, CellMetrics m, int up, int down, int left, int right)
    {
        var light = LightThickness(m);
        var cx = m.Snap(cell.X + (cell.Width / 2));
        var cy = m.Snap(cell.Y + (cell.Height / 2));

        void Vertical(double x, double y0, double y1, double t) =>
            dc.DrawRectangle(brush, null, new Rect(m.Snap(x - (t / 2)), y0, t, Math.Max(0, y1 - y0)));

        void Horizontal(double y, double x0, double x1, double t) =>
            dc.DrawRectangle(brush, null, new Rect(x0, m.Snap(y - (t / 2)), Math.Max(0, x1 - x0), t));

        void Arm(int weight, bool vertical, double from, double to)
        {
            switch (weight)
            {
                case 0:
                    return;
                case 3:
                    var gap = light;
                    if (vertical)
                    {
                        Vertical(cx - gap, from, to, light);
                        Vertical(cx + gap, from, to, light);
                    }
                    else
                    {
                        Horizontal(cy - gap, from, to, light);
                        Horizontal(cy + gap, from, to, light);
                    }

                    return;
                default:
                    var t = weight == 2 ? light * 2 : light;
                    if (vertical)
                    {
                        Vertical(cx, from, to, t);
                    }
                    else
                    {
                        Horizontal(cy, from, to, t);
                    }

                    return;
            }
        }

        // Each arm runs from the cell edge to just past the centre, so arms of any weight overlap at the junction.
        var reach = light * 2;
        Arm(up, vertical: true, cell.Top, cy + reach);
        Arm(down, vertical: true, cy - reach, cell.Bottom);
        Arm(left, vertical: false, cell.Left, cx + reach);
        Arm(right, vertical: false, cx - reach, cell.Right);
    }

    private static void DrawArc(DrawingContext dc, int codePoint, Rect cell, Brush brush, CellMetrics m)
    {
        var t = LightThickness(m);
        var cx = m.Snap(cell.X + (cell.Width / 2));
        var cy = m.Snap(cell.Y + (cell.Height / 2));
        var r = Math.Min(cell.Width, cell.Height) / 2;
        var (start, arcStart, arcEnd, end) = codePoint switch
        {
            0x256D => (new Point(cx, cell.Bottom), new Point(cx, cy + r), new Point(cx + r, cy), new Point(cell.Right, cy)),
            0x256E => (new Point(cell.Left, cy), new Point(cx - r, cy), new Point(cx, cy + r), new Point(cx, cell.Bottom)),
            0x256F => (new Point(cx, cell.Top), new Point(cx, cy - r), new Point(cx - r, cy), new Point(cell.Left, cy)),
            _ => (new Point(cell.Right, cy), new Point(cx + r, cy), new Point(cx, cy - r), new Point(cx, cell.Top)),
        };

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.LineTo(arcStart, isStroked: true, isSmoothJoin: true);
            ctx.ArcTo(arcEnd, new Size(r, r), 0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
            ctx.LineTo(end, isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(brush, t), geometry);
    }

    private static void DrawDiagonals(DrawingContext dc, int codePoint, Rect cell, Brush brush, CellMetrics m)
    {
        var pen = new Pen(brush, LightThickness(m));
        if (codePoint != 0x2572)
        {
            dc.DrawLine(pen, cell.BottomLeft, cell.TopRight);
        }

        if (codePoint != 0x2571)
        {
            dc.DrawLine(pen, cell.TopLeft, cell.BottomRight);
        }
    }

    private static void DrawBlock(DrawingContext dc, int codePoint, Rect cell, Brush brush, CellMetrics m)
    {
        double X(double fraction) => m.Snap(cell.X + (cell.Width * fraction));
        double Y(double fraction) => m.Snap(cell.Y + (cell.Height * fraction));
        void Fill(double x0, double y0, double x1, double y1) =>
            dc.DrawRectangle(brush, null, new Rect(new Point(X(x0), Y(y0)), new Point(X(x1), Y(y1))));

        switch (codePoint)
        {
            case 0x2580:
                Fill(0, 0, 1, 0.5);
                break;
            case >= 0x2581 and <= 0x2588:
                Fill(0, 1 - ((codePoint - 0x2580) / 8.0), 1, 1);
                break;
            case >= 0x2589 and <= 0x258F:
                Fill(0, 0, (0x2590 - codePoint) / 8.0, 1);
                break;
            case 0x2590:
                Fill(0.5, 0, 1, 1);
                break;
            case >= 0x2591 and <= 0x2593:
                dc.PushOpacity((codePoint - 0x2590) * 0.25);
                Fill(0, 0, 1, 1);
                dc.Pop();
                break;
            case 0x2594:
                Fill(0, 0, 1, 0.125);
                break;
            case 0x2595:
                Fill(0.875, 0, 1, 1);
                break;
            default:
                var bits = Quadrants[codePoint - 0x2596];
                if ((bits & 1) != 0)
                {
                    Fill(0, 0, 0.5, 0.5);
                }

                if ((bits & 2) != 0)
                {
                    Fill(0.5, 0, 1, 0.5);
                }

                if ((bits & 4) != 0)
                {
                    Fill(0, 0.5, 0.5, 1);
                }

                if ((bits & 8) != 0)
                {
                    Fill(0.5, 0.5, 1, 1);
                }

                break;
        }
    }
}
