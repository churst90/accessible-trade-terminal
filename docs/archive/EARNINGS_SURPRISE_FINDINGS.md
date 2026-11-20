# Earnings surprise — the follow-up the macro null earned

Run 2026-08-02. `dotnet run -- earnings fetch --key <alphavantage>` then `earnings study --horizon N`.

## Why this test exists

Release dates for CPI, NFP, PPI and GDP tested **null** while FOMC did not
(`MACRO_EVENT_FINDINGS.md`, `FOMC_FINDINGS.md`). The explanation offered was a distinction rather
than an excuse: **FOMC is a policy *action*, CPI and NFP are *data* the market spends the interval
forecasting** — so what should move price is the *surprise*, not the date.

That was untestable while macro consensus sat behind a paywall. It is testable on company earnings,
where Alpha Vantage gives reported and estimated EPS back to the 1990s on the free tier.

## Data

10 large-cap US stocks — AAPL, CAT, CVX, IBM, JNJ, JPM, KO, MCD, MMM, MSFT — **1,200 events** with
both an actual and an estimate. Surprise is standardised *within each symbol*, so a five-cent miss
is read against that company's own dispersion rather than the market's. Entry is the close after the
report for post-market releases, the same close for pre-market ones.

The control is an **exposure-matched null**: the same stocks' returns over any window of the same
length. It holds "being in the market" fixed and asks only whether the earnings event, or the
surprise inside it, carries information.

## Result: null on both halves

| horizon | Q1 (miss) | Q2 | Q3 | Q4 | Q5 (beat) | beat − miss |
|---|---|---|---|---|---|---|
| 20 bars | +0.64% | **+1.62%** | +0.49% | +1.17% | +0.72% | **+0.08%** |
| 60 bars | +1.34% | **+3.31%** | +1.75% | +2.24% | +2.45% | **+1.11%** |
| 120 bars | +4.18% | **+7.25%** | +3.43% | +3.41% | +4.71% | **+0.51%** |

**The quintiles are non-monotone at every horizon, and Q2 is the best one every time.** A ranking
variable that puts its highest returns in the second-lowest bucket is not ranking anything. The
beat-minus-miss spread never exceeds about one point and does not grow with horizon in any orderly way.

And the event itself carries nothing either:

| horizon | all earnings events | the same stocks, any window | event premium |
|---|---|---|---|
| 20 bars | +0.93% | +0.79% | **+0.14** |
| 60 bars | +2.22% | +2.36% | **−0.14** |
| 120 bars | +4.60% | +4.73% | **−0.13** |

At 60 and 120 bars, holding these stocks through an earnings report did *worse* than holding them
across an arbitrary window of the same length.

## What this does and does not settle

**It does not rescue the action-versus-data hypothesis.** If the surprise were the mechanism, this is
where it should have shown up, and it did not.

**But this is the least favourable possible sample, and that has to be said plainly.** Ten mega-cap
US names are the most analyst-covered, most arbitraged securities in existence. Post-earnings
announcement drift is documented largely in **small caps with thin coverage**, and there is not a
single one in this universe. A null here is consistent with "the effect does not exist" and equally
consistent with "the effect exists where analysts are not looking, and every stock here is a place
they are looking."

**What would settle it:** a universe of 100+ names weighted toward small and mid caps. The data is
free — Alpha Vantage's daily cap is the only obstacle, and one request returns a symbol's entire
history, so it is a matter of days of unattended polling rather than money.

Until then the honest record is: **the surprise did not move price on the ten largest, most-covered
names we could test**, and the macro contrast that motivated the test remains unexplained.
