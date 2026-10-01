using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CCUI.Core.Claude;

/// <summary>
/// Decodes Claude Code transcript lines (~/.claude/projects/&lt;dir&gt;/&lt;session&gt;.jsonl). The format is not a
/// public contract, so everything is read defensively: unknown record types and fields are ignored.
/// </summary>
public static partial class TranscriptParser
{
    private static readonly string[] SyntheticPromptPrefixes = ["<local-command-stdout>", "<local-command-stderr>", "<system-reminder>", "<command-message>", "Caveat:"];

    public static IReadOnlyList<TranscriptEvent> Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            return Parse(doc.RootElement);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The prompt text of a user message, or null if the message is not a typed prompt (tool results, meta).</summary>
    public static string? PromptText(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            var type = Str(block, "type");
            if (type == "tool_result")
            {
                return null;
            }

            if (type == "text")
            {
                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(Str(block, "text"));
            }
            else if (type == "image")
            {
                sb.Append(sb.Length > 0 ? "\n" : string.Empty).Append("[image]");
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>True for slash-command wrappers and other text Claude Code injects as a "user" message.</summary>
    public static bool IsSyntheticPrompt(string text) =>
        SyntheticPromptPrefixes.Any(p => text.StartsWith(p, StringComparison.Ordinal));

    /// <summary>
    /// The slash command a user line holds, or null: "&lt;command-name&gt;/model&lt;/command-name&gt;&lt;command-args&gt;opus&lt;/command-args&gt;"
    /// becomes "/model opus". Newer Claude Code versions also log the typed command as plain text ("/compact"); a
    /// first word like "/name" counts, a path such as "/c/work/x" does not.
    /// </summary>
    public static string? SlashCommand(string text)
    {
        var name = Between(text, "<command-name>", "</command-name>");
        if (name is null)
        {
            return PlainSlashCommand().IsMatch(text) ? text.Trim() : null;
        }

        var args = Between(text, "<command-args>", "</command-args>");
        return string.IsNullOrWhiteSpace(args) ? name : $"{name} {args}";
    }

    private static List<TranscriptEvent> Parse(JsonElement root)
    {
        var events = new List<TranscriptEvent>();
        var type = Str(root, "type");
        var timestamp = Timestamp(root);
        var agentId = root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True
            ? Str(root, "agentId") ?? "subagent"
            : null;

        if (Str(root, "sessionId") is { } sessionId && Str(root, "cwd") is { } cwd && agentId is null)
        {
            events.Add(new SessionMetadataEvent(timestamp, sessionId, cwd, Str(root, "gitBranch"), Str(root, "version")));
        }

        switch (type)
        {
            case "user":
                ParseUser(root, timestamp, agentId, events);
                break;
            case "assistant":
                ParseAssistant(root, timestamp, agentId, events);
                break;
            case "system":
                var subtype = Str(root, "subtype") ?? "system";
                var text = Str(root, "content") ?? Str(root, "stopReason") ?? subtype.Replace('_', ' ');
                events.Add(new SystemEvent(timestamp, agentId, subtype, text, Str(root, "level")));
                break;
            case "custom-title" when Str(root, "customTitle") is { Length: > 0 } title:
                events.Add(new TitleEvent(timestamp, title));
                break;
        }

        return events;
    }

    private static void ParseUser(JsonElement root, DateTimeOffset timestamp, string? agentId, List<TranscriptEvent> events)
    {
        if (!root.TryGetProperty("message", out var message))
        {
            return;
        }

        if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (Str(block, "type") == "tool_result" && Str(block, "tool_use_id") is { } toolUseId)
                {
                    var isError = block.TryGetProperty("is_error", out var err) && err.ValueKind == JsonValueKind.True;
                    events.Add(new ToolResultEvent(timestamp, agentId, toolUseId, ContentText(block), isError));
                }
            }
        }

        // Meta lines, and the summary a compaction writes as a user message, are not prompts.
        var isMeta = IsTrue(root, "isMeta") || IsTrue(root, "isCompactSummary") || IsTrue(root, "isVisibleInTranscriptOnly");
        if (isMeta || PromptText(message) is not { } text)
        {
            return;
        }

        if (SlashCommand(text) is { } command)
        {
            events.Add(new UserPromptEvent(timestamp, agentId, command, IsSlashCommand: true));
        }
        else if (!IsSyntheticPrompt(text))
        {
            events.Add(new UserPromptEvent(timestamp, agentId, text, IsSlashCommand: false));
        }
    }

    [GeneratedRegex(@"^/[A-Za-z][\w:.-]*(\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex PlainSlashCommand();

    private static bool IsTrue(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static void ParseAssistant(JsonElement root, DateTimeOffset timestamp, string? agentId, List<TranscriptEvent> events)
    {
        if (!root.TryGetProperty("message", out var message))
        {
            return;
        }

        var messageId = Str(message, "id") ?? Str(root, "uuid") ?? Guid.NewGuid().ToString();
        if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                switch (Str(block, "type"))
                {
                    case "text" when Str(block, "text") is { Length: > 0 } text:
                        events.Add(new AssistantTextEvent(timestamp, agentId, messageId, text));
                        break;
                    case "thinking" when Str(block, "thinking") is { Length: > 0 } thinking:
                        events.Add(new ThinkingEvent(timestamp, agentId, messageId, thinking));
                        break;
                    case "tool_use":
                        var input = block.TryGetProperty("input", out var i) ? i.GetRawText() : "{}";
                        events.Add(new ToolUseEvent(timestamp, agentId, messageId, Str(block, "id") ?? string.Empty, Str(block, "name") ?? "tool", input));
                        break;
                }
            }
        }

        if (message.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            events.Add(new AssistantUsageEvent(
                timestamp,
                agentId,
                messageId,
                Str(message, "model"),
                new TokenUsage(Long(usage, "input_tokens"), Long(usage, "output_tokens"), Long(usage, "cache_creation_input_tokens"), Long(usage, "cache_read_input_tokens")),
                Str(message, "stop_reason")));
        }
    }

    private static string ContentText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var item in content.EnumerateArray())
        {
            var text = Str(item, "type") switch
            {
                "text" => Str(item, "text"),
                "image" => "[image]",
                _ => null,
            };

            if (text is not null)
            {
                sb.Append(sb.Length > 0 ? "\n" : string.Empty).Append(text);
            }
        }

        return sb.ToString();
    }

    private static DateTimeOffset Timestamp(JsonElement root) =>
        Str(root, "timestamp") is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : DateTimeOffset.MinValue;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    private static string? Between(string text, string start, string end)
    {
        var s = text.IndexOf(start, StringComparison.Ordinal);
        if (s < 0)
        {
            return null;
        }

        s += start.Length;
        var e = text.IndexOf(end, s, StringComparison.Ordinal);
        return e < 0 ? null : text[s..e].Trim();
    }
}
