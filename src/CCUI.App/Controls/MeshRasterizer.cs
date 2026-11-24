namespace CCUI.App.Controls;

/// <summary>What one frame of the mesh needs: the pixel size to draw at, the moment in time and the look.</summary>
internal readonly record struct MeshFrame(int Width, int Height, double Time, double Strength, int Columns, (byte R, byte G, byte B) Primary, (byte R, byte G, byte B) Secondary);

/// <summary>
/// Draws the mesh into a premultiplied BGRA pixel buffer with its own line, dot and triangle routines. Pure CPU
/// work with no WPF objects, so a frame can be computed on a worker thread and handed to the UI as one bitmap;
/// WPF tessellating thousands of dashed strokes per frame is what made the first version crawl. Everything that
/// falls outside the frame is rejected before any pixel work, because the near rows overshoot the frame by a lot.
/// </summary>
internal sealed class MeshRasterizer
{
    // A low camera over a plane that reaches the horizon: the far row still spans the frame, the near row overshoots it.
    private const double Near = 1.6;
    private const double Far = 6.5;
    private const double CameraHeight = 1.1;
    private const double Focal = 0.9;
    private const double Amplitude = 1.25;
    private const double FaceThreshold = 0.07;

    private static readonly (byte R, byte G, byte B) Ink = (0xE6, 0xEA, 0xF2);

    private double[,] _px = new double[0, 0];
    private double[,] _py = new double[0, 0];
    private double[,] _elevation = new double[0, 0];
    private double[] _fade = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Premultiplied BGRA, <see cref="Width"/> * 4 bytes per row.</summary>
    public byte[] Pixels { get; private set; } = [];

    /// <summary>The surface height at a point, in world units: a wandering swell shaping a few overlapping waves,
    /// over a faint ripple everywhere.</summary>
    public static double Surface(double x, double z, double t)
    {
        var swellX = (1.6 * Math.Sin(0.11 * t)) + (0.7 * Math.Sin((0.067 * t) + 1.3));
        var swellZ = 3.9 + (1.6 * Math.Sin((0.083 * t) + 2.1));
        var dx = (x - swellX) / 2.8;
        var dz = (z - swellZ) / 2.4;
        var envelope = Math.Exp(-((dx * dx) + (dz * dz)));
        var waves = (0.55 * Math.Sin((1.3 * x) + (0.9 * t)) * Math.Cos((0.8 * z) - (0.7 * t)))
            + (0.35 * Math.Sin((2.1 * x) - (1.4 * z) + (1.3 * t)))
            + (0.2 * Math.Sin((3.1 * z) + (1.7 * t) + x));
        var ripple = 0.035 * Math.Sin((2.0 * x) + (0.5 * t)) * Math.Cos((1.5 * z) + (0.3 * t));
        return (Amplitude * envelope * waves) + ripple;
    }

    public void Render(MeshFrame frame)
    {
        var width = Math.Max(1, frame.Width);
        var height = Math.Max(1, frame.Height);
        if (width != Width || height != Height)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }
        else
        {
            Array.Clear(Pixels);
        }

        var strength = Math.Clamp(frame.Strength, 0, 1);
        if (strength <= 0 || width < 20 || height < 20)
        {
            return;
        }

        var columns = Math.Clamp(frame.Columns, 8, 240);
        var rows = Math.Max(6, (int)Math.Round(columns * 0.5));
        Project(width, height, rows, columns, frame.Time);
        DrawFaces(rows, columns, strength, frame);
        DrawGrid(rows, columns, strength);
        DrawDots(rows, columns, strength);
    }

    private void Project(int width, int height, int rows, int columns, double t)
    {
        if (_px.GetLength(0) != rows + 1 || _px.GetLength(1) != columns + 1)
        {
            _px = new double[rows + 1, columns + 1];
            _py = new double[rows + 1, columns + 1];
            _elevation = new double[rows + 1, columns + 1];
            _fade = new double[rows + 1];
        }

        var horizon = height * 0.40;
        var centre = width / 2.0;
        var scale = width * Focal;
        var halfWidth = centre / (scale / Far) * 1.1;
        for (var r = 0; r <= rows; r++)
        {
            var depth = (double)r / rows;
            var z = Near + ((Far - Near) * depth);
            var s = scale / z;
            _fade[r] = Math.Pow(1 - depth, 1.1);
            for (var c = 0; c <= columns; c++)
            {
                var x = (((double)c / columns * 2) - 1) * halfWidth;
                var y = Surface(x, z, t);
                _elevation[r, c] = y;
                _px[r, c] = centre + (x * s);
                _py[r, c] = horizon + ((CameraHeight - y) * s);
            }
        }
    }

    private void DrawFaces(int rows, int columns, double strength, MeshFrame frame)
    {
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var lift = Math.Max(
                    Math.Max(Math.Abs(_elevation[r, c]), Math.Abs(_elevation[r, c + 1])),
                    Math.Max(Math.Abs(_elevation[r + 1, c]), Math.Abs(_elevation[r + 1, c + 1])));
                if (lift < FaceThreshold || Outside(_px[r, c], _py[r, c], _px[r + 1, c + 1], _py[r + 1, c + 1]))
                {
                    continue;
                }

                var alpha = Alpha(strength * Math.Sqrt(_fade[r]) * Math.Clamp((lift - FaceThreshold) / 0.45, 0, 1) * 0.8);
                var mix = 0.5 + (0.5 * Math.Sin((1.1 * c / columns * 6) + (0.6 * r / rows * 6) + (0.4 * frame.Time)));
                var colour = (
                    (byte)Math.Round(frame.Primary.R + ((frame.Secondary.R - frame.Primary.R) * mix)),
                    (byte)Math.Round(frame.Primary.G + ((frame.Secondary.G - frame.Primary.G) * mix)),
                    (byte)Math.Round(frame.Primary.B + ((frame.Secondary.B - frame.Primary.B) * mix)));

                FillTriangle(_px[r, c], _py[r, c], _px[r, c + 1], _py[r, c + 1], _px[r + 1, c], _py[r + 1, c], colour, alpha);
                FillTriangle(_px[r, c + 1], _py[r, c + 1], _px[r + 1, c + 1], _py[r + 1, c + 1], _px[r + 1, c], _py[r + 1, c], colour, alpha);
                var edge = alpha * 6 / 10;
                DrawLine(_px[r, c], _py[r, c], _px[r, c + 1], _py[r, c + 1], Ink, edge);
                DrawLine(_px[r, c], _py[r, c], _px[r + 1, c], _py[r + 1, c], Ink, edge);
                DrawLine(_px[r, c + 1], _py[r, c + 1], _px[r + 1, c], _py[r + 1, c], Ink, edge);
            }
        }
    }

    private void DrawGrid(int rows, int columns, double strength)
    {
        for (var r = 0; r <= rows; r++)
        {
            var alpha = Alpha(strength * _fade[r] * 0.12);
            for (var c = 0; c < columns; c++)
            {
                DrawLine(_px[r, c], _py[r, c], _px[r, c + 1], _py[r, c + 1], Ink, alpha);
            }
        }

        for (var c = 0; c <= columns; c++)
        {
            for (var r = 0; r < rows; r++)
            {
                DrawLine(_px[r, c], _py[r, c], _px[r + 1, c], _py[r + 1, c], Ink, Alpha(strength * _fade[r] * 0.08));
            }
        }
    }

    private void DrawDots(int rows, int columns, double strength)
    {
        for (var r = 0; r <= rows; r++)
        {
            var spacing = (_px[r, columns] - _px[r, 0]) / columns;
            if (spacing < 2)
            {
                continue;
            }

            var radius = Math.Clamp(spacing * 0.05, 0.6, 1.6);
            var alpha = Alpha(strength * _fade[r] * 0.8);
            for (var c = 0; c <= columns; c++)
            {
                Dot(_px[r, c], _py[r, c], radius, alpha);
            }
        }
    }

    /// <summary>A soft round dot: a small disc with coverage falling off at its edge.</summary>
    private void Dot(double cx, double cy, double radius, int alpha)
    {
        var reach = radius + 1;
        if (cx + reach < 0 || cx - reach >= Width || cy + reach < 0 || cy - reach >= Height)
        {
            return;
        }

        var x0 = (int)Math.Floor(cx - reach);
        var x1 = (int)Math.Ceiling(cx + reach);
        var y0 = (int)Math.Floor(cy - reach);
        var y1 = (int)Math.Ceiling(cy + reach);
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var dx = x + 0.5 - cx;
                var dy = y + 0.5 - cy;
                var coverage = radius + 0.5 - Math.Sqrt((dx * dx) + (dy * dy));
                if (coverage > 0)
                {
                    Blend(x, y, Ink, coverage >= 1 ? alpha : (int)(alpha * coverage));
                }
            }
        }
    }

    /// <summary>Wu's anti-aliased line, clipped to the frame before it steps.</summary>
    private void DrawLine(double x0, double y0, double x1, double y1, (byte R, byte G, byte B) colour, int alpha)
    {
        if (alpha <= 0 || Outside(x0, y0, x1, y1))
        {
            return;
        }

        var steep = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);
        if (steep)
        {
            (x0, y0) = (y0, x0);
            (x1, y1) = (y1, x1);
        }

        if (x0 > x1)
        {
            (x0, x1) = (x1, x0);
            (y0, y1) = (y1, y0);
        }

        var dx = x1 - x0;
        var gradient = dx == 0 ? 0 : (y1 - y0) / dx;
        var start = (int)Math.Round(x0);
        var end = (int)Math.Round(x1);
        var limit = (steep ? Height : Width) - 1;
        var first = Math.Max(start, 0);
        end = Math.Min(end, limit);
        var y = y0 + (gradient * (first - x0));
        for (var x = first; x <= end; x++)
        {
            var floor = Math.Floor(y);
            var fraction = y - floor;
            var row = (int)floor;
            var far = (int)(alpha * fraction);
            var near = alpha - far;
            if (steep)
            {
                Blend(row, x, colour, near);
                Blend(row + 1, x, colour, far);
            }
            else
            {
                Blend(x, row, colour, near);
                Blend(x, row + 1, colour, far);
            }

            y += gradient;
        }
    }

    /// <summary>Scanline fill; the soft edges come from the lines drawn over the triangle's sides.</summary>
    private void FillTriangle(double x1, double y1, double x2, double y2, double x3, double y3, (byte R, byte G, byte B) colour, int alpha)
    {
        if (alpha <= 0)
        {
            return;
        }

        if (y2 < y1)
        {
            (x1, y1, x2, y2) = (x2, y2, x1, y1);
        }

        if (y3 < y1)
        {
            (x1, y1, x3, y3) = (x3, y3, x1, y1);
        }

        if (y3 < y2)
        {
            (x2, y2, x3, y3) = (x3, y3, x2, y2);
        }

        var top = Math.Max(0, (int)Math.Ceiling(y1));
        var bottom = Math.Min(Height - 1, (int)Math.Floor(y3));
        for (var y = top; y <= bottom; y++)
        {
            // A face thinner than a pixel row can still straddle a row boundary; sampling outside its own height
            // would extrapolate the edges into long streaks.
            var centre = Math.Clamp(y + 0.5, y1, y3);
            var longSide = XAt(x1, y1, x3, y3, centre);
            var shortSide = centre < y2 ? XAt(x1, y1, x2, y2, centre) : XAt(x2, y2, x3, y3, centre);
            var left = Math.Max(0, (int)Math.Ceiling(Math.Min(longSide, shortSide)));
            var right = Math.Min(Width - 1, (int)Math.Floor(Math.Max(longSide, shortSide)));
            for (var x = left; x <= right; x++)
            {
                Blend(x, y, colour, alpha);
            }
        }
    }

    private static double XAt(double xa, double ya, double xb, double yb, double y) =>
        yb == ya ? xa : xa + ((xb - xa) * (y - ya) / (yb - ya));

    /// <summary>True when the box spanned by the two points misses the frame entirely.</summary>
    private bool Outside(double xa, double ya, double xb, double yb) =>
        Math.Max(xa, xb) < 0 || Math.Min(xa, xb) >= Width || Math.Max(ya, yb) < 0 || Math.Min(ya, yb) >= Height;

    private static int Alpha(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);

    /// <summary>Source-over in premultiplied BGRA, with the alpha as 0 to 255.</summary>
    private void Blend(int x, int y, (byte R, byte G, byte B) colour, int alpha)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || alpha <= 0)
        {
            return;
        }

        if (alpha > 255)
        {
            alpha = 255;
        }

        var pixels = Pixels;
        var i = ((y * Width) + x) * 4;
        var keep = 255 - alpha;
        pixels[i] = (byte)(((colour.B * alpha) + (pixels[i] * keep)) / 255);
        pixels[i + 1] = (byte)(((colour.G * alpha) + (pixels[i + 1] * keep)) / 255);
        pixels[i + 2] = (byte)(((colour.R * alpha) + (pixels[i + 2] * keep)) / 255);
        pixels[i + 3] = (byte)(alpha + ((pixels[i + 3] * keep) / 255));
    }
}
