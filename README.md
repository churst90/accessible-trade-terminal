# Accessible Trading Terminal

A professional-grade trading and analytics platform built on **.NET 10 MAUI Blazor Hybrid**, specifically engineered for blind and visually impaired traders. It combines high-performance data processing with a "Hybrid Voice" architecture, merging real-time sonification (audio-mapped trends) with synchronized speech feedback via native screen reader integration.

## Core Philosophy

Traditional trading software relies on visual density. The Accessible Trading Terminal flips this paradigm, treating **Market Data as a Soundscape**. It uses spatial audio, frequency-mapped trends, and "audio textures" to allow a user to feel the market structure, volatility, and momentum without sight.

## High-Level Architecture

The terminal is built on a decoupled **Orchestrator Pattern**:

- **MarketOrchestrator:** App-level market/provider/symbol selection pipeline. Cascade: `SelectedMarket` → providers → symbols → `LoadChartAsync()`.
- **DataOrchestrator:** Network resilience facade. Wraps historical fetcher and live stream manager with Polly circuit breaker and retry.
- **DataOrchestrationService:** Indicator pipeline coordinator. Reacts to `DataManager.DataUpdated` and `InitStatus == Ready` to trigger `IndicatorOrchestrator`.
- **IndicatorOrchestrator:** Computes all active indicators, profiles, and heatmaps. Dispatches results to the store.
- **SonificationManager:** Single authoritative audio path for navigation sonification. Observes `StateStream` directly; calls `NavigationSonifier.SyncNavigationSlots()` on every navigation change.
- **AccessibilityFeedbackCoordinator:** Observes `StateStream` and `FeedbackRequestEvent` (EventBus). Routes speech to `SpeechFeedbackRouter` and audio earcons to `AudioFeedbackRouter`. This is the "Brain" for all user-facing feedback.

## Rendering Stack

`MainPage.xaml` hosts a Grid with two layers:
1. **`SKCanvasView` (layer 0, bottom):** SkiaSharp renders the chart natively. All chart drawing goes here.
2. **`BlazorWebView` (layer 1, top, transparent):** Blazor UI chrome (toolbar, modals, status) overlays the canvas.

**Important:** `UseSkiaSharp()` must be present in `MauiProgram.cs`. `SkiaSharp.Views.Blazor` is NOT used (removed — caused WebGL crash).

## Key Subsystems

### Multi-Source Data Engine

- **Provider Architecture:** Decoupled plugin system. Six providers: **Binance** (Spot+Futures, WebSocket), **Alpaca** (REST, Stocks+Crypto), **Bitstamp** (REST+WebSocket), **Coinbase** (REST, JWT auth pending), **Polygon** (data-only, Stocks/Forex), **FRED** (Federal Reserve macroeconomic data).
- **Resilient Pipeline:** Polly exponential backoff, circuit breakers (10 failures → 5s break), and automatic reconnection.
- **Zero-Allocation Math:** `readonly record struct Ohlcv` for all price data. Indicator hot-paths use `double[]` arrays with `double.NaN` for missing values.
- **State Machine:** `DataOrchestrator` manages `DataState` lifecycle: `Initializing → HistoricalFilling → GapFilling → LiveStreaming → Faulted`.

### Hybrid Sonification Engine (Custom DSP)

- **Pure C# Audio Engine:** Custom DSP engine in `AudioEngine.cs`. No NAudio for synthesis — ultra-low latency, no OS-level MIDI overhead.
- **64-Voice Polyphonic Oscillator:** Sine, Square, Saw, Triangle waveforms with ADSR envelopes and real-time parameter modulation.
- **Voice Slot Layout:** Slots 0–7 = navigation/data. Slots 16–31 = UI earcons (independent of navigation voice).
- **Dynamic Panning:** Spatial stereo panning based on viewport position (left edge → hard left, right edge → hard right).
- **Profile/Heatmap Sonification:** Structural role-based pitch (POC = 880 Hz sine, LVN = 220 Hz, etc.). Heatmap uses sawtooth for perceptual distinction.
- **Single Navigation Path:** ALL navigation audio flows through `SonificationManager` → `NavigationSonifier.SyncNavigationSlots()`. No other path writes to voice slot 0.

### Universal Keyboard Navigation

- **Global Input Routing:** `GlobalInputService` (JS `[JSInvokable]` bridge) → `BlazorInputService` → `ShortcutManager` → `CommandDispatcher` → `NavigationEngine` or `WorkspaceStore`.
- **Android:** `MainActivity.cs.DispatchKeyEvent()` → `IInputService.ProcessKey`.
- **Navigation Engine:** String command processing (`"NAV_LEFT"` etc.) → `NavigateAction` dispatch → `FeedbackRequestEvent` publish.
- **Help:** Press `Alt+H` to open the built-in Help dialog (keyboard reference + usage guide).

### Advanced Accessibility Cluster

- **Native Speech Integration:** Announcements to screen readers (NVDA, JAWS, Narrator, VoiceOver, TalkBack) via ARIA live regions (`aria-live="assertive"` double-buffer in `MainLayout.razor`).
- **Object Tree:** Hierarchical view to manage chart layers, indicators, and drawings (`Alt+O`).
- **Tactile Virtual Canvas:** Virtual buffer for tactile displays (Monarch, Graphiti) — `MonarchTactileDriver` skeleton; full implementation in Phase 7 roadmap.

## EventBus vs Rx — Quick Reference

See `CODEBASE_KNOWLEDGE_BASE.md` Section 5 for the full authoritative decision. Summary:

- **EventBus:** Cross-layer, fire-and-forget events — modal open/close commands, feedback routing, alerts, hardware input events from JS bridge.
- **EventBus.AsObservable<T>():** When you need Rx operators (Throttle, DistinctUntilChanged) on an EventBus event.
- **Direct Rx (BehaviorSubject/Subject):** Intra-service continuous state streams — `StateStream`, `DataStream`, `StateChanged`.
- **System.Threading.Channels:** High-frequency live tick data (already implemented in `DataOrchestrator.LiveStream`).
- **Never:** Route raw `Ohlcv` ticks through EventBus. Never use EventBus inside `AudioEngine.GenerateBuffer`.

## Keyboard Shortcuts — Quick Reference

Press `Alt+H` in the application to open the full Help dialog. Key bindings:

- `Left/Right Arrow` — Navigate data points (X axis).
- `Up/Down Arrow` — Navigate components within a series (Y axis).
- `Page Up/Down` — Switch between chart series.
- `Home/End` — Jump to viewport start/end. `\` — Jump to live edge.
- `[ / ]` — Pan viewport. `- / =` — Zoom in/out.
- `Space` — Play chart. `Shift+Space` — Play series. `Ctrl+Shift+Space` — Play component. `Ctrl+Space` — Pause/resume.
- `F1` — Settings. `F2` — Toggle speech. `F3` — Toggle sonification. `F4` — Context summary.
- `F5/Shift+F5` — Component volume up/down. `F6/Shift+F6` — Series volume. `F7/Shift+F7` — Master volume.
- `Alt+Up/Down` — Scroll indicator pane list when more panes are open than fit on screen.
- `Ctrl+Alt+Shift+C` — Focus chart + announce context summary.
- `Alt+C` — Toggle Heikin-Ashi candles. `Alt+L` — Toggle log scale.

## Current Status (2026-04-05)

**Phases 0–9 + Phase 10-A through 10-G + Improvement Plan Phases B–L all complete. Build: 0 errors, 0 warnings. Tests: 236/236 passing.**

### Completed Phases

- **Phase 0** — Documentation overhaul: README, CHANGES, TODO, CODEBASE_KNOWLEDGE_BASE, PLATFORMS.
- **Phase 1** — Accessibility path bugs: dual sonification path unified, chart-focus gate, loading-state speech, boundary earcons.
- **Phase 2** — Data pipeline bugs: PlaybackScope differentiation, indicator pipeline timing verified.
- **Phase 3** — Structural cleanup: EventBus rationalization, HelpModal + User Guide consolidated.
- **Phase 4** — SRP refactoring: CommandDispatcher, DrawingService, SkenderIndicatorProvider, WorkspaceStore reducers.
- **Phase 5** — Pane Layout UX: drag-handle pane resize, Alt+Up/Down pane scroll, Ctrl+Alt+Shift+C chart focus, pane ratio persistence.
- **Phase 6** — Provider order streams: Binance user data stream (listenKey + keepalive), Bitstamp private order channel (HMAC-SHA256).
- **Phase 7** — Platform audio + input: Android AudioTrack driver, iOS/Mac Catalyst AVAudioEngine driver, Mac Catalyst hardware keyboard (`KeyboardPageHandler`), strategy backtester UI.
- **Phase 8** — Indicator pane rendering: multiple same-type indicators, 80px pane height floor, full-height crosshair, OB/OS/zero reference levels always on-screen, settings/alert/workspace persistence, 69 tests total.
- **Phase 9** — Silent bug fixes: alert crossover detection, indicator context analyzer, bar detail lookback, F8 removal.
- **Phase 10-A** — Per-bar coloring, new display types (Dot/Arrow/StepLine/Cloud/Gradient), mute/volume persistence, AudioEngine noise oscillator.
- **Phase 10-B** — Sound Designer: `SoundPatch` model, earcon customisation, waveform preview, `.atpkg` sharing.
- **Phase 10-C** — BarDetailService full coverage, keyboard shortcut table in Settings, iOS hardware keyboard, Coinbase/Polygon zero-value filter, `StrategyIndicatorCache`.
- **Phase 10-D** — Custom Indicator Platform: Roslyn scripting sandbox, `.atpkg` import/export, indicator sharing.
- **Phase 10-E** — PineScript transpilation: pattern-based transpiler, Tier 1 core mapping, Tier 2 var/na/nz, Tier 3 stubs.
- **Phase 10-F** — Strategy platform extension: custom C# strategy tab (Roslyn), Accessible Cipher B (`CipherBProvider`), `CustomIndicatorRegistry`.
- **Phase 10-G** — Indicator architecture: self-describing metadata, new marker shapes, oscillator audio rule, indicator preferences service, Ctrl+L/R generalised, sub-pane architecture, Cipher A, Cipher SR, workspace load fix.
- **Cipher B MCB** — CipherBProvider single-pane layout with Money Flow histogram, dual-wave WT oscillator, cross dots, Laguerre RSI, Stoch, VWAP oscillator, full bell taxonomy sonification.
- **Cipher C** — Ehlers Cyber Cycle bandpass oscillator, Fisher Transform, Hull MA Lead Sine, 3-tier signal classification (Triple/Double/Single top/bottom), Shallow Peak/Trough trend signals, Cycle Fill cloud. 57 tests.
- **Cipher S** — Sentiment candle-color overlay using high-low channel normalization with 5th/95th percentile clipping and 3-bar EMA smoothing. 11 sentiment phases (Max Fear → Max Euphoria). Auto-detects cycle window via `IAdaptiveIndicatorProvider`. Incremental tick update (`RequiresFullRecalcOnTick = false`).
- **Spider Lines** — 8 Fibonacci-period EMA web (8/13/21/34/55/89/144/200) on price pane. Warm→cool color gradient. `GetDetailFact` reports EMA stack count below price.
- **Viewport Right Margin** — `RightMarginBars = 20` reserves empty future-space slots on the right for trendline projection. Fixed left-side bar compression by removing `xOffset` right-alignment from `ChartRenderer`. `ViewportNavigationService` fully rewritten to honour margin in all pan/zoom/navigate/clamp operations.

### Improvement Plan (all complete as of 2026-03-31)

- **Phase B** — Bell synthesis foundation: `ISoundPatchRegistry` with 6 built-in patches (sine_bell, triangle_bell, crystal_bell, detuned_pair_bell, gradient_blend, dual_tone_bell), `PlaybackLayer` enum (Background/Midground/Foreground), `DecayMs` per-component decay override.
- **Phase C** — Accessible Cipher A (`CipherAProvider`): 8 self-describing components (WT Momentum ribbon, Buy/Sell dots, Bullish/Bearish Divergence diamonds, Blood Diamond, Manipulation X, Exhaustion X) with full bell patch and gradient speech metadata.
- **Phase D** — Cipher B sonification redesign: bell taxonomy throughout all signal dots, dual_tone_bell for Triple Confluence Buy, volume layering via PlaybackLayer.
- **Phase E** — Cipher SR sonification: crystal_bell for pivot dots, zone hum in NavigationFeedbackManager, IsZoneLine flag.
- **Phase F** — Cluster/shapes-as-ticks: N marker signals on a bar produce N audio ticks 100 ms apart in significance order.
- **Phase G** — Contextual speech: `SignalSpeechTemplate` on `ComponentConfig`, provider-declared signal descriptions via `GetComponentSpeech`, multi-signal sequencing in NavigationFeedbackManager.
- **Phase H** — Cloud sonification: `CloudSonificationConfig` on `CloudFillConfig`, AudioSequencer cloud pass (slots 64–79), EMA Fill + WT Fill + Ichimoku cloud audio wired.
- **Phase I** — Drawing Coordinate Entry: keyboard-first anchor placement via CE mode (`Enter` to set each anchor, `Escape` to cancel), TTS price+timestamp feedback, change-from-anchor speech.
- **Phase J** — Ctrl+Left/Right redesign: context-aware crossing (ZeroLine/Threshold/MACross/Band/Trendline/SparseMarker) dispatched from `HandleTrendlineCrossJump`.
- **Phase K** — Ichimoku Kinko Hyo indicator: 5 components, Kumo cloud fill with 520/180 Hz sonification, displacement-shifted arrays, `GetDetailFact` context speech.
- **Phase L** — Test coverage expansion: 69 → 146 tests across 9 new test files covering all Phase B–K systems.

### Architecture Highlights (Phase 10-G + Improvement Plan)

#### Self-Describing Indicator Metadata
`IndicatorComponentMetadata` carries optional `Default*` visual and audio hints. `IndicatorModelFactory.CreateComponentConfigFromMeta` applies provider hints first, falls through to `IStylingService` role/type defaults. `StylingService` is now purely role/type-based with no indicator names.

#### 3-Layer Component Merge
`IndicatorModelFactory.CreateSeriesFromMetadata` applies: Layer 1 = provider metadata defaults; Layer 2 = workspace state-only (visibility/mute/volume/FreqMultiplier); Layer 3 = `IIndicatorPreferencesService` (JSON prefs at `%LOCALAPPDATA%\AccessibleTrader\IndicatorPrefs\{CODE}.json`). "Save as Defaults" in Properties dialog (Shift+F12) persists to Layer 3.

#### Sub-Pane Architecture
Any component can declare `SubPaneName` / `SubPaneHeightRatio` in `IndicatorComponentMetadata`. `ChartRenderer.RenderPane` performs multi-pass rendering: main area (top) + sub-pane strips (bottom, clamped 5–40% each). `ViewportRangeCalculator` accumulates ranges under composite keys (`"PaneName/SubPaneName"`).

#### Bell Synthesis and Sound Patches
`ISoundPatchRegistry` (not `ISoundPatchLibrary`) provides code-defined bell presets used by indicator providers. 6 built-in patches with distinct harmonic, decay, and detuning characteristics. Components declare `DefaultSoundPatchId` in metadata; `AudioSequencer` and `NavigationSonifier` resolve patch for decay and detuning parameters.

#### Context-Aware Ctrl+Left/Right Navigation
`CommandDispatcher.HandleTrendlineCrossJump` dispatches to one of six crossing strategies based on focused component type: trendline crossing (price series), sparse signal jump (Dot/Diamond/Cross etc.), zero-line crossing (MACD etc.), threshold crossing (RSI/Stoch etc.), MA crossover (EMA/SMA overlays), band boundary crossing (Bollinger %B).

### Platform Status

| Platform | Chart | Audio | Keyboard | Trading |
|---|---|---|---|---|
| Windows | ✅ WASAPI | ✅ | ✅ JS bridge | ✅ All providers |
| Android | ✅ | ✅ AudioTrack | ✅ DispatchKeyEvent | ✅ |
| iOS | ✅ | ✅ AVAudioEngine | ⚠️ On-screen only | ✅ |
| Mac Catalyst | ✅ | ✅ AVAudioEngine | ✅ KeyboardPageHandler | ✅ |

See `TODO.md` for the full Phase 10 roadmap and `PLATFORMS.md` for platform-specific details.

## Development

Built with **.NET 10 MAUI Blazor Hybrid**.

- **Core:** `AccessibleTrader.Core` — Business logic, custom DSP engine, Orchestrators.
- **UI:** `AccessibleTrader.BlazorClient` — MAUI host, Blazor WebView, SkiaSharp rendering (SKCanvasView).
- **SDK:** `AccessibleTrader.Sdk` — Plugin contracts and immutable performance models.
- **Plugins:** `Plugins/` — Six independent exchange/data providers.
- **Tests:** `AccessibleTrader.Tests` — Unit and integration diagnostics (236 tests, all passing).
