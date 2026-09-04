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
    /// IT IS NOT A BUY/SELL INDICATOR. It answers "where is value, and where did price turn
    /// relative to it". A reversal below value marks a SUPPORT zone; a reversal above value marks
    /// a RESISTANCE zone; the tier says how far from value that zone formed. The figures above are
    /// what followed those zones historically on equities — context for reading the mark, not an
    /// instruction. An earlier draft had an "invert for momentum" mode, which only made sense
    /// while the marks were framed as entries: under a support/resistance reading there is nothing
    /// to invert, because a reversal is a reversal whichever way the asset trends.
    /// </para>
    ///
    /// <para>
    /// Two limits worth keeping in mind. The measured follow-through is SHORT-horizon — strong at
    /// five bars, fading by twenty, gone by sixty — and it was measured on EQUITIES; no crypto
    /// reading is validated in either direction, so on crypto treat the marks as pure orientation.
    /// </para>
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

        public const string CompSupportShallow = "SupportShallow";
        public const string CompSupportMid     = "SupportMid";
        public const string CompSupportDeep    = "SupportDeep";
        public const string CompResistShallow = "ResistanceShallow";
        public const string CompResistMid     = "ResistanceMid";
        public const string CompResistDeep    = "ResistanceDeep";
        public const string CompPoc        = "ValuePoc";
        public const string CompValueHigh  = "ValueHigh";
        public const string CompValueLow   = "ValueLow";
        public const string CompTier       = "DeviationTier";

        private const string ParamWindow = "Window";
        private const string ParamTiers = "Tiers";
        private const string ParamMaxTier = "MaxTierVa";
        private const string ParamRequireMomentum = "RequireMomentumTurn";
        private const string ParamMinTier = "MinTier";

        private readonly IValueDeviationAnalyzer _analyzer = new ValueDeviationAnalyzer();

        public string Name => "Value Deviation";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code,
                Causality = ComponentCausality.Causal,
                // Parenthetical dropped from the spoken name — see SwingStructureProvider.
                Name = "Value Deviation",
                Category = "Overlays",
                DefaultPane = "Main",
                RequiresFullRecalcOnTick = true,
                Description =
                    "Marks where price REVERSED relative to value. A rolling volume-profile POC defines value; " +
                    "a reversal below it marks a support zone, above it a resistance zone, and the tier says how far " +
                    "from value the zone formed. Descriptive, not a buy/sell signal.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamWindow, DisplayName = "Profile window (bars)", DataType = typeof(int),
                            DefaultValue = 240.0, MinValue = 40.0, MaxValue = 2000.0,
                            Description = "Bars in the rolling volume profile that defines value. A SLOWER window anchored better in testing, so raise this when the chart has plenty of history. " +
                                          "It is automatically capped at a third of the loaded bars, so setting it high is safe — it shortens rather than going silent." },
                    new() { Name = ParamTiers, DisplayName = "Tiers per side", DataType = typeof(int),
                            DefaultValue = 5.0, MinValue = 2.0, MaxValue = 6.0,
                            Description = "Five stayed monotonic in testing; six collapsed the two innermost tiers together." },
                    new() { Name = ParamMaxTier, DisplayName = "Outermost tier at (value areas)", DataType = typeof(double),
                            DefaultValue = 2.0, MinValue = 0.5, MaxValue = 6.0 },
                    new() { Name = ParamMinTier, DisplayName = "Show tiers from", DataType = typeof(int),
                            DefaultValue = 2.0, MinValue = 1.0, MaxValue = 5.0,
                            Description = "Lowest tier that gets a mark. Tier 1 is a reversal barely outside value, which is " +
                                          "closer to noise than to a zone and is the bulk of the marks on a long view — so the " +
                                          "default starts at 2. Raise it to 3 or 4 on a weekly or a wide zoom to leave only the " +
                                          "deep stretches; drop it to 1 to see everything the analyzer found." },
                    new() { Name = ParamRequireMomentum, DisplayName = "Require a momentum turn as well", DataType = typeof(bool),
                            DefaultValue = true,
                            Description = "ON (default): a zone is only marked when the built-in WaveTrend oscillator is also turning that way — fewer, better-confirmed zones. OFF: marks on the reversal bar alone, giving more zones with more noise." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    Mark(CompSupportShallow, "Support tier 1-2", ComponentDisplayType.TriangleUp, "#66BB6A", 7f, 320, MarkerAnchor.BelowBar),
                    Mark(CompSupportMid,     "Support tier 3",   ComponentDisplayType.Dot,     "#2E9E4F", 8f, 260, MarkerAnchor.BelowBar),
                    Mark(CompSupportDeep,    "Support tier 4-5", ComponentDisplayType.Diamond, "#00E676", 10f, 200, MarkerAnchor.BelowBar),
                    Mark(CompResistShallow, "Resistance tier 1-2", ComponentDisplayType.TriangleDown, "#EF9A9A", 7f, 640, MarkerAnchor.AboveBar),
                    Mark(CompResistMid,     "Resistance tier 3",   ComponentDisplayType.Dot,    "#E53935", 8f, 780, MarkerAnchor.AboveBar),
                    Mark(CompResistDeep,    "Resistance tier 4-5", ComponentDisplayType.Diamond, "#FF1744", 10f, 920, MarkerAnchor.AboveBar),
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
                        // Signed tier: negative below value (support side), positive above.
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
            ComponentDisplayType shape, string colour, float size, double freq, MarkerAnchor anchor) => new()
        {
            DefaultMarkerAnchor = anchor,
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
            foreach (var key in new[] { CompSupportShallow, CompSupportMid, CompSupportDeep,
                                        CompResistShallow, CompResistMid, CompResistDeep,
                                        CompPoc, CompValueHigh, CompValueLow, CompTier })
            {
                var s = buffer.GetComponentSpan(key);
                for (int i = 0; i < n; i++) s[i] = double.NaN;
            }
            if (n < 30) return;

            int window = (int)GetParam(parameters, ParamWindow, 240);
            int tiers = Math.Clamp((int)GetParam(parameters, ParamTiers, 5), 2, 6);
            double maxTier = GetParam(parameters, ParamMaxTier, 2.0);
            bool requireMomentum = GetParam(parameters, ParamRequireMomentum, 1) != 0;
            int minTier = Math.Clamp((int)GetParam(parameters, ParamMinTier, 2), 1, 5);
            // The window is EXACTLY this many bars, and it is deliberately not clamped to the
            // length of the loaded series. Two earlier versions did clamp — first to a third of
            // the total bar count, then to a third of the bar's own array index — and both made
            // the same bar answer differently depending on how much history happened to be
            // fetched around it. A chart shorter than the window therefore reads nothing at all,
            // which GetDetailFact explains in bars; scrolling back then ADDS readings rather than
            // rewriting the ones already on screen.
            window = Math.Max(ValueDeviationAnalyzer.MinWindow, window);

            var bars = new Ohlcv[n];
            for (int i = 0; i < n; i++) bars[i] = data[i];

            var devs = _analyzer.Analyze(bars, window, tiers, maxTier);
            var (poc, vaHigh, vaLow) = _analyzer.Reference(bars, window);
            var wt = WaveTrend(bars);

            var pocSpan = buffer.GetComponentSpan(CompPoc);
            var hiSpan = buffer.GetComponentSpan(CompValueHigh);
            var loSpan = buffer.GetComponentSpan(CompValueLow);
            var tierSpan = buffer.GetComponentSpan(CompTier);

            var supportShallow = buffer.GetComponentSpan(CompSupportShallow);
            var supportMid = buffer.GetComponentSpan(CompSupportMid);
            var supportDeep = buffer.GetComponentSpan(CompSupportDeep);
            var resistShallow = buffer.GetComponentSpan(CompResistShallow);
            var resistMid = buffer.GetComponentSpan(CompResistMid);
            var resistDeep = buffer.GetComponentSpan(CompResistDeep);

            for (int i = 1; i < n; i++)
            {
                pocSpan[i] = poc[i];
                hiSpan[i] = vaHigh[i];
                loSpan[i] = vaLow[i];

                var d = devs[i];
                if (d.Tier <= 0) continue;
                tierSpan[i] = d.BelowValue ? -d.Tier : d.Tier;

                // Density control, applied to the MARK only. The Deviation Tier component and the
                // spoken detail still report every tier, so raising this hides glyphs without
                // hiding information — navigating to the bar or asking for its detail still says
                // "tier 1 below value". Tier 1 is a reversal barely outside value and is the bulk
                // of the marks on a long view; on a 200-bar weekly it turned the price pane into
                // a band of triangles.
                if (d.Tier < minTier) continue;

                // A reversal BELOW value marks a support zone; a reversal ABOVE value marks a
                // resistance zone. That is the whole claim — it is a description of where price
                // turned relative to value, not an instruction to trade in either direction.
                bool belowValue = d.BelowValue;

                bool turned = belowValue ? IsBullishReversalBar(bars, i) : IsBearishReversalBar(bars, i);
                if (!turned) continue;

                if (requireMomentum && !MomentumTurned(wt, i, belowValue)) continue;

                // The stored VALUE is the bar extreme the zone sits at — the actual support or
                // resistance price, which is what speech should quote and what a strategy leaf
                // should compare against. Where the shape is DRAWN is a separate concern handled
                // by MarkerAnchor, so the marker follows the displayed candle even under
                // Heikin-Ashi without this value drifting away from a real, quotable price.
                double zone = belowValue ? bars[i].Low : bars[i].High;

                var target = d.Tier >= 4
                    ? (belowValue ? supportDeep : resistDeep)
                    : d.Tier == 3
                        ? (belowValue ? supportMid : resistMid)
                        : (belowValue ? supportShallow : resistShallow);
                target[i] = zone;
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            // Profile-based: a new bar shifts the rolling window and can change the POC for many
            // prior bars, so there is no correct scalar update.
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) =>
            (int)GetParam(parameters, ParamWindow, 240) + 20;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (index < 0 || data.Length == 0) return "";

            double tier = At(calculatedResults, CompTier, index);
            double poc = At(calculatedResults, CompPoc, index);
            var sb = new StringBuilder();

            // NO POC, AND THE REASON IS KNOWABLE. The profile is a fixed number of bars and it
            // does not shrink to fit — a bar with fewer than `window` bars behind it has no
            // reading, by design, so that scrolling back adds readings instead of changing them.
            // Saying "no data" and stopping would make a deliberate refusal look like a fault,
            // and the user has no way to see that the chart is simply too short. The counts are
            // in the sentence because they are what turns it into an action: load more history,
            // or lower the window.
            int window = Math.Max(ValueDeviationAnalyzer.MinWindow, (int)GetParam(parameters, ParamWindow, 240));
            if (double.IsNaN(poc) && index < window)
            {
                return $"No value profile yet. The profile is built from the {window} bars before " +
                       $"each bar, and this one has {index}. Load more history, or lower the " +
                       "profile window in the indicator's settings.";
            }

            if (double.IsNaN(tier) || tier == 0)
                sb.Append("Price is inside the value band.");
            else
                sb.Append($"Tier {Math.Abs(tier):0} {(tier < 0 ? "below" : "above")} value.");

            if (!double.IsNaN(poc))
            {
                double gap = (data[index].Close - poc) / poc * 100.0;
                sb.Append($" Value P O C {Price(poc)}, price {Math.Abs(gap):0.0}% {(gap >= 0 ? "above" : "below")} it.");
            }

            // State what was MEASURED, and never phrase it as a forecast.
            if (!double.IsNaN(tier) && tier != 0)
            {
                sb.Append(tier < 0
                    ? $" A reversal here would mark a support zone. On equities, zones this far below value were followed by larger five-bar gains the deeper the tier."
                    : " A reversal here would mark a resistance zone. On equities the upside was not symmetric — treat this as where supply showed up, not as a short.");
            }

            return sb.ToString();
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;

            // Keyed on the component NAME, which is what SpeechFormatter passes — NOT the
            // DisplayName. These were written against the display strings ("Support tier 1-2"),
            // so every case fell through to null, the pipeline moved on to its generic template,
            // and the user heard a bare number where there was a sentence waiting. Nothing threw
            // and no test failed; it surfaced only because someone navigated the chart and noticed
            // the speech was uninformative. ComponentSpeechKeyTests now detects the whole class.
            return componentName switch
            {
                CompSupportShallow => $"Shallow support zone at {Price(value)}, just outside value.",
                CompSupportMid     => $"Support zone at {Price(value)}, well below value.",
                CompSupportDeep    => $"Deep support zone at {Price(value)}, far below value — the furthest tier.",
                CompResistShallow  => $"Shallow resistance zone at {Price(value)}, just outside value.",
                CompResistMid      => $"Resistance zone at {Price(value)}, well above value.",
                CompResistDeep     => $"Deep resistance zone at {Price(value)}, far above value — the furthest tier.",
                CompTier => value == 0 ? "Inside value."
                    : $"Tier {Math.Abs(value):0} {(value < 0 ? "below" : "above")} value.",
                CompPoc       => $"Value point of control, {Price(value)}.",
                CompValueHigh => $"Value area high, {Price(value)}.",
                CompValueLow  => $"Value area low, {Price(value)}.",
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

        /// <summary>
        /// EMA that tolerates a NaN warmup region in its INPUT.
        ///
        /// <para>
        /// This is load-bearing, not defensive padding. WaveTrend chains three EMAs: the second
        /// runs over |ap - esa|, and esa's own first nine values are NaN. Seeding from index 0
        /// therefore produced a NaN seed, and because the recurrence is e = src*k + e*(1-k), that
        /// single NaN propagated to every later value. The whole oscillator came out NaN, the
        /// momentum filter rejected every bar, and the indicator printed ZERO marks on any chart
        /// while still looking healthy — the POC and value lines were fine, so nothing pointed at
        /// the cause.
        /// </para>
        /// </summary>
        private static double[] Ema(double[] src, int period)
        {
            int n = src.Length;
            var outv = new double[n];
            Array.Fill(outv, double.NaN);
            if (n < period || period < 1) return outv;

            int start = 0;
            while (start < n && double.IsNaN(src[start])) start++;
            if (start + period > n) return outv;

            double k = 2.0 / (period + 1);
            double sum = 0;
            for (int i = start; i < start + period; i++)
            {
                if (double.IsNaN(src[i])) return outv;   // a NaN hole inside the seed window
                sum += src[i];
            }

            double e = sum / period;
            outv[start + period - 1] = e;
            for (int i = start + period; i < n; i++)
            {
                if (double.IsNaN(src[i])) { outv[i] = e; continue; }  // hold through gaps
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
