using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using CCUI.Terminal.Input;

namespace CCUI.Terminal.Wpf;

public partial class TerminalControl
{
    /// <summary>Copies the selection to the clipboard; returns false when nothing is selected.</summary>
    public bool CopySelection()
    {
        var text = SelectedText();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch (COMException)
        {
            // The clipboard is held by another process; the copy is lost, as in other terminals.
            return false;
        }

        return true;
    }

    public void PasteFromClipboard()
    {
        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch (COMException)
        {
            return;
        }

        if (text.Length > 0 && Session is { } session)
        {
            SendInput(KeyEncoder.EncodePaste(text, session.Emulator.Modes.BracketedPaste));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Session is not { } session)
        {
            return;
        }

        // Keys consumed by an input method (CJK composition) are not for the process; the committed text
        // arrives through OnTextInput.
        if (e.Key == Key.ImeProcessed)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (key is Key.DeadCharProcessed or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        e.Handled = HandleKey(session, key, modifiers);
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || Session is null)
        {
            return;
        }

        // Alt+key arrives as "system text" (WM_SYSCHAR); AltGr combinations arrive as normal text.
        var alt = string.IsNullOrEmpty(e.Text) && !string.IsNullOrEmpty(e.SystemText);
        var text = alt ? e.SystemText : e.Text;

        // Control characters were already sent by OnKeyDown.
        var printable = new string(text.Where(c => c >= 0x20 && c != 0x7F).ToArray());
        if (printable.Length == 0)
        {
            return;
        }

        SendInput(KeyEncoder.EncodeText(printable, alt));
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (Session is { } session && session.Emulator.Modes.FocusEvents)
        {
            session.SendText(KeyEncoder.EncodeFocus(focused: true));
        }

        UpdateBlinkTimer();
        RenderOverlay();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (Session is { } session && session.Emulator.Modes.FocusEvents)
        {
            session.SendText(KeyEncoder.EncodeFocus(focused: false));
        }

        UpdateBlinkTimer();
        RenderOverlay();
    }

    private static TerminalKey? ToTerminalKey(Key key) => key switch
    {
        Key.Enter => TerminalKey.Enter,
        Key.Tab => TerminalKey.Tab,
        Key.Back => TerminalKey.Backspace,
        Key.Escape => TerminalKey.Escape,
        Key.Up => TerminalKey.Up,
        Key.Down => TerminalKey.Down,
        Key.Left => TerminalKey.Left,
        Key.Right => TerminalKey.Right,
        Key.Home => TerminalKey.Home,
        Key.End => TerminalKey.End,
        Key.Insert => TerminalKey.Insert,
        Key.Delete => TerminalKey.Delete,
        Key.PageUp => TerminalKey.PageUp,
        Key.PageDown => TerminalKey.PageDown,
        >= Key.F1 and <= Key.F12 => TerminalKey.F1 + (key - Key.F1),
        _ => null,
    };

    private static KeyModifiers ToKeyModifiers(ModifierKeys modifiers) =>
        (modifiers.HasFlag(ModifierKeys.Shift) ? KeyModifiers.Shift : KeyModifiers.None)
        | (modifiers.HasFlag(ModifierKeys.Alt) ? KeyModifiers.Alt : KeyModifiers.None)
        | (modifiers.HasFlag(ModifierKeys.Control) ? KeyModifiers.Control : KeyModifiers.None);

    private bool HandleKey(TerminalSession session, Key key, ModifierKeys modifiers)
    {
        if (KeyBindings?.FirstOrDefault(b => b.Matches(key, modifiers)) is { } binding)
        {
            SendInput(binding.Text);
            return true;
        }

        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        var alt = modifiers.HasFlag(ModifierKeys.Alt);

        // Clipboard: Ctrl+C copies only when something is selected (otherwise it is ^C for the application).
        if (key == Key.C && ctrl && !alt && (shift || HasSelection))
        {
            CopySelection();
            ClearSelection();
            return true;
        }

        if (key == Key.V && ctrl && !alt)
        {
            PasteFromClipboard();
            return true;
        }

        if (shift && !ctrl && !alt)
        {
            switch (key)
            {
                case Key.PageUp:
                    ScrollLines(Math.Max(1, _frame.Rows - 1));
                    return true;
                case Key.PageDown:
                    ScrollLines(-Math.Max(1, _frame.Rows - 1));
                    return true;
            }
        }

        if (ToTerminalKey(key) is { } terminalKey
            && KeyEncoder.Encode(terminalKey, ToKeyModifiers(modifiers), session.Emulator.Modes.ApplicationCursorKeys) is { } sequence)
        {
            SendInput(sequence);
            return true;
        }

        // Ctrl+letter etc. AltGr reports as Ctrl+Alt with the right Alt key down; that is text, not a control code.
        var altGr = ctrl && alt && Keyboard.IsKeyDown(Key.RightAlt);
        if (ctrl && !altGr)
        {
            char? c = key switch
            {
                >= Key.A and <= Key.Z => (char)('a' + (key - Key.A)),
                Key.Space => ' ',
                _ => null,
            };

            if (c is { } letter && KeyEncoder.EncodeControl(letter, alt) is { } control)
            {
                SendInput(control);
                return true;
            }
        }

        return false;
    }

    private void SendInput(string text)
    {
        if (Session is not { } session)
        {
            return;
        }

        session.SendText(text);
        ClearSelection();
        ScrollToBottom();
        _blinkOn = true;
        UpdateBlinkTimer();
    }
}
