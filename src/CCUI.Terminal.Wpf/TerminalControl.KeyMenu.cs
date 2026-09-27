using System.Windows;
using System.Windows.Interop;

namespace CCUI.Terminal.Wpf;

public partial class TerminalControl
{
    private const int WmSysCommand = 0x0112;
    private const int ScKeyMenu = 0xF100;

    private HwndSource? _hookedSource;

    /// <summary>
    /// Tapping Alt alone makes Windows enter its window-menu mode, which then swallows the next key. Terminals
    /// use Alt as a modifier, so while a terminal has focus that keyboard-initiated menu request is dropped.
    /// </summary>
    private void AddKeyMenuHook()
    {
        RemoveKeyMenuHook();
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            _hookedSource = source;
            source.AddHook(KeyMenuHook);
        }
    }

    private void RemoveKeyMenuHook()
    {
        _hookedSource?.RemoveHook(KeyMenuHook);
        _hookedSource = null;
    }

    private IntPtr KeyMenuHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScKeyMenu && lParam == IntPtr.Zero && IsKeyboardFocusWithin)
        {
            handled = true;
        }

        return IntPtr.Zero;
    }
}
