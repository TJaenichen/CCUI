using System.Globalization;
using System.Text;
using CCUI.Terminal.Buffer;

namespace CCUI.Core.Demo;

/// <summary>The live part at the bottom of the demo screen: spinner, input box and status lines.</summary>
internal sealed record DemoBottomState(
    string? SpinnerVerb,
    int SpinnerFrame,
    TimeSpan Elapsed,
    long TurnTokens,
    string Input,
    string Project,
    long TotalTokens,
    int ContextPercent,
    int UsagePercent,
    int RunningAgents);

/// <summary>
/// Paints screens that look like Claude Code's terminal UI, using the same kinds of escape sequences it emits
/// (true colour, box drawing, block elements, emoji, synchronized output). Only used by demo mode.
/// </summary>
internal sealed class ClaudeTuiPainter
{
    private const string Reset = "\e[0m";
    private const string Bold = "\e[1m";
    private static readonly string[] SpinnerGlyphs = ["·", "✢", "✳", "✶", "✻", "✽", "✻", "✶", "✳", "✢"];

    private static readonly string Orange = Fg(215, 119, 87);
    private static readonly string Gray = Fg(153, 153, 153);
    private static readonly string DarkGray = Fg(102, 102, 102);
    private static readonly string Green = Fg(78, 186, 101);
    private static readonly string Red = Fg(255, 107, 128);
    private static readonly string White = Fg(255, 255, 255);
    private static readonly string Cyan = Fg(86, 182, 194);
    private static readonly string Yellow = Fg(229, 192, 123);
    private static readonly string Pink = Fg(255, 95, 135);
    private static readonly string BarGreen = Fg(152, 195, 121);
    private static readonly string BarBlue = Fg(97, 175, 239);

    public int Width { get; set; } = 120;

    public static string Fg(int r, int g, int b) => string.Create(CultureInfo.InvariantCulture, $"\e[38;2;{r};{g};{b}m");

    public string Welcome(string project, string workingDirectory)
    {
        var inner = Math.Clamp(Width - 4, 40, 62);
        var sb = new StringBuilder();
        var title = " Claude Code v2.1.283 ";
        sb.Append(Orange).Append("╭───").Append(title).Append(new string('─', Math.Max(0, inner - 3 - title.Length))).Append("╮\r\n");
        void Row(string text, string color)
        {
            var pad = inner - DisplayWidth(text);
            var left = pad / 2;
            sb.Append(Orange).Append('│').Append(Reset)
              .Append(new string(' ', left)).Append(color).Append(text).Append(Reset)
              .Append(new string(' ', pad - left)).Append(Orange).Append('│').Append("\r\n");
        }

        Row(string.Empty, White);
        Row("Welcome back!", Bold + White);
        Row(string.Empty, White);
        Row(" ▐▛███▜▌ ", Orange);
        Row("▝▜█████▛▘", Orange);
        Row("  ▘▘ ▝▝  ", Orange);
        Row(string.Empty, White);
        Row($"Opus 5.5 · Claude Max · {workingDirectory}", Gray);
        Row(string.Empty, White);
        sb.Append(Orange).Append('╰').Append(new string('─', inner)).Append('╯').Append(Reset).Append("\r\n\r\n");
        sb.Append(Gray).Append($"  Demo session for {project}. Type a prompt and press Enter; Shift+Enter adds a line.").Append(Reset).Append("\r\n");
        return sb.ToString();
    }

    public string Prompt(string text)
    {
        var sb = new StringBuilder("\r\n");
        var first = true;
        foreach (var line in text.Split('\n'))
        {
            sb.Append(Gray).Append(first ? "> " : "  ").Append(line).Append(Reset).Append("\r\n");
            first = false;
        }

        return sb.ToString();
    }

    public string Say(string text)
    {
        var sb = new StringBuilder("\r\n");
        var first = true;
        foreach (var paragraph in text.Split('\n'))
        {
            foreach (var line in Wrap(paragraph, Width - 4))
            {
                sb.Append(first ? White + "⏺ " + Reset : "  ").Append(line).Append("\r\n");
                first = false;
            }
        }

        return sb.ToString();
    }

    public string Tool(DemoTool tool)
    {
        var dot = tool.IsError ? Red : Green;
        var sb = new StringBuilder("\r\n");
        sb.Append(dot).Append("⏺ ").Append(Reset).Append(Bold).Append(tool.Name).Append(Reset).Append('(').Append(Clip(tool.Label, Width - tool.Name.Length - 6)).Append(")\r\n");
        var shown = tool.Output.Take(3).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            sb.Append(i == 0 ? Gray + "  ⎿  " : "     ").Append(tool.IsError ? Red : Gray).Append(Clip(shown[i], Width - 7)).Append(Reset).Append("\r\n");
        }

        if (tool.Output.Count > 3)
        {
            sb.Append(DarkGray).Append($"     … +{tool.Output.Count - 3} lines (ctrl+o to expand)").Append(Reset).Append("\r\n");
        }

        return sb.ToString();
    }

    public string Agent(DemoAgent agent, long tokens)
    {
        var sb = new StringBuilder("\r\n");
        sb.Append(Green).Append("⏺ ").Append(Reset).Append(Bold).Append(agent.AgentType).Append(Reset).Append('(').Append(agent.Description).Append(")\r\n");
        sb.Append(Gray).Append("  ⎿  Done (").Append(agent.ToolUses).Append(" tool uses · ").Append(Tokens(tokens)).Append(" tokens · ")
          .Append(agent.Seconds.ToString("0.#", CultureInfo.InvariantCulture)).Append("s)").Append(Reset).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>The bottom lines and which of them holds the input caret (its line index and column).</summary>
    public (IReadOnlyList<string> Lines, int InputLine, int InputColumn) Bottom(DemoBottomState state)
    {
        var lines = new List<string>();
        if (state.SpinnerVerb is { } verb)
        {
            var glyph = SpinnerGlyphs[state.SpinnerFrame % SpinnerGlyphs.Length];
            lines.Add(string.Empty);
            lines.Add($"{Orange}{glyph} {verb}…{Reset}{Gray} ({(int)state.Elapsed.TotalSeconds}s · ↓ {Tokens(state.TurnTokens)} tokens · esc to interrupt){Reset}");
        }

        lines.Add(string.Empty);
        var label = $" {state.Project} ─";
        lines.Add(DarkGray + new string('─', Math.Max(0, Width - label.Length - 1)) + label + Reset);

        var inputLines = state.Input.Split('\n');
        var inputLine = lines.Count + inputLines.Length - 1;
        for (var i = 0; i < inputLines.Length; i++)
        {
            lines.Add((i == 0 ? White + "❯ " + Reset : "  ") + inputLines[i]);
        }

        var inputColumn = 2 + DisplayWidth(inputLines[^1]);
        lines.Add(DarkGray + new string('─', Math.Max(0, Width - 1)) + Reset);

        var left = $"  {Cyan}[Opus 5.5 (1M context) | Max]{Reset} | {Yellow}demo{Reset} | {state.Project}";
        var right = $"{state.TotalTokens.ToString("N0", CultureInfo.InvariantCulture)} tokens";
        var gap = Math.Max(1, Width - 1 - DisplayWidth(StripAnsi(left)) - right.Length);
        lines.Add(left + new string(' ', gap) + Gray + right + Reset);
        lines.Add($"  Context {Bar(state.ContextPercent, BarGreen)} {state.ContextPercent}% | Usage {Bar(state.UsagePercent, BarBlue)} {state.UsagePercent}%");
        var agents = state.RunningAgents > 0 ? $" · {state.RunningAgents} agent{(state.RunningAgents == 1 ? string.Empty : "s")} running" : string.Empty;
        lines.Add($"  {Pink}⏵⏵ bypass permissions on{Reset}{Gray}{agents} · ← for agents{Reset}");

        // Never let a line wrap: the redraw moves the cursor by line count, as Claude Code's renderer does.
        return ([.. lines.Select(line => Fit(line, Width - 1))], inputLine, Math.Min(inputColumn, Width - 1));
    }

    /// <summary>Cuts a line with escape sequences to <paramref name="columns"/> display columns.</summary>
    public static string Fit(string line, int columns)
    {
        var sb = new StringBuilder(line.Length);
        var width = 0;
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] == '\e')
            {
                var start = i++;
                while (i < line.Length && !char.IsAsciiLetter(line[i]))
                {
                    i++;
                }

                i = Math.Min(i + 1, line.Length);
                sb.Append(line, start, i - start);
                continue;
            }

            var length = char.IsSurrogatePair(line, i) ? 2 : 1;
            var cellWidth = Math.Max(0, Graphemes.Width(char.ConvertToUtf32(line, i)));
            if (width + cellWidth > columns)
            {
                break;
            }

            sb.Append(line, i, length);
            width += cellWidth;
            i += length;
        }

        return sb.Append(Reset).ToString();
    }

    public static int DisplayWidth(string text)
    {
        var width = 0;
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            width += Graphemes.ClusterWidth((string)enumerator.Current);
        }

        return width;
    }

    private static string Tokens(long tokens) =>
        tokens >= 1000 ? (tokens / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k" : tokens.ToString(CultureInfo.InvariantCulture);

    private static string Bar(int percent, string color)
    {
        var filled = Math.Clamp(percent / 10, 0, 10);
        return color + new string('█', filled) + DarkGray + new string('░', 10 - filled) + Reset;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..Math.Max(1, max - 1)] + "…";

    private static string StripAnsi(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\e')
            {
                while (i < text.Length && !char.IsAsciiLetter(text[i]))
                {
                    i++;
                }

                continue;
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        if (text.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && DisplayWidth(line.ToString()) + 1 + DisplayWidth(word) > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        yield return line.ToString();
    }
}
