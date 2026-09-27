using CCUI.Terminal.Buffer;

namespace CCUI.Terminal.Tests;

public sealed class TerminalEmulatorTests
{
    [Fact]
    public void WrapsOnlyWhenTheNextCharacterArrives()
    {
        var term = Emulate(5, 3, "abcde");

        Assert.Equal((0, 4), (term.CursorRow, term.CursorColumn));
        Assert.False(term.Buffer[0].IsWrapped);

        term.Feed("f");

        Assert.Equal("abcde\nf", term.GetScreenText());
        Assert.True(term.Buffer[0].IsWrapped);
        Assert.Equal((1, 1), (term.CursorRow, term.CursorColumn));
    }

    [Fact]
    public void CarriageReturnCancelsAPendingWrap()
    {
        var term = Emulate(5, 3, "abcde\rX");

        Assert.Equal("Xbcde", term.GetScreenText());
    }

    [Fact]
    public void WithoutAutoWrapTheLastColumnIsOverwritten()
    {
        var term = Emulate(3, 2, "\e[?7labcd");

        Assert.Equal("abd", term.GetScreenText());
    }

    [Fact]
    public void MovesTheCursorAndClampsToTheScreen()
    {
        var term = Emulate(10, 5, "\e[3;4H");
        Assert.Equal((2, 3), (term.CursorRow, term.CursorColumn));

        term.Feed("\e[10A\e[99C");
        Assert.Equal((0, 9), (term.CursorRow, term.CursorColumn));

        term.Feed("\e[H\e[2B\e[G");
        Assert.Equal((2, 0), (term.CursorRow, term.CursorColumn));
    }

    [Fact]
    public void ErasesWithTheCurrentBackground()
    {
        var term = Emulate(6, 2, "abcdef\r\e[3C\e[41m\e[K");

        Assert.Equal("abc", term.Buffer[0].GetText());
        Assert.Equal(TerminalColor.FromIndex(1), term.Buffer[0][4].Style.Background);
        Assert.True(term.Buffer[0][4].Style.Foreground.IsDefault);
    }

    [Theory]
    [InlineData("\e[0J", "ab\ncd\ne")]
    [InlineData("\e[1J", "\n\n\ngh")]
    [InlineData("\e[2J", "")]
    public void ErasesInDisplay(string erase, string expected)
    {
        var term = Emulate(4, 4, "ab\r\ncd\r\nef\r\ngh\e[3;2H" + erase);

        Assert.Equal(expected, term.GetScreenText());
    }

    [Fact]
    public void AppliesSgrAttributesAndColours()
    {
        var term = Emulate(10, 1, "\e[1;3;4;38;5;208;48;2;1;2;3mx\e[38:2::255:128:0my\e[0mz");

        var x = term.Buffer[0][0].Style;
        Assert.True(x.Has(CellFlags.Bold) && x.Has(CellFlags.Italic) && x.Has(CellFlags.Underline));
        Assert.Equal(TerminalColor.FromIndex(208), x.Foreground);
        Assert.Equal(TerminalColor.FromRgb(1, 2, 3), x.Background);
        Assert.Equal(TerminalColor.FromRgb(255, 128, 0), term.Buffer[0][1].Style.Foreground);
        Assert.Equal(CellStyle.Default, term.Buffer[0][2].Style);
    }

    [Fact]
    public void BrightColoursMapToThePaletteUpperHalf()
    {
        var term = Emulate(4, 1, "\e[91;102mx");

        Assert.Equal(TerminalColor.FromIndex(9), term.Buffer[0][0].Style.Foreground);
        Assert.Equal(TerminalColor.FromIndex(10), term.Buffer[0][0].Style.Background);
    }

    [Fact]
    public void LinesScrollingOffTheTopGoToScrollback()
    {
        var term = Emulate(5, 2, "1\r\n2\r\n3\r\n4");

        Assert.Equal("3\n4", term.GetScreenText());
        Assert.Equal(2, term.Buffer.ScrollbackCount);
        Assert.Equal("1", term.Buffer.GetLine(0).GetText());
        Assert.Equal(2, term.ScrolledOffCount);
    }

    [Fact]
    public void ScrollbackIsBounded()
    {
        var term = new TerminalEmulator(5, 1, scrollbackLines: 3);
        term.Feed("1\r\n2\r\n3\r\n4\r\n5\r\n6");

        Assert.Equal(3, term.Buffer.ScrollbackCount);
        Assert.Equal("3", term.Buffer.GetLine(0).GetText());
        Assert.Equal(5, term.ScrolledOffCount);
    }

    [Fact]
    public void ScrollRegionKeepsOtherLinesAndSkipsScrollback()
    {
        var term = Emulate(5, 4, "top\r\na\r\nb\r\nbot\e[2;3r\e[3;1H\n\nX");

        Assert.Equal("top\n\nX\nbot", term.GetScreenText());
        Assert.Equal(0, term.Buffer.ScrollbackCount);
    }

    [Fact]
    public void AlternateScreenRestoresTheMainScreenAndCursor()
    {
        var term = Emulate(10, 3, "main\e[?1049h");
        Assert.True(term.IsAlternateScreen);
        Assert.Equal(string.Empty, term.GetScreenText());

        term.Feed("\e[2;2Halt\e[?1049l");

        Assert.False(term.IsAlternateScreen);
        Assert.Equal("main", term.GetScreenText());
        Assert.Equal((0, 4), (term.CursorRow, term.CursorColumn));
    }

    [Fact]
    public void WideCharactersTakeTwoCells()
    {
        var term = Emulate(6, 2, "a漢b");

        Assert.Equal(CellWidth.WideLead, term.Buffer[0][1].Width);
        Assert.Equal(CellWidth.WideTrail, term.Buffer[0][2].Width);
        Assert.Equal("a漢b", term.Buffer[0].GetText());
        Assert.Equal(4, term.CursorColumn);
    }

    [Fact]
    public void WideCharacterAtTheLastColumnWrapsFirst()
    {
        var term = Emulate(3, 2, "ab漢");

        Assert.Equal("ab\n漢", term.GetScreenText());
        Assert.True(term.Buffer[0].IsWrapped);
    }

    [Fact]
    public void OverwritingHalfAWideCharacterBlanksTheOtherHalf()
    {
        var term = Emulate(4, 1, "漢\e[2Gx");

        Assert.True(term.Buffer[0][0].IsBlank);
        Assert.Equal(CellWidth.Normal, term.Buffer[0][0].Width);
        Assert.Equal("x", term.Buffer[0][1].Text);
    }

    [Theory]
    [InlineData("e\u0301", 1)]
    [InlineData("👩\u200D💻", 2)]
    [InlineData("❤\uFE0F", 2)]
    [InlineData("🇩🇪", 2)]
    [InlineData("👍🏽", 2)]
    public void GraphemeClustersShareOneCell(string cluster, int width)
    {
        var term = Emulate(10, 1, cluster + "|");

        Assert.Equal(cluster, term.Buffer[0][0].Text);
        Assert.Equal(width, term.CursorColumn - 1);
        Assert.Equal("|", term.Buffer[0][width].Text);
    }

    [Fact]
    public void ThreeRegionalIndicatorsStartASecondCell()
    {
        var term = Emulate(10, 1, "🇩🇪🇫");

        Assert.Equal("🇩🇪", term.Buffer[0][0].Text);
        Assert.Equal("🇫", term.Buffer[0][2].Text);
    }

    [Fact]
    public void CursorMovementEndsACluster()
    {
        var term = Emulate(10, 1, "e\e[C\u0301");

        Assert.Equal("e", term.Buffer[0][0].Text);
    }

    [Fact]
    public void InsertsAndDeletesCharacters()
    {
        var term = Emulate(6, 1, "abcdef\e[2G\e[2@");
        Assert.Equal("a  bcd", term.Buffer[0].GetText(0, 6).PadRight(6));

        term.Feed("\e[3P");
        Assert.Equal("acd", term.Buffer[0].GetText());
    }

    [Fact]
    public void InsertsAndDeletesLinesInsideTheScrollRegion()
    {
        var term = Emulate(3, 3, "a\r\nb\r\nc\e[1;1H\e[L");
        Assert.Equal("\na\nb", term.GetScreenText());

        term.Feed("\e[2M");
        Assert.Equal("b", term.GetScreenText());
    }

    [Fact]
    public void RepeatsTheLastCharacter()
    {
        Assert.Equal("-----", Emulate(10, 1, "-\e[4b").GetScreenText());
    }

    [Fact]
    public void TranslatesDecSpecialGraphics()
    {
        Assert.Equal("┌─┐q", Emulate(10, 1, "\e(0lqk\e(Bq").GetScreenText());
    }

    [Fact]
    public void TabsAdvanceToEveryEighthColumn()
    {
        var term = Emulate(20, 1, "a\tb");

        Assert.Equal("b", term.Buffer[0][8].Text);
    }

    [Fact]
    public void AnswersDeviceQueries()
    {
        var term = new TerminalEmulator(80, 24);
        var replies = new List<string>();
        term.Reply += (_, r) => replies.Add(r);

        term.Feed("\e[c\e[5;10H\e[6n\e[?2026$p\e[?9999$p\e[18t");

        Assert.Equal("\e[?62;22;52c\e[5;10R\e[?2026;2$y\e[?9999;0$y\e[8;24;80t", string.Concat(replies));
    }

    [Fact]
    public void AnswersColourQueriesWithTheSameTerminator()
    {
        var term = new TerminalEmulator(10, 2) { Palette = TerminalPalette.FromScheme(ColorScheme.Campbell) };
        var replies = new List<string>();
        term.Reply += (_, r) => replies.Add(r);

        term.Feed("\e]11;?\a");
        term.Feed("\e]10;?\e\\");

        Assert.Equal(["\e]11;rgb:0c0c/0c0c/0c0c\a", "\e]10;rgb:cccc/cccc/cccc\e\\"], replies);
    }

    [Fact]
    public void RaisesTitleProgressNotificationAndClipboardEvents()
    {
        var term = new TerminalEmulator(10, 2);
        var events = new List<string>();
        term.TitleChanged += (_, _) => events.Add("title:" + term.Title);
        term.ProgressChanged += (_, _) => events.Add($"progress:{term.Progress.State}:{term.Progress.Value}");
        term.NotificationRequested += (_, n) => events.Add("notify:" + n);
        term.ClipboardWriteRequested += (_, c) => events.Add("clip:" + c);
        term.Bell += (_, _) => events.Add("bell");

        term.Feed("\e]0;✳ grants\a\e]9;4;1;42\a\e]9;Claude needs your permission\a\e]52;c;aGk=\a\a");

        Assert.Equal(["title:✳ grants", "progress:Normal:42", "notify:Claude needs your permission", "clip:hi", "bell"], events);
    }

    [Fact]
    public void TracksModesApplicationsCareAbout()
    {
        var term = Emulate(10, 2, "\e[?2004h\e[?2026h\e[?1004h\e[?1h\e[?25l\e[5 q");

        Assert.True(term.Modes.BracketedPaste);
        Assert.True(term.Modes.SynchronizedOutput);
        Assert.True(term.Modes.FocusEvents);
        Assert.True(term.Modes.ApplicationCursorKeys);
        Assert.False(term.Modes.CursorVisible);
        Assert.Equal(new CursorStyle(CursorShape.Bar, true), term.RequestedCursorStyle);
    }

    [Fact]
    public void ShrinkingKeepsTheCursorLineVisible()
    {
        var term = Emulate(10, 4, "1\r\n2\r\n3\r\n4");

        term.Resize(5, 2);

        Assert.Equal("3\n4", term.GetScreenText());
        Assert.Equal(1, term.CursorRow);
        Assert.Equal(2, term.Buffer.ScrollbackCount);
    }

    [Fact]
    public void ShrinkingDropsBlankRowsBelowTheCursorFirst()
    {
        var term = Emulate(10, 4, "1\r\n2");

        term.Resize(10, 2);

        Assert.Equal("1\n2", term.GetScreenText());
        Assert.Equal(0, term.Buffer.ScrollbackCount);
    }

    [Fact]
    public void FullResetClearsEverything()
    {
        var term = Emulate(10, 2, "\e]0;t\a\e[31mabc\r\n\r\n\ec");

        Assert.Equal(string.Empty, term.GetScreenText());
        Assert.Equal(string.Empty, term.Title);
        Assert.Equal(0, term.Buffer.ScrollbackCount);
    }

    [Fact]
    public void VersionChangesOnEveryFeed()
    {
        var term = new TerminalEmulator(10, 2);
        var before = term.Version;

        term.Feed("x");

        Assert.NotEqual(before, term.Version);
    }

    private static TerminalEmulator Emulate(int columns, int rows, string input)
    {
        var term = new TerminalEmulator(columns, rows);
        term.Feed(input);
        return term;
    }
}
