# README status history (archived)

The status sections `docs/README.md` carried from 2.3.0 (2026-08-01) to 2.11.0 (2026-09-21), moved here on 2026-11-21 so the README opens on the current state. Nothing in them was edited.

---

## Current Status — 2.11.0 (2026-09-21)

**2.11.0 is the release where two keys that were documented, bound and tested turned out never to
have reached the terminal at all.** `;` and `Shift+;` choose which of several overlapping chart
formations leads the readout and release that choice, and neither key was in the browser's
trapped-key list — so a press returned before it reached .NET, and a browser reports `Shift+;` as
`:`, which nothing folded back. Pinned between two points of a pattern with no way out was the
symptom. Both keys arrive now, and the whole shifted top row is folded rather than the one key
that broke. Ctrl+Left and Ctrl+Right also stop borrowing a sibling's line: on a pane with more
than one reference line — Aroon, where Up and Down swing about 50 and the Oscillator about zero —
the jump now uses the line the focused component declares and reads that component's own data.

The ear follows the eye onto the log scale. Alt+L already changed the picture; now a level-pitched
component in the price pane takes its pitch from where the value sits on the scale as drawn, using
the renderer's own arithmetic rather than a copy of it. Volume, meanwhile, had been playing on the
candle body's pitch — the bed sitting on the thing it was meant to sit under — and now sounds a
perfect fourth below it, re-derived on restore so a workspace saved before this release heals
itself.

The rendered chart was photographed for the first time, by a probe that drives a real browser
through both scales, six zoom steps, Heikin Ashi and nine indicators. Four drawing defects were
visible in almost every picture and none of them had been caught by a suite that had just measured
73.5% on that layer: y-axis labels drawn over each other, the crosshair badge painted over the
nearest gridline label, an empty region to the right of the data labelled "07/19 07/19 07/19", and
a legend running out of the bottom of its own pane.

Underneath, the suite went from 7,666 to 7,910 across five break-it-and-see campaigns aimed at
layers never tested that way. The accessibility layer — the code deciding what a blind user hears
and when — scored 82%, the highest rate in this repo's history, and every gap it exposed was about
*when* the terminal may speak: pressing N could start reciting history, an unconfirmed tick could
announce a cross that then un-happens, and switching narration on could fire a cross for every
overlay at once. All three now have a test on the line. The indicator maths scored 10.5%, the
lowest ever measured here, and that number is published rather than buried; thirty-two of its
thirty-four gaps are closed, each proved by re-breaking the code, and the other two change no
behaviour. `Services/Strategies` is next.

Anything that lands after this tag goes under a new `## [Unreleased]` heading in
[`CHANGES.md`](CHANGES.md) and is not in the 2.11.0 binaries.

### 2.10.0 — safer money, one rule for where words go, and a pane for every oscillator (2026-09-13)

**2.10.0 is the release where money got safer, words got one rule about where they go, and every
oscillator got a pane of its own.** Bracket legs are reduce-only and share one pair, so a stop
that fires after you have closed by hand cannot become a fresh position the other way; a key
marked Paper on a venue with no practice environment is refused by name instead of routed live;
the dashboard's key dropdown now chooses the key that signs and the status bar carries a **LIVE**
badge beside the PAPER one, because an absence is not a signal a screen reader can find. Every
trading plugin is held to one conformance suite — thirty-three rows were red on its first run and
ten plugins were fixed.

One rule now decides where a sentence goes: whatever is about the chart in front of you is spoken
there, and everything else — another tab, a symbol with no tab, the terminal with the browser
closed — is a system notification. Three default-off switches became one that defaults on, and
closing the browser tells you what will still be watched.

On the chart, thirty indicators had been sharing a single scale, which is why an RSI beside a MACD
was almost flat; each oscillator now has its own pane and saved workspaces heal on load. Seventeen
indicators declare their natural range so RSI 70 is the same note on every chart, a reference level
says what it is and what it means (ADX's 25 announces "strong trend"), the `0` key marks the pane's
declared neutral, and the fifteen drawing entries that used to sit in Add Indicator and produce an
empty series are gone — drawings are placed with Alt+D or their chords, and every point is asked
for by name.

Two things this release could not verify on the machine it was built on: the Windows toast notifier
and tray-on-close are still only compiled, never run, and the drawing prompts and their spoken
answers have not yet been heard with a screen reader. Both are in `docs/TODO.md`.

What postdated that tag is in the 2.11.0 section above.

### 2.9.0 — the line goes where the line means something (2026-09-06)

**2.9.0 is the release where a reference line goes where the line means something.** The `0` key
marks the pane's own neutral rather than the number zero — 50 on an RSI, −50 on Williams %R, still
zero on MACD — because a line at the floor of a 0-to-100 oscillator can never be crossed, never
sounds, and can never be navigated to; Ctrl+Left and Ctrl+Right reach an RSI's 50 line for the
first time (sixteen indicators spell that line four different ways and the crossing engine
recognised one of them); braille has its own Settings tab with Shift+F4 landing on it; the Order
book button is unconditional again and the dialog says whether the venue has no depth feed or
merely returned none; and drawing tools moved to the bar under the chart with the other things you
add to a chart.

Underneath, one reflection guard replaced the genre of test that names a single dropped field, and
found four defects nobody had reported — Market Structure's swing markers anchored to the value
instead of above and below the bar, a Properties checkbox never read back, Ctrl+Z resetting four
indicators' text settings, and a hand-picked colour losing its protection from the next theme
change. The keyboard and alert layers were measured by sabotage for the first time.

What postdated that tag is in the 2.10.0 section above.

Two things 2.9.0 could not verify on the machine it was built on: the Windows toast notifier
was still only compiled, never run, and the two-layout Alt+Shift question on Windows needs a Windows
box. Both are in `docs/TODO.md`, along with `docs/archive/BACKGROUND_MONITOR_SCOPE.md` — the scoped plan
for receiving notifications with the browser closed on every head. Phases 0 to 3 of that plan
are on `main` since 2026-09-11: alerts, order events, bar closes and the narration ladder all
reach you with no browser open. Phase 4 (also on `main`, post-tag) settled the routing policy —
the chart in front of you is spoken, everything else is a system notification.

### 2.7.0 — everything you switched on now speaks (2026-09-05)

**2.7.0 is the release where everything you switched on actually speaks.** 2.6.0 gave the
terminal a voice; this one is about the places that voice was silent while the user had already
asked for it, and every item on it was reachable from the same question — *did the thing it is
supposed to produce actually appear?*

*Since the tag:* the graded pass over every area is [`REPORT_CARD_2026-09-05.md`](archive/REPORT_CARD_2026-09-05.md)
(real-money readiness D+ → B-; workspace persistence held at C+ because this pass found that N and M on
a component were undone by every restart). What postdates 2.7.0 is under `## [Unreleased]` in
[`CHANGES.md`](CHANGES.md); none of it is in the 2.7.0 binaries.

- **Thirty-five of ninety-nine indicators could not narrate anything at all.** Stochastic, CCI,
  MFI, ADX, ROC, Williams %R, TRIX, CMO, Chop, PPO, StochRSI and two dozen more: press `N`, hear
  *"narrating"*, then silence for the session. Only three had a hand-registered context
  definition; the rest print no markers, declare no zone lines and own no cloud. They narrate
  their own declared levels now, so a level the user MOVED narrates at its new value and nothing
  needs maintaining per indicator.
- **A plain EMA was in the same state**, for the same reason one level down: cross detection sat
  behind a flag only Cipher SR sets.
- **Cipher B's gold Triple Confluence dot was dropped from every playback that has ever run.** It
  is computed inside the oversold-crossover branch, inside the WaveTrend cross branch, so all
  three fire on the same bar — and playback's two-clause ceiling was filled in the order the
  provider declares its components. Ranking is by measured fire count now: the rarest leads.
- **A bar close was two utterances and the web head kept one**, so the candle arrived and the
  indicators did not. And the first bar to close after pressing `N` was structurally silent — the
  forming bar was being counted as history.
- **`NarrationRouteContractTests` is the guard the hosted deploy notes asked for** (§5n, "assert
  the artifact, not the incantation"): it switches narration on for every shipped indicator in
  turn, drives a real bar close, and fails on silence — with an exemption list that is itself
  audited, because an exemption list is where this defect class reconstitutes itself.

**2.6.0 is the accessibility release.** 2.4.0 and 2.5.0 were both fixing releases with no
features in them; this one has features, and all of them are in the same place — what the
terminal *says*, and whether the dialog you are in is the only thing on the page.

**Speech that arrives.** The measured headline: the status strip at the bottom of the page
had become a SECOND live region carrying the SAME sentence as the speech buffer, and Orca 51
purges queued polite messages when an assertive one arrives and then drops the assertive one
as a duplicate of what it just purged — so an `m` or an `h` did the thing and said nothing.
Measured on the AT-SPI bus with `tools/atspi-listener.py`, not inferred: the polite copy
reached the bus first on **6 of 16** presses. The strip is no longer a live region; it still
holds the last spoken sentence as visible text, and
`LiveRegionInventoryBrowserTests` asserts BOTH halves precisely so "we deleted the mirror"
cannot pass as "we stopped it announcing". The two invisible speech buffers are also cleared
~2 s after announcing, so the bottom of the page reads as one line rather than three.

**`inert` on the background.** `aria-modal="true"` is advisory — it asks the screen reader
not to describe the background and does nothing about focus. With the AT-SPI bridge attached
and Orca running, **6 of 14 modals lost focus to somewhere outside themselves**, every move
carrying an EMPTY JavaScript stack: dispatched by the embedder, not by page script. An app
cannot out-focus a mover it never sees. Eight region roots carry `data-background-region` and
`keyboard.js` sets and clears `inert` from `setModalStack`. It is opt-IN because the two ARIA
speech buffers are SIBLINGS of `<main>` — inerting a wrapper would have silenced the terminal,
not muted the background. Re-measured with the bridge on: **6 of 7 modals held their own
heading through +4000 ms**, and no sample in any instrumented run landed inside an inert
subtree. Two limits recorded rather than assumed away: the probe is intermittent by nature,
and `ModalBase` publishes the open event before the render, so on the web host the background
is live for one interop round trip after a dialog opens.

**F3 was killing playback, and a property setter was why.** `SonificationManager.IsEnabled`'s
setter called `Stop()`; the store assigned that property on every state change and the
sequencer dispatched a navigate action per bar, so with F3 off each bar cancelled the playback
producing it. Measured before touching anything: **2 bars of 200 with F3 off, 200 of 200 with
it on.** A property setter that stops a background job is a trap whatever guards it. Playback
now renders silence per bar instead, which makes it the terminal's narration mode — and as of
the same day it has something to narrate: **playback speaks marker signals and formation
outcomes**, composed with the time landmark into ONE utterance per bar, rate-limited by bar
distance and dropping rather than queueing.

**Settings has eight tabs, and the eighth is Narration** — split from Speech by TRIGGER
rather than topic: Speech is how the terminal says what you asked for, Narration is what it
says when you pressed nothing. Alert delivery left Settings entirely for the alerts dialog
(Alt+J → Delivery settings), next to the alerts it delivers, and now writes on commit instead
of on close, because Escape is how a keyboard user leaves a dialog and Escape was discarding a
typed SMTP password with nothing said about it.

Settings then stopped carrying two commit idioms at once: fifteen controls used to write
themselves the moment they changed, so the dialog's own Cancel could not reach them. They are
staged now and written by Save, which announces; the Appearance tab keeps its live preview and
Cancel reverts it. Getting a real chart into the browser harness — the highest-value untaken
item in `docs/TODO.md` — closed the Object Tree's vacuity and turned up three defects on the
way: a dataset name with a space in it could never be charted, and the Indicator bar's series
picker never watched the store, so it could read empty on a chart with four series on it.

**One path for candle patterns.** Four routes name a candle — the bar that just closed, the
live bar as it forms, the arrow keys reading history, and the detail key. Two used the
twelve-pattern analyser; the other two each carried a private single-bar classifier with its
own thresholds. So engulfing, harami, morning star and three white soldiers were audible only
if you happened to be listening live, and on the shapes the copies did know they could
disagree — a 92% body was a marubozu when scanned and an ordinary candle when it closed. Worse,
only the analyser looked at the trend, and a hammer and a hanging man are the same candle told
apart only by the trend into them, so the copies named every one a hammer. One classifier and
one vocabulary now, in `CandlePatternSpeech`. A fourth defect fell out of the join: the live
bar is replaced in place as it ticks, so the intra-bar event's `PreviousBar` was an earlier
snapshot of the bar being classified — an engulfing tested against itself.

**"Reset paper account" asks first (WCAG 3.3.4).** It destroyed the balance, every open
position and the whole trade history on the first click, with no undo. Two-step in place now,
speaking the stakes before the keys, with Escape backing out of the question rather than the
dialog.

Suite **6,646** (`--list-tests`
reports 6641, which is the number this file must match and `doc-drift.yml` checks — the two
differ because `--list-tests` names a `[Theory]` once and a run expands its cases), plus
**207** browser tests and three JavaScript suites (63 + 19 + 15). **0 CRITICAL and 0 HIGH
items open, and the accessibility audit's numbered NEXT list is now empty.**

## Earlier in 2.6.0 (2026-09-03)

**The Object Tree never said whether a node was open or shut, twelve controls announced
words that were not on the screen, and nine of the sixteen drawing types could not be
edited without a mouse.** Those were the last three items on the 2026-09-01 accessibility
audit's list, and all three are closed.

Putting `role="treeitem"` on a `<summary>` REPLACES the browser's native disclosure
mapping, so the expanded state comes from `aria-expanded` and nothing else — and the
Object Tree had none anywhere. The keyboard handler knew the state and the user never
heard it. It is rendered now and kept honest by the browser's own `toggle` event, which
fires on every open-state change however it was caused; the ARIA review caught that the
keyboard handler read `aria-expanded` BEFORE the live `<details>`, which with a queued
`toggle` would have let a pane be collapsed and never re-opened. A projected state must
never outrank the state it projects.

Twelve controls failed WCAG 2.5.3 Label in Name — the audit counted ten, and missed two
because 2.5.3 compares a contiguous word sequence and "Watch" is not the word
"Watchlists". The guard that should have caught them asserted that an `AriaLabel` was
PRESENT, which could not fail for any of the twelve, since every one of them had one. It
asserts containment now, and is joined by a render sweep — rendering is what makes
`<ToolbarIconButton />`'s thirty-odd call sites visible at all, since no `<button`
regex can see a component tag.

And the drawing coordinates: the backlog said an existing drawing's anchors could only be
moved with a ten-pixel mouse drag. That was partly stale, and what replaced it was
bigger — measured against what the calculators actually read, 27 coordinates across nine
drawing types had no keyboard editor of any kind. A schema in Core now says which anchors
each type has and what to call each one, the dialog renders from it, and a test re-derives
the same census from the calculator sources so the two cannot drift.

**Before that, the same day: error state is conveyed now — it was conveyed nowhere.** The 2026-09-01 accessibility
audit's four sweeps returned zero across both projects (`aria-invalid`, `aria-required`,
`required=`, `aria-disabled`), and the consequence was demonstrated twice before anything
was written: type 0 into the order ticket's Quantity and the **Submit button VANISHES** —
native `disabled` removes a control from the tab order and from the screen reader's button
list, so for this product's user it is not "greyed out", it is deleted — with nothing said
and the quantity field still announcing itself as valid; and POST an empty email to the
sign-in page and the response carries the validation message with **no `aria-invalid`
anywhere in the document**. Thirty-four buttons carried a dynamic `disabled`; the nine
account pages had never been touched by a test of any kind.

Both halves are fixed. `GatedButton` keeps a refused button reachable, announces it as
unavailable, wires a human reason to `aria-describedby`, and swallows the activation while
speaking that reason — with the gate re-read at click time, so the double-Enter guard on
the money path is exactly as strong as it was under `disabled`. Four tag helpers give the
account pages `aria-required`, `aria-invalid`, focus on the field that actually failed, a
visible required marker, and an "Error: " title prefix.

**Then, the same day: the help dialog had one heading for 471 lines.** `HelpModal`'s
eighteen sections were bold `<summary>` text — a heading to anyone who can see the weight,
and nothing at all to the population this product exists for, on the one screen where
heading navigation matters most. A sweep over every dialog found the same shape in five
more: 27 disclosure sections, not one of them a heading. They are `<summary><h3>` now, which
is the only construct that works (`<summary>` must be the first child of `<details>`, so
wrapping it in a heading breaks the disclosure), and Chromium's real accessibility tree —
read over CDP, not inferred from a role query — reports all eighteen as unignored level-3
headings. The backlog's *other* accessibility item, four containers said to leave the app
with zero navigation landmarks, turned out to have been fixed two days earlier; what was
missing was a guard, since the existing scan bans the bad role and pins nothing in its
place. `StatusBar` — which carries the PAPER badge, the one persistent sign that orders are
simulated — was outside every landmark with nothing focusable in it, and is now a named
region.

**Most recent (2026-09-03): the hosted heads stop segfaulting, and the address named the line.**
Both took about two SIGSEGVs a day for a month — 20 in 31 days — always at or within seconds of
`Browser circuit closed`, and all 16 captured faults byte-identical: `signo 11 code 0001
addr 0x8`. That address is not a hint, it is a field offset. SkiaSharp zeroes `SKObject.Handle`
on `Dispose()` with no managed guard, so a later call passes NULL into native Skia and faults at
that property's own offset — and across 43 disposed-object operations only `SKFont.Size` lands on
`0x8`, while `SKPaint.Color` gives 0x30 and a disposed canvas 0x0. One statement matched:
`ChartRenderer.cs:82`, the first native touch in every frame. The renderer is `AddScoped`, so the
circuit scope freed it at teardown while `ChartArea` was still drawing inside a `Task.Run` nothing
tracked. `ChartRenderer.Dispose()` is empty now, and that IS the fix — a `_disposed` flag closes
only the sequential window, while SkiaSharp's own finalizer cannot run until no in-flight frame
still holds the renderer. Restoring the two Dispose calls crashes the test host, which is the
production fault reproduced in-suite. In the same pass: **a drawing no longer says "no data" at
the live edge** (drawings were skipped by every per-tick recalculation, so their arrays froze at
the bar count they were born with — and the same clause hid a scrollback misalignment), **every
toolbar tooltip had been silent for a day** (an `aria-describedby` resolving to the empty string
is not "no description", it is a description of "", and it suppresses `title`), and the top
toolbar now has one convention: the visible label is the accessible name, and the chord is spelled
out in words because "+" is handed to a speech synthesiser.


## Earlier status (2026-08-31)

**2.5.0 is another release with no features in it.** Everything since 2.4.0 (tagged 2026-08-28)
went to provider-layer correctness: the Twelve Data / Finnhub symbol lists (an empty HTTP 200
"success" and a 2,000-symbol alphabetical cut that made TSLA unselectable), the API-keys modal
saving keys under a provider name nothing answered to, one chart quoting three different closing
prices, and the status-blind HTTP reads — nine call sites that read a response body without
consulting the status code, the worst of which announced **"order placed" for an order the venue
never booked** (MEXC and Bitstamp, on any gateway error during placement) and reported **an
expired key as an account with no balances** (Kraken, Bitstamp, Coinbase, MEXC). Suite **6,011**
in both configurations (`--list-tests` reports 6015, which is the number this file must match and
`doc-drift.yml` checks — the two differ because `--list-tests` names a `[Theory]` once and a run
expands its cases). CI green on all three workflows that run on a push. **0 CRITICAL and 0 HIGH
items open.**

**Most recent fix (2026-08-30): one chart was quoting three different closing prices.** Reported
from live use on a Bitstamp BTC daily chart with Heikin-Ashi on — the browser title, the candle
readout and the close line each said a different number for the same bar, and the close line said
two different numbers depending on whether you had just arrived at it or were arrowing along it.
Four definitions of "the close" were live at once. The contract is now explicit and pinned by
tests: **the title bar is the live raw close and never tracks the cursor, the close line is raw
whatever the candle style is, and the candles are the only readout expected to differ** — because
a Heikin-Ashi candle is a different candle. A second, unrelated defect fell out of the same probe:
a scrollback that brought back exactly one bar shifted every mapped component array one bar to the
left, which made the close line report the *next* bar's price at every historical bar.

**Four audits ran, and they were run by breaking things rather than by reading.** That distinction
is the whole result. The scripting/sandbox audit *compiled 25 candidate escapes* instead of
inspecting the walker, and four of them worked — `dynamic` switched the entire blocklist off. The
test-suite audit introduced **28 single-line regressions one at a time**, each followed by a full
run: **11 survived a green suite**, so the true mutation catch rate was 61%, not the 79% a naive
count gave — five mutants had come back falsely "caught" by one unrelated flaky test firing alone.
**Re-run on 2026-08-28 against the 5,754-test suite, the same 28 mutants now score 89.3%** — 25
caught, 3 survived, and this time the second pass found zero spurious catches and none of the four
flaky tests fired across 29 full-suite runs. That number is an upper bound and is **not the one to
quote**: those 28 mutants are the ones the test work since was written against. Two independently
chosen sets have run since, each blind of every earlier list — **A2c on 2026-08-29 scored 67.9%**
(19 of 28, all nine survivors boundary conditions, all nine now closed) and **A2d on 2026-08-30
scored 73.1%** (19 of 26, zero spurious, all seven survivors now closed). **73.1% is the current
honest figure.** **The three survivors were closed on 2026-08-28** — a moving average that
averaged a short window over a gapped source and called it an N-period SMA, a modal focus contract
that asked "did focus go somewhere valid?" instead of "where", and 22 dialog controls announced to
a screen reader as nothing but the word "Close" or "Cancel". Each fix was proved by re-applying its
mutant and watching the new test go red.
The browser-harness audit *is* the tooling: `AccessibleTrader.BrowserTests` drives a real Chromium
against a real Kestrel running the real WebHost, 128 cases in under a minute, with a CI job of its
own, and most of what it produced was refutation — across 24 cold-start routes, focus lands on the
declared target every time and Escape returns it every time. By contrast, the read-only assessment
that preceded them filed 283 findings unverified and its census turned out roughly half wrong.
**The standing rule in this repo is now: demonstrate the defect, or mark it explicitly unverified.**

**Two structural refactors landed.** `OrderPlacement` collapsed three disagreeing result classifiers
into one typed record — a refusal, an *uncertainty* and a fill are three things a trader must tell
apart by ear, and the old string-sentinel scheme let the code that produced a sentinel drift from the
code that recognised it. The trading dashboard was decoupled from the chart, which required teaching
the paper broker **cross margin**, because the margin-mode selector was gated on a capability the
broker could not honestly declare.

**Then 71 HIGH items and about forty MEDIUM ones, closed in batches, every fix demonstrated by
reintroducing it and watching a test go red.** The ones worth knowing about:

- **A credential leak across five providers** — the API key went in the URL, and `ex.Message` was
  written to the channel that gets *spoken*. The fix is a `KeyParam` declaration plus two scans: one
  that fails any unescaped key-in-URL site (there were 39 across six providers; only FRED escaped),
  and a second proving every `KeyParam` declaration actually escapes.
- **Two real security holes.** A plugin's `HttpClient` followed `302 → 169.254.169.254` *below* the
  allow-list handler; and a password reset did not evict an open Blazor circuit, so the old session
  survived its own reset.
- **Every authentication event was pooling into one shared file.** Razor Pages are not circuits, so
  `ICurrentUser` was never populated for them and all accounts' sign-ins, lockouts, email addresses
  and IPs landed in `users/anon/` — a directory whose name says it holds no user data and which the
  pruner skips.
- **Chart-scope audio had clipped since it was written.** Nobody had measured it: an ordinary
  18-voice layout peaks at 5.5× full scale and a saturated plan at 21.5×, because the engine summed
  every voice straight into the host buffer. Now a brickwall limiter — gain riding, not waveshaping,
  because timbre carries meaning here.
- **One bar could be announced at two different times.** Bars are `DateTimeKind.Utc`, so one code
  path printed UTC and another printed local. `SpeechTimeFormatter` now owns the conversion, with a
  three-tier guard, because a timezone-agreement test is *vacuous* on a UTC build agent and both CI
  and the deployment box are UTC.
- **Kraken's History tab was empty for BTC/USD** (the filter spelled it `BTCUSD`; the venue returns
  `XXBTZUSD`), and KrakenFutures and Gemini answered a 2019 request with this morning's bars.
- **`OhlcvStore` stored a 31-day month a day early** and its insert-only dedup made that permanent.
- **The F1 help dialog was missing 37 bindings**, including every quick-trade chord.
- **Support versus resistance now has exactly one decider.** The same one-line invariant was got
  wrong twice in three weeks by two different wrong proxies — a component's audio frequency, then
  its spelling — and each was fixed only where it was noticed. `LevelPolarity.IsResistance` is the
  chokepoint, a scan guard makes a third variant unwritable, and the trap it carries is that a
  *break* has to be judged against the close BEFORE the break, since a break is by definition the
  moment price crossed the level.

**What is open.** The MEDIUM backlog, with no single obvious cluster left now that the provider tier
is closed — the candidates and the reasoning are in the START HERE block at the top of
[`TODO.md`](TODO.md), which is the authoritative order. Read that before picking anything up. Of the two
items that carried across the 2.4.0 tag in
[`RELEASE_2.4.0_VERIFICATION.md`](archive/RELEASE_2.4.0_VERIFICATION.md), the **stale mutation catch rate is
now closed** — re-measured 2026-08-28 at **89.3%**, with the three surviving mutants filed as the
next work — while the StrategyLab's flagship statistic remains **banner-marked provisional and owed
a re-run**: p = 0.0045 tested the winner of a 16-cell grid, and the survivorship stress beside it
could not fail by construction.

## Status at 2.3.0 (2026-08-01)

**The terminal ships tools, not opinions.** The app no longer chooses a strategy for the user, and
as of 2026-08-01 it no longer ships strategies at all: a fresh library opens **empty**, with an
explanation and two routes — build one, or import one. The thirty research specs moved to the
Strategy Lab's catalogue (`AccessibleTrader.StrategyLab/Catalogue/`), each carrying a recorded
provenance: what it was tested on, which controls ran, and the verdict. Scored honestly that reads
**1 control-tested, 5 walk-forward, 9 in-sample-only, 7 untested, 2 fragile, 6 falsified** — which
is precisely why shipping them as a starting library was an endorsement nobody had earned.
`StrategySpec.Provenance` travels with a spec through export and import so it can never arrive in a
library anonymous, and the library table shows it on every row including "Not recorded" for
user-built specs. Guard tests fail the build if a catalogue returns to shipping code, if a spec is
added without a verdict, or if the importer stops forcing auto-activate off. See
[`STRATEGY_LIBRARY_POLICY.md`](STRATEGY_LIBRARY_POLICY.md) for the reasoning and
[`STRATEGY_CATALOGUE.md`](STRATEGY_CATALOGUE.md) for the format, the CLI, and how to add a spec.

**2.1.0 shipped.** Market watch and screening, three analysis features, two chart modes, and an
application-wide theming system — 36 commits since 2.0.1.

**Watchlists and a screener** that reuses the Strategy Composer's own condition tree, so every
indicator and operator worked in it on day one, with a symbol picker and a quick screen builder.
A **respect report** measuring which levels and moving averages a market actually holds rather
than assuming. A **Market Structure** indicator (HH/HL/LH/LL, BOS, CHoCH; off by default), and a
**Value Deviation** indicator marking reversals relative to a rolling volume-profile POC. **Bar
replay** and **split view**. Toolbar controls for all of it — six features had shipped working,
tested and keyboard-reachable with no button anywhere on screen.

**Theming now covers the whole window**, not just the chart: eleven built-in themes (Steel Gray
as the new default, plus Blackout, Classic, Midnight Blue, Amber CRT, Walnut and Paper), a theme
editor exposing all 33 colours, and user themes that save, load and share as text.

Real fixes along the way: boolean indicator parameters were silently dead app-wide; a Cipher SR
backtest lookahead; two `DrawRect` bounds bugs; a toolbar that never watched the state it
displayed; a colour-vision gap in three themes that had already shipped; a Razor comment that
crashed the Settings dialog while compiling clean; a shortcut collision only the WebHost remap
would have produced; split-view mouse mapping; and Text Label drawings, which turned out to have
no way to enter text at all.

Suite 2109 → 3720, plus two JS suites (15 gesture, 13 keyboard).

**2.0.1 shipped.** A point release on top of 2.0.0: accessible mobile drawing (a "Place
drawing point" touch button so touch-only users can complete multi-point drawings),
previous/next-series touch buttons, sparse-signal speech ("N signals in view" instead of
"no data" on indicators like Cipher B), an opt-in gradient chart background, a cloud-fill
crossover gap fix, and a new keyless **Deribit** analytics provider (DVOL volatility index
+ realised volatility for BTC/ETH). See [`WHATSNEW.md`](WHATSNEW.md).

**2.0.0 shipped.** The 2.0 hardening line reached release: every provider brought to a
reliable standard, every exchange moved to a direct API (no shared exchange SDK left in
the tree), background monitoring that never fails silently, and the broker/order path
made trustworthy. Since v1.5.0, main has landed: Tier 1 correctness
(workspace persistence for active strategies + drawings, exchange-native
brackets on Tradier/Schwab with per-broker honesty on Kraken, fill history on
all six trading brokers, alert-failure surfacing, pipeline race fixes), the
keyed-feeds pipeline refactor (per-chart data feeds, opt-in live background
tabs, instant warm tab switches, live bar-close strategy evaluation, the
kline volume-inflation fix) plus its adversarial hardening pass, order-
lifecycle voice completeness (cancels announced, bracket legs audible),
session autosave with resume-on-open, and while-you-were-away position
reconciliation with spoken P&L. See `CHANGES.md` and `ROADMAP_2.0.md` for
the full record.

## Previous Status (2026-04-24)

**Tier 3 TODO sweep complete 2026-04-24** (same-day as Tier 1 + 2). Six
substantive architectural items shipped: BuildSetupTab decomposed into
`ConditionTreeEditor` + `RiskPlanEditor` + `SummaryExport` siblings
under a ~70-line coordinator; `IStrategyModalCoordinator` wraps six
services and drops StrategyModal's DI count from 10 → 5;
`AudioEngine.SetVoice` hot path now zero-allocation (replaced
`wave.ToLower()` with `ParseWaveform` / OrdinalIgnoreCase);
`IEventBus.SubscribeCoalesced` / `SubscribeSampled` expose Rx
`Throttle` / `Sample` for burst coalescing; script worker gains CPU
quota (kills sustained >90% over the poll window) + 16-worker
concurrency gate with clear over-cap error; SMTP +
Telegram alert delivery channels (`IAlertChannel` contract,
`EmailAlertChannel`, `TelegramAlertChannel`, `AlertDeliveryService`
fan-out). Deferred: DLL plugin strategy loader (multi-day) and Settings
Alerts-tab UI (2-3h). 537/537 tests still green. See `CHANGES.md`
2026-04-24 Tier 3 entry.

**Tier 1 + Tier 2 TODO sweep complete 2026-04-24.** 10 items shipped —
Ctrl+L/R focused-series-aware refinement (focused trendline walks only
that drawing; continuous-points lines announce "No points of interest"
instead of silently sweeping all trendlines); CIPHER_A WT Momentum
Gradient now a queryable `SignalDescriptor` so strategies can gate on
momentum strength; `BarDetailService` layers Bollinger squeeze/expansion
and MACD-vs-Signal crossover narration after raw values; `AlertEvaluator`
resolves POC via `ILevelService` for live POC-crossing alerts; BuildSetupTab
surfaces Score + Sequence logic operators, `MinLevelStrength` for
level operators, expand/collapse on groups, and Within-N for every typed
variant; drawing anchors now land in the 20-bar future-space margin via
`DrawingCalculatorHelper.ResolveAnchorIndex`; VPVR backtest replay chain
pinned by 4 new tests. Later on 2026-04-24: Phase 5 accessibility rework
(`ChartArea` `@onkeydown` fallback dedupes with window JS handler;
`OrderBookModal` aria-live status region + per-direction earcons),
three-tier level-crossing earcons (`LevelCrossingMonitor` approach + sustained
on top of existing crossing), divergence pivot-to-pivot slanted line, cross-
pane Anchor regime tint on the price pane, Roslyn strategy persistence + v18
migration off BNVISION alias, cloud-sonification scoping rule codified (wave
fills between oscillator-pair boundaries stay visual-only), ARIA tree arrow-
key navigation with meaningful labels on strategy + object trees. 577/577
tests green. See `CHANGES.md` 2026-04-24.

