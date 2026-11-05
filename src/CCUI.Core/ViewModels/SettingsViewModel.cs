using System.Text.Json;
using CCUI.Core.Settings;
using CCUI.Terminal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCUI.Core.ViewModels;

/// <summary>
/// The settings page. Each section is edited as a plain options object bound by the view; Save writes what differs
/// from the shipped defaults into the user file (see <see cref="UserSettingsFile"/>) and the host reloads it, so
/// the look and the key bindings change right away while the rest applies to new sessions or after a restart.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const string SkipPermissionsFlag = "--dangerously-skip-permissions";

    private static readonly string[] LineBreaks = ["\r\n", "\n"];
    private readonly ISettingsSource _source;
    private readonly IDialogService _dialogs;

    public SettingsViewModel(ISettingsSource source, IDialogService dialogs)
    {
        _source = source;
        _dialogs = dialogs;
        Load(defaults: false);
        StatusText = "Ctrl+S saves. The look and the key bindings apply right away, Claude Code settings to the next session, the rest after a restart.";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorSchemes))]
    public partial TerminalOptions Terminal { get; set; } = new();

    [ObservableProperty]
    public partial AppearanceOptions Appearance { get; set; } = new();

    [ObservableProperty]
    public partial ClaudeOptions Claude { get; set; } = new();

    [ObservableProperty]
    public partial SessionListOptions SessionList { get; set; } = new();

    [ObservableProperty]
    public partial KeyBindingOptions KeyBindings { get; set; } = new();

    [ObservableProperty]
    public partial WorkspaceOptions Workspace { get; set; } = new();

    [ObservableProperty]
    public partial UpdateOptions Update { get; set; } = new();

    /// <summary>Extra Claude arguments, one per line, without <see cref="SkipPermissionsFlag"/>.</summary>
    [ObservableProperty]
    public partial string ArgumentsText { get; set; } = string.Empty;

    /// <summary>Starts every session with <see cref="SkipPermissionsFlag"/>.</summary>
    [ObservableProperty]
    public partial bool SkipPermissions { get; set; }

    /// <summary>Environment variables removed from every session, one per line.</summary>
    [ObservableProperty]
    public partial string RemoveVariablesText { get; set; } = string.Empty;

    /// <summary>Environment variables set in every session, one NAME=value per line.</summary>
    [ObservableProperty]
    public partial string SetVariablesText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    public string UserFilePath => _source.UserFilePath;

    public IReadOnlyList<string> FontFamilies => _source.FontFamilies;

    /// <summary>The user's own schemes first, then the built-in ones.</summary>
    public IReadOnlyList<string> ColorSchemes =>
        [.. Terminal.Schemes.Select(s => s.Name).Concat(ColorScheme.BuiltIn.Select(s => s.Name)).Distinct(StringComparer.OrdinalIgnoreCase)];

    public static IReadOnlyList<string> FontWeights { get; } = ["Light", "Normal", "Medium", "SemiBold", "Bold"];

    public static IReadOnlyList<string> CursorShapes { get; } = ["Bar", "Underline", "Block"];

    public static IReadOnlyList<string> IntenseTextStyles { get; } = ["All", "Bold", "Bright", "None"];

    public static IReadOnlyList<string> StretchModes { get; } = ["None", "Fill", "Uniform", "UniformToFill"];

    public static IReadOnlyList<string> Alignments { get; } = ["Center", "Left", "Right", "Top", "Bottom", "TopLeft", "TopRight", "BottomLeft", "BottomRight"];

    public static IReadOnlyList<string> Backdrops { get; } = ["Acrylic", "Mica", "MicaAlt", "None"];

    public static IReadOnlyList<string> AnimationModes { get; } = ["On", "Off", "System"];

    public static IReadOnlyList<string> PseudoConsoles { get; } = ["Auto", "Bundled", "Inbox"];

    /// <summary>Raised after the user file has been written.</summary>
    public event EventHandler? Saved;

    [RelayCommand]
    private void Save()
    {
        Normalize();
        try
        {
            var file = _source.ReadUserFile();
            UserSettingsFile.Merge(file, TerminalOptions.Section, _source.Defaults<TerminalOptions>(TerminalOptions.Section), Terminal);
            UserSettingsFile.Merge(file, AppearanceOptions.Section, _source.Defaults<AppearanceOptions>(AppearanceOptions.Section), Appearance);
            UserSettingsFile.Merge(file, ClaudeOptions.Section, _source.Defaults<ClaudeOptions>(ClaudeOptions.Section), Claude);
            UserSettingsFile.Merge(file, SessionListOptions.Section, _source.Defaults<SessionListOptions>(SessionListOptions.Section), SessionList);
            UserSettingsFile.Merge(file, KeyBindingOptions.Section, _source.Defaults<KeyBindingOptions>(KeyBindingOptions.Section), KeyBindings);
            UserSettingsFile.Merge(file, WorkspaceOptions.Section, _source.Defaults<WorkspaceOptions>(WorkspaceOptions.Section), Workspace);
            UserSettingsFile.Merge(file, UpdateOptions.Section, _source.Defaults<UpdateOptions>(UpdateOptions.Section), Update);
            _source.WriteUserFile(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _dialogs.ShowError("Settings", "Could not write " + _source.UserFilePath + ": " + ex.Message);
            return;
        }

        StatusText = "Saved. The look and the key bindings apply now; Claude Code settings apply to new sessions, the rest after a restart.";
        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Loads the shipped defaults into the page; nothing changes until Save.</summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        Load(defaults: true);
        StatusText = "Defaults loaded. Save to keep them, or Discard to go back.";
    }

    [RelayCommand]
    private void Discard()
    {
        Load(defaults: false);
        StatusText = "Changes discarded.";
    }

    [RelayCommand]
    private void OpenFile() => _source.OpenUserFile();

    [RelayCommand]
    private void BrowseBackgroundImage()
    {
        var file = _dialogs.PickFile("Background image", "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*", Terminal.BackgroundImage);
        if (file is not null)
        {
            Terminal.BackgroundImage = file;
            OnPropertyChanged(nameof(Terminal));
        }
    }

    [RelayCommand]
    private void ClearBackgroundImage()
    {
        Terminal.BackgroundImage = string.Empty;
        OnPropertyChanged(nameof(Terminal));
    }

    private void Load(bool defaults)
    {
        Terminal = Get<TerminalOptions>(TerminalOptions.Section, defaults);
        Appearance = Get<AppearanceOptions>(AppearanceOptions.Section, defaults);
        Claude = Get<ClaudeOptions>(ClaudeOptions.Section, defaults);
        SessionList = Get<SessionListOptions>(SessionListOptions.Section, defaults);
        KeyBindings = Get<KeyBindingOptions>(KeyBindingOptions.Section, defaults);
        Workspace = Get<WorkspaceOptions>(WorkspaceOptions.Section, defaults);
        Update = Get<UpdateOptions>(UpdateOptions.Section, defaults);

        SkipPermissions = Claude.Arguments.Contains(SkipPermissionsFlag, StringComparer.OrdinalIgnoreCase);
        ArgumentsText = string.Join(Environment.NewLine, Claude.Arguments.Where(a => !a.Equals(SkipPermissionsFlag, StringComparison.OrdinalIgnoreCase)));
        RemoveVariablesText = string.Join(Environment.NewLine, Claude.RemoveEnvironmentVariables);
        SetVariablesText = string.Join(Environment.NewLine, Claude.SetEnvironmentVariables.Select(p => p.Key + "=" + p.Value));
    }

    private T Get<T>(string section, bool defaults)
        where T : class, new() =>
        defaults ? _source.Defaults<T>(section) : _source.Current<T>(section);

    /// <summary>Folds the text fields back into the options and keeps numbers in their working ranges.</summary>
    private void Normalize()
    {
        Claude.Arguments = [.. Lines(ArgumentsText).Where(a => !a.Equals(SkipPermissionsFlag, StringComparison.OrdinalIgnoreCase))];
        if (SkipPermissions)
        {
            Claude.Arguments.Add(SkipPermissionsFlag);
        }

        Claude.RemoveEnvironmentVariables = [.. Lines(RemoveVariablesText)];
        Claude.SetEnvironmentVariables = Lines(SetVariablesText)
            .Select(line => line.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts[0].Length > 0)
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Length > 1 ? g.Last()[1] : string.Empty, StringComparer.OrdinalIgnoreCase);

        Terminal.FontSize = Math.Clamp(Terminal.FontSize, 4, 96);
        Terminal.LineHeight = Math.Clamp(Terminal.LineHeight, 0.5, 3);
        Terminal.Opacity = Math.Clamp(Terminal.Opacity, 0, 100);
        Terminal.BackgroundImageOpacity = Math.Clamp(Terminal.BackgroundImageOpacity, 0, 1);
        Terminal.ScrollbackLines = Math.Max(0, Terminal.ScrollbackLines);
        Appearance.FocusBorderThickness = Math.Max(0, Appearance.FocusBorderThickness);
        Update.CheckHours = Math.Max(0, Update.CheckHours);
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split(LineBreaks, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
