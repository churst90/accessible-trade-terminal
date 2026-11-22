using System.Globalization;
using System.Text;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Supertrend (Olivier Seban): a trailing line an ATR multiple away from the bar's midpoint,
    /// under price in an uptrend and over it in a downtrend, which flips sides when a close crosses it.
    ///
    /// <para>
    /// <b>The maths, as TradingView's <c>ta.supertrend(factor, atrPeriod)</c> does it.</b> With
    /// <c>mid = (high + low) / 2</c> and <c>ATR</c> Wilder's average true range:
    /// </para>
    /// <code>
    ///   upper = mid + factor·ATR,  kept at the previous upper unless it is lower or the last close was above it
    ///   lower = mid − factor·ATR,  kept at the previous lower unless it is higher or the last close was below it
    ///   first bar with an ATR: down
    ///   after that: if the line was the upper band, up when close &gt; upper; else down when close &lt; lower
    ///   line = lower in an uptrend, upper in a downtrend
    /// </code>
    /// <para>
    /// The ratchet is the point: in an uptrend the lower band only rises, so the line is a stop that
    /// trails. ATR is Wilder's (an RMA of the true range, seeded with the simple average of the first
    /// <c>period</c> ranges), as <c>ta.atr</c> is; the first bar's true range is its high minus its low.
    /// Defaults 10 and 3, TradingView's.
    /// </para>
    ///
    /// <para>
    /// <b>Causal throughout.</b> Every value at a bar is decided by that bar and the ones before it.
    /// The turn marks sit on the bar whose close made the turn; on the bar still forming, that close
    /// is not final, so a turn can come and go until the bar closes, as on every close-based signal.
    /// The ratchet carries state from the first bar, so a tick recomputes the whole series
    /// (<see cref="IndicatorMetadata.RequiresFullRecalcOnTick"/>) rather than a window of it.
    /// </para>
    /// </summary>
    public sealed class SupertrendProvider : IIndicatorProvider
    {
        public const string Code = "SUPERTREND";

        public const string CompUp = "UpTrend";
        public const string CompDown = "DownTrend";
        public const string CompDirection = "Direction";
        public const string CompTurnUp = "TurnUp";
        public const string CompTurnDown = "TurnDown";

        public const string ParamPeriod = "atrPeriods";
        public const string ParamFactor = "multiplier";

        public string Name => "Supertrend";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Supertrend", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "A trailing line an ATR multiple from the bar's midpoint: under price in an uptrend, " +
                    "over it in a downtrend, and it changes sides when a close crosses it. ATR 10, factor 3 " +
                    "by default, as on TradingView.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamPeriod, DisplayName = "ATR Periods", DataType = typeof(int),
                            DefaultValue = 10, MinValue = 1, MaxValue = 200, Step = 1,
                            Description = "Bars in the average true range. 10 by default." },
                    new() { Name = ParamFactor, DisplayName = "Multiplier", DataType = typeof(double),
                            DefaultValue = 3.0, MinValue = 0.1, MaxValue = 20, Step = 0.1,
                            Description = "How many ATRs the line sits from the bar's midpoint. 3 by default." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompUp, DisplayName = "Supertrend Up", DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.LowerBand, DefaultColorHex = "#00C853", DefaultThickness = 2f,
                            SubscribedLevelNames = Array.Empty<string>(),
                            SpeechTemplate = "Supertrend up, support {value:price}." },
                    new() { Name = CompDown, DisplayName = "Supertrend Down", DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.UpperBand, DefaultColorHex = "#FF1744", DefaultThickness = 2f,
                            SubscribedLevelNames = Array.Empty<string>(),
                            SpeechTemplate = "Supertrend down, resistance {value:price}." },
                    new() { Name = CompTurnUp, DisplayName = "Supertrend Turned Up",
                            DisplayType = ComponentDisplayType.TriangleUp, Role = ComponentRole.Signal,
                            DefaultMarkerAnchor = MarkerAnchor.BelowBar,
                            DefaultColorHex = "#00C853", DefaultThickness = 9f,
                            SubscribedLevelNames = Array.Empty<string>(),
                            DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 784,
                            DefaultSignalSpeechTemplate = "Supertrend turned up, support {price}." },
                    new() { Name = CompTurnDown, DisplayName = "Supertrend Turned Down",
                            DisplayType = ComponentDisplayType.TriangleDown, Role = ComponentRole.Signal,
                            DefaultMarkerAnchor = MarkerAnchor.AboveBar,
                            DefaultColorHex = "#FF1744", DefaultThickness = 9f,
                            SubscribedLevelNames = Array.Empty<string>(),
                            DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 392,
                            DefaultSignalSpeechTemplate = "Supertrend turned down, resistance {price}." },
                    // +1 up, -1 down: for strategies and the detail line, left out of the picture.
                    new() { Name = CompDirection, DisplayName = "Supertrend Direction",
                            DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Signal,
                            DefaultIsEnabled = false, DefaultColorHex = "#B0BEC5", DefaultThickness = 1f,
                            DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = Array.Empty<string>(),
                            SpeechTemplate = "Supertrend direction {value:F0}." },
                },
            },
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var (period, factor) = ReadOptions(parameters);
            var r = Compute(data, period, factor);
            var up = buffer.GetComponentSpan(CompUp);
            var down = buffer.GetComponentSpan(CompDown);
            var dir = buffer.GetComponentSpan(CompDirection);
            var turnUp = buffer.GetComponentSpan(CompTurnUp);
            var turnDown = buffer.GetComponentSpan(CompTurnDown);
            for (int i = 0; i < data.Length; i++)
            {
                double line = r.Line[i];
                int d = r.Direction[i];
                dir[i] = d == 0 ? double.NaN : d;
                up[i] = d > 0 ? line : double.NaN;
                down[i] = d < 0 ? line : double.NaN;
                bool turned = i > 0 && d != 0 && r.Direction[i - 1] != 0 && r.Direction[i - 1] != d;
                turnUp[i] = turned && d > 0 ? line : double.NaN;
                turnDown[i] = turned && d < 0 ? line : double.NaN;
            }
        }

        /// <summary>The line and the direction (+1 up, −1 down, 0 before the ATR exists) at each bar.</summary>
        internal static (double[] Line, int[] Direction) Compute(ReadOnlySpan<Ohlcv> data, int period, double factor)
        {
            int n = data.Length;
            var line = new double[n];
            var direction = new int[n];
            Array.Fill(line, double.NaN);

            double atr = double.NaN, trSum = 0;
            double prevUpper = double.NaN, prevLower = double.NaN, prevLine = double.NaN;
            for (int i = 0; i < n; i++)
            {
                var b = data[i];
                double tr = i == 0
                    ? b.High - b.Low
                    : Math.Max(b.High - b.Low, Math.Max(Math.Abs(b.High - data[i - 1].Close), Math.Abs(b.Low - data[i - 1].Close)));
                if (i < period)
                {
                    trSum += tr;
                    if (i == period - 1) atr = trSum / period;
                }
                else atr = (atr * (period - 1) + tr) / period;
                if (double.IsNaN(atr)) continue;

                double mid = (b.High + b.Low) / 2;
                double upper = mid + factor * atr;
                double lower = mid - factor * atr;
                if (!double.IsNaN(prevLower))
                {
                    double lastClose = data[i - 1].Close;
                    if (!(lower > prevLower || lastClose < prevLower)) lower = prevLower;
                    if (!(upper < prevUpper || lastClose > prevUpper)) upper = prevUpper;
                }

                int d;
                if (double.IsNaN(prevLine)) d = -1;                        // the first bar with an ATR
                else if (prevLine == prevUpper) d = b.Close > upper ? 1 : -1;
                else d = b.Close < lower ? -1 : 1;

                direction[i] = d;
                line[i] = d > 0 ? lower : upper;
                prevUpper = upper; prevLower = lower; prevLine = line[i];
            }
            return (line, direction);
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            // The ratchet carries state from the first bar; a window would start it somewhere else.
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
            => Math.Max(ReadOptions(parameters).Period * 3, 35);

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return "";
            double d = At(calculatedResults, CompDirection, index);
            if (double.IsNaN(d)) return "Supertrend: not yet established.";
            double line = d > 0 ? At(calculatedResults, CompUp, index) : At(calculatedResults, CompDown, index);

            int since = 0;
            if (calculatedResults.TryGetValue(CompDirection, out var dirs))
                for (int i = index; i >= 0 && i < dirs.Length && dirs[i] == d; i--) since++;

            double close = data[index].Close;
            var sb = new StringBuilder("Supertrend ");
            sb.Append(d > 0 ? "up" : "down").Append(since == 1 ? ", turned this bar" : $" for {since} bars");
            sb.Append(d > 0 ? ". Support " : ". Resistance ").Append(SpeechPriceFormatter.FormatPrice(line));
            if (close != 0 && !double.IsNaN(line))
                sb.Append(", ").Append((Math.Abs(close - line) / close * 100).ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(d > 0 ? "% below the close." : "% above the close.");
            else sb.Append('.');
            return sb.ToString();
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompUp or "Supertrend Up" => $"Supertrend up, support {SpeechPriceFormatter.FormatPrice(value)}.",
                CompDown or "Supertrend Down" => $"Supertrend down, resistance {SpeechPriceFormatter.FormatPrice(value)}.",
                CompTurnUp or "Supertrend Turned Up" => $"Supertrend turned up, support {SpeechPriceFormatter.FormatPrice(value)}.",
                CompTurnDown or "Supertrend Turned Down" => $"Supertrend turned down, resistance {SpeechPriceFormatter.FormatPrice(value)}.",
                CompDirection or "Supertrend Direction" => value > 0 ? "Supertrend up." : "Supertrend down.",
                _ => null,
            };
        }

        private static (int Period, double Factor) ReadOptions(Dictionary<string, object> p)
            => ((int)Math.Clamp(Math.Round(NumberParam.Get(p, ParamPeriod, 10)), 1, 200),
                Math.Clamp(NumberParam.Get(p, ParamFactor, 3.0), 0.1, 20));

        private static double At(IReadOnlyDictionary<string, double[]> data, string key, int index)
            => data.TryGetValue(key, out var arr) && index >= 0 && index < arr.Length ? arr[index] : double.NaN;
    }

    /// <summary>Reading a number parameter however it arrived: a double, an int, or the text the dialog saved.</summary>
    internal static class NumberParam
    {
        internal static double Get(Dictionary<string, object>? p, string key, double fallback)
        {
            if (p != null && p.TryGetValue(key, out var raw) && raw != null &&
                double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v))
                return v;
            return fallback;
        }
    }
}
