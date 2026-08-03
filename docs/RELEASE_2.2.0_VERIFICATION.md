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

That is not a hypothesis. **Five defects were found by using the terminal, none by the 2,791-test
suite:**

| Defect | Why the suite could not see it |
|---|---|
| Only one thing spoken per keypress | Speech went into an ARIA live region and Blazor batched the writes; every call "succeeded" |
| Outcomes announced at a formation's *starting* bar | Each sentence was well-formed; only the sequence was wrong |
| "End of" said when arrowing **left** onto a formation's start | The method **took the direction as an argument**, so a test could only confirm the mapping it was handed |
| `FeedbackType.Boundary` discarded its message at **10 call sites** | The events were published and observed; nothing asserted they were *heard* |
| Two 4h charts shared one pattern-cache entry | The key collided only for same-timeframe charts of the same asset class — the ordinary case, not an edge case |

Every one is a defect of *behaviour over time* or *silence*, and those are the two things unit tests
systematically miss.

---

## Checked ✅

**Build and suite**
- [x] Core, SDK, WebHost, Components, StrategyLab, Tests all build clean
- [x] **Zero compiler warnings** (the last two, redundant `@inject` in `LabelTextModal`, removed)
- [x] **2,798 tests pass, 0 failed, 0 skipped**
- [x] Edge registry validates — 40 edges, structurally sound
- [x] MAUI head now compiled by CI on every push to main, not only at tag time

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
- [x] Formation edge naming, `,`/`.` behaviour → **produced three of the five defects above**

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

**3. Live trading is unverified.** Paper is verified end to end. A live account has not placed an
order. The 2.2.0 changes do not touch order routing, so this is not a regression risk — it is a
standing gap.

**4. The forward recorders are one day old.** `record-universe`, `record-gdelt` and `grades record`
each hold a single run. Their failure-classification and refuse-partial-write logic was written the
same day it was first exercised. Two more clean runs each before anyone relies on the archives.

**5. GDELT is missing two themes** (`interestrate`, `gold`) from the only run so far, throttled out.
Recoverable — GDELT re-serves its whole window — but the next run must be checked for backfill.

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

In order: the MAUI head launch, the dialog sweep on both heads, and a second run of each forward
recorder.
