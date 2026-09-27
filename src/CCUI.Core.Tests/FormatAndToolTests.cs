using CCUI.Core.Claude;
using CCUI.Core.ViewModels;

namespace CCUI.Core.Tests;

public sealed class FormatAndToolTests
{
    [Theory]
    [InlineData(850, "850ms")]
    [InlineData(4_200, "4.2s")]
    [InlineData(33_000, "33s")]
    [InlineData(185_000, "3m 05s")]
    [InlineData(3_720_000, "1h 02m")]
    public void FormatsDurations(int milliseconds, string expected) => Assert.Equal(expected, Format.Duration(TimeSpan.FromMilliseconds(milliseconds)));

    [Theory]
    [InlineData(950, "950")]
    [InlineData(1_234, "1.2k")]
    [InlineData(185_005, "185k")]
    [InlineData(2_500_000, "2.5M")]
    public void FormatsTokens(long tokens, string expected) => Assert.Equal(expected, Format.Tokens(tokens));

    [Theory]
    [InlineData("claude-opus-5-5", "Opus 5.5")]
    [InlineData("claude-sonnet-4-5-20250929", "Sonnet 4.5")]
    [InlineData("claude-haiku-4-5", "Haiku 4.5")]
    [InlineData(null, "—")]
    public void FormatsModelNames(string? model, string expected) => Assert.Equal(expected, Format.ModelName(model));

    [Fact]
    public void FormatsRelativeTimes()
    {
        var now = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("just now", Format.Relative(now.AddSeconds(-5), now));
        Assert.Equal("7m ago", Format.Relative(now.AddMinutes(-7), now));
        Assert.Equal("3h ago", Format.Relative(now.AddHours(-3), now));
        Assert.Equal("yesterday", Format.Relative(now.AddHours(-30), now));
    }

    [Fact]
    public void FirstLineMarksMore()
    {
        Assert.Equal("one …", Format.FirstLine("one\ntwo"));
        Assert.Equal("one", Format.FirstLine("  one\n\n"));
    }

    [Theory]
    [InlineData("Bash", """{"command":"git status","description":"Check tree"}""", "Check tree")]
    [InlineData("Bash", """{"command":"git status"}""", "git status")]
    [InlineData("Read", """{"file_path":"src/a.cs"}""", "src/a.cs")]
    [InlineData("TodoWrite", """{"todos":[{},{}]}""", "2 todos")]
    [InlineData("mcp__github__get_file", """{"owner":"me","path":"x"}""", "me")]
    [InlineData("Bash", "not json", "")]
    public void DescribesToolCalls(string tool, string input, string expected) => Assert.Equal(expected, ToolSummaries.Describe(tool, input));

    [Fact]
    public void ShortensToOneLine()
    {
        Assert.Equal("a b c", ToolSummaries.Shorten("a\nb\r\nc", 10));
        Assert.Equal("abcd…", ToolSummaries.Shorten("abcdefgh", 5));
    }
}
