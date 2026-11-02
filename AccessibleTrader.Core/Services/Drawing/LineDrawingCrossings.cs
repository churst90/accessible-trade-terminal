using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Drawing.Calculators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing
{
    /// <summary>
    /// Where price crosses a horizontal line drawing, and where the cursor crosses a vertical one —
    /// shared by the crossing earcon (<see cref="LevelCrossingMonitor"/>) and by Ctrl+Left /
    /// Ctrl+Right (<c>IndicatorCrossingEngine</c>), so the key lands exactly where the chirp sounds.
    ///
    /// <para>
    /// "Price" is the bar's close as drawn — the Heikin-Ashi close on a Heikin-Ashi chart, since
    /// 2026-10-22, when Cody asked for the close TradingView uses there — so both callers hand in
    /// <c>ChartMath.SeriesAsDrawn</c>. A horizontal line is crossed on the bar whose close is on the other
    /// side of it from the bar before (<see cref="LevelCross.Direction"/> — the reference level's
    /// rule). A vertical line has no price to cross; it is crossed when the cursor moves onto its
    /// bar or past it.
    /// </para>
    /// </summary>
    public static class LineDrawingCrossings
    {
        /// <summary>A horizontal or vertical line that is switched on. Hidden drawings are neither
        /// heard nor stopped at: hiding one says you are not interested in it.</summary>
        public static bool IsCrossable(ChartSeries series)
            => series.IsVisible && series.Drawing is { Type: DrawingType.HorizontalLine or DrawingType.VerticalLine };

        /// <summary>
        /// The drawings with lines price can cross, other than the horizontal and vertical line:
        /// a fib's levels, a channel's and a pitchfork's three lines, risk/reward's entry, stop and
        /// target, the anchored VWAP, a head and shoulders' neckline and a triangle's two sides —
        /// and the trend line, for the earcon (Ctrl+Left/Right has walked trend lines on its own
        /// since before this).
        ///
        /// <para>
        /// Cody, 2026-11-01: <i>"yes i want the fib levels narrated."</i> Only horizontal and
        /// vertical lines chirped, and a focused fib answered Ctrl+Right with "No points of
        /// interest". A pattern's legs are not here: they join the points placed, and price
        /// crossing a leg means nothing.
        /// </para>
        /// </summary>
        public static bool HasPriceLines(ChartSeries series)
            => series.IsVisible && series.Drawing?.Type is DrawingType.TrendLine
                or DrawingType.FibRetracement or DrawingType.FibExtension or DrawingType.Channel
                or DrawingType.AndrewsPitchfork or DrawingType.RiskReward or DrawingType.AnchoredVwap
                or DrawingType.HeadAndShoulders or DrawingType.TrianglePattern;

        /// <summary>One crossable line of a drawing: what it is called when crossed, its value on
        /// each bar (NaN where it is not drawn), and the first bar a crossing can be on.</summary>
        public sealed record PriceLine(string Name, double[] Values, int From);

        /// <summary>
        /// The lines of <paramref name="series"/> price can cross, read from what its calculator
        /// drew, so a cross is heard where the line is drawn. Parts switched off are left out.
        ///
        /// <para>
        /// A fib's levels, risk/reward's lines and a pattern's neckline or sides are crossed only
        /// after the drawing's last point: they are drawn across the whole chart, and price going
        /// through the 61.8% level a year before the swing it measures is not news about that
        /// swing. A trend line is the line through its two points, without end, as
        /// Ctrl+Left/Right has always read it.
        /// </para>
        /// </summary>
        public static IEnumerable<PriceLine> PriceLines(ChartSeries series, IReadOnlyList<Ohlcv> data)
        {
            if (!HasPriceLines(series)) yield break;
            var d = series.Drawing!;

            if (d.Type == DrawingType.TrendLine)
            {
                if (TrendLineValues(d, data) is double[] line) yield return new PriceLine("trend line", line, 1);
                yield break;
            }

            int from = d.Type is DrawingType.FibRetracement or DrawingType.FibExtension or DrawingType.RiskReward
                    or DrawingType.HeadAndShoulders or DrawingType.TrianglePattern
                ? LastAnchorBar(d, data) + 1 : 1;
            foreach (var comp in series.Components)
            {
                if (!comp.IsVisible) continue;
                if (d.Type == DrawingType.HeadAndShoulders && comp.Name != HeadAndShouldersCalculator.Neckline) continue;
                if (d.Type == DrawingType.TrianglePattern
                    && comp.Name is not (TrianglePatternCalculator.Upper or TrianglePatternCalculator.Lower)) continue;
                var values = series.GetComponentData(comp.Name);
                if (values.Length == 0) continue;
                yield return new PriceLine(SpokenName(d.Type, comp.Name), values, Math.Max(1, from));
            }
        }

        /// <summary>
        /// The fixed prices a drawing marks, for a price alert: a horizontal line's price, each fib
        /// level's, and risk/reward's entry, stop loss and take profit. Parts switched off are left
        /// out. Sloped lines are not here — an alert holds a price, and the monitor that watches
        /// with the browser closed has no drawings to read a moving line from.
        ///
        /// <para>
        /// The alert is made at the price the level has when it is set. Moving the drawing
        /// afterwards does not move the alert; that is said when it is made.
        /// </para>
        /// </summary>
        public static IEnumerable<(string Key, string Name, double Price)> FixedPrices(ChartSeries series)
        {
            if (!series.IsVisible || series.Drawing is not { } d) yield break;
            if (HorizontalPrice(series) is double h)
            {
                yield return ("Line", "horizontal line", h);
                yield break;
            }
            if (d.Type is not (DrawingType.FibRetracement or DrawingType.FibExtension or DrawingType.RiskReward)) yield break;
            foreach (var comp in series.Components)
            {
                if (!comp.IsVisible) continue;
                double? price = d.Type == DrawingType.RiskReward
                    ? comp.Name switch { "Entry" => d.AnchorPrice1, "Stop Loss" => d.AnchorPrice2, "Take Profit" => d.AnchorPrice3, _ => null }
                    : FibLevels.LevelOf(comp.Name) is double level ? FibLevels.PriceAt(d, level) : null;
                if (price is double p && double.IsFinite(p)) yield return (comp.Name, SpokenName(d.Type, comp.Name), p);
            }
        }

        /// <summary>What a crossed line is called: "61.8% level", "stop loss", "neckline".</summary>
        internal static string SpokenName(DrawingType type, string key)
            => type is DrawingType.FibRetracement or DrawingType.FibExtension && FibLevels.LevelOf(key) is double level
                ? $"{FibLevels.Name(level)} {(type == DrawingType.FibExtension ? "extension" : "level")}"
             : key == "AVWAP" ? "anchored VWAP"
             : type == DrawingType.Channel ? $"channel {key.ToLowerInvariant()}"
             : type == DrawingType.AndrewsPitchfork ? $"pitchfork {key.ToLowerInvariant()}"
             : key.ToLowerInvariant();

        /// <summary>Which way the close crossed <paramref name="line"/> arriving at bar
        /// <paramref name="index"/>: +1, −1 or 0. The rule <see cref="LevelCross.Direction"/>
        /// uses, against the line's value on each of the two bars.</summary>
        public static int CrossAt(IReadOnlyList<Ohlcv> data, PriceLine line, int index)
        {
            if (index < line.From || index < 1 || index >= data.Count || index >= line.Values.Length) return 0;
            double before = line.Values[index - 1], now = line.Values[index];
            if (!double.IsFinite(before) || !double.IsFinite(now)) return 0;
            double previous = data[index - 1].Close, current = data[index].Close;
            if (previous < before && current >= now) return 1;
            if (previous >= before && current < now) return -1;
            return 0;
        }

        /// <summary>The bar of the drawing's last point (the latest of its anchors), or −1.</summary>
        internal static int LastAnchorBar(DrawingData d, IReadOnlyList<Ohlcv> data)
        {
            int last = -1;
            for (int slot = 1; slot <= 6; slot++)
                if (d.GetAnchorDate(slot) is DateTime at && data.Count > 0)
                    last = Math.Max(last, DrawingCalculatorHelper.ResolveAnchorIndex(data, at));
            return last;
        }

        /// <summary>The trend line through its two points on every bar — the line
        /// Ctrl+Left/Right walks (<c>IndicatorCrossingEngine</c>) — or null.</summary>
        private static double[]? TrendLineValues(DrawingData d, IReadOnlyList<Ohlcv> data)
        {
            if (d.AnchorDate1 is not DateTime d1 || d.AnchorDate2 is not DateTime d2
                || d.AnchorPrice1 is not double p1 || d.AnchorPrice2 is not double p2) return null;
            int i1 = -1, i2 = -1;
            for (int k = 0; k < data.Count; k++) { if (data[k].Date >= d1) { i1 = k; break; } }
            for (int k = 0; k < data.Count; k++) { if (data[k].Date >= d2) { i2 = k; break; } }
            if (i1 < 0 || i2 < 0 || i1 == i2) return null;
            double m = (p2 - p1) / (i2 - i1), b = p1 - m * i1;
            var values = new double[data.Count];
            for (int i = 0; i < values.Length; i++) values[i] = m * i + b;
            return values;
        }

        /// <summary>The horizontal line's price, or null for anything else.</summary>
        public static double? HorizontalPrice(ChartSeries series)
            => series.Drawing is { Type: DrawingType.HorizontalLine, AnchorPrice1: double p } && double.IsFinite(p) ? p : null;

        /// <summary>The bar a vertical line stands on — the first bar at or after its date, as the
        /// renderer and <c>VerticalLineCalculator</c> place it — or −1.</summary>
        public static int VerticalBar(ChartSeries series, IReadOnlyList<Ohlcv>? data)
        {
            if (data == null || series.Drawing is not { Type: DrawingType.VerticalLine, AnchorDate1: DateTime at })
                return -1;
            for (int i = 0; i < data.Count; i++)
                if (data[i].Date >= at) return i;
            return -1;
        }

        /// <summary>Which way the close crossed <paramref name="price"/> arriving at bar
        /// <paramref name="index"/> from the bar before it: +1, −1 or 0.</summary>
        public static int HorizontalCrossAt(IReadOnlyList<Ohlcv> data, int index, double price)
            => index < 1 || index >= data.Count ? 0
             : LevelCross.Direction(data[index - 1].Close, data[index].Close, price);

        /// <summary>
        /// Whether moving the cursor from <paramref name="from"/> to <paramref name="to"/> moved onto
        /// or across <paramref name="bar"/>. Leaving the bar is not crossing it — only arriving, or
        /// passing over it in one jump, is — so stepping off a vertical line is silent and stepping
        /// back onto it chirps again.
        /// </summary>
        public static bool VerticalCrossed(int from, int to, int bar)
        {
            if (bar < 0 || from == to) return false;
            return from < to ? bar > from && bar <= to : bar < from && bar >= to;
        }
    }
}
