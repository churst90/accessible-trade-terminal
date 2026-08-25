using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// Prices assets from the market-data layer.
    ///
    /// <para>
    /// **There is no "current price" call in the SDK** — <c>IMarketDataProvider</c>
    /// offers bars and an order book, nothing else. So this asks for two daily
    /// bars, which gives the latest close AND the previous one, meaning the day
    /// change costs nothing extra. That constraint turned out to be a gift.
    /// </para>
    ///
    /// <para>
    /// Symbol conventions differ per venue — <c>BTCUSDT</c>, <c>BTC/USD</c>,
    /// <c>BTC-USD</c>, and Kraken's <c>XBT</c> for bitcoin — so candidates are
    /// tried in order and the first that returns bars wins. Results are cached for
    /// a short window: a portfolio with a dozen assets would otherwise re-fetch
    /// every one on each refresh.
    /// </para>
    /// </summary>
    public sealed class MarketDataPriceSource : IAssetPriceSource
    {
        private readonly IDataService _data;
        private readonly ILogger<MarketDataPriceSource> _logger;

        // Short by design. A stale portfolio total is a wrong portfolio total, and
        // this only exists to stop one screen refresh fanning out into a dozen
        // identical fetches.
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

        private readonly object _gate = new();
        private readonly Dictionary<string, (DateTime At, (double, double?)? Value)> _cache = new();

        public MarketDataPriceSource(IDataService data, ILogger<MarketDataPriceSource> logger)
        {
            _data = data;
            _logger = logger;
        }

        /// <summary>
        /// Symbol spellings to try for an asset/quote pair, most common first.
        /// Internal so the ordering can be pinned by test rather than trusted.
        /// </summary>
        internal static IEnumerable<string> CandidateSymbols(string asset, string quote)
        {
            // Kraken still prices bitcoin as XBT on many pairs.
            var assets = string.Equals(asset, "BTC", StringComparison.OrdinalIgnoreCase)
                ? new[] { asset, "XBT" }
                : new[] { asset };

            foreach (string a in assets)
            {
                yield return $"{a}{quote}";
                yield return $"{a}/{quote}";
                yield return $"{a}-{quote}";
            }

            // USD-quoted books are frequently spelled with a USD stablecoin and
            // vice versa; treat them as interchangeable for VALUATION only. This is
            // a display figure, not a trade, and refusing to value USDC because the
            // book is USDT would be pedantry the user pays for in blank cells.
            foreach (string alt in Equivalents(quote))
                foreach (string a in assets)
                {
                    yield return $"{a}{alt}";
                    yield return $"{a}/{alt}";
                    yield return $"{a}-{alt}";
                }
        }

        private static IEnumerable<string> Equivalents(string quote) =>
            quote.ToUpperInvariant() switch
            {
                "USD"  => new[] { "USDT", "USDC" },
                "USDT" => new[] { "USD", "USDC" },
                "USDC" => new[] { "USD", "USDT" },
                _      => Array.Empty<string>(),
            };

        public async Task<(double Price, double? PreviousClose)?> TryGetDailyAsync(
            string provider, string asset, string quote, CancellationToken ct = default)
        {
            string key = $"{provider}|{asset}|{quote}";
            lock (_gate)
            {
                if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < CacheFor)
                    return hit.Value;
            }

            (double, double?)? result = null;

            foreach (string symbol in CandidateSymbols(asset, quote))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var (bars, _) = await _data
                        .FetchOhlcvAsync(provider, new MarketDataRequest("Crypto", symbol, "1d", Limit: 2))
                        .ConfigureAwait(false);

                    if (bars.Count == 0) continue;

                    double last = bars[^1].Close;
                    if (last <= 0) continue;
                    double? prev = bars.Count >= 2 ? bars[^2].Close : null;
                    result = (last, prev);
                    break;
                }
                catch (Exception ex)
                {
                    // A wrong spelling throwing is expected — that is what trying
                    // candidates means. Only the final absence is worth reporting,
                    // and the caller does that.
                    _logger.LogTrace(ex, "Price candidate {Symbol} failed on {Provider}", symbol, provider);
                }
            }

            lock (_gate) _cache[key] = (DateTime.UtcNow, result);
            return result;
        }
    }
}
