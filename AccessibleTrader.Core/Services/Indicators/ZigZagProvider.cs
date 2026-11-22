using System.Globalization;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// ZigZag: straight lines from turn to turn, where a turn is a high (or low) that price then
    /// moved away from by at least the reversal percentage. Moves smaller than that are ignored, so
    /// the line is the market with its noise taken out.
    ///
    /// <para>
    /// <b>The rule.</b> Highs and lows, not closes. While the line is rising it follows each new
    /// high; when a low comes in the reversal percentage under the highest high since the last
    /// turn, that high becomes a turn and the line starts falling from it. Falling mirrors it. At
    /// the start, whichever comes first decides the first turn: a high that far above the lowest low
    /// so far, or a low that far under the highest high. 5% by default, as on TradingView and in
    /// Skender's ZigZag.
    /// </para>
    ///
    /// <para>
    /// <b>What it knows when.</b> The line and the turn marks are hindsight: a turn is drawn on its
    /// own bar but is only known at the bar that moved the percentage away from it, and the last leg
    /// (from the last turn to the newest extreme) moves until a reversal confirms it. Both are
    /// declared Lookahead, so they are on the chart and out of the strategy builder. The last
    /// confirmed turn high and low, carried from the bar that confirmed them, are causal, as is the
    /// direction of the confirmed swing.
    /// </para>
    /// </summary>
    public sealed class ZigZagProvider : IIndicatorProvider
    {
        public const string Code = "ZIGZAG";
        public const string CompLine = "ZigZag";
        public const string CompTurnHigh = "TurnHigh";
        public const string CompTurnLow = "TurnLow";
        public const string CompLastHigh = "LastTurnHigh";
        public const string CompLastLow = "LastTurnLow";
        public const string ParamPercent = "percentChange";

        public string Name => "ZigZag";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "ZigZag", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "Straight lines from turn to turn, ignoring any move smaller than 5% (the setting). " +
                    "A turn is known only once price has moved that far from it, and the last leg keeps " +
                    "moving until then; it describes what happened, it does not signal.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamPercent, DisplayName = "Reversal Percent", DataType = typeof(double),
                            DefaultValue = 5.0, MinValue = 0.1, MaxValue = 100, Step = 0.1,
                            Description = "How far price must move back from a high or low, in percent, to make it a turn. 5 by default." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompLine, DisplayName = "ZigZag", Causality = ComponentCausality.Lookahead,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Median,
                            DefaultColorHex = "#FFCA28", DefaultThickness = 2f,
                            SubscribedLevelNames = Array.Empty<string>() },
                    new() { Name = CompTurnHigh, DisplayName = "ZigZag High", Causality = ComponentCausality.Lookahead,
                            DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultMarkerAnchor = MarkerAnchor.AboveBar, DefaultColorHex = "#FFCA28", DefaultThickness = 6f,
                            SubscribedLevelNames = Array.Empty<string>(),
                            DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 659,
                            DefaultSignalSpeechTemplate = "ZigZag high {price}." },
                    new() { Name = CompTurnLow, DisplayName = "ZigZag Low", Causality = ComponentCausality.Lookahead,
                            DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultMarkerAnchor = MarkerAnchor.BelowBar, DefaultColorHex = "#FFCA28", DefaultThickness = 6f,
                            SubscribedLevelNames = Array.Empty<string>(),
                            DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 330,
                            DefaultSignalSpeechTemplate = "ZigZag low {price}." },
                    PivotMath.LastLevel(CompLastHigh, "Last ZigZag High", "#FFCA28", "Last zigzag high"),
                    PivotMath.LastLevel(CompLastLow, "Last ZigZag Low", "#FFCA28", "Last zigzag low"),
                },
            },
        };

        /// <summary>A turn: its bar, its price, high or low, and the bar that confirmed it.</summary>
        internal readonly record struct Turn(int Index, double Price, bool IsHigh, int ConfirmedAt);

        /// <summary>The confirmed turns in order, and the newest extreme the last leg runs to (−1 when none).</summary>
        internal static (List<Turn> Turns, int TailIndex, double TailPrice) Compute(ReadOnlySpan<Ohlcv> d, double percent)
        {
            var turns = new List<Turn>();
            double p = percent / 100.0;
            int n = d.Length;
            if (n == 0) return (turns, -1, double.NaN);

            int trend = 0;                       // +1 rising (following highs), −1 falling, 0 not started
            int hiIdx = 0, loIdx = 0;            // trend 0: the highest high and lowest low so far
            int extIdx = 0;                      // trend ±1: the extreme the current leg has reached
            for (int i = 0; i < n; i++)
            {
                double h = d[i].High, l = d[i].Low;
                if (double.IsNaN(h) || double.IsNaN(l)) continue;
                if (trend == 0)
                {
                    if (h > d[hiIdx].High || double.IsNaN(d[hiIdx].High)) hiIdx = i;
                    if (l < d[loIdx].Low || double.IsNaN(d[loIdx].Low)) loIdx = i;
                    bool upFromLow = h >= d[loIdx].Low * (1 + p) && loIdx < i;
                    bool downFromHigh = l <= d[hiIdx].High * (1 - p) && hiIdx < i;
                    // The confirming bar is the leg's extreme so far: any bar between the low and it
                    // high enough to beat it would have confirmed the low itself.
                    if (upFromLow && (!downFromHigh || loIdx < hiIdx))
                    {
                        turns.Add(new Turn(loIdx, d[loIdx].Low, false, i));
                        trend = 1; extIdx = i;
                    }
                    else if (downFromHigh)
                    {
                        turns.Add(new Turn(hiIdx, d[hiIdx].High, true, i));
                        trend = -1; extIdx = i;
                    }
                }
                else if (trend > 0)
                {
                    if (h > d[extIdx].High) extIdx = i;
                    else if (l <= d[extIdx].High * (1 - p))
                    {
                        turns.Add(new Turn(extIdx, d[extIdx].High, true, i));
                        trend = -1; extIdx = i;
                    }
                }
                else
                {
                    if (l < d[extIdx].Low) extIdx = i;
                    else if (h >= d[extIdx].Low * (1 + p))
                    {
                        turns.Add(new Turn(extIdx, d[extIdx].Low, false, i));
                        trend = 1; extIdx = i;
                    }
                }
            }
            if (trend == 0) return (turns, -1, double.NaN);
            return (turns, extIdx, trend > 0 ? d[extIdx].High : d[extIdx].Low);
        }

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int n = data.Length;
            var line = buffer.GetComponentSpan(CompLine);
            var th = buffer.GetComponentSpan(CompTurnHigh);
            var tl = buffer.GetComponentSpan(CompTurnLow);
            var lh = buffer.GetComponentSpan(CompLastHigh);
            var ll = buffer.GetComponentSpan(CompLastLow);
            for (int i = 0; i < n; i++) line[i] = th[i] = tl[i] = lh[i] = ll[i] = double.NaN;

            var (turns, tailIdx, tailPrice) = Compute(data, Percent(parameters));

            var points = turns.Select(t => (t.Index, t.Price)).ToList();
            if (tailIdx >= 0 && (points.Count == 0 || tailIdx > points[^1].Index)) points.Add((tailIdx, tailPrice));
            for (int k = 0; k + 1 < points.Count; k++)
            {
                var (a, pa) = points[k];
                var (b, pb) = points[k + 1];
                for (int i = a; i <= b; i++) line[i] = pa + (pb - pa) * (i - a) / (b - a);
            }

            double hi = double.NaN, lo = double.NaN;
            int next = 0;
            for (int c = 0; c < n; c++)
            {
                while (next < turns.Count && turns[next].ConfirmedAt == c)
                {
                    var t = turns[next++];
                    if (t.IsHigh) { th[t.Index] = t.Price; hi = t.Price; }
                    else { tl[t.Index] = t.Price; lo = t.Price; }
                }
                lh[c] = hi; ll[c] = lo;
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 100;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return "";
            return Known(calculatedResults, index, data[index].Close);
        }

        /// <summary>
        /// What was known at the close of <paramref name="index"/>, from the causal last-turn levels
        /// only: the turn confirmed most recently (so which way the swing is going), how far the
        /// close is from it, and the other one. The leg's end is hindsight and is not said here;
        /// arrowing along the line says it.
        /// </summary>
        internal static string Known(IReadOnlyDictionary<string, double[]> r, int index, double close)
        {
            double hi = PivotMath.At(r, CompLastHigh, index), lo = PivotMath.At(r, CompLastLow, index);
            if (double.IsNaN(hi) && double.IsNaN(lo)) return "ZigZag: no turn confirmed yet.";
            // The level that changed most recently is the newer turn.
            bool highNewer = false;
            for (int i = index; i >= 0; i--)
            {
                double h = PivotMath.At(r, CompLastHigh, i), l = PivotMath.At(r, CompLastLow, i);
                bool hChanged = i == 0 ? !double.IsNaN(h) : !Same(h, PivotMath.At(r, CompLastHigh, i - 1));
                bool lChanged = i == 0 ? !double.IsNaN(l) : !Same(l, PivotMath.At(r, CompLastLow, i - 1));
                if (hChanged != lChanged) { highNewer = hChanged; break; }
                if (hChanged) break;
            }
            if (double.IsNaN(lo)) highNewer = true;
            if (double.IsNaN(hi)) highNewer = false;
            double turn = highNewer ? hi : lo, other = highNewer ? lo : hi;
            string text = highNewer
                ? $"ZigZag: falling since the confirmed high {PivotMath.Price(turn)}{Pct(turn, close)} to the close."
                : $"ZigZag: rising since the confirmed low {PivotMath.Price(turn)}{Pct(turn, close)} to the close.";
            if (!double.IsNaN(other))
                text += highNewer ? $" The low before it, {PivotMath.Price(other)}." : $" The high before it, {PivotMath.Price(other)}.";
            return text;
        }

        private static bool Same(double a, double b) => a == b || (double.IsNaN(a) && double.IsNaN(b));

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompTurnHigh or "ZigZag High" => $"ZigZag high {PivotMath.Price(value)}{Change(allComponentData, dataIndex, true)}.",
                CompTurnLow or "ZigZag Low" => $"ZigZag low {PivotMath.Price(value)}{Change(allComponentData, dataIndex, false)}.",
                CompLine or "ZigZag" => Leg(allComponentData, dataIndex),
                CompLastHigh or "Last ZigZag High" => $"Last zigzag high {PivotMath.Price(value)}.",
                CompLastLow or "Last ZigZag Low" => $"Last zigzag low {PivotMath.Price(value)}.",
                _ => null,
            };
        }

        /// <summary>
        /// The leg a bar is on, in words: "rising leg, 89,400 to 95,000, up 6.3%." The last leg, with
        /// no confirmed turn after it, is said to be still moving.
        /// </summary>
        internal static string? Leg(IReadOnlyDictionary<string, double[]> r, int index)
        {
            if (!r.TryGetValue(CompTurnHigh, out var th) || !r.TryGetValue(CompTurnLow, out var tl)
                || !r.TryGetValue(CompLine, out var line) || index < 0 || index >= line.Length) return null;
            if (double.IsNaN(th[index]) == false) return $"ZigZag high {PivotMath.Price(th[index])}{Change(r, index, true)}.";
            if (double.IsNaN(tl[index]) == false) return $"ZigZag low {PivotMath.Price(tl[index])}{Change(r, index, false)}.";

            int from = -1;
            for (int i = index; i >= 0; i--) if (!double.IsNaN(th[i]) || !double.IsNaN(tl[i])) { from = i; break; }
            int to = -1;
            for (int i = index; i < line.Length; i++) if (!double.IsNaN(th[i]) || !double.IsNaN(tl[i])) { to = i; break; }
            if (from < 0) return double.IsNaN(line[index]) ? null : "ZigZag, before the first turn.";

            bool fromHigh = !double.IsNaN(th[from]);
            double start = fromHigh ? th[from] : tl[from];
            string dir = fromHigh ? "falling" : "rising";
            if (to < 0)
                return $"ZigZag {dir} leg from {PivotMath.Price(start)}, still moving, not confirmed.";
            double end = fromHigh ? tl[to] : th[to];
            if (double.IsNaN(end)) end = fromHigh ? th[to] : tl[to];
            return $"ZigZag {dir} leg, {PivotMath.Price(start)} to {PivotMath.Price(end)}{Pct(start, end)}.";
        }

        /// <summary>", from 89,400, up 6.3%": the move from the turn before, which is the other kind.</summary>
        private static string Change(IReadOnlyDictionary<string, double[]> r, int index, bool high)
        {
            if (!r.TryGetValue(high ? CompTurnLow : CompTurnHigh, out var other)
                || !r.TryGetValue(high ? CompTurnHigh : CompTurnLow, out var own)) return "";
            for (int i = index - 1; i >= 0; i--)
                if (!double.IsNaN(other[i]))
                    return ", from " + PivotMath.Price(other[i]) + Pct(other[i], own[index]);
            return "";
        }

        private static string Pct(double from, double to)
        {
            if (from == 0 || double.IsNaN(from) || double.IsNaN(to)) return "";
            double pct = (to - from) / from * 100;
            return (pct >= 0 ? ", up " : ", down ") + Math.Abs(pct).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        private static double Percent(Dictionary<string, object> p)
            => Math.Clamp(NumberParam.Get(p, ParamPercent, 5.0), 0.1, 100);
    }
}
