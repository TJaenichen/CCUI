namespace CCUI.Core.ViewModels;

/// <summary>Dialogs the view models need; implemented by the view.</summary>
public interface IDialogService
{
    string? PickFolder(string title, string? initialDirectory);

    /// <summary>Picks one file; <paramref name="filter"/> is in Windows' "Images|*.png;*.jpg|All files|*.*" form.</summary>
    string? PickFile(string title, string filter, string? initialPath);

    bool Confirm(string title, string message);

    void ShowError(string title, string message);
}

public interface IClipboardService
{
    void SetText(string text);
}

/// <summary>Finds claude processes started outside this app, to avoid resuming a session twice.</summary>
public interface IClaudeProcessProbe
{
    /// <summary>Session ids that appear on running claude command lines (after --resume or --session-id).</summary>
    IReadOnlySet<string> RunningSessionIds();
}

public sealed class NoClaudeProcesses : IClaudeProcessProbe
{
    public IReadOnlySet<string> RunningSessionIds() => new HashSet<string>();
}

/// <summary>What the settings page shows about plugins; implemented by the app's plugin loader.</summary>
public interface IPluginCatalog
{
    /// <summary>The folder plugins are loaded from.</summary>
    string DirectoryPath { get; }

    IReadOnlyList<PluginInfo> Plugins { get; }

    void OpenDirectory();
}

/// <summary>A plugin that was found: loaded, or failed with <see cref="Error"/>.</summary>
public sealed record PluginInfo(string Name, string Version, string Path, string? Error)
{
    public bool Failed => Error is not null;

    public string Status => Error ?? (Version.Length > 0 ? "Loaded, version " + Version : "Loaded");
}

public sealed class NoPlugins : IPluginCatalog
{
    public string DirectoryPath => string.Empty;

    public IReadOnlyList<PluginInfo> Plugins => [];

    public void OpenDirectory()
    {
    }
}
