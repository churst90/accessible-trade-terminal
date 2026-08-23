# Accessible Trader — SDK & Plugin Author's Guide

This is the guide to extending Accessible Trader. It explains the whole extension
model end to end: what you can add, why the seams are where they are, and how to
build each kind of plugin — with worked example code. It is the conceptual companion
to the two field references:

- **`PROVIDER_AUTHORING.md`** — the deep, field-by-field reference for *data
  providers*.
- **`PLUGIN_AUTHORING.md`** — the deep reference for *indicator plugins*.

Read this guide first to understand the landscape and pick your extension point; drop
into those two for exhaustive detail when you build a provider or an indicator.
Everything here targets the **`AccessibleTrader.Sdk`** assembly, which is the only
thing a plugin references.

## Contents

1. [The big picture](#1-the-big-picture)
2. [Choose your extension point](#2-choose-your-extension-point)
3. [The plugin lifecycle: discovery, trust, loading](#3-the-plugin-lifecycle-discovery-trust-loading)
4. [Host services available to plugins](#4-host-services-available-to-plugins)
5. [Extension point reference](#5-extension-point-reference)
6. [Authoring for accessibility](#6-authoring-for-accessibility)
7. [Security and trust](#7-security-and-trust)
8. [Tutorial: a minimal analytics provider](#8-tutorial-a-minimal-analytics-provider)
9. [Tutorial: a webhook alert channel](#9-tutorial-a-webhook-alert-channel)
10. [Reference and further reading](#10-reference-and-further-reading)

---

## 1. The big picture

Accessible Trader is built around a small, stable plugin contract so that the data
sources, the maths, the AI backends, the alert sinks, the drawing tools, the themes,
and the automated strategies are all things you can add without touching the host.

Three ideas hold the model together.

**One root, many capabilities.** Every dynamically-loaded plugin implements
`IProviderPlugin`, a tiny interface:

```csharp
public interface IProviderPlugin
{
    string Name { get; }
    string Description { get; }
    ProviderCapabilities Capabilities { get; }
    T? GetCapability<T>() where T : class;
}
```

`GetCapability<T>()` is how the host asks a plugin "do you also do *this*?" A Binance
plugin returns itself for `IMarketDataProvider`, `ITradingProvider`, and
`IOrderBookProvider`; a FRED plugin returns itself only for `IMarketDataProvider`. You
implement the capability interfaces you support and return `this` (or `null`) from
`GetCapability<T>()`. The host never assumes — it asks.

**The host hands you safe tools.** Plugins do not `new` their own `HttpClient`, read
secrets off disk, or hold long-lived API keys. They borrow host-provided bridges
through a single static accessor, `PluginHostServices`: an allow-listed HTTP factory,
a sign-time credential checkout, secure storage, an audit log, and a shared indicator
cache. This keeps a compromised or buggy plugin from reaching hosts it shouldn't or
leaking credentials it shouldn't keep. Section 4 covers each bridge.

**Accessibility is part of the contract, not an afterthought.** Because the terminal
turns market data into *sound and speech*, an indicator plugin does not just compute
numbers — it declares how each output should be voiced and sonified (waveform, pitch
mapping, bell patches, spoken templates). Section 6 is dedicated to this, and it is
the part that most distinguishes authoring here from authoring for a conventional
charting app.

---

## 2. Choose your extension point

Start here. Find what you want to add, and go to the matching section.

| I want to… | Implement | Discovered as | Reference |
|---|---|---|---|
| Add a market-data source (OHLCV) | `BaseMarketDataProvider` | `AccessibleTrader.Plugins.*.dll` in `Plugins/Providers/` | `PROVIDER_AUTHORING.md` |
| Add an analytics source (single-value series) | `BaseMarketDataProvider` with `DataShape = SingleValueLine` | `Plugins/Analytics/` | `PROVIDER_AUTHORING.md` §7 |
| Add live trading to a provider | also implement `ITradingProvider` | same DLL as the provider | §5.2 below |
| Expose order-book depth | also implement `IOrderBookProvider` | same DLL | §5.3 below |
| Add a built-in indicator (ships in a DLL) | `IIndicatorProvider` | `AccessibleTrader.Plugins.*.dll` in `Plugins/Indicators/` | `PLUGIN_AUTHORING.md` |
| Let users bring their own indicator at runtime | `ICustomIndicator` (compiled in-app) | the `.atpkg` / PineScript path, not a DLL | §5.5 below |
| Add an AI analyst backend | `ILLMProvider` | DI registration | §5.6 below |
| Deliver alerts somewhere (webhook, SMS…) | `IAlertChannel` | DI registration (`IEnumerable<IAlertChannel>`) | §5.7 below |
| Add a drawing tool's maths | `IDrawingCalculator` | DI registration per `DrawingType` | §5.8 below |
| Add a chart theme | a `ChartTheme` via `IThemeService` | host theme set | §5.9 below |
| Add an automated strategy (ships in a DLL) | `IStrategyPlugin` → `ITradingStrategy` | `AccessibleTrader.Plugins.Strategy.*.dll` in `Plugins/Strategies/` | §5.10 below |

A handful of SDK interfaces are **internal host services**, not extension points —
you consume them, you do not implement them: `IViewportNavigationService`,
`IViewportRangeCalculator`, `IVolumeStateService`, `IBarDetailService`,
`IPaneAssignmentService`, `IComponentRoleMapper`, `IAutoNarrationService`,
`IAIAnalystService`. They appear in the SDK so the host's own components can be
swapped in tests; ignore them when authoring a plugin.

---

## 3. The plugin lifecycle: discovery, trust, loading

DLL-based plugins (providers, indicators, strategies) follow one lifecycle, run by
`PluginLoaderService` (`AccessibleTrader.Core/Services/PluginLoaderService.cs`).

**1. Discovery.** At startup the loader scans for assemblies named
`AccessibleTrader.Plugins.*.dll` in:

- the application base directory (where the host's own plugins ship),
- its `Plugins/` subtree (scanned recursively), and
- a per-user drop-in folder — `%LOCALAPPDATA%\AccessibleTrader\Plugins\` on Windows,
  the platform equivalent elsewhere — so users can add plugins without rebuilding.

**2. Trust.** Before a DLL is loaded its SHA-256 is computed and checked against the
shipped trust manifest (`plugins_trusted.manifest`, one hex digest per line). With
the default policy (`RequireTrusted = true`) an unlisted DLL is **refused** and a
warning is logged — so a dropped-in or tampered plugin cannot run unless you trust it.
For development you can set `ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS=1` to disable
enforcement. The manifest is regenerated by the build (the `GeneratePluginTrustManifest`
MSBuild target hashes every `AccessibleTrader.Plugins.*.dll` in the output). Section 7
covers the why.

**3. Load and isolate.** Each trusted DLL is loaded into its own
`AssemblyLoadContext` (`PluginLoadContext`), so a plugin's private third-party
dependencies do not collide with the host's or with another plugin's. The SDK and the
framework assemblies are shared from the default context (so the *types you exchange*
with the host are the same types); everything else resolves from the plugin's own
folder.

> **Gotcha — shared output directory.** Although each plugin gets an isolated load
> context, the build copies every plugin and its dependencies into **one** output
> folder. If two plugins need *different versions of the same NuGet package*, only one
> copy of that DLL survives on disk and the isolation cannot pick the other — one
> plugin will fail to load with a `TypeLoadException`. Prefer hand-rolling against an
> API directly over taking a heavy SDK that pins a shared library at a specific
> version (this is exactly why the bundled Binance provider talks to the REST/WebSocket
> API directly instead of using an exchange SDK).

**4. Instantiate and configure.** The loader reflects each DLL for concrete types
implementing the interface it is loading, creates them with
`Activator.CreateInstance` — **so every plugin needs a public parameterless
constructor** — and, for providers, calls `Configure(Dictionary<string,string>)` with
any saved settings. The host then queries capabilities with `GetCapability<T>()`.

**The project.** Every DLL plugin is an ordinary `net10.0` class library that
references the SDK by project (or by the SDK DLL) and copies its dependencies:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- Copy NuGet dependencies next to the plugin DLL so the load context finds them. -->
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\AccessibleTrader.Sdk\AccessibleTrader.Sdk.csproj" />
  </ItemGroup>
</Project>
```

The DLL's file name **must** match `AccessibleTrader.Plugins.*` to be discovered. Put
it under `Plugins/Providers/`, `Plugins/Analytics/`, `Plugins/Indicators/`, or
`Plugins/Strategies/` by kind.

The non-DLL extension points — `ICustomIndicator` (compiled in-app from `.atpkg` or
PineScript), and the DI-registered ones (`ILLMProvider`, `IAlertChannel`,
`IDrawingCalculator`, themes) — do not go through this loader; their wiring is noted
in each section below.

---

## 4. Host services available to plugins

Plugins reach host capabilities through one static accessor,
`PluginHostServices` (`AccessibleTrader.Sdk/Services/PluginHostServices.cs`). Each
property is set once by the host at startup and may be `null` in a unit-test or CLI
context — **always null-check and fall back.**

**HTTP — `CreateHttpClient` / `IPluginHttpClientFactory`.** Do not `new HttpClient()`.
Ask the host for one, declaring the hosts you will talk to:

```csharp
private HttpClient Http => _http ??= PluginHostServices.CreateHttpClient(
    providerId: "MyProvider",
    allowedHosts: new[] { "api.example.com" },   // strict allow-list, no scheme/port
    userAgent: "AccessibleTrader/1.0");
```

The returned client caps response size and rejects any request to a host you did not
list — so a bug (or a malicious package) that interpolates user input into a URL can
never redirect traffic to an attacker's host.

**Credentials — `IApiKeyCheckout`.** Do not store API keys in long-lived fields. Check
them out at each signing site and let them go out of scope:

```csharp
var host = PluginHostServices.ApiKeys;
if (host != null)
{
    var c = await host.CheckoutAsync("MyProvider");   // ApiKeyCheckoutResult
    if (!c.HasCredentials) throw new InvalidOperationException("No active key.");
    Sign(request, c.Key, c.Secret /*, c.Passphrase */);
}
```

`ApiKeyCheckoutResult` is a `readonly record struct (Key, Secret, Passphrase,
HasCredentials)`; treat it as use-and-discard.

**Secure storage — `IPluginSecureStorage`.** For small secrets that must survive a
restart (OAuth refresh tokens, session tokens), use `GetAsync` / `SetAsync` /
`Remove` — backed by the platform keychain/DPAPI, never plaintext on disk.

**Audit — `ISecurityEventLog`.** `Record(SecurityEvent)` to log a security-relevant
event (a fallback, a rejected host, a credential failure) into the host's ring buffer
for operator review.

**Shared indicator maths — `IPluginStrategyIndicatorCache`.** Strategy plugins read
`GetSma` / `GetEma` / `GetRsi` / `GetBollingerBands` through this bridge instead of
recomputing; the host invalidates it per bar so repeated calls in one evaluation share
a result.

The base provider class also gives you `LiveStream`, `ErrorStream`, and
`ConnectionStateStream` observables and the `ScrubCredentials(...)` helper — see §5.1.

---

## 5. Extension point reference

For each, the *what* and *why* in a sentence, then a *how* skeleton. Providers and
indicators have full references elsewhere; the examples here orient you.

### 5.1 Market-data provider — `BaseMarketDataProvider`

**What/why.** A source of price (or analytics) data — historical bars, live ticks,
the symbol list, the supported timeframes. Subclass `BaseMarketDataProvider`; it
implements `IProviderPlugin` and `IMarketDataProvider` and gives you the observable
streams and credential helpers, so you fill in the abstract members.

The abstract surface you must implement:

```csharp
public override string Name => "MyProvider";
public override string Description => "...";
public override List<MarketType> SupportedMarkets => new() { MarketType.Crypto };
public override bool SupportsSymbolSearch => true;
public override bool RequiresApiKey => false;
public override bool IsConfigured => true;
public override bool SupportsLiveUpdates => true;
public override ProviderEnvironment Environment => ProviderEnvironment.Live;
public override int MaxBarsPerRequest => 1000;
public override List<string> NativelySupportedTimeframes => new() { "1m", "1h", "1d" };

public override void Configure(Dictionary<string, string> config) { /* read ApiKey etc. */ }
public override Task EnsureConnectedAsync() { /* open sockets */ }
public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) { /* live stream */ }
public override Task DisconnectAsync() { /* close + ScrubCredentials(...) */ }
public override Task<List<string>> GetSupportedSubTypesAsync(MarketType m) => Task.FromResult(new List<string> { "Spot" });
public override Task<List<string>> GetAvailableSymbolsAsync(MarketType m, string subType = "Spot") { /* ... */ }
public override Task<List<string>> GetSupportedTimeframesAsync() { /* ... */ }
public override Task<(List<Ohlcv>, List<(long, double)>)> FetchOhlcvAsync(MarketDataRequest request) { /* ... */ }
public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) { /* ... */ }
```

Push live bars by calling `_liveStream.OnNext(bar)`; surface a recoverable problem
with `SurfaceError("message", severity, category, symbol)` (which feeds both the typed
`ProviderErrors` stream and the legacy string `ErrorStream`) — always surface a failed
fetch rather than returning an empty result silently, so a blind trader hears it;
report connection changes through `_connectionStateStream`.

**Shared plumbing (use these instead of hand-rolling).** `RestSigning` provides
`HmacSha256Hex`, `BuildQuery`, and `QueryPrefixed`; `SymbolFormat` provides
`SplitBaseQuote` / `Concatenated` / `Slashed` / `Underscored` for exchange-specific
pair shapes; `ReconnectingWebSocket` handles reconnection, heartbeat (`WithHeartbeatMessage`
for a custom keepalive), text (`OnMessage`) and binary (`OnBinary`) frames. Prefer a
direct HTTP/WebSocket integration over a heavy exchange SDK — a shared SDK pins common
libraries and clashes in the flattened plugin output dir (a CI guard fails the build if
two plugins resolve the same assembly at different versions). See `PROVIDER_AUTHORING.md`
for the full walkthrough,
including analytics providers (set `DataShape => ProviderDataShape.SingleValueLine`
and use `GetSymbolRenderHints` to declare range, reference levels, and speech). A
complete worked analytics provider is in §8 below.

### 5.2 Trading provider — `ITradingProvider`

**What/why.** Adds order execution to a provider. Implement it on the same class and
return `this` from `GetCapability<ITradingProvider>()`.

```csharp
public bool IsConnected => /* authenticated */;
public bool SupportsMarginTrading => true;
public bool SupportsFuturesTrading => true;
public double MaxLeverage => 100;
public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdates;   // a Subject<OrderUpdate>

public Task<List<Balance>> GetBalancesAsync() { /* ... */ }
public Task<List<Position>> GetPositionsAsync() { /* ... */ }
public Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null) { /* ... */ }
public Task<string> PlaceOrderAsync(TradeSignal s) { /* returns order id or "ORDER_FAILED:..." */ }
public Task<bool> CancelOrderAsync(string orderId, string symbol) { /* ... */ }
public Task<double> SetLeverageAsync(string symbol, double leverage) { /* ... */ }
// Optional (default returns empty): recent fills for the History tab.
public Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50) { /* ... */ }
```

`TradeSignal` carries everything the order panel can express — type, price, stop/take-
profit, leverage, margin type, **trigger price**, **trailing stop / trailing
take-profit** (with `TrailMode` and an activation price), time-in-force, reduce-only,
post-only, and position side. Honour the fields your venue supports and ignore the
rest. Critically, **push every order event onto `OrderUpdateStream`** — fills,
partials, cancels, stop/TP triggers — because that stream is what drives the spoken
"Order filled… / Stop loss hit… / Trailing take profit hit…" announcements. Set
`StopTriggered` / `TakeProfitTriggered`, `Trailing`, and `RealizedPnL` on the
`OrderUpdate` so the speech layer can say the right thing.

Map your venue's status vocabulary onto `OrderStatus` **without a guessing fallback**:
`New` for accepted-and-working (logged, not announced), `Expired` when time-in-force
ran out (not `Cancelled` — nobody asked; not `Rejected` — the venue accepted it),
`Replaced` when the order was modified and is still live under a new id (never
`Cancelled`, which tells the trader they are flat while the order rests), and
`Unknown` for anything you don't recognise, with the raw venue word in
`OrderUpdate.Reason` so the log names it. A partially-filled-then-terminated order
reports its terminal state with `FilledQuantity` carrying the executed part — the
announcement speaks the fill. The full rules live on the `OrderStatus` enum doc
comment, and `OrderStatusContractTests` enforces that every member is consumed.

Advertise your capabilities
(`ProviderCapabilities.Leverage | TrailingStop | Brackets | ...`) so the order panel
shows only the controls you support.

### 5.3 Order-book provider — `IOrderBookProvider`

**What/why.** Exposes L2 depth for the order-book panel and heatmap.

```csharp
public Task<OrderBookSnapshot> GetOrderBookAsync(string symbol, int depth = 100) { /* ... */ }
public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol) { /* live deltas */ }
```

Return `OrderBookSnapshot(Symbol, Bids, Asks, Sequence, Timestamp)` and push
`OrderBookUpdate`s of the same shape; both use `OrderBookEntry(double Price, double
Quantity)`. Advertise `ProviderCapabilities.L2`.

### 5.4 Indicator provider — `IIndicatorProvider`

**What/why.** A maths engine that ships one or more indicators in a DLL. Optimised for
zero-allocation streaming. Full reference: `PLUGIN_AUTHORING.md`.

```csharp
public string Name => "MyIndicators";
public List<IndicatorMetadata> GetIndicators() => new() { /* declare each indicator */ };
public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
    Dictionary<string, object> p, IIndicatorResultBuffer buffer) { /* full recompute */ }
public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
    Dictionary<string, object> p, IIndicatorResultBuffer buffer) { /* last bar only */ }
public int GetStabilityWindow(string code, Dictionary<string, object> p) => 200;
public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
    IReadOnlyDictionary<string, double[]> results, int i, Dictionary<string, object> p) => "...";
```

Each `IndicatorMetadata` declares the indicator's `Code` (the method key), its
components, its default pane, parameters, and — importantly — the *audio and speech*
defaults for each component (see §6). Write results into the supplied
`IIndicatorResultBuffer` via `SetValue` / `GetComponentSpan`. `Calculate` runs on a
full load; `UpdateLast` runs per live tick for the last bar (unless
`RequiresFullRecalcOnTick`).

### 5.5 Custom (Roslyn) indicator — `ICustomIndicator`

**What/why.** The contract for an indicator a *user* writes and the app compiles at
runtime (the Alt+Comma scripts panel, or a transpiled PineScript). Simpler than
`IIndicatorProvider` — no metadata, one indicator, returns plain arrays.

```csharp
public class MyCustomIndicator : ICustomIndicator
{
    public string Id => "my_custom";
    public string DisplayName => "My Custom Indicator";
    public string[] ComponentNames => new[] { "Value" };
    public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
    public Dictionary<string, double> DefaultParameters => new() { ["period"] = 14 };

    public double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> p)
    {
        int period = (int)p["period"];
        var outv = new double[data.Length];
        // ... fill outv ...
        return new[] { outv };   // one array per component name
    }
}
```

These run **inside the sandbox** (§7), not as a trusted DLL. You package one as an
`.atpkg` (a zip of `manifest.json` + `source.cs`) and import it through the scripts
panel, where it is compiled in an isolated worker process with no file, network, or
reflection access. Custom-script compilation is disabled on iOS (no process sandbox).

### 5.6 LLM provider — `ILLMProvider`

**What/why.** A backend for the AI Analyst. The host picks among configured providers
by id (Claude, OpenAI, Ollama by default) and supplies the API key from the key
manager.

```csharp
public class MyLlmProvider : ILLMProvider
{
    public string ProviderId => "MyLlm";
    public string DisplayName => "My LLM";

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage,
        string? imageBase64, string apiKey, CancellationToken ct = default)
    {
        // POST to your endpoint with systemPrompt + userMessage (+ imageBase64 if vision),
        // authenticate with apiKey, return the model's text reply.
    }
}
```

Return plain text written for text-to-speech (no markdown tables). If `imageBase64`
is non-null and your model has vision, you get a PNG of the chart to reason over.

### 5.7 Alert channel — `IAlertChannel`

**What/why.** Delivers a fired alert somewhere external — email and Telegram ship in
the box; add SMS, a webhook, a desktop notifier. Registered as
`IEnumerable<IAlertChannel>`; the orchestrator fans every fired alert out to all
channels that report `IsConfigured`.

```csharp
public class WebhookAlertChannel : IAlertChannel
{
    public string Id => "webhook";
    public string DisplayName => "Webhook";
    public bool IsConfigured => !string.IsNullOrEmpty(_url);

    public async Task SendAsync(AlertFired alert, CancellationToken ct = default)
    {
        // POST alert.SpeechText (and alert.Definition / TriggeringValue) to _url.
    }
}
```

A full webhook channel is the second tutorial (§9). Note: in-app speech and earcon
delivery are handled by the host from the alert's own `Delivery` setting — your
channel is for *external* delivery.

### 5.8 Drawing calculator — `IDrawingCalculator`

**What/why.** Computes the data arrays for one drawing-tool type (a Fibonacci fan, a
pitchfork). One implementation per `DrawingType`, dispatched by a registry.

```csharp
public class MyToolCalculator : IDrawingCalculator
{
    public DrawingType DrawingType => DrawingType.MyTool;
    public Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
    {
        // From the drawing's anchors, compute one named array per line the tool draws.
        return new() { ["line1"] = /* ... */, ["line2"] = /* ... */ };
    }
}
```

Each returned array becomes a series the cursor can cross and announce, so a new
drawing tool is audible the moment it is placed.

### 5.9 Theme — `ChartTheme` / `IThemeService`

**What/why.** A theme is a `ChartTheme` record of SkiaSharp colours and font sizes —
candle bodies, grid, the 12-colour indicator palette, profile colours, drawing
colours. Themes matter for low-vision (not blind) users and sighted collaborators;
the built-ins include high-contrast dark and light and a Braille-oriented palette
(`ThemeType.HighContrastDark | HighContrastLight | SoftDark | Solarized | Braille`).
Construct a `ChartTheme { ThemeType = …, Background = …, … }` with all required
members and register it with the theme service; `SetTheme(ThemeType)` switches and
raises `ThemeChanged`.

### 5.10 Strategy plugin — `IStrategyPlugin` / `ITradingStrategy`

**What/why.** A DLL of one or more automated strategies (the in-app Strategy Composer
is the no-code path; this is the code path). `IStrategyPlugin.GetStrategies()` returns
template instances; the host parameterises and runs them.

```csharp
public class MyStrategyPlugin : IStrategyPlugin
{
    public string Name => "My Strategies";
    public string Description => "...";
    public IReadOnlyList<ITradingStrategy> GetStrategies() => new[] { new MomentumStrategy() };
}

public class MomentumStrategy : ITradingStrategy
{
    public string Id => "momentum_v1";          // stable id — workspaces rehydrate by it
    public string Name => "Momentum v1";
    public string Description => "...";
    public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
    public IReadOnlyList<StrategyParameter> Parameters => new[] {
        new StrategyParameter("period", "Lookback", StrategyParameterType.Integer, 20)
    };

    public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state,
        IDictionary<string, object> parameters) { /* warm up */ }

    public StrategySignal? OnBar(Ohlcv bar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
        => /* null, or a StrategySignal with side, stop, TP ladder, rationale, confidence */;

    public void OnOrderFilled(OrderUpdate fill) { }
    public void OnStop() { }
    public StrategyMetrics GetMetrics() => /* totals for the backtest panel */;
}
```

`OnBar` returns `null` most bars and a `StrategySignal` when your rules line up; the
signal's `Rationale` is spoken and journalled ("Long setup, score 0.85. Stop …, first
target … (R:R …)."). Remember the project-wide framing: **strategies are
experimental** — surface that, and never present a backtest as a promise.

---

## 6. Authoring for accessibility

This is the section that makes a plugin *belong* here rather than merely run. The
terminal renders data as a soundscape and speech, so a good plugin declares how its
output is heard.

For an **indicator**, the per-component metadata on `IndicatorComponentMetadata`
carries audio and speech defaults alongside the visual ones. The ones to think about:

- **`DefaultWaveform` / `DefaultAboveWaveform` / `DefaultBelowWaveform`** — the timbre
  of the component's continuous tone, and how it changes either side of a baseline
  (so the listener hears which side of zero an oscillator is on).
- **`DefaultPitchMapping` (`Value` / `Direction` / `Price` …)** and
  **`DefaultAmplitudeMapping`** — whether pitch tracks the value, the direction, or
  price, and what drives loudness.
- **`DefaultSoundPatchId` / `DefaultPlaybackLayer`** — which bell rings on this
  component's signal events, and in which depth layer (background / mid / foreground).
- **`SpeechTemplate` / `DefaultSignalSpeechTemplate` / `UsesGradientSpeech`** — how a
  value or a signal is *spoken*. Templates can interpolate the value and a zone word.
- **`DisplayType` (`Oscillator`, `Histogram`, `Dot`, `Cloud`, …)** and **`Role`**
  (`UpperBand`, `Signal`, `Histogram`, …) — these drive the host's default
  sonification profile via `ISonificationProfileProvider`, so even if you set nothing,
  a sensible sound is chosen for the *kind* of component you declared.

You can also override `GetComponentSpeech(...)` on `IIndicatorProvider` to compute a
bespoke spoken phrase per bar, and return `LevelDescriptor`s from `GetDefaultLevels`
with `PlayEarcon = true` so a reference level *clicks* as the cursor crosses it.

For a **provider** of analytics data, `GetSymbolRenderHints` lets you declare a
sensible range, reference levels, an oscillator display, and a `SpeechTemplate` for a
metric — so a Fear & Greed reading announces "Fear and Greed 28, fear" rather than a
bare number.

The test of a well-authored plugin here is simple: **could a blind user understand
your indicator with the screen off?** If the answer needs a glance at the chart, the
audio metadata is not done.

---

## 7. Security and trust

The terminal runs third-party code and talks to funded exchange accounts, so the SDK
is deliberately defensive. As an author, working *with* these mechanisms is easier
than fighting them.

- **Trust manifest.** DLL plugins are SHA-256-checked against a shipped manifest and
  refused if unlisted (default `RequireTrusted = true`). Your own builds are trusted
  automatically by the build's manifest target; a stranger's dropped-in DLL is not,
  unless the user opts in with `ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS=1`.
- **Load isolation.** Each plugin loads in its own `AssemblyLoadContext` (mind the
  shared-output gotcha in §3).
- **Allow-listed HTTP.** The host's `HttpClient` rejects any host you did not declare,
  with a size cap — so a URL-injection bug cannot exfiltrate to an arbitrary host.
- **Credential checkout, not storage.** Keys are fetched per-signature and dropped,
  shrinking the in-memory exposure window; persist only via `IPluginSecureStorage`
  (keychain/DPAPI), never plaintext.
- **User scripts are sandboxed harder.** `ICustomIndicator` code compiled in-app runs
  in an out-of-process worker (Windows AppContainer, macOS `sandbox-exec`, Android
  `isolatedProcess`) with a memory and wall-clock quota and no I/O, network, or
  reflection — and is disabled entirely on iOS, which offers no such sandbox. The full
  design is in `SANDBOX_DESIGN.md`.

When something defensive fires (a fallback, a rejected host, a killed worker), the
host records a `SecurityEvent`; if your plugin makes a security-relevant decision,
record one too via `PluginHostServices.SecurityEvents`.

---

## 8. Tutorial: a minimal analytics provider

A complete, single-file analytics provider that serves one made-up daily metric as a
single-value line, with accessibility hints. It needs no API key, so you can build and
load it immediately.

**`Plugins/Analytics/AccessibleTrader.Plugins.Demo/AccessibleTrader.Plugins.Demo.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\AccessibleTrader.Sdk\AccessibleTrader.Sdk.csproj" />
  </ItemGroup>
</Project>
```

**`DemoMetricProvider.cs`**

```csharp
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Plugins.Demo;

public sealed class DemoMetricProvider : BaseMarketDataProvider, IProviderPlugin
{
    public override string Name => "Demo Metric";
    public override string Description => "A demo single-value analytics series (0–100).";
    public override List<MarketType> SupportedMarkets => new() { MarketType.OnChain };
    public override bool SupportsSymbolSearch => false;
    public override bool RequiresApiKey => false;
    public override bool IsConfigured => true;
    public override bool SupportsLiveUpdates => false;
    public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
    public override int MaxBarsPerRequest => 1000;
    public override List<string> NativelySupportedTimeframes => new() { "1d" };

    // Analytics, not candles: render as a single line.
    public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

    public override void Configure(Dictionary<string, string> config) { }
    public override Task EnsureConnectedAsync() => Task.CompletedTask;
    public override Task SetSubscriptionAsync(string m, string s, string t) => Task.CompletedTask;
    public override Task DisconnectAsync() => Task.CompletedTask;

    public override Task<List<string>> GetSupportedSubTypesAsync(MarketType m)
        => Task.FromResult(new List<string> { "Spot" });
    public override Task<List<string>> GetAvailableSymbolsAsync(MarketType m, string subType = "Spot")
        => Task.FromResult(new List<string> { "DEMO_INDEX" });
    public override Task<List<string>> GetSupportedTimeframesAsync()
        => Task.FromResult(new List<string> { "1d" });

    public override Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)>
        FetchOhlcvAsync(MarketDataRequest request)
    {
        // Synthesize a deterministic 0–100 daily series. A real provider would fetch
        // this over HTTP via PluginHostServices.CreateHttpClient(...).
        int n = Math.Min(request.Limit, MaxBarsPerRequest);
        var start = DateTime.UtcNow.Date.AddDays(-n);
        var bars = new List<Ohlcv>(n);
        for (int i = 0; i < n; i++)
        {
            double v = 50 + 40 * Math.Sin(i / 9.0);      // a wave between 10 and 90
            var day = start.AddDays(i);
            // For a single-value line the host reads Close; mirror it across OHLC.
            bars.Add(new Ohlcv(day, v, v, v, v, 0));
        }
        var vol = bars.Select(b => (new DateTimeOffset(b.Date, TimeSpan.Zero).ToUnixTimeMilliseconds(), 0.0)).ToList();
        return Task.FromResult((bars, vol));
    }

    public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)>
        GetOrderBookAsync(string symbol, int limit = 10)
        => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

    // Accessibility: tell the host how to scale, mark, and SPEAK the metric.
    public override string GetSymbolDisplayName(string symbol) => "Demo Index";

    public override SymbolRenderHints? GetSymbolRenderHints(string symbol) => new SymbolRenderHints(
        RangeMin: 0,
        RangeMax: 100,
        ReferenceLevels: new List<LevelDescriptor>
        {
            new("Low", 25, "#44BB44", DashStyle.Dash, PlayEarcon: true),
            new("High", 75, "#FF4444", DashStyle.Dash, PlayEarcon: true),
        },
        DisplayType: ComponentDisplayType.Oscillator,
        SpeechTemplate: "Demo index {value}, {zone}");
}
```

Build it into `Plugins/Analytics/.../bin`, let the trust manifest pick it up (or set
`ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS=1` while developing), restart, and select
**OnChain → Demo Metric → DEMO_INDEX** in the toolbar. The line scales 0–100, clicks
as the cursor crosses 25 and 75, and announces "Demo index 82, high."

---

## 9. Tutorial: a webhook alert channel

A complete alert channel that POSTs every fired alert to a URL. Alert channels are
registered with the host's DI rather than discovered as DLLs, so this lives in (or is
referenced by) the host and registered alongside the email and Telegram channels.

```csharp
using System.Net.Http.Json;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Services;

public sealed class WebhookAlertChannel : IAlertChannel
{
    private readonly string? _url;
    private HttpClient? _http;

    public WebhookAlertChannel(string? url) => _url = url;   // wire the URL from settings

    public string Id => "webhook";
    public string DisplayName => "Webhook";
    public bool IsConfigured => Uri.TryCreate(_url, UriKind.Absolute, out _);

    public async Task SendAsync(AlertFired alert, CancellationToken ct = default)
    {
        if (!IsConfigured) return;
        var host = new Uri(_url!).Host;
        _http ??= PluginHostServices.CreateHttpClient("webhook-alert", new[] { host });

        var payload = new
        {
            name = alert.Definition.Name,
            text = alert.SpeechText,
            value = alert.TriggeringValue,
            previous = alert.PreviousValue,
            condition = alert.Definition.Condition.ToString(),
            target = alert.Definition.Target.ToString(),
        };
        using var resp = await _http.PostAsJsonAsync(_url, payload, ct);
        // Swallow non-2xx quietly, or PluginHostServices.SecurityEvents?.Record(...) it.
    }
}
```

```csharp
// In the host's ServiceCollectionExtensions, register it among the channels:
services.AddSingleton<IAlertChannel>(_ =>
    new WebhookAlertChannel(settings.GetSetting("alerts.webhook.url")?.ToString()));
```

Now when any alert fires, the orchestrator calls every configured channel — your
webhook receives `{ name, text, value, … }`, while the user simultaneously hears the
in-app speech/earcon governed by the alert's own `Delivery` setting. Note the
`CreateHttpClient` call pins the allow-list to exactly the webhook's host.

---

## 10. Reference and further reading

- **`PROVIDER_AUTHORING.md`** — the full data-provider reference: metadata, live
  streaming, analytics shape, render hints, API keys, rate limiting, disposal, and a
  quick-start example.
- **`PLUGIN_AUTHORING.md`** — the full indicator reference:
  `IIndicatorProvider`, `IndicatorMetadata`, the complete `IndicatorComponentMetadata`
  field reference, writing `Calculate`/`UpdateLast`, levels, and component speech.
- **`SANDBOX_DESIGN.md`** — the threat model and OS-sandbox design for user scripts.
- **`USER_MANUAL.md`** — how the features your plugin extends are used, and what they
  sound like, from the trader's seat.
- **The SDK source** — `AccessibleTrader.Sdk/` is small and readable; the interface
  files named throughout this guide are the authoritative contracts. The shipped
  plugins under `Plugins/` are working examples of every provider, indicator, and
  strategy pattern.

When in doubt, start from the extension-point table in §2, copy the nearest shipped
plugin, and lean on the two field references for depth. And whatever you build, make
it *heard* — that is the whole point of this terminal.
