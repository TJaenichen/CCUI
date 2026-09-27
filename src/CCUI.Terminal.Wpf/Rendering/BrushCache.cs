using System.Windows.Media;

namespace CCUI.Terminal.Wpf.Rendering;

/// <summary>Frozen solid brushes keyed by ARGB, so a redraw allocates none.</summary>
internal sealed class BrushCache
{
    private readonly Dictionary<uint, SolidColorBrush> _brushes = [];

    public SolidColorBrush Get(Rgb color, byte alpha = 255)
    {
        var key = ((uint)alpha << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            _brushes[key] = brush;
        }

        return brush;
    }
}
