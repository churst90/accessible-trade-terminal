# Chart formation narration

How the terminal describes double tops, head and shoulders, triangles, wedges and flags by ear —
and, more importantly, what it refuses to say about them.

Revised 2026-08-03 after live use. Suite 2787 green. Narration density measured on 22 real snapshots
with `StrategyLab pattern-speech`.

---

## The three things that can happen to a formation

| State | Means | Spoken as |
|---|---|---|
| **Forming** | The structure is there, the confirming level has not been reached, and the verdict is genuinely not in yet | "Possible double top forming, neckline 42,100, measured target 39,400 if it breaks." |
| **Confirmed** | A bar *closed* through the trigger | "Double top confirmed here: closed **below** the neckline at 42,100." |
| **Expired** | The structure aged out without price ever closing through | "Double top ends here without confirming — the neckline at 42,100 **held**." |

**The word "completed" is banned, and a test enforces it.** It was ambiguous in exactly the way that
matters: a user hearing "double top completed" could not tell whether the pattern had worked out or
failed. It said neither — it only ever meant *price closed through the line* — so the narration now
says that, naming the side and the level. This was reported from live use, not found by a test,
which is the usual shape of a wording bug: every assertion passed while the sentence misled.

**Expired did not exist at all** until the same pass. An unconfirmed pattern stayed *Forming*
forever, so a double top from 2019 whose neckline never broke was still announced as a live
decision. Worse, the resolve scan was unbounded, so if that neckline happened to break two hundred
bars later the pattern was reported as *completing* — an unrelated move wearing an old shape's name.
The scan is now bounded by the formation's own expiry, which is what makes all three states mean
something.

---

## Position: which edge did you cross?

A formation is a **region**, not a point. The announcement is **edge-triggered** — twice over a
pattern's whole life, not once per bar:

- **The first knowable bar** → "*Start of* possible double top forming…"
- **The resolution bar** → "*End of* double top: price closed below…" / "confirmed here…" / "…ends
  here without confirming."
- **Everything in between** → silence.

**The edge word is a property of the BAR, not of the direction you arrived from.** The first version
derived it from travel direction — right meant "Start of", left meant "End of" — which is correct
only when walking into a formation from outside it. Arrowing LEFT across a formation's opening bar
announced "End of", naming the wrong end of the shape at the exact moment the user was trying to
find its beginning. Reported from live use, and invisible to every test because the old signature
took the direction as an argument, so a test could only confirm the mapping it had been handed.

The rule is now positional: the first knowable bar is the start whichever way you crossed it, the
resolution bar is the end whichever way you crossed it, a bar between them is neither. That is the
only arrangement in which the readout is a reliable map — if a bar described itself differently
depending on how the cursor reached it, no picture of the chart could be built by moving around in
it.

The first implementation instead described whatever overlapped the current bar and suppressed
repeats. That sounds equivalent and is not: as the overlapping set churned bar by bar — a flag
dropping out here, a triangle arriving there — the readout kept changing, so the suppression kept
failing, and the user heard a different pile of formations every few bars with no way to tell which
were new.

### No lookahead in the wording

A pattern record carries the outcome the *whole series* eventually produced. Reading it verbatim
announced that outcome at every bar the formation overlapped, including its own starting bar:

> ❌ "Start of double top: price closed below the neckline at 1.072."

at the bar where nothing had happened yet. Every unit test passed — each sentence was individually
well-formed — and it was visible only when the narration was measured across real bars. It is the
same class of defect as the Cipher SR proximity artifact: something anchored to information only
knowable later.

`ChartPatternNarrator.AtBar` now projects every pattern to the state it held **at the requested
bar**, so no consumer can announce an outcome that had not happened. The projection lives there
rather than at each call site because there are three consumers and honesty must not depend on each
remembering to ask.

---

## Overlap: ranked, not resolved

A region genuinely can be an inverse head and shoulders, a double top and an ascending triangle at
once. Two traders looking at it would disagree about which it is, and **so should the terminal** —
picking one and hiding the others would be inventing a certainty that does not exist.

What is a real defect is reading them all at equal weight, which is what turns the feature into
noise. So they are **ranked**, the leader is described in full, and the rest are counted:

> "Start of possible symmetrical triangle forming, trigger 60… **Plus 2 more formations here.**"

The ranking, in order:

1. **Live before resolved.** A forming pattern is a decision; a resolved one is history.
2. **Bigger structure first.** The formation spanning eighty bars is the shape the chart is making;
   the twelve-bar flag inside it is a detail of that shape.
3. **More recently knowable first**, as a tie-break.

Rule 2 is doing the real work, and it was chosen because it is **the only tie-break available that
is not a directional opinion**. Ranking by "which pattern is more reliable" would be precisely the
untested claim the whole feature refuses to make.

`Alt+Shift+D` reads the complete list, ranked, each with its own trigger and target. That is the
right place for it: arrow-key navigation must stay short, but "tell me everything about this bar" is
the one request that should not be summarised.

### Overriding the ranking: the pin

`;` pins one of the overlapping formations so it leads instead; `Shift+;` clears it. The ranking is
the application's ordering, and the pin is how a user says theirs is different — the twelve-bar flag
inside the eighty-bar triangle may be exactly what their setup is built on. The set walked is every
formation whose window covers the current bar, so containers and their contents are equally
reachable, and it wraps.

**A pin scopes `,` and `.` to its own formation.** This was not true at first, and the gap produced
a report that read as the pin not working at all: pinning reordered the *readout* while the jump
keys kept computing their stops from every pattern on the chart, so `;` announced *"leading with
ascending triangle"* and the very next keypress landed on the double bottom's break bar and said
*"double bottom confirmed here."* Every sentence was individually correct, which is why it survived
review — the defect was in which bar the key chose, not in what was said once it got there.

The general lesson, and the third time this feature has taught it: **the pieces were each right and
the composition was wrong.** The edge words, the no-lookahead projection and the pin all behave
correctly in isolation and all three defects to date have been in how they combine. That composition
is now reachable from a test (`AccessibilityFeedbackCoordinator.ChartPatternContext` is `internal`
rather than `private`) instead of requiring the whole navigation stack to be stood up, which is why
it went unchecked for so long. See `ChartPatternPinNarrationTests`.

When **nothing** is live at the cursor, `Alt+Shift+D` names the last formation that finished and how
— *"No formation here. Most recent, 20 bars ago: double top: price closed below the neckline at
42,100."* A formation drops out of the live window the moment it resolves, which is right for
navigation but left the detail key saying "no chart formation here" twenty bars after a neckline
broke. That broken level is usually still the most relevant price on the screen and the pattern is
the reason. The lookup is **backward-only**: a shape not yet knowable at this bar stays
unmentionable, exactly as during navigation.

---

## Ranges: the one formation with two live levels

Flat top against flat bottom used to fall straight through the triangle grid, which handled every
*sloping* combination and let `(0, 0)` return null. So the single most common state a market is in
produced **silence** — the worst possible gap in a feature whose job is to say what the chart is
doing.

A range is the only kind carrying a `SecondaryLevel`, and it needs its own resolve:

```
Forming    "Possible range forming, top 110, bottom 100. Height 10."
Broken     "Range breaks here: closed above the top at 110, measured target 120."
Expired    "Range ends here still intact — price held between 100 and 110."
```

Three deliberate differences from every other shape:

- **Both boundaries are spoken while it is intact.** Naming one would quietly nominate a direction
  the shape has not chosen, and "undecided" is the entire content of a range.
- **No target until a side breaks.** The projection needs a direction; before the break there is
  none. `ResolveRange` therefore returns the direction as an *output* rather than taking it as an
  input — scanning one side only would mis-report every break the other way as the range holding.
- **Expiry reads as "still intact", not "did not confirm".** A range that never broke has not
  failed at anything; it is doing what a range does.

A range must also be at least two tolerance units tall. Below that the "range" is a flat line, and
naming a flat line a formation is noise wearing a technical word.

---

## Measured targets

Spoken, as of this revision, because a trader asking "where does this go if it breaks?" is asking
about a convention they already use, and answering it by ear is the terminal's job.

The framing carries the honesty:

- Always **"measured target"**, never "target".
- On a forming pattern, always **"if it breaks"** — unconditional it reads as a forecast.
- **Never on an expired pattern**: there is no break to project from.
- **Never when the projection lands at or below zero.** The measurement is a subtraction, so on a
  low-priced instrument a tall formation puts the conventional target underneath zero — measuring
  live snapshots produced `measured target -0.0001` on a sub-cent coin. A negative price is not a
  conservative estimate, it is a number that cannot happen, and it is one a user might type into an
  order ticket.

**It has never been tested here.** It is geometry — the formation's height projected from the
trigger — on the same footing as the trigger level itself. Every price-derived pattern claim this
project has tested has come back null (a random horizontal line was respected 59% of the time; real
swing levels held 46.2% against 46.7% for random; fib ratios did nothing across 355,000 tests). If
the measured move were ever to be *scored* rather than described, it would have to clear the same
controls as everything in the edge registry first.

---

## Measured density

`StrategyLab pattern-speech --snapshots strategy-lab-data --tf 1d` walks every bar exactly as the
arrow keys do and counts what would have been spoken. This exists because both real defects in this
feature so far were properties of a *rate*, and a rate is not something a unit test naturally
asserts:

- the relevance window silently collapsed to a single bar (coverage: one announcement per pattern,
  never when panning into the region it described);
- the readout re-fired on every churn of the overlapping set.

Current, across 22 snapshots:

| Metric | Value | Reading |
|---|---|---|
| Speech rate | **7–11% of bars** | Near 0 means the feature is dead; much above 10% means chatter and a user switches it off, at which point it protects nobody |
| Announcements per pattern | **~1.7** | Two is the design — entry and resolution. Materially above means edge detection is re-firing |
| Outcome mix | **confirmed ≈ expired** | No *Expired* at all would mean the resolve scan is unbounded again |

### Every shape is actually found

`pattern-speech` prints a per-kind count and flags any shape at zero, because **a kind that is
defined but never detected is indistinguishable from one that is not implemented** — which is
precisely what the range was until it was measured. Across 82 daily snapshots:

| Shape | Found | | Shape | Found |
|---|---:|---|---|---:|
| bull flag | 2,872 | | symmetrical triangle | 1,343 |
| double top | 2,736 | | range | **1,103** |
| double bottom | 2,570 | | inverse head and shoulders | 933 |
| ascending triangle | 2,374 | | descending triangle | 910 |
| rising wedge | 2,120 | | head and shoulders | 889 |
| bear flag | 1,863 | | falling wedge | 1,541 |

### Timeframe stability

Every tolerance is expressed in **ATR** — the instrument's own volatility — rather than in percent
or currency, so nothing needs recalibrating across timeframes or instruments. Measured on the same
markets at five timeframes:

| Timeframe | Bars | Formations | Speech rate | Per pattern |
|---|---|---|---|---|
| 1h | 59,027 | 2,829 | 8.1% | 1.70 |
| 4h | 89,731 | 4,859 | 9.2% | 1.69 |
| 1d | 393,893 | 20,151 | 8.8% | 1.71 |
| 2d | 17,986 | 860 | 8.1% | 1.69 |
| 1w | 2,132 | 109 | 8.7% | 1.70 |

Those are the two numbers you would want flat across timeframes, and they are — **8.1–9.2%** and
**1.69–1.71**. (Pre-range-detection figures; ranges add roughly 5% more formations without moving
the rate.)

What is counted in **bars** rather than time is the formation size window: `MinPatternBars = 12`,
`MaxPatternBars = 160`. That is intentional — a double top whose two highs are two years apart is
not a double top, it is two highs — but it means a formation is always sized relative to the chart
you are on, never to the calendar. A 12-bar flag is an hour on a 5-minute chart and three months on
a weekly one, and both are flags.

**Cost.** Detection runs once per loaded dataset and is cached (`IChartPatternCache`). About 20 ms
on a 5,400-bar daily chart; about **2.2 seconds on 328,679 intraday bars**. The narration walk in
`pattern-speech` is far slower than that, but it is a measurement artifact — it re-scans every
pattern on every bar, which nothing in the product does.

---

## What is deliberately absent

- **No direction.** Head and shoulders is not called bearish, ascending triangles are not said to
  break up. `triangle-direction-bias` is queued in the edge registry and untested; it would have to
  come back positive before a single directional word could be added.
- **No score, no confidence, no ranking of quality.** A marker in the product's own UI reads as the
  product's endorsement.
- **No candle-pattern jump key.** Dojis and spinning tops occur on a large share of every chart, so
  "jump to the next candle pattern" would usually mean "move one bar right" — a key that does
  nothing the right arrow does not, while consuming a binding. Candle patterns are read on the bar
  you are standing on, which is the right place for something that common.

---

## Heikin-Ashi: detection stays on standard candles

The chart can display Heikin-Ashi, and when it does the spoken open/high/low/close **are**
Heikin-Ashi values. **Formation detection is not** — it always runs on standard candles, and the
terminal says so when HA is switched on with description enabled:

> "Heikin-Ashi candles. Chart formations are still read from standard candles."

Two reasons, and the first is the decisive one:

- **A Heikin-Ashi close is an average of four prices, not a price anything traded at.** The trigger
  and the measured target are the numbers a user might put into an order ticket. A level derived
  from a synthetic average cannot be one of those.
- **HA smooths away the wicks that define the shapes.** A double top's two peaks are wick highs.
  Detecting on HA would find shapes that do not exist in the market and miss ones that do.

The alternative — detecting on whatever is displayed — is what charting platforms generally do with
indicators, and it is exactly why those platforms warn against backtesting strategies on Heikin-Ashi:
the fills are not real. Disclosure is the honest resolution here; recomputing on HA would produce
levels that read as prices and are not.

---

## The announcement rate is an output, not a dial

**Roughly 5 formations per 100 bars, and 8–9% of bars carry an announcement**, holding steady from
1-hour to weekly. Nothing exposed to the user changes it, and nothing should: it is what the
detector finds, and the ATR-relative tolerances are what keep it stable across instruments and
timeframes.

It is re-measured after every change to the feature because both real defects found so far were
properties of a *rate* rather than of any single sentence — one version announced each formation on
exactly one bar and never again, and every unit test still passed.

The knobs exist (`ChartPatternOptions`: `Span`, `ToleranceAtr`, `MinPatternBars`, `MaxPatternBars`,
`MinSwingAtr`) and are deliberately not surfaced. A sensitivity slider would let a user tune the
density, and would also let them tune the detector into agreeing with whatever they already believed
about the chart — which is the one thing a descriptive feature must not offer.

**Nested formations are correct output.** A large inverse head and shoulders containing two
ascending triangles is the same relationship a paragraph has to its sentences. The ranking exists to
name the larger shape first, not to suppress the smaller ones.

---

## How a user is meant to act on this

The narration is deliberately mechanical, so the interpretation belongs in the manual rather than in
the code. `USER_MANUAL.md` → *Analysis Tools* → *Chart formations* carries it, and the one point
worth repeating here because it is so easy to invert:

**On a double top, a break of the neckline is the pattern SUCCEEDING.** The neckline is the trough
between the two highs — it is support — so a close below it is the top doing what a top does, and it
is the conventional short. A neckline that *holds* means the double top failed to confirm, support
was tested and survived, and the conventional read flips bullish. Both are useful and they point
opposite ways, which is the whole reason the narration reports **what price did** rather than
whether the pattern "worked".

The forming announcement also carries a second, earlier trade: hearing "possible double top forming"
means price has returned to a level it was already rejected from once. Acting there — the second
top, before the neckline is near — is a better price, a tighter stop above the twin highs, and a
much lower hit rate, because most possible double tops never become double tops. The terminal
surfaces both moments and takes no view on either.

Cross-references: `SHORTCUTS.md` · `USER_MANUAL.md` (Chart formations) · `ALPHA_LEDGER.md` ·
`LAB_DESIGN.md` · `PLATFORM_AND_SIGNAL_SERVICE.md` (describe freely, score never).
