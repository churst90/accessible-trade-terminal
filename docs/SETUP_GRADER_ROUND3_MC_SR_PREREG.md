# Setup grader round 3, Market Cipher with Cipher SR location: pre-registration

> **2026-10-31: the grader no longer runs.** Its rules read Cipher B parts that were removed that day
> because Market Cipher B does not draw them (the bearish divergence in exit X4). `setup-grader-mc3` now refuses and exits 2;
> to re-run it, build commit 2e86d377. The results stand.
>
> **2026-10-18: run.** Results are in `docs/SETUP_GRADER_ROUND3_MC_SR_FINDINGS.md`: No edge in crypto
> 4h, crypto daily and equities; nothing passed the cross-round Holm or the placebo gate. Nothing below
> this line was edited after the results existed.

Written 2026-10-18, before any round-3 result existed. This is stage 1 of 2. Stage 2 (a different
agent) builds and runs what is written here. **This file was committed before any forward return,
win rate, trade outcome, setup count or Cipher SR value was computed for any rule in it. The commit
time is the proof.** No backtest was run while writing it. Nothing was counted. The only data work was
listing the file names and sizes in `strategy-lab-data/`.

Round 1 is `docs/SETUP_GRADER_PREREG.md` and `docs/SETUP_GRADER_FINDINGS.md`. Round 2 is
`docs/SETUP_GRADER_ROUND2_MC_PREREG.md` and `docs/SETUP_GRADER_ROUND2_MC_FINDINGS.md`. This round
edits none of them.

## The question, in one paragraph

Round 2 tested the Market Cipher school with Market Cipher B tools only. It read the environment
(Crypto Face's "identify your environment") from the higher timeframe's money flow, and it found no
edge for the Buy dot or the trigger-wave entry, in crypto or equities. It left out location, because
the educators draw their levels by hand. But Jason Casper and Trader Geo both say they put Market
Cipher away until price is at a key level, and Market Cipher sells its own level tool, Cipher SR. So
the method as those traders describe it (environment, then a level, then the dot or the trigger) has
never been tested as one thing. This round adds one variable to round 2: **is the setup at the
current Cipher SR support?** Everything else is round 2's, unchanged. It asks three questions:

1. Do setups at Cipher SR support do better than setups away from it?
2. At support, does the dot or the trigger beat a random bar that is also at support, in the same
   environment? This isolates the Market Cipher signal, given the location.
3. Does being at Cipher SR support, by itself, beat a random bar in the same environment?

Cody approved this design. It stays inside the Market Cipher school: no Tim structure, no cycles, no
macro.

## What I had already seen when writing this

Stage 2 must read this section, because it limits how blind the test is.

- **Round 1's and round 2's results, all of them.** The ones that touch this design:
  - **The primary class was chosen after round 2's results existed.** Crypto 4h was round 2's
    secondary class S4. There, the trigger-wave entry beat its matched random bar by +0.268R on 158
    trades, raw p 0.014, Holm 0.274, held-out +0.088R on 254. That was the smallest raw p among round
    2's entry tests. The stated reason for making 4h primary here is that our Cipher SR is closest to
    the real one on intraday bars (section 1.3). But **the choice is not blind**, and the cross-round
    budget (section 7) exists partly because of it.
  - Round 2's development-era counts, which this round splits: crypto 4h 334 non-vetoed setups and
    158 E1 trades (BTC, ETH and XRP almost entirely; SOL 9 and KAS 3 eligible setups); crypto daily
    70 and 33 (five assets); equities 1,423 and 721.
  - In equities, round 2's first-bearish-divergence exit (X4) beat random exits for both entries
    (Holm 0.008 each). It appears here as one of the fixed exits, **labelled as already seen.** On
    equities it is tested on a subset of the same trades, so it is not independent evidence.
  - Round 1's location layer (Tim's weekly levels, 1.0 ATR, a 5-bar window) was nearly always true
    at the dots that reached it: 5 of 6 crypto and 36 of 40 equity dots in good context. A loose
    location rule filters almost nothing.
- **The 2026-10-17 re-run** (`reports/cipher_strategies_rerun_REPORT.md`, section 5). With the
  rebuilt, causal Cipher SR, being near SR (within 0.5 ATR of the close) against away from it, on
  Cipher B's own signals, was null at 4h (−0.024R, p 0.78), daily (−0.046R, p 0.25) and 2-day
  (+0.076R, p 0.71). At 1h, on KAS and TAO only, it was +0.164R, p 0.069 with the new Cipher B
  (+0.308R, p 0.006 with the old). That 1h result has not been replicated. The full stack (Cipher B,
  structure, at an SR level) was negative at 4h and daily.
- **`docs/ALPHA_LEDGER.md` row 20:** real levels hold about as often as matched random lines (46.2%
  against 46.7%). How price approaches a level did not predict whether it holds.
- **How our Cipher SR is built** (section 1.3), from reading `CipherSRProvider.cs` and the
  2026-10-17 CHANGES entry.

No threshold below was chosen from any of those numbers. Where a number has no source, the text says
so and the number is frozen.

---

## 1. Sources

### 1.1 What was read

The corpus is the corpus agent's worktree: `AccessibleTrader.StrategyLab/Corpus/transcripts/` and
`.../Corpus/rules/`, read only, on 2026-10-18. It is work in progress. Every quote below was checked
against a transcript. Most transcripts are YouTube auto-captions or Whisper, so wording is approximate.
Citation form: short title, video date, timestamp, and a link at that second.

Round 2's section 1 (the anchor wave, the trigger wave, money flow, the environment, stops, exits) is
not repeated. It stands as written.

### 1.2 Key levels: who says what

**Trader Geo** says the level comes first and Market Cipher second.

- "the second way to use Market Cipher is to wait for a key level" ([The Two Ways, 2024-11-01,
  01:31](https://youtu.be/Uril5gdDpO0?t=91)).
- "I have learned to not care about the divergences to not care about the money flow to not care
  about the spike waves until I'm at a key level" ([The Two Ways, 2024-11-01, 05:38](https://youtu.be/Uril5gdDpO0?t=338)).
- "the majority of your amazing moves will happen right on your key level" ([The Two Ways,
  2024-11-01, 06:09](https://youtu.be/Uril5gdDpO0?t=369)).
- "My Little Secret is I put Market Cipher away until I'm at a key level I don't even look at it ...
  I'm not trading in the middle of these two lines" ([The Two Ways, 2024-11-01, 08:13](https://youtu.be/Uril5gdDpO0?t=493)).

**Jason Casper** says the same, and gives an order: touch, retest, then the Market Cipher entry.

- "we want to also see a bounce off of a significant level ... this level, 55.7,000, is a significant
  daily support level" ([Finding PERFECT Entries, 2021-04-10, 07:28](https://youtu.be/EwtsVvxt30g?t=448)).
- "We came down, we got a bounce, we got a retest, and then we got the money flow crossover"
  ([Finding PERFECT Entries, 2021-04-10, 07:58](https://youtu.be/EwtsVvxt30g?t=478)).
- "the third thing that we're looking for is a bounce or a rejection. Off of a key level"
  ([Finding PERFECT Entries, 2021-04-10, 11:36](https://youtu.be/EwtsVvxt30g?t=696)).
- "We see that we get one touch. We come up. We get the retest. When we're getting the retest, we
  clearly see we're printing an anchor trigger, and we're seeing the money flow is starting to cross
  into the green from the red ... that would be our entry" ([How to not Lose Trades, 2021-04-28,
  14:55](https://youtu.be/sFA5iHDlPq8?t=895)).
- "print a higher green dot as we come down to a level of support" ([2 Powerful Scalping Tips,
  2021-08-19, 26:38](https://youtu.be/UdsLO5OQ6b8?t=1598)).
- "you do not look at your indicator until price comes to your level" ([Divergence Masterclass,
  2025-05-21, 20:12](https://youtu.be/11pOQcHI9SU?t=1212)); "the trades happen at key levels"
  ([38:11](https://youtu.be/11pOQcHI9SU?t=2291)).
- "we don't just take a trade because you're at a level you take a trade because you're at a level
  and you see what you had pre-planned for on the chart" ([Massive Move, 2026-09-07,
  02:42](https://youtu.be/UcKZ4u0oSWk?t=162)).

**Others.**

- Crypto Face, on stocks: "Google came down to uh $300. This support level and had its little bounce.
  ... on the hourlies, we see money flow is in the red coming up" ([Price Action Forecast,
  2026-02-24, 08:54](https://youtu.be/kuB2O0PWV0Y?t=534)).
- The official channel's narrator: "we bounced off the uh the support line" ([BIG PROFITS Core
  Strategy, 2023-08-29, 05:11](https://youtu.be/5uM9i3naZwo?t=311)).
- Gainium: "Green dot at a major support level → strong long entry" (the "Cipher B +
  Support/Resistance Levels" section).
- The official channel's interviewer, of other traders: they "use the other tools of market cipher
  like the DBSI or A or SR" ([Trader Spotlight 5, 2024-03-29, 26:01](https://youtu.be/UZbht9IYHX8?t=1561)).

**What their levels are.** Casper's and Geo's levels are drawn by eye: "support and resistance is
basically historically any place that price has found a sticking point ... draw out with our eyes
zones of support and resistance" ([Divergence Masterclass, 2025-05-21, 58:06](https://youtu.be/11pOQcHI9SU?t=3486);
the same words in [Simple Scalping Strategy, 2023-04-15, 01:38](https://youtu.be/KKw4G8r4exw?t=98)).
Casper adds Fibonacci, volume profile and the 21 EMA. **Those are not Market Cipher tools, and this
round does not use them.** The round asks a narrower question: does Market Cipher's own level tool
do the job these traders give to a level?

### 1.3 What Market Cipher says about Cipher SR

- The official tutorial, narrated inside Crypto Face's intro video: "cipher sr is the final piece of
  the puzzle a support and resistance indicator that assists you in entering and exiting your trades
  the yellow dotted lines represent entries and the purple dotted lines represent exits the more dots
  present the more likely that area is going to be retested" ([Intro to Market Cipher, 2019-06-17,
  32:55](https://youtu.be/bxkm4Kjubqs?t=1975) and [33:25](https://youtu.be/bxkm4Kjubqs?t=2005)).
- Crypto Face, right after: set its resolution "3 4 five times less than the time frame you are in so
  right now we are using the hourly so i'm going to adjust it to the 15 minute" ([Intro to Market
  Cipher, 2019-06-17, 34:27](https://youtu.be/bxkm4Kjubqs?t=2067)). Then: "you could have entered
  right here and then boom you'd be exiting right here" ([35:00](https://youtu.be/bxkm4Kjubqs?t=2100)).
- The vendor's home page: "Market Cipher SR is a day trading strategy built for the hourly timeframe
  and below which heavily incorporates horizontal supports and resistances as well as Volume Weighted
  Average Price".
- Crypto Face's pivot strategy, which uses the same touch idea: "i'm going to long when it touches the
  pivot lows" in an uptrend ([Whale Crossing, 2019-07-26, 15:18](https://youtu.be/_4YBoFdVrRA?t=918));
  short "the next time we touch the top pivot point" in a downtrend ([13:39](https://youtu.be/_4YBoFdVrRA?t=819)).
  **An attribution caveat:** he calls the lines "pivot high and pivot low" ([12:31](https://youtu.be/_4YBoFdVrRA?t=751)).
  Market Cipher SR's own plots carry those names, but in our replica they are the day's range lines,
  not the support dots. The video does not settle which indicator he means. The quote is used only
  for the word "touch".

**Nobody says how close "at the level" is.** Crypto Face says "touches". Geo says "a specific number or
a a small range" ([The Two Ways, 2024-11-01, 02:33](https://youtu.be/Uril5gdDpO0?t=153)); his own short
zone ran from 71,446 to about 72,200, about 1% ([04:04](https://youtu.be/Uril5gdDpO0?t=244)). Casper
draws "zones". No one gives a distance in points, percent or ATR.

**A wick through the level still counts if price closes back above it.** Casper's planned long: "getting
a fake out of the value area high at 75.5 and getting a wick down here ... then we get a reclaim as
soon as we get the reclaim that gives us a stop loss that gives us a valid entry" ([Massive Move,
2026-09-07, 02:11](https://youtu.be/UcKZ4u0oSWk?t=131)). Our own Cipher SR counts a "test" the same way:
the wick reached the level and the close stayed on its side.

### 1.4 Our Cipher SR, and how far it is from the real one

`AccessibleTrader.Core/Services/Indicators/CipherSRProvider.cs`, rebuilt on 2026-10-17 from Market
Cipher SR's settings dialog, legend and two digitised tutorial frames:

- **Support Zone:** the exact low of a bar that the next bar did not undercut, and that was lower than
  the 3 bars before it, taken only when that bar's volume is above its 6-bar average (itself included).
  It becomes live on the bar after the pivot (one bar on the right), so it is causal: usable from its
  confirming bar on.
- **One level per side, held until replaced.** There is no break rule. A support stays live through
  closes below it until a newer pivot low replaces it. On the bar it is replaced, the companion array
  `Support Zone_replaced` holds the old level. `Support Zone_since` holds the time the level became
  live.
- Range High, Low and Mid and the VWAP are day-anchored and empty on daily and longer charts. **They
  are not used here** (Cody's call, recorded in `docs/TODO.md`: no auto-anchor).
- **Fidelity:** our level equals the real one (within 0.06%) on 68.6% of observed bars on the fitted
  frame (Coinbase BTC, 10-minute chart) and 62.5% on the held-out frame (12-minute chart). In both,
  the real Market Cipher SR computed on 15-minute bars. **The acceptance rule of the real one is not
  identified.** No feature separated its 17 accepted levels from the 251 pivots it skipped.
- **Timeframe:** the real one always computes on a lower resolution (15 minutes by default; Crypto
  Face says 3 to 5 times below the chart). Ours uses the chart's own bars. So a 4h chart gets 4h
  pivots and a daily chart daily pivots. The vendor says it is "built for the hourly timeframe and
  below", so **every timeframe in this round is off-label to some degree.** 4h is the closest the
  lab's data allows with a development era (section 6.1).

Two limits, stated so they cannot be argued later:

- **Every result is about our replica of Cipher SR, on our replica of Cipher B.** A null cannot be
  blamed on the 62.5% fidelity afterwards, and a pass cannot be credited to the paid script.
- **This replica's levels are mostly young.** A level is replaced at every qualifying pivot low, so in
  a falling market the live support is usually the newest swing low. At a Buy dot, it will often be
  the low of the anchor wave itself, confirmed a bar or two earlier. That is how Market Cipher SR
  works (on 15-minute bars, even more so). Section 4.3 tests whether an older level, one already on
  the chart before the down move began, does better.

---

## 2. The location rule, exact and frozen

### 2.1 Definitions

> Note added 2026-10-23, after the fact: Cipher SR's level rule has since been replaced by Market Cipher
> SR's exact rule (stepped two-bar pivot, drawn three bars later; see CHANGES). This round used the old
> rule as frozen below; its results describe that rule, not the current one.

Cipher SR runs with its shipped defaults (Pivot Left Bars 3, VolumeMA 6) on the entry (TTF) bars,
through the lab's indicator engine, separately per asset and timeframe. ATR is ATR(14) on the TTF, as
in round 2.

For a TTF bar t:

- **S\*(t), the support in force during bar t,** is the `Support Zone` value at the close of bar t−1.
  It is the level the chart showed before bar t opened.
- **At support, L(t) = 1,** when all three hold:
  1. S\*(t) is defined;
  2. Low(t) ≤ S\*(t) + 0.5 × ATR(t): the bar reached the level or came within half an ATR above it;
  3. Close(t) > S\*(t): the bar closed above the level.
- **Not at support, L(t) = 0,** when S\*(t) is defined and condition 2 or 3 fails.
- **Undefined** when S\*(t) is undefined (no qualifying pivot low yet). An undefined bar is not "not at
  support". It is left out of every location population and counted.

There is no lower bound on the low. A bar that wicks below the level and closes back above it is at
support (Casper's "fake out ... reclaim"). A bar that closes below the level is not at support,
however close its low.

**Where it is read:** for E0 at the dot's bar i, so L(i). For E1 at the trigger bar j, so L(j). The
entry stays at the next bar's open, as in round 2. For a random bar t, L(t).

### 2.2 Why these choices

- **The level in force before the bar, not the one the bar creates.** A bar can confirm a new support:
  bar t−1 was a pivot low and bar t did not undercut it. The new level is known at t's close, but bar
  t's own low cannot be a test of it. Our Cipher SR makes the same call for its "tested" count: "that
  bar's wick is no test of a level it did not yet know". So S\*(t) is always the level that was live
  at t−1's close.
- **The low, not the close, for the touch.** The sources describe a touch, a bounce and a wick
  (section 1.3). The close rule is the "reclaim": price did not close through the level.
- **k = 0.5 ATR.** No source gives a number (section 1.3). Half an ATR is the band the lab's earlier
  Cipher SR proximity test already used (`confluence`, default `--sr-gate 0.5`). It was fixed there
  before this round and is reused so that this round adds no new free number and stays comparable
  with the ledger. That earlier test's result (null at 4h and daily) is known (see "What I had
  already seen"). It measured the close on both sides of the level, on all Cipher B signals, so it is
  not the same test. **k = 0.5 is frozen. No other k may be run, not even descriptively.** It is
  tighter than round 1's 1.0 ATR band over 5 bars, which flagged almost every dot (section 8, lesson 3).

### 2.3 When the support was just replaced

- **On the bar a support is replaced** (`Support Zone_replaced` is defined at bar t), S\*(t) is the
  old level. It is the one that was live when bar t opened. (So S\*(t) equals `Support Zone_replaced`
  at t, which stage 2 must assert as a check.) A bar that wicked through the old level and closed back
  above it is at support.
- **From the next bar on,** the new level is S\*. If the new level was made by the setup's own swing
  low, a trigger that later retests it is at support. That is Casper's "one touch ... retest ...
  anchor trigger".
- **The level's age is recorded, not used, in the headline.** Age = the number of bars from the bar
  the level became live to bar t. The level's pivot bar is the bar before it became live.

### 2.4 Frozen numbers added by this round

| What | Value |
|---|---|
| Support | Cipher SR `Support Zone`, shipped defaults |
| Level used at bar t | the one live at t−1's close |
| Touch band | low within 0.5 ATR(14) above the level |
| Reclaim | close above the level |
| Older level (4.3) | pivot bar before the anchor wave began |
| Placebo offset (4.4) | 3 to 6 ATR, random sign (round 1's) |
| Placebo draws | 1,000 |
| Test draws | 20,000 |
| Minimum effect | +0.05R net, development era |

Everything in round 2's section 2.7 also stands, except the draw counts, which rise to 20,000
(section 7.3).

---

## 3. What stays exactly as round 2 wrote it

The only new variable is location. **Every one of these is round 2's, by reference, unchanged:**

- the higher-timeframe bars and their completion rule (round 2, 2.1);
- waves, troughs, the anchor, the void rule and the setup (the Buy dot) (2.2);
- the context states V, A, B and C, read on completed higher-timeframe bars (2.3);
- E0 (the next open after the dot) and E1 (the trigger wave within 30 bars, with money flow turning up)
  (2.4). E1h and E1m are not used in round 3;
- the school's stop (2.5), the exclusions (2.6), the vetoes (non-vetoed setups only in every test);
- the outcome harness: 1 ATR stop, 2R target, 20 bars, stop wins ties, 15 bps a side, net primary
  (3.1);
- round 2's matched control, which round 3 calls the **context control**: the mean net R of every TTF
  bar on the same asset, in the same era, in the same context state as the signal bar (3.2). A trade's
  **context excess** is its net R minus that;
- the eras (development: dots before 2023-01-01; held-out: 2023 on), with the control's era taken from
  the signal bar (round 2 findings, deviation 1);
- the gap-keeping noise perturbation, `SetupGraderMc2Core.PerturbKeepGap`, which carries volume
  unchanged;
- round 2's deviations and literal readings 1 to 17, where they apply.

**Plumbing check, counts only, before anything else:** stage 2's harness must reproduce round 2's
counts exactly before adding location. Development-era non-vetoed setups and E1 trades: crypto 4h 334
and 158, crypto daily 70 and 33, equities 1,423 and 721. A mismatch is a bug to be found and reported,
not a reason to change a definition.

---

## 4. The tests

Everything is in the development era unless it says held-out. Every population is non-vetoed
setups. "E0 trades" are all eligible non-vetoed setups, with location read at the dot. "E1 trades" are
those that confirmed, with location read at the trigger.

### 4.1 Family L, the headline (crypto 4h, 5 tests)

| Id | Test, one-sided |
|---|---|
| L1-E0 | at-support dots beat not-at-support dots |
| L1-E1 | at-support triggers beat not-at-support ones |
| L2-E0 | at-support dots beat at-support random bars |
| L2-E1 | at-support triggers beat at-support random bars |
| L3 | at-support random bars beat all random bars |

**L1, at support against away from it.** The statistic is the mean context excess of the at-support
trades minus that of the not-at-support trades. Significance: permute the at/not-at labels among the
trades within each asset × era stratum, each trade's value fixed (round 2's GO method), 20,000 draws.

**L2, the dot or trigger, given the location.** The **location control** of a trade is the mean net R
of every TTF bar on the same asset, in the same era, in the same context state as the signal bar, **and
with L = 1**. Each control bar's R comes from the same harness, entering at its next open. A trade's
**location excess** is its net R minus its location control. The statistic is the mean location excess
of the at-support trades. Significance: round 2's block permutation, inside each asset × era × context
× (L = 1) stratum: cut its rows, in time order, into 20-row blocks, permute the block order with the
trade labels travelling with their blocks, recompute. 20,000 draws.

**L3, the location alone.** The rows are every TTF bar with context A, B or C (the environments the
method trades in). For each asset × era × context stratum, a bar's value is its net R minus the
stratum's mean net R over **all** its bars. The statistic is the mean value over the bars with L = 1.
Significance: the same block permutation, with L = 1 as the label. Bars in context V are reported, not
tested.

**How the three fit together.** For an at-support trade, its context excess is roughly its L2 location
excess plus the L3 lift of its stratum. So:

- L2 passes and L3 does not: the dot or trigger adds something at support, and the support itself adds
  nothing.
- L3 passes and L2 does not: the location carries it, and the Market Cipher signal adds nothing on top.
- L1 passes and L2 does not: setups at support beat setups away from it, but no better than any bar at
  support would. **The location, not the dot.**

### 4.2 Family LS, the same five tests in the secondary classes (10 tests)

L1-E0, L1-E1, L2-E0, L2-E1 and L3 for crypto daily (weekly environment) and for equities daily
(weekly environment). Definitions as in 4.1.

**Stated before the counts:** crypto daily has 70 non-vetoed development setups on five assets and
33 E1 trades. Split by location, its groups will probably be below 30, so its tests will probably be
Suggestive only or not testable. That is why it is secondary.

### 4.3 Family A, an older level (crypto 4h, 2 tests)

Casper and Geo plan their levels before price arrives: "do your analysis in the morning, set your
alerts" ([Divergence Masterclass, 2025-05-21, 38:47](https://youtu.be/11pOQcHI9SU?t=2327)). The official
tutorial says "the more dots present the more likely that area is going to be retested". Our Cipher
SR's support at a dot is often the anchor's own low (section 1.4). So:

- An at-support trade's level is **older** when its pivot bar is before b(i), the bear cross where the
  setup's anchor wave began (round 2, 2.2). It was already on the chart before the down move.
- It is **newer** when its pivot bar is at or after b(i). It was made by the setup's own move.

| Id | Test, one-sided |
|---|---|
| A-E0 | older-level at-support dots beat newer-level ones |
| A-E1 | older-level at-support triggers beat newer-level ones |

The statistic is the mean location excess (L2's) of the older group minus the newer group. Label
permutation within asset × era strata, 20,000 draws. The same split is reported, not tested, in the
secondary classes.

### 4.4 The placebo gate for location (not a counted test)

Ledger row 20 says real levels hold about as often as random lines. So any location claim (L1 or L3)
must also beat placebo levels.

- **One draw:** shift every support level, for its whole life, by u × ATR(c), where c is the bar the
  level became live, u is uniform on 3 to 6, and the sign is random (round 1's placebo offsets,
  reused). Rebuild L(t) with section 2.1's rule against the shifted levels. Recompute the L1 and L3
  statistics. The context excess, the outcomes and the setups do not change.
- **1,000 draws,** seeded with `StableSeed.From("setup-grader-mc3-placebo", draw, asset)`. A draw
  whose placebo labels give no event in a group is dropped and counted. The gate needs at least 500
  usable draws.
- **The gate passes** when the real statistic is above the 95th percentile of the usable placebo
  draws. It is a condition in the verdict (section 7.4), not a p-value in any Holm.

### 4.5 Family X, exits on the at-support trades (12 tests)

**Population:** the at-support trades, E0 and E1, per class, development era. Each uses round 2's
school stop (round 2, 2.5). R is per position from the initial risk. Every variant is also reported in
ATR units.

| Id | Exit |
|---|---|
| XR | the next Cipher SR resistance above the entry |
| X4 | the first bearish divergence (already seen) |
| X5 | 20-bar time exit (reported, not tested) |

**XR, Cipher SR's own exit** ("the purple dotted lines represent exits"). R\*(t) is the `Resistance
Zone` value at the close of bar t−1. From the entry bar on, the exit fires on the first bar t where
R\*(t) is defined, R\*(t) is above the entry price, and High(t) ≥ R\*(t). It fills at R\*(t), or at
Open(t) if the bar opens at or above it. The stop is checked first on every bar, and a stop that the
bar opens through fills at the open. Cap: the close of bar s + 60.

**X4** is round 2's X4 unchanged: exit at the close of the first bar with a `Bearish Divergence`
marker, school stop, 60-bar cap. **In equities, X4 is a re-test of round 2's Holm-significant result on
a subset of the same trades. It must be written up as already seen, and it is not fresh evidence.**

**The control:** round 2's. Keep each entry and its variant's initial stop, draw a random exit bar from
that variant's own holding-time distribution (pooled per class and policy), 20,000 random books. The
p-value is (1 + books at or above the variant's mean) ÷ 20,001.

Tests: XR and X4, for E0 and E1, in 3 classes: 12.

### 4.6 Reported beside the tests (descriptive, not tests)

- At-support shares, by class, era, grade and policy. Why the rest are not at support: no level yet,
  the low too far above the level, or a close below it.
- The level's age in bars (median and quartiles) at at-support signal bars; the older and newer split
  in every class.
- The signal bars that fall on a replacement bar.
- **Counts only, no outcome:** how many at-support signal bars are also a "test" in our Cipher SR's own
  sense (low at or below the level, close above it, no band).
- The at-support trades' context excess (round 2's), development and held-out. It is used in the
  method verdict (section 7.4).
- L3 for bars in context V.
- Gross figures, per-asset tables, ATR units, X5, the noise seeds, the placebo distributions.
- **The cross-round re-reading** (section 7.2): which of round 1's and round 2's Holm-significant
  results would still pass under the cross-round Holm. It re-reads published numbers. It is not a test.

---

## 5. Exclusions and minimum counts

- **Round 2's exclusions stand.** Also excluded, and counted: setups and random bars whose S\*(t) is
  undefined at the bar where location is read.
- **Confirmatory:** at least 30 development-era events: trades for L2 and the exits; each group for
  L1 and Family A; at-support bars for L3. With 15 to 29, the test runs and can be Suggestive only.
  Below 15 it is "not testable": count only. Exits need 15 entries, as in round 2.
- **Held-out check:** at least 30 held-out events (each group, for group tests), raised from round 2's
  10. Below 30 it is "not checkable", which caps the verdict at Suggestive.

---

## 6. Classes, timeframes and data

### 6.1 What exists (file names and sizes only, read 2026-10-18)

| Class | Files |
|---|---|
| Crypto 4h | Bitstamp BTC, ETH, XRP, SOL, DOGE; MEXC KAS, TAO |
| Crypto daily | Bitstamp BTC, ETH, LTC, XRP, BCH, ADA, SOL, DOGE; MEXC KAS, TAO |
| Equities daily | round 2's 33 Yahoo series |
| Crypto 1h | MEXC KAS and TAO only |

### 6.2 The classes

- **Crypto 4h entries, daily environment: PRIMARY.** Round 2's S4 class, seven assets: BTC, ETH, XRP,
  SOL and DOGE (Bitstamp 4h; BTC and XRP files hold the 20,000-bar cap, BTC from 2017-04-30) and KAS
  and TAO (MEXC 4h). Daily bars built from the 4h bars. The development era is in practice BTC, ETH
  and XRP, so breadth means 2 of 3. This is the closest the data allows to where Market Cipher SR is
  built to work.
- **Crypto daily entries, weekly environment: secondary.** Round 2's ten assets.
- **Equities daily entries, weekly environment: secondary.** Round 2's 33 series.
- **Crypto 1h: not added.** Only KAS and TAO have 1h files. They are the discovery set of the
  unreplicated 1h SR result, so testing them again would not be a replication. Their MEXC histories
  start in late 2022 (KAS) and later (TAO), so a 1h class would have almost no development era.
  Fetching more data is outside stage 1 and needs its own dated note.
- **Left out, as in round 2:** GLD, SLV, USO, TLT, IEF; the Twelve Data duplicates; the 2-day, 12-hour
  and weekly files; the Alpaca 5-minute files; every macro, cross-series and event file.
- **Crypto and equities are never pooled. Longs only.** Support only; resistance appears only in XR.

---

## 7. Discipline across three rounds, and the verdicts

### 7.1 This is the third look at the same data

Rounds 1, 2 and 3 all ask whether a Market Cipher B Buy dot setup beats a matched random entry, on the
same assets, the same eras and largely the same dots. Each round ran Holm inside its own families. Over
three rounds, the chance that some test passes by luck alone grows with every round. So **round 3's
verdicts use one family-wise budget that counts every pre-registered test in all three rounds.**

| Round | Tests counted |
|---|---|
| Round 1 | 39 |
| Round 2 | 51 |
| Round 3 | 29 |
| **Total, M** | **119** |

- **Round 1:** 35 pre-registered (8 primary, 12 secondary, 5 macro, 10 exits) plus 4 fallback tests
  (2 run in equities, 2 not testable in crypto). Round 1's own budget counted the fallback as 2. This
  count uses 4, the larger. 14 of the 35 were computed.
- **Round 2:** 49 pre-registered (6 entry, 8 grade, 19 secondary, 16 exit) plus the 2 fallback tests
  that were triggered (crypto E0 computed, crypto E1 not testable). Equities' fallback was never
  triggered. 43 of the 49 were computed.
- **Round 3:** Family L 5, Family LS 10, Family A 2, Family X 12.

### 7.2 The cross-round Holm

- Put all M = 119 tests in one list. Rounds 1 and 2 enter with the raw p-values printed in their
  findings. A test that was not testable, or whose raw p cannot be found in its findings, enters as
  p = 1.
- Run Holm–Bonferroni at α = 0.05 over the whole list. **A round-3 test is "significant" only if it
  passes this cross-round Holm.** For orientation, the plain Bonferroni line is 0.05 ÷ 119 = 0.00042.
- Within-family Holm for each round-3 family is printed beside it, for comparison with rounds 1 and 2.
  **It decides nothing.**
- **Every test can pass the first step.** With 20,000 draws the smallest p is 1 ÷ 20,001, about
  0.00005, below 0.00042. That is why the draws rise from round 2's 10,000 and 2,000.
- Seeds: `StableSeed.From("setup-grader-mc3", test id, asset or class)`. Never `string.GetHashCode()`.

### 7.3 Noise and breadth

- **Noise:** round 2's gap-keeping perturbation at 25% of each series' own volatility, seeds 0, 1 and
  2 (offset by the symbol's stable seed), development era. Rebuild everything, **Cipher SR included,
  on the noisy bars**: higher-timeframe bars, Cipher B on both timeframes, waves, setups, contexts,
  S\*(t), L(t), controls. Retained = the mean noisy statistic ÷ the clean one. A claim needs at least
  50%. Noise runs are made for every Family L and LS test that is testable, and for Family A.
- **Breadth:** more than half of the assets with at least 10 development-era events (at-support
  trades for L2; trades in the smaller group for L1 and A; at-support bars for L3) have a positive
  statistic of their own. The per-asset table is printed whatever the result.

### 7.4 Verdicts

**A round-3 test is Supported** only if all of these hold:

1. It passes the cross-round Holm (7.2).
2. Its development-era statistic is at least +0.05R net.
3. **The held-out era agrees:** at least 30 held-out events, the same sign, at least half the
   development statistic's size, and a one-sided p of 0.10 or less when the same test is run on the
   held-out era alone.
4. Breadth holds (7.3).
5. At least 50% survives noise (7.3).
6. **For L1 and L3 only:** the placebo gate passes (4.4).

**The other verdicts:**

- **Fragile:** criteria 1 to 4 (and 6 where it applies) hold, and noise fails.
- **Suggestive:** a raw p below 0.05, and not Supported or Fragile, because the cross-round Holm
  failed, or the count is 15 to 29, or the held-out check is not checkable (fewer than 30). A
  Suggestive result is a lead, not an edge.
- **No edge:** anything else. A test that passes the cross-round Holm but fails criterion 2, 3, 4 or 6
  is **No edge**, not Suggestive.

**The method verdict, per class.** "Environment, then Cipher SR support, then the dot (or the
trigger) has an edge" is Supported for E0 (or E1) only if L2 for that entry is Supported **and** the
at-support trades' context excess (round 2's control) is positive in both eras. The second part asks
that the method also beat a random bar in the same environment, not just a random bar at support.

**Classes.** Each class gets its own verdicts. The headline sentence is crypto 4h's. If crypto 4h is
No edge and a secondary class is Supported, the write-up must say: "Where our Cipher SR is closest to
the real one (crypto 4h), the location layer has no edge. [Class] shows [result]; it is a secondary
class, on a less faithful Cipher SR." A secondary class cannot become the headline.

### 7.5 What counts as "no edge", stated plainly

For a class:

- If L2-E0 is not Supported or Fragile: **at Cipher SR support, a Buy dot bought at the next open is no
  better than a random bar that is also at Cipher SR support, in the same environment.**
- If L2-E1 is not Supported or Fragile: **at Cipher SR support, the trigger-wave entry is no better
  than a random bar that is also at support, in the same environment.**
- If L1 is not Supported or Fragile, for an entry: **setups at Cipher SR support do no better than
  setups away from it.**
- If L3 is not Supported or Fragile: **being at Cipher SR support, by itself, is no better than a
  random bar in the same environment.**
- If L1 passes and L2 does not: **the location helps, the dot does not. Any bar at support would have
  done as well.** The write-up must use those words.
- **If neither L2-E0 nor L2-E1 passes, the Market Cipher method as written here (environment, then
  Cipher SR support, then the dot or the trigger) has no edge in that class, whatever L1, L3, Family
  A or the exits say.**
- **If that holds for crypto 4h, the findings must also say: "Across three pre-registered rounds, no
  Market Cipher B entry rule tested in this lab has beaten a matched random entry."**

**The null cannot be reframed afterwards** by any of these:

- changing k, the touch rule, the close rule, or which level is read (S at t instead of t−1, the
  older level only, a window of several bars, the nearest of several levels);
- swapping in other levels: Range High, Low or Mid, the VWAP, resistance flipped to support,
  hand-drawn or Fibonacci levels, volume profile, moving averages;
- promoting Family A, the "tested" subset, a grade, an exit, a secondary class or any subgroup found
  after the fact to the headline;
- using the within-family Holm, or any round's own Holm, in place of the cross-round Holm;
- switching from net to gross, from excess to raw R, or from the location control to the context
  control for L2;
- dropping assets or eras, moving from the development era to the full sample, or using the held-out
  era for anything but the check in 7.4;
- pooling crypto with equities, or using one class to rescue another;
- **blaming the replica's 62.5% fidelity, or the 4h bars instead of 15-minute ones, or crediting the
  paid script;**
- **reading the X4 equities result as new evidence.** It was seen in round 2.

Also stated now:

- **A good exit does not rescue the entries.** Exits are compared on the same trades.
- **If at-support setups do worse than setups away from support** (L1 the wrong way), the write-up
  says so. It does not turn that into a "buy away from levels" rule; that would need its own
  pre-registration.
- **If the placebo gate fails while L3 passes,** the write-up says the lift belongs to "near a line
  the price came down to", not to Cipher SR's levels, as ledger row 20 found.

### 7.6 The counts-only stage comes first, and changes nothing

Stage 2 must commit the harness and its tests, then run it with `--counts-only`. That run computes no
outcome and prints:

- the round-2 plumbing check (section 3);
- setups and E1 trades per class and era, at support and not, by grade;
- why the rest are not at support (no level, too far above, closed below);
- the older and newer split; the replacement-bar count; the "tested" subset count;
- at-support bars per class, era and context, for the L2 and L3 strata;
- the held-out counts for every test;
- the placebo label counts of the first draw (labels only, no outcome).

That output goes into the findings before any outcome is computed. **The counts may decide only what
section 5 already says:** confirmatory, Suggestive only, not testable, or not checkable. No threshold,
band, window or definition may change because of them. If the at-support share looks extreme (below
10% or above 90% of setups), the findings say so, and nothing changes.

### 7.7 Counting what is run

**Pre-registered descriptive runs** count toward `variantsTried` but are not tests: everything in
section 4.6, the placebo draws (one run), the three noise seeds per class, X5, gross and ATR-unit
figures, the counts-only run.

**Every run beyond this list** is counted in `variantsTried` and listed with its result: a different
band, level, window or definition, another era cut or asset subset, a bug-fix rerun, an exploratory
look. A bug fix that changes a pre-registered number must report both numbers. Anything new is
exploratory and must be labelled so in the same sentence as its number.

---

## 8. What rounds 1 and 2 taught, written before any round-3 result

1. **Mixing schools starves the best grade** (round 1). Tim's weekly trend almost never agrees with a
   deep-oversold dot. **Here:** Market Cipher tools only. The level comes from Cipher SR, Market
   Cipher's own level tool, not from any other school.
2. **Neither the dot nor the trigger beat a random bar in the same environment** (round 2), in crypto
   or equities. **Here:** round 2's definitions are kept exactly, so the only thing that can change
   the answer is the location. A null here cannot be blamed on a different trigger rule.
3. **A loose location rule filters nothing.** Round 1's location (1 ATR, 5 bars) was true for nearly
   every dot that reached it. **Here:** half an ATR, on the signal bar only, with a reclaim, and the
   counts-only stage prints the at-support share before anything else.
4. **Location has to beat random lines** (ledger row 20). Round 1 planned a placebo with 200 draws,
   which could never pass Holm, and never ran it. **Here:** the placebo is a gate with 1,000 draws,
   not a Holm test, so its draw count cannot block it.
5. **The largest point estimates fell apart under noise.** Round 2's trigger-wave entries kept 32%
   (crypto) and 27% (equities). Small samples on a few correlated assets produce large numbers.
   **Here:** noise retention is still required, and the at-support split will make samples smaller,
   which is stated now.
6. **Held-out signs flipped often** in round 2: waiting in crypto, the veto in both classes, most
   per-setup comparisons. A sign check with 10 events is weak. **Here:** the held-out era must agree in
   sign, keep half the size, reach p 0.10 by itself, and have at least 30 events.
7. **Each round chose its design knowing the last one's results.** Round 2 knew round 1's equities
   result; round 3 knows round 2's crypto 4h result. **Here:** one cross-round Holm over all 119
   tests, and the primary class's non-blindness stated at the top.
8. **Crypto daily is thin.** The weekly money flow needs about 86 weeks of warm-up, so only five
   crypto assets have development-era daily setups. **Here:** crypto daily is secondary, and its
   likely lack of power is written down before the counts.
9. **A look-ahead guard can pass a sabotage that changes nothing on its data** (round 2's thickness
   window). **Here:** the guards in section 9 must use a fixture where the sabotage changes the
   answer, for example a support replaced on a signal bar.
10. **A controlled exit can show skill while the entries show none** (round 2's X4 in equities). It
   says something about exits, not about the setup. **Here:** exits are their own family, X4 is
   labelled as already seen, and section 7.5 says an exit cannot rescue an entry.

---

## 9. Files for stage 2

- **Read first:** this file, then round 2's pre-registration and findings, then round 1's.
- **New command,** suggested name `setup-grader-mc3`, with `--counts-only`. It reuses round 2's code
  (`SetupGraderMc2Core.cs`, `SetupGraderMc2Command.cs`) and round 1's (`SetupGraderCore.cs`).
  **`setup-grader-mc2` must produce byte-identical output after the change.** Shared helpers may gain
  overloads, as round 2 did, but must not change existing arithmetic. `GateCommand.Perturb` must not be
  edited.
- Cipher SR comes from the lab host's indicator engine (`CIPHER_SR`), shipped defaults. Do not
  reimplement it.
- **Tests, each seen red with its defect put back,** in a scratch copy restored byte-identical:
  1. L(t) reads the support live at t−1's close: on a fixture where a support is replaced on the
     signal bar, the old level is the one tested (sabotage: read the level at t).
  2. Truncating the series after any bar changes no S\*, L, age or older/newer label at or before it.
  3. The L2 control stratum includes both the context state and L, read at the signal bar (sabotage:
     drop L from the key; read L at the dot for an E1 trade).
  4. XR never fills at a resistance that was not live when its bar opened.
  5. The noise rebuild recomputes Cipher SR on the noisy bars (sabotage: reuse the clean levels).
  6. A placebo offset is fixed for each level's whole life and drawn at the bar it became live.
  7. The older/newer label compares the level's pivot bar with b(i) (sabotage: compare with i).
- **Write the results** in a new `docs/SETUP_GRADER_ROUND3_MC_SR_FINDINGS.md`, with an `edges.json`
  record (`EdgeRegistryTests` requires one). Record the sha256 of every input file before and after
  each run. **Do not edit this pre-registration after results exist,** except to add a dated pointer to
  the findings at the top.
