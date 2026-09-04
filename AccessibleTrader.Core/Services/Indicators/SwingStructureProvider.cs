using System.Text;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Market Structure — higher highs, higher lows, lower highs, lower lows, the trend state
    /// they imply, and the breaks between states.
    ///
    /// <para>
    /// This exists because structure is the one thing a sighted trader reads off a chart's SHAPE
    /// for free, and it is the piece a blind trader has had no equivalent for. Everything else the
    /// terminal narrates is a number at a bar; this narrates the relationship BETWEEN bars.
    /// </para>
    ///
    /// <para>
    /// It is descriptive, and deliberately so. Surrogate testing (2026-07-26) established that the
    /// geometric systems built on structure — equidistant channel grids, level-respect scoring,
    /// micro-interaction classification — carry no measurable edge over a random series with the
    /// same statistics. Structure still tells you WHERE YOU ARE, which is worth having; it just
    /// does not tell you where price is going, and nothing here pretends otherwise.
    /// </para>
    ///
    /// <para>
    /// Relationship to Cipher SR: that indicator answers "what price levels are in play" by
    /// carrying pivot prices forward as zones. This one answers "what is the SEQUENCE of pivots
    /// saying". They share the idea of a pivot and overlap nowhere else — SR gives you the levels,
    /// structure gives you the narrative, and they are genuinely complementary on one chart.
    /// </para>
    ///
    /// <para>
    /// The state and carry-forward arrays — <c>StructureState</c>, <c>LastSwingHigh</c>,
    /// <c>LastSwingLow</c>, and the two break events — change only from <c>barIndex + Span</c>, the
    /// bar a pivot becomes confirmable, so they report what was knowable at the time and are safe
    /// as strategy leaves.
    /// </para>
    ///
    /// <para>
    /// The two MARKERS are not, and say so: <c>SwingHigh</c> / <c>SwingLow</c> sit on the pivot bar
    /// so the glyph lands where the swing visually is, which means the value at that bar could not
    /// have been known for another <c>Span</c> bars. This paragraph used to claim "NO LOOKAHEAD …
    /// every component is safe as a strategy leaf" while the code wrote <c>high[s.BarIndex]</c>
    /// directly underneath, and an inline comment quietly narrowed the claim to the state arrays —
    /// but <c>SignalCatalog</c> published every component regardless, so a strategy gated on
    /// <c>SWING_STRUCTURE.SwingHigh</c> fired <c>Span</c> bars early. The markers are now declared
    /// <see cref="ComponentCausality.Lookahead"/>, which keeps them on the chart and out of the
    /// strategy builder.
    /// </para>
    /// </summary>
    public sealed class SwingStructureProvider : IIndicatorProvider
    {
        public const string Code = "SWING_STRUCTURE";

        public const string CompSwingHigh = "SwingHigh";
        public const string CompSwingLow = "SwingLow";
        public const string CompState = "StructureState";
        public const string CompBos = "BreakOfStructure";
        public const string CompChoch = "ChangeOfCharacter";
        public const string CompLastHigh = "LastSwingHigh";
        public const string CompLastLow = "LastSwingLow";

        private readonly ISwingStructureAnalyzer _analyzer = new SwingStructureAnalyzer();

        public string Name => "Market Structure";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code,
                Causality = ComponentCausality.Causal,
                // The parenthetical left this name on 2026-09-04. It was a picker-list gloss on a
                // field that is SPOKEN — the instance name is what the arrow keys, Page Up/Down and
                // the Object Tree read, dozens of times a session — and a screen reader renders
                // "(HH/HL/LH/LL)" as eight letters and four slashes. The gloss belongs in
                // Description, which is where the picker reads it from and where nothing reads it
                // aloud on every navigation.
                Name = "Market Structure",
                Category = "Overlays",
                DefaultPane = "Main",
                // Pivots are written to historical indices, so a tick must trigger a full recompute
                // exactly as Cipher SR does.
                RequiresFullRecalcOnTick = true,
                Description =
                    "Labels swing highs and lows as higher high, higher low, lower high or lower low " +
                    "(HH/HL/LH/LL) and reports the trend state they imply, plus breaks of structure and " +
                    "changes of character. Descriptive, not predictive.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "Span", DisplayName = "Pivot span (bars each side)",
                            DefaultValue = 5, MinValue = 2, MaxValue = 30 },
                    new() { Name = "MinSwingAtr", DisplayName = "Minimum swing size (ATR)",
                            DefaultValue = 1.0, MinValue = 0, MaxValue = 5 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new()
                    {
                        // Stamped at the pivot bar, knowable Span bars later — see the class remarks.
                        Causality = ComponentCausality.Lookahead,
                        Name = CompSwingHigh, DisplayName = "Swing High",
                        // SQUARE, not a triangle. Value Deviation's shallow resistance tier is a
                        // red down-triangle at the same 7px, so on a chart carrying both there was
                        // no way to tell a swing high from a shallow zone. Market Structure now owns
                        // the angular family (square / cross) and Value Deviation keeps the graded
                        // triangle → dot → diamond ladder, where the shape carries the tier.
                        DisplayType = ComponentDisplayType.Square, Role = ComponentRole.Signal,
                        // Sits just clear of the wick instead of exactly on its tip, so the pivot
                        // price stays readable underneath the glyph.
                        DefaultMarkerAnchor = MarkerAnchor.AboveBar,
                        DefaultColorHex = "#EF5350", DefaultThickness = 8f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 660,
                        // The price is the point of a swing marker: "swing high" alone tells you a pivot is
                        // here, not what level it set. {price} expands through SpeechPriceFormatter.
                        DefaultSignalSpeechTemplate = "Swing high {price}.",
                    },
                    new()
                    {
                        Causality = ComponentCausality.Lookahead,
                        Name = CompSwingLow, DisplayName = "Swing Low",
                        DisplayType = ComponentDisplayType.Square, Role = ComponentRole.Signal,
                        DefaultMarkerAnchor = MarkerAnchor.BelowBar,
                        DefaultColorHex = "#26A69A", DefaultThickness = 8f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 330,
                        DefaultSignalSpeechTemplate = "Swing low {price}.",
                    },
                    new()
                    {
                        // +1 uptrend, -1 downtrend, 0 range, NaN until two swings exist.
                        Name = CompState, DisplayName = "Structure State",
                        DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Signal,
                        IsVisible = false,
                        DefaultColorHex = "#B0BEC5", DefaultThickness = 1.5f,
                        DefaultReferenceLevel = 0.0,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Structure {value:F0}.",
                    },
                    new()
                    {
                        Name = CompBos, DisplayName = "Break of Structure",
                        DisplayType = ComponentDisplayType.Cross, Role = ComponentRole.Signal,
                        DefaultColorHex = "#FFB300", DefaultThickness = 9f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 880,
                        DefaultSignalSpeechTemplate = "Break of structure.",
                    },
                    new()
                    {
                        Name = CompChoch, DisplayName = "Change of Character",
                        // A CROSS, not a diamond. Purple against Value Deviation's neon-red deep
                        // tier looked distinct to me and measured 23,760 in RGB — inside the range
                        // where two glyphs of the same shape read as the same mark, and closer
                        // still under deuteranopia. Paired with Break of Structure as the two
                        // structure EVENTS: same shape, amber for continuation, purple for the
                        // turn, and the turn is drawn larger because it is the consequential one.
                        DisplayType = ComponentDisplayType.Cross, Role = ComponentRole.Signal,
                        DefaultColorHex = "#AB47BC", DefaultThickness = 12f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 990,
                        DefaultSignalSpeechTemplate = "Change of character.",
                    },
                    new()
                    {
                        Name = CompLastHigh, DisplayName = "Last Swing High",
                        DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Level,
                        IsVisible = false,
                        DefaultColorHex = "#EF5350", DefaultThickness = 1f,
                        DefaultDashStyle = DashStyle.Dash,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Last swing high {value:price}.",
                    },
                    new()
                    {
                        Name = CompLastLow, DisplayName = "Last Swing Low",
                        DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Level,
                        IsVisible = false,
                        DefaultColorHex = "#26A69A", DefaultThickness = 1f,
                        DefaultDashStyle = DashStyle.Dash,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Last swing low {value:price}.",
                    },
                }
            }
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int n = data.Length;
            var high = buffer.GetComponentSpan(CompSwingHigh);
            var low = buffer.GetComponentSpan(CompSwingLow);
            var state = buffer.GetComponentSpan(CompState);
            var bos = buffer.GetComponentSpan(CompBos);
            var choch = buffer.GetComponentSpan(CompChoch);
            var lastHigh = buffer.GetComponentSpan(CompLastHigh);
            var lastLow = buffer.GetComponentSpan(CompLastLow);

            for (int i = 0; i < n; i++)
            {
                high[i] = double.NaN; low[i] = double.NaN; bos[i] = double.NaN; choch[i] = double.NaN;
                state[i] = double.NaN; lastHigh[i] = double.NaN; lastLow[i] = double.NaN;
            }
            if (n == 0) return;

            var bars = new Ohlcv[n];
            for (int i = 0; i < n; i++) bars[i] = data[i];

            var result = _analyzer.Analyze(bars, ReadOptions(parameters));

            // Markers sit on the pivot BAR so the dot lands where the swing visually is; the
            // state and carry-forward arrays remain confirmation-gated. Navigation to a marker is a
            // look at history, never a live signal — and the markers are declared Lookahead, so
            // "which arrays strategy leaves read" is now enforced by the catalog rather than
            // asserted by this comment.
            foreach (var s in result.Swings)
            {
                if (s.BarIndex < 0 || s.BarIndex >= n) continue;
                if (s.IsHigh) high[s.BarIndex] = s.Price;
                else low[s.BarIndex] = s.Price;
            }

            foreach (var b in result.Breaks)
            {
                if (b.BarIndex < 0 || b.BarIndex >= n) continue;
                if (b.IsChangeOfCharacter) choch[b.BarIndex] = b.Level;
                else bos[b.BarIndex] = b.Level;
            }

            for (int i = 0; i < n; i++)
            {
                state[i] = result.StatePerBar[i] switch
                {
                    StructureState.Uptrend => 1.0,
                    StructureState.Downtrend => -1.0,
                    StructureState.Range => 0.0,
                    _ => double.NaN
                };
                lastHigh[i] = result.LastHighPrice[i];
                lastLow[i] = result.LastLowPrice[i];
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            // Pivot-based: a new bar can confirm a pivot several bars back, so there is no correct
            // scalar update. RequiresFullRecalcOnTick routes ticks to Calculate instead.
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            var o = ReadOptions(parameters);
            // Enough bars for ATR plus a handful of alternating swings to establish a state.
            return Math.Max(o.AtrPeriod, o.Span * 6);
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (index < 0 || data.Length == 0) return "";

            var sb = new StringBuilder();
            double state = At(calculatedResults, CompState, index);
            sb.Append("Structure: ").Append(DescribeState(state)).Append('.');

            double lastHigh = At(calculatedResults, CompLastHigh, index);
            double lastLow = At(calculatedResults, CompLastLow, index);
            double close = data[index].Close;

            if (!double.IsNaN(lastHigh))
            {
                sb.Append(" Last swing high ").Append(Price(lastHigh));
                if (close > lastHigh) sb.Append(", already exceeded");
                else sb.Append($", {Percent((lastHigh - close) / close)} above");
                sb.Append('.');
            }
            if (!double.IsNaN(lastLow))
            {
                sb.Append(" Last swing low ").Append(Price(lastLow));
                if (close < lastLow) sb.Append(", already broken");
                else sb.Append($", {Percent((close - lastLow) / close)} below");
                sb.Append('.');
            }

            // Where price sits between the two swings — the single most useful orientation fact,
            // and the direct answer to "where am I in the range".
            if (!double.IsNaN(lastHigh) && !double.IsNaN(lastLow) && lastHigh > lastLow)
            {
                double pos = (close - lastLow) / (lastHigh - lastLow);
                sb.Append($" Price is {pos * 100:0}% of the way from the swing low to the swing high.");
            }

            // Count the recent labelled swings so the user hears the sequence, not just the state.
            var labels = RecentLabels(calculatedResults, index, 4);
            if (labels.Count > 0) sb.Append(" Recent swings: ").Append(string.Join(", ", labels)).Append('.');

            return sb.ToString();
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (componentName == "Structure State" || componentName == CompState)
                return $"Structure {DescribeState(value)}.";

            if ((componentName == "Swing High" || componentName == CompSwingHigh) && !double.IsNaN(value))
                return $"Swing high {Price(value)}.";

            if ((componentName == "Swing Low" || componentName == CompSwingLow) && !double.IsNaN(value))
                return $"Swing low {Price(value)}.";

            if ((componentName == "Break of Structure" || componentName == CompBos) && !double.IsNaN(value))
                return $"Break of structure through {Price(value)}.";

            if ((componentName == "Change of Character" || componentName == CompChoch) && !double.IsNaN(value))
                return $"Change of character at {Price(value)}. The prevailing structure was broken against.";

            return null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static SwingOptions ReadOptions(Dictionary<string, object> parameters)
        {
            int span = (int)GetParam(parameters, "Span", 5);
            double minAtr = GetParam(parameters, "MinSwingAtr", 1.0);
            return new SwingOptions(Math.Clamp(span, 2, 30), Math.Max(0, minAtr));
        }

        private static double GetParam(Dictionary<string, object> p, string key, double fallback)
        {
            if (p != null && p.TryGetValue(key, out var raw) && raw != null &&
                double.TryParse(raw.ToString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                return v;
            return fallback;
        }

        private static double At(IReadOnlyDictionary<string, double[]> data, string key, int index)
        {
            if (data.TryGetValue(key, out var arr) && index >= 0 && index < arr.Length) return arr[index];
            return double.NaN;
        }

        private static string DescribeState(double v) =>
            double.IsNaN(v) ? "not yet established"
            : v > 0 ? "uptrend, higher highs and higher lows"
            : v < 0 ? "downtrend, lower highs and lower lows"
            : "range, highs and lows disagree";

        private static List<string> RecentLabels(IReadOnlyDictionary<string, double[]> data, int index, int take)
        {
            var result = new List<string>();
            if (!data.TryGetValue(CompSwingHigh, out var highs) || !data.TryGetValue(CompSwingLow, out var lows))
                return result;

            double prevHigh = double.NaN, prevLow = double.NaN;
            var seq = new List<string>();
            for (int i = 0; i <= index && i < highs.Length && i < lows.Length; i++)
            {
                if (!double.IsNaN(highs[i]))
                {
                    seq.Add(double.IsNaN(prevHigh) ? "first high"
                        : highs[i] > prevHigh ? "higher high" : "lower high");
                    prevHigh = highs[i];
                }
                if (!double.IsNaN(lows[i]))
                {
                    seq.Add(double.IsNaN(prevLow) ? "first low"
                        : lows[i] > prevLow ? "higher low" : "lower low");
                    prevLow = lows[i];
                }
            }
            return seq.Skip(Math.Max(0, seq.Count - take)).ToList();
        }

        private static string Price(double v) =>
            v >= 1000 ? v.ToString("N0") : v >= 1 ? v.ToString("0.##") : v.ToString("0.######");

        private static string Percent(double fraction) => $"{Math.Abs(fraction) * 100:0.0}%";
    }
}
