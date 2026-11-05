using System.Windows;
using System.Windows.Controls;
using CCUI.App.Hosting;
using CCUI.App.Views;
using CCUI.Core.Settings;
using CCUI.Terminal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CCUI.App.Tests;

[Collection("Wpf")]
public sealed class SettingsWindowTests(WpfFixture wpf)
{
    /// <summary>Opens the real settings window from the demo host and renders every section: catches XAML, style and binding errors.</summary>
    [Fact(Timeout = 60_000)]
    public Task SettingsWindowRendersEverySection()
    {
        var cancel = TestContext.Current.CancellationToken;
        return wpf.Run(async () =>
        {
            using var host = AppHost.Build(["--Demo:Enabled=true", "--Workspace:RestoreOnStartup=false", "--Appearance:Backdrop=None"]);
            var window = host.Services.GetRequiredService<SettingsWindow>();
            window.Show();
            await WpfFixture.Settle(500, cancel);

            var tabs = window.Tabs;
            Assert.Equal(7, tabs.Items.Count);
            for (var i = 0; i < tabs.Items.Count; i++)
            {
                tabs.SelectedIndex = i;
                await WpfFixture.Settle(150, cancel);
                var header = ((TabItem)tabs.Items[i]).Header?.ToString() ?? i.ToString();
                var snapshot = WpfFixture.Render((FrameworkElement)window.Content, 920, 640, "settings-" + header.ToLowerInvariant().Replace(' ', '-'));
                Assert.True(snapshot.CountDistinctColours() > 30, header);
            }

            Assert.Equal(window.ViewModel.UserFilePath, AppHost.UserSettingsPath);
            window.Close();
        });
    }

    /// <summary>The path a save takes: the configuration reloads, the options monitors fire, and the one TerminalAppearance every pane binds to changes.</summary>
    [Fact(Timeout = 30_000)]
    public Task AReloadedConfigurationReachesTheTerminalAppearance()
    {
        var cancel = TestContext.Current.CancellationToken;
        return wpf.Run(async () =>
        {
            var configuration = new ConfigurationBuilder()
                .Add(new MemoryConfigurationSource { InitialData = new Dictionary<string, string?> { ["Terminal:FontSize"] = "12" } })
                .Build();
            var memory = configuration.Providers.OfType<MemoryConfigurationProvider>().Single();
            using var services = new ServiceCollection()
                .AddLogging()
                .Configure<TerminalOptions>(configuration.GetSection(TerminalOptions.Section))
                .Configure<AppearanceOptions>(configuration.GetSection(AppearanceOptions.Section))
                .Configure<KeyBindingOptions>(configuration.GetSection(KeyBindingOptions.Section))
                .Configure<SessionListOptions>(configuration.GetSection(SessionListOptions.Section))
                .AddSingleton<TerminalAppearance>()
                .AddSingleton(TestShell.Create())
                .AddSingleton<LiveSettings>()
                .BuildServiceProvider();
            using var live = services.GetRequiredService<LiveSettings>();
            live.Start();
            var look = services.GetRequiredService<TerminalAppearance>();
            Assert.Equal(16, look.FontSize);

            var shell = services.GetRequiredService<CCUI.Core.ViewModels.ShellViewModel>();
            Assert.False(shell.SessionList.OpenOnClick);

            memory.Set("Terminal:FontSize", "15");
            memory.Set("Terminal:ColorScheme", "Tango Dark");
            memory.Set("SessionList:OpenOnClick", "true");
            configuration.Reload();
            await WpfFixture.Settle(300, cancel);

            Assert.Equal(20, look.FontSize);
            Assert.Equal("Tango Dark", look.ColorScheme.Name);
            Assert.True(shell.SessionList.OpenOnClick);
        });
    }

    [Fact]
    public Task TerminalAppearanceFollowsNewOptions() => wpf.Run(() =>
    {
        var look = new TerminalAppearance(Options.Create(new TerminalOptions { FontSize = 12, ColorScheme = "Campbell" }));
        var changed = new List<string?>();
        look.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        look.Apply(new TerminalOptions { FontSize = 15, ColorScheme = "One Half Dark", CursorShape = "Block", Opacity = 100 });

        Assert.Equal(20, look.FontSize); // 15pt is 20px
        Assert.Equal("One Half Dark", look.ColorScheme.Name);
        Assert.Equal(CursorShape.Block, look.CursorShape);
        Assert.Contains(nameof(TerminalAppearance.FontSize), changed);
        Assert.Contains(nameof(TerminalAppearance.ColorScheme), changed);
        Assert.Contains(nameof(TerminalAppearance.Background), changed);
        return Task.CompletedTask;
    });
}
