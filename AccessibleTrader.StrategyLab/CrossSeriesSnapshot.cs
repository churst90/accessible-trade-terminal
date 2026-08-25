using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Plugins.AlternativeMe;
using AccessibleTrader.Plugins.OkxDerivatives;
using AccessibleTrader.Sdk.Models;
using Newtonsoft.Json;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// On-disk file format for cross-series snapshots — funding rate, open interest, fear &amp; greed.
/// Each file is a list of (UnixMillis, Value) pairs identified by the same triple
/// (Provider, Symbol, Timeframe) that <see cref="CrossSeriesRequest"/> uses as its cache key,
/// so reloading the snapshot is a 1:1 fit for the live <see cref="ICrossSeriesCache"/> contract.
///
/// We deliberately store the data in this shape (not as <see cref="Ohlcv"/> bars) because
/// that's exactly what <see cref="ICrossSeriesCache.GetOrFetch"/> hands back to indicator
/// providers. Fetching writes a snapshot, loading registers it into the in-memory cache,
/// and the indicator pipeline never touches the network during a research run.
/// </summary>
public sealed class CrossSeriesSnapshot
{
    public string Provider { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Timeframe { get; set; } = "";
    public DateTime FetchedUtc { get; set; }
    public int PointCount { get; set; }
    public List<TimedValue> Points { get; set; } = new();

    /// <summary>The cache key matches <see cref="CrossSeriesRequest.CacheKey"/>: lowercase
    /// "{provider}:{symbol}:{timeframe}". Used as the dictionary key in the in-memory cache.</summary>
    public string CacheKey => $"{Provider}:{Symbol}:{Timeframe}".ToLowerInvariant();

    public string FilePath(string dir) =>
        Path.Combine(dir, $"xs_{Sanitize(Provider)}_{Sanitize(Symbol)}_{Sanitize(Timeframe)}.json");

    private static string Sanitize(string s) =>
        s.Replace("/", "_").Replace("\\", "_").Replace(":", "_").ToLowerInvariant();
}

/// <summary>(Unix millis, raw value). Both fields are settable for Newtonsoft round-tripping.</summary>
public sealed class TimedValue
{
    public long Ts { get; set; }
    public double Value { get; set; }
}

/// <summary>
/// Headless cross-series snapshot fetcher. Mirrors <see cref="SnapshotCommand"/> for OHLCV
/// but produces (timestamp, value) lists for funding/OI/FNG instead. Plugins are instantiated
/// directly with no DI — same approach Bitstamp uses.
///
/// Pagination strategy: walk back via <c>request.Until</c> like the OHLCV snapshotter, accumulate
/// into a sorted dictionary keyed by timestamp so duplicate-page entries naturally dedupe, stop
/// when a page yields zero new points or the page cap is reached.
/// </summary>
public static class CrossSeriesSnapshotCommand
{
    public static async Task<int> RunAsync(string outputDir, int targetPoints)
    {
        Directory.CreateDirectory(outputDir);

        var okx = new OkxDerivativesProvider();
        var fng = new AlternativeMeProvider();

        Console.WriteLine("Fetching BTC-USDT-SWAP funding rate (OKX)...");
        await FetchAndSave(
            okx,
            provider: "OkxDerivatives",
            symbol: "BTC-USDT-SWAP_FUNDING",
            timeframe: "1h",
            market: "Derivatives",
            targetPoints: targetPoints,
            outputDir: outputDir);

        Console.WriteLine();
        Console.WriteLine("Fetching BTC-USDT-SWAP open interest (OKX)...");
        await FetchAndSave(
            okx,
            provider: "OkxDerivatives",
            symbol: "BTC-USDT-SWAP_OI",
            timeframe: "1h",
            market: "Derivatives",
            targetPoints: targetPoints,
            outputDir: outputDir);

        Console.WriteLine();
        Console.WriteLine("Fetching Fear & Greed index (alternative.me)...");
        await FetchAndSave(
            fng,
            provider: "AlternativeMe",
            symbol: "FNG_INDEX",
            timeframe: "1d",
            market: "Sentiment",
            targetPoints: 5000, // FNG returns full history in one shot
            outputDir: outputDir);

        Console.WriteLine();
        Console.WriteLine("Cross-series snapshots complete.");
        return 0;
    }

    private static async Task FetchAndSave(
        AccessibleTrader.Sdk.Plugins.BaseMarketDataProvider plugin,
        string provider, string symbol, string timeframe, string market,
        int targetPoints, string outputDir)
    {
        var points = new SortedDictionary<long, double>();
        long? until = null;
        const int maxPages = 30;
        int pageCount = 0;

        while (points.Count < targetPoints && pageCount < maxPages)
        {
            var request = new MarketDataRequest(
                Market: market,
                Symbol: symbol,
                Timeframe: timeframe,
                Limit: 1000,
                Since: null,
                Until: until);

            var (page, _) = await plugin.FetchOhlcvAsync(request);
            pageCount++;

            if (page.Count == 0)
            {
                Console.WriteLine($"  page {pageCount}: empty, stopping.");
                break;
            }

            int newPoints = 0;
            foreach (var bar in page)
            {
                long ts = new DateTimeOffset(bar.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
                if (!points.ContainsKey(ts))
                {
                    // Each cross-series provider packs the value into Open=High=Low=Close.
                    points[ts] = bar.Close;
                    newPoints++;
                }
            }

            var oldest = page[0].Date;
            var newest = page[^1].Date;
            Console.WriteLine($"  page {pageCount}: {page.Count} pts [{oldest:yyyy-MM-dd} → {newest:yyyy-MM-dd}], +{newPoints} new (total {points.Count})");

            // No new points → walking past available history; bail.
            if (newPoints == 0) break;

            // Step backward: next page ends one second before this page's oldest point.
            until = (new DateTimeOffset(oldest, TimeSpan.Zero).ToUnixTimeMilliseconds()) - 1000;
        }

        if (points.Count == 0)
        {
            Console.Error.WriteLine($"  ! fetched zero points for {symbol} — skipping write.");
            return;
        }

        var snap = new CrossSeriesSnapshot
        {
            Provider = provider,
            Symbol = symbol,
            Timeframe = timeframe,
            FetchedUtc = DateTime.UtcNow,
            PointCount = points.Count,
            Points = points.Select(kv => new TimedValue { Ts = kv.Key, Value = kv.Value }).ToList()
        };

        var path = snap.FilePath(outputDir);
        await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(snap, Formatting.None));

        var firstDate = DateTimeOffset.FromUnixTimeMilliseconds(snap.Points[0].Ts).UtcDateTime;
        var lastDate = DateTimeOffset.FromUnixTimeMilliseconds(snap.Points[^1].Ts).UtcDateTime;
        Console.WriteLine($"  wrote {snap.PointCount} points [{firstDate:yyyy-MM-dd} → {lastDate:yyyy-MM-dd}]");
        Console.WriteLine($"    → {path}");
    }
}

/// <summary>
/// Drop-in replacement for the live app's <see cref="CrossSeriesCache"/> that reads from
/// pre-fetched JSON snapshots on disk. At construction time it scans the snapshot directory
/// for files matching <c>xs_*.json</c>, deserializes each into an in-memory list keyed by
/// the same lowercase "{provider}:{symbol}:{timeframe}" cache key the production cache uses.
///
/// <see cref="GetOrFetch"/> never hits the network. If a request comes in for a key that was
/// not snapshotted, returns an empty list — the calling indicator will produce all-NaN
/// component arrays, which is the same behavior the live cache exhibits during a fetch
/// timeout. This means strategies that gate on missing cross-series data will simply
/// fire less, never crash.
/// </summary>
public sealed class SnapshottingCrossSeriesCache : ICrossSeriesCache
{
    private readonly Dictionary<string, IReadOnlyList<(long Ts, double Value)>> _byKey;

    public IReadOnlyDictionary<string, int> AvailableKeys =>
        _byKey.ToDictionary(kv => kv.Key, kv => kv.Value.Count);

    public SnapshottingCrossSeriesCache(string snapshotDir)
    {
        _byKey = new Dictionary<string, IReadOnlyList<(long, double)>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(snapshotDir)) return;

        foreach (var path in Directory.EnumerateFiles(snapshotDir, "xs_*.json"))
        {
            try
            {
                var json = File.ReadAllText(path);
                var snap = JsonConvert.DeserializeObject<CrossSeriesSnapshot>(json);
                if (snap == null || snap.Points.Count == 0) continue;
                var list = snap.Points
                    .Select(p => (p.Ts, p.Value))
                    .OrderBy(p => p.Ts)
                    .ToList();
                _byKey[snap.CacheKey] = list;
            }
            catch
            {
                // Best-effort load: skip corrupt or partial files. The user will see an empty
                // result for that cache key when an indicator asks for it.
            }
        }
    }

    public IReadOnlyList<(long Ts, double Value)> GetOrFetch(CrossSeriesRequest request)
    {
        return _byKey.TryGetValue(request.CacheKey, out var list)
            ? list
            : Array.Empty<(long, double)>();
    }
}
