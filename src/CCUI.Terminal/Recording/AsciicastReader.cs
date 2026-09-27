using System.Text.Json;

namespace CCUI.Terminal.Recording;

public sealed record AsciicastHeader(int Width, int Height, string? Title);

/// <summary>One recorded event: seconds since the start, the event code ("o" output, "i" input, "r" resize) and its data.</summary>
public sealed record AsciicastEvent(double Time, string Code, string Data);

public sealed record Asciicast(AsciicastHeader Header, IReadOnlyList<AsciicastEvent> Events)
{
    public static Asciicast Parse(TextReader reader)
    {
        var headerLine = reader.ReadLine() ?? throw new FormatException("Empty asciicast.");
        using var header = JsonDocument.Parse(headerLine);
        var root = header.RootElement;
        if (!root.TryGetProperty("version", out var version) || version.GetInt32() != 2)
        {
            throw new FormatException("Only asciicast v2 is supported.");
        }

        var events = new List<AsciicastEvent>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var e = doc.RootElement;
            events.Add(new AsciicastEvent(e[0].GetDouble(), e[1].GetString() ?? string.Empty, e[2].GetString() ?? string.Empty));
        }

        return new Asciicast(
            new AsciicastHeader(
                root.GetProperty("width").GetInt32(),
                root.GetProperty("height").GetInt32(),
                root.TryGetProperty("title", out var title) ? title.GetString() : null),
            events);
    }
}
