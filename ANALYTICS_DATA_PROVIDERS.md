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

All four are under the **Analytics** terminal mode (the radio button where FRED lives).
The market dropdown reflects the data category; the provider dropdown picks the source.

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

## Free / cheap data sources NOT yet built (next batch)

These are good candidates if the existing four don't cover what you need:

- **CryptoQuant Community** — free tier with email signup; ~30 on-chain metrics
  including stablecoin exchange flows (a high-information signal). Similar shape to
  Glassnode. Build effort: ~1 hour.
- **Messari** — free public API with token fundamentals, supply data, and a few
  on-chain metrics. Some endpoints rate-limit aggressively. Build effort: ~1 hour.
- **Etherscan / Blockscout** — free per-address transaction history for ETH chains.
  Useful for whale-wallet tracking. Build effort: ~2 hours.
- **CoinAPI** — free tier 100 req/day; aggregated OHLCV across exchanges. Mostly
  duplicates what Binance/Coinbase already provide.
- **DefiLlama** — free public API with TVL by chain / protocol. Build effort: ~1 hour.

## NOT recommended (paid, fragile, or low-signal)

- **Order book L2/L3** — historical isn't really available without paying $150+/mo to
  Tardis.dev. Live order book is hard to use without HFT-grade infrastructure.
- **Twitter / Reddit sentiment** — paid APIs in the $100s/month range, questionable
  signal-to-noise, hard to backtest.
- **News scrapers** — fragile, often rate-limited or blocked, and the AI Analyst
  feature already covers ad-hoc news lookups.
- **Alternative data** (satellite, credit card, foot traffic) — institutional-only,
  thousands of dollars per month, not relevant at retail scale.
