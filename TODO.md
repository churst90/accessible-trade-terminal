# TODO — Accessible Trading Terminal

This file tracks all known bugs, improvements, and roadmap items. Items are organized by improvement-plan phase. Checked items `[x]` are confirmed complete. Open items `[ ]` are pending.

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
- [ ] Full slice reducer decomposition into separate functions per domain — deferred to Phase 5+.

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
- [ ] Implement "Live Preview" for trendline dragging via JS-to-C# event streaming.
- [x] Add "Coordinate Entry" mode for accessibility-first drawing creation (keyboard-only placement without cursor). *(Phase I, 2026-03-31)*

### Technical Analysis Polish
- [ ] Implement Bollinger Band 'Squeeze' and 'Expansion' logic in `IndicatorContextAnalyzer.GetDetailFact`.
- [ ] Add MACD crossover facts (Bullish/Bearish crosses) to `BarDetailService`.
- [ ] Implement Volume-Profile POC-crossing alerts in `AlertEvaluator`.

### Ctrl+Left/Right Crossing Navigation Redesign
- [x] Generalized to use focused series type (Phase J, 2026-03-31): price/candles → trendline, zero-line oscillators → zero cross, threshold oscillators → OB/OS entry/exit, MA overlays → price/MA cross, %B → band crossing, sparse markers → nearest non-NaN signal.
- [x] Crossing logic extracted to `IndicatorCrossingEngine` (Phase 4-SRP, 2026-04-01) — independently testable, no longer coupled to `CommandDispatcher`.
- [ ] Multiple trendlines: use the focused drawing, not "all trendlines."

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
- [ ] **Binance OrderUpdateStream:** User Data Stream WebSocket (listenKey management) not yet implemented. Subject exists but is never pushed to.
- [ ] **Bitstamp OrderUpdateStream:** WebSocket `order` channel exists but not yet mapped to `_orderUpdateSubject`.

### Binance Plugin
- [x] **Futures PlaceOrderAsync:** Routes to `UsdFuturesApi.Trading.PlaceOrderAsync` when `signal.SubType == "Futures"`. Applies leverage before order. Attaches TP stop as separate order.
- [ ] **Binance User Data Stream:** listenKey create/keep-alive/close + fill → `OrderUpdate` mapping. Deferred.

### Bitstamp Plugin
- [x] HMAC-SHA256 trading fully implemented. WebSocket live trades + order book diff stream.
- [ ] Wire `order` channel events → `OrderUpdateStream`. Deferred.

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
- [ ] **Mac Keyboard Input:** `NSEvent.AddLocalMonitorForEventsMatchingMask` in `AppDelegate.cs` → `IInputService.ProcessKey`.
- [ ] **Android Audio Output:** `AudioTrack`-based output in `BlazorAudioDriver` under `#if ANDROID`.
- [ ] **iOS Audio Output:** `AVAudioEngine` render callback in `BlazorAudioDriver` under `#if IOS || MACCATALYST`.
- [ ] **NAudio.Wasapi Removal:** After platform drivers validated, remove from `BlazorClient.csproj`.

### Remaining Provider Gaps
- [x] **Binance User Data Stream:** `StartUserDataStreamAsync` creates listenKey, subscribes via `_socketClient.SpotApi.Account.SubscribeToUserDataUpdatesAsync`, 25-min keepalive timer, cleanup in `DisconnectAsync`.
- [x] **Bitstamp OrderUpdateStream:** `SubscribePrivateChannelAsync` sends HMAC-SHA256 auth for `private-my_orders-{pair}`; `ReceiveLoop` handles `order_changed`/`order_deleted` → `_orderUpdateSubject`.

### Feature Completion
- [x] **Strategy Backtester UI:** `StrategyModal.razor` — Backtest section with capital/commission/slippage inputs, Run button, results grid (trades/win rate/P&L/drawdown/Sharpe), trade log details. `IStrategyBacktester` DI-registered in `ServiceCollectionExtensions`.
- [ ] **Custom Speech Template Editor:** "Speech" tab in `SettingsModal` with editable template fields.
- [ ] **Multi-Symbol Watchlist:** Extend `WorkspaceState` to hold collection of `ChartState`.

### Platform Parity
- [x] **Mac Keyboard Input:** `KeyboardPageHandler` (custom `PageHandler`) with `KeyboardViewController` override of `PressesBegan`. Uses NSEvent Unicode private-use characters for special keys. Registered in `MauiProgram.cs` via `#if MACCATALYST`.
- [x] **Android Audio Output:** `AudioTrack` PCM-Float push loop on `TaskCreationOptions.LongRunning` thread in `BlazorAudioDriver` under `#if ANDROID`.
- [x] **iOS/Mac Catalyst Audio Output:** `AVAudioEngine` + `AVAudioSourceNode` render callback in `BlazorAudioDriver` under `#if IOS || MACCATALYST`. De-interleaved via `Marshal.Copy`.
- [ ] **NAudio.Wasapi Removal:** After platform drivers validated on device, remove from `BlazorClient.csproj`.

### Chart Focus Shortcut
- [x] **Ctrl+Alt+Shift+C:** `SystemCommand.ChartFocus`, `ShortcutManager` binding, `CommandDispatcher` handler publishes `ChartFocusEvent` + `CONTEXT_SUMMARY` feedback. `HelpModal.razor` and `SHORTCUTS.md` updated.

### Performance (from previous Phase 6)
- [ ] **Span-Based Indicator Pipeline:** `ReadOnlySpan<Ohlcv>` + `ArrayPool<double>` in `SkenderIndicatorFactory`.
- [ ] **Full Channels Migration:** `Channel<Ohlcv>` from plugin → `DataManager` for live ticks.
- [ ] **Voice Slot Pooling:** Reuse/reset `OscillatorVoice` objects in `AudioEngine`.
- [ ] **EventBus Batch Notifications:** Coalesce multi-fire notifications with `Throttle`.

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
- [ ] **Area fill sonification (band width → amplitude):** Deferred to Phase 10-B alongside Sound Designer.

#### A5: AudioEngine Noise Oscillator ✅
- [x] `WaveformType.Noise` — pure pink noise via one-pole filter.
- [x] `ComponentConfig.NoiseAmount [0,1]` — blends noise into any waveform. Default 0 = zero overhead.
- [x] `OscillatorVoice.NoiseAmount` / `OscillatorVoice.NoiseState` — per-voice state; persists between samples for smooth texture.
- [x] `AudioEngine.SetVoice(... noiseAmount = 0f)` — optional param; all existing callers unaffected.
- [x] **PropertiesModal Audio tab NoiseAmount slider** — per-component range slider in Sonification tab. _(Completed 2026-04-01)_
- [ ] **Bollinger Band noise preset** — deferred.

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
- [ ] **Conditional color rules:** `color.new(...)` / ternary color expressions → ColorRule generation. Deferred.

#### Tier 3 — Stubs ✅
- [x] **`request.security()`:** Replaced with `NanArr(n)` + warning in TranspileResult.Warnings.
- [ ] **`line.new()` / `label.new()`:** Not yet mapped to DrawingService. Deferred.
- [ ] **`strategy.*` functions:** Not yet mapped to TradeSignal. Deferred.

---

### Phase 10-F — Strategy Platform Extension ✅ (partial)

- [x] **Custom C# Strategy tab:** `StrategyModal.razor` now has a tabbed layout (Add Strategy / Active / Backtest / Custom Script). Custom Script tab: textarea editor, C# template, execution mode, Compile & Add button.
- [x] **`IRoslynScriptingService.CompileStrategyAsync`:** Compiles user C# into `ITradingStrategy` via Roslyn, referencing both `AccessibleTrader.Sdk` and `AccessibleTrader.Core` so `BaseStrategy` is available. Result `CompileStrategyResult(Success, Strategy, Errors[])`. Errors shown inline in editor pane. On success: strategy added to `StrategyEngine`, tab switches to Active.
- [ ] **`ConfigurableStrategy` class (`Core/Trading`):** Implements `IStrategy`. Drives execution from a serializable `StrategyConditionSet` (list of `StrategyCondition` — indicator, component, operator, threshold/crossover-target). AND/OR logic. Entry/exit/stop-loss conditions. Persisted to `strategies.json`.
- [ ] **Strategy condition builder UI (StrategyModal):** Visual no-code strategy builder wizard. Step 1: Name + execution mode. Step 2: Entry conditions. Step 3: Exit conditions + stop-loss. Step 4: Review + Save.
- [ ] **DLL plugin strategy:** `strategies/` drop folder scanned by `StrategyRegistry` at startup. Same `IStrategy` contract. `AssemblyLoadContext` isolation.
- [ ] **`StrategyIndicatorCache` integration:** `ConfigurableStrategy` and script strategies resolve indicator values from `IStrategyIndicatorCache` — no chart dependency.
- [ ] **`IStrategyRegistry.GetCatalog()` extension:** Returns built-in + user-defined + DLL-plugin strategies in a unified list for the StrategyModal template picker.

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

- [ ] **Alert delivery channels (moved from Phase 10-G):** Email alerts via SMTP (`System.Net.Mail`) — configure in Settings → Alerts tab (SMTP server, from/to, TLS). Telegram via Bot API (`HttpClient` POST to `api.telegram.org/bot{token}/sendMessage`). Both opt-in; keys stored in `IApiKeyService` (encrypted).
- [x] **Multi-workspace tabs:** `WorkspaceState` extended with `TabSnapshots` + `ActiveTabIndex` + `TabCount`. `TabSnapshot` record freezes per-tab fields. `AddTabAction`, `CloseTabAction`, `SwitchTabAction`, `ToggleNarrationAction` reducer cases in `WorkspaceStore`. `TabBar.razor` renders between Toolbar and chart; hidden when only one tab open. Keyboard: `Ctrl+T` (new), `Ctrl+W` (close), `Ctrl+Tab` / `Ctrl+Shift+Tab` (cycle). `TabSwitchedEvent` published for audio engine stop. TTS announces tab label on switch. 14 tests added (`MultiTabTests.cs`). Build: 0 errors. Tests: 176/176. (2026-04-01)
- [x] **Drawing tool completions:** Audited all 16 registered drawing tools. All anchor counts and sequencing correct. One bug fixed: `GannBoxCalculator` price levels were spanning the entire chart instead of being bounded within the anchor date range — now fills NaN outside [i1,i2] and adds time subdivision points at Gann ratios. AVWAP confirmed correct (recalculated from scratch on each `Calculate()` call, so live bars work naturally). Build: 0 errors. Tests: 176/176. (2026-04-01)
- [x] **`AutoNarrationService`:** `SeriesConfig.IsAutoNarrated` + `ChartSeries.IsAutoNarrated` delegation. `ToggleNarrationAction` in store. `Ctrl+Shift+N` toggles narration for focused series. `AutoNarrationService` subscribes to `IndicatorUpdatedEvent` + `StateStream`; detects new marker signals (non-NaN Dot/Arrow/Diamond/etc.) on closed bars and oscillator zone transitions; announces via `ISpeechFeedbackRouter` (non-interrupting). Seeding prevents retroactive announcements when narration is enabled. "narrating" appended to series state suffix in `NavigationFeedbackManager`. `Ctrl+Shift+D` (existing `BarDetailService`) already reads non-NaN column values for focused series. 10 tests added (`AutoNarrationTests.cs`). Build: 0 errors. Tests: 162/162. (2026-04-01)
- [ ] **Three-tier level crossing earcons:** Tier 1 = approach (within 5% of OB/OS level, amplitude scales with proximity), Tier 2 = crossing (existing `PlayBoundary()`), Tier 3 = sustained beyond level >3 bars (looping low-amp background tone). Tracked per series/level in `LevelCrossingMonitor` singleton.
- [x] **Live AI Technical Analyst:** `IAIAnalystService` + `ILLMProvider` plugin contract in Sdk. Providers: `ClaudeProvider` (claude-sonnet-4-6), `OpenAIProvider` (gpt-4o), `OllamaProvider` (local llama3). Priority: Claude → OpenAI → Ollama (first configured key wins; Ollama needs no key). `Ctrl+Alt+Shift+A` → `AIAnalystModal.razor` (auto-triggers on open). Announces "no API key configured" if none found. Builds OHLCV prompt (50 viewport bars) + indicator summary + offscreen PNG snapshot via `SKSurface`. Speech-reads result. Build: 0 errors. Tests: 176/176. (2026-04-01)
- [ ] **NAudio.Wasapi removal:** After Android/iOS audio validated on device, remove NAudio.Wasapi conditional reference from `BlazorClient.csproj`.

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
