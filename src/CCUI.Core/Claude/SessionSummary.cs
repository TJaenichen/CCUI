namespace CCUI.Core.Claude;

/// <summary>What the session list shows about a session without loading its whole transcript.</summary>
public sealed record SessionSummary
{
    public required string SessionId { get; init; }

    public required string TranscriptPath { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? GitBranch { get; init; }

    public string? Version { get; init; }

    /// <summary>The /rename name, if any.</summary>
    public string? Title { get; init; }

    public string? FirstPrompt { get; init; }

    public string? LastPrompt { get; init; }

    /// <summary>
    /// When the conversation last moved: the latest user or assistant line. Housekeeping lines Claude Code appends
    /// later (the away recap, titles, cost state) do not count, so the session list does not reorder on them.
    /// </summary>
    public DateTimeOffset LastActive { get; init; }

    /// <summary>When the transcript file was last written, housekeeping included.</summary>
    public DateTimeOffset LastWrite { get; init; }

    public long Length { get; init; }

    public string ShortId => SessionId.Length >= 8 ? SessionId[..8] : SessionId;

    public string Project => WorkingDirectory is { Length: > 0 } dir ? PathNames.LastSegment(dir) : "?";

    public string DisplayName => Title
        ?? (FirstPrompt is { Length: > 0 } prompt ? ToolSummaries.Shorten(prompt, 60) : null)
        ?? ShortId;
}
