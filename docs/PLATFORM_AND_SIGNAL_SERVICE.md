# The north star, the terminal/lab boundary, and the signal service

Written 2026-08-02 from a design discussion. **Nothing here is built.** Companion to
[COMPANY_DATA_LAYER.md](COMPANY_DATA_LAYER.md) (what data, for whom) and
[LAB_DESIGN.md](LAB_DESIGN.md) (how research compounds).

---

## The north star, restated

> **The terminal is the public, fully accessible interface to all market data — OHLCV and
> everything else.** A human trader needs access to every piece of information a sighted person has,
> in the best format for hearing it.
>
> **The lab is the private research facility**, doing programmatically what a human trader does by
> judgement.

Everything below follows from those two sentences. The tools are given away; the alpha stays close.

## Three tiers, and the rule that decides what goes where

| tier | audience | ships | contains |
|---|---|---|---|
| **Terminal** | everyone, free | public | data, tools, charting, screening, the strategy engine, the dossier |
| **Lab** | you | never ships | the edge registry, the catalogue, every study, the raw research |
| **Signal service** | opt-in, possibly paid | a network endpoint | *conclusions* the lab has produced — never the method that produced them |

**The rule:** *anything that measures or displays goes in the terminal; anything that decides what is
true goes in the lab.*

Applied to concrete cases:

| thing | where | why |
|---|---|---|
| "earnings were X, revenue Y, chart made a higher high" | terminal | measurement, no judgement |
| "short interest is 22% of float" | terminal | a fact; display it even if it predicts nothing |
| pattern **detection and description** | terminal | see the pattern section below — this is an accessibility feature |
| "this level has held 7 of 9 touches" | terminal | measurement; the respect report already does it |
| whether that hit rate is *better than a random line* | **lab** | that is a verdict, and it needed a control to reach |
| "value buy zone at 91,200 because…" | **signal service** | a conclusion derived from private research |
| the edge registry, effect sizes, controls | **lab only** | this is the alpha |

The boundary is not about secrecy for its own sake. A terminal that ships measurements is honest
and useful on day one. A terminal that ships *conclusions* it cannot justify is the thing the
strategy-library policy already banned.

---

## The signal service

**The concept:** the lab runs on your VPS, keeps doing what it does, and exposes a small read-only
API. The terminal has an optional "connect to signal service" setting taking a URL and an API key.
When connected, the dossier gains a section: *"Signal service says…"* — clearly attributed, clearly
separate from the terminal's own measurements.

### What it may emit

Signals, with their provenance attached — the same discipline as `StrategySpec.Provenance`:

```jsonc
{
  "asset": "BTC/USD", "asOf": "2026-08-02T00:00:00Z",
  "kind": "attention",
  "headline": "Trend-continuation conditions are present and mean-reversion conditions are not.",
  "detail": "Price is in the regime where the control-tested trend edge applies (+0.4R, walk-forward
             with a random-parameter arm). The mean-reversion edge does not apply to this asset class.",
  "evidence": "ControlTested",
  "confidence": 0.55,
  "expires": "2026-08-09T00:00:00Z"
}
```

### What it must never emit

- The edge definitions, parameters, or the code that computes them.
- Anything derived from an edge that is not `ControlTested`. The scoring rule the lab already
  enforces travels with the signal.
- A bare "BUY" — the standing design preference is an *action recommendation with rationale*, and a
  paid signal with no rationale is exactly the product this project exists to be the opposite of.

### Architecture sketch

- **Lab side:** a small ASP.NET endpoint reading the same `edges.json` and asset profiles the CLI
  reads. No new research path — it publishes what already exists.
- **Auth:** API key per subscriber, rate-limited, revocable. Keys are hashed at rest.
- **Terminal side:** one optional provider-shaped client. It degrades to nothing when not
  configured — no nag, no dead menu item, and every existing feature keeps working.
- **Caching:** signals are dated and expire. A stale signal must announce its age rather than
  present itself as current.

### Honest caveats before any money changes hands

1. **You are one control-tested edge away from having something to sell**, and that edge is
   cross-sectional momentum in equities at +0.37% per 30 days. That is real and it is small. Price
   the product against what it actually delivers.
2. **A paid signal invites a different standard of scrutiny than a free tool.** Subscribers will
   (rightly) ask for a track record. Consider recording every emitted signal and its outcome from
   day one — the service should keep its own ledger, published.
3. **Regulatory surface.** Selling generic research is generally different from personalised
   investment advice, and the line varies by jurisdiction. Worth a real answer before launch, not
   after.
4. **It changes the incentive structure of the research.** Right now a null result costs nothing to
   report. With subscribers, there is pressure to have something to say weekly. That pressure is
   how research programmes start lying to themselves. If this gets built, the ledger's discipline
   matters more, not less.

---

## Chart patterns — the distinction that matters

You said users want the obvious patterns for daily analysis. Both halves of that are worth
separating:

**Pattern *description* is an accessibility feature, and it is valuable regardless of predictive
power.** "Price made a higher high and a higher low, it is in the upper third of its 3-month range,
and the last three sessions have been inside bars" is *what a sighted person sees in one glance*.
Delivering that by ear is the terminal's whole reason to exist. The market-structure indicator
already does part of it; a plain-language chart summary in the dossier finishes it. **Ship this.**

**Pattern *prediction* is what we have tested, and it is weak.** A random horizontal line is
respected 59% of the time; real swing levels held 46.2% against 46.7% for random lines; fib ratios
did nothing across 355,000 tests; the approach conditioning was null in every bucket.

So: **describe patterns freely, score them never.** The dossier can say "price is at a level it has
touched four times" without claiming that means anything, and it should say so in exactly those
terms.

### Built 2026-08-02 — and the design decision that mattered

`ChartPatternDetector` ships this: double tops and bottoms, head and shoulders both ways,
ascending / descending / symmetrical triangles, rising and falling wedges, bull and bear flags.
Opt-in via `speech.describeChartPatterns`, default OFF, spoken on time-axis navigation.

**The decision that makes it worth having is the life stage.** A detector that reports only
*completed* patterns is a curiosity: by the time a head and shoulders completes, the neckline has
broken and the move it names is underway. So every pattern is reported from the moment its structure
is knowable, as **Forming**, carrying the **trigger level** that would confirm it:

> "Possible double top in progress, neckline 42,100."

That is a level to watch. The completed form closes the loop on a report the user already heard:

> "Double top completed, neckline 42,100 broken."

Three properties are enforced by tests rather than convention:

1. **No lookahead.** Built on `ISwingStructureAnalyzer`, whose pivots carry the bar at which they
   could first be *known* — Span bars after they printed. A truncation test detects on the full
   series and on a prefix and requires identical output for everything knowable inside the prefix.
   That test caught a real wart: `KnownAtIndex` was being overwritten with the break bar for
   completed patterns, so the same pattern reported a different "knowable at" depending on how much
   future data happened to be loaded. Completion now lives in its own field.
2. **Forming means the trigger has genuinely not been hit** — verified across a 600-bar random walk.
3. **No directional language, ever.** A test asserts the narration contains none of *bullish*,
   *bearish*, *buy*, *sell*, *target*, *expect*, *reversal* or *likely*, for every pattern in both
   states. The conventional readings are exactly the claims this project has failed to confirm; the
   one that would have to be true first — "ascending triangles break up" — is queued as
   `triangle-direction-bias` and untested.

---

## Thesis building — what code can do, and where the LLM actually goes

The idea: an event triggers a thesis, and each day new data adds to it, supports it, or kills it.

**The part that is straightforward deterministic code, and is most of the value:**

A thesis is a record with a claim, a set of **named conditions**, and a state. Each day the
conditions are re-evaluated from data and the thesis is updated:

```jsonc
{
  "id": "thesis-2026-08-02-aapl",
  "claim": "Estimate revisions have turned up ahead of a re-rating.",
  "opened": "2026-08-02", "state": "developing",
  "conditions": [
    { "name": "revision breadth > 0",       "met": true,  "since": "2026-07-28" },
    { "name": "price above 200-day",        "met": true,  "since": "2026-06-11" },
    { "name": "short interest falling",     "met": false, "lastChecked": "2026-08-02" }
  ],
  "invalidatedIf": "revision breadth turns negative for 2 consecutive weeks",
  "score": 0.66
}
```

That gives you the thing you actually described — a thesis that accumulates, strengthens, weakens
and dies on its own stated terms — **with no LLM anywhere**, and it is auditable because the
invalidation condition was written down *before* the outcome was known. That last property is what
makes it research rather than storytelling.

**Where an LLM genuinely helps, and where it must not go:**

- *Helps:* turning a news item or filing into candidate conditions ("this 8-K is a CFO departure")
  at ingestion, and writing the human-readable narration of a thesis's current state.
- *Must not:* deciding whether a condition is met, or scoring the thesis. Those stay arithmetic.
- **And the constraint from the data-layer doc applies with full force:** an LLM reading a 2019
  filing already knows what happened next. Theses whose conditions come from LLM-extracted features
  can only be validated **forward**, from the day recording starts.

---

## Crypto: the quality and scam report

The Crypto Galaxy / 24-point material is already decomposed in `LAB_DESIGN.md` into three buckets.
What is new today is that **CoinMarketCap's free tier covers most of the machine-collectable
bucket** — verified below.

### The deterministic scorecard, computable today from CMC alone

| check | field | red flag when |
|---|---|---|
| dilution overhang | `fully_diluted_market_cap` / `market_cap` | ratio is large — most of the supply has yet to hit the market |
| supply already out | `circulating_supply` / `max_supply` | low, especially paired with a near unlock |
| infinite issuance | `infinite_supply` | true |
| self-reported supply | `self_reported_circulating_supply` present and ≠ `circulating_supply` | the project is marking its own homework — CMC flags this deliberately |
| listing breadth | `num_market_pairs` | very low — thin, hard to exit |
| turnover | `volume_24h` / `market_cap` | extremely low (illiquid) **or** extremely high (a wash-trading tell) |
| venue mix | `cex_volume_24h` vs `dex_volume_24h` | DEX-only at size |
| age | `date_added` | very recent |
| host chain | `platform` | a token on someone else's chain is a different risk class than a coin |
| disclosure | `urls.website`, `urls.technical_doc`, `urls.source_code`, `urls.explorer` | **missing** — no whitepaper or no public source is the loudest cheap signal there is |
| rank trajectory | `cmc_rank` over time | requires storing it daily; nothing gives you history retroactively |

Eleven checks, all arithmetic, all defensible, none requiring judgement. That is the "run scam
report" button — a **scorecard with each line shown and sourced**, not a verdict from a black box.

**What stays judgement** (team, whitepaper credibility, the bear case, the trial run): an
LLM-assisted checklist the user answers, stored as notes on the asset. Displayed, never scored.

**The prediction to test, unchanged:** this works as a **veto**, not a timing signal. Excluding
low-liquidity, high-dilution, undisclosed-source tokens should avoid losses; it should not pick
winners. Testing it needs **point-in-time universe snapshots taken forward**, because the dead
tokens vanish from the list and any backtest on today's universe is survivorship-poisoned.

**Gotcha found while testing:** CMC symbol lookup is ambiguous — querying `symbol=UNI` returned
*Unitecoin*, not Uniswap. Any implementation must resolve by `id` or `slug`, never by ticker.

---

## Provider and key status — tested 2026-08-02

| provider | key | status | what it gives us |
|---|---|---|---|
| **CoinMarketCap** | ✅ have | **works** — `listings/latest`, `quotes/latest`, `info` all 200 | the crypto vetting scorecard above. `ohlcv/historical` is **403 on this plan** — we do not need it, we have OHLCV elsewhere |
| **Nomics** | have | **DEAD** — the service shut down; the host does not resolve | nothing. Discard the key |
| **CoinAPI** | have | **403 "Quota exceeded: Insufficient Usage Credits"** on every endpoint | nothing usable as-is. Its free credits are consumed; would need a paid plan, and it duplicates data we already have |
| **Alpha Vantage** | ✅ have | works | earnings actual vs estimate back to the 1990s. Two limits: ~5/min (clears) and ~25/day (does not) |
| **FRED** | ✅ have | works | macro actuals |
| **FMP** | ✅ have | works on `/stable` free tier | quotes, statements, treasury rates, sector performance |
| **SEC EDGAR** | none needed | works | **the anchor**: filings, XBRL financials, Form 4 insider, 13F |
| **GDELT** | none needed | not yet tried | news events, timestamped and entity-tagged |
| **Wikipedia Pageviews** | none needed | not yet tried | hourly point-in-time attention back to 2015 |

### What to actually get

**Nothing, yet.** Every gap that matters is covered by a free source. Specifically:

- **Do not renew or replace CoinAPI or Nomics.** CMC covers the crypto metadata need, and OHLCV
  comes from the exchange providers already in the tree.
- **No new key needed** for SEC EDGAR, GDELT or Wikipedia — the three highest-value additions.
- Revisit paid tiers only when a *tested* edge needs data we cannot get free. That has not happened
  once yet.

**Security note:** the CMC, Nomics and CoinAPI keys were pasted into a chat transcript. They are
stored at `patches/*-api-key.txt`, mode 600 and gitignored, but rotating the CMC one is cheap
insurance since it is the only one still live.

### New providers that would need building

| provider | tier | effort | for |
|---|---|---|---|
| `SecEdgarProvider` | analytics | medium — XBRL frames, per-concept series | financial statements, insider, 13F |
| `CoinMarketCapProvider` | analytics | small — three endpoints, resolve by slug | the crypto scorecard |
| `GdeltProvider` | analytics | medium — event query + entity mapping | news-event counts |
| `WikipediaPageviewsProvider` | analytics | **small** — one REST call, clean daily series | attention |

All four fit the existing analytics-provider pattern (`O=H=L=C=value`), so they arrive sonifiable and
usable in the condition tree with no new plumbing. **Wikipedia is the cheapest and the most
time-sensitive** — its value comes from accumulated history, and history only accumulates if
collection starts.

---

## The trader-skills question

You asked whether to build skills modelled on the traders you follow, and whether feeding me their
videos would help.

**Separate three things, because they have very different answers.**

**1. Their process — worth capturing.** The order they look at things in, what they check before
sizing, what vetoes a trade entirely. That is a checklist, it is genuinely useful to a discretionary
user, and it does not depend on their edge being real. The trader's-triangle interview produced
exactly this: risk shaved by how much of the picture agrees, no-trade rules, a stated instrument
list. Cheap to encode, honest to label as *"process from X, unverified"*.

**2. Their edge — cannot be captured, and the evidence it exists is weaker than it looks.** This is
the part to be careful about. We have tested what these traders say they do:

- **Tim / Cosasverdes:** all four claims failed surrogate testing; the origin grid collapsed out of
  sample. His two public BTC calls were scored — the 2021 call was wrong on all three counts (a
  "30k floor" broke to 17.6k, a 350k target against a 31k actual), and the 2024 call offered four
  paths that between them covered every outcome.
- **Camel Finance / cycle counts:** a shuffled random walk reproduces the claimed cycle length on
  every asset. It is a property of the detector.
- **The trader's-triangle interview:** seven claims tested, seven nulls.

"Clearly it is working for them" is the premise worth examining. What we have is social-media
presence and self-reported records, not audited track records — and a 25k→250k year at up to 6% risk
per trade is a **variance** statement as much as a skill one. That is not a claim that they are
frauds. It is a statement that the evidence available to us does not distinguish skill from
survivorship, and building a system on the assumption that it does would be the largest unforced
error available.

**3. Would feeding me videos help?** For **claim extraction, yes, and it already works** — that
pipeline has produced eight tested claims in two days, and the `trading-video-analysis` skill covers
it. For **learning to trade like them, no.** I cannot acquire a trading intuition from watching
someone describe theirs, and a skill file that reproduces their vocabulary without their edge
produces confident-sounding noise — which is *worse* than nothing, because it sounds authoritative
in a domain where sounding authoritative is most of the con.

**Recommendation:** build **one** skill, and make it *our* method rather than an impersonation — the
control battery, the traps that have produced false results here, the ledger's standing findings, and
the rule that nothing scores until it beats a control. Then, separately and clearly labelled, encode
individual traders' **checklists** as unverified process aids for discretionary use. The first is an
edge in itself: it is the reason this project has 29 measured results instead of 29 opinions.

---

## What I would do first, if any of this is greenlit

1. **`WikipediaPageviewsProvider`** — smallest, and the only item where delay costs something that
   cannot be recovered.
2. **The dossier modal with pattern description** — justified by the discretionary audience alone,
   depends on no research result, and is the most visible improvement to the terminal.
3. **`CoinMarketCapProvider` + the eleven-check scorecard** — deterministic, defensible, and it
   answers "run scam report" without an LLM.
4. **Analyst revision breadth** — the one research test worth running before building more layers.
5. The signal service **last**, when there is more than one control-tested edge to serve.
