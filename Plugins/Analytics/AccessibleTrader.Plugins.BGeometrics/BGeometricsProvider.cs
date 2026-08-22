using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.BGeometrics
{
    /// <summary>
    /// BGeometrics provider — 154+ BTC on-chain metrics from the free bitcoin-data.com API.
    /// Covers MVRV, SOPR, NVT, NUPL, CDD, Hodl Waves, Stock-to-Flow, and many more.
    ///
    /// -- API details ----------------------------------------------------------------
    /// Base URL: https://bitcoin-data.com/v1
    /// Auth: none required for free tier
    /// Rate limit: ~8 requests per hour (be conservative with the free tier)
    /// Endpoints: GET /v1/{metric}?startday=YYYY-MM-DD&amp;endday=YYYY-MM-DD
    /// Response: JSON array of objects with "d" (date), "unixTs" (scientific notation
    ///           string), and a metric-specific value field.
    ///
    /// -- Strategy use ---------------------------------------------------------------
    /// On-chain metrics provide genuinely orthogonal signals to price-based indicators.
    /// MVRV &gt; 3.5 historically marks cycle tops; SOPR &lt; 1.0 = aggregate loss-taking
    /// (capitulation); NUPL extremes map to euphoria / capitulation zones. Combining
    /// these with Cipher / price-action signals adds a non-price conviction layer.
    /// </summary>
    public class BGeometricsProvider : BaseMarketDataProvider
    {
        // Host-provided HttpClient: 32 MB / 60 s + outbound allow-list.
        private readonly HttpClient _http = PluginHostServices.CreateHttpClient(
            providerId: "BGeometrics",
            allowedHosts: new[] { "bitcoin-data.com" });

        private readonly RateLimiter _rateLimiter = new(8, TimeSpan.FromHours(1));

        private const string BaseUrl = "https://bitcoin-data.com/v1";

        // ── Symbol → (endpoint, value-field) mapping ────────────────────────────────
        // Each entry maps a public-facing symbol to the BGeometrics REST endpoint
        // and the JSON field name that holds the metric value in the response.
        private static readonly Dictionary<string, (string Endpoint, string ValueField)> MetricMap =
            new(StringComparer.OrdinalIgnoreCase)
        {
            { "MVRV",             ("mvrv",                    "mvrv")                 },
            { "SOPR",             ("sopr",                    "sopr")                 },
            { "ASOPR",            ("asopr",                   "asopr")                },
            { "STH_SOPR",         ("sth-sopr",                "sthsopr")              },
            { "LTH_SOPR",         ("lth-sopr",                "lthsopr")              },
            { "NVT",              ("nvt",                     "nvt")                  },
            { "NVT_RATIO",        ("nvt-ratio",               "nvtRatio")             },
            { "NVT_SIGNAL",       ("nvt-signal",              "nvtSignal")            },
            { "NUPL",             ("nupl",                    "nupl")                 },
            { "NUPL_LTH",         ("nupl-lth",                "nupllth")              },
            { "NUPL_STH",         ("nupl-sth",                "nuplsth")              },
            { "CDD",              ("cdd",                     "cdd")                  },
            { "CVDD",             ("cvdd",                    "cvdd")                 },
            { "REALIZED_PRICE",   ("realized-price",          "realizedPrice")        },
            { "REALIZED_CAP",     ("realized-cap",            "realizedCap")          },
            { "RESERVE_RISK",     ("reserve-risk",            "reserveRisk")          },
            { "S2F",              ("s2f",                     "s2f")                  },
            { "TERMINAL_PRICE",   ("terminal-price",          "terminalPrice")        },
            { "BALANCED_PRICE",   ("balanced-price",          "balancedPrice")        },
            { "PUELL_MULTIPLE",   ("puell-multiple",          "puellMultiple")        },
            { "FUNDING_RATE",     ("funding-rate",            "fundingRate")          },
            { "OI_FUTURES",       ("open-interest-futures",   "openInterestFutures")  },
            { "ACTIVE_ADDRESSES", ("active-addresses",        "activeAddresses")      },
            { "HODL_WAVES",       ("hodl-waves-supply",       "age_1y_2y")            },
            { "BTC_SUPPLY",       ("supply",                  "supply")               },
            { "MAYER_MULTIPLE",   ("mayer-multiple",          "mayerMultiple")        },
            { "FEAR_GREED",       ("fear-greed",              "fearGreed")            },
            { "PI_CYCLE",         ("pi-cycle",                "piCycle")              },
        };

        // ── Provider metadata ───────────────────────────────────────────────────────

        public override string Name        => "BGeometrics";
        public override string Description => "BGeometrics \u2014 154+ BTC on-chain metrics (MVRV, SOPR, NVT, NUPL, CDD, Hodl Waves, S2F)";
        public override List<MarketType> SupportedMarkets => new() { MarketType.OnChain };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => false;
        public override bool IsConfigured         => true;
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 5000;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        // ── Display names ───────────────────────────────────────────────────────────

        public override string GetSymbolDisplayName(string symbol) => symbol switch
        {
            "MVRV"             => "Market Value to Realized Value",
            "SOPR"             => "Spent Output Profit Ratio",
            "ASOPR"            => "Adjusted Spent Output Profit Ratio",
            "STH_SOPR"         => "Short-Term Holder SOPR",
            "LTH_SOPR"         => "Long-Term Holder SOPR",
            "NVT"              => "Network Value to Transactions",
            "NVT_RATIO"        => "NVT Ratio",
            "NVT_SIGNAL"       => "NVT Signal",
            "NUPL"             => "Net Unrealized Profit/Loss",
            "NUPL_LTH"         => "NUPL Long-Term Holders",
            "NUPL_STH"         => "NUPL Short-Term Holders",
            "CDD"              => "Coin Days Destroyed",
            "CVDD"             => "Cumulative Value Days Destroyed",
            "REALIZED_PRICE"   => "Realized Price",
            "REALIZED_CAP"     => "Realized Cap",
            "RESERVE_RISK"     => "Reserve Risk",
            "S2F"              => "Stock-to-Flow",
            "TERMINAL_PRICE"   => "Terminal Price",
            "BALANCED_PRICE"   => "Balanced Price",
            "PUELL_MULTIPLE"   => "Puell Multiple",
            "FUNDING_RATE"     => "Funding Rate",
            "OI_FUTURES"       => "Open Interest (Futures)",
            "ACTIVE_ADDRESSES" => "Active Addresses",
            "HODL_WAVES"       => "HODL Waves (1-2 Year Band)",
            "BTC_SUPPLY"       => "BTC Supply",
            "MAYER_MULTIPLE"   => "Mayer Multiple",
            "FEAR_GREED"       => "Fear & Greed Index",
            "PI_CYCLE"         => "Pi Cycle Top Indicator",
            _                  => symbol
        };

        // ── Render hints ────────────────────────────────────────────────────────────

        public override SymbolRenderHints? GetSymbolRenderHints(string symbol) => symbol switch
        {
            "MVRV" => new SymbolRenderHints(
                RangeMin:    0,
                RangeMax:    null,
                DisplayType: ComponentDisplayType.Oscillator,
                ReferenceLevels: new[]
                {
                    new LevelDescriptor(
                        Name: "Break-Even", Value: 1.0,
                        ColorHex: "#888888", Dash: DashStyle.Dash),
                }),

            "SOPR" or "ASOPR" or "STH_SOPR" or "LTH_SOPR" => new SymbolRenderHints(
                RangeMin:    0,
                RangeMax:    null,
                DisplayType: ComponentDisplayType.Oscillator,
                ReferenceLevels: new[]
                {
                    new LevelDescriptor(
                        Name: "Break-Even", Value: 1.0,
                        ColorHex: "#888888", Dash: DashStyle.Dash),
                }),

            "NUPL" or "NUPL_LTH" or "NUPL_STH" => new SymbolRenderHints(
                RangeMin:    -1,
                RangeMax:    1,
                DisplayType: ComponentDisplayType.Oscillator,
                ReferenceLevels: new[]
                {
                    new LevelDescriptor(
                        Name: "Neutral", Value: 0,
                        ColorHex: "#888888", Dash: DashStyle.Dash),
                }),

            "FEAR_GREED" => new SymbolRenderHints(
                RangeMin:      0,
                RangeMax:      100,
                DisplayType:   ComponentDisplayType.Oscillator,
                SpeechTemplate:"{name}. {value:F0}.",
                ColorHex:      "#FFD54F",
                ReferenceLevels: new[]
                {
                    new LevelDescriptor(
                        Name: "Oversold (Extreme Fear)", Value: 25,
                        ColorHex: "#26A69A", Dash: DashStyle.Dash,
                        PlayEarcon: true, EarconVolume: 0.6f,
                        ZoneNoiseAmount: 0.25f, ZoneNoiseType: "pink"),
                    new LevelDescriptor(
                        Name: "Neutral", Value: 50,
                        ColorHex: "#888888", Dash: DashStyle.Dot),
                    new LevelDescriptor(
                        Name: "Overbought (Extreme Greed)", Value: 75,
                        ColorHex: "#EF5350", Dash: DashStyle.Dash,
                        PlayEarcon: true, EarconVolume: 0.6f,
                        ZoneNoiseAmount: 0.25f, ZoneNoiseType: "pink"),
                }),

            "FUNDING_RATE" => new SymbolRenderHints(
                RangeMin:    null,
                RangeMax:    null,
                DisplayType: ComponentDisplayType.Oscillator,
                ReferenceLevels: new[]
                {
                    new LevelDescriptor(
                        Name: "Neutral", Value: 0,
                        ColorHex: "#888888", Dash: DashStyle.Dash),
                }),

            _ => null
        };

        // ── Timeframes ──────────────────────────────────────────────────────────────

        public override List<string> NativelySupportedTimeframes => new()
        {
            StandardTimeframes.OneDay
        };

        // ── Configuration (no API key needed) ───────────────────────────────────────

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
            => Task.FromResult(MetricMap.Keys.OrderBy(k => k).ToList());

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(NativelySupportedTimeframes);

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        // ── OHLCV fetch ─────────────────────────────────────────────────────────────

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var symbol = request.Symbol ?? string.Empty;
            if (!MetricMap.TryGetValue(symbol, out var entry))
            {
                _errorStream.OnNext($"BGeometrics unknown metric symbol: {symbol}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var url = $"{BaseUrl}/{entry.Endpoint}";
                    var separator = '?';

                    if (request.Since.HasValue)
                    {
                        var sinceDate = DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime;
                        url += $"{separator}startday={sinceDate:yyyy-MM-dd}";
                        separator = '&';
                    }
                    if (request.Until.HasValue)
                    {
                        var untilDate = DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime;
                        url += $"{separator}endday={untilDate:yyyy-MM-dd}";
                    }

                    var json = await _http.GetStringAsync(url).ConfigureAwait(false);
                    var arr = JArray.Parse(json);

                    var bars = new List<Ohlcv>(arr.Count);
                    foreach (var pt in arr)
                    {
                        // Date field: "d" contains "YYYY-MM-DD"
                        var dateStr = pt["d"]?.Value<string>();
                        if (string.IsNullOrEmpty(dateStr)) continue;

                        if (!DateTime.TryParseExact(dateStr, "yyyy-MM-dd",
                                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                            continue;

                        date = DateTime.SpecifyKind(date, DateTimeKind.Utc);

                        // Value field: name varies per metric. Try the mapped field first,
                        // then fall back to "value" as a safety net.
                        var valToken = pt[entry.ValueField] ?? pt["value"];
                        if (valToken == null) continue;

                        // The API sometimes returns values as strings; parse robustly.
                        double val;
                        if (valToken.Type == JTokenType.String)
                        {
                            if (!double.TryParse(valToken.Value<string>(),
                                    NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                                continue;
                        }
                        else
                        {
                            val = valToken.Value<double>();
                        }

                        if (double.IsNaN(val) || double.IsInfinity(val)) continue;

                        bars.Add(new Ohlcv(date, val, val, val, val, 0));
                    }

                    var vols = bars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                    return (bars, vols);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"BGeometrics fetch error ({symbol}): {ex.Message}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        // ── Disposal ────────────────────────────────────────────────────────────────

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _http?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
