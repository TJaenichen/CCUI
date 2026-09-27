namespace CCUI.Core.Workspace;

/// <summary>Everything needed to bring the window back as it was: open sessions, their panes and the layout.</summary>
public sealed record WorkspaceState
{
    public int Version { get; init; } = 1;

    public DateTimeOffset SavedAt { get; init; }

    public List<WorkspacePane> Panes { get; init; } = [];

    /// <summary>The docking layout as serialised by the view (opaque to the core).</summary>
    public string? DockLayout { get; init; }

    public WindowPlacement? Window { get; init; }

    public double SessionListWidth { get; init; } = 320;

    public bool SessionListVisible { get; init; } = true;
}

public sealed record WorkspacePane(string PaneId, string SessionId, string WorkingDirectory, string? Title, bool DetailOpen, double DetailHeight);

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
