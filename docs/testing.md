# Testing

| Layer | Where | Runs on | What it covers |
|---|---|---|---|
| Terminal engine | `CCUI.Terminal.Tests` | any OS | parser, emulator behaviour, widths and graphemes, keys, quoting, recording |
| Core | `CCUI.Core.Tests` | any OS | transcript parsing, timeline and stats, catalog, hooks, launch planning, meters (fake clock), navigation, workspace, demo mode, view models |
| WPF rendering | `CCUI.App.Tests` | Windows | loads the real app resources, renders the terminal, a pane, the session list and the whole demo window to PNG, checks pixels |
| By eye | your machine | Windows | acrylic, background images, animation feel, speed with many live sessions, DPI and multi-monitor |

Run everything with `dotnet test --solution src/CCUI.sln` (the repo opts into Microsoft.Testing.Platform in
`src/global.json`), or from Visual Studio's Test Explorer.

## Snapshots

`CCUI.App.Tests` writes PNGs to `bin/<config>/net10.0-windows/TestResults/snapshots/`. CI uploads them as the
`snapshots` artifact of each run. The tests assert only robust facts (a pixel's colour, that an emoji is in colour,
that enough distinct colours were drawn); comparing against approved baseline images is a planned next step once the
first renders are reviewed.

## CI

`.github/workflows/ci.yml` builds and tests on `windows-latest` on every push that touches `src/`, and uploads the
snapshots. GitHub's hosted runners have no GPU, so acrylic does not show in their snapshots.

To run CI on your own machine instead (free, real GPU): in the repository's Settings → Actions → Runners → New
self-hosted runner (Windows x64), follow the download and `config.cmd` steps, add the label `ccui-desktop`, and
start it with `run.cmd` in your logged-in session (not as a service: a service has no desktop to render to). Then
change `runs-on` in the workflow as its comment says.
