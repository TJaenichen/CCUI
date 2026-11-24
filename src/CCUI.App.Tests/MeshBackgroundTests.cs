using System.Windows;
using CCUI.App.Controls;

namespace CCUI.App.Tests;

[Collection("Wpf")]
public sealed class MeshBackgroundTests(WpfFixture wpf)
{
    [Fact]
    public Task TheMeshDrawsDotsAndColouredFacesAndNothingAtZeroStrength() => wpf.Run(() =>
    {
        var mesh = new MeshBackground { Strength = 1, IsAnimated = false, Columns = 96 };
        var lit = WpfFixture.Render(mesh, 900, 560, "mesh-background");
        Assert.True(lit.CountDistinctColours() > 40, "dots, lines and faces should give many shades");
        Assert.True(lit.HasColourIn(new Int32Rect(0, 200, 900, 360)), "the swell's faces should be coloured");
        Assert.False(lit.HasColourIn(new Int32Rect(0, 0, 900, 120)), "the sky stays empty");

        var off = WpfFixture.Render(new MeshBackground { Strength = 0, IsAnimated = false }, 300, 200, "mesh-background-off");
        Assert.Equal(1, off.CountDistinctColours());
        return Task.CompletedTask;
    });

    [Fact]
    public void TheSurfaceStaysBoundedAndMoves()
    {
        var samples = new List<double>();
        for (var z = 1.0; z <= 9; z += 0.5)
        {
            for (var x = -4.0; x <= 4; x += 0.5)
            {
                samples.Add(MeshBackground.Surface(x, z, 0));
                samples.Add(MeshBackground.Surface(x, z, 37.5));
            }
        }

        Assert.All(samples, h => Assert.InRange(Math.Abs(h), 0, 1.2));
        Assert.True(samples.Max() > 0.3, "the swell should lift the surface somewhere");
        Assert.NotEqual(MeshBackground.Surface(0.5, 5, 0), MeshBackground.Surface(0.5, 5, 3));
    }
}
