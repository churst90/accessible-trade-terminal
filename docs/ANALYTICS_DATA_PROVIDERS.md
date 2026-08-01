# Analytics Data Providers

This document describes the analytics-tier data providers shipped with the application
and the standardised pattern for indicator authors to consume their data.

## TL;DR for indicator authors

Analytics data series flow through the **same `IDataOrchestrator.FetchOhlcvAsync`
path** as price data. Every analytics provider returns its values as `Ohlcv` records
where `O = H = L = C = value` and `Volume = 0`. From an indicator's perspective there
is no difference — you get a `ReadOnlySpan<Ohlcv>` and you read `.Close` to get the
metric value.

The mental model is:

> *"Analytics series are 'instruments' you load on a chart, just like BTC. Once
> loaded, they're available to indicators and to the strategy condition tree."*

## Currently shipped analytics providers

All twelve live under `Plugins/Analytics/` and are surfaced under the **Analytics**
terminal mode (the radio button where FRED lives). The market dropdown reflects the
data category; the provider dropdown picks the source. One section below
(*Equities Fundamentals — FMP Analytics*) is a second `IMarketDataProvider` exposed
from the combined `Plugins/Providers/AccessibleTrader.Plugins.Fmp` project, not a
separate plugin DLL.

All 12 analytics providers cap their outbound `HttpClient` at
`MaxResponseContentBufferSize = 32 MB` with a 60s timeout (security pass #2,
2026-04-16). BinanceVision additionally bounds per-archive decompressed size to
256 MB via a `BoundedReadStream` to defuse zip bombs.

### Economic — *FRED* (existing)
- **Auth:** free API key from stlouisfed.org
- **Coverage:** ~30 popular series + searchable catalog (GDP, CPI, Fed Funds, DXY, etc.)
- **Resolution:** daily / weekly / monthly / quarterly / annual
- **Strategy use:** macro regime gates ("don't long crypto when DXY trending up")

### Derivatives — *BinanceDerivatives* (NEW, no key)
- **Auth:** none — public REST endpoints
- **Symbols:**
  - `BTCUSDT_FUNDING`, `ETHUSDT_FUNDING`, `SOLUSDT_FUNDING`, `BNBUSDT_FUNDING`, `XRPUSDT_FUNDING`
  - `BTCUSDT_OI`, `ETHUSDT_OI`, `SOLUSDT_OI`, `BNBUSDT_OI`, `XRPUSDT_OI`
- **Resolution:** funding rates settle every 8h; open interest 5m / 15m / 30m / 1h / 4h / 1d
- **Units:** funding is **percent per 8h** (so `0.01` = `0.01%`); OI is **USD value**
- **Rate limit:** 1200 req/min (capped well below Binance's 2400 ceiling)
- **Why this matters:** funding tells you who's paying to hold positions. Extreme
  positive funding + Cipher oversold = longs are bleeding, the unwind is the buy.

### Derivatives — *BinanceVision* (no key, multi-year history)
- **Auth:** none — pulls ZIP archives from the public `data.binance.vision` S3 bucket.
- **Symbols:** `{BTC,ETH,XRP,SOL,DOGE,ADA,LTC,BNB}USDT_FUNDING` / `_OI` — both funding
  rate (monthly archives) and open interest (daily archives).
- **History:** ~6 years of funding back to contract launch (BTC: 2019-09, ETH: 2019-11,
  SOL: 2020-09, etc). OI daily archives from 2020-11. The primary source for Core's
  `FundingRateProvider` / `OpenInterestProvider` / `CrowdingIndexProvider` — the live
  `BinanceDerivatives` API only ships the last few weeks of OI, which is useless for
  backtesting.
- **Resolution:** 8h funding settlement; daily OI snapshots (last-row-of-day reading
  from the 5-minute metrics file).
- **Units:** funding is **percent per 8h** (multiplied ×100 at the fetch boundary to
  match `BinanceDerivatives`); OI is **USD value** (`sum_open_interest_value`).
- **Caches:** per-symbol in-memory cache; archives are immutable so no expiry. Response
  + decompression sizes are hard-capped (64 MB compressed, 256 MB expanded) with a
  `BoundedReadStream` zip-bomb guard.

### Derivatives — *OkxDerivatives* (no key)
- **Auth:** none — OKX public derivatives REST endpoints.
- **Symbols:** `{BTC,ETH,SOL,DOGE,LTC}-USDT-SWAP_FUNDING` / `_OI` plus a seed catalogue
  of popular perpetual swaps. Users can request any OKX perpetual by adding the suffix.
- **Resolution:** funding settlement cadence varies per contract (~8h); OI 5m–1d.
- **History:** live REST — shallow (typically last ~11 days for OI). For deep history
  prefer `BinanceVision`.
- **Rate limit:** 300 req/min (well under OKX's public ceiling).

### Derivatives — *Deribit* (NEW, no key)
- **Auth:** none — Deribit public v2 REST endpoints (`www.deribit.com/api/v2`).
- **Symbols:** `BTC_DVOL`, `ETH_DVOL` (the **Deribit Volatility Index** — crypto's
  "VIX", the options market's 30-day forward implied volatility, delivered as real
  OHLC) and `BTC_HISTVOL`, `ETH_HISTVOL` (**realised/historical** annualised
  volatility, a single value per timestamp rendered as a flat line).
- **Resolution:** DVOL at 1h / 12h / 1d; historical volatility is a rolling series.
- **Units:** annualised volatility in **percent** (e.g. `55.0` = 55% annualised).
- **History:** live REST, ~1000 bars per request.
- **Why this matters:** DVOL sitting well above realised vol means options are pricing
  fear — a mean-reversion tell; a DVOL spike alongside a price flush often marks
  capitulation. This is the terminal's first window onto the crypto **options** side.

### Sentiment — *AlternativeMe* (NEW, no key)
- **Auth:** none
- **Symbol:** `FNG_INDEX` (the only one — Crypto Fear & Greed)
- **Resolution:** daily
- **Units:** integer 0-100 (0 = extreme fear, 100 = extreme greed)
- **History:** back to 2018-02-01
- **Strategy use:** extreme readings are mean-reverting. Index < 20 + Cipher oversold
  = high-conviction long. Index > 80 + Cipher overbought = high-conviction short.

### OnChain — *Glassnode* (NEW, requires free API key)
- **Auth:** free API key from glassnode.com (no credit card required for Tier 1)
- **Symbols:**
  - `BTC_ACTIVE_ADDRS`, `BTC_TX_COUNT`, `BTC_PRICE_USD`, `BTC_MARKET_CAP`, `BTC_HASH_RATE`
  - `ETH_ACTIVE_ADDRS`, `ETH_TX_COUNT`
- **Resolution:** daily (free tier limit)
- **Rate limit:** 30 req/min
- **Strategy use:** network usage holding up while price falls = bullish divergence
  between fundamentals and price action. Hash rate ATH = miner conviction proxy.
- **Configure:** Settings → API Keys → Glassnode → paste key

### OnChain — *CoinGecko* (NEW, no key)
- **Auth:** none for public API
- **Symbols:**
  - Global: `GLOBAL_TOTAL_CAP`, `GLOBAL_BTC_DOM`, `GLOBAL_ETH_DOM`
  - Per-coin: `BTC_MCAP`, `ETH_MCAP`
- **Resolution:** daily
- **Rate limit:** ~20 req/min on free tier
- **Strategy use:** market breadth — rising total cap with falling BTC dominance is
  altseason; flight-to-quality is the opposite. The Cipher framework cannot see
  breadth, so this is genuinely orthogonal information.
- **Caveat:** the global breadth metrics have NO public historical endpoint; only
  the latest snapshot is returned. Per-coin market cap (`BTC_MCAP` / `ETH_MCAP`) has
  full daily history up to 365 days.

## Standardised access pattern for indicators

There are **three ways** an indicator or strategy can consume analytics data,
ordered from least to most coupled:

### Pattern 1 — Load as a separate chart series

The user adds the analytics symbol to a chart the same way they add a price symbol:
**market dropdown → Derivatives → BinanceDerivatives → BTCUSDT_FUNDING → 1d → Load**.
The series renders in its own pane. Indicators that operate on a chart series can
be applied to it (e.g. an SMA on funding rates, a Bollinger Band on the F&G index).

The strategy system can then leaf on signals exposed by indicators applied to that
series — same as any other indicator. **Zero new plumbing required.** This is how
FRED already works.

**When to use:** quick visual inspection, manual analysis, or one-off backtests
where the analytics series is the *primary* instrument.

### Pattern 2 — Cross-series indicator (recommended for strategies)

A custom indicator that, when added to a price chart, *internally fetches* a related
analytics series via `IDataOrchestrator.FetchOhlcvAsync` and aligns it to the active
chart's bars. This is the right pattern for "I want a Funding Rate signal *while
looking at BTC price*" — the indicator silently pulls funding alongside.

**Implementation skeleton:**

```csharp
public class FundingRateIndicator : IIndicatorProvider
{
    private readonly IDataOrchestrator _data;

    public FundingRateIndicator(IDataOrchestrator data) => _data = data;

    public void Calculate(string code, ReadOnlySpan<Ohlcv> bars,
        Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
    {
        // Fetch funding aligned to the active chart's date range.
        var fundingTask = _data.FetchOhlcvAsync(
            market:    "Derivatives",
            provider:  "BinanceDerivatives",
            symbol:    "BTCUSDT_FUNDING",
            timeframe: "1d",
            since:     new DateTimeOffset(bars[0].Date).ToUnixTimeMilliseconds(),
            limit:     bars.Length,
            until:     new DateTimeOffset(bars[^1].Date).ToUnixTimeMilliseconds());
        var funding = fundingTask.GetAwaiter().GetResult(); // Calculate is sync

        // Align funding values to the active chart bars by timestamp.
        var aligned  = AlignByDate(bars, funding);
        var extreme  = aligned.Select(v => v > 0.05 ? v : double.NaN).ToArray();

        WriteToBuffer(buffer, "Funding Rate", aligned);
        WriteToBuffer(buffer, "Extreme Long Pressure", extreme);
    }
}
```

**Caveats:**
- The `Calculate` path is sync, so the fetch must block on `.GetAwaiter().GetResult()`.
  This is fine for backtests and chart loads (one-shot fetch); for live ticks the
  indicator should cache the most recent fetch and refresh on a TTL.
- HTTP errors should be swallowed and emit NaN — never throw out of `Calculate`.
- The `IDataOrchestrator` already wraps fetches in Polly retry + circuit breaker, so
  you don't need to add resilience inside the indicator.

**When to use:** building a strategy leaf that needs an analytics signal as part of
its score gate, on a chart whose primary instrument is a price series (BTC, ETH, etc.).

### Pattern 3 — Strategy condition leaf with `Timeframe` routing (future)

A future enhancement could add a `MarketCategory` field to `ConditionLeaf` that lets
a strategy spec say *"this leaf reads from `Derivatives::BinanceDerivatives::BTCUSDT_FUNDING`"*
without requiring a custom indicator. The HTF cache infrastructure
(`IMultiTimeframeDataService.PrewarmIndicatorAsync`) is the right shape for this —
it already pre-warms cross-instrument data on strategy initialization.

**Status:** not yet implemented. Pattern 2 (cross-series indicator) is the
recommended path until this lands.

## Adding a new analytics provider

Five steps. The infrastructure auto-discovers plugins via `IPluginLoaderService` —
no DI registration required.

1. **Create a plugin csproj** at `Plugins/AccessibleTrader.Plugins.{Name}/`.
   Mirror an existing one (e.g. `AccessibleTrader.Plugins.AlternativeMe`) for the
   correct target framework, package references, and SDK project reference.

2. **Implement `BaseMarketDataProvider`** with:
   - `Name` — unique identifier (used in dropdown and EnsureContains)
   - `SupportedMarkets => new() { MarketType.OnChain }` (or whichever category fits)
   - `RequiresApiKey` — set to true if your source needs auth
   - `IsConfigured` — return true once `Configure()` has been called with a valid key
   - `FetchOhlcvAsync` — fetch from your source, return as `Ohlcv` with O=H=L=C=value

3. **Add the provider name to `MarketOrchestrator.RefreshProvidersAsync`** in the
   `EnsureContains` switch for the appropriate market category. This ensures the
   provider appears in the dropdown even if the data service hasn't yet discovered
   the DLL on a fresh install.

4. **(Optional) Add the market type to `MarketType.cs`** if your data is a brand
   new category not already covered (Economic, OnChain, Derivatives, Sentiment).
   Then update the `IsAnalyticsCategory` predicate in `MarketOrchestrator` to
   include the new category in Analytics-mode filtering.

5. **Build the plugin csproj** — `dotnet build Plugins/AccessibleTrader.Plugins.{Name}/...csproj`.
   The DLL ends up in `bin/Debug/net10.0/`. Copy it to the host's `Plugins` directory
   (or wire the build output via msbuild target — see existing plugins for the
   pattern).

> ### ⚠ FMP is BROKEN for any key issued after 2025-08-31 (verified 2026-08-01)
>
> Both FMP providers target `https://financialmodelingprep.com/api/v3` (and `/api/v4` for the
> analytics side). FMP retired those paths: they now answer **403 `Legacy Endpoint`** for every
> key that did not have a subscription before **2025-08-31**. Keys older than that still work, so
> this fails only for new users — the exact shape of silent read-path failure the provider audit
> called out. Verified against a live key: `api/v3/quote`, `api/v3/stock/list`,
> `api/v3/historical-price-full`, `api/v3/earnings-surprises`, `api/v3/economic_calendar` and
> `api/v4/historical/earning_calendar` all return 403; the same key succeeds on `/stable/`.
>
> **The fix** is a base-URL and response-shape migration to `https://financialmodelingprep.com/stable`,
> where paths take the symbol as a query parameter (`/stable/quote?symbol=AAPL`) rather than in
> the path. What that key can reach, tested 2026-08-01:
>
> | works on `/stable/` | restricted (402) on this plan |
> |---|---|
> | `quote`, `profile`, `search-symbol` | `stock-list`, `company-screener` |
> | `historical-price-eod/full` | `news/stock` |
> | `income-statement`, `ratios`, `key-metrics`, `key-metrics-ttm` | `insider-trading/search` |
> | `earnings` (**carries `epsActual` + `epsEstimated`**) | `institutional-ownership/*` |
> | `analyst-estimates`, `grades-consensus`, `dividends` | `economic-calendar` |
> | `treasury-rates`, `economic-indicators`, `sector-performance-snapshot` | `earnings-calendar` over a wide range |
> | `cryptocurrency-list` | |
>
> **Paying more does not fix this.** The 403 is an API-*version* retirement, not a subscription
> gate — the same key at the same moment gets 403 on `/api/v3/quote` and 200 on `/stable/quote`.
> The migration is required at every price point, including the free one.
>
> **What the free tier actually allows** (measured 2026-08-01, not inferred): EOD prices are fine —
> 5,000 rows back to 2006 in one call. Fundamentals and earnings are capped at **`limit` ≤ 5**;
> asking for more returns 402. So the free tier gives five quarters per symbol, which is enough to
> *display* and useless for a study.
>
> **Plans** (personal use, billed annually; monthly billing costs up to ~34% more): Basic free
> (250 calls/day) · **Starter $22/mo** (300 calls/min, 5 years of history, US coverage, annual
> fundamentals and ratios, news, crypto/forex) · **Premium $59/mo** (750 calls/min, 30 years,
> UK/Canada, full fundamentals, intraday, technical indicators, corporate calendars) ·
> **Ultimate $149/mo** (global, transcripts, ETF/fund holdings, 13F, 1-minute intraday, bulk).
> Insider trading and 13F sit in the upper tiers; FMP does not publish a per-endpoint plan badge,
> so which tier first unlocks `economic-calendar` is unverified — Premium's "corporate calendars"
> is the closest listed match.
>
> **Is any FMP tier worth buying? No — verified 2026-08-01.** Everything the paid tiers sell is
> available free from a primary source, including the one thing that looked scarce:
>
> | what a paid tier gives | free alternative, tested |
> |---|---|
> | 30 years of fundamentals (Premium) | **SEC EDGAR XBRL** — `data.sec.gov/api/xbrl/companyconcept/...`, official, free, no key. 338 EPS datapoints for AAPL back to 2007 in one call. FMP is reselling this |
> | 13F institutional holdings (Ultimate) | SEC EDGAR — 13F filings are public record |
> | insider trades (upper tiers) | SEC EDGAR Form 4 |
> | EOD prices | already free from our own providers |
> | market news | free or near-free from many sources |
> | **analyst / earnings estimates** | **Alpha Vantage `EARNINGS`, FREE tier** — verified: 122 quarters back to **1996** for IBM with `reportedEPS`, `estimatedEPS`, `surprise`, `surprisePercentage`, `reportedDate` and pre/post-market flag |
>
> The Alpha Vantage result is the decisive one. Consensus estimates were the only genuinely scarce
> item — actuals are free everywhere, expectations are proprietary — and the free tier carries actual,
> estimate and surprise back three decades. Its limit is 25 requests/day, which does not matter here:
> one request returns a symbol's entire history, the lab works from snapshots, so a hundred-symbol
> earnings-surprise dataset costs four days of polling and nothing else.
>
> The one thing still not obtainable free is **macro** consensus (the economic calendar). Given that
> the macro release-DATE study already came back null and the surprise hypothesis can now be tested on
> company earnings instead, that gap does not currently block anything.
>
> **Two consequences for research planning.** **Macro consensus is not available** on this key
> (`economic-calendar` is 402), so the actual-minus-consensus macro-surprise test cannot be built on
> FMP as things stand. But `/stable/earnings` returns actual *and* estimated EPS per quarter, so the
> same hypothesis — the surprise moves price, not the date — **is** testable on company earnings,
> with the caveat that five quarters per symbol is not an event study. Starter's five-year cap buys
> a single regime; Premium's thirty years is the first tier that supports the test properly.

### Equities — *FMP* (NEW, requires free API key)
- **Auth:** free API key from financialmodelingprep.com (250 req/day, no CC)
- **Coverage:** 70,000+ stocks across 60+ exchanges, 4,500+ cryptos, 1,500+ forex pairs, 40 commodities, indices
- **Markets:** Stock (Spot + ETF), Crypto, Forex, Commodity, Index
- **Resolution:** 1m, 5m, 15m, 30m, 1h, 4h, 1d
- **Configure:** Settings → API Keys → FMP → paste key

### Equities Fundamentals — *FMP Analytics* (NEW, requires same FMP API key)
- **Sub-types:** Key Metrics, Income, Ratios, Earnings, Sector Perf, Economic
- **Key Metrics symbols:** `{TICKER}_PE`, `{TICKER}_PB`, `{TICKER}_ROE`, `{TICKER}_DIVIDEND_YIELD`, etc. (15 metrics × 42 popular tickers)
- **Income symbols:** `{TICKER}_REVENUE`, `{TICKER}_NET_INCOME`, `{TICKER}_EPS`, etc. (11 metrics)
- **Ratios symbols:** `{TICKER}_PROFIT_MARGIN`, `{TICKER}_DEBT_RATIO`, etc. (10 metrics)
- **Earnings:** `{TICKER}_EARNINGS` — actual vs estimated EPS per quarter
- **Sector Perf:** Technology, Healthcare, Energy, etc. — daily sector return %
- **Economic:** EARNINGS_CALENDAR, ECONOMIC_CALENDAR, IPO_CALENDAR, DIVIDEND_CALENDAR, SPLIT_CALENDAR
- **Strategy use:** earnings avoidance gate, fundamental screening, sector rotation signals

### OnChain — *BGeometrics* (NEW, no key required)
- **Auth:** none for free tier (8 req/hr, 15 req/day, 4yr history)
- **Symbols:** MVRV, SOPR, ASOPR, STH_SOPR, LTH_SOPR, NVT, NVT_RATIO, NVT_SIGNAL, NUPL, NUPL_LTH, NUPL_STH, CDD, CVDD, REALIZED_PRICE, REALIZED_CAP, RESERVE_RISK, S2F, TERMINAL_PRICE, BALANCED_PRICE, PUELL_MULTIPLE, FUNDING_RATE, OI_FUTURES, ACTIVE_ADDRESSES, HODL_WAVES, BTC_SUPPLY, MAYER_MULTIPLE, FEAR_GREED, PI_CYCLE (28 symbols)
- **Resolution:** daily
- **BTC only** — for multi-asset on-chain, use CoinMetrics
- **Strategy use:** MVRV < 1 = undervalued, SOPR < 1 = capitulation, NUPL zones map to market cycle phases

### OnChain — *CoinMetrics* (NEW, no key required)
- **Auth:** none (community tier, 10 req/6s)
- **Assets:** BTC, ETH, LTC, DOGE, ADA, XRP, DOT, LINK, UNI (9 assets)
- **Metrics per asset:** MVRV, ACTIVE_ADDR, HASH_RATE, TX_COUNT, MARKET_CAP, EXCHANGE_INFLOW, EXCHANGE_OUTFLOW, SUPPLY, EXCHANGE_SUPPLY, FEES, PRICE, ROI_30D, TRANSFER_COUNT (13 metrics)
- **Resolution:** daily
- **Strategy use:** multi-asset MVRV comparison, exchange flow divergence from price

### OnChain — *DefiLlama* (NEW, no key required)
- **Auth:** none (generous limits)
- **TVL symbols:** ETHEREUM_TVL, BSC_TVL, SOLANA_TVL, ARBITRUM_TVL, POLYGON_TVL, AVALANCHE_TVL, BASE_TVL, OPTIMISM_TVL, TRON_TVL, BITCOIN_TVL, TOTAL_TVL + top protocols (LIDO_TVL, AAVE_TVL, etc.)
- **Stablecoin symbols:** USDT_SUPPLY, USDC_SUPPLY, DAI_SUPPLY, TOTAL_STABLECOIN_SUPPLY
- **Resolution:** daily
- **Strategy use:** TVL divergence from price = leading indicator. Rising stablecoin supply on exchanges = dry powder waiting to buy.

### OnChain — *Mempool* (NEW, no key required)
- **Auth:** none
- **Symbols:** HASHRATE, DIFFICULTY, BLOCK_FEES, BLOCK_REWARDS, BLOCK_SIZES, BLOCK_FEE_RATES
- **Resolution:** varies (aggregated by block height, daily-equivalent)
- **Strategy use:** mempool congestion → fee spikes → retail panic. Hash rate ATH = miner conviction.

### OnChain — *Etherscan* (NEW, requires free API key)
- **Auth:** free API key from etherscan.io (5 req/sec)
- **Symbols:** ETH_GAS_SAFE, ETH_GAS_FAST, ETH_GAS_PROPOSE, ETH_SUPPLY, ETH_SUPPLY2, ETH_PRICE, ETH_NODE_COUNT
- **Resolution:** snapshot (current value per request)
- **Strategy use:** gas price spikes = network congestion = potential volatility catalyst

## Free / cheap data sources NOT yet built (future candidates)

- **CryptoQuant** — no confirmed free API tier (requires auth, docs behind login). May revisit if community tier materializes.
- **Messari** — free public API with token fundamentals, supply data. Some endpoints rate-limit aggressively.
- **Blockscout** — free per-address transaction history for EVM chains. Useful for whale-wallet tracking.
- **CoinAPI** — free tier 100 req/day; aggregated OHLCV. Mostly duplicates existing providers.
- **Alpha Vantage** — free tier 25 req/day. Stocks + crypto + forex + technical indicators. Redundant with FMP.

## NOT recommended (paid, fragile, or low-signal)

- **Order book L2/L3** — historical isn't really available without paying $150+/mo to
  Tardis.dev. Live order book is hard to use without HFT-grade infrastructure.
- **Twitter / Reddit sentiment** — paid APIs in the $100s/month range, questionable
  signal-to-noise, hard to backtest.
- **News scrapers** — fragile, often rate-limited or blocked, and the AI Analyst
  feature already covers ad-hoc news lookups.
- **Alternative data** (satellite, credit card, foot traffic) — institutional-only,
  thousands of dollars per month, not relevant at retail scale.
