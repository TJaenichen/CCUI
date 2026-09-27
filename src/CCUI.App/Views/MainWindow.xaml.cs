using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using CCUI.App.Services;
using CCUI.Core.Claude;
using CCUI.Core.Settings;
using CCUI.Core.ViewModels;
using CCUI.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCUI.App.Views;

/// <summary>
/// The main window. View logic only: backdrop, window placement, and the docking layout (save, restore, tile).
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MainWindow> _logger;
    private readonly WorkspaceState? _saved;
    private bool _closeConfirmed;

    public MainWindow(
        ShellViewModel shell,
        IWorkspaceStore store,
        IDialogService dialogs,
        IOptions<WorkspaceOptions> workspace,
        IOptions<AppearanceOptions> appearance,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();
        _shell = shell;
        _dialogs = dialogs;
        _logger = logger;
        DataContext = shell;
        _saved = workspace.Value.RestoreOnStartup && !shell.IsDemo ? store.Load() : null;
        ApplyPlacement(_saved?.Window);

        shell.LayoutProvider = SaveLayout;
        shell.LayoutRestorer = RestoreLayout;
        shell.WindowProvider = CapturePlacement;

        SourceInitialized += (_, _) =>
        {
            if (!WindowBackdrop.TryApply(this, appearance.Value.Backdrop))
            {
                // No system backdrop (Windows 10, or turned off): paint an opaque base under the tint.
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1A));
            }

            if (_saved?.Window?.Maximized == true)
            {
                WindowState = WindowState.Maximized;
            }
        };
        Loaded += async (_, _) =>
        {
            await _shell.StartAsync(_saved);
            if (string.IsNullOrEmpty(_saved?.DockLayout))
            {
                Tile();
            }
        };
        Closing += OnClosing;
    }

    /// <summary>Arranges all docked sessions in a grid: columns of up to three panes, side by side.</summary>
    public void Tile() => DockLayout.Tile(Dock, maxRowsPerColumn: 3);

    public void FocusSessionList()
    {
        _shell.IsSessionListVisible = true;
        Dispatcher.BeginInvoke(SessionList.FocusList);
    }

    private void OnTileClick(object sender, RoutedEventArgs e) => Tile();

    private void OnSessionListResize(object sender, DragDeltaEventArgs e) =>
        _shell.SessionListWidth = Math.Clamp(_shell.SessionListWidth + e.HorizontalChange, 180, Math.Max(180, ActualWidth / 2));

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var working = _shell.Panes.Count(p => p.Activity == SessionActivity.Working && !p.HasExited);
        if (!_closeConfirmed && working > 0
            && !_dialogs.Confirm("Close CCUI", $"{working} session(s) are still working. Close anyway? They will be reopened next time."))
        {
            e.Cancel = true;
            return;
        }

        _closeConfirmed = true;
        _shell.SaveWorkspace();
    }

    private string? SaveLayout()
    {
        try
        {
            using var writer = new StringWriter();
            new XmlLayoutSerializer(Dock).Serialize(writer);
            return writer.ToString();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _logger.LogWarning(ex, "Could not save the docking layout");
            return null;
        }
    }

    private void RestoreLayout(string layout)
    {
        try
        {
            var serializer = new XmlLayoutSerializer(Dock);
            serializer.LayoutSerializationCallback += (_, e) =>
            {
                if (e.Model.ContentId is { } id && _shell.Panes.FirstOrDefault(p => p.ContentId == id) is { } pane)
                {
                    e.Content = pane;
                }
                else
                {
                    e.Cancel = true;
                }
            };

            using var reader = new StringReader(layout);
            serializer.Deserialize(reader);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Xml.XmlException or IOException)
        {
            _logger.LogWarning(ex, "Could not restore the docking layout; tiling instead");
        }

        // Every open session must have a place; if the saved layout lost any, rebuild from the sessions and tile.
        var placed = Dock.Layout.Descendents().OfType<LayoutContent>().Select(c => c.Content).ToHashSet();
        if (_shell.Panes.Any(p => !placed.Contains(p)))
        {
            Dock.DocumentsSource = null;
            Dock.DocumentsSource = _shell.Panes;
            Tile();
        }
    }

    private void ApplyPlacement(WindowPlacement? placement)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (placement is null || !screen.IntersectsWith(new Rect(placement.Left, placement.Top, placement.Width, placement.Height)))
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = Math.Max(MinWidth, placement.Width);
        Height = Math.Max(MinHeight, placement.Height);
    }

    private WindowPlacement CapturePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized);
    }
}
