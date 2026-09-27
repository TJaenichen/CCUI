namespace CCUI.Core.Demo;

/// <summary>One scripted exchange: a prompt and what "Claude" does about it.</summary>
public sealed record DemoTurn(string Prompt, IReadOnlyList<DemoAction> Actions);

public abstract record DemoAction;

public sealed record DemoThink(double Seconds, string Text) : DemoAction;

public sealed record DemoSay(string Text) : DemoAction;

public sealed record DemoTool(string Name, string InputJson, string Label, IReadOnlyList<string> Output, double Seconds, bool IsError = false) : DemoAction;

public sealed record DemoAgent(string Description, string AgentType, double Seconds, int ToolUses, string Result) : DemoAction;

/// <summary>A demo project: where it "lives" and the turns it cycles through.</summary>
public sealed record DemoProject(string Name, string WorkingDirectory, string Branch, string Title, IReadOnlyList<DemoTurn> Turns);

/// <summary>The scripted content for demo mode. Plain data: edit freely.</summary>
public static class DemoScript
{
    public static IReadOnlyList<DemoProject> Projects { get; } =
    [
        new(
            "grants",
            @"C:\source\grants",
            "main",
            "Quick-profile card rollout",
            [
                new(
                    "Deploy the quick-profile card and the advisor intake to prod",
                    [
                        new DemoThink(2.5, "The user wants a prod deploy. Check the tree, run the tests, then deploy."),
                        new DemoTool("Bash", """{"command":"git status --short","description":"Check working tree"}""", "git status --short", [" M src/Api/QuickProfile.cs", " M src/Web/Intake.tsx"], 0.8),
                        new DemoTool("Bash", """{"command":"dotnet test","description":"Run the test suite"}""", "dotnet test", ["Passed!  - Failed: 0, Passed: 128, Skipped: 0, Total: 128 - Api.Tests.dll", "Passed!  - Failed: 0, Passed: 41, Skipped: 0, Total: 41 - Web.Tests.dll"], 6.5),
                        new DemoAgent("Find where factors get tagged", "Explore", 9, 14, "Tagging lives in src/Jobs/FactorTagger.cs; it runs per field."),
                        new DemoTool("Edit", """{"file_path":"src/Api/QuickProfile.cs","old_string":"fields","new_string":"taggedFields"}""", "src/Api/QuickProfile.cs", ["Updated src/Api/QuickProfile.cs with 3 additions and 1 removal"], 1.2),
                        new DemoTool("Bash", """{"command":"az containerapp update --name ge-api","description":"Deploy revision"}""", "az containerapp update --name ge-api", ["Revision ge-api--0000021 is healthy and taking 100% of traffic."], 12),
                        new DemoSay("The deploy is live ✅\n\n- Revision ge-api--0000021 is healthy and takes 100% of traffic.\n- Smoke checks: health returns 200 and the quick-profile endpoint requires sign-in.\n- Factor tagging runs in the background on Opus, about $4. 🎉"),
                    ]),
                new(
                    "How many fields were tagged overnight?",
                    [
                        new DemoTool("Bash", """{"command":"sqlcmd -Q \"select count(*) from FactorTags\"","description":"Count tags"}""", "sqlcmd -Q \"select count(*) from FactorTags\"", ["-----------", "       2417", "(1 row affected)"], 2.2),
                        new DemoSay("2,417 fields are tagged: 1,902 to a factor, 431 none, 84 unclear. 📊"),
                    ]),
            ]),
        new(
            "CCUI",
            @"C:\source\CCUI",
            "feature/emoji",
            "Colour emoji in the terminal",
            [
                new(
                    "Render emoji in colour in the terminal control",
                    [
                        new DemoThink(3, "WPF draws colour fonts in monochrome. Rasterise emoji with DirectWrite and cache the bitmaps."),
                        new DemoTool("Read", """{"file_path":"src/CCUI.Terminal.Wpf/Rendering/RowRenderer.cs"}""", "src/CCUI.Terminal.Wpf/Rendering/RowRenderer.cs", ["Read 212 lines"], 0.4),
                        new DemoTool("Grep", """{"pattern":"DrawGlyphRun","path":"src"}""", "\"DrawGlyphRun\"", ["Found 2 files", "src/CCUI.Terminal.Wpf/Rendering/RowRenderer.cs", "src/CCUI.Terminal.Wpf/Rendering/TerminalSurface.cs"], 0.6),
                        new DemoTool("Write", """{"file_path":"src/CCUI.Terminal.Wpf/Rendering/ColorEmojiRasterizer.cs"}""", "src/CCUI.Terminal.Wpf/Rendering/ColorEmojiRasterizer.cs", ["Wrote 96 lines to src/CCUI.Terminal.Wpf/Rendering/ColorEmojiRasterizer.cs"], 1.5),
                        new DemoTool("Bash", """{"command":"dotnet build src/CCUI.sln","description":"Build"}""", "dotnet build src/CCUI.sln", ["ColorEmojiRasterizer.cs(77,55): error CS0104: 'FactoryType' is an ambiguous reference"], 7, IsError: true),
                        new DemoTool("Edit", """{"file_path":"src/CCUI.Terminal.Wpf/Rendering/ColorEmojiRasterizer.cs"}""", "src/CCUI.Terminal.Wpf/Rendering/ColorEmojiRasterizer.cs", ["Updated with 1 addition and 1 removal"], 0.8),
                        new DemoTool("Bash", """{"command":"dotnet build src/CCUI.sln","description":"Build"}""", "dotnet build src/CCUI.sln", ["Build succeeded.", "    0 Warning(s)", "    0 Error(s)"], 6),
                        new DemoSay("Emoji now render in colour: 😀 👩‍💻 🇩🇪 ❤️ 👍🏽\nThey are rasterised once per size with Direct2D and cached; box drawing stays procedural: ╭─╮ │ ╰─╯ ▐▛███▜▌"),
                    ]),
            ]),
        new(
            "homelab",
            @"C:\source\homelab",
            "main",
            "Nightly backup failures",
            [
                new(
                    "Why did the nightly backup fail?",
                    [
                        new DemoTool("Bash", """{"command":"ssh nas journalctl -u backup --since yesterday","description":"Read the backup log"}""", "ssh nas journalctl -u backup --since yesterday", ["backup[2210]: rsync: [receiver] write failed: No space left on device (28)", "backup[2210]: rsync error: error in file IO (code 11)"], 3.5, IsError: true),
                        new DemoTool("Bash", """{"command":"ssh nas df -h /mnt/backup","description":"Check free space"}""", "ssh nas df -h /mnt/backup", ["Filesystem  Size  Used Avail Use% Mounted on", "/dev/sdb1   3.6T  3.6T     0 100% /mnt/backup"], 1.4),
                        new DemoSay("The backup disk is full (3.6T of 3.6T) ⚠️\nOld snapshots are never pruned. Add `--delete-after` and keep 14 dailies? 🧹"),
                    ]),
            ]),
        new(
            "blog",
            @"C:\source\blog",
            "drafts",
            "Post: hosting ConPTY",
            [
                new(
                    "Draft a short post about hosting ConPTY from .NET",
                    [
                        new DemoThink(2, "Outline: why ConPTY, the five Win32 calls, the drain-on-close gotcha."),
                        new DemoTool("WebSearch", """{"query":"ClosePseudoConsole drain output pipe"}""", "ClosePseudoConsole drain output pipe", ["Did 1 search in 1.9s"], 2),
                        new DemoTool("Write", """{"file_path":"posts/2026-09-conpty.md"}""", "posts/2026-09-conpty.md", ["Wrote 84 lines to posts/2026-09-conpty.md"], 1.6),
                        new DemoSay("Drafted posts/2026-09-conpty.md 📝✨\nSections: why host it yourself, the five calls, draining on close, and grapheme widths."),
                    ]),
            ]),
    ];
}
