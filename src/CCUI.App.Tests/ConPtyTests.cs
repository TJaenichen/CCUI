using System.IO;
using System.Runtime.Versioning;
using CCUI.Terminal;
using CCUI.Terminal.Pty;

namespace CCUI.App.Tests;

/// <summary>
/// Runs real processes through ConPTY (the built-in one and the bundled one) to verify the interop end to end:
/// process start, environment block, output, input, exit code and clean shutdown.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ConPtyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(PseudoConsoleHost.Inbox)]
    [InlineData(PseudoConsoleHost.Bundled)]
    public async Task RunsAProcessAndReportsItsExitCode(PseudoConsoleHost host)
    {
        var environment = WindowsEnvironment.GetFreshUserEnvironment();
        environment["CCUI_TEST_VALUE"] = "hello from the environment block";
        var comSpec = environment.TryGetValue("ComSpec", out var c) ? c : @"C:\Windows\System32\cmd.exe";

        var (screen, exitCode) = await Run(host, comSpec, ["/d", "/c", "echo %CCUI_TEST_VALUE% & exit 7"], environment, input: null);

        Assert.Contains("hello from the environment block", screen, StringComparison.Ordinal);
        Assert.Equal(7, exitCode);
    }

    [Theory]
    [InlineData(PseudoConsoleHost.Inbox)]
    [InlineData(PseudoConsoleHost.Bundled)]
    public async Task SendsInputToTheProcess(PseudoConsoleHost host)
    {
        var environment = WindowsEnvironment.GetFreshUserEnvironment();
        var powershell = Path.Combine(environment["SystemRoot"], @"System32\WindowsPowerShell\v1.0\powershell.exe");

        var (screen, exitCode) = await Run(
            host,
            powershell,
            ["-NoLogo", "-NoProfile", "-Command", "$x = Read-Host 'name'; Write-Output ('got:' + $x)"],
            environment,
            input: "Jänichen 😀\r");

        Assert.Contains("got:Jänichen", screen, StringComparison.Ordinal);
        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData(PseudoConsoleHost.Inbox)]
    [InlineData(PseudoConsoleHost.Bundled)]
    public async Task ControlCharactersArriveAsControlKeys(PseudoConsoleHost host)
    {
        var environment = WindowsEnvironment.GetFreshUserEnvironment();
        var powershell = Path.Combine(environment["SystemRoot"], @"System32\WindowsPowerShell\v1.0\powershell.exe");

        var (screen, exitCode) = await Run(
            host,
            powershell,
            ["-NoLogo", "-NoProfile", "-Command", "[Console]::TreatControlCAsInput = $true; while ($true) { $k = [Console]::ReadKey($true); Write-Output ('key:' + $k.Key + ':' + $k.Modifiers + ':' + [int]$k.KeyChar); if ($k.Key -eq 'Q') { break } }"],
            environment,
            input: "\x1a\x18\x03q");

        Assert.Contains("key:Z:Control:26", screen, StringComparison.Ordinal);
        Assert.Contains("key:X:Control:24", screen, StringComparison.Ordinal);
        Assert.Contains("key:C:Control:3", screen, StringComparison.Ordinal);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void FreshEnvironmentLooksLikeALogon()
    {
        var environment = WindowsEnvironment.GetFreshUserEnvironment();

        Assert.True(environment.ContainsKey("Path"));
        Assert.True(environment.ContainsKey("SystemRoot"));
        Assert.True(environment.ContainsKey("USERPROFILE"));
    }

    private static async Task<(string Screen, int? ExitCode)> Run(PseudoConsoleHost host, string fileName, string[] arguments, IReadOnlyDictionary<string, string> environment, string? input)
    {
        var connection = new ConPtyConnection(new PtyStartInfo(fileName, arguments, Path.GetTempPath(), environment), host);
        await using var session = new TerminalSession(connection, 100, 30, 1000);
        var exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Exited += (_, code) => exited.TrySetResult(code);

        session.Start();
        if (input is not null)
        {
            await Task.Delay(1500, TestContext.Current.CancellationToken);
            session.SendText(input);
        }

        var exitCode = await exited.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        return (session.Emulator.GetScreenText(), exitCode);
    }
}
