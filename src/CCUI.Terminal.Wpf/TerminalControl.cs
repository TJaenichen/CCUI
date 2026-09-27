using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CCUI.Terminal.Buffer;
using CCUI.Terminal.Wpf.Rendering;

namespace CCUI.Terminal.Wpf;

/// <summary>
/// A WPF terminal view for a <see cref="TerminalSession"/>. It draws with WPF glyph runs (so it composes with
/// transparency, background images and overlays), falls back across fonts, draws box/block characters itself and
/// shows emoji in colour. Cells with the default background are transparent: set <see cref="Control.Background"/>
/// (a colour, an image brush, anything) to style what shows behind the text.
/// </summary>
[TemplatePart(Name = SurfacePartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = ScrollBarPartName, Type = typeof(ScrollBar))]
public partial class TerminalControl : Control
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(TerminalSession), typeof(TerminalControl), new PropertyMetadata(null, OnSessionChanged));

    public static readonly DependencyProperty ColorSchemeProperty = DependencyProperty.Register(
        nameof(ColorScheme), typeof(ColorScheme), typeof(TerminalControl), new PropertyMetadata(Terminal.ColorScheme.Campbell, OnAppearanceChanged));

    public static readonly DependencyProperty FallbackFontFamiliesProperty = DependencyProperty.Register(
        nameof(FallbackFontFamilies), typeof(string), typeof(TerminalControl),
        new PropertyMetadata("Cascadia Mono, Segoe UI Symbol, Segoe UI Emoji, Segoe UI, MS Gothic, Consolas", OnAppearanceChanged));

    public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register(
        nameof(LineHeight), typeof(double), typeof(TerminalControl), new PropertyMetadata(1.0, OnAppearanceChanged));

    public static readonly DependencyProperty CursorShapeProperty = DependencyProperty.Register(
        nameof(CursorShape), typeof(CursorShape), typeof(TerminalControl), new PropertyMetadata(Terminal.CursorShape.Bar, OnOverlayChanged));

    public static readonly DependencyProperty CursorBlinkProperty = DependencyProperty.Register(
        nameof(CursorBlink), typeof(bool), typeof(TerminalControl), new PropertyMetadata(true, OnOverlayChanged));

    public static readonly DependencyProperty IntenseTextStyleProperty = DependencyProperty.Register(
        nameof(IntenseTextStyle), typeof(IntenseTextStyle), typeof(TerminalControl), new PropertyMetadata(Wpf.IntenseTextStyle.All, OnAppearanceChanged));

    public static readonly DependencyProperty UseBuiltinGlyphsProperty = DependencyProperty.Register(
        nameof(UseBuiltinGlyphs), typeof(bool), typeof(TerminalControl), new PropertyMetadata(true, OnAppearanceChanged));

    public static readonly DependencyProperty UseColorEmojiProperty = DependencyProperty.Register(
        nameof(UseColorEmoji), typeof(bool), typeof(TerminalControl), new PropertyMetadata(true, OnAppearanceChanged));

    public static readonly DependencyProperty CopyOnSelectProperty = DependencyProperty.Register(
        nameof(CopyOnSelect), typeof(bool), typeof(TerminalControl), new PropertyMetadata(false));

    public static readonly DependencyProperty WheelScrollLinesProperty = DependencyProperty.Register(
        nameof(WheelScrollLines), typeof(int), typeof(TerminalControl), new PropertyMetadata(3));

    public static readonly DependencyProperty KeyBindingsProperty = DependencyProperty.Register(
        nameof(KeyBindings), typeof(IReadOnlyList<TerminalKeyBinding>), typeof(TerminalControl), new PropertyMetadata(null));

    public static readonly DependencyProperty ScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(ScrollBarVisibility), typeof(Visibility), typeof(TerminalControl), new PropertyMetadata(Visibility.Visible));

    private const string SurfacePartName = "PART_Surface";
    private const string ScrollBarPartName = "PART_ScrollBar";
    private static readonly TimeSpan SynchronizedOutputTimeout = TimeSpan.FromMilliseconds(150);

    private readonly TerminalFrame _frame = new();
    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _resizeTimer;
    private readonly DispatcherTimer _syncTimer;
    private ColorEmojiRasterizer? _emoji;
    private TerminalSurface? _surface;
    private ScrollBar? _scrollBar;
    private int _renderScheduled;
    private bool _rendererDirty = true;
    private bool _blinkOn = true;
    private bool _updatingScrollBar;

    // Viewport: either following the live screen, or pinned to a line. Pinned positions use "monotonic" line numbers
    // (absolute index + lines the scrollback ring has dropped) so the view stays put while output continues.
    private bool _following = true;
    private long _viewTopMonotonic;
    private long _droppedLines;
    private int _scrollbackCount;
    private DateTime _synchronizedSince;

    static TerminalControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TerminalControl), new FrameworkPropertyMetadata(typeof(TerminalControl)));
        FocusableProperty.OverrideMetadata(typeof(TerminalControl), new FrameworkPropertyMetadata(true));
        FontFamilyProperty.OverrideMetadata(typeof(TerminalControl), new FrameworkPropertyMetadata(OnAppearanceChanged));
        FontSizeProperty.OverrideMetadata(typeof(TerminalControl), new FrameworkPropertyMetadata(OnAppearanceChanged));
        FontWeightProperty.OverrideMetadata(typeof(TerminalControl), new FrameworkPropertyMetadata(OnAppearanceChanged));
    }

    public TerminalControl()
    {
        _blinkTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(530) };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            RenderOverlay();
        };
        _resizeTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(40) };
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            ApplySize();
        };
        _syncTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = SynchronizedOutputTimeout };
        _syncTimer.Tick += (_, _) =>
        {
            _syncTimer.Stop();
            RenderNow(force: true);
        };

        Loaded += (_, _) =>
        {
            _rendererDirty = true;
            UpdateBlinkTimer();
            ScheduleRender();
        };
        Unloaded += (_, _) =>
        {
            _blinkTimer.Stop();
            _emoji?.Dispose();
            _emoji = null;
            _rendererDirty = true;
        };
    }

    public TerminalSession? Session
    {
        get => (TerminalSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public ColorScheme ColorScheme
    {
        get => (ColorScheme)GetValue(ColorSchemeProperty);
        set => SetValue(ColorSchemeProperty, value);
    }

    /// <summary>Comma-separated font families tried, in order, for characters the main font lacks.</summary>
    public string FallbackFontFamilies
    {
        get => (string)GetValue(FallbackFontFamiliesProperty);
        set => SetValue(FallbackFontFamiliesProperty, value);
    }

    /// <summary>Row height as a multiple of the font's natural line height.</summary>
    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    public CursorShape CursorShape
    {
        get => (CursorShape)GetValue(CursorShapeProperty);
        set => SetValue(CursorShapeProperty, value);
    }

    public bool CursorBlink
    {
        get => (bool)GetValue(CursorBlinkProperty);
        set => SetValue(CursorBlinkProperty, value);
    }

    public IntenseTextStyle IntenseTextStyle
    {
        get => (IntenseTextStyle)GetValue(IntenseTextStyleProperty);
        set => SetValue(IntenseTextStyleProperty, value);
    }

    public bool UseBuiltinGlyphs
    {
        get => (bool)GetValue(UseBuiltinGlyphsProperty);
        set => SetValue(UseBuiltinGlyphsProperty, value);
    }

    public bool UseColorEmoji
    {
        get => (bool)GetValue(UseColorEmojiProperty);
        set => SetValue(UseColorEmojiProperty, value);
    }

    public bool CopyOnSelect
    {
        get => (bool)GetValue(CopyOnSelectProperty);
        set => SetValue(CopyOnSelectProperty, value);
    }

    public int WheelScrollLines
    {
        get => (int)GetValue(WheelScrollLinesProperty);
        set => SetValue(WheelScrollLinesProperty, value);
    }

    public IReadOnlyList<TerminalKeyBinding>? KeyBindings
    {
        get => (IReadOnlyList<TerminalKeyBinding>?)GetValue(KeyBindingsProperty);
        set => SetValue(KeyBindingsProperty, value);
    }

    public Visibility ScrollBarVisibility
    {
        get => (Visibility)GetValue(ScrollBarVisibilityProperty);
        set => SetValue(ScrollBarVisibilityProperty, value);
    }

    /// <summary>The size of one cell in device-independent pixels, once the control has been laid out.</summary>
    public Size CellSize => _surface?.Renderer is { } r ? new Size(r.Metrics.Width, r.Metrics.Height) : Size.Empty;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_surface is not null)
        {
            _surface.SizeChanged -= OnSurfaceSizeChanged;
        }

        if (_scrollBar is not null)
        {
            _scrollBar.ValueChanged -= OnScrollBarValueChanged;
        }

        _surface = new TerminalSurface();
        if (GetTemplateChild(SurfacePartName) is Decorator host)
        {
            host.Child = _surface;
        }

        _surface.SizeChanged += OnSurfaceSizeChanged;
        _scrollBar = GetTemplateChild(ScrollBarPartName) as ScrollBar;
        if (_scrollBar is not null)
        {
            _scrollBar.ValueChanged += OnScrollBarValueChanged;
        }

        _rendererDirty = true;
        ScheduleRender();
    }

    /// <summary>Scrolls the viewport; positive values scroll towards older output.</summary>
    public void ScrollLines(int lines)
    {
        if (lines == 0 || Session is null)
        {
            return;
        }

        var top = CurrentTopMonotonic() - lines;
        var bottomTop = _droppedLines + _scrollbackCount;
        _viewTopMonotonic = Math.Clamp(top, _droppedLines, bottomTop);
        _following = _viewTopMonotonic >= bottomTop;
        ScheduleRender();
    }

    public void ScrollToBottom()
    {
        if (!_following)
        {
            _following = true;
            ScheduleRender();
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _rendererDirty = true;
        ScheduleRender();
    }

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TerminalControl)d;
        if (e.OldValue is TerminalSession old)
        {
            old.OutputReceived -= control.OnOutputReceived;
        }

        control.ClearSelection();
        control._following = true;
        if (e.NewValue is TerminalSession session)
        {
            session.Emulator.Palette = TerminalPalette.FromScheme(control.ColorScheme);
            session.OutputReceived += control.OnOutputReceived;
            control.ApplySize();
        }

        control.ScheduleRender();
    }

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TerminalControl)d;
        control._rendererDirty = true;
        if (e.Property == ColorSchemeProperty && control.Session is { } session)
        {
            session.Emulator.Palette = TerminalPalette.FromScheme(control.ColorScheme);
        }

        control.ScheduleRender();
        control.ScheduleResize();
    }

    private static void OnOverlayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TerminalControl)d;
        control.UpdateBlinkTimer();
        control.RenderOverlay();
    }

    private void OnOutputReceived(object? sender, ReadOnlyMemory<byte> data) => ScheduleRender();

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e) => ScheduleResize();

    private void ScheduleResize()
    {
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    /// <summary>Coalesces render requests from any thread into one render per dispatcher cycle.</summary>
    private void ScheduleRender()
    {
        if (Interlocked.Exchange(ref _renderScheduled, 1) == 0)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () => RenderNow(force: false));
        }
    }

    private void RenderNow(bool force)
    {
        Interlocked.Exchange(ref _renderScheduled, 0);
        if (Session is not { } session || _surface is null || !IsLoaded)
        {
            return;
        }

        // DEC 2026: while the application is mid-frame, keep showing the previous frame (for at most a moment).
        if (session.Emulator.Modes.SynchronizedOutput && !force)
        {
            if (_synchronizedSince == default)
            {
                _synchronizedSince = DateTime.UtcNow;
            }

            if (DateTime.UtcNow - _synchronizedSince < SynchronizedOutputTimeout)
            {
                _syncTimer.Stop();
                _syncTimer.Start();
                return;
            }
        }

        _synchronizedSince = default;
        EnsureRenderer();
        CaptureFrame(session.Emulator);
        _surface.Render(_frame, BuildOverlay());
        UpdateScrollBar();
    }

    private void RenderOverlay()
    {
        _surface?.RenderOverlay(BuildOverlay());
    }

    private OverlayState BuildOverlay() => new(
        SelectionSpans(),
        CursorShape,
        CursorOn: !CursorBlink || !IsKeyboardFocusWithin || _blinkOn,
        Focused: IsKeyboardFocusWithin);

    private void EnsureRenderer()
    {
        if (!_rendererDirty || _surface is null)
        {
            return;
        }

        _rendererDirty = false;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var fonts = new FontFallback(FontFamily, FontWeight, FallbackFontFamilies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var metrics = CellMetrics.From(fonts.Primary, FontSize, dpi, LineHeight);
        _emoji ??= new ColorEmojiRasterizer();
        var settings = new RenderSettings(TerminalPalette.FromScheme(ColorScheme), IntenseTextStyle, UseBuiltinGlyphs, UseColorEmoji);
        _surface.Renderer = new RowRenderer(metrics, fonts, _emoji, settings);
        if (Session is { } session)
        {
            session.Emulator.CellPixelSize = ((int)Math.Round(metrics.Width * dpi), (int)Math.Round(metrics.Height * dpi));
        }
    }

    private void ApplySize()
    {
        if (Session is not { } session || _surface is null || _surface.ActualWidth <= 0 || _surface.ActualHeight <= 0)
        {
            return;
        }

        EnsureRenderer();
        if (_surface.Renderer is not { } renderer)
        {
            return;
        }

        var columns = Math.Max(2, (int)Math.Floor(_surface.ActualWidth / renderer.Metrics.Width));
        var rows = Math.Max(1, (int)Math.Floor(_surface.ActualHeight / renderer.Metrics.Height));
        session.Resize(columns, rows);
        ScheduleRender();
    }

    private void CaptureFrame(TerminalEmulator emulator)
    {
        lock (emulator.SyncRoot)
        {
            var buffer = emulator.Buffer;
            _frame.EnsureSize(buffer.Columns, buffer.Rows);
            _scrollbackCount = buffer.ScrollbackCount;
            _droppedLines = emulator.IsAlternateScreen ? 0 : emulator.ScrolledOffCount - buffer.ScrollbackCount;

            var top = _following || emulator.IsAlternateScreen
                ? buffer.ScrollbackCount
                : (int)Math.Clamp(_viewTopMonotonic - _droppedLines, 0, buffer.ScrollbackCount);
            if (top == buffer.ScrollbackCount)
            {
                _following = true;
            }

            _frame.TopLine = top;
            for (var r = 0; r < buffer.Rows; r++)
            {
                _frame.CopyLine(r, buffer.GetLine(top + r));
            }

            var cursorRow = buffer.ScrollbackCount + emulator.CursorRow - top;
            _frame.CursorRow = cursorRow >= 0 && cursorRow < buffer.Rows ? cursorRow : -1;
            _frame.CursorColumn = emulator.CursorColumn;
            _frame.CursorVisible = emulator.Modes.CursorVisible;
            _frame.CursorOnWideCell = _frame.CursorRow >= 0 && buffer[emulator.CursorRow][emulator.CursorColumn].Width == CellWidth.WideLead;
            _frame.RequestedCursorStyle = emulator.RequestedCursorStyle;
        }
    }

    private long CurrentTopMonotonic() => _following ? _droppedLines + _scrollbackCount : _viewTopMonotonic;

    private void UpdateScrollBar()
    {
        if (_scrollBar is null)
        {
            return;
        }

        _updatingScrollBar = true;
        _scrollBar.Minimum = 0;
        _scrollBar.Maximum = _scrollbackCount;
        _scrollBar.ViewportSize = _frame.Rows;
        _scrollBar.LargeChange = Math.Max(1, _frame.Rows - 1);
        _scrollBar.SmallChange = 1;
        _scrollBar.Value = _frame.TopLine;
        _updatingScrollBar = false;
    }

    private void OnScrollBarValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingScrollBar)
        {
            return;
        }

        var top = (int)Math.Round(e.NewValue);
        _viewTopMonotonic = _droppedLines + top;
        _following = top >= _scrollbackCount;
        ScheduleRender();
    }

    private void UpdateBlinkTimer()
    {
        _blinkOn = true;
        if (CursorBlink && IsKeyboardFocusWithin && IsLoaded)
        {
            _blinkTimer.Stop();
            _blinkTimer.Start();
        }
        else
        {
            _blinkTimer.Stop();
        }
    }
}
