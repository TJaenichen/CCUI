using System.Text.Json;

namespace CCUI.Core.Workspace;

public interface IWorkspaceStore
{
    WorkspaceState? Load();

    void Save(WorkspaceState state);
}

/// <summary>Stores the workspace as JSON. Saves are atomic (write a temp file, then replace), so a crash or reboot
/// mid-save never leaves a broken file behind.</summary>
public sealed class WorkspaceStore(string path) : IWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Lock _gate = new();

    public string Path { get; } = path;

    public WorkspaceState? Load()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(Path) ? JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(Path), JsonOptions) : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public void Save(WorkspaceState state)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temp, Path, overwrite: true);
        }
    }
}
