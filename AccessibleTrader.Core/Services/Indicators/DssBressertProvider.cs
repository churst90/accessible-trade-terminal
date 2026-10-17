using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// DSS Bressert — Walter Bressert's Double Smoothed Stochastic. A stochastic of a smoothed
    /// stochastic, smoothed again: 0 to 100, with 80 and 20 as its lines, like Lane's Stochastic,
    /// but with the jitter taken out twice so it turns less often and later.
    ///
    /// <para>
    /// <b>The maths, exactly.</b> With <c>P</c> the stochastic period, <c>E</c> the EMA period and
    /// <c>T</c> the trigger period:
    /// </para>
    /// <code>
    ///   stoch1  = 100 · (close − lowest(low, P)) / (highest(high, P) − lowest(low, P))
    ///   MIT     = EMA(stoch1, E)
    ///   stoch2  = 100 · (MIT − lowest(MIT, P)) / (highest(MIT, P) − lowest(MIT, P))
    ///   DSS     = EMA(stoch2, E)
    ///   Trigger = EMA(DSS, T)
    /// </code>
    /// <para>
    /// Every EMA is <c>x + α·(input − x)</c> with α = 2/(n+1). Every Bressert implementation found
    /// agrees on the first four lines: MetaQuotes' <c>DSS Bressert.mq4</c> writes the smoothing as
    /// <c>smooth_coefficient * (MIT - MitBuffer[i+1]) + MitBuffer[i+1]</c> with
    /// <c>smooth_coefficient = 2.0 / (1.0 + EMA_period)</c>; the TradingView scripts write
    /// <c>ta.ema(ta.stoch(close, high, low, PDS), EMAlen)</c> then
    /// <c>ta.ema(ta.stoch(xPreCalc, xPreCalc, xPreCalc, PDS), EMAlen)</c>; NinjaTrader 7's built-in,
    /// ProRealTime, TC2000 and Infront say the same in their own notation. Note what the second
    /// stochastic is taken OF: the smoothed line's own highest and lowest over P bars, not price's.
    /// That is the whole difference from a slow stochastic — and the reason this is NOT William
    /// Blau's "Double Smoothed Stochastic" (TASC, January 1991), which smooths the numerator and
    /// the denominator of ONE stochastic with two EMAs each. Same name, different indicator; the
    /// TASC article people cite for "DSS" is Blau's, and NinjaTrader's own docs credit its
    /// Bressert-shaped code to Blau.
    /// </para>
    ///
    /// <para>
    /// <b>Defaults: P = 13, E = 8, T = 5.</b> There is no single published default. The camps:
    /// 13/8 in the MetaQuotes MT4 and MT5 code base (<c>EMA_period=8; Stochastic_period=13</c>) and
    /// the ports of it (cTrader, TradingView "DSS Bressert by MaxCap"); 10/9 with a 5-bar trigger
    /// EMA in the TradingView family (Astral Vision, Roul_Charts); 10/3 in NinjaTrader 7,
    /// ProRealTime and Infront, which cites Bressert's own <i>Profit Trader</i> manual (2003) — so
    /// 10/3 is probably what Bressert himself used, though no primary source could be read to
    /// confirm it. 13/8 is the default because it is the pair most widely shipped under the name
    /// "DSS Bressert"; the other two are a settings change away.
    /// </para>
    ///
    /// <para>
    /// <b>The second line.</b> There is no standard one. The MetaQuotes versions draw MIT — the
    /// singly smoothed stochastic — beside the DSS; TradingView and Fat Tails' NinjaTrader version
    /// draw an EMA of the DSS (5 on TradingView); cTrader a WMA of it. The TRIGGER is the visible
    /// second line here because a line the DSS crosses is what both camps trade off, and EMA 5 is
    /// the only one with a published default. MIT is computed and declared but hidden, for anyone
    /// reading a MetaTrader chart beside this one.
    /// </para>
    ///
    /// <para>
    /// <b>Where the implementations differ, and what this one does.</b> They agree on the formula
    /// and differ only in how a run begins and what a flat window does. Neither is a setting in any
    /// of them — each just does one thing — so neither is a setting here.
    /// </para>
    /// <list type="bullet">
    ///   <item><b>The seed.</b> MT4 starts both EMAs from 0 (an empty buffer slot), MT5 from 50,
    ///         cTrader from 0; the Pine reference <c>pine_ema</c>, MaxCap's port and NinjaTrader's
    ///         EMA from the first input. This starts from the first input: 0 and 50 are values the
    ///         market never produced, and the second stochastic AMPLIFIES a seed transient — on BTC
    ///         daily the MT5 seed is still 7.6 points off on the first published bar and takes about
    ///         a hundred bars to wash out. After that every implementation agrees to 1e-12.</item>
    ///   <item><b>A flat window</b> (highest = lowest: a halted market, a run of identical prints).
    ///         MetaQuotes, ProRealTime and the Pine scripts do not guard it: MQL divides by zero and
    ///         Pine's x/0 is na. Pine's <c>ta.ema</c> ignores na inputs, and that is what this does —
    ///         the stochastic is UNDEFINED for that bar and the EMA holds its last value. A flat
    ///         window says nothing about where the close sits in a range, because there is no range;
    ///         inventing a 50 would move the line on no information. NinjaTrader 7 differs: it holds
    ///         the previous RAW stochastic and keeps feeding it to the EMA, so its line keeps drifting
    ///         towards that value. A hold never propagates a NaN, and it does not count as an input
    ///         towards the warm-up. The same rule applies at the second stage, where MIT itself can
    ///         be flat. A bar with a missing (NaN) high, low or close is treated the same way.</item>
    ///   <item><b>Warm-up.</b> The first <c>2P + E − 3</c> bars are blank — 31 at the defaults.
    ///         Pine paints from bar <c>2P − 2</c>, when the second stochastic first has a window; the
    ///         extra <c>E − 1</c> are the rule <see cref="AccessibleTrader.Sdk.Indicators.IndicatorMath.Ema"/>
    ///         applies to every EMA in the app: an EMA says nothing until it has had E inputs. The
    ///         blanking does not change the maths underneath, so the first value spoken is the Pine
    ///         value for the same bars.</item>
    /// </list>
    ///
    /// <para>
    /// Sources: MetaQuotes "DSS Bressert" MT4 (mql5.com/en/code/8310) and MT5 (mql5.com/en/code/789);
    /// TradingView scripts FtRMJcRd (Astral Vision), v3qBH25e (Roul_Charts), tzNzzTbP (MaxCap);
    /// NinjaTrader 7 <c>DoubleStochastics</c> (source reposted at nexusfi.com/showthread.php?p=468623);
    /// TC2000 help, "Double Smoothed Stochastic Bressert"; Infront, "Double Stochastics"; and for the
    /// contrast, Blau via store.traders.com (v11-c01) and GeniusTrader's DSS.pm. Walter Bressert,
    /// <i>The Power of Oscillator/Cycle Combinations</i>, is the original and could not be read.
    /// </para>
    /// </summary>
    public sealed class DssBressertProvider : IIndicatorProvider
    {
        public const string Code        = "DSS_BRESSERT";
        public const string CompDss     = "DSS";
        public const string CompTrigger = "Trigger";
        public const string CompMit     = "MIT";

        public const string ParamStoch   = "stochPeriods";
        public const string ParamEma     = "emaPeriods";
        public const string ParamTrigger = "triggerPeriods";

        public const int DefaultStochPeriods   = 13;
        public const int DefaultEmaPeriods     = 8;
        public const int DefaultTriggerPeriods = 5;

        public string Name => "DSS Bressert";

        private static readonly List<IndicatorMetadata> _indicators = new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "DSS Bressert", Category = "Oscillators",
                DefaultPane = "Pane_" + Code, RangeMin = 0, RangeMax = 100,
                Causality = ComponentCausality.Causal,
                Description =
                    "Walter Bressert's Double Smoothed Stochastic: a stochastic of an EMA-smoothed " +
                    "stochastic, smoothed again, 0 to 100 with 80 and 20 as its lines. Smoother and " +
                    "later than a plain Stochastic; a trigger line is an EMA of the DSS.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamStoch, DisplayName = "Stochastic Periods", DataType = typeof(int),
                            DefaultValue = DefaultStochPeriods, MinValue = 2, Step = 1,
                            Description = "Bars in both stochastic windows. 13 is the MetaTrader default; TradingView scripts and NinjaTrader use 10." },
                    new() { Name = ParamEma, DisplayName = "EMA Periods", DataType = typeof(int),
                            DefaultValue = DefaultEmaPeriods, MinValue = 1, Step = 1,
                            Description = "Length of both EMA smoothings. 8 is the MetaTrader default; TradingView scripts use 9, NinjaTrader 3." },
                    new() { Name = ParamTrigger, DisplayName = "Trigger Periods", DataType = typeof(int),
                            DefaultValue = DefaultTriggerPeriods, MinValue = 1, Step = 1,
                            Description = "Length of the trigger line, an EMA of the DSS. 5 in the TradingView scripts." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    // FIRST, and an Oscillator: the level-cross narrator speaks for one component
                    // per series and takes the first oscillator line it finds, so the DSS has to
                    // be the one it finds, not the trigger that lags it.
                    new() { Name = CompDss, DisplayName = "DSS", DisplayType = ComponentDisplayType.Oscillator,
                            DefaultColorHex = "#00B8D4", DefaultThickness = 1.5f,
                            DefaultTriggerBoundaryClick = true, DefaultNoiseAmount = 0f, DefaultReferenceLevel = 50.0,
                            SpeechTemplate = "{name}. {type}. {value:F2}. {zone}." },
                    new() { Name = CompTrigger, DisplayName = "Trigger", DisplayType = ComponentDisplayType.Oscillator,
                            DefaultColorHex = "#E65100",
                            DefaultNoiseAmount = 0f, DefaultReferenceLevel = 50.0,
                            SpeechTemplate = "{name}. {type}. {value:F2}." },
                    // MetaTrader's second line, hidden: see "The second line" above. Spoken as
                    // "Smoothed %K" — the stochastic after its first EMA — because "MIT" read
                    // aloud is a university.
                    new() { Name = CompMit, DisplayName = "Smoothed %K", DisplayType = ComponentDisplayType.Oscillator,
                            DefaultColorHex = "#7E57C2", IsVisible = false,
                            DefaultNoiseAmount = 0f, DefaultReferenceLevel = 50.0,
                            SpeechTemplate = "{name}. {type}. {value:F2}." },
                },
            },
        };

        public List<IndicatorMetadata> GetIndicators() => _indicators;

        // The Stochastic's own set, for the Stochastic's own reasons: 80/20 are the lines every
        // DSS implementation draws (MT4 indicator_level1 20 / level2 80, MT5 likewise, and the
        // TradingView scripts' Overbought/Oversold inputs), and the midline is what the 0 key
        // toggles on every 0–100 oscillator in the fleet. The clean line plus zone-only noise is
        // the bounded-oscillator sound contract — see SkenderBoundedOscillatorProvider.
        public List<LevelDescriptor> GetDefaultLevels(string code) =>
            !string.Equals(code, Code, StringComparison.OrdinalIgnoreCase)
                ? new List<LevelDescriptor>()
                : new List<LevelDescriptor>
                {
                    new("Overbought", 80.0, "#FF4444", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
                    new("Midpoint",   50.0, "#888888", DashStyle.Dot,  PlayEarcon: true, EarconVolume: 0.7f),
                    new("Oversold",   20.0, "#44BB44", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
                };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var (p, e, t) = Periods(parameters);
            var r = Compute(data, p, e, t);
            var dssSpan = buffer.GetComponentSpan(CompDss);
            var trgSpan = buffer.GetComponentSpan(CompTrigger);
            var mitSpan = buffer.GetComponentSpan(CompMit);
            for (int i = 0; i < data.Length; i++)
            {
                if (i < dssSpan.Length) dssSpan[i] = r.Dss[i];
                if (i < trgSpan.Length) trgSpan[i] = r.Trigger[i];
                if (i < mitSpan.Length) mitSpan[i] = r.Mit[i];
            }
        }

        /// <summary>
        /// A full recalculation. Both stochastic windows are 13 bars and both EMAs are recursions,
        /// so the whole series costs about what one Skender pass does; an incremental path would
        /// be a second implementation of the same maths, and a second implementation is a second
        /// place for the two to drift apart.
        /// </summary>
        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) =>
            Calculate(code, data, parameters, buffer);

        /// <summary>
        /// Bars of history the last value depends on, to within a part in a million: the two
        /// stochastic windows, plus enough bars for each EMA's seed to decay away
        /// ((1 − α)^n &lt; 1e-6 is about 6.9·(E+1)/2 bars).
        /// </summary>
        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            var (p, e, t) = Periods(parameters);
            int decay(int n) => (int)Math.Ceiling(7.0 * (n + 1) / 2.0);
            return 2 * p + 2 * decay(e) + decay(t);
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex) => null;

        /// <summary>
        /// The Stochastic's detail sentence, in the DSS's words: the zone, which way the line is
        /// going, the trigger, and a DSS/trigger cross on the bar it happened.
        /// </summary>
        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            calculatedResults.TryGetValue(CompDss, out var dss);
            calculatedResults.TryGetValue(CompTrigger, out var trg);
            if (dss == null || index < 0 || index >= dss.Length) return string.Empty;
            double v = dss[index];
            if (double.IsNaN(v)) return "Calculating...";

            string zone = v >= 80 ? "Overbought" : v <= 20 ? "Oversold" : "Neutral";
            string trend = string.Empty;
            if (index > 0 && !double.IsNaN(dss[index - 1]))
                trend = v > dss[index - 1] ? ", rising" : v < dss[index - 1] ? ", falling" : "";

            string trgPart = string.Empty;
            if (trg != null && index < trg.Length && !double.IsNaN(trg[index]))
            {
                double tv = trg[index];
                string cross = string.Empty;
                if (index > 0 && !double.IsNaN(dss[index - 1]) && !double.IsNaN(trg[index - 1]))
                {
                    if (dss[index - 1] < trg[index - 1] && v >= tv) cross = " DSS crossed above trigger.";
                    else if (dss[index - 1] > trg[index - 1] && v <= tv) cross = " DSS crossed below trigger.";
                }
                trgPart = $" Trigger {tv:F1}.{cross}";
            }
            return $"{zone} at DSS {v:F1}{trend}.{trgPart}";
        }

        // ── The maths ───────────────────────────────────────────────────────────────────────

        internal readonly record struct Result(double[] Mit, double[] Dss, double[] Trigger);

        /// <summary>
        /// The whole chain, chronological. Each stage is the plain recurrence from the doc comment;
        /// the only non-obvious parts are the seed (first input), the flat-window hold, and the
        /// blanking, all three explained there.
        /// </summary>
        internal static Result Compute(ReadOnlySpan<Ohlcv> data, int p, int e, int t)
        {
            int n = data.Length;
            var mit = new double[n];
            var mitOut = new double[n];
            var dss = new double[n];
            var trigger = new double[n];
            Array.Fill(mit, double.NaN);
            Array.Fill(mitOut, double.NaN);
            Array.Fill(dss, double.NaN);
            Array.Fill(trigger, double.NaN);

            double ae = 2.0 / (e + 1.0), at = 2.0 / (t + 1.0);

            // Stage 1: stochastic of price, EMA-smoothed into MIT. mitInputs[i] counts the real
            // (non-flat) stochastic values the EMA had absorbed by bar i.
            var mitInputs = new int[n];
            double m = double.NaN;
            int mCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (i >= p - 1)
                {
                    double hh = double.NegativeInfinity, ll = double.PositiveInfinity;
                    bool gap = false;
                    for (int j = i - p + 1; j <= i; j++)
                    {
                        double h = data[j].High, l = data[j].Low;
                        if (double.IsNaN(h) || double.IsNaN(l)) { gap = true; break; }
                        if (h > hh) hh = h;
                        if (l < ll) ll = l;
                    }
                    double c = data[i].Close;
                    if (!gap && !double.IsNaN(c) && hh > ll)
                    {
                        double x = 100.0 * (c - ll) / (hh - ll);
                        m = double.IsNaN(m) ? x : m + ae * (x - m);
                        mCount++;
                    }
                }
                mit[i] = m;
                mitInputs[i] = mCount;
                if (mCount >= e) mitOut[i] = m;
            }

            // Stage 2: stochastic of MIT over MIT's own range, EMA-smoothed into the DSS, then the
            // trigger EMA of that. Published once the window's oldest MIT and the DSS itself have
            // each had E real inputs.
            double d = double.NaN, tr = double.NaN;
            int dCount = 0, tCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (i >= p - 1 && !double.IsNaN(mit[i - p + 1]))
                {
                    double hh = double.NegativeInfinity, ll = double.PositiveInfinity;
                    for (int j = i - p + 1; j <= i; j++)
                    {
                        if (mit[j] > hh) hh = mit[j];
                        if (mit[j] < ll) ll = mit[j];
                    }
                    if (hh > ll)
                    {
                        double x = 100.0 * (mit[i] - ll) / (hh - ll);
                        d = double.IsNaN(d) ? x : d + ae * (x - d);
                        dCount++;
                    }
                }
                if (!double.IsNaN(d))
                {
                    tr = double.IsNaN(tr) ? d : tr + at * (d - tr);
                    tCount++;
                }

                if (dCount >= e && i >= p - 1 && mitInputs[i - p + 1] >= e)
                {
                    dss[i] = d;
                    if (tCount >= t) trigger[i] = tr;
                }
            }

            return new Result(mitOut, dss, trigger);
        }

        private static (int P, int E, int T) Periods(Dictionary<string, object>? parameters) =>
            (Math.Max(2, GetInt(parameters, ParamStoch, DefaultStochPeriods)),
             Math.Max(1, GetInt(parameters, ParamEma, DefaultEmaPeriods)),
             Math.Max(1, GetInt(parameters, ParamTrigger, DefaultTriggerPeriods)));

        private static int GetInt(Dictionary<string, object>? p, string k, int def) =>
            p != null && p.TryGetValue(k, out var v) && v != null ? (int)Convert.ToDouble(v) : def;
    }
}
