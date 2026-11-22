using System.Globalization;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Describes the chart's LAYOUT — what the axes measure, at what scale, how many panes there
    /// are and what lives in each.
    ///
    /// <para>
    /// This is the one thing a sighted user gets for free by glancing at the screen, and until now
    /// the only route to it was navigating every pane and counting. Everything else the terminal
    /// speaks answers "what is the value here?"; this answers "what am I looking at?", which is
    /// the question you have before the first one — and the question you come back to after
    /// loading a saved workspace, or switching tabs, or returning from lunch.
    /// </para>
    ///
    /// <para>
    /// Pure and static: it takes a state and returns a sentence. No DI, no events, no side
    /// effects, so what it says can be tested exactly rather than approximately.
    /// </para>
    /// </summary>
    public static class ChartLayoutDescriber
    {
        /// <summary>
        /// A spoken summary of the chart's structure.
        /// </summary>
        /// <param name="state">Current workspace.</param>
        /// <param name="symbol">Symbol on the chart, if known.</param>
        /// <param name="timeframe">Bar interval, e.g. "1d".</param>
        public static string Describe(WorkspaceState state, string? symbol = null, string? timeframe = null)
        {
            if (state == null) return "No chart loaded.";

            var parts = new List<string>();
            var bars = state.Data;

            if (bars == null || bars.Count == 0)
                return "No chart loaded.";

            // ── What, and at what interval ────────────────────────────────
            bool brickChart = state.ChartStyle.IsBrickStyle();
            string head = string.IsNullOrWhiteSpace(symbol) ? "Chart" : symbol!;
            if (!brickChart && !string.IsNullOrWhiteSpace(timeframe))
                head += $", {SpokenTimeframe(timeframe!)} per bar";
            parts.Add(head + ".");

            // ── X axis: how much time is on screen, and which slice ───────
            int start = Math.Clamp(state.ViewportStartIndex, 0, Math.Max(0, bars.Count - 1));
            int length = Math.Clamp(state.ViewportLength, 1, Math.Max(1, bars.Count - start));
            int end = Math.Min(bars.Count - 1, start + length - 1);

            var first = bars[start];
            var last = bars[end];
            if (brickChart
                && BrickChartSpeech.TryGetBrick(state, start, out _, out var firstBrick)
                && BrickChartSpeech.TryGetBrick(state, end, out _, out var lastBrick))
            {
                parts.Add($"X axis, time: {length} {Charts.BuiltCharts.Noun(state.ChartStyle, length)} in view of "
                    + $"{bars.Count.ToString("N0", CultureInfo.InvariantCulture)} drawn, "
                    + $"{BrickChartSpeech.Window(state, firstBrick)} to "
                    + $"{BrickChartSpeech.Window(state, lastBrick)}.");
            }
            else
            {
                parts.Add($"X axis, time: {length} bars in view of {bars.Count.ToString("N0", CultureInfo.InvariantCulture)} loaded, " +
                          $"{BarDate(first.Date, state, timeframe)} to {BarDate(last.Date, state, timeframe)}.");
            }

            // ── Y axis: the range, and the step between gridlines ─────────
            var (min, max) = state.ViewportRange;
            double scaleBase = ChartMath.ScaleBase(state);
            var scaleMode = ChartMath.EffectiveScaleMode(state.ScaleMode, scaleBase, state.IsLogScale);
            if (max > min && !double.IsNaN(min) && !double.IsNaN(max))
            {
                string scale = state.IsLogScale ? "logarithmic"
                    : scaleMode == PriceScaleMode.Percent ? "percent"
                    : scaleMode == PriceScaleMode.IndexedTo100 ? "indexed to 100"
                    : "linear";
                // On a relabelled axis the gridline step is in scale units, not dollars, so
                // quoting a price step here would describe a grid that is not on screen.
                var (scaleLo, scaleHi) = ChartMath.ScaleBounds(scaleMode, min, max, scaleBase);
                string scaleStep = GridStep(scaleHi - scaleLo).ToString("0.##", CultureInfo.InvariantCulture);
                string stepClause = scaleMode switch
                {
                    PriceScaleMode.Percent => $"about {scaleStep} percent between gridlines",
                    PriceScaleMode.IndexedTo100 => $"about {scaleStep} index points between gridlines",
                    _ => $"about {Money(GridStep(max - min))} between gridlines",
                };
                parts.Add($"Y axis, price: {Money(min)} to {Money(max)}, " +
                          $"{stepClause}, {scale} scale.");
                // Upside down is the one thing about this axis the eye gets wrong first.
                if (state.IsPriceInverted)
                    parts.Add($"Inverted: {Money(max)} at the bottom, {Money(min)} at the top; the pitch is turned over with it.");
            }

            // The base moves as the user pans, and panning must not talk on every arrow — so
            // the orientation sentence is where the current zero/100 is learned.
            if (ScaleSentence(state, bars[start].Date, timeframe, scaleMode, scaleBase) is { } scaleSentence)
                parts.Add(scaleSentence);

            // ── Panes: how the vertical space is divided ──────────────────
            var visible = state.ActiveSeries.Where(s => s.IsVisible).ToList();
            var byPane = visible.GroupBy(s => string.IsNullOrEmpty(s.Pane) ? "Main" : s.Pane).ToList();

            var main = byPane.FirstOrDefault(g => g.Key.Equals("Main", StringComparison.OrdinalIgnoreCase));
            int mainCount = main?.Count() ?? 0;
            parts.Add($"Main pane: {Count(mainCount, "series")}.");

            var others = byPane.Where(g => g != main).ToList();
            parts.Add(others.Count == 0
                ? "No separate indicator panes."
                : $"{Count(others.Count, "indicator pane")} below it, each with its own Y axis: " +
                  string.Join(", ", others.Select(g => $"{g.Key} with {Count(g.Count(), "series")}")) + ".");

            // ── What is switched off ──────────────────────────────────────
            // Stated because it explains a silent or empty-looking chart, and because the two
            // recovery shortcuts are useless to someone who does not know there is anything to
            // recover.
            int hidden = state.ActiveSeries.SelectMany(s => s.Components).Count(c => !c.IsVisible);
            int muted  = state.ActiveSeries.SelectMany(s => s.Components).Count(c => c.IsMuted);
            if (hidden > 0 || muted > 0)
            {
                var notes = new List<string>();
                if (hidden > 0) notes.Add($"{Count(hidden, "component")} hidden");
                if (muted > 0) notes.Add($"{Count(muted, "component")} muted");
                parts.Add(string.Join(", ", notes) + ".");
            }

            if (state.IsHeikinAshi) parts.Add("Heikin Ashi candles.");
            else if (state.ChartStyle == PriceChartStyle.Line) parts.Add("Line chart of closes.");
            else if (state.ChartStyle == PriceChartStyle.Area) parts.Add("Area chart of closes.");
            else if (Charts.ChartTypeWords.ChangeSentence(state) is { } typeSentence) parts.Add(typeSentence);
            else if (state.ChartStyle.IsBrickStyle()) parts.Add(BrickChartSpeech.ChangeSentence(state));

            // ── Is this chart LIVE? ──────────────────────────────────────────
            //
            // Alt+Shift+L is the orientation key — "the one thing a sighted user gets for free
            // by glancing at the screen" — and until now it could not answer the question that
            // matters most about a trading chart. Three watchdogs each spoke once into a
            // transient channel and left nothing to ask. A user who missed the line had no way
            // to find out whether the prices in front of them were current.
            //
            // The elapsed time, not just the word: "no data for eleven minutes" is actionable
            // in a way that "stale" is not.
            parts.Add(DescribeFeedFreshness(state));

            return string.Join(" ", parts);
        }

        /// <summary>
        /// Describes ONE PANE — the one the cursor is in — rather than the whole chart: what each
        /// of its two axes measures, the range each covers, and the step between gridlines.
        ///
        /// <para>
        /// This is the readback the five-key traversal needs to be usable. <see cref="Describe"/>
        /// answers "what is on this chart", which is asked once on arrival; this answers "what
        /// scale am I reading against", which is asked on every move into an unfamiliar band. A
        /// spoken value is meaningless without it — "62" is a fact about an oscillator only if
        /// you know the pane runs 0 to 100, and a sighted user reads that off an axis they can
        /// see while the terminal has never said it out loud.
        /// </para>
        ///
        /// <para>
        /// The Y range comes from <c>state.PaneRanges</c>, which is the SAME dictionary the
        /// renderer scales the pane with — so the numbers spoken are the numbers drawn. The Main
        /// pane falls back to <c>ViewportRange</c> when no entry exists yet, which is the state a
        /// chart is in before its first render.
        /// </para>
        /// </summary>
        /// <param name="state">Current workspace.</param>
        /// <param name="timeframe">Bar interval, e.g. "1d" — the X axis's step.</param>
        public static string DescribePane(WorkspaceState state, string? timeframe = null)
        {
            if (state == null) return "No chart loaded.";

            var bars = state.Data;
            if (bars == null || bars.Count == 0) return "No chart loaded.";

            var panes = ChartPaneModel.Panes(state.ActiveSeries);
            if (panes.Count == 0) return "No panes on this chart.";

            var focused = state.ActiveSeries.FirstOrDefault(x => x.Id == (state.FocusedSeriesId ?? "candles"));
            string currentKey = focused != null ? ChartPaneModel.KeyOf(focused) : panes[0].Key;
            var pane = panes.FirstOrDefault(p => string.Equals(p.Key, currentKey, StringComparison.Ordinal))
                       ?? panes[0];

            var parts = new List<string>();

            // ── Which pane, and where it sits ─────────────────────────────
            // The ordinal is only spoken when it tells the user a key has somewhere to go —
            // the same rule that drops "1 component" on a series switch.
            int ordinal = panes.ToList().FindIndex(p => ReferenceEquals(p, pane)) + 1;
            parts.Add(panes.Count > 1
                ? $"{pane.DisplayName} pane, {ordinal} of {panes.Count}."
                : $"{pane.DisplayName} pane.");

            int start = Math.Clamp(state.ViewportStartIndex, 0, Math.Max(0, bars.Count - 1));
            int length = Math.Clamp(state.ViewportLength, 1, Math.Max(1, bars.Count - start));
            int end = Math.Min(bars.Count - 1, start + length - 1);

            // ── Y axis: what it measures, over what range, at what step ───
            string measure = MeasureOf(pane);
            var (yMin, yMax) = PaneRange(state, pane);
            bool priceLike = pane.Key.Equals(ChartPaneModel.MainPaneKey, StringComparison.OrdinalIgnoreCase);
            double scaleBase = ChartMath.ScaleBase(state);
            var scaleMode = ChartMath.EffectiveScaleMode(state.ScaleMode, scaleBase, state.IsLogScale);
            if (yMax > yMin && !double.IsNaN(yMin) && !double.IsNaN(yMax))
            {
                string scale = priceLike
                    ? state.IsLogScale ? ", logarithmic scale"
                    : scaleMode == PriceScaleMode.Percent ? ", percent scale"
                    : scaleMode == PriceScaleMode.IndexedTo100 ? ", indexed to 100"
                    : ""
                    : "";
                double gridStep;
                string stepClause;
                if (priceLike && scaleMode != PriceScaleMode.Regular)
                {
                    var (slo, shi) = ChartMath.ScaleBounds(scaleMode, yMin, yMax, scaleBase);
                    gridStep = GridStep(shi - slo);
                    stepClause = scaleMode == PriceScaleMode.Percent
                        ? $"about {gridStep.ToString("0.##", CultureInfo.InvariantCulture)} percent between gridlines"
                        : $"about {gridStep.ToString("0.##", CultureInfo.InvariantCulture)} index points between gridlines";
                }
                else
                {
                    gridStep = GridStep(yMax - yMin);
                    stepClause = $"about {Axis(gridStep, gridStep)} between gridlines";
                }
                parts.Add($"Y axis, {measure}: {Axis(yMin, gridStep)} to {Axis(yMax, gridStep)}, " +
                          $"{stepClause}{scale}.");
            }
            else
            {
                parts.Add($"Y axis, {measure}: no range yet.");
            }

            if (priceLike && ScaleSentence(state, bars[start].Date, timeframe, scaleMode, scaleBase) is { } scaleSentence)
                parts.Add(scaleSentence);

            // ── X axis: shared by every pane, which is worth saying once ──
            if (state.ChartStyle.IsBrickStyle()
                && BrickChartSpeech.TryGetBrick(state, start, out _, out var paneFirst)
                && BrickChartSpeech.TryGetBrick(state, end, out _, out var paneLast))
            {
                parts.Add($"X axis, time: {BrickChartSpeech.Window(state, paneFirst)} to "
                    + $"{BrickChartSpeech.Window(state, paneLast)}, {length} {Charts.BuiltCharts.Noun(state.ChartStyle, length)}.");
            }
            else
            {
                string step = string.IsNullOrWhiteSpace(timeframe)
                    ? "one bar"
                    : SpokenTimeframe(timeframe!);
                parts.Add($"X axis, time: {BarDate(bars[start].Date, state, timeframe)} to " +
                          $"{BarDate(bars[end].Date, state, timeframe)}, {length} bars at {step} each.");
            }

            // ── What is in it ─────────────────────────────────────────────
            parts.Add($"{Count(pane.Series.Count, "series")}: " +
                      string.Join(", ", pane.Series.Select(x => x.Name)) + ".");

            return string.Join(" ", parts);
        }

        /// <summary>
        /// The pane's own Y range as the renderer computed it, falling back to the viewport range
        /// for Main before the first render has produced a pane range.
        /// </summary>
        private static (double Min, double Max) PaneRange(WorkspaceState state, PaneInfo pane)
        {
            if (state.PaneRanges != null && state.PaneRanges.TryGetValue(pane.Key, out var r))
                return r;
            return pane.Key.Equals(ChartPaneModel.MainPaneKey, StringComparison.OrdinalIgnoreCase)
                ? state.ViewportRange
                : (double.NaN, double.NaN);
        }

        /// <summary>
        /// What the pane's Y axis is measuring. Main is price; an indicator pane is named by what
        /// it holds, because "Y axis, value" says nothing a listener did not already know.
        /// </summary>
        private static string MeasureOf(PaneInfo pane)
        {
            if (pane.Key.Equals(ChartPaneModel.MainPaneKey, StringComparison.OrdinalIgnoreCase))
                return "price";
            if (pane.Key.Equals("Volume", StringComparison.OrdinalIgnoreCase))
                return "volume";
            return pane.Series.Count == 1 ? pane.Series[0].Name : pane.DisplayName;
        }

        /// <summary>
        /// Whether the feed is live, and how long since anything arrived.
        /// </summary>
        internal static string DescribeFeedFreshness(WorkspaceState state)
        {
            if (state.LastTickUtc is not { } last)
            {
                // Never having ticked is not the same as having gone quiet — a historical-only
                // provider is working exactly as intended, and calling that "stale" would cry
                // wolf on every analytics chart.
                return state.DataStatus == DataStatus.Stale
                    ? "Feed reported quiet; no live data has arrived."
                    : "No live data yet.";
            }

            var since = DateTime.UtcNow - last;
            string ago = since.TotalMinutes < 1
                ? $"{Math.Max(0, (int)since.TotalSeconds)} seconds"
                : $"{(int)since.TotalMinutes} minutes";

            return state.DataStatus == DataStatus.Stale
                ? $"Feed is QUIET: last update {ago} ago."
                : $"Feed live, last update {ago} ago.";
        }

        /// <summary>"1 series" / "3 series", "1 pane" / "2 panes" — pluralised without the
        /// "(s)" construction, which reads badly aloud.</summary>
        private static string Count(int n, string noun)
        {
            if (noun.EndsWith('s')) return $"{n} {noun}";          // "series" is already plural
            return n == 1 ? $"1 {noun}" : $"{n} {noun}s";
        }

        /// <summary>
        /// The step the Y axis actually labels at.
        ///
        /// <para>
        /// This deliberately duplicates <c>ChartRenderer.RenderYAxis</c>'s nice-number arithmetic
        /// rather than approximating it. The summary is describing the chart in front of the user;
        /// if it says "20,000 between gridlines" and the axis is labelled every 50,000, the
        /// summary is worse than silence — it is confidently wrong about something the user cannot
        /// independently check. The thresholds below (1.5 / 3.5 / 7.5) are the renderer's, and
        /// must move together with it.
        /// </para>
        /// </summary>
        internal static double GridStep(double range)
        {
            if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range)) return 0;

            const int TargetLabelCount = 5;   // a full-size pane; small panes use 3
            double rough = range / TargetLabelCount;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double fraction = rough / magnitude;

            double step = fraction < 1.5 ? 1
                        : fraction < 3.5 ? 2
                        : fraction < 7.5 ? 5
                        : 10;
            return step * magnitude;
        }

        /// <summary>
        /// The orientation sentence for a Percent or Indexed chart: what the zero (or 100) is,
        /// and which bar it came from — the date/time in the same form the bars are described in.
        /// Null in Regular, or when the base cannot carry the mode.
        /// </summary>
        private static string? ScaleSentence(WorkspaceState state, DateTime firstVisible, string? timeframe,
            PriceScaleMode mode, double scaleBase)
        {
            if (mode == PriceScaleMode.Regular || !ChartMath.BaseUsable(scaleBase)) return null;
            // The describer's own bar-zone rule (SpeechTimeFormatter.FormatBar): a daily or
            // coarser bar is named by its date, an intraday one by date and clock, exactly as
            // the X-axis sentence names the bars.
            int barSeconds = TimeframeUtility.ToSeconds(timeframe ?? "");
            if (barSeconds <= 0) barSeconds = PlaybackNarration.BarSeconds(state);
            string when = SpeechTimeFormatter.FormatBar(firstVisible, barSeconds,
                barSeconds < 86400 ? "d MMMM HH:mm" : SpeechTimeFormatter.LongDateFormat);
            string what = mode == PriceScaleMode.Percent ? "Percent scale" : "Indexed to 100";
            return $"{what} from the first close on screen, {SpeechPriceFormatter.FormatPrice(scaleBase)} on {when}.";
        }

        /// <summary>A bar's date by the bar rule (SpeechTimeFormatter.ToBarDisplay): the timeframe
        /// passed in when it parses, else the state's own.</summary>
        private static string BarDate(DateTime stamp, WorkspaceState state, string? timeframe)
        {
            int barSeconds = TimeframeUtility.ToSeconds(timeframe ?? "");
            if (barSeconds <= 0) barSeconds = PlaybackNarration.BarSeconds(state);
            return SpeechTimeFormatter.FormatBar(stamp, barSeconds, SpeechTimeFormatter.LongDateFormat);
        }

        /// <summary>"1d" → "1 day"; "4h" → "4 hours". Bare codes read as spelling aloud.</summary>
        internal static string SpokenTimeframe(string timeframe)
        {
            if (string.IsNullOrWhiteSpace(timeframe)) return timeframe;

            string t = timeframe.Trim();
            int i = 0;
            while (i < t.Length && char.IsDigit(t[i])) i++;
            if (i == 0 || i == t.Length) return t;

            if (!int.TryParse(t[..i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return t;

            // Capital M is month and lower-case m is minute, so it is matched BEFORE the case is
            // folded. The "M" arm used to sit inside the lower-cased switch, where it could never
            // match, and a monthly chart was described as "1 minute per bar".
            string suffix = t[i..];
            string unit = suffix == "M" ? "month" : suffix.ToLowerInvariant() switch
            {
                "m"  => "minute",
                "h"  => "hour",
                "d"  => "day",
                "w" or "wk" => "week",
                "mo" => "month",
                "y"  => "year",
                _    => null!,
            };
            if (unit == null) return t;

            return n == 1 ? $"1 {unit}" : $"{n} {unit}s";
        }

        /// <summary>
        /// An axis value, with its precision taken from the GRIDLINE STEP rather than from the
        /// value itself.
        ///
        /// <para>
        /// <see cref="Money"/> chooses by magnitude, which is right for a price and wrong for an
        /// axis: an oscillator pane running 0 to 100 has a floor of exactly zero, and "0.000000
        /// to 100.00" is six decimal places of nothing on the one number that needed none. The
        /// step is what the axis is actually labelled at, so it is what decides how many digits
        /// carry information.
        /// </para>
        /// </summary>
        private static string Axis(double v, double step)
        {
            double a = Math.Abs(step);
            string format = a >= 1000 ? "N0" : a >= 1 ? "N2" : a >= 0.01 ? "N4" : "N6";
            return v.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>Price with thousands separators and no trailing noise.</summary>
        private static string Money(double v)
        {
            double abs = Math.Abs(v);
            string format = abs >= 1000 ? "N0" : abs >= 1 ? "N2" : "N6";
            return v.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
