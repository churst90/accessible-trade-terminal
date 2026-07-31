# Camel / Bob Lucas / Charles Nana cycles — the period is the detector, the translation might not be

Run 2026-07-31. `dotnet run -- cycles`. BTC, ETH, SPY, QQQ on daily bars.

## The claims

Bitcoin runs a **60-day** daily cycle low-to-low ±10% (54–66d); the S&P **40 days** (36–44d). Lows
land inside the band **80% of the time**. Where the cycle *high* falls decides direction —
right-translated bullish, left-translated bearish. A **cycle failure** (undercutting the prior
cycle low) means more downside.

## The trap, and how it was avoided

In the tutorials, cycle lows are marked by looking back at a finished chart. Pick the lows knowing
the outcome and they *will* be 54–66 days apart, because that is how they were chosen. The 80%
figure then measures the selection, not the market.

So lows here are found by a fixed algorithm — a pivot low that is the lowest of `span` bars either
side, knowable only `span` bars later, which is also the delay the tutorials' own confirmation step
("wait for a swing low, trendline break or MA cross") imposes in practice.

**The control:** every statistic is recomputed on 200 surrogate series built by shuffling the log
returns and rebuilding the price path. Same return distribution, same volatility, every trace of
periodicity destroyed.

## Periodicity: dead

| asset | span | real mean gap | **surrogate mean** | real in-band | **surrogate in-band** |
|---|---|---|---|---|---|
| BTC | 20 | 64.7d | **61.7d** | 10% | **16%** |
| ETH | 20 | 55.7d | **60.2d** | 16% | **16%** |
| SPY | 8 | 38.9d | **36.2d** | 13% | **15%** |
| QQQ | 8 | 38.7d | **36.1d** | 14% | **15%** |

**A shuffled random walk reproduces the cycle length on every asset — and lands inside the claimed
timing band more often than the real data does on three of four.**

The mean gap is a near-linear function of the detector's span and nothing else. On BTC:
span 5 → 15.9d, 8 → 25.0d, 10 → 31.7d, 12 → 38.6d, 15 → 48.7d, 20 → 64.7d, 25 → 75.3d. You can
produce any "cycle length" you like by choosing the span, and the surrogate tracks it the whole way.

And the headline number does not survive at all: **lows land in the claimed band 10–17% of the time
at best, not 80%.**

## Translation: the part that survives the trap

Translation — did the high come late or early in the swing — is a momentum statement that does not
need the fixed period to be real. Tested separately for exactly that reason. Forward 20-bar return
measured *after* the cycle completes plus the detector's confirmation lag:

| asset | right-translated | left-translated | gap | p |
|---|---|---|---|---|
| **BTC** | +6.16% (n=41) | −1.40% (n=38) | **+7.56%** | **0.030** |
| ETH | +1.67% (n=28) | −7.60% (n=27) | +9.27% | 0.098 |
| SPY | +0.35% (n=199) | +1.29% (n=112) | −0.94% | 0.073 |
| QQQ | +0.30% (n=153) | +0.32% (n=101) | −0.02% | 0.984 |

**Both crypto assets positive, both equities not.** That is the asset-class polarity for the fifth
time — translation is a momentum measure, and momentum works in the trending class.

**Do not over-read the BTC result.** Eight tests were run (4 assets × 2 claims); at α = 0.05 you
expect 0.4 false positives, and a Bonferroni-corrected threshold would be 0.006. p = 0.030 on 79
cycles does not clear that. It is *consistent with* a finding established elsewhere on much larger
samples, which is the only reason it is worth recording at all.

## Cycle failure: nothing

| asset | failed − held | p |
|---|---|---|
| BTC | −3.49% | 0.342 |
| ETH | −2.48% | 0.660 |
| QQQ | −0.89% | 0.309 |
| SPY | +0.28% (backwards) | 0.604 |

Three of four point the claimed direction, none is close to significant.

## Verdict

**The 60-day/40-day cycle is a property of the swing detector, not of the market.** "It is cycles
doing cycle things" is a description of how humans see charts. Any pivot-finding rule produces
regularly-spaced lows on random data, and this one produces the claimed spacing on random data more
reliably than on real data.

What is left is **translation**, which is momentum with a cycle vocabulary — and which behaves
exactly like every other momentum measure tested here: positive in crypto, absent in equities. If
you want that edge, `xsmom` and the Trading Cross measure it directly, on far larger samples,
without the cycle scaffolding.

**Related:** an earlier study in this lab found a "~22–33 bar cycle window" that was *universal
across unrelated assets*. That should have been the tell — a universal period across unrelated
markets is a detector signature, not a market property.
