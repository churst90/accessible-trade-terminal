using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// A provider that is handed the RAW bars on a Heikin-Ashi chart, with
    /// <see cref="IndicatorCandles.HeikinAshiKey"/> set, and builds the Heikin-Ashi candles itself
    /// where it should read them. For an indicator that must read something from the real bars
    /// even when the chart shows Heikin-Ashi — Market Cipher B's "RSI" and "Stochastic" lines read
    /// the real bars on a Heikin-Ashi chart (measured against its screenshots), which only the raw
    /// bars can give — or that
    /// already has its own Heikin-Ashi switch and must not apply it twice (DBSI).
    /// </summary>
    public interface IComputesHeikinAshiItself { }

    /// <summary>
    /// The candles an indicator computes on: Heikin-Ashi when the chart shows Heikin-Ashi, the
    /// real bars otherwise. TradingView feeds an indicator on a Heikin-Ashi chart the Heikin-Ashi
    /// open, high, low and close (volume is unchanged), and Cody asked for what TradingView does
    /// (2026-10-22: "when hiken ashi is on, have indicators use those candles … whatever tv does,
    /// that is what I want").
    ///
    /// <para>What does NOT go through here, and stays on the real bars: the candles, close line
    /// and volume series themselves, profiles and the heatmap, drawings, strategies (live, in
    /// background tabs, in the backtester and in StrategyLab), and the screener. A strategy is
    /// tested on real prices, and trading it on anything else would trade signals nobody tested.</para>
    ///
    /// <para>The transform is <see cref="ChartMath.CalculateHeikinAshi"/> over the whole loaded
    /// history, the same series the chart draws.</para>
    /// </summary>
    public static class IndicatorCandles
    {
        /// <summary>Hidden parameter (`__`-prefixed, so no settings panel shows it): 1 when the
        /// chart shows Heikin-Ashi. Stamped for every provider; only
        /// <see cref="IComputesHeikinAshiItself"/> providers need to read it.</summary>
        public const string HeikinAshiKey = "__heikinAshi";

        /// <summary>The Heikin-Ashi candles of <paramref name="raw"/>, or <paramref name="raw"/>
        /// itself when <paramref name="heikinAshi"/> is off.</summary>
        public static IReadOnlyList<Ohlcv> Of(IReadOnlyList<Ohlcv> raw, bool heikinAshi)
        {
            if (!heikinAshi || raw.Count == 0) return raw;
            return ChartMath.CalculateHeikinAshi(raw as List<Ohlcv> ?? raw.ToList());
        }

        /// <summary>
        /// The bars to hand <paramref name="provider"/> with these parameters: Heikin-Ashi when
        /// they carry <see cref="HeikinAshiKey"/>, unless the provider builds Heikin-Ashi itself
        /// (<see cref="IComputesHeikinAshiItself"/>), in which case the raw bars. A provider that is
        /// not known (null) — a custom compiled indicator — gets Heikin-Ashi, as on TradingView.
        /// The indicator service calls this for every calculation, so a caller only stamps.
        /// </summary>
        public static Ohlcv[] For(IIndicatorProvider? provider, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, object>? parameters)
            => data.Length > 0 && Applies(provider, parameters)
                ? ChartMath.CalculateHeikinAshi(data.ToArray().ToList()).ToArray()
                : data.ToArray();

        /// <summary>Does <paramref name="provider"/> get Heikin-Ashi candles with these parameters?
        /// See <see cref="For(IIndicatorProvider?, ReadOnlySpan{Ohlcv}, IReadOnlyDictionary{string, object}?)"/>.</summary>
        public static bool Applies(IIndicatorProvider? provider, IReadOnlyDictionary<string, object>? parameters)
            => provider is not IComputesHeikinAshiItself && IsHeikinAshi(parameters);

        /// <inheritdoc cref="For(IIndicatorProvider?, ReadOnlySpan{Ohlcv}, IReadOnlyDictionary{string, object}?)"/>
        public static Ohlcv[] For(IIndicatorProvider? provider, IReadOnlyList<Ohlcv> data,
            IReadOnlyDictionary<string, object>? parameters)
            => For(provider, (data as Ohlcv[] ?? data.ToArray()).AsSpan(), parameters);

        /// <summary>Sets <see cref="HeikinAshiKey"/> on a Heikin-Ashi chart and removes it otherwise,
        /// so a parameter map built once and reused cannot carry a stale flag.</summary>
        public static void Stamp(Dictionary<string, object> parameters, bool heikinAshi)
        {
            if (heikinAshi) parameters[HeikinAshiKey] = 1.0;
            else parameters.Remove(HeikinAshiKey);
        }

        /// <summary>Is the chart these parameters came from showing Heikin-Ashi?</summary>
        public static bool IsHeikinAshi(IReadOnlyDictionary<string, object>? parameters)
            => IndicatorParams.GetBool(parameters, HeikinAshiKey, false);
    }
}
