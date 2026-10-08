using CCUI.Core.Claude;
using CCUI.Core.Tests.Fixtures;
using Microsoft.Extensions.Time.Testing;

namespace CCUI.Core.Tests;

public sealed class SessionCatalogTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("ccui-catalog").FullName;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    public void Dispose() => Directory.Delete(_home, recursive: true);

    [Fact]
    public void SummarisesSessions()
    {
        var path = Write("C--source-grants", Transcript.SessionId,
            Transcript.User("<local-command-stdout>x</local-command-stdout>"),
            Transcript.User("Deploy the card", branch: "main"),
            Transcript.Title("First name"),
            Transcript.Title("Renamed"),
            Transcript.LastPrompt("how many?"),
            Transcript.User("later", branch: "release"));

        var summary = Assert.Single(Catalog().Scan(TimeSpan.FromDays(1)));

        Assert.Equal(Transcript.SessionId, summary.SessionId);
        Assert.Equal(path, summary.TranscriptPath);
        Assert.Equal(Transcript.Cwd, summary.WorkingDirectory);
        Assert.Equal("Renamed", summary.Title);
        Assert.Equal("Deploy the card", summary.FirstPrompt);
        Assert.Equal("how many?", summary.LastPrompt);
        Assert.Equal("release", summary.GitBranch);
        Assert.Equal("grants", summary.Project);
        Assert.Equal("Renamed", summary.DisplayName);
    }

    [Fact]
    public void ReadsOnlyWhatWasAppended()
    {
        var path = Write("p", Transcript.SessionId, Transcript.User("one"));
        var catalog = Catalog();
        Assert.Null(Assert.Single(catalog.Scan(TimeSpan.FromDays(1))).Title);

        File.AppendAllText(path, Transcript.Title("Later title") + "\n");

        Assert.Equal("Later title", Assert.Single(catalog.Scan(TimeSpan.FromDays(1))).Title);
    }

    [Fact]
    public void SkipsOldEmptyAndNonSessionFiles()
    {
        var old = Write("p", "11111111-1111-1111-1111-111111111111", Transcript.User("old"));
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        Write("p", "22222222-2222-2222-2222-222222222222");
        Write("p", "agent-abc", Transcript.User("subagent"));
        Directory.CreateDirectory(Path.Combine(_home, "projects", "p", Transcript.SessionId, "subagents"));
        File.WriteAllText(Path.Combine(_home, "projects", "p", Transcript.SessionId, "subagents", "agent-1.jsonl"), Transcript.User("x"));

        Assert.Empty(Catalog().Scan(TimeSpan.FromDays(7)));
    }

    [Fact]
    public void SortsNewestFirst()
    {
        var a = Write("p", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", Transcript.User("a"));
        var b = Write("p", "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", Transcript.User("b"));
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddMinutes(-1));
        File.SetLastWriteTimeUtc(b, DateTime.UtcNow.AddMinutes(-5));

        Assert.Equal(["a", "b"], Catalog().Scan(TimeSpan.FromDays(1)).Select(s => s.FirstPrompt));
    }

    [Fact]
    public void HousekeepingLinesDoNotCountAsActivity()
    {
        // Minutes after a turn, Claude Code appends an "away" recap (and titles, cost state, …) to the transcript.
        // The file is newer, but the conversation is not, so the list must not move the session to the top.
        const string recap = """{"type":"system","subtype":"away_summary","content":"recap: …","timestamp":"2026-09-27T10:20:00.000Z"}""";
        var path = Write("p", Transcript.SessionId,
            Transcript.User("one", "2026-09-27T10:00:00Z"),
            Transcript.Assistant("m1", Transcript.Text("done"), "2026-09-27T10:05:00Z", stop: "end_turn"),
            recap);
        var written = new DateTime(2026, 9, 27, 10, 20, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        var summary = Assert.Single(Catalog().Scan(TimeSpan.FromDays(365)));

        Assert.Equal(new DateTimeOffset(2026, 9, 27, 10, 5, 0, TimeSpan.Zero), summary.LastActive);
        Assert.Equal(new DateTimeOffset(written), summary.LastWrite);
    }

    [Fact]
    public void FindsTranscriptsByProjectFolderOrSearch()
    {
        var paths = new ClaudePaths(_home);
        var expected = Write(ClaudePaths.ProjectFolderName(@"C:\src\app.v2"), Transcript.SessionId, Transcript.User("x"));

        Assert.Equal("C--src-app-v2", ClaudePaths.ProjectFolderName(@"C:\src\app.v2"));
        Assert.Equal(expected, paths.FindTranscript(Transcript.SessionId, @"C:\src\app.v2"));
        Assert.Equal(expected, paths.FindTranscript(Transcript.SessionId, @"C:\elsewhere"));
        Assert.Null(paths.FindTranscript(Guid.NewGuid().ToString(), null));
    }

    private SessionCatalog Catalog() => new(new ClaudePaths(_home), _time);

    private string Write(string project, string name, params string[] lines)
    {
        var directory = Path.Combine(_home, "projects", project);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".jsonl");
        File.WriteAllText(path, string.Concat(lines.Select(l => l + "\n")));
        return path;
    }
}
