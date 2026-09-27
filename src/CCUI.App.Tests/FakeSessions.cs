using System.Text;
using CCUI.Core.Claude;
using CCUI.Core.Sessions;
using CCUI.Terminal;

namespace CCUI.App.Tests;

/// <summary>A connection that prints fixed output when started and stays open.</summary>
internal sealed class ScriptedConnection(string output) : ITerminalConnection
{
    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;

    public event EventHandler<int?>? Exited
    {
        add { }
        remove { }
    }

    public void Start(int columns, int rows) => DataReceived?.Invoke(this, Encoding.UTF8.GetBytes(output));

    public void Write(ReadOnlySpan<byte> data)
    {
    }

    public void Resize(int columns, int rows)
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ManualFeed : ITranscriptFeed
{
    public event EventHandler<IReadOnlyList<TranscriptEvent>>? EventsArrived;

    public event EventHandler<HookEvent>? HookReceived
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? SessionSwitched
    {
        add { }
        remove { }
    }

    public void Start()
    {
    }

    public void Dispose()
    {
    }

    public void Push(IReadOnlyList<TranscriptEvent> events) => EventsArrived?.Invoke(this, events);
}
