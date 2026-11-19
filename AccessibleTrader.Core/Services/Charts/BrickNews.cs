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
        /// <paramref name="bars"/>' last entry is the bar that has just opened.
        /// </summary>
        public static IReadOnlyList<(Ohlcv Brick, BrickSpan Span)> CompletedAtClose(
            IReadOnlyList<Ohlcv> bars, double box, ChartIdentity? identity)
        {
            var none = Array.Empty<(Ohlcv, BrickSpan)>();
            if (bars == null || bars.Count < 2 || !(box > 0) || double.IsNaN(box) || double.IsInfinity(box))
                return none;

            var closed = new Ohlcv[bars.Count - 1];
            for (int i = 0; i < closed.Length; i++) closed[i] = bars[i];
            int closedBar = closed.Length - 1;

            var built = BrickChartBuilder.Build(closed, box, BrickChartBuilder.BarLength(identity, closed));
            if (!built.Ok) return none;

            var news = new List<(Ohlcv, BrickSpan)>();
            for (int i = built.Spans.Length - 1; i >= 0 && built.Spans[i].LastBar == closedBar; i--)
                news.Add((built.Bricks[i], built.Spans[i]));
            news.Reverse();
            return news;
        }

        /// <summary>
        /// The box a background Renko chart uses: the one the tab froze when Renko was chosen,
        /// else its settings against the bars. Zero when neither gives one.
        /// </summary>
        public static double BoxFor(IReadOnlyList<Ohlcv> bars, RenkoSettings? settings, double frozenBox)
        {
            if (frozenBox > 0) return frozenBox;
            return BrickChartBuilder.TryResolveBox(bars, settings ?? new RenkoSettings(), out double box, out _)
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
            string? ladder = null)
        {
            string what = string.Join(" ", new[] { identity.Symbol ?? "", identity.Timeframe ?? "" }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            string lead = what.Length == 0 ? "" : what + ": ";
            string tail = string.IsNullOrWhiteSpace(ladder) ? "" : " " + ladder.Trim();
            return lead + Summary(bricks) + "." + tail;
        }

        /// <summary>"new up brick, 95,000.00 to 95,500.00" without the symbol, for a toast body
        /// whose title already names the chart.</summary>
        public static string Summary(IReadOnlyList<(Ohlcv Brick, BrickSpan Span)> bricks)
        {
            if (bricks.Count == 0) return "";
            bool up = bricks[^1].Span.Up;
            string count = bricks.Count == 1 ? "new" : $"{bricks.Count} new";
            string noun = bricks.Count == 1 ? "brick" : "bricks";
            return $"{count} {(up ? "up" : "down")} {noun}, "
                 + $"{SpeechPriceFormatter.FormatPrice(bricks[0].Brick.Open)} to "
                 + $"{SpeechPriceFormatter.FormatPrice(bricks[^1].Brick.Close)}";
        }

        /// <summary>"BTCUSDT 1h: new brick" — the toast title.</summary>
        public static string Title(string symbol, string timeframe)
        {
            string what = string.Join(" ", new[] { symbol, timeframe }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return what.Length == 0 ? "New brick" : $"{what}: new brick";
        }
    }
}
