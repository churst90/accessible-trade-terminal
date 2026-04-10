# AccessibleTrader: Comprehensive Codebase Knowledge Base

This document is the authoritative technical reference for the AccessibleTrader project. It is designed to give an AI assistant or new team member the deep architectural context, design patterns, and platform-specific nuances required to maintain and evolve the system as a Senior .NET Engineer.

---

## 1. Core Vision & Mandate

**Purpose:** A professional-grade trading terminal engineered exclusively for the blind and visually impaired.
**Primary Feedback Loop:** Sonification (Audio) + Speech (TTS) + Tactile (Haptic/Physical) > Visuals.
**Goal:** Full information-density parity between visual charts and audio/tactile representations.

---

## 2. Technology Stack & Platform

- **Runtime:** .NET 10 (Target: `net10.0`).
- **Framework:** MAUI Blazor Hybrid (Host: MAUI, UI: Blazor WebView).
- **Target Platforms:** Windows (mature), Android (beta), macOS (stub), iOS (stub).
- **Rendering:** SkiaSharp on a native MAUI `SKCanvasView`. The `SKCanvasView` sits at Grid layer 0 in `MainPage.xaml`. The `BlazorWebView` is at layer 1 (transparent) overlaying it. **Do NOT use SkiaSharp.Views.Blazor — it was removed because it caused a WebGL crash.**
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
- **Audio:** `AudioEngine.cs` — Raw DSP logic (64-voice polyphonic oscillators, ADSR, panning).
- **Accessibility:** `AccessibilityFeedbackCoordinator`, `NavigationEngine`, `SpeechFormatter`, `ProfileBinClassifier`.
- **Input:** `CommandDispatcher`, `InputRouter`, `ShortcutManager`.
- **Rendering:** `ChartRenderer` coordinates `BackgroundLayer`, `DataLayer`, `OverlayLayer`, `ProfileRenderLayer`, `HeatmapRenderer`.

### `AccessibleTrader.BlazorClient`
- **Role:** The "Presentation and Driver" layer. Only platform-specific code lives here.
- **UI:** Razor components, CSS (Dark Mode priority), JS Interop.
- **Drivers:** `BlazorAudioDriver` (IAudioDriver impl), `BlazorSpeechManager` (ISpeechManager impl), `BlazorInputService` / `GlobalInputService` (IInputService impl), `MauiSecureStorageService`.
- **Rule:** No business logic in Razor components. Components are pure presenters + event forwarders.

### `Plugins/`
- **Role:** Exchange-specific integrations. Each implements `IMarketDataProvider` from Sdk.
- **Providers:** Binance (Spot+Futures, WebSocket), Alpaca (REST, Stocks+Crypto), Bitstamp (REST+WebSocket), Coinbase (REST, JWT auth STUB), Polygon (data-only), FRED (macroeconomic data-only).

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

- **Architecture:** `AudioEngine` generates raw `float[]` buffers. Platform drivers (`BlazorAudioDriver`) push these to WASAPI (Windows) / AudioTrack (Android, TODO) / AVFoundation (iOS, TODO).
- **Oscillators:** Sine, Square, Sawtooth, Triangle with real-time frequency modulation and interpolation.
- **Voice Slots:** 64 total. Slots 0–7 = navigation/data sonification. Slots 16–31 = UI earcons (played via `PlayNote`).
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
  - F1 = OpenSettings; F2 = ToggleSpeech; F3 = ToggleSonification; F4 = ContextSummary.
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
        ISignalCatalog                      ─── Walks every provider at startup,
              │                                  emits one SignalDescriptor per
              │                                  IndicatorComponentMetadata.
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
