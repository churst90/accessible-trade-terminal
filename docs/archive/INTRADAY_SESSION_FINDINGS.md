# The late-session move, and the hour-of-day claims

**Verdict: both NULL.** The 15:00 window ranks 4th of 7 hours for follow-through. Mean
follow-through correlation across every hour of the session is **+0.0014** — a number
indistinguishable from zero.

Command: `StrategyLab late-session --snapshots strategy-lab-data`
Measured 2026-08-02 · SPY, QQQ, IWM, DIA, AAPL · 2016-01 → 2026-07 · ~2.2M 5-minute bars.

---

## What was blocked, and what unblocked it

Two practitioners, four days apart, each produced an hour-of-day claim, and both sat in the registry
marked **blocked on US equity intraday data**. That was the clearest data-acquisition signal the
video series generated. An Alpaca key resolved it.

**What the key turned out to buy** — more than expected, and worth recording:

| | |
|---|---|
| feed | **SIP**, the full consolidated tape, not IEX |
| intraday depth | **5-minute bars back to 2016-01-01** |
| daily depth | 2016 |
| rate limit | 200 req/min |
| cost | free with the paper account |

The default feed had been silently in play and it matters: **IEX is a single venue carrying roughly
2% of consolidated volume, and its history only reaches 2022.** The provider now requests SIP,
falls back to IEX once if the account is not entitled, and says which one it got.

---

## The claims

**Peter Tuchman** (NYSE floor broker since 1985):

> "At 3:00 and at 3:30 usually the market make a big move … your retail audience should know that you
> should rather be on the same side of the move that the market does at 3 and 3:30 rather than trying
> to be counterintuitive."

He offers a *mechanism* — closing-bell order flow populates brokers' handhelds at 14:00 and updates
through the afternoon, so the late move is the market absorbing imbalance information. That made it
worth a real test rather than a dismissal.

**David Hannan:**

> "You tend to get a lot cleaner moves around open … if you're trying to buy a breakout at like 1:00
> p.m. I found it's just it tends to be lower volume and lower follow-through."

---

## The control

**Every hour of the session gets the same numbers, printed side by side.** "The market moves at 3pm"
is true of every hour if you never compare, and every hour of the session has someone who swears by
it. A claim that singles out one hour has to show that hour standing out.

Two things are measured and deliberately kept apart:

- **Magnitude** — how far price travels in the hour. Always the *full* hour, so rows are comparable.
- **Follow-through** — given the hour's move, does the next interval continue it? This is the actual
  claim ("be on the same side of the move"). For 15:00 it uses 15:00–15:30 predicting 15:30–16:00 so
  the windows do not overlap.

Continuation rate is tested against a **sign-flip null**: the forward return's sign is randomised,
which destroys the directional relationship while preserving both series' magnitudes exactly. The
null is "the next window moves independently", not "returns are zero".

Regular trading hours only, and **DST-aware** — converted through `America/New_York` rather than a
fixed −5 offset. A fixed offset would shift the whole study by an hour for eight months of the year,
which in a study *about the hour of day* is not a rounding issue.

---

## Results

### Pooled across 5 instruments

| window (ET) | mean continuation | mean follow-through r | instruments > 50% |
|---|---|---|---|
| 09:30–10:30 | 50.87% | −0.0565 | 4/5 |
| 10:00–11:00 | 50.89% | +0.0048 | 4/5 |
| 11:00–12:00 | 50.36% | +0.0177 | 3/5 |
| 12:00–13:00 | 49.67% | +0.0314 | 2/5 |
| 13:00–14:00 | 49.91% | −0.0406 | 2/5 |
| 14:00–15:00 | 49.81% | +0.0138 | 2/5 |
| **15:00–15:30** | **50.03%** | +0.0391 | 2/5 |

**Tuchman:** continuation at 15:00 is **50.03%**, against **50.25%** across the other hours. The
window ranks **4th of 7**. Not supported.

**Hannan:** open 50.87% vs 13:00 49.91% — a 0.96 point gap, on the same order as the spread between
any two adjacent hours, and the open's follow-through *correlation* is the most **negative** of the
session (−0.0565), meaning the opening move slightly **fades** rather than continues. The two halves
of his claim point in opposite directions. Not supported.

**Mean follow-through correlation across all hours: +0.0014.** For scale, Narang's number for a
*successful* quantitative strategy's out-of-sample R² is 0.03–0.04. This is two orders of magnitude
below that.

---

## What is true, and why the belief survives

**The volatility smile is real.** Mean absolute move is largest at the open and into the close and
smallest at midday, on every instrument. So "the market makes a big move at 3" is **correct as a
statement about magnitude** — there is more movement then than at 12:30.

**It just carries no directional information.** The move is bigger; which way it goes next is a coin
flip. The claim conflates *volatility* with *predictability*, and the first is easy to notice from
the floor while the second requires counting.

That is a satisfying explanation for how a four-decade practitioner arrives at a belief that does
not survive measurement: he is describing something real, and drawing the wrong inference from it.
It is the same shape as the regime-persistence result — the perception is honestly earned and the
conclusion does not follow.

---

## Scope and caveats

- **SPY, QQQ, DIA and IWM are largely one portfolio.** The instrument counts are not independent
  votes; AAPL is the only genuinely separate name.
- **7 windows × 2 claims = 14 comparisons.** At α = 0.05 expect 0.7 by chance. Nothing came close,
  so no correction was needed — worth stating because the same count would matter if something had.
- **Costs are not modelled.** A half-hour equity round trip would consume an edge far larger than
  anything here, so a positive result would still have needed a cost pass. A null does not.
- **The MOC imbalance itself remains untested and unavailable.** Tuchman's *actual* edge — seeing
  closing-bell order flow at 14:00 while the public sees it at 15:50 — is information latency from a
  floor seat. It is not replicable at any price, and the public 15:50 dissemination version needs a
  paid exchange feed. `moc-imbalance-pressure` stays blocked.
- **2016–2026 only**, which is one regime-rich decade but a single sample of market structure.

---

## The registry consequence

`late-session-drift` moves from Untested to **Falsified**. Hannan's time-of-day claim is folded into
the same record rather than queued separately, since one command tests both.

Cross-references: `REGIME_PERSISTENCE_FINDINGS.md` (same shape of result — a real perception,
a wrong inference) · `SIZING_AND_PYRAMIDING_NOTES.md` · `ANALYTICS_DATA_PROVIDERS.md` (the Alpaca
feed and pagination fixes this study forced).
