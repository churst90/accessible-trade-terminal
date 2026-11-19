using System.Globalization;
using System.Text.RegularExpressions;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface ISpeechFormatter
    {
        string FormatPointFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, Ohlcv pt, string prefixMessage);
        string FormatProfileFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, int binIndex, string prefixMessage);
        /// <param name="dataIndex">The bar the liquidity being described actually came from.</param>
        /// <param name="cursorDataIndex">
        /// Where the user is standing. Differs from <paramref name="dataIndex"/> when the caller
        /// had to fall back to the nearest bar carrying a book snapshot; pass -1 when they are
        /// the same by construction.
        /// </param>
        string FormatHeatmapFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, int dataIndex, int binIndex, string prefixMessage, int cursorDataIndex = -1);
        /// <param name="barSeconds">Seconds between bars, so an intraday range can carry the
        /// time. 0 infers it from the range, which is all a caller without the state has.</param>
        string FormatViewportDescription(int count, DateTime start, DateTime end, int barSeconds = 0);
        void RegisterTemplate(string indicatorCode, string componentName, string template);
    }

    public class SpeechFormatter : ISpeechFormatter
    {
        private readonly IReadOnlyList<IComponentSpeechStrategy> _strategies;
        private readonly IComponentSpeechStrategy _fallback;
        private readonly ILogger<SpeechFormatter> _logger;
        // Optional: resolves indicator providers for contextual component speech
        // (null in minimal tests → the provider strategy simply never matches).
        private readonly Indicators.IIndicatorEngine? _indicatorEngine;

        /// <summary>
        /// The ONE candle classifier — the same one the bar-close and live-bar announcements use.
        /// Injected so the configured thresholds reach the arrow keys, defaulted so the many
        /// existing zero- and one-argument constructions keep working.
        /// </summary>
        private readonly ISdkCandlePatternAnalyzer _candles;

        // The live theme, for the words that name what the renderer paints (volume's candle
        // colours, a profile row's two halves). Optional so minimal tests still construct; the
        // default matches a fresh install's Classic theme, the same default ChartRenderer uses.
        private readonly IThemeService? _themes;

        // The bar each line was last read on, and what was in focus then, so a line's colour is
        // compared with the bar the user came from (MarkerLook.LineSentence), whichever way they
        // moved. Per circuit in the WebHost (scoped), per app on the desktop.
        private readonly Dictionary<(string Series, string Comp), int> _lineReadAt = new();
        private string? _lineFocus;

        // The last shade said for each heatmap, and the last profile row read, compared with the
        // cell/row the user came from in either direction.
        private readonly Dictionary<string, string> _heatmapShade = new();
        private readonly Dictionary<string, int> _profileReadAt = new();

        private ChartTheme Theme => _themes?.Current ?? BaseThemeResolver.Resolve(ThemeType.Classic);

        /// <summary>The bar to compare a line's colour with: the one it was last read on, or -1
        /// when the user has just arrived at it (moved onto it, or onto its series).</summary>
        private int LineFrom(ChartSeries series, ComponentConfig comp, int dataIndex, bool arriving)
        {
            var key = (series.Id, comp.Name);
            int from = !arriving && _lineReadAt.TryGetValue(key, out int at) ? at : -1;
            _lineReadAt[key] = dataIndex;
            return from;
        }

        private static Ohlcv? BarAt(IReadOnlyList<Ohlcv>? bars, int index)
            => bars != null && index >= 0 && index < bars.Count ? bars[index] : null;

        /// <summary>True when the focus (a series summary, or one component) differs from the
        /// last one read.</summary>
        private bool ArrivedAt(string focus)
        {
            bool arrived = focus != _lineFocus;
            _lineFocus = focus;
            return arrived;
        }

        public SpeechFormatter() : this(NullLogger<SpeechFormatter>.Instance) { }

        public SpeechFormatter(ILogger<SpeechFormatter> logger,
                               Indicators.IIndicatorEngine? indicatorEngine = null,
                               ISdkCandlePatternAnalyzer? candleAnalyzer = null,
                               IThemeService? themeService = null)
        {
            _logger = logger;
            _indicatorEngine = indicatorEngine;
            _candles = candleAnalyzer ?? new SdkCandlePatternAnalyzer();
            _themes = themeService;
            // ── THE utterance precedence list (debt item 4) ────────────────────
            // Component-context speech resolves top-down; first non-null wins.
            // This array + the fallback IS the whole precedence — there is no
            // other speech source (NavigationFeedbackManager routes profile and
            // heatmap shapes separately, and Series-context summaries are the
            // three branches at the top of FormatPointFeedback).
            _strategies = new IComponentSpeechStrategy[]
            {
                new TextLabelStrategy(),        // 1. a pinned text label reads its own wording
                new ProviderSpeechStrategy(),   // 2. indicator's own contextual narrative
                new HiddenComponentStrategy(),  // 3. "hidden" state announcement
                new DrawingComponentStrategy(), // 4. a drawing: value, where on it, price against it
                new CloudComponentStrategy(),   // 5. Ichimoku-style cloud narration
                new PhaseNameStrategy(),        // 6. sentiment-phase names
                new MarkerSignalStrategy(),     // 7. signal-marker templates
                new PriceLineStrategy(),        // 8. the price series as one close line (Line, Area)
                new BrickStrategy(),            // 8. the price series as bricks (Renko)
                new CandleBodyStrategy(),       // 8. body = open→close span
                new VolumeBarStrategy(),        // 9. signed exact volume
            };
            _fallback = new StandardTemplateStrategy(); // 10. {name}.{type}.{value} templates
        }

        public void RegisterTemplate(string indicatorCode, string componentName, string template)
        {
            // No-op: kept for interface compatibility. Templates are now declared in
            // provider metadata via IndicatorComponentMetadata.SpeechTemplate.
        }

        public string FormatViewportDescription(int count, DateTime start, DateTime end, int barSeconds = 0)
        {
            if (barSeconds <= 0) barSeconds = SpeechTimeFormatter.SpacingOf(start, end, count);
            return $"Viewing {count} bars from {SpeechTimeFormatter.FormatBarRange(start, end, barSeconds)}";
        }

        public string FormatPointFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, Ohlcv pt, string prefixMessage)
        {
            string msg = string.Empty;
            bool summary = state.LastInteractionContext == InteractionContext.Series;

            string seriesId = series.Id.ToLowerInvariant();

            // What is in focus for this reading: a series summary, or one of its components. A
            // change of it, even by way of the candles and back, is arriving at a line.
            bool arrived = ArrivedAt(series.Id + "\u0001" + (summary || series.Components.Count == 0
                ? "" : series.Components[series.ClampComponent(state.FocusedComponentIndex)].Name));

            // ── WHICH BAR THIS SERIES DESCRIBES ─────────────────────────────────────────
            //
            // `pt` arrives as the bar AS DRAWN — Heikin-Ashi when that mode is on. That is the
            // right answer for the CANDLES, whose whole point is to describe the candle on
            // screen. It is the wrong answer for the close line, and that split is what this
            // block exists to make explicit.
            //
            // The close line is raw whatever the candle style is. Three things quote it and
            // they have to agree: the browser-title price (state.Data[^1].Close), the line the
            // renderer draws (the "close" mapped component array, always raw OHLCV), and this
            // sentence. With HA on they did not: the title said the raw close, this branch said
            // the HA close — an average of four prices that never traded — and arrowing along
            // the same line one keypress later said the raw close again, because the component
            // path reads the mapped array. One series, two numbers, neither of them announced.
            //
            // Reported from live use as three disagreeing prices on one Bitstamp daily chart.
            // The candles are the only readout expected to differ, because a Heikin-Ashi candle
            // IS a different candle.
            Ohlcv rawPt = (state.Data != null && state.Data.Count > 0)
                ? state.Data[Math.Clamp(state.CurrentDataIndex, 0, state.Data.Count - 1)]
                : pt;
            bool readsRawBar = seriesId == CoreSeriesIds.Price;

            // Percent and Indexed relabel the candles readout: it STARTS with the scale reading
            // of the close as drawn, then says everything it said before. The base is the first
            // visible close as drawn (ChartMath's one helper, shared with the renderer and the
            // describer), so panning moves the zero exactly as it does on TradingView. Regular
            // adds nothing, byte for byte.
            double scaleBase = ChartMath.ScaleBase(state);
            var scaleMode = ChartMath.EffectiveScaleMode(state.ScaleMode, scaleBase, state.IsLogScale);

            // A brick is not a candle: its edges are the whole drawing. The readout names the
            // brick's direction and two edges, then its REAL window — no shape, pattern or wick.
            Ohlcv brick = default;
            BrickSpan brickSpan = default;
            // Range bars are real candles (a body and two wicks), so only Renko and line break
            // take this path; a range bar reads as a candle, with its real window as the time.
            bool brickCandle = seriesId == "candles" && state.ChartStyle.IsBlockStyle()
                && BrickChartSpeech.TryGetBrick(state, state.CurrentDataIndex, out brick, out brickSpan);

            // On Line, Area and Renko the price series has ONE part (PriceSeriesShape): the close
            // line or the brick. The summary and that part read the same sentence, through the
            // template every part follows — its name, its type, then its value (Cody, 2026-11-19:
            // "as with any other line it needs to follow the speech template, component name,
            // component type"). No body, no wicks, no shape and no pattern: none is on screen.
            // The bar-detail key still reads the full OHLC bar, a question about the market.
            var onePart = seriesId == CoreSeriesIds.Candles && series.Components.Count == 1
                && series.Components[0].Name is Charts.PriceSeriesShape.LinePart or Charts.PriceSeriesShape.BrickPart
                    or Charts.PriceSeriesShape.BlockPart or Charts.PriceSeriesShape.KagiPart
                ? series.Components[0]
                : null;

            bool lineChart = seriesId == CoreSeriesIds.Candles && state.ChartStyle is PriceChartStyle.Line or PriceChartStyle.Area;
            if (brickCandle || lineChart)
            {
                if (onePart != null)
                {
                    // The point as drawn: the brick on Renko, the raw close on Line and Area.
                    msg = FormatTemplateValue(series, onePart, brickCandle ? brick : rawPt, state.CurrentDataIndex,
                        state.ReadColumnHeaders, state.SpeechOrder, isYMove: isYMove,
                        viewportStart: state.ViewportStartIndex, viewportLength: state.ViewportLength, bars: state.Data,
                        describeLook: state.DescribeSignalLooks,
                        lineFrom: LineFrom(series, onePart, state.CurrentDataIndex, arrived || isYMove),
                        scaleMode: scaleMode, scaleBase: scaleBase);
                    if (brickCandle) msg = msg.TrimEnd().TrimEnd('.');   // the window follows
                }
                else if (brickCandle)
                {
                    // Bricks drawn under candle parts (a shape not yet applied): the brick, plainly.
                    msg = ScalePrefix(scaleMode, brick.Close, scaleBase)
                        + BrickChartSpeech.BrickSummary(brick, brickSpan, state.ChartStyle, state.Data, state.CurrentDataIndex);
                }
                else
                {
                    // A line drawn over candle parts (a shape not yet applied): the point, plainly.
                    msg = ScalePrefix(scaleMode, rawPt.Close, scaleBase)
                        + $"Close {SpeechPriceFormatter.FormatPrice(rawPt.Close)}. "
                        + MoveFromPreviousClose(state.Data, state.CurrentDataIndex, rawPt.Close);
                }
            }
            else if (summary && seriesId == "candles")
            {
                // ── THE ARROW KEYS NAME MULTI-BAR PATTERNS NOW ──────────────────────────────
                //
                // This used to call a private ClassifyCandleType that saw ONE bar and knew five
                // shapes, so scanning history could never surface an engulfing, a harami, a
                // morning star or three white soldiers — the twelve patterns the terminal already
                // detects were audible only if you happened to be listening when the bar closed.
                // Reading a chart by ear is mostly reading the PAST, so the route that reads the
                // past was the one route that could not say them.
                //
                // It is the same analyser, over the same trailing window, that the live
                // announcement uses (CandlePatternSpeech). `pt` is passed as the current bar
                // because it is already the bar AS DRAWN — Heikin-Ashi when that mode is on — and
                // the window behind it is drawn the same way, so a pattern is judged on the
                // candles actually on screen rather than the raw bars underneath them.
                var analysis = CandlePatternSpeech.AnalyzeAt(
                    _candles, state.Data, state.CurrentDataIndex, state.IsHeikinAshi, current: pt);

                // Gated the same way the bar-close clause is. The DIRECTION is not: "Bullish" is
                // a fact about the bar rather than a pattern claim, it has always led this
                // sentence, and dropping it would leave the reading starting on a price.
                string shape = state.DescribeCandlePatterns
                    ? CandlePatternSpeech.WithLean(analysis)
                    : CandlePatternSpeech.DirectionOnly(pt);

                // WHICH CANDLES. Scanning across a three-bar pattern, only the last bar could name
                // it and the two before it said nothing, so the name arrived with no way to find
                // what it referred to — "bar 1 of 3, three white soldiers" is what makes it
                // findable by ear. Under the same switch as the name itself: it is the same claim.
                if (state.DescribeCandlePatterns)
                {
                    string member = CandlePatternSpeech.MembershipClause(analysis,
                        CandlePatternSpeech.MembershipsAt(
                            _candles, state.Data, state.CurrentDataIndex, state.IsHeikinAshi));
                    if (member.Length > 0) shape += ", " + member;
                }

                double range = pt.High - pt.Low;
                double body = Math.Abs(pt.Close - pt.Open);
                double bodyPct = range > 0 ? (body / range) * 100.0 : 0;
                double upperWick = pt.High - Math.Max(pt.Open, pt.Close);
                double lowerWick = Math.Min(pt.Open, pt.Close) - pt.Low;
                double upperPct = range > 0 ? (upperWick / range) * 100.0 : 0;
                double lowerPct = range > 0 ? (lowerWick / range) * 100.0 : 0;

                msg = $"{shape}. Close {SpeechPriceFormatter.FormatPrice(pt.Close)}. Open {SpeechPriceFormatter.FormatPrice(pt.Open)}. " +
                      $"High {SpeechPriceFormatter.FormatPrice(pt.High)}. Low {SpeechPriceFormatter.FormatPrice(pt.Low)}. Volume {FormatVolume(pt.Volume)}. " +
                      $"Body {bodyPct.ToString("F0", CultureInfo.InvariantCulture)}%, Upper wick {upperPct.ToString("F0", CultureInfo.InvariantCulture)}%, Lower wick {lowerPct.ToString("F0", CultureInfo.InvariantCulture)}%.";

                // `pt` is the bar AS DRAWN, so on a Heikin-Ashi chart this is the HA close — the
                // candle on screen and the same close the axis' base came from.
                msg = ScalePrefix(scaleMode, pt.Close, scaleBase) + msg;
            }
            else if (summary && seriesId == "price")
            {
                var priceComp = series.Components.FirstOrDefault(c => c.IsVisible && !c.IsMuted);
                string lineType = priceComp != null ? FriendlyTypeName(priceComp.DisplayType) : "line";
                // rawPt, not pt — see the note above. This branch also serves analytics providers
                // (SingleValueLine), where the "close" is the metric's own value and a candle
                // transform over it would be meaningless as well as wrong.
                msg = $"{series.Name}. {lineType}. {SpeechPriceFormatter.FormatPrice(rawPt.Close)}.";
                // Percent and Indexed lead with the scale reading here too: the close line is
                // drawn on that axis, so its summary says where on it the point sits.
                if (priceComp == null || string.Equals(priceComp.DataMapping, "close", StringComparison.OrdinalIgnoreCase))
                    msg = ScalePrefix(scaleMode, rawPt.Close, scaleBase) + msg;
            }
            else if (summary)
            {
                var values = series.Components
                    .Where(c => c.IsVisible && !c.IsMuted)
                    .Select(c => FormatTemplateValue(series, c, pt, state.CurrentDataIndex, state.ReadColumnHeaders, state.SpeechOrder,
                        viewportStart: state.ViewportStartIndex, viewportLength: state.ViewportLength, bars: state.Data,
                        describeLook: state.DescribeSignalLooks,
                        lineFrom: LineFrom(series, c, state.CurrentDataIndex, arrived || isYMove),
                        scaleMode: scaleMode, scaleBase: scaleBase))
                    .ToList();

                // A value that already closes its sentence (a signal followed by what it looks
                // like) is not given a second full stop by the join. The last keeps its own: what
                // follows the summary ("Main pane.") is joined on with a space.
                msg = string.Join(". ", values.Select((v, i) => i < values.Count - 1 ? v.TrimEnd().TrimEnd('.') : v.TrimEnd()));
            }
            else
            {
                // A series with no components has no VALUE to read — but the caller's prefix
                // is a separate thing that still needs saying: a series-switch announcement,
                // a pane label, "Home"/"End". Returning "" here discarded the prefix along
                // with the value, so pressing Home on a component-less series said nothing at
                // all rather than "Home." The concatenation at the end of this method is the
                // only thing that ever emits the prefix, so the early return had to stop
                // skipping it.
                if (series.Components.Count == 0)
                    return string.IsNullOrEmpty(prefixMessage) ? "" : prefixMessage.TrimEnd();
                var compIndex = series.ClampComponent(state.FocusedComponentIndex);
                var comp = series.Components[compIndex];
                // Provider contextual speech applies in Component context only (the
                // old NavigationFeedbackManager "path 1" gate, now strategy #2).
                var provider = state.LastInteractionContext == InteractionContext.Component
                               && !string.IsNullOrEmpty(series.IndicatorCode)
                    ? _indicatorEngine?.GetProvider(series.IndicatorCode)
                    : null;
                double? liveClose = state.Data != null && state.Data.Count > 0
                    ? (double?)state.Data[^1].Close
                    : null;
                msg = FormatTemplateValue(series, comp, readsRawBar ? rawPt : pt, state.CurrentDataIndex, state.ReadColumnHeaders, state.SpeechOrder,
                    isYMove: isYMove, liveClose: liveClose, provider: provider,
                    viewportStart: state.ViewportStartIndex, viewportLength: state.ViewportLength, bars: state.Data,
                    describeLook: state.DescribeSignalLooks,
                    lineFrom: LineFrom(series, comp, state.CurrentDataIndex, arrived || isYMove),
                    scaleMode: scaleMode, scaleBase: scaleBase);
            }

            // STRICT SPEECH POLICY: Apply settings to timestamps
            bool shouldSpeakTimestamp = state.SpeakTimestamps;
            if (shouldSpeakTimestamp)
            {
                if (state.TimestampReadLocation == "Along X Axis" && !isXMove) shouldSpeakTimestamp = false;
                else if (state.TimestampReadLocation == "Along Y Axis" && !isYMove) shouldSpeakTimestamp = false;
                else if (state.TimestampReadLocation == "None") shouldSpeakTimestamp = false;
            }

            if (brickCandle)
            {
                string when = shouldSpeakTimestamp
                    ? " " + BrickChartSpeech.Window(state, brickSpan) + "."
                    : "";
                return prefixMessage + msg + "." + when;
            }

            string timestamp = shouldSpeakTimestamp
                ? (BrickChartSpeech.TryGetBrick(state, state.CurrentDataIndex, out _, out var anyBrick)
                    ? BrickChartSpeech.Window(state, anyBrick)
                    : NavigationTimestamp(state, pt.Date)) + ". "
                : "";

            return timestamp + prefixMessage + msg;
        }

        /// <summary>
        /// The day the last spoken navigation timestamp fell on, so the next one knows whether it
        /// has crossed into a new one. Null until something has been read.
        ///
        /// <para>
        /// State in a formatter, deliberately, and it is state about what was SAID rather than
        /// about the chart — which is why it does not live in the store. Comparing against the
        /// previously READ bar rather than against the bar before this one in the data is what
        /// makes it work in both directions: arrowing LEFT across midnight lands on the last bar
        /// of the previous day, which is not the first bar of anything and would never announce
        /// itself under a data-only rule.
        /// </para>
        /// </summary>
        private DateTime? _lastSpokenBarDay;

        /// <summary>
        /// The timestamp an arrow-key reading carries.
        ///
        /// <para>
        /// Cody, 2026-09-05: <i>"if I switch to an hour chart, when I use the arrows to move by
        /// bar, I want to hear the date… when I actually cross into a new day, otherwise, just
        /// the timestamp itself."</i> Every bar used to carry the full stamp — "September 05,
        /// 2026, 14:00" — which is the same eleven syllables of date in front of every one of
        /// twenty-four consecutive readings, and on a daily chart it was the reverse absurdity:
        /// "September 05, 2026, 00:00", a time that is the same on every bar a daily chart has.
        /// </para>
        ///
        /// <list type="bullet">
        ///   <item><b>Daily bars or coarser</b> — the date, alone. There is no time to tell.</item>
        ///   <item><b>Intraday</b> — the time, alone, until the reading crosses into a new day;
        ///         that bar leads with the date. <c>Speak the date on every bar</c> in Settings
        ///         (F12) → Speech makes every bar carry it, for a user who would rather have it
        ///         than infer it.</item>
        /// </list>
        ///
        /// <para>
        /// The two "Speak values as" orders that name a timestamp explicitly — time only, date
        /// only — are honoured exactly as before. A user who has asked for one of them has
        /// already answered this question.
        /// </para>
        /// </summary>
        private string NavigationTimestamp(WorkspaceState state, DateTime stamp)
        {
            // Daily bars and coarser are named by their own date, not by the local date of
            // their UTC-midnight stamp — see SpeechTimeFormatter.ToBarDisplay.
            int barSeconds = PlaybackNarration.BarSeconds(state);
            // "Time only" on a daily chart used to read the clock time of the bar's UTC-midnight
            // stamp in the local zone — every daily bar "18:00" in Chicago (19:00 in summer), a
            // time no daily bar has. A daily bar has no time to tell, so it is named by its date.
            if (state.SpeechOrder.Contains("TimeOnly"))
                return barSeconds >= 86400
                    ? SpeechTimeFormatter.FormatBar(stamp, barSeconds, SpeechTimeFormatter.DateFormat)
                    : SpeechTimeFormatter.FormatTime(stamp);
            if (state.SpeechOrder.Contains("DateOnly"))
                return SpeechTimeFormatter.FormatBar(stamp, barSeconds, SpeechTimeFormatter.DateFormat);

            var day = SpeechTimeFormatter.ToBarDisplay(stamp, barSeconds).Date;
            bool dayChanged = _lastSpokenBarDay != day;
            _lastSpokenBarDay = day;

            if (barSeconds >= 86400)
                return SpeechTimeFormatter.FormatBar(stamp, barSeconds, SpeechTimeFormatter.LongDateFormat);

            return dayChanged || state.SpeakDateOnEveryBar
                ? $"{SpeechTimeFormatter.Format(stamp, SpeechTimeFormatter.DateFormat)}, {SpeechTimeFormatter.FormatTime(stamp)}"
                : SpeechTimeFormatter.FormatTime(stamp);
        }

        public string FormatProfileFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, int binIndex, string prefixMessage)
        {
            if (series.ProfileBins == null || series.ProfileBins.Count == 0) return "";

            // No bin focused yet — which is every time a profile is ADDED or switched to, since
            // only Up and Down select a bin. This used to return "", and the return value carried
            // the series-switch prefix ("Volume Profile. 24 bins. ") with it, so the name was not
            // heard until the first Up or Down. Cody, 2026-09-11: "When I add a profile to the
            // chart, the series name isn't read until I start moving around the profile." The
            // name, then the profile in one breath: where its point of control and value area
            // are, and how to move through it.
            if (binIndex < 0 || binIndex >= series.ProfileBins.Count)
                return prefixMessage + ProfileLevels.Overview(series.ProfileBins);
            var allBins = series.ProfileBins;
            var bin     = allBins[binIndex];

            string dataMsg;
            if (double.IsNaN(bin.TotalVolume) || bin.TotalVolume == 0)
            {
                dataMsg = $"Price {SpeechPriceFormatter.FormatPrice(bin.PriceLow)} to {SpeechPriceFormatter.FormatPrice(bin.PriceHigh)}, no data.";
            }
            else
            {
                var nodeType = ProfileBinClassifier.Classify(bin, allBins);
                string nodeLabel = ProfileBinClassifier.GetLabel(nodeType);

                // Percentage of total session volume for context.
                double totalVol = allBins.Where(b => !double.IsNaN(b.TotalVolume)).Sum(b => b.TotalVolume);
                double pct = totalVol > 0 ? bin.TotalVolume / totalVol * 100.0 : 0;

                if (bin.TpoLetters.Any())
                {
                    // TPO mode: report time periods (letters) rather than volume.
                    string letters = string.Join(" ", bin.TpoLetters);
                    string labelPart = string.IsNullOrEmpty(nodeLabel) ? "" : $", {nodeLabel}";
                    dataMsg = $"Price {SpeechPriceFormatter.FormatPrice(bin.PriceLow)} to {SpeechPriceFormatter.FormatPrice(bin.PriceHigh)}, " +
                              $"{bin.TpoPeriodCount.ToString("F0", CultureInfo.InvariantCulture)} {(bin.TpoPeriodCount == 1 ? "period" : "periods")}, " +
                              $"letters {letters}{labelPart}.";
                }
                else
                {
                    string labelPart = string.IsNullOrEmpty(nodeLabel) ? "" : $"{nodeLabel}, ";
                    dataMsg = $"Price {SpeechPriceFormatter.FormatPrice(bin.PriceLow)} to {SpeechPriceFormatter.FormatPrice(bin.PriceHigh)}, " +
                              $"{labelPart}" +
                              $"{FormatVolume(bin.TotalVolume)} contracts, " +
                              $"{pct.ToString("F1", CultureInfo.InvariantCulture)} percent.";
                }
            }

            // Buying and selling: data, not looks, so always said when a row has a split. The
            // leader rides inside the row's sentence — "…, 62 percent buying." — and is silent
            // for a TPO or restored row that has no split.
            dataMsg = WithTrailingClause(dataMsg, SplitLeader(bin));

            // The row's drawn colour, under the same opt-in and the same arrival/change rule as a
            // line or a bar. No-split rows are not given a colour word.
            string lookSuffix = "";
            if (state.DescribeSignalLooks)
            {
                bool arrived = ArrivedAt(series.Id + "\u0001profile");
                int from = !arrived && _profileReadAt.TryGetValue(series.Id, out int at) ? at : -1;
                _profileReadAt[series.Id] = binIndex;
                string look = ProfileRowLook(Theme, bin);
                string? previous = from >= 0 && from < allBins.Count ? ProfileRowLook(Theme, allBins[from]) : null;
                if (look.Length > 0 && previous != look)
                    lookSuffix = " " + MarkerLook.Capitalise(look) + ".";
            }

            // Timestamps on profiles only when moving across time (X axis).
            bool shouldSpeakTimestamp = state.SpeakTimestamps && isXMove
                && state.Data != null && state.CurrentDataIndex >= 0 && state.CurrentDataIndex < state.Data.Count;
            string timestamp = shouldSpeakTimestamp
                ? SpeechTimeFormatter.FormatBarClock(state.Data![state.CurrentDataIndex].Date, PlaybackNarration.BarSeconds(state)) + ". "
                : "";

            return timestamp + prefixMessage + dataMsg + lookSuffix;
        }

        /// <summary>", 62 percent buying", ", 58 percent selling" or ", even", from the split a
        /// volume-profile row carries; empty when it has none (TPO, zero volume, an old bin).</summary>
        private static string SplitLeader(ProfileBin bin)
        {
            int leader = ProfileSplit.Leader(bin, out double buying);
            if (double.IsNaN(buying)) return "";
            return leader switch
            {
                > 0 => $", {buying.ToString("F0", CultureInfo.InvariantCulture)} percent buying",
                < 0 => $", {(100.0 - buying).ToString("F0", CultureInfo.InvariantCulture)} percent selling",
                _ => ", even",
            };
        }

        /// <summary>The row's drawn colour: the POC's own colour on a POC row, otherwise the two
        /// halves' names, in the order the renderer paints them; empty when the row has no split.</summary>
        private static string ProfileRowLook(ChartTheme theme, ProfileBin bin)
        {
            if (bin.IsPOC) return MarkerLook.ColorName(ProfileRenderLayer.PocColor(theme)) + " row";
            if (bin.BuyVolume + bin.SellVolume <= 0) return "";
            var (buy, sell) = ProfileRenderLayer.SplitColors(theme);
            return $"{MarkerLook.ColorName(buy)} and {MarkerLook.ColorName(sell)} row";
        }

        /// <summary>Puts <paramref name="clause"/> (which starts with a comma or a word) before the
        /// full stop of <paramref name="sentence"/>; leaves the sentence alone when it is empty.</summary>
        private static string WithTrailingClause(string sentence, string clause)
        {
            if (clause.Length == 0) return sentence;
            string body = sentence.EndsWith(".") ? sentence[..^1] : sentence;
            return body + clause + ".";
        }

        public string FormatHeatmapFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, int dataIndex, int binIndex, string prefixMessage, int cursorDataIndex = -1)
        {
            if (series.HeatmapData == null || dataIndex < 0 || dataIndex >= series.HeatmapData.Count)
                return "No data.";

            var bar = series.HeatmapData[dataIndex];
            if (bar == null || !bar.Any())
                return "No data at this bar.";

            // Time label for the bar — always relevant for heatmaps (both axes navigable).
            //
            // It is the CURSOR's time, not the snapshot's. The caller resolves the nearest bar
            // carrying a book (order-book snapshots are sparse; a historical bar usually has
            // none), and before 2026-08-27 this read that resolved bar's stamp — so standing on
            // a bar from Tuesday and hearing the live snapshot's 14:30 was indistinguishable
            // from the book actually being Tuesday's. The user's own position is the one thing
            // they cannot cross-check, so it leads; the borrowed snapshot then says so.
            int cursorIdx = cursorDataIndex >= 0 ? cursorDataIndex : dataIndex;
            string timeLabel = "";
            if (state.Data != null && cursorIdx >= 0 && cursorIdx < state.Data.Count)
                timeLabel = SpeechTimeFormatter.FormatBarClock(state.Data[cursorIdx].Date, PlaybackNarration.BarSeconds(state)) + ", ";

            string borrowedLabel = "";
            if (cursorIdx != dataIndex && state.Data != null && dataIndex >= 0 && dataIndex < state.Data.Count)
                borrowedLabel = $"no book here, showing {SpeechTimeFormatter.FormatBarClock(state.Data[dataIndex].Date, PlaybackNarration.BarSeconds(state))}, ";

            string dataMsg;
            ProfileBin? described;
            if (binIndex < 0 || binIndex >= bar.Count)
            {
                // No bin focused — announce the column's peak.
                var peak = bar.MaxBy(b => b.TotalVolume);
                described = peak;
                if (peak == null || peak.TotalVolume <= 0)
                {
                    dataMsg = "no liquidity.";
                }
                else
                {
                    // Classify against this bar to get a consistent intensity label.
                    double barMax = bar.Max(b => b.TotalVolume);
                    var classifyBins = BuildClassifyBins(bar, barMax);
                    var peakClassify = classifyBins[bar.IndexOf(peak)];
                    var nodeType = ProfileBinClassifier.Classify(peakClassify, classifyBins);
                    string label = ProfileBinClassifier.GetLabel(nodeType);
                    string labelPart = string.IsNullOrEmpty(label) ? "" : $", {label}";
                    dataMsg = $"peak at price {SpeechPriceFormatter.FormatPrice(peak.PriceMid)}{labelPart}, {FormatVolume(peak.TotalVolume)} contracts.";
                }
            }
            else
            {
                var bin     = bar[binIndex];
                described   = bin;
                double barMax = bar.Max(b => b.TotalVolume);
                var classifyBins = BuildClassifyBins(bar, barMax);
                var nodeType = ProfileBinClassifier.Classify(classifyBins[binIndex], classifyBins);
                string nodeLabel = ProfileBinClassifier.GetLabel(nodeType);
                string labelPart = string.IsNullOrEmpty(nodeLabel) ? "" : $", {nodeLabel}";

                // Percentage of this column's volume for relative intensity.
                double colTotal = bar.Sum(b => b.TotalVolume);
                double pct = colTotal > 0 ? bin.TotalVolume / colTotal * 100.0 : 0;

                dataMsg = $"price {SpeechPriceFormatter.FormatPrice(bin.PriceLow)} to {SpeechPriceFormatter.FormatPrice(bin.PriceHigh)}" +
                          $"{labelPart}, " +
                          $"{FormatVolume(bin.TotalVolume)} contracts, " +
                          $"{pct.ToString("F1", CultureInfo.InvariantCulture)} percent.";
            }

            // The cell's shade, from the alpha the renderer paints it with (HeatmapRenderer).
            // Same arrival/change rule as a line: said on arriving, then only where the word
            // differs from the cell the user came from, moving between prices or between bars.
            // An empty cell says nothing.
            bool arrived = ArrivedAt(series.Id + "\u0001heatmap");
            string shade = state.DescribeSignalLooks && described != null
                ? HeatmapRenderer.CellShade(series, described)
                : "";
            bool hadShade = _heatmapShade.TryGetValue(series.Id, out string? previousShade);
            _heatmapShade[series.Id] = shade;
            string shadeSuffix = shade.Length > 0 && (arrived || !hadShade || previousShade != shade)
                ? " " + MarkerLook.Capitalise(shade) + " cell."
                : "";

            // Timestamp first, then prefix, then data — consistent with FormatPointFeedback ordering.
            // The borrowed-snapshot caveat rides immediately before the data it qualifies, and is
            // NOT gated on SpeakTimestamps: turning timestamps off asks for less chatter, not to
            // be told about another bar's liquidity as though it were this one's.
            string timestampPrefix = state.SpeakTimestamps ? timeLabel : "";
            return timestampPrefix + prefixMessage + borrowedLabel + dataMsg + shadeSuffix;
        }

        // ── Dispatcher ───────────────────────────────────────────────────────────

        /// <summary>
        /// The reading a Percent or Indexed chart puts in FRONT of the candles sentence:
        /// <c>"Plus 4.20 percent. "</c>, <c>"Minus 1.30 percent. "</c>, <c>"Zero percent. "</c>
        /// or <c>"Index 104.20. "</c> — capitalised because it starts the readout. Empty in
        /// Regular, or when the base cannot carry the mode (the chart is drawing Regular then,
        /// and saying a percentage would describe an axis that is not on screen).
        /// </summary>
        /// <summary>
        /// The point on a Line or Area chart's line: <c>"Close 95,000.50. Up 1,120.00 from the
        /// previous close, 1.19 percent."</c>, <c>"… Down …"</c>, <c>"… Unchanged from the
        /// previous close."</c> or <c>"… First bar."</c> The close is the RAW one: the reducer
        /// clears Heikin-Ashi for these types and the renderer never draws an HA line.
        /// </summary>
        /// <summary>How the close at <paramref name="idx"/> moved from the one before it:
        /// "Up 4.20 from the previous close, 4.20 percent." The point on a close line.</summary>
        internal static string MoveFromPreviousClose(IReadOnlyList<Ohlcv>? bars, int idx, double close)
        {
            if (bars == null || idx <= 0 || idx >= bars.Count) return "First bar.";

            double previous = bars[idx - 1].Close;
            double change = close - previous;
            if (change == 0) return "Unchanged from the previous close.";

            double percent = previous == 0 ? 0 : Math.Abs((change / previous) * 100.0);
            string direction = change > 0 ? "Up" : "Down";
            return $"{direction} {SpeechPriceFormatter.FormatPrice(Math.Abs(change))} from the previous close, " +
                   $"{percent.ToString("F2", CultureInfo.InvariantCulture)} percent.";
        }

        private static string ScalePrefix(PriceScaleMode mode, double closeAsDrawn, double scaleBase)
        {
            if (mode == PriceScaleMode.Regular || !ChartMath.BaseUsable(scaleBase)) return "";
            string spoken = ChartMath.FormatScaleReadingSpoken(mode,
                ChartMath.ScaleReading(mode, closeAsDrawn, scaleBase));
            return char.ToUpperInvariant(spoken[0]) + spoken[1..] + ". ";
        }

        /// <summary>Closes <paramref name="text"/> with a full stop when a sentence is about to
        /// follow it, so "Buy: buy signal at 64,250" and "Large green dot." read as two sentences.</summary>
        private static string EndSentence(string text, string following)
        {
            if (following.Length == 0) return text;
            string t = text.TrimEnd();
            return t.Length == 0 || ".!?".Contains(t[^1]) ? t : t + ".";
        }

        private string FormatTemplateValue(ChartSeries series, ComponentConfig comp, Ohlcv pt, int dataIndex, bool readHeaders, string speechOrder,
            bool isYMove = false, double? liveClose = null, AccessibleTrader.Sdk.Interfaces.IIndicatorProvider? provider = null,
            int viewportStart = -1, int viewportLength = -1, IReadOnlyList<Ohlcv>? bars = null,
            bool describeLook = false, int lineFrom = -1,
            PriceScaleMode scaleMode = PriceScaleMode.Regular, double scaleBase = double.NaN)
        {
            try
            {
                double val = GetPointValue(series, pt, comp.Name, dataIndex);
                var ctx = new ComponentFormatContext(series, comp, pt, dataIndex, readHeaders, speechOrder, val,
                    IsYMove: isYMove, LiveClose: liveClose, Provider: provider,
                    ViewportStart: viewportStart, ViewportLength: viewportLength, Bars: bars,
                    ScaleMode: scaleMode, ScaleBase: scaleBase);

                // THE HIDDEN/MUTED QUALIFIER, for every strategy, in one place.
                //
                // Reported from real use on 2026-09-04: "if I hide and mute both at once, if I
                // unhide it should say muted but it doesn't." Two independent flags were being
                // reported as a chain of two — ProviderSpeechStrategy tested them with an
                // if/else so hidden always won, HiddenComponentStrategy hard-coded the word
                // "hidden", and the other EIGHT strategies said nothing about either. So a muted
                // candle body announced itself as though it were audible, and a component that
                // was both told the user to press one key when it needed two.
                //
                // Leading, not trailing: whatever interrupts cuts the END of a sentence, and
                // "this one is silent" is the half a user must not lose. Y-move only — a
                // qualifier in front of every bar of a left/right sweep is the repeated prefix
                // this repo has deleted twice already.
                // A HIDDEN component says so on every move, not only Y: it has no value to read,
                // so the state IS the message — that is what HiddenComponentStrategy was for, and
                // dropping the word on an X-scan would leave a bare name repeating.
                bool sayState = isYMove || !comp.IsVisible;
                string statePrefix = sayState
                    ? VisibilityStateSpeech.Prefix(comp.IsVisible, comp.IsMuted)
                    : "";

                // ── "NARRATING" ON A COMPONENT, AND WHY IT TRAILS RATHER THAN LEADS ──────
                //
                // Hidden and muted lead because they explain a SILENCE — there is no value
                // coming, and losing that half to an interruption is what the prefix rule was
                // written for. Narrating explains nothing of the kind: it is an addition, the
                // value is coming as normal, and putting it in front would push every reading
                // late on every Y-move for a fact that does not change between bars. It trails,
                // like the series-level clause NavigationFeedbackManager already appends.
                //
                // Said ONLY when the series has a component selection. With no selection the
                // whole series narrates, the series clause has already said so, and repeating
                // it on all eleven components of a Cipher B is the noise this is meant to
                // replace. With a selection it is the one thing the user cannot otherwise
                // discover: WHICH of them is in it.
                string narrationSuffix =
                    isYMove
                    && SeriesNarrationScope.SeriesNarrates(series)
                    && SeriesNarrationScope.HasComponentSelection(series)
                    && comp.IsAutoNarrated
                        ? " Narrating."
                        : "";

                // What the marker on this bar looks like, after whatever the strategy said about
                // what it MEANS (MarkerLook; opt-in). Here rather than in MarkerSignalStrategy so a
                // signal answered by its provider's own speech is described too. Empty on a bar
                // with no marker, so a left/right sweep adds nothing between signals.
                string look = describeLook ? MarkerLook.Sentence(series, comp, dataIndex) : "";
                // A line's or a bar's colour, on arriving at it and where it differs from the bar
                // the user came from (MarkerLook.LineSentence); bars need the theme and the two
                // bars so the word is the paint the renderer used.
                if (describeLook && look.Length == 0)
                    look = MarkerLook.LineSentence(series, comp, dataIndex, lineFrom,
                        Theme, BarAt(bars, dataIndex), BarAt(bars, lineFrom));
                string lookSuffix = look.Length == 0 ? "" : " " + look;

                foreach (var strategy in _strategies)
                    if (strategy.CanHandle(ctx))
                    {
                        var result = strategy.Format(ctx);
                        if (result != null) return statePrefix + EndSentence(result, look) + lookSuffix + narrationSuffix;
                    }

                string fallback = _fallback.Format(ctx) ?? "";
                return statePrefix + EndSentence(fallback, look) + lookSuffix + narrationSuffix;
            }
            catch (Exception ex)
            {
                // Accessibility path: a malformed template or missing companion series must
                // not crash the speech pipeline -- a blind user listening for price updates
                // relies on a continuous output. "error" is a bounded fallback string so the
                // screen reader still has something to say. LOG the exception so the bug is
                // discoverable post-hoc -- previously the fallback was silent, meaning a
                // broken provider template could emit "<name>: error" on every bar for weeks
                // before anyone noticed.
                _logger.LogWarning(ex,
                    "SpeechFormatter template failed for component '{ComponentName}' on series '{SeriesId}' at dataIndex={DataIndex}.",
                    comp?.Name ?? "(null)", series?.Id ?? "(null)", dataIndex);
                return $"{comp?.DisplayName ?? "component"}: error";
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="FriendlyTypeName"/>, and for a window the picture it carries: "window, with
        /// the cycle arc" — the template's {type}, so an indicator with no speech of its own still
        /// names everything it draws.
        /// </summary>
        internal static string SpokenType(ComponentConfig comp, ChartSeries? series) =>
            comp.DisplayType == ComponentDisplayType.Window && comp.DrawsPathInEffect() && WindowSpans.HasPath(series, comp)
                ? $"window, with the {comp.PathName}"
                : FriendlyTypeName(comp.DisplayType);

        /// <summary>
        /// Maps ComponentDisplayType to a TTS-friendly lowercase string.
        /// Prevents internal enum names like "ZeroArea" being mangled by the speech engine.
        /// </summary>
        internal static string FriendlyTypeName(ComponentDisplayType dt) => dt switch
        {
            ComponentDisplayType.Line       => "line",
            ComponentDisplayType.Area       => "area",
            ComponentDisplayType.Oscillator => "oscillator",
            ComponentDisplayType.ZeroArea   => "oscillator",
            ComponentDisplayType.Histogram  => "histogram",
            ComponentDisplayType.Bar        => "bar",
            ComponentDisplayType.Dot        => "dot",
            ComponentDisplayType.ZeroDot    => "dot",
            ComponentDisplayType.Arrow      => "arrow",
            ComponentDisplayType.StepLine   => "step line",
            ComponentDisplayType.Gradient   => "gradient",
            ComponentDisplayType.Cloud      => "cloud",
            ComponentDisplayType.Heatmap    => "heatmap",
            ComponentDisplayType.Wick       => "wick",
            ComponentDisplayType.Candle      => "candle",
            ComponentDisplayType.Brick       => "brick",
            ComponentDisplayType.TriangleUp   => "triangle up",
            ComponentDisplayType.TriangleDown => "triangle down",
            ComponentDisplayType.Diamond      => "diamond",
            ComponentDisplayType.Square       => "square",
            ComponentDisplayType.CheckBox     => "check mark",
            ComponentDisplayType.RefreshBox   => "refresh mark",
            ComponentDisplayType.TriangleUpRing => "lettered triangle",
            ComponentDisplayType.Cross        => "cross",
            ComponentDisplayType.CandleColor  => "sentiment phase",
            ComponentDisplayType.ValueLabel   => "value label",
            ComponentDisplayType.Window       => "window",
            _                                 => dt.ToString().ToLower()
        };

        /// <summary>
        /// Formats a volume for natural speech (24350 → "24,350").
        ///
        /// <para>
        /// Below 1,000 the significant figures are kept. The old <c>F0</c> here was written for
        /// share and contract counts, which are whole numbers, and it silently rounded every
        /// fractional size to nothing: a spot BTC candle carrying 0.35 BTC of volume spoke
        /// "Volume 0", and a profile bin holding 0.4 contracts spoke "0 contracts, 12.3 percent"
        /// — a bin that plainly has volume, reported as having none. Crypto pairs are the common
        /// case in this app, so the sub-1 band is the one that mattered most and read worst.
        /// </para>
        /// <para>
        /// Whole numbers still read whole — 350 is "350", not "350.00" — because padding every
        /// share count with a fake ".00" is its own kind of noise.
        /// </para>
        /// <para>
        /// The <c>PriceFormatScanTests</c> guard bans a fixed <c>F0</c>/<c>F1</c>/<c>F2</c> next
        /// to a QUOTE-CURRENCY word, which is why an <c>F0</c> next to a *volume* word survived
        /// this long.
        /// </para>
        /// </summary>
        private static string FormatVolume(double vol)
        {
            if (double.IsNaN(vol)) return "unknown";
            double abs = Math.Abs(vol);
            if (abs >= 1_000_000) return $"{(vol / 1_000_000).ToString("F2", CultureInfo.InvariantCulture)}M";
            if (abs >= 1_000)     return vol.ToString("N0", CultureInfo.InvariantCulture);
            if (vol == Math.Floor(vol)) return vol.ToString("N0", CultureInfo.InvariantCulture);
            return QuantityFormatter.Format(vol);
        }

        /// <summary>
        /// Builds a temporary ProfileBin list for classification from heatmap bar data.
        /// Marks the column's highest-volume bin as IsPOC so the classifier can identify HVN/LVN
        /// relative to the column mean.
        /// </summary>
        private static List<ProfileBin> BuildClassifyBins(List<ProfileBin> bar, double barMax)
        {
            return bar.Select(b => new ProfileBin
            {
                PriceLow       = b.PriceLow,
                PriceHigh      = b.PriceHigh,
                TotalVolume    = b.TotalVolume,
                BuyVolume      = b.BuyVolume,
                SellVolume     = b.SellVolume,
                TpoPeriodCount = 0,
                IsPOC          = Math.Abs(b.TotalVolume - barMax) < 1e-9,
                IsValueArea    = false,
            }).ToList();
        }

        internal static double GetPointValue(ChartSeries s, Ohlcv p, string c, int i)
        {
            string sId = s.Id.ToLowerInvariant();

            // ── THE CANDLES READ THE BAR, NOT THE ARRAY ─────────────────────────────────
            //
            // The candle series' components are NOT virtual any more: ViewportReducer syncs a
            // mapped array for each of them (upper_wick→high, body→close, lower_wick→low), and
            // those arrays are always RAW OHLCV. `p` is the bar as drawn. So with Heikin-Ashi
            // on, the array lookup below answered the wick components with the raw high and low
            // while the series summary one keypress earlier described the HA candle — the
            // terminal reporting a lower wick of 19% for a candle drawn without a lower wick at
            // all, which is the original Heikin-Ashi report, still live on this path.
            //
            // It only ever looked fixed because HeikinAshiSpeechTests built its candle series
            // with an EMPTY data buffer, so the array lookup missed and execution fell through
            // to the bar. Production has the arrays. The fixture now carries them too.
            //
            // With HA off the two sources are the same numbers, so this reorder is a no-op
            // there. `PriceComponentFallback` returns NaN for any name that is not a candle
            // part, so a plugin component hosted on this series still reaches its own array.
            if (sId == "candles")
            {
                double drawn = ChartMath.PriceComponentFallback(c, p);
                if (!double.IsNaN(drawn)) return drawn;
            }

            var comp = s.Components.FirstOrDefault(x => x.Name.Trim().Equals(c.Trim(), StringComparison.OrdinalIgnoreCase));
            if (comp != null)
            {
                var data = s.GetComponentData(comp.Name);
                if (data != null && i >= 0 && i < data.Length)
                    return data[i];
            }

            // Fallback for the primary price series, whose components are virtual. Shared with
            // the renderer (ChartMath) rather than reimplemented: this block used to test the
            // PRE-rename names with Contains ("Body"/"Upper"/"Lower"/"Open"), all of which are
            // false against the current machine ids (body/upper_wick/lower_wick/line), so it
            // returned NaN and the wick spoke "no data" whenever the primary lookup missed.
            // Unlike the renderer, an unrecognised name stays NaN here — speech says "no data"
            // rather than reading out a close price under some other component's name.
            if (sId == "price" || sId == "candles")
                return ChartMath.PriceComponentFallback(c, p);
            if (sId == "volume") return p.Volume;
            return double.NaN;
        }
    }

    // ── Strategy registry ────────────────────────────────────────────────────────

    /// <summary>
    /// Per-component speech strategy. Strategies are consulted in registration
    /// order; the first <see cref="CanHandle"/> match owns the component.
    /// Adding a new DisplayType-specific speech path means adding a strategy
    /// class here, not editing <see cref="SpeechFormatter.FormatTemplateValue"/>.
    /// </summary>
    internal interface IComponentSpeechStrategy
    {
        bool CanHandle(ComponentFormatContext ctx);
        /// <summary>Null = decline (consult the next strategy) — e.g. a provider
        /// that has custom speech for some components but not this one.</summary>
        string? Format(ComponentFormatContext ctx);
    }

    internal readonly record struct ComponentFormatContext(
        ChartSeries Series,
        ComponentConfig Comp,
        Ohlcv Pt,
        int DataIndex,
        bool ReadHeaders,
        string SpeechOrder,
        double Value,
        // Provider-speech inputs (null/false outside Component-context navigation).
        bool IsYMove = false,
        double? LiveClose = null,
        AccessibleTrader.Sdk.Interfaces.IIndicatorProvider? Provider = null,
        // Visible window, so sparse marker components can report "N signals in view"
        // instead of "no data" at a bar with no marker. -1 = unknown (whole-array fallback).
        int ViewportStart = -1,
        int ViewportLength = -1,
        // The loaded bars. A drawing's position clause resolves its anchor DATES to bar indices
        // against these; null drops the clause rather than guessing (see DrawingSpeech.Locate).
        IReadOnlyList<Ohlcv>? Bars = null,
        // Percent/Indexed relabel the {value:price} token on a MAIN-pane series: the scale
        // reading first, then the price. Indicator panes are Regular (as they are for log).
        PriceScaleMode ScaleMode = PriceScaleMode.Regular,
        double ScaleBase = double.NaN);

    /// <summary>
    /// Strategy #1: a Text Label drawing reads the wording the user typed.
    ///
    /// <para>
    /// A label's component array holds the CLOSE PRICE of the bar it was pinned to — that is
    /// only how the anchor is stored, and it is the one thing about a label that carries no
    /// information at all. Every other strategy here treats a component's array as a value to
    /// read, so a label spoke a price and the text was audible nowhere on the chart. It went
    /// into the drawing's series NAME, which is announced once on a series switch and then
    /// never again; arrowing along the bars — the way a label is actually found — got the
    /// price. This strategy is first in the list precisely because the value must never reach
    /// the other strategies.
    /// </para>
    /// </summary>
    internal sealed class TextLabelStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) =>
            ctx.Series.Drawing is { Type: DrawingType.TextLabel };

        public string Format(ComponentFormatContext ctx)
        {
            // NaN = this is not the bar the label is pinned to. A label is a single point, so
            // most bars of its series are empty; saying so is what tells the user they have
            // arrowed past it rather than that the label is broken.
            return double.IsNaN(ctx.Value)
                ? $"{Describe(ctx.Series)}, not on this bar"
                : Describe(ctx.Series);
        }

        /// <summary>
        /// The spoken form of a label: "Label. &lt;wording&gt;", or "Label, no text" when the
        /// prompt was cancelled. Shared with <see cref="NavigationFeedbackManager"/>, which
        /// speaks the same phrase when the cursor crosses a labelled bar from another series —
        /// one wording, so the label sounds the same wherever it is met.
        /// </summary>
        internal static string Describe(ChartSeries series)
        {
            string text = series.Drawing?.Text?.Trim() ?? string.Empty;
            return string.IsNullOrEmpty(text) ? "Label, no text" : $"Label. {text}";
        }
    }

    /// <summary>
    /// Strategy #2: the indicator provider's own contextual speech
    /// (IIndicatorProvider.GetComponentSpeech) — e.g. Cipher's "Greed Phase 7,
    /// volatility expanding". Moved here from NavigationFeedbackManager's old
    /// "path 1" so the whole utterance precedence lives in one list. Declines
    /// (returns null) for components the provider has no custom narrative for.
    /// </summary>
    internal sealed class ProviderSpeechStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) => ctx.Provider != null;

        public string? Format(ComponentFormatContext ctx)
        {
            var series = ctx.Series;
            // Component data keyed by DisplayName, plus the companion arrays
            // providers read directly: _color (gradient source), _touches (S/R
            // pivot counts), and __live_close (present price for distance speech
            // regardless of how far back the cursor is).
            var compDataDict = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            // The parts left out too (SeriesConfig.ExcludedComponents): still computed, and a part's
            // speech may read one — DBSI's trend says which side of it the close is from its
            // Trend source close, left out by default since 2026-10-23.
            foreach (var c in series.Components.Concat(series.Config.ExcludedComponents))
            {
                var cd = series.GetComponentData(c.Name);
                if (cd != null) compDataDict[c.DisplayName ?? c.Name] = cd;

                if (c.UsesGradientSpeech)
                {
                    var colorKey = c.Name + "_color";
                    var colorData = series.GetComponentData(colorKey);
                    if (colorData.Length > 0 && !compDataDict.ContainsKey(colorKey))
                        compDataDict[colorKey] = colorData;
                }

                var touchKey = c.Name + "_touches";
                var touchData = series.GetComponentData(touchKey);
                if (touchData.Length > 0 && !compDataDict.ContainsKey(touchKey))
                    compDataDict[touchKey] = touchData;

                // A zone line's own companions, by suffix rather than by a naming convention:
                // "{zone}_since" (when the level became live — Cipher SR speaks its age from it)
                // and "{zone}_replaced". Cipher SR's touch arrays are named after the zone without
                // " Zone" — the narrator's convention — and since its pivot-dot components were
                // retired (2026-10-17) no component carries that name, so they are added here too.
                if (c.IsZoneLine)
                {
                    foreach (var key in new[] { c.Name + "_since", c.Name + "_replaced", c.Name.Replace(" Zone", "") + "_touches" })
                    {
                        var extra = series.GetComponentData(key);
                        if (extra.Length > 0 && !compDataDict.ContainsKey(key)) compDataDict[key] = extra;
                    }
                }
            }
            // And every other array the provider wrote: a component's companions
            // (ComponentCompanions — a cycle low's length, a confirmation's age) and the
            // provider's own "__" keys, so a component's speech can say what its value means.
            foreach (var kv in series.Data.ComponentData)
                if (!compDataDict.ContainsKey(kv.Key)) compDataDict[kv.Key] = kv.Value;
            if (ctx.LiveClose is double live)
                compDataDict["__live_close"] = new[] { live };

            // Name (the provider's internal switch key), not DisplayName.
            string? speech = ctx.Provider!.GetComponentSpeech(
                ctx.Comp.Name, ctx.Value, ctx.Pt, compDataDict, ctx.DataIndex);
            if (speech == null) return null; // decline → template chain below

            // On UP/DOWN (component switch) prepend "[Name]. [Type]." so the user hears what they
            // landed on; LEFT/RIGHT scans speak value only. The hidden/muted qualifier used to be
            // built here too, as `!IsVisible ? "Hidden. " : IsMuted ? "Muted. " : ""` — an
            // if/else over two INDEPENDENT flags, so hidden won and a component that was both
            // never said "muted". It now comes from the dispatcher, in front of whichever
            // strategy answered, so all ten of them carry it instead of one.
            if (!ctx.IsYMove) return speech;

            string stateLabel = "";
            string namePart = ctx.Comp.DisplayName ?? ctx.Comp.Name;

            // Sparse signal-marker (Cipher dots etc.): on landing, lead with the name
            // and HOW MANY signals are in the visible window, then the value at the bar
            // you actually landed on (the provider says "no data" when this bar has no
            // signal). The count stands in for the generic "Signal" type label, and
            // Ctrl+Left/Right afterwards scans between the lit dots (value-only, above).
            if (ctx.Comp.SignalSpeechTemplate != null
                && AudioConstants.MarkerDisplayTypes.Contains(ctx.Comp.DisplayType))
            {
                string countPhrase = MarkerSignalStrategy.SignalsInViewPhrase(
                    ctx.Series.GetComponentData(ctx.Comp.Name), ctx.ViewportStart, ctx.ViewportLength);
                return string.IsNullOrEmpty(countPhrase)
                    ? $"{namePart}. {stateLabel}{speech}"
                    : $"{namePart}. {stateLabel}{countPhrase}. {speech}";
            }

            string typeLabel = ComponentTypeLabel(ctx.Comp, ctx.Series);
            return string.IsNullOrEmpty(typeLabel)
                ? $"{namePart}. {stateLabel}{speech}"
                : $"{namePart}. {typeLabel}. {stateLabel}{speech}";
        }

        /// <summary>Short spoken type qualifier (moved from NavigationFeedbackManager).</summary>
        internal static string ComponentTypeLabel(ComponentConfig comp, ChartSeries? series = null)
        {
            var dt = comp.DisplayType;
            // A window says what it draws besides its shading, so nothing on the chart is out of
            // reach (Cody, 2026-10-22): "Window, with the cycle arc." Only when the picture is
            // there: Cipher C's weekly window has no arc on a chart with a daily cycle.
            if (dt == ComponentDisplayType.Window)
                return comp.DrawsPathInEffect() && WindowSpans.HasPath(series, comp)
                    ? $"Window, with the {comp.PathName}" : "Window";
            if (dt is ComponentDisplayType.Oscillator or ComponentDisplayType.ZeroArea) return "Oscillator";
            if (dt == ComponentDisplayType.CandleColor) return "Sentiment Phase";
            if (dt == ComponentDisplayType.Line) return comp.IsZoneLine ? "Level" : "Line";
            if (dt == ComponentDisplayType.Histogram) return "Histogram";
            if (dt == ComponentDisplayType.ZeroDot) return "";
            if (comp.Role == ComponentRole.Level) return "Level";
            if (dt is ComponentDisplayType.Dot or ComponentDisplayType.Diamond or
                       ComponentDisplayType.Arrow or ComponentDisplayType.Cross or
                       ComponentDisplayType.TriangleUp or ComponentDisplayType.TriangleDown or
                       ComponentDisplayType.Square or ComponentDisplayType.CheckBox or
                       ComponentDisplayType.RefreshBox or ComponentDisplayType.TriangleUpRing)
                return (comp.DisplayName ?? comp.Name).Contains("Signal", StringComparison.OrdinalIgnoreCase)
                    ? "" : "Signal";
            return "";
        }
    }

    /// <summary>
    /// Announces hidden components so the user still knows where Y-navigation landed. It used to
    /// spell the state itself ("{name}: hidden") and could therefore never mention mute; the word
    /// now comes from the dispatcher's <see cref="VisibilityStateSpeech"/> prefix, which knows
    /// about both flags, so a component that is hidden AND muted says so.
    /// </summary>
    internal sealed class HiddenComponentStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) => !ctx.Comp.IsVisible;
        public string Format(ComponentFormatContext ctx) => ctx.Comp.DisplayName ?? ctx.Comp.Name;
    }

    /// <summary>
    /// Strategy #4: a DRAWING reads <c>{value}[, {position}][, {relation}].</c>
    ///
    /// <para>
    /// Until 2026-09-03 a trend line said "Line, line, 150.50" — the generic template meeting a
    /// component whose name is its own type — and a rectangle one bar outside its span said
    /// "Top, line, no data". Neither answered the question a trader who drew the line is asking
    /// while arrowing along it: WHERE ON THE DRAWING AM I, and which side of it is price. The
    /// rules are in <see cref="DrawingSpeech"/>; this class only puts them in order.
    /// </para>
    ///
    /// <para>
    /// Value first, because whatever interrupts cuts the END of a sentence. The position clause
    /// is omitted strictly inside the span and off an anchor, the relation clause when there is
    /// no price to compare, so sweeping forty bars is usually two items long. No name and no
    /// type word per bar: the name is constant across the sweep — it is spoken on the series
    /// switch and on Ctrl+Up/Down, where it changes — and a constant prefix in front of the one
    /// varying number is the shape this repo has deleted twice already (the text label's
    /// name-then-name, the sub-pane name before every component).
    /// </para>
    ///
    /// <para>
    /// <see cref="SpeechPriceFormatter"/>, never <c>F2</c>: a drawing lives in price space by
    /// construction, and its series id is neither "price" nor "candles", so the fallback strategy
    /// spoke a KAS trend line at 0.0363 as "0.04".
    /// </para>
    /// </summary>
    internal sealed class DrawingComponentStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) => ctx.Series.IsDrawing && ctx.Series.Drawing != null;

        public string Format(ComponentFormatContext ctx)
        {
            var drawing = ctx.Series.Drawing!;
            var position = DrawingSpeech.Locate(drawing, ctx.DataIndex, ctx.Bars);

            // No value here. "Before start, 20 bars." is a navigation instruction; "no data" was
            // a shrug. And the span behind that sentence is the anchors' geometry, never the
            // array's length — DrawingSpeech.Locate says why at length.
            if (double.IsNaN(ctx.Value))
                return DrawingSpeech.NoValueSentence(position);

            string value = SpeechPriceFormatter.FormatPrice(ctx.Value);
            if (!ctx.ReadHeaders || ctx.SpeechOrder == "ValueOnly")
                return value;

            var parts = new List<string>(3) { value };

            string? where = DrawingSpeech.PositionClause(position);
            if (where != null) parts.Add(where);

            string? relation = Relation(ctx);
            if (relation != null) parts.Add(relation);

            return string.Join(", ", parts) + ".";
        }

        /// <summary>
        /// Price against the drawing at this bar, with the previous bar consulted so a cross
        /// replaces the plain side. The CLOSE of the loaded bar, not <c>Pt</c>: a drawing is
        /// compared against what traded, and <c>Pt</c> is the bar as drawn (Heikin-Ashi when
        /// that mode is on).
        /// </summary>
        private static string? Relation(ComponentFormatContext ctx)
        {
            var bars = ctx.Bars;
            int i = ctx.DataIndex;
            double close = bars != null && i >= 0 && i < bars.Count ? bars[i].Close : ctx.Pt.Close;

            double? prevValue = null, prevClose = null;
            if (bars != null && i - 1 >= 0 && i - 1 < bars.Count)
            {
                prevValue = SpeechFormatter.GetPointValue(ctx.Series, bars[i - 1], ctx.Comp.Name, i - 1);
                prevClose = bars[i - 1].Close;
            }
            return DrawingSpeech.RelationClause(ctx.Value, close, prevValue, prevClose);
        }
    }

    /// <summary>
    /// Cloud components (Ichimoku-style): announce bullish/bearish direction,
    /// cloud width, and whether price is inside / above / below the cloud.
    /// </summary>
    internal sealed class CloudComponentStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) => ctx.Comp.DisplayType == ComponentDisplayType.Cloud;

        public string Format(ComponentFormatContext ctx)
        {
            double signedWidth = ctx.Value;
            if (double.IsNaN(signedWidth))
                return $"{ctx.Comp.DisplayName}: no data";

            bool bullish = signedWidth >= 0;
            string direction = bullish ? "bullish" : "bearish";
            double absWidth = Math.Abs(signedWidth);

            string pricePosition = "";
            if (!string.IsNullOrEmpty(ctx.Comp.UpperComponentName) && !string.IsNullOrEmpty(ctx.Comp.LowerComponentName))
            {
                var upperData = ctx.Series.GetComponentData(ctx.Comp.UpperComponentName);
                var lowerData = ctx.Series.GetComponentData(ctx.Comp.LowerComponentName);
                if (upperData.Length > ctx.DataIndex && lowerData.Length > ctx.DataIndex)
                {
                    double u = upperData[ctx.DataIndex];
                    double l = lowerData[ctx.DataIndex];
                    if (!double.IsNaN(u) && !double.IsNaN(l))
                    {
                        double hi = Math.Max(u, l);
                        double lo = Math.Min(u, l);
                        double close = ctx.Pt.Close;
                        if (close >= lo && close <= hi)
                            pricePosition = " Price inside cloud.";
                        else if (close > hi)
                            pricePosition = " Price above cloud.";
                        else
                            pricePosition = " Price below cloud.";
                    }
                }
            }

            // Width is the distance between two lines on the same axis as price, so it is
            // price-space: an Ichimoku Kumo on a sub-cent asset is thousandths of a cent wide
            // and F2 announced every one of them as "width 0.00".
            return $"{ctx.Comp.DisplayName}. {direction}, width {SpeechPriceFormatter.FormatPrice(absWidth)}.{pricePosition}";
        }
    }

    /// <summary>
    /// CandleColor components carry a sentiment-phase index (0–10). Converts
    /// the raw numeric value to its phase name so the user hears "Neutral"
    /// instead of "CandleColor 5".
    /// </summary>
    internal sealed class PhaseNameStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) => ctx.Comp.DisplayType == ComponentDisplayType.CandleColor;

        public string Format(ComponentFormatContext ctx)
        {
            if (double.IsNaN(ctx.Value)) return $"{ctx.Comp.DisplayName}: warming up.";
            int phaseIdx = Math.Clamp((int)Math.Round(ctx.Value), 0, AudioConstants.PhaseNames.Length - 1);
            return $"{ctx.Comp.DisplayName}. {AudioConstants.PhaseNames[phaseIdx]}.";
        }
    }

    /// <summary>
    /// Marker components (Dot, Diamond, Cross, Arrow, Triangle*, Square) with a
    /// configured <see cref="ComponentConfig.SignalSpeechTemplate"/>. On UP/DOWN
    /// landing, leads with how many signals are in the visible window; then, and on
    /// LEFT/RIGHT scanning, speaks the value at the bar you actually landed on — the
    /// template when a signal fired there, "no data" when it did not.
    /// </summary>
    internal sealed class MarkerSignalStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) =>
            ctx.Comp.SignalSpeechTemplate != null
            && AudioConstants.MarkerDisplayTypes.Contains(ctx.Comp.DisplayType);

        public string Format(ComponentFormatContext ctx)
        {
            // Value AT the landed bar. Sparse markers are NaN at most bars → "no data"
            // there is correct (this bar has no signal); a fired bar expands the template.
            // Magnitude-aware formatting — sub-cent assets (SHIB, PEPE, KAS) would collapse
            // to "0" under F0. SpeechPriceFormatter carries ~3 significant digits.
            string value = double.IsNaN(ctx.Value)
                ? "no data"
                : ctx.Comp.SignalSpeechTemplate!
                    .Replace("{price}", SpeechPriceFormatter.FormatPrice(ctx.Value))
                    .Replace("{name}", ctx.Comp.DisplayName);

            // LEFT/RIGHT scan: just the value at this bar. UP/DOWN landing: lead with the
            // name + "N signals in view" so the user knows there ARE signals to jump to
            // with Ctrl+Left/Right, then the value at the bar they actually landed on.
            if (!ctx.IsYMove)
                return $"{ctx.Comp.DisplayName}: {value}";

            string countPhrase = SignalsInViewPhrase(
                ctx.Series.GetComponentData(ctx.Comp.Name), ctx.ViewportStart, ctx.ViewportLength);
            return string.IsNullOrEmpty(countPhrase)
                ? $"{ctx.Comp.DisplayName}: {value}"
                : $"{ctx.Comp.DisplayName}. {countPhrase}. {value}";
        }

        /// <summary>Counts non-NaN marker points within the visible viewport (whole
        /// array when the viewport is unknown). Returns -1 when the component has no
        /// data at all.</summary>
        internal static int CountMarkersInView(double[]? data, int viewportStart, int viewportLength)
        {
            if (data == null || data.Length == 0) return -1;
            int start = viewportStart < 0 || viewportLength <= 0 ? 0 : Math.Max(0, viewportStart);
            int end   = viewportStart < 0 || viewportLength <= 0 ? data.Length : Math.Min(data.Length, viewportStart + viewportLength);
            int count = 0;
            for (int i = start; i < end; i++)
                if (!double.IsNaN(data[i])) count++;
            return count;
        }

        /// <summary>"N signals in view" / "no signals in view" for the landing
        /// announcement, or "" when the component has no data at all (so the caller
        /// just speaks the value). Viewport unknown → counts the whole array.</summary>
        internal static string SignalsInViewPhrase(double[]? data, int viewportStart, int viewportLength)
        {
            int n = CountMarkersInView(data, viewportStart, viewportLength);
            if (n < 0)  return "";
            if (n == 0) return "no signals in view";
            return $"{n} signal{(n == 1 ? "" : "s")} in view";
        }
    }

    /// <summary>
    /// The candle Body component in component context (Ctrl+Up/Down onto "Body", then
    /// arrowing bars). The body IS the open→close span, so a single number can't convey
    /// its size — speak both ends plus direction: "Body. Bullish. Open 49,800, close
    /// 50,200." Series-context navigation (the full-candle summary) is unaffected.
    /// </summary>
    /// <summary>
    /// The price series' one part on a Line or Area chart, read like any other line: its name,
    /// its type, its value — "Close. line. 104.20." — then how it moved from the previous close.
    /// On Percent and Indexed the scale reading leads the value, as on the candle body.
    /// </summary>
    internal sealed class PriceLineStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) =>
            Charts.PriceSeriesShape.IsPriceSeries(ctx.Series.Config)
            && ctx.Comp.Name == Charts.PriceSeriesShape.LinePart;

        public string Format(ComponentFormatContext ctx)
        {
            double close = ctx.Pt.Close;
            string scale = StandardTemplateStrategy.ScaleReading(ctx, close);
            string value = (scale.Length == 0 ? "" : scale + ", ") + SpeechPriceFormatter.FormatPrice(close);
            string move = SpeechFormatter.MoveFromPreviousClose(ctx.Bars, ctx.DataIndex, close);
            if (!ctx.ReadHeaders || ctx.SpeechOrder == "ValueOnly")
                return $"{value}. {move}";
            return $"{ctx.Comp.DisplayName}. {SpeechFormatter.SpokenType(ctx.Comp, ctx.Series)}. {value}. {move}";
        }
    }

    /// <summary>
    /// The price series' one part on a Renko or line break chart: "Brick. Up. 110.00 to 120.00."
    /// ("Block. Up. ..." on line break) — its name,
    /// then its direction in the type's place (as the candle body says Bullish), then its two
    /// edges as drawn. The brick's real window follows from the caller. A Kagi line adds its
    /// thickness, which the chart draws: "Kagi line. Up, turns thick at 115.00. 110.00 to 120.00."
    /// </summary>
    internal sealed class BrickStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) =>
            Charts.PriceSeriesShape.IsPriceSeries(ctx.Series.Config)
            && ctx.Comp.Name is Charts.PriceSeriesShape.BrickPart or Charts.PriceSeriesShape.BlockPart
                or Charts.PriceSeriesShape.KagiPart;

        public string Format(ComponentFormatContext ctx)
        {
            var brick = ctx.Pt;
            string direction = brick.Close >= brick.Open ? "Up" : "Down";
            if (ctx.Comp.Name == Charts.PriceSeriesShape.KagiPart && ctx.Bars != null
                && ctx.DataIndex >= 0 && ctx.DataIndex < ctx.Bars.Count)
                direction += ", " + Charts.KagiLines.Words(Charts.KagiLines.At(ctx.Bars, ctx.DataIndex));
            string scale = StandardTemplateStrategy.ScaleReading(ctx, brick.Close);
            string edges = (scale.Length == 0 ? "" : scale + ", ")
                + $"{SpeechPriceFormatter.FormatPrice(brick.Open)} to {SpeechPriceFormatter.FormatPrice(brick.Close)}";
            if (!ctx.ReadHeaders || ctx.SpeechOrder == "ValueOnly")
                return $"{direction}, {edges}";
            return $"{ctx.Comp.DisplayName}. {direction}. {edges}.";
        }
    }

    internal sealed class CandleBodyStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) =>
            ctx.Comp.Role == ComponentRole.Body
            || ctx.Comp.DisplayType == ComponentDisplayType.Candle;

        public string Format(ComponentFormatContext ctx)
        {
            var pt = ctx.Pt;
            string open  = SpeechPriceFormatter.FormatPrice(pt.Open);
            string close = SpeechPriceFormatter.FormatPrice(pt.Close);
            // Percent and Indexed: the body is read by its close, so the close's place on the
            // scale leads ("plus 4.20 percent, open …"), as every other main-pane price does.
            string scale = StandardTemplateStrategy.ScaleReading(ctx, pt.Close);
            string lead = scale.Length == 0 ? "" : scale + ", ";
            if (!ctx.ReadHeaders || ctx.SpeechOrder == "ValueOnly")
                return $"{lead}Open {open}, close {close}";
            string trend = pt.Close >= pt.Open ? "Bullish" : "Bearish";
            return $"{ctx.Comp.DisplayName}. {trend}. {lead}Open {open}, close {close}.";
        }
    }

    /// <summary>
    /// Volume bars: bullish and bearish bars carry the same number, so speech marks
    /// direction the same way the bar's colour does (close vs open) — the exact value
    /// first, then a one-word direction: "12,345.68, down". Values are exact (full
    /// decimals when present), never rounded to a compact form.
    /// </summary>
    internal sealed class VolumeBarStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) =>
            ctx.Comp.Role == ComponentRole.Volume
            || ctx.Series.Id.Equals("volume", StringComparison.OrdinalIgnoreCase);

        public string Format(ComponentFormatContext ctx)
        {
            if (double.IsNaN(ctx.Value)) return "no data";
            string dir = ctx.Pt.Close >= ctx.Pt.Open ? "up" : "down";
            string v = FormatExactVolume(ctx.Value);
            if (!ctx.ReadHeaders || ctx.SpeechOrder == "ValueOnly")
                return $"{v}, {dir}";
            return $"{ctx.Comp.DisplayName}. {v}, {dir}.";
        }

        // Exact, not compact: whole-number volumes read without a fake ".00";
        // fractional volumes (crypto) keep their decimals.
        private static string FormatExactVolume(double vol)
            => vol == Math.Floor(vol) ? vol.ToString("N0", CultureInfo.InvariantCulture) : vol.ToString("N2", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Fallback: standard template processing with token substitution.
    /// Tokens: {name}, {type}, {value}, {value:Fn}, {trend}, {zone}, {gradient_speech}.
    /// Provider metadata supplies the template via <see cref="ComponentConfig.SpeechTemplate"/>;
    /// the default is "{name}. {type}. {value}.".
    /// </summary>
    internal sealed class StandardTemplateStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) => true;

        public string Format(ComponentFormatContext ctx)
        {
            double val = ctx.Value;
            var comp = ctx.Comp;
            var series = ctx.Series;

            // Price-family series (candles / price line) need magnitude-aware
            // precision so sub-dollar assets like KAS don't collapse to "0.04".
            string sId = series.Id.ToLowerInvariant();
            bool isPriceSeries = sId == "price" || sId == "candles";
            string valF2 = double.IsNaN(val)
                ? "no data"
                : (isPriceSeries ? SpeechPriceFormatter.FormatPrice(val) : val.ToString("F2", CultureInfo.InvariantCulture));

            // A price-family value (a wick's high or low, the Price line) reads its place on a
            // Percent or Indexed scale first wherever it is spoken — the value-only order and the
            // bare {value} token included, not only templates that spell {value:price}. Cody,
            // 2026-11-19: choosing Percent changed only the candles' summary sentence.
            string priceF2 = isPriceSeries ? PriceTokenValue(ctx, val) : valF2;

            if (!ctx.ReadHeaders || ctx.SpeechOrder == "ValueOnly")
                return double.IsNaN(val) ? "no data"
                    : isPriceSeries || TemplateReadsPrice(comp) ? PriceTokenValue(ctx, val) : valF2;

            // Template priority: provider metadata (comp.SpeechTemplate) → generic fallback.
            string tmpl = string.IsNullOrEmpty(comp.SpeechTemplate) ? "{name}. {type}. {value}." : comp.SpeechTemplate;

            string trend = ctx.Pt.Close >= ctx.Pt.Open ? "Bullish" : "Bearish";
            string zone = ResolveZone(series, val, comp);
            string gradientSpeech = ResolveGradientSpeech(ctx, tmpl);

            string result = tmpl
                .Replace("{gradient_speech}", gradientSpeech)
                .Replace("{name}", comp.DisplayName)
                .Replace("{type}", SpeechFormatter.SpokenType(comp, series));

            // Generic {value:Fn} format handler — catches F0, F1, F2, F3, ... so providers
            // can use any precision without adding a new Replace() line here.
            // On PRICE-FAMILY series the fixed precision is overridden by the
            // magnitude-aware formatter: saved workspaces persist the component's
            // SpeechTemplate, so an old "{value:F2}" on the price line would keep
            // collapsing sub-dollar assets (KAS at 0.0363 → "0.04") forever even
            // after the metadata default was fixed. Price values always speak with
            // ~3 significant digits regardless of what the stored template says.
            result = Regex.Replace(
                result,
                @"\{value:F(\d+)\}",
                m =>
                {
                    if (double.IsNaN(val)) return "no data";
                    if (isPriceSeries) return priceF2;
                    int digits = int.Parse(m.Groups[1].Value);
                    return val.ToString("F" + digits, CultureInfo.InvariantCulture);
                });

            // {value:price} is the magnitude-aware format token for price-space values
            // on non-price series — e.g. Regime's Close-minus-SMA delta, or an indicator
            // component that holds an absolute price level. Sub-cent assets (SHIB, PEPE,
            // KAS) would otherwise collapse to "0.00" through the {value} / {value:F2}
            // paths. Routes through SpeechPriceFormatter which scales precision by
            // magnitude and always carries ~3 significant digits.
            // In Percent and Indexed, a MAIN-pane value reads its place on the scale FIRST,
            // then the price: "plus 4.20 percent, 95000.50" / "index 104.20, 95000.50". The
            // COMPARE overlay reads against its OWN first visible value, so each symbol tells
            // its own change from the left edge. Every price of this symbol reads against the
            // chart's base. Indicator panes never take this path (the renderer's pct mode is
            // Main-only, and so is this).
            result = result.Replace("{value:price}", PriceTokenValue(ctx, val));

            return result
                .Replace("{value}", priceF2)
                .Replace("{trend}", trend)
                .Replace("{zone}", zone);
        }

        /// <summary>
        /// The <c>{value:price}</c> text: the price, preceded by the scale reading when this is
        /// a main-pane value on a Percent or Indexed chart.
        /// </summary>
        private static string PriceTokenValue(ComponentFormatContext ctx, double val)
        {
            string price = double.IsNaN(val) ? "no data" : SpeechPriceFormatter.FormatPrice(val);
            string reading = ScaleReading(ctx, val);
            return reading.Length == 0 ? price : reading + ", " + price;
        }

        /// <summary>Whether a component's own template reads a price-space value.</summary>
        private static bool TemplateReadsPrice(ComponentConfig comp) =>
            comp.SpeechTemplate?.Contains("{value:price}", StringComparison.Ordinal) == true;

        /// <summary>
        /// The scale reading of a main-pane value on a Percent or Indexed chart —
        /// <c>"plus 4.20 percent"</c>, <c>"index 104.20"</c> — or empty in Regular, off the main
        /// pane, or when the base cannot carry the mode.
        /// </summary>
        internal static string ScaleReading(ComponentFormatContext ctx, double val)
        {
            if (ctx.ScaleMode == PriceScaleMode.Regular || double.IsNaN(val)) return "";

            string pane = string.IsNullOrEmpty(ctx.Series.Pane) ? "Main" : ctx.Series.Pane;
            if (!string.Equals(pane, "Main", StringComparison.OrdinalIgnoreCase)) return "";

            if (!ChartMath.BaseUsable(ctx.ScaleBase)) return "";

            bool isCompare = string.Equals(ctx.Series.IndicatorCode,
                    Indicators.SymbolCompareProvider.OverlayCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(ctx.Comp.Name, Indicators.SymbolCompareProvider.CompLine,
                    StringComparison.OrdinalIgnoreCase);
            // The same transform the renderer and the range calculator use, not a second
            // spelling of it: for COMPARE the reading comes from chartBase × v / vFirst, so it
            // is that symbol's own change from the left edge.
            double drawnValue = isCompare
                ? ChartMath.RebaseCompareValue(ctx.ScaleBase, val,
                    ChartMath.FirstVisibleValue(ctx.Series.GetComponentData(ctx.Comp.Name),
                        ctx.ViewportStart, ctx.ViewportLength))
                : val;
            if (!ChartMath.BaseUsable(drawnValue)) return "";

            return ChartMath.FormatScaleReadingSpoken(ctx.ScaleMode,
                ChartMath.ScaleReading(ctx.ScaleMode, drawnValue, ctx.ScaleBase));
        }

        /// <summary>
        /// The word the <c>{zone}</c> token speaks: which named region of its own scale the value
        /// is in.
        ///
        /// <para>
        /// Two sources, in order. An overbought or oversold line answers by its
        /// <see cref="LevelConfig.EffectiveRole"/> — this used to match the WORDS "Overbought" and
        /// "Oversold" in the level's name, so a level that declared <c>Role = Overbought</c> under
        /// any other name produced nothing, and the four spellings of the midline had already
        /// taught this repo that lesson once. Then a BAND EDGE answers with the label it declares
        /// for the side the value is on.
        /// </para>
        ///
        /// <para>
        /// Band edges are why ADX and Choppiness spoke no zone at all until 2026-09-12: ADX's
        /// lines are Developing / Strong / Very Strong and Chop's are Trending / Ranging, none of
        /// which is an extreme, so no role fit and no name matched. The declared labels also carry
        /// the fact that CHOPPINESS IS INVERTED — below 38.2 is trending — which cannot be
        /// inferred from the numbers.
        /// </para>
        ///
        /// <para>
        /// Scoped to the component, through the subscription list the audio layer already honours:
        /// on a pane like Aroon's, where Up and Down run 0–100 about 50 while the Oscillator runs
        /// ±100 about zero, a level belonging to one of them means nothing for the others.
        /// </para>
        /// </summary>
        internal static string ResolveZone(ChartSeries series, double val, ComponentConfig? comp = null)
        {
            // The extremes win: being overbought is a more specific fact than being in the upper
            // band, and a pane can carry both.
            foreach (var lc in series.Config.Levels)
            {
                if (!lc.IsVisible || !Subscribes(comp, lc.Name)) continue;
                var role = lc.EffectiveRole;
                if (role == LevelRole.Overbought && val >= lc.Value) return "Overbought";
                if (role == LevelRole.Oversold   && val <= lc.Value) return "Oversold";
            }

            // Band edges: the tightest one the value is outside of, so ADX at 60 says "Very
            // Strong" rather than "Strong" — walk from the far edges inward by taking the label
            // whose boundary is closest to the value while still being crossed.
            string band = "";
            double bestDistance = double.MaxValue;
            foreach (var lc in series.Config.Levels)
            {
                if (!lc.IsVisible || !Subscribes(comp, lc.Name)) continue;

                string? label = val >= lc.Value ? lc.AboveLabel : lc.BelowLabel;
                if (string.IsNullOrWhiteSpace(label)) continue;

                double distance = Math.Abs(val - lc.Value);
                if (distance < bestDistance) { bestDistance = distance; band = label!; }
            }
            return band;
        }

        /// <summary>
        /// Whether a component answers to a level, using the subscription list
        /// <c>AudioZoneHelper</c> has always honoured: null subscribes to all, empty to none.
        /// </summary>
        private static bool Subscribes(ComponentConfig? comp, string levelName)
        {
            if (comp?.SubscribedLevelNames is not { } subs) return true;
            if (subs.Count == 0) return false;
            return subs.Contains(levelName, StringComparer.OrdinalIgnoreCase);
        }

        private static string ResolveGradientSpeech(ComponentFormatContext ctx, string tmpl)
        {
            if (!ctx.Comp.UsesGradientSpeech || !tmpl.Contains("{gradient_speech}"))
                return "";

            double oscillatorVal = SpeechFormatter.GetPointValue(ctx.Series, ctx.Pt, ctx.Comp.Name + "_color", ctx.DataIndex);
            if (double.IsNaN(oscillatorVal)) return "no data";

            string intensity = oscillatorVal > 60.0  ? "strong bullish momentum"
                             : oscillatorVal > 20.0  ? "moderate bullish momentum"
                             : oscillatorVal >= -20.0 ? "neutral momentum"
                             : oscillatorVal >= -60.0 ? "moderate bearish momentum"
                             :                          "strong bearish momentum";
            return $"{intensity}, {oscillatorVal.ToString("F1", CultureInfo.InvariantCulture)}";
        }
    }
}
