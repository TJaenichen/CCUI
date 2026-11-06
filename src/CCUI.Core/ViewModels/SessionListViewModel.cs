using System.Collections.ObjectModel;
using CCUI.Core.Claude;
using CCUI.Core.Folders;
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

    /// <summary>Pinned to the top of its folder.</summary>
    [ObservableProperty]
    public partial bool IsPinned { get; set; }

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

    partial void OnPaneChanged(SessionPaneViewModel? oldValue, SessionPaneViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnPanePropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnPanePropertyChanged;
        }
    }

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionPaneViewModel.Title))
        {
            OnPropertyChanged(nameof(Name));
        }
    }
}

/// <summary>A folder row in the session list; its children are subfolders followed by sessions.</summary>
public sealed partial class FolderViewModel : ObservableObject
{
    private readonly Action<FolderViewModel>? _expansionChanged;
    private bool _refreshing;

    public FolderViewModel(SessionFolder? folder, Action<FolderViewModel>? expansionChanged)
    {
        Model = folder;
        _expansionChanged = expansionChanged;
        Name = folder?.Name ?? "Other";
    }

    /// <summary>The stored folder; null for the implicit "Other" that holds unplaced sessions.</summary>
    public SessionFolder? Model { get; }

    public string? Id => Model?.Id;

    public bool IsOther => Model is null;

    public ObservableCollection<object> Children { get; } = [];

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkingDirectory), nameof(ToolTip))]
    public partial string? WorkingDirectory { get; set; }

    /// <summary>The folder's own vault folder, if set here rather than inherited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnVaultFolder))]
    public partial string? VaultFolder { get; set; }

    /// <summary>The vault folder in effect: this folder's, or the nearest ancestor's.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVaultFolder), nameof(ToolTip))]
    public partial string? EffectiveVaultFolder { get; set; }

    [ObservableProperty]
    public partial int SessionCount { get; set; }

    [ObservableProperty]
    public partial int WorkingCount { get; set; }

    [ObservableProperty]
    public partial int AttentionCount { get; set; }

    /// <summary>0 at the top level; menus indent by it.</summary>
    [ObservableProperty]
    public partial int Depth { get; set; }

    public bool HasWorkingDirectory => !string.IsNullOrWhiteSpace(WorkingDirectory);

    public bool HasOwnVaultFolder => !string.IsNullOrWhiteSpace(VaultFolder);

    public bool HasVaultFolder => !string.IsNullOrWhiteSpace(EffectiveVaultFolder);

    public string ToolTip => string.Join(
        Environment.NewLine,
        new[]
        {
            IsOther ? "Sessions not placed in a folder." : null,
            WorkingDirectory is { Length: > 0 } dir ? "Sessions start in " + dir : null,
            EffectiveVaultFolder is { Length: > 0 } vault ? "Vault: " + vault : null,
        }.Where(s => s is not null));

    /// <summary>Every session below this folder, subfolders included.</summary>
    public IEnumerable<SessionListItemViewModel> AllSessions() =>
        Children.SelectMany(c => c switch
        {
            SessionListItemViewModel s => [s],
            FolderViewModel f => f.AllSessions(),
            _ => Enumerable.Empty<SessionListItemViewModel>(),
        });

    internal void Refresh(FolderTree tree)
    {
        if (Model is null)
        {
            return;
        }

        _refreshing = true;
        try
        {
            Name = Model.Name;
            IsExpanded = Model.IsExpanded;
            WorkingDirectory = Model.WorkingDirectory;
            VaultFolder = Model.VaultFolder;
            EffectiveVaultFolder = tree.EffectiveVaultFolder(Model.Id);
            Depth = Math.Max(0, tree.PathTo(Model.Id).Count - 1);
        }
        finally
        {
            _refreshing = false;
        }
    }

    internal void Count()
    {
        foreach (var child in Children.OfType<FolderViewModel>())
        {
            child.Count();
        }

        var sessions = AllSessions().ToList();
        SessionCount = sessions.Count;
        WorkingCount = sessions.Count(s => s.Pane is { Activity: SessionActivity.Working, HasExited: false });
        AttentionCount = sessions.Count(s => s.Pane is { NeedsAttention: true });
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!_refreshing && Model is not null)
        {
            Model.IsExpanded = value;
            _expansionChanged?.Invoke(this);
        }
    }
}

/// <summary>
/// The session list: recent sessions, newest first, filterable, and arranged in the user's folders. Placement is
/// explicit (a session sits where it was started or dragged to); sessions without a placement follow a folder's
/// working-directory rule or land in "Other". With no folders at all, or while filtering, the list is flat.
/// </summary>
public sealed partial class SessionListViewModel : ObservableObject
{
    private readonly Dictionary<string, SessionListItemViewModel> _all = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FolderViewModel> _folders = [];
    private readonly FolderViewModel _other;
    private readonly IFolderStore _store;
    private readonly IDialogService? _dialogs;
    private readonly IClipboardService? _clipboard;
    private readonly FolderTree _tree;

    // Sessions the user no longer wants flagged as killed by the reboot.
    private readonly HashSet<string> _dismissedReboot = new(StringComparer.OrdinalIgnoreCase);

    public SessionListViewModel()
        : this(new MemoryFolderStore(), null, null)
    {
    }

    public SessionListViewModel(IFolderStore store, IDialogService? dialogs, IClipboardService? clipboard)
    {
        _store = store;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _tree = store.Load();
        _other = new FolderViewModel(null, null);
    }

    /// <summary>The flat list, newest first (and what the filter searches).</summary>
    public ObservableCollection<SessionListItemViewModel> Items { get; } = [];

    /// <summary>What the tree shows: folders and "Other", or the flat items while filtering or without folders.</summary>
    public ObservableCollection<object> Roots { get; } = [];

    /// <summary>Every folder in tree order, for "move to" menus.</summary>
    public ObservableCollection<FolderViewModel> FolderList { get; } = [];

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial object? SelectedItem { get; set; }

    /// <summary>A single click opens a session (the default is a double click); the view reads it.</summary>
    [ObservableProperty]
    public partial bool OpenOnClick { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KilledAtRebootText))]
    public partial int KilledAtRebootCount { get; set; }

    [ObservableProperty]
    public partial bool HasFolders { get; set; }

    public string KilledAtRebootText => KilledAtRebootCount == 1
        ? "1 session was killed by the last reboot."
        : $"{KilledAtRebootCount} sessions were killed by the last reboot.";

    /// <summary>The user wants to open a session.</summary>
    public event EventHandler<SessionListItemViewModel>? OpenRequested;

    /// <summary>The user wants a new session in a folder.</summary>
    public event EventHandler<FolderViewModel>? NewSessionRequested;

    public SessionListItemViewModel? Find(string sessionId) => _all.GetValueOrDefault(sessionId);

    /// <summary>The folder a session is placed in, or null.</summary>
    public FolderViewModel? FolderOf(SessionListItemViewModel item) =>
        _tree.FolderOf(item.SessionId) is { } folder ? _folders.GetValueOrDefault(folder.Id) : null;

    /// <summary>Session ids whose reboot flag was dismissed, for the workspace to remember.</summary>
    public IReadOnlyCollection<string> DismissedReboot => _dismissedReboot;

    /// <summary>Brings back dismissals saved with the workspace.</summary>
    public void RestoreDismissedReboot(IEnumerable<string> sessionIds) => _dismissedReboot.UnionWith(sessionIds);

    /// <summary>Stops flagging these sessions as killed by the reboot.</summary>
    public void DismissReboot(IEnumerable<SessionListItemViewModel> items)
    {
        foreach (var item in items)
        {
            _dismissedReboot.Add(item.SessionId);
            item.KilledAtReboot = false;
        }

        CountKilledAtReboot();
    }

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

            item.KilledAtReboot = killedAtReboot.Contains(summary.SessionId) && !_dismissedReboot.Contains(summary.SessionId);
            item.RunningElsewhere = runningElsewhere.Contains(summary.SessionId);
            item.Pane = openPane(summary.SessionId);
            item.IsPinned = _tree.IsPinned(summary.SessionId);
            item.Tick(now);
        }

        foreach (var gone in _all.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _all.Remove(gone);
        }

        // Forget dismissals the detector no longer reports (the session was resumed, or a later reboot took over).
        _dismissedReboot.IntersectWith(killedAtReboot);
        CountKilledAtReboot();
        ApplyFilter();
    }

    /// <summary>Re-links list items to panes after a pane opened or closed.</summary>
    public void RefreshPanes(Func<string, SessionPaneViewModel?> openPane)
    {
        foreach (var item in _all.Values)
        {
            item.Pane = openPane(item.SessionId);
        }

        CountKilledAtReboot();
        ApplyFilter();
    }

    public IEnumerable<SessionListItemViewModel> KilledAtReboot() => _all.Values.Where(i => i.KilledAtReboot && !i.IsOpen && !i.RunningElsewhere);

    private void CountKilledAtReboot() => KilledAtRebootCount = _all.Values.Count(i => i.KilledAtReboot && !i.IsOpen);

    public void Tick(DateTimeOffset now)
    {
        foreach (var item in _all.Values)
        {
            item.Tick(now);
        }

        foreach (var folder in Roots.OfType<FolderViewModel>())
        {
            folder.Count();
        }
    }

    /// <summary>Puts a session in a folder (null for none) and remembers it.</summary>
    public void Place(string sessionId, string? folderId)
    {
        if (_tree.Place(sessionId, folderId))
        {
            Save();
            ApplyFilter();
        }
    }

    /// <summary>Keeps a pane's placement and pin when /clear gave it a new session id.</summary>
    public void SessionIdChanged(string oldSessionId, string newSessionId)
    {
        if (oldSessionId.Equals(newSessionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var folder = _tree.FolderOf(oldSessionId);
        var pinned = _tree.IsPinned(oldSessionId);
        if (folder is null && !pinned)
        {
            return;
        }

        _tree.Forget(oldSessionId);
        _tree.Place(newSessionId, folder?.Id);
        _tree.Pin(newSessionId, pinned);
        Save();
        ApplyFilter();
    }

    public void MoveSession(SessionListItemViewModel item, FolderViewModel? folder) => Place(item.SessionId, folder?.Id);

    public bool MoveFolder(FolderViewModel folder, FolderViewModel? newParent, int index = -1)
    {
        if (folder.Id is null || !_tree.Move(folder.Id, newParent?.Id, index))
        {
            return false;
        }

        Save();
        ApplyFilter();
        return true;
    }

    [RelayCommand]
    private void NewFolder(FolderViewModel? parent)
    {
        var name = _dialogs?.Prompt("New folder", "Name", "New folder") ?? "New folder";
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var folder = _tree.Add(name.Trim(), parent?.Id);
        if (parent?.Model is { } model)
        {
            model.IsExpanded = true;
        }

        Save();
        ApplyFilter();
        SelectedItem = _folders.GetValueOrDefault(folder.Id);
    }

    [RelayCommand]
    private void RenameFolder(FolderViewModel? folder)
    {
        if (folder?.Id is not { } id || _dialogs?.Prompt("Rename folder", "Name", folder.Name) is not { } name || !_tree.Rename(id, name))
        {
            return;
        }

        Save();
        ApplyFilter();
    }

    /// <summary>Removes a folder; what it held moves up one level.</summary>
    [RelayCommand]
    private void DeleteFolder(FolderViewModel? folder)
    {
        if (folder?.Id is not { } id)
        {
            return;
        }

        var contents = folder.Children.Count;
        if (contents > 0 && _dialogs is not null
            && !_dialogs.Confirm("Delete folder", $"Delete '{folder.Name}'? Its {contents} item(s) move up one level; no session is deleted."))
        {
            return;
        }

        _tree.Delete(id);
        Save();
        ApplyFilter();
    }

    /// <summary>Sets where "New session here" starts, which also collects unplaced sessions from under that directory.</summary>
    [RelayCommand]
    private void SetFolderDirectory(FolderViewModel? folder)
    {
        if (folder?.Model is not { } model || _dialogs?.PickFolder("Sessions in '" + folder.Name + "' start in…", model.WorkingDirectory) is not { } directory)
        {
            return;
        }

        model.WorkingDirectory = directory;
        Save();
        ApplyFilter();
    }

    [RelayCommand]
    private void ClearFolderDirectory(FolderViewModel? folder)
    {
        if (folder?.Model is not { } model)
        {
            return;
        }

        model.WorkingDirectory = null;
        Save();
        ApplyFilter();
    }

    /// <summary>Assigns a vault folder (an Obsidian vault, or a folder inside one) to the folder and everything below it.</summary>
    [RelayCommand]
    private void SetVaultFolder(FolderViewModel? folder)
    {
        if (folder?.Model is not { } model || _dialogs?.PickFolder("Vault folder for '" + folder.Name + "'", model.VaultFolder ?? folder.EffectiveVaultFolder) is not { } directory)
        {
            return;
        }

        model.VaultFolder = directory;
        Save();
        ApplyFilter();
    }

    [RelayCommand]
    private void ClearVaultFolder(FolderViewModel? folder)
    {
        if (folder?.Model is not { } model)
        {
            return;
        }

        model.VaultFolder = null;
        Save();
        ApplyFilter();
    }

    /// <summary>Opens the folder's vault folder in Obsidian when it is installed, otherwise in Explorer.</summary>
    [RelayCommand]
    private void OpenVaultFolder(FolderViewModel? folder)
    {
        if (folder?.EffectiveVaultFolder is not { Length: > 0 } path || _dialogs is null)
        {
            return;
        }

        if (!_dialogs.Open("obsidian://open?path=" + Uri.EscapeDataString(path)))
        {
            _dialogs.Open(path);
        }
    }

    [RelayCommand]
    private void NewSessionIn(FolderViewModel? folder)
    {
        if (folder is not null)
        {
            NewSessionRequested?.Invoke(this, folder);
        }
    }

    [RelayCommand]
    private void TogglePin(SessionListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _tree.Pin(item.SessionId, !item.IsPinned);
        item.IsPinned = !item.IsPinned;
        Save();
        ApplyFilter();
    }

    [RelayCommand]
    private void CopySessionId(SessionListItemViewModel? item)
    {
        if (item is not null)
        {
            _clipboard?.SetText(item.SessionId);
        }
    }

    [RelayCommand]
    private void OpenSessionFolder(SessionListItemViewModel? item)
    {
        if (item?.WorkingDirectory is { Length: > 0 } directory)
        {
            _dialogs?.Open(directory);
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void Open(SessionListItemViewModel? item)
    {
        if ((item ?? SelectedItem as SessionListItemViewModel) is { } target)
        {
            OpenRequested?.Invoke(this, target);
        }
    }

    private void Save() => _store.Save(_tree);

    private void ApplyFilter()
    {
        var filter = FilterText.Trim();
        var wanted = _all.Values
            .Where(i => filter.Length == 0 || Matches(i, filter))
            .OrderByDescending(i => i.IsOpen)
            .ThenByDescending(i => i.LastActive)
            .ThenByDescending(i => i.Summary.LastWrite)
            .ToList();

        Sync(Items, wanted);
        BuildTree(filter.Length > 0);
    }

    private void BuildTree(bool filtering)
    {
        HasFolders = _tree.Folders.Count > 0;
        Sync(FolderList, _tree.All().Select(Node).ToList());
        foreach (var stale in _folders.Keys.Where(id => _tree.Find(id) is null).ToList())
        {
            _folders.Remove(stale);
        }

        if (filtering || !HasFolders)
        {
            Sync(Roots, Items.Cast<object>().ToList());
            return;
        }

        // Which folder each unplaced session shows in: a working-directory rule, or "Other".
        var placed = new Dictionary<string, List<SessionListItemViewModel>>();
        var other = new List<SessionListItemViewModel>();
        foreach (var item in _all.Values)
        {
            var folder = _tree.FolderOf(item.SessionId) ?? _tree.DefaultFolderFor(item.WorkingDirectory);
            if (folder is null)
            {
                other.Add(item);
            }
            else
            {
                (placed.TryGetValue(folder.Id, out var list) ? list : placed[folder.Id] = []).Add(item);
            }
        }

        foreach (var folder in _tree.All())
        {
            var node = Node(folder);
            var children = new List<object>();
            children.AddRange(folder.Children.Select(Node));
            children.AddRange(Order(placed.GetValueOrDefault(folder.Id) ?? []));
            Sync(node.Children, children);
        }

        Sync(_other.Children, Order(other).Cast<object>().ToList());
        var roots = new List<object>(_tree.Folders.Select(Node));
        if (_other.Children.Count > 0)
        {
            roots.Add(_other);
        }

        Sync(Roots, roots);
        foreach (var node in Roots.OfType<FolderViewModel>())
        {
            node.Count();
        }
    }

    private FolderViewModel Node(SessionFolder folder)
    {
        if (!_folders.TryGetValue(folder.Id, out var node))
        {
            node = new FolderViewModel(folder, _ => Save());
            _folders[folder.Id] = node;
        }

        node.Refresh(_tree);
        return node;
    }

    /// <summary>Pinned first in their pinned order, then open sessions, then by recency.</summary>
    private IEnumerable<SessionListItemViewModel> Order(IEnumerable<SessionListItemViewModel> items)
    {
        var pinOrder = _tree.Pinned.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i, StringComparer.OrdinalIgnoreCase);
        return items
            .OrderBy(i => pinOrder.TryGetValue(i.SessionId, out var p) ? p : int.MaxValue)
            .ThenByDescending(i => i.IsOpen)
            .ThenByDescending(i => i.LastActive)
            .ThenByDescending(i => i.Summary.LastWrite);
    }

    /// <summary>Updates a collection in place so selection and tree expansion survive refreshes.</summary>
    private static void Sync<T>(ObservableCollection<T> target, IList<T> wanted)
        where T : class
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (var position = 0; position < wanted.Count; position++)
        {
            var current = target.IndexOf(wanted[position]);
            if (current < 0)
            {
                target.Insert(position, wanted[position]);
            }
            else if (current != position)
            {
                target.Move(current, position);
            }
        }
    }

    private static bool Matches(SessionListItemViewModel item, string filter) =>
        new[] { item.Name, item.Project, item.Branch, item.WorkingDirectory, item.SessionId, item.Summary.FirstPrompt, item.Summary.LastPrompt }
            .Any(s => s?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
}
