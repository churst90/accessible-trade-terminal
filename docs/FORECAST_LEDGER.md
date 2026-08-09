# Forecast ledger — dated public calls, and how they resolved

A scoreboard for public forecasters whose method this project has digested. **A method is worth
adopting only if its dated calls resolve better than the base rate.** Findings documents test the
*mechanism*; this file tests the *person*. Both matter, and they can disagree — a sound framework
can belong to someone with a poor record, and a good record can come from calls that cost nothing
to make.

**Rules of this ledger.**
1. A prediction is recorded only with a **date made**, a **resolution criterion**, and a **deadline**.
   If it cannot be scored, it goes in "unfalsifiable" and is not counted.
2. The **base rate is recorded alongside the call.** "The S&P will correct 10–20% at some point in
   the next four months" is not skill if it happens in most four-month windows.
3. Resolve from data in `strategy-lab-data/`, and name the file. Never resolve from memory.
4. Resolve honestly and on time, including the ones that were right.

Existing scored record elsewhere: **Cosasverdes / Tim** — two dated BTC calls, both failed badly;
see the `cosasverdes-forecast-record` memory and `docs/FIB_GANN_FINDINGS.md`.

---

# Benjamin Cowen (Into The Cryptoverse)

Source: 22 videos, **2026-06-25 → 2026-08-06**, transcripts pulled 2026-08-06. Method write-up and
the tests of his mechanism: `docs/CYCLE_FINDINGS.md` (addendum).

**Market state when these calls were made:** BTC ATH close **$124,728 on 2025-10-06**; 2026 low so
far **$59,100** (2026-06-05 intraday, `bitstamp_BTC_USDT_1d.json`); BTC ~$63.5k on 2026-08-03. Gold
peaked **$5,597 on 2026-01-29**, 2026 low so far **$3,949** on 2026-06-30, ~$4,105 on 2026-07-10
(`twelvedata_XAU_USD_1d.json`). S&P at new all-time highs, **7,750 on 2026-08-04**. Fed funds
**3.75%**, held on 2026-07-29. Headline CPI **3.5%**, core **2.6%**. 10y **4.6%**, 30y through 5.2%.

## A. Already resolved inside the sample window

Six calls made and resolved between 2026-06-25 and 2026-08-06. **All six correct.**

| # | Made | Call | Outcome | Base rate — was it cheap? |
|---|---|---|---|---|
| A1 | 06-30 | Shallow S&P correction in June, ~5–8%, *not* a major top | ~5% drawdown, then recovery | **Cheap.** A 5% pullback happens in most quarters |
| A2 | 06-30, 07-19 | S&P rallies through July to new all-time highs into August | 7,750 ATH on 08-04 | **Cheap.** July is green in the S&P every year since 2015 |
| A3 | 06-25, 07-01 | BTC forms its summer low late June / early July | Low 07-01 at ~$57.7k (his index) | **Moderate.** Called before the fact, to the week |
| A4 | 07-01 | BTC counter-trend rally through July | July closed ~+10.5% | **Moderate.** July green in 2 of 3 prior midterms |
| A5 | 07-28 | Fed holds on 07-29 rather than hiking | Held at 3.75% | **Cheap.** Market-implied ~70% hold |
| A6 | 07-28 | If they hold, the 30y breaks 5.2% and long yields revolt | 30y broke out same day | **Genuinely good.** Conditional, dated, mechanistic, and it fired |

**Reading it honestly: 6/6 is a real hot streak, and four of the six were high-base-rate calls.**
A6 is the one with actual information content — it was conditional, specified the instrument and
the level, and resolved within 24 hours. A3 is the second best.

## B. Open — BITCOIN

| # | Made | Call | Resolution criterion | Deadline |
|---|---|---|---|---|
| B1 | 08-06 | **BTC cycle bottom between late September and mid-December 2026, October most likely** | Lowest daily close in `bitstamp_BTC_USDT_1d.json` after 2026-08-06, confirmed by a subsequent 50%+ recovery | 2027-06-30 to confirm |
| B2 | 08-03 | **August 2026 is a red month for BTC** (prior midterm Augusts: −15%, −15%, −18%, −5%) | Aug close vs Jul close | 2026-09-01 |
| B3 | 07-24, 08-03 | BTC resolves the 200-week MA / bear-market-resistance-band squeeze **downward**, not upward | First weekly close either <200WMA−5% or >BMR+5% | 2026-12-31 |
| B4 | 08-03 | BTC trades **below $60,000** again | Any daily close < 60,000 | 2026-12-31 |
| B5 | 06-25 | **MVRV Z-score goes below 0** before the bottom (was 0.251) | `xs_coinmetrics_btc_capmvrvcur_1d.json` + `capmrktcurusd` + `splycur`. Z = (market cap − realized cap) ÷ sd(market cap); realized cap = market cap ÷ MVRV | 2026-12-31 |
| B6 | 06-25 | BTC trades **below the realized price** (~$53k at the time) | realized price = (market cap ÷ MVRV) ÷ supply, same three feeds. **At the 2026-04-07 cutoff this computes to ~$54,100**, matching his on-screen ~$53k | 2026-12-31 |
| B7 | 06-25 | A **volume capitulation spike** marks the bottom (volume low → sharp spike) | Daily volume > 5× its trailing 90-day median within 10 days of the eventual low | 2027-06-30 |
| B8 | 06-25 | Balance price ~$38k = full price capitulation. **Explicitly not promised** — "get off my back" | Any daily close < 40,000 | 2026-12-31 |
| B9 | 07-14 | The 2018 fractal **breaks**, because 2026 bottoms *earlier* than 2018 did (Dec 2018) | B1's date is before 2026-12-15 | 2026-12-31 |
| B10 | 07-16 | Social interest bottoms within 6–12 months | His proprietary index — **unscoreable externally** | — |

## C. Open — ETHEREUM

| # | Made | Call | Resolution criterion | Deadline |
|---|---|---|---|---|
| C1 | 07-20 | ETH's **biggest window of weakness is Aug–Oct 2026** | Lowest close of 2026 H2 falls in Aug–Oct | 2026-12-31 |
| C2 | 07-20 | ETH falls **40–80% from its July 2026 high** — his own centre is nearer 40–50%, "somewhere in between" | Min close after the July high vs that high, `bitstamp_ETH_USDT_1d.json` | 2026-12-31 |
| C3 | 07-20 | ETH sweeps the **April 2025 low** and goes slightly below | Any close below that level | 2026-12-31 |
| C4 | 07-20 | **ETH/BTC prints one more macro low**, possibly sweeping the June 2025 low | ETH/BTC ratio from the two daily files | 2027-03-31 |
| C5 | 07-20 | ETH/BTC stays rejected at the **20-month MA** until 2027 | No monthly close above it before 2027-01-01 | 2026-12-31 |

*Note C2 is the weakest-form prediction in the whole set: a 40–80% range is a 2× spread, and he says
"somewhere in between". That is the Cosasverdes failure mode — a forecast covering every path.*

## D. Open — GOLD & SILVER

| # | Made | Call | Resolution criterion | Deadline |
|---|---|---|---|---|
| D1 | 07-13, 07-22 | **Gold bottoms between July and October 2026** | Lowest close of 2026 in `twelvedata_XAU_USD_1d.json` falls in that window | 2027-03-31 |
| D2 | 07-22 | Gold reaches or nearly reaches its **bull-market support band** (20m SMA / 21m EMA, ~$3,800) — about −4% from $4,000 | Any close within 1% of that band | 2026-12-31 |
| D3 | 07-13 | Gold's bull market **resumes into 2027** and runs to the end of the decade | New all-time high above $5,597 | 2027-12-31 for the first leg |
| D4 | 07-22 | **Silver underperforms gold until gold bottoms**, then leads | Gold/silver ratio direction around D1's date | 2027-03-31 |

## E. Open — S&P 500 & MACRO

| # | Made | Call | Resolution criterion | Deadline | Base rate |
|---|---|---|---|---|---|
| E1 | 08-04 | **10–20% S&P correction starting mid-Aug to late Sept 2026**, likely around the 09-16 FOMC; it *starts* then, it does not *finish* then | Peak date and depth in `twelvedata_SPY_1d.json` | 2026-12-31 | **11 of 19 years had a 9–25% drawdown; ~1/3 of years peak Jul–Oct.** Close to a coin flip |
| E2 | 08-04 | Possible retest of the long-term trendline ~6,000 (≈ −23%) — flagged as possible, **not** base case | SPY equivalent level | 2026-12-31 | Rare |
| E3 | 07-29, 08-04 | **The Fed HIKES once in 2026** — September most likely, else October or December | FOMC decisions | 2026-12-31 | Market-implied ~57% for Sept at the time. **This is his boldest call** |
| E4 | 07-29 | **10y yield returns to its October 2023 high (~5%)**; TLT sweeps its Oct 2023 lows | The **TLT half is scoreable now** — `yahoo_TLT_1d.json` runs to 2026-07-24. The yield level itself needs FRED `DGS10` | 2026-12-31 | Directional, was 4.6% |
| E5 | 06-28, 08-04 | **DXY rises to 105–106** later in 2026, tracking Trump term 1 | **The one genuinely missing feed.** Needs FRED `DTWEXBGS` (broad dollar) or a DXY source | 2026-12-31 | Directional |
| E6 | 07-15 | Oil's bounce is a backtest; energy stays an inflation risk into H2 | `yahoo_XLE_1d.json` and `yahoo_USO_1d.json` both exist | 2026-12-31 | Weak form |

## F. Unfalsifiable — recorded, not counted

- "Total crypto market cap to **$10 trillion, plus or minus a few trillion**" — no date, and the
  error band is ±30%. This is his sign-off line, and it is not a forecast.
- "The biggest critics of the four-year cycle will become its biggest cheerleaders in a couple of
  months" — social, not market.
- "The asset class stays below the fair-value logarithmic regression trend line for the rest of the
  year" — the trend line is proprietary and monotonically increasing, so it is close to self-fulfilling.

---

## How to resolve this

**Corrected 2026-08-08.** An earlier draft of this file claimed six calls were blocked on missing
data. That was wrong and the error is recorded here rather than silently fixed, because "we have no
feed for that" is exactly the kind of claim that quietly kills a study.

**Already in the snapshot:** MVRV and realized price (`xs_coinmetrics_btc_capmvrvcur_1d.json`,
`_capmrktcurusd_`, `_splycur_` — 2015-01-01 → **2026-04-07**, refreshed with `dotnet run --
coinmetrics`, community tier, no API key); TLT (`yahoo_TLT_1d.json` → 2026-07-24); XLE, USO, GLD,
SLV; SPY, QQQ, gold, and the crypto majors.

**Genuinely missing: one feed.** A dollar index for **E5**, and the 10-year *yield level* for the
first half of **E4**. FRED covers both (`DTWEXBGS`, `DGS10`) and the FRED path already exists for
`events_*.json` — see `MacroEventCommand`. Until then E5 resolves "unscoreable", which is a fair
outcome and must be recorded as such rather than dropped.

**The binding constraint is snapshot freshness, not feed coverage.** Crypto ends **2026-06-15**,
SPY/gold **2026-07-09/10**, on-chain **2026-04-07**. Nothing after those dates has been verified
here — including the July BTC low and the July rally, which are taken from Cowen's own on-screen
figures and are *not* independently confirmed.

**Next review: 2026-09-01** (resolves B2), then **2026-10-01**, then **2027-01-05** for the bulk.
Refresh the snapshots first; the resolution procedure is Task 2 in `docs/RESEARCH_QUEUE.md`.
