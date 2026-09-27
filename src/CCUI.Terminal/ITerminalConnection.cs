namespace CCUI.Terminal;

/// <summary>The process side of a terminal: a pseudo console, a replay, or anything else that produces VT output.</summary>
public interface ITerminalConnection : IAsyncDisposable
{
    /// <summary>
    /// Output bytes, raised on a background thread. The buffer is only valid during the call.
    /// </summary>
    event EventHandler<ReadOnlyMemory<byte>>? DataReceived;

    /// <summary>Raised once when the process ends; the argument is its exit code when known.</summary>
    event EventHandler<int?>? Exited;

    void Start(int columns, int rows);

    void Write(ReadOnlySpan<byte> data);

    void Resize(int columns, int rows);
}
