using CCUI.Core.Claude;

namespace CCUI.Core.Tests;

public sealed class RebootDetectorTests
{
    private static readonly DateTimeOffset Boot = new(2026, 9, 27, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FlagsTheLargestGroupSharingALastWriteSecond()
    {
        var killedAt = Boot.AddMinutes(-2);
        var sessions = new[]
        {
            Session("a", killedAt.AddMilliseconds(100)),
            Session("b", killedAt.AddMilliseconds(700)),
            Session("c", killedAt.AddMilliseconds(900)),
            Session("closed", Boot.AddHours(-5)),
            Session("after-boot", Boot.AddMinutes(10)),
            Session("too-old", Boot.AddDays(-3)),
        };

        Assert.Equal(new HashSet<string> { "a", "b", "c" }, RebootDetector.FindKilled(sessions, Boot, TimeSpan.FromHours(24)));
    }

    [Fact]
    public void NeedsAtLeastTwoSessionsToCallItAReboot()
    {
        Assert.Empty(RebootDetector.FindKilled([Session("a", Boot.AddMinutes(-1))], Boot, TimeSpan.FromHours(24)));
    }

    private static SessionSummary Session(string id, DateTimeOffset lastWrite) => new() { SessionId = id, TranscriptPath = id, LastActive = lastWrite, LastWrite = lastWrite };
}
