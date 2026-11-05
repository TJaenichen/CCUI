using System.IO;
using System.Windows;
using CCUI.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CCUI.App.Plugins;

/// <summary>One plugin's view of the app: its own logger, settings section and data folder, and the shared hub.</summary>
internal sealed class PluginHost(string name, PluginHub hub, IConfiguration configuration, TimeProvider time, ILoggerFactory loggers, string dataRoot) : IPluginHost
{
    public ILogger Logger { get; } = loggers.CreateLogger("Plugin." + name);

    public IConfigurationSection Settings { get; } = configuration.GetSection("PluginSettings:" + name);

    public TimeProvider Time => time;

    public string DataDirectory
    {
        get
        {
            var invalid = Path.GetInvalidFileNameChars();
            var folder = Path.Combine(dataRoot, string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public IReadOnlyList<PluginSession> Sessions => hub.Sessions;

    public event EventHandler<PluginSessionEventArgs>? SessionOpened
    {
        add => hub.SessionOpened += value;
        remove => hub.SessionOpened -= value;
    }

    public event EventHandler<PluginSessionEventArgs>? SessionChanged
    {
        add => hub.SessionChanged += value;
        remove => hub.SessionChanged -= value;
    }

    public event EventHandler<PluginSessionEventArgs>? SessionClosed
    {
        add => hub.SessionClosed += value;
        remove => hub.SessionClosed -= value;
    }

    public event EventHandler<ClaudeEventArgs>? ClaudeEvent
    {
        add => hub.ClaudeEvent += value;
        remove => hub.ClaudeEvent -= value;
    }

    public void AddSidebarPanel(SidebarPanel panel)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => hub.AddPanel(panel));
        }
        else
        {
            hub.AddPanel(panel);
        }
    }
}
