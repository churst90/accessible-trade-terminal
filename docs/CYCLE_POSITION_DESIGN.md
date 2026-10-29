# Cycle Position: where the chart is in its daily, weekly and yearly cycles

Written 2026-10-28. Cody: "use all of the visuals from cf that are in cipher c, but create a new
indicator implementing your ideas, test it and report the results using structured rules ... i want
something that helps me know where I am within a cycle so i can follow camel's technique ...
without a bunch of patch work but that is well designed, something that works for any asset if
possible, uses all the same symbols and markers as cipher c does."

The code is `CyclePositionProvider` (Core/Services/Indicators). It reuses `CipherCEngine` for the
daily rule and Cipher C's `Draw` for everything drawn. The tests are `CyclePositionProviderTests` and `CycleLowConfidenceTests`.
Cipher C is unchanged and stays the faithful copy of the original.

## 1. What was agreed

Seven points, proposed and agreed before building:

1. Windows learned from the chart's own past cycles, as percentiles. No per-asset window tables.
2. One nesting rule for every asset. Lows are confirmed on price.
3. Misses shown, nothing repainted, and a running hit rate said.
4. Yearly lows found from the price, said with less confidence.
5. No sine waves and no dominant-cycle search.
6. Cipher C stays the faithful copy; this is a separate indicator.
7. Tested on held-out data.

## 2. The rules

**Daily lows.** These are the original's default daily rule, the same on every asset:
- A double-smoothed stochastic (10, 9) turns up through its 5-bar average between 10 and 75.
- The lowest swing low since the last top is the candidate.
- It is held for half the shortest daily cycle. An undercut within a third of it drops the low.
- There is no per-asset smoothing, no metals pivot break, no drop-until-due and no faster
  confirmation.

The one number per asset is the **shortest daily cycle**:
- the symbol's own where Cipher C has one (Bitcoin 48 calendar days, the Dow 36 trading days);
- else 32;
- or a number set by hand.

It cannot be read off the price. Over the 46 lab dailies, price swing spacing was 50 to 65 bars on
every asset whatever the original's cycle, with a rank correlation of about 0. The table and bar
detail say which source was used.

**Weekly lows** are picked from the daily lows by time (`CipherCEngine.WeeklyByTime`; Cody chose
this, option B of section 7, on 2026-10-28; before, it was the original's count rule). The weekly
low is the lowest daily low at least a gap after the newest weekly low. It becomes a weekly low when
the next daily low is not under it. A daily low under the newest weekly low before the gap takes its
place. The gap is half the shortest weekly cycle: the symbol's own (Bitcoin 22 weeks), else 3
shortest daily cycles. The weekly count runs from the newest weekly low.

**Yearly lows** come from the price (`CipherCEngine.YearlyFromPrice`). A yearly low is the lowest
low for 0.15 of a cycle on both sides, at least 0.8 of a cycle after the last. The cycle length is
the symbol's own, else 4 years.

**Learned windows.**
- Each finished cycle's length (both lows counted) becomes known on the bar its second low is
  confirmed.
- A tier's window on a bar is the 20th to 80th percentile of the lengths known by then, linear
  between ranks and rounded.
- A window needs 6 past daily cycles, or 4 weekly or yearly ones. Until then it is a default:
  - daily: the shortest cycle to 1.25 times it;
  - weekly: 3 to 5 shortest daily cycles;
  - yearly: the symbol's range, at least ±25% of its middle.
- The yearly window is learned in tenths of a year and said in years.
- Off a daily chart every tier counts in the chart's bars, as Cipher C's does.

**"The low had come by now".** On each bar, the indicator looks at the past cycles that were still
unconfirmed on that day of their cycle: their next low not yet confirmed that many days in. Of
those, it says the share whose low had already happened by that day. It is said only when at least
3 past cycles qualify. A plain share of past lengths overstates it. On a late day, the cycles still
being counted lean long, because those whose low was confirmed are no longer counted. The plain
share said 90% when 72% had come.

**Phase.** The cycle's day against its window: EARLY before it, DUE in it, LATE after it. The
phase appears:
- under the held daily low, in place of the original's LOW/MID/HIGH;
- in the table;
- in the speech.

**Hit rate.** Each cycle is scored against the window it was given when it began (on the bar its
first low was confirmed), and only if that window was learned. The running count is said: "the
daily window has held 14 of 24 cycles, 58%".

**Nesting.**
- Daily cycle k of the weekly one, counted from the newest weekly low: 1 to 11 on the captures,
  mostly 2 to 4. A weekly low is known when the next daily low holds above it, so a new weekly
  cycle starts its count at 2, or at 1 when a lower daily low took the newest's place. While a
  candidate is drawn after the newest weekly low, it says so: "daily cycle 3 of it, a new weekly
  low drawn but not yet confirmed" (table: "daily 3, new drawn").
- Weekly cycle k of the yearly one.

**Drawn.** Cipher C's picture, all of it:
- D, W and Y lows;
- checks where each was confirmed, and crosses under dropped lows;
- the held low's clock;
- lettered translated tops with their boxes;
- bands over the live windows, now the learned ones;
- the wave from low to low;
- the moving average and the display switches.

Two of the original's switches start on here: cancelled daily lows and the confirmation mark, so
the misses are visible.

**Said.** For example: "Day 24 of the daily cycle, the low had come by now in 70% of 12 past cycles
this far in unconfirmed, due: in its window, days 22 to 30, daily cycle 2 of it." The table
(bottom right) has these rows:
- Count
- Phase
- Low come by now
- Window
- Window held
- Lower cycle
- Unconfirmed low

## 3. How it was tested

Three data sets. The rules were frozen before the third was opened.

- **Lab dailies (46 charts):** the Yahoo and Bitstamp daily files in `strategy-lab-data`, symbols
  from their own files. Scored walk-forward on the indicator's own lows: each cycle against the
  window it began with. All of these had been looked at while building.
- **The original's labels (39 captures, `e2elist.txt`):** D, W and Y lows the original drew on
  TradingView, used throughout Cipher C's work. Scored as agreement, an exact bar F1.
- **Blind held-out captures (cap17, 28 captures):**
  - 12 untuned daily charts: IWM, Google, Meta, Netflix, Walmart, Doge, Link, AUDUSD, GBPUSD, DAX,
    Hang Seng, Palladium.
  - 6 tuned daily charts on Heikin-Ashi: Coinbase, Silver miners, Ether, the dollar, Silver, Oil.
  - 10 tuned weekly or 4-hour charts.
  - Never looked at before. Scored once, for both indicators, after the tests and sabotages were
    done.

## 4. Results

### 4.1 Do its windows say how often they hold? (walk-forward, its own lows)

| | Lab 46 | Blind 18 dailies |
|---|---|---|
| Daily cycles scored | 8,638 | 3,812 |
| Daily window held | **60.3%** | **60.8%** |
| Per chart (min / median / max) | 51 / 61 / 68% | 50 / 61 / 68% |
| Weekly window held | 60.2% (3,749) | 62.5% (1,642) |
| Yearly window held | 55.0% (111) | 47.2% (108) |
| Cipher C's fixed daily windows, on its own lows | 27.6% | 31.0% |

The weekly rows were measured under the old count rule; under the time rule (section 7.5) the
weekly window holds 61.2% on the building captures and 60.7% blind.

A 20th to 80th percentile window should hold about 60%. It does, on every chart, including the
blind ones.

**But it is not sharper than Cipher C's.** The learned daily window is about twice as wide: a
mean of 21.6 days against 9.1, or 0.52 of the cycle's middle length against 0.23. Shrink the
learned window to Cipher C's width (35th to 65th percentile, 0.27 wide) and it holds 31.9% on the
lab set and 33.0% blind, against Cipher C's 27.6% at 0.23. Per unit of width the two are the
same. A narrow window that holds 3 cycles in 10 without saying so has become a wide one that holds
6 in 10 and says so. Cycle lengths are spread out (a window of relative width w catches about
1.15·w of cycles), and no window choice gets around that.

### 4.2 "The low had come by now": is the share it says the share that happens?

Each row covers the bars where the share said fell in that band. "Came" is how often the next low
had in fact already happened by that bar.

| Said | Came (lab 46) | Came (blind 18) |
|---|---|---|
| 0–9% | 3% | 4% |
| 10–19% | 15% | 17% |
| 20–29% | 26% | 28% |
| 30–39% | 36% | 39% |
| 40–49% | 46% | 49% |
| 50–59% | 58% | 58% |
| 60–69% | 66% | 65% |
| 70–79% | 69% | 67% |
| 80–89% | 68% | 66% |
| 90–99% | 70% | 63% |

Brier score 0.175 against 0.241 for one fixed rate on the lab set (skill 0.27), and 0.182 against
0.238 blind (skill 0.24). It is honest up to about 65%. Above that it overstates: "90%" means about
2 in 3. Fewer than 4% of bars are in those bands.

### 4.3 What the phase means (share of bars whose next low had already happened)

| Phase | Lab 46 | Blind 18 |
|---|---|---|
| EARLY | 6.5% | 7.3% |
| DUE | 50.4% | 49.8% |
| LATE | 70.4% | 65.4% |

So LATE does not mean "the low is surely in". About a third of the time the low is still ahead.

### 4.4 Misses

- Dropped (undercut before confirmation): 0.64 per daily low on the lab set, 0.59 blind. Cipher C:
  0.66 and 0.64.
- Failed: 0.39 per daily low, Cipher C 0.39.
- A low is known a median 16 bars after it on both indicators (the hold).

### 4.5 Agreement with the original's lows (exact bar F1)

| Captures | Tier | Cycle Position | Cipher C |
|---|---|---|---|
| Blind, 12 **untuned** dailies | daily | **.968** | .969 |
| | weekly | **.930** | .934 |
| Blind, 6 tuned dailies (Heikin-Ashi) | daily | .900 | .973 |
| | weekly | .693 | .914 |
| | yearly | 0 | 1.0 |
| Blind, tuned weekly charts | daily / weekly | .833 / .483 | .941 / 1.0 |
| Blind, tuned 4-hour charts | daily / weekly | .789 / .565 | .902 / .701 |
| Building set, daily charts | D / W | .906 / .770 | .971 / .935 |
| Building set, weekly charts | D / W | .860 / .682 | .972 / .835 |
| Building set, 4-hour charts | D / W | .820 / .613 | .951 / .799 |

The weekly column is the old count rule's. Under the time rule it agrees less (section 7.5:
untuned blind .716, building dailies .626) and picks the real weekly low more often.

On assets the original does not tune, Cycle Position's daily lows are the original's: one rule
already is the original's rule there. The gap is all on tuned assets:
- per-asset smoothing;
- the metals' pivot break;
- drop-until-due;
- the dates.

Leaving those out was the point: those per-asset rules are the patchwork. Cipher C's yearly 1.0 is
the original's own date table. Yearly lows from the price land within 30 days of the original's
on 28 of 57. When they miss, they pick a different low entirely (the Dow and the Nasdaq most).

## 5. Caveats

- **The windows are honest, not sharper.** Section 4.1.
- **Above about 65% the "low had come" share overstates.** Section 4.2. It could be recalibrated
  on these charts (a fitted map from said to came), but that would be one more fitted table; not done.
- **The weekly lows no longer follow the original's.** The time rule picks the real weekly low
  more often but agrees with the original's weekly lows less, most on untuned assets, where the old
  count rule matched them (section 7.5). The gap's half is chosen from a simulation, not fitted.
- **Too little yearly history to learn a yearly window** on most charts: Bitcoin's three yearly
  cycles since 2011 never reach the four needed.
- **The shortest daily cycle is still per asset.** It is the one number per asset and cannot be read
  from the price. Untuned symbols get 32.
- **The lab set and the building captures were looked at while building.** The blind numbers are
  one scoring of 28 captures.
- **Low is in and Low expected** have their own caveats (section 8.3).
- **Not seen or heard in the app.** The accessibility-lead review agent the project's hook asks for
  is not registered in this environment; not run.

## 6. Tests

`CyclePositionProviderTests`, 12 tests:
- **Nothing on a bar changes when later bars arrive.** Bitcoin daily is cut at six points. This
  covers every causal component and companion except the arc and the live band.
- **A window is learned from enough past cycles,** and is the default before.
- **The learning worked by hand.** Four lows, two cycles to a window, the hit scored against the
  window the cycle began with, the conditional share, and numpy-style percentiles.
- **The hit rate only grows** and never exceeds the cycles scored.
- **The phase is the day against the window.**
- **"Low had come" needs three past cycles,** and counts only those still unconfirmed.
- **The weekly nest counts on from 1,** one step at a time, restarting at 1 or 2; at least 4
  on Bitcoin; Bitcoin's weekly lows at least half of 22 weeks apart.
- **The weekly rule worked by hand** (`WeeklyByTime`): replace inside the gap, pass over, the
  candidate, undercut and cancelled, confirmed, no candidate inside the gap; a seed passed over and
  replaced, nothing cancelled.
- **The bar detail's words.**
- **The count's three sources** (default, symbol's own, set by hand).
- **The same components as Cipher C,** with the misses on.
- **Off a daily chart everything is in bars.**

Twelve sabotages, all red:
- learning from cycles not yet known;
- learned from 1 cycle;
- phase edge `<` for `<=`;
- every cycle a hit;
- the plain share;
- nest from the newest weekly low;
- length source order;
- weekly units off-daily;
- cancelled lows off by default;
- scoring against the window at the cycle's end;
- confirmation day without the confirmation;
- percentile without interpolation.

Seven more for the weekly rule by time (2026-10-28), all red: no replace inside the gap; the next
candidate ignoring the gap; an undercut candidate not cancelled; a zero gap; the symbol's own
weekly cycle ignored; the nest counted from the candidate (the old way, never past 2); the old count
rule. ("nest from the newest weekly low" above was a sabotage of the old rule, where the nest ran
from the candidate; it is now the rule.)

`CycleLowConfidenceTests`, 7 tests (25 cases), for section 8: the error function; both chances
the study's to 1e-9 on Bitcoin, on exactly its bars; Low is in only while a low is held (and a
dropped low checked); the two sets of bands; the speech says what the table shows; neither read off
a daily chart. The causality test above now names both chances. Thirteen sabotages, all red: the
error function's constant, a band edge each, a calibration point (twice; a point under 1% was green,
no Bitcoin day reaches it), a tree threshold, Low expected's intercept, either sentence dropped from
the speech, read off a daily chart, the table's dash, and past cycles read 30 days early (the
causality test).

`IndicatorCausalityTests` exempts the Cycle Position confirmations and weekly window from the
prepend check. A learned window depends on how much history precedes it, so prepending bars
changes it. The no-repaint property is what the provider's own test checks. Since the weekly rule by time (7.5)
the weekly window's count passes the prepend check on the guard's probe series, so its exemption was
removed (the guard fails an exemption that is no longer needed); the daily and weekly confirmations
and the yearly tier stay exempt.

## 7. Scored on the real low, and a weekly rule by time (2026-10-28)

Cody: "a weekly cycle isn't always 2 daily ones, it could be anywhere from 3 to 5 ... the goal for
the indicator is to be accurate to the numbers and the lows/highest/translation ... compare it to
cf ... should it be based on how often the correct actual low is chosen?" Sections 7.1 to 7.4 were
studied with no code changed (scratch scripts `lowscore.py`, `wsim.py`, `wsim2.py`, `wsim3.py`);
Cody then chose option B, built in 7.5.

### 7.1 What section 4.1's percentages are

They are how often a cycle's length fell in the window learned before it began, on the indicator's
own lows. They do not say whether those were the right lows. The window is the 20th to 80th
percentile, so it should hold about 6 in 10, not 8 in 10. A 10th to 90th percentile window holds
about 80% (79.5% in the width study), 0.77 of the cycle's middle length wide.

### 7.2 Is it the real low? Two scores

- **Against the original's lows:** exact bar and within two bars, one to one, as F1. The two are
  the same to the third decimal on daily charts, so the misses are whole lows, not bars.
- **Against the price:** a low is the real low of its cycle when no bar between the cycle top
  before it and the cycle top after it is lower. A top is the highest high between two lows of that
  tier. The original's own labels are scored the same way.

| Charts | Tier | Original | Cycle Position | Cipher C |
|---|---|---|---|---|
| Building set, daily charts | daily | 89.9% | 90.3% | 90.7% |
| | weekly | 77.8% | 80.7% | 78.4% |
| Blind set (cap17), daily charts | daily | 87.0% | 88.6% | 88.1% |
| | weekly | 78.4% | 81.7% | 79.3% |

All three pick the real daily low about 9 times in 10, and the real weekly low about 8 in 10. The
original is no better at this than either copy. Where they differ is agreement with the original
(section 4.5), not the price.

**Tops.** Where both daily lows are the original's, the top is on the original's bar 99.8 to 100%
of the time. The letter (left, middle, right) agrees 91.5% on the building set and 84.4% blind, the
same for both copies. The original's own letters follow the bar-midpoint rule of its own lows 94.4%
of the time on the building set since 2020, but only 81.7% on the 12 untuned blind charts (tuned
92.3%). No fixed day and no calendar midpoint does better (75% and 81%). Some of the rest look like
labels on a cycle that later changed (a top on bar 4 of 52 called right translated). Not decoded.

### 7.3 How many daily cycles the original puts in a weekly one

Counted on the original's own labels, daily charts:
- **Most assets, 1 to 3.** About 9% hold 1, 52% hold 2, 36% hold 3 and 2% more on the blind set.
- **Metals and miners, 3 to 10,** mostly 4 to 6. Their weekly rule is by weeks (`WeeklyByWeeks`).
- **Cycle Position under the count rule: 1 to 3,** about 2.25 on average (before 7.5).

**The original's weekly lows mostly fall outside its own weekly table.** 335 of 1,353 weekly cycles
(25%) land in the table's weekly range on the 36 daily charts. For example, the S&P table says 22 to
31 weeks; its drawn weekly cycles have a median of 18 weeks, and 11 of 56 are in the table. The
untuned default says 150 to 190 days; Google's median is 19 weeks, Doge's 12. Only the metals come
near their table (medians 27 to 30 weeks against 20 to 28). On most assets the original's weekly low
is every second daily low, as its count rule draws it, not the 3 to 5 daily cycles its table implies.

### 7.4 A weekly rule by time

The rule tried: the weekly low is the lowest daily low at least a gap after the newest weekly low,
made one when the next daily low is not under it. A daily low under the weekly low before the gap
either moves the weekly low to it ("replace") or is ignored. The gap is a fraction of the table's
shortest weekly cycle. 36 daily charts, building and blind together; the original's daily lows and
Cycle Position's.

| Rule | Lows | Agrees with the original's weekly (F1) | Rest / metals | Real weekly low | Daily cycles per weekly |
|---|---|---|---|---|---|
| Count, as now | original's | .762 | .788 / .527 | 74.3% | 2 or 3 |
| Gap 0.5 x min, replace | original's | .696 | .706 / .588 | 84.2% | 1–7, mostly 2–3 |
| Gap 0.8 x min, replace | original's | .604 | .598 / .674 | 80.8% | 1–7, mostly 3–4 |
| Gap 1.0 x min, replace | original's | .533 | .523 / .651 | 81.0% | 2–7+, mostly 3–5 |
| Count, as now | Cycle Position's | .708 | .732 / .484 | 76.6% | 2 or 3 |
| Gap 0.5 x min, replace | Cycle Position's | .665 | .676 / .542 | 85.7% | 1–7, mostly 2–3 |
| Gap 0.8 x min, replace | Cycle Position's | .575 | .574 / .591 | 81.9% | 1–7, mostly 3–4 |

("Count, as now" is a simplified replica without the engine's doubles; the engine's own count rule
scores .994 on the original's daily lows away from the metals, section 4.5's lineage.)

- **Every time rule agrees with the original less.** The longer the gap, the less, because the
  original draws its weekly lows by count, not by its table.
- **Every time rule picks the real weekly low more often:** 82 to 86% against 74 to 77%. The count
  rule makes the second daily low the candidate however shallow it is.
- **The number of daily cycles varies,** as Cody describes: 1 to 7 per weekly cycle. A gap of the
  table's whole minimum gives mostly 3 to 5.
- **On metals a time rule helps without a metals rule:** .53 to .65-.70 on the original's lows. The
  engine's metals rule is still better there (.892).
- **None lands in the table often.** At most 45% of the weekly cycles any rule draws fall inside the
  table's range: weekly lengths spread too widely for a 22 to 31 week window.

Three options were put to Cody: A, follow the original's drawings (the count rule); B, the time
rule with a short gap (0.5 x the shortest weekly cycle); C, the time rule with the gap the window's
start. Cody: "Let's do b".

The gap as built: half the symbol's own shortest weekly cycle, else half of 3 shortest daily cycles.
Simulated first (`wsim3.py`, 36 daily charts, Cycle Position's daily lows): tuned .638 / 87.3% real,
untuned .678 / 84.3%. Half of the untuned 150 days scored about the same (.690 / 84.3%); 3 shortest
daily cycles was kept as it needs no new number.

### 7.5 Built: the weekly rule by time (option B)

`CipherCEngine.WeeklyByTime(daily, data, gapBars, seed)`, called by Cycle Position only. Cipher C
is unchanged. The daily count's start is the first newest weekly low, drawn nowhere. Re-dumped over
every capture (`dumpe2eL.sh` with `AT_CC_PROVIDER=cp`), scored by `lowscore.py`:

| Captures | Chart | Agrees with the original's weekly (F1), count → time | Real weekly low, count → time | Original's real weekly low |
|---|---|---|---|---|
| Building set | daily | .725 → .626 | 80.7 → **85.9%** | 77.8% |
| | weekly | .682 → .558 | 77.3 → **82.4%** | 70.5% |
| | 4-hour | .615 → .581 | 78.2 → **84.1%** | 79.2% |
| Blind set | daily | .869 → .692 | 81.7 → **85.8%** | 78.4% |
| Blind, 12 untuned dailies | daily | .928 → .716 | 81.6 → 85.2% | 79.5% |
| Blind, tuned dailies | daily | .691 → .620 | 81.9 → 87.6% | 74.7% |
| Blind | 4-hour | .615 → .631 | 80.5 → 81.2% | 73.4% |

- **It picks the real weekly low about 85% of the time,** against 81% before and 78% for the
  original, on every set. The simulation said 85.7%.
- **It agrees with the original less,** as the simulation said: most on untuned assets, where the
  count rule was the original's own rule (.928 → .716).
- **Fewer weekly lows:** 696 → 608 on the building dailies (the original draws 544), and known
  sooner: a median 10 bars after the low, the original 27.
- **The weekly cycle holds 1 to 8 daily cycles** (closing low counted), mostly 2 to 4: building
  dailies 1:12% 2:47% 3:22% 4:10% 5:5% 6+:4%. Before: 1 to 3 only.
- **The learned weekly window holds as before:** 62.2 → 61.2% on the building captures, 61.5 →
  60.7% blind. The daily and yearly tiers are unchanged.
- The blind 1-week charts have only a handful of weekly lows (5 to 9); their numbers mean little.

## 8. "Low is in" and "Low expected" (2026-10-28)

Cody: "figure out anything that can help call and confirm lows with a confidence score ... the user
doesn't need to know 15 points", then "build it". Two readings on daily charts only, said as words:

- **Low is in**, on each day a daily low is held and not yet confirmed: the chance that Cycle
  Position will confirm it. Said as *likely to fail* (under 20%), *low* (under 60%), *medium* (under
  80%), *high* (under 95%) or *very high*, with the share, and with the facts it was read from:
  the days left until the confirmation, how far price is above the low (percent and average ranges),
  the chance at random of price going back to the low first, and which confirmations are on.
- **Low expected**, on each day after a confirmed daily low: the chance of a new daily low within
  the next k days, k = the shortest daily cycle ÷ 6 (Bitcoin 8). Said as *unlikely* (under 15%),
  *possible* (under 35%) or *likely*.

Both are in the table (a "Low is in" row and a "Low expected" row, daily column), in the bar's
state words, and the held low's own speech adds its Low is in sentence.

### 8.1 What they read

`CycleLowConfidence` (Core/Services/Indicators), the model in `CycleLowConfidence.Model.cs`
(generated). Everything is read from bars up to the day.

**Low is in**: small boosted trees (200 trees of depth 3; only-up constraints on the eight evidence
inputs), then a fitted map (isotonic) from score to share. Twelve inputs:
- **The chance at random of going back to the low first**: a random walk's chance of touching the
  low before Cycle Position's confirmation, `2·Φ(−ln(close/low) / (vol·√days left))`, with a 20-day
  high-low range volatility. Days left run to Cycle Position's own clock:
  `max(low, previous low + L − 2) + round(L/2)`. This alone is most of it (section 8.2).
- **How far price has risen off the low** in 14-day average ranges, and how far it has come back
  toward it.
- **How long each confirmation has been on**, as points by share of the shortest cycle: a higher
  low with the high between them broken; closes above the 10-day average; the stochastic (DSS 13, 8)
  over its 5-bar trigger; the SAR under price; a close above the 10 before it; and the days held.
- Two products (the touch chance with structure, and with the rise) and the days left.

**Low expected**: a fitted logistic on 19 inputs: how often this chart's past cycles of this age
ended within k days (its own last 40 cycles, plus 10 cycles' worth of the 30 assets' 1,178), the
position in the learned window, how far price is under the cycle's top (ranges and percent),
whether the cycle has closed under its own low, the close against the 10-day average and its slope,
a new 10-day low, where the top was, the DSS, both WaveTrends, an 81-bar stochastic, and whether a
low is held and for how long.

### 8.2 How they were tested

The label is Cycle Position's own: does it confirm the held low (Low is in), or draw a real daily low
in the next k days (Low expected). Only days a low is held and still standing are scored; days
after the confirmation are left out (the answer is known by then). 31 daily charts with Cycle
Position's lows. Bitcoin is held out of the shipped fit. A split check fits on 15 charts and scores
the other 15.

**Low is in, the words** (share of held days, and of those how many were confirmed):

| | likely to fail | low | medium | high | very high |
|---|---|---|---|---|---|
| 15 unseen charts (fit on 15) | 2% → 16% | 16% → 50% | 13% → 70% | 24% → 86% | 45% → 98% |
| Bitcoin 2020+ (fit on 30) | 1% → 12% | 21% → 50% | 14% → 74% | 25% → 87% | 40% → 99% |

**Calling it**: the first day each held low reaches a word:

| | first "high" | first "very high" |
|---|---|---|
| 15 unseen | 85% right, 4,118 of 4,119 caught, a median 11 days before the confirmation, 721 false | 96% right, 4,088 of 4,119, 7 days before, 162 false |
| Bitcoin 2020+ | 81% right, 39 of 39, 15 days, 9 false | 95% right, 39 of 39, 9 days, 2 false |

- **In the comparison of models** (the 15-and-15 split, before the exact clock and the refit), the touch chance alone gave AUC .888 on unseen charts; a logistic on everything .915; these
  trees .938, the same as an unconstrained boosted ceiling (.940). A pure random walk tops out
  around .92, so structure and the rise add real information.
- **Tried and adding nothing**: depth, lowest-of, under the previous low, 50/200 averages, the
  window, the cycle's age, oscillator states at the low, the weekly window's state, stochastic,
  RSI, 1-hour bars, timeframe multipliers.
- **A false "very high"** is mostly a real breakdown (the next cycle about 2.9 ranges lower), not a
  near miss.

**Low expected**: weak.

| | AUC | unlikely | possible | likely |
|---|---|---|---|---|
| 15 unseen (fit on 15) | .781 | 66% of days → 7% had a low | 23% → 22% | 12% → 45% |
| Bitcoin (fit on 30) | .788 | 67% → 7% | 25% → 20% | 7% → 59% |

Timing alone (the cycle's age against its past) is AUC .52-.57: the price state carries it.

**Parity**: the C# is the study's to 1e-9 on Bitcoin's 2,129 held days and 5,229 scored days
(`CycleLowConfidenceTests`, against the study's own numbers in `Fixtures/CycleLowConfidenceReference.cs`).

### 8.3 Caveats

- **It forecasts Cycle Position's own rule**, not a big turn: "very high" means Cycle Position is
  very likely to confirm the low (no undercut before its clock runs out). The touch chance is close
  to modelling that rule itself.
- **A dropped low is not read.** When the held low is undercut and dropped, Low is in goes away until
  a new low is held (the days-since count still carries the old one). The first fit included
  those days (21% of rows, all failures); it was refitted without them; the words are about the same.
- **The map from score to share was fitted on the same 30 charts** as the trees; the split check is
  the honest one, and it holds.
- **Bitcoin's 39 lows since 2020** are few.
- **Low expected is weak**: its "likely" is right about half the time.
- **Daily charts only.** Both were fitted on daily bars; off a daily chart neither is read (nor the
  weekly and yearly readings of section 8.5).
- **Low expected reads on past the newest confirmed low**, where the study (which needed the next
  low as its label) stopped; there it is the same model on bars the study could not score.

### 8.5 Weekly and yearly "Low is in" (2026-10-28)

Cody: "try and fit the weekly and yearly bottoms as well". The same reading for the weekly and the
yearly low being made, on daily charts, in the table's weekly and yearly columns of the "Low is in"
row, the bar's state words ("weekly low is in: high, 88%, for the low of 59,100.00; about 62 days to
its confirmation; ..."), and arrowing the weekly or yearly window adds it to the window's words.

**Which low it reads** (`CycleLowConfidence.WeeklySubject`, `YearlySubject`):
- **Weekly**: U the newest weekly low, C the drawn candidate (a daily low past the gap after U, not
  yet a weekly low), h the held daily low. It reads h when h is past the gap and strictly under C (or
  there is no C), as h would take C's place; else C; else h when it is inside the gap and under U, as
  it would take U's place. Days left: `low + L + round(L/2) − today` (at least 1).
- **Yearly**: the earliest low still standing that is the lowest for 0.15 of a yearly cycle before it
  (the first of equal lows), found from 0.8 of a cycle after the newest yearly low, and dropped when a
  later low is strictly under it, as `YearlyFromPrice` finds them. In calendar days (the bar's date),
  so a stock's or a currency's session hours do not move it. Days left: the bars in 0.15 of a cycle,
  from the low.

**What it reads**: weekly, the daily reading's 12 inputs on its own subject and clock, its own trees
(200, depth 3, only-up on the evidence). Yearly: only four, the touch chance, the rise, how far price
came back, and how long it has been held in 0.15-cycle units (capped at 2); 100 trees of depth 2, at
least 200 days a leaf. Both mapped to a share (isotonic). The yearly reading names no confirmations.

**Results**, every chart scored unseen (two halves, each fitted on the other; Bitcoin from the fit on
the other 30): the first day each low reaches "very high":

| | first "very high" right | lows caught | median days early (per chart) | "very high" days real |
|---|---|---|---|---|
| Daily | 96.6% (7,526 of 7,792) | 99% (7,526 of 7,625) | 7 (2 to 10) | 98.4% |
| Weekly | 94.6% (2,108 of 2,229) | 72% (2,108 of 2,948) | 12 (8 to 31) | 96.2% |
| Yearly | 94.5% (274 of 290) | 84% (274 of 328) | 40 (15 to 246) | 96.8% |

- **Per chart**: daily, "very high" days are real 97-100% on every chart. Weekly mostly 92-100%;
  Marathon 71%, Silver juniors 88%, Riot 91%; Gold catches 76 of 163 weekly lows, GDX 23 of 48.
  Yearly 87-100%; Silver catches 8 of 23 yearly lows, US oil 15 of 43, Gold 11 of 20.
- **Split checks**: weekly, fit on one half and scored on the other, AUC .835 and .851, "very high"
  96% real both ways; Bitcoin 2020+ AUC .85, first "very high" 15 of 15. Yearly, "very high" 98% and
  96% real, first "very high" right 97.1% and 89.9%; Bitcoin 3 of 3.
- **Weekly lows not caught** are mostly ones confirmed fast: the weekly low is confirmed when the
  next daily low is not under it, often before the reading reaches "very high".
- **Tried and adding nothing**: weekly extras (the daily reading's own chance, the daily low's
  confirmation, the nest, the candidate's age), other horizons for the clock, and bigger yearly input
  sets (the confirmations, the cycle's age, the window).

**Parity**: the C# is the study's to 1e-9 on Bitcoin, on exactly its bars: 4,085 days with a
weekly low to read, 1,266 with a yearly one (`Fixtures/CycleLowConfidenceTiersReference.cs`). Sums of
averages are added in numpy's order (pairwise), since on 2012-06-26 Bitcoin's close is exactly its
10-day average and the order decides the side. On all 31 captured charts, 57 of 241,519 weekly
days and 1 of 69,903 yearly days differ from the study. Each is a last-digit difference in the touch
chance (C#'s error function against Python's) that falls on a tree's split.

**Caveats**:
- It forecasts Cycle Position's own weekly and yearly lows, which come from its own rules (section
  7.5 for the weekly), not the original's.
- When the weekly reading reads the same low as the daily one, its confirmations repeat the daily
  ones'.
- Weekly lows with few days to their confirmation are often confirmed before "very high": a "high"
  there is worth hearing.
- The metals and oil are the weak charts; yearly lows are few (328 on 31 charts).
- Daily charts only; the 58 last-digit differences above.

### 8.4 Cipher B's big green dot against the cycle lows (research, 2026-10-28)

Cody asked whether Cycle Position's lows line up with Cipher B's big green dots (a WaveTrend cross
up with the fast wave under −60). The same 31 daily charts with default settings, candles, a dot
within ±2 bars; chance is the dots moved by a random shift (20 each):

| Lows | with a dot | chance |
|---|---|---|
| Daily lows that held (7,700) | 21% | 7% |
| Weekly lows (3,016) | 30% | 7% |
| Yearly lows (290) | 47% (58% within ±5) | 7% |
| Daily lows later dropped (4,661) | 26% | 7% |

- **Most cycle lows have no dot.** The dot needs a deep oversold wave. It is three times chance at a
  daily low and more at the bigger lows: about half of the yearly lows have one.
- **About 41% of dots sit within 2 bars of a low that held, 70% of a low found at all** (3.6x
  chance): the dot marks a bottom being made.
- **A dot does not say the low will hold**: lows with a dot held 58%, without 64%. A deep washout is
  undercut more often, as section 4's "low expected" flags were.
- **The dot comes a bar or two after the low** (median +1: it is a cross).
