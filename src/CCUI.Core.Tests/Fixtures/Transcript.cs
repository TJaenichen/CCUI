using System.Text.Json;

namespace CCUI.Core.Tests.Fixtures;

/// <summary>Builds transcript lines in Claude Code's format (as observed in version 2.1.x).</summary>
internal static class Transcript
{
    public const string SessionId = "6105bb7b-8dc6-5439-93fd-5d47f6eaa760";
    public const string Cwd = @"C:\source\grants";

    public static string User(string text, string time = "2026-09-27T10:00:00.000Z", bool meta = false, string branch = "main") => Line(new Dictionary<string, object?>
    {
        ["type"] = "user",
        ["isMeta"] = meta ? true : null,
        ["message"] = new { role = "user", content = text },
        ["timestamp"] = time,
        ["gitBranch"] = branch,
    });

    public static string UserBlocks(object[] content, string time = "2026-09-27T10:00:00.000Z") => Line(new Dictionary<string, object?>
    {
        ["type"] = "user",
        ["message"] = new { role = "user", content },
        ["timestamp"] = time,
    });

    public static string ToolResult(string toolUseId, string content, bool isError = false, string time = "2026-09-27T10:00:05.000Z") =>
        UserBlocks([new { type = "tool_result", tool_use_id = toolUseId, content, is_error = isError }], time);

    public static string Assistant(string messageId, object block, string time = "2026-09-27T10:00:02.000Z", string stop = "tool_use", long output = 100, bool sidechain = false) => Line(new Dictionary<string, object?>
    {
        ["type"] = "assistant",
        ["isSidechain"] = sidechain,
        ["agentId"] = sidechain ? "agent-1" : null,
        ["message"] = new
        {
            id = messageId,
            model = "claude-opus-5-5",
            role = "assistant",
            content = new[] { block },
            stop_reason = stop,
            usage = new { input_tokens = 2, output_tokens = output, cache_creation_input_tokens = 1000, cache_read_input_tokens = 40000 },
        },
        ["timestamp"] = time,
    });

    public static object Text(string text) => new { type = "text", text };

    public static object Thinking(string thinking) => new { type = "thinking", thinking, signature = "x" };

    public static object ToolUse(string id, string name, object input) => new { type = "tool_use", id, name, input };

    public static string Title(string title) => JsonSerializer.Serialize(new { type = "custom-title", customTitle = title, sessionId = SessionId });

    public static string LastPrompt(string prompt) => JsonSerializer.Serialize(new { type = "last-prompt", lastPrompt = prompt, sessionId = SessionId });

    private static string Line(Dictionary<string, object?> fields)
    {
        fields.TryAdd("sessionId", SessionId);
        fields.TryAdd("cwd", Cwd);
        fields.TryAdd("version", "2.1.283");
        fields.TryAdd("uuid", Guid.NewGuid().ToString());
        return JsonSerializer.Serialize(fields.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
