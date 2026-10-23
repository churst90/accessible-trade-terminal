using System.Globalization;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// <b>Cipher DBSI</b> (the name Cody asked for on 2026-10-22; code <c>DBSI_STYLE</c>) — a fast
    /// bull/bear strength score INSPIRED BY Market Cipher DBSI. It is not the vendor's DBSI: that
    /// formula is closed, and this one was reverse-fitted to it.
    ///
    /// <para>
    /// <b>Where the formula comes from.</b> 897 DBSI numbers were read by eye off twenty real
    /// charts in the feasibility study of 2026-10-17: six first (four Gunslinger Trades frames of
    /// Coinbase BTCUSD on 1D/1W/3D/2D, the vendor's own 15-minute Heikin-Ashi product image, a
    /// 30-minute Heikin-Ashi Bitstamp chart from the official channel), then fourteen more
    /// (Ethereum, XRP and Bitcoin, 10 minutes to 6 hours, five of them Heikin-Ashi). A
    /// greedy search over a library of fast "direction" conditions chose the twelve below on the
    /// first six charts (<c>dbsi/scripts/greedy.py</c>). The 2026-10-23 refit
    /// (<c>dbsi2/gen.py</c>, all twenty charts) gives each vote its own points
    /// (<see cref="VoteWeights"/>) and adds two terms; the numbers are copied here to the digit:
    /// </para>
    /// <code>
    ///   bull = round(max(−5, −5.244 + Σ points of the bull votes − 0.507 × min(bull streak, 8)
    ///                         + 0.689 × max(0, (50 − K14) / 10)))
    ///   bear = the same with −5.489, the bear votes, the bear streak and max(0, (K14 − 50) / 10)
    /// </code>
    /// <para>
    /// The <b>streak</b> is how many bars in a row have closed above (bear: below) the close two
    /// bars back; it counts AGAINST the score, so a fresh move scores higher than a long one. The
    /// <b>stretch</b> is how far the 14-bar stochastic's %K (SMA3) sits on the far side of 50 —
    /// oversold adds to the bulls, overbought to the bears. Both beat twelve equal votes and six
    /// "families" of conditions over six lookbacks (which did worse); this is the best of the
    /// tries, not DBSI's own formula.
    /// </para>
    /// <para>
    /// A condition votes BULL when its quantity is above zero and BEAR when below (exactly zero,
    /// or still warming up: neither) — the study's <c>sym()</c>. On the candles chosen by
    /// "Compute on Heikin-Ashi candles" (on by default):
    /// </para>
    /// <list type="number">
    ///   <item><c>c&gt;c[2]</c> — close − close 2 bars ago</item>
    ///   <item><c>stochk16&gt;d</c> — K − D, K = SMA3 of the 16-bar stochastic, D = SMA3 of K</item>
    ///   <item><c>macd5_13_up</c> — change of EMA5 − EMA13 of close</item>
    ///   <item><c>hl3_up2</c> — change of the 2-bar SMA of hlc3</item>
    ///   <item><c>stochk5&gt;d</c> — as 2, 5 bars</item>
    ///   <item><c>wt1_up</c> — change of WaveTrend wt1 (hlc3, channel 9, average 12)</item>
    ///   <item><c>c&gt;mid2</c> — close − midpoint of the 2-bar high/low</item>
    ///   <item><c>wt1&gt;wt2</c> — wt1 − SMA3 of wt1</item>
    ///   <item><c>mom2</c> — close − close 2 bars ago, AGAIN: the search picked the same 2-bar
    ///         momentum under two names, so it counts twice. Kept, because the fitted numbers were
    ///         fitted with it counting twice; "fixing" it would be a different, unfitted model.</item>
    ///   <item><c>stochk6&gt;d</c> — as 2, 6 bars</item>
    ///   <item><c>c&gt;pc</c> — close − previous close</item>
    ///   <item><c>o&gt;po</c> — open − previous open</item>
    /// </list>
    /// <para>
    /// Moving averages follow the study (and Pine's <c>ta.ema</c>): an EMA is seeded with the SMA of
    /// its first <c>n</c> inputs. Where the study's numpy SMA quietly counted a warming-up NaN as
    /// zero, this blanks the bar; that changes nothing once every condition has warmed up (bar 29),
    /// and the golden replay below shows it changes none of the 897 values.
    /// </para>
    ///
    /// <para>
    /// <b>How close it is, measured</b> (leave-one-chart-out: refitted twenty times, each time
    /// scored on the chart it never saw; the conditions themselves were picked on the first six):
    /// the exact integer 16.7% of the time, within ±1 42%, within ±2 60%, typical error 3.4 points.
    /// The twelve equal votes it replaced, refitted the same way: 16%, 35%, 56%, 3.7. Their shipped
    /// numbers, on the fourteen charts they were never fitted to: 14%, 34%, 54%, 4.0 — this model is
    /// closer on 13 of those 14. In sample on all 897 values: 16.7%, 43%, 61%, 3.3, and the side in
    /// control named right on 94% of bars where DBSI's two numbers differ —
    /// <c>DbsiStyleGoldenTests</c> replays it.
    /// </para>
    ///
    /// <para>
    /// <b>The known shortfall.</b> The top of the scale is still compressed: of the 84 readings of
    /// 17 or more on the twenty charts (the ones Crypto Face calls rare and alerts on), this prints
    /// 17 or more on only 4, and on 8 bars where DBSI did not. The highest it can print is about 19.
    /// Direction and rough size are trustworthy; exact numbers and big readings are not.
    /// </para>
    ///
    /// <para>
    /// <b>The trend line.</b> DBSI's "Long Term Trend 200": a 200-bar EMA of the same candles'
    /// closes (two digitised points of TradingView idea vBzsjpDu matched an EMA of Heikin-Ashi
    /// closes within 0.3%, a 200 SMA was 1.5–4% off — tentative), green while that close is above
    /// it and red while below.
    /// </para>
    ///
    /// <para>
    /// <b>Heikin-Ashi.</b> DBSI computes on the chart's own candles. This provider is handed the raw
    /// bars even on a Heikin-Ashi chart (<see cref="IComputesHeikinAshiItself"/>) and does the
    /// transform here, from the first bar (<see cref="ChartMath.CalculateHeikinAshi"/>, the same
    /// transform the chart draws): always on a Heikin-Ashi chart, and on a candle chart while the
    /// option is on (the default — on the two Heikin-Ashi charts in the study, Heikin-Ashi input
    /// fitted better, r 0.93/0.93 vs 0.90/0.89 at 15m). Turn the option off on a candle chart to
    /// match DBSI there.
    /// </para>
    /// </summary>
    public sealed class DbsiStyleProvider : IIndicatorProvider, IComputesHeikinAshiItself
    {
        public const string Code = "DBSI_STYLE";

        public const string CompBull = "Bull";
        public const string CompBear = "Bear";
        public const string CompTrend = "Trend";
        public const string CompBullsTakeControl = "Bulls Take Control";
        public const string CompBearsTakeControl = "Bears Take Control";
        public const string CompLongEntry = "Long Entry";
        public const string CompShortEntry = "Short Entry";
        public const string CompControl = "Control";
        public const string CompTrendSource = "Trend Source";

        public const string ParamLongEntry = "longEntryScore";
        public const string ParamShortEntry = "shortEntryScore";
        public const string ParamTrendLength = "trendLength";
        public const string ParamHeikinAshi = "useHeikinAshi";
        /// <summary>The series parameter a ValueLabel component reads for "label the newest N bars".</summary>
        public const string ParamLabelBars = ValueLabelBarsParameter;

        /// <summary>
        /// The generic name the renderer looks for on ANY series carrying a
        /// <see cref="ComponentDisplayType.ValueLabel"/> component (see that enum member).
        /// </summary>
        public const string ValueLabelBarsParameter = Rendering.ValueLabelRenderer.LabelBarsParameter;

        public const int DefaultLongEntry = 7, DefaultShortEntry = 7, DefaultTrendLength = 200, DefaultLabelBars = 25;

        // The model fitted on all twenty digitised charts (scratchpad dbsi2/gen.py, 2026-10-23), to the digit.
        internal const double BullIntercept = -5.243776448904809;
        internal const double BearIntercept = -5.488630059893191;
        /// <summary>Each vote's points, in the order of the quantities in <see cref="Compute"/>.</summary>
        internal static readonly double[] VoteWeights =
        {
            3.0997342786874698, 0.4459414887631937, 0.3503672928684513, 2.4039377111990494,
            0.7765487920083385, 2.9364097076608937, 0.863954814683868, 3.320275058470089,
            3.099734278687473, 1.307596993535656, 1.427224224788756, 1.0976267615037851,
        };
        /// <summary>Points per bar of the side's streak (closes beyond the close two bars back), up to <see cref="StreakCap"/> bars.</summary>
        internal const double StreakWeight = -0.5068956846943783;
        internal const int StreakCap = 8;
        /// <summary>Points per ten of the 14-bar stochastic's %K beyond 50 against the side (oversold for bulls).</summary>
        internal const double StretchWeight = 0.6892391919941329;
        internal const int StretchLength = 14;
        internal const double Floor = -5.0;
        internal const int VoteCount = 12;

        // DBSI's own colours as seen on the vendor image: green below, red above, yellow live.
        internal const string BullHex = "#00E676", BearHex = "#FF5252", LiveHex = "#FFEB3B";
        internal const string TrendAboveHex = "#00C853", TrendBelowHex = "#FF1744";

        public string Name => "Cipher DBSI";

        private static readonly List<IndicatorMetadata> _indicators = new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Cipher DBSI", Category = "Trend",
                DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Description =
                    "A fast bull/bear strength score inspired by Market Cipher DBSI, printed on the candles: " +
                    "bulls below each candle, bears above, the newest candle's leader in yellow, and a 200-bar trend " +
                    "line green above price and red below. Reverse-fitted from 897 DBSI numbers on twenty charts: the side in " +
                    "control agrees with DBSI on about 9 bars in 10, but the numbers are often 2 to 4 points off and it " +
                    "rarely goes above 16 where DBSI reaches the 20s. Not DBSI.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamLongEntry, DisplayName = "Long entry score", DataType = typeof(int),
                            DefaultValue = (double)DefaultLongEntry, MinValue = -5, MaxValue = 40, Step = 1,
                            Description = "Bulls reaching this score (from below) is announced. DBSI's 'Long Entry Candle Score (>=)', default 7; Crypto Face trades 14." },
                    new() { Name = ParamShortEntry, DisplayName = "Short entry score", DataType = typeof(int),
                            DefaultValue = (double)DefaultShortEntry, MinValue = -5, MaxValue = 40, Step = 1,
                            Description = "Bears reaching this score (from below) is announced. DBSI's 'Short Entry Candle Score (>=)', default 7." },
                    new() { Name = ParamTrendLength, DisplayName = "Trend length", DataType = typeof(int),
                            DefaultValue = (double)DefaultTrendLength, MinValue = 2, Step = 1,
                            Description = "Bars in the trend line's EMA. DBSI's 'Long Term Trend', default 200." },
                    new() { Name = ParamHeikinAshi, DisplayName = "Compute on Heikin-Ashi candles", DataType = typeof(bool),
                            DefaultValue = true,
                            Description = "Default ON. Scores and trend are computed from Heikin-Ashi candles, as DBSI is on the Heikin-Ashi charts it is used on (and the fit is better there). Off = regular candles, which is what DBSI uses on a regular chart." },
                    new() { Name = ParamLabelBars, DisplayName = "Label the newest N bars", DataType = typeof(int),
                            DefaultValue = (double)DefaultLabelBars, MinValue = 0, Step = 1,
                            Description = "How many of the newest candles get their numbers printed. DBSI shows 25 (TradingView's label limit). 0 = every candle. Drawing only: every bar is still spoken." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    // Bulls FIRST: the navigation order is the reading order — "Bulls 15, bears minus 5".
                    new() { Name = CompBull, DisplayName = "Bulls", DisplayType = ComponentDisplayType.ValueLabel,
                            Role = ComponentRole.Signal, Causality = ComponentCausality.Causal,
                            DefaultMarkerAnchor = MarkerAnchor.BelowBar,
                            DefaultColorHex = BullHex, DefaultColorHexSecondary = LiveHex,
                            DefaultColorAboveComponent = CompBear,
                            // Muted like Cipher B's RSI and Stochastic: drawn and spoken, no voice of its own.
                            DefaultIsMuted = true, DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None, DefaultPlaybackLayer = PlaybackLayer.Background },
                    new() { Name = CompBear, DisplayName = "Bears", DisplayType = ComponentDisplayType.ValueLabel,
                            Role = ComponentRole.Signal, Causality = ComponentCausality.Causal,
                            DefaultMarkerAnchor = MarkerAnchor.AboveBar,
                            DefaultColorHex = BearHex, DefaultColorHexSecondary = LiveHex,
                            DefaultColorAboveComponent = CompBull,
                            DefaultIsMuted = true, DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None, DefaultPlaybackLayer = PlaybackLayer.Background },
                    // Green where its close is above it, red where below: the Trend Source sibling is
                    // that close, and a line is drawn in its SECONDARY colour where it is above the sibling.
                    // Not muted: a muted line's crossings are not narrated, and "price crossed the
                    // trend" is one of the three things this indicator says unprompted.
                    new() { Name = CompTrend, DisplayName = "Trend", DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.Median, Causality = ComponentCausality.Causal,
                            DefaultColorHex = TrendAboveHex, DefaultColorHexSecondary = TrendBelowHex,
                            DefaultColorAboveComponent = CompTrendSource, DefaultThickness = 2f,
                            DefaultUsePolarityColoring = false, DefaultIsAreaFill = false },
                    // The narrated events: heard, not painted (DBSI draws nothing for them — the
                    // numbers on the candles already show who leads), never muted (a muted marker is
                    // not narrated).
                    new() { Name = CompBullsTakeControl, DisplayName = "Bulls take control", DisplayType = ComponentDisplayType.TriangleUp,
                            Role = ComponentRole.Signal, Causality = ComponentCausality.Causal,
                            DefaultIsDrawn = false, DefaultMarkerAnchor = MarkerAnchor.BelowBar,
                            DefaultColorHex = BullHex, DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSignalSpeechTemplate = "Bulls take control at {price}." },
                    new() { Name = CompBearsTakeControl, DisplayName = "Bears take control", DisplayType = ComponentDisplayType.TriangleDown,
                            Role = ComponentRole.Signal, Causality = ComponentCausality.Causal,
                            DefaultIsDrawn = false, DefaultMarkerAnchor = MarkerAnchor.AboveBar,
                            DefaultColorHex = BearHex, DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSignalSpeechTemplate = "Bears take control at {price}." },
                    new() { Name = CompLongEntry, DisplayName = "Long entry score", DisplayType = ComponentDisplayType.Diamond,
                            Role = ComponentRole.Signal, Causality = ComponentCausality.Causal,
                            DefaultIsDrawn = false, DefaultMarkerAnchor = MarkerAnchor.BelowBar,
                            DefaultColorHex = BullHex, DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSignalSpeechTemplate = "Bulls reached the long entry score at {price}." },
                    new() { Name = CompShortEntry, DisplayName = "Short entry score", DisplayType = ComponentDisplayType.Diamond,
                            Role = ComponentRole.Signal, Causality = ComponentCausality.Causal,
                            DefaultIsDrawn = false, DefaultMarkerAnchor = MarkerAnchor.AboveBar,
                            DefaultColorHex = BearHex, DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSignalSpeechTemplate = "Bears reached the short entry score at {price}." },
                    // For strategies and alerts: +1 bulls ahead, −1 bears ahead, 0 a tie. Hidden and
                    // undrawn (a ±1 line on the price pane is not a price).
                    new() { Name = CompControl, DisplayName = "Control", DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.Signal, Causality = ComponentCausality.Causal,
                            IsVisible = false, DefaultIsDrawn = false, DefaultIsMuted = true,
                            DefaultColorHex = LiveHex },
                    // The close the trend is computed from and coloured against (Heikin-Ashi when the
                    // option is on). Hidden and undrawn; it exists so the trend line can be two-coloured.
                    new() { Name = CompTrendSource, DisplayName = "Trend source close", DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.Signal, Causality = ComponentCausality.Causal,
                            IsVisible = false, DefaultIsDrawn = false, DefaultIsMuted = true,
                            DefaultColorHex = "#9E9E9E" },
                },
            },
        };

        public List<IndicatorMetadata> GetIndicators() => _indicators;

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var p = Settings.From(parameters);
            var r = Compute(data, p);
            Write(buffer, CompBull, r.Bull);
            Write(buffer, CompBear, r.Bear);
            Write(buffer, CompTrend, r.Trend);
            Write(buffer, CompBullsTakeControl, r.BullsTakeControl);
            Write(buffer, CompBearsTakeControl, r.BearsTakeControl);
            Write(buffer, CompLongEntry, r.LongEntry);
            Write(buffer, CompShortEntry, r.ShortEntry);
            Write(buffer, CompControl, r.Control);
            Write(buffer, CompTrendSource, r.Source);
        }

        private static void Write(IIndicatorResultBuffer buffer, string comp, double[] values)
        {
            var span = buffer.GetComponentSpan(comp);
            int n = Math.Min(span.Length, values.Length);
            for (int i = 0; i < n; i++) span[i] = values[i];
        }

        /// <summary>A full recalculation: twelve short windows and three EMAs, cheap at any length.</summary>
        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        /// <summary>
        /// The trend EMA dominates: its SMA seed, then about 6.9·(n+1)/2 bars for the seed to decay
        /// below a part in a million. The scores alone settle within 60 bars (the golden replay
        /// reproduces every digitised bar from 60 bars of history).
        /// </summary>
        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int t = Settings.From(parameters).TrendLength;
            return Math.Max(60, t + (int)Math.Ceiling(7.0 * (t + 1) / 2.0));
        }

        public List<LevelDescriptor> GetDefaultLevels(string code) => new();

        // ── Speech ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// "Bulls 15, bears minus 5, bulls in control." — on either score, because the two are one
        /// reading: who is ahead and by how much. "Minus" spelled out: a screen reader reads "-5"
        /// as "dash 5" on some voices. The event markers say what happened at the bar.
        /// </summary>
        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            double At(string displayName) =>
                allComponentData.TryGetValue(displayName, out var a) && dataIndex >= 0 && dataIndex < a.Length ? a[dataIndex] : double.NaN;

            switch (componentName)
            {
                case CompBull:
                case CompBear:
                {
                    double bull = At("Bulls"), bear = At("Bears");
                    if (double.IsNaN(bull) || double.IsNaN(bear)) return "Calculating";
                    return componentName == CompBull ? Reading(bull, bear) : ReadingBearsFirst(bull, bear);
                }
                case CompTrend:
                {
                    double t = At("Trend"), src = At("Trend source close");
                    if (double.IsNaN(t)) return "Calculating";
                    string side = double.IsNaN(src) ? "" : src > t ? ", price above" : src < t ? ", price below" : ", price on it";
                    return $"{SpeechPriceFormatter(t)}{side}";
                }
                case CompBullsTakeControl:
                case CompBearsTakeControl:
                case CompLongEntry:
                case CompShortEntry:
                {
                    if (double.IsNaN(value)) return "none";
                    double bull = At("Bulls"), bear = At("Bears");
                    return double.IsNaN(bull) || double.IsNaN(bear) ? "yes" : $"yes, {Reading(bull, bear)}";
                }
                default:
                    return null;
            }
        }

        private static string SpeechPriceFormatter(double v) =>
            Accessibility.SpeechPriceFormatter.FormatPrice(v);

        /// <summary>"Bulls 15, bears minus 5, bulls in control"</summary>
        public static string Reading(double bull, double bear) =>
            $"Bulls {Spoken(bull)}, bears {Spoken(bear)}, {ControlWords(bull, bear)}";

        private static string ReadingBearsFirst(double bull, double bear) =>
            $"Bears {Spoken(bear)}, bulls {Spoken(bull)}, {ControlWords(bull, bear)}";

        public static string ControlWords(double bull, double bear) =>
            bull > bear ? "bulls in control" : bear > bull ? "bears in control" : "tied";

        /// <summary>An integer score as spoken: "15", "minus 5", "0".</summary>
        public static string Spoken(double v)
        {
            long n = (long)Math.Round(v);
            return n < 0 ? $"minus {(-n).ToString(CultureInfo.InvariantCulture)}" : n.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The state at a bar in words, for Alt+Shift+D's ladder (which has already read the two
        /// numbers, and names a hand-over or an entry score itself, as the marker that fired):
        /// who is in control and which side of the trend line the close it is computed from is on.
        /// </summary>
        public static IReadOnlyList<string> StateWords(Func<string, double[]?> component, int index)
        {
            var words = new List<string>();
            double At(string name) => component(name) is { } a && index >= 0 && index < a.Length ? a[index] : double.NaN;
            double bull = At(CompBull), bear = At(CompBear);
            if (double.IsNaN(bull) || double.IsNaN(bear)) return words;
            words.Add(ControlWords(bull, bear));
            double t = At(CompTrend), src = At(CompTrendSource);
            if (!double.IsNaN(t) && !double.IsNaN(src))
                words.Add(src > t ? "above the trend line" : src < t ? "below the trend line" : "on the trend line");
            return words;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults,
            int index, Dictionary<string, object> parameters)
        {
            var words = StateWords(n => calculatedResults.TryGetValue(n, out var a) ? a : null, index);
            if (words.Count == 0) return "Calculating...";
            calculatedResults.TryGetValue(CompBull, out var b);
            calculatedResults.TryGetValue(CompBear, out var r);
            return $"{Reading(b![index], r![index])}. {string.Join(", ", words.Skip(1))}".TrimEnd(' ', '.') + ".";
        }

        // ── The maths ───────────────────────────────────────────────────────────────────

        internal readonly record struct Settings(int LongEntry, int ShortEntry, int TrendLength, bool HeikinAshi)
        {
            public static Settings From(Dictionary<string, object>? p) => new(
                GetInt(p, ParamLongEntry, DefaultLongEntry),
                GetInt(p, ParamShortEntry, DefaultShortEntry),
                Math.Max(2, GetInt(p, ParamTrendLength, DefaultTrendLength)),
                // On a Heikin-Ashi chart it reads Heikin-Ashi whatever the switch says, as DBSI on
                // TradingView reads the chart's candles; built here once, from the raw bars it is
                // handed (IComputesHeikinAshiItself), never Heikin-Ashi of Heikin-Ashi.
                IndicatorParams.GetBool(p, ParamHeikinAshi, true) || IndicatorCandles.IsHeikinAshi(p));
        }

        internal sealed record Result(
            double[] Bull, double[] Bear, double[] Control, double[] Trend, double[] Source,
            double[] BullsTakeControl, double[] BearsTakeControl, double[] LongEntry, double[] ShortEntry,
            int[] BullVotes, int[] BearVotes);

        /// <summary>
        /// What the score is built from, bar by bar: each of the twelve quantities whose sign is a
        /// vote, each side's streak (uncapped) and the 14-bar %K, on the candles it reads.
        /// </summary>
        internal sealed record Terms(double[][] Quantities, int[] BullRun, int[] BearRun, double[] K14, double[] Close);

        internal static Terms ScoringTerms(ReadOnlySpan<Ohlcv> data, bool heikinAshi)
        {
            int n = data.Length;
            double[] o = new double[n], h = new double[n], l = new double[n], c = new double[n];
            if (heikinAshi && n > 0)
            {
                var ha = ChartMath.CalculateHeikinAshi(data.ToArray().ToList());
                for (int i = 0; i < n; i++) { o[i] = ha[i].Open; h[i] = ha[i].High; l[i] = ha[i].Low; c[i] = ha[i].Close; }
            }
            else
            {
                for (int i = 0; i < n; i++) { o[i] = data[i].Open; h[i] = data[i].High; l[i] = data[i].Low; c[i] = data[i].Close; }
            }

            var hlc3 = new double[n];
            for (int i = 0; i < n; i++) hlc3[i] = (h[i] + l[i] + c[i]) / 3.0;

            // The twelve quantities whose sign is the vote, in the study's selection order.
            var mom2 = Change(c, 2);
            var macd = Change(Sub(Ema(c, 5), Ema(c, 13)), 1);
            var hl3 = Change(Sma(hlc3, 2), 1);
            var esa = Ema(hlc3, 9);
            var dd = Ema(Abs(Sub(hlc3, esa)), 9);
            var ci = new double[n];
            for (int i = 0; i < n; i++) ci[i] = (hlc3[i] - esa[i]) / (0.015 * dd[i]);
            var wt1 = Ema(ci, 12);
            var mid2 = new double[n];
            for (int i = 0; i < n; i++) mid2[i] = i >= 1 ? c[i] - (Math.Max(h[i], h[i - 1]) + Math.Min(l[i], l[i - 1])) / 2.0 : double.NaN;

            double[][] q =
            {
                mom2,                     // c>c[2]
                StochKMinusD(c, h, l, 16),// stochk16>d
                macd,                     // macd5_13_up
                hl3,                      // hl3_up2
                StochKMinusD(c, h, l, 5), // stochk5>d
                Change(wt1, 1),           // wt1_up
                mid2,                     // c>mid2
                Sub(wt1, Sma(wt1, 3)),    // wt1>wt2
                mom2,                     // mom2 — the same quantity again; see the class comment
                StochKMinusD(c, h, l, 6), // stochk6>d
                Change(c, 1),             // c>pc
                Change(o, 1),             // o>po
            };

            // The streak: how many bars in a row have closed above (bulls) or below (bears) the close two bars back.
            var bullRun = new int[n]; var bearRun = new int[n];
            for (int i = 0; i < n; i++)
            {
                bullRun[i] = mom2[i] > 0 ? (i > 0 ? bullRun[i - 1] : 0) + 1 : 0;
                bearRun[i] = mom2[i] < 0 ? (i > 0 ? bearRun[i - 1] : 0) + 1 : 0;
            }

            // The stretch: the 14-bar stochastic's %K (SMA3), centred on 50.
            return new Terms(q, bullRun, bearRun, Sma(Stochastic(c, h, l, StretchLength), 3), c);
        }

        internal static Result Compute(ReadOnlySpan<Ohlcv> data, Settings s)
        {
            int n = data.Length;
            var t = ScoringTerms(data, s.HeikinAshi);
            var q = t.Quantities; var c = t.Close; var stretch = t.K14;

            var bull = Nan(n); var bear = Nan(n); var control = Nan(n);
            var bullVotes = new int[n]; var bearVotes = new int[n];
            for (int i = 0; i < n; i++)
            {
                int vb = 0, vr = 0; double pb = 0, pr = 0; bool ready = true;
                for (int j = 0; j < q.Length; j++)
                {
                    double v = q[j][i];
                    if (double.IsNaN(v) || double.IsInfinity(v)) { ready = false; break; }
                    if (v > 0) { vb++; pb += VoteWeights[j]; } else if (v < 0) { vr++; pr += VoteWeights[j]; }
                }
                if (!ready) continue;
                bullVotes[i] = vb; bearVotes[i] = vr;
                double k = double.IsNaN(stretch[i]) ? 50 : stretch[i];   // a flat window: no stretch either way
                pb += StreakWeight * Math.Min(t.BullRun[i], StreakCap) + StretchWeight * Math.Max(0, (50 - k) / 10);
                pr += StreakWeight * Math.Min(t.BearRun[i], StreakCap) + StretchWeight * Math.Max(0, (k - 50) / 10);
                bull[i] = Score(BullIntercept, pb);
                bear[i] = Score(BearIntercept, pr);
                control[i] = Math.Sign(bull[i] - bear[i]);
            }

            var (bullsTake, bearsTake, longEntry, shortEntry) = Events(bull, bear, control, s.LongEntry, s.ShortEntry);

            var trend = Ema(c, s.TrendLength);
            return new Result(bull, bear, control, trend, (double[])c.Clone(), bullsTake, bearsTake, longEntry, shortEntry, bullVotes, bearVotes);
        }

        /// <summary>
        /// The narrated events, from the scores. A hand-over is the leader changing from the last
        /// side that LED — a tie in between hands nothing over, so bulls → tie → bulls says nothing
        /// and bears → tie → bulls says "bulls take control" on the bulls' bar. An entry is a side
        /// reaching its entry score from below. Each fires with that side's score as its value.
        /// </summary>
        internal static (double[] BullsTake, double[] BearsTake, double[] LongEntry, double[] ShortEntry) Events(
            double[] bull, double[] bear, double[] control, int longEntryScore, int shortEntryScore)
        {
            int n = control.Length;
            var bullsTake = Nan(n); var bearsTake = Nan(n); var longEntry = Nan(n); var shortEntry = Nan(n);
            int lastLeader = 0;
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(control[i])) continue;
                int ctl = (int)control[i];
                if (ctl != 0)
                {
                    if (lastLeader != 0 && ctl != lastLeader)
                    {
                        if (ctl > 0) bullsTake[i] = bull[i]; else bearsTake[i] = bear[i];
                    }
                    lastLeader = ctl;
                }
                if (i > 0 && !double.IsNaN(bull[i - 1]))
                {
                    if (bull[i] >= longEntryScore && bull[i - 1] < longEntryScore) longEntry[i] = bull[i];
                    if (bear[i] >= shortEntryScore && bear[i - 1] < shortEntryScore) shortEntry[i] = bear[i];
                }
            }
            return (bullsTake, bearsTake, longEntry, shortEntry);
        }

        /// <summary>round(max(−5, intercept + points)), half to even like numpy's round.</summary>
        internal static double Score(double intercept, double points) =>
            Math.Round(Math.Max(Floor, intercept + points), MidpointRounding.ToEven);

        /// <summary>K − D: K = SMA3 of the n-bar stochastic of close, D = SMA3 of K. A flat window is undefined.</summary>
        private static double[] StochKMinusD(double[] c, double[] h, double[] l, int len)
        {
            var k = Sma(Stochastic(c, h, l, len), 3);
            return Sub(k, Sma(k, 3));
        }

        /// <summary>The n-bar stochastic of close, 0 to 100. A flat window is undefined.</summary>
        private static double[] Stochastic(double[] c, double[] h, double[] l, int len)
        {
            int n = c.Length;
            var st = Nan(n);
            for (int i = len - 1; i < n; i++)
            {
                double hh = double.NegativeInfinity, ll = double.PositiveInfinity;
                for (int j = i - len + 1; j <= i; j++) { if (h[j] > hh) hh = h[j]; if (l[j] < ll) ll = l[j]; }
                if (hh > ll) st[i] = 100.0 * (c[i] - ll) / (hh - ll);
            }
            return st;
        }

        /// <summary>SMA; NaN while the window holds a NaN.</summary>
        internal static double[] Sma(double[] x, int len) => IndicatorMath.Sma(x, len);

        /// <summary>
        /// EMA as the study (and Pine's <c>ta.ema</c>) seed it: the mean of the first <paramref name="len"/>
        /// inputs from the first defined one, then α = 2/(len+1). A NaN input after the seed (a flat
        /// window) blanks that bar and leaves the average where it was.
        /// </summary>
        internal static double[] Ema(double[] x, int len)
        {
            int n = x.Length;
            var r = Nan(n);
            int start = Array.FindIndex(x, v => !double.IsNaN(v));
            if (start < 0 || start + len > n) return r;
            double sum = 0; int cnt = 0;
            for (int i = start; i < start + len; i++) if (!double.IsNaN(x[i])) { sum += x[i]; cnt++; }
            if (cnt == 0) return r;
            double e = sum / cnt, a = 2.0 / (len + 1);
            r[start + len - 1] = e;
            for (int i = start + len; i < n; i++)
            {
                if (double.IsNaN(x[i]) || double.IsInfinity(x[i])) continue;
                e = a * x[i] + (1 - a) * e;
                r[i] = e;
            }
            return r;
        }

        private static double[] Change(double[] x, int k)
        {
            var r = Nan(x.Length);
            for (int i = k; i < x.Length; i++) r[i] = x[i] - x[i - k];
            return r;
        }

        private static double[] Sub(double[] a, double[] b)
        {
            var r = new double[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[i] - b[i];
            return r;
        }

        private static double[] Abs(double[] a)
        {
            var r = new double[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = Math.Abs(a[i]);
            return r;
        }

        private static double[] Nan(int n) { var r = new double[n]; Array.Fill(r, double.NaN); return r; }

        private static int GetInt(Dictionary<string, object>? p, string k, int def) =>
            p != null && p.TryGetValue(k, out var v) && v != null ? (int)Math.Round(Convert.ToDouble(v, CultureInfo.InvariantCulture)) : def;
    }
}
