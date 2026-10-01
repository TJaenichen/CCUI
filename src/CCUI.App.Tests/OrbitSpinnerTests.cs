using System.Numerics;
using System.Windows;
using System.Windows.Media;
using CCUI.App.Controls;

namespace CCUI.App.Tests;

[Collection("Wpf")]
public sealed class OrbitSpinnerTests(WpfFixture wpf)
{
    [Theory]
    [InlineData(0)]
    [InlineData(1.3)]
    [InlineData(57.9)]
    public void DotsStayOnTheOrbitEvenlySpaced(double seconds)
    {
        var dots = OrbitSpinner.Positions(seconds);

        Assert.Equal(3, dots.Length);
        Assert.All(dots, d => Assert.Equal(1, d.Length(), 3));

        // Evenly spaced: every pair is 120° apart, wherever the plane is turned.
        Assert.Equal(Math.Sqrt(3), Vector3.Distance(dots[0], dots[1]), 3);
        Assert.Equal(Math.Sqrt(3), Vector3.Distance(dots[1], dots[2]), 3);
    }

    [Fact]
    public void ThePlaneTumblesOverTime()
    {
        static Vector3 Normal(double t)
        {
            var d = OrbitSpinner.Positions(t);
            return Vector3.Normalize(Vector3.Cross(d[1] - d[0], d[2] - d[0]));
        }

        // The orbit's plane faces noticeably different ways over a few seconds, and the dots move in depth.
        var normals = Enumerable.Range(0, 8).Select(i => Normal(i * 1.5)).ToList();
        Assert.Contains(normals, n => Math.Abs(Vector3.Dot(n, normals[0])) < 0.8);
        var depths = Enumerable.Range(0, 40).Select(i => OrbitSpinner.Positions(i * 0.1)[0].Z).ToList();
        Assert.True(depths.Max() - depths.Min() > 0.5);
    }

    [Fact]
    public Task DrawsOrangeDots() => wpf.Run(() =>
    {
        var spinner = new OrbitSpinner { Width = 28, Height = 28, Fill = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x57)), IsSpinning = false };

        var snapshot = WpfFixture.Render(spinner, 28, 28, "orbit-spinner");

        Assert.True(snapshot.HasColourIn(new Int32Rect(0, 0, 28, 28)), "The spinner should draw its dots.");
        return Task.CompletedTask;
    });
}
