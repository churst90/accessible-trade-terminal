using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Consolidates a provider's raw live ticks into period bars for one
    /// timeframe — the per-subscription replacement for the single bucket the
    /// old LiveStreamManager kept (docs/KEYED_FEEDS_DESIGN.md). One instance per
    /// live subscription; thread-safe.
    ///
    /// Style matters (see <see cref="LiveTickStyle"/>): trade-delta ticks ADD
    /// volume to the bucket; cumulative-bar ticks (kline streams) re-send the
    /// same source bar with its running total, so only the volume GROWTH since
    /// the last tick of that source bar is added — naively accumulating them
    /// re-adds the running total on every ~1s update and inflates the live bar.
    /// When the requested timeframe is coarser than the source bars (a 2h chart
    /// on 1h klines), each new source bar's volume enters the bucket once and
    /// its subsequent updates contribute only their growth.
    /// </summary>
    public sealed class BarBucketConsolidator
    {
        private readonly string _timeframe;
        private readonly LiveTickStyle _style;
        private readonly object _lock = new();
        private Ohlcv? _bucket;
        // Cumulative style: which source bar was last folded in, and its running
        // volume at that point — the baseline for the next tick's volume delta.
        private DateTime _sourceBarDate;
        private double _sourceBarVolume;

        public BarBucketConsolidator(string timeframe, LiveTickStyle style)
        {
            _timeframe = timeframe;
            _style = style;
        }

        /// <summary>
        /// Folds one tick into the current period bucket and returns the updated
        /// consolidated bar, or null when the result is malformed and must be
        /// dropped before it can corrupt indicator buffers: all four OHLC legs
        /// must be &gt; 0 (a zero leg means the feed glitched — real market bars
        /// satisfy Low &gt; 0 for any tradable instrument) and Volume &gt;= 0
        /// (the first tick of a period legitimately has zero volume on thin
        /// books or pre-market).
        /// </summary>
        public Ohlcv? Apply(Ohlcv tick)
        {
            // Validate the TICK before folding it in — merging first and checking
            // the merged result after would let a glitch tick poison the bucket's
            // O/H/L for the rest of the period even though it was "dropped".
            if (!(tick.Open > 0 && tick.High > 0 && tick.Low > 0 && tick.Close > 0 && tick.Volume >= 0))
                return null;

            lock (_lock)
            {
                DateTime periodStart = TimeframeUtility.GetPeriodStart(tick.Date, _timeframe);

                // A tick for an OLDER period than the current bucket is a replay
                // (reconnect) or wildly out-of-order — starting a "new" bucket from
                // it would throw away the accumulated current period. Drop it.
                if (_bucket is { } current && periodStart < current.Date)
                    return null;

                if (_bucket is not { } bucket || bucket.Date != periodStart)
                {
                    _bucket = new Ohlcv(periodStart, tick.Open, tick.High, tick.Low, tick.Close, tick.Volume);
                }
                else if (_style == LiveTickStyle.CumulativeBars)
                {
                    // Same source bar re-sent → add only its growth; a NEW source
                    // bar inside the same bucket enters with its full volume.
                    double volumeDelta = tick.Date == _sourceBarDate
                        ? Math.Max(0, tick.Volume - _sourceBarVolume)
                        : tick.Volume;
                    _bucket = new Ohlcv(
                        bucket.Date,
                        bucket.Open,
                        Math.Max(bucket.High, tick.High),
                        Math.Min(bucket.Low, tick.Low),
                        tick.Close,
                        bucket.Volume + volumeDelta);
                }
                else
                {
                    _bucket = bucket.UpdateWith(tick);
                }

                _sourceBarDate = tick.Date;
                _sourceBarVolume = tick.Volume;
                return _bucket.Value;
            }
        }
    }
}
