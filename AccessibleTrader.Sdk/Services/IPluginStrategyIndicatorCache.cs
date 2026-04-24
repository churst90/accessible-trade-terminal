using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Services
{
    /// <summary>
    /// Plugin-side view of the host's shared indicator computation cache. DLL-plugin and
    /// Roslyn-compiled strategies use this to resolve SMA / EMA / RSI / Bollinger Band
    /// values without maintaining their own buffers and without depending on the
    /// workspace's pre-computed chart series. The host invalidates per bar, so repeated
    /// calls within a single <see cref="Strategies.ITradingStrategy.OnBar"/> dispatch
    /// share a cached result.
    ///
    /// Mirrors the Core-side <c>IStrategyIndicatorCache</c>; kept in the SDK so plugin
    /// DLLs can depend on it without pulling Core. The host registers an adapter in
    /// <see cref="PluginHostServices.IndicatorCache"/> during startup. Plugins read
    /// through <see cref="PluginHostServices.IndicatorCache"/> and tolerate
    /// <see langword="null"/> (CLI / unit-test contexts where the host bridge isn't
    /// wired).
    /// </summary>
    public interface IPluginStrategyIndicatorCache
    {
        /// <summary>Simple moving average of the last <paramref name="period"/> closes.</summary>
        double GetSma(IReadOnlyList<Ohlcv> data, int period);

        /// <summary>Exponential moving average (last value) over the last <paramref name="period"/> closes.</summary>
        double GetEma(IReadOnlyList<Ohlcv> data, int period);

        /// <summary>Wilder's RSI over the last <paramref name="period"/> bars.</summary>
        double GetRsi(IReadOnlyList<Ohlcv> data, int period);

        /// <summary>
        /// Bollinger Bands centred on SMA(<paramref name="period"/>), ±<paramref name="deviations"/> stddev.
        /// Values are NaN until enough history is available.
        /// </summary>
        (double Middle, double Upper, double Lower) GetBollingerBands(
            IReadOnlyList<Ohlcv> data, int period, double deviations = 2.0);
    }
}
