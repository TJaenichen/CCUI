namespace CCUI.Core.Claude;

/// <summary>Token counts of one API response, as Claude Code records them.</summary>
public readonly record struct TokenUsage(long Input, long Output, long CacheCreation, long CacheRead)
{
    /// <summary>Everything the model read for the request: fresh input plus cache writes and reads.</summary>
    public long Context => Input + CacheCreation + CacheRead;

    public static TokenUsage operator +(TokenUsage a, TokenUsage b) =>
        new(a.Input + b.Input, a.Output + b.Output, a.CacheCreation + b.CacheCreation, a.CacheRead + b.CacheRead);
}

/// <summary>Something that happened in a session, decoded from one transcript line.</summary>
/// <param name="AgentId">Set for events from a subagent (sidechain); null for the main conversation.</param>
public abstract record TranscriptEvent(DateTimeOffset Timestamp, string? AgentId);

public sealed record UserPromptEvent(DateTimeOffset Timestamp, string? AgentId, string Text, bool IsSlashCommand)
    : TranscriptEvent(Timestamp, AgentId);

public sealed record AssistantTextEvent(DateTimeOffset Timestamp, string? AgentId, string MessageId, string Text)
    : TranscriptEvent(Timestamp, AgentId);

public sealed record ThinkingEvent(DateTimeOffset Timestamp, string? AgentId, string MessageId, string Text)
    : TranscriptEvent(Timestamp, AgentId);

public sealed record ToolUseEvent(DateTimeOffset Timestamp, string? AgentId, string MessageId, string ToolUseId, string Name, string InputJson)
    : TranscriptEvent(Timestamp, AgentId);

public sealed record ToolResultEvent(DateTimeOffset Timestamp, string? AgentId, string ToolUseId, string Content, bool IsError)
    : TranscriptEvent(Timestamp, AgentId);

/// <summary>The usage and model of an assistant message. Claude Code writes one line per content block, all
/// carrying the same message id and usage; consumers must count each message id once.</summary>
public sealed record AssistantUsageEvent(DateTimeOffset Timestamp, string? AgentId, string MessageId, string? Model, TokenUsage Usage, string? StopReason)
    : TranscriptEvent(Timestamp, AgentId);

public sealed record SystemEvent(DateTimeOffset Timestamp, string? AgentId, string Subtype, string Text, string? Level)
    : TranscriptEvent(Timestamp, AgentId);

/// <summary>Session-level metadata carried on most lines.</summary>
public sealed record SessionMetadataEvent(DateTimeOffset Timestamp, string SessionId, string? WorkingDirectory, string? GitBranch, string? Version)
    : TranscriptEvent(Timestamp, null);

/// <summary>A name given with /rename (the last one wins).</summary>
public sealed record TitleEvent(DateTimeOffset Timestamp, string Title) : TranscriptEvent(Timestamp, null);
