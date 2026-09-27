using System.Collections.ObjectModel;
using CCUI.Core.Claude;
using CCUI.Core.Sessions;
using CCUI.Core.Settings;
using CCUI.Core.Threading;
using CCUI.Core.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCUI.Core.ViewModels;

/// <summary>Services the shell needs, grouped to keep the constructor readable.</summary>
public sealed record ShellServices(
    ISessionLauncher Launcher,
    ISessionCatalog Catalog,
    IWorkspaceStore Workspace,
    IUiDispatcher Dispatcher,
    IDialogService Dialogs,
    IClipboardService Clipboard,
    IClaudeProcessProbe Processes,
    TimeProvider Time,
    ILogger<ShellViewModel> Logger);

/// <summary>The main window: the session list, the open panes, and saving/restoring them.</summary>
public sealed partial class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ShellServices _services;
    private readonly ClaudeOptions _claude;
    private readonly SessionListOptions _list;
    private readonly WorkspaceOptions _workspace;
    private readonly AppearanceOptions _appearance;
    private ITimer? _clock;
    private ITimer? _refresh;
    private ITimer? _autosave;
    private int _refreshing;
    private IReadOnlySet<string> _runningElsewhere = new HashSet<string>();

    public ShellViewModel(
        ShellServices services,
        IOptions<ClaudeOptions> claude,
        IOptions<SessionListOptions> list,
        IOptions<WorkspaceOptions> workspace,
        IOptions<AppearanceOptions> appearance,
        IOptions<DemoOptions> demo)
    {
        _services = services;
        _claude = claude.Value;
        _list = list.Value;
        _workspace = workspace.Value;
        _appearance = appearance.Value;
        IsDemo = demo.Value.Enabled;
        SessionList.OpenRequested += (_, item) => OpenSession(item);
        Panes.CollectionChanged += (_, _) => UpdateStatus();
    }

    public SessionListViewModel SessionList { get; } = new();

    public ObservableCollection<SessionPaneViewModel> Panes { get; } = [];

    public bool IsDemo { get; }

    /// <summary>Provides the current docking layout when the workspace is saved; set by the view.</summary>
    public Func<string?>? LayoutProvider { get; set; }

    /// <summary>Provides the window position when the workspace is saved; set by the view.</summary>
    public Func<WindowPlacement?>? WindowProvider { get; set; }

    [ObservableProperty]
    public partial SessionPaneViewModel? ActivePane { get; set; }

    [ObservableProperty]
    public partial bool IsSessionListVisible { get; set; } = true;

    [ObservableProperty]
    public partial double SessionListWidth { get; set; } = 320;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Loads the saved workspace (if restoring is on), reopens its sessions and starts the timers.</summary>
    public async Task StartAsync(WorkspaceState? saved)
    {
        _clock = _services.Time.CreateTimer(_ => _services.Dispatcher.Post(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _refresh = _services.Time.CreateTimer(_ => RefreshSessionList(), null, TimeSpan.Zero, TimeSpan.FromSeconds(Math.Max(1, _list.RefreshSeconds)));
        if (_workspace.AutoSaveSeconds > 0)
        {
            var period = TimeSpan.FromSeconds(_workspace.AutoSaveSeconds);
            _autosave = _services.Time.CreateTimer(_ => _services.Dispatcher.Post(SaveWorkspace), null, period, period);
        }

        if (saved is null)
        {
            return;
        }

        IsSessionListVisible = saved.SessionListVisible;
        SessionListWidth = saved.SessionListWidth;
        var runningElsewhere = _services.Processes.RunningSessionIds();
        var first = true;
        foreach (var pane in saved.Panes)
        {
            if (runningElsewhere.Contains(pane.SessionId))
            {
                _services.Logger.LogInformation("Not restoring {Session}: it is already running in another window", pane.SessionId);
                continue;
            }

            if (!first && _workspace.LaunchStaggerMilliseconds > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_workspace.LaunchStaggerMilliseconds), _services.Time).ConfigureAwait(true);
            }

            first = false;
            var vm = Launch(new SessionLaunchRequest(pane.PaneId, pane.WorkingDirectory, pane.SessionId), pane.Title);
            vm.IsDetailOpen = pane.DetailOpen;
            vm.DetailHeight = pane.DetailHeight > 0 ? pane.DetailHeight : vm.DetailHeight;
        }
    }

    public WorkspaceState CaptureWorkspace() => new()
    {
        SavedAt = _services.Time.GetUtcNow(),
        Panes = [.. Panes.Select(p => new WorkspacePane(p.PaneId, p.SessionId, p.WorkingDirectory, p.Title, p.IsDetailOpen, p.DetailHeight))],
        DockLayout = LayoutProvider?.Invoke(),
        Window = WindowProvider?.Invoke(),
        SessionListWidth = SessionListWidth,
        SessionListVisible = IsSessionListVisible,
    };

    public void SaveWorkspace()
    {
        if (IsDemo)
        {
            return;
        }

        try
        {
            _services.Workspace.Save(CaptureWorkspace());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Logger.LogWarning(ex, "Could not save the workspace");
        }
    }

    public SessionPaneViewModel? FindPaneBySession(string sessionId) =>
        Panes.FirstOrDefault(p => p.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase));

    public SessionPaneViewModel? FindPane(string paneId) => Panes.FirstOrDefault(p => p.PaneId == paneId);

    public async ValueTask DisposeAsync()
    {
        _clock?.Dispose();
        _refresh?.Dispose();
        _autosave?.Dispose();
        SaveWorkspace();
        foreach (var pane in Panes.ToList())
        {
            await pane.DisposeAsync().ConfigureAwait(false);
        }
    }

    partial void OnActivePaneChanged(SessionPaneViewModel? oldValue, SessionPaneViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        if (newValue is not null)
        {
            newValue.IsActive = true;
        }
    }

    [RelayCommand]
    private void NewSession()
    {
        var start = ActivePane?.WorkingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (_services.Dialogs.PickFolder("Start a Claude session in…", start) is { } folder)
        {
            ActivePane = Launch(new SessionLaunchRequest(NewPaneId(), folder, ResumeSessionId: null), title: null);
        }
    }

    [RelayCommand]
    private void OpenSession(SessionListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (FindPaneBySession(item.SessionId) is { } open)
        {
            ActivePane = open;
            return;
        }

        if (item.WorkingDirectory is null)
        {
            _services.Dialogs.ShowError("Cannot open session", "The session's folder is unknown.");
            return;
        }

        if (item.RunningElsewhere
            && !_services.Dialogs.Confirm("Session already running", $"'{item.Name}' is running in another window. Resuming it here as well makes two Claude processes write one transcript. Open anyway?"))
        {
            return;
        }

        ActivePane = Launch(new SessionLaunchRequest(NewPaneId(), item.WorkingDirectory, item.SessionId), item.Summary.Title);
    }

    [RelayCommand]
    private void ReopenKilledSessions()
    {
        foreach (var item in SessionList.KilledAtReboot().ToList())
        {
            OpenSession(item);
        }
    }

    [RelayCommand]
    private void ToggleDetail() => ActivePane?.ToggleDetailCommand.Execute(null);

    [RelayCommand]
    private void CloseActivePane()
    {
        if (ActivePane is { } pane)
        {
            ClosePaneCommand.Execute(pane);
        }
    }

    [RelayCommand]
    private void ToggleSessionList() => IsSessionListVisible = !IsSessionListVisible;

    [RelayCommand]
    private async Task ClosePaneAsync(SessionPaneViewModel? pane)
    {
        if (pane is null)
        {
            return;
        }

        if (pane.Activity == SessionActivity.Working && !pane.HasExited
            && !_services.Dialogs.Confirm("Close session", $"Claude is still working in '{pane.Title}'. Close it anyway? The session can be resumed later."))
        {
            return;
        }

        Panes.Remove(pane);
        if (ActivePane == pane)
        {
            ActivePane = Panes.LastOrDefault();
        }

        SessionList.RefreshPanes(FindPaneBySession);
        await pane.DisposeAsync().ConfigureAwait(true);
        SaveWorkspace();
    }

    private static string NewPaneId() => Guid.NewGuid().ToString("N");

    private SessionPaneViewModel Launch(SessionLaunchRequest request, string? title)
    {
        var runtime = _services.Launcher.Launch(request);
        var settings = new PaneSettings(
            _claude.ContextWindowTokens,
            TimeSpan.FromMilliseconds(_appearance.MeterDecayMilliseconds),
            TimeSpan.FromMilliseconds(_appearance.MeterPeakHoldMilliseconds));
        var pane = new SessionPaneViewModel(request.PaneId, request.WorkingDirectory, runtime, settings, _services.Dispatcher, _services.Time, title);
        pane.CloseRequested += (_, _) => ClosePaneCommand.Execute(pane);
        pane.RestartRequested += (_, _) => Restart(pane);
        pane.ClipboardWriteRequested += (_, text) => _services.Clipboard.SetText(text);
        pane.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionPaneViewModel.Activity) or nameof(SessionPaneViewModel.NeedsAttention) or nameof(SessionPaneViewModel.HasExited))
            {
                UpdateStatus();
            }
        };

        Panes.Add(pane);
        pane.Start();
        SessionList.RefreshPanes(FindPaneBySession);
        SaveWorkspace();
        return pane;
    }

    private void Restart(SessionPaneViewModel pane)
    {
        var index = Panes.IndexOf(pane);
        if (index < 0)
        {
            return;
        }

        var replacement = Launch(new SessionLaunchRequest(pane.PaneId, pane.WorkingDirectory, pane.SessionId), pane.Title);
        replacement.IsDetailOpen = pane.IsDetailOpen;
        replacement.DetailHeight = pane.DetailHeight;
        Panes.Remove(pane);
        Panes.Move(Panes.IndexOf(replacement), Math.Min(index, Panes.Count - 1));
        ActivePane = replacement;
        _ = pane.DisposeAsync();
    }

    private void RefreshSessionList()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        try
        {
            var summaries = _services.Catalog.Scan(TimeSpan.FromDays(Math.Max(1, _list.MaxAgeDays)));
            var killed = RebootDetector.FindKilled(summaries, RebootDetector.LastBoot(_services.Time), TimeSpan.FromHours(_list.RebootLookBackHours));
            var running = _services.Processes.RunningSessionIds();
            _services.Dispatcher.Post(() =>
            {
                var mine = Panes.Select(p => p.SessionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _runningElsewhere = running.Where(id => !mine.Contains(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                SessionList.Update(summaries, killed, _runningElsewhere, FindPaneBySession, _services.Time.GetUtcNow());
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Logger.LogWarning(ex, "Could not scan sessions");
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private void Tick()
    {
        var now = _services.Time.GetUtcNow();
        SessionList.Tick(now);
        foreach (var pane in Panes)
        {
            pane.Tick(now);
        }
    }

    private void UpdateStatus()
    {
        var working = Panes.Count(p => p.Activity == SessionActivity.Working && !p.HasExited);
        var waiting = Panes.Count(p => p.NeedsAttention);
        var parts = new List<string> { $"{Panes.Count} open" };
        if (working > 0)
        {
            parts.Add($"{working} working");
        }

        if (waiting > 0)
        {
            parts.Add($"{waiting} waiting for you");
        }

        StatusText = string.Join(" · ", parts);
    }
}
