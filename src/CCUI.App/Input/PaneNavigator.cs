using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CCUI.App.Views;
using CCUI.Core.Layout;

namespace CCUI.App.Input;

/// <summary>Moves keyboard focus to the visible pane in a direction (Alt+Arrow), across the main and floating windows.</summary>
public static class PaneNavigator
{
    public static void MoveFocus(NavigationDirection direction)
    {
        var targets = new List<(FrameworkElement Element, Bounds Bounds)>();
        foreach (Window window in Application.Current.Windows)
        {
            if (window.IsVisible)
            {
                Collect(window, targets);
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        var focused = Keyboard.FocusedElement as DependencyObject;
        var current = targets.FirstOrDefault(t => focused is not null && IsWithin(t.Element, focused));
        if (current.Element is null)
        {
            Focus(targets[0].Element);
            return;
        }

        if (SpatialNavigator.FindNeighbor(current.Bounds, targets, direction) is { } next)
        {
            Focus(next);
        }
    }

    private static void Focus(FrameworkElement element)
    {
        switch (element)
        {
            case SessionPaneView pane:
                pane.FocusTerminal();
                break;
            case SessionListView list:
                list.FocusList();
                break;
        }
    }

    private static bool IsWithin(DependencyObject ancestor, DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static void Collect(DependencyObject root, List<(FrameworkElement, Bounds)> targets)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is SessionPaneView or SessionListView)
            {
                var element = (FrameworkElement)child;
                if (element.IsVisible && element.ActualWidth > 0 && PresentationSource.FromVisual(element) is not null)
                {
                    var topLeft = element.PointToScreen(new Point(0, 0));
                    var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
                    targets.Add((element, new Bounds(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y)));
                }

                continue;
            }

            Collect(child, targets);
        }
    }
}
