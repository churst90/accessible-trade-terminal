# Cycle Position: where the chart is in its daily, weekly and yearly cycles

Written 2026-10-28. Cody: "use all of the visuals from cf that are in cipher c, but create a new
indicator implementing your ideas, test it and report the results using structured rules ... i want
something that helps me know where I am within a cycle so i can follow camel's technique ...
without a bunch of patch work but that is well designed, something that works for any asset if
possible, uses all the same symbols and markers as cipher c does."

The code is `CyclePositionProvider` (Core/Services/Indicators). It reuses `CipherCEngine` for the
daily rule and Cipher C's `Draw` for everything drawn. The tests are `CyclePositionProviderTests`.
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

**Weekly lows** are picked from the daily lows. The second daily low after a weekly low is the
candidate, and the next daily low judges it. The weekly count runs from the newest weekly low.

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
- Daily cycle 1 or 2 of the weekly one. While the count runs from a weekly candidate not yet a
  weekly low, it says so: "of the next weekly cycle, its low drawn but not yet confirmed".
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

On assets the original does not tune, Cycle Position finds the same lows as the original: one rule
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
- **The weekly cycle is always two daily cycles,** by construction of the weekly-from-daily rule. It
  says "daily cycle 1 or 2 of the weekly one", nothing more.
- **Too little yearly history to learn a yearly window** on most charts: Bitcoin's three yearly
  cycles since 2011 never reach the four needed.
- **The shortest daily cycle is still per asset.** It is the one number per asset and cannot be read
  from the price. Untuned symbols get 32.
- **The lab set and the building captures were looked at while building.** The blind numbers are
  one scoring of 28 captures.
- **Not seen or heard in the app.** The accessibility-lead review agent the project's hook asks for
  is not registered in this environment; not run.

## 6. Tests

`CyclePositionProviderTests`, 11 tests:
- **Nothing on a bar changes when later bars arrive.** Bitcoin daily is cut at six points. This
  covers every causal component and companion except the arc and the live band.
- **A window is learned from enough past cycles,** and is the default before.
- **The learning worked by hand.** Four lows, two cycles to a window, the hit scored against the
  window the cycle began with, the conditional share, and numpy-style percentiles.
- **The hit rate only grows** and never exceeds the cycles scored.
- **The phase is the day against the window.**
- **"Low had come" needs three past cycles,** and counts only those still unconfirmed.
- **The weekly nest is 1 or 2,** and resets.
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

`IndicatorCausalityTests` exempts the Cycle Position confirmations and weekly window from the
prepend check. A learned window depends on how much history precedes it, so prepending bars
changes it. The no-repaint property is what the provider's own test checks.
