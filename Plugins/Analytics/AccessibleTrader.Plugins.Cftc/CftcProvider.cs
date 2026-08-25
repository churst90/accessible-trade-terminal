using System.Globalization;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Cftc
{
    /// <summary>
    /// CFTC Commitment of Traders (COT) — weekly speculator positioning per futures
    /// contract, served as net position as a percent of total open interest.
    ///
    /// ── Why this matters for strategies ─────────────────────────────────────────
    /// COT is the only free, official window into what large funds actually HOLD
    /// (as opposed to price-derived guesses about "smart money"). The CFTC requires
    /// every trader above a reporting threshold to disclose positions; this provider
    /// serves the managed-money (commodities) / leveraged-funds (financials) cohort —
    /// the hedge-fund cohort. The literature-supported use is CONTRARIAN AT EXTREMES:
    /// when large specs are at a multi-year net-long extreme there is nobody left to
    /// buy, and vice versa. Mid-range readings carry little information. Combine with
    /// a z-score or percentile-rank indicator and gate cycle/oscillator entries on
    /// positioning NOT being at the opposing extreme.
    ///
    /// ── Data source ─────────────────────────────────────────────────────────────
    /// CFTC Socrata open-data API (publicreporting.cftc.gov) — free, no API key.
    ///   • Financials (BTC, ETH, indices, FX): "Traders in Financial Futures",
    ///     dataset gpe5-46if, cohort fields lev_money_positions_long/short.
    ///   • Commodities (gold, oil, …): "Disaggregated" dataset 72hh-3qpy, cohort
    ///     fields m_money_positions_long_all/short_all.
    /// Futures-only variants (not futures+options) — the convention COT analysis uses.
    /// History runs from 2006-06 (commodities) / 2006 (TFF); BTC from 2017-12.
    ///
    /// ── Look-ahead honesty ──────────────────────────────────────────────────────
    /// Positions are as-of Tuesday but only PUBLISHED Friday ~15:30 ET. Bars are
    /// therefore stamped at the release date (report Tuesday + 3 days), so a daily
    /// backtest can only see a value from the Friday it became public knowledge.
    /// The StrategyLab's CftcCotProvider stamps at the report date instead — when
    /// comparing results, expect this provider to lag it by three calendar days.
    /// </summary>
    public class CftcProvider : BaseMarketDataProvider
    {
        private const string BaseUrl = "https://publicreporting.cftc.gov/resource";
        private const string TffDataset = "gpe5-46if";     // Traders in Financial Futures, futures-only
        private const string DisaggDataset = "72hh-3qpy";  // Disaggregated commodities, futures-only

        // Positions are as-of Tuesday, published Friday — see class docs.
        private const int ReleaseLagDays = 3;

        private sealed record Contract(string Code, bool IsTff, string DisplayName);

        // Symbol → CFTC contract. Codes verified against the live Socrata datasets
        // (report week 2026-07-07). Add new contracts here and they appear in the
        // symbol list automatically.
        private static readonly Dictionary<string, Contract> Contracts = new(StringComparer.OrdinalIgnoreCase)
        {
            // Financials — TFF dataset, "leveraged funds" cohort
            ["BITCOIN_COT"]    = new("133741", true,  "Bitcoin CME — Leveraged Funds Net % of OI"),
            ["ETHER_COT"]      = new("146021", true,  "Ether CME — Leveraged Funds Net % of OI"),
            ["SP500_COT"]      = new("13874A", true,  "E-mini S&P 500 — Leveraged Funds Net % of OI"),
            ["NASDAQ_COT"]     = new("209742", true,  "Nasdaq-100 Mini — Leveraged Funds Net % of OI"),
            ["EURO_FX_COT"]    = new("099741", true,  "Euro FX — Leveraged Funds Net % of OI"),
            ["USD_INDEX_COT"]  = new("098662", true,  "US Dollar Index — Leveraged Funds Net % of OI"),
            // Commodities — Disaggregated dataset, "managed money" cohort
            ["GOLD_COT"]       = new("088691", false, "Gold COMEX — Managed Money Net % of OI"),
            ["SILVER_COT"]     = new("084691", false, "Silver COMEX — Managed Money Net % of OI"),
            ["COPPER_COT"]     = new("085692", false, "Copper COMEX — Managed Money Net % of OI"),
            ["WTI_CRUDE_COT"]  = new("067411", false, "WTI Crude Oil — Managed Money Net % of OI"),
            ["NATGAS_COT"]     = new("023651", false, "Natural Gas NYMEX — Managed Money Net % of OI"),
        };

        // Host-provided HttpClient with an outbound-host allow-list; see
        // IPluginHttpClientFactory for the enforcement contract.
        private readonly HttpClient _http = PluginHostServices.CreateHttpClient(
            providerId: "CFTC",
            allowedHosts: new[] { "publicreporting.cftc.gov" });

        // Socrata is generous to anonymous callers, but the data is weekly — there is
        // never a reason to hammer it.
        private readonly RateLimiter _rateLimiter = new(30, TimeSpan.FromMinutes(1));

        public override string Name        => "CFTC";
        public override string Description => "CFTC Commitment of Traders — weekly fund positioning as % of open interest (free, no key).";
        public override List<MarketType> SupportedMarkets => new() { MarketType.Derivatives };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => false;
        public override bool IsConfigured         => true;
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 5000;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        public override string GetSymbolDisplayName(string symbol) =>
            Contracts.TryGetValue(symbol, out var c) ? c.DisplayName : symbol;

        // Net % of OI is an unbounded oscillator around zero; typical values run
        // roughly −30…+60 depending on the contract, so no hard range is declared —
        // only the zero line, which separates net-long from net-short cohorts.
        public override SymbolRenderHints? GetSymbolRenderHints(string symbol)
        {
            if (!Contracts.ContainsKey(symbol)) return null;
            return new SymbolRenderHints(
                DisplayType:   ComponentDisplayType.Oscillator,
                SpeechTemplate:"{name}. {value:F1} percent of open interest.",
                ColorHex:      "#BB86FC",
                ReferenceLevels: new[]
                {
                    new LevelDescriptor(
                        Name: "Neutral (flat positioning)", Value: 0,
                        ColorHex: "#888888", Dash: DashStyle.Dot),
                });
        }

        public override List<string> NativelySupportedTimeframes => new()
        {
            // The report is weekly; on daily charts the cross-series cache
            // forward-fills each Friday value until the next release.
            StandardTimeframes.OneDay,
            StandardTimeframes.OneWeek
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
            => Task.FromResult(Contracts.Keys.OrderBy(k => k).ToList());

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(NativelySupportedTimeframes);

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!Contracts.TryGetValue(request.Symbol ?? string.Empty, out var contract))
            {
                _errorStream.OnNext($"CFTC: no positioning dataset for '{request.Symbol}'.");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }

            string dataset = contract.IsTff ? TffDataset : DisaggDataset;
            string longField  = contract.IsTff ? "lev_money_positions_long"  : "m_money_positions_long_all";
            string shortField = contract.IsTff ? "lev_money_positions_short" : "m_money_positions_short_all";

            // Socrata SoQL: one request returns the full weekly history (~1,050 rows
            // for the oldest contracts), already sorted oldest → newest.
            string url =
                $"{BaseUrl}/{dataset}.json" +
                $"?$select=report_date_as_yyyy_mm_dd,open_interest_all,{longField},{shortField}" +
                $"&$where=cftc_contract_market_code='{contract.Code}'" +
                $"&$order=report_date_as_yyyy_mm_dd&$limit=5000";

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var json = await _http.GetStringAsync(url);
                    if (JToken.Parse(json) is not JArray rows)
                        return (new List<Ohlcv>(), new List<(long, double)>());

                    var bars = new List<Ohlcv>(rows.Count);
                    foreach (var row in rows)
                    {
                        // Bare "yyyy-MM-dd" — AssumeUniversal so it isn't read as
                        // Local then shifted (a ±1-day drift on non-UTC boxes).
                        if (!DateTime.TryParse(row["report_date_as_yyyy_mm_dd"]?.ToString(),
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var reportDate))
                            continue;
                        if (!TryNum(row[longField], out double longPos)) continue;
                        if (!TryNum(row[shortField], out double shortPos)) continue;
                        if (!TryNum(row["open_interest_all"], out double oi) || oi < 1) continue;

                        double netPctOi = (longPos - shortPos) / oi * 100.0;
                        var releaseDate = DateTime.SpecifyKind(reportDate.Date, DateTimeKind.Utc)
                                                  .AddDays(ReleaseLagDays);
                        bars.Add(new Ohlcv(releaseDate, netPctOi, netPctOi, netPctOi, netPctOi, 0));
                    }

                    if (request.Since.HasValue)
                    {
                        var sinceDt = DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime;
                        bars = bars.Where(b => b.Date >= sinceDt).ToList();
                    }
                    if (request.Until.HasValue)
                    {
                        var untilDt = DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime;
                        bars = bars.Where(b => b.Date <= untilDt).ToList();
                    }
                    if (request.Limit > 0 && bars.Count > request.Limit)
                        bars = bars.Skip(bars.Count - request.Limit).ToList();

                    var vols = bars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                    return (bars, vols);
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"CFTC fetch error: {ex.Message}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        private static bool TryNum(JToken? token, out double value) =>
            double.TryParse(token?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
