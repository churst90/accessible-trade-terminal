using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.WikipediaPageviews
{
    /// <summary>
    /// Wikipedia article pageviews — a public attention series, one value per day per entity.
    ///
    /// <para>
    /// ── Why this one is worth having ────────────────────────────────────────────
    /// Almost every input this project has measured is a transform of price and volume. The three
    /// genuinely non-price classes tested so far — on-chain valuation, COT positioning, funding and
    /// open interest — all came back null. This is a fourth, and it has a property none of the
    /// others do and that no text-sentiment feature can ever have:
    /// </para>
    ///
    /// <para>
    /// <b>It is point-in-time and cannot be contaminated retroactively.</b> The count of people who
    /// opened the Bitcoin article on 3 March 2021 was fixed on 3 March 2021. Compare that with
    /// scoring a 2019 news article for sentiment today, where the scorer already knows what happened
    /// next — invisible lookahead, with no offset to shift and no confirmation lag to add. That
    /// distinction is why this provider is the one built first.
    /// </para>
    ///
    /// <para>
    /// ── The reason to build it BEFORE deciding whether to use it ────────────────
    /// The API serves history back to 2015-07-01 for any article, so unlike a recorder there is no
    /// "start collecting now or lose it" urgency for the daily series itself. What accumulates only
    /// forward is the pairing of attention against the terminal's own universe over time. Either
    /// way, the cost of building it is one REST call.
    /// </para>
    ///
    /// <para>
    /// ── How it should be USED, given everything already measured ────────────────
    /// As a rate of change against the entity's own trailing baseline, never as a level. "Ranks and
    /// ratios, not levels" is settled for price indicators and applies with more force here: raw
    /// views differ by three orders of magnitude between Bitcoin and Walmart, so any cross-entity
    /// comparison of the level is measuring article popularity, not attention. The provider ships
    /// the level because a provider's job is to report what the source says; shaping it is the
    /// indicator's job.
    /// </para>
    ///
    /// <para>
    /// And it must be checked for the collapse that killed Fear &amp; Greed as an input: a sentiment
    /// series derived from price is not orthogonal to price. Attention is plausibly driven BY large
    /// price moves, so before believing any result, correlate it against trailing return — the check
    /// that exposed the crowding index's claimed orthogonality as +0.19.
    /// </para>
    ///
    /// <para>
    /// ── Endpoint ────────────────────────────────────────────────────────────────
    /// <c>GET wikimedia.org/api/rest_v1/metrics/pageviews/per-article/en.wikipedia/all-access/user/{article}/{granularity}/{start}/{end}</c>
    /// </para>
    /// <list type="bullet">
    ///   <item>No API key, no signup, no quota published. Wikimedia asks for a descriptive
    ///         User-Agent and will throttle anonymous clients that do not send one.</item>
    ///   <item><b>agent=user</b>, not all-agents. Bot and spider traffic is not attention, and on
    ///         low-traffic articles it is most of the count.</item>
    ///   <item><b>Daily and monthly only.</b> Per-article hourly returns HTTP 400 — the hourly
    ///         endpoint exists but is project-wide aggregate, not per article. (Verified
    ///         2026-08-02; the design note in COMPANY_DATA_LAYER.md said hourly and was wrong.)</item>
    ///   <item><b>History starts 2015-07-01.</b> Requests reaching earlier return 404 for the whole
    ///         range rather than a clipped series, so the request window is clamped rather than
    ///         passed through.</item>
    /// </list>
    ///
    /// <para>
    /// ── Symbols ─────────────────────────────────────────────────────────────────
    /// Curated tickers of the form <c>BTC_VIEWS</c> mapped to article titles, so workspaces store a
    /// clean identifier rather than <c>Tesla,_Inc.</c> with its comma. Every mapped title was
    /// verified to return 200 on 2026-08-02 — a catalogue entry that 404s is exactly the silent
    /// read-path failure the provider audit called the dominant defect class here. Any symbol NOT in
    /// the table is passed through as a raw article title, so a hand-edited workspace can point at
    /// any article on Wikipedia without a code change.
    /// </para>
    /// </summary>
    public class WikipediaPageviewsProvider : BaseMarketDataProvider
    {
        private readonly HttpClient _http = PluginHostServices.CreateHttpClient(
            providerId: "WikipediaPageviews",
            allowedHosts: new[] { "wikimedia.org" },
            // Wikimedia's REST policy asks anonymous clients to identify themselves and throttles
            // those that do not. A generic agent string is the difference between a working provider
            // and intermittent 429s that would surface as an empty chart.
            userAgent: "AccessibleTrader/2.2 (accessible-trade-terminal; contact via repository)");

        private readonly RateLimiter _rateLimiter = new(100, TimeSpan.FromMinutes(1));

        private const string Base = "https://wikimedia.org/api/rest_v1/metrics/pageviews/per-article/en.wikipedia/all-access/user";

        /// <summary>Pageviews data does not exist before this date; earlier requests 404 the whole range.</summary>
        internal static readonly DateTime FirstAvailable = new(2015, 7, 1);

        public override string Name => "Wikipedia";
        public override string Description => "Wikipedia article pageviews — daily public attention per entity, history to 2015.";
        public override List<MarketType> SupportedMarkets => new() { MarketType.Sentiment };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey => false;
        public override bool IsConfigured => true;
        public override bool SupportsLiveUpdates => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest => 5000;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        public override List<string> NativelySupportedTimeframes => new()
        {
            // Daily and monthly are the only per-article granularities the upstream serves. Hourly
            // is a 400 on this endpoint, so declaring it would produce an empty chart with no error.
            StandardTimeframes.OneDay,
            StandardTimeframes.OneMonth,
        };

        // ── Catalogue ───────────────────────────────────────────────────────────

        /// <summary>
        /// Symbol → Wikipedia article title. Every title here returned HTTP 200 from the pageviews
        /// endpoint on 2026-08-02. Titles that Wikipedia serves as an article but the pageviews API
        /// does not (Binance, Stock market, Tether) were verified and deliberately left out rather
        /// than shipped as dead dropdown entries.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, (string Article, string Display, string SubType)> Catalogue =
            new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                // Crypto
                ["BTC_VIEWS"] = ("Bitcoin", "Bitcoin attention", "Crypto"),
                ["ETH_VIEWS"] = ("Ethereum", "Ethereum attention", "Crypto"),
                ["SOL_VIEWS"] = ("Solana_(blockchain_platform)", "Solana attention", "Crypto"),
                ["XRP_VIEWS"] = ("Ripple_Labs", "Ripple / XRP attention", "Crypto"),
                ["DOGE_VIEWS"] = ("Dogecoin", "Dogecoin attention", "Crypto"),
                ["ADA_VIEWS"] = ("Cardano_(blockchain_platform)", "Cardano attention", "Crypto"),
                ["LTC_VIEWS"] = ("Litecoin", "Litecoin attention", "Crypto"),
                ["DOT_VIEWS"] = ("Polkadot_(cryptocurrency)", "Polkadot attention", "Crypto"),
                ["AVAX_VIEWS"] = ("Avalanche_(blockchain_platform)", "Avalanche attention", "Crypto"),
                ["BCH_VIEWS"] = ("Bitcoin_Cash", "Bitcoin Cash attention", "Crypto"),
                ["XMR_VIEWS"] = ("Monero", "Monero attention", "Crypto"),
                ["COIN_VIEWS"] = ("Coinbase", "Coinbase attention", "Crypto"),
                ["CRYPTO_VIEWS"] = ("Cryptocurrency", "Cryptocurrency attention", "Crypto"),

                // Equities
                ["AAPL_VIEWS"] = ("Apple_Inc.", "Apple attention", "Equities"),
                ["MSFT_VIEWS"] = ("Microsoft", "Microsoft attention", "Equities"),
                ["TSLA_VIEWS"] = ("Tesla,_Inc.", "Tesla attention", "Equities"),
                ["NVDA_VIEWS"] = ("Nvidia", "Nvidia attention", "Equities"),
                ["AMZN_VIEWS"] = ("Amazon_(company)", "Amazon attention", "Equities"),
                ["GOOGL_VIEWS"] = ("Alphabet_Inc.", "Alphabet attention", "Equities"),
                ["META_VIEWS"] = ("Meta_Platforms", "Meta attention", "Equities"),
                ["JPM_VIEWS"] = ("JPMorgan_Chase", "JPMorgan Chase attention", "Equities"),
                ["XOM_VIEWS"] = ("ExxonMobil", "ExxonMobil attention", "Equities"),
                ["WMT_VIEWS"] = ("Walmart", "Walmart attention", "Equities"),

                // Macro concepts — the search-interest proxies people actually reach for
                ["INFLATION_VIEWS"] = ("Inflation", "Inflation attention", "Macro"),
                ["RECESSION_VIEWS"] = ("Recession", "Recession attention", "Macro"),
                ["FED_VIEWS"] = ("Federal_Reserve", "Federal Reserve attention", "Macro"),
                ["GOLD_VIEWS"] = ("Gold", "Gold attention", "Macro"),
                ["RATES_VIEWS"] = ("Interest_rate", "Interest rate attention", "Macro"),
                ["MARKETTREND_VIEWS"] = ("Market_trend", "Bull/bear market attention", "Macro"),
                ["ETF_VIEWS"] = ("Exchange-traded_fund", "ETF attention", "Macro"),
                ["EXCHANGE_VIEWS"] = ("Stock_exchange", "Stock exchange attention", "Macro"),
                ["INDEX_VIEWS"] = ("Stock_market_index", "Stock index attention", "Macro"),
            };

        public override string GetSymbolDisplayName(string symbol)
            => Catalogue.TryGetValue(symbol, out var e) ? e.Display : symbol.Replace('_', ' ');

        /// <summary>
        /// Pageviews is an unbounded count, not an oscillator, so no range bounds or reference
        /// levels are declared — a fixed "high attention" line would be meaningless across entities
        /// that differ by three orders of magnitude. Speech reads the integer because a fraction of
        /// a pageview does not exist.
        /// </summary>
        public override SymbolRenderHints? GetSymbolRenderHints(string symbol) => new(
            DisplayType: ComponentDisplayType.Line,
            SpeechTemplate: "{name}. {value:F0} views.",
            ColorHex: "#64B5F6");

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
        {
            var wanted = Catalogue
                .Where(kv => string.IsNullOrEmpty(subType) || subType == "Spot"
                          || kv.Value.SubType.Equals(subType, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(wanted);
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Crypto", "Equities", "Macro" });

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(NativelySupportedTimeframes);

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
            => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        // ── Fetch ───────────────────────────────────────────────────────────────

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var empty = (new List<Ohlcv>(), new List<(long, double)>());
            try
            {
                string article = ResolveArticle(request.Symbol);
                if (string.IsNullOrWhiteSpace(article)) return empty;

                bool monthly = request.Timeframe == StandardTimeframes.OneMonth;
                string granularity = monthly ? "monthly" : "daily";

                var (from, to) = ResolveWindow(request, monthly);
                if (from > to) return empty;

                string url = $"{Base}/{Uri.EscapeDataString(article)}/{granularity}/{from.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}/{to.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";

                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    using var resp = await _http.GetAsync(url);

                    // A 404 here means "no data for this article in this window", not a transport
                    // failure — an article created last year genuinely has nothing in 2016. Reporting
                    // it as an error would train users to ignore the error stream, so it returns an
                    // empty series quietly and the chart shows what it is: nothing.
                    if (resp.StatusCode == HttpStatusCode.NotFound) return empty;
                    resp.EnsureSuccessStatusCode();

                    var json = await resp.Content.ReadAsStringAsync();
                    return (Parse(json), Parse(json)
                        .Select(b => (new DateTimeOffset(b.Date, TimeSpan.Zero).ToUnixTimeMilliseconds(), b.Volume))
                        .ToList());
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Wikipedia pageviews fetch error: {ex.Message}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return empty;
            }
        }

        /// <summary>
        /// Views arrive newest-last already, but the order is not documented as guaranteed, so it is
        /// sorted rather than assumed — every downstream renderer and indicator requires chronological
        /// bars and an out-of-order series produces wrong numbers rather than an error.
        /// </summary>
        internal static List<Ohlcv> Parse(string json)
        {
            var bars = new List<Ohlcv>();
            var items = JObject.Parse(json)["items"] as JArray;
            if (items == null) return bars;

            foreach (var it in items)
            {
                string? ts = it["timestamp"]?.ToString();
                if (string.IsNullOrEmpty(ts) || ts.Length < 8) continue;
                if (!DateTime.TryParseExact(ts.Substring(0, 8), "yyyyMMdd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                if (!double.TryParse(it["views"]?.ToString(), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var views)) continue;

                bars.Add(new Ohlcv(date, views, views, views, views, 0));
            }

            return bars.OrderBy(b => b.Date).ToList();
        }

        /// <summary>Curated ticker, or a raw article title for anything not in the catalogue.</summary>
        internal static string ResolveArticle(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return "";
            return Catalogue.TryGetValue(symbol, out var e) ? e.Article : symbol.Trim().Replace(' ', '_');
        }

        /// <summary>
        /// The requested window, clamped to what the source actually has.
        ///
        /// <para>
        /// The clamp is not cosmetic: asking for a range that begins before 2015-07-01 returns 404
        /// for the ENTIRE request rather than a clipped series, so a chart asking for "5000 daily
        /// bars" would silently render empty instead of showing the eleven years that exist.
        /// </para>
        /// </summary>
        internal static (DateTime From, DateTime To) ResolveWindow(MarketDataRequest request, bool monthly)
        {
            var to = request.Until.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime.Date
                : DateTime.UtcNow.Date;

            DateTime from;
            if (request.Since.HasValue)
            {
                from = DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime.Date;
            }
            else
            {
                int units = request.Limit > 0 ? request.Limit : 1000;
                from = monthly ? to.AddMonths(-units) : to.AddDays(-units);
            }

            if (from < FirstAvailable) from = FirstAvailable;
            if (to < FirstAvailable) to = FirstAvailable;
            return (from, to);
        }
    }
}
