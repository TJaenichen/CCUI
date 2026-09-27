using System.Windows.Controls;
using AvalonDock;
using AvalonDock.Layout;

namespace CCUI.App.Views;

/// <summary>Arranges docked documents in a grid, like the mock-up: columns side by side, panes stacked in each.</summary>
public static class DockLayout
{
    public static void Tile(DockingManager dock, int maxRowsPerColumn)
    {
        var root = dock.Layout;
        var documents = root.Descendents().OfType<LayoutDocument>().Where(d => !d.IsFloating).ToList();
        if (documents.Count == 0)
        {
            return;
        }

        var active = documents.FirstOrDefault(d => d.IsActive) ?? documents[^1];
        var columns = Math.Max((int)Math.Ceiling(documents.Count / (double)maxRowsPerColumn), Math.Min(documents.Count, 2));
        var perColumn = (int)Math.Ceiling(documents.Count / (double)columns);

        // Build the new structure inside the live layout first, then move the documents into it, so each document
        // stays attached to the layout throughout (detaching one would look like closing it).
        var panel = root.RootPanel;
        var previous = panel.Children.ToList();
        panel.Orientation = Orientation.Horizontal;
        var panes = new List<LayoutDocumentPane>();
        for (var c = 0; c < columns; c++)
        {
            var column = new LayoutDocumentPaneGroup { Orientation = Orientation.Vertical };
            var rows = Math.Min(perColumn, documents.Count - (c * perColumn));
            for (var r = 0; r < rows; r++)
            {
                var pane = new LayoutDocumentPane();
                column.Children.Add(pane);
                panes.Add(pane);
            }

            if (column.ChildrenCount > 0)
            {
                panel.Children.Add(column);
            }
        }

        for (var i = 0; i < documents.Count && i < panes.Count; i++)
        {
            panes[i].Children.Add(documents[i]);
        }

        foreach (var old in previous)
        {
            if (!old.Descendents().OfType<LayoutContent>().Any())
            {
                panel.RemoveChild(old);
            }
        }

        root.CollectGarbage();
        active.IsActive = true;
    }
}
