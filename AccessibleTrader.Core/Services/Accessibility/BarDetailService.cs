using System.Globalization;
using System.Text;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Provides deep, context-aware analysis of a single data point (bar).
    /// Used for the detailed summary command (Ctrl+Shift+D, Alt+Shift+D on the web head).
    /// </summary>
    public class BarDetailService : IBarDetailService
    {
        private readonly IEventBus _eventBus;

        /// <summary>
        /// Optional so the many existing two-argument constructions keep working. When absent the
        /// detail summary simply omits the chart-formation clause rather than failing.
        /// </summary>
        private readonly IChartPatternCache? _patterns;

        public BarDetailService(IEventBus eventBus, IChartPatternCache? patterns = null)
        {
            _eventBus = eventBus;
            _patterns = patterns;
        }

        /// <inheritdoc />
        public void AnnounceDetails(WorkspaceState state)
        {
            // Ctrl+Shift+D is an EXPLICIT request. Answering it with pure silence is the
            // worst shape a failure can take here: the user asked a direct question and got
            // nothing back, with no way to tell a broken key from an empty chart. Both of
            // these were bare returns.
            if (state.Data == null || state.Data.Count == 0)
            {
                _eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Error, "No chart data to describe.", true));
                return;
            }

            var seriesId = state.FocusedSeriesId ?? state.PrimarySeriesId;
            var series = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            if (series == null)
            {
                _eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Error,
                    "No series in focus to describe. Press Page Up or Page Down to pick one.", true));
                return;
            }

            int idx = Math.Clamp(state.CurrentDataIndex, 0, state.Data.Count - 1);
            var bar = BarAsDrawn(state, idx);

            // Build a lookback slice (up to 50 bars before the current index) so GetDetailFact
            // can perform pattern/context analysis on real price data.
            int sliceStart = Math.Max(0, idx - 50);
            var dataSlice  = state.Data.Skip(sliceStart).Take(idx - sliceStart + 1).ToArray();
            string detail  = GetBarDetailFact(series, bar, idx, dataSlice);

            string formations = ChartFormationDetail(state, idx);
            if (!string.IsNullOrEmpty(formations))
                detail = string.IsNullOrEmpty(detail) ? formations : detail + " " + formations;

            _eventBus.Publish(new AnnouncementEvent(detail, true));
        }

        /// <summary>
        /// Every chart formation the cursor sits inside, in full.
        ///
        /// <para>
        /// This is the one place that reads the complete list. Arrow-key navigation deliberately
        /// describes only the dominant formation and counts the rest, because a region can satisfy
        /// four definitions at once and reading all four on every bar is how a user learns to
        /// switch the feature off. But "tell me everything about this bar" is precisely the request
        /// that should not be summarised — so the detail key enumerates them, ranked, each with its
        /// own trigger and measured target.
        /// </para>
        ///
        /// <para>
        /// Deliberately NOT gated on the <c>DescribeChartPatterns</c> setting. That setting governs
        /// unsolicited narration during navigation; this command is the user explicitly asking, and
        /// a key that stays silent because of a preference they set for a different purpose is the
        /// kind of thing that is impossible to diagnose by ear.
        /// </para>
        /// </summary>
        private string ChartFormationDetail(WorkspaceState state, int idx)
        {
            if (_patterns == null) return "";

            var all = _patterns.For(state.Identity, state.Data);
            if (all.Count == 0) return "";

            var here = ChartPatternNarrator.ByDominance(ChartPatternNarrator.AtBar(all, idx)).ToList();
            if (here.Count == 0) return MostRecentlyResolved(all, idx);

            var sb = new StringBuilder();
            sb.Append(here.Count == 1 ? "One formation here. " : $"{here.Count} formations here. ");
            foreach (var p in here)
                sb.Append(ChartPatternNarrator.Describe(p, SpeechPriceFormatter.FormatPrice)).Append(' ');
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// When nothing is live at the cursor, name the last formation that finished and say how.
        ///
        /// <para>
        /// A formation drops out of the live window the moment it resolves, which is right for
        /// arrow-key navigation — repeating old shapes forever is how the feature becomes noise.
        /// But it left the detail key answering "no chart formation here" on a bar sitting twenty
        /// bars after a double top broke, which is misleading in a way that matters: the level that
        /// broke is still the most relevant price on the screen, and the pattern that produced it
        /// is the reason.
        /// </para>
        ///
        /// <para>
        /// This looks BACKWARD only. A formation whose structure is not yet knowable at this bar
        /// stays unmentionable, exactly as during navigation — the point is to recover context that
        /// has passed, never to preview context that has not arrived.
        /// </para>
        /// </summary>
        private static string MostRecentlyResolved(IReadOnlyList<ChartPattern> all, int idx)
        {
            var last = all
                .Where(p => p.ResolvesAt < idx && p.KnownAtIndex <= idx)
                .OrderByDescending(p => p.ResolvesAt)
                .FirstOrDefault();

            if (last == null) return "No chart formation here.";

            int ago = idx - last.ResolvesAt;
            string what = ChartPatternNarrator.Describe(last, SpeechPriceFormatter.FormatPrice);
            return $"No formation here. Most recent, {ago} {(ago == 1 ? "bar" : "bars")} ago: {what}";
        }

        /// <summary>
        /// The bar as the user is actually looking at it: the raw candle normally, its Heikin-Ashi
        /// equivalent when that mode is on.
        ///
        /// <para>
        /// This readout used to take <c>state.Data[idx]</c> unconditionally while
        /// <see cref="NavigationFeedbackManager"/> applied the Heikin-Ashi transform — so with HA
        /// active the two paths described the same bar differently, and the detail key described a
        /// candle that was not on screen. Body and wick percentages are where that showed up
        /// loudest: HA candles routinely have NO shadow on one side (that is the shaved look of a
        /// trending HA series) while the raw bar underneath has one, so the terminal reported a
        /// lower wick of 19% for a candle drawn without a lower wick at all. Reported from live
        /// use, and the numbers were never wrong about the raw bar — they were about the wrong bar.
        /// </para>
        ///
        /// <para>
        /// The transform itself now lives in <see cref="ChartMath.BarAsDrawn"/>, shared with
        /// <c>NavigationFeedbackManager</c> and <c>NavigationSonifier</c>, which each carried
        /// their own copy of it.
        /// </para>
        /// </summary>
        private static Ohlcv BarAsDrawn(WorkspaceState state, int idx)
            => ChartMath.BarAsDrawn(state.Data!, idx, state.IsHeikinAshi);

        private string GetBarDetailFact(ChartSeries series, Ohlcv bar, int index, Ohlcv[] recentData)
        {
            var sb = new StringBuilder();
            sb.Append($"{SpeechTimeFormatter.FormatTime(bar.Date)}: ");

            // If it's the primary candle series, add candle pattern details
            // Pattern/type details only apply to true OHLCV series. Price-line primary
            // series (analytics providers) deliberately skip this block — a single-value
            // point has no wicks, body, or multi-bar pattern to describe.
            if (series.Id == CoreSeriesIds.Candles || series.IndicatorCode == "CANDLES")
            {
                string trend = bar.Close >= bar.Open ? "Bullish" : "Bearish";
                string type  = ClassifyBar(bar);
                double range = bar.High - bar.Low;
                double body  = Math.Abs(bar.Close - bar.Open);
                double bodyPct = range > 0 ? (body / range) * 100.0 : 0;
                double upperWick = bar.High - Math.Max(bar.Open, bar.Close);
                double lowerWick = Math.Min(bar.Open, bar.Close) - bar.Low;
                double upperPct = range > 0 ? (upperWick / range) * 100.0 : 0;
                double lowerPct = range > 0 ? (lowerWick / range) * 100.0 : 0;

                sb.Append($"{trend} {type}. Body {bodyPct.ToString("F0", CultureInfo.InvariantCulture)}%, Upper wick {upperPct.ToString("F0", CultureInfo.InvariantCulture)}%, Lower wick {lowerPct.ToString("F0", CultureInfo.InvariantCulture)}%. ");
                return sb.ToString().TrimEnd();
            }

            // Ctrl+Shift+D always reads raw component values regardless of indicator type.
            // GetDetailFact is intentionally bypassed here so every indicator reveals its
            // actual numeric column values rather than a condensed narrative summary.
            foreach (var comp in series.Components)
            {
                if (!comp.IsVisible) continue;

                var data = series.GetComponentData(comp.Name);
                if (index < 0 || index >= data.Length) continue;

                double val = data[index];

                if (comp.UsesGradientSpeech)
                {
                    var colorData = series.GetComponentData(comp.Name + "_color");
                    if (colorData != null && index < colorData.Length && !double.IsNaN(colorData[index]))
                        val = colorData[index];
                }

                if (double.IsNaN(val)) continue;

                // Magnitude-aware, not F2: the raw dump must not collapse sub-dollar
                // prices (or tiny oscillator values like a MACD of 0.0012) to "0.00".
                // For ordinary magnitudes it reads identically to the old F2.
                sb.Append($"{comp.DisplayName ?? comp.Name} {SpeechPriceFormatter.FormatPrice(val)}, ");
            }

            // Indicator-specific narrative facts. Layered after the raw value list so the user
            // first hears "Upper 100.2, Lower 95.6" and then the interpretation ("band expanding,
            // volatility increasing"). Each branch is a guarded noop when the series doesn't
            // carry the required components or a NaN slice is in view.
            string code = (series.IndicatorCode ?? string.Empty).ToUpperInvariant();
            if (code == "BB" || code == "BOLLINGER" || code == "BOLLINGERBANDS")
            {
                string bb = BollingerSqueezeExpansionFact(series, index);
                if (!string.IsNullOrEmpty(bb)) sb.Append(bb + ", ");
            }
            else if (code == "MACD")
            {
                string macd = MacdCrossoverFact(series, index);
                if (!string.IsNullOrEmpty(macd)) sb.Append(macd + ", ");
            }

            return sb.ToString().TrimEnd(',', ' ');
        }

        /// <summary>
        /// Returns "band squeezing" / "band expanding" / empty when current BB width is
        /// materially (±10 %) tighter or wider than the 20-bar rolling-average width. Falls back
        /// to a directional hint ("band narrowing" / "band widening") for smaller changes.
        /// Requires Upper and Lower component arrays with at least 21 non-NaN samples in view.
        /// </summary>
        private static string BollingerSqueezeExpansionFact(ChartSeries series, int index)
        {
            var upper = series.GetComponentData("Upper");
            var lower = series.GetComponentData("Lower");
            if (upper == null || lower == null) return string.Empty;
            if (index < 20 || index >= upper.Length || index >= lower.Length) return string.Empty;

            double curWidth = upper[index] - lower[index];
            if (double.IsNaN(curWidth) || curWidth <= 0) return string.Empty;

            double sumWidth = 0;
            int samples = 0;
            for (int k = index - 19; k <= index; k++)
            {
                if (k < 0) continue;
                double w = upper[k] - lower[k];
                if (double.IsNaN(w) || w <= 0) continue;
                sumWidth += w;
                samples++;
            }
            if (samples < 10) return string.Empty;
            double avg = sumWidth / samples;
            if (avg <= 0) return string.Empty;

            double pct = (curWidth - avg) / avg;
            if (pct < -0.10) return "band squeezing, low volatility";
            if (pct >  0.10) return "band expanding, volatility rising";
            if (pct < -0.03) return "band narrowing";
            if (pct >  0.03) return "band widening";
            return string.Empty;
        }

        /// <summary>
        /// Detects a MACD-vs-Signal crossover on the current bar. Reads the "MACD" and "Signal"
        /// component arrays (standard Skender MACD layout) and compares prev-bar vs current-bar
        /// sign of (MACD − Signal). Empty string when no crossover or when component data is
        /// missing / NaN.
        /// </summary>
        private static string MacdCrossoverFact(ChartSeries series, int index)
        {
            if (index < 1) return string.Empty;
            var macd   = series.GetComponentData("MACD");
            var signal = series.GetComponentData("Signal");
            if (macd == null || signal == null) return string.Empty;
            if (index >= macd.Length || index >= signal.Length) return string.Empty;

            double m  = macd[index];
            double s  = signal[index];
            double mp = macd[index - 1];
            double sp = signal[index - 1];
            if (double.IsNaN(m) || double.IsNaN(s) || double.IsNaN(mp) || double.IsNaN(sp)) return string.Empty;

            if (mp <= sp && m >  s) return "MACD crossed above signal, bullish";
            if (mp >= sp && m <  s) return "MACD crossed below signal, bearish";
            return string.Empty;
        }

        private static string ClassifyBar(Ohlcv bar)
        {
            double range = bar.High - bar.Low;
            if (range <= 0) return "Flat";

            double body       = Math.Abs(bar.Close - bar.Open);
            double bodyPct    = body / range;
            double upperWick  = bar.High - Math.Max(bar.Open, bar.Close);
            double lowerWick  = Math.Min(bar.Open, bar.Close) - bar.Low;
            double upperPct   = upperWick / range;
            double lowerPct   = lowerWick / range;

            if (bodyPct < 0.05)
            {
                if (lowerPct > 0.6 && upperPct < 0.1) return "Dragonfly Doji";
                if (upperPct > 0.6 && lowerPct < 0.1) return "Gravestone Doji";
                return "Doji";
            }
            if (bodyPct > 0.90) return "Marubozu";
            if (bodyPct < 0.30 && lowerPct > 0.60 && upperPct < 0.10) return "Hammer";
            if (bodyPct < 0.30 && upperPct > 0.60 && lowerPct < 0.10) return "Shooting Star";
            if (bodyPct < 0.30 && upperPct > 0.25 && lowerPct > 0.25) return "Spinning Top";

            return "Standard Candle";
        }
    }
}
