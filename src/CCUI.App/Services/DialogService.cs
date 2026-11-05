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

    public string? PickFile(string title, string filter, string? initialPath)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(initialPath);
            dialog.InitialDirectory = System.IO.Path.GetDirectoryName(expanded) ?? string.Empty;
            dialog.FileName = System.IO.Path.GetFileName(expanded);
        }

        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
