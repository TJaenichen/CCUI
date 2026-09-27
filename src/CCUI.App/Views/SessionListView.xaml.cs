using System.Windows.Controls;

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
}
