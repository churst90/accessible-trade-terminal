namespace AccessibleTrader.Sdk.Strategies;

/// <summary>
/// How to start a strategy over when <c>Activator.CreateInstance(prototype.GetType())</c> cannot.
///
/// <para>
/// The causality probe needs a strategy that has seen no bars for each of its runs — that is the
/// whole basis of comparing a 150-bar run against a 400-bar one — and in-process it gets one by
/// reflecting on the prototype's type. A strategy that is really a PROXY for an instance living
/// in the sandbox worker has no such constructor to call: its type is the proxy's, and
/// constructing another proxy would have nothing behind it.
/// </para>
///
/// <para>
/// Implementing this is how such a proxy says "ask me instead". It is not a general strategy
/// concern — an ordinary <c>ITradingStrategy</c> should not implement it, and the probe falls
/// back to <c>Activator</c> for everything that does not.
/// </para>
/// </summary>
public interface IRestartableStrategy
{
    /// <summary>
    /// A strategy of the same kind that has seen no bars.
    ///
    /// <para>
    /// May return <c>this</c> when the implementation discards its state on the next
    /// <c>Initialize</c> rather than by being reconstructed — which is exactly what the
    /// out-of-process proxy does, since every <c>InitializeStrategy</c> frame builds a fresh
    /// instance inside the worker. Callers must therefore treat the returned strategy as
    /// virgin only AFTER they have called <c>Initialize</c> on it, which is the order both the
    /// probe and the backtester already use.
    /// </para>
    /// </summary>
    ITradingStrategy StartFresh();
}
