using System.Text.Json;

namespace CCUI.Core.Folders;

public interface IFolderStore
{
    /// <summary>The saved tree, or an empty one when there is no file yet or it cannot be read.</summary>
    FolderTree Load();

    void Save(FolderTree tree);
}

/// <summary>Stores the folder tree as JSON next to the workspace (folders.json), with the same atomic save.</summary>
public sealed class FolderStore(string path) : IFolderStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Lock _gate = new();

    public string Path { get; } = path;

    public FolderTree Load()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(Path) ? JsonSerializer.Deserialize<FolderTree>(File.ReadAllText(Path), JsonOptions) ?? new FolderTree() : new FolderTree();
            }
            catch (JsonException)
            {
                return new FolderTree();
            }
        }
    }

    public void Save(FolderTree tree)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(tree, JsonOptions));
            File.Move(temp, Path, overwrite: true);
        }
    }
}

/// <summary>Keeps the tree in memory only; for tests and the demo.</summary>
public sealed class MemoryFolderStore : IFolderStore
{
    public FolderTree Tree { get; set; } = new();

    public int Saves { get; private set; }

    public FolderTree Load() => Tree;

    public void Save(FolderTree tree)
    {
        Tree = tree;
        Saves++;
    }
}
