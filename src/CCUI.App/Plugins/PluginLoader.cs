using System.Diagnostics;
using System.IO;
using System.Reflection;
using CCUI.App.Hosting;
using CCUI.Core.Settings;
using CCUI.Core.ViewModels;
using CCUI.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCUI.App.Plugins;

/// <summary>
/// Finds and starts plugins: every folder under %LOCALAPPDATA%\CCUI\plugins (and the configured extra folders)
/// holds one plugin, a DLL named like the folder by preference, with a class implementing <see cref="ICcuiPlugin"/>.
/// A plugin that fails to load or start is listed with its error and does not affect the others.
/// </summary>
public sealed class PluginLoader(IOptions<PluginOptions> options, IConfiguration configuration, PluginHub hub, TimeProvider time, ILoggerFactory loggers) : IPluginCatalog
{
    private readonly ILogger _logger = loggers.CreateLogger<PluginLoader>();
    private readonly List<PluginInfo> _plugins = [];

    public string DirectoryPath => AppHost.PluginsDirectory;

    public IReadOnlyList<PluginInfo> Plugins => _plugins;

    public void LoadAll()
    {
        if (!options.Value.Enabled)
        {
            _logger.LogInformation("Plugins are turned off");
            return;
        }

        Load(DirectoryPath);
        foreach (var extra in options.Value.Directories)
        {
            Load(Environment.ExpandEnvironmentVariables(extra));
        }
    }

    /// <summary>Loads every plugin folder under <paramref name="root"/>.</summary>
    public void Load(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var folder in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            LoadFolder(folder);
        }
    }

    public void OpenDirectory()
    {
        Directory.CreateDirectory(DirectoryPath);
        Process.Start(new ProcessStartInfo(DirectoryPath) { UseShellExecute = true });
    }

    private void LoadFolder(string folder)
    {
        var folderName = Path.GetFileName(folder);
        var candidates = Directory.GetFiles(folder, "*.dll")
            .Where(file => !PluginLoadContext.IsSharedWithApp(Path.GetFileNameWithoutExtension(file)))
            .OrderBy(file => Path.GetFileNameWithoutExtension(file).Equals(folderName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        foreach (var file in candidates)
        {
            try
            {
                var assembly = new PluginLoadContext(file).LoadFromAssemblyPath(file);
                var types = PluginTypes(assembly);
                if (types.Count == 0)
                {
                    continue;
                }

                var version = assembly.GetName().Version?.ToString(3) ?? string.Empty;
                foreach (var type in types)
                {
                    Start(type, file, version);
                }

                return;
            }
            catch (BadImageFormatException)
            {
                // A native library or some other non-.NET file; try the next one.
            }
            catch (Exception ex) when (ex is FileLoadException or FileNotFoundException or TypeLoadException or NotSupportedException)
            {
                _logger.LogWarning(ex, "Could not load plugin {File}", file);
                _plugins.Add(new PluginInfo(folderName, string.Empty, file, ex.Message));
                return;
            }
        }

        _plugins.Add(new PluginInfo(folderName, string.Empty, folder, "No class implementing ICcuiPlugin found in its DLLs."));
    }

    private static List<Type> PluginTypes(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = [.. ex.Types.OfType<Type>()];
        }

        return [.. types.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ICcuiPlugin).IsAssignableFrom(t))];
    }

    private void Start(Type type, string file, string version)
    {
        var name = type.Name;
        try
        {
            var plugin = (ICcuiPlugin)Activator.CreateInstance(type)!;
            name = string.IsNullOrWhiteSpace(plugin.Name) ? type.Name : plugin.Name.Trim();
            plugin.Initialize(new PluginHost(name, hub, configuration, time, loggers, Path.Combine(AppHost.DataDirectory, "plugin-data")));
            _plugins.Add(new PluginInfo(name, version, file, null));
            _logger.LogInformation("Loaded plugin {Name} {Version} from {File}", name, version, file);
        }
        catch (Exception ex)
        {
            _plugins.Add(new PluginInfo(name, version, file, ex.Message));
            _logger.LogWarning(ex, "Plugin {Name} failed to start", name);
        }
    }
}
