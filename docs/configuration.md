# Configuration

All settings are in `src/CCUI.App/appsettings.json` (copied next to the exe). Values are read in this order, later
ones winning:

1. `appsettings.json`
2. `appsettings.{Environment}.json`, where the environment comes from `DOTNET_ENVIRONMENT` (the Visual Studio
   launch profiles set `Demo` or `Development`)
3. `%LOCALAPPDATA%\CCUI\appsettings.user.json`: personal overrides that survive updates
4. Environment variables prefixed `CCUI_` (e.g. `CCUI_Terminal__FontSize=13`)
5. Command-line arguments (e.g. `--Demo:Enabled=true`)

Settings are read at startup; restart to apply changes.

## Demo mode

Pick the **CCUI (Demo)** launch profile in Visual Studio, or pass `--Demo:Enabled=true`. Four scripted sessions
open and tile; they print Claude-like output (colours, box drawing, emoji, spinner, status line) and publish
matching transcript events, so the session list, stats, meters and detail view all have live data. Typing a
prompt and pressing Enter gets a short reply that echoes it, which is a quick way to check keyboard input
(AltGr characters, emoji, Shift+Enter). `Demo:Speed` speeds playback up; the script is in
`src/CCUI.Core/Demo/DemoScript.cs`.

## Skipping permission prompts ("YOLO mode")

To start every session with `--dangerously-skip-permissions`, put it in your personal overrides rather than
`appsettings.json`, so it stays on that machine and never becomes the repo default. Create
`%LOCALAPPDATA%\CCUI\appsettings.user.json` (the folder exists once CCUI has run):

```jsonc
{
  "Claude": {
    "Arguments": [ "--dangerously-skip-permissions" ]
  }
}
```

Restart CCUI. New and resumed sessions then start with the flag; panes already open keep their permissions until
they are resumed. Claude runs every tool call without asking, so use it only where that is acceptable. If
organisation-managed Claude Code settings disable bypass mode, Claude refuses the flag and says so in the session.
To use it for one run only, start CCUI with `--Claude:Arguments:0=--dangerously-skip-permissions`.

## Sections

**Claude**: how sessions start.
- `Command`: `claude` by default, found through PATH (claude.exe, or claude.cmd from npm).
- `Arguments`: extra arguments for every session, e.g. `["--model", "opus"]`.
- `FreshEnvironment`: start from a fresh logon environment rather than CCUI's own.
- `RemoveEnvironmentVariables` / `SetEnvironmentVariables`: applied on top.
- `UseSessionHooks`: registers the hooks that let a pane follow `/clear`, show "working" as soon as a prompt is sent, and see "waiting for you" notifications.
- `PseudoConsole`: `Auto`, `Bundled` or `Inbox`.
- `ContextWindowTokens`: denominator of the context-used percentage (use 1000000 for 1M-context models).

**SessionList**: `MaxAgeDays`, `RefreshSeconds`, `RebootLookBackHours`.

**Terminal**: the terminal look, with Windows Terminal's names and units.
- `FontFamily`, `FontSize` (points), `FontWeight`, `FallbackFontFamilies`, `LineHeight`.
- `ColorScheme`: a built-in scheme (`Campbell`, `Campbell Powershell`, `One Half Dark`, `Tango Dark`) or one
  from `Schemes`. Scheme objects use Windows Terminal's format, so you can paste yours from its settings.json.
- `Opacity` (0–100): how much of the window backdrop shows through the terminal background.
- `BackgroundImage`, `BackgroundImageOpacity`, `BackgroundImageStretchMode`, `BackgroundImageAlignment`.
- `CursorShape` (Bar, Underline, Block), `CursorBlink`, `IntenseTextStyle` (All, Bold, Bright, None).
- `UseBuiltinGlyphs`, `UseColorEmoji`, `ScrollbackLines`, `CopyOnSelect`, `Padding`.
- `SendKeys`: chords that send text. The default maps Shift+Enter to ESC CR, a newline in Claude Code.

**Appearance**: `Backdrop` (Acrylic, Mica, MicaAlt, None; Windows 11), `WindowTint` (#AARRGGBB over the
backdrop), `AccentColor`, `FocusBorderColor`, `FocusBorderThickness`, `MeterDecayMilliseconds`,
`MeterPeakHoldMilliseconds`, `Animations` (the working spinner: `On`, `Off`, or `System` to follow Windows' "Show
animations"; not the default because Remote Desktop sessions report animations as off).

**KeyBindings**: `FocusLeft/Right/Up/Down` (Alt+Arrows), `ToggleDetail` (Ctrl+Shift+D), `NewSession`
(Ctrl+Shift+T), `CloseSession` (Ctrl+Shift+W), `ToggleSessionList` (Ctrl+Shift+B), `FocusSessionList`
(Ctrl+Shift+E), `TileLayout` (Ctrl+Shift+G). Empty disables a binding.

**Workspace**: `RestoreOnStartup`, `AutoSaveSeconds`, `Path` (default `%LOCALAPPDATA%\CCUI\workspace.json`),
`LaunchStaggerMilliseconds`.

## Terminal keys

Ctrl+C copies when text is selected and sends ^C otherwise; Ctrl+Shift+C always copies; Ctrl+V pastes (bracketed
when the app asks for it); right-click copies a selection or pastes; Shift+PageUp/PageDown scroll.
