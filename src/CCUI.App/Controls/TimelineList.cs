using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CCUI.App.Controls;

/// <summary>A virtualised list that keeps the newest entry in view, unless the user scrolled up to read.</summary>
public sealed class TimelineList : ListBox
{
    public static readonly DependencyProperty FollowNewestProperty = DependencyProperty.Register(
        nameof(FollowNewest), typeof(bool), typeof(TimelineList), new PropertyMetadata(true));

    private ScrollViewer? _scroll;

    public bool FollowNewest
    {
        get => (bool)GetValue(FollowNewestProperty);
        set => SetValue(FollowNewestProperty, value);
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        if (!FollowNewest || Items.Count == 0 || e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset))
        {
            return;
        }

        _scroll ??= FindScrollViewer(this);
        var atBottom = _scroll is null || _scroll.VerticalOffset >= _scroll.ScrollableHeight - 2;
        if (atBottom)
        {
            ScrollIntoView(Items[^1]);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if ((child as ScrollViewer ?? FindScrollViewer(child)) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
