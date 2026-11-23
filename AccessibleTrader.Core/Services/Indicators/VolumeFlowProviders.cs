using System.Globalization;
using System.Text;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    // Indicator group 3 (Cody 2026-11-22: "work through these, adding all of the ones you listed
    // that are missing"): cumulative volume delta, the Klinger oscillator, price volume trend and
    // relative volume at time. Net Volume is not here: the chart's bars carry no buy/sell split, so
    // per bar it would be the volume signed by its candle, which the Volume pane already draws,
    // sounds and says (ChartMath.IsDrawnOverTheCandles, 2026-11-22).

    /// <summary>
    /// Cumulative volume delta: buying volume minus selling volume, added up from the start of
    /// each day (or week, month, year), and back to zero when the next one starts.
    ///
    /// <para>
    /// <b>Where buying and selling come from.</b> TradingView splits each bar into lower-timeframe
    /// bars and signs each one's volume by its own candle. The chart's bars here carry no trade-side
    /// volume and no finer bars, so the split is made at the chart's own bars, by the same rule:
    /// a candle that closed above its open is buying, below is selling, and a doji takes the side of
    /// its close against the last close (none on the first bar). On a 1-minute chart that is close to
    /// TradingView's; on a daily chart a whole day's volume goes one way. The manual says so.
    /// </para>
    ///
    /// <para>
    /// <b>The anchor</b> is the point of the indicator and also what keeps it causal: the running
    /// total starts from zero at each period boundary, so a bar's value depends only on its own
    /// period. "Auto" anchors to the day on an intraday chart, the month on a daily one and the year
    /// on a weekly or longer one. A period the chart starts partway through is left blank, because a
    /// total that began at the chart's left edge would change when older bars arrive; "None" sums
    /// from the chart's first bar and is the one setting that does.
    /// </para>
    /// </summary>
    public sealed class CumulativeVolumeDeltaProvider : IIndicatorProvider
    {
        public const string Code = "CVD";
        public const string CompCvd = "Cvd";
        public const string ParamAnchor = "Anchor";

        public string Name => "Cumulative Volume Delta";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Cumulative Volume Delta", Category = "Volume", DefaultPane = "Pane_Cvd",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "Buying volume minus selling volume, added up from the start of each day (or week, " +
                    "month, year) and back to zero when the next one starts. Each bar's volume counts as " +
                    "buying when its candle closed up and selling when it closed down.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamAnchor, DisplayName = "Anchor Period", DataType = typeof(string),
                            DefaultValue = "Auto",
                            Description = "Auto, Day, Week, Month, Year or None. Auto is the day on an intraday " +
                                          "chart, the month on a daily one and the year on a weekly one. None adds " +
                                          "up from the chart's first bar." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompCvd, DisplayName = "Volume Delta", DisplayType = ComponentDisplayType.Histogram,
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350",
                            DefaultColorSource = ColorSource.Value, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = new[] { "Zero" },
                            SpeechTemplate = "Volume delta {value:F0}." },
                },
            },
        };

        public List<LevelDescriptor> GetDefaultLevels(string code) => VolumeFlowMath.ZeroLevel();

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var cvd = Compute(data, ReadAnchor(parameters), out _);
            var span = buffer.GetComponentSpan(CompCvd);
            for (int i = 0; i < data.Length; i++) span[i] = cvd[i];
        }

        public enum Anchor { Auto, Day, Week, Month, Year, None }

        /// <summary>The running delta at each bar, NaN in a period the chart starts partway through.
        /// <paramref name="resolved"/> is the anchor Auto became.</summary>
        internal static double[] Compute(ReadOnlySpan<Ohlcv> data, Anchor anchor, out Anchor resolved)
        {
            int n = data.Length;
            var cvd = new double[n];
            Array.Fill(cvd, double.NaN);
            resolved = anchor == Anchor.Auto ? AutoAnchor(data) : anchor;
            if (n == 0) return cvd;

            // The first period counts only if the chart's first bar is that period's first moment.
            bool counting = resolved == Anchor.None || data[0].Date == PeriodStart(data[0].Date, resolved);
            double run = 0;
            for (int i = 0; i < n; i++)
            {
                if (i > 0 && PeriodStart(data[i].Date, resolved) != PeriodStart(data[i - 1].Date, resolved))
                {
                    counting = true;
                    run = 0;
                }
                if (!counting) continue;
                run += VolumeFlowMath.SignedVolume(data, i);
                cvd[i] = run;
            }
            return cvd;
        }

        /// <summary>The day on an intraday chart, the month on a daily one, the year on a weekly one.
        /// Read from the shortest gap between the first bars, as weekends and holidays only lengthen gaps.</summary>
        internal static Anchor AutoAnchor(ReadOnlySpan<Ohlcv> data)
        {
            TimeSpan step = VolumeFlowMath.BarStep(data);
            if (step < TimeSpan.FromDays(1)) return Anchor.Day;
            if (step < TimeSpan.FromDays(7)) return Anchor.Month;
            return Anchor.Year;
        }

        internal static DateTime PeriodStart(DateTime t, Anchor anchor) => anchor switch
        {
            Anchor.Day => t.Date,
            Anchor.Week => t.Date.AddDays(-(((int)t.DayOfWeek + 6) % 7)),   // Monday
            Anchor.Month => new DateTime(t.Year, t.Month, 1, 0, 0, 0, t.Kind),
            Anchor.Year => new DateTime(t.Year, 1, 1, 0, 0, 0, t.Kind),
            _ => DateTime.MinValue,
        };

        internal static Anchor ReadAnchor(Dictionary<string, object>? p)
        {
            string s = p != null && p.TryGetValue(ParamAnchor, out var raw) ? Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "" : "";
            s = s.Trim();
            foreach (Anchor a in Enum.GetValues<Anchor>())
                if (string.Equals(s, a.ToString(), StringComparison.OrdinalIgnoreCase)) return a;
            // TradingView's spellings.
            return s.ToUpperInvariant() switch
            {
                "1D" or "D" or "SESSION" or "DAILY" => Anchor.Day,
                "1W" or "W" or "WEEKLY" => Anchor.Week,
                "1M" or "M" or "MONTHLY" => Anchor.Month,
                "12M" or "1Y" or "Y" or "YEARLY" => Anchor.Year,
                _ => Anchor.Auto,
            };
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 1;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return "";
            var anchor = ReadAnchor(parameters);
            double v = At(calculatedResults, CompCvd, index);
            var resolved = anchor == Anchor.Auto ? AutoAnchor(data) : anchor;
            string since = SinceWords(data[index].Date, resolved);
            if (double.IsNaN(v)) return $"Volume delta: the chart starts partway through this {PeriodWord(resolved)}, so it has no total yet.";

            var sb = new StringBuilder("Volume delta ").Append(VolumeFlowMath.SignedSpoken(v)).Append(' ').Append(since);
            sb.Append(v > 0 ? ": buyers ahead." : v < 0 ? ": sellers ahead." : ": even.");
            double bar = VolumeFlowMath.SignedVolume(data, index);
            if (bar != 0) sb.Append(" This bar ").Append(VolumeFlowMath.SignedSpoken(bar)).Append('.');
            return sb.ToString();
        }

        private static string SinceWords(DateTime t, Anchor a) => a switch
        {
            Anchor.Day => "since the day opened",
            Anchor.Week => "since Monday",
            Anchor.Month => "since the 1st of " + t.ToString("MMMM", CultureInfo.InvariantCulture),
            Anchor.Year => "since the start of " + t.Year.ToString(CultureInfo.InvariantCulture),
            _ => "since the chart's first bar",
        };

        private static string PeriodWord(Anchor a) => a switch
        {
            Anchor.Day => "day", Anchor.Week => "week", Anchor.Month => "month", Anchor.Year => "year", _ => "period",
        };

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName is CompCvd or "Volume Delta"
                ? $"Volume delta {VolumeFlowMath.SignedSpoken(value)}{(value > 0 ? ", buyers ahead." : value < 0 ? ", sellers ahead." : ".")}"
                : null;
        }

        private static double At(IReadOnlyDictionary<string, double[]> data, string key, int index)
            => data.TryGetValue(key, out var arr) && index >= 0 && index < arr.Length ? arr[index] : double.NaN;
    }

    /// <summary>
    /// The Klinger oscillator, as TradingView's built-in computes it: each bar's volume signed by
    /// whether the typical price (high + low + close) / 3 rose or fell, and the difference between
    /// a fast and a slow EMA of that, with an EMA of the difference as its signal line.
    /// <code>
    ///   sv     = volume when hlc3 &gt;= hlc3 of the bar before, else −volume
    ///   klinger = EMA(sv, 34) − EMA(sv, 55)
    ///   signal  = EMA(klinger, 13)
    /// </code>
    /// <para>
    /// Stephen Klinger's original weights the volume by a "cumulative measurement" of the range
    /// (Skender's GetKvo does that); TradingView's built-in, which most people see, is the plain form
    /// above, so it is the one here. The EMAs are recursive, so a tick recomputes the series.
    /// The cross marks sit on the bar whose close made the cross.
    /// </para>
    /// </summary>
    public sealed class KlingerOscillatorProvider : IIndicatorProvider
    {
        public const string Code = "KLINGER";
        public const string CompKlinger = "Klinger";
        public const string CompSignal = "Signal";
        public const string CompCrossUp = "CrossUp";
        public const string CompCrossDown = "CrossDown";

        public const string ParamFast = "fastPeriods";
        public const string ParamSlow = "slowPeriods";
        public const string ParamSignal = "signalPeriods";

        public string Name => "Klinger Oscillator";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Klinger Oscillator", Category = "Volume", DefaultPane = "Pane_Klinger",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "Volume signed by whether the typical price rose or fell, as a fast EMA minus a slow one " +
                    "(34 and 55), with a 13-bar signal line. Above zero, volume has been coming in on up bars.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamFast, DisplayName = "Fast Length", DataType = typeof(int),
                            DefaultValue = 34, MinValue = 1, MaxValue = 500, Step = 1 },
                    new() { Name = ParamSlow, DisplayName = "Slow Length", DataType = typeof(int),
                            DefaultValue = 55, MinValue = 1, MaxValue = 500, Step = 1 },
                    new() { Name = ParamSignal, DisplayName = "Signal Length", DataType = typeof(int),
                            DefaultValue = 13, MinValue = 1, MaxValue = 500, Step = 1 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompKlinger, DisplayName = "Klinger", DefaultColorHex = "#2962FF",
                            DefaultThickness = 1.5f, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = new[] { "Zero" },
                            SpeechTemplate = "Klinger {value:F0}." },
                    new() { Name = CompSignal, DisplayName = "Klinger Signal", DefaultColorHex = "#43A047",
                            DefaultThickness = 1f, SubscribedLevelNames = Array.Empty<string>(),
                            SpeechTemplate = "Klinger signal {value:F0}." },
                    new() { Name = CompCrossUp, DisplayName = "Klinger Crossed Above Signal",
                            DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultColorHex = "#43A047", DefaultThickness = 4f,
                            SubscribedLevelNames = Array.Empty<string>(),
                            DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 660,
                            DefaultSignalSpeechTemplate = "Klinger crossed above its signal." },
                    new() { Name = CompCrossDown, DisplayName = "Klinger Crossed Below Signal",
                            DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultColorHex = "#EF5350", DefaultThickness = 4f,
                            SubscribedLevelNames = Array.Empty<string>(),
                            DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 330,
                            DefaultSignalSpeechTemplate = "Klinger crossed below its signal." },
                },
            },
        };

        public List<LevelDescriptor> GetDefaultLevels(string code) => VolumeFlowMath.ZeroLevel();

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var (fast, slow, signal) = ReadOptions(parameters);
            var (k, s) = Compute(data, fast, slow, signal);
            var ks = buffer.GetComponentSpan(CompKlinger);
            var ss = buffer.GetComponentSpan(CompSignal);
            var up = buffer.GetComponentSpan(CompCrossUp);
            var down = buffer.GetComponentSpan(CompCrossDown);
            for (int i = 0; i < data.Length; i++)
            {
                ks[i] = k[i];
                ss[i] = s[i];
                double was = i > 0 ? k[i - 1] - s[i - 1] : double.NaN;
                double now = k[i] - s[i];
                up[i] = was <= 0 && now > 0 ? k[i] : double.NaN;
                down[i] = was >= 0 && now < 0 ? k[i] : double.NaN;
            }
        }

        internal static (double[] Klinger, double[] Signal) Compute(ReadOnlySpan<Ohlcv> data, int fast, int slow, int signal)
        {
            int n = data.Length;
            var sv = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (i == 0) { sv[i] = double.NaN; continue; }
                double now = (data[i].High + data[i].Low + data[i].Close) / 3;
                double before = (data[i - 1].High + data[i - 1].Low + data[i - 1].Close) / 3;
                sv[i] = now >= before ? data[i].Volume : -data[i].Volume;
            }
            var f = IndicatorMath.Ema(sv, fast);
            var s = IndicatorMath.Ema(sv, slow);
            var k = new double[n];
            for (int i = 0; i < n; i++) k[i] = f[i] - s[i];
            return (k, IndicatorMath.Ema(k, signal));
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            var (fast, slow, signal) = ReadOptions(parameters);
            return Math.Max(fast, slow) * 3 + signal;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return "";
            double k = At(calculatedResults, CompKlinger, index);
            double s = At(calculatedResults, CompSignal, index);
            if (double.IsNaN(k)) return "Klinger: not yet established.";
            var sb = new StringBuilder("Klinger ").Append(VolumeFlowMath.SignedSpoken(k));
            sb.Append(k > 0 ? ", volume coming in on up bars" : k < 0 ? ", volume coming in on down bars" : "");
            if (!double.IsNaN(s))
            {
                sb.Append(k > s ? ", above its signal " : k < s ? ", below its signal " : ", on its signal ")
                  .Append(VolumeFlowMath.SignedSpoken(s));
                if (!double.IsNaN(At(calculatedResults, CompCrossUp, index))) sb.Append(", crossed above it this bar");
                else if (!double.IsNaN(At(calculatedResults, CompCrossDown, index))) sb.Append(", crossed below it this bar");
            }
            return sb.Append('.').ToString();
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompKlinger or "Klinger" => $"Klinger {VolumeFlowMath.SignedSpoken(value)}.",
                CompSignal or "Klinger Signal" => $"Klinger signal {VolumeFlowMath.SignedSpoken(value)}.",
                CompCrossUp or "Klinger Crossed Above Signal" => "Klinger crossed above its signal.",
                CompCrossDown or "Klinger Crossed Below Signal" => "Klinger crossed below its signal.",
                _ => null,
            };
        }

        private static (int Fast, int Slow, int Signal) ReadOptions(Dictionary<string, object> p)
            => (Len(p, ParamFast, 34), Len(p, ParamSlow, 55), Len(p, ParamSignal, 13));

        private static int Len(Dictionary<string, object> p, string key, int fallback)
            => (int)Math.Clamp(Math.Round(NumberParam.Get(p, key, fallback)), 1, 500);

        private static double At(IReadOnlyDictionary<string, double[]> data, string key, int index)
            => data.TryGetValue(key, out var arr) && index >= 0 && index < arr.Length ? arr[index] : double.NaN;
    }

    /// <summary>
    /// Price volume trend: a running total of each bar's volume times its close's percentage change,
    /// as TradingView's <c>ta.pvt</c>: <c>cum(change(close) / close[1] · volume)</c>. Like OBV, but a 1%
    /// rise adds a tenth of what a 10% rise does where OBV adds the whole volume for either.
    ///
    /// <para>
    /// A running total from the chart's first bar has no other start, so its level moves when older
    /// bars arrive (OBV and ADL are the same) and is listed as inherent in the prepend guard. Its
    /// slope does not: the detail says how much it rose or fell over the last 20 bars, which is the
    /// part that means something.
    /// </para>
    /// </summary>
    public sealed class PriceVolumeTrendProvider : IIndicatorProvider
    {
        public const string Code = "PVT";
        public const string CompPvt = "Pvt";
        private const int SlopeBars = 20;

        public string Name => "Price Volume Trend";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Price Volume Trend", Category = "Volume", DefaultPane = "Pane_Pvt",
                Causality = ComponentCausality.Causal,
                Description =
                    "A running total of each bar's volume times its percentage change. Like OBV, but a small " +
                    "move adds a little and a big move adds a lot.",
                Parameters = new List<IndicatorParameterMetadata>(),
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompPvt, DisplayName = "PVT", DefaultColorHex = "#2962FF", DefaultThickness = 1.5f,
                            SubscribedLevelNames = Array.Empty<string>(),
                            SpeechTemplate = "PVT {value:F0}." },
                },
            },
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var pvt = Compute(data);
            var span = buffer.GetComponentSpan(CompPvt);
            for (int i = 0; i < data.Length; i++) span[i] = pvt[i];
        }

        internal static double[] Compute(ReadOnlySpan<Ohlcv> data)
        {
            int n = data.Length;
            var pvt = new double[n];
            double run = 0;
            for (int i = 0; i < n; i++)
            {
                if (i == 0) { pvt[i] = double.NaN; continue; }
                double prev = data[i - 1].Close;
                if (prev != 0) run += (data[i].Close - prev) / prev * data[i].Volume;
                pvt[i] = run;
            }
            return pvt;
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            // A running total: the last bar is the one before plus its own step.
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 2;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return "";
            double v = At(calculatedResults, CompPvt, index);
            if (double.IsNaN(v)) return "PVT: starts on the second bar.";
            var sb = new StringBuilder("PVT ").Append(VolumeFlowMath.SignedSpoken(v));
            double then = At(calculatedResults, CompPvt, index - SlopeBars);
            if (!double.IsNaN(then))
            {
                double d = v - then;
                sb.Append(d > 0 ? ", up " : d < 0 ? ", down " : ", unchanged ");
                if (d != 0) sb.Append(QuantityFormatter.FormatSpoken(Math.Abs(d))).Append(' ');
                sb.Append("over the last ").Append(SlopeBars).Append(" bars");
            }
            return sb.Append('.').ToString();
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
            => double.IsNaN(value) || componentName is not (CompPvt or "PVT")
                ? null
                : $"PVT {VolumeFlowMath.SignedSpoken(value)}.";

        private static double At(IReadOnlyDictionary<string, double[]> data, string key, int index)
            => data.TryGetValue(key, out var arr) && index >= 0 && index < arr.Length ? arr[index] : double.NaN;
    }

    /// <summary>
    /// Relative volume at time: this bar's volume against the average volume at the same time of
    /// day over the last N days. 2 means twice the usual for this hour; 0.5, half. Volume has a daily
    /// shape (the open and the close are busy, lunch is quiet), so comparing with the bars just before
    /// calls every open unusual and every lunch dead; comparing with the same time on other days does not.
    ///
    /// <para>
    /// <b>Two modes, as TradingView's.</b> Cumulative (the default) compares the volume so far today,
    /// up to and including this bar, with the average volume up to this time on the last N days;
    /// Regular compares this bar alone with the same bar on those days. A day with no bar at that time
    /// (a gap in the data) is passed over rather than counted as zero: the average is of the last N
    /// days that had one.
    /// </para>
    ///
    /// <para>
    /// <b>On a daily or longer chart</b> there is no time of day, so it compares each bar with the
    /// average of the N bars before it. The chart's first day is left out when the chart starts
    /// partway through it, since its cumulative volume would be short. Everything at a bar comes
    /// from that bar and earlier days.
    /// </para>
    /// </summary>
    public sealed class RelativeVolumeAtTimeProvider : IIndicatorProvider
    {
        public const string Code = "RELVOL_AT_TIME";
        public const string CompRatio = "RelVol";
        public const string ParamLength = "lookbackDays";
        public const string ParamMode = "Mode";

        public string Name => "Relative Volume at Time";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Relative Volume at Time", Category = "Volume", DefaultPane = "Pane_RelVolAtTime",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "Volume against the average at the same time of day over the last 10 days: 2 is twice the " +
                    "usual for this time, 0.5 half. On a daily chart, against the average of the 10 bars before.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamLength, DisplayName = "Length (Days)", DataType = typeof(int),
                            DefaultValue = 10, MinValue = 1, MaxValue = 100, Step = 1,
                            Description = "How many earlier days the average is taken over. 10 by default." },
                    new() { Name = ParamMode, DisplayName = "Calculation Mode", DataType = typeof(string),
                            DefaultValue = "Cumulative",
                            Description = "Cumulative compares the volume so far today with the usual by this time; " +
                                          "Regular compares this bar alone with the same bar on other days." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompRatio, DisplayName = "Relative Volume", DisplayType = ComponentDisplayType.Histogram,
                            DefaultColorHex = "#7E57C2", DefaultThickness = 1f, DefaultReferenceLevel = 1.0,
                            SubscribedLevelNames = new[] { "Usual", "Twice usual" },
                            SpeechTemplate = "Relative volume {value:F2}." },
                },
            },
        };

        public List<LevelDescriptor> GetDefaultLevels(string code) => new()
        {
            new("Usual", 1.0, "#888888", DashStyle.Dash, PlayEarcon: false, EarconVolume: 0.7f),
            new("Twice usual", 2.0, "#FFB300", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var (length, cumulative) = ReadOptions(parameters);
            var r = Compute(data, length, cumulative);
            var span = buffer.GetComponentSpan(CompRatio);
            for (int i = 0; i < data.Length; i++) span[i] = r[i];
        }

        internal static double[] Compute(ReadOnlySpan<Ohlcv> data, int length, bool cumulative)
        {
            int n = data.Length;
            var ratio = new double[n];
            Array.Fill(ratio, double.NaN);
            if (n == 0) return ratio;

            if (VolumeFlowMath.BarStep(data) >= TimeSpan.FromDays(1))
            {
                // No time of day: against the average of the bars before.
                double sum = 0;
                for (int i = 0; i < n; i++)
                {
                    if (i >= length)
                    {
                        double avg = sum / length;
                        ratio[i] = avg > 0 ? data[i].Volume / avg : double.NaN;
                        sum -= data[i - length].Volume;
                    }
                    sum += data[i].Volume;
                }
                return ratio;
            }

            // Each time of day keeps the last `length` days' values, oldest first.
            var history = new Dictionary<long, Queue<double>>();
            var today = new List<(long Tod, double Value)>();
            bool counting = data[0].Date == data[0].Date.Date;   // a first day that starts at midnight is whole
            double cum = 0;
            for (int i = 0; i < n; i++)
            {
                if (i > 0 && data[i].Date.Date != data[i - 1].Date.Date)
                {
                    if (counting) Commit(history, today, length);
                    today.Clear();
                    counting = true;
                    cum = 0;
                }
                if (!counting) continue;

                cum += data[i].Volume;
                double value = cumulative ? cum : data[i].Volume;
                long tod = data[i].Date.TimeOfDay.Ticks;
                today.Add((tod, value));
                if (history.TryGetValue(tod, out var past) && past.Count >= length)
                {
                    double avg = past.Average();
                    ratio[i] = avg > 0 ? value / avg : double.NaN;
                }
            }
            return ratio;
        }

        private static void Commit(Dictionary<long, Queue<double>> history, List<(long Tod, double Value)> day, int length)
        {
            foreach (var (tod, value) in day)
            {
                if (!history.TryGetValue(tod, out var q)) history[tod] = q = new Queue<double>();
                q.Enqueue(value);
                while (q.Count > length) q.Dequeue();
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
            => ReadOptions(parameters).Length + 1;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return "";
            var (length, cumulative) = ReadOptions(parameters);
            double v = At(calculatedResults, CompRatio, index);
            bool daily = VolumeFlowMath.BarStep(data) >= TimeSpan.FromDays(1);
            if (double.IsNaN(v))
                return daily
                    ? $"Relative volume: needs {length} bars before this one."
                    : $"Relative volume: needs {length} earlier days with a bar at {data[index].Date:HH:mm}.";
            string against = daily
                ? $"the average of the {length} bars before"
                : cumulative
                    ? $"the usual volume by {data[index].Date:HH:mm} over the last {length} days"
                    : $"the usual {data[index].Date:HH:mm} bar over the last {length} days";
            return $"Relative volume {Times(v)} {against}{(cumulative && !daily ? ", counting today so far" : "")}.";
        }

        private static string Times(double v)
            => v.ToString("0.00", CultureInfo.InvariantCulture) + " times";

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
            => double.IsNaN(value) || componentName is not (CompRatio or "Relative Volume")
                ? null
                : $"Relative volume {Times(value)} usual.";

        private static (int Length, bool Cumulative) ReadOptions(Dictionary<string, object> p)
        {
            int length = (int)Math.Clamp(Math.Round(NumberParam.Get(p, ParamLength, 10)), 1, 100);
            string mode = p != null && p.TryGetValue(ParamMode, out var raw) ? Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "" : "";
            return (length, !mode.Trim().Equals("Regular", StringComparison.OrdinalIgnoreCase));
        }

        private static double At(IReadOnlyDictionary<string, double[]> data, string key, int index)
            => data.TryGetValue(key, out var arr) && index >= 0 && index < arr.Length ? arr[index] : double.NaN;
    }

    internal static class VolumeFlowMath
    {
        /// <summary>A bar's volume as buying (+) or selling (−): by its candle, a doji by its close
        /// against the last close, the Volume pane's rule for which way a bar is drawn.</summary>
        internal static double SignedVolume(ReadOnlySpan<Ohlcv> data, int i)
        {
            var b = data[i];
            if (b.Close > b.Open) return b.Volume;
            if (b.Close < b.Open) return -b.Volume;
            if (i == 0) return 0;
            double prev = data[i - 1].Close;
            return b.Close > prev ? b.Volume : b.Close < prev ? -b.Volume : 0;
        }

        /// <summary>The shortest gap between the first 20 bars: a weekend or a holiday only lengthens one.</summary>
        internal static TimeSpan BarStep(ReadOnlySpan<Ohlcv> data)
        {
            TimeSpan best = TimeSpan.MaxValue;
            for (int i = 1; i < data.Length && i <= 20; i++)
            {
                var d = data[i].Date - data[i - 1].Date;
                if (d > TimeSpan.Zero && d < best) best = d;
            }
            return best == TimeSpan.MaxValue ? TimeSpan.FromHours(1) : best;
        }

        internal static string SignedSpoken(double v)
            => v > 0 ? "plus " + QuantityFormatter.FormatSpoken(v)
             : v < 0 ? "minus " + QuantityFormatter.FormatSpoken(-v)
             : "0";

        internal static List<LevelDescriptor> ZeroLevel() => new()
        {
            new("Zero", 0.0, "#888888", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
        };
    }
}
