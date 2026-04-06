using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// EMA Fill — two EMAs (Fast + Slow) with a cloud fill between them.
    /// Renders on the main price pane as an overlay.
    ///
    /// Components:
    ///   Fast EMA  — faster-period EMA line (blue)
    ///   Slow EMA  — slower-period EMA line (orange)
    ///
    /// Cloud fill (green/red) is declared via DefaultCloudFills — not as a Component —
    /// so it is purely visual and does not appear in navigation or sonification.
    ///
    /// Parameters:
    ///   FastPeriod — fast EMA period (default 9)
    ///   SlowPeriod — slow EMA period (default 21)
    /// </summary>
    public class EmaFillProvider : IIndicatorProvider
    {
        public string Name => "Accessible.EmaFill";

        public const string CompFastEma = "Fast EMA";
        public const string CompSlowEma = "Slow EMA";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "EMA_FILL",
                Name        = "EMA Fill",
                Category    = "Overlays",
                DefaultPane = "Main",
                Description = "Two EMA lines with a directional cloud fill between them. " +
                              "Green when Fast EMA is above Slow EMA (bullish); red when below (bearish).",
                Parameters  = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "FastPeriod", DisplayName = "Fast Period", DataType = typeof(int), DefaultValue = 9,
                            Description = "Period for the fast EMA." },
                    new() { Name = "SlowPeriod", DisplayName = "Slow Period", DataType = typeof(int), DefaultValue = 21,
                            Description = "Period for the slow EMA." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompFastEma, DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal, DefaultColorHex = "#2196F3", DefaultUsePolarityColoring = false },
                    new() { Name = CompSlowEma, DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal, DefaultColorHex = "#FF9800", DefaultUsePolarityColoring = false },
                },
                DefaultCloudFills = new List<CloudFillConfig>
                {
                    new() { UpperComponentName = CompFastEma, LowerComponentName = CompSlowEma,
                            BullishColorHex = "#00C853", BearishColorHex = "#FF1744",
                            DisplayName = "EMA Fill", IsVisible = true,
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
            int n    = data.Length;
            int fast = GetInt(parameters, "FastPeriod", 9);
            int slow = GetInt(parameters, "SlowPeriod", 21);

            var close = new double[n];
            for (int i = 0; i < n; i++) close[i] = data[i].Close;

            double[] fastEma = Ema(close, fast);
            double[] slowEma = Ema(close, slow);

            WriteToBuffer(buffer, CompFastEma, fastEma, n);
            WriteToBuffer(buffer, CompSlowEma, slowEma, n);
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int slow = GetInt(parameters, "SlowPeriod", 21);
            return slow * 2;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results, int index, Dictionary<string, object> parameters)
        {
            if (!code.Equals("EMA_FILL", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (index < 0) return string.Empty;

            double fast = GetVal(results, CompFastEma, index);
            double slow = GetVal(results, CompSlowEma, index);
            if (double.IsNaN(fast) || double.IsNaN(slow)) return string.Empty;

            string trend = fast > slow ? "bullish — fast above slow" :
                           fast < slow ? "bearish — fast below slow" : "neutral — lines converging";
            return $"Fast EMA {fast:F4}, Slow EMA {slow:F4}. {trend}.";
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static double[] Ema(double[] src, int period)
        {
            var r = new double[src.Length];
            double k = 2.0 / (period + 1.0);
            double ema = double.NaN;
            int warmup = 0;
            for (int i = 0; i < src.Length; i++)
            {
                double v = src[i];
                if (double.IsNaN(v)) { r[i] = double.NaN; continue; }
                if (double.IsNaN(ema)) { ema = v; warmup = 1; }
                else { ema = v * k + ema * (1.0 - k); warmup++; }
                r[i] = warmup < period ? double.NaN : ema;
            }
            return r;
        }

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

        // EMA Fill is a price overlay — no OB/OS reference level lines.
        public List<LevelDescriptor> GetDefaultLevels(string code)
            => new();
    }
}
