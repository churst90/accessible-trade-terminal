# Trading schools: what each school's traders share, and where they disagree

This file consolidates the per-trader rules files in this folder into one method per school. The schools are kept separate on purpose, and nothing is combined across them.

## How to read this file

Each school has the same parts, in this order.

1. The method in its own terms, in one paragraph.
2. Shared rules, written as the complete method in order: context, setup (location), trigger, confirmation, vetoes, stop, exits, sizing, then timeframes and indicator settings.
3. Where they disagree.
4. Candidate rules not in the pre-registered setup grader.

Every rule names the traders who hold it. Citations use the source's own format: a file name plus date and mm:ss, for example "cryptoface, Intro to Market Cipher, 2019-06-17, 20:04". The cosasverdes file also uses "tutorial N, mm:ss". Quotes are kept to a sentence or two. Everything else is paraphrase.

Where a rules file says nothing, this file says "nothing stated". It does not fill the gap.

"Stated" means the trader says it in words. "Inferred from a worked example" means the trader never says it, but the example on screen implies it.

The frozen setup grader (docs/SETUP_GRADER_PREREG.md) is the reference for "not in the grader". For stage 1 it uses:

- Trigger: WT1 crosses above WT2 with WT2 at or below -60, entry at the next bar's open.
- Context: the weekly body-trend and the weekly EMA10, EMA21 and SMA10 phase.
- Location: within 1.0 ATR of a weekly pivot body level or the weekly EMA10.
- Confirmations: D1 divergence within 5 bars, D2 daily Money Flow rising two bars, D3 weekly Money Flow rising.
- Vetoes: V1 context bad, V2 daily Money Flow below zero and falling, V3 weekly Money Flow below zero and falling.
- Stop: lowest low of the last 5 bars minus 0.25 ATR, floor 0.5 ATR.
- Exits X1 to X5: ATR ladder, stop-based ladder, WT bear cross, sell-setup exit, 20-bar time exit.

Standing nulls in this repo: the Camel and Lucas cycle counts are a detector artifact; cosasverdes origin lines and equidistant lines failed surrogate tests; price-derived confluence does not stack; "cycles as a low detector" and "translation as direction" are dead; fixed-R and ATR exits are on the dead list. Candidates below do not propose changing the frozen grader. They are new, separately pre-registered tests.

Schools:

| School | Traders | Rules file(s) |
|---|---|---|
| structure | Tim (cosasverdes) | cosasverdes.md |
| market-cipher | many, listed in school 2 | cryptoface.md, market-cipher.md, jason-casper.md and 13 more |
| cycle | Camel Finance, Bressert DSS sources, LikesMoney | camel-finance.md, bressert-dss.md, likesmoney.md |
| other | none currently | none |

---

# School 1: structure (Tim, cosasverdes)

One trader, so "shared" here means the rules he holds consistently across tutorials and the later 2021 to 2024 videos.

## Method in its own terms

Tim reads price as a stack of regimes and levels. The weekly EMA10 and EMA21 (and the weekly SMA10) say which phase the market is in: bull, warning or bear. Inside a phase, candle-body highs and lows define the trend, and horizontal levels drawn from the weekly down to the micro scale say where price will react. He buys at a level in a bull phase, sells in tranches at the next level, and buys back after a correction whose depth he sizes from the size of the run. The trend changes only on a close. Indicators are secondary; the trend outranks them.

## Shared rules, in method order

### Context

- Weekly EMA10 above EMA21 is bull. SMA10 above EMA10 is the warning phase (sell half). EMA10 below EMA21 is bear (sell the rest). Weekly SMA20/21 above EMA21 is the "big nasty box". [cosasverdes tutorial 4, 12:16 to 17:33; tutorial 5, 11:53 to 12:56]
- Body trend: a higher local high and a higher local low, taken from candle bodies. It changes only on a close. A daily uptrend breaks at the first close below the higher low. [tutorial 3, 15:13]
- Altcoin context: the trigger to buy an altcoin is the BTC daily close confirming the uptrend. No altcoin buys while the BTC daily trend is broken. [tutorial 3, 22:11 and 27:00]
- Weekly EMA10 acts as a magnet, with three bear reactions described. He counts "respect" of the weekly 10: 14 weeks in 2019, 0 in 2023. A reclaim needs 3 to 4 daily closes above it. [Part 2 additions, cosasverdes]
- A weekly close below the weekly low led to more downside 8 of 9 times ("86%"); it failed in late 2023. [$42K Target, 2024-06-27, 05:48]
- Daily RSI at 80 means continuation, not a top. [$47K in November, 2023-11-01, 10:17]
- A red flip of the 2-day EMA10/21 cloud marks the top. [Cheat Code, 2024-05-10, 04:13]
- A monthly-open gate, dominance and FOMC context are also used. The rules file lists them without a numeric test.

### Setup (location)

- Weekly horizontal levels. The strongest are where price "turns on a dime", then the ones with the most connections. [tutorial 2, 00:30 and 02:34]
- Micro-candle scoring at a level (tutorial 8): pass-through scores 0, pierce and reclaim scores 1, pure rejection scores 2.
- Daily 10, 21 and 55 EMA dip test. [1 SIMPLE TRICK, 2021-11-17, 03:03]
- Punch-high and punch-low at extremes. [CRASH to $60,000, 2024-06-14, 04:37]
- Three drives down plus higher lows on RSI is the only knife-catch he allows. A 4H EMA10/21 bear-cross short is allowed only when the daily EMA10 is above the EMA55.

### Trigger

- Buy at a weekly level in a bull phase once the body trend on the trading timeframe turns up on a close. For altcoins, the BTC daily close confirming the uptrend is the trigger (see context). Nothing numeric beyond that is stated.

### Confirmation

- Closes, not intrabar moves, change the trend (tutorial 3, 15:13). In tutorial 8 micro reactions at the level score the quality of the reaction.

### Vetoes

- No buys in the warning or bear phase.
- Sell a line at most twice.
- No shorts in a bull market.
- No altcoin buys while the BTC daily trend is broken.

### Stop

- Structural only. A break of the level or the higher low. No percent or ATR stop is stated.

### Exits

- Sell in tranches: 20% (4 x 5%) or 10% (5 x 2%) on a second or weaker line.
- The Batman sign, a close below the last swing low's box, is used as an exit. [tutorial 8]
- Buyback depth is about 10% of the run: a 200% run means a 20% correction, an 800% run means 50%. After a second sale use 18%. With no consolidation use 26 to 30%.

### Sizing

- 50/50 split between large and mid-small coins, at most 15 coins.
- Futures at most 25% of capital (5% for newcomers), leverage 4 to 5x at most.

### Timeframes and indicator settings

- Weekly for regime, daily for trend and trigger, 4H and below for micro reaction. The 2024 set is 4H, daily and monthly. [The three time frames, 2024-04-11, 14:05]
- Weekly EMA10, EMA21, SMA10, SMA20/21. Daily EMA10, 21, 55 (and 70 in 2023). Daily RSI. 2-day EMA10/21 cloud. A tutorial 1 passage is ambiguous between a MA20 and a "21 ma" in the audio.

## Where they disagree (Tim against Tim)

| Topic | Earlier view | Later view |
|---|---|---|
| Buying in a falling market | 2021: "buy a little of the blood" [ORIGIN LINES, 2021-03-08, 17:26] | Tutorial 5: buy nothing until the weekly EMAs flip |
| Closes against micro reactions | Tutorial 8: micro reactions outrank closes | 2024: 4H, daily and monthly closes work [The three time frames, 2024-04-11, 14:05] |
| Daily stand-in for the weekly 10 | 55 EMA in 2021 [1 SIMPLE TRICK, 15:48] | 70 EMA in 2023 [$21,300 Mid Summer, 25:43] |
| Timeframe set | Earlier sets include the weekly | The 2024 set drops the weekly |
| Trend against indicators | Trend outranks indicators | Exception inside a yellow triangle |

## Candidate rules not in the pre-registered setup grader

Each is a separate test. Origin lines, channels and equidistant lines are dead (failed surrogates) and are not candidates.

1. Deep-bear veto: weekly SMA20/21 above EMA21 blocks longs.
2. Phase allocation: 100% exposure in bull, 50% in warning, 0% in bear. The grader uses the phase as a good/bad context flag, not as a size.
3. BTC daily body-trend gate for altcoin longs.
4. Weekly close below the prior weekly low as a downside marker (the 8 of 9 claim).
5. Daily RSI at or above 80 as a continuation, not exhaustion, marker.
6. Punch-high and punch-low at extremes.
7. 10/21/55 daily EMA dip test as a location, alongside the grader's weekly EMA10.
8. Weekly-10 respect count and the 3-to-4-close reclaim filter.
9. Batman exit: close below the last swing low's box.
10. Buyback depth as a function of run size (10% of the run).
11. Touch-count break probability; round-number levels; monthly-open gate; 2-day cloud top marker; monthly A/D veto.

---

# School 2: market-cipher

The school covers users of the Market Cipher B indicator (Wave Trend, Money Flow, VWAP, RSIs, momentum, divergence dots and "green dot" and "yellow X" signals), including the free VuManChu Cipher B clone. Lead sources are cryptoface, the official market-cipher channel and jason-casper. Educators: trader-geo, trade-pro, daviddtech, trading-bureau, dylan-waves, gunslinger-trades, frankie-candles, alexocrypto, chart-wis, dbsi-reviewers, gainium, market-cipher-educators.

The traders use two vocabularies. The anchor and trigger vocabulary (cryptoface, market-cipher, jason-casper, dylan-waves, gunslinger-trades, chart-wis, market-cipher-educators) describes a deep wave followed by a smaller wave. The filter vocabulary (trade-pro, daviddtech, gainium, frankie-candles, trading-bureau) describes a dot plus filters. Trader Geo and trading-bureau use neither fully. Where a file has no anchor or trigger vocabulary, this file says "nothing stated".

## Anchor wave, trigger wave, Money Flow "deep green", and confirmation after the dot

This section is exact and checkable. Each item is tagged "stated" or "inferred from a worked example".

### 1. Anchor depth, trader by trader

| Trader | Anchor depth | Tag | Citation |
|---|---|---|---|
| cryptoface | "Typically" past the blue line; "nice deep". No number. The narration gives thresholds at +-60 and alarm lines at +-53, and says the on-screen blue lines are the alarm lines. No dotted line is named. | stated | How to Start Successfully, 2021-02-13, 31:15; Intro to Market Cipher, 2019-06-17, 20:04, 21:09, 32:23; Ch.1 Divergences, 2019-06-08, 05:22 |
| market-cipher (narrator) | "At least below this white line and dotted line". The dotted-line value is never stated. | stated (value not stated) | Market Cipher Basic/Core Strategy, 2023-08-03, 00:32 |
| market-cipher (narrator, looser) | "past this solid line at least a Green Dot is nice too". | stated | same video, 14:27 |
| market-cipher (counter-example) | A "halfway down" anchor gave 0.5% in 4 hours. | inferred from a worked example | same video, 11:25 |
| jason-casper | "Usually" below -60. No big dot required. In 2021 an anchor is a structural reference point. | stated | 4/24 Beginner Strategy, 2024-02-06, 02:02 to 02:33; How to Find the Best Trades, 2021-03-18, 24:00 |
| dylan-waves | A wave past +-60; deeper and thicker is better. No dotted line. | stated | Basic Strategy, 2023-11-24, 12:49, 13:20, 18:58, 19:59, 21:31, 22:01 |
| trader-geo | Nothing stated as an anchor. The environmental-timeframe green dot sits "relatively close to or lower than this 60 line". "Near" has no number. | stated (no number) | EASY Market Cipher Strategy, 2023-06-02, 02:37, 03:07 |
| trader-geo (cheat sheet) | Green dot below ZERO, not -60. | stated | Cheat Sheet Entry, 2023-09-08, 01:01 to 05:06 |
| gunslinger-trades | The large green dot is the anchor. No depth number. | stated | 4/24, 2022-03-29, 07:42 |
| chart-wis | A larger wave first. No numbers. | stated | Market Cipher B tutorial, 2026-10-09, 02:03, 02:33 |
| market-cipher-educators (Crypto Wolf) | The first wave "usually" over 60, then two more waves (triple anchor). | stated | 2023-12-28, 01:01 |
| trading-bureau | Dot beyond +-45 (a "shark fin" shape, never defined); use 60 for conservative. | stated | Trigger Waves, 2025-11-26, 00:40, 01:11, 01:42, 02:44, 05:20 |
| trade-pro, daviddtech | No anchor. Blue waves on the opposite side of zero (below zero for longs). No -60 requirement. | stated | trade-pro 2021-06-25, 04:36; daviddtech 2021-09-17 |
| gainium | Both WT lines below -60 with a green dot. Anchor and trigger not used. | stated | Ares Sanchez, 2026-04-06 |
| frankie-candles, alexocrypto, dbsi-reviewers | Nothing stated as an anchor. | not stated | n/a |
| market-cipher guests | TP Mint: wave hits -60 on the 1H. AlexOCrypto: line at 55, a "mini anchor". | stated | Trader Spotlight Episode 4, 2024-02-16, 10:02; Episode 5, 2024-03-29, 20:20 |

Summary: the depth gate is -60 for cryptoface, jason-casper, dylan-waves, gainium and TP Mint; +-45 for trading-bureau; "dotted line" with no value for the official narrator; "below zero" for trader-geo's cheat sheet, trade-pro and daviddtech. Nobody gives a number for "nice deep" or "near".

### 2. Trigger-wave definition

Plain statement: no trader gives a size ratio between the trigger wave and the anchor, and no trader gives a maximum bar gap between them. Wherever the table says "no ratio or gap", that is a statement that the file has none.

| Trader | Trigger wave | Ratio or gap | Citation |
|---|---|---|---|
| cryptoface | "Smaller, shorter". The second hump can stay above the line. The second trough is higher. A guest adds that the wave "never went above zero between the troughs". | none stated | Ch.1 Divergences, 08:31, 13:47; LIVE Weekly Green Dot, 2022-02-07, 12:10; Completely NEW Trader, 2019-08-10, 31:24 |
| market-cipher | "Noticeably smaller" and a "big difference". The entry is the trigger wave's light-blue to dark-blue turn. | none stated | Core Strategy, 2023-08-03, 00:32, 12:25, 03:05 |
| jason-casper | Any higher wave after a deep one; or a higher wave with higher Money Flow. After a regular bullish divergence, the next wave below zero is the big impulse (about 90%). | none stated | Divergence Masterclass, 2025-05-21, 13:45 to 14:16; 4/24 Beginner Strategy, 02:33 to 03:03 |
| dylan-waves | A smaller wave with a HIGHER LOW. One anchor can have many triggers. "Tight to zero" is ideal. A lower low means the shift is over. | none stated | Basic Strategy, 14:21, 14:51, 15:23, 15:54, 16:24, 16:56, 18:58, 19:29 |
| gunslinger-trades | A wave above the big-dot anchor, a single "lumpy" island below zero. The small green dot is the trigger's dot. | none stated | 4/24, 00:30, 01:01, 01:32, 03:34, 04:05, 06:39, 07:11 |
| chart-wis | A smaller wave with progressively higher lows. | none stated | 02:03, 02:33 |
| market-cipher-educators | After the three waves, a trigger wave that is optional extra confirmation. | none stated | 01:32 |
| alexocrypto | A different thing: a "trigger wave" that does NOT reach the overbought or oversold lines; the zero cross is the trigger. | n/a | Trigger Waves, 2023-12-17, 01:32, 02:02, 03:03 |
| trading-bureau | "Trigger Waves" here means the dot beyond +-45 with VWAP and Money Flow. No anchor. | n/a | 2025-11-26 |
| trader-geo, trade-pro, daviddtech, gainium, frankie-candles, dbsi-reviewers | Nothing stated. | n/a | n/a |

The 4-hour gap in jason-casper's divide-by-10 pairs concerns timeframes, not bars between waves.

### 3. Money Flow "deep green" and "turning up"

Nobody says "deep green" as a rule. The terms that do occur are "thick", "deep" and "nice deep red", none with a number. The question is whether the signal is colour, shape or the zero cross.

| Trader | What counts | Tag | Citation |
|---|---|---|---|
| cryptoface | Shape: curvature, entering about three-quarters of the way through the wave. OR the zero cross (basic strategy). Colour green is the environment, not the signal. | stated | How to Start Successfully, 05:02, 07:18, 09:32; Basic Strategy CORRECTLY, 2023-11-25, 03:41 |
| market-cipher | Shape, not colour. It may still be red. "Smooth round". The live trader says "outwardly curve", and the earliest entry is the zero cross. | stated | Core Strategy, 02:04; live trader 2023-11-25, 02:08, 03:15 |
| jason-casper | The zero cross is the entry event. "Thick" is magnitude. Dual Money Flow plus momentum divergence is strongest. Money Flow divergence alone is a no-go. | stated | Amazing Trading Strategy, 2021-11-02, 04:19; How to Find the Best Trades, 07:14, 08:47; Understanding Moneyflow, 2022-01-30, 05:55, 06:26; Divergence Masterclass, 30:57 |
| trader-geo (2023-06-02) | A Money Flow cross up. Thick green Money Flow before a short means no short. | stated | 05:11; 2024-01-06 Episode 1, 07:17 to 08:21 |
| trader-geo (cheat sheet) | "Thick green" Money Flow; "thick" is subjective, relative to the previous wave. Only Money Flow green and the dot below zero are non-negotiable. | stated | 2023-09-08, 01:31, 10:12 |
| dylan-waves | Money Flow in or coming into the red as the anchor prints, then curving toward zero. "Deep" has no number. Jagged is bad. Still falling at the trigger means no signal. | stated | 13:20, 13:51, 17:57, 22:31, 33:43, 34:44 |
| trading-bureau | Direction as an "arrow", not colour. Pointing against the trade is a veto, regardless of sign. | stated | Money Flow, 2026-03-03, 03:44, 01:43, 04:14 |
| trade-pro, daviddtech | Money Flow the colour of the trade, as one of four filters. | stated | 03:03 / 03:43 |
| frankie-candles | Money Flow sign is a trend filter; the zero cross is the event. | stated | 04:33, 03:33 |
| gainium | Money Flow shifts red to green. | stated | 2026-04-06 |
| gunslinger-trades | A cross to positive is one of two alternative triggers. | stated | 4/24, 02:04, 02:34, 10:14 |
| chart-wis | A smooth outward curve is continuation; an inward curve is weakening. | stated | 01:03, 01:33 |

Summary: colour is a filter for trade-pro, daviddtech, gainium, trader-geo and cryptoface (as environment). Shape is the signal for cryptoface, market-cipher, dylan-waves, trading-bureau and chart-wis. The zero cross is the trigger for jason-casper, frankie-candles, gunslinger-trades and the official live trader's earliest entry.

### 4. What each trader waits for after the dot

| Trader | Waits for | Tag | Citation |
|---|---|---|---|
| cryptoface | The dot to survive the bar close. The dot only starts the search; a weekly dot alone is not enough. The cross must hold 1 to 2 candles. Worked sequence: dot, deep anchor, smaller wave "not ready yet", Money Flow curves up from red, another smaller wave, then enter. On the 2022 weekly dot he did not wait for the weekly close. | stated; sequence inferred from a worked example | Intro, 13:23, 13:54, 24:13; How to Start Successfully, 24:13, 23:04, 14:29, 38:02, 38:35; LIVE Weekly Green Dot, 144:30 |
| market-cipher | The second smaller wave; smooth Money Flow; higher-timeframe backing; the Money Flow zero cross; Money Flow joining momentum in the positive; the wave leaving +-60; the VWAP crossing zero. No candle-close rule is stated. | stated | live trader 04:48, 05:24, 07:33, 12:44; The Basics of Market Cipher, 2022-07-11, 10:51; Nick, 2021-04-04, 00:30 to 01:33 |
| market-cipher (TP Mint) | A candle body closing above "the AEMA" after the wave hits -60 on the 1H, with price under the weekly open. EMA length unknown. | stated | Episode 4, 10:02, 10:37 |
| jason-casper | A LOWER-timeframe trigger. 4/24: a 4H trigger, then a 24m trigger wave or Money Flow cross, taking the 4H dot intrabar. 12m/3H: 12m Money Flow cross while a 3H dot prints. Candle close confirms a dot, but he often does not wait (waiting cost 0.25% against 1.4% in one case). For beginners, wait for the dot. | stated | 4/24, 03:33, 07:06; Amazing Trading Strategy, 10:28, 14:07; High Win Rate Beginner Strategy, 21:21, 09:00 to 09:37; Divergence Masterclass, 09:16, 10:53, 40:26 |
| trader-geo | Execution timeframe is environmental divided by 5 to 10. Then a bullish divergence, a Money Flow cross up, wait for the dot to close, enter. Front-running is risky; newcomers wait for the candle. | stated | 2023-06-02, 01:33, 02:05, 03:37, 05:11; 2023-09-08, 07:09 |
| dylan-waves | The trigger wave itself with Money Flow curving up, then VWAP curvature with each bar sharper as it crosses zero ("final confirmation"). No candle-close rule. | stated | 24:01, 24:31, 29:37 to 31:10 |
| gunslinger-trades | 4/24: then on the 24m, whichever comes first, a green dot or a Money Flow cross to positive; entry the next 24m candle. 3/12: the 12m Money Flow cross FIRST, then a 3H dot within 2 to 5 twelve-minute candles (he says "three to five" at 04:06 and "two to five" at 07:14; about seven is a no). | stated | 4/24, 02:04, 03:04, 03:34; 3/12 2022-04-03, 04:06, 04:37, 07:14 |
| trading-bureau | Money Flow turning up after the dot. | stated | Trigger Waves, 05:52; Money Flow, 04:45, 05:15 |
| frankie-candles | The dot must close; unconfirmed it can "unprint". | stated | 12:41 |
| alexocrypto | Does NOT wait: "by now it's too late". Front-runs the 4H from the 1H double dot, 2H confirmed, 3H printing. | stated | 04:34 to 05:37; 06:38 to 08:44 |
| gainium | Enter on the candle AFTER the dot. | stated | 2026-04-06 |
| market-cipher-educators | Waits for the third divergence; the dot confirms; getting in while it still prints is preferred. | stated | 01:01, 01:32, 02:03, 02:33 |
| trade-pro, daviddtech | Nothing waited for after the dot. Whether trade-pro enters at the close of the cross bar or the next open is not stated. DaviddTech enters on the dot candle. | stated / not stated | trade-pro 2021-06-25; daviddtech 2021-09-17 |
| dylan-waves, chart-wis, dbsi-reviewers | No candle-close rule stated. | not stated | n/a |

Candle-count waits actually stated: cryptoface 1 to 2 candles of the cross holding; gunslinger-trades 2 to 5 twelve-minute candles between the Money Flow cross and the 3H dot. Nobody else gives a number.

### 5. Vetoes

| Veto | Held by | Citation |
|---|---|---|
| Yellow X on the same candle as the dot | cryptoface | Intro, 17:57 |
| Yellow X on the higher timeframe | cryptoface | Whale Crossing, 2019-07-26, 08:55 |
| Higher-timeframe Money Flow, RSI and momentum all falling | cryptoface | Basic Strategy CORRECTLY, 05:15 |
| Thin Money Flow | cryptoface | How to Start Successfully |
| Money Flow pointing against the trade | trading-bureau | Money Flow, 2026-03-03 |
| Money Flow still falling at the trigger | dylan-waves | 33:43, 34:44 |
| Thick green Money Flow against a short | trader-geo | Episode 1, 07:17 to 08:21 |
| 4H dot above +60 vetoes the 4/24 long | jason-casper | 4/24 Beginner Strategy, 06:36 |
| Higher-timeframe waves "very separated" | jason-casper | Finding PERFECT Entries, 29:24 |
| 3H dot not printing at the 12m Money Flow cross | jason-casper | Amazing Trading Strategy, 14:07 to 15:11 |
| 3/12: the 3H dot before the 12m cross; more than 5 candles apart | gunslinger-trades | 3/12 |
| Momentum above +30 (skip unless Money Flow is very thick) | gunslinger-trades | 4/24, 05:06 to 06:08 |
| No confirmation, no trade | jason-casper | 2 Powerful Scalping Tips, 02:00 |
| Not at a pre-marked key level | trader-geo, jason-casper | 2024-11-01, 08:13; Divergence Masterclass, 19:35 |
| Environmental overbought means no longs | trader-geo | 2024-11-01, 06:14 |

Higher-timeframe agreement is required by cryptoface in 2021 and 2023 and not required in 2020 (see disagreements). The official market-cipher channel names no explicit vetoes.

### 6. What is inferred and what is stated, in one line each

- The -60 gate: stated by five traders, not by the official narrator (dotted line, no value).
- The trigger ratio: not stated by anyone.
- The bar gap between anchor and trigger: not stated by anyone, except gunslinger-trades' 2 to 5 twelve-minute candles between two different signals.
- "Deep green" Money Flow: not stated by anyone. The nearest are "thick" and "nice deep red".
- A candle-close rule after the dot: stated by cryptoface, jason-casper (for beginners), trader-geo, frankie-candles, gainium; explicitly rejected by alexocrypto; not stated by the official channel.

## Shared rules, in method order

These are the rules held by two or more traders. Single-trader rules are listed after under "Held by one trader".

### Context

- Check a higher timeframe and require it to back the trade or at least not oppose it. [cryptoface 2021 and 2023; market-cipher live trader 07:33, 12:44; jason-casper, Finding PERFECT Entries, 29:24; dylan-waves, 04:37 and 05:08 (two timeframes up and two down); trader-geo, 08:41 and 09:11]
- The signal's timeframe is a ratio of the checking timeframe. cryptoface: 15x (4m entry, 1H check). jason-casper: divide by 10 (4H to 24m, 3H to 18m, 2H to 12m, 50m to 5m). trader-geo: 5 to 10. [cryptoface; jason-casper; trader-geo 2023-06-02, 01:33]
- Money Flow sign as the environment: green favours longs. [cryptoface; trade-pro; daviddtech; frankie-candles, 04:33; trader-geo]
- The 50 EMA as a pullback zone: trade-pro, daviddtech, trader-geo (cheat sheet). The 200 EMA as side filter: trade-pro and daviddtech. The 21 EMA: jason-casper.

### Setup (location)

- Deep wave at or beyond the threshold (section 1). [cryptoface, market-cipher, jason-casper, dylan-waves, gainium, trading-bureau, TP Mint]
- A key level marked beforehand, indicator read only there. [jason-casper; trader-geo 2024]
- Divergence between price and the wave, or Money Flow, or both. [cryptoface, jason-casper, trader-geo, market-cipher guests, market-cipher-educators, frankie-candles, gainium]

### Trigger

- Anchor then a smaller trigger wave with a higher low, entry on the trigger cross. [cryptoface, market-cipher, dylan-waves, jason-casper (any higher wave), gunslinger-trades, chart-wis, market-cipher-educators]
- The Money Flow zero cross as the trigger or earliest entry. [jason-casper, frankie-candles, gunslinger-trades, market-cipher live trader, cryptoface (basic strategy), alexocrypto]
- The green dot as the trigger. [trade-pro, daviddtech, gainium, trader-geo, gunslinger-trades, trading-bureau]

### Confirmation

- Money Flow shape or colour agrees. [section 3]
- Higher-timeframe backing. [context]
- A lower-timeframe trigger (jason-casper, gunslinger-trades, trader-geo, alexocrypto).
- VWAP crossing zero or curving up. [market-cipher narrator; dylan-waves; trading-bureau]
- Candle close of the dot. [section 4]

### Vetoes

- See section 5. The commonest are a yellow X, Money Flow against the trade, and higher-timeframe disagreement.

### Stop

- Beyond the last swing low or high. [jason-casper (many videos); trade-pro; gainium; trader-geo (no buffer); trading-bureau (a little buffer, no size)]
- Alternatives: ATR band [daviddtech, "second ATR band", length and multiple not stated, 04:52 and 05:28]; below the 21 EMA, about 4% [jason-casper, How to Stop losing Trades, 10:30 to 11:38]; the previous 6H candle's last wick, about 3% [gunslinger-trades, DBSI 6H, 03:07].
- The official market-cipher channel states no stop level for the blue wave.

### Exits

- A fixed reward-to-risk: trade-pro 2R; daviddtech 1.5 to 2R; jason-casper at least 2:1, prefers 3:1; trading-bureau majority at 1:1, then break-even, ultimate 1:3; alexocrypto 2.5 to 3:1.
- Exit on the indicator: Money Flow curving back (jason-casper); the higher-timeframe Money Flow flipping (frankie-candles, 11:09); the first bearish divergence on the entry timeframe (trader-geo, 07:39 and 08:10); the blood diamond (official channel, 2023-08-03, 08:50); a red diamond take-profit zone (jason-casper, Market Cipher A).
- Partials: jason-casper uses several (50/20/30; 25 to 50% then break-even; 50% at 3:1). cryptoface scales in and out in 2 to 4 pieces and never averages down.

### Sizing

- Risk 0.5 to 5% per trade, 1% standard: jason-casper; dylan-waves 0.5 to 1%; the official channel mentions 1% (Dylan). Leverage varies widely (jason-casper about 20x for dual divergence, 25 to 30x himself, 1 to 3x for beginners).

### Timeframes and indicator settings

- Thresholds at +-60 and +-53 (cryptoface), +-45 (trading-bureau). Regular candles (jason-casper, Finding PERFECT Entries, 35:31); Heikin-Ashi (official channel recommends; cryptoface always). Regular RSI, not stochastic RSI (jason-casper, 64:13 to 64:44). No WaveTrend lengths are stated by anyone. DBSI maximum is 34 per the vendor and 31 in a video.
- In the terminal only RSI, EMA, WaveTrend/Money Flow and volume exist. Wolf Pack ID, Crown, Awesome Oscillator and the Market Cipher A diamonds are not available.

## Held by one trader (not shared)

- cryptoface: "enter in red, exit in green"; "roach pig" (bank 30%); a list of 23 candidate setups; same-candle yellow X veto; MF plus momentum as the mandatory core (17:20), 3 to 4 bullets (27:33).
- jason-casper: the three-part entry rule (Finding PERFECT Entries, 11:06); 1H divergence with entry on the second dot; 12m or 6m double divergence at a level; the 2021 Wolf Pack ID crosses; the Awesome Oscillator first green bar; the yellow VWAP as a third divergence layer (06:50); about 80% of his own losses came from "kidding myself" (11:50 to 12:21); re-enter after a stop-out if confirmations remain.
- trader-geo: four-condition cheat sheet (thick green Money Flow, dot below zero, pullback into the 50 EMA, hidden bullish divergence); in practice 1 to 3 are required; take profit one at the high of the move; escalate to a higher timeframe on failure.
- gunslinger-trades: the 3/12 and 4/24 pair sequences; the DBSI 14 rule.
- trading-bureau: three confluences for at least five with outside ones; a mistaken example at Money Flow 05:15 that contradicts his own rule (probably a slip; not used).
- market-cipher-educators: the triple-anchor rule ("when you see the three there's usually never a four").

## Where they disagree

| Topic | Side A | Side B |
|---|---|---|
| Is higher-timeframe agreement required? | cryptoface 2021 and 2023: yes | cryptoface 2020 [Basic Strategy Part 2, 2020-05-13, 07:19]: not required |
| Yellow X trap rate | cryptoface 2019: about 80% | cryptoface 2021: 55 to 60% |
| Money Flow entry | cryptoface: 3/4 curve | cryptoface (2023): zero cross |
| Wait for the dot to close | cryptoface, trader-geo, frankie-candles, gainium: wait | alexocrypto: "by now it's too late"; jason-casper often does not wait; cryptoface did not wait on the 2022 weekly dot |
| Depth gate | cryptoface, jason-casper, dylan-waves, gainium: -60 | trading-bureau: 45; trader-geo cheat sheet, trade-pro, daviddtech: below zero; alexocrypto: trigger waves do not reach the lines |
| Trigger | anchor then smaller wave (many) | jason-casper: any higher wave; trade-pro, daviddtech: just the dot plus filters |
| Candles | official channel and cryptoface: Heikin-Ashi | jason-casper: regular candles |
| Waiting for Money Flow shape against colour | colour as filter: trade-pro, daviddtech, gainium | shape as signal: cryptoface, market-cipher, dylan-waves, trading-bureau |
| Long entry against the 50 EMA | trade-pro: pulled back into the 50 EMA | daviddtech: a close beyond the 50 EMA |
| Test results | trade-pro: 54 won of 100 on EUR/USD 30m at 1:2 | daviddtech: 44% win rate over 144 trades, net profitable, timeframe unstated |
| Stop | jason-casper often beyond the swing | jason-casper 2021 often no hard stop; official channel gives none |
| DBSI maximum | vendor: 34 | video: 31 |

## Candidate rules not in the pre-registered setup grader

The grader already uses the WT cross at WT2 at or below -60, daily and weekly Money Flow, divergence and the stop ladders. The candidates below are about things it does not test. Each needs its own pre-registration. Where a trader gave no number, the test must pre-register one and say so.

1. Anchor-then-trigger sequence: a prior wave below -60, then a later cross at a higher low of the wave. Grid the trigger-to-anchor ratio and the maximum gap, because no trader states either. [dylan-waves, cryptoface, market-cipher]
2. Smooth-Money-Flow proxy: Money Flow curving up for k bars, regardless of colour, at the trigger. [cryptoface, market-cipher, dylan-waves, chart-wis]
3. Money Flow zero cross after the dot within k bars. [jason-casper, frankie-candles, gunslinger-trades, market-cipher live trader]
4. Higher-timeframe slope backing at the signal (not the grader's weekly Money Flow rising rule, but the slope of the next timeframe up). [cryptoface, jason-casper, dylan-waves]
5. Wave leaving +-60 as the entry (rather than the cross). [Nick, 2021-04-04]
6. Candle-close confirmation compared with entering on the open of the dot bar. [cryptoface, trader-geo, alexocrypto against jason-casper]
7. Body close above an EMA after the wave touches -60 (EMA length unknown, pre-register one). [TP Mint]
8. Thick-Money-Flow counter-trade veto: no short while Money Flow is thick green. [trader-geo]
9. Dual divergence (price against wave and Money Flow both). [jason-casper, trader-geo]
10. VWAP crossing zero as a second confirmation (probably the same event as the dot, so test for independence first). [market-cipher narrator, dylan-waves, trading-bureau]
11. Wave-and-Money-Flow sign agreement; momentum cap at +30 for longs. [gunslinger-trades, trade-pro]
12. Trigger at +-45 instead of +-60. [trading-bureau]
13. Exit on the first bearish divergence on the entry timeframe; exit on Money Flow curving back. [trader-geo, jason-casper]
14. Heikin-Ashi input for the wave. [official channel, cryptoface]
15. Mini-anchor class (a wave to about 55). [alexocrypto]
16. DBSI at or above 14 (needs vendor data; not computable in the terminal). [gunslinger-trades, dbsi-reviewers]
17. Next-candle entry with both WT lines below -60. [gainium]
18. Triple-wave divergence plus a second-chance trigger. [market-cipher-educators]
19. Cheat-sheet long: thick green Money Flow, wave below zero, pullback to the 50 EMA, hidden bullish divergence, 1 to 3 required. [trader-geo, trade-pro, daviddtech]
20. Lower-timeframe sequence: Money Flow cross first, then a higher-timeframe dot within 2 to 5 candles. [gunslinger-trades 3/12]

---

# School 3: cycle

The school covers three groups: Camel Finance (daily video calls and an indicator he sells), the Bressert double smoothed stochastic (DSS) sources, and LikesMoney, a short written second voice. They share the idea that markets move in nested time cycles with a predictable low-to-low length, and that a low is only trusted once confirmed. They are kept in separate subsections because their evidence differs, and Camel's tool is not merged with Bressert's DSS.

## Method in its own terms

Cycle traders count the days or weeks between successive cycle lows. A cycle low is expected inside a window (the average length plus or minus a tolerance, usually 10%). They wait for the window, then wait for the low to be confirmed on its own timeframe, then buy the first higher low. A cycle that peaks early and closes below its start is "left-translated" and failed; one that peaks late is "right-translated" and bullish. A larger cycle sets the bias for the smaller. Stops go just below the cycle low. Under this school the oscillator, where used, only times the turn inside the window.

Note: in this repo "cycles as a low detector" and "translation as direction" are dead (failed surrogates). The cycle school's rules are recorded as stated. They are not evidence.

## 3a: Camel Finance

Source: camel-finance.md. 69 daily transcripts from 2026-07-25 to 2026-10-17, plus older education videos. All are Whisper transcripts. Dated drifts are noted where they matter.

### Shared rules, in method order

#### Context

- The larger degree sets the bias for the smaller; the BTC phase governs altcoins; trend-follow the higher timeframe until it breaks.
- Structure: below the swing high means a downtrend; a bull market needs a higher low and then a higher high.
- Failure cascades upward: two failed dailies imply a failed weekly.
- The 80% rule. [2026-10-13, 06:18; 2026-10-14, 10:00]
- Inversions mean seller exhaustion. The CF indicator docs do not list inversions.

#### Setup (location)

- Location is a time window, not a price. [It's Started, 2026-08-04, 04:11; The Treasury Just Blinked, 2026-08-20, 11:46; Here's my Trading Plan for the Week, 2026-10-05, 02:47]
- The first hurdle is the swing high after a cycle low. The preferred long is the breakout-retest higher low.

#### Trigger

- A long is a daily cycle low (DCL) confirmed inside the window. [It's a trap, 2026-07-27, 13:29; The Lows Finally Confirmed, 2026-07-31, 08:22]
- Never on the window alone. Later rather than earlier. [Time to LOCK IN, 2026-08-05, 06:41; More Upside Ahead, 2026-08-13, 10:07]
- Shorts come from left translation, then failure.
- "Take a shot to land a shot" with a 2% stop. [2026-09-17, 20:30]
- Two accounts (short-term and long-term) enter differently.

#### Confirmation

The checklist was never fixed. By date:

| Date | What counts | Citation |
|---|---|---|
| 2023-08-30 | Any one of swing low, trendline break, close above the 10 MA | 10:33, 11:03 |
| 2026-07-28 | "Two out of six" was not enough | 06:12 |
| 2026-07-29 | Medium confluence | 02:03 |
| 2026-09-16 | All three wanted: trendline break, indicator pick and Bressert cross | 08:22 |
| 2026-09-17 | Any one is enough | 17:52 |
| 2026-10-13 | Pivot, Bressert cross and trendline break | 02:05 |

#### Vetoes

- No bull call without a higher-high close ("malpractice to be bearish above this line, but it is malpractice to be bullish whilst we are below it").
- No stock short without a printed-then-failed cycle.
- No trade on an unconfirmed low.
- No averaging down in the short-term account.
- RSI divergence alone is not a signal. [It's a trap, 2026-07-27, 03:03]

#### Stop

- A little below the cycle low. No fixed offset ("my eye pretty much works"). He suggests ATR, standard deviation or parabolic SAR for mechanical use. [Answering Your Questions, 2026-08-18, 01:06, 02:11]
- Close against wick for "failure" is inconsistent: 2023 "breaks below" (12:35); 2026-08-18 "closing below" (01:39); 2026-10-13 "closes below" (06:18); yet 2026-09-02 gold failed "on a wicking basis" (12:51).
- The BTC 83K line hardened into a closes rule, was softened on 2026-09-08 and 2026-09-14, and fired on 2026-09-22 (01:00), before a weekly close and out of the stated order. Oil invalidations were set after the fact.
- The long-term account has no hard stop, only a manual line under the latest confirmed DCL.

#### Exits

- Hold winners until a technical breakdown. R-multiple targets are used in places (ETH 10R, XRP 15R, TLT about double). Flat by Wednesday on a high-yield spread blow-out. [2026-10-17, 14:20]

#### Sizing

- 2% per trade, 6% per sector. [How to Manage Risk in Trading, 2024-03-12, 06:11 to 07:44]
- Leverage is a by-product of the stop. Long-term clips are 5%. Hit rate about 50%, good ones 60%.
- The metals allocation and the TLT worst-case arithmetic were restated many times and moved by date.

#### Timeframes and settings

- Each low is confirmed by a close on its own timeframe. Equity days count in trading days, BTC in calendar days.
- Cycle lengths by date:

| Market | Length | Citation |
|---|---|---|
| BTC daily | 60 days +-10% = 54 to 66 | 2023-08-30, 02:33 |
| BTC weekly | 24 to 28 in 2023; 28 to 32 on 2026-08-04; "This says 29 weeks, but we've averaged 31 weeks" on 2026-08-01 | dated |
| S&P daily | 40 days +-10% = 36 to 44; weekly 22 to 26 | dated |
| Gold daily | 22 to 28 | dated |
| TLT | 93 months | dated |
| DXY | 3 years | dated |

- Calibration: in-window base rate 80% in 2023 and 70 to 80% in 2025 to 2026; the 80/80 DCL resolution rule; timing-based consolidations are rare.
- No oscillator lengths are spoken. The DSS (10, 9, 5, with 80/20) in the ledger comes from a screen frame, not from speech.

### Guest within the Camel file: Andrew (@GoldnGuitars, 2024-08-10)

Back-test of a low on bars 5 to 10 (02:06); three confirmations (25:03, 25:34); typical lengths; trust the higher timeframe. Kept as a separate voice inside the file.

### Where they disagree (Camel against Camel)

| Topic | Earlier | Later |
|---|---|---|
| Confirmation count | 2023: any one; 2026-09-17: any one | 2026-09-16: all three; 2026-07-28: two of six not enough |
| Failure on close or wick | 2026-08-18 and 2026-10-13: close | 2026-09-02: wick (gold) |
| The 83K line | Closes rule | Softened 2026-09-08 and 2026-09-14, fired 2026-09-22 before a weekly close |
| Weekly BTC length | 24 to 28 | 28 to 32 |
| Entry timing | Window alone is never enough | "later rather than earlier" |

### What Camel's indicator (Cipher C / CF Cycle Trading Indicator) is said to compute

Provenance first. "Cipher C" appears in neither the videos nor the docs. The only "Cipher" in the transcripts is Cipher Mining. The product is the CF Cycle Trading Indicator, built with Variance Technologies, a TradingView script sold via cftrading.co.uk. The documentation is marketing copy, not a specification. Everything below is what the docs and videos say, not what the code does.

#### Outputs per the CF docs

- DCL, WCL and YCL detection (daily, weekly and yearly cycle lows). The daily 1D chart is required. The docs claim "70-80% accuracy".
- DCL states: confirmed, unconfirmed, cancelled, updated. The rules that must "align" are not listed.
- WCL has the cancelled state only.
- Pending DCL and WCL alerts exist for fine-tuned assets only.
- DCL confluence grade: High, Mid or Low, from breakout, momentum and supporting indicators. Components and cut-offs are not given.
- Future Cycle Low Range boxes: the claim is that the next low lands inside "70-80% of the time". The boxes update only once the latest DCL is confirmed.
- Translation labels LTR, MTR and RTR (left, middle, right), with colours. The MTR band is undefined.
- Cycle range midpoint, a cycle count number, a low windows table, and an asset cycle count table for 16 fine-tuned assets (the numbers are not published).
- A simple moving average (default length not stated), arcs, a US macro table, and alerts. There are no YCL alerts.

#### What the videos add

- Inversions, which the docs do not list.
- Labelling only "through the window" and waiting for the open.
- "Medium confluence" read aloud on 2026-07-29 (02:03).
- Camel uses the tool as confirmation and confluence plus a window predictor. [Answering Your Questions, 2026-08-18, 11:02]
- The docs say it is not a signal service.
- The vendor back-test buys every flash without confirmation. It shows no edge on BNB and an edge on GDXJ. It was not public as of 2026-10-17.

#### Engine controls (words only, no defaults)

Cycle Length Adjustment Factor, Fast EMA Adjustment, Slow EMA Adjustment, Overbought Filter (Uptrend Threshold), Oversold Filter (Downtrend Threshold), and a Faster Confirmation toggle. Templates exist for fine-tuned assets ("tens of thousands of different settings").

#### What is NOT disclosed

- The formula.
- The pivot width.
- The EMA source and lengths.
- The overbought and oversold values.
- Base cycle lengths and tolerance.
- The rules for the confirmed tick.
- The Pending trigger.
- Close against wick for cancel.
- The confluence components.
- The MTR band.
- How a YCL is found.
- Whether the 10 SMA gates a label.
- How inversions are labelled.
- Any out-of-sample result.

#### Price

None spoken. The sales page lists $56 a month, $560 a year and $4,999 lifetime. From 1 November 2026, new customers come via the TradingView marketplace.

#### Clone decisions a re-implementation would have to make

1. Swing detector.
2. Per-asset cycle lengths.
3. Trend engine (the fast and slow EMA, overbought and oversold filters).
4. The confirmation rule. Candidates: close above SMA(10); DSS (10, 9, 5) crossing up from below 20; a trendline proxy; a thrust bar.
5. Cancel and update rules.
6. The Pending trigger.
7. The confluence score.
8. Degree nesting.
9. Future Cycle Low Range box geometry.
10. Translation top definition and the MTR band.
11. Timeframe.
12. Pre-registration against random-entry controls and surrogates.

### Candidate rules not in the pre-registered setup grader (Camel)

Ranked by Camel's rules file. Cycles as a low detector and translation as direction are dead and not candidates.

1. Close-based against intrabar cycle stop (the inconsistency is itself testable).
2. Confirmation-count gate, with the dated variants (any one, two, three).
3. Trend-template box breakout.
4. The "low is in" classifier: the 80/80 DCL resolution rule.
5. Breakout-retest higher low against the first low after the window.
6. Pyramid at confirmed DCLs with one trailing stop.
7. Higher-high-close regime switch (the 83K style rule, applied generally).
8. First-hurdle veto: no long under the swing high after the cycle low.
9. Timing window as a risk-placement filter, not a low detector.
10. In-window base rate (with surrogates).

## 3b: Bressert DSS sources

Source: bressert-dss.md. 11 sources, all secondary. Nothing in the file is Walter Bressert's own text. The DSS settings here are kept separate from Camel's tool.

### Method in its own terms

Cycle-first. A hierarchy of long-term (2 to 11 years), primary or weekly (9 to 22 weeks), a trading (daily) cycle in days, and alpha and beta sub-cycles that are each about half the length. The larger cycle sets the trend of the smaller. Translation is right or left. A 70% timing band (70 in the band, 20 early, 10 late; CrusherHODL, 12:23 and 12:53). The oscillator only times the turn inside the band.

### Five rules from the RSI3M3+ page (TradingView, kromut76)

The oscillator on this page is RSI3M3, NOT the DSS.

1. The low falls 15 to 30 market days after the previous trading-cycle bottom.
2. A cycle high with the oscillator above 60 came first.
3. The oscillator drops below 40 and turns up.
4. Buy on a rise above the high of the upturn day.
5. Stop slightly under the cycle low (the script uses 99% of the bottom price).

Script defaults: cycle length 15 to 30, lookback 30; arrows at 30/70.

### DSS definitions and settings, source by source

| Source | Definition and settings |
|---|---|
| LuxAlgo | Stochastic of price, EMA, stochastic of the EMA line, EMA. n about 10 to 21, m about 3 to 9. Levels 80/20. Oversold hooks only when a dominant cycle low is due and the higher timeframe points up. |
| TC2000 | Stochastic 14, EMA 5, optional EMA 1, signal EMA 5. Levels 70/30. |
| StrategyQuant | Levels 80/20. No lengths. |
| Indicator Factory | Period 2 to 500 (common 5 to 30), smoothing 1 to 50. Levels drawn at 80/20 though the text says 70 or 80 and 20 or 30. |
| The Forex Geek | MT4/MT5 defaults EMA 8, stochastic 13, levels 80/20. This equals the terminal's shipped 13/8/5. |
| NetPicks | Credited to Blau; levels 70/30; a different construction. |
| TQFM | Two plain stochastics (20 and 200). NOT a DSS. |
| RSI3M3+ | RSI 3 smoothed by a 3-period MA (type unstated); required bars 20; overbought 70, oversold 30; buy line 40, sell line 60; stop 0.99 of the cycle low. |
| Jerome Bressert (HAL Cycle Finder) | Green, yellow and red lines; green dots mark cycle-low bands. No formulas or lengths. |
| SwingTrading with Cycles | A 10-period moving average, type not stated, on every timeframe. |

No source names 10/9/5 as a default or gives Bressert's own DSS lengths. The terminal's DssBressertProvider uses the LuxAlgo structure with shipped default 13/8/5. Camel's 10/9/5 with 80/20 is therefore a parameter change, from a screen frame, not speech. The levels split 80/20 against 70/30.

### Presenters' own additions (not Bressert's)

- CrusherHODL (2024-12-22): trade the trend with the cycle; always look at higher timeframes; a 60-day daily cycle plus or minus 10% = 54 to 66 days (from "the counts that most people agree on", not from Bressert, and in conflict with the five rules' 15 to 30 day trading cycle); BTC weekly cycle 24 to 42 weeks; nesting ("a daily cycle low will always fall on a weekly cycle low"); a break of the alpha-cycle low below the daily cycle low is a cycle failure (14:23, 16:26); size up at weekly and 4-year lows, no numbers (15:24, 01:36); oscillators RSI, MACD or TSI, no settings (10:50); he marks only daily cycle lows (16:56).
- Others: Jerome Bressert (2016-02-02), SwingTrading with Cycles (2022-10-02), Indicator Factory (2022-11-22), NetPicks (2023-07-20), Forex Geek (2026-01-12), TQFM (2023-10-25).

### Shared rules across the Bressert sources, in method order

- Context: higher cycle or higher timeframe sets the trend (LuxAlgo, CrusherHODL, RSI3M3+, SwingTrading with Cycles).
- Setup: a timing band for the next low (RSI3M3+: 15 to 30 days; CrusherHODL: 70/20/10 band).
- Trigger: the oscillator turns up from the oversold zone (RSI3M3 below 40; DSS below 20 or 30 hook or cross).
- Confirmation: price rises above the high of the upturn day (RSI3M3+); same indicator on higher timeframes (RSI3M3+, LuxAlgo).
- Veto: against the higher timeframe trend; alpha-low failure (CrusherHODL).
- Stop: just under the cycle low (RSI3M3+: 0.99 of it).
- Exit: oscillator above 60, turns down and price trades below the downturn day's low (RSI3M3+).
- Sizing: nothing numeric stated.

### Where they disagree

| Topic | Side A | Side B |
|---|---|---|
| Levels | 80/20: LuxAlgo, StrategyQuant, Indicator Factory, Forex Geek | 70/30: TC2000, NetPicks |
| Trend behaviour | LuxAlgo: pinned at the extreme in a trend | StrategyQuant: often reaches the other end |
| Cycle length | RSI3M3+ rules: trading cycle 15 to 30 days | CrusherHODL: 54 to 66 days |
| DSS lengths | LuxAlgo: n 10 to 21, m 3 to 9 | TC2000: 14 and 5; Forex Geek 13 and 8; none gives 10/9/5 |
| Trend filter | LuxAlgo and TQFM use the trend as a gate | Indicator Factory, Forex Geek, NetPicks leave it undefined |

### Candidate rules not in the pre-registered setup grader (Bressert)

The grader uses no DSS or RSI3M3. Cycle count may be used only as a filter on an oscillator trigger, with an exposure-matched random-entry control.

1. The Bressert five-rule long on daily bars (RSI3M3+): 15 to 30 days since the last cycle low, a prior oscillator high above 60, an upturn from below 40, buy-stop at the upturn day's high for k bars (pre-register k = 3), stop at 0.99 of the lowest low.
2. Upturn-day-high entry against entry at the close of the hook bar.
3. DSS hook against DSS/trigger cross against plain Stochastic(14,3) below 20, at (10,9,5), (13,8,5) and (14,5).
4. Timing-band filter: in-band against out-of-band hooks (pre-register +-20%).
5. Higher-timeframe gate: take a daily DSS hook only if the weekly DSS is rising.
6. Level set 80/20 against 70/30.
7. Trend-pinning measurement (descriptive): how often DSS reaches the other extreme inside an SMA(50) trend, to settle the LuxAlgo and StrategyQuant disagreement.
8. Bressert mirror exit: oscillator above 60, turns down, price trades below the downturn day's low, compared with the grader's exits.

The file also lists video-derived ideas (DSS level re-cross with a trend filter, the TQFM "trend inside a trend", a 10-period-MA undercut as a low gate, a swing-high plus late-count short, 70/20/10 band calibration, alpha-low failure as a veto, seasonal windows). Seasonal windows are relayed only and weak.

## 3c: LikesMoney (FXStreet cycle columns, 2017-03-10 to 2021-02-16)

How this source is used: as a short written second voice in the cycle school. It is kept separate from Camel and Bressert. The four columns run 166 to 409 words each, so the method is only partly visible. Treat it as a cross-check on vocabulary, not as a full rule set. Citations have no timestamps because the columns are text.

### Method in its own terms

A daily cycle (days since the last daily cycle low) sits inside an intermediate cycle (weeks). Each has a timing band. A swing low confirms the low; a close above the 10-day MA confirms the new cycle. Right translation trends up "in a stair step". An intermediate decline needs a left-translated, failed daily cycle.

### Shared rules, in method order

- Context: the intermediate cycle sets the frame; right translation is bullish. [Right Translation, 2017-10-19]
- Setup: the daily cycle timing band. Its construction is not stated. [Timing band, 2021-02-16]
- Trigger: a swing low confirms the low. [A Low is Nigh, 2017-03-10]
- Confirmation: a close above the 10-day MA. [A Low is Nigh; Daily cycle low, 2021-02-01]
- Further support: a daily trendline break, a TSI bearish zero-line cross, a weekly swing, bullish RSI divergence, 50-day MA support.
- Veto: an uptrend continues until a close below the lower daily band.
- Stop and sizing: nothing stated.
- Settings: 10-day and 50-day MA (type not stated); TSI and RSI lengths not stated; no DSS.

### Claims and calls

- A day-27 peak "assures" right translation (XLE). A bullish RSI divergence at a DCL means the advance continues. Embedded oversold RSI means a decline.
- Calls: 2017-03-10 US stocks DCL above 2369.08; 2017-10-19 S&P buy dips; 2021-02-01 DCL on 2021-01-29 (day 61); 2021-02-16 expects a combined DCL in 4 to 6 weeks.

### Where they disagree

One analyst, four columns: no internal disagreements are visible. Against Camel: LikesMoney uses a close above the 10-day MA as the cycle confirmation, which is one of Camel's several confirmations; and 61 days for a daily cycle is near Camel's 60 days for BTC but is an equity index here.

### Candidate rules not in the pre-registered setup grader (LikesMoney)

1. DCL confirmation buy: swing low plus close above the 10-day MA.
2. Cycle-band buy (the band is undefined, so pre-register one).
3. Swing-high plus 10MA-break exit or short.
4. RSI divergence at a DCL against an embedded oversold RSI.
5. Intermediate lows only after a left-translated daily cycle (conditional).
6. TSI zero-cross as a pre-DCL condition (the terminal has no TSI).
7. 50-day support swing low.

## Cycle school: candidates to run first

Across the three groups, the least defined and most testable are: the close-based stop, the confirmation-count gate, the higher-high-close regime switch, the 10-day MA close as confirmation (Camel, LikesMoney, SwingTrading with Cycles), and Bressert's five-rule long with an exposure-matched control.

---

# School 4: other

None currently.

Bob Loukas's transcripts are in the corpus (transcripts/bob-loukas/), but no rules file has been written for them yet. He is skipped on Cody's instruction, so he is not part of any school above.
