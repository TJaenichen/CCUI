using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CCUI.Core.ViewModels;

namespace CCUI.App.Views;

/// <summary>A session pane. View logic only: resizing the detail view and moving focus into the terminal.</summary>
public partial class SessionPaneView : UserControl
{
    private const double MinimumDetailHeight = 80;
    private const double MinimumTerminalHeight = 120;

    public SessionPaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private SessionPaneViewModel? ViewModel => DataContext as SessionPaneViewModel;

    public void FocusTerminal()
    {
        if (Window.GetWindow(this) is { IsActive: false } window)
        {
            window.Activate();
        }

        Terminal.Focus();
        Keyboard.Focus(Terminal);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged old)
        {
            old.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is INotifyPropertyChanged current)
        {
            current.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When the shell activates this pane (e.g. from the session list), put the keyboard in its terminal.
        if (e.PropertyName == nameof(SessionPaneViewModel.IsActive) && ViewModel is { IsActive: true } && !IsKeyboardFocusWithin)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusTerminal);
        }
    }

    private void OnDetailResize(object sender, DragDeltaEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            var maximum = Math.Max(MinimumDetailHeight, ActualHeight - MinimumTerminalHeight);
            vm.DetailHeight = Math.Clamp(vm.DetailHeight + e.VerticalChange, MinimumDetailHeight, maximum);
        }
    }
}
