using System.Windows.Threading;
using CCUI.Core.Threading;

namespace CCUI.App.Services;

public sealed class WpfDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
        }
    }
}
