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

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (!componentName.Equals(CompCloud, StringComparison.OrdinalIgnoreCase)) return null;
            if (double.IsNaN(value)) return "MA Cloud: no data";

            bool bullish = value >= 0;
            double width = Math.Abs(value);
            string direction = bullish ? "bullish" : "bearish";

            // Check price position relative to cloud.
            string pricePos = "";
            double fast = GetVal(allComponentData, DataFastMA, dataIndex);
            double slow = GetVal(allComponentData, DataSlowMA, dataIndex);
            if (!double.IsNaN(fast) && !double.IsNaN(slow))
            {
                double hi = Math.Max(fast, slow);
                double lo = Math.Min(fast, slow);
                double close = bar.Close;
                if (close >= lo && close <= hi) pricePos = " Price inside.";
                else if (close > hi) pricePos = " Price above.";
                else pricePos = " Price below.";
            }

            return $"MA Cloud, {direction}, width {width:F2}.{pricePos}";
        }

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

    // Backward compatibility alias — existing workspaces may reference the old class name.
    public class EmaFillProvider : MACloudProvider { }
}
