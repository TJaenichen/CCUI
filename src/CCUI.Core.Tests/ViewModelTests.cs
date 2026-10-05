using CCUI.Core.Claude;
using CCUI.Core.Sessions;
using CCUI.Core.Settings;
using CCUI.Core.Tests.Fixtures;
using CCUI.Core.Threading;
using CCUI.Core.ViewModels;
using CCUI.Core.Workspace;
using CCUI.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CCUI.Core.Tests;

public sealed class ViewModelTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeLauncher _launcher = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly MemoryStore _store = new();

    [Fact]
    public void PaneFoldsTranscriptEventsIntoStatsDetailAndMeters()
    {
        var pane = Pane(out var feed);

        feed.Push(
            Transcript.User("Deploy", "2026-09-27T10:00:00Z"),
            Transcript.Assistant("m1", Transcript.ToolUse("t1", "Bash", new { command = "dotnet test" }), "2026-09-27T10:00:01Z"),
            Transcript.ToolResult("t1", "Passed!", time: "2026-09-27T10:00:05Z"),
            Transcript.Assistant("m2", Transcript.Text("Done ✅"), "2026-09-27T10:00:07Z", stop: "end_turn", output: 1500));

        Assert.Equal(3, pane.Detail.All.Count);
        Assert.Equal(2, pane.Detail.Messages.Count);
        var tool = Assert.IsType<ToolCallItemViewModel>(Assert.Single(pane.Detail.Tools));
        Assert.Equal(("Bash", "dotnet test", "4.0s", "Passed!"), (tool.ToolName, tool.Intro, tool.Metric, tool.Result));
        Assert.Equal("Opus 5.5", pane.Stats.Model);
        Assert.Equal("7.0s", pane.Stats.TurnTime);
        Assert.Equal(1, pane.Stats.ToolCalls);
        Assert.Equal(SessionActivity.WaitingForUser, pane.Activity);
        Assert.True(pane.SentLevel.Level > 0);
        Assert.True(pane.ReceivedLevel.Level > 0);
    }

    [Fact]
    public void SubagentActivityMovesTheMetersOnly()
    {
        var pane = Pane(out var feed);

        feed.PushSubagent(
            Transcript.Assistant("a1", Transcript.ToolUse("t9", "Bash", new { command = "dotnet build" }), sidechain: true),
            Transcript.ToolResult("t9", "error CS1002", isError: true));

        Assert.True(pane.ReceivedLevel.Level > 0);
        Assert.True(pane.SentLevel.Level > 0);
        Assert.Empty(pane.Detail.All);
        Assert.Equal((0, 0), (pane.Stats.ToolCalls, pane.Stats.ToolErrors));
    }

    [Fact]
    public void ASubmittedPromptShowsWorkingBeforeTheTranscriptHasIt()
    {
        var pane = Pane(out var feed);
        feed.Push(
            Transcript.User("first", "2026-09-27T10:00:00Z"),
            Transcript.Assistant("m1", Transcript.Text("done"), "2026-09-27T10:00:04Z", stop: "end_turn"));
        Assert.Equal(SessionActivity.WaitingForUser, pane.Activity);

        feed.Hook(Hook("UserPromptSubmit", "second"));
        Assert.Equal(SessionActivity.Working, pane.Activity);

        // Unrelated transcript lines do not undo it; the prompt arriving hands over to the transcript.
        feed.Push(Transcript.Assistant("m1", Transcript.Text("late line"), "2026-09-27T10:00:05Z", stop: "end_turn"));
        Assert.Equal(SessionActivity.Working, pane.Activity);
        feed.Push(
            Transcript.User("second", "2026-09-27T10:01:00Z"),
            Transcript.Assistant("m2", Transcript.Text("ok"), "2026-09-27T10:01:30Z", stop: "end_turn"));
        Assert.Equal(SessionActivity.WaitingForUser, pane.Activity);
    }

    [Theory]
    [InlineData("/compact")]
    [InlineData("<command-name>/model</command-name>")]
    public void SubmittedSlashCommandsDoNotStartATurn(string prompt)
    {
        var pane = Pane(out var feed);

        feed.Hook(Hook("UserPromptSubmit", prompt));

        Assert.NotEqual(SessionActivity.Working, pane.Activity);
    }

    [Fact]
    public void AnInterruptedTurnEnds()
    {
        var pane = Pane(out var feed);

        feed.Push(
            Transcript.User("long job", "2026-09-27T10:00:00Z"),
            Transcript.Assistant("m1", Transcript.ToolUse("t1", "Bash", new { command = "sleep 100" }), "2026-09-27T10:00:02Z"),
            Transcript.User("[Request interrupted by user for tool use]", "2026-09-27T10:00:09Z"));

        Assert.Equal(SessionActivity.WaitingForUser, pane.Activity);
        Assert.Equal(1, pane.Stats.Prompts);
    }

    private static HookEvent Hook(string name, string? message) => new(name, "s1", null, null, message, DateTimeOffset.MinValue);

    [Fact]
    public void RunningTurnTimeTicks()
    {
        var pane = Pane(out var feed);
        feed.Push(Transcript.User("go", "2026-09-27T10:00:00Z"));

        pane.Tick(_time.GetUtcNow().AddSeconds(42));

        Assert.True(pane.Stats.IsWorking);
        Assert.Equal("42s", pane.Stats.TurnTime);
    }

    [Fact]
    public void AttentionIsRaisedOnlyWhileUnfocusedAndClearedByFocus()
    {
        var pane = Pane(out var feed);
        feed.Hook(new HookEvent("Notification", null, null, null, "Claude needs your permission to use Bash", _time.GetUtcNow()));

        Assert.True(pane.NeedsAttention);
        Assert.Equal("Claude needs your permission to use Bash", pane.AttentionMessage);

        pane.IsActive = true;
        Assert.False(pane.NeedsAttention);

        feed.Hook(new HookEvent("Notification", null, null, null, "again", _time.GetUtcNow()));
        Assert.False(pane.NeedsAttention);
    }

    [Fact]
    public void SessionSwitchStartsAFreshTimeline()
    {
        var pane = Pane(out var feed);
        feed.Push(Transcript.User("before"));

        feed.Switch("new-session");

        Assert.Equal("new-session", pane.SessionId);
        Assert.Empty(pane.Detail.All);
    }

    [Theory]
    [InlineData("✳ grants", "grants")]
    [InlineData("✻ Fixing tests", "Fixing tests")]
    [InlineData("◐ ccui", "ccui")]
    [InlineData("◓  Unit test coverage", "Unit test coverage")]
    [InlineData("⠐ build", "build")]
    [InlineData("plain", "plain")]
    [InlineData("2 ◐ things", "2 ◐ things")]
    public void CleansTerminalTitles(string raw, string expected) => Assert.Equal(expected, SessionPaneViewModel.CleanTitle(raw));

    [Fact]
    public async Task OpeningASessionTwiceActivatesTheExistingPane()
    {
        var shell = Shell();
        var item = new SessionListItemViewModel(new SessionSummary { SessionId = "s1", TranscriptPath = "x", WorkingDirectory = @"C:\src\app" });

        shell.OpenSessionCommand.Execute(item);
        shell.OpenSessionCommand.Execute(item);

        var pane = Assert.Single(shell.Panes);
        Assert.Same(pane, shell.ActivePane);
        Assert.True(pane.IsActive);
        Assert.Equal(new SessionLaunchRequest(pane.PaneId, @"C:\src\app", "s1"), Assert.Single(_launcher.Requests));
        await shell.DisposeAsync();
    }

    [Fact]
    public async Task OpeningAnAgentRowDoesNothing()
    {
        var shell = Shell();

        shell.OpenSessionCommand.Execute(new AgentViewModel(new RunningAgent("t1", "Find things", "Explore", _time.GetUtcNow())));

        Assert.Empty(shell.Panes);
        await shell.DisposeAsync();
    }

    [Fact]
    public async Task RunningElsewhereAsksFirst()
    {
        var shell = Shell();
        var item = new SessionListItemViewModel(new SessionSummary { SessionId = "s1", TranscriptPath = "x", WorkingDirectory = "/w" }) { RunningElsewhere = true };
        _dialogs.ConfirmResult = false;

        shell.OpenSessionCommand.Execute(item);

        Assert.Empty(shell.Panes);
        Assert.Single(_dialogs.Confirmations);
        await shell.DisposeAsync();
    }

    [Fact]
    public async Task NewSessionsStartInThePickedFolder()
    {
        var shell = Shell();
        _dialogs.Folder = @"C:\src\new";

        shell.NewSessionCommand.Execute(null);

        var request = Assert.Single(_launcher.Requests);
        Assert.Equal((@"C:\src\new", null), (request.WorkingDirectory, request.ResumeSessionId));
        await shell.DisposeAsync();
    }

    [Fact]
    public async Task WorkspaceRoundTripsThroughRestore()
    {
        var shell = Shell();
        shell.LayoutProvider = () => "<layout/>";
        shell.OpenSessionCommand.Execute(new SessionListItemViewModel(new SessionSummary { SessionId = "s1", TranscriptPath = "x", WorkingDirectory = "/a" }));
        shell.Panes[0].IsDetailOpen = true;

        var state = shell.CaptureWorkspace();
        await shell.DisposeAsync();

        Assert.Equal("<layout/>", state.DockLayout);
        var saved = Assert.Single(state.Panes);
        Assert.Equal(("s1", "/a", true), (saved.SessionId, saved.WorkingDirectory, saved.DetailOpen));

        _launcher.Requests.Clear();
        var restored = Shell();
        await restored.StartAsync(state);

        Assert.Equal(new SessionLaunchRequest(saved.PaneId, "/a", "s1"), Assert.Single(_launcher.Requests));
        Assert.True(restored.Panes[0].IsDetailOpen);
        await restored.DisposeAsync();
    }

    [Fact]
    public async Task ClosingAPaneDisposesItsSession()
    {
        var shell = Shell();
        shell.OpenSessionCommand.Execute(new SessionListItemViewModel(new SessionSummary { SessionId = "s1", TranscriptPath = "x", WorkingDirectory = "/a" }));

        await shell.ClosePaneCommand.ExecuteAsync(shell.Panes[0]);

        Assert.Empty(shell.Panes);
        Assert.True(_launcher.Feeds.Single().Disposed);
        Assert.Null(shell.ActivePane);
        await shell.DisposeAsync();
    }

    [Fact]
    public void SessionListFiltersAndKeepsOpenSessionsFirst()
    {
        var list = new SessionListViewModel();
        var now = _time.GetUtcNow();
        SessionSummary S(string id, string cwd, int minutesAgo) => new() { SessionId = id, TranscriptPath = id, WorkingDirectory = cwd, LastActive = now.AddMinutes(-minutesAgo) };

        list.Update([S("a", @"C:\src\grants", 1), S("b", @"C:\src\blog", 5), S("c", @"C:\src\ccui", 9)], new HashSet<string>(), new HashSet<string>(), id => null, now);
        Assert.Equal(["a", "b", "c"], list.Items.Select(i => i.SessionId));

        list.FilterText = "BLOG";
        Assert.Equal(["b"], list.Items.Select(i => i.SessionId));

        list.FilterText = string.Empty;
        Assert.Equal("1m ago", list.Items[0].LastActiveText);
    }

    [Fact]
    public void FailedToolCallsAreListedInOrderOnceTheirResultArrives()
    {
        var pane = Pane(out var feed);
        feed.Push(
            Transcript.Assistant("m1", Transcript.ToolUse("t1", "Bash", new { command = "one" }), "2026-09-27T10:00:01Z"),
            Transcript.Assistant("m2", Transcript.ToolUse("t2", "Bash", new { command = "two" }), "2026-09-27T10:00:02Z"),
            Transcript.Assistant("m3", Transcript.ToolUse("t3", "Bash", new { command = "three" }), "2026-09-27T10:00:03Z"));
        Assert.Empty(pane.Detail.Failed);

        // Results arrive in separate batches, the later call failing first.
        feed.Push(Transcript.ToolResult("t3", "boom", isError: true), Transcript.ToolResult("t2", "fine"));
        feed.Push(Transcript.ToolResult("t1", "bang", isError: true));

        Assert.Equal(["one", "three"], pane.Detail.Failed.Cast<ToolCallItemViewModel>().Select(t => t.Intro));
        Assert.Equal(2, pane.Stats.ToolErrors);

        pane.ShowFailedToolsCommand.Execute(null);
        Assert.True(pane.IsDetailOpen);
        Assert.Equal(SessionDetailViewModel.FailedTab, pane.Detail.SelectedTab);
    }

    [Theory]
    [InlineData(0, 2, 2)]
    [InlineData(2, 2, 0)]
    [InlineData(2, 4, 4)]
    [InlineData(0, 0, 0)]
    public void SelectingTheCurrentFilterAgainClearsIt(int current, int clicked, int expected)
    {
        var detail = new SessionDetailViewModel { SelectedTab = current };

        detail.SelectTab(clicked);

        Assert.Equal(expected, detail.SelectedTab);
    }

    [Fact]
    public void RebootFlagsCanBeDismissedOneByOneAndStayDismissed()
    {
        var list = new SessionListViewModel();
        var now = _time.GetUtcNow();
        SessionSummary S(string id) => new() { SessionId = id, TranscriptPath = id, WorkingDirectory = "/w", LastActive = now };
        var killed = new HashSet<string> { "a", "b" };

        list.Update([S("a"), S("b"), S("c")], killed, new HashSet<string>(), id => null, now);
        Assert.Equal(2, list.KilledAtRebootCount);

        list.DismissReboot([list.Find("a")!]);
        Assert.Equal(1, list.KilledAtRebootCount);
        Assert.False(list.Find("a")!.KilledAtReboot);

        // The next scan reports the same sessions; the dismissal holds.
        list.Update([S("a"), S("b"), S("c")], killed, new HashSet<string>(), id => null, now);
        Assert.Equal(["b"], list.KilledAtReboot().Select(i => i.SessionId));
        Assert.Equal(["a"], list.DismissedReboot);

        // Once the detector stops reporting a session, its dismissal is forgotten.
        list.Update([S("a"), S("b"), S("c")], new HashSet<string> { "b" }, new HashSet<string>(), id => null, now);
        Assert.Empty(list.DismissedReboot);
    }

    [Fact]
    public async Task DismissedRebootFlagsAreSavedWithTheWorkspace()
    {
        var shell = Shell();
        var now = _time.GetUtcNow();
        shell.SessionList.Update([new SessionSummary { SessionId = "a", TranscriptPath = "a", WorkingDirectory = "/w", LastActive = now }], new HashSet<string> { "a" }, new HashSet<string>(), id => null, now);

        shell.DismissKilledSessionsCommand.Execute(null);

        Assert.Equal(0, shell.SessionList.KilledAtRebootCount);
        Assert.Equal(["a"], _store.State!.DismissedRebootSessions);

        var restored = Shell();
        await restored.StartAsync(_store.State);
        Assert.Equal(["a"], restored.SessionList.DismissedReboot);
        await shell.DisposeAsync();
        await restored.DisposeAsync();
    }

    private SessionPaneViewModel Pane(out FakeFeed feed)
    {
        var runtime = _launcher.Launch(new SessionLaunchRequest("pane", @"C:\src\grants", "s1"));
        feed = _launcher.Feeds[^1];
        var pane = new SessionPaneViewModel("pane", @"C:\src\grants", runtime, new PaneSettings(200_000, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(900)), new ImmediateDispatcher(), _time);
        pane.Start();
        return pane;
    }

    private ShellViewModel Shell() => new(
        new ShellServices(_launcher, new EmptyCatalog(), _store, new ImmediateDispatcher(), _dialogs, new NullClipboard(), new NoClaudeProcesses(), _time, NullLogger<ShellViewModel>.Instance),
        Options.Create(new ClaudeOptions()),
        Options.Create(new SessionListOptions()),
        Options.Create(new WorkspaceOptions { LaunchStaggerMilliseconds = 0, AutoSaveSeconds = 0 }),
        Options.Create(new AppearanceOptions()),
        Options.Create(new DemoOptions()));

    private sealed class FakeLauncher : ISessionLauncher
    {
        public List<SessionLaunchRequest> Requests { get; } = [];

        public List<FakeFeed> Feeds { get; } = [];

        public ClaudeSessionRuntime Launch(SessionLaunchRequest request)
        {
            Requests.Add(request);
            var feed = new FakeFeed();
            Feeds.Add(feed);
            var terminal = new TerminalSession(new OpenConnection(), 80, 24, 100);
            return new ClaudeSessionRuntime(request.ResumeSessionId ?? "new", terminal, feed);
        }
    }

    /// <summary>A process that never exits and ignores input.</summary>
    private sealed class OpenConnection : ITerminalConnection
    {
        public event EventHandler<ReadOnlyMemory<byte>>? DataReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<int?>? Exited
        {
            add { }
            remove { }
        }

        public void Start(int columns, int rows)
        {
        }

        public void Write(ReadOnlySpan<byte> data)
        {
        }

        public void Resize(int columns, int rows)
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFeed : ITranscriptFeed
    {
        public event EventHandler<IReadOnlyList<TranscriptEvent>>? EventsArrived;

        public event EventHandler<IReadOnlyList<TranscriptEvent>>? SubagentEventsArrived;

        public event EventHandler<HookEvent>? HookReceived;

        public event EventHandler<string>? SessionSwitched;

        public bool Disposed { get; private set; }

        public void Start()
        {
        }

        public void Dispose() => Disposed = true;

        public void Push(params string[] lines) => EventsArrived?.Invoke(this, [.. lines.SelectMany(TranscriptParser.Parse)]);

        public void PushSubagent(params string[] lines) => SubagentEventsArrived?.Invoke(this, [.. lines.SelectMany(TranscriptParser.Parse)]);

        public void Hook(HookEvent hook) => HookReceived?.Invoke(this, hook);

        public void Switch(string sessionId) => SessionSwitched?.Invoke(this, sessionId);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public string? Folder { get; set; }

        public bool ConfirmResult { get; set; } = true;

        public List<string> Confirmations { get; } = [];

        public string? PickFolder(string title, string? initialDirectory) => Folder;

        public bool Confirm(string title, string message)
        {
            Confirmations.Add(message);
            return ConfirmResult;
        }

        public void ShowError(string title, string message)
        {
        }
    }

    private sealed class MemoryStore : IWorkspaceStore
    {
        public WorkspaceState? State { get; private set; }

        public WorkspaceState? Load() => State;

        public void Save(WorkspaceState state) => State = state;
    }

    private sealed class EmptyCatalog : ISessionCatalog
    {
        public IReadOnlyList<SessionSummary> Scan(TimeSpan maxAge) => [];
    }

    private sealed class NullClipboard : IClipboardService
    {
        public void SetText(string text)
        {
        }
    }
}
