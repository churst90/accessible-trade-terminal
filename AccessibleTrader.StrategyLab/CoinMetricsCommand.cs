using System.Globalization;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// One-shot downloader for CoinMetrics community-tier on-chain metrics.
///
/// Why CoinMetrics community tier: it's the only free, multi-year-history,
/// no-auth-required source for the standard on-chain regime measurements
/// (MVRV, realized cap, active addresses, transactions, fees, hash rate, etc).
/// Glassnode and CryptoQuant gate everything behind paid tiers above the trivial
/// "current value" endpoint. CoinMetrics gives ~daily history back to 2010 for
/// BTC and 2015 for ETH at zero cost — exactly the kind of orthogonal-to-price
/// data the strategy thesis says we need to break the autocorrelation wall.
///
/// Community tier endpoint:
///   GET https://community-api.coinmetrics.io/v4/timeseries/asset-metrics
///   ?assets=btc&amp;metrics=CapMVRVCur,CapRealUSD,...&amp;frequency=1d
///   &amp;start_time=2015-01-01&amp;page_size=10000
///
/// Pagination: response includes `next_page_url` until exhausted.
///
/// Default metrics (all free on community tier, all useful for regime classification):
///   • CapMVRVCur     — market cap / realized cap (Pal/Glassnode "MVRV ratio").
///                       The single most-cited on-chain cycle measurement.
///                       Rule of thumb: &lt;1 capitulation, 1-2 fair, 2-3 markup,
///                       3-4 distribution, &gt;4 euphoric top.
///   • CapRealUSD     — realized cap (sum of UTXOs at last-moved price). Slow
///                       trend-following measurement of "cost basis of all coins".
///   • CapMrktCurUSD  — current market cap (a sanity check / denominator for MVRV).
///   • AdrActCnt      — daily active addresses. Network usage proxy.
///   • TxCnt          — daily transaction count. Settlement velocity proxy.
///   • FeeMeanUSD     — mean transaction fee (USD). Demand pressure proxy.
///
/// Output: one xs_coinmetrics_{asset}_{metric_lower}_1d.json per asset×metric.
/// Each file matches the existing CrossSeriesSnapshot shape for direct ingest by
/// the provider via ICrossSeriesCache.
/// </summary>
public static class CoinMetricsCommand
{
    private const string BaseUrl = "https://community-api.coinmetrics.io/v4/timeseries/asset-metrics";

    // Confirmed-free community-tier metrics for BTC as of 2026-04-09 (probed individually
    // because CoinMetrics returns 403 if ANY metric in a request is paywalled, killing
    // the whole call). Most of the "premium" metrics (CapRealUSD, NVTAdj, SOPR, address-
    // balance buckets, FeeMeanUSD, volatility windows) are gated behind paid plans now.
    // What's left is still the core cycle/usage measurement set:
    //   • CapMVRVCur     — MVRV ratio (precomputed, no need for CapRealUSD). The single
    //                       most-cited on-chain cycle indicator. <1=capitulation, >3.5=top.
    //   • CapMrktCurUSD  — market cap
    //   • PriceUSD       — reference price (sanity / time alignment)
    //   • AdrActCnt      — daily active addresses (network usage)
    //   • TxCnt          — daily transaction count
    //   • TxTfrCnt       — transfer count (different from TxCnt for chains with smart contracts)
    //   • HashRate       — BTC mining hashrate (structural mining-cost floor)
    //   • SplyCur        — current circulating supply
    public static readonly string[] DefaultMetrics =
    {
        "CapMVRVCur",
        "CapMrktCurUSD",
        "PriceUSD",
        "AdrActCnt",
        "TxCnt",
        "TxTfrCnt",
        "HashRate",
        "SplyCur",
    };

    public static async Task<int> RunAsync(string outputDir, string[] assets, string[] metrics, string startDate)
    {
        Directory.CreateDirectory(outputDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Add("User-Agent", "AccessibleTrader-StrategyLab/1.0");
        http.DefaultRequestHeaders.Add("Accept", "application/json");

        foreach (var asset in assets)
        {
            string assetLower = asset.ToLowerInvariant();
            Console.WriteLine();
            Console.WriteLine($"=== {assetLower.ToUpper()} on-chain ===");

            // CoinMetrics returns one row per (asset, time) with all requested metrics as
            // columns. We fetch all metrics in a single call, then split by metric on write.
            string metricsCsv = string.Join(",", metrics);
            string url = $"{BaseUrl}?assets={assetLower}&metrics={metricsCsv}&frequency=1d" +
                         $"&start_time={startDate}&page_size=10000&pretty=false";

            // Per-metric sorted-time-series sinks. Built up across pagination.
            var sinks = metrics.ToDictionary(
                m => m,
                _ => new SortedDictionary<long, double>());

            int pageCount = 0;
            while (!string.IsNullOrEmpty(url) && pageCount < 50) // hard cap = 500k rows
            {
                pageCount++;
                JObject? doc;
                try
                {
                    using var resp = await http.GetAsync(url);
                    if (!resp.IsSuccessStatusCode)
                    {
                        Console.Error.WriteLine($"  page {pageCount}: HTTP {(int)resp.StatusCode} — {await resp.Content.ReadAsStringAsync()}");
                        break;
                    }
                    var body = await resp.Content.ReadAsStringAsync();
                    doc = JObject.Parse(body);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  page {pageCount}: error — {ex.Message}");
                    break;
                }

                var rows = doc["data"] as JArray;
                int newRows = 0;
                if (rows != null)
                {
                    foreach (var row in rows)
                    {
                        // time is ISO 8601 like "2015-01-01T00:00:00.000000000Z"
                        var timeStr = (string?)row["time"];
                        if (timeStr == null) continue;
                        if (!DateTime.TryParse(timeStr, CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                            continue;
                        long ts = new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeMilliseconds();

                        foreach (var m in metrics)
                        {
                            var vTok = row[m];
                            if (vTok == null || vTok.Type == JTokenType.Null) continue;
                            var vStr = (string?)vTok;
                            if (vStr == null) continue;
                            if (!double.TryParse(vStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                            if (sinks[m].TryAdd(ts, v)) newRows++;
                        }
                    }
                }

                Console.WriteLine($"  page {pageCount}: +{newRows} datapoints (cumulative MVRV {sinks[metrics[0]].Count})");

                url = (string?)doc["next_page_url"] ?? string.Empty;
            }

            // Write one snapshot file per metric so the existing cross-series cache /
            // provider pattern works unchanged. We use lowercase metric names in filenames
            // because the existing convention is lowercase symbol_metric in xs_*.json.
            foreach (var m in metrics)
            {
                var sink = sinks[m];
                if (sink.Count == 0)
                {
                    Console.Error.WriteLine($"  {m}: no data, skipping");
                    continue;
                }

                var snap = new CrossSeriesSnapshot
                {
                    Provider = "CoinMetrics",
                    Symbol = $"{assetLower}_{m}",
                    Timeframe = "1d",
                    FetchedUtc = DateTime.UtcNow,
                    PointCount = sink.Count,
                    Points = sink.Select(kv => new TimedValue { Ts = kv.Key, Value = kv.Value }).ToList()
                };
                var path = snap.FilePath(outputDir);
                await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(snap, Formatting.None));
                var firstDate = DateTimeOffset.FromUnixTimeMilliseconds(snap.Points[0].Ts).UtcDateTime;
                var lastDate  = DateTimeOffset.FromUnixTimeMilliseconds(snap.Points[^1].Ts).UtcDateTime;
                Console.WriteLine($"  {m,-15} {sink.Count,5} pts [{firstDate:yyyy-MM-dd} → {lastDate:yyyy-MM-dd}]");
            }
        }

        return 0;
    }
}
