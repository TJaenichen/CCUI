using System.Globalization;
using Wcwidth;

namespace CCUI.Terminal.Buffer;

/// <summary>
/// Cell-width and cluster rules. They follow the "grapheme" measurement that Claude Code (via string-width) and the
/// bundled ConPTY (PSEUDOCONSOLE_GLYPH_WIDTH_GRAPHEMES) use: a cluster is as wide as its first code point, except
/// that an emoji presentation selector (U+FE0F) or a regional-indicator pair makes it two columns.
/// </summary>
public static class Graphemes
{
    private const int ZeroWidthJoiner = 0x200D;
    private const int EmojiPresentationSelector = 0xFE0F;

    /// <summary>The columns a code point occupies on its own: 0 (combining/zero-width), 1 or 2; -1 if it is not printable.</summary>
    public static int Width(int codePoint)
    {
        if (codePoint is >= 0x20 and < 0x7F)
        {
            return 1;
        }

        var width = UnicodeCalculator.GetWidth(codePoint);
        return width > 2 ? 2 : width;
    }

    /// <summary>True when <paramref name="codePoint"/> extends the cluster <paramref name="previous"/> instead of starting a new cell.</summary>
    public static bool Extends(string? previous, int codePoint)
    {
        if (string.IsNullOrEmpty(previous))
        {
            return false;
        }

        if (codePoint == ZeroWidthJoiner || IsVariationSelector(codePoint) || IsEmojiModifier(codePoint) || IsTag(codePoint))
        {
            return true;
        }

        var last = LastCodePoint(previous);
        if (last == ZeroWidthJoiner)
        {
            return true;
        }

        if (IsRegionalIndicator(codePoint) && IsRegionalIndicator(last))
        {
            // Pair regional indicators two by two: a third one starts a new flag.
            return CountRegionalIndicators(previous) % 2 == 1;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.SpacingCombiningMark
            || Width(codePoint) == 0;
    }

    /// <summary>The columns a whole cluster occupies.</summary>
    public static int ClusterWidth(string cluster)
    {
        var first = char.ConvertToUtf32(cluster, 0);
        var width = Math.Max(1, Width(first));
        if (width == 2)
        {
            return 2;
        }

        if (cluster.Contains((char)EmojiPresentationSelector, StringComparison.Ordinal))
        {
            return 2;
        }

        return IsRegionalIndicator(first) && CountRegionalIndicators(cluster) >= 2 ? 2 : width;
    }

    public static bool IsRegionalIndicator(int codePoint) => codePoint is >= 0x1F1E6 and <= 0x1F1FF;

    private static bool IsVariationSelector(int codePoint) => codePoint is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF;

    private static bool IsEmojiModifier(int codePoint) => codePoint is >= 0x1F3FB and <= 0x1F3FF;

    private static bool IsTag(int codePoint) => codePoint is >= 0xE0020 and <= 0xE007F;

    private static int LastCodePoint(string text)
    {
        var last = text.Length - 1;
        return last > 0 && char.IsLowSurrogate(text[last]) && char.IsHighSurrogate(text[last - 1])
            ? char.ConvertToUtf32(text[last - 1], text[last])
            : text[last];
    }

    private static int CountRegionalIndicators(string text)
    {
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsRegionalIndicator(rune.Value))
            {
                count++;
            }
        }

        return count;
    }
}
