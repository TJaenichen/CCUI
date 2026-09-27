using CCUI.Core.Claude;
using CCUI.Core.Sessions;
using CCUI.Core.Settings;
using CCUI.Core.Tests.Fixtures;
using Microsoft.Extensions.Time.Testing;

namespace CCUI.Core.Tests;

public sealed class LaunchAndHookTests : IDisposable
{
    private static readonly Guid FixedGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private readonly string _root = Directory.CreateTempSubdirectory("ccui-hooks").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void NewSessionsGetTheirIdUpFront()
    {
        var plan = ClaudeLaunchPlanner.Plan(new SessionLaunchRequest("pane1", @"C:\src", null), new ClaudeOptions(), Env(), hooks: null, () => FixedGuid);

        Assert.Equal(FixedGuid.ToString(), plan.SessionId);
        Assert.Equal(["--session-id", FixedGuid.ToString()], plan.Arguments);
    }

    [Fact]
    public void ResumesByFullIdAndAppendsHooksAndExtraArguments()
    {
        var hooks = new HookEnvironment(_root, @"C:\Apps\CCUI\CCUI.exe");
        var options = new ClaudeOptions { Arguments = ["--model", "opus"] };

        var plan = ClaudeLaunchPlanner.Plan(new SessionLaunchRequest("pane1", @"C:\src", Transcript.SessionId), options, Env(), hooks, () => FixedGuid);

        Assert.Equal(["--resume", Transcript.SessionId, "--settings", hooks.SettingsFilePath, "--model", "opus"], plan.Arguments);
        Assert.Equal("pane1", plan.Environment[ClaudeLaunchPlanner.PaneIdVariable]);
        Assert.Equal(_root, plan.Environment[ClaudeLaunchPlanner.HookDirectoryVariable]);
    }

    [Fact]
    public void CleansTheEnvironment()
    {
        var options = new ClaudeOptions
        {
            RemoveEnvironmentVariables = ["NO_COLOR", "CLAUDE_CODE_CHILD_SESSION"],
            SetEnvironmentVariables = new() { ["COLORTERM"] = "truecolor" },
        };

        var plan = ClaudeLaunchPlanner.Plan(new SessionLaunchRequest("p", "/", null), options, Env(("no_color", "1"), ("CLAUDE_CODE_CHILD_SESSION", "1"), ("PATH", "x")), null, () => FixedGuid);

        Assert.False(plan.Environment.ContainsKey("NO_COLOR"));
        Assert.False(plan.Environment.ContainsKey("CLAUDE_CODE_CHILD_SESSION"));
        Assert.Equal("truecolor", plan.Environment["COLORTERM"]);
        Assert.Equal("x", plan.Environment["Path"]);
    }

    [Fact]
    public void HookSettingsUseForwardSlashesAndCoverTheNeededEvents()
    {
        var json = HookEnvironment.BuildSettingsJson(@"C:\Program Files\CCUI\CCUI.exe");

        Assert.Contains("\\\"C:/Program Files/CCUI/CCUI.exe\\\" --hook", json, StringComparison.Ordinal);
        foreach (var name in new[] { "SessionStart", "Notification", "Stop" })
        {
            Assert.Contains($"\"{name}\"", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RelayAppendsThePayloadForThePane()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 7, 0, 0, TimeSpan.Zero));
        var env = new Dictionary<string, string> { [ClaudeLaunchPlanner.PaneIdVariable] = "pane7", [ClaudeLaunchPlanner.HookDirectoryVariable] = _root };

        var exit = HookEnvironment.Relay(new StringReader("""{"hook_event_name":"SessionStart","session_id":"abc","source":"clear","transcript_path":"/t.jsonl"}"""), env.GetValueOrDefault, time);

        Assert.Equal(0, exit);
        var hook = HookEvent.Parse(File.ReadAllLines(Path.Combine(_root, "pane7.jsonl")).Single());
        Assert.Equal(new HookEvent("SessionStart", "abc", "/t.jsonl", "clear", null, time.GetUtcNow()), hook);
    }

    [Fact]
    public void RelayIgnoresCallsFromOtherSessions()
    {
        Assert.Equal(0, HookEnvironment.Relay(new StringReader("{}"), _ => null, TimeProvider.System));
        Assert.Empty(Directory.GetFiles(_root, "*.jsonl"));
    }

    [Fact]
    public void FeedFollowsTheSessionAfterClear()
    {
        var home = Path.Combine(_root, "claude");
        var project = Path.Combine(home, "projects", "p");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "old.jsonl"), Transcript.User("before clear") + "\n");
        var newTranscript = Path.Combine(project, "new.jsonl");
        File.WriteAllText(newTranscript, Transcript.User("after clear") + "\n");
        var hookFile = Path.Combine(_root, "pane.jsonl");
        File.WriteAllText(hookFile, string.Empty);

        using var feed = new FileTranscriptFeed(new ClaudePaths(home), "old", @"C:\x", hookFile, TimeSpan.FromSeconds(1), TimeProvider.System);
        var prompts = new List<string>();
        var switched = new List<string>();
        feed.EventsArrived += (_, events) => prompts.AddRange(events.OfType<UserPromptEvent>().Select(p => p.Text));
        feed.SessionSwitched += (_, id) => switched.Add(id);

        feed.Poll();
        var hookLine = System.Text.Json.JsonSerializer.Serialize(new
        {
            receivedAt = "2026-09-27T07:00:00Z",
            payload = new { hook_event_name = "SessionStart", session_id = "new", source = "clear", transcript_path = newTranscript },
        });
        File.AppendAllText(hookFile, hookLine + "\n");
        feed.Poll();

        Assert.Equal(["before clear", "after clear"], prompts);
        Assert.Equal(["new"], switched);
    }

    private static Dictionary<string, string> Env(params (string Key, string Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
}
