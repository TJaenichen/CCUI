using System.Windows;
using CCUI.Core.ViewModels;
using Microsoft.Win32;

namespace CCUI.App.Services;

public sealed class DialogService : IDialogService
{
    public string? PickFolder(string title, string? initialDirectory)
    {
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = initialDirectory ?? string.Empty };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
