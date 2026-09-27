namespace CCUI.Core.Claude;

public static class PathNames
{
    /// <summary>The last segment of a path, splitting on both separators (transcripts carry Windows paths on any OS).</summary>
    public static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var index = trimmed.LastIndexOfAny(['\\', '/']);
        return index >= 0 ? trimmed[(index + 1)..] : trimmed;
    }
}
