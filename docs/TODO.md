# TODO — Accessible Trading Terminal

This file tracks all known bugs, improvements, and roadmap items. Items are organized by improvement-plan phase. Checked items `[x]` are confirmed complete. Open items `[ ]` are pending.

---

## [2026-04-24] — Icon toolbar system (complete 2026-04-24)

Replaced text-only toolbar + indicator bar with a circular-icon system:
25 custom SVG icons as inline sprite symbols, reusable
`ToolbarIconButton.razor` component, six saturated color variants
(data / action / warning / danger / neutral / thought). Icons paired
with labels — never icon-only — for the low-vision audience. 537/537
tests still green.

- [x] **Inline SVG sprite** at `Components/IconSprite.razor` with 25
  rounded-stroke symbols using `stroke="currentColor"`. Injected once
  from `MainLayout`.
- [x] **`ToolbarIconButton` component** with `Icon` / `Label` /
  `Tooltip` / `AriaLabel` / `Variant` / `IsToggleOn` / `Primary` /
  `Disabled` / `OnClick` parameters.
- [x] **Six CSS variants** with single CSS custom property
  `--btn-color`. Hue never shifts on hover / focus / pressed — only
  alpha + ring intensity. Muscle memory preserved.
- [x] **Toolbar groups** (`.toolbar-group`) separate Mode / Chart
  Setup / Analysis / Workspace / Meta clusters with inset vertical
  rules.
- [x] **3 px focus-visible ring at full variant saturation** replaces
  the 1 px dotted default.
- [x] **`Toolbar.razor` + `IndicatorBar.razor`** rewired to use the new
  component end-to-end.

### Composition-layer fixes (follow-ups, same day)

Shipping the icon toolbar required a six-commit bisection across
composition issues that had been latent since the original
`MainPage.xaml` was written — the text-button toolbar was always
painted over by the Skia canvas, but the app was keyboard-driven +
OCR/screen-reader-readable, so the missing pixels went unnoticed.
Fixed as part of this sweep. Full writeup in
`docs/CHANGES.md` "Icon-toolbar composition fixes" entry.

- [x] **`<base href="/">` + SVG `<use href="#id">` fragment-ref bug**
  — added `xlink:href` shim alongside `href` on every `<use>`.
- [x] **Nested string literals in Razor attribute values** —
  extracted to plain C# computed properties in `@code`.
- [x] **`MainPage.xaml` z-order: canvas-on-top with margin** —
  `BlazorWebView` spans the full Grid, `SKCanvasView` is declared
  after it (top layer) but margin-constrained to the middle chart
  region via `Margin="0,185,0,100"` so the toolbar / header / footer
  / indicator bar from the WebView stay visible above and below.
- [x] **`ChartArea.razor` outer div** → `background: transparent`.
  Previously `black`, left over from the canvas-on-top-without-
  margin era where the outer div was never visible.
- [x] **`IsDataReadyToRender()`** simplified to the same condition
  the canvas uses (`state.Data.Count > 0`). Old logic also required
  the orchestrator state to be `LiveStreaming` / `GapFilling`,
  which kept the blackout-overlay visible while the canvas had
  already started drawing bars.
- [x] **Pixel-perfect canvas sizing via JS-interop bounding-rect**
  — shipped 2026-04-24 (post-toolbar sweep). New
  `ICanvasRegionProvider` bridges Blazor (ResizeObserver) to the
  native `SKCanvasView.Margin`. XAML 185/100 values remain as a
  first-paint fallback.

---

## [2026-04-24] — Visual polish + titlebar/Schwab fixes (complete 2026-04-24)

Post-screenshot-review sweep. 537/537 tests still green.

- [x] **Titlebar stale after timeframe change** — `MainPage.xaml.cs`
  tracked only `_lastTitleSymbol`; changing only the timeframe left the
  titlebar stamped with the previous value. Now change-detects on a
  composite `{Symbol}|{Timeframe}|{Provider}` key.
- [x] **Schwab missing from stocks provider dropdown** — Schwab was in
  `.slnx` but not referenced from `BlazorClient.csproj`, so the
  assembly never shipped next to the host. Added the missing
  `<ProjectReference>` between Polygon and Tradier; plugin trust
  manifest auto-bumps 25 → 26 hashes on the next Release build.
- [x] **Pane legend readability** — `RenderPaneLegend` bg α 180 → 225
  and a 1px subtle border so the legend reads cleanly against bright
  candles / histogram.
- [x] **Y-gridline density** — `BackgroundLayer.Render` gained a
  nice-number gridline algorithm (7 minor steps, every 5th line
  major). Round-number anchors ($25k / $50k, ±50 on oscillators) land
  on major lines.
- [x] **Crosshair halo** — `RenderCrosshair` now paints a 5px white
  40α halo under every crisp crosshair segment (vertical + horizontal
  main + per-indicator-pane horizontal). Crosshair visibility survives
  busy backgrounds.
- [x] **Y-axis swatches at current indicator value** — new
  `RenderYAxisSwatches` draws a 4×3 px colored tick on the left edge
  of each pane's Y-axis strip at every visible Line/Area component's
  most-recent value. Walks back up to 20 bars so warmup NaNs don't
  suppress the tick.

---

## [2026-04-24] — Settings-modal Alerts tab (complete 2026-04-24)

Post-sweep phase 2. Closes the UI gap on the SMTP + Telegram channels
shipped earlier same day. 537/537 tests still green, 0 warnings.

- [x] **Alerts tab in `SettingsModal.razor`** — new sibling tab between
  Keyboard and License. SMTP fieldset (host / port / TLS / username /
  password / from / to) + Telegram fieldset (bot token / chat id) with
  per-channel "Send test" buttons that build a stub `AlertFired` and
  invoke `IAlertChannel.SendAsync` via the DI-registered channel list.
  `PersistAlertSettings()` writes each field through
  `ISettingsManager.SetSetting` + `SaveSettings()` on Close (and before
  Test). The existing `LoadEmailAlertConfig` / `LoadTelegramAlertConfig`
  helpers in `ServiceCollectionExtensions` continue reading the same
  key-paths per-send, so saved values take effect on the very next
  fired alert without any service reload.

---

## [2026-04-24] — Tier 3 sweep (complete 2026-04-24)

Six substantive items landed same-day as the Tier 1 + Tier 2 sweep. 537/537
tests still green. See `docs/CHANGES.md` 2026-04-24 Tier 3 entry.

- [x] **BuildSetupTab UI split** — 1145-line monolith decomposed into
  `ConditionTreeEditor.razor` + `RiskPlanEditor.razor` +
  `SummaryExport.razor` siblings under a thin `BuildSetupTab.razor`
  coordinator. Children take `Spec` by `[Parameter]` and mutate in
  place; parent raises `OnSpecReplaced` on structural load/new/import.
- [x] **`IStrategyModalCoordinator` facade** — StrategyModal @inject
  count 10 → 5. Coordinator wraps Engine + Backtester + WarmupAnalyzer
  + Library + Factory + Roslyn with `StartSpec`/`StopSpec`/`RemoveActive`/
  `TogglePause`/`RecommendedWarmup`/`RunBacktestAsync`/
  `CompileAndAddStrategyAsync`. Structured `StrategyCoordinatorResult`
  per call.
- [x] **Voice-slot pooling** — the `OscillatorVoice[]` array was already
  pool-allocated at ctor; the real hot-path allocation was
  `wave.ToLower()` in `SetVoice`. Extracted `ParseWaveform` with
  `StringComparison.OrdinalIgnoreCase` branches — zero allocations on
  the 300-calls/sec playback path.
- [x] **EventBus throttle/coalesce** — new `SubscribeCoalesced<T>` (Rx
  `Throttle`) + `SubscribeSampled<T>` (Rx `Sample`) convenience
  helpers on `IEventBus`. XML docs forbid using them for accessibility
  events.
- [x] **Script worker CPU quota + per-user worker-count cap.**
  `DefaultMaxCpuFraction = 0.9` polls `TotalProcessorTime` delta /
  wall-clock delta every 2 s; sustained > 0.9 triggers kill + security
  event. `DefaultMaxConcurrentWorkers = 16` with atomic counter in
  `StartAsync`/`DisposeAsync`. `IScriptWorkerProcess.TotalProcessorTime`
  added to contract with `GetProcessTimes` P/Invoke in
  `AppContainerScriptWorkerProcess`.
- [x] **SMTP + Telegram alert delivery.** `IAlertChannel` SDK
  interface; `EmailAlertChannel` (System.Net.Mail) +
  `TelegramAlertChannel` (Bot API) in Core; `AlertDeliveryService`
  subscribes to `AlertFiredEvent` and fans out in parallel
  `Task.Run(...)` with per-channel exception logging + security-event
  records. Eagerly resolved in `MainLayout.razor`. Config loads from
  `ISettingsManager` per-send under `alerts.email.*` / `alerts.telegram.*`.

### Deferred this sweep with refreshed rationale

- [x] **DLL plugin strategies + StrategyIndicatorCache integration +
  IStrategyRegistry.GetCatalog extension** — Phase 10-F complete
  2026-04-24. All three sub-items shipped in a single pass; see
  `docs/CHANGES.md` for the full writeup.
  (a) `IStrategyPlugin` SDK contract + `StrategyPluginRegistry` +
  fixture plugin + 7 loader tests (load / scan / idempotent-init /
  unload+reload / trust-policy enforce / missing-dir tolerance / GC).
  (b) `IPluginStrategyIndicatorCache` SDK mirror + host bridge via
  `PluginHostServices.IndicatorCache` + per-bar `Invalidate` in the
  backtester + pinning test that proves stale-cache-value bug is fixed.
  (c) Unified `StrategyRegistry` merges `IStrategyLibrary.All` +
  plugin templates with spec-wins-on-collision semantics + 5 catalog
  tests.
- [x] **Settings-modal Alerts tab UI** — shipped 2026-04-24 (same day).
  New Alerts tab in `SettingsModal.razor` reads + writes the
  `alerts.email.*` / `alerts.telegram.*` key-paths via `ISettingsManager`
  and exposes a "Send test" button per channel that resolves the live
  `IAlertChannel` from DI.

---

## [2026-04-24] — Tier 1 + Tier 2 sweep (complete 2026-04-24)

10 items shipped from the pre-sweep TODO triage. 537/537 tests pass.
See `docs/CHANGES.md` 2026-04-24 entry for per-item detail + rationale.

- [x] **Ctrl+L/R — focused-series-aware refinement.** Focused-trendline
  walks only that drawing; continuous-points components announce
  "no points of interest on {component}" instead of silently falling
  through to all trendlines.
- [x] **Cipher A WT Momentum Gradient queryable descriptor** (Phase 12).
  Hidden Line component registered so strategies can gate on momentum
  strength (0.0..1.0 normalized) via the standard leaf operators.
- [x] **Bollinger squeeze/expansion + MACD crossover narration.**
  Layered after raw component values in `BarDetailService` for
  Ctrl+Shift+D.
- [x] **Volume-Profile POC crossing alerts.** `AlertTarget.Poc` +
  `ILevelService` injection in `AlertEvaluator` resolve the live POC
  per-evaluation and override the stored threshold.
- [x] **Score + Sequence logic operators exposed in BuildSetupTab.**
  Evaluator already implemented both; the UI now surfaces
  `ScoreThreshold` with a max-score hint.
- [x] **MinLevelStrength UI** for `PriceRejectsLevel` /
  `PriceBreaksLevel` operators.
- [x] **Within-N input** now appears for every operator that consumes it
  (`GreaterThanWithin`, `LessThanWithin`, `BetweenWithin`,
  `PercentileBelow`, `PercentileAbove`).
- [x] **Group expand/collapse disclosure** on condition-tree groups.
  Toggles `aria-expanded` + hides children; evaluation unaffected.
- [x] **Future-space drawing anchors.** `DrawingInteractionManager`
  accepts clicks in the right-margin; anchor dates synthesised via
  median inter-bar delta. `DrawingCalculatorHelper.ResolveAnchorIndex`
  projects future dates to synthetic indices so trendlines keep their
  slope math intact.
- [x] **VPVR backtest replay pinning test** (`VpvrBacktestReplayTests` —
  4 tests). Closes the "most important pending S/R correctness" item.

### Deferred with refreshed rationale (2026-04-24)

- [x] **StrategyModal facade (`IStrategyModalCoordinator`)** — shipped
  2026-04-24 Tier 3 sweep. Wraps Engine + Backtester + WarmupAnalyzer +
  Library + Factory + Roslyn; StrategyModal @inject count 10 → 5.
- *Deferred sub-items collapsed into their canonical entries elsewhere in
  this file (divergence line rendering, cross-pane Anchor cloud tint,
  adaptive WT thresholds, three-tier level-crossing earcons, Custom
  Script Roslyn persistence, `ICustomScriptService.RunScriptAsync` full
  pipeline, Pine `line.new`/`label.new` mapping). Live trendline preview
  shipped 2026-04-24 (Mouse UX sweep). Custom Speech Template Editor
  shipped 2026-04-24 in Indicator Properties modal. Suggestion-mode
  metrics tracked separately at line 1096.*

---

## [2026-04-23] — Unit-test gap analysis (triage)

Produced after Week 4 + file-sink ship. Current coverage: **323 tests**
across 32 test files. Biggest uncovered surfaces below. Tier 1 is
in-flight this session; Tier 2/3 remain backlog.

### Tier 1 — highest risk, highest leverage (complete 2026-04-23)

60 new tests across 4 files; 383/383 total. See `docs/CHANGES.md`
2026-04-23 Tier 1 entry.

- [x] **`WorkspaceStore` + 5 reducers** — `WorkspaceStoreTests.cs`
  (28 tests). Covers every action type plus two concurrency stress
  tests. Pins the post-Week-1 `AddLevelAction` immutability fix.
- [x] **`AudioEngine` synthesis hot path** — `AudioEngineSlotAndPanTests.cs`
  (14 tests). Pan arithmetic, ViewportLength invariant, voice-slot
  isolation, envelope triggering, Reset-silences-output. Added
  `InternalsVisibleTo` so tests can reach `internal AudioConstants`.
- [x] **`DataOrchestrator` resilience** — `DataOrchestratorResilienceTests.cs`
  (8 tests). Per-provider Polly breaker isolation + full DataState
  transition-table pin. Reproduces production config without needing
  the mock farm (HistoricalDataFetcher + LiveStreamManager +
  IDbContextFactory).
- [x] **`StrategyBacktester` correctness** — `StrategyBacktesterTests.cs`
  (10 tests). Warmup gate, stop exits (long+short), single TP, 3-rung
  TP ladder with portion correctness, end-of-data close, date-range
  slicing, equity-curve ordering.

### Tier 2 — meaningful risk, moderate leverage (complete 2026-04-23)

55 new tests across 5 files; 438/438 total. See `docs/CHANGES.md`
2026-04-23 Tier 2 entry.

- [x] **`ConditionEvaluator.HtfLastClosedIndexExclusive`** —
  `ConditionEvaluatorHtfTests.cs` (10 tests). Reflection-tests the
  private binary search for the four called-out edge cases (empty /
  before-all / after-all / perfect-alignment) plus main-bar-between-
  HTF-bars. Behavioural tests confirm HTF price + indicator leaf
  paths clip to the exclusive index and that the per-(leafId,
  timeframe) warning dedup surfaces each distinct leaf exactly once
  via a `TraceListener` capture of `Debug.WriteLine`.
- [x] **`NavigationSonifier` + `AudioSequencer`** —
  `NavigationSonifierClusterTests.cs` (12 tests). Drives
  `FireClusterTicksAsync` against a spy `IAudioDriver` to pin the
  tier-ascending-then-positive-first ordering, NaN + focused-component
  + IsZoneLine + non-marker skip rules, the 5-tick cap on slots 3-7,
  and the navigation-vs-playback cross-series gating. Also pins the
  slot-layout contract: `SyncNavigationSlots` stops slots 2-7 before
  firing slot 0, and `PlayNote` round-robins strictly within 16-31.
- [x] **`IndicatorOrchestrator` incremental path** —
  `IndicatorOrchestratorIncrementalTests.cs` (7 tests). Direct
  coverage for the grow-vs-overwrite branch: same-bar tick overwrite,
  first-tick-of-new-bar grow, slow-data-arrival NaN fill for jumped
  bars, unknown-key silent skip, empty-data early return, cancelled
  token short-circuit, and mixed grow+overwrite across two components
  in one series.
- [x] **`BarDetailService` / `IndicatorContextAnalyzer`** —
  `BarDetailContextTests.cs` (14 tests). Candle-path pattern
  classifications (Marubozu, Hammer, Flat) + wick-percent phrasing,
  indicator-path visible-component value listing, hidden + NaN skip.
  Analyzer coverage: RSI OB/OS/Normal+Rising hints, MACD bullish
  crossover detection, BB upper-band branch, NaN-current-value null,
  out-of-range data-index null, unregistered-indicator fallback to
  first visible component.
- [x] **`SpeechFormatter` strategy chain** —
  `SpeechFormatterDispatchTests.cs` (12 tests). One dispatch test
  per strategy plus priority + token-expansion pins: Hidden wins
  over Cloud when a cloud is hidden; Cloud announces direction +
  width + price-position; PhaseName clamps out-of-range phase
  indices; MarkerSignal expands {name}/{price} and returns
  "no data" when the signal doesn't fire; StandardTemplate handles
  the {value:F1} / ValueOnly / NaN paths as the fallback.

### Tier 3 — lower risk / harder to unit-test (complete 2026-04-23)

41 new tests across 3 files; 479/479 total. See `docs/CHANGES.md`
2026-04-23 Tier 3 entry. Blazor-modal item stays deferred — still
needs a new bUnit dependency.

- [x] **Per-provider symbol normalisation** —
  `ProviderSymbolNormalisationTests.cs` (20 tests). Drives
  `BaseMarketDataProvider.CleanSymbol` via a test-only subclass, Kraken
  `FormatPair`/`FormatRestPair` via reflection (new ProjectReference
  to the Kraken plugin), and the inline Coinbase product-id transform
  as a mirrored reference impl. Test csproj now references
  `AccessibleTrader.Plugins.Kraken` so private statics resolve.
- [x] **Pagination bound sweeps** — `PaginationBoundsTests.cs`
  (9 tests). Reflects `HistoricalDataFetcher.ApplyFinalFilters` (every
  fetch path funnels through it). Pins: since/until inclusive
  boundary, zero-price forming-candle drop, partial-zero drop, limit
  TakeLast (not TakeFirst), limit applied AFTER filtering, empty
  input safe, limit > available returns all.
- [x] **`DrawingService` + calculators** —
  `DrawingCalculatorGeometryTests.cs` (12 tests). TrendLine linear fit
  + extrapolation beyond anchor range + missing-anchor early return;
  Channel baseline/upper/median at configured width + 5%-of-anchor
  fallback; FibRetracement standard levels (0/23.6/38.2/50/61.8/78.6/
  100) + inverted-anchor orientation; FibExtension levels including
  161.8%/261.8%; Rectangle normalises corners + NaN outside date range
  + reversed dates swap; HorizontalLine constant fill +
  missing-anchor early return.
- [ ] **Blazor modals** — still needs bUnit; deferred.

---

## [2026-04-23] — Post-audit 4-week plan

Independent six-subsystem audit on 2026-04-23 produced an overall grade
of **B**. Week 1 is correctness ship-blockers (started immediately);
Weeks 2-4 are ordered by user impact for a blind trader. See
`docs/CHANGES.md` 2026-04-23 entry for the per-subsystem grades and the
full finding list.

### Week 1 — correctness ship-blockers (complete 2026-04-23)

303/303 tests pass across all 4 TFMs. Five of the seven audit
ship-blockers landed; two were refuted on re-read. See
`docs/CHANGES.md` 2026-04-23 Week 1 entry for the full list.

- [x] **1. Bar X-alignment — audit finding refuted on re-read.**
  `StandardRenderers.cs:252,299` — bars use `x = i*barWidth` as the
  left edge of the cell, then `DrawRect(x+spacing, ..., barWidth-2*spacing, ...)`.
  Rectangle center sits at `i*barWidth + barWidth/2 = i*barWidth + halfBar`,
  which matches the line/dot/candle center anchors exactly. The audit
  mistook the variable's meaning; no code change required. Re-verified
  against `AudioConstants.ComputePanWidth` comment at
  `AudioConstants.cs:14` ("bar at local index k sits at visual
  fraction (k + 0.5) / ViewportLength").
- [x] **2. `SeriesReducer` immutability leak.** Fixed. `AddLevel`,
  `UpdateSeriesZoneBands`, `UpdateSeriesParameters` now each clone the
  target series via `ChartSeries.Clone()`, mutate the clone, and
  replace the target in `ActiveSeries` via `Select`. Stale "triggers
  UI bindings" justifications removed — no consumer subscribes to
  `CollectionChanged` on these collections.
- [x] **3. `IndicatorOrchestrator` incremental array bounds — audit
  finding refuted on re-read.** `IndicatorOrchestrator.cs:246-257` —
  the branch `data.Count > arr.Length` routes first-tick-of-new-bar
  to the grow-and-write path; `data.Count == arr.Length` (same-bar
  tick update) goes to `arr[^1] = kvp.Value`, correctly overwriting
  the current bar. The agent mis-read the branch condition. Logic is
  correct as written.
- [x] **4. IPC decoder bounds checks.** Fixed. Added
  `MaxArrayElements = 1_000_000` cap on every decoded `u32` count via
  `CheckCount(raw, field)`. `ByteReader.EnsureAvailable(n)` is called
  before every primitive read. `ReadString` caps the length field at
  `MaxStringBytes = 64 KB`. Malformed frames now throw typed
  `InvalidDataException` at decode time, not OOM.
- [x] **5. `@key` on live `@foreach` tables.** Fixed. `StrategyModal`
  Library/Active/Trades/bt-spec-dropdown all keyed; `BuildSetupTab`
  library dropdown keyed, and recursive condition-tree `<li>` keyed
  by `node.Id`.
- [x] **6. `LiveStreamManager` zero-value filter.** Fixed.
  `LiveStreamManager.cs:135` now requires all four OHLC legs `> 0`
  and `Volume >= 0` (Volume can legitimately be zero on the first
  tick of a new period for thin books / pre-market).
- [x] **7. `KrakenProvider` nonce.** Fixed. Replaced the
  `Increment` + `Exchange` + `Increment` sequence (which had a TOCTOU
  race producing duplicate nonces under concurrent signers) with a
  `CompareExchange` spin loop that atomically moves `_nonceCounter`
  to `max(current+1, now)`.

### Week 2 — accessibility silent-failure sweep (complete 2026-04-23)

303/303 tests pass. Four shipped, two refuted on re-read. See
`docs/CHANGES.md` 2026-04-23 Week 2 entry.

- [x] **Modal open/close earcons.** Fired from `MainLayout.razor` —
  `Info` on open, `Boundary` on close, before speech.
- [x] **F2 speech-toggle earcon.** Emits immediate `Info` earcon
  alongside speech. F3 sonification toggle deliberately omitted (see
  CHANGES for rationale).
- [x] **Order-failure earcons (single-sink fix).** One fix at
  `AccessibilityFeedbackCoordinator.OnFeedbackRequest` Error-case
  covers all 14 trading providers since they all funnel through
  `IGlobalErrorCoordinator.ReportError` → `FeedbackRequestEvent(Error)`.
- [x] **Cloud NaN guard — already present.** `AudioSequencer.cs:399`
  already has `if (double.IsNaN(signedWidth)) return;`.
- [x] **`SpeechFormatter` exception logging.** `ILogger<SpeechFormatter>`
  injected with parameterless fallback ctor for existing tests. Catch
  block logs at Warning with component + series + dataIndex context.
- [x] **Provider silent-catch audit.** Five critical silent catches
  (Binance/MEXC user-data + keep-alive, Coinbase user-update parse,
  Kraken auth WS + message parsers, TwelveData tick parse) now publish
  to `_errorStream`.

### Week 3 — security + correctness hardening (complete 2026-04-23)

303/303 tests pass. All 8 items shipped. See `docs/CHANGES.md`
2026-04-23 Week 3 entry.

- [x] **Gate `ACCESSIBLETRADER_SCRIPT_IN_PROCESS` behind `#if DEBUG`.**
- [x] **Surface `SandboxApplied=false` in startup UI.** Startup
  advisory via `AnnouncementEvent` + `Alert` earcon when the
  registered launcher is `DefaultProcessLauncher`.
- [x] **FRED + TwelveData API keys in URL.** No header auth available;
  instead scrubbed `ex.Message` → `ex.GetType().Name` in all catches
  so URL with key cannot leak through exception messages.
- [x] **Raw `ex.Message` in order-fail strings.** All 10 trading
  providers (Binance, Bitstamp, Alpaca, Tradier, Oanda, Coinbase,
  IBKR, Schwab, MEXC, Kraken) now return `ORDER_FAILED:{type}` +
  publish typed error to `_errorStream`. Schwab's controlled
  `SchwabReauthRequiredException.Message` kept intact.
- [x] **Cipher S detection race.** Per-symbol lock via
  `ConcurrentDictionary<string, object>`.
- [x] **Bearer-token header interpolation.** Tradier / Oanda /
  Coinbase now use `AuthenticationHeaderValue("Bearer", token)`.
  Polygon + Schwab already correct.
- [x] **Binance listen-key cleanup.** `_listenKey` nulled only on
  `StopUserStreamAsync` success; failure publishes to `_errorStream`.
- [x] **`ReconnectingWebSocket` 10 s connect timeout.** Linked CTS
  with `CancelAfter(ConnectTimeout)` bounds the handshake.

### Week 4 — tests + observability (complete 2026-04-23)

316/316 tests pass (303 → 316; 13 new regression tests). All items
shipped except the optional file-sink documentation. See
`docs/CHANGES.md` 2026-04-23 Week 4 entry.

- [x] **Unit tests for fixed bugs.** 13 new regression tests in
  `PostAuditRegressionTests.cs` covering IPC decoder bounds, Kraken
  nonce CAS idempotence under thread contention,
  `LiveStreamManager` zero-value predicate, and
  `ChartSeries.Clone` collection-isolation invariant.
- [x] **Audio-drop row in Journal Modal.** `IAudioDriver` extended
  with `DroppedCommandCount` / `TotalCommandCount` /
  `ResetAudioTelemetry` as default-interface members;
  `JournalModal.razor` renders a live status row at the bottom with
  a Reset button.
- [x] **Per-session HTF degradation warnings.** Replaced the static
  `_htfWarningLogged` bool with a `ConcurrentDictionary<string, byte>`
  keyed by `leafId|timeframe` so each distinct HTF leaf surfaces at
  least once per session.
- [x] **`ProfileService` null diagnostic logging.** Warning-level log
  with series id + code + bar count before the empty-list fallback.
- [x] **`SecurityEventLog` persistent file sink.** Shipped 2026-04-23
  as `SecurityEventFileSink` decorator — daily-rotated JSONL at
  `%LocalAppData%/AccessibleTrader/SecurityEvents/`, opt-out via
  `ACCESSIBLETRADER_SECURITY_EVENT_PERSIST=0`, dir override via
  `ACCESSIBLETRADER_SECURITY_EVENT_DIR`. 7 new tests.

### Deferred (rationale holds)

- [x] BuildSetupTab UI split — shipped 2026-04-24. Three sibling
  components (`ConditionTreeEditor`, `RiskPlanEditor`, `SummaryExport`)
  under a thin parent coordinator.
- [x] StrategyModal facade extraction — shipped 2026-04-24 as
  `IStrategyModalCoordinator`. Injection count 10 → 5.
- [x] SKPaint pooling — shipped 2026-04-24. New `SKPaintPool`
  (`[ThreadStatic]` stack + `RentedPaint` lease) retrofit into every
  per-bar hot path in `StandardRenderers`. Steady-state alloc count
  drops from ~2500/frame to ≈10 on a busy chart.
  Real GC win but needs profiling first to confirm on target devices.

---

## Next sprint — audit backlog closed (only UI split deferred)

Items 1, 2, 3, 4 all shipped on 2026-04-22. Item 5's Core-side extraction
shipped; the UI split into sibling razor components is deliberately
deferred (see rationale below).

### Shipped 2026-04-22 (post-audit work)

- [x] **1. `SpeechFormatter` strategy registry** — `FormatTemplateValue`
  shrank from ~160-line interleaved conditional to a ~15-line dispatcher
  over five `IComponentSpeechStrategy` implementations
  (`HiddenComponent`, `CloudComponent`, `PhaseName`, `MarkerSignal`,
  `StandardTemplate`). Public `ISpeechFormatter` surface unchanged.
- [x] **2. `WorkspaceStore.Reduce` decomposition** —
  `WorkspaceStore.cs` 893 → 277 lines. Five per-domain reducers under
  `Services/Workspace/Reducers/` (`ViewportReducer`, `SeriesReducer`,
  `PlaybackReducer`, `TabReducer`, `DrawingReducer`); top-level `Reduce`
  is a 30-line dispatcher.
- [x] **3. REST-provider silent-failure sweep** — audit of all 26
  providers found 23 already routed errors through `_errorStream`. The
  three stragglers (`PolygonProvider.FetchOhlcvAsync`,
  `PolygonProvider.GetAvailableSymbolsAsync`,
  `FinnhubProvider.GetAvailableSymbolsAsync`) split into typed handlers.
- [x] **4. CI doc-drift guard** — `scripts/check_doc_drift.py` +
  `.github/workflows/doc-drift.yml`. Verifies shortcut bindings /
  plugin-directory count / live test count against `docs/README.md`
  and `docs/SHORTCUTS.md`.
- [x] **5a. Strategy-spec Core services** — `EditableStrategySpec` POCO
  + `StrategySpecValidator` + `StrategySpecNarrator` +
  `StrategyLibraryFacade` (`IStrategyLibraryFacade`) in
  `Core/Services/Strategies/`. 11 new validator tests
  (`StrategySpecValidatorTests`). `BuildSetupTab.razor` rewired to the
  new services — 1373 → 1037 lines (-25%).

### Deferred (conscious choice, not missed)

- [x] **5b. `BuildSetupTab` UI split into sibling components.**
  Shipped 2026-04-24. `ConditionTreeEditor.razor` +
  `RiskPlanEditor.razor` + `SummaryExport.razor` all exist as siblings
  under a thin parent that owns a single `EditableStrategySpec`. The
  ~30 `@onchange` bindings rewrote to the `Spec.X = v` form. Public
  behavior unchanged; 537/537 tests still green.

### When returning to this project
Audit is closed. Next session starts from a clean "post-audit" baseline —
no pending items. Don't redo the audit; findings are in the
`docs/CHANGES.md` 2026-04-22 entries and the memory file
`project_audit_sprint_2026-04-22.md`.

---

## [2026-04-22] — Pre-release hardening sprint (Day 1–4 complete)

Remediation of the 2026-04-22 full-codebase audit. All four clusters (ship-
blockers, accessibility & resilience, strategy correctness, silent-failure
sweep) landed in a single session. **292 / 292 tests pass** across all 4 TFMs.
See `docs/CHANGES.md` 2026-04-22 for the full diff.

- [x] **Polygon API key moved from URL to Authorization header** — `?apiKey=` removed from all REST call sites; `BuildAuthorizedGet` + `GetAuthorizedStringAsync` helpers added.
- [x] **WebSocket heartbeat sends real bytes** — `ReconnectingWebSocket` was passing `count: 0`; fixed to full payload length. Silent catch scoped + logged.
- [x] **`SymbolValidator` added to SDK** — conservative `[A-Za-z0-9_./:-]{1,32}` allow-list, enforced at `DataOrchestrator.FetchOhlcvAsync` + `StartLiveStreamAsync` choke points. 24 new xunit tests.
- [x] **`IndicatorOrchestrator.ValidateBufferKeys` un-gated from `#if DEBUG`** — mismatched buffer keys now log a Warning in Release, not silently blank a component.
- [x] **Modal open/close announced via ARIA live** — `ModalStateChangedEvent` extended with `ModalName`; 17 modals updated; `MainLayout` routes phrases through the existing speech double-buffered live region.
- [x] **Tab trap inside open modals** — `keyboard.js` capture-phase handler keeps Tab / Shift+Tab inside the last visible `[role="dialog"]`. Covers stacked modals via depth counter.
- [x] **Chart-focus gate on single-letter commands** — `_chartFocused` tracked in `keyboard.js`; single ASCII letters without modifier skip the dispatcher when a modal is open. Form-control guard extended to cover `contentEditable`.
- [x] **`LiveStreamManager.StartLiveStreamAsync` idempotency guard** — re-entry with identical `(provider, market, symbol, timeframe)` on an attached provider no-ops instead of tearing down the subscription.
- [x] **Per-provider circuit breaker** — `DataOrchestrator` now keys its Polly breakers by provider id. One dead source no longer blocks every other provider for 5 s. `ConnectionStatusEvent` carries the provider id.

### Day 4 — silent-failure sweep (complete)

- [x] **HTF pre-warm gate** — `ConfigurableStrategy` tracks pre-warm tasks; `OnBar` blocks HTF evaluation until `IsPrewarmComplete` is `true`; a one-shot speech announcement fires on the first blocked evaluation.
- [x] **Pure-pulse entry trigger: refuse save** — `BuildSetupTab.ValidateForSave` blocks `SaveSpec` / `AddToEngine` with a spoken error when a pulse tree has a non-Immediate trigger. Legacy specs still auto-promote with a one-shot Alert announcement.
- [x] **`AIAnalystService` fallback retry** — `AskAsync` and `AnalyseAsync` iterate the full provider list, retrying on empty response or exception; publish a terminal error when every provider is exhausted.
- [x] **`AudioEngine` command-buffer overflow telemetry** — atomic `DroppedCommandCount` / `TotalCommandCount` counters + `CommandDropped` event; `BlazorAudioDriver` records an `AudioCommandDropped` security-event every 10 drops. 4 new xunit tests.

### Deferred architectural refactors

Canonical list lives in the 2026-04-22 "Deferred (rationale holds)" block
higher in this file — duplicates collapsed 2026-04-23. Status:

- [x] **`WorkspaceStore.Reduce` decomposition** — shipped 2026-04-22 as
  5 per-domain reducers under `Services/Workspace/Reducers/`. Tests:
  `WorkspaceStoreTests.cs` (28).
- [x] **`SpeechFormatter` plugin registry** — shipped 2026-04-22 as the
  5-strategy dispatch chain (`HiddenComponent` / `CloudComponent` /
  `PhaseName` / `MarkerSignal` / `StandardTemplate`). Tests:
  `SpeechFormatterDispatchTests.cs` (12).
- [x] **Symbol-normalization common layer (Tier B.1 — 2026-04-23)** —
  `BaseMarketDataProvider.CleanSymbol` is the shared layer. Coinbase's
  five inline sites consolidated into `ToProductId` private helper
  (2026-04-23). Kraken's `FormatPair` / `FormatRestPair` retained
  (WS-vs-REST wire format is genuinely distinct). Pinned by
  `ProviderSymbolNormalisationTests.cs` (20) + `TierBRegressionTests.cs` (5).
- [x] **Timeframe-map common layer (Tier B.2 — 2026-04-23)** — the
  `Models.TimeframeUtility` regex-based parser is the canonical common
  layer. Legacy `Configuration.TimeframeUtility` flagged `[Obsolete]`;
  Bitstamp migrated to the Models version (and now supports `8h`/`2w`/
  arbitrary tokens the legacy switch couldn't). Per-provider wire-format
  mappings (OANDA `H1`, Kraken `60`) remain inline — deferred until a
  second provider needs the same translation table.

### Silent-failure sweep (cross-cutting, schedule after Day 4)

Every silent-failure path flagged by the audit should either emit an earcon
or a terse speech notification. Catalogue: REST `catch { return (empty) }`
blocks in providers, indicator buffer-key mismatches (now logged but not
user-surfaced), strategy leaf auto-promotion (Day 4 item above), narration
seeding race, AI Analyst fallback (Day 4 item), ring-buffer overflow (Day 4
item). Treat this as one sprint of "every drop-event gets a notification."

---

## [2026-04-21] — Viewport + Home/End + audio-visual sync (complete)

User-reported session: Home/End behavior, right-margin consistency,
audio sonification tracking visual bar positions, drawing-tool shortcut
reliability. All fixes landed in a single session; details in
`docs/CHANGES.md` 2026-04-21 entry.

- [x] **Home/End decoupled from scroll logic** — new `SetCursorAction` + reducer helper `CursorOnlyJump` clamps into `[ViewportStartIndex, ViewportStartIndex + visibleCount - 1]` and bypasses `Navigate()` entirely. End can never advance the viewport; future refactors of scroll logic can't accidentally re-couple them.
- [x] **Right-margin rule rewritten to match TradingView** — `ChartRenderer.Render` takes `Take(effectiveWindow)` at live edge, `Take(viewportLength)` when panned back. Margin exists only as the "future space" at live edge. Renderer path passes `state.RightMarginBars` from MainPage + AIAnalystService.
- [x] **`ViewportNavigationService.Navigate` uses `cursorWindow`** — scroll trigger now matches the renderer's visible bar count (effectiveWindow at live edge, ViewportLength when panned back). Arrow-key navigation inside a panned-back viewport stops scrolling prematurely.
- [x] **Live updates no longer jump focus** — `WorkspaceStore.UpdateData` preserves cursor unconditionally; viewport advances only if it was already showing the live edge.
- [x] **Audio pan = visual position, always** — `AudioConstants.ComputePanWidth` returns `ViewportLength` unconditionally; audio stereo position now matches the candle's x-fraction on the canvas. 5 call sites updated.
- [x] **Crosshair upper-bound clamp** — `RenderCrosshair` clamps `localIndex` to `visibleData.Count - 1` instead of returning early. Guarantees crosshair anchors to a real bar; never renders in the margin.
- [x] **Drawing-tool shortcuts fixed** — `keyboard.js` switched to capture phase + `e.stopImmediatePropagation()` on modifier chords; `trappedKeys` list expanded to cover all drawing-tool letters. WebView2 no longer steals Ctrl+Shift+T before our handler.

### Follow-up (deferred — feature, not bug)

- [x] **Allow drawing anchors in future-space** — shipped 2026-04-24.
  `DrawingInteractionManager.HandleMouseEvent` accepts mouse clicks in
  the right-margin zone; anchor dates synthesised via a median
  inter-bar delta projection. `DrawingCalculatorHelper.ResolveAnchorIndex`
  projects future dates to synthetic indices so trendline slope math
  stays intact when one anchor sits past `Data[^1]`. Mouse-side
  `HandleAddDrawing` keyboard path still clamps to real bars — future
  work when keyboard users want to anchor into the margin directly.

### Follow-up (deferred — UX call)

- [~] **Make `RightMarginBars` a fraction of `ViewportLength` rather than absolute count** — currently hardcoded to 20 bars. At ViewportLength=500 (zoomed out), the margin is only 4% of canvas width; at ViewportLength=100 (default), it's 20%. If the goal is "always ~20% visual gap for projections," switch to `RightMarginFraction = 0.20` and compute `RightMarginBars = ceil(ViewportLength * fraction)` on demand. *Deferred* — 20+ call sites read the field; no user pushback motivates the ripple. Re-open when friction surfaces.

---

## [2026-04-19] — Pre-release quality audit (complete)

Full-codebase audit across Core/SDK, 26 plugins, and the Blazor client.
All flagged issues resolved in a single sweep; build green across all
TFMs, 264/264 tests pass.

- [x] **FMP analytics HttpClient bypass** — `FmpAnalyticsProvider.Configure` was using `new HttpClient()` directly, skipping the phase-4 allow-list / response cap / timeout. Now routes through `PluginHostServices.CreateHttpClient` like every other analytics plugin.
- [x] **Blazor modal event-sub leaks** — three modals (`DrawingToolsModal`, `HelpModal`, `AddIndicatorModal`) had `_eventSub` and a `Dispose()` method but no `@implements IDisposable` directive. Blazor was never calling Dispose; each modal open→close leaked one subscription. Fixed.
- [x] **PropertiesModal ARIA tabs** — screen-reader regression. Tabs were missing `aria-controls`; tabpanel was missing `id` and `aria-labelledby`. Added all three plus a dynamic `ActiveTabId` property driving `aria-labelledby="@ActiveTabId"`.
- [x] **Toolbar `async void` handlers** — `OnMarketChanged` / `OnProviderChanged` / `OnSubTypeChanged` converted from `async void` to `async Task` so exceptions propagate to Blazor's error boundary instead of `SynchronizationContext.UnhandledException`.
- [x] **Missing `@key` on live lists** — order-book bid/ask rows and trading-dashboard balances / positions / open-orders tables got `@key` bindings. Focus + input state no longer corrupt when live ticks reorder the list.
- [x] **Sync-over-async deadlock risk (`LiveStreamManager`)** — implemented `IAsyncDisposable`; kept `IDisposable` fallback but wrapped the provider disconnect in `Task.Run` so the captured `SynchronizationContext` can't deadlock the shutdown path.
- [x] **Sync-over-async in `AnalyticsDataResolver`** — added sync `IDataService.IsProviderConfigured(string)` overload (internal impl is already synchronous, no I/O); resolver uses it directly now. Test mocks updated.
- [x] **Binance pagination defensive comment** — the MEXC pagination fix last session flagged a class of API bug (silent "latest-N" degradation on single-bound queries). Binance is unaffected but structurally identical; added a pointer comment at `BinanceProvider.FetchOhlcvAsync` so future maintainers know where to copy MEXC's bound-computation pattern from if the API behavior ever changes.
- [x] **Silent `catch {}` blocks audited** — 6 sites across Schwab and BinanceVision narrowed to specific exception types where safe (`CryptographicException`, `IOException`, `HttpRequestException`, `InvalidDataException`, `JsonException`) and commented-in-place where the broad catch was the correct call. `SpeechFormatter` catch kept broad but with an explicit justification (accessibility path must never stop emitting audio).
- [x] **MainLayout keyboard-init timeout** — added a 10 s `CancellationTokenSource` + `.WaitAsync(ct)` so a hung JS runtime on first render can't trap initialization indefinitely. `OperationCanceledException` caught separately with a distinct log message.
- [x] **Stale TODO removed** — MacCatalyst `AppDelegate.cs` had a "TODO Phase 7: Wire Mac Catalyst keyboard input" comment; `KeyboardPageHandler` already does this. Replaced with a pointer to the real implementation.
- [x] **Trading-provider interface docs** — added `<summary>` on `GetBalancesAsync` / `GetPositionsAsync` / `GetOpenOrdersAsync` / `CancelOrderAsync` in `ITradingProvider`, noting the MEXC-spot "symbol required" quirk on `GetOpenOrdersAsync`.

### Follow-ups (deferred — architectural decisions pending)

Each item below was surfaced by the audit but intentionally not touched
this session because it requires a design call, not cleanup. The
framing, options considered, and the recommendation are recorded so a
future session can act on them without re-deriving the tradeoffs.

#### 1. Symbol-normalization consolidation (crypto providers)

**State:** Coinbase does `/`→`-`, Bitstamp strip-all + lowercase + `usdt`→`usd`, Kraken has a 3-branch heuristic, Oanda does `_`. Each lives inline in its own class.

**Options considered:**
- **A.** `BaseMarketDataProvider.NormalizeSymbol(string)` virtual, each provider overrides.
- **B.** Static `SymbolNormalizer` class in the SDK with named methods (`SlashToDash`, `StripAndLowercase`, etc.) providers compose.
- **C.** Leave it — the rules are actually different enough that a shared API would be 4 special cases in a trench coat.

**Recommendation:** **C.** The providers *look* duplicative, but the normalization rules are genuinely different. Forcing them through one abstraction would save ~12 lines of code at the cost of indirection and a harder-to-read flow at each call site. Revisit only if a 5th crypto provider lands with the same rule as one of the existing four.

#### 2. Timeframe-mapping consolidation (7+ providers)

**State:** Most providers use the exchange SDK's strongly-typed enum (`Binance.Net.Enums.KlineInterval`, `JKorf.Mexc.Net.Enums.KlineInterval`, etc.), not strings. A shared `Dictionary<string,string>` doesn't fit.

**What is worth extracting:** `TimeframeDuration(string)` returning a `TimeSpan`. `MexcProvider` already has it for pagination math; it's a pure function with no provider-specific flavor.

**Recommendation:** Lift `TimeframeDuration` (or a `TimeframeUtil` static) onto `BaseMarketDataProvider`. Leave the per-provider enum mappings alone. Small, zero-risk win; ~30 min of work.

#### 3. `BuildSetupTab.razor` decomposition (1,330 lines)

**State:** One cohesive component that builds one `StrategySpec` — strategy metadata, condition-tree editor, leaf/group mutation, risk-plan UI, persistence — all sharing `_spec` in-scope.

**The honest question:** is this file actively hurting us, or just intimidating to read? No open bug reports are blocked on its size today.

**Options considered (if decomposing):**
- **Cascading parameter** of `StrategySpec` — simple, couples children to its shape.
- **EventBus** messages for edits — loose, harder to trace.
- **Explicit `[Parameter]` + `EventCallback`** on sub-components — most idiomatic Blazor, most plumbing.

**Recommendation:** hold off unless a feature is about to land here (e.g. copy-from-existing-strategy flow, template library). The split is 4–6 hours of work whose payoff is "the file is shorter." If/when a feature forces movement, decompose first so the new feature has a clean home. Otherwise leave it.

#### 4. `StrategyModal` → `StrategyFacade` (10 injections)

**State:** `StrategyModal.razor` injects `IStrategyEngine`, `IStrategyBacktester`, `IBacktestWarmupAnalyzer`, `IStrategyLibrary`, `IConfigurableStrategyFactory`, `IRoslynScriptingService`, `ISeriesManagementService`, `IWorkspaceStore`, `IEventBus`, `IJSRuntime`.

**Smell or legitimate?** Each of those is a distinct responsibility the modal genuinely has. The facade doesn't remove work — it relocates it.

**Proposed shape:** `IStrategyModalCoordinator` wraps the 6 strategy-specific services (engine + backtester + warmup + library + factory + roslyn). Modal then injects 5 things (facade + series + workspace + eventbus + jsruntime).

**Recommendation:** worth doing, ~2-3 hours. The real benefit isn't reducing the injection count — it's centralizing the "here's how strategy operations coordinate" logic that currently lives scattered across the modal's event handlers. Good candidate to land **before** any `BuildSetupTab` split if that ever happens, since a clean facade makes the decomposition easier.

---

**Suggested execution order if taking these on:**

1. **#2 first** (30 min, zero risk).
2. **#4 second** (2–3 hr, clean refactor with clear testable boundary).
3. **#3 only when a feature demands it** (4–6 hr).
4. **#1 never, unless a 5th provider arrives with a matching rule.**

---

## [2026-04-18] — MEXC provider + decimal precision overhaul + Cipher C fix (complete)

- [x] **MEXC provider plugin** — `Plugins/Providers/AccessibleTrader.Plugins.Mexc` using `JK.Mexc.Net 5.0.1`. Spot + futures klines, order book, user-data stream, full `ITradingProvider` surface (balances, positions, open orders, place/cancel order, set leverage). Registered in `AccessibleTrader.slnx` AND in `AccessibleTrader.BlazorClient.csproj` `<ProjectReference>` (the MAUI app enumerates plugins explicitly). Trusted-plugin manifest auto-bumped 23 → 25 on build.
- [x] **MEXC pagination fix** — `MaxBarsPerRequest` dropped 1000 → 500 (real API cap); `FetchOhlcvAsync` now computes the missing time bound from `limit × bar-duration` when the caller passes only one, because MEXC's spot klines endpoint silently ignores single-bound queries and falls back to "latest 500". Restores the full available history window (e.g. KAS/USDT daily now goes back to the Dec 2024 listing date instead of ~Sept 2025).
- [x] **Price formatters for the UI.** New `AccessibleTrader.BlazorClient/Services/PriceFormatter.cs` (`FormatPrice`, `FormatQuantity`, `FormatPnL`). Applied to `TradingDashboardModal.razor` (live price, spread, open-order price, balance Free, position qty / PnL) and `StrategyModal.razor` (entry/exit/PnL in summary + details panel + per-trade grid). Sub-dollar assets now display with magnitude-adaptive precision instead of `0.04`.
- [x] **Chart Y-axis + crosshair adaptive precision.** `ChartRenderer.RenderYAxis` and `RenderCrosshair` route through new `FormatAxisValue(val, range)` helper. Formula `decimals = clamp(2 − floor(log10(range)), 2, 10)` — KAS-scale ranges get 4–7 decimals, BTC-scale gets 2.
- [x] **Speech-pipeline adaptive precision.** New `AccessibleTrader.Core/Services/Accessibility/SpeechPriceFormatter.cs`. Applied to `SpeechFormatter` (candle / price-line / profile-bin / heatmap / `{value}` template for price series), `AccessibilityFeedbackCoordinator` (new-bar close/open), `NavigationFeedbackManager` (coordinate entry — was `F0`, rounding sub-dollar to 0), `DrawingInteractionManager` (all anchor announcements), `CipherAProvider` / `CipherBProvider` / `SpiderLinesProvider` (price-annotated narrations). Indicator values (RSI, MACD, WT) intentionally stay on `F2`.
- [x] **Cipher C tail-boost removed.** `CipherCProvider.Calculate()` had a pre-clamp Fisher amplifier that inverted its stated intent — stoch ≥ 0.94 already exceeded the ±100 clamp, and the boost dragged the 0.90–0.94 band above 100 as well, collapsing every extreme read to the same value. Dropped the five-line boost block. On the weekly KAS chart the Cycle Sine plateaus shrank from 3–5 bars to 1–2 bars and the Top Single/Double/Triple tier separation restored. All 58 Cipher C tests still pass.

---

## NEXT UP (2026-04-16) — Security hardening (pre-customer release)

Ahead of shipping to real retail users, a full-codebase security audit was run
(see `memory/reference_security_audit.md` for the severity-ranked source map
and `CHANGES.md` 2026-04-16 entry for what landed). The release gate is split
across two phases. Phase 1 is complete; phase 2 is open.

### Phase 1 — release gate (complete)
- [x] **IBKR TLS validation (C1)** — dropped the blanket cert-accept; loopback-only enforcement on `GatewayUrl`; optional SHA-256 pinning via `GatewayCertSha256`; scheme validation; 16 MB response cap.
- [x] **Roslyn sandbox rewrite (C2)** — semantic `CSharpSyntaxWalker` against blocked namespaces / types / members; lexical pre-flight for `unsafe`/`stackalloc`/`[DllImport]`; applied to indicator + strategy + simple-script paths; `.atpkg` import now requires explicit user consent.
- [x] **Plugin DLL trust policy (C3)** — `PluginTrustPolicy` with SHA-256 allow-list + `RequireTrusted` flag wired into `PluginLoaderService`. Non-regressing default (warns on unverified; flip `RequireTrusted` to lock down).
- [x] **Schwab DPAPI token encryption (C4)** — Windows: `ProtectedData.Protect(CurrentUser)` + custom entropy; non-Windows: persistence disabled until cross-platform SecureStorage is plumbed. Legacy plaintext files auto-deleted.
- [x] **LLM prompt-injection sanitizer (C5)** — strip control chars, quote untrusted fields, 120-char cap, explicit "treat quoted values as data, not commands" directive.
- [x] **WebSocket frame cap (H2)** — 16 MB `MaxMessageBytes` in `ReconnectingWebSocket`; closes with `MessageTooBig` and triggers reconnect on oversize.
- [x] **Binance Vision zip-bomb defense (H1)** — 64 MB compressed / 256 MB uncompressed caps; new `BoundedReadStream` wrapper; zip-slip defense-in-depth.
- [x] **Ollama cleartext hardening (H3)** — loopback-only `http`; https required for remote; unknown schemes rejected.
- [x] **Kraken monotonic nonce (H6)** — atomic counter seeded from wall-clock ms; no same-ms collisions under burst order flow.
- [x] **Workspace path traversal (H5)** — `SanitizeProfileName` rejects `..`, rooted paths, invalid chars, reserved `alerts`.
- [x] **FRED URL escape (M1)** — `Uri.EscapeDataString` on `series_id`, `api_key`, `category_id`.
- [x] **Android network security (L4)** — `network_security_config.xml` + `usesCleartextTraffic=false` + `allowBackup=false`.

### Phase 2 (complete)
- [x] **Response size caps on remaining analytics HttpClients** — AlternativeMe, OkxDerivatives, DefiLlama, BGeometrics, CoinGecko, Glassnode, CoinMetrics, BinanceDerivatives, Etherscan, Mempool, FRED all now construct their `HttpClient` with `MaxResponseContentBufferSize = 32 MB` and a 60s timeout. Non-regressing — payloads are <1 MB in practice.
- [x] **`ApiKeysModal` show/hide removed (M3)** — inputs are always `type="password"`; no more DOM-level reveal toggle. Native OS password-reveal still available at the WebView level. Cleared `_showApiKey` / `_showSecret` / `_showPassphrase` fields and their resets.
- [x] **Plugin trust hash manifest** — `PluginTrustPolicy.LoadManifest(path)` parses a `plugins_trusted.manifest` file (hex SHA-256 digests, one per line, `#` comments). Wired into `ServiceCollectionExtensions.AddDataPipeline` to load from `AppContext.BaseDirectory` at startup. `ACCESSIBLETRADER_REQUIRE_TRUSTED_PLUGINS=1` env var flips `RequireTrusted`. Build-time generator ships as `tools/generate-plugin-trust-manifest.{ps1,sh}` (both Windows and POSIX). Hash the manifest after each Release build and ship it alongside the app; unverified DLLs log a warning (or are blocked under the env var).
- [x] **StrategyLab dev CLI size caps** — `BinanceVisionFundingCommand.cs` and `BinanceVisionOiCommand.cs` now use `MaxResponseContentBufferSize` + a local `BoundedStream` zip-bomb guard mirroring the plugin pattern.

### Phase 3 (complete 2026-04-17)
- [x] **Auto-generated plugin trust manifest on Release build** — `GeneratePluginTrustManifest` MSBuild target in `AccessibleTrader.BlazorClient.csproj` uses an inline `RoslynCodeTaskFactory` task to walk `$(OutDir)` after each Release build, hash every `AccessibleTrader.Plugins.*.dll`, and emit `plugins_trusted.manifest` next to the shipped binary. No external scripts required; works on any build agent.
- [x] **Schwab cross-platform SecureStorage via `PluginHostServices`** — new `IPluginSecureStorage` + `PluginHostServices` in `AccessibleTrader.Sdk.Services`. `MauiSecureStorageService` now implements both the Core `ISecureStorageService` and the plugin-facing `IPluginSecureStorage`; DI forwards both to the same singleton. `MauiProgram.CreateMauiApp` sets `PluginHostServices.SecureStorage` after container build. `SchwabOAuthService` now persists refresh tokens via the host bridge on every platform, with DPAPI-on-Windows as a fallback and a migration path from legacy DPAPI files into the bridge.
- [x] **Credential scrub on disconnect (H4 pragmatic)** — new `BaseMarketDataProvider.ScrubCredentials` helper with a best-effort gen-0 GC hint. Wired into `DisconnectAsync` for every trading-funds provider (Binance, Coinbase, Kraken, Bitstamp, Alpaca, Schwab). Drops GC roots so crash dumps post-disconnect don't leak live credentials. True in-place zeroing requires fetch-on-demand — deferred to phase 4.
- [x] **Out-of-process Roslyn sandbox design doc** — new `SANDBOX_DESIGN.md` specs the worker-process IPC contract, per-platform OS sandbox (Windows AppContainer, macOS `sandbox-exec`, Android `isolatedProcess`, Linux seccomp-bpf, iOS deferral), resource quotas, threat-model delta, and 5-week rollout plan. Design only.

### Phase 4 — Track A (complete 2026-04-17)
- [x] **iOS `.atpkg` and script compile refusal (A1)** — `CustomScriptsModal.razor` guards `ImportAtpkgFromFile`, `ImportAtpkgJson`, and `CompileScript` with a `DevicePlatform.iOS` check. Every path into `RoslynScriptingService.CompileIndicatorAsync` is refused outright on iOS; textarea still works for editing.
- [x] **Manifest target runs on every config (A2a)** — dropped the Release-only condition so Debug builds also produce `plugins_trusted.manifest`. Keeps the dev workflow in sync with the new shipping default.
- [x] **`PluginTrustPolicy.RequireTrusted` default flipped to `true` (A2b)** — a missing manifest now refuses every plugin (intentional fail-closed). `ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS=1` env var bypasses with a loud warning; `ACCESSIBLETRADER_REQUIRE_TRUSTED_PLUGINS=1` kept for back-compat.
- [x] **GitHub Actions workflow `plugin-manifest.yml` (A2c)** — PR + push + tag triggers, Windows Release build, sanity-checks manifest has ≥10 hash entries, uploads as workflow artifact, attaches to GitHub Release on `v*` tags.

### Phase 4 — Track B (complete 2026-04-17)
- [x] **`IApiKeyCheckout` + `PluginHostServices.ApiKeys` (B0+B1)** — SDK interface + host adapter + Kraken canary. Per-request checkout with graceful fallback to Configure-populated fields when the host bridge is null. Best-effort `Array.Clear` on the decoded HMAC secret after signing. Migration recipe in `CREDENTIAL_CHECKOUT_MIGRATION.md`.
- [x] **`IPluginHttpClientFactory` + `PluginHostServices.HttpClientFactory` (B0+B2)** — SDK interface + host adapter + outbound-host allow-list `DelegatingHandler`. All 12 analytics providers migrated to `PluginHostServices.CreateHttpClient(providerId, allowedHosts)`.
- [x] **Remaining trading providers migrated (future)** — Binance / Coinbase / Bitstamp / Alpaca / Schwab / IBKR stay on the phase-3 scrub-on-disconnect pattern. Status matrix + per-provider notes in `CREDENTIAL_CHECKOUT_MIGRATION.md`. Drive migration order by actual user exposure; Coinbase + Bitstamp are the cleanest next canary candidates since they have explicit sign-per-request code like Kraken.

### Phase 4 — Track C (process-boundary landed 2026-04-17)
- [x] **Worker skeleton + stdio IPC (C1)** — new `AccessibleTrader.ScriptSandbox` contract library + `AccessibleTrader.ScriptWorker` console app. Binary frame codec (4-byte length + 1-byte opcode + payload up to 64 MB), opcode enum, tight DTO codec for metadata / CalculateRequest / CalculateResponse. Worker loads assemblies into a collectible ALC; one indicator per worker lifetime.
- [x] **`IScriptWorkerLauncher` abstraction** + `DefaultProcessLauncher` that spawns the worker unsandboxed via `Process.Start`. Per-platform launchers (C2/C3/C4) plug in behind the same interface.
- [x] **Host supervisor (C5)** — `OutOfProcessScriptHost` owns the process handle, serializes stdin writes, streams stderr to the logger, enforces per-call wall-clock timeouts (5 s Calculate / 10 s LoadAssembly), kills the worker on timeout via `Process.Kill(entireProcessTree: true)`, sends `Shutdown` frame with a 1-second grace window on disposal.
- [x] **Rewire `RoslynScriptingService` (C6)** — `CompileIndicatorAsync` returns an `OutOfProcessIndicator` proxy by default; `ACCESSIBLETRADER_SCRIPT_IN_PROCESS=1` opts into the legacy in-process path for breakpoint debugging. `UnloadScript` disposes the out-of-process host cascading to worker kill. Cached-scripts recompile note in CHANGES.
- [x] **Roundtrip integration test** — `OutOfProcessScriptingTests.Roundtrip_TrivialIndicator_EchoesClosePrices` exercises the full Roslyn-compile → worker-spawn → stdio-roundtrip → proxy-Calculate → clean-UnloadScript path. Suite is 258/258 passing.
- [x] **Build wiring** — both new projects added to `AccessibleTrader.slnx`; `BlazorClient.csproj` / `Tests.csproj` reference the worker with `ReferenceOutputAssembly=false`; new `CopyScriptWorker` MSBuild target copies the worker output next to the host binary at build time.

### Phase 4 — Track C follow-ups (OS-level sandboxing, 2026-04-17 complete)
- [x] **Windows AppContainer launcher (C2)** — full `CreateProcessW` +
  `STARTUPINFOEX` + `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` wiring
  with manually-managed inheritable pipes, backed by
  `AppContainerScriptWorkerProcess : IScriptWorkerProcess`. Profile
  management via `userenv.dll` (`CreateAppContainerProfile` /
  `DeriveAppContainerSidFromAppContainerName`); cached SID reused across
  launches. `SandboxApplied` returns `true` on success, `false` with
  `LastCreateProcessError` populated on `ERROR_ACCESS_DENIED` (dev-box
  ACL gap) so dev builds fall back gracefully to the default launcher.
- [x] **macOS / Mac Catalyst sandbox (C3)** — `MacSandboxExecLauncher`
  ships + `AccessibleTrader.ScriptWorker/sandbox-profiles/script-worker.sb`
  deny-default profile.
- [x] **Android `isolatedProcess` (C4)** — `ScriptWorkerService` bound
  service with `[Service(IsolatedProcess=true)]`; `Messenger`-based
  IPC transfers two `ParcelFileDescriptor` pipe ends. Real launcher in
  `AccessibleTrader.BlazorClient/Platforms/Android/AndroidIsolatedProcessLauncher.cs`
  binds the service + hands host-side `FileStream`s over the pipes to
  `OutOfProcessScriptHost`. MAUI wires the platform launcher into DI on
  Android builds; Core-side routing stub throws if mis-wired.
- [x] **Hostile-script smoke tests** — `HostileScriptTests` (6) compile
  indicators attempting `File.ReadAllText` / `HttpClient.GetStringAsync`
  / `Process.Start` / unsafe / `[DllImport]` / `Assembly.LoadFrom` and
  assert `CompileResult.Success == false`. Covers the in-worker Roslyn
  sandbox layer; OS-sandbox layer still needs on-target integration
  tests (run-on-device / run-on-AppContainer harness) but the
  defense-in-depth first line is covered.
- [x] **Resource quotas beyond wall-clock (C5)** — `OutOfProcessScriptHost`
  polls `WorkingSet64` every 2 s, kills on overage (default 256 MB).
- [x] **Track B1 follow-ups** — Bitstamp + Coinbase per-request checkout;
  Alpaca + Binance per-connection-lifecycle; Schwab / IBKR N/A. Status
  matrix in `CREDENTIAL_CHECKOUT_MIGRATION.md`.
- [x] **Cross-TFM build errors** — NETSDK1150 on iOS/Android/macCatalyst
  fixed via `ProjectReference` + `CopyScriptWorker` TFM guards. Inline
  `HashPluginDlls` task's `SHA256.HashData` swapped for
  `SHA256.Create().ComputeHash` so it compiles under every supported
  MSBuild runtime. `GeneratePluginTrustManifest` guarded on non-empty
  `$(OutDir)` for aggregate multi-TFM builds.
- [x] **Warnings** — every CS warning across the solution is addressed.
  Full Release build is 0 warnings / 0 errors.

### Remaining follow-ups (optional, no security impact)
- [ ] **On-device / on-AppContainer integration tests** — a test
  harness that, on the target platform, compiles an indicator which
  reaches for `File.WriteAllText` via a trick the Roslyn sandbox
  misses and asserts the OS sandbox blocks at runtime. Requires CI on
  each platform; today's xunit suite covers the Roslyn layer only.
- [~] **Hot-path credential cache** — per-provider 60s session cache if
  per-request `CheckoutAsync` latency becomes user-visible on Android
  KeyStore. Measure first. **Measurement layer shipped 2026-04-24:**
  `CheckoutLatencyTracker` (per-provider rolling window of 256 samples,
  P50/P95/P99/Max via NIST-handbook interpolation) wired into
  `MauiApiKeyCheckoutAdapter`. Pending: a session of live data on
  Android device + the JournalModal surface to read out the percentiles.
  If sustained P95 stays under 15 ms the item closes as "no cost, no
  fix needed"; over 15 ms green-lights the session-cache implementation.
- [x] **macCatalyst scripting refusal** — shipped 2026-04-24. Rather than
  silently falling through to the in-process path,
  `RoslynScriptingService.CreateDefaultLauncher` now returns a
  `RefusingScriptWorkerLauncher` on macCatalyst that throws
  `ScriptingNotSupportedOnPlatformException` at launch time (same refusal
  as iOS, which joined explicitly here too). Dedicated macCatalyst worker
  packaging remains an open enablement item for a future session if Mac
  desktop users ever demand script support.

### Post-phase-4 polish (2026-04-17, complete)
- [x] **Security event audit log** — `ISecurityEventLog` +
  `SecurityEventLog` ring-buffer impl. Instrumented at AppContainer
  fallback, memory-quota kill, Calculate timeout, Schwab token-cleanup
  failures. Mirrors to `ILogger<T>` at Warning level.
- [x] **Schwab silent `catch {}` closed** — three of five
  `File.Delete` swallows on the explicit scrub path now record
  `TokenCleanupFailed` events.
- [x] **`StrategyBacktester` UTC filenames** — `DateTime.Now` →
  `DateTime.UtcNow` with `Z` suffix.
- [x] **CI test gate** — `.github/workflows/tests.yml` runs the full
  264-test xunit suite on every PR/push.
- [x] **HttpClient factory migration for trading + LLM providers** —
  13 trading providers (minus IBKR and Binance, documented
  exceptions) and both LLM providers now build `HttpClient` via
  `PluginHostServices.CreateHttpClient` with per-provider outbound-
  host allow-lists. WS endpoints stay on `ReconnectingWebSocket` with
  its own 16 MB frame cap.

### Next priorities (broader codebase audit — 2026-04-17)
- [~] **Phase 5 — financial `double` → `decimal` migration.** Every
  money-path record (`Ohlcv`, `OrderUpdate`, `Balance`, `Position`,
  `OrderBookEntry`) uses `double`, which accumulates binary-float
  rounding across ticks, fills, and P&L aggregation. Schema change
  across every provider, every indicator, the backtester, storage
  serialization. Its own dedicated phase.

  **Tier A.5 decision (2026-04-23):** reframed and deferred. Full
  migration would touch 14 trading providers, every `ITradingProvider`
  record, the full StrategyBacktester arithmetic, every position sizer,
  and backtest serialization — multi-day refactor. No reproducible
  bug motivates it right now: float drift per-op is ~1e-15, the
  display layer is now magnitude-aware (2026-04-23 sub-cent fix), and
  Kelly's clamps absorb sub-penny drift. Re-open when the codebase
  moves toward automated live trading with cumulative fill
  accumulation over many sessions — the only scenario where float
  drift is material in practice.
- [ ] **Phase 5 — accessibility modal rework.** `ChartArea.razor`
  needs explicit `@onkeydown` binding; `OrderBookModal.razor` needs
  `role="status" aria-live="polite"` regions and sonification hooks
  for depth changes. This is the product's reason to exist.
- [x] **CPU quota on script worker** — shipped 2026-04-24.
  `DefaultMaxCpuFraction = 0.9`; polls `TotalProcessorTime` delta vs
  wall-clock every 2 s; sustained overage triggers kill + security
  event + descriptive Calculate-side exception.
- [x] **Per-user worker-count limit** — shipped 2026-04-24.
  `DefaultMaxConcurrentWorkers = 16` with atomic counter gate in
  `StartAsync`/`DisposeAsync`. Configurable via `SetMaxConcurrentWorkers`.
- [~] **Provider unit-test coverage** — rounds 1-4 shipped 2026-04-24.
  `ProviderTimeframeContractTests` (31 tests) pins every provider's
  NativelySupportedTimeframes against TimeframeUtility;
  `ProviderSymbolNormalisationTests` covers wire-format transforms;
  `Fakes/FakeHttpMessageHandler` + `Fakes/FakeApiKeyCheckout` shipped as
  fixtures; `ProviderFetchOhlcvTests` (39 tests across Bitstamp / Polygon /
  Tradier / Coinbase / AlternativeMe / Mempool / DefiLlama / OkxDerivatives /
  Glassnode / Etherscan / Fred / BinanceDerivatives / BGeometrics /
  CoinMetrics) drives FetchOhlcvAsync end-to-end via reflection-swapped
  HttpClient. `ProviderLiveStreamTests` (16 tests across Bitstamp /
  Coinbase / Polygon) reflects into private `HandleWebSocketMessage(string)`
  and asserts on public IObservable streams.
  **Remaining:** Binance / MEXC are SDK-managed (HttpClient lives inside
  the SDK — would need an adapter layer); Alpaca / Kraken / Oanda need
  credential-checkout fakes wired through PluginHostServices.ApiKeys for
  the full parse path; remaining WS providers (Kraken, Binance, MEXC,
  Schwab streamer, IBKR gateway) each need 5-6 live-stream tests in the
  same shape; CoinGecko / BinanceVision / FmpAnalytics still need ~3
  fetch tests each.
- [x] **Silent `catch {}` sweep (Tier A.1 — 2026-04-23)** — shipped. Upgraded 9 user-facing silent catches to diagnostic `Debug.WriteLine` / `_logger.LogDebug`: `AlertEvaluator` (alert-rule failure), `AIAnalystService` (screenshot encode failure), and 7 provider feed parsers (Alpaca ×2, Finnhub, InteractiveBrokers, OANDA ×2, Polygon). Teardown/Dispose swallows and `OperationCanceledException` swallows retained as legitimate.
  codebase-wide. Most are correct (malformed WS frame, best-effort
  cleanup); the rest should at minimum log. Prioritized by call-path
  impact.

### Phase-4 operating assumptions (confirmed 2026-04-17)
- **Timeline:** open-ended; complete as we go.
- **CI platform:** GitHub Actions.
- **Credential checkout cadence:** default per-request; opt-in 60s session cache for tick-rate hot paths.
- **iOS stance:** full refusal (no consent prompt).
- **Binary size:** no ceiling (worker exe is fine).
- **Cached-script compat:** OK to break on the out-of-process release; ship a recompile note.

### Cross-cutting (2026-04-17)
- [x] **Ichimoku targeted metadata tests** — replaced the stale `GetMetadata_Returns5Components` count assertion with `Components_ContainClassicalFiveLines`, `Components_ExposeHiddenKumoPolarityHelper`, `Components_ExposeVisibleTkCrossMarkers`, and a sentinel `Components_CountMatchesDeclaredContract`. Tests now pass 256/256 (up from 252/253) and encode the actual component contract so regressions name which piece broke instead of just "count changed".

---

## PRIOR (2026-04-11 Evening) — OB/OS fix, strategy cleanup, BinanceVision promotion

### Completed this session
- [x] **OB/OS zone band architecture** — `ZoneBandConfig` extended with `FixedTop`/`FixedBottom`/`IsFixedMode`. `RenderZoneBand` paints full-viewport rectangle in fixed mode. Cipher B refactored to `DefaultZoneBands` (2 bands: OB +53..+100, OS -53..-100). Deleted `CompZoneCeiling`/`CompZoneFloor` phantom components. **The OB/OS shading bug is fixed** — it was trying to do visual work through the data-component pipeline.
- [x] **Strategy cleanup** — 14 dead builders purged (`CryptoFaceLong`, `V3`, `V4Claude`, `V5`–`V12`, `V13ShortBearDivBelowSma200`). File shrank 3339 → ~1100 lines. v13s removal confirmed by fresh walk-forward (BTC 1d -0.132R, BTC 4h -0.439R Sharpe -8.92).
- [x] **v18 Refined Short** — Hidden Bear Continuation + below SMA200 + crowded-long funding. First cross-asset short survivor. DOGE 4h 14T+15T 64–67% WR. BTC 4h H1+H2 both positive. XRP 1d H2 100%.
- [x] **v21 MVRV Capitulation Trilogy** — v16 trilogy + `COINMETRICS.MVRVRegime < 2`. Positive on ETH 4h, XRP 4h, BTC 1d H1.
- [x] **v19 + v20 attempted and deleted** — both failed BTC 4h H2.
- [x] **Walk-forward matrix** — 5 strategies × 5 assets × 2 TF = 50 tests. Results in `AccessibleTrader.StrategyLab/walk_forward_results.json` + session memory.
- [x] **BinanceVision plugin promoted** — `Plugins/Analytics/AccessibleTrader.Plugins.BinanceVision/BinanceVisionProvider.cs` fetches `data.binance.vision` monthly ZIPs (~6 years history, free, no API key). Exposes `{PAIR}USDT_FUNDING` / `{PAIR}USDT_OI` for 8 majors. Funding ×100 normalized at boundary. Registered in `.slnx` + `BlazorClient.csproj`.
- [x] **Core indicators repointed** — `FundingRateProvider`, `OpenInterestProvider`, `CrowdingIndexProvider` all switched from OkxDerivatives (11 days) → BinanceVision (6 years). Live app now has deep free derivatives data.
- [x] **Deep OHLCV snapshots** — BTC/ETH/XRP pulled to 20000 bars (2017 → 2026), SOL/DOGE pulled to Bitstamp history depth (SOL 2022-08, DOGE 2022-12). BTC 4h + ETH/SOL/XRP/DOGE 4h all refreshed.
- [x] **Extended BinanceVision DOGE/ADA/LTC support** — `BinanceVisionFundingCommand.SymbolStartMonths`, `BinanceVisionOiCommand.SymbolStartDates`, and both lab providers' asset-resolution whitelists.

### Open gaps — next session
- [x] **Funding snapshot scale rewrite (2026-04-23)** — 8 files in `strategy-lab-data/` rewritten ×100 via idempotent PowerShell (`ScaleAppliedPercent: true` marker guards re-runs). Threshold-based strategies (v18 `Funding > 0.05`) now fire identically in lab vs live.
- [x] **Asset-aware Core FundingRate / OpenInterest / CrowdingIndex** — shipped 2026-04-23. `IndicatorOrchestrator` stamps `parameters["__symbol"]` from `state.Identity.Symbol` on both full-recalc and tick-update paths. Each provider's new private `BuildRequest`/`BuildRequests` helper derives the cross-series symbol per-call (normalises `/`, `-`, appends `USDT` for bare bases, falls back to BTCUSDT when the hint is absent). Tests: `AssetAwareCrossSeriesTests.cs` (15).
- [ ] **Delete redundant BNVISION_FUNDING / BNVISION_OI lab providers** — now duplicating what Core FUNDING_RATE / OPEN_INTEREST provide. Still referenced by v18/v21 strategy leaves. Once v18/v21 are migrated to `FUNDING_RATE.Funding Rate` leaf, the lab providers can be deleted along with their command files.

### Uncommitted work
- [ ] **Commit this session's work in logical groups**:
  - (a) Zone band + OB/OS fix (ZoneBandConfig, StandardRenderers, CipherBProvider)
  - (b) Dead strategy cleanup (v1-v12 + v13s + v19 + v20 deletion)
  - (c) New strategy seeds (v18 Refined Short + v21 MVRV Capitulation Trilogy)
  - (d) BinanceVision plugin creation + Core indicator repointing + slnx/csproj registration
  - (e) StrategyLab BinanceVision extension for DOGE/ADA/LTC/BNB
  - (f) Deep OHLCV snapshot refresh + cross-series data

### Strategy work — future
- [ ] **Cross-asset matrix rerun with v18 asset-aware**: once Core providers accept `__symbol`, re-run v18 on ETH/SOL/XRP/DOGE in LIVE mode to verify parity with StrategyLab.
- [ ] **v13/v14/v15 cross-asset walk-forward** — never tested on non-BTC. May reveal additional survivors or confirm BTC-specialist pattern.
- [ ] **Divergence line rendering** — real MCB draws slanted line between pivots; currently only a diamond at the 2nd pivot. Renderer feature, deferred.
- [ ] **Cross-pane Anchor cloud** — tint price pane background with anchor regime color. Currently only in oscillator pane.
- [x] **Schwab UI sign-in button (2026-04-23)** — per-row "Sign in" button added to `ApiKeysModal` for Schwab profiles. Activates the profile, reaches the provider via `IDataService`, invokes `BeginAuthorizationAsync` through reflection (keeps UI off the plugin hard-dep), publishes start/success/failure feedback for screen-reader users.

---

## Prior Session — 2026-04-11 afternoon (Cipher B fidelity + trilogy strategies)

### Completed
- [x] Cipher B full MCB-fidelity rewrite (body/range MF, WT Histogram, K-of-N gold, depth gate, alt divergence detector, anchor suppression, TF-aware gates)
- [x] Visual polish (histogram saturation, anchor cloud opacity, MF sqrt expansion, dot hierarchy)
- [x] v13 / v14 / v15 / v16 / v16s / v17 long/short trilogy strategy seeds
- [x] v12 retired from seeds (Anchor-sign thesis invalidated by the rewrite)
- [x] StrategyLab DI fix (`LabHost.Build()` registers `ILoggerFactory`)
- [x] `DiagnosticCommand --side long|short` flag
- [x] Schwab provider plugin (OAuth2, EQUITY market/limit/stop orders, 120 rpm limiter)

---

## Previously active — Build-Out (2026-04-10)

### Completed Session 1
- [x] BGeometrics plugin — 28 BTC on-chain symbols (MVRV, SOPR, NVT, NUPL, CDD, Hodl Waves, S2F, etc.)
- [x] CoinMetrics live plugin — 117 symbols across 9 assets (MVRV, active addresses, hash rate, exchange flows)
- [x] DefiLlama plugin — DeFi TVL (10 chains, 8 protocols), stablecoin supply (USDT/USDC/DAI/total)
- [x] Mempool plugin — BTC hashrate, difficulty, block fees/rewards/sizes/fee rates
- [x] Etherscan plugin — ETH gas oracle, supply, price, node count
- [x] FMP plugin — Stock/Crypto/Forex/Commodity/Index OHLCV with intraday
- [x] FMP Analytics plugin — fundamentals, ratios, earnings, sector performance, economic calendar
- [x] Full code quality overhaul (SafeFireAndForget, structured logging, disposal, ConfigureAwait, sandbox hardening)

### Completed Session 2
- [x] IAnalyticsDataResolver — 30 metrics, priority-ordered provider resolution, API key awareness
- [x] ApiKeysModal — expanded to 19 providers
- [x] LiveStreamManager auto-reconnect — 5 attempts, tear-down/reconnect/re-subscribe
- [x] InsideCloud operator fix — reads both CloudFillConfig bounds, proper inside evaluation
- [x] Plugin directory restructure — Providers/Analytics/Indicators subdirectories
- [x] Dynamic indicator plugin discovery — scan Plugins/Indicators/ at startup
- [x] PROVIDER_AUTHORING.md — complete data provider authoring guide
- [x] PropertiesModal per-component picker — dropdown filter for 3+ component indicators
- [x] Parameter validation — MinValue/MaxValue/Step on IndicatorParameterMetadata, clamp on edit
- [x] TrailByAtr stop adjustment — Wilder ATR trailing stop in backtester after TP1
- [x] Cloud component architecture — navigable, sonified, speech-announcing, auto-narrating clouds
- [x] MACloudProvider — 6 MA types (EMA/SMA/WMA/HMA/DEMA/TEMA), replaces EmaFillProvider
- [x] MovingAverageHelper — shared utility replacing 3 duplicate Ema() implementations

### Research / Next Session
- [ ] Adaptive WT thresholds for Cipher B — dynamic OB/OS levels based on oscillator's own distribution (percentile-based)
- [ ] Pulse indicator simplification — consider decomposing v1/v2/v3 signal tiers into strategy conditions
- [ ] Phase 12 Session 3 — v9 backtest + thesis validation
- [ ] Commit all uncommitted work (~120+ files modified/created across 2 sessions)

### Data Landscape Reference (updated)
Free: BGeometrics (BTC 154+ metrics), CoinMetrics Community (9 assets MVRV), DefiLlama (TVL, stablecoins), Mempool.space (BTC mining), Etherscan (ETH gas/supply), OKX public (307 perps funding/OI), Binance public (derivatives), Alternative.me (FNG), CoinGecko (dominance), FRED (macro), FMP free tier (250 req/day).
Paid only: CoinGlass ($29+/mo, no free tier), CryptoQuant ($109+/mo for API), Glassnode (API requires paid plan + add-on), FMP paid ($14-79/mo for higher limits).
Gaps: ETH missing SOPR/NVT/exchange flows (paid only). SOL/AVAX no on-chain metrics free. KAS no data anywhere. TAO derivatives only (OKX).

---

## PHASE 11 — Strategy Composer & Risk-Managed Setups (multi-session)

A user-buildable signal composer that combines indicator components from any registered indicator, evaluates them as an AND/OR/NOT condition tree, gates the result on a reward/risk plan with TP ladders, and announces every step (initial setup, re-confirmation, dropouts) through bells and speech. The output is reviewable in the Journal modal (Ctrl+Alt+Shift+J).

### Session A — Foundation (2026-04-07) — DONE

- [x] **Bell earcons:** `setup_long_bell` (sine + perfect-fifth chord) and `setup_short_bell` (triangle + sub-octave) registered in `SoundPatchRegistry`. `IEarconService.PlaySetupBell(side, reconfirmation)` renders them as one-shot `ISonificationManager.PlayNote` chords.
- [x] **Journal modal (Ctrl+Alt+Shift+J):** `IJournalService` ring buffer (2000 entries) auto-subscribing to `StrategySignalEvent` / `AlertFiredEvent` / `AppErrorEvent`. `BlazorSpeechManager.Speak()` mirrors every TTS phrase. `JournalModal.razor` console-style filterable copyable text view. (Initially Ctrl+J — corrected to Ctrl+Alt+Shift+J 2026-04-07.)
- [x] **Backtester warmup gate:** `BacktestConfig.WarmupBars` (default 200), `BacktestResult.WarmupBars` / `EvaluatedBars`, signals dropped during warmup, modal input + display.
- [x] **Sdk types:** `SignalDescriptor` + `SignalKind`, `ConditionTree` (`ConditionNode`/`ConditionLeaf`/`ConditionGroup`/`LogicOperator`/`LeafOperator`/`ConditionEvaluation`), `RiskPlan` (4 stop sources + 4 Phase-4 stubs / 3 target sources + 5 stubs / TP ladder / sizing modes / `EntryTrigger` / `MinRewardRiskRatio` gate / `ResolvedRiskPlan`), `StrategySpec`. `ConditionLeaf.Timeframe` foundation field for MTF.
- [x] **Core services:** `ISignalCatalog` walks `IIndicatorProvider.GetIndicators()`, `IConditionEvaluator` (no AND short-circuit so per-leaf result map is complete), `IRiskPlanResolver` (Wilder ATR, percent, swing low, fixed; RR multiple, percent, fixed; FixedRiskPercent / FixedRiskCash / FixedQuantity sizing).
- [x] **`ConfigurableStrategy : BaseStrategy`** with the inactive→active→reconfirm→dropout state machine, dropout label resolution via the catalog.
- [x] **`IConfigurableStrategyFactory`** + **`JsonStrategyLibrary`** (System.Text.Json `JsonPolymorphic` discriminator `$kind` for round-trip).
- [x] **Setup events:** `SetupConfirmedEvent` / `SetupReconfirmedEvent` / `SetupDroppedEvent` in Events.cs.
- [x] **`SetupSonifier`** subscribes to all 3, plays bell + speech. Eagerly resolved via MainLayout `@inject`.
- [x] **DI registration** in `ServiceCollectionExtensions.AddBusinessServices`.

### Session B — Multi-timeframe data + adaptive backtester history + entry-armed state machine (2026-04-07) — DONE

- [x] **`IMultiTimeframeDataService` + `MultiTimeframeDataService`** wrapping `IDataOrchestrator.FetchOhlcvAsync` (already cache-backed via SQLite + Polly). In-memory `(provider|symbol|timeframe)` cache with bar-size-proportional TTL. `GetBarsAsync` populates, `GetCachedBars` is the sync hot-path read for the evaluator.
- [x] **HTF leaf routing in `ConditionEvaluator` (price-only subset):** `ConditionLeaf.Timeframe` triggers HTF cached lookup; price comparisons (`GreaterThan` / `LessThan` / `Between` / `CrossesAbove` / `CrossesBelow`) evaluate directly against HTF bars. Indicator-on-HTF computation falls through to active-TF with a one-time warning — needs sync indicator runner or pre-warm cache, deferred to Session C.
- [x] **`IBacktestWarmupAnalyzer` + `BacktestWarmupAnalyzer`** walks `StrategySpec` condition tree, collects unique indicator codes, queries each provider's `GetStabilityWindow`, returns `max × 1.2` (or floor). `ReferencedIndicators` sibling helper.
- [x] **R-multiple metrics on `BacktestResult`:** `AverageR`, `Expectancy`, `ProfitFactor`, `AverageBarsInTrade`, `LongestLosingStreak`. `BacktestTrade` extended with `StopPrice` and `BarsInTrade`. `StrategyBacktester` tracks `openStop` + `openBarIndex` and computes per-trade R = `reward / |entry - stop|`. Speech summary includes Average R when known.
- [x] **Entry-armed state machine in `ConfigurableStrategy`:** new `SetupState` enum (Inactive / Armed / Active). Inactive→Armed when EntryTrigger != Immediate; Armed→Active on trigger fire. `OnPullbackToLevel` / `OnBreakoutOf` / `OnNextNCandleClose` trigger evaluation. No setup expiration. Heartbeat `SetupReconfirmedEvent` while armed.
- [x] **`SetupArmedEvent` + `SetupEntryReachedEvent`** added to `Models/Events.cs`.
- [x] **`IEarconService.PlaySetupArmed` + `PlaySetupEntryReached`** — distinct earcons for the armed-waiting state and the entry-reached state. SetupSonifier subscribes and routes.
- [x] **DI registration** — `IMultiTimeframeDataService` and `IBacktestWarmupAnalyzer` registered as singletons in `AddBusinessServices`.
- [x] **Journal shortcut corrected** to `Ctrl+Alt+Shift+J` from initial `Ctrl+J`.

**Still pending in this scope (deferred to Session C+):**
- [x] **HTF indicator computation (Tier A.2 — 2026-04-23)** — infrastructure (PrewarmIndicatorAsync + GetCachedIndicator + ConfigurableStrategy.Initialize + pre-warm gate) was already wired. Closed the last gap: `MultiTimeframeDataService.PrewarmIndicatorAsync` now calls a new `BuildDefaultParameters` helper when the caller passes an empty parameter dict, looking up `IndicatorMetadata.Parameters` defaults from the indicator provider. Was previously passing an empty dict which made some providers emit all-NaN arrays. Regression pinned by `ConditionEvaluatorHtfTests.cs`.
- [x] **Adaptive warmup auto-apply in StrategyModal (Tier B.3 verified 2026-04-23)** — already shipped. `StrategyModal.razor`'s `AutoWarmup()` wires the "Auto" button; `BuildSetupTab.razor:992` auto-applies `WarmupAnalyzer.RecommendedWarmup(spec)` in the preview flow.
- [x] **Pre-warm of HTF data on strategy add** — shipped in Session C+
  (the infrastructure was already in place; TODO entry was stale).
  `ConfigurableStrategyFactory` optionally injects `IMultiTimeframeDataService`;
  `ConfigurableStrategy.Initialize` collects the unique `(Timeframe, IndicatorCode)`
  pairs from the condition tree and fire-and-forgets `PrewarmIndicatorAsync`
  per pair plus `GetBarsAsync` per unique HTF timeframe. The
  `IsPrewarmComplete` gate blocks `OnBar` evaluation until every prewarm
  task finishes — otherwise NaN reads on unwarmed HTF leaves silently flip
  condition results. Pinning tests added 2026-04-24
  (`ConfigurableStrategyPrewarmTests.cs`, 4 tests): per-pair collapse,
  no-HTF-leaf fast-path, null-MTF tolerance, gate-flips-after-completion.

### Session C — Support / resistance + volume profile as condition + risk sources (2026-04-07) — PARTIAL

- [x] **`ILevelProvider`** abstraction with `PriceLevel` record (NB: named PriceLevel, not LevelDescriptor — name collision with Sdk.Models.LevelDescriptor for indicator default reference levels). `LevelKind` enum: Support / Resistance / Pivot / Poc / Vah / Val / Hvn / Lvn / Vwap / Kijun / KumoTop / KumoBottom.
- [x] **`ILevelService` aggregator** with `GetAllLevels` / `NearestBelow(kindFilter?)` / `NearestAbove(kindFilter?)`.
- [x] **`DrawnHorizontalLevelProvider`** — reads workspace drawings (Horizontal / TrendLine endpoints / Rectangle edges / RiskReward anchors), classifies as Support/Resistance based on current price.
- [x] **`SwingPivotLevelProvider`** — algorithmic swing-high/low detection from raw OHLCV (LookbackBars=5, MaxPivots=12 newest-first). Fallback when nothing else is loaded.
- [x] **`IchimokuLevelProvider`** — exposes Kijun-sen + KumoTop + KumoBottom from the active Ichimoku series.
- [x] **`CipherSrLevelProvider`** — walks the Cipher SR Resistance/Support component arrays for the last 200 bars; recency-weighted strength.
- [x] **Phase-4 stop sources implemented:** `BelowSupport`, `BelowKijun`, `BelowKumo` — all wired through ILevelService. `BelowLvn` still returns null pending VPVR.
- [x] **Phase-4 target source: `NextResistance`** wired through ILevelService.
- [x] **New leaf operators:** `PriceRejectsLevel` (touch + close-away within N bars + tolerance), `PriceBreaksLevel` (open/close straddle a level), `BarClosesAbovePoc` / `BarClosesBelowPoc` (defined, dormant until VPVR provider ships).
- [x] **VPVR / TPO level provider** — `VolumeProfileLevelProvider` walks `series.ProfileBins` (which IS populated eagerly by IndicatorOrchestrator, contrary to earlier belief — not render-time only). Emits POC / VAH / VAL / HVN / LVN with same thresholds as ProfileBinClassifier (HVN: `IsValueArea && volume > mean × 1.3`; LVN: `IsSinglePrint || volume < mean × 0.4`).
- [x] **Phase-4 stop source `BelowLvn`** — wired through `NearestBelow(kindFilter: Lvn)`.
- [x] **Phase-4 target sources `NextHvn` / `Poc` / `Vah`** — wired through nearest-by-kind lookups.
- [x] **`FibExtension` target source** — pure history-derived: lowest low + highest high in last 50 bars, validates impulse direction, projects entry + range × FibLevel.
- [x] **Leaf operators `PriceInsideValueArea` / `PriceOutsideValueArea` / `WickIntoLvn`** — implemented in ConditionEvaluator.
- [x] **Future-leak fix on indicator-derived providers:** `IchimokuLevelProvider` and `CipherSrLevelProvider` now clip component-data scans to `min(history.Count, data.Length)`. Strategy at backtest bar 100 no longer sees Ichimoku/Cipher SR values from bars in the future.
- [x] **Backtester profile-state replay** — `IBacktestProfileCache` + `BacktestProfileCache` ambient cache, `VolumeProfileLevelProvider` reads from cache when active, `StrategyBacktester` recomputes bins per bar via `IProfileService.CalculateVolumeProfile/MarketProfile(historyBuffer)` when `BacktestConfig.ReplayProfiles=true` (default). Cache cleared in try/finally so live evaluations after the run fall through to live `series.ProfileBins`.
- [x] **HTF indicator computation** — `IMultiTimeframeDataService.PrewarmIndicatorAsync` + `GetCachedIndicator`, uses `IIndicatorEngine.CalculateAsync` (one-shot), `ConfigurableStrategy.Initialize` walks tree and fire-and-forgets pre-warm for every unique (Timeframe, IndicatorCode) pair, `ConditionEvaluator` checks the cache first then falls through to price-only HTF path.

### Path A Correctness Pass (2026-04-07) — DONE

- [x] **Fixed `ConditionEvaluator` main-path future-leak** — was reading `data[^1]` from the full series array, surfacing final-bar values at every backtest bar. Now clips reads to `Math.Min(history.Count, data.Length) - 1`. `FiredWithin` and `DirectionChanged` updated with `historyCount` parameter.
- [x] **Fixed `StrategyBacktester` passing `WorkspaceState.Initial` (dummy)** — `IStrategyBacktester.RunAsync` now takes optional `WorkspaceState? state = null`. `StrategyModal.RunBacktestAsync` passes `Store.State`. Without this fix, ConfigurableStrategy backtests were silently broken because they read `state.ActiveSeries` and the dummy state had none.
- [x] **`BacktestConfig.ReplayProfiles`** flag (default true) — gates the per-bar profile recomputation in StrategyBacktester. Set to false for fast iteration on strategies that don't gate on profile levels.
- [x] **`ConfigurableStrategy` ctor + factory** carry `IMultiTimeframeDataService` through. `Initialize` triggers pre-warm.

### Session D — Builder UI in StrategyModal (2026-04-07) — DONE

- [x] **Modal input trap fix** — `CommandDispatcher` subscribes to `ModalStateChangedEvent` with a counter, suppresses chart commands while any modal is open. Allowlist preserves F1 (Help), F2 (toggle speech), F3 (toggle sonification) for accessibility.
- [x] **`BuildSetupTab.razor`** new component, hosted by a "Build Setup" tab in `StrategyModal.razor` (between Add Strategy and Active). Lazy-mounted via `@if (_activeTab == "build")`.
- [x] **ARIA tree** (`role="tree"` + `treeitem` + `aria-level` + `aria-expanded` + `aria-selected`) replacing the rejected nested-list pattern. Each tree item has inline `+ leaf` / `+ group` / `×` buttons.
- [x] **Cascading combo-box leaf editor** below the tree: Indicator → Component → Operator → Value → optional Upper Bound → optional Within-N → Timeframe → Score. Operator dropdown gated by the descriptor's `SignalKind`.
- [x] **Risk plan section** — full UI for all 8 stop sources, TP ladder editor (default 3 rungs), R:R minimum, sizing mode + parameters, notional equity, entry trigger.
- [x] **Save / Load / Add to Engine** via `IStrategyLibrary` + `IConfigurableStrategyFactory` + `IStrategyEngine.AddStrategy`.
- [x] **Preview button** — runs warmup-aware backtester with `ReplayProfiles=false` for fast iteration; results displayed inline (trades, win rate, P&L, avg R, profit factor, max drawdown, warmup/evaluated). Manual trigger rather than auto-debounce-on-edit (cost too high on long charts; the `_previewTimer` field is preserved as a hook for future polish).
- [x] **Read aloud button** — `NarrateSpec()` walks the editable tree and emits a plain-English sentence; speaks via `ISpeechManager.Speak(interrupt: true)`. Mirrors automatically into the journal.
- [x] **Auto-apply `IBacktestWarmupAnalyzer`** in Backtest tab — "Auto" button resolves the spec by name match and sets warmup to the analyzer's recommendation.
- [x] **`CrossesAboveLine` / `CrossesBelowLine` second descriptor refs** — `ConditionLeaf.SecondSignalDescriptorId` added, evaluator implements MA-cross semantics, builder UI conditionally shows a second-component combo box.
- [x] **Export / Import to `.atstrat` files** — `{AppData}/exports/{SafeName}.atstrat`. Import-latest reads the most-recently-modified file.

### Session E — Lifecycle integration (2026-04-07) — DONE

- [x] **Per-restart strategy persistence** via `StrategyAutoLoader` + `StrategySpec.IsAutoActivate` flag. The builder UI's "Add to Engine" sets the flag and persists; on next launch `MainLayout` calls `_autoLoader.LoadAll()` which walks the library, filters by `IsAutoActivate=true`, and re-instantiates each via the factory. Idempotent. Saved-but-not-activated specs remain in the library as templates. (Architectural simplification: per-tab strategy IDs in `TabConfiguration` were considered but rejected for marginal benefit; the field stays as a forward-compat hook.)
- [x] **Distinct entry-armed earcon** — already shipped in Session B as `IEarconService.PlaySetupArmed` (long: 660+990 sine; short: 330+220 triangle), distinct from `PlaySetupBell` (full setup) and `PlaySetupEntryReached` (in-trade). All three subscribed by `SetupSonifier`.
- [x] **AI Analyst "Review my setups today"** — `IAIAnalystService.AskAsync(prompt)` method, new modal button, builds structured prompt from today's journal entries + matching library specs, calls LLM with a setup-review system prompt, displays + speaks the response, mirrors back into the journal as an Info entry for later review.

### Phase 11 Audit Fixes (2026-04-07) — DONE

User reported 0 trades after adding Cipher B + building a strategy. Audit revealed 7 issues; all fixed in one focused session.

- [x] **Backtester honors TP/SL exits** — `StrategySignal` extended with `TpLadder` + `TpClosePortions`. `StrategyBacktester.Run` rewritten with per-bar exit check (stop priority + TP rung loop + breakeven move after TP1). Every backtest before this returned 0% profit because exits were never simulated. Single most important Phase 11 correctness fix.
- [x] **Cipher B catalog/chart mismatch** — `ConditionEvaluator` series lookup is now case-insensitive. `BuildSetupTab` leaf editor warns when the selected indicator isn't loaded on the active chart (yellow alert + `(not on chart)` annotations in the dropdown).
- [x] **Legacy SMA/RSI/Bollinger templates deleted** — three files removed; `BuiltInStrategyRegistry` reduced to empty stub.
- [x] **Library tab** — replaces Add Strategy. Lists `IStrategyLibrary.All` with Start/Stop/Delete actions. Active status column. New methods: StartSpec / StopSpec / DeleteSpec / RemoveExistingInstancesOfSpec helper.
- [x] **Backtest tab uses library specs** — `_btSelectedSpecId` dropdown + `Factory.Create` instead of legacy template selection. AutoWarmup uses the actual selected spec.
- [x] **Active tab Remove clears `IsAutoActivate`** — closes the bug where removed strategies came back on next launch.
- [x] **Warmup label** changed from misleading "Warmup / Evaluated: 579 / 2200" to explicit "Bars used: 2779 total (579 warmup + 2200 evaluated)".
- [x] **Duplicate-add guard** in `BuildSetupTab.AddToEngine` — removes any existing instance with same spec id before adding the new one.

**Still pending (polish, not blocking):**
- [~] **Live mode TP ladder execution** — broker-side bracket order plumbing per provider remains deferred (multi-day per broker: Binance OCO, Coinbase brackets, Schwab OCO, Alpaca brackets, Kraken conditional-close, plus emulation for brokers without native support). Tier B.5 (2026-04-23) shipped a safety warning: `SetupSonifier.OnArmed` now appends "Ladder has N rungs — only the first target fires live until multi-rung bracket support ships" when `TpPrices.Count > 1`. Closes the silent-failure path; multi-rung implementation stays on this list.
- [x] **Active tab metrics for Suggestion-mode strategies** (shipped
  2026-04-24) — `BaseStrategy` now wraps `OnBar` with theoretical-fill
  tracking: each signal with a Stop AND TakeProfit is recorded as a
  theoretical entry at bar close; subsequent bars walk Stop/TP against
  High/Low with stop-priority on same-bar ties (matching
  `StrategyBacktester`). `GetMetrics()` blends real-fill (Auto) +
  theoretical-fill (Suggestion) counters. Subclass contract changed:
  `ComputeSignal` is the new abstract hook (renamed from `OnBar`) —
  only one subclass exists (`ConfigurableStrategy`) and was updated.
  `SuggestionMetricsTests.cs` pins the contract (5 tests).
- [x] **TreeView expand/collapse + arrow-key navigation** — shipped
  2026-04-24. New `wwwroot/js/treeKeyboard.js` auto-wires ArrowUp/Down,
  ArrowRight/Left, Home/End, Enter/Space to every `role="tree"` element.
  Handles both the aria-expanded pattern (ConditionTreeEditor) and the
  `<details><summary>` pattern (ObjectTreeModal). All tree levels emit
  meaningful aria-labels that screen readers announce as a single phrase.
- [ ] **Custom Script tab Roslyn strategy persistence** — Roslyn-compiled strategies still aren't saved as `StrategySpec`s.

---

## PHASE 12 — Cross-Series Indicators & Non-Price Edge (2026-04-08, in progress)

The strategy thesis (see `memory/project_strategy_thesis_2026_04_08.md`) established empirically that 8 versions of pure-Cipher confluence (v2-v8) all walk-forward to break-even because price-derived indicators are auto-correlated. Real edge requires non-price data: funding, open interest, sentiment, on-chain. This phase builds the indicator-side plumbing to bridge those data sources into the strategy system.

### Session 1 — Cross-series foundation + 3 indicators (2026-04-08) — DONE

- [x] **OkxDerivatives plugin** — companion to BinanceDerivatives. Reason: Binance Futures REST is geo-blocked from US/UK/parts of EU (verified empirically). Bybit also CloudFront-blocked. OKX public REST remains reachable. Same `_FUNDING`/`_OI` suffix scheme so future indicators don't care which provider produced the data. Endpoints: `/api/v5/public/funding-rate-history` + `/api/v5/rubik/stat/contracts/open-interest-volume`. Wired in `BlazorClient.csproj` ProjectReferences and `MarketOrchestrator.cs:254`.
- [x] **Cross-series indicator architecture** — first one in the codebase. Pattern: per-provider static cache + background fetch via `Task.Run` fire-and-forget + `SemaphoreSlim` debounce + forward-fill in synchronous Calculate. `GetComponentSpeech` override returns "no data for this bar" on NaN to avoid the literal-template speech bug. Documented in detail in `FundingRateProvider` class comment.
- [x] **`FundingRateProvider`** (`FUNDING_RATE`, sub-pane `Pane_FUNDING`) — line + Extreme Long (≥0.05%/8h) + Extreme Short (≤−0.05%/8h) + Sign Flip dots. Reference levels at ±0.05/±0.01/0. Pagination walk-back: up to 10 pages (~333 days, well past OKX's actual depth) with no-progress guard, partial-page early-stop, dedupe-by-timestamp.
- [x] **`OpenInterestProvider`** (`OPEN_INTEREST`, sub-pane `Pane_OPEN_INTEREST`) — OI Value line + OI Delta histogram (polarity colored) + OI Spike dot (>2σ rolling-30-bar stdev) + OI Divergence dot (5-bar price/OI direction disagree, both moves material). The Divergence component is the most actionable signal — captures rallies-without-positioning (likely fades) and selloffs-without-positioning (capitulation bottoms). Single-page fetch (OKX rubik OI is hard-capped at ~180 bars on 1D, less on finer periods).
- [x] **`FearGreedProvider`** (`FEAR_GREED`, sub-pane `Pane_FEAR_GREED`) — Sentiment line + Extreme Fear (≤20) + Extreme Greed (≥80) + Sentiment Flip dots. Reference levels at 20/40/50/60/80. Single-call fetch (alternative.me serves full history back to 2018 in one response). `GetComponentSpeech` returns categorical labels alongside the raw number.
- [x] **DI registration** in `ServiceCollectionExtensions.cs:152-154`.
- [x] **Memory** — `memory/project_cross_series_indicators_2026_04_08.md` documents the architecture, the gotchas, and the next steps.

### Known limitations / gotchas

- **AddIndicatorModal string-parameter limitation:** `AddIndicatorModal.razor:55-57` hardcodes `<input type="number">` and force-converts every parameter via `IConvertible.ToDouble`. Selecting an indicator with `typeof(string)` parameters throws `InvalidCastException` and breaks the modal catastrophically (only one indicator visible, category dropdown frozen, close button unresponsive). Workaround: all three cross-series indicators expose only numeric parameters, source/symbol hardcoded as constants in Calculate. Multi-asset support (BTC/ETH/SOL) currently means separate indicator codes or fixing the modal first. **See Session 2 below.**
- **OKX history depth:** funding ~3 months, OI ~6 months on 1D (less on finer periods). Deep history requires Coinglass / paid sources.
- **Empty marker navigation:** Ctrl+L/R on a marker component (Extreme Long, Extreme Greed, etc.) only stops at bars where the marker fired. If no markers fired in the visible window, navigation says nothing — that's correct sparse-marker behavior, not a bug.

### Session 2 — Refactor + CrowdingIndex + modal string params + v9 (2026-04-08) — DONE

- [x] **Shared `ICrossSeriesCache` service** — `Core/Services/Indicators/CrossSeriesCache.cs`. `CrossSeriesRequest` record + `ICrossSeriesCache.GetOrFetch` + walk-back pagination + `CrossSeriesForwardFill.Fill` helper. Single singleton in DI. Replaced the per-provider static caches in FundingRate / OpenInterest / FearGreed — each provider lost ~150 lines of fetch boilerplate. Done as the first task per "do it right the first time, not a static fix."
- [x] **`CrowdingIndexProvider`** — first composite cross-series indicator. `crowding = funding_zscore + sign(price_delta) × oi_delta_zscore` over a 30-bar rolling window. The price_dir multiplier flips the OI z-score sign so positive composite always means "longs crowded" and negative always means "shorts crowded" regardless of price direction. Components: Crowding Score line + Long Crowded dot (≥+2σ) + Short Crowded dot (≤−2σ). **First codebase signal that pure-price indicators cannot replicate at any lookback** — combines two exchange-internal datasets that aren't computable from OHLCV.
- [x] **AddIndicatorModal string parameter support** — full plumbing fix. `ISeriesManagementService.RegisterSeriesFromMetadata` signature changed from `Dictionary<string, double>?` to `Dictionary<string, object>?`, with a `FormatParam(object?)` helper handling double / float / int / long / bool / string / IConvertible / null cleanly. AddIndicatorModal.razor now branches the input render on `param.DataType` (text input for string, number input for numerics), `_editParams` is `Dictionary<string, object>`, `InitialEditValue` and `GetNumericDisplay` helpers handle the type-aware path safely. The `InvalidCastException` from `string.ToDouble()` that broke the modal in Session 1 is fixed at the root. Existing callers (`WorkspaceInitializer.cs`) pass null so the change is transparent.
- [x] **v9 strategy spec** — `BuildV9CrossSeriesConfluence` in BuiltInStrategySeeds. ID `builtin.long.v9-cross-series-confluence`. Score budget designed so Cipher leaves max out at 5.0 and the gate is 5.5 — pure-Cipher mathematically cannot fire. Cross-series leaves: Funding Rate < -0.005 (1.5), FNG Sentiment < 25 (1.5), OI Divergence (1.5), Crowding Short Crowded (2.0). Cipher leaves: blue dot (1.0), Cipher A buy (1.0), Cipher C Bottom Triple (1.5), Anchor Wave < -53 (1.5). Same risk plan as v7/v8 (ATR×2 stop, 1.5R/3R ladder, BE after TP1, 0.5% risk) for clean A/B comparison. **Moment of truth for the strategy thesis** — does adding orthogonal non-price data restore edge that v2-v8 couldn't find from price alone?

### Session 3 — v9 backtest + thesis verdict (planned, next session)

- [ ] **Test the refactor + CrowdingIndex end-to-end** — confirm shared cache means no duplicate fetches when multiple cross-series indicators load on the same chart, confirm modal string param branch renders text inputs (smoke test once a string-param indicator is needed), confirm CrowdingIndex line shows up with expected magnitude
- [ ] **Run v9 backtest** on BTC/USDT 1h Bitstamp, recent 30-90 day range (OKX history depth)
- [ ] **Verdict on the strategy thesis** — does v9 produce materially different walk-forward metrics than v7/v8?

### Session 4 — Glassnode + multi-asset (deferred)

- [ ] **`GlassnodeProvider` plugin** — when API key is purchased. Deep history (back to 2019) for funding/OI/sentiment. Same source-name swap pattern: change `Provider = "OkxDerivatives"` to `Provider = "Glassnode"` in each indicator's `CrossSeriesRequest` constant.
- [ ] **Multi-asset support** — once Glassnode is in or v9 thesis validated, expose `Source` and `Symbol` as string parameters on each cross-series indicator (modal already supports this). One indicator code that can target BTC/ETH/SOL via parameter.

### Session 5 — Glassnode plugin (deferred — paid)

- [ ] **`GlassnodeProvider`** — when API key is purchased. Same pattern as OkxDerivatives. Unlocks deep history for funding/OI/on-chain that the free providers cap out on.

---

## Phase 11 — DONE (2026-04-07)

End-to-end complete. The composite signal-composer pipeline ships in 7 sessions:
- **Session A** — Foundation (signal catalog, condition tree, risk plan, ConfigurableStrategy state machine, journal modal, backtester warmup)
- **Session B** — MTF data + R-multiple metrics + entry-armed state machine
- **Session C** — Level providers + S/R-aware stops/targets + level operators
- **Session C Hardening** — VPVR provider + remaining Phase-4 sources + future-leak fix on Ichimoku/CipherSR
- **Path A Correctness Pass** — Main-path future-leak fix + real WorkspaceState in backtester + IBacktestProfileCache + per-bar VPVR replay + HTF indicator pre-warm
- **Session D** — Builder UI (BuildSetupTab) + modal input trap fix
- **Phase 11 Complete pass** — D2 polish (cross-line operators wired, HTF bar pre-warm, read aloud, preview, export/import, 2nd descriptor picker, auto warmup button) + Session E (StrategyAutoLoader, AI Analyst review-my-setups)

---

## PHASE 0 — Zero-Risk Cleanup

- [x] **StatusBar double speech:** Removed `<StatusBar />` from `MainLayout.razor` (done in 2026-03-25 sprint).
- [x] **Documentation overhaul:** README, CHANGES, CODEBASE_KNOWLEDGE_BASE, PLATFORMS, TODO updated (2026-03-26).
- [x] **EventBus vs Rx documented:** Canonical routing decision table written in `CODEBASE_KNOWLEDGE_BASE.md` Section 5.
- [x] **HelpModal + User Guide combined:** `HelpModal.razor` enriched with conceptual User Guide content alongside keyboard reference.
- [x] **Stub annotations:** `BlazorAudioDriver.cs`, `AppDelegate.cs`, `CoinbaseProvider.cs` annotated with `// STUB: ... Phase 5 roadmap.` (2026-03-26).
- [x] **NAudio.Wasapi audit:** Confirmed `BlazorAudioDriver` is the only consumer, Windows-only, `BlazorClient.csproj` `Condition` guard in place. No changes required. Phase 5 removal tracked above.

---

## PHASE 1 — Accessibility Path Bug Fixes

### Bug: Dual Navigation Sonification Path (Double-fire / Click artifacts)
- [x] Identified root cause: `SonificationManager.SyncNavigationSlots` (Path 1, 0.4s) AND `NavigationFeedbackManager.SonifyCurrentContext` → `AudioFeedbackRouter.SonifyComponent` (Path 2, 0.2s) both write to voice slot 0.
- [x] Fix: Removed `SonifyCurrentContext()` call and `_audioRouter.Silence()` call from `NavigationFeedbackManager.HandleNavigationFeedback`. NavigationFeedbackManager now handles SPEECH ONLY.
- [x] Fix: `SonificationManager` is the single authoritative audio path for navigation.

### Note: Audio Glide / ADSR / Default Volume
- [x] `AudioEngine` already has `ENVELOPE_SAMPLES = 220` (~5ms at 44100 Hz) providing attack/release. `continuous: false, 0.4s` in `SyncNavigationSlots` is intentional and correct — keydown repeat ensures seamless audio while held; the note fades naturally when released. No change required.
- [x] `WorkspaceState.Initial.ChartVolume` was already `0.5f`. No change required.

### Bug: No Loading-State Speech Feedback
- [x] `AccessibilityFeedbackCoordinator.OnStateChanged` already announces "Loading history..." on `DataStatus.LoadingHistorical` entry (was present). Verified this covers left-arrow during backfill.
- [x] Added: announce "Ready" on `InitializationStatus` transition from `Loading` → `Ready`.

### Bug: IsInputActive / IsChartFocused Race (Keys silently eaten)
- [x] Added `_isChartActive` gate to `CommandDispatcher`. Subscribes to `ChartFocusEvent` (→ true) and `DeactivateEvent` (→ false, 50ms debounce). Navigation, playback, and drawing commands gated. Global commands (F1–F8, volume, modal opens) bypass. Starts `true` so startup navigation works immediately. `CommandDispatcher` is now `IDisposable`.

---

## PHASE 2 — Data Pipeline Bug Fixes

### Bug: All Indicators Show "No Data" After Add
- [x] Root cause confirmed: `MarketOrchestrator.LoadChartAsync` dispatches `InitializationStatus.Ready` after `RefreshDataAsync()`. Verified this is in place.
- [x] `DataOrchestrationService` subscribes to `IndicatorUpdatedEvent` → `OnDataUpdated(forceFull: true)` for immediate recalculation when a series is added.
- [x] Profile recalculation on viewport change (zoom/pan): StateStream subscription now checks for active profile series and passes `forceFull: true` when any are present. Profiles (VPVR/TPO) re-slice visible bars on every pan/zoom. (2026-03-27 session 2)
- [x] Heatmap order book pipeline fixed: `GetOrderBookAsync` now called before the `needsFull` branch so snapshots accumulate on every tick. `needsFull` excludes profile/heatmap from the "empty data" trigger, breaking the infinite-full-recalc loop that starved the history service. (2026-03-27 session 2)
- [x] Added "No data" fallback in `NavigationFeedbackManager`/`BinnedNavigationStrategy.NavigateY` when focused series bins are empty (was already present per CHANGES.md Phase 2).

### Bug: Historical Data Not Loading on Scroll-Left
- [x] Resolved dual-trigger race: `PrependOlderDataAsync` is owned exclusively by `HistoryBufferCoordinator` via `RequestHistoryEvent`. `DataOrchestrationService.StateStream` subscription does NOT trigger backfill.
- [x] Bitstamp `FetchOhlcvAsync` missing `&end=` parameter — added (2026-03-25 sprint).
- [x] FRED `FetchOhlcvAsync` missing `observation_end` parameter — added (2026-03-25 sprint).
- [x] During `DataStatus.LoadingHistorical`, "Loading history..." announced (AccessibilityFeedbackCoordinator).
- [x] Right-arrow and series-switch allowed to produce feedback normally during historical backfill.

### Bug: Space Plays Only Focused Series (PlaybackScope Not Differentiated)
- [x] `CommandDispatcher.HandlePlayback` correctly dispatches `SetPlaybackAction(true, PlaybackScope.Chart/Series/Component)` per key binding.
- [x] Added `componentFilter` parameter to `IAudioSequencer.StartPlaybackAsync`. `-1` = all components (Series), `n` = specific component (Component). `PlaybackOrchestrator` passes the correct filter.
- [x] Chart scope anchors to `CoreSeriesIds.Candles` starting from `ViewportStartIndex`. Full multi-series layered audio is Phase 5 roadmap.

### Bug: No Audio Feedback at Data Boundaries
- [x] Added `FeedbackType.Boundary` to `FeedbackType` enum.
- [x] `NavigationEngine.NavigateX`: publishes `FeedbackType.Boundary` earcon when `strategy.NavigateX` returns `Success = false` (cursor already at edge).
- [x] `AccessibilityFeedbackCoordinator` handles `Boundary` with earcon-only (no speech per user preference).
- [x] `AudioFeedbackRouter.PlayEarcon` maps `Boundary` → `IEarconService.PlayBoundary()`.

---

## PHASE 3 — Structural Cleanup

### EventBus Rationalization
- [x] All EventBus subscriptions audited. Categorized as modal lifecycle (keep) or data-flow (use AsObservable or direct Rx). Decision documented in CODEBASE_KNOWLEDGE_BASE.md Section 5.
- [x] `NavKeyReleasedEvent` already consumed via `_eventBus.AsObservable<NavKeyReleasedEvent>()` — correct pattern confirmed.
- [x] `IndicatorUpdatedEvent` already consumed via `_eventBus.AsObservable<IndicatorUpdatedEvent>()` — correct pattern confirmed.
- [x] No EventBus subscriptions found that should be migrated to direct Rx streams — existing usage is already appropriate.

### HelpModal + User Guide Consolidation
- [x] `HelpModal.razor` enriched with "Understanding the Soundscape" conceptual section from USER_GUIDE.md.
- [x] Volume Profiles guidance added to HelpModal.
- [x] Drawing tools workflow (sequential anchoring) added to HelpModal.
- [x] Indicator customization guidance added to HelpModal.
- [x] SHORTCUTS.md remains as a standalone reference document (not removed).
- [x] USER_GUIDE.md remains as a standalone reference document (not removed).
- [x] Help button in toolbar retained — opens HelpModal via `OpenHelpEvent` on EventBus.

### NAudio.Wasapi Audit
- [x] Confirmed `BlazorAudioDriver` is the only consumer. Windows-only, `BlazorClient.csproj` `Condition` guard in place. Phase 5 removal tracked in roadmap section.

---

## PHASE 4 — SRP Refactoring

### CommandDispatcher — Chart-Focus Gate + Structural Clarity
- [x] Added `_isChartActive` flag with EventBus subscriptions (done in Phase 1 above).
- [x] Added numbered section comments (1–6), `IsDrawingCommand()` helper, `IDisposable` implementation.
- [x] **`IndicatorCrossingEngine` extracted:** All crossing/scan logic moved from `CommandDispatcher` to `IndicatorCrossingEngine`. `CommandDispatcher` injects it and delegates `HandleCrossJump`. Methods `ScanSignCrossing`/`ScanThresholdCrossing` are now `internal static` on the engine (tested via reflection).
- [ ] Full Command Pattern (extract `ICommandHandler<T>` per domain) — deferred to Phase 5+ if dispatcher grows beyond current size.

### DrawingService — Strategy Pattern Extraction
- [x] **`IDrawingCalculator` interface** created in `Sdk/Interfaces/IDrawingCalculator.cs`.
- [x] **15 calculator classes** created in `Core/Services/Drawing/Calculators/`: `HorizontalLineCalculator`, `VerticalLineCalculator`, `TrendLineCalculator`, `ChannelCalculator`, `FibRetracementCalculator`, `TextLabelCalculator`, `FibExtensionCalculator`, `GannFanCalculator`, `RectangleCalculator`, `RiskRewardCalculator`, `AnchoredVwapCalculator`, `MeasureToolCalculator`, `GannBoxCalculator`, `AndrewsPitchforkCalculator`, `AngleFibCalculator`.
- [x] **`DrawingService` rewritten** as a registry/dispatcher — resolves `IEnumerable<IDrawingCalculator>` from DI and routes by `DrawingType`. New tools can be dropped into `Drawing/Calculators/` without touching `DrawingService`.
- [x] **`DrawingCalculatorHelper`** — shared `FindIndex` / `CalculateLinearPoints` utility used by calculators that need index lookup or linear math.
- [x] All 15 calculators registered in `ServiceCollectionExtensions.AddRenderingServices`.

### SkenderIndicatorProvider — GetDetailFact Extraction
- [x] **`IDetailFactProvider` interface** created in `Sdk/Interfaces/IDetailFactProvider.cs`.
- [x] **`SkenderDetailFactProvider`** created in `Core/Services/Indicators/` — all 10 indicator-fact cases (RSI, BB, MACD, MA, Stochastic, VWAP, ATR, CCI, ADX, generic) extracted verbatim.
- [x] **`SkenderIndicatorProvider`** delegates `GetDetailFact` to `SkenderDetailFactProvider` — the fact logic is now independently testable and library-agnostic.
- [ ] Split `SkenderIndicatorProvider` into `SkenderIndicatorDiscovery` + `SkenderResultMapper` — deferred to Phase 5+ when a second Skender-based provider is added.

### WorkspaceStore — Domain Section Comments
- [x] Added domain-section comment headers to `Reduce()` switch expression (IDENTITY/MODE, DATA, NAVIGATION, PLAYBACK, etc.).
- [x] Added XML doc comment to `Reduce()` explaining delegation pattern.
- [x] Full slice reducer decomposition — shipped 2026-04-22 as 5 per-domain reducers.

---

## [2026-04-05] — Cipher S Algorithm Revamp + Viewport Right Margin

### Cipher S — Algorithm v5 (2026-04-05)
- [x] **High-low channel normalization:** Replaced percentile rank count with `(close - wLow) / (wHigh - wLow) × 100`. Anchors sentiment to the current cycle's own extremes, not multi-year rank table. Eliminates missing cold-color phases (blue/teal/cyan) on secularly trending assets like BTC.
- [x] **5th/95th percentile clipping:** Sort window closes; use 5th/95th percentile index as wLow/wHigh. Prevents flash crashes and thin-volume ATH spikes from compressing all other bars into a narrow mid-band.
- [x] **3-bar EMA smoothing (α = 0.5):** Applied to rawPct before phase mapping. Eliminates single-candle phase flicker without distorting the trend.
- [x] **Incremental tick optimization:** `RequiresFullRecalcOnTick = false`. `UpdateLast()` implemented — recalculates only the last bar; reads `pctSpan[i-1]` from buffer as EMA seed. Per-tick cost reduced from O(n×window) to O(window). Scroll-back correctness preserved — DataOrchestrationService already triggers full recalc on historical prepend.

### Viewport Right Margin (2026-04-05)
- [x] **`RightMarginBars = 20` in `WorkspaceState`:** Added to record, `TabSnapshot`, `Initial`. Default 20 bars of empty future-space reserved on the right of the viewport for trendline projection.
- [x] **`ViewportNavigationService` fully rewritten:** All four methods (`Navigate`, `Pan`, `Zoom`, `ClampViewportToData`) use `effectiveWindow = ViewportLength - RightMarginBars`. `ClampViewportToData` no longer mutates `ViewportLength`. `Zoom` anchors to `lastDataBar` so the margin slot count stays constant.
- [x] **`WorkspaceStore` updated:** `UpdateData`, `JumpToLatestAction`, `ZoomAction`, `SnapshotFromState`, `RestoreSnapshot`, `AddTab` all use/carry `RightMarginBars`.
- [x] **Left-side compression fixed (xOffset removed from `ChartRenderer`):** Removed `float xOffset = rect.Width - (visibleData.Count * itemWidth)` and the corresponding `xOffsetForAxis`. Bar positions now start uniformly from `rect.Left`. Empty space falls naturally to the right of the last bar through the right margin architecture.

**Build: 0 errors, 0 warnings. Tests: 236/236 passing.**

---

## MEDIUM PRIORITY — Future Work

### Drawing Tool Refinement
- [x] **Live Preview for trendline dragging + full mouse UX sweep**
  (shipped 2026-04-24). Click-drag placement creates a preview series on
  MouseDown that follows the cursor on every MouseMove and commits on
  MouseUp. Existing drawings can be repositioned by grabbing their anchor
  handles (10 px hit-test). Right-click opens a floating
  Delete/Duplicate/Properties menu. Scroll-wheel zoom centres on the
  cursor. JS `mousemove`/`wheel` listeners + three new `[JSInvokable]`
  entry points; `WheelZoomAction` + `ViewportReducer.WheelZoom`. 4 new
  pinning tests — 562/562 green.
- [x] Add "Coordinate Entry" mode for accessibility-first drawing creation (keyboard-only placement without cursor). *(Phase I, 2026-03-31)*

### Technical Analysis Polish
- [x] Implement Bollinger Band 'Squeeze' and 'Expansion' logic in `IndicatorContextAnalyzer.GetDetailFact`.
  Shipped 2026-04-24 in `BarDetailService.BollingerSqueezeExpansionFact`
  (20-bar avg width with ±10% thresholds).
- [x] Add MACD crossover facts (Bullish/Bearish crosses) to `BarDetailService`.
  Shipped 2026-04-24 in `BarDetailService.MacdCrossoverFact`.
- [x] Implement Volume-Profile POC-crossing alerts in `AlertEvaluator`.
  Shipped 2026-04-24: `AlertTarget.Poc` + `ILevelService` POC resolution.

### Ctrl+Left/Right Crossing Navigation Redesign
- [x] Generalized to use focused series type (Phase J, 2026-03-31): price/candles → trendline, zero-line oscillators → zero cross, threshold oscillators → OB/OS entry/exit, MA overlays → price/MA cross, %B → band crossing, sparse markers → nearest non-NaN signal.
- [x] Crossing logic extracted to `IndicatorCrossingEngine` (Phase 4-SRP, 2026-04-01) — independently testable, no longer coupled to `CommandDispatcher`.
- [x] Multiple trendlines: use the focused drawing, not "all trendlines."
  Shipped 2026-04-24 in `IndicatorCrossingEngine.DoFocusedTrendlineCrossJump`.

---

## [2026-03-28] — Session Fixes

### Heatmap Arrow Navigation "No Data" (fixed)
- [x] `BinnedNavigationStrategy.NavigateY`: bin count now uses `LastOrDefault(l => l?.Count > 0)?.Count ?? 0` — no longer depends on `CurrentDataIndex` backwards search that fails when cursor is in historical area.
- [x] `NavigationFeedbackManager.FindNearestHeatmapIndex`: falls back to forward search (last live snapshot) if backwards search from `CurrentDataIndex` finds nothing.
- [x] `IndicatorOrchestrator.RecalculateLastAsync`: heatmap `HeatmapData[^1]` is now only overwritten when `lastBarBins.Count > 0`. Previously an empty bids/asks response reset the live snapshot to empty on every tick where order book data was momentarily unavailable, causing subsequent navigation to see all-empty HeatmapData and report "No data".

### Wick Solo Playback & Ping Duration (fixed)
- [x] `AudioSequencer.StartPlaybackAsync` and `StartMultiSeriesPlaybackAsync`: Ping envelopes now receive `durationSeconds = min(0.15, msPerBar × 0.8 / 1000)` instead of `0.0`. This makes wick pings audible and lets them ring out.
- [x] `SonificationProfileProvider`: wick profile reverted to `PitchMapping.None`.
- [x] `DefaultSonificationStrategy.CreateAudioPoint`: upper wick → 880 Hz, lower wick → 220 Hz (fixed tones, `FreqMultiplier` still applied for per-user tuning).

### Alt+C / Alt+L Toggle Speech (fixed)
- [x] `AccessibilityFeedbackCoordinator.OnStateChanged`: announces "Heikin-Ashi candles"/"Standard candles" and "Log scale"/"Linear scale" on state change.
- [x] F2/F3/Alt+C/Alt+L toggle checks all moved before the `IsPlaying` gate.

### Heikin-Ashi Navigation Speech (fixed)
- [x] `NavigationFeedbackManager.HandleNavigationFeedback`: when `state.IsHeikinAshi`, computes HA bar via `ChartMath.CalculateHeikinAshi` for the current index before passing to formatter. Spoken OHLC values now match the visual chart.

### Heikin-Ashi Navigation Sonification (fixed)
- [x] `NavigationSonifier.SyncNavigationSlots`: added `using AccessibleTrader.Core.Services`. When `state.IsHeikinAshi`, computes HA bar via `ChartMath.CalculateHeikinAshi` and uses it as the audio source `navPoint`. `PitchMapping.Direction` (bullish/bearish pitch) now reflects HA candle direction rather than raw bar direction, matching the visual chart and speech output.

### Candle Colors in Properties Dialog (fixed)
- [x] `StandardRenderers.RenderCandles`: body color uses `comp.ColorHex` (bullish) and `comp.ColorHexSecondary` (bearish) from the Candle Body component config.
- [x] `PropertiesModal.razor`: Candle display-type components show Bullish/Bearish color pickers; all others show single Color picker.
- [x] `SettingsModal.razor`: Removed read-only candle color swatches; replaced with note directing users to Properties dialog (Shift+F12).

### Heatmap AND Profile Bin Navigation Speech "No Data" (fixed)
- [x] **Root cause:** `NavigationFeedbackManager.HandleNavigationFeedback` evaluated `isProfile` before `isHeatmap` in the speech-formatting block. `IndicatorModelFactory` sets `IsProfile = true` for heatmap series (`meta.Code == "HEATMAP"`), so heatmaps entered the profile branch, which checked `s.Data.ProfileBins.Count` (always 0 for heatmaps) and spoke "No data". Profiles were separately affected: when profile bins were empty at navigation time the same "No data" path fired.
- [x] **Fix:** Swapped if/else-if order in `NavigationFeedbackManager` — `isHeatmap` is now checked first, matching the already-correct ordering in `BinnedNavigationStrategy`. Heatmaps now correctly enter `FormatHeatmapFeedback`; profiles use `FormatProfileFeedback` as intended.

---

## PHASE 5 — Indicator Pane Robustness & Multi-Instance Indicators (2026-03-28 Session 4)

### Multiple Indicator Instances
- [x] **Multiple instances of same indicator type blocked:** `SeriesManagementService.RegisterSeries` gave every non-core indicator the same `id.ToLowerInvariant()` ID. Second EMA/RSI/etc. hit duplicate guard and silently returned. Fixed: non-core indicators always receive `Guid.NewGuid()` ID; only the four core singletons keep deterministic IDs.

### Indicator Pane Height Robustness
- [x] **Fixed 70/30 height split becomes unreadable with 3+ indicators:** Added `MinIndicatorPaneHeightPx = 80f` floor (density-scaled) to `ChartRenderer`. Main pane clamped to minimum 25% of total height. Bottom panes clip gracefully at canvas edge.

### Crosshair Across All Panes
- [x] **Crosshair vertical line stopped at main pane bottom:** `RenderCrosshair` now draws vertical line across full chart height. Each indicator pane also receives its own horizontal crosshair at the cursor's indicator value (first non-NaN component value, slightly dimmed to distinguish from main pane crosshair).

### Reference Level Source of Truth
- [x] **`IndicatorReferenceLevels` static class:** Single source of truth for all OB/OS/zero/midpoint definitions. Both `SeriesManagementService.InjectDefaultLevels` and `StylingService.GetLevelComponents` delegate here.
- [x] **`ViewportRangeCalculator` expands pane ranges to include level values:** OB=70/OS=30 always on-screen for RSI; zero-line always on-screen for MACD — regardless of where data currently sits.
- [x] **Hidden levels excluded from range expansion:** `IsVisible = false` levels do not expand the pane range.

### Settings & Workspace Persistence
- [x] **Theme persistence wired:** `ThemeService` reads/writes via `ISettingsManager`.
- [x] **Alert persistence wired:** `WorkspaceLibraryService` `SaveAlerts`/`LoadAlerts` via `alerts.json`.
- [x] **Workspace layout persistence wired:** `SeriesManagementService.PersistWorkspace()` saves active series configs; `WorkspaceInitializer` restores on startup.

### Tests
- [x] **`ReferenceLevelTests.cs` (28 tests):** All indicator families, case-insensitivity, level injection via `RegisterSeries`.
- [x] **`BackfillManagerTests.cs` (5 tests):** Queue, persistence, error resilience, cancellation.
- [x] **`ViewportRangeCalculatorTests.cs` (8 tests):** Guard cases, pane range calculation, level expansion, hidden levels, shared pane with two same-type oscillators.

**Build after phase: 0 errors 0 warnings. Tests: 69/69 passing.**

---

## PHASE 5 (PLANNED) — Pane Layout UX & Crosshair Value Labels

### User-Resizable Pane Dividers *(Phase 5 roadmap)*
- [x] **Pane divider drag interaction:** `PaneHeightRatios` in `WorkspaceState` (`ImmutableDictionary<string, float>`). `ResizePaneAction` adjusts ratios, clamped [0.05, 0.60]. `IPaneLayoutService` published by `ChartRenderer` after each render; `ChartArea.razor` renders CSS drag-handle divs and dispatches on `@onmousemove`. `SetPaneHeightRatiosAction` restores from saved workspace.
- [x] **Minimum pane size enforcement during drag:** 80px floor (density-scaled) in `ChartRenderer`; main pane floored at 25% of total height.
- [x] **Persist pane ratios to workspace profile:** `WorkspaceConfiguration.PaneHeightRatios` serialised via `WorkspaceInitializer.SaveWorkspace()`; restored in `InitializeDefaultSeries()`.

### Scrollable Pane Area *(Phase 5 roadmap)*
- [x] **Vertical scroll offset for indicator panes:** `IndicatorPaneScrollIndex` in `WorkspaceState`. `ScrollIndicatorPanesAction`. Alt+Up/Down bound in `ShortcutManager`; handled in `CommandDispatcher`. Speech: "Scroll panes up/down".

### Crosshair Y-Value Labels in Indicator Panes *(Phase 5 roadmap)*
- [x] **Per-pane value label on crosshair:** `ChartRenderer.RenderCrosshair` draws numeric label at right edge of each indicator pane at the crosshair Y position (same font as Y-axis labels).

---

## PHASE 6 — Audio Fidelity, Shortcuts & Indicator Reference Lines

### Audio Playback Glide Fix
- [x] **Playback click artifacts:** `AudioSequencer.StartPlaybackAsync` changed to `continuous: true, duration: 0.0`. AudioEngine glide smooths frequency/volume between bars — no envelope restart click.
- [x] **Candle body sonification identical to bars:** Verified — candle body uses same `SonificationStrategy` path as bars. No separate waveform injected.

### Wick & Candle Playback Fixes (2026-03-27 session 2)
- [x] **Wick ping restart during playback:** `AudioSequencer` now uses `continuous = (envelopeType != "Ping")`. Wick "Ping" envelopes restart on each bar; sustain-enveloped lines glide as before.
- [x] **Candle body too quiet:** `SonificationProfileProvider` changed candle body from `AmplitudeMapping.Size` to `AmplitudeMapping.None`. Body always plays at full `baseVolume`; bullish/bearish pitch preserved via `PitchMapping.Direction`.
- [x] **Null-ref guard:** `series.Pane ?? ""` added in `AudioSequencer.StartPlaybackAsync` pane-range lookup.

### Data Pipeline Fixes (2026-03-27 session 2)
- [x] **Indicator flat-line on historical prepend:** `DataOrchestrationService.OnDataUpdated()` now detects prepend via `_lastFirstBarDate` and sets `forceFull: true`, triggering `RecalculateAllAsync` to re-index all indicator buffers against the new bar range.
- [x] **Profile recalculation on pan/zoom:** StateStream subscription now passes `forceFull: true` when any profile series is active, so VPVR/TPO always re-slice their visible-bar window.
- [x] **Heatmap order book history fix:** `GetOrderBookAsync` moved before the `needsFull` branch. Snapshots accumulate on every tick; `needsFull` no longer includes heatmap/profile in the "empty data" trigger (was causing an infinite full-recalc loop that starved the history service).

### Navigation Note Duration
- [x] **Navigation note duration:** Reduced from `0.4s` to `0.15s` in `NavigationSonifier.SyncNavigationSlots`. Home/End/PgUp/PgDn feel crisp; held-arrow gives rapid staccato.

### Drawing Shortcuts in HelpModal
- [x] **All 15 Ctrl+Shift drawing shortcuts documented:** Added Ctrl+Shift+A/B/E/G/J/M/P/R/W to `HelpModal.razor` shortcut table.
- [x] **Alt+B = Order Book button:** `ShortcutManager`, `CommandDispatcher`, `Toolbar.razor` all wired. `OrderBookModal.razor` created.

### Indicator Reference Lines
- [x] **Auto-inject reference levels on indicator add:** `SeriesManagementService.InjectDefaultLevels` called in `RegisterSeries`. RSI/MFI/STOCH: 30/50/70. MACD/ROC/CCI/etc.: zero-line. AROON: 50. PERCENTB: 0/0.5/1.

### Volume Bar Direction Colors
- [x] **Volume bars colored by price direction:** `StandardRenderers.RenderDirectionalBars` colors green/red per OHLCV bar. `DataLayer` routes `CoreSeriesIds.Volume` series to this method.

### General Bar Coloring Rule (All Indicators)
- [x] **All Bar/Histogram components use directional coloring:** `StandardRenderers.RenderDirectionalBars` generalized to use `comp.ColorSource`. `ColorSource.PriceAction` → candle direction (green/red). `ColorSource.Value` → value sign (positive/negative). No special-casing per indicator — the rule is universal. `DataLayer` removed the `CoreSeriesIds.Volume` special-case; all `Bar`/`Histogram` display types now route to `RenderDirectionalBars`.

### Simultaneous Multi-Series Playback (Space = Chart Scope)
- [x] **Space plays all series simultaneously:** `IAudioSequencer.StartMultiSeriesPlaybackAsync` added. Bar-by-bar loop iterates all visible non-drawing non-profile series. Each series gets up to `SlotsPerSeries = 8` voice slots (`PlaybackSlotOffset + (sIdx × 8) + cIdx`). `PlaybackOrchestrator` Chart scope now calls `StartMultiSeriesPlaybackAsync` (was previously sequential pane playback).
- [x] **Wick / per-component audio in playback:** `ISonificationStrategy.MapComponentToAudio` added. Each component maps its own pitch/amplitude independently. `AudioSequencer` calls `MapComponentToAudio(series, cIdx, ...)` per slot rather than the single `MapToAudio` which always returned the first component's audio.

### Profile Sonification on Arrow Key Navigation
- [x] **Profile sonification fires on bin Up/Down:** `SonificationManager` now includes `binChanged = state.FocusedBinIndex != _currentState.FocusedBinIndex` in the `SyncNavigationSlots` trigger condition.

### Live Bar Intra-Bar Component Array Sync
- [x] **Component arrays updated on intra-bar ticks:** `WorkspaceStore.UpdateData` now has an `else if (!initial && list.Count > 0)` branch that clones the component array and updates only `arr[^1]` for DataMapping fields (Open/High/Low/Close/Volume) when a live tick replaces the last bar without changing bar count.

### Modal Visibility / Chart Canvas Hide-Show
- [x] **Modals now visually appear:** `MainPage.xaml` reverted to original order (BlazorWebView bottom, SKCanvasView top — Skia renders correctly on top). Added `ModalStateChangedEvent(bool IsOpen)` to `Events.cs`. All 11 modals publish this event in `ShowAsync()` and `Close()`. `MainPage.xaml.cs` hides `_chartCanvas` on first modal open and restores it on last close (reference-counted to handle nested modals).

---

## PHASE 6 — Provider Plugin Completion

### Market Type Selection
- [x] **Spot/Futures dropdown:** `MarketOrchestrator` extended with `SelectedSubType`/`AvailableSubTypes`. Toolbar shows conditional sub-type dropdown when `AvailableSubTypes.Count > 1`. `LoadChartAsync` passes `marketKey = "{market}|{subType}"`.

### API Key Infrastructure
- [x] **API key required gating:** `MarketOrchestrator.RefreshSymbolsAsync` places `ApiKeyRequiredSentinel` in symbol list when provider requires key but none configured. Toolbar `Load` button disabled for sentinel value.
- [x] **Alt+K keys wired:** `ApiKeyService` + `TradingDashboardModal` use stored keys. `GeneralOrderService` passes provider name to all calls.

### Live Stream Audit
- [x] **Alpaca:** Switched from 15s REST polling to WebSocket v2 live bars. OrderUpdateStream wired from `trade_updates` WebSocket.
- [x] **Binance OrderUpdateStream:** verified 2026-04-23 — `_listenKey` + `KeepAliveUserStreamAsync` + `StopUserStreamAsync` wired in `BinanceProvider.cs`. See also the `[x]` entry in the Phase 7 block below.
- [x] **Bitstamp OrderUpdateStream:** verified 2026-04-23 — `SubscribePrivateChannelAsync` + `private-my_orders-{pair}` handling shipped; see the `[x]` entry in the Phase 7 block below.

### Binance Plugin
- [x] **Futures PlaceOrderAsync:** Routes to `UsdFuturesApi.Trading.PlaceOrderAsync` when `signal.SubType == "Futures"`. Applies leverage before order. Attaches TP stop as separate order.
- [x] **Binance User Data Stream:** fully shipped per 2026-04-23 re-read — listenKey create/keep-alive/close + `onOrderUpdateMessage` callback produces `OrderUpdate` records (status PartiallyFilled / Filled / Cancelled / Rejected; Stop/TP flags derived from order type) and pushes through `_orderUpdateSubject`.

### Bitstamp Plugin
- [x] HMAC-SHA256 trading fully implemented. WebSocket live trades + order book diff stream.
- [x] Wire `order` channel events → `OrderUpdateStream` — shipped per `SubscribePrivateChannelAsync` in `BitstampProvider.cs`.

### Alpaca Plugin
- [x] WebSocket v2 data stream (stocks + crypto). Trade update WebSocket wired to `OrderUpdateStream`.

### Coinbase Plugin
- [x] JWT auth implemented (ECDsa PEM key, `GenerateJwt`). WebSocket user channel wires order updates. Full trading API implemented (pending live test).

### Polygon Plugin
- [x] WebSocket live feed implemented (`delayed.polygon.io`). Stocks/crypto/forex routing by market prefix.

### FRED Plugin
- [x] REST OHLCV fetch implemented with frequency mapping. Irregular dates handled by FRED API's `frequency` param.

### Trading Dashboard
- [x] **Margin type selector:** Cross/Isolated dropdown shown when `_supportsMargin = true`. `SupportsMarginTradingAsync` added to service.
- [x] **Leverage field:** Shown with max leverage cap when margin supported.
- [x] **Take Profit field:** Added to order entry form.
- [x] **Accessible order book table:** `role="table"` + `<thead>/<tbody>/<th scope="col">` replaces `<div class="book-row">`.
- [x] **Full signal wiring:** `SubmitOrder` passes `SubType`, `MarginType`, `Leverage`, `TakeProfit` in `TradeSignal`.
- [x] **TradeSignal:** `SubType` + `MarginType` fields added to SDK record.

---

## PHASE 7 — Platform Parity & Feature Completion (Roadmap)

### Platform Parity
- [x] **Mac Keyboard Input:** shipped as `KeyboardPageHandler` + `KeyboardViewController` (line 1224 below).
- [x] **Android Audio Output:** shipped as `AudioTrack` PCM-Float push loop (line 1225 below).
- [x] **iOS/Mac Catalyst Audio Output:** shipped as `AVAudioEngine` + `AVAudioSourceNode` (line 1226 below).
- [x] **NAudio.Wasapi Removal** — shipped 2026-04-24. `BlazorAudioDriver`
  now plays Float32 on Windows via a winmm.dll P/Invoke (waveOut with a
  three-buffer round-robin); package reference dropped from
  `BlazorClient.csproj`. Android AudioTrack + iOS/macCatalyst AVAudioEngine
  paths unchanged. User will verify Windows audio in a later session.

### Remaining Provider Gaps
- [x] **Binance User Data Stream:** `StartUserDataStreamAsync` creates listenKey, subscribes via `_socketClient.SpotApi.Account.SubscribeToUserDataUpdatesAsync`, 25-min keepalive timer, cleanup in `DisconnectAsync`.
- [x] **Bitstamp OrderUpdateStream:** `SubscribePrivateChannelAsync` sends HMAC-SHA256 auth for `private-my_orders-{pair}`; `ReceiveLoop` handles `order_changed`/`order_deleted` → `_orderUpdateSubject`.

### Feature Completion
- [x] **Strategy Backtester UI:** `StrategyModal.razor` — Backtest section with capital/commission/slippage inputs, Run button, results grid (trades/win rate/P&L/drawdown/Sharpe), trade log details. `IStrategyBacktester` DI-registered in `ServiceCollectionExtensions`.
- [x] **Custom Speech Template Editor** (shipped 2026-04-24, scope
  corrected) — per-indicator speech templates are now editable in the
  **Indicator Properties modal** (`PropertiesModal.razor`), not the
  Settings modal. The original TODO placed this in `SettingsModal`
  which was the wrong scope: per-indicator templates belong on the
  indicator instance, not app-wide settings. The new **Speech** tab
  edits `ComponentConfig.SpeechTemplate` + `SignalSpeechTemplate`
  directly — fields were already present on the model and already
  consumed by `SpeechFormatter`; only the UI was missing. Reset-to-
  default button restores provider metadata defaults.
  `SpeechTemplateOverrideTests.cs` pins the contract (4 tests).
- [ ] **Multi-Symbol Watchlist:** Extend `WorkspaceState` to hold collection of `ChartState`.

### Platform Parity
- [x] **Mac Keyboard Input:** `KeyboardPageHandler` (custom `PageHandler`) with `KeyboardViewController` override of `PressesBegan`. Uses NSEvent Unicode private-use characters for special keys. Registered in `MauiProgram.cs` via `#if MACCATALYST`.
- [x] **Android Audio Output:** `AudioTrack` PCM-Float push loop on `TaskCreationOptions.LongRunning` thread in `BlazorAudioDriver` under `#if ANDROID`.
- [x] **iOS/Mac Catalyst Audio Output:** `AVAudioEngine` + `AVAudioSourceNode` render callback in `BlazorAudioDriver` under `#if IOS || MACCATALYST`. De-interleaved via `Marshal.Copy`.
- [~] **NAudio.Wasapi Removal:** tracked above in the top Platform Parity section.

### Chart Focus Shortcut
- [x] **Ctrl+Alt+Shift+C:** `SystemCommand.ChartFocus`, `ShortcutManager` binding, `CommandDispatcher` handler publishes `ChartFocusEvent` + `CONTEXT_SUMMARY` feedback. `HelpModal.razor` and `SHORTCUTS.md` updated.

### Performance (from previous Phase 6)
- [ ] **Span-Based Indicator Pipeline:** `ReadOnlySpan<Ohlcv>` + `ArrayPool<double>` in `SkenderIndicatorFactory`.
- [ ] **Full Channels Migration:** `Channel<Ohlcv>` from plugin → `DataManager` for live ticks.
- [x] **Voice Slot Pooling:** shipped 2026-04-24. `OscillatorVoice[]`
  was already pool-allocated at ctor; the real hot-path allocation was
  `wave.ToLower()` in `SetVoice`. Extracted `ParseWaveform` using
  `StringComparison.OrdinalIgnoreCase` — zero allocations on the
  300-calls/sec playback path.
- [x] **EventBus Batch Notifications:** shipped 2026-04-24.
  `SubscribeCoalesced<T>(handler, quietWindow)` (Rx `Throttle`) +
  `SubscribeSampled<T>(handler, window)` (Rx `Sample`) on `IEventBus`.

---

## PHASE 8 — Code Quality & Robustness (from 2026-03-28 Architectural Assessment)

### AudioEngine Thread Safety
- [x] **`StopAll()` and `Reset()` bypass the ring buffer:** Removed direct `_voices[i].*` writes from both methods. All voice mutations now route exclusively through `EnqueueCommand` → ring buffer → `Read()` on the audio callback thread. Master gain is reset directly (single aligned float — no torn read on x86/x64). Voice deactivation happens via the master-gain fade path inside `Read()`, which is the safe write path.

### Platform Stub Enforcement
- [x] **Mac keyboard input not wired — silent failure today:** `AppStartupService.WarnAboutUnimplementedPlatformFeatures` now emits a speech announcement and `LogWarning` under `#if MACCATALYST`.
- [x] **Android/iOS audio not wired:** Same method emits warning under `#if ANDROID` / `#if IOS`.

### Resilience Tests (MISSING COVERAGE)
- [x] **Resilience tests added (`ResilienceTests.cs` — 6 tests):**
  - `FetchOhlcv_WhenNonRetriableExceptionThrown_ShouldReturnEmptyAndFault`
  - `FetchOhlcv_WhenHttpExceptionThrown_ShouldReturnEmptyAndFaultAfterRetry`
  - `FetchOhlcv_WhenCircuitAlreadyOpen_ShouldReturnEmptyAndFaultQuickly`
  - `FetchOhlcv_WhenCancelled_ShouldReturnEmptyCleanly`
  - `FetchOhlcv_OnError_ShouldPublishFeedbackRequestEvent`
  - `FetchOhlcv_WhenSilentAndFails_ShouldNotPublishEventsOrChangeState`

### DI Feature Slices
- [x] **`ServiceCollectionExtensions` refactored into domain slices:** `AddAccessibleTraderServices` now delegates to eight private static helpers — `AddCoreInfrastructure`, `AddDataPipeline`, `AddIndicatorPipeline`, `AddRenderingServices`, `AddBusinessServices`, `AddInputRouting`, `AddAudioServices`, `AddAccessibilityServices`. No runtime change.

### Modal Contract Enforcement
- [x] **`ModalBase.cs` created** — `ModalBase : ComponentBase, IDisposable` provides `ShowModalAsync(headingElementId)` and `CloseModal()` which always publish `ModalStateChangedEvent`. `AlertsModal.razor` migrated as the reference implementation (`@inherits ModalBase`). Remaining 10 modals are functional as-is; migrate them when touching each one (tracked in Phase 7).

---

## PHASE 9 — Known Bug Fixes (from 2026-03-28 Architectural Assessment)

These are confirmed code bugs identified during the architectural review session. They cause silent incorrect behaviour rather than crashes. Fix in priority order.

### AlertEvaluator — Indicator Crossover Alerts Broken
- [x] **Root cause:** `AlertOrchestrator` always passed an empty `previousValues` dict. Fixed: `AlertOrchestrator` now maintains `_previousValues`, populated after each tick from all active indicator component values. Crossover detection now works correctly.

### IndicatorContextAnalyzer — Wrong Component Selection
- [x] **Root cause:** `Analyze()` picked the first visible component rather than the registered definition's `ComponentName`. Fixed: iterates `_defs` to resolve the component by name match first; first-visible is only a fallback.

### IndicatorContextAnalyzer — EvaluateTrendChange Incorrect
- [x] **Root cause:** `EvaluateTrendChange` returned `trend != Flat` on every bar. Fixed: `AlertEvaluator` tracks `_previousTrends` per alert+series key; fires only on actual direction flip.

### BarDetailService — Empty Span Passed to GetDetailFact
- [x] **Root cause:** `ReadOnlySpan<Ohlcv>.Empty` passed to `GetDetailFact`. Fixed: `AnnounceDetails` builds a lookback slice of up to 50 bars and passes it through the call chain.

### TODO.md — Duplicate "Platform Parity" Section
- [x] **Fixed:** Duplicate "Platform Parity" and "Performance" blocks removed from Phase 7. Single canonical copy retained.

### F8 — ToggleMuteSonification Removal
- [x] **Removed:** F8 was documented but never implemented in `SystemCommand` or `ShortcutManager`. References removed from `HelpModal.razor`, `CODEBASE_KNOWLEDGE_BASE.md`, and `keyboard.js` trapped-keys list. F8 now passes through to screen reader / OS.

### ScriptingService — Dead Code (No UI Entry Point)
- [x] **Annotated:** `ScriptingService.cs` class-level `<remarks>` comment added: `STUB: No UI entry point — Phase 10 scripting roadmap. Wire to ScriptEditorModal (Alt+,).`

---

## PHASE 10 — Comprehensive Enhancement Roadmap (from 2026-03-28 Session)

Items ordered by impact. Phases labeled 10-A through 10-G for implementation sequencing.

### First Wave — Already Implemented (2026-03-28)
- [x] **PropertiesModal persistence:** `Apply()` now calls `SeriesManager.PersistWorkspace()` so component appearance/audio changes survive restart.
- [x] **AlertOrchestrator warm-up guard:** `_initialized` flag prevents false-positive crossover alerts on first tick (cold start seeds `_previousValues` without firing).
- [x] **Custom Scripts infrastructure:** `OpenCustomScriptsEvent`, `SystemCommand.OpenCustomScripts`, `Alt+,` shortcut, `CustomScriptsModal.razor`, "Scripts" button in `IndicatorBar.razor`, `ICustomScriptService` interface.
- [x] **Data Export (CSV):** `IDataExportService` + `DataExportService` — viewport-scoped export including all visible indicator components. "Export CSV" button in Settings → General tab. JS `downloadCsv` helper in `keyboard.js`.
- [x] **Settings Profiles (Visual/Audio):** `VisualProfile` / `AudioProfile` classes (`SettingsProfiles.cs`). Export/Import buttons in Settings → General. `IWorkspaceLibraryService` extended with `ExportVisualProfile`, `ExportAudioProfile`, `ImportVisualProfile`, `ImportAudioProfile`.
- [x] **Keyboard tab in SettingsModal:** `ShortcutDisplayBinding` record, `IShortcutManager.GetAllBindings()`, shortcut table rendered in new "Keyboard" tab (General / Appearance / Keyboard / License / About).
- [x] **Zero-value live bar filter:** Binance, Bitstamp, Alpaca WebSocket callbacks now reject frames where all OHLCV values are zero and timestamp is epoch/zero. Dead-asset bars (genuinely zero) are unaffected (timestamp is still valid).
- [x] **BackfillManagerTests race fix:** Wait condition now requires both DB rows saved AND `BACKFILL_COMPLETE` event published before asserting — eliminates flakiness in parallel test runs.

---

### Phase 10-A — Foundation: Persistence, Display Types, Audio Texture ✅ Complete

#### A1: Mute/Volume Persistence ✅
- [x] **`ChartCommandManager`:** `PersistWorkspace()` called after `ToggleMuteAction` (component and series scope), `ToggleHideAction` (both scopes), and all `VolumeChangeEvent` dispatches (component/series/chart scopes).
- [x] **Result:** Mute state, hide state, and F5–F7 volume levels survive app restart.

#### A2: Per-Bar Coloring System ✅
- [x] **`ColorRule` record + `ColorCondition` enum (`Sdk/Models/ColorRule.cs`):** `AboveZero`, `BelowZero`, `Rising`, `Falling`, `AboveLevel`, `BelowLevel` + `string ColorHex` + `double Level`.
- [x] **`ComponentConfig.ColorRules: List<ColorRule>`** — empty by default; first matching rule overrides static `ColorHex` per bar.
- [x] **`StandardRenderers.ResolveBarColor()`** — evaluates rules against current and previous bar value; returns `null` when no rules (zero overhead on existing indicators).
- [x] **`StandardRenderers.RenderLine`** — per-bar colored segments when `ColorRules` non-empty.
- [x] **`StandardRenderers.RenderDirectionalBars`** — per-bar color from rules, falls back to directional logic when no rule matches.
- [x] **`PropertiesModal` Appearance tab:** "Color Rules" section — Add/Remove rules, condition dropdown, color picker per rule, optional Level field for threshold conditions. _(Completed 2026-04-01)_
- [x] **Persistence:** `ColorRules` serialized via `ComponentConfig` → `SeriesConfig` → `workspace.json`.

#### A3: New Display Types ✅
- [x] `ComponentDisplayType.Dot` — `RenderDot`: filled circle at value Y, radius = `Thickness * density`.
- [x] `ComponentDisplayType.Arrow` — `RenderArrow`: up/down triangle, direction from value sign.
- [x] `ComponentDisplayType.StepLine` — `RenderStepLine`: horizontal-then-vertical staircase.
- [x] `ComponentDisplayType.Cloud` — `RenderCloud`: filled polygon between `UpperComponentName` and `LowerComponentName` components; direction runs split into bullish/bearish fills.
- [x] `ComponentDisplayType.Gradient` — `RenderLine` (shared): alpha fill below line to pane zero.
- [x] `Area` display type fill fixed: was bare line; now alpha-60 fill + line on top.
- [x] `DataLayer` switch updated for all new types.

#### A4: IsAreaFill Verification ✅ (partial)
- [x] `Area` display type now renders correctly (fill added in `RenderLine`).
- [x] `Cloud` display type provides the band-fill use case for Bollinger/Keltner (assign `UpperComponentName`/`LowerComponentName` on the cloud component).
- [x] **Area fill sonification (band width → amplitude):** Closed 2026-04-24 as
  "won't do". Rationale: the line value already drives amplitude; a width-
  derived voice duplicates what `DeltaFromPrice` amplitude mapping on a
  derived series provides, and a third voice between two already-sonified
  boundaries breaks the audio=visual invariant. See `docs/CHANGES.md`
  2026-04-24 "Cloud sonification scoping" entry.

#### A5: AudioEngine Noise Oscillator ✅
- [x] `WaveformType.Noise` — pure pink noise via one-pole filter.
- [x] `ComponentConfig.NoiseAmount [0,1]` — blends noise into any waveform. Default 0 = zero overhead.
- [x] `OscillatorVoice.NoiseAmount` / `OscillatorVoice.NoiseState` — per-voice state; persists between samples for smooth texture.
- [x] `AudioEngine.SetVoice(... noiseAmount = 0f)` — optional param; all existing callers unaffected.
- [x] **PropertiesModal Audio tab NoiseAmount slider** — per-component range slider in Sonification tab. _(Completed 2026-04-01)_
- [x] **Bollinger Band noise preset** — closed 2026-04-24 as "won't do". The
  existing `LevelConfig.ZoneNoiseAmount` is the canonical "inside zone" audio
  cue. A band-presence noise layer would play ~95% of the time on Bollinger
  bands (price is almost always inside the band) and become inaudible to the
  user; the only information users need is band-exit, which existing boundary
  earcons + speech already announce.

---

### Phase 10-B — Sound Designer ✅

- [x] **`SoundPatch` model (`Sdk/Models`):** `Id`, `Name`, `Waveform`, `NoiseAmount`, `BaseFrequency`, `FreqMultiplier`, `Volume`, `EnvelopeType`, `DurationSeconds`, `Description`. Serializable. `Clone()` assigns fresh GUID.
- [x] **`ISoundPatchLibrary` (`Core`):** `GetPatches()`, `AddPatch`, `RemovePatch`, `UpdatePatch`, `GetPatch`, `ExportPatchJson`, `ImportPatchJson`, `EarconOverrides`, `SaveEarconOverrides`, `SavePatches`. Persists to `patches.json` + `earcon-settings.json`.
- [x] **`SoundDesignerModal.razor`:** `Alt+W` shortcut → `OpenSoundDesignerEvent`. Patch list (New/Clone/Delete), parameter editor (Waveform/Noise/Freq/Vol/Envelope), Preview, Save, Export JSON, earcon assignment table, Import JSON. ARIA-accessible throughout.
- [x] **Earcon custom waveforms:** `EarconService` injects `ISoundPatchLibrary`; `PlayWithPatchFallback()` checks earcon override before using hardcoded defaults. All eight earcons (Boundary, Info, Error, Success, Retry, NewBar, Connected, Disconnected) are assignable.
- [x] **`.atpkg` sharing format:** Zip containing `source.cs` + `manifest.json` (version, name, author, type). Export via `downloadBlob` JS (binary zip); import via `readFileAsBase64` file picker. Legacy JSON paste-import retained for backward compat. _(Completed 2026-04-01)_
- [x] **Patch persistence:** `ComponentConfig.SoundPatchId` (nullable string) added in Phase 10-A. `ISoundPatchLibrary` resolves at render-time; fallback to component fields if patch not found.

---

### Phase 10-C — Completions & Polish ✅

- [x] **BarDetailService full coverage:** Rich `GetDetailFact` narratives added for Volume (10-bar avg, surge/dry-up, building/declining trend), RSI (divergence hint), MACD (expanding/contracting histogram, zero-line approach), Bollinger Bands (live squeeze/expansion from 20-bar avg width, corrected %B), EMA/SMA/WMA (price-to-MA distance %, per-bar slope %), CCI (zone + direction), ADX (strength label + DI direction). CoreIndicatorProvider handles VOLUME; SkenderIndicatorProvider handles the rest.
- [x] **HelpModal keyboard reference audit:** `HelpModal.razor` now injects `IShortcutManager`. Added live "All Keyboard Shortcuts" section auto-generated from `GetAllBindings()`. Missing shortcuts (Alt+D/J/W/,, Alt+C/L, Ctrl+Shift+D, Shift+F12) added to UI & Settings table. `FormatCommandName()` helper inserts spaces in PascalCase command names.
- [x] **iOS / iPadOS hardware keyboard:** `Platforms/iOS/KeyboardPageHandler.cs` added — mirrors Mac Catalyst `PressesBegan` pattern. Registered in `MauiProgram.cs` under `#if IOS`.
- [x] **Settings import from file:** `readFileAsText` JS interop — `ImportVisualProfileAsync` and `ImportAudioProfileAsync` open native file picker and pass JSON to `IWorkspaceLibraryService`. _(Completed 2026-04-01)_
- [x] **Keyboard remapping UI:** Settings → Keyboard tab shows interactive table with Rebind button per command. `captureNextKey` JS captures next key combo in capture phase (before chart handler). `[JSInvokable] OnKeyCaptured` calls `IShortcutManager.UpdateBinding` + persists immediately. _(Completed 2026-04-01)_
- [x] **Coinbase / Polygon zero-value filter:** Coinbase: price ≤ 0 skipped. Polygon: all-zero OHLC frame skipped. Same pattern as Binance/Bitstamp/Alpaca.
- [x] **`StrategyIndicatorCache`:** `IStrategyIndicatorCache` + `StrategyIndicatorCache` (Core). Caches SMA/EMA/RSI/BollingerBands by `(type, period, data.Count)`. `StrategyEngine` injects it and calls `Invalidate` before each `OnBar` cycle. Registered as singleton.

---

### Phase 10-D — Custom Indicator Platform (Roslyn) ✅

- [x] **`ICustomIndicator` interface (`Sdk`):** `Id`, `DisplayName`, `ComponentNames[]`, `DisplayTypes[]`, `DefaultParameters`, `Calculate(ReadOnlySpan<Ohlcv>, parameters)` returning `double[][]`.
- [x] **`RoslynScriptingService`:** `CSharpCompilation` emit to in-memory DLL. Isolated `AssemblyLoadContext` per script (collectible). Sandbox: Sdk + System.Runtime.* only. `UnloadScript(id)` for cleanup. `ExecuteSimpleAsync` path retained for expression scripts.
- [x] **`CustomScriptsModal.razor` full implementation:** Script list (New/Delete), monospace code editor with ICustomIndicator template placeholder, Compile button → error output, Add to Chart button on success, Export .atpkg download.
- [x] **`.atpkg` format:** JSON payload `{Version, Name, Author, Code}`. Export via download JS interop; import via paste-and-parse in the Import section.
- [x] **`AddCustomIndicator`:** `ISeriesManagementService.AddCustomIndicator(indicator, state)` bridges compiled indicator to the chart's `RegisterSeries` pipeline.
- [ ] **`ICustomScriptService.RunScriptAsync` full pipeline:** Compiled `ICustomIndicator.Calculate` routed through `IndicatorOrchestrator` → results stored in `SeriesDataBuffer`. Currently registers via `RegisterSeries` but doesn't yet wire `Calculate` into the indicator recalc pipeline. Deferred to Phase 10-D.2.

---

### Phase 10-E — PineScript Transpilation ✅

Three-tier pattern-based transpiler (no ANTLR — hand-written regex/pattern approach).

#### Tier 1 — Core Mapping ✅
- [x] **Pattern-based transpiler:** `PineTranspiler` in `Core/PineScript/`. Regex patterns for all common Pine constructs.
- [x] **ta.* mapping:** `ta.sma/ema/rsi/macd/bb/atr/stoch/crossover/crossunder/highest/lowest/stdev` → C# helper arrays.
- [x] **plot() / plotshape():** Component registration. `plotshape` → `ComponentDisplayType.Dot`.
- [x] **Roslyn compile step:** Generated C# fed into `RoslynScriptingService.CompileIndicatorAsync`. Same ICustomIndicator sandbox.
- [x] **Static helpers embedded:** All ta.* equivalents as private static methods in the generated class.

#### Tier 2 — Extended Patterns ✅
- [x] **`var` / `varip`:** Stripped to plain variable declaration.
- [x] **`na` / `nz()` mapping:** `na` → `double.NaN`; `nz(x, d)` → `NzHelper`.
- [~] **Conditional color rules:** `color.new(...)` / ternary color expressions → ColorRule generation. **Detector shipped 2026-04-24** — every `color.new()` call site now emits a warning naming the feature so users know the dynamic coloring fell back to the component default. Mapping to `ColorRule` itself still deferred to the eventual ICustomStrategy host contract.

#### Tier 3 — Stubs ✅
- [x] **`request.security()`:** Replaced with `NanArr(n)` + warning in TranspileResult.Warnings.
- [~] **`line.new()` / `label.new()`:** **Detector shipped 2026-04-24** — every call site emits a `TranspileResult.Warnings` entry naming the feature and pointing to `docs/TODO.md` for the mapping path. Wiring to `DrawingService` itself still requires the `ICustomStrategy` host contract (Phase 10-D.2).
- [~] **`strategy.*` functions:** **Detector shipped 2026-04-24** — `strategy.entry`/`strategy.exit`/`strategy.close` each emit a warning per call site pointing users to the StrategyComposer (BuildSetupTab) for trading logic. Mapping to `TradeSignal` still requires the `ICustomStrategy` host contract.

---

### Phase 10-F — Strategy Platform Extension ✅ (partial)

- [x] **Custom C# Strategy tab:** `StrategyModal.razor` now has a tabbed layout (Add Strategy / Active / Backtest / Custom Script). Custom Script tab: textarea editor, C# template, execution mode, Compile & Add button.
- [x] **`IRoslynScriptingService.CompileStrategyAsync`:** Compiles user C# into `ITradingStrategy` via Roslyn, referencing both `AccessibleTrader.Sdk` and `AccessibleTrader.Core` so `BaseStrategy` is available. Result `CompileStrategyResult(Success, Strategy, Errors[])`. Errors shown inline in editor pane. On success: strategy added to `StrategyEngine`, tab switches to Active.
- [x] **`ConfigurableStrategy` class (`Core/Trading`):** shipped — see
  `AccessibleTrader.Core/Strategies/ConfigurableStrategy.cs`. Serializable
  `StrategySpec` + condition tree; persists via `JsonStrategyLibrary` +
  `strategies.json`.
- [x] **Strategy condition builder UI (StrategyModal):** shipped — see
  `BuildSetupTab.razor` (split into `ConditionTreeEditor` /
  `RiskPlanEditor` / `SummaryExport` in the 2026-04-24 Tier 3 sweep).
- [x] **DLL plugin strategy:** shipped 2026-04-24 Phase 10-F(a) — see
  `IStrategyPlugin` SDK contract + `StrategyPluginRegistry` + fixture
  plugin + 7 loader tests.
- [x] **`StrategyIndicatorCache` integration:** shipped 2026-04-24
  Phase 10-F(b) — SDK bridge `IPluginStrategyIndicatorCache` +
  `PluginHostServices.IndicatorCache` + per-bar `Invalidate` in the
  backtester.
- [x] **`IStrategyRegistry.GetCatalog()` extension:** shipped 2026-04-24
  Phase 10-F(c) — unified `StrategyRegistry` merges
  `IStrategyLibrary.All` + `IStrategyPluginRegistry.Templates` with
  spec-wins-on-ID-collision semantics.

### Phase 10-F2 — Accessible Cipher B ✅ Complete

- [x] **`CipherBProvider` (`Core/Services/Indicators/CipherBProvider.cs`):** Full native C# Market Cipher B replica. Wave Trend (WT1/WT2/WT Fill cloud), MC Money Flow histogram, Blue/Red/Gold signal dots, 4-type divergence detection (regular + hidden bull/bear dots). Parameters: WT1Period, WT2Period, MFPeriod, OBLevel, RSIPeriod, RSIOSLevel, PivotBars.
- [x] **Registered** in `ServiceCollectionExtensions.AddIndicatorPipeline()`.
- [x] **Reference levels:** ±60 (extreme OB/OS dotted), ±53 (OB/OS dashed), 0 (zero line). Injected via `IndicatorReferenceLevels`.
- [x] **StylingService:** Per-component color map — WT1 #00C8FF (blue), WT2 #7FDBFF, WT Fill cloud bullish/bearish, MF green/red, signal dots with distinct colors.
- [x] **PaneAssignmentService:** Category `Multi-Signal`, pane `Pane_CIPHER_B`.
- [x] **Cloud component metadata:** `IndicatorComponentMetadata.UpperComponentName`/`LowerComponentName` added; `IndicatorModelFactory` propagates to `ComponentConfig`. WT Fill links WT1 and WT2.
- [x] **OB/OS noise texturing:** `NavigationSonifier` detects Overbought/Oversold Level components and blends 0.20f noise when value exceeds threshold. `IAudioDriver.SetVoice` gains `noiseAmount` parameter (was only in AudioEngine; now propagated through BlazorAudioDriver).
- [x] **MFI/Chaikin styling fixed:** MFI → `Histogram` display with `ColorBaseline=50`. Chaikin OSC variants → `Histogram` with zero-crossing.
- [x] **`ComponentConfig.ColorBaseline`:** Used by `RenderDirectionalBars` as the green/red split threshold. Persisted in `Clone()` and `IndicatorModelFactory`.
- [x] **`CustomIndicatorRegistry`:** Thread-safe runtime lookup for Roslyn/Pine compiled `ICustomIndicator` instances. `IndicatorEngine` routes to registry before `IIndicatorService`.

---

### Phase 10-G — Indicator Architecture Improvements

- [x] **Self-describing indicator color/style metadata:** Added optional `DefaultColorHex`, `DefaultColorHexSecondary`, `DefaultThickness`, `ColorBaseline`, `DefaultDashStyle`, `DefaultColorSource` + audio hints (`DefaultWaveform`, `DefaultEnvelopeType`, `DefaultNoiseAmount`, `DefaultAmplitudeMapping`, `DefaultPitchMapping`, `DefaultBaseFrequency`) to `IndicatorComponentMetadata`. `IndicatorModelFactory.CreateComponentConfigFromMeta` applies metadata hints first, falls through to `IStylingService` role-based defaults. Migrated `CipherBProvider`, `SpiderLinesProvider`, `EmaFillProvider`, `SkenderIndicatorProvider` (lookup tables for RSI/MFI/Stoch/etc.). `StylingService` is now purely role/type-based — no indicator names.

- [x] **Extended shape vocabulary for component display types:** Added `ComponentDisplayType` values: `TriangleUp`, `TriangleDown` (direction-coded), `Diamond` (divergence), `Square` (discrete event), `Cross` (invalidation). Each has a `StandardRenderers.Render*` method, a `DataLayer` dispatch case, a Ping-envelope sonification profile in `SonificationProfileProvider`, and TTS-friendly strings in `SpeechFormatter.FriendlyTypeName`. `CipherBProvider` signal dots remain `Dot` (required for Ctrl+Left/Right sparse navigation in `CommandDispatcher`); new shapes are available for future providers that don't require dot-based navigation.

- [x] **Oscillator sonification rule:** `SonificationProfileProvider` oscillator/ZeroArea profiles use `AboveWaveform = "triangle"`, `BelowWaveform = "sine"` (rule: triangle above zero, sine below). `IndicatorModelFactory.CreateComponentConfigFromMeta` sets `ReferenceLevel = 0` for Oscillator/ZeroArea types so `DefaultSonificationStrategy` triggers the above/below waveform switch. Dynamic OB/OS noise (0.20f) computed in `CreateAudioPoint` by scanning Level siblings — playback now matches navigation noise behaviour. `AudioPoint` carries `NoiseAmount`; `AudioSequencer` passes it through to `SetVoice`.
- [x] **Indicator preferences service:** `IIndicatorPreferencesService` + `IndicatorPreferencesService` — per-indicator JSON prefs at `%LOCALAPPDATA%\AccessibleTrader\IndicatorPrefs\`. `IndicatorModelFactory.CreateSeriesFromMetadata` applies a 3-layer merge (metadata → workspace state-only → preferences). PropertiesModal "Save as Defaults" button persists current appearance + sonification as preferences. This permanently fixes the "stale workspace silences new audio defaults" problem.
- [x] **Ctrl+Left/Right sparse navigation generalised:** `CommandDispatcher` now recognises all marker display types (`Dot`, `ZeroDot`, `Arrow`, `Diamond`, `TriangleUp`, `TriangleDown`, `Square`, `Cross`) for sparse NaN-scan navigation — not just `Dot`.
- [x] **Dot/Arrow earcon profile:** `SonificationProfileProvider` has explicit `Dot`/`Arrow` case → Ping envelope, `PitchMapping.Direction` 660/220 Hz. Previously fell through to Sustain default.

- [x] **Indicator sub-panes:** `IndicatorComponentMetadata.SubPaneName` + `SubPaneHeightRatio` declare sub-pane membership. `ComponentConfig` carries these through from `IndicatorModelFactory`. `RenderContext.SubPaneFilter` controls which components each pass renders. `ChartRenderer.RenderPane` does multi-pass rendering: main area (top) + sub-pane strips (bottom, clamped 5–40% each). `ViewportRangeCalculator` accumulates per-sub-pane ranges under composite keys (`"PaneName/SubPaneName"`) — also fixes early-exit bug where only the first series per pane was computed. `DataLayer` sub-pane filter gate skips wrong-pass components; cloud fills and levels are main-area-only. `CipherBProvider` Money Flow Wave and Money Flow Dot now declare `SubPaneName = "MF", SubPaneHeightRatio = 0.22f`.
- [x] **Sub-pane follow-up — remove normalization:** ±35 scaling removed from `LaguerreRsi` / `ComputeStochRsi` in `CipherBProvider.Calculate`; ±30 MF normalization removed — raw values now fill their sub-pane naturally (2026-03-30).
- [x] **Sub-pane follow-up — drag-resize + persistence:** Sub-pane height ratio exposed as drag handle (ResizePaneAction pattern). Sub-pane ratios persisted in `WorkspaceConfiguration` using composite key scheme (2026-03-30).

---

### Phase 10-H — Alerts, Multi-Workspace, Drawing Completions

- [x] **Alert delivery channels (moved from Phase 10-G):** service-layer
  shipped 2026-04-24. `IAlertChannel` SDK contract, `EmailAlertChannel`
  (SMTP), `TelegramAlertChannel` (Bot API), `AlertDeliveryService` fan-out
  via `AlertFiredEvent`. Config lives under `alerts.email.*` /
  `alerts.telegram.*` setting keys and loads per-send via
  `ISettingsManager`. Settings-modal **"Alerts" tab shipped 2026-04-24**
  (same day) with per-channel "Send test" buttons that resolve the live
  `IAlertChannel` from DI.
- [x] **Multi-workspace tabs:** `WorkspaceState` extended with `TabSnapshots` + `ActiveTabIndex` + `TabCount`. `TabSnapshot` record freezes per-tab fields. `AddTabAction`, `CloseTabAction`, `SwitchTabAction`, `ToggleNarrationAction` reducer cases in `WorkspaceStore`. `TabBar.razor` renders between Toolbar and chart; hidden when only one tab open. Keyboard: `Ctrl+T` (new), `Ctrl+W` (close), `Ctrl+Tab` / `Ctrl+Shift+Tab` (cycle). `TabSwitchedEvent` published for audio engine stop. TTS announces tab label on switch. 14 tests added (`MultiTabTests.cs`). Build: 0 errors. Tests: 176/176. (2026-04-01)
- [x] **Drawing tool completions:** Audited all 16 registered drawing tools. All anchor counts and sequencing correct. One bug fixed: `GannBoxCalculator` price levels were spanning the entire chart instead of being bounded within the anchor date range — now fills NaN outside [i1,i2] and adds time subdivision points at Gann ratios. AVWAP confirmed correct (recalculated from scratch on each `Calculate()` call, so live bars work naturally). Build: 0 errors. Tests: 176/176. (2026-04-01)
- [x] **`AutoNarrationService`:** `SeriesConfig.IsAutoNarrated` + `ChartSeries.IsAutoNarrated` delegation. `ToggleNarrationAction` in store. `Ctrl+Shift+N` toggles narration for focused series. `AutoNarrationService` subscribes to `IndicatorUpdatedEvent` + `StateStream`; detects new marker signals (non-NaN Dot/Arrow/Diamond/etc.) on closed bars and oscillator zone transitions; announces via `ISpeechFeedbackRouter` (non-interrupting). Seeding prevents retroactive announcements when narration is enabled. "narrating" appended to series state suffix in `NavigationFeedbackManager`. `Ctrl+Shift+D` (existing `BarDetailService`) already reads non-NaN column values for focused series. 10 tests added (`AutoNarrationTests.cs`). Build: 0 errors. Tests: 162/162. (2026-04-01)
- [ ] **Three-tier level crossing earcons:** Tier 1 = approach (within 5% of OB/OS level, amplitude scales with proximity), Tier 2 = crossing (existing `PlayBoundary()`), Tier 3 = sustained beyond level >3 bars (looping low-amp background tone). Tracked per series/level in `LevelCrossingMonitor` singleton.
- [x] **Live AI Technical Analyst:** `IAIAnalystService` + `ILLMProvider` plugin contract in Sdk. Providers: `ClaudeProvider` (claude-sonnet-4-6), `OpenAIProvider` (gpt-4o), `OllamaProvider` (local llama3). Priority: Claude → OpenAI → Ollama (first configured key wins; Ollama needs no key). `Ctrl+Alt+Shift+A` → `AIAnalystModal.razor` (auto-triggers on open). Announces "no API key configured" if none found. Builds OHLCV prompt (50 viewport bars) + indicator summary + offscreen PNG snapshot via `SKSurface`. Speech-reads result. Build: 0 errors. Tests: 176/176. (2026-04-01)
- [~] **NAudio.Wasapi removal:** tracked in Platform Parity section.

---

## UPCOMING — Sonification & Audio Engine Improvements

### Phase B — Audio Engine: Bell Synthesis Foundation ✅ Complete (2026-03-31)
- [x] Configurable decay length in ComponentConfig (DecayMs field, nullable) — comp.DecayMs overrides patch default; both applied in AudioSequencer and NavigationSonifier
- [x] SoundPatchId wired to SoundPatchRegistry (built-in patches: sine_bell, triangle_bell, crystal_bell, detuned_pair_bell, gradient_blend) — ISoundPatchRegistry singleton registered in DI
- [x] Bell harmonic content (HarmonicAmount/HarmonicFreqMultiplier fields on SoundPatch record — consumed by AudioSequencer for future AudioEngine integration)
- [x] PlaybackLayer enum on ComponentConfig (Background=60%, Midground=80%, Foreground=100%) — AudioSequencer applies LayerVolume() scale in both StartPlaybackAsync and StartMultiSeriesPlaybackAsync
- [x] Detuned paired bell: AudioSequencer fires two voice commands with configurable ms offset (DetunedOffsetMs via Task.Delay); NavigationSonifier uses Slot 1 for detuned voice

### Phase C — Cipher A: Self-Describing Metadata + Sonification ✅ Complete (2026-03-31)
- [x] All 8 Cipher A components get Default* metadata fields (colors, thickness, SoundPatchId, DecayMs, frequency)
- [x] Gradient blend patch wired for WT Momentum dot (SoundPatchId = "gradient_blend")
- [x] Buy/Sell signals: sine_bell, 880/220 Hz, 380ms decay
- [x] Divergence diamonds: triangle_bell, 660/330 Hz, 280ms decay
- [x] Blood Diamond: triangle_bell, 165 Hz, 500ms decay
- [x] Manipulation/Exhaustion: detuned_pair_bell, 320ms decay
- [x] Component-level contextual speech: UsesGradientSpeech on WT Momentum; SpeechFormatter reads companion _color array and maps WT1 oscillator value to qualitative momentum language (strong/moderate bullish/bearish/neutral). IndicatorComponentMetadata.DefaultSoundPatchId and UsesGradientSpeech fields added; ComponentConfig.UsesGradientSpeech propagated by IndicatorModelFactory.

### Phase D — Cipher B: Sonification Redesign ✅
- [x] Anchor waves: Background layer, triangle (WT1 Anchor) / sine (WT2 Anchor), AmplitudeMapping.None
- [x] WT1: Midground, triangle above / sawtooth below zero, Value pitch, AmplitudeMapping.None
- [x] WT2: Midground, smooth sine throughout, Value pitch, AmplitudeMapping.None
- [x] Trigger Wave: Midground, triangle, DefaultFreqMultiplier=1.3 for snappier "ahead" character
- [x] Money Flow Wave: Midground, sine both sides, 0.08 noise preserved
- [x] MF dot (ZeroDot): sine_bell, 150ms, Direction pitch 600/250 Hz, Midground
- [x] MF Signal Large: sine_bell, 350ms, Direction pitch, Foreground
- [x] MF Signal Small: sine_bell, 160ms, Direction pitch, Foreground
- [x] RSI~/Stoch %K/%D/VWAP~: Background layer, triangle waveform (contextual subdued)
- [x] Oversold Crossover (Blue): sine_bell, 840 Hz, 350ms, Foreground
- [x] Overbought Crossover (Red): sine_bell, 210 Hz, 350ms, Foreground
- [x] Triple Confluence Buy (Gold): dual_tone_bell (440+660 Hz simultaneous chord), 500ms, Foreground
- [x] Bullish Divergence: triangle_bell, 620 Hz, 230ms, Foreground
- [x] Bearish Divergence: triangle_bell, 310 Hz, 230ms, Foreground
- [x] Hidden Bull Continuation: triangle_bell, 520 Hz, 180ms, Foreground
- [x] Hidden Bear Continuation: triangle_bell, 360 Hz, 180ms, Foreground
- [x] Added dual_tone_bell patch in SoundPatchRegistry (220 Hz apart, simultaneous, 500ms decay)
- [x] Added DefaultAboveWaveform, DefaultBelowWaveform, DefaultBullishFrequency, DefaultBearishFrequency, DefaultFreqMultiplier to IndicatorComponentMetadata
- [x] Wired new metadata fields in IndicatorModelFactory.CreateComponentConfigFromMeta

### Phase E — Cipher SR: Sonification Design ✅
- [x] Resistance/Support pivot dots: crystal_bell, 700/330 Hz, 220ms decay, Foreground layer
- [x] Zone lines: contextual hum in NavigationFeedbackManager when price within zone (0.5% tolerance, slot 2, 100ms sine)
- [x] IsZoneLine on ComponentConfig + DefaultIsZoneLine on IndicatorComponentMetadata; wired in IndicatorModelFactory
- [x] INavigationSonifier.PlayZoneProximity(float frequency, bool isResistance) added and implemented

### Phase F — Cluster/Shapes-as-Ticks Navigation
- [x] NavigationSonifier fires N ticks (100ms apart) when bar has N marker shapes
- [x] Significance ordering: structural (SR/divergence) first, action (crossover) second

### Phase G — Speech: Contextual Component Descriptions
- [x] `SignalSpeechTemplate` on `ComponentConfig` + `DefaultSignalSpeechTemplate` on `IndicatorComponentMetadata`
- [x] Cipher A gradient dot qualitative range-aware speech (UsesGradientSpeech, from Phase C)
- [x] Cipher A Buy/Sell/Divergence/BloodDiamond/Manipulation/Exhaustion signal speech templates
- [x] Cipher B Triple Confluence, Oversold/Overbought Crossover, divergence/hidden continuation speech templates
- [x] Cipher B MF Signal Large/Small speech templates
- [x] Cipher SR Resistance/Support pivot dot speech includes zone level value ("Resistance pivot at {price}")
- [x] Multi-signal bar speech sequences in same order as audio ticks (Component context, "Also: ..." prefix)
- [x] SR zone proximity speech: "Near resistance/support at {level}" on zone hum fire

### Phase H — Cloud Sonification Architecture (COMPLETE 2026-03-31)
- [x] CloudFillConfig gains optional CloudSonificationConfig (frequencies, patch, amplitude mode)
- [x] AudioSequencer cloud-aware pass in multi-series playback
- [x] EMA Fill cloud sonification declared in metadata
- [x] CipherB WT Fill cloud sonification declared in metadata
- [x] Cloud voice in dedicated slot range (slots 64-79, separate from component slots 32-63)

### Phase I — Drawing Tools: Coordinate Entry Mode ✓ (2026-03-31)
- [x] Keyboard-only anchor placement mode (navigate to point, Enter to set)
- [x] TTS announces price + timestamp during coordinate entry navigation
- [x] Anchor 1 confirmed → navigation speech includes change-from-anchor delta
- [x] Escape cancels CE mode with speech feedback
- ~~Live Preview for trendline dragging~~ (removed — not planned)

### Phase J — Ctrl+Left/Right Crossing Navigation Redesign
- [x] Context-aware crossing: zero-line for MACD/Momentum, OB/OS for RSI/MFI, band for Bollinger, MA-cross for EMA/SMA

### Phase K — Ichimoku Indicator
- [x] Full Ichimoku implementation (Tenkan, Kijun, Chikou, Senkou A/B)
- [x] Dual Kumo cloud fills (Senkou A/B)
- [x] Future cloud projection (26 periods ahead) handled gracefully in navigation

### Phase L — Test Coverage Expansion
- [x] SoundPatchRegistryTests (7): built-in patch presence, custom registration/replacement, detuned/gradient properties
- [x] PlaybackLayerTests (4): volume multipliers, default layer, factory propagation, clone preservation
- [x] DecayMsTests (4): default null, factory propagation, clone with/without value
- [x] CipherAMetadataTests (13): all 8 components verified for patch ID, frequency, decay, layer, gradient speech
- [x] CipherBMetadataTests (10): Triple Confluence dual-tone, crossover frequencies, divergence patches, Background anchors
- [x] CipherSrMetadataTests (7): crystal bell, zone line flag, factory propagation to ComponentConfig
- [x] IchimokuProviderTests (12): component count, cloud fill, Tenkan/Chikou/Senkou calculations, GetDetailFact, stability window
- [x] CloudSonificationTests (8): backward compat null, Clone preserves Sonification, EMA/Ichimoku/CipherB frequencies
- [x] CrossingNavigationTests (3): zero-line crossing, OB threshold entry, no-crossing returns -1

---

## PHASE 12 — Strategy Research: System Upgrades for Score-Based Confluence (planned, 2026-04-07)

The v2-v6 strategy iteration sprint produced one walked-forward stable strategy (v2) and four failures (v3/v4/v5/v6). The cross-strategy decay pattern + indicator code audit revealed that the system has substantial unused capability and several bugs that prevent the next class of strategies from being built. Phase 12 ships the infrastructure required for v7 (multi-source score-based confluence) and addresses the documented gaps.

**Reference:** `project_strategy_research_2026_04_07.md` in memory + the CHANGES.md entry for the same date document the empirical results that motivate this phase.

### Required system upgrades (in priority order — DO BEFORE building v7)

- [x] **Score-based root operator** — shipped. Evaluator landed in earlier
  session; BuildSetupTab UI (dropdown + threshold input with max-score
  hint) shipped 2026-04-24.

- [x] **Pivot strength filter on level operators** — shipped.
  `ConditionLeaf.MinLevelStrength` + `ConditionEvaluator.FilterByStrength`
  already present; BuildSetupTab UI input shipped 2026-04-24. Touch-count
  filter still deferred — would need a new operator variant rather than
  a parameter, and is only a marginal win over the strength gate.

- [x] **HTF future-leak bug fix in `EvaluateHtfIndicatorLeaf`** — shipped. `ConditionEvaluator.HtfLastClosedIndexExclusive` clips HTF reads via strict-less-than binary search on `history[^1].Date`, and both `EvaluateHtfIndicatorLeaf` and `EvaluateHtfPriceLeaf` honour the exclusive end. Tests: `ConditionEvaluatorHtfTests.cs` (10) including perfect-alignment + before-all + after-all edges.

- [x] **VPVR backtest replay end-to-end verification** — shipped
  2026-04-24. `VpvrBacktestReplayTests` (4 tests) pins the chain:
  cache IsActive/Set/Get/Clear semantics, provider-reads-cache-when-
  active, provider-falls-through-when-inactive, no-profile-series empty
  case. Any future refactor that breaks the cache preference will trip
  these tests.

- [x] **Rolling-window score aggregation** — already shipped as typed
  operator variants (`GreaterThanWithin`, `LessThanWithin`,
  `BetweenWithin`, `PercentileBelow`, `PercentileAbove`). The 2026-04-24
  sweep extended the BuildSetupTab `NeedsWithinN` gate to surface the
  Within-N input for every operator that consumes it.

- [x] **Expose Cipher A WT Momentum gradient as a queryable signal** —
  shipped 2026-04-24 as `CIPHER_A.WT Momentum Gradient` hidden Line
  component. Normalised 0.0..1.0 derivation in `CipherAProvider.Calculate`
  (raw WT1 clamped to ±OBLevel then linear-mapped). Strategies gate via
  the standard leaf operators (`GreaterThan 0.7 = strong overbought`).

### v7 strategy build (AFTER infrastructure is in place)

- [ ] **Build v7 — Score-based multi-source confluence**
  - Single condition tree using the Score root operator
  - Leaves combining: Cipher A/B momentum pulses (score 1.0 each, FiredWithin 5), Cipher A/B divergences (score 2.0, FiredWithin 7), Cipher B gold cross (score 2.0), Cipher SR support with `MinLevelStrength=0.7` (score 1.5), VPVR value area / POC / LVN wick (score 1.0-1.5), HTF Cipher B uptrend (score 1.5, requires HTF bug fix)
  - Threshold ~4.0 — fires when ≥4 points of evidence align
  - Same risk plan as v2 (ATR(14)×2 stop, 1.5R/3R ladder, breakeven after TP1, 0.5% risk) for clean comparison
  - Required indicators on chart: Cipher A, Cipher B, Cipher SR, VPVR. Cipher C optional as additional weighted contributor.
  - Expected on BTC 1d: 35-55 trades over 9 years, WR 60-70%, Avg R 0.50-0.80, PF 2.0-3.0
  - Validation: full backtest first, then walk-forward halves, then ETH 1d cross-symbol

### Bigger systemic improvements (future, lower priority)

- [ ] **Walk-forward parameter optimization (expanding window)** — re-tune Cipher periods every N months using only data prior to that point. Multi-week project. Real-but-careful curve-fit avoidance.
- [ ] **Regime classifier with regime-conditional strategy routing** — classify each bar as trending/ranging/volatile via ADX + ATR percentile + autocorrelation features, route to different strategies. Multi-week project.
- [ ] **Indicator-on-HTF computation** (deferred from Session B) — sync `IIndicatorRunner` so HTF leaves can reference indicators not just price. Currently HTF leaves fall through to active-TF data.
- [ ] **Expand SignalCatalog companion-array support** — generic mechanism for indicators to expose multiple value streams per component (not just `_color` and `_touches`)

### Strategies that should be deleted from the library after v7 lands

- [ ] **v4 r1 (`builtin.cryptoface.long.v4-claude`)** — broken HTF leaf, kept as teaching example. Delete after Phase 12 HTF fix is verified working.
- [ ] **v6 (`builtin.long.v6-cipher-c-cycle`)** — no edge in either walk-forward half. Cipher C cycle math may not latch onto BTC daily; retain only if visual verification confirms Lead Sine actually leads price turns.
- [ ] **v3 (`builtin.cryptoface.long.v3`)** — empirically refuted by being worse than v2 in absolute terms. Could delete as well, or keep as a reference example of the literal Crypto Face stage gates.

### Open question (no work item, just documented)

The walk-forward decay pattern across v2-v6 (better in first half than second half across ALL strategies) suggests modern BTC daily is structurally harder to trade with retail momentum signals than early BTC was. Three plausible causes (in order): public signal decay from millions of Crypto Face viewers, market structure maturation (institutional flow + ETF + perpetuals), and inflated early-BTC bull run dynamics. **No code change can fix this** — it's a property of the asset and the indicator family. The honest realistic upper bound on Cipher-based strategies on BTC 1d in current conditions is approximately v2's second-half walk-forward result: PF ~1.5 net of costs, Avg R ~0.4, win rate ~56%. v7 might improve this by ~0.2-0.4 R if the orthogonal-source confluence hypothesis is right; it won't transform it.

---

## COMPLETED — Earlier Phases (Pre-2026-03-26 Session)

- [x] **Universal Skender Discovery:** Robust `IQuote` generic argument detection.
- [x] **Drawing Tool Suite:** Risk/Reward, AVWAP, Pitchfork, Gann Fan/Box, Measure Tool, all 15+ registered.
- [x] **Indicator Categorization:** Intelligent lookup table — Trend/Momentum/Volatility/Volume/Profiles.
- [x] **Zero-Allocation Data Pipeline:** `ComponentConfig.Data` → `double[]` with `double.NaN`.
- [x] **Custom Audio Engine:** Replaced NAudio synthesis with pure C# DSP engine.
- [x] **Platform Migration:** WinUI 3 → .NET 10 MAUI Blazor Hybrid.
- [x] **Professional Drawing Suite:** Risk/Reward, AVWAP, Pitchfork, Gann Fan/Box, Measure Tool.
- [x] **Archetype Injection:** 0/30/70 reference levels auto-injected into oscillator indicators.
- [x] **State Machine Implementation:** DataOrchestrator lifecycle state machine.
- [x] **Exclusive Focus Sonification:** Eliminated sawtooth leakage during distribution navigation.
- [x] **ProfileBinClassifier:** Node classification shared by sonification and speech.
- [x] **Profile/Heatmap Sonification:** Node-type-based pitch system, heatmap sawtooth waveform.
- [x] **Double-announcement fix:** Navigation feedback exclusively via `FeedbackRequestEvent`.
- [x] **F2/F3/F4/F5-F7 wiring:** All function key commands correctly dispatched and announced.
- [x] **NavKeyReleasedEvent chain:** Arrow keyup stops navigation voice immediately.
- [x] **Modal z-index:** `.modal-overlay` at z-index 9999.
- [x] **PrependOlderDataAsync notify:** `NotifyDataUpdate` + `SetDataStatusAction(Ready)` after backfill.
- [x] **SonifyHeatmap null safety:** Guarded `SelectMany` against null inner lists.
- [x] **Series nav shortcuts corrected:** Page Up/Down = series; Up/Down arrows = component.
- [x] **All 21 tests passing** (as of 2026-03-25 sprint).
