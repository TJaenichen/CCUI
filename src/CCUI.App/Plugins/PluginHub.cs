using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CCUI.Core.Claude;
using CCUI.Core.ViewModels;
using CCUI.Plugins;
using Microsoft.Extensions.Logging;

namespace CCUI.App.Plugins;

/// <summary>
/// Turns the shell's panes into the contract's sessions and events, and holds the sidebar panels. One for the app;
/// every plugin sees it through its own <see cref="PluginHost"/>. Runs on the UI thread, like the panes.
/// </summary>
public sealed class PluginHub
{
    private readonly ShellViewModel _shell;
    private readonly ILogger<PluginHub> _logger;
    private readonly Dictionary<SessionPaneViewModel, PluginSession> _sessions = [];
    private readonly HashSet<(string PaneId, string ToolUseId)> _finishedTools = [];

    public PluginHub(ShellViewModel shell, ILogger<PluginHub> logger)
    {
        _shell = shell;
        _logger = logger;
        foreach (var pane in shell.Panes)
        {
            Attach(pane);
        }

        shell.Panes.CollectionChanged += OnPanesChanged;
    }

    public ObservableCollection<SidebarPanel> TopPanels { get; } = [];

    public ObservableCollection<SidebarPanel> BottomPanels { get; } = [];

    public IReadOnlyList<PluginSession> Sessions => [.. _sessions.Values];

    public event EventHandler<PluginSessionEventArgs>? SessionOpened;

    public event EventHandler<PluginSessionEventArgs>? SessionChanged;

    public event EventHandler<PluginSessionEventArgs>? SessionClosed;

    public event EventHandler<ClaudeEventArgs>? ClaudeEvent;

    public void AddPanel(SidebarPanel panel) => (panel.Slot == SidebarSlot.Top ? TopPanels : BottomPanels).Add(panel);

    private void OnPanesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var pane in _sessions.Keys.ToList())
            {
                Detach(pane);
            }

            foreach (var pane in _shell.Panes)
            {
                Attach(pane);
            }

            return;
        }

        foreach (var pane in e.OldItems?.OfType<SessionPaneViewModel>() ?? [])
        {
            Detach(pane);
        }

        foreach (var pane in e.NewItems?.OfType<SessionPaneViewModel>() ?? [])
        {
            Attach(pane);
        }
    }

    private void Attach(SessionPaneViewModel pane)
    {
        if (_sessions.ContainsKey(pane))
        {
            return;
        }

        var session = Snapshot(pane);
        _sessions[pane] = session;
        pane.PropertyChanged += OnPaneChanged;
        pane.TimelineChanged += OnTimelineChanged;
        Raise(SessionOpened, new PluginSessionEventArgs(session));
    }

    private void Detach(SessionPaneViewModel pane)
    {
        if (!_sessions.Remove(pane, out var session))
        {
            return;
        }

        pane.PropertyChanged -= OnPaneChanged;
        pane.TimelineChanged -= OnTimelineChanged;
        _finishedTools.RemoveWhere(t => t.PaneId == pane.PaneId);
        Raise(SessionClosed, new PluginSessionEventArgs(session));
    }

    private void OnPaneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SessionPaneViewModel pane || !_sessions.TryGetValue(pane, out var previous))
        {
            return;
        }

        if (e.PropertyName is not (nameof(SessionPaneViewModel.SessionId) or nameof(SessionPaneViewModel.Title)
            or nameof(SessionPaneViewModel.Activity) or nameof(SessionPaneViewModel.NeedsAttention) or nameof(SessionPaneViewModel.HasExited)))
        {
            return;
        }

        var session = Snapshot(pane);
        if (session == previous)
        {
            return;
        }

        _sessions[pane] = session;
        Raise(SessionChanged, new PluginSessionEventArgs(session));
    }

    private void OnTimelineChanged(object? sender, TimelineChanges changes)
    {
        if (sender is not SessionPaneViewModel pane || !_sessions.TryGetValue(pane, out var session) || ClaudeEvent is null)
        {
            return;
        }

        foreach (var item in changes.Added)
        {
            Raise(ClaudeEvent, ToEvent(session, item, finished: false));
        }

        // A tool call shows up again once its result is in; report that once.
        foreach (var item in changes.Added.Concat(changes.Updated))
        {
            if (item is { Kind: TimelineItemKind.ToolCall, CompletedAt: not null, ToolUseId: { } id } && _finishedTools.Add((pane.PaneId, id)))
            {
                Raise(ClaudeEvent, ToEvent(session, item, finished: true));
            }
        }
    }

    private static ClaudeEventArgs ToEvent(PluginSession session, TimelineItem item, bool finished) => new()
    {
        Session = session,
        Kind = finished ? ClaudeEventKind.ToolResult : item.Kind switch
        {
            TimelineItemKind.Prompt => ClaudeEventKind.Prompt,
            TimelineItemKind.Response => ClaudeEventKind.Response,
            TimelineItemKind.Thinking => ClaudeEventKind.Thinking,
            TimelineItemKind.ToolCall => ClaudeEventKind.ToolCall,
            _ => ClaudeEventKind.System,
        },
        Timestamp = finished ? item.CompletedAt!.Value : item.Timestamp,
        Text = item.Text,
        ToolName = item.Kind == TimelineItemKind.ToolCall ? item.Text : null,
        ToolUseId = item.ToolUseId,
        ToolInputJson = item.ToolInputJson,
        ToolResult = item.ToolResult,
        IsError = item.IsError,
        Duration = item.Duration,
        AgentId = item.AgentId,
    };

    private static PluginSession Snapshot(SessionPaneViewModel pane) => new(
        pane.PaneId,
        pane.SessionId,
        pane.Title,
        pane.WorkingDirectory,
        pane.Activity switch
        {
            SessionActivity.Working => PluginSessionActivity.Working,
            SessionActivity.WaitingForUser => PluginSessionActivity.WaitingForUser,
            _ => PluginSessionActivity.Idle,
        },
        pane.NeedsAttention,
        pane.HasExited);

    /// <summary>One plugin's handler failing must not stop the others, nor the pane that raised the event.</summary>
    private void Raise<T>(EventHandler<T>? handler, T args)
    {
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList().Cast<EventHandler<T>>())
        {
            try
            {
                subscriber(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A plugin failed handling {Event}", typeof(T).Name);
            }
        }
    }
}
