using CCUI.Core.Claude;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CCUI.Core.ViewModels;

/// <summary>The numbers in a pane's header.</summary>
public sealed partial class SessionStatsViewModel(long contextWindowTokens) : ObservableObject
{
    private SessionStatistics _stats = new();

    [ObservableProperty]
    public partial string Model { get; set; } = "—";

    [ObservableProperty]
    public partial string TokensIn { get; set; } = "0";

    [ObservableProperty]
    public partial string TokensOut { get; set; } = "0";

    [ObservableProperty]
    public partial string CacheRead { get; set; } = "0";

    /// <summary>Share of the context window used by the latest request, 0-100.</summary>
    [ObservableProperty]
    public partial double ContextPercent { get; set; }

    [ObservableProperty]
    public partial string ContextText { get; set; } = "—";

    /// <summary>The running turn's elapsed time, or the last turn's duration.</summary>
    [ObservableProperty]
    public partial string TurnTime { get; set; } = "—";

    [ObservableProperty]
    public partial string TotalTime { get; set; } = "—";

    [ObservableProperty]
    public partial int Prompts { get; set; }

    [ObservableProperty]
    public partial int ToolCalls { get; set; }

    [ObservableProperty]
    public partial int ToolErrors { get; set; }

    [ObservableProperty]
    public partial bool IsWorking { get; set; }

    public void Update(SessionStatistics stats, DateTimeOffset now)
    {
        _stats = stats;
        Model = Format.ModelName(stats.Model);
        TokensIn = Format.Tokens(stats.Tokens.Input + stats.Tokens.CacheCreation);
        TokensOut = Format.Tokens(stats.Tokens.Output);
        CacheRead = Format.Tokens(stats.Tokens.CacheRead);
        ContextPercent = contextWindowTokens > 0 ? Math.Min(100, stats.ContextTokens * 100.0 / contextWindowTokens) : 0;
        ContextText = stats.ContextTokens > 0 ? $"{ContextPercent:0}% · {Format.Tokens(stats.ContextTokens)}" : "—";
        Prompts = stats.Prompts;
        ToolCalls = stats.ToolCalls;
        ToolErrors = stats.ToolErrors;
        Tick(now);
    }

    public void Tick(DateTimeOffset now)
    {
        IsWorking = _stats.TurnStartedAt is not null;
        TurnTime = _stats.TurnStartedAt is { } started
            ? Format.Duration(now - started)
            : _stats.LastTurnDuration > TimeSpan.Zero ? Format.Duration(_stats.LastTurnDuration) : "—";
        var total = _stats.TotalTurnTime + (_stats.TurnStartedAt is { } running ? now - running : TimeSpan.Zero);
        TotalTime = total > TimeSpan.Zero ? Format.Duration(total) : "—";
    }
}
