using System.Text;
using CCUI.Terminal;

namespace CCUI.Core.Sessions;

/// <summary>A connection that only prints a message and ends, used when a session cannot be started.</summary>
public sealed class MessageConnection(string message) : ITerminalConnection
{
    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;

    public event EventHandler<int?>? Exited;

    public void Start(int columns, int rows)
    {
        DataReceived?.Invoke(this, Encoding.UTF8.GetBytes($"\e[31m{message.Replace("\n", "\r\n", StringComparison.Ordinal)}\e[0m\r\n"));
        Exited?.Invoke(this, null);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
    }

    public void Resize(int columns, int rows)
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
