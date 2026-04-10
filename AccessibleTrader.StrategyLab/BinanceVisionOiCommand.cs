using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Binance Vision daily-metrics downloader. Pulls one ZIP per day per symbol from
/// data.binance.vision/data/futures/um/daily/metrics/{SYMBOL}/{SYMBOL}-metrics-{YYYY-MM-DD}.zip,
/// extracts the LAST row from each daily file (the 23:55 5-min snapshot — closest to UTC day
/// close), and stores it as a daily (timestamp, sum_open_interest_value) pair.
///
/// USDT-denominated OI value (`sum_open_interest_value`) is preferred over coin-denominated
/// (`sum_open_interest`) because the latter scales mechanically with price and produces
/// spurious "OI growth" during bull runs that's actually just price appreciation.
///
/// Why daily snapshots and not 5-min: our strategies are daily-bar; resampling from 5-min to
/// daily would just throw away the granularity we don't need. Pulling one row per file makes
/// the eventual JSON file ~50KB instead of 14MB per symbol.
///
/// Concurrency: Binance Vision is a static S3 bucket and tolerates parallel GETs fine. We use
/// 8 concurrent requests with a semaphore — keeps the total runtime to ~3-4 minutes for 4
/// symbols × 1900 days each (7600 files) instead of ~25 minutes serial.
///
/// History: BTCUSDT metrics start ~2020-11. ETH/XRP/SOL similarly. Walking back further yields
/// 404s which we silently skip.
/// </summary>
public static class BinanceVisionOiCommand
{
    private const string BaseUrl = "https://data.binance.vision/data/futures/um/daily/metrics";
    private const int Concurrency = 8;

    private static readonly Dictionary<string, DateTime> SymbolStartDates = new()
    {
        ["BTCUSDT"] = new DateTime(2020, 11, 1),
        ["ETHUSDT"] = new DateTime(2020, 11, 1),
        ["XRPUSDT"] = new DateTime(2020, 11, 1),
        ["SOLUSDT"] = new DateTime(2020, 11, 1),
    };

    public static async Task<int> RunAsync(string outputDir, string[] symbols)
    {
        Directory.CreateDirectory(outputDir);
        using var http = new HttpClient(new HttpClientHandler { MaxConnectionsPerServer = 16 })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        http.DefaultRequestHeaders.Add("User-Agent", "AccessibleTrader-StrategyLab/1.0");

        foreach (var symbol in symbols)
        {
            if (!SymbolStartDates.TryGetValue(symbol, out var startDate))
            {
                Console.Error.WriteLine($"Unknown symbol '{symbol}'.");
                continue;
            }

            Console.WriteLine();
            Console.WriteLine($"=== {symbol} OI ===");

            var endDate = DateTime.UtcNow.Date.AddDays(-1);  // yesterday — today's file may not exist yet
            var allDates = new List<DateTime>();
            for (var d = startDate; d <= endDate; d = d.AddDays(1)) allDates.Add(d);
            Console.WriteLine($"Walking {allDates.Count} days from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd} (concurrency={Concurrency})...");

            var results = new ConcurrentDictionary<long, double>();
            var hits = 0;
            var misses = 0;
            var processed = 0;

            using var sem = new SemaphoreSlim(Concurrency, Concurrency);
            var tasks = new List<Task>(allDates.Count);
            foreach (var date in allDates)
            {
                await sem.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var url = $"{BaseUrl}/{symbol}/{symbol}-metrics-{date:yyyy-MM-dd}.zip";
                        using var resp = await http.GetAsync(url);
                        if (!resp.IsSuccessStatusCode)
                        {
                            Interlocked.Increment(ref misses);
                            return;
                        }
                        var bytes = await resp.Content.ReadAsByteArrayAsync();
                        var (ts, oi) = ExtractEodOi(bytes);
                        if (ts.HasValue && oi.HasValue)
                        {
                            results[ts.Value] = oi.Value;
                            Interlocked.Increment(ref hits);
                        }
                    }
                    catch
                    {
                        Interlocked.Increment(ref misses);
                    }
                    finally
                    {
                        sem.Release();
                        var done = Interlocked.Increment(ref processed);
                        if (done % 250 == 0)
                            Console.WriteLine($"  ... {done}/{allDates.Count} processed (hits {hits}, misses {misses})");
                    }
                }));
            }
            await Task.WhenAll(tasks);

            Console.WriteLine($"  Done: {hits} hits, {misses} misses, {results.Count} unique points");
            if (results.Count == 0)
            {
                Console.Error.WriteLine($"  Wrote nothing for {symbol}.");
                continue;
            }

            var sorted = results.OrderBy(kv => kv.Key).ToList();
            var snap = new CrossSeriesSnapshot
            {
                Provider = "BinanceVision",
                Symbol = $"{symbol}_OI",
                Timeframe = "1d",
                FetchedUtc = DateTime.UtcNow,
                PointCount = sorted.Count,
                Points = sorted.Select(kv => new TimedValue { Ts = kv.Key, Value = kv.Value }).ToList()
            };
            var path = snap.FilePath(outputDir);
            await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(snap, Formatting.None));

            var firstDate = DateTimeOffset.FromUnixTimeMilliseconds(snap.Points[0].Ts).UtcDateTime;
            var lastDate = DateTimeOffset.FromUnixTimeMilliseconds(snap.Points[^1].Ts).UtcDateTime;
            Console.WriteLine($"  → wrote {snap.PointCount} points [{firstDate:yyyy-MM-dd} → {lastDate:yyyy-MM-dd}]");
            Console.WriteLine($"    {path}");
        }
        return 0;
    }

    /// <summary>
    /// Reads the embedded CSV (288 rows of 5-min snapshots), returns the (unix-millis, OI value)
    /// from the last data row. Returns nulls if the file is malformed.
    /// </summary>
    private static (long? Ts, double? Oi) ExtractEodOi(byte[] zipBytes)
    {
        try
        {
            using var ms = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                if (!entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                reader.ReadLine(); // header
                string? lastDataLine = null;
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length > 0) lastDataLine = line;
                }
                if (lastDataLine == null) return (null, null);
                var parts = lastDataLine.Split(',');
                // create_time, symbol, sum_open_interest, sum_open_interest_value, ...
                if (parts.Length < 4) return (null, null);
                if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                    return (null, null);
                if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var oiVal))
                    return (null, null);
                return (new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeMilliseconds(), oiVal);
            }
        }
        catch { }
        return (null, null);
    }
}
