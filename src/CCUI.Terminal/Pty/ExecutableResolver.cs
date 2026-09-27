namespace CCUI.Terminal.Pty;

/// <summary>Finds an executable the way a shell would: via PATH and, for names without an extension, PATHEXT.</summary>
public static class ExecutableResolver
{
    private const string DefaultPathExt = ".COM;.EXE;.BAT;.CMD";

    public static string? Resolve(string command, IReadOnlyDictionary<string, string> environment, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var extensions = Get(environment, "PATHEXT", DefaultPathExt)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hasExtension = Path.HasExtension(command);

        IEnumerable<string> Candidates(string basePath) =>
            hasExtension ? [basePath, .. extensions.Select(e => basePath + e)] : extensions.Select(e => basePath + e);

        if (Path.IsPathRooted(command) || command.Contains('\\') || command.Contains('/'))
        {
            return Candidates(command).FirstOrDefault(fileExists);
        }

        foreach (var directory in Get(environment, "PATH", string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Candidates(Path.Combine(directory.Trim('"'), command)).FirstOrDefault(fileExists);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string Get(IReadOnlyDictionary<string, string> environment, string name, string fallback)
    {
        foreach (var (key, value) in environment)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return fallback;
    }
}
