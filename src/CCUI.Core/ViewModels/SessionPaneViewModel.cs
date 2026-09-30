using System.Collections.ObjectModel;
using CCUI.Core.Claude;
using CCUI.Core.Metering;
using CCUI.Core.Sessions;
using CCUI.Core.Threading;
using CCUI.Terminal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCUI.Core.ViewModels;

/// <summary>Settings a pane needs from the app.</summary>
public sealed record PaneSettings(long ContextWindowTokens, TimeSpan MeterHalfLife, TimeSpan MeterPeakHold);

/// <summary>
/// One session pane: the terminal, the header stats and meters, and the detail view. Events from the session
/// (background threads) are applied on the UI thread through <see cref="IUiDispatcher"/>.
/// </summary>
public sealed partial class SessionPaneViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly char[] TitleStatusGlyphs = ['✳', '✻', '✽', '✶', '✢', '·', '⠂', '⠐', '*', ' '];

    /// <summary>The meter floor while Claude is working: a few segments, well under a real burst.</summary>
    private const double WorkingLevel = 0.2;

    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly ClaudeSessionRuntime _runtime;
    private SessionTimeline _timeline = new();
    private bool _disposed;

    public SessionPaneViewModel(string paneId, string workingDirectory, ClaudeSessionRuntime runtime, PaneSettings settings, IUiDispatcher dispatcher, TimeProvider time, string? title = null)
    {
        PaneId = paneId;
        WorkingDirectory = workingDirectory;
        SessionId = runtime.SessionId;
        _runtime = runtime;
        _dispatcher = dispatcher;
        _time = time;
        Title = title ?? PathNames.LastSegment(workingDirectory);
        Stats = new SessionStatsViewModel(settings.ContextWindowTokens);
        SentLevel = new DecayingLevel(time, settings.MeterHalfLife, settings.MeterPeakHold);
        ReceivedLevel = new DecayingLevel(time, settings.MeterHalfLife, settings.MeterPeakHold);
    }

    /// <summary>Asks the shell to close this pane.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Asks the shell to start the session again (after it exited).</summary>
    public event EventHandler? RestartRequested;

    /// <summary>The session wrote to the clipboard (OSC 52).</summary>
    public event EventHandler<string>? ClipboardWriteRequested;

    public string PaneId { get; }

    /// <summary>Identifies the pane in the saved docking layout.</summary>
    public string ContentId => "session:" + PaneId;

    public string WorkingDirectory { get; }

    public string Project => PathNames.LastSegment(WorkingDirectory);

    public TerminalSession Terminal => _runtime.Terminal;

    public SessionStatsViewModel Stats { get; }

    public SessionDetailViewModel Detail { get; } = new();

    /// <summary>Activity towards the model (prompts, tool results).</summary>
    public DecayingLevel SentLevel { get; }

    /// <summary>Activity from the model (text, thinking, tool calls).</summary>
    public DecayingLevel ReceivedLevel { get; }

    public ObservableCollection<AgentViewModel> Agents { get; } = [];

    [ObservableProperty]
    public partial string SessionId { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string? GitBranch { get; set; }

    [ObservableProperty]
    public partial SessionActivity Activity { get; set; }

    /// <summary>Claude wants something (a permission, an answer) while the pane is not focused.</summary>
    [ObservableProperty]
    public partial bool NeedsAttention { get; set; }

    [ObservableProperty]
    public partial string? AttentionMessage { get; set; }

    [ObservableProperty]
    public partial bool HasExited { get; set; }

    [ObservableProperty]
    public partial int? ExitCode { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial bool IsDetailOpen { get; set; }

    [ObservableProperty]
    public partial double DetailHeight { get; set; } = 240;

    [ObservableProperty]
    public partial TerminalProgress Progress { get; set; }

    public void Start()
    {
        _runtime.Feed.EventsArrived += OnEventsArrived;
        _runtime.Feed.HookReceived += OnHookReceived;
        _runtime.Feed.SessionSwitched += OnSessionSwitched;
        var emulator = _runtime.Terminal.Emulator;
        emulator.TitleChanged += OnTitleChanged;
        emulator.ProgressChanged += OnProgressChanged;
        emulator.Bell += OnBell;
        emulator.NotificationRequested += OnNotification;
        emulator.ClipboardWriteRequested += OnClipboardWrite;
        _runtime.Terminal.Exited += OnExited;
        _runtime.Start();
    }

    public void Tick(DateTimeOffset now) => Stats.Tick(now);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime.Feed.EventsArrived -= OnEventsArrived;
        _runtime.Feed.HookReceived -= OnHookReceived;
        _runtime.Feed.SessionSwitched -= OnSessionSwitched;
        await _runtime.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Removes a leading status glyph (Claude Code puts a spinner or "✳" before the title).</summary>
    public static string CleanTitle(string title) => title.TrimStart(TitleStatusGlyphs).Trim();

    // Keeps the "from Claude" meter lit while Claude works, between transcript writes.
    partial void OnActivityChanged(SessionActivity value) => ReceivedLevel.SetFloor(value == SessionActivity.Working ? WorkingLevel : 0);

    partial void OnIsActiveChanged(bool value)
    {
        if (value)
        {
            NeedsAttention = false;
        }
    }

    /// <summary>The view should put the keyboard in this pane's terminal.</summary>
    public event EventHandler? FocusRequested;

    public void RequestFocus() => FocusRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleDetail() => IsDetailOpen = !IsDetailOpen;

    /// <summary>Opens the detail view on the failed tool calls.</summary>
    [RelayCommand]
    private void ShowFailedTools()
    {
        Detail.ShowFailed();
        IsDetailOpen = true;
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Restart() => RestartRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ClearAttention() => NeedsAttention = false;

    private void OnEventsArrived(object? sender, IReadOnlyList<TranscriptEvent> events) => _dispatcher.Post(() => Apply(events));

    private void Apply(IReadOnlyList<TranscriptEvent> events)
    {
        if (_disposed)
        {
            return;
        }

        var changes = _timeline.Apply(events);
        Detail.Apply(changes);
        foreach (var pulse in changes.Pulses)
        {
            (pulse.Direction == ActivityDirection.Sent ? SentLevel : ReceivedLevel).Hit(LevelScale.FromSize(pulse.Size));
        }

        var stats = _timeline.Statistics;
        Stats.Update(stats, _time.GetUtcNow());
        if (!HasExited)
        {
            Activity = stats.Activity;
        }

        GitBranch = _timeline.GitBranch ?? GitBranch;
        if (_timeline.Title is { } title)
        {
            Title = title;
        }

        if (events.Any(e => e is UserPromptEvent { AgentId: null }))
        {
            NeedsAttention = false;
            AttentionMessage = null;
        }

        SyncAgents(stats.RunningAgents);
    }

    private void SyncAgents(IReadOnlyList<RunningAgent> running)
    {
        var ids = running.Select(a => a.ToolUseId).ToHashSet();
        for (var i = Agents.Count - 1; i >= 0; i--)
        {
            if (!ids.Contains(Agents[i].Id))
            {
                Agents.RemoveAt(i);
            }
        }

        foreach (var agent in running.Where(a => Agents.All(existing => existing.Id != a.ToolUseId)))
        {
            Agents.Add(new AgentViewModel(agent));
        }
    }

    private void OnHookReceived(object? sender, HookEvent hook) => _dispatcher.Post(() =>
    {
        switch (hook.EventName)
        {
            case "Notification":
                AttentionMessage = hook.Message;
                NeedsAttention = !IsActive;
                break;
            case "Stop" when !HasExited:
                Activity = SessionActivity.WaitingForUser;
                break;
        }
    });

    private void OnSessionSwitched(object? sender, string sessionId) => _dispatcher.Post(() =>
    {
        // /clear started a new conversation in the same pane.
        SessionId = sessionId;
        _timeline = new SessionTimeline();
        Detail.Clear();
        Agents.Clear();
        Stats.Update(_timeline.Statistics, _time.GetUtcNow());
    });

    private void OnTitleChanged(object? sender, EventArgs e)
    {
        var title = CleanTitle(_runtime.Terminal.Emulator.Title);
        if (title.Length > 0)
        {
            _dispatcher.Post(() => Title = title);
        }
    }

    private void OnProgressChanged(object? sender, EventArgs e)
    {
        var progress = _runtime.Terminal.Emulator.Progress;
        _dispatcher.Post(() => Progress = progress);
    }

    private void OnBell(object? sender, EventArgs e) => _dispatcher.Post(() => NeedsAttention |= !IsActive);

    private void OnNotification(object? sender, string message) => _dispatcher.Post(() =>
    {
        AttentionMessage = message;
        NeedsAttention = !IsActive;
    });

    private void OnClipboardWrite(object? sender, string text) => _dispatcher.Post(() => ClipboardWriteRequested?.Invoke(this, text));

    private void OnExited(object? sender, int? exitCode) => _dispatcher.Post(() =>
    {
        HasExited = true;
        ExitCode = exitCode;
        Activity = SessionActivity.Idle;
    });
}
