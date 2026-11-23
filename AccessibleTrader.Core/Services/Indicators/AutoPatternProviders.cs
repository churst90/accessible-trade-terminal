using System.Globalization;
using System.Text;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    // Indicator group 6: lines a chartist draws by hand, drawn from the swings instead. Checked against
    // the suite first. The Fibonacci retracement is a drawing tool (you place both ends), nothing drew
    // one from the swings, and nothing drew trend lines. Chart formations were already drawn
    // (ChartFormationLayer, Settings > Appearance) and spoken, so they are not here.
    //
    // Both are drawn only from the bar their swings are known, never back over the bars before: a
    // line drawn from a pivot that was confirmed ten bars later would show a level nobody could have
    // seen at the time, and every past bar would read as if it had.

    /// <summary>
    /// Auto Fibonacci retracement: the levels of the last finished swing, from the ZigZag's confirmed
    /// turns. 0 is the swing's end (the newer turn) and 100 its start, so on a swing up 38.2 is 38.2
    /// percent of the way back down. Redrawn each time a new turn is confirmed.
    /// </summary>
    public sealed class AutoFibProvider : OscillatorProviderBase
    {
        public const string Code = "AUTO_FIB";
        public const string ParamPercent = ZigZagProvider.ParamPercent;
        public const string CompNewUp = "NewFibSwingUp";
        public const string CompNewDown = "NewFibSwingDown";

        /// <summary>The levels, as fractions of the swing back from its end, and their component names.</summary>
        internal static readonly (string Name, double Ratio, string Label)[] Levels =
        {
            ("Fib0", 0, "0"), ("Fib236", 0.236, "23.6"), ("Fib382", 0.382, "38.2"), ("Fib500", 0.5, "50"),
            ("Fib618", 0.618, "61.8"), ("Fib786", 0.786, "78.6"), ("Fib100", 1, "100"),
        };

        /// <summary>"Fib 61.8": a level's spoken name, here and in "Near resistance at …".</summary>
        internal static string? SpokenName(string component)
        {
            foreach (var l in Levels) if (l.Name == component) return "Fib " + l.Label;
            return null;
        }

        public override string Name => "Auto Fib Retracement";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Auto Fib Retracement", Category = "Reference Level", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description = "Fibonacci retracement levels of the last finished swing, from the ZigZag's turns. " +
                    "Drawn from the bar the swing's end is known, and marked there.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamPercent, DisplayName = "Reversal Percent", DataType = typeof(double),
                            DefaultValue = 5.0, MinValue = 0.1, MaxValue = 100, Step = 0.1,
                            Description = "How far price must move back from a high or low, in percent, to end a swing. 5 by default, as in the ZigZag." },
                },
                Components = Levels.Select(l => new IndicatorComponentMetadata
                {
                    Name = l.Name, DisplayName = "Auto Fib " + l.Label,
                    DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.None,
                    DefaultColorHex = l.Ratio switch { 0 or 1 => "#B0BEC5", 0.5 => "#FFCA28", 0.618 => "#FF7043", _ => "#90CAF9" },
                    DefaultThickness = l.Ratio is 0.5 or 0.618 ? 1.5f : 1f,
                    SubscribedLevelNames = Array.Empty<string>(),
                    SpeechTemplate = $"Fib {l.Label} {{value:price}}.",
                }).Concat(OverlayMath.BreakMarks(
                    CompNewUp, "New Fib Swing Up", "Fib levels redrawn on a swing up.",
                    CompNewDown, "New Fib Swing Down", "Fib levels redrawn on a swing down.")).ToList(),
            },
        };

        public override List<LevelDescriptor> GetDefaultLevels(string code) => new();

        private static double Percent(Dictionary<string, object>? p)
            => Math.Clamp(NumberParam.Get(p ?? new(), ParamPercent, 5.0), 0.1, 100);

        /// <summary>The swing known at each bar: its start and end turns (null before two are confirmed).</summary>
        internal static (ZigZagProvider.Turn From, ZigZagProvider.Turn To)?[] Swings(ReadOnlySpan<Ohlcv> data, double percent)
        {
            var (turns, _, _) = ZigZagProvider.Compute(data, percent);
            var at = new (ZigZagProvider.Turn, ZigZagProvider.Turn)?[data.Length];
            int next = 0;
            (ZigZagProvider.Turn, ZigZagProvider.Turn)? now = null;
            for (int c = 0; c < data.Length; c++)
            {
                while (next < turns.Count && turns[next].ConfirmedAt <= c)
                {
                    if (next > 0) now = (turns[next - 1], turns[next]);
                    next++;
                }
                at[c] = now;
            }
            return at;
        }

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            int n = data.Length;
            var r = Levels.ToDictionary(l => l.Name, _ => IndicatorMath.NanArray(n));
            r[CompNewUp] = IndicatorMath.NanArray(n); r[CompNewDown] = IndicatorMath.NanArray(n);
            var swings = Swings(data, Percent(p));
            for (int c = 0; c < n; c++)
            {
                if (swings[c] is not { } s) continue;
                double end = s.To.Price, start = s.From.Price;
                foreach (var l in Levels) r[l.Name][c] = end + (start - end) * l.Ratio;
                // Marked on the bar the swing's end is confirmed, when the levels are redrawn.
                if (s.To.ConfirmedAt == c) r[s.To.IsHigh ? CompNewUp : CompNewDown][c] = data[c].Close;
            }
            return r;
        }

        public override int GetStabilityWindow(string code, Dictionary<string, object> p) => 100;

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            double pct = Percent(p);
            if (Swings(data[..(index + 1)], pct)[index] is not { } s)
                return $"Auto Fib: no finished swing of {pct.ToString("0.#", CultureInfo.InvariantCulture)} percent yet; scroll back or lower the reversal percent.";
            double end = s.To.Price, start = s.From.Price, close = data[index].Close;
            bool up = s.To.IsHigh;
            var sb = new StringBuilder($"Last swing {(up ? "up" : "down")} from {OverlayMath.Price(start)} to {OverlayMath.Price(end)}");
            sb.Append($", {index - s.To.Index} bars since it ended. ");
            double back = (end - close) / (end - start) * 100;   // percent of the swing given back
            string b = Math.Abs(back).ToString("0.0", CultureInfo.InvariantCulture);
            if (back < 0) sb.Append($"Close beyond the swing's end, {b} percent of it further {(up ? "up" : "down")}.");
            else if (back > 100) sb.Append($"Close has given back all of it and {(back - 100).ToString("0.0", CultureInfo.InvariantCulture)} percent more.");
            else
            {
                sb.Append($"Close has retraced {b} percent");
                var below = Levels.Last(l => l.Ratio * 100 <= back);
                var above = Levels.FirstOrDefault(l => l.Ratio * 100 > back);
                sb.Append(above.Name == null || below.Ratio * 100 == back
                    ? $", on {below.Label}." : $", between {below.Label} and {above.Label}.");
            }
            return sb.ToString();
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            var name = SpokenName(componentName);
            return name == null ? null : $"{name} {OverlayMath.Price(value)}.";
        }
    }

    /// <summary>
    /// Auto trend lines: a resistance line through the last two pivot highs when the newer one is
    /// lower, and a support line through the last two pivot lows when the newer one is higher,
    /// extended a bar at a time from the bar the second pivot is confirmed. A close through a line
    /// is marked and ends it; the next pivot starts a new one.
    /// </summary>
    public sealed class AutoTrendLinesProvider : OscillatorProviderBase
    {
        public const string Code = "AUTO_TRENDLINES";
        public const string CompResistance = "TrendResistance";
        public const string CompSupport = "TrendSupport";
        public const string CompBrokeUp = "BrokeTrendResistance";
        public const string CompBrokeDown = "BrokeTrendSupport";
        public const string ParamBars = "pivotBars";

        public override string Name => "Auto Trend Lines";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Auto Trend Lines", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description = "A falling line through the last two pivot highs and a rising line through the last two pivot lows, " +
                    "drawn from the bar the second pivot is known until a close breaks it.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamBars, DisplayName = "Pivot Bars", DataType = typeof(int), DefaultValue = 10,
                            MinValue = 1, MaxValue = 100, Step = 1,
                            Description = "Bars each side a pivot must stand above (or below). 10 by default; a pivot is known that many bars after it." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompResistance, DisplayName = "Trend Line Resistance", DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.None, DefaultColorHex = "#EF5350", DefaultThickness = 1.5f,
                            SubscribedLevelNames = Array.Empty<string>(), SpeechTemplate = "Trend line resistance {value:price}." },
                    new() { Name = CompSupport, DisplayName = "Trend Line Support", DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.None, DefaultColorHex = "#26A69A", DefaultThickness = 1.5f,
                            SubscribedLevelNames = Array.Empty<string>(), SpeechTemplate = "Trend line support {value:price}." },
                }.Concat(OverlayMath.BreakMarks(
                    CompBrokeUp, "Broke Trend Line Resistance", "Closed above the trend line resistance.",
                    CompBrokeDown, "Broke Trend Line Support", "Closed below the trend line support.")).ToList(),
            },
        };

        public override List<LevelDescriptor> GetDefaultLevels(string code) => new();

        private static int Bars(Dictionary<string, object>? p)
            => (int)Math.Clamp(Math.Round(NumberParam.Get(p ?? new(), ParamBars, 10)), 1, 100);

        /// <summary>One line as it stands at a bar: the two pivots it runs through, and whether a close has broken it.</summary>
        internal readonly record struct TrendLine(int I1, double P1, int I2, double P2, int BrokeAt)
        {
            public double Slope => (P2 - P1) / (I2 - I1);
            public double At(int bar) => P2 + Slope * (bar - I2);
        }

        /// <summary>The resistance and support lines as they stood at the close of each bar.</summary>
        internal static (TrendLine?[] Res, TrendLine?[] Sup) Lines(ReadOnlySpan<Ohlcv> d, int k)
        {
            int n = d.Length;
            var res = new TrendLine?[n]; var sup = new TrendLine?[n];
            (int I, double P)? h1 = null, h2 = null, l1 = null, l2 = null;
            TrendLine? r = null, s = null;
            for (int c = 0; c < n; c++)
            {
                int i = c - k;   // the bar confirmed as a pivot (or not) at this close
                if (i >= 0 && PivotMath.IsPivot(d, i, k, k, high: true))
                {
                    h1 = h2; h2 = (i, d[i].High);
                    r = h1 is { } a && h2.Value.P < a.P ? new TrendLine(a.I, a.P, i, d[i].High, -1) : null;
                }
                if (i >= 0 && PivotMath.IsPivot(d, i, k, k, high: false))
                {
                    l1 = l2; l2 = (i, d[i].Low);
                    s = l1 is { } a && l2.Value.P > a.P ? new TrendLine(a.I, a.P, i, d[i].Low, -1) : null;
                }
                if (r is { BrokeAt: < 0 } rl && d[c].Close > rl.At(c)) r = rl with { BrokeAt = c };
                if (s is { BrokeAt: < 0 } sl && d[c].Close < sl.At(c)) s = sl with { BrokeAt = c };
                res[c] = r; sup[c] = s;
            }
            return (res, sup);
        }

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            int n = data.Length;
            var res = IndicatorMath.NanArray(n); var sup = IndicatorMath.NanArray(n);
            var up = IndicatorMath.NanArray(n); var down = IndicatorMath.NanArray(n);
            var (rs, ss) = Lines(data, Bars(p));
            for (int c = 0; c < n; c++)
            {
                // A broken line is drawn up to the bar that broke it, and not after.
                if (rs[c] is { } r && (r.BrokeAt < 0 || r.BrokeAt == c))
                {
                    res[c] = r.At(c);
                    if (r.BrokeAt == c) up[c] = data[c].Close;
                }
                if (ss[c] is { } s && (s.BrokeAt < 0 || s.BrokeAt == c))
                {
                    sup[c] = s.At(c);
                    if (s.BrokeAt == c) down[c] = data[c].Close;
                }
            }
            return new() { [CompResistance] = res, [CompSupport] = sup, [CompBrokeUp] = up, [CompBrokeDown] = down };
        }

        // Each line hangs on its last two pivots; a few hundred bars covers them on any chart a line
        // still describes.
        public override int GetStabilityWindow(string code, Dictionary<string, object> p) => 300;

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            var (rs, ss) = Lines(data[..(index + 1)], Bars(p));
            double close = data[index].Close;
            return Describe("Resistance", "falling", "pivot highs rose", rs[index], index, close) + " " +
                   Describe("Support", "rising", "pivot lows fell", ss[index], index, close);
        }

        private static string Describe(string side, string way, string none, TrendLine? line, int index, double close)
        {
            if (line is not { } t) return $"No {side.ToLowerInvariant()} line: the last two {none}, or there are not two yet.";
            if (t.BrokeAt >= 0 && t.BrokeAt < index)
                return $"{side} line broken {index - t.BrokeAt} bars ago at {OverlayMath.Price(t.At(t.BrokeAt))}; waits for a new pivot.";
            double now = t.At(index);
            var s = $"{side} line {way} {OverlayMath.Price(Math.Abs(t.Slope))} a bar from {OverlayMath.Price(t.P1)} and {OverlayMath.Price(t.P2)}, " +
                    $"now {OverlayMath.Price(now)}; close {OverlayMath.Against(close, now)} it.";
            return t.BrokeAt == index ? s + " Broke it this bar." : s;
        }

        /// <summary>"trend line resistance": a line's spoken name, here and in "Near resistance at …".</summary>
        internal static string? SpokenName(string component) => component switch
        {
            CompResistance => "trend line resistance", CompSupport => "trend line support", _ => null,
        };

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            var name = SpokenName(componentName);
            return name == null ? null : $"{char.ToUpperInvariant(name[0])}{name[1..]} {OverlayMath.Price(value)}.";
        }
    }
}
