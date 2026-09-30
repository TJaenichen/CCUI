using CCUI.Core.Claude;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CCUI.Core.ViewModels;

/// <summary>The detail view of a session: everything by time, and each kind on its own.</summary>
public sealed partial class SessionDetailViewModel : ObservableObject
{
    private readonly Dictionary<long, TimelineItemViewModel> _bySequence = [];

    public RangeObservableCollection<TimelineItemViewModel> All { get; } = [];

    public RangeObservableCollection<TimelineItemViewModel> Messages { get; } = [];

    public RangeObservableCollection<TimelineItemViewModel> Tools { get; } = [];

    /// <summary>Tool calls whose result was an error, oldest first.</summary>
    public RangeObservableCollection<TimelineItemViewModel> Failed { get; } = [];

    public RangeObservableCollection<TimelineItemViewModel> Thinking { get; } = [];

    public RangeObservableCollection<TimelineItemViewModel> System { get; } = [];

    /// <summary>Keep the newest item in view as items arrive.</summary>
    [ObservableProperty]
    public partial bool FollowNewest { get; set; } = true;

    /// <summary>The selected tab: timeline, messages, tools, failed, thinking, system.</summary>
    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    public const int FailedTab = 3;

    public const int TimelineTab = 0;

    /// <summary>Selects a tab; selecting the current filter again clears it, back to the whole timeline.</summary>
    public void SelectTab(int tab) => SelectedTab = tab == SelectedTab ? TimelineTab : tab;

    /// <summary>Switches to the failed tool calls.</summary>
    public void ShowFailed() => SelectedTab = FailedTab;

    public void Apply(TimelineChanges changes)
    {
        if (changes.Added.Count > 0)
        {
            var added = changes.Added.Select(TimelineItemViewModelFactory.Create).ToList();
            foreach (var vm in added)
            {
                _bySequence[vm.Sequence] = vm;
            }

            All.AddRange(added);
            Messages.AddRange([.. added.Where(v => v is PromptItemViewModel or ResponseItemViewModel)]);
            Tools.AddRange([.. added.OfType<ToolCallItemViewModel>()]);
            Thinking.AddRange([.. added.OfType<ThinkingItemViewModel>()]);
            System.AddRange([.. added.OfType<SystemItemViewModel>()]);
        }

        foreach (var item in changes.Updated)
        {
            if (_bySequence.TryGetValue(item.Sequence, out var vm))
            {
                vm.Refresh();
            }
        }

        // A tool call learns it failed when its result arrives, usually in a later batch than the call itself.
        foreach (var item in changes.Added.Concat(changes.Updated))
        {
            if (item.IsError && _bySequence.TryGetValue(item.Sequence, out var vm) && !Failed.Contains(vm))
            {
                AddFailed(vm);
            }
        }
    }

    private void AddFailed(TimelineItemViewModel vm)
    {
        var index = Failed.Count;
        while (index > 0 && Failed[index - 1].Sequence > vm.Sequence)
        {
            index--;
        }

        Failed.Insert(index, vm);
    }

    public void Clear()
    {
        _bySequence.Clear();
        All.Clear();
        Messages.Clear();
        Tools.Clear();
        Failed.Clear();
        Thinking.Clear();
        System.Clear();
    }
}
