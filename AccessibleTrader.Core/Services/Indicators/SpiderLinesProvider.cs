using System;
using System.Collections.Generic;
using System.Text;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Spider Lines — 8 Fibonacci-period EMAs overlaid on the main price pane.
    ///
    /// Periods (Fibonacci sequence): 8, 13, 21, 34, 55, 89, 144, 200.
    /// Color gradient: warm (short periods) → cool (long periods), matching Market Cipher aesthetics.
    ///
    /// All lines render in the Main pane as price overlays. The 200 EMA serves as a
    /// key dynamic support/resistance level. Together the 8 EMAs form a "web" that
    /// signals trend direction by their ordering and spacing.
    ///
    /// Parameters:
    ///   (none) — periods are fixed Fibonacci values by design.
    /// </summary>
    public class SpiderLinesProvider : IIndicatorProvider
    {
        public string Name => "Accessible.SpiderLines";

        // Component name constants — period baked in for clarity in navigation and TTS.
        public const string Comp8   = "EMA 8";
        public const string Comp13  = "EMA 13";
        public const string Comp21  = "EMA 21";
        public const string Comp34  = "EMA 34";
        public const string Comp55  = "EMA 55";
        public const string Comp89  = "EMA 89";
        public const string Comp144 = "EMA 144";
        public const string Comp200 = "EMA 200";

        // Warm → cool gradient matching MC spider colors
        private static readonly string[] Colors =
        {
            "#FF4D4D",  // EMA 8   — red
            "#FF8C00",  // EMA 13  — orange
            "#FFD700",  // EMA 21  — gold
            "#66BB6A",  // EMA 34  — green
            "#26C6DA",  // EMA 55  — teal/cyan
            "#42A5F5",  // EMA 89  — blue
            "#AB47BC",  // EMA 144 — purple
            "#EC407A",  // EMA 200 — magenta/pink
        };

        private static readonly (string Name, int Period)[] Lines =
        {
            (Comp8,   8),
            (Comp13,  13),
            (Comp21,  21),
            (Comp34,  34),
            (Comp55,  55),
            (Comp89,  89),
            (Comp144, 144),
            (Comp200, 200),
        };

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "SPIDER_LINES",
                Name        = "Spider Lines",
                Category    = "Overlays",
                DefaultPane = "Main",
                Description = "Eight Fibonacci-period EMAs (8, 13, 21, 34, 55, 89, 144, 200) " +
                              "overlaid on the price chart. Short-period lines are warm colors; " +
                              "long-period lines are cool colors. Trend direction is signalled by " +
                              "the ordering and spacing of the web.",
                Parameters  = new List<IndicatorParameterMetadata>(),
                Components  = new List<IndicatorComponentMetadata>
                {
                    new() { Name = Comp8,   DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal, DefaultColorHex = "#FF4D4D", DefaultIsZoneLine = true, DefaultUsePolarityColoring = false },
                    new() { Name = Comp13,  DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal, DefaultColorHex = "#FF8C00", DefaultIsZoneLine = true, DefaultUsePolarityColoring = false },
                    new() { Name = Comp21,  DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal, DefaultColorHex = "#FFD700", DefaultIsZoneLine = true, DefaultUsePolarityColoring = false },
                    new() { Name = Comp34,  DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal, DefaultColorHex = "#66BB6A", DefaultIsZoneLine = true, DefaultUsePolarityColoring = false },
                    new() { Name = Comp55,  DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal, DefaultColorHex = "#26C6DA", DefaultIsZoneLine = true, DefaultUsePolarityColoring = false },
                    new() { Name = Comp89,  DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal, DefaultColorHex = "#42A5F5", DefaultIsZoneLine = true, DefaultUsePolarityColoring = false },
                    new() { Name = Comp144, DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal, DefaultColorHex = "#AB47BC", DefaultIsZoneLine = true, DefaultUsePolarityColoring = false },
                    new() { Name = Comp200, DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal, DefaultColorHex = "#EC407A", DefaultIsZoneLine = true, DefaultUsePolarityColoring = false },
                },
            }
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int n = data.Length;
            var close = new double[n];
            for (int i = 0; i < n; i++) close[i] = data[i].Close;

            for (int li = 0; li < Lines.Length; li++)
            {
                var (name, period) = Lines[li];
                double[] ema = Ema(close, period);
                WriteToBuffer(buffer, name, ema, n);
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
            => 200 * 2; // longest period × 2 for warm-up

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results, int index, Dictionary<string, object> parameters)
        {
            if (!code.Equals("SPIDER_LINES", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (index < 0) return string.Empty;

            double price = index < data.Length ? data[index].Close : double.NaN;
            if (double.IsNaN(price)) return string.Empty;

            var sb = new StringBuilder();
            sb.Append($"Price {price:F4}. ");

            // Count how many EMAs are below price (bullish stacking signal)
            int belowCount = 0;
            foreach (var (name, _) in Lines)
            {
                double v = GetVal(results, name, index);
                if (!double.IsNaN(v) && v < price) belowCount++;
            }

            string trend = belowCount switch
            {
                8 => "all 8 EMAs below price — strongly bullish",
                7 => "7 EMAs below price — bullish",
                6 => "6 EMAs below price — moderately bullish",
                5 => "5 EMAs below price — slightly bullish",
                4 => "price splitting the web — neutral",
                3 => "3 EMAs below price — slightly bearish",
                2 => "2 EMAs below price — moderately bearish",
                1 => "only 1 EMA below price — bearish",
                0 => "all EMAs above price — strongly bearish",
                _ => "mixed"
            };

            sb.Append(trend);
            sb.Append(". ");

            // Key levels
            double e21 = GetVal(results, Comp21, index);
            double e55 = GetVal(results, Comp55, index);
            double e200 = GetVal(results, Comp200, index);
            if (!double.IsNaN(e21))  sb.Append($"EMA 21: {e21:F4}. ");
            if (!double.IsNaN(e55))  sb.Append($"EMA 55: {e55:F4}. ");
            if (!double.IsNaN(e200)) sb.Append($"EMA 200: {e200:F4}.");

            return sb.ToString();
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

        // Spider Lines are price overlays — no OB/OS reference level lines.
        public List<LevelDescriptor> GetDefaultLevels(string code)
            => new();

        // Colors are declared directly in component metadata (DefaultColorHex).
        // This method is used internally by GetDetailFact for display purposes.
        private static string GetComponentColor(string componentName)
        {
            for (int i = 0; i < Lines.Length; i++)
                if (Lines[i].Name == componentName) return Colors[i];
            return "#FFFFFF";
        }
    }
}
