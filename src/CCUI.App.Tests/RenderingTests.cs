using System.Windows;
using CCUI.App.Views;
using CCUI.Core.Claude;
using CCUI.Core.Sessions;
using CCUI.Core.Threading;
using CCUI.Core.ViewModels;
using CCUI.Terminal;
using CCUI.Terminal.Wpf;

namespace CCUI.App.Tests;

/// <summary>
/// Renders real controls with the app's resources. Each test saves a PNG to TestResults/snapshots and checks a few
/// pixels, which catches XAML that only fails at runtime and rendering regressions such as monochrome emoji.
/// </summary>
[Collection("Wpf")]
public sealed class RenderingTests(WpfFixture wpf)
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task TerminalDrawsColoursBoxDrawingAndColourEmoji() => wpf.Run(async () =>
    {
        // Row 0: an orange block bar; row 1: box drawing; row 2: text and a colour emoji at column 10.
        const string output = "\e[38;2;215;119;87m██████████\e[0m\r\n╭──────╮\r\n\e[97mDone ok  \e[0m😀\r\n\e[1;32mbold green\e[0m";
        var session = new TerminalSession(new ScriptedConnection(output), 40, 6, 100);
        var control = new TerminalControl { Session = session, Width = 520, Height = 140, Padding = new Thickness(0), FontSize = 16, UseColorEmoji = true };
        session.Start();

        WpfFixture.Render(control, 520, 140, "terminal-basics");
        await WpfFixture.Settle(100);
        var snapshot = WpfFixture.Render(control, 520, 140, "terminal-basics");

        var cell = control.CellSize;
        Assert.False(cell.IsEmpty);
        var orange = snapshot[(int)(cell.Width * 2), (int)(cell.Height / 2)];
        Assert.InRange(orange.R, 190, 240);
        Assert.InRange(orange.G, 100, 140);
        var emojiArea = new Int32Rect((int)(cell.Width * 10), (int)(cell.Height * 2), (int)(cell.Width * 2), (int)cell.Height);
        Assert.True(snapshot.HasColourIn(emojiArea), "The emoji should be drawn in colour.");
    });

    [Fact]
    public Task TerminalRendersTheDemoScreen() => wpf.Run(async () =>
    {
        var launcher = new Core.Demo.DemoSessionLauncher(
            Microsoft.Extensions.Options.Options.Create(new Core.Settings.DemoOptions { Speed = 40 }),
            Microsoft.Extensions.Options.Options.Create(new Core.Settings.TerminalOptions { DefaultColumns = 110, DefaultRows = 34 }),
            TimeProvider.System);
        var runtime = launcher.Launch(new SessionLaunchRequest("pane", @"C:\source\CCUI", Core.Demo.DemoSessionCatalog.SessionIdFor(1)));
        var control = new TerminalControl { Session = runtime.Terminal, FontSize = 14 };
        runtime.Start();

        await WpfFixture.Settle(6000);
        var snapshot = WpfFixture.Render(control, 1000, 620, "terminal-demo");

        Assert.True(snapshot.CountDistinctColours() > 20);
        await runtime.DisposeAsync();
    });

    [Fact]
    public Task SessionPaneShowsHeaderStatsAndDetail() => wpf.Run(async () =>
    {
        var feed = new ManualFeed();
        var runtime = new ClaudeSessionRuntime("6105bb7b-8dc6-5439-93fd-5d47f6eaa760", new TerminalSession(new ScriptedConnection("\e[90m> deploy it\e[0m\r\n\r\n⏺ Deploying…"), 80, 20, 100), feed);
        var pane = new SessionPaneViewModel("pane", @"C:\source\grants", runtime, new PaneSettings(200_000, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(900)), new ImmediateDispatcher(), TimeProvider.System)
        {
            IsDetailOpen = true,
            DetailHeight = 200,
        };
        pane.Start();
        feed.Push(
        [
            new SessionMetadataEvent(T0, pane.SessionId, @"C:\source\grants", "main", "2.1.283"),
            new UserPromptEvent(T0, null, "Deploy the quick-profile card to prod", false),
            new ToolUseEvent(T0.AddSeconds(2), null, "m1", "t1", "Bash", """{"command":"dotnet test","description":"Run the tests"}"""),
            new AssistantUsageEvent(T0.AddSeconds(2), null, "m1", "claude-opus-5-5", new TokenUsage(3, 800, 1_200, 40_000), "tool_use"),
            new ToolResultEvent(T0.AddSeconds(9), null, "t1", "Passed! 128 tests", false),
            new ToolUseEvent(T0.AddSeconds(10), null, "m2", "t2", "Task", """{"description":"Find the factor tagger","subagent_type":"Explore"}"""),
            new AssistantTextEvent(T0.AddSeconds(11), null, "m3", "Deploying revision 21 ✅"),
        ]);

        var view = new SessionPaneView { DataContext = pane };
        WpfFixture.Render(view, 1000, 640, "session-pane");
        await WpfFixture.Settle(150);
        var snapshot = WpfFixture.Render(view, 1000, 640, "session-pane");

        Assert.True(snapshot.CountDistinctColours() > 30);
        Assert.Equal(3, pane.Detail.All.Count);
    });

    [Fact]
    public Task SessionListShowsSessionsAndAgents() => wpf.Run(async () =>
    {
        var shell = TestShell.Create();
        var now = DateTimeOffset.UtcNow;
        shell.SessionList.Update(
        [
            new SessionSummary { SessionId = "a", TranscriptPath = "a", WorkingDirectory = @"C:\source\grants", GitBranch = "main", Title = "Quick-profile rollout", LastActive = now.AddMinutes(-2) },
            new SessionSummary { SessionId = "b", TranscriptPath = "b", WorkingDirectory = @"C:\source\blog", FirstPrompt = "Draft a post about ConPTY", LastActive = now.AddHours(-3) },
        ],
        new HashSet<string> { "b" },
        new HashSet<string>(),
        _ => null,
        now);

        var view = new SessionListView { DataContext = shell };
        WpfFixture.Render(view, 340, 420, "session-list");
        await WpfFixture.Settle(100);
        var snapshot = WpfFixture.Render(view, 340, 420, "session-list");

        Assert.True(snapshot.CountDistinctColours() > 10);
        Assert.Equal(1, shell.SessionList.KilledAtRebootCount);
    });
}
