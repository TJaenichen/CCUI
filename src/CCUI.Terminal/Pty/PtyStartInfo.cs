namespace CCUI.Terminal.Pty;

/// <param name="FileName">A resolved path to the executable (or batch file).</param>
/// <param name="Arguments">Arguments, unquoted.</param>
/// <param name="WorkingDirectory">The process's starting directory.</param>
/// <param name="Environment">The complete environment of the new process.</param>
public sealed record PtyStartInfo(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment);
