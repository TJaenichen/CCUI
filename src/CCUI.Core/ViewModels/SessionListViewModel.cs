using System.Collections.ObjectModel;
using CCUI.Core.Claude;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCUI.Core.ViewModels;

/// <summary>A session in the list. When the session is open, live state (activity, agents) comes from its pane.</summary>
public sealed partial class SessionListItemViewModel(SessionSummary summary) : ObservableObject
{
    private static readonly IReadOnlyList<AgentViewModel> NoAgents = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Project), nameof(Branch), nameof(WorkingDirectory), nameof(ToolTip), nameof(LastActive))]
    public partial SessionSummary Summary { get; set; } = summary;

    [ObservableProperty]
    public partial string LastActiveText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(Agents), nameof(Name))]
    public partial SessionPaneViewModel? Pane { get; set; }

    [ObservableProperty]
    public partial bool KilledAtReboot { get; set; }

    [ObservableProperty]
    public partial bool RunningElsewhere { get; set; }

    public string SessionId => Summary.SessionId;

    public string ShortId => Summary.ShortId;

    public string Name => Pane?.Title is { Length: > 0 } live && Summary.Title is null ? live : Summary.DisplayName;

    public string Project => Summary.Project;

    public string? Branch => Summary.GitBranch;

    public string? WorkingDirectory => Summary.WorkingDirectory;

    public DateTimeOffset LastActive => Summary.LastActive;

    public bool IsOpen => Pane is not null;

    /// <summary>Running subagents; the tree shows them as children of the session.</summary>
    public IReadOnlyList<AgentViewModel> Agents => (IReadOnlyList<AgentViewModel>?)Pane?.Agents ?? NoAgents;

    public string ToolTip => string.Join(
        Environment.NewLine,
        new[]
        {
            Summary.WorkingDirectory,
            Summary.GitBranch is { } b ? "Branch: " + b : null,
            Summary.FirstPrompt is { } f ? "First: " + Format.FirstLine(f, 100) : null,
            Summary.LastPrompt is { } l ? "Last: " + Format.FirstLine(l, 100) : null,
            "Session " + Summary.SessionId,
        }.Where(s => s is not null));

    public void Tick(DateTimeOffset now) => LastActiveText = Format.Relative(LastActive, now);
}

/// <summary>The session list: recent sessions, newest first, filterable.</summary>
public sealed partial class SessionListViewModel : ObservableObject
{
    private readonly Dictionary<string, SessionListItemViewModel> _all = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<SessionListItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SessionListItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial int KilledAtRebootCount { get; set; }

    /// <summary>The user wants to open a session.</summary>
    public event EventHandler<SessionListItemViewModel>? OpenRequested;

    public SessionListItemViewModel? Find(string sessionId) => _all.GetValueOrDefault(sessionId);

    public void Update(IReadOnlyList<SessionSummary> summaries, IReadOnlySet<string> killedAtReboot, IReadOnlySet<string> runningElsewhere, Func<string, SessionPaneViewModel?> openPane, DateTimeOffset now)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var summary in summaries)
        {
            seen.Add(summary.SessionId);
            if (_all.TryGetValue(summary.SessionId, out var item))
            {
                if (item.Summary != summary)
                {
                    item.Summary = summary;
                }
            }
            else
            {
                item = new SessionListItemViewModel(summary);
                _all[summary.SessionId] = item;
            }

            item.KilledAtReboot = killedAtReboot.Contains(summary.SessionId);
            item.RunningElsewhere = runningElsewhere.Contains(summary.SessionId);
            item.Pane = openPane(summary.SessionId);
            item.Tick(now);
        }

        foreach (var gone in _all.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _all.Remove(gone);
        }

        KilledAtRebootCount = _all.Values.Count(i => i.KilledAtReboot && !i.IsOpen);
        ApplyFilter();
    }

    /// <summary>Re-links list items to panes after a pane opened or closed.</summary>
    public void RefreshPanes(Func<string, SessionPaneViewModel?> openPane)
    {
        foreach (var item in _all.Values)
        {
            item.Pane = openPane(item.SessionId);
        }

        KilledAtRebootCount = _all.Values.Count(i => i.KilledAtReboot && !i.IsOpen);
    }

    public IEnumerable<SessionListItemViewModel> KilledAtReboot() => _all.Values.Where(i => i.KilledAtReboot && !i.IsOpen && !i.RunningElsewhere);

    public void Tick(DateTimeOffset now)
    {
        foreach (var item in Items)
        {
            item.Tick(now);
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void Open(SessionListItemViewModel? item)
    {
        if ((item ?? SelectedItem) is { } target)
        {
            OpenRequested?.Invoke(this, target);
        }
    }

    private void ApplyFilter()
    {
        var filter = FilterText.Trim();
        var wanted = _all.Values
            .Where(i => filter.Length == 0 || Matches(i, filter))
            .OrderByDescending(i => i.IsOpen)
            .ThenByDescending(i => i.LastActive)
            .ToList();

        // Update in place so selection and tree expansion survive refreshes.
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Items[i]))
            {
                Items.RemoveAt(i);
            }
        }

        for (var target = 0; target < wanted.Count; target++)
        {
            var current = Items.IndexOf(wanted[target]);
            if (current < 0)
            {
                Items.Insert(target, wanted[target]);
            }
            else if (current != target)
            {
                Items.Move(current, target);
            }
        }
    }

    private static bool Matches(SessionListItemViewModel item, string filter) =>
        new[] { item.Name, item.Project, item.Branch, item.WorkingDirectory, item.SessionId, item.Summary.FirstPrompt, item.Summary.LastPrompt }
            .Any(s => s?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
}
