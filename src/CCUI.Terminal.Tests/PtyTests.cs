using CCUI.Terminal.Pty;

namespace CCUI.Terminal.Tests;

public sealed class PtyTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("with space", "\"with space\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData(@"a\\""b", "\"a\\\\\\\\\\\"b\"")]
    public void QuotesArgumentsForCommandLineToArgv(string argument, string expected)
    {
        Assert.Equal(expected, CommandLine.Quote(argument));
    }

    [Fact]
    public void RunsBatchFilesThroughTheCommandInterpreter()
    {
        var line = CommandLine.ForProcess(@"C:\npm\claude.cmd", ["--resume", "abc"], @"C:\Windows\system32\cmd.exe");

        Assert.Equal(@"C:\Windows\system32\cmd.exe /d /s /c ""C:\npm\claude.cmd --resume abc""", line);
    }

    [Fact]
    public void StartsExecutablesDirectly()
    {
        Assert.Equal(@"""C:\Program Files\x.exe"" -a", CommandLine.ForProcess(@"C:\Program Files\x.exe", ["-a"], "cmd.exe"));
    }

    [Fact]
    public void ResolvesThroughPathAndPathExt()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine("/bin2", "claude.CMD") };
        var env = new Dictionary<string, string> { ["Path"] = "/bin1;\"/bin2\"", ["PATHEXT"] = ".EXE;.CMD" };

        Assert.Equal(Path.Combine("/bin2", "claude.CMD"), ExecutableResolver.Resolve("claude", env, files.Contains));
    }

    [Fact]
    public void PrefersEarlierPathEntries()
    {
        var files = new HashSet<string> { Path.Combine("/a", "claude.EXE"), Path.Combine("/b", "claude.EXE") };
        var env = new Dictionary<string, string> { ["PATH"] = "/a;/b", ["PATHEXT"] = ".EXE" };

        Assert.Equal(Path.Combine("/a", "claude.EXE"), ExecutableResolver.Resolve("claude", env, files.Contains));
    }

    [Fact]
    public void AcceptsExplicitPathsWithExtension()
    {
        var files = new HashSet<string> { "/tools/claude.exe" };

        Assert.Equal("/tools/claude.exe", ExecutableResolver.Resolve("/tools/claude.exe", new Dictionary<string, string>(), files.Contains));
    }

    [Fact]
    public void ReturnsNullWhenNotFound()
    {
        Assert.Null(ExecutableResolver.Resolve("claude", new Dictionary<string, string> { ["PATH"] = "/x" }, _ => false));
    }
}
