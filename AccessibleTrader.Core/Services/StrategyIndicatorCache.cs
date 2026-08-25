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
        /// Opens the cache scope for one series and drops that series' entries from any other
        /// bar count. Call once immediately before dispatching a strategy against
        /// <paramref name="identity"/> — at the start of each StrategyEngine evaluation cycle,
        /// and at each bar advance during backtest (see <c>StrategyBacktester</c>).
        ///
        /// <para>
        /// The scope is what makes the cache safe to share. The plugin-facing methods
        /// (<c>GetSma</c> and friends) receive only <c>(data, period)</c> — no symbol, no
        /// provider, no timeframe — so before this existed the key was
        /// <c>"SMA|50|500"</c> and BTC 1h and KAS 4h, both sitting at 500 bars, resolved to
        /// the same entry: whichever ran first handed its moving average to the other with no
        /// error. A backtest could poison a concurrent live evaluation the same way. The
        /// signature cannot carry the identity without breaking every compiled plugin DLL, so
        /// the host supplies it out of band here instead.
        /// </para>
        ///
        /// <para>
        /// The scope is ambient to the calling async flow, so concurrent evaluations — the
        /// engine's live bar-close path, a backtest, and <c>BackgroundWorkspaceMonitor</c>'s
        /// non-focused symbols — each carry their own without contending on the shared
        /// instance. A <c>Get*</c> call with no scope open computes and returns WITHOUT
        /// caching: an unattributable value is never worth storing under a key another series
        /// might read.
        /// </para>
        /// </summary>
        void BeginSeries(Sdk.Models.ChartIdentity identity, int currentCount);
    }

    public class StrategyIndicatorCache : IStrategyIndicatorCache
    {
        // Key format: "market|provider|symbol|timeframe|TYPE|period[|extra]|count".
        // The series prefix is mandatory — see IStrategyIndicatorCache.BeginSeries for why a
        // key without it silently crossed two symbols' values over.
        private readonly ConcurrentDictionary<string, double> _scalars = new();
        private readonly ConcurrentDictionary<string, (double Middle, double Upper, double Lower)> _bands = new();

        // Ambient per-flow series scope. AsyncLocal rather than a plain field because one
        // instance is shared by the live engine, background monitors and any running
        // backtest, and those interleave.
        private static readonly System.Threading.AsyncLocal<string?> _scope = new();

        // Entries for a series that is never evaluated again would otherwise linger for the
        // life of the process. Well under any real working set (a few dozen strategies ×
        // a handful of indicator calls), so hitting it means something is churning scopes.
        private const int MaxEntries = 4096;

        public void BeginSeries(Sdk.Models.ChartIdentity identity, int currentCount)
        {
            string scope = $"{identity.Market}|{identity.Provider}|{identity.Symbol}|{identity.Timeframe}";
            _scope.Value = scope;

            if (_scalars.Count + _bands.Count > MaxEntries)
            {
                _scalars.Clear();
                _bands.Clear();
                return;
            }

            // Drop only THIS series' stale bar counts. Other series' entries can no longer
            // collide with it, so evicting them would just throw away valid work.
            string prefix = scope + "|";
            string suffix = $"|{currentCount}";
            foreach (var key in _scalars.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal) && !key.EndsWith(suffix, StringComparison.Ordinal))
                    _scalars.TryRemove(key, out _);
            }
            foreach (var key in _bands.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal) && !key.EndsWith(suffix, StringComparison.Ordinal))
                    _bands.TryRemove(key, out _);
            }
        }

        public double GetSma(IReadOnlyList<Sdk.Models.Ohlcv> data, int period)
        {
            if (_scope.Value is not string scope) return ComputeSma(data, period);
            string key = $"{scope}|SMA|{period}|{data.Count}";
            if (_scalars.TryGetValue(key, out double cached)) return cached;
            double result = ComputeSma(data, period);
            _scalars[key] = result;
            return result;
        }

        public double GetEma(IReadOnlyList<Sdk.Models.Ohlcv> data, int period)
        {
            if (_scope.Value is not string scope) return ComputeEma(data, period);
            string key = $"{scope}|EMA|{period}|{data.Count}";
            if (_scalars.TryGetValue(key, out double cached)) return cached;
            double result = ComputeEma(data, period);
            _scalars[key] = result;
            return result;
        }

        public double GetRsi(IReadOnlyList<Sdk.Models.Ohlcv> data, int period)
        {
            if (_scope.Value is not string scope) return ComputeRsi(data, period);
            string key = $"{scope}|RSI|{period}|{data.Count}";
            if (_scalars.TryGetValue(key, out double cached)) return cached;
            double result = ComputeRsi(data, period);
            _scalars[key] = result;
            return result;
        }

        public (double Middle, double Upper, double Lower) GetBollingerBands(
            IReadOnlyList<Sdk.Models.Ohlcv> data, int period, double deviations = 2.0)
        {
            if (_scope.Value is not string scope) return ComputeBollingerBands(data, period, deviations);
            string key = $"{scope}|BB|{period}|{deviations:F2}|{data.Count}";
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
