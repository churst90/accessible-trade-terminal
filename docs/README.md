# Accessible Trading Terminal

A professional-grade trading and analytics platform specifically engineered for blind and visually impaired traders. It combines high-performance data processing with a "Hybrid Voice" architecture, merging real-time sonification (audio-mapped trends) with synchronized speech feedback via native screen reader integration.

The same Razor component library is hosted two ways:

- **.NET 10 MAUI Blazor Hybrid** on Windows / macOS / iOS / Android. Native `SKCanvasView` chart overlay, platform-native audio (WASAPI / AudioTrack / AVAudioEngine), and platform-native screen reader integration.
- **ASP.NET Core Blazor Server (`AccessibleTrader.WebHost`)** on Linux and any browser-reachable target. Same `ChartRenderer` paints to an in-memory SKBitmap that's PNG-encoded and streamed to an `<img>` element; speech routes through Orca's D-Bus `PresentMessage` (respecting the user's voxin/SpeechDispatcher voice config), with `spd-say` and browser `SpeechSynthesis` as fallbacks. As of v1.2.0 the WebHost is **multi-user** (every browser connection is its own DI scope → isolated session), and as of v1.3.0 it can run as a logged-in, **paper-trading education terminal** (`--accounts`): self-hosted accounts persist each user's settings, sound design, workspaces, and paper record, with real-money trading reserved for the desktop client. It's also the deploy target for the public-website chart demo. See [`WEBHOST_MULTI_USER_SCOPING.md`](WEBHOST_MULTI_USER_SCOPING.md), [`HOSTED_ACCOUNTS_STRATEGY.md`](HOSTED_ACCOUNTS_STRATEGY.md), and [`SERVER_SETUP.md`](SERVER_SETUP.md).

Both hosts share the platform-agnostic `AccessibleTrader.Core` business logic, the `AccessibleTrader.BlazorClient.Components` Razor Class Library, and the 29 provider/analytics plugins. The MAUI/desktop head is unchanged by the WebHost and stays single-user (Singleton-scoped) — the host-specific code paths are runtime-gated on `IRuntimePlatform.IsBrowserHost`, and the per-circuit scoping lives only in the WebHost's DI registration.

## Download

Pre-built binaries are on the [Releases page](https://github.com/churst90/accessible-trade-terminal/releases) (latest release: **v2.0.1** — accessibility polish on top of the 2.0 milestone: accessible mobile drawing, touch series navigation, sparse-signal speech, an opt-in gradient background, and a new keyless Deribit crypto-options volatility provider; see [`WHATSNEW.md`](WHATSNEW.md) and `ROADMAP_2.0.md`). The cross-platform **WebHost** — `linux-x64`, `win-x64`, `osx-x64`, `osx-arm64`; run it and it opens in your browser — is the recommended distribution. Native MAUI desktop builds for Windows and macOS are also attached but are **unsigned** (expect a SmartScreen/Gatekeeper prompt). See [`PLATFORMS.md`](PLATFORMS.md#which-version-to-use) for which to choose. Build from source with `dotnet run --project AccessibleTrader.WebHost` (Linux) or the MAUI workloads (Windows/macOS).

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

**Under MAUI:** `MainPage.xaml` hosts a Grid with two layers:
1. **`SKCanvasView` (layer 0, bottom):** SkiaSharp renders the chart natively. All chart drawing goes here.
2. **`BlazorWebView` (layer 1, top, transparent):** Blazor UI chrome (toolbar, modals, status) overlays the canvas.

`UseSkiaSharp()` must be present in `MauiProgram.cs`. `SkiaSharp.Views.Blazor` is NOT used in the MAUI head (and was found to be unusable under Blazor Server too — depends on WASM-only `System.Runtime.InteropServices.JavaScript`).

**Under the WebHost (Linux + public website):** `ChartArea.razor` renders an inline `<img>` whose `src` is a base64-encoded PNG. The same `ChartRenderer.Render(SKCanvas, ...)` paints to an off-screen `SKBitmap`; PNG-encoded; pushed to the browser via SignalR. Throttled to ~10 fps via a Reactive subject so live ticks don't flood the circuit. Both rendering paths are guarded by `IRuntimePlatform.IsBrowserHost`, so MAUI keeps its native overlay and WebHost keeps its server-rendered surface — no cross-talk.

## Key Subsystems

### Multi-Source Data Engine

- **Provider Architecture:** Decoupled plugin system with **29 data providers** — 14 in `Plugins/Providers/` (trading) and 15 in `Plugins/Analytics/` (analytics) — plus `Plugins/Indicators/` for drop-in indicator DLLs. **Trading providers:** Binance (Spot+Futures, direct REST+WebSocket), Bitstamp (REST+WebSocket), Coinbase (REST, JWT auth), Alpaca (REST, Stocks+Crypto), Polygon (Stocks/Forex), Kraken, Finnhub, Oanda, Tradier, TwelveData, InteractiveBrokers, FMP (Stocks/Crypto/Forex/Commodities/Indices), Schwab (OAuth2, US stocks + options), MEXC (Spot+Futures, WebSocket — ToS-flagged for US users). **Analytics providers:** FRED (macroeconomic), FMP Analytics (fundamentals/ratios/earnings/economic calendars), CoinGecko (dominance/market cap), AlternativeMe (Fear & Greed), Glassnode (on-chain), OkxDerivatives (funding/OI), BinanceDerivatives (live REST funding/OI), **BinanceVision (free `data.binance.vision` monthly archives, ~6 years of funding + OI history, zero cost, no API key — primary source for Core FundingRate/OpenInterest/CrowdingIndex)**, BGeometrics (154+ BTC on-chain metrics), CoinMetrics (multi-asset MVRV/addresses/flows), DefiLlama (DeFi TVL/stablecoins), Mempool (BTC mempool/hashrate/difficulty), Etherscan (ETH gas/supply), CFTC (Commitment of Traders weekly fund positioning — free Socrata API, no key), FINRA (daily short-sale volume ratio for US equities — free Reg SHO files, no key), Deribit (crypto-options DVOL volatility index + realised volatility for BTC/ETH — public v2 API, no key).
- **Resilient Pipeline:** Polly exponential backoff, circuit breakers (10 failures → 5s break), and automatic reconnection.
- **Zero-Allocation Math:** `readonly record struct Ohlcv` for all price data. Indicator hot-paths use `double[]` arrays with `double.NaN` for missing values.
- **State Machine:** `DataOrchestrator` manages `DataState` lifecycle: `Initializing → HistoricalFilling → GapFilling → LiveStreaming → Faulted`.

### Hybrid Sonification Engine (Custom DSP)

- **Pure C# Audio Engine:** Custom DSP engine in `AudioEngine.cs`. No NAudio for synthesis — ultra-low latency, no OS-level MIDI overhead.
- **128-Voice Polyphonic Oscillator:** Sine, Square, Saw, Triangle, Noise waveforms with ADSR envelopes and real-time parameter modulation. Raised from 64 (the old 64-bit dirty-slot mask structurally capped polyphony at slot 63); the extra headroom lets many series/components and cloud/ribbon fills (e.g. EMA Fill) play at once instead of being dropped.
- **Voice Slot Layout:** Slots 0–15 = navigation/data. Slots 16–31 = UI earcons (independent of navigation voice). Slots 32–95 = playback. Slots 96–127 = cloud/ribbon fills.
- **Dynamic Panning:** Spatial stereo panning based on viewport position (left edge → hard left, right edge → hard right).
- **Profile/Heatmap Sonification:** Structural role-based pitch (POC = 880 Hz sine, LVN = 220 Hz, etc.). Heatmap uses sawtooth for perceptual distinction.
- **Single Navigation Path:** ALL navigation audio flows through `SonificationManager` → `NavigationSonifier.SyncNavigationSlots()`. No other path writes to voice slot 0.

### Universal Keyboard Navigation

- **Global Input Routing:** `GlobalInputService` (JS `[JSInvokable]` bridge) → `BlazorInputService` → `ShortcutManager` → `CommandDispatcher` → `NavigationEngine` or `WorkspaceStore`.
- **Android:** `MainActivity.cs.DispatchKeyEvent()` → `IInputService.ProcessKey`.
- **Navigation Engine:** String command processing (`"NAV_LEFT"` etc.) → `NavigateAction` dispatch → `FeedbackRequestEvent` publish.
- **Help:** Press `F1` to open the built-in Help dialog (keyboard reference + usage guide).

### Advanced Accessibility Cluster

- **Native Speech Integration:** Announcements to screen readers (NVDA, JAWS, Narrator, VoiceOver, TalkBack) via ARIA live regions (`aria-live="assertive"` double-buffer in `MainLayout.razor`).
- **Object Tree:** Hierarchical view to manage chart layers, indicators, and drawings (`Alt+O`).
- **Tactile Display Output:** **All Dot Pad models are supported** — the **Dot Pad X** (newest) and the **second generation** — via the shared `DotPadSDK-3.0.0` graphics ABI (300-cell graphic area + 20-cell braille text strip). Renders a two-pane 50/50 graphic (top series + focused series) with 1-pin candles and dynamic-gap bar density, plus a minimal value/X-value strip and F1-F4 device-side speech queries. On-device testing so far has been on the second generation; the Dot Pad X uses the same SDK and binds without code changes. Driver is Windows-only via `DotpadTactileDriver` + `DotPadSDK-3.0.0.dll`; the SDK is not committed to the repo — see [PLATFORMS.md §7](PLATFORMS.md#7-tactile-display-support) for the install steps. Other tactile devices (APH Monarch, etc.) are not yet supported.

## EventBus vs Rx — Quick Reference

See `CODEBASE_KNOWLEDGE_BASE.md` Section 5 for the full authoritative decision. Summary:

- **EventBus:** Cross-layer, fire-and-forget events — modal open/close commands, feedback routing, alerts, hardware input events from JS bridge.
- **EventBus.AsObservable<T>():** When you need Rx operators (Throttle, DistinctUntilChanged) on an EventBus event.
- **Direct Rx (BehaviorSubject/Subject):** Intra-service continuous state streams — `StateStream`, `DataStream`, `StateChanged`.
- **System.Threading.Channels:** High-frequency live tick data (already implemented in `DataOrchestrator.LiveStream`).
- **Never:** Route raw `Ohlcv` ticks through EventBus. Never use EventBus inside `AudioEngine.GenerateBuffer`.

## Keyboard Shortcuts — Quick Reference

Press `F1` in the application to open the full Help dialog. Key bindings:

- `Left/Right Arrow` — Navigate data points (X axis).
- `Up/Down Arrow` — Navigate components within a series (Y axis).
- `Page Up/Down` — Switch between chart series.
- `Home/End` — Jump to viewport start/end. `\` — Jump to live edge.
- `[ / ]` — Pan viewport. `- / =` — Zoom in/out. (Also available as toolbar buttons, and you can click-drag the chart to pan.)
- `Space` — Play chart. `Shift+Space` — Play series. `Ctrl+Shift+Space` — Play component. `Ctrl+Space` — Pause/resume.
- `F1` — Help. `F2` — Toggle speech. `F3` — Toggle sonification. `F4` — Context summary. `F12` — Settings.
- `F5/Shift+F5` — Component volume up/down. `F6/Shift+F6` — Series volume. `F7/Shift+F7` — Master volume.
- `Alt+Up/Down` — Scroll indicator pane list when more panes are open than fit on screen.
- `Ctrl+Alt+Shift+C` — Focus chart + announce context summary.
- `Alt+C` — Toggle Heikin-Ashi candles. `Alt+L` — Toggle log scale.
- `Ctrl+Alt+Shift+J` — Open the Journal modal (review/copy every spoken phrase, alert, strategy setup, error from this session).
- `Alt+M` — Market watch (watchlists + screener). `Alt+R` — Respect report (which levels this market actually holds).
- `Ctrl+Alt+Shift+P` (or `F11`) — Bar replay on/off; `F9` / `Shift+F9` reveal/hide a bar, `F10` auto-advance.
- `Ctrl+Alt+Shift+S` — Split view on/off; `Ctrl+Alt+Shift+E` next tab in the second pane, `Ctrl+Alt+Shift+O` side-by-side/stacked.

Every one of these is also a toolbar button — row 1 opens panels, row 2 changes the chart.

## Current Status (2026-07-27)

**2.1.0 staged, not cut.** Version is bumped and everything is merged to `main`, but no tag
exists yet — see [`RELEASE_2.1.0_VERIFICATION.md`](RELEASE_2.1.0_VERIFICATION.md) for the
hand-verification pass that has to come first. Three specific gaps keep it staged: the MAUI
desktop head was never built this cycle (the development box lacks the workloads, and that
head carries its own `app.css`), fifteen dialogs had colours rewritten without being opened,
and none of the six new features has been run end to end.

What landed since 2.0.1, across 30 commits: **watchlists and a screener** (with a symbol
picker and a quick screen builder) reusing the Strategy Composer's own condition tree; a
**respect report** measuring which levels and moving averages a market actually holds; a
**Market Structure** indicator (HH/HL/LH/LL, BOS, CHoCH) on by default; a **Value Deviation**
indicator marking reversals relative to a rolling volume-profile POC; **bar replay** and
**split view**; toolbar controls for all of it; and an **application-wide theming system** —
a theme now covers the toolbars, tabs and dialogs as well as the chart — with three new
presets (Steel Gray as the default, Blackout, Classic). Plus real fixes: boolean indicator
parameters were silently dead app-wide, a Cipher SR backtest lookahead, two `DrawRect` bounds
bugs, a toolbar that never watched the state it displayed, and a colour-vision gap in three
themes that already shipped. Suite 2109 → 2507.

**2.0.1 shipped.** A point release on top of 2.0.0: accessible mobile drawing (a "Place
drawing point" touch button so touch-only users can complete multi-point drawings),
previous/next-series touch buttons, sparse-signal speech ("N signals in view" instead of
"no data" on indicators like Cipher B), an opt-in gradient chart background, a cloud-fill
crossover gap fix, and a new keyless **Deribit** analytics provider (DVOL volatility index
+ realised volatility for BTC/ETH). See [`WHATSNEW.md`](WHATSNEW.md).

**2.0.0 shipped.** The 2.0 hardening line reached release: every provider brought to a
reliable standard, every exchange moved to a direct API (no shared exchange SDK left in
the tree), background monitoring that never fails silently, and the broker/order path
made trustworthy. Since v1.5.0, main has landed: Tier 1 correctness
(workspace persistence for active strategies + drawings, exchange-native
brackets on Tradier/Schwab with per-broker honesty on Kraken, fill history on
all six trading brokers, alert-failure surfacing, pipeline race fixes), the
keyed-feeds pipeline refactor (per-chart data feeds, opt-in live background
tabs, instant warm tab switches, live bar-close strategy evaluation, the
kline volume-inflation fix) plus its adversarial hardening pass, order-
lifecycle voice completeness (cancels announced, bracket legs audible),
session autosave with resume-on-open, and while-you-were-away position
reconciliation with spoken P&L. See `CHANGES.md` and `ROADMAP_2.0.md` for
the full record.

## Previous Status (2026-04-24)

**Tier 3 TODO sweep complete 2026-04-24** (same-day as Tier 1 + 2). Six
substantive architectural items shipped: BuildSetupTab decomposed into
`ConditionTreeEditor` + `RiskPlanEditor` + `SummaryExport` siblings
under a ~70-line coordinator; `IStrategyModalCoordinator` wraps six
services and drops StrategyModal's DI count from 10 → 5;
`AudioEngine.SetVoice` hot path now zero-allocation (replaced
`wave.ToLower()` with `ParseWaveform` / OrdinalIgnoreCase);
`IEventBus.SubscribeCoalesced` / `SubscribeSampled` expose Rx
`Throttle` / `Sample` for burst coalescing; script worker gains CPU
quota (kills sustained >90% over the poll window) + 16-worker
concurrency gate with clear over-cap error; SMTP +
Telegram alert delivery channels (`IAlertChannel` contract,
`EmailAlertChannel`, `TelegramAlertChannel`, `AlertDeliveryService`
fan-out). Deferred: DLL plugin strategy loader (multi-day) and Settings
Alerts-tab UI (2-3h). 537/537 tests still green. See `CHANGES.md`
2026-04-24 Tier 3 entry.

**Tier 1 + Tier 2 TODO sweep complete 2026-04-24.** 10 items shipped —
Ctrl+L/R focused-series-aware refinement (focused trendline walks only
that drawing; continuous-points lines announce "No points of interest"
instead of silently sweeping all trendlines); CIPHER_A WT Momentum
Gradient now a queryable `SignalDescriptor` so strategies can gate on
momentum strength; `BarDetailService` layers Bollinger squeeze/expansion
and MACD-vs-Signal crossover narration after raw values; `AlertEvaluator`
resolves POC via `ILevelService` for live POC-crossing alerts; BuildSetupTab
surfaces Score + Sequence logic operators, `MinLevelStrength` for
level operators, expand/collapse on groups, and Within-N for every typed
variant; drawing anchors now land in the 20-bar future-space margin via
`DrawingCalculatorHelper.ResolveAnchorIndex`; VPVR backtest replay chain
pinned by 4 new tests. Later on 2026-04-24: Phase 5 accessibility rework
(`ChartArea` `@onkeydown` fallback dedupes with window JS handler;
`OrderBookModal` aria-live status region + per-direction earcons),
three-tier level-crossing earcons (`LevelCrossingMonitor` approach + sustained
on top of existing crossing), divergence pivot-to-pivot slanted line, cross-
pane Anchor regime tint on the price pane, Roslyn strategy persistence + v18
migration off BNVISION alias, cloud-sonification scoping rule codified (wave
fills between oscillator-pair boundaries stay visual-only), ARIA tree arrow-
key navigation with meaningful labels on strategy + object trees. 577/577
tests green. See `CHANGES.md` 2026-04-24.

**Phases 0–11 complete. Full-codebase audit 2026-04-23 complete end-to-end (Weeks 1–4).** 29 data providers (14 trading in `Plugins/Providers/`, 15 analytics in `Plugins/Analytics/` — Deribit crypto-options volatility (DVOL index + realised vol) joined 2026-07-26, indicator drop-in via `Plugins/Indicators/`). MEXC (JK.Mexc.Net) joined the trading tier on 2026-04-18 with spot + futures klines, order book, user-data stream, and adaptive-precision UI and speech formatters shipped across the chart pane, trading dashboard, strategy modal, and accessibility pipeline so sub-dollar assets (KAS, SHIB, PEPE) actually display and narrate with real precision. MACloudProvider supports 6 MA types (EMA/SMA/WMA/HMA/DEMA/TEMA). Cloud components are fully navigable with sonification, speech, and auto-narration. IAnalyticsDataResolver maps 30 metrics to best provider. TrailByAtr stop adjustment in backtester. `PluginTrustPolicy.RequireTrusted` defaults to `true` — unverified plugin DLLs are refused unless `ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS=1` is set for dev bypass. `ACCESSIBLETRADER_SCRIPT_IN_PROCESS` is honoured only in DEBUG builds; Release ignores the env var entirely. iOS refuses all `.atpkg` / Roslyn compile paths (no OS sandbox available). **All trading, analytics, and LLM providers route their `HttpClient` through `IPluginHttpClientFactory` with per-provider outbound-host allow-lists** (IBKR keeps its custom TLS-pinned handler). **As of 2026-07 the tree carries NO shared exchange SDK** — Binance and MEXC both call the exchange REST/WebSocket API directly (MEXC's spot WS is Protobuf, decoded from the official `mexcdevelop/websocket-proto` files via build-time codegen), so `CryptoExchange.Net` / `Binance.Net` / `JK.Mexc.Net` are gone and the flattening-clash hazard they carried is eliminated; a CI guard (`PluginDependencyIsolationTests`) fails the build if any two plugins ever resolve the same third-party assembly at different versions. Shared REST/signing (`RestSigning`), symbol shaping (`SymbolFormat`), and typed error surfacing (`ProviderError` / `SurfaceError`) live in the SDK. **All 14 trading providers use per-request or per-connection-lifecycle `IApiKeyCheckout`** (Schwab uses OAuth; IBKR is gateway-session-auth). **User-compiled Roslyn indicators run in an OS-sandboxed worker process:** Windows AppContainer (`CreateProcessW` + `STARTUPINFOEX`), macOS `sandbox-exec` (deny-default profile), Android `isolatedProcess` service. `OutOfProcessScriptHost` enforces wall-clock + 256 MB memory quota. IPC frame decoder (`AccessibleTrader.ScriptSandbox/Messages.cs`) caps string lengths at 64 KB and array counts at 1 M with bounds checks on every `ByteReader` read. Two GitHub Actions workflows — `plugin-manifest.yml` (publishes manifest as release asset) and `tests.yml` (runs full xunit suite on every PR/push). Build across all 4 TFMs: 0 errors, 0 warnings. **All green (2507 tests) as of 2026-07-27** (the finalization push — security, mouse, touch, visual-accessibility, and test-debt phases — added ~1120 over the 383 at the 2026-04-23 audit; that audit's 80 new tests were: 13 IPC / nonce / zero-value / clone regression tests, 7 `SecurityEventFileSink` tests, plus the Tier-1 coverage fill — 28 `WorkspaceStore` + reducer tests, 14 `AudioEngine` slot / pan / envelope tests, 8 `DataOrchestrator` resilience + state-machine tests, 10 `StrategyBacktester` correctness tests).

### Security hardening (2026-04-16 → 2026-04-17, complete)

Ahead of shipping to real retail users, a codebase-wide security audit was run and every finding — plus the broader-codebase polish items that followed — has landed. Highlights:

- **TLS / network.** IBKR cert validation no longer blanket-accepts self-signed certs (loopback-only gateway + optional SHA-256 pinning). Ollama refuses cleartext on non-loopback hosts. Android forbids cleartext traffic except loopback.
- **Credentials.** Schwab OAuth refresh tokens persist through `PluginHostServices.SecureStorage` on every platform (keychain on macOS/iOS, KeyStore on Android, DPAPI on Windows), with a DPAPI-encrypted file fallback. All trading providers now use sign-time `IApiKeyCheckout` (per-request for Kraken / Coinbase / Bitstamp; per-connection-lifecycle for Binance / Alpaca — SDK-managed clients built lazily and disposed on `DisconnectAsync`). Silent `catch {}` blocks in Schwab's token-cleanup path now record structured `TokenCleanupFailed` events via `ISecurityEventLog`.
- **Resource caps.** WebSocket frames capped at 16 MB. Binance Vision zip archives capped at 64 MB compressed / 256 MB uncompressed with a `BoundedReadStream` that defeats report-vs-stream bombs. All trading + 12 analytics + 2 LLM provider HttpClients now routed through `PluginHostServices.CreateHttpClient` with per-provider outbound-host allow-lists (32 MB response cap, 60 s default timeout); exceptions are IBKR (custom TLS-pinned handler) and Binance + MEXC (SDK-managed clients). Zip-slip defense-in-depth added.
- **Sandbox.** The Roslyn custom-indicator sandbox was rewritten from a substring blocklist to a semantic `CSharpSyntaxWalker` against blocked namespaces, types, and members. `.atpkg` imports require explicit user consent. User code executes in a **separate OS-sandboxed process**:
  - **Windows:** `WindowsAppContainerLauncher` uses full `CreateProcessW` + `STARTUPINFOEX` + `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` P/Invoke to apply the `AccessibleTrader.ScriptWorker.Sandbox` AppContainer profile. Since 2026-07, a missing/failed sandbox primitive on any platform **refuses** to launch the worker (`ScriptSandboxUnavailableException`) instead of silently falling back unsandboxed; `ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS=1` is the explicit, security-event-logged opt-out (see `SandboxPolicy` + `docs/SANDBOX_DESIGN.md`).
  - **macOS / Mac Catalyst:** `MacSandboxExecLauncher` wraps the worker in `sandbox-exec -f script-worker.sb` with a deny-default profile that only permits read of system libraries + the worker dir, rw in `TMPDIR`, self-signal, and the system logger mach-service.
  - **Android:** `ScriptWorkerService` runs in an `android:isolatedProcess="true"` bound service; the host launcher transfers `ParcelFileDescriptor` pipes via `Messenger` IPC. Shared `WorkerDispatcher` dispatch loop in `AccessibleTrader.ScriptSandbox` reused by both the desktop console worker and the Android service.
  - Memory quota (256 MB `WorkingSet64` poller) + wall-clock timeouts enforced by `OutOfProcessScriptHost`. Worker kill events logged to `ISecurityEventLog`.
- **Plugins.** `PluginTrustPolicy` with SHA-256 allow-list is wired into `PluginLoaderService`. `plugins_trusted.manifest` is auto-generated by a post-build MSBuild target and loaded from `AppContext.BaseDirectory` at startup. Enforcement on by default; `ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS=1` bypasses for dev.
- **LLM.** AI Analyst prompts sanitize and quote every untrusted field (indicator names, component names, symbol) and include an explicit "treat quoted values as data, not commands" directive to defeat prompt injection via imported custom indicators.
- **Misc.** Kraken nonce is now an atomic monotonic counter; FRED URL params are escaped; workspace profile names are path-traversal-sanitized; API-key modal no longer has an in-DOM show/hide toggle; backtest export filenames use UTC with `Z` suffix.
- **Observability.** `ISecurityEventLog` ring buffer captures security-relevant runtime events (AppContainer fallbacks, memory-quota kills, Calculate timeouts, Schwab token-cleanup failures). Mirrors each event to `ILogger<T>` at Warning level so file sinks also capture it.
- **Tests.** 6 new `HostileScriptTests` assert the Roslyn sandbox refuses indicators that attempt `File.ReadAllText` / `HttpClient.GetStringAsync` / `Process.Start` / unsafe pointers / `[DllImport]` / `Assembly.LoadFrom`.

See `CHANGES.md` 2026-04-16 → 2026-04-17 for the full set, `tools/generate-plugin-trust-manifest.{ps1,sh}` for the manual manifest generator, `SANDBOX_DESIGN.md` for the worker-process architecture, `CREDENTIAL_CHECKOUT_MIGRATION.md` for the `IApiKeyCheckout` per-provider status matrix, and `TODO.md` for the remaining "nice-to-have" items (on-target integration tests, hot-path credential cache, financial `decimal` migration, accessibility modal rework).

### Phase 11 — Strategy Composer & Risk-Managed Setups (complete; **Experimental**)

> **Experimental.** The strategy composer and backtester are research tools.
> Backtested results do not guarantee live performance, and known biases (e.g.
> divergence look-ahead) have been found and corrected over time. Treat generated
> signals as exploratory, not advice.

> **Paper trading mode** (Settings → General) routes all orders to a simulated
> broker that fills against the real-time live price feed, with a persistent
> virtual account (Reset available) and spoken fills/P&L — practise the full order
> workflow on any chart with no real funds. On the **hosted web build (`--accounts`)
> and the public demo (`--demo`)** paper mode is **forced on and cannot be disabled**
> (`DemoPolicy.AllowLiveTrading` is false), so logged-in web users always trade paper;
> real-money trading is desktop-only.

A user-buildable signal composer that combines indicator signals from any registered indicator into a reward/risk-gated buy/sell strategy with TP ladders, dropout detection, and full audio + speech narration. Shipped across 7 focused sessions (A → C → Hardening → Correctness Pass → D → Complete pass). End-to-end functional in both live and backtest modes.

**What you can do as a user today:**

- **Build a setup via the Build Setup tab** in the Strategy Manager modal. Tree-based condition editor (`role="tree"` ARIA) with cascading combo boxes for each leaf: indicator → component → operator (gated by `SignalKind`) → value → optional timeframe → optional second descriptor for cross-line operators. Add AND/OR/NOT groups to build arbitrary boolean expressions. Configure the risk plan: stop source (8 kinds including swing low / ATR / Ichimoku Kijun / Kumo / Cipher SR support / VPVR LVN), TP ladder (default 3 rungs at 1R / 2R / 3R), sizing mode, R:R minimum gate, entry trigger (immediate / pullback / breakout / N-candle confirm).
- **Hear the spec read aloud** before saving — `NarrateSpec()` walks the tree and emits a plain-English sentence covering every condition + risk plan field.
- **Preview a backtest inline** with R-multiple metrics, warmup gating, and the warmup-aware backtester from Session A.
- **Save / Load / Export / Import** strategies. Library lives at `{AppData}/strategies.json`; export drops a `.atstrat` file in `{AppData}/exports/`.
- **Add to Engine** marks the spec `IsAutoActivate=true` so the `StrategyAutoLoader` re-instantiates it on the next app launch — **strategies survive restart**.
- **Receive setup alerts** via three distinct earcons: `setup_long_bell` / `setup_short_bell` for the main confirmation, `PlaySetupArmed` for entry-armed waiting state, `PlaySetupEntryReached` for entry zone hit. Speech announces the rationale (side, score, stop, first target, R:R, notes). Re-fires on each confirming bar at lower volume; announces individual leaf dropouts (*"Cipher A wave cross dropped off"*).
- **Review every fired setup** via the **Journal Modal (`Ctrl+Alt+Shift+J`)** — persistent ring-buffer review surface for every TTS phrase, alert, strategy setup, and error this session. Filterable, copyable from a screen-reader-friendly monospace text area.
- **Ask the AI Analyst to review your day** via the new "Review setups today" button. Builds a structured prompt from today's journal entries + matching strategy specs, calls the configured LLM (Claude / OpenAI / Ollama), displays + speaks the response, mirrors the review back into the journal for later re-reading.
- **Backtest with full correctness**: warmup gate, R-multiple metrics, real `WorkspaceState` (so `ConfigurableStrategy` actually evaluates indicator references), per-bar VPVR profile-state replay (`BacktestConfig.ReplayProfiles=true` default), no future-leak.
- **Multi-timeframe leaves**: any leaf can carry an optional `Timeframe` field. `ConfigurableStrategy.Initialize` fire-and-forgets HTF bar + indicator pre-warm via `IMultiTimeframeDataService` so the synchronous evaluator can read indicator-on-HTF results on the hot path.
- **Modal input trap** is automatic for any modal that publishes `ModalStateChangedEvent` — chart navigation no longer leaks through arrow keys when a modal is open. F1 / F2 / F3 stay in the allowlist for accessibility toggles.

See `TODO.md` Phase 11 for the per-session breakdown and `CODEBASE_KNOWLEDGE_BASE.md` section 12.5 for the pipeline diagram.

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
- **Phase G** — Contextual speech: `SignalSpeechTemplate` on `ComponentConfig`, provider-declared signal descriptions via `GetComponentSpeech`, multi-signal sequencing in NavigationFeedbackManager. 2026-04-24: per-indicator speech templates are now user-editable via the **Speech tab in the Indicator Properties modal** (`PropertiesModal.razor`) — continuous (`SpeechTemplate`) and signal (`SignalSpeechTemplate`) fields per component with a Reset-to-default button that restores provider metadata defaults.
- **Phase H** — Cloud sonification: `CloudSonificationConfig` on `CloudFillConfig`, AudioSequencer cloud pass (slots 96–127 since the 128-voice bump; were 64–79 and silently dropped by the old 64-voice engine), EMA Fill + WT Fill + Ichimoku cloud audio wired.
- **Phase I** — Drawing keyboard placement: keyboard-first **sequential anchoring** — re-press the tool's own shortcut (e.g. `Ctrl+Shift+T`) at each anchor, `Escape` to cancel — with TTS price+timestamp feedback and change-from-anchor speech. (Note: there is no Enter-to-confirm; the `ConfirmCoordinateEntry` command is reserved/unused.)
- **Phase J** — Ctrl+Left/Right redesign: context-aware crossing (ZeroLine/Threshold/MACross/Band/Trendline/SparseMarker) dispatched from `HandleTrendlineCrossJump`.
- **Phase K** — Ichimoku Kinko Hyo indicator: 5 classical lines (Tenkan, Kijun, Senkou A/B, Chikou) + 3 post-phase additions (hidden Kumo Polarity strategy leaf, TK Bull / TK Bear confirmed-cross dots), Kumo cloud fill with 520/180 Hz sonification, displacement-shifted arrays, `GetDetailFact` context speech.
- **Phase L** — Test coverage expansion: 69 → 146 tests across 9 new test files covering all Phase B–K systems.

### Architecture Highlights (Phase 10-G + Improvement Plan)

#### Self-Describing Indicator Metadata
`IndicatorComponentMetadata` carries optional `Default*` visual and audio hints. `IndicatorModelFactory.CreateComponentConfigFromMeta` applies provider hints first, falls through to `IStylingService` role/type defaults. `StylingService` is now purely role/type-based with no indicator names.

#### 3-Layer Component Merge
`IndicatorModelFactory.CreateSeriesFromMetadata` applies: Layer 1 = provider metadata defaults; Layer 2 = workspace state-only (visibility/mute/volume/FreqMultiplier); Layer 3 = `IIndicatorPreferencesService` (JSON prefs at `%LOCALAPPDATA%\AccessibleTrader\IndicatorPrefs\{CODE}.json`). "Save as Defaults" in Properties dialog (Shift+F12) persists to Layer 3.

#### Sub-Pane Architecture
Any component can declare `SubPaneName` / `SubPaneHeightRatio` in `IndicatorComponentMetadata`. `ChartRenderer.RenderPane` performs multi-pass rendering: main area (top) + sub-pane strips (bottom, clamped 5–40% each). `ViewportRangeCalculator` accumulates ranges under composite keys (`"PaneName/SubPaneName"`).

#### Bell Synthesis and Sound Patches
`ISoundPatchRegistry` (not `ISoundPatchLibrary`) provides code-defined bell presets used by indicator providers. 6 built-in patches with distinct harmonic, decay, and detuning characteristics. Components declare `DefaultSoundPatchId` in metadata; `AudioSequencer` and `NavigationSonifier` resolve patch for decay and detuning parameters. The **Sound Designer** (`SoundDesignerModal.razor`, Alt+W) is now a general-purpose patch workbench over `ISoundPatchLibrary`: a `SoundPatch` carries a list of `OscillatorLayer`s (waveform, gain, freq ratio, noise amount/colour) plus base frequency, multiplier, volume, envelope, and duration — `EffectiveLayers()` upgrades legacy single-waveform patches for backward compatibility. User patches are assignable to earcons or, via `PropertiesModal.razor`'s Sonification/Acoustics section, to indicator components through `ComponentConfig.SoundPatchId` (plus `BullishSoundPatchId` / `BearishSoundPatchId` for directional green/red components); assignments live-link, and `SonificationManager.PlayPatch` drives previews.

#### Context-Aware Ctrl+Left/Right Navigation
`CommandDispatcher.HandleTrendlineCrossJump` dispatches to one of six crossing strategies based on focused component type: trendline crossing (price series), sparse signal jump (Dot/Diamond/Cross etc.), zero-line crossing (MACD etc.), threshold crossing (RSI/Stoch etc.), MA crossover (EMA/SMA overlays), band boundary crossing (Bollinger %B).

### Platform Status

| Platform | Host | Chart | Audio | Keyboard | Speech | Trading |
|---|---|---|---|---|---|---|
| Windows | MAUI | ✅ Native SkiaSharp | ✅ WASAPI | ✅ JS bridge | ✅ NVDA + ARIA | ✅ All providers |
| Android | MAUI | ✅ | ✅ AudioTrack | ✅ DispatchKeyEvent | ✅ TalkBack | ✅ |
| iOS | MAUI | ✅ | ✅ AVAudioEngine | ⚠️ On-screen only | ✅ VoiceOver | ✅ |
| Mac Catalyst | MAUI | ✅ | ✅ AVAudioEngine | ✅ KeyboardPageHandler | ✅ VoiceOver | ✅ |
| **Linux** | **WebHost** | **✅ Server PNG @ 10 fps** | **✅ pw-cat / pacat / aplay** | **✅ JS bridge (Ctrl+Shift+letter remapped to Alt+Shift+letter)** | **✅ Orca D-Bus** | **✅ same plugins** |

See `TODO.md` for the full Phase 10 + Linux WebHost L5–L7 roadmap and `PLATFORMS.md` for platform-specific details including the dual-host architecture.

### Which version should I use? (MAUI vs WebHost)

The terminal ships as **two heads that cover different platforms — they are not
redundant, and both are needed.** The full rationale is in
[`PLATFORM_STRATEGY_AND_ROADMAP.md`](PLATFORM_STRATEGY_AND_ROADMAP.md); the short
version:

- **Use the MAUI app** on **Windows, macOS, iOS, and Android.** It is the *only* way
  to run on a phone or tablet, and on the desktop it gives you the deepest native
  integration: lowest-latency native audio (WASAPI / AVAudioEngine / AudioTrack),
  full-frame-rate native chart rendering, and the real OS keychain for credentials.
  **It is also the only head that drives a refreshable braille / tactile display:**
  the Dot Pad tactile-graphics support is Windows-native, so a Dot Pad user wants the
  Windows MAUI build (enable it under Settings → General → "Enable braille / tactile
  display output").
- **Use the WebHost** on **Linux** — MAUI has no Linux head, so the browser-based
  WebHost is Linux's primary (and excellent) client, with Orca speech over D-Bus and
  audio via PipeWire/PulseAudio. It is also what powers the public-website chart demo.

Feature parity between the two is high — charts, indicators, trading, alerts,
custom scripts, sonification, and keyboard navigation all work on both. The
differences are native-integration and hardware: the WebHost renders the chart as a
server-side PNG (~10 fps) rather than a native canvas, and **tactile/braille output
is Windows-only** (the vendor's Linux Dot Pad SDK exposes no graphic API, so the
WebHost cannot drive a tactile display yet). Mobile (iOS/Android) requires the MAUI
app and is gated on touch-gesture support still in development.

## Development

Built with **.NET 10**. Two hosts share the same component library and core.

- **Core:** `AccessibleTrader.Core` — Business logic, custom DSP engine, Orchestrators. Platform-agnostic net10.0.
- **RCL:** `AccessibleTrader.BlazorClient.Components` — All Razor components (toolbar, modals, ChartArea, MainLayout). Platform-agnostic net10.0; consumed by both hosts unchanged.
- **MAUI host:** `AccessibleTrader.BlazorClient` — MAUI head for Windows / macOS / iOS / Android. Blazor WebView + native SkiaSharp `SKCanvasView` overlay.
- **WebHost:** `AccessibleTrader.WebHost` — ASP.NET Core Blazor Server head for Linux + public-website demo. Kestrel + server-side PNG chart rendering + Orca D-Bus speech.
- **SDK:** `AccessibleTrader.Sdk` — Plugin contracts and immutable performance models.
- **Plugins:** `Plugins/` — 29 exchange, data, and analytics provider plugins (14 trading + 15 analytics).
- **ScriptSandbox:** `AccessibleTrader.ScriptSandbox` — shared host/worker IPC contract (frame codec + opcodes + message DTOs).
- **ScriptWorker:** `AccessibleTrader.ScriptWorker` — standalone console app that hosts user-compiled indicators in a separate OS process.
- **Tests:** `AccessibleTrader.Tests` — Unit and integration diagnostics (2507 tests, all passing), plus a zero-dependency JS gesture-engine suite (`node tools/jstests/gesture-tests.mjs`, 12 tests). Both run in CI.

To run on Linux: `dotnet run --project AccessibleTrader.WebHost`. To run the MAUI head: build on the appropriate platform (Windows/macOS for the MAUI workloads).

## License

Copyright (C) 2026 Cody Hurst.

Accessible Trading Terminal is free software: you can redistribute it and/or modify it under the terms of the **GNU General Public License, version 3** (GPLv3) as published by the Free Software Foundation. This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the full license text in [`LICENSE`](../LICENSE) at the repository root, or <https://www.gnu.org/licenses/gpl-3.0.html>.

SPDX-License-Identifier: `GPL-3.0-or-later`

> Note: the bundled third-party SDKs and provider libraries (e.g. the Dot Pad SDK, exchange client SDKs) remain under their own licenses; GPLv3 covers the Accessible Trading Terminal source in this repository.
