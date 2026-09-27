using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CCUI.App.Services;

/// <summary>
/// Windows 11 system backdrops (acrylic, mica) for a WPF window: extend the DWM frame over the client area, make
/// WPF's own background transparent and ask DWM for the backdrop. On older Windows it reports false and the caller
/// keeps an opaque background.
/// </summary>
public static partial class WindowBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;

    public static bool TryApply(Window window, string backdrop)
    {
        var type = backdrop.ToLowerInvariant() switch
        {
            "mica" => 2,
            "acrylic" => 3,
            "micaalt" => 4,
            _ => 0,
        };

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var dark = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        // The system backdrop API exists from Windows 11 22H2 (build 22621).
        if (type == 0 || Environment.OSVersion.Version.Build < 22621)
        {
            return false;
        }

        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
        {
            target.BackgroundColor = Colors.Transparent;
        }

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        return DwmExtendFrameIntoClientArea(hwnd, ref margins) == 0
            && DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int)) == 0;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }
}
