using System.IO;
using CCUI.App.Services;
using CCUI.App.Views;
using CCUI.Core.Claude;
using CCUI.Core.Demo;
using CCUI.Core.Sessions;
using CCUI.Core.Settings;
using CCUI.Core.Threading;
using CCUI.Core.ViewModels;
using CCUI.Core.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CCUI.App.Hosting;

/// <summary>The composition root: configuration (appsettings.json, appsettings.{Environment}.json, the user file,
/// environment variables, command line) and every service registration.</summary>
public static class AppHost
{
    public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCUI");

    public static IHost Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Configuration.AddJsonFile(Path.Combine(DataDirectory, "appsettings.user.json"), optional: true, reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables("CCUI_");
        builder.Configuration.AddCommandLine(args);

        var services = builder.Services;
        services.Configure<ClaudeOptions>(builder.Configuration.GetSection(ClaudeOptions.Section));
        services.Configure<SessionListOptions>(builder.Configuration.GetSection(SessionListOptions.Section));
        services.Configure<TerminalOptions>(builder.Configuration.GetSection(TerminalOptions.Section));
        services.Configure<AppearanceOptions>(builder.Configuration.GetSection(AppearanceOptions.Section));
        services.Configure<KeyBindingOptions>(builder.Configuration.GetSection(KeyBindingOptions.Section));
        services.Configure<WorkspaceOptions>(builder.Configuration.GetSection(WorkspaceOptions.Section));
        services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.Section));

        var demo = builder.Configuration.GetValue<bool>($"{DemoOptions.Section}:{nameof(DemoOptions.Enabled)}");

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IUiDispatcher>(new WpfDispatcher(System.Windows.Application.Current.Dispatcher));
        services.AddSingleton(sp => new ClaudePaths(sp.GetRequiredService<IOptions<ClaudeOptions>>().Value.ConfigDirectory));
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IWorkspaceStore>(sp => new WorkspaceStore(
            sp.GetRequiredService<IOptions<WorkspaceOptions>>().Value.Path is { Length: > 0 } path
                ? Environment.ExpandEnvironmentVariables(path)
                : Path.Combine(DataDirectory, "workspace.json")));

        if (demo)
        {
            services.AddSingleton<ISessionCatalog, DemoSessionCatalog>();
            services.AddSingleton<ISessionLauncher, DemoSessionLauncher>();
            services.AddSingleton<IClaudeProcessProbe, NoClaudeProcesses>();
        }
        else
        {
            services.AddSingleton(_ => new HookEnvironment(Path.Combine(DataDirectory, "hooks"), Environment.ProcessPath!));
            services.AddSingleton<ISessionCatalog, SessionCatalog>();
            services.AddSingleton<ISessionLauncher, ClaudeSessionLauncher>();
            services.AddSingleton<IClaudeProcessProbe, WmiClaudeProcessProbe>();
        }

        services.AddSingleton<TerminalAppearance>();
        services.AddSingleton<ShellServices>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
        return builder.Build();
    }
}
