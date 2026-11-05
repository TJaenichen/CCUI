using System.IO;
using System.Windows.Controls;
using CCUI.App.Hosting;
using CCUI.App.Plugins;
using CCUI.App.Views;
using CCUI.Core.ViewModels;
using CCUI.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CCUI.App.Tests;

/// <summary>A plugin that works: shows a panel titled from its settings and writes every event it gets into the panel.</summary>
public sealed class GoodTestPlugin : ICcuiPlugin
{
    public string Name => "Good";

    public void Initialize(IPluginHost host)
    {
        var log = new TextBlock();
        host.AddSidebarPanel(new SidebarPanel { Title = "Good " + host.Settings["Greeting"], Content = log });
        host.SessionOpened += (_, e) => log.Text += "opened:" + e.Session.Title + ";";
        host.SessionChanged += (_, e) => log.Text += "changed:" + e.Session.Activity + ";";
        host.ClaudeEvent += (_, e) => log.Text += e.Kind + (e.ToolName is { } tool ? "(" + tool + ")" : string.Empty) + ";";
        host.Logger.LogInformation("Good plugin started with {Count} sessions", host.Sessions.Count);
    }
}

/// <summary>A plugin whose start fails; it must be reported and must not stop the others.</summary>
public sealed class BadTestPlugin : ICcuiPlugin
{
    public string Name => "Bad";

    public void Initialize(IPluginHost host) => throw new InvalidOperationException("boom");
}

[Collection("Wpf")]
public sealed class PluginTests(WpfFixture wpf)
{
    /// <summary>Copies this test assembly into a plugin folder and loads it like a real plugin: through its own load context, found by the contract interface.</summary>
    [Fact(Timeout = 60_000)]
    public Task PluginsLoadFromAFolderAndGetSessionAndClaudeEvents()
    {
        var cancel = TestContext.Current.CancellationToken;
        return wpf.Run(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ccui-plugin-test-" + Guid.NewGuid().ToString("N"));
            var folder = Path.Combine(root, "TestPlugin");
            Directory.CreateDirectory(folder);
            File.Copy(typeof(PluginTests).Assembly.Location, Path.Combine(folder, "TestPlugin.dll"));

            using var host = AppHost.Build([
                "--Demo:Enabled=true", "--Demo:Speed=20", "--Workspace:RestoreOnStartup=false", "--Appearance:Backdrop=None",
                "--PluginSettings:Good:Greeting=morning"]);
            var loader = host.Services.GetRequiredService<PluginLoader>();
            loader.Load(root);

            var good = Assert.Single(loader.Plugins, p => p.Name == "Good");
            Assert.Null(good.Error);
            var bad = Assert.Single(loader.Plugins, p => p.Name == "Bad");
            Assert.Contains("boom", bad.Error);

            var hub = host.Services.GetRequiredService<PluginHub>();
            var panel = Assert.Single(hub.BottomPanels);
            Assert.Equal("Good morning", panel.Title);

            var shell = host.Services.GetRequiredService<ShellViewModel>();
            var window = host.Services.GetRequiredService<MainWindow>();
            window.Width = 1400;
            window.Height = 900;
            window.Show();
            await WpfFixture.Settle(3000, cancel);

            var log = ((TextBlock)panel.Content).Text;
            Assert.Equal(4, hub.Sessions.Count);
            Assert.Contains("opened:", log);
            Assert.Contains("changed:Working;", log);
            Assert.Matches("Prompt;|Response;|Thinking;|ToolCall\\(|System;", log);

            window.Close();
            await shell.DisposeAsync();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The loaded DLL stays locked for the life of the process.
            }
        });
    }
}
