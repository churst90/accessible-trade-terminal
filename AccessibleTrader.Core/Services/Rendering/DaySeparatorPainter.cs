using AccessibleTrader.Sdk.Models;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// A dotted vertical line where a new day starts on an intraday chart (TradingView calls
    /// them session breaks; MetaTrader, thinkorswim and NinjaTrader have the same). Settings >
    /// Appearance > Draw day separators, default OFF (Cody, 2026-11-23: "add them as a visual
    /// toggle with a default of off").
    ///
    /// <para>
    /// The day is the one the time axis and the spoken readouts name the bar in
    /// (<see cref="Accessibility.SpeechTimeFormatter.ToBarDisplay(DateTime, int)"/>): the user's
    /// clock, or UTC when Settings says so. A line drawn at UTC midnight on a Chicago chart sat
    /// six hours from where the axis printed the new date, and what a sighted helper pointed at
    /// would not be the bar the arrow keys called the first of the day.
    /// </para>
    ///
    /// <para>
    /// A daily-or-coarser chart has no line: every bar is its own day, so a separator would be a
    /// second grid. Drawn in every pane, under the data, like the gridlines.
    /// </para>
    /// </summary>
    public static class DaySeparatorPainter
    {
        /// <summary>
        /// The indices into <paramref name="bars"/> of each bar that starts a new day: a bar
        /// whose shown date differs from the bar before it. The line goes at that bar's left
        /// edge, between the two days. Empty when the bars are a day apart or more.
        /// </summary>
        internal static List<int> Boundaries(IReadOnlyList<Ohlcv> bars, int barSeconds, Func<DateTime, DateTime> shown)
        {
            var starts = new List<int>();
            if (bars.Count < 2 || barSeconds <= 0 || Accessibility.SpeechTimeFormatter.IsDaily(barSeconds)) return starts;
            DateTime previous = shown(bars[0].Date).Date;
            for (int i = 1; i < bars.Count; i++)
            {
                DateTime day = shown(bars[i].Date).Date;
                if (day != previous) starts.Add(i);
                previous = day;
            }
            return starts;
        }

        /// <summary>The bar indices with a separator, by the user's clock.</summary>
        internal static List<int> Boundaries(IReadOnlyList<Ohlcv> bars)
        {
            int barSeconds = ChartMath.BarSecondsOf(bars);
            return Boundaries(bars, barSeconds,
                stamp => Accessibility.SpeechTimeFormatter.ToBarDisplay(stamp, barSeconds));
        }

        /// <summary>The x of the line before local bar <paramref name="index"/>: its left edge.</summary>
        internal static float X(RenderContext ctx, int index) => ctx.PaneRect.Left + index * ctx.ItemWidth;

        public static void Paint(RenderContext ctx)
        {
            var starts = Boundaries(ctx.Data);
            if (starts.Count == 0) return;
            float d = ctx.Density;
            // The axis text colour, faded: the theme's gridline colour is muted for a grid seen
            // every 40 px, and a line drawn once a day has to be found, not merely tolerated.
            using var dash = SKPathEffect.CreateDash(new[] { 4f * d, 4f * d }, 0);
            using var paint = new SKPaint
            {
                Color = ctx.Theme.AxisText.WithAlpha(120),
                StrokeWidth = 1 * d,
                Style = SKPaintStyle.Stroke,
                IsAntialias = false,
                PathEffect = dash,
            };
            foreach (int i in starts)
            {
                float x = X(ctx, i);
                ctx.Canvas.DrawLine(x, ctx.PaneRect.Top, x, ctx.PaneRect.Bottom, paint);
            }
        }
    }
}
