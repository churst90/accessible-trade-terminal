# Report card — 2026-09-13

The third graded pass over the whole terminal. The first was the 2026-08-24 health assessment
(13 areas, 283 findings filed unverified, production-readiness **D+**). The second was
`REPORT_CARD_2026-09-05.md`. This one is written against `HEAD` after the fifty-fifth fix pass —
eight days, twenty-four pass entries and two releases later (2.9.0, 2.10.0).

**How to read it.** Each grade is backed by the evidence column and nothing else. Where the
evidence is a measurement (a test count, a browser run, a bus capture, a mutant that survived) it
says so; where it is a read-through it says that too. The standing rule of this repo applies to
its own report card: *demonstrate the defect or mark it unverified.* Items marked **unverified**
are claims.

**What is new in the method this time.** Two of the three summary grades moved on the back of
measurements that did not exist on 09-05: the first mutation campaign ever run against the
rendering path (09-12), the first ever run against the speech and narration path (09-13, A2f),
and a live run against a real venue API (Gemini sandbox, 09-07). Where an area's 09-05 entry said
"what would move it", this pass says plainly whether that thing happened.

## The grades

| Area | 2026-08-24 | 2026-09-05 | **2026-09-13** | Evidence for the grade | What would move it |
|---|---|---|---|---|---|
| **Accessibility — web surface** | B- | B+ | **B+** | Unchanged in kind, larger in extent: 209/209 browser cases against real Chromium, red on sabotage; every drawing point now named by what it IS rather than "anchor 1/2/3"; band labels and the declared neutral spoken. **None of 09-05's three named gaps closed:** still no VPAT, the MAUI head has still never been measured with a screen reader, Shift+Tab is still outside the browser focus contract. | The same three. The MAUI one is now the oldest unaddressed item on this card. |
| **Speech and narration** | — | B | **B+** | The largest body of work in the window: the coherence pass (ability vs occasion), the narration ladder with the browser closed, one utterance per scan holding under load, the routing policy (`§0a` — the channel is the event's SUBJECT), band-crossing narration, playback speaking a band crossing by discreteness rather than component type, and every drawing tool speaking its own answer. **And for the first time it is measured: A2f, 26 mutants over `Services/Accessibility` — 26 mutants, 18 caught, **69.2%**, and every one of the six low-failure catches re-verified with test names captured so a lone flake cannot masquerade as a catch. The eight survivors are closed, each proved red by re-applying its mutant.** | The survivors say where it is thin: two of the eight were in `SpeechFormatter` alone, and two more were selection rules that no fixture had ever given a second candidate. Next: a campaign over `Services/Audio`, which has had ONE mutant ever. |
| **Indicators** | C+ | B- | **B** | The declaration cluster landed whole: 17 indicators declare `RangeMin`/`RangeMax` and keep a fixed axis at every zoom; 31 components declare `DefaultReferenceLevel`, with `DeclaredNeutralTests` refusing a neutral on a declared bound; the level/component contract (a level is a CONSTANT, a component has a value per bar); warmup NaN is universal, so `Any(IsNaN)` is not sparseness; 15 inert drawing placeholders deleted from the registry. Census improved: **12 of 59 indicator types never named by a test, from 15.** | A mutation campaign confined to `Services/Indicators` — still the single largest never-mutated area now that Accessibility has been swept. `ComponentRoleMapper`'s name registry. |
| **Trading — live** | C+ | B- | **B** | **09-05's blocker is partly discharged.** "No automated run against a live venue, ever" is no longer true: the stored Gemini sandbox credential drove the real plugin against `api.sandbox.gemini.com` across symbols, candles, balances, open orders, and a limit order placed and cancelled — and it found two things reasoning had missed (the positions table's stop/target editor cannot work on Gemini at all, and `SupportsOrderEventStreaming = false` means a subscribe that succeeds and emits nothing forever). Order routing safety shipped: one credential per provider, a refusal at the chokepoint, a dashboard that says "real money". The conformance suite went **33 rows red on its first run** and forced fixes in ten plugins. | A recorded paper-then-live session on a venue with REAL money and the journal as evidence. A sandbox is a venue; it is not a fill. |
| **Trading — paper** | C+ | B | **B** | Unchanged. Cross margin, decoupled dashboard, reset asks first. `PaperTradingProvider.cs` is now the largest file in the repo at 2,236 lines. | Fills/partials replayed from a venue capture instead of synthetic. |
| **Alerts and monitoring** | D | C+ | **B** | **09-05's named mover is DONE.** The notifier seam (TODO §2c) closed across four phases: Phase 0 made the OS a parameter (delivery was Linux-only via one early return in an unrelated helper), Phase 1 gave the process one long-lived DI scope (off-screen alerts had been watched by nobody), Phase 2 brought order fills headless, Phase 3 brought new bars — including on charts you have open but are not looking at. Alerts with the browser closed now read a real chart (`HeadlessChart`). Three default-off notification switches became one that defaults ON. A notification that FAILS is now heard. | Condition-tree alerts are still session-only. The MAUI head has no `LocalBackgroundMonitor`. |
| **Data flow and providers** | C+ | B- | **B** | The conformance suite exists and is the evidence: one property stated ONCE across every venue, 33 red rows on the first run, ten plugins fixed, and the design rule that a skipped row cannot go red. The SDK contract says what it means. "Coverage" was redefined from *did a subscribe call return* to *can an event ever arrive* — the distinction that made Phase 2's own guard vacuous on three venues. | Recorded-response tests for the equity providers (Schwab's unnamed types). Binance stays geo-blocked. |
| **Workspace and persistence** | C+ | C+ | **C+** | **Held, deliberately, and the reason is that the window produced three more.** The playback speed persisted NOWHERE; `PreferenceRoundTripTests` was VACUOUS for any type its `Flip` helper did not know, so it had been passing over untested fields; and a RESUMED session left `SymbolDisplayName` empty, so the focused chart claimed no coverage AND its alerts never fired. This is the fourth consecutive card on which restore is where the defects are. | Replace the factory's hand-written clone with `ComponentConfig.Clone()`. A round-trip test that serialises a full state and diffs EVERY field — the vacuous-`Flip` bug is the proof that a partial one passes. |
| **Audio and sonification** | C+ | B- | **B-** | Held, and now with a measured reason rather than a suspicion. Real work landed — one grit rule for every bar, playback discreteness, the brickwall limiter, two earcon families. But `Services/Audio` is 19 files and 4,394 lines with **exactly one mutant ever applied to it** (in `AudioEngine.cs`), and **8 of 32 types are still never named by a test**. For an application whose primary output is sound, that is the least-measured thing that matters most. | A mutation campaign over `Services/Audio` — the natural successor to A2f. A rendered-audio regression fixture (peak, RMS) for the standard patches. |
| **Drawing and rendering** | C+ | B- | **B** | **Rendering was tested and mutated for the first time in the project's history** (09-12): 18 mutants, 17 caught, and the campaign paid for itself twice — 704 of 1,910 range/pane combinations put an axis label between gridlines while `RenderYAxis`'s comment promised they aligned, and with eight indicator panes the bottom one was drawn UNDER the x-axis strip. Both fixed; one `ChartMath.NiceStep` for grid and labels; a FINAL FIT pass in the allocator. The `SKTypeface` leak and `AIAnalystService`'s shared-renderer snapshot are closed. Drawings left the indicator dialog, every point is named, and **Alt+Shift+M and Alt+Shift+P were confirmed by ear on 2026-09-13.** | **Two known holes, both named in TODO:** the one surviving rendering mutant (delete the minimum-label-spacing check in `RenderYAxis` and nothing goes red) and `RenderCandles`'s `hasPhaseOverride` branch, which has **no fixture at all** — grep the test project for `phaseData` and it returns nothing, so a mutant aimed there cannot be evaluated. |
| **Strategies and StrategyLab** | D+ | C+ | **C+** | Held, and the hole is measurably bigger than it was: **71 of 99 types in the census still have no test at all**, and counting the project as it stands today it is 77 of 111 — the largest single hole in the codebase by a distance, and it grew. Nothing in this window touched it. | Tests on the StrategyLab commands. It is research tooling rather than terminal surface, which is why it has been outranked eight passes running — but it should stop being invisible on this card. |
| **Analytics plugins** | C | C+ | **C+** | Partly covered by the conformance suite's key handling and status reads. Still **no dedicated pass since 08-24**. Largely unverified — the same sentence as the last card, which is itself the finding. | A pass of its own. |
| **SDK and plugin trust** | B- | B- | **B-** | No change in evidence since 08-25. Manifest workflow green on every push; four sandbox escapes compiled and closed. | — |
| **WebHost, hosting, security** | B | B | **B** | Unchanged. `CapabilityManifest` now writes one startup line naming every `DemoPolicy` flag, which closes the deploy-notes item whose shared cause was that a `HostMode` gate's only evidence was an ABSENCE in a journal. | §7b crash-dump path (box-side). The `sync-trader-docs.sh` blind `cp` from HEAD rather than from the tag. |
| **Tests and CI** | — | B | **B** | Suite **7,680** (run count; `--list-tests` reports 7,675), from ~6,780. Browser harness 209/209; three JS suites 15/61/19; doc-drift all four checks green. Census improved to **24% of declared types never named**, from 26% (09-06) and 28% (08-30). **The catch rate is now honestly framed:** 73.1% and 72.0% were measured over ~6% of the tree; after the rendering campaign and A2f it is ~11%, and it should still be quoted as a sample. | The remaining never-mutated areas, in order of how much a defect there would hurt: `Services/Audio`, `Services/Indicators`, `Services/Workspace`, `Services/Analysis`. |
| **Documentation** | — | B+ | **A-** | The 54th pass was a documentation pass in its own right: the quickstart condensed, the manual gained four chapters, WHATSNEW assembled complete from twenty-four pass entries, fourteen diagrams validated by rendering. And the guard got sharper in a way that matters — `DrawingShortcutDocParityTests` checks **PAIRING, not presence**: `check_doc_drift.py` only ever asked whether a chord appeared somewhere in SHORTCUTS.md, so a doc naming Alt+Shift+P and then describing it as the wrong tool would have passed. | Extend the pairing guard beyond the seven drawing chords — the same gap almost certainly exists for the rest. `/features` on the hosted box still says "three switches, each off until you turn it on", which went false at this tag. |

## The three summary grades

| | 2026-08-24 | 2026-08-28 | 2026-09-05 | **2026-09-13** |
|---|---|---|---|---|
| Chart-reading terminal for a blind trader | B- | — | B+ | **A-** |
| Paper trading | C+ | — | B | **B** |
| **Production-readiness to trade real money** | **D+** | C+ | B- | **B** |

**Why the chart-reading grade moves to A-.** Three things that were opinion on 09-05 are
measurements now. The rendering path had never been tested at all and is now tested and mutated.
The speech path had never been mutated and now has been. And the declaration cluster — bounds,
neutrals, level roles, pane assignment — replaced four separate sniffing heuristics with one
declaration each, which is why the 46th through 52nd passes kept finding the same shape of bug
and the 53rd through 55th did not. What holds it below A is that the MAUI head has never been put
in front of a screen reader, and that is not a small remaining thing: it is the head Cody's Dot
Pad attaches to.

**Why real-money readiness moves to B.** The 09-05 card said the grade was held below B by one
thing no amount of unit testing moves — nothing had ever been exercised against a live venue.
That is now false: the Gemini sandbox run happened, it was run rather than reasoned, and it found
two defects that reasoning had missed. Order routing safety and the conformance suite closed the
structural gaps around it. What holds it below B+ is that a sandbox is not a fill: there is still
no recorded paper-then-live session with real money and a journal.

## What this pass added to the evidence

- **A defect CLASS was closed rather than a defect.** Four separate bugs in the 46th–49th passes
  had one shape — a property declared, stored, shown in the Properties dialog, and read by nobody
  at the one place it means anything (`UsePolarityColoring`, `DefaultReferenceLevel`,
  `DefaultPane`, and three indicator bool parameters). `DeclaredKnobObservabilityTests` now sweeps
  every knob on `ComponentConfig` by reflection across 27 shape x audio cells, flips it, and
  requires one of four channels — pixels, the sonification point, the navigation readout, the
  bar-close narration scan — to come out different. It is proven red on the real MFI defect:
  reverting the polarity split makes it name `UsePolarityColoring` and nothing else.
  **It found one genuinely dead field on its first run** — `SecondaryWaveform`, declared on
  `ComponentConfig`, copied by `Clone()`, present in Cody's saved workspace as `""`, with no
  metadata field able to set it and no consumer anywhere able to read it. Deleted.
  Four knobs carry recorded exemptions naming the real consumer that sits outside these four
  channels (`PlaybackLayer` and `DecayMs` in `AudioSequencer`, `SubPaneHeightRatio` in
  `ChartRenderer`, `DataMapping` in the tactile canvas among others).
- **The sweep is also a lesson in fixture honesty.** Its first run named eighteen knobs; thirteen
  of those were defects in the FIXTURE, not the code — the narration channel returned "" for
  every input because seeding and scanning the same state correctly means "no news"; speech was
  set to `ValueOnly` so it never said a name; `prevVal` was left off the sonification call so the
  whole boundary-click branch was unreachable. **A channel that is identical for every input is
  not evidence of anything**, and each of those would have been filed as a finding by a less
  suspicious reading.
- **The measurement that reframed the catch rate.** 73.1% was never wrong; it was just measured
  over 6% of the tree, and two campaigns since have not moved the *rate* so much as widened the
  *frame*. The right sentence is "~72% across five independently-chosen sets covering ~11% of the
  tree", and the corroboration across disjoint areas is the interesting part, not the number.
- **Two areas are now measurably, not impressionistically, the weakest:** `Services/Audio` (one
  mutant ever, 8 of 32 types unnamed, and it is the primary output channel) and the StrategyLab
  (71 of 99 unnamed, and growing).

## What is still unverified, by name

- **The MAUI head with a screen reader.** Never done. The Windows toast notifier and tray-on-close
  shipped in 2.10.0 compiled and never run on a Windows machine, and WHATSNEW says so rather than
  claiming otherwise. The Braille tab is desktop-only by construction, so its markup has never
  rendered on the head that has a Dot Pad attached.
- **The segfault fix (§7g).** The watch window ran to ~2026-09-10 and has now elapsed with no
  crash reported, but the report would come from the hosted box and this card cannot query it.
  Absence of a report is not the same as a measurement — the standing rule applies.
- **Analytics plugins as a whole** — no dedicated pass since 08-24.
- **Real money at a real venue** — a sandbox is not a fill.
- **`RenderCandles`'s phase-colour branch** — no fixture exists, so it is not that the tests are
  weak there; it is that nothing has ever executed it.
