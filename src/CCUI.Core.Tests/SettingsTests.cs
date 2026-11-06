using System.Text.Json;
using System.Text.Json.Nodes;
using CCUI.Core.Settings;
using CCUI.Core.ViewModels;

namespace CCUI.Core.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void MergeWritesOnlyWhatDiffersAndKeepsTheRestOfTheFile()
    {
        var file = JsonNode.Parse("""{ "Terminal": { "FontSize": 14, "Padding": "2,2" }, "Logging": { "LogLevel": { "Default": "Debug" } } }""")!.AsObject();
        var defaults = new TerminalOptions { FontSize = 10, FontFamily = "Consolas" };
        var edited = new TerminalOptions { FontSize = 10, FontFamily = "Cascadia Mono" };

        UserSettingsFile.Merge(file, TerminalOptions.Section, defaults, edited);

        var terminal = file["Terminal"]!.AsObject();
        Assert.Equal("Cascadia Mono", (string?)terminal["FontFamily"]);
        Assert.Null(terminal["FontSize"]); // back to the default, so no longer written
        Assert.Null(terminal["Padding"]); // the page's value equals the default too
        Assert.Single(terminal);
        Assert.Equal("Debug", (string?)file["Logging"]!["LogLevel"]!["Default"]);
    }

    [Fact]
    public void MergeRemovesASectionThatMatchesTheDefaults()
    {
        var file = JsonNode.Parse("""{ "Update": { "CheckHours": 4 } }""")!.AsObject();

        UserSettingsFile.Merge(file, UpdateOptions.Section, new UpdateOptions(), new UpdateOptions());

        Assert.Empty(file);
    }

    [Fact]
    public void TheSkipPermissionsToggleAndTheTextListsRoundTrip()
    {
        var source = new FakeSettingsSource();
        source.Set(ClaudeOptions.Section, new ClaudeOptions
        {
            Arguments = ["--model", "opus", "--dangerously-skip-permissions"],
            SetEnvironmentVariables = new Dictionary<string, string> { ["A"] = "1" },
        });
        var vm = new SettingsViewModel(source, new NoDialogs(), new NoPlugins());

        Assert.True(vm.SkipPermissions);
        Assert.Equal("--model" + Environment.NewLine + "opus", vm.ArgumentsText);
        Assert.Equal("A=1", vm.SetVariablesText);

        vm.SkipPermissions = false;
        vm.ArgumentsText = "--model\r\nsonnet\r\n";
        vm.SetVariablesText = "A=1\nB = two=2\n\n";
        var saved = 0;
        vm.Saved += (_, _) => saved++;
        vm.SaveCommand.Execute(null);

        Assert.Equal(1, saved);
        Assert.Equal(1, source.Writes);
        var claude = source.File["Claude"]!.AsObject();
        Assert.Equal(["--model", "sonnet"], claude["Arguments"]!.AsArray().Select(n => (string?)n));
        Assert.Equal("two=2", (string?)claude["SetEnvironmentVariables"]!["B"]);
        Assert.Null(claude["RemoveEnvironmentVariables"]); // unchanged from the defaults
        Assert.Null(source.File["Terminal"]); // untouched sections are not written at all
    }

    [Fact]
    public void SaveWritesOnlyChangedValuesAndResetGoesBackToTheShippedDefaults()
    {
        var source = new FakeSettingsSource();
        source.SetDefaults(TerminalOptions.Section, new TerminalOptions { FontSize = 10 });
        source.Set(TerminalOptions.Section, new TerminalOptions { FontSize = 14 });
        var vm = new SettingsViewModel(source, new NoDialogs(), new NoPlugins());
        Assert.Equal(14, vm.Terminal.FontSize);

        vm.Terminal.FontSize = 11;
        vm.Terminal.Opacity = 300; // clamped on save
        vm.SaveCommand.Execute(null);

        var terminal = source.File["Terminal"]!.AsObject();
        Assert.Equal(11, (double?)terminal["FontSize"]);
        Assert.Equal(100, (double?)terminal["Opacity"]);
        Assert.Equal(2, terminal.Count);

        vm.ResetToDefaultsCommand.Execute(null);
        Assert.Equal(10, vm.Terminal.FontSize);
        Assert.Equal(1, source.Writes); // nothing is written until Save

        vm.DiscardCommand.Execute(null);
        Assert.Equal(11, vm.Terminal.FontSize); // back to what is in effect, which is what was saved
    }

    [Fact]
    public void TheSchemeListHasTheUsersSchemesBeforeTheBuiltInOnes()
    {
        var source = new FakeSettingsSource();
        source.Set(TerminalOptions.Section, new TerminalOptions { Schemes = [new CCUI.Terminal.ColorScheme { Name = "Mine" }] });
        var vm = new SettingsViewModel(source, new NoDialogs(), new NoPlugins());

        Assert.Equal("Mine", vm.ColorSchemes[0]);
        Assert.Contains("Campbell", vm.ColorSchemes);
    }

    /// <summary>Sections as JSON, handed out as fresh objects each time like the configuration binder does.</summary>
    private sealed class FakeSettingsSource : ISettingsSource
    {
        private readonly Dictionary<string, JsonNode> _current = [];
        private readonly Dictionary<string, JsonNode> _defaults = [];

        public JsonObject File { get; private set; } = [];

        public int Writes { get; private set; }

        public string UserFilePath => @"C:\fake\appsettings.user.json";

        public IReadOnlyList<string> FontFamilies => ["Consolas"];

        public void Set<T>(string section, T value) => _current[section] = JsonSerializer.SerializeToNode(value)!;

        public void SetDefaults<T>(string section, T value) => _defaults[section] = JsonSerializer.SerializeToNode(value)!;

        public T Current<T>(string section)
            where T : class, new() =>
            _current.TryGetValue(section, out var node) ? node.Deserialize<T>()! : Defaults<T>(section);

        public T Defaults<T>(string section)
            where T : class, new() =>
            _defaults.TryGetValue(section, out var node) ? node.Deserialize<T>()! : new T();

        public JsonObject ReadUserFile() => File.DeepClone().AsObject();

        public void WriteUserFile(JsonObject file)
        {
            File = file;
            Writes++;
            _current.Clear();
            foreach (var (section, values) in file)
            {
                _current[section] = values!.DeepClone();
            }
        }

        public void OpenUserFile()
        {
        }
    }

    private sealed class NoDialogs : IDialogService
    {
        public string? PickFolder(string title, string? initialDirectory) => null;

        public string? PickFile(string title, string filter, string? initialPath) => null;

        public string? Prompt(string title, string label, string? initial) => null;

        public bool Open(string target) => false;

        public bool Confirm(string title, string message) => false;

        public void ShowError(string title, string message)
        {
        }
    }
}
