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
        private const string QueryApiUrl = "https://api.finra.org/data/group/otcMarket/name/equityShortInterestStandardized";
        private const string Suffix = "_SHORTVOL";
        private const string SuffixShortInt = "_SHORTINT";
        private const string SuffixDtc = "_DTC";

        // Biweekly short-interest settlements publish roughly nine business days
        // after the settlement date (FINRA's dissemination schedule). Stamp bars at
        // settlement + this many calendar days so a backtest can never see a value
        // before it was public — the same release-honesty rule as the CFTC provider.
        private const int ShortIntPublicationLagDays = 13;
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
            allowedHosts: new[] { "cdn.finra.org", "api.finra.org" });

        // Per-day parsed cache: date → (symbol → shortPct). A day file is ~500 KB
        // raw and parses to a few thousand entries; immutable, so no expiry. A
        // missing entry with a true marker means "fetched, market holiday/404".
        private readonly ConcurrentDictionary<DateTime, Dictionary<string, double>?> _dayCache = new();

        // Per-ticker short-interest cache: (settlement-stamped date, shares short,
        // days to cover). Biweekly data, immutable once published — no expiry.
        private readonly ConcurrentDictionary<string, IReadOnlyList<(DateTime Date, double Shares, double Dtc)>> _shortIntCache
            = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _fetchGate = new(MaxConcurrentFetches);

        private readonly RateLimiter _rateLimiter = new(120, TimeSpan.FromMinutes(1));

        public override string Name        => "FINRA";
        public override string Description => "FINRA short-sale data (free, no key): daily short-volume ratio for every US " +
            "equity ({TICKER}_SHORTVOL), plus biweekly short interest ({TICKER}_SHORTINT, shares) and days-to-cover " +
            "({TICKER}_DTC) for OTC securities. NOTE: FINRA publishes short INTEREST for OTC issues only — " +
            "exchange-listed symbols return no rows there; use the daily short-volume ratio for listed names.";
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
            var (t, kind) = ParseSymbol(symbol);
            return (t, kind) switch
            {
                (null, _) => symbol,
                (_, SeriesKind.ShortInterest) => $"{t} — Short Interest (shares, biweekly, OTC only)",
                (_, SeriesKind.DaysToCover)   => $"{t} — Days to Cover (biweekly, OTC only)",
                _ => $"{t} — Daily Short Volume % of Total",
            };
        }

        public override SymbolRenderHints? GetSymbolRenderHints(string symbol)
        {
            var (t, kind) = ParseSymbol(symbol);
            if (t == null) return null;
            if (kind == SeriesKind.ShortInterest)
                return new SymbolRenderHints(
                    DisplayType:   ComponentDisplayType.Oscillator,
                    SpeechTemplate:"{name}. {value:N0} shares short.",
                    ColorHex:      "#F06292");
            if (kind == SeriesKind.DaysToCover)
                return new SymbolRenderHints(
                    RangeMin:      0,
                    DisplayType:   ComponentDisplayType.Oscillator,
                    SpeechTemplate:"{name}. {value:F1} days to cover.",
                    ColorHex:      "#BA68C8",
                    ReferenceLevels: new[]
                    {
                        new LevelDescriptor(
                            Name: "Crowded short (squeeze fuel)", Value: 8,
                            ColorHex: "#EF5350", Dash: DashStyle.Dash,
                            PlayEarcon: true, EarconVolume: 0.6f,
                            ZoneNoiseAmount: 0.25f, ZoneNoiseType: "pink"),
                        new LevelDescriptor(
                            Name: "Elevated", Value: 3,
                            ColorHex: "#888888", Dash: DashStyle.Dot),
                    });
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
            var (ticker, kind) = ParseSymbol(request.Symbol);
            if (ticker == null)
                return (new List<Ohlcv>(), new List<(long, double)>());

            if (kind is SeriesKind.ShortInterest or SeriesKind.DaysToCover)
                return await FetchShortInterestAsync(ticker, kind, request);

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

        internal enum SeriesKind { ShortVolume, ShortInterest, DaysToCover }

        /// <summary>
        /// Biweekly short interest / days-to-cover from FINRA's public Query API
        /// (equityShortInterestStandardized — OTC securities only; exchange-listed
        /// symbols legitimately return zero rows). One POST per ticker per session.
        /// </summary>
        private async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchShortInterestAsync(
            string ticker, SeriesKind kind, MarketDataRequest request)
        {
            try
            {
                if (!_shortIntCache.TryGetValue(ticker, out var rows))
                {
                    rows = await _rateLimiter.ExecuteAsync(() => QueryShortInterestAsync(ticker));
                    _shortIntCache[ticker] = rows;
                }

                var bars = new List<Ohlcv>(rows.Count);
                foreach (var (date, shares, dtc) in rows)
                {
                    double v = kind == SeriesKind.DaysToCover ? dtc : shares;
                    bars.Add(new Ohlcv(date, v, v, v, v, 0));
                }

                if (request.Since.HasValue)
                {
                    var sinceDt = DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime;
                    bars = bars.FindAll(b => b.Date >= sinceDt);
                }
                if (request.Until.HasValue)
                {
                    var untilDt = DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime;
                    bars = bars.FindAll(b => b.Date <= untilDt);
                }
                if (request.Limit > 0 && bars.Count > request.Limit)
                    bars = bars.GetRange(bars.Count - request.Limit, request.Limit);

                var vols = bars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                return (bars, vols);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"FINRA short-interest fetch error: {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        private async Task<IReadOnlyList<(DateTime Date, double Shares, double Dtc)>> QueryShortInterestAsync(string ticker)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, QueryApiUrl);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Content = new StringContent(
                "{\"limit\":5000,\"compareFilters\":[{\"compareType\":\"EQUAL\"," +
                "\"fieldName\":\"securitiesInformationProcessorSymbolIdentifier\"," +
                "\"fieldValue\":\"" + ticker + "\"}]}",
                System.Text.Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req);
            if (resp.StatusCode == HttpStatusCode.NoContent)
                return Array.Empty<(DateTime, double, double)>(); // not an OTC issue — no data by design
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync();
            return ParseShortInterestJson(body);
        }

        /// <summary>
        /// Parses the Query API JSON rows into settlement-stamped points, shifted by
        /// <see cref="ShortIntPublicationLagDays"/> for release honesty. Internal for tests.
        /// </summary>
        internal static IReadOnlyList<(DateTime Date, double Shares, double Dtc)> ParseShortInterestJson(string json)
        {
            var rows = Newtonsoft.Json.Linq.JArray.Parse(json);
            var list = new List<(DateTime, double, double)>(rows.Count);
            foreach (var r in rows)
            {
                if (!DateTime.TryParse(r["settlementDate"]?.ToString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal, out var settle)) continue;
                if (!double.TryParse(r["currentShortPositionQuantity"]?.ToString(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double shares)) continue;
                double.TryParse(r["daysToCoverQuantity"]?.ToString(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double dtc);
                var published = DateTime.SpecifyKind(settle.Date, DateTimeKind.Utc)
                                        .AddDays(ShortIntPublicationLagDays);
                list.Add((published, shares, dtc));
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
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

        internal static (string? Ticker, SeriesKind Kind) ParseSymbol(string? symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return (null, SeriesKind.ShortVolume);
            string s = symbol.Trim().ToUpperInvariant();
            SeriesKind kind = SeriesKind.ShortVolume;
            if (s.EndsWith(SuffixShortInt, StringComparison.Ordinal)) { kind = SeriesKind.ShortInterest; s = s[..^SuffixShortInt.Length]; }
            else if (s.EndsWith(SuffixDtc, StringComparison.Ordinal)) { kind = SeriesKind.DaysToCover; s = s[..^SuffixDtc.Length]; }
            else if (s.EndsWith(Suffix, StringComparison.Ordinal)) { s = s[..^Suffix.Length]; }
            if (s.Length is < 1 or > 7) return (null, kind);
            foreach (char c in s)
                if (!char.IsLetter(c) && c != '.') return (null, kind);
            return (s, kind);
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
