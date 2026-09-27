using System.Globalization;

namespace CCUI.Core.ViewModels;

/// <summary>Short, consistent display strings for times, durations, tokens and model names.</summary>
public static class Format
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string Duration(TimeSpan duration) => duration switch
    {
        { TotalSeconds: < 1 } => $"{(int)duration.TotalMilliseconds}ms",
        { TotalMinutes: < 1 } => duration.TotalSeconds.ToString(duration.TotalSeconds < 10 ? "0.0" : "0", Culture) + "s",
        { TotalHours: < 1 } => $"{(int)duration.TotalMinutes}m {duration.Seconds:00}s",
        _ => $"{(int)duration.TotalHours}h {duration.Minutes:00}m",
    };

    public static string Tokens(long tokens) => tokens switch
    {
        < 1_000 => tokens.ToString(Culture),
        < 1_000_000 => (tokens / 1_000.0).ToString(tokens < 10_000 ? "0.0" : "0", Culture) + "k",
        _ => (tokens / 1_000_000.0).ToString("0.0", Culture) + "M",
    };

    public static string Relative(DateTimeOffset then, DateTimeOffset now)
    {
        var ago = now - then;
        return ago switch
        {
            { TotalSeconds: < 45 } => "just now",
            { TotalMinutes: < 60 } => $"{Math.Max(1, (int)ago.TotalMinutes)}m ago",
            { TotalHours: < 24 } => $"{(int)ago.TotalHours}h ago",
            { TotalDays: < 2 } => "yesterday",
            { TotalDays: < 7 } => $"{(int)ago.TotalDays}d ago",
            _ => then.ToLocalTime().ToString("MMM d", Culture),
        };
    }

    /// <summary>"claude-opus-5-5" → "Opus 5.5", "claude-sonnet-4-5-20250929" → "Sonnet 4.5".</summary>
    public static string ModelName(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return "—";
        }

        var parts = model.Replace("claude-", string.Empty, StringComparison.OrdinalIgnoreCase).Split('-', StringSplitOptions.RemoveEmptyEntries);
        var family = parts.FirstOrDefault(p => !char.IsAsciiDigit(p[0]));
        var version = parts.Where(p => p.Length <= 2 && p.All(char.IsAsciiDigit)).ToList();
        if (family is null)
        {
            return model;
        }

        var name = char.ToUpperInvariant(family[0]) + family[1..];
        return version.Count > 0 ? $"{name} {string.Join('.', version)}" : name;
    }

    /// <summary>The first line of a text, shortened for a one-line intro.</summary>
    public static string FirstLine(string text, int max = 160)
    {
        var line = text.AsSpan().TrimStart();
        var end = line.IndexOfAny('\r', '\n');
        var first = (end >= 0 ? line[..end] : line).ToString().Trim();
        var more = end >= 0 && line[end..].Trim().Length > 0;
        if (first.Length > max)
        {
            return first[..(max - 1)] + "…";
        }

        return more ? first + " …" : first;
    }
}
