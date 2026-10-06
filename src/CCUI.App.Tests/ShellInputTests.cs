using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CCUI.App.Hosting;
using CCUI.App.Views;
using CCUI.Core.ViewModels;
using CCUI.Terminal.Wpf;
using Microsoft.Extensions.DependencyInjection;

namespace CCUI.App.Tests;

/// <summary>
/// Keys pressed in a terminal of the real window must reach its session: nothing between the window and the
/// terminal (docking, app shortcuts, command bindings) may swallow them.
/// </summary>
[Collection("Wpf")]
public sealed class ShellInputTests(WpfFixture wpf)
{
    [Fact(Timeout = 60_000)]
    public Task ControlKeysInADockedTerminalReachTheSession()
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
            await WpfFixture.Settle(3000, cancel);

            var pane = shell.ActivePane ?? shell.Panes[0];
            var terminal = Descendants(window).OfType<TerminalControl>().Single(t => ReferenceEquals(t.Session, pane.Terminal));
            var sent = new List<int>();
            pane.Terminal.InputSent += (_, length) => sent.Add(length);
            var source = PresentationSource.FromVisual(terminal)!;

            void Press(Key key) => terminal.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });

            using (new ControlHeld())
            {
                Press(Key.Z);
                Press(Key.X);
                Press(Key.C);
            }

            // Three control characters, one byte each.
            Assert.Equal([1, 1, 1], sent);

            string? clipboard = null;
            try
            {
                clipboard = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException)
            {
            }

            if (clipboard is { Length: > 0 })
            {
                sent.Clear();
                using (new ControlHeld())
                {
                    Press(Key.V);
                }

                // The clipboard text, with or without bracketed-paste markers.
                Assert.Single(sent, length => length >= clipboard.Length);
            }

            window.Close();
            await shell.DisposeAsync();
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var grandchild in Descendants(child))
            {
                yield return grandchild;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] state);

    [DllImport("user32.dll")]
    private static extern bool SetKeyboardState(byte[] state);

    /// <summary>Marks Ctrl as held on this thread until disposed.</summary>
    private sealed class ControlHeld : IDisposable
    {
        private readonly byte[] _before = new byte[256];

        public ControlHeld()
        {
            GetKeyboardState(_before);
            var held = (byte[])_before.Clone();
            held[0x11] = held[0xA2] = 0x80;
            SetKeyboardState(held);
        }

        public void Dispose() => SetKeyboardState(_before);
    }
}
