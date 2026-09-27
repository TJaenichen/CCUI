using CCUI.Core.Settings;

namespace CCUI.Core.Sessions;

/// <summary>What to open: a new session in a folder, or an existing one by id.</summary>
/// <param name="PaneId">Identifies the pane across restarts (also used to route hook callbacks).</param>
public sealed record SessionLaunchRequest(string PaneId, string WorkingDirectory, string? ResumeSessionId);

/// <summary>The arguments and environment for one claude process, and the session id it will use.</summary>
public sealed record ClaudeLaunchPlan(string SessionId, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment);

/// <summary>Builds the claude command line and environment. Pure, so the rules are unit-tested.</summary>
public static class ClaudeLaunchPlanner
{
    public const string PaneIdVariable = "CCUI_PANE_ID";
    public const string HookDirectoryVariable = "CCUI_HOOK_DIR";

    public static ClaudeLaunchPlan Plan(
        SessionLaunchRequest request,
        ClaudeOptions options,
        IReadOnlyDictionary<string, string> baseEnvironment,
        HookEnvironment? hooks,
        Func<Guid> newGuid)
    {
        // Always pass the full id: names are not unique, and --resume <name> silently fails when two sessions share one.
        var sessionId = request.ResumeSessionId ?? newGuid().ToString();
        var arguments = new List<string>();
        arguments.AddRange(request.ResumeSessionId is null ? ["--session-id", sessionId] : ["--resume", sessionId]);
        if (hooks is not null)
        {
            arguments.AddRange(["--settings", hooks.SettingsFilePath]);
        }

        arguments.AddRange(options.Arguments);

        var environment = new Dictionary<string, string>(baseEnvironment, StringComparer.OrdinalIgnoreCase);
        foreach (var name in options.RemoveEnvironmentVariables)
        {
            environment.Remove(name);
        }

        foreach (var (name, value) in options.SetEnvironmentVariables)
        {
            environment[name] = value;
        }

        if (hooks is not null)
        {
            environment[PaneIdVariable] = request.PaneId;
            environment[HookDirectoryVariable] = hooks.Directory;
        }

        return new ClaudeLaunchPlan(sessionId, arguments, environment);
    }
}
