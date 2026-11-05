using System.Text.Json;
using System.Text.Json.Nodes;

namespace CCUI.Core.Settings;

/// <summary>What the settings page needs from the host: each section as it is in effect and as shipped, and the
/// personal override file.</summary>
public interface ISettingsSource
{
    /// <summary>The personal override file (appsettings.user.json), read after the shipped files.</summary>
    string UserFilePath { get; }

    /// <summary>A section as it is in effect now, every configuration source applied.</summary>
    T Current<T>(string section) where T : class, new();

    /// <summary>A section without the user file: the values the app ships with.</summary>
    T Defaults<T>(string section) where T : class, new();

    JsonObject ReadUserFile();

    /// <summary>Writes the user file and reloads the configuration, so the running app picks the values up.</summary>
    void WriteUserFile(JsonObject file);

    /// <summary>Opens the user file in the editor registered for .json files.</summary>
    void OpenUserFile();

    /// <summary>Installed font family names, for the font picker.</summary>
    IReadOnlyList<string> FontFamilies { get; }
}

/// <summary>
/// Keeps appsettings.user.json minimal: a section holds only the values that differ from the shipped defaults, so a
/// later change of a default still reaches the user, and keys the page does not know about are left alone.
/// </summary>
public static class UserSettingsFile
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.General) { WriteIndented = true };

    /// <summary>
    /// Writes the values of <paramref name="edited"/> that differ from <paramref name="defaults"/> into the section
    /// and removes the ones that match, so the file says only what the user changed.
    /// </summary>
    public static void Merge<T>(JsonObject file, string section, T defaults, T edited)
        where T : class
    {
        var defaultValues = JsonSerializer.SerializeToNode(defaults, Json)!.AsObject();
        var editedValues = JsonSerializer.SerializeToNode(edited, Json)!.AsObject();
        if (file[section] is not JsonObject target)
        {
            target = new JsonObject();
            file[section] = target;
        }

        foreach (var (name, value) in editedValues.ToList())
        {
            if (JsonNode.DeepEquals(value, defaultValues[name]))
            {
                target.Remove(name);
            }
            else
            {
                target[name] = value?.DeepClone();
            }
        }

        if (target.Count == 0)
        {
            file.Remove(section);
        }
    }
}
