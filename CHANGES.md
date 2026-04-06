# Changelog

All notable changes to this project will be documented in this file.

---

## [2026-04-05] — Cipher S Revamp + Viewport Right Margin

### Changed: Cipher S — High-low channel normalization (algorithm v5)
Replaced the percentile rank counting approach with proper high-low channel normalization, plus two further improvements for accuracy and performance.

**Root cause of cold colors absent:** Percentile rank counted how many historical bars were *below* the current close. On a secularly trending asset like BTC, the 2022 bear market lows still ranked high (60–70th percentile) because the window contained much lower 2018/2019 prices. The result: blue/teal/cyan phases (0–3, "Fear" spectrum) were never reached even at true cycle bottoms.

**New algorithm (three-pass):**
1. **High-low channel normalization:** `rawPct[i] = (close[i] - wLow) / (wHigh - wLow) × 100` — anchors sentiment to the current cycle's own extremes, not a multi-year rank table.
2. **5th/95th percentile clipping:** Sort the window, use indices at 5% and 95% as `wLow`/`wHigh`. Prevents flash-crash lows or thin-volume ATH spikes from anchoring the channel and compressing all other bars into a narrow middle band.
3. **3-bar EMA smoothing:** α = 0.5 (i.e. EMA period 3), applied to `rawPct` before the `PercentileToPhase` mapping. Eliminates single-candle flicker on compressed charts without distorting the phase trend.

**Performance optimization:**
- `RequiresFullRecalcOnTick` changed from `true` → `false`.
- `UpdateLast()` implemented: recalculates only the last bar on live ticks. Reads `pctSpan[i-1]` from the indicator buffer as the EMA seed for continuity. Reduces per-tick cost from O(n×window) to O(window).
- Scroll-back correctness: `DataOrchestrationService` already calls `OnDataUpdated(forceFull: true)` on historical prepend, triggering full `RecalculateAllAsync` — no change needed.

**`ResolveWindow()` helper:** `w == 0 ? 1500 : w` — guards against the zero default during incremental updates before auto-detection fires.

**Build: 0 errors, 0 warnings. Tests: 236/236 passing.**

---

### Fixed: Left-side chart bar compression (xOffset removed from ChartRenderer)

**Root cause:** `ChartRenderer.RenderPane` computed `float xOffset = rect.Width - (visibleData.Count * itemWidth)` and applied it as a left-shift to all bar positions. This was intended to right-align bars when `visibleData.Count < viewportLength`. In practice it *compressed* the left portion of the chart whenever fewer bars than the full viewport were visible (zoom-in, early data, historical edge), making bars appear squashed on the left side while the right side had correct spacing.

**Fix:** Removed `xOffset` entirely from `RenderPane`, `RenderXAxis`, and `RenderCrosshair`. Bar positions now start from `rect.Left` with uniform `itemWidth` spacing. Empty space is handled exclusively by `RightMarginBars` (see below) — future space falls naturally to the right of the last data bar.

---

### Added: RightMarginBars — traditional trading platform right margin

Implements the standard trading terminal viewport: data bars are left-aligned within the effective window, with N empty slots reserved on the right for trendline projection into future space.

**Design:**
- `RightMarginBars = 20` added to `WorkspaceState` (default, part of `TabSnapshot`).
- `effectiveWindow = ViewportLength - RightMarginBars` — the number of real data bars visible.
- The last data bar always lands at canvas slot `(ViewportLength - RightMarginBars - 1)`.
- Slots beyond that are empty future space — trendlines and drawings project naturally into them.

**`ViewportNavigationService` rewritten (all four methods):**
- `Navigate`: uses `effectiveWindow` for scroll-trigger and `maxStart` calculation.
- `Pan`: uses `effectiveWindow` for `maxStart`.
- `Zoom`: `maxLength = Data.Count + RightMarginBars` (allows zooming out to see all data plus margin); anchors to `lastDataBar = ViewportStartIndex + effectiveWindow - 1` so the right margin slot count stays constant during zoom.
- `ClampViewportToData`: no longer mutates `ViewportLength` — only clamps `ViewportStartIndex`. `ViewportLength` legitimately exceeds `Data.Count` by `RightMarginBars`.

**`WorkspaceStore` updated:** All `effectiveWindow` calculations in `UpdateData`, `JumpToLatestAction`, `ZoomAction` use `ViewportLength - RightMarginBars`. `SnapshotFromState` / `RestoreSnapshot` / `AddTab` all carry `RightMarginBars` through.

**Build: 0 errors, 0 warnings. Tests: 236/236 passing.**

---

## [2026-04-05] — Cipher C: Indicator Rename + Ehlers Cyber Cycle Math Revamp

### Renamed: "Cycle Cipher" → "Cipher C" everywhere
- `CycleCipherProvider.cs` → `CipherCProvider.cs`; class `CycleCipherProvider` → `CipherCProvider`
- Indicator `Name`: `"Cycle Cipher"` → `"Cipher C"`; `Code`: `CYCLE_CIPHER` → `CIPHER_C`; pane key: `Pane_CYCLE_CIPHER` → `Pane_CIPHER_C`
- DI registration in `ServiceCollectionExtensions.cs` updated accordingly
- All other indicators also de-prefixed: `"Accessible.CipherA"` → `"Cipher A"`, `"Accessible.CipherB"` → `"Cipher B"`, `"Accessible.CipherSR"` → `"Cipher SR"`
- `PLUGIN_AUTHORING.md` naming convention example updated

### Changed: Cipher C math — Ehlers Cyber Cycle bandpass filter (v2)
Replaced the EMA pre-smooth + stochastic foundation with a proper Ehlers Cyber Cycle bandpass filter.
The old math was a momentum oscillator masquerading as a cycle detector; the new math correctly isolates the dominant price cycle by rejecting the trend component.

**Old pipeline:** EMA(close, SmoothPeriod) → Stochastic(EMA, CyclePeriod) → Fisher Transform → EMA(signal, SignalPeriod)

**New pipeline:**
1. Ehlers 4-bar weighted smooth: `(P + 2P[1] + 2P[2] + P[3]) / 6` — minimal-lag fixed pre-smooth
2. Ehlers Cyber Cycle bandpass: `Cycle = a1²(S-2S[1]+S[2]) + 2a2·Cycle[1] - a2²·Cycle[2]` (alpha = 2/(CyclePeriod+1))
3. Post-filter EMA (SmoothPeriod; 1 = raw cycle)
4. Stochastic(smoothCycle, CyclePeriod) → Fisher Transform × 50, clamped ±100 → CycleSine
5. **HullMA**(CycleSine, SignalPeriod) → LeadSine (was EMA — lower lag)
6. Hull RSI for tier confirmation (unchanged)

All styling, colors, dot sizes, audio config, signal classification logic, and cloud fill are unchanged.

### Changed: `GetStabilityWindow` formula
- Old: `cyclePeriod * 3 + smoothPeriod + signalPeriod + 16` (default 52)
- New: `cyclePeriod * 4 + signalPeriod * 2 + 20` (default 66) — Ehlers bandpass needs more warmup

### Added: Cipher C unit tests (57 tests)
- `AccessibleTrader.Tests/CipherCProviderTests.cs` — metadata, component audio config, signal classification, GetDetailFact, GetComponentSpeech, stability window

**Build: 0 errors, 0 warnings. Tests: 235/235 passing.**

---

## [2026-04-01] — Indicator Recalculation Fix + Drawing Tool Restore

### Fixed: Indicator signals missing/gapped on resampled weekly charts
- `DataOrchestrationService`: added `DataStatus.LoadingHistorical` gate to `OnDataUpdated`. While the data pipeline is loading or resampling (e.g. 1W bars assembled from Bitstamp daily data), per-bar recalculation triggers are suppressed entirely. A `_pendingRecalcAfterLoad` flag is set instead.
- New `DataStatus.Ready` subscription fires exactly one `RecalculateAllAsync` when loading completes — at which point the full bar set is available, all warmup periods are satisfied, and every indicator (including Cipher A/B/SR) calculates in a single uncontested pass.
- Eliminates racing concurrent recalculations that previously wrote partial/stale NaN arrays into sparse signal components as bars trickled in over several seconds.
- The existing `_tickCts` cancellation pattern continues to protect against races in all other scenarios.

### Fixed: Drawing tool workflow restored to key-as-anchor-setter
- `CommandDispatcher`: drawing command cases now publish `AddDrawingEvent(typeName)` directly. The old `EnterCoordinateEntryAction` dispatch and entire `ConfirmCoordinateEntry` (Enter-based) case removed. `IsDrawingCommand` no longer includes `ConfirmCoordinateEntry`.
- `CancelDrawing` case simplified — just publishes `CancelDrawingEvent` (CE state cleanup removed).
- `DrawingInteractionManager`: `CoordinateEntryCompleteEvent` subscription and `HandleCoordinateEntryComplete` method removed. The existing `HandleAddDrawing` state machine now drives everything: first key press sets anchor 1, same key again sets anchor 2 (and for 3-anchor tools — FibExtension, RiskReward, Pitchfork — a third press completes).
- Added `FriendlyName(DrawingType)` helper — speech-friendly tool names ("Trend line", "Fibonacci retracement", "Andrews pitchfork", etc.).
- Improved all feedback messages: anchor set announcements include price, timestamp, and "press the shortcut again" hint; completion messages report "placed from X to Y"; cancel messages name the tool.
- Pressing a different drawing shortcut while one is in progress cancels the first and announces it before starting the new one.

**Build: 0 errors, 0 warnings. Tests: 146/146 passing.**

---

## [2026-04-01] — Phase 4 SRP Completion: Drawing Calculators, Detail Facts, Crossing Engine

### Added: `IDrawingCalculator` strategy pattern (`Sdk/Interfaces/IDrawingCalculator.cs`)
- Interface: `DrawingType DrawingType { get; }` and `Dictionary<string, double[]> Calculate(DrawingData, IReadOnlyList<Ohlcv>)`.
- 15 calculator classes in `Core/Services/Drawing/Calculators/`: `HorizontalLineCalculator`, `VerticalLineCalculator`, `TrendLineCalculator`, `ChannelCalculator`, `FibRetracementCalculator`, `TextLabelCalculator`, `FibExtensionCalculator`, `GannFanCalculator`, `RectangleCalculator`, `RiskRewardCalculator`, `AnchoredVwapCalculator`, `MeasureToolCalculator`, `GannBoxCalculator`, `AndrewsPitchforkCalculator`, `AngleFibCalculator`.
- `DrawingCalculatorHelper`: shared `FindIndex` and `CalculateLinearPoints` used across calculators.

### Changed: `DrawingService` rewritten as a registry/dispatcher
- Constructor takes `IEnumerable<IDrawingCalculator>` from DI; builds a `DrawingType → IDrawingCalculator` dictionary.
- `CalculateDrawingData` is a single `TryGetValue` lookup — no switch statement.
- New drawing tools can be added by creating a calculator class and registering it in `ServiceCollectionExtensions`.

### Added: `IDetailFactProvider` interface (`Sdk/Interfaces/IDetailFactProvider.cs`)
- `string? GetDetailFact(string code, ReadOnlySpan<Ohlcv>, IReadOnlyDictionary<string, double[]>, int, Dictionary<string, object>)`.
- Returns `null` to signal "no match", enabling a provider chain pattern.

### Added: `SkenderDetailFactProvider` (`Core/Services/Indicators/SkenderDetailFactProvider.cs`)
- Implements `IDetailFactProvider` — all 10 indicator speech-fact cases extracted from `SkenderIndicatorProvider`: RSI, Bollinger Bands, MACD, Moving Averages, Stochastic, VWAP, ATR, CCI, ADX, generic fallback.
- `SkenderIndicatorProvider.GetDetailFact` now delegates to this class.

### Added: `IndicatorCrossingEngine` (`Core/Services/Input/IndicatorCrossingEngine.cs`)
- Extracted from `CommandDispatcher`: all crossing and sparse-signal navigation logic.
- Public entry point: `HandleCrossJump(SystemCommand)`.
- `ScanSignCrossing` and `ScanThresholdCrossing` are `internal static` — still covered by `CrossingNavigationTests` via reflection.
- Registered as singleton in `ServiceCollectionExtensions.AddInputRouting`.

### Changed: `CommandDispatcher`
- Injects `IndicatorCrossingEngine`; `NavLeftJump`/`NavRightJump` delegate to `_crossingEngine.HandleCrossJump(command)`.
- All crossing enums, scan helpers, and Do*Jump methods removed (~600 lines).

### Changed: `CrossingNavigationTests`
- `DispatcherType` updated from `typeof(CommandDispatcher)` to `typeof(IndicatorCrossingEngine)`.

### Changed: `ServiceCollectionExtensions`
- `AddRenderingServices`: registers all 15 `IDrawingCalculator` implementations before `DrawingService`.
- `AddInputRouting`: registers `IndicatorCrossingEngine` singleton before `CommandDispatcher`.

**Build: 0 errors, 0 warnings. Tests: 146/146 passing.**

---

## [2026-03-31] — Legend Rendering + Hidden State TTS

### Added: Main-pane legend for overlay indicators (Cipher A, SR, Ichimoku, MA overlays)
- `ChartRenderer`: after rendering the main candle pane, overlay series (excluding CANDLES/PRICE/VOLUME/HEATMAP) are collected and passed to `RenderPaneLegend`. Cipher A and SR now display a visible color legend on the main chart.

### Fixed: Hidden component not announced during UP/DOWN arrow navigation
- `NavigationFeedbackManager`: component navigation speech prefix now checks `!IsVisible` before `IsMuted`. Hidden components announce "Hidden." when arrowed past, matching the existing "Muted." behavior.

**Build: 0 errors, 0 warnings. Tests: 146/146 passing.**

---

## [2026-03-31] — Navigation Speech Bug Fixes

### Fixed: Silent component navigation for marker components with no signal
- `SpeechFormatter.FormatTemplateValue`: when `SignalSpeechTemplate` is set and the component value is NaN (no signal on this bar), now returns the component's DisplayName instead of empty string. Previously, navigating to "Buy Signal" on a bar without a signal produced complete silence — the user had to press multiple times blindly.

### Fixed: Y-navigation passes silently through hidden components
- `PointNavigationStrategy.NavigateY`: now skips components where `IsVisible == false`. Previously, hidden-by-default components (e.g. VWAP~ in Cipher B) consumed a down-arrow press with no feedback, requiring multiple presses to advance.

### Fixed: Cluster audio ticks always centered (pan=0)
- `NavigationSonifier.FireClusterTicksAsync`: cluster ticks now use the same viewport-position reactive pan as the main navigation voice. Previously all cluster signals sounded from center regardless of where the bar was in the viewport.

### Fixed: "Also: signal" speech fires when focused inside indicator series
- `NavigationFeedbackManager`: additional signal speech ("Also: buy signal at...") now only fires when the user is focused on the candle/price series. When navigating inside Cipher A, B, or SR, the user is already in that indicator's context — cross-indicator signal announcements were confusing and unexpected.

### Fixed: NaN marker component fires click artifact in NavigationSonifier
- `NavigationSonifier.SyncNavigationSlots`: marker-type components (Dot/Diamond/Cross etc.) with NaN value at the current bar no longer trigger a voice event. Previously, landing on a signal component with no data on the current bar could produce an unintended click or plunk sound.

**Build: 0 errors, 0 warnings. Tests: 146/146 passing.**

---

## [2026-03-31] — Phase L: Test Coverage Expansion

### Added: 9 new test files, targeting Phases B–K additions

- **`SoundPatchRegistryTests`** (7 tests): built-in patch registration, custom patch registration/replacement, detuned/gradient patch properties.
- **`PlaybackLayerTests`** (4 tests): volume multiplier values, default layer, factory propagation, clone preservation.
- **`DecayMsTests`** (4 tests): default null, factory propagation, clone preservation with/without value.
- **`CipherAMetadataTests`** (13 tests): all 8 components verified for key audio metadata fields (patch ID, frequency, decay, layer, gradient speech).
- **`CipherBMetadataTests`** (10 tests): Triple Confluence dual-tone bell, crossover frequencies, divergence patches, Background-layer anchors/oscillators.
- **`CipherSrMetadataTests`** (7 tests): crystal bell patch, zone line flag propagation through IndicatorModelFactory.
- **`IchimokuProviderTests`** (12 tests): component count, cloud fill structure, Tenkan/Chikou/Senkou calculations, GetDetailFact speech, stability window.
- **`CloudSonificationTests`** (8 tests): backward compat (null Sonification), EMA Fill/Ichimoku/CipherB cloud frequencies.
- **`CrossingNavigationTests`** (3 tests): zero-line crossing scan, threshold crossing scan, no-crossing returns -1.

**Build: 0 errors, 0 warnings. Tests: 146/146 passing.**

---

## [2026-03-31] — Phase K: Ichimoku Kinko Hyo Indicator

### Added: `IchimokuProvider` (`Core/Services/Indicators/IchimokuProvider.cs`)
- Code: `ICHIMOKU`, Category: Overlays, Pane: Main.
- **5 components**: Tenkan-sen (#E91E63), Kijun-sen (#2196F3), Senkou Span A (#4CAF50), Senkou Span B (#F44336), Chikou Span (#9C27B0).
- **Kumo cloud fill**: Senkou Span A vs B — bullish (#4CAF5060) / bearish (#F4433660) with cloud sonification: 520/180 Hz, 220ms, max volume 0.80. Distinct frequencies from EMA Fill and WT Fill.
- **Displacement**: Senkou spans plotted 26 bars ahead; Chikou plotted 26 bars behind. NaN used for out-of-range indices.
- **Parameters**: TenkanPeriod (9), KijunPeriod (26), SenkouBPeriod (52), Displacement (26).
- **GetDetailFact**: contextual speech — price relative to Kijun, TK cross status, price position relative to Kumo, cloud polarity.
- Registered in `ServiceCollectionExtensions.AddIndicatorPipeline`.
- `PaneAssignmentService`: ICHIMOKU → Main pane, Overlays category.

**Build: 0 errors, 0 warnings. Tests: 69/69 passing.**

---

## [2026-03-31] — Phase J: Ctrl+Left/Right Context-Aware Crossing Navigation

### Changed: `CommandDispatcher` — Ctrl+Left/Right is now context-aware
- Crossing type is determined from the focused series type, not hardcoded to trendlines.
- **Price/Candles series (or no focus)**: unchanged — scans for nearest trendline crossing.
- **Zero-line oscillators** (MACD, Momentum, MF Wave, ZeroArea): scans for the series crossing zero.
- **Threshold oscillators** (RSI, MFI, Stoch, CCI): scans for OB/OS level crossings (entering/leaving the zone). Speaks "Entering overbought", "Leaving oversold" etc.
- **Moving average overlays** (EMA, SMA, WMA, DEMA, TEMA, HULL, ALMA, VWMA, Spider Lines): scans for price (close) crossing the focused MA line. Speaks "Price crosses above/below MA".
- **Band/channel indicators** (Bollinger %B, PERCENTB): scans for price crossing Upper (1.0), Midband (0.5), or Lower (0.0) band boundary.
- **Sparse marker series** (Dot/Diamond/Cross/Arrow/TriangleUp/TriangleDown/Square/ZeroDot/GradientDot): unchanged — jumps to nearest non-NaN signal bar.
- When no crossing is found in the visible range: speaks "No crossing in view".

### Added: `CrossingType` enum (private, inside `CommandDispatcher`)
- Values: Trendline, ZeroLine, ThresholdLevel, MovingAverageCross, BandLine.

### Added: Helper methods in `CommandDispatcher`
- `GetCrossingStrategy(state, focusedSeries)` — determines crossing type from indicator code and component display types.
- `DoSparseSignalJump` — non-NaN jump for marker components (extracted from old Case 2 path).
- `DoZeroLineCrossJump` — scans primary component data for sign changes crossing zero.
- `DoThresholdCrossJump` — scans for OB/OS level entries/exits; resolves levels from series `Levels` collection or `IndicatorReferenceLevels` fallback.
- `DoMACrossJump` — scans for price (from `state.Data` OHLCV) crossing the MA component line.
- `DoBandLineCrossJump` — scans against all three Bollinger %B boundaries; picks nearest crossing.
- `ScanSignCrossing(data, current, scanRight, threshold)` — generic sign-change scanner (static).
- `ScanThresholdCrossing(data, current, scanRight, level, aboveIsZone, out message)` — threshold scanner with entering/leaving speech.
- `GetNamedLevelValue(series, nameFragments[])` — extracts level value by name fragment from series.Levels.
- `GetFirstValidValue(data)` — returns first non-NaN value from an array.
- `FormatTimestamp(state, dataIndex)` — formats `state.Data[i].Date` as short time string.

**Build: 0 errors, 0 warnings. Tests: 69/69 passing.**

---

## [2026-03-31] — Phase I: Drawing Tools — Coordinate Entry Mode

### Added: Coordinate Entry Mode for keyboard-first drawing placement
- Activating any drawing shortcut (Ctrl+Shift+T/H/V/C/F/L/E/R/G/P/W/M/B/A/J) enters Coordinate Entry mode.
- TTS announces "Coordinate entry mode. Navigate to first anchor point and press Enter."
- Arrow keys move the cursor normally; TTS announces current price and timestamp on each step.
- **Enter**: sets anchor. First Enter sets anchor 1 with speech feedback. Second Enter completes the drawing and exits CE mode.
- **Escape**: cancels CE mode with speech "Coordinate entry cancelled."
- When anchor 1 is set, navigation speech includes price change from anchor 1 ("Change from anchor: +125").

### Added: WorkspaceState CE fields
- `IsCoordinateEntryMode`, `PendingDrawingTool`, `CoordinateEntryAnchorCount`, `CoordinateEntryAnchor1Index`.
- New actions: `EnterCoordinateEntryAction`, `SetCoordinateEntryAnchorAction`, `ExitCoordinateEntryAction`.

### Added: SystemCommand.ConfirmCoordinateEntry
- Bound to Enter/Return keys. Only acts when `IsCoordinateEntryMode == true`.

### Added: CoordinateEntryCompleteEvent
- Published by `CommandDispatcher` when both anchors are confirmed. `DrawingInteractionManager` subscribes and calls `HandleDrawingStep` for each anchor to complete the drawing.

### Changed: DrawingInteractionManager
- Subscribes to `CoordinateEntryCompleteEvent` and completes drawings from keyboard-placed anchors.

### Changed: NavigationFeedbackManager
- In CE mode: always speaks price + timestamp regardless of speech settings.
- After anchor 1 is set: appends "Change from anchor: ±N" to each navigation step.

**Build: 0 errors, 0 warnings. Tests: 69/69 passing.**

---

## [2026-03-31] — Phase H: Cloud Sonification Architecture

### Added: `CloudSonificationConfig` record (`Sdk/Models/CloudFillConfig.cs`)
- Declares audio properties for a cloud fill during Chart-scope playback.
- Fields: `BullishFrequency`, `BearishFrequency`, `SoundPatchId`, `DecayMs`, `MaxVolume`.
- `CloudFillConfig.Sonification` (nullable) — null = no audio (existing behavior for all current clouds until this phase).

### Added: Cloud voice pass in `AudioSequencer.StartMultiSeriesPlaybackAsync`
- After component voices fire for each bar, iterates all active series' cloud fills.
- Cloud thickness (|upper - lower|) normalized against viewport maximum → voice volume.
- Direction (upper >= lower = bullish) → selects BullishFrequency or BearishFrequency.
- Cloud voices use slots 64–79 (CloudSlotOffset), separate from component slots (32–63).
- Bars where normalized thickness < 0.05 produce no sound (silence during consolidation).
- Cloud voices do NOT fire in `StartPlaybackAsync` (Series/Component scope).

### Changed: `EmaFillProvider` cloud fill declares sonification
- Bullish: 440 Hz, Bearish: 220 Hz, 200ms decay, max volume 0.75.
- Thick EMA cloud (strong trend divergence) = loud tone. Thin (compression) = near-silent.

### Changed: `CipherBProvider` WT Fill declares sonification
- Bullish: 480 Hz, Bearish: 200 Hz, 180ms decay, max volume 0.70.
- Distinct from EMA Fill frequency so both can play simultaneously without confusion.

**Build: 0 errors, 0 warnings. Tests: 69/69 passing.**

---

## [2026-03-31] — Phase G: Contextual Component Speech

### Added: `SignalSpeechTemplate` on `ComponentConfig`, `DefaultSignalSpeechTemplate` on `IndicatorComponentMetadata`
- When set and the component has a non-NaN value at the current bar: used instead of the generic `{name}, {type}, {value}` template.
- When set and value is NaN: returns empty string (no speech for absent signals).
- Supports `{price}` token (formats value as integer price) and `{name}` token.

### Changed: `CipherAProvider` — signal speech templates
- Buy/Sell: "Buy/Sell signal at {price}".
- Bullish/Bearish Divergence diamonds: "Bullish/Bearish divergence detected".
- Blood Diamond: "Overbought bearish divergence, high confidence".
- Manipulation: "Potential smart money accumulation".
- Exhaustion: "Potential distribution, exhaustion signal".

### Changed: `CipherBProvider` — signal speech templates
- Oversold/Overbought crossovers, Triple Confluence, divergence dots: contextual descriptive templates.
- MF Signal Large/Small: "Large money flow signal" / "Money flow signal".

### Changed: `CipherSrProvider` — signal speech templates
- Resistance/Support pivot dots: "Resistance/Support pivot at {price}".

### Changed: `NavigationFeedbackManager`
- Additional signal scan after primary component speech (Component context only): announces secondary marker signals on the same bar with "Also: ..." prefix, in same tier order as cluster audio ticks (Phase F).
- SR zone proximity speech: "Near resistance/support at {level:F0}" spoken (non-interrupting) when zone hum fires.

### Changed: `IndicatorModelFactory`
- `CreateComponentConfigFromMeta` and `CloneComponent` propagate `SignalSpeechTemplate` from metadata.

**Build: 0 errors, 0 warnings. Tests: 69/69 passing.**

---

## [2026-03-31] — Phase F: Cluster/Shapes-as-Ticks Navigation

### Added: `INavigationSonifier.FireClusterTicksAsync`
- On X-navigation (left/right arrow), scans all active series for marker-type components (Dot, ZeroDot, Arrow, Diamond, TriangleUp, TriangleDown, Square, Cross) with non-NaN values at the current bar.
- Fires each as a distinct audio tick on slots 3–7 with 100ms gaps, in significance order.
- Significance tiers: 1 = SR/structural, 2 = divergences, 3 = crossover/signal events, 4 = other.
- Within each tier: positive (bullish) before negative (bearish).
- The primary focused component (slot 0) is excluded from cluster re-firing.
- Zone line components (IsZoneLine=true) are excluded (handled by PlayZoneProximity).
- Fire-and-forget: does not block main navigation response.

### Changed: `SonificationManager`
- After `SyncNavigationSlots` on X-navigation events, calls `FireClusterTicksAsync` when not in playback mode.

**Build: 0 errors, 0 warnings. Tests: 69/69 passing.**

---

## [2026-03-31] — Phase E: Cipher SR Sonification Design

### Changed: `CipherSrProvider` sonification metadata
- **Resistance dot**: `crystal_bell`, 700 Hz, 220ms decay, Foreground layer.
- **Resistance Zone step line**: sine, 650 Hz, Background layer, IsZoneLine=true.
- **Support dot**: `crystal_bell`, 330 Hz, 220ms decay, Foreground layer.
- **Support Zone step line**: sine, 300 Hz, Background layer, IsZoneLine=true.

### Added: `IsZoneLine` on `ComponentConfig` and `DefaultIsZoneLine` on `IndicatorComponentMetadata`
- When true, NavigationFeedbackManager checks zone proximity on each navigation step.
- If the current candle's price range overlaps the zone level (within 0.5% tolerance), a quiet 100ms proximity tone plays on audio slot 2.
- Resistance zones play at the component's BaseFrequency (high end, ceiling character).
- Support zones play at the component's BaseFrequency (low end, floor character).

### Added: `INavigationSonifier.PlayZoneProximity(float frequency, bool isResistance)`
- Fires a quiet (0.25f volume) 100ms sine tone on slot 2, separate from main navigation voice.

**Build: 0 errors, 0 warnings. Tests: 69/69 passing.**

---

## [2026-03-31] — Phase D: Cipher B Sonification Redesign

### Changed: `CipherBProvider` sonification metadata updated throughout
- **Anchor waves**: Background layer (35% mix volume), triangle/sine waveforms.
- **Trigger Wave**: Midground layer, triangle, higher freq multiplier (1.3×) for "ahead" feel.
- **WT1**: Midground layer, triangle above zero / sawtooth below zero (cutter character).
- **WT2**: Midground layer, smooth sine throughout (channel/envelope character).
- **Money Flow Wave**: Midground, sine with 0.08 noise texture preserved.
- **Money Flow dot**: `sine_bell`, 150ms decay, Direction pitch (600/250 Hz).
- **MF Signal Large**: `sine_bell`, 350ms decay, Direction pitch, Foreground layer.
- **MF Signal Small**: `sine_bell`, 160ms decay, Direction pitch, Foreground layer.
- **RSI~/Stoch/VWAP~**: Background layer, triangle waveform (contextual, subdued in mix).
- **Oversold Crossover**: `sine_bell`, 840 Hz, 350ms decay, Foreground.
- **Overbought Crossover**: `sine_bell`, 210 Hz, 350ms decay, Foreground.
- **Triple Confluence Buy**: `dual_tone_bell` (440 Hz + 660 Hz simultaneous chord), 500ms decay, Foreground.
- **Bullish/Bearish Divergence dots**: `triangle_bell`, 620/310 Hz, 230ms decay, Foreground.
- **Hidden Bull/Bear Continuation dots**: `triangle_bell`, 520/360 Hz, 180ms decay, Foreground.

### Added: `dual_tone_bell` patch in `SoundPatchRegistry`
- Two simultaneous sine voices 220 Hz apart (no stagger), 500ms decay. Used for Triple Confluence to produce a golden chord character distinct from Manipulation's staggered metallic pair.

### Added: `DefaultAboveWaveform`, `DefaultBelowWaveform`, `DefaultBullishFrequency`, `DefaultBearishFrequency`, `DefaultFreqMultiplier` on `IndicatorComponentMetadata`
- Allows providers to declare zero-crossing waveform character and Direction-pitch frequencies directly in metadata.
- Applied in `IndicatorModelFactory.CreateComponentConfigFromMeta`.

**Build: 0 errors, 0 warnings. Tests: 69/69 passing.**

---

## [2026-03-31] — Phase C: Cipher A Self-Describing Metadata + Sonification Redesign

### Changed: `IndicatorComponentMetadata` gains `DefaultSoundPatchId` and `UsesGradientSpeech`
- `DefaultSoundPatchId` (nullable string): provider-declared SoundPatch to assign on component creation. Applied in `IndicatorModelFactory.CreateComponentConfigFromMeta`.
- `UsesGradientSpeech` (bool): when true, navigation speech produces qualitative momentum language ("strong bullish momentum", "neutral momentum", etc.) instead of raw value. Applied in speech formatter.

### Changed: `ComponentConfig` gains `UsesGradientSpeech`
- Propagated from `IndicatorComponentMetadata.UsesGradientSpeech` by `IndicatorModelFactory`.
- `Clone()` and `CloneComponent` copy the field correctly.
- Speech formatter substitutes qualitative range-aware description for gradient components during navigation.

### Changed: `CipherAProvider` fully self-describing
- All 8 components now declare `Default*` metadata fields (colors, thickness, waveform, envelope, DecayMs, base frequency, PlaybackLayer, SoundPatchId).
- WT Momentum gradient dot: `SoundPatchId = "gradient_blend"`, 80ms decay, 440 Hz, Background layer, `UsesGradientSpeech = true`.
- Buy Signal: `sine_bell`, 880 Hz, 380ms decay, Foreground layer.
- Sell Signal: `sine_bell`, 220 Hz, 380ms decay, Foreground layer.
- Bullish Divergence: `triangle_bell`, 660 Hz, 280ms decay, Foreground layer.
- Bearish Divergence: `triangle_bell`, 330 Hz, 280ms decay, Foreground layer.
- Overbought Bearish Divergence ("Blood Diamond"): `triangle_bell`, 165 Hz, 500ms decay, Foreground layer.
- Manipulation: `detuned_pair_bell`, 550 Hz, 320ms decay, Foreground layer.
- Exhaustion: `detuned_pair_bell`, 250 Hz, 320ms decay, Foreground layer.

### Changed: `SpeechFormatter` gains CIPHER_A templates + gradient speech logic
- All 8 Cipher A components registered with descriptive speech templates.
- WT Momentum template uses `{gradient_speech}` token; formatter reads companion `_color` array (raw WT1 oscillator value) and maps it to qualitative descriptions: "strong bullish momentum" (>60), "moderate bullish momentum" (>20), "neutral momentum" (±20), "moderate bearish momentum" (<-20), "strong bearish momentum" (<-60), with numeric value appended.

**Build: 0 errors, 0 warnings. Tests: 69/69 passing.**

---

## [2026-03-31] — Phase B: Audio Engine Bell Synthesis Foundation

### Added: `DecayMs` field on `ComponentConfig` and `IndicatorComponentMetadata`
- `ComponentConfig.DecayMs` (nullable int): configurable bell decay in milliseconds. Null = use existing envelope defaults. Overrides patch DefaultDecayMs when set.
- `IndicatorComponentMetadata.DefaultDecayMs` (nullable int): provider-declared default. Applied as Layer 1 in 3-layer merge.
- `IndicatorModelFactory.CreateComponentConfigFromMeta` and `CloneComponent` updated.

### Added: `PlaybackLayer` enum and field on `ComponentConfig`
- `PlaybackLayer` enum: Background (60%), Midground (80%), Foreground (100%).
- `ComponentConfig.PlaybackLayer` (default Midground): controls voice volume scaling during multi-series playback.
- `IndicatorComponentMetadata.DefaultPlaybackLayer` (nullable): provider-declared default.
- `AudioSequencer.StartMultiSeriesPlaybackAsync` and `StartPlaybackAsync` apply layer scaling.

### Added: `SoundPatchRegistry` (`Core/Services/Audio/SoundPatchRegistry.cs`)
- `ISoundPatchRegistry` + `SoundPatchRegistry` singleton. Built-in patches: `sine_bell`, `triangle_bell`, `crystal_bell`, `detuned_pair_bell`, `gradient_blend`.
- `sine_bell`: clean sine with 25% 2nd-harmonic blend, 300ms default decay — for crossover signal dots.
- `triangle_bell`: hollow triangle fundamental, 250ms default decay — for divergence markers.
- `crystal_bell`: triangle + 3rd harmonic (15%), 200ms default decay — for SR boundary dots.
- `detuned_pair_bell`: triangle pair, 100 Hz apart, 40ms stagger, 320ms decay — for Manipulation/Exhaustion.
- `gradient_blend`: timbre interpolates from sine (bullish) through triangle (neutral) to sawtooth (bearish), 80ms decay — for Cipher A momentum gradient dots.
- Registered as singleton in DI via `ServiceCollectionExtensions.AddAudioServices`.

### Added: `PatchId` field on `AudioPoint`
- `AudioPoint` record extended: `string? PatchId = null` as 9th positional parameter (backward-compatible).

### Changed: `DefaultSonificationStrategy` resolves `ISoundPatchRegistry`
- Added `ISoundPatchRegistry` as constructor dependency.
- When `comp.SoundPatchId` is set and registry has the patch, populates `AudioPoint.PatchId`.

### Changed: `AudioSequencer` bell patch handling
- Added `ISoundPatchRegistry` as constructor dependency.
- `LayerVolume()` helper: Background=0.60, Midground=0.80, Foreground=1.00.
- `ResolvePingDuration()` helper: comp.DecayMs > patch.DefaultDecayMs > bar-proportional default.
- Ping-envelope voices with a PatchId use `ResolvePingDuration` for voice duration.
- `detuned_pair_bell` fires a second voice at `DetunedOffsetMs` ms delay on next available slot.
- `PlaybackLayer` volume scaling applied in both `StartPlaybackAsync` and `StartMultiSeriesPlaybackAsync`.

### Changed: `NavigationSonifier` resolves `ISoundPatchRegistry`
- Added `ISoundPatchRegistry` as constructor dependency.
- `ResolveNavPingDuration()` helper applies patch-aware decay for navigation Ping voices.
- Detuned pair bell fires second voice on Slot 1 at `DetunedOffsetMs` ms delay.

**Build: 0 errors, 0 warnings. Tests: 69/69 passing.**

---

## [2026-03-30] — Indicator Sub-Panes, Anchor/Trigger Waves, Cipher SR

### Added: General Indicator Sub-Pane Architecture
- `IndicatorComponentMetadata` gains `SubPaneName?` and `SubPaneHeightRatio?`. Any provider can declare components in a named sub-pane strip by setting these fields; `null` means main area (existing behavior, all current indicators unaffected).
- `ComponentConfig` carries `SubPaneName` and `SubPaneHeightRatio` as plain properties (non-observable); propagated by `IndicatorModelFactory.CreateComponentConfigFromMeta` and `CloneComponent`.
- `RenderContext` gains optional `SubPaneFilter` positional parameter (default `null`). `null` = main-area pass (skip sub-pane components); string = sub-pane pass (render only matching components).
- `ChartRenderer.RenderPane` is now multi-pass: detects sub-panes from component metadata, allocates a main area rect (top, ≥30% of pane height) + per-sub-pane strip rects (bottom, each clamped to [0.05, 0.40]), renders each strip with its own clip + range + `SubPaneFilter`. A subtle separator line appears at the top of each sub-pane strip. Indicator pane calls pass `allPaneRanges` so sub-pane range look-up works via composite keys.
- `ViewportRangeCalculator` rewritten: removes early-exit bug where only the first series per pane contributed to the range calculation. Now accumulates min/max across ALL series per pane. Per-component range key: `"PaneName/SubPaneName"` for sub-pane components, plain pane name for main-area components. Sub-panes get a 15% buffer (vs 10% for main panes). Cipher B ±100 floor applied only to `"Pane_CIPHER_B"` main area, not its sub-panes.
- `DataLayer` sub-pane filter gate: `ctx.SubPaneFilter == null` skips components with a SubPaneName; non-null filter skips non-matching components. Cloud fills and reference levels are main-area-only.

### Added: Cipher B — Money Flow Sub-Pane, Anchor Waves, Trigger Wave
- `Money Flow Wave` and `Money Flow` dot now declare `SubPaneName = "MF", SubPaneHeightRatio = 0.22f` — MF renders in its own 22% strip at the bottom of the Cipher B pane, matching the real Market Cipher B layout.
- **Anchor Waves** (WT1 Anchor, WT2 Anchor): same Wave Trend algorithm re-run at `AnchorMultiplier × WT periods`. Blue-gray (#78909C) and deep ocean blue (#01579B) lines — thicker and slower, rendering *behind* the main WT waves (listed first in component metadata for correct z-order). Default `AnchorMultiplier` updated **3 → 5** for better macro-wave separation.
- **Trigger Wave** (`Trigger Wave`): `WT1 − EMA(WT1, TriggerPeriod)` — a fast momentum derivative that leads WT1/WT2 crossovers by 1–2 bars. Thin bright yellow (#FFEB3B) line, `TriggerPeriod` parameter (default 4).
- `GetStabilityWindow` updated to account for anchor multiplier-scaled periods.

### Added: Accessible Cipher SR (`Core/Services/Indicators/CipherSrProvider.cs`)
- New `IIndicatorProvider` (code `CIPHER_SR`, category `Overlays`, pane `Main`).
- Four components on the price chart: **Resistance** (purple dot at pivot high, 660 Hz Ping), **Resistance Zone** (dashed purple step line), **Support** (gold dot at pivot low, 330 Hz Ping), **Support Zone** (dashed gold step line).
- Pivot confirmed when `high[i]` is the strict maximum over `[i−PivotBars .. i+PivotBars]` AND volume at `i ≥ VolumeMultiplier × rolling-average(VolumeLookback bars)`.
- Zone lines carry the last confirmed level forward as a horizontal step pattern.
- Parameters: `PivotBars` (default 5), `VolumeLookback` (default 20), `VolumeMultiplier` (default 1.5).
- Registered in `ServiceCollectionExtensions.AddIndicatorPipeline`.

---

## [2026-03-30] — Sonification + Navigation + Preferences Improvements

### Fixed: Ctrl+Left/Right Sparse Navigation for All Marker Types (`Core/Services/Input/CommandDispatcher.cs`)
- Expanded the sparse-navigation check from `DisplayType == Dot` to all marker display types: `Dot`, `ZeroDot`, `Arrow`, `Diamond`, `TriangleUp`, `TriangleDown`, `Square`, `Cross`.
- Navigation logic (scan for nearest non-NaN bar) was already correct; only the gating condition was too narrow.
- Future providers using the new shape vocabulary automatically get Ctrl+Left/Right navigation without code changes.

### Fixed: Workspace Override No Longer Silences New Audio Defaults (`Core/Services/IndicatorModelFactory.cs`)
- `CreateSeriesFromMetadata` now uses a **3-layer merge** instead of all-or-nothing workspace replacement:
  - **Layer 1 (base):** Fresh configs from provider metadata — always applied, ensures new defaults (WT colors, waveforms) take effect.
  - **Layer 2 (workspace state):** Visibility, mute, volume, FreqMultiplier only — no colors or audio properties.
  - **Layer 3 (user preferences):** Full appearance + sonification settings saved via Properties dialog → "Save as Defaults".
- WT1/WT2 colors (`#D0D0D0`, `#0090C8`) and waveforms (triangle/sine) now load correctly even from old workspace saves.

### Fixed: Oscillator Above/Below Zero Waveform Switching (`Core/Services/IndicatorModelFactory.cs`)
- `CreateComponentConfigFromMeta` now sets `ReferenceLevel = 0.0` for `Oscillator` and `ZeroArea` display types when StylingService returns null.
- Enables `DefaultSonificationStrategy.CreateAudioPoint` waveform selection: triangle above zero, sine below zero.

### Added: Dot/Arrow Ping Profile in SonificationProfileProvider (`Core/Services/Audio/SonificationProfileProvider.cs`)
- Added explicit `Dot` and `Arrow` cases → Ping envelope, `PitchMapping.Direction`, 660 Hz (bullish) / 220 Hz (bearish).
- Previously fell through to the default Sustain line profile — earcons fired as long sustain tones instead of transient pings.
- ZeroArea: updated to `AboveWaveform = "triangle"` (was "sine") to match the oscillator rule.

### Added: Dynamic OB/OS Noise in Playback (`Core/Services/Audio/ISonificationStrategy.cs`)
- `DefaultSonificationStrategy.CreateAudioPoint` now computes threshold-based noise (0.20f) for oscillator/ZeroArea/histogram/line components whose series has labelled "Overbought"/"Oversold" Level siblings.
- Noise is included in `AudioPoint.NoiseAmount` and passed through `AudioSequencer` → `SetVoice` — playback now matches navigation's rough-texture-in-danger-zone behaviour.
- `AudioPoint` record extended: added `float NoiseAmount = 0f` as 8th positional parameter.
- `AudioSequencer` both loops updated to pass `audioPt.TriggerClick` and `audioPt.NoiseAmount` to `SetVoice` (was hardcoded `false, 0f`).

### Added: Indicator Preferences Service (`Core/Services/IndicatorPreferencesService.cs`)
- New `IIndicatorPreferencesService` + `IndicatorPreferencesService` backed by JSON files at `%LOCALAPPDATA%\AccessibleTrader\IndicatorPrefs\{CODE}.json`.
- `ComponentPreference` model: nullable per-field (ColorHex, ColorHexSecondary, Thickness, DashStyle, Waveform, EnvelopeType, Volume, FreqMultiplier, BaseFrequency, NoiseAmount, IsVisible).
- Registered as singleton in `ServiceCollectionExtensions.cs`.

### Added: "Save as Defaults" in PropertiesModal (`BlazorClient/Components/PropertiesModal.razor`)
- New button in modal footer (visible for non-drawing series).
- Captures all component appearance + sonification fields from the current (edited) state and persists via `IIndicatorPreferencesService`.
- Preferences are applied as Layer 3 on next indicator add — changes persist across workspace reloads and new sessions.

### Changed: Bullish Candle Default Color (`Core/Services/StylingService.cs`)
- `GetDefaultColor` for `ComponentRole.PriceAction` changed from `"#FFFFFF"` to `"#26A69A"` (industry-standard bullish green).

**Build: 0 errors, 0 warnings. Tests: 69/69 passing.**

---

## [2026-03-30] — Phase 10-G: New Marker Shapes + Self-Describing Indicator Metadata

### Added: Five New `ComponentDisplayType` Shape Values (`Sdk/Models/ChartSeries.cs`)
- `TriangleUp` — fixed upward-pointing triangle, direction independent of value sign.
- `TriangleDown` — fixed downward-pointing triangle, same.
- `Diamond` — rotated 45° square; visually distinct from `Dot`; ideal for divergence markers.
- `Square` — axis-aligned filled square; useful for POC/profile discrete event flags.
- `Cross` — X-shaped cross marker; useful for invalidation/alert flags.

### Added: Render Methods for New Shapes (`Core/Services/Rendering/StandardRenderers.cs`)
- `RenderTriangleUp`, `RenderTriangleDown` — filled equilateral triangles at value Y.
- `RenderDiamond` — rotated square path, size from `comp.Thickness * density`.
- `RenderSquare` — axis-aligned filled rect, same size convention.
- `RenderCross` — two diagonal stroked lines, arm length from `comp.Thickness * density`.
- All five respect `comp.ColorRules` per-bar color overrides (via `ResolveBarColor`).

### Added: Dispatch Cases in DataLayer (`Core/Services/Rendering/DataLayer.cs`)
- `case TriangleUp/Down/Diamond/Square/Cross` routed to the corresponding new renderer.

### Added: Sonification Profile for Marker Shapes (`Core/Services/Audio/SonificationProfileProvider.cs`)
- All five new shapes → sine/Ping envelope, `PitchMapping.Direction` (440 Hz up / 220 Hz down), `AmplitudeMapping.None`. Markers produce a transient click rather than a continuous tone.

### Changed: `IndicatorComponentMetadata` Extended with Self-Describing Hints (`Sdk/Models/IndicatorMetadata.cs`)
- Added optional visual fields: `DefaultColorHex`, `DefaultColorHexSecondary`, `DefaultThickness`, `ColorBaseline`, `DefaultDashStyle`, `DefaultColorSource`.
- Added optional audio fields: `DefaultWaveform`, `DefaultEnvelopeType`, `DefaultNoiseAmount`, `DefaultAmplitudeMapping`, `DefaultPitchMapping`, `DefaultBaseFrequency`.
- All fields are nullable — `null` = use global role/type-based StylingService default (zero overhead for existing providers).

### Changed: `IndicatorModelFactory` Uses Metadata Hints First (`Core/Services/IndicatorModelFactory.cs`)
- New private `CreateComponentConfigFromMeta(code, IndicatorComponentMetadata)` method: applies metadata `Default*` fields directly to `ComponentConfig`, falling through to `IStylingService` only for unset fields.
- `CreateSeriesFromMetadata` now calls `CreateComponentConfigFromMeta` instead of the public `CreateComponentConfig(code, name)` path — providers are fully self-describing, StylingService is a fallback only.
- Fixed `CloneComponent`: now copies `DashStyle`, `NoiseAmount`, `SoundPatchId`, `IsVisible`, `ColorRules` (previously missing, would silently reset these fields on series clone).

### Changed: `CipherBProvider` Fully Self-Describing (`Core/Services/Indicators/CipherBProvider.cs`)
- All color, thickness, and audio envelope hints moved into component metadata.
- Signal dot components (BullDiv, BearDiv, HiddenBull, HiddenBear) remain `ComponentDisplayType.Dot` — `CommandDispatcher.HandleTrendlineCrossJump` checks `DisplayType == Dot` for Ctrl+Left/Right sparse navigation; changing the type breaks navigation and earcon triggers.
- Money Flow Wave gains `DefaultNoiseAmount = 0.08f` for a textured, flowing sonic character.

### Changed: `SpiderLinesProvider` Self-Describing (`Core/Services/Indicators/SpiderLinesProvider.cs`)
- Colors moved from static `GetComponentColor()` into each component's `DefaultColorHex`.
- `GetComponentColor` is now private (was public static, consumed only by StylingService which no longer needs it).

### Changed: `EmaFillProvider` Self-Describing (`Core/Services/Indicators/EmaFillProvider.cs`)
- Fast EMA `#2196F3` and Slow EMA `#FF9800` moved into `DefaultColorHex` on each component.

### Changed: `SkenderIndicatorProvider` Has Its Own Display-Type Override Table (`Core/Services/Indicators/SkenderIndicatorProvider.cs`)
- Added static `_codeDisplayTypeOverrides` (code → DisplayType) for RSI, Stoch, StochRsi, UltOsc, WilliamsR, CCI (→ Oscillator) and MFI, ChaikinOsc, CMF (→ Histogram).
- Added static `_componentDisplayTypeOverrides` (code → component → DisplayType) for MACD Histogram component.
- Added `ColorBaseline = 50.0` for MFI in per-component discovery (replaces StylingService hardcode).
- `InitializeMetadata` checks these tables before calling `StylingService.GetDisplayType` — providers are now the authority on display types.

### Changed: `StylingService` Is Now Purely Role/Type-Based (`Core/Services/StylingService.cs`)
- Removed per-indicator blocks from `GetDefaultColor` (CIPHER_B, SPIDER_LINES, EMA_FILL).
- Removed per-indicator block from `GetSecondaryColor` (CIPHER_B).
- Removed per-indicator blocks from `GetDisplayType` (CIPHER_B, MFI, Chaikin OSC variants, RSI/Stoch/etc.) — now only delegates to `_roleMapper`.
- Removed per-indicator block from `GetColorBaseline` (MFI) — always returns 0.0 as fallback.
- Removed dead `GetThickness(indicatorCode, componentName, displayType)` non-interface method (was only used internally by CIPHER_B block).
- Updated `GetDefaultThickness` to return 4.0f for Diamond, TriangleUp, TriangleDown, Square and 3.0f for Cross (new shapes).
- Removed `using AccessibleTrader.Core.Services.Indicators` import (SpiderLinesProvider no longer referenced).

### Fixed: `SpeechFormatter.FriendlyTypeName` for New Shape Types (`Core/Services/Accessibility/SpeechFormatter.cs`)
- Added explicit cases for `TriangleUp` → "triangle up", `TriangleDown` → "triangle down", `Diamond` → "diamond", `Square` → "square", `Cross` → "cross".
- Without this, TTS would read `dt.ToString().ToLower()` → "triangleup", "triangledown" etc. verbatim.

**Build: 0 errors, 0 warnings. Tests: 69/69 passing.**

---

## [2026-03-30] — Cipher B Sonification, Speech Fixes & Visual Hierarchy

### Fixed: ZeroArea / ZeroDot Sonification Profiles (`Core/Services/Audio/SonificationProfileProvider.cs`)
- `ZeroArea` (Money Flow Wave) was falling through to the generic sine/line default — TTS and audio both treated it as a plain line.
- Added explicit case: sine waveform, `AmplitudeMapping.Absolute`, `PitchMapping.Value`, zero-crossing boundary click, Sustain envelope. The wave now glides pitch up/down as money flow oscillates.
- Added `ZeroDot` case: sine Ping envelope, `PitchMapping.Direction` — positive MF = 660 Hz bright tone, negative = 220 Hz low tone. One note per signal dot.

### Fixed: Speech `{type}` Token Mangled for New Display Types (`Core/Services/Accessibility/SpeechFormatter.cs`)
- `comp.DisplayType.ToString().ToLower()` → `"zeroarea"` was being passed verbatim to TTS, which read it as "ZAO-rea" or similar.
- Added `FriendlyTypeName(ComponentDisplayType)` helper: `ZeroArea` → "oscillator", `ZeroDot` → "dot", `StepLine` → "step line", all others mapped explicitly. Unknown values fall back to `.ToString().ToLower()`.
- Applied to both the generic `{type}` token substitution path and the price-series display type path.

### Fixed: CIPHER_B Speech Templates — Stale and Missing Entries
- `"Money Flow"` template was `"Money Flow. Bar. {value:F1}."` — leftover from when it was a Histogram. Now `"Money Flow. Dot. {value:F1}."`.
- Added missing templates: `Money Flow Wave` → "oscillator", `RSI~` → "Smoothed RSI. Oscillator.", `Stoch %K` → "Stochastic K. Oscillator.", `Stoch %D` → "Stochastic D. Oscillator.", `VWAP~` → "VWAP Oscillator. Oscillator."
- `WT1`/`WT2` display names clarified to "Wave Trend 1" / "Wave Trend 2" for unambiguous TTS reading.

---

## [2026-03-30] — Cipher B Visual Polish, Spider Lines, Component Display Labels

### Added: SpiderLinesProvider (`Core/Services/Indicators/SpiderLinesProvider.cs`)
- 8 Fibonacci-period EMA overlays on the main price pane (periods: 8, 13, 21, 34, 55, 89, 144, 200).
- Warm→cool gradient colors: EMA 8 = red `#FF4D4D` through EMA 200 = magenta `#EC407A`.
- `GetDetailFact` announces EMA stacking count (bullish/bearish web) and key levels (21/55/200).
- Registered in `ServiceCollectionExtensions.AddIndicatorPipeline()`.
- `PaneAssignmentService`: `SPIDER_LINES` → Main pane, Overlays category.
- `StylingService`: colors delegated to `SpiderLinesProvider.GetComponentColor()`.

### Updated: CipherBProvider — Visual Hierarchy Corrections
- **Laguerre RSI normalization:** ±50 → ±35 (`* 70.0` instead of `* 100.0`). Keeps RSI~ subdued and contextual vs dominant WT waves.
- **Stoch %K / %D normalization:** Same — ±50 → ±35. Stoch lines no longer visually compete with WT.
- **VWAP~ defaults hidden:** `IsVisible = false` in component metadata. VWAP oscillator accuracy is ~45% at short timeframes; opt-in via Object Tree rather than on by default.

### Updated: StylingService — Cipher B Color Refinements
- **WT1:** `#00C8FF` (blue) → `#D0D0D0` (gray/white — MC-accurate "cutter" line).
- **WT2:** `#7FDBFF` → `#0090C8` (deeper teal channel wave).
- **Money Flow Wave:** `#26A69A` positive / `#EF5350` negative (MC teal-green / MC red).
- **Money Flow dot:** same MC teal-green / MC red palette.
- **Stoch %K:** `#00E5FF` → `#00B8D4` (softened cyan — less visually aggressive).
- **Stoch %D:** `#FF6D00` → `#E65100` (softened amber-orange).
- **RSI~:** 1.5px thickness. Stoch %K / %D / VWAP~: 1.0px thickness.
- **GetDisplayType CIPHER_B:** added `ZeroArea` (Money Flow Wave), `ZeroDot` (Money Flow), `Oscillator` for RSI~/Stoch/VWAP~.
- **GetSecondaryColor:** Money Flow Wave and Money Flow dot now use MC red `#EF5350`.
- Added `using AccessibleTrader.Core.Services.Indicators` for `SpiderLinesProvider` color lookup.

### Updated: StandardRenderers — Money Flow Wave Visibility
- `RenderZeroArea` fill alpha increased from 80 → 120 (~47% opacity). Improves Money Flow Wave visibility on dark background without overwhelming the WT wave lines.

### Fixed: ObjectTreeModal — Component Display Type Label
- `@comp.DisplayType` (raw enum) replaced with `DisplayTypeName()` helper mapping to user-friendly strings.
- `ZeroArea` → "Oscillator", `ZeroDot` → "Dot", `StepLine` → "Step Line". All other types mapped explicitly; unknown enum values fall back to `.ToString()`.

### Architecture: Planned Roadmap Items Added to TODO.md
- **Phase 10-G: Indicator self-describing color/style metadata** — move colors/thickness into `IndicatorComponentMetadata`; `IndicatorMetadataCache` singleton; `StylingService` reads metadata first.
- **Phase 10-G: Indicator sub-panes** — per-component Y-axis strips within oscillator panes; Money Flow Wave primary use case; normalization removable when sub-panes implemented.

---

## [2026-03-29] — Phase 10-F: Accessible Cipher B, Custom Strategy Tab & Indicator Styling

### Added: Accessible Cipher B (`Core/Services/Indicators/CipherBProvider.cs`)
- Native C# `IIndicatorProvider` replicating the Market Cipher B indicator suite.
- **Code:** `CIPHER_B` — category `Multi-Signal` — own oscillator pane `Pane_CIPHER_B`.
- **11 components:** WT1 (blue line), WT2 (gray line), WT Fill (cloud bullish/bearish), Money Flow (green/red histogram), Blue/Red/Gold signal dots, Bull/Bear divergence dots, Hidden Bull/Hidden Bear dots.
- **Wave Trend algorithm:** hlc3 EMA channel → CI → EMA → WT1, SMA(WT1, 4) = WT2.
- **MC Money Flow:** direction-based (close≥open ? +vol : -vol), SMA-smoothed, normalized to ±100.
- **Divergence detection:** 4 types — regular bull/bear + hidden bull/bear via pivot high/low detection (`PivotBars` bars each side).
- **Signal dots:** Blue = WT cross from oversold; Red = WT cross from overbought; Gold = Blue + RSI oversold + positive money flow.
- **Reference levels:** ±60 (Extreme OB/OS, dotted red/green), ±53 (OB/OS, dashed), 0 (zero line).
- **GetDetailFact:** rich accessibility speech describing bar context (WT position, MF direction, active signals).
- Registered in `ServiceCollectionExtensions.AddIndicatorPipeline()`.

### Added: CustomIndicatorRegistry (`Core/Services/Indicators/CustomIndicatorRegistry.cs`)
- Thread-safe `ConcurrentDictionary`-backed registry for Roslyn/Pine compiled `ICustomIndicator` instances.
- `ICustomIndicatorRegistry` interface: `Register`, `TryGet`, `Unregister`, `GetAll`.
- Registered as singleton in DI. `IndicatorEngine` now checks registry first before `IIndicatorService`.
- `SeriesManagementService.AddCustomIndicator` calls `_customRegistry.Register(indicator)` before `RegisterSeries`.

### Added: IndicatorComponentMetadata Cloud fields (`Sdk/Models/IndicatorMetadata.cs`)
- `UpperComponentName` and `LowerComponentName` on `IndicatorComponentMetadata` carry cloud boundary names through the metadata pipeline.
- `IndicatorModelFactory.CreateSeriesFromMetadata` copies these to `ComponentConfig` when present.
- CipherBProvider WT Fill component uses this to link WT1/WT2 as cloud boundaries.

### Updated: IAudioDriver / BlazorAudioDriver
- `SetVoice(...)` gains `float noiseAmount = 0f` parameter — matches `AudioEngine.SetVoice` that already supported it.
- Allows `NavigationSonifier` dynamic OB/OS noise texturing to propagate cleanly without a cast.

### Updated: StylingService
- CIPHER_B per-component color map: WT1=#00C8FF, WT2=#7FDBFF, cloud bullish/bearish, MF green/red, signal dot colors.
- `GetThickness` helper: WT1/WT2 get 3px thickness.

### Updated: PaneAssignmentService
- `CIPHER_B` → category `Multi-Signal`, pane `Pane_CIPHER_B`.

### Updated: RoslynScriptingService
- Added `CompileStrategyAsync(string code)` → `CompileStrategyResult(Success, ITradingStrategy?, Errors[])`.
- Compiles a user-written class implementing `ITradingStrategy` (with `AccessibleTrader.Core` assembly in references so `BaseStrategy` is available).
- `IRoslynScriptingService` interface updated with `CompileStrategyAsync`.

### Updated: StrategyModal — Custom Script tab
- New tabbed layout: Add Strategy / Active (N) / Backtest / **Custom Script** tabs.
- Custom Script tab: C# code editor textarea, template expandable section, execution mode selector, Compile & Add button.
- On success: compiles via `IRoslynScriptingService.CompileStrategyAsync`, adds to `StrategyEngine`, switches to Active tab.
- Compilation errors shown inline in the editor pane.

### Updated: MFI / Chaikin styling
- `GetDisplayType`: MFI → `Histogram`; Chaikin OSC variants → `Histogram`.
- `GetColorBaseline`: MFI → 50.0 (green above 50, red below 50 via `ColorBaseline` field on `ComponentConfig`).
- `StandardRenderers.RenderDirectionalBars` uses `comp.ColorBaseline` instead of hardcoded 0 for threshold coloring.

### Updated: NavigationSonifier — dynamic OB/OS noise texturing
- When navigating an Oscillator/Histogram/Line component that has Overbought/Oversold Level components, and the current value exceeds those thresholds, blends 0.20f pink noise into the voice (via `noiseAmount` parameter).
- RSI's existing sine/triangle waveform switching is untouched; noise is additive only in extremes.

**Build:** 0 errors, 0 new warnings. **Tests:** 69/69 passing.

---

## [2026-03-29] — Phase 10-E: PineScript Transpilation

### Added: PineTranspiler (`Core/PineScript/PineTranspiler.cs`)
- Three-tier pattern-based transpiler (no ANTLR dependency — hand-written regex/pattern matching).
- **Tier 1 — Core Mapping:** `ta.sma`, `ta.ema`, `ta.rsi`, `ta.macd`, `ta.bb`, `ta.atr`, `ta.stoch`, `ta.crossover`, `ta.crossunder`, `ta.highest`, `ta.lowest`, `ta.stdev`. `plot()` → component registration. `plotshape()` → `ComponentDisplayType.Dot`. `input()` / `input.int()` / `input.float()` → `DefaultParameters`. All six source series (`close`, `open`, `high`, `low`, `volume`, `hl2`, `hlc3`, `ohlc4`) mapped to array references.
- **Tier 2:** `var` / `varip` stripped (both produce a plain C# variable). `na` → `double.NaN`. `nz(x)` / `nz(x, d)` → `NzHelper(x)` / `NzHelper(x, d)`. `math.max/min/abs/sqrt/pow/log/pi` → `Math.*`. Conditional color expressions → partial support (color expressions stripped to prevent compile errors).
- **Tier 3 stubs:** `request.security()` → `NanArr(n)` with a warning. `line.new()` / `label.new()` / `strategy.*` → not translated (generate as comment or fall through to generic body).
- Generated class implements `ICustomIndicator` — emits `Id`, `DisplayName`, `ComponentNames`, `DisplayTypes`, `DefaultParameters`, `Calculate(ReadOnlySpan<Ohlcv>, parameters)`. Static helper methods embedded in the generated class: `SmaArr`, `EmaArr`, `RsiArr`, `AtrArr`, `HighestArr`, `LowestArr`, `StdevArr`, `CrossoverArr`, `CrossunderArr`, `MacdArr`, `BbArr`, `StochArr`, `NzHelper`, `NanArr`, `Arr`.
- `TranspileResult(Success, CSharpCode, Errors[], Warnings[])`.

### Updated: CustomScriptsModal — Pine Transpile Section
- "Transpile from Pine Script v5" `<details>` section added below Import.
- Textarea accepts Pine v5 source → `PineTranspiler.Transpile()` → generated C# loaded into the code editor as a new script entry.
- Warnings (e.g., `request.security()` stubs) shown in an amber notice box.
- Script can then be compiled via the existing Compile → Add to Chart flow.

**Build:** 0 errors, 0 new warnings. **Tests:** 69/69 passing.

---

## [2026-03-29] — Phase 10-D: Custom Indicator Platform (Roslyn)

### Added: ICustomIndicator Interface (`Sdk/Interfaces/ICustomIndicator.cs`)
- Contract for user-defined Roslyn-compiled indicators: `Id`, `DisplayName`, `ComponentNames[]`, `DisplayTypes[]`, `DefaultParameters`, `Calculate(ReadOnlySpan<Ohlcv>, Dictionary<string,double>)`.
- Each `Calculate` call returns one `double[]` per component in the same order as `ComponentNames`.

### Added: RoslynScriptingService (`Core/Services/RoslynScriptingService.cs`)
- `IRoslynScriptingService` interface: `CompileIndicatorAsync(code)` and `ExecuteSimpleAsync(code, data)`.
- `CompileIndicatorAsync`: Uses `CSharpCompilation` (not `CSharpScript`) to emit a real DLL in memory. Each compile runs in an isolated `AssemblyLoadContext` (collectible). Returns `CompileResult(Success, Indicator, Errors[])`.
- Sandbox: allowed references — `AccessibleTrader.Sdk`, `System.Numerics`, `System.Runtime.*`, `Skender.Stock.Indicators` (if loaded). No `System.IO` or `System.Net` surface in the script's reference set.
- Scripts that don't include `using` / `namespace` declarations are auto-wrapped in `using AccessibleTrader.Sdk.*` + `namespace CustomIndicators { ... }`.
- `UnloadScript(id)` unloads the per-script ALC when a script is deleted.
- `ExecuteSimpleAsync` retains the original `CSharpScript` path for lightweight expression scripts.
- Registered as singleton in `ServiceCollectionExtensions`.

### Added: CustomScriptsModal Full Implementation (`BlazorClient/Components/CustomScriptsModal.razor`)
- Two-panel layout (200 px script list + flex editor).
- **Script list**: listbox with name + status (Active / Saved), New / Delete buttons.
- **Editor**: script name input, large monospace `<textarea>` with code placeholder showing `ICustomIndicator` template.
- **Compile** button: calls `IRoslynScriptingService.CompileIndicatorAsync`, shows error list in red or "Compiled successfully" in green.
- **Add to Chart** button (shown only after successful compile): calls `ISeriesManagementService.AddCustomIndicator(indicator, state)` → registers the compiled indicator as a standard chart series.
- **Export .atpkg** button: serializes `{Version, Name, Author, Code}` as JSON and downloads via `accessibleTrader.downloadCsv` JS interop. File extension `.atpkg`.
- **Import .atpkg** `<details>` section: paste JSON → deserialize `AtpkgPayload` → create new script entry with imported code. Success/error feedback.
- Code placeholder guides the user with a commented `ICustomIndicator` template skeleton.

### Updated: SeriesManagementService
- `ISeriesManagementService.AddCustomIndicator(ICustomIndicator, WorkspaceState)` — creates a series entry from a compiled indicator using `RegisterSeries` with the indicator's ID, display name, component names, and default parameters.

**Build:** 0 errors, 0 new warnings. **Tests:** 69/69 passing.

---

## [2026-03-29] — Phase 10-C: Completions & Polish

### Enhanced: BarDetailService Coverage
- **Volume (CoreIndicatorProvider):** Rich `GetDetailFact` for `VOLUME` code. Reports volume value, comparison to 10-bar average as a ratio (surge ≥2×, above average ≥1.3×, dry-up ≤0.4×, below average ≤0.7×), and 3-bar consecutive trend (building / declining).
- **RSI (SkenderIndicatorProvider):** Added 5-bar divergence hint — compares RSI trend vs price trend over 5 bars. Reports "Bullish divergence hint" when RSI rising but price falling; "Bearish divergence hint" when RSI falling but price rising.
- **MACD (SkenderIndicatorProvider):** Histogram trend improved from "growing/fading" to "expanding/contracting". Added zero-line approach detection: when MACD is trending toward zero and has lost >50% of magnitude vs 3 bars ago, announces "Approaching zero line."
- **Bollinger Bands (SkenderIndicatorProvider):** Squeeze/expansion now computed from live 20-bar average band width (replaces `__SQUEEZE` sentinel). `< 0.7×` avg = "Squeeze."; `> 1.4×` avg = "Expansion." Also fixed `percent` calculation to `percentB` (uses Close, not Open) for correct %B position label.
- **EMA/SMA/WMA/HMA etc. (SkenderIndicatorProvider):** Added price-to-MA distance % ("Price 0.45% above."). Added per-bar slope as % of MA value ("Slope +0.012% per bar."). 5-bar consecutive trend text now reads "Strong uptrend." / "Strong downtrend." Crossover detection retained.
- **CCI (SkenderIndicatorProvider):** New case — reports value, zone (Overbought > 100, Oversold < −100, Neutral), and rising/falling direction.
- **ADX (SkenderIndicatorProvider):** New case — reports ADX value, strength label (Weak / Developing / Strong / Extremely strong), and dominant DI direction with +/− values when available.

### Added: HelpModal Live Shortcut Reference
- `HelpModal.razor` now injects `IShortcutManager`.
- New "All Keyboard Shortcuts (Live)" `<details>` section at the bottom renders `ShortcutManager.GetAllBindings()` in a two-column table (Key Combination, Command). This always reflects the active shortcut profile — no drift from hardcoded tables.
- Added missing entries to the UI & Settings section: Alt+D, Alt+J, Alt+W, Alt+,, Alt+C, Alt+L, P / Shift+F12, Ctrl+Shift+D.
- `FormatCommandName()` helper converts PascalCase SystemCommand names to readable text (e.g. "NavLeft" → "Nav Left").

### Added: iOS Hardware Keyboard Support
- `Platforms/iOS/KeyboardPageHandler.cs` — mirrors the Mac Catalyst implementation. Wraps the root `UIViewController` with `KeyboardViewController` that overrides `PressesBegan` to route hardware keyboard events to `IInputService.ProcessKey`.
- Uses the same UIKit Unicode private-use-area key mapping as Mac Catalyst (arrows, F1–F8, Home, End, PageUp/Down, Escape).
- Registered in `MauiProgram.cs` under `#if IOS ConfigureMauiHandlers`.

### Fixed: WebSocket Zero-Value Frame Filter (Coinbase & Polygon)
- **Coinbase (`CoinbaseProvider`):** Ticker tick prices of `<= 0` are now skipped before updating the running candle. Prevents zero-OHLCV bars from subscribe-confirmation messages.
- **Polygon (`PolygonProvider`):** Aggregate messages where `Open == 0 && High == 0 && Low == 0 && Close == 0` are now skipped. Same pattern as Binance/Bitstamp/Alpaca.

### Added: StrategyIndicatorCache
- `IStrategyIndicatorCache` + `StrategyIndicatorCache` (Core) — shared indicator computation cache for strategies. Provides `GetSma`, `GetEma`, `GetRsi`, `GetBollingerBands` methods. Results keyed by `(type, period, data.Count)` in a `ConcurrentDictionary`.
- `Invalidate(currentCount)` clears stale entries at the start of each `OnDataUpdated` cycle — strategies always compute against fresh data, never stale cached values.
- `StrategyEngine` injects `IStrategyIndicatorCache` and calls `Invalidate` before each `OnBar` evaluation cycle.
- Registered as singleton in `ServiceCollectionExtensions.AddBusinessServices()`.
- Decouples strategies from the chart's `ActiveSeries` — custom strategies (Phase 10-D) can compute indicators without requiring them to be on the chart.

**Build:** 0 errors, 0 new warnings. **Tests:** 69/69 passing.

---

## [2026-03-29] — Phase 10-B: Sound Designer — Patch Library, Custom Earcons, Alt+W Modal

### Added: SoundPatch Model (`Sdk/Models/SoundPatch.cs`)
- Serializable named sound preset: `Waveform`, `NoiseAmount`, `BaseFrequency`, `FreqMultiplier`, `Volume`, `EnvelopeType`, `DurationSeconds`, `Description`.
- Each patch has a stable `Id` (GUID). `Clone()` assigns a fresh GUID to the copy so originals are never mutated.

### Added: SoundPatchLibrary Service (`Core/Services/SoundPatchLibrary.cs`)
- `ISoundPatchLibrary` — `GetPatches`, `AddPatch`, `RemovePatch`, `UpdatePatch`, `GetPatch`, `ExportPatchJson`, `ImportPatchJson`, `EarconOverrides`, `SaveEarconOverrides`, `SavePatches`.
- `EarconSettings` — `Dictionary<string, string> EarconPatchIds` maps earcon keys (Boundary, Info, Error, Success, Retry, NewBar, Connected, Disconnected) to patch IDs.
- Loads from / saves to `patches.json` + `earcon-settings.json` in `IPlatformPathService.AppDataDirectory`. Missing files → empty library (no crash on first run).
- `ImportPatchJson` always assigns a fresh GUID to the imported patch to prevent ID collisions with existing library entries.
- Registered as singleton in `ServiceCollectionExtensions.AddCoreInfrastructure()`.

### Added: EarconService Patch Override (`Core/Services/Accessibility/EarconService.cs`)
- Constructor now injects `ISoundPatchLibrary`.
- `PlayWithPatchFallback(earconKey, defaultFreq, ...)` — checks `EarconOverrides.EarconPatchIds[earconKey]`, plays the assigned patch if found; falls back to hardcoded default parameters if not.
- `PlayInfo()`, `PlayBoundary()` use `PlayWithPatchFallback`. `PlayNewBar()` checks the override before playing its three-partial bell.

### Added: Sound Designer Modal (`BlazorClient/Components/SoundDesignerModal.razor`)
- Opened via `Alt+W` (`OpenSoundDesignerEvent`). Focuses `h2#sound-designer-title` on open (ARIA pattern).
- Two-panel layout (200 px patch list + flex editor), max-width 760 px.
- **Patch list**: `role="listbox"` with keyboard-accessible items (`Enter` / `Space` to select). New / Clone / Delete buttons.
- **Editor**: Identity fieldset (Name, Description), Oscillator fieldset (Waveform select incl. Noise, Noise Blend range, Base Freq, Freq Multiplier, Volume range), Envelope fieldset (Sustain/Ping type select, Duration).
- **Preview** button: plays current editor values immediately via `ISonificationManager.PlayNote`.
- **Save Patch** button: commits editor values back to `ISoundPatchLibrary`.
- **Export JSON** button: calls `PatchLibrary.ExportPatchJson` and triggers browser download via `accessibleTrader.downloadCsv` JS interop.
- **Earcon Assignments** `<details>`: table mapping all eight earcon keys to patch dropdowns with per-row preview buttons.
- **Import JSON** `<details>`: textarea + Import button with colour-coded success/error status message.
- Publishes `ModalStateChangedEvent(true/false)` on open/close (canvas-hide pattern).

### Added: Keyboard Shortcut & Command Wiring
- `SystemCommand.OpenSoundDesigner` — `Alt+W` (W for waveform; Alt+S already taken by OpenStrategies).
- `CommandDispatcher` handles `OpenSoundDesigner` → `EventBus.Publish(new OpenSoundDesignerEvent())`.
- `ShortcutManager.InitializeDefaultProfile()` includes the `Alt+W` binding.

**Build:** 0 errors, 0 new warnings. **Tests:** 69/69 passing.

---

## [2026-03-28] — Phase 10-A: Foundation — Persistence, Display Types, Per-Bar Coloring, Audio Noise

### Fixed: Mute / Hide / Volume State Not Persisted on Restart (A1)
- `ChartCommandManager`: `_seriesManager.PersistWorkspace()` is now called after `ToggleMuteAction` (both component and series scope), after `ToggleHideAction` (both scopes), and after every `VolumeChangeEvent` dispatch. Mute state, hide state, and F5–F7 volume levels now survive app restarts.

### Added: Per-Bar Coloring System (A2)
- `ColorCondition` enum (`Sdk/Models/ColorRule.cs`): `AboveZero`, `BelowZero`, `Rising`, `Falling`, `AboveLevel`, `BelowLevel`.
- `ColorRule` record: `Condition`, `ColorHex`, `Level` (threshold for AboveLevel/BelowLevel).
- `ComponentConfig.ColorRules: List<ColorRule>` — empty by default (no overhead on existing indicators). First matching rule wins and overrides the static `ColorHex` for that bar.
- `StandardRenderers.ResolveBarColor()` — private helper; evaluates `ColorRules` against the component data value and previous bar value.
- `StandardRenderers.RenderLine` — when `ColorRules` is non-empty, draws each line segment individually with the resolved per-bar color rather than using a single-path approach.
- `StandardRenderers.RenderDirectionalBars` — when `ColorRules` is non-empty, resolves per-bar color before drawing; still falls back to candle-direction or value-sign coloring when no rule matches.

### Added: New Display Types (A3)
- `ComponentDisplayType` enum expanded: `Dot`, `Arrow`, `StepLine`, `Cloud`, `Gradient`.
- `ComponentConfig.UpperComponentName` / `LowerComponentName` — used by `Cloud` display type to name the two boundary components within the same series.
- `StandardRenderers.RenderDot` — filled circle per bar at value Y. Radius = `comp.Thickness * density`.
- `StandardRenderers.RenderArrow` — up/down triangle per bar. Positive value = up arrow; negative = down arrow. Uses `ColorRules` when present.
- `StandardRenderers.RenderStepLine` — staircase line: horizontal to next bar X, then vertical to new value. Used by ADX-style indicators.
- `StandardRenderers.RenderCloud` — filled polygon between `UpperComponentName` and `LowerComponentName` components. Direction runs (upper > lower vs upper < lower) are split into bullish (ColorHex alpha-60) and bearish (ColorHexSecondary alpha-60) filled regions. `FlushCloudRun` helper handles polygon closure.
- `StandardRenderers.RenderLine` (`Area` / `Gradient` display types) — now produces a filled area below the line (alpha-60 fill, then line re-drawn on top). Previously `Area` type drew only a bare line; the fill was missing.
- `DataLayer` switch statement updated: `Gradient` routes to `RenderLine`; `Dot` → `RenderDot`; `Arrow` → `RenderArrow`; `StepLine` → `RenderStepLine`; `Cloud` → `RenderCloud`.

### Added: AudioEngine Noise Oscillator (A5)
- `WaveformType.Noise` — pure pink noise waveform (one-pole low-pass filtered white noise: `y[n] = 0.997 * y[n-1] + 0.003 * x[n]`). Phase advance still occurs so `FreqMultiplier` remains a consistent parameter for cutoff-like tuning.
- `ComponentConfig.NoiseAmount` `[0.0, 1.0]` — blends noise into any waveform at the voice level. `0` = pure waveform (no change to existing sounds). `1` = pure noise. `0.3` = subtle texture.
- `OscillatorVoice.NoiseAmount` / `OscillatorVoice.NoiseState` — per-voice noise state. `NoiseState` persists between samples for a smooth, non-clicking texture (not reset between bars).
- `VoiceCommand.NoiseAmount` — carries the noise level from the main thread to the audio callback ring buffer.
- `AudioEngine.SetVoice(... noiseAmount = 0f)` — optional parameter; all existing callers unaffected (default = 0, silent noise path).
- `AudioEngine._rng` — `Random` instance used exclusively on the audio callback thread. No locking required.

**Build:** 0 errors, 0 warnings (pre-existing platform warnings unchanged). **Tests:** 69/69 passing.

---

## [2026-03-28] — Phase 10 First Wave: Persistence, Custom Scripts, Data Export, Settings Profiles

### Fixed: PropertiesModal Changes Not Persisted on Restart
- `PropertiesModal.Apply()` now calls `SeriesManager.PersistWorkspace()` after dispatching `UpdateSeriesAction`. Component colors, audio settings, and level configurations now survive app restarts.

### Fixed: AlertOrchestrator False-Positive Crossover Alerts on Cold Start
- Added `_initialized` guard to `AlertOrchestrator`. First evaluation tick seeds `_previousValues` from current indicator state and returns without firing alerts. Subsequent ticks evaluate crossovers normally against the now-populated snapshot.

### Added: Custom Scripts Infrastructure
- `OpenCustomScriptsEvent`, `SystemCommand.OpenCustomScripts`, `Alt+,` shortcut binding in `ShortcutManager`.
- `CommandDispatcher`: routes `OpenCustomScripts` → publishes `OpenCustomScriptsEvent`.
- `IndicatorBar.razor`: "Scripts" button added after "Add Indicator" — opens `CustomScriptsModal` via EventBus.
- `ICustomScriptService` interface (`Core`): `CustomScript` record (Id, Name, Code, IsEnabled); `GetScripts`, `AddScript`, `RemoveScript`, `UpdateScript`, `RunScriptAsync`, `SaveScripts`.
- `CustomScriptsModal.razor`: Full script list modal. Subscribes to `OpenCustomScriptsEvent`. Focuses `scripts-title` h2 on open. Publishes `ModalStateChangedEvent` on show/close.
- `MainLayout.razor`: `<CustomScriptsModal />` added alongside other modals.

### Added: Data Export (CSV)
- `IDataExportService` / `DataExportService` (`Core`): exports viewport-scoped OHLCV + all visible non-drawing indicator components to CSV. Columns: Date, Open, High, Low, Close, Volume, then one column per visible component (named `SeriesId.ComponentName`).
- Settings → General tab: "Export CSV" button calls `DataExporter.ExportToCsvAsync` then triggers `accessibleTrader.downloadCsv(filename, csvContent)` JS interop for browser file save.
- `keyboard.js`: `accessibleTrader.downloadCsv(filename, csv)` function — creates a Blob URL, triggers `<a>` click, revokes URL.
- `ServiceCollectionExtensions`: `DataExportService` registered as singleton.

### Added: Settings Profiles (Visual / Audio)
- `VisualProfile` / `AudioProfile` / `ComponentAppearance` / `ComponentAudioOverride` classes in `AccessibleTrader.Sdk/Models/SettingsProfiles.cs`.
- `IWorkspaceLibraryService` extended: `ExportVisualProfile()`, `ExportAudioProfile()`, `ImportVisualProfile(json)`, `ImportAudioProfile(json)`. Visual profile captures theme + all series component colors. Audio profile captures volume levels + per-component waveform/envelope/freq settings.
- Settings → General tab: "Export Visual", "Export Audio", "Import Visual", "Import Audio" buttons.

### Added: Keyboard Shortcut Reference Tab in SettingsModal
- `ShortcutDisplayBinding` record: `Command`, `Key`, `Modifiers (Ctrl/Alt/Shift)`, `Description`. `FormatBinding()` helper: builds "Ctrl+Alt+Shift+Key" display string.
- `IShortcutManager.GetAllBindings()` / `ShortcutManager.GetAllBindings()`: returns all registered bindings as `List<ShortcutDisplayBinding>`.
- Settings modal: new "Keyboard" tab (tab order: General / Appearance / Keyboard / License / About). Renders a `<table>` of all bindings — `role="table"` with accessible `<caption>`.

### Fixed: Zero-Value Live Bar Filter (Binance, Bitstamp, Alpaca)
- WebSocket message handlers in `BinanceProvider`, `BitstampProvider`, `AlpacaProvider` now reject frames where all OHLCV fields are zero AND the bar timestamp is zero or Unix epoch. These are subscribe-confirmation frames that previously produced a 0-bar at the chart start. Bars with a valid timestamp but zero OHLCV (genuinely dead assets) are still accepted.

### Fixed: BackfillManagerTests Timing Race in Parallel Runs
- `WaitForConditionAsync` condition in `QueueBackfill_WhenFetchSucceeds_PersistsBarsAndPublishesEvent` now requires BOTH `ctx.OhlcvData.CountAsync().Result >= 2` AND `eventBus.Log.Exists(e => e is ChartEvent ce && ce.Type == "BACKFILL_COMPLETE")`. Previously only the DB check was required, causing the test to read `eventBus.Log` before the background thread had published the event. All 5 BackfillManagerTests pass under `dotnet test --maxcpucount`.

---

## [2026-03-28] — Visual Polish: Chart Rendering Improvements

### Fixed: X-Axis Timestamp Text Clipping at Canvas Edge
- `ChartRenderer.RenderXAxis`: text baseline moved from `rect.Bottom - 5` (near canvas bottom edge) to `rect.Top + fontSize + 6`, placing labels in the upper portion of the axis strip. Text no longer risks clipping on high-DPI or full-height canvases.
- `ThemeService`: `AxisHeight` increased from `30f` to `40f` across all standard themes (HighContrastLarge retains `35f`) to give the timestamp strip more breathing room.

### Fixed: Y-Axis Label Crowding in Small Indicator Panes
- `ChartRenderer.RenderYAxis` now adapts label density to pane height: panes taller than 100 logical px use 5 evenly-spaced labels; shorter panes use 3. An additional minimum-spacing guard prevents any two labels from overlapping regardless of zoom level.
- Label alignment changed from right-of-axis to left-justified inside the Y-axis column with consistent left padding (`rect.Left + 3`).

### Fixed: Missing Separator Lines Between Chart Area and Axis Columns
- A vertical separator line is now drawn at `x = width - axisWidth` spanning the full chart area height (main + indicator panes), clearly delineating the Y-axis column from the chart area.
- A horizontal separator is drawn at `y = height - axisHeight` between the chart data area and the X-axis timestamp strip.
- Both lines use `theme.GridLine` color at 160 alpha so they match the active theme without hardcoded colours.

### Improved: Indicator Pane Default Height in Auto-Layout Mode
- When no stored `PaneHeightRatios` ratio is present for a pane, auto-layout now assigns **22% of total chart height** per pane (previously: 30% / paneCount). This gives each oscillator pane a consistent ~22% regardless of how many panes are open, instead of shrinking each pane as indicators are added.
- Minimum floor of 80px (density-scaled) and 25% main-pane floor remain in effect.

---

## [2026-03-28] — Phase 7: Strategy Backtester UI, Mac Catalyst Keyboard, Platform Audio Drivers

### Added: Strategy Backtester UI (StrategyModal.razor)
- New **Backtest** section in `StrategyModal.razor` with capital, commission, and slippage inputs. A "Run Backtest" button invokes `IStrategyBacktester.RunAsync(instance, data, params)` on the selected strategy.
- Results section displays: Sharpe Ratio, Max Drawdown, Win Rate, Total Trades. A collapsible `<details>` trade log lists every closed trade (entry date/price, exit date/price, profit, direction).
- `IStrategyBacktester` registered as a singleton in `ServiceCollectionExtensions.AddBusinessServices()`.

### Added: Mac Catalyst Hardware Keyboard Input
- New `KeyboardPageHandler` (custom `PageHandler`) in `Platforms/MacCatalyst/` wraps the root `UIViewController` with `KeyboardViewController`, which overrides `PressesBegan` to forward hardware keyboard events to `IInputService.ProcessKey`.
- Special keys use NSEvent Unicode private-use-area constants (`\uF700` = ArrowUp … `\uF70B` = F8, `\uF729` = Home, `\uF72B` = End) — NOT `UIKeyCommand.InputXxx` static properties, which are absent from the .NET 10 MAUI binding.
- Registered in `MauiProgram.cs` via `#if MACCATALYST` inside `ConfigureMauiHandlers`.
- `AppStartupService.WarnAboutUnimplementedPlatformFeatures()` no longer emits Mac keyboard warning.

### Added: Android Audio Driver
- `BlazorAudioDriver` (Windows `#elif ANDROID` branch): `AudioTrack` PCM-Float stream mode. Buffer sized to `max(1024 * channels * sizeof(float), AudioTrack.GetMinBufferSize(...))`. Write loop runs on `TaskCreationOptions.LongRunning` background thread with `CancellationTokenSource` for clean shutdown.

### Added: iOS / Mac Catalyst Audio Driver
- `BlazorAudioDriver` (`#elif IOS || MACCATALYST` branch): `AVAudioEngine` + `AVAudioSourceNode` render callback. Uses `new AVAudioFormat((double)sampleRate, (uint)channels)` constructor (avoids `PCMFormatFloat32` enum absent in .NET 10). De-interleaves samples per channel via `Marshal.Copy` (avoids `unsafe` code). Callback returns `0` (noErr int). `AppStartupService` no longer emits Android/iOS audio warnings.

---

## [2026-03-28] — Phase 6: Provider Order Update Streams

### Added: Binance User Data Stream
- `BinanceProvider.EnsureConnectedAsync`: calls `StartUserDataStreamAsync()` when API keys are present. Obtains a `listenKey` via `TradingClient.SpotApi.Account.StartUserStreamAsync()`, then subscribes via `_socketClient.SpotApi.Account.SubscribeToUserDataUpdatesAsync` with an `onOrderUpdateMessage` handler that maps execution reports to `OrderUpdate` objects and pushes to `_orderUpdateSubject`.
- A `System.Timers.Timer` fires every 25 minutes to call `KeepAliveUserStreamAsync`, preventing the listen key from expiring.
- `DisconnectAsync` stops the timer and calls `StopUserStreamAsync` for clean teardown.

### Added: Bitstamp Private Order Channel
- `BitstampProvider`: HMAC-SHA256 authentication for `private-my_orders-{pair}` WebSocket channel. Auth signature = `HMAC-SHA256(nonce + timestamp + apiKey)` with API secret.
- `ReceiveLoop` now handles `order_changed` and `order_deleted` events from the private channel and pushes mapped `OrderUpdate` objects to `_orderUpdateSubject`.
- Private channel subscription called from `ConnectAsync` when API key and secret are available.

---

## [2026-03-28] — Phase 5: Pane Layout UX, Ctrl+Alt+Shift+C Chart Focus

### Added: Pane Height Resize (Drag Handles)
- `IPaneLayoutService` singleton: `ChartRenderer` writes divider Y-fractions after each render; `ChartArea.razor` reads these to position CSS drag-handle dividers at the correct pixel positions.
- `ChartArea.razor`: drag handles rendered between indicator panes. `@onmousedown` / `@onmousemove` / `@onmouseup` handlers dispatch `ResizePaneAction(paneName, delta)` to the store.
- `ResizePaneAction` reducer clamps each pane ratio to `[0.05, 0.60]`.

### Added: Indicator Pane Scroll (Alt+Up / Alt+Down)
- `ShortcutManager`: `Alt+Up` → `ScrollPanesUp`; `Alt+Down` → `ScrollPanesDown`.
- `CommandDispatcher`: dispatches `ScrollIndicatorPanesAction(±1)` and publishes `FeedbackRequestEvent` with "Scroll panes up/down".
- `WorkspaceState.IndicatorPaneScrollIndex` int applied in `ChartRenderer` to `Skip(scrollIndex)` on indicator pane groups.

### Added: Ctrl+Alt+Shift+C — Chart Focus with Context Summary
- `ShortcutManager`: `Ctrl+Alt+Shift+C` → `ChartFocus` command.
- `CommandDispatcher`: publishes `ChartFocusEvent()` (sets `_isChartActive = true`) + `FeedbackRequestEvent(Info, "CONTEXT_SUMMARY", true)`.
- `ChartArea.razor`: `OnChartFocused` handler already publishes `ChartFocusEvent()` — confirmed wired.

### Added: Pane Ratio Persistence
- `WorkspaceState`: new `SetPaneHeightRatiosAction(ImmutableDictionary<string,float> Ratios)` reducer action.
- `WorkspaceInitializer.InitializeDefaultSeries`: restores `PaneHeightRatios` from saved workspace config on startup.
- `WorkspaceInitializer.SaveWorkspace`: serialises `PaneHeightRatios` to `WorkspaceConfiguration.PaneHeightRatios`.

---

## [2026-03-28] — Phase 9: Silent Bug Fixes (Alert Crossover, Indicator Context, Bar Detail, F8 Removal)

### Fixed: AlertEvaluator Indicator Crossover Alerts Never Firing
- **Root cause:** `AlertOrchestrator.EvaluateAlerts` always passed a fresh empty `Dictionary<string,double>` as `previousValues`. `AlertEvaluator.TryEvaluate` compares current value against `previousValues[key]`, which was always `NaN` — so `CrossesAbove`/`CrossesBelow` conditions never triggered.
- **Fix:** `AlertOrchestrator` now keeps a persistent `_previousValues` dict. After each evaluation tick it snapshots all current indicator component values into `_previousValues` (keyed `"IndicatorCode.ComponentName"`). The next tick's evaluation receives those values as the previous state, enabling correct crossover detection.

### Fixed: IndicatorContextAnalyzer Selecting Wrong Component for Multi-Component Indicators
- **Root cause:** `IndicatorContextAnalyzer.Analyze()` used `series.Components.FirstOrDefault(c => c.IsVisible && !c.IsMuted)` to pick the primary component. For MACD, the first visible component is often the "MACD" line, but the registered definition targets "Histogram". The definition lookup then missed and crossover detection was skipped.
- **Fix:** `Analyze()` now iterates `_defs` to find the registered `ComponentName` for the indicator code first. The correct component is resolved by name match; first-visible is only a fallback when no definition is found.

### Fixed: EvaluateTrendChange Firing on Every Non-Flat Bar
- **Root cause:** `EvaluateTrendChange` returned `ctx.Trend != TrendDirection.Flat` — i.e., any bar in a trend (Rising or Falling) would fire the alert, not just bars where the trend *changed*.
- **Fix:** `AlertEvaluator` tracks `_previousTrends` per alert+series key. `EvaluateTrendChange` now returns `true` only when `ctx.Trend != TrendDirection.Flat && ctx.Trend != prevTrend` (an actual direction flip).

### Fixed: BarDetailService Passing Empty OHLCV Span to GetDetailFact
- **Root cause:** `BarDetailService.GetBarDetailFact` called `_indicatorService.GetDetailFact(..., ReadOnlySpan<Ohlcv>.Empty, ...)`. The empty span meant indicator detail facts (pattern analysis, lookback context) always ran on zero data and returned empty strings, causing the fallback "list component values" path to always trigger.
- **Fix:** `AnnounceDetails` now builds a lookback slice of up to 50 bars (`state.Data[sliceStart..currentIndex]`) and passes it down. `GetDetailFact` receives real price history for pattern/context analysis.

### Removed: F8 ToggleMuteSonification
- F8 was documented but never implemented in `SystemCommand` or `ShortcutManager` (no binding existed in code). References in `HelpModal.razor`, `CODEBASE_KNOWLEDGE_BASE.md`, and `keyboard.js` trapped-keys list have been removed. F8 is now released for screen-reader and system use.
- F7/Shift+F7 (chart master volume) is the correct replacement for global audio level control.

---

## [2026-03-28] — Indicator Pane Rendering, Multi-Instance Indicators & Reference Level Tests

### Fixed: Multiple Instances of the Same Indicator Blocked (e.g. EMA 100 + EMA 200)
- **Root cause:** `SeriesManagementService.RegisterSeries` assigned `seriesId = id.ToLowerInvariant()` for all non-core indicators, giving every EMA the same ID `"ema"` regardless of period. The duplicate-check guard then found the existing `"ema"` series and silently returned without adding the second instance.
- **Fix:** Non-core indicators now always receive a `Guid.NewGuid()` ID. Only the four singleton core series (`price`, `candles`, `volume`, `heatmap`) retain deterministic fixed IDs. The duplicate guard now only fires for those four.
- **Result:** EMA(100) + EMA(200), two RSI periods, multiple MACD instances, etc. all coexist correctly. Each instance gets its own series slot, its own data buffer, and its own reference levels.

### Fixed: Indicator Pane Height Becoming Unreadable with Multiple Indicators
- **Root cause:** `ChartRenderer` computed `indicatorPaneHeight = totalPaneHeight * 0.3f / count` with no minimum floor. With three indicators each pane received ~10% of chart height — effectively unreadable.
- **Fix:** Enforced `MinIndicatorPaneHeightPx = 80f` (scaled by device density). Each indicator pane is now at least 80 logical px tall. The main price pane receives the remainder but is clamped to a minimum of 25% of total height to prevent the price chart collapsing. Bottom-most panes clip gracefully if canvas height is insufficient.

### Fixed: Crosshair Not Extending Into Indicator Panes
- **Root cause:** `RenderCrosshair` was called once with only the main pane's rect and price range. The vertical crosshair line stopped at the bottom of the main pane and did not cross indicator panes.
- **Fix:** `RenderCrosshair` now:
  - Draws the **vertical line across the full chart height** (main + all indicator panes).
  - Draws a **horizontal crosshair per indicator pane** at the cursor's actual indicator value at that bar index. The first non-NaN component value from the pane's series list is used, mapped via `ChartMath.MapY` with the pane's own min/max. Indicator pane crosshair lines are rendered slightly dimmer (`alpha=100` vs `alpha=150`) to distinguish them from the main price crosshair.
  - The indicator pane layout info (rect, min, max, series list) is accumulated during the pane render loop and passed to the updated method.

### Improved: Reference Level Source of Truth Consolidated
- `IndicatorReferenceLevels` static class introduced as the single source of truth for all OB/OS/zero/midpoint level definitions.
- `SeriesManagementService.InjectDefaultLevels` and `StylingService.GetLevelComponents` both delegate to this class — no more divergence between the two code paths.
- Custom OB/OS parameter values (e.g. user-supplied RSI overbought threshold) now override the canonical defaults at injection time.

### Fixed: Reference Level Lines Not Visible on RSI / MACD Panes
- **Root cause:** `ViewportRangeCalculator` computed pane Y-ranges exclusively from component data arrays, never consulting `series.Levels`. When RSI data was in the 40–60 band, OB=70 and OS=30 mapped outside the computed range bounds and were clipped off-screen. Similarly, MACD zero-line was invisible during sustained trends.
- **Fix:** After scanning component data, the calculator now expands `paneMin`/`paneMax` to include every visible level value. Hidden levels (`IsVisible = false`) are excluded. Levels alone (no component data yet) are sufficient to establish a valid pane range.

### Improved: Settings & Alert Persistence Wired Up
- `ThemeService`: reads saved theme from `ISettingsManager` on construction; persists on every `SetTheme()` call.
- `WorkspaceLibraryService`: `SaveAlerts` / `LoadAlerts` added — alert definitions now survive app restarts via `alerts.json`.
- `AlertOrchestrator`: restores alerts from library on construction; saves after every `AddAlert` / `RemoveAlert`.
- `SeriesManagementService`: calls `PersistWorkspace()` after `RegisterSeries` and after `ChartCommandManager` removes a series. Workspace restored via `WorkspaceInitializer` from `"default"` profile on startup.
- `WorkspaceConfiguration.Series` changed from `List<ChartSeries>` to `List<SeriesConfig>` to prevent serialising data arrays to disk.

### Tests Added
- `ReferenceLevelTests.cs` — 28 tests covering `IndicatorReferenceLevels.GetLevels` for all indicator families, case-insensitivity, non-oscillators returning empty, and `SeriesManagementService.RegisterSeries` level injection for RSI, MACD, CCI, SMA.
- `BackfillManagerTests.cs` — 5 tests: queue acceptance, successful fetch persists bars + publishes `BACKFILL_COMPLETE`, empty fetch writes nothing, fetch failure doesn't kill processing loop, dispose cancels cleanly (SQLite in-process via temp file).
- `ViewportRangeCalculatorTests.cs` — 8 tests: guard cases, main pane range, RSI pane level expansion (OB/OS always on-screen), MACD zero-line always on-screen, hidden levels not expanding range, levels-only pane range, two separate panes independent ranges, two RSI instances sharing a pane with unified range.

**Build:** 0 errors, 0 warnings. **Tests:** 69/69 passing.

---

## [2026-03-28] — Audio, Heatmap, Heikin-Ashi & Candle Color Fixes

### Fixed: Heatmap Arrow-Key Navigation Returning "No Data"
- **Root cause (original fix):** `BinnedNavigationStrategy.NavigateY` searched backwards from `CurrentDataIndex` for a non-empty heatmap snapshot. When the cursor is in historical bars (before the live session), no snapshot is found going backwards, returning a "No data" error despite visible heatmap data at recent bars.
- **Fix (original):** Changed to `LastOrDefault(l => l?.Count > 0)?.Count ?? 0` — always uses the most recent live snapshot's bin count.
- **Fix:** `NavigationFeedbackManager.FindNearestHeatmapIndex` now also searches forward from the cursor if the backwards pass finds nothing, so the nearest live snapshot is always found for speech formatting.
- **Root cause (second fix):** `IndicatorOrchestrator.RecalculateLastAsync` was overwriting `HeatmapData[^1]` with an empty bin list on every tick where `GetOrderBookAsync` returned no data. This reset a previously-populated snapshot to empty, causing the next navigation attempt to see all-empty HeatmapData and report "No data".
- **Fix:** `RecalculateLastAsync` now only overwrites `HeatmapData[^1]` when `lastBarBins.Count > 0`. If the order book is momentarily unavailable (empty bids/asks), the existing live snapshot is preserved rather than reset to empty.

### Fixed: Wick Solo Playback (Ctrl+Shift+Space) Producing No Sound
- **Root cause:** `AudioSequencer.StartPlaybackAsync` called `SetVoice` with `durationSeconds = 0.0` for all components. Ping-envelope voices (wicks) require a non-zero duration to produce a ring — with 0.0 the envelope completes instantly, producing silence.
- **Fix:** Ping envelopes now receive `durationSeconds = min(0.15, msPerBar × 0.8 / 1000)`. At default 1× speed this is 80ms; at faster speeds it caps to prevent stacking overlapping pings. Applied to both `StartPlaybackAsync` and `StartMultiSeriesPlaybackAsync`.

### Fixed: Wick Pitch Reverted to Fixed Tones (880 Hz / 220 Hz)
- User preference: consistent identifiable tones per wick type are more useful than price-relative pitch during playback.
- `SonificationProfileProvider`: wick profile reverted from `PitchMapping.Price` to `PitchMapping.None`.
- `DefaultSonificationStrategy.CreateAudioPoint`: when component role/displayType is Wick, overrides frequency to **880 Hz (upper wick)** or **220 Hz (lower wick)** based on `comp.Name`, regardless of PitchMapping. `FreqMultiplier` still applied so per-component tuning via Properties dialog works.

### Fixed: Wick Clipping Candle Bodies During Series Playback
- The Ping duration fix above (0.0 → proper duration) eliminates the Dirac-click artifact that caused wicks to "clip" when simultaneous with the Sustain body voice.

### Fixed: Alt+C / Alt+L Toggle Speech Announcements Missing
- `AccessibilityFeedbackCoordinator.OnStateChanged` now checks `IsHeikinAshi` and `IsLogScale` state changes and announces **"Heikin-Ashi candles" / "Standard candles"** and **"Log scale" / "Linear scale"** respectively.
- These checks (and the existing F2/F3 checks) are moved BEFORE the `IsPlaying` gate so toggle feedback fires even during chart playback.

### Fixed: Heikin-Ashi Navigation Speech Using Raw OHLC Values
- When `state.IsHeikinAshi` is true, `NavigationFeedbackManager.HandleNavigationFeedback` now computes the HA-transformed bar for the current index using `ChartMath.CalculateHeikinAshi` before passing it to the formatter. Spoken O/H/L/C values now match what the user sees on screen.

### Fixed: Heatmap Speech Using Profile Code Path ("No data" on bin navigation)
- **Root cause:** `NavigationFeedbackManager.HandleNavigationFeedback` checked `isProfile` before `isHeatmap` in the speech-formatting block. Because `IndicatorModelFactory` sets `IsProfile = true` for heatmap series (so `meta.Code == "HEATMAP"` triggers the same flag as volume profiles), heatmaps entered the profile branch, which checks `s.Data.ProfileBins.Count` — always 0 for heatmaps — and spoke "No data".
- **Fix:** Swapped the if/else-if order in `NavigationFeedbackManager` so `isHeatmap` is checked first. Heatmaps now correctly enter the heatmap speech path (`FormatHeatmapFeedback`). Profiles are unaffected as they never have `isHeatmap = true`.

### Fixed: Heikin-Ashi Navigation Sonification Not Reflecting HA Values
- **Root cause:** `NavigationSonifier.SyncNavigationSlots` passed `state.Data[idx]` (raw bar) to `CreateAudioPoint`. When HA mode is active, the raw bar's close/open values differ from the HA-transformed values — resulting in the wrong pitch direction being played (e.g., a HA bullish candle sounding bearish because the raw bar closed down).
- **Fix:** Added `using AccessibleTrader.Core.Services` to `NavigationSonifier.cs`. In `SyncNavigationSlots`, when `state.IsHeikinAshi` is true, the code now computes `ChartMath.CalculateHeikinAshi(rawSlice)` for the current index and uses the resulting `navPoint` (HA bar) as the audio source. The `PitchMapping.Direction` now reflects HA candle direction, matching both speech and visual output.

### Improved: Candle Body Colors Per-Indicator in Properties Dialog
- `StandardRenderers.RenderCandles`: body color now reads from the Candle Body `ComponentConfig.ColorHex` (bullish) and `ComponentConfig.ColorHexSecondary` (bearish) instead of hardcoded `SKColors.Green` / `SKColors.Red`.
- `PropertiesModal.razor` Appearance tab: Candle display-type components now show separate **Bullish Color** and **Bearish Color** pickers (using `ColorHex` and `ColorHexSecondary`). All other component types show a single Color picker as before.
- `SettingsModal.razor`: The read-only candle color swatches are replaced with a note directing users to the Properties dialog (Shift+F12) where colors are actually editable.

### Improved: Indicator Detail Narratives (Ctrl+Shift+D)
- `SkenderIndicatorProvider.GetDetailFact`: Added rich narratives for **STOCH/StochRSI** (K%, D%, overbought/oversold zone, K/D crossover), **VWAP** (value, price deviation %, rising/falling), and **ATR** (value, % of price, volatility expanding/contracting/stable).
- `BarDetailService`: Now injects `IIndicatorService` and calls `GetDetailFact` for indicator series, falling back to raw component values only if no narrative is produced. Candle series returns its rich candle breakdown immediately (no raw values appended).

---

## [2026-03-26] — Improvement Plan Session: Phases 0–4

### Documentation (Phase 0)

- **README.md overhauled:** Corrected rendering stack description (SkiaSharp on SKCanvasView, not HTML5 Canvas), updated provider list to all six plugins, updated shortcut reference to point to HelpModal (Alt+H), added EventBus/Rx quick reference, updated current status section.
- **CODEBASE_KNOWLEDGE_BASE.md rewritten:** Added authoritative EventBus vs Rx decision table (Section 5). Corrected rendering technology (SkiaSharp, not HTML5 Canvas). Added navigation sonification single-path rule (Section 7). Updated initialization order. Added improvement plan phase reference.
- **PLATFORMS.md updated:** Corrected rendering entry, updated audio platform status (WASAPI=complete; AudioTrack/AVFoundation=stub). Updated compatibility matrix with accurate platform status. Added Phase 5 roadmap section.
- **TODO.md restructured:** All items organized by improvement-plan phase (0–4 active, 5–7 roadmap). All previously completed items marked `[x]`. Phase 5–7 items documented as roadmap intent.
- **HelpModal.razor enriched:** Combined keyboard reference (from SHORTCUTS.md) with conceptual User Guide content (soundscape understanding, Volume Profile navigation, drawing tool workflows, indicator customization). Help button and modal retained.
- **GEMINI.md:** Retained as AI assistant context file (not project documentation).
- **Stub annotations added:** `BlazorAudioDriver.cs` (#else block), `AppDelegate.cs` (Mac keyboard), `CoinbaseProvider.cs` (trading auth) — all annotated as Phase 5 roadmap items.

### Phase 1 — Accessibility Path Bug Fixes

#### Fixed: Dual Navigation Sonification Path (Click/Pop + Race Condition)
- **Root cause:** `SonificationManager.SyncNavigationSlots` (Path 1, voice slot 0, 0.4s) AND `NavigationFeedbackManager.SonifyCurrentContext` (Path 2, voice slot 0, 0.2s) both wrote to the same DSP voice slot. Path 2 called `_audioRouter.Silence()` first, killing Path 1's note mid-duration.
- **Fix:** Removed `SonifyCurrentContext()`, `_lastLeadingEdgeSonify` field, and `_audioRouter` constructor dependency from `NavigationFeedbackManager`. The class now handles SPEECH ONLY. Navigation audio is exclusively owned by `SonificationManager` → `NavigationSonifier.SyncNavigationSlots()`.
- Updated 5 test call sites in `AccessibilityPipelineTests`, `UIDiagnosticTests`, `RobustnessTestSuite`, and `AudioDiagnosticTests` to remove the now-removed `audioRouter` constructor parameter.

#### Noted: AudioEngine Already Has 5ms Attack/Release
- `AudioEngine.cs` ENVELOPE_SAMPLES = 220 (~5ms at 44100 Hz) already provides attack/release for non-continuous voices. The `continuous: false, 0.4s` design in `SyncNavigationSlots` is intentional — keydown repeat (~30ms) refreshes the note before it terminates, and the last note fades naturally. No change required.

#### Fixed: Chart-Focus Gate — Navigation Keys Leaking into Modals
- Added `_isChartActive` boolean flag to `CommandDispatcher`. Subscribes to `ChartFocusEvent` (set true) and `DeactivateEvent` (set false with 50ms debounce to prevent the keydown/blur race). Navigation, playback, and drawing commands are gated behind this flag. Global commands (F1–F8, modal opens, volume) bypass the gate. Starts `true` so keyboard navigation works from app start without requiring explicit focus.
- Added `IDisposable` implementation to `CommandDispatcher` (cleans up EventBus subscriptions).
- Added `IsDrawingCommand()` helper method alongside existing `IsNavigationCommand()` and `IsPlaybackCommand()`.

#### Added: Loading-State Speech — InitializationStatus.Ready Announcement
- `AccessibilityFeedbackCoordinator.OnStateChanged` now tracks `InitStatus` changes.
- On `Loading → Ready`: speaks `"{Symbol} on {Provider}, {Timeframe}. Ready."` (or "Chart ready." if identity not set).
- On any `→ Error`: speaks "Chart failed to load."

### Phase 2 — Data Pipeline Bug Fixes

#### Fixed: PlaybackScope Not Differentiated (Component = Series)
- Added `componentFilter` parameter to `IAudioSequencer.StartPlaybackAsync` and `AudioSequencer.StartPlaybackAsync`.
  - `-1` = play all visible components (Series scope).
  - `n` = play only component at index `n` (Component scope).
- `PlaybackOrchestrator.StartPlayback` now passes `componentFilter = FocusedComponentIndex` for `PlaybackScope.Component`, and `-1` for `Series`. Chart scope anchors to `CoreSeriesIds.Candles` starting from `ViewportStartIndex`.

#### Fixed: No Feedback at Data Boundary (NAV_LEFT/RIGHT on Edge Bars)
- Added `FeedbackType.Boundary` to the `FeedbackType` enum.
- `NavigationEngine.NavigateX`: when `strategy.NavigateX` returns `Success = false` (cursor already at data edge), publishes `FeedbackRequestEvent(FeedbackType.Boundary)`.
- `AccessibilityFeedbackCoordinator.OnFeedbackRequest`: handles `Boundary` by calling `_audioRouter.PlayEarcon(FeedbackType.Boundary)` — no speech, earcon only per user preference.
- `AudioFeedbackRouter.PlayEarcon`: maps `FeedbackType.Boundary` → `IEarconService.PlayBoundary()`.

#### Verified: Indicator Pipeline Timing
- Confirmed `DataOrchestrationService` subscribes to `IndicatorUpdatedEvent` → `OnDataUpdated(forceFull: true)` and to `StateStream` with `InitStatus == Ready` → `OnDataUpdated(forceFull: false)`. Pipeline correctly wired.

### Phase 3 — Structural Cleanup

#### Voice Slot Layout Documented
- Added authoritative slot-range comment block to `NavigationSonifier.cs` documenting the 64-voice slot layout (0–7 navigation, 8–15 reserved, 16–31 UI earcons, 32–63 playback sequencer). Ensures future code never creates slot collisions.

#### NAudio Audit — Clean
- Confirmed `NAudio.Wasapi` package only exists in `AccessibleTrader.BlazorClient.csproj` with `Condition="...=='windows'"`. Zero references in `AccessibleTrader.Core`. No changes required.

#### EventBus Rationalization — Audit Passed
- Full audit of all `IEventBus.Subscribe<T>` and `IEventBus.Publish<T>` call sites. All usages categorized as modal lifecycle, cross-layer fire-and-forget, or one-shot notifications — all architecturally appropriate on EventBus. No migrations required.

#### HelpModal + User Guide Consolidation (completed in Phase 0)
- Documented under Phase 0 above. Already included conceptual sections and full keyboard reference.

### Phase 4 — SRP Structural Clarity

#### WorkspaceStore.Reduce — Domain Section Comments
- Added domain-section comment headers to the `Reduce` switch expression: `IDENTITY/MODE`, `DATA`, `NAVIGATION`, `PLAYBACK`, `ACCESSIBILITY/SETTINGS`, `SERIES FOCUS`, `SERIES VISIBILITY/AUDIO`, `PLAYBACK STATE`, `CHART DISPLAY`, `SERIES MANAGEMENT`, `INITIALIZATION`, `USER SETTINGS`, `VOLUME`.
- Added XML doc comment to `Reduce()` explaining the delegation pattern and domain ownership.

#### SkenderIndicatorProvider — Responsibility Documentation
- Added class-level XML doc comment explaining the three co-located responsibilities (Discovery, Invocation, Mapping) and why they're co-located (tight Skender type coupling).
- Documents the extraction path (IndicatorMetadataScanner + SkenderResultMapper) for when a second provider is added.

#### DrawingService — Extensibility Note
- Added class-level XML doc comment noting the current switch-dispatch approach and the extraction threshold (when to consider IDrawingCalculator strategy).

#### CommandDispatcher — Already Improved in Phase 1
- Phase 1 additions (chart-focus gate, `IDisposable`, numbered section comments, `IsDrawingCommand` helper) constitute the Phase 4 structural improvements for this class.

---

## [2026-03-26] — Phase 5–6: Audio Fixes, Provider Completion, Trading Dashboard

### Phase 5 — Audio, Shortcuts, Reference Lines

#### Fixed: Playback Glide (No Clicks Between Notes)
- `AudioSequencer.PlayAsync`: changed `SetVoice` call from `continuous: false, duration: 0.1` to `continuous: true, duration: 0.0`. With `continuous: true` the AudioEngine skips the envelope attack/release restart, letting `GLIDE_FACTOR` smoothly converge frequency/volume between bars. Eliminates the 5ms silence click that occurred at each bar transition during Space playback.

#### Fixed: Navigation Note Duration (No More Sustained Drone)
- `NavigationSonifier.SyncNavigationSlots`: reduced duration from `0.4s` to `0.15s`. Held-arrow key gives rapid staccato movement rather than an extending drone. Notes for Home/End/PgUp/PgDn feel crisp as single-fire 0.15s tones.

#### Added: Drawing Shortcuts in HelpModal
- Added 9 missing drawing-tool shortcuts: `Ctrl+Shift+E` (Fib Extension), `Ctrl+Shift+J` (Angle Fib), `Ctrl+Shift+R` (Rectangle), `Ctrl+Shift+A` (Andrews Pitchfork), `Ctrl+Shift+G` (Gann Fan), `Ctrl+Shift+B` (Gann Box), `Ctrl+Shift+P` (Risk/Reward), `Ctrl+Shift+W` (Anchored VWAP), `Ctrl+Shift+M` (Measure tool).

#### Added: Alt+B — Order Book toolbar button & shortcut
- `SystemCommand.OpenOrderBook` added to `SystemCommand` enum.
- `OpenOrderBookEvent` record added to `Events.cs`.
- `ShortcutManager`: `Alt+B` binds to `OpenOrderBook`.
- `CommandDispatcher`: dispatches `OpenOrderBookEvent` for `OpenOrderBook`.
- `Toolbar.razor`: "Order Book" button publishing `OpenOrderBookEvent`.
- `OrderBookModal.razor`: new accessible modal with `role="dialog"`, spread summary (`aria-live="polite"`), two `role="table"` sections (Bids/Asks with `<thead>/<tbody>/<th scope="col">`), depth gradient background (visual only), green bids / red asks, Refresh button. Loads via `IOrderExecutionService.GetOrderBookAsync`.
- `MainLayout.razor`: `<OrderBookModal />` registered.
- `HelpModal.razor`: `Alt+B → Open Order Book` added to UI & Settings shortcut table.

#### Added: RSI/MACD/Stochastic Reference Lines Auto-Injected on Indicator Add
- `SeriesManagementService.RegisterSeries`: calls `InjectDefaultLevels()` after creating a `ChartSeries`.
- `InjectDefaultLevels`: RSI/MFI/WILLR/STOCH/STOCHRSI → overbought (70, red dash), midpoint (50, gray dot), oversold (30, green dash). MACD/MOMENTUM/ROC/CCI/DPO/CMO/PPO/AROONOSC/ULTOSC → zero-line (gray dash). AROON → 50 midpoint. PERCENTB/BOLLINGERPERCENTB/BBP → 1.0, 0.5, 0.0 levels.

### Phase 6 — Provider Completion, Trading Dashboard

#### Added: Spot/Futures Sub-Type Toolbar Dropdown
- `MarketOrchestrator`: `IMarketOrchestrator` extended with `SelectedSubType`/`AvailableSubTypes` properties. `RefreshSymbolsAsync` calls `GetSupportedSubTypesAsync`, populates `_availableSubTypes`, builds `marketKey = "{market}|{subType}"` when count > 1.
- `Toolbar.razor`: conditional sub-type dropdown shown only when `AvailableSubTypes.Count > 1` (between Provider and Symbol selects). `OnSubTypeChanged` handler calls `RefreshSymbolsAsync`.
- `LoadChartAsync`: uses `marketForIdentity = "{market}|{subType}"` when multiple sub-types exist.

#### Added: Trading Dashboard — Margin Type + Leverage + Accessible Order Book
- `IOrderExecutionService` + `GeneralOrderService`: added `SupportsMarginTradingAsync(provider)`.
- `TradingDashboardModal.razor`:
  - Margin type (Cross/Isolated) and leverage multiplier inputs — shown only when `_supportsMargin = true`.
  - `Take Profit` field added to order entry form.
  - Order book panel replaced with `role="table"` ARIA markup (was non-semantic `<div class="book-row">`). Spread shown with `aria-live="polite"`. Loading state announced.
  - `SubmitOrder` now passes `TakeProfit`, `Leverage`, `SubType` (from `Store.State.Identity.Market`), and `MarginType` in `TradeSignal`.
- `TradeSignal` record: added `SubType` (nullable, routes Spot vs Futures) and `MarginType` (nullable, "Isolated"/"Cross").

#### Fixed: Binance Futures Order Placement
- `BinanceProvider.PlaceOrderAsync`: routes to `UsdFuturesApi.Trading.PlaceOrderAsync` when `signal.SubType == "Futures"`. Sets leverage before placing if `signal.Leverage` is specified. Attaches a separate take-profit stop order if `signal.TakeProfit` is set. Spot orders unchanged.

#### Fixed: Alpaca Live Updates — Polling → WebSocket
- `AlpacaProvider.SetSubscriptionAsync`: replaced 15-second REST polling timer with Alpaca v2 WebSocket (`wss://stream.data.alpaca.markets/v2/stocks` / `v1beta3/crypto/us`). Authenticates with key/secret, subscribes to minute bars. Data receive loop pushes `Ohlcv` to `_liveStream`.
- `AlpacaProvider`: added trading update WebSocket (`wss://paper-api.alpaca.markets/stream`) subscribing to `trade_updates`. Order fills/cancels/rejects push `OrderUpdate` to `OrderUpdateStream` (was stub).

#### Added: Volume Bars Colored by Candle Direction
- `StandardRenderers.RenderDirectionalBars`: new method renders volume bars green (Close >= Open) or red (Close < Open) using the corresponding OHLCV bar from `ctx.Data`.
- `DataLayer.Render`: volume series (`s.Id == CoreSeriesIds.Volume`) uses `RenderDirectionalBars` instead of generic `RenderBars`.

---

## [2026-03-27] — Bug-Fix Session #2: Wick Playback, Prepend Indicator Flattening, Profiles on Pan/Zoom, Heatmap Order Book

### Fixed: Wick Components Silent During All Playback Modes (Space / Shift+Space / Ctrl+Shift+Space)

**Root causes (two separate bugs):**

1. **Ping envelope blocked by `continuous: true`:** Wick components use `EnvelopeType = "Ping"` (short transient) but the sequencer called `SetVoice` with `continuous: true` on every bar. `continuous: true` tells the AudioEngine to update frequency/volume *without restarting the envelope*. Since the ping decays in ~50 ms, all bars after the first were sent to an already-silent voice — no restart, no sound.

2. **Candle body too quiet:** `AmplitudeMapping.Size` computed `vol = (|close - open| / absMaxPrice) * 2.0`. For BTC at ~$83,000 with a $100 body, vol ≈ 0.0024, always clamped to the 5% floor. At 5% volume the body tone is nearly inaudible.

**Fixes:**
- `AudioSequencer.StartPlaybackAsync` and `StartMultiSeriesPlaybackAsync`: changed `continuous` from hardcoded `true` to `audioPt.EnvelopeType != "Ping"`. Ping-enveloped components (wicks) now restart their transient on every bar. Sustain-enveloped components (lines, bodies, histograms) still glide to avoid attack-restart clicks.
- `SonificationProfileProvider`: changed candle-body profile from `AmplitudeMapping.Size` to `AmplitudeMapping.None` so the body always plays at full `baseVolume`. The bullish/bearish pitch distinction (440 Hz vs 220 Hz square) is preserved via `PitchMapping.Direction`.
- `AudioSequencer.StartPlaybackAsync`: added null guard `series.Pane ?? ""` when looking up pane range to prevent `ArgumentNullException` on series with a null Pane property.

**Files:** `AccessibleTrader.Core/Services/Audio/SonificationProfileProvider.cs`, `AccessibleTrader.Core/Services/Audio/AudioSequencer.cs`

---

### Fixed: Indicator Data Reverts to Flat Line After Loading Historical Bars (Prepend)

**Root cause:** `DataOrchestrationService.OnDataUpdated()` (no-arg, event handler) unconditionally called `OnDataUpdated(forceFull: false)`. When `HistoryBufferCoordinator` prepends older bars, all existing component data arrays are indexed against the old start position. Running `RecalculateLastAsync` (incremental) after a prepend only recalculates the last bar — the pre-existing indicators for the original bars stay but are now at the wrong array offsets, appearing as a flat line when rendered.

**Fix:** `OnDataUpdated()` now reads `_dataManager.Data` before dispatching, compares `data[0].Date` to `_lastFirstBarDate`, and sets `forceFull = true` when a prepend is detected (new first-bar date is earlier than the previous one). `_lastFirstBarDate` is updated on each call.

**File:** `AccessibleTrader.Core/Services/DataOrchestrationService.cs`

---

### Fixed: Volume Profile / Market Profile Not Updating on Pan or Zoom

**Root cause:** The StateStream subscription in `DataOrchestrationService` always called `OnDataUpdated(forceFull: false)` for all viewport changes. `RecalculateLastAsync` skips profile series (`if (isProfile || s.IsDrawing) continue`). Profiles (VPVR/TPO) slice the data to `[ViewportStartIndex … ViewportStartIndex + ViewportLength]`, so they must recalculate whenever the visible window changes. With `forceFull: false` they never recalculated on pan or zoom.

**Fix:** The StateStream subscription now checks `_store.State.ActiveSeries.Any(s => s.IsProfile && !s.IsDrawing)`. If any profile series is active, `forceFull: true` is passed, triggering `RecalculateAllAsync` which re-slices the data for the current viewport window.

**File:** `AccessibleTrader.Core/Services/DataOrchestrationService.cs`

---

### Fixed: Heatmap Never Populated (Order Book History Starved)

**Root cause:** The heatmap series starts with `HeatmapData.Count == 0`. The old `needsFull` check included `s.Data.HeatmapData.Count == 0` as a trigger for `RecalculateAllAsync`. This meant every live tick took the `needsFull = true` branch, calling `RecalculateAllAsync` (which uses `_bookHistory.GetHistory` — empty at first). The `GetOrderBookAsync()` call that fed the history service was in the `else` (incremental) branch, which was **never reached**. The history service accumulated no snapshots, so `GetHistory` always returned empty lists, so the heatmap stayed blank in a permanent loop.

**Fixes:**
- `GetOrderBookAsync()` is now called at the top of `OnDataUpdated(bool)`, before the `needsFull` branch decision. The live snapshot is added to `_bookHistory` before calling `RecalculateAllAsync` on full-recalc paths (so `GetHistory` includes the latest snapshot). Incremental paths continue to let `RecalculateLastAsync` add the snapshot.
- `needsFull` check now excludes profile/heatmap series from the "empty data" trigger (`!s.IsProfile` guard added). Non-profile indicators without data still trigger a full recalc as before.

**File:** `AccessibleTrader.Core/Services/DataOrchestrationService.cs`

---

### Build: 0 errors, 0 warnings. Tests: 21/21 passing.

---

## [2026-03-27] — Bug-Fix Session: Sonification, Live Bars, Wick Playback & Modal Visibility

### Fixed: Profile Sonification Not Firing on Arrow-Key Bin Navigation
- **Root cause:** `SonificationManager`'s StateStream subscription only checked `indexChanged || focusChanged` to call `SyncNavigationSlots`. `FocusedBinIndex` changes from `SelectBinAction` (fired by `NavigationEngine.NavigateY` on profile series) were not included in the condition, so `SyncNavigationSlots` — and therefore `NavigationSonifier.SonifyProfile` — never fired when navigating bins with Up/Down arrows.
- **Fix:** Added `bool binChanged = state.FocusedBinIndex != _currentState.FocusedBinIndex` to `SonificationManager`'s state-change handler. Added `binChanged` to the `SyncNavigationSlots` trigger condition: `(indexChanged || focusChanged || binChanged)`.
- **File:** `AccessibleTrader.Core/Services/SonificationManager.cs`

### Fixed: Live/Intra-Bar Component Arrays Not Updated
- **Root cause:** `WorkspaceStore.UpdateData` only rebuilt component data arrays (Open, High, Low, Close, Volume) when `currentData.Length != list.Count`. For live ticks where `DataManager.ReplaceLast` updates the current bar without changing the count, the sync was entirely skipped — leaving component arrays holding the previous bar's values.
- **Fix:** Added an `else if (!initial && list.Count > 0)` branch: clones the current data array, updates only `arr[^1]` with `ExtractValue(list[^1], c.DataMapping)`, and stores the result. Intra-bar replacements now propagate to the component arrays used by the renderer and speech system.
- **File:** `AccessibleTrader.Core/Services/WorkspaceStore.cs`

### Fixed: Wicks (and All Components) Not Sonified During Playback
- **Root cause:** `AudioSequencer.StartPlaybackAsync` and `StartMultiSeriesPlaybackAsync` called `_strategy.MapToAudio()` once per series, which always picks the **first visible component** and returns a single `AudioPoint`. That same audio point was then applied to every voice slot (`PlaybackSlotOffset + cIdx`), making all components (body, high wick, low wick, open line) sound identical and the layering inaudible.
- **Fix 1:** Added `MapComponentToAudio(series, componentIndex, dataIndex, data, relativeIndex, viewportWidth, viewportRange, chartVolume)` to `ISonificationStrategy` and implemented it in `DefaultSonificationStrategy`. This method maps the component at the given index, reading from that component's specific data array (falling back to OHLCV field via `comp.DataMapping` for price-mapped series like candle wicks).
- **Fix 2:** Updated both playback loops in `AudioSequencer` to call `_strategy.MapComponentToAudio(series, cIdx, ...)` instead of the single `MapToAudio`. Each component now produces its own frequency, amplitude, and waveform independently.
- **Files:** `AccessibleTrader.Core/Services/Audio/ISonificationStrategy.cs`, `AccessibleTrader.Core/Services/Audio/AudioSequencer.cs`

### Fixed: Modal Overlay Z-Order / Chart Disappearing
- **Root cause:** Fixing the XAML Grid layer order (WebView bottom, SkiaCanvas top) made the Skia canvas cover all modals. The previous fix swapped them (Skia bottom, WebView top) but the WinUI 3 compositor does not correctly compose the Skia `SwapChain` surface with a transparent `WebView2` above it, resulting in an all-black chart.
- **Fix:** Reverted `MainPage.xaml` to original order: `BlazorWebView` first (bottom, z-index 0), `SKCanvasView` second (top, z-index 1). The Skia canvas renders correctly on top. Modals are now surfaced by **hiding the canvas** when any modal opens and **restoring it** when the last modal closes.
  - Added `ModalStateChangedEvent(bool IsOpen)` to `Events.cs`.
  - All 11 modal components (`SettingsModal`, `ApiKeysModal`, `HelpModal`, `AlertsModal`, `AddIndicatorModal`, `OrderBookModal`, `ObjectTreeModal`, `DrawingToolsModal`, `PropertiesModal`, `StrategyModal`, `TradingDashboardModal`) now publish `ModalStateChangedEvent(true)` in their `ShowAsync()` and `ModalStateChangedEvent(false)` in their `Close()`.
  - `MainPage.xaml.cs` subscribes to `ModalStateChangedEvent` with an `_openModalCount` reference counter: hides canvas on first modal open, restores on last modal close. Handles nested modal sequences (e.g. Properties → AddIndicator) without flickering.
- **Files:** `AccessibleTrader.Core/Models/Events.cs`, `AccessibleTrader.BlazorClient/MainPage.xaml`, `AccessibleTrader.BlazorClient/MainPage.xaml.cs`, all 11 modal `.razor` files.

### Known Remaining Issue: Live Bar Initial 0.00
- `state.Data[currentIdx].Close` reads from the raw `ImmutableList<Ohlcv>` (not component arrays). If the value is 0.0, the provider's WebSocket message parser is producing a zero-value OHLCV bar from a subscription-confirmation message. This is a per-provider issue to be fixed in each plugin's WebSocket handler (filter non-data frames before attempting OHLCV parse). Not addressed this session.

### Known Remaining Issue: Historical Order Book / Heatmap
- Bitstamp (and essentially all retail-tier exchanges) do not expose historical L2 order book data via REST. The `HeatmapData` buffer can only be populated from the current session's live order book snapshots. This is a design constraint, not a bug. The heatmap correctly renders "no data" for bars before the session start. Documentation updated.

### Build: 0 errors, 0 warnings. Tests: 21/21 passing.

---

## [2026-03-25] — Bug-Fix Sprint: Navigation, Audio, Data, & Modals

### Fixed
- **Double-announcement race (navigation):** Removed the `HandleNavigationFeedback` call from `AccessibilityFeedbackCoordinator.OnStateChanged`. Navigation feedback is now exclusively driven by `FeedbackRequestEvent`, eliminating the second call that interrupted the first announcement mid-sentence.
- **F2/F3 toggle flags not syncing:** After each speech/sonification toggle announcement, `_navManager.IsSpeechEnabled` and `_audioRouter.IsSonificationEnabled` are now synced from the store state in `OnStateChanged`. Prevents the coordinator announcing toggles but navigation paths still playing/speaking as if the toggle never happened.
- **F4 context summary appending series name:** `FeedbackType.Info "CONTEXT_SUMMARY"` format string was `"{symbol}{provider}, {timeframe}, {seriesName}"`. Removed `seriesName` — F4 now speaks symbol, provider, and timeframe only.
- **`SonifySeries`/`SonifyComponent` ignoring sonification toggle:** Both methods in `AudioFeedbackRouter` were missing the `IsSonificationEnabled` guard. Added early-return when sonification is disabled.
- **Stuck navigation note on key release:** Introduced `NavKeyReleasedEvent` published from `GlobalInputService.OnKeyUp` (new `[JSInvokable]` method). `SonificationManager` subscribes and calls `_navigation.StopNavigationVoice()` on receipt. `SyncNavigationSlots` changed from `continuous: true, 0.2s` to `continuous: false, 0.4s` as a self-terminating fallback if keyup events are missed.
- **Modal z-index below chart:** `.modal-overlay` z-index raised from 1000 to 9999 in `app.css`.
- **`PlayStop` command unbound:** `Shift+Escape` added to `ShortcutManager` defaults. `PlayStop` case added to `CommandDispatcher.HandlePlayback`.
- **`PrependOlderDataAsync` not triggering indicator recalculation:** Added `NotifyDataUpdate(false)` after a successful prepend in `DataManager`. Added `SetDataStatusAction(DataStatus.Ready)` dispatch in the `finally` block when status is still `LoadingHistorical` after prepend completes.
- **`IsSonificationEnabled` guard missing on series/component sonification paths:** Fixed in `AudioFeedbackRouter.SonifySeries` and `SonifyComponent`.

### Added
- **`NavKeyReleasedEvent`** in `Core/Models/Events.cs` — published on keyup of arrow keys; consumed by `SonificationManager` to stop the navigation voice slot.
- **`INavigationSonifier.StopNavigationVoice()`** — new interface method and implementation in `NavigationSonifier`; stops voice slot 0 (navigation) immediately on keyup.
- **`GlobalInputService.OnKeyUp(string key)`** — new `[JSInvokable]` method; wired from JS `keyup` event listener on `ArrowLeft/Right/Up/Down`.
- **JS keyup listener** in `keyboard.js` — publishes keyup for arrow keys to `GlobalInputService.OnKeyUp` via DotNet reference.

### Tests Updated
- `MockNavigationSonifier` updated: added `StopNavigationVoice()` stub to satisfy new interface member.
- `MockLiveStreamManager` updated: overrides `LiveStream` as `ChannelReader<Ohlcv>` (was `IObservable<Ohlcv>`) to match updated base class.
- `IntegrationDiagnosticTests.System_ShouldRespondToNavigationFeedbackEvents`: renamed (was `...StateChanges`); now publishes `FeedbackRequestEvent` directly — validates the single authoritative feedback path instead of the removed `OnStateChanged` double-dispatch.

---

## [2026-03-25] — Phase 2: Heatmap/Profile Sonification & Speech Overhaul

### Added
- **`ProfileBinClassifier`** (`Core/Services/Accessibility/`): New single-responsibility helper for bin classification. Classifies `ProfileBin` as `LVN / Normal / ValueArea / VAL / VAH / HVN / POC`. Exposes `GetBasePitch()`, `GetWaveform()`, `ShouldTriggerClick()`, `GetDuration()`, `GetLabel()`, `GetYMultiplier()`.

### Fixed
- **Profile Sonification:** Node-type-based pitch system (no Y-axis pitch shift). POC click transient. Volume = amplitude normalized to session max.
- **Heatmap Sonification:** Sawtooth waveform, global-range Y→pitch multiplier (0.5×–2.0×).
- **Profile X-Navigation:** Silent no-op for left/right on all Profile-type series.
- **HOME/END/`\` IsXMove fix:** Bar at destination is announced; no meta-prefix spoken.
- **Viewport Announcement Policy:** Zoom announces, pan announces, cursor jumps suppress viewport description.
- **Series Switch Announcement:** Includes hidden/muted state suffix and correct bin count.
- **Profile/Heatmap Speech Format:** Node labels, formatted volumes, percentages, TPO letter chains.
- **NAV_MOVE chatter:** `NavigationResult.FeedbackMessage` default changed to `null`.
- **F2/F3 confirmation speech:** Toggle announcements fire in `AccessibilityFeedbackCoordinator.OnStateChanged`.
- **F4 wired:** `"CONTEXT_SUMMARY"` → `FeedbackType.Info` in `CommandDispatcher`.
- **F5-F7 volume speech:** `FeedbackType.VolumeChange` handled in `OnFeedbackRequest`.
- **Series nav shortcuts corrected:** Page Up/Down = series; Up/Down arrows = component.
- **SonifyHeatmap null safety:** Guarded `SelectMany` against null inner lists.

### Removed
- **`ChartStateCoordinator.cs`:** Deleted — dead code never registered in DI.

---

## [2026-03-25] — Refactor 2026: Pull-Architecture & Zero-Allocation DSP Phase

### Added
- **Architectural Shift:** Commenced transition to Pull/Stream data model using `System.Threading.Channels`.
- **Speech Template Engine:** Decoupled verbal feedback into template-driven engine for customizability.
- **Playback Controls (Corrected):** Defined logic for `Space` (Chart), `Shift+Space` (Series), and `Ctrl+Shift+Space` (Component) playback.
- **F-Key Protocol:** Standardized F2 (Speech), F3 (Sound), F4 (Context), and F5-F7 (Volume Cycles).

### Fixed
- **Navigation Chatter:** Removed "NAV_MOVE" and other technical IDs from the speech output.
- **Zoom/Pan Feedback:** Standardized to "Viewing X bars from [Date] to [Date]".
- **Home/End/`\`:** These now only announce the target bar data.
- **Build & Test:** Resolved all .NET 10 compilation errors and updated the 21-test diagnostic suite.

---

## [2026-03-22] — High-Performance Refactoring & Professional Drawing Suite

### Added
- **Professional Drawing Suite:** Risk/Reward, Anchored VWAP, Andrews' Pitchfork, Gann Fan & Box, Measure Tool, Angle Fibs.
- **Interactive Mouse Support:** JavaScript bridge for mouse coordinates on chart canvas.
- **Enhanced Indicator Taxonomy:** 60+ indicators categorized into Trend, Momentum, Volatility, Volume, Profiles.

### Improved
- **Zero-Allocation Data Pipeline:** `ComponentConfig.Data` from `List<double?>` to `double[]` with `double.NaN`.
- **Dynamic Series Naming:** Navigation speaks full context, e.g., "MACD 12, 26, 9 with 3 components."
- **Persistence Stability:** Standardized cross-platform pathing using `LocalApplicationData`.

### Fixed
- **Profile Visibility:** Resolved bug where hidden profiles remained visible on the chart.
- **Speech Interruption:** Fixed "Series Name Cut-off" issue during series switching.
- **Ghost Tones:** Silence time-series oscillators when navigating distribution-based profiles.

---

## [2026-03-21] — Framework Migration & Orchestration Refactoring

### Added
- **Full Framework Migration:** WinUI 3 → .NET 10 MAUI Blazor Hybrid.
- **Blazor-Based UI:** All UI components in `BlazorClient`, utilizing SkiaSharp on native SKCanvasView.
- **Custom Audio Engine:** Replaced NAudio/MIDI dependency with pure C# DSP engine.
- **Orchestration Layer:** Introduced `DataOrchestrator`, `IndicatorOrchestrator`, and `MarketOrchestrator`.

### Improved
- **Memory Efficiency:** Standardized on `readonly record struct Ohlcv` for all data handling.

### Removed
- **WinUI 3 (Windows App SDK):** Removed all native XAML and Windows-specific UI drivers.
- **NAudio synthesis dependency:** NAudio retained only for WASAPI output push on Windows.
