using System.Collections.Concurrent;

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
        //
        // Every one of these must return exactly what the equivalent
        // Sdk.Indicators.IndicatorMath helper puts in its LAST slot for the same closes —
        // that is the library the chart's own providers draw from, and a strategy whose
        // "RSI" is not the RSI on screen is a bug the user cannot see until it costs them a
        // trade. StrategyIndicatorCacheParityTests pins each pair against a random series.
        //
        // They are re-implemented here rather than delegating because these are scalar,
        // last-bar-only questions asked once per bar per strategy: IndicatorMath allocates a
        // closes array plus a full result array per call, which in a 5,000-bar backtest is
        // 5,000 of each. The parity test, not shared code, is what keeps them honest.

        private static double ComputeSma(IReadOnlyList<Sdk.Models.Ohlcv> data, int period)
        {
            int count = data.Count;
            if (count < period) return double.NaN;
            double sum = 0;
            for (int i = count - period; i < count; i++) sum += data[i].Close;
            return sum / period;
        }

        /// <summary>
        /// EMA seeded from the FIRST close, matching <c>IndicatorMath.Ema</c> — the seed every
        /// chart series built on that library uses (Cipher B/C/SR, Ichimoku, Spider Lines, and
        /// anything a plugin author writes against the documented helper).
        ///
        /// <para>
        /// This used to seed with the SMA of the first <c>period</c> closes, written as the dead
        /// arithmetic <c>data[count - count + i]</c>. The seed's weight decays by
        /// <c>(1 - k)</c> per bar, so on a long series the two agree to many decimals — but
        /// "agrees on long series" is not a contract, and short warmup windows are exactly where
        /// a strategy's first live signal is decided.
        /// </para>
        /// </summary>
        private static double ComputeEma(IReadOnlyList<Sdk.Models.Ohlcv> data, int period)
        {
            int count = data.Count;
            if (count < period) return double.NaN;
            double k = 2.0 / (period + 1.0);
            double ema = data[0].Close;
            for (int i = 1; i < count; i++)
                ema = data[i].Close * k + ema * (1.0 - k);
            return ema;
        }

        /// <summary>
        /// Wilder's RSI (RMA-smoothed), matching <c>IndicatorMath.Rsi</c> and
        /// <c>PulseProvider.ComputeRsi</c>.
        ///
        /// <para>
        /// This was a Cutler RSI — a plain arithmetic mean of the gains and losses over the last
        /// <c>period</c> changes with no smoothing — while the interface it implements has always
        /// documented "Wilder's RSI over the last period bars" and every RSI the user can see on
        /// the chart is Wilder. The two do not converge with more data: on a 14-bar setting they
        /// routinely sit several points apart, which is the difference between a 30-threshold
        /// entry firing and not firing. A strategy backtested against the line it was reading and
        /// then run live got a different number from the same name.
        /// </para>
        ///
        /// <para>
        /// Wilder is path-dependent, so this walks the whole series rather than a trailing
        /// window. That is the same O(n) the EMA above already costs.
        /// </para>
        /// </summary>
        private static double ComputeRsi(IReadOnlyList<Sdk.Models.Ohlcv> data, int period)
        {
            int count = data.Count;
            if (count < period + 1) return double.NaN;

            double avgGain = 0, avgLoss = 0;
            for (int i = 1; i <= period; i++)
            {
                double ch = data[i].Close - data[i - 1].Close;
                if (ch > 0) avgGain += ch; else avgLoss -= ch;
            }
            avgGain /= period;
            avgLoss /= period;

            for (int i = period + 1; i < count; i++)
            {
                double ch = data[i].Close - data[i - 1].Close;
                double gain = ch > 0 ? ch : 0;
                double loss = ch < 0 ? -ch : 0;
                avgGain = (avgGain * (period - 1) + gain) / period;
                avgLoss = (avgLoss * (period - 1) + loss) / period;
            }

            // 1e-10, not == 0: IndicatorMath uses that floor, and an exact-zero test disagrees
            // with it on a series whose losses are merely tiny.
            return avgLoss < 1e-10 ? 100.0 : 100.0 - 100.0 / (1.0 + avgGain / avgLoss);
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
