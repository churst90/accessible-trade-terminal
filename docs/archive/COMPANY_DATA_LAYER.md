# The company and macro data layer — design notes

Written 2026-08-02 from a design discussion. **Nothing here is built.** This is the record of the
reasoning so the decision can be made later without re-deriving it.

---

## Who this is for — and why that is the first design decision

The data layer serves **two audiences with different needs**, and most of the design follows from
taking both seriously:

1. **Automated strategies**, which need a small number of orthogonal, point-in-time-honest features
   that have survived a control.
2. **A person reading the data and deciding for themselves.** Discretionary trading is a
   first-class use, not a byproduct.

These pull in opposite directions more often than they agree:

| | strategies want | a person reading wants |
|---|---|---|
| coverage | a few features that earned their place | everything available about this company |
| null results | excluded — a null feature is noise | **still shown** — "short interest is 22% of float" is worth knowing whether or not it predicts returns |
| freshness | consistent as-of timestamps | "how old is this number" stated plainly |
| absence | a missing value to handle | **"no insider filings in 90 days" is information**, and a blank row is a bug |
| shape | a numeric series | a narrated hierarchy: headline first, structure beneath |

**The rule that reconciles them is the one the strategy library already uses: display everything
with its provenance, score only what has earned it.** A fact and a recommendation are different
objects. The terminal shows facts; scoring is gated on evidence the same way strategy provenance is
(see `STRATEGY_LIBRARY_POLICY.md`).

**This changes the cost/benefit of the whole project.** If every edge tested from this data comes
back null — which our base rate says is the likeliest single outcome — the terminal has still gained
a company dossier that users want and that no accessible platform offers well. The research is the
upside; the dossier is the floor. That is a much easier project to justify than one that only pays
off if the research works.

---

## The uncomfortable frame

The alpha ledger holds **29 measured edges, and nearly every one is a transform of price and
volume**. The three genuinely orthogonal data classes tested so far — on-chain valuation, COT
positioning, funding/OI crowding — were **all null**.

So "technicals alone will not give good entries" is right, and our record on external data is
**0 for 3**.

That is not an argument against this layer. It is an argument about *which* external data. All three
failures were **positioning and valuation** data: slow-moving, widely watched, already in the price
by the time it is published. The classes with documented priors are different in kind — they are
about **revisions and events**, not levels.

---

## Presentation: both surfaces, one rule

> **Anything with a time axis belongs on the chart. Anything that is a snapshot of many facts about
> one company belongs in a modal.**

### On the chart
Estimate revisions, short interest, insider transaction counts, filing-event rates, attention
series. These become analytics series through the existing `IDataOrchestrator.FetchOhlcvAsync`
path — meaning they arrive sonifiable, navigable with Ctrl+Left/Right, usable in the condition tree
and in screens, with no new plumbing. Discrete events (an 8-K, a Form 4 cluster, an earnings date)
render as markers so their position relative to price is audible.

### In a modal — the company dossier
Suggested `Alt+I`. Tabbed sections, each a walkable table, mirroring the data classes below.

**The accessibility requirement that outranks the layout:** the modal opens with a **spoken
headline**, not a table.

> "Apple, as of yesterday's close. Three of eight tracked items changed this week: estimate
> revisions turned up, insider selling continued, short interest unchanged. Two items have no data."

Then the structure beneath it. A person reading by ear must never have to walk a table to discover
whether anything happened. Every section states its **as-of date** and says so when it is empty.

### What the dossier must not do
No stars, no rankings, no "strong buy". Facts with sources and dates. This is the same policy the
strategy library now enforces, for the same reason: a marker in the product's own UI reads as the
product's endorsement.

---

## What is actually available from companies

Ordered by whether there is a documented prior, because the list of *available* data is far longer
than the list of *useful* data.

### Has a real prior
- **Analyst estimate revisions** — *breadth* (up minus down, over total) and *direction of change*,
  not the level of the estimate. One of the best-documented anomalies in the literature and the
  strongest single candidate here.
- **Earnings quality / accruals** — the Sloan accrual anomaly; computable directly from statements.
- **Short interest and borrow cost** — documented, and mechanically orthogonal to price.
- **Insider transactions (Form 4)** — cluster *buying* specifically; modest but real.

### Available, weaker or unproven
Filings (8-K material events, 10-K/10-Q, 13D/G activist stakes, S-1) · 13F institutional holdings
(a 45-day lag guts most of it) · options implied vol, skew and unusual activity · corporate actions
· guidance · earnings-call transcripts · litigation · patents · FDA calendars · index-membership
changes · supply-chain relationships.

### Alt data
Web traffic, app downloads, job postings, card-transaction panels. Mostly expensive, heavily mined
by people with better data than we would buy, and a poor early bet.

### For crypto, the equivalent list
Token unlock schedules and cliffs · holder concentration · exchange listings · pair age and
liquidity depth · venue volume distribution · governance proposals · hacks and exploits · chain the
token is built on. This is the machine-collectable subset of the 24-point vetting guide (see
`LAB_DESIGN.md`), and the same prediction applies: likely a **veto**, not a timing signal.

**Honest framing:** the first list is a *research queue*, not a feature list. Our base rate says most
of it will come back null. The reason to build it anyway is the dual-audience argument above — and
the fact that it is the only genuinely non-price information we would have.

---

## Providers, and why FMP was never going to cover this

**SEC EDGAR is the anchor, and it is free with no key.** Financial statements via XBRL, 8-K events,
Form 4 insider trades, 13F, 13D/G. FMP's paid tiers are substantially *reselling this* — which is
why "I thought FMP provided all of this" was a reasonable expectation and still the wrong bet.
Verified 2026-08-01: one unauthenticated call to `data.sec.gov` returned 338 diluted-EPS datapoints
for AAPL back to 2007.

**Already have:** FRED (macro actuals) · Alpha Vantage (estimates, surprises, back to the 1990s) ·
FMP free tier (quotes, statements, treasury rates, sector performance).

**Worth adding, in order, none requiring payment to start:**

| source | what it gives | key |
|---|---|---|
| **SEC EDGAR** | filings, financials, insider, 13F | none |
| **GDELT** | global news-event database, timestamped and entity-tagged — the best free news-event source that exists | none |
| **Wikipedia Pageviews API** | hourly attention per entity back to 2015 | none |
| Reddit / StockTwits | mention volume, post counts | free tier |
| Finnhub or Tiingo | headline news if GDELT proves too coarse | free tier |

**Wikipedia pageviews deserves singling out.** It is a genuine **point-in-time attention series** —
hourly, historical, and impossible to contaminate retroactively. That property is rare and, as the
next section explains, it is the property that decides whether a sentiment feature is testable at all.

---

## Sentiment, inference, and the LLM question

The tension is real: turning text into a number *is* inference, which is what a human trader does,
and no amount of arithmetic substitutes for it. The resolution is not "no LLM" or "LLM in the loop":

> **The LLM belongs in the ingestion pipeline, not the decision loop.**

It reads a filing or an article **once, at ingestion**, and emits a structured record — entities,
event type, direction, magnitude, confidence — stored with the timestamp it was generated at.
Everything downstream is deterministic code over stored data. The standing constraint (no LLM in the
trading loop) is preserved exactly. The existing AI Analyst modal is the precedent: user-triggered,
advisory, never in an order path.

### The trap that decides whether any of it is trustworthy

**If an LLM scores a 2019 article today, it already knows what happened next.**

This is lookahead contamination, and unlike the Cipher B divergence lookahead this project caught in
June, it is **invisible** — there is no offset to shift, no confirmation lag to add. A backtest of
LLM-scored historical sentiment will look excellent and mean nothing.

The consequence is uncomfortable and clean:

> **LLM-derived features can only be validated forward, from the day recording starts.**

That is months of waiting. Which means: if that answer is wanted next year, the recorder has to
start now. Nothing else about the design changes that.

### What can be backtested honestly today, with no LLM at all

These are counts and rates, not judgments — so history is safe:

- estimate revision **breadth** and **dispersion** (analyst disagreement)
- surprise magnitude versus consensus
- filing-event **rates** — 8-K frequency against that company's own baseline
- insider transaction counts and clustering
- **Wikipedia pageview anomalies** against the stock's own trailing attention
- Reddit / StockTwits **mention-volume rate of change**

Note these are the "**ranks and ratios, not levels**" principle already settled on for price —
applied to non-price data. The same reasoning that made percentile thresholds the right shape for
indicators makes rate-of-change the right shape here.

### One data point we already have on sentiment
Fear & Greed tested **redundant**: the gated spec was byte-identical to the ungated one, because
when the oscillator buys at a cycle bottom at support you are *already* in extreme fear. Sentiment
that is derived from price is not orthogonal to price. Anything in this layer has to be checked for
the same collapse before it is believed.

---

## Recommended sequence

**Do not build the data layer. Build one feature and let the result decide whether the layer is
worth it.**

1. **Analyst estimate revision breadth**, tested with the cross-sectional harness that produced the
   project's best result. Strongest documented prior on the list; Alpha Vantage already supplies the
   inputs free; and it is a cross-sectional ranking signal, the one family where a confirmed edge
   already exists, so the machinery and the controls are known.
   - If it ranks forward returns, the case for the rest of the layer is made empirically.
   - If it is null, that cost a day rather than a month — and it says something about whether
     non-price data is going to work here at all.
2. **In parallel, and nearly free: start the recorders.** Wikipedia pageviews and GDELT event counts
   only become useful with history, and history only accumulates if collection starts. This is the
   one part worth doing *before* any decision, because delay is the only cost that cannot be
   recovered later.
3. **The dossier modal**, which is justified by the discretionary audience alone and does not depend
   on any research result.
4. Everything else, gated on what step 1 says.

**Not yet queued in the edge registry** — these are candidates pending a decision, and the registry
is for claims someone has committed to testing.

## A realistic expectation

The stated goal is to spot trends "just as or before they're happening". The honest prior stands:
Narang's number for a *successful* quantitative strategy's out-of-sample R² is 0.03–0.04, and the
strongest thing in our ledger is a 0.37% per-30-day spread. What this layer can realistically
deliver is **attention direction and context for a human decision** — which is exactly what the
action-alert design targets, and exactly what the discretionary half of the audience is asking for.

Cross-references: `LAB_DESIGN.md` (the edge registry and the claim-intake seam) ·
`ALPHA_LEDGER.md` (what is already known) · `STRATEGY_LIBRARY_POLICY.md` (facts versus
recommendations) · `ANALYTICS_DATA_PROVIDERS.md` (the provider pattern this would extend).
