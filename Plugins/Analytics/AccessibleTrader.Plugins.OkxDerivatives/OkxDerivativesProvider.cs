using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.OkxDerivatives
{
    /// <summary>
    /// OKX perpetual swap derivatives metadata: funding rates and open interest history.
    ///
    /// Companion to <c>BinanceDerivativesProvider</c>. Binance Futures REST is geo-blocked
    /// from a number of jurisdictions (US, UK, parts of EU); Bybit is also CloudFront-blocked
    /// in many of the same places. OKX public REST endpoints remain reachable from those
    /// regions, so this plugin exists to give every user a working source of funding/OI data
    /// regardless of where they're running from.
    ///
    /// No API key required. Free public REST. Rate limit ~20 req/2s on the public endpoints
    /// we touch — we cap well below.
    ///
    /// ── Symbol convention ──────────────────────────────────────────────────────
    /// We deliberately keep the same `*_FUNDING` / `*_OI` suffix scheme that BinanceDerivatives
    /// uses, but the underlying token is OKX's own naming (`BTC-USDT-SWAP`):
    ///
    ///     BTC-USDT-SWAP_FUNDING
    ///     ETH-USDT-SWAP_FUNDING
    ///     BTC-USDT-SWAP_OI
    ///     ETH-USDT-SWAP_OI
    ///
    /// Strategies that leaf on the funding signal don't care which venue the data came from;
    /// the suffix lets the same indicator code path consume either provider's output.
    ///
    /// ── Sampling frequency ─────────────────────────────────────────────────────
    /// OKX funding settles every 8 hours, same as Binance. OI history is offered at 5m / 1H / 1D
    /// via the rubik stat endpoint; we map intermediate timeframes to the closest supported.
    ///
    /// Endpoints used:
    ///   GET /api/v5/public/funding-rate-history?instId=BTC-USDT-SWAP&amp;limit=100
    ///   GET /api/v5/rubik/stat/contracts/open-interest-volume?ccy=BTC&amp;period=1H
    /// </summary>
    public class OkxDerivativesProvider : BaseMarketDataProvider
    {
        // Host-provided HttpClient: 32 MB response cap, 60 s timeout, and an
        // outbound-host allow-list enforced by the DelegatingHandler in
        // MauiPluginHttpClientFactory.
        private readonly HttpClient _http = PluginHostServices.CreateHttpClient(
            providerId: "OkxDerivatives",
            allowedHosts: new[] { "www.okx.com" });

        private readonly RateLimiter _rateLimiter = new(300, TimeSpan.FromMinutes(1));

        // Default symbol catalogue. Users can request any OKX perpetual swap by adding the
        // _FUNDING / _OI suffix; this seeds the dropdown for discovery.
        private static readonly string[] CoreInstruments =
        {
            "BTC-USDT-SWAP",
            "ETH-USDT-SWAP",
            "SOL-USDT-SWAP",
            "BNB-USDT-SWAP",
            "XRP-USDT-SWAP"
        };

        private const string FundingSuffix = "_FUNDING";
        private const string OiSuffix      = "_OI";

        private const string OkxBase = "https://www.okx.com";

        public override string Name        => "OkxDerivatives";
        public override string Description => "OKX perpetual swap funding rates and open interest history (analytics).";
        public override List<MarketType> SupportedMarkets => new() { MarketType.Derivatives };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => false;
        public override bool IsConfigured         => true;
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 100;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        // Human-readable labels for the Price series FriendlyName on analytics tabs.
        // Symbols are encoded as "<BASE>-<QUOTE>-SWAP_FUNDING" or "..._OI". We unpack
        // the suffix and base pair into something speech-friendly.
        public override string GetSymbolDisplayName(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return symbol;

            string metric = "";
            string instrument = symbol;
            if (symbol.EndsWith(FundingSuffix, StringComparison.Ordinal))
            {
                metric = "Funding Rate";
                instrument = symbol[..^FundingSuffix.Length];
            }
            else if (symbol.EndsWith(OiSuffix, StringComparison.Ordinal))
            {
                metric = "Open Interest";
                instrument = symbol[..^OiSuffix.Length];
            }
            // Pretty-print instrument: "BTC-USDT-SWAP" → "BTC/USDT Perp"
            var pair = instrument.Replace("-SWAP", "").Replace('-', '/');
            if (instrument.EndsWith("-SWAP")) pair += " Perp";
            return string.IsNullOrEmpty(metric) ? pair : $"{pair} {metric}";
        }

        // ── Render hints for funding / OI ──────────────────────────────────────────
        // Funding rate: centered-at-zero metric, typically ±0.05% per 8h on major
        // perps. Sign matters — positive = longs paying shorts (crowded longs),
        // negative = shorts paying longs (crowded shorts). Extreme readings are
        // mean-reverting. Range bounds are generous (±1%) to accommodate squeezes
        // without clipping. Reference levels mark neutral (0) and the ±0.05% OB/OS
        // thresholds that drive the OB/OS audio.
        //
        // Open Interest: monotonic positive dollar value — no natural bounds. We
        // leave it as a plain unbounded line (returns null from this switch).
        public override SymbolRenderHints? GetSymbolRenderHints(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return null;

            if (symbol.EndsWith(FundingSuffix, StringComparison.Ordinal))
            {
                return new SymbolRenderHints(
                    RangeMin:       -1.0,   // -1% lower (generous — actual extremes ~-0.3%)
                    RangeMax:        1.0,
                    DisplayType:    ComponentDisplayType.Oscillator,
                    SpeechTemplate: "{name}. {value:F3} percent.",
                    ColorHex:       "#FF9800",
                    ReferenceLevels: new[]
                    {
                        new LevelDescriptor(
                            Name: "Oversold", Value: -0.05,
                            ColorHex: "#26A69A", Dash: DashStyle.Dash,
                            PlayEarcon: true, EarconVolume: 0.5f,
                            ZoneNoiseAmount: 0.2f, ZoneNoiseType: "pink"),
                        new LevelDescriptor(
                            Name: "Neutral", Value: 0.0,
                            ColorHex: "#888888", Dash: DashStyle.Solid),
                        new LevelDescriptor(
                            Name: "Overbought", Value: 0.05,
                            ColorHex: "#EF5350", Dash: DashStyle.Dash,
                            PlayEarcon: true, EarconVolume: 0.5f,
                            ZoneNoiseAmount: 0.2f, ZoneNoiseType: "pink"),
                    });
            }

            // Open Interest — unbounded, plain line. Default null falls through to the
            // metadata default render (solid white line on the main pane).
            return null;
        }

        public override List<string> NativelySupportedTimeframes => new()
        {
            // OI: 5m / 1h / 1d native. Funding: every 8h regardless of timeframe asked.
            StandardTimeframes.FiveMinutes,
            StandardTimeframes.FifteenMinutes,
            StandardTimeframes.ThirtyMinutes,
            StandardTimeframes.OneHour,
            StandardTimeframes.FourHours,
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

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(NativelySupportedTimeframes);

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            var symbols = new List<string>(CoreInstruments.Length * 2);
            foreach (var inst in CoreInstruments)
            {
                symbols.Add(inst + FundingSuffix);
                symbols.Add(inst + OiSuffix);
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
                if (symbol.EndsWith(FundingSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var inst = symbol.Substring(0, symbol.Length - FundingSuffix.Length);
                    return await FetchFundingHistoryAsync(inst, request);
                }
                if (symbol.EndsWith(OiSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var inst = symbol.Substring(0, symbol.Length - OiSuffix.Length);
                    return await FetchOpenInterestHistoryAsync(inst, request);
                }
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"OkxDerivatives fetch error ({symbol}): {ex.Message}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        /// <summary>
        /// GET /api/v5/public/funding-rate-history?instId=BTC-USDT-SWAP&amp;limit=100
        ///
        /// Response: { code, msg, data: [ { instId, fundingRate, fundingTime, ... }, ... ] }
        /// Newest-first ordering, so we reverse before emitting OHLCV.
        ///
        /// Funding rate is returned as a fraction (e.g. "0.0001" = 0.01% per 8h). We multiply
        /// by 100 to match the BinanceDerivatives convention so any indicator written against
        /// either provider sees the same units.
        /// </summary>
        private async Task<(List<Ohlcv>, List<(long, double)>)> FetchFundingHistoryAsync(string instId, MarketDataRequest request)
        {
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                // OKX pagination is confusingly named:
                //   after=<ts>  → return records OLDER than ts (paging backwards in time)
                //   before=<ts> → return records NEWER than ts (paging forwards in time)
                // So request.Until ("give me bars up to this point") maps to OKX `after`,
                // and request.Since ("give me bars from this point on") maps to OKX `before`.
                var url = $"{OkxBase}/api/v5/public/funding-rate-history?instId={instId}&limit=100";
                if (request.Until.HasValue) url += $"&after={request.Until.Value}";
                if (request.Since.HasValue) url += $"&before={request.Since.Value}";

                var json = await _http.GetStringAsync(url);
                var root = JObject.Parse(json);
                var data = root["data"] as JArray;
                if (data == null) return (new List<Ohlcv>(), new List<(long, double)>());

                var bars = new List<Ohlcv>(data.Count);
                foreach (var entry in data)
                {
                    if (!long.TryParse(entry["fundingTime"]?.ToString(), out long ts)) continue;
                    if (!double.TryParse(entry["fundingRate"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double rate)) continue;

                    var date = DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime;
                    double pct = rate * 100.0;
                    bars.Add(new Ohlcv(date, pct, pct, pct, pct, 0));
                }

                // OKX returns newest-first; the rest of the app expects ascending time order.
                bars.Sort((a, b) => a.Date.CompareTo(b.Date));

                var vols = bars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                return (bars, vols);
            });
        }

        /// <summary>
        /// GET /api/v5/rubik/stat/contracts/open-interest-volume?ccy=BTC&amp;period=1H
        ///
        /// Response: { code, data: [ [ ts, oiUsd, volUsd ], ... ] } — newest-first.
        /// This endpoint is per-currency (aggregated across all that currency's contracts on
        /// OKX), so we strip the "-USDT-SWAP" suffix from the instId and pass just the base
        /// asset. Period choices are 5m / 1H / 1D — we round the requested timeframe.
        /// </summary>
        private async Task<(List<Ohlcv>, List<(long, double)>)> FetchOpenInterestHistoryAsync(string instId, MarketDataRequest request)
        {
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                string ccy = ExtractCurrency(instId);
                string period = MapOiPeriod(request.Timeframe);

                // The rubik OI endpoint uses begin/end as a literal time range (begin = older
                // edge, end = newer edge), not OKX's confusing before/after pagination.
                var url = $"{OkxBase}/api/v5/rubik/stat/contracts/open-interest-volume?ccy={ccy}&period={period}";
                if (request.Since.HasValue) url += $"&begin={request.Since.Value}";
                if (request.Until.HasValue) url += $"&end={request.Until.Value}";

                var json = await _http.GetStringAsync(url);
                var root = JObject.Parse(json);
                var data = root["data"] as JArray;
                if (data == null) return (new List<Ohlcv>(), new List<(long, double)>());

                var bars = new List<Ohlcv>(data.Count);
                foreach (var entry in data)
                {
                    // Each entry is a tuple: [timestampMs, oiUsd, volUsd]
                    if (entry is not JArray tuple || tuple.Count < 2) continue;
                    if (!long.TryParse(tuple[0]?.ToString(), out long ts)) continue;
                    if (!double.TryParse(tuple[1]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double oiUsd)) continue;

                    var date = DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime;
                    bars.Add(new Ohlcv(date, oiUsd, oiUsd, oiUsd, oiUsd, 0));
                }

                bars.Sort((a, b) => a.Date.CompareTo(b.Date));

                var vols = bars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                return (bars, vols);
            });
        }

        /// <summary>
        /// "BTC-USDT-SWAP" → "BTC". The rubik OI endpoint is keyed by base currency.
        /// </summary>
        private static string ExtractCurrency(string instId)
        {
            int dash = instId.IndexOf('-');
            return dash > 0 ? instId.Substring(0, dash) : instId;
        }

        /// <summary>
        /// OKX rubik OI history only supports 5m, 1H, 1D. Round to nearest.
        /// </summary>
        private static string MapOiPeriod(string? timeframe) => (timeframe ?? "1h").ToLowerInvariant() switch
        {
            "5m" or "15m" or "30m" => "5m",
            "1h" or "2h" or "4h"   => "1H",
            "6h" or "12h" or "1d" or "1w" => "1D",
            _ => "1H"
        };
    }
}
