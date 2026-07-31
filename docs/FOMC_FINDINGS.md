# FOMC event study — a real effect, replicated, and largely arbitraged away since publication

Run 2026-07-31. `dotnet run -- fomc`. **224 scheduled FOMC decision dates 2000–2027**, parsed from
the Federal Reserve's own calendar and historical pages into `strategy-lab-data/events_fomc.json`.
Eight per year in every year except 2020, which correctly has seven — the March 2020 meeting was
cancelled and replaced by the emergency 15 March action. 24 unscheduled meetings/conference calls
are kept separately.

This is the first test in the lab that uses **real event dates**. An event date is exogenous: set
months in advance, public, no asymmetry about *when*. A calendar date cannot be a repackaged price
transform, which is the failure mode that killed crowding, volume-as-conditioner and MVRV.

## The control

FOMC decisions land on Wednesdays 151 times out of 212 and Tuesdays 51 times. This lab has already
measured a weekday effect in SPY, so "event days vs all days" would partly measure the calendar.
**The random control samples from the same weekday distribution as the real dates**, holding weekday
fixed so only the FOMC-ness varies.

## Result: a large decision-day effect in US equities

Day-0 excess over the weekday-matched random control:

| asset | day 0 excess | p | t−1 excess | p |
|---|---|---|---|---|
| **IWM** | **+0.265%** | **0.0045** | −0.010% | 0.708 |
| **XLF** | **+0.272%** | **0.0165** | −0.015% | 0.924 |
| **SPY** | **+0.173%** | **0.0190** | −0.010% | 0.639 |
| **QQQ** | **+0.227%** | **0.0230** | +0.025% | 0.633 |
| TLT | +0.088% | 0.156 | +0.134% | **0.038** |
| GLD | +0.095% | 0.141 | +0.071% | 0.297 |
| ETH | +0.722% | 0.131 | +0.240% | 0.833 |
| BTC | −0.308% | 0.998 | +0.369% | 0.193 |

**Four US equity vehicles, the same offset, the same sign.** 88 tests were run (8 assets × 11
offsets) and ~4.4 false positives are expected at α = 0.05 — but random false positives scatter
across offsets, and these do not. They also do not appear in gold or crypto, which is what a *US
monetary policy* effect should look like.

This is not a discovery. It is a replication of **Lucca & Moench (2015), "The Pre-FOMC Announcement
Drift," Journal of Finance** — their window is 2pm t−1 to 2pm t, which falls almost entirely inside
our close-to-close day-0 bar. Replicating a published result on our own data with a stricter control
is the right outcome.

Days t+1 and t+2 are negative across all three indices (SPY −0.135%, −0.108%), a partial give-back.

## It survives as a rule — the test MVRV failed

Long only on decision days, entering at the prior close, one round trip per event:

| asset | days in market | strategy | per-day | vs average day | net 3bps | p (exposure + weekday matched) |
|---|---|---|---|---|---|---|
| SPY | 212 | 1.62× | +0.228% | **6.8×** | 1.43× | **0.0175** |
| QQQ | 212 | 1.85× | +0.290% | 7.7× | 1.63× | **0.0310** |
| IWM | 209 | 1.88× | +0.302% | 10.7× | 1.66× | **0.0070** |
| XLF | 212 | 1.95× | +0.315% | 20.0× | 1.71× | **0.0160** |

All four beat a null that matches both the number of days in market *and* the weekday. MVRV's
quintile result was equally significant as a conditional mean and failed this test outright; this
passes it. Costs at 3 bps/side are conservative for SPY-class instruments and leave the result
intact.

## But it has been arbitraged away since publication

| asset | pre-2015 per day | n | post-2015 per day | n | retained |
|---|---|---|---|---|---|
| SPY | +0.334% | 120 | +0.090% | 92 | **0.27×** |
| QQQ | +0.332% | 120 | +0.235% | 92 | 0.71× |
| IWM | +0.440% | 117 | +0.127% | 92 | **0.29×** |
| XLF | +0.572% | 120 | −0.023% | 92 | **−0.04×** |

**Three of four have lost 70%+ of the effect and XLF has gone slightly negative.** The full-sample
6.8× is a pre-publication number.

Post-2015 SPY still runs +0.090%/day against a +0.033% baseline — about 2.7× a normal day, not 6.8×.
Whether that residual is a surviving edge or the tail of a dying one cannot be settled on 92 events.

This is the cleanest example of alpha decay this lab has produced, and it matches both interviewees:
Narang's *"you're literally sprinting on a treadmill to stand still"* and Varma's *"your ordinary
sources of alpha are going to disappear."* A documented anomaly is a traded anomaly.

## Caveats

- SPY/QQQ/IWM/XLF are highly correlated; four assets is closer to ~1.5 independent tests.
- The decay split is one cut at one date chosen because it is the publication year — not a
  pre-registered breakpoint.
- Daily bars cannot isolate the 2pm-to-2pm window the literature actually measures.
- Unscheduled meetings show SPY −0.36%, IWM −0.33%, GLD −0.78% on day 0. Those are crisis meetings;
  the causality runs from the crash to the meeting, not the other way. Reported for contrast only.

## CPI: blocked, not skipped

BLS returns 403 to automated requests and `fred.stlouisfed.org` is unreachable from here
(`api.stlouisfed.org` responds but requires a registered key). CPI release dates were **not**
reconstructed from memory — fabricated dates at the centre of a result are worse than no result.

**A free FRED API key would unlock this immediately**, and not just CPI: release dates for NFP, PPI,
GDP and everything else in the FRED release calendar. That is the single cheapest unlock available.
