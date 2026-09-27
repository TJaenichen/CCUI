using CCUI.Core.Claude;
using CCUI.Core.Sessions;
using CCUI.Core.Settings;
using CCUI.Terminal;
using Microsoft.Extensions.Options;

namespace CCUI.Core.Demo;

/// <summary>Lists the demo projects as sessions, with deterministic ids and staggered "last active" times.</summary>
public sealed class DemoSessionCatalog(IOptions<DemoOptions> options, TimeProvider time) : ISessionCatalog
{
    private readonly DateTimeOffset _started = time.GetUtcNow();

    public static string SessionIdFor(int index) => new Guid(index + 1, 0xCC, 0x01, [0x9a, 0x3a, 0x0d, 0xe5, 0x5e, 0x55, 0x10, 0x4e]).ToString();

    public IReadOnlyList<SessionSummary> Scan(TimeSpan maxAge)
    {
        var count = Math.Clamp(options.Value.Sessions, 1, DemoScript.Projects.Count);
        return [.. DemoScript.Projects.Take(count).Select((project, index) => new SessionSummary
        {
            SessionId = SessionIdFor(index),
            TranscriptPath = $"demo://{project.Name}",
            WorkingDirectory = project.WorkingDirectory,
            GitBranch = project.Branch,
            Title = project.Title,
            FirstPrompt = project.Turns[0].Prompt,
            LastPrompt = project.Turns[^1].Prompt,
            LastActive = _started - TimeSpan.FromMinutes(index * 17 + 3),
            Version = "2.1.283",
        })];
    }
}

/// <summary>Starts demo sessions instead of claude processes.</summary>
public sealed class DemoSessionLauncher(IOptions<DemoOptions> demo, IOptions<TerminalOptions> terminal, TimeProvider time) : ISessionLauncher
{
    public ClaudeSessionRuntime Launch(SessionLaunchRequest request)
    {
        var projects = DemoScript.Projects;
        var index = Enumerable.Range(0, projects.Count).FirstOrDefault(i => DemoSessionCatalog.SessionIdFor(i) == request.ResumeSessionId, -1);
        if (index < 0)
        {
            index = Enumerable.Range(0, projects.Count).FirstOrDefault(i => string.Equals(projects[i].WorkingDirectory, request.WorkingDirectory, StringComparison.OrdinalIgnoreCase), 0);
        }

        var sessionId = request.ResumeSessionId ?? Guid.NewGuid().ToString();
        var session = new DemoSession(projects[index], sessionId, time, demo.Value.Speed, demo.Value.Seed + index);
        var options = terminal.Value;
        var terminalSession = new TerminalSession(session.Connection, options.DefaultColumns, options.DefaultRows, options.ScrollbackLines);
        return new ClaudeSessionRuntime(sessionId, terminalSession, session.Feed);
    }
}
