using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Shared indicator computation cache for strategies.
    /// Multiple strategies with the same indicator + period avoid redundant recomputation
    /// on each bar. The cache is invalidated whenever the bar count changes.
    ///
    /// Also surfaces the plugin-side contract <see cref="Sdk.Services.IPluginStrategyIndicatorCache"/>
    /// so the same instance can be handed to DLL plugins and Roslyn strategies via
    /// <see cref="Sdk.Services.PluginHostServices.IndicatorCache"/>.
    /// </summary>
    public interface IStrategyIndicatorCache : Sdk.Services.IPluginStrategyIndicatorCache
    {
        /// <summary>
        /// Clears all cached entries whose bar-count key no longer matches the current data length.
        /// Call once at the start of each StrategyEngine.OnBar cycle (and each bar advance
        /// during backtest — see <c>StrategyBacktester</c>).
        /// </summary>
        void Invalidate(int currentCount);
    }

    public class StrategyIndicatorCache : IStrategyIndicatorCache
    {
        // Key format: "TYPE|period[|extra]|count" → result array or single double
        private readonly ConcurrentDictionary<string, double> _scalars = new();
        private readonly ConcurrentDictionary<string, (double Middle, double Upper, double Lower)> _bands = new();

        public void Invalidate(int currentCount)
        {
            // Remove all entries whose embedded count no longer matches.
            foreach (var key in _scalars.Keys)
            {
                if (!key.EndsWith($"|{currentCount}"))
                    _scalars.TryRemove(key, out _);
            }
            foreach (var key in _bands.Keys)
            {
                if (!key.EndsWith($"|{currentCount}"))
                    _bands.TryRemove(key, out _);
            }
        }

        public double GetSma(IReadOnlyList<Sdk.Models.Ohlcv> data, int period)
        {
            string key = $"SMA|{period}|{data.Count}";
            if (_scalars.TryGetValue(key, out double cached)) return cached;
            double result = ComputeSma(data, period);
            _scalars[key] = result;
            return result;
        }

        public double GetEma(IReadOnlyList<Sdk.Models.Ohlcv> data, int period)
        {
            string key = $"EMA|{period}|{data.Count}";
            if (_scalars.TryGetValue(key, out double cached)) return cached;
            double result = ComputeEma(data, period);
            _scalars[key] = result;
            return result;
        }

        public double GetRsi(IReadOnlyList<Sdk.Models.Ohlcv> data, int period)
        {
            string key = $"RSI|{period}|{data.Count}";
            if (_scalars.TryGetValue(key, out double cached)) return cached;
            double result = ComputeRsi(data, period);
            _scalars[key] = result;
            return result;
        }

        public (double Middle, double Upper, double Lower) GetBollingerBands(
            IReadOnlyList<Sdk.Models.Ohlcv> data, int period, double deviations = 2.0)
        {
            string key = $"BB|{period}|{deviations:F2}|{data.Count}";
            if (_bands.TryGetValue(key, out var cached)) return cached;
            var result = ComputeBollingerBands(data, period, deviations);
            _bands[key] = result;
            return result;
        }

        // ── Pure computations ────────────────────────────────────────────────

        private static double ComputeSma(IReadOnlyList<Sdk.Models.Ohlcv> data, int period)
        {
            int count = data.Count;
            if (count < period) return double.NaN;
            double sum = 0;
            for (int i = count - period; i < count; i++) sum += data[i].Close;
            return sum / period;
        }

        private static double ComputeEma(IReadOnlyList<Sdk.Models.Ohlcv> data, int period)
        {
            int count = data.Count;
            if (count < period) return double.NaN;
            double k = 2.0 / (period + 1);
            // Seed with SMA of the first `period` closes
            double ema = 0;
            for (int i = 0; i < period; i++) ema += data[count - count + i].Close;
            ema /= period;
            for (int i = period; i < count; i++)
                ema = data[i].Close * k + ema * (1 - k);
            return ema;
        }

        private static double ComputeRsi(IReadOnlyList<Sdk.Models.Ohlcv> data, int period)
        {
            int count = data.Count;
            if (count < period + 1) return double.NaN;
            double gain = 0, loss = 0;
            for (int i = count - period; i < count; i++)
            {
                double change = data[i].Close - data[i - 1].Close;
                if (change > 0) gain += change;
                else loss -= change;
            }
            double avgGain = gain / period;
            double avgLoss = loss / period;
            if (avgLoss == 0) return 100.0;
            double rs = avgGain / avgLoss;
            return 100.0 - 100.0 / (1.0 + rs);
        }

        private static (double Middle, double Upper, double Lower) ComputeBollingerBands(
            IReadOnlyList<Sdk.Models.Ohlcv> data, int period, double deviations)
        {
            int count = data.Count;
            if (count < period) return (double.NaN, double.NaN, double.NaN);
            double middle = ComputeSma(data, period);
            double variance = 0;
            for (int i = count - period; i < count; i++)
            {
                double diff = data[i].Close - middle;
                variance += diff * diff;
            }
            double stdDev = Math.Sqrt(variance / period);
            return (middle, middle + deviations * stdDev, middle - deviations * stdDev);
        }
    }
}
