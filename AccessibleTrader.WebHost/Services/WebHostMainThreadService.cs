using AccessibleTrader.Core.Services;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// WebHost implementation of <see cref="IMainThreadService"/>.
    ///
    /// In Blazor Server there's no "UI thread" the way MAUI has — every
    /// connected circuit has its own synchronization context owned by the
    /// hub. The MAUI version dispatches onto <c>MainThread</c> via
    /// <c>BeginInvokeOnMainThread</c>; the web equivalent is "post the
    /// continuation to the current SynchronizationContext if there is one,
    /// otherwise fire-and-forget on the thread pool." Either is fine —
    /// callers use this to avoid touching UI state from a background
    /// thread, and Blazor's renderer already serialises into the circuit
    /// context.
    /// </summary>
    public sealed class WebHostMainThreadService : IMainThreadService
    {
        public void InvokeOnMainThread(Action action)
        {
            var ctx = System.Threading.SynchronizationContext.Current;
            if (ctx is not null)
            {
                ctx.Post(_ => action(), null);
            }
            else
            {
                _ = Task.Run(action);
            }
        }
    }
}
