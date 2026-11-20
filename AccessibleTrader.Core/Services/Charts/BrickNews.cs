using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Charts
{
    /// <summary>
    /// What a Renko chart nobody is looking at has to say when one of its time bars closes: the
    /// bricks that close completed, or nothing.
    ///
    /// <para>
    /// Cody, 2026-11-19: "yes announce new bricks on renko views". Until then a background Renko
    /// tab, and the same chart with the browser closed, announced every real time-bar close at
    /// its real price — "close 95,123.40 at 14:00. New bar: open ..." — about a chart that draws
    /// no time bars at all. A Renko chart's news is a brick, so a close that completes none is
    /// not news, and is not said, toasted or chimed.
    /// </para>
    ///
    /// <para>
    /// The bricks are built from the CLOSED bars only (the forming bar is left out), and the ones
    /// whose completion bar is the bar that just closed are the news. The focused chart builds
    /// with the forming bar too, so it can say a brick mid-bar; a chart in the background says
    /// it at the close, once, and never says a brick the forming bar made and then took back.
    /// </para>
    /// </summary>
    public static class BrickNews
    {
        /// <summary>
        /// How many bars the browser-closed monitor fetches for a Renko chart. The brick edges are
        /// whole multiples of the box from the first bar on, but which way the bricks are going is
        /// learned from history: with too few bars the first brick can go the wrong way. A few
        /// hundred bars hold a reversal, after which the bricks are the tab's own.
        /// </summary>
        public const int HistoryBars = 500;

        /// <summary>
        /// The bricks the bar before the newest one completed, with their spans. Empty when that
        /// bar completed none, when there are fewer than two bars, or when the box is unusable.
        /// <paramref name="bars"/>' last entry is the bar that has just opened. On Kagi only a line
        /// that bar TURNED is news: a line growing on (its end moving to the new close) is the
        /// same line, and saying it on every bar would be every close again. On point and figure a
        /// column that bar grew IS news, box by box, as each Renko brick is: a column whose newest
        /// box that bar reached.
        /// </summary>
        public static IReadOnlyList<(Ohlcv Brick, BrickSpan Span)> CompletedAtClose(
            IReadOnlyList<Ohlcv> bars, double box, ChartIdentity? identity,
            PriceChartStyle style = PriceChartStyle.Renko, RenkoSettings? settings = null)
        {
            var none = Array.Empty<(Ohlcv, BrickSpan)>();
            if (bars == null || bars.Count < 2 || !(box > 0) || double.IsNaN(box) || double.IsInfinity(box))
                return none;

            var closed = new Ohlcv[bars.Count - 1];
            for (int i = 0; i < closed.Length; i++) closed[i] = bars[i];
            int closedBar = closed.Length - 1;

            var built = BuiltCharts.Build(style, closed, settings ?? new RenkoSettings(), box,
                BrickChartBuilder.BarLength(identity, closed));
            if (!built.Ok) return none;

            var news = new List<(Ohlcv, BrickSpan)>();
            for (int i = built.Spans.Length - 1; i >= 0 && built.Spans[i].LastBar == closedBar; i--)
            {
                if (style == PriceChartStyle.Kagi && built.Bricks[i].Date != closed[closedBar].Date) continue;
                news.Add((built.Bricks[i], built.Spans[i]));
            }
            news.Reverse();
            return news;
        }

        /// <summary>
        /// The buy or sell signal a point and figure column gave at the close of the bar before
        /// the newest one: "Buy signal, above the last X top at 105.00". Null when the column that
        /// bar moved broke no top or bottom, or had broken it already before that bar (a column
        /// gives its signal once, as the focused chart says it once). Built from the closed bars
        /// only, as <see cref="CompletedAtClose"/> is.
        /// </summary>
        public static string? PnfSignalAtClose(IReadOnlyList<Ohlcv> bars, double box, ChartIdentity? identity,
            RenkoSettings? settings = null)
        {
            if (bars == null || bars.Count < 2 || !(box > 0) || double.IsInfinity(box)) return null;
            settings ??= new RenkoSettings();
            var closed = bars.Take(bars.Count - 1).ToArray();
            var now = BuiltCharts.Build(PriceChartStyle.PointAndFigure, closed, settings, box,
                BrickChartBuilder.BarLength(identity, closed));
            if (!now.Ok || now.Spans.Length == 0 || now.Spans[^1].LastBar != closed.Length - 1) return null;
            int k = now.Bricks.Count - 1;
            string? words = PointFigureColumns.SignalWords(now.Bricks, k);
            if (words == null) return null;

            // The same column a bar earlier, if it was there: had it already given the signal?
            var before = BuiltCharts.Build(PriceChartStyle.PointAndFigure, closed[..^1], settings, box,
                BrickChartBuilder.BarLength(identity, closed));
            if (before.Ok && before.Bricks.Count == now.Bricks.Count
                && before.Bricks[k].Date == now.Bricks[k].Date
                && !double.IsNaN(PointFigureColumns.SignalLevel(before.Bricks, k)))
                return null;
            return words;
        }

        /// <summary>
        /// The box a background Renko chart uses: the one the tab froze when Renko was chosen,
        /// else its settings against the bars. Zero when neither gives one.
        /// </summary>
        public static double BoxFor(IReadOnlyList<Ohlcv> bars, RenkoSettings? settings, double frozenBox,
            PriceChartStyle style = PriceChartStyle.Renko)
        {
            if (frozenBox > 0) return frozenBox;
            return BuiltCharts.TryResolveBox(style, bars, settings ?? new RenkoSettings(), out double box, out _)
                ? box
                : 0;
        }

        /// <summary>
        /// "BTCUSDT 1h: new up brick, 95,000.00 to 95,500.00." — or, for a close that completed
        /// several, "BTCUSDT 1h: 3 new up bricks, 94,000.00 to 95,500.00." One close moves one
        /// way, so the bricks it completes all go one way. The ladder, when there is one, follows:
        /// it is about the newest brick.
        /// </summary>
        public static string Sentence(ChartIdentity identity, IReadOnlyList<(Ohlcv Brick, BrickSpan Span)> bricks,
            string? ladder = null, PriceChartStyle style = PriceChartStyle.Renko, double box = 0,
            string? signal = null)
        {
            string what = string.Join(" ", new[] { identity.Symbol ?? "", identity.Timeframe ?? "" }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            string lead = what.Length == 0 ? "" : what + ": ";
            string tail = string.IsNullOrWhiteSpace(ladder) ? "" : " " + ladder.Trim();
            return lead + Summary(bricks, style, box, signal) + "." + tail;
        }

        /// <summary>"new up brick, 95,000.00 to 95,500.00" without the symbol, for a toast body
        /// whose title already names the chart. Range bars say "range bar", line break "block".
        /// A point and figure column, new or grown, says its mark and boxes and not "new":
        /// "up column, X, 6 boxes, 100.00 to 106.00", and the signal it gave at this close when
        /// there is one (<see cref="PnfSignalAtClose"/>): "up column, X, 6 boxes, buy signal,
        /// above the last X top at 105.00, 100.00 to 106.00".</summary>
        public static string Summary(IReadOnlyList<(Ohlcv Brick, BrickSpan Span)> bricks,
            PriceChartStyle style = PriceChartStyle.Renko, double box = 0, string? signal = null)
        {
            if (bricks.Count == 0) return "";
            bool up = bricks[^1].Span.Up;
            if (style == PriceChartStyle.PointAndFigure)
            {
                var column = bricks[^1].Brick;
                return $"{(up ? "up" : "down")} {BuiltCharts.Noun(style)}, "
                     + PointFigureColumns.Words(up, PointFigureColumns.Boxes(column, box)) + ", "
                     + (string.IsNullOrEmpty(signal) ? "" : char.ToLowerInvariant(signal[0]) + signal[1..] + ", ")
                     + $"{SpeechPriceFormatter.FormatPrice(column.Open)} to "
                     + $"{SpeechPriceFormatter.FormatPrice(column.Close)}";
            }
            string count = bricks.Count == 1 ? "new" : $"{bricks.Count} new";
            string noun = BuiltCharts.Noun(style, bricks.Count);
            return $"{count} {(up ? "up" : "down")} {noun}, "
                 + $"{SpeechPriceFormatter.FormatPrice(bricks[0].Brick.Open)} to "
                 + $"{SpeechPriceFormatter.FormatPrice(bricks[^1].Brick.Close)}";
        }

        /// <summary>"BTCUSDT 1h: new brick" — the toast title. "BTCUSDT 1h: column moved" on point
        /// and figure, whose news may be a column growing.</summary>
        public static string Title(string symbol, string timeframe, PriceChartStyle style = PriceChartStyle.Renko)
        {
            string noun = BuiltCharts.Noun(style);
            string what = string.Join(" ", new[] { symbol, timeframe }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (style == PriceChartStyle.PointAndFigure)
                return what.Length == 0 ? "Column moved" : $"{what}: column moved";
            return what.Length == 0 ? $"New {noun}" : $"{what}: new {noun}";
        }
    }
}
