using System.Text.Json;

namespace CCUI.Core.Claude;

public interface ISessionCatalog
{
    /// <summary>Sessions active within <paramref name="maxAge"/>, newest first.</summary>
    IReadOnlyList<SessionSummary> Scan(TimeSpan maxAge);
}

/// <summary>
/// Lists sessions from the transcripts under ~/.claude/projects. Each file is summarised with a light pass that only
/// decodes the few lines that matter, and re-read incrementally when it grows. Thread-safe.
/// </summary>
public sealed class SessionCatalog(ClaudePaths paths, TimeProvider time) : ISessionCatalog
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public IReadOnlyList<SessionSummary> Scan(TimeSpan maxAge)
    {
        if (!Directory.Exists(paths.ProjectsDirectory))
        {
            return [];
        }

        var cutoff = time.GetUtcNow() - maxAge;
        var results = new List<SessionSummary>();
        lock (_gate)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in EnumerateTranscripts())
            {
                if (file.LastWriteTimeUtc < cutoff.UtcDateTime || file.Length == 0)
                {
                    continue;
                }

                seen.Add(file.FullName);
                if (!_entries.TryGetValue(file.FullName, out var entry))
                {
                    entry = new Entry(file.FullName);
                    _entries[file.FullName] = entry;
                }

                entry.Update(file);
                results.Add(entry.Summary);
            }

            foreach (var stale in _entries.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _entries.Remove(stale);
            }
        }

        return [.. results.OrderByDescending(s => s.LastActive)];
    }

    private IEnumerable<FileInfo> EnumerateTranscripts()
    {
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(paths.ProjectsDirectory).ToList();
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var directory in directories)
        {
            IEnumerable<FileInfo> files;
            try
            {
                // Only the top level: subagent transcripts live in per-session subfolders.
                files = new DirectoryInfo(directory).EnumerateFiles("*.jsonl").ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (ClaudePaths.IsSessionTranscript(file.Name))
                {
                    yield return file;
                }
            }
        }
    }

    private sealed class Entry(string path)
    {
        private readonly TranscriptReader _reader = new(path);
        private long _length = -1;
        private DateTime _lastWrite;

        public SessionSummary Summary { get; private set; } = new()
        {
            SessionId = System.IO.Path.GetFileNameWithoutExtension(path),
            TranscriptPath = path,
        };

        public void Update(FileInfo file)
        {
            if (file.Length == _length && file.LastWriteTimeUtc == _lastWrite)
            {
                return;
            }

            if (file.Length < _length)
            {
                Summary = Summary with { Title = null, FirstPrompt = null, LastPrompt = null, WorkingDirectory = null };
            }

            _length = file.Length;
            _lastWrite = file.LastWriteTimeUtc;
            Summary = Summary with { LastActive = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero), Length = file.Length };

            string? lastMetadataLine = null;
            foreach (var line in _reader.ReadNewLines())
            {
                if (line.Contains("\"gitBranch\"", StringComparison.Ordinal))
                {
                    lastMetadataLine = line;
                }

                if (line.Contains("\"custom-title\"", StringComparison.Ordinal) && Decode(line, "customTitle") is { Length: > 0 } title)
                {
                    Summary = Summary with { Title = title };
                }
                else if (line.Contains("\"last-prompt\"", StringComparison.Ordinal) && Decode(line, "lastPrompt") is { Length: > 0 } last)
                {
                    Summary = Summary with { LastPrompt = last };
                }
                else if (Summary.FirstPrompt is null && line.Contains("\"type\":\"user\"", StringComparison.Ordinal))
                {
                    Summary = Summary with { FirstPrompt = FirstPrompt(line) };
                }

                if (Summary.WorkingDirectory is null && line.Contains("\"cwd\"", StringComparison.Ordinal))
                {
                    ApplyMetadata(line);
                }
            }

            // The branch can change during a session; the latest line tells the current one.
            if (lastMetadataLine is not null)
            {
                ApplyMetadata(lastMetadataLine);
            }
        }

        private static string? Decode(string line, string property)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                return doc.RootElement.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? FirstPrompt(string line)
        {
            foreach (var e in TranscriptParser.Parse(line))
            {
                if (e is UserPromptEvent { AgentId: null, IsSlashCommand: false } prompt)
                {
                    return prompt.Text;
                }
            }

            return null;
        }

        private void ApplyMetadata(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                string? Get(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                Summary = Summary with
                {
                    WorkingDirectory = Get("cwd") ?? Summary.WorkingDirectory,
                    GitBranch = Get("gitBranch") ?? Summary.GitBranch,
                    Version = Get("version") ?? Summary.Version,
                };
            }
            catch (JsonException)
            {
            }
        }
    }
}
