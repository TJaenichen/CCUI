namespace CCUI.Core.Claude;

public enum TimelineItemKind
{
    Prompt,
    Response,
    Thinking,
    ToolCall,
    System,
}

/// <summary>One entry of a session's timeline. Tool calls are updated in place when their result arrives.</summary>
public sealed class TimelineItem
{
    public required long Sequence { get; init; }

    public required TimelineItemKind Kind { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public string? AgentId { get; init; }

    /// <summary>The main text: prompt, response, thinking, system message, or the tool name for tool calls.</summary>
    public required string Text { get; init; }

    public string? ToolUseId { get; init; }

    public string? ToolInputJson { get; init; }

    public string? ToolResult { get; internal set; }

    public bool IsError { get; internal set; }

    public DateTimeOffset? CompletedAt { get; internal set; }

    public TimeSpan? Duration => CompletedAt is { } done ? done - Timestamp : null;

    public bool IsRunning => Kind == TimelineItemKind.ToolCall && CompletedAt is null;

    /// <summary>Output tokens of the response this item belongs to, when known.</summary>
    public long? OutputTokens { get; internal set; }

    public string? MessageId { get; init; }
}

/// <summary>A subagent started by a Task/Agent tool call that has not returned yet.</summary>
public sealed record RunningAgent(string ToolUseId, string Description, string? AgentType, DateTimeOffset Started);

public enum SessionActivity
{
    Idle,
    Working,
    WaitingForUser,
}

/// <summary>A snapshot of a session's numbers for the pane header.</summary>
public sealed record SessionStatistics
{
    public int Prompts { get; init; }

    public int Responses { get; init; }

    public int ToolCalls { get; init; }

    public int ToolErrors { get; init; }

    public TokenUsage Tokens { get; init; }

    /// <summary>Context size of the latest main-conversation request.</summary>
    public long ContextTokens { get; init; }

    public string? Model { get; init; }

    public DateTimeOffset? FirstActivity { get; init; }

    public DateTimeOffset? LastActivity { get; init; }

    /// <summary>When the running turn started (the latest prompt), or null when idle.</summary>
    public DateTimeOffset? TurnStartedAt { get; init; }

    public TimeSpan LastTurnDuration { get; init; }

    public TimeSpan TotalTurnTime { get; init; }

    public SessionActivity Activity { get; init; }

    public IReadOnlyList<RunningAgent> RunningAgents { get; init; } = [];
}

/// <summary>What changed when events were applied, for incremental view updates.</summary>
public sealed record TimelineChanges(IReadOnlyList<TimelineItem> Added, IReadOnlyList<TimelineItem> Updated, IReadOnlyList<ActivityPulse> Pulses);

public enum ActivityDirection
{
    /// <summary>Content that goes to the model with the next request: prompts and tool results.</summary>
    Sent,

    /// <summary>Content the model produced: text, thinking and tool calls.</summary>
    Received,
}

/// <summary>A burst of traffic for the activity meters; <see cref="Size"/> is in characters.</summary>
public readonly record struct ActivityPulse(ActivityDirection Direction, long Size)
{
    /// <summary>The traffic an event stands for, or null for events that carry none (metadata, usage).</summary>
    public static ActivityPulse? For(TranscriptEvent e) => e switch
    {
        UserPromptEvent prompt => new ActivityPulse(ActivityDirection.Sent, prompt.Text.Length),
        ToolResultEvent result => new ActivityPulse(ActivityDirection.Sent, result.Content.Length),
        AssistantTextEvent text => new ActivityPulse(ActivityDirection.Received, text.Text.Length),
        ThinkingEvent thinking => new ActivityPulse(ActivityDirection.Received, thinking.Text.Length),
        ToolUseEvent tool => new ActivityPulse(ActivityDirection.Received, tool.InputJson.Length),
        _ => null,
    };
}

/// <summary>
/// Folds transcript events into timeline items and statistics. Not thread-safe; feed it from one thread.
/// </summary>
public sealed class SessionTimeline
{
    private readonly List<TimelineItem> _items = [];
    private readonly Dictionary<string, TimelineItem> _toolCalls = [];
    private readonly Dictionary<string, RunningAgent> _agents = [];
    private readonly HashSet<string> _countedMessages = [];
    private long _sequence;
    private DateTimeOffset? _turnStart;
    private DateTimeOffset? _lastEventInTurn;

    public IReadOnlyList<TimelineItem> Items => _items;

    public SessionStatistics Statistics { get; private set; } = new();

    public string? SessionId { get; private set; }

    public string? WorkingDirectory { get; private set; }

    public string? GitBranch { get; private set; }

    public string? Title { get; private set; }

    public TimelineChanges Apply(IEnumerable<TranscriptEvent> events)
    {
        var added = new List<TimelineItem>();
        var updated = new List<TimelineItem>();
        var pulses = new List<ActivityPulse>();
        var stats = Statistics;

        foreach (var e in events)
        {
            if (ActivityPulse.For(e) is { } pulse)
            {
                pulses.Add(pulse);
            }

            if (e.Timestamp != DateTimeOffset.MinValue)
            {
                stats = stats with
                {
                    FirstActivity = stats.FirstActivity ?? e.Timestamp,
                    LastActivity = stats.LastActivity is { } last && last > e.Timestamp ? last : e.Timestamp,
                };
            }

            switch (e)
            {
                case SessionMetadataEvent meta:
                    SessionId = meta.SessionId;
                    WorkingDirectory = meta.WorkingDirectory ?? WorkingDirectory;
                    GitBranch = meta.GitBranch ?? GitBranch;
                    break;

                case TitleEvent title:
                    Title = title.Title;
                    break;

                case UserPromptEvent prompt:
                    added.Add(Add(TimelineItemKind.Prompt, prompt, prompt.Text));
                    if (prompt.AgentId is null && !prompt.IsSlashCommand)
                    {
                        stats = EndTurn(stats);
                        _turnStart = prompt.Timestamp;
                        _lastEventInTurn = prompt.Timestamp;
                        stats = stats with
                        {
                            Prompts = stats.Prompts + 1,
                            TurnStartedAt = prompt.Timestamp,
                            Activity = SessionActivity.Working,
                        };
                    }

                    break;

                case AssistantTextEvent text:
                    added.Add(Add(TimelineItemKind.Response, text, text.Text, messageId: text.MessageId));
                    stats = stats with { Responses = stats.Responses + (text.AgentId is null ? 1 : 0) };
                    TouchTurn(text);
                    break;

                case ThinkingEvent thinking:
                    added.Add(Add(TimelineItemKind.Thinking, thinking, thinking.Text, messageId: thinking.MessageId));
                    TouchTurn(thinking);
                    break;

                case ToolUseEvent tool:
                    var call = Add(TimelineItemKind.ToolCall, tool, tool.Name, tool.ToolUseId, tool.InputJson, tool.MessageId);
                    _toolCalls[tool.ToolUseId] = call;
                    added.Add(call);
                    stats = stats with { ToolCalls = stats.ToolCalls + 1, Activity = SessionActivity.Working };
                    if (ToolSummaries.IsAgentTool(tool.Name))
                    {
                        _agents[tool.ToolUseId] = new RunningAgent(tool.ToolUseId, ToolSummaries.Describe(tool.Name, tool.InputJson, 80), ToolSummaries.AgentType(tool.InputJson), tool.Timestamp);
                    }

                    TouchTurn(tool);
                    break;

                case ToolResultEvent result:
                    if (_toolCalls.Remove(result.ToolUseId, out var pending))
                    {
                        pending.ToolResult = result.Content;
                        pending.IsError = result.IsError;
                        pending.CompletedAt = result.Timestamp;
                        updated.Add(pending);
                    }

                    _agents.Remove(result.ToolUseId);
                    stats = stats with { ToolErrors = stats.ToolErrors + (result.IsError ? 1 : 0) };
                    TouchTurn(result);
                    break;

                case AssistantUsageEvent usage:
                    if (_countedMessages.Add(usage.MessageId))
                    {
                        stats = stats with { Tokens = stats.Tokens + usage.Usage };
                        foreach (var item in _items.Where(i => i.MessageId == usage.MessageId && i.OutputTokens is null))
                        {
                            item.OutputTokens = usage.Usage.Output;
                        }
                    }

                    TouchTurn(usage);
                    if (usage.AgentId is null)
                    {
                        stats = stats with { ContextTokens = usage.Usage.Context, Model = usage.Model ?? stats.Model };
                        if (usage.StopReason is "end_turn" or "stop_sequence" or "max_tokens" or "refusal")
                        {
                            stats = EndTurn(stats) with { Activity = SessionActivity.WaitingForUser };
                        }
                    }

                    break;

                case SystemEvent system:
                    added.Add(Add(TimelineItemKind.System, system, system.Text));
                    break;
            }
        }

        Statistics = stats with { RunningAgents = [.. _agents.Values.OrderBy(a => a.Started)] };
        return new TimelineChanges(added, updated, pulses);
    }

    private TimelineItem Add(TimelineItemKind kind, TranscriptEvent e, string text, string? toolUseId = null, string? input = null, string? messageId = null)
    {
        var item = new TimelineItem
        {
            Sequence = ++_sequence,
            Kind = kind,
            Timestamp = e.Timestamp,
            AgentId = e.AgentId,
            Text = text,
            ToolUseId = toolUseId,
            ToolInputJson = input,
            MessageId = messageId,
        };
        _items.Add(item);
        return item;
    }

    private void TouchTurn(TranscriptEvent e)
    {
        if (e.AgentId is null && _turnStart is not null && e.Timestamp != DateTimeOffset.MinValue
            && (_lastEventInTurn is null || e.Timestamp > _lastEventInTurn))
        {
            _lastEventInTurn = e.Timestamp;
        }
    }

    /// <summary>Closes the running turn, adding its length to the totals.</summary>
    private SessionStatistics EndTurn(SessionStatistics stats)
    {
        if (_turnStart is not { } started)
        {
            return stats;
        }

        var ended = _lastEventInTurn is { } last && last > started ? last : started;
        var duration = ended - started;
        _turnStart = null;
        _lastEventInTurn = null;
        return stats with
        {
            TurnStartedAt = null,
            LastTurnDuration = duration,
            TotalTurnTime = stats.TotalTurnTime + duration,
        };
    }
}
