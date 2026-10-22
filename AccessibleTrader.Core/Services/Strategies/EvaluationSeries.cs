using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// A chart's series with every indicator recomputed on given bars, for evaluation only:
    /// never dispatched, never drawn. Two callers. A background tab's monitor builds the tab's
    /// indicators from its polled bars. And a strategy on a Heikin-Ashi chart gets its indicators
    /// recomputed on the REAL bars, because the chart's own now read Heikin-Ashi candles
    /// (<see cref="IndicatorCandles"/>) and a strategy is tested — in the backtester, in
    /// StrategyLab — on real prices. Trading it on Heikin-Ashi values would trade signals that
    /// were never tested, with real money in Auto mode.
    /// </summary>
    public static class EvaluationSeries
    {
        /// <summary>
        /// <paramref name="series"/> with each indicator (a series with an indicator code)
        /// computed on <paramref name="bars"/>; the candles, close line and volume series pass
        /// through as they are, since the evaluators read those off the bars. A series whose
        /// indicator throws is kept as it was and reported through <paramref name="failed"/>; its
        /// leaves then evaluate false.
        /// </summary>
        /// <param name="heikinAshi">Compute on Heikin-Ashi candles of the bars, as a Heikin-Ashi
        /// chart does. False for a strategy, always.</param>
        public static ImmutableList<ChartSeries> Compute(
            IIndicatorService indicators,
            IEnumerable<ChartSeries> series,
            Ohlcv[] bars,
            string? symbol,
            bool heikinAshi,
            Action<ChartSeries, Exception>? failed = null)
        {
            var computed = ImmutableList.CreateBuilder<ChartSeries>();
            foreach (var s in series)
            {
                if (string.IsNullOrEmpty(s.Config.IndicatorCode))
                {
                    computed.Add(s);
                    continue;
                }

                try
                {
                    var parameters = s.BuildParameterMap();
                    if (!string.IsNullOrEmpty(symbol)) parameters["__symbol"] = symbol;
                    IndicatorCandles.Stamp(parameters, heikinAshi);

                    var results = new Dictionary<string, double[]>();
                    var buffer = new IndicatorResultBuffer(results, bars.Length);
                    indicators.CalculateIndicator(s.Config.IndicatorCode, bars, parameters, buffer);

                    computed.Add(s.WithData(new SeriesDataBuffer
                    {
                        SeriesId = s.Id,
                        ComponentData = results,
                    }));
                }
                catch (Exception ex)
                {
                    failed?.Invoke(s, ex);
                    computed.Add(s);
                }
            }
            return computed.ToImmutable();
        }
    }
}
