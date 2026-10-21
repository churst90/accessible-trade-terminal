# Camel Finance's method, in his own terms

Written 2026-10-18. This is the cycle school's reference document for the lab: what Camel Finance
("Camel") actually does, in his words, with a timestamp for every claim, and a tag on every claim
saying whether this lab can test it. It is the input to two other documents:

- `docs/CYCLE_LOW_INDICATOR_DESIGN.md`: our own cycle-low indicator, designed from scratch.
- `docs/CYCLE_SCHOOL_PREREG.md`: the pre-registered tests of his method and of our design.

Nothing in this file was computed against prices. It is reading only.

**The three tags used below.**

- **Testable now**: the lab has the data, and the claim can be written as a fixed rule.
- **Needs data we lack**: the claim is a rule, but an input is missing. The missing input is named.
- **Not testable**: the claim is hand-drawn, relabelled after the fact, or has no number.

**One school only.** Nothing here is mixed with Market Cipher or with Tim's structure method
(Cody, 2026-10-18). Camel credits his cycle system to Bob Loukas, who he believes took it from
Charles Nenner. Walter Bressert's DSS is the oscillator he confirms with. Those three are the cycle
school, and only they appear here.

## 1. Sources

- **66 public daily videos, 2026-07-25 to 2026-10-17**, plus one public tutorial, "HOW TO TRADE
  CYCLES" (2023-08-30). They are Whisper transcripts in the research corpus,
  `AccessibleTrader.StrategyLab/Corpus/transcripts/camel-finance/` (on the corpus branch,
  worktree `agent-a008132e086022dc6`, while it is still being built). Every claim here was taken
  from the consolidated rules file `Corpus/rules/camel-finance.md` and its five part files, and
  the macro quotes were searched again in the transcripts themselves for this document.
- **The vendor's public pages for his indicator**: about 50 GitBook pages (current and the
  archived 1.0) and the cftrading.co.uk sales page. Marketing copy; every number in them is the
  vendor's claim.
- **The lab's own earlier work**: `docs/CAMEL_TRACK_RECORD.md` (August 2026 ledger, now committed),
  `docs/CAMEL_CYCLE_LIVE_READ.md`, `docs/CYCLE_FINDINGS.md`, `docs/TRANSLATION_FINDINGS.md`,
  `docs/ALPHA_LEDGER.md`.

**Not used.** The 16 members-only ("Level 3") videos were never requested: members get his exact
entries and stops, and those are almost never said on air. Four public dailies were not obtained
(29 Aug, 8 Sep, 25 Sep, 16 Oct). **No Bob Loukas material was obtained**: the one item in the
corpus manifest, a TradingView "60-Day Cycle Tracker (Bob Loukas Method)" page, returned 404. Bob
Loukas appears here only as Camel quotes him. The Bressert material is four secondary web pages,
not Bressert's own writing (`Corpus/rules/bressert-dss.md`).

**Transcription.** Whisper large-v3-turbo. Known mishearings are corrected in paraphrase only:
"Bresset" or "breast" is DSS Bressert, "Dixie" is DXY, "Campbell" is Camel.

## 2. The method in one paragraph

Camel trades **nested time cycles**: daily cycles inside weekly cycles inside each asset's
multi-year cycle. "Cycles predict time, not price." He projects a **timing window** for the next
low of each degree, waits for price to get into it, then waits for the low to be **confirmed on
that cycle's own timeframe** by a few price signals. He sets an **invalidation level in advance**,
puts a stop a little under the low sized to lose 2% of the account, and adds at each later higher
low. **Translation** (whether the cycle peaked before or after its midpoint) tells him the trend,
and a **higher-high close** is the gate he will not call a bull market without. Around all of that
sits a macro reading that is openly contrarian: the bond market is the truth, headline data is
narrative, the NBER dates recessions months late, and sentiment has to "match" a major low.

> "No one really cares about the price here. We're looking for the high probability low to manage
> the risk around." ([4 Aug, 04:11](https://youtu.be/JoRNW_4jCVc?t=251))

## 3. How he calls a low in real time

Cody's point is that the indicator paints a low only after the low has passed, and that Camel's
skill is calling it before that. The transcripts support the distinction. The real-time call is
built from five things, in this order.

### 3.1 He projects the window in advance and states it with a date

- The window is the asset's average low-to-low length, plus or minus 10%. "Average them all out at
  the end and then apply a 10% tolerance. That will give you your counts."
  ([18 Aug, 08:20](https://youtu.be/EKSjKkcIIqU?t=500))
- He says the date out loud before it arrives. On 16 Sep the next Bitcoin daily cycle low was due
  around 2 to 3 Oct; on 1 Oct it moved to about 15 Oct; on 7 and 17 Oct it was "around the 16th
  plus or minus 10 percent". ([16 Sep, 04:12](https://youtu.be/28wXrOWuVyA?t=252),
  [1 Oct, 10:48](https://youtu.be/fkncsQKAIp4?t=648),
  [17 Oct, 19:39](https://youtu.be/l4d4MOUG1Gg?t=1179))
- "Never buy on the window alone": "sometimes cycles will extend significantly beyond the timing
  window", especially "in very bearish conditions". ([2023, 11:03](https://youtu.be/XWTIrduvgrU?t=663))
- **Tag: testable now** as a filter (does a low found inside the window confirm more often?).
  **Not testable** as a cycle length: the lab has already shown the length is a property of the
  swing detector (section 9).

### 3.2 He sets price gates for the coming low before it prints

- 16 Sep, for the expected early-October low: a low "above about 70K" means a breakout is likely;
  "below about 65K" means "left trans and failure". ([16 Sep, 04:12](https://youtu.be/28wXrOWuVyA?t=252))
- 2 Oct: "low 70s, high 60s handle" is where he would be "smash buying".
  ([2 Oct, 07:30](https://youtu.be/MG8WI9O0Eyk?t=450))
- **Tag: testable now** only as his dated calls (they are listed for scoring in the rules file).
  A gate like "above 70K" is chosen by eye for one episode and cannot be written as a general rule.

### 3.3 Inside the window he acts on the earliest confirmations, before the indicator does

This is the core of the real-time skill. He takes a small position as soon as some of the
checklist (section 4) has fired, with the stop under the low, and does not wait for every signal or
for his indicator's label.

- "Take a shot to land a shot": "worst case scenario, you pay the 2% stop". "I would rather pay a
  2% stop loss here, see left translation and failure, head down here and buy heavy, than just
  assume that's what's coming, not get long, and then potentially get locked out."
  ([17 Sep, 20:30](https://youtu.be/ZlYblgqzJ2w?t=1230))
- On 15 Oct he set the trendline break "as our confirmation signal", said the Bressert was "almost
  done at the bottom", and that the indicator "should pick this imminently… it's through the window
  now". The indicator came last. ([15 Oct, 13:38](https://youtu.be/hhb_9awG53c?t=818))
- But "no trade on an unconfirmed low", "Always wait for the confirmation signals to fire", above
  all in FOMC weeks. ([16 Sep, 08:22](https://youtu.be/28wXrOWuVyA?t=502),
  [17 Sep, 18:24](https://youtu.be/ZlYblgqzJ2w?t=1104))

### 3.4 He checks that sentiment and the tape "match" a low

- "This is absolutely a pivot out of a capitulative low into a timing window on record bearish
  sentiment. And with media attention, that should be it." ([13 Oct, 08:22](https://youtu.be/qarjzOUTk8k?t=502))
- "We don't have to have sentiment extremes at inflection points, but typically for four year cycle
  lows, we do." Mocking and victory laps are "not how the lows occur".
  ([16 Sep, 05:12](https://youtu.be/28wXrOWuVyA?t=312), [21 Aug, 05:50](https://youtu.be/BHMj3gGjT-Q?t=350))
- Leaders should lead: the Dow led the July low, MSTR should lead Bitcoin bottoms, semis are "the
  canary". ([29 Jul, 01:33](https://youtu.be/nKpG-HTBRrQ?t=93),
  [23 Sep, 17:25](https://youtu.be/l8tdmwnJxSk?t=1045), [17 Aug, 04:48](https://youtu.be/Q_U-MbBmLdI?t=288))
- Cycles "act as heat-seeking missiles for over-leveraged players": a forced liquidation in the
  window is a tell. ([31 Jul, 02:33](https://youtu.be/bkf_ALW8W_A?t=153))
- **Tag: testable now** for the sentiment part, using AAII, Cboe put/call (to 2019) and the crypto
  Fear and Greed index. His own sentiment reads (his X feed, comments, polls) are **not testable**;
  he says himself the feed is "all noise and no signal" ([2 Sep, 07:00](https://youtu.be/DPhLPRVnliM?t=420)).
  Leader/laggard tells are testable for symbols we have (QQQ, IWM, DIA, the sector ETFs) and
  **need data we lack** for MSTR and the miners.

### 3.5 Then he lets the indicator confirm, and checks it agrees with him

- "The indicator will pick that low and upon confirmation, we should be long."
  ([27 Jul, 13:29](https://youtu.be/C3E_zU5a6c4?t=809))
- The indicator is slow by design: it is "trained to wait… Probably wants to see the open first";
  "the first big green candle will trigger the indicator to pick this".
  ([31 Jul, 07:21](https://youtu.be/bkf_ALW8W_A?t=441), [16 Sep, 07:21](https://youtu.be/28wXrOWuVyA?t=441))
- He uses it "only really… as a confirmation and confluence tool. As well as a future window
  prediction tool." ([18 Aug, 11:02](https://youtu.be/EKSjKkcIIqU?t=662))
- The vendor docs agree that the label comes after the fact: a confirmed low shows its icon "the
  moment all rules align", and "the Daily FCLR Bar only updates once the latest DCL is confirmed"
  (section 8).

**What this means for a test.** His real-time call is the checklist firing inside the window, ahead
of a slower, lagged confirmation. A test has to fix the checklist as a rule, fix a lagged
confirmation of the same lows, and measure how often and how far ahead the checklist gets there.
That is test (a) in the pre-registration.

## 4. The low-calling checklist

Each item is something he names on air. The list was never fixed: how many items he required, and
which, changed from video to video (section 4.3).

### 4.1 The items

**1. A swing low inside the timing window.** In 2023: "I like to use a swing low pattern or a trend
line break… you could also use a cross above the 10 moving average… or you can choose your own".
([2023, 10:33](https://youtu.be/XWTIrduvgrU?t=633)) Whether it is a higher or a lower low "doesn't
make a difference to me. I'm just looking to get inside of that window."
([5 Oct, 02:47](https://youtu.be/fMQyp-wqH8Y?t=167))
Tag: **testable now** (a pivot-low rule).

**2. A close back above the 10-period simple moving average** on the cycle's own timeframe. US100
on 31 Jul: "We're above the 10 SMA. We've got a confirmation… We're in."
([31 Jul, 08:22](https://youtu.be/bkf_ALW8W_A?t=502)) In 2023 he says the 10 MA is "what bob lucas
likes to use". Tag: **testable now**.

**3. A DSS Bressert bullish cross "down the bottom"** (out of oversold), on the daily, weekly or
monthly. "All we need is the indicator to pick the low or a [Bressert] cross down the bottom or a
trend line break. Any of those will do." ([17 Sep, 17:52](https://youtu.be/ZlYblgqzJ2w?t=1072))
A "divergent higher low… on the monthly" is stronger. **No oscillator lengths are ever spoken.** The
setting (10, 9, 5, 80/20) was read from a screen frame in August, not from speech. The phrase
"bracketing cross", used in `CAMEL_TRACK_RECORD.md`, is not his: it appears in none of the 67
transcripts. Tag: **testable now** (the terminal has `DssBressertProvider`).

**4. A trendline break.** "Always want a trendline break." ([16 Sep, 08:22](https://youtu.be/28wXrOWuVyA?t=502))
The line is hand-drawn. Tag: **testable now only as a proxy** (a line through the last two lower
pivot highs, or a close above recent highs). The hand-drawn line itself is **not testable**.

**5. A big green thrust candle** off the low. On TLT's weekly list, "and most importantly, a big
green weekly candle". ([31 Aug, 05:19](https://youtu.be/-HDKylerUWo?t=319)) Tag: **testable now**
(range against ATR, close near the high).

**6. His indicator "picks" the low.** "I want my indicator to pick me a low." ([16 Sep, 08:22](https://youtu.be/28wXrOWuVyA?t=502))
Tag: **not testable**. It is closed source; section 8 lists what is known about it.

**7. Secondary items**: a Parabolic SAR flip "helps" (31 Aug), and the TSI is an alternative
oscillator (31 Aug). Tag: **testable now** (PSAR); low priority.

**8. The close that counts is on the cycle's own timeframe**: daily close for a daily low, weekly
for a weekly low, monthly for a 4-year low. "It's the weekly close that matters. And more
importantly than the weekly closes, it's the monthly." ([25 Aug, 11:30](https://youtu.be/_freGHQfGEc?t=690))
Tag: **testable now** (completed bars only).

### 4.2 The veto over all of it: the higher-high close

The checklist can be complete and he still will not call a bull market without a close above the
prior swing high.

- "It is malpractice to be bearish above this line, but it is malpractice to be bullish whilst we
  are below it." ([27 Jul, 08:45](https://youtu.be/C3E_zU5a6c4?t=525))
- Wicks do not count: "poking above it and then coming back down obviously doesn't count";
  "a break and multiple closes". ([26 Aug, 03:05](https://youtu.be/c46MwOOnips?t=185),
  [27 Aug, 02:39](https://youtu.be/RevmcdyF-5s?t=159))
- On 25 Aug Bitcoin passed almost every item (weekly Bressert cross, trendline break, "the big green
  up thrust", the indicator's label, above the 10 SMA) and he stayed bearish: "The only thing we're
  really missing, again, is that higher high." ([25 Aug, 13:37](https://youtu.be/_freGHQfGEc?t=817))
- It fired on 22 Sep and he flipped: "Invalidations exist for a reason. And now it's time to flip
  bullish." ([22 Sep, 13:02](https://youtu.be/n3R4fE6denY?t=782))
- The rules file is careful about scope: the higher-high close is the master gate for calling a
  *bull market* or a bigger-degree low. It is not an item he needs for a daily-cycle trade.
- His later "80% rule" (13 Oct) is the same idea made into a number: if a confirmed daily low near
  a candidate 4-year low goes on to make new highs, "about 80% probability the four-year cycle low
  is in"; if it left-translates and closes below the confirmed low, 80% that it is not.
  ([13 Oct, 06:18](https://youtu.be/qarjzOUTk8k?t=378))

Tag: **testable now**, at the weekly degree, as a lagged confirmation of bigger lows (the 80% rule
is a direct claim with a number).

### 4.3 How many items he required, by date

| Date | What he asked for |
|---|---|
| 2023 | Any one of swing low, trendline break, 10-MA cross. |
| 28 Jul 2026 | "Two out of six confirmation signals" was not enough to size up. |
| 29 Jul | Swing, trendline close, Bressert cross and the indicator's label: "medium confluence". |
| 16 Sep | All three: trendline break, indicator low, Bressert cross. |
| 17 Sep | Any one: indicator low, Bressert cross or trendline break. "Any of those will do." |
| 13 Oct | Gold: a pivot, the Bressert cross at the bottom, a trendline break. |

Citations for each row are in `Corpus/rules/camel-finance.md`, section "Confirmations". The six
items of July cannot be identified exactly from the audio; the best reading is swing low, 10 SMA,
trendline, Bressert cross, indicator low and thrust candle. The pre-registration therefore tests a
primary rule (a swing low plus at least three of the four computable items) and his two other
wordings as named variants, with the count fixed in advance.

### 4.4 Invalidation, written in advance

- "A confirmed cycle low becomes the invalidation." "Price has no business trading below that low if
  it's going to remain in bullish structure." ([15 Aug, 09:33](https://youtu.be/9-ISGikTF4Y?t=573))
  In 2023: "that becomes the line in the sand". ([2023, 15:54](https://youtu.be/XWTIrduvgrU?t=954))
- Failure means a close below the cycle low, in his 2026 wording ("closing below the cycle low",
  18 Aug; "closes below wherever this cycle low confirms", 13 Oct). In 2023 a break was enough, and
  on 2 Sep he called gold's cycle failed "on a wicking basis". So close-versus-wick is not settled.
- The stop goes "a little bit below the cycle low… You don't want it in line with the cycle low
  because occasionally you get double bottoms or even quick sweeps". No offset is given ("my eye
  pretty much works"). ([18 Aug, 01:06](https://youtu.be/EKSjKkcIIqU?t=66))
- **The record on this is mixed, and the lab's August ledger found the reason.** Where he had
  written a level down (Bitcoin's 83K, gold's trendline) he acted on it. Where he had not (oil) he
  repeated "no changes" for four weeks while oil rose 20%. Twice he moved an invalidation after the
  fact (Bitcoin softened on 14 Sep; the 10-year moved on 1 Oct). (`CAMEL_TRACK_RECORD.md` section 4;
  rules file "Ledger corrections" 16.)

Tag: **testable now** for "close below the confirmed low" as the stop rule. The levels themselves
are hand-chosen and **not testable** as a general rule.

## 5. Cycle timing

### 5.1 Lengths he states

| Asset and degree | Number | When said |
|---|---|---|
| Bitcoin daily | 60 days ±10%, so 54 to 66 (calendar days) | 2023 and throughout 2026 |
| Bitcoin weekly | 24 to 28 weeks (2023); "between 28 and 32 weeks, with an average of about 30 or 31" (4 Aug 2026) | |
| Bitcoin 4-year | top in month 35; low "116 to 165 days away" on 25 Aug | |
| S&P daily | 40 trading days ±10%, so 36 to 44; half-cycle low about day 20 | 2023, Oct 2026 |
| S&P weekly | 22 to 26 weeks | 2023 |
| Gold daily | 22 to 28 days, average 25 to 26 | Aug 2026 |
| Gold weekly | week 22 to 28, "a week 26 on average" | Sep and Oct 2026 |
| TLT | 7.5-year cycle; "the last cycle was 93 months. And right now we are on month 93" | 24 Aug |
| DXY | 3-year cycle | |
| 4-year and yearly lows | tolerance "plus or minus one weekly cycle" | from 15 Aug |

Citations are in the rules file's cycle-length table. Bitcoin is counted in calendar days and
equities in trading days ("This is weekend candles. Let me go back here", 1 Oct, 12:26).

### 5.2 Rules about the timing

- **In-window rate**: lows fall inside the window "80% of the time" (2023); "70 to 80% of all lows…
  depending on the asset class" (10 Sep 2026). ([2023, 05:16](https://youtu.be/XWTIrduvgrU?t=316),
  [10 Sep, 15:00](https://youtu.be/cp7MHZZBFW8?t=900))
- **Nesting**: "three four five daily cycles" make a weekly cycle (2023); "three and then an
  inversion" (1 Aug). He rejected an October 2026 4-year low for Bitcoin because "having two dailies
  inside of a weekly just doesn't really fit". ([1 Aug, 17:57](https://youtu.be/L3QWk203NR4?t=1077))
- **Inversions** mean seller exhaustion: "Inversions only happen when we exhaust the sellers."
  ([22 Aug, 12:41](https://youtu.be/8ZbQVOShDco?t=761))
- **Failure cascades up**: two failed daily cycles in a row imply a failed weekly cycle, which means
  "seeking a four-year cycle low at the next weekly cycle". ([3 Oct, 20:08](https://youtu.be/IYfTEZtA2f8?t=1208))
- **Synchrony**: "When you get syncing of cycles, it's normally a very powerful indicator of what is
  to come." ([28 Sep, 03:51](https://youtu.be/JBHwqCwJma8?t=231))

**Tags.** The in-window rate and nesting counts are **already falsified** as stated: shuffled-return
surrogates give the same cycle length and land in the band as often as real prices
(`docs/CYCLE_FINDINGS.md`). They are not re-tested. The window is kept only as a **filter** in the
pre-registration (does a checklist call inside the window confirm more often than one outside it).
Inversions and relabelled counts are **not testable**: "with the new data presented… we'll be able
to then reassess this count" (4 Aug). Failure cascades and synchrony are close to the dead ground
and are not registered.

## 6. Translation

- Measure low to low, mark the midpoint. A peak left of it is left-translated (bearish), right of it
  right-translated (bullish), exactly in the middle "neutral or leaning towards a bullish" cycle.
  ([2023, 07:55](https://youtu.be/XWTIrduvgrU?t=475), [28 Aug, 02:01](https://youtu.be/CgNaNridcaY?t=121))
- A weekly cycle typically has right-translated dailies and then a left-translated one that fails
  into the weekly low. ([2023, 06:21](https://youtu.be/XWTIrduvgrU?t=381))
- An extremely right-translated cycle has to "puke into the low" and correct "hard and fast".
  ([4 Aug, 08:53](https://youtu.be/JoRNW_4jCVc?t=533))

**Tags.** Translation as a direction signal is **already measured**: it is momentum in cycle
vocabulary (`docs/CYCLE_FINDINGS.md`). "Right translation means a faster decline" is **falsified**:
plain trailing return reproduces 98% of the effect and a third of shuffled surrogates match it
(`docs/TRANSLATION_FINDINGS.md`). Neither is re-tested here.

## 7. The macro framework

This is the part Cody says takes "a little thinking". Read across the 66 videos, it has one idea
underneath it: **the official headline numbers are narrative, and markets (above all the bond
market) are the truth.** Each piece below is that idea applied to one area.

### 7.1 "Tell that to the bond market" (he also calls it "Camel's razor")

What he means by it is narrower than it sounds. It is mostly a rule about the Fed:

- "It is the bond market in aggregate that sets rates, not the Fed. The Fed only changes the
  interbank lending rate." ([11 Sep, 12:43](https://youtu.be/V55qMPLctPE?t=763))
- "We don't have to try to guess what the fed is going to do… because it's the bond market in
  aggregate that sets rates… if this thing remains above 85 then we can say with as much confidence
  as one could ever have in markets that we will be seeing a rate hike." "This thing" is the CME
  FedWatch probability. ([12 Sep, 15:28](https://youtu.be/ZpYL6r2JzuY?t=928))
- "Wherever the two year yields go or wherever the bond market goes, really, the Fed funds rate
  tends to follow." ([15 Sep, 01:54](https://youtu.be/wqa0EcxKOYY?t=114))
- He applied it against his own view: the hike he did not expect came on 16 Sep, and "tell that to
  the bond market, right? That's the whole point of this thing." He does not listen to FOMC press
  conferences: "I'm not really interested in the rhetoric." ([17 Sep, 01:05](https://youtu.be/ZlYblgqzJ2w?t=65),
  [17 Sep, 02:08](https://youtu.be/ZlYblgqzJ2w?t=128))

Tag: the 85% rule **needs data we lack** (historical fed funds futures probabilities). The broader
claim, that short-dated Treasury yields lead the Fed's decision, is **testable now** with the
3-month and 2-year yields, the target series and the FOMC dates. Note that it is close to true by
definition for a 3-month bill, so the cheap alternative it has to beat is "the Fed's next move is
the same as its last".

### 7.2 Bonds as the truth against the headline data

The stronger form of the same idea: when the bond market and a data release disagree, believe the
bond market.

- "The bond market knows inflation is not as sticky as they're letting on, the labor market is a
  lot weaker than they're letting on, especially when you account for the downward revisions they
  keep doing, and thus this interest rate policy is likely too restrictive."
  ([17 Oct, 13:17](https://youtu.be/l4d4MOUG1Gg?t=797))
- "The bond market is finally working this out because CPI comes in hot and yields went down, not
  up." ([15 Oct, 04:09](https://youtu.be/hhb_9awG53c?t=249))
- "The bond market is telling you for a reason, okay? Why does it want to have the interbank lending
  rate so far below the 10-year? It's because it knows the 10-year is about to reverse."
  ([3 Oct, 12:55](https://youtu.be/IYfTEZtA2f8?t=775))
- He also reads the bond market's own capitulation: "The bond market has been capitulating. Yields
  have been forced higher… the Fed had to respond. It was basically being held hostage by the bond
  market." ([3 Oct, 11:24](https://youtu.be/IYfTEZtA2f8?t=684))

Tag: **testable now** as a release-day rule. A strong payrolls first print on a day the 2-year
yield falls, or a hot CPI first print on a day the 10-year falls, is a "bond market disagrees" day.
His thesis predicts what follows (weaker stocks after the jobs case; lower yields after the CPI
case). Both are in the pre-registration (tests C1 and C2). **Needs data we lack** for "strong
relative to expectations": the lab has no consensus forecasts, so "strong" has to mean strong
against the recent trend of first prints.

### 7.3 Headline data, revisions, and "not meaning what the system wants you to think"

- "It's very easy to get psyoped by the headline data. But if you look at the overall trend… the
  labor market has been undergoing consistent deterioration." ([25 Jul, 07:30](https://youtu.be/Mm5NUZElP-Y?t=450))
- The revisions, which he repeats in nearly every macro segment: "911,000 jobs revised out… in
  September of 25", "over a million jobs for 2025", "In total, 2.1 million jobs have been revised
  out of the initially reported data over the past three years."
  ([8 Aug, 08:05](https://youtu.be/vSroUWCVwtU?t=485))
- "The 12-month average of the first print suggested we added 900k jobs. But they have currently
  published 273k… So just like inflation, this too is a narrative that is being orchestrated by
  some people at top that want the majority of the masses to just read the headline data and move
  on happily." ([29 Aug, 04:17](https://youtu.be/48gjRRnmyY0?t=257)) By 17 Oct he gave the same
  episode as "plus 900k jobs added into negative 627k". ([17 Oct, 09:01](https://youtu.be/l4d4MOUG1Gg?t=541))
- He admits the motive is speculation: "it could be that this is just the way they do it… but i
  being a more paranoid…" ([12 Sep, 13:22](https://youtu.be/ZpYL6r2JzuY?t=802))
- Inflation is read the same way: real-time measures (Truflation, a CPI without shelter he calls
  "ALNRI", core PCE on 3- and 6-month averages) instead of the headline. ([15 Oct, 03:04](https://youtu.be/hhb_9awG53c?t=184),
  [3 Oct, 05:49](https://youtu.be/IYfTEZtA2f8?t=349))
- The leading labour data he shows instead of the headline: quits (total and construction), JOLTS,
  temporary help services, the hiring rate, the Sahm rule, unemployment "lead-in before a pop".
  ([25 Jul, 08:05](https://youtu.be/Mm5NUZElP-Y?t=485), [1 Aug, 08:15](https://youtu.be/L3QWk203NR4?t=495))

Tags. **Testable now**: whether runs of downward payroll revisions (first print against later
vintages, from ALFRED) come before equity drawdowns (test C3). Whether first prints are biased up
on average is also computable from the vintages, but it is not a market claim and is not
registered. **Not testable**: that the revisions are deliberate. **Needs data we lack**: quits,
JOLTS, temporary help and Truflation are not in the lab (JOLTS and temporary help are on FRED and
could be added in a later, separately registered fetch).

### 7.4 The NBER is always late

- "The NBER that is in charge of dating the recessions always does so well after the fact."
  ([25 Jul, 06:57](https://youtu.be/Mm5NUZElP-Y?t=417))
- His expected sequence: a blow-off top, a fast fall into the 4-year low, a short spell of asset
  deflation, and only then "the NBER would go back and paint those gray recessionary bars over the
  pullback". ([17 Oct, 01:36](https://youtu.be/l4d4MOUG1Gg?t=96), [1 Aug, 07:45](https://youtu.be/L3QWk203NR4?t=465))
- He uses the lag as a reason to ignore "no recession" headlines: the leading data "looks
  recessionary", and "the only thing this chart is really missing is one of these gray recessionary
  bars". ([22 Aug, 06:50](https://youtu.be/8ZbQVOShDco?t=410))

Tag. The lag itself is a fact: the lab has NBER's announcement dates (`macro/nber/`), with lags of
4 to 21 months since 1980 (`docs/MACRO_DATA.md`). "The market low comes before the NBER says
recession" is **testable only descriptively**: the lab's long equity series (SPY) starts in 1993,
which leaves three peak announcements and three trough announcements. That is too few for a test
(the pre-registration lists it as a descriptive table, C4). A statistical test **needs data we
lack**: a daily S&P 500 index back to at least 1950.

### 7.5 Liquidity

- Stealth QE: "the Fed has been printing more money to buy treasuries to the tune of around 300
  billion in the last year… It should indeed put a bottom in the bond market."
  ([27 Jul, 09:47](https://youtu.be/C3E_zU5a6c4?t=587))
- Treasury buybacks: "they're essentially doing yield curve control here… It's also akin to QE at
  the margin." ([20 Aug, 03:12](https://youtu.be/_Gn7BpBvA44?t=192)) The Fed buying bills "is not
  QE. It's more like routine balance sheet management." ([15 Sep, 05:38](https://youtu.be/wqa0EcxKOYY?t=338))
- Money supply: "anything you do… to the M2 rate of change shows up in the CPI with a lag", the
  basis of his disinflation and short asset-deflation thesis. Global money supply growth
  "recovering from 7% to 9%" is "good news for gold". ([25 Jul, 05:25](https://youtu.be/Mm5NUZElP-Y?t=325),
  [10 Aug, 03:10](https://youtu.be/MMnkrbLKZRc?t=190))

Tag. He never uses a net-liquidity number (Fed assets minus the Treasury account minus reverse
repo). Our design adds one, from WALCL, WTREGEN and RRPONTSYD (**testable now**). "M2 growth shows
up in CPI with a lag" is **testable now** from the ALFRED vintages, but it is a macro-to-macro
claim with few independent points, and it is not registered. Global money supply **needs data we
lack**.

### 7.6 The alarm gate (when to stop buying dips)

The order, spelled out on 26 Sep: "failed daily cycles in stocks first, then trendline breakdowns,
and only then the alarm signals". The alarms are a high-yield credit-spread blow-out and an
"accelerative sweep to the upside" in the 10y−3m and 10y−2y curves. An alarm alone is not a veto:
the high-yield breakout of early October was "not occurring on trendline breakdowns or failed
cycles for the stocks". ([26 Sep, 14:34](https://youtu.be/_gS-hSL_vOI?t=874),
[3 Oct, 13:57](https://youtu.be/IYfTEZtA2f8?t=837))

Tag. **Testable now** with one substitution: ICE high-yield spreads exist in the lab only from
2023-10-17, so Moody's BAA minus the 10-year (investment grade, from 1986) stands in, and any finding
must say so (test C6). The gate is about tops and exits, not lows; it is registered because it is
the centre of his macro framework.

### 7.7 Order of tops between markets

"Yields top first, then stocks about three to four months later, then commodities" (2000 and 2007).
A yield top "sets off the countdown timer for the final bull leg". ([2 Oct, 02:41](https://youtu.be/MG8WI9O0Eyk?t=161),
[5 Oct, 01:35](https://youtu.be/fMQyp-wqH8Y?t=95))
Tag: **not testable** as a statistic. There are three or four episodes, and dating a "top" in
yields needs a cycle-top rule that would be chosen by looking back.

### 7.8 Contrarian reads

- He names himself the contrarian: "I am still here to be the contrarian in the room" (rate hike
  odds, 29 Aug); TLT is "super contrarian. Lots of people don't think this is possible" (13 Oct).
  ([29 Aug, 05:20](https://youtu.be/48gjRRnmyY0?t=320), [13 Oct, 07:52](https://youtu.be/qarjzOUTk8k?t=472))
- "Every man and his dog" being a bond expert, and "a bearish sentiment extreme right into a
  capitulative moment", is his read of the bond low. ([1 Oct, 00:33](https://youtu.be/fkncsQKAIp4?t=33))
- Named sentiment and positioning reads (none of them his own data): AAII bears at 53% ("55 to 60
  would be considered extremely bearish; the GFC low only had 70%"), record asset-manager Nasdaq
  futures shorts as squeeze fuel, money-market fund flows "synonymous with the lows", the share of
  stocks with RSI below 30. ([18 Sep, 01:02](https://youtu.be/NUaGWQQTN5Y?t=62),
  [19 Aug, 02:30](https://youtu.be/O2e2PpZn6vA?t=150), [29 Sep, 06:18](https://youtu.be/0zlHZQzW28Q?t=378))

Tag. **Testable now**: AAII extremes, the put/call ratio to 2019, Fear and Greed, the COT
positioning the lab has, and a "share of RSI below 30" breadth measure built from the lab's own
sector ETFs. **Needs data we lack**: money-market flows, options skew, the CFTC Traders in
Financial Futures report for asset-manager positioning, constituent-level breadth.

### 7.9 Narrative follows cycles

"Cycles are in control. Everything else is narrative." "Show me the charts and I'll tell you the
news." The size of the story scales with the degree of the low: a tweet for a daily low, a surprise
print or hike for a weekly low, "the sky is falling" for a multi-year low. Earnings and news are
not vetoes. ([11 Aug, 04:57](https://youtu.be/UcAX4VpRTfg?t=297), [6 Oct, 01:39](https://youtu.be/un3YVNZSEVQ?t=99))
Tag: **not testable**. Any news fits, with no asset and no level.

### 7.10 How the pieces fit

Putting it together as he uses it:

1. **The cycle clock says when a low is due.** Macro never overrides the clock.
2. **The bond market says whether the backdrop agrees.** When yields fall against strong headline
   data, he reads it as the market seeing weakness the data hides.
3. **The headline data is discounted** for revisions, and the recession call is made from leading
   labour data, knowing the NBER will confirm it late.
4. **The alarm gate decides whether dips are bought at all**, and it only counts on top of failed
   cycles and broken trendlines.
5. **Sentiment has to match the degree of the low.** A 4-year low should come with fear and
   capitulation.

Only steps 2, 4 and 5 can be turned into rules here. Step 1's clock is the falsified part; step 3 is
partly opinion.

## 8. What his indicator computes, from the sources

The vendor calls it the **CF Cycle Trading Indicator**, a TradingView script built "between Camel
Finance and the developers at Variance Technologies". "Cipher C" appears in neither the videos nor
the vendor's pages; it is the lab's own name for the clone target. (On 2026-10-21 `CipherCProvider` was rebuilt as that clone;
it had been an unrelated Ehlers cycle oscillator. See `docs/CIPHER_C_DESIGN.md`.)

**What it outputs**, per the vendor's docs (rules file, section "What his indicator computes"):

- Daily, weekly and yearly cycle-low labels. A daily low has four states: confirmed ✅ ("the moment
  all rules align"), unconfirmed 🕓 (provisional), cancelled ❌ ("when a DCL breaks down
  post-trigger") and updated 🔄 (a new low replaces an invalidated one). A ⚠️ pending alert gives an
  "early heads-up that a DCL may be forming soon" on fine-tuned assets.
- A High, Mid or Low "confluence" grade on daily lows from three components: "Breakout
  Confirmation", "Momentum Signals" and "Supporting Indicators". Their definitions and weights are
  not published.
- A projected "Future Cycle Low Range" box, which "only updates once the latest DCL is confirmed".
- Translation labels (left, mid, right), cycle midpoints, cycle counts, a table of cycle lengths for
  16 "fine-tuned" assets (the numbers are not published).
- A US macro data table (CPI, unemployment, fed funds and more, with a red or green change from the
  last release) and a "CF Bullish Meter", a rough "sentiment dial" that is "not a signal".

**The only settings described** are a cycle-length adjustment factor, fast and slow EMA adjustment
factors, overbought and oversold "trend thresholds", and a "Faster Confirmation" toggle. No default
is published. The reading in the rules file is that the engine keeps an up or down trend state from
two EMAs of some series, gated by thresholds, combined with an expected cycle length.

**How it is tuned**: "pre-optimised cycle templates" per asset, built from "tens of thousands of
different settings" in backtests ([8 Oct, 01:25](https://youtu.be/xbRQulPGwkQ?t=85)). The vendor's
own backtest buys "every single one of the flashes the indicator produces without waiting for any
confirmation signals", optimised over "millions and millions of combinations", and shows no edge on
BNB and an edge on GDXJ. It was not public as of 17 Oct. The docs claim 70 to 80% accuracy with no
method and no out-of-sample split.

**The "90+ metrics" claim.** Cody says the indicator claims to use more than 90 metrics to form its
cycle prints. **That claim appears in none of the public sources the lab holds**: not in the 67
transcripts, the vendor's GitBook pages or the sales page (all searched for "90", "ninety",
"metrics", "data point" and "inputs" on 2026-10-18). The nearest public facts are the macro
dashboard (FRED series: S&P 500, the broad dollar, the 10-year, the Fed balance sheet, M2, VIX), the
macro table inside the indicator, and the per-asset optimisation over thousands of settings. If the
claim exists, it is in member material or a source the lab has not seen. It is treated as
unverified.

**What none of the sources say**: the detection formula, the pivot definition, the EMA lengths and
what they smooth, the threshold values, the base cycle lengths, the rules behind ✅, the trigger for
⚠️, the confluence components, how weekly and yearly lows are chosen, and any out-of-sample result.
DSS Bressert is a separate tool on his chart ("we have got a Bresset cross and we have got the
indicator", 29 Jul, 02:03).

**Why this matters for our design.** A clone would have to guess every one of those numbers, and an
indicator tuned over thousands of settings per asset cannot be checked by its own backtest. That is
why `CYCLE_LOW_INDICATOR_DESIGN.md` does not clone it. It builds something with the same outputs
(pending, confirmed, cancelled, a grade) from rules fixed before any test, with no fitted weights.

## 9. What the lab already knows, and what that rules out

- **Cycle counts are a detector artifact.** Any pivot rule gives regular lows on a shuffled random
  walk, at a spacing set by its own span; the real data landed in the claimed band 10 to 17% of the
  time, not 80% (`docs/CYCLE_FINDINGS.md`, ledger row 15). So **no test may score a cycle length, a
  spacing or an in-window rate.** Only timing relative to lows confirmed by a fixed rule is scored.
- **Translation is momentum**, and "right translation means a harder fall" is falsified
  (`docs/TRANSLATION_FINDINGS.md`).
- **On-chain valuation (MVRV, NVT) is exposure to the bottom, not timing** (ledger row 10), the
  **crowding index is a null** (row 12), **macro release days are a null** for returns on the day
  (row 13), **Fear and Greed is redundant** with an oscillator buy (row 18), and **COT positioning is
  a null** as a predictor of returns (row 11). These were tests of different questions (returns
  after a level), but they set a low prior for sentiment and positioning as timing tools.
- **The FOMC pre-announcement drift is real** but mostly arbitraged since 2015 (row 7).
- **The real Camel claim that was never tested** is the risk-placement one: that a stop under a
  low found this way is hit less often (`CAMEL_CYCLE_LIVE_READ.md`). The pre-registration's
  trading test (d) is the closest it gets.
- **His August record** (`CAMEL_TRACK_RECORD.md`): the higher-high veto decided his Bitcoin month;
  the regime gate stayed green and buying equity dips worked; the discipline held exactly where a
  level had been written down and failed where it had not.

## 10. Summary of what is testable

| Claim | Tag | Where |
|---|---|---|
| The checklist calls lows before a lagged confirmation | Testable now | prereg (a) |
| The timing window improves the checklist | Testable now (as a filter) | prereg (a) |
| His other checklist wordings ("any one", "all three") | Testable now | prereg (a) |
| The 80% rule (new highs after the low mean the bigger low is in) | Testable now | prereg (a) |
| Sentiment matches major lows | Testable now (AAII, Fear and Greed) | prereg (b) |
| Bond market disagrees with a strong jobs print | Testable now | prereg (c) C1 |
| Hot CPI with falling yields means yields keep falling | Testable now | prereg (c) C2 |
| Downward payroll revisions come before drawdowns | Testable now | prereg (c) C3 |
| The market low comes before the NBER says recession | Descriptive only (6 events since 1993) | prereg (c) C4 |
| Short-dated yields lead the Fed | Testable now (proxy for FedWatch) | prereg (c) C5 |
| The alarm gate (failed cycles, breakdowns, credit and curve) | Testable now with BAA in place of high yield | prereg (c) C6 |
| FedWatch above 85% means the Fed does it | Needs fed funds futures history | not registered |
| Yields top, then stocks, then commodities | Not testable (3 to 4 episodes) | not registered |
| Cycle lengths, in-window rates, nesting counts | Already falsified | not re-tested |
| Translation | Already measured or falsified | not re-tested |
| The indicator's labels | Not testable (closed source) | not registered |
| Revisions are deliberate; narrative follows cycles | Not testable | not registered |
