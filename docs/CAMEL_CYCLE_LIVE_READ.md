# Camel Finance, live — how the cycle system is actually used

Source: <https://www.youtube.com/watch?v=JoRNW_4jCVc> · analysed 2026-08-04 · 2,759 words.

This is **not a tutorial**. It is a daily market update, which makes it more useful than a tutorial:
you get to watch the method being applied to an undecided situation rather than to a chart whose
outcome is already known. That distinction matters here more than anywhere, because our own test of
this system (`CYCLE_FINDINGS.md`) killed it as a *detector* — and this video shows the detector is
not really what he is using it for.

---

## The numbers he actually states

These are the first hard, testable parameters from this source. `camel-cycle-spec` in memory has the
BTC 54–66 day daily cycle; this adds the layer above it.

| Level | Length | Structure |
|---|---|---|
| **Daily cycle** | ~60+ days — "Friday would be **day 63**, right in time for a daily cycle low" | — |
| **Weekly cycle** | **28–35 weeks, average 30–31** — he enumerates 31, 32, 28, 35, 32, 28 | **three daily cycles plus an inversion** |
| **Yearly / 4-year** | — | weeklies nest inside it |

He also cites a Fibonacci overlay from "Tony": **144-day candles**, three of them to print the cycle
low, then nine to produce the bull market.

**The falsifiable core, stated plainly:** weekly cycle lows should be roughly equally spaced at
~31 weeks, and each weekly cycle should decompose into three daily cycles plus one inversion. He
rejects a rival forecast (an October bottom) *specifically* because it would require a weekly cycle
containing only two daily cycles, and "all of these weekly cycles are pretty equally spaced apart".

That is a real, checkable claim about spacing regularity, and it is **not the claim we tested**. Our
cycle work tested whether the *daily* cycle length was distinguishable from a swing-detector
artifact on shuffled surrogates — it was not. Spacing regularity of the weekly level, and the
3-dailies-per-weekly decomposition, are untested here.

---

## What he says the system is FOR — and it is not prediction

This is the most important thing in the video and it reframes our own null:

> *"No one really cares about the price here. **We're looking for the high probability low to manage
> the risk around.** We're looking for that low to skew the probability in our favor so that we know
> we can have a significant probability of being correct versus getting stopped out."*

That is a **risk-placement claim, not a forecasting claim.** It says: near a projected cycle low, a
stop placed below it is less likely to be hit than a stop placed at an arbitrary point. That is
testable and it is *different* from "cycle lows predict returns", which is what
`CYCLE_FINDINGS.md` falsified.

He is also explicit that the low is a probability zone rather than a date — "day by day, week by
week, one cycle at a time, we'll get to find this low."

---

## The unfalsifiability, in his own words

He is candid about it, which is to his credit and does not make it less fatal for testing:

> *"We will learn whether or not this is an inversion based on what happens out of this daily cycle
> low. If this is a higher low, then we can talk about counting the inversion. If this sweeps both
> of those lows, then this is just a failed cycle… **with the new data presented … we'll be able to
> then reassess this count**."*

**The count is revised as price arrives.** Two escape hatches — *inversion* and *failed cycle* —
absorb any outcome. A rule that can relabel its own history after the fact will fit anything, and
this is precisely the retrospective-selection trap recorded in `camel-cycle-spec`: features chosen
by looking back will have the claimed spacing *because they were chosen that way*.

Note what this means for testing: **any test must fix the labelling rule in advance and apply it with
a confirmation lag.** A test that adopts his live count is testing his judgement, not the system.

---

## The one genuinely new testable claim

> *"When we overextend an extra right translate like this, we typically **come down harder and
> faster** to correct the four-year cycle. Rather than have this kind of 10 to 12 month orderly walk
> down, we tend to **puke into the low** after heavily right translating."*

**Right translation → faster, deeper decline into the next cycle low.** This is mechanically
testable and it is a *conditional shape* claim rather than a timing claim:

- Define the cycle algorithmically (fixed span, confirmation lag).
- Measure translation: where in the cycle the high printed, as a fraction of cycle length.
- Measure the decline: peak-to-trough depth and the *duration* of the drop into the next low.
- Ask whether high translation predicts a steeper or shorter decline.
- **Control:** the same measurement on phase-randomised surrogates. If translation is a detector
  artifact — and our cycle work found the cycle length itself was — this correlation will appear in
  surrogates too.

Our earlier finding is directly relevant and should be a stated prior: **translation is momentum in
cycle vocabulary** (`CYCLE_FINDINGS.md`), and it splits crypto/equity like everything else. A cycle
that peaks late is one that trended; the "harder correction" claim may be a restatement of "what
went up fast comes down fast", which is testable against a plain trailing-return control.

---

## What is worth doing with this

**Queue as `Untested`, do not re-open the old one.** Two distinct new claims:

1. **`weekly-cycle-spacing-regularity`** — are weekly lows spaced more regularly than
   surrogate-derived lows? He gives 28–35 weeks with a ~31 average, which is a tight enough band to
   falsify.
2. **`right-translation-predicts-decline-shape`** — does a late cycle high predict a faster/deeper
   drop, beyond what trailing return alone predicts?

**Do not test "cycles find lows" again.** That is `camel-cycle-counts`, already Falsified with 200
surrogates.

**The risk-placement framing is the interesting one and the hardest to test honestly**, because it
requires committing to a projected low *in advance*. It is a natural candidate for the forward
recorder pattern: log the projected low as of today, then score it later. Nothing else makes it
falsifiable, for exactly the reason the count is revised in flight.

---

## A note on the presentation

He repeatedly frames the market as engineered against retail — *"the best way to hurt the most
amount of market participants here is to let all the guys that are going to buy in October buy only
to see that daily cycle left translate and fail."* That is not evidence and should not be treated
as such, but it is a good tell for the general shape of an unfalsifiable narrative: any outcome
becomes confirmation, because being wrong was the market's intent.

To be fair, he says three separate times that he is "open to all possible outcomes", holds a live
short, and states his reasoning as probability rather than certainty. He is doing discretionary
trading with a framework, and describing it honestly. The framework is just not, as stated, a thing
a backtest can hold still.

Cross-references: `CYCLE_FINDINGS.md` · `ALPHA_LEDGER.md` · memory `camel-cycle-spec`.
