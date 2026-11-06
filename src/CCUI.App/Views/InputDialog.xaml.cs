using System.Windows;
using CCUI.App.Services;

namespace CCUI.App.Views;

/// <summary>One line of text, in the app's look: folder names and the like.</summary>
public partial class InputDialog : Window
{
    public InputDialog(string title, string label, string? initial)
    {
        InitializeComponent();
        Title = title;
        Caption.Text = title;
        Label.Text = label;
        Input.Text = initial ?? string.Empty;
        SourceInitialized += (_, _) => WindowBackdrop.TryApply(this, "None");
        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    /// <summary>The text entered, or null when cancelled.</summary>
    public static string? Show(Window? owner, string title, string label, string? initial)
    {
        var dialog = new InputDialog(title, label, initial) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Input.Text : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
