namespace CCUI.Core.Claude;

/// <summary>Where Claude Code keeps its data: ~/.claude, or CLAUDE_CONFIG_DIR when set, or an explicit override.</summary>
public sealed class ClaudePaths
{
    public ClaudePaths(string? configDirectory = null)
    {
        Home = !string.IsNullOrWhiteSpace(configDirectory)
            ? Environment.ExpandEnvironmentVariables(configDirectory)
            : Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } fromEnv
                ? fromEnv
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    }

    public string Home { get; }

    public string ProjectsDirectory => Path.Combine(Home, "projects");

    /// <summary>
    /// The project folder name Claude Code derives from a working directory: every character that is not an ASCII
    /// letter or digit becomes '-', e.g. C:\Users\me\src\app → C--Users-me-src-app.
    /// </summary>
    public static string ProjectFolderName(string workingDirectory) =>
        new(workingDirectory.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());

    /// <summary>The transcript path for a session, if the file exists: the expected project folder first, then a search.</summary>
    public string? FindTranscript(string sessionId, string? workingDirectory)
    {
        var fileName = sessionId + ".jsonl";
        if (workingDirectory is not null)
        {
            var expected = Path.Combine(ProjectsDirectory, ProjectFolderName(workingDirectory), fileName);
            if (File.Exists(expected))
            {
                return expected;
            }
        }

        if (!Directory.Exists(ProjectsDirectory))
        {
            return null;
        }

        foreach (var directory in Directory.EnumerateDirectories(ProjectsDirectory))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool IsSessionTranscript(string path) =>
        Path.GetExtension(path).Equals(".jsonl", StringComparison.OrdinalIgnoreCase)
        && Guid.TryParse(Path.GetFileNameWithoutExtension(path), out _);
}
