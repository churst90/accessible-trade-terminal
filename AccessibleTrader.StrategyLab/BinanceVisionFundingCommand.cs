using System.IO.Compression;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// One-shot downloader for Binance Vision funding-rate history. Pulls monthly ZIP files
/// from data.binance.vision for each requested USDT-margined perpetual symbol from contract
/// inception to the current month, parses the embedded CSV (calc_time, interval_hours,
/// last_funding_rate), and writes the full series as a cross-series snapshot file shaped
/// like every other xs_*.json the harness already understands.
///
/// Why Binance Vision: Coinglass and CryptoQuant both monetized their APIs in 2025-2026.
/// Coinglass Hobbyist ($29/mo) only ships 6-12 days of history — useless for backtest.
/// Binance Vision is the only source that ships multi-year funding rate history at zero cost.
/// BTCUSDT history starts 2019-09 (perpetual launch); ETH 2019-11; XRP/SOL 2020.
///
/// Output: one xs_binancevision_{symbol}_funding_8h.json per symbol. The 8h tag matches
/// Binance's settlement cadence — funding settles every 8 hours regardless of contract.
/// </summary>
public static class BinanceVisionFundingCommand
{
    private const string BaseUrl = "https://data.binance.vision/data/futures/um/monthly/fundingRate";

    // Approximate USDT-perp launch dates. Walking back further than this just yields 404s.
    private static readonly Dictionary<string, DateTime> SymbolStartMonths = new()
    {
        ["BTCUSDT"] = new DateTime(2019, 9, 1),
        ["ETHUSDT"] = new DateTime(2019, 11, 1),
        ["XRPUSDT"] = new DateTime(2020, 1, 1),
        ["SOLUSDT"] = new DateTime(2020, 9, 1),
    };

    public static async Task<int> RunAsync(string outputDir, string[] symbols)
    {
        Directory.CreateDirectory(outputDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Add("User-Agent", "AccessibleTrader-StrategyLab/1.0");

        foreach (var symbol in symbols)
        {
            if (!SymbolStartMonths.TryGetValue(symbol, out var startMonth))
            {
                Console.Error.WriteLine($"Unknown symbol '{symbol}'. Known: {string.Join(", ", SymbolStartMonths.Keys)}");
                continue;
            }

            Console.WriteLine();
            Console.WriteLine($"=== {symbol} funding rate ===");
            Console.WriteLine($"Walking months from {startMonth:yyyy-MM} to current...");

            // Sorted by timestamp so duplicates dedupe naturally and final list is monotonic.
            var allPoints = new SortedDictionary<long, double>();

            var month = startMonth;
            var endMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            int monthsAttempted = 0;
            int monthsHit = 0;

            while (month <= endMonth)
            {
                monthsAttempted++;
                var url = $"{BaseUrl}/{symbol}/{symbol}-fundingRate-{month:yyyy-MM}.zip";
                try
                {
                    using var response = await http.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        // 404s on early months are expected (pre-launch). Don't log them by default.
                        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                            Console.Error.WriteLine($"  {month:yyyy-MM}: HTTP {(int)response.StatusCode}");
                        month = month.AddMonths(1);
                        continue;
                    }

                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    var added = ParseZipInto(bytes, allPoints);
                    monthsHit++;
                    Console.WriteLine($"  {month:yyyy-MM}: +{added} rows (total {allPoints.Count})");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  {month:yyyy-MM}: error — {ex.Message}");
                }

                month = month.AddMonths(1);
            }

            if (allPoints.Count == 0)
            {
                Console.Error.WriteLine($"  No data fetched for {symbol}, skipping write.");
                continue;
            }

            var snap = new CrossSeriesSnapshot
            {
                Provider = "BinanceVision",
                Symbol = $"{symbol}_FUNDING",
                Timeframe = "8h",
                FetchedUtc = DateTime.UtcNow,
                PointCount = allPoints.Count,
                Points = allPoints.Select(kv => new TimedValue { Ts = kv.Key, Value = kv.Value }).ToList()
            };
            var path = snap.FilePath(outputDir);
            await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(snap, Formatting.None));

            var firstDate = DateTimeOffset.FromUnixTimeMilliseconds(snap.Points[0].Ts).UtcDateTime;
            var lastDate = DateTimeOffset.FromUnixTimeMilliseconds(snap.Points[^1].Ts).UtcDateTime;
            Console.WriteLine($"  → wrote {snap.PointCount} points [{firstDate:yyyy-MM-dd} → {lastDate:yyyy-MM-dd}]");
            Console.WriteLine($"    {path}  ({monthsHit}/{monthsAttempted} months hit)");
        }

        return 0;
    }

    /// <summary>
    /// Reads the single CSV file inside a Binance Vision funding-rate ZIP and merges its rows
    /// into the supplied dictionary. Binance ships the CSV with a header row and three columns:
    /// calc_time (unix millis), funding_interval_hours, last_funding_rate. We keep last_funding_rate
    /// as a raw decimal (e.g. 0.0001 = 0.01%/8h, the typical neutral funding) — the consuming
    /// indicator can convert to bps or percent as it sees fit.
    /// </summary>
    private static int ParseZipInto(byte[] zipBytes, SortedDictionary<long, double> sink)
    {
        int added = 0;
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (!entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            reader.ReadLine(); // header row, discard
            string? line = reader.ReadLine();
            while (line != null)
            {
                var parts = line.Split(',');
                if (parts.Length >= 3
                    && long.TryParse(parts[0], out var ts)
                    && double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out var rate))
                {
                    if (!sink.ContainsKey(ts))
                    {
                        sink[ts] = rate;
                        added++;
                    }
                }
                line = reader.ReadLine();
            }
        }
        return added;
    }
}
