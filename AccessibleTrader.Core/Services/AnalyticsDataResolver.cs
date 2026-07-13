using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Indicators;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// A single provider source for a metric, ordered by priority in each metric's
    /// source list. Free providers (RequiresApiKey = false) are listed first so
    /// resolution always prefers zero-configuration sources.
    /// </summary>
    internal sealed record MetricSource(
        string Provider,
        string Market,
        string Symbol,
        string Timeframe,
        bool RequiresApiKey,
        int MaxPages = 1);

    public sealed class AnalyticsDataResolver : IAnalyticsDataResolver
    {
        private readonly IDataService _dataService;
        private readonly ILogger<AnalyticsDataResolver> _logger;

        // Canonical metric name → ordered list of sources (free first, then paid).
        // Each entry maps one logical metric to every provider that can serve it.
        private static readonly Dictionary<string, MetricEntry> Registry = BuildRegistry();

        public AnalyticsDataResolver(IDataService dataService, ILogger<AnalyticsDataResolver> logger)
        {
            _dataService = dataService;
            _logger = logger;
        }

        public CrossSeriesRequest? Resolve(string metric, string? asset = null)
        {
            if (string.IsNullOrWhiteSpace(metric)) return null;

            // Normalise: "FundingRate", "funding_rate", "FUNDING_RATE" all map to the same key.
            string key = NormalizeKey(metric);

            if (!Registry.TryGetValue(key, out var entry))
            {
                _logger.LogDebug("AnalyticsDataResolver: no registry entry for metric '{Metric}'.", metric);
                return null;
            }

            // Walk sources in priority order (free first).
            foreach (var src in entry.Sources)
            {
                if (src.RequiresApiKey && !_dataService.IsProviderConfigured(src.Provider))
                    continue;

                return new CrossSeriesRequest(
                    Market: src.Market,
                    Provider: src.Provider,
                    Symbol: src.Symbol,
                    Timeframe: src.Timeframe,
                    MaxPages: src.MaxPages);
            }

            _logger.LogDebug("AnalyticsDataResolver: no available provider for metric '{Metric}' (all require unconfigured API keys).", metric);
            return null;
        }

        public IReadOnlyList<AnalyticsMetricDescriptor> GetAvailableMetrics()
        {
            var result = new List<AnalyticsMetricDescriptor>(Registry.Count);
            foreach (var (key, entry) in Registry)
            {
                result.Add(new AnalyticsMetricDescriptor(
                    Metric: key,
                    DisplayName: entry.DisplayName,
                    Category: entry.Category,
                    SupportedAssets: entry.Assets,
                    AvailableProviders: entry.Sources.Select(s => s.Provider).ToArray()));
            }
            return result;
        }

        public bool IsMetricAvailable(string metric)
        {
            return Resolve(metric) != null;
        }

        // ── Registry construction ───────────────────────────────────────────────

        private static string NormalizeKey(string metric)
            => metric.Trim().Replace(" ", "_").Replace("-", "_").ToUpperInvariant();

        private sealed record MetricEntry(
            string DisplayName,
            string Category,
            string[] Assets,
            MetricSource[] Sources);

        private static Dictionary<string, MetricEntry> BuildRegistry()
        {
            var reg = new Dictionary<string, MetricEntry>(StringComparer.OrdinalIgnoreCase);

            // ── Derivatives ────────────────────────────────────────────────

            reg["FUNDING_RATE"] = new MetricEntry(
                "Funding Rate", "Derivatives", new[] { "BTC", "ETH" },
                new[]
                {
                    new MetricSource("OkxDerivatives",      "Derivatives", "BTC-USDT-SWAP_FUNDING",  "1h", false, MaxPages: 10),
                    new MetricSource("BinanceDerivatives",  "Derivatives", "BTCUSDT_FUNDING",        "1h", false),
                    new MetricSource("BGeometrics",         "OnChain",     "FUNDING_RATE",           "1d", false),
                });

            reg["OPEN_INTEREST"] = new MetricEntry(
                "Open Interest", "Derivatives", new[] { "BTC", "ETH" },
                new[]
                {
                    new MetricSource("OkxDerivatives",      "Derivatives", "BTC-USDT-SWAP_OI",       "1h", false),
                    new MetricSource("BinanceDerivatives",  "Derivatives", "BTCUSDT_OI",             "1h", false),
                    new MetricSource("BGeometrics",         "OnChain",     "OI_FUTURES",             "1d", false),
                });

            // ── Positioning (CFTC Commitment of Traders) ───────────────────
            // Weekly net fund positioning as % of open interest, from the free
            // CFTC Socrata API. One metric per contract: resolution is by metric
            // name only (the asset parameter is unused — see Resolve()), so
            // multi-contract data gets one registry key per contract.

            void CotMetric(string key, string display, string asset, string symbol) =>
                reg[key] = new MetricEntry(
                    display, "Positioning", new[] { asset },
                    new[] { new MetricSource("CFTC", "Derivatives", symbol, "1w", false) });

            CotMetric("COT_BITCOIN",   "COT Net Positioning — Bitcoin CME",    "BTC",    "BITCOIN_COT");
            CotMetric("COT_ETHER",     "COT Net Positioning — Ether CME",      "ETH",    "ETHER_COT");
            CotMetric("COT_GOLD",      "COT Net Positioning — Gold",           "GOLD",   "GOLD_COT");
            CotMetric("COT_SILVER",    "COT Net Positioning — Silver",         "SILVER", "SILVER_COT");
            CotMetric("COT_COPPER",    "COT Net Positioning — Copper",         "COPPER", "COPPER_COT");
            CotMetric("COT_WTI_CRUDE", "COT Net Positioning — WTI Crude Oil",  "OIL",    "WTI_CRUDE_COT");
            CotMetric("COT_NATGAS",    "COT Net Positioning — Natural Gas",    "NATGAS", "NATGAS_COT");
            CotMetric("COT_SP500",     "COT Net Positioning — E-mini S&P 500", "SPX",    "SP500_COT");
            CotMetric("COT_NASDAQ",    "COT Net Positioning — Nasdaq-100",     "NDX",    "NASDAQ_COT");
            CotMetric("COT_EURO_FX",   "COT Net Positioning — Euro FX",        "EUR",    "EURO_FX_COT");
            CotMetric("COT_USD_INDEX", "COT Net Positioning — US Dollar Index","DXY",    "USD_INDEX_COT");

            // FINRA daily short-volume ratio — per-symbol; callers append the
            // ticker to build the symbol ("{TICKER}_SHORTVOL"), so only liquid
            // reference names are registered as browsable metrics here.
            reg["SHORT_VOLUME_SPY"] = new MetricEntry(
                "Daily Short Volume % — SPY", "Positioning", new[] { "SPX" },
                new[] { new MetricSource("FINRA", "Derivatives", "SPY_SHORTVOL", "1d", false) });
            reg["SHORT_VOLUME_QQQ"] = new MetricEntry(
                "Daily Short Volume % — QQQ", "Positioning", new[] { "NDX" },
                new[] { new MetricSource("FINRA", "Derivatives", "QQQ_SHORTVOL", "1d", false) });

            // ── Sentiment ──────────────────────────────────────────────────

            reg["FEAR_GREED"] = new MetricEntry(
                "Fear & Greed Index", "Sentiment", new[] { "BTC" },
                new[]
                {
                    new MetricSource("AlternativeMe",  "Sentiment", "FNG_INDEX",   "1d", false),
                    new MetricSource("BGeometrics",    "OnChain",   "FEAR_GREED",  "1d", false),
                });

            // ── On-Chain (BTC) ─────────────────────────────────────────────

            reg["MVRV"] = new MetricEntry(
                "Market Value to Realized Value", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("BGeometrics",  "OnChain", "MVRV",           "1d", false),
                    new MetricSource("CoinMetrics",  "OnChain", "btc_CapMVRVCur", "1d", false),
                    new MetricSource("Glassnode",    "OnChain", "MVRV",           "1d", true),
                });

            reg["SOPR"] = new MetricEntry(
                "Spent Output Profit Ratio", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("BGeometrics",  "OnChain", "SOPR",  "1d", false),
                    new MetricSource("Glassnode",    "OnChain", "SOPR",  "1d", true),
                });

            reg["NUPL"] = new MetricEntry(
                "Net Unrealized Profit/Loss", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("BGeometrics",  "OnChain", "NUPL",  "1d", false),
                    new MetricSource("Glassnode",    "OnChain", "NUPL",  "1d", true),
                });

            reg["NVT"] = new MetricEntry(
                "Network Value to Transactions", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("BGeometrics",  "OnChain", "NVT",        "1d", false),
                    new MetricSource("CoinMetrics",  "OnChain", "btc_NVTAdj", "1d", false),
                });

            reg["REALIZED_PRICE"] = new MetricEntry(
                "Realized Price", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("BGeometrics",  "OnChain", "REALIZED_PRICE",  "1d", false),
                });

            reg["S2F"] = new MetricEntry(
                "Stock-to-Flow", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("BGeometrics",  "OnChain", "S2F",  "1d", false),
                });

            reg["PUELL_MULTIPLE"] = new MetricEntry(
                "Puell Multiple", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("BGeometrics",  "OnChain", "PUELL_MULTIPLE",  "1d", false),
                });

            reg["RESERVE_RISK"] = new MetricEntry(
                "Reserve Risk", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("BGeometrics",  "OnChain", "RESERVE_RISK",  "1d", false),
                });

            reg["CDD"] = new MetricEntry(
                "Coin Days Destroyed", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("BGeometrics",  "OnChain", "CDD",  "1d", false),
                });

            reg["ACTIVE_ADDRESSES"] = new MetricEntry(
                "Active Addresses", "OnChain", new[] { "BTC", "ETH" },
                new[]
                {
                    new MetricSource("BGeometrics",  "OnChain", "ACTIVE_ADDRESSES",      "1d", false),
                    new MetricSource("CoinMetrics",  "OnChain", "btc_AdrActCnt",         "1d", false),
                    new MetricSource("Glassnode",    "OnChain", "ACTIVE_ADDRESSES",      "1d", true),
                });

            reg["MAYER_MULTIPLE"] = new MetricEntry(
                "Mayer Multiple", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("BGeometrics",  "OnChain", "MAYER_MULTIPLE",  "1d", false),
                });

            reg["PI_CYCLE"] = new MetricEntry(
                "Pi Cycle Top Indicator", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("BGeometrics",  "OnChain", "PI_CYCLE",  "1d", false),
                });

            reg["HODL_WAVES"] = new MetricEntry(
                "HODL Waves (1-2 Year Band)", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("BGeometrics",  "OnChain", "HODL_WAVES",  "1d", false),
                });

            // ── On-Chain (multi-asset via CoinMetrics) ─────────────────────

            reg["HASH_RATE"] = new MetricEntry(
                "Hash Rate", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("Mempool",      "OnChain", "HASHRATE",          "1d", false),
                    new MetricSource("CoinMetrics",  "OnChain", "btc_HashRate",      "1d", false),
                });

            reg["DIFFICULTY"] = new MetricEntry(
                "Mining Difficulty", "OnChain", new[] { "BTC" },
                new[]
                {
                    new MetricSource("Mempool",      "OnChain", "DIFFICULTY",           "1d", false),
                    new MetricSource("CoinMetrics",  "OnChain", "btc_DiffMean",        "1d", false),
                });

            reg["TX_COUNT"] = new MetricEntry(
                "Transaction Count", "OnChain", new[] { "BTC", "ETH" },
                new[]
                {
                    new MetricSource("CoinMetrics",  "OnChain", "btc_TxCnt",  "1d", false),
                });

            reg["FEES_TOTAL"] = new MetricEntry(
                "Total Fees", "OnChain", new[] { "BTC", "ETH" },
                new[]
                {
                    new MetricSource("CoinMetrics",  "OnChain", "btc_FeeTotNtv",  "1d", false),
                    new MetricSource("Mempool",      "OnChain", "FEES",            "1d", false),
                });

            // ── DeFi ───────────────────────────────────────────────────────

            reg["TVL"] = new MetricEntry(
                "Total Value Locked", "OnChain", new[] { "ETH", "ALL" },
                new[]
                {
                    new MetricSource("DefiLlama",  "OnChain", "TVL_TOTAL",  "1d", false),
                });

            reg["STABLECOIN_SUPPLY"] = new MetricEntry(
                "Stablecoin Supply", "OnChain", new[] { "ALL" },
                new[]
                {
                    new MetricSource("DefiLlama",  "OnChain", "STABLES_TOTAL",  "1d", false),
                });

            // ── ETH-specific ───────────────────────────────────────────────

            reg["GAS_PRICE"] = new MetricEntry(
                "ETH Gas Price", "OnChain", new[] { "ETH" },
                new[]
                {
                    new MetricSource("Etherscan",  "OnChain", "GAS_AVG",  "1d", true),
                });

            reg["ETH_SUPPLY"] = new MetricEntry(
                "ETH Total Supply", "OnChain", new[] { "ETH" },
                new[]
                {
                    new MetricSource("Etherscan",  "OnChain", "ETH_SUPPLY",  "1d", true),
                });

            // ── Economic ───────────────────────────────────────────────────

            reg["UNEMPLOYMENT"] = new MetricEntry(
                "Unemployment Rate", "Economic", new[] { "USD" },
                new[]
                {
                    new MetricSource("Fred",  "Economic", "UNRATE",  "1d", true),
                });

            reg["CPI"] = new MetricEntry(
                "Consumer Price Index", "Economic", new[] { "USD" },
                new[]
                {
                    new MetricSource("Fred",  "Economic", "CPIAUCSL",  "1d", true),
                });

            reg["GDP"] = new MetricEntry(
                "Gross Domestic Product", "Economic", new[] { "USD" },
                new[]
                {
                    new MetricSource("Fred",  "Economic", "GDP",  "1d", true),
                });

            reg["FED_FUNDS"] = new MetricEntry(
                "Federal Funds Rate", "Economic", new[] { "USD" },
                new[]
                {
                    new MetricSource("Fred",  "Economic", "FEDFUNDS",  "1d", true),
                });

            reg["DXY"] = new MetricEntry(
                "US Dollar Index", "Economic", new[] { "USD" },
                new[]
                {
                    new MetricSource("Fred",  "Economic", "DEXUSEU",  "1d", true),
                });

            reg["TREASURY_10Y"] = new MetricEntry(
                "10-Year Treasury Yield", "Economic", new[] { "USD" },
                new[]
                {
                    new MetricSource("Fred",  "Economic", "DGS10",  "1d", true),
                });

            return reg;
        }
    }
}
