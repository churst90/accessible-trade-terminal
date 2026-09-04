# AccessibleTrader: Comprehensive Codebase Knowledge Base

This document is the authoritative technical reference for the AccessibleTrader project. It is designed to give an AI assistant or new team member the deep architectural context, design patterns, and platform-specific nuances required to maintain and evolve the system as a Senior .NET Engineer.

Read it alongside [`Diagrams/`](../Diagrams/README.md), which carries the same material as ten
verified Mermaid sources — architecture, hosting topology, the data / navigation / order / feedback
paths, the indicator adapter surface, the plugin trust chain, the script sandbox and tactile paging —
each with a prose summary. When something here changes, check whether a diagram claims it too.

---

## 1. Core Vision & Mandate

**Purpose:** A professional-grade trading terminal engineered exclusively for the blind and visually impaired.
**Primary Feedback Loop:** Sonification (Audio) + Speech (TTS) + Tactile (Haptic/Physical) > Visuals.
**Goal:** Full information-density parity between visual charts and audio/tactile representations.

---

## 2. Technology Stack & Platform

- **Runtime:** .NET 10 (Target: `net10.0`). SDK pinned to `10.0.301` by `global.json`; Razor builds need `-p:UseRazorSourceGenerator=false` (dotnet/razor#13184).
- **Two hosts, one component library.** MAUI Blazor Hybrid (host: MAUI, UI: Blazor WebView) **and** ASP.NET Core Blazor Server (`AccessibleTrader.WebHost`). Both consume `AccessibleTrader.BlazorClient.Components` unchanged; host-specific paths are runtime-gated on `IRuntimePlatform.IsBrowserHost`. The WebHost is the recommended distribution and the only one deployed publicly — see `Diagrams/hosting_topology.mmd` and `SERVER_SETUP.md`.
- **Target Platforms:** WebHost on Linux / Windows / macOS (recommended, and what the public deployment runs); MAUI on Windows (mature), Android (beta), macOS and iOS (unsigned builds, limited testing). iOS and Mac Catalyst refuse all script compilation — no usable sandbox primitive.
- **Rendering:** SkiaSharp. Under MAUI, a native `SKCanvasView` at Grid layer 0 in `MainPage.xaml` with the `BlazorWebView` transparent at layer 1 over it. Under the WebHost, the same `ChartRenderer.Render` paints an off-screen `SKBitmap` that is PNG-encoded and pushed over SignalR, throttled to ~10 fps. **Do NOT use SkiaSharp.Views.Blazor — it was removed because it caused a WebGL crash, and it is unusable under Blazor Server too (depends on the WASM-only `System.Runtime.InteropServices.JavaScript`).**
- **Audio:** Custom Pure C# DSP Engine (`AudioEngine.cs`). No NAudio, no MIDI, no external dependencies for synthesis. NAudio.Wasapi package remains in BlazorClient for the WASAPI output push path on Windows only; it is not used for synthesis.
- **State Management:** Redux-like `WorkspaceStore` with immutable `WorkspaceState`, `BehaviorSubject<WorkspaceState>` for the stream, and typed `WorkspaceAction` dispatch.
- **Reactive Extensions:** System.Reactive + DynamicData for observable streams and collection synchronization.

---

## 3. Project Structure & Responsibilities

### `AccessibleTrader.Sdk`
- **Role:** The "Contract" layer. Must remain dependency-free and platform-agnostic.
- **Critical Types:**
  - `readonly record struct Ohlcv` — Optimized for zero-allocation processing of high-frequency data.
  - `IMarketDataProvider` — Interface for all exchange plugins.
  - `IIndicatorProvider` — Adapter interface for technical analysis libraries.
  - `WorkspaceState` — Immutable record holding all chart and navigation state.

### `AccessibleTrader.Core`
- **Role:** The "Brain" of the application. No UI dependencies.
- **Orchestrators:** Central hubs — `MarketOrchestrator`, `DataOrchestrator`, `IndicatorOrchestrator`, `DataOrchestrationService`, `SonificationManager`.
- **Audio:** `AudioEngine.cs` — Raw DSP logic (128-voice polyphonic oscillators, ADSR, panning).
- **Accessibility:** `AccessibilityFeedbackCoordinator`, `NavigationEngine`, `SpeechFormatter`, `ProfileBinClassifier`.
- **Input:** `CommandDispatcher`, `InputRouter`, `ShortcutManager`.
- **Rendering:** `ChartRenderer` coordinates `BackgroundLayer`, `DataLayer`, `OverlayLayer`, `ProfileRenderLayer`, `HeatmapRenderer`.

### `AccessibleTrader.BlazorClient`
- **Role:** The "Presentation and Driver" layer. Only platform-specific code lives here.
- **UI:** Razor components, CSS (Dark Mode priority), JS Interop.
- **Drivers:** `BlazorAudioDriver` (IAudioDriver impl), `BlazorSpeechManager` (ISpeechManager impl), `BlazorInputService` / `GlobalInputService` (IInputService impl), `MauiSecureStorageService`.
- **Rule:** No business logic in Razor components. Components are pure presenters + event forwarders.

### `AccessibleTrader.ScriptSandbox`
- **Role:** Shared host ↔ worker IPC contract library. Frame codec, opcodes, binary DTOs for indicator metadata / Calculate requests / Calculate responses.
- **Dependencies:** references `AccessibleTrader.Sdk` only (for `Ohlcv` etc). No platform deps.

### `AccessibleTrader.WebHost`
- **Role:** ASP.NET Core Blazor Server head. Kestrel, server-side PNG chart rendering over SignalR, Orca D-Bus speech with `spd-say` and browser `SpeechSynthesis` fallbacks. Per-circuit DI scoping makes it multi-user; `--accounts` adds self-hosted ASP.NET Identity accounts and `--demo` the anonymous public demo.
- **Host gating:** `DemoPolicy` (`HostMode.Full | Demo | Hosted`) decides which features a head may expose. Read it before adding anything to a hosted head. Standing constraint: **no hosted head may hold real broker credentials or place a live order, not even behind a flag.**
- **Publish:** always `-p:ServerPublish=true`. Without it the Windows Release `OutputType=WinExe` silently drops `_framework/blazor.web.js` from the static-asset manifest — pages return 200 and the circuit never boots. Guarded by `WebHostStaticAssetManifestTests`.

### `AccessibleTrader.StrategyLab`
- **Role:** Headless research CLI over snapshot data. Falsifies strategy claims against recorded controls, and owns the spec catalogue with each spec's provenance. Nothing here ships as a starting library — see `STRATEGY_LIBRARY_POLICY.md`. Design in `LAB_DESIGN.md`, conventions for adding a command in the `strategy-lab` skill.

### `AccessibleTrader.ScriptWorker`
- **Role:** Standalone console exe that hosts user-compiled Roslyn indicators **and strategies** out-of-process. Reads frames from stdin, dispatches into `ICustomIndicator.Calculate` or the strategy frame protocol (`InitializeStrategy` / `OnBar` / `OrderFilled` / `StopStrategy` / `GetMetrics`), writes result frames to stdout. A strategy returns a `StrategySignal` — a *description* of an order that the host's own risk rules then act on — so a sandbox escape in the trading half of the scripting surface does not land in the trading host. One script per worker lifetime; the host spawns a fresh worker per compiled script.
- **Target:** `net10.0` (not MAUI multi-target — it's a plain console app). Copied next to the host binary at build time by the `CopyScriptWorker` target in `AccessibleTrader.BlazorClient.csproj`.

### `Plugins/`
- **Role:** Exchange-specific integrations. Each implements `IMarketDataProvider` from Sdk; trading providers additionally implement `ITradingProvider`.
- **Provider plugins (`Plugins/Providers/`, 16 total):** Alpaca, Binance (Spot+Futures+WebSocket user-data stream), Bitstamp (REST+WebSocket, HMAC-SHA256 signing), Coinbase (REST, full ECDSA JWT auth), Finnhub, FMP, Gemini, InteractiveBrokers (Client Portal Gateway, TLS pinning), Kraken (REST+WebSocket, monotonic nonce counter), KrakenFutures, MEXC, Oanda, Polygon, Schwab (OAuth2 auth-code flow, refresh-token persistence via `PluginHostServices`), Tradier, TwelveData. **Twelve of the sixteen implement `ITradingProvider`** — Finnhub, FMP, Polygon and TwelveData are data-only. See `README.md` and `PROVIDER_AUTHORING.md` for the per-provider capability matrix.
- **Analytics providers (`Plugins/Analytics/`, 17 total):** AlternativeMe, BGeometrics, BinanceDerivatives, BinanceVision (monthly archive zip walker with size caps and zip-bomb guard), CFTC, CoinGecko, CoinMetrics, DefiLlama, Deribit, Etherscan, FINRA, Fred, Glassnode, Mempool, OkxDerivatives, SecEdgar, WikipediaPageviews. All share a capped `HttpClient` pattern (`MaxResponseContentBufferSize = 32 MB`, 60s timeout).
- **Strategy plugins (`Plugins/Strategies/`):** loaded by `StrategyPluginRegistry`, deliberately separate from the provider loader so the two plugin sets cannot be confused for one another.
- **Drop-in indicators (`Plugins/Indicators/`):** optional indicator DLLs loaded at startup via `PluginLoaderService` the same way provider plugins are. Gated by `PluginTrustPolicy` (see section 14).

---

## 4. The Orchestrator Ecosystem

The application uses specialized Orchestrators to manage complexity and asynchronous state:

1. **`MarketOrchestrator`** — App-level market/provider/symbol selection. Cascade: `SelectedMarket` → `RefreshProvidersAsync()` → `RefreshSymbolsAsync()` → `PipelineUpdated` fires. Calls `LoadChartAsync()` → `DataManager.RefreshDataAsync()` → dispatches `InitializationStatus.Ready`.
2. **`DataOrchestrator`** — Network resilience facade. Wraps `HistoricalDataFetcher` + `LiveStreamManager` with Polly circuit breaker and retry. Owns the `DataState` state machine (`Initializing → HistoricalFilling → GapFilling → LiveStreaming → Faulted`).
3. **`DataOrchestrationService`** — Indicator pipeline coordinator. Subscribes to `DataManager.DataUpdated` and `StateStream` changes. When `InitStatus == Ready`, triggers `RecalculateAllAsync` or `RecalculateLastAsync` via `IndicatorOrchestrator`. Also subscribes to `IndicatorUpdatedEvent` (via EventBus) to force full recalculation when a series is added.
4. **`IndicatorOrchestrator`** — Computes all active indicators, profiles, and heatmaps. Dispatches `UpdateSeriesDataAction` to the store with results.
5. **`SonificationManager`** — Observes `WorkspaceStore.StateStream`. On navigation (`indexChanged || focusChanged`), calls `NavigationSonifier.SyncNavigationSlots()`. This is the **single authoritative audio path for navigation**.
6. **`PlaybackOrchestrator` + `AudioSequencer`** — Manage chart replay sequences. `AudioSequencer` iterates visible series and plays each point.

---

## 5. EventBus vs Rx — Authoritative Routing Decision

This is the most important architectural rule. Violations create double-firing bugs and architectural confusion.

### Rule 1: Use EventBus for cross-layer, fire-and-forget events

`IEventBus.Publish<T>()` / `IEventBus.Subscribe<T>()` when:
- The event crosses assembly or lifecycle boundaries (Core ↔ BlazorClient, or between independent services).
- It is a one-shot command or notification — not a continuous stream.
- Multiple unrelated subscribers must react (fan-out) and you do not know them at publish time.

**Canonical EventBus events (must stay on EventBus):**

| Event | Publisher | Why EventBus |
|---|---|---|
| `OpenSettingsEvent` | CommandDispatcher | Modal lifecycle — BlazorClient modal responds to Core command |
| `OpenHelpEvent` | CommandDispatcher | Modal lifecycle |
| `OpenApiKeysEvent` | CommandDispatcher | Modal lifecycle |
| `OpenAddIndicatorEvent` | CommandDispatcher | Modal lifecycle |
| `OpenDrawingToolsEvent` | CommandDispatcher | Modal lifecycle |
| `OpenStrategiesEvent` | CommandDispatcher | Modal lifecycle |
| `OpenObjectTreeEvent` | CommandDispatcher | Modal lifecycle |
| `OpenTradingDashboardEvent` | CommandDispatcher | Modal lifecycle |
| `OpenAlertsEvent` | CommandDispatcher | Modal lifecycle |
| `OpenPropertiesEvent` | CommandDispatcher | Modal lifecycle + optional SeriesId payload |
| `FeedbackRequestEvent` | NavigationEngine, DataOrchestrator, CommandDispatcher | Cross-layer speech routing — Core publishes, AccessibilityFeedbackCoordinator in Core routes to BlazorClient SpeechManager |
| `AnnouncementEvent` | Various | One-shot speech announcement |
| `AlertFiredEvent` | AlertEvaluator | Cross-cutting — affects audio, speech, and UI |
| `NavKeyReleasedEvent` | GlobalInputService (BlazorClient JS bridge) | Hardware event from UI layer consumed in Core audio engine |
| `IndicatorUpdatedEvent` | SeriesManagementService | Decoupled pipeline trigger — published when a series is added/removed; DataOrchestrationService reacts |
| `ConnectionStatusEvent` | DataOrchestrator, plugins | Status broadcast consumed by multiple independent services |
| `RedrawEvent` | HistoryBufferCoordinator | One-shot render request |
| `AddDrawingEvent` | CommandDispatcher | Drawing tool activation |
| `CancelDrawingEvent` | CommandDispatcher | Drawing cancellation |
| `ToggleHideEvent` | CommandDispatcher | Series visibility toggle |
| `ToggleMuteEvent` | CommandDispatcher | Series mute toggle |
| `DeleteSeriesEvent` | CommandDispatcher | Series removal |
| `VolumeChangeEvent` | CommandDispatcher | Volume adjustment |

### Rule 2: Use EventBus.AsObservable<T>() when you need Rx operators on an EventBus event

Call `_eventBus.AsObservable<T>()` when the consuming service needs `Throttle`, `DistinctUntilChanged`, `Sample`, `Buffer`, or other Rx operators on a fire-and-forget event.

**Current uses:**
- `DataOrchestrationService`: `_eventBus.AsObservable<IndicatorUpdatedEvent>().Subscribe(...)` — no operator needed here but uses AsObservable for consistency with the CompositeDisposable pattern.
- `SonificationManager`: `_eventBus.AsObservable<NavKeyReleasedEvent>().Subscribe(...)` — same pattern.
- `SonificationManager`: `_eventBus.AsObservable<AlertFiredEvent>().Subscribe(...)` — same pattern.

### Rule 3: Use direct Rx Subjects/Observables for intra-service state streams

Use `BehaviorSubject<T>`, `Subject<T>`, or `IObservable<T>` exposed as properties when:
- State is **owned by one service** and consumed downstream (not cross-layer).
- You need **BehaviorSubject semantics** (latest value replay on subscribe).
- The stream is **continuous** (not one-shot events).

**Canonical direct Rx streams (must NOT go through EventBus):**

| Observable | Owner | Why direct Rx |
|---|---|---|
| `IWorkspaceStore.StateStream` | WorkspaceStore | Central state — all services observe this directly |
| `IDataManager.DataStream` | DataManager | Continuous OHLCV data stream |
| `IDataManager.InitialLoadStream` | DataManager | One-time initial load completion |
| `IDataOrchestrator.StateChanged` | DataOrchestrator | Data state machine transitions |
| `IMarketOrchestrator.PipelineUpdated` | MarketOrchestrator | Market selection changes |
| `ISonificationManager.StateChanged` | SonificationManager | Audio engine state transitions |
| `IPlaybackOrchestrator.PlaybackFinished` | PlaybackOrchestrator | Playback completion event |

### Rule 4: Never

- Never publish `Ohlcv` ticks through EventBus — use `System.Threading.Channels` (already implemented in `DataOrchestrator.LiveStream`).
- Never use EventBus inside the audio buffer generation loop (`AudioEngine.GenerateBuffer`) — performance-critical, lock-free only.
- Always dispose EventBus subscriptions in `IDisposable.Dispose()` to prevent memory leaks in Blazor components.
- Never subscribe to EventBus in `struct` types.

---

## 6. Critical Data & Event Pipeline

1. **Ingestion:** `LiveStreamManager` (WebSockets) receives raw ticks as `System.Threading.Channels.ChannelReader<Ohlcv>`.
2. **Normalization:** Ticks are `Ohlcv` record structs from the plugin — no conversion needed.
3. **Resampling:** `ResamplerService` generates higher timeframes (1m → 5m) in real-time.
4. **Persistence:** `DataCacheService` + `FileCacheService` (two-tier: in-memory circular buffer + disk).
5. **State Broadcast:** `WorkspaceStore.Dispatch(new SetDataAction(...))` → `StateStream` notifies all observers.
6. **Indicator Calculation:** `DataOrchestrationService.OnDataUpdated()` → `IndicatorOrchestrator.RecalculateAllAsync()` → `UpdateSeriesDataAction` per series.
7. **Audio/Speech:** `SonificationManager` observes `StateStream` directly. `AccessibilityFeedbackCoordinator` handles `FeedbackRequestEvent` from EventBus.

---

## 7. Navigation Sonification — Single Code Path (CRITICAL)

**The dual-path bug and its fix (as of improvement plan session, 2026-03-26):**

There are two places that manipulate audio voice slot 0 (the navigation voice):
- **Path 1 (authoritative):** `SonificationManager.StateStream` subscription → `NavigationSonifier.SyncNavigationSlots()`. This fires on every `indexChanged || focusChanged`.
- **Path 2 (removed):** `NavigationFeedbackManager.HandleNavigationFeedback()` → `SonifyCurrentContext()` → `AudioFeedbackRouter.SonifyComponent/Series`. This was the duplicate.

**Resolution:** `NavigationFeedbackManager.HandleNavigationFeedback()` now handles SPEECH ONLY. All navigation sonification flows exclusively through `SonificationManager` via Path 1. The `SonifyCurrentContext()` call and `_audioRouter.Silence()` call have been removed from `NavigationFeedbackManager`.

**Navigation note parameters (as of fix):**
- `continuous: true` — voice sustains while key is held; keyup stops via `NavKeyReleasedEvent`.
- `0.4s` fallback duration — self-terminates if keyup is missed.
- ADSR: Attack 5ms, Release 20ms on slot 0 to eliminate click artifacts.
- Default master volume: 50% (normalized from 100%).

---

## 8. Audio Engine & Sonification Logic

- **Architecture:** `AudioEngine` generates raw `float[]` buffers. Platform drivers push them out: `BlazorAudioDriver` to WASAPI (Windows), AudioTrack (Android) and AVAudioEngine (iOS/macOS); `WebHostAudioDriver` to Web Audio in the browser.
- **Output limiter (do not remove).** The engine sums every voice, and before 2026-08-26 it summed them straight into the host buffer with nothing between the sum and the DAC — an ordinary eighteen-voice layout at the shipped default volume peaks at **5.5× full scale**, a saturated voice plan at 21.5×, so chart-scope playback had clipped since it was written. `LimiterCeiling = 0.99f` with a 0.25 s release now rides the gain down. **Gain riding, not waveshaping:** timbre carries meaning in this application, so distorting a voice to fit is not an acceptable trade.
- **Oscillators:** Sine, Square, Sawtooth, Triangle, Noise (pink/white/brown) with real-time frequency modulation and interpolation. User `SoundPatch`es layer several oscillators (`OscillatorLayer` list) and are assignable to earcons or per-indicator-component (`ComponentConfig.SoundPatchId` / `BullishSoundPatchId` / `BearishSoundPatchId`), resolved live in `DefaultSonificationStrategy.CreateAudioPoint`.
- **Voice Slots:** 128 total. Slots 0–15 = navigation/data sonification. Slots 16–31 = UI earcons (via `PlayNote`; directional cross earcons on 30/31). Slots 32–95 = playback sequencer. Slots 96–127 = cloud/ribbon fills.
- **Mapping:**
  - Price → Pitch: Higher price = higher frequency (log scale).
  - Time → Pan: Viewport position maps to left/right stereo (-1.0 to +1.0).
  - Volume/Volatility → Texture: Waveform harmonics.
- **Profile Sonification:** Node structural role determines pitch (LVN=220Hz, Normal=330Hz, VAL=440Hz, VAH=550Hz, HVN=660Hz, POC=880Hz). Amplitude = normalized session volume.
- **Heatmap Sonification:** Sawtooth waveform. Global Y-range pitch multiplier (0.5× bottom to 2.0× top).

**Heatmap vs Profile — isHeatmap/isProfile ordering rule (CRITICAL):**
`IndicatorModelFactory` sets `IsProfile = true` for heatmap series (`meta.Code == "HEATMAP"`). This means `isProfile` is always `true` for heatmaps. In any code that branches on both flags, **`isHeatmap` MUST be checked first**, otherwise heatmaps fall into the profile branch. Both `BinnedNavigationStrategy` and `NavigationFeedbackManager` follow this rule. Do not introduce new branches that check `isProfile` before `isHeatmap`.

---

## 9. Input & Navigation Engine

- **Global Input:** `GlobalInputService` (JS bridge via `[JSInvokable]`) captures raw hardware key events in MAUI/Blazor context.
- **Android:** `MainActivity.cs` overrides `DispatchKeyEvent` → `IInputService.ProcessKey`.
- **Mac:** NOT YET IMPLEMENTED — stub comment in `AppDelegate.cs`.
- **Forwarding:** `BlazorInputService` → `ShortcutManager` → `CommandDispatcher` → `NavigationEngine` or `WorkspaceStore`.
- **Navigation Engine:** Processes string commands (`"NAV_LEFT"`, `"NAV_RIGHT"`, etc.) → dispatches `NavigateAction` to store → publishes `FeedbackRequestEvent(FeedbackType.Navigation)`.
- **F-Key Protocol:**
  - F1 = OpenHelp; F2 = ToggleSpeech; F3 = ToggleSonification; F4 = ContextSummary; F12 = OpenSettings (Shift+F12 = OpenProperties).
  - F5/Shift+F5 = Component volume up/down; F6/Shift+F6 = Series volume up/down; F7/Shift+F7 = Chart master volume up/down.
- **Initialization:** `IInputRouter` and `IChartCommandManager` MUST be resolved at startup (done in MainPage constructor) — they self-wire via constructor subscriptions.

---

## 10. Keyboard Layout (MAUI MainPage)

`MainPage.xaml` hosts a Grid with two children:
1. `SKCanvasView` at layer 0 (bottom) — SkiaSharp renders the chart here.
2. `BlazorWebView` at layer 1 (top, transparent) — Blazor UI chrome overlays it.

The transparency stack:
- `app.css`: forces `html/body/#app { background: transparent }`.
- `MainPage.xaml.cs OnBlazorWebViewInitialized`: sets `WebView2.DefaultBackgroundColor = transparent` and injects JS to clear background colors.
- `UseSkiaSharp()` MUST be in `MauiProgram.cs` — without it MAUI crashes (`HandlerNotFoundException` for `SKCanvasView`).

---

## 11. Known Architectural Challenges & Guardrails

### The Initialization Order (STRICT)
1. `ConfigService` → Load JSON settings.
2. `MarketOrchestrator.RefreshPipelineAsync()` → Establish provider list.
3. `MarketOrchestrator.LoadChartAsync()` → `DataManager.RefreshDataAsync()` → dispatches `InitializationStatus.Ready`.
4. `DataOrchestrationService` reacts to `InitStatus == Ready` → triggers first indicator calculation.
5. `IndicatorOrchestrator.RecalculateAllAsync()` completes → `UpdateSeriesDataAction` per series → `StateStream` notifies audio/speech.

### The ChartArea Blackout Gate
`ChartArea.razor` shows a blackout overlay until `DataOrchestrator` reaches `LiveStreaming` state. Modals have `z-index: 9999` and are rendered outside `<main>` in `MainLayout.razor` so they always overlay the blackout. Do not change the blackout z-index to above 9999 or modals will become inaccessible.

### The "White Chart" Race Condition
`ChartRenderer` must check that `WorkspaceConfiguration` and canvas are both ready before rendering. Default to black (opaque, not white) on any premature render call.

### Modal Focus Pattern (ALL modals)
All modals focus the `h2` heading on open (`tabindex="-1"` on `h2`, `focusElement("modal-title-id")`). **Do NOT focus the close button** — screen readers need to hear the title first to understand modal context.

### Memory Management Rules
- Use `readonly record struct Ohlcv` for all price data — never `class Ohlcv`.
- For audio DSP: use `Span<T>` and `Memory<T>`. Strictly avoid `new` or LINQ inside `AudioEngine.GenerateBuffer`.
- Always unsubscribe EventBus subscriptions in `Dispose()` to prevent memory leaks.
- `NavigationSonifier.SyncNavigationSlots` uses pre-allocated `OscillatorVoice` slots — never creates GC objects in the hot path.

---

## 12. Design Principles for Maintenance

- **SRP:** One class, one reason to change. `CommandDispatcher` routes only — it does not contain business logic. `NavigationFeedbackManager` handles speech only — not audio. `SonificationManager` handles audio only — not speech.
- **Accessibility First:** Every visual change must have a corresponding speech fact or audio earcon via `AccessibilityFeedbackCoordinator`. No exceptions.
- **Surgical Updates:** When modifying services, maintain existing interfaces to avoid breaking SDK contracts. New providers and indicators should require zero changes to Core.
- **No Business Logic in Blazor:** Razor components and `BlazorClient` services are drivers and presenters only. All logic belongs in `Core` orchestrators.

---

## 12.5. Strategy Composer Pipeline (Phase 11, 2026-04-07)

A user-buildable signal composer that combines indicator components from any registered indicator into a reward/risk-gated buy/sell strategy. Lives alongside the existing built-in / Roslyn / Composite strategy paths and uses the same `IStrategyEngine` for execution.

### Pipeline diagram

```
   IIndicatorProvider.GetIndicators()
              │
              ▼
        ISignalCatalog                      ─── Walks every provider at startup and emits one
              │                                  SignalDescriptor per IndicatorComponentMetadata —
              │                                  but publishes only those declared Causal. See
              │                                  "The causality gate" below.
              │
              ▼
   StrategySpec (persisted to strategies.json)
              │   ├── ConditionNode tree (ConditionLeaf / ConditionGroup, AND/OR/NOT)
              │   ├── RiskPlan (StopSource / TpLadderRung[] / PositionSizing /
              │   │              EntryTrigger / MinRewardRiskRatio / NotionalEquity)
              │   └── Side / ExecutionMode
              │
              ▼
   IConfigurableStrategyFactory.Create(spec)
              │
              ▼
   ConfigurableStrategy.OnBar(newBar, history, state)
              │
              │   1. IConditionEvaluator.Evaluate(tree, history, state)
              │      → ConditionEvaluation { OverallTrue, LeafResults, Score }
              │      (no AND short-circuit — every leaf evaluated for dropout map)
              │
              │   2. Diff LeafResults vs _lastLeafResults
              │      → publish SetupDroppedEvent for any flipped-off leaves
              │
              │   3. State machine
              │      ├── inactive + true  → IRiskPlanResolver.Resolve
              │      │                       → null = silent drop (R:R gate fail or Phase-4 stub)
              │      │                       → emit StrategySignal,
              │      │                          publish SetupConfirmedEvent
              │      ├── active + true    → publish SetupReconfirmedEvent
              │      └── active + false   → silent transition to inactive
              │
              ▼
   IStrategyEngine                          ─── Existing 30s-dedup pipeline +
              │                                  StrategySignalEvent publication.
              │                                  Auto mode → IOrderExecutionService.
              │
              ├──→ JournalService           ─── Subscribes to StrategySignalEvent,
              │                                  records full rationale (side, score,
              │                                  stop, first target, R:R, notes).
              │
              └──→ SetupSonifier            ─── Subscribes to SetupConfirmed/
                                                Reconfirmed/Dropped events.
                                                Confirmed = full bell + speech.
                                                Reconfirmed = quieter bell + heartbeat.
                                                Dropped = speech only ("X dropped off").
```

### Audio surfaces

- `IEarconService.PlaySetupBell(OrderSide side, bool reconfirmation)` — long = sine 440 + perfect-fifth 660 + octave 880; short = triangle 220 + sub-fifth 165 + low octave 110. Reconfirmation halves duration and drops to ~40% volume so confirming bars don't fatigue.
- `setup_long_bell` / `setup_short_bell` patches in `SoundPatchRegistry` are the per-bar playback-pipeline equivalents (used by `AudioSequencer` when a strategy signal happens to land on a played bar). The earcon is the one-shot equivalent for live state-machine transitions.

### Speech / Journal surfaces

- `JournalService` (Ctrl+Alt+Shift+J) is the persistent ring-buffer review surface. Subscribes to `StrategySignalEvent`, `AlertFiredEvent`, `AppErrorEvent`. `BlazorSpeechManager.Speak()` mirrors every TTS phrase via lazy `IServiceProvider` resolution. JournalModal renders the buffer in a screen-reader-friendly monospace `<textarea readonly>` with category filter buttons and copy-visible.
- A confirmed setup speaks like: *"Long setup, score 0.82. Stop 1.0795, first target 1.0930 (R:R 2.00). Stop below 20-bar swing extreme at 1.0795."* — every piece of information the trader needs to act and to review.

### The causality gate (2026-08-21)

This pipeline turns a chart component into something a strategy can be built on. Until 2026-08-21 it
did that for **every** component of **every** provider, with no allowlist — so "is this component
look-ahead-safe" was never a chart-cosmetics question, it was a backtest-validity question for all
of them, and five components failed it. `ICHIMOKU.Chikou Span` is the clearest: `chikou[j]` holds
`close[j + 26]`, which is the correct way to draw a lagging span, so the leaf condition
"Chikou Span > Close" evaluated `close[j+26] > close[j]` and returned a spectacular, entirely fake
edge.

- **The declaration.** `ComponentCausality { Undeclared, Causal, Lookahead }` in
  `AccessibleTrader.Sdk/Models/ComponentCausality.cs`. An indicator declares once via
  `IndicatorMetadata.Causality`; a component overrides via the nullable
  `IndicatorComponentMetadata.Causality`. `CausalityContract.Effective / IsPublishable /
  RefusalReason` resolves the pair. `Undeclared` is the default **and publishes nothing** — a new
  component is invisible to the strategy builder until someone decides, because silence is the one
  answer that cannot be wrong by accident.
- **The gate.** `SignalCatalog.All` carries only `Causal` descriptors. The rest go to `Excluded`,
  keep a sentence in `RefusalReason(id)`, and — deliberately — remain resolvable through
  `GetById`, so a strategy saved before the gate existed can be told why its leaf stopped firing
  instead of resolving to null and reading as a typo.
- **The refusal.** `ConditionEvaluator.RefusedForCausality` covers all three paths that resolve a
  descriptor and then read its data: the plain leaf, the per-bar read behind the `Sequence`
  operator, and the second descriptor of a crosses-line comparison. A refused leaf returns false and
  records why on `LastDegradation` (renamed from `LastHtfDegradation`, which now has two callers).
- **The proof.** `IndicatorCausalityTests` runs every provider over `bars.Take(k)` and over the full
  series and requires every `Causal` component to agree on the shared prefix, sweeping k over
  eleven lengths and three synthetic series. Sweeping matters more than the value of k: a marker
  only disagrees within the last few bars of a prefix.
- **What it actually catches.** Look-ahead proper is only half of it. Any parameter derived from
  `data.Length` fails the same test — Cipher SR scaled its pivot window by the total loaded bar
  count, Value Deviation capped its profile at a third of the series, Pulse blanked a whole
  component on a short series. The bar answers differently in a backtest and on a live chart,
  decided by how much history was fetched. **When reviewing an indicator here, grep its `Calculate`
  for `n /`, `data.Length /` and `if (n < …) return`.** A third variant is information destroyed
  retroactively: `SwingStructureAnalyzer` used to delete an earlier pivot when a more extreme one
  appeared, so the same bar described a structure that a longer load denied.
- **The chart/strategy split that makes this liveable.** A marker belongs on the bar it describes
  even though that bar was not knowable until later, so Cipher SR's dots, Swing Structure's
  `SwingHigh`/`SwingLow` and Ichimoku's Chikou stay exactly where they are on the chart and in
  navigation speech, declared `Lookahead`, while the causal form of the same information — the
  confirmation-gated zone lines, `LastSwingHigh`, `StructureState` — is what strategies get. When
  the marker itself should move to its confirmation bar instead,
  `IndicatorMath.ShiftMarkersForward` is the shared implementation (Cipher A and Cipher B both call
  it).

### Why ConditionEvaluator doesn't AND short-circuit

The per-leaf result map is what makes dropout detection possible. If AND short-circuited, a leaf later in the children list would never be re-evaluated when an earlier leaf is false, and `_lastLeafResults` would have stale data, breaking the "Cipher A wave cross dropped off" announcement. The cost is small (every leaf evaluated each bar) and the correctness gain is essential.

### Why RiskPlanResolver returns null silently on failure

A setup that fails the R:R gate or references a Phase-4 (S/R / volume profile) stop source isn't an error — it's a *bad setup* that the user shouldn't be alerted about. Silent drop means the bell never rings, the journal never gets an entry, the order is never placed. The correct user experience is "the strategy doesn't fire" rather than "the strategy keeps spamming errors." Phase-4 stop/target sources will fire normally once Session C wires the level providers.

### Where Multi-Timeframe plugs in (Session B)

`ConditionLeaf.Timeframe` is already on every leaf as an optional string. `ConditionEvaluator.EvaluateLeaf` currently falls through to active-TF data regardless of `Timeframe`. Session B replaces the active-series lookup with a `(timeframe == null ? activeSeries : multiTimeframeService.GetSeries(symbol, timeframe))` branch. The indicator engine's cache key gets a TF dimension. Session B is also where the adaptive backtester history fetch and R-multiple metrics ship.

---

## 13. Improvement Plan Reference (2026-03-26 Session)

The current codebase is operating under a 4-phase improvement plan approved 2026-03-26:

- **Phase 0 (complete):** Zero-risk cleanup — StatusBar de-duplication (was already done), stub annotations, task tracker migration.
- **Phase 1 (complete):** Accessibility path bugs — dual sonification path unified, ADSR on nav slot, default volume normalized, loading-state speech.
- **Phase 2 (complete):** Data pipeline bugs — InitializationStatus.Ready timing, PlaybackScope differentiation.
- **Phase 3 (complete):** Structural cleanup — EventBus rationalization (documented above), HelpModal enriched with User Guide content.
- **Phase 4 (complete):** SRP refactoring — CommandDispatcher, DrawingService, SkenderIndicatorProvider, WorkspaceStore reducers.

**Phases 5–7 (roadmap, not yet implemented):**
- Phase 5: Platform parity (Mac keyboard, Android/iOS audio drivers, Coinbase JWT).
- Phase 6: Performance (Span-based indicators, System.Threading.Channels full migration, voice pooling).
- Phase 7: Feature completion (strategy backtester UI, custom speech templates, HelpModal content, tactile display).

See `TODO.md` for granular items per phase.

---

## 14. Security architecture (2026-04-16 → 2026-04-17, phase 4 complete)

A pre-customer-release security audit drove four phases of hardening ending with full OS-level sandboxing on every supported platform. The full narrative lives in `CHANGES.md`; this section documents the architectural pieces that future maintainers need to know about.

### Plugin trust

- **`PluginTrustPolicy`** (`AccessibleTrader.Core/Services/PluginLoaderService.cs`) — SHA-256 hex allow-list plus a `RequireTrusted` bool. `PluginLoaderService` hashes every candidate DLL before `LoadFromAssemblyPath` and skips any DLL whose hash isn't in the allow-list. **Default: `RequireTrusted = true`** (phase-4 Track A). A missing manifest leaves the policy enforcing an empty allow-list — every plugin is refused, which is the intentional fail-closed behaviour.
- **`plugins_trusted.manifest`** — sibling file in `AppContext.BaseDirectory`, newline-separated hex SHA-256 digests with `#` comments. Auto-generated by the `GeneratePluginTrustManifest` MSBuild target in `AccessibleTrader.BlazorClient.csproj` on **every build** (Debug + Release) so the dev workflow stays in sync with the shipping default. `tools/generate-plugin-trust-manifest.{ps1,sh}` ship for manual use against an external build output. `.github/workflows/plugin-manifest.yml` uploads the manifest as a workflow artifact on PRs/pushes and attaches it to the GitHub Release on `v*` tag pushes.
- **Dev bypass `ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS=1`** — disables enforcement at runtime with a loud per-DLL warning. Intended for developers hand-dropping a new plugin before the manifest has regenerated.
- **Legacy env var `ACCESSIBLETRADER_REQUIRE_TRUSTED_PLUGINS=1`** — kept for back-compat with phase-2/3 deploys. Now redundant since enforcement is the default.

### Plugin host services bridge

Plugins are activated via `Activator.CreateInstance` — they get no DI container and intentionally don't reference `AccessibleTrader.Core` or `Microsoft.Maui.*`. For host-owned services plugins need, the bridge is `AccessibleTrader.Sdk/Services/PluginHostServices.cs` with three static accessors:

- **`PluginHostServices.SecureStorage`** of type `IPluginSecureStorage` — 3-method interface (`GetAsync` / `SetAsync` / `Remove`). Used by `SchwabOAuthService` for cross-platform refresh-token persistence.
- **`PluginHostServices.ApiKeys`** of type `IApiKeyCheckout` — sign-time credential fetch. One method: `CheckoutAsync(providerId, marketType, ct)` returning a use-and-discard `ApiKeyCheckoutResult`. Replaces the phase-3 pattern of stashing `_apiKey` / `_apiSecret` in long-lived fields. Kraken is the canary implementation; remaining trading providers migrate per `CREDENTIAL_CHECKOUT_MIGRATION.md`.
- **`PluginHostServices.HttpClientFactory`** of type `IPluginHttpClientFactory` — creates `HttpClient` instances capped with an `HttpClientPolicy` (provider id, allowed hosts, response-size cap, timeout, User-Agent). The host implementation wraps each client in a `DelegatingHandler` that rejects any outbound request to a host not in the allow-list. **All 17 analytics providers + 11 of the 12 `ITradingProvider` plugins + both LLM providers route through the factory.** The one exception is IBKR (custom TLS-pinned `HttpClientHandler` — incompatible with the factory's wrapping model, has its own 16 MB cap and 30 s timeout inline). Binance and MEXC were rewritten to call the exchange API directly through the factory-created client (no `Binance.Net` / `JK.Mexc.Net` / `CryptoExchange.Net` — those are gone from the tree); MEXC's spot WebSocket is Protobuf, decoded from build-time codegen of the official `mexcdevelop/websocket-proto` files. The migration recipe + per-provider allow-list matrix lives in `CREDENTIAL_CHECKOUT_MIGRATION.md`.
- **`PluginHostServices.SecurityEvents`** of type `ISecurityEventLog` — append-only ring buffer (256 entries) that captures security-relevant runtime events. Concrete impl in `AccessibleTrader.Core/Services/Security/SecurityEventLog.cs` mirrors each record to `ILogger<T>` at Warning level so it also flows into whatever log sinks the host has configured. Event kinds: `AppContainerFallback`, `SandboxExecFallback`, `AndroidServiceFallback`, `MemoryQuotaKill`, `CalculateTimeout`, `CredentialCheckoutFailed`, `TokenCleanupFailed`, `PluginTrustRejected`, `HttpClientHostRejected`, `AudioCommandDropped`, `Other`. Call sites currently instrumented: `WindowsAppContainerLauncher` fallback, `OutOfProcessScriptHost` memory-quota kill + Calculate timeout, `SchwabOAuthService.DeletePersistedRefreshToken` (replaced silent `catch {}` blocks on the explicit scrub path), `BlazorAudioDriver` (every 10th `AudioEngine` ring-buffer overflow).

All three are backed by a single host-side class family:
- **`MauiSecureStorageService`** implements both `ISecureStorageService` (Core) and `IPluginSecureStorage` (Sdk).
- **`MauiApiKeyCheckoutAdapter`** wraps the Core `IApiKeyService` in the SDK `IApiKeyCheckout` shape.
- **`MauiPluginHttpClientFactory`** builds hardened `HttpClient`s with the host allow-list handler.

DI registers each concrete + interface pairing in `ServiceCollectionExtensions.AddCore`. `MauiProgram.CreateMauiApp` resolves all three after `builder.Build()` and sets the `PluginHostServices` statics. The WebHost sets only `SecureStorage`, and only in `HostMode.Full` (`Program.cs`): the bridge is a process-wide static and the hosted head's secret store is shared across users, so hosted/demo leave it null on purpose. Plugins read the statics lazily and null-check — a null adapter means "unit-test / bare-CLI / hosted-multi-user" mode.

Convenience: `PluginHostServices.CreateHttpClient(providerId, allowedHosts, maxResponseBytes?, timeout?, userAgent?)` lets providers write a one-liner field initializer without the "if factory is null, fall back" boilerplate. The fallback path still applies response-size + timeout caps, just without the allow-list handler.

Keep the bridge minimal — it's a service locator, not a DI container. Add a new interface only if a plugin genuinely needs a host-owned capability that can't be delivered via `Configure(Dictionary<string, string>)`.

### Roslyn sandbox

- **`RoslynScriptingService`** (`AccessibleTrader.Core/Services/`) runs user-compiled C# for custom indicators / strategies / `.atpkg` imports. The sandbox has four layers:
  1. **Lexical pre-flight** — rejects `unsafe`, `stackalloc`, `fixed`, `[DllImport]`, `[LibraryImport]` on the raw source before calling the compiler.
  2. **Semantic walker** (`SandboxWalker : CSharpSyntaxWalker`) — walks the bound syntax tree and rejects any call-site reference whose resolved symbol is in a blocked namespace, blocked type, or blocked member list (`Type.GetType`, `Activator.CreateInstance`, `Assembly.Load*`, `Delegate.CreateDelegate`, etc). Applied to indicator, strategy, and legacy-script compile paths.
  3. **ALC isolation** — each script loads into its own collectible `AssemblyLoadContext` inside the worker process. Defense-in-depth only; `AssemblyLoadContext` is not a security boundary.
  4. **Process boundary (phase 4 Track C)** — after a successful compile, the indicator runs in `AccessibleTrader.ScriptWorker`, a standalone console executable spawned via `IScriptWorkerLauncher`. The worker has its own GC, ALC, and handle table; the host communicates with it over a tight binary stdio protocol defined in `AccessibleTrader.ScriptSandbox`. A sandbox escape in user code lands in the worker's memory space — it cannot reach `PluginHostServices.SecureStorage`, live trading WebSockets, or the host's credential service. `OutOfProcessScriptHost` enforces per-call timeouts (5 s Calculate, 10 s LoadAssembly/Ready) and kills the worker via `Process.Kill(entireProcessTree: true)` on overage. Per-platform OS-level sandbox launchers (Windows AppContainer, macOS `sandbox-exec`, Android `isolatedProcess`) are the remaining follow-ups from `SANDBOX_DESIGN.md`.
  - Dev opt-in to the legacy in-process path via `ACCESSIBLETRADER_SCRIPT_IN_PROCESS=1` — only for breakpoint debugging; documented as strictly weaker. **The env var is honoured only in DEBUG builds** (`RoslynScriptingService.InProcessOptIn` is `#if DEBUG`-gated, post-2026-04-23). Release builds ignore it unconditionally so a compromised installer or misconfigured deployment cannot silently downgrade retail users to the unsandboxed in-process path.
- **`.atpkg` imports** (`CustomScriptsModal.razor`) require an explicit user-consent prompt before the source is staged.

### Credential handling

- **Configure path** — `ApiKeyService` stores credentials in platform `SecureStorage` (DPAPI on Windows, keychain on macOS/iOS, KeyStore on Android) keyed by profile nickname. Only metadata (`apikeys_meta.json`) lives in plaintext in `%LocalAppData%`.
- **Sign-time — fetch-on-demand (phase 4 Track B)** — providers call `PluginHostServices.ApiKeys.CheckoutAsync(providerId, marketType)` at each sign site. The host adapter reads the currently-active credential from `IApiKeyService` (→ SecureStorage) and returns a use-and-discard `ApiKeyCheckoutResult`. Credential string lives only for the duration of one signed request. `KrakenProvider.PostPrivateAsync` is the reference implementation; see `CREDENTIAL_CHECKOUT_MIGRATION.md` for the per-provider migration recipe + status matrix.
- **Sign-time — fallback** — providers still populate `_apiKey` / `_apiSecret` fields from `Configure()` for unit tests / CLI runs where `PluginHostServices.ApiKeys` is null. The phase-3 `BaseMarketDataProvider.ScrubCredentials(params Action[])` nulls these on `DisconnectAsync` so crash dumps post-disconnect don't root live secrets.
- **Schwab OAuth** — `SchwabOAuthService` persists refresh tokens ONLY through the host-bridge `PluginHostServices.SecureStorage`; with no bridge the token is memory-only for the session (non-persist). The pre-2026-08 DPAPI-encrypted file at `%APPDATA%\AccessibleTrader\schwab_refresh_token.json` is still read once (Windows only, located via the `%APPDATA%` environment variable — not `Environment.GetFolderPath`, per `PerUserPathPolicyTests`) to migrate the token into the bridge, then deleted; it is never written again. MAUI and the Full-mode WebHost install the bridge at startup; the hosted multi-user WebHost deliberately leaves it null because its secret store is process-wide and a persisted token would be shared across users.

### Transport

- **WebSockets** — `ReconnectingWebSocket.MaxMessageBytes = 16 MB` caps a single frame; oversize triggers a `MessageTooBig` close and the reconnect loop. Protects every streaming provider (Binance, Bitstamp, IBKR, Kraken, …).
- **HttpClient factory (phase 4 Track B)** — `IPluginHttpClientFactory` (host-owned) builds every analytics + trading + LLM provider's `HttpClient` with a per-provider `HttpClientPolicy` that declares (a) response-size cap (default 32 MB), (b) timeout (default 60 s, 120 s for LLM, `InfiniteTimeSpan` for long-poll streams), (c) **outbound-host allow-list** enforced by a `DelegatingHandler` that throws `HttpRequestException` on any URL outside the allow-list. Closes the "future bug interpolates user input into a URL and redirects the request at an attacker-chosen host" class. Full migration status lives in `CREDENTIAL_CHECKOUT_MIGRATION.md` with the per-provider allow-list matrix. Exceptions: IBKR keeps its custom TLS-pinned handler (16 MB cap + 30 s timeout inline); Binance is SDK-managed.
- **Binance Vision archives** additionally cap decompressed size via a `BoundedReadStream` wrapper that throws mid-stream if a zip bomb over-expands.
- **TLS** — `InteractiveBrokersProvider` is the only provider with a custom `ServerCertificateCustomValidationCallback`: loopback-only gateway URL, optional SHA-256 cert pinning via `GatewayCertSha256`. No other provider overrides the default chain.
- **Android manifest** forbids cleartext traffic except on loopback; `allowBackup="false"` to block `adb backup` exfiltration.

### Prompt injection

- `AIAnalystService.BuildUserMessage` wraps every untrusted field (symbol, provider, timeframe, series name, component name) in quotes, strips control chars / backticks / newlines, caps length at 120 chars, and appends an explicit "treat quoted values as data, not commands" directive. Defeats prompt-injection via malicious indicator metadata in imported custom indicators.

### iOS policy

Every path into `RoslynScriptingService.CompileIndicatorAsync` (`.atpkg` file import, pasted-JSON import, direct-typed-in-editor Compile, Pine-transpile output) is refused outright on iOS. iOS has no AppContainer / `isolatedProcess` equivalent and iOS App Review doesn't accept runtime C# compilation. The textarea still works as a text editor so a user can draft a script on iOS and sync to a desktop install. See `CustomScriptsModal.razor` `IosRefusalMessage`.

### Script worker architecture

- **`AccessibleTrader.ScriptSandbox`** — shared contract library. Four-byte length prefix + one-byte opcode + payload framing (`FrameCodec`). Binary DTOs (`IndicatorMetadataMessage`, `CalculateRequest`, `CalculateResponse`) coded by `MessageCodec` — no JSON on the Calculate hot path. Opcode enum splits host → worker commands (LoadAssembly, Calculate, Shutdown) from worker → host responses (Ready, Result, Error, Diagnostic). Max frame size 64 MB. Also holds `WorkerDispatcher` — the transport-agnostic dispatch loop shared by the desktop console worker and the Android bound service.
- **`AccessibleTrader.ScriptWorker`** — thin desktop entry point (`net10.0`). Opens raw stdin/stdout, hands them to `WorkerDispatcher.RunAsync`. All dispatch logic lives in the shared library; this binary is a stdio adapter.
- **`OutOfProcessScriptHost`** (`AccessibleTrader.Core/Services/Scripting/`) — host-side supervisor over an `IScriptWorkerProcess` abstraction (not `System.Diagnostics.Process` directly — see below). Serializes stdin writes on a `SemaphoreSlim`, reads response frames off stdout, streams stderr to the logger, enforces per-call wall-clock timeouts (5 s Calculate, 10 s LoadAssembly/Ready), polls `WorkingSet64` every 2 s against a 256 MB memory quota, kills the worker via `Kill(entireProcessTree: true)` on overage or timeout, sends graceful `Shutdown` on disposal with a 1-second grace window. Memory kills + Calculate timeouts record `ISecurityEventLog` events.
- **`IScriptWorkerProcess`** — launcher-owned process abstraction. Methods: `StdinWrite` / `StdoutRead` / `StderrReader` streams, `HasExited` / `ExitCode`, `Kill` / `WaitForExit`, `Refresh` / `WorkingSet64`, `Dispose`. Exists because the Windows AppContainer path (`CreateProcessW` + `STARTUPINFOEX`) and the Android path (bound `Service` with `ParcelFileDescriptor` pipes) cannot produce a working `System.Diagnostics.Process` — neither has the pipe wiring `Process.Start` sets up. Adapters: `DotNetProcessAdapter` (Default + Mac), `AppContainerScriptWorkerProcess` (Windows), `AndroidScriptWorkerProcess` (Android).
- **`OutOfProcessIndicator`** — `ICustomIndicator` proxy returned from `CompileIndicatorAsync`. `Calculate(ReadOnlySpan<Ohlcv>, Dictionary<string,double>)` materializes the span into an `Ohlcv[]`, round-trips through the host, deserializes the result. Disposal cascades `Shutdown` + worker kill.
- **`IScriptWorkerLauncher`** — abstraction over the OS primitive used to start the worker. `RoslynScriptingService.CreateDefaultLauncher()` picks per OS at startup:
  - **Windows:** `WindowsAppContainerLauncher` manages the `AccessibleTrader.ScriptWorker.Sandbox` AppContainer profile via `userenv.dll` P/Invoke (create / derive SID, cached for the life of the process), then spawns the worker via `CreateProcessW` with `EXTENDED_STARTUPINFO_PRESENT` and a proc-thread attribute list carrying `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` pointing at the profile SID. Manually creates 3 anonymous pipes for stdio redirection since the .NET `Process` class doesn't expose the extended attribute path. Falls back to `DefaultProcessLauncher` on `ERROR_ACCESS_DENIED` (typical dev-box gap — `%USERPROFILE%\...` lacks `ALL APPLICATION PACKAGES` ACE; `Program Files` installs have it by default). `SandboxApplied` / `LastCreateProcessError` expose the real state for telemetry. P/Invoke lives in `WindowsInterop.cs`.
  - **macOS / Mac Catalyst:** `MacSandboxExecLauncher` wraps the worker in `sandbox-exec -f sandbox-profiles/script-worker.sb -D WORKER_DIR=… -D TMPDIR=…`. The deny-default profile permits read of `/usr/lib` / `/System/Library` / the worker dir, read+write in `TMPDIR` (for .NET's R2R/shadow-copy cache), self-signal, self-pidinfo, and the system logger mach-service. Everything else (network, outbound file writes, mach-lookup to any other service, process-exec) is denied by the OS.
  - **Android:** `AccessibleTrader.BlazorClient/Platforms/Android/AndroidIsolatedProcessLauncher` binds `ScriptWorkerService` — a class declared with `[Service(IsolatedProcess=true, Exported=false)]` so Android auto-generates the `<service android:isolatedProcess="true">` manifest entry. Transport is `Messenger`: the launcher creates two `ParcelFileDescriptor.CreatePipe()` pairs, sends the worker-side ends in a Bundle (typed `GetParcelable<T>` on API 33+), closes its own copies so EOF propagates, wraps the host-side FDs as `FileStream`s, and returns `AndroidScriptWorkerProcess`. The service detaches the FDs into `SafeFileHandle`s and hands them to `WorkerDispatcher` — the same dispatch loop the desktop console worker uses. Core's `AndroidIsolatedProcessLauncher` is a routing stub that throws if ever reached — MAUI overrides the DI registration with the real platform launcher at startup via "last registration wins".
  - **Other / fallback:** `DefaultProcessLauncher` spawns the worker via `Process.Start`. Process boundary isolation only; no OS-level capability restriction.
- **Build wiring** — `BlazorClient.csproj` has a `ProjectReference` to the worker with `ReferenceOutputAssembly=false` and **a TFM guard** that excludes iOS / Android / macCatalyst (self-contained mobile targets can't reference a non-self-contained exe per NETSDK1150). `CopyScriptWorker` MSBuild target copies the worker's `bin/$(Configuration)/net10.0` output next to the host binary on Windows + plain desktop builds. `AccessibleTrader.Tests.csproj` follows the same pattern. Android doesn't need the copy — the worker lives in-APK as the `ScriptWorkerService` class.
- **Tests** — `OutOfProcessScriptingTests` roundtrips a trivial indicator through the full Roslyn compile → worker spawn → stdio Calculate → proxy result path. `HostileScriptTests` compiles 6 indicators that deliberately attempt blocked capabilities (`File`, `HttpClient`, `Process.Start`, unsafe, `[DllImport]`, `Assembly.LoadFrom`) and asserts the Roslyn sandbox refuses compilation.

### Remaining follow-ups (no security impact)

- **On-target integration tests** — verify that AppContainer / `sandbox-exec` / `isolatedProcess` actually deny filesystem + network at runtime on the target OS. Requires per-platform CI matrix.
- **Financial `decimal` migration** — every money-path record (`Ohlcv`, `OrderUpdate`, `Balance`, `Position`, `OrderBookEntry`) currently uses `double`. Binary-float rounding accumulates across ticks / fills / P&L. Planned as Phase 5a.
- **Accessibility modal rework** — `ChartArea.razor` needs explicit `@onkeydown` binding; `OrderBookModal.razor` needs live regions + sonification for depth changes. Planned as Phase 5b.

See `SANDBOX_DESIGN.md` for the full worker spec and per-platform rationale.

---

## 15. Screening, analysis and chart-mode services (2026-07-26)

Six user-facing features added in one arc. They share a rule worth stating first,
because breaking it is what made the arc necessary: **a feature is not shipped until
it has an on-screen control.** All six worked, were tested, and had keyboard
shortcuts for a full release cycle with no toolbar button anywhere, which made them
effectively invisible. `ToolbarControlSurfaceTests` now enforces the mechanical part
of that — every feature in its table must have a toolbar button, every button must
name a sprite icon that exists, and every button must carry an `AriaLabel`.

### Toolbar layout contract

Row 1 opens panels. Row 2 builds and changes the chart. New panel-openers go in row 1
next to their neighbours; new chart-mode toggles go in row 2 beside Heatmap / Heikin /
Log. Split and Replay are pinned to row 2 by a test, since "it's a button, put it
anywhere" is how the two rows lose their meaning.

Toolbar toggles for Split and Replay resolve `ISplitViewCoordinator` / `IReplayService`
through `IServiceProvider.GetService<T>()` rather than `@inject`. Deliberate: a host
that hasn't stood up the chart-rendering graph still renders a working toolbar with two
inert buttons, instead of throwing at component-initialisation time and taking the whole
toolbar down. Both buttons publish their command EVENT rather than calling the service —
so button and shortcut share one code path, including the spoken confirmation. The
toolbar subscribes to both events to repaint its pressed state when the change came
from the keyboard.

### Screening (`AccessibleTrader.Core/Services/Screening/`)

- **`IWatchlistLibrary` / `JsonWatchlistLibrary`** — `watchlists.json`. Ordered entries,
  deduped by `WatchlistEntry.Key` (`provider|market|subtype|symbol`).
- **`IScreenerLibrary` / `JsonScreenerLibrary`** — `screeners.json`. Polymorphic
  round-trip of the condition tree relies on the `JsonPolymorphic` attributes on
  `ConditionNode`, exactly as `JsonStrategyLibrary` does.
- **`IScreenerService` / `ScreenerService`** — fetches N bars per symbol at
  `MaxConcurrency = 4`, computes indicators through `IOfflineWorkspaceBuilder`, and
  evaluates with the SAME `ISignalCatalog` + `IConditionEvaluator` the Strategy Composer
  uses. That reuse is the whole design: a screen is a strategy's entry condition asked
  across many symbols at one instant rather than across many bars of one symbol, so every
  indicator and operator works in the screener on day one.
- **Failed rows are REPORTED, never dropped.** `ScreenerRowStatus` distinguishes
  Evaluated / InsufficientHistory / Failed, and `ScreenerRunResult` carries all three
  counts. Silently dropping unfetchable symbols turns "we never looked at twelve of
  these" into "nothing qualified", which is the kind of difference that costs money.
- **`QuickScreenBuilder`** — the flat-filter-rows ⇄ `ConditionNode` translation behind
  the screen builder UI. Lives in Core, not in the razor, because everything it does
  fails SILENTLY when wrong: an operator a signal kind can never satisfy, or an operand
  dropped on the way to disk, produces a screen that runs cleanly and matches nothing,
  which is indistinguishable from a quiet market. Key invariants, all pinned by
  `QuickScreenBuilderTests`:
  - `OperatorsFor(SignalKind)` gates the operator dropdown. A `MarkerFire` signal is NaN
    on every bar it didn't fire, so threshold operators against it are false forever.
  - `BuildRoot` writes only the operands the chosen operator uses. A `Value2` left over
    from a previously selected operator must not change the saved screen's meaning.
  - `FromRoot` reports `HasNestedGroups` rather than silently flattening. Re-saving a
    flattened copy over a hand-built nested screen destroys work the user can't recover.
  - `IndicatorCodeOf` splits at the FIRST dot — component names can contain dots.
- **`OfflineWorkspaceBuilder`** — the shared "compute indicators off a bar list, without a
  workspace" seam. Used by the screener and the respect report; projects
  CANDLES/PRICE/VOLUME pseudo-indicators so those are addressable as signals.

### Symbol picker

`WatchlistModal`'s Add-a-symbol area mirrors `MarketOrchestrator`'s cascade exactly,
including the market-key rule: `"Market|SubType"` **only when the provider actually has
more than one sub-type**, plain `Market` otherwise. That string routes the fetch; a
mismatched key silently returns the wrong universe. It was a free-text box before, and a
typo produced a watchlist entry that failed at screen time and looked like a broken
screener. `QuickScreenBuilder.FilterSymbols` applies the substring filter and the
500-row display cap in one place, and the modal always reports both counts — a truncated
list must never pose as a complete one.

### Analysis (`AccessibleTrader.Core/Services/Analysis/`)

- **`LevelRespectAnalyzer`** — counts touches with WICKS, judges outcome on CLOSES.
  Measuring the reaction from the touch bar's extreme made a sweep and a genuine
  breakdown identical. `InteractionKind` splits Through / Reclaim / Ricochet.
  `lastCountedBar` starts at `-1`, not `int.MinValue` — `i - lastCountedBar` overflowed
  and suppressed every touch in the first version.
- **`MaRespectRanker`** — ranks a standard period set including MTF projections, which
  read only the last CLOSED higher-timeframe bar.
- **`LevelProvenanceService`** — gathers MAs, `ILevelProvider` levels, prior-period H/L
  and round numbers into one ranked list.
- **`SwingStructureAnalyzer`** — pivots, HH/HL/LH/LL labels, structure state, BOS, CHoCH.
  **Contains a retrospective pass that replaces a kept pivot when a later, more extreme
  same-kind pivot arrives.** Correct for describing history; fatal in a backtest. Running
  the descriptive analyzer through a P&L loop produced +3,137,733% on BTC; the causal
  version produced +9,291%, a factor of ~340. Anything that trades off swings must
  re-derive them causally (see `SwingTradeCommand.CausalSwings` in the Lab).
- **`ValueDeviationAnalyzer`** — rolling volume-profile POC, value area, signed deviation
  tiers. Carries a **peak-prominence guard**: a uniform profile has no meaningful POC, and
  the value-area-width test does NOT catch it (growing from bin 0, 70% of volume fills 35
  of 50 bins and looks "narrow"). `volume[pocBin] < meanBin * 1.15` does.
- **`ReplayService`** — dispatches a growing PREFIX of history via `UpdateDataAction`.
  Dispatch ORDER is load-bearing: mode flag before truncation, after restoration.
- **`ChartFrameRenderer`** — the one place both heads (the WebHost's offscreen surface and
  the MAUI head's `SKCanvasView`) go through to draw a frame. It exists because a frame is
  not just `ChartRenderer.Render`: the formations layer has to be resolved from the pattern
  cache against the chart identity and the WHOLE series, which the renderer cannot do
  because it only ever sees the visible slice. Replaced `SplitViewCoordinator` when split
  view was retired 2026-09-04 — the second pane was read-only by construction, so the
  terminal drew a chart it could say nothing about.
- **`ChartPaneModel`** — the structural model: which panes exist, in what visual order, what
  each is called, which series belong to which, and what strips are inside. Navigation,
  speech and the pane readback all read it, so there is one answer rather than three
  disagreeing ones. A PANE IS A Y AXIS; a sub-pane is a strip drawn from every series in the
  pane, which is why walking one per-series was wrong.

### Indicator-layer changes made for these features

- **`MarkerAnchor` (`Value` / `BelowBar` / `AboveBar`)** on `ComponentConfig`, seeded from
  `IndicatorMetadata.DefaultMarkerAnchor`. Marker components whose value is a ZONE PRICE
  rather than a plot level (Value Deviation's tiers) anchor to the bar extreme.
  `StandardRenderers.ResolveMarkerY` is the single place all 12 marker Y computations go
  through — they run over the VIEWPORT index, and `ctx.Data` is Heikin-Ashi-transformed
  for the main pane, so an anchored marker follows the transformed bars.
- **Bool indicator parameters were silently dropped app-wide.** `FormatParam` writes
  `"true"`, and `IndicatorModelFactory` required `double.TryParse`. Fixed with
  `TryParseParamValue`, which accepts bools first. This had been quietly disabling
  Cipher SR's AdaptiveBreak and Cipher B's UseAnchorSuppression as well.
- **`CipherSrLevelProvider` had a 15-bar lookahead** relative to the provider it mirrors.
  Backtest-only; live was unaffected because live never has the future bars. Correcting
  it turned +0.739R at p=0.0002 into −0.095R at p=0.23 — a reminder that a spectacular
  backtest result is a bug report until proven otherwise.

### Chart legibility rules (2026-07-27)

Three rules that only reveal themselves when several indicators share one chart.
Reviewing a feature alone will not surface any of them.

- **The pane legend ranks before it truncates.** `ChartRenderer.BuildLegendRows` is
  separated from the drawing call because choosing WHAT to name is the part that can be
  wrong. Row budget is derived from pane height (45%, floor 3, ceiling 9); rows sort base
  data → continuous lines → markers; a series with ≥3 marker components collapses to one
  row; leftover rows are announced as "+N more". The collapse is scoped by the series'
  start index, not by colour — matching on colour let one indicator's collapse delete an
  earlier indicator's row whenever two colours agreed.
- **Marker shape families are owned per indicator.** Market Structure = squares and
  crosses. Value Deviation = triangle → dot → diamond, where the shape encodes the tier
  and therefore cannot be reassigned. A new marker indicator must not reuse another's
  shape at a similar colour; `MarkerLegibilityTests` enforces it with a squared-RGB
  threshold of 30,000, which is calibrated on two pairs that genuinely failed on screen
  (#EF5350 vs #EF9A9A at 13,034, and #AB47BC vs #FF1744 at 23,760). Judgement about
  whether two colours "obviously differ" proved unreliable at glyph size, and the
  colour-blind palette makes it worse.
- **Density controls hide glyphs, never information.** Value Deviation's `MinTier`
  suppresses the mark but leaves the Deviation Tier component, the reference lines and
  the spoken detail untouched. A screen-reader user must still be able to ask a bar what
  it was. Any future thinning must follow the same split.

**`SKCanvas.DrawRect` gotcha.** The four-float overload is `(x, y, width, height)`. Passing
`(left, top, right, bottom)` compiles, throws nothing, and paints a rectangle sized by the
coordinates themselves — which is how an 8px legend swatch and a 4x3px axis tick spent a long
time drawing as canvas-wide colour blocks that read as intentional styling. Prefer
`SKRect.Create(x, y, w, h)` or an explicit `SKRect` at every call site so the intent is on the
page. `MarkerSizingTests` renders into a real `SKSurface` and measures the painted bounding
box, which is the only kind of test that catches this class of bug.

**Markers are clamped against bar width.** `StandardRenderers.ClampMarkerSize` caps every glyph
at 2.2x bar width with a 3px-per-density floor. Thickness is authored for a normal zoom, so
without this a 330-bar view draws marks four times a candle wide. Any new marker renderer must
route its size through it.

**Legend keys mirror the render type.** `ChartRenderer.LegendRow` carries a `LegendGlyph`
(Line / Marker / Fill) plus the series' dash style, stroke width, marker shape and every
distinct colour in a collapsed marker family. A uniform coloured square for every row is what
made the legend uninformative — it communicated colour and nothing else. Colliding labels are
prefixed with the owning indicator's short name so two indicators' "Resistance" rows do not
read as one entry listed twice.

**`ClampMarkerExtent` takes a FULL extent.** The marker renderers disagree about their size
variable: a triangle's `arrowSize` is the whole height; a square's `half`, a diamond's `half`,
a cross's `arm` and a dot's `radius` are half of it. Half-extent callers must use
`ClampMarkerHalfExtent` — passing a half-extent to the full-extent clamp draws at twice the cap,
which is exactly what happened on the first pass and why the squares still looked heavy.

**Nothing long goes in a toolbar `<select>`.** A select sizes to its widest option, so one
verbose entry reflows the whole toolbar and moves controls out from under a Tab-order user.
`MarketOrchestrator.ApiKeyRequiredSentinel` is deliberately terse and budget-tested; the full
text lives in `ApiKeyRequiredHelp` and reaches the user through the tooltip, an inline action
button, and a spoken announcement. The symbol dropdown is additionally width-capped, because
long real tickers (options contracts) cause the same reflow.

## 16. Theming covers the whole window (2026-07-27)

The chart is Skia and reads `ChartTheme`; every toolbar, tab, dialog and label around it is
HTML. Until this landed the HTML half read a fixed `:root` block, so a theme stopped at the
canvas edge and the light theme produced a white chart in a near-black frame.

- **`ChartTheme` carries an application-chrome palette** (`SurfaceRaised`, `SurfaceSunken`,
  `TextPrimary`, `TextMuted`, `ChromeBorder`, `Accent`, `ButtonNeutral`) alongside the chart
  colours. Optional with defaults, so an un-dressed theme still renders.
- **`ThemeCssBridge` (Core/Services/Theming) is the mapping**, pure and static so it is
  testable without a DOM. `MainLayout` applies it on first render and on `ThemeChanged` via
  `accessibleTrader.applyThemeVariables`. **Any variable added to
  `ThemeCssBridge.VariableNames` must also be declared in BOTH `app.css` `:root` blocks** —
  the fallback is what keeps the app styled if JS interop fails, and a test enforces the match.
- **Never hard-code a chrome colour in CSS.** Use the variables. The reason the seam existed
  for so long is that each new dialog reached for `#1e1e1e` because that is what the file
  already did.
- **The focus ring is derived, not fixed.** `FocusRingFor` picks from the chrome's luminance;
  the old fixed `#ffff00` was invisible on the light theme, and an invisible focus ring is the
  same as no keyboard navigation for a low-vision user.
- **Background gradients span `RenderContext.GradientRect`** (the whole stacked-pane area),
  not `PaneRect`. Per-pane anchoring restarted the fade in every pane and put a hard seam
  above the volume pane.
- **Contrast checks: use luminance for neutrals, RGB distance for anything saturated.** A
  luminance-only check failed `#DD0000` on a `#383C42` background — within 0.05 in brightness
  yet obviously distinct, because the separation is chroma. Grid lines get a BAND check
  (visible but subordinate) rather than a contrast floor; they are supposed to sit close to
  the background.

**Price action follows the THEME; everything else follows its component.** Candle bodies,
wicks and volume bars resolve their colour from `ChartTheme`, not from the component's
`ColorHex` — that hex comes from indicator metadata and is a hardcoded TradingView teal, which
made the theme's candle colours dead code for years. `ComponentConfig.IsUserStyled` is the
escape hatch: the properties dialog sets it on a hand-picked colour, and the renderer then
defers to the component. Among directional bars only `ComponentRole.Volume` / `PriceAction`
follow the candle palette; a MACD histogram is not price direction.

**The window is one gradient.** `.app-container` carries it; toolbars, tab bar and indicator
bar are transparent and ride on it; the Skia canvas paints the middle slice via
`ChartTheme.Background` → `BackgroundGradientEnd`. Giving any chrome region its own opaque
fill puts the stacked-boxes seam back.

**Three theme bands, and the switch that unifies them.** `ChartTheme` colours the toolbar band
(`SurfaceRaised` → `ChromeTopEnd`), the canvas (`Background` → `BackgroundGradientEnd`) and the
footer band (`ChromeBottom` → `ChromeBottomEnd`) independently — a walnut header over a
near-black chart is expressible. A theme achieves one continuous window fade by sharing the
boundary values; `UnifiedGradient.Apply` does that arithmetic for the user-facing switch. Its
stops are NOMINAL proportions, not measurements: band colours are decided before layout, and
Skia and CSS each paint their half without knowing the other's height. Adjacent bands always
share their boundary value, which is the property that actually removes a seam.

**Up/down colour is an app preference, not a theme property.** `SettingsKeys.BullishColor` /
`BearishColor` layer over whatever theme is active. Which colour means "up" is a habit carried
between themes; absent values leave each theme's own pair, which is how High Contrast Dark
keeps its deliberate white-on-red.

**`ThemeFields` is the catalogue; the theme editor generates itself from it.** Adding a themeable
colour means one entry — key, group, label, description, getter, setter — and it appears in the
editor, in saved theme files and in the round-trip tests at once. `ThemeCustomizationTests` fails
if a `ChartTheme` colour has no entry, because "themeable in the renderer, invisible in the UI" is
the exact gap the editor exists to close. **Field keys are on disk in every saved theme — add and
deprecate, never rename.**

**A saved theme is a base plus a sparse override set.** Never a full snapshot: a snapshot cannot
know about a colour added later, so every future field would reach every old theme as black. An
explicitly stored null clears an optional field ("flat, no gradient"); an absent key means "not
customised". Unknown keys are ignored on load so a file from a newer version still works.

**The editor uses `BaseThemeResolver`, not `ThemeService.Current`.** The running application wants
the theme as the user will see it — their up/down colours, their background override. An editor
showing that would present personal preferences as theme properties and then save them into the
theme.

**A new shortcut must be checked against the REMAPPED profile.** The Linux WebHost rewrites every
`Ctrl+Shift+letter` chord to `Alt+Shift+letter` at startup (`WebHostShortcutRemap`), so any new
`Alt+Shift+letter` binding collides with a drawing tool for every WebHost user while the default
profile stays clean. `ShortcutConflictTests` checks the shipped profile;
`WebHostShortcutConflictTests` checks the one people actually run. Three-modifier chords
(`Ctrl+Alt+Shift+…`) are untouched by the rewrite and are the safe family for anything global.

**Pointer coordinates are pane-relative, not canvas-relative.** `DrawingInteractionManager`
translates through `ISplitViewCoordinator.ActiveChartFraction` before any mapping runs, and drops
events outside the active pane. The fraction is normalised because pointer coordinates arrive in
CSS pixels while the canvas is painted in device pixels — a pixel rect would need a density the
mouse layer never sees.
