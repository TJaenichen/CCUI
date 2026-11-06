namespace CCUI.Core.Folders;

/// <summary>
/// A folder in the session list: a name, optional child folders, the sessions placed in it, an optional working
/// directory ("new session here" starts there, and sessions found on disk under it land here unless placed
/// elsewhere) and an optional vault folder that applies to the folder, its sessions and everything below it.
/// </summary>
public sealed class SessionFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "New folder";

    public string? WorkingDirectory { get; set; }

    /// <summary>A folder of notes (an Obsidian vault or a folder inside one) for this subtree; inherited downwards.</summary>
    public string? VaultFolder { get; set; }

    public bool IsExpanded { get; set; } = true;

    public List<SessionFolder> Children { get; set; } = [];

    /// <summary>Session ids placed here, in display order.</summary>
    public List<string> Sessions { get; set; } = [];
}

/// <summary>
/// The user's folder tree for the session list, as saved in folders.json. Placement is explicit: a session belongs
/// to the folder it was started in or dragged to, and is remembered by its id. Every mutation keeps the invariant
/// that a session id appears in at most one folder.
/// </summary>
public sealed class FolderTree
{
    public int Version { get; set; } = 1;

    public List<SessionFolder> Folders { get; set; } = [];

    /// <summary>Session ids pinned to the top of their folder.</summary>
    public List<string> Pinned { get; set; } = [];

    /// <summary>Every folder, parents before children.</summary>
    public IEnumerable<SessionFolder> All() => Flatten(Folders);

    public SessionFolder? Find(string folderId) => All().FirstOrDefault(f => f.Id == folderId);

    /// <summary>The folder's parent, or null at the top level (or when the folder is unknown).</summary>
    public SessionFolder? ParentOf(string folderId) => All().FirstOrDefault(f => f.Children.Any(c => c.Id == folderId));

    /// <summary>The top-level folder down to the folder itself; empty when the folder is unknown.</summary>
    public IReadOnlyList<SessionFolder> PathTo(string folderId)
    {
        var path = new List<SessionFolder>();
        return Descend(Folders, folderId, path) ? path : [];

        static bool Descend(List<SessionFolder> folders, string id, List<SessionFolder> path)
        {
            foreach (var folder in folders)
            {
                path.Add(folder);
                if (folder.Id == id || Descend(folder.Children, id, path))
                {
                    return true;
                }

                path.RemoveAt(path.Count - 1);
            }

            return false;
        }
    }

    /// <summary>The folder a session was placed in, or null when it is unplaced.</summary>
    public SessionFolder? FolderOf(string sessionId) => All().FirstOrDefault(f => f.Sessions.Contains(sessionId, StringComparer.OrdinalIgnoreCase));

    public SessionFolder Add(string name, string? parentId = null)
    {
        var folder = new SessionFolder { Name = name };
        (parentId is { } id && Find(id) is { } parent ? parent.Children : Folders).Add(folder);
        return folder;
    }

    public bool Rename(string folderId, string name)
    {
        if (Find(folderId) is not { } folder || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        folder.Name = name.Trim();
        return true;
    }

    /// <summary>Removes the folder; its subfolders and sessions move up to its parent (sessions at the top level become unplaced).</summary>
    public bool Delete(string folderId)
    {
        if (Find(folderId) is not { } folder)
        {
            return false;
        }

        var parent = ParentOf(folderId);
        var siblings = parent?.Children ?? Folders;
        var index = siblings.IndexOf(folder);
        siblings.RemoveAt(index);
        siblings.InsertRange(index, folder.Children);
        parent?.Sessions.AddRange(folder.Sessions.Where(s => !parent.Sessions.Contains(s, StringComparer.OrdinalIgnoreCase)));
        return true;
    }

    /// <summary>Moves a folder under another (null for the top level) at the index, or at the end. A folder cannot move into itself or its own subtree.</summary>
    public bool Move(string folderId, string? newParentId, int index = -1)
    {
        if (Find(folderId) is not { } folder)
        {
            return false;
        }

        if (newParentId is not null && (newParentId == folderId || PathTo(newParentId).Any(f => f.Id == folderId)))
        {
            return false;
        }

        var target = newParentId is { } id ? Find(id)?.Children : Folders;
        if (target is null)
        {
            return false;
        }

        var source = ParentOf(folderId)?.Children ?? Folders;
        var from = source.IndexOf(folder);
        source.RemoveAt(from);
        if (ReferenceEquals(source, target) && index > from)
        {
            index--;
        }

        target.Insert(index < 0 || index > target.Count ? target.Count : index, folder);
        return true;
    }

    /// <summary>Puts a session in a folder (null unplaces it), removing it from wherever it was.</summary>
    public bool Place(string sessionId, string? folderId, int index = -1)
    {
        var target = folderId is { } id ? Find(id) : null;
        if (folderId is not null && target is null)
        {
            return false;
        }

        Forget(sessionId);
        if (target is not null)
        {
            target.Sessions.Insert(index < 0 || index > target.Sessions.Count ? target.Sessions.Count : index, sessionId);
        }

        return true;
    }

    /// <summary>Drops a session from its folder and the pins, e.g. when its transcript is gone.</summary>
    public bool Forget(string sessionId)
    {
        var removed = Pinned.RemoveAll(s => s.Equals(sessionId, StringComparison.OrdinalIgnoreCase)) > 0;
        foreach (var folder in All())
        {
            removed |= folder.Sessions.RemoveAll(s => s.Equals(sessionId, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        return removed;
    }

    public bool IsPinned(string sessionId) => Pinned.Contains(sessionId, StringComparer.OrdinalIgnoreCase);

    public void Pin(string sessionId, bool pinned)
    {
        Pinned.RemoveAll(s => s.Equals(sessionId, StringComparison.OrdinalIgnoreCase));
        if (pinned)
        {
            Pinned.Add(sessionId);
        }
    }

    /// <summary>The vault folder that applies to a folder: its own, or the nearest ancestor's.</summary>
    public string? EffectiveVaultFolder(string folderId) =>
        PathTo(folderId).LastOrDefault(f => !string.IsNullOrWhiteSpace(f.VaultFolder))?.VaultFolder;

    /// <summary>Where an unplaced session with this working directory belongs: the deepest folder whose working
    /// directory contains it, or null.</summary>
    public SessionFolder? DefaultFolderFor(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return null;
        }

        return All()
            .Where(f => !string.IsNullOrWhiteSpace(f.WorkingDirectory) && IsUnder(workingDirectory, f.WorkingDirectory!))
            .OrderByDescending(f => PathTo(f.Id).Count)
            .ThenByDescending(f => f.WorkingDirectory!.Length)
            .FirstOrDefault();
    }

    /// <summary>True when the directory is the root or lies below it, comparing paths the Windows way.</summary>
    public static bool IsUnder(string directory, string root)
    {
        var dir = Normalize(directory);
        var top = Normalize(root);
        return dir.Equals(top, StringComparison.OrdinalIgnoreCase) || dir.StartsWith(top + '\\', StringComparison.OrdinalIgnoreCase);

        static string Normalize(string path) => path.Trim().Replace('/', '\\').TrimEnd('\\');
    }

    private static IEnumerable<SessionFolder> Flatten(List<SessionFolder> folders)
    {
        foreach (var folder in folders)
        {
            yield return folder;
            foreach (var child in Flatten(folder.Children))
            {
                yield return child;
            }
        }
    }
}
