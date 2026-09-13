# What's New

<!-- UNRELEASED. This file holds the CURRENT RELEASE ONLY; the section below is what the next
     tag will say, and it must be moved into CHANGES.md if a tag is cut without it. Check with
     `git diff <lasttag> HEAD -- docs/WHATSNEW.md` before every cut — this file has accumulated
     post-tag entries under an old heading before. -->

## Unreleased — safer money, one rule for where words go, and a pane for every oscillator

### Money

- **A stop can no longer open a position the other way.** Bracket legs are reduce-only and
  share one pair, so a stop that fires after you closed by hand cannot become a fresh short.
  A protective leg on the wrong side of the fill is refused out loud instead of attached.
- **A key marked Paper on a venue with no practice environment is refused, never routed
  live.** Bitstamp, Coinbase, Interactive Brokers, Kraken, Kraken Futures (its demo was
  withdrawn on 2026-07-14), MEXC and Schwab have no sandbox; the refusal names the key and
  the fix.
- **The dashboard's key dropdown now chooses the key that signs.** It is labelled "API key
  for this order"; options read "Live, real money" or "Paper, practice environment"; the
  dashboard speaks the account when it opens. The status bar carries a **LIVE** badge beside
  the PAPER one, because an absence is not a signal a screen reader can find.
- **The live review re-arms if the ticket changes.** "Ticket changed since the review.
  Nothing was sent."
- **Every trading plugin is held to one conformance suite.** Thirty-three rows were red on
  the first run; ten plugins were fixed. "Covered" now means events can arrive, not that a
  subscribe call returned.
- Order fills, stops and cancels reach you with the browser closed.

### Where words go

- **One rule.** Whatever is about the chart in front of you is spoken there. Everything else,
  another tab, a symbol with no tab, the terminal with the browser closed, is a system
  notification. Minimising the browser still counts as being there.
- **One switch, on by default.** "Events you cannot see" in Alt+J → Delivery settings
  replaces three default-off switches in a different dialog. The "shortest timeframe to
  announce" floor beneath it now quietens the narration ladder as well as the bar close.
- **Closing the browser tells you what happens next.** One notification, saying whether
  anything will be watched and naming the switch if not. A reload does not trigger it.
- **The monitor re-reads its switches** on every pass instead of once at start, says so once
  when its polling fails and again when it recovers, and speaks a notification aloud if the
  notification daemon did not take it.
- **Alerts with the browser closed read a real chart.** Indicator, point-of-control, trend,
  zone and condition-tree alerts are evaluated headless; the only unwatchable alert is one
  scoped to "the current chart", or a POC alert on a chart with no profile. Bar closes and
  the full narration ladder for your saved tabs arrive the same way. The hosted site never
  runs any of this.
- **Other open tabs get the full ladder**, not two clauses. Closing the browser no longer
  tells you more than leaving it open.
- **A resumed session did not know which symbol was on screen**, so the focused chart's bar
  closes were notified and its alerts skipped. Fixed on resume and on tab switch.
- **The Windows app's X button minimises to the tray by default** and says so. Settings →
  General → "Minimize to tray on exit" turns it off. *Compiled, never run on a Windows
  machine.*
- The tray's silence item says what it silences, "alerts and bar closes", and order fills
  always come through it. Ctrl+Alt+Shift+M reports both monitoring halves.

### The chart

- **Every oscillator has a pane of its own.** Thirty indicators shared one scale, so RSI beside
  MACD was almost flat. Saved workspaces heal on load; Alt+PageDown walks one more pane per
  oscillator. The Add Indicator dialog says whether an indicator joins the price pane.
- **Bounded indicators keep their full scale at every zoom.** Seventeen declare their natural
  range (RSI, Stochastic, MFI, ADX, Williams %R, CMO, Aroon, Cipher B and more) and four
  declare a floor (ATR, standard deviation, historical volatility, Ulcer); RSI 70 is the same
  note on every chart.
- **A level says what it is, what it means and whose it is.** ADX's 20, 25 and 50 and
  Choppiness's 38.2 and 61.8 name the band you entered ("strong trend", "ranging");
  Ctrl+Left and Ctrl+Right stop at those edges; playback speaks the crossing. A level you
  hide stops being a navigation target, an earcon and a narration event. MFI draws the two
  colours it declared.
- **0 toggles the pane's declared neutral** (50 on RSI, zero on MACD, −50 on Williams %R);
  it used to answer "already marks 50" forever. Axis labels sit on gridlines.
- **Drawings left the Add Indicator dialog.** Fifteen entries there produced a series with
  nothing in it. Drawings are placed with Alt+D or their chords, and every point is asked for
  by name: "Risk Reward: entry at 64,100. Navigate to the stop loss and press the shortcut
  again." The measure tool speaks its distance, percent and bar count; the risk/reward tool
  speaks its ratio. Risk/reward is Alt+Shift+P; Alt+Shift+R is the rectangle.
- **Volume reads at the close**, with its direction, when you flag the volume series with N.
  Volume and market profiles narrate their point of control, value area and POC moves at each
  bar close; they say nothing during playback, by design.
- **Playback speed survives a restart.**
- Narration coherence: a switch that said "narrating" for a series with nothing to say now
  refuses and names a way out; the forming candle and the forming formation each have their
  own switch (formations off by default); Ctrl+Alt+Shift+N is gone, N is the switch.
- The "Export Visual" and "Export Audio" buttons are gone from Settings. They promised a
  whole tab as one file and saved only the current chart's component overrides. Share a
  soundscape as patch JSON from the Sound Designer and a theme as text from the theme
  editor; both work.
- The toolbar dropdowns follow the chart, including after a restore. A closed tab no longer
  keeps sending its alerts for three minutes. A market string that grew on every load
  ("Crypto|Crypto|Spot") repairs itself.

## 2.9.0 — the line goes where the line means something

*Everything before this release is in `CHANGES.md`.*

- **The 0 key marks the line that matters.** On RSI, Stochastic, Stoch RSI, MFI and the
  Ultimate Oscillator it marks 50, not zero; on Williams %R, −50; on MACD, still zero; on the
  price chart, the price under the cursor. Where the indicator already draws its midline you
  are told so. Where a pane has no centre, the key says so. Pressing 0 again takes it back.
- **Ctrl+Left and Ctrl+Right reach the midline.** Sixteen indicators spell that line four ways
  and the crossing keys knew one; all sixteen are reachable, alongside overbought and
  oversold.
- **Braille has its own Settings tab.** Shift+F4 opens it with focus on the tab. The browser
  build has no such tab, because a Dot Pad connects to the machine running the terminal.
- **Drawing tools moved to the bar under the chart**, with Add indicator and Scripts. The
  Order book button is always on the toolbar and its dialog says whether the venue has no
  depth feed or returned none.
- **Four dropped switches found by one guard** that checks every saved field at once: Market
  Structure's swing markers anchored to the wrong place, a Properties checkbox never read
  back, Ctrl+Z resetting four indicators' text settings, and a hand-picked colour losing its
  protection from the next theme change.
- Under the hood: the keyboard and alert layers were tested by deliberate sabotage for the
  first time; seven of twenty-seven regressions slipped past the suite and all seven are
  closed, including F1 going quiet inside every dialog.
