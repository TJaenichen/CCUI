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
    private static readonly List<(KeyChord Chord, Action Action)> Bindings = [];
    private static bool _handlerRegistered;

    public static void Register(IServiceProvider services)
    {
        Reload(
            services.GetRequiredService<IOptions<KeyBindingOptions>>().Value,
            services.GetRequiredService<ShellViewModel>(),
            services.GetRequiredService<ILogger<App>>());

        if (_handlerRegistered)
        {
            return;
        }

        _handlerRegistered = true;
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            foreach (var (chord, action) in Bindings)
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

    /// <summary>Replaces the bindings, e.g. after the settings changed.</summary>
    public static void Reload(KeyBindingOptions options, ShellViewModel shell, ILogger logger)
    {
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
        Add(options.OpenSettings, () => Main()?.ShowSettings());

        Bindings.Clear();
        Bindings.AddRange(bindings);
    }
}
