using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// The background monitors' one bar fetch: a chart's timeframe the venue serves is asked for
    /// as it is; one it does not serve is built from the largest native timeframe that divides
    /// it, through the same <see cref="IResamplerService"/> the chart uses.
    ///
    /// <para><b>Why this exists.</b> Until 2026-10-31 both <see cref="LocalBackgroundMonitor"/>
    /// and <see cref="HostedAlertMonitor"/> handed the saved tab's timeframe straight to the
    /// provider. The chart never does that — <c>HistoricalDataFetcher</c> resamples anything the
    /// venue lacks — so a chart that worked on screen was unwatchable the moment the browser
    /// closed. Cody, a BTCUSDT 24-minute chart on Bitstamp, browser shut: Bitstamp answers
    /// step=1440 with "Not a valid choice", every poll failed at debug level, and the dead-feed
    /// warning never fired because the 1-day and 4-hour tabs on the same symbol kept succeeding.
    /// No bar close, no narration, no alert on that chart, and nothing said so.</para>
    ///
    /// <para><b>The oldest built bar is dropped.</b> The fetch window starts wherever the
    /// venue's newest N bars happen to start, which is almost never on a bucket boundary, so
    /// the first built bar is usually missing part of its period: a wrong open, high and low
    /// read aloud as real. One extra bucket is fetched so the drop costs nothing.</para>
    /// </summary>
    internal static class BackgroundBarFetch
    {
        internal static async Task<List<Ohlcv>> FetchAsync(
            IMarketDataProvider provider, IResamplerService resampler,
            string market, string symbol, string timeframe, int limit)
        {
            var native = provider.NativelySupportedTimeframes ?? new List<string>();
            if (native.Count == 0 || native.Contains(timeframe))
            {
                var (bars, _) = await provider.FetchOhlcvAsync(
                    new MarketDataRequest(market, symbol, timeframe, Limit: limit));
                return bars;
            }

            string baseTf = TimeframeUtility.GetBestBaseTimeframe(timeframe, native);
            long targetMs = TimeframeUtility.ToMilliseconds(timeframe);
            long baseMs = TimeframeUtility.ToMilliseconds(baseTf);
            int baseLimit = baseMs > 0 && targetMs > baseMs
                ? (int)Math.Ceiling((limit + 1) * (double)targetMs / baseMs)
                : limit + 1;
            if (provider.MaxBarsPerRequest > 0) baseLimit = Math.Min(baseLimit, provider.MaxBarsPerRequest);

            var (raw, _) = await provider.FetchOhlcvAsync(
                new MarketDataRequest(market, symbol, baseTf, Limit: baseLimit));
            var built = resampler.Resample(raw ?? new List<Ohlcv>(), timeframe);
            if (built.Count > 1) built.RemoveAt(0);
            return built.Count > limit ? built.GetRange(built.Count - limit, limit) : built;
        }
    }
}
