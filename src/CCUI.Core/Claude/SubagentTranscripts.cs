namespace CCUI.Core.Claude;

/// <summary>
/// Tails a session's subagent transcripts (<c>&lt;session&gt;/subagents/agent-*.jsonl</c>, one per agent). Only
/// activity from now on counts: whatever the files hold on the first look is skipped, and agents that start later
/// are read from their beginning.
/// </summary>
public sealed class SubagentTranscripts(string directory)
{
    private readonly Dictionary<string, TranscriptReader> _readers = new(StringComparer.OrdinalIgnoreCase);
    private bool _primed;

    public string Directory { get; } = directory;

    /// <summary>The subagent folder that belongs to a session transcript.</summary>
    public static string DirectoryFor(string transcriptPath) =>
        Path.Combine(Path.GetDirectoryName(transcriptPath) ?? string.Empty, Path.GetFileNameWithoutExtension(transcriptPath), "subagents");

    public IReadOnlyList<TranscriptEvent> ReadNewEvents()
    {
        var events = new List<TranscriptEvent>();
        try
        {
            var folder = new DirectoryInfo(Directory);
            if (folder.Exists)
            {
                // The listing carries each file's length, so files that did not grow are not opened.
                foreach (var file in folder.EnumerateFiles("*.jsonl"))
                {
                    if (!_readers.TryGetValue(file.FullName, out var reader))
                    {
                        reader = new TranscriptReader(file.FullName);
                        _readers[file.FullName] = reader;
                        if (!_primed)
                        {
                            reader.ReadNewLines();
                            continue;
                        }
                    }

                    if (file.Length != reader.Offset)
                    {
                        events.AddRange(reader.ReadNewLines().SelectMany(TranscriptParser.Parse));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder is being created or cleaned up; the next poll will see it settled.
        }

        _primed = true;
        return events;
    }
}
