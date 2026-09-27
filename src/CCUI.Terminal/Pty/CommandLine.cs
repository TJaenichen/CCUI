using System.Text;

namespace CCUI.Terminal.Pty;

/// <summary>Builds Windows command lines that CommandLineToArgvW (and the MSVC runtime) split back into the same arguments.</summary>
public static class CommandLine
{
    public static string Build(string fileName, IEnumerable<string> arguments)
    {
        var sb = new StringBuilder(Quote(fileName));
        foreach (var argument in arguments)
        {
            sb.Append(' ').Append(Quote(argument));
        }

        return sb.ToString();
    }

    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.AsSpan().IndexOfAny(" \t\n\v\"") < 0)
        {
            return argument;
        }

        var sb = new StringBuilder(argument.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                // Backslashes before a quote are doubled, and the quote itself is escaped.
                sb.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        // Trailing backslashes are doubled so they do not escape the closing quote.
        return sb.Append('\\', backslashes * 2).Append('"').ToString();
    }

    /// <summary>
    /// The command line to start <paramref name="fileName"/>. Batch files (.cmd/.bat) cannot be started directly and
    /// are run through the command interpreter.
    /// </summary>
    public static string ForProcess(string fileName, IReadOnlyList<string> arguments, string commandInterpreter)
    {
        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return Build(fileName, arguments);
        }

        // /s strips the outer quotes, so the inner command line is passed through verbatim.
        return $"{Quote(commandInterpreter)} /d /s /c \"{Build(fileName, arguments)}\"";
    }
}
