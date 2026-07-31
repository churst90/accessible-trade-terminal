# Volume — one claim confirmed, one rejected, and the polarity split for the fourth time

Run 2026-07-31. `dotnet run -- volume`. 48 symbols, 385k bar observations, 20-bar forward horizon,
60-bar trailing window.

Testing Samir Varma's two specific claims — volume being the input he calls "the key", and the
reason he says institutions moved to dark pools ("they don't want you to know what the volume is").

## Claim 1 — "volume rising into up-moves ⇒ upside left"

Measured as the trailing correlation between daily returns and log volume. Positive = volume
arrives on up-days.

| class | top − bottom quintile | p |
|---|---|---|
| **crypto** (10 sym, 24.5k obs) | **+1.26 ATR** | **0.0002** |
| **equity** (33 sym, 333k obs) | **−0.19 ATR** | **0.0002** |
| bond (2 sym) | +0.25 ATR | 0.015 |
| commodity (3 sym) | +0.16 ATR | 0.112 |

**Confirmed in crypto. Significantly reversed in equities**, where the quintiles decline
monotonically (+0.58 → +0.47 → +0.41 → +0.41 → +0.39): volume on *down*-days predicts the higher
forward return.

### Is it just momentum wearing a volume hat?

It had to be asked — volume arriving on up-days is close to a description of an uptrend, and the
signal does correlate **+0.43 (equity) / +0.59 (crypto)** with the trailing 60-bar return. This is
the exact failure mode found in the crowding index, whose docstring claimed orthogonality to price
while correlating 0.19 with trailing returns.

Re-measured inside trailing-return terciles:

| class | trend falling | trend flat | trend rising |
|---|---|---|---|
| **crypto** | **+0.37** (p=0.004) | **+0.56** (p=0.009) | **+1.40** (p=0.0002) |
| equity | −0.37 (p=0.0002) | −0.31 (p=0.0002) | +0.14 (p=0.0005) |

**Crypto survives in all three buckets, same sign, all significant.** Volume carries information
there beyond the trend. In equities the effect is negative in falling and flat markets and only
turns positive in rising ones — a reversal signature, not a confirmation one.

## Claim 2 — "a 20× volume day during a decline is capitulation, may be time to buy"

Measured against the **decline-only** baseline, not the all-bars one: buying any dip and buying a
dip that came with a volume spike are different claims, and only the difference belongs to volume.

| class | ≥3× median vol | ≥5× median vol |
|---|---|---|
| equity | −0.10 ATR (p=0.050) | −0.16 ATR (p=0.103) |
| **crypto** | −0.25 ATR (p=0.149) | **−0.72 ATR (p=0.024)** |
| **commodity** | **−0.92 ATR (p=0.0015)** | too few |
| bond | +0.16 ATR (p=0.573) | +1.14 ATR (p=0.026, n=48) |

**Rejected.** No class supports it, and crypto and commodities run *significantly backwards* — a
volume spike during a decline predicts worse forward returns, not better. The one positive is 48
observations across two bond ETFs.

Equity at ≥20× is +0.45 ATR excess but n=86 and p=0.22. If a real capitulation effect exists it
lives at extremes daily bars barely sample.

## The finding underneath

This is the **fourth independent measurement** of the same asset-class split, from a fourth kind of
data: POC deviation, Value Deviation, the Trading Cross, and now volume. Crypto confirms — volume
follows price and predicts continuation. Equities revert — volume on weakness predicts recovery.
The same fork, arrived at through order flow rather than through price geometry.

## Caveats

- **Crypto volume is one venue's self-report.** Bitstamp is a real order book, but exchange volume
  is not consolidated tape and wash trading is endemic in the asset class. Equity volume from Yahoo
  is far more trustworthy — and equity is the class where the claim *fails*.
- 10 crypto symbols; commodity and bond arms are 3 and 2 symbols and their conditioned results flip
  sign between buckets, consistent with noise.
- +1.40 ATR over 20 bars is a conditional relationship, not a strategy. No entry, exit, or cost model.
- **Daily bars cannot represent what Varma is actually describing.** His claim is about reading order
  flow and level 2. That this survives at daily resolution in crypto at all is the surprise; that it
  fails in equities may say more about the resolution than the claim.
