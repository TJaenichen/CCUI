using System.Windows.Media;

namespace CCUI.Terminal.Wpf.Rendering;

/// <summary>Cell geometry in device-independent pixels, snapped to whole device pixels so rows and columns never blur.</summary>
internal sealed record CellMetrics(
    double Width,
    double Height,
    double Baseline,
    double UnderlineOffset,
    double LineThickness,
    double StrikethroughOffset,
    double EmSize,
    double PixelsPerDip)
{
    public static CellMetrics From(GlyphTypeface primary, double emSize, double pixelsPerDip, double lineHeightScale)
    {
        double Snap(double value) => Math.Max(1, Math.Round(value * pixelsPerDip)) / pixelsPerDip;

        var advance = primary.CharacterToGlyphMap.TryGetValue('M', out var m) ? primary.AdvanceWidths[m] : 0.6;
        var width = Snap(advance * emSize);
        var fontHeight = primary.Height * emSize;
        var height = Snap(fontHeight * lineHeightScale);

        // Centre the font's ascent+descent in the (possibly taller) cell.
        var baseline = Math.Round(((primary.Baseline * emSize) + ((height - fontHeight) / 2)) * pixelsPerDip) / pixelsPerDip;
        var thickness = Snap(Math.Max(primary.UnderlineThickness * emSize, 1 / pixelsPerDip));
        return new CellMetrics(
            width,
            height,
            baseline,
            Math.Round(-primary.UnderlinePosition * emSize * pixelsPerDip) / pixelsPerDip,
            thickness,
            Math.Round(primary.StrikethroughPosition * emSize * pixelsPerDip) / pixelsPerDip,
            emSize,
            pixelsPerDip);
    }

    public double Snap(double value) => Math.Round(value * PixelsPerDip) / PixelsPerDip;
}
