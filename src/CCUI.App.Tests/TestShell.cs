using CCUI.Core.Claude;
using CCUI.Core.Sessions;
using CCUI.Core.Settings;
using CCUI.Core.Threading;
using CCUI.Core.ViewModels;
using CCUI.Core.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CCUI.App.Tests;

internal static class TestShell
{
    public static ShellViewModel Create() => new(
        new ShellServices(new NoLauncher(), new NoCatalog(), new NoStore(), new ImmediateDispatcher(), new NoDialogs(), new NoClipboard(), new NoClaudeProcesses(), TimeProvider.System, NullLogger<ShellViewModel>.Instance),
        Options.Create(new ClaudeOptions()),
        Options.Create(new SessionListOptions()),
        Options.Create(new WorkspaceOptions { AutoSaveSeconds = 0 }),
        Options.Create(new AppearanceOptions()),
        Options.Create(new DemoOptions()));

    private sealed class NoLauncher : ISessionLauncher
    {
        public ClaudeSessionRuntime Launch(SessionLaunchRequest request) => throw new NotSupportedException();
    }

    private sealed class NoCatalog : ISessionCatalog
    {
        public IReadOnlyList<SessionSummary> Scan(TimeSpan maxAge) => [];
    }

    private sealed class NoStore : IWorkspaceStore
    {
        public WorkspaceState? Load() => null;

        public void Save(WorkspaceState state)
        {
        }
    }

    private sealed class NoDialogs : IDialogService
    {
        public string? PickFolder(string title, string? initialDirectory) => null;

        public bool Confirm(string title, string message) => false;

        public void ShowError(string title, string message)
        {
        }
    }

    private sealed class NoClipboard : IClipboardService
    {
        public void SetText(string text)
        {
        }
    }
}
