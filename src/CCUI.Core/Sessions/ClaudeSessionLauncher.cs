using System.Collections;
using CCUI.Core.Claude;
using CCUI.Core.Settings;
using CCUI.Terminal;
using CCUI.Terminal.Pty;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCUI.Core.Sessions;

/// <summary>Starts real claude processes in pseudo consoles.</summary>
public sealed class ClaudeSessionLauncher(
    IOptions<ClaudeOptions> claudeOptions,
    IOptions<TerminalOptions> terminalOptions,
    ClaudePaths paths,
    HookEnvironment? hooks,
    TimeProvider time,
    ILogger<ClaudeSessionLauncher> logger) : ISessionLauncher
{
    public ClaudeSessionRuntime Launch(SessionLaunchRequest request)
    {
        var claude = claudeOptions.Value;
        var terminal = terminalOptions.Value;
        var plan = ClaudeLaunchPlanner.Plan(request, claude, BaseEnvironment(claude), claude.UseSessionHooks ? hooks : null, Guid.NewGuid);
        var connection = CreateConnection(request, claude, plan);
        var session = new TerminalSession(connection, terminal.DefaultColumns, terminal.DefaultRows, terminal.ScrollbackLines);
        var feed = new FileTranscriptFeed(
            paths,
            plan.SessionId,
            request.WorkingDirectory,
            claude.UseSessionHooks ? hooks?.EventFileFor(request.PaneId) : null,
            TimeSpan.FromMilliseconds(Math.Max(100, claude.TranscriptPollMilliseconds)),
            time);
        return new ClaudeSessionRuntime(plan.SessionId, session, feed);
    }

    private static IReadOnlyDictionary<string, string> BaseEnvironment(ClaudeOptions options)
    {
        if (options.FreshEnvironment && OperatingSystem.IsWindows())
        {
            return WindowsEnvironment.GetFreshUserEnvironment();
        }

        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            current[(string)entry.Key] = entry.Value as string ?? string.Empty;
        }

        return current;
    }

    private ITerminalConnection CreateConnection(SessionLaunchRequest request, ClaudeOptions options, ClaudeLaunchPlan plan)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new MessageConnection("Sessions need Windows (ConPTY). Use the demo mode elsewhere.");
        }

        if (!Directory.Exists(request.WorkingDirectory))
        {
            return new MessageConnection($"The folder '{request.WorkingDirectory}' no longer exists (a removed worktree?).");
        }

        var executable = ExecutableResolver.Resolve(options.Command, plan.Environment, File.Exists);
        if (executable is null)
        {
            return new MessageConnection($"'{options.Command}' was not found on PATH. Set Claude:Command in appsettings.json.");
        }

        var host = Enum.TryParse<PseudoConsoleHost>(options.PseudoConsole, ignoreCase: true, out var h) ? h : PseudoConsoleHost.Auto;
        var connection = new ConPtyConnection(new PtyStartInfo(executable, plan.Arguments, request.WorkingDirectory, plan.Environment), host);
        logger.LogInformation("Starting {Executable} {Arguments} in {Directory} with {Host}", executable, string.Join(' ', plan.Arguments), request.WorkingDirectory, connection.HostDescription);
        return connection;
    }
}
