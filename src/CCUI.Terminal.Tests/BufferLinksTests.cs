using CCUI.Terminal.Buffer;

namespace CCUI.Terminal.Tests;

public sealed class BufferLinksTests
{
    [Theory]
    [InlineData("see https://example.com/a?b=1 now", 10, "https://example.com/a?b=1")]
    [InlineData("see https://example.com/a?b=1 now", 4, "https://example.com/a?b=1")]
    [InlineData("Open https://example.com/docs.", 12, "https://example.com/docs")]
    [InlineData("(https://en.wikipedia.org/wiki/Foo_(bar))", 5, "https://en.wikipedia.org/wiki/Foo_(bar)")]
    [InlineData("(see http://x.io/y)", 8, "http://x.io/y")]
    [InlineData("<https://example.com>", 3, "https://example.com")]
    [InlineData("│ https://example.com │", 5, "https://example.com")]
    [InlineData("file:///C:/work/log.txt", 2, "file:///C:/work/log.txt")]
    public void FindsTheUrlUnderTheCell(string text, int column, string expected)
    {
        var term = new TerminalEmulator(60, 2);
        term.Feed(text);

        Assert.Equal(expected, BufferLinks.LinkAt(term.Buffer, new BufferPosition(0, column))?.Uri);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(59)]
    public void ReturnsNullAwayFromUrls(int column)
    {
        var term = new TerminalEmulator(60, 2);
        term.Feed("see https://example.com/a?b=1 now");

        Assert.Null(BufferLinks.LinkAt(term.Buffer, new BufferPosition(0, column)));
    }

    [Fact]
    public void FollowsAUrlAcrossSoftWrappedLines()
    {
        var term = new TerminalEmulator(20, 4);
        term.Feed("go https://example.com/a/very/long/path ok");

        var link = BufferLinks.LinkAt(term.Buffer, new BufferPosition(1, 5));

        Assert.NotNull(link);
        Assert.Equal("https://example.com/a/very/long/path", link.Uri);
        Assert.Equal((new BufferPosition(0, 3), new BufferPosition(1, 18)), (link.Start, link.End));
    }

    [Fact]
    public void DoesNotJoinHardLineBreaks()
    {
        var term = new TerminalEmulator(40, 4);
        term.Feed("https://example.com/a\r\nmore");

        Assert.Equal("https://example.com/a", BufferLinks.LinkAt(term.Buffer, new BufferPosition(0, 5))?.Uri);
        Assert.Null(BufferLinks.LinkAt(term.Buffer, new BufferPosition(1, 1)));
    }
}
