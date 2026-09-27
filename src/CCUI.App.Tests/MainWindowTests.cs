using System.Windows;
using CCUI.App.Hosting;
using CCUI.App.Views;
using CCUI.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CCUI.App.Tests;

/// <summary>Starts the real window in demo mode: catches composition, XAML and docking problems end to end.</summary>
[Collection("Wpf")]
public sealed class MainWindowTests(WpfFixture wpf)
{
    [Fact(Timeout = 60_000)]
    public Task DemoWindowOpensTilesAndRendersSessions() => wpf.Run(async () =>
    {
        using var host = AppHost.Build(["--Demo:Enabled=true", "--Demo:Speed=20", "--Workspace:RestoreOnStartup=false", "--Appearance:Backdrop=None"]);
        var shell = host.Services.GetRequiredService<ShellViewModel>();
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Width = 1600;
        window.Height = 1000;
        window.Show();

        await WpfFixture.Settle(5000);
        var snapshot = WpfFixture.Render((FrameworkElement)window.Content, 1600, 960, "main-window-demo");

        Assert.Equal(4, shell.Panes.Count);
        Assert.True(snapshot.CountDistinctColours() > 50);

        window.Close();
        await shell.DisposeAsync();
    });
}
