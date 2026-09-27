using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CCUI.Terminal.Pty;

[SupportedOSPlatform("windows")]
public static class WindowsEnvironment
{
    /// <summary>
    /// The environment a freshly logged-on process gets (machine + user registry variables, profile paths), rather
    /// than a copy of this process's environment. A Start-menu launch sees the same. This keeps variables that a
    /// parent shell or tool set (e.g. NO_COLOR from a Claude tool shell) from leaking into new sessions.
    /// </summary>
    public static Dictionary<string, string> GetFreshUserEnvironment()
    {
        if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), NativeMethods.TokenQuery | NativeMethods.TokenDuplicate, out var token))
        {
            throw new InvalidOperationException($"OpenProcessToken failed ({Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            if (!NativeMethods.CreateEnvironmentBlock(out var block, token, inherit: false))
            {
                throw new InvalidOperationException($"CreateEnvironmentBlock failed ({Marshal.GetLastPInvokeError()}).");
            }

            try
            {
                return ParseBlock(block);
            }
            finally
            {
                NativeMethods.DestroyEnvironmentBlock(block);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    /// <summary>Serialises an environment into a CreateProcess block: sorted "NAME=value\0" entries and a final "\0".</summary>
    public static string ToBlock(IReadOnlyDictionary<string, string> environment)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in environment.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(key).Append('=').Append(value).Append('\0');
        }

        return sb.Append('\0').ToString();
    }

    private static Dictionary<string, string> ParseBlock(IntPtr block)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cursor = block;
        while (Marshal.PtrToStringUni(cursor) is { Length: > 0 } entry)
        {
            // Entries like "=C:=C:\dir" hold per-drive directories; they are not real variables.
            var separator = entry.IndexOf('=', 1);
            if (separator > 0)
            {
                result[entry[..separator]] = entry[(separator + 1)..];
            }

            cursor += (entry.Length + 1) * sizeof(char);
        }

        return result;
    }
}
