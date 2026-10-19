namespace CCUI.Core.Updates;

/// <summary>
/// Checks for, fetches and applies new versions of CCUI. The app implements it with Velopack; Core only knows
/// that an update can be found, downloaded, and applied when the process exits.
/// </summary>
public interface IAppUpdater
{
    /// <summary>False when this copy was not installed by the updater (run from the IDE, or unpacked), so updates are off.</summary>
    bool IsInstalled { get; }

    /// <summary>The running version, as the installer knows it or as the assembly reports it.</summary>
    string CurrentVersion { get; }

    /// <summary>Looks for a newer version; the version string, or null when up to date.</summary>
    Task<string?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the version the last check found, so applying it is instant.</summary>
    Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken);

    /// <summary>Arranges for the downloaded version to be applied once the process exits, and CCUI started again.</summary>
    void RestartToApply();
}

/// <summary>No updates (demo mode, or a build without an installer).</summary>
public sealed class NoAppUpdater(string version) : IAppUpdater
{
    public bool IsInstalled => false;

    public string CurrentVersion => version;

    public Task<string?> CheckAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    public Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

    public void RestartToApply()
    {
    }
}
