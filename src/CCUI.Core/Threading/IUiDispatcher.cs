namespace CCUI.Core.Threading;

/// <summary>Runs work on the UI thread. View models use it to apply background events.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>Runs actions immediately on the calling thread (tests, headless use).</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
