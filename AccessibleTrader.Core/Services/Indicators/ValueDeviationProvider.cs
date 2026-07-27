using System;
using System.Collections.Generic;
using System.Text;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Value Deviation — graded scale-in / scale-out marks based on how far price has stretched
    /// from established value, printed only on bars that actually turn.
    ///
    /// <para>
    /// SELF-CONTAINED BY DESIGN. It computes its own rolling volume profile and its own WaveTrend
    /// momentum internally. Nothing else has to be on the chart. Cipher B's math is EMBEDDED, not
    /// depended on — adding one indicator should not silently require four others.
    /// </para>
    ///
    /// <para>
    /// WHAT THE EVIDENCE SAYS, and it is the reason this indicator exists in this shape. Across
    /// 38 equity, sector, metal and bond series — 348,000 daily bars — five-day forward returns
    /// rise monotonically with distance below the POC:
    /// </para>
    /// <code>
    ///   tier 1  +0.25%      tier 4  +0.65%
    ///   tier 2  +0.33%      tier 5  +1.83%   (slow anchor, net of 5 bps)
    ///   tier 3  +0.47%
    /// </code>
    /// <para>
    /// Three caveats are baked into the design rather than left to the user to remember:
    /// </para>
    /// <list type="number">
    /// <item>The SELL side does not work on equities — every short tier was negative after costs.
    /// Sell marks are therefore framed as scale-OUT of an existing long, never as short entries.</item>
    /// <item>The effect is SHORT-horizon: strong at five bars, fading by twenty, gone by sixty.
    /// It is not a multi-month position thesis.</item>
    /// <item>It INVERTS by asset class. Crypto showed the opposite sign — momentum, not reversion
    /// — so <see cref="ParamInvert"/> flips the semantics and speech says which way it is reading.</item>
    /// </list>
    ///
    /// <para>
    /// Shapes carry tier and hue carries direction, deliberately kept orthogonal: three shapes map
    /// to three distinct earcons, and hue is never the only channel, which keeps it readable for
    /// colour-vision-deficient users too.
    /// </para>
    /// </summary>
    public sealed class ValueDeviationProvider : IIndicatorProvider
    {
        public const string Code = "VALUE_DEVIATION";

        public const string CompBuyShallow = "BuyShallow";
        public const string CompBuyMid     = "BuyMid";
        public const string CompBuyDeep    = "BuyDeep";
        public const string CompSellShallow = "TrimShallow";
        public const string CompSellMid     = "TrimMid";
        public const string CompSellDeep    = "TrimDeep";
        public const string CompPoc        = "ValuePoc";
        public const string CompValueHigh  = "ValueHigh";
        public const string CompValueLow   = "ValueLow";
        public const string CompTier       = "DeviationTier";

        private const string ParamWindow = "Window";
        private const string ParamTiers = "Tiers";
        private const string ParamMaxTier = "MaxTierVa";
        internal const string ParamInvert = "InvertForMomentum";
        private const string ParamRequireMomentum = "RequireMomentumTurn";

        private readonly IValueDeviationAnalyzer _analyzer = new ValueDeviationAnalyzer();

        public string Name => "Value Deviation";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code,
                Name = "Value Deviation (scale-in tiers)",
                Category = "Overlays",
                DefaultPane = "Main",
                RequiresFullRecalcOnTick = true,
                Description =
                    "Graded scale-in / scale-out marks by distance from a rolling volume-profile POC, " +
                    "printed on reversal bars. Measured edge rises with tier depth on equities over 5 bars; " +
                    "the sell side is scale-out only, and crypto inverts.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamWindow, DisplayName = "Profile window (bars)", DataType = typeof(int),
                            DefaultValue = 480.0, MinValue = 60.0, MaxValue = 2000.0,
                            Description = "Bars in the rolling volume profile. The slow anchor tested best; ~480 daily bars is about two years." },
                    new() { Name = ParamTiers, DisplayName = "Tiers per side", DataType = typeof(int),
                            DefaultValue = 5.0, MinValue = 2.0, MaxValue = 6.0,
                            Description = "Five stayed monotonic in testing; six collapsed the two innermost tiers together." },
                    new() { Name = ParamMaxTier, DisplayName = "Outermost tier at (value areas)", DataType = typeof(double),
                            DefaultValue = 2.0, MinValue = 0.5, MaxValue = 6.0 },
                    new() { Name = ParamRequireMomentum, DisplayName = "Require a momentum turn as well", DataType = typeof(bool),
                            DefaultValue = true,
                            Description = "ON (default): a mark only prints when the built-in WaveTrend oscillator is also turning that way — fewer, cleaner marks. OFF: prints on the reversal bar alone." },
                    new() { Name = ParamInvert, DisplayName = "Momentum market — flip the buy side", DataType = typeof(bool),
                            DefaultValue = false,
                            Description = "OFF (default) = MEAN-REVERTING market: buys print BELOW value, trims above. This is how stocks, sectors, metals and bonds measured. " +
                                          "ON = MOMENTUM market: buys print ABOVE value, trims below. This is how crypto measured — it ran the opposite way. " +
                                          "Leave OFF for equities; turn ON for BTC and other crypto." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    Mark(CompBuyShallow, "Buy tier 1-2", ComponentDisplayType.TriangleUp, "#66BB6A", 7f, 320),
                    Mark(CompBuyMid,     "Buy tier 3",   ComponentDisplayType.Dot,        "#2E9E4F", 8f, 260),
                    Mark(CompBuyDeep,    "Buy tier 4-5", ComponentDisplayType.Diamond,    "#00E676", 10f, 200),
                    Mark(CompSellShallow, "Trim tier 1-2", ComponentDisplayType.TriangleDown, "#EF9A9A", 7f, 640),
                    Mark(CompSellMid,     "Trim tier 3",   ComponentDisplayType.Dot,          "#E53935", 8f, 780),
                    Mark(CompSellDeep,    "Trim tier 4-5", ComponentDisplayType.Diamond,      "#FF1744", 10f, 920),
                    new()
                    {
                        Name = CompPoc, DisplayName = "Value POC",
                        DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Level,
                        DefaultColorHex = "#FFD54F", DefaultThickness = 1.5f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Value P O C {value:price}.",
                    },
                    new()
                    {
                        Name = CompValueHigh, DisplayName = "Value Area High",
                        DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Level,
                        IsVisible = false, DefaultColorHex = "#9E9E9E", DefaultThickness = 1f,
                        DefaultDashStyle = DashStyle.Dash,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Value area high {value:price}.",
                    },
                    new()
                    {
                        Name = CompValueLow, DisplayName = "Value Area Low",
                        DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Level,
                        IsVisible = false, DefaultColorHex = "#9E9E9E", DefaultThickness = 1f,
                        DefaultDashStyle = DashStyle.Dash,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Value area low {value:price}.",
                    },
                    new()
                    {
                        // Signed tier: negative below value (buy side), positive above.
                        Name = CompTier, DisplayName = "Deviation Tier",
                        DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Signal,
                        IsVisible = false, DefaultColorHex = "#B0BEC5", DefaultThickness = 1f,
                        DefaultReferenceLevel = 0.0,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Tier {value:F0}.",
                    },
                }
            }
        };

        private static IndicatorComponentMetadata Mark(string name, string display,
            ComponentDisplayType shape, string colour, float size, double freq) => new()
        {
            Name = name,
            DisplayName = display,
            DisplayType = shape,
            Role = ComponentRole.Signal,
            DefaultColorHex = colour,
            DefaultThickness = size,
            SubscribedLevelNames = Array.Empty<string>(),
            DefaultEnvelopeType = "Ping",
            DefaultBaseFrequency = freq,
            DefaultSignalSpeechTemplate = display + ".",
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int n = data.Length;
            // Span<double> cannot live in a Dictionary (ref struct), so clear each one inline.
            foreach (var key in new[] { CompBuyShallow, CompBuyMid, CompBuyDeep,
                                        CompSellShallow, CompSellMid, CompSellDeep,
                                        CompPoc, CompValueHigh, CompValueLow, CompTier })
            {
                var s = buffer.GetComponentSpan(key);
                for (int i = 0; i < n; i++) s[i] = double.NaN;
            }
            if (n < 30) return;

            int window = (int)GetParam(parameters, ParamWindow, 480);
            int tiers = Math.Clamp((int)GetParam(parameters, ParamTiers, 5), 2, 6);
            double maxTier = GetParam(parameters, ParamMaxTier, 2.0);
            bool invert = GetParam(parameters, ParamInvert, 0) != 0;
            bool requireMomentum = GetParam(parameters, ParamRequireMomentum, 1) != 0;
            window = Math.Clamp(window, 60, Math.Max(60, n - 10));

            var bars = new Ohlcv[n];
            for (int i = 0; i < n; i++) bars[i] = data[i];

            var devs = _analyzer.Analyze(bars, window, tiers, maxTier);
            var (poc, vaHigh, vaLow) = _analyzer.Reference(bars, window);
            var wt = WaveTrend(bars);

            var pocSpan = buffer.GetComponentSpan(CompPoc);
            var hiSpan = buffer.GetComponentSpan(CompValueHigh);
            var loSpan = buffer.GetComponentSpan(CompValueLow);
            var tierSpan = buffer.GetComponentSpan(CompTier);

            var buyShallow = buffer.GetComponentSpan(CompBuyShallow);
            var buyMid = buffer.GetComponentSpan(CompBuyMid);
            var buyDeep = buffer.GetComponentSpan(CompBuyDeep);
            var sellShallow = buffer.GetComponentSpan(CompSellShallow);
            var sellMid = buffer.GetComponentSpan(CompSellMid);
            var sellDeep = buffer.GetComponentSpan(CompSellDeep);

            for (int i = 1; i < n; i++)
            {
                pocSpan[i] = poc[i];
                hiSpan[i] = vaHigh[i];
                loSpan[i] = vaLow[i];

                var d = devs[i];
                if (d.Tier <= 0) continue;
                tierSpan[i] = d.BelowValue ? -d.Tier : d.Tier;

                // Which side of the trade this deviation implies. Normally stretched-below is the
                // buy; on an inverted (momentum) asset it is stretched-ABOVE that is bought.
                bool buySide = invert ? !d.BelowValue : d.BelowValue;

                bool turned = buySide ? IsBullishReversalBar(bars, i) : IsBearishReversalBar(bars, i);
                if (!turned) continue;

                if (requireMomentum && !MomentumTurned(wt, i, buySide)) continue;

                // Marks sit just outside the bar so they never obscure the candle body.
                double pad = (bars[i].High - bars[i].Low) * 0.35;
                double y = buySide ? bars[i].Low - pad : bars[i].High + pad;

                var target = d.Tier >= 4
                    ? (buySide ? buyDeep : sellDeep)
                    : d.Tier == 3
                        ? (buySide ? buyMid : sellMid)
                        : (buySide ? buyShallow : sellShallow);
                target[i] = y;
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            // Profile-based: a new bar shifts the rolling window and can change the POC for many
            // prior bars, so there is no correct scalar update.
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) =>
            (int)GetParam(parameters, ParamWindow, 480) + 20;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (index < 0 || data.Length == 0) return "";
            bool invert = GetParam(parameters, ParamInvert, 0) != 0;

            double tier = At(calculatedResults, CompTier, index);
            double poc = At(calculatedResults, CompPoc, index);
            var sb = new StringBuilder();

            if (double.IsNaN(tier) || tier == 0)
                sb.Append("Price is inside the value band.");
            else
                sb.Append($"Tier {Math.Abs(tier):0} {(tier < 0 ? "below" : "above")} value.");

            if (!double.IsNaN(poc))
            {
                double gap = (data[index].Close - poc) / poc * 100.0;
                sb.Append($" Value P O C {Price(poc)}, price {Math.Abs(gap):0.0}% {(gap >= 0 ? "above" : "below")} it.");
            }

            // The honest part: say what was measured, and never imply it is a prediction.
            if (!double.IsNaN(tier) && tier != 0)
            {
                bool buySide = invert ? tier > 0 : tier < 0;
                sb.Append(buySide
                    ? $" On equities, tier {Math.Abs(tier):0} below value historically returned more over the next five bars the deeper it went."
                    : " Trim side: on equities the short side did not pay after costs, so treat this as scaling out, not shorting.");
            }
            if (invert) sb.Append(" Inverted mode: this asset is being read as momentum, not mean reversion.");

            return sb.ToString();
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                "Buy tier 1-2" => "Shallow buy. Price just outside value.",
                "Buy tier 3" => "Medium buy. Price well below value.",
                "Buy tier 4-5" => "Deep buy. Price far below value — the strongest tier measured.",
                "Trim tier 1-2" => "Shallow trim.",
                "Trim tier 3" => "Medium trim.",
                "Trim tier 4-5" => "Deep trim. Price far above value.",
                "Deviation Tier" => value == 0 ? "Inside value."
                    : $"Tier {Math.Abs(value):0} {(value < 0 ? "below" : "above")} value.",
                _ => null
            };
        }

        // ── Triggers ──────────────────────────────────────────────────────────

        /// <summary>
        /// A bullish reversal bar: this bar undercut the previous low and still closed up and in
        /// the upper half of its range. That is the minimal, unambiguous "sellers tried and
        /// failed" shape, and it needs no pattern library to define.
        /// </summary>
        private static bool IsBullishReversalBar(IReadOnlyList<Ohlcv> b, int i)
        {
            if (i < 1) return false;
            double range = b[i].High - b[i].Low;
            if (range <= 0) return false;
            return b[i].Low < b[i - 1].Low
                && b[i].Close > b[i].Open
                && (b[i].Close - b[i].Low) / range > 0.5;
        }

        private static bool IsBearishReversalBar(IReadOnlyList<Ohlcv> b, int i)
        {
            if (i < 1) return false;
            double range = b[i].High - b[i].Low;
            if (range <= 0) return false;
            return b[i].High > b[i - 1].High
                && b[i].Close < b[i].Open
                && (b[i].High - b[i].Close) / range > 0.5;
        }

        /// <summary>WaveTrend turning with the mark — Cipher B's oscillator math, embedded.</summary>
        private static bool MomentumTurned(double[] wt, int i, bool buySide)
        {
            if (i < 2 || double.IsNaN(wt[i]) || double.IsNaN(wt[i - 1])) return false;
            return buySide ? wt[i] > wt[i - 1] : wt[i] < wt[i - 1];
        }

        /// <summary>
        /// Standard WaveTrend on HLC3 (channel 10, average 21) — the same construction Cipher B
        /// uses. Reimplemented here so this indicator has no dependency on Cipher B being present.
        /// </summary>
        private static double[] WaveTrend(IReadOnlyList<Ohlcv> bars, int channel = 10, int average = 21)
        {
            int n = bars.Count;
            var wt = new double[n];
            Array.Fill(wt, double.NaN);
            if (n < channel + average + 2) return wt;

            var ap = new double[n];
            for (int i = 0; i < n; i++) ap[i] = (bars[i].High + bars[i].Low + bars[i].Close) / 3.0;

            var esa = Ema(ap, channel);
            var dev = new double[n];
            for (int i = 0; i < n; i++) dev[i] = Math.Abs(ap[i] - esa[i]);
            var d = Ema(dev, channel);

            var ci = new double[n];
            for (int i = 0; i < n; i++)
                ci[i] = d[i] == 0 ? 0 : (ap[i] - esa[i]) / (0.015 * d[i]);

            return Ema(ci, average);
        }

        private static double[] Ema(double[] src, int period)
        {
            int n = src.Length;
            var outv = new double[n];
            Array.Fill(outv, double.NaN);
            if (n < period) return outv;

            double k = 2.0 / (period + 1);
            double sum = 0;
            for (int i = 0; i < period; i++) sum += src[i];
            double e = sum / period;
            outv[period - 1] = e;
            for (int i = period; i < n; i++)
            {
                e = src[i] * k + e * (1 - k);
                outv[i] = e;
            }
            return outv;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static double GetParam(Dictionary<string, object> p, string key, double fallback)
        {
            if (p != null && p.TryGetValue(key, out var raw) && raw != null)
            {
                if (raw is bool b) return b ? 1 : 0;
                if (double.TryParse(raw.ToString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                    return v;
            }
            return fallback;
        }

        private static double At(IReadOnlyDictionary<string, double[]> data, string key, int index) =>
            data.TryGetValue(key, out var arr) && index >= 0 && index < arr.Length ? arr[index] : double.NaN;

        private static string Price(double v) =>
            v >= 1000 ? v.ToString("N0") : v >= 1 ? v.ToString("0.##") : v.ToString("0.######");
    }
}
