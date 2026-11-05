using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CCUI.Plugins;

/// <summary>
/// A CCUI plugin. Put a DLL with a class implementing this into a folder under %LOCALAPPDATA%\CCUI\plugins\; CCUI
/// loads it at start, creates it with its parameterless constructor on the UI thread and calls
/// <see cref="Initialize"/>. Everything the plugin needs comes through the <see cref="IPluginHost"/>.
/// </summary>
public interface ICcuiPlugin
{
    /// <summary>Shown in the settings page and used for the plugin's settings section and data folder.</summary>
    string Name { get; }

    void Initialize(IPluginHost host);
}

/// <summary>What CCUI offers a plugin. Events are raised on the UI thread.</summary>
public interface IPluginHost
{
    ILogger Logger { get; }

    /// <summary>The plugin's own settings: section PluginSettings:{Name} of CCUI's configuration, so they can live in
    /// %LOCALAPPDATA%\CCUI\appsettings.user.json like every other setting.</summary>
    IConfigurationSection Settings { get; }

    TimeProvider Time { get; }

    /// <summary>A folder for the plugin's own files, created on first use.</summary>
    string DataDirectory { get; }

    /// <summary>The sessions open right now.</summary>
    IReadOnlyList<PluginSession> Sessions { get; }

    /// <summary>A session pane was opened (new, resumed or restored).</summary>
    event EventHandler<PluginSessionEventArgs>? SessionOpened;

    /// <summary>A session's title, id, activity, attention flag or exit state changed.</summary>
    event EventHandler<PluginSessionEventArgs>? SessionChanged;

    event EventHandler<PluginSessionEventArgs>? SessionClosed;

    /// <summary>Something happened in a session's transcript: a prompt, a response, thinking, a tool call, a tool
    /// result or a system message.</summary>
    event EventHandler<ClaudeEventArgs>? ClaudeEvent;

    /// <summary>Shows a panel above or below the session list. The content is the plugin's own WPF element; the
    /// app's theme brushes (AccentBrush, TextPrimaryBrush, PanelBrush, ...) are in Application.Current.Resources.</summary>
    void AddSidebarPanel(SidebarPanel panel);
}

/// <summary>A panel in the session list column.</summary>
public sealed class SidebarPanel
{
    /// <summary>A small caption; empty for none.</summary>
    public string Title { get; init; } = string.Empty;

    public required FrameworkElement Content { get; init; }

    public SidebarSlot Slot { get; init; } = SidebarSlot.Bottom;
}

public enum SidebarSlot
{
    /// <summary>Above the session list.</summary>
    Top,

    /// <summary>Below the session list.</summary>
    Bottom,
}

public enum PluginSessionActivity
{
    Idle,
    Working,
    WaitingForUser,
}

/// <summary>A snapshot of a session pane. <see cref="PaneId"/> stays the same for the life of the pane; the session
/// id changes on /clear.</summary>
public sealed record PluginSession(
    string PaneId,
    string SessionId,
    string Title,
    string WorkingDirectory,
    PluginSessionActivity Activity,
    bool NeedsAttention,
    bool HasExited);

public sealed class PluginSessionEventArgs(PluginSession session) : EventArgs
{
    public PluginSession Session { get; } = session;
}

public enum ClaudeEventKind
{
    /// <summary>The user sent a prompt (slash commands included).</summary>
    Prompt,

    /// <summary>Claude answered with text.</summary>
    Response,

    Thinking,

    /// <summary>Claude called a tool; <see cref="ClaudeEventArgs.ToolName"/> and <see cref="ClaudeEventArgs.ToolInputJson"/> say which.</summary>
    ToolCall,

    /// <summary>A tool call finished; <see cref="ClaudeEventArgs.ToolResult"/>, <see cref="ClaudeEventArgs.IsError"/> and
    /// <see cref="ClaudeEventArgs.Duration"/> are set.</summary>
    ToolResult,

    /// <summary>A system message in the transcript (compaction, errors, ...).</summary>
    System,
}

public sealed class ClaudeEventArgs : EventArgs
{
    public required PluginSession Session { get; init; }

    public required ClaudeEventKind Kind { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>The prompt, response, thinking or system text; the tool name for tool calls.</summary>
    public required string Text { get; init; }

    public string? ToolName { get; init; }

    public string? ToolUseId { get; init; }

    public string? ToolInputJson { get; init; }

    public string? ToolResult { get; init; }

    public bool IsError { get; init; }

    public TimeSpan? Duration { get; init; }

    /// <summary>Set when the event came from a subagent rather than the session itself.</summary>
    public string? AgentId { get; init; }
}
