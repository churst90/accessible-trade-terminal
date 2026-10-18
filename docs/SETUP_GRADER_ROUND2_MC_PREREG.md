# Setup grader round 2, the Market Cipher school: pre-registration

> **2026-10-18: run.** Results are in `docs/SETUP_GRADER_ROUND2_MC_FINDINGS.md`: No edge in crypto and
> equities. Nothing below this line was edited after the results existed.

Written 2026-10-18, before any round-2 result existed. This is stage 1 of 2. Stage 2 (a different
agent) builds and runs what is written here. **This file was committed before any forward return,
win rate, trade outcome or setup count was computed for any rule in it. The commit time is the
proof.** No backtest was run while writing it. Nothing was counted. The only data work was listing
the file names and sizes in `strategy-lab-data/`.

Round 1 is `docs/SETUP_GRADER_PREREG.md` (frozen) and `docs/SETUP_GRADER_FINDINGS.md`. This round
does not edit either of them.

## The question, in one paragraph

Round 1 mixed schools. It took Tim's weekly trend and cycle phase (a trend-continuation idea) and put
a Market Cipher deep-oversold dot under it, then graded the dot on the bar it printed, before any of
the confirmation Market Cipher traders wait for. Grade A almost never happened, and the verdict was
"No edge". Cody's direction for round 2: test each school separately, in its own terms and with its
own tools, and "wait for confirmation after the dot — some traders do, some don't, let's see how that
plays out". So this round asks three things about the Market Cipher school only, with Market Cipher B
tools only:

1. Does a Buy dot, taken at the next open, beat a random entry in the same higher-timeframe context?
2. Does waiting for the school's own confirmation (the trigger wave, with money flow turning up) beat
   a random entry in the same context?
3. Is waiting better than not waiting, on the same setups?

Grades by higher-timeframe money flow, and the school's own exits, are tested beside these.

## What I had already seen when writing this

Stage 2 must read this section, because it limits how blind the test is.

- **Round 1's results, all of them** (`docs/SETUP_GRADER_FINDINGS.md`). The ones that touch this
  design:
  - In equities, non-vetoed grade C Buy dots beat a random bar in the same context: excess +0.115R
    net, Holm 0.011. Round 2's test P1 on equities (the Buy dot at the next open, vetoes applied) is
    close to that test. The vetoes and the context are different, but **P1 equities is not blind.**
    The write-up must call it a near-replication with a different veto, not fresh evidence.
  - In equities the round-1 vetoes helped (vetoed dots 0.125R worse, Holm 0.029). In crypto, vetoed
    dots did better (+0.227R against −0.140R, 105 against 44 dots). **Because of that, the veto test
    here is two-sided.** That choice was made knowing the round-1 result.
  - Round-1 Buy dot counts, development era: about 153 graded crypto daily dots on ten assets, 4,159
    equity daily dots on 33 assets, and about 636 crypto 4h dots. These are the only counts I know,
    and they were made under round 1's exclusions.
  - Grade A starved because its three conditions were close to mutually exclusive with a deep
    oversold dot. In particular, money flow rising two bars in a row, checked on the dot's own bar,
    was rarely true.
  - No exit beat its random-exit control in equities. The lab ladder (X1) had the highest mean.
- **The lab's standing nulls**, as listed in round 1's "What I had already seen": fixed R targets,
  ATR trails and time stops have no skill against random exits; the signal exit has skill in crypto
  only (`docs/EXIT_FINDINGS.md`); the dip filter (Buy dot above SMA 200) is Fragile.
- **The 2026-10-17 battery's K1 finding:** a Buy dot essentially never fires while the daily EMA 11
  is above the daily EMA 33. Context must come from a higher timeframe.
- **How our Cipher B is built** (section 1.2), from reading `CipherBProvider.cs`.

None of the thresholds below was chosen from those numbers. Where a number comes from no source, the
text says so and the number is frozen.

---

## 1. Sources, and the Market Cipher method in its own terms

### 1.1 Sources

The rules files and transcripts are in the corpus agent's worktree,
`AccessibleTrader.StrategyLab/Corpus/rules/` and `.../Corpus/transcripts/`, read on 2026-10-18. They
are work in progress; the quotes below were checked against the transcripts where they carry a rule.
No `SCHOOLS.md` existed yet when this was written.

| Source | What was read |
|---|---|
| Crypto Face (CF), creator of Market Cipher | 16 transcripts, 2019-06 to 2026-10 |
| Market Cipher official channels | 27 videos and the vendor home page |
| Jayson (Jason) Casper | 17 transcripts, 2021-02 to 2026-10 |
| Dylan Waves | 1 walkthrough, 2023-11-24 |
| Trader Geo | 3 videos, 2023-06 to 2024-11 |
| Trading Bureau (Jordan) | 2 videos, 2025-11 and 2026-03 |
| Gunslinger Trades | 5 videos, 2022 |
| Trade Pro, and DaviddTech's coding of it | 1 video each, 2021 |
| Frankie Candles | 1 video, 2024-07-17 |
| Crypto Wolf (educators file) | 1 clip, 2023-12-28 |
| Gainium blog | 1 page, 2026-04-06 |

Citation form: short title, video date, timestamp, and the link at that second. Most transcripts are
YouTube auto-captions or Whisper, so wording is approximate and indicator names are sometimes
mangled.

One attribution note. CF's "How to use Market Cipher Basic Strategy CORRECTLY!" (2023-11-25,
`7U8zYPnqeco`) and the official channel's "How to Use Market Cipher Core Strategy CORRECTLY"
(2023-11-25, `sJbamrt-Cdc`) have the same words a few seconds apart. The corpus files disagree on
whether the speaker is CF or an unnamed live trader. This document calls it **"Basic CORRECTLY"**
and cites the CF copy.

### 1.2 The vocabulary, mapped onto our Cipher B

Our indicator is `AccessibleTrader.Core/Services/Indicators/CipherBProvider.cs`, fitted to Market
Cipher B (MCB) screenshots on 2026-10-17. The words the traders use map onto it like this.

- **"Light blue" or "white" wave = `Wave Trend` (WT1). "Dark blue" wave = `Wave Trend 2` (WT2).**
  WT2 is a 3-bar average of WT1. The traders never say which line is which formula; this mapping is
  ours. "The white cuts into the blue" and "the light blue turns into the dark blue" are the wave
  crosses.
- **The white lines at ±60.** "these plus or minus 60 white threshold lines ... the negative 60 line
  is going to be the oversold line" ([Extreme overbought/oversold, 2021-04-04, 00:30](https://youtu.be/KKHCyMxrgVw?t=30)).
  CF's narration also names alarm lines at ±53. The "dotted line" the official narrator mentions
  has no stated value anywhere in the corpus.
- **The green dot (Buy dot) = `Oversold Crossover`** in its default MCB mode: every cross of WT1 up
  through WT2 with WT2 at or below −60. Every Buy dot therefore ends a wave whose bottom is at or
  below −60.
- **"VWAP" (the yellow wave) = `WT Histogram` = WT1 − WT2.** Its zero cross is the same event as the
  wave cross. **So "the VWAP crossed up" adds nothing to a dot in our indicator,** and it is not used
  as a separate confirmation. (Dylan notes the same: "Green Dot vwap crossed" ([Dylan, 2023-11-24, 29:37](https://youtu.be/sYJD_E6pXV4?t=1777)).)
- **Money flow = `Money Flow Wave`:** 180 × the 60-bar average of Heikin-Ashi body ÷ range, centred
  on zero, clamped to ±100. Green above zero, red below.
- **"RSIs" = the `RSI` line:** MCB's magenta 0 to 100 line, which our fit found is a 40-bar
  stochastic of the typical price, smoothed over 3 bars. It is not a true RSI.
- **Divergences = `Bullish Divergence` and `Bearish Divergence`,** already moved to the bar where
  they can first be known. Our code has momentum divergences only. It has no money flow divergence.
- **Our `Anchor Wave` component is not the traders' anchor wave.** Ours is a WaveTrend with five
  times the periods, on the same bars. The traders' anchor wave is the first, deep wave on the same
  chart, before a smaller "trigger wave" (section 1.3). Our component is not used in round 2.

Two limits of the replica, stated so they cannot be argued later:

- Casper says "the money flow has not been duplicated in the ripoffs not even close to it"
  ([Divergence Masterclass, 2025-05-21, 24:29](https://youtu.be/11pOQcHI9SU?t=1469)), and Geo says
  only the paid script "has the money flow down". **Every result here is about our replica.** A null
  cannot be blamed on the replica afterwards, and a pass cannot be credited to the paid script.
- CF and the official channel read Heikin-Ashi candles ("I never use regular candles"); Casper reads
  regular candles ([Finding PERFECT Entries, 2021-04-10, 35:31](https://youtu.be/EwtsVvxt30g?t=2131)).
  Our waves read raw candles; our money flow reads Heikin-Ashi internally. Trades fill on raw prices.
  This is a named limitation, not a variant.

### 1.3 The anchor wave

All four sources that define it agree that an anchor wave is a deep first wave, past the −60 line.

- Official narrator: "An anchor wave is a fat deep initial wave we want to go at least below this
  white line and dotted line it's got to go deep" ([Core Strategy, 2023-08-03, 00:32](https://youtu.be/OscnxGwvAWs?t=32)).
- Casper: "an anchor wave is a nice deep momentum wave that usually goes down below the negative 60
  line" ([4/24 Beginner Strategy, 2024-02-06, 02:02](https://youtu.be/eIJBiUakLN0?t=122)).
- Dylan: "in general if a wave gets past the -60 line or the 60 line I will consider it an anchor
  wave" ([Dylan, 2023-11-24, 13:20](https://youtu.be/sYJD_E6pXV4?t=800)).
- CF: "a nice healthy blue wave a nice big one typically that dips below or above this blue line
  marker" ([Intro to Market Cipher, 2019-06-17, 21:09](https://youtu.be/bxkm4Kjubqs?t=1269)).

A green dot on the anchor is preferred, not required: "a Green Dot is nice too"
([Core Strategy, 2023-08-03, 14:27](https://youtu.be/OscnxGwvAWs?t=867)). In this test every setup
is a Buy dot, so every setup's wave is an anchor wave by all four definitions.

**The anchor is not the entry.** This is the school's own "wait for confirmation":

- Casper: "The anchor wave is telling you it's time to start looking to enter the trade. It doesn't
  mean to enter." ([2 Powerful Scalping Tips, 2021-08-19, 27:10](https://youtu.be/UdsLO5OQ6b8?t=1630)).
- CF, on the dots: "Do I long after just seeing these? Only look at these? No" ([How to Start
  Successfully, 2021-02-13, 24:13](https://youtu.be/tXJzvRKEhhY?t=1453)).
- Dylan: "it wasn't until these trigger waves printed that a good trade setup was actually
  presented" ([Dylan, 2023-11-24, 24:31](https://youtu.be/sYJD_E6pXV4?t=1471)).

### 1.4 The trigger wave

The trigger wave is the next, smaller wave after the anchor. The entry is its cross.

- Narrator: "the trigger wave should be smaller noticeably smaller afterwards" ([Core Strategy,
  2023-08-03, 00:32](https://youtu.be/OscnxGwvAWs?t=32)), and "your entry point is right at when the
  light blue turns into the dark blue on the trigger wave so right there boom I entered" ([Core
  Strategy, 2023-08-03, 03:05](https://youtu.be/OscnxGwvAWs?t=185)).
- CF: "looking for another blue wave that is smaller shorter than the previous" ([Intro to Market
  Cipher, 2019-06-17, 21:09](https://youtu.be/bxkm4Kjubqs?t=1269)).
- Casper: "a trigger wave simply means after you print a really deep wave any higher wave is called
  a trigger wave", and the one that matters is "that next momentum wave that prints below the zero
  line" ([Divergence Masterclass, 2025-05-21, 13:45](https://youtu.be/11pOQcHI9SU?t=825)).
- Dylan: a second wave that also goes below −60 still counts, because "it is still putting in a
  higher low compared to this wave" ([Dylan, 2023-11-24, 15:23](https://youtu.be/sYJD_E6pXV4?t=923)).
- Gunslinger: "you want to see a single like island lump below the zero line" ([4/24 Strategy,
  2022-03-29, 01:01](https://youtu.be/BozOB9RMbxs?t=61)).

**Invalidation.** A new wave below the anchor ends the setup. Dylan: if momentum "prints a lower
wave", "there's a good chance that this momentum shift is over" ([Dylan, 2023-11-24, 16:24](https://youtu.be/sYJD_E6pXV4?t=984)).
Casper's bearish version: "as long as we don't print a momentum wave above this one here it's still
a bearish trigger wave" ([Divergence Masterclass, 2025-05-21, 41:29](https://youtu.be/11pOQcHI9SU?t=2489)).
CF's version of the failure: the cross can "reject and just turn into a bigger blue wave" ([How to
Start Successfully, 2021-02-13, 14:59](https://youtu.be/tXJzvRKEhhY?t=899)).

**Not stated by anyone:** how much smaller the trigger must be (no ratio or points), or how many bars
may separate anchor and trigger. CF says only that divergence humps should be "kind of close
together" ([Ch.1 Divergences, 2019-06-08, 20:32](https://youtu.be/nO-9QTqt1fo?t=1232)).

### 1.5 Money flow, and what "deep green" means

Money flow is the other mandatory half. CF: "I don't make a trade unless I'm seeing these two ...
this is your core, everything else is a bonus" ([How to Start Successfully, 2021-02-13, 17:20](https://youtu.be/tXJzvRKEhhY?t=1040)),
meaning money flow and momentum.

**On the entry timeframe, money flow is red and turning up.** CF's rule is "We enter in the red and
exit in the green" ([Basic Strategy Part 3, 2020-05-14, 01:39](https://youtu.be/a7RAbpFe7ek?t=99)).
At the trigger:

- Dylan: when "money flow is still decreasing at the time ... I would not call this a signal"
  ([Dylan, 2023-11-24, 33:43](https://youtu.be/sYJD_E6pXV4?t=2023)).
- Trading Bureau: "Once we started to see that money flow had turned around to an upwards sloping
  direction, that is when we could have got bullish to enter the trade" ([Trigger Waves, 2025-11-26,
  05:52](https://youtu.be/q4UdlkQScmg?t=352)).
- Casper: "a very powerful thing here is the money flow is the money flow also getting higher as that
  momentum wave is getting higher" ([4/24 Beginner Strategy, 2024-02-06, 02:33](https://youtu.be/eIJBiUakLN0?t=153)).
- CF's own worked example, in order: a big green dot is "time to start sniffing"; "we have a deep
  anchor wave, then ... a smaller momentum wave, that's a good sign but we're not ready to long yet";
  money flow came down into the red, curved and "started coming up"; then "another momentum wave
  smaller, I'm looking along right here" ([How to Start Successfully, 2021-02-13, 38:02](https://youtu.be/tXJzvRKEhhY?t=2282)
  and [38:35](https://youtu.be/tXJzvRKEhhY?t=2315)).

**"Deep green" is about the higher timeframe.** Cody asked for money flow "in deep green as Crypto
Face says". In CF's words, thick green money flow is the environment, read on the larger timeframe,
and it is what makes the lower timeframe's red dips buyable:

- "when it's nice and thick and green" you look for green dots ([Intro to Market Cipher,
  2019-06-17, 14:55](https://youtu.be/bxkm4Kjubqs?t=895)).
- His backed trade: on the four-hour "we had momentum a nice deep anchor wave and trigger wave ...
  we're thick in the green money is in the pair ... that means on the lower time frames oh I'm
  looking to long these drops" ([How to Start Successfully, 2021-02-13, 31:15](https://youtu.be/tXJzvRKEhhY?t=1875)).
- "if it's a confirmed bull trend then yeah you want to hit them red" ([How to Start Successfully,
  2021-02-13, 36:57](https://youtu.be/tXJzvRKEhhY?t=2217)).
- "When it's green resistance is futile, when it is red support is futile" ([Intro to Market Cipher,
  2019-06-17, 15:25](https://youtu.be/bxkm4Kjubqs?t=925)).

So round 2 reads "deep green" as **the higher timeframe's money flow above zero and thick**, and the
entry timeframe's money flow as **red and turning up at the trigger**. Neither CF nor anyone else
gives a number for "thick". Geo says it outright: "thick is subjective ... money flow is relative to
its previous point in time ... so just use your discretion" ([Cheat Sheet Entry, 2023-09-08, 01:31](https://youtu.be/qC9JMB4nmHI?t=91)).
Section 2.3 turns that into a relative rule.

### 1.6 What they wait for after a dot, and who does not wait

Every source treats the bar close as the first confirmation: "when the candle closes and the green
dot is there after it closes then it's confirmed" ([Intro to Market Cipher, 2019-06-17, 13:23](https://youtu.be/bxkm4Kjubqs?t=803));
"if you don't let it confirm it can easily just unprint" ([Frankie Candles, 2024-07-17, 12:41](https://youtu.be/QqeLqy2z9yg?t=761)).
The lab works on closed bars only, so **every entry here already waits for the close.** "Not
waiting" means entering at the next bar's open.

Beyond the close, the sources split three ways.

**They wait for the trigger wave with money flow turning up** (the primary confirmation, E1):
the official narrator, Dylan, CF in his worked example, Casper in a downtrend, and Trading Bureau
(sections 1.3 to 1.5).

**They wait a candle for the cross to hold** (a short wait, secondary E1h). CF: "let a couple candles
go by for it to confirm because this wave can cross in then up then reject" ([How to Start
Successfully, 2021-02-13, 14:29](https://youtu.be/tXJzvRKEhhY?t=869)); "you can wait a candle for it
to confirm" ([Intro to Market Cipher, 2019-06-17, 24:13](https://youtu.be/bxkm4Kjubqs?t=1453)).

**They wait for money flow to cross into the green** (a long wait, secondary E1m). Basic CORRECTLY:
"in the right situation you should be able to enter right here basically when the money flow crosses
the zero line" ([Basic CORRECTLY, 2023-11-25, 03:41](https://youtu.be/7U8zYPnqeco?t=221)). Casper:
"we will enter long if the money flow crosses over the zero line" ([Amazing Trading Strategy,
2021-11-02, 04:19](https://youtu.be/3250VIgKwPA?t=259)), and "as soon as the money flow crosses over
into the blue ... set your long position" ([How to Stop losing Trades, 2021-02-23, 11:06](https://youtu.be/h3iRyoNHWCs?t=666)).

**They do not wait** (E0):

- Trade Pro: "when all conditions are met the entry signal is given by the blue wave cross"
  ([Trade Pro, 2021-06-25, 03:03](https://youtu.be/3RvKWdnrs2g?t=183)). DaviddTech coded the entry
  "just about here on this candle" ([DaviddTech, 2021-09-17, 04:52](https://youtu.be/OSupc_S0sJY?t=292)).
- Gainium: "Entry: On the candle following the green dot" (the "Classic Green Dot Reversal" section).
- Casper, for himself: "a lot of times ... I'm not necessarily waiting for a confirmation to happen"
  ([Divergence Masterclass, 2025-05-21, 10:53](https://youtu.be/11pOQcHI9SU?t=653)); "if we wait for
  the dot to print ... we miss most of the move" ([High Win Rate Beginner Strategy, 2021-09-07,
  09:00](https://youtu.be/hWNA0jOoRsc?t=540)).
- Crypto Wolf: "you kind of want to get in when this is printing right now it's not confirmed yet"
  ([Triple Anchor Wave, 2023-12-28, 02:03](https://youtu.be/mp9QEFb19HM?t=123)).

**Casper makes waiting depend on the regime,** in one breath: "If you're in an uptrend on the four
hour time frame, these things are very powerful to say it's time to go into a long. But if you're in
a downtrend ... Don't long the anchor wave. Long the trigger wave" ([2 Powerful Scalping Tips,
2021-08-19, 26:07](https://youtu.be/UdsLO5OQ6b8?t=1567) and [26:38](https://youtu.be/UdsLO5OQ6b8?t=1598)).
That is pre-registered as secondary test S7.

**Casper also names the cost of waiting.** Waiting for the trigger made an entry "not good", because
the stop still has to sit beyond the same swing, so the reward to risk gets worse ([How to not Lose
Trades, 2021-04-28, 10:36](https://youtu.be/sFA5iHDlPq8?t=636)). That is pre-registered as S5, using
the school's own stop.

### 1.7 What they never buy into

- **The higher timeframe falling hard.** Basic CORRECTLY, on the two failed entries: "one hour money
  flow coming straight down rsis coming straight down momentum down so this 4 minute entry did not
  have the higher time frames backing it up therefore it crapped out" ([Basic CORRECTLY, 2023-11-25,
  05:15](https://youtu.be/7U8zYPnqeco?t=315)). The winner he did not take, "on a Big Green Dot" while
  higher-timeframe money flow "was still coming down", "kind of got lucky" ([05:47](https://youtu.be/7U8zYPnqeco?t=347)).
  CF earlier: no long trigger wave when "pretty much every daily time frame higher is bearish"
  ([Whale Crossing, 2019-07-26, 11:30](https://youtu.be/_4YBoFdVrRA?t=690)).
- **A trigger with money flow still falling** (Dylan, section 1.5). In this test that is part of the
  E1 confirmation, not a separate veto.
- **One indicator alone.** "do not make a move off one indicator" ([How to Start Successfully,
  2021-02-13, 41:04](https://youtu.be/tXJzvRKEhhY?t=2464)). This is the case for E1 over E0, and is
  what P3 tests.

Considered and not used:

- **CF's yellow X on the dot's candle** (Cipher A). Round 1 showed our yellow X is the bearish wave
  cross itself, so it can never share a bar with a Buy dot.
- **"Thin" money flow** ("always avoid trading when the money flow index is thin", [Intro to Market Cipher, 2019-06-17, 19:29](https://youtu.be/bxkm4Kjubqs?t=1169)). It
  is the official tutorial's narration, not CF's own rule, and it would add a second thickness
  threshold. It enters only through the grades: thin higher-timeframe money flow cannot be grade A.
- **The higher timeframe overbought** (Geo: no longs while the environmental timeframe is above +60,
  [EASY Market Cipher Strategy, 2023-06-02, 06:14](https://youtu.be/2QGTGSXjHi4?t=374)). Nick, on the
  same channel family, says a weekly wave above +60 is an "acceleration phase" with "the potential for
  more upward move" ([Extreme overbought/oversold, 2021-04-04, 05:06](https://youtu.be/KKHCyMxrgVw?t=306)).
  The sources disagree, so neither is used.
- **Key levels** (Casper, Geo: "I put Market Cipher away until I'm at a key level"). Their levels are
  hand-drawn zones, volume profile and Fibonacci, which are not Market Cipher B tools, and round 1
  found the lab's computed levels behave like placebo lines. The official presenters give almost no
  location rule.
- **Trade Pro's 200 and 50 EMA filters.** Moving averages are not Market Cipher B tools.

### 1.8 Timeframe pairing

The higher timeframe is the "environment"; the lower one is where the entry is taken.

- Geo: execution timeframe = environmental timeframe ÷ 5 to 10 ([EASY Market Cipher Strategy,
  2023-06-02, 01:33](https://youtu.be/2QGTGSXjHi4?t=93)).
- Casper: divide by 10 (4H → 24m, 3H → 18m) ([How to Find the Best Trades, 2021-03-18, 04:09](https://youtu.be/Ma2lR62m4nA?t=249)).
- CF: "have the larger time frames have my back on the lower time frames" ([How to Start
  Successfully, 2021-02-13, 30:14](https://youtu.be/tXJzvRKEhhY?t=1814)); Basic CORRECTLY pairs a
  4-minute entry with the 1-hour (15 times).
- The basic strategy is for "any stock or crypto" ([Core Strategy, 2023-08-03, 00:00](https://youtu.be/OscnxGwvAWs?t=0)),
  and CF calls dots on the 2-hour to daily "a strong indication" ([Intro to Market Cipher,
  2019-06-17, 08:45](https://youtu.be/bxkm4Kjubqs?t=525)).

The lab has two pairings with history: **daily entries with a weekly environment** (7 calendar days
for crypto, 5 trading days for equities) and **4-hour entries with a daily environment** (6). All
three ratios sit inside Geo's 5 to 10. Casper's 10 and CF's 15 are not available with enough history,
and no other pairing is added.

Cody's phrase "4h trigger with daily anchor, daily with weekly anchor" uses "anchor" for the higher
timeframe. In the sources' vocabulary the anchor wave is on the entry chart; the higher timeframe
"backs" it. Both readings are tested: the entry chart's anchor and trigger in E1, and a higher
timeframe that has itself just printed a Buy dot in S6 (Casper: "We never got a bottom on the
four-hour. There's no indication that we should be entering a long", [How to Stop losing Trades,
2021-02-23, 04:23](https://youtu.be/h3iRyoNHWCs?t=263); Geo: the environmental green dot "relatively
close to or lower than this 60 line", [EASY Market Cipher Strategy, 2023-06-02, 02:37](https://youtu.be/2QGTGSXjHi4?t=157)).

### 1.9 Stops

Below the swing low, with a little room:

- Casper: below the last swing low, in nearly every strategy ([How to not Lose Trades, 2021-04-28,
  08:21](https://youtu.be/sFA5iHDlPq8?t=501)).
- Geo: "under the low" ([Cheat Sheet Entry, 2023-09-08, 06:38](https://youtu.be/qC9JMB4nmHI?t=398)).
- Trading Bureau: "below the previous swing low with a little bit of buffer" ([Trigger Waves,
  2025-11-26, 04:17](https://youtu.be/q4UdlkQScmg?t=257)).
- Gunslinger: "below the last wick" ([4/24 Strategy, 2022-03-29, 03:34](https://youtu.be/BozOB9RMbxs?t=214)).

No source gives a buffer size.

### 1.10 Exits

- **Money flow curving over in the green.** CF: when money flow, having come up, starts to curve
  over, "that's when you take profits" ([How to Start Successfully, 2021-02-13, 17:58](https://youtu.be/tXJzvRKEhhY?t=1078));
  "Thick in the green, this is take profit time" ([28:05](https://youtu.be/tXJzvRKEhhY?t=1685)).
  Casper: exit a long when money flow comes back down ([High Win Rate Beginner Strategy, 2021-09-07,
  27:48](https://youtu.be/hWNA0jOoRsc?t=1668)). Dylan: "once it gets pretty thick in the green and
  price starts to slow down that's when you'll close your trade" ([Dylan, 2023-11-24, 10:16](https://youtu.be/sYJD_E6pXV4?t=616)).
- **The waves crossing back down.** CF: exit when the white cuts into the blue the other way ([Intro
  to Market Cipher, 2019-06-17, 22:39](https://youtu.be/bxkm4Kjubqs?t=1359)). The narrator rode a
  move "to the next major wave when the light blue turned into dark blue" ([Core Strategy, 2023-08-03,
  09:53](https://youtu.be/OscnxGwvAWs?t=593)).
- **The first bearish divergence.** Geo: "the way that you can get out is by looking for the first
  Divergence on the time frame that you entered" ([Cheat Sheet Entry, 2023-09-08, 07:39](https://youtu.be/qC9JMB4nmHI?t=459)).
- Not used: Cipher A red and blood diamonds (the narrator's exits) because our Cipher A symbols are
  unverified against the paid script; DBSI scores (closed source); Trading Bureau's 1:1 and 1:3 fixed
  targets (fixed targets are already dead in the lab); Nick's "exit when the wave falls back under
  +60" (it applies only to waves that went above +60).

---

## 2. Exact definitions

Everything below is computed on closed bars only. "TTF" is the entry (trigger) timeframe. "HTF" is
the higher (environment) timeframe.

### 2.1 Bars and the higher timeframe

- HTF bars are **built from the TTF bars** with round 1's `SetupGraderCore.Aggregate`: weekly bars
  from daily (crypto: calendar weeks from Monday 00:00 UTC; equities: exchange weeks ending on their
  last trading day), and daily bars from 4h (UTC days).
- A TTF bar t may use only the newest **completed** HTF bar, h = `UsableAt[t]`, exactly as round 1
  does. The app's own higher-timeframe path is not used. The `bitstamp_*_1w` files are not used.
- Cipher B runs with its shipped defaults on each timeframe separately, with `__timeframe` set to
  "1d", "1w" or "4h". The defaults that matter: WT1 periods 9 and 12, WT2 = 3-bar average, MCB money
  flow over 60 bars, Buy dot at WT2 ≤ −60.
- ATR is ATR(14) on the TTF.

### 2.2 Waves, troughs and the setup

On the TTF:

- **Bull cross at t:** WT1(t−1) < WT2(t−1) and WT1(t) ≥ WT2(t). **Bear cross at t:** WT1(t−1) >
  WT2(t−1) and WT1(t) ≤ WT2(t). These are the code's own cross rules.
- **The down-wave ending at a bull cross t** runs from the most recent bear cross before t, called
  b(t), to t. If there is no earlier bear cross, the wave is undefined.
- **The setup ("the dot") is a Buy dot at bar i:** a bull cross with WT2(i) ≤ −60, the shipped
  `Oversold Crossover` in MCB mode. Its down-wave is the **anchor wave**.
- **The anchor wave ends** at e, the first bear cross after i. **The anchor trough** is
  T_A = the lowest WT2 from b(i) to e. Including the bars just after i matters: WT2 is a 3-bar
  average, so it can keep falling for a bar or two after the cross. Those bars belong to the anchor,
  not to a new lower wave.
- **The trough of a later down-wave** ending at bull cross j is T(j) = the lowest WT2 from b(j) to j.
- **The setup is void** at the first bar t after e with WT2(t) ≤ T_A: a new wave as deep as the
  anchor or deeper (Dylan, Casper, CF; section 1.4). A void setup gets no E1 trade. If that deeper
  wave ends in a Buy dot, that dot is a new setup of its own.

### 2.3 The higher-timeframe context, on every TTF bar

At a TTF bar t, with h the newest completed HTF bar, read three HTF Cipher B lines: money flow
MF_H, the fast wave WT1_H and the RSI line RSI_H.

- **V, the veto (higher timeframe coming straight down):** MF_H, WT1_H and RSI_H have each fallen on
  each of the last two completed HTF bars: x(h) < x(h−1) < x(h−2) for all three. Source: Basic
  CORRECTLY's "money flow coming straight down rsis coming straight down momentum down". **Free
  choice:** "straight down" = two falls in a row, the smallest count that tells a run from a single
  tick.
- **Thick:** MF_H(h) ≥ the median of |MF_H| over the trailing 104 completed HTF bars, h included,
  with at least 26 defined values. Source: Geo's "relative to its previous point in time". **Free
  choice:** 104 bars (two years of weeks; about 100 days of days) and the median. The window is in
  bars because the school treats every timeframe alike.
- **A, "deep green" (CF):** not V, MF_H(h) > 0, and thick.
- **B, "backed" (Basic CORRECTLY: "already in the red coming up"):** not V, not A, and
  MF_H(h) > MF_H(h−1).
- **C, not backed:** not V, not A, not B. The higher timeframe's money flow is falling (or flat),
  without all three lines coming straight down. Basic CORRECTLY calls such a trade "higher risk" and
  did not take it.

Order of precedence: V first, then A, then B, then C. If any input is undefined, the context is
undefined.

**Why this order (the grade hierarchy, section 4).** CF ranks a dot in green money flow as "golden"
and one in red as "still super strong" but "be cautious" ([Intro to Market Cipher, 2019-06-17,
16:25](https://youtu.be/bxkm4Kjubqs?t=985) and [17:57](https://youtu.be/bxkm4Kjubqs?t=1077)). His
strongest environment is thick green on the higher timeframe, where the lower timeframe's red is
bought. Basic CORRECTLY's best trade was backed by higher-timeframe money flow "in the red coming up",
and it rejected the trade where that money flow was still falling. So A > B > C, and V is the trade
not taken. Thick green but falling is still A: no source says to stop buying dips in a thick-green
environment because it has started to curve.

### 2.4 The entry policies, on the same setups

Every policy sees the same setups (section 2.2). Each takes at most one trade per setup.

**E0, enter at the dot.** Signal bar s = i. Entry at bar i+1's open.

**E1, enter at the trigger wave (primary confirmation).** The signal bar s = j is the first bull
cross after e with j ≤ i + 30 such that all of these hold:

1. T(j) > T_A: a higher low, so a smaller wave (CF, Casper, Dylan, the narrator).
2. T(j) < 0: the trigger wave is below the zero line (Casper 2025, Gunslinger).
3. MF(j) > MF(j−1) on the TTF: money flow turned up at the trigger (Dylan, Trading Bureau, Casper
   2024, CF's worked example).
4. The setup was not void at any bar up to j.

Entry at bar j+1's open. A setup with no such j by bar i+30 is **unconfirmed**: no E1 trade.

- A later Buy dot can be the trigger, if its wave's trough is higher than the anchor's (Dylan:
  a second wave below −60 still counts). Then the E1 trade of the earlier setup and the E0 trade of
  the later one share an entry. That is allowed; the significance tests treat nearby setups as one
  cluster (section 6.5).
- Crosses that are higher lows but fail rule 3 are skipped, and the wait continues to the next cross.
  Dylan measures triggers "from the original anchor".
- **Free choice:** the 30-bar window. No source gives a number. Thirty bars is about two WaveTrend
  cycles at periods 9 and 12, long enough for a second wave and short enough that the anchor still
  belongs to the same move.
- **Not imposed:** the narrator's "noticeably smaller". It has no number, and Casper and Dylan accept
  any higher low. The anchor-to-trigger depth difference is reported descriptively (section 3.4).

**E1h, wait a candle for the cross to hold (secondary).** If WT1(i+1) ≥ WT2(i+1), the signal bar is
s = i+1 and entry is at bar i+2's open. Otherwise the setup is unconfirmed for E1h. Source: CF,
"you can wait a candle for it to confirm". **Free choice:** one candle (CF says one or two).

**E1m, wait for money flow to turn green (secondary).** The signal bar s is the first bar m from i to
i+30 with MF(m) > 0 and either m = i or MF(m−1) ≤ 0. Entry at bar m+1's open. If money flow is
already green at the dot, E1m is the same trade as E0 (Trade Pro and Geo want it green at the dot).
Otherwise, no green by i+30 means unconfirmed. Sources: Basic CORRECTLY and Casper (section 1.6).

### 2.5 The school's own stop (used by the exit family and S5)

- **E0:** the lowest low from b(i) to i, the swing low of the anchor wave.
- **E1, E1h, E1m:** the lowest low from b(i) to s, the swing low of the whole anchor-to-entry
  structure. Casper keeps the stop beyond the same swing whichever entry is used ([4/24 Beginner
  Strategy, 2024-02-06, 09:08](https://youtu.be/eIJBiUakLN0?t=548)).
- Subtract 0.25 × ATR(s). If the stop is then less than 0.5 × ATR(s) below the entry, put it at
  entry − 0.5 × ATR(s).
- **Free choices:** the 0.25 buffer (Trading Bureau's "little bit of buffer") and the 0.5 floor. Both
  are round 1's numbers, reused unchanged.

### 2.6 Exclusions (undefined is not false)

A setup is **eligible** only if all of these are defined:

- its context at i (section 2.3);
- its anchor wave (an earlier bear cross exists);
- the full 30-bar confirmation window and every 20-bar outcome it could need: bar i + 30 + 21 must
  exist in the data.

Every excluded setup is counted, with its reason. Weekly money flow needs 60 weeks, and the thickness
median needs 26 more, so most assets lose about their first 86 weeks. Bars whose context is
undefined are also left out of every control.

### 2.7 Frozen numbers

None of these may be tuned.

| What | Value and source |
|---|---|
| Buy dot | WT2 ≤ −60 at a bull cross (shipped MCB rule; the white line) |
| Anchor | the Buy dot's down-wave, through the next bear cross |
| Trigger | higher trough, trough below 0, money flow up 1 bar (sources) |
| Void | WT2 at or below the anchor trough after the anchor ends (sources) |
| Trigger window | 30 TTF bars after the dot (free choice) |
| Hold wait (E1h) | 1 candle (CF) |
| Money-flow wait (E1m) | first close above 0 within 30 bars |
| Veto | 3 HTF lines each down 2 bars running (2 is a free choice) |
| Thick | at least the median absolute MF_H over 104 HTF bars, 26 values minimum (free choice) |
| HTF anchor (S6) | an HTF Buy dot on one of the last 3 completed HTF bars (free choice) |
| Stop buffer, floor | 0.25 ATR, 0.5 ATR (round 1) |
| Outcome harness | 1 ATR stop, 2R target, 20 bars, stop wins ties (lab standard) |
| Exit cap | 60 bars (round 1) |
| Costs | 15 bps a side (engine defaults) |
| Era cut | 2023-01-01 by the dot's date (round 1) |
| Minimum counts | 30 / 15 / 10 (round 1) |
| Noise | 25% of the series' own volatility, seeds 0, 1, 2; 50% kept (round 1) |
| Cluster gap | 50 TTF bars (the window plus the horizon) |
| Draws | 10,000 permutation; 2,000 random exits |

---

## 3. The entry-policy comparison (the headline)

### 3.1 The outcome

For the entry-policy and grade tests, every trade uses **the lab's standard R harness**, so E0 and E1
are compared on the same footing: entry at the next open after the signal bar s, stop 1 × ATR(s)
below the entry, target 2R, exit at the close of bar s + 20 if neither is hit, the stop wins a bar
that touches both (`SetupGraderCore.StandardR`). Net R subtracts 2 × 0.0015 × entry ÷ ATR(s). **Net
is primary; gross is printed beside it.** The school's own stop and exits are tested separately
(section 5 and S5), because a different stop changes what one R means.

### 3.2 The matched random control

For each trade, the control is the mean net R of **all** TTF bars on the same asset, in the same era,
whose own context state (V, A, B or C, section 2.3) equals the context at the trade's signal bar s.
Each control bar's R comes from the same harness, entering at its next open. This is round 1's
averaged control. For E0 the context is read at the dot. For E1 it is read at the trigger bar, so the
control asks "is this entry better than a random bar in the environment the trader is in when he
actually buys?".

- A trade's **excess** = its net R minus its control.
- **Ex0** = the mean excess of E0 trades. **Ex1** = the mean excess of E1 trades.

### 3.3 Waiting against not waiting, per setup

The trader who waits sees the same setups as the one who does not, and sometimes ends up with no
trade. So the fair comparison is per setup:

- V0(d) = the E0 trade's excess for setup d.
- V1(d) = the E1 trade's excess if setup d confirmed, and 0 if it did not (no trade, no excess).
- **ΔV = the mean of V1(d) − V0(d) over all eligible non-vetoed setups.** ΔV > 0 means waiting added
  edge per setup seen.

Excess is used rather than raw R so that the policy holding more trades does not win just by carrying
more of an up-drifting market.

ΔV > 0 can also happen because E0 loses and E1 rarely trades. **The verdict rules (section 6.8) keep
these apart:** "waiting is better than the dot" (P3) is not "the trigger-wave entry has an edge" (P2).

### 3.4 Reported beside the tests (descriptive, not tests)

- The confirmation rate, the share void, the share whose window ran out, and the median bars from
  the dot to the trigger, by class and grade.
- **E0 split by what happened next:** Ex0 on the setups that went on to confirm, and on those that
  did not (Cody asked for this). The confirmed half is not a tradable policy, because nobody knows at
  the dot that it will confirm.
- Per trade: Ex1 against Ex0 on the confirmed setups, paired.
- The anchor-to-trigger depth difference (T(j) − T_A): E1 excess for differences above and below
  their median. This checks the narrator's claim that a bigger difference gives a bigger move.
- All Buy dots including vetoed ones, E0, ungraded (reference row).

---

## 4. Grades, and the ordering test

The grade of a setup is its context at the dot (section 2.3): **A, B, C or V.** It is the same for
every entry policy, so the grades are compared on the same setups.

- **The hierarchy has one input family** (the higher timeframe's Cipher B lines) and two free numbers
  (the thickness window and the "straight down" count). Round 1's grade A needed three layers at once
  and starved.
- **The hierarchy:** A > B > C on mean net R and on excess. V is the trade the sources do not take.
- **The ordering test** compares A with C. B sits between them on point estimates only.
- **ExA, ExB, ExC** are the mean excesses of the grade's trades, for E0 and for E1 separately.

---

## 5. Exits: the school's own, against the lab's ladder

**Population:** the eligible non-vetoed trades of E0 and of E1, per daily class, development era.

**R is per position, from the initial risk:** the sum over exits of (portion × (exit − entry)) ÷
(entry − initial stop), as `SetupGraderCore.Ladder` and `StopAndRule` already compute it. Every
variant is also reported in **ATR units**, (exit − entry) ÷ ATR(s) less the cost in ATR units,
because the variants use different stops.

| Id | Exit rule |
|---|---|
| X1 | the lab's ladder: stop 3 × ATR, half off at 2R, half at 4R, then trail 3 × ATR (round 1's X1) |
| X2 | the school's stop (2.5); exit at the close of the first bar from the entry bar on with a bear cross (CF, the narrator); 60-bar cap |
| X3 | the school's stop; exit at the close of the first bar with MF > 0 and MF falling, MF(t) < MF(t−1) (CF's "exit in the green", Casper, Dylan); 60-bar cap |
| X4 | the school's stop; exit at the close of the first bar with a `Bearish Divergence` marker (Geo); 60-bar cap |
| X5 | the school's stop; time exit at the close of bar s + 20 (the baseline; a time stop is its own random control) |

**The control for every variant** is round 1's: keep each entry and its variant's initial stop, draw a
random exit bar from that variant's own holding-time distribution (pooled per class and policy), and
repeat 2,000 times. The p-value is the share of random books whose mean R is at least the variant's.

**Prior, stated now.** The ledger predicts no mechanical exit beats random, and that a signal exit
shows skill in crypto only. X5 should sit near its control. If X3 or X2 shows skill, it should be in
crypto.

---

## 6. Universe, discipline and verdicts

### 6.1 Classes and timeframes

**Crypto and equities are never pooled.** Round 1 showed the vetoes behaving in opposite directions
in the two classes.

- **Crypto daily, primary.** BTC, ETH, LTC, XRP, BCH, ADA, SOL and DOGE (Bitstamp daily) and KAS
  and TAO (MEXC daily). Weekly environment built from the daily bars. Only BTC, XRP, LTC, ETH and BCH
  have much development-era history.
- **Equities daily, primary.** The 33 Yahoo series of round 1: AAPL, CAT, CVX, DIA, EEM, EFA, FXI,
  IBM, IWM, JNJ, JPM, KO, MCD, MMM, MSFT, PFE, PG, QQQ, SPY, T, VTI, VZ, WMT, XLB, XLE, XLF, XLI,
  XLK, XLP, XLU, XLV, XLY and XOM. Weekly environment built from the daily bars.
- **Crypto 4h, secondary (S4).** BTC, ETH, XRP, SOL and DOGE (Bitstamp 4h, capped at 20,000 bars) and
  KAS and TAO (MEXC 4h). Daily environment built from the 4h bars. From the file sizes, SOL, DOGE,
  KAS and TAO start in late 2022 or 2023, so the development era is in practice BTC, ETH and XRP from
  2017. Stage 2 reports the exact coverage.
- **Left out:** GLD, SLV, USO, TLT and IEF (another class, too few); the Twelve Data duplicates; the
  2-day, 12-hour and 1-hour files (another pairing would be another variant); the Alpaca 5-minute
  files; every macro, cross-series and event file. **No Tim structure, no cycle tool, no macro.**
- **Longs only.** The mirror short is not tested in round 2. Round 1's short side showed nothing, and
  a short round needs its own pre-registration.

### 6.2 Eras

- **Development era:** setups whose dot is before 2023-01-01. **Every confirmatory test runs here.**
  A development setup's E1 trade can run up to 50 bars past the cut; that is accepted and stated.
- **Held-out era:** dots from 2023-01-01 to the end of the data. It is used for no choice. It is read
  once, after the development tests, for sign agreement only.

### 6.3 Costs

15 bps a side, the engine's 10 bps commission plus 5 bps slippage. Net is primary everywhere.

### 6.4 Noise retention

- Perturb every series at 25% of its own daily volatility, seeds 0, 1 and 2, development era only.
  Rebuild everything: HTF bars, Cipher B on both timeframes, waves, setups, contexts, controls.
- **Retained** = the mean noisy statistic ÷ the clean one. A claim needs at least 50%.
- **A pre-registered change from the lab's `GateCommand.Perturb`:** that function sets each bar's
  open equal to its close (round 1, deviation 10). Here every entry fills at an open, so an open equal
  to the close would turn "next open" into "next close" on noisy bars, and money flow's Heikin-Ashi
  bodies would change shape too. Stage 2 must use a new function that keeps each bar's own opening
  gap: noisy open = previous noisy close × (open ÷ previous close), with the high and low widened if
  needed to contain the open and close. **`GateCommand.Perturb` itself must not be edited,** because
  other results depend on it. Both functions' behaviour must be stated in the findings.
- Noise runs are made for P1, P2 and P3 in both daily classes, and for the grade tests if grade A is
  testable.

### 6.5 Significance

- **Ex > 0** (P1, P2, GA, the Ex halves of S2 and S3, and S4's P1 and P2): round 1's block
  permutation. In each asset × era × context stratum, cut the bar sequence into 20-bar blocks,
  permute the block order carrying the signal labels with their blocks, and recompute the mean
  excess. 10,000 draws. One-sided p = (1 + draws at or above the observed) ÷ 10,001.
- **Per-setup differences** (P3, the ΔV halves of S2 and S3, S4's P3, and S5): a cluster
  sign-flip. Setups on the same asset whose dots are within 50 TTF bars of the previous setup form
  one cluster, because their trades can overlap. Flip the sign of each cluster's summed per-setup
  difference at random, 10,000 times. All are one-sided except S5, which is two-sided.
- **Orderings and group differences (GO, S1, S6, S7):** permute the two labels among the setups
  within each asset × era stratum, each setup's value held fixed, 10,000 draws. Setups less than 20
  bars apart share outcome bars, so this p is optimistic; that is one reason the held-out sign check
  is required. S1 is two-sided: p = (1 + draws with |null| at or above |observed|) ÷ 10,001.
- **Seeds:** `StableSeed.From("setup-grader-mc", test id, asset or class)`. Never
  `string.GetHashCode()`.
- **Family-wise correction:** Holm–Bonferroni at α = 0.05 inside each family, using the full family
  size even when a member is not testable.
- **Every family can pass Holm's first step.** With 10,000 draws the smallest p is about 0.0001; with
  2,000 random exits it is about 0.0005. The strictest first step is 0.05 ÷ 19 = 0.0026. (Round 1's
  200-draw placebo could not pass its first step.)

### 6.6 Breadth (per-asset win share)

For P1 and P2: more than half of the assets with at least 10 development-era trades must have
positive excess. For P3: more than half of the assets with at least 10 development-era setups must
have ΔV > 0. For GA: as round 1, assets with at least 3 grade A trades. The per-asset table is printed
whatever the result.

### 6.7 Minimum events, and the counts-only stage

- **Confirmatory:** at least 30 events in the development era (trades for P1, P2 and the grades;
  setups for P3, which also needs at least 15 confirmed). With 15 to 29, the test runs and can be
  Suggestive only. Below 15 it is "not testable": count only.
- **Held-out sign check:** at least 10 events, else "not checkable", which caps the verdict at
  Suggestive.
- **Fallback if grade A has fewer than 30 in a class:** one extra pooled grade A∪B against C, for E0
  and for E1, reported as Suggestive whatever its p. That is 4 extra tests, counted in the budget.
- **The counts-only stage comes first, and changes nothing.** Round 1 learned that a counts-only run
  takes seconds and would have shown grade A starving. Stage 2 must commit the harness, then run it in
  a counts-only mode that computes no outcome and prints: setups per class and era; exclusions by
  reason; setups per grade; confirmed, void and expired counts for E1, E1h and E1m; the median wait;
  the number of clusters. That output goes into the findings before any outcome is computed. **The
  counts may decide only what this section already says** (testable, Suggestive only, not testable,
  the A∪B fallback). No threshold, window or definition may be changed because of them. If they look
  bad, the findings say so.

### 6.8 The families and the budget

**Family P, the entry policies (6 tests, Holm inside).** For each of crypto daily and equities daily:

| Id | Test (development era, net) |
|---|---|
| P1 | Ex0 > 0: the Buy dot at the next open, non-vetoed setups |
| P2 | Ex1 > 0: the trigger-wave entry, non-vetoed setups that confirmed |
| P3 | ΔV > 0: waiting beats not waiting, per setup |

**Family G, the grades (8 tests, Holm inside).** For each daily class and each of E0 and E1: GA,
ExA > 0; GO, ExA − ExC > 0.

**Family S, secondary (19 tests, Holm inside).**

| Id | Test | Count |
|---|---|---|
| S1 | the veto: Ex0 of vetoed setups minus Ex0 of non-vetoed ones, two-sided, per daily class | 2 |
| S2 | wait a candle (E1h): Ex1h > 0, and ΔV for E1h > 0, per daily class | 4 |
| S3 | wait for green money flow (E1m): Ex1m > 0, and ΔV for E1m > 0, per daily class | 4 |
| S4 | crypto 4h with a daily environment: P1, P2 and P3 | 3 |
| S5 | the cost of waiting under the school's own plan (section 2.5 stop, X3 exit): per-setup E1 minus E0 in net ATR units, no trade = 0, two-sided, per daily class | 2 |
| S6 | a higher timeframe that has its own bottom: Ex1 of non-vetoed E1 trades whose HTF printed a Buy dot on one of the last 3 completed HTF bars at the dot, minus Ex1 of the rest, per daily class | 2 |
| S7 | Casper's regime rule: the mean of V1 − V0 in grade C minus that in grade A > 0 (waiting helps more when the environment does not back the trade), per daily class | 2 |

S5 is two-sided because the sources disagree on its direction: CF and the narrator wait, Casper says
waiting worsens the reward to risk. S1 is two-sided for the reason given in "What I had already
seen".

**Family X, exits (16 tests, Holm inside).** X1 to X4 against their random-exit controls, for E0 and
E1, per daily class. X5 is reported, not tested.

**Budget: 6 + 8 + 19 + 16 = 49 pre-registered tests**, plus up to 4 fallback tests.

**Pre-registered descriptive runs** count toward `variantsTried` but are not tests: everything in
section 3.4; gross figures; the three noise seeds; the per-asset tables; ATR-unit figures; the A∪B
fallback if it is triggered; the counts-only run.

**Counting rule.** Every run stage 2 makes beyond this list is counted in `variantsTried` and listed
with its result: a different window, threshold or definition, another era cut or asset subset, a
bug-fix rerun, an exploratory look. A bug fix that changes a pre-registered number must report both
numbers. Anything new is exploratory and must be labelled so in the same sentence as its number.

### 6.9 Verdicts

Each question gets its own verdict per daily class. The 4h class (S4) is secondary and can only
support or contradict the daily verdict in words.

**"The trigger-wave entry has an edge" (Supported)** in a class needs all of these:

1. P2 significant after Holm.
2. Held-out Ex1 > 0 with at least 10 held-out trades.
3. Breadth (section 6.6).
4. At least 50% of Ex1 kept under noise.

The same four criteria, with P1 and Ex0, decide **"the Buy dot at the next open has an edge"**; and
with P3 and ΔV, **"waiting beats not waiting"**. The grades are **Supported** only if GA and GO are
both significant after Holm, the point estimates run A > B > C on both mean net R and excess, held-out
ExA > 0 and ExA − ExC > 0 (at least 10 held-out grade A trades), breadth holds and 50% survives noise.

- **Fragile:** criteria 1 to 3 hold, noise fails.
- **Suggestive:** the main p is below 0.05 before Holm but not after; or the count is 15 to 29; or
  the held-out check is not checkable. A Suggestive result is a lead, not an edge.
- **No edge:** anything else.

### 6.10 What counts as "no edge", stated plainly

For a class:

- If P1 is not Supported or Fragile: **a Buy dot, bought at the next open, is no better than a random
  bar in the same higher-timeframe environment.**
- If P2 is not Supported or Fragile: **waiting for the trigger wave with money flow turning up gives an
  entry no better than a random bar in the same environment.**
- If P3 is not Supported or Fragile: **waiting is not better than buying the dot.**
- If P3 passes and P2 does not: **waiting is better only because buying the dot loses, or because
  waiting trades less. Neither entry is an edge.** The write-up must use those words.
- **If neither P1 nor P2 passes, the Market Cipher entry, as written here, has no edge in that class,
  whatever P3, the grades, the secondary tests or the exits say.**

**The null cannot be reframed afterwards** by any of these:

- changing the window, the trigger rules (a margin, the zero rule, the money flow rule), the void
  rule, the thickness rule, the veto count or the HTF dot window;
- promoting E1h or E1m, or a grade, or a secondary test, or a subgroup found after the fact, to the
  headline;
- moving from per setup to per trade for P3, or counting "no trade" as anything but zero;
- switching the headline to the school's own stop or exit, or from net to gross, or from excess to
  raw R;
- dropping assets or eras, moving from the development era to the full sample, or using the
  held-out era for anything but signs;
- pooling crypto with equities, or using the 4h class to rescue a daily one;
- blaming the replica (section 1.2), or crediting the paid script.

Also stated now:

- **A good exit result does not rescue the entries.** Exits are compared on the same trades; they
  are not evidence that the entries work.
- **If the veto turns out to remove the better trades** (S1 in the opposite direction), the
  write-up says the veto is wrong in that class. It does not turn that into a "buy the capitulation"
  strategy; that would need its own pre-registration.
- **If grade A beats C only because its control is lower,** the matching did its job; the verdict is
  about excess.

---

## 7. What round 1 taught, and what changed because of it

Written before any round-2 result existed.

1. **Round 1 mixed schools.** It put a Market Cipher deep-oversold dot under Tim's weekly trend and
   cycle phase, which is a trend-continuation context. A dot almost never fires inside that context,
   so the best grade could not happen. **Change:** one school, its own tools. Context comes only from
   the higher timeframe's Cipher B lines, which is how the Market Cipher sources read context. No
   candle-body trend, no moving-average phase, no levels, no macro.
2. **Round 1 graded the dot on the bar it printed, before any confirmation.** Its "money flow rising
   two bars" confirmation was checked on the dot's own bar, where a deep oversold dot rarely has it.
   The sources wait for money flow to turn after the dot, at a later and smaller wave. **Change:** two
   entry policies on the same setups, E0 at the dot and E1 at the trigger wave, with money flow
   checked at the trigger. Dots that never confirm are counted as no trade, and their E0 outcome is
   reported separately.
3. **Grade A needed three near-exclusive conditions at once and starved** (0 crypto, 2 equities).
   **Change:** the grades come from one input family with two free numbers, the headline does not
   depend on the grades at all, and a counts-only run is mandatory before any outcome, with its
   consequences written here in advance.
4. **The vetoes behaved in opposite directions in the two classes.** In crypto, vetoed dots did
   better. **Change:** the classes stay separate, the veto test is two-sided, and the veto is the one
   the sources state (the higher timeframe coming straight down on all three lines, two bars running),
   not three separate one-bar conditions.
5. **The body-pivot rule depended on opening gaps,** so MEXC's KAS and TAO never got a trend.
   **Change:** no candle-body rule is used anywhere.
6. **The placebo test could not pass Holm with 200 draws.** **Change:** every family's draws are sized
   so its smallest p can pass Holm's first step (section 6.5).
7. **The noise function sets the open equal to the close.** Round 1 never ran it. Round 2 enters at
   opens and reads Heikin-Ashi money flow, so it would bias both. **Change:** a gap-keeping
   perturbation, specified in section 6.4, without editing the shared function.
8. **The engine defects are fixed on main** (R per position from the initial stop; a higher-timeframe
   bar used only once closed). This harness still builds its own HTF bars and computes its own R, as
   round 1 did, so it does not rely on the fixes.
9. **Round 1's equities result for non-vetoed dots is known.** **Change:** it is disclosed at the top,
   and P1 equities must be written up as a near-replication with a different veto.
10. **The verdict must survive its own exits.** Round 1's exit family found nothing, and the ledger
    says the same. **Change:** the headline uses the lab's standard harness, the school's own stop and
    exits are a separate family, and S5 asks whether waiting pays under the school's own plan.

---

## 8. Files for stage 2

- **Read first:** this file, then `docs/SETUP_GRADER_PREREG.md`, `docs/SETUP_GRADER_FINDINGS.md` and
  `docs/EXIT_FINDINGS.md`.
- **Reuse rather than reinvent** (`AccessibleTrader.StrategyLab/SetupGraderCore.cs`): `Aggregate`
  and `UsableAt` for completed HTF bars, `StandardR`, `CostR`, `Ladder`, `StopAndRule` and `Holm`.
  The block permutation is in `cipher-a-battery`, the random-exit control in `exits`.
- **New:** a command (suggested name `setup-grader-mc`) with a `--counts-only` flag; the wave and
  trough segmentation; the setup, void and trigger finder; the HTF context per bar; the three entry
  policies; the cluster sign-flip; the gap-keeping perturbation.
- **Tests, each seen red with its defect put back** (the lab rule: a guard must be proven to fail):
  1. E1 never enters before its trigger bar has closed, and never uses a bar after it.
  2. Truncating the series after any bar changes no setup, context, void or trigger at or before
     that bar (no look-ahead).
  3. The anchor trough includes the bars between the dot and the next bear cross, so WT2's lag after
     the cross does not void the setup.
  4. A deeper wave voids the setup, and a higher sub-zero cross with money flow falling is skipped.
  5. The control stratum includes the context state, read at the signal bar.
  6. R is per position from the initial risk after a stop move.
  7. The gap-keeping perturbation keeps each bar's open distinct from its close.
- **Write the results** in a new `docs/SETUP_GRADER_ROUND2_MC_FINDINGS.md`, with an `edges.json`
  record (`EdgeRegistryTests` requires one). Record the sha256 of every input file before and after
  the run. **Do not edit this pre-registration after results exist,** except to add a dated pointer to
  the findings at the top.
