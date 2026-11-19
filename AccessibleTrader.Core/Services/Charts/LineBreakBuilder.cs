using System.Collections.Immutable;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Charts
{
    /// <summary>
    /// Line break (three-line break by default): a block is drawn only when a close goes past the
    /// last block, and the chart turns only when a close breaks the extreme of the last
    /// <c>lines</c> blocks. Pure: no state, no services, safe to call from a reducer.
    ///
    /// <para>
    /// Closes only. The first block runs from the first bar's close to the first close that
    /// differs from it. After that, a close beyond the last block's close draws a block on in the
    /// same direction, from that close to the new one. A close the other way draws nothing until
    /// it is beyond the lowest low (or highest high) of the last <c>lines</c> blocks, or of all of
    /// them while there are fewer; the turning block runs from the last block's open to the new
    /// close. Blocks have no fixed size: each is as tall as the move that made it.
    /// </para>
    ///
    /// <para>
    /// A block's <c>Date</c> is the open time of the bar whose close made it; its span is the real
    /// window from the first bar after the previous block to that bar's close, as Renko's.
    /// </para>
    /// </summary>
    public static class LineBreakBuilder
    {
        public const int MinLines = 1;
        public const int MaxLines = 50;

        public static BrickBuildResult Build(
            IReadOnlyList<Ohlcv> bars,
            int lines,
            TimeSpan barLength,
            int maxBlocks = BrickChartBuilder.MaxBricks)
        {
            if (lines < MinLines || lines > MaxLines)
                return new BrickBuildResult(
                    TimeSeriesBuffer<Ohlcv>.Empty,
                    ImmutableArray<BrickSpan>.Empty,
                    $"Line break takes {MinLines} to {MaxLines} lines.");

            var blocks = new List<Ohlcv>();
            var spans = new List<BrickSpan>();
            if (bars.Count == 0)
                return new BrickBuildResult(new TimeSeriesBuffer<Ohlcv>(blocks), spans.ToImmutableArray(), null);

            double first = bars[0].Close;
            int lastCompletionBar = -1;
            double pendingVolume = 0;

            for (int i = 0; i < bars.Count; i++)
            {
                pendingVolume += bars[i].Volume;
                double close = bars[i].Close;

                double from;
                bool up;
                if (blocks.Count == 0)
                {
                    if (close == first) continue;
                    from = first;
                    up = close > first;
                }
                else
                {
                    var last = blocks[^1];
                    bool lastUp = spans[^1].Up;
                    int n = Math.Min(lines, blocks.Count);
                    double lowest = double.MaxValue, highest = double.MinValue;
                    for (int k = blocks.Count - n; k < blocks.Count; k++)
                    {
                        lowest = Math.Min(lowest, blocks[k].Low);
                        highest = Math.Max(highest, blocks[k].High);
                    }

                    if (lastUp && close > last.Close) { from = last.Close; up = true; }
                    else if (!lastUp && close < last.Close) { from = last.Close; up = false; }
                    else if (lastUp && close < lowest) { from = last.Open; up = false; }
                    else if (!lastUp && close > highest) { from = last.Open; up = true; }
                    else continue;
                }

                if (blocks.Count >= maxBlocks)
                    return new BrickBuildResult(
                        TimeSeriesBuffer<Ohlcv>.Empty,
                        ImmutableArray<BrickSpan>.Empty,
                        $"Line break would draw over {maxBlocks.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} blocks.");

                int firstBar = lastCompletionBar < 0 ? 0 : Math.Min(lastCompletionBar + 1, i);
                blocks.Add(new Ohlcv(bars[i].Date, from, Math.Max(from, close), Math.Min(from, close), close, pendingVolume));
                spans.Add(new BrickSpan(bars[firstBar].Date, bars[i].Date + barLength, firstBar, i, up));
                pendingVolume = 0;
                lastCompletionBar = i;
            }

            return new BrickBuildResult(new TimeSeriesBuffer<Ohlcv>(blocks), spans.ToImmutableArray(), null);
        }
    }
}
