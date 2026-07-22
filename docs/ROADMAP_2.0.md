# Road to 2.0 — audit, grades, and the three tiers

Recorded 2026-07-22 from the full-codebase audit (five parallel subsystem
audits + hand verification of every critical claim). Cody's directive: no
release until 2.0; make big improvements and get everything rock solid first;
the order/trading dashboard must reach A+ with every provider supported to the
fullest reliable extent.

## Audit grades (2026-07-22, pre-Tier-1)

| Component | Grade | Note |
|---|---|---|
| Audio engine & sonification | A | Thread-safe slots, tier gating, wavetables wired end to end |
| Speech & accessibility | A− | One channel bypass (fixed in Tier 1) |
| Alerts | B− | Silent eval failures, simple/tree asymmetry (fixed in Tier 1) |
| Strategies & Lab | B | Persistence gap was workspace-side; seed descriptors unvalidated at startup |
| Trading & orders | C+ | Tradier/Schwab dropped SL/TP; fills Binance-only (fixed in Tier 1) |
| Chart data pipeline | B− | Tick/backfill race, disposal gaps, dead code (fixed in Tier 1) |
| Workspaces & persistence | D+ | Strategies + drawings not saved (fixed in Tier 1) |
| My Data / compare / OCO | A− | Binance OCO unverified against the live exchange |
| Hosted / auth / WebHost | B+ | Server-side alerts still session-bound (Tier 2) |
| Documentation | B+ | One stale manual keybinding, stale README counts (fixed in Tier 1) |

Overall pre-Tier-1: **B**. The accessibility core is the strength; persistence
and broker parity were the gap.

## Tier 1 — Make it rock solid (STARTED 2026-07-22; see CHANGES.md)

- [x] Workspace persistence: active strategies (SpecId through the engine,
  workspace-level SavedActiveStrategy records, REPLACE-on-load, saved-symbol
  binding) and drawings (anchors persisted on SeriesConfig, rehydrated on
  restore, arrays recomputed by the indicator orchestrator).
- [x] Alert failure surfacing (spoken once per alert, gate resets on edit),
  RepeatIfStillActive/Cooldown parity for simple level alerts, webhook
  missing-target warnings.
- [x] Pipeline: tick/backfill race closed via the prepend lock; live loop
  awaited on stop/dispose; subscription double-window closed; dead code swept
  (DataStream, InitialLoadStream, OnTickReceived, StopFallbackPolling).
- [x] Speech: Dot Pad connection announcements on the Event channel.
- [x] Broker parity: Tradier native OTO/OTOCO brackets + standalone TP;
  Schwab native TRIGGER/OCO bracket trees; Kraken single-slot limitation
  declared (SupportsSimultaneousStopAndTarget) and spoken; fill history on
  Kraken, Tradier, Alpaca, Coinbase, Schwab (was Binance-only).
- [x] Docs: manual F4→Shift+F1 correction, README counts, stale code comments.
- [ ] Remaining Tier-1 candidates: seed signal-descriptor validation at
  startup (spoken "missing signal" instead of silent dead seed); external
  alert channel per-alert opt-out design decision; paper hedge-mode/lot
  tracking decision.

## Tier 2 — The big rocks (why 2.0 is 2.0)

1. **Keyed-feeds pipeline refactor** — per-ChartIdentity data buffers + live
   subscriptions replacing the focused-chart singletons (DataManager,
   LiveStreamManager, DataOrchestrator, store sync, tab-switch logic; the
   IMarketFeeds seam is the entry point). Unlocks: live background tabs (not
   30s polls), split view, tick-level background strategy evaluation, hosted
   shared-feed scale. Do FIRST inside 2.0 so everything after lands on the
   new architecture.
2. **Hosted server-side alerts + Web Push** — saved alerts evaluated on the
   server against shared feeds, delivered via existing webhook/Telegram/email
   plus browser push (VAPID + service worker) with the tab closed.
3. Optional on top: split-view rendering; live-streaming background tabs UI.

## Tier 3 — Parity & polish

- Windows tray VERIFY on a Windows session (code gated behind
  -p:EnableWindowsTrayIcon=true), then enable by default.
- Live-OCO first-fire verification on real Binance (tiny pair); extend native
  OCO to other exchanges as demand asks.
- Schwab ACCT_ACTIVITY streamer (needs Cody's developer app + real account).
- Factory sound bank (deferred until Cody's WAV set arrives).
- Per-user workspace scoping on the hosted terminal.
- Paper hedge-mode / lot-level position tracking (if wanted).
- xlsx import for My Data.

## Standing external dependencies (Cody)

- developer.schwab.com app approval (Schwab streamer + live verification).
- Event sound WAVs (factory sound bank).
- Windows session (tray verify, MAUI title-bar price).
- VPS egress check for HIBP (`curl https://api.pwnedpasswords.com/range/AAAAA`).
