using System.Globalization;
using System.Text;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    // Indicator group 5: the levels a session leaves behind. Checked against the suite first. PIVOTS
    // works out the previous day's, week's or month's high, low and close but draws only the pivots
    // made from them, and nothing drew the session's own high and low or its opening range.
    //
    // Both read only the bars on the chart. A period or session that begins on the first loaded bar
    // may have started before it, so it draws nothing: a previous-day high from half a day would be
    // a wrong price spoken as a level, and it would change when you scrolled back and loaded the rest.

    /// <summary>
    /// The previous day's, week's and month's high, low and close, each held flat through the period
    /// after it: the lines intraday traders mark by hand every morning. Days, weeks (ISO, Monday to
    /// Sunday) and months are UTC, the same boundaries PIVOTS uses. The day's three are on by default.
    /// A close above the previous day's high, or below its low, is marked once a day.
    /// </summary>
    public sealed class PreviousPeriodLevelsProvider : OscillatorProviderBase
    {
        public const string Code = "PREV_PERIOD_LEVELS";
        public const string CompBrokeHigh = "BrokePrevDayHigh";
        public const string CompBrokeLow = "BrokePrevDayLow";

        /// <summary>The nine lines: (component, period for <see cref="PivotLevelsProvider.PeriodChanged"/>,
        /// the period's word, which of high/low/close).</summary>
        internal static readonly (string Name, string Period, string Word, char Part)[] LinesSpec =
        {
            ("PDH", "Daily", "day", 'H'), ("PDL", "Daily", "day", 'L'), ("PDC", "Daily", "day", 'C'),
            ("PWH", "Weekly", "week", 'H'), ("PWL", "Weekly", "week", 'L'), ("PWC", "Weekly", "week", 'C'),
            ("PMH", "Monthly", "month", 'H'), ("PML", "Monthly", "month", 'L'), ("PMC", "Monthly", "month", 'C'),
        };

        private static string PartWord(char part) => part switch { 'H' => "high", 'L' => "low", _ => "close" };

        /// <summary>"previous day high": the line's spoken name, here and in "Near resistance at …".</summary>
        internal static string? SpokenName(string component)
        {
            foreach (var l in LinesSpec)
                if (l.Name == component) return $"previous {l.Word} {PartWord(l.Part)}";
            return null;
        }

        public override string Name => "Previous Day, Week and Month Levels";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Previous Day, Week and Month Levels", Category = "Reference Level",
                DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Description = "The previous day's, week's and month's high, low and close, held through the period after it. " +
                    "UTC days, ISO weeks. A period that started before the first bar on the chart is left out.",
                Parameters = new List<IndicatorParameterMetadata>(),
                Components = LinesSpec.Select(l => new IndicatorComponentMetadata
                {
                    Name = l.Name,
                    DisplayName = $"Previous {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(l.Word)} {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(PartWord(l.Part))}",
                    DisplayType = ComponentDisplayType.StepLine,
                    Role = ComponentRole.None,
                    DefaultColorHex = l.Part switch { 'H' => "#EF5350", 'L' => "#26A69A", _ => "#B0BEC5" },
                    DefaultThickness = l.Word == "day" ? 1.5f : l.Word == "week" ? 2f : 2.5f,
                    DefaultIsEnabled = l.Word == "day",
                    SubscribedLevelNames = Array.Empty<string>(),
                    SpeechTemplate = $"Previous {l.Word} {PartWord(l.Part)} {{value:price}}.",
                }).Concat(OverlayMath.BreakMarks(
                    CompBrokeHigh, "Closed Above Previous Day High", "Closed above the previous day's high.",
                    CompBrokeLow, "Closed Below Previous Day Low", "Closed below the previous day's low.")).ToList(),
            },
        };

        public override List<LevelDescriptor> GetDefaultLevels(string code) => new();

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            int n = data.Length;
            var r = new Dictionary<string, double[]>();
            foreach (var period in new[] { "Daily", "Weekly", "Monthly" })
            {
                var spec = LinesSpec.Where(l => l.Period == period).ToArray();
                var hi = IndicatorMath.NanArray(n); var lo = IndicatorMath.NanArray(n); var cl = IndicatorMath.NanArray(n);
                // The period in progress: where it began, and its high and low so far.
                int start = 0;
                double runH = double.NegativeInfinity, runL = double.PositiveInfinity;
                double prevH = double.NaN, prevL = double.NaN, prevC = double.NaN;
                for (int i = 0; i < n; i++)
                {
                    if (i > 0 && PivotLevelsProvider.PeriodChanged(data[i - 1].Date, data[i].Date, period))
                    {
                        // The period that just ended counts only if it began after the first bar.
                        bool whole = start > 0;
                        prevH = whole ? runH : double.NaN;
                        prevL = whole ? runL : double.NaN;
                        prevC = whole ? data[i - 1].Close : double.NaN;
                        start = i;
                        runH = double.NegativeInfinity; runL = double.PositiveInfinity;
                    }
                    runH = Math.Max(runH, data[i].High);
                    runL = Math.Min(runL, data[i].Low);
                    hi[i] = prevH; lo[i] = prevL; cl[i] = prevC;
                }
                r[spec[0].Name] = hi; r[spec[1].Name] = lo; r[spec[2].Name] = cl;
            }

            // A close beyond the previous day's high or low, the first time in the day.
            var up = IndicatorMath.NanArray(n); var down = IndicatorMath.NanArray(n);
            bool brokeUp = false, brokeDown = false;
            var pdh = r["PDH"]; var pdl = r["PDL"];
            for (int i = 0; i < n; i++)
            {
                if (i > 0 && PivotLevelsProvider.PeriodChanged(data[i - 1].Date, data[i].Date, "Daily"))
                    brokeUp = brokeDown = false;
                double c = data[i].Close;
                if (!brokeUp && !double.IsNaN(pdh[i]) && c > pdh[i]) { up[i] = c; brokeUp = true; }
                if (!brokeDown && !double.IsNaN(pdl[i]) && c < pdl[i]) { down[i] = c; brokeDown = true; }
            }
            r[CompBrokeHigh] = up; r[CompBrokeLow] = down;
            return r;
        }

        // Two month boundaries at most: the month that ended and the one before it, on hourly bars.
        public override int GetStabilityWindow(string code, Dictionary<string, object> p) => 2 * 31 * 24;

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            double close = data[index].Close;
            var sb = new StringBuilder();
            double h = At(r, "PDH", index), l = At(r, "PDL", index), c = At(r, "PDC", index);
            if (double.IsNaN(h))
                sb.Append("Previous day: needs a whole day of bars on the chart; scroll back to load one.");
            else
            {
                sb.Append($"Previous day high {OverlayMath.Price(h)}, low {OverlayMath.Price(l)}, close {OverlayMath.Price(c)}. ");
                if (close > h) sb.Append($"Close {OverlayMath.Price(close - h)} above its high.");
                else if (close < l) sb.Append($"Close {OverlayMath.Price(l - close)} below its low.");
                else sb.Append($"Close inside it, {OverlayMath.Price(h - close)} under the high, {OverlayMath.Price(close - l)} over the low.");
            }
            foreach (var (word, hk, lk) in new[] { ("week", "PWH", "PWL"), ("month", "PMH", "PML") })
            {
                double wh = At(r, hk, index), wl = At(r, lk, index);
                if (!double.IsNaN(wh)) sb.Append($" Previous {word} high {OverlayMath.Price(wh)}, low {OverlayMath.Price(wl)}.");
            }
            if (!double.IsNaN(At(r, CompBrokeHigh, index))) sb.Append(" Closed above the previous day's high this bar.");
            if (!double.IsNaN(At(r, CompBrokeLow, index))) sb.Append(" Closed below the previous day's low this bar.");
            return sb.ToString();
        }

        public override string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> all, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            var name = SpokenName(componentName);
            return name == null ? null : $"{char.ToUpperInvariant(name[0])}{name[1..]} {OverlayMath.Price(value)}.";
        }
    }

    /// <summary>
    /// The session's high and low so far, and its opening range: the high and low of its first
    /// minutes (30 by default), drawn from the bar the range is complete. The session is the UTC day
    /// (crypto) or New York's 9:30 to 16:00 (US stocks; bars outside it have none). The first close
    /// above the range, and the first below it, are marked once a session.
    /// </summary>
    public sealed class SessionRangeProvider : OscillatorProviderBase
    {
        public const string Code = "SESSION_RANGE";
        public const string CompHigh = "SessionHigh";
        public const string CompLow = "SessionLow";
        public const string CompOrHigh = "OpeningRangeHigh";
        public const string CompOrLow = "OpeningRangeLow";
        public const string CompBreakUp = "BrokeAboveOpeningRange";
        public const string CompBreakDown = "BrokeBelowOpeningRange";
        public const string ParamSession = "session";
        public const string ParamMinutes = "rangeMinutes";
        public const string SessionUtc = "UTC day";
        public const string SessionNewYork = "New York";

        public override string Name => "Session High, Low and Opening Range";

        public override List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code, Name = "Session High, Low and Opening Range", Category = "Reference Level",
                DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Description = "The session's high and low so far, and the high and low of its first minutes. " +
                    "A session that started before the first bar on the chart is left out.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamSession, DisplayName = "Session", DataType = typeof(string), DefaultValue = SessionUtc,
                            Description = "\"UTC day\" (midnight to midnight UTC) or \"New York\" (9:30 to 16:00 New York time)." },
                    new() { Name = ParamMinutes, DisplayName = "Opening Range Minutes", DataType = typeof(int), DefaultValue = 30,
                            MinValue = 1, MaxValue = 600, Step = 5,
                            Description = "How long the opening range lasts from the session's start. 30 by default." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    Line(CompHigh, "Session High", "#EF9A9A", "Session high {value:price}.", 1f),
                    Line(CompLow, "Session Low", "#80CBC4", "Session low {value:price}.", 1f),
                    Line(CompOrHigh, "Opening Range High", "#FF7043", "Opening range high {value:price}.", 2f),
                    Line(CompOrLow, "Opening Range Low", "#26A69A", "Opening range low {value:price}.", 2f),
                }.Concat(OverlayMath.BreakMarks(
                    CompBreakUp, "Broke Above Opening Range", "Closed above the opening range.",
                    CompBreakDown, "Broke Below Opening Range", "Closed below the opening range.")).ToList(),
            },
        };

        private static IndicatorComponentMetadata Line(string name, string display, string colour, string speech, float thickness) => new()
        {
            Name = name, DisplayName = display, DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.None,
            DefaultColorHex = colour, DefaultThickness = thickness,
            SubscribedLevelNames = Array.Empty<string>(), SpeechTemplate = speech,
        };

        public override List<LevelDescriptor> GetDefaultLevels(string code) => new();

        private static bool NewYork(Dictionary<string, object>? p)
            => p != null && p.TryGetValue(ParamSession, out var v) && v != null
               && (Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").Trim()
                    .StartsWith("new", StringComparison.OrdinalIgnoreCase);

        private static DateTime Utc(DateTime d) => d.Kind == DateTimeKind.Utc ? d
            : d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);

        /// <summary>The session a bar is in (its start, in UTC), or null for a bar outside New York's hours.</summary>
        internal static DateTime? SessionStart(DateTime barTime, bool newYork)
        {
            var t = Utc(barTime);
            if (!newYork) return t.Date;
            var et = TimeZoneInfo.ConvertTimeFromUtc(t, ExchangeTime.UsEastern);
            var tod = et.TimeOfDay;
            if (tod < new TimeSpan(9, 30, 0) || tod >= new TimeSpan(16, 0, 0)) return null;
            return ExchangeTime.EasternWallClockToUtc(et.Date.AddHours(9.5));
        }

        internal override Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object> p)
        {
            int n = data.Length;
            bool ny = NewYork(p);
            var minutes = TimeSpan.FromMinutes(Math.Clamp(Math.Round(NumberParam.Get(p, ParamMinutes, 30)), 1, 600));
            var hi = IndicatorMath.NanArray(n); var lo = IndicatorMath.NanArray(n);
            var orH = IndicatorMath.NanArray(n); var orL = IndicatorMath.NanArray(n);
            var up = IndicatorMath.NanArray(n); var down = IndicatorMath.NanArray(n);

            DateTime? current = null;
            bool whole = false, brokeUp = false, brokeDown = false;
            double runH = 0, runL = 0, rangeH = double.NegativeInfinity, rangeL = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                var start = SessionStart(data[i].Date, ny);
                if (start == null) { current = null; continue; }
                if (start != current)
                {
                    current = start;
                    // A session whose first bar is the chart's first bar may have begun before it,
                    // and one first seen after its range ended has no range to measure.
                    whole = i > 0;
                    runH = double.NegativeInfinity; runL = double.PositiveInfinity;
                    rangeH = double.NegativeInfinity; rangeL = double.PositiveInfinity;
                    brokeUp = brokeDown = false;
                }
                if (!whole) continue;
                var b = data[i];
                runH = Math.Max(runH, b.High); runL = Math.Min(runL, b.Low);
                hi[i] = runH; lo[i] = runL;
                if (Utc(b.Date) < start.Value + minutes)
                {
                    rangeH = Math.Max(rangeH, b.High); rangeL = Math.Min(rangeL, b.Low);
                    continue;
                }
                if (double.IsInfinity(rangeH)) continue;   // the session's first bar came after the range
                orH[i] = rangeH; orL[i] = rangeL;
                if (!brokeUp && b.Close > rangeH) { up[i] = b.Close; brokeUp = true; }
                if (!brokeDown && b.Close < rangeL) { down[i] = b.Close; brokeDown = true; }
            }
            return new()
            {
                [CompHigh] = hi, [CompLow] = lo, [CompOrHigh] = orH, [CompOrLow] = orL,
                [CompBreakUp] = up, [CompBreakDown] = down,
            };
        }

        // A session is at most a day; two of them on hourly bars.
        public override int GetStabilityWindow(string code, Dictionary<string, object> p) => 48;

        public override string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> r, int index, Dictionary<string, object> p)
        {
            if (index < 0 || index >= data.Length) return "";
            bool ny = NewYork(p);
            if (SessionStart(data[index].Date, ny) == null) return "Outside the New York session.";
            double h = At(r, CompHigh, index), l = At(r, CompLow, index);
            if (double.IsNaN(h)) return "Session: began before the first bar on the chart; scroll back to load it.";
            int mins = (int)Math.Clamp(Math.Round(NumberParam.Get(p, ParamMinutes, 30)), 1, 600);
            var sb = new StringBuilder($"Session high {OverlayMath.Price(h)}, low {OverlayMath.Price(l)}. ");
            double oh = At(r, CompOrHigh, index), ol = At(r, CompOrLow, index);
            if (double.IsNaN(oh))
            {
                var start = SessionStart(data[index].Date, ny)!.Value;
                sb.Append(Utc(data[index].Date) < start.AddMinutes(mins)
                    ? $"Opening range still forming, the first {mins} minutes."
                    : "No opening range: the session's first bars are not on the chart.");
            }
            else
            {
                double close = data[index].Close;
                sb.Append($"Opening range high {OverlayMath.Price(oh)}, low {OverlayMath.Price(ol)}; close ");
                sb.Append(close > oh ? $"{OverlayMath.Price(close - oh)} above it."
                        : close < ol ? $"{OverlayMath.Price(ol - close)} below it." : "inside it.");
            }
            if (!double.IsNaN(At(r, CompBreakUp, index))) sb.Append(" Broke above the opening range this bar.");
            if (!double.IsNaN(At(r, CompBreakDown, index))) sb.Append(" Broke below the opening range this bar.");
            return sb.ToString();
        }

        /// <summary>"session high", "opening range low": a line's spoken name, here and in "Near resistance at …".</summary>
        internal static string? SpokenName(string component) => component switch
        {
            CompHigh => "session high", CompLow => "session low",
            CompOrHigh => "opening range high", CompOrLow => "opening range low",
            _ => null,
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
