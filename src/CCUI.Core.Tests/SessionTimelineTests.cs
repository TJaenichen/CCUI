using CCUI.Core.Claude;
using CCUI.Core.Tests.Fixtures;

namespace CCUI.Core.Tests;

public sealed class SessionTimelineTests
{
    [Fact]
    public void PairsToolCallsWithResultsAndMeasuresThem()
    {
        var timeline = Feed(
            Transcript.User("go", "2026-09-27T10:00:00Z"),
            Transcript.Assistant("m1", Transcript.ToolUse("t1", "Bash", new { command = "dotnet test" }), "2026-09-27T10:00:02Z"),
            Transcript.ToolResult("t1", "Passed!", time: "2026-09-27T10:00:09.500Z"));

        var call = Assert.Single(timeline.Items, i => i.Kind == TimelineItemKind.ToolCall);
        Assert.Equal("Passed!", call.ToolResult);
        Assert.Equal(TimeSpan.FromSeconds(7.5), call.Duration);
        Assert.False(call.IsRunning);
    }

    [Fact]
    public void ReportsUpdatedItemsWhenResultsArrive()
    {
        var timeline = new SessionTimeline();
        timeline.Apply(Events(Transcript.Assistant("m1", Transcript.ToolUse("t1", "Read", new { file_path = "a.cs" }))));

        var changes = timeline.Apply(Events(Transcript.ToolResult("t1", "ok")));

        Assert.Empty(changes.Added);
        Assert.Single(changes.Updated);
    }

    [Fact]
    public void CountsEachMessagesUsageOnce()
    {
        // Claude Code writes one line per content block, each repeating the message's usage.
        var timeline = Feed(
            Transcript.Assistant("m1", Transcript.Thinking("hmm"), output: 300),
            Transcript.Assistant("m1", Transcript.Text("done"), output: 300),
            Transcript.Assistant("m2", Transcript.Text("more"), output: 50));

        Assert.Equal(350, timeline.Statistics.Tokens.Output);
        Assert.Equal(2, timeline.Statistics.Responses);
        Assert.Equal("claude-opus-5-5", timeline.Statistics.Model);
        Assert.Equal(41002, timeline.Statistics.ContextTokens);
    }

    [Fact]
    public void TracksTurnsFromPromptToTheEndOfTheAnswer()
    {
        var timeline = Feed(
            Transcript.User("first", "2026-09-27T10:00:00Z"),
            Transcript.Assistant("m1", Transcript.Text("working"), "2026-09-27T10:00:04Z", stop: "tool_use"));

        Assert.Equal(SessionActivity.Working, timeline.Statistics.Activity);
        Assert.NotNull(timeline.Statistics.TurnStartedAt);

        timeline.Apply(Events(Transcript.Assistant("m2", Transcript.Text("done"), "2026-09-27T10:00:30Z", stop: "end_turn")));

        Assert.Equal(SessionActivity.WaitingForUser, timeline.Statistics.Activity);
        Assert.Null(timeline.Statistics.TurnStartedAt);
        Assert.Equal(TimeSpan.FromSeconds(30), timeline.Statistics.LastTurnDuration);

        timeline.Apply(Events(
            Transcript.User("second", "2026-09-27T10:05:00Z"),
            Transcript.Assistant("m3", Transcript.Text("ok"), "2026-09-27T10:05:10Z", stop: "end_turn")));

        Assert.Equal(TimeSpan.FromSeconds(40), timeline.Statistics.TotalTurnTime);
        Assert.Equal(2, timeline.Statistics.Prompts);
    }

    [Fact]
    public void CompactingAfterATurnLeavesTheSessionWaiting()
    {
        // As logged by Claude Code 2.1: the typed command as plain text, the summary as a user message, then the
        // command again in tags and its output.
        const string summary = """{"type":"user","isCompactSummary":true,"isVisibleInTranscriptOnly":true,"message":{"role":"user","content":"This session is being continued from a previous conversation."},"timestamp":"2026-09-27T10:02:00.000Z"}""";
        var timeline = Feed(
            Transcript.User("first", "2026-09-27T10:00:00Z"),
            Transcript.Assistant("m1", Transcript.Text("done"), "2026-09-27T10:00:04Z", stop: "end_turn"),
            Transcript.User("/compact", "2026-09-27T10:01:00Z"),
            summary,
            Transcript.User("<command-name>/compact</command-name>\n<command-message>compact</command-message>\n<command-args></command-args>", "2026-09-27T10:01:00Z"),
            Transcript.User("<local-command-stdout>Compacted</local-command-stdout>", "2026-09-27T10:02:01Z"));

        Assert.Equal(SessionActivity.WaitingForUser, timeline.Statistics.Activity);
        Assert.Equal(1, timeline.Statistics.Prompts);
    }

    [Theory]
    [InlineData("[Request interrupted by user]")]
    [InlineData("[Request interrupted by user for tool use]")]
    public void CancellingATurnLeavesTheSessionWaiting(string marker)
    {
        // As logged by Claude Code 2.1: the cancelled tool's result, then the marker as a user message. No end_turn follows.
        var timeline = Feed(
            Transcript.User("first", "2026-09-27T10:00:00Z"),
            Transcript.Assistant("m1", Transcript.ToolUse("t1", "Bash", new { command = "sleep 60" }), "2026-09-27T10:00:02Z"),
            Transcript.ToolResult("t1", "interrupted", isError: true, time: "2026-09-27T10:00:10Z"),
            Transcript.UserBlocks([new { type = "text", text = marker }], "2026-09-27T10:00:10Z"));

        Assert.Equal(SessionActivity.WaitingForUser, timeline.Statistics.Activity);
        Assert.Null(timeline.Statistics.TurnStartedAt);
        Assert.Equal(TimeSpan.FromSeconds(10), timeline.Statistics.LastTurnDuration);
        Assert.Equal(1, timeline.Statistics.Prompts);
        Assert.DoesNotContain(timeline.Items, i => i.Kind == TimelineItemKind.Prompt && i.Text == marker);
    }

    [Fact]
    public void ListsSubagentsUntilTheyReturn()
    {
        var timeline = Feed(Transcript.Assistant("m1", Transcript.ToolUse("t9", "Task", new { description = "Find the tagger", subagent_type = "Explore", prompt = "…" })));

        var agent = Assert.Single(timeline.Statistics.RunningAgents);
        Assert.Equal(("Find the tagger", "Explore"), (agent.Description, agent.AgentType));

        timeline.Apply(Events(Transcript.ToolResult("t9", "found it")));

        Assert.Empty(timeline.Statistics.RunningAgents);
    }

    [Fact]
    public void EmitsActivityPulsesInBothDirections()
    {
        var changes = new SessionTimeline().Apply(Events(
            Transcript.User("hello"),
            Transcript.Assistant("m1", Transcript.Text("hi there"))));

        Assert.Contains(new ActivityPulse(ActivityDirection.Sent, 5), changes.Pulses);
        Assert.Contains(new ActivityPulse(ActivityDirection.Received, 8), changes.Pulses);
    }

    [Fact]
    public void PicksUpTitleAndBranch()
    {
        var timeline = Feed(Transcript.User("x", branch: "release"), Transcript.Title("Named"));

        Assert.Equal(("Named", "release", Transcript.SessionId), (timeline.Title, timeline.GitBranch, timeline.SessionId));
    }

    private static SessionTimeline Feed(params string[] lines)
    {
        var timeline = new SessionTimeline();
        timeline.Apply(Events(lines));
        return timeline;
    }

    private static List<TranscriptEvent> Events(params string[] lines) => [.. lines.SelectMany(TranscriptParser.Parse)];
}
