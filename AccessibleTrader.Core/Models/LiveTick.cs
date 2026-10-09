using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

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
    ///
    /// <para><see cref="Source"/> says which consolidator built the bar and what its ticks
    /// mean, so the feed can CONTINUE the provider's fetched forming bar instead of replacing
    /// it — see <see cref="LiveStreamSource"/>. Null means the bar is a complete candle that
    /// replaces the last bar as it stands (what a test fake handing the feed finished bars
    /// wants); every production producer sets it.</para>
    /// </summary>
    public readonly record struct LiveTick(ChartIdentity Identity, Ohlcv Bar, LiveStreamSource? Source = null);

    /// <summary>
    /// One live subscription's consolidator, as the feed it ticks into sees it.
    ///
    /// <para><b>Why the feed needs this.</b> Every subscribe builds a fresh
    /// <see cref="BarBucketConsolidator"/>, and its first bucket is built from the first tick
    /// alone: on a trade feed that is Open = High = Low = Close = one trade's price and Volume =
    /// one trade's size. <c>ChartFeed.ApplyLiveTick</c> used to <c>ReplaceLast</c> the
    /// provider's real forming bar with it. Reported by Cody, 2026-10-09: on a weekly chart
    /// (and every other) the most recent candle "starts flat", as if it began when he opened
    /// the chart — on Bitstamp, the default provider, the week's open, range and volume were
    /// thrown away the moment the first trade arrived.</para>
    ///
    /// <para>A bucket is a running total of what ONE consolidator has seen, so the feed keeps
    /// the stream's last volume and adds only the growth to the bar it already holds.
    /// <see cref="StreamId"/> tells two consolidators apart (a re-subscribe starts again from
    /// nothing; a watchdog reconnect keeps the same one). <see cref="Style"/> settles the one
    /// thing growth cannot: whether a stream's FIRST bucket of a period is new volume (trade
    /// deltas — those trades arrived after we subscribed) or already inside the fetched bar
    /// (cumulative bars — a kline or a minute bar carries its running total from before we
    /// subscribed).</para>
    /// </summary>
    public readonly record struct LiveStreamSource(long StreamId, LiveTickStyle Style)
    {
        private static long _next;

        /// <summary>A source with a process-unique id. Ids only grow, so a feed can tell a
        /// superseded subscription's late ticks from the current one's.</summary>
        public static LiveStreamSource Create(LiveTickStyle style)
            => new(System.Threading.Interlocked.Increment(ref _next), style);
    }
}
