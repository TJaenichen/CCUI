using System.Windows;
using System.Windows.Media;

namespace CCUI.Terminal.Wpf.Rendering;

/// <summary>
/// Maps code points to glyphs across a chain of fonts: the configured family first, then the fallbacks. WPF's
/// GlyphRun has no font fallback of its own, so this is what makes symbols such as ⏺ ⎿ ✻ render.
/// </summary>
internal sealed class FontFallback
{
    private static readonly string[] LastResort = ["Cascadia Mono", "Consolas", "Courier New"];

    private readonly GlyphTypeface[][] _chains = new GlyphTypeface[4][];
    private readonly Dictionary<(int CodePoint, int Variant), (GlyphTypeface Typeface, ushort Glyph)?> _cache = [];

    public FontFallback(FontFamily family, FontWeight weight, IEnumerable<string> fallbackFamilies)
    {
        var names = family.Source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(fallbackFamilies)
            .Concat(LastResort)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var variant = 0; variant < 4; variant++)
        {
            var bold = (variant & 1) != 0;
            var italic = (variant & 2) != 0;
            _chains[variant] = [.. names
                .Select(name => TryGetGlyphTypeface(name, italic ? FontStyles.Italic : FontStyles.Normal, bold ? FontWeights.Bold : weight))
                .OfType<GlyphTypeface>()];
        }

        if (_chains[0].Length == 0)
        {
            throw new InvalidOperationException("None of the configured terminal fonts is installed.");
        }
    }

    /// <summary>The first available font of the chain; it defines the cell size.</summary>
    public GlyphTypeface Primary => _chains[0][0];

    public bool TryMap(int codePoint, bool bold, bool italic, out GlyphTypeface typeface, out ushort glyph)
    {
        var variant = (bold ? 1 : 0) | (italic ? 2 : 0);
        if (!_cache.TryGetValue((codePoint, variant), out var hit))
        {
            hit = Find(_chains[variant], codePoint) ?? (variant != 0 ? Find(_chains[0], codePoint) : null);
            _cache[(codePoint, variant)] = hit;
        }

        if (hit is { } found)
        {
            (typeface, glyph) = found;
            return true;
        }

        typeface = Primary;
        glyph = 0;
        return false;
    }

    private static (GlyphTypeface, ushort)? Find(GlyphTypeface[] chain, int codePoint)
    {
        foreach (var typeface in chain)
        {
            if (typeface.CharacterToGlyphMap.TryGetValue(codePoint, out var glyph))
            {
                return (typeface, glyph);
            }
        }

        return null;
    }

    private static GlyphTypeface? TryGetGlyphTypeface(string familyName, FontStyle style, FontWeight weight)
    {
        var typeface = new Typeface(new FontFamily(familyName), style, weight, FontStretches.Normal);
        return typeface.TryGetGlyphTypeface(out var glyphTypeface) ? glyphTypeface : null;
    }
}
