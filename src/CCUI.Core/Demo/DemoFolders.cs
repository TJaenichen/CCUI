using CCUI.Core.Folders;

namespace CCUI.Core.Demo;

/// <summary>A folder tree for the demo: the scripted projects under Work and Home, one left for "Other", and an empty client folder.</summary>
public static class DemoFolders
{
    public static FolderTree Create()
    {
        var tree = new FolderTree();
        var work = tree.Add("Work");
        work.VaultFolder = @"C:\notes\Omni\domains\work";
        foreach (var project in DemoScript.Projects.Take(2))
        {
            tree.Add(project.Name, work.Id).WorkingDirectory = project.WorkingDirectory;
        }

        var home = tree.Add("Home");
        foreach (var project in DemoScript.Projects.Skip(2).Take(1))
        {
            tree.Add(project.Name, home.Id).WorkingDirectory = project.WorkingDirectory;
        }

        var clients = tree.Add("Clients");
        clients.IsExpanded = false;
        tree.Add("Acme", clients.Id);
        return tree;
    }
}
