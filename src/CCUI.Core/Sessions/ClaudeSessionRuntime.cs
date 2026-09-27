using CCUI.Terminal;

namespace CCUI.Core.Sessions;

/// <summary>A running session: its terminal (the process) and its transcript feed.</summary>
public sealed class ClaudeSessionRuntime(string sessionId, TerminalSession terminal, ITranscriptFeed feed) : IAsyncDisposable
{
    public string SessionId { get; } = sessionId;

    public TerminalSession Terminal { get; } = terminal;

    public ITranscriptFeed Feed { get; } = feed;

    public void Start()
    {
        Feed.Start();
        try
        {
            Terminal.Start();
        }
        catch (Exception ex)
        {
            // Show launch failures where the user is looking: in the pane itself.
            Terminal.Emulator.Feed($"\e[31mCould not start the session: {ex.Message}\e[0m\r\n");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Feed.Dispose();
        await Terminal.DisposeAsync().ConfigureAwait(false);
    }
}

public interface ISessionLauncher
{
    ClaudeSessionRuntime Launch(SessionLaunchRequest request);
}
