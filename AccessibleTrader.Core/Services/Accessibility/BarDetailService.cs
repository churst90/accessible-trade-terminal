using System.Globalization;
using System.Text;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Provides deep, context-aware analysis of a single data point (bar).
    /// Used for the detailed summary command (Alt+Shift+D on every head since 2026-09-05).
    /// </summary>
    public class BarDetailService : IBarDetailService
    {
        private readonly IEventBus _eventBus;

        /// <summary>
        /// Optional so the many existing two-argument constructions keep working. When absent the
        /// detail summary simply omits the chart-formation clause rather than failing.
        /// </summary>
        private readonly IChartPatternCache? _patterns;

        /// <summary>
        /// The ONE candle classifier. Optional for the same reason as <see cref="_patterns"/> —
        /// the existing two-argument constructions keep working — but a default instance is
        /// substituted rather than the block being skipped, because a detail key that stops
        /// naming the candle is a silent regression rather than a visible one.
        /// </summary>
        private readonly ISdkCandlePatternAnalyzer _analyzer;

        public BarDetailService(
            IEventBus eventBus,
            IChartPatternCache? patterns = null,
            ISdkCandlePatternAnalyzer? analyzer = null)
        {
            _eventBus = eventBus;
            _patterns = patterns;
            _analyzer = analyzer ?? new SdkCandlePatternAnalyzer();
        }

        /// <inheritdoc />
        public void AnnounceDetails(WorkspaceState state)
        {
            // Alt+Shift+D is an EXPLICIT request. Answering it with pure silence is the
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

            // EVERYTHING AT THE BAR, IN ONE ANNOUNCEMENT. Cody, 2026-10-17: "alt shift d doesn't
            // seem to read the entire ladder either, it reads the current chart formation, the
            // current focused series and that's it". He was right, and the manual had promised
            // the opposite for months — "the candle's open, high, low, close and volume ... every
            // active indicator's reading there, and ... any signal events on the bar across all
            // indicators". This key read the focused series alone (for the candles not even the
            // four prices, only the shape and the wick percentages), so "everything the terminal
            // knows about a single moment" meant pressing Page Down and asking again, once per
            // series. And since the TradingView status line (ChartStatusLine) went in the same
            // day, the sighted view prints every visible indicator's reading in every pane at
            // once — Cody's rule is that nothing the status line shows may be reachable only by
            // eye, and "the ladder" is that block, all panes, top to bottom.
            //
            // The order:
            //   1. the bar's date;
            //   2. the FOCUSED series, exactly as before — a user who walked to an indicator
            //      hears the thing they walked to first, not the candle they walked away from;
            //   3. the candle as drawn (Heikin-Ashi aware): open, high, low, close, volume, then
            //      its shape, pattern and wick percentages. When the candles ARE the focused
            //      series this is step 2;
            //   4. every OTHER visible series in the status line's order — the price pane's
            //      overlays first, then the panes top to bottom as the renderer stacks them —
            //      each by name with its readings and the marks that fire at this bar (those are
            //      the "signal events on the bar across all indicators");
            //   5. the chart formations, last, as before.
            //
            // The trailing context the candle analyser needs is assembled by CandlePatternSpeech
            // from `state` itself. There used to be a 50-bar slice built here and handed down as
            // `recentData`; nothing in the method ever read it — it was allocated on every press
            // of the detail key and discarded, and it also predated the analyser knowing how to
            // find the classified bar inside a window.
            bool focusedIsCandles = IsCandles(series);
            var candles = state.ActiveSeries.FirstOrDefault(IsCandles);
            var volume = state.ActiveSeries.FirstOrDefault(IsVolume);

            // The candle is read when its series is on screen, or when it is the one in focus
            // (a focused series is always read, hidden or not, exactly as before). Volume rides
            // with the candle when the Volume series is shown and not already the focused one.
            bool readCandle = focusedIsCandles || (candles != null && candles.IsVisible);
            bool volumeWithCandle = readCandle && volume != null && volume.IsVisible && volume.Id != series.Id;

            var blocks = new List<string>();
            if (focusedIsCandles)
            {
                blocks.Add(CandleFact(state, bar, idx, volumeWithCandle));
            }
            else
            {
                blocks.Add(Sentence(FocusedSeriesFact(series, idx, bar.Date)));
                if (readCandle) blocks.Add(CandleFact(state, bar, idx, volumeWithCandle));
            }

            blocks.AddRange(LadderFacts(state, series, idx, readCandle, volumeWithCandle));

            string formations = ChartFormationDetail(state, idx);
            if (!string.IsNullOrEmpty(formations)) blocks.Add(formations);

            string detail = $"{SpeechTimeFormatter.FormatBarClock(bar.Date, PlaybackNarration.BarSeconds(state))}: "
                + string.Join(" ", blocks.Where(b => b.Length > 0));
            detail = detail.TrimEnd();

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

        private static bool IsCandles(ChartSeries s) =>
            s.Id == CoreSeriesIds.Candles || s.IndicatorCode == "CANDLES";

        private static bool IsVolume(ChartSeries s) =>
            s.Id.Equals(CoreSeriesIds.Volume, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.IndicatorCode, "VOLUME", StringComparison.OrdinalIgnoreCase);

        private static bool IsPrice(ChartSeries s) =>
            s.Id == CoreSeriesIds.Price || string.Equals(s.IndicatorCode, "PRICE", StringComparison.OrdinalIgnoreCase);

        /// <summary>A block that ends a sentence: a period added unless one is there already.</summary>
        private static string Sentence(string text)
        {
            text = text.TrimEnd();
            if (text.Length == 0) return "";
            return text.EndsWith('.') ? text : text + ".";
        }

        /// <summary>
        /// The candle as drawn: its four prices and volume, then its shape, where in a pattern it
        /// sits, which way it leans, and its body and wick percentages.
        ///
        /// <para>
        /// <b>The four prices are new on 2026-10-17.</b> This block used to name the shape and the
        /// wick percentages and stop, so the key the manual calls "the one-key way to gather
        /// everything the terminal knows about a single moment" could not say what the candle
        /// opened or closed at — the status line's symbol line prints both, and Cody's report was
        /// that the detail key read less than the screen showed. They come from
        /// <see cref="BarAsDrawn"/>, the same Heikin-Ashi-aware bar the status line's O H L C
        /// prints, so the ear and the eye agree about which candle this is. Volume is the raw
        /// bar's (a Heikin-Ashi chart re-draws prices, not volume) in the volume pane's own
        /// spoken form (<see cref="ChartStatusLine.FormatVolume"/>: exact, grouped, no fake
        /// ".00").
        /// </para>
        /// </summary>
        private string CandleFact(WorkspaceState state, Ohlcv bar, int index, bool includeVolume)
        {
            var sb = new StringBuilder();
            sb.Append($"Open {SpeechPriceFormatter.FormatPrice(bar.Open)}, high {SpeechPriceFormatter.FormatPrice(bar.High)}, " +
                      $"low {SpeechPriceFormatter.FormatPrice(bar.Low)}, close {SpeechPriceFormatter.FormatPrice(bar.Close)}");
            if (includeVolume)
                sb.Append($", volume {ChartStatusLine.FormatVolume(state.Data![index].Volume)}");
            sb.Append(". ");

            // THE SAME CLASSIFIER THE LIVE ANNOUNCEMENT USES, over the same trailing window.
            //
            // This block used to call a private ClassifyBar that knew five single-bar shapes
            // and nothing else, so "tell me everything about this bar" could not say
            // "morning star", "three white soldiers" or "bullish engulfing" on any bar the
            // user was not present for — which is every bar the detail key exists to read.
            // Its thresholds also disagreed with the analyser's (a 90% body was a marubozu
            // here, 95% there), so the same bar was named differently depending on whether
            // you asked about it or heard it close.
            //
            // Deliberately NOT gated on DescribeCandlePatterns, exactly as the chart-formation
            // clause is not gated on DescribeChartPatterns: those settings govern unsolicited
            // narration, and this key is the user asking a direct question.
            var analysis = CandlePatternSpeech.AnalyzeAt(
                _analyzer, state.Data, index, state.IsHeikinAshi, current: bar);
            string shape = CandlePatternSpeech.DescribeShape(analysis);
            string bias  = CandlePatternSpeech.Bias(analysis);

            // WHICH CANDLES. A three-bar pattern is only recognisable on its LAST bar, so
            // "three white soldiers" was announced on one bar in three and the other two said
            // nothing — a name with no way to find the candles it refers to, for a listener
            // who cannot see the chart. This clause says where in the pattern this bar sits,
            // on every bar of it. See CandlePatternSpeech.MembershipAt for why looking forward
            // is legitimate here and is not wired into the live routes.
            string member = CandlePatternSpeech.MembershipClause(analysis,
                CandlePatternSpeech.MembershipAt(_analyzer, state.Data, index, state.IsHeikinAshi));
            double range = bar.High - bar.Low;
            double body  = Math.Abs(bar.Close - bar.Open);
            double bodyPct = range > 0 ? (body / range) * 100.0 : 0;
            double upperWick = bar.High - Math.Max(bar.Open, bar.Close);
            double lowerWick = Math.Min(bar.Open, bar.Close) - bar.Low;
            double upperPct = range > 0 ? (upperWick / range) * 100.0 : 0;
            double lowerPct = range > 0 ? (lowerWick / range) * 100.0 : 0;

            // Shape, then WHERE in the pattern, then which way it leans: what it is, which
            // candles, what it means.
            var parts = new List<string> { shape };
            if (member.Length > 0) parts.Add(member);
            if (bias.Length > 0)   parts.Add(bias);
            string leadIn = string.Join(", ", parts) + ".";
            sb.Append($"{leadIn} Body {bodyPct.ToString("F0", CultureInfo.InvariantCulture)}%, Upper wick {upperPct.ToString("F0", CultureInfo.InvariantCulture)}%, Lower wick {lowerPct.ToString("F0", CultureInfo.InvariantCulture)}%.");
            return sb.ToString();
        }

        /// <summary>
        /// The focused (non-candle) series: every visible component's raw value, then the
        /// indicator's narrative facts. Unchanged by the 2026-10-17 rework apart from where it
        /// sits: it is still read first, so the series the user navigated to is the first thing
        /// heard. Price-line primary series (analytics providers) land here too — a single-value
        /// point has no wicks, body or multi-bar pattern to describe.
        /// </summary>
        private static string FocusedSeriesFact(ChartSeries series, int index, DateTime? barDate = null)
        {
            var sb = new StringBuilder();

            // Alt+Shift+D always reads raw component values regardless of indicator type.
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
                // A value label prints a NUMBER, often a whole one ("Bulls 15", "Bears minus 5"),
                // and is said that way, not as a price with two decimals.
                string said = comp.DisplayType == ComponentDisplayType.ValueLabel || comp.IsCount
                    ? Rendering.ValueLabelRenderer.Spoken(val)
                    : SpeechPriceFormatter.FormatPrice(val);
                sb.Append($"{comp.DisplayName ?? comp.Name} {said}, ");
            }

            foreach (var fact in NarrativeFacts(series, index, barDate))
                sb.Append(fact + ", ");

            return sb.ToString().TrimEnd(',', ' ');
        }

        /// <summary>
        /// Indicator-specific narrative facts. Layered after the raw value list so the user first
        /// hears "Upper 100.2, Lower 95.6" and then the interpretation ("band expanding,
        /// volatility increasing"). Each branch is a guarded noop when the series doesn't carry
        /// the required components or a NaN slice is in view. Read for the focused series and for
        /// every series on the ladder alike.
        /// </summary>
        /// <param name="barDate">Given for the focused series only: facts about the whole chart
        /// (Cipher C's cycle preset) are said there and not repeated on the ladder.</param>
        private static IEnumerable<string> NarrativeFacts(ChartSeries series, int index, DateTime? barDate = null)
        {
            string code = (series.IndicatorCode ?? string.Empty).ToUpperInvariant();
            if (code == "BB" || code == "BOLLINGER" || code == "BOLLINGERBANDS")
            {
                string bb = BollingerSqueezeExpansionFact(series, index);
                if (!string.IsNullOrEmpty(bb)) yield return bb;
            }
            else if (code == "MACD")
            {
                string macd = MacdCrossoverFact(series, index);
                if (!string.IsNullOrEmpty(macd)) yield return macd;
            }
            else if (code == Indicators.DssBressertProvider.Code)
            {
                // Its state in words (Cody, 2026-10-17: "dss narration should give its state"),
                // against the chart's OWN overbought/oversold lines, so a user who moved them to
                // 90/10 hears "overbought" where the line is.
                double ob = LevelNamed(series, "Overbought") ?? 80, os = LevelNamed(series, "Oversold") ?? 20;
                foreach (var w in Indicators.DssBressertProvider.StateWords(
                             series.GetComponentData(Indicators.DssBressertProvider.CompDss),
                             series.GetComponentData(Indicators.DssBressertProvider.CompTrigger), index, ob, os))
                    yield return w;
            }
            else if (code == Indicators.DbsiStyleProvider.Code)
            {
                // Who is in control, a hand-over or an entry score on this bar, and the side of the
                // trend line — the numbers themselves were just read as Bulls and Bears.
                foreach (var w in Indicators.DbsiStyleProvider.StateWords(series.GetComponentData, index))
                    yield return w;
            }
            else if (code == Indicators.CipherCProvider.Code)
            {
                // Where the daily and weekly cycles are against their timing windows, a
                // confirmation's confluence, a cancellation or failure that still stands, and (on
                // the focused series) the preset — read from the components' companion arrays.
                foreach (var w in Indicators.CipherCProvider.StateWords(series.GetComponentData, index, barDate))
                    yield return w;
            }
        }

        private static double? LevelNamed(ChartSeries series, string name) =>
            series.Levels.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

        /// <summary>
        /// THE LADDER: every other visible series at the bar, one sentence each, in the order the
        /// status line stacks them — the price pane's lines (<see cref="ChartStatusLine.MainPaneSeries"/>)
        /// and then each indicator pane top to bottom.
        ///
        /// <para>
        /// The pane order is the renderer's, by way of <see cref="ChartPaneModel.Panes"/> over the
        /// VISIBLE series: the renderer groups the visible series by pane in first-appearance
        /// order with the price pane on top, and so does ChartPaneModel. Feeding it every series
        /// would let a hidden series decide where a pane sits in the list when it is not where the
        /// pane sits on the screen.
        /// </para>
        ///
        /// <para>
        /// Hidden series are left out: a hidden series is not on the chart, and the status line
        /// does not print it. MUTED series are read: muted is about sound — a user mutes Cipher B
        /// so its tones stop competing with the candles, not because they no longer want to know
        /// its value — and this key is speech the user explicitly asked for.
        /// </para>
        ///
        /// <para>
        /// Skipped as already said: the focused series (step 2), and the candles, volume and the
        /// close line when the candle block read them. The close line is skipped only on a
        /// normal chart — on a Heikin-Ashi chart the candle block reads the HA close, and the
        /// Price line is the RAW close (ChartMath.BarAsDrawn), a different number.
        /// </para>
        /// </summary>
        private static IEnumerable<string> LadderFacts(
            WorkspaceState state, ChartSeries focused, int index, bool candleRead, bool volumeRead)
        {
            var visible = state.ActiveSeries.Where(s => s.IsVisible && !s.IsDrawing).ToList();
            foreach (var pane in ChartPaneModel.Panes(visible))
            {
                IEnumerable<ChartSeries> paneSeries = pane.Key.Equals(ChartPaneModel.MainPaneKey, StringComparison.OrdinalIgnoreCase)
                    ? ChartStatusLine.MainPaneSeries(pane.Series)
                    : pane.Series;
                foreach (var s in paneSeries)
                {
                    if (s.Id == focused.Id) continue;
                    if (candleRead && IsCandles(s)) continue;
                    if (volumeRead && IsVolume(s)) continue;
                    if (candleRead && !state.IsHeikinAshi && IsPrice(s)) continue;

                    string fact = LadderReading(s, index);
                    if (fact.Length > 0) yield return fact;
                }
            }
        }

        /// <summary>
        /// One series on the ladder: "Name: reading, reading, Mark." — or "Name: 55.20." for a
        /// one-plot indicator, whose name already says what the number is.
        ///
        /// <para>
        /// The components read are the ones the status line prints (ChartStatusLine.SeriesLine):
        /// visible and enabled, with a reading of their own (<see cref="ChartStatusLine.HasReading"/> —
        /// lines, oscillators, areas, bars, histograms; not a constant level, a cloud fill or a
        /// candle colouring) and a value somewhere on the chart
        /// (<see cref="ChartStatusLine.NoDataAnywhere"/>), or a marker. A marker is read by NAME and only when it fires at this
        /// bar — a mark is there or it is not, and those marks are the signal events the manual
        /// promises. A NaN reading (warm-up, a plot with nothing at this bar) is skipped, and a
        /// series with nothing to say at the bar is left out entirely rather than read as a bare
        /// name.
        /// </para>
        ///
        /// <para>
        /// The value is the status line's (the plot's raw value) in the detail key's spoken form:
        /// <see cref="SpeechPriceFormatter.FormatPrice"/>'s magnitude-aware digits, or the volume
        /// pane's form for a volume reading. The series is named as Page Up and Page Down name it
        /// (<see cref="ChartSeries.Name"/>).
        /// </para>
        /// </summary>
        private static string LadderReading(ChartSeries s, int index)
        {
            // Plots with no value on any bar of this chart are left out as the status line leaves
            // them out (ChartStatusLine.NoDataAnywhere) — and so do not count towards labelling.
            var empty = ChartStatusLine.NoDataAnywhere(s);
            bool labelled = s.Components.Count(c => c.IsVisible && c.IsEnabled
                && !ChartRenderer.IsMarker(c.DisplayType) && ChartStatusLine.HasReading(c.DisplayType)
                && !empty.Contains(c.Name)) > 1;

            var parts = new List<string>();
            foreach (var comp in s.Components)
            {
                if (!comp.IsVisible || !comp.IsEnabled) continue;
                bool marker = ChartRenderer.IsMarker(comp.DisplayType);
                if (!marker && !ChartStatusLine.HasReading(comp.DisplayType)) continue;
                if (!marker && empty.Contains(comp.Name)) continue;

                var data = s.GetComponentData(comp.Name);
                double v = data != null && index >= 0 && index < data.Length ? data[index] : double.NaN;
                if (double.IsNaN(v)) continue;

                string label = comp.DisplayName ?? comp.Name;
                if (marker) { parts.Add(label); continue; }

                bool isVolume = comp.Role == ComponentRole.Volume || IsVolume(s);
                string text = isVolume ? ChartStatusLine.FormatVolume(v)
                    : comp.DisplayType == ComponentDisplayType.ValueLabel || comp.IsCount ? Rendering.ValueLabelRenderer.Spoken(v)
                    : SpeechPriceFormatter.FormatPrice(v);
                parts.Add(labelled ? $"{label} {text}" : text);
            }

            parts.AddRange(NarrativeFacts(s, index));
            if (parts.Count == 0) return "";

            string name = !string.IsNullOrWhiteSpace(s.Name) ? s.Name.Trim()
                : !string.IsNullOrWhiteSpace(s.FriendlyName) ? s.FriendlyName.Trim() : s.Id;
            return Sentence($"{name}: {string.Join(", ", parts)}");
        }

        /// <summary>
        /// Returns "band squeezing" / "band expanding" / empty when current BB width is
        /// materially (±10 %) tighter or wider than the 20-bar rolling-average width. Falls back
        /// to a directional hint ("band narrowing" / "band widening") for smaller changes.
        /// Requires UpperBand and LowerBand component arrays with at least 21 non-NaN samples in view.
        /// </summary>
        private static string BollingerSqueezeExpansionFact(ChartSeries series, int index)
        {
            var upper = ComponentNamed(series, "UpperBand", "Upper");
            var lower = ComponentNamed(series, "LowerBand", "Lower");
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
        /// Detects a MACD-vs-Signal crossover on the current bar. Reads the "Macd" and "Signal"
        /// component arrays (standard Skender MACD layout) and compares prev-bar vs current-bar
        /// sign of (MACD − Signal). Empty string when no crossover or when component data is
        /// missing / NaN.
        /// </summary>
        private static string MacdCrossoverFact(ChartSeries series, int index)
        {
            if (index < 1) return string.Empty;
            var macd   = ComponentNamed(series, "Macd", "MACD");
            var signal = ComponentNamed(series, "Signal");
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


        /// <summary>
        /// The first of <paramref name="names"/> the series carries, matched exactly; an empty array
        /// when it carries none (which every caller already treats as "no fact").
        ///
        /// <para>
        /// The real names come FIRST. Until 2026-09-30 these facts asked for "Upper", "Lower" and
        /// "MACD"; the Bollinger and MACD providers name their components "UpperBand",
        /// "LowerBand" and "Macd", the lookup is case-sensitive, and a missing component reads as
        /// an empty array. So both clauses were silent on every real chart, and their only tests
        /// used the same wrong names. The old spellings stay as fallbacks for a plugin that
        /// uses them.
        /// </para>
        /// </summary>
        private static double[] ComponentNamed(ChartSeries series, params string[] names)
        {
            foreach (var name in names)
                if (series.Data.ComponentData.TryGetValue(name, out var values)) return values;
            return Array.Empty<double>();
        }
    }
}
