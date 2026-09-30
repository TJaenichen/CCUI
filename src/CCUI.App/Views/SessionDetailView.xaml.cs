using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CCUI.Core.ViewModels;

namespace CCUI.App.Views;

/// <summary>The detail view. View logic only: a click on the selected tab clears that filter.</summary>
public partial class SessionDetailView : UserControl
{
    public SessionDetailView() => InitializeComponent();

    private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TabControl tabs || DataContext is not SessionDetailViewModel vm)
        {
            return;
        }

        // Only tab headers count: the selected content is hosted by the TabControl, not inside its TabItem.
        for (var node = e.OriginalSource as DependencyObject; node is not null && node != tabs; node = Parent(node))
        {
            if (node is TabItem item)
            {
                vm.SelectTab(tabs.Items.IndexOf(item));
                e.Handled = true;
                return;
            }
        }

        static DependencyObject? Parent(DependencyObject node) =>
            node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
    }
}
