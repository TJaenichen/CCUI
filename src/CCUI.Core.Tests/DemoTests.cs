using CCUI.Core.Claude;
using CCUI.Core.Demo;
using CCUI.Core.Sessions;
using CCUI.Core.Settings;
using CCUI.Terminal;
using Microsoft.Extensions.Options;

namespace CCUI.Core.Tests;

public sealed class DemoTests
{
    [Fact]
    public void PainterOutputRendersLikeClaudeCode()
    {
        var painter = new ClaudeTuiPainter { Width = 100 };
        var term = new TerminalEmulator(100, 40);

        term.Feed(painter.Welcome("grants", @"C:\source\grants"));
        term.Feed(painter.Tool(new DemoTool("Bash", "{}", "git status", ["one", "two", "three", "four"], 1)));
        term.Feed(painter.Say("Done ✅ 🎉"));
        var (lines, _, _) = painter.Bottom(new DemoBottomState("Baking", 3, TimeSpan.FromSeconds(12), 1200, "typed", "grants", 185_005, 18, 16, 1));
        term.Feed(string.Join("\r\n", lines));

        var screen = term.GetScreenText();
        Assert.Contains("Claude Code v2.1.283", screen, StringComparison.Ordinal);
        Assert.Contains("▐▛███▜▌", screen, StringComparison.Ordinal);
        Assert.Contains("⏺ Bash(git status)", screen, StringComparison.Ordinal);
        Assert.Contains("⎿  one", screen, StringComparison.Ordinal);
        Assert.Contains("+1 lines", screen, StringComparison.Ordinal);
        Assert.Contains("⏺ Done ✅ 🎉", screen, StringComparison.Ordinal);
        Assert.Contains("✶ Baking… (12s · ↓ 1.2k tokens", screen, StringComparison.Ordinal);
        Assert.Contains("❯ typed", screen, StringComparison.Ordinal);
        Assert.Contains("185,005 tokens", screen, StringComparison.Ordinal);
        Assert.Contains("1 agent running", screen, StringComparison.Ordinal);
    }

    [Fact]
    public void BottomLinesFitTheWidth()
    {
        var painter = new ClaudeTuiPainter { Width = 80 };
        var (lines, inputLine, inputColumn) = painter.Bottom(new DemoBottomState(null, 0, TimeSpan.Zero, 0, "ab\ncd", "p", 0, 0, 0, 0));
        var term = new TerminalEmulator(80, 20);
        term.Feed(string.Join("\r\n", lines));

        Assert.All(Enumerable.Range(0, term.Rows), r => Assert.False(term.Buffer[r].IsWrapped));
        Assert.Equal("  cd", term.Buffer[inputLine].GetText());
        Assert.Equal(4, inputColumn);
    }

    [Fact]
    public void NarrowPanesDoNotWrap()
    {
        var painter = new ClaudeTuiPainter { Width = 44 };
        var (lines, _, inputColumn) = painter.Bottom(new DemoBottomState("Delegating", 1, TimeSpan.FromSeconds(3), 900, "a long typed prompt that is wider than the pane", "homelab", 12_345, 30, 16, 2));
        var term = new TerminalEmulator(44, 20);
        term.Feed(string.Join("\r\n", lines));

        Assert.All(Enumerable.Range(0, term.Rows), r => Assert.False(term.Buffer[r].IsWrapped));
        Assert.True(inputColumn < 44);
    }

    [Fact]
    public void CatalogIsStable()
    {
        var catalog = new DemoSessionCatalog(Options.Create(new DemoOptions { Sessions = 3 }), TimeProvider.System);

        var sessions = catalog.Scan(TimeSpan.FromDays(1));

        Assert.Equal(3, sessions.Count);
        Assert.Equal(DemoSessionCatalog.SessionIdFor(0), sessions[0].SessionId);
        Assert.All(sessions, s => Assert.True(Guid.TryParse(s.SessionId, out _)));
        Assert.Equal("Quick-profile card rollout", sessions[0].DisplayName);
    }

    [Fact]
    public async Task PlaysATurnWithMatchingTerminalOutputAndTranscriptEvents()
    {
        var launcher = new DemoSessionLauncher(
            Options.Create(new DemoOptions { Speed = 60, Seed = 1 }),
            Options.Create(new TerminalOptions { DefaultColumns = 100, DefaultRows = 40 }),
            TimeProvider.System);
        await using var runtime = launcher.Launch(new SessionLaunchRequest("pane", @"C:\source\blog", DemoSessionCatalog.SessionIdFor(3)));
        var timeline = new SessionTimeline();
        var done = new TaskCompletionSource();
        runtime.Feed.EventsArrived += (_, events) =>
        {
            lock (timeline)
            {
                timeline.Apply(events);
            }
        };
        runtime.Feed.HookReceived += (_, hook) => done.TrySetResult();

        runtime.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        lock (timeline)
        {
            Assert.Equal(1, timeline.Statistics.Prompts);
            Assert.Equal(SessionActivity.WaitingForUser, timeline.Statistics.Activity);
            Assert.All(timeline.Items.Where(i => i.Kind == TimelineItemKind.ToolCall), call => Assert.NotNull(call.ToolResult));
            Assert.True(timeline.Statistics.Tokens.Output > 0);
        }

        var screen = runtime.Terminal.Emulator.GetScreenText();
        Assert.Contains("> Draft a short post about hosting ConPTY", screen, StringComparison.Ordinal);
        Assert.Contains("⏺ Write(posts/2026-09-conpty.md)", screen, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypedPromptsGetAnAnswer()
    {
        var launcher = new DemoSessionLauncher(
            Options.Create(new DemoOptions { Speed = 60 }),
            Options.Create(new TerminalOptions { DefaultColumns = 100, DefaultRows = 40 }),
            TimeProvider.System);
        await using var runtime = launcher.Launch(new SessionLaunchRequest("pane", @"C:\source\blog", DemoSessionCatalog.SessionIdFor(3)));
        var answered = new TaskCompletionSource<string>();
        runtime.Feed.EventsArrived += (_, events) =>
        {
            foreach (var text in events.OfType<AssistantTextEvent>().Where(t => t.Text.Contains("ÄÖÜ", StringComparison.Ordinal)))
            {
                answered.TrySetResult(text.Text);
            }
        };

        runtime.Start();
        runtime.Terminal.SendText("hi ÄÖÜ @€ 😀x");
        runtime.Terminal.SendText("\x7f\r");

        var answer = await answered.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Assert.Contains("“hi ÄÖÜ @€ 😀”", answer, StringComparison.Ordinal);
    }
}
