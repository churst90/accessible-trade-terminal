using System.Globalization;
using System.Text;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// The bar-against-its-neighbours tests behind Williams Fractals and Pivot Points High/Low.
    ///
    /// <para>
    /// Neither is Market Structure's swing. That one filters swings by size (an ATR multiple) and
    /// makes highs and lows alternate, so it names the turns that matter; these mark every bar that
    /// stands above (or below) its neighbours, small or large, two highs in a row if the bars say so.
    /// The pivot levels indicator (PIVOTS) is a different thing again: floor-trader levels from the
    /// previous session's high, low and close.
    /// </para>
    /// </summary>
    internal static class PivotMath
    {
        /// <summary>
        /// Bill Williams' fractal: the high is above the <paramref name="span"/> highs after it, and
        /// above <paramref name="span"/> highs before it, where an earlier bar with the SAME high is
        /// passed over rather than counted (his rule for flat tops) and any earlier higher high
        /// cancels it. Lows mirror it.
        /// </summary>
        internal static bool IsFractal(ReadOnlySpan<Ohlcv> d, int i, int span, bool high)
        {
            if (i - span < 0 || i + span >= d.Length) return false;
            double v = high ? d[i].High : d[i].Low;
            if (double.IsNaN(v)) return false;
            for (int j = i + 1; j <= i + span; j++)
                if (!Beyond(v, high ? d[j].High : d[j].Low, high)) return false;
            int lower = 0;
            for (int j = i - 1; j >= 0 && lower < span; j--)
            {
                double w = high ? d[j].High : d[j].Low;
                if (w == v) continue;                 // a flat top: look past it
                if (!Beyond(v, w, high)) return false;
                lower++;
            }
            return lower == span;
        }

        /// <summary>
        /// A pivot high in the TradingView sense: higher than every high in the <paramref name="left"/>
        /// bars before it and the <paramref name="right"/> bars after. Strict on both sides, so two
        /// equal highs side by side make neither a pivot. Lows mirror it.
        /// </summary>
        internal static bool IsPivot(ReadOnlySpan<Ohlcv> d, int i, int left, int right, bool high)
        {
            if (i - left < 0 || i + right >= d.Length) return false;
            double v = high ? d[i].High : d[i].Low;
            if (double.IsNaN(v)) return false;
            for (int j = i - left; j <= i + right; j++)
                if (j != i && !Beyond(v, high ? d[j].High : d[j].Low, high)) return false;
            return true;
        }

        private static bool Beyond(double v, double other, bool high) => high ? v > other : v < other;

        /// <summary>
        /// Marks at the pivot bar (known only <paramref name="confirmAfter"/> bars later) and the
        /// last confirmed high and low carried forward from the bar that confirms them.
        /// </summary>
        internal static void Write(ReadOnlySpan<Ohlcv> d, int confirmAfter, Func<int, bool, bool> isPivot,
            Span<double> markHigh, Span<double> markLow, Span<double> lastHigh, Span<double> lastLow)
        {
            int n = d.Length;
            for (int i = 0; i < n; i++) { markHigh[i] = markLow[i] = lastHigh[i] = lastLow[i] = double.NaN; }
            double hi = double.NaN, lo = double.NaN;
            for (int c = 0; c < n; c++)
            {
                int i = c - confirmAfter;            // the bar this one can confirm
                if (i >= 0)
                {
                    if (isPivot(i, true)) { markHigh[i] = d[i].High; hi = d[i].High; }
                    if (isPivot(i, false)) { markLow[i] = d[i].Low; lo = d[i].Low; }
                }
                lastHigh[c] = hi; lastLow[c] = lo;
            }
        }

        internal static string Detail(string name, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, string markHigh, string markLow,
            string lastHigh, string lastLow, int confirmAfter)
        {
            if (index < 0 || index >= data.Length) return "";
            var sb = new StringBuilder();
            double mh = At(r, markHigh, index), ml = At(r, markLow, index);
            if (!double.IsNaN(mh)) sb.Append($"{name} high here, {Price(mh)}, known {Bars(confirmAfter)} later. ");
            if (!double.IsNaN(ml)) sb.Append($"{name} low here, {Price(ml)}, known {Bars(confirmAfter)} later. ");
            double close = data[index].Close;
            double h = At(r, lastHigh, index), l = At(r, lastLow, index);
            sb.Append($"Last {name.ToLowerInvariant()} high ").Append(double.IsNaN(h) ? "none yet" : Price(h) + Away(h, close));
            sb.Append($". Last {name.ToLowerInvariant()} low ").Append(double.IsNaN(l) ? "none yet" : Price(l) + Away(l, close)).Append('.');
            return sb.ToString();
        }

        private static string Away(double level, double close)
        {
            if (close == 0 || double.IsNaN(close)) return "";
            double pct = (level - close) / close * 100;
            if (Math.Abs(pct) < 0.05) return ", at the close";
            return ", " + Math.Abs(pct).ToString("0.0", CultureInfo.InvariantCulture) + (pct > 0 ? "% above" : "% below");
        }

        private static string Bars(int n) => n == 1 ? "1 bar" : $"{n} bars";

        internal static string Price(double v) => SpeechPriceFormatter.FormatPrice(v);

        internal static double At(IReadOnlyDictionary<string, double[]> data, string key, int index)
            => data.TryGetValue(key, out var arr) && index >= 0 && index < arr.Length ? arr[index] : double.NaN;

        internal static IndicatorComponentMetadata LastLevel(string name, string display, string colour, string words) => new()
        {
            Name = name, DisplayName = display, DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Level,
            DefaultIsEnabled = false, DefaultColorHex = colour, DefaultThickness = 1f, DefaultDashStyle = DashStyle.Dash,
            SubscribedLevelNames = Array.Empty<string>(),
            SpeechTemplate = words + " {value:price}.",
        };
    }

    /// <summary>
    /// Williams Fractals (Bill Williams, <i>Trading Chaos</i>): a high above the two highs on each
    /// side of it, or a low below the two lows on each side; flat tops are looked past. The mark sits
    /// on the fractal bar, so it is drawn <c>span</c> bars after that bar closed: declared Lookahead,
    /// on the chart and out of the strategy builder. The last confirmed fractal high and low, carried
    /// from the bar that confirmed them, are causal (Williams' own use: a break of the last fractal).
    /// </summary>
    public sealed class WilliamsFractalsProvider : IIndicatorProvider
    {
        public const string Code = "WILLIAMS_FRACTALS";
        public const string CompHigh = "FractalHigh";
        public const string CompLow = "FractalLow";
        public const string CompLastHigh = "LastFractalHigh";
        public const string CompLastLow = "LastFractalLow";
        public const string ParamSpan = "span";

        public string Name => "Williams Fractals";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Williams Fractals", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "Bill Williams' fractals: a high above the two highs either side of it, or a low below " +
                    "the two lows either side. Each mark appears two bars after its bar. Every such bar is " +
                    "marked, small or large; Market Structure is the one that keeps only the swings that matter.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamSpan, DisplayName = "Bars Each Side", DataType = typeof(int),
                            DefaultValue = 2, MinValue = 1, MaxValue = 20, Step = 1,
                            Description = "How many bars on each side the high or low must beat. 2 by default, Williams' own." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompHigh, DisplayName = "Fractal High", Causality = ComponentCausality.Lookahead,
                            DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultMarkerAnchor = MarkerAnchor.AboveBar, DefaultColorHex = "#FF7043", DefaultThickness = 5f,
                            SubscribedLevelNames = Array.Empty<string>(),
                            DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 740,
                            DefaultSignalSpeechTemplate = "Fractal high {price}." },
                    new() { Name = CompLow, DisplayName = "Fractal Low", Causality = ComponentCausality.Lookahead,
                            DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultMarkerAnchor = MarkerAnchor.BelowBar, DefaultColorHex = "#29B6F6", DefaultThickness = 5f,
                            SubscribedLevelNames = Array.Empty<string>(),
                            DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 370,
                            DefaultSignalSpeechTemplate = "Fractal low {price}." },
                    PivotMath.LastLevel(CompLastHigh, "Last Fractal High", "#FF7043", "Last fractal high"),
                    PivotMath.LastLevel(CompLastLow, "Last Fractal Low", "#29B6F6", "Last fractal low"),
                },
            },
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int span = Span(parameters);
            var bars = data.ToArray();
            PivotMath.Write(data, span, (i, high) => PivotMath.IsFractal(bars, i, span, high),
                buffer.GetComponentSpan(CompHigh), buffer.GetComponentSpan(CompLow),
                buffer.GetComponentSpan(CompLastHigh), buffer.GetComponentSpan(CompLastLow));
        }

        // A new bar confirms a fractal span bars back, so there is no last-bar-only update.
        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => Span(parameters) * 2 + 1;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters)
            => PivotMath.Detail("Fractal", data, calculatedResults, index, CompHigh, CompLow, CompLastHigh, CompLastLow, Span(parameters));

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompHigh or "Fractal High" => $"Fractal high {PivotMath.Price(value)}.",
                CompLow or "Fractal Low" => $"Fractal low {PivotMath.Price(value)}.",
                CompLastHigh or "Last Fractal High" => $"Last fractal high {PivotMath.Price(value)}.",
                CompLastLow or "Last Fractal Low" => $"Last fractal low {PivotMath.Price(value)}.",
                _ => null,
            };
        }

        private static int Span(Dictionary<string, object> p)
            => (int)Math.Clamp(Math.Round(NumberParam.Get(p, ParamSpan, 2)), 1, 20);
    }

    /// <summary>
    /// Pivot Points High/Low, TradingView's built-in of that name: a high above every high in the
    /// <c>left</c> bars before it and the <c>right</c> bars after, a low below every low the same way,
    /// 10 and 10 by default. The marks sit on the pivot bar and are known <c>right</c> bars later,
    /// so they are declared Lookahead; the last confirmed pivot high and low are causal.
    /// Not to be confused with PIVOTS (floor-trader pivot levels).
    /// </summary>
    public sealed class PivotPointsHighLowProvider : IIndicatorProvider
    {
        public const string Code = "PIVOTS_HL";
        public const string CompHigh = "PivotHigh";
        public const string CompLow = "PivotLow";
        public const string CompLastHigh = "LastPivotHigh";
        public const string CompLastLow = "LastPivotLow";
        public const string ParamLeft = "leftBars";
        public const string ParamRight = "rightBars";

        public string Name => "Pivot Points High Low";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Pivot Points High Low", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "Marks each high that is above every high for 10 bars either side, and each low below " +
                    "every low the same way, with its price. A mark appears when the bars after it are in. " +
                    "Not the floor-trader pivot levels (Pivot Levels).",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamLeft, DisplayName = "Bars Before", DataType = typeof(int),
                            DefaultValue = 10, MinValue = 1, MaxValue = 100, Step = 1,
                            Description = "How many bars before it the pivot must beat. 10 by default." },
                    new() { Name = ParamRight, DisplayName = "Bars After", DataType = typeof(int),
                            DefaultValue = 10, MinValue = 1, MaxValue = 100, Step = 1,
                            Description = "How many bars after it the pivot must beat, and so how late it is marked. 10 by default." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompHigh, DisplayName = "Pivot High", Causality = ComponentCausality.Lookahead,
                            DisplayType = ComponentDisplayType.Diamond, Role = ComponentRole.Signal,
                            DefaultMarkerAnchor = MarkerAnchor.AboveBar, DefaultColorHex = "#FFA726", DefaultThickness = 9f,
                            SubscribedLevelNames = Array.Empty<string>(),
                            DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 698,
                            DefaultSignalSpeechTemplate = "Pivot high {price}." },
                    new() { Name = CompLow, DisplayName = "Pivot Low", Causality = ComponentCausality.Lookahead,
                            DisplayType = ComponentDisplayType.Diamond, Role = ComponentRole.Signal,
                            DefaultMarkerAnchor = MarkerAnchor.BelowBar, DefaultColorHex = "#42A5F5", DefaultThickness = 9f,
                            SubscribedLevelNames = Array.Empty<string>(),
                            DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 349,
                            DefaultSignalSpeechTemplate = "Pivot low {price}." },
                    PivotMath.LastLevel(CompLastHigh, "Last Pivot High", "#FFA726", "Last pivot high"),
                    PivotMath.LastLevel(CompLastLow, "Last Pivot Low", "#42A5F5", "Last pivot low"),
                },
            },
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var (left, right) = Spans(parameters);
            var bars = data.ToArray();
            PivotMath.Write(data, right, (i, high) => PivotMath.IsPivot(bars, i, left, right, high),
                buffer.GetComponentSpan(CompHigh), buffer.GetComponentSpan(CompLow),
                buffer.GetComponentSpan(CompLastHigh), buffer.GetComponentSpan(CompLastLow));
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            var (left, right) = Spans(parameters);
            return left + right + 1;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters)
            => PivotMath.Detail("Pivot", data, calculatedResults, index, CompHigh, CompLow, CompLastHigh, CompLastLow, Spans(parameters).Right);

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompHigh or "Pivot High" => $"Pivot high {PivotMath.Price(value)}.",
                CompLow or "Pivot Low" => $"Pivot low {PivotMath.Price(value)}.",
                CompLastHigh or "Last Pivot High" => $"Last pivot high {PivotMath.Price(value)}.",
                CompLastLow or "Last Pivot Low" => $"Last pivot low {PivotMath.Price(value)}.",
                _ => null,
            };
        }

        private static (int Left, int Right) Spans(Dictionary<string, object> p)
            => ((int)Math.Clamp(Math.Round(NumberParam.Get(p, ParamLeft, 10)), 1, 100),
                (int)Math.Clamp(Math.Round(NumberParam.Get(p, ParamRight, 10)), 1, 100));
    }
}
