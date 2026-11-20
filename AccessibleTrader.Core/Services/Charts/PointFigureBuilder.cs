using System.Collections.Immutable;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Charts
{
    /// <summary>
    /// Point and figure: columns of X (price rising) and O (price falling), one box of price per
    /// mark, a column turning only when price comes back the reversal count of boxes. Pure: no
    /// state, no services, safe to call from a reducer.
    ///
    /// <para>
    /// Closes only, on a grid of whole boxes from zero. A box level is reached going up when a
    /// close is at or above it, going down when a close is at or below it. The first column is
    /// drawn when a close reaches a level one box past the first close's own: up, X from the
    /// level above the first close to the highest reached; down, O likewise. After that a close
    /// that reaches a level past the column's end extends the column to it; a close that comes
    /// back the reversal count of boxes from the end starts a new column the other way, one box
    /// in from the end (an O column under an X top of 105 at box 1 starts at 104). Anything else
    /// draws nothing.
    /// </para>
    ///
    /// <para>
    /// Each drawn bar is one column, drawn as the move it is: Open is the level it moved FROM (the
    /// end of the column before, or the first close's own level), Close its newest box, so its
    /// boxes are the whole boxes between the two (<see cref="PointFigureColumns.Boxes"/>) and a
    /// one-box column still has a direction. A column's Close moves while it grows and the column
    /// count moves only on a turn. A column's <c>Date</c> is the open time
    /// of the bar whose close started it and never changes. Its span is the real window it covers,
    /// from the bar after the previous column's newest box to the bar of its own newest box, and
    /// its volume is that window's.
    /// </para>
    /// </summary>
    public static class PointFigureBuilder
    {
        /// <summary>The fewest boxes a turn may take: one, a box back turns the column.</summary>
        public const int MinReversal = 1;

        /// <summary>The most boxes a turn may take.</summary>
        public const int MaxReversal = 20;

        /// <summary>TradingView's default reversal: three boxes.</summary>
        public const int DefaultReversal = 3;

        // A close on a box level divided by the box can land a hair under the level in floating
        // point (300 / 0.1 = 2999.9999999999995); this much is still the level.
        private const double Hair = 1e-9;

        /// <summary>The highest box level at or under <paramref name="price"/>.</summary>
        public static long UpLevel(double price, double box) => (long)Math.Floor(price / box + Hair);

        /// <summary>The lowest box level at or over <paramref name="price"/>.</summary>
        public static long DownLevel(double price, double box) => (long)Math.Ceiling(price / box - Hair);

        public static BrickBuildResult Build(
            IReadOnlyList<Ohlcv> bars,
            double box,
            int reversal,
            TimeSpan barLength,
            int maxColumns = BrickChartBuilder.MaxBricks)
        {
            if (!(box > 0) || double.IsInfinity(box))
                return new BrickBuildResult(
                    TimeSeriesBuffer<Ohlcv>.Empty,
                    ImmutableArray<BrickSpan>.Empty,
                    "Point and figure needs a box size above zero.");
            if (reversal < MinReversal || reversal > MaxReversal)
                return new BrickBuildResult(
                    TimeSeriesBuffer<Ohlcv>.Empty,
                    ImmutableArray<BrickSpan>.Empty,
                    ReversalRefusal);
            if (bars.Count == 0)
                return new BrickBuildResult(TimeSeriesBuffer<Ohlcv>.Empty, ImmutableArray<BrickSpan>.Empty, null);

            // (first box level, newest box level, birth bar, first bar of the window, bar of the newest box, up)
            var columns = new List<(long From, long To, int Birth, int First, int Last, bool Up)>();
            double first = double.NaN;
            for (int i = 0; i < bars.Count; i++)
            {
                double close = bars[i].Close;
                if (double.IsNaN(close)) continue;
                if (double.IsNaN(first)) { first = close; continue; }

                if (columns.Count == 0)
                {
                    long above = UpLevel(first, box) + 1, below = DownLevel(first, box) - 1;
                    long up = UpLevel(close, box), down = DownLevel(close, box);
                    if (up >= above) columns.Add((above, up, i, 0, i, true));
                    else if (down <= below) columns.Add((below, down, i, 0, i, false));
                    continue;
                }

                var last = columns[^1];
                if (last.Up)
                {
                    long up = UpLevel(close, box);
                    if (up > last.To) { columns[^1] = last with { To = up, Last = i }; continue; }
                    long down = DownLevel(close, box);
                    if (down > last.To - reversal) continue;
                    if (columns.Count >= maxColumns) return TooMany(maxColumns);
                    columns.Add((last.To - 1, down, i, last.Last + 1, i, false));
                }
                else
                {
                    long down = DownLevel(close, box);
                    if (down < last.To) { columns[^1] = last with { To = down, Last = i }; continue; }
                    long up = UpLevel(close, box);
                    if (up < last.To + reversal) continue;
                    if (columns.Count >= maxColumns) return TooMany(maxColumns);
                    columns.Add((last.To + 1, up, i, last.Last + 1, i, true));
                }
            }

            var drawn = new Ohlcv[columns.Count];
            var spans = ImmutableArray.CreateBuilder<BrickSpan>(columns.Count);
            for (int k = 0; k < columns.Count; k++)
            {
                var c = columns[k];
                double open = (c.Up ? c.From - 1 : c.From + 1) * box, close = c.To * box;
                double volume = 0;
                for (int b = c.First; b <= c.Last; b++) volume += bars[b].Volume;
                drawn[k] = new Ohlcv(bars[c.Birth].Date, open, Math.Max(open, close), Math.Min(open, close), close, volume);
                spans.Add(new BrickSpan(bars[c.First].Date, bars[c.Last].Date + barLength, c.First, c.Last, c.Up));
            }

            return new BrickBuildResult(new TimeSeriesBuffer<Ohlcv>(drawn), spans.MoveToImmutable(), null);
        }

        /// <summary>The refusal for a reversal outside <see cref="MinReversal"/> to <see cref="MaxReversal"/>.</summary>
        public static string ReversalRefusal =>
            $"Point and figure takes a reversal of {MinReversal} to {MaxReversal} boxes.";

        private static BrickBuildResult TooMany(int max) => new(
            TimeSeriesBuffer<Ohlcv>.Empty,
            ImmutableArray<BrickSpan>.Empty,
            $"Point and figure would draw over {max.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} columns.");
    }

    /// <summary>What a point and figure column is: X or O, and how many boxes.</summary>
    public static class PointFigureColumns
    {
        /// <summary>The boxes in <paramref name="column"/> at <paramref name="box"/>: the whole
        /// boxes from its Open (the level it moved from) to its Close. 0 when the box is unknown.</summary>
        public static int Boxes(Ohlcv column, double box) =>
            box > 0 && double.IsFinite(box)
                ? (int)Math.Round(Math.Abs(column.Close - column.Open) / box)
                : 0;

        /// <summary>"X, 4 boxes", "O, 1 box"; "X" or "O" alone when the box is unknown.</summary>
        public static string Words(Ohlcv column, double box) => Words(column.Close >= column.Open, Boxes(column, box));

        /// <summary>"X, 4 boxes", "O, 1 box"; "X" or "O" alone when <paramref name="boxes"/> is 0.</summary>
        public static string Words(bool up, int boxes) =>
            (up ? "X" : "O") + (boxes > 0 ? $", {boxes} box{(boxes == 1 ? "" : "es")}" : "");
    }
}
