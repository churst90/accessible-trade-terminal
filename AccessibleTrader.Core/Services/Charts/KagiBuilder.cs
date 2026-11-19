using System.Collections.Immutable;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Charts
{
    /// <summary>
    /// Kagi: one vertical line per swing, which keeps growing while price goes its way and turns
    /// only when a close comes back the reversal amount from the line's end. Pure: no state, no
    /// services, safe to call from a reducer.
    ///
    /// <para>
    /// Closes only. The first line runs from the first bar's close to the first close at least one
    /// reversal away from it. After that, a close beyond the newest line's end extends the line to
    /// it; a close at least one reversal back from the end starts a new line the other way, from
    /// that end to the close. Anything else draws nothing. Each drawn bar is one line: Open where
    /// it starts, Close where it ends now, so the newest line's Close moves while it grows and the
    /// line count moves only on a turn.
    /// </para>
    ///
    /// <para>
    /// A line's <c>Date</c> is the open time of the bar whose close turned it (for the first line,
    /// the bar that drew it), and never changes as the line grows. Its span is the real window it
    /// covers: from the bar after the previous line's end to the bar of its own end, so it widens
    /// as the line grows. Its volume is that window's.
    /// </para>
    ///
    /// <para>
    /// Thick and thin (yang and yin) are not part of a line's bars: they follow from the lines in
    /// order, and <see cref="KagiLines"/> works them out for the drawing and the speech alike.
    /// </para>
    /// </summary>
    public static class KagiBuilder
    {
        public static BrickBuildResult Build(
            IReadOnlyList<Ohlcv> bars,
            double reversal,
            TimeSpan barLength,
            int maxLines = BrickChartBuilder.MaxBricks)
        {
            if (!(reversal > 0) || double.IsInfinity(reversal))
                return new BrickBuildResult(
                    TimeSeriesBuffer<Ohlcv>.Empty,
                    ImmutableArray<BrickSpan>.Empty,
                    "Kagi needs a reversal amount above zero.");

            // (open, end, birth bar, first bar of the window, end bar, up)
            var lines = new List<(double Open, double End, int Birth, int First, int Last, bool Up)>();
            if (bars.Count == 0)
                return new BrickBuildResult(TimeSeriesBuffer<Ohlcv>.Empty, ImmutableArray<BrickSpan>.Empty, null);

            double first = bars[0].Close;
            for (int i = 0; i < bars.Count; i++)
            {
                double close = bars[i].Close;
                if (double.IsNaN(close)) continue;

                if (lines.Count == 0)
                {
                    if (Math.Abs(close - first) >= reversal)
                        lines.Add((first, close, i, 0, i, close > first));
                    continue;
                }

                var last = lines[^1];
                if (last.Up ? close > last.End : close < last.End)
                {
                    lines[^1] = last with { End = close, Last = i };
                    continue;
                }

                if (last.Up ? close <= last.End - reversal : close >= last.End + reversal)
                {
                    if (lines.Count >= maxLines)
                        return new BrickBuildResult(
                            TimeSeriesBuffer<Ohlcv>.Empty,
                            ImmutableArray<BrickSpan>.Empty,
                            $"Kagi would draw over {maxLines.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} lines.");
                    lines.Add((last.End, close, i, last.Last + 1, i, !last.Up));
                }
            }

            var drawn = new Ohlcv[lines.Count];
            var spans = ImmutableArray.CreateBuilder<BrickSpan>(lines.Count);
            for (int k = 0; k < lines.Count; k++)
            {
                var l = lines[k];
                double volume = 0;
                for (int b = l.First; b <= l.Last; b++) volume += bars[b].Volume;
                drawn[k] = new Ohlcv(bars[l.Birth].Date, l.Open, Math.Max(l.Open, l.End), Math.Min(l.Open, l.End), l.End, volume);
                spans.Add(new BrickSpan(bars[l.First].Date, bars[l.Last].Date + barLength, l.First, l.Last, l.Up));
            }

            return new BrickBuildResult(new TimeSeriesBuffer<Ohlcv>(drawn), spans.MoveToImmutable(), null);
        }
    }

    /// <summary>
    /// Thick (yang) and thin (yin) on a Kagi chart. The first line is thick going up and thin
    /// going down. Every later line starts as the line before it ended; an up line turns thick
    /// where it rises past the previous shoulder (the top of the line before, which is that
    /// line's Open), a down line turns thin where it falls past the previous waist (likewise the
    /// line before's Open). A thick line going down stays thick until then, and the same the
    /// other way: thickness is the trend, direction is the swing.
    /// </summary>
    public static class KagiLines
    {
        /// <summary>One line's look: thick at its start and at its end, and the price where it
        /// turned (NaN when it did not).</summary>
        public readonly record struct Look(bool ThickAtStart, bool ThickAtEnd, double TurnAt)
        {
            public bool Turned => !double.IsNaN(TurnAt);
        }

        /// <summary>Every line's look, in order.</summary>
        public static Look[] All(IReadOnlyList<Ohlcv> lines)
        {
            var looks = new Look[lines?.Count ?? 0];
            for (int i = 0; i < looks.Length; i++)
                looks[i] = Next(lines!, i, i == 0 ? default : looks[i - 1]);
            return looks;
        }

        /// <summary>The look of the line at <paramref name="index"/>; default when there is none.</summary>
        public static Look At(IReadOnlyList<Ohlcv>? lines, int index)
        {
            if (lines == null || index < 0 || index >= lines.Count) return default;
            Look look = default;
            for (int i = 0; i <= index; i++) look = Next(lines, i, look);
            return look;
        }

        private static Look Next(IReadOnlyList<Ohlcv> lines, int i, Look before)
        {
            var line = lines[i];
            bool up = line.Close > line.Open;
            if (i == 0) return new Look(up, up, double.NaN);

            bool start = before.ThickAtEnd;
            double edge = lines[i - 1].Open;
            if (up && !start && line.Close > edge) return new Look(false, true, edge);
            if (!up && start && line.Close < edge) return new Look(true, false, edge);
            return new Look(start, start, double.NaN);
        }

        /// <summary>"thick", "thin", "turns thick at 120.00" or "turns thin at 120.00".</summary>
        public static string Words(Look look) =>
            look.Turned
                ? $"turns {(look.ThickAtEnd ? "thick" : "thin")} at {Accessibility.SpeechPriceFormatter.FormatPrice(look.TurnAt)}"
                : look.ThickAtEnd ? "thick" : "thin";
    }
}
