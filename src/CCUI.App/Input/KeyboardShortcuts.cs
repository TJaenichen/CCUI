using System.Windows;
using System.Windows.Input;
using CCUI.App.Views;
using CCUI.Core.Layout;
using CCUI.Core.Settings;
using CCUI.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCUI.App.Input;

/// <summary>
/// App-wide shortcuts from the KeyBindings settings. They are checked on PreviewKeyDown of every window, before the
/// terminal sees the key, so they work from inside a session and from floating windows.
/// </summary>
public static class KeyboardShortcuts
{
    public static void Register(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<KeyBindingOptions>>().Value;
        var shell = services.GetRequiredService<ShellViewModel>();
        var logger = services.GetRequiredService<ILogger<App>>();
        var bindings = new List<(KeyChord Chord, Action Action)>();

        void Add(string gesture, Action action)
        {
            if (KeyChord.TryParse(gesture, out var chord))
            {
                bindings.Add((chord, action));
            }
            else if (!string.IsNullOrWhiteSpace(gesture))
            {
                logger.LogWarning("Ignoring key binding '{Gesture}': not a valid key chord", gesture);
            }
        }

        static MainWindow? Main() => Application.Current.MainWindow as MainWindow;

        Add(options.FocusLeft, () => PaneNavigator.MoveFocus(NavigationDirection.Left));
        Add(options.FocusRight, () => PaneNavigator.MoveFocus(NavigationDirection.Right));
        Add(options.FocusUp, () => PaneNavigator.MoveFocus(NavigationDirection.Up));
        Add(options.FocusDown, () => PaneNavigator.MoveFocus(NavigationDirection.Down));
        Add(options.ToggleDetail, () => shell.ToggleDetailCommand.Execute(null));
        Add(options.NewSession, () => shell.NewSessionCommand.Execute(null));
        Add(options.CloseSession, () => shell.CloseActivePaneCommand.Execute(null));
        Add(options.ToggleSessionList, () => shell.ToggleSessionListCommand.Execute(null));
        Add(options.FocusSessionList, () => Main()?.FocusSessionList());
        Add(options.TileLayout, () => Main()?.Tile());

        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            foreach (var (chord, action) in bindings)
            {
                if (chord.Matches(e))
                {
                    action();
                    e.Handled = true;
                    return;
                }
            }
        }));
    }
}
