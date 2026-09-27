using System.Runtime.InteropServices;

namespace CCUI.Terminal.Pty;

/// <summary>Which ConPTY implementation to use.</summary>
public enum PseudoConsoleHost
{
    /// <summary>The bundled conpty.dll + OpenConsole.exe when present, otherwise the one built into Windows.</summary>
    Auto,

    /// <summary>The bundled conpty.dll (Microsoft.Windows.Console.ConPTY); fails if it is missing.</summary>
    Bundled,

    /// <summary>The pseudo console built into Windows (kernel32).</summary>
    Inbox,
}

/// <summary>The three pseudo-console entry points, resolved from either the bundled conpty.dll or kernel32.</summary>
internal sealed unsafe class PseudoConsoleApi
{
    /// <summary>Measure cells by grapheme cluster, matching how Claude Code lays out its UI. Only the bundled ConPTY knows it.</summary>
    private const uint GlyphWidthGraphemes = 0x08;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<PseudoConsoleHost, PseudoConsoleApi> Cache = [];

    private PseudoConsoleApi(IntPtr library, string prefix, bool bundled, string description)
    {
        _create = (delegate* unmanaged[Stdcall]<NativeMethods.Coord, IntPtr, IntPtr, uint, IntPtr*, int>)NativeLibrary.GetExport(library, prefix + "CreatePseudoConsole");
        _resize = (delegate* unmanaged[Stdcall]<IntPtr, NativeMethods.Coord, int>)NativeLibrary.GetExport(library, prefix + "ResizePseudoConsole");
        _close = (delegate* unmanaged[Stdcall]<IntPtr, void>)NativeLibrary.GetExport(library, prefix + "ClosePseudoConsole");
        CreateFlags = bundled ? GlyphWidthGraphemes : 0;
        Description = description;
    }

    private readonly delegate* unmanaged[Stdcall]<NativeMethods.Coord, IntPtr, IntPtr, uint, IntPtr*, int> _create;
    private readonly delegate* unmanaged[Stdcall]<IntPtr, NativeMethods.Coord, int> _resize;
    private readonly delegate* unmanaged[Stdcall]<IntPtr, void> _close;

    public uint CreateFlags { get; }

    public string Description { get; }

    /// <summary>Creates a pseudo console; returns the HRESULT.</summary>
    public int CreatePseudoConsole(int columns, int rows, IntPtr input, IntPtr output, out IntPtr pseudoConsole)
    {
        IntPtr handle;
        var hr = _create(new NativeMethods.Coord(columns, rows), input, output, CreateFlags, &handle);
        pseudoConsole = handle;
        return hr;
    }

    public void ResizePseudoConsole(IntPtr pseudoConsole, int columns, int rows) => _resize(pseudoConsole, new NativeMethods.Coord(columns, rows));

    public void ClosePseudoConsole(IntPtr pseudoConsole) => _close(pseudoConsole);

    public static PseudoConsoleApi Load(PseudoConsoleHost host)
    {
        lock (Gate)
        {
            if (!Cache.TryGetValue(host, out var api))
            {
                api = LoadCore(host);
                Cache[host] = api;
            }

            return api;
        }
    }

    private static PseudoConsoleApi LoadCore(PseudoConsoleHost host)
    {
        if (host != PseudoConsoleHost.Inbox)
        {
            foreach (var path in BundledCandidates())
            {
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var library))
                {
                    return new PseudoConsoleApi(library, "Conpty", bundled: true, $"bundled ConPTY ({path})");
                }
            }

            if (host == PseudoConsoleHost.Bundled)
            {
                throw new FileNotFoundException("The bundled conpty.dll was not found next to the application.");
            }
        }

        return new PseudoConsoleApi(NativeLibrary.Load("kernel32.dll"), string.Empty, bundled: false, "Windows built-in ConPTY");
    }

    private static IEnumerable<string> BundledCandidates()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => null,
        };

        // conpty.dll looks for OpenConsole.exe next to itself or in an architecture subfolder of its own folder.
        if (arch is not null)
        {
            yield return Path.Combine(AppContext.BaseDirectory, arch, "conpty.dll");
        }

        yield return Path.Combine(AppContext.BaseDirectory, "conpty.dll");
    }
}
