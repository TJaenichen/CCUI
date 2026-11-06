using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CCUI.Core.ViewModels;

namespace CCUI.App.Views;

public partial class SessionListView : UserControl
{
    private const string RowFormat = "ccui-session-list-row";
    private static readonly System.Windows.Media.Brush DropBrush = (System.Windows.Media.Brush)Application.Current.Resources["SelectedBrush"];
    private Point _pressedAt;
    private object? _pressedRow;

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

    // Drag and drop: a press remembers the row; moving far enough with the button down starts the drag, so plain
    // clicks (including on the expander and the chips inside a row) are untouched.
    private void OnRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressedRow = (sender as FrameworkElement)?.DataContext;
        _pressedAt = e.GetPosition(this);
    }

    private void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedRow is null || !ReferenceEquals(_pressedRow, (sender as FrameworkElement)?.DataContext))
        {
            return;
        }

        var moved = e.GetPosition(this) - _pressedAt;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var row = _pressedRow;
        _pressedRow = null;
        if (row is SessionListItemViewModel or FolderViewModel { IsOther: false })
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(RowFormat, row), DragDropEffects.Move);
        }
    }

    private void OnFolderDragOver(object sender, DragEventArgs e)
    {
        e.Effects = sender is FrameworkElement { DataContext: FolderViewModel target } && CanDrop(e.Data, target) ? DragDropEffects.Move : DragDropEffects.None;
        if (sender is Grid grid)
        {
            grid.Background = e.Effects == DragDropEffects.Move ? DropBrush : System.Windows.Media.Brushes.Transparent;
        }

        e.Handled = true;
    }

    private void OnFolderDragLeave(object sender, DragEventArgs e)
    {
        if (sender is Grid grid)
        {
            grid.Background = System.Windows.Media.Brushes.Transparent;
        }
    }

    private void OnFolderDrop(object sender, DragEventArgs e)
    {
        OnFolderDragLeave(sender, e);
        if (sender is FrameworkElement { DataContext: FolderViewModel target } && CanDrop(e.Data, target))
        {
            Move(e.Data, target.IsOther ? null : target);
        }

        e.Handled = true;
    }

    // Empty space below the rows: out of any folder.
    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(RowFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(RowFormat))
        {
            Move(e.Data, null);
        }

        e.Handled = true;
    }

    private bool CanDrop(IDataObject data, FolderViewModel target)
    {
        if (!data.GetDataPresent(RowFormat) || DataContext is not ShellViewModel shell)
        {
            return false;
        }

        return data.GetData(RowFormat) switch
        {
            SessionListItemViewModel item => !ReferenceEquals(shell.SessionList.FolderOf(item), target.IsOther ? null : target),
            FolderViewModel folder => !ReferenceEquals(folder, target) && !folder.AllFolders().Contains(target),
            _ => false,
        };
    }

    private void Move(IDataObject data, FolderViewModel? target)
    {
        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        switch (data.GetData(RowFormat))
        {
            case SessionListItemViewModel item:
                shell.SessionList.MoveSession(item, target);
                break;
            case FolderViewModel folder:
                shell.SessionList.MoveFolder(folder, target);
                break;
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
