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
