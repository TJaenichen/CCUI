using System.Management;
using System.Text.RegularExpressions;
using CCUI.Core.ViewModels;

namespace CCUI.App.Services;

/// <summary>Reads claude command lines through WMI (as the reboot-sessions script does), cached briefly.</summary>
public sealed partial class WmiClaudeProcessProbe(TimeProvider time) : IClaudeProcessProbe
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);
    private readonly Lock _gate = new();
    private IReadOnlySet<string> _cached = new HashSet<string>();
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public IReadOnlySet<string> RunningSessionIds()
    {
        lock (_gate)
        {
            if (time.GetUtcNow() - _cachedAt < CacheFor)
            {
                return _cached;
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name LIKE 'claude%' OR Name = 'node.exe' OR Name = 'bun.exe'");
                foreach (var process in searcher.Get())
                {
                    using (process)
                    {
                        if (process["CommandLine"] is string commandLine)
                        {
                            foreach (Match match in SessionArgument().Matches(commandLine))
                            {
                                ids.Add(match.Groups[1].Value);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                // WMI unavailable (e.g. the service is restarting): treat as "nothing running elsewhere".
            }

            _cached = ids;
            _cachedAt = time.GetUtcNow();
            return ids;
        }
    }

    [GeneratedRegex(@"--(?:resume|session-id)[ =]""?([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})")]
    private static partial Regex SessionArgument();
}
