using System.Text;
using CCUI.Terminal.Parsing;

namespace CCUI.Terminal.Tests;

public sealed class VtParserTests
{
    [Fact]
    public void PrintsTextAndExecutesControls()
    {
        var log = Parse("ab\r\n");

        Assert.Equal(["print a", "print b", "exec 0D", "exec 0A"], log);
    }

    [Fact]
    public void DispatchesCsiWithParametersAndPrivateMarker()
    {
        Assert.Equal(["csi ? [] 1;2004 h"], Parse("\e[?1;2004h"));
        Assert.Equal(["csi  [] ;5 H"], Parse("\e[;5H"));
        Assert.Equal(["csi  [ ] 2 q"], Parse("\e[2 q"));
    }

    [Fact]
    public void KeepsColonSubParameters()
    {
        Assert.Equal(["csi  [] 38:2::255:0:0 m"], Parse("\e[38:2::255:0:0m"));
    }

    [Theory]
    [InlineData("\e]0;title\a", "osc 0;title bel")]
    [InlineData("\e]0;title\e\\", "osc 0;title st")]
    public void DispatchesOscWithEitherTerminator(string input, string expected)
    {
        Assert.Contains(expected, Parse(input));
    }

    [Fact]
    public void DecodesUtf8SplitAcrossReads()
    {
        var handler = new RecordingHandler();
        var parser = new VtParser(handler);
        var bytes = Encoding.UTF8.GetBytes("é😀");

        foreach (var b in bytes)
        {
            parser.Advance([b]);
        }

        Assert.Equal(["print E9", "print 1F600"], handler.Log.Select(l => l.StartsWith("print ", StringComparison.Ordinal) ? $"print {int.Parse(l[6..], System.Globalization.CultureInfo.InvariantCulture):X}" : l));
    }

    [Fact]
    public void ReplacesInvalidUtf8()
    {
        var handler = new RecordingHandler();
        new VtParser(handler).Advance([0x61, 0xFF, 0x62]);

        Assert.Equal(["print a", "print \uFFFD", "print b"], handler.Log.Select(RecordingHandler.Readable));
    }

    [Fact]
    public void EscapeAbortsAnUnfinishedSequence()
    {
        Assert.Equal(["csi  [] 1 m"], Parse("\e[12\e[1m"));
    }

    [Fact]
    public void CancelAbortsAndReturnsToGround()
    {
        Assert.Equal(["exec 18", "print x"], Parse("\e[12\x18x"));
    }

    [Fact]
    public void DispatchesDcsWithData()
    {
        Assert.Equal(["dcs  [$] q data=m"], Parse("\eP$qm\e\\"));
    }

    private static List<string> Parse(string input)
    {
        var handler = new RecordingHandler();
        new VtParser(handler).Advance(Encoding.UTF8.GetBytes(input));
        return handler.Log.Select(RecordingHandler.Readable).Where(l => l != "esc [] \\").ToList();
    }

    private sealed class RecordingHandler : IVtHandler
    {
        public List<string> Log { get; } = [];

        public static string Readable(string entry) =>
            entry.StartsWith("print ", StringComparison.Ordinal) ? "print " + char.ConvertFromUtf32(int.Parse(entry[6..], System.Globalization.CultureInfo.InvariantCulture)) : entry;

        public void Print(int codePoint) => Log.Add($"print {codePoint}");

        public void Execute(int controlCode) => Log.Add($"exec {controlCode:X2}");

        public void EscDispatch(ReadOnlySpan<char> intermediates, char final) => Log.Add($"esc [{intermediates}] {final}");

        public void CsiDispatch(VtParams parameters, char privateMarker, ReadOnlySpan<char> intermediates, char final) =>
            Log.Add($"csi {(privateMarker == '\0' ? string.Empty : privateMarker.ToString())} [{intermediates}] {parameters} {final}");

        public void OscDispatch(string data, bool bellTerminated) => Log.Add($"osc {data} {(bellTerminated ? "bel" : "st")}");

        public void DcsDispatch(VtParams parameters, char privateMarker, ReadOnlySpan<char> intermediates, char final, string data) =>
            Log.Add($"dcs {(privateMarker == '\0' ? string.Empty : privateMarker.ToString())} [{intermediates}] {final} data={data}");
    }
}
