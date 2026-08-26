# Accessible Trader — Data Provider Plugin Authoring Guide

This document covers how to build data provider plugins (market data, analytics,
on-chain metrics, etc.) for Accessible Trader. For indicator plugins, see
`PLUGIN_AUTHORING.md`.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Directory Structure](#2-directory-structure)
3. [Creating a Provider Project](#3-creating-a-provider-project)
4. [BaseMarketDataProvider](#4-basemarketdataprovider)
5. [Provider Metadata](#5-provider-metadata)
6. [Implementing FetchOhlcvAsync](#6-implementing-fetchohlcvasync)
7. [Analytics Providers (SingleValueLine)](#7-analytics-providers-singlevalueline)
8. [Symbol Display Names and Render Hints](#8-symbol-display-names-and-render-hints)
9. [Live Streaming](#9-live-streaming)
10. [API Key Integration](#10-api-key-integration)
11. [Rate Limiting](#11-rate-limiting)
12. [Disposal](#12-disposal)
13. [Quick-Start Example](#13-quick-start-example)
14. [Trading Providers (ITradingProvider)](#14-trading-providers-itradingprovider)
15. [Shared Plumbing and House Rules](#15-shared-plumbing-and-house-rules)

---

## 1. Overview

A data provider plugin is a class that extends `BaseMarketDataProvider` from the
`AccessibleTrader.Sdk` assembly. It supplies historical OHLCV data, symbol lists,
and optionally live streaming.

Provider plugins are **auto-discovered** at startup. Drop a compiled DLL into the
correct directory and the app picks it up — no recompilation needed.

### Two Provider Categories

| Category | Directory | Examples | DataShape |
|----------|-----------|----------|-----------|
| **Providers** (tradeable data) | `Plugins/Providers/` | Binance, Alpaca, Polygon | `Ohlcv` |
| **Analytics** (non-tradeable data) | `Plugins/Analytics/` | FRED, BGeometrics, DefiLlama | `SingleValueLine` |

The distinction is purely organizational. Both implement the same `IMarketDataProvider`
interface via `BaseMarketDataProvider`.

---

## 2. Directory Structure

```
Plugins/
  Providers/                        ← Trading/market data providers
    AccessibleTrader.Plugins.Binance/
      AccessibleTrader.Plugins.Binance.csproj
      BinanceProvider.cs
  Analytics/                        ← Non-tradeable analytics providers
    AccessibleTrader.Plugins.BGeometrics/
      AccessibleTrader.Plugins.BGeometrics.csproj
      BGeometricsProvider.cs
  Indicators/                       ← Indicator plugins (see PLUGIN_AUTHORING.md)
```

### Discovery Directories (scanned at startup)

1. `AppDomain.CurrentDomain.BaseDirectory` — where MAUI flattens DLLs at publish
2. `{BaseDir}/Plugins/` — recursive scan through all subdirectories
3. `%LOCALAPPDATA%\AccessibleTrader\Plugins\` — user drop-in folder (auto-created)

**DLL naming convention:** `AccessibleTrader.Plugins.*.dll`

---

## 3. Creating a Provider Project

Create a new .NET class library:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <!-- Reference the SDK — adjust path for your project location -->
    <ProjectReference Include="..\..\..\AccessibleTrader.Sdk\AccessibleTrader.Sdk.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- Add any API client NuGet packages your provider needs -->
    <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
  </ItemGroup>
</Project>
```

**Key:** `CopyLocalLockFileAssemblies` must be `true` so NuGet dependencies are copied
alongside your DLL. The plugin loader uses an isolated `AssemblyLoadContext` that resolves
dependencies from your DLL's folder.

**Constructor:** Must be **parameterless** — plugins are instantiated via
`Activator.CreateInstance()`, not DI.

---

## 4. BaseMarketDataProvider

All providers extend `BaseMarketDataProvider`, which implements `IMarketDataProvider`
and `IProviderPlugin`. It manages:

- Rx subjects for `LiveStream`, `ErrorStream`, `ConnectionStateStream`
- `IDisposable` with a virtual `Dispose(bool)` pattern
- `GetCapability<T>()` returning `this` for `IMarketDataProvider` **only** — every
  other capability (trading, order book, wallet) returns `null` until you override
  it. Implementing `ITradingProvider` on your class does nothing until
  `GetCapability<ITradingProvider>()` returns `this`; see
  [Section 14](#14-trading-providers-itradingprovider).

```csharp
using AccessibleTrader.Sdk.Plugins;

public class MyProvider : BaseMarketDataProvider
{
    // Override abstract/virtual members below
}
```

---

## 5. Provider Metadata

Override these properties to describe your provider:

```csharp
public override string Name        => "MyProvider";
public override string Description => "My Provider — description of what it provides";

// Which market categories this provider appears under
public override List<MarketType> SupportedMarkets => new() { MarketType.Crypto };

public override bool SupportsSymbolSearch => false;   // true if you support search
public override bool RequiresApiKey       => true;    // false for free APIs
public override bool IsConfigured         => _apiKey != null;
public override bool SupportsLiveUpdates  => false;   // true if you push live ticks
public override ProviderEnvironment Environment => ProviderEnvironment.Live;
public override int MaxBarsPerRequest     => 1000;    // max bars per fetch
```

### MarketType Enum

```csharp
public enum MarketType
{
    Crypto,       // Binance, Kraken, Coinbase
    Stock,        // Alpaca, Polygon
    Forex,        // Oanda, Finnhub
    Commodity,    // FMP
    Economic,     // FRED, FMP Analytics      ← Analytics mode
    OnChain,      // Glassnode, BGeometrics    ← Analytics mode
    Options,      // (reserved)
    Futures,      // InteractiveBrokers
    Bonds,        // (reserved)
    Index,        // FMP
    Derivatives,  // BinanceDerivatives        ← Analytics mode
    Sentiment     // AlternativeMe             ← Analytics mode
}
```

Markets marked "Analytics mode" appear only when the user switches to the Analytics
radio button. All others appear in Trading mode.

### Timeframes

```csharp
public override List<string> NativelySupportedTimeframes => new()
{
    StandardTimeframes.OneMinute,    // "1m"
    StandardTimeframes.FiveMinutes,  // "5m"
    StandardTimeframes.FifteenMinutes, // "15m"
    StandardTimeframes.OneHour,      // "1h"
    StandardTimeframes.FourHours,    // "4h"
    StandardTimeframes.OneDay,       // "1d"
    StandardTimeframes.OneWeek       // "1w"
};
```

---

## 6. Implementing FetchOhlcvAsync

This is the core data method. It receives a `MarketDataRequest` and returns OHLCV bars
plus separate volume tuples.

```csharp
public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)>
    FetchOhlcvAsync(MarketDataRequest request)
{
    // request.Symbol   — e.g. "BTCUSDT"
    // request.Market   — e.g. "Crypto" or "Crypto|Spot"
    // request.Timeframe — e.g. "1h"
    // request.Since    — optional start time (Unix ms)
    // request.Until    — optional end time (Unix ms)
    // request.Limit    — optional max bars

    var bars = new List<Ohlcv>();

    // ... fetch from your API ...

    foreach (var item in apiResponse)
    {
        bars.Add(new Ohlcv(
            Date:   item.Timestamp,   // DateTime (UTC)
            Open:   item.Open,        // double
            High:   item.High,        // double
            Low:    item.Low,         // double
            Close:  item.Close,       // double
            Volume: item.Volume       // double
        ));
    }

    // TimeSpan.Zero pins the conversion: bare new DateTimeOffset(dt) reads the
    // machine's local zone when dt.Kind is Unspecified, so the volume pane shifts
    // against the candles on any non-UTC box while passing on a UTC dev machine.
    var volumes = bars
        .Select(b => (new DateTimeOffset(b.Date, TimeSpan.Zero).ToUnixTimeMilliseconds(), b.Volume))
        .ToList();

    return (bars, volumes);
}
```

### Required Support Methods

```csharp
// Return available symbols for a market category
public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
    => Task.FromResult(new List<string> { "BTCUSDT", "ETHUSDT" });

// Return sub-types (e.g., Spot vs Futures)
public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
    => Task.FromResult(new List<string> { "Spot" });

// Return supported timeframes
public override Task<List<string>> GetSupportedTimeframesAsync()
    => Task.FromResult(NativelySupportedTimeframes);

// Connection lifecycle
public override Task EnsureConnectedAsync()
{
    _connectionStateStream.OnNext(ConnectionState.Connected);
    return Task.CompletedTask;
}

public override Task DisconnectAsync()
{
    _connectionStateStream.OnNext(ConnectionState.Disconnected);
    return Task.CompletedTask;
}

// Subscription (for live updates — no-op if not supported)
public override Task SetSubscriptionAsync(string market, string symbol, string timeframe)
    => Task.CompletedTask;

// Order book (return empty if not supported)
public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)>
    GetOrderBookAsync(string symbol, int limit = 10)
    => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));
```

---

## 7. Analytics Providers (SingleValueLine)

Analytics providers return single-value time series (not true OHLCV). Override
`DataShape` so the chart renders a line instead of candles:

```csharp
public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;
```

In `FetchOhlcvAsync`, set `O = H = L = C = value` and `Volume = 0`:

```csharp
bars.Add(new Ohlcv(date, value, value, value, value, 0));
```

The chart engine detects `SingleValueLine` and seeds a Price line series instead
of the default Candles + Volume + Price stack.

---

## 8. Symbol Display Names and Render Hints

### Display Names

Override `GetSymbolDisplayName` to give symbols human-readable labels. This controls
what the TTS speech system says:

```csharp
public override string GetSymbolDisplayName(string symbol) => symbol switch
{
    "MVRV"       => "Market Value to Realized Value",
    "SOPR"       => "Spent Output Profit Ratio",
    "FEAR_GREED" => "Fear & Greed Index",
    _            => symbol
};
```

### Render Hints

Override `GetSymbolRenderHints` to declare bounded ranges, reference levels, and
audio zones for analytics metrics:

```csharp
public override SymbolRenderHints? GetSymbolRenderHints(string symbol) => symbol switch
{
    "FEAR_GREED" => new SymbolRenderHints(
        RangeMin:      0,
        RangeMax:      100,
        DisplayType:   ComponentDisplayType.Oscillator,
        SpeechTemplate:"{name}. {value:F0}.",
        ColorHex:      "#FFD54F",
        ReferenceLevels: new[]
        {
            new LevelDescriptor(
                Name: "Extreme Fear", Value: 25,
                ColorHex: "#26A69A", Dash: DashStyle.Dash,
                PlayEarcon: true, EarconVolume: 0.6f,
                ZoneNoiseAmount: 0.25f, ZoneNoiseType: "pink"),
            new LevelDescriptor(
                Name: "Neutral", Value: 50,
                ColorHex: "#888888", Dash: DashStyle.Dot),
            new LevelDescriptor(
                Name: "Extreme Greed", Value: 75,
                ColorHex: "#EF5350", Dash: DashStyle.Dash,
                PlayEarcon: true, EarconVolume: 0.6f,
                ZoneNoiseAmount: 0.25f, ZoneNoiseType: "pink"),
        }),
    _ => null
};
```

---

## 9. Live Streaming

If your provider supports live updates, set `SupportsLiveUpdates => true` and push
ticks through the inherited `_liveStream` subject:

```csharp
public override bool SupportsLiveUpdates => true;

public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
{
    // Connect to your WebSocket / streaming API
    _ws = new ClientWebSocket();
    await _ws.ConnectAsync(uri, CancellationToken.None);

    // In your receive loop:
    _liveStream.OnNext(new Ohlcv(DateTime.UtcNow, open, high, low, close, volume));
}
```

The `LiveStreamManager` subscribes to `LiveStream`, aggregates ticks into timeframe
buckets, and pushes them to the chart.

### Declare your tick semantics: `LiveTickStyle`

The consolidator needs to know what your pushes *mean*, and the two meanings are
irreconcilable if guessed wrong:

```csharp
// Default: each push is an independent trade/tick — volumes ADD into the bucket.
public override LiveTickStyle LiveTickStyle => LiveTickStyle.TradeDeltas;

// Kline-style feeds (Binance, MEXC, …): each push is the RUNNING state of the
// current bar — volume is a running total and must REPLACE, not add.
public override LiveTickStyle LiveTickStyle => LiveTickStyle.CumulativeBars;
```

A kline-style feed left on the `TradeDeltas` default double-counts its running
volume total on every push — and volume is sonified, so the error is audible
before it is visible. If your venue pushes candle snapshots, override this.

### Trading providers: `SupportsOrderEventStreaming` must be honest

`ITradingProvider.SupportsOrderEventStreaming` **defaults to `true`**, and the order
service reads it **at order-placement time** to decide whether to start the fill
poller. That makes the default a trap: a provider whose push channel is dead — never
opened, auth rejected, listen key expired, socket down — but whose flag still says
`true` gets *neither* stream events *nor* polling, and fills are announced by no path
at all. This shipped, more than once.

Report the flag from the **live state of the actual push channel**, not from
configuration or intent:

```csharp
// Pattern (see MEXC, Tradier, Coinbase, Binance for real examples):
public bool SupportsOrderEventStreaming => _privateWs?.IsConnected ?? false;
```

Count the channel as up only once the venue has *accepted* it (subscription
acknowledged, auth succeeded, listen key alive) — a connected socket with a rejected
subscription is exactly the silent state this flag exists to expose. If your provider
has no push channel, declare `=> false` statically (Schwab) so the poller always runs.

---

## 10. API Key Integration

If `RequiresApiKey => true`, the `Configure` method receives credentials from the
user's saved API key profiles:

```csharp
private string? _apiKey;
private string? _apiSecret;

public override void Configure(Dictionary<string, string> config)
{
    config.TryGetValue("ApiKey", out _apiKey);
    config.TryGetValue("ApiSecret", out _apiSecret);
    config.TryGetValue("Passphrase", out var passphrase);
}

public override Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
{
    if (string.IsNullOrEmpty(_apiKey))
        return Task.FromResult((false, "API key is required."));

    // Optionally make a lightweight test request
    return Task.FromResult((true, "OK"));
}
```

Users manage their keys via the API Keys modal (Alt+K). The provider name in the modal
must match `Name` exactly.

---

## 11. Rate Limiting

The SDK includes a `RateLimiter` utility for throttling API calls:

```csharp
using AccessibleTrader.Sdk.Services;

private readonly RateLimiter _rateLimiter = new(10, TimeSpan.FromMinutes(1));

public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
{
    return await _rateLimiter.ExecuteAsync(async () =>
    {
        // your API call here
    }).ConfigureAwait(false);
}
```

---

## 12. Disposal

Override `Dispose(bool)` to clean up HTTP clients, WebSockets, etc.:

```csharp
protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        _httpClient?.Dispose();
        _webSocket?.Dispose();
    }
    base.Dispose(disposing);
}
```

---

## 13. Quick-Start Example

A minimal free analytics provider:

```csharp
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.MyMetric
{
    public class MyMetricProvider : BaseMarketDataProvider
    {
        private readonly HttpClient _http = new();

        public override string Name        => "MyMetric";
        public override string Description => "My custom on-chain metric";
        public override List<MarketType> SupportedMarkets => new() { MarketType.OnChain };
        public override bool RequiresApiKey       => false;
        public override bool IsConfigured         => true;
        public override bool SupportsLiveUpdates  => false;
        public override bool SupportsSymbolSearch => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 5000;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;
        public override List<string> NativelySupportedTimeframes => new() { "1d" };

        public override string GetSymbolDisplayName(string symbol) => symbol switch
        {
            "MY_METRIC" => "My Custom Metric",
            _ => symbol
        };

        public override void Configure(Dictionary<string, string> config) { }

        public override Task EnsureConnectedAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Connected);
            return Task.CompletedTask;
        }

        public override Task SetSubscriptionAsync(string m, string s, string t) => Task.CompletedTask;

        public override Task DisconnectAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType m, string sub = "Spot")
            => Task.FromResult(new List<string> { "MY_METRIC" });

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType m)
            => Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(NativelySupportedTimeframes);

        // The tuple element names are part of both override signatures — dropping
        // them is CS8139 ("cannot change tuple element names when overriding").
        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)>
            GetOrderBookAsync(string s, int l = 10)
            => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)>
            FetchOhlcvAsync(MarketDataRequest request)
        {
            var json = await _http.GetStringAsync("https://api.example.com/metric")
                .ConfigureAwait(false);
            var arr = JArray.Parse(json);
            var bars = new List<Ohlcv>();

            foreach (var pt in arr)
            {
                // Pin the kind: Newtonsoft's DateTime conversion keeps whatever the
                // string implied, and an Unspecified kind is read as LOCAL time by
                // the epoch conversion below — correct on a UTC box, shifted
                // everywhere else.
                var date = DateTime.SpecifyKind(pt["date"]!.Value<DateTime>(), DateTimeKind.Utc);
                var val  = pt["value"]!.Value<double>();
                bars.Add(new Ohlcv(date, val, val, val, val, 0));
            }

            var vols = bars.Select(b =>
                (new DateTimeOffset(b.Date, TimeSpan.Zero).ToUnixTimeMilliseconds(), b.Volume)).ToList();
            return (bars, vols);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _http?.Dispose();
            base.Dispose(disposing);
        }
    }
}
```

Build this project, copy the output DLL (and any dependencies) to
`%LOCALAPPDATA%\AccessibleTrader\Plugins\`, and restart the app.
The provider will appear automatically in the appropriate market dropdown.

---

## 14. Trading Providers (ITradingProvider)

Implement `ITradingProvider` on the same class as the data provider and wire it up:

```csharp
public override T? GetCapability<T>() where T : class
{
    if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
    if (typeof(T) == typeof(ITradingProvider))    return this as T;
    return null;
}
```

Without the `GetCapability` branch the interface is invisible to the app —
implementing it is not enough.

### Capability flags are promises

`ProviderCapabilities` gates which controls the order panel renders, so **a flag is
a promise to a user**: declare `TrailingStop` and the trailing fields appear; if
`PlaceOrderAsync` then ignores `TrailStopValue`, the user typed a protective level
that does not exist, heard nothing, and holds an unprotected position. Declare only
what `PlaceOrderAsync` actually sends. Two suites police this and both must stay
green when you add a provider:

- `ProviderCapabilityHonestyTests` — cross-provider invariants (OCO requires
  `IOcoTradingProvider`, `Leverage` requires a margin/futures product and
  `MaxLeverage > 1`, `Brackets` requires a protective leg, the flag-derived bools
  cannot disagree with the flags).
- `ProviderCapabilityAudit` (StrategyLab) — a source scan that checks each flag
  against evidence in your code (reading `TrailStopValue`, implementing
  `IOcoTradingProvider`, …). Do not read a `TradeSignal` field *purely to refuse
  it* — the audit reads any use of the field as evidence the capability exists.

`SupportsStopLoss` / `SupportsTakeProfit` render the SL/TP fields. If your venue
needs a bracket structure you have not built yet, **refuse the order with spoken
text** rather than placing the entry and dropping the legs — a naked position sized
for a stop that does not exist is the worst silent failure in this codebase's
history, and it has shipped on four venues at once.

### The PlaceOrderAsync return protocol

`PlaceOrderAsync` returns a `string`. This string protocol is the **plugin
boundary contract** and it is not going away — but it is now recognised exactly
once, by `OrderPlacement.Parse`, at the edge of Core. Nothing above
`GeneralOrderService` ever sees a status string; every caller reads a typed
`OrderPlacement`. Write to this table and the terminal says the right thing:

- **Success:** the venue's order id, verbatim → `OrderOutcome.Placed`. The order
  service hands it to the status poller and the fill announcements depend on it
  being real.
- **Failure:** `"ORDER_FAILED:<reason>"` → `Rejected`. Everything after the colon
  is **spoken to the user**, so write the reason as a sentence a trader can act
  on ("Tradier trades whole shares only; round the quantity and place the order
  again"), not an error code.
- **Not configured:** `"PROVIDER_NOT_CONFIGURED"` (optionally
  `"PROVIDER_NOT_CONFIGURED:<reason>"`) → `Unavailable`. The whole
  `PROVIDER_NOT_*` family parses the same way, bare or with a reason.
- **Accepted, no id:** `"ORDER_SUBMITTED"` → `Accepted`. Use this ONLY when the
  venue really did accept the order and really did not return an id. It is a
  success: brackets are verified, but no fill poll can start, so the terminal
  tells the user their fill cannot be announced.
- **Never invent other `ORDER_`/`PROVIDER_`-prefixed return values.** Both
  prefixes are reserved, and an unrecognised one is now parsed as a **refusal**
  that names the code out loud. That is the safe direction — an invented
  "success" code used to be announced as a placed order — but it means an
  invented code makes your provider unusable rather than subtly wrong. A venue
  order id must never begin with either prefix.

The vocabulary is pinned by `OrderPlacementVocabularyTests`, and the enum has an
exhaustiveness guard: adding an `OrderOutcome` without a sample fails the suite.

Validation rule: **refuse, never resize.** If the venue trades whole shares and
the signal says 9.7, return `ORDER_FAILED` naming the rule — silently truncating
to 9 ships a position the risk sizer did not choose.

### Order status, fills, and the poller

- Map venue status words onto `OrderStatus` **without a guessing fallback** —
  `Expired` is not `Cancelled`, `Replaced` is not `Cancelled`, unknown words map
  to `Unknown` with the raw word in `OrderUpdate.Reason`. The full rules live on
  the `OrderStatus` enum; `OrderStatusContractTests` enforces every member is
  consumed.
- `SupportsOrderEventStreaming` must report the **live push channel state** (see
  [Section 9](#9-live-streaming)); if you also implement `GetOrderStatusAsync`,
  set `SupportsOrderStatusQuery => true` so the poller uses the authoritative
  endpoint.
- `GetPositionsAsync` reports quantities **signed** — a short is negative. Six
  providers once reported shorts positive; every risk calculation and spoken
  summary read them as longs.
- Trading reads (`GetPositionsAsync`, `GetOpenOrdersAsync`, `GetFillsAsync`,
  `GetOrderStatusAsync`) must **throw on failure**, not return empty — an empty
  result reads as "account flat" and has overwritten a live reconciliation
  snapshot before. Let the order service classify the exception.
- `GetFillsAsync` feeds the History tab; without it the fill poller falls back to
  open-list heuristics that can announce a filled order as "cancelled".

Wallet-side capabilities (`IWalletProvider` for read-only deposit addresses and
balances, `IWithdrawalProvider` for moving funds — deliberately separate
interfaces, since a read-only credential must never imply withdrawal ability) are
specified in `docs/WALLET_AND_PORTFOLIO_DESIGN.md`.

When you add a broker, add its wire-payload tests to `BrokerParityTests` (payload
pinned through a captured `FakeHttpMessageHandler`, proven red by sabotage) — the
providers with zero coverage there are exactly where the audit found the
ship-blockers.

---

## 15. Shared Plumbing and House Rules

The SDK ships helpers for the code every provider gets subtly wrong when
hand-rolled. Prefer them:

- **`RestSigning`** — `HmacSha256Hex`, `HmacSha384Hex` (Gemini's shape),
  `HmacSha512Base64` + `Sha256` (both Kraken shapes), `BuildQuery`,
  `QueryPrefixed`. Your venue keeps its signature RECIPE (what gets hashed,
  which header carries it) in its own small auth class; the primitives live
  here, once. The invariant that matters: **the string you sign must be byte-identical to the
  string you send.** Building the signature from one encoding and the body with
  another (`Uri.EscapeDataString` vs `FormUrlEncodedContent`) produces
  "invalid signature" errors only on symbols/values containing spaces or
  brackets — Kraken shipped that for months.
- **`SymbolFormat`** — `SplitBaseQuote` / `Concatenated` / `Slashed` /
  `Underscored` with an 18-quote table. Do not hand-slice pairs: a hardcoded
  3-char quote split turns `BTCUSDT` into `BTCU/SDT` and the venue answers with
  silence, not an error.
- **`ReconnectingWebSocket`** — reconnection with backoff, heartbeat
  (`WithHeartbeatMessage`), text/binary handlers, and an `IsConnected` your
  `SupportsOrderEventStreaming` can read. Hand-rolled sockets have shipped every
  failure mode this class exists to prevent.
- **`RateLimiter`** — token bucket + retry with backoff (see
  [Section 11](#11-rate-limiting)). Nested `ExecuteAsync` calls are safe — the
  semaphore is released before your action runs.
- **`TimestampParser`** — epoch parsing that is culture-safe and
  magnitude-tolerant (seconds vs milliseconds). **`ExchangeTime`** — US-Eastern
  wall-clock conversion for vendors that send naive Eastern strings (FMP stock
  intraday, Tradier timesales windows). Ask the venue for UTC where it lets you
  (TwelveData `&timezone=UTC`) instead of converting at all.
- **`SurfaceError(message, severity, category, symbol)`** — feeds both the typed
  `ProviderErrors` stream and the legacy string `ErrorStream`. Surface every
  failed read; for a blind trader a silent empty chart and a healthy empty chart
  are indistinguishable. Never put credentials in an error message — exception
  messages can carry full request URLs, and TwelveData/FRED keys ride in the
  query string, so log `ex.GetType().Name`, not `ex.Message`, on those paths.

**Culture invariance is a wire-protocol rule.** Every number and date that goes
into a URL, request body, or signature, and every one parsed out of a response,
uses `CultureInfo.InvariantCulture`. Under a Thai locale the year renders as 2569;
under `de-DE`, `double.Parse("50000.5")` yields 500005 — both have shipped as
silently-wrong data. `CultureInvariantScanTests` scans provider sources and fails
the build on ambient-culture parse/format calls, so the guard will find you if
you forget.
