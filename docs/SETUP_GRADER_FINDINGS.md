# Setup grader for the Cipher B Buy dot: findings

Run 2026-10-18. Stage 2 of 2. The plan is `docs/SETUP_GRADER_PREREG.md`, committed (0597d7de)
before any result existed. The harness is `StrategyLab setup-grader`
(`AccessibleTrader.StrategyLab/SetupGraderCommand.cs` and `SetupGraderCore.cs`). It was committed
(9bbced00, then aab4c5b1) before the first run that computed any outcome, so every literal reading
below was fixed before a result was seen.

## The verdict, in the pre-registration's words

**Crypto daily: No edge. Equities daily: No edge.**

In both classes the reason is the same, and it comes before any p-value. **Grade A almost never
happens.** In the development era (trigger bars before 2023) there were **0** grade A Buy dots in
crypto and **2** in equities. The pre-registration's section 7.4 says a grade with fewer than 15
triggers is "not testable": its count is reported and nothing else. So P1 (ExA > 0) and P4
(ExA − ExC > 0) could not be run, and section 8's first two no-edge conditions hold by default.

In section 8's own sentence: **the hierarchy adds nothing to the bare Buy dot that this test can
detect, and the Buy dot inside good context is no better than a random bar in the same context.**
The honest version of that sentence here is stronger and plainer: as written, the hierarchy almost
never produces a grade A setup, so there is nothing to test it on.

Two pre-registered results did come out significant after Holm, both in equities, and neither is
about the hierarchy. They are reported in their place below and are not promoted to a headline
(section 8 forbids it):

- **P3, equities:** grade C Buy dots (non-vetoed, not A or B) beat random bars in the same context,
  excess +0.115R net, p 0.0014, Holm 0.011.
- **S1, equities:** vetoed Buy dots do worse than grade C, by 0.125R, p 0.0024, Holm 0.029. In
  crypto the point estimate goes the other way (vetoed dots did better than C), which the
  pre-registration says the write-up must state: in crypto the vetoes look wrong, on small counts.

## How many setups of each grade

Long Buy dots ("Oversold Crossover", the MCB rule), graded. Dev = before 2023-01-01, held-out =
2023 onward.

**Crypto daily (ten assets):**

| Grade | Dev | Held-out |
|---|---|---|
| A | 0 | 0 |
| B | 4 | 17 |
| C | 44 | 31 |
| V (vetoed) | 105 | 101 |

Excluded as undefined: 132 (83 with no weekly trend or phase, 38 with weekly money flow still in
warm-up, 11 with no 20-bar outcome).

**Equities daily (33 assets):**

| Grade | Dev | Held-out |
|---|---|---|
| A | 2 | 0 |
| B | 90 | 14 |
| C | 1,426 | 144 |
| V (vetoed) | 2,641 | 228 |

Excluded as undefined: 169 (89 context, 70 weekly money flow, 10 no outcome).

**Crypto 4h (S4, seven assets):** dev A 0, B 19, C 152, V 465; held-out A 1, B 35, C 157, V 458.

Which veto fired, development era (a dot can trip more than one): crypto V1 47, V2 59, V3 61;
equities V1 1,369, V2 1,516, V3 1,301.

The mirror sell setup (WaveTrend Cross Bear with the slow wave at +60 or above) is just as starved:
grade A dev 2 in crypto and 14 in equities. The shipped red dot, run as the named alternate, has the
same counts of A (2 and 14).

### Why grade A is so rare (counts only, no outcome)

Grade A needs three things at once: context Good (own weekly trend Up and the phase Bull), location
yes, and at least two of the three confirmations. The counts-only run shows where it breaks:

| Development era | Crypto | Equities |
|---|---|---|
| Bars with context Good | 17.4% | 18.7% |
| Buy dots with context Good | 6 of 153 | 40 of 4,159 |
| ... of those, with location | 5 | 36 |
| ... of those, with 2+ confirmations | 0 | 2 |

Two things combine:

1. **A Buy dot almost never fires in good weekly context.** About 18% of all bars are context Good,
   but only 4% of crypto Buy dots and 1% of equity Buy dots are. A wave at −60 or below on the daily
   chart usually comes with a weekly close below the last weekly low, which turns the weekly trend
   off. This is the same thing the 2026-10-17 battery found one timeframe down (K1: the dot almost
   never fires while the daily EMA 11 is above the EMA 33).
2. **The confirmations are close to mutually exclusive with the trigger.** D2 needs the daily money
   flow to have risen two bars running, and D3 needs the weekly money flow rising. At a deep
   oversold cross, money flow is usually still falling. Location, by contrast, is not the bottleneck:
   most Good-context dots had it.

The pre-registration did not anticipate this (it worried that grade A might be below 30; it did not
consider below 15). Nothing was changed because of it. Changing the confirmation count or the
context rule now would be exactly the reframing section 8 forbids.

## The primary family (8 tests, Holm inside the family)

Development era, net R (15 bps a side), excess over the matched random entry. "Not testable" means
fewer than 15 triggers, so no statistic was computed.

| Test | n | Ex | p raw | p Holm |
|---|---|---|---|---|
| P1 crypto, ExA > 0 | 0 | not testable | | |
| P2 crypto, ExB > 0 | 4 | not testable | | |
| P3 crypto, ExC > 0 | 44 | −0.140 | 0.765 | 1.000 |
| P4 crypto, ExA − ExC > 0 | 0 vs 44 | not testable | | |
| P1 equities, ExA > 0 | 2 | not testable | | |
| P2 equities, ExB > 0 | 90 | −0.004 | 0.476 | 1.000 |
| P3 equities, ExC > 0 | 1,426 | +0.115 | 0.0014 | **0.011** |
| P4 equities, ExA − ExC > 0 | 2 vs 1,426 | not testable | | |

**The ordering test (A > B > C):** not testable in either class, because A is below 15. Where B
and C can be compared (equities), B is below C on both measures (mean net R −0.095 against +0.030;
Ex −0.004 against +0.115). So even the part of the ordering that can be seen runs the wrong way.

**The A∪B fallback (section 7.4).** Grade A was below 30 in both classes, so the pooled "A∪B"
grade was run against C. It is reported as Suggestive whatever its p, and is not in the Holm family.

- Crypto: A∪B has 4 triggers: not testable.
- Equities: Ex(A∪B) −0.026 (n 92, p 0.53); Ex(A∪B) − ExC −0.141 (p 0.84). Nothing, and the wrong
  sign.

## The six success criteria, class by class

| Criterion | Crypto | Equities |
|---|---|---|
| 1. P1 significant after Holm | not testable | not testable |
| 2. P4 significant after Holm | not testable | not testable |
| 3. A > B > C | not testable | not testable (B < C) |
| 4. Held-out ExA > 0, ExA − ExC > 0 | not checkable (A = 0) | not checkable (A = 0) |
| 5. ExA > 0 on most breadth assets | no asset has 3 A | no asset has 3 A |
| 6. 50% kept at 25% noise | not run | not run |

**Held-out era signs.** Not checkable for grade A (0 in both classes; the check needs 10). For the
record, the held-out grade C excess was +0.726R in crypto (n 31) and +0.322R in equities (n 144):
the same sign as development-era C in equities, the opposite sign in crypto.

**Noise retention.** Not run. There is no grade A excess to retain, so the three noise seeds were
not made (they would have measured nothing the pre-registration asks about).

**Per-asset wins.** No asset in either class has the 3 grade A triggers needed to enter the breadth
list. (Equities' 2 grade A dots are one on CAT and one on XLK.)

## The secondary family (12 tests, Holm inside the family)

| Test | n | Statistic | p raw | p Holm |
|---|---|---|---|---|
| S1 crypto, ExC − ExV > 0 | 44 vs 105 | −0.368 | 0.923 | 1.000 |
| S1 equities, ExC − ExV > 0 | 1,426 vs 2,641 | +0.125 | 0.0024 | **0.029** |
| S2 crypto, location placebo | 0 | not testable | | |
| S2 equities, location placebo | 2 | not testable | | |
| S3 crypto, short ExA > 0 | 2 | not testable | | |
| S3 crypto, short ExA − ExC > 0 | 2 vs 38 | not testable | | |
| S3 equities, short ExA > 0 | 14 | not testable | | |
| S3 equities, short ExA − ExC > 0 | 14 vs 679 | not testable | | |
| S4 crypto 4h, ExA > 0 | 0 | not testable | | |
| S4 crypto 4h, ExA − ExC > 0 | 0 vs 152 | not testable | | |
| S5 crypto, ExA in good location | 0 | not testable | | |
| S5 equities, ExA in good location | 2 | not testable | | |

**What S1 says.** In equities the three vetoes separate the Buy dots: vetoed dots had an excess of
−0.010R, non-vetoed grade C dots +0.115R, and the gap survives Holm. That is a statement about the
vetoes on the bare dot, not about the grading hierarchy, and it is a secondary test. It cannot be
promoted to the headline. In crypto the vetoed dots did *better* than grade C (+0.227R against
−0.140R; 105 against 44 dots). The test was one-sided for C above V, so the reverse was not tested,
but the pre-registration is explicit: if V is better than C, the vetoes are wrong, and the write-up
must say so. **In crypto the vetoes look wrong on these counts.**

S2 (the placebo-level control for location) could not run because there is no grade A. Note for
any future run: with 200 placebo draws the smallest possible p is 1/201 = 0.00498, which cannot
pass Holm's first step in a 12-test family (0.05 ÷ 12 = 0.00417). It could only pass once two
other secondary tests had passed first. That is a property of the frozen numbers, recorded here,
not changed.

## The macro modifier family (5 tests, Holm inside the family)

Population: all non-vetoed Buy dots (A, B and C together), development era. Each dot's control is
matched on asset, era, context grade and macro state. Statistic: Ex in the predicted-better state
minus Ex in the other.

| Test | n (state 1 / 0) | Δ | p raw | p Holm |
|---|---|---|---|---|
| M1 crypto, SPY above its 200-day | 42 / 6 | −0.272 | 0.730 | 1.000 |
| M1 equities, SPY above its 200-day | 743 / 429 | +0.102 | 0.123 | 0.616 |
| M2 crypto, TLT 126-day return > 0 | 32 / 16 | +0.317 | 0.216 | 0.864 |
| M2 equities, TLT 126-day return > 0 | 513 / 432 | −0.063 | 0.774 | 1.000 |
| M3 crypto, BTC funding not crowded | 19 / 0 | not testable | | |

**Did macro change the dot's value? No.** No test is significant before or after correction, and
the signs disagree across classes (M1 helps equities and hurts crypto on the point estimate; M2 the
reverse). M3 could not be tested: in the development era, funding data runs only from 2020, the
trailing 1,095-print percentile is defined from 2021, and no non-vetoed Buy dot fell in a crowded
window. Equity macro tests lose dots before SPY's 200-day exists (346 dots, before 1993-11) and
before TLT's 126-day return exists (573 dots, before 2003). Per section 8, even a significant macro
result here would be a statement about the bare Buy dot, not the hierarchy.

## The exit comparison (10 tests, Holm inside the family)

Population: grade A and B long entries, development era, per class. Crypto has 4, so all five
crypto exit tests are not testable. Equities has 92 (2 of them grade A).

| Equities | Mean R | ATR units | Random-exit ATR units | Ratio (R) | p raw |
|---|---|---|---|---|---|
| X1 ATR×3, 2R/4R, trail | +0.183 | +0.549 | +0.346 | 1.59 | 0.242 |
| X2 X1 with S_inv stop | −0.024 | +0.155 | +0.236 | −0.25 | 0.647 |
| X3 opposite wave cross | −0.057 | −0.089 | −0.110 | 0.76 | 0.376 |
| X4 sell setup for exit | −0.002 | +0.067 | +0.014 | 0.08 | 0.413 |
| X5 20-bar time exit | −0.069 | −0.044 | −0.072 | 0.98 | 0.488 |

Holm-adjusted p is 1.000 for every one. Mean R is net, per position, from the initial risk.
X1's R is on a 3 ATR stop, so compare X1 with the others in the ATR-units column. The "ratio"
divides two small means and is unstable when the random mean is near zero or negative; the ATR
columns are the readable comparison.

**Which exit did best:** X1, the lab's current style, has the highest mean in both R and ATR units
(+0.549 ATR against +0.346 for its random-exit control). **It does not beat its control** (p 0.24),
and nothing else does either. As the pre-registration predicted, X5 (a time stop) sits at its own
random control (ratio 0.98). X3, the signal exit, has no skill in equities, which matches ledger
row 6 (signal-exit skill is crypto only); the crypto arm could not be tested. Section 8 is explicit
that no exit result could rescue the grading anyway.

## Reference rows (descriptive, not tests)

Development era, net R, excess over the context-matched random entry:

| Row | Crypto n | Crypto Ex | Equities n | Equities Ex |
|---|---|---|---|---|
| R1 all Buy dots, ungraded | 153 | +0.118 | 4,159 | +0.032 |
| R0 Buy dot, close above SMA(200) | 33 | −0.230 | 1,097 | +0.161 |

R0 is the dip filter of ledger row 8, here with a context-matched control rather than the gate
command's filter-matched one, so its number (+0.161R) is not the row-8 number (+0.145R) but is in
the same place. Grade A was meant to beat R0's point estimate; with no testable grade A, it cannot.

The shipped red dot ("Overbought Crossover"), run once as the named alternate short trigger: grade C
dev excess −0.346R (n 24) in crypto, −0.075R (n 465) in equities; grade A 2 and 14, not testable.

Gross (no cost) figures are printed beside every net figure by the command. The ordering of results
does not change with costs. One caveat on reading net against gross: cost in R is 0.003 ÷ (ATR ÷
price), so a trigger on a high-volatility bar pays less cost in R than the average bar in its
control. In equities that is why grade C's net excess (+0.115R) is a little larger than its gross
excess (+0.092R).

## Deviations and literal readings

All of these were fixed in code before the first outcome was computed (commits 9bbced00 and
aab4c5b1).

1. **Weeks.** Crypto weeks are calendar weeks starting Monday 00:00 UTC, complete at the following
   Monday 00:00, so they are usable on the Sunday bar. Equity weeks are complete on their last
   trading day, which the exchange calendar fixes in advance (a holiday Friday makes Thursday the
   last day). The final week of a series is kept only when the data shows it finished. The
   no-look-ahead test caught the first version of this: dropping the final week always made a grade
   at a Sunday differ between the full and the truncated series.
2. **The weekly body pivots are applied exactly as written, and on two assets they can never fire.**
   The rule asks for a body top strictly above the two weeks either side. When a week opens at the
   previous week's close, the next week's body top always includes this week's close, so a strict
   pivot is impossible. MEXC's KAS and TAO open at the prior close on 99% of weeks, so they never get
   a weekly trend, and every one of their dots is excluded as "context undefined" (57 daily, 323 on
   4h). Bitstamp's weeks open a fraction of a percent away from the prior close (mean gap 0.07% to
   0.35%), and that small gap is what lets Bitstamp pivots exist at all. Their rate (about one per
   five weeks) is similar to the equities', but whether a crypto week counts as a pivot partly
   depends on which way a tiny opening gap fell. Equities have real opening gaps, but on the
   longest Yahoo series the open equals the prior close on up to 43% of weeks (KO, CAT; probably the
   early decades, where Yahoo carries no true opens; not checked by date), which suppresses pivots
   there. **Reported, not patched:** patching would change a frozen rule.
3. **Matched control.** A bar enters the control when its context grade and its 20-bar outcome are
   defined, as section 6.4 says. It does not also need every trigger input to be defined.
4. **p-values the plan did not spell out.** S1 and the macro Δ use the ordering scheme (permute the
   two labels among the triggers within each asset, each trigger's excess held fixed). S2's p would
   be (1 + placebo draws at or above the real ExA) ÷ 201. The exit p is the literal share of 1,000
   random-exit books with mean R at or above the variant's.
5. **The S2 placebo** would shift every candidate level, including each week's EMA(10), by its own
   draw, scaled by the daily ATR on the bar where the level became known. (Not run: no grade A.)
6. **Exit fills.** A stop the bar opens through fills at the open, as the engine books it; a target
   the bar opens beyond fills at the open. After the 2R exit, X1 and X2 move the stop to the entry and
   then trail it with the engine's own TrailByAtr arithmetic (`ManagedExitRules.AtrTrailStop`), which
   is "the lab's current style". X3 and X4's "first bar after entry" includes the entry bar's close.
   The 60-bar cap and X5's 20 bars end at the close of bar i+60 and i+20. The random-exit control
   keeps the variant's initial stop; the targets and the trail are the exit being tested.
7. **Holm** uses the full pre-registered family size (8, 12, 5, 10) even when members are not
   testable. That is the conservative reading: a family fixed in advance does not shrink.
8. **Minimum counts.** Section 7.4's 15 and 30 gate every test whose population is a grade. The exits
   apply the same minimum to their pooled A+B population. Descriptive grade rows below 15 print the
   count only. When grade A is below 15, the noise runs are not made.
9. **"Suggestive"** is read as: criterion 3 holds, P1 and P4 each have raw p below 0.05, grade A has
   at least 15 triggers, the held-out signs do not fail where they can be checked, and the class is
   not Supported or Fragile. It did not arise.
10. **Noise injection** would use `GateCommand.Perturb` unchanged (seed s + StableSeed(symbol) mod
    1000, as the gate command does). That function sets each bar's open equal to its close, so on
    noisy bars every candle body is zero and the body pivots become close pivots. Not run.
11. **The A∪B fallback** is not added to the primary family's Holm correction. Section 7.4 says it is
    Suggestive whatever its p, so it cannot change a verdict either way.

## Runs and variants counted

- **Runs of the command:** 5. Two counts-only runs (plumbing; they print counts and exclusions and no
  outcome). Three full runs: the second only stopped the verdict block from printing two numbers for
  grades below 15 (equities' 2 grade A dots and crypto's 4 grade B dots), which section 7.4 says
  must show the count only; every other line of the first two full runs is identical. The third was
  made after merging main's engine fixes (6e145723) and its output is byte-identical to the second.
- **One diagnostic** read the weekly bars to count pivots per asset (no outcome), which found
  deviation 2.
- **Pre-registered tests:** 35. Computed: 14 (P3 crypto, P2 and P3 equities, S1 in both classes,
  four macro tests, five equity exits). Not testable: 21.
- **Fallback tests:** 2 (A∪B against C in equities); the crypto pair was not testable.
- **Pre-registered descriptive rows:** R0 and R1 per class and era, the shipped red dot alternate,
  gross figures. The noise seeds and per-asset breakdowns were not made (no grade A).
- **Variants beyond the budget: none.** No threshold, span, window, level set, era cut or asset
  subset was changed. variantsTried for the edge record is 37 (35 + 2).

## Data proof

52 input files, all read from `strategy-lab-data/` with an absolute path, read only. sha256 of every
file was taken before and after each run; 0 changed. Nothing was fetched.

## Lab problems found

- **The strict body-pivot rule depends on opening gaps** (deviation 2). Any future rule built on
  candle bodies of a 24/7 market has to say what happens when the open equals the prior close.
- **The pre-registration's minimums interact with its own grade rule.** The plan expected grade A
  might be thin; it did not check whether the grade-A conjunction could happen at all on these
  series. A counts-only dry run before freezing a plan would have shown it in five seconds. That is
  worth adding to the lab's pre-registration checklist.
- **S2 cannot pass Holm's first step with 200 draws** in a 12-test family (above).
- Not found here but relevant: the engine defects named in section 2.2 were avoided here, not
  relied on. They have since been verified and fixed on main (6e145723, a separate piece of work):
  the backtester's R is now per position from the initial stop, and `HtfLastClosedIndexExclusive`
  counts a higher-timeframe bar only once its period has ended. This harness still builds its own
  weekly bars and its own R, as the pre-registration says, so the fixes change none of its numbers.

## Scope and caveats

- Survivor snapshots throughout: every asset here still trades.
- Crypto daily is small. The development era has 153 graded Buy dots on five assets; the other five
  (ADA, SOL, DOGE, KAS, TAO) start too late or have no weekly trend at all.
- The held-out era is mostly bull-market crypto and a strong equity tape; it was used for signs
  only, and for grade A there was nothing to sign.
- P3 equities and S1 equities are real pre-registered results that passed Holm, but they are about
  the bare Buy dot with the vetoes applied, inside the same context. They were not designed as the
  question, they were not checked under noise injection, and they overlap ledger row 8's dip filter
  (which is Fragile). They are leads, not edges.

## Reproduce

```bash
cd AccessibleTrader.StrategyLab
dotnet build -v q --nologo
dotnet run --no-build -- setup-grader --snapshots /absolute/path/to/strategy-lab-data
# --counts-only stops after the counts, before any outcome is summarised.
```

Tests: `SetupGraderTests` (five): completed weeks only for crypto and for an equity holiday week, R
per position from the initial risk after the stop moves, no look-ahead under truncation, and the
matched control drawn from the same context grade. Each was seen red with its defect put back: the
app's "usable once the week opens" rule, R divided by the current stop, location reading bar t+1's
low, and the control stratum keyed without the context grade.
