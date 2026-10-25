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
    /// <b>The score</b> (2026-10-24, <c>dbsi7/gen5.py</c>; side-specific run 2026-10-25, <c>gen6.py</c>;
    /// the bears' broken-run close-up 3, <c>gen7.py</c>; the stoch-40 and Stoch RSI votes and the Q
    /// state, <c>dbsi8/gen8.py</c>): whole numbers only, no fitted decimals. For each side, on the
    /// candles chosen by "Compute on Heikin-Ashi candles":
    /// </para>
    /// <code>
    ///   score = max(−5, base + run + Q)
    ///   base  = −5 + 2 per base vote (the gap vote 1): up to +8
    ///   run   = in a WaveTrend run:    its level: bulls 7 if the cross was deep, bears 2; else 1
    ///         + while the run is alive: bulls 6, bears 7, + bars since the cross (up to 10)
    ///         + while it is not alive (broken, or no run): bars the gap has widened in a row − 1 (0 to 9)
    ///             + in a broken run, on a close beyond the previous close: bulls 2, bears 3
    ///   Q     = 4 while the Q state is on (never in a run): off a run, 2+ bars after it ended, a close
    ///           beyond the previous close with the 11-bar stochastic (SMA3) on the side's low half
    ///           turns it on; it stays on while each close is beyond the close 2 bars back
    /// </code>
    /// <para>
    /// The highest score is 31 for bulls and 27 for bears (every vote, a deep cross alive for ten
    /// bars); off a run it is 21 (every vote, a ten-bar streak and the Q state). DBSI's own highest is
    /// 27, on the digitised charts and on 7,127 hourly bars, and the model's is 27 on the charts. The <b>base votes</b> (<see cref="BaseVotes"/>, <see cref="BasePoints"/>)
    /// are DBSI's score on bars with nothing to remember, found with Market Cipher's own script on a
    /// Dow Jones daily chart: moving its Start date one bar at a time leaves a bar where only the
    /// base counts, and an integer program matched the bulls' number there. The <b>run</b>
    /// (<see cref="WaveTrendRun"/>) is what DBSI was found to remember from the WaveTrend (9, 12, 3)
    /// cross toward the side: a level, higher after a deep cross (wt1 past −60 against the side), and
    /// about 6 more plus one a bar while the gap between wt1 and its 3-bar average widens every bar.
    /// Once the gap narrows the run is no longer alive, and from then on, and between runs, the
    /// score follows the gap's widening streak and the close instead. The <b>Q state</b>
    /// (<see cref="Model.QState"/>) is the value DBSI's Start grid showed setting on a close up and
    /// holding while the close stays beyond the close 2 back; its trigger was found on the hourly
    /// set's misses (2026-10-25): a close up while the side's short stochastic is in its low half, at
    /// least two bars after the side's run ended.
    /// </para>
    /// <para>
    /// <b>How the numbers were chosen.</b> Every setting is a small whole number, picked by trying
    /// them all, best by the share within 2 plus the exact share. The streak start on the twenty
    /// digitised charts below (897 DBSI numbers read by eye, 2026-10-17:
    /// Coinbase BTCUSD on 1D/1W/3D/2D, the vendor's own 15-minute Heikin-Ashi image, Bitstamp,
    /// Ethereum and XRP from 10 minutes to 6 hours, seven of them Heikin-Ashi). Each side's run
    /// settings, the broken run's close-up points and the off-run close-up on an hourly set of DBSI's
    /// own numbers (Bitcoin 1-hour Heikin-Ashi, 2026-01-01 to 09-06, 5,826 bars read from the vendor's
    /// script, kept out of this repository): its bull runs after a deep cross sat about 6 points
    /// higher than the bears', and a free fit of the broken-run close-up gave bears 2.9 points to the
    /// bulls' 1.8 (refitting both sides together moved only the bears' 2 to 3).
    /// The hourly set's misses then gave three more (<c>dbsi8/</c>, 2026-10-25): the third base vote's
    /// stochastic is 40 bars, not 21; the sixth vote is Stoch RSI (RSI 12, its 12-bar stochastic,
    /// K its 3-bar SMA) rising, a flat K voting by its side of 50, in place of the previous close
    /// above the close before it; and the Q state, worth 4, which made the off-run close-up (bars
    /// since the run, up to 4) worse than nothing, so it is gone. Each setting one step either way
    /// scores lower on the twenty. The RSI's length is not sharp (11 to 13 score within a point; the
    /// usual 14 is worse on every set) and neither is the Q state's stochastic length (10 to 12) or
    /// midline (45 to 55).
    /// </para>
    /// <para>
    /// A vote is 1 when its quantity is above zero on the side's candles (the bears': the candles
    /// turned upside down, so every "above" is a strict "below"); undefined, or still warming up, is
    /// no vote. Moving averages follow Pine's <c>ta.ema</c>: an EMA is seeded with the SMA of its first
    /// <c>n</c> inputs. Scores start once the gap's change is defined (bar 30).
    /// </para>
    ///
    /// <para>
    /// <b>How close it is, measured</b> (<c>DbsiStyleGoldenTests</c> replays all of it). On the
    /// fifteen livestream charts (2026-10-23, Coinbase/Bitstamp Bitcoin and Ethereum, 1 minute to
    /// 1 day) that no choice saw: DBSI's exact number 48.5% of the time, within ±1 58.7%, within ±2
    /// 78.1%, typical error 2.80 points; before the stoch-40, Stoch RSI and Q changes (gen7) 36.8%,
    /// 50.0%, 77.1%, 2.78; before the hourly choices (gen5) 31.2%, 45.2%, 71.5%, 3.07. On the last
    /// seven weeks of the hourly chart (2026-09-07 to 10-24, held out from every choice, 2 × 1,151
    /// numbers): 59.6%, 71.7%, 85.6%, 1.81; gen7 44.0%, 57.5%, 81.9%, 2.11; gen5 36.4%, 51.6%, 75.9%,
    /// 2.53. The linear model it replaced (twelve weighted votes, eight weighted base votes, a streak,
    /// a stretch and weighted run terms, fitted on the same twenty): 20.4%, 50.5%, 67.1%, 3.06 on the
    /// fifteen. On the twenty: 55.1%, 66.7%, 80.4%, 2.16 (gen7: 40.2%, 54.4%, 77.4%, 2.29; gen5: 34.6%,
    /// 47.4%, 74.8%, 2.58).
    /// </para>
    ///
    /// <para>
    /// <b>The known shortfall.</b> About half the readings are not DBSI's exact number, and on the
    /// livestream charts about one in five is 3 or more off; the typical error there did not fall
    /// (2.80, gen7 2.78), so the misses left are a little bigger. It prints 17 or more on 38 of the 48
    /// such readings there (gen7 35, gen5 21), and where DBSI does not on 6 bars (gen5 3; the linear
    /// model 27 and 10). About half the hourly misses are a side in a run while the other side is off
    /// a run and not in the Q state; a broken run is often one short. Why bulls and bears differ after
    /// a deep cross, and in a broken run, is not known; the hourly set is one market in one
    /// rising-and-falling year. Direction and rough size are trustworthy; exact numbers and single big
    /// readings are not.
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

        // The integer model (scratchpad dbsi7/gen5.py, 2026-10-24; run settings gen6.py/gen7.py, 2026-10-25; the stoch-40 and Stoch RSI votes and the Q state dbsi8/gen8.py, 2026-10-25): base points and streak start chosen on the twenty digitised charts, the rest on the hourly set.
        /// <summary>Each base vote's points, in the order of <see cref="BaseVotes"/>.</summary>
        internal static readonly int[] BasePoints = { 2, 2, 2, 2, 1, 2, 2 };
        /// <summary>The score with no vote and nothing remembered, and its floor.</summary>
        internal const int Floor = -5;
        /// <summary>
        /// One side's run settings (<see cref="Model.RunPoints"/>): its level in a run, by the cross
        /// (<see cref="WaveTrendRun"/>), and while the run is alive these alive points plus one per bar
        /// since the cross, up to <see cref="RunCountCap"/>; once the run is broken (not alive), these
        /// broken close-up points on a close beyond the previous close. The two sides differ (2026-10-25,
        /// chosen on an hourly set of DBSI's own numbers): a deep bull cross is worth far more than a
        /// deep bear one, and a bear close beyond the previous close in a broken run one more than a bull's.
        /// </summary>
        internal sealed record RunSettings(int RunLevel, int DeepRunLevel, int AlivePoints, int BrokenCloseUpPoints);
        internal static readonly RunSettings BullRun = new(RunLevel: 1, DeepRunLevel: 7, AlivePoints: 6, BrokenCloseUpPoints: 2);
        internal static readonly RunSettings BearRun = new(RunLevel: 1, DeepRunLevel: 2, AlivePoints: 7, BrokenCloseUpPoints: 3);
        internal const int RunCountCap = 10;
        /// <summary>While the run is not alive: the gap's widening streak counts from its bar after this many (<see cref="Model.NotAlivePoints"/>).</summary>
        internal const int StreakStart = 1;
        /// <summary>
        /// The Q state's points (<see cref="Model.QState"/>): off a run, a close beyond the previous close
        /// with the <see cref="QStochLength"/>-bar stochastic (SMA3) on the side's far side of
        /// <see cref="QBelow"/>, at least <see cref="QAfterRun"/> bars after the side's last run ended,
        /// starts it; it holds while each close stays beyond the close 2 bars back, and a run ends it.
        /// </summary>
        internal const int QPoints = 4;
        internal const int QAfterRun = 2;
        internal const int QStochLength = 11;
        internal const double QBelow = 50.0;
        /// <summary>The third base vote's stochastic length (SMA2 of the stochastic of close).</summary>
        internal const int StochLength = 40;
        /// <summary>The sixth base vote, Stoch RSI: RSI of this length, its stochastic over this many bars, K its SMA of <see cref="StochRsiSmoothing"/>.</summary>
        internal const int StochRsiLength = 12;
        internal const int StochRsiSmoothing = 3;
        /// <summary>A Stoch RSI K change this small is no change (a flat K is a tie, decided by the side of 50; this absorbs the SMA's rounding).</summary>
        internal const double StochRsiTie = 1e-9;
        /// <summary>The money flow's averaging length (Cipher B's 60) for the seventh base vote.</summary>
        internal const int MoneyFlowLength = 60;
        /// <summary>wt1 beyond this against the side at the cross makes the cross deep.</summary>
        internal const double DeepLevel = 60.0;

        /// <summary>
        /// The score's settings as one value, so a variant can be scored the same way (the golden
        /// tests try every setting one step either way). <see cref="Shipped"/> is the constants above.
        /// </summary>
        internal sealed record Model(int[] BasePoints, RunSettings Bull, RunSettings Bear, int StreakStart,
            int QPoints, int QAfterRun, int StochLength, int StochRsiSmoothing)
        {
            /// <summary>The run's points on bar <paramref name="i"/>: its level, and while alive the alive points and the count.</summary>
            public static int RunPoints(RunSettings r, WaveRun w, int i) =>
                w.Run[i] == 0 ? 0 : (w.Deep[i] == 1 ? r.DeepRunLevel : r.RunLevel) + (w.Alive[i] == 1 ? r.AlivePoints + (int)w.Count[i] : 0);

            /// <summary>
            /// The points of a side whose run is not alive: one per bar of the gap's widening streak
            /// beyond <see cref="StreakStart"/> (the streak counted up to <see cref="RunCountCap"/>), and
            /// in a broken run on a close beyond the previous close the side's <see cref="RunSettings.BrokenCloseUpPoints"/>
            /// (off a run the close counts only through the Q state).
            /// </summary>
            public int NotAlivePoints(RunSettings r, WaveRun w, int streak, double closeUp, int i) =>
                Math.Max(Math.Min(streak, RunCountCap) - StreakStart, 0)
                + (closeUp == 1 && w.Run[i] == 1 ? r.BrokenCloseUpPoints : 0);

            /// <summary>
            /// One side's Q state, bar by bar (1 or 0): in a run it is off; off a run, a close beyond the
            /// previous close (<paramref name="closeUp"/>) with the side's <see cref="QStochLength"/>-bar
            /// stochastic below <see cref="QBelow"/> (<paramref name="qStoch"/>, on the side's candles) at
            /// least <see cref="QAfterRun"/> bars after its last run ended turns it on; it stays on while
            /// the close stays beyond the close 2 bars back (<paramref name="hold"/>, the first base vote).
            /// </summary>
            public double[] QState(double[] closeUp, double[] qStoch, double[] hold, WaveRun w)
            {
                int n = closeUp.Length;
                var q = new double[n];
                bool on = false;
                for (int i = 0; i < n; i++)
                {
                    if (w.Run[i] == 1) on = false;
                    else if (closeUp[i] == 1 && qStoch[i] < QBelow && w.SinceRun[i] >= QAfterRun) on = true;
                    else if (on && hold[i] != 1) on = false;
                    q[i] = on ? 1 : 0;
                }
                return q;
            }

            /// <summary>One side's score on bar <paramref name="i"/> (<paramref name="side"/> 1 bulls, −1 bears): max(−5, base + run + Q).</summary>
            public int Score(int side, double[][] baseVotes, WaveRun wave, int streak, double closeUp, double q, int i)
            {
                int score = Floor;
                for (int j = 0; j < BasePoints.Length; j++) if (baseVotes[j][i] == 1) score += BasePoints[j];
                var r = side > 0 ? Bull : Bear;
                score += RunPoints(r, wave, i);
                if (wave.Alive[i] == 0) score += NotAlivePoints(r, wave, streak, closeUp, i);
                if (q == 1) score += QPoints;
                return Math.Max(Floor, score);
            }
        }

        internal static readonly Model Shipped = new(BasePoints, BullRun, BearRun, StreakStart, QPoints, QAfterRun, StochLength, StochRsiSmoothing);

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
                    "plus what DBSI remembers of the WaveTrend cross. On charts it was not built from, it prints DBSI's exact " +
                    "number about 1 time in 2 and lands within 2 points about 8 times in 10; it prints 17 or more less often " +
                    "than DBSI does. Not DBSI.",
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
        /// What the score is built from, bar by bar, on the candles it reads: each side's base votes,
        /// WaveTrend run, gap streak (bars in a row the gap has widened, uncapped), close up (1 or
        /// 0) and Q state (<see cref="Model.QState"/>), the closes, and the gap itself (wt1 − its 3-bar
        /// SMA), which says when scores start.
        /// </summary>
        internal sealed record Terms(double[][] BullBase, double[][] BearBase, WaveRun BullWave, WaveRun BearWave,
            int[] BullStreak, int[] BearStreak, double[] BullCloseUp, double[] BearCloseUp, double[] BullQ, double[] BearQ,
            double[] Close, double[] Gap);

        /// <summary>
        /// The seven base votes of one side, each 1 or 0 per bar, on the candles given (the bears'
        /// are these on the candles turned upside down: −open, −low as high, −high as low, −close,
        /// so every "above" becomes a strict "below"). In order: close above the close 2 bars back;
        /// the 4-bar EMA of close above itself 2 bars back; the 40-bar stochastic (SMA2) rising;
        /// WaveTrend wt1 (9, 12) rising; wt1's gap over its 3-bar SMA widening; Stoch RSI (RSI 12, its
        /// 12-bar stochastic, K = SMA3) rising, or flat and above 50; Cipher B's money flow (the SMA60 of hlc3 less its 5-bar mean over
        /// the 5-bar mean size of that, on the candles given) rising — for the
        /// bears, falling. Undefined compares as no vote.
        /// </summary>
        internal static double[][] BaseVotes(double[] o, double[] h, double[] l, double[] c, Model? model = null)
        {
            var m = model ?? Shipped;
            var e4 = Ema(c, 4);
            var k = Sma(Stochastic(c, h, l, m.StochLength), 2);
            var wt1 = WaveTrend1(h, l, c);
            var gap = Sub(wt1, Sma(wt1, 3));
            // Cipher B's money flow, unscaled, on these candles (Heikin-Ashi when the score reads
            // Heikin-Ashi candles), as Cipher B's is on such a chart.
            var mf = CipherBProvider.McbMoneyFlowShare(h, l, c, MoneyFlowLength);
            return new[]
            {
                Above(c, Shift(c, 2)), Above(e4, Shift(e4, 2)), Above(k, Shift(k, 1)), Above(wt1, Shift(wt1, 1)),
                Above(gap, Shift(gap, 1)), StochRsiRising(c, m.StochRsiSmoothing), Above(mf, Shift(mf, 1)),
            };
        }

        /// <summary>
        /// The Stoch RSI vote: K (the SMA of the <see cref="StochRsiLength"/>-bar stochastic of RSI
        /// <see cref="StochRsiLength"/>) rising; where K has not moved (by more than
        /// <see cref="StochRsiTie"/>) or its change is undefined, K above 50. On the bears' upside-down
        /// candles the RSI is 100 less the bulls', so this is K falling, or flat below 50.
        /// </summary>
        private static double[] StochRsiRising(double[] c, int smoothing)
        {
            var rsi = Rsi(c, StochRsiLength);
            var k = Sma(Stochastic(rsi, rsi, rsi, StochRsiLength), smoothing);
            var r = new double[c.Length];
            for (int i = 0; i < c.Length; i++)
            {
                double d = i > 0 ? k[i] - k[i - 1] : double.NaN;
                bool tie = double.IsNaN(d) || Math.Abs(d) <= StochRsiTie;
                r[i] = (tie ? k[i] > 50 : d > 0) ? 1 : 0;   // NaN compares false
            }
            return r;
        }

        /// <summary>Wilder's RSI, its averages seeded as Pine's <c>ta.rma</c> (the mean of the first <paramref name="len"/> changes).</summary>
        private static double[] Rsi(double[] c, int len)
        {
            int n = c.Length;
            var up = Nan(n); var dn = Nan(n);
            for (int i = 1; i < n; i++) { double d = c[i] - c[i - 1]; up[i] = Math.Max(d, 0); dn[i] = Math.Max(-d, 0); }
            var au = Smoothed(up, len, 1.0 / len); var ad = Smoothed(dn, len, 1.0 / len);
            var r = new double[n];
            for (int i = 0; i < n; i++) r[i] = 100 - 100 / (1 + au[i] / ad[i]);
            return r;
        }

        /// <summary>The position of the gap-widening vote in <see cref="BaseVotes"/>.</summary>
        private const int GapVote = 4;
        /// <summary>The base vote that holds the Q state on: the close beyond the close 2 bars back.</summary>
        private const int HoldVote = 0;

        /// <summary>WaveTrend wt1: hlc3, channel 9, average 12.</summary>
        private static double[] WaveTrend1(double[] h, double[] l, double[] c)
        {
            int n = c.Length;
            var hlc3 = new double[n];
            for (int i = 0; i < n; i++) hlc3[i] = (h[i] + l[i] + c[i]) / 3.0;
            var esa = Ema(hlc3, 9);
            var dd = Ema(Abs(Sub(hlc3, esa)), 9);
            var ci = new double[n];
            for (int i = 0; i < n; i++) ci[i] = (hlc3[i] - esa[i]) / (0.015 * dd[i]);
            return Ema(ci, 12);
        }

        private static double[] Above(double[] a, double[] b)
        {
            var r = new double[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[i] - b[i] > 0 ? 1 : 0;   // NaN compares false
            return r;
        }

        private static double[] Shift(double[] x, int k)
        {
            var r = Nan(x.Length);
            for (int i = k; i < x.Length; i++) r[i] = x[i - k];
            return r;
        }

        /// <summary>
        /// One side's WaveTrend run, bar by bar (1 or 0, Count in bars). A run is the stretch where
        /// wt1 is beyond its 3-bar average on the side's side (above for bulls); it starts on the
        /// cross bar. <c>Deep</c>: wt1 was past −60 on the side's far side at the cross (below −60
        /// for bulls, above +60 for bears). <c>Alive</c>: the gap between wt1 and its average has
        /// widened on every bar since the cross; once it narrows it stays off for the rest of the
        /// run. <c>Count</c>: bars since the cross while alive, up to <see cref="RunCountCap"/>, else
        /// 0. <c>SinceRun</c>: on bars off a run, the bars since the side's last run ended (0 on the
        /// first bar out), else 0. A run already going on the first bar is not counted (its cross is
        /// not in the data): it is no run, and its end starts no count.
        /// </summary>
        internal sealed record WaveRun(double[] Run, double[] Deep, double[] Alive, double[] Count, double[] SinceRun);

        internal static WaveRun WaveTrendRun(double[] wt1, double[] wt2, int side)
        {
            int n = wt1.Length;
            var r = new WaveRun(new double[n], new double[n], new double[n], new double[n], new double[n]);
            int cross = -1, lastEnd = -1; bool alive = false;
            for (int i = 1; i < n; i++)
            {
                double gap = side * (wt1[i] - wt2[i]), before = side * (wt1[i - 1] - wt2[i - 1]);
                if (!(gap > 0))
                {
                    if (cross >= 0) lastEnd = i;
                    cross = -1;
                    r.SinceRun[i] = lastEnd >= 0 ? i - lastEnd : 0;
                    continue;
                }
                if (!(before > 0)) { cross = i; alive = true; }
                if (cross < 0) continue;
                int age = i - cross;
                if (age > 0 && alive && gap <= before) alive = false;
                r.Run[i] = 1;
                r.Deep[i] = side * wt1[cross] < -DeepLevel ? 1 : 0;
                r.Alive[i] = alive ? 1 : 0;
                r.Count[i] = alive ? Math.Min(age, RunCountCap) : 0;
            }
            return r;
        }

        internal static Terms ScoringTerms(ReadOnlySpan<Ohlcv> data, bool heikinAshi, Model? model = null)
        {
            var m = model ?? Shipped;
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

            var wt1 = WaveTrend1(h, l, c);
            var wt2 = Sma(wt1, 3);
            double[] Neg(double[] x) => x.Select(v => -v).ToArray();
            var bullBase = BaseVotes(o, h, l, c, m);
            var bearBase = BaseVotes(Neg(o), Neg(l), Neg(h), Neg(c), m);
            int[] Streak(double[] vote)
            {
                var s = new int[n];
                for (int i = 0; i < n; i++) s[i] = vote[i] == 1 ? (i > 0 ? s[i - 1] : 0) + 1 : 0;
                return s;
            }
            double[] bullUp = new double[n], bearUp = new double[n];
            for (int i = 1; i < n; i++) { bullUp[i] = c[i] > c[i - 1] ? 1 : 0; bearUp[i] = c[i] < c[i - 1] ? 1 : 0; }
            // The Q state's stochastic, on each side's candles; undefined counts as the midline (no trigger).
            double[] QStoch(double[] cc, double[] hh, double[] ll) =>
                Sma(Stochastic(cc, hh, ll, QStochLength), 3).Select(v => double.IsNaN(v) ? QBelow : v).ToArray();
            WaveRun bullWave = WaveTrendRun(wt1, wt2, 1), bearWave = WaveTrendRun(wt1, wt2, -1);
            var bullQ = m.QState(bullUp, QStoch(c, h, l), bullBase[HoldVote], bullWave);
            var bearQ = m.QState(bearUp, QStoch(Neg(c), Neg(l), Neg(h)), bearBase[HoldVote], bearWave);
            return new Terms(bullBase, bearBase, bullWave, bearWave,
                Streak(bullBase[GapVote]), Streak(bearBase[GapVote]), bullUp, bearUp, bullQ, bearQ, c, Sub(wt1, wt2));
        }

        internal static Result Compute(ReadOnlySpan<Ohlcv> data, Settings s, Model? model = null)
        {
            var m = model ?? Shipped;
            int n = data.Length;
            var t = ScoringTerms(data, s.HeikinAshi, m);
            var c = t.Close;

            var bull = Nan(n); var bear = Nan(n); var control = Nan(n);
            for (int i = 1; i < n; i++)
            {
                if (double.IsNaN(t.Gap[i]) || double.IsNaN(t.Gap[i - 1])) continue;   // still warming up
                bull[i] = m.Score(1, t.BullBase, t.BullWave, t.BullStreak[i], t.BullCloseUp[i], t.BullQ[i], i);
                bear[i] = m.Score(-1, t.BearBase, t.BearWave, t.BearStreak[i], t.BearCloseUp[i], t.BearQ[i], i);
                control[i] = Math.Sign(bull[i] - bear[i]);
            }

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
