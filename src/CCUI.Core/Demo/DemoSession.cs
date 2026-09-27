using System.Text;
using CCUI.Core.Claude;
using CCUI.Core.Sessions;
using CCUI.Terminal;

namespace CCUI.Core.Demo;

/// <summary>
/// Plays a <see cref="DemoProject"/>: paints a Claude-like screen into its <see cref="Connection"/> and publishes the
/// matching transcript events through its <see cref="Feed"/>, so every part of the UI has live data without running
/// Claude. Typed prompts get a short canned reply, which makes keyboard input easy to check.
/// </summary>
public sealed class DemoSession
{
    private readonly object _gate = new();
    private readonly DemoProject _project;
    private readonly TimeProvider _time;
    private readonly double _speed;
    private readonly Random _random;
    private readonly ClaudeTuiPainter _painter = new();
    private readonly Queue<DemoTurn> _typedTurns = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly StringBuilder _input = new();
    private TaskCompletionSource _submitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _bottomDrawn;
    private int _cursorLineInBottom;
    private string? _spinnerVerb;
    private int _spinnerFrame;
    private DateTimeOffset _turnStarted;
    private long _turnTokens;
    private long _totalTokens;
    private long _context = 18_000;
    private int _runningAgents;
    private int _messageCounter;
    private int _escapeState;

    public DemoSession(DemoProject project, string sessionId, TimeProvider time, double speed, int seed)
    {
        _project = project;
        SessionId = sessionId;
        _time = time;
        _speed = Math.Max(0.05, speed);
        _random = new Random(seed);
        Connection = new DemoConnection(this);
        Feed = new DemoFeed();
    }

    public string SessionId { get; }

    public ITerminalConnection Connection { get; }

    public ITranscriptFeed Feed { get; }

    private DemoFeed FeedImpl => (DemoFeed)Feed;

    private DemoConnection ConnectionImpl => (DemoConnection)Connection;

    private void Start(int columns)
    {
        _painter.Width = columns;
        _ = Task.Run(() => RunAsync(_stop.Token));
    }

    private async Task RunAsync(CancellationToken cancel)
    {
        try
        {
            FeedImpl.Publish([new SessionMetadataEvent(_time.GetUtcNow(), SessionId, _project.WorkingDirectory, _project.Branch, "2.1.283")]);
            lock (_gate)
            {
                Redraw(_painter.Welcome(_project.Name, _project.WorkingDirectory));
            }

            var next = 0;
            var first = true;
            while (!cancel.IsCancellationRequested)
            {
                var typed = await IdleAsync(first ? Seconds(1.5 + _random.NextDouble() * 2) : Seconds(20 + _random.NextDouble() * 25), cancel).ConfigureAwait(false);
                first = false;
                var turn = typed ?? _project.Turns[next++ % _project.Turns.Count];
                await PlayTurnAsync(turn, cancel).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Waits for the idle period, returning early with a typed prompt if the user submits one.</summary>
    private async Task<DemoTurn?> IdleAsync(TimeSpan delay, CancellationToken cancel)
    {
        Task submitted;
        lock (_gate)
        {
            if (_typedTurns.Count > 0)
            {
                return _typedTurns.Dequeue();
            }

            submitted = _submitted.Task;
        }

        await Task.WhenAny(Task.Delay(delay, _time, cancel), submitted).ConfigureAwait(false);
        cancel.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return _typedTurns.Count > 0 ? _typedTurns.Dequeue() : null;
        }
    }

    private async Task PlayTurnAsync(DemoTurn turn, CancellationToken cancel)
    {
        lock (_gate)
        {
            _turnStarted = _time.GetUtcNow();
            _turnTokens = 0;
            Redraw(_painter.Prompt(turn.Prompt));
        }

        FeedImpl.Publish([new UserPromptEvent(_time.GetUtcNow(), null, turn.Prompt, IsSlashCommand: false)]);

        for (var i = 0; i < turn.Actions.Count; i++)
        {
            var last = i == turn.Actions.Count - 1;
            switch (turn.Actions[i])
            {
                case DemoThink think:
                    await SpinAsync("Thinking", think.Seconds, cancel).ConfigureAwait(false);
                    PublishAssistant(think.Text.Length, last, id => new ThinkingEvent(_time.GetUtcNow(), null, id, think.Text));
                    break;

                case DemoTool tool:
                    var toolId = "toolu_demo_" + Guid.NewGuid().ToString("N")[..12];
                    PublishAssistant(tool.InputJson.Length, stop: false, id => new ToolUseEvent(_time.GetUtcNow(), null, id, toolId, tool.Name, tool.InputJson));
                    await SpinAsync(tool.Name == "Bash" ? "Running" : "Working", tool.Seconds, cancel).ConfigureAwait(false);
                    FeedImpl.Publish([new ToolResultEvent(_time.GetUtcNow(), null, toolId, string.Join('\n', tool.Output), tool.IsError)]);
                    lock (_gate)
                    {
                        Redraw(_painter.Tool(tool));
                    }

                    break;

                case DemoAgent agent:
                    var agentId = "toolu_demo_" + Guid.NewGuid().ToString("N")[..12];
                    var input = $$"""{"description":"{{agent.Description}}","subagent_type":"{{agent.AgentType}}","prompt":"…"}""";
                    PublishAssistant(input.Length, stop: false, id => new ToolUseEvent(_time.GetUtcNow(), null, id, agentId, "Task", input));
                    Interlocked.Increment(ref _runningAgents);
                    await SpinAsync("Delegating", agent.Seconds, cancel).ConfigureAwait(false);
                    Interlocked.Decrement(ref _runningAgents);
                    var agentTokens = 8_000 + _random.Next(20_000);
                    FeedImpl.Publish([new ToolResultEvent(_time.GetUtcNow(), null, agentId, agent.Result, false)]);
                    lock (_gate)
                    {
                        Redraw(_painter.Agent(agent, agentTokens));
                    }

                    break;

                case DemoSay say:
                    await SpinAsync("Writing", 0.8 + (say.Text.Length / 400.0), cancel).ConfigureAwait(false);
                    PublishAssistant(say.Text.Length, last, id => new AssistantTextEvent(_time.GetUtcNow(), null, id, say.Text));
                    lock (_gate)
                    {
                        Redraw(_painter.Say(say.Text));
                    }

                    break;
            }
        }

        lock (_gate)
        {
            _spinnerVerb = null;
            Redraw(null);
        }

        FeedImpl.PublishHook(new HookEvent("Stop", SessionId, null, null, null, _time.GetUtcNow()));
    }

    private void PublishAssistant(int size, bool stop, Func<string, TranscriptEvent> content)
    {
        var messageId = $"msg_demo_{SessionId[..8]}_{Interlocked.Increment(ref _messageCounter)}";
        var output = 40 + (size / 3) + _random.Next(200);
        _context += 400 + _random.Next(2_500);
        Interlocked.Add(ref _turnTokens, output);
        Interlocked.Add(ref _totalTokens, output + 3);
        var usage = new TokenUsage(3, output, 400 + _random.Next(1_500), _context);
        FeedImpl.Publish(
        [
            content(messageId),
            new AssistantUsageEvent(_time.GetUtcNow(), null, messageId, "claude-opus-5-5", usage, stop ? "end_turn" : "tool_use"),
        ]);
    }

    private async Task SpinAsync(string verb, double seconds, CancellationToken cancel)
    {
        var end = _time.GetUtcNow() + Seconds(seconds * (0.8 + (_random.NextDouble() * 0.4)));
        lock (_gate)
        {
            _spinnerVerb = verb;
        }

        while (_time.GetUtcNow() < end)
        {
            lock (_gate)
            {
                _spinnerFrame++;
                Redraw(null);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(120), _time, cancel).ConfigureAwait(false);
        }
    }

    private TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds / _speed);

    /// <summary>Erases the bottom area, appends <paramref name="history"/> above it and paints it again. Call under the lock.</summary>
    private void Redraw(string? history)
    {
        var sb = new StringBuilder("\e[?2026h");
        if (_bottomDrawn)
        {
            if (_cursorLineInBottom > 0)
            {
                sb.Append("\e[").Append(_cursorLineInBottom).Append('A');
            }

            sb.Append("\r\e[J");
        }

        sb.Append(history);
        var state = new DemoBottomState(
            _spinnerVerb,
            _spinnerFrame,
            _spinnerVerb is null ? TimeSpan.Zero : _time.GetUtcNow() - _turnStarted,
            Interlocked.Read(ref _turnTokens),
            _input.ToString(),
            _project.Name,
            Interlocked.Read(ref _totalTokens),
            (int)Math.Min(99, _context * 100 / 1_000_000),
            16,
            Volatile.Read(ref _runningAgents));
        var (lines, inputLine, inputColumn) = _painter.Bottom(state);
        sb.Append(string.Join("\r\n", lines));
        var up = lines.Count - 1 - inputLine;
        if (up > 0)
        {
            sb.Append("\e[").Append(up).Append('A');
        }

        sb.Append("\e[").Append(inputColumn + 1).Append('G');
        sb.Append("\e[?2026l");
        _cursorLineInBottom = inputLine;
        _bottomDrawn = true;
        ConnectionImpl.Emit(sb.ToString());
    }

    private void OnInput(ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data);
        lock (_gate)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (_escapeState == 1)
                {
                    // ESC CR (Shift+Enter) adds a line; ESC [ starts a sequence to skip.
                    _escapeState = c == '[' || c == 'O' ? 2 : 0;
                    if (c == '\r')
                    {
                        _input.Append('\n');
                    }

                    continue;
                }

                if (_escapeState == 2)
                {
                    if (c is >= '@' and <= '~')
                    {
                        _escapeState = 0;
                    }

                    continue;
                }

                switch (c)
                {
                    case '\e':
                        _escapeState = 1;
                        break;
                    case '\r':
                        Submit();
                        break;
                    case '\x7f':
                    case '\b':
                        RemoveLastTextElement();
                        break;
                    default:
                        if (c >= ' ')
                        {
                            _input.Append(c);
                        }

                        break;
                }
            }

            Redraw(null);
        }
    }

    private void Submit()
    {
        var prompt = _input.ToString().Trim();
        _input.Clear();
        if (prompt.Length == 0)
        {
            return;
        }

        _typedTurns.Enqueue(new DemoTurn(prompt,
        [
            new DemoThink(1.2, "Demo mode: acknowledge the typed prompt."),
            new DemoSay($"Demo mode doesn't run Claude, but your input arrived intact: “{prompt}” 👍"),
        ]));
        var submitted = _submitted;
        _submitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        submitted.TrySetResult();
    }

    private void RemoveLastTextElement()
    {
        var text = _input.ToString();
        if (text.Length == 0)
        {
            return;
        }

        var elements = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        _input.Length = elements[^1];
    }

    private sealed class DemoConnection(DemoSession owner) : ITerminalConnection
    {
        public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;

        public event EventHandler<int?>? Exited;

        public void Start(int columns, int rows) => owner.Start(columns);

        public void Write(ReadOnlySpan<byte> data) => owner.OnInput(data);

        public void Resize(int columns, int rows)
        {
            lock (owner._gate)
            {
                // Like Claude Code, repaint the live area at the new width; history above stays as it was.
                owner._painter.Width = columns;
                if (owner._bottomDrawn)
                {
                    owner.Redraw(null);
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            owner._stop.Cancel();
            Exited?.Invoke(this, 0);
            return ValueTask.CompletedTask;
        }

        public void Emit(string ansi) => DataReceived?.Invoke(this, Encoding.UTF8.GetBytes(ansi));
    }

    private sealed class DemoFeed : ITranscriptFeed
    {
        public event EventHandler<IReadOnlyList<TranscriptEvent>>? EventsArrived;

        public event EventHandler<HookEvent>? HookReceived;

        public event EventHandler<string>? SessionSwitched
        {
            add { }
            remove { }
        }

        public void Start()
        {
        }

        public void Dispose()
        {
        }

        public void Publish(IReadOnlyList<TranscriptEvent> events) => EventsArrived?.Invoke(this, events);

        public void PublishHook(HookEvent hook) => HookReceived?.Invoke(this, hook);
    }
}
