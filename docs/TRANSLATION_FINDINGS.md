# Right translation — NULL, and killed twice over

Run 2026-08-04. `dotnet run -- translation --tf 1d --surrogates 400`.
**4,472 complete cycles across 49 symbols.**

## The claim

Camel Finance, live (`CAMEL_CYCLE_LIVE_READ.md`):

> *"When we overextend an extra right translate like this, we typically **come down harder and
> faster** to correct the four-year cycle. Rather than have this kind of 10 to 12 month orderly walk
> down, we tend to **puke into the low** after heavily right translating."*

A cycle whose high prints late should be followed by a faster, deeper decline.

## Why it was worth testing when the cycle counts were not

`camel-cycle-counts` is already **Falsified** — shuffled-return surrogates reproduced the claimed
cycle *length* on every asset, so the length belonged to the detector. This is a different shape of
claim: it does not ask *when* the low arrives, it asks whether — **given** a cycle — the position of
its high conditions the decline that follows. Both arms are measured inside the same detector, so
whatever the detector invents, it invents for both.

## The mistake in the first run, and why it matters

The first version measured "faster" as **decline share** — the fraction of the cycle spent falling.
It reported a −0.524 effect for a translation gap of +0.524, which looked like a large confirmation.

It is a **tautology**. Translation is `(high − low) / length`; decline share is
`(nextLow − high) / length`. They sum to exactly 1 by construction:

```
early:  0.302 + 0.698 = 1.000
late:   0.826 + 0.174 = 1.000
```

The measurement was arithmetic restating its own input. **Depth alone cannot decide the claim
either**, for a related reason: a late high mechanically leaves fewer bars to fall in, so a
shallower drop is expected whatever the market does.

The honest measure of "harder and faster" is **velocity — fractional decline per bar**, which is
what "puke into the low" actually describes: a lot of ground covered in few bars.

> Recorded as a trap: **when a conditioning variable and an outcome variable are both fractions of
> the same total, they are one variable.** Check that the two cannot be added to a constant before
> interpreting any spread between them.

## Result

| Arm | n | Translation | Depth | Velocity (%/bar) |
|---|---:|---:|---:|---:|
| Early high (bottom third) | 1,490 | 0.302 | 20.18% | 0.4620 |
| Late high (top third) | 1,490 | 0.826 | 10.92% | 1.0342 |
| **Late − early** | | | **−9.27%** | **+0.5722** |

Late-translated cycles *do* fall faster — more than twice the velocity. The claim survives its first
look. It then dies twice.

### Control 1 — the cheap alternative reproduces it almost exactly

Sorting the same cycles on **plain trailing advance** instead of translation:

| | Translation split | Trailing-advance split |
|---|---:|---:|
| Velocity gap | +0.5722 %/bar | **+0.5591 %/bar** |

**97.7% of the effect.** Sorting on "how much it went up" gives essentially the same answer as
sorting on "when it peaked", which is what our earlier cycle work already concluded: **translation is
momentum in cycle vocabulary.** The cycle framing adds nothing measurable.

### Control 2 — the same detector on shuffled returns

357 surrogate draws, log returns shuffled and the path rebuilt, through the identical detector:

| | Matched or beaten in |
|---|---|
| Depth gap | 198 of 357 — **55.5%** |
| Velocity gap | 116 of 357 — **32.5%** |

One-sided p ≈ **0.33** for velocity. Nowhere near significant. A third of random price paths produce
this relationship as strongly, because the detector that defines "the cycle" manufactures the
relationship in noise too.

## Verdict

**NULL.** Registered as `Falsified`.

Two independent reasons, either of which is sufficient:

1. The cheap alternative — trailing return — reproduces 98% of the effect, so the cycle vocabulary
   is doing no work.
2. A third of shuffled-return surrogates reproduce it, so the effect is not distinguishable from
   what the measurement invents.

**This is the second claim from this source to die on surrogates and for the same reason.** Both
depended on a cycle boundary that a swing detector will happily impose on random data.

## Scope and caveats

- Daily bars, 20-bar pivot with a matching confirmation lag. The source works on a longer cycle
  (~60-day daily cycles, ~31-week weeklies), so this tests the *shape* of the relationship at a
  faster cadence rather than his exact degree.
- No era split and no asset-class split. Neither is worth running: a claim that fails its cheap
  alternative by 98% does not become real inside a subset.
- The one thing this does **not** test is the risk-placement claim — that a stop below a projected
  cycle low is hit less often than an arbitrary one. That remains open and needs forward recording,
  because the source revises its count as price arrives.

Cross-references: `CYCLE_FINDINGS.md` · `CAMEL_CYCLE_LIVE_READ.md` · `ALPHA_LEDGER.md`.
