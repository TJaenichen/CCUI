using CCUI.Core.Claude;
using CCUI.Core.Tests.Fixtures;

namespace CCUI.Core.Tests;

public sealed class TranscriptParserTests
{
    [Fact]
    public void ReadsTypedPromptsAndSessionMetadata()
    {
        var events = TranscriptParser.Parse(Transcript.User("Deploy it", branch: "feature/x"));

        var meta = Assert.Single(events.OfType<SessionMetadataEvent>());
        Assert.Equal((Transcript.SessionId, Transcript.Cwd, "feature/x"), (meta.SessionId, meta.WorkingDirectory, meta.GitBranch));
        var prompt = Assert.Single(events.OfType<UserPromptEvent>());
        Assert.Equal("Deploy it", prompt.Text);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero), prompt.Timestamp);
    }

    [Fact]
    public void JoinsTextBlocksAndMarksImages()
    {
        var events = TranscriptParser.Parse(Transcript.UserBlocks([new { type = "image" }, new { type = "text", text = "look" }]));

        Assert.Equal("[image]\nlook", Assert.Single(events.OfType<UserPromptEvent>()).Text);
    }

    [Fact]
    public void SkipsMetaAndInjectedMessages()
    {
        Assert.Empty(TranscriptParser.Parse(Transcript.User("injected", meta: true)).OfType<UserPromptEvent>());
        Assert.Empty(TranscriptParser.Parse(Transcript.User("<local-command-stdout>ok</local-command-stdout>")).OfType<UserPromptEvent>());
    }

    [Fact]
    public void TurnsSlashCommandsIntoReadableCommands()
    {
        var events = TranscriptParser.Parse(Transcript.User("<command-message>model</command-message>\n<command-name>/model</command-name>\n<command-args>opus</command-args>"));

        var prompt = Assert.Single(events.OfType<UserPromptEvent>());
        Assert.True(prompt.IsSlashCommand);
        Assert.Equal("/model opus", prompt.Text);
    }

    [Theory]
    [InlineData("/compact", "/compact")]
    [InlineData("/model opus", "/model opus")]
    [InlineData("/plugin:skill args\nmore", "/plugin:skill args\nmore")]
    public void RecognisesSlashCommandsLoggedAsPlainText(string text, string expected)
    {
        var prompt = Assert.Single(TranscriptParser.Parse(Transcript.User(text)).OfType<UserPromptEvent>());

        Assert.True(prompt.IsSlashCommand);
        Assert.Equal(expected, prompt.Text);
    }

    [Theory]
    [InlineData("/c/work/app is broken")]
    [InlineData("/ is the root")]
    [InlineData("fix /compact handling")]
    public void KeepsPromptsThatOnlyLookLikeCommands(string text) =>
        Assert.False(Assert.Single(TranscriptParser.Parse(Transcript.User(text)).OfType<UserPromptEvent>()).IsSlashCommand);

    [Fact]
    public void SkipsTheCompactionSummary()
    {
        const string line = """{"type":"user","isCompactSummary":true,"isVisibleInTranscriptOnly":true,"message":{"role":"user","content":"This session is being continued from a previous conversation."},"timestamp":"2026-10-01T17:19:58.000Z"}""";

        Assert.Empty(TranscriptParser.Parse(line).OfType<UserPromptEvent>());
    }

    [Fact]
    public void ReadsToolResults()
    {
        var events = TranscriptParser.Parse(Transcript.ToolResult("toolu_1", "Exit code 1", isError: true));

        var result = Assert.Single(events.OfType<ToolResultEvent>());
        Assert.Equal(("toolu_1", "Exit code 1", true), (result.ToolUseId, result.Content, result.IsError));
        Assert.Empty(events.OfType<UserPromptEvent>());
    }

    [Fact]
    public void ReadsAssistantBlocksWithUsage()
    {
        var events = TranscriptParser.Parse(Transcript.Assistant("msg_1", Transcript.ToolUse("toolu_1", "Bash", new { command = "ls" })));

        var tool = Assert.Single(events.OfType<ToolUseEvent>());
        Assert.Equal(("msg_1", "toolu_1", "Bash"), (tool.MessageId, tool.ToolUseId, tool.Name));
        Assert.Contains("\"ls\"", tool.InputJson, StringComparison.Ordinal);
        var usage = Assert.Single(events.OfType<AssistantUsageEvent>());
        Assert.Equal(new TokenUsage(2, 100, 1000, 40000), usage.Usage);
        Assert.Equal(41002, usage.Usage.Context);
        Assert.Equal("tool_use", usage.StopReason);
    }

    [Fact]
    public void MarksSidechainEventsWithTheAgent()
    {
        var events = TranscriptParser.Parse(Transcript.Assistant("msg_2", Transcript.Text("sub"), sidechain: true));

        Assert.Equal("agent-1", Assert.Single(events.OfType<AssistantTextEvent>()).AgentId);
        Assert.Empty(events.OfType<SessionMetadataEvent>());
    }

    [Fact]
    public void ReadsTitlesAndIgnoresUnknownRecords()
    {
        Assert.Equal("Quick profile", Assert.IsType<TitleEvent>(Assert.Single(TranscriptParser.Parse(Transcript.Title("Quick profile")))).Title);
        Assert.Empty(TranscriptParser.Parse("""{"type":"queue-operation","operation":"enqueue"}"""));
        Assert.Empty(TranscriptParser.Parse("{not json"));
    }
}
