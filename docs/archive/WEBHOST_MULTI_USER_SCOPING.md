# WebHost multi-user scoping (per-circuit state)

**Status:** in progress. Phase 0 complete (staging instance stood up + bug reproduced).
**Scope:** `AccessibleTrader.WebHost` only. The MAUI/desktop heads are **not** touched.

---

## Problem

The public web demo (`AccessibleTrader.WebHost` run with `--demo`) shares **one set of
application state across every visitor**. If two people open the demo and one changes
the symbol, adds an indicator, or navigates, it changes for **everyone** on the site.

Reproduced (2026-06-26) against a clean staging build with two independent browser
sessions:

```
A initial: BTCUSD 1d on Bitstamp
B initial: BTCUSD 1d on Bitstamp
>> A switches market to Stock, loads AAPL
A after: AAPL 1d on Twelve Data
B after (B did nothing): AAPL 1d on Twelve Data   <-- shared state
```

This also explained a string of "impossible" demo bugs: keyboard navigation that
"broke" intermittently was actually **concurrent sessions mutating the same state**
(including the maintainer's own headless test browsers fighting a live user's session).

## Root cause

In `AccessibleTrader.WebHost/ServiceCollectionExtensions.cs`, **every service is
registered `AddSingleton`** — including the per-user state services (`IWorkspaceStore`,
`IEventBus`, `IMarketOrchestrator`, `IDataManager`, `IDataOrchestrator`,
`LiveStreamManager`, `GlobalInputService`, the speech/audio/input services, the
indicator engine, rendering state, etc.).

This is **correct for the MAUI/desktop head** — one user per process, so a Singleton
*is* "the user's state." The WebHost was originally a "desktop app in a browser" and
inherited the same wiring. But under **Blazor Server**, every browser connection is its
own **circuit** with its own DI **scope**; a `Singleton` is one instance for the whole
*process* (all visitors), while `Scoped` is one instance **per circuit** (per visitor).
So the fix is to register per-user state services as `Scoped` in the WebHost.

Why it isn't a 3-line change: `IEventBus` and `IWorkspaceStore` are injected almost
everywhere, and **a Singleton may not depend on a Scoped service** (captive dependency).
Once the bus/store become per-circuit, nearly everything that touches them must become
per-circuit too. So this is a broad, deliberate reclassification of the graph, not a
surgical edit.

## Why it's worth doing (beyond the demo)

- **Desktop app: zero impact.** It stays single-user/Singleton (correct). WebHost-only change.
- **Unlocks a hosted, no-install, browser-based product.** A genuine multi-user web
  app is the foundation for a hosted Accessible Trader — meaningful for accessibility
  (no download barrier; works on Chromebooks, locked-down/library/work machines).
- **Code hygiene:** makes the shared-infrastructure vs per-user-state boundary explicit.

## Approach

1. **Classify** every WebHost registration as *shared infrastructure* (stays Singleton)
   or *per-user state* (becomes Scoped). See the table below.
2. **Per-visitor provider instances** (chosen for simplicity): each circuit gets its own
   data-provider instances and its own upstream connections/subscriptions, so two
   visitors on different symbols don't fight over one socket. (A shared connection pool
   keyed by symbol is a possible later optimization; not now.) As a bonus this fixes
   orphaned live streams — a visitor's streams die with their circuit.
3. **Split plugin *loading* (shared) from provider *instantiation* (per-circuit).** The
   plugin DLLs/types load once (Singleton `IPluginLoaderService`); each scope creates its
   own provider instances from those types and configures them from the shared key store.
4. **Split startup.** `AppStartupService` currently does everything once. Separate
   **app-once** init (load plugins/indicators, trust manifest, seed the demo TD key into
   the shared key store) from **per-circuit** init (instantiate+configure that circuit's
   providers, init its workspace, load the default BTC/USD chart — which already runs
   per-circuit in `MainLayout`).
5. **Let the runtime find captive dependencies.** Enable `ValidateScopes = true` and
   `ValidateOnBuild = true` on the host builder; it throws at startup with the exact list
   of "Singleton X depends on Scoped Y" violations. Iterate until clean. This is what
   makes a ~100-service reclassification tractable instead of guesswork.

## Service classification (initial; refined by ValidateScopes)

**Stays Singleton — shared, stateless, or process-wide infrastructure:**
- Logging/paths/runtime: `IAppLogger`, `IPlatformPathService`, `IRuntimePlatform`, `IMainThreadService`
- `DbContextFactory<AppDbContext>` (already a factory — correct)
- Secrets/security: `WebHostSecureStorageService`, `IApiKeyService`, `SecurityEventLog`, `CheckoutLatencyTracker`, `IPluginHttpClientFactory`, `IApiKeyCheckout`
- Plugins: `PluginTrustPolicy`, `IPluginLoaderService` (load DLLs once)
- Caches: `IDataCacheService`, `ICacheService` (FileCache), `IResamplerService`
- Indicator **catalog** (stateless calculators): all `IIndicatorProvider` impls,
  `IIndicatorRegistry`, `IIndicatorModelFactory`, `IComponentRoleMapper`,
  `ISonificationProfileProvider`, `ISoundPatchLibrary`
- Drawing **calculators** (stateless): all `IDrawingCalculator` impls
- Rendering math if stateless: `ChartRenderer`, `IViewportRangeCalculator` (verify no per-user fields)
- Libraries/definitions: `IWorkspaceLibraryService`, `IStrategyLibrary`, `ISignalCatalog`
- `AppStartupService` (app-once init; will be split)

**Becomes Scoped — per-circuit user state:**
- `IEventBus` (the per-user pub/sub backbone — the keystone of the change)
- `IWorkspaceStore` (the chart state: data, active series, cursor, viewport)
- `IMarketOrchestrator`, `IDataManager`, `IDataOrchestrator`, `IDataOrchestrationService`
- `LiveStreamManager`, `HistoricalDataFetcher`, `BackfillManager`, `IConnectionManager`
- `IDataService` (per-circuit provider instances; see split above)
- Input: `GlobalInputService`, `IInputService`
- Speech: `BlazorSpeechManager`, `ISpeechManager` (per-circuit ARIA live regions)
- Audio: `WebHostBrowserAudioSink`, `IAudioDriver`
- View state: `ICanvasRegionProvider`, `IViewportNavigationService`, `IVolumeStateService`, `ThemeService`, `IPaneLayoutService`
- Indicators (stateful): `IIndicatorService`, `IIndicatorEngine`, `IIndicatorOrchestrator`,
  `IIndicatorStateMapper`, `ISeriesManagementService`, `IHeatmapService`, `IProfileService`, `CrossSeriesCache`
- Drawing/strategy/business state: `IDrawingService`, strategy engine/backtester/coordinator,
  `IDataExportService`, `IPaperTradingProvider`, `IOrderExecutionService`, `SetupSonifier`,
  alert delivery, `IMultiTimeframeDataService`, `ILevelService`
- `ISettingsManager` (per-circuit in the demo; non-persistent)

> The split is verified empirically: any misclassification surfaces as a captive-dependency
> error at startup from `ValidateScopes`, which is then corrected.

## Staging environment (Phase 0 — done)

So the live demo is never touched until the change is proven:

- **Build:** `dotnet publish AccessibleTrader.WebHost/...csproj -c Release -p:ServerPublish=true -o <stage>` → `/opt/accessible-trader-demo-staging`, regen `plugins_trusted.manifest`.
- **systemd:** `accessible-trader-demo-staging.service`, `User=debian`, runs with `--demo --no-launch`.
- **Port:** `Kestrel__Endpoints__Http__Url=http://127.0.0.1:5146` (the port is pinned in
  `appsettings.json`, which overrides `ASPNETCORE_URLS`; this env key overrides *that*).
- **Isolated state:** `XDG_DATA_HOME=/opt/.../xdg-data`, `XDG_CACHE_HOME=/opt/.../xdg-cache`
  so staging's settings/workspace/secrets never collide with the live demo's `~/.local/share`.
- **Secret:** `EnvironmentFile=-/etc/accessible-trader-demo-staging.env` (its own copy of `DEMO_TWELVEDATA_APIKEY`).
- **Access:** localhost only (`127.0.0.1:5146`), **no nginx, not publicly exposed.** Tested
  directly and via headless Chromium from the box.

## Acceptance test

Two independent browser contexts against `http://127.0.0.1:5146/app/`: A changes
symbol/market; B (which does nothing) must **stay on its own chart**. Baseline (current
code) fails this (B follows A). After scoping, B must be unaffected. Also: add an
indicator in A and confirm B's indicator set is unchanged.

## Phase checklist

- [x] **Phase 0** — staging instance on 5146, isolated state, bug reproduced, harness validated.
- [x] **Phase 1** — reclassified registrations (default → Scoped; small curated Singleton allow-list).
- [x] **Phase 2** — `ValidateScopes`/`ValidateOnBuild` enabled; captive dependencies resolved
      (`PluginTrustPolicy` re-pinned Singleton; `IAppLogger` → Scoped because it publishes via `IEventBus`).
- [x] **Phase 3** — `PluginLoaderService` now caches discovered TYPES once; per-circuit (Scoped) `DataService`
      instantiates fresh provider objects from the cache → isolated streams, no per-circuit DLL reload.
- [x] **Phase 4** — pipeline init moved out of app-start: `Program.cs` keeps only the app-once TD-key seed;
      `MainLayout.OnInitializedAsync` runs `IAppStartupService.InitializeAsync()` per circuit (before the
      Toolbar's market cascade). `ValidateScopes` host-builder flags added.
- [x] **Phase 5** — **two-session isolation test PASSES on staging.** Baseline: B followed A to AAPL.
      Scoped build: A → AAPL while **B stayed on BTCUSD**; A's RSI did **not** appear in B; per-session
      keyboard nav works (A announced the RSI value while navigating); no client-side errors.
- [x] **Phase 6** — cut over to the live demo. Live isolation test PASSES on production; `ObjectDisposedException`
      fixed (prerender disabled on the app tree). Done 2026-06-27.

## Implementation summary (what changed)

- `Core/Services/PluginLoaderService.cs` — type cache (`GetOrDiscoverTypes`/`DiscoverTypes`); `LoadPlugins`
  instantiates fresh from cached types each call.
- `WebHost/ServiceCollectionExtensions.cs` — flipped default to `AddScoped`; **Singleton allow-list**
  (shared/stateless infra): loggers? no — `IAppLogger` is Scoped (publishes via EventBus); the true
  Singletons are paths/runtime/main-thread, secure storage + api-key store + security log + checkout/http
  infra, `PluginTrustPolicy`, `IPluginLoaderService`, the caches (`IDataCacheService`/`ICacheService`/
  `IResamplerService`), and the `DbContextFactory`. Everything else per-circuit Scoped.
- `WebHost/Program.cs` — `UseDefaultServiceProvider(ValidateScopes/ValidateOnBuild=true)`; removed the
  app-start pipeline-init/shortcut/warm-up block (now per-circuit); kept the app-once TD-key seed.
- `BlazorClient.Components/Layout/MainLayout.razor` — `@inject IAppStartupService`; awaits
  `InitializeAsync()` per circuit in `OnInitializedAsync`.

## Known follow-ups (not blocking isolation)

- **`ObjectDisposedException` on prerender — FIXED.** Root cause: `InteractiveServer` **prerendering** ran
  the app tree's `OnInitialized` twice — once in a throwaway SSR scope (disposed right after prerender),
  then again on the real circuit. The prerender pass kicked off the async chart load, which then dispatched
  to the already-disposed per-circuit `WorkspaceStore.StateStream` (a `BehaviorSubject`). Fix: render the
  app tree with `prerender: false` (`App.razor` → `<AppRoutes @rendermode="@(new InteractiveServerRenderMode(prerender: false))" />`).
  Now the stateful init runs exactly once, in the long-lived per-visitor circuit scope. Verified: 0
  occurrences on staging and live with real circuits; isolation + nav unaffected. (Only cost: the app tree
  paints when the circuit connects rather than as prerendered SSR — the shell/`<head>` still prerender.)
- **Shortcut remap** (`WebHostShortcutRemap`, Firefox Ctrl+Shift→Alt+Shift) was app-once and is currently
  not re-applied per circuit — should move into a WebHost `CircuitHandler` (can't live in the RCL). Arrow
  nav and 3-modifier chords are unaffected; only some 2-modifier Firefox chords regress until added.
- **Resource note:** per-visitor provider instances mean per-visitor upstream connections (by design,
  "simplest"). nginx caps the demo at 12 concurrent. Revisit a shared connection pool only if needed.

## Cutover

Once staging passes the isolation test and is stable, publish the same build to
`/opt/accessible-trader-demo` and restart `accessible-trader-demo`. The change ships as a
patch to `WebHost/ServiceCollectionExtensions.cs` (+ `DataService`/`AppStartupService`
refactor + host-builder `ValidateScopes` flags). The staging unit can be left in place as
a permanent pre-prod target or removed.
