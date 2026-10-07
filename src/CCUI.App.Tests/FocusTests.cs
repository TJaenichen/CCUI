using System.Windows.Input;
using CCUI.App.Hosting;
using CCUI.App.Views;
using CCUI.Core.ViewModels;
using CCUI.Terminal.Wpf;
using Microsoft.Extensions.DependencyInjection;

namespace CCUI.App.Tests;

[Collection("Wpf")]
public sealed class FocusTests(WpfFixture wpf)
{
    [Fact(Timeout = 60_000)]
    public Task TheActivePanesTerminalHasTheKeyboardOnceOpened()
    {
        var cancel = TestContext.Current.CancellationToken;
        return wpf.Run(async () =>
        {
            using var host = AppHost.Build(["--Demo:Enabled=true", "--Demo:Speed=20", "--Workspace:RestoreOnStartup=false", "--Appearance:Backdrop=None"]);
            var shell = host.Services.GetRequiredService<ShellViewModel>();
            var window = host.Services.GetRequiredService<MainWindow>();
            window.Width = 1600;
            window.Height = 1000;
            window.Show();
            window.Activate();
            await WpfFixture.Settle(3000, cancel);

            // The pane that was active when its view was created must have taken the keyboard, without a click.
            var terminal = Assert.IsType<TerminalControl>(Keyboard.FocusedElement);
            Assert.Same(shell.ActivePane?.Terminal, terminal.Session);

            window.Close();
            await shell.DisposeAsync();
        });
    }
}
