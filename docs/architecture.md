# Architecture

## Projects

| Project | Target | What it holds |
|---|---|---|
| `CCUI.Terminal` | net10.0 | Terminal emulation: VT parser, screen buffer and scrollback, grapheme-aware widths, key encoding, asciicast recording, and the ConPTY connection (Windows at runtime). No UI. |
| `CCUI.Terminal.Wpf` | net10.0-windows | `TerminalControl`, the WPF view of a `TerminalSession`: rendering, input, selection, scrollback. Reusable on its own. |
| `CCUI.Core` | net10.0 | Claude Code integration (transcripts, session catalog, timeline, hooks, launching), layout math, meters, workspace persistence, demo mode, and all view models. No WPF. |
| `CCUI.App` | net10.0-windows | The WPF shell: composition root, views, styles, docking, Windows services (DWM backdrop, WMI, dialogs). |
| `*.Tests` | | Terminal and Core tests run on any OS; App tests render WPF and need Windows. |

Dependencies point one way: App → Core → Terminal, and App → Terminal.Wpf → Terminal.

## How a session works

```
claude.exe ──ConPTY──▶ ConPtyConnection ──bytes──▶ TerminalEmulator ──frame──▶ TerminalControl (WPF)
    ▲                                                                              │ keys, paste
    └──────────────────────────────── TerminalSession.SendText ◀──────────────────┘

~/.claude/projects/<dir>/<session>.jsonl ──poll──▶ FileTranscriptFeed ──events──▶ SessionTimeline ──▶ view models
CCUI.exe --hook (SessionStart/Notification/Stop) ──▶ %LOCALAPPDATA%\CCUI\hooks\<pane>.jsonl ──▶ same feed
```

- **The terminal is CCUI's own.** A native terminal control (the WPF wrapper of Windows Terminal's renderer) is a
  child window that WPF cannot draw over or blend, which rules out transparency, background images and overlays.
  So `CCUI.Terminal` emulates the terminal and `TerminalControl` draws it with WPF glyph runs. Because WPF only
  draws colour fonts in monochrome, emoji are rasterised with Direct2D/DirectWrite and cached. Box drawing and
  block elements are drawn as geometry so borders and bars join seamlessly.
- **ConPTY is bundled.** The app ships Microsoft's current ConPTY (`conpty.dll` + `OpenConsole.exe` from the
  `Microsoft.Windows.Console.ConPTY` package) and asks it to measure characters by grapheme cluster, which is how
  Claude Code lays out its UI. Without the bundled files it falls back to the one built into Windows.
- **State comes from structured data, not the screen.** Claude Code writes every session to a JSONL transcript;
  CCUI tails it for prompts, responses, tool calls and results, token usage and running subagents. Hooks (passed
  with `--settings`) tell a pane when `/clear` switched it to a new session and when Claude is waiting for input.
- **Sessions start clean.** New sessions get a fresh logon environment (as a Start-menu launch does) minus the
  variables a Claude tool shell sets (`NO_COLOR`, `CLAUDE_CODE_CHILD_SESSION`, …), and are always resumed by full
  session id. These are the fixes from the reboot-sessions script, built in.

## View layer

MVVM with CommunityToolkit.Mvvm. View models live in Core and are unit-tested; views are XAML with implicit
DataTemplates per type (e.g. each timeline entry kind), styles in `Themes/`, and code-behind only for view logic
(resizing, focus, docking layout). The shell uses AvalonDock for Visual Studio-style docking; the docking layout is
saved as AvalonDock XML inside the workspace file.

Keyboard shortcuts are handled on every window's `PreviewKeyDown`, before the terminal sees the key. Alt+Arrow
finds the neighbouring pane geometrically (`SpatialNavigator`, unit-tested) across the main and floating windows.

## Key files

- `src/CCUI.Terminal/TerminalEmulator*.cs`: the emulator (print/wrap, CSI/SGR/modes, OSC/DCS).
- `src/CCUI.Terminal/Pty/ConPtyConnection.cs`: process hosting.
- `src/CCUI.Terminal.Wpf/Rendering/RowRenderer.cs`: how a row of cells becomes pixels.
- `src/CCUI.Core/Claude/TranscriptParser.cs`, `SessionTimeline.cs`: from transcript lines to timeline and stats.
- `src/CCUI.Core/ViewModels/ShellViewModel.cs`: opening, closing, restoring sessions.
- `src/CCUI.Core/Demo/`: the demo mode's script, painter and player.
- `src/CCUI.App/Hosting/AppHost.cs`: every registration and the configuration sources.

`src/CCUI.Terminal/Buffer/EmojiData.cs` is generated from Unicode's `emoji-data.txt` (property
`Emoji_Presentation`, merged into ranges).
