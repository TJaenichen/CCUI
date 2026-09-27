using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace CCUI.Terminal.Pty;

/// <summary>
/// Runs a process inside a Windows pseudo console (ConPTY) and exposes its VT output and input.
/// Output is read on a dedicated thread; input is written on a background task so the UI never blocks on a full pipe.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ConPtyConnection : ITerminalConnection
{
    private const uint StillActive = 259;
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(3);

    private readonly PtyStartInfo _startInfo;
    private readonly PseudoConsoleApi _api;
    private readonly Channel<byte[]> _input = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Lock _gate = new();
    private IntPtr _pseudoConsole;
    private SafeFileHandle? _inputWrite;
    private SafeFileHandle? _outputRead;
    private SafeProcessHandle? _process;
    private Thread? _reader;
    private Task? _writer;
    private RegisteredWaitHandle? _exitWait;
    private int _exitRaised;
    private int _disposed;

    public ConPtyConnection(PtyStartInfo startInfo, PseudoConsoleHost host = PseudoConsoleHost.Auto)
    {
        _startInfo = startInfo;
        _api = PseudoConsoleApi.Load(host);
    }

    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;

    public event EventHandler<int?>? Exited;

    public int? ProcessId { get; private set; }

    /// <summary>Which ConPTY implementation is in use, for diagnostics.</summary>
    public string HostDescription => _api.Description;

    public void Start(int columns, int rows)
    {
        if (!NativeMethods.CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0)
            || !NativeMethods.CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreatePipe failed.");
        }

        using (inputRead)
        using (outputWrite)
        {
            var hr = _api.CreatePseudoConsole(columns, rows, inputRead.DangerousGetHandle(), outputWrite.DangerousGetHandle(), out var pseudoConsole);
            if (hr != 0)
            {
                inputWrite.Dispose();
                outputRead.Dispose();
                Marshal.ThrowExceptionForHR(hr);
            }

            _pseudoConsole = pseudoConsole;
            _inputWrite = inputWrite;
            _outputRead = outputRead;
            try
            {
                _process = StartProcess(pseudoConsole);
            }
            catch
            {
                ClosePseudoConsole();
                throw;
            }

            // The pseudo console holds its own duplicates of inputRead/outputWrite; ours close here so that the
            // output pipe breaks once the pseudo console goes away.
        }

        _reader = new Thread(ReadLoop) { IsBackground = true, Name = $"ConPTY reader {ProcessId}" };
        _reader.Start();
        _writer = Task.Run(WriteLoopAsync);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(new ProcessWaitHandle(_process), (_, _) => OnProcessExited(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (!data.IsEmpty)
        {
            _input.Writer.TryWrite(data.ToArray());
        }
    }

    public void Resize(int columns, int rows)
    {
        lock (_gate)
        {
            if (_pseudoConsole != IntPtr.Zero)
            {
                _api.ResizePseudoConsole(_pseudoConsole, columns, rows);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _input.Writer.TryComplete();

        // Closing the pseudo console sends CTRL_CLOSE_EVENT to the process; give it the drain timeout to exit.
        await Task.Run(ClosePseudoConsole).ConfigureAwait(false);
        await Task.Run(() => _reader?.Join(DrainTimeout)).ConfigureAwait(false);
        if (_process is { IsInvalid: false } process && NativeMethods.GetExitCodeProcess(process, out var code) && code == StillActive)
        {
            NativeMethods.TerminateProcess(process, 1);
        }

        if (_writer is not null)
        {
            await _writer.ConfigureAwait(false);
        }

        _exitWait?.Unregister(null);
        _inputWrite?.Dispose();
        _outputRead?.Dispose();
        _process?.Dispose();
    }

    private SafeProcessHandle StartProcess(IntPtr pseudoConsole)
    {
        var attributeListSize = IntPtr.Zero;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
        var attributeList = Marshal.AllocHGlobal(attributeListSize);
        var commandInterpreter = _startInfo.Environment.TryGetValue("ComSpec", out var comSpec) ? comSpec : "cmd.exe";
        var commandLine = Marshal.StringToHGlobalUni(CommandLine.ForProcess(_startInfo.FileName, _startInfo.Arguments, commandInterpreter));
        var environment = Marshal.StringToHGlobalUni(WindowsEnvironment.ToBlock(_startInfo.Environment));
        try
        {
            if (!NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize)
                || !NativeMethods.UpdateProcThreadAttribute(attributeList, 0, NativeMethods.ProcThreadAttributePseudoConsole, pseudoConsole, IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not attach the pseudo console to the process attributes.");
            }

            var startupInfo = new NativeMethods.StartupInfoEx
            {
                StartupInfo = new NativeMethods.StartupInfo
                {
                    Cb = Marshal.SizeOf<NativeMethods.StartupInfoEx>(),

                    // Null std handles force the child onto the pseudo console even if this process has redirected ones.
                    Flags = NativeMethods.StartfUseStdHandles,
                },
                AttributeList = attributeList,
            };

            if (!NativeMethods.CreateProcess(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles: false,
                    NativeMethods.ExtendedStartupInfoPresent | NativeMethods.CreateUnicodeEnvironment,
                    environment,
                    _startInfo.WorkingDirectory,
                    ref startupInfo,
                    out var processInfo))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not start '{_startInfo.FileName}' in '{_startInfo.WorkingDirectory}'.");
            }

            NativeMethods.CloseHandle(processInfo.Thread);
            ProcessId = processInfo.ProcessId;
            return new SafeProcessHandle(processInfo.Process, ownsHandle: true);
        }
        finally
        {
            NativeMethods.DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            Marshal.FreeHGlobal(commandLine);
            Marshal.FreeHGlobal(environment);
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[64 * 1024];
        using var stream = new FileStream(_outputRead!, FileAccess.Read, bufferSize: 1, isAsync: false);
        while (true)
        {
            int read;
            try
            {
                read = stream.Read(buffer, 0, buffer.Length);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                break;
            }

            if (read <= 0)
            {
                break;
            }

            DataReceived?.Invoke(this, buffer.AsMemory(0, read));
        }
    }

    private async Task WriteLoopAsync()
    {
        await using var stream = new FileStream(_inputWrite!, FileAccess.Write, bufferSize: 1, isAsync: false);
        try
        {
            await foreach (var chunk in _input.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                stream.Write(chunk);
                stream.Flush();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The process went away; pending input is moot.
        }
    }

    private void OnProcessExited()
    {
        int? exitCode = _process is { } process && NativeMethods.GetExitCodeProcess(process, out var code) ? unchecked((int)code) : null;

        // Closing the pseudo console flushes its last output and breaks the pipe; wait for the reader so the final
        // screen is complete before anyone reacts to the exit.
        ClosePseudoConsole();
        _reader?.Join(DrainTimeout);
        if (Interlocked.Exchange(ref _exitRaised, 1) == 0)
        {
            Exited?.Invoke(this, exitCode);
        }
    }

    private void ClosePseudoConsole()
    {
        IntPtr pseudoConsole;
        lock (_gate)
        {
            pseudoConsole = _pseudoConsole;
            _pseudoConsole = IntPtr.Zero;
        }

        if (pseudoConsole != IntPtr.Zero)
        {
            _api.ClosePseudoConsole(pseudoConsole);
        }
    }

    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeProcessHandle process) =>
            SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false);
    }
}
