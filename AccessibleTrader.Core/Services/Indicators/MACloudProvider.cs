using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Moving Average Cloud — a directional cloud between two moving averages of any type.
    ///
    /// The user sees one navigable component: the cloud itself. The underlying MAs are hidden
    /// data sources. The Cloud component stores signed width data: positive = bullish
    /// (fast above slow), negative = bearish (fast below slow).
    ///
    /// Supports: EMA, SMA, WMA, HMA, DEMA, TEMA for each line independently.
    /// Example: Bull Market Support Band = 20-week SMA + 21-week EMA.
    ///
    /// Parameters:
    ///   FastPeriod — fast MA period (default 9)
    ///   SlowPeriod — slow MA period (default 21)
    ///   FastType   — fast MA type: EMA, SMA, WMA, HMA, DEMA, TEMA (default EMA)
    ///   SlowType   — slow MA type: EMA, SMA, WMA, HMA, DEMA, TEMA (default EMA)
    /// </summary>
    public class MACloudProvider : IIndicatorProvider
    {
        public string Name => "Accessible.MACloud";

        // Internal data keys (__ prefix = not a component, skipped by buffer validator).
        // These hold the raw MA values for the visual CloudFillConfig rendering.
        public const string DataFastMA = "__FastMA";
        public const string DataSlowMA = "__SlowMA";
        public const string CompCloud  = "MA Cloud";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "MA_CLOUD",
                Causality = ComponentCausality.Causal,
                Name        = "MA Cloud",
                Category    = "Overlays",
                DefaultPane = "Main",
                Description = "Directional cloud between two moving averages. " +
                              "Supports EMA, SMA, WMA, HMA, DEMA, TEMA for each line independently. " +
                              "Green when bullish (fast above slow); red when bearish.",
                Parameters  = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "FastPeriod", DisplayName = "Fast Period", DataType = typeof(int), DefaultValue = 9,
                            MinValue = 2, MaxValue = 500, Step = 1,
                            Description = "Period for the fast moving average." },
                    new() { Name = "SlowPeriod", DisplayName = "Slow Period", DataType = typeof(int), DefaultValue = 21,
                            MinValue = 2, MaxValue = 500, Step = 1,
                            Description = "Period for the slow moving average." },
                    new() { Name = "FastType", DisplayName = "Fast MA Type", DataType = typeof(string), DefaultValue = "EMA",
                            Description = "Moving average type for the fast line: EMA, SMA, WMA, HMA, DEMA, or TEMA." },
                    new() { Name = "SlowType", DisplayName = "Slow MA Type", DataType = typeof(string), DefaultValue = "EMA",
                            Description = "Moving average type for the slow line: EMA, SMA, WMA, HMA, DEMA, or TEMA." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    // Single navigable component: the cloud itself. The underlying MAs
                    // are internal data arrays (__ prefix), not components.
                    new() { Name = CompCloud, DisplayType = ComponentDisplayType.Cloud, Role = ComponentRole.Signal,
                            DefaultColorHex = "#00C853", DefaultColorHexSecondary = "#FF1744",
                            DefaultBullishFrequency = 440, DefaultBearishFrequency = 220,
                            DefaultEnvelopeType = "Sustain",
                            DefaultWaveform = "sine",
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            UpperComponentName = DataFastMA, LowerComponentName = DataSlowMA,
                            DefaultUsePolarityColoring = false },
                },
                DefaultCloudFills = new List<CloudFillConfig>
                {
                    // Visual cloud fill — references the internal MA data arrays for rendering.
                    new() { UpperComponentName = DataFastMA, LowerComponentName = DataSlowMA,
                            BullishColorHex = "#6000C853", BearishColorHex = "#60FF1744",
                            DisplayName = "MA Cloud Fill", IsVisible = true,
                            Sonification = new CloudSonificationConfig(
                                BullishFrequency: 440f,
                                BearishFrequency: 220f,
                                SoundPatchId: "sine_bell",
                                DecayMs: 200,
                                MaxVolume: 0.75f) },
                },
            }
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int n = data.Length;
            int fastPeriod = GetInt(parameters, "FastPeriod", 9);
            int slowPeriod = GetInt(parameters, "SlowPeriod", 21);
            string fastType = GetString(parameters, "FastType", "EMA");
            string slowType = GetString(parameters, "SlowType", "EMA");

            var close = new double[n];
            for (int i = 0; i < n; i++) close[i] = data[i].Close;

            double[] fastMA = MovingAverageHelper.Calculate(close, fastPeriod, fastType);
            double[] slowMA = MovingAverageHelper.Calculate(close, slowPeriod, slowType);

            // Cloud width: positive = bullish (fast > slow), negative = bearish.
            var cloudWidth = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(fastMA[i]) || double.IsNaN(slowMA[i]))
                    cloudWidth[i] = double.NaN;
                else
                    cloudWidth[i] = fastMA[i] - slowMA[i];
            }

            WriteToBuffer(buffer, DataFastMA, fastMA, n);
            WriteToBuffer(buffer, DataSlowMA, slowMA, n);
            WriteToBuffer(buffer, CompCloud, cloudWidth, n);
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int fastPeriod = GetInt(parameters, "FastPeriod", 9);
            int slowPeriod = GetInt(parameters, "SlowPeriod", 21);
            string fastType = GetString(parameters, "FastType", "EMA");
            string slowType = GetString(parameters, "SlowType", "EMA");

            int fastWarmup = MovingAverageHelper.GetWarmupBars(fastType, fastPeriod);
            int slowWarmup = MovingAverageHelper.GetWarmupBars(slowType, slowPeriod);
            return Math.Max(fastWarmup, slowWarmup) * 2;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results, int index, Dictionary<string, object> parameters)
        {
            if (index < 0) return string.Empty;

            double fast = GetVal(results, DataFastMA, index);
            double slow = GetVal(results, DataSlowMA, index);
            if (double.IsNaN(fast) || double.IsNaN(slow)) return string.Empty;

            string fastType = GetString(parameters, "FastType", "EMA");
            string slowType = GetString(parameters, "SlowType", "EMA");
            double width = Math.Abs(fast - slow);
            string trend = fast > slow ? "bullish — fast above slow" :
                           fast < slow ? "bearish — fast below slow" : "neutral — lines converging";
            return $"MA Cloud width {width:F4}. {trend}. Fast {fastType} {fast:F4}, Slow {slowType} {slow:F4}.";
        }

        /// <summary>
        /// What a trader actually wants to know about a cloud, in the order they want it.
        ///
        /// <para>
        /// The previous version said "MA Cloud, bullish, width 2.13" — and the width was in raw
        /// price units, which is unreadable without already knowing the instrument's scale. 2.13 is
        /// enormous on a sub-dollar coin and invisible on an index fund. Every measurement here is
        /// therefore expressed as a <b>percentage of price</b>, the same reasoning that puts every
        /// chart-formation tolerance in ATR.
        /// </para>
        ///
        /// <para>
        /// The order is deliberate, because this is heard on every arrow key and the listener's
        /// attention is on the first few words:
        /// </para>
        /// <list type="number">
        ///   <item><b>Which side price is on, and by how far.</b> The single most actionable fact.
        ///         Inside the cloud is its own answer — it means the two averages disagree about
        ///         where price is, which is what "no trend" looks like mechanically.</item>
        ///   <item><b>Whether it just crossed.</b> Fast crossing slow is THE event on this
        ///         indicator, and it is invisible in a snapshot of the current bar — it is a
        ///         property of two bars, so it has to be computed rather than read.</item>
        ///   <item><b>Expanding or contracting.</b> A widening cloud is a trend gathering pace; a
        ///         pinching one is compression. Direction of change carries more than the level.</item>
        ///   <item><b>Width.</b> Last, because it is the number that needs the most context to
        ///         interpret and the one a listener is least likely to act on directly.</item>
        /// </list>
        /// </summary>
        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (!componentName.Equals(CompCloud, StringComparison.OrdinalIgnoreCase)) return null;
            if (double.IsNaN(value)) return "MA Cloud: no data";

            double fast = GetVal(allComponentData, DataFastMA, dataIndex);
            double slow = GetVal(allComponentData, DataSlowMA, dataIndex);
            double close = bar.Close;

            if (double.IsNaN(fast) || double.IsNaN(slow) || close <= 0)
                return $"MA Cloud, {(value >= 0 ? "bullish" : "bearish")}.";

            double hi = Math.Max(fast, slow);
            double lo = Math.Min(fast, slow);
            var parts = new List<string>();

            // 1. Side, with distance. Inside is a distinct state, not a missing answer.
            if (close >= lo && close <= hi)
                parts.Add("Price inside the cloud");
            else if (close > hi)
                parts.Add($"Price above the cloud by {Pct(close - hi, close)}");
            else
                parts.Add($"Price below the cloud by {Pct(lo - close, close)}");

            // 2. A cross is a two-bar fact and cannot be seen in this bar alone.
            double prev = GetVal(allComponentData, CompCloud, dataIndex - 1);
            bool bullish = value >= 0;
            if (dataIndex > 0 && !double.IsNaN(prev) && (prev >= 0) != bullish)
                parts.Add(bullish ? "just crossed bullish" : "just crossed bearish");
            else
                parts.Add(bullish ? "bullish" : "bearish");

            // 3. Direction of change in width. Needs the previous bar, same as the cross.
            if (dataIndex > 0 && !double.IsNaN(prev))
            {
                double now = Math.Abs(value), was = Math.Abs(prev);
                // A 2% change in the width itself, so ordinary jitter does not read as a trend.
                if (was > 0 && now > was * 1.02) parts.Add("expanding");
                else if (was > 0 && now < was * 0.98) parts.Add("contracting");
            }

            // 4. Width last.
            parts.Add($"width {Pct(Math.Abs(value), close)}");

            return "MA Cloud. " + string.Join(", ", parts.Where(p => p.Length > 0)) + ".";
        }

        /// <summary>
        /// A distance as a percentage of price — scale-free, so it reads the same on a sub-dollar
        /// coin and an index fund.
        /// </summary>
        private static string Pct(double distance, double price)
            => price > 0 ? $"{Math.Abs(distance) / price * 100.0:F2} percent" : "unknown";

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void WriteToBuffer(IIndicatorResultBuffer buffer, string name, double[] data, int n)
        {
            var span = buffer.GetComponentSpan(name);
            int len = Math.Min(span.Length, n);
            for (int i = 0; i < len; i++) span[i] = data[i];
        }

        private static double GetVal(IReadOnlyDictionary<string, double[]> results, string key, int index)
        {
            if (results.TryGetValue(key, out var arr) && index < arr.Length) return arr[index];
            return double.NaN;
        }

        private static int GetInt(Dictionary<string, object> p, string k, int def)
        {
            if (p.TryGetValue(k, out var v))
            {
                if (v is int i)    return i;
                if (v is double d) return (int)d;
                if (int.TryParse(v?.ToString(), out int parsed)) return parsed;
            }
            return def;
        }

        private static string GetString(Dictionary<string, object> p, string k, string def)
        {
            if (p.TryGetValue(k, out var v) && v is string s && !string.IsNullOrWhiteSpace(s))
                return s;
            return def;
        }

        // MA Cloud is a price overlay — no OB/OS reference level lines.
        public List<LevelDescriptor> GetDefaultLevels(string code)
            => new();
    }
}
