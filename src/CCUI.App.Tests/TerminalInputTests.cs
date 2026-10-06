using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CCUI.Terminal;
using CCUI.Terminal.Wpf;

namespace CCUI.App.Tests;

/// <summary>
/// Sends real WPF key events to a <see cref="TerminalControl"/> (hosted in a window that is never shown) and checks
/// what reaches the process. Modifier keys are set on the UI thread's keyboard state, which is what
/// <see cref="Keyboard.Modifiers"/> reads. Mouse selection is driven at fixed points, because the position of a
/// WPF mouse event is wherever the real pointer happens to be.
/// </summary>
[Collection("Wpf")]
public sealed class TerminalInputTests(WpfFixture wpf)
{
    private const int VkControl = 0x11;
    private const int VkLeftControl = 0xA2;

    [Fact(Timeout = 60_000)]
    public Task ControlLettersReachTheProcess()
    {
        var cancel = TestContext.Current.CancellationToken;
        return wpf.Run(async () =>
        {
            using var host = await Host.Create(cancel);

            using (HoldControl())
            {
                host.Press(Key.Z);
                host.Press(Key.X);
                host.Press(Key.C);
            }

            Assert.Equal("\x1a\x18\x03", host.Connection.Sent);
        });
    }

    [Fact(Timeout = 60_000)]
    public Task AClickIsNotASelection()
    {
        var cancel = TestContext.Current.CancellationToken;
        return wpf.Run(async () =>
        {
            using var host = await Host.Create(cancel);

            // WPF raises MouseMove when the mouse is captured, so a click always comes with a move at (about) the same spot.
            host.Control.BeginSelection(new Point(30, 10), clickCount: 1);
            host.Control.ExtendSelection(new Point(30, 10));
            host.Control.ExtendSelection(new Point(31, 11));
            host.Control.EndSelection();
            Assert.False(host.Control.HasSelection);

            // So Ctrl+C right after clicking into a terminal still interrupts, instead of copying one cell.
            using (HoldControl())
            {
                host.Press(Key.C);
            }

            Assert.Equal("\x03", host.Connection.Sent);
        });
    }

    [Fact(Timeout = 60_000)]
    public Task ADragAndADoubleClickSelect()
    {
        var cancel = TestContext.Current.CancellationToken;
        return wpf.Run(async () =>
        {
            using var host = await Host.Create(cancel);

            host.Control.BeginSelection(new Point(5, 10), clickCount: 1);
            host.Control.ExtendSelection(new Point(60, 10));
            host.Control.EndSelection();
            Assert.True(host.Control.HasSelection);

            host.Control.ClearSelection();
            host.Control.BeginSelection(new Point(5, 10), clickCount: 2);
            host.Control.EndSelection();
            Assert.True(host.Control.HasSelection);
        });
    }

    [Fact(Timeout = 60_000)]
    public Task PastesFilesAsQuotedPathsAndImagesAsAltV()
    {
        var cancel = TestContext.Current.CancellationToken;
        return wpf.Run(async () =>
        {
            using var host = await Host.Create(cancel);

            // Files copied in Explorer or dropped on the terminal: paths, quoted when they contain spaces.
            host.Control.Paste(new DataObject(DataFormats.FileDrop, new[] { @"C:\shots\My Screen.png", @"D:\work\notes.md" }));
            Assert.Equal("\"C:\\shots\\My Screen.png\" D:\\work\\notes.md", host.Connection.Sent);

            // Text wins over everything else.
            var both = new DataObject();
            both.SetData(DataFormats.UnicodeText, "typed");
            both.SetData(DataFormats.FileDrop, new[] { @"C:\x.txt" });
            host.Control.Paste(both);
            Assert.EndsWith("typed", host.Connection.Sent);

            // An image alone: Alt+V, so Claude Code reads the image from the clipboard itself.
            var pixel = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, new byte[4], 4);
            host.Control.Paste(new DataObject(DataFormats.Bitmap, pixel));
            Assert.EndsWith("\ev", host.Connection.Sent);
        });
    }

    /// <summary>Marks Ctrl as held on this thread until disposed.</summary>
    private static ControlHeld HoldControl() => new();

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] state);

    [DllImport("user32.dll")]
    private static extern bool SetKeyboardState(byte[] state);

    private sealed class ControlHeld : IDisposable
    {
        private readonly byte[] _before = new byte[256];

        public ControlHeld()
        {
            GetKeyboardState(_before);
            var held = (byte[])_before.Clone();
            held[VkControl] = held[VkLeftControl] = 0x80;
            SetKeyboardState(held);
        }

        public void Dispose() => SetKeyboardState(_before);
    }

    private sealed class Host : IDisposable
    {
        private readonly HwndSource _source;

        private Host(ScriptedConnection connection, TerminalControl control, HwndSource source)
        {
            Connection = connection;
            Control = control;
            _source = source;
        }

        public ScriptedConnection Connection { get; }

        public TerminalControl Control { get; }

        public static async Task<Host> Create(CancellationToken cancel)
        {
            var connection = new ScriptedConnection("first line\r\nsecond line");
            var session = new TerminalSession(connection, 40, 6, 100);
            var control = new TerminalControl { Session = session, Width = 520, Height = 140, Padding = new Thickness(0), FontSize = 16 };
            var source = new HwndSource(new HwndSourceParameters("CCUI input test") { Width = 520, Height = 140, WindowStyle = 0 }) { RootVisual = control };
            session.Start();
            await WpfFixture.Settle(100, cancel);
            return new Host(connection, control, source);
        }

        public void Press(Key key) => Control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, _source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });

        public void Dispose() => _source.Dispose();
    }
}
