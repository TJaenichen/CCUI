# Roadmap

Next steps, roughly in order.

1. **Network view.** A local logging proxy set through `ANTHROPIC_BASE_URL`, so the detail view gets a "Network"
   tab with every request and streamed response (headers redacted), their timing and sizes, and the meters measure
   real bytes on the wire.
2. **Snapshot baselines.** Approve the first rendered PNGs and compare future renders against them with a small
   tolerance; add UI automation smoke tests (focus moves, float and re-dock) on a runner with a desktop.
3. **Cheap classification with Jev.** Jev (typesafe.ai) returns typed decisions with confidence in 70–500 ms at
   about $0.04 per million input tokens. Uses: richer session state than the heuristics ("asking a question",
   "waiting for permission", "stuck on an error loop", "done"), flagging risky tool calls in the timeline, and
   ranking which session needs you first. A `ISessionClassifier` seam in Core would keep it optional.
4. **Terminal polish.** Reflow on resize, clickable OSC 8 hyperlinks, win32-input-mode for exact key reporting,
   importing the look straight from a Windows Terminal profile.
5. **More agent detail.** Read subagent transcripts (`<session>/subagents/*.jsonl`) to show what each running agent
   is doing under its session.

## Known gaps

- **Emoji outside the terminal** (session list, detail view) are monochrome: WPF text cannot draw colour fonts.
  The terminal's Direct2D rasteriser could be reused for an emoji-aware text block.
- **Ctrl with punctuation or digits** (Ctrl+[, Ctrl+/, Ctrl+2 …) sends nothing; only Ctrl+letter and Ctrl+Space
  map to control characters. The punctuation keys depend on the keyboard layout, so this needs care.
- **Reboot detection with Fast Startup**: a shut-down/power-on with Fast Startup does not reset the boot time
  Windows reports, so sessions killed that way are not flagged (restarts, including update restarts, are).
  Reading boot events from the System event log would cover it.
- **Tiling many panes** re-styles every document on each move; fine for a dozen sessions, slow for many more.
- **Flags** (🇩🇪) show as letters: Segoe UI Emoji has no flag glyphs.
