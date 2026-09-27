using System.Text.Json;

namespace CCUI.Core.Claude;

/// <summary>One-line descriptions of tool calls for list "intros", e.g. <c>Bash · git status</c>.</summary>
public static class ToolSummaries
{
    private static readonly Dictionary<string, string[]> KeyFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bash"] = ["description", "command"],
        ["PowerShell"] = ["description", "command"],
        ["Read"] = ["file_path"],
        ["Write"] = ["file_path"],
        ["Edit"] = ["file_path"],
        ["MultiEdit"] = ["file_path"],
        ["NotebookEdit"] = ["notebook_path"],
        ["Grep"] = ["pattern"],
        ["Glob"] = ["pattern"],
        ["WebFetch"] = ["url"],
        ["WebSearch"] = ["query"],
        ["Task"] = ["description"],
        ["Agent"] = ["description"],
        ["Skill"] = ["skill"],
        ["ToolSearch"] = ["query"],
    };

    /// <summary>The most telling input value of a tool call, single-lined and shortened.</summary>
    public static string Describe(string toolName, string inputJson, int maxLength = 120)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            var input = doc.RootElement;
            if (input.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            if (KeyFields.TryGetValue(toolName, out var fields))
            {
                foreach (var field in fields)
                {
                    if (input.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } s)
                    {
                        return Shorten(s, maxLength);
                    }
                }
            }

            if (toolName.Equals("TodoWrite", StringComparison.OrdinalIgnoreCase) && input.TryGetProperty("todos", out var todos) && todos.ValueKind == JsonValueKind.Array)
            {
                return $"{todos.GetArrayLength()} todos";
            }

            foreach (var property in input.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: > 0 } s)
                {
                    return Shorten(s, maxLength);
                }
            }
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }

    /// <summary>Indented JSON for the expanded view; returns the input unchanged if it is not valid JSON.</summary>
    public static string Pretty(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, PrettyOptions);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>True for tools that start a subagent.</summary>
    public static bool IsAgentTool(string toolName) =>
        toolName.Equals("Task", StringComparison.OrdinalIgnoreCase) || toolName.Equals("Agent", StringComparison.OrdinalIgnoreCase);

    public static string? AgentType(string inputJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            return doc.RootElement.TryGetProperty("subagent_type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Shorten(string text, int maxLength)
    {
        var line = string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return line.Length <= maxLength ? line : line[..(maxLength - 1)] + "…";
    }

    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
