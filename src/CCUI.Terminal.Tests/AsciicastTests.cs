using System.Text;
using CCUI.Terminal.Recording;

namespace CCUI.Terminal.Tests;

public sealed class AsciicastTests
{
    [Fact]
    public void RoundTripsOutputIncludingSplitUtf8()
    {
        var text = new StringWriter();
        using (var writer = new AsciicastWriter(text, 80, 24, "demo"))
        {
            var bytes = Encoding.UTF8.GetBytes("\e[31mhé😀\r\n");
            writer.WriteOutput(bytes.AsSpan(0, 7));
            writer.WriteOutput(bytes.AsSpan(7));
            writer.WriteResize(100, 30);
        }

        var cast = Asciicast.Parse(new StringReader(text.ToString()));

        Assert.Equal(new AsciicastHeader(80, 24, "demo"), cast.Header);
        Assert.Equal("\e[31mhé😀\r\n", string.Concat(cast.Events.Where(e => e.Code == "o").Select(e => e.Data)));
        Assert.Equal("100x30", cast.Events[^1].Data);
    }
}
