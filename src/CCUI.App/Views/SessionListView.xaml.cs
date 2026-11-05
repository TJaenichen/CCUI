using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CCUI.Core.ViewModels;

namespace CCUI.App.Views;

public partial class SessionListView : UserControl
{
    public SessionListView() => InitializeComponent();

    // The mouse-down already selected the item; with OpenOnClick the release opens it too. Buttons inside the item
    // (Reopen, dismiss) handle their own release, so it never gets here from them.
    private void OnItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ShellViewModel { SessionList.OpenOnClick: true } shell && sender is FrameworkElement { DataContext: SessionListItemViewModel item })
        {
            shell.OpenSessionCommand.Execute(item);
            e.Handled = true;
        }
    }

    /// <summary>Moves keyboard focus to the selected (or first) session.</summary>
    public void FocusList()
    {
        var container = Tree.SelectedItem is { } selected
            ? Tree.ItemContainerGenerator.ContainerFromItem(selected)
            : Tree.ItemContainerGenerator.ContainerFromIndex(0);
        if (container is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
        else
        {
            Tree.Focus();
        }
    }
}
