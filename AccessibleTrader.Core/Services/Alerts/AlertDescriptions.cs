using System.Globalization;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Alerts
{
    /// <summary>
    /// The words and numbers a simple alert is described with: the alerts dialog's confirmation
    /// ("Alert added: BTC/USD price touches SMA 50"), the value it pre-fills, and the line names
    /// the evaluator speaks when a line alert fires. One place, so the sentence the user hears
    /// when an alert is created names the line the same way the alert names it when it fires.
    /// </summary>
    public static class AlertDescriptions
    {
        /// <summary>
        /// A line's spoken name: the series' own name ("SMA 50"), plus the component when the
        /// series has more than one ("Bollinger Bands upper band" rather than three lines all
        /// called "Bollinger Bands").
        /// </summary>
        public static string LineName(ChartSeries series, ComponentConfig comp)
        {
            string name = !string.IsNullOrWhiteSpace(series.FriendlyName) ? series.FriendlyName
                        : !string.IsNullOrWhiteSpace(series.Name) ? series.Name
                        : series.IndicatorCode;
            if (series.Components.Count <= 1) return name;
            string part = string.IsNullOrWhiteSpace(comp.DisplayName) ? comp.Name : comp.DisplayName;
            return $"{name} {part}";
        }

        /// <summary>
        /// A level as it is spoken: grouped thousands, and only the decimals the number has —
        /// "64,250.5", "61.25", "0.0363". Never the six fixed decimals the fire speech has always
        /// used ("64250.500000").
        /// </summary>
        public static string FormatForSpeech(double value) =>
            Math.Round(value, Decimals(value)).ToString("#,0.##########", CultureInfo.InvariantCulture);

        /// <summary>
        /// A level as the value field is pre-filled with it: no grouping (a number input rejects
        /// commas), invariant decimal point, and roughly five significant figures — a moving
        /// average's 63000.12345678 becomes 63000.12, a sub-cent coin keeps its digits.
        /// </summary>
        public static string FormatForInput(double value) =>
            Math.Round(value, Decimals(value)).ToString("0.##########", CultureInfo.InvariantCulture);

        private static int Decimals(double value)
        {
            double abs = Math.Abs(value);
            if (abs == 0 || double.IsNaN(abs) || double.IsInfinity(abs)) return 2;
            return Math.Clamp(4 - (int)Math.Floor(Math.Log10(abs)), 2, 10);
        }

        /// <summary>
        /// One sentence that says what a simple alert will do — "BTC/USD price crosses above
        /// 64,250", "BTC/USD RSI enters overbought", "Any symbol price touches SMA 50".
        /// <paramref name="chartSeries"/> names the indicator and line the way the chart does;
        /// a reference to something no longer on it falls back to its code.
        /// </summary>
        public static string Describe(AlertDefinition a, IEnumerable<ChartSeries> chartSeries)
        {
            var series = chartSeries as IReadOnlyCollection<ChartSeries> ?? chartSeries.ToList();
            string symbol = string.IsNullOrWhiteSpace(a.Symbol) ? "Any symbol" : a.Symbol!;

            string subject = a.Target switch
            {
                AlertTarget.Price => "price",
                AlertTarget.Candle => "candle",
                AlertTarget.Poc => "price",
                _ => NameOf(series, a.IndicatorCode, a.SeriesId, a.ComponentName),
            };

            string zone = a.Zone switch
            {
                AlertZone.Overbought => "overbought",
                AlertZone.Oversold => "oversold",
                null => "zone",
                var z => z.ToString()!,
            };

            string level = a.ComparesToLine()
                ? NameOf(series, a.LineIndicatorCode, a.LineSeriesId, a.LineComponentName)
                : a.Target == AlertTarget.Poc ? "the point of control"
                : FormatForSpeech(a.Threshold ?? 0);

            string what = a.Condition switch
            {
                AlertCondition.CrossesAbove => $"crosses above {level}",
                AlertCondition.CrossesBelow => $"crosses below {level}",
                AlertCondition.Touches => $"touches {level}",
                AlertCondition.EntersZone => $"enters {zone}",
                AlertCondition.ExitsZone => $"exits {zone}",
                AlertCondition.ChangesDirection when a.Target == AlertTarget.Indicator => "changes direction",
                AlertCondition.ChangesDirection => "changes colour",
                AlertCondition.TrendChange => "changes trend",
                AlertCondition.PatternDetected => $"forms {a.Pattern}",
                _ => a.Condition.ToString(),
            };
            return $"{symbol} {subject} {what}";
        }

        private static string NameOf(IReadOnlyCollection<ChartSeries> chart, string? code, string? seriesId, string? component)
        {
            if (string.IsNullOrWhiteSpace(code)) return "indicator";
            var s = (!string.IsNullOrEmpty(seriesId)
                        ? chart.FirstOrDefault(x => x.Id.Equals(seriesId, StringComparison.OrdinalIgnoreCase))
                        : null)
                    ?? chart.FirstOrDefault(x => x.IndicatorCode.Equals(code, StringComparison.OrdinalIgnoreCase));
            var comp = s?.Components.FirstOrDefault(c => c.Name.Equals(component, StringComparison.OrdinalIgnoreCase));
            if (s == null || comp == null) return string.IsNullOrWhiteSpace(component) ? code! : $"{code} {component}";
            return LineName(s, comp);
        }
    }
}
