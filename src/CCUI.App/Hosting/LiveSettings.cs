using System.Windows;
using System.Windows.Threading;
using CCUI.App.Input;
using CCUI.App.Views;
using CCUI.Core.Settings;
using CCUI.Core.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCUI.App.Hosting;

/// <summary>
/// Applies the look, the backdrop and the key bindings again when the configuration reloads, which happens when the
/// settings page saves or appsettings.user.json is edited. Each changed section raises its own notification; they
/// are folded into one pass on the UI thread.
/// </summary>
public sealed class LiveSettings(
    IOptionsMonitor<TerminalOptions> terminal,
    IOptionsMonitor<AppearanceOptions> appearance,
    IOptionsMonitor<KeyBindingOptions> keyBindings,
    TerminalAppearance look,
    ShellViewModel shell,
    ILogger<LiveSettings> logger) : IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];
    private int _pending;

    public void Start()
    {
        Subscribe(terminal.OnChange((_, _) => Schedule()));
        Subscribe(appearance.OnChange((_, _) => Schedule()));
        Subscribe(keyBindings.OnChange((_, _) => Schedule()));
    }

    /// <summary>Pushes the current options into the running app.</summary>
    public void Apply()
    {
        Interlocked.Exchange(ref _pending, 0);
        if (Application.Current is not { } app)
        {
            return;
        }

        try
        {
            look.Apply(terminal.CurrentValue);
            ThemeResources.Apply(app, appearance.CurrentValue, look);
            KeyboardShortcuts.Reload(keyBindings.CurrentValue, shell, logger);
            foreach (Window window in app.Windows)
            {
                if (window is MainWindow main)
                {
                    main.ApplyBackdrop(appearance.CurrentValue.Backdrop);
                }
            }

            logger.LogInformation("Settings applied");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply the changed settings");
        }
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private void Subscribe(IDisposable? subscription)
    {
        if (subscription is not null)
        {
            _subscriptions.Add(subscription);
        }
    }

    private void Schedule()
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1 || Application.Current is not { Dispatcher: var dispatcher })
        {
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Apply));
    }
}
