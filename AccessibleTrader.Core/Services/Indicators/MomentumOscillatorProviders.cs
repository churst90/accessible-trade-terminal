using System.Globalization;
using System.Text;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    // Indicator group 4, first half (Cody 2026-11-22: "work through these, adding all of the ones you
    // listed that are missing"): eight oscillators, each in its own pane, each as TradingView's
    // built-in computes it. Checked against the suite first: none of them was there. Two of the list
    // are folded in rather than added twice: Bull Bear Power is Elder-Ray's bull power plus its bear
    // power, so it is Elder-Ray's third line; and Woodies CCI is two CCIs (14 and 6), which the
    // existing CCI already draws, so it is not here at all.

    /// <summary>
    /// What the eight share: every component is computed whole from the bars (each is a short
    /// recursion or a finite window, so the last bar is as cheap to redo as to update), a zero level,
    /// and the "crossed above / below its signal" pair of marks with their tones.
    /// </summary>
    public abstract class OscillatorProviderBase : IIndicatorProvider
    {
        public abstract string Name { get; }
        public abstract List<IndicatorMetadata> GetIndicators();
        public abstract int GetStabilityWindow(string code, Dictionary<string, object> parameters);
        public abstract string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters);
        public abstract string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex);

        /// <summary>Every component's values for these bars, keyed by component name.</summary>
        internal abstract Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters);

        public virtual List<LevelDescriptor> GetDefaultLevels(string code) => OscMath.ZeroLevel();

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            foreach (var (name, values) in Compute(data, parameters))
            {
                var span = buffer.GetComponentSpan(name);
                for (int i = 0; i < data.Length; i++) span[i] = values[i];
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        protected static double At(IReadOnlyDictionary<string, double[]> data, string key, int index)
            => data.TryGetValue(key, out var arr) && index >= 0 && index < arr.Length ? arr[index] : double.NaN;

        protected static int Len(Dictionary<string, object> p, string key, int fallback, int max = 500)
            => (int)Math.Clamp(Math.Round(NumberParam.Get(p, key, fallback)), 1, max);

        protected static IndicatorParameterMetadata LengthParam(string name, string display, int value, string? description = null)
            => new() { Name = name, DisplayName = display, DataType = typeof(int), DefaultValue = value,
                       MinValue = 1, MaxValue = 500, Step = 1, Description = description };

        /// <summary>The two cross marks, a dot and a tone each, with the Klinger pair's pitches.</summary>
        protected static IEnumerable<IndicatorComponentMetadata> CrossMarks(string up, string down, string what, string against)
        {
            yield return new() { Name = up, DisplayName = $"{what} Crossed Above {against}",
                DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                DefaultColorHex = "#43A047", DefaultThickness = 4f, SubscribedLevelNames = Array.Empty<string>(),
                DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 660,
                DefaultSignalSpeechTemplate = $"{what} crossed above its {against.ToLowerInvariant()}." };
            yield return new() { Name = down, DisplayName = $"{what} Crossed Below {against}",
                DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                DefaultColorHex = "#EF5350", DefaultThickness = 4f, SubscribedLevelNames = Array.Empty<string>(),
                DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 330,
                DefaultSignalSpeechTemplate = $"{what} crossed below its {against.ToLowerInvariant()}." };
        }

        /// <summary>", above its signal 0.12, crossed above it this bar": the line against its signal,
        /// and the cross if this is its bar.</summary>
        protected static void AppendSignal(StringBuilder sb, IReadOnlyDictionary<string, double[]> r, int index,
            string line, string signal, string up, string down, string word, Func<double, string> fmt)
        {
            double v = At(r, line, index), s = At(r, signal, index);
            if (double.IsNaN(v) || double.IsNaN(s)) return;
            sb.Append(v > s ? $", above its {word} " : v < s ? $", below its {word} " : $", on its {word} ").Append(fmt(s));
            if (!double.IsNaN(At(r, up, index))) sb.Append(", crossed above it this bar");
            else if (!double.IsNaN(At(r, down, index))) sb.Append(", crossed below it this bar");
        }
    }

    internal static class OscMath
    {
        internal static List<LevelDescriptor> ZeroLevel() => new()
        {
            new("Zero", 0.0, "#888888", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
        };

        internal static double[] Hl2(ReadOnlySpan<Ohlcv> data)
        {
            var r = new double[data.Length];
            for (int i = 0; i < data.Length; i++) r[i] = (data[i].High + data[i].Low) / 2;
            return r;
        }

        internal static double[] Closes(ReadOnlySpan<Ohlcv> data)
        {
            var r = new double[data.Length];
            for (int i = 0; i < data.Length; i++) r[i] = data[i].Close;
            return r;
        }

        /// <summary>Rate of change in percent, <c>ta.roc</c>: 100 · (x − x[n]) / x[n].</summary>
        internal static double[] Roc(double[] x, int n)
        {
            var r = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
                r[i] = i < n || x[i - n] == 0 ? double.NaN : 100 * (x[i] - x[i - n]) / x[i - n];
            return r;
        }

        /// <summary><c>ta.wma</c>: weights n, n−1, … 1 from the newest bar; NaN while any input in the
        /// window is.</summary>
        internal static double[] Wma(double[] x, int n)
        {
            var r = new double[x.Length];
            double norm = n * (n + 1) / 2.0;
            for (int i = 0; i < x.Length; i++)
            {
                if (i < n - 1) { r[i] = double.NaN; continue; }
                double sum = 0;
                for (int j = 0; j < n; j++) sum += x[i - j] * (n - j);
                r[i] = sum / norm;
            }
            return r;
        }

        /// <summary><c>ta.swma</c>: the 1-2-2-1 weighted average of the last four values.</summary>
        internal static double[] Swma(double[] x)
        {
            var r = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
                r[i] = i < 3 ? double.NaN : (x[i - 3] + 2 * x[i - 2] + 2 * x[i - 1] + x[i]) / 6;
            return r;
        }

        /// <summary><c>math.sum</c> over the last n values; NaN while any of them is.</summary>
        internal static double[] Sum(double[] x, int n)
        {
            var r = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                if (i < n - 1) { r[i] = double.NaN; continue; }
                double s = 0;
                for (int j = i - n + 1; j <= i; j++) s += x[j];
                r[i] = s;
            }
            return r;
        }

        /// <summary>The bars where <paramref name="a"/> goes from at or below <paramref name="b"/> to
        /// above it (up) or from at or above to below (down), marked at <paramref name="a"/>'s value.</summary>
        internal static (double[] Up, double[] Down) Crosses(double[] a, double[] b)
        {
            int n = a.Length;
            var up = IndicatorMath.NanArray(n);
            var down = IndicatorMath.NanArray(n);
            for (int i = 1; i < n; i++)
            {
                double was = a[i - 1] - b[i - 1], now = a[i] - b[i];
                if (was <= 0 && now > 0) up[i] = a[i];
                if (was >= 0 && now < 0) down[i] = a[i];
            }
            return (up, down);
        }

        internal static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>"plus 0.42" / "minus 0.42" / "0.00": a unitless oscillator's value.</summary>
        internal static string Signed2(double v) => v > 0 ? "plus " + F2(v) : v < 0 ? "minus " + F2(-v) : "0.00";

        /// <summary>"plus 120.50": a difference in price units, at the instrument's price precision.</summary>
        internal static string SignedPrice(double v)
            => v > 0 ? "plus " + SpeechPriceFormatter.FormatPrice(v) : v < 0 ? "minus " + SpeechPriceFormatter.FormatPrice(-v) : "0";

        internal static string Trend(double[]? x, int i)
            => x is null || i < 1 || i >= x.Length || double.IsNaN(x[i]) || double.IsNaN(x[i - 1]) ? ""
             : x[i] > x[i - 1] ? ", rising" : x[i] < x[i - 1] ? ", falling" : ", flat";
    }

    /// <summary>
    /// Elder-Ray: bull power is the high minus a 13-bar EMA of the close, bear power the low minus it.
    /// Bull power above zero says buyers pushed above the average this bar; bear power below zero says
    /// sellers pushed under it. Bull Bear Power (TradingView's built-in) is the two added together,
    /// <c>high + low − 2·EMA</c>, so it is the third line here rather than an indicator of its own.
    /// </summary>
    public sealed class ElderRayProvider : OscillatorProviderBase
    {
        public const string Code = "ELDER_RAY";
        public const string CompBull = "BullPower";
        public const string CompBear = "BearPower";
        public const string CompNet = "BullBearPower";
        public const string ParamLength = "lookbackPeriods";

        public override string Name => "Elder-Ray and Bull Bear Power";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Elder-Ray and Bull Bear Power", Category = "Oscillators", DefaultPane = "Pane_ElderRay",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "Bull power is the high minus a 13-bar EMA of the close, bear power the low minus it. " +
                    "Bull Bear Power is the two added together.",
                Parameters = new List<IndicatorParameterMetadata> { LengthParam(ParamLength, "Length", 13) },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompBull, DisplayName = "Bull Power", DisplayType = ComponentDisplayType.Histogram,
                            DefaultColorHex = "#26A69A", DefaultThickness = 1f, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = Array.Empty<string>(), SpeechTemplate = "Bull power {value:F2}." },
                    new() { Name = CompBear, DisplayName = "Bear Power", DisplayType = ComponentDisplayType.Histogram,
                            DefaultColorHex = "#EF5350", DefaultThickness = 1f, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = Array.Empty<string>(), SpeechTemplate = "Bear power {value:F2}." },
                    new() { Name = CompNet, DisplayName = "Bull Bear Power", DefaultColorHex = "#2962FF",
                            DefaultThickness = 1.5f, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = new[] { "Zero" }, SpeechTemplate = "Bull bear power {value:F2}." },
                },
            },
        };

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            int n = data.Length;
            var ema = IndicatorMath.Ema(OscMath.Closes(data), Len(p, ParamLength, 13));
            var bull = new double[n]; var bear = new double[n]; var net = new double[n];
            for (int i = 0; i < n; i++)
            {
                bull[i] = data[i].High - ema[i];
                bear[i] = data[i].Low - ema[i];
                net[i] = bull[i] + bear[i];
            }
            return new() { [CompBull] = bull, [CompBear] = bear, [CompNet] = net };
        }

        public override int GetStabilityWindow(string code, Dictionary<string, object> p) => Len(p, ParamLength, 13) * 6;

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            double bull = At(r, CompBull, index), bear = At(r, CompBear, index), net = At(r, CompNet, index);
            int len = Len(p, ParamLength, 13);
            if (double.IsNaN(bull)) return $"Elder-Ray: needs {len} bars for its average.";
            return $"Bull power {OscMath.SignedPrice(bull)}: the high {Side(bull)} the {len}-bar EMA. " +
                   $"Bear power {OscMath.SignedPrice(bear)}: the low {Side(bear)} it. " +
                   $"Bull bear power {OscMath.SignedPrice(net)}.";
        }

        private static string Side(double v)
            => v > 0 ? SpeechPriceFormatter.FormatPrice(v) + " above"
             : v < 0 ? SpeechPriceFormatter.FormatPrice(-v) + " below" : "on";

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompBull or "Bull Power" => $"Bull power {OscMath.SignedPrice(value)}.",
                CompBear or "Bear Power" => $"Bear power {OscMath.SignedPrice(value)}.",
                CompNet or "Bull Bear Power" => $"Bull bear power {OscMath.SignedPrice(value)}.",
                _ => null,
            };
        }
    }

    /// <summary>
    /// Fisher Transform, as TradingView's built-in: the bar's midpoint placed in its range over the
    /// last 9 bars, smoothed, then stretched so the turns stand out (<c>½·ln((1+x)/(1−x))</c>), with
    /// yesterday's value as the trigger. Past ±1.5 is a stretched move. A flat window (no range)
    /// counts as the middle of it, where TradingView's division by zero leaves a gap for that bar and
    /// starts the recursion again from zero.
    /// </summary>
    public sealed class FisherTransformProvider : OscillatorProviderBase
    {
        public const string Code = "FISHER";
        public const string CompFisher = "Fisher";
        public const string CompTrigger = "Trigger";
        public const string CompCrossUp = "CrossUp";
        public const string CompCrossDown = "CrossDown";
        public const string ParamLength = "lookbackPeriods";

        public override string Name => "Fisher Transform";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Fisher Transform", Category = "Oscillators", DefaultPane = "Pane_Fisher",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "Where the midpoint sits in its 9-bar range, stretched so the turns stand out, with the last " +
                    "bar's value as its trigger. Past 1.5 either way is a stretched move.",
                Parameters = new List<IndicatorParameterMetadata> { LengthParam(ParamLength, "Length", 9) },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompFisher, DisplayName = "Fisher", DefaultColorHex = "#2962FF",
                            DefaultThickness = 1.5f, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = new[] { "Zero", "Stretched high", "Stretched low" },
                            SpeechTemplate = "Fisher {value:F2}." },
                    new() { Name = CompTrigger, DisplayName = "Fisher Trigger", DefaultColorHex = "#FF6D00",
                            DefaultThickness = 1f, SubscribedLevelNames = Array.Empty<string>(),
                            SpeechTemplate = "Fisher trigger {value:F2}." },
                }.Concat(CrossMarks(CompCrossUp, CompCrossDown, "Fisher", "Trigger")).ToList(),
            },
        };

        public override List<LevelDescriptor> GetDefaultLevels(string code) => new()
        {
            new("Stretched high", 1.5, "#EF5350", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
            new("Zero", 0.0, "#888888", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
            new("Stretched low", -1.5, "#26A69A", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
        };

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            int n = data.Length, len = Len(p, ParamLength, 9);
            var hl2 = OscMath.Hl2(data);
            var fish = IndicatorMath.NanArray(n);
            var trig = IndicatorMath.NanArray(n);
            double value = 0, f = 0;
            for (int i = len - 1; i < n; i++)
            {
                double hi = double.MinValue, lo = double.MaxValue;
                for (int j = i - len + 1; j <= i; j++) { hi = Math.Max(hi, hl2[j]); lo = Math.Min(lo, hl2[j]); }
                double pos = hi > lo ? (hl2[i] - lo) / (hi - lo) - 0.5 : 0;
                value = 0.66 * pos + 0.67 * value;
                // Pine's round_: past ±0.99 it is pinned at ±0.999, which also keeps the log finite.
                if (value > 0.99) value = 0.999; else if (value < -0.99) value = -0.999;
                double prev = f;
                f = 0.5 * Math.Log((1 + value) / (1 - value)) + 0.5 * prev;
                fish[i] = f;
                if (i > len - 1) trig[i] = prev;
            }
            var (up, down) = OscMath.Crosses(fish, trig);
            return new() { [CompFisher] = fish, [CompTrigger] = trig, [CompCrossUp] = up, [CompCrossDown] = down };
        }

        public override int GetStabilityWindow(string code, Dictionary<string, object> p) => Len(p, ParamLength, 9) + 60;

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            double f = At(r, CompFisher, index);
            if (double.IsNaN(f)) return $"Fisher: needs {Len(p, ParamLength, 9)} bars.";
            var sb = new StringBuilder("Fisher ").Append(OscMath.Signed2(f));
            AppendSignal(sb, r, index, CompFisher, CompTrigger, CompCrossUp, CompCrossDown, "trigger", OscMath.Signed2);
            if (f >= 1.5) sb.Append(", stretched high");
            else if (f <= -1.5) sb.Append(", stretched low");
            return sb.Append('.').ToString();
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompFisher or "Fisher" => $"Fisher {OscMath.Signed2(value)}.",
                CompTrigger or "Fisher Trigger" => $"Fisher trigger {OscMath.Signed2(value)}.",
                CompCrossUp or "Fisher Crossed Above Trigger" => "Fisher crossed above its trigger.",
                CompCrossDown or "Fisher Crossed Below Trigger" => "Fisher crossed below its trigger.",
                _ => null,
            };
        }
    }

    /// <summary>
    /// Coppock Curve, as TradingView's built-in: a 10-bar weighted average of the 14-bar and 11-bar
    /// rates of change added together. Made for monthly charts: its classic signal is the curve
    /// turning up while still below zero, which is marked with a dot and a tone.
    /// </summary>
    public sealed class CoppockCurveProvider : OscillatorProviderBase
    {
        public const string Code = "COPPOCK";
        public const string CompCoppock = "Coppock";
        public const string CompTurnUp = "TurnUpBelowZero";
        public const string ParamWma = "wmaPeriods";
        public const string ParamLong = "longRocPeriods";
        public const string ParamShort = "shortRocPeriods";

        public override string Name => "Coppock Curve";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Coppock Curve", Category = "Oscillators", DefaultPane = "Pane_Coppock",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "A 10-bar weighted average of the 14-bar and 11-bar rates of change added together. Its classic " +
                    "signal is turning up while still below zero.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    LengthParam(ParamWma, "WMA Length", 10),
                    LengthParam(ParamLong, "Long RoC Length", 14),
                    LengthParam(ParamShort, "Short RoC Length", 11),
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompCoppock, DisplayName = "Coppock", DefaultColorHex = "#2962FF",
                            DefaultThickness = 1.5f, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = new[] { "Zero" }, SpeechTemplate = "Coppock {value:F2}." },
                    new() { Name = CompTurnUp, DisplayName = "Coppock Turned Up Below Zero",
                            DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultColorHex = "#43A047", DefaultThickness = 4f, SubscribedLevelNames = Array.Empty<string>(),
                            DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 660,
                            DefaultSignalSpeechTemplate = "Coppock turned up below zero." },
                },
            },
        };

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            var close = OscMath.Closes(data);
            var a = OscMath.Roc(close, Len(p, ParamLong, 14));
            var b = OscMath.Roc(close, Len(p, ParamShort, 11));
            var sum = new double[close.Length];
            for (int i = 0; i < sum.Length; i++) sum[i] = a[i] + b[i];
            var c = OscMath.Wma(sum, Len(p, ParamWma, 10));
            var turn = IndicatorMath.NanArray(c.Length);
            // A turn needs the two bars before it: falling into the last one, rising out of it.
            for (int i = 2; i < c.Length; i++)
                if (c[i] < 0 && c[i] > c[i - 1] && c[i - 1] <= c[i - 2]) turn[i] = c[i];
            return new() { [CompCoppock] = c, [CompTurnUp] = turn };
        }

        public override int GetStabilityWindow(string code, Dictionary<string, object> p)
            => Math.Max(Len(p, ParamLong, 14), Len(p, ParamShort, 11)) + Len(p, ParamWma, 10) + 2;

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            double c = At(r, CompCoppock, index);
            if (double.IsNaN(c)) return "Coppock: not yet established.";
            var sb = new StringBuilder("Coppock ").Append(OscMath.Signed2(c))
                .Append(OscMath.Trend(r.TryGetValue(CompCoppock, out var arr) ? arr : null, index));
            if (!double.IsNaN(At(r, CompTurnUp, index))) sb.Append(", turned up below zero this bar");
            return sb.Append('.').ToString();
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompCoppock or "Coppock" => $"Coppock {OscMath.Signed2(value)}.",
                CompTurnUp or "Coppock Turned Up Below Zero" => "Coppock turned up below zero.",
                _ => null,
            };
        }
    }

    /// <summary>
    /// Know Sure Thing, as TradingView's built-in: four rates of change (10, 15, 20 and 30 bars), each
    /// smoothed (10, 10, 10 and 15 bars), weighted 1, 2, 3 and 4 and added, with a 9-bar average as its
    /// signal. Above zero, momentum is up over all four spans together.
    /// </summary>
    public sealed class KnowSureThingProvider : OscillatorProviderBase
    {
        public const string Code = "KST";
        public const string CompKst = "Kst";
        public const string CompSignal = "Signal";
        public const string CompCrossUp = "CrossUp";
        public const string CompCrossDown = "CrossDown";
        public const string ParamSignal = "signalPeriods";
        private static readonly (string Roc, string Sma, int RocLen, int SmaLen)[] Legs =
        {
            ("roc1Periods", "sma1Periods", 10, 10),
            ("roc2Periods", "sma2Periods", 15, 10),
            ("roc3Periods", "sma3Periods", 20, 10),
            ("roc4Periods", "sma4Periods", 30, 15),
        };

        public override string Name => "Know Sure Thing";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Know Sure Thing", Category = "Oscillators", DefaultPane = "Pane_Kst",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "Four smoothed rates of change (10, 15, 20 and 30 bars) weighted 1 to 4 and added, with a 9-bar " +
                    "average as its signal. Above zero, momentum is up over all four spans together.",
                Parameters = Legs.SelectMany((l, k) => new[]
                    {
                        LengthParam(l.Roc, $"RoC Length #{k + 1}", l.RocLen),
                        LengthParam(l.Sma, $"SMA Length #{k + 1}", l.SmaLen),
                    })
                    .Append(LengthParam(ParamSignal, "Signal Line Length", 9)).ToList(),
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompKst, DisplayName = "KST", DefaultColorHex = "#2962FF",
                            DefaultThickness = 1.5f, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = new[] { "Zero" }, SpeechTemplate = "KST {value:F2}." },
                    new() { Name = CompSignal, DisplayName = "KST Signal", DefaultColorHex = "#EF5350",
                            DefaultThickness = 1f, SubscribedLevelNames = Array.Empty<string>(),
                            SpeechTemplate = "KST signal {value:F2}." },
                }.Concat(CrossMarks(CompCrossUp, CompCrossDown, "KST", "Signal")).ToList(),
            },
        };

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            var close = OscMath.Closes(data);
            var kst = new double[close.Length];
            for (int k = 0; k < Legs.Length; k++)
            {
                var leg = IndicatorMath.Sma(OscMath.Roc(close, Len(p, Legs[k].Roc, Legs[k].RocLen)), Len(p, Legs[k].Sma, Legs[k].SmaLen));
                for (int i = 0; i < kst.Length; i++) kst[i] += (k + 1) * leg[i];
            }
            var sig = IndicatorMath.Sma(kst, Len(p, ParamSignal, 9));
            var (up, down) = OscMath.Crosses(kst, sig);
            return new() { [CompKst] = kst, [CompSignal] = sig, [CompCrossUp] = up, [CompCrossDown] = down };
        }

        public override int GetStabilityWindow(string code, Dictionary<string, object> p)
            => Legs.Max(l => Len(p, l.Roc, l.RocLen) + Len(p, l.Sma, l.SmaLen)) + Len(p, ParamSignal, 9);

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            double k = At(r, CompKst, index);
            if (double.IsNaN(k)) return "KST: not yet established.";
            var sb = new StringBuilder("KST ").Append(OscMath.Signed2(k));
            AppendSignal(sb, r, index, CompKst, CompSignal, CompCrossUp, CompCrossDown, "signal", OscMath.Signed2);
            return sb.Append('.').ToString();
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompKst or "KST" => $"KST {OscMath.Signed2(value)}.",
                CompSignal or "KST Signal" => $"KST signal {OscMath.Signed2(value)}.",
                CompCrossUp or "KST Crossed Above Signal" => "KST crossed above its signal.",
                CompCrossDown or "KST Crossed Below Signal" => "KST crossed below its signal.",
                _ => null,
            };
        }
    }

    /// <summary>
    /// Relative Vigor Index (not the relative volatility index), as TradingView's built-in: over the
    /// last 10 bars, how far each candle closed from its open against how far it ranged, each
    /// smoothed 1-2-2-1 over four bars, with that smoothing again as its signal. Above zero, candles
    /// have been closing above their opens.
    /// </summary>
    public sealed class RelativeVigorIndexProvider : OscillatorProviderBase
    {
        public const string Code = "RVGI";
        public const string CompRvgi = "Rvgi";
        public const string CompSignal = "Signal";
        public const string CompCrossUp = "CrossUp";
        public const string CompCrossDown = "CrossDown";
        public const string ParamLength = "lookbackPeriods";

        public override string Name => "Relative Vigor Index";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Relative Vigor Index", Category = "Oscillators", DefaultPane = "Pane_Rvgi",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "How far candles closed from their opens against how far they ranged, over 10 bars, with a " +
                    "smoothed signal. Above zero, candles have been closing above their opens.",
                Parameters = new List<IndicatorParameterMetadata> { LengthParam(ParamLength, "Length", 10) },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompRvgi, DisplayName = "RVGI", DefaultColorHex = "#008000",
                            DefaultThickness = 1.5f, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = new[] { "Zero" }, SpeechTemplate = "Vigor {value:F2}." },
                    new() { Name = CompSignal, DisplayName = "RVGI Signal", DefaultColorHex = "#FF0000",
                            DefaultThickness = 1f, SubscribedLevelNames = Array.Empty<string>(),
                            SpeechTemplate = "Vigor signal {value:F2}." },
                }.Concat(CrossMarks(CompCrossUp, CompCrossDown, "Vigor", "Signal")).ToList(),
            },
        };

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            int n = data.Length, len = Len(p, ParamLength, 10);
            var co = new double[n]; var hl = new double[n];
            for (int i = 0; i < n; i++) { co[i] = data[i].Close - data[i].Open; hl[i] = data[i].High - data[i].Low; }
            var num = OscMath.Sum(OscMath.Swma(co), len);
            var den = OscMath.Sum(OscMath.Swma(hl), len);
            var rvgi = new double[n];
            for (int i = 0; i < n; i++) rvgi[i] = den[i] > 0 ? num[i] / den[i] : double.NaN;
            var sig = OscMath.Swma(rvgi);
            var (up, down) = OscMath.Crosses(rvgi, sig);
            return new() { [CompRvgi] = rvgi, [CompSignal] = sig, [CompCrossUp] = up, [CompCrossDown] = down };
        }

        public override int GetStabilityWindow(string code, Dictionary<string, object> p) => Len(p, ParamLength, 10) + 8;

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            double v = At(r, CompRvgi, index);
            if (double.IsNaN(v)) return "Vigor: not yet established.";
            var sb = new StringBuilder("Vigor ").Append(OscMath.Signed2(v))
                .Append(v > 0 ? ", candles closing above their opens" : v < 0 ? ", candles closing below their opens" : "");
            AppendSignal(sb, r, index, CompRvgi, CompSignal, CompCrossUp, CompCrossDown, "signal", OscMath.Signed2);
            return sb.Append('.').ToString();
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompRvgi or "RVGI" => $"Vigor {OscMath.Signed2(value)}.",
                CompSignal or "RVGI Signal" => $"Vigor signal {OscMath.Signed2(value)}.",
                CompCrossUp or "Vigor Crossed Above Signal" => "Vigor crossed above its signal.",
                CompCrossDown or "Vigor Crossed Below Signal" => "Vigor crossed below its signal.",
                _ => null,
            };
        }
    }

    /// <summary>
    /// SMI Ergodic, TradingView's two built-ins (the Indicator and the Oscillator) in one: the true
    /// strength index of the close (its change, EMA-smoothed over 20 then 5 bars, over its absolute
    /// change smoothed the same way, so between −1 and 1), a 5-bar EMA of that as the signal, and the
    /// difference as columns. The TSI line is the true strength index over 100.
    /// </summary>
    public sealed class SmiErgodicProvider : OscillatorProviderBase
    {
        public const string Code = "SMI_ERGODIC";
        public const string CompSmi = "Smi";
        public const string CompSignal = "Signal";
        public const string CompOsc = "Oscillator";
        public const string CompCrossUp = "CrossUp";
        public const string CompCrossDown = "CrossDown";
        public const string ParamLong = "longPeriods";
        public const string ParamShort = "shortPeriods";
        public const string ParamSignal = "signalPeriods";

        public override string Name => "SMI Ergodic";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "SMI Ergodic", Category = "Oscillators", DefaultPane = "Pane_SmiErgodic",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                RangeMin = -1, RangeMax = 1,
                Description =
                    "The true strength index (the close's change, doubly smoothed, over its size, between minus 1 " +
                    "and 1), a 5-bar signal line, and the gap between them as columns.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    LengthParam(ParamLong, "Long Length", 20),
                    LengthParam(ParamShort, "Short Length", 5),
                    LengthParam(ParamSignal, "Signal Line Length", 5),
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompSmi, DisplayName = "SMI", DefaultColorHex = "#2962FF",
                            DefaultThickness = 1.5f, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = new[] { "Zero" }, SpeechTemplate = "SMI {value:F2}." },
                    new() { Name = CompSignal, DisplayName = "SMI Signal", DefaultColorHex = "#FF6D00",
                            DefaultThickness = 1f, SubscribedLevelNames = Array.Empty<string>(),
                            SpeechTemplate = "SMI signal {value:F2}." },
                    new() { Name = CompOsc, DisplayName = "SMI Oscillator", DisplayType = ComponentDisplayType.Histogram,
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350",
                            DefaultColorSource = ColorSource.Value, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = Array.Empty<string>(), SpeechTemplate = "SMI oscillator {value:F2}." },
                }.Concat(CrossMarks(CompCrossUp, CompCrossDown, "SMI", "Signal")).ToList(),
            },
        };

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            int n = data.Length, lng = Len(p, ParamLong, 20), sht = Len(p, ParamShort, 5);
            var pc = IndicatorMath.NanArray(n); var apc = IndicatorMath.NanArray(n);
            for (int i = 1; i < n; i++) { pc[i] = data[i].Close - data[i - 1].Close; apc[i] = Math.Abs(pc[i]); }
            var num = IndicatorMath.Ema(IndicatorMath.Ema(pc, lng), sht);
            var den = IndicatorMath.Ema(IndicatorMath.Ema(apc, lng), sht);
            var smi = new double[n];
            for (int i = 0; i < n; i++) smi[i] = den[i] > 0 ? num[i] / den[i] : double.NaN;
            var sig = IndicatorMath.Ema(smi, Len(p, ParamSignal, 5));
            var osc = new double[n];
            for (int i = 0; i < n; i++) osc[i] = smi[i] - sig[i];
            var (up, down) = OscMath.Crosses(smi, sig);
            return new() { [CompSmi] = smi, [CompSignal] = sig, [CompOsc] = osc, [CompCrossUp] = up, [CompCrossDown] = down };
        }

        public override int GetStabilityWindow(string code, Dictionary<string, object> p)
            => (Len(p, ParamLong, 20) + Len(p, ParamShort, 5) + Len(p, ParamSignal, 5)) * 6;

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            double v = At(r, CompSmi, index);
            if (double.IsNaN(v)) return "SMI: not yet established.";
            var sb = new StringBuilder("SMI ").Append(OscMath.Signed2(v));
            AppendSignal(sb, r, index, CompSmi, CompSignal, CompCrossUp, CompCrossDown, "signal", OscMath.Signed2);
            return sb.Append('.').ToString();
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompSmi or "SMI" => $"SMI {OscMath.Signed2(value)}.",
                CompSignal or "SMI Signal" => $"SMI signal {OscMath.Signed2(value)}.",
                CompOsc or "SMI Oscillator" => $"SMI oscillator {OscMath.Signed2(value)}.",
                CompCrossUp or "SMI Crossed Above Signal" => "SMI crossed above its signal.",
                CompCrossDown or "SMI Crossed Below Signal" => "SMI crossed below its signal.",
                _ => null,
            };
        }
    }

    /// <summary>
    /// Awesome Oscillator, as TradingView's built-in: a 5-bar average of the bar midpoint minus a
    /// 34-bar one, as columns, in price units. TradingView colours each column by whether it rose
    /// from the last; here the columns are coloured by sign and the speech says rising or falling.
    /// </summary>
    public sealed class AwesomeOscillatorProvider : OscillatorProviderBase
    {
        public const string Code = "AWESOME_OSC";
        public const string CompAo = "Ao";
        public const string ParamFast = "fastPeriods";
        public const string ParamSlow = "slowPeriods";

        public override string Name => "Awesome Oscillator";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Awesome Oscillator", Category = "Oscillators", DefaultPane = "Pane_Awesome",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description = "A 5-bar average of the bar midpoint minus a 34-bar one, in price units.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    LengthParam(ParamFast, "Fast Length", 5),
                    LengthParam(ParamSlow, "Slow Length", 34),
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompAo, DisplayName = "Awesome Oscillator", DisplayType = ComponentDisplayType.Histogram,
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350",
                            DefaultColorSource = ColorSource.Value, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = new[] { "Zero" }, SpeechTemplate = "Awesome {value:F2}." },
                },
            },
        };

        internal static double[] Ao(ReadOnlySpan<Ohlcv> data, int fast, int slow)
        {
            var hl2 = OscMath.Hl2(data);
            var f = IndicatorMath.Sma(hl2, fast);
            var s = IndicatorMath.Sma(hl2, slow);
            var ao = new double[hl2.Length];
            for (int i = 0; i < ao.Length; i++) ao[i] = f[i] - s[i];
            return ao;
        }

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
            => new() { [CompAo] = Ao(data, Len(p, ParamFast, 5), Len(p, ParamSlow, 34)) };

        public override int GetStabilityWindow(string code, Dictionary<string, object> p)
            => Math.Max(Len(p, ParamFast, 5), Len(p, ParamSlow, 34)) + 1;

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            double v = At(r, CompAo, index);
            if (double.IsNaN(v)) return "Awesome oscillator: not yet established.";
            return $"Awesome oscillator {OscMath.SignedPrice(v)}" +
                   OscMath.Trend(r.TryGetValue(CompAo, out var arr) ? arr : null, index) +
                   $": the {Len(p, ParamFast, 5)}-bar average midpoint " +
                   (v >= 0 ? "above" : "below") + $" the {Len(p, ParamSlow, 34)}-bar one.";
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
            => double.IsNaN(value) || componentName is not (CompAo or "Awesome Oscillator") ? null
             : $"Awesome {OscMath.SignedPrice(value)}{OscMath.Trend(all.TryGetValue(CompAo, out var a) ? a : null, dataIndex)}.";
    }

    /// <summary>
    /// Accelerator Oscillator, as TradingView's built-in: the awesome oscillator minus its own 5-bar
    /// average, so it turns before the awesome oscillator does. Columns, in price units, coloured by
    /// sign as the awesome oscillator's are.
    /// </summary>
    public sealed class AcceleratorOscillatorProvider : OscillatorProviderBase
    {
        public const string Code = "ACCELERATOR_OSC";
        public const string CompAc = "Ac";
        public const string ParamSmooth = "smoothPeriods";

        public override string Name => "Accelerator Oscillator";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Accelerator Oscillator", Category = "Oscillators", DefaultPane = "Pane_Accelerator",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description = "The awesome oscillator minus its own 5-bar average, so it turns first.",
                Parameters = new List<IndicatorParameterMetadata> { LengthParam(ParamSmooth, "Length", 5) },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompAc, DisplayName = "Accelerator Oscillator", DisplayType = ComponentDisplayType.Histogram,
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350",
                            DefaultColorSource = ColorSource.Value, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = new[] { "Zero" }, SpeechTemplate = "Accelerator {value:F2}." },
                },
            },
        };

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            var ao = AwesomeOscillatorProvider.Ao(data, 5, 34);
            var avg = IndicatorMath.Sma(ao, Len(p, ParamSmooth, 5));
            var ac = new double[ao.Length];
            for (int i = 0; i < ac.Length; i++) ac[i] = ao[i] - avg[i];
            return new() { [CompAc] = ac };
        }

        public override int GetStabilityWindow(string code, Dictionary<string, object> p) => 34 + Len(p, ParamSmooth, 5);

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            double v = At(r, CompAc, index);
            if (double.IsNaN(v)) return "Accelerator oscillator: not yet established.";
            return $"Accelerator oscillator {OscMath.SignedPrice(v)}" +
                   OscMath.Trend(r.TryGetValue(CompAc, out var arr) ? arr : null, index) +
                   (v >= 0 ? ": the awesome oscillator above its average." : ": the awesome oscillator below its average.");
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
            => double.IsNaN(value) || componentName is not (CompAc or "Accelerator Oscillator") ? null
             : $"Accelerator {OscMath.SignedPrice(value)}{OscMath.Trend(all.TryGetValue(CompAc, out var a) ? a : null, dataIndex)}.";
    }
}
