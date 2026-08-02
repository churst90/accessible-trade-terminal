# Chart formation narration

How the terminal describes double tops, head and shoulders, triangles, wedges and flags by ear —
and, more importantly, what it refuses to say about them.

Revised 2026-08-02 after live use. Suite 2743 green. Narration density measured on 22 real snapshots
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

- **Crossing in, moving right** → "*Start of* possible double top forming…"
- **Crossing in, moving left** → "*End of* double top: price closed below…"
- **The resolution bar** → "Double top confirmed here…" / "…ends here without confirming."
- **Everything in between** → silence.

The edge word is the point. Moving forward in time you meet the structure first and its outcome
later; moving backward you meet the outcome first. Without naming the edge, both sound identical and
there is no way by ear to tell which way through the formation you are travelling.

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

Cross-references: `SHORTCUTS.md` · `ALPHA_LEDGER.md` · `LAB_DESIGN.md` ·
`PLATFORM_AND_SIGNAL_SERVICE.md` (describe freely, score never).
