# CPI, NFP, PPI and GDP release days — nothing

Run 2026-08-01. `dotnet run -- macro`. **Real release dates from the FRED API**: 457 CPI, 445 NFP,
455 PPI, 487 GDP releases since 1990. These were unavailable until a key existed — BLS blocks
automated requests and the FRED web UI is unreachable from this machine. The dates were never
reconstructed from memory.

## The control

CPI and PPI land mid-month; **NFP is the first Friday 423 times out of 445**. This lab has already
measured a weekday effect in SPY and a turn-of-month effect in SPY and BTC, so a naive comparison
would partly be measuring the calendar. The random control draws from the **same weekday
distribution**, and each release's day-of-month profile is printed so a calendar artifact is visible
rather than hidden.

## Result

Excess over the weekday-matched random control, 5 assets × 4 releases × 3 offsets:

| | SPY | QQQ | TLT | GLD | BTC |
|---|---|---|---|---|---|
| **CPI** day 0 | +0.013% | +0.061% | +0.031% | +0.073% | +0.135% |
| **NFP** day 0 | +0.094% | +0.077% | −0.160% | −0.064% | −0.454% |
| **PPI** day 0 | +0.001% | −0.031% | **+0.118%** (p=0.020) | −0.041% | −0.103% |
| **GDP** day 0 | +0.055% | +0.036% | **+0.105%** (p=0.032) | +0.097% | +0.114% |

**2 of 20 release-day cells significant; 0 of 20 on the day before.**

60 tests were run and **~3 false positives are expected by chance**. A Bonferroni threshold would be
0.00083; the two hits are at 0.020 and 0.032. Neither survives.

Both hits are TLT, which is at least mechanistically plausible — inflation and growth prints move
rates — but two isolated cells out of sixty is what chance produces, and they do not repeat across
the other four assets or on the adjacent offsets.

**CPI, the release everyone watches, shows nothing anywhere.**

## The contrast with FOMC is the actual finding

`FOMC_FINDINGS.md` found **four US equity vehicles significant at the same offset with the same
sign** (SPY +0.173%, QQQ +0.227%, IWM +0.265%, XLF +0.272%), absent in gold and crypto — the shape a
genuine US monetary-policy effect should have. It then survived an exposure- and weekday-matched
null as a tradeable rule, before decaying ~70% post-publication.

This study is what the *absence* of that looks like: scattered singletons at p≈0.03, no agreement
across assets, no adjacent-offset support. Same harness, same controls, same author — different
answer. That is the harness working.

## Why the difference is plausible

An FOMC decision is a *policy action* with a persistent effect on discount rates. CPI and NFP are
*data*, and the market's job between releases is to forecast them — so the surprise is what moves
price, not the release itself. Measuring the release date without the surprise measures the average
of beats and misses, which is close to zero by construction.

**That points at the real next test:** the *surprise* (actual minus consensus), not the date. FRED
gives actuals; consensus needs a different source. Until then this is a null on the date, not on the
event.

## Caveats

- Daily bars. These releases hit at 08:30 ET and much of the reaction is in the first minutes.
- 1990 onward, so ~35 years — plenty of releases, but macro regimes are few.
- Long-only day returns; no volatility or options-implied measurement, where an effect is more
  likely to live.
