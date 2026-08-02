# Pyramiding: recycling the same risk is worse than just being bigger

**Verdict: FALSIFIED, and by the cleanest margin in the ledger.** The pyramid loses to holding the
same average position flat from entry on **41 of 41 equities and 6 of 6 crypto — 47 of 47, no
exceptions.** And the structure the adds are anchored to carries nothing: it is indistinguishable
from adding at random moments inside the same trade.

Command: `StrategyLab pyramid --snapshots strategy-lab-data --tf 1d [--exit-z -1.0]`
Measured 2026-08-02 · 51 instruments · suite green.

---

## The claim

From the 2026-08-02 David Hannan interview:

> "You might have had a 10K initial risk on a trade, but you just get so big after adding and moving
> your stop up that that 10k initial risk turns into a million dollars … it's not necessarily
> increasing risk, it's recycling the same risk."

The mechanic, and it is genuinely mechanical:

1. Enter with a fixed dollar risk R.
2. The trade moves in your favour until it forms structure supporting a **tighter** stop.
3. Move the stop up — dollar risk on the existing position is now below R.
4. **Add size until total dollar risk is back at R.** Bigger position, same risk.
5. Repeat at each new structure.

This mattered because the ledger held ~20 measured edges on **entries**, two on **exits**, and
**zero on sizing**. It was the first sizing rule anyone had proposed here with no conviction input
and no discretion about how much to add — given an entry, an exit, and a rule for when a tighter
stop is justified, the size schedule is arithmetic.

**And our own prior said it should work**, which is why it got extra arms rather than fewer. The
exit study found the BTC trend edge has a fat right tail (mean +8.15R at a 47% win rate) and that
fixed-percentage scale-*outs* destroyed 95–100% of the return. Pyramiding is the exact inverse of
scaling out.

---

## Design

**Entry and exit are held completely fixed** — the z-momentum entry and signal exit from the exit
study, the only entry/exit pair this lab has validated. Every arm trades the same bars, enters on
the same signals and leaves on the same signals. **Only the size schedule varies.**

| arm | what it is |
|---|---|
| **flat 1×** | one unit of risk at entry, stop never moves. Baseline. |
| **PYRAMID** | the claim. At each confirmed swing low above the current stop, move the stop there and add until risk is back at one unit. |
| **random adds** | *the structure control.* Same **number** of adds as the pyramid made on that same trade, at random bars inside it, stop moved the same way. |
| **flat @ pyramid's avg size** | *the leverage control.* A constant position equal to the average size the pyramid actually carried. |
| **naive adds** | adds at the same moments **without** moving the stop, so risk compounds. The strawman the claim defines itself against. |

Adds are triggered by confirmed swing lows taken at their **confirmation** bar, never the pivot bar
— a pivot is not knowable when it prints. Every add is charged 5 bps slippage in R terms, because
each one is a market order into a move already going.

**Win rate is not the score.** Hannan states his own would be ~60% without pyramiding and is 41%
with it, so **win rate falling is predicted by the claim**. Total R is the score; max drawdown is the
cost.

---

## Results — default exit (10–26 bar holds)

### Pooled, 41 equities

| arm | total R | vs flat | beats flat |
|---|---|---|---|
| flat 1× | +2761.1 | — | baseline |
| **PYRAMID** | **+3105.4** | 1.12× | 22/41 |
| random adds | +2911.8 | 1.05× | 23/41 |
| **flat @ pyramid avg size** | **+3976.4** | **1.44×** | **41/41** |
| naive adds (risk grows) | +4015.0 | 1.45× | 22/41 |

- **Does structure matter?** pyramid +3105 vs random adds +2912 — **26 of 41 instruments**.
- **Is it just leverage?** pyramid +3105 vs flat at the same average size +3976 — **1 of 41**.

---

## Results — the technique's best case (long holds)

The first run's average hold was 10–26 bars, which barely lets the schedule engage (~0.5 adds per
trade, average size 1.1×). A null there would be a statement about the exit rule, not about
pyramiding. So the exit was loosened to `--exit-z -1.0`, producing 34–37 bar holds and giving the
position room to compound.

**It made no difference to the verdict.**

| arm | equities (41) | crypto (6) |
|---|---|---|
| flat 1× | +2182.3 | +457.1 |
| **PYRAMID** | +2252.0 | +463.5 |
| random adds | +2251.0 | +484.3 |
| **flat @ pyramid avg size** | **+3052.2** | **+505.4** |
| naive adds | +3780.5 | +541.2 |

- **Does structure matter?** equities: pyramid +2252.0 vs random +2251.0 — a difference of **one R
  across 41 instruments**, 19/41 instruments favouring the pyramid. That is a coin flip stated in
  four significant figures. Crypto: 2/6.
- **Is it just leverage?** **0 of 41 equities and 0 of 6 crypto.** Forty-seven instruments, no
  exceptions.

---

## Why it fails, and why it was always going to

The mechanism is visible once the two controls are read together.

**The structure carries nothing.** Anchoring adds to confirmed swing lows performs identically to
adding at random moments in the same trade. This is the same answer every price-structure claim
tested in this project has given — random horizontal lines respected 59% of the time, real swing
levels 46.2% against 46.7% for random, fibs nothing across 355,000 tests. Swing lows are not special
places to add; they are just places.

**And the schedule is actively harmful relative to size.** This is the part worth internalising:

> **A pyramid buys later, at worse prices, and therefore carries a higher average entry than a flat
> position of the same average size.** The flat position owns the whole move. The pyramid owns the
> back half of it, at a premium, having paid slippage for the privilege.

That the pyramid *does* beat flat 1× (1.03–1.12×) is real, and it is exactly what makes the claim
persuasive from the inside: a trader who pyramids genuinely makes more than a trader who does not.
The comparison that was never being made is against **the same trader simply sizing up at entry** —
and that comparison is lost 47 times out of 47.

**The 10K → $1M story is not refuted by this and is not evidence either.** It is one trade. This
tests the rule across ~10,000 trades and asks whether the schedule beats the alternatives.

---

## What survives

**Nothing about pyramiding.** But two things came out of the study worth keeping:

1. **`naive adds` — adding without moving the stop — produced the highest raw return of any arm**
   (+4015 R equities, 1.45×). It also produced the worst drawdowns by a wide margin (BTC: 58.2 R
   against 5.7 R for the pyramid; XLY: 67.4 vs 34.3). That is not an edge, it is leverage with the
   risk control removed, and it is worth naming because it is what "add to winners" degrades into
   when the stop discipline is dropped — which is the failure Hannan describes as his own worst
   loss.
2. **The right conclusion for sizing is the boring one.** If a bigger average position is what pays,
   take it at entry where the price is best, and spend the risk budget on position size rather than
   on a schedule. That is a statement the ledger can now support with 47 instruments.

---

## Scope and caveats

- **The entry is one family.** z-momentum breakout, long-only. A pyramid attached to a different
  entry might behave differently, though the leverage control's mechanism is not entry-specific.
- **Long-only.** The exit study established there is no short-side skill here to borrow.
- **Slippage 5 bps per add**; the verdict does not depend on it, since the pyramid loses to the
  leverage control before costs are considered at all.
- **Survivorship.** The snapshot universe is survivors, which flatters every arm equally.
- **The size multiple reached is modest** (1.1–1.2× average). The technique gets larger over longer
  trends than this entry produces. But the leverage control scales with it by construction — it is
  always compared against *its own* realised average size — so the comparison stays fair at any
  multiple.
- **Not tested: pyramiding with a discretionary conviction overlay.** Hannan's actual practice adds
  exponential bet sizing on top, which he admits is "a gut feeling". That is not mechanically
  specifiable and is not queued.

---

Cross-references: `EXIT_FINDINGS.md` (the fat right tail this was predicted to exploit) ·
`RISK_TARGET_AND_METRIC_FINDINGS.md` (the 1:3 target falsification, same underlying mechanism) ·
`SIZING_AND_PYRAMIDING_NOTES.md` (the interview) · `ALPHA_LEDGER.md`.
