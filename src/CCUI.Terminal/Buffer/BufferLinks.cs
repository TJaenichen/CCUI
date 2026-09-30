using System.Text;
using System.Text.RegularExpressions;

namespace CCUI.Terminal.Buffer;

/// <summary>A URL found in the buffer, from its first to its last cell (inclusive).</summary>
public sealed record BufferLink(string Uri, BufferPosition Start, BufferPosition End);

/// <summary>Finds URLs in the buffer text, as Windows Terminal does for Ctrl+click.</summary>
public static partial class BufferLinks
{
    private const string TrailingPunctuation = ".,;:!?'\"*_";

    /// <summary>
    /// The URL under <paramref name="position"/>, or null. A URL may run across soft-wrapped lines; box-drawing
    /// characters and blanks end it.
    /// </summary>
    public static BufferLink? LinkAt(ScreenBuffer buffer, BufferPosition position)
    {
        if (position.Line < 0 || position.Line >= buffer.TotalLines)
        {
            return null;
        }

        // The logical line: the hit line plus the soft-wrapped lines before and after it.
        var first = position.Line;
        while (first > 0 && buffer.GetLine(first - 1).IsWrapped)
        {
            first--;
        }

        var last = position.Line;
        while (last < buffer.TotalLines - 1 && buffer.GetLine(last).IsWrapped)
        {
            last++;
        }

        // One char per cell (wide trails map onto their lead), so a text offset maps back to a cell.
        var text = new StringBuilder();
        var cells = new List<BufferPosition>();
        var hit = -1;
        for (var lineIndex = first; lineIndex <= last; lineIndex++)
        {
            var line = buffer.GetLine(lineIndex);
            for (var column = 0; column < line.Length; column++)
            {
                var cell = line[column];
                if (lineIndex == position.Line && column == position.Column)
                {
                    hit = cell.Width == CellWidth.WideTrail ? cells.Count - 1 : cells.Count;
                }

                if (cell.Width == CellWidth.WideTrail)
                {
                    continue;
                }

                text.Append(cell.Text is { Length: 1 } single ? single[0] : ' ');
                cells.Add(new BufferPosition(lineIndex, column));
            }
        }

        if (hit < 0)
        {
            return null;
        }

        foreach (Match match in UrlPattern().Matches(text.ToString()))
        {
            var length = TrimmedLength(match.Value);
            if (hit >= match.Index && hit < match.Index + length)
            {
                var end = cells[match.Index + length - 1];
                var endCell = buffer.GetLine(end.Line)[end.Column];
                var endColumn = endCell.Width == CellWidth.WideLead ? end.Column + 1 : end.Column;
                return new BufferLink(match.Value[..length], cells[match.Index], end with { Column = endColumn });
            }
        }

        return null;
    }

    /// <summary>The URL without trailing sentence punctuation or an unbalanced closing bracket.</summary>
    private static int TrimmedLength(string url)
    {
        var length = url.Length;
        while (length > 0)
        {
            var c = url[length - 1];
            if (TrailingPunctuation.Contains(c, StringComparison.Ordinal)
                || (c == ')' && Count(url, length, '(') < Count(url, length, ')'))
                || (c == ']' && Count(url, length, '[') < Count(url, length, ']')))
            {
                length--;
                continue;
            }

            break;
        }

        return length;
    }

    private static int Count(string text, int length, char c) => text.AsSpan(0, length).Count(c);

    [GeneratedRegex(@"\b(?:https?|file)://[^\s<>""'`{}|\\^─-▟]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();
}
