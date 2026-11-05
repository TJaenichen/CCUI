using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CCUI.App.Services;
using CCUI.Core.Settings;
using CCUI.Core.ViewModels;
using Microsoft.Extensions.Options;

namespace CCUI.App.Views;

/// <summary>The settings page: a window owned by the main one, with the same backdrop. View logic only.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel, IOptionsMonitor<AppearanceOptions> appearance)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) =>
        {
            if (!WindowBackdrop.TryApply(this, appearance.CurrentValue.Backdrop))
            {
                Resources["WindowTintBrush"] = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1A));
            }
        };
    }

    public SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Moving focus commits the field being edited before the save reads it.
            SaveButton.Focus();
            ViewModel.SaveCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Keyboard.FocusedElement is not ComboBox { IsDropDownOpen: true })
        {
            Close();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }
}
