using System.Globalization;
using System.Text;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    // Indicator group 4, second half: five overlays drawn on the price, each as TradingView's built-in
    // computes it. Checked against the suite first: none of them was there (the bands are Bollinger,
    // Keltner and Donchian; MA_CLOUD is two averages and the cloud between them, not a ribbon of four;
    // the stops are Chandelier, Supertrend and parabolic SAR). The Least Squares Moving Average is the
    // regression channel's centre line, at the channel's length, rather than a sixth indicator.
    //
    // They share the oscillators' base for its plumbing (every component computed whole, the last bar
    // redone rather than updated), and none of them has a level: an overlay is read against the price.

    internal static class OverlayMath
    {
        /// <summary><c>ta.atr</c>: Wilder's average of the true range, seeded with the plain average of
        /// the first n. The first bar's true range is its high minus its low (<c>ta.tr(true)</c>).</summary>
        internal static double[] Atr(ReadOnlySpan<Ohlcv> data, int n)
        {
            var r = IndicatorMath.NanArray(data.Length);
            double atr = double.NaN, sum = 0;
            for (int i = 0; i < data.Length; i++)
            {
                var b = data[i];
                double tr = i == 0 ? b.High - b.Low
                    : Math.Max(b.High - b.Low, Math.Max(Math.Abs(b.High - data[i - 1].Close), Math.Abs(b.Low - data[i - 1].Close)));
                if (i < n) { sum += tr; if (i == n - 1) atr = sum / n; }
                else atr = (atr * (n - 1) + tr) / n;
                r[i] = atr;
            }
            return r;
        }

        /// <summary><c>ta.highest</c> / <c>ta.lowest</c> over the last n values, this one included;
        /// NaN while any of them is.</summary>
        internal static double[] Extreme(double[] x, int n, bool highest)
        {
            var r = IndicatorMath.NanArray(x.Length);
            for (int i = n - 1; i < x.Length; i++)
            {
                double best = highest ? double.NegativeInfinity : double.PositiveInfinity;
                bool ok = true;
                for (int j = i - n + 1; j <= i && ok; j++)
                {
                    if (double.IsNaN(x[j])) ok = false;
                    else best = highest ? Math.Max(best, x[j]) : Math.Min(best, x[j]);
                }
                if (ok) r[i] = best;
            }
            return r;
        }

        internal static double[] Highs(ReadOnlySpan<Ohlcv> data)
        {
            var r = new double[data.Length];
            for (int i = 0; i < data.Length; i++) r[i] = data[i].High;
            return r;
        }

        internal static double[] Lows(ReadOnlySpan<Ohlcv> data)
        {
            var r = new double[data.Length];
            for (int i = 0; i < data.Length; i++) r[i] = data[i].Low;
            return r;
        }

        internal static string MaType(Dictionary<string, object>? p, string key, string fallback)
        {
            string s = p != null && p.TryGetValue(key, out var raw) && raw != null
                ? (Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "").Trim().ToUpperInvariant() : "";
            return Array.IndexOf(MovingAverageHelper.SupportedTypes, s) >= 0 ? s : fallback;
        }

        internal static IndicatorParameterMetadata MaTypeParam(string name, string fallback) => new()
        {
            Name = name, DisplayName = "Average Type", DataType = typeof(string), DefaultValue = fallback,
            Description = "SMA, EMA, WMA, HMA, DEMA or TEMA. " + fallback + " by default.",
        };

        /// <summary>A mark and a tone for the bar the close breaks out of the top of a band (high
        /// tone) and out of the bottom of it (low tone).</summary>
        internal static IEnumerable<IndicatorComponentMetadata> BreakMarks(string up, string upDisplay, string upSpeech,
            string down, string downDisplay, string downSpeech)
        {
            yield return new() { Name = up, DisplayName = upDisplay,
                DisplayType = ComponentDisplayType.TriangleUp, Role = ComponentRole.Signal,
                DefaultMarkerAnchor = MarkerAnchor.BelowBar,
                DefaultColorHex = "#43A047", DefaultThickness = 8f, SubscribedLevelNames = Array.Empty<string>(),
                DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 660,
                DefaultSignalSpeechTemplate = upSpeech };
            yield return new() { Name = down, DisplayName = downDisplay,
                DisplayType = ComponentDisplayType.TriangleDown, Role = ComponentRole.Signal,
                DefaultMarkerAnchor = MarkerAnchor.AboveBar,
                DefaultColorHex = "#EF5350", DefaultThickness = 8f, SubscribedLevelNames = Array.Empty<string>(),
                DefaultEnvelopeType = "Ping", DefaultBaseFrequency = 330,
                DefaultSignalSpeechTemplate = downSpeech };
        }

        internal static IndicatorComponentMetadata PriceLine(string name, string display, string colour,
            ComponentRole role, string speech, float thickness = 1.5f) => new()
        {
            Name = name, DisplayName = display, DisplayType = ComponentDisplayType.Line, Role = role,
            DefaultColorHex = colour, DefaultThickness = thickness,
            SubscribedLevelNames = Array.Empty<string>(), SpeechTemplate = speech,
        };

        internal static string Price(double v) => SpeechPriceFormatter.FormatPrice(v);

        /// <summary>"820.50 above" / "820.50 below" / "on": the close against a line.</summary>
        internal static string Against(double close, double line)
            => close > line ? Price(close - line) + " above" : close < line ? Price(line - close) + " below" : "on";
    }

    /// <summary>
    /// Linear regression channel, as TradingView's built-in fits it: a least-squares line through the
    /// last 100 closes, and lines 2 standard deviations of the residuals above and below it
    /// (<c>√(Σ residual² / (n − 1))</c>). TradingView draws the one channel over the last 100 bars;
    /// here every bar carries where the end of the channel fitted to the 100 bars up to it was, so it
    /// can be arrowed through and never changes once the bar has closed. That end is
    /// <c>ta.linreg(close, n, 0)</c>, the Least Squares Moving Average; at 25 bars it is TradingView's
    /// LSMA. The slope (price per bar) and the fit's R (TradingView's Pearson's R, 0 to 1) are in the
    /// detail and in two components strategies can read.
    /// </summary>
    public sealed class LinearRegressionChannelProvider : OscillatorProviderBase
    {
        public const string Code = "LINREG_CHANNEL";
        public const string CompCentre = "Centre";
        public const string CompUpper = "Upper";
        public const string CompLower = "Lower";
        public const string CompSlope = "Slope";
        public const string CompFit = "FitR";
        public const string CompAbove = "ClosedAboveUpper";
        public const string CompBelow = "ClosedBelowLower";
        public const string ParamLength = "lookbackPeriods";
        public const string ParamDev = "deviations";

        public override string Name => "Linear Regression Channel";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Linear Regression Channel", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "A least-squares line through the last 100 closes and lines 2 standard deviations above and " +
                    "below it. The centre is the least squares moving average.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamLength, DisplayName = "Length", DataType = typeof(int), DefaultValue = 100,
                            MinValue = 2, MaxValue = 500, Step = 1,
                            Description = "Closes the line is fitted to. 100 by default." },
                    new() { Name = ParamDev, DisplayName = "Deviations", DataType = typeof(double), DefaultValue = 2.0,
                            MinValue = 0.1, MaxValue = 10, Step = 0.1,
                            Description = "How many standard deviations of the fit the outer lines sit from it. 2 by default." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    OverlayMath.PriceLine(CompCentre, "Regression Centre", "#2962FF", ComponentRole.Median,
                        "Regression centre {value:price}.", 2f),
                    OverlayMath.PriceLine(CompUpper, "Regression Upper", "#EF5350", ComponentRole.UpperBand,
                        "Regression upper {value:price}."),
                    OverlayMath.PriceLine(CompLower, "Regression Lower", "#26A69A", ComponentRole.LowerBand,
                        "Regression lower {value:price}."),
                    new() { Name = CompSlope, DisplayName = "Regression Slope", DisplayType = ComponentDisplayType.Line,
                            DefaultIsEnabled = false, DefaultColorHex = "#B0BEC5", DefaultThickness = 1f,
                            SubscribedLevelNames = Array.Empty<string>(), SpeechTemplate = "Regression slope {value:F2} a bar." },
                    new() { Name = CompFit, DisplayName = "Regression Fit R", DisplayType = ComponentDisplayType.Line,
                            DefaultIsEnabled = false, DefaultColorHex = "#B0BEC5", DefaultThickness = 1f,
                            SubscribedLevelNames = Array.Empty<string>(), SpeechTemplate = "Regression fit R {value:F2}." },
                }.Concat(OverlayMath.BreakMarks(
                    CompAbove, "Closed Above Regression Channel", "Closed above the regression channel.",
                    CompBelow, "Closed Below Regression Channel", "Closed below the regression channel.")).ToList(),
            },
        };

        public override List<LevelDescriptor> GetDefaultLevels(string code) => new();

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            int n = data.Length, len = Math.Max(2, Len(p, ParamLength, 100));
            double dev = Math.Clamp(NumberParam.Get(p, ParamDev, 2.0), 0.1, 10);
            var close = OscMath.Closes(data);
            var mid = IndicatorMath.NanArray(n); var up = IndicatorMath.NanArray(n); var lo = IndicatorMath.NanArray(n);
            var slope = IndicatorMath.NanArray(n); var fit = IndicatorMath.NanArray(n);

            // x runs 0 (the oldest bar of the window) to len − 1 (this bar).
            double sx = len * (len - 1) / 2.0, sxx = (len - 1) * len * (2.0 * len - 1) / 6.0;
            double dx = len * sxx - sx * sx;
            for (int i = len - 1; i < n; i++)
            {
                double sy = 0, sxy = 0, syy = 0;
                for (int x = 0; x < len; x++)
                {
                    double y = close[i - len + 1 + x];
                    sy += y; sxy += x * y; syy += y * y;
                }
                double b = (len * sxy - sx * sy) / dx;
                double a = (sy - b * sx) / len;
                double ss = 0;
                for (int x = 0; x < len; x++)
                {
                    double e = close[i - len + 1 + x] - (a + b * x);
                    ss += e * e;
                }
                double sd = Math.Sqrt(ss / (len - 1));
                double end = a + b * (len - 1);
                mid[i] = end; up[i] = end + dev * sd; lo[i] = end - dev * sd;
                slope[i] = b;
                double dy = len * syy - sy * sy;
                fit[i] = dy <= 0 ? 0 : Math.Abs(len * sxy - sx * sy) / Math.Sqrt(dx * dy);
            }
            var above = OscMath.Crosses(close, up).Up;
            var below = OscMath.Crosses(close, lo).Down;
            return new() { [CompCentre] = mid, [CompUpper] = up, [CompLower] = lo, [CompSlope] = slope,
                           [CompFit] = fit, [CompAbove] = above, [CompBelow] = below };
        }

        public override int GetStabilityWindow(string code, Dictionary<string, object> p) => Len(p, ParamLength, 100);

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            int len = Math.Max(2, Len(p, ParamLength, 100));
            double mid = At(r, CompCentre, index);
            if (double.IsNaN(mid)) return $"Regression channel: needs {len} bars.";
            double up = At(r, CompUpper, index), lo = At(r, CompLower, index);
            double b = At(r, CompSlope, index), fit = At(r, CompFit, index), close = data[index].Close;
            var sb = new StringBuilder($"Regression channel over {len} bars: centre {OverlayMath.Price(mid)}, ");
            sb.Append(b > 0 ? $"rising {OverlayMath.Price(b)} a bar" : b < 0 ? $"falling {OverlayMath.Price(-b)} a bar" : "flat");
            sb.Append($", fit R {OscMath.F2(fit)}. Close {OverlayMath.Against(close, mid)} the centre");
            double sd = (up - mid) / Math.Clamp(NumberParam.Get(p, ParamDev, 2.0), 0.1, 10);
            if (sd > 0)
            {
                double z = (close - mid) / sd;
                sb.Append($", {Math.Abs(z).ToString("0.0", CultureInfo.InvariantCulture)} deviations");
            }
            sb.Append($". Upper {OverlayMath.Price(up)}, lower {OverlayMath.Price(lo)}.");
            if (!double.IsNaN(At(r, CompAbove, index))) sb.Append(" Closed above the channel this bar.");
            else if (!double.IsNaN(At(r, CompBelow, index))) sb.Append(" Closed below the channel this bar.");
            return sb.ToString();
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompCentre or "Regression Centre" => $"Regression centre {OverlayMath.Price(value)}.",
                CompUpper or "Regression Upper" => $"Regression upper {OverlayMath.Price(value)}.",
                CompLower or "Regression Lower" => $"Regression lower {OverlayMath.Price(value)}.",
                _ => null,
            };
        }
    }

    /// <summary>
    /// Chande Kroll Stop, TradingView's built-in defaults (ATR 10, coefficient 1, stop length 9):
    /// the highest high of 10 bars less one ATR, then the highest of that over 9 bars, is the stop for
    /// a long and sits under the price; the lowest low of 10 bars plus one ATR, then the lowest of that
    /// over 9 bars, is the stop for a short and sits over it. Named here by what each one protects.
    /// </summary>
    public sealed class ChandeKrollStopProvider : OscillatorProviderBase
    {
        public const string Code = "CHANDE_KROLL";
        public const string CompLong = "LongStop";
        public const string CompShort = "ShortStop";
        public const string CompBelowLong = "ClosedBelowLongStop";
        public const string CompAboveShort = "ClosedAboveShortStop";
        public const string ParamAtr = "atrPeriods";
        public const string ParamMult = "multiplier";
        public const string ParamStop = "stopPeriods";

        public override string Name => "Chande Kroll Stop";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Chande Kroll Stop", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "Two trailing stops from the last 10 bars' extremes and an ATR: the long stop under the price, " +
                    "the short stop over it.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    LengthParam(ParamAtr, "ATR Length", 10, "Bars in the extremes and the ATR. 10 by default."),
                    new() { Name = ParamMult, DisplayName = "ATR Coefficient", DataType = typeof(double), DefaultValue = 1.0,
                            MinValue = 0.1, MaxValue = 20, Step = 0.1,
                            Description = "How many ATRs each stop sits back from the extreme. 1 by default." },
                    LengthParam(ParamStop, "Stop Length", 9, "Bars the stop holds its best value. 9 by default."),
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    OverlayMath.PriceLine(CompLong, "Long Stop", "#26A69A", ComponentRole.LowerBand, "Long stop {value:price}."),
                    OverlayMath.PriceLine(CompShort, "Short Stop", "#EF5350", ComponentRole.UpperBand, "Short stop {value:price}."),
                }.Concat(OverlayMath.BreakMarks(
                    CompAboveShort, "Closed Above Short Stop", "Closed above the short stop.",
                    CompBelowLong, "Closed Below Long Stop", "Closed below the long stop.")).ToList(),
            },
        };

        public override List<LevelDescriptor> GetDefaultLevels(string code) => new();

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            int n = data.Length, len = Len(p, ParamAtr, 10), q = Len(p, ParamStop, 9);
            double x = Math.Clamp(NumberParam.Get(p, ParamMult, 1.0), 0.1, 20);
            var atr = OverlayMath.Atr(data, len);
            var hh = OverlayMath.Extreme(OverlayMath.Highs(data), len, highest: true);
            var ll = OverlayMath.Extreme(OverlayMath.Lows(data), len, highest: false);
            var firstLong = new double[n]; var firstShort = new double[n];
            for (int i = 0; i < n; i++)
            {
                firstLong[i] = hh[i] - x * atr[i];
                firstShort[i] = ll[i] + x * atr[i];
            }
            var longStop = OverlayMath.Extreme(firstLong, q, highest: true);
            var shortStop = OverlayMath.Extreme(firstShort, q, highest: false);
            var close = OscMath.Closes(data);
            return new()
            {
                [CompLong] = longStop, [CompShort] = shortStop,
                [CompBelowLong] = OscMath.Crosses(close, longStop).Down,
                [CompAboveShort] = OscMath.Crosses(close, shortStop).Up,
            };
        }

        public override int GetStabilityWindow(string code, Dictionary<string, object> p)
            => Len(p, ParamAtr, 10) * 5 + Len(p, ParamStop, 9);

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            double lng = At(r, CompLong, index), sht = At(r, CompShort, index), close = data[index].Close;
            if (double.IsNaN(lng) || double.IsNaN(sht))
                return $"Chande Kroll Stop: needs {Len(p, ParamAtr, 10) + Len(p, ParamStop, 9) - 1} bars.";
            var sb = new StringBuilder($"Long stop {OverlayMath.Price(lng)}, close {OverlayMath.Against(close, lng)} it. ");
            sb.Append($"Short stop {OverlayMath.Price(sht)}, close {OverlayMath.Against(close, sht)} it.");
            if (!double.IsNaN(At(r, CompBelowLong, index))) sb.Append(" Closed below the long stop this bar.");
            if (!double.IsNaN(At(r, CompAboveShort, index))) sb.Append(" Closed above the short stop this bar.");
            return sb.ToString();
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompLong or "Long Stop" => $"Long stop {OverlayMath.Price(value)}.",
                CompShort or "Short Stop" => $"Short stop {OverlayMath.Price(value)}.",
                _ => null,
            };
        }
    }

    /// <summary>
    /// McGinley Dynamic, TradingView's built-in (14 bars): an average that speeds up when price runs
    /// away from it and slows down when price comes back, <c>mg + (close − mg) / (n · (close / mg)⁴)</c>,
    /// started from the 14-bar EMA. The close crossing it is marked.
    /// </summary>
    public sealed class McGinleyDynamicProvider : OscillatorProviderBase
    {
        public const string Code = "MCGINLEY";
        public const string CompLine = "McGinley";
        public const string CompCrossUp = "CloseCrossedAbove";
        public const string CompCrossDown = "CloseCrossedBelow";
        public const string ParamLength = "lookbackPeriods";

        public override string Name => "McGinley Dynamic";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "McGinley Dynamic", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description = "A moving average that speeds up when price runs away from it and slows down when it comes back. 14 bars.",
                Parameters = new List<IndicatorParameterMetadata> { LengthParam(ParamLength, "Length", 14) },
                Components = new List<IndicatorComponentMetadata>
                {
                    OverlayMath.PriceLine(CompLine, "McGinley Dynamic", "#FF9800", ComponentRole.Median,
                        "McGinley {value:price}.", 2f),
                }.Concat(OverlayMath.BreakMarks(
                    CompCrossUp, "Close Crossed Above McGinley", "Close crossed above the McGinley Dynamic.",
                    CompCrossDown, "Close Crossed Below McGinley", "Close crossed below the McGinley Dynamic.")).ToList(),
            },
        };

        public override List<LevelDescriptor> GetDefaultLevels(string code) => new();

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            int n = data.Length, len = Len(p, ParamLength, 14);
            var close = OscMath.Closes(data);
            var ema = IndicatorMath.Ema(close, len);
            var mg = IndicatorMath.NanArray(n);
            for (int i = 0; i < n; i++)
            {
                double prev = i > 0 ? mg[i - 1] : double.NaN;
                if (double.IsNaN(prev) || prev <= 0 || !(close[i] > 0)) { mg[i] = ema[i]; continue; }
                // The step is 1 / (len · (close/prev)^4) of the gap. Past 1 the line overshoots the
                // close, and after a fall of about half in one bar it diverges to minus infinity
                // (TradingView's script does the same). Capped at 1: the line goes at most to the
                // close. On ordinary bars the step is far below 1 and the cap never applies.
                double step = Math.Min(1.0, 1.0 / (len * Math.Pow(close[i] / prev, 4)));
                mg[i] = prev + (close[i] - prev) * step;
            }
            var (up, down) = OscMath.Crosses(close, mg);
            return new() { [CompLine] = mg, [CompCrossUp] = up, [CompCrossDown] = down };
        }

        public override int GetStabilityWindow(string code, Dictionary<string, object> p) => Len(p, ParamLength, 14) * 6;

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            double mg = At(r, CompLine, index);
            if (double.IsNaN(mg)) return $"McGinley Dynamic: needs {Len(p, ParamLength, 14)} bars.";
            r.TryGetValue(CompLine, out var line);
            var sb = new StringBuilder($"McGinley {OverlayMath.Price(mg)}{OscMath.Trend(line, index)}, ");
            sb.Append($"close {OverlayMath.Against(data[index].Close, mg)} it.");
            if (!double.IsNaN(At(r, CompCrossUp, index))) sb.Append(" Crossed above it this bar.");
            else if (!double.IsNaN(At(r, CompCrossDown, index))) sb.Append(" Crossed below it this bar.");
            return sb.ToString();
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
            => double.IsNaN(value) || componentName is not (CompLine or "McGinley Dynamic") ? null
             : $"McGinley {OverlayMath.Price(value)}.";
    }

    /// <summary>
    /// Envelopes, TradingView's built-in "Envelope": a 20-bar average of the close and lines 10% above
    /// and below it. The average is simple by default (TradingView's "Exponential" box turns it into an
    /// EMA; here any of the six types). The close breaking out of either side is marked.
    /// </summary>
    public sealed class EnvelopesProvider : OscillatorProviderBase
    {
        public const string Code = "ENVELOPES";
        public const string CompBasis = "Basis";
        public const string CompUpper = "Upper";
        public const string CompLower = "Lower";
        public const string CompAbove = "ClosedAboveUpper";
        public const string CompBelow = "ClosedBelowLower";
        public const string ParamLength = "lookbackPeriods";
        public const string ParamPercent = "percent";
        public const string ParamType = "maType";

        public override string Name => "Envelopes";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Envelopes", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description = "A 20-bar average of the close and lines a fixed 10% above and below it.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    LengthParam(ParamLength, "Length", 20),
                    new() { Name = ParamPercent, DisplayName = "Percent", DataType = typeof(double), DefaultValue = 10.0,
                            MinValue = 0.1, MaxValue = 100, Step = 0.1,
                            Description = "How far the lines sit from the average, in percent of it. 10 by default." },
                    OverlayMath.MaTypeParam(ParamType, "SMA"),
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    OverlayMath.PriceLine(CompBasis, "Envelope Basis", "#FF6D00", ComponentRole.Median, "Envelope basis {value:price}."),
                    OverlayMath.PriceLine(CompUpper, "Envelope Upper", "#2196F3", ComponentRole.UpperBand, "Envelope upper {value:price}."),
                    OverlayMath.PriceLine(CompLower, "Envelope Lower", "#2196F3", ComponentRole.LowerBand, "Envelope lower {value:price}."),
                }.Concat(OverlayMath.BreakMarks(
                    CompAbove, "Closed Above Envelope", "Closed above the envelope.",
                    CompBelow, "Closed Below Envelope", "Closed below the envelope.")).ToList(),
            },
        };

        public override List<LevelDescriptor> GetDefaultLevels(string code) => new();

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            int n = data.Length;
            double k = Math.Clamp(NumberParam.Get(p, ParamPercent, 10.0), 0.1, 100) / 100.0;
            var close = OscMath.Closes(data);
            var basis = MovingAverageHelper.Calculate(close, Len(p, ParamLength, 20), OverlayMath.MaType(p, ParamType, "SMA"));
            var up = new double[n]; var lo = new double[n];
            for (int i = 0; i < n; i++) { up[i] = basis[i] * (1 + k); lo[i] = basis[i] * (1 - k); }
            return new()
            {
                [CompBasis] = basis, [CompUpper] = up, [CompLower] = lo,
                [CompAbove] = OscMath.Crosses(close, up).Up, [CompBelow] = OscMath.Crosses(close, lo).Down,
            };
        }

        public override int GetStabilityWindow(string code, Dictionary<string, object> p) => Len(p, ParamLength, 20) * 6;

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            double basis = At(r, CompBasis, index), up = At(r, CompUpper, index), lo = At(r, CompLower, index);
            if (double.IsNaN(basis)) return $"Envelopes: needs {Len(p, ParamLength, 20)} bars.";
            double close = data[index].Close;
            var sb = new StringBuilder($"Envelope basis {OverlayMath.Price(basis)}, upper {OverlayMath.Price(up)}, lower {OverlayMath.Price(lo)}. ");
            if (close > up) sb.Append($"Close {OverlayMath.Price(close - up)} above the upper line.");
            else if (close < lo) sb.Append($"Close {OverlayMath.Price(lo - close)} below the lower line.");
            else
            {
                double pct = up > lo ? 100 * (close - lo) / (up - lo) : 50;
                sb.Append($"Close inside, {pct.ToString("0", CultureInfo.InvariantCulture)} percent of the way up.");
            }
            if (!double.IsNaN(At(r, CompAbove, index))) sb.Append(" Broke out of the top this bar.");
            else if (!double.IsNaN(At(r, CompBelow, index))) sb.Append(" Broke out of the bottom this bar.");
            return sb.ToString();
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompBasis or "Envelope Basis" => $"Envelope basis {OverlayMath.Price(value)}.",
                CompUpper or "Envelope Upper" => $"Envelope upper {OverlayMath.Price(value)}.",
                CompLower or "Envelope Lower" => $"Envelope lower {OverlayMath.Price(value)}.",
                _ => null,
            };
        }
    }

    /// <summary>
    /// Moving Average Ribbon, TradingView's built-in defaults: four simple averages of the close, 20,
    /// 50, 100 and 200 bars. The detail reads their order from the top and where the close is among
    /// them; the first bar they stack in order (shortest on top, or shortest at the bottom) is marked.
    /// TradingView lets each line have its own type; here one type is shared by all four.
    /// </summary>
    public sealed class MovingAverageRibbonProvider : OscillatorProviderBase
    {
        public const string Code = "MA_RIBBON";
        public static readonly string[] Lines = { "Ma1", "Ma2", "Ma3", "Ma4" };
        public static readonly string[] LengthParams = { "ma1Periods", "ma2Periods", "ma3Periods", "ma4Periods" };
        private static readonly int[] DefaultLengths = { 20, 50, 100, 200 };
        private static readonly string[] Colours = { "#F6C309", "#FB9800", "#FB6500", "#F60C0C" };
        public const string CompStackedUp = "StackedUp";
        public const string CompStackedDown = "StackedDown";
        public const string ParamType = "maType";

        public override string Name => "Moving Average Ribbon";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Moving Average Ribbon", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description = "Four moving averages of the close, 20, 50, 100 and 200 bars, read as one: their order and the close among them.",
                Parameters = Enumerable.Range(0, 4)
                    .Select(k => LengthParam(LengthParams[k], $"Average {k + 1} Length", DefaultLengths[k]))
                    .Append(OverlayMath.MaTypeParam(ParamType, "SMA")).ToList(),
                Components = Enumerable.Range(0, 4)
                    .Select(k => OverlayMath.PriceLine(Lines[k], $"Ribbon Average {k + 1}", Colours[k], ComponentRole.None,
                        $"Ribbon average {k + 1} {{value:price}}."))
                    .Concat(OverlayMath.BreakMarks(
                        CompStackedUp, "Ribbon Stacked Up", "Ribbon stacked up, shortest average on top.",
                        CompStackedDown, "Ribbon Stacked Down", "Ribbon stacked down, shortest average at the bottom.")).ToList(),
            },
        };

        public override List<LevelDescriptor> GetDefaultLevels(string code) => new();

        private static int[] Lengths(Dictionary<string, object> p)
            => Enumerable.Range(0, 4).Select(k => Len(p, LengthParams[k], DefaultLengths[k])).ToArray();

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            int n = data.Length;
            var close = OscMath.Closes(data);
            var lens = Lengths(p);
            string type = OverlayMath.MaType(p, ParamType, "SMA");
            var ma = lens.Select(l => MovingAverageHelper.Calculate(close, l, type)).ToArray();
            // "Stacked" is the order of the lines by length, not by slot: shortest above the next
            // shortest, and so on, whichever slot the user put each length in.
            var order = Enumerable.Range(0, 4).OrderBy(k => lens[k]).ToArray();
            var state = new int[n];
            for (int i = 0; i < n; i++)
            {
                bool up = true, down = true;
                for (int j = 0; j < 3; j++)
                {
                    double a = ma[order[j]][i], b = ma[order[j + 1]][i];
                    if (double.IsNaN(a) || double.IsNaN(b)) { up = down = false; break; }
                    if (!(a > b)) up = false;
                    if (!(a < b)) down = false;
                }
                state[i] = up ? 1 : down ? -1 : 0;
            }
            var stackedUp = IndicatorMath.NanArray(n); var stackedDown = IndicatorMath.NanArray(n);
            for (int i = 1; i < n; i++)
            {
                if (state[i] == 1 && state[i - 1] != 1) stackedUp[i] = close[i];
                if (state[i] == -1 && state[i - 1] != -1) stackedDown[i] = close[i];
            }
            var r = new Dictionary<string, double[]> { [CompStackedUp] = stackedUp, [CompStackedDown] = stackedDown };
            for (int k = 0; k < 4; k++) r[Lines[k]] = ma[k];
            return r;
        }

        public override int GetStabilityWindow(string code, Dictionary<string, object> p) => Lengths(p).Max() * 2;

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            var lens = Lengths(p);
            var ready = Enumerable.Range(0, 4).Where(k => !double.IsNaN(At(r, Lines[k], index)))
                .OrderByDescending(k => At(r, Lines[k], index)).ToList();
            if (ready.Count == 0) return $"Moving average ribbon: needs {lens.Min()} bars.";
            var sb = new StringBuilder();
            if (IsStacked(r, lens, index, 1)) sb.Append("Ribbon stacked up");
            else if (IsStacked(r, lens, index, -1)) sb.Append("Ribbon stacked down");
            else sb.Append(ready.Count < 4 ? "Ribbon" : "Ribbon mixed");
            sb.Append(": from the top ").Append(string.Join(", ", ready.Select(k => lens[k].ToString(CultureInfo.InvariantCulture))));
            double close = data[index].Close;
            var vals = ready.Select(k => At(r, Lines[k], index)).ToList();   // highest first
            int above = vals.Count(v => close > v);
            if (above == vals.Count) sb.Append(ready.Count == 4 ? ". Close above all four" : ". Close above them all");
            else if (above == 0) sb.Append(ready.Count == 4 ? ". Close below all four" : ". Close below them all");
            else
            {
                int pos = vals.FindIndex(v => close > v);   // the first line, from the top, the close is above
                sb.Append($". Close between the {lens[ready[pos - 1]]} and the {lens[ready[pos]]}");
            }
            var missing = Enumerable.Range(0, 4).Where(k => !ready.Contains(k)).ToList();
            if (missing.Count > 0)
                sb.Append(". Not yet drawn: the ").Append(string.Join(" and the ", missing.Select(k => lens[k]))).Append("-bar");
            sb.Append('.');
            if (!double.IsNaN(At(r, CompStackedUp, index))) sb.Append(" Stacked up this bar.");
            else if (!double.IsNaN(At(r, CompStackedDown, index))) sb.Append(" Stacked down this bar.");
            return sb.ToString();
        }

        private static bool IsStacked(IReadOnlyDictionary<string, double[]> r, int[] lens, int index, int dir)
        {
            var order = Enumerable.Range(0, 4).OrderBy(k => lens[k]).ToArray();
            for (int j = 0; j < 3; j++)
            {
                double a = At(r, Lines[order[j]], index), b = At(r, Lines[order[j + 1]], index);
                if (double.IsNaN(a) || double.IsNaN(b)) return false;
                if (dir > 0 ? !(a > b) : !(a < b)) return false;
            }
            return true;
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            int k = Array.IndexOf(Lines, componentName);
            if (k < 0 && componentName.StartsWith("Ribbon Average ", StringComparison.Ordinal))
                k = int.TryParse(componentName.AsSpan(15), out int slot) ? slot - 1 : -1;
            return k is >= 0 and < 4 ? $"Ribbon average {k + 1} {OverlayMath.Price(value)}." : null;
        }
    }
}
