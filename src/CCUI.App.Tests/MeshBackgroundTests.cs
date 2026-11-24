using System.Diagnostics;
using System.IO;
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
        mesh.RenderNow(900, 560);
        var lit = WpfFixture.Render(mesh, 900, 560, "mesh-background");
        Assert.True(lit.CountDistinctColours() > 40, "dots, lines and faces should give many shades");
        Assert.True(lit.HasColourIn(new Int32Rect(0, 200, 900, 360)), "the swell's faces should be coloured");
        Assert.False(lit.HasColourIn(new Int32Rect(0, 0, 900, 120)), "the sky stays empty");

        var off = new MeshBackground { Strength = 0, IsAnimated = false };
        off.RenderNow(300, 200);
        Assert.Equal(1, WpfFixture.Render(off, 300, 200, "mesh-background-off").CountDistinctColours());
        return Task.CompletedTask;
    });

    /// <summary>A frame at the largest bitmap the control uses must be cheap, or the mesh is not worth having.</summary>
    [Fact]
    public void AFullSizeFrameRastersQuickly()
    {
        var rasterizer = new MeshRasterizer();
        var frame = new MeshFrame(1280, 800, 12.5, 1, 120, (0xFF, 0x3B, 0x5C), (0x3B, 0xB3, 0xFF));
        var pixels = new byte[1280 * 800 * 4];
        rasterizer.Render(frame, pixels); // warm up: allocations and JIT
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++)
        {
            rasterizer.Render(frame with { Time = 12.5 + i }, pixels);
        }

        var perFrame = watch.Elapsed.TotalMilliseconds / 5;
        Directory.CreateDirectory(WpfFixture.SnapshotDirectory);
        File.WriteAllText(Path.Combine(WpfFixture.SnapshotDirectory, "mesh-frame-ms.txt"), perFrame.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(perFrame < 150, $"a frame took {perFrame:0} ms"); // about 3x slower in Debug than in Release
        Assert.Contains(pixels, b => b != 0);
    }

    [Fact]
    public void TheSurfaceStaysBoundedAndMoves()
    {
        var samples = new List<double>();
        for (var z = 1.0; z <= 9; z += 0.5)
        {
            for (var x = -4.0; x <= 4; x += 0.5)
            {
                samples.Add(MeshRasterizer.Surface(x, z, 0));
                samples.Add(MeshRasterizer.Surface(x, z, 37.5));
            }
        }

        Assert.All(samples, h => Assert.InRange(Math.Abs(h), 0, 1.2));
        Assert.True(samples.Max() > 0.3, "the swell should lift the surface somewhere");
        Assert.NotEqual(MeshRasterizer.Surface(0.5, 5, 0), MeshRasterizer.Surface(0.5, 5, 3));
    }
}
