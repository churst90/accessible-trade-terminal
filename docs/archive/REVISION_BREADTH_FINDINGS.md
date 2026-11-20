# Analyst revision breadth — UNTESTED, and that is the finding

Run 2026-08-02. `StrategyLab grades fetch --key <fmp>` then `grades study`.

**Verdict: UNTESTED. Not null.** The distinction is the entire content of this document.

---

## Why this test was queued ahead of everything else

The earnings-surprise test came back null and the write-up said plainly why that was the least
favourable possible sample: post-earnings drift is documented in thinly covered small caps, and
every name tested was among the most analysed securities in existence
(`EARNINGS_SURPRISE_FINDINGS.md`).

Revision breadth is the natural follow-up because it is a claim about the **analysts** rather than
about the number they missed. If coverage is what kills surprise, coverage is exactly what revision
breadth needs. It was also the last untested item that would justify extending the company-data
layer at all, which is why it was next.

## The measurement

Breadth is the bullish share of the rating mix:

```
(strongBuy + buy) / (strongBuy + buy + hold + sell + strongSell)
```

and the signal is its **change** from one monthly observation to the next. The level is not the
signal — a stock everyone has always rated a buy carries no news, and sorting on the level would
mostly rank sectors.

Each month, symbols are sorted on the breadth change and the top third is measured against the
bottom third over the next 21 trading days. A cross-sectional spread is the right shape because it
holds the market fixed: every name is measured against the others in the same month, so a rising
tide cannot be mistaken for a signal.

**Why this data source and not the obvious one.** FMP's `grades-historical` is a monthly snapshot
carrying its own observation date, so the mix attributed to March is the mix as it stood in March.
`analyst-estimates` returns *today's* estimates for past periods — a restated series that would hand
the test its answer.

## What the free tier actually yields

| | |
|---|---|
| symbols with a rating history | **11** |
| of those, with matching price history | 11 |
| symbol-months with a forward return | 84 |
| distinct monthly cross-sections | 8 |
| months with enough names to form terciles | **6** |

FMP's free tier caps `limit` at **10 rows per symbol** — about nine months — and refuses some
symbols outright (CAT, IBM, MCD, MMM and PG were all blocked; MSTR and SILJ are not available at
any symbol level on this tier).

## The result, and why it must not be read as one

| | |
|---|---|
| mean top-minus-bottom tercile spread | −1.56% |
| standard deviation across months | 5.66% |
| t | −0.68 |

**Minimum detectable effect: 6.47% per month — roughly 78% a year.**

That number is the actual finding. A real revision-breadth effect is documented in the literature at
well under 1% a month. This sample cannot see anything smaller than 6.47%, so **both a positive and
a null result here would be uninformative**. The −1.56% is noise, and reporting it as evidence
against the hypothesis would be exactly the error this project's controls exist to prevent — the
mirror image of reporting a lucky backtest as an edge.

Stating the minimum detectable effect is the cheapest guard against a false null, and it belongs in
any test where the sample is small enough to doubt. It is now part of the standard kit alongside
the exposure-matched null and the random-parameter arm.

## What fixes it

Two things, and only two:

1. **`StrategyLab grades record` monthly.** A committed forward archive — same discipline as
   `record-universe` — that gains one usable period a month. About **three years** before this test
   means anything, which is an argument for starting immediately rather than for not starting.
   Started 2026-08-02 with 17 symbols in `grades-archive/`.
2. **A paid tier**, which buys the history outright and makes the test runnable this week.

Recording also removes the one caveat that cannot be resolved from outside the vendor: "monthly
snapshot with a date on it" is what the API *presents*, but whether FMP recorded each row at the
time or reconstructed the series later is not visible to us. A row we wrote on the day carries no
such question.

## Two defects this run exposed in the recorder itself

Both are the same bug wearing different clothes, and it is worth naming as a pattern.

**Every failure was collapsed into one return value.** "The tier does not cover this symbol", "no
analyst covers this company", "we asked too fast" and "the network broke" all became `null`. The
first `record` run captured **1 symbol of 21**, reported success, and wrote the file. Only the
second is a finding; the rest are reasons to retry.

**There was no coverage floor.** An empty snapshot was refused — but a snapshot holding one symbol
out of twenty-one was not, and on any later delta that file reads as an industry losing coverage
overnight. The floor now tests against the *reachable* universe (excluding permanently blocked
symbols, which are stable and therefore harmless) and refuses a run that lost more than a third of
it. After both fixes and a wider request spacing: **17 of 21**.

> **The general rule:** any recorder that cannot distinguish *not yet* from *not there* will
> eventually write a hole into an archive and call it data. The GDELT recorder built the same day
> had the identical defect and needed the identical fix.

## Scope and caveats

- 11 mega-cap US names. Even with full history this universe is wrong for the hypothesis — the
  effect is supposed to live where coverage is thin, and there is not a single such name here.
- One horizon (21 bars) and one sort (terciles). Neither has been varied, because varying anything
  at this power would be fishing.
- No costs, no exposure-matched null, no era split. All of those come *after* the sample can
  support a result at all.

Cross-references: `EARNINGS_SURPRISE_FINDINGS.md` · `COMPANY_DATA_LAYER.md` · `ALPHA_LEDGER.md` ·
`FMP_LEGACY_ENDPOINTS` note in memory.
