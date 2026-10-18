# A cycle-low indicator, designed from scratch

Written 2026-10-18, before any test. Cody's question: "how would YOU design a cycle indicator that
can do what the CF indicator does, from scratch… sentiment too?" This is the answer. It is a
design only: no part of it has been computed against prices. The tests are fixed in
`docs/CYCLE_SCHOOL_PREREG.md`, and everything numeric here is frozen there.

Background: `docs/CAMEL_METHOD.md` (what Camel does, and what is known about his indicator).

## 1. What it has to do, and what it deliberately does not

The CF indicator gives a trader four things (CAMEL_METHOD section 8): a **pending** warning that a
low may be forming, a **confirmed** low label that comes after the low, **cancelled** and
**updated** states when a confirmed low breaks, and a **grade** for how strong the low is. Camel
uses it to check his own real-time call.

This design gives the same four outputs:

| CF output | Ours |
|---|---|
| ⚠️ Pending DCL | **ALERT**: the capitulation score is at an extreme while a low is due |
| ✅ Confirmed DCL | **CONFIRMED**: a swing low, the score turning back from its extreme, and a close above the low bar's high, all within a fixed number of bars (lagged, like CF's) |
| ❌ Cancelled | **CANCELLED**: a close below the confirmed low |
| 🔄 Updated | **UPDATED**: the next anchor low after a cancel, confirmed by the same rule |
| High / Mid / Low confluence | **GRADE**: the score's own percentile at the low (high, mid, low) |

**What it does not do, on purpose.**

- **It does not project cycle windows or cycle lengths.** The lab showed that the length of a
  "cycle" comes from the swing detector, not the market (`docs/CYCLE_FINDINGS.md`). A future-window
  box would draw a detector property as if it were a forecast. "A low is due" is kept only as a
  minimum spacing between alerts, and the pre-registration tests whether it adds anything.
- **It does not label translation.** Translation is momentum in cycle words
  (`docs/TRANSLATION_FINDINGS.md`).
- **It fits nothing.** CF is tuned per asset over "tens of thousands of different settings". Every
  weight here is equal, every threshold is fixed in advance, and the same rules run on every asset.
  An indicator tuned to its own history cannot be checked by its own backtest.
- **It is not Market Cipher and not Tim's structure method.** Nothing from either school is an
  input (Cody, 2026-10-18: test each school separately). DSS Bressert is used because it belongs to
  the cycle school.

## 2. The idea in plain words

Market lows of any size tend to come when many unrelated things are washed out at once: price has
fallen a long way and fast, volatility is high, traders have given up on being long, surveys are
fearful, on-chain holders are underwater, and the macro and credit backdrop is under stress. No
single one of those times a low; most of them are high for weeks. The design asks a narrower
question: **is the washout unusually broad right now, by this asset's own history?**

So each input is turned into "how extreme is this today compared with everything this input has
done before today", a percentile from 0 to 100. Those are averaged within a family of similar
inputs, and the families are averaged into one **capitulation score** from 0 to 100. A high score
is a broad washout. An extreme score while a low is due raises the **alert**. The low is
**confirmed** later, by price, the way CF's label is.

Equal weights are a choice, not an assumption that every input is equally good. They are the one
weighting that cannot be fitted to the past.

## 3. How every metric is made comparable

**Causal, as known then.** At daily bar *i* (decided at its close, acted on at bar *i*+1):

- A price-derived metric uses bars up to and including *i*. A weekly metric uses completed weeks
  only: a week counts on bar *i* if its last daily bar is on or before *i*. Weekly bars are built
  from daily bars in the lab harness, never through the app's higher-timeframe service (it treats a
  week as closed once it has opened; see `docs/SETUP_GRADER_PREREG.md` section 2.2).
- An external value is usable on bar *i* if its known-from date is on or before the date of bar
  *i* (section 5 lists the rule for each source). **One exception:** the Fed's weekly H.4.1 data
  (WALCL, WTREGEN) is released at 4:30 pm Eastern, after the US equity close, so on **equity** bars
  it needs known-from strictly before the bar's date. Bitcoin's UTC day closes at 8 pm Eastern, so
  for Bitcoin the normal rule holds.
- Revised series (payrolls, unemployment, claims, Michigan sentiment, the Sahm rule) are read from
  their ALFRED vintages through `MacroPointInTime.AsOf`, as published on that day, never from the
  latest values.
- **Staleness.** A value older than its staleness limit (section 5) is undefined, not carried
  forward. This stops a feed that ended (Cboe put/call ends 2019-10-04, the Fear and Greed snapshot
  ends 2026-04-09, funding ends 2026-04-01) from being held at its last value for years.

**Oriented.** Every metric is signed so that **a higher value means more washed out** (closer to a
capitulation). Section 5 gives the sign for each.

**The percentile.** For metric *m* with oriented value *x(i)*:

    pct_m(i) = 100 × (number of known past values x(k), k ≤ i, with x(k) < x(i)
                     + half the number with x(k) = x(i)) ÷ (number of known values up to i)

It is an **expanding window**: every value the metric has had, from its first defined day up to and
including today. It is undefined until the metric has **252 defined daily values** (104 for a weekly
metric, 36 for a monthly one). The window never looks forward, so a value is never ranked against
its own future. Ranking against the full sample, which is what a published chart shows, gave 2.3
times the honest spread in an earlier test here (ledger row 24); the expanding window is that
lesson.

A metric with a native weekly or monthly cadence enters the daily percentile at its own cadence:
its percentile is computed over its own release history, then held on daily bars until the next
release or until it goes stale.

## 4. The score, the alert, the confirmation and the grade

### 4.1 Capitulation score

- **Family score** F_f(i) = the mean of pct_m(i) over the metrics *m* in family *f* that are
  defined on bar *i*. A family is defined if at least one of its metrics is.
- **Capitulation score** CAP(i) = the mean of F_f(i) over the families defined on bar *i*. CAP is
  defined only if **at least 3 families** are defined.
- **Price-only score** CAP_P(i) = F_P(i), the price family alone. It is the baseline the full score
  must beat: if adding sentiment, positioning, on-chain and macro data does not improve on price
  alone, they are not earning their place.

Averaging squeezes the score toward 50: a mean of seven percentiles rarely reaches 90. So the
alert is set on the score's **own** expanding percentile, not on its raw value:

- pctCAP(i) = the expanding percentile of CAP(i) among CAP's own earlier values (needs 252 defined
  CAP values). pctCAP_P(i) is the same for CAP_P. This makes the full and the price-only score fire
  at about the same rate, which is what lets them be compared fairly.

### 4.2 The reference lows used by the indicator itself

The indicator needs to know when the last confirmed low was, for the "low due" rule. It uses the
same fixed price rule the tests use as their reference (`CYCLE_SCHOOL_PREREG.md` section 3), the
**reference low**:

- Bar *j* is a reference low if low(*j*) is the lowest low of bars *j*−20 to *j*, and, before any
  later bar trades below low(*j*), some later bar closes at least 3 × ATR(14) (measured at bar *j*)
  above low(*j*). It becomes known at that close, the **confirmation bar** c(*j*). Before c(*j*)
  the indicator does not know it exists.

### 4.3 ALERT (pending)

On bar *i*, ALERT fires if all of these hold:

1. pctCAP(i) ≥ 95;
2. **a low is due**: the number of bars since the most recent reference low known on bar *i* is at
   least 0.75 × the median spacing between that asset's earlier known reference lows (needs at least
   4 earlier spacings; with fewer, this condition is treated as met);
3. no ALERT fired in the previous 20 bars.

The "due" rule is a minimum spacing and nothing more. Because the spacing of reference lows is set
by the rule's own parameters, this condition cannot carry a market cycle; it only stops an alert
right after a confirmed low. The pre-registration runs the alert with and without it.

### 4.4 CONFIRMED, CANCELLED, UPDATED

On bar *i*, take the anchor *j* = the bar with the lowest low among bars *i*−10 to *i*−1. CONFIRMED
fires on bar *i* if all of these hold:

1. **swing low**: low(*j*) is the lowest low of bars *j*−20 to *i* (nothing lower since);
2. **washout at the low**: the highest pctCAP over bars *j*−5 to *j* is at least 90;
3. **the score has turned**: pctCAP(i) is at least 15 points below the highest pctCAP over bars
   *j*−5 to *i*;
4. **price confirms**: close(*i*) > high(*j*);
5. this anchor *j* has not already been confirmed.

Only the first qualifying bar confirms a given anchor. After that:

- **CANCELLED** fires on the first later bar that closes below low(*j*).
- **UPDATED** is simply the next CONFIRMED on a new anchor after a cancel. It needs no rule of its
  own.

This is lagged by construction: it cannot fire until price has closed above the low bar's high,
which is what CF's label waits for ("the first big green candle will trigger the indicator").

### 4.5 GRADE

At a CONFIRMED low, the grade is the highest pctCAP over bars *j*−5 to *j*: **high** at 97 or more,
**mid** at 93 up to 97, **low** below 93. The grade is descriptive only: it is reported with every
confirmation and is not one of the registered tests.

### 4.6 Two degrees

The same score serves two sizes of low:

- **daily degree** (primary): the rules above;
- **weekly degree** (secondary): the reference low uses the lowest low of 130 bars and a 6 × ATR(14)
  rally, and the anchor window for CONFIRMED is bars *i*−30 to *i*−1 with the swing-low test over
  *j*−130 to *i*. Everything else is unchanged.

### 4.7 What it would say aloud

The terminal is used by ear, so the readout is part of the design. One line per event, families in
a fixed order, numbers rounded:

- "Capitulation 96, pending low. Price 98, volatility 94, positioning 71, sentiment 90, on-chain 88,
  macro 62."
- "Low confirmed at 61,840, grade high. Cancelled on a close below 61,840."

This is a note for a later build, not part of the test. The indicator should not reach the app's
indicator set unless the pre-registered tests pass.

## 5. The metrics, family by family

Every file is under `strategy-lab-data/` (gitignored). Known-from rules are as listed; "date" means
the file's own `known_from` column unless stated. Orientation: **+** means a higher raw value is
more washed out, **−** means a lower raw value is.

### 5.1 Price and time (P): own asset, several lengths

| Id | Metric | Sign | Known from | Staleness |
|---|---|---|---|---|
| P1 | drawdown of close from its highest close of the last 63 bars | more negative = washed (−) | bar close | n/a |
| P2 | drawdown of close from its highest close of the last 252 bars | − | bar close | n/a |
| P3 | RSI(14), daily | − | bar close | n/a |
| P4 | RSI(14) of completed weekly closes | − | completed week | n/a |
| P5 | DSS Bressert (10, 9, 5) DSS line, daily, from `DssBressertProvider` | − | bar close | n/a |
| P6 | DSS Bressert (10, 9, 5) on completed weekly bars | − | completed week | n/a |
| P7 | (close − SMA(200)) ÷ SMA(200) | − | bar close | n/a |

Price files: `bitstamp_BTC_USDT_1d.json` (2011-08-18), `bitstamp_ETH_USDT_1d.json` (2017-08-16),
`yahoo_SPY_1d.json` (1993-01-29), `yahoo_QQQ_1d.json` (1999-03-10), `yahoo_DIA_1d.json`
(1998-01-20), `yahoo_IWM_1d.json` (2000-05-26), `yahoo_GLD_1d.json` (2004-11-18). Crypto ends
2026-06-15, equities and GLD 2026-07-24.

### 5.2 Breadth (B): how many of a peer group are washed out

| Id | Metric | Sign | Universe | Defined |
|---|---|---|---|---|
| B1 | share of the universe with close > SMA(50) | − | see below | ≥ 4 members with 60 bars |
| B2 | share of the universe with RSI(14) < 30 | + | see below | same |

- **US equities** (for SPY, QQQ, DIA, IWM): the nine sector SPDRs (`yahoo_XLB`, `XLE`, `XLF`,
  `XLI`, `XLK`, `XLP`, `XLU`, `XLV`, `XLY`, all from 1998-12). Sector ETFs do not suffer from
  survivorship the way a list of today's stocks would. B2 is the measure Camel cites ("the share of
  stocks with RSI below 30").
- **Crypto** (for BTC, ETH): the eight Bitstamp daily coins (BTC, ETH, LTC, XRP, BCH, ADA, SOL,
  DOGE). This list is survivorship-biased (coins still listed today), and is defined only from about
  2017.
- **Gold**: no peer group in the lab. Undefined.

### 5.3 Volatility (V): own asset

| Id | Metric | Sign |
|---|---|---|
| V1 | standard deviation of the last 20 daily log returns | + |
| V2 | that standard deviation over 10 days ÷ over 60 days | + |

Known from the bar close. VIX is in the macro family, because for Bitcoin and gold it is outside
stress, not their own volatility.

### 5.4 Positioning (Q)

| Id | Metric | Sign | File | Known from | Staleness |
|---|---|---|---|---|---|
| Q1 | mean of the last 21 eight-hour funding prints (BTC, ETH) | − | `xs_binancevision_{btc,eth}usdt_funding_8h.json` (2020-01 to 2026-04-01) | print time | 3 days |
| Q2 | 30-day % change in open interest (BTC, ETH) | − | `xs_binancevision_{btc,eth}usdt_oi_1d.json` (from 2020-11; the lab notes say usable from 2021-12) | point date + 1 day | 3 days |
| Q3 | CFTC net managed-money positioning as % of open interest | − | BTC: `xs_cftc_bitcoin_cot_1w.json` (2017-12); SPY, DIA, IWM: `xs_cftc_sp500_cot_1w.json` (2006-06); QQQ: `xs_cftc_nasdaq_cot_1w.json`; gold: `xs_cftc_gold_cot_1w.json` (2013-01) | the Friday release: read through the lab cache, which moves Tuesday-stamped files to their release date (CHANGES 2026-10-18); `sp500_cot` and `nasdaq_cot` are already on release dates | 21 days |

**Unverified, check before use:** the COT downloader parses the CFTC *disaggregated* report, which
does not normally carry financial futures (S&P, Nasdaq, Bitcoin are in the Traders in Financial
Futures report). Stage 2 must confirm which report each `xs_cftc_*` file came from and what its
value means before Q3 is used for them. If a file cannot be confirmed, Q3 is undefined for that
asset, and that is reported.

### 5.5 Sentiment (S)

| Id | Metric | Sign | File | Known from | Staleness |
|---|---|---|---|---|---|
| S1 | AAII bulls minus bears (equities) | − | `macro/aaii/aaii_bull_minus_bear.csv` (1987-07) | date (Thursday, Friday before the 1990s) | 14 days |
| S2 | Cboe equity put/call, 10-day mean (equities) | + | `macro/cboe/cboe_equity_pc_stitched.csv` (2003-10-17 to 2019-10-04) | date | 5 days |
| S3 | Michigan consumer sentiment, as published (equities) | − | `macro/alfred/UMCSENT_vintages.csv` (vintages from 1998-07) | realtime_start | 75 days |
| S4 | Crypto Fear and Greed (BTC, ETH) | − | `xs_alternativeme_fng_index_1d.json` (2018-02-01 to 2026-04-09) | point date + 1 day | 3 days |

Gold has no sentiment metric in the lab. Michigan sentiment on FRED is mostly the final monthly
reading, posted near month end (`docs/MACRO_DATA.md` trap 8); it is used as published.

### 5.6 On-chain (C): Bitcoin, and Ethereum's MVRV

| Id | Metric | Sign | File | Known from |
|---|---|---|---|---|
| C1 | MVRV (market value ÷ realized value) | − | `macro/coinmetrics/btc_capmvrvcur.csv` (2010-07); ETH: `xs_coinmetrics_eth_capmvrvcur_1d.json` | date (UTC day + 1) |
| C2 | Puell multiple: daily issuance in USD ÷ its mean over the last 365 days | − | `macro/coinmetrics/btc_isstotusd.csv` (2010-07) | date |
| C3 | hash ribbon: 30-day mean hash rate ÷ 60-day mean hash rate | − | `macro/coinmetrics/btc_hashrate.csv` (2009) | date |

Staleness 3 days. **Realized price is not a separate metric**: price ÷ realized price equals MVRV by
definition (realized price = market cap ÷ MVRV ÷ supply), so adding it would count MVRV twice. The
on-chain history from 2010 lets these percentiles warm up before Bitstamp's price bars start, which
is legitimate: each metric is ranked only against its own past.

### 5.7 Macro and liquidity (M): US, for every asset

| Id | Metric | Sign | File | Known from | Staleness |
|---|---|---|---|---|---|
| M1 | curve steepening: T10Y3M today minus its value 182 days earlier | + | `macro/fred/T10Y3M.csv` (1982) | date | 7 days |
| M2 | credit stress: BAA10Y today minus its value 91 days earlier | + | `macro/fred/BAA10Y.csv` (1986) | date | 7 days |
| M3 | dollar: 91-day log change of the broad dollar, chained from `DTWEXM.csv` (to 2005-12-30) and `DTWEXBGS.csv` (from 2006-01-02) | + | `macro/fred/` | date (DTWEXBGS has ALFRED release dates from 2019) | 10 days |
| M4 | net liquidity: 91-day % change of WALCL − WTREGEN − 1000 × RRPONTSYD (WALCL and WTREGEN are in millions, RRPONTSYD in billions) | − | `macro/fred/WALCL.csv` (2002-12), `WTREGEN.csv`, `RRPONTSYD.csv` | the latest of the three dates; on equity bars strictly before the bar date | 14 days |
| M5 | policy: the fed funds target today minus 182 days earlier (DFEDTAR to 2008-12-15, then the midpoint of DFEDTARU and DFEDTARL) | − (cuts = washed) | `macro/fred/DFEDTAR*.csv` | date | 7 days |
| M6 | VIX close | + | `macro/fred/VIXCLS.csv` (1990) | date | 7 days |
| M7 | Sahm rule, real time | + | `macro/alfred/SAHMREALTIME_vintages.csv`; before its first vintage (2019-09-06) each month is known from the first-print release date of UNRATE for the same month (MACRO_DATA trap 4) | realtime_start | 75 days |
| M8 | jobless claims: 4-week mean ÷ the lowest 4-week mean of the previous 52 weeks, minus 1 | + | `macro/alfred/ICSA_vintages.csv`; **undefined before 2009-05-28** (no earlier vintage) | realtime_start | 14 days |
| M9 | payrolls: mean of the last 3 monthly changes, as published that day | − | `macro/alfred/PAYEMS_vintages.csv` (vintages from 1955) | realtime_start | 75 days |

Rules for M:

- **RRP gaps**: RRPONTSYD has rows only on days with an operation. On a day with no row, use the
  last row if it is at most 7 days old, else 0.
- **Not used, and why**: the ICE high-yield spread (only from 2023-10-17, MACRO_DATA trap 1);
  NFCI, ANFCI and STLFSI4 (revised in full every week, so not point in time, trap 7); USREC and
  USRECD (rewritten after the fact, trap 3); M2 growth (its sign at lows is ambiguous: money growth
  jumps at the start of QE, which is at or after the low).
- **Orientation is a choice, and two are ambiguous.** Net liquidity (M4) is signed "draining =
  washed out". But in 2009 and 2020 the Fed's balance sheet jumped right at the low, which would
  push M4 the other way. The policy rate (M5) is signed "cuts = washed out", which is right for
  2001, 2008 and 2020 and says nothing about a low inside a hiking cycle (2022). These signs are
  frozen now and may not be flipped after a test.
- **Many macro series are coincident or lagging.** Unemployment peaks after equity lows, credit
  spreads at them, the curve steepens most after them. So M is expected to be high around and after
  big lows, not before. That is why the alert window allows a low up to 5 bars before the alert.

### 5.8 Which families each asset gets

| Asset | Families | Notes |
|---|---|---|
| BTC | P, B, V, Q, S, C, M | B from about 2017, S from 2018, Q funding from 2020, OI from late 2021, CME COT from 2017-12 |
| ETH | P, B, V, Q, S, C (MVRV only), M | replication asset for crypto |
| SPY, QQQ, DIA, IWM | P, B, V, Q, S, M | S2 ends 2019-10; Q3 needs the report check |
| GLD | P, V, Q, M | no breadth, no sentiment |

The mix of families changes over time as feeds start and stop. That makes CAP's meaning drift: early
Bitcoin is a four-family score, late Bitcoin a seven-family one. The pre-registration requires a
timeline of which families were defined, per asset, beside every result.

## 6. Honest note on independence

Most of these inputs measure one thing: **the price has fallen a lot, fast**. In particular:

- the whole price family; volatility (it rises in sell-offs); breadth (peers fall together);
- Fear and Greed (built partly from price momentum and volatility);
- MVRV and the Puell multiple (the Puell multiple is close to price ÷ its one-year mean, with steps
  at halvings);
- AAII and funding (both follow recent returns); COT managed money (trend followers); VIX; credit
  spreads (they co-move with equity drawdowns).

The inputs that are not mechanically tied to recent price are few: the hash rate (partly: miners
switch off when price falls), the labour data, the policy rate, the curve and net liquidity, and
those are slow and often lag the low. **A realistic count of independent families is two or three,
not seven**: a "price washout" cluster, an "economy" cluster and perhaps a "plumbing" cluster
(liquidity and the dollar).

What this means:

- Equal weighting across families gives the price cluster most of the vote anyway. The score will
  look a lot like the price-only score, and the test of whether the extra data adds anything (the
  comparison with CAP_P) is the one that matters.
- The ledger already says sentiment is redundant with an oscillator buy at a bottom (row 18), and
  on-chain valuation is exposure to the bottom rather than timing (row 10). The prior for this
  design beating price alone is low, and it is stated here so a null is not a surprise.
- The pre-registration requires a correlation matrix of the family scores and an effective number of
  families (the participation ratio of that matrix's eigenvalues) for the development era. They are
  reported only. Weights may not be changed because of them.

## 7. What it needs that the lab lacks

None of these is used. Each would need its own dated pre-registration before being fetched:

- high-yield credit spreads before 2023-10 (ICE licence);
- fed funds futures probabilities (CME FedWatch history);
- Cboe put/call after 2019-10-04 (one HTML page per day; Cody's decision is pending);
- money-market fund flows; options skew; the CFTC Traders in Financial Futures report (asset-manager
  and leveraged-fund positioning);
- constituent-level US breadth (advance-decline line, share of S&P stocks above their 200-day);
- paid on-chain data (SOPR, NUPL, exchange flows);
- Google Trends; JOLTS quits and temporary help services (on FRED, not fetched);
- a daily S&P 500 index before 1993.

## 8. Build notes for stage 2

- Build it as a StrategyLab command first (for example `cycle-school`), not as an app indicator.
  Read the macro CSVs with `MacroPointInTime`: `ParseKnownCsv` plus `LatestKnown` for
  `date,value,known_from` files, and `ParseVintageCsv` plus `AsOf` or `LatestKnown` for the ALFRED
  `_vintages.csv` files. The cross-series cache does not load `macro/` files.
- Use `DssBressertProvider` for P5 and P6 with (10, 9, 5); build weekly bars from daily bars.
- Fixed seeds from `StableSeed.From(...)` for anything random.
- Record the sha256 of every input file before and after the run.
