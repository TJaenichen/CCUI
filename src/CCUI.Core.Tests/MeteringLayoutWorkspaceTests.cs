using CCUI.Core.Layout;
using CCUI.Core.Metering;
using CCUI.Core.Workspace;
using Microsoft.Extensions.Time.Testing;

namespace CCUI.Core.Tests;

public sealed class DecayingLevelTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void JumpsOnAHitAndHalvesEveryHalfLife()
    {
        var level = new DecayingLevel(_time, TimeSpan.FromMilliseconds(400), TimeSpan.Zero);

        level.Hit(0.8);
        Assert.Equal(0.8, level.Level, 3);

        _time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal(0.4, level.Level, 3);

        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(0, level.Level);
        Assert.True(level.IsSilent);
    }

    [Fact]
    public void AFloorHoldsTheLevelUntilItIsCleared()
    {
        var level = new DecayingLevel(_time, TimeSpan.FromMilliseconds(400), TimeSpan.Zero);
        level.SetFloor(0.2);
        level.Hit(0.9);

        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.InRange(level.Level, 0.15, 0.2);
        Assert.False(level.IsSilent);

        level.SetFloor(0);
        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(level.IsSilent);
    }

    [Fact]
    public void ASmallerHitDoesNotLowerTheLevel()
    {
        var level = new DecayingLevel(_time, TimeSpan.FromMilliseconds(400), TimeSpan.Zero);
        level.Hit(0.9);

        level.Hit(0.2);

        Assert.Equal(0.9, level.Level, 3);
    }

    [Fact]
    public void PeakHoldsThenFalls()
    {
        var level = new DecayingLevel(_time, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(900));
        level.Hit(1);

        _time.Advance(TimeSpan.FromMilliseconds(800));
        Assert.Equal(1, level.Peak, 3);
        Assert.True(level.Level < 0.01);

        _time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(0.5, level.Peak, 3);
    }

    [Fact]
    public void ScalesSizesLogarithmically()
    {
        Assert.Equal(0, LevelScale.FromSize(0));
        Assert.True(LevelScale.FromSize(50) > 0.3);
        Assert.Equal(1, LevelScale.FromSize(1_000_000));
        Assert.True(LevelScale.FromSize(500) < LevelScale.FromSize(5_000));
    }
}

public sealed class SpatialNavigatorTests
{
    // The mock-up: two columns of panes, detail view on top of each, three terminals below.
    private static readonly (string, Bounds)[] Grid =
    [
        ("L0", new(0, 0, 500, 450)), ("R0", new(510, 0, 500, 450)),
        ("L1", new(0, 460, 500, 260)), ("R1", new(510, 460, 500, 260)),
        ("L2", new(0, 730, 500, 260)), ("R2", new(510, 730, 500, 260)),
    ];

    [Theory]
    [InlineData("L1", NavigationDirection.Right, "R1")]
    [InlineData("R2", NavigationDirection.Left, "L2")]
    [InlineData("L1", NavigationDirection.Up, "L0")]
    [InlineData("L1", NavigationDirection.Down, "L2")]
    [InlineData("R0", NavigationDirection.Down, "R1")]
    [InlineData("L0", NavigationDirection.Left, null)]
    [InlineData("R2", NavigationDirection.Down, null)]
    public void MovesToTheAlignedNeighbour(string from, NavigationDirection direction, string? expected)
    {
        var bounds = Grid.Single(g => g.Item1 == from).Item2;

        Assert.Equal(expected, SpatialNavigator.FindNeighbor(bounds, Grid, direction));
    }

    [Fact]
    public void PrefersPanesThatLineUpOverCloserOnes()
    {
        var from = new Bounds(0, 0, 100, 100);
        (string, Bounds)[] candidates = [("near-but-below", new(105, 150, 100, 100)), ("far-but-level", new(400, 10, 100, 100))];

        Assert.Equal("far-but-level", SpatialNavigator.FindNeighbor(from, candidates, NavigationDirection.Right));
    }
}

public sealed class WorkspaceStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ccui-ws").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void RoundTrips()
    {
        var store = new WorkspaceStore(Path.Combine(_directory, "sub", "workspace.json"));
        var state = new WorkspaceState
        {
            Panes = [new WorkspacePane("p1", "s1", @"C:\src", "Title", DetailOpen: true, DetailHeight: 300)],
            DockLayout = "<Layout/>",
            Window = new WindowPlacement(10, 20, 1200, 800, Maximized: true),
            SessionListWidth = 280,
        };

        store.Save(state);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(state.Panes, loaded.Panes);
        Assert.Equal(state.Window, loaded.Window);
        Assert.Equal("<Layout/>", loaded.DockLayout);
        Assert.False(File.Exists(store.Path + ".tmp"));
    }

    [Fact]
    public void ToleratesMissingAndBrokenFiles()
    {
        var path = Path.Combine(_directory, "workspace.json");
        Assert.Null(new WorkspaceStore(path).Load());

        File.WriteAllText(path, "{ broken");
        Assert.Null(new WorkspaceStore(path).Load());
    }
}
