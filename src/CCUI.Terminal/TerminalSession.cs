using System.Text;

namespace CCUI.Terminal;

/// <summary>
/// Wires a <see cref="TerminalEmulator"/> to an <see cref="ITerminalConnection"/>: output feeds the emulator, the
/// emulator's replies and user input go back to the process.
/// </summary>
public sealed class TerminalSession : IAsyncDisposable
{
    private int _started;
    private int _disposed;

    public TerminalSession(ITerminalConnection connection, int columns, int rows, int scrollbackLines)
    {
        Connection = connection;
        Emulator = new TerminalEmulator(columns, rows, scrollbackLines);
        Emulator.Reply += (_, reply) => SendText(reply);
        Connection.DataReceived += OnDataReceived;
        Connection.Exited += OnExited;
    }

    /// <summary>Raw output, after it was fed to the emulator (background thread). For recording and activity meters.</summary>
    public event EventHandler<ReadOnlyMemory<byte>>? OutputReceived;

    /// <summary>Bytes written to the process, e.g. keystrokes and pastes.</summary>
    public event EventHandler<int>? InputSent;

    public event EventHandler<int?>? Exited;

    public TerminalEmulator Emulator { get; }

    public ITerminalConnection Connection { get; }

    public bool HasExited { get; private set; }

    public int? ExitCode { get; private set; }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            Connection.Start(Emulator.Columns, Emulator.Rows);
        }
    }

    public void SendText(string text)
    {
        if (text.Length > 0)
        {
            SendBytes(Encoding.UTF8.GetBytes(text));
        }
    }

    public void SendBytes(ReadOnlySpan<byte> data)
    {
        if (HasExited || data.IsEmpty)
        {
            return;
        }

        Connection.Write(data);
        InputSent?.Invoke(this, data.Length);
    }

    public void Resize(int columns, int rows)
    {
        if (columns < 1 || rows < 1 || (columns == Emulator.Columns && rows == Emulator.Rows))
        {
            return;
        }

        Emulator.Resize(columns, rows);
        if (Volatile.Read(ref _started) == 1 && !HasExited)
        {
            Connection.Resize(columns, rows);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Connection.DataReceived -= OnDataReceived;
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnDataReceived(object? sender, ReadOnlyMemory<byte> data)
    {
        Emulator.Feed(data.Span);
        OutputReceived?.Invoke(this, data);
    }

    private void OnExited(object? sender, int? exitCode)
    {
        HasExited = true;
        ExitCode = exitCode;
        Exited?.Invoke(this, exitCode);
    }
}
