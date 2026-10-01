using CCUI.Terminal;

namespace CCUI.Core.Settings;

/// <summary>How sessions are started. Section "Claude" in appsettings.json.</summary>
public sealed class ClaudeOptions
{
    public const string Section = "Claude";

    /// <summary>The command to run; resolved through PATH/PATHEXT (claude.exe, or claude.cmd from npm).</summary>
    public string Command { get; set; } = "claude";

    /// <summary>Extra arguments for every session, e.g. ["--model", "opus"].</summary>
    public List<string> Arguments { get; set; } = [];

    /// <summary>Claude Code's data folder; empty means CLAUDE_CONFIG_DIR or ~/.claude.</summary>
    public string? ConfigDirectory { get; set; }

    /// <summary>
    /// Start sessions with a fresh logon environment (as a Start-menu launch gets) instead of inheriting this
    /// process's environment. Keeps NO_COLOR and friends from a parent shell out of new sessions.
    /// </summary>
    public bool FreshEnvironment { get; set; } = true;

    // List and dictionary defaults live in appsettings.json: the configuration binder appends to (rather than
    // replaces) collections that already have items.

    /// <summary>Variables always removed from a session's environment (e.g. NO_COLOR inherited from a tool shell).</summary>
    public List<string> RemoveEnvironmentVariables { get; set; } = [];

    /// <summary>Variables set in every session's environment.</summary>
    public Dictionary<string, string> SetEnvironmentVariables { get; set; } = [];

    /// <summary>
    /// Registers a SessionStart/Notification/Stop hook (via --settings) that reports back to CCUI, so a pane
    /// follows its session through /clear and knows when Claude waits for input.
    /// </summary>
    public bool UseSessionHooks { get; set; } = true;

    /// <summary>Which pseudo console to use: Auto (bundled if present), Bundled, or Inbox.</summary>
    public string PseudoConsole { get; set; } = "Auto";

    /// <summary>The context window used for the "context used" percentage.</summary>
    public long ContextWindowTokens { get; set; } = 200_000;

    /// <summary>How often live transcripts are polled.</summary>
    public int TranscriptPollMilliseconds { get; set; } = 400;
}

/// <summary>Section "SessionList".</summary>
public sealed class SessionListOptions
{
    public const string Section = "SessionList";

    /// <summary>Sessions older than this are not listed.</summary>
    public int MaxAgeDays { get; set; } = 14;

    public int RefreshSeconds { get; set; } = 5;

    /// <summary>How far before the last boot to look for sessions the reboot killed.</summary>
    public int RebootLookBackHours { get; set; } = 24;
}

/// <summary>Terminal look and behaviour, mirroring Windows Terminal profile settings. Section "Terminal".</summary>
public sealed class TerminalOptions
{
    public const string Section = "Terminal";

    public string FontFamily { get; set; } = "Cascadia Mono";

    /// <summary>In points, like Windows Terminal (12pt = 16px).</summary>
    public double FontSize { get; set; } = 12;

    public string FontWeight { get; set; } = "Normal";

    public string FallbackFontFamilies { get; set; } = "Cascadia Mono, Segoe UI Symbol, Segoe UI Emoji, Segoe UI, MS Gothic, Consolas";

    /// <summary>Row height as a multiple of the font's line height.</summary>
    public double LineHeight { get; set; } = 1.0;

    /// <summary>A name from <see cref="Schemes"/> or a built-in scheme (Campbell, Campbell Powershell, One Half Dark, Tango Dark).</summary>
    public string ColorScheme { get; set; } = "Campbell";

    /// <summary>Extra schemes in Windows Terminal's format; paste them from its settings.json.</summary>
    public List<ColorScheme> Schemes { get; set; } = [];

    /// <summary>Bar, Underline or Block.</summary>
    public string CursorShape { get; set; } = "Bar";

    public bool CursorBlink { get; set; } = true;

    /// <summary>All, Bold, Bright or None.</summary>
    public string IntenseTextStyle { get; set; } = "All";

    public bool UseBuiltinGlyphs { get; set; } = true;

    public bool UseColorEmoji { get; set; } = true;

    public int ScrollbackLines { get; set; } = 10_000;

    public bool CopyOnSelect { get; set; }

    /// <summary>Background opacity in percent (0-100): how much of the window's backdrop shows through.</summary>
    public double Opacity { get; set; } = 80;

    /// <summary>Image path (environment variables allowed). Empty for none.</summary>
    public string? BackgroundImage { get; set; }

    public double BackgroundImageOpacity { get; set; } = 0.25;

    /// <summary>None, Fill, Uniform or UniformToFill.</summary>
    public string BackgroundImageStretchMode { get; set; } = "UniformToFill";

    /// <summary>Center, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft or BottomRight.</summary>
    public string BackgroundImageAlignment { get; set; } = "Center";

    public string Padding { get; set; } = "8,4";

    /// <summary>Size used before a pane has been laid out.</summary>
    public int DefaultColumns { get; set; } = 120;

    public int DefaultRows { get; set; } = 30;

    /// <summary>Key chords that send fixed text; appsettings.json maps Shift+Enter to a newline in Claude Code.</summary>
    public List<SendKeyBinding> SendKeys { get; set; } = [];
}

public sealed class SendKeyBinding
{
    /// <summary>A WPF key gesture such as "Shift+Enter" or "Ctrl+Alt+K".</summary>
    public string Keys { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

/// <summary>Window and chrome. Section "Appearance".</summary>
public sealed class AppearanceOptions
{
    public const string Section = "Appearance";

    /// <summary>Acrylic, Mica, MicaAlt or None (Windows 11; ignored elsewhere).</summary>
    public string Backdrop { get; set; } = "Acrylic";

    /// <summary>Tint laid over the backdrop, #AARRGGBB.</summary>
    public string WindowTint { get; set; } = "#B0101014";

    public string AccentColor { get; set; } = "#D97757";

    /// <summary>Border colour of the focused pane; empty uses the accent colour.</summary>
    public string? FocusBorderColor { get; set; }

    public double FocusBorderThickness { get; set; } = 2;

    /// <summary>Half-life of the activity meters' decay.</summary>
    public int MeterDecayMilliseconds { get; set; } = 400;

    /// <summary>How long the meters' peak marker holds before it falls.</summary>
    public int MeterPeakHoldMilliseconds { get; set; } = 900;

    /// <summary>
    /// Decorative animation (the working spinner): On, Off, or System to follow Windows' "Show animations". System
    /// is not the default because Remote Desktop sessions report animations as off.
    /// </summary>
    public string Animations { get; set; } = "On";
}

/// <summary>App-level shortcuts as WPF key gestures. Section "KeyBindings".</summary>
public sealed class KeyBindingOptions
{
    public const string Section = "KeyBindings";

    public string FocusLeft { get; set; } = "Alt+Left";

    public string FocusRight { get; set; } = "Alt+Right";

    public string FocusUp { get; set; } = "Alt+Up";

    public string FocusDown { get; set; } = "Alt+Down";

    public string ToggleDetail { get; set; } = "Ctrl+Shift+D";

    public string NewSession { get; set; } = "Ctrl+Shift+T";

    public string CloseSession { get; set; } = "Ctrl+Shift+W";

    public string ToggleSessionList { get; set; } = "Ctrl+Shift+B";

    public string FocusSessionList { get; set; } = "Ctrl+Shift+E";

    /// <summary>Arranges all docked sessions in a grid.</summary>
    public string TileLayout { get; set; } = "Ctrl+Shift+G";
}

/// <summary>Saving and restoring open sessions. Section "Workspace".</summary>
public sealed class WorkspaceOptions
{
    public const string Section = "Workspace";

    public bool RestoreOnStartup { get; set; } = true;

    public int AutoSaveSeconds { get; set; } = 30;

    /// <summary>The workspace file; empty means %LOCALAPPDATA%\CCUI\workspace.json.</summary>
    public string? Path { get; set; }

    /// <summary>Pause between session launches on restore, so a dozen sessions do not start at once.</summary>
    public int LaunchStaggerMilliseconds { get; set; } = 800;
}

/// <summary>Demo mode replays scripted sessions instead of running Claude. Section "Demo".</summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";

    public bool Enabled { get; set; }

    public int Sessions { get; set; } = 4;

    /// <summary>Playback speed multiplier.</summary>
    public double Speed { get; set; } = 1.0;

    public int Seed { get; set; } = 7;
}
