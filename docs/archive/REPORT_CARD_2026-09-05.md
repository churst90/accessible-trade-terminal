# Report card — 2026-09-05

The second graded pass over the whole terminal. The first was the 2026-08-24 health assessment
(13 areas, 283 findings filed unverified, production-readiness **D+**). This one is written
against `HEAD` after the twenty-fifth fix pass, twelve days and three releases later
(2.5.0, 2.6.0, 2.7.0).

**How to read it.** Each grade is backed by the evidence column and nothing else. Where the
evidence is a measurement (a test count, a browser run, a bus capture) it says so; where it is a
read-through it says that too. The standing rule of this repo applies to its own report card:
*demonstrate the defect or mark it unverified.* Items marked **unverified** are claims.

## The grades

| Area | 2026-08-24 | 2026-09-05 | Evidence for the grade | What would move it |
|---|---|---|---|---|
| **Accessibility — web surface** | B- | **B+** | 272-finding WCAG 2.2 AA audit (09-01) worked to an empty list (09-03). Modal background `inert`; focus contract asserts the declared target per dialog; speech reaches Orca (measured on the AT-SPI bus, the second live region removed). 209 browser cases against real Chromium, red on sabotage. | A conformance statement / VPAT in `docs/`; the MAUI head measured with a screen reader (never has been); Shift+Tab in the browser focus contract. |
| **Speech and narration** | — (new area) | **B** | 2.7.0: 35 indicators that could not narrate now do; one sentence per bar close; the date only when it changes. Every route pinned by a test that captures the bus. **But** this pass found the narration and component-mute switches did not survive a restart, for as long as they have existed. | One more session with no "it did not persist / did not speak" report. The restore path has produced four defects in six days (name pinning, tab index, viewport, now flags). |
| **Indicators** | C+ | **B-** | 99 providers; causality contract (declaration + `SignalCatalog` gate + prefix test); `ScanLevelCrosses` uses declared levels so a moved level narrates. 15 of 59 indicator types never named by a test (census 08-30). | The census number. A mutation campaign confined to `Services/Indicators`. |
| **Trading — live** | C+ | **B-** | Typed `OrderPlacement` (B1); dedup gate and in-flight guard; reduce-only close; the stale-review bug (edit after arming) closed 09-01; nine status-blind reads closed 08-31. Real money is desktop-only by policy. **Unverified:** no automated run against a live venue, ever. | A recorded paper-then-live session on one venue with the journal as evidence. |
| **Trading — paper** | C+ | **B** | Cross margin in the paper broker; dashboard decoupled from the chart; reset asks first (WCAG 3.3.4). | Fills/partials replayed from a venue capture instead of synthetic. |
| **Alerts and monitoring** | D | **C+** | Orchestrator actually starts; local background monitor speaks/toasts/sounds with the browser closed (simple alerts); hosted Web Push. **Gaps:** condition-tree alerts are session-only; no OS notification in-session; new bars and fills never reach a notification (TODO §2c). | The notifier seam in TODO §2c. |
| **Data flow and providers** | C+ | **B-** | 39 key-in-URL sites → `KeyParam` + two guards; body-first-then-status contract fleet-wide; symbol lists tested for the first time; provider-name drift fixed four times, now guarded. | Recorded-response tests for the equity providers (Schwab 7/10 types unnamed). |
| **Workspace and persistence** | C+ | **C+** | Save shape is correct (per-tab indices fixed); restore is where defects keep appearing — this pass added three dropped flags to the list. `RestoreSeriesFromSaved` has a hand-written clone in its path that is a second place every field must be added. | Replace the factory's clone with `ComponentConfig.Clone()`; a round-trip test that serialises a full state and diffs every field. |
| **Audio and sonification** | C+ | **B-** | Brickwall limiter (whole-chart playback had always clipped); two earcon families; `LevelCrossingMonitor` gated on visibility; drawings sonify. 11 of 31 audio types never named by a test. | A rendered-audio regression fixture (peak, RMS) for the standard patches. |
| **Drawing and rendering** | C+ | **B-** | Segfault root-caused to `ChartRenderer.cs:82` (`addr 0x8`) and fixed with an empty Dispose; ~38 h clean as of the last check — **not yet proof** (§7g, ~09-10). Keyboard nudge, anchor schema, drawings no longer freeze at the live edge. | The §7g window closing with zero crashes. |
| **Strategies and StrategyLab** | D+ | **C+** | Four look-ahead paths in the consumer layer closed; the flagship re-run reproduces cell for cell (09-04). 71 of 99 StrategyLab types have no test at all — the largest hole in the codebase by a distance. | Tests on the StrategyLab commands; a second independent hypothesis through the whole pipeline. |
| **Analytics plugins** | C | **C+** | Covered by the provider cluster (key handling, status reads). No dedicated pass since 08-24. **Largely unverified.** | A pass of its own. |
| **SDK and plugin trust** | B- | **B-** | Plugin-trust manifest workflow green on every push; sandbox escapes compiled, four closed. No change in evidence since 08-25. | — |
| **WebHost, hosting, security** | B | **B** | Hosted notes §5a–§5g closed, §5n answered with an artifact guard; `www-data` group leak found and closed; outbound network guard fails closed. | §7b crash-dump path (box-side). |
| **Tests and CI** | — | **B** | Suite ~6,780 (list count is the README number); catch rate **73.1%** measured by sabotage (61 → 67.9 → 73.1); three workflows checked per push; doc-drift guard. 28% of production types never named by any test. | The census; a fresh mutant campaign on the areas that changed most (narration, restore). |
| **Documentation** | — | **B+** | CHANGES/WHATSNEW/RELEASING discipline holds; `[Unreleased]` heading added this pass so the tag's section cannot grow after the tag; manual and quick-start updated with every change. | The README "Current Status" narrative is still Cody's to frame. |

## The three summary grades

| | 2026-08-24 | 2026-08-28 | **2026-09-05** |
|---|---|---|---|
| Chart-reading terminal for a blind trader | B- | — | **B+** |
| Paper trading | C+ | — | **B** |
| **Production-readiness to trade real money** | **D+** | C+ | **B-** |

The real-money grade is held below B by one thing that no amount of unit testing moves: nothing
has been exercised against a live venue in an automated or recorded way. Everything else that
kept it at D+ — the nine blockers, the false "Order placed", the stale review — is closed with
a test that fails on the defect.

## What this pass added to the evidence

- **A class of defect, not one defect:** state a user sets with a key that lives exactly one
  session. Narration (N) and component mute (M) both did. The restore path is the fourth-most
  recently fixed thing in the repo and the most recently broken; the workspace area stays at C+
  on purpose.
- **Two guards that fire on the property, not the spelling:** the crossing message must START
  with the series name; the tab close must never be nested and must be named.

## What is still unverified, by name

- The segfault fix (§7g) — silence is not yet evidence.
- Analytics plugins as a whole — no dedicated pass since 08-24.
- Any live-venue behaviour — never automated.
- The MAUI head with a screen reader — never measured.
