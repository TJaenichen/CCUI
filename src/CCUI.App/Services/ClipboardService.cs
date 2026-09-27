using System.Runtime.InteropServices;
using System.Windows;
using CCUI.Core.ViewModels;

namespace CCUI.App.Services;

public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (COMException)
        {
            // Another process holds the clipboard.
        }
    }
}
