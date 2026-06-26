# Demo redesign handoff — "real interface, scaled down"

**Goal:** replace the bespoke `Demo.razor` mini-page with the **real app shell**
(`MainLayout`), running under a central **`DemoPolicy`** that restricts it. A demo
visitor gets the actual product — dropdowns, indicator menu, sonification,
keyboard nav — limited to a curated set, so it feels like the real thing and
drives the download. Opening it full-screen *is* the real WebHost minus the gated
features.

This package contains:
- `DemoPolicy.cs` — a **drop-in** new file (verbatim) for `AccessibleTrader.Core/Services/`.
- This README — exact anchors + step-by-step wiring for everything else.

Everything below was checked against the current `main` (commit `ad4c1170`). File
paths, class names, member names, and indicator codes are real. Where you must
confirm a signature before editing, it says so.

---

## The curated demo (decisions already made with Cody)

| Dimension | In the demo | Held back (download only) |
|---|---|---|
| **Providers** | Bitstamp (live crypto, free), Twelve Data (stocks/forex/index, free key) | all 24 other providers |
| **Symbols** | BTC/USD, ETH/USD, AAPL, TSLA, NVDA, SPY, EUR/USD | full searchable universe |
| **Timeframes** | **4h, 1d** (as buttons) | every other timeframe + custom composer |
| **Indicators** | EMA, SMA, Bollinger, RSI, MACD, VWAP, Volume Profile (VPVR) | the full catalog (Cipher/Skender/Hurst/Ichimoku/on-chain/etc.) |
| **On (the stars)** | chart, sonification, keyboard nav, zoom, narration, drawing tools, sound designer, help | — |
| **Off** | trading, order book, strategies, alerts, custom scripts, AI Analyst, API-keys modal, workspace save/load, settings persistence, symbol search | these are the download incentives |

`DemoPolicy.cs` already encodes all of this. Tweak the lists there, not in the UI.

---

## Indicator codes (verified)

These are the `Code` values the menu uses — the demo whitelist keys off them:

| Indicator | Code | Defined in |
|---|---|---|
| EMA | `Ema` | `Services/Indicators/SkenderTrendProvider.cs` |
| SMA | `Sma` | `SkenderTrendProvider.cs` |
| Bollinger Bands | `Bb` | `SkenderBandProvider.cs` |
| RSI | `Rsi` | `SkenderBoundedOscillatorProvider.cs` |
| MACD | `Macd` | `SkenderZeroCrossProvider.cs` |
| VWAP | `Vwap` | `SkenderTrendProvider.cs` |
| Volume Profile (visible range) | `VPVR` | `ProfileIndicatorProvider.cs` |

(If you'd rather use anchored VWAP, the code is `AVWAP` in `CoreIndicatorProvider.cs`.)

---

## Step 1 — add `DemoPolicy` and register it

1. Copy `DemoPolicy.cs` into `AccessibleTrader.Core/Services/`.
2. Register a singleton **in every head**, so components can always `@inject` it:
   - **WebHost** (`AccessibleTrader.WebHost/Program.cs`, next to the existing
     `AddSingleton(new WebHostDemoMode(demoMode))` at ~line 33):
     ```csharp
     builder.Services.AddSingleton(new DemoPolicy(demoMode));
     ```
   - **MAUI / desktop heads** (wherever their services are composed): register
     `new DemoPolicy(false)` so the full app resolves a no-op policy.
   - Tests: the harness can register `new DemoPolicy(false)`.

`DemoPolicy` lives in `Core`, which `BlazorClient.Components` already references, so
both Razor components and Core services can depend on it.

---

## Step 2 — gate the data at the orchestrator (the single best choke point)

`AccessibleTrader.Core/Services/MarketOrchestrator.cs` is where provider/symbol/
timeframe lists are built. Filtering here means **every** piece of UI that reads
`Available*` is restricted automatically — you barely touch the Toolbar.

Inject `DemoPolicy` into `MarketOrchestrator` (add a ctor param; it's already a DI
service). Then:

- **`RefreshProvidersAsync()`** — after `_availableProviders` is populated:
  ```csharp
  _availableProviders = _demo.FilterProviders(_availableProviders).ToList();
  ```
- **`RefreshSymbolsAsync()`** — after `_availableSymbols` is populated:
  ```csharp
  _availableSymbols = _demo.FilterSymbols(SelectedProvider, _availableSymbols).ToList();
  ```
- **Timeframes** — wherever `_availableTimeframes` is set, in demo force it:
  ```csharp
  _availableTimeframes = _demo.IsDemo
      ? _demo.AllowedTimeframes.ToList()
      : /* existing full list */;
  ```
- **Guards** — in the `SelectedTimeframe` / `SelectedSymbol` / `SelectedProvider`
  setters (or `LoadChartAsync`), reject out-of-policy values in demo so a crafted
  request can't escape the whitelist:
  ```csharp
  if (_demo.IsDemo && !_demo.IsTimeframeAllowed(value)) return; // or clamp to "1d"
  ```

This is the security boundary — don't rely on hidden buttons alone; enforce in the
orchestrator so a hand-built SignalR/URL request still can't pull a non-whitelisted
symbol or timeframe.

---

## Step 3 — point the demo at the real shell (`MainLayout`)

Today: `Demo.razor` (`@page "/demo"`) + `DemoLayout`. The real UI is `MainLayout`
(rendered for `Home.razor` `@page "/"`), which contains `<Toolbar/>`, `<ChartArea/>`,
`<IndicatorBar/>` and the modals.

Recommended: **retire `Demo.razor` + `DemoLayout`** and let the demo use `/` →
`MainLayout` like the real app. Move the demo's one useful job — opening on
Bitstamp · BTC/USD · 1d — into a startup hook:

- In `MainLayout.OnInitializedAsync` (or a startup service), when `DemoPolicy.IsDemo`,
  seed the default selection using the existing orchestrator calls already proven in
  `Demo.razor.ForceLoadDemoChartAsync()`:
  ```csharp
  if (_demo.IsDemo)
  {
      await MarketOrchestrator.RefreshPipelineAsync();
      MarketOrchestrator.SelectedMarket = _demo.DefaultMarket;     // "Crypto"
      await MarketOrchestrator.RefreshProvidersAsync();
      MarketOrchestrator.SelectedProvider = _demo.DefaultProvider; // "Bitstamp"
      await MarketOrchestrator.RefreshSymbolsAsync();
      MarketOrchestrator.SelectedSymbol = /* PickBtcUsd(AvailableSymbols) */;
      MarketOrchestrator.SelectedTimeframe = _demo.DefaultTimeframe;
      await MarketOrchestrator.LoadChartAsync();
  }
  ```
  (`PickBtcUsd` helper already exists in `Demo.razor` — keep it.)

If you'd rather not delete `Demo.razor`, the alternative is to make it `@layout
MainLayout`-equivalent, but that re-creates the shell — deleting is cleaner.

> Website side (Cody/Claude-on-server will handle): the `/preview` iframe and the
> "open full screen" link currently point at `/app/demo`; they become `/app/`
> (root) once the demo is the real shell. Don't worry about this in the app repo.

---

## Step 4 — filter the indicator menu

`AddIndicatorModal.razor` (and/or `IndicatorBar.razor`) lists
`IndicatorService.GetAvailableIndicators()`. Filter the displayed list in demo:

```razor
@inject DemoPolicy Demo
...
@foreach (var ind in IndicatorService.GetAvailableIndicators()
                        .Where(i => Demo.IsIndicatorAllowed(i.Code)))
{
    ... existing row ...
}
```

Belt-and-braces: in `SeriesManagementService.RegisterSeriesFromMetadata` (or
`IndicatorService`), no-op if `!Demo.IsIndicatorAllowed(meta.Code)` so a stray
dispatch can't add a gated indicator.

---

## Step 5 — hide the gated surfaces in `MainLayout` + `Toolbar`

`AccessibleTrader.BlazorClient.Components/Layout/MainLayout.razor` renders these
modals (around lines 62–79). Wrap the gated ones and **also hide the Toolbar
buttons that open them** (otherwise you get dead buttons):

```razor
@inject DemoPolicy Demo
...
@if (Demo.AllowTrading)        { <TradingDashboardModal /> }
@if (Demo.AllowOrderBook)      { <OrderBookModal /> }
@if (Demo.AllowApiKeysModal)   { <ApiKeysModal /> }
@if (Demo.AllowStrategies)     { <StrategyModal /> }
@if (Demo.AllowAlerts)         { <AlertsModal /> }
@if (Demo.AllowCustomScripts)  { <CustomScriptsModal /> }
@if (Demo.AllowAiAnalyst)      { <AIAnalystModal /> }
@if (Demo.AllowWorkspaceSaveLoad) { <SaveWorkspaceModal /> <LoadWorkspaceModal /> }
```
Keep always-on: `Toolbar`, `ChartArea`, `IndicatorBar`, `AddIndicatorModal`,
`HelpModal`, `SettingsModal`, `ObjectTreeModal`, `PropertiesModal`, `JournalModal`,
and (per the table) `DrawingToolsModal`, `SoundDesignerModal`.

In `Toolbar.razor`:
- Wrap the trading/strategy/alerts/AI/api-keys/workspace buttons in the matching
  `@if (Demo.Allow…)`.
- **Timeframe control** (currently a custom multiplier+unit composer plus quick-pick
  buttons from `AvailableTimeframes`, ~lines 167–198): in demo, hide the composer and
  render only the `AllowedTimeframes` buttons. Since Step 2 already forces
  `AvailableTimeframes == ["4h","1d"]`, the quick-pick row alone is correct — just
  wrap the composer inputs in `@if (!Demo.IsDemo)`.
- **Symbol search**: gate behind `@if (Demo.AllowSymbolSearch)`; the whitelisted
  dropdown stays.

---

## Step 6 — settings persistence off in demo

`SettingsManager` reads/writes `settings.json`. In demo, make the save path a no-op
when `!Demo.AllowSettingsPersist`, so visitors don't clobber shared server state.
(Reads/defaults are fine.)

---

## Step 7 — Twelve Data key (server-side, NOT committed)

`TwelveDataProvider` has `RequiresApiKey = true` and reads config key `ApiKey`. Feed
it through the existing `IApiKeyService`. Seed it **from an environment variable** at
demo startup so the key never lands in source control.

In `Program.cs`, inside the existing `if (demoMode) { … }` block, after the app is
built:
```csharp
var tdKey = Environment.GetEnvironmentVariable("DEMO_TWELVEDATA_APIKEY");
if (!string.IsNullOrWhiteSpace(tdKey))
{
    var apiKeys = app.Services.GetRequiredService<IApiKeyService>();
    await apiKeys.SaveKeyAsync(new ApiKeyConfig(
        Provider:   "TwelveData",
        Nickname:   "demo",
        ApiKey:     tdKey,
        ApiSecret:  "",
        Passphrase: "",
        MarketType: "Stock",
        Environment:"Live",
        IsActive:   true));
}
```
`ApiKeyConfig` is `AccessibleTrader.Core/Services/IApiKeyService.cs` (record:
`Provider, Nickname, ApiKey, ApiSecret, Passphrase="", MarketType="Spot",
Environment="Paper", IsActive=false`). Confirm `SecureStorage` works headless on the
WebHost; if `ISecureStorageService` is a no-op on Linux, instead `Configure()` the
provider instance directly with `new Dictionary<string,string>{["ApiKey"]=tdKey}`.

**Quota:** free tier ≈ 800 req/day, 8/min. With the whitelist + 2 timeframes +
server-side caching shared across visitors, that's plenty. Make sure the
historical-data cache path covers Twelve Data REST pulls (it already caches OHLCV);
crypto stays live via Bitstamp's WebSocket. Free-tier stock data is delayed/EOD and
the live WebSocket may be paid — fine for a demo (poll REST + cache; don't depend on
TD streaming).

The systemd unit on the server will set the env var (server side handles this):
```ini
# /etc/systemd/system/accessible-trader-demo.service  [Service]
Environment=DEMO_TWELVEDATA_APIKEY=xxxxxxxx
```

---

## Build & deploy (server side — for reference)

The demo is published and run on the VPS, not from the desktop builds. Two gotchas
that already bit us:

1. **Always publish with `-p:OutputType=Exe`.** `WebHost.csproj` sets
   `OutputType=WinExe` for Release (to hide the Windows console). On a Release
   *publish* that drops `_framework/blazor.web.js` from the static-assets manifest →
   the Blazor circuit never boots → "no data loaded" with no server error. `-p:OutputType=Exe`
   overrides it (WinExe is meaningless headless).
   ```
   dotnet publish AccessibleTrader.WebHost/AccessibleTrader.WebHost.csproj \
       -c Release -p:OutputType=Exe -o <stage>
   ```
2. **Regenerate `plugins_trusted.manifest`** after publish (SHA-256 of each
   `AccessibleTrader.Plugins.*.dll`), or `RequireTrusted` refuses every plugin → no
   data.

Smoke test after deploy:
```
curl -sf https://trade.codyhurst.com/app/_framework/blazor.web.js   # must be 200
curl -s -X POST https://trade.codyhurst.com/app/_blazor/negotiate?negotiateVersion=1  # must return a connectionToken
```

---

## Open questions for you (Cody)

1. **Drawing tools & Sound Designer in the demo?** I left both ON (a richer taste).
   Flip `AllowDrawingTools` / `AllowSoundDesigner` to `!IsDemo` in `DemoPolicy.cs`
   if you'd rather hold them back.
2. **Settings modal:** keep it visible (non-persisting) so people can try sonification
   options, or hide it? Currently visible, just non-persistent.
3. **Anchored VWAP vs rolling VWAP** in the indicator set — I used rolling (`Vwap`);
   say the word for `AVWAP` instead.

That's the whole job. `DemoPolicy.cs` is the spine; Steps 2, 4, 5 are the wiring;
Step 7 lights up stocks once your key is in the env var.
