using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Pivot Levels — classic floor-trader pivots (PP / R1-R3 / S1-S3) and
    /// Camarilla pivots (H1-H4 / L1-L4) computed from the prior period's
    /// HLC. Period defaults to "Daily" (re-computes each new UTC day);
    /// works on intraday charts (4h, 1h, 15m) where daily pivots are the
    /// natural reference levels traders watch.
    ///
    /// Why classic + Camarilla: classic pivots are the canonical support/
    /// resistance from prior session HLC and the most-tracked levels in
    /// equities and forex. Camarilla H3/L3 are particularly tight reversal
    /// levels (tight stop, high R/R), and H4/L4 are breakout levels.
    /// They're independent enough to add signal even if classic pivots
    /// already mark a zone.
    ///
    /// Components (overlay lines on the price pane):
    ///   PP, R1, R2, R3, S1, S2, S3 — classic
    ///   CamH3, CamH4, CamL3, CamL4 — most-actionable Camarilla levels
    ///   AtPivotZone               — discrete -1/0/+1: -1 = price at S-zone, +1 at R-zone
    /// </summary>
    public sealed class PivotLevelsProvider : IIndicatorProvider
    {
        public string Name => "Pivot Levels";

        public const string Code      = "PIVOTS";
        public const string CompPP    = "PP";
        public const string CompR1    = "R1";
        public const string CompR2    = "R2";
        public const string CompR3    = "R3";
        public const string CompS1    = "S1";
        public const string CompS2    = "S2";
        public const string CompS3    = "S3";
        public const string CompCamH3 = "Cam H3";
        public const string CompCamH4 = "Cam H4";
        public const string CompCamL3 = "Cam L3";
        public const string CompCamL4 = "Cam L4";
        public const string CompZone  = "Pivot Zone";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = Code,
                Causality = ComponentCausality.Causal,
                Name        = "Pivot Levels",
                Category    = "Reference Level",
                DefaultPane = "Main",
                Description =
                    "Classic floor-trader pivots (PP / R1-R3 / S1-S3) plus Camarilla H3/H4/" +
                    "L3/L4 from the prior session's HLC. Re-computes each new period (default " +
                    "daily). Pivot Zone component is +1 when price is near R1-R3, -1 when near " +
                    "S1-S3 — useful for strategy gating to fire entries near support/resistance.",
                RequiresFullRecalcOnTick = false,

                Components = new List<IndicatorComponentMetadata>
                {
                    PivotLine(CompPP, "Pivot Point",  "#FFFFFF", true),
                    PivotLine(CompR1, "R1",           "#FF7043", true),
                    PivotLine(CompR2, "R2",           "#EF5350", true),
                    PivotLine(CompR3, "R3",           "#C62828", false),
                    PivotLine(CompS1, "S1",           "#26A69A", true),
                    PivotLine(CompS2, "S2",           "#43A047", true),
                    PivotLine(CompS3, "S3",           "#1B5E20", false),
                    PivotLine(CompCamH3, "Cam H3",    "#FFB300", false),
                    PivotLine(CompCamH4, "Cam H4",    "#FF8F00", false),
                    PivotLine(CompCamL3, "Cam L3",    "#9CCC65", false),
                    PivotLine(CompCamL4, "Cam L4",    "#558B2F", false),
                    new() { Name = CompZone, DisplayName = "Pivot Zone",
                            DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFFFFF", DefaultThickness = 1.0f,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultReferenceLevel = 0.0,
                            SpeechTemplate = "Zone {value:F0}.",
                            IsVisible = false },
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "Period", DisplayName = "Pivot Period",
                            DataType = typeof(string), DefaultValue = "Daily",
                            Description = "Daily / Weekly / Monthly. Daily is the standard for intraday TFs." },
                    new() { Name = "ZoneAtrTolerance", DisplayName = "Zone Tolerance (ATR)",
                            DataType = typeof(double), DefaultValue = 0.5,
                            Description = "How close to a pivot level (in ATR units) counts as 'at' it." },
                    new() { Name = "AtrPeriod", DisplayName = "ATR Period",
                            DataType = typeof(int), DefaultValue = 14.0 },
                }
            }
        };

        public List<LevelDescriptor> GetDefaultLevels(string code) => new();

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            string period = GetStr(parameters, "Period", "Daily");
            double zoneAtr = Math.Max(0.0, GetDbl(parameters, "ZoneAtrTolerance", 0.5));
            int atrPeriod = Math.Max(2, GetInt(parameters, "AtrPeriod", 14));

            int n = data.Length;
            var ppSpan  = buffer.GetComponentSpan(CompPP);
            var r1Span  = buffer.GetComponentSpan(CompR1);
            var r2Span  = buffer.GetComponentSpan(CompR2);
            var r3Span  = buffer.GetComponentSpan(CompR3);
            var s1Span  = buffer.GetComponentSpan(CompS1);
            var s2Span  = buffer.GetComponentSpan(CompS2);
            var s3Span  = buffer.GetComponentSpan(CompS3);
            var ch3Span = buffer.GetComponentSpan(CompCamH3);
            var ch4Span = buffer.GetComponentSpan(CompCamH4);
            var cl3Span = buffer.GetComponentSpan(CompCamL3);
            var cl4Span = buffer.GetComponentSpan(CompCamL4);
            var zoneSpan = buffer.GetComponentSpan(CompZone);

            for (int i = 0; i < n; i++)
            {
                if (i < ppSpan.Length)   ppSpan[i]   = double.NaN;
                if (i < r1Span.Length)   r1Span[i]   = double.NaN;
                if (i < r2Span.Length)   r2Span[i]   = double.NaN;
                if (i < r3Span.Length)   r3Span[i]   = double.NaN;
                if (i < s1Span.Length)   s1Span[i]   = double.NaN;
                if (i < s2Span.Length)   s2Span[i]   = double.NaN;
                if (i < s3Span.Length)   s3Span[i]   = double.NaN;
                if (i < ch3Span.Length)  ch3Span[i]  = double.NaN;
                if (i < ch4Span.Length)  ch4Span[i]  = double.NaN;
                if (i < cl3Span.Length)  cl3Span[i]  = double.NaN;
                if (i < cl4Span.Length)  cl4Span[i]  = double.NaN;
                if (i < zoneSpan.Length) zoneSpan[i] = double.NaN;
            }
            if (n < 2) return;

            double[] atr = ComputeAtrWilder(data, atrPeriod);

            // Walk forward; whenever the period (UTC date / ISO week / month) changes
            // versus the previous bar, compute new pivots from the JUST-CLOSED period's HLC.
            int periodStart = 0;
            double pH = double.MinValue, pL = double.MaxValue, pC = 0;

            for (int i = 1; i < n; i++)
            {
                if (PeriodChanged(data[i - 1].Date, data[i].Date, period))
                {
                    // Close out the previous period: compute HLC over [periodStart, i-1].
                    pH = double.MinValue; pL = double.MaxValue;
                    for (int j = periodStart; j < i; j++)
                    {
                        if (data[j].High > pH) pH = data[j].High;
                        if (data[j].Low  < pL) pL = data[j].Low;
                    }
                    pC = data[i - 1].Close;
                    periodStart = i;

                    double pp = (pH + pL + pC) / 3.0;
                    double r1 = 2 * pp - pL;
                    double s1 = 2 * pp - pH;
                    double r2 = pp + (pH - pL);
                    double s2 = pp - (pH - pL);
                    double r3 = pH + 2 * (pp - pL);
                    double s3 = pL - 2 * (pH - pp);

                    // Camarilla — tighter levels using a 1.1/N denominator.
                    double range = pH - pL;
                    double cH3 = pC + range * (1.1 / 4.0);
                    double cH4 = pC + range * (1.1 / 2.0);
                    double cL3 = pC - range * (1.1 / 4.0);
                    double cL4 = pC - range * (1.1 / 2.0);

                    // Project forward through this period until next change.
                    int projectFrom = i;
                    int projectTo = n;
                    for (int j = projectFrom; j < n; j++)
                    {
                        if (j > projectFrom && PeriodChanged(data[j - 1].Date, data[j].Date, period))
                        {
                            projectTo = j;
                            break;
                        }
                        if (j < ppSpan.Length)  ppSpan[j]  = pp;
                        if (j < r1Span.Length)  r1Span[j]  = r1;
                        if (j < r2Span.Length)  r2Span[j]  = r2;
                        if (j < r3Span.Length)  r3Span[j]  = r3;
                        if (j < s1Span.Length)  s1Span[j]  = s1;
                        if (j < s2Span.Length)  s2Span[j]  = s2;
                        if (j < s3Span.Length)  s3Span[j]  = s3;
                        if (j < ch3Span.Length) ch3Span[j] = cH3;
                        if (j < ch4Span.Length) ch4Span[j] = cH4;
                        if (j < cl3Span.Length) cl3Span[j] = cL3;
                        if (j < cl4Span.Length) cl4Span[j] = cL4;
                        if (j < zoneSpan.Length && atr[j] > 0)
                        {
                            double tolerance = atr[j] * zoneAtr;
                            double close = data[j].Close;
                            bool atR = Math.Abs(close - r1) < tolerance
                                    || Math.Abs(close - r2) < tolerance
                                    || Math.Abs(close - r3) < tolerance
                                    || Math.Abs(close - cH3) < tolerance
                                    || Math.Abs(close - cH4) < tolerance;
                            bool atS = Math.Abs(close - s1) < tolerance
                                    || Math.Abs(close - s2) < tolerance
                                    || Math.Abs(close - s3) < tolerance
                                    || Math.Abs(close - cL3) < tolerance
                                    || Math.Abs(close - cL4) < tolerance;
                            zoneSpan[j] = atR && !atS ?  1.0
                                        : atS && !atR ? -1.0
                                        :                0.0;
                        }
                    }
                }
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) =>
            Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 30;

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            if (componentName == CompZone)
            {
                string label = value > 0.5 ? "at resistance" : value < -0.5 ? "at support" : "neutral";
                return $"Pivot zone {label}.";
            }
            return $"{componentName} {Accessibility.SpeechPriceFormatter.FormatPrice(value)}.";
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            double pp = TryGet(calculatedResults, CompPP, index);
            return $"Pivots: PP {(double.IsNaN(pp) ? "n/a" : Accessibility.SpeechPriceFormatter.FormatPrice(pp))}.";
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static IndicatorComponentMetadata PivotLine(string name, string display, string color, bool visible) =>
            new()
            {
                Name = name,
                DisplayName = display,
                DisplayType = ComponentDisplayType.Line,
                Role = ComponentRole.Signal,
                DefaultColorHex = color,
                DefaultThickness = 1.0f,
                DefaultPlaybackLayer = PlaybackLayer.Background,
                DefaultWaveform = "sine",
                DefaultPitchMapping = PitchMapping.Value,
                // Pivot lines are absolute price levels drawn on the price axis, so they take
                // the magnitude-aware token. {value:F2} spoke "S1 0.00" for every sub-dollar
                // asset — on the one number a user would put in an order.
                SpeechTemplate = $"{display} {{value:price}}.",
                IsVisible = visible
            };

        private static bool PeriodChanged(DateTime a, DateTime b, string period)
        {
            return period.ToLowerInvariant() switch
            {
                "weekly"  => GetWeekKey(a)  != GetWeekKey(b),
                "monthly" => a.Year != b.Year || a.Month != b.Month,
                _         => a.Date != b.Date,   // daily default
            };
        }

        private static int GetWeekKey(DateTime d)
        {
            // Simple ISO-ish week key: year * 100 + week-of-year.
            var cal = System.Globalization.CultureInfo.InvariantCulture.Calendar;
            int week = cal.GetWeekOfYear(d, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            return d.Year * 100 + week;
        }

        private static double[] ComputeAtrWilder(ReadOnlySpan<Ohlcv> data, int period)
        {
            int n = data.Length;
            var atr = new double[n];
            for (int i = 0; i < n; i++) atr[i] = double.NaN;
            if (n < 2) return atr;
            double trSum = 0;
            for (int i = 1; i <= period && i < n; i++)
            {
                double tr = Math.Max(data[i].High - data[i].Low,
                            Math.Max(Math.Abs(data[i].High - data[i - 1].Close),
                                     Math.Abs(data[i].Low  - data[i - 1].Close)));
                trSum += tr;
                if (i == period) atr[i] = trSum / period;
            }
            for (int i = period + 1; i < n; i++)
            {
                double tr = Math.Max(data[i].High - data[i].Low,
                            Math.Max(Math.Abs(data[i].High - data[i - 1].Close),
                                     Math.Abs(data[i].Low  - data[i - 1].Close)));
                atr[i] = (atr[i - 1] * (period - 1) + tr) / period;
            }
            return atr;
        }

        private static double TryGet(IReadOnlyDictionary<string, double[]> dict, string key, int idx)
        {
            if (!dict.TryGetValue(key, out var arr) || arr == null
                || idx < 0 || idx >= arr.Length) return double.NaN;
            return arr[idx];
        }

        private static string GetStr(Dictionary<string, object> p, string k, string def) =>
            p != null && p.TryGetValue(k, out var v) ? v.ToString() ?? def : def;
        private static double GetDbl(Dictionary<string, object> p, string k, double def) =>
            p != null && p.TryGetValue(k, out var v) ? Convert.ToDouble(v) : def;
        private static int GetInt(Dictionary<string, object> p, string k, int def) =>
            p != null && p.TryGetValue(k, out var v) ? (int)Convert.ToDouble(v) : def;
    }
}
