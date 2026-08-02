using AccessibleTrader.Plugins.Bitstamp;
using AccessibleTrader.Plugins.Mexc;
using AccessibleTrader.Plugins.Alpaca;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Newtonsoft.Json;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Pulls historical OHLCV from a chosen provider (Bitstamp or MEXC) via the live
/// provider plugin (instantiated directly, no DI / no plugin loader) and serializes
/// to JSON on disk so the research loop can replay deterministic data offline.
///
/// Walks backward via <c>MarketDataRequest.Until</c> until either <paramref name="targetBars"/>
/// is reached or the provider returns an empty page (history exhausted). Page size
/// is provider-determined (Bitstamp 1000, MEXC 500 per <c>MaxBarsPerRequest</c>).
/// </summary>
public static class SnapshotCommand
{
    public static async Task<int> RunAsync(string symbol, string timeframe, int targetBars, string outputDir, string providerName = "bitstamp", string? apiKey = null, string? apiSecret = null)
    {
        Directory.CreateDirectory(outputDir);

        BaseMarketDataProvider provider;
        string providerLabel;
        int pageLimit;
        string market = "Spot";
        // Pagination direction. Bitstamp/MEXC support `Until`-only walk-back from "now"
        // toward the past. Alpaca's REST historical bar endpoint pages forward from a
        // `Since` timestamp instead — `Until`-only fetches return only the most recent
        // bar. So we run a forward-walk for Alpaca: seed a far-past `Since`, advance
        // one bar past the newest result each iteration, until we have targetBars or
        // the provider returns an empty page (i.e. caught up to the present).
        bool walkForward = false;
        switch (providerName.ToLowerInvariant())
        {
            case "mexc":
                provider = new MexcProvider();
                providerLabel = "mexc";
                pageLimit = 500;
                break;
            case "alpaca":
                // Alpaca needs credentials for market data, unlike every other provider here. Left
                // unchecked, a missing --key produced an empty first page and the message
                // "history exhausted" -- indistinguishable from a symbol that genuinely has no data,
                // and it cost a debugging round.
                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
                {
                    Console.Error.WriteLine("alpaca requires --key and --secret (paper credentials are enough for market data).");
                    return 1;
                }
                var ap = new AlpacaProvider();
                if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret))
                    ap.Configure(new Dictionary<string, string>
                    {
                        ["ApiKey"]    = apiKey,
                        ["ApiSecret"] = apiSecret,
                    });
                provider = ap;
                providerLabel = "alpaca";
                pageLimit = 10000;
                market = "Stock";  // default to equities; "Crypto" also supported
                walkForward = true;
                break;
            case "bitstamp":
            default:
                provider = new BitstampProvider();
                providerLabel = "bitstamp";
                pageLimit = 1000;
                break;
        }
        var stepSeconds = TimeframeToSeconds(timeframe);
        if (stepSeconds <= 0)
        {
            Console.Error.WriteLine($"Unknown timeframe '{timeframe}'.");
            return 1;
        }

        // Providers report fetch failures on their error stream and return an EMPTY page rather
        // than throwing. Nothing here was listening, so an auth failure, a bad symbol and a
        // genuinely exhausted history all printed the same "empty (history exhausted)" line — the
        // silent read-path failure this project keeps rediscovering, this time in its own tooling.
        using var errors = provider.ErrorStream.Subscribe(e => Console.Error.WriteLine($"  ! {e}"));

        // Walk loop. We accumulate into a dictionary keyed by Date so duplicate bars
        // returned across overlapping pages are naturally de-duplicated. Each page costs ~1
        // HTTP request — provider rate limiting is handled inside the provider.
        var bars = new SortedDictionary<DateTime, Ohlcv>();
        long? until = null;
        // Forward-walk seed: 20 years before "now" — well before any equity provider's
        // history depth. Daily equity bars from 2005 are deeper than any retail use needs.
        // Intraday (5m / 1m) on Alpaca only goes back ~6-8 years anyway; the empty-page
        // exit catches the actual history-depth boundary.
        long? since = walkForward
            ? new DateTimeOffset(DateTime.UtcNow.AddYears(-20), TimeSpan.Zero).ToUnixTimeMilliseconds()
            : (long?)null;
        int pageCount = 0;
        // Hard safety cap on requests: lift to 100 so we can fully back-fill multi-year
        // intraday histories (e.g. KAS/USDT 1h since 2022 ≈ 25k bars / 500 per page = 50 pages).
        const int maxPages = 100;

        while (bars.Count < targetBars && pageCount < maxPages)
        {
            var request = new MarketDataRequest(
                Market: market,
                Symbol: symbol,
                Timeframe: timeframe,
                Limit: pageLimit,
                Since: since,
                Until: until);

            var (page, _) = await provider.FetchOhlcvAsync(request);
            pageCount++;

            if (page.Count == 0)
            {
                Console.WriteLine($"  page {pageCount}: empty (history exhausted)");
                break;
            }

            int newBars = 0;
            foreach (var bar in page)
            {
                if (bars.TryAdd(bar.Date, bar)) newBars++;
            }

            var oldest = page[0].Date;
            var newest = page[^1].Date;
            Console.WriteLine($"  page {pageCount}: {page.Count} bars [{oldest:yyyy-MM-dd} → {newest:yyyy-MM-dd}], +{newBars} new (total {bars.Count})");

            // No new bars? We're walking past the boundary of available history; bail.
            if (newBars == 0) break;

            if (walkForward)
            {
                // Step forward: next page starts one second after this page's newest bar.
                since = new DateTimeOffset(newest, TimeSpan.Zero).ToUnixTimeMilliseconds() + 1000;
            }
            else
            {
                // Step backward: next page ends one second before this page's oldest bar.
                until = new DateTimeOffset(oldest, TimeSpan.Zero).ToUnixTimeMilliseconds() - 1000;
            }
        }

        if (bars.Count == 0)
        {
            Console.Error.WriteLine("Snapshot fetched zero bars — aborting write.");
            return 2;
        }

        var ordered = bars.Values.ToList();
        var snapshot = new SnapshotFile
        {
            Provider = providerLabel,
            Symbol = symbol,
            Timeframe = timeframe,
            FetchedUtc = DateTime.UtcNow,
            BarCount = ordered.Count,
            FirstDate = ordered[0].Date,
            LastDate = ordered[^1].Date,
            Bars = ordered
        };

        var safeSymbol = symbol.Replace("/", "_").Replace("\\", "_");
        var path = Path.Combine(outputDir, $"{providerLabel}_{safeSymbol}_{timeframe}.json");
        var json = JsonConvert.SerializeObject(snapshot, Formatting.None);
        await File.WriteAllTextAsync(path, json);

        Console.WriteLine($"Wrote {ordered.Count} bars [{snapshot.FirstDate:yyyy-MM-dd} → {snapshot.LastDate:yyyy-MM-dd}]");
        Console.WriteLine($"  → {path}");
        return 0;
    }

    /// <summary>
    /// One-off helper: aggregates an existing daily snapshot into a higher-timeframe one
    /// by grouping every <paramref name="barsPerGroup"/> daily bars into one synthetic bar.
    /// Used to produce weekly (7) and monthly (30) snapshots from a daily file when the
    /// upstream provider doesn't natively support those steps. OHLCV is aggregated correctly:
    /// open = first, high = max, low = min, close = last, volume = sum.
    /// </summary>
    public static async Task<int> AggregateAsync(string srcPath, int barsPerGroup, string newTimeframe)
    {
        if (!File.Exists(srcPath))
        {
            Console.Error.WriteLine($"Source snapshot not found: {srcPath}");
            return 1;
        }
        if (barsPerGroup < 2)
        {
            Console.Error.WriteLine("barsPerGroup must be >= 2");
            return 1;
        }

        var src = Load(srcPath);
        var ordered = src.Bars.OrderBy(b => b.Date).ToList();
        var aggBars = new List<Ohlcv>();
        for (int i = 0; i + barsPerGroup <= ordered.Count; i += barsPerGroup)
        {
            var group = ordered.GetRange(i, barsPerGroup);
            aggBars.Add(new Ohlcv
            {
                Date   = group[0].Date,
                Open   = group[0].Open,
                High   = group.Max(b => b.High),
                Low    = group.Min(b => b.Low),
                Close  = group[^1].Close,
                Volume = group.Sum(b => b.Volume),
            });
        }

        var outFile = new SnapshotFile
        {
            Provider   = src.Provider,
            Symbol     = src.Symbol,
            Timeframe  = newTimeframe,
            FetchedUtc = DateTime.UtcNow,
            BarCount   = aggBars.Count,
            FirstDate  = aggBars[0].Date,
            LastDate   = aggBars[^1].Date,
            Bars       = aggBars,
        };

        var dir = Path.GetDirectoryName(srcPath) ?? ".";
        var safeSymbol = src.Symbol.Replace("/", "_").Replace("\\", "_");
        // Preserve the source provider name so aggregated MEXC snapshots (and any
        // other future providers) keep their lineage instead of being mislabeled.
        var providerLabel = string.IsNullOrWhiteSpace(src.Provider)
            ? "bitstamp"
            : src.Provider.ToLowerInvariant();
        var outPath = Path.Combine(dir, $"{providerLabel}_{safeSymbol}_{newTimeframe}.json");
        var json = JsonConvert.SerializeObject(outFile, Formatting.None);
        await File.WriteAllTextAsync(outPath, json);

        Console.WriteLine($"Aggregated {ordered.Count} → {aggBars.Count} bars ({barsPerGroup}-bar groups)");
        Console.WriteLine($"  [{outFile.FirstDate:yyyy-MM-dd} → {outFile.LastDate:yyyy-MM-dd}]");
        Console.WriteLine($"  → {outPath}");
        return 0;
    }

    /// <summary>
    /// Reloads a snapshot file written by <see cref="RunAsync"/>. Trivial helper, lives next to
    /// the writer so the file format only needs to change in one place.
    /// </summary>
    public static SnapshotFile Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<SnapshotFile>(json)
            ?? throw new InvalidDataException($"Failed to deserialize snapshot at {path}");
    }

    private static int TimeframeToSeconds(string tf) => tf switch
    {
        "1m" => 60,
        "3m" => 180,
        "5m" => 300,
        "15m" => 900,
        "30m" => 1800,
        "1h" => 3600,
        "2h" => 7200,
        "4h" => 14400,
        "6h" => 21600,
        "12h" => 43200,
        "1d" => 86400,
        "3d" => 259200,
        _ => -1
    };
}

public sealed class SnapshotFile
{
    public string Provider { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Timeframe { get; set; } = "";
    public DateTime FetchedUtc { get; set; }
    public int BarCount { get; set; }
    public DateTime FirstDate { get; set; }
    public DateTime LastDate { get; set; }
    public List<Ohlcv> Bars { get; set; } = new();
}
