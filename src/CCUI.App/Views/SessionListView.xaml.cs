using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CCUI.Core.ViewModels;

namespace CCUI.App.Views;

public partial class SessionListView : UserControl
{
    public SessionListView() => InitializeComponent();

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

    /// <summary>Fills the session menu's "Move to" submenu with the folders as they are right now.</summary>
    private void OnSessionMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not ShellViewModel shell
            || sender is not FrameworkElement { ContextMenu: { } menu, DataContext: SessionListItemViewModel item }
            || menu.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Tag, "move")) is not { } moveTo)
        {
            return;
        }

        var list = shell.SessionList;
        var current = list.FolderOf(item);
        moveTo.Items.Clear();
        moveTo.Items.Add(Entry("Other (no folder)", current is null, () => list.MoveSession(item, null)));
        if (list.FolderList.Count > 0)
        {
            moveTo.Items.Add(new Separator());
        }

        foreach (var folder in list.FolderList)
        {
            var target = folder;
            moveTo.Items.Add(Entry(new string(' ', folder.Depth * 3) + folder.Name, ReferenceEquals(current, folder), () => list.MoveSession(item, target)));
        }

        static MenuItem Entry(string header, bool isCurrent, Action action)
        {
            var entry = new MenuItem { Header = header, IsChecked = isCurrent };
            entry.Click += (_, _) => action();
            return entry;
        }
    }
}
