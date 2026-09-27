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

    public RangeObservableCollection<TimelineItemViewModel> Thinking { get; } = [];

    public RangeObservableCollection<TimelineItemViewModel> System { get; } = [];

    /// <summary>Keep the newest item in view as items arrive.</summary>
    [ObservableProperty]
    public partial bool FollowNewest { get; set; } = true;

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
    }

    public void Clear()
    {
        _bySequence.Clear();
        All.Clear();
        Messages.Clear();
        Tools.Clear();
        Thinking.Clear();
        System.Clear();
    }
}
