# DBSI reviewers (C3 IoT, CryptoKid): extracted rules

Source slug `dbsi-reviewers`, school **market-cipher**. Two different YouTube channels, each reviewing the then-new Market Cipher **DBSI** indicator within days of its release in May 2021. **2 transcripts read in full**, dated 2021-05-07 and 2021-05-09. Kind of material: tool tutorials / first impressions. Public. The indicator is paid (an invite-only TradingView script). YouTube ASR: "dbsi" is sometimes heard as other words, "hike and a she" = Heikin-Ashi, "v-wap/view op" = MC B's VWAP wave.

**Two speakers, kept apart below:**
- **C3 IoT** ("c3 family"): a single presenter. "Market Cipher NEW indicator DBSI | Full tutorial | UPDATED | How to setup and use on TRADING VIEW", 2021-05-07, https://youtu.be/yUZWIkY_LZE.
- **CryptoKid**: Josh and his dad. "Checking out the new DBSI indicator on Market Cypher", 2021-05-09, https://youtu.be/LOzLBD4uyck. Below, "CryptoKid" means the two of them; the dad does most of the reading.

| Date | Id | Channel | Title | Read |
|---|---|---|---|---|
| 2021-05-07 | yUZWIkY_LZE | C3 IoT | Market Cipher NEW indicator DBSI \| Full tutorial \| UPDATED | in full |
| 2021-05-09 | LOzLBD4uyck | CryptoKid | Checking out the new DBSI indicator on Market Cypher | in full |

Timestamps cite the start of the ~30 s transcript chunk that holds the words.

## Method in its own terms

Both reviewers read DBSI as **two numbers per candle**: a bear score above the candle and a bull score below. Each runs from −5 to 28. The higher a side's number, the stronger that side's push. A bullish shift is the bear number falling while the bull number rises. **14** is CryptoFace's recommended entry threshold for an alert. Both insist DBSI is **not used alone**. C3 wants regular and Heikin-Ashi candles to agree and "a nice fat VWAP" on MC B. CryptoKid reads DBSI numbers alongside MC B money flow, VWAP, RSIs and dots on each timeframe from 30m to daily, using higher timeframes for the overall pattern and DBSI on lower ones for entries.

## DBSI rules (exact)

- **Name and construction.** "dbsi which is the dual band strength index". It is "dual band" "because you have a top and bottom number" (C3) ([DBSI tutorial (C3), 2021-05-07, 01:02](https://youtu.be/yUZWIkY_LZE?t=62)). C3 says it "uses 36 different indicators all in one" and "was built off of a trading bot from crypto face himself" ([DBSI tutorial (C3), 2021-05-07, 02:03](https://youtu.be/yUZWIkY_LZE?t=123)). Unverified vendor marketing as relayed.
- **Which number is which.** Top = bear, bottom = bull (C3 ([DBSI tutorial (C3), 2021-05-07, 01:33](https://youtu.be/yUZWIkY_LZE?t=93)); CryptoKid: "the red ones on the top are the more bearish numbers and the one on the bottom are the bullish numbers" ([DBSI first look (CryptoKid), 2021-05-09, 00:00](https://youtu.be/LOzLBD4uyck?t=0))).
- **Score scale (exact).** "negative 5 to 28" (C3 ([DBSI tutorial (C3), 2021-05-07, 01:33](https://youtu.be/yUZWIkY_LZE?t=93))). "it ranges from negative 5 to 28" (CryptoKid ([DBSI first look (CryptoKid), 2021-05-09, 00:31](https://youtu.be/LOzLBD4uyck?t=31))). The later re-voiced vendor tutorial says −5 to 31 (`chart-wis.md`). Gunslinger guessed −10 to 30 (`gunslinger-trades.md`).
- **What a number means.** C3: "these numbers will show you what direction the trend is heading". "the higher the number up here the more probability of a swing to the downside" ([DBSI tutorial (C3), 2021-05-07, 02:03](https://youtu.be/yUZWIkY_LZE?t=123); [DBSI tutorial (C3), 2021-05-07, 02:34](https://youtu.be/yUZWIkY_LZE?t=154)). CryptoKid: "it's a strength indicator of that candle so it kind of gives you an idea of how much that candle is going to move" ([DBSI first look (CryptoKid), 2021-05-09, 00:31](https://youtu.be/LOzLBD4uyck?t=31)).
- **What a signal is (exact).**
  - **Threshold 14.** C3: "what crypto face recommends and what he does is he likes to use a 14s which are you know half of 28". "it comes defaulted with seven" ([DBSI tutorial (C3), 2021-05-07, 06:38](https://youtu.be/yUZWIkY_LZE?t=398)). The input sets when DBSI's "long entry" / "short entry" alert fires. CryptoKid: "what is the number that we start looking for 14 or higher" ([DBSI first look (CryptoKid), 2021-05-09, 00:31](https://youtu.be/LOzLBD4uyck?t=31)).
  - **Direction of change matters.** C3: "for a bullish pattern we want to see these top numbers low we want to see them decreasing and then these bottom numbers increasing" ([DBSI tutorial (C3), 2021-05-07, 03:04](https://youtu.be/yUZWIkY_LZE?t=184); [DBSI tutorial (C3), 2021-05-07, 03:34](https://youtu.be/yUZWIkY_LZE?t=214)). His example of a turn: the bottom number going from −5 to −1 while the top falls from 20 to 12 is "a really good sign" ([DBSI tutorial (C3), 2021-05-07, 03:04](https://youtu.be/yUZWIkY_LZE?t=184)).
  - **Extreme pair.** C3: "a 20 against a negative 5 paired with a nice little blood diamond" (an MC A symbol) meant "the trend was going down" ([DBSI tutorial (C3), 2021-05-07, 02:34](https://youtu.be/yUZWIkY_LZE?t=154)). CryptoKid: 16 on the bottom and −5 on top means "it's going to go up ... more than likely" ([DBSI first look (CryptoKid), 2021-05-09, 00:31](https://youtu.be/LOzLBD4uyck?t=31)).
  - **Indecision.** CryptoKid: "a five on top and five on the bottom so that's kind of indecision on the candle" ([DBSI first look (CryptoKid), 2021-05-09, 03:37](https://youtu.be/LOzLBD4uyck?t=217)).
- **What they wait for (exact).**
  - **The candle's close.** C3: on the 2H, 15 top versus −5 bottom, "unless we start to see this number decrease before this candle closes we're still looking at this downward trend" ([DBSI tutorial (C3), 2021-05-07, 04:04](https://youtu.be/yUZWIkY_LZE?t=244)). But for the alert he wants it **intrabar**, "once per bar", "as soon as it happens not when the candle closes" ([DBSI tutorial (C3), 2021-05-07, 08:41](https://youtu.be/yUZWIkY_LZE?t=521)). So the alert is the heads-up and the close decides.
  - **Regular and Heikin-Ashi agreement.** Regular candles give "an exact number". Heikin-Ashi gives "an actual moving average kind of like an ma or an ema", so the numbers differ, and "you need to use both of them you need to have confirmation from both" ([DBSI tutorial (C3), 2021-05-07, 04:04](https://youtu.be/yUZWIkY_LZE?t=244); [DBSI tutorial (C3), 2021-05-07, 04:36](https://youtu.be/yUZWIkY_LZE?t=276)).
  - **MC B agreement.** "make sure you have a nice fat v-wap coming back to the upside" (C3) ([DBSI tutorial (C3), 2021-05-07, 04:36](https://youtu.be/yUZWIkY_LZE?t=276)). CryptoKid: "this indicator is not meant to be used by itself it's ... meant to go along with all the other indicators that we have in market cipher" ([DBSI first look (CryptoKid), 2021-05-09, 01:02](https://youtu.be/LOzLBD4uyck?t=62)).
  - **All timeframes.** C3: "make sure you're going through all your time frames going back and forth between different candles" ([DBSI tutorial (C3), 2021-05-07, 05:06](https://youtu.be/yUZWIkY_LZE?t=306)). "give it the one four six hour backup" ([DBSI tutorial (C3), 2021-05-07, 09:43](https://youtu.be/yUZWIkY_LZE?t=583)). CryptoKid: "look at the higher time frames to ... see what the overall pattern is but then you can take a look at the lower time frames with the new indicator and ... use that for get your entry" ([DBSI first look (CryptoKid), 2021-05-09, 01:02](https://youtu.be/LOzLBD4uyck?t=62); [DBSI first look (CryptoKid), 2021-05-09, 01:32](https://youtu.be/LOzLBD4uyck?t=92)).
- **Long-term trend MA.** DBSI has a settable trend moving average, "a 200 candle moving average of 50 100 whatever" (C3) ([DBSI tutorial (C3), 2021-05-07, 07:08](https://youtu.be/yUZWIkY_LZE?t=428)).

## Anchor wave, trigger wave, money flow and confirmation (exact)

Neither reviewer discusses anchor or trigger waves. What they say about MC B alongside DBSI:
- **Money flow.** CryptoKid reads its thickness per timeframe: "we had big green money flow it's thinning thinning out" (30m); "thicker money flow than we did on the 30 minute" (1H); "very very thick green money flow we're still in green money flow but it's pretty thin" (daily) ([DBSI first look (CryptoKid), 2021-05-09, 02:03](https://youtu.be/LOzLBD4uyck?t=123); [DBSI first look (CryptoKid), 2021-05-09, 02:36](https://youtu.be/LOzLBD4uyck?t=156); [DBSI first look (CryptoKid), 2021-05-09, 05:08](https://youtu.be/LOzLBD4uyck?t=308)). "Thick" = large magnitude. No number.
- **Dots.** "two small green dots" on the 30m, and "a green dot" on the 2H, as positives ([DBSI first look (CryptoKid), 2021-05-09, 02:03](https://youtu.be/LOzLBD4uyck?t=123); [DBSI first look (CryptoKid), 2021-05-09, 03:06](https://youtu.be/LOzLBD4uyck?t=186)). C3 mentions "nice little bi[g] dots" and a yellow X ("manipulation") from MC A ([DBSI tutorial (C3), 2021-05-07, 07:39](https://youtu.be/yUZWIkY_LZE?t=459); [DBSI tutorial (C3), 2021-05-07, 09:12](https://youtu.be/yUZWIkY_LZE?t=552)).
- **What they wait for after a dot.** Nothing stated beyond the DBSI rules above.
- **Momentum position.** "we're kind of at the top of a momentum wave so that's kind of ... iffy" (3H) (CryptoKid) ([DBSI first look (CryptoKid), 2021-05-09, 03:37](https://youtu.be/LOzLBD4uyck?t=217); [DBSI first look (CryptoKid), 2021-05-09, 04:07](https://youtu.be/LOzLBD4uyck?t=247)). A wave near its top is a negative for a long.

## Context / higher timeframe / regime

- CryptoKid: higher timeframes give the overall pattern, lower timeframes the entry ([DBSI first look (CryptoKid), 2021-05-09, 01:02](https://youtu.be/LOzLBD4uyck?t=62)).
- C3: "the trend is your friend till the end". He chose a long alert because "we're in a bull market and the alts are crushing it" ([DBSI tutorial (C3), 2021-05-07, 08:11](https://youtu.be/yUZWIkY_LZE?t=491); [DBSI tutorial (C3), 2021-05-07, 09:12](https://youtu.be/yUZWIkY_LZE?t=552)).

## Location (levels, zones, where a setup counts)

- Nothing stated.

## Entry triggers

- C3: an alert when DBSI's bull score reaches 14 on the 15m (long entry), confirmed by Heikin-Ashi plus regular candles, the MC B VWAP and higher timeframes ([DBSI tutorial (C3), 2021-05-07, 07:39](https://youtu.be/yUZWIkY_LZE?t=459); [DBSI tutorial (C3), 2021-05-07, 08:11](https://youtu.be/yUZWIkY_LZE?t=491)).
- CryptoKid: a DBSI bull score of 14 or more on a lower timeframe, with MC B agreeing ([DBSI first look (CryptoKid), 2021-05-09, 00:31](https://youtu.be/LOzLBD4uyck?t=31); [DBSI first look (CryptoKid), 2021-05-09, 01:02](https://youtu.be/LOzLBD4uyck?t=62)).

## Confirmations (things that raise conviction)

- MC B per timeframe (CryptoKid's checklist): RSIs pointing up, VWAP above zero and "slanting up really hard", green dots, and money flow green and thickening ([DBSI first look (CryptoKid), 2021-05-09, 02:03](https://youtu.be/LOzLBD4uyck?t=123); [DBSI first look (CryptoKid), 2021-05-09, 02:36](https://youtu.be/LOzLBD4uyck?t=156)).
- Heikin-Ashi DBSI agreeing with regular-candle DBSI (C3) ([DBSI tutorial (C3), 2021-05-07, 04:36](https://youtu.be/yUZWIkY_LZE?t=276)).

## Vetoes (things that cancel a trade whatever else says)

- C3: higher timeframes (1H, 4H, 6H) showing "the exact opposite" of the entry timeframe means don't be "too zoomed into your charts" ([DBSI tutorial (C3), 2021-05-07, 09:43](https://youtu.be/yUZWIkY_LZE?t=583)). A soft veto.

## Stops and invalidation

- Nothing stated.

## Exits and targets

- CryptoKid: DBSI is "really good to find your entries and exits" on lower timeframes ([DBSI first look (CryptoKid), 2021-05-09, 06:11](https://youtu.be/LOzLBD4uyck?t=371)). No exit rule.

## Position sizing and risk

- C3: "i would recommend paper trading these indicators before you actually put in real trades" ([DBSI tutorial (C3), 2021-05-07, 05:06](https://youtu.be/yUZWIkY_LZE?t=306)). Nothing else.

## Timeframes

- C3: 10m and 15m entries, 2H checks, and 1H/4H/6H "backup" ([DBSI tutorial (C3), 2021-05-07, 01:33](https://youtu.be/yUZWIkY_LZE?t=93); [DBSI tutorial (C3), 2021-05-07, 09:43](https://youtu.be/yUZWIkY_LZE?t=583)).
- CryptoKid: 30m, 1H, 2H, 3H, 8H, 12H and daily ([DBSI first look (CryptoKid), 2021-05-09, 02:03](https://youtu.be/LOzLBD4uyck?t=123) to [05:08](https://youtu.be/LOzLBD4uyck?t=308)).

## Macro context

- May 2021: both treat it as a bull market (C3: "we're in a bull market"; CryptoKid: heading back to all-time highs).

## Indicator settings stated (exact numbers: lengths, thresholds, colours meaning)

- DBSI entry threshold default 7. CryptoFace uses 14 ([DBSI tutorial (C3), 2021-05-07, 06:38](https://youtu.be/yUZWIkY_LZE?t=398)).
- Trend MA length settable (200 / 100 / 50) ([DBSI tutorial (C3), 2021-05-07, 07:08](https://youtu.be/yUZWIkY_LZE?t=428)).
- Range −5 to 28. Top = bear (red), bottom = bull ([DBSI tutorial (C3), 2021-05-07, 01:33](https://youtu.be/yUZWIkY_LZE?t=93); [DBSI first look (CryptoKid), 2021-05-09, 00:00](https://youtu.be/LOzLBD4uyck?t=0)).
- DBSI values on regular candles differ from those on Heikin-Ashi ([DBSI tutorial (C3), 2021-05-07, 04:04](https://youtu.be/yUZWIkY_LZE?t=244)).
- "it works on all asset classes" (C3) ([DBSI tutorial (C3), 2021-05-07, 01:02](https://youtu.be/yUZWIkY_LZE?t=62)).

## Calibration numbers and falsifiable side-claims (win rates, base rates, "X happens N% of the time")

- C3: "we've been using it the last 24 hours and been crushing it on some trades" ([DBSI tutorial (C3), 2021-05-07, 02:03](https://youtu.be/yUZWIkY_LZE?t=123); [DBSI tutorial (C3), 2021-05-07, 02:34](https://youtu.be/yUZWIkY_LZE?t=154)). A one-day anecdote.
- Falsifiable: a DBSI bull score ≥ 14 (with bear ≤ about 0) predicts an up move on the next candle(s) ([DBSI first look (CryptoKid), 2021-05-09, 00:31](https://youtu.be/LOzLBD4uyck?t=31)).

## Dated calls (for later scoring)

| Video date | Asset | Direction | Level / target / invalidation | Horizon stated | Citation |
|---|---|---|---|---|---|
| 2021-05-07 | ETH (C3) | Long ("still looking for more gains"; 15m DBSI alert fired at 17, the 18 bear number dropped to −3) | none | not stated | [DBSI tutorial (C3), 2021-05-07, 09:12](https://youtu.be/yUZWIkY_LZE?t=552) |
| 2021-05-09 | BTC (implied by "the 60 000 mark"; the chart asset is not named on audio) (CryptoKid) | Long ("heading on one up to all-time highs again") | "getting over the 60 000 mark" | "over the next couple days" | [DBSI first look (CryptoKid), 2021-05-09, 05:39](https://youtu.be/LOzLBD4uyck?t=339) |

## Candidate rules NOT in the pre-registered setup grader

1. **DBSI bull ≥ 14 with bear falling** as an entry, and its timing (alert intrabar, decide at close). This needs the vendor's DBSI, which is closed-source and, per Gunslinger, shows only 24 bars of history: **needs data we do not have**. The terminal's DBSI-style rebuild can only proxy it, and any result is about the proxy.
2. **Band-cross rule.** Bear score falling and bull score rising over 2+ bars ("top numbers decreasing, bottom increasing"). Same data caveat.
3. **Heikin-Ashi agreement.** Require the same signal on HA and regular candles. Computable for any indicator the terminal can run on HA bars (as a generic filter). For DBSI itself, same caveat.

## Not usable / vague

- "36 different indicators" and "built off a trading bot": vendor marketing, not checkable.
- "A nice fat VWAP": no threshold.
- Both are first-48-hours impressions ("we're still learning this one").
