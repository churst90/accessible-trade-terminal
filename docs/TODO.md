# TODO — Accessible Trading Terminal

This file lists what is LEFT to do. What was done, with its caveats, is in `docs/CHANGES.md`. Everything
that used to be in this file (finished items, old audits, superseded START HERE blocks, old phase plans) was
moved on 2026-11-04 to `docs/archive/TODO_HISTORY.md`, unedited. Line numbers below like "archive line 9300"
point into that file.

## How to work from this file

The rule that has held since 2026-08-25: **demonstrate the defect, or mark it explicitly unverified.**
Reading code for bugs filed 283 findings in 2026-08 and about half were wrong; compiling the escape or
running the sabotage is what found real ones. Test one thing, write down what the test could not show.

The agreed order, unchanged:

1. The first section below is everything that is BUILT but not yet seen or heard by Cody. Most of the
   recent work is in it. Until Cody has looked and listened, treat these as unverified, and take his
   findings before starting new work on the same feature.
2. Then Cody's open questions (they are inside each item, under "Questions").
3. Then the older audit findings in the last section. None of them has been re-checked since it was
   filed (2026-08 and earlier), and many may have shipped since: check CHANGES and the code before taking one.

Standing facts: the full test suite runs on GitHub Actions at the push, not locally; build in a worktree,
never in the main checkout under a running server; DBSI gets no more ports until the residue is solved
(Cody, 2026-10-29).

## Built, waiting for Cody to see or hear

(Names: on 2026-11-04 the two cycle indicators swapped names. In the items below, written before that,
"Cipher C" is the copy of CF's indicator (code CIPHER_C), now shown as **Cycle Position**, and "Cycle
Position" is the more complete one (code CYCLE_POSITION), now shown as **Cipher C**.)

- [ ] **Close part of a position at a limit, and size by value (2026-11-04, built, NOT tried; see CHANGES).**
  Cody to try on PAPER: Close at limit, Tab to the percent, 50, Enter; and Size in → USD, 1000, a limit
  order. Open, mine: a percent for the market Close position button; remembering the Size in choice.
- [ ] **Volume profile sound, third try (584b8115): ACCEPTED by Cody 2026-11-04 ("profiles sound good now
  too").** Left, mine: profiles are still left out of playback, and the heatmap's sound has not been heard.
- [ ] **A bar close is spoken close → narration ladder → new bar's open, on every route; Shift+Enter places an
  armed quick trade as a limit (2026-11-02, built, NOT heard or tried; see CHANGES).** Cody to listen to a
  focused close, a background tab's close and a browser-closed close: the ladder should now come before
  "New bar: open". Shift+Enter: arm a quick trade on PAPER, Shift+Enter, and check that a limit order was
  placed. Open, mine: the focused chart's notification (window hidden) has no ladder; with two browser
  windows open at once on different tabs, the browser-closed monitor watches only the one that saved last
  (not tried; one window, the usual case, was checked against the 2026-11-01 overnight log: fine).
- [ ] **Cipher DBSI = the study's zu_hs66e, ported bit for bit (2026-11-02, Cody: "please do commit the latest
  version of dbsi"; NOT seen or heard; see CHANGES).** The ten veto sets are 99.99% exact, the blind sets
  99.97%, and the held-out sets 100%. Cody to look at Bitcoin daily and 1h Heikin-Ashi against DBSI. Still open
  in the study (no further port until asked, per Cody's 2026-10-29 rule): flat-price eras miss 6.4% of fresh
  labels (float noise); the pinned-Stoch-RSI hold vs re-arm (3 re-arms, 30 holds, unseparated); Intel 1990s
  (one shape); the Start-grid emulator not moved to this model. Unpinned (no harvested bar changes if broken):
  fsum against a plain sum and its half-even step, the Stoch RSI zero-range tolerance and its 77.2 edge,
  the wide gap 17.34 and the near-zero 5.6 moved up, the max that keeps NaN (differs from Math.Max only on ±0),
  and two NaN holds the zero-range hold makes unreachable.
- [ ] **Drawing tools pass (2026-11-01/02, built, NOT seen or heard; see CHANGES).** Cody to try: a fib
  retracement swing low → swing high (0% should be at the high, 61.8% below it); a fib extension, listening
  for the pullback sentence after the second point, then arrowing that many bars right and pressing Enter;
  Alt+D (21 tools, five groups); one pattern tool (ABCD or head and shoulders); Ctrl+Right on a focused fib
  ("Price crosses below 61.8% level at …"); an alert from "A line on the chart"; F12 → Speech → UTC and the
  12-hour clock. Open, mine: vertical lines and fib time zones are drawn as marks nobody has looked at on
  screen; the 12-hour axis labels are wider and not seen; the mouse half-bar fix also moved the hover readout
  and hairline at the chart's edges; an alert on a drawn level stays at its price if the drawing is moved;
  sloped lines are not offered as alert lines.
  Offer for Cody, not built: an option to speak a bar's CLOSE time instead of its open (TradingView labels
  bars by their open, as this does).
- [ ] **Browser closed: a bar close is said once, with its real open, when Bitstamp answers mid-update (2026-11-01, built, NOT heard; see CHANGES).**
  Bitstamp's list in the first seconds after a close can lack the bar that just closed, or carry a
  zero-volume placeholder at a stale price. That is where the doubled "1h: close 77401.41 at 07:00" and the
  wrong "New bar: open" prices came from. Left: Cody restarts his terminal on this build, closes the
  browser, and listens to a few 1-hour and 4-hour closes: each hour said once, in order, none skipped.
  Open, mine: the new bar's open is provisional on Bitstamp for its first seconds (the last close until
  the first trade), so it can still be a few dollars off; other venues were not polled.
- [ ] **Texture only in the extreme zones (2026-10-31, built, NOT heard; see CHANGES).** Cody to arrow Cipher B's
  waves on a daily chart: no texture inside ±53, a soft pink noise from ±53 to ±60, the same noise louder past
  ±60; and RSI, clean from 30 to 70. Is 0.25 / 0.45 the right step between the two zones?
  Questions: (1) and (2) ANSWERED and built (see CHANGES): COT, Crowding Index, Funding Rate and Fear & Greed
  have no texture, their extreme lines are not overbought/oversold lines, and Fear & Greed's line lost the
  tinge. (3) ANSWERED and built: no line keeps the brown tinge (MACD, Signal lines, Aroon, the price line),
  and the volume bed lost its brown tinge too, on the same rule (noise means a zone). Caveat: an indicator
  default saved on the VPS with a texture other than the old defaults is kept as the user's choice.
- [ ] **Cipher B's Wave Gap and Money Flow: RSI's voice, zero-only jumps (2026-10-31, built, NOT heard; see CHANGES).**
  Cody to arrow both on a daily chart and say whether they now sound like RSI on each side of zero, and press
  Ctrl+Left/Right on each: it should say "Zero line cross", never overbought or oversold. Question: they now
  click at each zero cross, like the blue waves. Keep the click, or remove it?
- [ ] **Cipher B has Market Cipher B 2.4.0's six settings, colours and lines (2026-10-31, built, NOT seen or heard; see CHANGES).**
  Settings: Use Current Chart Resolution, Different Timeframe, Over Bought Level 1, Trigger 1, Over Sold
  Level 1 (= the buy dot's threshold), Trigger 2 (= where the money-flow bar sits). Each line IS its setting.
  Gone: the four periods and Buy Dot Level (MCB has none). Questions for Cody:
  1. **White lines on light themes.** MCB's lines are white at 40-50%; on Paper / HighContrastLight they are
     faint. Keep MCB's white everywhere, or use the old grey #8C8C96 on light themes only?
  2. **SteelGray**: MCB's translucent fast wave is 2.995:1 against SteelGray (just under 3:1; the test floor
     is 2.99 there). Accept, or make the waves opaque on that one theme?
  3. The **periods** went (MCB offers none). Anyone who had changed one sees MCB's period now. OK?
  4. MCB's **"Version"** input is a label that changes nothing; left out. OK?
  Open, mine: the waves' spoken "overbought"/"oversold" still uses ±53 whatever Trigger 1/2 say; the pane's
  floor is fixed at −103, so Trigger 2 below −53 clips the bar; the buy dot is drawn at the fixed −96; a
  Different Timeframe shorter than the chart's falls back to the chart (MCB would compute from the lower
  timeframe); 4-hour and daily buckets are UTC-aligned, not session-aligned, on stocks; Different Timeframe
  on Heikin-Ashi not checked against TradingView.
- [ ] **Cipher B is only what Market Cipher B draws (2026-10-31, built, NOT seen or heard; see CHANGES).**
  Removed (Cody: "remove all of that old code from cipher b"): the red Overbought Crossover, the regular
  and hidden divergences, Anchor Wave, Anchor Wave 2, Anchor Polarity, Anchor Fill, WT Fill, and the
  Legacy money flow, Gated buy dot and Percentile threshold settings with the parameters only they read.
  Kept: the waves, Wave Gap, Money Flow, both cross dots, the Buy dot, RSI and Stochastic lines, and
  "WT Slow Wave Smoothing" (my call: it is MCB's own smoothing, 3 by default). The picture is unchanged:
  58 rendered frames (4 charts at several zooms) byte-identical before and after. Left:
  - Cody to open his saved Cipher B and check that it looks and sounds as before, with nothing extra
    in Properties → Parts and no Advanced tab.
  - Twelve catalogue strategies needed a removed part in a required condition and were retired with
    their verdicts kept (v14, v15, v18 and the v23 family). Agree, or rebuild any on Market Cipher B's
    parts?
  - The three setup graders now refuse to run (their pre-registered rules read the divergences or the
    red dot); build 2e86d377 to re-run one. OK?
  - `TradeRanker` lost its anchor-wave input (8 points); it has no callers in the app, but its very-high
    band now needs Hurst at or below about 0.27. Delete it, or keep it?
- [ ] **Cipher B's Wave Gap and Money Flow are read as waves by default (2026-10-30, built, NOT seen or heard; see CHANGES).**
  Both are now pitched on the pane's ±100 scale with no leap at zero. Properties has a "Read as" setting for each
  (Wave / Histogram) that changes only the type word, navigation, sound and speech; the picture is the same, and
  hiding Money Flow hides its red/green bar too. The WT Histogram is named "Wave Gap". Money Flow says its signed
  number. Cody to arrow along both on a daily Bitcoin and say whether:
  - the wave sounds right;
  - "Wave Gap" is a good name, and "Read as" a clear name for the setting;
  - the histogram form's grit is now loud enough.
  Open: sound settings saved as defaults in one form also apply in the other. Should each form keep its own?
- [ ] **Hyperliquid: research done, nothing built (2026-10-25; see `docs/HYPERLIQUID_RESEARCH.md`).**
  Questions for Cody before anything is built:
  - Are you a US resident or citizen? If so, live trading (phase 4), and in practice testnet
    (phase 2), break Hyperliquid's terms until the Payward/Bitnomial route opens.
  - Is this for your own trading, or a feature for the terminal's users?
  - Is a read-only data provider (phase 1: funding, open interest, HIP-3 stock perps) worth building on its own?
  - Hand-written signing, Nethereum, or JKorf's HyperLiquid.Net?
  - Should we watch Payward/Bitnomial and Kraken Pro instead?
- [ ] **The Market Cipher tests on the exact Ciphers: DONE, still No edge (2026-10-25; see CHANGES
  and `docs/EXACT_CIPHERS_RERUN_FINDINGS.md`).** Left:
  - The only lead from the whole Market Cipher series is the crypto 4h trigger-wave entry, and it
    is weak. It came out at +0.268R on the old Ciphers, +0.094R on the exact ones on candles, and
    +0.338R on Heikin-Ashi, but only +0.037R in the held-out era. Test it again only under its
    own pre-registration, on 4h bars after 2026-10-18; it needs months of new bars. Cody to
    decide whether that is worth waiting for.
  - Round 2's exits can never pass the three-round correction: 2,000 random books cannot give a p
    below 0.0005, and the first step needs 0.00042. A future round with exits needs more than 2,380
    books just to make passing possible (1 ÷ 2,381 = 0.00042); 10,000 leaves room.
- [ ] **Properties pass (Cody, 2026-10-23).** Three decisions, done in this order:
  1. Reset to Defaults resets everything, parameters included (built, NOT seen; see CHANGES).
     Cody to change an RSI's period and colour, press Reset to Defaults, and hear the name and
     period come back. It keeps the pane, your own levels, a profile's anchor, and mute / hide / narrate.
  2. An Advanced tab for settings the original indicator has no input for (built, NOT seen; see
     CHANGES). Cipher B's thirteen settings of ours and two of DBSI's are on it. Questions:
     - Should the Add Indicator dialog split them the same way? Today it lists every parameter.
     - Cipher B's Wave Trend periods are not Market Cipher B inputs either. Move them too?
     - Cipher C has now been compared with the original's inputs one by one (CIPHER_C_DESIGN section 21).
       Faster confirmation is on Advanced. Should the original's trigger and slow DSS adjustments be
       settings too? Today each preset's DSS is fixed.
  3. Every indicator's default-off parts left out (Parts → Include), not added hidden and muted
     (built, NOT seen; see CHANGES). Cody to open an indicator with default-off parts (Ichimoku's
     Kumo Polarity) and check that Ctrl+Down skips them and Properties → Parts lists them. (Cipher
     B's divergences and anchor waves, the first example, were removed on 2026-10-31.)
     Questions:
     - Should Ctrl+Alt+Shift+K (show everything hidden) also include left-out parts?
     - Should the Alerts dialog list left-out parts, so an alert can be set without including one?
- [ ] **Cycle Position, a new indicator (2026-10-28, built, NOT seen; see CHANGES and `docs/CYCLE_POSITION_DESIGN.md`).**
  Cipher C's picture with one rule for every asset and windows learned from the chart's own cycles. Its
  window held 61% of cycles, blind too, but is twice as wide as Cipher C's and no sharper per day.
 
  Cody to:
  - compare it with Cipher C on a daily Bitcoin and an untuned chart (Google, AUDUSD), and hear the bar
    detail and the table;
  - hear the weekly rule by time (Cody chose B): the nest "daily cycle 3 of it, a new weekly low drawn
    but not yet confirmed" on a daily chart;
  - hear "Low is in" and "Low expected" on a daily Bitcoin (arrow along a held low; the table's two new
    rows), then the weekly and yearly columns, and one on a 4-hour Bitcoin;
  - hear the "Next low", "Top so far" and "Half-cycle low" rows, the daily window, a failure mark and a
    half-cycle mark; on a daily Ether, the weekly Low is in cell; a mark at "very high" (daily green,
    weekly blue); and try an alert "Very high call is above 0";
  - try "Asset's own confirmation rules" on a daily Gold or Bitcoin.
  Questions for Cody:
  - Should the "low had come" share be recalibrated above 65% (it says 90% when 2 in 3 have come)?
  - Keep "Low expected" (right about half the time) or drop it?
  - Weak charts (Marathon weekly, Gold weekly, Silver and US oil yearly): say "less reliable on this
    chart", or leave it?
  - Weekly lows confirmed fast are missed (72% caught): should "high" be the word to act on for the weekly?
  - The strategy catalog reads check-box marks (Cycle Position's and Cipher C's confirmations) as lines,
    not events. Make them events?
  - The per-asset rules bring the lows much closer to the original's (daily F1 .907 -> .968) but picking
    the real low is mixed (blind daily 88.7 -> 88.3%). Turn them on by default? That changes the lows the
    "Low is in" models were fitted on, so they would be refitted.
  Caveats: translation letters follow the original's bar-midpoint rule only 82% on untuned blind charts
  (not decoded further); intraday dates past the last bar do not read holidays or clock changes; Cipher B's
  big green dot sits near 21% of daily lows but does not say a low will hold (no change made). Merged and
  pushed 2026-10-28; deploying to the VPS waits for Cody's go-ahead.
- [ ] **Cipher C, drawn and counted as the original does (2026-10-21 to 2026-10-28, built, NOT seen or heard;
  see CHANGES and `docs/CIPHER_C_DESIGN.md` sections 11 to 37).** Cipher C was rebuilt as a lookalike of the
  Cycle Finder original. Every step below is built and none has been seen or heard. The original's trial ends
  about 2026-11-05, so anything that needs a replay of the original must be read before then.
  Cody to look at a daily Bitcoin,
  a Dow daily chart and a Gold daily chart next to the original:
  - the drawing: lows with letter discs, dotted lines and 49D labels; checks, crosses, the clock, the grade
    and Start under the D; lettered tops with a box over each cycle; bands over the live windows; the yellow
    wave; the table in the price pane's corner; far fewer orange (mid) tops;
  - the settings: 27 display switches on General in the original's order (is the list too long? it could be
    its own "Display" tab); no mark under a confirmation's bar and no cross on a cancelled low by default
    (both still spoken); a picked count's words;
  - the sound: a pending low says its LOW / MID / HIGH tag ("...on day 76, mid confluence: ..."); a
    cancellation ("Daily pending cycle low cancelled: ..."); lows named, confirmed and dropped on the
    original's bars; Faster confirmation (Properties, Advanced) on a Bitcoin daily chart;
  - Bitcoin daily weekly marks moved (one added, two gone); no W under the 5 August 2024 crash;
  - Apple's and Ether's daily charts (deep double), Marathon's and SILJ's (first of two equal lows), GDX's
    and GDXJ's (miners' weekly double), a Gold daily chart (weekly state machine).
  Questions for Cody:
  - "Yearly lows found from price" (off by default; Dow, Nasdaq, Oil, Marathon gain lows): should it default
    on? An untuned symbol no longer draws yearly lows by default. Look at a Dow daily chart with it on.
  - The dollar's table says 15-24 days and its band is drawn at 16-25, as in the original. Make them the same
    anyway? (Likewise Ether's band and Gold's and Silver's weekly bands.)
  - Confirmed lows say their own confluence (1 to 3) though the original tags no confirmed low: keep, drop,
    or say the pending tag's last value?
  - Untuned assets: the original draws no weekly lows (Platinum, Apple, EUR/USD) but counts weeks. Draw them
    (as now) or hide the marks and keep the count?
  - Announce a later change of the pending low's tag?
  - Cipher C does not say when a low is being held ("unconfirmed" with a day count). Add it as a reading?
  - The zero line in Market Cipher B is white and dotted; ours is grey and solid. Change it?
  Known gaps, not explained: Coinbase 4-hour stalls (the original confirms nothing for its last 659 bars);
  Bitcoin weekly has one low the original lacks; the Dow's late undercuts are fitted per asset; GDXJ is the
  worst weekly chart; the dollar, SILJ, Silver, Coinbase 4h and NVDA, UUP, AMZN daily have late tops the
  original calls; 17 early keeps and 15 daily namings the original skips are unseparable; the yearly low on
  Bitcoin is confirmed about 254 weeks late (the engine's rule, not shown wrong); TLT has no preset; weekly
  charts' "weeks since last WCL" is not decoded; bands off a daily chart are unchecked; yearly band misses
  come from NYSE's unscheduled 2025 closure and TradingView's holidays stopping after 2027; Coinbase,
  MicroStrategy and Riot are a bar late. Replays still wanted while the trial lasts: Coinbase 4h and Bitcoin
  weekly (TradingView needs a visible window). Full detail: archive lines 204 to 540.
- [ ] **"Say what signals look like" (2026-10-22, built, NOT heard; see CHANGES).** Cody to tick it (Settings
  -> Speech) and arrow Cipher B on Bitcoin daily: the Buy dot should say "Drawn as a large green dot", a
  bearish WaveTrend cross "a red dot". Then arrow Cipher C's lows and confirmations, play back a stretch,
  and let a bar close with a signal on it. Questions: are the colour words right to the eye ("large" starts
  at thickness 4.5, "small" below 2.5)? Should alerts describe the marker too (tree alerts say only
  "conditions met")? Script/plugin indicators using `GradientDot` are not described (no built-in uses it).
- [ ] **Arrowing heard for the focused component only (2026-10-22 and 10-23, built, NOT heard; see CHANGES).**
  Cody to arrow Cipher B's money flow, WaveTrend and RSI on Bitcoin daily (also an RSI in its own pane):
  each component should ring only its own approach and "held beyond" cues, and other dots and crosses
  should not tick; Cipher A's Green Dot should no longer name the Yellow Diamond. "Tick other signals while
  arrowing" (Sonification tab) and "Speak other signals while arrowing" (Speech) bring them back. If
  something still sounds on a component, say which bar.
- [ ] **Market Cipher A, B and SR checked against the originals' values (2026-10-23 and 10-24, built, NOT
  seen or heard; see CHANGES).** Cody to look at Bitcoin daily: Cipher A's symbols, ribbon (the original's
  blues and two light fills) and, from Properties -> Appearance -> Parts, Ribbon 9 and the pivots should
  sit where Market Cipher A's do; Cipher B's Buy dots and RSI line should track Market Cipher B's, and its
  money flow lie exactly on it; on a 15-minute chart Cipher SR's purple and yellow dots should sit where
  Market Cipher SR's do (about half the old levels are gone; Cody chose to keep ours for the range). With
  Cipher A narrating, listen for "Price entered the ribbon, between ...", "left the ribbon above/below",
  "crossed above/below the ribbon" on bar closes, and Green Dot: "Trend turned up: ...". Tell me any
  sentence that reads wrong or too long. Still open: Cipher A pivots on sessions that cross midnight UTC and
  on weekly charts are unchecked; the last Include box refusing to untick is tested in bUnit, not a browser.
- [ ] **Cipher DBSI: the model, what was ported, what is unknown (2026-10-23 to 2026-10-29; the latest port is
  the first item of this file; see CHANGES and `dbsi-start-grid-residue` in the research notes).** Cody
  decided on 2026-10-29: keep searching for the rest of the original's rules, and port nothing more until the
  residue is solved. Open in the study:
  - the bear side still misses more than the bull side (alive bars 9.2% against 3.2%, dead bars 7.0% against
    3.5%); the missing input is not found; the bear hunt was stopped 2026-10-27, then the EMA(5)/EMA(11) cross
    was found 2026-10-29 and ported;
  - the bear zero-line bonus at the cross (78 missed, 26 false), bear dead bars (383 misses of a +2 in short
    stretches), a bull flat dead bar after a break (26 drops against about 1,200 holds), bear onset extras on
    threshold edges, nine S&P 500 bear runs 2 short on their dead bars, one Bitcoin bear run 2021-10-21..25
    two short;
  - false 17+ readings: the fifteen 3 (true 45 of 48), the twenty 5 (true 77 of 84);
  - Q's trigger fits about 9 times in 10; its stochastic length (10 to 12) and midline (45 to 55) are not
    sharp; the original's other-side counter and "bonus of about 3" are not in the model;
  - the rule "cross from WT above 36.5 with the 81-bar stochastic above 81.5" (+4) never fires on the 35
    digitised charts, so only the private sets pin it; the Start-grid emulator is not moved to the latest model;
  - Cody asked on 2026-10-24 whether the original's Start-grid numbers may go in the public repo so DBSI can
    be fitted on them (about 2,500 days; the shipped model's test would need them in its fixture). Not answered.
  - Cody to look at Bitcoin 4h and 1h Heikin-Ashi and any daily chart: big readings reach the 20s after a
    fresh turn from oversold and fall back as the wave flattens; the printed numbers should sit on Market
    Cipher DBSI's own more than nine times in ten.
  Every earlier DBSI revision is a revertible commit; the list is in CHANGES.
- [ ] **Heikin-Ashi: indicators compute on the Heikin-Ashi candles, strategies on the real bars (2026-10-22,
  built, NOT seen or heard; see CHANGES).** Cody to switch Alt+C on Bitcoin daily with Cipher B, an RSI and
  an EMA: every indicator should recompute at once and a strategy on the chart should keep trading as
  before. Hear the new-bar close on HA and off, a "price crosses EMA" alert, Ctrl+Right to a crossing.
  Open questions:
  - A custom compiled indicator cannot be recomputed in the strategy path, so its leaves evaluate false in a
    strategy on an HA chart. Recompute it there, or refuse the strategy with a reason?
  - A background tab's candle type is read when its monitor starts; switching it later waits for the next
    restart of that monitor.
  - Market Cipher B's S3 and S5 screenshots not re-measured on HA. The hosted server's alerts and the
    focused close line still read the raw close.
- [ ] **A strategy can read its signals on Heikin-Ashi (2026-10-22, built, NOT tried in the app; see CHANGES).**
  Cody to build a strategy with "Signals from Heikin-Ashi candles" ticked (Build Setup), hear the Summary say
  "Signals read on Heikin-Ashi candles; orders, stops and targets on the real prices", backtest it on a
  candle chart and an HA chart (same trades both ways), and check the entry and stop prices spoken are real.
  Reversible choices to confirm: a stop or target placed on an indicator reads it on the real bars; "next N
  candles close" reads HA candles; higher-timeframe leaves stay real; with no HA series the strategy refuses
  to trade. Not done: compiled (Roslyn) and plugin strategies ignore the option.
- [ ] **Restored tabs open with the history they had (2026-10-22, built, NOT tried in the app).** Cody to
  reopen a saved workspace after scrolling a tab back: the tab should open as deep as it was, at its saved
  zoom, and a new chart should still open with 200 bars. Workspaces saved before this open with 200 bars (or
  back to their oldest drawing) until saved once. A drawing anchored before the loaded history is still
  drawn wrong where the history cannot be reached (the provider has nothing that old, or it is past 5,000
  bars). Untested: an older page that throws mid-way, and the tab-switch path end to end.
- [ ] **Timing windows: sound, narration, type, fill and arc (2026-10-22, built, NOT heard or seen; see CHANGES).**
  Cody to listen and look on Bitcoin daily with Cipher C: Left/Right on a timing window is silent; in
  playback a low 110 Hz brown-noise texture while the window is open (level and colour are a first guess:
  say louder, quieter, brighter or darker); with N on Cipher C, "Daily timing window open, day 1." and
  "Daily cycle low confirmed, on {date} at {price}, N bars ago."; the green shading, the yellow arcs under the
  candles, "Window, with the cycle arc" on landing, Ctrl+Left/Right through the window; Properties ->
  Appearance -> "Draw the cycle arc" and "Fill under the line". Questions for Cody: Alt+Shift+D says the
  window count and then the words say it again ("Daily Timing Window 79 ... daily cycle day 79, 13 days
  past its window"): keep the raw value or drop it? What should a Window sound like (now a steady 330 Hz
  sine)? Known gaps: script indicators never mark components as hindsight; the forming bar is in every
  indicator's computation so a hindsight mark can show mid-bar and vanish at the close; wording says "bars
  ago" on every timeframe; the arc is look-ahead and moves as lows are confirmed; Cipher C's weekly window on
  a daily chart has no tint or arc (blank on the Dot Pad) though Appearance still offers "Draw the cycle
  arc"; the SMA has no Ctrl+Left/Right stop for price crossing it; windows other than Cipher C's are tested
  only with made-up data. Question for Cody: did "keep the gold dot as a drawing element" mean a marker you
  can PLACE yourself (a gold dot among the drawing tools)? Not built.
- [ ] **Heatmap and Profile Ctrl+Left/Right: DEFERRED by Cody 2026-10-22** ("it works fine and it's not hurting
  anything"). The bins live outside the component, so Ctrl+Left/Right says "No points of interest" and
  Alt+Shift+D says nothing. To build: Profile stops where price crosses the point of control and enters or
  leaves the value area; heatmap stops where the biggest liquidity level moved.
- [ ] **Cody's 2026-10-22 design points, still open:** nothing out of reach (every drawn thing must at least be
  named in the series summary or Alt+Shift+D, even with nothing to step through). The yearly cycle low
  sketch (a yearly low is a weekly cycle low confirmed when the next weekly low is confirmed higher, about 7
  to 9 months on Bitcoin) was worked by hand on one asset only; Cipher C's "yearly lows found from price"
  switch (above) is the built version.

## From the 2026-09-25 to 2026-10-01 test and mutation passes (status unclear, check)

The 81st pass (archive 1679) ended with these. Later passes (A2q merged 2026-10-01 with 44 of 44
survivors closed) may have settled some. Decisions for Cody:

- [ ] Where should Page Up/Down land on the candles? The engine says component 0 (upper wick), the reducer's
  default is the body, and the tests pin component 0.
- [ ] A signal that printed while F2 was on is heard after unmuting. Is that wanted?
- [ ] A same-chart reload that returns more history is treated as history loaded in front. OK?
- [ ] The survey's section 4 item 8 design questions (`scratchpad/a2q_SURVEY.md`) are still open.
- [ ] Should any Interface/Event/OrderEvent classification in the 2026-10-01 F2 entry in CHANGES move?
- [ ] The Tab-trap flake now has a message ("AIAnalystModal reports 2 focusable controls but Tab only ever
  reached 1"). Root-cause it.
- [ ] Unverified: Dot Pad shows values below 0.001 as "0" (read only); `dotpad.log` growth; the Dot Pad fixes on
  hardware; all new wording by ear.
- [ ] Not yet mutated: StrategyLab (25,804 lines); `ModalBrowserContractTests` Tab/Shift+Tab (not reproduced in
  eight loaded runs); `SettingsModalTests.SettingsModal_OpenedOnATab...` (one co-failure, no message kept);
  `ValueDeviationTests.BarsBeforeTheWindowFills_HaveNoReading` looks vacuous (flat fixture); a bUnit upgrade
  may break `SettledEventDispatch`'s reflection route (its guard names it); the browser probes rewrite
  `scratchpad/` files on every run.

## Still open from earlier audits

Every line below was an unchecked `[ ]` item in the old TODO, quoted by its first line, in the order it
appears there, under the section it was filed in. The group names are the old section titles. Items from the
2026-10-18 to 2026-10-21 passes are recent; everything from "The work A2b creates" down was filed in
2026-08 or earlier and is **(status unclear, check)**: many have been fixed since, and the archive holds the
full finding with file and line references. Open the archive line before starting one.

Not lifted, because they are superseded or answered: the questions for Cody in the 2026-10-17 blocks about
Cipher B, Cipher A, Cipher SR, the old DBSI-style indicator, Spider Lines and formations (all of those
indicators were rebuilt as Market Cipher copies on 2026-10-23 and 2026-10-31, Spider Lines were removed, and
the 2026-10-17 answers are recorded as DONE in CHANGES); every NEXT list in the START HERE blocks from
2026-09-03 to 2026-09-24 (each repeated the one before it; the work was done, or is carried in the items
above or below); and all `[x]` items, finished audit write-ups and phase plans.

### 2026-10-21 — the hand-drawn trend line study, and text label placement fixed; ...
- [ ] Cody to look at text labels in the app: one on the newest bar, two on one bar, a long one. (archive 1048)
- [ ] A label's text in a script no installed font covers (Japanese on this machine) still shows (archive 1050)
- [ ] Not checked: whether a label's colour set in Properties changes anything visible (the plate (archive 1052)
- [ ] The trend line rule. One dated case separates the candidate rules: the Dow's 23 Jul 2026 (archive 1054)
- [ ] The trend line is NOT part of CF as far as can be told: CF is hidden on every frame with a (archive 1069)
- [ ] Cody to open `strategy-lab-data/trendlines/Camel cycles trend lines.json`: a copy of his (archive 1072)

### 2026-10-21 — Cipher C rebuilt as cycle lows after CF's Cycle Trading Indicator; ...
- [ ] Cody to hear it with Orca: a daily Bitcoin and an SPX chart, a weekly chart (no "daily cycle (archive 1085)
- [ ] Merge (both branches), push (the full suite runs there), restart the server: Cody's go-ahead. (archive 1087)
- [ ] CF's future cycle low range (FCLR) boxes to the right of the last bar: the chart has no future (archive 1088)
- [ ] Confluence that upgrades LOW → MID → HIGH over the bars after a low, as CF's demo shows. Ours is (archive 1090)
- [ ] A yearly cycle low has never been seen on CF's screens; ours is unchecked against it. Same for (archive 1093)
- [ ] Still estimated: NDQ, DJI, DXY, SILVER, USOIL, GDXJ, SILJ. Twelve more videos searched (archive 1106)
- [ ] "Days since Last DCL : N" is bars from the low to the last bar (Bitcoin: 46 on 7 Aug 2025 = (archive 1109)
- [ ] More of CF's look, not built: (archive 1116)
- [ ] The cancelled low's green box around the original stack (ours is a red cross on the cancelling bar). (archive 1121)
- [ ] CF puts the check under the low's own stack once confirmed; ours sits above the confirming bar. (archive 1122)
- [ ] CF's LOW / MID / HIGH text on the pending label; ours is spoken only (the tag itself is the original's since 2026-10-27). (archive 1123)
- [ ] Re-test the retired Pulse Reversal Long on `CIPHER_C.DCL Confirmed` before reviving it. (archive 1133)
- [ ] Drawing the window tint as CF's band is: only the past part is drawn. (archive 1134)

### 2026-10-21 — the Camel cycles workspace broke on load: five defects FIXED on branch ...
- [ ] Not pushed, not deployed, and Cody's running server is still on the old build. Merge, push (archive 1141)
- [ ] The GLD 1d tab's "400 (Bad Request)": the reason is unknown because the body was never read. (archive 1143)
- [ ] One Bitstamp "400 BadRequest" for BTCUSDT from the MarketFeedHub live subscription. Every (archive 1145)
- [ ] The session file already saved the DIA tab as Crypto Alpaca AAVE/USD, and dropped the Loukas (archive 1147)
- [ ] The Alpaca live stream: the log showed 409 "insufficient subscription" (the plan is IEX only) (archive 1150)
- [ ] The DIA 1d tab "freezes" and Orca crashed on it (Cody, 2026-10-21). Not explained: DIA's (archive 1157)
- [ ] `LocalBackgroundMonitor.FetchAsync` (WebHost alert poll) asks the provider for the watch's (archive 1182)
- [ ] `FileCacheService` puts its cache under `Environment.SpecialFolder.InternetCache`, which is (archive 1185)
- [ ] Untested live on stocks: no stock request with the new `start`/`sort=desc` has been sent with (archive 1190)
- [ ] Re-score the dated calls in `Corpus/rules/*.md` against prices (Camel Finance's 358 first, (archive 1209)
- [ ] Fold the 35 later educator transcripts (Trader Geo's 93-minute tutorial among them) and the (archive 1214)

### 2026-10-18 — setup grader round 3, Market Cipher with Cipher SR location: DONE, No ...
- [ ] Questions for Cody (nothing applied): (archive 1222)
- [ ] Not pushed: the branch is committed locally. The full suite runs on GitHub Actions at the push. (archive 1233)

### 2026-10-18 — the cycle school: stage 2 DONE, results in ...
- [ ] Decide (Cody): fix the old `cftc-cot` downloader to choose contracts by code and stamp (archive 1243)
- [ ] Unverified: the CFTC release dates before 2026 are a holiday rule plus the CFTC's (archive 1247)
- [ ] If the design is ever revised (a new pre-registration): drop the "low due" rule (B5 found it (archive 1250)

### 2026-10-18 — the cycle school: stage 1 DONE (method, design, pre-registration); see ...
- [ ] Stage 2: build and run the 56 pre-registered tests exactly as written, results into a new (archive 1263)
- [ ] Before using COT in the score: confirm which CFTC report each `xs_cftc_*` file came from. The (archive 1265)
- [ ] Questions for Cody (nothing applied): the "90+ metrics" claim for the CF indicator is in (archive 1268)

### 2026-10-18 — macro, Fed, liquidity and sentiment data: FETCHED, point in time; see ...
- [ ] Decide on Cboe put/call after 2019-10-04. It exists only as one HTML page per day, about (archive 1289)
- [ ] Decide on NFCI as-known-then values. ALFRED returned HTTP 504 on the initial-release file. (archive 1291)
- [ ] Optional: a loader that registers the macro CSVs in the cross-series cache, so indicators can (archive 1293)

### 2026-10-18 — Cody's BTCUSDT 1D screenshot: axis tags and empty status-line plots DONE; ...
- [ ] General tag drift rule (recommendation, Cody's call). `StackScaleLabels` has no limit on (archive 1304)
- [ ] Ctrl+Up/Down still stops on a plot with no value on this chart (Cipher SR's Range/VWAP on a (archive 1312)

### 2026-10-18 — setup grader round 2, Market Cipher school: DONE, No edge; see CHANGES ...
- [ ] Question for Cody (nothing applied): in equities, the first bearish divergence exit beat (archive 1323)
- [ ] Not pushed: the branch is committed locally. The full suite runs on GitHub Actions at the push. (archive 1326)

### 2026-10-18 — setup grader for the Cipher B Buy dot: DONE, No edge; see CHANGES and ...
- [ ] The equities side results (non-vetoed dots +0.115R over a random bar in the same context; (archive 1331)
- [ ] If the grading idea is to be tried again, it needs a new dated pre-registration (the old one (archive 1335)

### 2026-10-18 — three lab defects verified and fixed: R after a stop move, HTF ...
- [ ] Re-record every engine R figure on a two-rung ladder with breakeven or trail. R is now one (archive 1341)
- [ ] Re-run the COT battery cells (`battery` v5a-v5h, BTC CFTC_COT) and `cipher-a-battery`'s COT gate (archive 1352)
- [ ] Optional: re-download the COT snapshots with the fixed `cftc-cot` so the files themselves carry (archive 1354)
- [ ] `docs/SETUP_GRADER_PREREG.md` section 2.2 still lists the HTF and R caveats; it is a frozen (archive 1356)

### 2026-10-17 night — Cipher strategy re-run on the new Cipher A/B/SR: DONE; see CHANGES ...
- [ ] Delete `CIPHER_A.Buy Signal` and `CIPHER_SR.Support` from bare-bull-pulse, faber-pulse and (archive 1365)
- [ ] The 1h Cipher SR proximity result (old Cipher B +0.308R p 0.006, new +0.164R p 0.069, KAS and (archive 1371)
- [ ] CatalogueProvenance's rolling-window figures were all computed before the 2026-08-24 (archive 1373)
- [ ] The battery's cells that need retired leaves inside an AND (v23+A, v23+SR, v23+ASR, v23+ALL, (archive 1376)
- [ ] ML confidence model: retrain on the new Cipher B export when lightgbm/scikit-learn are (archive 1379)

### The work A2b creates (status unclear, check)
- [ ] Re-check what else parked behind `ShowModalAsync`. Until 2026-08-28 every line a concrete (archive 10126)

### The work A3 creates (status unclear, check)
- [ ] (F2) Give `textarea#sd-import-json` a real label. Same acceptance test. LOW. (archive 10710)
- [ ] (F3) Make Shift+F12 with no focused series say so instead of returning silently. LOW. (archive 10711)
- [ ] (F4) Read the timeout in (archive 10712)
- [ ] Teach the harness to seed a chart and credentials, which unlocks the seven unswept dialogs (archive 10715)
- [ ] Sweep controls behind `<details>` expanders and populated lists, not just the initial render (archive 10718)

### Trading dashboard decoupling (roadmap adopted 2026-08-23) (status unclear, check)
- [ ] Spec Appendix B follow-ups (separate from the decoupling): duplicate protective legs (archive 11306)

### Hosted deployment — notes back from the server (2026-08-24) (status unclear, check)
- [ ] `AudioEngine.RingBuffer<T>.Enqueue` is single-producer and now has FOUR producers (note §5b). (archive 11409)
- [ ] Process, not code: tell the server before the SDK band moves (note §4). `global.json` pins (archive 11446)
- [ ] Run the integration suite a second time under a non-empty `PathBase` (note §3). Re-checked (archive 11544)

### Money path, batch two — follow-ups filed 2026-08-24 (status unclear, check)
- [ ] A withdrawal still shares its provider instance with the order path for the length of one (archive 11844)
- [ ] No guard stops the next `WaitForAssertion` over non-render state. bUnit re-evaluates (archive 11851)
- [ ] The shared paper account still holds its creating tab's scoped `IDataService`. (archive 11862)
- [ ] API-key secrets are stored under the nickname alone, so one nickname across two providers (archive 11868)
- [ ] A failed secure-storage read makes every credential silently absent (archive 11875)
- [ ] Spoken money is always prefixed "$" regardless of quote currency (archive 11881)
- [ ] Follow-up from the money-path batch: the quick-trade sizing seam cannot see the instrument. (archive 11888)
- [ ] Portfolio pricing is hardcoded to the Crypto market (`MarketDataPriceSource.cs:106`). (archive 11900)

### Health audit — strategy engine and backtest↔live parity (grade D+) (status unclear, check)
- [ ] Two deliberate live-vs-replay divergences left open by the 2026-08-25 parity work, both in (archive 12059)
- [ ] An account selector for Schwab. Carved out of the multi-account finding closed (archive 12180)
- [ ] `LevelService.cs:30-31` swallows every level-provider exception with `catch { continue; }` — no (archive 12271)
- [ ] `StrategyEngine.AddStrategy:255`, `PauseStrategy:280-282` and `SetExecutionMode:287-289` (archive 12279)
- [ ] `StrategyEngine._pendingSignals` is written and never read — Suggestion mode has no confirmation (archive 12286)
- [ ] The announced entry price and R:R are never the ones the backtest fills at. (archive 12293)
- [ ] (amends TODO:2084/2092) Add two more copies to the hand-rolled-ATR census: (archive 12348)

### Health audit — alerts and notification delivery (grade D) (status unclear, check)
- [ ] `AlertOrchestrator` is scoped per circuit in WebHost, so two browser tabs silently delete each (archive 12567)
- [ ] The VAPID private key is written in plaintext with default permissions and the VAPID subject is (archive 12581)
- [ ] The hosted server logs every user's alert text at Information — `HostedAlertMonitor.cs:185`. (archive 12588)
- [ ] Nothing checks bar freshness, so a stale feed reads as a live market — (archive 12594)
- [ ] Two load-bearing comments describe behaviour the code does not have. (archive 12600)
- [ ] (amends TODO:1281) That entry says "Adding or deleting an alert produces no feedback and (archive 12608)

### Health audit — market-data flow (grade C+) (status unclear, check)
- [ ] Opt the providers into the cancellable fetch, one at a time. Carved out of the (archive 12741)
- [ ] `OrderBookHistoryService` hardcodes a 1-minute bar, so heatmap history is empty on every other (archive 12748)
- [ ] `DataOrchestrationService` issues a live order-book network request on every single data update, (archive 12757)
- [ ] The provider-timeframe verification only catches data that is too FINE, never too COARSE (archive 12767)
- [ ] The tab-switch handler assigns the composite market string to the SUB-TYPE field (archive 12777)
- [ ] `MarketFeedHub.TryStartFeedLiveAsync` reports `Started` when it lost the race and threw its (archive 12806)
- [ ] `ChartFeed`'s cap-trimming loops reallocate the entire backing array once per bar (archive 12815)
- [ ] Two `CancellationTokenSource`s are replaced without being disposed, once per tick and once per (archive 12823)
- [ ] `DataService`'s constructor takes an `IPluginLoaderService` it drops on the floor and stores an (archive 12829)

### Health audit — SDK and the 16 market-data / trading provider plugins (grade B-) (status unclear, check)
- [ ] The SDK is not dependency-free, contradicting `docs/CODEBASE_KNOWLEDGE_BASE.md:30`. (archive 13135)

### Health audit — analytics / alternative-data plugins (grade C) (status unclear, check)
- [ ] (amends TODO:5825-5834) `ProviderFetchOhlcvTests.cs` gives the analytics plugins one (archive 13776)

### Health audit — indicators (grade C+) (status unclear, check)
- [ ] `AnchoredVwapProvider.cs:191-198` — `AVWAP Bias Soft` can never take the value 0 its comment (archive 13974)
- [ ] `CipherBProvider.cs:1038` reads a `DivergenceConfirmLag` parameter that `:385-431` never (archive 13982)
- [ ] A parameter can silently flip a component from causal to look-ahead, and the declaration cannot (archive 13989)
- [ ] `CipherSRProvider.cs:199` — the `AutoScale` parameter description shown to the user documents the (archive 14000)
- [ ] `PulseProvider.cs:967-995` — `GetDetailFact` speaks the literal string "NaN" during warmup. (archive 14006)
- [ ] `RegimeProvider.cs:103` emits `0.0` for `RegimeState` during the 199-bar warmup instead of NaN, (archive 14013)
- [ ] `CipherSProvider.cs:193-213` sorts a freshly allocated copy of the whole rolling window on every (archive 14020)
- [ ] `IndicatorService.cs:151-201` and `IndicatorEngine.cs:46-51` call `provider.GetIndicators()` on (archive 14027)
- [ ] `AccessibleTrader.Sdk/Indicators/IndicatorMath.cs` has no unit test. It is the shared library — (archive 14058)
- [ ] The causality guard has two remaining structural blind spots its own doc does not name: (archive 14071)
- [ ] Comments that are actively wrong and load-bearing. `RegimeProvider.cs:106-107` — "seeded with (archive 14080)
- [ ] `IndicatorStateMapper.cs:28-30` maps provider output to components by substring in both (archive 14094)
- [ ] `PaneAssignmentService.cs:15,44` use culture-sensitive `ToUpper()`/`ToLower()` on indicator (archive 14103)
- [ ] `VolRegimeProvider.cs:215` hardcodes 1.25 / 0.75 in `GetDetailFact` while `VolState` (`:148`) (archive 14110)
- [ ] (amends TODO:2084) `CipherCProvider.cs:819-831` re-implements Hull MA next to (archive 14114)

### Health audit — accessibility layer (grade B-) (status unclear, check)
- [ ] A chart load failure still cannot say WHY. Filed out of the 2026-08-27 MEDIUM batch, which (archive 14447)
- [ ] Sub-pane navigation names no pane on the first feedback event of a session. Filed out of (archive 14454)
- [ ] Five price/size formatters coexist in the speech layer. `SpeechPriceFormatter.FormatPrice` (archive 14462)
- [ ] (amends TODO:960-968) Those citations have drifted ~17 lines since the 2026-08-23 edits: the (archive 14474)
- [ ] The speech-layer components that still have no tests. Carved out of the (archive 14507)
- [ ] The candle-summary tests assert only the *bullish* prefix (archive 14517)

### Health audit — workspace state, settings, persistence (grade C+) (status unclear, check)
- [ ] `ImportVisualProfile`/`ImportAudioProfile` mutate live state objects without dispatching — (archive 14694)
- [ ] `LoadWorkspaceModal.ResetWorkspaceState` can spin forever — `LoadWorkspaceModal.razor:158-171`. (archive 14703)
- [ ] `SettingsManager`'s `JObject` is mutated from multiple threads with no lock — (archive 14711)
- [ ] `WorkspaceConfiguration` has no schema version, and an indicator that no longer exists is restored (archive 14721)
- [ ] `CaptureConfig` mutates the live series during a save — `WorkspaceLibraryService.cs:205-209`. (archive 14733)
- [ ] A failed or refused tab switch is completely silent — `WorkspaceStore.cs:100`, `:163-170`. The (archive 14738)
- [ ] `AtomicFile` never fsyncs the parent directory and never cleans stale temps — (archive 14759)
- [ ] `AppDbContext` declares an index identical to its primary key, and its fallback path ignores the (archive 14767)
- [ ] `MarketFeeds.GetBarsAsync` drops its `CancellationToken` at the fetch — (archive 14774)
- [ ] Indicator parameters round-trip through the ambient culture and are re-parsed as invariant — (archive 14779)
- [ ] (amends TODO:4495-4497) "`WorkspaceStore` immutable snapshots. Replace direct property mutation (archive 14790)

### Health audit — audio engine and sonification (grade C+) (status unclear, check)
- [ ] A self-terminating voice is deactivated with no release fade, and the note envelope's (archive 14860)
- [ ] The engine's `DataIndex` → `PointReached` plumbing is dead, and per-frame work in the render loop (archive 14899)
- [ ] `WebHostAudioDriver` is `AddScoped` — one `AudioEngine`, one OS thread and one `pw-cat` child (archive 14910)
- [ ] The audio callback runs on the general .NET ThreadPool on Windows, under a lock — (archive 14946)
- [ ] `WebHostBrowserAudioSink`'s class doc is actively wrong about its own lifetime — (archive 14956)
- [ ] Playback tempo is `Task.Delay` with no clock anchoring, so it drifts and quantises — (archive 14962)
- [ ] Two `CancellationTokenSource` leaks in the playback path. `AudioSequencer.Stop:554-559` does (archive 14970)
- [ ] `SonifyProfile` and `SonifyHeatmap` are the only sonifiers that never pan, and `SonifyHeatmap` (archive 14975)
- [ ] `AudioDiagnosticTests.Coordinator_ShouldCoalesce_RapidStateChanges:84-119` asserts nothing and says (archive 14984)
- [ ] (amends TODO:1024) The no-output-limiting item understates the measurement: 20 sine voices at (archive 14990)

### Health audit — chart rendering, drawing tools, coordinate math, theming (grade C+) (status unclear, check)
- [ ] `ExtendLeft`/`ExtendRight` are accepted and ignored — (archive 15118)
- [ ] Audio series volume is used as the heatmap's visual alpha — `HeatmapRenderer.cs:35-36`. (archive 15137)
- [ ] The pane-divider drag uses a hardcoded 600 px container height that is never updated — (archive 15144)
- [ ] Zone-band shading and zone audio are two independent declarations with nothing keeping them in (archive 15151)
- [ ] The TBD "distribution ≥ 0.5" tint has no non-visual counterpart — `ChartRenderer.cs:391-424`. (archive 15162)
- [ ] `RenderZoneBand`'s new-level test uses a signed threshold — `StandardRenderers.cs:1134`. (archive 15170)
- [ ] Crosshair value labels are drawn on a hardcoded dark plate with theme-coloured ink — (archive 15177)
- [ ] The colour-vision-safe override reaches only candles and directional bars — (archive 15187)
- [ ] Candle bodies vanish at wide zoom — `StandardRenderers.cs:195`. (archive 15196)
- [ ] Malformed level colour hex kills the whole frame — `DataLayer.cs:192`. (archive 15202)
- [ ] A render failure is logged but never spoken — `ChartRenderer.cs:269-273`, (archive 15209)
- [ ] Dash `SKPathEffect`s are disposed at two of four creation sites — `StylingService.cs:69,76` and (archive 15217)
- [ ] Heikin-Ashi is recomputed over the entire history three times per keystroke — (archive 15225)
- [ ] Two crosshairs are drawn on the main pane every frame — `OverlayLayer.cs:24-40` and (archive 15232)
- [ ] A formation whose trigger is off-screen also loses its floor and its target — (archive 15268)
- [ ] Area fills stretch across the empty right margin — `StandardRenderers.cs:267-270`. (archive 15273)
- [ ] Stale constructor comment — `ThemeService.cs:58`. "fall back to HighContrastDark if not set" (archive 15289)
- [ ] (amends TODO:3665 / 5105 / 5521) TODO:3665 ("shared coordinate math consolidated into (archive 15303)

### Health audit — hosted WebHost, auth, multi-user security (grade B) (status unclear, check)
- [ ] `Register` has no email confirmation and anyone can register any address — (archive 15711)

### Health audit — StrategyLab research methodology (grade B-) (status unclear, check)
- [ ] `ExitCommand.cs:164-165` asserts trades do not overlap; for at least four of the eight exit rules (archive 15862)
- [ ] `ExitCommand`'s stop and target rules exit at the trigger bar's CLOSE, not at the stop or target (archive 15873)
- [ ] `TradingCrossCommand`'s fill-timing comment contradicts its own code by one bar (`:168-172` vs (archive 15881)
- [ ] `TradingCrossCommand.RunEras` leaks the next era into the first era's result (`:472-476`). (archive 15892)
- [ ] `TradingCrossCommand.RunDca:273` computes DCA's CAGR as `(finalValue / totalDeployed)^(1/years) - (archive 15901)
- [ ] Two commands derive a noise seed as `base + hash % 1000`, guaranteeing correlated noise across (archive 15909)
- [ ] `LabStats.PermutationP` silently computes a wrong statistic when `nA + nB > pool.Length`, and it (archive 15920)
- [ ] `XsMomentumRobustness.cs:66` and `:72` carry two undocumented magic constants inside the cost model (archive 15931)
- [ ] `ml/train.py:39-46` splits chronologically but applies no purge or embargo, and the split boundary (archive 15939)
- [ ] `OnChainCommand.cs:248-249` prints a dynamically-counted metric total next to two hardcoded (archive 15948)
- [ ] `GateCommand.cs:52` holds mutable static accumulator state (`_barsSeen`, `_crossBarsOpen`, (archive 15954)
- [ ] `ExitCommand.SignalExitAt:269-273` recomputes the full `ZScore` array on every bar of every trade (archive 15959)
- [ ] `WalkForwardCommand` blemishes (the strongest command in the file set, recorded so they are not (archive 15965)
- [ ] Cost model absent from most commands. No costs in `xsmom` (disclosed at (archive 15970)
- [ ] The forward universe recorder holds two days and there is a gap. `universe-archive/` contains (archive 15978)
- [ ] `RegimePersistenceCommand:187,312,338` hardcodes 252 bars/year for crypto too. Every use is a (archive 15982)
- [ ] The lab has no machine-readable output mode, which is its biggest accessibility gap. (archive 15985)
- [ ] (amends TODO:2028) The permutation-clamp count is wrong: it says "four commands" and there are (archive 15997)
- [ ] Have each command print its own test count and a Bonferroni threshold, the way (archive 16099)
- [ ] Commit content-addressed snapshots for the samples that back a recorded edge, and (archive 16106)
- [ ] The 59 lab files with zero test references, and the absent arithmetic guards. Carved (archive 16114)

### The scrollback smear (reported from use, 2026-08-25) (status unclear, check)
- [ ] The heatmap series has the same prepend bug and is not covered by that fix. (archive 16187)
- [ ] Known full-suite flake: `ModalDisposeLeakTests.Dialog_DisposeReleasesEveryEventBusSubscription` (archive 16374)

### Causality for scripted indicators — closed 2026-08-25 (status unclear, check)
- [ ] Nothing consults `ICustomIndicatorRegistry.IsPublishable` yet, because nothing publishes (archive 16438)
- [ ] The strategy probe hands over an empty `ActiveSeries`, so an indicator-reading strategy is (archive 16479)

### Open (status unclear, check)
- [ ] A script strategy cannot reach the host services a script strategy might want. Filed out (archive 16621)
- [ ] Pine dialect fidelity was not assessed. `Core/PineScript/PineTranspiler.cs` (649 lines) (archive 16721)

### Still open from the patch's own list (status unclear, check)
- [ ] The impossible tab. Nothing validates a symbol/provider pair on workspace restore — (archive 16868)

### Ship-blockers — data pipeline — FIRST TWO FIXED 2026-08-22 (status unclear, check)
- [ ] `OhlcvStore` can write bars that were never real, and being insert-only can never repair (archive 17050)
- [ ] `EventBus` has no exception isolation between subscribers. `Publish` is (archive 17061)
- [ ] `DataState.LiveStreaming` is unreachable; the pipeline reports `Initializing` while (archive 17066)
- [ ] `AnalyticsDataResolver` ignores its `asset` parameter, so requesting ETH returns BTC (archive 17084)
- [ ] Live ticks never recalculate indicators. `DataManager.OnFocusedFeedUpdated` deliberately (archive 17089)
- [ ] Zero-price bars are silently deleted from every fetch. (archive 17095)
- [ ] `"1y"` datasets are unchartable with a misleading error. `MyDataProvider` advertises (archive 17100)
- [ ] Two `MyDataStore` instances over one user directory destroy each other's imports. (archive 17105)

### Ship-blockers — indicator lookahead (the backtest-validity cluster) — ALL SIX FIXED 2026-08-21 (status unclear, check)
- [ ] `ScreenerService.ReadColumns` still reads a component value by descriptor without consulting (archive 17148)
- [ ] Surface `ConditionEvaluator.LastDegradation` in the UI. Some strategies (archive 17158)
- [ ] `IchimokuProvider.cs:264-266` — the Chikou Span array is a raw 26-bar future price, exposed (archive 17212)
- [ ] `CipherAProvider.cs:400-427` — Cipher A's divergences are still stamped at the pivot bar; (archive 17221)
- [ ] `CipherSRProvider.cs:245,299,321` — the historic "Cipher SR backtest lookahead" is still (archive 17230)
- [ ] `SwingStructureProvider.cs:196-201` contradicts its own class doc. The doc at `:37-40` (archive 17236)
- [ ] `SwingStructureAnalyzer.cs:141-159` — pass 2 retroactively deletes swings, so the same bar (archive 17242)
- [ ] The only causality test in the codebase is vacuous. VERIFIED — (archive 17250)

### Ship-blockers — indicator computation (status unclear, check)
- [ ] `IndicatorEngine.cs:127-133` — on a same-bar tick the buffer is handed to the provider (archive 17270)
- [ ] `IndicatorResultBuffer.cs:65,75` zero-fills where the codebase's own stated convention is (archive 17283)
- [ ] `SkenderCalculationCore.cs:193-201` — ten registered Skender indicators resolve to no method (archive 17288)
- [ ] NOT IN THE AUDIT — seven more Skender indicators resolve fine and then write to component (archive 17295)
- [ ] `SkenderCalculationCore.cs:172-178` — every Skender indicator's live bar is recomputed from (archive 17321)
- [ ] `SkenderCalculationCore.cs:173-178` reads uninitialised `ArrayPool` memory. The temp buffer (archive 17327)
- [ ] `SkenderCalculationCore.cs:139-157` — the synthetic `__CROSSOVER`/`__SQUEEZE` flags are (archive 17331)
- [ ] `CoinMetricsProvider.cs:204` — daily metrics are stamped at the START of the day they (archive 17335)
- [ ] `Plugins/Analytics/…Cftc/CftcProvider.cs:197` — COT "release date" is a synthesised (archive 17340)
- [ ] `OpenInterestProvider.cs:203` and `CrowdingIndexProvider.cs:258` — zero-valued OI rows are (archive 17344)

### Indicator correctness — HIGH (status unclear, check)
- [ ] `BarDetailService.cs:227-282` — both indicator narrative facts are dead code. (archive 17352)
- [ ] `IndicatorContextAnalyzer.cs:85-91` — the Money Flow Wave thresholds are calibrated to a (archive 17358)
- [ ] `IndicatorContextAnalyzer.cs:197-213` — `DetermineZone` never compares price to a band. (archive 17362)
- [ ] `CipherSProvider.cs:96-109` speaks unhedged trading instructions — *"strong DCA (archive 17366)
- [ ] `CipherSRProvider.cs:448-455` — pivot speech scans FORWARD to report a touch count from the (archive 17375)
- [ ] `AnchoredVwapProvider.cs:142-163` anchors to the confirmation bar, not the pivot, silently (archive 17382)
- [ ] Skender component names that resolve to nothing or to duplicates. `Vip`/`Vim` on Vortex (archive 17385)
- [ ] `IndicatorMath.cs:141-151` — `ComputeStochRsi` fabricates 50.0 across the entire RSI (archive 17397)
- [ ] `FundingRateProvider.cs:60` (+ OpenInterest, Crowding — verbatim triplicate) mangles (archive 17401)
- [ ] `CrossSeriesCache.cs:97,153` — no TTL on success, no negative caching on failure. A (archive 17406)
- [ ] `CoinMetricsProvider.cs:279` attributes ETH's on-chain metrics to a BTC chart whenever the (archive 17411)
- [ ] `CotPositioningProvider.cs:281` starts emitting Crowded markers off a 5-sample population (archive 17414)
- [ ] Chart-pattern geometry defects. `ChartPatternDetector.cs:388-391` — the H&S neckline (archive 17422)
- [ ] `ChartPatternNarrator.cs:189-190` — the exact wording defect the class doc at `:199-207` (archive 17430)

### Ship-blockers — audio and speech (status unclear, check)
- [ ] Component metadata has no way to say "this value is a price." The fix above changed the (archive 17465)
- [ ] Saved workspaces persist `SpeechTemplate`, so existing users keep the old `{value:F2}` (archive 17472)
- [ ] Zone-proximity speech overwrites the bar reading on the web head. VERIFIED as a second (archive 17519)
- [ ] Component-context speech reads RAW OHLC under Heikin-Ashi. `NavigationFeedbackManager` (archive 17544)
- [ ] Muting is not absolute for Cloud components. `NavigationSonifier:352` and (archive 17552)
- [ ] `LevelCrossingMonitor` ignores series mute and every volume tier. `:113` filters on (archive 17560)
- [ ] The earcon round-robin overwrites the "dedicated" cross-chirp and level-cue slots. (archive 17577)
- [ ] No output limiting anywhere. `AudioEngine:621-624` writes `leftSum * _masterGain` (archive 17583)
- [ ] The volume bed and the candle body play the same two pitches. (archive 17590)
- [ ] `AutoNarrationService:179` re-introduces the F2 bypass the router redesign removed. A (archive 17599)
- [ ] Hitting the first or last series is a silent no-op. `NavigationEngine:178-190` clamps and (archive 17613)
- [ ] `SoundPatchLibrary` mutates a plain `List<SoundPatch>` that the navigation path enumerates. (archive 17617)

### Ship-blockers — UI and accessibility (status unclear, check)
- [ ] Every feedback message is spoken twice, and F2 silences only one of the speakers. (archive 17769)
- [ ] The assertive double buffer fails on even-numbered batches. `MainLayout.razor:186-194` (archive 17775)
- [ ] `role="menu"` popups are counted as open modals but are not covered by the Tab trap. (archive 17782)
- [ ] Keyboard rebinding cannot capture any modifier chord. `captureNextKey` registers on (archive 17788)
- [ ] The inline stop-loss / take-profit editor opens with `autofocus`, which browsers ignore for (archive 17794)
- [ ] `LoadWorkspaceModal` arrow keys change the highlight silently. `:96-120` implements a (archive 17800)
- [ ] `OrderBookModal` rows are `@key`ed on price, so a live depth update destroys and recreates (archive 17805)
- [ ] The chart's failure state is visual-only. `ChartArea.razor:128-149` puts "Connection (archive 17817)
- [ ] ~15 modals render status text into a `role="status"` region created in the same render as (archive 17822)
- [ ] Silent early returns. `TradingDashboardModal:788` returns before `_isVisible = true` when (archive 17847)
- [ ] MAUI: `MainPage.xaml.cs:61-129` subscribes on every `OnHandlerChanged` and unsubscribes only (archive 17877)
- [ ] MAUI: the Android hardware-keyboard bridge is dead for navigation keys and double-dispatches (archive 17883)
- [ ] MAUI: `TrayIconService.cs` is the highest compile risk in the tree. Behind `#if TRAY_ICON`, (archive 17896)
- [ ] MAUI: `BlazorAudioDriver.cs:205-213` (Android) exits its PCM push loop on (archive 17900)
- [ ] Two copies of `app.css` (763 and 782 lines) differing by exactly two hunks — and the (archive 17903)
- [ ] `.sr-only` is defined only inside `TradingDashboardModal`'s `<style>` block but used by (archive 17906)
- [ ] Three independent modal-open counters (`keyboard.js`, `CommandDispatcher`, (archive 17909)
- [ ] `role="dialog"` markup is copy-pasted 25 times. `ModalBase` captures the *event* contract (archive 17917)

### Ship-blockers — trading providers and the SDK order contract (status unclear, check)
- [ ] Tradier equity sides ignore position effect: `sell` vs `sell_short`, `buy` vs (archive 18167)
- [ ] `ReconnectingWebSocket` — three defects in the SDK's shared socket. `SendAsync` is (archive 18276)
- [ ] `KrakenFuturesProvider.cs:367` silently turns an IOC request into a GTC order — it sets (archive 18282)
- [ ] `InteractiveBrokersProvider.cs:649-668` auto-confirms every IBKR risk warning — up to 8 in (archive 18288)
- [ ] `PolygonProvider.cs:126-131` hardcodes the 15-minute-delayed WebSocket while (archive 18291)
- [ ] `FinnhubProvider.cs:155` processes only the last trade in each batched frame (archive 18295)
- [ ] Capability flags declared with nothing behind them. IB declares `L2` and does not implement (archive 18298)
- [ ] `BaseMarketDataProvider.GetCapability<T>` returns null for everything except market data, (archive 18306)
- [ ] No `CancellationToken` on `IMarketDataProvider` or `ITradingProvider`, while (archive 18327)
- [ ] `BarBucketConsolidator.cs:52` drops every negative-valued tick (`tick.Open > 0 && …`), so (archive 18331)
- [ ] `ProviderResult<T>` has zero callers repo-wide, and `ProviderErrors`/`SurfaceError` has (archive 18430)

### Duplication swept 2026-08-22, and what the sweep found (status unclear, check)
- [ ] 36 copies of the indicator parameter accessors, in 15 distinct implementations, and they (archive 18601)
- [ ] `ValueDeviationProvider.Ema` is a different algorithm from every other EMA in the tree. (archive 18619)
- [ ] Three near-identical JSON-backed list stores. `JsonScreenerLibrary`, (archive 18633)

### Correctness / duplication debt found by this audit (status unclear, check)
- [ ] `VolumeProfileLevelProvider:61` silently falls back to a known future-leaking profile. (archive 18656)
- [ ] Three candle classifiers — and the 2026-08-20 entry understates it on two counts. That (archive 18664)
- [ ] ATR is hand-rolled five times with different warmups. `ChartPatternDetector.cs:675-699` (archive 18693)
- [ ] Signal polarity is classified twice and they disagree. `NavigationSonifier.IsPositiveSignal` (archive 18719)
- [ ] Order-failure classification exists three times in three non-equivalent forms. (archive 18739)
- [ ] "Order placed" is spoken unconditionally, including for orders the broker refused. (archive 18748)
- [ ] A forced liquidation is announced as an ordinary fill. `PaperTradingProvider` builds (archive 18752)
- [ ] Paper order updates are published synchronously while the account lock is held. `Emit` → (archive 18757)
- [ ] Stops and take-profits fill at the trigger even when the bar gapped through it. (archive 18771)
- [ ] Realized P&L excludes fees, so the spoken result of a trade is wrong by the round-trip (archive 18776)
- [ ] `GetBalancesAsync` nets a short against a long in the same base asset, so a BTC/USDT long (archive 18778)

### Documentation drift (status unclear, check)
- [ ] README is two to three releases stale in its most prominent sections. Line 14 still says (archive 18872)
- [ ] `ServiceCollectionExtensions.cs:487-490` (WebHost) describes a silent unsandboxed (archive 18896)

### Untested code that decides money, security, or research truth (status unclear, check)
- [ ] The block bootstrap is non-circular, so the sample edges are under-drawn. Found while (archive 19257)
- [ ] Zero tests for (MOSTLY CLOSED 2026-08-22 — five clusters tested, two remain): (archive 19355)

### Flake risk (status unclear, check)
- [ ] OBSERVED 2026-08-25 — `LinuxBwrapSandboxTests.A_script_cannot_read_the_hosts_environment` (archive 19494)
- [ ] No time abstraction anywhere. Every timeout, throttle, debounce, poll interval and (archive 19547)

### Tests that should exist and do not (status unclear, check)
- [ ] Orphaned legs after a manual close: close the position, drive price to the stop, assert no (archive 19620)
- [ ] The focused pump must not deliver a tick for a different identity. Set focus to B, push an (archive 19622)
- [ ] Replace `DataOrchestratorResilienceTests`' hand-copied `Transition` switch with a test (archive 19651)

### Open after the 2026-08-20 hosted-tier and audio batch (status unclear, check)
- [ ] `trader_local.db` is not under `Accounts__DataRoot`. It resolves to (archive 19832)
- [ ] `OhlcvStore` has no schema migration path. `EnsureCreated` will not alter an existing (archive 19837)
- [ ] Server-side state migration for existing hosted installs. Workspaces, `alerts.json` and (archive 19840)
- [ ] The analytics cache key has no user or credential dimension. Correct today — hosted keys (archive 19852)
- [ ] Three candle classifiers. `BarDetailService.ClassifyBar`, (archive 19855)
- [ ] Upper and lower wicks share a waveform. They are now correctly distinguished by pitch (archive 19860)
- [ ] `AccessibleTrader.BlazorClient` cannot be built on this machine (`NETSDK1147: (archive 19871)

### Terminal / lab split (2026-08-01) (status unclear, check)
- [ ] Bundle export from the terminal. Import exists; the app can only write the old (archive 19895)
- [ ] Per-spec confirmation before starting an `ExecutionMode.Auto` import. The count (archive 19898)

### Platform tiers + signal service — designed, not built (2026-08-02) (status unclear, check)
- [ ] `CoinMarketCapProvider` — now optional. Only worth it for fields CoinGecko lacks (archive 19935)
- [ ] `SecEdgarProvider`, `GdeltProvider` — larger, gated on the revision-breadth result. (archive 19937)
- [ ] Signal service LAST, when there is more than one control-tested edge to serve. (archive 19938)

### Company / macro data layer — designed, not built (2026-08-02) (status unclear, check)
- [ ] Start the GDELT recorder. (archive 19963)
- [ ] First test if the layer is greenlit: analyst estimate revision breadth, through the (archive 19964)

### Post-2.0 polish batch (2026-07-26) (status unclear, check)
- [ ] CoinGlass + extra trading brokers (Tastytrade / Bybit / Kraken Futures / (archive 20010)

### Provider system — quality pass (2026-07-24 → 07-25) (status unclear, check)
- [ ] Coinbase live candle volume. Live candles are synthesized from the (archive 20032)
- [ ] MEXC live-price responsiveness on thin markets. The live title price is (archive 20037)
- [ ] Migrate the other crypto providers onto the shared REST base. MEXC uses (archive 20045)

### Follow-ups (status unclear, check)
- [ ] Keyed-feed DataManager refactor if tick-level background evaluation is ever (archive 20136)
- [ ] Auth: HaveIBeenPwned password validator — k-anonymity range API, (archive 20180)
- [ ] Ops: systemd `UMask=0077` drop-in on the hosted unit so future files (archive 20189)
- [ ] FINRA Query API short-interest metric (needs free dev registration — Cody). (archive 20201)
- [ ] Tiered RiskPercent by setup quality (2-tier, evidence-based) in RiskPlan. (archive 20202)
- [ ] Sector risk governor as an optional *enforcing* mode (default stays warn-only). (archive 20203)
- [ ] Pre-registered lab test: wider ladder (3R/6R or trail) for the gated v23 variants. (archive 20204)
- [ ] Schwab streamer ACCT_ACTIVITY (~2-3d, riskier) — requires the full Schwab (archive 20226)
- [ ] MAUI native window title: add the live price (WebHost browser-tab title has it, (archive 20231)

### [2026-07-22] — Local background monitoring core (SHIPPED) (status unclear, check)
- [ ] Factory sound bank — Cody's event WAVs as bundled defaults per earcon key (archive 20248)

### [2026-07-22] — My Data: CSV import (Wave 2 CSV item SHIPPED) (status unclear, check)
- [ ] Windows tray VERIFY — MAUI head close-to-tray (H.NotifyIcon.Maui 2.3.0, (archive 20289)
- [ ] Hosted server-side alerts + Web Push — evaluate saved alerts server-side (archive 20303)
- [ ] xlsx import — future; needs a spreadsheet-parsing dependency. The (archive 20307)

### Mute-tier redesign (the F-key row) (~2-3d total, ship as one coherent change) (status unclear, check)
- [ ] Shift+F4 dedicated braille display picker modal — PARTLY CLOSED 2026-09-06. (archive 20332)

### Follow-ups raised by this pass (status unclear, check)
- [ ] Mexc full contract enrollment after per-plugin dependency folders land. (archive 20380)

### Still open (the honest remainder) (status unclear, check)
- [ ] Native touch layer — iOS adjustable `UIAccessibilityElement` + rotor (archive 20417)
- [ ] Play range — sequencer needs an end-index concept; shift+click summary (archive 20424)
- [ ] Price-axis drag to scale (needs a manual y-range override in state); (archive 20426)
- [ ] "Recent events" visual journal ticker (visual earcons cover the alerting half). (archive 20428)

### Phase D remaining (deferred, tracked) (status unclear, check)
- [ ] "Recent events" visual ticker surfacing the Journal ambiently (deaf/HoH). (archive 20467)

### Phase C second pass (native, deferred — needs macOS + physical devices) (status unclear, check)
- [ ] iOS: `UIAccessibilityElement` with adjustable trait over the canvas (archive 20497)
- [ ] Android: `ExploreByTouchHelper` virtual nodes (adjustable first; per-pane (archive 20500)
- [ ] On-device VoiceOver + TalkBack verification of the shipped web layer inside the (archive 20502)
- [ ] JS test infra (Vitest) so the gesture state machine gets direct tests (Tier-4). (archive 20504)

### Phase B second pass (deferred, tracked) (status unclear, check)
- [ ] Price-axis drag to scale; time-axis drag to zoom; double-click axis to reset. (archive 20545)

### Deliberately deferred (tracked, not forgotten) (status unclear, check)
- [ ] Plugin trust manifest cryptographic signing (hash-based TOFU fine until (archive 20588)
- [ ] Per-user (post-auth) rate limiting. (archive 20590)
- [ ] CAPTCHA-alternative / accessible fallback flow on 429 for shared-IP users. (archive 20591)

### Open (status unclear, check)
- [ ] Multi-layer patch preview vs component parity — verify by ear that the (archive 20706)
- [ ] `AudioSequencer.BuildVoicePlan` slot packing / budget-overflow / (archive 20718)

### Medium-value tests still pending (status unclear, check)
- [ ] `WebHostProgramStartupSmokeTests` (1 case) — boots host via (archive 20859)
- [ ] `DiagJournalEndpointTests` (2 cases) — empty / newest-first. (archive 20863)

### WebHost phases — L5 / L6 / L7 / L8 / L9 (L6 docs is the only one still open) (status unclear, check)
- [ ] L8 — Desktop shell. SKIPPED per user choice (2026-05-16). (archive 20934)
- [ ] Shared connection pool keyed by symbol (per-visitor upstream connections are (archive 20951)
- [ ] Follow-ups: transactional email (confirmation + password reset); tier (archive 20961)

### Cosmetic + known issues (status unclear, check)
- [ ] Chart pixel density. Server renders at fixed 1280×720; CSS (archive 20967)
- [ ] Drawing-tool mouse interactions in browser. With the chart (archive 20985)

### Empirical verification (status unclear, check)
- [ ] PgDn/PgUp cycle skips price-line and produces sensible top/bottom (archive 21105)
- [ ] F4 pause resumes cleanly after workspace identity change (auto-reset). (archive 21107)
- [ ] Pan keys empirically belong to graphic vs strip on the actual device. (archive 21108)
- [ ] Strip X-value timeout (~1.5 s) feels right under live arrow nav. (archive 21111)
- [ ] Body+wick+gap structure is tactile-distinguishable at 4 dot rows (archive 21112)
- [ ] Dot Pad X on-device verification. The Dot Pad X is supported (same (archive 21115)

### Out of scope (deferred to future iterations) (status unclear, check)
- [ ] 3-pane mode. F-key chord toggle (e.g. F12) cycling 1 / 2 / 3 (archive 21125)

### Empirical verification (after each ship) (status unclear, check)
- [ ] Splash actually centers in 60×40 with the chosen font table. (archive 21141)
- [ ] PgDn/PgUp cycle skips price-line and produces sensible top/bottom (archive 21142)
- [ ] F4 pause resumes cleanly after workspace identity change (auto-reset). (archive 21144)
- [ ] Pan keys empirically belong to graphic vs strip on the actual device. (archive 21145)
- [ ] Strip X-value timeout (~1.5 s) feels right under live arrow nav. (archive 21146)

### Important security (status unclear, check)
- [ ] Sign ScriptWorker.exe + plugin DLLs. Use a code-signing cert; verify (archive 21317)
- [ ] Coinbase + remaining-providers credential-checkout migration. (archive 21320)
- [ ] CPU quota uses sliding window. `OutOfProcessScriptHost.cs:262-301` (archive 21324)
- [ ] Sync-over-async cleanup (3 sites). `StrategyAutoLoader.cs:73-74` (archive 21327)

### Build-system safeguards (status unclear, check)
- [ ] RCL platform-code Roslyn analyzer. Error on (archive 21334)
- [ ] DI lifetime validator. Add a startup pass that walks the registered (archive 21338)

### Documentation (status unclear, check)
- [ ] Customer-facing README rewrite. Current README is 90% architecture. (archive 21345)
- [ ] USER_GUIDE coverage gaps. Add sections for: strategy building, (archive 21348)
- [ ] Sample plugin DLL. Ship `Plugins/Samples/SimpleRsiProvider.cs` (or (archive 21351)
- [ ] `Tests/README.md` — fixture conventions, `BlazorTestHarness` recipe, (archive 21354)

### Architectural follow-ups (post-1.0; large) (status unclear, check)
- [ ] God-modal split. `PropertiesModal.razor` (826 lines), (archive 21359)
- [ ] `WorkspaceStore` immutable snapshots. Replace direct property (archive 21364)
- [ ] Plugin manifest v2. Version, signature chain, capability declaration (archive 21367)
- [ ] Modal close-on-Esc + open-on-app-key as single dispatcher cases. (archive 21370)
- [ ] Per-strategy timeout override. `OutOfProcessScriptHost` (archive 21372)

### Genuinely-future research (unblocked, not backlog) (status unclear, check)
- [ ] AssetClassifier on equity snapshots. Pull TSLA/AMZN/SPY via the Alpaca (archive 21408)
- [ ] `v23r-SRRES` fires ZERO times in nine years and is a broken cell, not a null. (archive 21442)
- [ ] v23or weekly cross-asset. OR-gate may broaden coverage on smaller-cap (archive 21458)

### Open follow-ups for v22 (revised after walk-windows analysis) (status unclear, check)
- [ ] Default `walk-windows` over `walk` for strategy validation. (archive 21740)
- [ ] ConfirmThreshold sweep — try 0.5, 0.6, 0.7, 0.8 on v22-LONG (archive 21747)

### Open follow-ups for the timeframe work (status unclear, check)
- [ ] Cross-asset 1d rolling-window for v22-LONG. Walk-windows says (archive 21799)
- [ ] Weekly confirmation across other markets. With adaptation, (archive 21804)
- [ ] Score-component adaptation review. The current weekly (archive 21808)

### Remaining follow-ups (optional, no security impact) (status unclear, check)
- [ ] On-device / on-AppContainer integration tests — a test (archive 22633)

### Next priorities (broader codebase audit — 2026-04-17) (status unclear, check)
- [ ] Toolbar button labels showing raw shortcut keys (surfaced (archive 22714)
- [ ] Phase 5 v2: order book large-order detection. Adaptive (archive 22722)

### Open gaps — next session (status unclear, check)
- [ ] Delete redundant BNVISION_FUNDING / BNVISION_OI lab providers — now duplicating what Core FUNDING_RATE / OPEN_INTEREST provide. Stil... (archive 22793)

### Strategy work — future (status unclear, check)
- [ ] Cross-asset matrix rerun with v18 asset-aware: once Core providers accept `__symbol`, re-run v18 on ETH/SOL/XRP/DOGE in LIVE mode to... (archive 22800)
- [ ] v13/v14/v15 cross-asset walk-forward — never tested on non-BTC. May reveal additional survivors or confirm BTC-specialist pattern. (archive 22801)

### Research / Next Session (status unclear, check)
- [ ] Adaptive WT thresholds for Cipher B — dynamic OB/OS levels based on oscillator's own distribution (percentile-based) (archive 22849)
- [ ] Pulse indicator simplification — consider decomposing v1/v2/v3 signal tiers into strategy conditions (archive 22850)
- [ ] Phase 12 Session 3 — v9 backtest + thesis validation (archive 22851)

### Phase 11 Audit Fixes (2026-04-07) — DONE (status unclear, check)
- [ ] Custom Script tab Roslyn strategy persistence — Roslyn-compiled strategies still aren't saved as `StrategySpec`s. (archive 22983)

### Session 3 — v9 backtest + thesis verdict (planned, next session) (status unclear, check)
- [ ] Test the refactor + CrowdingIndex end-to-end — confirm shared cache means no duplicate fetches when multiple cross-series indicators... (archive 23016)
- [ ] Run v9 backtest on BTC/USDT 1h Bitstamp, recent 30-90 day range (OKX history depth) (archive 23017)
- [ ] Verdict on the strategy thesis — does v9 produce materially different walk-forward metrics than v7/v8? (archive 23018)

### Session 4 — Glassnode + multi-asset (deferred) (status unclear, check)
- [ ] `GlassnodeProvider` plugin — when API key is purchased. Deep history (back to 2019) for funding/OI/sentiment. Same source-name swap... (archive 23022)
- [ ] Multi-asset support — once Glassnode is in or v9 thesis validated, expose `Source` and `Symbol` as string parameters on each cross-s... (archive 23023)

### Session 5 — Glassnode plugin (deferred — paid) (status unclear, check)
- [ ] `GlassnodeProvider` — when API key is purchased. Same pattern as OkxDerivatives. Unlocks deep history for funding/OI/on-chain that t... (archive 23027)

### CommandDispatcher — Chart-Focus Gate + Structural Clarity (status unclear, check)
- [ ] Full Command Pattern (extract `ICommandHandler<T>` per domain) — deferred to Phase 5+ if dispatcher grows beyond current size. (archive 23132)

### SkenderIndicatorProvider — GetDetailFact Extraction (status unclear, check)
- [ ] Split `SkenderIndicatorProvider` into `SkenderIndicatorDiscovery` + `SkenderResultMapper` — deferred to Phase 5+ when a second Skend... (archive 23145)

### Feature Completion (status unclear, check)
- [ ] Multi-Symbol Watchlist: Extend `WorkspaceState` to hold collection of `ChartState`. (archive 23401)

### Performance (from previous Phase 6) (status unclear, check)
- [ ] Span-Based Indicator Pipeline: `ReadOnlySpan<Ohlcv>` + `ArrayPool<double>` in `SkenderIndicatorFactory`. (archive 23413)
- [ ] Full Channels Migration: `Channel<Ohlcv>` from plugin → `DataManager` for live ticks. (archive 23414)

### Phase 10-D — Custom Indicator Platform (Roslyn) ✅ (status unclear, check)
- [ ] `ICustomScriptService.RunScriptAsync` full pipeline: Compiled `ICustomIndicator.Calculate` routed through `IndicatorOrchestrator` →... (archive 23574)

### v7 strategy build (AFTER infrastructure is in place) (status unclear, check)
- [ ] Build v7 — Score-based multi-source confluence (archive 23810)

### Bigger systemic improvements (future, lower priority) (status unclear, check)
- [ ] Walk-forward parameter optimization (expanding window) — re-tune Cipher periods every N months using only data prior to that point.... (archive 23821)
- [ ] Regime classifier with regime-conditional strategy routing — classify each bar as trending/ranging/volatile via ADX + ATR percentile... (archive 23822)
- [ ] Indicator-on-HTF computation (deferred from Session B) — sync `IIndicatorRunner` so HTF leaves can reference indicators not just pri... (archive 23823)
- [ ] Expand SignalCatalog companion-array support — generic mechanism for indicators to expose multiple value streams per component (not... (archive 23824)

## Where the rest went

Everything else is in `docs/archive/TODO_HISTORY.md`: the 2026-08 audits (A1, A2, A3, B1, B2), the 2026-09
START HERE passes, the health-assessment write-ups, the phase plans from March and April 2026, and the old
release sections. Open `docs/CHANGES.md` for what shipped.

