using System.IO;
using System.Text;
using System.Windows.Threading;
using CCUI.App.Hosting;
using CCUI.App.Input;
using CCUI.App.Views;
using CCUI.Core.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CCUI.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Claude runs "CCUI.exe --hook" for session hooks: relay the payload and exit before any UI starts.
        if (args.Length > 0 && args[0] == HookEnvironment.HookArgument)
        {
            using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return HookEnvironment.Relay(input, Environment.GetEnvironmentVariable, TimeProvider.System);
        }

        var app = new App();
        app.InitializeComponent();

        using var host = AppHost.Build(args);
        host.Start();
        var logger = host.Services.GetRequiredService<ILogger<App>>();
        app.DispatcherUnhandledException += (_, e) => OnUnhandled(logger, e);
        ThemeResources.Apply(app, host.Services);
        KeyboardShortcuts.Register(host.Services);

        var window = host.Services.GetRequiredService<MainWindow>();
        var exitCode = app.Run(window);

        // End the sessions (their transcripts are saved by Claude, so they can be resumed).
        host.Services.GetRequiredService<CCUI.Core.ViewModels.ShellViewModel>().DisposeAsync().AsTask().GetAwaiter().GetResult();
        host.StopAsync().GetAwaiter().GetResult();
        return exitCode;
    }

    private static void OnUnhandled(ILogger logger, DispatcherUnhandledExceptionEventArgs e)
    {
        logger.LogError(e.Exception, "Unhandled UI exception");
        System.Windows.MessageBox.Show(e.Exception.Message, "CCUI", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        e.Handled = true;
    }
}
