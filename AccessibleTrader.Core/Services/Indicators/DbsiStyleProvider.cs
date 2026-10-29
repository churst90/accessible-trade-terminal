using System.Globalization;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// <b>Cipher DBSI</b> (the name Cody asked for on 2026-10-22; code <c>DBSI_STYLE</c>) — a fast
    /// bull/bear strength score INSPIRED BY Market Cipher DBSI. It is not the vendor's DBSI: that
    /// formula is closed, and this one was reverse-engineered from its printed numbers.
    ///
    /// <para>
    /// <b>The score</b> (2026-10-26, <c>dbsi8/clean_dbsi.py</c>, ported in <see cref="DbsiScoreModel"/>):
    /// whole numbers from −5 up, one for each side, on the candles chosen by "Compute on Heikin-Ashi
    /// candles". Each side starts from eight votes (−5 + 2 for each of six, + 1 for the WaveTrend gap)
    /// and then scores by where its WaveTrend (9, 12, 3) run is: off a run, the gap's widening streak
    /// and a Q state set by a low close up; alive (the gap has widened every bar since the cross), the
    /// run's level plus 3, one a bar and the ARM, a value remembered from the cross that raw Stoch RSI
    /// drops and re-arms; dead (the gap narrowed once), the level, the streak and a held step that raw
    /// Stoch RSI arms and drops in the same way. Around those three states sit the
    /// corrections the misses demanded, each a few points on a narrow kind of bar, written out in
    /// <see cref="DbsiScoreModel"/>. Bulls and bears run the same rules on mirrored prices with a few
    /// side-specific numbers (the bears' deep level 2 against the bulls' 7, a re-arm step of 3 against 2).
    /// </para>
    /// <para>
    /// <b>How it was found.</b> From DBSI's own numbers, read off its drawn labels (never its inputs):
    /// first 897 read by eye (the twenty charts, 2026-10-17), then thousands harvested from the vendor's
    /// script on Bitcoin hourly Heikin-Ashi, a Dow daily and Bitcoin daily, and fifteen livestream
    /// charts (all kept out of this repository). Each rule was kept only if it lost no exact matches on
    /// any of nine main sets and then on ten more sets never used to find a rule (gold, Apple, the Dow,
    /// the S&amp;P 500 and Ethereum daily; Bitcoin and Ethereum 4-hour; Bitstamp 4-hour and hourly).
    /// Those ten could veto a rule, so they are not untouched; the Bitstamp sets came last, after nearly
    /// every rule was in, and three more (Microsoft 15-minute, Bitstamp 5- and 1-minute) were harvested
    /// after the ARM and never used to choose anything.
    /// </para>
    /// <para>
    /// A vote is 1 when its quantity is above zero on the side's candles (the bears': the candles
    /// turned upside down, so every "above" is a strict "below"); undefined, or still warming up, is
    /// no vote. Moving averages follow Pine's <c>ta.ema</c>: an EMA is seeded with the SMA of its first
    /// <c>n</c> inputs. Scores start once the gap's change is defined (bar 30).
    /// </para>
    ///
    /// <para>
    /// <b>How close it is, measured.</b> DBSI's exact number on 96.0–99.5% of bars of the ten veto sets
    /// (98.5% pooled, 37,032 numbers), within 2 on 99.6–100%, typical error about 0.3 points; on the
    /// three held-out sets 97.9% (Microsoft 15-minute), 98.5% and 97.0% (Bitstamp 5- and 1-minute). The
    /// hourly chart's last seven weeks: 98.3% exact (the 2026-10-25 model, gen8: 59.6%). The digitised
    /// charts, read by eye and noisier: the twenty 88.1% exact, 94.1% within 2 (gen8 55.1%, 80.4%); the
    /// fifteen livestream charts 85.0%, 90.1% (gen8 48.5%, 78.1%; the linear model before it 20.4%, 67.1%). <c>DbsiStyleGoldenTests</c> replays the
    /// digitised numbers; <c>DbsiScoreModelParityTests</c> holds every bar to the study's file.
    /// </para>
    ///
    /// <para>
    /// <b>The known shortfall.</b> About 1 bar in 65 is not DBSI's exact number, bulls and bears alike (1.6% and 1.4% on the ten veto sets). The misses left are
    /// timing: raw Stoch RSI pinned at 0 or 100 for a bar or two, where a one-bar difference in when it
    /// lets go moves a few points, and the dead-run step that holds or drops on it. A first brute-force
    /// search of other indicators found no missing input; a later one over moving-average crosses found
    /// one, the bears' EMA(5)/EMA(11) cross (2026-10-29), which replaced five fitted triggers, and the
    /// hidden arm on off-run bars then replaced about ten more (and, for bulls, a flag after a deep run that
    /// ended high, 2026-10-29). So some
    /// of the remaining corrections may still stand in for an input not yet found. On the fifteen
    /// livestream charts it prints 17 or more on 46 of DBSI's
    /// 48 such readings and on 2 where DBSI does not (gen8 38 and 6). Many corrections are small and
    /// narrow, found on Bitcoin first; they held on gold, stocks and Ethereum, but a market unlike all
    /// of these may see more misses.
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
    /// option is on (the default — on the two Heikin-Ashi charts in the first study, Heikin-Ashi input
    /// fitted better). Turn the option off on a candle chart to match DBSI there.
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
                    "line green above price and red below. Reverse-engineered from DBSI's printed numbers as whole-number votes " +
                    "plus what DBSI remembers of the WaveTrend cross. On markets it was checked on (Bitcoin, Ethereum, gold, the Dow, " +
                    "the S&P 500, Apple; hourly to daily) it prints DBSI's exact number about 9 times in 10 and lands within 2 " +
                    "points about 98 times in 100. Not DBSI.",
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
                    new() { IsAdvanced = true, Name = ParamHeikinAshi, DisplayName = "Compute on Heikin-Ashi candles", DataType = typeof(bool),
                            DefaultValue = true,
                            Description = "Default ON. Scores and trend are computed from Heikin-Ashi candles, as DBSI is on the Heikin-Ashi charts it is used on (and the fit is better there). Off = regular candles, which is what DBSI uses on a regular chart." },
                    new() { IsAdvanced = true, Name = ParamLabelBars, DisplayName = "Label the newest N bars", DataType = typeof(int),
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
                    // For strategies and alerts: +1 bulls ahead, −1 bears ahead, 0 a tie. Left out and
                    // undrawn (a ±1 line on the price pane is not a price).
                    new() { Name = CompControl, DisplayName = "Control", DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.Signal, Causality = ComponentCausality.Causal,
                            DefaultIsEnabled = false, DefaultIsDrawn = false, DefaultIsMuted = true,
                            DefaultColorHex = LiveHex },
                    // The close the trend is computed from and coloured against (Heikin-Ashi when the
                    // option is on). Left out and undrawn (still computed: the trend reads it); it exists so the trend line can be two-coloured.
                    new() { Name = CompTrendSource, DisplayName = "Trend source close", DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.Signal, Causality = ComponentCausality.Causal,
                            DefaultIsEnabled = false, DefaultIsDrawn = false, DefaultIsMuted = true,
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
            double[] BullsTakeControl, double[] BearsTakeControl, double[] LongEntry, double[] ShortEntry);

        /// <summary>
        /// The two scores (<see cref="DbsiScoreModel"/>) on the candles chosen by "Compute on
        /// Heikin-Ashi candles", who leads, the narrated events and the trend line.
        /// </summary>
        internal static Result Compute(ReadOnlySpan<Ohlcv> data, Settings s)
        {
            int n = data.Length;
            double[] o = new double[n], h = new double[n], l = new double[n], c = new double[n];
            if (s.HeikinAshi && n > 0)
            {
                var ha = ChartMath.CalculateHeikinAshi(data.ToArray().ToList());
                for (int i = 0; i < n; i++) { o[i] = ha[i].Open; h[i] = ha[i].High; l[i] = ha[i].Low; c[i] = ha[i].Close; }
            }
            else
            {
                for (int i = 0; i < n; i++) { o[i] = data[i].Open; h[i] = data[i].High; l[i] = data[i].Low; c[i] = data[i].Close; }
            }

            var (bull, bear) = DbsiScoreModel.Score(o, h, l, c);
            var control = Nan(n);
            for (int i = 0; i < n; i++)
                if (!double.IsNaN(bull[i])) control[i] = Math.Sign(bull[i] - bear[i]);

            var (bullsTake, bearsTake, longEntry, shortEntry) = Events(bull, bear, control, s.LongEntry, s.ShortEntry);

            var trend = Ema(c, s.TrendLength);
            return new Result(bull, bear, control, trend, (double[])c.Clone(), bullsTake, bearsTake, longEntry, shortEntry);
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

        /// <summary>
        /// EMA as the study (and Pine's <c>ta.ema</c>) seed it: the mean of the first <paramref name="len"/>
        /// inputs from the first defined one, then α = 2/(len+1). A NaN input after the seed (a flat
        /// window) blanks that bar and leaves the average where it was.
        /// </summary>
        internal static double[] Ema(double[] x, int len) => Smoothed(x, len, 2.0 / (len + 1));

        private static double[] Smoothed(double[] x, int len, double a)
        {
            int n = x.Length;
            var r = Nan(n);
            int start = Array.FindIndex(x, v => !double.IsNaN(v));
            if (start < 0 || start + len > n) return r;
            double sum = 0; int cnt = 0;
            for (int i = start; i < start + len; i++) if (!double.IsNaN(x[i])) { sum += x[i]; cnt++; }
            if (cnt == 0) return r;
            double e = sum / cnt;
            r[start + len - 1] = e;
            for (int i = start + len; i < n; i++)
            {
                if (double.IsNaN(x[i]) || double.IsInfinity(x[i])) continue;
                e = a * x[i] + (1 - a) * e;
                r[i] = e;
            }
            return r;
        }

        private static double[] Nan(int n) { var r = new double[n]; Array.Fill(r, double.NaN); return r; }

        private static int GetInt(Dictionary<string, object>? p, string k, int def) =>
            p != null && p.TryGetValue(k, out var v) && v != null ? (int)Math.Round(Convert.ToDouble(v, CultureInfo.InvariantCulture)) : def;
    }
}
