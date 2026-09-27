namespace CCUI.Core.Claude;

/// <summary>
/// Spots sessions that a reboot killed, as the reboot-sessions skill does: their transcripts were last written in
/// the hours before the boot and not since, and the biggest group of them shares one last-write second (the moment
/// the machine went down mid-write).
/// </summary>
public static class RebootDetector
{
    public static IReadOnlySet<string> FindKilled(IEnumerable<SessionSummary> sessions, DateTimeOffset bootTime, TimeSpan lookBack)
    {
        var candidates = sessions
            .Where(s => s.LastActive <= bootTime && s.LastActive >= bootTime - lookBack)
            .ToList();

        var group = candidates
            .GroupBy(s => s.LastActive.UtcTicks / TimeSpan.TicksPerSecond)
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .FirstOrDefault();

        return group is null
            ? new HashSet<string>()
            : group.Select(s => s.SessionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>When this machine last booted.</summary>
    public static DateTimeOffset LastBoot(TimeProvider time) =>
        time.GetUtcNow() - TimeSpan.FromMilliseconds(Environment.TickCount64);
}
