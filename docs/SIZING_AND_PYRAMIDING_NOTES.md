# Sizing, pyramiding, and the David Hannan interview

Notes from the 2026-08-02 Titans of Tomorrow interview with David Hannan ("Laptop Legend"), a
full-time small-cap short seller ~4.5 years in. Transcript pulled and read in full (20,549 words).

**Why this one matters more than the previous five videos in the series.** The trader's-triangle
interview split trading into strategy, risk management and trade management, and this project's
ledger answered it uncomfortably: about twenty measured edges on **entries**, two on **exits**, and
**zero on sizing**. Every video since has produced more entry claims. This one is almost entirely
about sizing, and its central rule is mechanically specifiable — which is the rare combination.

Five claims are queued in `edges.json`. None are tested yet.

---

## The core idea: pyramiding is risk *recycling*, not risk *adding*

> "You might have had a 10K initial risk on a trade, but you just get so big after adding and moving
> your stop up that that 10k initial risk turns into a million dollars … it's not necessarily
> increasing risk, it's recycling the same risk."

The mechanic, stated precisely:

1. Enter with a fixed dollar risk — say $10,000 — defined by entry minus stop times size.
2. The trade moves in your favour until price forms a **new structure** that supports a **tighter**
   stop (his examples: a wash-and-reclaim of a support level, a new higher low).
3. Move the stop up. Dollar risk on the existing position is now *less* than $10,000.
4. **Add size until total dollar risk is back at $10,000.** The position is bigger; the risk is not.
5. Repeat at each new structure.

This is the part worth separating from the mystique. It contains **no conviction input**, no
discretion about how much to add, and no "risk more when you feel good". Given an entry, an exit,
and a rule for when a tighter stop is justified, the size schedule falls out arithmetically. That
makes it testable, which is more than can be said for most of what these videos contain.

**Our own ledger predicts this should work — which is precisely why to be careful.** The exit study
found the BTC trend edge has a fat right tail: mean **+8.15R at a 47% win rate**, and fixed-percentage
scale-*outs* destroyed **95–100%** of the return. Pyramiding is the exact inverse of scaling out. A
positive result would agree with something already measured, and agreement with a prior is how
results get believed without being checked.

**The controls the queued record demands:**

| control | why |
|---|---|
| Exposure-matched null | Adding to winners mechanically raises exposure during trends. In a trending asset that wins for the same reason buy-and-hold wins. |
| Hold entry and exit signals **fixed**, vary only the size schedule | Otherwise the test re-measures the entry rule. |
| Report **total R**, not win rate | He states his own win rate would be ~60% without pyramiding and is **41%** with it. Win rate moving the wrong way is *predicted by the claim*, not evidence against it. |
| Model slippage on the adds | Every add is an order into a move that is already going. |

---

## The calibration number, and why it is the most valuable thing in the interview

> "I looked at the data for 2024 … the top 14% of my days accounted for like 82% of my profits."

**This project measured the same shape independently.** The 2026-08 queue results found that the
**top 10% of trades carry 101–245% of total R** — that is, the rest of the book is at best
break-even and often negative. That was a backtest result across our own strategies. This is a
practitioner's actual brokerage P&L over a calendar year, arrived at independently.

Two measurements of very different provenance landing on the same extreme concentration is the
strongest corroboration in the ledger of anything, and it has a direct design consequence:

> **A sizing rule that treats all opportunities alike is throwing away most of the available return,
> and an exit rule that caps the right tail is throwing away all of it.**

That is also the honest reason the 1:3-target claim was falsified here, and the reason fixed
scale-outs failed. Three separate results, one mechanism.

His prescription from the same observation is **exponential bet sizing** — not 1% on everything, but
roughly *eight times* normal risk on the rare A+ setup. He is explicit that his A+ risk is $100–200k
while the Bitcoin breakout that made $1M started at a $10k risk. **This part is not testable as
stated**: the conviction ranking is admitted to be a gut feeling ("I'm not systematized about that
either, to be honest"). Encode it as an unverified process aid if at all, never as a scored input.

---

## Other claims, queued

- **`first-red-day-reversal`** — after several consecutive up days with expanding range, expanding
  volume and gaps, the first break of the prior day's low reverses. His most-repeated setup, and
  fully mechanical. Comes with a transfer rule he attributes to Jack Kellogg: it is a small-cap
  pattern that works on large caps only "when large caps start behaving like small caps", which is a
  testable conditioning statement rather than hand-waving.
- **`breakout-vs-meanreversion-winrate`** — "it's a fact if you go look at the data". Cheap to check.
  The asymmetry to declare *before* running it: he accepts a lower win rate for a better payoff, so a
  result where breakouts lose more often and still earn more **confirms** him.
- **`triangle-direction-bias`** — ascending breaks up, descending breaks down, symmetrical is 50/50.
  Queued specifically because the terminal is gaining an opt-in chart-pattern describer, and this is
  the claim that would have to be true before a pattern readout could carry any directional hint.
  The prior is bad: every price-derived pattern claim tested here has come back null. His own
  "symmetrical is 50/50" is a usable internal control — a detector with no information produces the
  same rate for all three types.
- **`adr-selection-for-swings`** — rank swing candidates by average daily range. Sounds obvious and
  is not: the claim is about *returns* and the mechanism offered is about *range*. The decisive
  control is risk-matched comparison, which removes the range advantage by construction. If high-ADR
  still wins at matched risk the claim has content; if not, it is a leverage statement in disguise
  and the cheaper answer is to size up on the low-ADR names.

**Blocked:** his time-of-day claim (breakouts near the open follow through better than 13:00–14:00
breakouts) needs US equity intraday — the same blocker sitting on `late-session-drift` from the
Tuchman interview. Two separate practitioners have now produced an intraday-equity claim we cannot
test. That is the clearest data-acquisition signal the video series has generated.

---

## Practitioner calibration worth keeping

Numbers from someone with a real P&L are often worth more than the strategy attached to them.

| | |
|---|---|
| career win rate | **41%**, deliberately — "I would rather sacrifice win rate to have amazing risk reward" |
| profit concentration | top **14%** of days = **82%** of 2024 profit |
| drawdown discipline | sub-2% from all-time highs for several years — and he believes it is **holding him back** |
| a 28% drawdown | took **over a year** to recover |
| worst single loss | **$850k in an hour**, and it came *after* he found success, not before |
| years unprofitable | 2016–2018, before anything worked |

The drawdown line is the interesting one. He treats a sub-2% maximum drawdown as a *constraint he
imposes on himself* and identifies it as the thing capping his growth — the opposite of how
drawdown is usually discussed. It also means his stated returns come with a risk profile most
published track records do not have.

---

## Two things to notice about the series as a whole

**He had never heard of the COT report.** Asked directly whether he looks at positioning data:
*"Honestly, I've never even heard of that report."* Our COT tests were null from both available
sources, and the S&P and Nasdaq — roughly 90% correlated — gave *opposite* significant answers. This
is now the second practitioner in this series who does not use the vocabulary that dominates retail
education; Tuchman had never heard of ICT or smart-money concepts either.

**His stated source of edge is market inefficiency, not pattern-reading.** He says small caps carry
more edge than futures or forex because those are "more efficient — less exploitable opportunities",
and that big players "arbitrage all of those inefficiencies away" in liquid markets. That is a
capacity argument, and it is correct in form. It also implies the honest limit on transferring
anything from this interview: **the venue where he claims his edge lives is one where our data
coverage is zero**, and where he himself reports 5–10% slippage on stops.

---

## What this changes about the plan

Sizing goes from "the untested corner" to **the corner with a mechanically specified candidate and a
favourable prior from our own exit work**. `pyramiding-risk-recycling` is the single most promising
untested claim in the registry, and unlike an entry rule it can be layered onto the edges that are
already control-tested rather than competing with them.

Cross-references: `EXIT_FINDINGS.md` (the fat right tail this depends on) ·
`RISK_TARGET_AND_METRIC_FINDINGS.md` (the 1:3 target falsification, same mechanism) ·
`ALPHA_LEDGER.md` · `.claude/skills/strategy-research/SKILL.md`.
