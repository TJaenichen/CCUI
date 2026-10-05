using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CCUI.Core.Sessions;

/// <summary>
/// The hook plumbing: a settings file passed to claude with --settings that runs "CCUI.exe --hook" on
/// SessionStart, UserPromptSubmit, Notification and Stop. The hook process appends what Claude sent to a per-pane
/// file, which the pane's feed tails. That is how a pane learns its new session id after /clear, that a prompt was
/// sent (before the transcript shows it), and when Claude waits for input.
/// </summary>
public sealed class HookEnvironment
{
    public const string HookArgument = "--hook";

    private static readonly string[] Events = ["SessionStart", "UserPromptSubmit", "Notification", "Stop"];

    public HookEnvironment(string directory, string executablePath)
    {
        Directory = directory;
        SettingsFilePath = Path.Combine(directory, "claude-hooks.json");
        System.IO.Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsFilePath, BuildSettingsJson(executablePath));
    }

    public string Directory { get; }

    public string SettingsFilePath { get; }

    public string EventFileFor(string paneId) => Path.Combine(Directory, paneId + ".jsonl");

    /// <summary>
    /// The settings JSON. Forward slashes keep the command valid whether Claude runs hooks through Git Bash or cmd.
    /// </summary>
    public static string BuildSettingsJson(string executablePath)
    {
        var command = $"\"{executablePath.Replace('\\', '/')}\" {HookArgument}";
        var hooks = new JsonObject();
        foreach (var name in Events)
        {
            hooks[name] = new JsonArray(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = 10 }),
            });
        }

        return new JsonObject { ["hooks"] = hooks }.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    /// <summary>
    /// Runs in the short-lived hook process: appends Claude's hook payload to the pane's event file. Prints nothing
    /// (SessionStart output would be added to Claude's context) and never fails the hook.
    /// </summary>
    public static int Relay(TextReader input, Func<string, string?> getEnvironmentVariable, TimeProvider time)
    {
        var paneId = getEnvironmentVariable(ClaudeLaunchPlanner.PaneIdVariable);
        var directory = getEnvironmentVariable(ClaudeLaunchPlanner.HookDirectoryVariable);
        if (string.IsNullOrWhiteSpace(paneId) || string.IsNullOrWhiteSpace(directory) || paneId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return 0;
        }

        try
        {
            var payload = JsonNode.Parse(input.ReadToEnd());
            var line = new JsonObject
            {
                ["receivedAt"] = time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
                ["payload"] = payload,
            }.ToJsonString();

            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.AppendAllText(Path.Combine(directory, paneId + ".jsonl"), line + "\n");
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(20);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A lost hook event only costs a status update; never block or fail Claude over it.
        }

        return 0;
    }
}

/// <summary>A hook callback as recorded by <see cref="HookEnvironment.Relay"/>.</summary>
/// <param name="Message">The notification text (Notification) or the submitted prompt (UserPromptSubmit).</param>
public sealed record HookEvent(string EventName, string? SessionId, string? TranscriptPath, string? Source, string? Message, DateTimeOffset ReceivedAt)
{
    public static HookEvent? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? Get(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var received = Get(root, "receivedAt") is { } r && DateTimeOffset.TryParse(r, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : DateTimeOffset.MinValue;
            return Get(payload, "hook_event_name") is { } name
                ? new HookEvent(name, Get(payload, "session_id"), Get(payload, "transcript_path"), Get(payload, "source"), Get(payload, "message") ?? Get(payload, "prompt"), received)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
