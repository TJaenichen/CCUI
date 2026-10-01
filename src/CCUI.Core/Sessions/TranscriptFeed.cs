using CCUI.Core.Claude;

namespace CCUI.Core.Sessions;

/// <summary>A live stream of a session's transcript events (and hook callbacks). Events arrive on a background thread.</summary>
public interface ITranscriptFeed : IDisposable
{
    event EventHandler<IReadOnlyList<TranscriptEvent>>? EventsArrived;

    /// <summary>New events from the session's subagent transcripts (live activity only, no history).</summary>
    event EventHandler<IReadOnlyList<TranscriptEvent>>? SubagentEventsArrived;

    event EventHandler<HookEvent>? HookReceived;

    /// <summary>The pane now shows a different session (after /clear); the argument is the new session id.</summary>
    event EventHandler<string>? SessionSwitched;

    void Start();
}

/// <summary>
/// Polls a session's transcript file, its subagent transcripts (<c>&lt;session&gt;/subagents/*.jsonl</c> next to it)
/// and its pane's hook file. Before the transcript exists (a brand-new session) it keeps looking for it.
/// </summary>
public sealed class FileTranscriptFeed(
    ClaudePaths paths,
    string sessionId,
    string workingDirectory,
    string? hookFile,
    TimeSpan interval,
    TimeProvider time) : ITranscriptFeed
{
    private readonly TranscriptReader? _hooks = hookFile is null ? null : new TranscriptReader(hookFile);
    private string _sessionId = sessionId;
    private string? _transcriptPath;
    private TranscriptReader? _reader;
    private SubagentTranscripts? _subagents;
    private ITimer? _timer;
    private int _polling;

    public event EventHandler<IReadOnlyList<TranscriptEvent>>? EventsArrived;

    public event EventHandler<IReadOnlyList<TranscriptEvent>>? SubagentEventsArrived;

    public event EventHandler<HookEvent>? HookReceived;

    public event EventHandler<string>? SessionSwitched;

    public void Start() => _timer ??= time.CreateTimer(_ => Poll(), null, TimeSpan.Zero, interval);

    public void Dispose() => _timer?.Dispose();

    /// <summary>One polling pass; public so tests can drive it without a timer.</summary>
    public void Poll()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            ReadHooks();
            if (_reader is null && (_transcriptPath ?? paths.FindTranscript(_sessionId, workingDirectory)) is { } path)
            {
                _transcriptPath = path;
                _reader = new TranscriptReader(path);
                _subagents = new SubagentTranscripts(SubagentTranscripts.DirectoryFor(path));
            }

            if (_reader is not null)
            {
                var events = _reader.ReadNewLines().SelectMany(TranscriptParser.Parse).ToList();
                if (events.Count > 0)
                {
                    EventsArrived?.Invoke(this, events);
                }
            }

            if (_subagents?.ReadNewEvents() is { Count: > 0 } subagentEvents)
            {
                SubagentEventsArrived?.Invoke(this, subagentEvents);
            }
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private void ReadHooks()
    {
        if (_hooks is null)
        {
            return;
        }

        foreach (var line in _hooks.ReadNewLines())
        {
            if (HookEvent.Parse(line) is not { } hook)
            {
                continue;
            }

            if (hook.EventName == "SessionStart" && hook.SessionId is { Length: > 0 } id && !id.Equals(_sessionId, StringComparison.OrdinalIgnoreCase))
            {
                _sessionId = id;
                _transcriptPath = hook.TranscriptPath;
                _reader = null;
                _subagents = null;
                SessionSwitched?.Invoke(this, id);
            }

            HookReceived?.Invoke(this, hook);
        }
    }
}
