namespace CCUI.Terminal;

public enum MouseTrackingMode
{
    None,
    X10,
    Normal,
    ButtonEvent,
    AnyEvent,
}

public enum CursorShape
{
    Block,
    Underline,
    Bar,
}

/// <summary>A cursor shape an application requested with DECSCUSR.</summary>
public readonly record struct CursorStyle(CursorShape Shape, bool Blinking);

public enum ProgressState
{
    None = 0,
    Normal = 1,
    Error = 2,
    Indeterminate = 3,
    Paused = 4,
}

/// <summary>A progress indication set with OSC 9;4 (the ConEmu / Windows Terminal taskbar progress sequence).</summary>
public readonly record struct TerminalProgress(ProgressState State, int Value);

/// <summary>Terminal modes an application can set. Read them under <see cref="TerminalEmulator.SyncRoot"/>.</summary>
public sealed class TerminalModes
{
    public bool ApplicationCursorKeys { get; internal set; }

    public bool ApplicationKeypad { get; internal set; }

    public bool AutoWrap { get; internal set; } = true;

    public bool OriginMode { get; internal set; }

    public bool InsertMode { get; internal set; }

    public bool LineFeedNewLine { get; internal set; }

    public bool CursorVisible { get; internal set; } = true;

    public bool CursorBlinking { get; internal set; }

    public bool ReverseVideo { get; internal set; }

    public bool BracketedPaste { get; internal set; }

    public bool FocusEvents { get; internal set; }

    /// <summary>DEC mode 2026: the application is mid-frame; renderers should hold the previous frame.</summary>
    public bool SynchronizedOutput { get; internal set; }

    public MouseTrackingMode MouseTracking { get; internal set; }

    public bool SgrMouse { get; internal set; }

    public bool AlternateScroll { get; internal set; }

    public bool Win32InputMode { get; internal set; }

    internal void Reset()
    {
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        AutoWrap = true;
        OriginMode = false;
        InsertMode = false;
        LineFeedNewLine = false;
        CursorVisible = true;
        CursorBlinking = false;
        ReverseVideo = false;
        BracketedPaste = false;
        FocusEvents = false;
        SynchronizedOutput = false;
        MouseTracking = MouseTrackingMode.None;
        SgrMouse = false;
        AlternateScroll = false;
        Win32InputMode = false;
    }
}
