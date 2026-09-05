using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public class IndicatorContextAnalyzer : IIndicatorContextAnalyzer
    {
        private readonly Dictionary<string, IndicatorContextDefinition> _defs =
            new(StringComparer.OrdinalIgnoreCase);

        public IndicatorContextAnalyzer()
        {
            // Built-in definitions
            RegisterDefinition(new IndicatorContextDefinition
            {
                IndicatorCode = "RSI", ComponentName = "RSI",
                OverboughtThreshold = 70, OversoldThreshold = 30,
                TrendLookbackBars = 3,
                SpeechTemplate = "{value:F6}, {trend}, {zone}"
            });
            RegisterDefinition(new IndicatorContextDefinition
            {
                IndicatorCode = "MACD", ComponentName = "Histogram",
                CrossoverComponentA = "MACD", CrossoverComponentB = "Signal",
                TrendLookbackBars = 3,
                SpeechTemplate = "{value:F6}, {trend}, {zone}"
            });
            // ── FOUR DEFINITIONS USED TO SIT HERE AND BIND TO NOTHING ──────────────
            //
            // "BB|Upper" and "BB|Lower" (Bollinger's components are "UpperBand"/"LowerBand"),
            // "STOCH|K" (Stochastic's are "Oscillator"/"Signal", renamed years ago to match
            // Skender's result type) and "CIPHER_B|Trigger Wave" (no such component). Keys that
            // match no component are dead configuration that LOOKS like coverage, which is worse
            // than no entry at all: it is why nobody noticed that Stochastic could not narrate.
            // `NarrationRouteContractTests.EveryRegisteredOscillatorDefinition_NamesAComponentThatExists`
            // fails if one comes back.
            //
            // They are deleted rather than re-keyed because the generic routes cover all three
            // now: Bollinger is a Main-pane overlay and gets price-cross narration, and
            // Stochastic declares Overbought/Oversold levels and gets level-cross narration.
            // A definition is for an indicator whose thresholds need their OWN wording.
            //
            // Vortex is the opposite case and gets one: VI+ crossing VI- IS the indicator, it
            // declares no levels, and nothing else could ever have spoken for it.
            RegisterDefinition(new IndicatorContextDefinition
            {
                IndicatorCode = "Vortex", ComponentName = "Pvi",
                CrossoverComponentA = "Pvi", CrossoverComponentB = "Nvi",
                TrendLookbackBars = 3,
                SpeechTemplate = "{value:F2}, {trend}"
            });
            RegisterDefinition(new IndicatorContextDefinition
            {
                IndicatorCode = "ATR", ComponentName = "ATR",
                TrendLookbackBars = 3,
                SpeechTemplate = "{value:F6}, {trend}"
            });
            // ── Cipher B — four components each narrated independently ────────────
            // Wave Trend: primary oscillator; OB/OS at ±53 matching MC-B reference levels.
            RegisterDefinition(new IndicatorContextDefinition
            {
                IndicatorCode = "CIPHER_B", ComponentName = "Wave Trend",
                OverboughtThreshold = 53, OversoldThreshold = -53,
                TrendLookbackBars = 3,
                SpeechTemplate = "{value:F1}, {zone}"
            });
            // Anchor Wave: slow macro WT (5× period); same ±53 thresholds.
            // Speech uses "Macro wave" prefix so the user can distinguish setup vs entry layer.
            RegisterDefinition(new IndicatorContextDefinition
            {
                IndicatorCode = "CIPHER_B", ComponentName = "Anchor Wave",
                OverboughtThreshold = 53, OversoldThreshold = -53,
                TrendLookbackBars = 5,
                SpeechTemplate = "{value:F1}, {zone}"
            });
            // (The "Trigger Wave" definition that sat here named a component Cipher B does not
            // have — see the note above.)
            // Money Flow Wave: raw values −100..−60, neutral at −80.
            // OB = −70 (>50% buying pressure), OS = −90 (>50% selling pressure).
            RegisterDefinition(new IndicatorContextDefinition
            {
                IndicatorCode = "CIPHER_B", ComponentName = "Money Flow Wave",
                OverboughtThreshold = -70, OversoldThreshold = -90,
                TrendLookbackBars = 3,
                SpeechTemplate = "{value:F1}"
            });
        }

        public void RegisterDefinition(IndicatorContextDefinition definition)
        {
            string key = $"{definition.IndicatorCode.ToUpperInvariant()}|{definition.ComponentName.ToUpperInvariant()}";
            _defs[key] = definition;
        }

        /// <inheritdoc />
        public bool HasZoneThresholds(string indicatorCode, string componentName)
            => _defs.TryGetValue($"{indicatorCode.ToUpperInvariant()}|{componentName.ToUpperInvariant()}", out var def)
               && (def.OverboughtThreshold.HasValue || def.OversoldThreshold.HasValue);

        public IndicatorContext? Analyze(ChartSeries series, WorkspaceState state)
            => AnalyzeAll(series, state).FirstOrDefault();

        public IEnumerable<IndicatorContext> AnalyzeAll(ChartSeries series, WorkspaceState state)
        {
            if (series == null || series.Components.Count == 0) yield break;

            bool anyMatched = false;
            foreach (var kv in _defs)
            {
                if (!kv.Value.IndicatorCode.Equals(series.IndicatorCode, StringComparison.OrdinalIgnoreCase))
                    continue;
                var comp = series.Components.FirstOrDefault(c =>
                    c.Name.Equals(kv.Value.ComponentName, StringComparison.OrdinalIgnoreCase));
                if (comp == null) continue;

                var ctx = AnalyzeComponent(series, state, comp, kv.Value);
                if (ctx != null) { anyMatched = true; yield return ctx; }
            }

            // Fallback: first visible component when no registered definition matched.
            if (!anyMatched)
            {
                var comp = series.Components.FirstOrDefault(c => c.IsVisible && !c.IsMuted);
                if (comp != null)
                {
                    string defKey = $"{series.IndicatorCode.ToUpperInvariant()}|{comp.Name.ToUpperInvariant()}";
                    _defs.TryGetValue(defKey, out var def);
                    var ctx = AnalyzeComponent(series, state, comp, def);
                    if (ctx != null) yield return ctx;
                }
            }
        }

        private IndicatorContext? AnalyzeComponent(ChartSeries series, WorkspaceState state,
            ComponentConfig comp, IndicatorContextDefinition? def)
        {
            var data = series.GetComponentData(comp.Name);
            int dataIndex = state.CurrentDataIndex;
            if (dataIndex < 0 || dataIndex >= (data?.Length ?? 0)) return null;
            double currentValue = data![dataIndex];
            if (double.IsNaN(currentValue)) return null;

            double? prevValue = dataIndex > 0 && data.Length > dataIndex - 1
                ? (double.IsNaN(data[dataIndex - 1]) ? null : data[dataIndex - 1])
                : null;

            int lookback = def?.TrendLookbackBars ?? 3;
            var (trend, trendBars) = DetectTrend(data, dataIndex, lookback);
            ZoneStatus zone = DetermineZone(currentValue, prevValue, def, series, state, dataIndex);

            CrossoverStatus crossover = CrossoverStatus.None;
            if (def?.CrossoverComponentA != null && def.CrossoverComponentB != null)
                crossover = DetectCrossover(series, def.CrossoverComponentA, def.CrossoverComponentB, dataIndex);

            string hint = BuildNarrativeHint(zone, crossover, trend);

            return new IndicatorContext
            {
                IndicatorCode  = series.IndicatorCode,
                ComponentName  = comp.Name,
                CurrentValue   = currentValue,
                PreviousValue  = prevValue,
                Trend          = trend,
                TrendBars      = trendBars,
                Zone           = zone,
                Crossover      = crossover,
                NarrativeHint  = hint
            };
        }

        private static (TrendDirection trend, int bars) DetectTrend(double[] data, int dataIndex, int lookback)
        {
            if (data == null || dataIndex < 1) return (TrendDirection.Flat, 0);

            int start = Math.Max(0, dataIndex - lookback);
            var window = new List<double>();
            for (int i = start; i <= dataIndex; i++)
            {
                if (data.Length > i && !double.IsNaN(data[i]))
                    window.Add(data[i]);
            }

            if (window.Count < 2) return (TrendDirection.Flat, 0);

            bool allRising  = true, allFalling = true;
            for (int i = 1; i < window.Count; i++)
            {
                if (window[i] <= window[i - 1]) allRising  = false;
                if (window[i] >= window[i - 1]) allFalling = false;
            }

            if (allRising)  return (TrendDirection.Rising,  window.Count - 1);
            if (allFalling) return (TrendDirection.Falling, window.Count - 1);
            return (TrendDirection.Flat, 0);
        }

        private static ZoneStatus DetermineZone(double value, double? prevValue,
            IndicatorContextDefinition? def, ChartSeries series, WorkspaceState state, int dataIndex)
        {
            if (def == null) return ZoneStatus.Normal;

            if (def.OverboughtThreshold.HasValue && value >= def.OverboughtThreshold.Value)
                return ZoneStatus.Overbought;
            if (def.OversoldThreshold.HasValue && value <= def.OversoldThreshold.Value)
                return ZoneStatus.Oversold;

            // For Bollinger: check component name
            if (def.ComponentName.Equals("Upper", StringComparison.OrdinalIgnoreCase))
                return ZoneStatus.AtUpperBand;
            if (def.ComponentName.Equals("Lower", StringComparison.OrdinalIgnoreCase))
                return ZoneStatus.AtLowerBand;

            return ZoneStatus.Normal;
        }

        private static CrossoverStatus DetectCrossover(ChartSeries series, string nameA, string nameB, int dataIndex)
        {
            var compA = series.Components.FirstOrDefault(c =>
                c.Name.Equals(nameA, StringComparison.OrdinalIgnoreCase));
            var compB = series.Components.FirstOrDefault(c =>
                c.Name.Equals(nameB, StringComparison.OrdinalIgnoreCase));

            if (compA == null || compB == null || dataIndex < 1) return CrossoverStatus.None;

            var dataA = series.GetComponentData(compA.Name);
            var dataB = series.GetComponentData(compB.Name);

            if (dataA == null || dataB == null) return CrossoverStatus.None;
            if (dataA.Length <= dataIndex || dataB.Length <= dataIndex) return CrossoverStatus.None;

            double aNow  = dataA[dataIndex];
            double bNow  = dataB[dataIndex];
            double aPrev = dataA[dataIndex - 1];
            double bPrev = dataB[dataIndex - 1];

            if (double.IsNaN(aNow) || double.IsNaN(bNow) || double.IsNaN(aPrev) || double.IsNaN(bPrev))
                return CrossoverStatus.None;

            if (aPrev < bPrev && aNow >= bNow) return CrossoverStatus.BullishCrossover;
            if (aPrev > bPrev && aNow <= bNow) return CrossoverStatus.BearishCrossover;

            return CrossoverStatus.None;
        }

        private static string BuildNarrativeHint(ZoneStatus zone, CrossoverStatus crossover, TrendDirection trend)
        {
            if (crossover == CrossoverStatus.BullishCrossover) return "bullish crossover detected";
            if (crossover == CrossoverStatus.BearishCrossover) return "bearish crossover detected";

            return zone switch
            {
                ZoneStatus.Overbought  => "approaching overbought territory",
                ZoneStatus.Oversold    => "approaching oversold territory",
                ZoneStatus.AtUpperBand => "at upper band - potential resistance",
                ZoneStatus.AtLowerBand => "at lower band - potential support",
                ZoneStatus.AbovePOC    => "above point of control",
                ZoneStatus.BelowPOC    => "below point of control",
                _ => trend == TrendDirection.Rising  ? "trending higher"
                   : trend == TrendDirection.Falling ? "trending lower"
                   : ""
            };
        }
    }
}
