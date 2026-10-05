# CCUI

**Run many Claude Code sessions side by side, in a native Windows app that knows what each one is doing.**

CCUI is a WPF desktop app. Every session is a real `claude` process in its own terminal pane, drawn by CCUI
itself with transparency, a background image, colour emoji and Windows Terminal colour schemes. Around the
terminals, CCUI reads the session transcripts and shows what is going on: which sessions are working, waiting
for you or running subagents, how much context each one has used, and every prompt, response and tool call in a
detail view. Close it and open it again later and everything is back: the sessions, the pane layout and the
window.

## Why native

- **WPF, not a browser in a frame.** No bundled Chromium, no web renderer per pane. A dozen sessions run in one
  process with the memory and startup time of a normal desktop app, and the terminals stay sharp at any DPI.
- **A terminal built for this job.** Terminal emulation and rendering are CCUI's own, so panes compose with
  acrylic, Mica, background images and overlays, which a hosted native terminal control cannot do. Emoji are
  rasterised in colour with Direct2D; box drawing and block characters are drawn as geometry so Claude Code's
  borders and bars join seamlessly.
- **The current ConPTY, bundled.** CCUI ships Microsoft's latest pseudo console and asks it to measure text by
  grapheme cluster, the way Claude Code lays out its UI. On a machine without the bundled files it falls back to
  the one built into Windows.
- **State from data, not screen scraping.** Claude Code writes every session to a JSONL transcript. CCUI tails
  it, and registers a few hooks, so session state, stats and timeline are exact rather than guessed.

## Features

### Sessions, side by side

- **Panes, Visual Studio style.** Dock, tab, split, float and re-dock sessions (AvalonDock). `Ctrl+Shift+G`
  tiles everything into a grid.
- **Live state everywhere.** Each pane, tab and list entry shows working, waiting for you, needs attention
  (permission prompts and questions), or exited. A spinner marks the sessions that are busy.
- **Subagents.** Running subagents appear under their session in the list and drive the activity meters.
- **Session list.** Every recent session on the machine, newest first and filterable; open or resume any of them.
  Sessions killed by a reboot are flagged with a one-click "Reopen".
- **Follows Claude Code.** `/clear` and `/compact` are understood, `/rename` titles show up, a cancelled turn
  reads as cancelled, and sessions are always resumed by full session id.

### Insight into each session

- **Header stats.** Model, turn time and total time, tokens in, out and from cache, context window used, tool
  calls and failed tool calls.
- **Activity meters.** Two VU-style meters per session show traffic to and from the model as it happens.
- **Detail view.** Every prompt, response, thinking block, tool call with its input and result, and system
  message, by time or filtered by kind. Failed tool calls have their own tab.
- **Attention.** When Claude asks something in a pane you are not looking at, the pane and the list say so.

### Looks the way you want

- **Transparency and backdrops.** Acrylic, Mica or Mica Alt behind the whole window (Windows 11), a tint over
  it, and per-terminal opacity.
- **Background images.** Any image behind the terminals, with opacity, stretch and alignment settings.
- **Windows Terminal colour schemes.** Built-in schemes, or paste your own from Windows Terminal's
  `settings.json`; the format is the same.
- **Fonts and cursor.** Font family, size, weight, fallback fonts, line height, cursor shape and blink, bold
  rendering, built-in glyphs for box drawing, and colour emoji on or off.
- **Accent and focus.** An accent colour for the active tab and a coloured border on the focused pane.

### Keyboard first

- `Alt+Arrow` moves focus to the pane in that direction, geometrically, across the main and floating windows.
- `Ctrl+Shift+T` new session, `Ctrl+Shift+W` close, `Ctrl+Shift+D` detail view, `Ctrl+Shift+B` and
  `Ctrl+Shift+E` show and focus the session list, `Ctrl+Shift+G` tile. All rebindable.
- In the terminal: `Ctrl+C` copies a selection and interrupts otherwise, `Ctrl+Shift+C` always copies, `Ctrl+V`
  pastes (bracketed when Claude asks for it), right-click copies or pastes, `Shift+Enter` inserts a newline, and
  `Ctrl+click` opens a URL. Extra chords can be mapped to any text with `SendKeys`.

### Save and restore

- Open sessions, the docking layout, the window position and size, and dismissed notices are saved
  automatically and come back on the next start.
- Sessions start from a fresh logon environment, so they behave like Claude Code started from the Start menu,
  wherever CCUI itself was launched from.

### Demo mode

Four scripted sessions that look and behave like Claude Code (colours, box drawing, emoji, spinner, status
line, transcript events), so you can try the whole UI without running Claude or spending tokens.

## Run it

Requires Windows 10/11 and the .NET 10 SDK. Visual Studio 2026 is optional.

1. Open `src/CCUI.sln` and set **CCUI.App** as the startup project.
2. Pick a launch profile: **CCUI** (real sessions) or **CCUI (Demo)** (no Claude needed).
3. Run.

From a shell:

```
dotnet run --project src/CCUI.App
dotnet run --project src/CCUI.App -- --Demo:Enabled=true
```

## Configure it

Everything is in `src/CCUI.App/appsettings.json`, with Windows Terminal's names and units where they apply. Put
personal overrides in `%LOCALAPPDATA%\CCUI\appsettings.user.json`; they are read last and survive updates:

```jsonc
{
  "Terminal": {
    "FontFamily": "Cascadia Mono",
    "FontSize": 11,
    "ColorScheme": "One Half Dark",
    "Opacity": 80,
    "BackgroundImage": "C:\\Pictures\\wallpaper.jpg",
    "BackgroundImageOpacity": 0.25
  },
  "Appearance": { "Backdrop": "Acrylic", "AccentColor": "#D97757" },
  "Claude": { "Arguments": [ "--model", "opus" ] }
}
```

Settings can also come from environment variables (`CCUI_Terminal__FontSize=13`) and the command line
(`--Demo:Enabled=true`). [docs/configuration.md](docs/configuration.md) lists every setting, including how to
start sessions with `--dangerously-skip-permissions` on one machine only.

## Documentation

- [Architecture](docs/architecture.md): projects, data flow and the main decisions.
- [Configuration](docs/configuration.md): every setting, the demo mode and personal overrides.
- [Testing](docs/testing.md): what is tested where, rendered snapshots, CI and a local runner.
- [Roadmap](docs/roadmap.md): what comes next, and the known gaps.

## Status

CCUI is in daily use and under active development; expect rough edges. The transcript format and hooks it
relies on are Claude Code internals rather than a public contract, so a Claude Code update can call for a CCUI
update. Issues and pull requests are welcome.
