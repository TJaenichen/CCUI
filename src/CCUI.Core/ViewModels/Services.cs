namespace CCUI.Core.ViewModels;

/// <summary>Dialogs the view models need; implemented by the view.</summary>
public interface IDialogService
{
    string? PickFolder(string title, string? initialDirectory);

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
