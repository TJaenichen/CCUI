# CCUI

A Windows desktop app for running many Claude Code sessions side by side. Each session is a real `claude`
process in its own terminal pane (drawn by CCUI, with transparency, background images, colour emoji and
Windows Terminal colour schemes). Around the terminals, CCUI shows what the transcripts say: a session list with
live state and running subagents, per-session stats, activity meters, and a detail view of every prompt, response
and tool call.

- **Panes, Visual Studio style**: dock, tab, split, float and re-dock sessions; Ctrl+Shift+G tiles them.
- **Keyboard first**: Alt+Arrow moves focus to the pane in that direction; the focused pane has a coloured border.
- **Save and restore**: open sessions, the layout and the window come back on the next start (and after a reboot,
  sessions killed by it are flagged with a one-click "Reopen").
- **Demo mode**: scripted sessions that look and behave like Claude Code, for trying the UI without running Claude.

## Run it

Requires Windows 10/11, the .NET 10 SDK and Visual Studio 2026 (or `dotnet`).

1. Open `src/CCUI.sln`.
2. Pick a launch profile in the toolbar: **CCUI** (real sessions) or **CCUI (Demo)** (no Claude needed).
3. Run. Settings live in `src/CCUI.App/appsettings.json`; see [docs/configuration.md](docs/configuration.md).

From a shell: `dotnet run --project src/CCUI.App` (add `--Demo:Enabled=true` for the demo).

## Documentation

- [Architecture](docs/architecture.md): projects, data flow and the main decisions.
- [Configuration](docs/configuration.md): every setting, the demo mode and personal overrides.
- [Testing](docs/testing.md): what is tested where, snapshots, CI and a local runner.
- [Roadmap](docs/roadmap.md): what comes next.
