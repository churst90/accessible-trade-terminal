# Macro, Fed, liquidity and sentiment data for the lab

Added 2026-10-18 for the coming tests of Camel Finance's macro and cycle method and of a
from-scratch cycle-low indicator. Before this the lab had FRED release DATES only
(`fred_raw_*.json`, `events_*.json`), and no macro values at all.

- **Where:** `strategy-lab-data/macro/<source>/` (gitignored, like every snapshot). Each source has
  a `raw/` folder with exactly what the server sent, a `.meta.json` beside each raw file (URL,
  method and request, retrieval time, HTTP status, sha256), and normalized CSVs one level up.
- **Manifest:** `strategy-lab-data/macro/MACRO_MANIFEST.json`, copied into the repo as
  `AccessibleTrader.StrategyLab/Data/MACRO_MANIFEST.json`. Every raw and normalized file is listed
  with its source URL, retrieval time, sha256, size, rows, first and last date, point-in-time status,
  how `known_from` was set, and notes. It also lists what was not fetched and why, and the
  first-print cross-checks.
- **Command:** `StrategyLab macro-fetch [--snapshots ../strategy-lab-data] [--out <dir>]
  [--manifest <copy.json>] [--only fred,alfred,nber,aaii,cboe,coinmetrics,blockchain]`. Code in
  `AccessibleTrader.StrategyLab/Macro/`. The point-in-time logic is `MacroPointInTime.cs`, and its
  tests are `MacroPointInTimeTests` (23).
- **Size:** 31 MB in 332 files: 111 raw files (13 MB), 111 sidecars, 109 normalized CSVs (17.5 MB)
  and the manifest. Every raw file was retrieved on 2026-10-18 between 12:44 and 13:21 UTC.
- **No keys and no accounts.** Every source is public and keyless.

## Nothing already on disk is ever touched

A raw file that already exists is reused and never re-downloaded. Its sha256 is checked against its
sidecar first. A normalized file is rewritten only if the new bytes are identical. A refresh has to go
to a new `--out` folder, so the sample an old verdict was computed on survives. No file outside
`macro/` was written. A check after the fetch found no top-level file in `strategy-lab-data`
modified that day.

## The three file shapes

1. **`date,value,known_from`**: the value, and the first day it is safe to use. A blank `known_from`
   means there is no defensible release date, and the loader (`ParseKnownCsv`) turns it into "never".
2. **`period,value,realtime_start,realtime_end`** (`*_vintages.csv`): ALFRED's full real-time
   history. Each row says that this was the published number for that period from `realtime_start`
   through `realtime_end`, inclusive. A blank end means the number is still current, and "." means
   it was withdrawn.
3. **`period,first_value,first_release_date,latest_value,latest_vintage_date,versions,first_is_initial_print`**
   (`*_first_release.csv`): the first print, the current number, and how many versions there have
   been.

**The day convention.** `known_from` is the first calendar day by whose END the value was public
(US Eastern for US releases). Read it at bar i's close with `known_from <= date(i)` and act at bar
i+1, which is already the lab's rule. A study that acts at bar i's OPEN needs `known_from < date(i)`.
An 8:30 ET release (jobs, CPI) is usable on its own day. The Fed's 4:30 pm H.4.1 also counts as its
own day, but only at the close.

**As-of reads.** `MacroPointInTime.AsOf(rows, day)` gives the whole series as a reader saw it on
that day. `LatestKnown(rows, day)` gives the newest period they had. Neither ever serves a row before
its `realtime_start`; that is what the tests pin.

## The series

### Revised headline data, full vintage history (ALFRED; point in time, exact)

Folder `macro/alfred/`. Each series has `_vintages.csv`, `_first_release.csv` and a plain `.csv`.
The plain file holds the latest value, with `known_from` set to the day the CURRENT number was
published, so early rows show today's revised figures. Use the `_vintages` file for "as known then".

| Series | What | Observations | ALFRED vintages from |
|---|---|---|---|
| PAYEMS | Nonfarm payrolls | 1939-01 → 2026-09 | 1955-05-06 |
| UNRATE | Unemployment rate | 1948-01 → 2026-09 | 1960-03-15 |
| ICSA | Initial jobless claims, weekly | 1967-01-07 → 2026-10-10 | 2009-05-28 |
| CPIAUCSL | CPI, all items | 1947-01 → 2026-09 | 1972-07-21 |
| CPILFESL | Core CPI | 1957-01 → 2026-09 | 1996-12-12 |
| PCEPILFE | Core PCE (the Fed's target measure) | 1959-01 → 2026-08 | 2000-08-01 |
| INDPRO | Industrial production | 1919-01 → 2026-09 | 1927-01-26 |
| GDPC1 | Real GDP, quarterly | 1947Q1 → 2026Q2 | 1991-12-04 |
| M2SL | M2 money stock | 1959-01 → 2026-08 | 1980-02-08 |
| UMCSENT | Michigan consumer sentiment | 1952-11 → 2026-08 | 1998-07-31 |
| SAHMREALTIME | Sahm rule indicator (built from real-time data) | 1959-12 → 2026-09 | 2019-09-06 |
| USREC | NBER recession flag, monthly | 1854-12 → 2026-09 | 2014-09-18 |

**Cross-check:** ALFRED's own "initial release only" file was downloaded for each series and compared
with the first prints derived from the real-time history. All twelve agree row for row: 560, 857,
798, 907, 650, 357, 314, 1197, 139, 337, 84 and 145 periods, with no value or date different.

### Market rates, spreads and policy (FRED latest values; `known_from` from the schedule)

Folder `macro/fred/`. Market closes are given `known_from` = their own date.

- **Curve:**
  - DGS3MO (from 1981), DTB3 (from 1954, a longer front end), DGS2 (1976), DGS5 (1962), DGS10 (1962)
    and DGS30 (1977).
  - T10Y2Y (1976) and T10Y3M (1982): Camel's curve gates.
  - DFII10 (the real 10-year yield) and T10YIE (10-year breakeven inflation), both from 2003.
- **Policy:**
  - DFF (daily, from 1954) and EFFR (from 2000). Known the next business day plus a margin.
  - FEDFUNDS (monthly average). Known after the month ends.
  - DFEDTAR (the single target, 1982 → 2008-12-15), then DFEDTARU and DFEDTARL (the range, from
    2008-12-16). These are the easing-versus-tightening series.
- **Credit:**
  - BAMLH0A0HYM2 (high-yield spread) and BAMLC0A0CM (investment-grade spread). **Only from 2023-10-17:
    see the traps below.**
  - BAA10Y (from 1986) and AAA10Y (from 1983): Moody's spreads over the 10-year.
  - DBAA and DAAA: daily yields.
  - BAA and AAA: monthly from 1919, which covers every cycle on record.
- **Dollar:**
  - DTWEXBGS, the broad dollar, from 2006.
  - DTWEXM, the major-currencies dollar, 1973 → 2019 (discontinued). Splice the two over 2006 to 2019
    for a long history.
- **Liquidity:**
  - WALCL (Fed total assets, from 2002-12), WRESBAL (reserve balances, from 1984) and WTREGEN (the
    Treasury General Account, from 1986). All three are weekly, from the H.4.1 release.
  - RRPONTSYD (overnight reverse repo, daily from 2003).
  - Net liquidity = WALCL − WTREGEN − RRPONTSYD. Mind the units: WALCL and WTREGEN are in millions of
    dollars, RRPONTSYD is in billions.
- **Stress:**
  - VIXCLS (from 1990).
  - NFCI and ANFCI (from 1971).
  - STLFSI4 (from 1993).
- **USRECD** (the daily recession flag): stored with a blank `known_from`, so it is never served. See
  the traps below.

**Weekly and monthly releases use real release dates where ALFRED has them.** These ten series (FEDFUNDS,
BAA, AAA, DTWEXBGS, WALCL, WRESBAL, WTREGEN, NFCI, ANFCI and STLFSI4) take their `known_from` from
ALFRED rather than from a rule:

- **Eight have an `_initial.csv` file.** For FEDFUNDS, BAA, AAA, DTWEXBGS, WALCL, WRESBAL, WTREGEN and
  STLFSI4, the date ALFRED first published each observation is used as `known_from`. The first print
  of each observation is kept in `<ID>_initial.csv`.
- **NFCI and ANFCI use ALFRED's release dates.** ALFRED answered their initial-release download with
  HTTP 504, at both 800 and 100 vintages per request. Each observation instead takes the first release
  date after it, and never a date earlier than the schedule rule.
- **Before ALFRED's archive starts, the schedule rule applies.** The archive starts in 1996 for
  FEDFUNDS, BAA and AAA, 2019 for DTWEXBGS, 2011 for WALCL, 2004 for WRESBAL, 2008 for WTREGEN, 2011
  for NFCI and 2022 for STLFSI4.

A probe against ALFRED's release-date lists found **no observation in WALCL, WTREGEN, DTWEXBGS or NFCI
whose `known_from` is earlier than the first release that could contain it.** Before this change, the
schedule rule alone was early in 3 of 797 WALCL weeks, 7 of 930 WTREGEN weeks and 4 of 803 NFCI weeks.
All of those were holiday weeks.

### NBER turning points with announcement dates

`macro/nber/nber_turning_points.csv` has 69 peaks and troughs (1854 → 2020), each with the day NBER
announced it. All 12 turning points since 1980 matched an announcement. The seven committee memos
are listed too. How long each announcement took, counted from the 1st of the turning-point month:

| Turning point | Announced | Lag |
|---|---|---|
| Peak Jan 1980 | 1980-06-03 | 5 months |
| Trough Jul 1980 | 1981-07-08 | 12 months |
| Peak Jul 1981 | 1982-01-06 | 6 months |
| Trough Nov 1982 | 1983-07-08 | 8 months |
| Peak Jul 1990 | 1991-04-25 | 9 months |
| Trough Mar 1991 | 1992-12-22 | 21 months |
| Peak Mar 2001 | 2001-11-26 | 8 months |
| Trough Nov 2001 | 2003-07-17 | 20 months |
| Peak Dec 2007 | 2008-12-01 | 12 months |
| Trough Jun 2009 | 2010-09-20 | 15 months |
| Peak Feb 2020 | 2020-06-08 | 4 months |
| Trough Apr 2020 | 2021-07-19 | 15 months |

Turning points before 1980 have no announcement date. They were dated after the fact, and the committee
issued its first memo in 1979.

### Sentiment

- **AAII weekly survey** (`macro/aaii/`): bullish, neutral, bearish and bull-minus-bear, as fractions.
  2,045 weeks from 1987-07-24 to 2026-10-15. `known_from` is the sheet's reported date. That date is
  a Friday up to the early 1990s and a Thursday since. The file was read by a small built-in reader
  for the old .xls format (`LegacyXlsReader.cs`), because the lab has no spreadsheet package.
- **Cboe put/call ratios** (`macro/cboe/`):
  - Total from 1995-09-27, and equity and index from 2003-10-17. All end on 2019-10-04.
  - SPX from 2010, VIX from 2006 and exchange-traded products from 2006, also to 2019-10-04.
  - There is one normalized file per archive and column, plus a stitched total, equity and index file
    in which the later archive wins on overlapping days.
- **Crypto Fear and Greed** is already in the lab (`xs_alternativeme_fng_index_1d.json`): 2,986 days
  from 2018-02-01 to 2026-04-09, fetched 2026-04-09. It was not refreshed, because re-fetching it
  would change the sample of old verdicts. A refresh belongs in a new file.
- **Google Trends: not fetched.** There is no keyless route that keeps to Google's terms.

### On-chain (keyless)

- **CoinMetrics community API, BTC** (`macro/coinmetrics/`): CapMVRVCur, CapMrktCurUSD, PriceUSD and
  IssTotUSD from 2010-07-18, and HashRate, SplyCur and AdrActCnt from 2009. All run to 2026-10-17.
  The lab's existing `xs_coinmetrics_*` files start in 2015, so these add the 2011 and 2015 cycle
  lows.
- **Blockchain.com charts** (`macro/blockchain/`): miners' revenue, difficulty and hash rate from 2009
  to 2026-10-14.
- **Derivable cycle measures:**
  - Realized price = CapMrktCurUSD / CapMVRVCur / SplyCur.
  - The Puell multiple = IssTotUSD, or miners' revenue, divided by its 365-day mean.
  - Hash ribbons come from the hash rate.
- `known_from` is the observation date plus one day, because a UTC day is computed after it closes.
- **These need an account:** SOPR, realized cap as its own metric, NUPL, exchange flows (Glassnode,
  CryptoQuant), and CoinMetrics' paid metrics. CoinMetrics refuses a whole request (HTTP 403) if any
  one metric in it is paid.

## Traps a test must respect

1. **High-yield and investment-grade spreads exist only from 2023-10-17.** ICE's licence lets FRED
   serve just three years, and ALFRED has no older vintages. Camel's credit gate cannot be tested on
   ICE data over any cycle. The long-history substitutes are Moody's BAA10Y and AAA10Y, which are
   investment grade, not high yield. Say so in any finding that uses them.
2. **The 2025 government shutdown broke every schedule rule.**
   - September 2025 payrolls were first printed on 2025-11-20.
   - October 2025 payrolls never had a release of their own; they first appeared on 2025-12-16,
     together with November.
   - **October 2025 CPI does not exist.** CPIAUCSL's row for 2025-10 has the value "." (missing),
     posted on 2025-12-18.
   - GDP for 2025Q4 was first printed on 2026-02-20.
   - Only the vintage files have these dates right. A fixed "first Friday" or "about the 13th" rule is
     wrong here, and a CPI series indexed by month has a hole.
3. **USREC is rewritten after the fact, and FRED's convention looks like a forecast.**
   - FRED set USREC to 1 for March and April 2020 on 2020-06-08, the day of the peak announcement.
   - From then on it printed 1 for every new month, until the trough announcement.
   - On 2021-07-21 it flipped May 2020 to June 2021 back to 0.
   - The flag also starts the month AFTER the peak.
   - USREC's vintages go back only to 2014, so the 2008 recession cannot be replayed from them. Use
     `nber_turning_points.csv` for any "the recession was declared N months later" test.
   - USRECD (daily) is not point in time at all, and its `known_from` is blank.
4. **Before a series' first vintage, nothing is "known".** For example, `AsOf()` returns nothing for
   ICSA before 2009-05-28, and nothing for UMCSENT before 1998. Those periods carry
   `first_is_initial_print = false`, because their first archived value is already revised. A
   first-print-versus-revised study is limited to the vintage era of each series.
   SAHMREALTIME is built from real-time unemployment, so its values are as known at the time, but
   ALFRED only dates them from 2019. For older months, take the release date from UNRATE's first
   print for the same month.
5. **Benchmark revisions rewrite whole histories.** PAYEMS's 1961-11-03 vintage, for one, revised
   every recent month (January 1960 went from 52,880 to 54,211) and back-filled the series to 1939.
   "Revised" can mean decades later. The `versions` column counts the changes.
6. **Weekly releases skip holidays.** NFCI, the H.4.1 series and claims move or merge releases around
   Thanksgiving, Christmas and New Year. For example, the NFCI week ending 2022-12-16 was first
   published on 2023-01-05. Those series' `known_from` now uses the real release dates; a home-made
   weekly lag would not.
7. **NFCI, ANFCI and STLFSI4 are revised in full every week.**
   - The stored values are today's estimates. A z-score of today's NFCI at a 2015 date uses
     information from 2026.
   - Only STLFSI4 has its first prints (`STLFSI4_initial.csv`, from 2022-11).
   - The NFCI initial-release file could not be fetched (ALFRED returned 504).
   - Treat these as regime context, not as signals, unless a vintage source is added.
8. **FRED's posting date is not always the publisher's.** ALFRED's dates are when FRED posted the
   number. That is sometimes days after the actual release, most often in holiday weeks, which makes
   it conservative, not early.
   - Michigan sentiment on FRED is mostly the FINAL monthly reading, posted near month end (median 27
     days after the 1st). The preliminary mid-month number is not on FRED.
   - FRED lags on Michigan sentiment. On 2026-10-18 its newest value was August 2026, posted 2026-08-28.
9. **Daily rates are market closes, and their publication lags.** The H.15 yields (DGS*) and DFF were
   posted to FRED weekly in 2005 and the next business day later on. A yield was knowable from the
   market at the close, which is what `known_from` = its date assumes. A test that insists on the
   published H.15 number should add a business day.
10. **Monthly observations are stamped on the 1st of the month they describe.** January's CPI is dated
    2020-01-01, but it was published in mid-February. Never join a monthly value to a daily bar by
    observation date; join it by `known_from` or `realtime_start`.
11. **The FRED graph CSV names its value column with the vintage date** (`DGS10_20261018`). Parse by
    position, not by name.
12. **The Cboe archives change basis.**
    - Volumes are OCC cleared volume up to 2012-05-31 and preliminary volume after that.
    - Equity excludes exchange-traded products from 2012-06-11.
    - Where the archives overlap (2006 to 2012), they differ by about 0.01 to 0.02 on more than half
      the days.
    - Nothing after 2019-10-04 is in the archives.
13. **CoinMetrics and Blockchain.com have no vintages,** and either may restate history. They are
    stamped at the start of the UTC day they describe. Look-ahead from these is limited to their
    `known_from` margin.
14. **AAII is a member survey** that closes on Wednesday and is published on Thursday. The dates moved
    from Fridays to Thursdays in the early 1990s. The sheet's own moving averages (columns not
    exported) are computed with the full sample.

## What could not be fetched, and why

- **The FRED API** needs a key. It was not used. ALFRED's keyless graph CSV and download form served
  the same values and every vintage.
- **ICE BofA spreads before 2023-10-17** are blocked by ICE's licence (trap 1).
- **The NFCI and ANFCI initial-release files** failed with HTTP 504 on 2026-10-18, at 800 and at 100
  vintages per request.
- **data.nber.org** refuses non-browser clients ("we have disallowed bots from downloading datasets").
  The same dates were read from the chronology table on www.nber.org instead of spoofing a browser.
- **Cboe put/call after 2019-10-04** exists only as one HTML page per trading day, about 425 KB each
  and roughly 1,770 pages. It was not scraped. This needs Cody's decision, or Cboe DataShop.
- **Google Trends** has no keyless route that keeps to Google's terms.
- **Paid on-chain metrics** (SOPR, NUPL, realized cap, exchange flows) need a Glassnode, CryptoQuant
  or CoinMetrics key.

## ALFRED request limits, for the next fetch

- **Vintage cap.** One download may carry at most 1,500 vintages for a monthly series ("You may only
  download 1500 vintages at one time"). The fetcher asks in slices of 1,000 and joins them.
  - Each slice's rows already carry their true end dates.
  - PAYEMS fetched as two halves joined back into the single-request file exactly, all 13,691 rows.
  - M2SL, WRESBAL and INDPRO came in two slices each.
- **Some clients are stalled.** `curl` and .NET's `HttpClient` get answers. Python's `urllib` and
  `fred.stlouisfed.org/graph/fredgraph.csv` hang, and that pattern looks like bot filtering.
  `alfred.stlouisfed.org/graph/alfredgraph.csv?id=…` serves the same latest-vintage CSV.
