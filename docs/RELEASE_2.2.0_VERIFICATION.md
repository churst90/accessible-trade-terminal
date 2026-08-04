# 2.2.0 — verification checklist

**Not yet tagged.** This is the live record of what has and has not been checked, kept in the same
form as `RELEASE_2.1.0_VERIFICATION.md` so the two can be compared.

The 2.1.0 document ended with two items open at the moment of tagging, recorded rather than quietly
dropped. **Both are still open**, and they are listed again below rather than being allowed to age
into invisibility.

---

## Why this release needed a hands-on pass more than most

2.2.0 changes **what every single arrow keypress says**. That is the highest-blast-radius change the
project has made, and it is the kind of change a test suite is worst at judging: every sentence can
be individually well-formed while the experience of moving through a chart is wrong.

That is not a hypothesis. **Eight defects were found by using the terminal, none by the 2,875-test
suite:**

| Defect | Why the suite could not see it |
|---|---|
| Only one thing spoken per keypress | Speech went into an ARIA live region and Blazor batched the writes; every call "succeeded" |
| Outcomes announced at a formation's *starting* bar | Each sentence was well-formed; only the sequence was wrong |
| "End of" said when arrowing **left** onto a formation's start | The method **took the direction as an argument**, so a test could only confirm the mapping it was handed |
| `FeedbackType.Boundary` discarded its message at **10 call sites** | The events were published and observed; nothing asserted they were *heard* |
| Two 4h charts shared one pattern-cache entry | The key collided only for same-timeframe charts of the same asset class — the ordinary case, not an edge case |
| Chart pattern description reset at every launch | It lived in the store, so it worked perfectly all session; nothing tested that it reached disk |
| Formation labels overprinted into an illegible smear | Every drawing test asked whether rendering *threw*, not whether the result was readable |
| The new render tests **aborted the whole suite** | Undisposed `SKSurface` handles. It never presented as a failing test — the run simply died at a different count each time (761, 1,472, 1,843 of 2,849) |
| The `0` key put a level at zero on the **price** pane, stretching the axis to the origin | The command did exactly what it was written to do; the defect was that the same key had to mean something different on a price pane than on an oscillator |

Every one is a defect of *behaviour over time* or *silence*, and those are the two things unit tests
systematically miss.

---

## Checked ✅

**Build and suite**
- [x] Core, SDK, WebHost, Components, StrategyLab, Tests all build clean
- [x] **Zero compiler warnings** (the last two, redundant `@inject` in `LabelTextModal`, removed)
- [x] **2,875 tests pass, 0 failed, 0 skipped — three consecutive clean runs**, because an
      intermittent abort was found and fixed during this pass (see below)
- [x] Edge registry validates — 42 edges, structurally sound
- [x] Doc-drift guard green (README plugin count, test count, shortcut table)
- [x] MAUI head now compiled by CI on every push to main, not only at tag time
- [x] **Host parity enforced by test** — 167 DI registrations match across both heads; the 5
      differences are host-specific and named. Same test compares both `app.css` theme-variable
      sets, which nothing compiles.

**Features added since the first draft of this document**
- [x] Quick trade from the chart (arm risk → stop → limit/market), 15 tests incl. hand-worked sizing
- [x] Nested formation reporting and formation pinning (`;` / `Shift+;`)
- [x] Formation drawing on the canvas, off by default
- [x] MA cloud narration rewritten to percentages of price
- [x] Drawing names
- [x] Build id in About (`2.2.0+<sha>`)
- [x] Breadth recorded and required for every scorable edge

**MAUI head, from Linux**
- [x] `app.css` theme custom properties **identical** between the WebHost and MAUI copies —
      closing the specific drift 2.1.0 was worried about
- [x] Only selector difference is `.speech-prompt`, correctly WebHost-only

**WebHost, on Linux**
- [x] Starts, binds, serves — `HTTP 200` on `localhost:5145`
- [x] Desktop tray registers (`org.kde.StatusNotifierItem`)
- [x] Settings load from `~/.local/share/AccessibleTrader/settings.json`

**Live broker — Alpaca paper**
- [x] Account reachable, `ACTIVE`
- [x] **Bracket atomicity verified.** One POST returned a parent order plus **both protective legs
      already attached in `held` status**. Alpaca accepts entry + stop + target as a *single* order
      via `order_class`, so the broker guarantees there is no window where the entry exists
      unprotected. Order cancelled immediately; **0 open orders, 0 positions** afterwards.
- [x] Offline half pinned in `AlpacaBracketTests` — `bracket` vs `oto`, stop entries take no legs,
      `stop_price` vs `limit_price` not interchangeable

**Data sources actually exercised this cycle**
- [x] CoinGecko (universe recorder, 1,000 assets), GitHub, SEC EDGAR, GDELT (5,592 theme-days),
      FMP grades, Alpaca market data

**Hands-on, by the maintainer**
- [x] Alt+I dossier — tab strip and content
- [x] The new per-indicator Properties checkbox
- [x] Chart formation narration density — judged acceptable at 8–9% of bars
- [x] Formation edge naming, `,`/`.` behaviour → **produced three defects**
- [x] Tab switching → **produced the pattern-cache collision**
- [x] Settings persistence → **produced the DescribeChartPatterns reset**
- [x] A screenshot of the drawn formations → **produced the label-collision defect, and surfaced the
      pre-existing y-axis-to-zero defect, now traced to the `0` shortcut and closed** (item 7 below)

**Research completed in-cycle**
- [x] Analyst revision breadth — **UNTESTED** (minimum detectable effect 6.47%/month), not null
- [x] Right translation — **FALSIFIED** twice over (`TRANSLATION_FINDINGS.md`)
- [x] Three forward archives recording: universe (2 days), GDELT, analyst grades

**Docs**
- [x] `CHANGES.md` — `[Unreleased]` closed as `[2.2.0]`, and the day of work that was missing from it
- [x] `WHATSNEW.md` — user-facing summary
- [x] `USER_MANUAL.md` — chart formations, the asset dossier (which had **no manual entry at all**),
      Heikin-Ashi, expected formation counts
- [x] `SHORTCUTS.md` — `,`/`.`, the positional edge rule, the Boundary correction
- [x] `CHART_PATTERN_NARRATION.md`, `CRYPTO_SCREENER.md`, `REVISION_BREADTH_FINDINGS.md`
- [x] Version 2.2.0 in `Directory.Build.props` and all five HTTP user-agent strings

---

## Open ⚠️ — decide before tagging

**1. The MAUI desktop heads have still never been *run*.** *(carried over from 2.1.0)*

This development box cannot close it: no MAUI workloads, and none of that head's target frameworks
target Linux. But two thirds of the original worry have now been closed from here.

**Closed — the CSS drift.** The 2.1.0 document's specific fear was that 121 theming edits went into
the WebHost's copy of `app.css` while the MAUI head's copy was never seen, leaving dialogs dark on
dark. Both copies were compared this cycle:

- **Theme custom properties are byte-for-byte identical** between the two files.
- The only selector difference is `.speech-prompt` (six rules), which is the WebHost's browser-voice
  permission prompt and correctly does not exist on the MAUI head.

The theming edits did land in both. That does not prove the head renders, but it removes the
specific mechanism 2.1.0 was worried about.

**Closed — the CI gap, which was worse than it looked.** The MAUI head was built **only by
`release.yml`, which triggers on a tag**. A compile break in it therefore surfaced at the moment of
cutting a release, and that is precisely why two consecutive releases shipped a head that had never
been built during their own development cycle — there was no earlier opportunity.

`tests.yml` now carries a **`maui-windows-build`** job on push-to-main and manual dispatch. It
*builds* rather than publishes (publishing adds minutes of trimming and proves nothing extra about
compilation), and it is deliberately not on pull requests, because Windows runners bill at a higher
multiplier and catching a break within one commit is worth far more than catching it one PR
iteration earlier.

**Still open — nobody has launched it.** No CI job opens a window, and `app.css` is not compiled by
anything. This needs a human on Windows or macOS, and it is the single largest remaining unknown in
the release.

**2. Not every dialog has been opened on both heads.** *(carried over from 2.1.0)*

Several were on the WebHost this cycle, and doing so is what found the defects above.

**3. Quick trade has never been driven by a person.** The arithmetic has fifteen tests including a
hand-worked sizing case, and bracket atomicity is verified against a real Alpaca paper account — but
the path from *hotkey → armed → stop set → Enter → order* has not been walked end to end by a human.
It is the only feature in the terminal where a keystroke sizes and sends a real order with no dialog
in between, so it should be exercised once on paper before the tag.

**4. Live trading is unverified.** Paper is verified end to end. A live account has not placed an
order. The 2.2.0 changes do not touch order routing, so this is not a regression risk — it is a
standing gap.

**5. The forward recorders are new.** `record-universe`, `record-gdelt` and `grades record`
each hold a single run. Their failure-classification and refuse-partial-write logic was written the
same day it was first exercised. Two more clean runs each before anyone relies on the archives.

**6. GDELT throttling is worse than the retry budget handles.** The first run captured 8 of 10
themes; a second run on 2026-08-04 lost **7 of 10** and was correctly refused rather than written as
a partial snapshot. The refusal is the recorder working as designed — but it means the archive is
still one run deep and the two missing themes are not yet backfilled.

The cause is GDELT's IP-level throttle, which is stickier than its documented "one request every
five seconds" and persists across runs from the same address. Options, none blocking for 2.2.0:
raise the spacing well beyond 10s, split the themes across two daily runs, or move to GDELT's
bulk/ngrams dataset. **Do not tag on the assumption the GDELT archive is usable yet.**

**7. ~~A main-pane indicator forces the price axis to include zero.~~ CLOSED — and it was not an
indicator.**

Found in a maintainer screenshot of BTC 4h: the y-axis ran 0 → 70,000, compressing all price action
into the top tenth of the pane.

**The cause.** The `0` shortcut (`SystemCommand.AddReferenceLevel`) added a level at **literal zero**
to whatever series held focus. It was written for oscillators, where zero is a real and useful
constant — but pressed with the **price** series focused it put a level at 0 on a chart trading near
64,000. `ViewportRangeCalculator` expands the price range to cover any visible main-pane level, so
the axis stretched to the origin. Levels persist in the workspace, so the chart came back broken at
every launch.

It was confirmed by reading the maintainer's own saved workspace rather than by inference. The entry
was still sitting there:

```json
{ "Name": "Zero", "Value": 0.0, "ColorHex": "#888888", "IsVisible": true }   // on the CANDLES series
```

**Why the first suspect was wrong, and why that matters.** `LoukasCyclesProvider` declares "DC Floor"
at 0.0, "DC Window Open" at 35.0 and "DC Overdue" at 90.0 — bar counts, not prices — which looked
like an exact match. It was not the cause: pane assignment is decided by `PaneAssignmentService` from
the indicator *code*, **not** by the provider's own `DefaultPane` property, and the two disagree for
several providers. Checking the property would have cleared the wrong indicator and convicted it in
the same breath.

**Three fixes, deliberately at different levels:**

1. **`ReferenceLevelPlacement`** — the `0` key now places the level in the units of the pane. Zero on
   an oscillator; the **price under the cursor** on a price pane; a spoken refusal when there is no
   price to use. 8 tests.
2. **A units guard in `ViewportRangeCalculator`** — a level more than 3× the visible data span
   outside it is not the same quantity as the pane and cannot expand it. This holds regardless of how
   the level got there, so it also covers saved overrides, analytics hints, and a series whose pane
   assignment is wrong. 7 tests. The Fear & Greed case the expansion was added for still works.
3. **`MainPaneLevelUnitsTests`** — enumerates every constructible provider, asks
   `PaneAssignmentService` where each of its codes lands, and fails if anything on "Main" declares a
   fixed level. A constant in source cannot be a price, so this is statically checkable. It currently
   passes, which is the evidence that **no shipped indicator has this defect**.

**One residue.** The stale `Zero` level is still in the maintainer's `__last-session__.json`. It is
now harmless — the guard stops it expanding the axis, and it renders off-screen rather than clamped
to an edge — but it can be deleted from the series Properties dialog.

**What this one is really an instance of.** Three of this cycle's defects — the pattern-cache key,
the unpersisted preference, and this — were found by *reading state that had been written to disk*
rather than by reasoning about code. The workspace file named the culprit in one line after two
rounds of source inspection had produced only a plausible-and-wrong suspect.

---

## The tag

```bash
git tag v2.2.0
git push origin v2.2.0
```

`.github/workflows/release.yml` produces four self-contained WebHost builds (linux-x64, win-x64,
osx-x64, osx-arm64), the two unsigned MAUI heads, and `SHA256SUMS.txt`.

**Do not tag until items 1 and 2 are either closed or explicitly accepted**, in the same way 2.1.0
accepted them — recorded, with a name against the decision, rather than forgotten.

---

## For 2.2.1, if one becomes necessary

In order: the MAUI head launch, the dialog sweep on both heads, and the quick-trade paper
walkthrough. The zero-axis defect (item 7) is closed and does not carry forward.
