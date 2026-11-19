using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Charts
{
    /// <summary>
    /// Range bars: every bar spans the same price range, high to low, however long it takes. Pure:
    /// no state, no services, safe to call from a reducer.
    ///
    /// <para>
    /// A bar ends when price moves past its range: it closes at the edge it broke (its high, or its
    /// low), and the next bar opens there. A bar is a real candle — open, high, low and close are
    /// prices the market traded through — so it keeps the candle's wicks, unlike a Renko brick.
    /// </para>
    ///
    /// <para>
    /// Built from time bars, not ticks, so the path inside each bar is assumed: an up bar
    /// (close at or above its open) goes open, low, high, close; a down bar goes open, high, low,
    /// close. A gap between one bar's close and the next bar's open is walked too, so a gap of
    /// three ranges makes three bars. Only COMPLETED bars are drawn, as Renko draws only completed
    /// bricks; the one still forming is left out.
    /// </para>
    ///
    /// <para>
    /// A bar's <c>Date</c> is the open time of the time bar it completed in. Its span is the real
    /// window it took: from the time bar it opened in to the close of the one it completed in.
    /// </para>
    /// </summary>
    public static class RangeBarBuilder
    {
        public static BrickBuildResult Build(
            IReadOnlyList<Ohlcv> bars,
            double range,
            TimeSpan barLength,
            int maxBars = BrickChartBuilder.MaxBricks)
        {
            if (!(range > 0) || double.IsNaN(range) || double.IsInfinity(range))
                return new BrickBuildResult(
                    TimeSeriesBuffer<Ohlcv>.Empty,
                    ImmutableArray<BrickSpan>.Empty,
                    "A range bar's range must be a positive price.");

            var built = new List<Ohlcv>();
            var spans = new List<BrickSpan>();
            if (bars.Count == 0)
                return new BrickBuildResult(new TimeSeriesBuffer<Ohlcv>(built), spans.ToImmutableArray(), null);

            double open = bars[0].Open, high = open, low = open, close = open;
            int startBar = 0;
            double pendingVolume = 0;
            bool overCap = false;

            // Moves the forming bar to price q, completing every bar the move passes through.
            void MoveTo(double q, int i)
            {
                while (!overCap)
                {
                    bool? up = null;
                    if (q > low + range) { high = low + range; close = high; up = true; }
                    else if (q < high - range) { low = high - range; close = low; up = false; }

                    if (up == null)
                    {
                        if (q > high) high = q;
                        if (q < low) low = q;
                        close = q;
                        return;
                    }

                    if (built.Count >= maxBars) { overCap = true; return; }
                    built.Add(new Ohlcv(bars[i].Date, open, high, low, close, pendingVolume));
                    spans.Add(new BrickSpan(bars[startBar].Date, bars[i].Date + barLength, startBar, i, up.Value));
                    pendingVolume = 0;

                    open = close; high = close; low = close;
                    startBar = i;
                }
            }

            for (int i = 0; i < bars.Count && !overCap; i++)
            {
                var bar = bars[i];
                pendingVolume += bar.Volume;
                bool upBar = bar.Close >= bar.Open;
                MoveTo(bar.Open, i);
                MoveTo(upBar ? bar.Low : bar.High, i);
                MoveTo(upBar ? bar.High : bar.Low, i);
                MoveTo(bar.Close, i);
            }

            if (overCap)
                return new BrickBuildResult(
                    TimeSeriesBuffer<Ohlcv>.Empty,
                    ImmutableArray<BrickSpan>.Empty,
                    $"A range of {SpeechPriceFormatter.FormatPrice(range)} would draw over "
                    + $"{maxBars.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} "
                    + "bars. Choose a larger range.");

            return new BrickBuildResult(new TimeSeriesBuffer<Ohlcv>(built), spans.ToImmutableArray(), null);
        }
    }
}
