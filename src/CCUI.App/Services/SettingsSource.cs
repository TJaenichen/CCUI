using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using CCUI.App.Hosting;
using CCUI.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CCUI.App.Services;

/// <summary>
/// Binds sections from the live configuration (everything applied) and from the shipped files alone (the
/// defaults), and reads and writes appsettings.user.json for the settings page.
/// </summary>
public sealed class SettingsSource(IConfiguration configuration, IHostEnvironment environment, ILogger<SettingsSource> logger) : ISettingsSource
{
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly Lazy<IConfigurationRoot> _defaults = new(() => new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{environment.EnvironmentName}.json", optional: true)
        .Build());

    private readonly Lazy<IReadOnlyList<string>> _fonts = new(() =>
        Fonts.SystemFontFamilies.Select(f => f.Source).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList());

    public string UserFilePath => AppHost.UserSettingsPath;

    public IReadOnlyList<string> FontFamilies => _fonts.Value;

    public T Current<T>(string section)
        where T : class, new() =>
        configuration.GetSection(section).Get<T>() ?? new T();

    public T Defaults<T>(string section)
        where T : class, new() =>
        _defaults.Value.GetSection(section).Get<T>() ?? new T();

    public JsonObject ReadUserFile()
    {
        if (!File.Exists(UserFilePath))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(File.ReadAllText(UserFilePath), documentOptions: Lenient) as JsonObject ?? new JsonObject();
    }

    public void WriteUserFile(JsonObject file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserFilePath)!);
        File.WriteAllText(UserFilePath, file.ToJsonString(UserSettingsFile.Json) + Environment.NewLine);
        logger.LogInformation("Wrote {File}", UserFilePath);

        // Apply now rather than when the file watcher gets round to it.
        (configuration as IConfigurationRoot)?.Reload();
    }

    public void OpenUserFile()
    {
        if (!File.Exists(UserFilePath))
        {
            WriteUserFile(new JsonObject());
        }

        try
        {
            Process.Start(new ProcessStartInfo(UserFilePath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No editor for .json: show the file in Explorer instead.
            logger.LogInformation(ex, "No editor opened {File}", UserFilePath);
            Process.Start("explorer.exe", "/select,\"" + UserFilePath + "\"");
        }
    }
}
