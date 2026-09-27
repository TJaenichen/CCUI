using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CCUI.Core.ViewModels;

/// <summary>An ObservableCollection that can add many items with a single notification.</summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IReadOnlyCollection<T> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        if (items.Count == 1)
        {
            Add(items.First());
            return;
        }

        CheckReentrancy();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
