using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Charts
{
    /// <summary>Everything one Renko build produced. Null <see cref="Refusal"/> means it built.</summary>
    public readonly record struct BrickBuildResult(
        TimeSeriesBuffer<Ohlcv> Bricks,
        ImmutableArray<BrickSpan> Spans,
        string? Refusal)
    {
        public bool Ok => Refusal == null;
    }

    /// <summary>A validated Renko choice: the settings, the frozen box and the bricks they make.</summary>
    public sealed record BrickChartPlan(
        RenkoSettings Settings,
        double BoxSize,
        TimeSeriesBuffer<Ohlcv> Bricks,
        ImmutableArray<BrickSpan> Spans);

    /// <summary>
    /// Close-based, traditional Renko. Pure: no state, no services, safe to call from a reducer.
    ///
    /// <para>
    /// The grid is anchored at <c>floor(firstClose / box) * box</c>, so every brick edge is a
    /// whole multiple of the box and a prepend of older history keeps the edges on the same
    /// grid. It does not promise the same bricks: the older bars can leave the trend in a
    /// different direction, and the bricks then differ until a move large enough to agree. A brick is one box tall; a bar that moves four boxes makes four bricks.
    /// Continuing the trend takes a close one box past the last brick's edge; a reversal takes
    /// a close two boxes past its far edge — one box beyond the last brick's opposite edge — and
    /// draws one brick back across that first box.
    /// </para>
    ///
    /// <para>
    /// A brick's <c>Date</c> is the open time of the bar it completed in, so several bricks may
    /// share it. Its span is the REAL window it took to form: from the first bar after the
    /// previous brick completed to the completion bar's close. That is what the speech says.
    /// </para>
    /// </summary>
    public static class BrickChartBuilder
    {
        public const int MaxBricks = 20_000;

        /// <summary>The length of one real time bar on this chart, from its timeframe or the
        /// spacing of the loaded bars. Used only to name a brick's end time.</summary>
        public static TimeSpan BarLength(ChartIdentity? identity, IReadOnlyList<Ohlcv>? bars)
        {
            int seconds = TimeframeUtility.ToSeconds(identity?.Timeframe ?? "");
            if (seconds > 0) return TimeSpan.FromSeconds(seconds);

            if (bars is { Count: >= 2 })
            {
                var gap = bars[^1].Date - bars[^2].Date;
                if (gap > TimeSpan.Zero) return gap;
            }

            return TimeSpan.FromDays(1);
        }

        /// <summary>
        /// The box a choice would use, or the refusal that stops it. ATR reads the CLOSED bars
        /// (the newest, still-forming bar is excluded) and takes the ATR at the last closed one.
        /// </summary>
        public static bool TryResolveBox(
            IReadOnlyList<Ohlcv> bars,
            RenkoSettings settings,
            out double box,
            out string? refusal)
            => TryResolveSize(bars, settings.Mode, settings.BoxSize, settings.AtrLength, "Renko", "box",
                out box, out refusal);

        /// <summary>
        /// The ATR-or-fixed size behind Renko's box and the range bars' range: the same rule,
        /// worded for the chart that asked (<paramref name="chart"/> and its <paramref name="unit"/>).
        /// </summary>
        internal static bool TryResolveSize(
            IReadOnlyList<Ohlcv> bars,
            RenkoBoxMode mode,
            double fixedSize,
            int atrLength,
            string chart,
            string unit,
            out double box,
            out string? refusal)
        {
            var settings = new RenkoSettings(mode, fixedSize, atrLength);
            box = 0;
            refusal = null;

            if (settings.Mode == RenkoBoxMode.Fixed)
            {
                box = settings.BoxSize;
                if (!(box > 0) || double.IsNaN(box) || double.IsInfinity(box))
                {
                    refusal = $"A {chart} {unit} must be a positive price.";
                    return false;
                }
                return true;
            }

            int closedCount = bars.Count > 0 ? bars.Count - 1 : 0;
            // ATR 14 needs 15 closed bars (one more than its period); add the forming bar the
            // chart is still drawing and the first usable box needs 16 loaded bars.
            int needed = settings.AtrLength + 2;
            if (settings.AtrLength < 1 || bars.Count < needed)
            {
                refusal = $"{chart} needs at least {needed} bars for ATR {settings.AtrLength}; "
                        + $"this chart has {bars.Count}.";
                return false;
            }

            var closed = new Ohlcv[closedCount];
            for (int i = 0; i < closedCount; i++) closed[i] = bars[i];
            var atr = IndicatorMath.Atr(closed, settings.AtrLength);
            box = atr[^1];

            if (!(box > 0) || double.IsNaN(box) || double.IsInfinity(box))
            {
                refusal = $"A {chart} {unit} must be a positive price.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// A full user choice: resolve the box, build, then refuse an empty chart. Commands and
        /// dialogs call this BEFORE dispatching; the reducer never refuses.
        /// </summary>
        public static bool TryPlan(
            IReadOnlyList<Ohlcv> bars,
            RenkoSettings settings,
            TimeSpan barLength,
            out BrickChartPlan? plan,
            out string? refusal)
            => BuiltCharts.TryPlan(PriceChartStyle.Renko, bars, settings, barLength, out plan, out refusal);

        /// <summary>
        /// Builds the bricks. Returns a refusal for an unusable box or a result that would
        /// exceed <paramref name="maxBricks"/>; a flat series returns no bricks and no refusal.
        /// </summary>
        public static BrickBuildResult Build(
            IReadOnlyList<Ohlcv> bars,
            double box,
            TimeSpan barLength,
            int maxBricks = MaxBricks)
        {
            if (!(box > 0) || double.IsNaN(box) || double.IsInfinity(box))
                return new BrickBuildResult(
                    TimeSeriesBuffer<Ohlcv>.Empty,
                    ImmutableArray<BrickSpan>.Empty,
                    "A Renko box must be a positive price.");

            var bricks = new List<Ohlcv>();
            var spans = new List<BrickSpan>();
            string overCap = $"A box of {SpeechPriceFormatter.FormatPrice(box)} would draw over "
                           + $"{maxBricks.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} "
                           + "bricks. Choose a larger box.";

            if (bars.Count == 0)
                return new BrickBuildResult(new TimeSeriesBuffer<Ohlcv>(bricks), spans.ToImmutableArray(), null);

            double anchor = Math.Floor(bars[0].Close / box) * box;
            double lowEdge = anchor;
            double highEdge = anchor;
            bool? up = null;

            int lastCompletionBar = -1;
            double pendingVolume = 0;

            for (int i = 0; i < bars.Count; i++)
            {
                var bar = bars[i];
                pendingVolume += bar.Volume;
                double close = bar.Close;
                bool madeOneThisBar = false;
                bool assignedVolume = false;

                while (true)
                {
                    bool? direction = null;
                    double openEdge = 0;
                    double closeEdge = 0;

                    if (up == null)
                    {
                        if (close >= anchor + box)
                        {
                            direction = true;
                            openEdge = anchor;
                            closeEdge = anchor + box;
                        }
                        else if (close <= anchor - box)
                        {
                            direction = false;
                            openEdge = anchor;
                            closeEdge = anchor - box;
                        }
                    }
                    else if (up.Value)
                    {
                        if (close >= highEdge + box)
                        {
                            direction = true;
                            openEdge = highEdge;
                            closeEdge = highEdge + box;
                        }
                        else if (close <= lowEdge - box)
                        {
                            direction = false;
                            openEdge = lowEdge;
                            closeEdge = lowEdge - box;
                        }
                    }
                    else
                    {
                        if (close <= lowEdge - box)
                        {
                            direction = false;
                            openEdge = lowEdge;
                            closeEdge = lowEdge - box;
                        }
                        else if (close >= highEdge + box)
                        {
                            direction = true;
                            openEdge = highEdge;
                            closeEdge = highEdge + box;
                        }
                    }

                    if (direction == null) break;

                    if (bricks.Count >= maxBricks)
                        return new BrickBuildResult(
                            TimeSeriesBuffer<Ohlcv>.Empty,
                            ImmutableArray<BrickSpan>.Empty,
                            overCap);

                    int firstBar = lastCompletionBar < 0 ? 0 : Math.Min(lastCompletionBar + 1, i);
                    double volume = 0;
                    if (!assignedVolume)
                    {
                        volume = pendingVolume;
                        pendingVolume = 0;
                        assignedVolume = true;
                    }

                    double low = Math.Min(openEdge, closeEdge);
                    double high = Math.Max(openEdge, closeEdge);

                    bricks.Add(new Ohlcv(bar.Date, openEdge, high, low, closeEdge, volume));
                    spans.Add(new BrickSpan(
                        bars[firstBar].Date,
                        bar.Date + barLength,
                        firstBar,
                        i,
                        direction.Value));

                    lowEdge = low;
                    highEdge = high;
                    up = direction;
                    lastCompletionBar = i;
                    madeOneThisBar = true;
                }

                _ = madeOneThisBar;
            }

            return new BrickBuildResult(
                new TimeSeriesBuffer<Ohlcv>(bricks),
                spans.ToImmutableArray(),
                null);
        }
    }
}
