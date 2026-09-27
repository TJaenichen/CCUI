using CCUI.Core.Claude;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CCUI.Core.ViewModels;

/// <summary>
/// A timeline entry for the detail view: a one-line "intro" that unfolds into the full content when expanded.
/// Each kind has its own subclass so the view can pick a DataTemplate by type.
/// </summary>
public abstract partial class TimelineItemViewModel(TimelineItem item) : ObservableObject
{
    public TimelineItem Item { get; } = item;

    public long Sequence => Item.Sequence;

    public DateTimeOffset Timestamp => Item.Timestamp;

    public string Time => Item.Timestamp == DateTimeOffset.MinValue ? string.Empty : Item.Timestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    public abstract string KindName { get; }

    /// <summary>The one-line summary shown while collapsed.</summary>
    public virtual string Intro => Format.FirstLine(Item.Text);

    /// <summary>The full content shown when expanded.</summary>
    public virtual string Body => Item.Text;

    /// <summary>A short measurement shown at the right of the intro (duration, tokens).</summary>
    public virtual string? Metric => Item.OutputTokens is { } tokens ? $"↓ {Format.Tokens(tokens)}" : null;

    public bool IsSubagent => Item.AgentId is not null;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Re-reads the underlying item after it changed (e.g. a tool call got its result).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}

public sealed class PromptItemViewModel(TimelineItem item) : TimelineItemViewModel(item)
{
    public override string KindName => "Prompt";

    public override string? Metric => null;
}

public sealed class ResponseItemViewModel(TimelineItem item) : TimelineItemViewModel(item)
{
    public override string KindName => "Response";
}

public sealed class ThinkingItemViewModel(TimelineItem item) : TimelineItemViewModel(item)
{
    public override string KindName => "Thinking";
}

public sealed class SystemItemViewModel(TimelineItem item) : TimelineItemViewModel(item)
{
    public override string KindName => "System";

    public override string? Metric => null;
}

public sealed class ToolCallItemViewModel(TimelineItem item) : TimelineItemViewModel(item)
{
    public override string KindName => "Tool";

    public string ToolName => Item.Text;

    public override string Intro => ToolSummaries.Describe(Item.Text, Item.ToolInputJson ?? "{}");

    public override string Body => ToolSummaries.Pretty(Item.ToolInputJson ?? "{}");

    public string Input => Body;

    public string Result => Item.ToolResult ?? string.Empty;

    public bool HasResult => Item.ToolResult is not null;

    public bool IsError => Item.IsError;

    public bool IsRunning => Item.IsRunning;

    public override string? Metric => Item.Duration is { } duration ? Format.Duration(duration) : "running…";
}

public static class TimelineItemViewModelFactory
{
    public static TimelineItemViewModel Create(TimelineItem item) => item.Kind switch
    {
        TimelineItemKind.Prompt => new PromptItemViewModel(item),
        TimelineItemKind.Response => new ResponseItemViewModel(item),
        TimelineItemKind.Thinking => new ThinkingItemViewModel(item),
        TimelineItemKind.ToolCall => new ToolCallItemViewModel(item),
        _ => new SystemItemViewModel(item),
    };
}
