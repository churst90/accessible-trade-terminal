using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Plugins.Finra
{
    /// <summary>
    /// FINRA Daily Short Sale Volume — per-symbol short-volume ratio for every US
    /// equity, from FINRA's free public Reg SHO files. The closest thing stocks
    /// have to crypto's funding rate as a daily positioning/crowding proxy.
    ///
    /// ── What the value means ────────────────────────────────────────────────────
    /// ShortVolume / TotalVolume × 100 per trading day (0–100%). This is the share
    /// of off-exchange + TRF volume marked short — NOT short interest (that is the
    /// separate biweekly report). Typical readings sit near 35–50% because market-
    /// maker liquidity provision is marked short; the information is in the
    /// EXTREMES and the trend, not the level: sustained readings far above a
    /// symbol's own normal band indicate aggressive shorting pressure, far below
    /// indicate buyers dominating the tape. Literature on squeeze-vs-informed-
    /// shorts is genuinely two-sided — treat as context, not a signal by itself.
    ///
    /// ── Data source ─────────────────────────────────────────────────────────────
    /// https://cdn.finra.org/equity/regsho/daily/CNMSshvol{yyyyMMdd}.txt
    /// (consolidated NMS file; pipe-delimited: Date|Symbol|ShortVolume|
    /// ShortExemptVolume|TotalVolume|Market). Free, no key, no registration.
    /// One file per trading day covering ALL symbols — the provider fetches the
    /// needed days concurrently (max 6 in flight) and caches each parsed day in
    /// memory, so the first symbol pays the download and every subsequent symbol
    /// on the same session is instant. Files are immutable once published.
    ///
    /// ── Symbol convention ───────────────────────────────────────────────────────
    /// "{TICKER}_SHORTVOL" (e.g. AAPL_SHORTVOL). Any NMS ticker works; the symbol
    /// list returns a starter set of liquid names for the dropdown.
    /// </summary>
    public class FinraShortVolumeProvider : BaseMarketDataProvider
    {
        private const string BaseUrl = "https://cdn.finra.org/equity/regsho/daily";
        private const string Suffix = "_SHORTVOL";
        private const int MaxConcurrentFetches = 6;
        private const int DefaultDays = 250;   // ~1 trading year
        private const int MaxDays = 1250;      // ~5 years — keep session memory sane

        private static readonly string[] StarterSymbols =
        {
            "AAPL", "AMD", "AMZN", "COIN", "GME", "GOOGL", "META", "MSFT",
            "MSTR", "NVDA", "PLTR", "QQQ", "SPY", "TSLA",
        };

        private readonly HttpClient _http = PluginHostServices.CreateHttpClient(
            providerId: "FINRA",
            allowedHosts: new[] { "cdn.finra.org" });

        // Per-day parsed cache: date → (symbol → shortPct). A day file is ~500 KB
        // raw and parses to a few thousand entries; immutable, so no expiry. A
        // missing entry with a true marker means "fetched, market holiday/404".
        private readonly ConcurrentDictionary<DateTime, Dictionary<string, double>?> _dayCache = new();
        private readonly SemaphoreSlim _fetchGate = new(MaxConcurrentFetches);

        private readonly RateLimiter _rateLimiter = new(120, TimeSpan.FromMinutes(1));

        public override string Name        => "FINRA";
        public override string Description => "FINRA daily short sale volume — per-symbol short-volume ratio for US equities (free, no key).";
        public override List<MarketType> SupportedMarkets => new() { MarketType.Derivatives };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => false;
        public override bool IsConfigured         => true;
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => MaxDays;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        public override string GetSymbolDisplayName(string symbol)
        {
            var t = TickerOf(symbol);
            return t == null ? symbol : $"{t} — Daily Short Volume % of Total";
        }

        public override SymbolRenderHints? GetSymbolRenderHints(string symbol)
        {
            if (TickerOf(symbol) == null) return null;
            return new SymbolRenderHints(
                RangeMin:      0,
                RangeMax:      100,
                DisplayType:   ComponentDisplayType.Oscillator,
                SpeechTemplate:"{name}. {value:F0} percent short.",
                ColorHex:      "#FF8A65",
                ReferenceLevels: new[]
                {
                    new LevelDescriptor(
                        Name: "Heavy shorting", Value: 60,
                        ColorHex: "#EF5350", Dash: DashStyle.Dash,
                        PlayEarcon: true, EarconVolume: 0.6f,
                        ZoneNoiseAmount: 0.25f, ZoneNoiseType: "pink"),
                    new LevelDescriptor(
                        Name: "Typical band", Value: 45,
                        ColorHex: "#888888", Dash: DashStyle.Dot),
                    new LevelDescriptor(
                        Name: "Buyers dominant", Value: 30,
                        ColorHex: "#26A69A", Dash: DashStyle.Dash,
                        PlayEarcon: true, EarconVolume: 0.6f,
                        ZoneNoiseAmount: 0.25f, ZoneNoiseType: "pink"),
                });
        }

        public override List<string> NativelySupportedTimeframes => new()
        {
            StandardTimeframes.OneDay
        };

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

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
            => Task.FromResult(StarterSymbols.Select(s => s + Suffix).ToList());

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(NativelySupportedTimeframes);

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            string? ticker = TickerOf(request.Symbol);
            if (ticker == null)
                return (new List<Ohlcv>(), new List<(long, double)>());

            try
            {
                // Determine which calendar days to cover. Weekends are skipped up
                // front; holidays resolve to a 404 that is cached as an empty day.
                int wanted = request.Limit > 0 ? Math.Min(request.Limit, MaxDays) : DefaultDays;
                var end = request.Until.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime.Date
                    : DateTime.UtcNow.Date;
                var start = request.Since.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime.Date
                    : end.AddDays(-(int)(wanted * 1.5) - 7); // calendar padding for weekends/holidays

                var days = new List<DateTime>();
                for (var d = start; d <= end; d = d.AddDays(1))
                    if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                        days.Add(d);
                // Newest days matter most when Limit trims: fetch from the end.
                if (days.Count > (int)(wanted * 1.2) + 5)
                    days = days.Skip(days.Count - ((int)(wanted * 1.2) + 5)).ToList();

                await Task.WhenAll(days.Select(EnsureDayAsync));

                var bars = new List<Ohlcv>(days.Count);
                foreach (var day in days)
                {
                    if (!_dayCache.TryGetValue(day, out var map) || map == null) continue;
                    if (!map.TryGetValue(ticker, out double pct)) continue;
                    bars.Add(new Ohlcv(DateTime.SpecifyKind(day, DateTimeKind.Utc), pct, pct, pct, pct, 0));
                }

                if (request.Limit > 0 && bars.Count > request.Limit)
                    bars = bars.Skip(bars.Count - request.Limit).ToList();

                var vols = bars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                return (bars, vols);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"FINRA fetch error: {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        private async Task EnsureDayAsync(DateTime day)
        {
            if (_dayCache.ContainsKey(day)) return;
            await _fetchGate.WaitAsync();
            try
            {
                if (_dayCache.ContainsKey(day)) return;
                var parsed = await _rateLimiter.ExecuteAsync(() => FetchDayAsync(day));
                _dayCache[day] = parsed;
            }
            catch
            {
                // Transient failure: leave uncached so a later request can retry.
            }
            finally
            {
                _fetchGate.Release();
            }
        }

        private async Task<Dictionary<string, double>?> FetchDayAsync(DateTime day)
        {
            string url = $"{BaseUrl}/CNMSshvol{day:yyyyMMdd}.txt";
            using var resp = await _http.GetAsync(url);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return null; // market holiday (or not yet published) — cache the miss
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync();
            return ParseDayFile(body);
        }

        /// <summary>
        /// Parses a Reg SHO daily file (Date|Symbol|ShortVolume|ShortExemptVolume|
        /// TotalVolume|Market) into symbol → short-% map. Internal for tests.
        /// </summary>
        internal static Dictionary<string, double> ParseDayFile(string body)
        {
            var map = new Dictionary<string, double>(8192, StringComparer.OrdinalIgnoreCase);
            foreach (var line in body.Split('\n'))
            {
                var parts = line.Split('|');
                if (parts.Length < 5) continue;
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double shortVol)) continue;
                if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double totalVol)) continue;
                if (totalVol < 1) continue;
                map[parts[1].Trim()] = shortVol / totalVol * 100.0;
            }
            return map;
        }

        private static string? TickerOf(string? symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;
            string s = symbol.Trim().ToUpperInvariant();
            if (s.EndsWith(Suffix, StringComparison.Ordinal)) s = s[..^Suffix.Length];
            // NMS tickers: 1-5 letters plus optional class suffix like BRK.B
            if (s.Length is < 1 or > 7) return null;
            foreach (char c in s)
                if (!char.IsLetter(c) && c != '.') return null;
            return s;
        }
    }
}
