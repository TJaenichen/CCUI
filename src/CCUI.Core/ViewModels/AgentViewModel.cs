using CCUI.Core.Claude;

namespace CCUI.Core.ViewModels;

/// <summary>A running subagent, shown under its session in the list and in the pane header.</summary>
public sealed class AgentViewModel(RunningAgent agent)
{
    public string Id => agent.ToolUseId;

    public string Description => agent.Description;

    public string AgentType => agent.AgentType ?? "agent";

    public DateTimeOffset Started => agent.Started;
}
