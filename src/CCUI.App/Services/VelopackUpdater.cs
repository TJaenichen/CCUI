using System.Reflection;
using CCUI.Core.Settings;
using CCUI.Core.Updates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Velopack;
using Velopack.Sources;

namespace CCUI.App.Services;

/// <summary>
/// Updates through Velopack: releases on GitHub (or any folder or URL in <see cref="CCUI.Core.Settings.UpdateOptions.Source"/>),
/// delta packages, and a swap of the install folder when the app exits. Nothing happens when the app was not
/// installed by Velopack's Setup.exe, which is the case when it runs from Visual Studio.
/// </summary>
public sealed class VelopackUpdater : IAppUpdater
{
    private readonly UpdateManager _manager;
    private readonly ILogger<VelopackUpdater> _logger;
    private UpdateInfo? _found;

    public VelopackUpdater(IOptions<CCUI.Core.Settings.UpdateOptions> options, ILogger<VelopackUpdater> logger)
    {
        _logger = logger;
        var source = options.Value.Source.Trim();
        _manager = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            ? new UpdateManager(new GithubSource(source, accessToken: null, prerelease: options.Value.Prerelease))
            : new UpdateManager(source);
    }

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion => _manager.IsInstalled && _manager.CurrentVersion is { } installed
        ? installed.ToString()
        : AssemblyVersion;

    /// <summary>The version compiled into the assembly, without any build metadata.</summary>
    public static string AssemblyVersion
    {
        get
        {
            var informational = typeof(VelopackUpdater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = informational ?? typeof(VelopackUpdater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            var plus = version.IndexOf('+');
            return plus < 0 ? version : version[..plus];
        }
    }

    public async Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        if (!_manager.IsInstalled)
        {
            return null;
        }

        _found = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_found is null)
        {
            _logger.LogDebug("CCUI {Version} is up to date", CurrentVersion);
            return null;
        }

        var version = _found.TargetFullRelease.Version.ToString();
        _logger.LogInformation("CCUI {Version} is available (running {Current})", version, CurrentVersion);
        return version;
    }

    public Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken) =>
        _found is { } found
            ? _manager.DownloadUpdatesAsync(found, progress is null ? null : progress.Report, cancellationToken)
            : Task.CompletedTask;

    public void RestartToApply()
    {
        if (_found is { } found)
        {
            // Applies once this process has exited, so the window can close normally and save the workspace first.
            _manager.WaitExitThenApplyUpdates(found.TargetFullRelease, silent: false, restart: true);
        }
    }
}
