using CCUI.Terminal.Buffer;

namespace CCUI.Terminal.Tests;

public sealed class GraphemesTests
{
    [Theory]
    [InlineData("😀", true)]
    [InlineData("✅", true)]
    [InlineData("❤️", true)]
    [InlineData("🇩🇪", true)]
    [InlineData("1️⃣", true)]
    [InlineData("⏺", false)]
    [InlineData("⎿", false)]
    [InlineData("✻", false)]
    [InlineData("❤", false)]
    [InlineData("a", false)]
    [InlineData("─", false)]
    public void DetectsEmojiPresentation(string cluster, bool expected)
    {
        Assert.Equal(expected, Graphemes.IsEmojiPresentation(cluster));
    }

    [Theory]
    [InlineData('a', 1)]
    [InlineData('漢', 2)]
    [InlineData('́', 0)]
    public void MeasuresCodePoints(char c, int width)
    {
        Assert.Equal(width, Graphemes.Width(c));
    }
}
