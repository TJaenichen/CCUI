using CCUI.Terminal.Buffer;

namespace CCUI.Terminal.Tests;

public sealed class BufferTextTests
{
    [Fact]
    public void JoinsSoftWrappedLinesAndBreaksHardOnes()
    {
        var term = new TerminalEmulator(5, 4);
        term.Feed("abcdefg\r\nxyz");

        var text = BufferText.Extract(term.Buffer, new BufferPosition(0, 0), new BufferPosition(2, 4), "\n");

        Assert.Equal("abcdefg\nxyz", text);
    }

    [Fact]
    public void ExtractsPartialLinesAndAcceptsReversedEnds()
    {
        var term = new TerminalEmulator(10, 2);
        term.Feed("hello world");

        Assert.Equal("llo w", BufferText.Extract(term.Buffer, new BufferPosition(0, 6), new BufferPosition(0, 2), "\n"));
    }

    [Fact]
    public void IncludesScrollback()
    {
        var term = new TerminalEmulator(10, 1);
        term.Feed("old\r\nnew");

        Assert.Equal("old\nnew", BufferText.Extract(term.Buffer, new BufferPosition(0, 0), new BufferPosition(1, 9), "\n"));
    }

    [Theory]
    [InlineData(2, 0, 4)]
    [InlineData(8, 6, 15)]
    [InlineData(5, 5, 5)]
    public void FindsWordsBetweenDelimiters(int column, int start, int end)
    {
        var term = new TerminalEmulator(20, 1);
        term.Feed("hello src/app.cs");
        var line = term.Buffer[0];

        Assert.Equal((start, end), BufferText.WordAt(line, column, " "));
    }
}
