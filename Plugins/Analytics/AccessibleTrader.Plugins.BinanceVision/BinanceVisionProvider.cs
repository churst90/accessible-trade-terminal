using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Plugins.BinanceVision
{
    /// <summary>
    /// Binance Vision free-archive analytics provider. Pulls historical funding-rate and
    /// open-interest data from the public data.binance.vision S3 bucket — the only source
    /// of multi-year derivatives history at zero cost since Coinglass / CryptoQuant
    /// monetized their APIs in 2025. No API key, no rate-limit signing, no auth of any
    /// kind; it is simply a static CDN of monthly ZIP archives.
    ///
    /// ── Why this exists alongside BinanceDerivatives ───────────────────────────
    /// The <c>BinanceDerivatives</c> plugin hits the live <c>fapi.binance.com</c> REST
    /// API. That path works for the last few weeks of data but is rate-limited and
    /// doesn't ship the full multi-year history a backtester needs (the OI endpoint in
    /// particular caps at 30 days). Binance Vision ships the canonical monthly/daily
    /// archives that Binance itself uses for reference, and we pull those directly.
    ///
    /// ── Symbol convention ───────────────────────────────────────────────────────
    /// Mirrors <c>BinanceDerivativesProvider</c> so the two plugins expose identical
    /// symbol strings and downstream indicators can switch data sources transparently:
    ///
    ///     BTCUSDT_FUNDING   historical 8h funding rate, percent per 8h
    ///     ETHUSDT_FUNDING
    ///     BTCUSDT_OI        historical open interest in USD value (EOD sample)
    ///     ETHUSDT_OI
    ///
    /// Funding rates are returned multiplied by 100 so the displayed value is percent
    /// per 8h (matching BinanceDerivatives). Raw archive values are fractions (0.0001
    /// = 0.01% per 8h) and we normalize at the fetch boundary.
    ///
    /// ── History depth ──────────────────────────────────────────────────────────
    /// Contract-launch dates per symbol are hard-coded below. Walking earlier just
    /// yields 404s, which we silently skip. Typical depth as of 2026:
    ///   BTCUSDT_FUNDING: 2019-09 → present   (~6 years, ~6800 rows)
    ///   BTCUSDT_OI:       2020-11 → present   (~5 years daily)
    ///
    /// ── Caching ────────────────────────────────────────────────────────────────
    /// In-memory per-symbol cache so the cross-series cache's first-fetch walk-back
    /// pagination doesn't cause duplicate archive downloads within a session.
    /// Archive data is immutable so there is no cache expiry — later calls re-read
    /// the same monthly files if the process is restarted.
    /// </summary>
    public class BinanceVisionProvider : BaseMarketDataProvider
    {
        // Hard caps to protect against a compromised CDN / DNS-hijacked endpoint:
        //   - Response-size cap stops a 5 GB download from blowing RAM.
        //   - Decompression cap stops a classic zip bomb (small archive, huge expansion).
        // Real monthly funding archives are <1 MB compressed, <50 MB expanded.
        private const int MaxArchiveBytes        = 64 * 1024 * 1024;   // 64 MB compressed
        private const long MaxUncompressedBytes  = 256 * 1024 * 1024;  // 256 MB expanded

        // Host-provided HttpClient with the standard caps + outbound allow-list.
        // Note: we lose the custom `MaxConnectionsPerServer = 16` that the
        // factory can't express (handler is owned by the factory). The parallel
        // OI walk below uses a SemaphoreSlim capped at 8 workers, so the
        // connection ceiling never binds in practice — the default `Int32.MaxValue`
        // is fine.
        private readonly HttpClient _http = PluginHostServices.CreateHttpClient(
            providerId: "BinanceVision",
            allowedHosts: new[] { "data.binance.vision" },
            maxResponseBytes: MaxArchiveBytes,
            userAgent: "AccessibleTrader-BinanceVision/1.0");

        // Per-symbol in-memory cache. Key = symbol (e.g. "BTCUSDT_FUNDING"), value = sorted
        // list of (ts, percent). Populated synchronously on first fetch, immutable afterwards.
        private readonly ConcurrentDictionary<string, IReadOnlyList<Ohlcv>> _cache = new();

        // ── Funding archive base ───────────────────────────────────────────────
        private const string FundingBase = "https://data.binance.vision/data/futures/um/monthly/fundingRate";
        // ── OI (metrics) archive base ──────────────────────────────────────────
        private const string OiBase      = "https://data.binance.vision/data/futures/um/daily/metrics";

        private const string FundingSuffix = "_FUNDING";
        private const string OiSuffix      = "_OI";

        // Contract launch dates (month) per USDT-perp. Walking earlier yields 404s.
        private static readonly Dictionary<string, DateTime> FundingStartMonth = new(StringComparer.OrdinalIgnoreCase)
        {
            ["BTCUSDT"]  = new(2019, 9, 1),
            ["ETHUSDT"]  = new(2019, 11, 1),
            ["XRPUSDT"]  = new(2020, 1, 1),
            ["SOLUSDT"]  = new(2020, 9, 1),
            ["DOGEUSDT"] = new(2020, 7, 1),
            ["ADAUSDT"]  = new(2020, 2, 1),
            ["LTCUSDT"]  = new(2019, 11, 1),
            ["BNBUSDT"]  = new(2020, 2, 1),
            ["AVAXUSDT"] = new(2020, 9, 1),
            ["LINKUSDT"] = new(2020, 1, 1),
            ["MATICUSDT"] = new(2020, 10, 1),
            ["ATOMUSDT"] = new(2020, 2, 1),
            ["DOTUSDT"]  = new(2020, 8, 1),
        };

        // Daily metrics archive start date. Binance started shipping metrics ZIPs around 2020-11.
        private static readonly DateTime OiStartDate = new(2020, 11, 1);

        // Concurrency for OI daily-file fetch (Binance Vision tolerates parallel GETs fine).
        private const int OiConcurrency = 8;

        private static readonly string[] CoreSymbols =
        {
            "BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "DOGEUSDT", "ADAUSDT", "LTCUSDT", "BNBUSDT"
        };

        public override string Name        => "BinanceVision";
        public override string Description => "Binance Vision public archive — free multi-year history for funding rate and open interest (analytics).";
        public override List<MarketType> SupportedMarkets => new() { MarketType.Derivatives };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => false;
        public override bool IsConfigured         => true;
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 10000;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        public override List<string> NativelySupportedTimeframes => new()
        {
            // Funding settles every 8h; OI is daily in the archives. We advertise the
            // common downstream choices but the actual data is always 8h (funding) or
            // 1d (OI) — CrossSeriesForwardFill handles forward-fill onto any chart TF.
            StandardTimeframes.OneHour,
            StandardTimeframes.FourHours,
            StandardTimeframes.OneDay
        };

        public override string GetSymbolDisplayName(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return symbol;
            string metric = "", pair = symbol;
            if (symbol.EndsWith(FundingSuffix, StringComparison.OrdinalIgnoreCase))
            {
                metric = "Funding Rate";
                pair = symbol[..^FundingSuffix.Length];
            }
            else if (symbol.EndsWith(OiSuffix, StringComparison.OrdinalIgnoreCase))
            {
                metric = "Open Interest";
                pair = symbol[..^OiSuffix.Length];
            }
            foreach (var quote in new[] { "USDT", "USDC", "BUSD" })
            {
                if (pair.EndsWith(quote, StringComparison.OrdinalIgnoreCase) && pair.Length > quote.Length)
                {
                    pair = $"{pair[..^quote.Length]}/{quote}";
                    break;
                }
            }
            return string.IsNullOrEmpty(metric) ? pair : $"{pair} {metric}";
        }

        public override void Configure(Dictionary<string, string> config) { }

        public override Task EnsureConnectedAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Connected);
            return Task.CompletedTask;
        }

        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;

        public override Task DisconnectAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(NativelySupportedTimeframes);

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            var symbols = new List<string>(CoreSymbols.Length * 2);
            foreach (var s in CoreSymbols)
            {
                symbols.Add(s + FundingSuffix);
                symbols.Add(s + OiSuffix);
            }
            return Task.FromResult(symbols);
        }

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            string symbol = request.Symbol ?? string.Empty;
            try
            {
                IReadOnlyList<Ohlcv> all;
                if (symbol.EndsWith(FundingSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var underlying = symbol[..^FundingSuffix.Length].ToUpperInvariant();
                    all = await GetOrFetchFundingAsync(underlying, symbol);
                }
                else if (symbol.EndsWith(OiSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var underlying = symbol[..^OiSuffix.Length].ToUpperInvariant();
                    all = await GetOrFetchOiAsync(underlying, symbol);
                }
                else
                {
                    return (new List<Ohlcv>(), new List<(long, double)>());
                }

                // Apply Since/Until/Limit windowing from the request so the cross-series
                // cache's walk-back pagination protocol works as expected. CrossSeriesCache
                // issues a sequence of requests with decreasing Until, so we slice the
                // cached full series by those bounds.
                IEnumerable<Ohlcv> windowed = all;
                if (request.Since.HasValue)
                {
                    var sinceDt = DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime;
                    windowed = windowed.Where(b => b.Date >= sinceDt);
                }
                if (request.Until.HasValue)
                {
                    var untilDt = DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime;
                    windowed = windowed.Where(b => b.Date <= untilDt);
                }
                var result = windowed.ToList();
                if (request.Limit > 0 && result.Count > request.Limit)
                {
                    // Take the most-recent N (matching how paginated fetchers work).
                    result = result.Skip(result.Count - request.Limit).ToList();
                }
                var vols = result.Select(b => (new DateTimeOffset(b.Date, TimeSpan.Zero).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                return (result, vols);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"BinanceVision fetch error ({symbol}): {ex.Message}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        // ── Funding: monthly ZIP archive walk ──────────────────────────────────

        private async Task<IReadOnlyList<Ohlcv>> GetOrFetchFundingAsync(string underlying, string fullSymbol)
        {
            if (_cache.TryGetValue(fullSymbol, out var cached)) return cached;

            if (!FundingStartMonth.TryGetValue(underlying, out var startMonth))
            {
                // Unknown symbol — default to the launch date of the earliest contract
                // and let the server 404 past-bounds months. Better than hard-rejecting
                // user-added symbols.
                startMonth = new DateTime(2019, 9, 1);
            }

            var points = new SortedDictionary<long, double>();
            var endMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            for (var month = startMonth; month <= endMonth; month = month.AddMonths(1))
            {
                var url = $"{FundingBase}/{underlying}/{underlying}-fundingRate-{month:yyyy-MM}.zip";
                try
                {
                    using var response = await _http.GetAsync(url).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) continue;
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    ParseFundingZipInto(bytes, points);
                }
                catch
                {
                    // Network hiccup on a single monthly file — log and move on.
                    _errorStream.OnNext($"BinanceVision funding {underlying} {month:yyyy-MM}: fetch error");
                }
            }

            // Convert raw fraction to percent per 8h so the on-chart oscillator shows
            // readable numbers (0.01 for neutral instead of 0.0001). Matches
            // BinanceDerivativesProvider convention so strategies see the same scale
            // regardless of which source the cache serves from.
            var bars = points
                .Select(kv =>
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds(kv.Key).UtcDateTime;
                    double pct = kv.Value * 100.0;
                    return new Ohlcv(dt, pct, pct, pct, pct, 0);
                })
                .ToList();

            _cache[fullSymbol] = bars;
            return bars;
        }

        private static void ParseFundingZipInto(byte[] zipBytes, SortedDictionary<long, double> sink)
        {
            using var ms = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            long totalUncompressed = 0;
            foreach (var entry in zip.Entries)
            {
                // Defense in depth: zip-slip guard even though we never extract to disk.
                if (entry.FullName.Contains("..", StringComparison.Ordinal)
                    || entry.FullName.Contains(":", StringComparison.Ordinal))
                    continue;
                if (!entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
                // Header says this entry would expand past our cap — refuse.
                if (entry.Length > MaxUncompressedBytes - totalUncompressed) continue;

                using var rawStream = entry.Open();
                using var bounded = new BoundedReadStream(rawStream, MaxUncompressedBytes - totalUncompressed);
                using var reader = new StreamReader(bounded, Encoding.UTF8);
                reader.ReadLine(); // header row
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var parts = line.Split(',');
                    if (parts.Length < 3) continue;
                    if (!long.TryParse(parts[0], out var ts)) continue;
                    if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)) continue;
                    sink[ts] = rate;
                }
                totalUncompressed += bounded.BytesRead;
                if (totalUncompressed >= MaxUncompressedBytes) break;
            }
        }

        // ── Open Interest: daily metrics ZIP walk ──────────────────────────────

        private async Task<IReadOnlyList<Ohlcv>> GetOrFetchOiAsync(string underlying, string fullSymbol)
        {
            if (_cache.TryGetValue(fullSymbol, out var cached)) return cached;

            var endDate = DateTime.UtcNow.Date.AddDays(-1);
            var dates = new List<DateTime>();
            for (var d = OiStartDate; d <= endDate; d = d.AddDays(1)) dates.Add(d);

            var results = new ConcurrentDictionary<long, double>();
            using var sem = new SemaphoreSlim(OiConcurrency, OiConcurrency);
            var tasks = new List<Task>(dates.Count);
            foreach (var date in dates)
            {
                await sem.WaitAsync().ConfigureAwait(false);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var url = $"{OiBase}/{underlying}/{underlying}-metrics-{date:yyyy-MM-dd}.zip";
                        using var resp = await _http.GetAsync(url).ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode) return;
                        var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        var (ts, oi) = ExtractEodOi(bytes);
                        if (ts.HasValue && oi.HasValue) results[ts.Value] = oi.Value;
                    }
                    catch (HttpRequestException) { /* Single day 404 / network blip -- skip and continue. */ }
                    catch (InvalidDataException)  { /* BoundedReadStream tripped (zip bomb) -- skip day. */ }
                    finally { sem.Release(); }
                }));
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);

            var bars = results
                .OrderBy(kv => kv.Key)
                .Select(kv =>
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds(kv.Key).UtcDateTime;
                    double oi = kv.Value;
                    return new Ohlcv(dt, oi, oi, oi, oi, 0);
                })
                .ToList();

            _cache[fullSymbol] = bars;
            return bars;
        }

        private static (long? Ts, double? Oi) ExtractEodOi(byte[] zipBytes)
        {
            try
            {
                using var ms = new MemoryStream(zipBytes);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.Contains("..", StringComparison.Ordinal)
                        || entry.FullName.Contains(":", StringComparison.Ordinal))
                        continue;
                    if (!entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.Length > MaxUncompressedBytes) continue;
                    using var rawStream = entry.Open();
                    using var stream = new BoundedReadStream(rawStream, MaxUncompressedBytes);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    reader.ReadLine(); // header
                    string? lastDataLine = null, line;
                    while ((line = reader.ReadLine()) != null)
                        if (line.Length > 0) lastDataLine = line;
                    if (lastDataLine == null) return (null, null);
                    var parts = lastDataLine.Split(',');
                    // create_time, symbol, sum_open_interest, sum_open_interest_value, ...
                    if (parts.Length < 4) return (null, null);
                    if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                        return (null, null);
                    if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var oiVal))
                        return (null, null);
                    return (new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeMilliseconds(), oiVal);
                }
            }
            // Defuse-on-any for zip inputs -- a malformed day archive shouldn't kill
            // the whole backfill. BoundedReadStream already guards decompression size.
            catch (InvalidDataException)  { /* corrupt zip or zip-bomb tripped */ }
            catch (IOException)           { /* entry read failure */ }
            catch (FormatException)       { /* csv row unparseable */ }
            return (null, null);
        }

        /// <summary>
        /// Wraps a <see cref="Stream"/> and throws <see cref="InvalidDataException"/>
        /// the moment total read bytes exceed <c>maxBytes</c>. Used to defuse zip
        /// bombs that report a small <see cref="ZipArchiveEntry.Length"/> but stream
        /// far more data at decompression time.
        /// </summary>
        private sealed class BoundedReadStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _maxBytes;
            public long BytesRead { get; private set; }

            public BoundedReadStream(Stream inner, long maxBytes)
            {
                _inner = inner;
                _maxBytes = maxBytes;
            }

            public override bool CanRead  => _inner.CanRead;
            public override bool CanSeek  => false;
            public override bool CanWrite => false;
            public override long Length   => throw new NotSupportedException();
            public override long Position
            {
                get => BytesRead;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = _inner.Read(buffer, offset, count);
                BytesRead += n;
                if (BytesRead > _maxBytes)
                    throw new InvalidDataException($"Decompressed stream exceeded {_maxBytes} bytes (possible zip bomb).");
                return n;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value)                 => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
