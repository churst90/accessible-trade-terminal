using System.Globalization;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Charts
{
    /// <summary>
    /// What the time chart types beside candles draw that a candle reading does not say, in
    /// words: a hollow candle's colour and fill, a volume candle's weight, the baseline's side.
    /// One place, so the arrow keys' summary, the body's own reading and the status line say the
    /// same thing the drawing shows (Cody, 2026-11-20: "just as long as a totally blind
    /// keyboard/speech user can get the same information that a sighted person can").
    /// </summary>
    public static class ChartTypeWords
    {
        /// <summary>The bars a volume candle is weighed against: the twenty before it.</summary>
        public const int VolumeLookback = 20;

        /// <summary>At or above this many times the average, a volume candle is heavy.</summary>
        public const double HeavyVolume = 1.5;

        /// <summary>At or below this many times the average, a volume candle is light.</summary>
        public const double LightVolume = 0.5;

        /// <summary>
        /// "Hollow green", "Filled red" …: a hollow candle as TradingView draws it. Green or red is
        /// the close against the previous close; hollow or filled is the close against its own
        /// open (hollow when it closed at or above it).
        /// </summary>
        public static string Hollow(bool green, bool hollow) =>
            (hollow ? "Hollow " : "Filled ") + (green ? "green" : "red");

        /// <summary>"Hollow green" for bar <paramref name="index"/> of <paramref name="series"/>,
        /// or null when the series is not drawn as hollow candles.</summary>
        public static string? Hollow(ChartSeries series, int index, Ohlcv bar) =>
            PriceSeriesShape.HollowIsGreen(series, index) is bool green ? Hollow(green, bar.Close >= bar.Open) : null;

        /// <summary>
        /// The volume of bar <paramref name="index"/> against the average of the
        /// <see cref="VolumeLookback"/> bars before it: "Heavy volume, 2.3 times the 20-bar
        /// average" or "Light volume, 0.4 times …". Null for an ordinary bar, a bar with fewer
        /// than five before it, or no volume to weigh: a volume candle's width is only worth a
        /// word when it stands out.
        /// </summary>
        public static string? Volume(IReadOnlyList<Ohlcv>? bars, int index)
        {
            if (bars == null || index < 5 || index >= bars.Count) return null;
            int from = Math.Max(0, index - VolumeLookback);
            double sum = 0;
            int n = 0;
            for (int i = from; i < index; i++)
                if (double.IsFinite(bars[i].Volume)) { sum += bars[i].Volume; n++; }
            if (n < 5 || !(sum > 0)) return null;
            double ratio = bars[index].Volume / (sum / n);
            string times = ratio.ToString("0.0", CultureInfo.InvariantCulture);
            string window = $"{n}-bar average";
            if (ratio >= HeavyVolume) return $"Heavy volume, {times} times the {window}";
            if (ratio <= LightVolume) return $"Light volume, {times} times the {window}";
            return null;
        }

        /// <summary>"2.10 percent above the base, 101.00", "at the base, 101.00": where a
        /// Baseline chart's close sits against its base. Empty when the base is unknown.</summary>
        public static string Baseline(double close, double basePrice)
        {
            if (!double.IsFinite(basePrice) || !double.IsFinite(close)) return "";
            string at = SpeechPriceFormatter.FormatPrice(basePrice);
            if (close == basePrice) return $"at the base, {at}";
            double percent = basePrice == 0 ? 0 : Math.Abs((close - basePrice) / basePrice * 100.0);
            return $"{percent.ToString("F2", CultureInfo.InvariantCulture)} percent {(close > basePrice ? "above" : "below")} the base, {at}";
        }

        /// <summary>"High 104.00, low 99.00, range 5.00": a high-low bar, as drawn.</summary>
        public static string HighLow(Ohlcv bar) =>
            $"High {SpeechPriceFormatter.FormatPrice(bar.High)}, low {SpeechPriceFormatter.FormatPrice(bar.Low)}, " +
            $"range {SpeechPriceFormatter.FormatPrice(bar.High - bar.Low)}";

        /// <summary>
        /// The sentence a change to one of these chart types speaks: "Bar chart.", "Baseline
        /// chart, base 101.00." … Null for every other type (Candles, Line, Area and the built
        /// charts have their own).
        /// </summary>
        public static string? ChangeSentence(WorkspaceState state) => state.ChartStyle switch
        {
            PriceChartStyle.Bars => "Bar chart.",
            PriceChartStyle.HollowCandles => "Hollow candle chart. Colour against the previous close, hollow when the close is above the open.",
            PriceChartStyle.Baseline => "Baseline chart, " + BaseWords(state.Renko.BaselinePrice) + ".",
            PriceChartStyle.HighLow => "High-low chart.",
            PriceChartStyle.HlcArea => "HLC area chart.",
            PriceChartStyle.VolumeCandles => "Volume candle chart. Wider candles for more volume.",
            _ => null,
        };

        /// <summary>"base 101.00" or "base at the first close in view".</summary>
        public static string BaseWords(double baselinePrice) =>
            baselinePrice > 0 && double.IsFinite(baselinePrice)
                ? "base " + SpeechPriceFormatter.FormatPrice(baselinePrice)
                : "base at the first close in view";

        /// <summary>Where the arrows are after a change to one of these types: " On the bars." …
        /// Null for every other type.</summary>
        public static string? LandingWords(PriceChartStyle style) => style switch
        {
            PriceChartStyle.Bars => " On the bars.",
            PriceChartStyle.HighLow => " On the high-low bars.",
            PriceChartStyle.Baseline or PriceChartStyle.HlcArea => " On the close line.",
            PriceChartStyle.HollowCandles or PriceChartStyle.VolumeCandles => " On the candles.",
            _ => null,
        };

        /// <summary>The type in "Already a … chart": "candle", "line", "bar", "HLC area" …</summary>
        public static string Noun(PriceChartStyle style) => style switch
        {
            PriceChartStyle.Line => "line",
            PriceChartStyle.Area => "area",
            PriceChartStyle.Bars => "bar",
            PriceChartStyle.HollowCandles => "hollow candle",
            PriceChartStyle.Baseline => "baseline",
            PriceChartStyle.HighLow => "high-low",
            PriceChartStyle.HlcArea => "HLC area",
            PriceChartStyle.VolumeCandles => "volume candle",
            _ => "candle",
        };

        /// <summary>"Open 100.00, high 104.00, low 99.00, close 103.00": an OHLC bar, as drawn.</summary>
        public static string Ohlc(Ohlcv bar) =>
            $"Open {SpeechPriceFormatter.FormatPrice(bar.Open)}, high {SpeechPriceFormatter.FormatPrice(bar.High)}, " +
            $"low {SpeechPriceFormatter.FormatPrice(bar.Low)}, close {SpeechPriceFormatter.FormatPrice(bar.Close)}";
    }
}
