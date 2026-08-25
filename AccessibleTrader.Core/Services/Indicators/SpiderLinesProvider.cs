using System.Text;
using AccessibleTrader.Sdk.Indicators;
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
        public const string CompStackingScore = "Stacking Score";


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
                Causality = ComponentCausality.Causal,
                Name        = "Spider Lines",
                Category    = "Overlays",
                DefaultPane = "Main",
                Description = "Eight Fibonacci-period EMAs (8, 13, 21, 34, 55, 89, 144, 200) " +
                              "overlaid on the price chart. Short-period lines are warm colors; " +
                              "long-period lines are cool colors. Trend direction is signalled by " +
                              "the ordering and spacing of the web.",
                Parameters  = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "FastMode", DisplayName = "Fast Mode (HMA)", DataType = typeof(bool), DefaultValue = false,
                            Description = "When true, all lines use Hull MA instead of EMA. HMA has ~50% less lag than EMA at the same period, which sharpens the web's response to regime changes at the cost of slightly more whipsaw in chop." },
                },
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
                    // Quantitative stacking score: −36 to +36, weighted by inverse period rank.
                    // A single component strategies can leaf on ("StackingScore > 20 = strong bull").
                    new() { Name = CompStackingScore, DisplayName = "Stacking Score",
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFFFFF", DefaultThickness = 1.0f,
                            IsVisible = false,
                            DefaultReferenceLevel = 0.0,
                            SpeechTemplate = "Spider stacking {value:F0}." },
                },
            }
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int n = data.Length;
            var close = new double[n];
            for (int i = 0; i < n; i++) close[i] = data[i].Close;

            bool fastMode = IndicatorParams.GetBool(parameters, "FastMode", false);

            var emaResults = new double[Lines.Length][];
            for (int li = 0; li < Lines.Length; li++)
            {
                var (name, period) = Lines[li];
                double[] ma = fastMode
                    ? MovingAverageHelper.Hma(close, period)
                    : MovingAverageHelper.Ema(close, period);
                emaResults[li] = ma;
                WriteToBuffer(buffer, name, ma, n);
            }

            // Quantitative stacking score: Σ weight_i × sign(close − ma_i),
            // where weight_i is the descending rank (8 for shortest, 1 for longest).
            // Output range is [-36, +36] (sum of 8+7+6+5+4+3+2+1). Positive = price
            // is stacked above the web (bullish), negative = below (bearish).
            var score = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = 0; int valid = 0;
                for (int li = 0; li < Lines.Length; li++)
                {
                    double v = emaResults[li][i];
                    if (double.IsNaN(v)) { score[i] = double.NaN; valid = -1; break; }
                    double weight = Lines.Length - li; // 8, 7, 6, ... 1
                    s += weight * (close[i] > v ? 1 : (close[i] < v ? -1 : 0));
                    valid++;
                }
                if (valid >= 0) score[i] = s;
            }
            WriteToBuffer(buffer, CompStackingScore, score, n);
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
            sb.Append($"Price {AccessibleTrader.Core.Services.Accessibility.SpeechPriceFormatter.FormatPrice(price)}. ");

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
    }
}
