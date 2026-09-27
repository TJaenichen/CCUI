using System.Text;

namespace CCUI.Terminal.Buffer;

/// <summary>A position in a <see cref="ScreenBuffer"/>: an absolute line index (scrollback first) and a column.</summary>
public readonly record struct BufferPosition(int Line, int Column) : IComparable<BufferPosition>
{
    public int CompareTo(BufferPosition other) => Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);

    public static bool operator <(BufferPosition left, BufferPosition right) => left.CompareTo(right) < 0;

    public static bool operator >(BufferPosition left, BufferPosition right) => left.CompareTo(right) > 0;

    public static bool operator <=(BufferPosition left, BufferPosition right) => left.CompareTo(right) <= 0;

    public static bool operator >=(BufferPosition left, BufferPosition right) => left.CompareTo(right) >= 0;
}

/// <summary>Extracts text for copy and finds word boundaries for double-click selection.</summary>
public static class BufferText
{
    /// <summary>Windows Terminal's default word delimiters.</summary>
    public const string DefaultWordDelimiters = " /\\()\"'-.,:;<>~!@#$%^&*|+=[]{}?│";

    /// <summary>
    /// The text from <paramref name="start"/> to <paramref name="end"/> (both inclusive). Soft-wrapped lines are
    /// joined; hard line breaks become <paramref name="newLine"/>; trailing blanks on each line are dropped.
    /// </summary>
    public static string Extract(ScreenBuffer buffer, BufferPosition start, BufferPosition end, string newLine = "\r\n")
    {
        if (end < start)
        {
            (start, end) = (end, start);
        }

        var sb = new StringBuilder();
        var last = Math.Min(end.Line, buffer.TotalLines - 1);
        for (var lineIndex = Math.Max(0, start.Line); lineIndex <= last; lineIndex++)
        {
            var line = buffer.GetLine(lineIndex);
            var from = lineIndex == start.Line ? start.Column : 0;
            var to = lineIndex == end.Line ? end.Column + 1 : line.Length;
            sb.Append(line.GetText(from, to));
            if (lineIndex < last && !(line.IsWrapped && to >= line.Length))
            {
                sb.Append(newLine);
            }
        }

        return sb.ToString();
    }

    /// <summary>The inclusive column range of the word around <paramref name="column"/>.</summary>
    public static (int Start, int End) WordAt(TerminalLine line, int column, string delimiters = DefaultWordDelimiters)
    {
        column = Math.Clamp(column, 0, line.Length - 1);
        var isDelimiter = IsDelimiter(line, column, delimiters);
        var start = column;
        while (start > 0 && IsDelimiter(line, start - 1, delimiters) == isDelimiter)
        {
            start--;
        }

        var end = column;
        while (end < line.Length - 1 && IsDelimiter(line, end + 1, delimiters) == isDelimiter)
        {
            end++;
        }

        return (start, end);
    }

    private static bool IsDelimiter(TerminalLine line, int column, string delimiters)
    {
        var cell = line[column];
        if (cell.Width == CellWidth.WideTrail && column > 0)
        {
            cell = line[column - 1];
        }

        var text = cell.DisplayText;
        return text.Length == 1 && delimiters.Contains(text[0], StringComparison.Ordinal);
    }
}
