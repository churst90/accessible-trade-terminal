using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Models
{
    /// <summary>
    /// A consolidated live bar together with the identity it was SUBSCRIBED FOR.
    ///
    /// The single focused live subscription is retargeted asynchronously on a tab
    /// switch, while focus itself moves synchronously — so for the length of one
    /// gap-fill round-trip the pump is holding the outgoing symbol's ticks and the
    /// incoming symbol's feed. Routing by "whatever holds focus now" merged one
    /// symbol's prices into another symbol's buffer, which fabricates bars, raises
    /// LiveAppend, and can auto-execute a strategy on a closed bar that never
    /// happened.
    ///
    /// The identity travels WITH the bar so the consumer can compare rather than
    /// assume: a tick is applied only to the feed it was fetched for. That is a
    /// property of the value, not of the ordering of two async operations, so it
    /// cannot be reopened by a future change to the retarget sequence.
    /// </summary>
    public readonly record struct LiveTick(ChartIdentity Identity, Ohlcv Bar);
}
