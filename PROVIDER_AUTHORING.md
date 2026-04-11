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
- Default `GetCapability<T>()` returning `this`

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

    var volumes = bars
        .Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume))
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

public override async Task<(List<Ohlcv>, List<(long, double)>)> FetchOhlcvAsync(MarketDataRequest request)
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

        public override Task<(List<OrderBookEntry>, List<OrderBookEntry>)>
            GetOrderBookAsync(string s, int l = 10)
            => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        public override async Task<(List<Ohlcv>, List<(long, double)>)>
            FetchOhlcvAsync(MarketDataRequest request)
        {
            var json = await _http.GetStringAsync("https://api.example.com/metric")
                .ConfigureAwait(false);
            var arr = JArray.Parse(json);
            var bars = new List<Ohlcv>();

            foreach (var pt in arr)
            {
                var date = pt["date"]!.Value<DateTime>();
                var val  = pt["value"]!.Value<double>();
                bars.Add(new Ohlcv(date, val, val, val, val, 0));
            }

            var vols = bars.Select(b =>
                (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
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
