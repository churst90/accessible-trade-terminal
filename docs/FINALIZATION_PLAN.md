# Finalization Plan — Accessible Trade Terminal

**Created:** 2026-07-09 (v1.4.0 baseline, commit `1a07d62d`)
**Status:** Phase A COMPLETE (2026-07-09). Phase B FIRST PASS COMPLETE (2026-07-09,
full suite 1288/1288 green — pending Cody's real-app verification): click-bar-to-hear,
shift+wheel pan, double-click jump-to-live, crosshair + DOM readout, chart-level
context menu with per-series actions, idle right-click fix, shared ChartMath pointer
mapping + tests. Phase B second pass (hit-test index, per-component menus, range
selection, axis drag, magnet snap) deferred and tracked in TODO.md. Phase C is next.

This document captures the five-area finalization audit (mouse, touch, UX/disabilities,
unit tests, security/robustness) and the agreed phased execution plan. It is the
authoritative tracker for the finalization push; update the per-phase status blocks as
work lands.

---

## Phase status

| Phase | Scope | Status |
|-------|-------|--------|
| A | Security hardening + Tier-1 money-path tests | **Complete 2026-07-09** (see CHANGES.md [Unreleased] + TODO.md 2026-07-09 section) |
| B | Mouse completion (click-bar, crosshair, context menus, hit-test index) | **First pass complete 2026-07-09** — click-bar/wheel-pan/dbl-click/crosshair/chart menu shipped; hit-test index + range select + axis drag + magnet deferred (TODO.md) |
| C | Touch input (web Pointer Events + slider semantics, iOS adjustable, Android ExploreByTouchHelper) | Not started |
| D | Multi-disability UX (deaf/HoH, colorblind, vestibular, cognitive, low-vision) | Not started |
| E | Test debt (continuous; Tier 1 inside Phase A, Tiers 2–4 ongoing) | Started with Phase A |

Rationale for the order: A removes live risk on the public site now; B builds the
render-time hit-test index that C reuses (explore-by-touch, DotPad region mapping);
C is the only genuinely large unknown (on-device screen reader behavior) so everything
feeding it lands first; D and E are low-risk items that fill scheduling gaps.
Documentation updates ride along with each phase rather than batching at the end.

---

## 1. Mouse interaction (Phase B)

### Already shipped (v1.4.0, tested)

- Click-drag pan with fractional-bar precision and off-canvas mouseup capture
  (`DrawingInteractionManager`, 3 tests)
- Wheel zoom anchored at the cursor — the bar under the pointer stays fixed
  (`ViewportReducer.WheelZoom`, 9 tests)
- Drawing tools: click-drag with live preview, legacy click-click, endpoint drag-to-edit
  with 10 px handle hit-testing
- Right-click context menu **on drawings only** (Delete / Duplicate / Properties,
  `DrawingContextMenu.razor`), with keyboard parity via the Application key
- Pane divider drag-resize

Key architectural fact: the renderer tracks no per-series/per-component screen
positions, so nothing beyond drawing anchors can currently be hit-tested.
`MapXToIndex()` (pixel → bar index) already exists in `DrawingInteractionManager`.

### Planned additions, in order

1. **Click bar → select + announce + sonify.** Dispatch the same store update the arrow
   keys use (`CurrentDataIndex`); the existing `AccessibilityFeedbackCoordinator` →
   `NavigationSonifier` + speech pipeline fires automatically. Perfect keyboard/mouse
   parity: a sighted user clicks a bar and hears exactly what a keyboard user hears, and
   the keyboard cursor moves there. Distinguish click from pan-drag with the same 5 px
   dead zone the drawing tools use. Cheapest, highest value — the model interaction for
   the app.
2. **Crosshair + hover readout.** Overlay-layer crosshair with a date/price readout in
   the corner, throttled through the existing RAF-throttled mousemove. The readout must
   be a real DOM text element (not baked into the PNG) so it is magnifier- and
   contrast-friendly. Do NOT wire hover to the live region (speech spam). Optional
   quiet hover-sonification setting, default off.
3. **Chart-level right-click menu (no hit-testing needed).** Context menu on empty chart
   space with a "Series ▸" submenu enumerating `ActiveSeries` by friendly name, each
   with Mute / Hide / Solo / Properties / Remove. Deliberately avoids precise pointing —
   an accessibility win for low-vision and tremor users. Entries: Play from here, Add
   indicator, Paste drawing, series submenu, chart display toggles.
4. **Render-time hit-test index → true per-series/per-component right-click.**
   Instrument `ChartRenderer` to record bounding geometry per (series, component) during
   the render pass. Enables: right-click a MACD line → component-scoped menu (Mute,
   Mute others, Sound patch, Color, Properties); click a component → move keyboard
   component focus there. This index is shared infrastructure for Phase C
   explore-by-touch and DotPad region mapping.
5. **Range selection:** shift+click or drag on the time axis → select bars X–Y, then
   right-click → Play range / Speak summary (high, low, net change) / Backtest here /
   Export. Maps onto the existing playback sequencer.
6. **Axis interactions (lower priority):** drag price axis to scale, drag time axis to
   zoom, double-click axis to reset auto-fit.
7. **Magnet/snap mode for drawings** (snap anchors to OHLC of nearest bar).

Skip double-click-to-zoom-fit as a primary gesture (put "Fit" in the context menu):
double-click is failure-prone for tremor users and undiscoverable.

Prerequisite from Phase E: `ChartMath` coordinate-transform tests BEFORE this work.

---

## 2. Touch input — iOS / Android / web (Phase C)

### Current state

No touch code exists in any head. Web `keyboard.js` registers mouse events only; the
MAUI `SKCanvasView` is `InputTransparent="True"` with no gesture recognizers and no
semantic wrappers; `IInputService` has no touch concept. On a phone the app requires a
Bluetooth keyboard (now documented). `docs/PLATFORM_STRATEGY_AND_ROADMAP.md` §4 already
contains the correct MAUI-side design; the web side is specified here.

### Shared layer (build first)

`IGestureService`, sibling to `IInputService`, normalizing platform gestures into the
existing `SystemCommand` enum, routed through `CommandDispatcher`. All downstream
speech/sonification/DotPad works unchanged.

Direct-touch gesture map (screen reader off):
- One-finger drag = pan (mirrors mouse pan-drag)
- Pinch = zoom (reuse `WheelZoomAction`, anchor at pinch centroid)
- Tap a bar = select + announce + sonify (same handler as Phase B mouse click)
- Double-tap = play/pause; long-press = context menu
- Two-finger swipe up/down = pane/series navigation

### MAUI iOS — VoiceOver

Wrap the canvas in a `UIAccessibilityElement` with `UIAccessibilityTraitAdjustable`:
VoiceOver flick up/down calls `accessibilityIncrement`/`Decrement` → next/previous bar.
`accessibilityCustomActions` populate the VoiceOver **rotor** with: Next component,
Next pane, Play series, Open trading ticket, Drawing tools. Magic-tap = play/stop.
`accessibilityValue` returns the `SpeechFormatter` bar summary so VoiceOver's own voice
reads it (no live-region races on mobile).

### MAUI Android — TalkBack

`ExploreByTouchHelper` virtual nodes. Start with a single adjustable node mirroring the
iOS design; later add virtual child nodes per pane so explore-by-touch (finger sweep
over the chart) announces pane/region — that variant needs the Phase B hit-test index.

### WebHost + accessibletrader.com demo

1. **Direct touch:** Pointer Events (`pointerdown/move/up/cancel`) alongside the mouse
   handlers in `keyboard.js` — Pointer Events unify mouse and touch so the existing
   `OnMouseEvent` bridge barely changes. Pinch via two-pointer tracking → `OnWheel`
   with centroid anchor.
2. **Mobile screen readers:** VoiceOver/TalkBack own the touchscreen; the web analog of
   the iOS adjustable trait is **`role="slider"`** with `aria-valuetext` = current bar
   spoken summary. Flick up/down = next/previous bar, spoken natively by the mobile
   screen reader.
3. **On-screen touch toolbar** (collapsible, ≥44 px targets): Previous/Next bar,
   Component up/down, Play, Menu. Serves mobile SR users, motor-impaired users, and
   anyone on whom gestures fail. Gestures must never be the only path.
4. **Fix viewport meta:** remove `user-scalable=no, maximum-scale=1.0` from
   `index.html` (WCAG 1.4.4 failure — blocks pinch-zoom for low-vision visitors). Use
   `touch-action: manipulation` on the chart element only if double-tap-zoom interferes.

Sequencing: web first (improves the public demo immediately, testable in any phone
browser) → iOS → Android. Budget real on-device time with actual VoiceOver and
TalkBack. Split the god-modals before/during this phase (mobile layouts reuse per-tab
components).

---

## 3. UI/UX and additional disabilities (Phase D)

### Deaf and hard-of-hearing (biggest gap)

- **AI Analyst output is speech-only** — render the analysis as text in the modal too.
- **Playback has no visual counterpart** — moving playback cursor / highlighted-bar
  overlay during Space playback.
- **Earcons have no visual pulse** — optional "visual earcons" setting: brief border
  flash or corner badge per earcon category; persistent indicator when a strategy setup
  confirms. **Constraint:** under three flashes per second, low contrast (WCAG 2.3.1) —
  do not trade a deaf/HoH fix for a photosensitive-epilepsy hazard.
- Surface the Journal better: a small "recent events" ticker region for ambient
  awareness.

### Low vision

- Finish the tracked contrast sweep (~30 inline `#888`/`#aaa` literals failing WCAG AA
  on light panels — TODO.md).
- Server-rendered 1280×720 PNG is fuzzy on HiDPI: render at client `devicePixelRatio`.
- In-app UI scale setting (root `font-size` multiplier).

### Colorblind

- Palette picker with a deuteranopia-safe option (blue/orange), plus optional
  hollow-vs-filled candles (the classic colorblind-safe convention).

### Vestibular

- Respect `prefers-reduced-motion` (currently ignored; animations are minimal so this
  is a one-hour CSS fix).

### Motor

- Raise click targets under 44×44 px (tab buttons especially), or add generous
  invisible hit areas.

### Cognitive / onboarding

- Three-step first-launch prompt (market → provider → symbol, "try the demo workspace"
  escape hatch); link QUICKSTART from the Help modal (F1).
- Settings search (filter box across all six tabs).
- Wire up the built-but-unsurfaced "Use Recommended" strategy preset button.
- Speech-template editor UI (currently requires hand-editing JSON).

### Organizational

- Split the four god-modals (PropertiesModal 958 lines, TradingDashboard 865,
  Settings 845, Strategy 730) into per-tab components — schedule before/during Phase C.

---

## 4. Unit test gaps (Phase E, Tier 1 inside Phase A)

**Tier 1 — money-touching, effectively untested:**
1. `GeneralOrderService` — dedup window, quantity clamping, provider routing, paper
   fallthrough.
2. `PaperTradingProvider` — stop/TP trigger semantics (penetration vs equality),
   position averaging, fee math, persistence round-trip, no-fill-when-symbol-unloaded.
3. `ApiKeyService` — zero tests. CRUD, async-lock concurrency, save/load round-trip,
   missing-secret null safety.
4. `RiskPercentPositionSizer` — sizing math incl. division-by-zero when entry == stop.

**Tier 2 — regression-prone infrastructure:** `ChartMath` transforms (before Phase B),
`CommandDispatcher` modal-stack/focus gating, `AlertEvaluator`/`AlertOrchestrator`,
`DataOrchestrator` state transitions, `SettingsManager` corrupt-file recovery.

**Tier 3 — provider contract enrollment:** Binance, InteractiveBrokers, Schwab,
Finnhub, TwelveData, Fmp, Mexc missing from `ProviderFetchOhlcvTests` /
`ProviderLiveStreamTests`. Start with Binance (recently rewritten direct-API).

**Tier 4 — new surfaces:** WebHost auth flow (registration validation, lockout, tier
gating); JS test infra (none exists — Vitest; first targets: `audio.js` base64→Float32
decode, keyboard dedup window).

Full gap list ≈300+ tests; Tier 1 + `ChartMath` (~40 tests) covers the majority of
actual risk.

---

## 5. Security & robustness (Phase A)

Architecture strengths (keep): out-of-process script sandboxing on all platforms,
scoped DI multi-user isolation, hash-based plugin trust manifest, secrets separated
from metadata, security event logging, paper-only hosted accounts.

### Fix first (high confidence, low effort)

1. **Silent unsandboxed script fallback.** `LinuxBwrapLauncher` falls back to a plain
   process launcher when bwrap is missing, sets `SandboxApplied = false`, tells the
   user nothing → hostile custom indicator gets full file/network access including
   `apikeys_meta.json`. Fix: refuse to run scripts without a sandbox (or explicit,
   loudly-worded opt-in) and surface sandbox status in the UI.
2. **Security headers on WebHost.** No HSTS, CSP, X-Frame-Options,
   X-Content-Type-Options in `Program.cs`. Add response-header middleware.
3. **Encrypt `apikeys_meta.json`** (metadata: which exchanges, nicknames, environment
   flags) with the same protector as the secrets.

### Fix soon (medium)

4. `DateTime.Now` in `LiveStreamManager` watchdog + earcon throttles — non-monotonic
   local time in reconnect logic. Use `Stopwatch`/`Environment.TickCount64` for
   intervals, `DateTimeOffset.UtcNow` for persisted times.
5. Unbounded live-stream channel in `DataOrchestrator` — bound (drop-oldest ~1000).
6. Rate limiting is per-IP only — add stricter partition on `/account/login` and
   `/account/register`.
7. Symbol/timeframe validation exists only in demo mode — apply format whitelist
   (`^[A-Z0-9/_\-.]{1,20}$`, timeframe from known enum) in all modes.
8. Fire-and-forget watchdog tasks swallow exceptions — route to the global error
   coordinator.

### Verify before acting — RESULTS (verified 2026-07-09)

- **"Session fixation on login": NOT APPLICABLE.** No `ISession`/`AddSession`/`UseSession`
  anywhere in WebHost; auth is cookie-only and ASP.NET Core Identity issues a fresh
  auth ticket on `PasswordSignInAsync`. No action needed.
- **Antiforgery: VERIFIED FINE.** `_ViewImports.cshtml` adds
  `Microsoft.AspNetCore.Mvc.TagHelpers`, so every `<form method="post">` in
  Login/Register/Logout auto-emits the antiforgery token; `UseAntiforgery()` +
  Razor Pages auto-validation cover the POST side.
- **dp-keys:** backup was already documented in SERVER_SETUP.md; added `chmod 700` /
  service-account-ownership guidance (2026-07-09).
- **WebSocket origin / circuit hijacking: MITIGATED BY DESIGN.** The auth cookie is
  `SameSite=Lax`; browsers do not attach Lax cookies to cross-site WebSocket
  handshakes, so a hostile origin cannot open an authenticated circuit. Documented in
  SERVER_SETUP.md's security checklist.
- **Symbol validation "demo-only" claim from the audit: WRONG.** `SymbolValidator` was
  already enforced at the `DataOrchestrator` choke point in every mode. Phase A added
  the missing companion check for timeframe tokens (`TimeframeUtility.IsValid`).

### Deliberately deferred

Cryptographic signing of the plugin manifest (hash-based TOFU is fine until third-party
plugin distribution), per-user rate limiting, GUID sanitization cosmetics.

---

## 6. Documentation updates (ride along with each phase)

- **USER_MANUAL.md:** proper "Using the mouse" section (expand as Phase B ships);
  "Touch and mobile" section stating the Bluetooth-keyboard requirement until Phase C;
  visual-earcons and reduced-motion settings when they land.
- **SHORTCUTS.md:** "Mouse and touch equivalents" table alongside keyboard bindings —
  keeps keyboard/mouse/touch parity honest.
- **SERVER_SETUP.md:** dp-keys backup/rotation, security-header middleware, bwrap
  required (not optional) for script execution.
- **PLATFORM_STRATEGY_AND_ROADMAP.md §4:** extend with the web-side touch design
  (Pointer Events + slider semantics + touch toolbar).
- **QUICKSTART.md:** link from the in-app Help modal (F1).
- **New ACCESSIBILITY.md:** WCAG 2.2 AA conformance statement with known exceptions —
  institutional users and APH-type partners ask for exactly this.
