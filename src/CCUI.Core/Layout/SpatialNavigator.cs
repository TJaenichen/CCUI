namespace CCUI.Core.Layout;

public enum NavigationDirection
{
    Left,
    Right,
    Up,
    Down,
}

/// <summary>An axis-aligned rectangle in screen coordinates.</summary>
public readonly record struct Bounds(double X, double Y, double Width, double Height)
{
    public double Left => X;

    public double Top => Y;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);
}

/// <summary>
/// Finds the pane in a direction, the way Visual Studio's and tiling window managers' "focus left/right" works:
/// only panes lying in that direction count; panes that line up with the current one (overlap on the other axis)
/// win over panes that do not; then the nearest one; then the one best aligned with the current pane's centre.
/// </summary>
public static class SpatialNavigator
{
    private const double Tolerance = 4;

    public static T? FindNeighbor<T>(Bounds from, IEnumerable<(T Item, Bounds Bounds)> candidates, NavigationDirection direction)
        where T : class
    {
        T? best = null;
        (int Aligned, double Gap, double Overlap, double Offset) bestKey = default;
        foreach (var (item, bounds) in candidates)
        {
            if (bounds == from || !LiesInDirection(from, bounds, direction))
            {
                continue;
            }

            var horizontal = direction is NavigationDirection.Left or NavigationDirection.Right;
            var gap = Math.Max(0, direction switch
            {
                NavigationDirection.Right => bounds.Left - from.Right,
                NavigationDirection.Left => from.Left - bounds.Right,
                NavigationDirection.Down => bounds.Top - from.Bottom,
                _ => from.Top - bounds.Bottom,
            });
            var overlap = horizontal
                ? Math.Min(from.Bottom, bounds.Bottom) - Math.Max(from.Top, bounds.Top)
                : Math.Min(from.Right, bounds.Right) - Math.Max(from.Left, bounds.Left);
            var offset = horizontal ? Math.Abs(bounds.CenterY - from.CenterY) : Math.Abs(bounds.CenterX - from.CenterX);
            var key = (overlap > Tolerance ? 0 : 1, gap, -overlap, offset);
            if (best is null || key.CompareTo(bestKey) < 0)
            {
                best = item;
                bestKey = key;
            }
        }

        return best;
    }

    private static bool LiesInDirection(Bounds from, Bounds candidate, NavigationDirection direction) => direction switch
    {
        NavigationDirection.Right => candidate.CenterX > from.CenterX && candidate.Left >= from.Right - Tolerance - (from.Width / 2),
        NavigationDirection.Left => candidate.CenterX < from.CenterX && candidate.Right <= from.Left + Tolerance + (from.Width / 2),
        NavigationDirection.Down => candidate.CenterY > from.CenterY && candidate.Top >= from.Bottom - Tolerance - (from.Height / 2),
        _ => candidate.CenterY < from.CenterY && candidate.Bottom <= from.Top + Tolerance + (from.Height / 2),
    };
}
