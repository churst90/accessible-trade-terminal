using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>A strategy that reads its conditions on Heikin-Ashi candles
    /// (<see cref="StrategySpec.SignalsOnHeikinAshi"/>). The hosts ask it so they compute the
    /// Heikin-Ashi series only when something will read them.</summary>
    public interface IReadsHeikinAshiSignals
    {
        bool SignalsOnHeikinAshi { get; }
    }

    /// <summary>
    /// The chart as the strategies read it, built the same way by the live engine, a background
    /// tab and the backtester. <see cref="WorkspaceState.ActiveSeries"/> is always computed on the
    /// real bars: stops, targets and every price an order sits at read it. When a running strategy
    /// reads its signals on Heikin-Ashi, <see cref="WorkspaceState.HeikinAshiSeries"/> carries the
    /// same series computed on Heikin-Ashi candles, and its conditions read those.
    /// </summary>
    public static class StrategyCandles
    {
        public static bool WantsHeikinAshi(ITradingStrategy strategy) =>
            strategy is IReadsHeikinAshiSignals h && h.SignalsOnHeikinAshi;

        /// <summary>
        /// <paramref name="state"/> as the strategies read it over <paramref name="bars"/> (the
        /// closed bars a strategy is handed, oldest first, index-aligned with the chart's series).
        /// Returns <paramref name="state"/> itself when there is nothing to change: a candle chart
        /// and no strategy wanting Heikin-Ashi.
        /// </summary>
        /// <param name="heikinAshiWanted">A running strategy reads its signals on Heikin-Ashi.</param>
        public static WorkspaceState For(
            IIndicatorService indicators,
            WorkspaceState state,
            Ohlcv[] bars,
            bool heikinAshiWanted,
            Action<ChartSeries, Exception>? failed = null)
        {
            if (!state.IsHeikinAshi && !heikinAshiWanted) return state;

            string? symbol = state.Identity.Symbol;
            // On a Heikin-Ashi chart the chart's own indicators already read Heikin-Ashi candles;
            // the real ones are what has to be computed. On a candle chart, the other way round.
            var real = state.IsHeikinAshi
                ? EvaluationSeries.Compute(indicators, state.ActiveSeries, bars, symbol, heikinAshi: false, failed)
                : state.ActiveSeries;
            ImmutableList<ChartSeries>? heikinAshi = null;
            if (heikinAshiWanted)
                heikinAshi = WithoutPriceLines(state.IsHeikinAshi
                    ? state.ActiveSeries
                    : EvaluationSeries.Compute(indicators, state.ActiveSeries, bars, symbol, heikinAshi: true, failed));

            return state with { ActiveSeries = real, IsHeikinAshi = false, HeikinAshiSeries = heikinAshi };
        }

        /// <summary>
        /// The candle and close-line series carry the REAL prices on every chart (the close line is
        /// drawn raw; the candle series' arrays are the raw bars). A condition reading price on
        /// Heikin-Ashi must not find them: with their arrays empty, <c>ConditionEvaluator</c> projects
        /// price off the Heikin-Ashi bars it is handed instead. Volume is the same on both.
        /// </summary>
        internal static ImmutableList<ChartSeries> WithoutPriceLines(ImmutableList<ChartSeries> series) =>
            series.Select(s => IsPriceLine(s) ? s.WithData(new SeriesDataBuffer { SeriesId = s.Id }) : s)
                .ToImmutableList();

        private static bool IsPriceLine(ChartSeries s) =>
            s.Id == CoreSeriesIds.Candles || s.Id == CoreSeriesIds.Price
            || string.Equals(s.IndicatorCode, "CANDLES", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.IndicatorCode, "PRICE", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The state a Heikin-Ashi-signal strategy's conditions read: the Heikin-Ashi series in
        /// place of the real ones. Null when no host computed them — the strategy then reads
        /// nothing rather than reading the real bars under a Heikin-Ashi label.
        /// </summary>
        public static WorkspaceState? SignalState(WorkspaceState state)
        {
            var series = state.HeikinAshiSeries
                         ?? (state.IsHeikinAshi ? WithoutPriceLines(state.ActiveSeries) : null);
            if (series == null) return null;
            return state with { ActiveSeries = series, IsHeikinAshi = true, HeikinAshiSeries = null };
        }
    }
}
