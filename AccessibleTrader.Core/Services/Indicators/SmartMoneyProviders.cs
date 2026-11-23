using System.Globalization;
using System.Text;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// The rules behind the smart money concepts indicators, written once so the chart and the
    /// strategy lab use the same code: <c>docs/archive/SMC_PREREG.md</c> grades exactly what these
    /// draw. Every zone and level is known at the close of the bar it is drawn from.
    ///
    /// <para>
    /// Market Structure (break of structure, change of character) is its own indicator and is not
    /// repeated here; the breaks below only decide where an order block is and which way the
    /// structure points for premium and discount.
    /// </para>
    /// </summary>
    internal static class SmcMath
    {
        /// <summary>A price zone known at <see cref="KnownAt"/>, from <see cref="Bottom"/> to <see cref="Top"/>.</summary>
        internal readonly record struct Zone(int KnownAt, int Origin, double Top, double Bottom, bool Bullish);

        /// <summary>A swing high or low at <see cref="Pivot"/>, usable from <see cref="KnownAt"/>.</summary>
        internal readonly record struct Swing(int Pivot, int KnownAt, double Price, bool High);

        /// <summary>A close through the last swing high (up) or low (down).</summary>
        internal readonly record struct Break(int At, bool Up, double Level);

        /// <summary>Two swing highs (or lows) at one price, open from <see cref="KnownAt"/> until <see cref="EndAt"/> (-1: still open).</summary>
        internal readonly record struct EqualLevel(int KnownAt, double Level, bool High, int EndAt);

        /// <summary>The first bar to trade through a swing level: a sweep when it closes back inside, else a breakout.</summary>
        internal readonly record struct Take(int At, double Level, bool High, bool Sweep);

        internal const int DefaultSwing = 5;
        internal const int BlockSearch = 10;

        internal static double[] Atr(ReadOnlySpan<Ohlcv> d) => IndicatorMath.Atr(d, 14);

        /// <summary>
        /// Three-bar gaps: up when bar i's low is above bar i-2's high, by at least
        /// <paramref name="minAtr"/> × ATR; down mirrored. The zone is the price no bar traded in.
        /// </summary>
        internal static List<Zone> FairValueGaps(ReadOnlySpan<Ohlcv> d, double[] atr, double minAtr)
        {
            var list = new List<Zone>();
            for (int i = 2; i < d.Length; i++)
            {
                double a = atr[i];
                if (double.IsNaN(a) || a <= 0) continue;
                double up = d[i].Low - d[i - 2].High;
                if (up > 0 && up >= minAtr * a) list.Add(new Zone(i, i - 2, d[i].Low, d[i - 2].High, true));
                double down = d[i - 2].Low - d[i].High;
                if (down > 0 && down >= minAtr * a) list.Add(new Zone(i, i - 2, d[i - 2].Low, d[i].High, false));
            }
            return list;
        }

        /// <summary>Strict swings, <paramref name="span"/> bars each side, in the order they become known.</summary>
        internal static List<Swing> Swings(ReadOnlySpan<Ohlcv> d, int span)
        {
            var s = new List<Swing>();
            for (int p = span; p + span < d.Length; p++)
            {
                if (PivotMath.IsPivot(d, p, span, span, true)) s.Add(new Swing(p, p + span, d[p].High, true));
                if (PivotMath.IsPivot(d, p, span, span, false)) s.Add(new Swing(p, p + span, d[p].Low, false));
            }
            return s;
        }

        /// <summary>
        /// Breaks of structure, order blocks and the structure's direction per bar (+1 after a break
        /// up, -1 after a break down, 0 before the first). A break up is the first close above the last
        /// known swing high; each swing high breaks once. Its order block is the last down candle in the
        /// <see cref="BlockSearch"/> bars before the break. Down mirrors it.
        /// </summary>
        internal static (List<Break> Breaks, List<Zone> Blocks, int[] Direction) Structure(ReadOnlySpan<Ohlcv> d, List<Swing> swings)
        {
            var breaks = new List<Break>();
            var blocks = new List<Zone>();
            var dir = new int[d.Length];
            double hi = double.NaN, lo = double.NaN;
            bool hiUsed = true, loUsed = true;
            int s = 0, state = 0;
            for (int c = 0; c < d.Length; c++)
            {
                for (; s < swings.Count && swings[s].KnownAt == c; s++)
                {
                    if (swings[s].High) { hi = swings[s].Price; hiUsed = false; }
                    else { lo = swings[s].Price; loUsed = false; }
                }
                if (c > 0 && !hiUsed && d[c].Close > hi && d[c - 1].Close <= hi)
                {
                    hiUsed = true; state = 1;
                    breaks.Add(new Break(c, true, hi));
                    for (int k = c - 1; k >= Math.Max(0, c - BlockSearch); k--)
                        if (d[k].Close < d[k].Open) { blocks.Add(new Zone(c, k, d[k].High, d[k].Low, true)); break; }
                }
                if (c > 0 && !loUsed && d[c].Close < lo && d[c - 1].Close >= lo)
                {
                    loUsed = true; state = -1;
                    breaks.Add(new Break(c, false, lo));
                    for (int k = c - 1; k >= Math.Max(0, c - BlockSearch); k--)
                        if (d[k].Close > d[k].Open) { blocks.Add(new Zone(c, k, d[k].High, d[k].Low, false)); break; }
                }
                dir[c] = state;
            }
            return (breaks, blocks, dir);
        }

        /// <summary>
        /// The most recent zone of one side still standing at each bar. A zone stops being drawn
        /// (mitigated) on the first later close beyond its far edge: below the bottom for a bullish
        /// zone, above the top for a bearish one.
        /// </summary>
        internal static void DrawZones(ReadOnlySpan<Ohlcv> d, List<Zone> zones, bool bullish, Span<double> top, Span<double> bottom)
        {
            var active = new List<Zone>();
            int z = 0;
            for (int c = 0; c < d.Length; c++)
            {
                double close = d[c].Close;
                active.RemoveAll(a => bullish ? close < a.Bottom : close > a.Top);
                for (; z < zones.Count && zones[z].KnownAt <= c; z++)
                    if (zones[z].Bullish == bullish && zones[z].KnownAt == c) active.Add(zones[z]);
                if (active.Count > 0) { top[c] = active[^1].Top; bottom[c] = active[^1].Bottom; }
                else top[c] = bottom[c] = double.NaN;
            }
        }

        /// <summary>
        /// Equal highs: a swing high within <paramref name="tolerance"/> × ATR (at the bar it becomes
        /// known) of the swing high before it, with no bar between them trading above the pair. The
        /// level is the higher of the two, open until a later bar trades above it. Lows mirror it.
        /// </summary>
        internal static List<EqualLevel> EqualLevels(ReadOnlySpan<Ohlcv> d, List<Swing> swings, double[] atr, double tolerance)
        {
            var list = new List<EqualLevel>();
            Swing? prevHigh = null, prevLow = null;
            foreach (var s in swings)
            {
                var prev = s.High ? prevHigh : prevLow;
                if (s.High) prevHigh = s; else prevLow = s;
                if (prev is not { } p) continue;
                double a = atr[s.KnownAt];
                if (double.IsNaN(a) || a <= 0 || Math.Abs(s.Price - p.Price) > tolerance * a) continue;
                double level = s.High ? Math.Max(s.Price, p.Price) : Math.Min(s.Price, p.Price);
                bool clean = true;
                for (int k = p.Pivot + 1; k < s.Pivot && clean; k++)
                    clean = s.High ? d[k].High <= level : d[k].Low >= level;
                if (!clean) continue;
                int end = -1;
                for (int t = s.KnownAt + 1; t < d.Length; t++)
                    if (s.High ? d[t].High > level : d[t].Low < level) { end = t; break; }
                list.Add(new EqualLevel(s.KnownAt, level, s.High, end));
            }
            return list;
        }

        /// <summary>
        /// The first bar to trade through each swing level after it is known, one per bar and side: a
        /// sweep when that bar closes back inside every level it took, a breakout when it closes beyond
        /// the nearest. A bar can be both, through two levels with the close between them.
        /// </summary>
        internal static List<Take> Takes(ReadOnlySpan<Ohlcv> d, List<Swing> swings)
        {
            var byBar = new SortedDictionary<(int At, bool High, bool Sweep), double>();
            foreach (var s in swings)
            {
                for (int t = s.KnownAt + 1; t < d.Length; t++)
                {
                    bool through = s.High ? d[t].High > s.Price : d[t].Low < s.Price;
                    if (!through) continue;
                    bool sweep = s.High ? d[t].Close < s.Price : d[t].Close > s.Price;
                    var key = (t, s.High, sweep);
                    // The level a mark names: the farthest one swept, the nearest one broken.
                    if (!byBar.TryGetValue(key, out var have)
                        || (sweep == s.High ? s.Price > have : s.Price < have))
                        byBar[key] = s.Price;
                    break;
                }
            }
            return byBar.Select(kv => new Take(kv.Key.At, kv.Value, kv.Key.High, kv.Key.Sweep)).ToList();
        }

        internal static string Price(double v) => PivotMath.Price(v);

        internal static string Away(double level, double close)
        {
            if (close == 0 || double.IsNaN(close) || double.IsNaN(level)) return "";
            double pct = (level - close) / close * 100;
            if (Math.Abs(pct) < 0.05) return ", at the close";
            return ", " + Math.Abs(pct).ToString("0.0", CultureInfo.InvariantCulture) + (pct > 0 ? "% above" : "% below");
        }

        internal static double At(IReadOnlyDictionary<string, double[]> r, string key, int i) => PivotMath.At(r, key, i);

        internal static void Fill(Span<double> s) { for (int i = 0; i < s.Length; i++) s[i] = double.NaN; }

        internal static IndicatorComponentMetadata Edge(string name, string display, string colour, string words, bool enabled = true) => new()
        {
            Name = name, DisplayName = display, DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Level,
            DefaultIsEnabled = enabled, DefaultColorHex = colour, DefaultThickness = 1f,
            SubscribedLevelNames = Array.Empty<string>(),
            SpeechTemplate = words + " {value:price}.",
        };

        internal static IndicatorComponentMetadata Mark(string name, string display, ComponentDisplayType shape, bool above,
            string colour, int hz, string words) => new()
        {
            Name = name, DisplayName = display, DisplayType = shape, Role = ComponentRole.Signal,
            DefaultMarkerAnchor = above ? MarkerAnchor.AboveBar : MarkerAnchor.BelowBar,
            DefaultColorHex = colour, DefaultThickness = 7f,
            SubscribedLevelNames = Array.Empty<string>(),
            DefaultEnvelopeType = "Ping", DefaultBaseFrequency = hz,
            DefaultSignalSpeechTemplate = words + " {price}.",
        };

        internal static CloudFillConfig Shade(string upper, string lower, string colour, string name) => new()
        {
            UpperComponentName = upper, LowerComponentName = lower,
            BullishColorHex = colour, BearishColorHex = colour, DisplayName = name, IsVisible = true,
        };

        internal static string ZoneWords(string what, double top, double bottom, double close)
            => double.IsNaN(top) ? $"no {what} open"
               : $"{what} {Price(bottom)} to {Price(top)}{Away((top + bottom) / 2, close)}";

        internal static int SwingParam(Dictionary<string, object> p)
            => (int)Math.Clamp(Math.Round(NumberParam.Get(p, "swingBars", DefaultSwing)), 1, 50);

        internal static IndicatorParameterMetadata SwingParameter() => new()
        {
            Name = "swingBars", DisplayName = "Swing Bars Each Side", DataType = typeof(int),
            DefaultValue = DefaultSwing, MinValue = 1, MaxValue = 50, Step = 1,
            Description = "How many bars on each side a swing high or low must beat. It is known that many bars later. 5 by default.",
        };
    }

    /// <summary>
    /// Fair value gaps: three bars where the third's low is above the first's high (a gap up), or its
    /// high below the first's low (a gap down), so the middle bar left a range no bar traded in. The
    /// nearest gap still open on each side is drawn as a zone; a close beyond its far edge ends it.
    /// </summary>
    public sealed class FairValueGapsProvider : IIndicatorProvider
    {
        public const string Code = "FAIR_VALUE_GAPS";
        public const string CompUpTop = "GapUpTop";
        public const string CompUpBottom = "GapUpBottom";
        public const string CompDownTop = "GapDownTop";
        public const string CompDownBottom = "GapDownBottom";
        public const string CompUpMark = "GapUp";
        public const string CompDownMark = "GapDown";
        public const string ParamMin = "minAtr";

        public string Name => "Fair Value Gaps";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Fair Value Gaps", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal, RequiresFullRecalcOnTick = true,
                Description =
                    "Smart money concepts' fair value gap: three bars where the third bar's low is above the " +
                    "first bar's high, or its high below the first's low, leaving a range no bar traded in. The " +
                    "newest gap still open on each side is drawn as a zone until a close beyond it.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamMin, DisplayName = "Minimum Gap (ATR)", DataType = typeof(double),
                            DefaultValue = 0.1, MinValue = 0.0, MaxValue = 5.0, Step = 0.05,
                            Description = "The smallest gap marked, as a fraction of the 14-bar average true range. 0.1 by default." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    SmcMath.Edge(CompUpTop, "Gap Up Top", "#26A69A", "Gap up top"),
                    SmcMath.Edge(CompUpBottom, "Gap Up Bottom", "#26A69A", "Gap up bottom"),
                    SmcMath.Edge(CompDownTop, "Gap Down Top", "#EF5350", "Gap down top"),
                    SmcMath.Edge(CompDownBottom, "Gap Down Bottom", "#EF5350", "Gap down bottom"),
                    SmcMath.Mark(CompUpMark, "New Gap Up", ComponentDisplayType.Square, false, "#26A69A", 523, "Fair value gap up"),
                    SmcMath.Mark(CompDownMark, "New Gap Down", ComponentDisplayType.Square, true, "#EF5350", 262, "Fair value gap down"),
                },
                DefaultCloudFills = new List<CloudFillConfig>
                {
                    SmcMath.Shade(CompUpTop, CompUpBottom, "#4026A69A", "Gap Up Fill"),
                    SmcMath.Shade(CompDownTop, CompDownBottom, "#40EF5350", "Gap Down Fill"),
                },
            },
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var gaps = SmcMath.FairValueGaps(data, SmcMath.Atr(data), Min(parameters));
            SmcMath.DrawZones(data, gaps, true, buffer.GetComponentSpan(CompUpTop), buffer.GetComponentSpan(CompUpBottom));
            SmcMath.DrawZones(data, gaps, false, buffer.GetComponentSpan(CompDownTop), buffer.GetComponentSpan(CompDownBottom));
            var up = buffer.GetComponentSpan(CompUpMark);
            var down = buffer.GetComponentSpan(CompDownMark);
            SmcMath.Fill(up); SmcMath.Fill(down);
            foreach (var g in gaps)
                if (g.Bullish) up[g.KnownAt] = g.Top; else down[g.KnownAt] = g.Bottom;
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 15;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return "";
            double close = data[index].Close;
            var sb = new StringBuilder("Fair value gaps: ");
            if (!double.IsNaN(SmcMath.At(r, CompUpMark, index))) sb.Append("a gap up opened on this bar. ");
            if (!double.IsNaN(SmcMath.At(r, CompDownMark, index))) sb.Append("a gap down opened on this bar. ");
            sb.Append(SmcMath.ZoneWords("gap up", SmcMath.At(r, CompUpTop, index), SmcMath.At(r, CompUpBottom, index), close)).Append("; ");
            sb.Append(SmcMath.ZoneWords("gap down", SmcMath.At(r, CompDownTop, index), SmcMath.At(r, CompDownBottom, index), close)).Append('.');
            return sb.ToString();
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompUpTop or CompUpBottom or "Gap Up Top" or "Gap Up Bottom"
                    => $"Open gap up, {SmcMath.Price(SmcMath.At(all, CompUpBottom, dataIndex))} to {SmcMath.Price(SmcMath.At(all, CompUpTop, dataIndex))}.",
                CompDownTop or CompDownBottom or "Gap Down Top" or "Gap Down Bottom"
                    => $"Open gap down, {SmcMath.Price(SmcMath.At(all, CompDownBottom, dataIndex))} to {SmcMath.Price(SmcMath.At(all, CompDownTop, dataIndex))}.",
                CompUpMark or "New Gap Up" => $"Fair value gap up, top {SmcMath.Price(value)}.",
                CompDownMark or "New Gap Down" => $"Fair value gap down, bottom {SmcMath.Price(value)}.",
                _ => null,
            };
        }

        internal static double Min(Dictionary<string, object> p) => Math.Clamp(NumberParam.Get(p, ParamMin, 0.1), 0.0, 5.0);
    }

    /// <summary>
    /// Order blocks: when a close breaks the last swing high, the last down candle in the ten bars
    /// before the break is the bullish order block, drawn from its low to its high until a close
    /// below its low. Bearish mirrors it on a break of the last swing low.
    /// </summary>
    public sealed class OrderBlocksProvider : IIndicatorProvider
    {
        public const string Code = "ORDER_BLOCKS";
        public const string CompBullTop = "BullBlockTop";
        public const string CompBullBottom = "BullBlockBottom";
        public const string CompBearTop = "BearBlockTop";
        public const string CompBearBottom = "BearBlockBottom";
        public const string CompBullMark = "NewBullBlock";
        public const string CompBearMark = "NewBearBlock";

        public string Name => "Order Blocks";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Order Blocks", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal, RequiresFullRecalcOnTick = true,
                Description =
                    "Smart money concepts' order block: when a close breaks the last swing high, the last down " +
                    "candle before the break, drawn as a zone from its low to its high until a close below it. " +
                    "Bearish blocks mirror it on a break of the last swing low.",
                Parameters = new List<IndicatorParameterMetadata> { SmcMath.SwingParameter() },
                Components = new List<IndicatorComponentMetadata>
                {
                    SmcMath.Edge(CompBullTop, "Bullish Block Top", "#66BB6A", "Bullish order block top"),
                    SmcMath.Edge(CompBullBottom, "Bullish Block Bottom", "#66BB6A", "Bullish order block bottom"),
                    SmcMath.Edge(CompBearTop, "Bearish Block Top", "#EC407A", "Bearish order block top"),
                    SmcMath.Edge(CompBearBottom, "Bearish Block Bottom", "#EC407A", "Bearish order block bottom"),
                    SmcMath.Mark(CompBullMark, "New Bullish Block", ComponentDisplayType.TriangleUp, false, "#66BB6A", 587, "Bullish order block, top"),
                    SmcMath.Mark(CompBearMark, "New Bearish Block", ComponentDisplayType.TriangleDown, true, "#EC407A", 294, "Bearish order block, bottom"),
                },
                DefaultCloudFills = new List<CloudFillConfig>
                {
                    SmcMath.Shade(CompBullTop, CompBullBottom, "#4066BB6A", "Bullish Block Fill"),
                    SmcMath.Shade(CompBearTop, CompBearBottom, "#40EC407A", "Bearish Block Fill"),
                },
            },
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var (_, blocks, _) = SmcMath.Structure(data, SmcMath.Swings(data, SmcMath.SwingParam(parameters)));
            SmcMath.DrawZones(data, blocks, true, buffer.GetComponentSpan(CompBullTop), buffer.GetComponentSpan(CompBullBottom));
            SmcMath.DrawZones(data, blocks, false, buffer.GetComponentSpan(CompBearTop), buffer.GetComponentSpan(CompBearBottom));
            var bull = buffer.GetComponentSpan(CompBullMark);
            var bear = buffer.GetComponentSpan(CompBearMark);
            SmcMath.Fill(bull); SmcMath.Fill(bear);
            foreach (var b in blocks)
                if (b.Bullish) bull[b.KnownAt] = b.Top; else bear[b.KnownAt] = b.Bottom;
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => SmcMath.SwingParam(parameters) * 2 + 1;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return "";
            double close = data[index].Close;
            var sb = new StringBuilder("Order blocks: ");
            if (!double.IsNaN(SmcMath.At(r, CompBullMark, index))) sb.Append("this bar broke a swing high and set a bullish block. ");
            if (!double.IsNaN(SmcMath.At(r, CompBearMark, index))) sb.Append("this bar broke a swing low and set a bearish block. ");
            sb.Append(SmcMath.ZoneWords("bullish block", SmcMath.At(r, CompBullTop, index), SmcMath.At(r, CompBullBottom, index), close)).Append("; ");
            sb.Append(SmcMath.ZoneWords("bearish block", SmcMath.At(r, CompBearTop, index), SmcMath.At(r, CompBearBottom, index), close)).Append('.');
            return sb.ToString();
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompBullTop or CompBullBottom or "Bullish Block Top" or "Bullish Block Bottom"
                    => $"Bullish order block, {SmcMath.Price(SmcMath.At(all, CompBullBottom, dataIndex))} to {SmcMath.Price(SmcMath.At(all, CompBullTop, dataIndex))}.",
                CompBearTop or CompBearBottom or "Bearish Block Top" or "Bearish Block Bottom"
                    => $"Bearish order block, {SmcMath.Price(SmcMath.At(all, CompBearBottom, dataIndex))} to {SmcMath.Price(SmcMath.At(all, CompBearTop, dataIndex))}.",
                CompBullMark or "New Bullish Block" => $"New bullish order block, top {SmcMath.Price(value)}.",
                CompBearMark or "New Bearish Block" => $"New bearish order block, bottom {SmcMath.Price(value)}.",
                _ => null,
            };
        }
    }

    /// <summary>
    /// Liquidity: equal highs and lows (two swing highs at one price, the stops traders say sit above
    /// them), drawn until price trades through; and sweeps, the first bar through a swing high or low
    /// that closes back inside.
    /// </summary>
    public sealed class LiquidityProvider : IIndicatorProvider
    {
        public const string Code = "LIQUIDITY";
        public const string CompEqualHigh = "EqualHighs";
        public const string CompEqualLow = "EqualLows";
        public const string CompSweepHigh = "SweptHigh";
        public const string CompSweepLow = "SweptLow";
        public const string ParamTolerance = "tolerance";

        public string Name => "Liquidity: Equal Highs and Lows, Sweeps";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Liquidity: Equal Highs and Lows, Sweeps", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal, RequiresFullRecalcOnTick = true,
                Description =
                    "Smart money concepts' liquidity. Equal highs: two swing highs at about the same price with " +
                    "nothing above them in between, drawn until price trades above; equal lows mirror it. A sweep " +
                    "is the first bar through a swing high or low that closes back inside.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    SmcMath.SwingParameter(),
                    new() { Name = ParamTolerance, DisplayName = "Equal Within (ATR)", DataType = typeof(double),
                            DefaultValue = 0.1, MinValue = 0.0, MaxValue = 2.0, Step = 0.05,
                            Description = "How close two highs must be to count as equal, as a fraction of the 14-bar average true range. 0.1 by default." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    SmcMath.Edge(CompEqualHigh, "Equal Highs", "#FFB74D", "Equal highs"),
                    SmcMath.Edge(CompEqualLow, "Equal Lows", "#4FC3F7", "Equal lows"),
                    SmcMath.Mark(CompSweepHigh, "Swept High", ComponentDisplayType.Cross, true, "#FFB74D", 659, "Swept the high"),
                    SmcMath.Mark(CompSweepLow, "Swept Low", ComponentDisplayType.Cross, false, "#4FC3F7", 330, "Swept the low"),
                },
            },
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var swings = SmcMath.Swings(data, SmcMath.SwingParam(parameters));
            var levels = SmcMath.EqualLevels(data, swings, SmcMath.Atr(data), Tolerance(parameters));
            Draw(data.Length, levels, true, buffer.GetComponentSpan(CompEqualHigh));
            Draw(data.Length, levels, false, buffer.GetComponentSpan(CompEqualLow));
            var sh = buffer.GetComponentSpan(CompSweepHigh);
            var sl = buffer.GetComponentSpan(CompSweepLow);
            SmcMath.Fill(sh); SmcMath.Fill(sl);
            foreach (var t in SmcMath.Takes(data, swings))
                if (t.Sweep) (t.High ? sh : sl)[t.At] = t.Level;
        }

        // The newest equal level still open on each side; it is drawn through the bar that takes it.
        private static void Draw(int n, List<SmcMath.EqualLevel> levels, bool high, Span<double> line)
        {
            SmcMath.Fill(line);
            for (int c = 0; c < n; c++)
                for (int k = levels.Count - 1; k >= 0; k--)
                {
                    var l = levels[k];
                    if (l.High != high || l.KnownAt > c || (l.EndAt >= 0 && l.EndAt < c)) continue;
                    line[c] = l.Level;
                    break;
                }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => SmcMath.SwingParam(parameters) * 2 + 15;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return "";
            double close = data[index].Close;
            var sb = new StringBuilder("Liquidity: ");
            double sh = SmcMath.At(r, CompSweepHigh, index), sl = SmcMath.At(r, CompSweepLow, index);
            if (!double.IsNaN(sh)) sb.Append($"this bar swept the high {SmcMath.Price(sh)} and closed back below. ");
            if (!double.IsNaN(sl)) sb.Append($"this bar swept the low {SmcMath.Price(sl)} and closed back above. ");
            double eh = SmcMath.At(r, CompEqualHigh, index), el = SmcMath.At(r, CompEqualLow, index);
            sb.Append(double.IsNaN(eh) ? "no equal highs open" : $"equal highs {SmcMath.Price(eh)}{SmcMath.Away(eh, close)}").Append("; ");
            sb.Append(double.IsNaN(el) ? "no equal lows open" : $"equal lows {SmcMath.Price(el)}{SmcMath.Away(el, close)}").Append('.');
            return sb.ToString();
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompEqualHigh or "Equal Highs" => $"Equal highs {SmcMath.Price(value)}.",
                CompEqualLow or "Equal Lows" => $"Equal lows {SmcMath.Price(value)}.",
                CompSweepHigh or "Swept High" => $"Swept the high {SmcMath.Price(value)}, closed back below.",
                CompSweepLow or "Swept Low" => $"Swept the low {SmcMath.Price(value)}, closed back above.",
                _ => null,
            };
        }

        internal static double Tolerance(Dictionary<string, object> p) => Math.Clamp(NumberParam.Get(p, ParamTolerance, 0.1), 0.0, 2.0);
    }

    /// <summary>
    /// Premium and discount: the range from the last swing low to the last swing high, split at its
    /// middle (equilibrium). Above the middle is premium, below is discount. The structure's direction
    /// (the last close through a swing high or low) is carried alongside, because the teaching is to
    /// buy discount in a rising structure and sell premium in a falling one.
    /// </summary>
    public sealed class PremiumDiscountProvider : IIndicatorProvider
    {
        public const string Code = "PREMIUM_DISCOUNT";
        public const string CompHigh = "RangeHigh";
        public const string CompLow = "RangeLow";
        public const string CompMid = "Equilibrium";
        public const string CompZone = "Zone";
        public const string CompStructure = "Structure";
        public const string CompIntoPremium = "IntoPremium";
        public const string CompIntoDiscount = "IntoDiscount";

        public string Name => "Premium and Discount";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Premium and Discount", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal, RequiresFullRecalcOnTick = true,
                Description =
                    "Smart money concepts' dealing range: from the last swing low to the last swing high, split at " +
                    "its middle, the equilibrium. Above it is premium, below it discount. Also says which way the " +
                    "structure last broke.",
                Parameters = new List<IndicatorParameterMetadata> { SmcMath.SwingParameter() },
                Components = new List<IndicatorComponentMetadata>
                {
                    SmcMath.Edge(CompHigh, "Range High", "#EF9A9A", "Range high"),
                    SmcMath.Edge(CompMid, "Equilibrium", "#BDBDBD", "Equilibrium"),
                    SmcMath.Edge(CompLow, "Range Low", "#A5D6A7", "Range low"),
                    // +1 premium, -1 discount, 0 outside the range or at the middle: for strategies and the detail.
                    new() { Name = CompZone, DisplayName = "Premium or Discount", DisplayType = ComponentDisplayType.StepLine,
                            Role = ComponentRole.Signal, DefaultIsEnabled = false, DefaultColorHex = "#B0BEC5",
                            DefaultThickness = 1f, DefaultReferenceLevel = 0.0, SubscribedLevelNames = Array.Empty<string>(),
                            SpeechTemplate = "Zone {value:F0}." },
                    // +1 after a close above a swing high, -1 after a close below a swing low.
                    new() { Name = CompStructure, DisplayName = "Structure Direction", DisplayType = ComponentDisplayType.StepLine,
                            Role = ComponentRole.Signal, DefaultIsEnabled = false, DefaultColorHex = "#B0BEC5",
                            DefaultThickness = 1f, DefaultReferenceLevel = 0.0, SubscribedLevelNames = Array.Empty<string>(),
                            SpeechTemplate = "Structure {value:F0}." },
                    // The bar a close moves into the zone from anywhere else; marked at the equilibrium.
                    SmcMath.Mark(CompIntoPremium, "Into Premium", ComponentDisplayType.Diamond, true, "#EF9A9A", 494, "Into premium, equilibrium"),
                    SmcMath.Mark(CompIntoDiscount, "Into Discount", ComponentDisplayType.Diamond, false, "#A5D6A7", 392, "Into discount, equilibrium"),
                },
                DefaultCloudFills = new List<CloudFillConfig>
                {
                    SmcMath.Shade(CompHigh, CompMid, "#26EF5350", "Premium Fill"),
                    SmcMath.Shade(CompMid, CompLow, "#2626A69A", "Discount Fill"),
                },
            },
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var swings = SmcMath.Swings(data, SmcMath.SwingParam(parameters));
            var (_, _, dir) = SmcMath.Structure(data, swings);
            var hi = buffer.GetComponentSpan(CompHigh);
            var lo = buffer.GetComponentSpan(CompLow);
            var mid = buffer.GetComponentSpan(CompMid);
            var zone = buffer.GetComponentSpan(CompZone);
            var structure = buffer.GetComponentSpan(CompStructure);
            var intoPremium = buffer.GetComponentSpan(CompIntoPremium);
            var intoDiscount = buffer.GetComponentSpan(CompIntoDiscount);
            SmcMath.Fill(intoPremium); SmcMath.Fill(intoDiscount);
            double h = double.NaN, l = double.NaN;
            int s = 0;
            for (int c = 0; c < data.Length; c++)
            {
                for (; s < swings.Count && swings[s].KnownAt == c; s++)
                    if (swings[s].High) h = swings[s].Price; else l = swings[s].Price;
                structure[c] = dir[c];
                if (double.IsNaN(h) || double.IsNaN(l) || h <= l)
                {
                    hi[c] = lo[c] = mid[c] = zone[c] = double.NaN;
                    continue;
                }
                hi[c] = h; lo[c] = l; mid[c] = (h + l) / 2;
                double close = data[c].Close;
                zone[c] = close < l || close > h || close == mid[c] ? 0 : close > mid[c] ? 1 : -1;
                double was = c > 0 ? zone[c - 1] : double.NaN;
                if (zone[c] > 0 && !(was > 0)) intoPremium[c] = mid[c];
                if (zone[c] < 0 && !(was < 0)) intoDiscount[c] = mid[c];
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => SmcMath.SwingParam(parameters) * 2 + 1;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return "";
            double h = SmcMath.At(r, CompHigh, index), l = SmcMath.At(r, CompLow, index);
            double st = SmcMath.At(r, CompStructure, index);
            string structure = st > 0 ? "Structure last broke up." : st < 0 ? "Structure last broke down." : "No break of structure yet.";
            if (double.IsNaN(h) || double.IsNaN(l)) return "Premium and discount: no range yet, it needs a swing high above a swing low. " + structure;
            double close = data[index].Close;
            string where = close > h ? "above the range"
                : close < l ? "below the range"
                : close > (h + l) / 2 ? $"premium, {Pct(close, l, h)}% of the way up"
                : close < (h + l) / 2 ? $"discount, {Pct(close, l, h)}% of the way up"
                : "at equilibrium";
            if (!double.IsNaN(SmcMath.At(r, CompIntoPremium, index))) where = "moved into " + where;
            if (!double.IsNaN(SmcMath.At(r, CompIntoDiscount, index))) where = "moved into " + where;
            return $"Premium and discount: {where}; range {SmcMath.Price(l)} to {SmcMath.Price(h)}, equilibrium {SmcMath.Price((h + l) / 2)}. {structure}";
        }

        private static string Pct(double close, double l, double h)
            => ((close - l) / (h - l) * 100).ToString("0", CultureInfo.InvariantCulture);

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompHigh or "Range High" => $"Range high {SmcMath.Price(value)}.",
                CompLow or "Range Low" => $"Range low {SmcMath.Price(value)}.",
                CompMid or "Equilibrium" => $"Equilibrium {SmcMath.Price(value)}, {(bar.Close > value ? "price in premium" : bar.Close < value ? "price in discount" : "price at equilibrium")}.",
                CompZone or "Premium or Discount" => value > 0 ? "Premium." : value < 0 ? "Discount." : "Outside the range.",
                CompStructure or "Structure Direction" => value > 0 ? "Structure up." : value < 0 ? "Structure down." : "No structure yet.",
                CompIntoPremium or "Into Premium" => $"Into premium, above equilibrium {SmcMath.Price(value)}.",
                CompIntoDiscount or "Into Discount" => $"Into discount, below equilibrium {SmcMath.Price(value)}.",
                _ => null,
            };
        }
    }
}
