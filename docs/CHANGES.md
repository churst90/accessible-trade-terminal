# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Close part of a position at a limit; size an order by its value (2026-11-04, NOT tried)

- **Close at limit takes a percent.** Cody: "when i limit close a trade i should have the option of
  closing a portion/percentage of the position rather than the whole thing". Beside the price field is a
  percent field (`close-percent-…`, starts at 100, "%" allowed). The order is still a reduce-only limit,
  for `OrderSizing.PartialCloseQuantity`: 100 is the whole size exactly (no dust from rounding), less is
  rounded down to eight decimals. A percent above 100, at or below zero, or not a number is refused out
  loud and focus stays in the field; a share too small to send is refused before anything is placed. The
  confirmation names the percent and what stays open.
- **Size in base or value.** Cody: "I should be able to specify my position size in contracts, not in btc.
  right now if i leave it as '1' that means '1 btc' not '1 dollar'". A new "Size in" select
  (`order-size-in`) picks the base unit (the old behaviour, still the default) or the quote currency. In
  value mode the field is `order-value`, with a hint giving the approximate quantity; the quantity is
  `OrderSizing.QuantityFromValue` (rounded down) at the limit price, or the current price for a market
  order. It is worked out on each edit and again on Submit, never while a live review is armed, so the
  review reads what is sent and a price tick cannot re-arm it. A value with no price is refused. The risk
  Size button sets the unit back to base. There is no contract-size concept: every provider takes base
  units (a Kraken Futures linear PF_ contract is one coin), so a value is the honest "dollars" answer.
- **Caveats:** market closes still close everything (no percent on Close position). The "Size in" choice
  is not remembered between openings. Not tried by Cody; no screen reader run (the accessibility-lead agent
  the hook asks for is not registered).
- **Tests:** new `OrderSizingTests` (25); `TradingDashboardDecouplingTests` close 50% of a 2 ETH long
  (sends 1.0, reduce-only) and refuse 150; `LiveOrderReviewStalenessTests` send 0.02 for 1000 USD at a
  50,000 limit, send 0.04 after the limit moves to 25,000, and refuse a value with no price. The
  money-path scan tests pin the new `CloseAsync` signature. Six sabotages red: 100% floored, value rounded
  up, the whole position sent, the percent check removed, the no-price refusal removed, the submit-time
  recompute removed.

### Cipher C and Cycle Position swap names (2026-11-04, NOT seen)

Cody: "swap the names of the indicators such that cipher c is the cycle position more complete one
and make our clone of camel's indicator the cycle position one". Only the shown names changed:
- `CYCLE_POSITION` (`CyclePositionProvider`): the more complete one, with learned windows, Low is in,
  Low expected, the half-cycle low and the four switches. It is now shown as **Cipher C**. Its table
  head says "Cipher C", and its description and settings call the copy "Cycle Position".
- `CIPHER_C` (`CipherCProvider`, `CipherCEngine`): the copy of Camel Finance's CF indicator. It is now
  shown as **Cycle Position**.
- The codes, class names, parameter names and component names are unchanged. Saved charts,
  strategies and alerts hold codes, so they keep working.
- A restored series takes its FriendlyName from the metadata, so a chart saved before the swap shows
  the new names with no migration.
- The 25 retired-signal messages for CIPHER_C, and Loukas's pointer to its replacement, now say
  "Cycle Position (it was called Cipher C until 2026-11-04)".

Docs:
- The manual has a new primer paragraph, "Cycle lows: Cipher C and Cycle Position". The complete
  indicator had never been described in the manual before this.
- The long cycle-low passage under Auto-narration now names Cycle Position.
- WHATSNEW's unreleased entries now use the new names and say the two were swapped.
- README lists both indicators with their codes.
- Both design docs carry a name note at the top. Their bodies keep the old names, as written.

Tests: `CipherCCyclePositionNameSwapTests` (4) checks the names and codes, a saved chart of each
coming back under its new name, and the complete one's text naming no "Cipher C". Three sabotages
went red: the copy's metadata named Cipher C again, the complete one's provider Name put back, and
its description saying "Cipher C's marks".

Caveat: the design docs, research notes and memory all use the old names. CIPHER_C_DESIGN.md is
about what is now Cycle Position.

### Alt+Shift+D says the candle pattern first; the S&P 500 counts from 2000; new charts get only the default series (2026-11-04, NOT heard)

- **Alt+Shift+D: pattern, then prices.** Cody: "when I press alt shift d, I'd like to hear the candle pattern
  spoken first before the OHLC values rather than after." The candle part of the bar detail
  (`BarDetailService.CandleFact`) is now the shape, pattern and bias, then "Open …, high …, low …, close …"
  (and volume), then the body and wick percentages. Before, the four prices came first.
- **The S&P 500 counts from 3 January 2000.** The preset had no Start date because its chart was thought
  never read. In fact the original's S&P Start label (2000-01-03) was read on 2026-10-27 with its per-asset
  settings on. `CyclePreset("SPX")` now carries it, so the daily count starts there instead of on the
  chart's first bar. The S&P still has no yearly dates (the original draws none on it). The untuned
  fallback is built from the SPX preset but never reads its Start (`StartBar` takes the symbol's own
  preset, null when untuned).
- **"Add Market Structure to new charts" is gone.** Cody: "it's an indicator people can add it if they
  want it, just the default series should be on a new chart, that's it." The switch (F12 → General, off
  by default since 2026-09-05) and its key `analysis.marketStructureDefault` are removed, along with
  `IAppSettings.MarketStructureOnByDefault` and the initializer's seeding step. A saved value of the
  old key is ignored. A new chart is Candles, Volume and Price.
- **Tests:**
  - `BarDetailReadsEverythingTests` now pins the pattern before "Open".
  - `CipherCDailyTests.An_untuned_symbol_is_counted_from_the_first_bar_of_2000` pins SPX on its 2000-01-03
    bar, and on bar 0 when the preset's Start is taken away.
  - New: `NewChartIsTheDefaultSeriesOnlyTests` checks that a new chart registers only Candles, Volume and
    Price while Market Structure is on offer.
  - Each was proven red: prices moved back in front of the pattern, the SPX Start removed, and the
    seeding step put back.

### A volume profile's pitch is its part of the profile again, on a sine, and grit is the bin's size (2026-11-03, NOT heard)

- **Why**: Cody, on babe4449 (the entry below): "volume profile, not quite. I think i like the sine wave pitches for
  the different ranges as it is easy to spot kind of like before. i.e. value area has a certain pitch with the
  length texturing, poc has it's own chime tone with texturing, lvn's hvn's and bars with no data should be a low
  tone...."
- **Now** (`NavigationSonifier.ProfileBinNotes`): every bin is a pure sine ping, equally loud, and its pitch says
  which part of the profile it is in:

  | Part | Pitch |
  |---|---|
  | Point of Control | 880 Hz, a chime: the note plus an octave (0.40) and a bell partial ×2.76 (0.20), with a click, 0.8 s |
  | Value area (VAH and VAL included) | 440 |
  | Outside the value area | 330 |
  | High volume node | 220 |
  | Low volume node | 165 |
  | No volume (NaN or zero) | 110, always clean |

  - **Grit is the size**: `BarGrit` against the biggest bin, on the first note only.
  - **Ping lengths**: every other bin is a 0.40 s ping.
  - **Price is not heard**: it is spoken, with the node name.
- **Removed from babe4449**:
  - the price pitch;
  - the chord, the octave double, the rising and falling edge notes and their delayed-note cancel counter;
  - the triangle and square partials.
- **Kept from babe4449**: a profile bin stops all eight navigation slots before it plays.
- **Heatmap**: unchanged.
- **Tests**: `ProfileSonificationTests` has 9 tests. The babe4449 shape tests were replaced with:
  - the pitch table;
  - the three low tones are distinct and below the value area;
  - the POC chime and its click;
  - every bin is a sine and only its first note is gritty;
  - the no-volume tone;
  - all eight slots are stopped.
- **Sabotages**: ten were run. Each turned a test red, and the file was restored:
  - pitch back to price;
  - the HVN tone made the same as the LVN tone;
  - no special case for empty bins;
  - the chime partial removed;
  - the click removed;
  - the grit removed;
  - grit put on a partial;
  - only slot 0 stopped;
  - loudness back to size;
  - a square partial added.
- **Caveats**:
  - Not heard.
  - The value area edges now sound like the rest of the value area. They are still announced in speech.
  - The HVN is lower than the value area it sits in, because Cody asked for it to be low.
  - The pitches are first guesses at "low".

### Each part of a volume profile has its own sound again: pitch is price, grit is size, the sound is the type (2026-11-03, NOT heard; REJECTED by Cody and replaced the same day, see above)

- **Why**: Cody, on the entry below (d8533b2f): "everything sounds the same except for the grittiness so i can't
  easily spot the POC. I like the other way we had it where the poc made a different tone and the value areas
  and nodes made their own sound … grittiness for bar size, but tone for the different parts of the profile …
  bar size, area type and the position using the pitch."
- **Now** (`NavigationSonifier.ProfileBinNotes`, which returns a bin's notes as slot, delay and voice):
  - **Pitch is the price** and **grit is the size**, as in d8533b2f. Both are on the first note, which is equally
    loud on every bin. Notes added for the type are quieter than it and carry no grit.
  - **The sound is the type**:
    - **Point of Control**: a chord (the note, its fifth and its octave, on slots 0 to 2) with a click, 0.6 s.
    - **High volume node**: the note doubled an octave up, warm, 0.45 s.
    - **Value Area High**: two notes RISING a fourth, 110 ms apart.
    - **Value Area Low**: two notes FALLING a fourth.
    - **Inside the value area**: one warm note (0.20 triangle), 0.40 s.
    - **Outside it**: one plain note, 0.40 s.
    - **Low volume node**: a short thin tick (0.30 square, 0.15 s).
  - **Exclusive focus**: a profile bin stops all eight navigation slots before it plays. It used to stop only slot 0.
  - **The edge's second note is dropped if you have moved on.** A counter is bumped on every profile bin and every
    navigation move, and the delayed note plays only if it is still current.
- **Heatmap**: unchanged from d8533b2f.
- **Tests**: `ProfileSonificationTests` now has 11 tests. One was removed: "every bin is one sound bar a little
  warmth". Six were added: seven types make seven sounds; the POC chord and its click; the edges rise and fall;
  the high and low volume nodes and the value area; added notes are quieter and carry no grit; a stale edge note is
  dropped while a current one plays. Eight sabotages were run: all but the POC one sound, the POC without its fifth,
  the low edge rising, no stale-note guard, grit on an added note, pitch back to the node type, the low volume node
  not short, and loudness back to size. Each one turned a test red, and the file was restored.
- **Caveats**:
  - Not heard.
  - The intervals, lengths and mixes are first guesses.
  - The cancel when you leave the profile (the bump in `SyncNavigationSlots`) has no test of its own. Only arrowing
    from bin to bin within the profile is tested.
  - A one-bin value area is its POC, so it has no edges.
  - The POC's three notes add up louder than a single note, even though each added note is quieter than the first.

### A volume profile's bins sound like bars: size is grit, loudness is constant, pitch is price (2026-11-03, NOT heard; its one-sound-for-every-type part was REJECTED by Cody and replaced the same day, see above)

- **Why**: Cody: "I think profiles are still using the old way of sonifying, specifically the aplitude/volume
  of the sound is tied to the length of the bars but as with histograms, the size of the bars in the profile
  should have associated grittiness and if you are able to hone in the sonification of profiles in general,
  do so."
- **Before** (`NavigationSonifier.SonifyProfile`): loudness ran from 0.15 to 1.0 with the bin's volume against
  the biggest bin. Pitch was the node TYPE: POC 880 Hz, HVN 660, VAH 550, VAL and the rest of the value area 440,
  other bins 330, LVN 220. The note lasted 0.10 to 0.25 s. So size was said twice (HVN and LVN are size classes)
  and price not at all: two bins of one type were the same note wherever they sat, and the value area low
  sounded the same as the value area inside it. A thin bin was quiet, which is what the bar rule exists to
  prevent.
- **Now** (`NavigationSonifier.ProfileBinVoice`), one thing per dimension:
  - **Pitch is the bin's price**: the bottom bin 220 Hz, the top bin 880 Hz, evenly spaced in pitch between,
    so Up climbs.
  - **Grit is the bin's size**: the same `BarGrit` as volume bars and histograms, against the profile's biggest
    bin (the bin the renderer draws full width). The POC is at 0.30; a bin a tenth its size is at 0.03.
  - **Loudness is constant**.
  - **The value area is warm**: a 0.12 triangle partial inside it, none outside. The volume bars' 0.10 square
    partial is on every bin.
  - **The POC clicks** as it starts.
  - Every bin is a 0.40 s ping, the bars' length under the arrows, because the grit is an octave down and needs
    that long to be heard.
  - Node names ("Point of Control", "Value Area High", "Low Volume Node", "Entering Value Area") are still spoken.
- **The heatmap** (`SonifyHeatmap`) had the same loudness rule against its column. It now has constant loudness and
  grit against the column's biggest bin, and a 0.40 s ping (it was 0.1 s, too short for grit). Its pitch (node
  type × price) and sawtooth are unchanged.
- **Removed**: `ProfileBinClassifier.GetWaveform`, `ShouldTriggerClick` and `GetDuration`, which only the old
  profile voice used. `GetBasePitch` stays for the heatmap.
- **Tests**: `ProfileSonificationTests`, 6 new. Five sabotages were each run: loudness back to size, the grit
  set to zero, pitch back to node type, the value-area warmth removed, and the heatmap loudness back to size.
  Each turned a test red, and the file was restored.
- **Caveats**: not heard. Whether 220 to 880 Hz is the right span, and whether 0.12 triangle is enough to hear the
  value-area edge, are guesses. Playback still leaves profiles out, as before. A TPO profile uses the same
  per-bin size the renderer draws (`TotalVolume`).
- **Docs**: USER_MANUAL "The volume profile" said the POC "rings with a distinct square-wave tone". It was a sine
  with a click, and the paragraph now describes the new sound. The README and the knowledge base were updated too.

### A bar close is spoken in the order it happened: the close, the narration ladder, then the new bar's open (2026-11-02, NOT heard)

- **Why**: Cody: "on bar alerts, when a bar closes, I should hear the close and the indicator narration ladder.
  Then the open of the newest bar should be read as well."
- **Before**: everywhere a bar close was spoken with its ladder, the ladder came LAST, after "New bar: open".
  The ladder describes the bar that closed, so it was heard after the next bar had been named. The server
  log from the night of 2026-11-01 shows it: "BTCUSDT 4h: close 77932.95 at 02:00. New bar: open 78087.23.
  Bears take control at 77932.95."
- **Now**, the same order on every route:
  - **The focused chart**: "Close X at T[, candle pattern]. [chart-pattern outcome.] [ladder] New bar: Open Y".
    The coordinator gives the narrator the close and the open separately
    (`IAutoNarrationService.DeferBarCloseSentence(sentence, last)`), and the narrator speaks
    close, then its findings, then the open, as ONE utterance as before.
  - **Background tabs** and **the browser-closed monitor**:
    "ETH/USD 1h: close X at T. [ladder] New bar: open Y." (`BackgroundBarAnnouncer.BackgroundSentence` takes
    the ladder).
  - **Notifications**: a background tab's body is the close, the ladder, then the open. The focused chart's
    body (sent only when the window is hidden) now ends with the open too; until now it stopped at the close.
- **Tests**: 4 new tests, and two existing tests now check the order. Each of the four changes was put back
  the old way, and a test went red each time. The second existing test
  (`HeadlessNarrationTests.With_no_browser_the_ladder_rides_the_bar_close_as_ONE_utterance`) still pinned the
  old order and failed the first CI run on 5118fea4. The local runs had missed it because their filters did not
  include it. It now checks close, ladder, open, and putting the monitor back to ladder-last turns it red.
- **Caveat**: the focused chart's notification body has no ladder. The ladder is worked out after the
  notification is sent, as before.

### Shift+Enter on the chart places an armed quick trade as a limit order (2026-11-02, NOT tried live)

- **Why**: Cody asked for it to be looked into. It was broken in the browser: `keyboard.js` kept every Enter on
  the chart from .NET when Shift was held (`... && self._chartFocused && !e.shiftKey`), and Shift alone is not a
  modifier, so the chord never reached `QuickPlaceLimit`. Ctrl+Enter (market) worked, because Ctrl is a
  modifier. The .NET side (ShortcutManager's `ENTER` + Shift, and the dispatcher) was already right and tested.
- **Now**: Shift+Enter on the chart reaches the dispatcher. Off the chart, and in any text field on it, the page
  keeps it (a new line in a text area).
- **Tests**: 2 new JS tests (keyboard 83 -> 85). Putting the old line back makes the first one fail (84/85).
- **Caveat**: this is a money path. Nobody has placed a limit order with it yet; try it on paper first.

### Checked, not changed: the browser-closed monitor reads only the newest session slot

- Each browser connection saves its own `__last-session__<id>` slot, and the monitor reads the newest one.
  On Cody's machine (2026-11-02) all five slots hold the whole workspace, and the newest holds all five tabs.
  On the night of 2026-11-01 the monitor announced seven closes (1h and 4h, with ladders) from it.
- The 8h tab shows no "Headless chart watching" line because nothing on it is under N, so it has no ladder.
  Its bar closes are still watched, and no 8h bar closed while the browser was shut.
- **Not demonstrated**: two browser windows open at the SAME time on DIFFERENT tabs. Only the window that
  saved last would be watched. This was not tried.

### Cipher DBSI: the study's latest model, zu_hs66e, ported bit for bit (2026-11-02, NOT seen or heard)

- **Why**: Cody: "and please do commit the latest version of dbsi since you've been working on it". This is an
  exception to his 2026-10-29 rule (no more ports until the residue is solved); the search goes on in the study.
- **What**: `DbsiScoreModel` is rewritten as `dbsi8/zu_hs66e.py` (private study), replacing `zt_c2.py` (ported
  2026-10-29). Since then, about sixty rules and fixes have been found in DBSI's own labels:
  - **One event applied everywhere.** Most new rules are a piece we already had, used on another bar type or
    side: the bulls' WT > 60 dead-bar +2, the near-floor +1 at more sites, the floor touch (0, up, 0) on alive,
    break and off bars, the EMA-cross latch held until Stoch RSI falls, and the bear Q that started on the
    hidden arm keeping it.
  - **Float arithmetic as TradingView does it.** The money flow's three SMAs are Pine running sums, and an
    exact tie keeps the last direction. RMA is `(x + (n − 1)·prev) / n`, and the price stochastic is
    `(c − ll) / (hh − ll) · 100`. A Stoch RSI window with no range holds the last value. An exact SMA2 tie or
    an equal close keeps the previous bar's direction. A top-pin dip is read by the sign Pine's own sums give.
  - **Switches Z1-Z18.** They are research switches in the study, all on by default except Z15, and are ported
    as fixed code.
- **Measured** (zt_c2 -> zu_hs66e):
  - The ten veto sets: 98.48% -> 99.99% exact (37,132 of 37,136), within 2 99.73% -> 99.99%, RMSE 0.26 -> 0.03.
  - The blind sets, never used to choose a rule: 98.35% -> 99.97% (33,097 of 33,108).
  - The held-out sets, all 100%: Microsoft 15-minute (was 97.9%), Bitstamp 5-minute (98.5%), Bitstamp
    1-minute (97.0%), Bitstamp daily and its mirrored, shifted and scaled copies (97.9-98.7%), and hourly
    Bitcoin Heikin-Ashi (99.1%).
  - Fresh TradingView reads of 63 daily charts nobody had scored: real-price bars 6 misses in 3,464 (0.17%).
  - The digitised charts, read by eye: the twenty 88.1% -> 88.6% exact, within 1 90.6% -> 90.5%, within 2
    94.1% -> 94.8%, error 1.18 -> 1.16; the fifteen livestream charts 85.0% -> 86.0%, within 1 87.0% -> 87.3%,
    within 2 90.1% (the same). 17+ printed: the same (81 of 84 with 5 false; 46 of 48 with 2 false). Side in
    control: the twenty the same (419 of 422), the fifteen 258 -> 259 of 262.
- **Caveat**: the held-out sets have been gates ("no set worse") since 2026-10-29, so they are not untouched.
  Every miss bar was also re-read from TradingView on 2026-11-01. Harvests made before 2026-10-31 had stored
  wrong labels, and the re-reads changed about 150 of them. The 99.99% is against the corrected labels.
- **Still open** (in the study):
  - Flat-price eras (any bar with high = low in the last 60) miss 36 of 560 fresh labels (6.4%). Their
    money-flow and RSI arithmetic is 0/0 and 1e-14 noise, and TradingView's noise is not ours.
  - Two real-price events are parked: Intel 1990s (bull +4 / bear −5, one shape), and Walmart 2015-11-03, a
    pinned-Stoch-RSI hold that DBSI re-arms. That is 3 re-arms against 30 holds, and no feature separates them.
- **Tests**: `DbsiCleanScores`, `DbsiDigitisedCharts` and `DbsiCoverageCharts` are regenerated from zu_hs66e,
  and the fidelity pins moved to the numbers above. The coverage fixture gains 23 windows of real candles (32 in
  all), each where breaking one rule changes a score and no other window catches it. Thirteen are for the switches
  Z1-Z18, the near-zero bound and the bear Q latch's 80. Ten are for the precision and bar terms: Cisco 2022, the
  yen 2016 and 2025, silver 1915, oil 1988, the Dow 1900, 1906 and 1995, the S&P 500 1886, gold 1844. The generator
  refuses to write unless each of its 41 study-model sabotages changes some window. On the C# port, 43 sabotages
  were run: 36 go red. The first run left 14 green (the money-flow running sum and its tie, the zone and 8-bar
  terms, the two value holds, the carry past a NaN and across a cross, the alive-run and bear floors, the Q hair,
  the held NaN, the Stoch RSI high, the off-run bar after a top). The ten new windows were found for them, and all
  14 are now red. Seven stay green because no score on the ~1.5 million harvested bars changes: fsum against a
  plain sum and its half-even step, the Stoch RSI zero-range tolerance, its 77.2 edge moved to 78, the max that
  keeps NaN against `Math.Max` (they differ only on ±0), and two NaN holds that the zero-range hold makes
  unreachable. The wide gap 17.34 -> 17.4 and the near-zero 5.6 -> 6 change nothing in the study either. 124 DBSI
  tests; README count 9934 -> 9957.
- **Not done**: not seen or heard in the app. The accessibility-lead agent is not registered here (there is no
  UI change). The Start-grid emulator is not moved to this model.

### Drawing tools: fibs like TradingView, the pullback found for you, five pattern tools, Enter, Alt+D, crossings and alerts on drawn levels, times in UTC or 12-hour, and the mouse on the bar it is over (2026-11-01/02, NOT seen or heard)

- **Why**: Cody: "address all the issues you found with the drawing tools alt d, fibs, etc. incorporate the
  additional drawing pattern recognition tools as well that may be useful...yes i want the fib levels
  narrated. on the fibs for the third point, how would i find the pull back level, like a retest of the swing
  low?" and, on the clocks, "I may not want 24 hour time, so we need AM and PM if we do 12 hour time". What
  was wrong: a fib retracement drew 0% at the swing start, so every level from 23.6 to 78.6 had the other
  level's name next to a TradingView tutorial; Enter did nothing while drawing; Alt+D listed eleven of the
  tools, so the Gann fan, Gann box, angle fib and risk/reward could be reached only by a shortcut nobody had
  been told about; fib levels never chirped and Ctrl+Right on a focused fib said "No points of interest"; no
  drawn level could be alerted on, though the manual said drawings were what an alert refers to; and a mouse
  click near the right edge landed up to five bars off the candle under the pointer.
- **What changed** (77ea9390, 5b7848a2, 566af293, c169d96a, 9b218901, 15901c66):
  - **Fibs** (`FibLevels`, new: one place for where a level is, what it is called and what is alerted on).
    0% at the swing end, 100% at the start, TradingView's eleven default levels (0 to 423.6%), and a Levels box
    in Properties for your own (fractions or percentages). A trend-based extension's 100% is one whole swing
    from its third point.
  - **The pullback.** After a fib extension's second point the app finds the pullback and says it: after a swing
    up, the lowest low before price goes above the swing's high again (the mirror after a swing down), with its
    price, date, how many bars right of the swing end it is and how much of the swing it retraced, and whether
    price has gone back past the swing end yet.
  - **Swing snap** (Settings, General, "Snap swing points to the wick", on by default): a fib, pitchfork or
    pattern point goes on its bar's low or high instead of the close, the first two points deciding which is
    the low, alternating after.
  - **Five pattern tools** (`PatternReading`, new): ABCD, XABCD (Gartley, Bat, Butterfly, Crab, Cypher, or the
    nearest), head and shoulders (neckline break and target), triangle (which triangle or wedge, where the sides
    meet), Elliott impulse (the three rules). Each says what the placed points make. They read the points; they
    do not search the chart. Also fib time zones. Trend lines and channels have Extend left / Extend right.
  - **Enter** places the next point while the chart has focus (`ConfirmCoordinateEntry`, unbound and unhandled
    until now); re-pressing the tool's shortcut still works. **Alt+D** is built from `DrawingToolCatalog`, all 21
    tools in five groups, and a test holds every `DrawingType` to it once.
  - **Crossings** (`LineDrawingCrossings.PriceLines`): fib levels, channels, pitchforks, risk/reward, AVWAP,
    necklines, triangle sides and trend lines chirp when crossed and are Ctrl+Left/Right stops ("Price crosses
    below 61.8% level at 138.2"). A fib, risk/reward and pattern lines count only after their last point.
  - **Alerts on drawn levels**: "A line on the chart" in the Alerts dialog lists a fib's levels, horizontal lines
    and risk/reward lines with their prices; choosing one makes a plain price alert, which the browser-closed
    monitor can watch.
  - **Times** (F12, Speech): "Times of day are read in" this computer's zone (default) or UTC, and "12-hour clock
    with AM and PM". Both live on `SpeechTimeFormatter`, so speech, the hover readout, the Dot Pad strip and the
    chart's time axis change together. "Time only" on a daily chart now names the date instead of "18:00".
  - **Mouse**: clicks, Shift+click measures, right-clicks, handle grabs and the hover hairline use the renderer's
    own slot geometry over the plot width (the price-axis column is not chart). A handle past the last bar is
    grabbed where it is drawn. Clicks that cannot act now say why: "No bars loaded yet.", a point before the first
    bar or past the room to the right, "No bar there to measure to."; a press with no tool armed and a moving
    pointer stay quiet.
- **Caveats**: none of it seen on screen or heard by Cody. Vertical lines and fib time zones are drawn as marks
  nobody has looked at; the 12-hour axis labels are wider, also not seen. An alert on a drawn level stays at its
  price if the drawing is moved (the confirmation says so); sloped lines are not offered. "This computer's time
  zone" on the VPS is the server's zone; the hint names the zone id and offset. The mouse fix also moves the hover
  readout and hairline at the chart's edges. Bars are named by their open, as TradingView labels them; a
  "speak the close time" option was not built. Found, not changed (money path): Shift+Enter for a quick-trade
  limit looks never to be forwarded by `keyboard.js`; unverified. The accessibility-lead agent is not registered
  here, so no accessibility review agent ran.
- **Tests**: `DrawingPatternToolsTests` (new, 24), `DrawingEnterPlacementTests` (new, 5), crossings 10,
  alert lines 2, `SpeechClockSettingsTests` (new, 6) and a daily "time only" test, 14 mouse tests (every pixel of a
  slot picks its candle, handles at the bar centre and past the last bar, the axis column, the hairline, the
  refusals). Sabotages, each red: crossings 6, alerts 4, time settings 9, mouse 10; fibs, pullback, swing snap,
  patterns, catalog and Enter 15 (fib orientation, extension from point 3, default levels, custom levels, pullback stop, pullback unsaid, snap flipped, snap setting ignored, H&S target, Elliott wave 3, ABCD equality, a tool dropped from the catalog, Enter dispatch, Enter unbound, Enter forwarded everywhere). The doc guard also caught two new manual sentences ("measured" and "trend-based" read as other tools); reworded. Suite 9934 tests; full run on CI at push.

### With the browser closed, a bar close is said once, with its real open, when the venue answers mid-update (2026-11-01, NOT heard)

- **Why**: Cody's terminal log from 2026-10-31, browser closed, BTCUSDT on Bitstamp: "1h: close 77401.41 at 07:00"
  said twice an hour apart, the 08:00 close never said, and several "New bar: open" prices that were not the
  bar's open. The 4-hour tab said its 07:00 close twice too. Cody had several workspaces open, but the
  monitor holds only one 1-hour watch, so this was not two workspaces. Polling Bitstamp's btcusd hourly and
  4-hour lists every 5 seconds across 04:00 UTC on 2026-11-01 showed the cause. At 04:00:01 the hourly list
  had 999 bars: 02:00, then 04:00, no 03:00. At 04:00:04 the 03:00 bar was back, but the 04:00 bar read
  open = close = 77429.98 with zero volume, a price from five hours earlier (the market was at 77658). By
  04:00:08 both lists were right. The monitor polls once a minute and announces `bars[^2]` the first time
  it sees a newer bar. Caught in the first case, it re-announced the 02:00 bar, and every later poll saw
  04:00 as already seen, so 03:00 was never said. Caught in the second, it read the stale price as the new
  bar's open.
- **What changed**: `VenueSettling` (new) recognises both shapes, when the last poll saw a bar forming: the
  forming bar is missing while a newer one is present, or the new bar has zero volume on a feed whose previous
  bar has volume. The local monitor sets such a list aside before observing, announcing or evaluating
  anything, and the next poll, a minute later, tries again. One new bar is set aside at most twice. After
  that the list is believed: a quiet market can open a bar with no trades, and that bar is real. If the
  forming bar is still missing then, the close is noted and not announced, so the bar before it is never said
  twice. The hosted monitor (VPS alerts) sets the same lists aside, as an empty list (skipped quietly), not a
  failed fetch (which reports a dead feed). Its price alerts compare the newest close with the one before, so
  the stale placeholder could have fired a false cross. That was not observed, only reasoned.
- **Caveats**: a close that lands in the window is now said up to a minute later. The two shapes were seen
  on Bitstamp only, at one boundary. Other venues were not polled. The new bar's open on Bitstamp is
  provisional for its first seconds: open = the last close until the first trade, revised afterwards (the
  07:00 bar read 77470.99 at first, 77456.44 later). The guard does not cover that, so an open read in those
  seconds can be off by a few dollars. The hosted monitor's newest-bar memory is not pruned, at one entry
  per alerted chart. The hosted guard is tested by calling it directly; its call in the poll is not.
- **Tests**: `VenueSettlingTests` (new, 12): six shapes (missing bar, placeholder, ordinary close, several
  closes after a sleep, a feed with no volume, no new bar); at most `MaxSkips` skips; no seed, no skip; the
  hosted monitor returns empty and then the list; and through the real poll (`HeadlessMonitorHarness`, new
  `Tamper` hook): a missing bar is said on the next poll exactly as an untampered run says it, and once; a
  placeholder's open is not read; a bar that never comes back is not announced as the bar before it, and the
  next close is said. Nine sabotages, each red: the local skip call (2 fail), the silent note (1), the
  missing-bar clause (3), the placeholder clause (2), its volume condition (1), the skip cap (1), the
  new-bar check (1), the hosted skip (1), the hosted memory (1). A slice of 222 monitor tests is green
  locally. On the first push CI's enrollment guard failed (1 of 9836): the new class polls the monitor but
  had not joined the `CircuitCoverage` collection, so it could run beside the pretend-browser tests. It has
  now. The accessibility-lead agent is not registered here; this change has no UI.

### With the browser closed, a chart on a timeframe the venue lacks is watched again (2026-10-31, NOT seen)

- **Why**: Cody: "I have background monitoring on and I'm not getting any alerts for my 24 minute chart" and
  "browser closed but it's still running and I'm only getting new bar close notifications for like one or so
  workspaces". His last session holds three BTCUSDT tabs on Bitstamp: 1 day, 4 hours and 24 minutes. The
  browser-closed monitor asked Bitstamp for 24-minute bars; Bitstamp has none (step=1440 answers "Not a valid
  choice", checked by hand), so every poll of that tab failed at debug level. The chart itself works because it
  builds 24-minute bars from 3-minute ones; the monitor did not. The failure was never reported either: the
  dead-feed count was kept per symbol, and the 1-day tab on the same symbol succeeding reset it on every poll.
  The 1-day and 4-hour tabs were watched all along; neither had closed since the browser closed.
- **What changed**: `BackgroundBarFetch` (new) is the one fetch for `LocalBackgroundMonitor` and
  `HostedAlertMonitor`. A native timeframe is asked for as it is. Any other timeframe is built from the largest
  native timeframe that divides it, through the same resampler the chart uses, with one extra bar fetched and
  the oldest built bar dropped (it usually starts mid-period, so its open, high and low would be wrong). The
  local monitor's dead-feed count is now kept per chart (symbol and timeframe), and its report says
  "Background monitoring stopped for BTCUSDT 24m ... Alerts, bar closes and narration on this chart are not
  being watched." Every browser-closed path is fixed by this: bar closes, narration, alerts, and the hosted
  monitor's alerts on the VPS.
- **Caveats**: the hosted monitor's dead-feed count is still per user and symbol. The poll loop passing the
  timeframe to the per-chart count is not covered by a test, only the count itself. Not checked against a
  live Bitstamp poll yet: Cody's terminal runs the old build until it is restarted. The in-browser background
  tabs fetch through the chart's own path and were never affected.
- **Tests**: `BackgroundBarFetchTests` (new, 3; the fake provider refuses a non-native step as Bitstamp
  does): a 24m request goes out as 3m and every bar is whole and on a 24-minute boundary; a native 4h request
  goes out unchanged; at the venue's 1000-bar cap the partial oldest bar is dropped. `LocalBackgroundMonitorTests.
  A_dead_chart_is_reported_even_while_another_timeframe_on_its_symbol_works` (new). Three sabotages, each red:
  the old direct fetch (2 fail), no drop (1 fails), the count keyed per symbol (1 fails). Full suite green on CI; 9819 tests.
  The accessibility-lead agent is not registered here.

### Noise means a zone: no line or volume bar has a texture outside one (2026-10-31, NOT heard)

- **Why**: Cody, answering question (3) (should MACD, Signal lines and Aroon keep the faint brown tinge?): "no,
  macd and others should not have the brown noise tinge. the noise coloring should only happen in zones such as
  over bought or over sold, as it signifies that zone. Having it always on would lose its meaning."
- **What changed**: the sound code gave every Line component a 0.05 brown noise on every bar (the price line's
  timbre), unless the line had a textured overbought/oversold zone. That tinge is gone, so MACD, Signal lines,
  Aroon and the price line are clean on every bar; their triangle and square colouring is unchanged. On the same
  rule, the volume bed's constant 0.06 brown noise is gone too (it still carries its sub-octave weight by bar
  size, and differs from the histogram by its softer square and its pitch). This one was not asked about: Cody
  can ask for it back. The `NoLineTinge` switch from the Fear & Greed entry below, and
  `AudioZoneHelper.HasZoneTexture`, had no use left and are removed (neither was released). Not changed: the
  brown texture while a window is open (it marks a state, like a zone), a drawing's pink texture, the Flute
  sound theme's breath, and any noise a user puts in their own sound patch.
- **Tests**: `No_indicator_component_sounds_noise_outside_a_zone` (new): every component of every indicator,
  with its zones' texture taken away, sounds no noise at -80, -1, 0, 0.5, 50 and 80. MACD's check in
  `Pulses_lines_are_clean_between_70_and_30` now pins it clean; `VolumeCarriesNoNoise` replaces
  `VolumeCarriesABrownNoiseTinge`; `A_textured_level_that_is_not_overbought_or_oversold_adds_no_texture`
  replaces the test that leaned on the old switch and still guards the zone role check.
  `TheHistogramAndTheVolumeBedAreDistinctInstruments` no longer compares their noise. Two sabotages, both red:
  the line tinge back (5 tests fail, the new guard among them) and the volume tinge back (2 fail). Slice of 3111
  (Indicator/Provider/Level/Sonification/Audio/Component/Clone/Timbre/Volume/Window/Doc) green; full suite on CI;
  9815 tests. The accessibility-lead agent is not registered here.

### Fear & Greed has no texture at all (2026-10-31, NOT heard)

- **Why**: asked whether Fear & Greed should lose its texture and its line tinge like COT, Crowding Index and
  Funding Rate, Cody said: "yes clear that".
- **What changed**: the Sentiment line no longer has the faint brown tinge every plain line has (the price line's
  timbre), so it is clean on every bar. A new component switch does this, `NoLineTinge`
  (`IndicatorComponentMetadata` → `ComponentConfig`, provider-owned, rebuilt from the indicator on every load);
  Fear & Greed's Sentiment is the only part that sets it. Extreme Greed and Extreme Fear no longer declare a zone
  texture (it had never played) and stay plain lines, not overbought/oversold lines: their bells, speech and
  Ctrl+Left/Right are unchanged. Nothing else about the line changed: still drawn as a line, still triangle
  above 50 and sine below.
- **Tests**: `Fear_and_greed_has_no_texture_and_no_overbought_or_oversold_line` replaces the test that pinned
  the tinge. `A_textured_level_that_is_not_overbought_or_oversold_leaves_the_line_tinge` keeps the role check
  in `AudioZoneHelper.HasZoneTexture` guarded (the old test was its guard). Six sabotages, all red: the switch
  off on Fear & Greed, the factory not copying it, the sound ignoring it, Extreme Greed's texture back, the
  role check removed, and `ComponentConfig.Clone` dropping it (an existing clone guard). Slice of 2941
  (Indicator/Provider/Level/Sonification/Audio/Component/Clone/Knob) green; full suite on CI. The
  accessibility-lead agent is not registered here.
- **Still open**: every other line without zones (MACD, Signal lines, Aroon) keeps the tinge; question (3) in TODO (answered: the entry above removes it).

### COT, Crowding Index and Funding Rate have no texture, and their extreme lines stay plain lines (2026-10-31, NOT heard)

- **Why**: Cody, answering the question left by the entry below: "no, cot, crowd funding should not use the
  texturing or over sold/ob lines".
- **What changed**: their extreme lines (COT's Crowded Long/Short, Crowding Index's Long/Short Crowded, Funding
  Rate's Extreme Long/Short) no longer declare a zone texture. It had never played, because the lines are not
  overbought/oversold lines, so nothing sounds different: the three are clean on every bar, past their extremes
  too. The lines keep their bells, their speech and their Ctrl+Left/Right behaviour; they are not made
  overbought/oversold lines.
- **Saved charts**: a level saved with the old 0.20 white heals to the new default (none), as before; the 0.45 pink
  default pushed this morning (d786f762) was never deployed, and a level saved with it since carries it as its
  baseline and heals the same way.
- **Tests**: `Cot_crowding_and_funding_have_no_texture_and_no_overbought_or_oversold_line` (three cases) replaces
  the Funding Rate test: no level declares a texture, no level is an overbought/oversold line, and the part is clean
  inside and past its extremes. Two sabotages, both red: a texture put back on Funding Rate's Extreme Long, and
  COT's Crowded Long renamed so it reads as an overbought line. Indicator/Provider/Level/Sonification slice 2509
  green; full suite on CI. The accessibility-lead agent is not registered here.
- **Still open**: Fear & Greed (Cody did not name it) keeps its extreme lines' texture, which does not play, and
  the line tinge; a question in TODO.

### Texture only in the extreme zones: Cipher B's waves clean inside ±53, the same soft noise past ±53 and louder past ±60 (2026-10-31, NOT heard)

- **Why**: Cody: "there should only be texturing, i.e. the white noise under the sound, within those extreme zones.
  In the case of cipher b, there are 2 lines, correct, -53 -60 53 and 60. my idea was to have the softer texturing
  between the 53 and 60 and above/below the 60/-60 lines have the louder more prominent white noise. As of right now
  though, I am hearing the softer white noise all the time, are we making sure this is being set correctly? the
  normal rsi may also be effected... perhaps instead of the louder white noise, we can just amplify the softer
  noise". Yes, ±53 and ±60 are the two lines. He was right: the two waves carried a texture of their own
  (0.15 pink) on every bar, and their zones switched to a different, harsher WHITE noise (0.30 past ±53, 0.45
  past ±60).
- **Cipher B**: the waves have no texture of their own. Clean inside ±53; 0.25 pink between ±53 and ±60; the same
  pink at 0.45 past ±60. RSI and Stochastic (80/20, unchanged at 0.45 pink) and the other parts are clean in
  their normal range.
- **The plain RSI** was already right: clean from 30 to 70, pink past them. Pinned now.
- **The others**: eight more components had a texture of their own on every bar, now removed: Fear & Greed's
  Sentiment, Crowding Index's score, COT's z-score, Funding Rate (0.10 each), Top/Bottom Detector's Capitulation
  and Distribution (0.05), Pulse's Fast Trigger (0.06) and MFI (0.05), Open Interest (0.08). Every zone is pink
  now: the four sentiment indicators' extreme lines were white 0.20, now pink 0.45.
- **Lines with zones**: every part drawn as a plain line had a faint brown tinge (0.05) on every bar, the price
  line's timbre, and in a zone it turned the zone's noise brown. A line with an overbought/oversold zone
  (Pulse's Fast Trigger and Anchor) no longer has it: clean between 70 and 30, the zone's pink past them
  (`AudioZoneHelper.HasZoneTexture`). Lines without zones (MACD and the like, the price line) keep it.
- **Saved charts and saved defaults**: a level saved at an old default takes the new one. With a baseline it is
  healed field by field as before; a level saved before levels kept baselines (2026-10-17) is healed only when it
  holds a texture KNOWN to have been a default (`RetiredTextures.ZoneNoise`: Cipher B's 0.10/0.30 and 0.20/0.45
  white, the sentiment extremes' 0.20 white). "Save as default" writes every field, so an indicator default saved
  before today holds the old values whether or not anyone touched them: a saved value equal to a retired default
  is read as never chosen (`RetiredTextures`), any other value is kept. Cody's own saved Cipher B opens clean
  inside ±53 and pink in both zones. If only the amount of a level was changed, the type still follows the
  indicator (pink).
- **Not fixed (a question in TODO)**: Fear & Greed, Crowding Index, COT and Funding Rate's extreme lines declare a
  zone texture but are not named overbought/oversold, so it has never played. Before today their only texture was
  the one on every bar; now they have none outside the line tinge (Fear & Greed's line keeps it). Making those
  lines count as overbought/oversold would also change what they say and where Ctrl+Left/Right goes.
- Tests: CipherBZoneTextureTests (both waves at 0, ±10, ±30, ±52.9, ±54, ±59.9, ±61, ±90; Cipher B's other parts;
  the plain RSI; Pulse's lines and MACD's tinge; Fear & Greed's line keeps its tinge; Funding Rate clean; a fleet guard: no component of any indicator
  declares a texture of its own and no zone is white, over 30+ textured levels; Cody's save; baseline-less old
  levels; a chosen texture kept; old saved defaults, both waves). CipherBLookTests and SavedLevelsHealTests updated.
  18 sabotages, 18 red (two were green until the tests covered them: Wave Trend 2's old saved default, and the
  zone check's role test). Not heard in the app; the accessibility-lead agent is not registered here.

### Cipher B's Wave Gap and Money Flow sound like RSI and jump between their zero crosses (2026-10-31, NOT heard)

- **Why**: Cody: "the wave gap and the money flow have reversed sounds and the sounds aren't the oscillator sound
  triangle above and sine below like the rsi. and why do I hear over sold and over bought when I do control
  left/right on the wave gap? ... is this accurate?" Not on those two. The ±53/±60 lines are Market Cipher B's
  overbought and oversold for the wave trend (its Over Bought/Over Sold Level and Trigger inputs), so on the two
  waves the jumps are right. The Wave Gap and Money Flow have no such lines; their one line is zero.
- **Sound**: the two wave forms declared their own waveforms, the Wave Gap square above zero and sawtooth below,
  Money Flow sawtooth above and square below: opposite to each other, and neither the oscillator voice. Both now
  take the profile's oscillator voice, as RSI does: a sine with a square tinge above the midline (zero for them)
  and triangle warmth below. Pitch, loudness and the histogram form are unchanged.
- **Ctrl+Left/Right**: both answered to no line, and a component with an empty list is given every line in the
  pane, so the key on either jumped to the waves' ±53 and ±60 and said "overbought"/"oversold". Both forms of
  both now answer to the zero line only (`CipherBProvider.ZeroOnly`): the key jumps between zero crosses and says
  "Zero line cross at ...".
- **New**: in the wave form they now click as they cross zero, as the blue waves and RSI's midline do. Until
  today they had no click at all (the empty list also silenced it). They still do not click or roughen at ±53/±60.
- Charts saved before today get both changes on open (waveforms and the level list come from the indicator, not
  the save). Caveat: a Cipher B sound default saved as the indicator's default before today would keep the old
  waveforms; none is saved on this machine.
- Tests: CipherBWaveGapVoiceTests (same waveform fields and same timbre as RSI either side of the midline; a click
  at zero and none at ±53/±60, no roughness past them; Ctrl+Right lands on the zero cross, not +53, in both
  forms); CipherBWaveFormTests now pins the zero-only list. 10 sabotages, 10 red. Local slice (Cipher B,
  indicators, providers, crossing, oscillator, knobs, audio, narration, levels, workspace) 3099 green; full suite
  on CI. Not heard in the app; the accessibility-lead agent is not registered here.

### Cipher B has Market Cipher B 2.4.0's settings, colours and lines, and nothing else (2026-10-31, NOT seen or heard)

- **Why**: Cody: "the code for cipher b should be clean, tight and accurate to spec with all options found in the
  trading view version also in our version, true to spec." Each input of MCB 2.4.0 was changed on TradingView to see
  what it moves, and its declared plot styles were read from the chart (TradingView trial).
- **Settings** are MCB's six, under MCB's names, on General (no Advanced tab): Use Current Chart Resolution (on),
  Different Timeframe ("60"), Over Bought Level 1 (60), Trigger 1 (53), Over Sold Level 1 (−60), Trigger 2 (−53).
  MCB's "Version" input is a label (2.4.0) that changes nothing, so it is not offered. Gone: WT Channel Period,
  WT Average Period, Money Flow Period, WT Slow Wave Smoothing and Buy Dot Level. MCB offers none of them; its
  periods are fixed (9, 12, 3, 60; the lines 40 and 81), and those are what Cipher B uses.
- **One number, one meaning** (as in MCB): Over Sold Level 1 is the −60 line AND the buy dot's threshold; Trigger 2
  is the dotted −53 line AND where the money-flow bar sits (40 to 50 below it). Over Bought Level 1 and Trigger 1 are
  the +60 and +53 lines. Changing a setting on General moves its line, typing a line's value on Levels changes its
  setting, and Save keeps them together. How: `LevelDescriptor.ValueParameter` / `LevelConfig.ValueParameter` and
  `ZoneBandConfig.AnchorParameter` / `AnchorValue`; `ParameterBinding.Apply` (new, Sdk) puts the lines and the band
  where the settings say, on a new series, a restored one, Reset and each setting typed in Properties. The zero line is not a setting.
- **Different Timeframe**: with Use Current Chart Resolution off, Cipher B is computed on the other timeframe's bars
  (built from the chart's) and each chart bar shows the last value finished by then: a period's value appears on the
  chart bar that finishes it, earlier bars show the period before, and the newest bar shows the period still forming.
  TradingView's text is read as minutes (60 → 1 hour, 240 → 4 hours) or D, W, M (with or without a number). A
  timeframe no longer than the chart's gives the chart's own values. Dots appear only on a period's last bar.
  Warm-up is counted in the other timeframe's bars (225 of them).
- **Look**, as MCB 2.4.0 declares it: fast wave rgba(144,202,249,0.7) and slow wave rgba(13,71,161,0.7), both 1-pixel
  lines over their areas (the fast wave was opaque #90CAF9 at 2 pixels and the slow had a #1E88E5 edge, ours); Wave
  Gap 75% yellow (was 73%); Money Flow 40% green/red (was 50%); cross dots up 80% green, down 65% red (were #00E676 /
  #FF5252 at 85%); buy dot 80% green; the money-flow bar 40% at −93…−103 (was 30% at −92…−100, fitted to 2.2.6
  screenshots). Lines: ±60 solid white 40% at 2 pixels, ±53 dotted white 50%, zero solid white 40%, and MCB's dotted
  "100%" line at +100 (new; drawn and silent, no component listens to it). The lines were grey #8C8C96 (our contrast
  choice for light themes). The pane's scale now reaches −103, the bar's bottom.
- **MCB's fills, answering Cody's question**: MCB's only `fill()` is "Plots Background", the money-flow bar. Its other
  coloured areas are its plots' own area style (the waves, Wave Gap, Money Flow), which Cipher B draws the same way.
  The WT Fill and Anchor Fill removed earlier today were ours. On TradingView a user cannot add a fill between two
  plots; the Style tab only shows, hides and recolours the fills a script declares. So nothing was missing.
- **Saved charts** open with MCB's six settings and nothing else (`WorkspaceInitializer.MigrateCipherBSettings`):
  undeclared keys dropped, missing ones added at their defaults; a Buy Dot Level moved from 60 becomes Over Sold
  Level 1 (45 → −45); a ±53/±60 line moved before the lines were settings gives its setting its value. A saved fixed
  band the provider declares takes the declaration's colour and place (only its shown/hidden is the user's).
  A saved chart that changed a removed period now draws with MCB's period: its picture changes.
- **Lab**: `--set CIPHER_B.WT1Period=…` examples now name settings that exist.
- **Caveats**: the waves' spoken "overbought"/"oversold" still uses ±53 whatever Trigger 1/2 say (the bar summary reads
  the settings); the pane's floor is fixed at −103, so with Trigger 2 below −53 the bar is clipped; the buy dot is
  drawn at the fixed −96; minute timeframes below the chart's are not supported (MCB would compute them from the lower
  timeframe); 4-hour and daily buckets are aligned to UTC, not the exchange session, on stocks; Different Timeframe
  on a Heikin-Ashi chart is not checked against TradingView; white lines on light themes and the translucent waves on
  SteelGray (fast wave 2.995:1, just under 3:1) are questions in docs/TODO.md.
- **Tests**: new CipherBMcbInputsTests (binding, idempotence, lines and bar placed by the settings, buy-dot threshold
  −60/−40/−20, saved-chart migration three ways, 17 timeframe texts, the longer-timeframe rule, the period-end step
  on weekday bars against resampled weekly bars, chart-or-shorter = own values, warm-up × 7, a numeric-looking
  timeframe stays text) and CipherBBoundLevelPropertiesTests (the dialog both ways and Save; the zero line keeps its
  own value); PropertiesAdvancedTabTests checks the six labels; CipherBLookTests floor 2.99 on SteelGray only;
  DeclaredBounds CIPHER_B (−103, 100); restore tests: Cody's saved chart with moved settings opens with its lines on
  those settings and MCB's look (±60 solid 2 px, ±53 dotted 1 px), and a user's own line width stays while the
  extreme lines heal to 2 px. 29 sabotages, 28 red; three (the heal keeping the binding, the width copied, the
  binding on restore) were green until the restore tests were added, then red; the one still green (Properties'
  Apply re-binding) was redundant, every typed setting already binds, and that code is removed.
  Different Timeframe checked against TradingView: MCB on Dow Jones daily with "W", 31,858 of 32,522 bars exact (1e-6),
  the misses warm-up (gone by mid-1898) and holiday-short weeks before about 2000, where TradingView shows the week a
  bar later; one miss since 2000. The first push (2cc42d00) failed CI on one test: the timeframe switch was read by a
  private boolean reader, which the IndicatorBoolParameterReachabilityTests guard forbids; it now uses the shared
  IndicatorParams.GetBool (fixed in the next commit; my local slices had not included that guard). README 9792. The accessibility-lead agent is not registered here.

### Cipher B is only what Market Cipher B draws: its own extra parts and old settings removed; the picture is the same (2026-10-31, pushed ba7b67f5, NOT seen or heard)

- **Why**: Cody: "anything not in the original, please remove as well....unless you can think of a good reason not
  to....", then "Yes remove all of that old code from cipher b ... gert rid of all of that....", then "just make sure
  that visually it doesn't change the way cipher b looks to the original mc b". All of it was left out or hidden by
  default (Market Cipher B draws none of it), so on a default chart none of it showed.
- **Removed**: the red "Overbought Crossover" dot (our own gated sell rule; MCB has no sell dot), the regular and
  hidden divergences with their pivot lines, Anchor Wave, Anchor Wave 2, Anchor Polarity, the Anchor Fill and WT Fill
  clouds, and the settings only they or the old formulas read: Money Flow Formula (Legacy), Buy Dot Rule (Gated),
  Threshold Mode (Percentile, with its lookback, percentiles and floor), Confirm Bars, Cooldown Bars, anchor
  suppression and depth, timeframe awareness, pivot bars, anchor multiplier, OB level and the divergence confirmation
  lag. Cipher B's Properties has five settings now (WT Channel Period, WT Average Period, Money Flow Period, WT Slow
  Wave Smoothing, Buy Dot Level), no Advanced tab and no cloud fills.
- **Kept**: both waves, the Wave Gap, Money Flow and its bar, the small cross dots (WaveTrend Cross Bull / Bear), the
  Buy dot ("Oversold Crossover"), the RSI and Stochastic lines. "WT Slow Wave Smoothing" stays (my call): it is MCB's
  own slow-wave smoothing, 3 by default, and 4 still gives the slow wave from before 2026-10-17.
- **The picture is unchanged**: Cipher B rendered before (2e86d377) and after on 4 charts at several zooms, 58 frames:
  every frame identical, all 58 PNGs byte for byte; the list of drawn parts and every kept part's values identical.
- **Saved charts, strategies, alerts**:
  - A saved chart drops the Anchor Fill and WT Fill (a fill the indicator no longer declares is pruned on restore,
    any indicator) and the three mode settings (`WorkspaceInitializer.MigrateSeriesConfig`).
  - Strategies and alerts on a removed part say it was removed and name the nearest part (`SignalCatalog.Retired`):
    WaveTrend Cross Bear for the red dot, Wave Trend Fast / Slow for the anchor waves, nothing for the divergences
    and Anchor Polarity. A spec that reads one only inside an OR still fires on its other conditions.
  - **Twelve catalogue strategies retired** (v14, v15, v18 and the v23 family, `CatalogueProvenance` retired list,
    verdicts kept): each needed a removed part in a required condition. Catalogue version 2026-10-31.1.
- **The lab**: `--cipherb old` is refused (there is no old Cipher B); the three setup graders refuse to run and exit
  2 (their pre-registered rules read the divergences or the red dot; build 2e86d377 to re-run one; results stand).
  Research commands that defaulted to the red dot for the short side use MCB's cross-down dot. `TradeRanker` lost its
  anchor-wave input (8 points; it has no callers in the app); its top band now needs Hurst at or below about 0.27.
- **Caveats**:
  - Twelve strategies are gone from the catalogue; anyone who imported one keeps their copy, which now names the
    removed part and never fires on it.
  - The setup graders' results can no longer be reproduced on this build.
  - Not seen or heard in the app. The accessibility-lead agent is not registered here.
- **Tests**: CipherBTimingTests and DivergenceConfirmLagTests deleted (they tested removed parts); the metadata,
  look, stock-visibility, signal-rule, adaptive-threshold, lab-profile, tint and left-out-parts tests rewritten on what
  remains; `CipherBWaveTrendParityTests` keeps every kept output's pre-removal hash (none re-pinned) and drops the
  removed ones; `CipherBMcbFormulaTests` checks the old mode values change nothing; new
  `SetupGraderTests.The_graders_refuse_to_run_without_Cipher_Bs_removed_parts`. 13 sabotages, 12 red; the green one
  (accepting `old` at the second check) cannot be reached, as `old` throws first. Whole suite 9763: 9762 green, and the
  scan guard's one red (the new test used the system temp path) fixed and both classes re-run green. README 9758.

### Cipher B's Wave Gap and Money Flow: read as waves by default, as histograms by choice; the picture is the same (2026-10-30, branch cipher-settings-text, NOT pushed, NOT seen or heard)

- **Why**: Cody: "The wt histogram jumps to os/ob levels even though it doesn't have them ... histograms shouldn't have
  over bought/over sold levels, so we need to pick one display type", then "the money flow I think does show the waves
  plus the red/green bar, so perhaps a toggle here would be best, with the waves being the default view ... it's a
  yellow filled area so this should read correctly as well. whatever scale thee two are set to should be what is read
  out". Both were Histograms pitched by side: two fixed notes (WT Histogram 360/140 Hz, Money Flow 300/100 Hz) that
  leap more than an octave at every zero cross, the low ones below the blue waves' whole 200-1000 Hz scale. That leap
  is the "jump" Cody heard; there were no levels behind it. Then, on the first build (which drew the histogram form as
  columns): "the toggle for the money flow shouldn't change what is visually shown, only what component type the
  screen reader sees and navigates on ... both should be on screen visually. hiding the money flow should hide both
  the waves and histogram".
- **What**: each has a "Read as" setting in Properties (Wave / Histogram), Wave by default. It changes only what the
  screen reader is given: the type word, how it is navigated, the sound and the speech. **The picture is the same in
  both forms**: the filled area (drawn pixel for pixel as before; unfilled, its line), and for Money Flow the red/green
  Money Flow Bar along the bottom.
  - **Wave**: sounded on the pane's own ±100 scale (Value pitch, like the blue waves), no levels, constant loudness,
    square above zero and triangle or sawtooth below.
  - **Histogram**: pitched by side, with the bar grit from the commit below.
  - **Hiding Money Flow hides the Money Flow Bar too** (hidden, switched off, undrawn or left out). Before, the bar
    stayed on screen with Money Flow hidden; it now goes with any band coloured by a component's sign.
  - Saved per chart (the workspace) and as the indicator's default (Save as Defaults). A chart saved before today
    opens as the wave.
- **Names and speech**: the WT Histogram is shown and spoken as "Wave Gap" (it is the fast wave less the slow one;
  Market Cipher B calls it "VWAP", which it is not). Its component name stays "WT Histogram", so saved charts,
  strategies and alerts that name it still find it. Money Flow says its signed number ("18.0", "-12.3"), as the Wave
  Gap does; no "buying pressure" or "selling pressure".
- **How**: a component's metadata can carry a second form (`IndicatorComponentMetadata.OtherForm`, `FormName`) that
  declares only its type, sound and speech. The user's choice is `ComponentConfig.UserDisplayType`. The factory builds
  the first form and takes only the type, sound and speech from the chosen one (`IndicatorModelFactory.BuildForm`,
  `TakeForm`), before restoring the saved state; the dialog's switch does the same on its copy (`ApplyForm`) and leaves
  the picture and the pitch multiplier as the user set them. A filled Line or Oscillator with `AreaBySign` is drawn by
  the directional area renderer the histogram area used; a Histogram with `AreaBySign` unfilled is drawn as its line.
  A zone band with a `SignComponentName` is drawn only while that component is (`DataLayer.SignShown`).
- **Caveats**:
  - Sound settings saved as defaults in one form (waveform, base frequency, noise) apply in the other form too.
  - The component's type word is still "oscillator" in the wave form, as for the blue waves; the dialog says "Wave".
  - The crosshair badge ranks a histogram's reading below a line's, so with both forms on screen the badge may lead
    with a different part in the histogram form; the chip colour is the same.
  - Not seen or heard in the app.
  - The accessibility-lead agent is not registered here.
- **Tests**:
  - `CipherBWaveFormTests`: the default is the wave, and the WT Histogram is named Wave Gap. The wave's pitch rises
    with its value and does not leap at zero, while the histogram keeps its two notes. A chart saved as histograms and
    histograms saved as the default both build histograms. The switch works both ways and keeps the user's colour,
    thickness and fill. Only the type, sound and speech fields differ between the forms (every serialised field
    compared). Both forms draw the same frame, filled and unfilled, loaded or switched; the wave draws the same pixels
    as the old histogram area. The Money Flow Bar is drawn, and is gone when Money Flow is hidden, switched off,
    undrawn or left out, in both forms.
  - `CipherBMoneyFlowSpeechTests`: the signed number for both parts.
  - The grit and pitch-by-side tests now build the histogram form (`ComponentForms`).
  - The knob sweep has a filled-wave cell, and names the factory as `UserDisplayType`'s reader.
  - `AreaFillEdgeTests`' sign-band fixture had its sign component hidden (as Money Flow was); it is now visible,
    since a hidden component's band is no longer drawn. README 9819.
  - 26 sabotages, 25 red. The green one (the histogram form not taking its speech template) is equivalent: both forms
    declare the same template.

### A bar's grit is its size against the biggest bar of its own kind in view (2026-10-30, branch cipher-settings-text, NOT pushed, NOT seen or heard)

- **What**: Cody: "keep the money flow and wt histogram sonified as a histogram, but do double check the grittiness
  as it's hard to hear". Measured first: grit (the sub-octave sawtooth that carries a bar's size, 0 to 0.30) was the
  bar's size against the PANE's peak, and Cipher B's pane is fixed at ±100 while its bars are far smaller. On
  Bitstamp's Bitcoin daily history the WT Histogram's median bar is 5.5, so grit 0.016 (nine in ten under 0.045), and
  Money Flow's 17.6, grit 0.053; on Coinbase's 15-minute bars 0.020 and 0.037. Nothing in view reached the top, so
  every bar was nearly a clean tone. Grit is now the bar's size against the biggest bar of the same component in view
  (`DefaultSonificationStrategy.VisibleBarPeak`), on playback and when arrowing: the biggest bar in view gets the full
  0.30, the median bar 0.07-0.14. Still a histogram: constant loudness, direction pitch, grit for size. Volume and
  MACD take the same rule; volume's pane already fit its bars, so it barely changes. A bar read straight from the
  candles (no series data) keeps the pane's peak.
- **Tests**: `CipherBBarGritTests` (Bitcoin daily and 15-minute, every 100-bar view, both bars, both paths: each bar's
  grit is 0.30 x its size over the view's biggest, the biggest is 0.30, the median at least 0.06). Five sabotages red:
  the pane's peak back, the arrowing path not passing its bar, the playback path not passing it, the peak taken over
  the whole history, the peak taken over half the view. 86 audio tests green. Interface change:
  `ISonificationStrategy.CreateAudioPoint` takes an optional `dataIndex`; three test doubles updated. Not heard in the
  app; the accessibility-lead agent is not registered here.

### Settings say what they do and their default, nothing else (2026-10-30, branch cipher-settings-text, NOT pushed, NOT seen or heard)

- **What**: Cody: "The indicator properties of cipher b general tab mentions market cipher b and functionality
  before....strip out that text. Settings are for the setting name and description and value, that's it. No MCB's
  default, just default". Every setting description the Properties dialog prints that named another product (Market
  Cipher A/B/SR, VuManChu, TradingView, MetaTrader, NinjaTrader), a trader, "the original", or what the indicator did
  before a date now says only what the setting does and its default: Cipher A (3), Cipher B (5), Cipher SR (2), Cipher
  C (2), DSS Bressert (3), DBSI-style (5). Cipher B's default Money Flow Formula and Buy Dot Rule, the value "MCB", are
  now "Standard"; a saved chart holding "MCB" is rewritten to "Standard" on restore (WorkspaceInitializer), and the
  calculation reads anything but Legacy / Gated as the default either way, so no number changes.
- **Not changed**: the indicators' own one-paragraph descriptions (the Add Indicator list, not Properties) still say
  what Cipher A and Cipher SR were measured against; code comments and the StrategyLab profile text are unchanged.
- **Tests**: `SettingTextNamesOnlyTheSettingTests`: no parameter description or string default of any indicator names
  another product, a trader, the original or a date; Cody's saved Cipher B restores "MCB" as "Standard", and "MCB",
  "Standard" and no value compute the same series (Legacy / Gated differ, so the check can see a difference). Five
  sabotages red: Cipher B's buy-dot text back, the "MCB" default back, the migration removed, DSS's TradingView text
  back, "MCB" computing differently. The accessibility-lead agent is not registered here (text only, no new UI).

### Cipher DBSI: the bear Q value from the hidden arm, a floor touch inside Q, and the bulls' deep-run flag (2026-10-29, merged and pushed, NOT seen or heard)

- **What**: three more rules from the search of the bars still wrong after the hidden arm (Cody: "yes port ztc and
  push"). (1) A bear Q bar is worth nothing while the hidden arm is down, and on a Q's first bar with V0 off it is
  worth the hidden arm itself instead of 4 less the latched 1. (2) Both sides: an off-run bar in a Q that was on the
  bar before too, with raw Stoch RSI touching its floor (0, up, 0), the hidden arm up and no Stoch RSI vote, gets +2.
  (3) Bulls only: after a deep run (one that crossed below −60) ends with the bull side's WT above 60, off-run and
  alive bars get +2; the flag carries through each later run that crosses and ends with WT above 60, and drops
  otherwise. All 15 such bull stretches on the sets were 2 short; the bear side has none wrong.
- **Measured** (the hidden arm model just below -> now): the ten veto sets 98.27% -> 98.48% exact pooled (36,391 ->
  36,469 of 37,032), each 96.0-99.5%, none worse; within 2 99.72% -> 99.73%, RMSE 0.28 -> 0.26. Misses: bulls 349 ->
  302, bears 292 -> 261. Every main and unseen set better or the same (the hourly discovery set +46, the hourly
  Bitcoin set +31). Never mined: Microsoft 15m 97.7% -> 97.9%, Bitstamp 5m 98.4% -> 98.5%, Bitstamp 1m the same
  (97.0%), Bitstamp daily 98.4% -> 98.7% (mirrored, shifted, scaled each +8 bars), hourly Bitcoin Heikin-Ashi 98.7% ->
  99.0%. The hourly weeks 98.3% (+1 bar). Twin charts: +192 bars, no cell worse. The twenty and the fifteen unchanged.
  Every term reads only this bar and earlier ones.
- **Caveat**: on the ten veto sets the flag's carry through later runs (3) adds nothing; its gains are on the hourly
  sets it was found on (+43, mostly one event) and 11 bars on hourly Bitcoin Heikin-Ashi, never mined.
- **Not taken**: the flag carried with a stricter start rule, or on off-run bars only (Bitcoin daily 3 and 4 bars worse).
- **Tests**: `DbsiCleanScores` and `DbsiCoverageCharts` regenerated from the study's `zt_c2.py` (the 35 charts' printed
  scores did not change, so the golden pins stay). One coverage window added, gold 1980-1981: a bear Q bar with the
  hidden arm down, the hidden arm on a bear Q's first bar with V0 off, and the flag read on the bar a run ends; the
  generator refuses to write unless breaking each of those changes a score. 17 new C# sabotages, all red: the bear Q
  value without its hidden-arm gate, without the hidden-arm branch, without its Q-before or V0 condition, from 3; the
  floor touch dropped, without its Stoch RSI-vote condition, outside Q; the flag's off-run and alive +2s dropped, its
  end at 55, its carry dropped, its start at 70 or never checked, read on the bar before or on the run's last bar, and
  on the bear side too. The 21 earlier Python sabotages the 35 charts miss were re-made on this model and are still caught. Two of the floor touch's
  conditions cannot change a score: Q on the bar before (a Q cannot start on a close-up bar with raw Stoch RSI at 0)
  and raw Stoch RSI at 0 two bars back (Q is never on while it fell over 2 bars); the hidden-arm condition needs raw
  Stoch RSI equal 3 bars and 1 bar back, which no history has. One test more (the new window): README count updated.
  `DbsiStyleProvider`'s measured figures updated.
- **Not done**: not seen or heard in the app; the accessibility-lead agent is not registered here; the Start-grid
  emulator not moved to this model.

### Cipher DBSI: the hidden arm on off-run bars, and the bears' EMA state (2026-10-29, merged and pushed, NOT seen or heard)

- **What**: off-run bars with V0 on (close above the close 2 bars back) and raw Stoch RSI not falling over 2 bars now
  score the votes, the streak and the hidden arm (the ARM a run starting on that bar would carry; the Q value if that
  is larger), and nothing else. A bull Q bar is worth the hidden arm + 1 (less the latched 1), and nothing while the
  hidden arm is down. The pinned-top (+2/+3) and the bar-after-Q terms need the hidden arm up. A new floor touch
  outside Q (raw Stoch RSI 0, up, 0) adds the hidden arm, and 2 more without the Stoch RSI vote. Bears: the cross
  term's second part, the near-zero term and both wide-gap terms count only while EMA 5 is below EMA 11, and an alive
  bar within 2 bars of that EMA cross, ARM kept and no cross term, gets at least 2. This replaced about ten fitted
  terms: the V0 bonus (1-3 bars after V0 turned on), the fresh-V0 +3, the missed one-bar Q, two bull V0 +2s, the
  bull K > 61 Q discount, the bear pinned-with-V0 −3, and the bears' Q turn (+2 for a Q bar with WT turning up near
  the zero line).
- **How it was found**: a search of the bars still wrong after the EMA cross, under Cody's "keep searching for that
  last rule": the off-run misses lined up with the hidden arm, which the alive bars already used.
- **Measured** (the EMA cross model just below -> now): the ten veto sets 96.9% -> 98.3% exact pooled
  (35,866 -> 36,391 of 37,032), each 96.0-99.2% (Apple daily lowest), none worse; within 2 99.17% -> 99.72%, RMSE
  0.42 -> 0.28. Misses: bulls 602 -> 349 (1.9%), bears 564 -> 292 (1.6%). Every main and unseen set better or the
  same. Never mined: Microsoft 15m 96.2% -> 97.7%, Bitstamp 5m 97.0% -> 98.4%, Bitstamp 1m 95.1% -> 97.0%, Bitstamp
  daily 96.9% -> 98.4% (mirrored 95.9 -> 97.6, shifted 96.9 -> 98.4, scaled 96.8 -> 98.3), hourly Bitcoin
  Heikin-Ashi 97.8% -> 98.7%. The hourly weeks 96.8% -> 98.3%. Twin charts: all 56 side/twin cells better or the
  same, about 1,300 bars. The twenty 86.7% -> 88.1% exact, within 2 93.9% -> 94.1% (17+ printed 79 -> 81 of 84, but
  false 4 -> 5); the fifteen 83.9% -> 85.0%, within 2 90.0% -> 90.1% (17+ still 46 of 48, false 3 -> 2). Side in
  control: the twenty 418 -> 419 of 422, the fifteen 256 -> 258 of 262. Every term reads only this bar and earlier ones.
- **Not taken**: the bear Q value as the hidden arm within 4 bars of a run (Microsoft 15m 1 bar worse, fitted).
- **Tests**: `DbsiCleanScores`, `DbsiDigitisedCharts` and `DbsiCoverageCharts` regenerated from the study's
  `zq_s2.py`. One coverage window added, gold 1967-1970: the only bull Q bar with the hidden arm down on gold, the
  Dow, the S&P or Bitcoin hourly, apart from one Bitcoin hourly bar. `DbsiStyleGoldenTests` pins moved to the figures
  above. The generator also refuses to write unless the override's `>=` as `>`, the pinned top without the arm, the
  EMA state dropped from the wide-gap terms, the 2-bar term without the kept ARM, and the Q bar counted while the
  arm is down each change a score. 40 C# sabotages: the 22 of the EMA cross re-made on this model and 18 new ones, 39 red. The new ones: the bull
  Q value from 4 instead of the hidden arm, or counted while the arm is down; the pinned top, the bar after Q and the
  floor touch without the arm; the floor touch dropped, without its Stoch RSI-vote +2, or inside the bar after Q; the
  override with `>` for `>=`, without the Q value, without V0; the EMA state dropped from the cross term, the near-zero
  term and each wide-gap term; the 2-bar term at 1 or 3 bars, without the kept ARM, with a cross term. The green one
  is still `>=` for `>` in the cross test (equivalent, as before). One test more (the new window): README count updated. `DbsiStyleProvider`'s measured figures updated.
- **Not done**: not seen or heard in the app; the accessibility-lead agent is not registered here. The
  Start-grid emulator is not yet updated to this model, so the S&P 500 Start-grid figure is the EMA cross model's.

### Cipher DBSI: the bears' EMA(5)/EMA(11) cross, the input the bear rules were standing in for (2026-10-29, merged and pushed, NOT seen or heard)

- **What**: a bearish cross, EMA(close, 5) crossing below EMA(close, 11) (on the bears' upside-down candles, the
  side's EMA 5 crossing above its EMA 11), now drives three bear rules. (1) On a dead bar it sets the +2 flag, held
  until raw Stoch RSI falls over 2 bars; this replaces the near-zero flag (side WT climbing 5 or more into −15 to 10,
  not on a flat-hold bar). (2) An off-run bar on the cross bar gets +2. (3) The zero-line bonus (+6, or +4 on top of
  a cross term) goes to a run whose own WT cross bar is an EMA cross bar. This replaces five fitted triggers (two
  jump conditions and three cross shapes) and the continuation zero-cross term, which turned out to be proxies for it.
  Bulls are unchanged: the same search on the bull side found nothing.
- **How it was found**: Cody: "could it not be in the price data we already have? ... ema's, oscillators". The
  study scanned 705 price features on the bear dead bars DBSI scores 2 higher than we do, then moving-average crosses
  of every length pair. EMA 5/11 flagged 224 of 225 of those bars, with 1 false. Cody: "merge the best new findings
  into the c# model and keep searching for that last rule".
- **Measured** (the Q value model just below -> now): the ten veto sets 96.0% -> 96.9% exact pooled
  (35,535 -> 35,866 of 37,032), each 95.5-98.1%, none worse; within 2 98.75% -> 99.17%, RMSE 0.57 -> 0.42. Bear
  misses 895 -> 564; bull 602, unchanged. The main and unseen sets: none worse either (+660 bars over all of them).
  Never mined: Microsoft 15m 94.7% -> 96.2%, Bitstamp 5m 95.6% -> 97.0%, Bitstamp 1m 94.0% -> 95.1%, Bitstamp daily
  96.0% -> 96.9% (mirrored 95.3 -> 95.9, shifted 96.0 -> 96.9, scaled 95.9 -> 96.8), hourly Bitcoin Heikin-Ashi
  97.0% -> 97.8%. The hourly weeks 96.4% -> 96.8%. Twin charts: every bear side better, the mirrors too (about 970
  bars). The twenty 85.5% -> 86.7% exact (17+ printed 77 -> 79 of 84, false still 4); the fifteen 83.7% -> 83.9%
  (17+ 45 -> 46 of 48, false still 3). The S&P 500 Start grid, never fitted on: the emulator matches 93.2% of all
  Start ages (was 91.0%) and 99.3% of the good-bar ages (was 99.1%).
- **Not taken**: a fourth rule, +2 on an alive bar on the cross bar with the WT gap at most 10.5 and Stoch RSI D at
  most 63. It gains 83 more but makes the twenty 1 bar worse, and its thresholds are fitted. Holding the flag on
  alive bars, merging it with the wide-gap term, and reading the cross a bar early or late were all worse.
- **Tests**: `DbsiCleanScores`, `DbsiDigitisedCharts` and `DbsiCoverageCharts` regenerated from the study's
  `zj_nocont.py`; `DbsiStyleGoldenTests` pins moved to the figures above. The coverage windows are the same seven.
  The generator now also refuses to write unless EMA 6 or 12, dropping the off-run +2, and reading the zero-line
  bonus a bar early each change a score on them; the earlier sabotages, re-made on the new model, are still caught.
  22 C# sabotages: the twelve Q-value ones and ten new ones, 21 red. The new ones: EMA 6 and 12, the cross without
  the bar before, the flag only when the step holds, the flag off on the break bar, the flag never cleared, no off-run
  +2, the zero-line bonus a bar early and also on the bar's own cross. The green one, `>=` for `>` in the cross test,
  is equivalent: the gap is exactly 0 only on the first bar, inside the warm-up. `DbsiStyleProvider`'s measured
  figures updated, and its "no missing input" sentence corrected. No new tests; README count unchanged.
- **Not done**: not merged; not seen or heard in the app; the accessibility-lead agent is not registered here.
  Not re-checked: whether the Bitcoin bear run of 2026-10-21 to 25 (the Q value's caveat) moved.

### Cipher DBSI: the Q value, worked out from the Start grid (2026-10-29, merged and pushed, NOT seen or heard)

- **What**: what an off "Q" bar scores, and what a run that crosses straight out of Q inherits. The Q bar's worth is
  4, less 1 when the slow stochastic plus its value a bar before, latched where the Q episode began, is over 18
  (bulls) or over 80 (bears). Bulls also lose 1 when the 81-bar K is over 61 and rose no more than 6.25. This
  replaces the bulls' "the larger of the two over 18" and the bears' "K at the start over 41". A bull Q episode that
  starts right after a run of 1 or 2 bars, which itself started right after a Q bar, keeps the earlier Q's latched
  value. Also the bear near-zero cross bound moves from 6 to 5.6.
- **How it was found**: the 2026-10-28 reopening (Cody: a fresh look, all three pieces approved). A Start
  emulator (the study's `start_dbsi.py`: indicators over the whole history, the counting loops from the Start) now
  agrees with Market Cipher DBSI's own numbers at 98.4-99% of the Start ages swept on the Dow and Bitcoin. Where it
  disagreed, the age showed which earlier bar the number depends on, and that pointed at the Q bar.
- **Measured** (clean v5 -> now): the ten veto sets 95.4% -> 96.0% exact pooled (35,326 -> 35,535 of 37,032;
  bull misses 684 -> 602, bear 1,022 -> 895), each 94.5-97.7%, none worse. The hourly weeks 96.1% -> 96.4%. Never
  mined: Microsoft 15m 94.2% -> 94.7%, Bitstamp 5m 95.3% -> 95.6%, Bitstamp 1m 94.0% (same); Bitstamp daily
  95.5% -> 96.0% (mirrored 94.9 -> 95.3, shifted 95.5 -> 96.0, scaled 95.4 -> 95.9); hourly Bitcoin Heikin-Ashi
  96.7% -> 97.0%. The twenty digitised charts 84.8% -> 85.5% exact, false 17+ 5 -> 4; the fifteen unchanged (83.7%).
  The Start grids: on Bitcoin the emulator agrees at 171 more bar-and-Start pairs (96.30% -> 96.72% of all); on the
  Dow, unchanged.
- **Thresholds are sharp**: bull 17 or 19 instead of 18 and bear 79 or 84 instead of 80 each lose bars. The carry
  through 0, 1 or 3 bars, carrying on bears, and carrying without the Q bar before the run each lose bars too
  (dropping that last condition: 11 fewer). The near-zero bound from 5.5 to 5.7 scores the same; 5.6 was taken.
- **Caveat**: one Bitcoin bear run, 2026-10-21 to 25, gets worse. It is in four harvests of one feed and was already
  1 short; it is now 2 short. On those harvests within 1 drops from 99.0% to 95.1% (Bitstamp daily from TradingView)
  and from 99.1% to 95.7% (Coinbase daily). Either the vendor gives Q a 5 there, or it has a +1 we lack.
- **Tests**: `DbsiCleanScores` regenerated (7 charts change), `DbsiDigitisedCharts`, `DbsiStyleGoldenTests`
  (twenty 0.848 -> 0.855, false 17+ 5 -> 4). Five new coverage windows in `DbsiCoverageCharts`, of real candles where the
  new rules fire and the 35 charts do not reach them: Dow 1967 and gold 1949 (carry 0, 1, 3), Dow 1953 and hourly
  Bitcoin Heikin-Ashi 2026 (near-zero 5 and 6), Dow 1904 (carry without a Q bar before the run). 12 C# sabotages,
  all red: bear 79 and 84, bull 17 and 19, the sum as a maximum, carry 0/1/3, carry on bears, carry without the
  Q bar before, near-zero 5 and 6. `DbsiStyleProvider`'s measured figures updated. The old "bears about twice as
  often as bulls" was wrong: even on v5 it was 1.5 times. README 9797.
- **Not done**: not merged; not seen or heard in the app; the accessibility-lead agent is not registered here.
  The study files (`dbsi8/clean_dbsi_qsC.py`, the emulator) stay in the private study.

### Cycle Position: Cipher C's per-asset rules, Start date and yearly dates, as switches (2026-10-28)

- **What**: the last of the Cipher C pieces Cody approved (CYCLE_POSITION_DESIGN section 9.10). Four check boxes in
  Cycle Position's settings, all off by default, so nothing changes until one is turned on. "Asset's own confirmation
  rules": on a symbol Cipher C tunes, the daily lows confirm by its rules for it (the swing-high break on gold, silver,
  the miners and the dollar; dropping a held low on any undercut before it is due on the Dow, the dollar, silver, Ether
  and Bitcoin; the symbol's own stochastic lengths). "Faster confirmation": the swing-high break on any symbol. "Count
  from the asset's start date": no low before Cipher C's Start date, the Start held as a confirmed low. "Yearly lows
  from the asset's dates": the symbol's yearly date table, then from the price after the newest.
- **Measured**, each alone on the captured charts: the asset's rules bring the lows much closer to the original's
  (daily F1 .907 -> .968 building, .950 -> .969 blind; 4-hour .824 -> .953) but picking the real low is mixed (building
  daily 90.3 -> 90.6%, blind 88.7 -> 88.3%; blind weekly 85.8 -> 84.9%). Faster confirmation is worse on both. The Start
  date and yearly dates change almost nothing on these scores.
- **Why off by default**: none is clearly better at the real low, and the rules would change the lows the "Low is in"
  models were fitted on. A TODO question for Cody (it would need the models refitted).
- **Caveats**: not seen or heard in the app; the accessibility-lead agent is not registered here. Holding the Start as
  confirmed changes nothing on Bitcoin's own dates, so the test moves the bars to a start where it does. Test
  `Cipher_Cs_asset_rules_and_dates_are_off_until_switched_on`; 15 sabotages, 14 red (the green one set a maximum on a
  check box, which nothing reads). README 9792.

### Cycle Position: a mark at "very high", and lines for strategies and alerts (2026-10-28)

- **What**: the "alert or mark at very high" and "backtest hooks" Cody approved (CYCLE_POSITION_DESIGN section 9.9).
  A green diamond under the first bar on which a low's daily "Low is in" reading reaches very high, a blue one for the
  weekly reading, once a low ("Low is in: very high, 97%, for the low of 59,100.00."); switch "Very high marks", on by
  default. Six lines, off, undrawn and muted by default so the chart is unchanged: the daily, weekly and yearly reading
  and Low expected in percent, and a daily and weekly "very high call" (the share on the marked bar, 0 elsewhere).
  Alerts read them though they are not drawn ("Very high call is above 0" alerts once a low); strategies get the marks
  as events and the lines as lines. 31 daily charts: 7,868 daily and 2,332 weekly marks, 270 bars with both.
- **Why call lines**: an alert on the share crossing 95 would fire 8,424 times for 7,868 daily calls and 4,209 for 2,332
  weekly ones, and miss calls made on a low's first reading.
- **Caveats**: not seen or heard in the app; the accessibility-lead agent is not registered here. The Low expected line
  uses the learned daily window, so older bars can change it (listed in the prepend guard beside the confirmations).
  The strategy catalog reads check-box marks (Cycle Position's and Cipher C's confirmations) as lines, not events: a
  TODO question. Test `The_very_high_marks_and_lines_are_the_first_very_high_bar_of_each_low`; 20 sabotages red (the
  Low expected line's speech was green until tested). README 9791.

### Cycle Position: "weekly lows often come without it" on charts where they do (2026-10-28)

- **What**: the "less reliable on this chart" note Cody approved, built where it was measured to hold (CYCLE_POSITION_DESIGN
  section 9.8). Where under 60% of at least 5 weekly lows confirmed so far had a "very high" weekly reading by their
  confirmation, the weekly Low is in reading adds "on this chart weekly lows often come without it: 34 of 74 so far had
  a very high reading by their confirmation". The table's weekly cell adds ", often missed here". On 31 daily charts the
  next weekly lows on charts so flagged had one 58% of the time, against 75% (halves 68/76, 48/74). It is said on about
  6% of read days, mostly Ether, SILJ, GDXJ, Dogecoin and Marathon.
- **Not built, by measurement**: a note about wrong calls. A chart's record of first "very high" calls told nothing
  about its next calls: daily 91% against 97% right on only 90 calls, weekly 96 against 95%, and the last 10 calls told
  nothing. The daily reading catches 99% of daily lows everywhere, so it gets no note either.
- **Caveats**: 60% and 5 were chosen on these charts; that the record carries over is measured on daily charts only.
  Not seen or heard in the app; the accessibility-lead agent is not registered here.
- **Tests**: `The_weekly_record_counts_lows_called_very_high_by_their_confirmation` (made-up arrays, edges, and
  Bitcoin low by low with the words and cell on every bar). Two new causal outputs are in the causality test.
  14 sabotages, all red. README 9790.

### Cycle Position: the half-cycle low (2026-10-28)

- **What**: the half-cycle low, from the pieces Cody approved (CYCLE_POSITION_DESIGN section 9.7). In each daily cycle
  it is the first low from 0.3 to 0.7 of the usual cycle in that is above the cycle's low and the lowest for an eighth of
  the shortest cycle each side. It is marked with a small up triangle on its bar, found that many bars later. A switch
  "Half-cycle lows" (on) hides the marks. Found in 69% of daily cycles on 31 daily charts.
- **Said**: from when it is found until the next daily low, with how much of the rise before it it gave back and how
  often cycles that gave back as much failed: under 50% 18%, 50-70% 31%, 70-80% 43%, 80%+ 49%, against 30% of all.
  On the two halves of the charts these were 18/17, 31/30, 43/43 and 44-51/49-56%. It is in the state words, the
  arrowed daily window, a table row "Half-cycle low" and the mark's speech. The share is left out once the cycle has
  failed.
- **Not built, by measurement**: the planned projection of the next daily low from it. At the learned window's span it
  held 59.5% against 58.4%, and 59.4% against 60.0%, so it added nothing to the window.
- **Caveats**: the failure rates are fitted on these 31 charts, not held out (the halves agree within 6 points). Daily
  tier only. Not seen or heard in the app; the accessibility-lead agent is not registered here.
- **Tests**: `The_half_cycle_low_is_found_as_defined_and_said_with_its_depth` (by hand, plus one-bar edge cases),
  `The_half_cycle_low_is_said_as_shown`, `The_half_cycle_lows_switch_hides_only_their_marks`. The components test is
  now "Cipher C's, then its own". 24 sabotages, 23 red (the green one changes nothing). Recomputed independently on 31
  charts: no difference. README 9789.

### Cycle Position: the next window's dates, the top so far, a failed cycle's warning (2026-10-28)

- **What**: three more of the pieces Cody approved, CYCLE_POSITION_DESIGN sections 9.4-9.6.
- **Next window's dates**: each tier's live window is said with its dates on the last bar ("the next
  daily low's window opens November 3 2026 and closes November 14 2026"; "the weekly window is open,
  to July 16 2026"; "the daily window closed June 11 2026"), in the state words and the arrowed
  window, and in a new table row "Next low". Past the last bar a daily chart's dates are Cipher C's
  future clock's (trading days, its holidays); other charts repeat their last week's bar times a week
  on (`CyclePositionProvider.FutureStamps`).
- **Top so far**: after a confirmed daily low, the highest high since, its day, whether it came before
  the cycle's middle (from half the usual cycle on), how often cycles at this stage with the top this
  early or late went on to fail (35/18%, 26/14%, 16/10%, said 35% → failed 35.1% on the same charts),
  and the share of past cycles whose top had come by this day (said 75% → came 74.9%). A table row
  "Top so far", and the chart's tops by side in the bar detail ("40 left, 2 mid, 61 right").
- **Failed cycle**: "daily cycle failed, so a weekly low is likely next: after a failed daily cycle the
  next daily low was a weekly low 56% of the time, against 26% after one that held" (31 daily charts,
  3,182 failures; 4-hour 52/25, weekly 50/28), in the state words and the failure mark's speech, until
  the next daily low is confirmed.
- **Caveats**: the failure rates and the weekly-next shares are fitted on these charts, not held out.
  Intraday dates past the last bar do not read holidays or clock changes, so they can be a bar out.
  The "top had come" share under 20% is rare and not calibrated. Not seen or heard in the app; the
  accessibility-lead agent is not registered here.
- **Tests**: `The_next_windows_dates_are_the_bars_its_band_spans`,
  `Past_the_last_bar_an_intraday_chart_keeps_its_sessions`,
  `The_top_so_far_is_worked_from_the_bars_and_past_cycles`, `A_top_exactly_halfway_is_counted_as_mid`,
  `The_top_and_a_failed_cycle_are_said_as_shown`; the causality test covers the new values. 31
  sabotages, 30 red (the green one changes nothing). The app's values were recomputed independently
  on all 31 daily charts: no difference. README 9786.

### Cycle Position: "Low is in" off daily charts, the weekly reading said once, the confluence grade (2026-10-28)

- **What**: the first three of the pieces Cody approved ("i approve all of your suggestions, fixes
  and nice to haves"), CYCLE_POSITION_DESIGN section 9.
- **Off daily charts**: "Low is in" (daily and weekly) and "Low expected" are now read on 4-hour and
  weekly charts too, said in bars. They count in bars, so the daily models were scored, without
  refitting, on 15 4-hour and 15 weekly charts: the first "very high" right 96.5% and 97.1% (daily
  96.6%), a median 5-6 bars early; every band holds; Low expected about as weak (AUC .76). The yearly
  reading (calendar days) is read on weekly charts (121 of 123 right) but not on 4-hour ones (19 of 26).
- **Said once**: when the weekly reading reads the same low as the daily one, the state words say "the
  same low as the daily reading" and leave out the distance above the low and the confirmations, as
  the daily reading has just said them. Arrowing the weekly window says it whole.
- **Confluence**: the original's LOW/MID/HIGH grade, while a daily low is held, with its two reasons:
  a "Confluence" row in the table, the state words and the held low's speech ("confluence: mid, the
  Awesome Oscillator above zero, the close not above this month's VWAP"; "no volume, so no VWAP").
  Held lows graded high were confirmed 92% of days, low 74%; inside each "Low is in" band it adds
  nothing, so it is context, not evidence. Cipher C's grade is split into its two points
  (`CipherCProvider.GradeParts`), unchanged.
- **Caveats**: the 4-hour and weekly captures are the same 15 assets the daily fit partly used, on
  other bars. Not seen or heard in the app.
- **Tests**: `Off_a_daily_chart_they_are_read_in_bars` replaces `Off_a_daily_chart_neither_is_read`
  (the same candles stamped 4-hourly read the same chances as stamped daily, said in bars; no yearly
  on 4-hour, yearly on weekly); `A_weekly_reading_of_the_daily_low_is_not_said_twice`;
  `The_confluence_grade_is_the_originals_while_a_low_is_held`. Twelve sabotages red (one green at
  first, a gate written twice; made one gate).

### Cycle Position: weekly and yearly "low is in" (2026-10-28)

- **What**: Cody asked to "fit the weekly and yearly bottoms as well". On daily charts, the "Low is
  in" row's weekly and yearly columns now say how likely the weekly and the yearly low being made is
  to become one, in the same five words. The bar's state words say it too ("weekly low is in: high,
  88%, for the low of 59,100.00; about 62 days to its confirmation; ..."), and so does arrowing the
  weekly or yearly window. The yearly reading names no confirmations.
- **Which low**: weekly, the held daily low that would become the weekly low (past the gap and under
  the drawn candidate, or inside the gap and under the newest weekly low), else the drawn candidate
  (`CycleLowConfidence.WeeklySubject`). Yearly, the earliest low still standing that is the lowest
  for 0.15 of a cycle before it, from 0.8 of a cycle after the newest yearly low, as Cycle Position
  finds its yearly lows (`YearlySubject`), counted in calendar days.
- **The models** (generated into `CycleLowConfidence.Tiers.Model.cs`): weekly, the daily reading's
  12 inputs on its own low and a clock of a cycle and a half, 200 trees of depth 3. Yearly, four
  inputs (the touch chance, the rise, how far price came back, the time held), 100 trees of depth 2.
  Fitted on Cycle Position's own lows on 30 daily charts; Bitcoin held out.
- **Results**, every chart scored unseen (CYCLE_POSITION_DESIGN section 8.5): the first weekly
  "very high" is right 94.6% (2,108 of 2,229), catches 72% of weekly lows, a median 12 days early;
  yearly 94.5% (274 of 290), 84%, about 40 days early. Daily, for comparison, 96.6% and 99% across
  the same 31 charts. Bitcoin: weekly 15 of 15 since 2020, yearly 3 of 3.
- **Caveats**: weak on Marathon (weekly "very high" days 71% real), Gold (76 of 163 weekly lows
  caught), Silver and US oil (yearly 8 of 23 and 15 of 43 caught). Weekly lows confirmed fast are
  often missed: the weekly low is confirmed when the next daily low holds above it. When the weekly
  reading reads the same low as the daily one, the confirmations are said twice. Yearly lows are few
  (328). On the 31 captured charts, 58 of 311,422 days differ from the study by a last-digit
  difference in the touch chance that falls on a tree split. Daily charts only.
- **Exactness**: averages are summed in numpy's pairwise order. Bitcoin's close on 2012-06-26 is
  exactly its 10-day average, so the order decides that bar's side.
- **Tests**: `CycleLowConfidenceTests` gains 6 tests and one hand-worked weekly case:
  - the study's own weekly and yearly chances to 1e-9 on Bitcoin, on exactly its 4,085 and 1,266
    days (`Fixtures/CycleLowConfidenceTiersReference.cs`);
  - all 40 weekly and 3 yearly lows read before they are confirmed;
  - the weekly and yearly lows read worked by hand, ties included;
  - calendar days, not hours;
  - no yearly cycle, no yearly reading;
  - speech = table.
- **Sabotages**: 15 of 17 red. The two green ones change nothing: no yearly tree splits held time
  above 0.91, so its cap of 2 never acts; on a daily chart the weekly tier always runs, so its gate
  cannot close. README 9779. Not seen or heard in the app; the accessibility-lead agent is not
  registered here.

### Cycle Position: "Low is in" and "Low expected", in words (2026-10-28)

- **Low is in**, daily charts, on each day a daily low is held: how likely Cycle Position is to
  confirm it, said as *likely to fail*, *low*, *medium*, *high* or *very high* with the share, then
  its reasons: about how many days to the confirmation, how far above the low (percent and average
  ranges), the chance at random of going back to it first, and which confirmations are on (a higher
  low's high broken, closes above the 10-day average, the stochastic over its trigger, the SAR under
  price, a close above the 10 before it). In the table ("Low is in" row), the bar's state words and
  the held low's speech. Small boosted trees on 12 inputs, the score mapped to a share
  (`CycleLowConfidence`, model generated into `CycleLowConfidence.Model.cs`). Fitted on 30 daily
  charts' Cycle Position lows; Bitcoin held out.
- **Low expected**, daily charts, each day after a confirmed low: the chance of a new daily low in
  the next L/6 days (Bitcoin 8), *unlikely*, *possible* or *likely*. A fitted logistic on 19 inputs.
- **Results** (CYCLE_POSITION_DESIGN section 8): fit on 15 charts, scored on the other 15, the words
  hold (very high 98% confirmed, high 86%, medium 70%, low 50%, likely to fail 16%); the first "very
  high" is right 96%, catches 4,088 of 4,119 lows, a median 7 days before the confirmation. Bitcoin
  2020+: 95% right, 39 of 39, 9 days early, 2 false. Low expected: AUC .78-.79; "likely" right
  45-59%.
- **Caveats**: it forecasts Cycle Position's own confirmation, not a big turn; the share map was
  fitted on the training charts; a dropped low is not read (refitted without those days); Low
  expected is weak; daily charts only.
- **Research, no change**: Cipher B's big green dot sits within 2 bars of 21% of daily, 30% of
  weekly and 47% of yearly lows (chance 7%), but lows with a dot held less often (58% against 64%).
- **Tests**: `CycleLowConfidenceTests` (7 tests, 25 cases): the study's own numbers to 1e-9 on
  Bitcoin (`Fixtures/CycleLowConfidenceReference.cs`), the bands, the speech matching the table,
  daily only; the causality test names both chances. Thirteen sabotages red. Not seen or heard in the
  app; the accessibility-lead agent is not registered here.

### Cycle Position: weekly lows by time (2026-10-28, Cody's option B)

- **The rule** (`CipherCEngine.WeeklyByTime`, Cycle Position only; Cipher C is unchanged). The
  weekly low is the lowest daily low at least a gap after the newest weekly low. It becomes one when
  the next daily low is not under it. A daily low under the newest weekly low inside the gap takes
  its place, and the old one is cancelled. The gap is half the shortest weekly cycle: the symbol's
  own, else 3 shortest daily cycles.
- **Real weekly low** (no lower bar between the tops either side), count rule → time rule:
  - building dailies 80.7 → 85.9%;
  - blind dailies 81.7 → 85.8%;
  - weekly charts 77.3 → 82.4%;
  - 4-hour charts 78.2 → 84.1%.

  The original scores 70-79%.
- **Agreement with the original's weekly lows drops:**
  - building dailies .725 → .626;
  - blind dailies .869 → .692;
  - untuned blind .928 → .716, where the count rule was the original's own.
- **A weekly cycle now holds 1 to 8 daily cycles,** mostly 2 to 4 (was 1 to 3).
- **The spoken nest and the table** count from the newest weekly low. A drawn candidate is said as
  "a new weekly low drawn but not yet confirmed" (table "new drawn"). Previously it ran from the
  candidate and never passed 2 (fixed).
- The learned weekly window still holds about 61%.
- **Tests:** the "one or two" test is replaced by "counts on from one" (Bitcoin: at least 4, weekly
  lows at least 77 days apart). New hand-worked `WeeklyByTime` test. CyclePosition 11 → 12. Seven
  sabotages red. The weekly window's prepend exemption in `IndicatorCausalityTests` is removed: the
  guard reported it no longer needed (its count now passes the prepend check on the probe series).
- Caveats:
  - the gap's half was chosen from a simulation on the same captures, not fitted further;
  - the blind captures were scored again;
  - not seen or heard in the app;
  - the accessibility-lead review agent is not registered here, so it was not run.

### Cycle Position: scored on the real low; the weekly rule studied (2026-10-28, docs only)

- **Real low by price** (no bar lower between the tops either side). Daily: the original 87-90%,
  Cycle Position 89-90%, Cipher C 88-91%. Weekly: 78%, 81-82%, 78-79%. None of the three is better
  than the others at this.
- **Tops:** where both daily lows agree, the same top bar 99.8-100%. The same letter 92% building,
  84% blind. The original's own letters follow the midpoint rule only 82% on untuned charts.
- **The original's weekly lows** fall in its own weekly table 25% of the time. On most assets it
  draws 2 or 3 daily cycles per weekly cycle; only the metals draw 4 to 6.
- **A weekly rule by time** picks the real weekly low more often (82-86% against 74-77%) and lets
  the count vary (1-7). It agrees with the original's weekly lows less. Not built; Cody's call.
- **Corrected:** CYCLE_POSITION_DESIGN said a weekly cycle is always two daily cycles; it is one to
  three. The spoken nest still says only 1 or 2 (defect, in TODO).
- Caveat: the blind captures (cap17) were scored again here; nothing was fitted on them.

### A new indicator, Cycle Position: where the chart is in its cycles (2026-10-28)

- **What it is** (`docs/CYCLE_POSITION_DESIGN.md`): a separate indicator, Cycles tab, drawn with
  all of Cipher C's marks. Cipher C is unchanged and stays the faithful copy of the original.
- **The design:**
  - One daily rule for every asset (the original's default). The one number per asset is the
    shortest daily cycle: the symbol's own, else 32, or set by hand.
  - Weekly lows picked from the daily lows, counted from the newest weekly low.
  - Yearly lows found from the price.
  - Each tier's window is learned from the chart's own past cycles (20th to 80th percentile, once 6
    daily or 4 weekly or yearly cycles are known).
  - EARLY / DUE / LATE under the held daily low, in place of LOW/MID/HIGH.
  - Each bar says what share of past cycles still unconfirmed that far in had already made their
    low.
  - A running hit rate: each cycle scored against the window it began with.
  - Which daily cycle of the weekly one this is.
  - Cancelled lows and confirmation marks on by default.
- **Results** (walk-forward on its own lows; blind = 18 held-out daily captures never looked at
  before, scored once after the rules froze):
  - The daily window held 60.3% of 8,638 cycles on 46 lab dailies, 60.8% blind. Every chart was
    50-68%. Cipher C's fixed windows held 27.6% and 31.0%.
  - **But the learned window is about twice as wide.** Narrowed to Cipher C's width, it holds 32-33%
    against Cipher C's 28-31%: no sharper per unit of width. The gain is that it says how often it
    holds, not that it holds more per day.
  - "Low had come by now": calibrated up to about 65% (said 50-59%, came 58%), overstated above
    (said 90%+, came 63-70%). Brier skill 0.27, blind 0.24.
  - LATE bars: the low had already happened 70%, blind 65%. EARLY: 7%.
  - Against the original's lows, blind, on its 12 untuned dailies: daily F1 .968, weekly .930
    (Cipher C .969, .934). On tuned assets it is lower (daily .79-.90), because the per-asset rules
    are left out on purpose. Yearly from the price: 28 of 57 within 30 days.
- **Wiring:** registered in the Blazor, WebHost and lab hosts; its own table (`ChartInfoTable`) and
  bar detail (`BarDetailService`).
- **Refactor:** `CipherCProvider` was split so the new provider can share its drawing (`Draw` with a
  `Plan`). Cipher C's output was checked byte-identical on the 39 captures before and after.
- **Tests:** 11 new `CyclePositionProviderTests`. They cover:
  - no repaint over six cuts of Bitcoin's history;
  - learning worked by hand;
  - hit rate, phase, nest and share properties;
  - the count's three sources;
  - Cipher C's components;
  - bars off a daily chart.

  `IndicatorCausalityTests` exempts its confirmations and windows from the prepend check (a learned
  window depends on the history before it). README 9730 -> 9746 (with the five yearly-from-price tests, not yet counted).
- **Sabotages:** twelve red. One survived at first (scoring against the window at a cycle's end);
  the hand-worked test was added for it, and for the percentile interpolation.
- **Caveats:**
  - The weekly cycle is always 2 daily cycles, by construction.
  - Most charts have too few yearly cycles to learn a yearly window.
  - The accessibility-lead review agent is not registered here; not run.
  - Local branch only: not pushed, not seen or heard in the app.

### Cipher C can find its yearly lows from the price (2026-10-28)

- **What changed** (CIPHER_C_DESIGN section 37): a new setting on the Cycles tab, **Yearly lows found
  from price**, off by default. The original's yearly lows are hand-entered dates that stop at its
  newest (December 2023 at the latest). With the switch on, a tuned asset keeps those dates and
  carries on after them. An untuned asset finds all of them.
- **The rule** (`CipherCEngine.YearlyFromPrice`):
  - A yearly low is a bar with no lower or equal low in the 0.15 of the yearly cycle before it, and
    no lower one in the 0.15 after it. Of equal lows, the first counts.
  - It is confirmed on the first bar 0.15 of a cycle after it, not before (219 days on a 4-year cycle).
  - The next search starts 0.8 of a cycle on.
  - The yellow band moves on from a low found after the dates.
  - A yearly range set by hand also finds them from the price, as before.
- **Fitted on the original's own 151 dates:** one step ahead, 41 of 64 within 30 days; held out,
  17 of 31 and 22 of 33.
- **What it adds today:** the Dow and the Nasdaq 2025-04-07, Oil 2023-05-01 and 2025-12-15, Marathon
  2026-02-05.
- **Behaviour change:** an untuned symbol now draws no yearly lows unless the switch is on, as the
  original. Before, the old detector drew them.
- **Tests:** five new `CipherCYearlyTests`. Changed tests:
  - The causality test runs the whole Bitcoin history with the switch on.
  - The yearly-on-weekly claim is now checked on lows that exist (it was vacuous).
  - CipherC tests 281 -> 286.
- **Sabotages:** ten red. Not moving a found low onto a weekly low stayed green: Bitcoin's found
  lows already sit on weekly lows.
- **Caveats:**
  - About 6 in 10 one step ahead: a guess at the original's next date, not a reading of it.
  - A found low is drawn 7 months after it on a 4-year cycle.
  - Local branch only: not pushed, not seen or heard in the app.

### Cipher C's bands past the last bar land where the original's do (2026-10-28)

- **What changed** (CIPHER_C_DESIGN section 36): a band whose end is past the last bar is now
  counted on the bars the chart would print, not every weekday at the last bar's time.
  `CipherCFutureClock` reads them from the chart's own bars, with no symbol table. It keeps each
  weekday's session time in UTC or US-Eastern with daylight saving: stocks open at 13:30 UTC in
  summer and 14:30 in winter, and futures-style sessions are stamped on Sunday evening. It also
  skips the US holidays the chart had no bar on the last time they fell on a weekday.
- **The far end** is the rounded start plus the window's length, rounded (the Dow's weekly
  216 + 88 = 304 days, was 303; Gold's and Silver's 216 + 59 = 275, was 274). The Nasdaq's weekly
  band is a day later at both ends than the Dow's (217-305 days), as the original draws it.
- **Result against the original's band ends past the last bar** (21 assets, four dates each),
  exact to the bar: daily 56 → 70 of 70, weekly 37 → 115 of 128, yearly 25 of 36. Ends dated
  in the past: daily 82 of 82 unchanged, weekly 15 → 16 of 16.
- **Tests:** six new `CipherCFutureClockTests`; the band tests' Dow, Gold and Silver ends move a
  day, and the Nasdaq is added. Seven sabotages red (section 36).
- **Caveats:**
  - 13 weekly misses are anchors that are not ours (the dollar, the metals, Oil, MicroStrategy),
    plus one EURUSD end a bar early.
  - Not modelled in the yearly misses: NYSE's unscheduled 9 January 2025 closure, and
    TradingView's holiday list seeming to stop after 2027 (2028 targets are 4-7 bars off).
    Coinbase, MicroStrategy and Riot are a bar late, unexplained.
  - Only US holidays. Off a daily chart the bands are in bars, unchecked.
  - Local branch only: not pushed, not seen or heard in the app.

### Cipher C's weekly tier starts on the original's Start bar; the app scored end to end (2026-10-28)

- **Scored end to end** (CIPHER_C_DESIGN section 35): the app's lows on the original's 39 captures
  (16 tuned daily, 11 weekly, 11 four-hour, plus Platinum untuned), against its final labels. The
  dates (previous entry) made every yearly low exact (daily charts 6 → 27 of 27, weekly 0 → 38 of
  38, 4-hour 2 → 10 of 10) and lifted the daily lows (F1 on daily charts .9441 → .9469, on weekly
  charts .9374 → .9715). They cost weekly lows (daily charts .9177 → .9074).
- **What changed:** the weekly tier now starts on the same Start bar as the daily count. The Start
  is the first weekly low the rule judges from, drawn nowhere, so the first daily low after it is
  no longer a weekly low by itself; the second is the candidate, as after any weekly low. On the
  metals a candidate made a weekly low may be any distance from the Start. Found from the
  original's first weekly low after its Start label: it is what this rule gives on all eleven tuned
  daily charts that have one (ours before: two).
- **Result:** weekly lows on daily charts .9074 → .9350 (539 of 580, extras 51 → 34; .9177 before
  the dates), on weekly charts .7755 → .8351, on 4-hour charts .7945 → .7990. Daily and yearly lows
  unchanged.
- **Tests:** two new (the weekly tier from the Start, drawn nowhere; the metals' Start far enough
  for a candidate only). The weekly-rule test passes the Start; the weekly band test also checks
  Bitcoin cut back a month at a time, as the full chart no longer ends on a candidate. Seven
  sabotages red: no seed (daily rule, metals), the seed confirming, the metals' Start never far
  enough or far enough for an undercut too, and the provider passing no seed (either rule).
- **Caveats:**
  - Coinbase is worse than before the dates on 4 hours (12 → 11 of 13) and weekly (2 → 1 of 2).
  - On a chart that starts after the Start date the first bar is the seed. Unchecked: no capture
    of the original starts after its Start date.
  - Not seen or heard in the app.

### Cipher C counts from the original's dates and draws its yearly lows and band from them (2026-10-28)

- **What changed (Cody, 2026-10-28: "add the original start date in the repo", and the yearly date
  table "yes as well").** The dates read off the original's charts are now in the presets
  (CIPHER_C_DESIGN section 34):
  - Each tuned asset's count starts on the original's own Start date, the first bar on or after it.
    A chart that ends before it has no daily count yet, as the original draws none before its Start.
  - A tuned asset's yearly lows are the original's 🅨 dates, each on the first bar on or after it,
    a date before the chart on its first bar. They stand alone, not added to the weekly or daily
    lows. Setting the yearly years by hand finds them from the price, as before.
  - The yearly band runs from the newest 🅨 date plus a fixed number of calendar days per asset to
    the date plus a second number (Bitcoin 1143-1508: 14 January 2026 to 14 January 2027). This
    was read from the original's band at past dates on ten assets.
  - The S&P 500 has no dates: its chart was never read. (Wrong, corrected 2026-11-04: its Start label,
    3 January 2000, had been read; it is now on the preset. The S&P has no yearly dates.)
- **Checked:** on the original's charts the Start dates take the model from F1 .9523 to .9595 (lows
  found 1,817 -> 1,826 of 1,894, extras 105 -> 86). The C# end to end is in the next entry. The yearly
  band holds exactly across epochs on Bitcoin (3) and Ether (2). The Dow (8 epochs before 1994) is
  within a day; the dollar, Gold, Riot and Marathon are within the holidays the original skips past
  the last bar.
- **Tests:** four new (the Start, the dated yearly lows and their clamp, the yearly band, causality
  of the dated count). The whole-history Bitcoin tests now run the preset on a chart with no
  symbol, so they still count from 2011. Six sabotages red (section 34).
  `A_count_component_is_a_whole_number_on_the_status_line` had failed since the moving average was
  left out by default (96768f6b); it now includes the moving average first.
- **Caveats:**
  - The 🅨 dates are hindsight: a backtest that trades on a tuned asset's yearly lows reads the
    future.
  - Silver, the miners, MicroStrategy and Coinbase bands were read at one date only.
  - Under a custom count and on untuned symbols the original draws no yearly lows; Cipher C still
    finds its own there.
  - The dollar's table still says 15-24 while its band is 16-25, as the original shows them.
  - Not seen or heard in the app.

### Cipher C's daily and weekly bands are drawn where the original draws them (2026-10-28)

- **What changed (Cody's precise pass, "the weekly and yearly bands" and "the dollar's daily band,
  a day late").** Read from the original's band boxes at four dates on each of 21 daily charts
  (CIPHER_C_DESIGN section 33):
  - A band's days count from the low bar's timestamp, time of day included. US stocks' bars are
    stamped 14:30 UTC in winter and 13:30 in summer, so a winter low's band lands a bar later in
    summer. That was the dollar's "day late" and several stocks' (`BarOnOrAfter`).
  - The weekly band runs from the newest weekly low (a candidate not included, `TierRun.NewestAt`),
    not the confirmed one, and counts weeks of 7 calendar days (7/5 off crypto), not 7 bars.
  - Presets whose band is not their table's range carry Band ranges: the dollar's daily band 16-25
    (table 15-24), Ether's weekly 50-71 days, Gold's and Silver's weekly start 22 weeks (table 20).
    The table and the counts still say the table's ranges.
  - An untuned symbol's bands are calendar days on any calendar: daily 40-50 days, weekly 150-190.
- **Checked:** band ends dated on the chart, Cipher C against the original: daily 48 of 58 (tuned
  only) -> 82 of 85 (all 21 assets); weekly 4 of 10 -> 15 of 17.
- **Tests:** two new (the preset exceptions and untuned bands on weekdays and crypto; the time of
  day), and the weekly-band test from the start of this pass. Eleven sabotages red, listed in
  section 33.
- **Caveats:**
  - The misses are lows that are not ours (Marathon's and Solana's daily lows, Oil's weekly low)
    and one Nasdaq weekly start a day early, unexplained.
  - Past the last bar the original's bars skip exchange holidays and ours do not: there its band's
    bar count is ours less the holidays in between (within one bar on 186 of 218 future ends; crypto
    exact). The dates agree.
  - The yearly band is unchanged (a year of 365.25 days): the original's reads its per-asset date
    table, which waits on Cody's answer (section 22).
  - Off a daily chart (bands in bars) not checked against the original. Not seen in the app.

### Cipher C's unconfirmed week count runs from the newest weekly low drawn, the candidate included (2026-10-28)

- **What changed (Cody's precise pass, "counting a dropped weekly low in the unconfirmed weekly
  cell").** The table's unconfirmed weekly cell counted weeks from Cipher C's newest weekly low, so
  while a weekly candidate was drawn and not yet judged it ran on from the low before (often 20 or
  30 weeks, where the original said 3). Read from the original's table on its sixteen tuned daily
  replays: it counts from the newest low it has drawn, the candidate included (94% of bars on its
  own drawings; the rest are the metals). On Gold, Silver and the two miners' ETFs it also counts
  from a low it drew nowhere: a candidate that went (too near the newest weekly low and not under
  it), or an undercut that became only the reference low. `TierRun.HeldAt` of both weekly machines
  is now that low; only the table's unconfirmed weekly cell reads it, so no drawing changes.
- **Checked:** the cell, Cipher C against the original on every replayed bar of the 19 daily charts
  (24,118 bars): 53.2% -> 91.7% exact. GDX goes to 100%; every chart is level or better. The other
  three cells (days and weeks since the confirmed lows, days since the unconfirmed daily low) are
  unchanged at 96.2, 88.8 and 97.5%.
- **Tests:** two new (the candidate counted from when drawn, and after it is judged; a metal low
  drawn nowhere counted from), and the "only what it knew then" test now covers HeldAt. Five
  sabotages red: no drawn low, no candidate on the non-metals, no gone candidate, no reference, no
  metal candidate.
- **Caveats:** what is left in the cell is where Cipher C's weekly lows differ from the original's
  (Coinbase, Riot, the dollar and oil on the newest-drawn rule; Gold, Silver and junior gold miners
  most of all). Not seen in the app.

### Cipher C starts its count where the original does: on the chart's first bar, or the first bar of 2000 on an untuned symbol, as a low confirmed one hold later (2026-10-28)

- **What changed (Cody's precise pass, "per-asset start dates").** The original writes "Start" under
  the bar its count starts from. Read on its captured charts: on a symbol it does not tune, that is
  the first bar on or after 1 January 2000 (UTC), on every chart that has the label (16 of 16; the
  others are past its 500-label limit). The count then runs as if a low on the Start bar had been
  confirmed one hold (half the shortest cycle) later, so the first cycle's top is looked for from
  the bar before that, not from the bar after Start.
  - Cipher C's daily tier started from the lowest bar of the first shortest cycle, and looked for
    the first top from the bar after it. It now starts on the chart's first bar on a tuned symbol,
    on the first bar on or after 1 January 2000 on any other, and holds the start as a confirmed low
    (`CipherCEngine.StartBar`, `DetectDaily(seedConfirmed: true)`). DSS is still worked out over the
    whole history.
  - **Checked:** a line-for-line Python model of the daily tier with this start matches
    `DetectDaily` exactly, every low on all 39 captured charts. Scored from the original's Start bar
    on its charts that have one, the original's lows found / extra lows: the old start 1,812 of 1,894
    with 109 extras (F1 .9499), the new 1,817 with 105 (F1 .9523). No chart is worse.
- **Not carried: the original's start dates on the symbols it tunes.** On those it starts on a date
  of its own (many at the March 2020 low, some in the 1980s); with those dates the same model
  scores 1,826 with 86 extras (F1 .9595), and Bitcoin's weekly extra low of September 2023 goes
  away. The dates are read off the original's chart, so they wait for Cody's word, as the yearly
  date table does. Until then a tuned symbol starts on the chart's first bar.
- **Caveats:** confirmed one hold later and one hold and a bar later score the same on the
  original's charts (F1 .9595 both, with its dates); one hold was kept, the same as a real low's
  confirmation, and no test tells them apart. Starting with no confirmed low, or confirmed on the
  Start bar or a bar later, scores .9564. The original names its first low one bar earlier than this
  on Bitcoin's weekly chart (46 bars from Start, not 47), probably on the bar still forming; not
  modelled. On the Bitcoin daily fixture the new start moves nothing.
- Tests: `CipherCDailyTests.The_count_starts_on_the_start_bar_as_a_low_confirmed_one_hold_later`
  (built bars: a spike high on bar 2 and a dip on bar 3; from a plain start the dip is named, from
  the confirmed start the wave's own low on bar 14; the provider counts the same way) and
  `An_untuned_symbol_is_counted_from_the_first_bar_of_2000` (the fixture moved back twelve years).
  The provider-versus-engine tests now start their expected runs on the confirmed first bar.
  Sabotages, red: the start not held as confirmed; an untuned symbol started on the first bar;
  after 2000-01-01 rather than on it; tuned and untuned swapped; the provider not passing the held
  start. Green, as expected: one hold and a bar later (see caveats).
- Not seen or heard in the app. No accessibility agent is registered here to review it.

### Cipher C mirrors the original's settings: its display switches, the table's sections, the moving average's source and offset, and the custom count's words (2026-10-28)

- **What changed (Cody, 2026-10-28: "all trading view cf settings should be mirrored", "is the
  table drawn on screen by default in the original … have it shown by default … with a way to
  disable it", "default count: address this").** Every input of the original was listed with its
  default, and the unclear ones were set one at a time on its Dow daily chart and read back from its
  drawing (CIPHER_C_DESIGN section 30).
  - **27 display switches, with the original's defaults, in its order** (`CipherCProvider.Switches`):
    the table and its four sections (asset detection, asset list, cycle low windows, asset cycle
    count); the daily lows' marks, lines and grade; the cancelled lows' crosses (off), the updated,
    unconfirmed and confirmed signs, and the confirmation mark (off); the weekly and yearly lows'
    marks and lines; the count numbers, daily and weekly; the cycle wave; the daily, weekly and
    yearly bands; the translated tops, their lines and their boxes. **Pictures only:** a switch
    hides a drawing and nothing else; what is found, spoken, stopped on and alerted on is the same.
    The table is shown by default, as the original's is, and can be switched off.
  - **Two marks are now hidden by default, as in the original:** the cross on a cancelled daily low
    and the mark under a confirmation's bar. Both are still spoken and still stops.
  - **The table's sections** each blank their own cells and move nothing: asset detection the whole
    first column, the asset list its last three rows, the cycle count the ranges, the windows the
    counts. `ChartInfoTable` passes the series' settings to `TableRows`.
  - **A count picked in Asset preset now says so, in the original's words:** "ETH COUNT SELECTED"
    in red over the ranges, "ETH Count Selected", "Recommended for Short-cycle Crypto", no yearly
    range, and "FUTURE CYCLE LOW IS INVALID". The first cell still says whether the chart's own
    symbol is tuned. Gold and silver are named "XAU/USD" and "XAG/USD", as the original lists them.
    Auto still means the symbol picks. The original ignores its count selection until its custom
    count switch is on, and then uses it even on a tuned symbol, which is what a typed preset
    already did.
  - **The moving average** gains the original's source and offset (General), and is left out until
    included, as the original's is off by default (its width 3 and colour #FDD835CC are the
    original's). The confluence grade still reads the close's average, unshifted; the original's
    labels do not change with its source either.
  - **Settings tabs:** the six cycle range overrides moved to Advanced (the original has no such
    inputs); Faster confirmation moved to General (the original has it).
  - Renderers: `ComponentCompanions.Hide` (bits `HideMarker`, `HideLine`, `HideWord`, badge signs
    shifted by `HideBadgeShift`), read by the upward ring, the downward triangle, the marks' lines
    and the stacking.
- **Not mirrored:** the original with its custom count on and left at NONE (on a tuned symbol it
  keeps the count, drops the yearly lows and shows the custom words); the original draws no yearly
  lows under a custom count, but Cipher C still finds its own; sizes, glyphs, the mascot, line
  styles and widths, per-part colours beyond Appearance, the US data table, cancelled weekly lows,
  the adjustment group and the arcs' geometry.
- **Tests:** seven new (CipherCProviderTests section 7c, `A_hidden_marker_draws_no_ink`), one
  extended (the chart's table reads the series' switches), one updated (a saved Cipher C restores
  with the moving average left out). Each display switch is flipped on Bitcoin's daily chart and
  must change at least one picture and nothing else. Sabotages red: the confirmation mark never
  hidden; a band switch clearing the window itself; a number switch made vacuous; the grade reading
  the shown source; the offset ignored; the windows section collapsing its cells; the asset list
  switch ignored; the table switch ignored; a picked count keeping the yearly range; gold's
  name; the badge shift dropped; the lines, the ring's stacking and the downward triangle ignoring
  the hide bits; `ChartInfoTable` not passing the settings. One did not go red: dropping
  `Hidden`'s NaN check, because on .NET 10 a NaN converts to 0, so it hides nothing either way.
- Not seen or heard in the app. No accessibility agent is registered here to review it.

### Cipher C says the original's "days since", and speaks its unconfirmed counts and marks (2026-10-28)

- **What changed (Cody, 2026-10-28: "speech, switch to whichever one is more accurate and make them
  match", and on the unconfirmed counts, "yes speak them").**
  - **Days since, everywhere.** The timing windows' value (Daily, Weekly and Yearly Timing Window)
    is now the original's number: the days (weeks, years; bars off a daily chart) since the last
    low, 0 on the low's own bar. Before, it was the day of the cycle, one more. Speech, the status
    line, the bar detail, Alt+Shift+D, new-bar narration and the table now say the same number:
    "40 days since the last daily cycle low, in its window, 4 days left" (was "daily cycle day 41,
    …"); "Daily timing window open, 35 days since the last daily low." (was "…, day 36."); a pending
    low, "Daily cycle low pending 76 days after the last cycle low, mid confluence: …" (was "on day
    77"). The window itself is unchanged (it still counts the low's own day as day 1, which is what
    the original's window does; section 29 of CIPHER_C_DESIGN). **Breaking for saved strategies and
    the ML export:** a rule on Daily Timing Window's value, or the `c_dcl_day` column, is one lower
    than before; `SignalCatalog`'s notes say so.
  - **The unconfirmed counts are spoken.** The table's lower cells, the days since the low held now
    and the weeks since the newest weekly low, are in the state words (Alt+Shift+D, the bar
    detail): "12 days since the unconfirmed daily low", "3 weeks since the unconfirmed weekly low";
    nothing when none is held (the table's ✅). `CipherCProvider.HeldWords`.
  - **The marks are spoken and are stops.** The held low's clock mark and a cancelled low's cross
    are drawn on bars with no confirmed low, so arrowing said nothing there and Ctrl+Left/Right
    skipped them. On Daily Cycle Low the held low now says "Daily cycle low, unconfirmed: the low of
    59100.00, mid confluence, 10 days since it." (the count as the table prints it, plus "A low was
    cancelled since the last confirmed one." with the refresh sign), a cancelled one "Daily cycle low
    cancelled: the low of 7325.37, undercut before it was confirmed." (`CipherCProvider.MarkWords`).
    `IndicatorCrossingEngine.DoSparseSignalJump` treats a marker's `ComponentCompanions.Mark` bars
    as stops too, for any indicator.
- **Also fixed:** `BackgroundTintTests.Cipher_C_tints_only_the_bars_inside_its_timing_window` had
  been red since d82c20a6 (the original's window green, and the tint off by default); it now
  switches the tint on and expects the new green.
- **Tests:** new `The_unconfirmed_counts_are_said_as_the_table_prints_them` (7 cases),
  `The_held_low_and_its_count_are_spoken` (Bitcoin fixture: state words, table cell and every
  mark's speech), `A_mark_says_held_or_cancelled`, `CrossingMessageTests.A_markers_mark_only_bars_are_stops_too`;
  the window, pending, detail, table and tint tests moved to days since. Sabotages, all red: count
  as the day of the cycle (2 tests); no held words; no mark speech; the held count read at the
  mark's bar instead of the last; a cross spoken as held (2); no mark stops; the pending count one
  more; the window arithmetic on days since instead of the cycle's day (several window cases).
- **Caveats:** the weekly tier's marks (none drawn) and the Start plate are not spoken; the
  signal count said on landing on Daily Cycle Low counts lows only, not marks. Not seen or heard in
  the app; no accessibility agent registered here to review it.

### Cipher C's marks touch the bar, Start sits where the count starts, and the table says the original's words (2026-10-28)

- **What changed:** three of the open points in the entry below.
  - **Marks touch the bar.** A daily, weekly or yearly low's triangle points up at the bar's low
    with its tip about two pixels under it, and the daily confirmation's down triangle points at the
    bar's high the same way, as the original draws them. Before, both sat a share of the bar's range
    away (the marker padding every other indicator keeps). New knob `MarkerTouchesBar`
    (`IndicatorComponentMetadata` → `ComponentConfig`); `StandardRenderers.ResolveMarkerY` measures
    the gap from the triangle's tip, so the base sits under the tip and not on the bar. The 49D label
    beside a low never rises over the tip.
  - **Start** is drawn under the bar the daily count starts from (the lowest bar before the first
    low, `CipherCEngine` `TierRun.Start`), on its own orange plate with no triangle. Before, it was
    under the first low.
  - **The table** says the original's words and numbers: "✅ Finetuned Asset Detected ✅" (or
    "⚠️ Unoptimised Asset Detected ⚠️"), "<symbol> CYCLE COUNT", "Cycle Low Windows", "Default
    Count Selected", "Daily / Weekly / Yearly Cycle Range", "✅ Optimised Timeframe - Daily ✅" (or
    "⚠️ Timeframe Not Optimised, Switch to Daily ⚠️"), "✅ Confirmed ✅" and "🕘 Unconfirmed 🕘"
    with "Days since Last DCL :" and "Weeks Since Last WCL :", "👇 Finetuned Assets Listed Below 👇"
    and the preset symbols in two rows (stocks and metals, then the crypto ones from Ether). Its
    confirmed counts are the days and weeks since the low (the low's own bar 0), one less than the
    day of the cycle Cipher C speaks. The unconfirmed cells are new: the days since the low held
    now (a check when none is held, as after a confirmation, and still counting from the dropped
    low's bar after a cancellation, as the original does), and the weeks since the newest,
    unconfirmed weekly low, in the preset's week.
- **Not changed:** speech still says the day of the cycle (one more than the table); question in TODO.
- **Tests:** `A_Cipher_C_low_touches_the_bars_low`, `A_Cipher_C_confirmation_touches_the_bars_high`
  (the tip within 3 px of the bar's edge, no marker ink inside the bar, the padded mark lower) and
  `Start_is_drawn_alone_under_the_bar_the_count_starts_from` (real renderer pixels); the signs test
  pins Start at the lowest bar before the first low and alone there; the three table tests pin the
  words, the counts as days since, the unconfirmed run, the check on a confirmation, the count
  carried across a cancellation and the weekly cell; the metadata test pins which marks touch; the
  knob sweep gains a ringed marker under the bar. README 9686 tests.
- **Sabotages red (22):** the touching gap ignored (pixel tests and the knob sweep), the triangle's
  reach left out of the ringed and the down triangle, the lows and the confirmation not touching,
  the factory and the clone dropping the knob, the label allowed over the tip, Start not drawn
  alone, Start under the first low, the count's start not recorded, the count not less one, the
  unconfirmed count reset on a cancellation, no check after a confirmation, the weekly cell from the
  oldest weekly low, the symbol rows split in half, three header words changed.
- **Caveats:**
  - "Default Count Selected" is always shown. The original's wording when a preset's count is
    changed is not read (Cipher C's count is not a setting).
  - The unconfirmed weekly cell counts from Cipher C's newest weekly low. The original counts a
    dropped weekly low on, which Cipher C does not model.
  - On one 4-hour chart the original's header names another symbol's count; not copied.
  - Not seen in the app. The accessibility-lead review agent is not registered in this environment,
    so no agent review was run. The table stays aria-hidden: everything in it is spoken already,
    except the unconfirmed counts (question in TODO).

### Cipher C is drawn as the original draws it (2026-10-28)

- **What changed:** Cipher C's look is replaced by the original's, read from the original's own
  drawings on its charts (Cody: *"please add all remaining visual elements from the original ...
  Remove our visuals and replace with the original"*). Speech, sounds, stops and readings are
  unchanged, except that the yearly window is a new component (below).
  - **Lows:** a triangle over a disc with the tier's letter cut out (D green, W blue, Y gold), a
    faint dotted line through the chart at each, the cycle's length beside the daily and weekly
    lows (49D, 24W; the weekly one is now printed beside the daily one on a shared bar), and the
    weekly mark stacked under the daily one, the yearly under the weekly.
  - **Signs under the daily low's D:** a check once confirmed, with a refresh sign over it when the
    low replaced a cancelled one; a cancelled low keeps its mark with a red cross; the low being
    held now has a clock, the refresh sign if one was cancelled since the last confirmed low, and
    its grade (LOW, MID, HIGH); the first low says Start on an orange plate.
  - **The daily confirmation** is a check over a green down triangle at the bar's high. The weekly
    and yearly confirmations, the pending, cancelled and failed events are no longer drawn, since
    the original draws none of them. They are still heard and are still stops. Each can be drawn
    again from Appearance.
  - **Translated tops:** the letter (L, M, R) in a disc over a down triangle, a dotted line at the
    top, and a faint box over the cycle, from low to low and from its lowest low to the top.
  - **Timing windows:** the live cycle's windows as faint bands running into the future, daily
    green, weekly blue, yearly yellow. The tint inside the window is now off by default (it is
    ours, not the original's). A new Yearly Timing Window component counts the year of the yearly
    cycle and carries the yearly band.
  - **The wave** is the original's yellow and level: middle 0.8 x the close on the bar of the
    newest daily confirmation, troughs and crests a fifth of the 100 bars' range (to that bar) under
    and over it, over the last 21 cycles and the live one. Before, it sat under the candles at a
    height of our own.
  - **The table:** the original's table in the price pane's bottom right corner, seven rows of
    five columns in its cell colours, with our words. It shows whether the symbol has a tuned preset
    (green headers, or red with the ranges block left blank), the preset's daily, weekly and yearly
    ranges, whether the chart is daily (or the original's red "switch to daily"), the daily and
    weekly cycle counts the speech says, how long ago the held daily low was, and the symbols with a
    preset. It reads the bar the status lines read. It is real DOM text, aria-hidden and transparent
    to the mouse, like the status lines: everything in it is spoken already.
  - **Colours** are the original's: daily #4CAF50, weekly #2962FF, yearly #D5C110, tops #F23645 /
    #FF9800 / #4CAF50, the wave #FDD835 at 0xCC. TradingView keeps them as 0xAABBGGRR, which is
    how they were read.
- **New knobs any indicator can use** (`ComponentConfig`, rebuilt from `IndicatorComponentMetadata`
  on load): `BoxAlpha` (boxes from the `_boxStart`/`_boxEnd`/`_boxTop`/`_boxBottom` companions),
  `MarkLineAlpha` (dotted lines), `PathColorHex`, `MarkerStacksUnder`, `BadgeWords` (with the
  `_badge` bits and `_badgeWord` companions), and `DefaultIsDrawn`. Drawn by
  `Rendering/ComponentPictures`; the table is `Rendering/ChartInfoTable` and
  `CipherCProvider.TableRows`.
- **Numbers (private captures):**
  - The wave's level and height: to floating-point precision on 68 of the original's 68 charts.
    On Heikin-Ashi its middle follows the Heikin-Ashi close and its height does not change (the Dow
    live: the difference is exactly 0.8 x the change in the last close).
  - The daily band: to the day on 7 of the 8 daily charts with one in view; the dollar index's is
    one day late at both ends. It starts on the first bar on or after the last low's date plus the
    shortest cycle in calendar days (a trading day is 7/5 of one), rounded, and ends on the first
    on or after plus the longest, rounded down.
- **Tests:** `The_signs_the_top_boxes_and_the_bands_are_where_the_original_draws_them`, the table's
  five (`The_table_on_a_tuned_daily_chart_...`, `..._no_preset_is_red_...`,
  `..._off_a_daily_chart_warns_...`, `There_is_no_table_outside_the_data`,
  `The_chart_draws_a_table_for_each_shown_Cipher_C_on_the_price_pane`) and the bUnit
  `Cipher_Cs_table_is_hidden_dom_text_reading_the_cursor_bar`; `The_cycle_arc_is_drawn_between_known_lows`
  now pins the level, including a history cut where the 100th bar back holds the range's extreme;
  `A_label_companion_prints_49D_beside_a_low_triangle`; the metadata test pins the colours, letters,
  alphas and the undrawn events; the knob test moves every new knob. README 9683 tests.
- **Sabotages red:** the wave's level 0.81, its range 99 and 101 bars, the oldest confirmation in
  place of the newest; the band's start rounded down, its end rounded, the 7/5 calendar dropped;
  no refresh sign, no cross, a wrong grade word, no Start, the top box's floor without the two
  lows' bars; the table's untuned ranges shown, its timeframe cell always green, its headers always
  green, its pending cell always a tick, aria-hidden removed, a table for a hidden Cipher C, a table
  for one on its own pane; no boxes drawn, no dotted lines drawn.
- **Caveats:**
  - The weekly and yearly bands are drawn by the daily band's rule; the original's are not that
    (its weekly band starts weeks early, its yearly one reads its yearly date table). Undecoded.
  - Start is placed under Cipher C's first low, which is not the original's first low on every chart.
  - The original's confirmation triangle touches the bar's high; ours keeps the marker padding.
  - The table's words are ours. Its pending weekly cell is not modelled (a dash). Its counts are
    Cipher C's day of the cycle, which is the original's "days since" + 1 (question in TODO).
  - Not seen or heard in the app. The accessibility-lead review agent is not registered in this
    environment, so no agent review was run.

### Cipher C: a translated top is mid only exactly halfway (2026-10-28)

- **What changed:** `CipherCProvider.TranslatedTop` picks each finished cycle's top and its
  translation, on every tier. The top is the highest high with both lows' bars included (was:
  the bars between them only), the first of equal highs. It is mid only when it is exactly halfway
  in bars, left before, right after (was: 45-55% of the way through was mid). A top on the starting
  low's bar is drawn now; it used to be skipped.
- **Why:** this is the original's rule, read from its own boxes and the letters over their tops
  (CIPHER_C_DESIGN section 28). It draws about 2% of tops mid; the band drew many more.
- **Numbers (private captures), band → exact midpoint:**
  - On the original's boxes: daily 86.0% → 94.6% of 7,529 tops, 4-hour 78.1% → 86.8% of 1,244,
    weekly 218 → 236 of 239. The top's bar: 7,524 of 7,529.
  - As the app runs on the sixteen tuned daily charts, a top on the original's bar with its letter:
    1,024 → 1,143 of the original's 1,244.
- **Tests:** `A_top_is_the_first_highest_high_and_mid_only_exactly_halfway` (ten built cases: before,
  at and after halfway, 45% and 55%, equal highs, a top on either low's bar, odd lengths);
  `A_top_says_its_high_and_where_it_came_in_its_cycle` now rebuilds every top on Bitcoin's fixture
  from its two lows; `A_mid_top_says_its_day_and_cycle` keeps the mid top's words, since Bitcoin's
  fixture no longer has a mid top. README 9676 tests. Sabotages red: the band back (3 tests), the ending low's bar
  left out, the last of equal highs, an exact half called left, the starting low's bar left out
  (2). The provider skipping a top on an ending low's bar stays green: no top on Bitcoin's fixture
  falls there.
- **Caveats:**
  - The misses cluster on a few charts: the dollar (94 of 113 daily, 64 of 129 on 4 hours), SILJ,
    Silver and Coinbase on 4 hours, and three untuned daily charts. There the original often calls
    a late top left; nothing tried explains it (section 28).
  - When one bar is the top of two cycles, the later one's top replaces the earlier one there.
  - Not seen in the app: the triangles' colours move on many tops.

### Cipher C: on the miners a weekly double must be under the newest weekly low (2026-10-27)

- **What changed:** `CyclePreset.DoubleUnderNewest`, on for GDX, GDXJ and SILJ. In
  `CipherCEngine.WeeklyByWeeks` the "double" (the first daily low after the reference made a weekly
  low because it is under the reference and the next daily low is not under it) also needs that
  daily low under the newest weekly low. `CipherCProvider` passes the preset's flag. Gold and Silver
  keep the plain double.
- **Why:** the metals' weekly residue, on the original's own daily lows: of the five miner doubles
  the original skipped the two that were not under the newest weekly low and drew two of the three
  that were (CIPHER_C_DESIGN section 27). On Gold the same condition scores lower.
- **Numbers (private captures), plain → under on the miners:**
  - On the original's daily lows: metals' weekly F1 0.848 → 0.892 (GDX 10 + 2 → 11 + 0, GDXJ
    9 + 3 → 10 + 1, SILJ unchanged); nineteen assets 699 → 701 of 713, others 14 → 10 (0.980 → 0.985).
  - As the app runs on the nineteen captured daily charts: weekly 0.927 → 0.931 (metals 0.815 →
    0.859; GDX 10 + 2 → 11 + 0, GDXJ 6 + 5 → 7 + 3); daily lows unchanged (0.974). No chart worse.
- **Tests:** `A_miner_double_must_be_under_the_newest_weekly_low` (built case: over, level and under
  the newest weekly low, flag on and off); `The_miners_weekly_tier_doubles_only_under_the_newest_weekly_low`
  (Bitcoin's fixture run backwards, dates kept in order, on the GDX preset: the provider's weekly
  marks are the rule's and differ from the plain double's); the presets test pins the flag to the
  three miners. Sabotages red: the rule removed (3 tests), `<=` for `<` (the level case), the rule on
  for every preset (3), the provider not passing the flag (the provider test), GDXJ's flag off (the
  presets test). README 9665 tests.
- **Caveats:**
  - Very few events: five miner doubles, and one of them (junior gold miners) was drawn by the
    original while over the newest weekly low, against the rule. Most of the gain is knock-on (the
    machine stays in step afterwards). Found and scored on the same history.
  - The held-out 4-hour and weekly charts have no miner double: no evidence either way.
  - A daily low level with the newest weekly low is no double; untested against the original.
  - Not seen or heard in the app.

### Cipher C: of two equal lows, the first is named (2026-10-27)

- **What changed:** in `CipherCEngine.DetectDaily`, when two swing lows after the cycle's top are
  equally low, the naming bar names the first of them; before, the latest. What makes a bar a swing
  low is unchanged.
- **Why:** five of the daily misses were a low of the original's with an extra of Cipher C's a few
  bars later at exactly the same price (Marathon, SILJ, Platinum twice, Apple). Taking the first of
  a flat bottom instead, or the first of equal highs for the cycle's top, is worse
  (CIPHER_C_DESIGN section 26).
- **Numbers (private captures), latest → first:**
  - Whole-history daily lows, nineteen charts (replica): F1 0.9683 → 0.9719 (1,773 → 1,780 of
    1,838, extras 51 → 45). Heikin-Ashi, held out: unchanged (0.9804).
  - As the app runs on the nineteen captured daily charts: daily 0.970 → 0.974, weekly 0.926 → 0.927.
  - Held out: 4-hour charts 0.950 → 0.951, weekly charts unchanged, 36 untuned daily charts
    0.9541 → 0.9554.
  - No chart worse on any set.
- **Tests:** `CipherCDailyStudy` regenerated by `ccd_fixture2.py` from the replica with the new
  rule (the Bitcoin fixture's early prices tie often, so every run's lists moved); new
  `The_first_of_equal_swing_lows_is_named` (bars 162 and 166 of the Bitcoin fixture, both 3.80; bar
  169 names 162). Sabotage, the latest again: the five parity runs and the new test go red.
- **Caveats:**
  - Found on the same whole-history lows it is scored on; the held-out sets move by only six lows
    in all, every one of them the right way.
  - Not seen or heard in the app.

### Cipher C: Bitcoin drops a pending low on any undercut before it is due (2026-10-27)

- **What changed:** `CyclePreset.DropUntilDue` is on for BTC too (the Dow, the dollar, Silver and
  Ether already had it). On Bitcoin a bar under the pending daily low at any time before it comes
  due now drops it; before, only within d / 3 bars of naming it.
- **Why:** Bitcoin's daily chart never undercuts a pending low between d / 3 and due, so the
  original's daily drawings could not decide it (entry below). Its 4-hour and weekly charts, where
  the original runs its daily count on the chart's bars and which were never used to fit anything,
  do: the original's confirmed marks there match 0.989 against 0.944 (4-hour) and 0.800 against 0.667
  (weekly) with it. The same held-out charts confirm the earlier split: the Dow, the dollar and Silver
  score better until due on both timeframes, Ether the same, and the Nasdaq, gold, GDX, oil and
  Coinbase worse (CIPHER_C_DESIGN section 25).
- **Numbers (private captures):**
  - Bitcoin's captured daily candles as the app runs: daily lows 22 of 22 with 1 other → none
    other, weekly lows 9 of 10 with 1 other → 10 of 10 with none.
  - Pooled over the nineteen captured daily charts: weekly F1 0.924 → 0.926, daily unchanged (0.970).
  - Whole history (probe), daily F1 0.9681 → 0.9683; Heikin-Ashi held out 0.9793 → 0.9804.
  - Off the daily chart, pooled: 4-hour 0.947 → 0.950, weekly 0.942 → 0.944.
- **Tests:** the presets test now expects BTC among the drop-until-due assets; the Bitcoin weekly
  test in `CipherCProviderTests` now pins all ten of the original's weekly lows to the fixture's end
  and no other (before, it allowed one miss and two extras); the faster-confirmation test builds its
  expected lows with the preset's drop rule. Sabotage: BTC without the flag turns the presets test and
  the weekly test red.
- **Also checked, no change:** on 36 daily charts of symbols the original does not tune, the default
  count's d / 3 window scores 0.949 against 0.905 until due, so the default keeps the window. The two
  charts where it did badly, NASDAQ:COIN and NASDAQ:MSTR, are tuned by the original (by ticker), and
  Cipher C already gives them their presets (0.919 and 0.909 there; section 25).
- **Caveats:**
  - Bitcoin's 4-hour and weekly charts are one capture each; the weekly one has only 2 confirmed
    marks. The weekly chart still has one other low, and the original confirms nothing there after
    its second mark.
  - Coinbase's 4-hour chart (0.758) is unexplained: the original confirms nothing for its last 659
    bars.
  - Not seen or heard in the app.

### Cipher C: the deep double compares with the daily low before the weekly low (2026-10-27)

- **What changed:** in `CipherCEngine.WeeklyFromDaily`, after a candidate is made a weekly low (U),
  the first daily low after it (F) is a weekly low of its own when the second is not under F, F is
  under the daily low before U, and F is at least `DoubleMinBars` (16) bars after U. This replaces
  the deep double of the entry below (U more than 2% under the weekly low before it;
  `DoubleDepth` is gone). The far double, the candidate and the metals' `WeeklyByWeeks` are
  unchanged.
- **Why:** on the original's whole-history drawings, the 214 cases after a candidate became a
  weekly low (second daily low not under the first) split on one comparison: F was a weekly low in
  36 of 40 cases when under the daily low before U and in 0 of 174 when not. The four exceptions
  are all under 16 bars. The 2% rule got 24 of the 214 wrong. CIPHER_C_DESIGN section 24.
- **Numbers (private captures),** weekly lows after the original's first, F1:
  - On the original's own daily lows, non-metals 0.913 → 0.994 (643 exact, 5 extra, 3 missed; 11
    of 14 assets exact). All nineteen 0.907 → 0.980. Before 2022 0.912 → 0.993, from 2022 0.913 →
    0.995.
  - As the app runs, on Cipher C's daily lows from the chart's first bar, 0.839 → 0.925 (657 / 51
    / 56; non-metals 0.841 → 0.936; metals 0.815, unchanged).
  - Better: Apple 0.73 → 1.00, MicroStrategy 0.86 → 1.00, Ether 0.69 → 0.97, Riot 0.71 → 0.89,
    the S&P 0.81 → 0.94, Platinum 0.84 → 0.91, oil 0.90 → 0.94, the dollar and the Dow by 0.01.
    Worse: Marathon 0.89 → 0.86 (one more extra).
- **Tests:** `CipherCWeeklyTests`: the deep-double test is a theory over its edges (F under or not
  under the daily low before U; 16 and 15 bars after U); the no-deep-double-after-an-undercut test
  now has an earlier candidate made a weekly low, so it fails if the undercut does not clear the
  rule; the constants test pins 16. On the nineteen assets' candles the C# matches the Python model
  (uncommitted harness). Seven sabotages went red: `<=` for `<` against the daily low before U,
  `>` for `>=` on the bars, 15 bars, the rule after any weekly low (not only a candidate made one),
  the weekly low before U in place of the daily low before it, the second daily low remembered as
  the one before the candidate, and no bar limit.
- **Caveats:** the comparison and the 16 bars were read from all the history at once; the split by
  2022 is a check on both halves, not a held-out test. Why the original compares with the daily
  low before the weekly low is not known. The metals still do not fit this machine (0.49–0.62 with
  it) and stay at 0.815 in the app; GDXJ 0.55 and Silver 0.70 are the weakest. The remaining app
  misses on the non-metals come from Cipher C's daily lows (EUR/USD 0.88, the dollar 0.89, the
  Nasdaq 0.90, Coinbase 0.88, Marathon 0.86), not this rule. Not seen or heard in the app.
- **Held out, Heikin-Ashi bars** (the original's drawings on them were never used in fitting):
  weekly F1 as the app runs, the Nasdaq 0.962 (0.821 before the weekly machine), gold 0.872
  (0.698), the Dow 0.992, Bitcoin 0.900; on the original's own Heikin-Ashi daily lows the machine is
  exact on the Dow, the Nasdaq and Bitcoin.

### Cipher C: the weekly tier is one state machine, and by weeks on the metals (2026-10-27)

- **What changed:** `CipherCEngine.WeeklyFromDaily` replaces `WeeklyFromTwoDaily`,
  `WeeklyFromNextDaily` and `KeepBars`. The second daily low after the newest weekly low is drawn
  as the candidate; the next daily low cancels it (undercut; that lower low is a weekly low at any
  distance) or fixes it. Two "doubles" make the first daily low after a weekly low one of its own:
  the far double (under it, 1.25 shortest daily cycles or more after it; off on Ether) and the deep
  double (after a fixed candidate more than 2% under the weekly low before it). Gold, Silver and
  the miners use `CipherCEngine.WeeklyByWeeks`, the same candidate judged in weeks: a new weekly low
  8 weeks short of the longest weekly cycle, a nearer lower one replaces the newest, a double 14
  weeks short. `CyclePreset` loses `WeeklyEveryTwoDaily` and `WeeklyNextDaily` and gains
  `WeeklyByWeeks`; `WeeklyDoubles` (false on Ether) now switches the far double.
- **Why:** read from the original's candle replays (its table's two week counts, bar by bar,
  2022–26) and checked on its whole-history drawings on nineteen assets. CIPHER_C_DESIGN section 23;
  sections 14 and 16 are superseded.
- **Numbers (private captures),** weekly lows after the original's first, F1:
  - On the original's own daily lows 0.837 → 0.907 (metals 0.772 → 0.848, the rest 0.843 → 0.913).
  - As the app runs, on Cipher C's daily lows from the chart's first bar, 0.794 → 0.839
    (595 exact, 111 extra, 118 missed; metals 0.815, the rest 0.841).
  - Held out by time: before 2022 the non-metal rule scores 0.912 (the rules before, 0.840).
  - Better: the Dow 0.81 → 0.99, SILJ 0.75 → 0.96, gold 0.73 → 0.88, Coinbase 0.67 → 0.88,
    EUR/USD 0.81 → 0.88, Silver 0.60 → 0.70, Ether 0.65 → 0.70, Platinum 0.78 → 0.84; Marathon,
    Riot, the Nasdaq and the dollar by 0.01–0.04.
- **Tests:** `CipherCWeeklyTests` rewritten for the machine (cancel, candidate drawn live, far and
  deep doubles at their edges, no deep double after an undercut, Ether's missing far double, the
  metal new / replace / gone cases, undercuts, the 70-bar double, the bar-by-bar causality run on
  both rules). `CipherCDailyTests` gains a per-preset theory (provider = the preset's rule, and the
  rules differ), the flag map, and real-bar sensitivity of the far double and both metal margins;
  the prepend test now asks the weekly tier to agree again on real bars cut by 17–500 (before bar
  1,200). The provider causality test cuts after the last weekly replace. On the nineteen assets'
  candles the C# matches the Python models (uncommitted harness). Nine sabotages went red (far
  double 1.3, depth 0.97, 9 weeks short, an undercut adding no weekly low, Ether's far double on,
  metals never replacing, the provider always passing far doubles, the provider taking metals by
  daily lows, a fix not counted as a candidate). One stayed green and is an equivalent mutant: not
  resetting the metal double's reference, which after a double can never double again anyway.
- **Caveats:**
  - Worse on six assets: GDXJ 0.76 → 0.55, Bitcoin 0.90 → 0.86, MicroStrategy 0.91 → 0.86, GDX
    0.91 → 0.87, Apple 0.74 → 0.72, oil 0.90 → 0.89. The S&P is unchanged at 0.81.
  - The metals' margins were fitted on the whole history; most of their gain is in sample (fitted
    after 2022 only, 0.694 before it).
  - The machine counts daily lows, so where the chart starts matters: it can run one daily low out
    of step until an undercut or a double brings it back (within 32 daily lows on the nineteen
    assets). The original's own start cannot be read; from its first weekly low the score would be
    0.855.
  - Not reproduced: the original sometimes moves its reference on by one daily low after a fixed
    candidate; nothing tried separates those cases.
  - On a metal bar that cancels both the candidate and the newest weekly low the engine reports one
    cancel.
  - Not seen or heard in the app.

### Cipher C: the Dow, the dollar, silver and Ether drop a pending low on any undercut before it is due (2026-10-27)

- **What changed:** `CyclePreset.DropUntilDue` (on for DJI, DXY, SILVER and ETH). On those assets a
  bar under the pending daily low at any time before it comes due drops it and the search goes on;
  elsewhere the drop stays within d / 3 bars of naming it. A bar under it on the due bar still
  confirms it. `CipherCEngine.DetectDaily` takes the flag; the provider passes the preset's.
- **Why:** the original's whole-history drawings with its bar limits off keep its cancelled marks,
  which the replays' final frames did not. Every undercut after d / 3 and before due was a
  cancellation on the Dow (13), the dollar (8), Silver (3) and Ether (2), and a keep on every other
  asset (64, 6 of them the Nasdaq's on the Dow's own numbers). CIPHER_C_DESIGN section 22.
- **Numbers (private captures):**
  - Sixteen tuned replays: 1,185 → 1,202 of 1,244 daily lows on the original's bar, others 65 → 35,
    F1 0.950 → 0.969. Whole history: 1,310 → 1,328 of 1,372, others 71 → 36 (0.952 → 0.971).
  - The Dow 138 → 147 of 148 (others 16 → 0), weekly F1 0.692 → 0.794; the dollar 107 → 111 of 115;
    Silver 51 → 54 of 56; Ether 162 → 164 of 165. No asset worse; untuned Platinum, Apple and
    EUR/USD unchanged (445 of 466).
  - Held out on Heikin-Ashi bars (never fitted): the Dow 138 → 146 of 147, others 14 → 0.
- **Tests:** `The_dow_drops_a_pending_low_on_any_undercut_before_it_is_due` (every drop is an
  undercut before due, at least one 12+ bars after naming, an undercut on the due bar confirms, and
  the lows differ without the flag); the parity study gains a Dow run (fixture regenerated by
  `ctz/ccd_fixture2.py`, the dollar run now with the flag); the presets test pins which four carry
  it; the provider's daily-tier theory gains DJI. Four sabotages went red: the engine ignoring the
  flag (4 tests), the provider not passing it (4), Silver losing it (1), and the flag dropping on
  the due bar too (3).
- **Caveats:**
  - Fitted per asset from these drawings; why the Nasdaq keeps what the Dow drops is unknown. GDXJ
    has one case each way and keeps d / 3.
  - The weekly tier is weaker on Heikin-Ashi than on candles on the Nasdaq and gold (held out).
  - The original's yearly marks turn out to be fixed dates per asset; Cipher C's yearly tier is
    unchanged (TODO question).
  - Not seen or heard in the app.

### Cipher C gains Faster confirmation, the original's input of that name (2026-10-27)

- **What changed:** a Faster confirmation setting (Properties → Advanced, off by default) turns on
  the daily tier's second way to name a low, the break of the last swing high, which gold, silver,
  the miners and the dollar always have (`CipherCProvider.ParamFasterConfirmation`). The Confirm
  level and Arm level descriptions now say what they do on the daily tier: they bound the DSS band
  a daily low is named in. The old arm-then-confirm meaning applies only to a weekly chart's own
  weekly tier and the yearly tier.
- **Why:** the original's inputs were compared one by one (CIPHER_C_DESIGN section 21). Its "Faster
  Confirmation" changes neither the hold nor the confirmation lag. It names lows on the break too.
- **Numbers (private captures):**
  - The Dow in bar replay with it on, 2022-2026: the model with the break names 41 of the
    original's 49 lows on the same bar; without the break, 22. The control is 39 of 44.
  - Final drawings, F1 against the original with it on, with the break vs without: Bitcoin 1.000 vs
    0.846, Coinbase 1.000 vs 0.923, Ether 0.946 vs 0.792, the Dow 0.857 vs 0.770.
  - Gold and GDX: no change.
- **Tests:** `Faster_confirmation_switches_the_swing_high_break_on` (Bitcoin and GDX) checks the
  provider draws the break's confirmations with it on, a different set from off on Bitcoin, and the
  same set on GDX. Two sabotages went red: the setting ignored (Bitcoin), and the setting replacing
  the preset's break instead of adding to it (GDX, along with four other tests).
- **Caveats:**
  - The S&P's long-history drawing fits neither way end to end (0.677 with the break, 0.673
    without; 0.788 with the setting off). One moved low moves the rest. Step by step, seeded at
    each of the original's lows, the break gets the next low 152 of 174 times, and 133 without it.
  - In the original, these inputs act only while its asset-adjustment switch is on; Cipher C's act
    on their own.
  - The original's DSS trigger and slow adjustments are not ported.
  - Not seen or heard in the app.

### Cipher C's pending low says the original's LOW / MID / HIGH tag, and which cancelled low it replaces (2026-10-27)

- **What changed:** on the daily tier, Cycle Low Pending gains two companions.
  - `_grade` (0 low, 1 mid, 2 high) is on every bar the daily low is held. One point is for the
    Awesome Oscillator, SMA 5 less SMA 34 of hl2, above zero. The other is for the close above the
    VWAP of hlc3 anchored at the calendar month. A month without volume gets no VWAP point.
  - `_replaced` is the cancelled low it replaces, on a pending low named after a cancellation since
    the last confirmed low.
  - The speech is now "Daily cycle low pending on day 76, mid confluence: the low of 45.50, 7 days
    ago. It replaces the cancelled low of 48.25." (`CipherCProvider.Grade`, `GradeWord`).
- **Why:** the original writes LOW, MID or HIGH on its pending daily label and changes it bar by
  bar. It marks a pending low with 🔄 when an earlier one was dropped since the last confirmed low.
  Both were read from TradingView's bar replay (private captures; study `ctz/grademodel.py`).
- **Numbers:** the C# gives the study's grade on every row.
  - Sixteen tuned replays, 2022-2026, where the rule was read: 7,486 labelled bars, 98.65% the
    same (per asset 97.3-99.4%).
  - Fourteen held out in time, 2020-03 to 2022-01: 4,874 bars, 98.75%.
  - On the bars where Cipher C holds a low too: 7,192 of the 7,486, 98.69%.
  - Other sources for both parts were worse: close 95.9%, hl2 98.1%, ohlc4 97.8%.
  - 🔄: on 880 of 884 of the original's labels it means "not the first pending low since the last
    confirmed one". `_replaced` agrees with it on 831 of 844 namings both share (2022-26) and 535 of
    548 (held out). The rest are where the drops differ.
- **Tests:** `The_pending_tag_is_the_oscillator_and_the_months_vwap` uses synthetic scenarios in
  which the close in place of hl2, no anchor, a weekly anchor, a no-volume VWAP point, or a 33-bar
  start each change the last bar's grade. `A_held_daily_low_carries_the_tag_and_what_it_replaces`
  covers the Bitcoin daily chart. `A_pending_low_says_its_tag_and_the_low_it_replaces` pins the exact
  words. The old pending-speech pattern takes the tag. Nine sabotages went red, each on its own
  test: source close, weekly anchor, no reset, the VWAP point without volume, a 33-bar start, a
  grade on every bar, a replacement kept past a confirmation, the confirmation's confluence words,
  and the replacement unsaid.
- **Caveats:**
  - About 1.3% of bars miss: the original is one grade higher on a single bar with the oscillator
    under zero (every one a lower low than the bar before). Nothing tried explains them.
  - The original's index, dollar, gold, silver and oil charts have no volume and are never HIGH. A
    data source that gives those charts volume would make Cipher C say HIGH where the original cannot.
  - A later change of tag is read on the bar, not announced.
  - The original tags no confirmed low. Cipher C's own confirmation confluence stays, as a question
    for Cody in TODO.
  - Not seen or heard in the app.

### Cipher C's daily pending low names the low it holds; the original's table checked bar by bar (2026-10-27)

- **What changed:** on the daily tier, Cycle Low Pending's companions (`_lowest`, `_lowestAgo`) are
  the low the daily tier names on that bar (`TierRun.HeldAt`), not the lowest low since the last
  cycle low. That lowest low is often before the cycle's top, and the original never takes it. The
  speech is now "Daily cycle low pending on day 76: the low of 59100.00, 7 days ago." The old words
  ("the cycle line reached oversold", "Lowest low since the last cycle low") belonged to the old
  search. They stay on the weekly and yearly tiers, which still run it (weekly charts).
- **The original's table, read in its replays** (private, `ctz/tabcmp.py`, `ctz/tabdiff.py`,
  `ctz/unc2.py`, `ctz/wconf_t.py`; Cipher C walk-forward dumps from the untracked
  `ZzCipherCDumpHarness` via `ctz/tabrun.sh`):
  - *Days since the last daily low* is the bars since it: the low's own day is 0. Cipher C's daily
    window says the day of the cycle, counting the low's own day as 1. So it is one more than the
    original wherever the lows agree, which is every row of GDX, SPX, NDQ, MSTR and the held-out
    EUR/USD and Apple (1,207-1,250 each). The window still opens on the bar the original may first
    name a low. Whether to say the original's number is a question for Cody (TODO).
  - *Days since the unconfirmed daily low* is the bars since the pending low. After a drop the
    original keeps counting the dropped low until it names a new one. With that rule, Cipher C's
    pending lows give the original's cell on 96.4% of 24,100 rows, and every row on 6 of 19 assets.
    The rest are bars where the lows differ (the daily residue). Cipher C has no such per-bar count;
    nothing is added for it.
  - *Weeks since the last weekly low*: the original confirms a weekly low only on a daily
    confirmation bar (66 of 66 on six assets), once the next weekly low is fixed. Cipher C works the
    same way, so the rows that differ are where the weekly lows differ. On the held-out EUR/USD
    (hidden weekly lows) every row agrees.
- **Tests:** `A_pending_low_names_the_lowest_low_since_the_last_one` becomes
  `A_daily_pending_low_names_the_low_it_holds`, on the Bitcoin test chart. Every pending low's bar is
  later drawn as a daily low, or cancelled at that price before the next pending low, or still held
  at the chart's end. The speech is pinned, without "oversold".
  - Three sabotages turn it red: the old lowest-since companion, the old words on the daily tier, and
    the age one day off.
  - All 221 Cipher C tests pass locally.
- **Caveats:** not seen or heard in the app. The day-count convention is unchanged pending Cody.

### Cipher C's daily tier is the original's rule: about 19 in 20 daily lows on the same bar (2026-10-27)

- **What changed:** the daily tier has its own detector, `CipherCEngine.DetectDaily`, read bar by bar
  from the original in TradingView's bar replay on candles (all sixteen tuned assets; replays and
  labels private in `strategy-lab-data/cf-tv-export`, study model `ctz/sim_dss2.py`). The old search
  trigger (`Detect(searchOnTurn: true)`, `SearchFromBeforeMin`) is gone; the weekly and yearly tiers
  still use `Detect`. With d the shortest daily cycle in bars:
  - **Naming:** once the last low is d − 1 bars back, and d − 1 bars from where its hold began, a bar
    names a low when Bressert's double-smoothed stochastic is above its 5-bar EMA and between 10 and
    75. Length and EMA per preset (Bitcoin, Coinbase, MicroStrategy 14; Ether 9; the miners, Gold and
    the dollar 8; Silver and SILJ 10; default 10; EMA 9, Bitcoin 14). Proven on the replays: the
    overbought level the original needs to still name a low is the DSS rounded up on every date tried.
  - **Second way (Gold, Silver, GDX, GDXJ, SILJ, the dollar: `CyclePreset.PivotBreak`):** a bar that
    breaks the last one-bar swing high, the first break since it formed that no close has made. On a
    GDX replay with overbought at 1 (every naming by this path) 59 of 59 named, 0 false of 461 bars.
  - **The low:** the lowest swing low after the cycle's top (the highest one-bar swing high since the
    last low, or since the bar before it was confirmed), at most 99 bars back.
  - **The hold:** half of d rounded down, from the low if it is at least d − 2 after the last, else
    from the bar before the naming bar; never on the naming bar; a low due on a bar that undercuts it
    still confirms.
  - **The drop:** a bar under the pending low within d / 3 bars of naming it (rounded up) drops it and
    the search goes on; after that the low is locked.
  - **The start:** the lowest bar of the first d bars (first of equal lows), not drawn.
- **Results (end to end against the original's final daily lows on 16 replays: started at the
  first low both have, then our own lows all the way):** 1183 of 1240 on the same bar, F1 0.951 (before 2022 0.950, after 0.953; the rules were
  read from 2022–26 replays). **Held out, never used in fitting** (untuned, so the default count of
  32): Platinum 0.939, Apple 0.985, EUR/USD 0.961 (after 2022: 0.915, 1.000, 0.981). The second way is
  per preset: switched on for Platinum it falls to 0.807. Worst tuned: Silver, Coinbase, the Dow and
  the dollar (about 0.88–0.91). The old search rule was not re-scored on these replays.
- **Weekly tier:** Ether keeps the first weekly rule without its "double" (`CyclePreset.WeeklyDoubles`
  false: a weekly low after a lower one is a new candidate, not a double), SILJ moves from the
  next-daily rule to the first rule. Scored end to end on our own daily lows (`ctz/we2e.py`), weekly
  F1 0.799 → 0.839 (before 2022 0.802 → 0.837, after 0.794 → 0.841); Ether 0.61 → 0.85, SILJ 0.61 →
  0.96, no asset worse. Fitted on 2022–26, checked before 2022.
- **Caveats:** not seen or heard in the app. Left over on the replays: 18 drops (the original keeps a
  low after an early undercut or drops one late; nothing found separates them), 7 lows named early
  (Riot, Bitcoin ×2, Ether, the dollar ×2, Gold; no common feature), 1 candidate. Untuned assets draw
  no weekly lows in the original (Platinum, Apple); Cipher C still does — not changed. The LOW / MID /
  HIGH tag and Faster Confirmation are not decoded. Prepending history now moves the weekly chain for
  longer than before (the original's chain also starts from the first bar): `CIPHER_C.WCL Confirmed`
  and the weekly timing window join `NotStableWhenHistoryIsPrepended`, and a test pins that a prepend
  moves daily lows only in the first 300 bars and weekly ones only in the first 1100 of 1400.
- **Tests:** `CipherCDailyTests` (new): bit-for-bit parity with the study on four fixture runs
  (`Fixtures/CipherCDailyStudy.cs`, candles and our outputs only, generated by `ctz/ccd_fixture.py`),
  a DSS written out independently, the flat-window 50, first-of-equal-lows seed, Bitcoin's window,
  hold and drop, the seed not drawn, the pivot break, the presets, the provider wiring, the prepend
  bounds; the weekly wiring tests moved here from `CipherCSearchTests` (deleted; the Bitcoin test now
  passes the symbol under the key the provider reads). `CipherCWeeklyTests` gains the no-double case; `CipherCHoldTests` runs on
  `Detect` with the tiers it still serves. Sabotages, all red: drop d/4, drop rounded down, the window at
  d − 2, d − 1 from the hold's start as d − 2, pivot break off, DSS length/EMA swapped, seed drawn, drop before confirm,
  trigger EMA 4, hold-from strict, back 30, break by a high, top tie to the earlier, seed last of
  equal lows, flat window 0, Ether doubles on, the double and S-lower gates, SILJ on next-daily, the
  provider ignoring `WeeklyDoubles`. README 9657 tests (the doc guard's `--list-tests` count; 9662, first written here, was a run total).

### Cipher DBSI rebuilt from every rule found: about 19 in 20 exact (2026-10-26)

- **What changed:** the score is a new class, `DbsiScoreModel`, a line-for-line port of the study's
  `strategy-lab-data/dbsi-study/dbsi8/clean_dbsi.py` (private; ported from its frozen copy `clean_dbsi_port_v5.py`, identical to it). `DbsiStyleProvider` keeps its components,
  settings, events, trend line and Heikin-Ashi handling and now calls `DbsiScoreModel.Score`. gen8's
  model, settings record and terms are gone.
  - The base: −5 + 2 × (ALMA 9 rising, the 40-bar and 81-bar stochastics rising, wt1 rising, Stoch RSI,
    money flow rising) + 1 for the WaveTrend gap widening. The close against the close 2 back (V0) no
    longer scores by itself; it gates several rules.
  - The Stoch RSI vote: the 2-bar mean of raw Stoch RSI (12, 12) against its value 2 bars back; where
    that is flat, K's slope, then K's side of 50; a flat K on the wrong side still votes when raw Stoch
    RSI stayed off its pin and RSI moved that way a bar ago.
  - Three states per side: off a run (the streak, the Q state at 4, Stoch RSI terms), alive (the level,
    3 + 1 a bar + the ARM, terms read at the cross), dead (the level, the streak, a held step that raw
    Stoch RSI re-arms, holds or drops). Bulls and bears run the same code on mirrored prices; the side
    numbers stay (deep level 7/2, step 2/3, broken close-up 2/3).
  - **The ARM**, one memory per side carried through an alive run, replaces a dozen alive-bar patches. At
    the cross it takes the Q value of the bar before if Q was on, else the hidden arm (bulls +1 when K ≤ 6);
    it is 0 if raw Stoch RSI fell over 2 bars (bulls: did not rise). Any later fall drops it; a later rise
    re-arms it at the step (bulls 2, bears 3) **+1 when s2, the mean of raw Stoch RSI and its value a bar
    before, is at or below 60 for bulls or 40 for bears**. The s2 rule is exact on the study's rows (bulls 0
    misses in 1,885, bears 5 in 1,582); thresholds on K were a worse stand-in. The dead run's re-arm uses the same rule.
  - **Kept**: the bear zero-line bonus (4 or 6), the near-zero bonus, the continuation cross and the Q-turn
    +2 count only while the arm has not been dropped since the cross. The bear cross terms and the two
    wide-gap +2 terms stay unconditional.
  - **The bear near-zero flag** on dead bars replaces a one-bar +2: when the bear side's WaveTrend climbs
    by 5 or more into −15 to 10 with the gap and ALMA votes on and raw Stoch RSI not falling, +2 is held
    until raw Stoch RSI falls (not set on a flat bar that reads −2). The truth's dead-run +2 stretches came
    from it: bear dead misses 8.2% → 7.0% of bars, +51 main, +75 veto sets, none worse.
  - **A fifth way into the bear zero-line bonus**: with no cross term, the bear side's WaveTrend jumps
    by 10 or more into −32 to 5 with a gap of 10 or more, raw Stoch RSI 95 or over and the bear-side
    RSI 52 or over. Of the cross bars where the truth had the bonus and we did not, most were this deep
    jump; its look-alikes had a narrower gap, a cross term or a lower RSI. +10 main, +25 veto sets,
    none worse; the bonus trigger's errors 115 → 104.
  - **Three rules removed** by leaving each of 54 out in turn (each removal gained exact matches): the bull
    Q start on a 3-bar high, the bear "barely crossed" exception, and the bear zero-cross 5-or-6 split
    (+10 main, +7 veto sets together).
  - About forty corrections of 1 to 6 points, each found on DBSI's own numbers where the model missed
    (CAND10 to CAND114 in the study), then cleaned: six simplifications that cost nothing, and every
    constant rounded where rounding lost nothing (the whole rounded model is +1 exact on the study).
  - Raw Stoch RSI is rounded to 9 decimals so its 0 and 100 pins compare equal on both sides; sums
    follow numpy's pairwise order; NaN handling follows Python's (`min` keeps the left value).
- **How it was chosen:** a rule was kept only if it lost no exact matches on any of nine main sets
  (the twenty, the fifteen, the vendor's 15-minute image, the Dow and Bitcoin daily Start grids, the
  hourly discovery and held-out weeks, raw Bitcoin daily and raw Bitcoin hourly), then on ten veto
  sets never used to find a rule (gold, Apple, the Dow, the S&P 500, Ethereum daily; Bitcoin raw and
  Heikin-Ashi 4h; Ethereum 4h; Bitstamp Bitcoin 4h and 1h). All are DBSI's drawn labels, never its
  inputs, kept out of this repository.
- **How close it is** (exact / within 1 / within 2 / typical error):
  - Three sets harvested after the ARM was built and never used to choose anything: Microsoft 15-minute
    2822 of 2996 (94.2%), Bitstamp Bitcoin 5-minute 2858 of 3000 (95.3%), 1-minute 1326 of 1410 (94.0%);
    the model before the ARM had 2725, 2742, 1278 (before the near-zero flag 2814, 2845, 1326; before
    the deep-jump zero-line way 2822, 2856, 1326).
  - The ten veto sets: 94.5–97.5% exact (pooled 95.4% of 37,032; before the ARM 91.6%), 96.4–98.4% within 1,
    98.5–99.5% within 2, error 0.43–0.63. Bitstamp 4h 95.7 / 97.3 / 98.9 / 0.54, 1h 95.0 / 97.4 / 98.8 / 0.54.
  - The hourly held-out weeks (2026-09-07 to 10-24): gen8 59.6 / 71.7 / 85.6 / 1.81 → **96.1 / 97.7 / 99.2 / 0.61**.
  - The fifteen: 48.5 / 58.7 / 78.1 / 2.80 → **83.7 / 86.2 / 89.6 / 2.26**. The twenty: 55.1 / 66.7 / 80.4 /
    2.16 → **84.8 / 88.3 / 93.3 / 1.26**. These are read by eye; part of what is left is reading error.
  - Side in control: the twenty 399 → 418 of 422, the fifteen 247 → 256 of 262.
  - 17+: the fifteen 38 → 45 of 48 printed, false 6 → 3; the twenty 65 → 77 of 84, false 12 → 5.
  - Bears still miss more often than bulls (alive bars 9.2% against 3.2%, dead 7.0% against 3.5%).
  - **Daily charts harvested after the model was settled (2026-10-27), each with its mirror twin**
    (the spread `K − X`, price upside down, so the original's bull on the twin is its bear on the
    plain chart; the first 200 bars and the last bar left out). Held out (BNB, Tesla, Amazon, US oil,
    plain and mirrored): 12,796 bars a side, **94.4% exact** (bulls 96.0, bears 92.8), 97.9% within 2;
    gen8 56.5%. Unseen markets (Solana, silver, Nvidia, the euro, plain and mirrored): 11,119 a side,
    **94.2% exact**, 98.1% within 2. The Solana twin was stopped at 900 bars (bulls 97.1, bears 93.3).
    The twins of the veto sets score the same: the Dow, S&P 500 and gold mirrors bulls 95.1–98.1,
    bears 92.2–93.3.
- **Tests:** the fixture is regenerated (`dbsi8/zz_export_clean.py MODEL.py`; `--check` diffs against the
  fixture on disk, `--charts` also rewrites the printed Bull/Bear arrays in `DbsiDigitisedCharts`), gen8 now as Previous. New
  `DbsiScoreModelParityTests`: on all 35 charts every bar's bull and bear score equals the study's
  (`DbsiCleanScores`, full length, warm-up null), bit for bit. The golden tests drop the gen8 model's
  tests (one-step settings, hand-scored rows, the Q state on hand-made bars, the close-up and Stoch RSI
  vote tests) and gain a numpy pairwise-sum test (9 values sum to 6, not 7; 132 values to 124, not
  130); the WaveTrend run test moves to `DbsiScoreModel.WaveRun`. The fidelity tests pin the new
  numbers and clean ≥ gen8 + 10 points exact on both sets. Sabotages: raw Stoch RSI unrounded (21
  red), the 17.34 threshold at 12 (33 red), a running-total sum (the sum test red; parity stays green:
  no score on the 35 charts depends on the order); on the ARM port, the bull s2 threshold 60 → 61
  (2 charts red), the bear 40 → 41 (2), the kept gate on the bear zero-line terms always on (3), the arm
  never dropped as kept (3), the near-zero flag without its ALMA gate (1), the deep-jump zero-line way
  dropped (1) or without its RSI gate (5). The 35 charts never reach four rules, so two windows of
  real daily candles were added (`DbsiCoverageCharts`: gold 2004-05 and the Dow 1948-49, 300 bars
  each, candles and our scores only; the generator refuses to write them unless each broken rule
  changes a score there). The C# matches the study on both, bit for bit. Each of these now turns one
  window red: the near-zero flag's rise of 5 moved to 6, its flat-bar exception dropped (gold), the
  36.5 threshold at 30, and 17.34 → 17.4 (Dow). Still not caught: 36.5 → 36.6, which changes no
  score on any of the ~67,000 harvested daily bars of gold, the Dow and the S&P.
  README 9644 tests.
- **Caveats:**
  - **The veto sets are not untouched.** They could reject a rule and did (CAND110 to 113), and the
    ARM's s2 rule and the three removals were kept only where no veto set got worse. The Bitstamp sets
    came last and score like the rest; the Microsoft 15-minute and Bitstamp 5- and 1-minute sets were
    harvested after the ARM and are the only truly held-out numbers here.
  - **Many rules are narrow** (a few points on one kind of bar), found on Bitcoin first. They held on
    gold, stocks and Ethereum, daily to hourly, and on the three held-out sets down to 1 minute.
  - **What is left is timing, not a missing indicator**: raw Stoch RSI leaving its pin a bar early or
    late, and the dead-run step that holds on it.
  - Not seen or heard in the app.

### Cipher DBSI: a 40-bar stochastic, a Stoch RSI vote and the Q state (2026-10-25)

- **What changed** (`DbsiStyleProvider`, study `strategy-lab-data/dbsi-study/dbsi8/`, standalone
  `model8.py`, fixture `gen8.py`):
  - The third base vote's stochastic is 40 bars (was 21): SMA2 of the 40-bar stochastic of close, rising.
  - The sixth base vote is Stoch RSI rising (was the previous close above the close before it):
    RSI 12 (Wilder), its 12-bar stochastic, K its 3-bar SMA. Where K has not moved (a pinned
    stochastic of RSI) it votes by its side of 50. Bears are the mirror.
  - **The Q state, worth 4.** This is the value DBSI's Start grid showed (2026-10-23) setting on a close up
    and holding while the close stays beyond the close 2 back, whose trigger was unresolved. Off a run,
    at least 2 bars after the side's run ended, a close beyond the previous close with the side's 11-bar
    stochastic (SMA3) below 50 turns it on. It holds while each close is beyond the close 2 bars back.
    A run turns it off.
  - The off-run close-up (1 point per bar since the run ended, up to 4, from 76aebc5c) is gone. With the
    Q state it scored worse than nothing. The broken-run close-up (bulls 2, bears 3) stays.
  - The highest scores stay 31 and 27. Off a run the top is now 21 (every vote, a ten-bar streak, Q).
- **Where it came from:** the hourly misses after 5d7a6bb4, on the discovery bars (2026-01-01 to
  09-06). The Q state was found first (`t3.py`). Then each base vote was tested for replacement by
  other oscillators (`u1.py` to `u25.py`). The 40-bar stochastic and Stoch RSI were the two that
  helped. The tie rule (a flat K by its side of 50) came from the bars where they still missed.
  Then two terms became redundant: the off-run close-up, and a "transfer" term tried along the way
  (Q with a falling stochastic, and the other side's Q in a broken run). Both are left out, because
  without them every set scores the same or better.
- **How close it is** (exact / within 1 / within 2 / typical error; gen7 → gen8):
  - Hourly held out (2026-09-07 to 10-24): 44.0 / 57.5 / 81.9% / 2.11 → **59.6 / 71.7 / 85.6% / 1.81**.
    Bears 43.7 / 53.2 / 79.2 → 62.2 / 68.8 / 82.1; bulls 44.3 / 61.9 / 84.6 → 57.0 / 74.6 / 89.1.
  - The fifteen livestream charts: 36.8 / 50.0 / 77.1% / 2.78 → **48.5 / 58.7 / 78.1% / 2.80**.
  - The twenty: 40.2 / 54.4 / 77.4% / 2.29 → 55.1 / 66.7 / 80.4% / 2.16.
  - Private grids: the Dow 48.8 / 62.9 / 85.5% → 63.1 / 75.7 / 87.6%; Bitcoin daily 44.5 / 57.9 / 82.4% →
    60.4 / 72.2 / 86.4%.
  - Readings that became exact against those that stopped being exact: hourly held out 442 / 83, the
    fifteen 115 / 44, the twenty 175 / 42, the Dow 394 / 73, Bitcoin daily 530 / 79.
  - 17+ printed where DBSI printed 17+: the fifteen 35 → 38 of 48, hourly held out 151 → 166 of 222.
    False 17+: the fifteen 6 → 6, the twenty 12 → 12, hourly held out 14 → 18, the Dow 25 → 30,
    Bitcoin daily 22 → 23.
  - Side in control: the fifteen 243 → 247 of 262, the twenty 396 → 399 of 422.
- **Tests:** the fixture is regenerated by gen8.py and carries gen7's numbers as Previous. The
  provider reproduces the study's numbers on all 35 charts. The one-step test drops the off-run cap and gains Q
  points, Q bars after the run, the stochastic length and the Stoch RSI smoothing; every neighbour scores lower
  on the twenty (1.3545). New tests: the Q state on hand-made bars, and the Stoch RSI vote (exactly one
  side votes on every bar once K is defined). The fifteen's test pins exact at least 10 points and ±1 at least
  5 above gen7, ±2 above it, the typical error within 0.05 of it. Seven sabotages went red: stoch 21 back,
  the previous-close vote back, a flat K casting no vote, Q worth 3, Q never lapsing, Q ignoring its
  stochastic, the off-run close-up back.
- **Caveats:**
  - **The fifteen's typical error did not fall (2.78 → 2.80).** The misses it keeps are a little bigger.
  - **The hourly held-out set was watched.** It was scored at each step of this search (to decide
    whether to keep going), so it is no longer untouched. The fifteen, the Dow and Bitcoin
    daily were scored only at the start and the end.
  - **False 17+ rose a little on the hourly held-out set and the Dow** (14 → 18, 25 → 30), while true
    17+ rose more.
  - Some lengths are not sharp. RSI 11 to 13 and the Q stochastic 10 to 12 score within a point of each other,
    and the Q midline is flat from 45 to 55. The usual 14/14 Stoch RSI is worse on every set.
  - About half the hourly misses left are a side in a run while the other side is off a run and
    not in the Q state; a broken run is often one short. Not explained.
  - Not seen or heard in the app.

### Cipher DBSI: in a broken run a bear close down counts 3 (2026-10-25)

- **What changed** (`DbsiStyleProvider.BearRun`, study `strategy-lab-data/dbsi-study/dbsi7/gen7.py`):
  in a broken run (a WaveTrend run whose gap has stopped widening), a close beyond the previous close
  is worth 3 for bears. Bulls stay at 2. Off a run, and in an alive run, nothing changed. The
  highest possible scores stay 31 and 27.
- **Where it came from:** the hourly residuals (`hres2.py`, `hbrk.py`). The bears' broken runs sat
  under DBSI's number by about 4 on the first bar after the break, fading after that, in whole
  even steps. A free least-squares fit of the close-up weight gave bears 2.9 and bulls 1.8.
  Refitting both sides' run settings together on the 5,826 discovery bars (`hjoint.py`) moved only
  this one setting. 76aebc5c had tried 3 on both sides and rejected it; giving bulls 3 as well still
  scores worse.
- **How close it is** (exact / within 1 / within 2 / typical error; was → now):
  - Hourly held out (frozen 2026-09-07 to 10-24, never chosen on): 41.9 / 57.2 / 81.2% / 2.16 →
    **44.0 / 57.5 / 81.9% / 2.11**. Bears alone: exact 39.4 → 43.7%, ±2 77.8 → 79.2%.
  - The fifteen livestream charts: 36.2 / 50.2 / 78.0% / 2.80 → 36.8 / 50.0 / 77.1% / 2.78.
  - The twenty: 39.1 / 54.1 / 77.8% / 2.31 → 40.2 / 54.4 / 77.4% / 2.29.
  - Private grids: the Dow exact 47.2 → 48.8%; Bitcoin daily exact 42.4 → 44.5%, ±2 81.7 → 82.4%.
  - False 17+ unchanged on every set. Side in control unchanged (243 of 262 on the fifteen).
  - Bar by bar, readings that became exact against those that stopped being exact: hourly held out
    62 / 13, the fifteen 10 / 6, the twenty 15 / 5.
- **Tests:** the fixture is regenerated by gen7.py and carries gen6's numbers. The fifteen's test
  now pins exact and typical error above gen6 and ±2 within a point of it, plus gen5's floor. The
  broken-run test has a bear line. Every one-step neighbour scores lower on the twenty (1.1761;
  bear close-up 2 gives 1.1695, 4 gives 1.1672). Four sabotages went red: bears back to 2, the
  sides swapped, bulls 3 too, and bull settings used for both sides.
- **Caveats:**
  - **Within 2 drops a little on the fifteen (78.0 → 77.1%) and the twenty (77.8 → 77.4%)**, and
    ±1 on the fifteen 50.2 → 50.0%. The gain is exactness, and the bars it pulls ±2 off are few.
  - Why the bears' broken run differs is not known. It is the second bull/bear asymmetry, after the
    deep cross. The hourly set is still one market in one year.
  - Not seen or heard in the app.

### Research notes, no code change: Cipher C's weekly tier on weekly charts (2026-10-25)

- **What was read** (private captures cf-tv-export cap5 and cap9, ten weekly charts; scripts
  `ctz/w1w.py` and `ctz/wgrid.py`; CIPHER_C_DESIGN section 18, end):
  - The original's table counts on a weekly chart are in bars. "Weeks Since Last WCL" is the bars
    since the weekly low divided by the preset's week (5, or 7 on Bitcoin and Ether), rounded down.
    The confirmed count runs from the weekly low before the newest, as on the daily chart. This holds
    on 8 of 9 charts; Silver is not explained.
  - Given the original's own daily lows, the dollar/Dow/Nasdaq weekly rule draws 7 of 9, 19 of 22
    and 17 of 21 of its weekly lows on weekly charts. So the weekly misses there come from Cipher C's
    daily lows.
- **Tried and not adopted:** Ether on the every-two-daily-lows rule. It fits the original's weekly
  chart (9 of 9 on its own daily lows; 2 of 9 for the old pick). End to end the weekly chart goes
  2 → 6 of 9 and daily 33 → 34 of 57, but 4-hour falls 38 → 32 of 54 and there are 3 more extras.
  The engine is unchanged.
- **Not decoded:** oil's weekly chart. The old pick is 55 of 57 on oil's daily chart but 14 of 23
  on its weekly one, whatever its gap.

### Hyperliquid research: can the terminal trade on it, with leverage? (2026-10-25)

- `docs/HYPERLIQUID_RESEARCH.md`, saved from the Claude Doc
  https://claude.ai/artifact/ArWAdoRPAWy62CMESyVmgD. Research only. No code or tests changed.
- Leverage, from the live `meta` API on 2026-10-25: BTC 40x, ETH 25x, SOL and XRP 20x, most of the
  178 crypto perps 3x to 10x. HIP-3 markets: stocks 20x, EUR and JPY 50x.
- Verdict: technically very feasible. The API fits the Kraken Futures provider pattern, and an
  agent key can trade but cannot withdraw. Legally it is not open to a US resident today; the terms
  bar US persons, and the Payward/Bitnomial US route is not live yet.
- Caveat: the terms page refused automated requests, so the US restriction comes from secondhand
  guides and the CEO's own statement. Candle depth was measured on BTC only.

### Cipher C off the daily chart counts in the chart's bars, as the original does; a two-bar session is no longer read as daily bars (2026-10-25)

- **What changed** (`CipherCProvider.Compute`, `InBarsKey`, `PresetWords`; `CipherCEngine.MeasureBars`;
  study `strategy-lab-data/cf-tv-export` cap5, cap9, `ctz/ccrun.sh`; CIPHER_C_DESIGN section 18):
  - On any chart that is not daily (weekly, 2-day, 4-hour, hourly), Cipher C counts each bar as one
    day of the preset, on the preset's own calendar: the Dow's 36 to 44 days are 36 to 44 bars, its
    22 to 31 weeks 110 to 155 bars. It used to count time (a 4-hour Bitcoin chart's 54 days were 324
    bars), and a weekly chart had no daily cycle at all.
  - A weekly chart now has daily lows (D), with the weekly lows picked from them as on a daily chart.
  - Off daily, every count, age, cycle length and window is said in bars ("daily cycle bar 12, in
    its window, 8 bars left", "a 48 bar cycle, confirmed 24 bars later"), and the preset line ends
    with "Not tuned for this timeframe: its days are counted as this chart's bars; switch to daily
    for the tuned count". The Daily cycle shortest setting is in bars there too.
  - `MeasureBars`: a market that does not trade on weekends is judged by its bars per date whenever
    its bars are under a day and a half apart. With two bars a session (TradingView's Dow 4-hour
    chart, 13:30 and 17:30 UTC) half the gaps are the 20-hour night, so the median gap sat exactly on
    the 20-hour cut-off and the chart read as one bar a day.
- **Why:** Cody, 2026-10-25: "copy the original: goal is 100% accurate replication". Off daily the
  original says "Timeframe Not Optimised, Switch to Daily" and runs its daily count in the chart's
  bars: the count that best fits its labels is the Dow's 36 on weekly and on 4-hour (preset 36),
  gold's 23 on weekly (22), the miners' 21 (22), Bitcoin's 51 on 4-hour (48).
- **Against the original's own labels** (same charts the rule was read from):
  - 4-hour daily F1: gold 0.22 → 0.748, Bitcoin 0.245 → 0.729; the Dow (0.836) and GDX (0.782)
    unchanged, since the bug already read them as daily.
  - Weekly charts, weekly F1: Dow 0.365 → 0.773, gold 0.214 → 0.889, GDX 1.000 (3 of 3); daily lows
    (none before) Dow 0.755, gold 0.846, GDX 0.960. Bitcoin's weekly capture has 3 labels: too few.
- **Held out** (cap9, captured after the rule was written: Nasdaq, silver, Ether, Coinbase, oil,
    the dollar index on weekly and 4-hour, scored on both builds; pooled F1):
    - 4-hour: daily 0.398 → 0.749, weekly 0.300 → 0.558. Silver, Ether, oil and the dollar gain
      (daily 0.23-0.33 → 0.75-0.83); the Nasdaq and Coinbase (two bars a session) are unchanged.
    - Weekly charts: daily lows (none before) 0.763 (121 of 158, 38 extra). Weekly lows 0.332 →
      0.400, but mixed: the Nasdaq 0.30 → 0.48, silver 0.27 → 0.80, oil 0.32 → 0.41 better; Ether
      0.33 → 0.20, Coinbase 0.25 → 0 (2 labels), the dollar 0.51 → 0.38 worse. Recall fell 0.76 →
      0.39 as the extras fell 185 → 38: the weekly tier on a weekly chart is not yet the original's.
- **Caveats:**
  - This reverses an older rule: a weekly chart had no daily cycle (from a wish about Loukas's
    weekly chart, before Cipher C was rebuilt on the original). The original draws them.
  - The weekly cycle's numbers off daily are five or seven bars a "week" of the count, said in bars;
    the original's "Weeks since last WCL" off daily is not decoded.
  - Tests: three that pinned the old weekly chart (no daily cycle, time-counted weekly window, the
    arc on the weekly window) now pin the bar count; new tests for a 4-hour two-bar-session chart, a
    daily chart's words, and two-bar sessions in `MeasureBars`. Sabotages red: counting time again,
    day words off daily, the weekly window in weeks, no warning, the old `MeasureBars`.
  - The causality guard's 400 hourly bars now produce Cipher C's daily and weekly events (they
    produced none while an hourly chart counted time), and those seven pass the prefix check; they
    are off its blind-spot list (only the yearly low stays on it).
  - Not seen or heard in the app.

### Cipher C: an ETF or future no longer takes its index's preset (2026-10-25)

- **What changed** (`CipherCEngine.Aliases`; study `strategy-lab-data/cf-tv-export` cap6-cap8,
  `ctz/ccrun.sh`; CIPHER_C_DESIGN section 17):
  - SPY, VOO, IVV, ES, QQQ, NQ, DIA, YM, UUP, DX, GLD, GC, IAU, SLV, SI, USO, CL, ETHA, ETHE, IBIT,
    FBTC and GBTC no longer map to the S&P, Nasdaq, Dow, dollar, gold, silver, oil, Ether and
    Bitcoin presets. They get the default count (32 bars), as any other symbol does.
  - IXIC no longer maps to the Nasdaq 100 preset: it is the Nasdaq Composite, another index.
  - Kept: other spellings of the same instrument (^GSPC, US500, SPX500, NDX, US100, NAS100, US30,
    DJIA, XAU, XAG, WTI, OIL, XBT, BITCOIN, quote suffixes such as BTCUSDT).
- **Why:** the ETF and future aliases were the replica's own idea (8c2f8b00), not the original's.
  The original says "Unoptimised Asset Detected" on BATS:SPY, BATS:QQQ, BATS:DIA, CME_MINI_DL:ES1!
  BATS:GLD, BATS:SLV, BATS:USO, BATS:UUP, BATS:IBIT and COMEX_DL:GC1!, and runs its default count there. Cipher C's default matches its lows better than the
  index preset on each one read: SPY daily F1 0.710 → 0.758, QQQ 0.645 → 0.706, ES1! 0.658 → 0.735.
- **Caveats:**
  - The original tunes one feed per asset, not the asset. It also calls COINBASE:BTCUSD,
    BINANCE:BTCUSDT, COINBASE:ETHUSD, OANDA:XAUUSD, TVC:SPX and NASDAQ_DLY:NDX unoptimised. Cipher C
    still gives those instruments their presets on any feed. Cody, 2026-10-25: keep it per asset.
  - The backtest in CIPHER_C_DESIGN section 8 ran SPY, QQQ, DIA and GLD on the index presets. It was
    not re-run.
  - Tests: the symbol table's cases now say SPY, DIA, QQQ, ES, GLD, IBIT and ^IXIC have no preset, and
    US30 and NDX do. Three tests that used SPY or IBIT to reach a preset now name it. Sabotages red:
    SPY put back, IXIC put back, US30 dropped.
  - Not seen in the app.

### Cipher C on a symbol without a preset: the original's default count of 32 bars (2026-10-25)

- **What changed** (`CipherCEngine.Fallback`, `DefaultCountDailyMin`; study
  `strategy-lab-data/cf-tv-export/ctz/unpreset.py`, `ccrun.sh`, `wsim5.py`, CIPHER_C_DESIGN section 17):
  - A symbol none of the sixteen presets covers gets the S&P's ranges and weekly rule, counted from
    32 bars. Stocks were counted from the S&P's 36. Charts that trade every day used Bitcoin's preset
    (counted from 48, a different weekly rule).
  - The 32 is bars on either clock: a coin is no longer stretched by 7/5.
  - The spoken preset says "no preset for this symbol, so the default count: daily lows from 32 bars, …".
- **Where it came from:** the original's own final drawings on 17 daily charts it does not tune. Its
  table reads "Unoptimised Asset Detected" and "Default Count Selected" on each. The 17 are seven
  stocks, SPY, QQQ, three coins (SOL, XRP, LTC), EURUSD, USDJPY, Brent and the Nikkei, plus SP:SPX
  from before.
  - Its shortest gap between daily lows is 18 to 23 bars on every one; the best count in the replica
    is 26 to 33.
  - The 32 was chosen on AAPL, TSLA, NVDA and MSFT. The other 13 were only scored.
- **How close it is**, on the 13 held out (Cipher C's own lows against the original's):
  - Daily lows on the original's bar: 1,007 → **1,170** of 1,529. Extra lows 341 → 373. F1 0.700 →
    **0.762**, better on all 13. The coins gain most: SOL 0.50 → 0.65, XRP 0.61 → 0.77, LTC 0.60 → 0.78.
  - Weekly lows: 336 → 373 of 673. Extra 211 → 252. F1 0.551 → 0.575, better on 9, worse on 4 (XOM,
    USDJPY, Brent, Nikkei).
  - SPY and QQQ are scored as the original treats them, unoptimised. Cipher C still sends them to
    the S&P and Nasdaq presets, which score worse there: SPY daily 0.710 against 0.758, QQQ 0.645
    against 0.706 (TODO).
- **Not changed:** the weekly gap stays two daily counts less two bars each (60). A gap of 56 fits the
  original's own daily lows better, but with Cipher C finding its own it is a wash: mean weekly F1
  0.584 against 0.582, 3 better and 3 worse.
- **Tests:** the fallback test pins 32 on both clocks, the chart's clock and the weekly rule. The
  preset-words test pins the new sentence.
  - `CipherCSearchTests` passed the symbol under a key the provider does not read. It only got
    Bitcoin's preset through the old fallback. It now passes `__symbol`.
  - Four sabotages went red: a count of 36, the 7/5 stretch back, Bitcoin's preset back for coins,
    and the old sentence.
- **Caveats:**
  - The default's weekly range is not shown by the original, so the S&P's 22 to 31 weeks is said.
  - Only daily charts were checked.
  - The original's drawings are private captures, not in the repo.
  - Not seen or heard in the app.
  - The accessibility-lead agent the project hook asks for is not registered. This is engine math
    with one spoken sentence, so it went ahead without it.

### Cipher DBSI: bulls and bears score their runs differently, and a close up off a run grows (2026-10-25)

- **What changed** (`DbsiStyleProvider`, study `strategy-lab-data/dbsi-study/dbsi7/gen6.py`):
  - A deep bull cross (wt1 below −60) is worth 7, was 3; a deep bear cross 2, was 3. While the run
    is alive, bulls add 6 (unchanged) and bears 7 (was 6), plus the count.
  - Off a run, a close beyond the previous close is worth one point per bar since the side's run
    ended: 0 on the first bar out, up to 4. It was a flat 2. In a broken run it stays 2.
  - The highest possible score is now 31 for bulls, 27 for bears. DBSI's highest seen is 27, and the
    model's highest on every set is 27.
- **Where it came from:** DBSI's own numbers on Bitcoin 1-hour Heikin-Ashi, read from the vendor's
  script with the End trick (7,127 bars, 2026-01-01 to 10-24). The settings were chosen on the 5,826
  bars before 2026-09-07; the last 1,151 were frozen and only scored. The full harvest picks the same
  settings that the first 2,500 bars did. Every setting one step either way scores lower on the
  hourly set and on the twenty digitised charts.
- **How close it is** (exact / within 1 / within 2 / typical error):
  - Hourly held out: 36.4 / 51.6 / 75.9% / 2.53 → **41.9 / 57.2 / 81.2% / 2.16**.
  - The fifteen livestream charts, which no choice saw: 31.2 / 45.2 / 71.5% / 3.07 → **36.2 / 50.2 /
    78.0% / 2.80**. 17+ printed on 35 of DBSI's 48, was 21.
  - The twenty: 34.6 / 47.4 / 74.8% → 39.1 / 54.1 / 77.8%.
  - Side in control: about the same (243 of 262 on the fifteen, was 242).
- **Tests:** the fixture is regenerated by gen6.py and carries the previous model's numbers. The
  golden tests pin the new fidelity against both older models. The score and run tests cover the
  side split and the off-run count. Six sabotages went red: sides swapped, the count starting at 1, no
  cap, a broken run on the off-run rule, an uncounted run's end starting the count, and the flat 2.
- **Caveats:**
  - The hourly set is one market on one timeframe, in one year.
  - **False 17+ readings doubled:** 3 → 6 on the fifteen, 8 → 14 on the hourly held-out.
  - Why bulls and bears differ is not known.
  - The hourly labels stay private (the same question as the grids). That is why the repo's tests
    cannot replay the choice itself.
  - Not seen or heard in the app.

### The Market Cipher tests re-run on the exact Ciphers: still No edge (2026-10-25)

- **What ran:** the four pre-registered tests of 2026-10-17 and 10-18 (Cipher A battery, setup
  grader rounds 1 to 3). They were run again, unchanged, on the Cipher A, B and SR that now match
  Market Cipher's own plotted values. The plan was written first
  (`docs/EXACT_CIPHERS_RERUN_PREREG.md`); results in `docs/EXACT_CIPHERS_RERUN_FINDINGS.md`.
- **Verdicts:** all four say No edge again, in every class. Nothing passes its own correction.
  - Round 1: the equities grade-C dot still passes inside round 1 (Holm 0.049, was 0.011); the veto
    no longer does.
  - Round 2: the equities trigger entry is now Suggestive (p 0.024, Holm 0.145). The equities exits
    still beat random exits.
  - Round 3: 0 of 29 pass, none Suggestive (was 4).
  - Battery: 0 cells survive; best pFW 0.11 (was 0.38).
  - Round 3's correction over all three rounds' 119 tests passes nothing. Round 2's exits now enter
    at their raw p; on 10-18 they entered at 1.
- **StrategyLab:**
  - `setup-grader-mc3 --priors exact` reads rounds 1 and 2 as re-run.
  - A lab-wide `--cipher-candles ha` computes the three Ciphers as on a Heikin-Ashi chart
    (exploratory; rounds 1 and 3 refuse it).
  - Tests for both, with sabotages red.
- **Exploratory, Heikin-Ashi:** round 2 and the battery once each; nothing. The crypto 4h trigger
  entry reaches +0.338R on 141 (p 0.0024) in development, but is +0.037R on 233 from 2023 on.
- **Caveats:**
  - The data, the held-out era and the old answers were all known before this run.
  - Round 3 lost most of its setups at support (crypto 4h dot 128 → 41), so its crypto daily
    location tests are not testable and equities has no held-out check.
  - Not seen in the app; nothing in the app changed.

### Cipher B's money flow is Market Cipher B's exactly (2026-10-24)

- **Formula:** d = typical price (high + low + close) / 3 less its 5-bar mean; money flow =
  200/3 × the 60-bar mean of d ÷ (the 5-bar mean of |d|), clamped to ±100, on the chart's own candles
  (Heikin-Ashi on a Heikin-Ashi chart, made once from the real candles). Was 68 × the 60-bar mean of
  each Heikin-Ashi body ÷ the 5-bar mean body size (2026-10-23). The open plays no part.
- **Checked against Market Cipher B 2.4.0's plotted money flow:** equal to the five decimals
  TradingView prints on every finished bar of TVC:DJI 1D since 1896 (32,585 bars), BTCUSD 1D on
  candles and on Heikin-Ashi (5,446 each) and BTCUSD 15m (5,009; only the still-forming last bar
  differs, by 0.02). The first value is on bar 68 (index 67), the original's: 4 + 4 + 59 bars of
  warm-up. That explains the "starts 4 bars late" left open on 2026-10-23, and why the original
  barely changes between candle and Heikin-Ashi charts.
- **How it was found:** the 60-bar mean makes each step of the original's line 1/60 of the newest
  input less the one 60 bars back, so its per-bar input was recovered up to one offset per bar of
  the 60 (fitted). That input, regressed on the last ten candles' prices, weighed high, low and close
  alike, the open almost nothing, and the four bars before a quarter each against the newest, nothing
  older: the typical price less its 5-bar mean. The scale came out 66.667 on every series (200/3), not
  assumed.
- **Cipher A's Yellow Diamond**, which reads it: against Market Cipher A, 0 bars differ on BTCUSD 1D
  Heikin-Ashi (was 4) and candles (was 3); TVC:DJI 300/300 still exact. The Cipher A goldens did not
  change (golden5.py regenerates them with this formula; the same 226 diamonds).
- **Cipher DBSI's money-flow vote** reads it too: on the fifteen livestream charts no fit saw, exact
  31.6% → 31.2%, within 2 70.6% → 71.5%, error 3.14 → 3.07, false 17+ 4 → 3, side in control 241 →
  242 of 262; on the twenty, exact 33.1% → 34.6%, within 1 45.8% → 47.4%, within 2 73.5% → 74.8%,
  error 2.65 → 2.58, 17+ printed 35 → 37 of 84, side in control 393 → 394 of 422. The same settings
  still win (intfinal.py re-run; every one-step neighbour lower). Fixture regenerated (gen5.py, the
  study's MF vote now defaults to the exact formula). Private Start grids (not in the repo): Dow
  within 2 78.8% → 79.2%, Bitcoin 75.8% → 76.6%.
- **Tests:** four money-flow tests replace four: only the typical price counts (moving the opens or
  stretching both wicks changes nothing, raising one high does); 200/3 on steady candles of any size;
  the formula restated with its first value on index 67; on a Heikin-Ashi chart it reads the
  Heikin-Ashi candles' typical price. Fingerprint re-pinned for the money-flow line only. Eight
  sabotages red: scale 68, 4 or 6 bars, the close for the typical price, hlcc4, squared for the size,
  a 61-bar mean, raw candles on a Heikin-Ashi chart.
- **Caveats:** five unchanged typical prices in a row would give 0 / 0; ours reads 0 there, the
  original's was never seen there (no such run in any series read). Legacy mode unchanged. Not seen
  in the app.

### Cipher DBSI scores in whole-number votes, as the original does (2026-10-24)

- **Formula:** the fitted weights are gone. Each side starts at −5 and adds whole points:
  - 2 each for the close above the close 2 back, the previous close above the one before it, the
    4-bar EMA rising, the 21-bar stochastic (SMA 2) rising, WaveTrend rising and Cipher B's money flow
    rising; 1 for the WaveTrend gap (WaveTrend minus its 3-bar average) widening. The bears use the
    candles turned upside down.
  - From WaveTrend's last cross toward the side: 1 point (3 when it crossed from deeper than 60 on
    the side's own side). While that run is alive (the gap has widened on every bar since the cross),
    6 more plus 1 per bar since the cross, up to 10.
  - When the side's run is not alive: 1 per bar beyond the first that the gap has widened in a row
    (up to 9), and 2 when the close is strictly beyond the previous close.
  - The score never goes below −5. The top is 27, the same as DBSI's.
- **How it was chosen:** an integer simulator (`dbsi7/intsim5.py`) searched on the same twenty
  charts only (`intfinal.py`, scored on within 2 plus exact). Every setting one step either way
  scores lower there, and a test pins that. DBSI's remembered "3" (the Q) is left out: its trigger
  is still unresolved (presence matches only about half), and the twenty prefer no Q.
- **Measured on the fifteen livestream charts no fit saw** (integer vs the linear model it replaces):
  exact 20.4% → 31.6%, within 2 67.1% → 70.6%, false 17+ 10 → 4; but within 1 50.5% → 45.2% (every
  vote moves the score by 2, so a miss is usually 2, not 1), error 3.06 → 3.14, 17+ printed on 21 of
  48 (was 27), side in control 241 of 262 (was 243). On the twenty: exact 33.1%, within 1 45.8%,
  within 2 73.5%, error 2.65, side in control 393 of 422 (was 395). On the private Start grids
  (never fitted; kept out of the repo): Dow within 2 75.2% → 78.8%, exact 41.8%; Bitcoin 71.9% →
  75.8%, exact 37.6%, false 17+ 45 → 4.
- **Caveats.** It reaches 17+ less often than DBSI does; its highest number on the 35 charts is 23.
  Rows where the run is alive and DBSI holds no "3" are about 1.8 under. The structure was found
  after seeing all the grids, so the fifteen are held out from the fit but not from the search for
  what to fit. Not seen in the app.
- **Tests:** fixture regenerated (`dbsi7/gen5.py`, still keeps the linear model's numbers for
  comparison); golden replay on all 35 charts bar for bar; a neighbour test; the twenty's and the
  fifteen's numbers pinned, with the integer model beating the linear one on exact by 10 points and
  on within 2 by 3; floor/top; scores start on bar 30; an unchanged close is a close up for neither
  side; the not-alive terms. The leave-one-out, refit and vote-comparison tests went with the fit.
  Nine sabotages red: alive points 5 (1 failed), close up while alive (40), streak uncapped (2),
  streak of the wrong vote (38), c > c[1] for c[1] > c[2] (37), scores from bar 29 (1), deep level 2
  (33), and each side's close up non-strict (1 each; the bears' survived until the unchanged-close
  test was added). README 9596.

### Cipher DBSI adds an eighth base vote: Cipher B's money flow rising (2026-10-24)

- **Formula:** each side's base votes gain one more: Cipher B's money flow (the 60-bar average of each
  Heikin-Ashi body over the average size of the last five bodies) rising for the bulls, falling for
  the bears. It is computed on Heikin-Ashi of the candles the score reads, so on the default
  Heikin-Ashi setting it is Heikin-Ashi of Heikin-Ashi, as Cipher B's own is on a Heikin-Ashi chart
  (`CipherBProvider.McbMoneyFlowShare`, reused). Undefined until its 64-bar window fills. Every
  constant refitted on the same twenty charts (`dbsi6/gen4.py`), copied to the digit. Full score 34
  for both sides (bears were 33); no votes still −5.
- **How it was found:** on the original's Start grids (bars whose two numbers add to 3, nothing
  remembered), a search for the missing base votes found that the money flow rising takes the place
  of one of the 2-point votes. With it, the integer vote count matches the original's bull number
  exactly on 70.5% of the Dow 2024-26 grid, 72.6% of the Dow 2022-24 grid and 72.3% of Bitcoin's
  (about 62% before), and the extremes are now 89-98% exact. The weight-1 vote is confirmed as the
  WaveTrend gap widening.
- **Measured:** on the fifteen livestream charts no fit saw: exact 17.4% -> 20.4%, within 1 48.5% ->
  50.5%, within 2 65.3% -> 67.1%, error 3.08 -> 3.06; 17+ printed on 27 of 48 (same), false 17+
  8 -> 10. Leave one chart out on the twenty: exact 24.1% -> 25.1%, within 2 71.9% -> 72.8%, error
  2.52 -> 2.51, false 17+ 15 -> 20. In sample: 25.2%, 55.3%, 75.6%, 2.36; 17+ printed 50 of 84, 18
  false; side in control 395 of 422 (was 397).
- **On the original's Start grids, never fitted** (private; Cody asked to keep the grids out of the
  repo for now): Dow daily 2022-26 within 2 72.5% -> 75.2%, exact 23.3% -> 27.3%; Bitcoin 1D
  Heikin-Ashi 2022-26 within 2 68.8% -> 71.9%, exact 21.2% -> 24.0%, but false 17+ 35 -> 45.
- **Caveats.** More false 17+ readings on every set: the model now reaches 17 a little more often
  where DBSI does not. About 28% of the no-memory bars are still unmatched, mostly middling scores,
  so some of the other base votes are probably stand-ins for DBSI's own. The money flow was found
  after looking at all the grids. Not seen in the app.
- **Tests:** fixture regenerated; golden replay on all 35 charts, refit recovery (one more column),
  in-sample, leave-one-out and livestream numbers updated, and a new test that the shipped model
  beats the same model fitted without the money flow on the fifteen livestream charts. Four
  sabotages red: falling instead of rising (39 failed), a 59-bar average (39), the vote dropped
  (40), the money flow computed from the open alone (40).

### Cipher C judges a weekly candidate at the next daily low on Gold, Silver and the miners (2026-10-24)

- On Gold, Silver, GDX, GDXJ and SILJ (the original's 22-day assets) the weekly lows are now picked
  as the original picks them there, read from GDXJ's weekly-label replay: a daily low is drawn as
  the candidate and the next daily low judges it. Not lower, the candidate is a weekly low when 4.5
  shortest daily cycles (99 trading days) after the last; lower, that one is a weekly low itself at
  4 cycles (88). Too near, the lower of the two takes the last weekly low's place. After a lower
  judge, or a candidate more than 6 cycles (132) out, the next daily low draws no candidate.
- On the original's own daily lows: 40 of its 47 weekly lows with 6 extra, against 43 with 51 for
  the old pick. Cipher C end to end (its own daily lows, all history): exact 43 → 39 of 72, extras
  44 → 23; the replay years 26 → 24 of 47, extras 31 → 18. SILJ, which the rule was not read from,
  loses three (9 → 6) and Silver two; GDX gains one. The three gaps are fitted.
- Caveat: the rule alternates, so one extra or missing daily low of ours can put the next few
  weekly lows on the wrong daily low. Oil and Bitcoin keep the old pick.
- `CyclePreset.WeeklyNextDaily`, `CipherCEngine.WeeklyFromNextDaily`. Seven new tests, 13 cases (one per branch
  at its edge, causality, the provider wiring for GDX and Silver); nine sabotages red. Not seen in
  the app.

### Cipher C never takes back a confirmed low; a cancellation is now a pending low undercut (2026-10-24)

- Cody's decision: confirmed lows are not cancelled. The original removed none of the 379 it
  confirmed in its candle replays and never drew its cancelled mark over one. A close under the
  newest confirmed low is now always a failed cycle (Cycle Failed), however soon after the low; the
  0.6-of-the-shortest-cycle gap no longer splits the two.
- **Cycle Low Cancelled now fires where the original drops its unconfirmed low.** While a found
  low is holding, a bar going under it before its due bar cancels it. The daily search then drops
  it and looks again from the next bar, as the original does: it names the next pending low a bar
  or more later on 328 of the 332 it dropped (none on the same bar). Before this the lower bar
  silently took its place. The next confirmation still says which cancelled low it replaces. The
  cycle line's cross (used before a chart's first low, and with no daily tier) still moves the low
  onto the undercutting bar.
- Speech: "Daily pending cycle low cancelled: a low of 75285.00, under the pending low of 76000.00
  from 4 days ago." The status line says "the last daily pending cycle low was cancelled 24 days
  ago". A failure is unchanged: "Daily cycle failed: closed at 64045.64, under the cycle low of
  64955.00 from 66 days ago."
- Scored on the original's candle replays, all sixteen assets: exact daily lows 946 → 958 of 1,244
  (extra 246 → 256); weekly lows 280 → 284 of 463 (extra 176 → 172); check marks on the original's
  bar 340 → 332. Over the replay windows the original drops 332 pending lows. Cipher C cancels 470,
  197 of them on the original's bar.
- Bitcoin's weekly lows: Cipher C now keeps 12 May 2023 as a weekly low, which the original does
  not, instead of 10 March 2023. Its other weekly lows since 2022 are unchanged.
- Tried and not adopted (scored on all sixteen):
  - A Schaff-trigger start for the search: first bar exact 405 of 854, against 258 for the close
    above the bar before's high, and better on 15 of 16 assets held out one at a time. End to end
    it was worse: daily 925, extra 310.
  - Keeping a pending low undercut 1 or 2 bars before it is due: daily 964 or 968, but extra 276
    or 298, and weekly 280 or 279.
  - Re-picking the pending low on every bar: agrees with the original on fewer bars (5,081 against
    5,277 of 7,458).
  - A decision tree on when the original names its pending low: no better than the current rule
    (at best recall 0.52 at precision 0.40, leave-one-asset-out).
- New tests:
  - A confirmed low is never taken back.
  - A failure is the first close under the confirmed low, at any gap (4 of 26 on Bitcoin come
    before the old gap).
  - A cancellation is a bar under the pending low before it is due, nothing is held on that bar,
    and the next confirmation names it.
  - Seven sabotages are red: removing the confirmed low, the old gap, re-holding on the same bar,
    cancelling on a close, no Updated, cancelling on the due bar, and speaking the close.
- Not seen or heard in the app.

### Cipher C counts RIOT from 39 trading days (2026-10-24)

- Its table says 36 to 44, the same as MARA's. In the original's candle replay (2022–26, 23 lows) it
  confirms a low 19 bars after it on 19 of 23, as MARA does. But a short cycle is confirmed no
  sooner than the last low + 56 (56, 56, 56, then 60), and a first label shows no sooner than the
  last low + 38. That makes the shortest cycle 39, not MARA's 38. Floor(39/2) is still 19. At 36,
  none of Cipher C's check marks landed on the original's bar (0 of 16).
- Scored on that replay (held out): check marks on the original's bar 0 of 16 → 12 of 15 (all three
  cut-short cycles included), exact daily lows 21 of 31 both, weekly 7 of 15 both. Counting from 38
  instead gives 9 of 15 on the bar, but daily 22 and weekly 8: one more low of each, with the
  timing off. 39 is used because all three timing readings agree on it.
- CTZ, the open-source look-alike: daily 23 of 31 with 33 extra, weekly 7 of 15, never on the
  original's bar.
- The stock count test covers RIOT too: a hold of 19 and a wait of 56. Counting from 36, 38 or 40
  turns it red. That completes the sixteen presets. Not seen in the app.

### Cipher C counts MARA from 38 trading days (2026-10-24)

- Its table says 36 to 44. In the original's candle replay (2022–26, 23 lows) it confirms a low 19
  bars after it on 19 of 23, a short cycle no sooner than the last low + 55 (55, 55, 56, 67), and
  shows a first label no sooner than the last low + 37: a shortest cycle of 38. At 36 every Cipher C
  check mark came a bar early (0 of 20 on the original's bar).
- Scored on that replay (held out): check marks on the original's bar 0 of 20 → 15 of 19, exact
  daily lows 27 → 26 of 32, extra daily lows 4 → 5, weekly 11 of 14 both (3 extra). Counting from
  37 gives 0 of 20; from 39, 15 of 18 with daily 25 and weekly 10. CTZ, the open-source look-alike:
  daily 20 of 32 with 34 extra, weekly 9 of 14, never on the original's bar.
- The Coinbase and MicroStrategy test now covers MARA too: a hold of 19 and a wait of 55. Counting
  from 36, 37 or 39 turns it red. Not seen in the app.

### Cipher C counts Coinbase and MicroStrategy from 51 days, and the hold rounds down (2026-10-24)

- Both tables say 40 to 90 trading days. In the original's candle replays (Coinbase 2021–26, 17
  lows; MicroStrategy 2019–26, 18 lows) it confirms a low 25 bars after it on 14 of each, and a
  short cycle no sooner than the last low + 74 (Coinbase 74; MicroStrategy 74, 74, 74, 75). Its
  first label comes no sooner than the last low + 50. All three fit a shortest cycle of 51 if half
  of it is rounded DOWN (25); every earlier asset's shortest cycle was even, so this is the first
  that tells the two apart. Cipher C's hold now rounds down. At 40, every Cipher C check mark on
  these two came 5 bars early.
- MicroStrategy (held out): exact daily lows 22 → 25 of 28, extra daily lows 14 → 4, weekly 7 → 10
  of 12 (extras 7 → 2), check marks on the original's bar 0 of 15 → 15 of 16. CTZ, the open-source
  look-alike: daily 17 of 28 with 46 extra, weekly 8 of 12, never on the original's bar.
- Coinbase (held out): check marks on the original's bar 0 of 13 → 7 of 8, extra daily lows 9 → 6,
  but exact daily lows 12 → 7 of 19 and weekly 5 → 4 of 9. The lows lost are search and
  cancellation, not timing. Once, the bar that turns up is itself the cycle's highest high, so the
  search finds nothing (the original still names the low before it). Once, a low undercut 29 bars
  after it is now inside the longer cancel gap (31 bars). CTZ: daily 13 of 19 with 33 extra.
- The other twelve scored assets come out byte for byte the same (their shortest cycles are even).
  A minimum typed in by hand that is odd now holds a bar less.
- Tried and not adopted, on all fourteen assets: letting the search's top exclude the bar that turns
  up (exact daily lows 886 → 904 of 1167, but extra lows 229 → 334). Not cancelling a confirmed low
  at all: 886 → 903, extras 229 → 239, weekly unchanged. The original never removed a confirmed
  low in any replay (0 of 379 on nine assets) and never drew its cancelled mark (Cody: never cancel
  a confirmed low; done in the entry above).
- New test: weekday bars under the Coinbase and MicroStrategy presets confirm 25 bars after the low
  and no sooner than the last kept low + 74. Rounding the hold to nearest (26), or counting from 50
  (73) or 52, turns it red. Not seen in the app.

### Cipher C counts Ethereum's daily cycle from 16 days (2026-10-24)

- Ethereum's table says 15 to 25 days. In the original's candle replay (2022–26, 75 lows) it
  confirms a low 8 bars after it on 59 of 75, a short cycle no sooner than the last low + 22 (all
  16 cut-short cycles) and a first label no sooner than the last low + 15: all fit a shortest cycle
  of 16. At 15 the wait is also 8 (7.5 rounds up), so only the short-cycle wait differs: 21, a bar
  early. The range shown and said stays 15 to 25, followed by "counted from 16".
- Scored on that replay (Ethereum was held out until now): check marks on the original's bar 47 of
  66 → 52 of 67, extra daily lows 44 → 37, exact daily lows 113 → 112 of 130; weekly 31 of 57 both,
  extra weekly lows 36 → 33. CTZ, the open-source look-alike, at its own setting: daily 68 of 130,
  weekly 11; set to the table's ranges, 105 and 20, with 59 extra daily lows; never on the
  original's confirmation bar.
- Ethereum's weekly lows fit neither weekly rule: counting daily lows (the dollar's rule) gives the
  original's own weekly lows 26 of 35 since 2021-11 at its gap, the weekly pick 24 of 34. Counting
  them in Cipher C adds one exact weekly low (31 → 32); not adopted on that evidence.
- The junior miners were checked and behave as their table's 22: GDXJ holds 11 on 37 of 44, short
  cycles from the last low + 31; SILJ 11 on 38 of 43, from + 31. Cipher C on SILJ (held out): daily
  45 of 60, 8 extra, check marks on the original's bar 27 of 29, weekly 9 of 14; CTZ daily 36 of 60.
- New test: Bitcoin's bars under Ethereum's preset confirm 8 bars after the low and no sooner than
  the last kept low + 22. Counting from 15 (21) or 17 (23) turns it red. Not seen in the app.

### Cipher C picks the weekly lows of the dollar, Dow, S&P and Nasdaq by counting daily lows (2026-10-24)

- Read from the original's weekly labels and week counts, logged bar by bar in the dollar's candle
  replay: the first daily low after a new weekly low takes its place if lower; from the second on, a
  new weekly low goes on the lowest daily low since then that is two shortest daily cycles less two
  bars each after the last (68 bars on the Dow and Nasdaq, 32 on the dollar). The weekly low called
  confirmed is the one before the newest. Fed the original's own daily lows it draws 22 of 22 of the
  dollar's weekly lows, 10 of 11 on the Dow and Nasdaq, 11 of 13 on the S&P.
- Cipher C on its own daily lows, exact weekly lows (extras): dollar 19 → 27 of 46 (16 → 16), Dow
  39 → 40 of 57 (17 → 13), Nasdaq 33 → 38 of 56 (22 → 16), S&P 30 → 29 of 57 (19 → 20; the original
  counts the S&P from its default 32, Cipher C from 36). Daily lows: Dow 101 → 100, dollar 87 → 86
  (every weekly low is drawn as a daily one). Oil, Gold, Silver, GDX and Bitcoin do not fit it and
  keep the weekly pick from 2026-10-23.
- The gap was fitted on the replays (2022–26); the history before them is held out.
- Oil (held out) is scored: its table's 36 days fits its timing, daily lows 97 of 129, check marks
  on the original's bar 13 of 17. Not changed.
- Seven new tests (each branch by hand, causality on every cut, the Dow's provider wiring); six
  sabotages red: the gap one bar longer, no replacement, a replacement replaced, no placement on an
  earlier low, the confirmed low moved on the drawing bar, the flag ignored. Not seen in the app.

### Cipher C counts the dollar index's daily cycle from 18 days (2026-10-24)

- The dollar's table says 15 to 24 days. In the original's candle replay (2021–26, 52 lows) it
  confirms a low 9 bars after it on 39 of 52, a short cycle no sooner than the last low + 25 and a
  first label no sooner than the last low + 17: all fit a shortest cycle of 18. The range shown and
  said stays 15 to 24, followed by "counted from 18".
- Scored on that replay: check marks on the original's bar 1 of 43 → 30 of 40, extra daily lows
  37 → 21. It costs exact lows: daily 93 → 87 of 114, weekly 21 → 19 of 46. The lost lows come from
  Cipher C's search firing before the original's (its top comes earlier), not from the count.
  The other seven scored assets are unchanged.
- The Silver test is now one test over three assets (Silver 12, Gold 11, dollar 9 bars, on the
  same weekday bars). Counting the dollar from 15 or 20 turns it red. Not seen in the app.

### Cipher C counts Silver's daily cycle from 24 days (2026-10-24)

- Silver's table says 22 to 28 days, the same as Gold's, and Gold behaves as 22. In the original's
  candle replay of Silver (2021–26, 40 lows) it confirms a low 12 bars after it on 33 of 40, a short
  cycle no sooner than the last low + 34 and a first label no sooner than the last low + 23: all
  three fit a shortest cycle of 24. Cipher C now counts Silver from 24, as Bitcoin from 48. The
  range shown and said stays 22 to 28, followed by "counted from 24".
- Scored on that replay (Silver was held out until now): check marks on the original's bar 0 of 33
  → 27 of 32, extra daily lows 12 → 8. It costs one exact daily low (47 → 46 of 56) and one exact
  weekly low (6 → 5 of 12); both stay 6 within 2 bars. The other six scored assets are unchanged.
- Silver's weekly lows do not fit the weekly pick either (7 of the original's 9 since 2021 at best,
  as Gold and GDX, the other 22-day assets).
- One new test: the same weekday bars under Silver confirm 12 bars after their low, under Gold 11.
  Counting Silver from 22 or 26 turns it red. Not seen in the app.

### Cipher C confirms a low on the day it comes due, even if that day goes lower (2026-10-24)

- Until now a lower low on the bar a held daily low came due took its place and started the hold
  again. The original does not do that. In its candle replays on five daily charts (GDX, Bitcoin,
  the Dow, SP:SPX, Nasdaq, 2022–26), 13 bars went under its unconfirmed low once the hold had run.
  On 8 of them it confirmed the low anyway; it kept the low unconfirmed on 3 and dropped it on 2.
  Before the hold has run, a lower low still takes the held one's place (`CipherCEngine.Detect`).
- **Scored on the same candle replays, against the original's labels** (before → after):
  - GDX: daily lows on its bar 49 → 51 of 61, weekly 5 → 6 of 12 (extra weekly 7 → 6), check marks
    on its bar 30 of 33 → 31 of 35;
  - Bitcoin: daily 22 → 23 of 25 (extra 6 → 5), weekly 9 → 10 of 11 (extra 3 → 2): it now has the
    original's daily low of 12 May 2023 (was 15 June) and so its weekly low of 11 September 2023;
  - S&P (SP:SPX): daily 83 → 84 of 129, weekly 29 → 30 of 57;
  - Dow: daily 101 of 129 both, weekly 40 → 39 of 57 (extra 14 → 17), check marks 15 of 19 → 16 of 20;
  - Nasdaq: unchanged.
  The rule was chosen after scoring all five, so none is held out. Gold and the assets after it,
  replayed next, are.
- **Tests:** a new search test (a lower low on the due bar does not take the low's place: Bitcoin's
  12 May 2023 at 48; the bar before still does: 23 October 2019 for 30 September 2019); the weekly
  test's list (now misses only 21 November 2025, extra 10 March 2023 and 22 June 2025); the two
  "lowest bar up to the confirmation" tests now stop at the bar before it. Sabotages, all red: the
  old rule; never replacing; stopping a bar early; stopping a bar late.

### Cipher C counts Bitcoin's daily cycle from 48 days, as the original does (2026-10-24)

- The original's table says Bitcoin's daily cycle is 54 to 66 days. Replayed bar by bar on candles
  (2022–26, 24 lows), every timing it shows fits a shortest cycle of 48:
  - its first label of a new low comes no sooner than the last low + 47;
  - it confirms 24 bars after the low on 19 of 24;
  - a short cycle's confirmation waits until the last low + 70 on 4 of 5.
  The same three readings give 22 on GDX, 36 on the Dow and 32 on the S&P's default count, each
  that asset's own shortest cycle. So the rule is unchanged; only Bitcoin's number was wrong.
- `CyclePreset.CountDailyMin` (new, 0 = the table's) holds Bitcoin's 48. The detector counts with it.
  The range shown and said stays 54 to 66, and the preset's words add "counted from 48". The
  "Daily cycle shortest" setting still overrides it.
- **Scored on the Bitcoin candle replay:**
  - daily lows on the original's bar: 19 → 22 of 25, with 6 extra lows before and after;
  - check marks on the original's bar: 0 of 19 (all 3 bars late) → 20 of 22;
  - weekly lows: 9 of 11 before and after, extra weekly lows 1 → 3. It now has the original's
    weekly lows of 19 December 2022 and 11 March 2025, but misses 11 September 2023 and
    21 November 2025, where the daily lows before them differ.
  The 48 was read from this same replay, so this is not held out. The older Heikin-Ashi replay and
  Ethereum are still to check.
- **Also found:** on TradingView's SP:SPX the original says "Unoptimised Asset Detected" and uses a
  default count that behaves as 32. Cipher C keeps the S&P's 36 to 44 for SPX (question in TODO).
- **Fixed on the way:** Cipher C's "ago" companions (cancellation, failure, lowest since the
  low) existed only if the event had fired in the bars given, so a chart with no cancellation
  had fewer components than one with. They now always exist (NaN when the event never fires).
  The causality test found it: one of its cuts has no cancellation at 48.
- **Tests:** Bitcoin's daily confirmations come 24 bars after the low, never sooner, and 24 is the
  commonest lag. The preset's words say "counted from 48", and other presets don't. The window
  on a weekday chart is still 54 × 5/7. Bitcoin's weekly lows are listed against the original's.
  Sabotages, all red: the provider ignores the count; the words drop it; 48 → 54; the window
  starts at the count; the companion is made only when its event fires.

### Cipher DBSI adds seven base votes found on the original's Start grid (2026-10-24)

- **Formula:** each side adds seven more weighted checks (bears read the candles upside down): close
  above the close 2 back, a 4-bar EMA of the close above itself 2 bars back, the typical price above
  its 4-bar RMA, the 6-bar stochastic (SMA 2) above its 3-bar average, the 21-bar stochastic (SMA 2)
  rising, WaveTrend wt1 rising, and the gap between wt1 and its 3-bar average widening. They are the
  smallest set whose plain count matched the original's score on the Dow daily Start grid (DBSI's own
  labels from the live script) on bars with no memory: 66% exact before 2025, 58% after. Two of them
  are the same as existing votes and only add to those votes' points. Every constant was refitted on
  the same twenty charts (`dbsi6/gen3.py`), copied to the digit (`DbsiStyleProvider.BaseWeights`,
  `BaseVotes`). Full score 34 bulls / 33 bears; no votes still −5.
- **Measured, held out one chart at a time on the twenty:** exact 13.0% → 24.1%, within ±1 44.0% →
  52.0%, within ±2 67.1% → 71.9%, error 2.82 → 2.52; false 17+ readings 26 → 15. In sample: 23.7%,
  54.1%, 73.9%, 2.41; 17+ printed on 47 of 84 (was 56), 13 false (was 22); side in control 397 of 422.
- **Out of sample:** fitted on the twenty, scored on fifteen later livestream charts no fit saw (now
  in the fixture, marked not fitted): exact 14.0% → 17.4%, ±1 40.6% → 48.5%, ±2 61.0% → 65.3%, error
  3.30 → 3.08; 17+ printed on 27 of 48 (was 28), false 18 → 8.
- **Caveats.** Still far from exact: about 1 value in 6 on new charts. The checks were found on a Dow
  daily grid, while most of the thirty-five charts are crypto; they are what fits, not the original's
  code. The remembered "3" and the opposite side's counter are still not in. Not seen in the app.
- **Tests:** the fixture now carries all 35 charts (20 fitted, 15 held out); golden replay on every
  bar, refit recovery (with the two duplicate votes merged), in-sample, leave-one-chart-out and a new
  test that the base votes beat the model without them on the fifteen. Four sabotages red: bears not
  mirrored (high and low not swapped, 39 failed), RMA swapped for EMA (19), gap vote dropped (39), EMA
  compared 1 bar back instead of 2 (39).

### Parts that should not show by default are left out, not hidden (2026-10-23)

Cody, 2026-10-23: audit every indicator; parts that should not show by default are left out
(Properties → Appearance → Parts), "not added hidden and muted".

- A hidden part was still a keyboard stop (announced "Hidden"), still listed in the Alerts dialog,
  and could be narrated. A part left out is none of those until its Include box is ticked: not
  drawn, sounded, spoken, narrated or reached with Ctrl+Up/Down. It is still computed, so
  strategies, the parts that read it, and speech that reads it by name (Cipher DBSI's "price
  above/below" reads its Trend source close) work as before.
- **Every indicator** was audited; no part ships hidden any more (a test fails on any that does).
  Left out by default now: Anchored VWAP's Bias and Bias Soft; Cipher B's anchor waves, Anchor
  Polarity, red dot and four divergences; Coin Metrics' MVRV regime, MVRV Z, active addresses Z and
  hash rate; COT's net % of open interest; Cipher DBSI's Control and Trend source; DSS Bressert's
  MIT; Hurst's Regime; Ichimoku's Kumo Polarity; Pivots' Zone and R3/S3 (and Camarilla H3/H4/L3/L4);
  fourteen of Pulse's; Regime's Above SMA and Above EMA; Market Structure's State and Last Swing
  High/Low; Value Deviation's Value High, Value Low and Tier; Vol Regime's Percentile and State.
  Included, each one shows (a left-out part's line is not also hidden).
- **Saved charts.** A part saved hidden at the default that hid it comes back left out; so does a
  Cipher B signal saved shown only because it was shown by default before 2026-10-17. A part the
  user had shown stays in and shown; so does one the user had given a background tint (Anchor
  Polarity shading the price chart with its line hidden), which a left-out part could not paint;
  either keeps the visibility it was saved with.
  A part included since, then hidden with H, stays included and hidden. "Save as Defaults" saved
  while a part was hidden leaves it out.
- **Caveats.** Cipher B's Anchor Fill draws only once both anchor waves are included. Ctrl+Alt+Shift+K
  (show everything hidden) does not include parts. An alert already set on a part now left out
  still fires, but the Alerts dialog does not list that part and describes the alert by code and
  part name. A part included and hidden with H, then saved with "Save as Defaults", comes back
  left out on the next new chart (the saved defaults cannot tell that from an old hidden default).
- Tests: `HiddenPartsAreLeftOutTests` (ten), Cipher B's visibility tests rewritten for left out
  (`CipherBStockVisibilityTests`), background tint and restore tests updated. Two older failures
  fixed on the way: the workspace restore contract did not declare `ExcludedComponents`
  (since 8f359e83), and the knob sweep had no entry for `RibbonName` (its reader, the bar-close
  ribbon narration, is pinned by `RibbonNarrationTests`). Nine sabotages red: a part hidden again,
  either migration removed, speech skipping left-out data, the retired default, a since-change
  include, a tint, a user's show, and a kept-in part's saved visibility each ignored. Non-browser
  suite 9,486 passed. Not seen in the app.

### Properties → Advanced: the settings the original indicator does not have (2026-10-23)

Cody, 2026-10-23: settings the original indicator has no input for go on an Advanced tab.

- A parameter can now say it is one of ours (`IndicatorParameterMetadata.IsAdvanced`). Properties
  shows those on a new **Advanced** tab, last in the row, and only for an indicator that has one;
  General keeps the rest. Arrow keys, Home and End reach it like the other tabs.
- **Cipher B**: Money Flow Formula (legacy), Buy Dot Rule (gated), Anchor Wave Multiplier, Threshold
  Mode, Adaptive Lookback, Upper and Lower Percentile, Min Threshold Floor, Confirmation Bars,
  Cooldown Bars, Anchor Suppression and its depth, Timeframe-Aware Gates. General keeps the four
  periods, Buy Dot Level, OB/OS Threshold and Divergence Pivot Bars.
- **Cipher DBSI**: Compute on Heikin-Ashi candles and Label the newest N bars (the original scores
  whatever candles the chart draws, and labels 25 because of TradingView's label limit).
- **Not moved**, because they are the original's inputs or were not checked against it: Cipher A,
  Cipher SR, Cipher C (CF's inputs were not compared one by one). The Wave Trend periods are not
  inputs of Market Cipher B either, but are the indicator's formula, so they stay on General.
- Nothing about how any indicator calculates changed; only where its settings are shown. The Add
  Indicator dialog still lists every parameter together.
- Three tests (`PropertiesAdvancedTabTests`); five sabotages red: nothing advanced, the tab always
  shown, the arrows skipping it, the legacy money flow back on General, Advanced listing everything.
  Not seen in the app.

### Reset to Defaults resets everything, parameters included (2026-10-23)

Cody, 2026-10-23: Reset to Defaults should reset everything, inputs, periods and modes too.

- **Properties → Reset to Defaults** used to rebuild the parts, colours, sound and levels and carry
  the parameters across, so an RSI set to 7 or a Cipher B on its legacy money flow stayed that way.
  It now also puts every parameter back to the indicator's default (as the Add Indicator dialog
  seeds them), and Cipher-style fills and bands, Sonification's series volume and General's
  "announce across series". The name is worked out again from the new parameters, for every
  instance of that indicator on the chart. The open dialog shows the defaults at once.
- **Kept**, because they are not settings of the indicator: its pane, the levels you placed, a
  fixed-range or anchored profile's anchor (which bars it covers), and the mute, hide and narrate
  switches the keys toggle.
- Moved from the dialog into `SeriesManagementService.ResetToIndicatorDefaults` so it can be tested.
  Five tests (`ResetToIndicatorDefaultsTests`); six sabotages red: parameters kept, no re-name,
  anchor dropped, volume kept, preferences not cleared, parts kept as the user had them.
- Not seen in the app.

### Cipher A in the original's colours, with Ribbon 9, pivots, Parts → Include and ribbon narration (2026-10-23)

Cody, 2026-10-23: the ninth ribbon line "available but disabled", pivot marks "true to the
original", "all colors should match as well for everything", "each shape and ribbon and visual
option as a preference", and on narration "Ribbon narration, yes … for this indicator and I suppose
ribbons in general" / "announce when in the ribbon? yes".

- **Colours and sizes** are Market Cipher A 2.5.0's own style settings, read from its settings
  dialog: the ladder #0066CC #1A8CFF #4DA6FF #80BFFF #99CCFF #99CCFF #CCE6FF #E6F2FF (was matched
  from screenshots, #1573D4 …), grey #505662, every line width 2 (EMA 11–33 were 1.5). Symbols:
  Yellow Diamond #FDD835 at 30% opacity, Blue Triangle #4259F4, Green Dot #00E676, Red X #F23645,
  Yellow X #FDD835, Red Diamond #F23645 at 55%, Blood Diamond #F23645; the original's "tiny" is 8 px
  and "small" 12 px here (Blue Triangle, Yellow X and Blood Diamond are small).
- **Two fills**, as the original: EMA 5 to EMA 33 (#B3D9FF at 10%) and EMA 5 to EMA 11 (#80B3FF at
  15%). Drawn only; they have no sound.
- **Ribbon 9**: a 21-bar simple average of the close (General → Ribbon 9 Length), yellow #FDD835,
  width 2. **Pivot High / Pivot Low**: the highest high and lowest low of the last N bars (General →
  Pivot Bars Back, 1 by default, as the original's "Days Back"), #4DA6FF at 80%, width 1. All three
  are left out by default, as the original has them off; every other part is in.
  - **Pivots checked against Market Cipher A on TradingView** (TVC:DJI, 300 bars each, by setting
    its "H/L Pivot Days Back" and Show Pivot inputs and reading what it drew, then putting them
    back): 1D at 1 and 3 back match bar for bar. On 1 hour the original counts DAYS: every bar shows
    the 1D bars' high and low of its own day (1 back), and of its day and the trading day before
    (2 back; Monday with Friday), 300/300 both. Ours now does the same on intraday charts
    (`CipherAProvider.Pivots`, days from `CipherSrProvider.IsIntraday` / UTC dates). **Deliberate
    difference:** on past days the original's 10:00 bar already carries the 15:00 high (the whole
    day's); ours uses today's high so far, as Cipher SR's range (Cody: keep it running), so the two
    agree on each day's last bar and on the forming bar and differ earlier in a day. **Not checked:**
    sessions that cross midnight UTC (split into two days here), weekly charts (bars here), Bars Back
    above 2 intraday. Test `On_an_intraday_chart_the_pivots_are_trading_days_back_with_today_so_far`;
    sabotages red: chart bars always, calendar days, one day too many, the whole day's high.
- **Properties → Appearance → Parts**: an "Include" box per part, for any indicator with more than
  one. A part left out moves to `SeriesConfig.ExcludedComponents`: still computed (so other parts
  and strategies that read it still work) but not drawn, sounded, spoken, narrated or reached with
  the arrow keys, and a fill with a left-out edge is not drawn. It is saved with the chart; "Save as
  Defaults" stores it (`IndicatorPreferences.IsEnabled`); "Reset to Defaults" puts the indicator's
  own choice back. A part put back returns to its place in the indicator's order. The last included
  part cannot be left out: its box cancels the click and says "At least one part has to stay
  included." (Before that fix, found while writing this, the refusal was spoken but the box was left
  unticked, since Blazor saw no change to re-render.) A provider declares a part off with
  `IndicatorComponentMetadata.DefaultIsEnabled = false`; a chart saved before a part existed gets it
  per that default.
- **Ribbon narration.** Lines that share an `IndicatorComponentMetadata.RibbonName` narrate as one
  band on the bar close: "Cipher A: Price entered the ribbon, between 64,200.00 and 66,900.00.",
  "… left the ribbon above, at …" / "below, at …", and "… crossed above / below the ribbon, at …"
  when one bar jumps it. The band is the min and max of the visible ribbon lines on that bar.
  Cipher A's eight EMAs are one ribbon; Ribbon 9 and the pivots are not part of it. A ribbon line
  never speaks its own "Price crossed". The side price is on when narration is switched on is the
  starting point, so the bar forming then can be news when it closes.
- **Correction to the TODO's question 3:** it said per-line crosses were announced "today". They
  were not: the eight lines are muted and the overlay-cross scan skips muted lines, so the ribbon
  said nothing on a bar close (`Ribbon_lines_were_silent_before_ribbon_narration` records it).
- **Caveats.** Not seen or heard in the app. The pivots are checked against the original only at
  Days Back 1 on a daily chart; higher values and intraday charts are unverified (the "highest of N
  bars" reading is a guess for N above 1). Ribbon narration ignores the lines' mute (their mute is
  for sound); it is silenced by the series not narrating or by an N selection that picks none of its
  lines. The last-box refusal is tested in bUnit (the cancel flag and the spoken refusal), not in a
  browser. The accessibility-lead review agent is not registered in this session, so no agent review
  of the Parts fieldset was run.
- **Tests:** `ComponentInclusionTests` (7), `RibbonNarrationTests` (11), Cipher A metadata and
  provider tests for colours, fills, defaults, Ribbon 9 and pivots, and a `PropertiesModalTests` case
  for the last part. Sabotages red: factory partition off, restore drops excluded, fill ignores
  exclusion, clone drops excluded, preference ignored, include appends, Ribbon 9 on by default, pivot
  on close, Yellow Diamond opaque, fills removed; ribbon scan not called, overlay speaks ribbon lines,
  mute honoured, selection ignored, hidden lines counted, no seed, left above says below, factory
  drops the ribbon name, clone drops it, Cipher A without a ribbon; last box not cancelled, refusal
  not spoken. ("No seed" and "factory drops" first stayed green; a tick test and a factory test were
  added and both now go red.)

### Cipher A's Yellow Diamond needs Cipher B's money flow on a near tie of the waves (2026-10-23)

- **Rule:** the Yellow Diamond (fast wave above slow, EMA 5 above EMA 11 and EMA 33) now also needs
  the fast wave more than 1.25 above the slow one, or Cipher B's money flow (MCB mode, 60 bars) above 5.
  Cipher A computes the money flow itself (`CipherBProvider.McbMoneyFlow`, the code Cipher B already
  used, moved into a shared method).
- **Found:** every one of the Yellow Diamonds the old rule added had the waves 1.18 or less apart (raw
  chart 1.22), yet real ones are drawn on gaps as small as 0.02, so no gap threshold alone works and no
  Wave Trend length, source or smoothing did better. A depth-1 tree on the near-tie bars picked money
  flow; with Market Cipher B 2.4.0's own plotted money flow, "gap > T or money flow > M" is exact on
  both Bitcoin daily charts (Bitstamp 2011–2026 after a 300-bar warm-up: Heikin-Ashi 1,312/1,312 with
  34 extra before, raw candles 1,322/1,322 with 39 extra before). The feasible bounds were T in
  (1.22, 1.28] and M in [4.97, 5.01) across the two; 1.25 and 5 picked.
- **Held out:** Market Cipher A 2.5.0 on DJI 1D (300 bars, 2024-03 to 2025-06, never used for the fit),
  with this app's money flow: 9 extra → none, 92/92. DJI has no volume, so the gate does not use volume.
- **Caveats.** With this app's money flow on Bitcoin daily, which is close to but not exactly Market
  Cipher B's, 6 (Heikin-Ashi: 1 extra, 5 missing) and 4 (raw: 3 extra, 1 missing) Yellow Diamonds
  differ; the money-flow fix would close them. Two thresholds fitted on 73 bars across two charts of
  one market, with one held-out chart. The Green Dot needs the Yellow Diamond, so a Green Dot can now
  move with it (none did on the charts above). Not seen in the app.
- **Tests:** goldens regenerated from an independent restatement (`mca2/golden3.py`); a new test runs
  Cipher B alone on four generated walks and checks the gate on 27 near ties with money flow 0–5 and 317
  above 5 (the golden candles have none in 0–5). Sabotages red: gate removed, gap 2.5, "and" for "or",
  money flow 0, money flow 10.

### Cipher A's symbols say what they mean, not which averages crossed (2026-10-23)

- Landing on a symbol now says its meaning: Green Dot "Trend turned up: medium-term trend crossed up,
  with momentum and the short-term trend already up", Blue Triangle "Early turn up…", Yellow Diamond
  "Bullish conditions…", Red X "Short-term trend turned down", Red Diamond "Momentum turned down",
  Blood Diamond "…the strongest sell warning", Yellow X "Momentum turned down again while already
  oversold: often a trap; several in a row can mean selling is running out". Before, it read the EMA
  and Wave Trend values. Cody asked for this (2026-10-23).
- The manual defines short-term trend, medium-term trend and momentum in terms of the averages, and
  adds how traders read the symbols, marked as their claims, untested by the app.
- **Caveats.** The trader readings in the Yellow X and Blood Diamond sentences are claims, not
  measured. Not heard in the app.
- **Tests:** the speech test now also asserts no sentence contains "EMA". Sabotage (Red X back to
  "EMA 5 crossed under EMA 11") red.

### Cipher DBSI adds the WaveTrend run the original was found to remember (2026-10-23)

- **Formula:** each side's score adds −0.02 on the WaveTrend (9, 12, 3) cross toward it, +2.09 for the
  rest of the run if wt1 was past −60 against the side at the cross, +0.18 if it was past +60 for it,
  +3.45 while the gap between wt1 and its 3-bar average has widened on every bar since the cross, and
  +1.61 per bar it has widened (up to 10). Once the gap narrows the last two go for the rest of the run.
  The votes, streak and stretch were refitted with these terms on the same twenty charts
  (`dbsi2/gen2.py`), copied to the digit (`DbsiStyleProvider.RunWeights`, `WaveTrendRun`).
- **Measured, held out one chart at a time on the twenty:** exact 16.7% → 13.0%, within ±1 42.4% →
  44.0%, within ±2 60.1% → 67.1%, error 3.40 → 2.82; readings of 17+ printed on 55 of 84 (was 4), and on
  26 bars where DBSI did not (was 8). In sample: 14.6%, 46.0%, 69.9%, 2.67, 56 of 84 printed, 22 false;
  side in control 395 of 422 (94%).
- **Out of sample:** fitted on the twenty, scored on fifteen later livestream charts no fit saw: exact
  14.1% → 14.0%, ±1 36.5% → 40.6%, ±2 55.6% → 61.0%, error 3.77 → 3.30, lower error on all 15 charts
  (±2 better on 10, equal 2, worse 3). 17+ printed on 28 of 48 (was 2), 18 false (was 12). Refitted
  on all 35 with charts held out one at a time: ±1 36.4% → 41.6%, ±2 57.7% → 62.3%, error 3.54 → 2.99.
- **Caveats.** Exact hits did not improve. More false high readings. The terms are what could be
  fitted from the structure below, not the original's mechanism; the remembered "3" and the opposite
  side's counter are not in. The fixture is regenerated from the same twenty charts (the fifteen later
  ones are not in it; they are scratch data and their scores above are the Python study's). Not seen
  in the app.
- **Tests:** golden replay, refit recovery (21 constants), in-sample, held-out and a hand-made
  WaveTrend run test. Five sabotages red: alive term dropped (21 failed), alive never dying (24), the
  deep flag ignoring the side (17), count uncapped (1), a run already going on the first bar counted (1).

### Cipher DBSI measured against the original's own labels: what it remembers (research) (2026-10-23)

- **How it was read.** The original's labels were read from the chart only (Dow 1D, Bar Replay back to
  2021; the script was not opened). Its Start input was moved one bar at a time across 40 replay
  windows (2024-10 to 2026-10). A label that changes with Start depends on bars before Start, which is
  how the remembered part was separated from the part each bar scores fresh.
- **Each side's WaveTrend run (9/12/3, own side's line above the other) carries a bonus, set at the cross:**
  - a run level: bull 7 when the line crosses below −60, otherwise 1; bear 2 when the line is beyond ±60,
    otherwise 1;
  - about 4 more while the gap between the two lines has widened on every bar since the cross; once it
    narrows, this is gone for the rest of the run;
  - about 3 more (3 or 4 on bear) that lasts while the gap widens and for a varying number of bars after.
- **Not found: what ends that 3.** The best single rule tried (close minus its 13-bar average still
  rising since the gap first narrowed) agrees on 88% of 331 bars. The best pair of rules agrees on 90%.
- **Second memory, newly found.** While one side is in that "after the widening, 3 still on" stretch,
  the other side gains 1 per bar. The gain stays until the other side's own cross. It ticked on 12 of 23
  such bars, on none of 59 bars while the gap was still widening, and on 4 of 62 bars after the 3 had
  gone. On 2025-01-17 to 01-29 the bear label climbed −4, −3, −2, −1, 0 while the bull label held at 11.
- **Effect on a whole-label rule (fitted 2021–24, scored 2025–26, 908 bars): small.** With the run
  bonus and the opposite counter: exact 32%, within 2 67%, error 3.21. Without them: 31%, 64%, 3.46.
  The app's linear form refitted on this chart scores 18% exact, 63% within 2, error 3.18 without the
  run terms. With them it scores 20%, 72%, 2.55, and 60 of 97 readings of 17+ instead of 2. So the
  linear form with run terms is the better candidate. It held on the twenty livestream charts and on
  fifteen later ones (entry above), so it is in the app.
- **The limit is the per-bar score, not the memory.** On bars that carry no memory, an integer vote
  count over our 300-condition library matches only about 40% exactly. More of the grid (2021–2024)
  and a second chart (Bitcoin) are what to gather next.
- **Later the same evening: what each earlier bar adds (research only, no code change).** From the
  same windows, with no new reading: the change in a label when Start moves back one more bar is what
  that bar contributes. Lined up by contributing bar, this gives a map of what each bar adds to each
  later day's label.
  - **The cross bar** adds the run level, plus 4 while the gap widens, plus the 3 when it is on
    (for example 14 = 7 + 4 + 3). Once the gap narrows it drops to level + 3, then to the level, and to 0
    when the run ends.
  - **Other bars in the run** add 1 each, which lasts one to three days. **Bars on the other side** do
    the same for the other side's counter: on its k-th day, each of the last k bars adds exactly 1.
  - **The 3 is its own event, usually on the bar just before the side's own cross** (3, 4, or 5 on the
    first day). It lasts 1 to 10 days and can end while the run is still widening.
  - **What starts it (best found):** the close moved this side's way, this side's 11-bar stochastic
    (3-bar average) is below 50 for bulls, above 50 for bears, and at least 2 bars have passed since this side's last run
    ended. That matches 56 of 67 starts, with 14 false starts.
  - **What ends it:** not one close the other way. "Close beyond the close 2 bars back" keeps it on for
    88.5% of 156 days. With "5/13 MACD still rising" added it is 91.7%, but that pair was the best of
    about 80,000 tried, so it may be luck.
  - **Held out (checked later on 2026-10-23):** both rules were fixed on the 2024-10 to 2026-10 grid,
    then scored on a new Start grid of the same Dow daily chart, 2022-05 to 2024-09 (49 snapshots, 389
    candidate bars, 80 starts). Start rule: 68 of 80 starts, 20 false (precision 0.77, recall 0.85;
    fitted 0.80 and 0.84). End rule "close beyond the close 2 bars back": right on 92.6% of 190 days
    (fitted 88.5%); with the MACD added, 92.1% (fitted 91.7%), so the MACD adds nothing out of sample.
    One chart and one market, so it says the rule is real there, not that it is the original's.
  - **Tried in the app's linear form, not adopted.** On the fifteen later livestream charts: within 2
    59.0% to 62.8%, error 3.28 to 3.19, exact 14.6% to 14.0%, false 17+ readings 17 to 20. The best
    variant was chosen after seeing these scores, and the fitted weight is about 1.5 against a true 3–4,
    so the rule is only a rough stand-in.
  - **No volume needed.** The Dow chart has no volume on any of its 1,733 bars, and the original still
    scores there. The app's version uses no volume either.

### Cipher B's money flow divides by the five-bar average Heikin-Ashi BODY size: one scale fits every chart (2026-10-23)

- **Formula (MoneyFlowMode = MCB, the default):** 68 × SMA60(Heikin-Ashi body ÷ SMA5(|Heikin-Ashi
  body|)), clamped to ±100. Earlier the same day it was 146 × SMA60(body ÷ SMA5(Heikin-Ashi range))
  (the entry below). The wicks no longer count at all. Legacy mode is unchanged.
- **What gave it away:** the Dow's daily chart before about 1930 has flat bars (open = high = low
  = close), so the Heikin-Ashi range there is the body. On those bars the range form needed a scale
  of about 71; on the rest of the Dow about 140; on Bitcoin 135–163. Dividing by the body size
  instead needs one scale on all of them. Market Cipher B 2.4.0's money flow was harvested on
  TVC:DJI 1D for this (the whole history), as well as the Bitcoin charts already measured.
- **Measured against Market Cipher B 2.4.0's plotted money flow** (typical error, RMSE in points on
  a ±100 scale, at the pooled scale; from the fitting script, which the app's restatement test
  matches):

  | Chart | Best scale alone | RMSE range form → body form |
  |---|---|---|
  | Dow 1D, before 1930 (flat bars) | 69.6 | 3.06 → 1.99 |
  | Dow 1D, 1930–1993 | 68.7 | 5.43 → 1.72 |
  | Dow 1D, 1994 on | 66.5 | 4.97 → 1.73 |
  | Bitcoin 1D | 66.4 | 4.74 → 1.80 |
  | Bitcoin 15-minute | 66.1 | 4.06 → 1.77 |

  The pooled best scale is 67.8; the app uses 68. Same side of zero 96.6–97.5% (was 92–94%).
  Fitted on the Dow alone the scale is still about 68, and Bitcoin, never fitted, stays at 1.8.
- **Cipher A's Yellow Diamond follows it** (its near-tie gate reads this money flow above 5).
  Against Market Cipher A's own diamonds: Bitcoin daily Heikin-Ashi 6 → 4 bars differ (of 5,213),
  candles 4 → 3, Dow (300 bars, no volume) still exact. One diamond left the Cipher A goldens
  (2021-08-14), regenerated from an independent restatement (golden4.py).
- **Tried and worse on every chart:** 4, 6, 7 or 8 bodies; an EMA or RMA of the body size; the
  largest of the last five; root mean square; SMA5(body) ÷ SMA5(size); an EMA, 59- or 61-bar outer
  average; a one-bar shift; raw candle bodies; the result normalised by its own spread. On a
  Heikin-Ashi chart, Heikin-Ashi of the Heikin-Ashi bars fits worse (2.43) than Heikin-Ashi of the
  raw bars (1.93), so the existing handling stays.
- **Caveats:** still NOT exact (about 1.8 points typical). The original's line starts at bar 67,
  ours at 63; that is still unexplained. The body form was found after looking at all the data;
  what argues against overfitting is that one number now fits two very different markets and the
  Dow's flat-bar era, where the range form needed half the scale.
- Tests: `Money_flow_reads_68_on_steady_candles_whatever_their_size` (steady candles of size 1 and
  10 both read 68; the range form read a size-dependent number), `Money_flow_ignores_the_wicks` (a
  symmetric wick spike changes nothing), and `Money_flow_is_68_times_the_60_bar_mean_of_body_over_five_bar_mean_size`
  (an independent restatement on a random walk) replace the range test and the 146 linear test. The
  Cipher B fingerprint re-pinned only the money-flow line. Sabotages, all red: range divisor, 4
  bodies, 6 bodies, scale 70, own body size, raw candle body.

### Cipher B's money flow divides by the five-bar average Heikin-Ashi range (2026-10-23)

- **Formula (MoneyFlowMode = MCB, the default):** 146 × SMA60(Heikin-Ashi body ÷ SMA5(Heikin-Ashi
  range)), clamped to ±100. It was 180 × SMA60(body ÷ that candle's own range). Legacy mode is
  unchanged.
- **Measured against Market Cipher B 2.4.0's plotted money flow** (BTCUSD 1D candles and Heikin-Ashi
  from 2011, 5,446 bars each; BTCUSD 15-minute from 2026-09-01, 5,009 bars). Typical error (RMSE,
  points on a ±100 scale) and share of bars on the same side of zero, old → new:

  | Chart | RMSE | Same side of zero | Correlation |
  |---|---|---|---|
  | 1D candles | 8.5 → 5.0 | 92.0% → 93.8% | 0.980 |
  | 1D Heikin-Ashi | 6.4 → 4.8 | 92.8% → 95.0% | 0.986 |
  | 15-minute candles | 6.7 → 4.3 | 86.9% → 91.7% | 0.967 |

  The new numbers are from the app's own output, not the fitting script (they agree to the
  printed digit).
- **How it was chosen:** the 60-bar average is confirmed by deconvolution: each step of the
  original's line is a fixed multiple of the newest bar's term minus the term 60 bars back. The
  five-bar range was picked on 1D candles before 2020 only, then checked on the rest of the 1D
  candles, the 1D Heikin-Ashi chart and the 15-minute chart, and improved all three. The 146 is the
  pooled fit.
- **Caveats:** still NOT exact. Fitted alone, each chart wants a different scale (135–163), so some
  ingredient is still missing. The original's line also starts at bar 67, 8 bars later than a
  60-bar average needs (ours starts at bar 63); that is unexplained. Rejected because they fitted
  worse or only fitted the data they were chosen on: volume weighting, RSI and MFI of 4–15 bars,
  EMAs, double smoothing, windowed stochastics, clipping and tanh, shifted bars, and the
  Heikin-Ashi hlc3 in place of the close (slightly better, but chosen on all the data).
- Tests: `Money_flow_divides_by_the_five_bar_average_range` widens one candle's wicks, which
  changes its Heikin-Ashi range and not its body; the wave must stay lower until bar k+63 and be
  back exactly at k+64. The linear test now expects 146 × share. The Cipher B fingerprint re-pinned
  only the money-flow line; no other output moved. SetupGraderMc2's causality test now runs on two
  random walks (seeds 7 and 11), because on seed 7 alone no wait ended void under the new money
  flow, and the vacuity check needs one. Sabotages, all red: own range (1 bar), 4 bars, 6 bars,
  scale 180, raw body in place of Heikin-Ashi.
- Not seen in the app.
- **Later the same day: research only, no code change.**
  - **Chart type barely matters.** The original's money flow on the Bitcoin daily candle chart and on
    the Heikin-Ashi chart agree closely: correlation 0.998, median gap 0.9 points, largest 5.8 (at the
    start of the series and in July 2018). Its waves differ by up to 13 points between the two.
  - **What that suggests:** the original computes its Heikin-Ashi bars from the real candles even on
    a Heikin-Ashi chart, but not exactly, since the lines are not identical. The app computes them
    from whatever bars the chart shows, so on a Heikin-Ashi chart it works from Heikin-Ashi of
    Heikin-Ashi.
  - **The newest-bar term:** worked back from the original's line, its per-bar input has a
    correlation of 0.944 with ours, peaking with no lag. The slope is steady from year to year
    (0.94–1.10).
  - **What's left over** correlates most with where the close sits in the Heikin-Ashi-of-Heikin-Ashi
    bar (0.32) and with the previous candle's body (0.27).
  - **Two extra terms:** fitted before 2020 and tested after, they cut the per-bar error from 0.199 to
    0.169. That is a formula made up to fit, not the original's, so it was not adopted.
  - **The 8-bar-late start** is still unexplained. Every form tried that would start at bar 67 fitted
    worse: 9-bar range averages (simple, EMA, RMA, high-low), a 5-bar average of a 5-bar average, and
    a 5-bar average of the ratio.

### Arrowing speaks only the focused component; lines say their colour; no "Drawn as" (2026-10-23)

- **Reported by Cody:** with signal descriptions on, "I could be on cipher b moving around and hear
  cipher A shapes ... when on cipher a on the green dot I'll be hearing about the yellow diamonds and
  everything else ... I should only hear what is relevant to the component I'm on unless they are
  drawn meta on the chart like chart patterns and the cycle arc".
  - **Cause:** every left/right step spoke the other marker signals on the bar: the focused
    indicator's other markers and every price-pane indicator's (NavigationFeedbackManager
    GetAdditionalSignalSpeech, on since it stopped being gated on the candle series). The
    descriptions made it longer, not different. The sound side was already focused-only
    (2026-10-22, "Tick other signals while arrowing").
  - **Fix:** that clause is now opt-in, **Settings → Speech → Speak other signals while arrowing**
    (`speech.speakOtherSignalsWhileArrowing`, off). Formations, zone proximity and the user's text
    labels are chart-wide and still spoken; narration at bar close and playback are unchanged.
- **Lines say their colour** under the renamed **Say what lines and signals look like**
  (MarkerLook.LineSentence): "Orange line." on moving up or down onto a line or area, and on a
  left/right step only where the colour's name changes. The colour is the one painted
  (StandardRenderers.LineColorAt: colour rules, sibling comparison, polarity split, ZeroArea sign,
  else the component's colour). Volume and histogram bars say none: they are coloured by
  direction on every bar. Every built-in line uses its component's colour, so this covers all the
  standard indicators without per-indicator work.
- **"Drawn as" removed:** "Buy. Large green dot." (was "Buy. Drawn as a large green dot.").
- Tests: the opt-in is pinned both ways on a Cipher A-like pair and another indicator's marker; line
  colour on arrival, not again along the line, on a colour-rule change, against a sibling, never for
  histograms, and through navigation. NavigationUtteranceTests now runs with the opt-in on. Five
  sabotages each went red: the gate removed, the colour said on every bar, the sibling colour
  ignored, "Drawn as" put back, histograms describing a colour. Not heard in the app.

### Cipher SR checked against Market Cipher SR's own values: the level rule is now exact (2026-10-23)

- **Read from the original's plotted values.** Market Cipher SR 2.4.0's six plots were read from
  TradingView on Bitstamp BTCUSD 15-minute (5,076 bars, 2026-09-01 to 2026-10-23), at its defaults
  and with VolumeMA set to 1, 2, 12 and 50, Days Back 3 and Resolution 5 and 30.
  - **Cipher Resistance / Support:** a bar whose high climbed STRICTLY for two bars into it and
    fell STRICTLY for two bars after it (h[p-2] < h[p-1] < h[p] > h[p+1] > h[p+2]; the mirror for
    lows), with volume above its VolumeMA-bar average (itself included), drawn from bar p + 3.
    Rebuilt that way, Cipher SR's Resistance and Support Zones equal the original's on every bar at
    VolumeMA 2, 6, 12 and 50 (5,013 to 5,065 bars per line). VolumeMA 1 draws nothing in both.
    The rule this replaces (three bars left, one right, drawn on the next bar) was fitted to two
    video frames; it took about twice the levels, and each one bar after its pivot.
  - **How the rule was found:** every accepted level is a two-bar pivot on volume above average,
    but only half of those are accepted. Changing VolumeMA never flipped a pivot that passed the
    volume test at both settings, so the second condition is a fixed property of the bars; a search
    over price comparisons near the pivot found the stepped climb and fall (99.4% alone, then 100%).
  - **VWAP was already exact** (to 0.00001, the live bar excepted). **H/L Pivot Days Back** N
    covers today and the N - 1 days before it, as ours did (checked at 1 and 3).
  - **Pivot High / Low ("Range High / Low") differ on purpose.** The original draws the whole UTC
    day's high and low from the day's first bar, which needs the rest of the day; ours draws the
    range so far, the same value on the live bar (50% of historical bars equal). Unchanged.
  - **Resolution** (15 by default) recomputes everything on that timeframe's bars; a provider here
    sees only the chart's bars, so ours is the original's on a 15-minute chart only. Unchanged.
- **Setting removed:** "Pivot Left Bars". The original's pivot has no setting. A chart that saved
  one loads and ignores it.
- **On the old video frames,** which were not used to find the rule, agreement rose from 68.6% to
  96.5% (10-minute frame) and from 62.5% to 69.8% (12-minute frame, read only to the bar).
- **Research moves with it.** The setup-grader round 3 (Market Cipher at Cipher SR support) and the
  confluence studies read the old levels; none was re-run.
- Tests: the zone-rule test now pins the stepped pivot three bars back, and that every stepped
  pivot is taken; a new test pins that an equal step on either side is not a step, with its twin;
  the video-frame floors rose to 93% and 66%. The TradingView export is not in the repository (in
  the gitignored strategy-lab-data/mc-exact). Five sabotages each went red: a plain two-bar pivot,
  drawn one bar early, an equal step allowed on the left, on the right, and no volume filter. Not
  seen or heard in the app.

### Cipher A checked against Market Cipher A's own values: four symbol rules corrected (2026-10-23)

- **Read from the original's plotted values.** Market Cipher A 2.5.0's data-window values were read
  from TradingView on Bitstamp BTCUSD daily (2011–2026), on candles and on Heikin-Ashi, and compared
  with Cipher A bar for bar.
  - **The eight ribbon lines were already exact** (EMA of the close, 5 11 15 18 21 25 29 33), and so
    were the Red X (185 of 185) and the Blood Diamond (29 of 29).
  - **Green Dot:** EMA 11 crossing above EMA 33 counts only on a Yellow Diamond candle (fast wave
    above slow, EMA 5 above EMA 11 and EMA 33). Now 60 of 60 on candles, 61 of 61 on Heikin-Ashi,
    none extra.
  - **Blue Triangle:** EMA 5 crossing above EMA 25 with the fast wave above the slow one and
    **EMA 11** below EMA 33. The old test was EMA 29 below EMA 33, which missed 8 and added 8. Now
    88 of 88 and 85 of 85.
  - **Red Diamond:** drawn on every wave cross down, a Red X candle included; the original plots it
    under its Blood Diamond. It used to be left off those candles. Now 541 of 541. The bar detail
    still names only the Blood Diamond on such a candle.
  - **Yellow X:** the stochastic is Cipher B's corrected "Stochastic" line (81-bar stochastic of the
    **close**, smoothed over 2) **below 20**. It was an 80-bar stochastic of hlc3 smoothed over 3,
    below 22. Now 26 of 26 and 25 of 25. The Yellow X Range Level default moved from 22 to 20; a
    chart that saved 22 keeps it.
- **The Yellow Diamond is still not exact.** It finds every real one (1,391) and draws 40 the
  original does not. Those 40 sit near a tie of the waves or the ribbon lines, but no threshold,
  brute-force search or decision tree on the inputs separated them.
- **The original has things this does not draw:** a ninth ribbon line (a 21-bar simple average of
  the close) and pivot high/low marks at the bar's high and low. Both are hidden in its defaults.
  Its defaults also show only the EMA 21 line, and hide the Yellow Diamond, Green Dot and Red
  Diamond; Cipher A shows everything. Neither is changed; both are questions in TODO.
- **Research moves with it.** The StrategyLab Cipher A battery (`cipher-a-battery`) reads these
  symbols; it was not re-run, and its 2026-10-17 verdicts stand on the old rules.
- Tests: `CipherAGoldenData`'s reference events and values were regenerated from an independent
  restatement of the new rules (scratchpad `golden2.py`; the ribbon and wave values came out
  bit-identical to the old reference). Both real-chart tests still pass, with a Red Diamond under a
  digitised Blood Diamond allowed. The Red Diamond test now pins every Blood Diamond on a Red
  Diamond. Six sabotages each went red: the Green Dot without its gate, the Blue Triangle without the
  wave test or without EMA 11 below EMA 33, the Red Diamond skipping Red X candles, the old
  stochastic, and the old level 22. Not seen or heard in the app.

### Cipher B checked against Market Cipher B's own values: the two 0–100 lines and the Buy dot corrected (2026-10-23)

- **Read from the original's plotted values, not screenshots.** With Cody's Market Cipher membership,
  Market Cipher B 2.4.0's data-window values were read from TradingView on Bitstamp BTCUSD daily,
  5,513 bars, once on a Heikin-Ashi chart and once on candles. Compared against Cipher B's formulas:
  - The waves, the yellow "VWAP" (fast minus slow) and the small cross dots were already exact
    (524 of 524 crosses each way).
  - **The "RSI" and "Stochastic" lines were wrong.** They are stochastics of the **close**, not of
    hlc3. The lengths are **40 and 81** bars, not 40 and 80, and they are smoothed over **2** bars,
    not 3 (`McbRsiLength`, `McbStochLength`, `McbLineSmoothing`). Now exact (RMSE 0.000003 against
    the next-best forms' 1.3 and 0.7 points).
  - **On a Heikin-Ashi chart the lines now read the Heikin-Ashi candles,** as the waves do. The
    2026-10-22 screenshot digitising had put them on the real bars, and the exact values say otherwise.
  - **The Buy dot tests the fast wave.** It is every cross up with the fast wave below −60. The
    slow wave was the old test. Now 64 of 64 Market Cipher B Buy dots on candles and 68 of 68 on
    Heikin-Ashi, none extra. The slow-wave rule drew 10 and 7 dots the original never drew. The
    crosses now use Pine's crossover and crossunder exactly.
- **The money flow is still not exact.** It is near a 60-bar average of each Heikin-Ashi candle's
  body over its range (Heikin-Ashi of Heikin-Ashi on a Heikin-Ashi chart). Against the original:
  correlation 0.97, same side of zero on 92% of bars. The multiplier is unchanged. Lower-timeframe
  sourcing and other forms are not yet tried.
- **Research moves with it.** The StrategyLab setup grader reads the Buy dot (`CompBlue`), so a re-run
  of the Buy-dot studies would see slightly different dots. They were not re-run; their "no edge"
  verdicts stand on the old dots.
- Tests: `CipherBMcbFormulaTests` restates the Buy dot (with a check that the slow-wave rule really
  differs) and the lines' stochastic independently. `HeikinAshiIndicatorCandlesTests` expects the
  lines on Heikin-Ashi candles. `CipherBWaveTrendParityTests` re-pinned the RSI and Stochastic
  fingerprints only. Five sabotages each went red: the Buy dot back on the slow wave, the lines on
  hlc3, 80 bars, 3-bar smoothing, and the lines on the real bars under Heikin-Ashi. Not seen or
  heard in the app.

### Cipher C picks its weekly lows from its daily lows, as the original does (2026-10-23)

- **On a daily chart the weekly tier no longer finds its own lows.** `CipherCEngine.WeeklyFromDaily`
  judges each confirmed daily low on the bar it is confirmed. The new daily low is the weekly
  candidate. It is kept when the next daily low comes at least `KeepBars` from the last kept weekly
  low: the shortest weekly cycle less four weeks (`KeepWeeksBelowMin`), so 90 bars on GDX and the Dow
  and 126 on Bitcoin. A lower daily low replaces it, and a kept low undercut too soon is dropped
  (Cancelled, with the price of the low that replaced it on the next confirmation). After a weekly
  low lower than the one before, the next daily low decides a "double". "Confirmed" is the kept
  weekly low before the newest, as the original labels it, so on Bitcoin a weekly low is confirmed
  156 to 374 days after it (median 206). Weekly lows are no longer snapped onto daily lows; they are
  daily lows already.
  - Read from the original's labels in TradingView, not from its code. Run on the original's own
    daily lows over sixteen assets: 317 of its weekly lows drawn, 60 extra, 60 missed. On the spans
    replayed bar by bar (GDX, Dow, Bitcoin) it draws exactly the original's.
  - End to end, with Cipher C finding its own daily lows (measured afterwards, same day): weekly F1
    0.580 on the same bar over the sixteen assets, against 0.560 before; eight assets better, seven
    worse (GDXJ and GDX lose). The daily lows are what limit it.
  - On the app's Bitcoin data since 2022-05-12, Cipher C's weekly lows are the original's eight.
    It adds 2025-04-07 where the original has 2025-03-11, because the daily lows differ there. It
    has no 2022-12-19, which is a daily low Cipher C does not find. The 5 August 2024 crash is no
    longer a weekly low; the original has a daily low there but no weekly one. The old test asserted
    that weekly low and was replaced.
  - Caveats: the four weeks are fitted, not read (GDX accepts 84 to 100 bars; Bitcoin gives the same
    lows anywhere from 119 to 134). Residuals remain on the dollar index, Ethereum and gold. The
    "lower low a daily cycle later" step and the double rest on few cases. On Bitcoin a weekly low
    is never replaced, so that part is tested only on synthetic lows. Weekly charts are unchanged
    (the weekly tier still finds its own lows with the cross and hold, unverified): the original has
    no weekly mode and warns "Timeframe Not Optimised".
  - Tests: `CipherCWeeklyTests` (new, worked by hand: the margin, the keep, the drop and the double,
    the replacement, causality at every cut), the provider's wiring at 22 and 24 weeks
    (`Cipher_C_searches_this_way_on_the_daily_tier_only`; 24 because Bitcoin cannot tell 18 weeks
    from 19), and the Bitcoin dates in `CipherCProviderTests`. Sixteen sabotages, all red.
  - DBSI side note, no code: two more DBSI videos were read (TODO). The real scale reaches 29–30.
    One checkable daily reading (Bitcoin 2021-05-09, bull 8 / bear −2 on an open candle) has Cipher
    DBSI on the same side but stronger (13 / −1 closed).
  - Not seen in the app.

### Cipher DBSI refitted on twenty charts: per-vote points, a streak and a stretch (2026-10-23)

- **Each of the twelve votes now has its own points, and two terms join them.** The old score was
  `max(−5, a + 1.668 × votes)`, fitted on the first six digitised charts. Refitted on all twenty
  (897 DBSI numbers, `dbsi2/gen.py`), each vote has its own weight (`DbsiStyleProvider.VoteWeights`,
  0.35 to 3.32), the side's **streak** (bars in a row closing beyond the close two back, capped at 8)
  subtracts 0.51 a bar, and the **stretch** (the 14-bar stochastic's %K beyond 50 against the side)
  adds 0.69 per ten points. `ScoringTerms` exposes the per-bar inputs so the golden test refits from
  them.
  - Held out one chart at a time: exact 16.7%, within ±1 42.4%, within ±2 60.1%, error 3.40. Twelve
    equal votes refitted on the same folds: 16.1%, 35.0%, 56.0%, 3.72. On the fourteen charts the old
    numbers never saw, closer on 13 (ETH 10-minute is worse: 4.24 vs 3.56). In sample: 16.7%, 42.7%,
    60.8%, 3.30, side in control 94%.
  - Caveats: the twelve conditions were picked on the first six charts, so those six are optimistic
    held out. The top of the scale stays compressed: of DBSI's 84 readings of 17 or more, it prints
    17+ on 4 (and on 8 bars where DBSI did not); the most it can print is 19. Bulls now reach −5
    (the old no-vote score rounded to −4). Not DBSI's formula: two other designs failed ("−5 + 2 ×
    votes"; six families of conditions over six lookbacks, after the vendor's "36 indications").
  - `DbsiStyleGoldenTests` rewritten: exact replay on all twenty charts (was six), the refit recovers
    all fifteen constants, in-sample and held-out fidelity, the scale. Revert is this one commit.
  - Not seen in the app.

### Cipher C: the daily search opens on schedule, gold's weeks are five bars, and the backtest re-run (2026-10-23)

- **The daily tier searches for the next low the way the original appears to.** It no longer waits
  for the cycle line to cross up through overbought once a confirmed low is there. From the last low
  plus the shortest cycle less two bars (`CipherCEngine.SearchFromBeforeMin`), the first close above
  the bar before's high takes the lowest low since the cycle's highest high as the low to hold
  (`Detect(searchOnTurn: true)`, used by the daily tier only; the first low on a chart, and the weekly
  and yearly tiers, still wait for the cross). Read from the Dow and GDX bar replays: the original's
  "unconfirmed" low first appears 21 to 43 bars after the last low on GDX, and is the low after the
  cycle's top, seldom the lowest bar since the last low.
  - It is the best rule tried, not the original's: simulated on every replayed bar it names the
    original's candidate on the bar it appears GDX 25 of 38, Dow 21 of 35. The original sometimes
    names a low on a falling bar, or a day or two later.
  - Exact-bar F1 against the original's labels, 8 assets held out: daily 0.727 to 0.739, weekly 0.587
    to 0.592, tops 0.795 to 0.815 (five better, two worse, Bitcoin level). Training assets: daily
    0.694 to 0.721.
  - Not seen or heard in the app.
- **Gold's and the dollar index's weeks are five bars, as the original counts them.** One Saturday or
  Sunday bar in the last 500 used to make Cipher C treat a market as trading every day, so
  twelvedata's gold (short weekend bars since 2024) flipped its weeks from five bars to seven as the
  window moved, and redrew every cycle. Now Saturday and Sunday bars must each turn up on at least half
  the weeks, each at a median range of at least a quarter of a weekday's (`WeekendRangeShare`); on
  two-day bars at least a fifth must fall on a weekend. Flat bars (high equals low: nothing traded)
  are left out of the ranges, since Bitstamp's Bitcoin had more flat weekend bars than not in 2011
  and counting them made its first 300 bars a five-day market (Dogecoin and Solana four-hour bars
  likewise early on). Over every cut of the lab's 52 daily assets, and its four-hour and five-minute
  files, each asset keeps one clock: crypto seven days, gold and stocks five. Read from the original's own "weeks since the
  last weekly low" on sixteen assets: crypto is the only seven-day week. XAU's bar-by-bar replay now
  agrees with the full run on all 161 daily confirmations (was 42 of 150); replayed from bar 100,
  XAU 168 of 168, Bitcoin 89 of 89, Ethereum 153 of 153.
- **The backtest of buying the confirmed daily low, re-run on the original's timing** (Cody chose
  to keep it). Every event replays causally on all seven assets. Confirmation now comes a median
  18 bars after the low on the stock ETFs, 27 on Bitcoin. Exits still show no edge against random
  entries with the same exit and stop. **The left-translated finding is withdrawn:** after the
  change, the cycle after a left-translated one beat the cycle after a right-translated one on only
  6 of the 13 ETFs it had been predicted on (12 of 13 before), and SPY, QQQ and DIA show nothing
  (p 0.39–0.79). Docs only; see CIPHER_C_DESIGN section 8.
- Tests: `CipherCSearchTests` (6) and `CipherCClockTests` (13). The rules' numbers are written into
  the tests, not read from the engine. Twelve sabotages failed as they should. Search: the gate two
  bars later, a close above the previous close, the lowest low since the last low, two bars to the
  low's right, no search, the cross still allowed. Clock: Saturday presence only, Sunday presence
  only, no presence check, flat bars counted, flat bars counted on weekends only, all-flat weekends
  counted as trading. Earlier clock sabotages also failed: the old any-weekend-bar rule, no
  range check, and two-day bars judged by range. `CipherCHoldTests` now run the daily tier as
  shipped (with the search).

### Cipher C confirms a cycle low when the original indicator does (2026-10-22)

- **A found low now waits out a hold before it is confirmed.** It holds for half the shortest cycle,
  counted from the low or from the last confirmed low plus the shortest cycle less two bars,
  whichever is later. A lower low while it holds takes its place and the hold starts again. This is
  the original's own timing. It was read bar by bar from TradingView's bar replay with Cody's trial
  (Dow daily from 2023, GDX daily from October 2023; nothing exported, data kept out of the repo).
  The original fits it on every cycle: Dow 18 bars after the later of the low and the last low plus
  34, GDX 11 bars after the later of the low and the last low plus 20.
  - Confirmations on the original's own bar: Dow 13 of 18 (was 1), GDX 16 of 27 (was 0). Same low
    on any date: Dow 16 (was 14), GDX 21 (was 20).
  - On the 8 assets held out of the original's finetuned list, exact-bar F1 against its labels:
    daily 0.712 to 0.727, weekly 0.560 to 0.587, tops 0.789 to 0.795.
  - `CipherCEngine.HoldFraction` (0.5) and `HoldFromBeforeMin` (2). Cancellation is kept, because
    the original has the state, though dropping it scored a little higher.
- **All sixteen presets are now the original's ranges.** Fifteen were read from the table it draws
  on TradingView. Changed: DXY 15–24 days, 16–20 weeks, 3 years; crude oil 36–44 days, 16–20 weeks,
  3 years; GDXJ and SILJ 22–26 weeks; Nasdaq, Dow and Ethereum 4 years. SPX keeps its ranges from the
  videos, because its table does not draw on SP:SPX. No preset is spoken as "estimated" any more.
- Tests: `CipherCHoldTests` (3) hold the engine to the rule on the Bitcoin fixture. The rule's
  numbers are written in the test, not read from the engine. 6 sabotages went red: no hold, no
  replacement, the hold counted from the last low plus 53 or plus 51, the hold ignoring the last
  low, and a hold of 0.55. The causality test now also cuts just after the last confirmation that
  replaced a cancelled low, because cancellations are rare under the hold. The preset test now
  covers all fifteen read tables.
- **Caveats:**
  - A daily low is now known later: 18 trading days on the Dow, 27 days on Bitcoin (a median 12
    before). The section 8 backtest bought earlier and has not been re-run.
  - What starts the search for a low is still Cipher C's own. In strong rallies the original
    confirms on schedule while Cipher C waits for its cycle line (GDX, late 2025).
  - Daily charts only; the weekly tier uses the same hold unverified.
  - Not seen or heard in the app.

### Cipher C checked against labels it was not built on (2026-10-22, docs and research data only)

- Scored against the original indicator's output in videos from after the build (10-19 to 10-22). On
  DJI and TLT daily, five of seven lows match within a few days (`docs/CIPHER_C_DESIGN.md` section 9).
- ~~Defect: on DJI the daily tier took a half-cycle low (06-23) instead of 07-22.~~ **Withdrawn the
  same day.** The original indicator itself, read on TradingView, confirms 06-23 too, and counts its
  timing window from it. The screenshot used other settings, a different symbol or another version.
  The proposed fix is dropped (section 9 carries the note).
- **Against the original itself** (section 10). Cody took the creator's TradingView trial. Every label,
  box and table cell it draws on DJI daily was read from the chart. Nothing was exported: that needs
  a paid plan. The data stays out of the repo. From 2017 to 2026:
  - 35 of its 48 daily lows are on the same bar as Cipher C's;
  - 12 of its 20 weekly lows match, and 38 of its 47 tops;
  - Cipher C makes more, shorter cycles: 8 under 27 bars, where the original never goes under 27;
  - the original labels only left- and right-translated tops.

  Caveats: final labels, so this says nothing about real-time confirmation timing. One asset so far.
- The four transcripts are added to the corpus (`AccessibleTrader.StrategyLab/Corpus`, manifest
  updated). Two members-only videos were not obtained.
- Caveat: these are two screenshots of other people's charts, with dates read to 1–3 days. The
  creator hides the indicator on his own charts.

### Signals can say what they look like (2026-10-22)

Cody asked whether the descriptors had been added: "if not, let's work on this". Sighted traders name a
signal by its look ("the big green dot", "the red triangle above the candle"), and a signal heard only by
its name ("Buy") cannot be matched to that. New setting: Settings → Speech → **Say what signals look
like** (off by default; `SettingsKeys.DescribeSignalLooks`, mirrored into `WorkspaceState.DescribeSignalLooks`,
declared NotCarried in the script sandbox projection). With it on, a signal marker is followed by a
sentence: *"Buy. Drawn as a large green dot."*

- **What is said** (`MarkerLook`, Core/Services/Accessibility):
  - The **shape**: dot, up or down arrow (by the value's sign, as it is drawn), up/down triangle,
    up triangle with a ring (and its letter), diamond, square, X, square with a check mark, square with
    circling arrows.
  - The **size**, from the thickness that sizes every marker: below 2.5 "small", 4.5 and up "large", no
    word between. Cipher B's Buy dot (5) is large; its ordinary dots (3, 3.5) say no size.
  - The **colour painted on that bar**, not the declared one. A colour rule, a Money Flow dot's sign and
    a gradient dot's companion each repaint a marker per bar. The renderers' per-bar colour choice moved
    into `StandardRenderers.MarkerColorAt`, which the dot renderers and the speech both read.
    Colour names come from hue and lightness (`MarkerLook.ColorName`), tuned on every colour the built-in
    markers use. For example, #78909C (Cipher C's pending box) is "gray", #FF3D00 is "red", #EF9A9A is
    "light red" and #E91E63 is "pink".
  - **Where it sits**, when it is pinned to the candle: "below the candle" or "above the candle".
- **Where it is said:**
  - arrowing onto a signal (the focused component, and the Series-context summary);
  - another indicator's signal on the bar;
  - playback narration;
  - the signals narrated when a bar closes (after a hindsight mark's date).
- **Not described:** alerts. A tree alert says "conditions met" and names no marker.
- **Changed on the way:** the Series summary joins its components with ". ", so a value already ending
  in a full stop (a signal template such as "Buy.") gave a double stop before the next one. That comes from
  reading the old join and was not shown by a test. The join now drops the inner stops and keeps the last one.
- **Tests.** `MarkerLookTests` (56):
  - colour names, size, shape, sign, anchor and the ring's letter;
  - nothing said on a bar with no marker, a hidden component or a line;
  - a **pixel sweep**: every marker shape is drawn through the real `DataLayer`, and the colour named
    must be the name of the colour covering most of the marker. This covers colour rules, ZeroDot signs
    and the gradient. A guard fails if a new marker shape is not in the sweep.
  - each speech path with the setting on and off.

  Twelve sabotages turned it red, each restored and diffed:
  - the setting gate removed;
  - the declared colour used in place of the painted one;
  - each of the five speech hooks dropped;
  - each Series/focused gate forced on, and the summary gate forced off;
  - the renderer's gradient replaced with the flat colour;
  - the size word and the arrow's sign removed.

  A first pass of the pixel sweep caught three wrong names (saddle brown said "orange", blue-gray said
  "blue", #CCCCCC said "white"). The summary test caught the summary's lost final full stop.
- **Not heard in the app yet.**

### Arrowing is heard for the component you are on (2026-10-22)

Cody: "when left and right arrowing in an indicator with many series, it seems like I hear
sonifications from components other than the one I'm navigating … some of them i think may be rsi
crosses or something". Two things made those sounds. Both are shown by tests that failed before the fix.

- **Level cues followed every indicator, not the focus.** `LevelCrossingMonitor.OnBarNavigated` worked out
  the 1,400 Hz approach ping and the 220 Hz "held beyond" tone for EVERY visible indicator with levels,
  each from its first line, whatever had focus. Arrowing Cipher B's money flow rang for its WaveTrend
  nearing ±60, and for an RSI in another pane nearing 70. They now follow the focused component, against
  the levels it answers to (`SubscribedLevelNames`). The crossing chirp already worked that way. A marker,
  a level line or a muted component in focus gets no level cue. Each tracker is now keyed by component too.
- **Cluster ticks are now opt-in.** After the focused component's tone, every other marker of the same
  indicator on that bar (Cipher B's dots and crosses, for example) ticked quietly on slots 3–7. They now
  play only with Settings → Sonification → **Tick other signals while arrowing** (off by default;
  `SettingsKeys.TickOtherSignalsWhileArrowing`, `IAppSettings`, injected into `SonificationManager`
  as optional). Playback is unchanged.
- **Unchanged:** a horizontal or vertical line you drew still sounds its crossing chirp whatever has
  focus (Cody, 2026-10-09). Price crosses those lines, not an indicator component.
- **Tests.** `ArrowingHearsOnlyTheFocusedComponentTests` (6): an RSI in another pane is silent, a sibling
  line is silent, the focused line still rings (the positive control), cluster ticks are off with no
  settings and with the box unticked, and they fire with it ticked. `LevelCrossingMonitorTests` now
  focuses its RSI. `ClusterTicksFireOnNavigationTests` turns the setting on. Three sabotages turned it
  red, each restored and diffed: the subscription filter removed, the first indicator with levels used
  in place of the focused one, and the setting gate removed.
- **Not heard in the app yet.**

### The sub-pane strip is gone (2026-10-22)

Cody: "remove the small strip now". A component could set `SubPaneName` (and `SubPaneHeightRatio`)
and be drawn in a short strip along the bottom of its pane, on its own range. Loukas Cycle Detection's
"FY" was the last user and Loukas was removed today, so no built-in indicator has used a strip since.

- **Removed.** The layout and the extra render pass in `ChartRenderer`, `IPaneLayoutService.SubPaneStrips`
  and the `SubPaneStrip` record, the strip's own axis labels, the `RenderContext.SubPaneFilter`
  parameter, the strip clauses in hit-testing, hover mapping and drawing anchors, the `"Pane/Sub"`
  range keys in `ViewportRangeCalculator`, the "N strips inside it" speech and the strip part of the
  pane-change and Shift+F1 sentences, and the two properties on `ComponentConfig` and
  `IndicatorComponentMetadata`. Intra-pane navigation now walks every component in the pane.
  A missing pane range key no longer falls back through a second key; the pane key is looked up once.
- **Saved files still load.** Workspaces that carry `"SubPaneName"` or `"SubPaneHeightRatio"` (Cipher B
  saves have both as null; a Loukas save has `"FY"`) deserialize as before. The loader ignores members
  it does not know, and the component is drawn in its pane's main area. `SavedSubPaneNameTests` (2)
  pins both. Proven by sabotage: putting `SubPaneName` back on `ComponentConfig` with the old main-pass
  skip turns the draw test red ("not drawn at all"); restored and diffed.
- **Breaking for plugins.** A plugin that sets `SubPaneName` or `SubPaneHeightRatio` no longer compiles.
  Remove the lines; the component is then drawn in the main area of its pane. A component that needs its
  own axis should use its own pane.
- **Tests.** `SubPaneStripTests` deleted (the strip's labels, hover, hit-test and frame), plus one
  strip case each in `BackgroundTintTests`, `ChartHitTesterAndRangeTests`, `RenderLayerTests` (2),
  `DeclaredBoundsTests`, `CipherBMetadataTests`, `PaneRangeFallbackTests`, `PaneModelAndTrailingSpeechTests`
  and `PriceAutoFitScopeTests`. Strip knobs left the declared-knob sweep. `GetSeriesRange_SubPane_*`
  were renamed `GetSeriesRange_IndicatorPane_*` (they test indicator panes). The guard against a
  too-short pane failing the frame (`if (labelBottom < labelTop) return;` in `RenderYAxis`) is still in
  the code, but its test went with the strip. Not seen or heard in the app.

### Cipher C backtested: buying the confirmed daily low (2026-10-22, research, no code change)

Asked by Cody: buy at the confirmed cycle low, sell at the top, split by translation. Full write-up in
`docs/CIPHER_C_DESIGN.md` section 8; scripts in `strategy-lab-data/cipher-c-backtest-2026-10-22/`.

- **The confirmations are causal.** A bar-by-bar replay puts every confirmation, cancellation and
  failure on the same bar as the full-history run on BTC, ETH, SPY, QQQ, DIA and GLD.
- **Selling at the exact top** (hindsight) makes BTC +38% a trade, ETH +10%, the stock ETFs and gold
  +3–5%. But the top has often already passed when the low is confirmed: ETH 67 of 145 cycles.
- **Real exits are not an edge.** Against random entries with the same exit and stop, they range from
  worse than random (QQQ, ETH) to slightly better (DIA, SPY), a fraction of a percent a trade.
- **The previous cycle's translation held out of sample on stocks.** The cycle after a left-translated
  one beat the cycle after a right-translated one by about 2 points a trade in 12 of 13 (window exit)
  and 13 of 13 (20 bars) stock ETFs that were not looked at first. Part of it is that those lows fell
  further; within equal falls, about 0.75–2 points remain. Not on crypto. The ETFs share their years,
  and nothing has been walked forward.
- **Found on the way:** on XAU (twelvedata), whose file has stray weekend bars, the replay agrees on only
  42 of 150 confirmations. One weekend bar flips Cipher C's bar clock to "trades every day" and rescales
  every cycle. Not fixed (TODO).

### Cipher B's money flow strokes its edge as Market Cipher B does; Cipher B's dots, VWAP and "RSI" re-measured; Loukas Cycle Detection removed (2026-10-22)

Cody: "please go out and see if you can tackle those items regarding cipher b, the vwap, missing dots,
etc. … how do the red dots correspond to the real thing? … How does cipher dbsi now line up with the
reference screenshots, still off? … any additional fine tuning you can do with cipher A … loukas is
what we want to get rid of we don't need it. Please color match the money flow." Measured on the same
two Heikin-Ashi screenshots as before (S2, 398 bars; S4, 148 bars; BTCUSD 1D Bitstamp). Nothing here has
been seen or heard in the app.

- **Money flow colour.** The fills already matched: each side's two tones are what the 50% fill
  composites to over the background and over the other waves, within 6 per channel. What was missing
  was the rim. Market Cipher B strokes the value edge of the area at 50% over its 50% fill, so the edge
  composites to about 75% over roughly 2 px (measured 0.65–0.74 opacity against a body of 0.51).
  - New `IndicatorMetadata.DefaultAreaEdge` / `ComponentConfig.AreaEdge`: a histogram area strokes its
    value edge in the area's colour at the component's thickness. Never the baseline, never a line on
    its own. Clone and the factory carry it.
  - Cipher B's money flow sets it, at thickness 2: edge 0.68 against MCB's 0.68 on S4 (1.5 px gave
    0.63), body 0.51 in both.
  - Guards (`AreaFillEdgeTests`): the rim is at least 0.15 more opaque than the body and the baseline
    rows are not; without the flag there is no rim; the factory-built money flow has it, and Clone keeps
    it. The declared-knob sweep has a histogram-area cell, and it was red on `AreaEdge` before that cell.
    Proven by sabotage: no edge, an edge along the baseline, the factory dropping the flag, Clone
    dropping it — each red. Not guarded: the vertical sides at the start of a run.
  - Left as it is: Market Cipher B's zero line is white and dotted; ours is grey and solid.
- **Red and green dots, bar by bar.** The "missing" dots were the digitiser's, not ours.
  - S4: red 16 of 16 and green 15 of 15 on the exact bar.
  - S2 red: the digitiser found 40 and we draw 38; 32 are on the exact bar, 4 one bar early (S2 is
    3.7 px a day, so a one-bar offset is within the digitiser's error). The three it found that we lack
    are not dots: 2022-06-05 and 2022-06-12 are false detections (the real dots are on 06-03 and 06-09,
    where ours are) and 2021-07-09 is a fragment at the left edge of the picture.
  - S2 green: 36 found, 32 on the exact bar and 4 one bar early.
  - **The dots we draw that the digitiser did not find are Market Cipher B's too** (checked the same
    day): S2 red 2021-10-10 and 2022-05-05, S2 green 2021-10-11, 10-15 and 11-29, S4 red 2021-12-14,
    S4 green 2021-10-11 and 10-15. Cropped at each date (the axis ticks land on the date lines), MCB
    draws a dot on every one; they sit on the wave crest and the dotted level, where the digitiser
    lost them. Two of them were already inconsistent between screenshots of the same bars (12-14 found
    on S2, missed on S4; 10-10 the reverse). An earlier line here called them crosses under 1 point:
    wrong, they are 1.46 to 8.87 points apart.
  - **No threshold to tune.** Asked to try one: the smallest cross Market Cipher B does draw
    (1.15 points apart after the cross) is smaller than every one of these, so any threshold that
    dropped them would drop real dots too. The cross rule is unchanged.
  - The earlier entry's "the four S2 cross-downs missing are missing on raw too" was wrong and is
    struck through.
- **VWAP was already right.** The r 0.78/0.82 reported earlier came from a digitiser that picked up the
  red dots and the orange marks along with the VWAP. Re-digitised as the run joined to the zero line:
  S4 r 0.993, RMSE 1.24, sign 88 of 88, scale 0.98; S2 r 0.947, RMSE 2.81, sign 301 of 301, scale 0.89
  (a coarser picture). The corpus agrees: the dots print as the VWAP crosses zero, which is what a VWAP
  of wave 1 minus wave 2 does. No change.
- **"RSI" and "Stochastic RSI": heavily smoothed, and not a stock RSI.** CryptoFace's 2019
  introduction to Market Cipher (`StrategyLab/Corpus/transcripts/cryptoface/2019-06-17_bxkm4Kjubqs.txt`)
  calls the RSI "the heavily smoothed purple oscillator… oversold at 20… overbought at 80" and the
  stochastic RSI a "heavily smoothed red and green line", smoothed "to distinguish the divergences". No
  lengths are given anywhere in the corpus. Against 309 points of S2:
  - ours (a 40-bar stochastic of hlc3, SMA 3) is 2.13 points off; SMA 2 is 2.55, SMA 4 4.18, a 45-bar
    stochastic 5.4 or more;
  - the best true RSI of any length 2–40, SMA or EMA 1–30, on the close, hlc3 or Heikin-Ashi close is
    13.48 off (RSI 9 of the Heikin-Ashi close, EMA 5).
  So the line Market Cipher B calls RSI is not an RSI, and ours stays.
- **Cipher DBSI: unchanged since 2026-10-17, and still off.** Against the 144 numbers read off the
  references: bulls r 0.89, RMSE 3.68, exact 13%, within 2 58%; bears r 0.92, RMSE 3.33, exact 18%,
  within 2 52%; the side in control agrees 126 of 144. Ours cannot go above 16 (12 votes × 1.668 −
  4.3), but DBSI printed 17–25 on 33 of the numbers, where ours said 12–16. Closing that needs a
  magnitude term, and more reference screenshots to hold out; not attempted.
- **Cipher A: nothing to tune.** It is registered in both versions of the app and not deprecated, and
  it was already fitted on the Heikin-Ashi screenshots (entry below).
- **Loukas Cycle Detection removed** (deprecated 2026-10-21). Cipher C finds the same lows per asset.
  - `RetiredIndicators` has it: a saved chart opens without it and says once "Loukas Cycle Detection
    was removed on 2026-10-22, so it was left off this chart: Cipher C now finds daily, weekly and
    yearly cycle lows for each asset, which is what it did. To get the same lines back, add Cipher C."
    A strategy or alert on one of its signals says once that it cannot be read and what to use: DCL
    Confirmed on Cipher C for DCL Confirmed, WCL Confirmed for ICL Confirmed, the Daily Timing Window
    for DC In Window, otherwise "add Cipher C". The day counts and the four-year phase have no
    counterpart.
  - The provider, its tests and its three registrations (desktop, web, StrategyLab) are gone. The
    StrategyLab Cipher A battery lost its G6 gate (in a Loukas daily-cycle window); the strategy
    battery keeps its four Loukas cells as a record, and they now resolve to removed signals.
  - **The catalogue's v24 Cycle Low Reversal is retired with it**: its entry event was Loukas's DCL
    Confirmed. Its verdict stays in `CatalogueProvenance`'s retired list (walk-forward, positive in both
    halves, not re-tested on Cipher C's DCL Confirmed). Its two seed tests went with it.
  - New `LoukasRemovalTests` (7): the resume notice, said once across two tabs; a strategy on each of
    the three mapped signals and one unmapped one; an alert; and every Cipher C name the sentences give
    is a component Cipher C declares. Proven by sabotage: un-retiring the code turns all 7 red, and
    pointing ICL at a name Cipher C does not have turns 2 red.
  - Comments that spoke of Loukas in the present tense now say it was removed. No built-in indicator
    uses a sub-pane strip any more (Loukas's "FY" was the only one); the mechanism was removed the same day.

### A strategy can read its signals on Heikin-Ashi candles and trade real prices; backtests read the candles live does (2026-10-22)

Cody asked: "is basing strategies on ha candles a thing people do? Is using indicator values based on ha
something that would be useful in a strategy? If so, let's do it." Yes for signals. Heikin-Ashi trend
filters, colour-change entries and indicators run on Heikin-Ashi are common, and they are sound as long
as nothing fills at a Heikin-Ashi price. Filling there is the known trap. A Heikin-Ashi close is an
average of four prices, often one that never traded, so a backtest that fills at it shows profits no
real order could get. TradingView's strategy setting "Fill orders on standard OHLC" exists to avoid
this, and this option follows that model.

Fixed later the same day: `WorkspaceState.HeikinAshiSeries` was missing from the script sandbox's
projection census, so `WorkspaceProjectionTests` was red. It is now declared NotCarried (a script
strategy never asks for Heikin-Ashi signals; carrying the series would double the per-bar payload).

- **The option.** `StrategySpec.SignalsOnHeikinAshi` (default off), a checkbox in Build Setup labelled
  "Signals from Heikin-Ashi candles (orders, stops and targets on real prices)". It is said in the
  Summary read-back after the conditions, and survives load/save in the builder (`EditableStrategySpec`).
  Off, a strategy behaves exactly as before.
- **What reads Heikin-Ashi:**
  - every condition, on any chart: the indicators recomputed on Heikin-Ashi candles of the bars, and
    the price itself, projected from the Heikin-Ashi bars because the candle and close-line series
    carry real prices;
  - the "next N candles close in the setup direction" entry trigger.
- **What stays real:**
  - the entry price, stop and targets, including a stop or target placed on an indicator, which reads
    that indicator on the real bars;
  - the pullback and breakout trigger levels;
  - every fill and the profit and loss;
  - higher-timeframe leaves.
- **One builder for every host.** `StrategyCandles.For` builds the state strategies read in all three
  hosts: the live engine, a background tab's monitor and the backtester. The real series are in
  `ActiveSeries`; the Heikin-Ashi ones are in a new `WorkspaceState.HeikinAshiSeries`, computed only
  when an active strategy on that symbol asks for them. The StrategyLab command-line backtester gets the
  indicator service too.
- **Fails closed.** A Heikin-Ashi strategy with no Heikin-Ashi series does not trade the real bars
  under a Heikin-Ashi label. It does nothing and says once: "Strategy '…' reads Heikin-Ashi signals and
  none were computed for it. It will not trade until they are." A backtester without an indicator
  service refuses with a reason.
- **Defect fixed on the way (an existing gap, found while reading the code, not reported by a user).**
  A backtest started on a Heikin-Ashi chart, from the strategy dialog or the Summary preview, handed
  every strategy the chart's Heikin-Ashi indicators while filling at real prices. The live engine
  recomputed those indicators on the real bars, so the backtest could show trades the live strategy
  would never take. The backtester now builds the same state the engine does. A date-filtered backtest
  slices the Heikin-Ashi series with the real ones.

Caveats:
1. **Not seen or heard in the app.** The checkbox is new UI. The accessibility-lead review agent the
   hook asks for is not registered here, so it did not review the checkbox. The markup copies the
   labelled-checkbox pattern from the Alerts dialog.
2. **Scope.** Applies to condition-tree strategies only. A compiled (Roslyn) or plugin strategy ignores
   the flag.
3. **Custom compiled indicators.** They cannot be recomputed in this path, so their leaves evaluate
   false in a Heikin-Ashi-signal strategy (as they already did for any strategy on a Heikin-Ashi chart).
4. **Date-filtered warm-up.** In a date-filtered backtest, the strategy's Heikin-Ashi price history is
   seeded from the window's first bar, not the chart's. The difference halves with every bar, so it is
   gone well inside the 200-bar warm-up.
5. **Cost.** The live engine recomputes the chart's indicators once per bar close, while a Heikin-Ashi
   strategy runs. On a candle chart that is one extra pass; on a Heikin-Ashi chart it is the pass that
   already happened.
6. **Choices Cody can reverse:**
   - a stop or target on an indicator reads the real indicator;
   - the N-candle trigger reads Heikin-Ashi;
   - higher-timeframe leaves stay real;
   - with Heikin-Ashi missing, the strategy fails closed rather than falling back to real candles.

Tests (23 new):
- `HeikinAshiStrategySignalTests` covers:
  - an indicator leaf and a price leaf firing on a Heikin-Ashi-only close, on a candle chart and on a
    Heikin-Ashi chart, with the stop at 99 (real) and not 108.90;
  - the same strategy unflagged taking nothing, on a candle chart and on a Heikin-Ashi chart;
  - the N-candle trigger;
  - failing closed, said once;
  - backtests: the fill at 100 with the stop at 99, the Heikin-Ashi-chart gap, date-filter slicing,
    and the refusal.
- Engine and background-monitor tests: the Heikin-Ashi series are handed over only when asked for.
- Builder round-trip and reset, and the read-back sentence.
- Each guard was proven to fail by sabotage (21 sabotages, all red). The first run left one green:
  keeping the price lines passed on a candle chart, because the test's indicator service does not
  know "CANDLES". The Heikin-Ashi-chart price case was added for it.

### With Heikin-Ashi on, every close you hear is the Heikin-Ashi close; DBSI is "Cipher DBSI" (2026-10-22)

- Cody: "if ha is on and a bar closes, speak that close, simple as that. if ha is off, speak the
  real close." That is what TradingView does: on a Heikin-Ashi chart `close` IS the Heikin-Ashi
  close, for an alert and for a script. Until now the indicators had moved to Heikin-Ashi (entry
  below) while every spoken price stayed real, so "price crosses EMA" compared a real close with a
  Heikin-Ashi EMA.
- **What says the Heikin-Ashi close and open now** (all through `ChartMath.BarAsDrawn`,
  `BarsAsDrawn` or the new `SeriesAsDrawn`, which return the raw bars when Heikin-Ashi is off):
  - the new-bar announcement (`AccessibilityFeedbackCoordinator`), the toast with the window hidden
    (`DesktopNotificationService`), a background tab's sentence and the bar it publishes
    (`BackgroundBarAnnouncer`), and the browser-closed sentence (`LocalBackgroundMonitor`);
  - price and candle-pattern alerts (`AlertEvaluator`: the two closes a crossing compares, and the
    three bars a pattern reads), in the session, on background tabs and with the browser closed
    (`HeadlessChart` and the monitor's bare state carry the tab's candle type);
  - price crossing a line or a moving average: the overlay and zone-line narration and every other
    close the narration compares (`NarrationScanner.CloseAsDrawn`), Ctrl+Left/Right to the next
    crossing (`IndicatorCrossingEngine`, both the drawing and the moving-average jumps), and the
    crossing chirp (`LevelCrossingMonitor`);
  - `{price}` in a narrated marker and in playback narration (`PlaybackNarration`).
- **Stays raw:** with the close line itself focused (`CoreSeriesIds.Price`, which is always drawn
  from the real closes), Ctrl+Left/Right and the chirp follow the line you hear, the real close.
  Orders, fills, positions, P&L and the screener never use Heikin-Ashi.
- **Found while building it:** a price alert on a saved Heikin-Ashi tab with no indicators read the
  real close with the browser closed. The watch took its candle type from `SavedTabFor`, which skips
  tabs with no series; it now reads `SavedChartTab`, the exact chart whatever it holds.
- **Cipher DBSI.** Cody: "call dbsi cipher dbsi". The indicator's name is now "Cipher DBSI"
  everywhere it is shown; its code stays `DBSI_STYLE`, so saved charts, alerts and strategies keep
  working. It is still our score inspired by Market Cipher DBSI, not the vendor's formula.
- **Cipher A, re-checked on Heikin-Ashi** (Cody: "retune cipher A and see how close you can get").
  No change was needed: the fit was made on the Heikin-Ashi screenshots already, and a grid search on
  Heikin-Ashi bars picks the same rules (Red Diamond WT hlc3 9/12/3, F1 0.91 against 0.81 for the
  next; Blue Triangle 0.90; Green Dot EMA 11 x 33, 0.90). On the exact bar, Heikin-Ashi feed against
  a raw feed: Green Dot 17/18 vs 11/18, Red X 49/50 vs 23/50, Blue Triangle 22/25 vs 17/25, Blood
  Diamond 9/9 vs 4/9, Yellow Diamond 158/161 vs 143/161, Yellow X 24/28 vs 23/28, Red Diamond
  recall 129/144 vs 111/144. The CHANGES entry below that said "Cipher A was fitted on raw bars" was
  wrong.
- **Caveats:**
  - the hosted web version's server-side alert monitor (`HostedAlertMonitor`, browser closed) still
    reads the real close: it has no saved tab to read a candle type from;
  - the narration's individual close comparisons (profile, seeds, cloud and zone lines) are covered
    through the one helper they share; only the overlay crossing and the marker price have their own
    tests;
  - not seen or heard in the app.
- Tests: the new-bar sentence (`NewBarNarrationTests`), background tabs (`BackgroundBarAnnouncerTests`),
  the toast (`DesktopNotificationServiceTests`), the browser closed (`HeadlessNarrationTests`, both
  the bare state and a headless chart with a narrated EMA; `HeadlessAlertTests`), alerts
  (`AlertEvaluatorTests`, price and pattern), the chirp and Ctrl+Left/Right with and without the close
  line focused (`LineDrawingCrossingEarconTests`, `LineDrawingCrossJumpTests`), the marker and playback
  `{price}` (`AutoNarrationTests`, `PlaybackNarrationScopeTests`) and the name (`DbsiStyleProviderTests`).
  18 sabotages each turned their test red; two stayed green on the first run (the headless chart's
  flag and playback's `{price}`, whose tests went through another path) and got tests of their own.

### On a Heikin-Ashi chart, indicators compute on the Heikin-Ashi candles; strategies stay on the real bars (2026-10-22)

- Cody: "when hiken ashi is on, have indicators use those candles ... whatever tv does, that is what
  I want." TradingView hands an indicator on a Heikin-Ashi chart the Heikin-Ashi open, high, low and
  close, with volume unchanged. Until now every indicator here read the raw bars.
- **What changed:** `IndicatorCandles` (Core) stamps a hidden `__heikinAshi` parameter, and
  `IndicatorService` swaps in Heikin-Ashi candles of the whole loaded history on every full, last-bar
  and prepend calculation. Every path stamps it: the focused chart (`IndicatorOrchestrator`), custom
  compiled indicators (`IndicatorEngine`), headless charts and background tabs (alerts and the
  background new-bar ladder read the tab's saved candle type). Switching Heikin-Ashi on or off now
  forces a full recalculation (`DataOrchestrationService`); before, nothing changed in the bars, so
  nothing would have recomputed.
- **Market Cipher B, measured against its own Heikin-Ashi screenshots** (S2, 398 bars; S4, 148 bars;
  BTCUSD 1D Bitstamp). Heikin-Ashi everywhere was not what MCB does, so Cipher B builds its own
  (`IComputesHeikinAshiItself`):
  - waves and VWAP read Heikin-Ashi: slow wave RMSE 7.2/6.3 raw to 6.5/6.2, envelope 8.0/6.9 to
    6.5/6.2;
  - the money flow already took Heikin-Ashi of the bars it is given; on a Heikin-Ashi chart that is
    Heikin-Ashi of Heikin-Ashi, and that fits best: 6.2/6.5 to 4.6/4.8, sign agreement 0.96/0.99;
  - the "RSI" and "Stochastic" lines read the REAL bars on a Heikin-Ashi chart: on Heikin-Ashi they
    were 3.5/1.9 (S2), on the real bars 2.1/1.2. They are MCB's own lines, not a stock RSI or
    stochastic, which we already knew from the fit;
  - big green dots on the exact bar: 5/5 and 3/3, from 2/5 and 1/3. Cross dots within one bar: S2
    up 36/36, down 36/40; S4 up 15/15, down 15/16. ~~The four S2 cross-downs missing are missing on
    raw too.~~ Wrong, re-checked the same day: three of them were digitiser errors (entry above). For scale, the VuManChu port scores 7.8/8.5 on the same charts (the digitising floor).
    S3 and S5 not re-measured.
- **DBSI** (now "Cipher DBSI", entry above) applies Heikin-Ashi once on a Heikin-Ashi chart, whatever its own Heikin-Ashi setting says.
- **Cipher A and Cipher C follow the chart.** (Cipher A was said here to be fitted on raw bars; it
  was fitted on the Heikin-Ashi screenshots, see the entry above.) Value Deviation's zones sit at the Heikin-Ashi bar extreme on a Heikin-Ashi
  chart, as on TradingView.
- **Stays on the real bars:** the candles' own readouts, volume, profiles, heatmap, drawings, chart
  formations, the screener and StrategyLab. **Strategies** recompute the chart's indicators on the
  real bars before reading them (`StrategyEngine` via `EvaluationSeries`; background tabs the same),
  so a strategy trades what the backtester tested. TradingView runs strategies on Heikin-Ashi and
  warns the results are not real; here they never are.
- **Fixed on the way:** the renderer built Heikin-Ashi candles from the visible slice, so a panned
  view drew candles that the readouts and indicators did not have. It now draws the tail of the whole
  series (`ChartMath.BarsAsDrawn`).
- **Caveats:**
  - ~~price in an alert is the real price~~ — answered the same day: every close is the Heikin-Ashi
    close on a Heikin-Ashi chart (entry above);
  - a custom compiled indicator cannot be recomputed in the strategy path, so its leaves evaluate
    false in a strategy on a Heikin-Ashi chart;
  - a background tab's candle type is read when its monitor starts; switching it later takes effect
    at the next restart of that monitor;
  - not seen or heard in the app.
- Tests: `HeikinAshiIndicatorCandlesTests` (the swap on every path, Cipher B's split against MCB's
  choices, DBSI once, strategies raw on Heikin-Ashi and untouched otherwise, headless and background
  tabs, the panned candle in pixels), `HeikinAshiWatchTests` (WebHost watches carry the tab's candle
  type), the toggle recalculation in `StaleIndicatorOnAssetChangeTests`, and a background tab's ladder
  on Heikin-Ashi in `BackgroundBarAnnouncerTests` (whose harness now registers the moving averages,
  as the WebHost does). 24 sabotages each turned their test red. 637 related tests pass locally; the
  full suite runs on GitHub Actions.

### Cipher B's gold dot removed; its rule kept as a part nothing uses (2026-10-22)

- Cody: "remove the gold dot from the cipher b indicator as it isn't faithful. Keep the gold dot as
  a drawing element because something may want it later." Market Cipher B draws no gold dot; ours
  was VuManChu Cipher B's `wtGoldBuy` (default since 2026-10-18), with the older K-of-4 confluence
  rule behind `GoldDotMode = Confluence`.
- **Gone from Cipher B:** the "Triple Confluence Buy" component ("Gold dot"), its speech and status
  line value, and nine settings that served only it: Gold Dot Rule, Gold RSI Divergence, RSI Period,
  RSI Oversold Level, ADX Period, ADX Gate, ATR Period, ATR Floor and Gold Min Confluence. An old
  save that still carries them is ignored, not an error. The stability window drops from 508 to 500
  bars (the RSI term was the gold dot's).
- **Kept, used by nothing:** `GoldDot` in Core holds the ported rule (`GoldDot.Rule`, verbatim, with
  its hand-built tests) and the look and sound (`GoldDot.Component`: #FFD700 dot, the 440 Hz
  two-tone bell in the foreground). I read "drawing element" as this; a user-placed gold marker in the
  drawing tools was not built (question in TODO).
- **Saved charts and strategies:** `SignalCatalog.Retired` gives `CIPHER_B.Triple Confluence Buy`
  its reason and points to the Buy dot. Cody's saved Cipher B restores without it and the next save
  drops it. The three bull-pulse catalogue strategies and two battery cells keep their gold leaf (it
  now evaluates false with that reason), the precedent set by their dead Cipher A and SR leaves.
- **StrategyLab:** `--cipherb-gold` is refused with the reason (a run would otherwise compare two
  identical results); `--cipherb old` sets three parameters, not four; MlExport drops the
  `sig_gold` column and `ml/train.py` its feature.
- **Proof nothing else moved:** every other Cipher B output array was hashed on the commit before
  (ea37fdf3) and after, on the four causality probe series, at 1400 and 333 bars, under six
  parameter sets (including the old gold settings): 1,297 arrays, all identical. The pre-refactor
  WaveTrend fingerprint test lost only its gold line.
- Tests: the gold tests now cover `GoldDot.Rule` (hand-built cases, and causality on Cipher B's own
  slow wave), the kept look, Cipher B having no gold component/setting/output, the retirement
  reason, and Cody's save. Five sabotages each turned their test red (gold back in Cipher B; no
  retirement notice; the rule drawn on the bottom bar; the kept bell retuned; the lab swallowing
  `--cipherb-gold`). 757 related tests pass locally; the full suite runs on GitHub Actions.

### A window is heard in playback, said on new bars; a low found in hindsight is said once, with its date (2026-10-22)

- Cody: "I don't think manual left/right should sonify anything, but, during chart playback I
  should hear a slight texturing under the chart ... surely that should be added to the narration
  ladder on new bar closes. also on new bar closes if an indicator picks a low in hind sight, that
  should be announced ... cycle low confirmed on previous date". Then: "Yes please do all 4."
- **Left/Right on a window plays nothing** (`NavigationSonifier` mutes every navigation voice when
  the focused component is a `Window`). Landing on it still speaks its count.
- **In playback a window is a quiet texture under the chart while it is open**: 110 Hz with brown
  noise (`AudioSequencer.PlayWindowTexture`), at half the background layer's level, panned with
  the playhead, silent on closed bars. Same in multi-series playback. Open is
  `WindowSpans.IsOpenAt`, the same rule as the shading. The Window profile's tone went from 330 Hz
  to 110 Hz to match. **Not heard; the level and colour are a first guess to tune by ear.**
- **New-bar narration says a window opening and closing** ("Daily timing window open, day 1.",
  "Weekly Timing Window closed."), at the crossing tier, from its `_stops` companion when it has
  one, or from where it turns open or closed. Cipher C's two windows declare the opening sentence
  (`DefaultSignalSpeechTemplate` with `{value}`); a window with none says "{name} open."
- **A mark painted in hindsight is said once, on a bar close, with its date.** A component whose
  causality is `Lookahead` (Cipher C's lows and tops; Ichimoku, swing structure and the zero-cross
  pivots declare it too) is now `ComponentConfig.IsHindsight`, set by the factory. For those:
  - never on an intra-bar tick (a tick can paint the mark and the next take it away);
  - no 20-bar limit: a low found 30 bars late is still said. Turning narration on seeds the last
    mark already painted, so the history is not read out;
  - said with the date of the bar it is painted on: "Right translated top, bullish, on March 17 2026."
- **A low its indicator confirms is said once, with its confirmation**: a hindsight component
  that names another as `ConfirmedBy` (new, `IndicatorComponentMetadata` and `ComponentConfig`)
  is silent while that one is shown and narrated, and the confirmation says where the low is. On
  real Bitcoin daily bars: "Daily cycle low confirmed, on March 29 2026 at 64955.00, 6 bars ago."
  With the confirmation hidden, the low is said by itself, with its date.
- **`_confirms`** (`ComponentCompanions.Confirms`, new): on a confirmation bar, how many bars back
  the mark it confirms is drawn. Cipher C writes it for all three tiers. Without it, narration
  falls back to the latest mark at or before the confirmation, which **was wrong on 44 of 133
  Bitcoin confirmations** looked at over the whole history (that low was cancelled later, or a
  weekly or yearly low was moved onto a daily one). Cipher C's own confirmation speech (`_low`,
  `_ago`) now names the low where it is drawn too: a weekly or yearly confirmation used to give
  the price of the unmoved low.
- **Fixed on the way:** `WorkspaceProjectionTests` was red on this branch since 60256450 added
  `WorkspaceState.RestoredLoad`; it is declared host-side (not carried to scripts).
- Caveats: none of this has been heard in the app. A component of a script indicator is never a
  hindsight mark (scripts do not declare causality). Indicators still compute with the forming
  bar, so a hindsight mark can appear and vanish during a bar; narration now waits for the close,
  the chart still shows it.

### Windows, fill under the line, and the cycle arc as a drawing (2026-10-22)

- Cody: "Move forward with window type, area fill in appearance, and if the arc is more akin to
  the chart formation lines and drawings, then leave it as such, just a drawing".
- **`ComponentDisplayType.Window`** (Sdk, appended as 29): a stretch of bars that opens and closes.
  Its value is a count to hear, never drawn as a line and never on the value axis. It is open where
  its `_tint` companion is at or above the tint midline, or (with none) wherever it has a value.
  The open bars are shaded (This pane unless the indicator says otherwise; Appearance wins),
  Ctrl+Left/Right steps where it opens and closes (or through its `_stops`), the Dot Pad raises the
  open bars full height and leaves closed ones flat, and the status line shows the count it says.
  One helper (`WindowSpans`) decides "open" for the shading, the keys and the Dot Pad.
  Spoken as "window"; a steady 330 Hz sine with no pitch mapping for now (not heard).
- **Cipher C's Timing Windows are Windows**: landing on one no longer says "Line".
- **A component's drawing, `_path`** (`ComponentCompanions.Path`, `PathName` in the metadata): a
  line in the component's own units, in its colour and thickness. Not a stop, not a sound. Landing
  on the component names it ("Window, with the cycle arc") while it is drawn and has data; the
  speech template's `{type}` says the same. Properties → Appearance → "Draw the cycle arc" turns it
  off (`UserDrawPath`); it counts toward the fitted price range, so it is not cut off.
- **The cycle arc is back as the Timing Window's drawing** (code from 8421c5af): one arc under the
  candles from each cycle low to the next, and a live arc from the last low to the expected length.
  **Look-ahead**: an arc is drawn once its ending low is known. `CipherCProviderTests` now excludes
  `_path` from the causal check, and a new test pins the look-ahead (the arc changes when bars are
  added) and that every arc point sits under its bar's low.
- **Fill under the line in Appearance** (`UserIsAreaFill`): "Fill under the line", or "Draw as a
  filled area" on a histogram, on or off; the choice wins over the indicator's. Offered only where a
  fill is drawn (a plain line or oscillator, or a histogram coloured by value), not on levels, lines
  coloured by rules or by another line, or candle-coloured bars. Saved with the chart and with Save
  as Defaults; a chart's own choice wins over the default.
- `,` and `.` stay with chart formations (see TODO for the reasoning).
- **Fixed on the way:** the declared-knob sweep (`DeclaredKnobObservabilityTests`) was already red on
  this branch for `IsCount` and `MarkerText`, from unpushed commits CI never ran. It now also reads
  the status line, so a knob that changes only what is printed there counts as observable, and has
  ValueLabel and Window cells. A history-limit message from 60256450 ("History limit reached: 5,000
  bars") formatted its number in the current culture; `CultureInvariantScanTests` caught it.
- Tests: `WindowComponentTests` (open spans and edges, the spoken drawing name, the fitted range,
  the Dot Pad, the fill choice and where it is offered), three pixel tests in `BackgroundTintTests`
  (a window shaded where open, no line through its count, the drawing drawn and switched off), the
  window-edges jump in `CrossingMessageTests`, the fill and drawing preferences in
  `ComponentPreferenceChoicesTests`. Fourteen sabotages, one per guard, each turned it red (one
  first survived because the renderer's default branch draws nothing; redone as "drawn as a line").
- Not seen or heard in the app. Cipher C's weekly window on a daily chart has no tint and no arc,
  so its Dot Pad view is blank and its "Draw the cycle arc" switch does nothing.

### Alt+Shift+D reads in one order, whichever series has focus (2026-10-22)

- Cody: "with alt shift d it reads the current focused indicator first, then the candles and
  everything. It should read all series/components ... from top to bottom starting with candles
  otherwise the reading order is changed every time you navigate to a different series". It now
  reads the date, the candle, then every visible series in the status line's order (price pane,
  then the panes top to bottom), then the formations. The focused series is read in its place and
  in the same words as when it is not focused; it used to come first, in a longer form of its own.
  A focused series that is hidden (not on the ladder) is read after the others.
- A chart with nothing shown now says "Nothing to describe at this bar: no series is shown on the
  chart." (it said "No series in focus", which no longer decides anything here).
- Cipher C's preset words are now said wherever Cipher C sits on the ladder, focused or not.
- Lost: the focused series' longer form read every component with a value, including ones the
  status line leaves out (a component with no reading of its own), and a gradient component's
  colour value; the ladder reads what the status line prints. Not heard in the app.
- Tests: `BarDetailReadsEverythingTests` checks the order and the exact words for four different
  focuses, and a focused hidden series; putting the focused series back first turned it red.

### Cipher C cut from 28 components to 15, each one worth landing on (2026-10-22)

- Cody: "Each component must hold value, simply hiding and muting it isn't what I want. hiding and
  muting should be a user defined preferential function". Cipher C had thirteen hidden or muted
  components that only held a number for another one. They are gone; each number is now a
  **companion** (`ComponentCompanions`, Sdk): an array keyed `"{component}_{suffix}"` beside the
  component, handed to its speech, with no keyboard stop or sound of its own. All fifteen that remain
  are shown and heard by default. The parts and what each says are a table in
  `docs/CIPHER_C_DESIGN.md` section 3.
- The lows say their price, their cycle length and when they were confirmed ("Daily cycle low,
  64955.00. A 52 day cycle. Confirmed 6 days later."). Confirmed carries the confluence (1 to 3) and
  says the low and any cancelled low it replaces. Pending says the lowest low since the last cycle low;
  Cancelled and Failed say the close and the low it went under, and how far back; the tops say their
  high and which day of how long a cycle. One Daily and one Weekly Timing Window replace DCL Day, WCL
  Week, In DCL/WCL Window and the four window-bound markers: the value is the day (week) of the cycle,
  the price pane is tinted while inside the window, and Ctrl+Left/Right stops where it opens and closes.
  The preset is said by Alt+Shift+D. The Cycle Arc wave and its Arc Position are gone (a picture with
  nothing to navigate). Each removed name has a retired-signal message in `SignalCatalog`; a saved
  chart with the 28 restores with the 15.
- New for any indicator, read by the chart: a `_label` companion prints a number beside a marker on
  the far side of the bar (49D above a low whose triangle is below); a `_tint` companion is what a
  background tint is painted from, so a component can hold a count and tint by a state; a `_stops`
  companion sets the bars Ctrl+Left/Right stops on. `IsCount` (IndicatorMetadata / ComponentConfig)
  makes the status line and Alt+Shift+D say a whole number ("47", not "47.00").
- Fixed on the way: the Confirmed, Cancelled and Failed templates said "Close {price}", and the
  cross-series signal list fills `{price}` with the component's own value, so it spoke the confluence
  score or the low as if it were the close. The templates no longer carry `{price}`.
- Fixed on the way: "a 82 day cycle" and "yearly 4 to 4 years" (an/a by the number as said; a range
  whose ends are equal is said once).
- Tests: `CipherCProviderTests` (the 15 and their defaults, each one through the real speech path, the
  companions through the real engine and mapper, causality including the causal companions, the
  restore and retired names), `ValueLabelRenderingTests` (49D in pixels; a count on the status line),
  `BackgroundTintTests` (tint from the `_tint` companion), `CrossingMessageTests` (stops). Nine
  sabotages, one per wiring point, each turned its test red. Not heard in the app. Caveats: the yearly
  low on Bitcoin is confirmed about 254 weeks after it (an engine behaviour, not changed here, see
  TODO); landing on a Timing Window still says its shape, "Line", though nothing is drawn.

### Restored workspace tabs open with the history and zoom they had (2026-10-22)

- Cody: "the workspace should load with all of the bars it had when it was closed last rather than
  defaulting to 200, 200 is the default for new fresh charts". Each saved tab now records the date
  of its first bar (`TabConfiguration.EarliestBarDate`; a date, not a count, so autosave does not
  rewrite on every live bar). On restore the tab loads back to that date, or to its oldest drawing
  anchor if that is older (`WorkspaceInitializer.HistoryFromForTab`), so a trend line drawn from a
  top months back is drawn from its real start. Fresh charts and Load Chart still open with 200 bars.
- How: the restore puts a `RestoredTabLoad` (the chart it belongs to and the date) on the tab's
  state. `DataManager.RefreshDataAsync` reads it, `ChartFeed.RefreshAsync` fetches the usual live-edge
  200 and then 200-bar older pages (the same pages scrollback stores locally) and replaces the buffer
  once, so the indicators compute once. It stops at the date, when the provider has nothing older,
  or at 5,000 bars, and a failed older page keeps what was loaded. A tab restored but never visited
  keeps its depth when the workspace is saved again. A different symbol loaded first gets a fresh chart.
- Fixed on the way: the restored zoom width was set and then reset to 100 by the fresh-load path,
  so every restored tab opened 100 bars wide. A restored tab now keeps its zoom.
- Fixed on the way: scrolling back at the 5,000-bar cap fetched 200 older bars and dropped the 200
  newest to stay under it, losing the live edge, then said "No more history available". At the cap
  nothing is fetched and it says "History limit reached: 5,000 bars."
- Tests: `RestoredHistoryDepthTests` (real restore, store, DataManager and ChartFeed over a scripted
  provider) and one in `HistoryBackfillCoordinatorTests`; each of the seven guards was shown to fail
  with its fix removed. Not tried in the app. Caveats: a page that throws mid-way is not tested, and
  workspaces saved before this carry no depth until saved once.

### Trend lines from the study written into a workspace as ordinary drawings (2026-10-22)

- `WorkspaceDrawingTool` (test project) adds Trend line and Text label drawings to the tabs of a saved
  workspace and writes it the way the app saves a profile. It runs only when `AT_WORKSPACE_SPEC`
  names a spec, so it never writes in CI. It reads the file back, recomputes every drawing from its
  saved anchors and writes the line's price at each anchor and at the newest bar to a report.
- `DrawingPictureTool`'s drawing builder is shared with it. Drawings are named as the app names them:
  "Trend line (n)", and "Label: text" for a text label (was "Label (n)").
- Used to make `strategy-lab-data/trendlines/Camel cycles trend lines.json`. Caveat: the bars were
  Yahoo's (SPY, DIA, GLD, BTC-USD), not Alpaca's or Bitstamp's; the anchors are dates and prices, so
  the lines land on the same days, but a wick that differs between feeds can sit a little off the line.
  Not opened in the app yet.

### Text labels stay whole, stack, and draw characters the default font lacks (2026-10-21)

Found while drawing the trend line study's pictures with the real drawing tools: two labels on the
newest bars were cut off under the price axis mid-word ("daily close above th"). A stress chart of
nine labels (right edge, top, bottom, same bar, neighbours, long text, non-Latin text; dark at 1x and
Paper at 2x) then showed four defects, all in `OverlayLayer.RenderTextLabels`:

- **Cut off at the right edge:** the plate always started at the anchor and ran right. It now slides
  left as far as needed to end at the pane's edge; a label wider than the pane is shortened with "…".
- **Overprinted:** two labels on one bar at about one price printed over each other and neither
  could be read. A label that would cover one already placed now moves just below it (above when
  below has no room).
- **Empty boxes for missing characters:** the default face has no check mark (and no CJK here).
  Each character the face lacks now comes from whatever installed face the font manager finds.
  Japanese still shows boxes on this machine because no installed font has it.
- **Vertical:** the plate flipped below its anchor once and was never kept inside the pane; it is now
  clamped top to bottom.
- **Drawn in strips:** the overlay layer also runs for the strips stacked inside the price pane, with
  the strip's own scale. A label's price could land inside the strip and be drawn a second time
  there. Labels now skip strips. Found by reading the code; never seen on screen.
- A plate that was slid or stacked no longer starts at its bar, so its anchor gets a small dot and a
  line to the plate.

Placement is the new `OverlayLayer.LayoutTextLabels`. Tests are in `RenderLayerTests`: the newest
bar's label ends inside the pane, a too-wide label is shortened, two labels on one bar do not
overlap, the plate stays inside at four prices, nothing is drawn in a strip, and characters come from
a face that has them. Six sabotages each turned their test red and were restored green: no right
clamp (2 red), no stacking (1), no vertical clamp (1), no strip guard (1), no fallback (1), no
ellipsis (1). The fallback test passes trivially where the default face has every character, as it
may on CI. Only pictured offscreen (`DrawingPictureTool`, below); Cody has not seen it in the app.

`DrawingPictureTool` (tests): renders real bars plus drawings made the way the drawing tools make
them (the real calculators, component configs and renderer) to PNG from a JSON spec. It does
nothing unless `AT_DRAWING_SPEC` is set, so it never writes in CI.

### Cipher C's low markers carry letters, check marks and counts (2026-10-21)

Cipher C now uses three append-only marker shapes to match the glyphs in Camel Finance's CF Cycle
Trading Indicator demo: lettered triangle rings for the cycle lows, white check marks in green rounded
boxes for confirmations, and white circular arrows in grey-blue rounded boxes while a low is pending.
The DCL and WCL count labels print `49D` / `24W`; their spoken readings remain the provider's existing
"49 day cycle" / "24 week cycle" forms. White stays white on both dark and light chart themes.

- **Metadata and saved charts:** D/W/Y ring letters, check/refresh shapes, colors and sizes are declared
  by Cipher C metadata. `MarkerText` is copied to `ComponentConfig`, cloned and re-derived from current
  metadata when a saved chart loads, so old saves acquire the visual annotations too. New ordinals are
  appended after `ValueLabel` (25): `CheckBox` 26, `RefreshBox` 27, `TriangleUpRing` 28.
- **Evidence:** CF's demo frames as summarized in `docs/CIPHER_C_DESIGN.md` section 3: the daily D
  ring, weekly W stack, check mark, refresh arrows and `49D` count. The review PNGs are in
  `strategy-lab-data/shapes/`. The owner has not yet seen these rendered markers or this change.
- **Box size (Claude, after viewing the review PNGs):** at Luna's sizes (the old square's 3 / 4 / 5,
  pending 2.5) the daily check box was 6 px across at 1x, smaller than the triangle it sits with, and
  the refresh arrows read as a dot. Cipher C now draws them at 4.5 / 5.5 / 6.5 and pending 4: the check
  and both arrows are legible and the box is about the triangle's size. The renderers' size rule is
  unchanged (same as `Square`); only Cipher C's metadata and its pinned sizes in
  `CF_marker_metadata_declares_ring_letters_check_boxes_refresh_box_and_count_suffixes` moved.
- **Still not built:** a green cancellation box around the original low stack (ours
  remains a red cross on the cancelling bar), CF's check beneath the original low stack (ours is above
  the confirming bar), and LOW/MID/HIGH text under the stack (ours is spoken only). Future FCLR boxes
  and later confluence upgrades remain open as well; see `docs/TODO.md`. The camel emoji is not
  being built (Cody declined vendor branding).
- **Red-first:** the 14 new/changed cases in `CipherCProviderTests`, `MarkerSizingTests` and
  `ValueLabelRenderingTests` were run before implementation: 14 expected failures, 210 passed. After
  implementation the focused filter passed 230/230; the wider filter passed 1,920/1,920 and the
  source-reading classes passed 867/867. `CipherCProviderTests` now has 84 passing cases.
- **Sabotage:** drawing `CheckBox` without its check turned 2 cases red; dropping `MarkerText` in
  value-label layout turned 2 red; omitting the factory's metadata copy turned 2 red. Each file was
  restored from `strategy-lab-data/sab/`, verified clean with `git diff --quiet`, rebuilt, and the
  230-case focused filter returned green.

### Cipher C names no vendor (2026-10-21)

Cody: "this is not supposed to be representative brand wise so remove camel finance text or speech,
just cycles." Cipher C's description, parameter help, spoken preset source and the retired-signal
messages no longer name Camel Finance or CF. The preset is now spoken as "Gold ranges, tuned: ..." or
"Dow Jones ranges, estimated: ...", and a symbol with no preset as "no preset for this symbol, so
Bitcoin's ranges". WHATSNEW and the README feature line say the same. The camel emoji from CF's look is
not being built. Code comments, `docs/CIPHER_C_DESIGN.md`, the cycle-school notes and the research
corpus keep the provenance, since that is where the ranges came from. Tests:
`The_preset_is_said_with_its_source` pins the new spoken words; the new
`Nothing_the_user_reads_or_hears_names_a_vendor` checks the whole metadata and every preset's spoken
words for "Camel" or "CF", and the retired-signal test checks its messages. Sabotaged four ways (a "Camel uses 10" parameter help,
the old no-preset words, "estimated, not CF's", one retired message with the vendor clause): each
turned 1 or 2 tests red, and back to green on restore.

### Cipher C's arcs are on the price chart, as CF draws them (2026-10-21)

Cody, looking at the new Cipher C on his Bitcoin chart: "I simply want it to look like the original
indicator as much as possible." The Cycle Arc was a wave in its own strip under the price, with its
own axis (1.000, -0.6). CF's vendor demo shows a row of equal mustard humps on the price chart, well
under the candles, each trough under a cycle low's marks. It is now drawn that way.

- **Where it sits:** each hump is a quarter of the median cycle's swing tall; each low's trough is one
  and a quarter humps under the lowest low of the two cycles it joins. The level steps from one low
  to the next and is flat at each low, so the trough stays on the low's bar. CF draws one level for
  the whole screen; ours steps, because one level across fifteen years of Bitcoin would be useless.
  The height and offset are our choice; CF lets the user set them, and ours are not settings yet.
- **Saved charts move too:** the strip came from the component's metadata, which a saved chart
  re-reads on load, so an existing Cipher C loses its strip.
- **The price pane fits the arcs,** so the candles use less of the pane than before (on Bitcoin daily
  the troughs sit about 15% under the cycle's low). Not yet seen on screen.
- **What it says is unchanged:** "mid-cycle" or "N percent of the arc", now from a hidden Cycle Arc
  Position number, since the line's value is a price.
- **Tests:** a new test checks, on Bitcoin's daily history, that the arc is under every candle, has a
  trough at every low, and that every hump is the same height over the line between its troughs
  (0.116 to 0.125 in log price). Putting the strip back, raising the crest into the candles, using a
  straight floor between lows, and scaling the hump by cycle length each turned it red. 76 Cipher C
  tests and 580 in the wider indicator filter pass.

### Bitcoin's cycle ranges are now CF's own table (2026-10-21)

Two reviewer videos of 7 to 9 August 2025 show CF's "BTCUSD CYCLE COUNT" table on a Bitstamp daily
chart: daily 54-66 days, weekly 22-42 weeks, yearly 4 years (`strategy-lab-data/cf-frames3/frames/`
`s4EfttpMNXA/35.jpg` and `qVlnivWr2ho/0.jpg`, gitignored; read by OpenCode/Luna and checked by eye).
Cipher C's Bitcoin preset was 54-66 days, 28-32 weeks and 3.5-4.5 years, from what Camel says on his
2026 streams. It now uses the table and is marked as CF's, so nine of the sixteen presets are CF's.

- **The weekly window is much wider (22-42 against 28-32).** The indicator is what Cipher C copies,
  so the table wins over Camel's spoken band. The same table showed "Weeks Since Last WCL : 43",
  already past its own 42.
- **What still passes:** the pinned Bitcoin lows (4 February and 29 March 2026) and the 5 August 2024
  weekly low. All 74 Cipher C tests and 578 in the wider indicator filter pass. A test row pins the
  new preset; it was red before the change, and changing the weekly maximum to 41 or the source to
  estimated each turned it red.
- **What "Days since Last DCL" counts:** that table read 46 on a frame whose own clock says 7 August
  2025, and 46 days back from 7 August is 22 June 2025, Bitcoin's swing low ($98,286). So the number
  is bars from the low to the last bar. Across all 14 tables read so far that reading fits 5, but
  most misses are frames whose last-bar date could not be pinned. Not yet used anywhere.
- **Still estimated:** NDQ, DJI, DXY, SILVER, USOIL, GDXJ and SILJ. Twelve more videos were searched
  and none showed their tables.

### CF cycle ranges added for five assets and gold updated (2026-10-21)

Cipher C now uses CF's own cycle-range tables for COIN, MSTR, MARA, RIOT and GDX, read by eye from
reviewer videos from August 2025, and the newer gold table. The evidence is in the checked frames
under `strategy-lab-data/cf-frames2/`: COIN `frames/76LvfFq1e9M/5.jpg`; MSTR
`frames/76LvfFq1e9M/200.jpg` and `frames/m3u0zvEi65I/5.jpg`; MARA
`frames/76LvfFq1e9M/250.jpg`; RIOT `frames/76LvfFq1e9M/300.jpg`; GDX
`frames/76LvfFq1e9M/400.jpg`; and GOLD `frames/HhZgHv6dhtM/40.jpg` and
`frames/Jexw-gt4jzA/100.jpg`.

- **COIN and MSTR:** 40-90 daily trading days, 24-42 weeks, 4 years.
- **MARA and RIOT:** 36-44 daily trading days, 22-26 weeks, 4 years.
- **GDX:** 22-28 daily trading days, 22-26 weeks, 8 years.
- **GOLD:** 22-28 days, 20-28 weeks, 8 years; the old 16-20 weeks and 7-9 years could not be
  traced to a source, so this newer table wins.

The COIN, MSTR, MARA and RIOT daily values are marked as trading days because CF runs on
TradingView bars. That is an inference from the charts, not an explicit CF statement. SILVER, GDXJ
and SILJ remain estimated, using gold's new 20-28 weekly range and 8-year yearly value. The other
remaining estimated presets are NDQ, DJI, DXY and USOIL, for seven estimated presets in total.

The new ranges do not make the lows agree better with CF's marks on those stocks: on Coinbase,
MicroStrategy, Marathon and Riot the Python prototype of the detector found 9 of 21 of CF's daily
lows within four days with the old estimates and 8 of 21 with CF's ranges. Most of the 21 dates are
pixel-read guesses, so that cannot separate the two; the numbers are used because they are CF's.
CF's settings panel at its defaults shows the adjustment switch off and all three factors at 0.
Tests: the six read presets are pinned value by value; the calendar-to-trading-day conversion test
now runs on IBIT (the Bitcoin preset) since Coinbase is no longer calendar days; a Coinbase weekday
chart's window is 40 to 90. Not heard by ear.

### Cipher C rebuilt as cycle lows after Camel Finance's CF Cycle Trading Indicator; Loukas retired (2026-10-21)

Cody: "Cipher c needs to be designed as close to the cf indicator as possible from any and all
screen grabs you can get from it… loukas indicator retire it unless it does something our new
cipher c won't. I want one robust cycle detection indicator." The old Cipher C (an Ehlers
cyber-cycle oscillator in its own pane) is gone. The design, the evidence and the measured fit are
in `docs/CIPHER_C_DESIGN.md`.

- **What it draws, on the price pane:** a green triangle under each daily cycle low, a bigger blue
  one under each weekly low and a gold one under each yearly low; the cycle's length in days over
  each daily low; a square on the bar a low is confirmed, a grey dot when one is pending, a red cross
  when it is cancelled and an orange cross when a cycle fails; left, mid and right translated tops;
  CF's yellow low-to-low arcs in a strip along the bottom; a green tint while the daily cycle is in
  its window; the 10-bar SMA. Alt+Shift+D on a bar says, for example, "Daily cycle day 40, in its
  window, 4 days left", and on a confirming bar "Cycle low confirmed, mid confluence".
- **The sixteen assets CF is tuned for** (its own TradingView watchlist): SPX, DXY, NDQ, DJI, GOLD,
  SILVER, USOIL, GDX, GDXJ, SILJ, BTCUSD, ETHUSD, COIN, MSTR, MARA, RIOT. Each has a preset, chosen
  from the chart's symbol (SPY, ES, XAUUSD, BTC/USD and other aliases included). Any other symbol
  gets Bitcoin's ranges if it trades every day and the S&P's if not, and says it is not one CF tunes.
- **The detector** is a Schaff Trend Cycle with CF's own settings-screen thresholds (overbought 75,
  oversold 10), its lengths taken from the asset's cycle range, fitted on 25 SPX lows CF labelled:
  18 of 25 found within two bars. On 13 CF labels it never saw, 8 within four days, including both
  of Bitcoin's 2026 lows.
- **Tiers follow the timeframe:** a weekly chart has weekly and yearly lows and never says "daily
  cycle low" (what Cody heard from Loukas on a weekly chart).
- **Loukas Cycle Detection is retired:** still computed on saved charts, no longer offered. Every
  part has a Cipher C counterpart (its ICL is the weekly low; DC Overdue is DCL Day past the window
  end; its four-year phase is spoken in the Bitcoin preset words).
- **Old Cipher C signals are retired** in the signal catalogue. A strategy or alert on one of the ten
  (Top/Bottom Single/Double/Triple, Shallow Peak/Trough and so on) says it was removed on 2026-10-21
  and names the nearest new part. A saved chart with the old Cipher C restores as the new one, on the
  price pane, without the old ±75 and ±90 levels.
- **StrategyLab:** Pulse Reversal Long (built on the old Cipher C bottoms) moved to the catalogue's
  retired list; the ML export reads DCL Day and In DCL Window instead of the old bandpass.

Two defects were found by the new tests while building, and fixed before commit:

- A weekly stock chart's bar clock was inverted (5/7 instead of 7/5 days per bar), so the weekly
  tier on SPY counted its window in the wrong unit.
- A series moving from an oscillator pane to the price pane kept its old levels: the old Cipher C's
  ±75 and ±90 would have been drawn as prices. Now cleared on that move only (`WorkspaceInitializer`).

Tests (66 in `CipherCProviderTests`): the events on a bar never change when later bars arrive (four
cuts of 15 years of Bitstamp BTC; every causal component is checked to have fired at least once);
both CF Bitcoin labels; every low is its bar's low and confirmed later; cancel and fail rules; the
counts; a weekly low is a daily low; weekly crypto and weekly stock charts; 13 symbol-to-preset
cases; restore of a pre-rebuild save; the Alt+Shift+D words; the retired signals. Seen failing with
each part undone: the 7/5 clock, the level clearing, the weekly-to-daily merge, a retired signal
entry, a window bound declared causal, and a confirmation stamped on the low's bar instead.

Caveats:
- Nothing has been heard by ear. The sounds and speech are new and untried with Orca.
- The lows are drawn on their own bar and are known only once confirmed (a median of nine bars on
  SPX daily), like CF's. They are declared look-ahead, so strategies cannot use them; they can use
  the events.
- Twelve of the sixteen presets are estimates; only SPX, gold and Ethereum ranges were ever readable
  on CF's screens, and Bitcoin's are Camel's spoken ones.
- Not built: the future FCLR boxes right of the last bar, the D/W letter stacks, the "49D" suffix,
  confluence that upgrades over later bars, the macro table and the Bullish Meter. Our confluence
  scoring is our own (CF does not publish its three parts).
- CF's own SPX counts fall inside its stated window only about 22% of the time. This follows the
  method; it does not show the method works (`docs/CYCLE_SCHOOL_FINDINGS.md`).
- No weekly chart with CF on it and no yearly cycle low was ever seen, so the weekly and yearly
  tiers on a weekly chart are not compared to CF at all.
- The bar clock is measured from the last 500 bars. Under seven days of a crypto chart it reads as a
  stock's; nothing can be drawn on so little history, but the window bounds and preset are declared
  look-ahead because of it.
- The yearly tier needs about 2,000 daily bars; an hourly chart runs the daily tier only with 54+
  days of bars (1,296 hourly bars for Bitcoin).
- Pulse Reversal Long has not been re-tested on the new DCL Confirmed.
- The accessibility-lead review agent the repo's hook asks for is not registered here, so no
  accessibility review was run.

### The Trading dashboard lists every live account, including empty ones, and no sandbox keys (2026-10-21)

Cody, with paper trading off: "I only see gemini and kraken in the balances tab ... the trading
dashboard should show all balances for which I have live api keys active for, sandbox ones should
be omitted." Two things were wrong.

- **An account holding nothing had no row.** Balances listed only non-zero holdings, so a connected
  account with an empty or all-zero balance (likely Alpaca's) vanished. Each account without
  holdings now gets one row: "No funds" with its name, or "Not read" when its balances could not be
  fetched (the reason stays in the tab's note).
- **Sandbox keys were listed.** Every active key counted, whatever its environment. Now a key not
  marked Live is skipped when the venue has a sandbox (Gemini, Alpaca, Binance testnet...). On a
  venue with no sandbox (Kraken, Bitstamp, Coinbase, MEXC, Kraken Futures, IBKR, Schwab) a key
  labelled Paper still reaches the real account, so it stays listed; hiding it would hide real
  money. The order service gained `HasPracticeEnvironmentAsync` for this (the venue's own flag).

The "no accounts" note now reads "No live trading account is connected ... Sandbox keys are not
listed."

Tests, each seen failing with its part undone: an account holding nothing still gets a row; a
Paper key on a venue with a sandbox is never asked for balances or positions; a Paper key on a venue
without one is; `HasPracticeEnvironmentAsync` returns the venue's flag, not a constant.

Caveats: not run against Cody's live keys. If a live account is still missing, the likely cause is
its key being labelled Paper in API Keys on a venue that has a sandbox. The `accessibility-lead`
review agent is not registered, so the new rows had no separate accessibility review.

### Weekly background tabs on Bitstamp no longer get a 400 on every tab switch (2026-10-21)

Each time the background monitor started for the BTCUSDT 1w tab, an error tone and "Bitstamp
FetchOhlcvAsync failed for BTCUSDT: 400 BadRequest" followed. Bitstamp's bars stop at 3 days; the
chart asks for days and builds the weeks, but the background tabs' bar source (`MarketFeeds`)
sent the timeframe as-is, so Bitstamp was asked for 604,800-second bars and refused (its reply is
"Not a valid choice" for the step). `MarketFeeds` now fetches through the same
`HistoricalDataFetcher` the chart uses, in both hosts.

Tests, each seen failing with the fix taken out: a 1w background tab on a provider whose largest
bar is 3d asks for days and gets ten weekly bars from seventy days; the web host's container
builds `MarketFeeds` with the fetcher (it is optional on the constructor, so a container without
it would have built silently).

Caveat: the WebHost's own `LocalBackgroundMonitor` alert poll still fetches raw from the provider
and would do the same for a 1w alert on Bitstamp; not changed (TODO).

### Alpaca's US stock list no longer silences the screen reader (2026-10-21)

Cody: "when I switch to the DIA tab speech and I think keyboard freezes ... it just stops
talking." DIA was the first Alpaca stock tab after two Bitstamp tabs, so arriving on it rebuilt the
toolbar's Symbol dropdown with Alpaca's whole tradable US stock list: 14,402 symbols, counted from
the app's own symbol cache (Bitstamp's is 256, Alpaca's crypto list 73). One option each, every one
an accessibility node, all added at once; the screen reader stopped until it had read the flood of
changes. The offline harness had missed it because its My Data market holds two symbols.

A symbol list of up to 1,000 renders whole, as before. A longer one is searched: a **Find symbol**
box appears before the Symbol dropdown, and the dropdown holds the chart's own symbol plus at most
200 matches for what was typed (an exact match first, then symbols starting with the text, then
symbols containing it). The box's description says how many symbols or matches there are.

Tests, each seen failing with the matching part undone: with 14,402 symbols the dropdown holds at
most 201 options and still shows DIA as selected; typing "spy" gives DIA, SPY, SPYG, SPYV, XSPY in
that order and "4 matches"; a 256-symbol list renders whole with no Find box (seen failing with the
limit at 100). Label-in-name, toolbar and dialog accessibility sweeps pass (144).

Caveats: not yet heard with Orca. That the option flood is what silenced Orca is inferred from the
size and the timing (only the first stock tab, only on arrival); the harness has no screen reader
to show it. The `accessibility-lead` review agent the hook asks for is not registered, so this UI
change had no separate accessibility review.

### Alpaca's key travels on each request, so tabs loading together no longer get 401 (2026-10-21)

With the stream fixed, Cody's DIA, SPY and TLT tabs each reported "Alpaca data error: HTTP 401"
followed by an HTML "401 Authorization Required" page. Every Alpaca call removed the key headers
from the shared HTTP client and added them back, on the belief that the rate limiter ran calls one
at a time. It only caps them per minute. When the background monitors fetched several tabs at
once, a request could leave inside another call's remove-then-add window carrying no key. Every
Alpaca request now carries the key on the request itself; the shared client holds none.

Test, seen failing with the old shared headers put back (2 listed with de-DE, README 9192 → 9194):
four tabs fetched at once all carry the key, and no HTTP client the provider holds has it as a
default header. The 257 Alpaca, provider fetch, conformance and websocket tests passed.

Caveat: not yet run live.

### Alpaca's streams no longer send a "ping" Alpaca cannot read (2026-10-21)

Once the stream connected, Cody's DIA tab reported "Alpaca data stream refused (400 invalid
syntax)". The shared websocket class sends the text "ping" every 30 seconds to keep a connection
alive. Alpaca defines no such message and answers it with error 400. Both Alpaca streams (market
data and trading) now turn that text heartbeat off, as Tradier's already did; the connection's own
protocol-level keepalive still runs.

Test, seen failing with the fix removed (2 listed with de-DE, README 9190 → 9192): Alpaca's stream
sockets have no text heartbeat. The 208 Alpaca, provider fetch and websocket tests passed.

Caveat: not yet run live. The DIA tab's freeze and Orca crash are NOT explained by this (see TODO).

### Alpaca's live stream reaches IEX and subscribes only after the login is confirmed (2026-10-21)

The previous fix did not hold on Cody's IEX-only plan. His log showed the stream refused with 409
"insufficient subscription", then 401 "not authenticated", over and over. Two defects:

- **The stream stayed on SIP.** The chart's bar fetch hit the SIP refusal first and moved the
  feed to IEX. The stream had already been opened on `/v2/sip` a moment earlier, so its 409 found
  the feed already downgraded, was only reported, and the reconnect loop retried SIP. A 409 now
  also reconnects whenever the open stream's address is not the feed's current one.
- **The subscribe was sent on a timer.** It went out half a second after the auth whether or not
  the auth had been accepted, and Alpaca answered it with 401. The subscribe is now sent when
  Alpaca replies "authenticated", and only then.

Tests, each seen failing with its fix removed (4 listed, README 9186 → 9190): no subscribe is sent
for "connected" or an error frame, one is sent for "authenticated"; a 409 on a SIP stream after the
bar fetch already moved to IEX is not reported as a refusal (both also run under de-DE). The 206
Alpaca, provider fetch and websocket tests passed.

Caveat: not yet run live with Cody's key.

### Alpaca's live stream says why it was refused, and moves to IEX when SIP is not in the plan (2026-10-21)

With the stream address fixed, Cody's Camel cycles tabs connected and were then closed by Alpaca:
"closed the WebSocket connection without completing the close handshake", two or three times per
tab. Alpaca sends its reason as a `{"T":"error","code":...,"msg":...}` frame just before it
closes, and the handler dropped every frame that was not a bar. Error frames are now handled:

- **409 "insufficient subscription" on the SIP stream** moves every stock read to IEX once (the
  same downgrade, now one shared method, that the bar fetch already did), says so, and reconnects
  on `/v2/iex`.
- **Every other refusal is reported with its code and reason**, for example 406 "connection
  limit exceeded; another app or window is already streaming with this key".

The stock order book (the latest quote) also failed on every Alpaca tab, logged only as
"HttpRequestException". It now asks on the bars' feed (`?feed=sip|iex`), reads the refusal,
downgrades to IEX on a SIP refusal and retries; other failures carry their message.

Tests, each seen failing with its fix removed (6 listed, README 9180 → 9186): a 409 frame moves
the stream to IEX and says so; a 406 frame is reported with its reason and leaves the feed alone;
a quote refused on SIP is retried on IEX (all three also run under de-DE). The 192 Alpaca and
provider fetch tests passed.

Caveat: which refusal Cody's account actually gets is not known yet. A free (Basic) Alpaca plan
streams IEX only, which would be the 409, but the next log will say. The fix has not been run live.

### The Camel cycles workspace loads: five defects behind "chart render failed" and Alpaca's one candle (2026-10-21)

Cody loaded the new "Camel cycles" workspace and every tab said "chart render failed"; the Alpaca
stock tabs that did draw held one candle the arrow keys could not leave. The server log showed four
separate faults, and the saved session showed a fifth.

- **A sub-pane strip shorter than one axis label failed the whole frame.** `RenderYAxis` clamps
  each label between one text line below the strip's top and 3 px above its bottom. On a short
  Loukas cycles strip under a five-line status block that band inverts, and `Math.Clamp` throws
  rather than picking a side ("'770.375' cannot be greater than 764"). A strip with no room now
  draws no labels; the other clamps in the file were already guarded.
- **Alpaca sent no `start`, and an omitted start is the beginning of today.** Every initial load
  (no `Since`) returned at most today's bar. It now asks for an early start read newest-first
  (`sort=desc`), puts the bars back in time order and keeps the newest `limit`. A request with a
  `Since` still reads forward from it.
- **Alpaca's refusals were never read.** `GetStringAsync` threw on any 4xx before the body was
  parsed, so the log said only "400 (Bad Request)", and the SIP-to-IEX downgrade, which reads the
  refusal message, could not run against the real API (it sends a 403). The body is now read on
  every status; 408, 429 and 5xx still throw for the pipeline's retry, and other refusals are
  reported with Alpaca's message ("HTTP 400: ...").
- **The stock live stream URL did not exist.** `/v2/stocks` 404s; the path is the feed, `/v2/sip`
  or `/v2/iex`, and it now follows the REST feed, including after a downgrade.
- **Switching tabs kept the previous tab's market.** The toolbar sync kept the current market
  whenever the provider supported it, and Alpaca supports Crypto too. Switching from a Crypto tab
  to the DIA tab loaded Alpaca's crypto symbols, DIA was not among them, and the tab was saved back
  as Crypto AAVE/USD. A composite identity's own category (`"Stock|Spot"`) now wins; an identity
  without one keeps the old behaviour.

Tests, each seen failing with its defect put back and passing with the fix (17 listed, README
9163 → 9180): `SubPaneStripTests.A_strip_too_short_for_a_label_does_not_fail_the_frame` (renders at
every canvas height from 80 to 400 px), `TabKeepsItsMarketTests` (2), and in the Alpaca fetch tests
an initial load's `start`/`sort=desc` and newest-`limit` order, a dated request reading forward, a
403 SIP refusal still downgrading, a 400's message reaching the error stream, and the stream URL
per feed (each also runs under de-DE). A filtered run of 1,832 provider, renderer, orchestrator
and market tests passed.

Caveats: not yet run live with Cody's Alpaca key, and not pushed. The fix does not repair a
session file that already saved the DIA tab as AAVE/USD. Why GLD got a 400 is still unknown: the
next load will log Alpaca's reason.

### A live strategy first evaluates on the same bar count as its backtest: 201 by default (2026-10-20)

A backtest feeds its warmup bars without taking signals and first evaluates the bar after them, so
its first possible signal sees warmup + 1 bars of history. The live gate allowed evaluation at
exactly the warmup count, one bar earlier. Live now waits for warmup + 1 bars too: 201 for the
default 200 (Cody's decision; moving the backtest instead would have shifted every backtest
result by a bar). The warming-up announcement counts toward the same number, for example
"10 of 201 bars loaded", so it never says "200 of 200" while the strategy is still waiting.

`ConfigurableStrategyLiveWarmupTests` now pins 50 of 50 not evaluating and 51 evaluating, the
announcement's "10 of 51", and a parity test that runs the real backtester with warmup 5 and
the live gate with warmup 5 and asserts both first evaluate at 6 bars.
`ConfigurableStrategyFactoryWarmupTests` moved to 200/201 and 240/241. Six of those tests failed
before the change (the parity one with 6 expected, 5 actual) and all 11 passed after it.

Caveat: a live strategy now starts one bar later than before. A directly constructed strategy
with a warmup of 0 still has no gate.

### A live strategy waits for at least the default backtest's 200 bars (2026-10-20)

The factory previously used only the analyzer recommendation for live warmup: 60 bars for a
simple spec, or no gate when no analyzer was registered. A default backtest suppresses signals
for its first 200 bars, so a validated strategy could start trading live sooner than its default
backtest allowed. `BacktestConfig.DefaultWarmupBars` now supplies the unchanged 200-bar defaults
in both `BacktestConfig` and the Strategy dialog. The factory uses the larger of that floor and
the analyzer recommendation, including when an analyzer recommends more than 200; no analyzer
means a recommendation of zero and the 200-bar floor still applies. Backtest-mode strategies
still bypass the live gate, and the backtest's own warmup remains unchanged.

`ConfigurableStrategyFactoryWarmupTests` first failed the 60-bar-analyzer and no-analyzer cases
(2 failed, 2 passed). After the fix, all 4 passed. Restoring `?? 0` without the `Math.Max` made
those same 2 tests fail; restoring the fix, rebuilding and rerunning passed all 4. The filtered
regression run passed 102 tests across `ConfigurableStrategyFactoryWarmupTests`,
`ConfigurableStrategyLiveWarmupTests`, `BacktestWarmupAnalyzerTests`, `SpiderLinesRemovalTests`,
`StrategyBacktesterTests`, `StrategyModalTests`, `ModalFocusTargetContractTests`,
`DisclosureHeadingSweepTests` and `ModalCatalogTests`.

Caveats: on short histories (such as a new listing or a weekly chart), a live strategy now waits
longer before its first signal and uses the existing warming-up announcement; its wording is
unchanged. A custom `BacktestConfig.WarmupBars` is per-run and is not stored with the spec, so live
does not know about that override. The backtest warmup and its results are unchanged.

There is a one-bar boundary difference: the backtester adds bar `i` to history before `OnBar` and
drops signals while `i < warmupBars`, so its first possible signal sees `warmupBars + 1` bars.
Live allows evaluation at `history.Count == warmup` (`ConfigurableStrategyLiveWarmupTests.Live_WithSufficientHistory_Evaluates`
pins 50 of 50). For the default 200, backtest first allows a signal at 201 bars while live allows
one at 200; this is recorded for Cody, not changed here.

### Strategy scripts' RSI helper is Wilder's RSI, the one the chart shows (2026-10-20)

`BaseStrategy.Rsi` used a plain mean of the last period gains and losses, so a strategy script
calling `Rsi(history, 14)` could disagree with the Wilder RSI drawn on the chart. `BaseStrategy`'s
SMA and Bollinger helpers already matched the strategy indicator cache, including Bollinger's
population standard deviation. All three helpers now call the cache's shared computations, and
`BaseStrategy.Rsi` returns Wilder's RSI. The old `data[count - count + i]` expression is already gone
from executable cache code; the EMA starts at `data[0]` and then reads `data[i]`.

`BaseStrategyHelperParityTests` first failed its three RSI periods against the old Cutler formula,
while its SMA, Bollinger, short-history and vacuity checks passed (4 passed, 3 failed). After the
fix, all 7 passed. Putting the Cutler body back into `BaseStrategy.Rsi` made the same 3 parity cases
fail; after restoring the fix, rebuilding and rerunning the class passed all 7. The filtered
regression run passed 200 tests across `BaseStrategyHelperParityTests`,
`StrategyIndicatorCacheParityTests`, `StrategyIndicatorCacheBacktestTests` and the tests that mention
`RoslynScripting` or `BaseStrategy`.

Caveat: a script strategy that calls `Rsi()` gets different numbers from now on. Built-in and
catalogue strategies, StrategyLab results and the cache are unchanged.

### The backtest's "bars evaluated" no longer counts the final bar (2026-10-20)

`RunAsync` previously counted the final bar as evaluated even though the signal loop stops one bar
early and reserves the last bar for closing an open position. The count now excludes that bar, so
the spoken summary changes from `3 warmup bars, 7 bars evaluated` to `3 warmup bars, 6 bars
evaluated` for a 10-bar run with 3 warmup bars. The Strategy dialog's "Bars used" line still
accounts for the full window and now labels the final bar as close-out when at least one bar was
evaluated; runs with no evaluated bars keep the existing form.

`BacktestEvaluatedBarsTests` first failed with 7 instead of 6, then passed after the fix. Filtered
regressions passed: `BacktestEvaluatedBarsTests` (1), `StrategyModalTests` (10),
`StrategyBacktesterTests` (12), `BacktestEquityCurveTests` (4), and `BacktestDrawdownTests` (7).

### Stale backtester and evaluator comments rewritten (2026-10-20)

- `StrategyBacktester.cs`: replaced the removed SMA/RSI/Bollinger built-in claim with the current
  stop behavior: ConfigurableStrategy carries its resolved stop, while scripts and other strategy
  implementations may omit `StopLoss`.
- `ConditionEvaluator.cs`: documented the pre-warmed HTF price/indicator cache, closed-bar clip,
  false-with-degradation behavior for missing lines, and StrategyLab's remaining active-TF fallback
  when no MTF service is supplied.
- `ConfigurableStrategy.cs` (found by the same pass, fixed at review): the pre-warm comment said
  HTF leaves fall through to active-TF data until the cache populates; OnBar's pre-warm gate in
  fact skips live evaluation until then.
- `VolumeProfileLevelProvider.cs`: confirmed its existing caveat already documents the closed cache
  fix; no source change was needed.
- `ComputeSharpe` uses the per-bar curve with zero risk-free rate; the old permanently-false per-fill guard no longer applies after the same curve change.

### Backtests no longer write a CSV to the temp folder unless asked (2026-10-20)

Every trade-producing backtest previously exported its log and feature snapshots to the system
temp directory, including Strategy dialog and StrategyLab battery runs; second-resolution names
could silently overwrite. Exports are now off by default. `BacktestConfig.DiagnosticCsvDirectory`
opts in to an export in the requested directory, creates that directory as needed, and uses a
`-2`, `-3`, ... suffix when a name is already taken. StrategyLab's `run` command still opts in to
the system temp folder and reports its CSV per run; write failures go to Debug and stderr without
failing the backtest.

`BacktestDiagnosticCsvTests` covers no default export, requested-directory contents, and distinct
files for back-to-back runs. Before the writer change, the requested-directory and repeated-run
cases failed; the default-run case failed in its isolated run (the first class run collided with
an existing same-second filename). After the change it passed all 3 tests. The two sabotage probes
are making the writer unconditional and restoring overwrite-capable `new StreamWriter(path)`;
they target the default-run and repeated-run assertions. Required filtered regressions also passed:
`TestTempScanTests` (2), `BacktestDrawdownTests` (7), `BacktestEquityCurveTests` (4),
`StrategyBacktesterTests` (12), `BacktestCostAndPositionTests` (10), `BacktestMathFixTests` (5),
`LabRunnerTests` (5), and `StrategyIndicatorCacheBacktestTests` (3).

Caveat: files already in the temp folder are left alone; StrategyLab's `run` still writes one per
run, by design.

### Backtest equity curve and Sharpe are marked to market every bar (2026-10-20)

`StrategyBacktester` previously added equity-curve points at fills, so open-position swings were
missing from `EquityCurve` and `ComputeSharpe`. The curve now contains its starting point, one
marked-to-market account value at every bar close, and the final equity at the last bar. The
drawdown and curve use the same marked value; `ComputeSharpe` still uses observed sampling
frequency, now bars per year. `BacktestEquityCurveTests` checks one point per bar across a laddered
position, the $6,000 marked value for the 40% open-loss fixture, the last-bar final-equity point,
and a lower Sharpe for an open swing with the same realised gain. Removing the per-bar point made
`EquityCurve_HasOnePointPerBar_ForLadderedPosition`,
`EquityCurve_ShowsAnOpenLossAtTheBarClose`, and `Sharpe_ReflectsAnOpenPositionSwing` fail; removing
only the final point made `EquityCurve_HasOnePointPerBar_ForLadderedPosition` and
`EquityCurve_EndsAtFinalEquityOnTheLastBar` and `Sharpe_ReflectsAnOpenPositionSwing` fail.

Caveat: **Sharpe ratios will change (up or down) and are not comparable with older results; the
curve now has one point per bar, so a long backtest returns a longer `EquityCurve`.**

### The test project builds with no code warnings, and new ones are errors (2026-10-20)

The Debug `--no-incremental` baseline had 140 code warnings in `AccessibleTrader.Tests` plus two
`NU1902` lines. The warning counts were: xUnit1031 ×58, ASP0006 ×36, xUnit2013 ×13, CS8602 ×10,
xUnit2031 ×9, xUnit2029 ×3, CS8601 ×3, CS8619 ×2, CS8604 ×2, xUnit2030 ×1, xUnit2000 ×1,
xUnit1030 ×1, and CS8603 ×1. Before editing, the warning-list filtered run passed 775 tests with
0 failures and 0 skips; the same 41-class filter passed the same 775 tests afterward.

- **xUnit1031 (58):** converted the flagged test methods to `async Task` and awaited blocking task
  operations and bUnit dispatcher calls. `SettledEventDispatchTests.Fired_from_inside_the_dispatcher_it_runs_inline_rather_than_deadlocking`
  intentionally keeps its bounded `done.Wait(TimeSpan.FromSeconds(10))` deadlock check; its single
  line is surrounded by `#pragma warning disable/restore xUnit1031` with a reason comment.
- **ASP0006 (36):** replaced incrementing render-tree sequence variables with literal source-order
  sequence numbers in `FormControlNameSweepTests` and `SettledEventDispatchTests`. The event helper
  uses the same literal numbers each time, as it renders the same source shape for each event.
- **xUnit2013 (13):** replaced collection count checks for zero or one with `Assert.Empty` or
  `Assert.Single`, preserving each asserted count.
- **xUnit2031 (9), xUnit2029 (3), and xUnit2030 (1):** used the filtered `Assert.Single`,
  `Assert.DoesNotContain`, and `Assert.Contains` overloads respectively.
- **xUnit2000 (1):** put the expected policy constant first. **xUnit1030 (1):** the de-DE canary's
  `ConfigureAwait(false)` is deliberate (it resumes on a pool thread, so only the async flow can
  carry the culture there), so it is kept under a reasoned `#pragma` instead. Luna had removed it;
  Claude restored it at review, as removing it would have weakened the canary.
- **Nullable warnings (18):** used null-safe speech predicates and `Assert.NotNull` at the point
  tests consume optional messages; the OANDA rig documents that its URI is present because its path
  filter reads it; metadata fixtures use the model's display-name and cloud-name defaults; and
  nullable result annotations now match their event data. In `SpokenWordingFixTests`, empty heatmap
  bin lists represent the two bars with no snapshot, matching the non-null heatmap collection type.

`AccessibleTrader.Tests.csproj` now enables `TreatWarningsAsErrors` and excludes only package
advisories NU1901–NU1904 from errors. The guard was tested by restoring
`Assert.Equal(1, feed.Bars.Count)` in `KeyedFeedsTests.Refresh_with_empty_result_keeps_the_old_buffer`:
the Debug build failed with xUnit2013 as an error. Restoring the file from `.sabotage/` and building
again succeeded. Debug and Release `--no-incremental` builds both succeed with 0 code warnings and
only the two NU1902 lines.

**CI caught one miss after the push (4bf3a6d8):** `BunitAsyncSettleGuardTests` reads the test
sources, and accepted `await` only at the start of a line, so the newly awaited
`try { await cut.InvokeAsync(...); }` in `ObjectTreeSelectionFollowsFocusTests` read as a discarded
Task. Neither Luna's run nor Claude's review ran that source-scanning guard. The guard now matches
an `await` before the call anywhere on the line. Proved: making that line `try { cut.InvokeAsync(...); }`
again turned the guard red (it named line 137); restored, it passed. With warnings as errors, the
same sabotage also fails the build with CS4014, a second net for a discarded call in an async test.

Caveat: **NU1902 remains a warning on purpose.** The AngleSharp advisory is documented in
`AccessibleTrader.Tests.csproj`; the patched AngleSharp version breaks bUnit.

### Backtest max drawdown is marked to market every bar (2026-10-19)

`StrategyBacktester` previously sampled `MaxDrawdown` only when trades were realised, missing open-position swings and the final-bar close. It now samples marked-to-market account equity at every visited bar close, including accrued carry, and after the end-of-data close. `BacktestDrawdownTests` covers a recovering open loss, a loss on the final bar, an open gain given back, short-side marking, no position, and the spoken summary. Removing the per-bar mark makes the recovering-long, marked-peak-giveback, adverse-short and spoken-summary tests fail; removing only the end-of-data sample makes the final-bar-loss and marked-peak-giveback tests fail; dropping the accrued carry from the mark makes `Carry_owed_on_an_open_position_is_drawdown` fail (funding owed while flat, closed at a realised gain).

Written by GPT 6 Luna (OpenCode sub-agent) and reviewed by Claude, who checked every claimed test run against the session log, added the carry test and saw it go red.

Caveats: **every backtest's max drawdown will now read the same or higher, so older results (StrategyLab catalogues, saved comparisons) are not comparable with new ones**; it is sampled at bar closes, not intrabar; the equity curve and Sharpe are still realised-only.

### Three small chart fixes: Cipher B uses WaveTrendMath, undrawn lines get no axis label, Save as Defaults keeps two choices (2026-10-19)

`CipherBProvider` now uses `WaveTrendMath` for its main and anchor waves and MCB stochastic lines.
`CipherBWaveTrendParityTests` preserves every output-array fingerprint on the fixed BTC bar fixture
for defaults, `WTSignalPeriod=4`, and `AnchorMultiplier=2`. `CipherAProviderTests` (21 passed) and
the `FullyQualifiedName~CipherB` run (190 passed) also passed.

`ChartRenderer.ScaleLabelsFor` skips components with `IsDrawn=false` for axis labels while leaving
their reading and speech path intact. `An_undrawn_line_has_no_axis_label` failed before the renderer
change; `ChartStatusLineTests` (55 passed), `CodysBtcChartTests` (5 passed), and
`DeclaredKnobObservabilityTests` (2 passed) passed afterward.

`SaveAsDefaults` now carries `UserShowAxisLabel` and `UserBackgroundTint`, including null for choices
never made. `ComponentPreferenceChoicesTests` (4 passed), `PropertiesModalTests` (27 passed, including
`Save_as_defaults_copies_both_choices_and_keeps_unset_choices_null`), and
`IndicatorPrefsPerUserIsolationTests` (3 passed) cover fresh defaults, chart-choice precedence, old
JSON with no new keys, and per-user storage. The dialog markup and focus behavior are unchanged.

Caveats: the old-JSON test is a pin, not a regression test (it could only fail before the fix because
the two fields did not exist yet). The three fixes were written by GPT 6 Luna in the OpenCode sandbox
from a handoff and reviewed by Claude, who checked every test count it reported against its session
log, replaced its by-name (reflection) property access in the new tests with plain code, and saw the
cleaned-up tests go red again with each fix taken out.

### StrategyLab research corpus: trader transcripts and extracted rules (2026-10-18)

`AccessibleTrader.StrategyLab/Corpus/` (data and scripts, no app code): 315 manifest records, 297
obtained (234 videos, 69.6 hours, plus 63 public web pages; about 800,000 words). The full
transcripts are committed in `Corpus/transcripts/<source>/` (text with `[mm:ss]` markers plus the
VTT; never audio or video), with `manifest.json` (school, method, sha256, words, access per item),
`rules/<source>.md` per source (method in its own terms, the complete method in order, dated calls,
candidate rules the setup grader does not use) and `rules/SCHOOLS.md` (structure / market-cipher /
cycle, never blended). The scripts in `Corpus/scripts/` rebuild everything; a mirror is in the
gitignored `strategy-lab-data/corpus/`. Caveats: YouTube's caption endpoint answered HTTP 429 for
most of the run, so 135 items are Whisper large-v3-turbo transcripts and 98 are YouTube ASR (one
manual track); ASR mangles tickers and indicator names. 16 Camel Finance members-only videos were
never requested, one Market Cipher video is unavailable and one TradingView page 404'd; all are in
the manifest as not obtained. Bob Loukas has transcripts but no rules file (deferred), and 35
educator videos plus the 2026-10-16 Camel daily arrived after their rules files were written.
Dated calls are recorded but not yet scored against prices (TODO).
### Setup grader round 3, Market Cipher with Cipher SR location: run as pre-registered, No edge (2026-10-18)

`docs/SETUP_GRADER_ROUND3_MC_SR_FINDINGS.md`. A new lab command, `setup-grader-mc3`, was built and
run as `docs/SETUP_GRADER_ROUND3_MC_SR_PREREG.md` wrote it. The order is in the history: the harness
first, then the counts-only stage, then the full run.

- **Round 2 reproduced first, exactly:** 334 setups and 158 trigger trades in crypto 4h, 70 and 33 in
  crypto daily, 1,423 and 721 in equities. Round 3 builds its setups by calling round 2's own code.
- **Verdict: No edge in crypto 4h (the headline), crypto daily and equities.** No round-3 test passed
  the cross-round Holm over all 119 tests of rounds 1 to 3, and none passed the placebo-level gate.
  The pre-registration's required sentence applies: **across three pre-registered rounds, no Market
  Cipher B entry rule tested in this lab has beaten a matched random entry.**
- **Being at Cipher SR support did not help.**
  - In crypto 4h, setups at support did worse than setups away from it: −0.225R for the dot and
    −0.109R for the trigger.
  - Random bars at support did worse than random bars in the same environment, in all three classes
    (crypto 4h −0.095R over 5,197 bars).
  - Lines 3 to 6 ATR from the real support did better than the real support in crypto.
- **The dot or the trigger at support, against random bars also at support:**
  - The crypto 4h trigger was +0.253R on 53 trades, with 95% kept under noise and a held-out
    +0.143R. Its raw p was 0.091, so it does not pass even before correction. That is about the size
    of round 2's crypto 4h trigger result: the location did not sharpen it.
  - Everything else was within ±0.06R, apart from crypto daily's small samples.
- **The support at a setup is almost always fresh:** its median age is 2 to 5 bars, and no crypto
  trigger at support used a level older than its anchor wave.
- **Suggestive only (leads, not edges):**
  - older levels for the crypto 4h dot (+0.567R, 23 setups);
  - the first-bearish-divergence exit (raw p 0.004 in crypto 4h). In equities this is round 2's
    result, already seen.
- **Exits:** Cipher SR's own exit, the next resistance, lost to random exits in 5 of 6 populations.
- **Under one correction over three rounds, nothing from rounds 1 or 2 survives either:** round 1's
  equity results move from Holm 0.011 and 0.029 to 0.17 and 0.28.
- **New code:**
  - `SetupGraderMc3Core.cs`: Cipher SR read at the previous bar's close, the location rule, the level's
    age and pivot, the placebo levels, the XR exit, and the 20,000-draw tests.
  - `SetupGraderMc3Command.cs`: the command.
  - Rounds 1 and 2's files are untouched.
- **Tests:** 8 new `SetupGraderMc3Tests`, each seen red with its defect put back.
- **Recorded:** an edge record `market-cipher-sr-location` (Falsified, variantsTried 108) and ledger
  row 34 (renumbered at merge).
- **Caveats:**
  - **One reporting bug was fixed after the full run.** The held-out check compared the held-out
    sign with zero, not with the development sign, so two negative tests printed "agrees". The rerun
    changed only those labels, and no verdict.
  - The descriptive block printed 48 outcome rows the plan did not ask for (splits by grade, and the
    Cipher SR "tested" subset). They are counted as exploratory and not used.
  - Everything here is about our replicas of Cipher SR and Cipher B.

### The cycle school, stage 2: the extra data fetched, the 56 pre-registered tests run (2026-10-18)

Results in `docs/CYCLE_SCHOOL_FINDINGS.md`; three new `edges.json` records and ledger rows 31 to 33 (renumbered at merge; row 30 is the Market Cipher round 2).

- **Data first, before any outcome** (`macro-fetch --only labour,cftc`, new files only):
  - JOLTS quits rate, JOLTS openings and temporary help services jobs, with full ALFRED vintages;
  - the CFTC Traders in Financial Futures history for the E-mini S&P 500, the Nasdaq-100 mini and
    CME Bitcoin, plus disaggregated COMEX gold. Contracts are chosen by CFTC code, and each value is
    stamped at its release (`CftcReleaseCalendar`): the Friday, moved for holidays, the 2013, 2018-19
    and 2025 shutdowns and the 2023 ION incident, and never before the history was first posted
    (2010-12-03 for TFF).
  - A dated addendum to `docs/CYCLE_SCHOOL_PREREG.md` put the new series into slots the design
    already had (positioning Q3; macro M10 to M12, copying M9). It was committed before anything was
    computed.
- **What the old COT files held** (checked value by value): `sp500_cot`, `nasdaq_cot` and `gold_cot`
  are what they claim. `bitcoin_cot` mixes Cboe, CME and CME micro contracts, and `wti_cot` mixes
  ICE WTI with spread swaps, because the old downloader matched names. The S&P and Nasdaq files hold
  2006-2010 values that were not public until 2010-12-03. Nothing was changed in them; the study
  used the new files.
- **`StrategyLab cycle-school`** builds the pre-registration literally, with `--counts-only` for the
  pass that settles the fallbacks. Seven new causality tests (`CycleSchoolCausalityTests`), each
  proven red by a sabotage and restored byte-identical.
- **Results**:
  - Camel's checklist: **no edge** in crypto, equities or gold. It beats chance and random swing
    lows, but shuffled prices score the same: its items need the rally that confirms the low.
  - The capitulation score: **no edge from the added data**. Its alerts find lows well, but price
    alone does as well or better. Gold: not testable.
  - Camel's macro theses: **no support** for any. The nearest, downward payroll revisions before
    deeper drawdowns, missed Holm (0.051). The alarm gate: too few events.
  - Trading: no edge, because no detector passed.
- **Caveats.** Five runs: one counts-only, then four full runs. Two of them were bug fixes:
  - a test with fewer than 15 lows had been marked not testable;
  - C5 counted ten FOMC meetings that have not happened yet.

  Neither changed a verdict, and both sets of numbers are in the findings. The last run added C5's
  two pre-registered descriptive rows, which had been left out.

  The held-out era was read by each full run with the same code. Gold and crypto are small classes.
  C6 uses an investment-grade spread, not high yield.

### Setup grader round 3, Market Cipher with Cipher SR location: pre-registered, not run (2026-10-18)

`docs/SETUP_GRADER_ROUND3_MC_SR_PREREG.md`, stage 1 of 2, committed before any setup was counted, any
Cipher SR value computed or any outcome seen. Round 2's method is kept exactly. The one new variable is
location: is the Buy dot (or the trigger wave) at the current Cipher SR support? Casper and Geo say
they put Market Cipher away until price is at a key level, and Cipher SR is Market Cipher's own level
tool. Quotes with timestamps are from Casper, Geo, Crypto Face, the official tutorial and Gainium.

- **The location rule:** the bar's low within 0.5 ATR above the support that was live at the previous
  bar's close, and the close above it. A wick through the level still counts if the close reclaims it.
  On the bar a support is replaced, the old level is the one tested. The 0.5 ATR band is the one the
  lab's earlier Cipher SR test used; no source gives a number, and it is frozen.
- **Primary class:** crypto 4h with a daily environment, where our Cipher SR is closest to the real
  one. Crypto daily and equities daily are secondary. 1h was not added (only KAS and TAO have it, the
  assets of the unreplicated 1h result).
- **Headline tests:** setups at support against setups away from it; at-support dots and triggers
  against random bars that are also at support in the same environment; and at-support random bars
  against all random bars. Also an older-level split, a placebo-level gate (ledger row 20), and Cipher
  SR's resistance as an exit, beside round 2's divergence exit, which is labelled as already seen.
- **Across rounds:** this is the third Market Cipher round on the same data. Verdicts use one Holm over
  all 119 pre-registered tests of rounds 1 to 3 (39, 51 and 29). "Supported" also needs the held-out era
  (2023 on) to agree in sign, keep half the size, reach p 0.10 by itself and have 30 events. The
  pre-registration says that the primary class was chosen after round 2's results were known.
- Run the same day: see the entry above and `docs/SETUP_GRADER_ROUND3_MC_SR_FINDINGS.md`.

### Setup grader round 2, the Market Cipher school: run as pre-registered, No edge (2026-10-18)

`docs/SETUP_GRADER_ROUND2_MC_FINDINGS.md`. A new lab command, `setup-grader-mc2`, built and run as
`docs/SETUP_GRADER_ROUND2_MC_PREREG.md` wrote it. The order is in the history: the harness first,
then the counts-only stage, then the full run.

- **Verdict: No edge in crypto and equities** for all three questions:
  - the Buy dot bought at the next open;
  - waiting for the trigger wave with money flow turning up;
  - waiting against not waiting.

  The grades also show no edge. None of the six headline tests reached p < 0.05, even before Holm.
- **The largest numbers were the trigger-wave entries,** and both fall apart under noise:
  - crypto: +0.398R over the matched random bar on 33 trades (p 0.067), 32% kept under noise;
  - equities: +0.081R on 721 trades (p 0.063), 27% kept.
- **Waiting against buying the dot,** per setup seen: +0.274R in crypto, with the held-out sign
  flipped; −0.012R in equities.
- **Equities' dot entry is a near-replication of round 1:** +0.053R here (p 0.095) against round 1's
  +0.115R, under a different context and veto. It did not replicate.
- **Grades:** crypto grade A ("deep green" weekly money flow) had 12 development-era dots, so it
  was not testable. The equity grades did not order A > B > C.
- **The veto, tested both ways:** in crypto, vetoed dots did better (as in round 1); in equities,
  worse. Neither is significant, and both held-out signs disagree.
- **Exits:** in equities, the first bearish divergence beat random exits for both entries (Holm
  0.008), and the lab ladder did for the dot entry. That goes against the ledger's prior. It does not
  rescue the entries. Crypto showed nothing.
- **New code:**
  - `SetupGraderMc2Core.cs`: the anchor and trigger waves, the void rule, the weekly context, the
    school's stop, and a noise function that keeps each bar's opening gap. `GateCommand.Perturb` is
    untouched.
  - `SetupGraderMc2Command.cs`: the command.
  - Round 1's `Ladder`, `StopAndRule` and `LabelPerm` gained overloads and options. Round 1's
    behaviour is unchanged.
- **Tests:** 8 new `SetupGraderMc2Tests`, each seen red with its defect put back.
- **Recorded:** an edge record `market-cipher-dot-vs-trigger-wave` (Falsified, variantsTried 80) and
  ledger row 30.
- **Caveats:**
  - Development-era crypto is five assets.
  - One sabotage (a thickness window reading one week ahead) changed nothing on the synthetic series,
    so the truncation test could not show it. Other look-ahead sabotages turned it red.
  - Everything here is about our replica of Market Cipher B.

### The cycle school: Camel's method written up, our own cycle-low indicator designed, tests pre-registered (2026-10-18)

Stage 1 of 2. Documents only: no code, and **no forward return, outcome, hit rate or event count
was computed** for any rule. A later agent runs the tests.

- **`docs/CAMEL_METHOD.md`**: Camel Finance's method in his own words, with a timestamped link for
  every claim and a tag on each (testable now, needs data we lack, not testable). It covers how he
  calls a low in real time (window first, price gates said in advance, early confirmations, a
  sentiment check, then his indicator confirms late), the checklist and how its required count
  changed by date, the higher-high close as the veto, invalidation, timing windows, translation,
  and the macro framework: "tell that to the bond market", bonds as the truth against headline data,
  payroll revisions, the NBER lag, liquidity, the alarm gate, contrarian sentiment.
  - Found while writing it: the "90+ metrics" claim for his indicator is in **none** of the public
    sources the lab holds (67 transcripts, the vendor's pages, the sales page). It is treated as
    unverified. "Bracketing cross" is the lab's phrase, not his.
  - No Bob Loukas material exists in the corpus (the one item was a 404); Loukas appears only as
    Camel quotes him.
- **`docs/CYCLE_LOW_INDICATOR_DESIGN.md`**: a from-scratch indicator with the CF indicator's outputs
  (pending, confirmed, cancelled, updated, a grade) and no fitted weights. Seven metric families
  (price at several lengths, breadth, volatility, positioning, sentiment, on-chain, macro and
  liquidity), each metric an expanding-window percentile, equal-weighted within and then across
  families into a 0 to 100 capitulation score. Every metric names its file, its sign, its known-from
  rule and a staleness limit. It says plainly that most inputs measure "price fell a lot" and that
  two or three families are really independent.
- **`docs/CYCLE_SCHOOL_PREREG.md`**: 56 tests in six families with Holm inside each, all numbers
  frozen. (a) Camel's checklist against lows confirmed later by one fixed rule, against chance, random
  swing lows and shuffled-bar surrogates; (b) the capitulation score against chance and against its
  own price-only version; (c) six macro theses (strong jobs print with falling yields, hot CPI with
  falling yields, downward revisions, short yields leading the Fed, the alarm gate; the NBER lag as a
  descriptive table); (d) trading versions that cannot rescue a failed detector. Development era to
  2022, held-out 2023 on, and a plain "no edge" definition with the reframings it forbids. Cycle
  lengths are never scored, because the lab showed they come from the detector.
- **`docs/CAMEL_TRACK_RECORD.md`** (the August 2026 ledger) is now committed. It had been gitignored
  as personal notes; Cody asked for research material to be committed, so the ignore line is gone.

### Setup grader round 2, the Market Cipher school: pre-registered, not run (2026-10-18)

`docs/SETUP_GRADER_ROUND2_MC_PREREG.md`, stage 1 of 2, committed before any setup was counted or any
outcome computed. Round 1 mixed Tim's weekly trend with a Market Cipher dot and graded the dot before
any confirmation; this round uses Market Cipher B tools only and the sources' own words, with a quote
and timestamp for every rule (Crypto Face, the official channels, Jayson Casper, Dylan Waves, Trader
Geo, Trading Bureau, Gunslinger, Trade Pro, DaviddTech, Frankie Candles, Crypto Wolf, Gainium).

- **Setups:** every MCB Buy dot (the anchor wave's dot) on daily crypto, daily equities and, as a
  secondary class, 4h crypto; the environment is the weekly (or daily) Cipher B built from completed
  bars only.
- **Two entry policies on the same setups:** E0 at the next open after the dot; E1 at the trigger
  wave (the next higher, sub-zero wave cross within 30 bars, with money flow turning up, void if a
  wave goes as deep as the anchor). Dots that never confirm are no trade for E1, and their E0 outcome
  is reported separately. Secondary waits: one candle for the cross to hold, and money flow crossing
  into the green.
- **Headline tests:** E0 and E1 each against a random bar in the same higher-timeframe context, and
  waiting against not waiting per setup seen (no trade counts as zero).
- **Grades** from the higher timeframe's money flow only: A "deep green" (above zero and at least its
  two-year median size), B rising, C falling, V all three lines coming straight down (a two-sided
  test, because round 1's crypto vetoes went the wrong way).
- **Exits:** the lab's ladder against the school's own (opposite wave cross, money flow curving over
  in the green, first bearish divergence), each against random exits, R per position.
- Development era before 2023, held-out 2023 on for signs, 15 bps a side, noise retention, breadth,
  minimums, Holm per family, 49 tests, a mandatory counts-only run first, and the "no edge" sentences
  and forbidden reframings written in advance.
- **Caveat:** nothing was counted, so the sample sizes under these rules are unknown. The noise test
  needs a new perturbation that keeps each bar's open; the shared one sets open equal to close.

### Macro, Fed, liquidity and sentiment data for the lab, point in time (2026-10-18)

Groundwork for the tests of Camel Finance's macro and cycle method and of a from-scratch cycle-low
indicator. Until now the lab had FRED release dates and no macro values. `docs/MACRO_DATA.md` lists
every series, how each is dated, the traps, and what could not be fetched.

- **New lab command `macro-fetch`** (`AccessibleTrader.StrategyLab/Macro/`). It writes to
  `strategy-lab-data/macro/<source>/`: raw files, a `.meta.json` for each one (URL, request, time,
  sha256), and normalized CSVs. The manifest is `AccessibleTrader.StrategyLab/Data/MACRO_MANIFEST.json`.
  - It never overwrites. A raw file on disk is reused after its sha256 is checked, and a normalized
    file is rewritten only when the new bytes are identical.
  - Nothing outside `macro/` was written.
  - It needs no keys. FRED values come from ALFRED's graph CSV. Vintages come from ALFRED's own
    download form, and the FRED API was not used.
- **Fetched** (31 MB, 332 files, retrieved 2026-10-18):
  - **ALFRED full vintage history** for PAYEMS, UNRATE, ICSA, CPIAUCSL, CPILFESL, PCEPILFE, INDPRO,
    GDPC1, M2SL, UMCSENT, SAHMREALTIME and USREC. Each has its vintages, its first releases, and the
    latest value with the day that value was published.
  - **35 FRED series:**
    - the curve and real yields;
    - fed funds and the target, and the target range;
    - ICE and Moody's credit spreads;
    - the broad and major-currency dollar;
    - WALCL, reserves, the TGA and reverse repo;
    - VIX, NFCI, ANFCI and STLFSI4;
    - USRECD.
  - **NBER's 69 turning points**, each with the day it was announced. All 12 since 1980 matched.
  - **AAII weekly sentiment**, 1987 to 2026.
  - **Cboe put/call archives**, 1995 to 2019-10-04.
  - **CoinMetrics BTC from 2010**, which adds the 2011 and 2015 lows that the existing 2015-start files
    miss.
  - **Blockchain.com**: miners' revenue, difficulty and hash rate.
- **Point in time:**
  - Revised series are read as of a day from their vintages. `MacroPointInTime.AsOf` never serves a
    value before the day it was published.
  - Every other series carries a `known_from` date. For ten weekly and monthly series, that date comes
    from ALFRED's real release dates. For market closes, it comes from the publisher's schedule.
  - The first prints derived from the vintages were checked against ALFRED's own "initial release
    only" file for all twelve vintaged series. They agree row for row.
- **Tests:** `MacroPointInTimeTests` (23). They cover:
  - a vintage invisible before its release;
  - a revision taking over only from its own day;
  - a withdrawn value not being served;
  - the first print, not the revised number;
  - back-fills not counted as first prints;
  - slices of a long vintage list merging back exactly;
  - each schedule rule never earlier than the publisher;
  - a skipped holiday release;
  - the two NBER pages.

  Five deliberate breaks, made in four builds, each turned the relevant tests red. The file was restored
  byte-identical after each build.
- **Caveats and traps found** (details in MACRO_DATA.md):
  - **ICE high-yield and IG spreads exist only from 2023-10-17** (licence). Camel's credit gate needs
    the Moody's Baa substitute, which is investment grade, not high yield.
  - **The 2025 shutdown broke the release schedules.** September payrolls were printed 2025-11-20,
    October payrolls had no release of their own, and October 2025 CPI does not exist.
  - **USREC is rewritten after the fact.** FRED flagged every month after the Feb 2020 peak as
    recession until the trough was announced, then flipped 14 months back to 0.
  - **NFCI and STLFSI4 are revised every week.** ALFRED returned HTTP 504 on NFCI's initial-release
    file.
  - **Cboe has no CSV after 2019.**
  - **Google Trends and paid on-chain metrics were not fetched.**
  - **Not done:** no study has used the data yet, and the lab's cross-series cache does not load it.
    A study reads the CSVs directly.

### Cody's BTCUSDT 1D screenshot: the price axis carries one ribbon tag; empty plots leave the status line; what the blue bands and orange crosses are (2026-10-18)

Each change has tests seen red with its part undone (13 sabotages, all red). Checked on his own
chart: his newest saved session (`__last-session__782c…`, copied read-only; the four indicator
series committed as `Fixtures/codys_btc_1d_series_2026-10-18.json`) restored through
`MigrateSeriesConfig` + `MaterializeSaved` and drawn by the real renderer on the Bitstamp BTC daily
fixture, and in scratch on the same history extended to 2026-10-18 from Bitstamp's public API.
**Not seen on Windows, not seen or heard by Cody.**

- **Cipher A's ribbon puts only EMA 5's value on the price axis.** About 13 tags were stacked on the
  axis (eight ribbon lines, Cipher SR's levels, the close), and the stacking pushed tags off their
  lines: on his chart at 1400x900, Resistance 84,254 sat 48 px from its line, Support 79,694 64 px,
  EMA 5 78 px. Now Resistance and Support sit on their lines (0 px) and EMA 5 is 14 px off, beside
  the close badge. The lines are still drawn, spoken and sounded.
  - **New for every indicator** (plugin authors too): `IndicatorComponentMetadata.DefaultShowAxisLabel`
    (null = true), `ComponentConfig.ShowAxisLabel` (the indicator's, rebuilt from metadata on every
    load) and `ComponentConfig.UserShowAxisLabel` (the user's choice, null = never chose, restored
    with the workspace), read through `ShowAxisLabelInEffect()` by `ChartRenderer.ScaleLabelsFor`.
    The same split as the background tint, so a saved chart follows the new default unless its user
    chose (his saved Cipher A, which predates the field, gets the new default; a user's on/off
    survives a save and a second restore). `Clone()` copies both.
  - **Properties → Appearance → "Value label on the axis"**, a checkbox per line or bar plot (not on
    markers), labelled "Value label on the axis for EMA 11" etc. for a screen reader's form list.
  - `DeclaredKnobObservabilityTests`' pixel channel now includes the axis labels, so the two fields
    are observed rather than exempted.
- **A plot with no value anywhere on the chart leaves the status line.** Cipher SR's line read
  "Range High — Range Low — Range Mid — VWAP —" on every bar of a daily chart, where those four (per
  UTC session) are never computed. `ChartStatusLine.NoDataAnywhere` names the plots with no finite
  value on any bar; the status line leaves them out, and Alt+Shift+D's ladder (which already skipped
  NaN readings, so it never said them) no longer counts them when deciding whether to name each
  reading. A plot with values elsewhere still shows "—" at a bar without one (warm-up). The same
  series switched to a 15-minute chart shows all four again. A series not computed yet keeps its
  dashes; a series whose every plot is empty keeps its name and inputs.
  - **Not changed:** Ctrl+Up/Down still stops on those plots (Cody: stepping must not skip hidden
    parts, and these are the same kind of stop); landing there says "no data". The series' spoken
    component count is unchanged for the same reason.
- **Identified, nothing changed:** the darker blue bands between dotted vertical blue lines are
  **chart formation spans** (`ChartFormationLayer`, Settings → Appearance → "Draw chart formations",
  on in his settings): on his history the descending triangle (15 Jun – 6 Jul) and symmetrical
  triangle (1 Jul – 9 Aug, completed) and the ascending triangle still forming (15 Sep – 2 Oct). The
  orange crosses are **Market Structure's Break of Structure** marks (#FFB300 amber crosses), on
  21 Sep (at the broken swing high, 82,281) and 8 Oct (82,555), drawn at the broken level on the bar
  that broke it; removing Market Structure from the render removes them. Cipher A's Red X is red
  (#FF5252) and fired on 10 Sep and 8 Oct.
- Tests: `CodysBtcChartTests` (5: his chart's axis before and after, his restored Cipher A and a
  user's choice kept, the fleet's only opt-outs, his Cipher SR's status line on daily and after a
  switch to 15-minute), `BarDetailReadsEverythingTests.APlotWithNoValueAnywhere…`, two
  `PropertiesModalTests`, `WorkspaceRestoreContractTests` (UserShowAxisLabel user-owned). A first
  draft that dropped an all-empty series' line entirely was caught by three existing tests
  (`CipherSrZoneLineRenderTests`, `CrosshairBadgeAndLegendKeyTests`, `ChartStatusLineTests`) — the
  status band changes height — and is not what shipped.
- **Recommended, not done:** a limit on how far any tag may be stacked from its own value (TODO).

### The setup grader for the Cipher B Buy dot: run as pre-registered (2026-10-18)

Stage 2 of the pre-registered study in `docs/SETUP_GRADER_PREREG.md`. Results:
`docs/SETUP_GRADER_FINDINGS.md`; ALPHA_LEDGER row 29; `edges.json` record
`cipherb-setup-grader-hierarchy` (Falsified).

- **New lab command `setup-grader`** (`SetupGraderCommand.cs`, `SetupGraderCore.cs`). It builds its
  own weekly bars from completed weeks only, computes R per position from the initial risk, and
  writes the five exit variants and their random-exit controls itself, as the pre-registration
  required. `--counts-only` stops before any outcome. The harness was committed before the first
  outcome was computed.
- **Tests:** `SetupGraderTests` (5): completed weeks only (crypto and an equity holiday week), R from
  the initial risk after the stop moves to breakeven, a grade unchanged when later bars are removed,
  and every trigger's random control drawn from bars in the same context grade. Each was seen red with
  its defect put back. `GateCommand.Perturb` is now internal so the noise test could call it
  unchanged.
- **Finding: No edge in both classes.** Grade A almost never happens (0 crypto, 2 equities in the
  development era), below the testable minimum, so the headline tests could not run. Buy dots fire in
  good weekly context only 1 to 4% of the time, and money flow is almost never rising at the cross.
  Where B and C can be compared (equities), B is below C. Two pre-registered side results passed Holm
  in equities, both about the bare dot rather than the hierarchy: non-vetoed dots beat a random bar in
  the same context (+0.115R), and vetoed dots do worse than those (by 0.125R). In crypto the vetoed
  dots did better. No macro modifier and no exit beat its control.
- **Caveats:** the strict weekly body-pivot rule can never fire when weeks open at the prior close,
  so MEXC's KAS and TAO never get a trend (applied as written, not patched); Bitstamp pivots exist
  only because of tiny opening gaps. Noise injection was not run, because there was no grade A to
  measure. Survivor snapshots.

### Three lab defects verified and fixed: R after a stop move, higher-timeframe look-ahead, COT dated early (2026-10-18)

All three came from the setup-grader pre-registration's lab notes (`docs/SETUP_GRADER_PREREG.md`
section 2.2). Each was shown with a test that failed on the old code, then fixed, then the test was
seen red again by putting the bug back.

- **R after a breakeven or ATR-trail move (real).** Every exit row the backtester wrote carried the
  stop as it was at that moment. After take-profit 1 moved it to breakeven, later rows had zero risk
  and were dropped from Average R and from the bootstrap; a trailed runner was dropped from Average R
  and divided by the trail's distance in the bootstrap. R was also averaged per exit row, not per
  position. On a 2R/4R ladder, a position that reached 4R scored +2R, and one that took TP1 then came
  back to breakeven also scored +2R (it is +1R). Fixed: rows now carry the stop the position opened
  with (what `BacktestTrade.StopPrice` was always documented to be), and R is one number per
  position: the share-weighted sum of its exits divided by the initial risk. Average R, the bootstrap
  CI (`BootstrapCi.ExtractRs`) and the Lab tab's survivor gate now use the same list. Trade counts,
  P&L, win rate and profit factor do not change. `BacktestRAccountingTests` (5 tests).
  - Measured on every catalogue spec with breakeven or trail after TP1 and a two-rung ladder, BTC and
    ETH daily, whole sample, battery costs (mean R old -> new, same trade counts): v23 BTC +0.618 ->
    +0.956 (CI low -0.01 -> +0.16), v23 ETH +0.060 -> +0.233, v23r BTC +0.353 -> +0.695, v23r ETH
    -0.408 -> -0.210, Bare Bull Pulse BTC +0.536 -> +0.573, ETH -0.017 -> +0.041, Faber-Pulse BTC
    +0.347 -> +0.529, ETH +0.100 -> +0.100, v24 (trail) BTC +0.573 -> +0.932, ETH +0.263 -> +0.354 (CI
    low +0.07 -> -0.29). Longs mostly rise (the 4R rung was never counted); shorts and some pulse
    specs fall (TP1-then-breakeven was counted as a full 2R or 1.5R). The CI widens nearly everywhere.
  - Caveat: R uses fill prices (slippage in, commission out), as before. An entry that filled through
    its own stop has no defined R and is left out, as Average R always did; the bootstrap used to keep
    it with the absolute distance.
- **Higher-timeframe look-ahead (real; recorded "found, NOT fixed" on 2026-10-09).** A higher-
  timeframe bar counted as closed once it had OPENED before the chart bar, so on a Wednesday a weekly
  leaf read the week that opened on Monday, which in a backtest is that week's final close. Fixed in
  `ConditionEvaluator`: a bar counts once its period has ENDED by the chart bar's open (calendar months
  walked properly). That is the rule the line-cross code already described and the one the chart's
  higher-timeframe moving averages already use, so the chart path needed no change. This path drives
  strategies (live and in-app backtests), advanced alert trees, the background monitor and the
  screener. The lab is not affected: it passes no timeframe service, and no catalogue spec has an HTF
  leaf. Two tests pinned the old behaviour and were changed: `HtfLastClosedIndex_MainBarBetweenHtfBars`
  (now `..._MainBarInsideAnHtfBar_ExcludesTheBarStillForming`, expects 1 not 2) and
  `Daily_close_crosses_the_weekly_sma_read_as_a_step_as_of_each_bar` (its fixture only crossed because
  Tuesday read the forming week; moved a week later). New weekly and 31-day-month tests.
  - Measured on Bitstamp BTC daily against its weekly bars: a "weekly candle is green" gate gives a
    different answer on 44.9% of days and "weekly close above its 10-week SMA" on 13.9%. With the old
    rule the 5-day forward return when the green-week gate is on is +2.18%; with the fixed rule +0.82%.
    Example: Wednesday 3 Jan 2024 read the week of 1 Jan (open 42,258, final close 43,944, green); it
    now reads the week of 25 Dec (close 42,258, red).
  - Caveat: the rule is conservative by up to one chart bar (a weekly bar that ends at Sunday's close
    becomes visible on Monday's daily bar). Intraday HTF leaves (4h on a 1h chart) lag the same way.
- **COT dated early (real, lab only).** The app's CFTC plugin already stamps each value at its Friday
  release. The lab's `cftc-cot` downloader stamped the Tuesday report date, and the lab cache served it
  as is, so lab COT reads (CFTC_COT, and COT_POSITIONING on BTC, gold and the euro) saw each week's
  positioning three days early. Fixed: the cache moves a CFTC point stamped Monday or Tuesday to three
  days later as it loads it (the files on disk are not rewritten), and the downloader now writes
  release dates. Files already on release dates (sp500_cot, nasdaq_cot) are left alone.
  `CotReleaseDateTests` (4 tests).
  - Measured: the COT_POSITIONING z-score changes on 57.8% of gold days and 42.9% of BTC days; the
    "z below 1.5" gate flips on 189 gold days and 129 BTC days. v23c, the only catalogue spec with a COT
    gate, gives identical trades before and after on XAU, SPY, QQQ and BTC daily. SPY and QQQ are
    unchanged (their files were already release-dated).
  - Caveat: a Friday stamp at 00:00 UTC is before the 15:30 ET release; fine for daily bars judged at
    their close, up to about 20 hours early for intraday bars (same as the app's plugin). `events`
    already applied its own six-day lag and is unchanged.

### The Cipher strategy research re-run on the new Cipher A, B and SR (2026-10-17, night)

Every StrategyLab result that used a Cipher indicator was re-run after Cipher A was rebuilt, Cipher B
refitted to Market Cipher B (including the gold dot) and Cipher SR rebuilt. Full report:
`reports/cipher_strategies_rerun_REPORT.md`; dated "RE-RUN 2026-10-17" sections in
POLARITY_AND_GATE, CONFLUENCE_SENTIMENT, ASSET_PROFILE, ML_CONFIDENCE, STRATEGY_CATALOGUE,
ALPHA_LEDGER (rows 8, 16, 18, 28), BTC_STRATEGY, CROWDING and LAB_DESIGN; `edges.json` decay entries.

- **New lab switch `--cipherb old|new`** (and `--cipherb-gold old|new`) on every command: it sits in
  the indicator engine the lab host hands out, so gate, confluence, asset-profile, ml-export and every
  strategy run see it. Old output is identical line for line to the pre-refit build (aca73a1d).
  LabCipherProfileTests, seen red with the switch made a no-op and with it unwired.
- **New commands:** `cipher-a-battery` (the rebuilt Cipher A signal by signal on eight assets and six
  gates, block-permuted, family-wise max-statistic null, plus six pre-registered A x B combinations)
  and `spec-variants` (catalogue specs as written / dead leaves removed / replaced; `--all` sweeps the
  catalogue). `confluence --sr-lag N`: the rebuilt SR is causal, so the old 15-bar shift is optional.
- **Findings.** The dip filter (ledger row 8) gets larger with the new green dot (+0.145R excess over
  random, was +0.100R) and stays Fragile (29% kept at 25% noise). The rebuilt Cipher A has no signal
  that survives a family-wise null (0 of 441 cells; 0 of 70 combinations); three of its seven symbols
  are Cipher B's wave cross down. The causal SR adds nothing at 4h, 1d or 2d. The gold refit cuts gold
  to about a seventh as many fires, with no return-based reason to prefer either rule. The A + B + DBSI
  suite test was not run: neither member cleared the pre-registered bar.
- **Found on the way, older than the refit:** the April-July rolling-window and catalogue numbers
  reproduce only at their own build. Three August fixes moved them — a look-ahead in date-filtered
  backtests (ef4d3916, 2026-08-24), divergence timing (91648ad5) and take-profit rungs counted as
  trades (411022a2) — and the July confluence structure result moved with the causality contract
  (3601e37e). v23p's "best cell anywhere" (ETH +0.523R) is −0.416R now. The 2026-07-31 noise table
  used a per-process random seed and cannot be reproduced; it was re-run on fixed seeds.
- **Dead leaves:** bare-bull-pulse, faber-pulse and capitulation-buy OR in `CIPHER_A.Buy Signal`
  (dead since 2026-10-17) and `CIPHER_SR.Support` (refused since 2026-08-21). Proposal, not applied:
  delete both, add no replacement.
- Caveats: the July gate battery's code was never committed, so the Cipher A battery is a
  reconstruction from its description; EUR and COIN have no snapshot; the ML model was not retrained
  (lightgbm not installed); every result is on survivor snapshots.

### Dual Band Strength (DBSI-style): DBSI's numbers on the candles, reverse-fitted (2026-10-17, late night)

A new indicator inspired by Market Cipher DBSI, code `DBSI_STYLE`, in the Trend category on the price
pane, registered in both heads and StrategyLab. It is a **reconstruction, not DBSI**: a model
reverse-fitted to 143 DBSI candles digitised off six real charts (the 2026-10-17 feasibility study).
Each guard was proven red by sabotage (24 cases). **Not heard by Cody**; checked in rendered PNGs on
Linux only.

- **What it draws.** A bull score under each candle (green) and a bear score over it (red), whole
  numbers; on the newest candle the leading side's number is yellow. Only the newest 25 candles are
  labelled ("Label the newest N bars", 0 = every bar), as DBSI does. A 200-bar trend line (EMA), green
  while the close is above it and red below.
- **The model, copied to the digit from the study:** twelve fast direction votes (two-bar momentum,
  which counts twice because the constants were fitted that way; stochastic %K over %D at 16, 5 and 6;
  a 5/13 MACD rising; a 2-bar hlc3 average rising; WaveTrend rising and above its slow wave; close
  above the 2-bar midpoint; close and open above the previous), then bull = round(max(−5, −4.332 +
  1.668 × bull votes)) and bear = round(max(−5, −4.790 + 1.668 × bear votes)). Bulls therefore run −4
  to 16 and bears −5 to 15, where DBSI reaches the 20s. "Compute on Heikin-Ashi candles" is on by
  default (DBSI is used on Heikin-Ashi charts and the fit is better there).
- **How close it is.** On the study's own bars every vote and every printed number equals the study
  model's. In sample: r 0.92 bull / 0.93 bear, typical error about 3 points (RMSE 3.10), the side in
  control 94% of the time. The study's nested held-out figure — the honest one — is r 0.91 / 0.93, RMSE
  3.5, side in control 91%. **The exact integer matches only 14% of the time** (within ±2 points 59%).
  On the vendor's 15-minute image the leader agrees on 22 of 24 closed bars.
- **What you hear.** Landing on Bulls or Bears: "Bulls 15, bears minus 5, bulls in control".
  Alt+Shift+D adds both scores, the trend, who is in control and which side of the trend line the
  close is on. With N on the indicator, a bar close can say "Bulls take control at …", "Bears take
  control at …", "Bulls reached the long entry score at …" / "Bears reached the short entry score at
  …" (Long and Short entry score, default 7), and "Price crossed above Dual Band Strength (DBSI-style)
  at …" for the trend line. A tie hands nothing over.
- **Sound.** The two scores start muted. The trend line and the four event markers are **not** muted,
  because a muted component is not narrated; the markers are heard but never drawn (DBSI draws nothing
  for them).
- **New for every indicator** (plugin authors too): `ComponentDisplayType.ValueLabel` (appended as
  ordinal 25), a value printed as text at its bar, legible on every theme (the formation labels' ink
  rule, on a 245 backing), kept inside the plot, shrunk to the bar slot and left out altogether when
  even 7 px will not fit; a series parameter named `labelBars` limits it to the newest N bars. And
  `ComponentConfig.IsDrawn` / `DefaultIsDrawn`: a component heard but not painted, never stretching an
  axis or offered as a crossable line, rebuilt from metadata on every load. Formation labels now count
  the value labels' plates as ground to avoid.
- Tests: `DbsiStyleGoldenTests`, `DbsiStyleProviderTests`, `ValueLabelRenderingTests`.
- **Caveats.** Heikin-Ashi is on even on a regular-candle chart (the provider cannot see the chart
  type). Under Heikin-Ashi the trend line's colour uses the Heikin-Ashi close while the "price crossed"
  narration uses the raw close, so the two can disagree for a bar near the line. Labels disappear when
  bars are too narrow for the numbers (zoom in). Colour-vision-safe mode does not recolour the labels
  (they differ by position too). The drawn Heikin-Ashi candles are seeded at the left edge of the view
  and the indicator's from the first bar, so the first few on screen can differ (pre-existing).

### Cipher B's gold dot is shown again, with the one gold rule that can be read; the green dot is "Buy dot" (2026-10-17, late night)

Cody asked for the gold dot to be visible, fitted to Market Cipher B's gold-dot conditions, and for the
two dots to be called "Gold dot" and "Buy dot". Each change has a test seen red with its part undone.
**Not heard, and not seen on Windows.** (The code comments and the Properties text date this
2026-10-18.)

- **No evidence that real Market Cipher B has a gold dot at all.** About 100 TradingView idea images
  tagged Market Cipher B were colour-scanned (7 by eye) and none shows a gold dot in the lower pane;
  neither the vendor's material nor the guides we reached mention one; one third-party guide
  (tradelogic.pro, 2026-02-24) says outright that the golden circle exists only in VuManChu's free
  clone; every gold-dot description we traced came from VuManChu. So nothing could be digitised and
  fitted. What ships is **VuManChu Cipher B's gold circle, ported from its Pine code**, and the code,
  tests and Properties text say so. **Cody's decision is pending** — the agent recommends hiding it
  by default again (see TODO).
- **The rule (Gold Dot Rule = MCB, the default).** A gold dot fires when the slow wave makes a bottom
  at or below −65 (strictly lower than the two bars either side) that is a regular bullish divergence
  against the previous such bottom — price made a lower low, the wave a higher low — and that previous
  bottom was at or below −75 with RSI(14) under 30 there, and the slow wave is now above −75 and at
  least 5 above that bottom. Where VuManChu's header text and its code disagree (RSI 20 / −80 / a
  cross), the code was ported. It is stamped on the first bar the bottom is knowable, two bars after
  it, and never moves; VuManChu draws it two bars back. It runs on our fitted slow wave (hlc3, 9/12,
  SMA 3), reads raw-candle lows, and is drawn in the money-flow bar beside the buy dot (Confluence mode
  keeps it on the slow wave). An independent bar-by-bar Python reimplementation from the Pine source
  agreed with it on every bar of daily BTC, ETH, SOL and XRP. "Gold Dot: RSI Divergence Too" (off, as
  in that code) also accepts an RSI divergence.
- **It is rare.** Bitstamp daily, whole history:

  | Series | Old rule | New rule | With RSI divergence |
  |---|---|---|---|
  | BTC | 31 | 3 | 9 |
  | ETH | 15 | 3 | 5 |
  | SOL | 12 | 0 | 1 |
  | XRP | 21 | 0 | 1 |

  BTC's three confirm on 2014-03-26, 2014-10-02 and 2023-03-13; each comes 1–2 bars after a buy dot.
- **Old behaviour kept.** Gold Dot Rule = Confluence reproduces the old gold dot byte for byte (17
  hashed goldens captured before the change); StrategyLab takes `--set CIPHER_B.GoldDotMode=Confluence`
  on top of the 2026-10-17 `WTSignalPeriod=4 / MoneyFlowMode=Legacy / BuyDotMode=Gated` flags. The old
  gates (RSI Oversold, ADX, ATR, Gold Min Confluence, Anchor Suppression, Confirm Bars, Timeframe-Aware)
  now say "Confluence gold rule only".
- **Shown on saved charts too.** A chart saved before the hidden-gold build (gold shown) and one saved
  by it (gold hidden by default, `ProviderIsVisible = false`) both show it; a gold the user hid stays
  hidden. Not covered: a "Save as Defaults" made on Cipher B while gold was hidden still wins, by design.
- **Names and speech.** The machine names stay (`Oversold Crossover`, `Triple Confluence Buy`), so saved
  charts, alerts, strategy ids and StrategyLab keep working; what you hear and see is "Buy dot" and
  "Gold dot" — status line, Ctrl+Up/Down, Alt+Shift+D, the alert and strategy pickers ("Cipher B — Buy
  dot"). Narration: "Gold dot, waves turning up from oversold" (it claims no buy; the code it is ported
  from says not to buy on it) and "Buy dot, waves crossed up from oversold" (was "Oversold crossover,
  long signal"); both are checked true on every fired bar under both rules. Landing on gold says
  ", oversold" only when the fast wave is below −53 — it was unconditional, and false on two of the new
  dots (BTC 2023-03-13 at −38.0, ETH 2024-09-10 at −51.1).
- **Properties names components by their display names**, as every other surface does: the component
  pickers, the "Component: / Speech: / Acoustics:" legends and every "… for X" field label read the
  machine name before. This reaches every indicator, not only Cipher B (Cipher B's fast wave now reads
  "Wave Trend Fast" there). `PropertiesComponentNamesTests` (bUnit).
- **Strategies.** A leaf's parameters are numeric, so a strategy leaf cannot choose the gold rule: on
  the chart it reads the chart's Cipher B (set Gold Dot Rule there), on a higher timeframe and in the
  backtester it gets the default MCB rule. Catalogue and battery strategies that read the gold dot
  (the three bull-pulse strategies, `StrategyBatteryCommand`'s `*-gold` cells, MlExport's `sig_gold`)
  fire about 10× less often. Not pinned to Confluence — Cody's call.
- Tests: `CipherBGoldDotTests` (47: hand-built rule cases at every boundary, the RSI branch, causality,
  the Confluence golden, speech truth, every naming surface, visibility on Cody's save in both shapes)
  and `PropertiesComponentNamesTests`; 16 sabotages, each red.

### Cipher SR draws what Market Cipher SR draws; a replaced level is no longer "broken" (2026-10-17, late night)

Two commits. The first fixed what Cipher SR said; the second rebuilt what it draws from Market Cipher
SR's own frames. Each fix has a test seen red with its part undone, measured on a committed copy of
Cody's Bitstamp BTC daily chart (5,416 bars) with his saved Cipher SR settings. **Not heard.**

- **Replaced was announced as broken.** On the bar a new pivot took over, the zone line was left empty
  so the renderer would not join the two levels, and the narrator reads "had a value, now empty" as a
  break: 84 of the 136 "Resistance at X broken" announcements on that chart were levels price never
  closed through, and the new level was never named. The zone line now has a value on every bar a
  level is live, and a companion `{zone}_replaced` array holds the old level on the bar it is
  replaced. The narrator says **"New resistance at Y, replacing X."** and starts the new level's cross,
  approach and touch state afresh; the line renderer lifts the pen there, so the picture is unchanged.
  The narrator acts on `_replaced` only for lines that publish it.
- **Other speech fixes in the same pass:** a real break was named for the side price closed on the bar
  before (14 of 55 called a support "resistance"), now for the side the level was born on; 44 pivot
  dots spoke another level's test count and 62 said the wrong side of price; warm-up ignored Auto-Scale
  (40 bars, now 375); the detail sentence (which no key speaks yet) named an unconfirmed dot and called
  it "nearest" (now "Most recent resistance"). The pivot dots and Auto-Scale were then retired by the
  rebuild below.
- **Rebuilt from Market Cipher SR.** Sources: the vendor's product image; a TradingView chart whose
  legend reads "MarketCipher SR (2.2.0, 1, 15, 6)"; the vendor's tutorial video showing the settings
  dialog (H/L Pivot Days Back 1, Resolution 15 minutes, VolumeMA 6, VWAP; plots Pivot High, Pivot Low,
  Center Of Range, Cipher Resistance, Cipher Support, VWAP); and two YouTube frames of Coinbase BTC-USD
  (10-minute, fitted; 12-minute, held out) digitised against Coinbase candles for the same minutes.
  - **Resistance Zone / Support Zone** (names kept): one level per side, the exact high (low) of a bar
    the next bar did not beat — every observed level fitted a pivot with **one** bar on the right —
    taken only when the pivot bar's volume is above its 6-bar average, with 3 bars on the left. It is
    held through closes beyond it until a newer level replaces it, and drawn as a dotted run in MCSR's
    purple (#E040FB) and yellow (#FFEB3B). **MCSR's acceptance rule is not identified:** no feature or
    pair of features separated its 17 accepted levels from the 251 pivots it skipped. Our level equals
    MCSR's (within 0.06%) on **68.6%** of observed dot bars on the fitted frame and **62.5%** on the
    held-out one; the old design scored 12.6% and 0%.
  - **No break rule.** A level is never "broken"; price crossing it is announced as a cross ("Price
    crossed above Resistance Zone at …").
  - **New, muted by default:** Range High, Range Low and Range Mid (the UTC day's running high, low and
    midpoint — MCSR's legend values for 2022-01-29 reproduced exactly; MCSR repaints the whole day from
    its first bar on history, which needs look-ahead, so ours is the running value MCSR shows live) and
    VWAP (hlc3 volume-weighted, reset at 00:00 UTC; two legend values matched to within a cent). All
    four are empty on daily and longer charts, where a session is one bar.
  - **Arrowing onto a zone line says its age:** "97939.00, price below, held 152 days, tested 2 times",
    from a `{zone}_since` companion the speech formatter now hands to the provider.
  - **Retired:** the Resistance and Support pivot-bar dots — drawn before the pivot could be known, and
    MCSR has none; a saved strategy or alert naming them is told to use the zone lines. **Removed
    settings:** Auto-Scale, Pivot Bars, Volume Lookback, Volume Multiplier, Break Threshold, Adaptive
    Break, ATR Period and the ±0.3% bands on new charts. **New settings:** Pivot Left Bars (3),
    VolumeMA (6), H/L Pivot Days Back (1). Saved values for removed settings are carried but ignored.
  - `CipherSrLevelProvider` (risk plans and stops) reads the causal zone lines and drops its
    confirmation-lag guess.
- **Caveats.** MCSR computes its levels on 15-minute bars whatever the chart; ours uses the chart's own
  bars, so a 1-hour chart gets 1-hour pivots and a daily chart daily ones. On BTC daily that is about
  one new level per side every 5.5 days, and **Cipher SR's narration on that chart goes from 255
  utterances over its history (after the speech fixes) to about 1,915** — one every two or three days.
  Cody's saved ±0.3% bands still restore from his file and are drawn around the dots (new charts get
  none). Sessions are UTC days, not exchange sessions, for stocks too.
- Tests: `CipherSrSpeechTests`, `CipherSrSavedChartTests` (Cody's own saved series restores narrating
  through `MaterializeSaved`), `CipherSrMarketCipherFidelityTests` (floors 65% fitted, 55% held out),
  `CipherSrZoneLineRenderTests`, `CipherSrZoneRulesTests`; 9 speech sabotages all red; 18 rebuild
  sabotages, 17 red — the green one ("an equal left high counts as beaten") cannot be seen on
  continuous prices.
- **Reachability guard.** `IndicatorBoolParameterReachabilityTests`' floor on shared `GetBool` call
  sites drops from 8 to 5: Spider Lines left and Cipher A and SR lost their switches (6 call sites
  remain, 9 before). The floor guards against an empty population, not a per-provider count.

### Cipher A is back, rebuilt from measurements of real Market Cipher A (2026-10-17, late night)

Cipher A had been retired since 1.6.0 as Cipher B's WaveTrend drawn on the price chart. It is now
Market Cipher A as measured: seven TradingView charts showing Market Cipher A (three with
"MarketCipher A (2.1.2, 1, 21)" in their legend) digitised column by column against the same bars,
and checked on three charts never used for fitting (BTCUSDT 1D Binance, a 2023 Bitget 3-hour chart,
SLPUSDT 1D). It is offered again in Add Indicator (Multi-Signal, price pane); the code stays `CIPHER_A`.
**Not heard; seen only in rendered PNGs on the default dark theme.**

- **The ribbon:** eight EMAs of the close, 5 11 15 18 21 25 29 33 (OpenCipher A's set, not VuManChu's
  …24 28 34). Each line is blue while it is above the next-longer line and grey otherwise; the last,
  EMA 33, is blue while it is below EMA 29. That rule fits all 1,410 line colours read off three charts
  (303 + 654 + 453); "every line against EMA 33" fits only on the daily chart, which is why the 2-day
  chart's colours are pinned too. On the 2-day chart (23 px a candle) the lines fit within 0.05–0.10%
  of price (EMA 5: 0.4%). MCA paints no background tint. The eight lines start **muted**.
- **The seven symbols**, each a marker with its own bell, first in the keyboard order, then the eight
  lines (15 stops):

  | Symbol | Rule |
  |---|---|
  | Green Dot | EMA 11 crosses above EMA 33 |
  | Blue Triangle | EMA 5 crosses above EMA 25 while EMA 29 is below EMA 33 |
  | Yellow Diamond | Wave Trend above its slow wave, EMA 5 above EMA 11 and EMA 33 |
  | Red X | EMA 5 crosses below EMA 11 |
  | Red Diamond | Wave Trend crosses down (Cipher B's fitted wave) |
  | Blood Diamond | a Red Diamond and a Red X on one candle |
  | Yellow X | a Red Diamond with the slow wave at −40 or lower and price in the bottom 22% of its 80-bar range |

- **Fidelity.** Fitted charts, on the real symbol's candle: Green Dot 13/13, Red X 36/36, Blood
  Diamond 5/5, Yellow X 22/22; within one candle Blue Triangle 15/16, Red Diamond 117/124, Yellow
  Diamond recall 128/128. Held out, within one candle: Green Dot 5/5, Blue Triangle 10/10, Red X 17/17,
  Blood Diamond 6/6, Red Diamond 37/40 with no false ones, Yellow Diamond 35/37 on the 3-hour chart.
- **Speech.** Landing on a symbol says what made it ("EMA 11 crossed above EMA 33. Price 23,964.");
  on a line, its value and colour ("Blue, bullish: above EMA 11."). Its detail sentence ("Ribbon blue,
  all eight lines bullish." / "Ribbon mixed, 5 of 8 lines blue.") is built and tested but, like every
  indicator's `GetDetailFact`, **no key speaks it** — Alt+Shift+D reads the values (the agent's report
  said otherwise; the code does not call it). Narration names the symbols ("Green dot,
  bullish", "Blood diamond, strongly bearish", "Yellow X, bearish warning"); the eight lines are
  price-space lines, so with Cipher A narrating they also get "Price crossed above EMA 5 at …".
- **What went.** The old components — Buy/Sell Signal, the three divergences, Manipulation, Exhaustion,
  WT Momentum and its gradient — were all Cipher B's WaveTrend, and Market Cipher A draws none of them.
  A saved chart rebuilds as the new Cipher A (tested on a chart saved with the old nine components);
  their saved parameters are ignored. The nine are listed in `SignalCatalog.Retired`, so a strategy or
  alert naming one says once that it can never be true, and what to use instead. The WaveTrend now
  lives in a shared `WaveTrendMath` (a line-for-line copy of Cipher B's, pinned equal: Red Diamond plus
  Blood Diamond is exactly Cipher B's cross-down on 893 real bars).
- **Caveats.** The Yellow Diamond rule is the best fit among those tried; its precision can be checked
  only on two charts, and no published replica has it. The Yellow X's 22% was 20% until the SLP
  hold-out showed an X at 21.1, so SLP is not a clean hold-out for that number, and the margin is narrow
  (21.1 against 22.6). EMA 29 and EMA 33 are pinned by the 2-day chart alone. The "high volume" in Yellow
  X descriptions is not in the data (median 0.91× its 20-bar volume average). MCA's translucent fill
  between EMA 5 and EMA 33 is not drawn. On a Heikin-Ashi chart our indicators read the raw candles while
  TradingView feeds MCA the Heikin-Ashi ones, so a symbol can land a candle away. The pale end of the blue
  ladder is near invisible on light themes (MCA's own is too; not measured). Not looked at in the desktop
  head.
- **Strategies.** Three built-in StrategyLab catalogue strategies (Faber-Pulse Long, Capitulation Buy,
  Bare Bull Pulse Long) OR in `CIPHER_A.Buy Signal`, and live each would now say once that the condition
  can never be true; `StrategyBatteryCommand` cells on the old Buy/Sell Signal, Exhaustion and
  Manipulation score as never firing. Not edited — Cody's call (TODO).
- Tests: `CipherAProviderTests` against digitised real MCA symbols and colours and a Python reference
  model on 893 Bitstamp candles (`CipherAGoldenData`), including no change on the newest bar at any of
  700 cut points (the fleet causality guard's few cut points missed a planted Green Dot look-ahead);
  `CipherAMetadataTests` rewritten. 14 sabotages, each red.

### Spider Lines is removed; saved charts, strategies and alerts that name it say so once (2026-10-17, late night)

Cody: "I'd just remove the indicator for now." Spider Lines (`SPIDER_LINES`) was eight Fibonacci-length
EMAs (8 to 200) and a Stacking Score, every EMA flagged as a zone line, under a name that belongs to
Market Cipher's diagonal support and resistance lines on the BTC log chart. It had nothing to do with
them.

- **Gone from all three hosts** (desktop, web, StrategyLab); `PaneAssignmentService` and the
  moving-average cross test no longer name it.
- **Before, an unknown code was not dropped.** Restore rebuilt it as a series with no provider behind
  it — listed, drawing nothing, saying nothing — and a strategy leaf or alert on it was false forever
  without a word. Now a new `RetiredIndicators` list (code, name, date, why, what instead) is read by:
  workspace restore (multi-tab and legacy), which leaves it off and appends one sentence to the resume
  and Load Workspace announcements ("Spider Lines was removed on 2026-10-17, so it was left off this
  chart: … To get the same lines back, add an EMA for each length you want."); `SignalCatalog`, so a
  strategy leaf or tree alert says why ("…use an EMA with length 21 instead"); `ConfigurableStrategy`,
  which also reports a stop or target placed on it; `AlertEvaluator`, once per alert, with the remedy
  "Edit the alert or delete it."; and the background monitor, which does not build it and says once
  that it is not narrated.
- Tests: `SpiderLinesRemovalTests` (10, from files on disk through the real restore, library and
  orchestrator paths; a guard that no provider declares a retired code); 9 sabotages, each red.
  `SpiderLinesFastModeCanBeTurnedOn` and three causality exemptions removed.
- **Not verified:** the Load Workspace dialog's sentence is tested only through the notice it reads
  (no rendered dialog); the background monitor's announcement is not run end to end. The desktop head
  was not compiled (its change is one deleted line).

### Cipher B shows what stock Market Cipher B shows, on saved charts too; its dots say the wave trend again (2026-10-17, late night)

Cody: "finish disabling the elements on cipher b to make it similar to stock market cipher's config. Go
through cipher B's code … make sure it's cleanly written and commented where needed." Each change has a
test seen red with its part undone. **Not heard, and not seen on Windows.**

- **Hidden by default:** the red Overbought Crossover, the four divergences (with their slanted pivot
  lines) and the Anchor Fill cloud — ours, not Market Cipher B's: none of them appears on the five dated
  MCB charts, while our rules fire them on those same bars. The gold dot was hidden too, and shown again
  later the same night (entry above). They are still computed, still keyboard stops in the same order
  (18), and still strategy and alert inputs; hidden means silent — no bell, no narration, not in
  Alt+Shift+D or the status line. Bring one back with H on it or in Properties; Ctrl+Alt+Shift+K shows
  every hidden component (the anchor waves and Anchor Polarity too); Anchor Fill is under Properties →
  Appearance → Cloud Fills.
- **A saved chart follows a changed default visibility unless you changed it.** Visibility was restored
  verbatim, so a new default reached only new charts — the same shape as the levels bug that day.
  `ComponentConfig.ProviderIsVisible` and `CloudFillConfig.ProviderIsVisible` now record the default each
  flag was saved against: equal on restore → it takes the indicator's current default; different →
  yours, kept. Saves older than the record know Cipher B's retired defaults
  (`ProviderDefaultVisibility`: the six signals, Anchor Fill and WT Fill were shown). It runs in
  `IndicatorModelFactory` (so the background monitor gets it) and, for fills, in
  `WorkspaceInitializer.MigrateSeriesConfig`; "Save as Defaults" still wins. Tested on Cody's own saved
  Cipher B, copied read-only into `Fixtures/CodysCipherBSave.cs`, through the real restore path. What it
  cannot tell apart: a component hidden and shown again follows the indicator from then on.
- **Cipher B's dots say the wave trend again.** Landing on a cross, buy, sell, divergence or gold dot said
  only "Price …": the provider looked its data up by component name and the formatter keys it by
  display name ("Wave Trend Fast"). Gold also says "Money flow positive" again, and no longer ends "Trend
  and volatility confirmed" — its old rule needs only 3 of 4 conditions, and ATR was sometimes the one
  that failed. `CipherBProviderSpeechTests` drives the real formatter.
- **Money Flow says "buying pressure, 18.0"** (or "selling pressure, …"), not "18% buying pressure": the
  value is not a percentage (Cody). `CipherBMoneyFlowSpeechTests`.
- **Code review of `CipherBProvider`, output byte-identical** (a golden of every array, every bar's
  speech and detail, 79 series × parameter cases): stale comments corrected; dead code out (an unused
  array, a money-flow pass computed and discarded, a gold cooldown that was always zero); the WaveTrend
  core and the conviction test written once; magic numbers named; seven wrong parameter descriptions
  corrected (ADX/ATR gates, Confirm Bars, Anchor Suppression and its depth, OB/OS Threshold,
  Timeframe-Aware Gates).
- **Left open:** the rest of a saved cloud fill (colours, sound) is still restored verbatim; show-all
  also unhides the always-hidden anchors; hidden components are still keyboard stops (9 of 18 now) and
  their alerts still fire.

### Formation labels readable on every theme and placed off the candles (2026-10-17, late night)

The labels and lines a chart formation draws (Settings → Appearance → Draw chart formations) were all
in the theme's accent colour at fixed opacities, which no check ever held against the chart. On the
default theme the label text was 3.65:1 on its backing (2.46:1 where the backing sat over a doji), the
dashed measured target 1.63:1 and the span edges 1.34:1. WCAG asks 4.5:1 for text and 3:1 for a line
you need to see; every built-in theme missed at least one.

- **Per-theme ink (`FormationInk`).** Each colour starts as the theme's accent and is mixed toward
  white (or black on a light chart) in 1/32 steps only until text reaches 4.5:1 on its backing over
  everything it can cover (both chart ends, candle bodies and wicks, a doji, the accent line) and every
  line reaches 3:1 on both chart ends. Nine of twelve themes keep their text colour; Classic's goes
  from #2962FF to #517FFF. User-built themes get the same guarantee with nothing new in the theme editor.
- **The backing is 245/255**, as the status line's (was 205, so a candle showed through at 20%).
- **The measured target is still the lightest line** — the trigger's ink at the least opacity that
  reaches 3:1, never heavier than the trigger, thinner and dashed — so the visual weight is still the
  disclaimer, but it can now be seen.
- **One-pixel lines sit on the pixel grid.** Unsnapped, a 1 px line on a whole-pixel row draws as two
  half-strength rows: the corrected colour measured 1.63:1 on screen, exactly the old figure.
- **Span edges are dotted** (2 on, 4 off), so at 3:1 they do not compete with the candles. A judgement
  call; the alternative is to call them decorative and leave them faint.
- **Placement.** A label sat just above its line at the line's start, often right on the breakout
  candles. It now tries 18 spots — above and below the line at nine points along it — and takes the one
  covering the least candle body and wick, with small costs for moving away from the line or covering it.
  A label with nothing under it stays exactly where it was.
- Tests: `FormationLabelLegibilityTests` (41: the palette per theme, a pixel read-back per theme, the
  1 px snap, two placement cases, the cost function), each proven red.
- **Caveats:** measured ratios, not a viewing — nobody with low vision has looked yet. Where candles
  crowd both sides of a short line the label still touches some (the backing keeps it legible). The
  worst case under the backing counts the theme's candle colours, not indicator lines, background tints,
  volume-profile bins or user-styled candles. Skia on Linux only; not checked on Windows.

### Labels inside the chart, status lines never over the data, tint as a property, strips and dividers that agree (2026-10-17, night)

From Cody's look at the deployed BTCUSDT 1d chart: "fix the 2 label problems and make the background
tint like the real cipher b... address the status line as well", then "shouldn't this, if tinting is
all it does, be a property in the indicator properties?", then "can you fix the items you just found
too?". Each change has tests seen red with its part undone; checked in headless Chromium screenshots
of the seeded chart, **not yet on Windows or by a sighted reviewer**.

- **Formation labels.** "confirms on **an** upward breakout" (it read "a upward"). A label near the
  right edge slides left to stay inside the pane instead of running under the price axis, and one
  longer than the pane is shortened with "…" rather than cut mid-word (`ChartFormationLayer.FitInPane`).
- **Each pane's plot starts below its status lines.** The TradingView-style status lines are DOM text
  over the canvas; Cipher B's 0–100 RSI and Stochastic and the waves' peaks ran behind them. Every pane
  now reserves a strip at its top exactly as tall as the block shown there (one computation,
  `ChartStatusLine.BandMetrics`/`BandHeight`, published as `IPaneLayoutService.StatusBands`, honouring
  Text size), and every value↔pixel mapper — renderer layers, axis labels, badges, crosshair, hover,
  hit-testing, drawing anchors, the AI snapshot — uses the plot below it. A pane keeps at least 40% of
  its height and 20 px of plot; below that it shows fewer status lines ("+N more") or none. Sound is
  unchanged (it works on values). Panes are about 25 px shorter per status line at 100% text.
- **Background tint is a property any component can have** — `BackgroundTint` Off / This pane / Price
  chart in Properties → Appearance (labelled select with a hint in words), declared by providers
  (`DefaultBackgroundTint`, `DefaultTintColorAboveHex`, `DefaultTintColorBelowHex`, `DefaultTintMidline`,
  `DefaultTintFullAt`), the user's choice kept in `UserBackgroundTint`. One painter
  (`Rendering/BackgroundTintPainter`) replaces the two tints the renderer used to find BY NAME. Cipher B's
  Anchor Polarity defaults to **Off** — real Market Cipher B paints nothing on the price chart — and can
  be set to tint the price chart or its pane while the line stays hidden; Top/Bottom Detector's red
  topping band is unchanged, byte for byte. Replaces the evening's stopgap (tint only while Anchor Polarity
  was shown). Open: tint colours are not editable in the dialog; Save as Defaults excludes the tint; two
  tints now stack; Anchor Polarity at exactly 0 takes the above colour.
- **Sub-pane strips agree.** Loukas Cycles' FY strip was drawn on its own range while its axis labels,
  crosshair badge, hover value and hit-testing used the pane's range stretched over both areas (labels
  ~25 px off, hover read 45.68 for 35, clicks missed). One shared layout (`ChartRenderer.SubPaneLayout`,
  published as `IPaneLayoutService.SubPaneStrips`) now feeds them all; strips get their own scale; strip
  ratios over 70% share the 70%; price-pane strips get their real range (were −1…1).
- **Dragging a pane divider follows the mouse.** The drag assumed a 600 px chart, so a divider moved about
  half as far as the mouse in a small window and further in a tall one. It now uses the chart's measured
  height and starts from the pane's height as drawn (proven in real Chromium at 1000x610 and 1400x950).
  Still as before: a drag registers only while the pointer is over the chart.
- **A click-to-focus test that could not fail** now starts with focus on another series.

### Restored charts' levels follow the indicator, except where you changed them (2026-10-17, late night)

Cody, after the 80/20 push: "ctrl left/right only jumps between midline crosses and it does so even if
0 line is hidden … the texturing has disappeared and ctrl left/right does not jump to os/ob zones now".
Every test passed; his chart did not, because his chart was RESTORED. His saved session (read from the
file) held Cipher B's five levels as saved that afternoon — ±53 at 0.10, ±60 at 0.20, Zero hidden — and
no "Stoch Overbought"/"Stoch Oversold". Saved levels were restored verbatim (they are per-instance and
Properties edits every field), so no later change to an indicator's levels ever reached a chart that
already existed: the RSI and Stochastic subscribed to levels his chart lacked (no texture, no 80/20
jumps; Ctrl+Left/Right fell back to their 50 midline), and the louder ±53/±60 texture never arrived.

- **`LevelConfig.ProviderDefaults`** records what the indicator declared for each level. On restore
  (`SeriesManagementService.HealSavedLevels`, inside `MaterializeSaved`, so the background monitor too):
  levels the indicator declares that the chart lacks are added; whether a level is drawn always comes
  from the indicator; every other field still equal to that baseline takes the indicator's current value,
  and a field you changed is kept. A level saved before the baseline existed knows only the defaults known
  to be retired (Cipher B's 0.10 / 0.20) — anything else on it is treated as yours.
- **The Ctrl+Left/Right midline fallback honours a hidden midline** — the rule every level-based jump has
  followed since 2026-09-12. (His RSI reached it only because its levels were missing.)
- Tests: `SavedLevelsHealTests` restores a save shaped like his through the real path — the 80/20 appear
  undrawn, ±53/±60 at 0.30/0.45, his hidden Zero stays hidden, the RSI is textured past 80, Ctrl+Right lands
  on its 80 crossing and not the 50 it crosses first; edits survive; the fallback ignores a hidden midline.
  Each seen red with its part undone. Not heard yet.

**Lesson for this project:** a change to an indicator's levels must be tested on a RESTORED chart, not
only a fresh one.

### Levels can be heard but not drawn; Cipher B's RSI and Stochastic are spoken and narrated at 80/20 (2026-10-17, late)

Cody: "yes, they should be narrated", and asked whether an SDK author can give components different
bounds. They need not: components in one pane share its axis (and are pitched where drawn); what
differs is their MEANING — a midline (`DefaultReferenceLevel`) and extremes (levels a component
subscribes to). The one missing piece was a level that is not drawn.

- **`LevelDescriptor.IsDrawn` / `LevelConfig.IsDrawn`** (default true). An undrawn level textures,
  clicks, is spoken as a zone, narrated when crossed and alertable for the components subscribed to
  it, exactly as a drawn one — it is not painted and does not stretch the axis
  (`DataLayer`, `ViewportRangeCalculator`).
- **Cipher B**: undrawn "Stoch Overbought" 80 and "Stoch Oversold" 20, subscribed to by RSI and
  Stochastic alone; every component that took "all levels" now names the waves' five explicitly
  (`CipherBProvider.WaveLevels`), so nothing that heard ±53 hears anything new. Their templates gain
  `{zone}`: "RSI. 85.0. Overbought." Replaces the evening's per-component zone fields (never released).
- **Live narration of other lines' own levels.** `NarrationScanner.ScanLevelCrosses` read only a
  series' primary line, so the RSI crossing 80 would have been textured and clicked but never said.
  Lines whose levels the primary does not answer to are now narrated by name: "Cipher B RSI: crossed
  above stoch overbought, 80." Muted lines stay quiet (RSI and Stochastic start muted).
- **Plugin guide**: level subscriptions, component midlines and undrawn levels documented (it never
  mentioned `SubscribedLevelNames`).
- **`LevelConfig.Clone()` copies `IsDrawn`.** Caught by CI's `CloneCompletenessTests` on the first
  push (missed by the targeted local runs): without it a restored chart would have painted the
  undrawn 80/20 lines again.
- Tests: `CipherBStochasticZonesTests` (zone word, live narration muted/unmuted, waves unaffected),
  `CipherBLookTests.The_lines_own_80_and_20_are_not_painted`; each seen red with its part undone.

### An oscillator is above or below by its own midline (2026-10-17, night)

Cody, after the 80/20 zones: Cipher B's RSI and Stochastic still "don't follow the rule … above,
… below … are we patching a deeper problem with indicators with components with different bounds?"
Yes. The engine decided "which side" two ways: the above/below WAVEFORM at the component's declared
reference, the bright/warm TIMBRE and the above/below SOUND PATCH at the middle of the pane's visible
range. A survey of every built-in oscillator (`ReferenceLevel` vs pane) found them disagreeing on 30
lines: Cipher B's two 0–100 lines (midline 50, pane middle 0 — always "above"), and 28 oscillators in
auto-fit panes whose middle moves as you scroll (Momentum, ROC, CCI, TRIX, PPO, DPO, AVWAP bias,
funding, open interest, COT, crowding, Hurst, BTC strength, top/bottom confidence, pivot zone). Every
one of them declares its midline; only the timbre ignored it. One rule now
(`DefaultSonificationStrategy.AboveMidline`): the declared reference, the pane middle only when none.
Bounded oscillators whose midline is the pane middle (RSI, Stochastic, MFI …) sound exactly as before.
`OscillatorMidlineTests` sweeps all 54 oscillator lines in a deliberately shifted pane; with the old
rule all 54 fail. Not heard yet.

Still on the pane's scale, deliberately: PITCH (a line is pitched where it is drawn). Not yet on the
component's own scale: SPOKEN zone words and level-crossing narration for the two 0–100 lines read the
pane's levels, so they texture at 80/20 but are not told "overbought".

### Cipher B's RSI and Stochastic get their own zones; the waves' ±53 zone is heard (2026-10-17, evening)

Cody: "the rsi/stoch though doesn't have the texturing on the os/ob zones … shouldn't the os/ob zones
be -53 and 53?" Both lines subscribed to no level, so they had no texture. And ±53 is the waves' scale:
the lines run 0–100 on Cipher B's −100…+100 pane (as Market Cipher draws them), where +53 is just above
their middle and −53 is never reached. Cody chose **80 / 20, sound only**.

- **Their own 80/20.** First built as per-component zone fields; replaced the same night by
  undrawn LEVELS (see the next entry), so they are also spoken and narrated.
- **The waves' ±53 zone was never heard.** Its texture (0.10) sat under the waves' own base texture
  (0.15) and the engine plays the stronger, so between ±53 and ±60 the waves sounded as they did at
  zero. The levels now carry 0.30 at ±53 and 0.45 at ±60: clean, rough, rougher.
- Tests: `CipherBStochasticZonesTests` (texture and clicks at 80/20, none at 53/60/50; the waves graded
  past ±53 and ±60), each seen red with its part undone; `DeclaredKnobObservabilityTests` gains a cell
  inside a component's own zone. Not heard yet.

### Cipher B heard as drawn; Alt+Shift+D reads the whole ladder; areas stop at the last bar (2026-10-17, afternoon)

From Cody's report on the morning's push. Each change has a test seen red with its bug put back.
**Nothing here has been heard or seen on Windows yet.**

- **A bar is pitched by the side it is drawn on, not the candle.** Cody: Cipher B's WT Histogram
  "on october 11 daily has a positive sound but says negative"; Money Flow's "negative deeper
  sounding bars say positive". A Direction pitch with a zero reference fell through to the candle's
  direction (`ISonificationStrategy.CreateAudioPoint`), and histograms default to PriceDirection,
  which is the candle too. `OwnSignSide` now gives every bar that is not coloured by the candle its
  bullish/bearish pitch AND sound patch from its own value against `ColorBaseline` — the side its
  colour and its spoken number already use. Volume (coloured by the candle) and profiles (pitched per
  price bin) are unchanged. Sound changes for: Cipher B's WT Histogram and Money Flow (Money Flow
  regressed this morning, when its anchor moved from −80 to zero), MACD's and PPO's histograms,
  Chaikin Oscillator, CMF, Ease of Movement, Force Index. `BarPitchFollowsItsColourTests` sweeps every
  built-in indicator with the renderer's colour as the oracle.
- **Alt+Shift+D reads everything at the bar** (Cody: "doesn't seem to read the entire ladder").
  The manual promised OHLCV, the candle pattern, every active indicator and every signal; the code
  read the focused series (for candles not even the four prices) and the formations. Now: date; the
  focused series; the candle as drawn (OHLCV, Heikin-Ashi aware, then shape and wicks); every other
  visible series in the status line's order — price pane, then each pane down — with its readings and
  the marks that fire on that bar; formations last. Hidden series AND hidden components are not read
  (Cody: "read only what is not hidden"); muted ones are. A series with nothing at the bar is silent
  (Cody: "just don't say anything"). `BarDetailReadsEverythingTests`.
- **DSS Bressert says its state** (Cody: "dss narration should give its state"): after its two
  numbers, in words — overbought/oversold against the chart's own lines, rising/falling, "crossed
  above/below trigger" (`DssBressertProvider.StateWords`). Other indicators' detail sentences
  (`GetDetailFact`) remain unspoken: Cipher B's reads hidden components and repeats signals the
  ladder already names, so wiring them all in would break both rules above.
- **Filled areas stop at their last bar.** Cipher B's wave areas ran past the newest bar into the
  future space as a triangle back to zero (Cody's screenshot), and bridged warm-up and NaN gaps with
  slanted edges: `StandardRenderers.RenderLine`'s area branch closed at the pane's last and first
  slots. Each run of values is now its own polygon. Checking every filled kind found worse: at the live
  edge every cloud's latest run (since its last crossover) was not drawn at all — a `break` for a
  `continue` (Ichimoku, MA clouds, Cipher B's Anchor Fill). Component S/R zone bands stop at the last
  bar too. `AreaFillEdgeTests` (pixel-compared with an empty chart).
- **Cipher B's Adaptive OB / Adaptive OS are no longer components** (Cody: "do I really need
  these?"). They were hidden keyboard stops reading "Hidden. Adaptive OB. Oscillator. 53.0" on every
  bar in the default Fixed mode — the ±53 lines again. The thresholds are still computed (companion
  arrays) so Percentile mode still gates the dots. Saved charts drop them on load (components are
  rebuilt from metadata). Cipher B has 18 keyboard stops instead of 20. A saved strategy or alert that
  used them now says once why it cannot fire; and any alert whose component has disappeared from its
  indicator now warns once instead of returning nothing (`AlertEvaluator`). `CipherBAdaptiveThresholdTests`.
- **Browser harness: pages opt into session save and resume.** Deleting the session slots (this
  morning's fix) raced the close-time save Blazor makes about three minutes after a tab closes; one
  mixed run in four still failed. Each test page now writes "Resume last session" for its own circuit,
  and only `SessionRestoreBrowserTests` turns it on — proven by turning it off there, which made the
  restore test fail for want of a saved session.

### TradingView status lines; Cipher B redrawn as real Market Cipher B; DSS Bressert; breakout wording (2026-10-17)

From Cody's answers to the 2026-10-16 questions. Each change has a test seen red with its bug put
back. Checked in rendered PNGs and real Chromium; **not looked at on Windows or the desktop head,
and none of the sound changes has been heard.**

- **TradingView status lines replace the legend and the hover box** (Cody: "yes, the TradingView
  method — but anything shown must already be spoken"). `ChartStatusLine` builds one line per
  indicator per pane — name, inputs, each visible plot's value at the active bar in its own colour
  (ink plus a colour chip when the colour is under 4.5:1 on the backing) — and `ChartArea.razor`
  draws them as aria-hidden DOM text. The price pane's first line is symbol · timeframe · venue and
  O H L C (Heikin Ashi candles as drawn, as speech gives them). Values follow the keyboard cursor;
  the hovered bar wins only while the mouse moved more recently. The floating hover box is retired:
  its date is a label on the time axis, its price a label on the price axis (now in the scale of the
  pane under the pointer — over RSI it used to map the price range onto that pane), its OHLC the
  status line. Every plot's newest value is a filled label on its axis, stacked around the crosshair
  badge, kept inside its pane, and dropped rather than overprinted; the badge itself is clamped to
  its pane. New setting Settings → Appearance → "Status line background" (96%). **Speech parity:**
  every field maps to a key that already speaks it (Shift+F1, Page Up/Down, arrows on Candles,
  Alt+Shift+D, Alt+O); TradingView's change-from-previous-close is left out because nothing speaks
  it, pinned by `ChartStatusLineTests`. Not spoken and shown anyway: the "N mark types" count and
  the mouse-pointer value label. Found on the way: at 175% text the price pane's block ran 18px past
  its pane (a still chart never re-counted its lines). Tests: `ChartStatusLineTests`,
  `StatusLineInChartAreaTests`, `StatusLineBrowserTests`; `PaneLegendTests`,
  `HoverReadoutPlacementTests` and its browser test retired with what they tested.
- **Cipher B is drawn and computed like real Market Cipher B.** Compared against dated MCB
  TradingView charts — vBzsjpDu (legend "MarketCipher B 2.2.6", BTCUSD 1D Bitstamp HA, certain),
  37mQbQzO, AiASKFgl, LfGIzkND (probably MCB) — and the vendor's product image, digitised column by
  column and recreated with ours on the same bars.
  - *Look:* WT1 a light-blue #90CAF9 area, WT2 a #0D47A1 area in front of it, the "VWAP" (our WT
    Histogram) a yellow #FFEB3B area over both — it was green/red columns near-identical to Money
    Flow, the 2026-10-16 complaint — Money Flow a lime/red area at 50%, a money-flow bar along the
    bottom with MCB's big green buy dots, plain grey levels (±60 and 0 solid, ±53 dotted; the
    red/green zone shading is gone and saved charts drop it on load), and MCB's magenta "RSI" (a
    40-bar stochastic smoothed over 3) and red/green "Stochastic" (80-bar) lines. A new paint-order
    property draws the VWAP over the waves **without changing the keyboard order** (pinned). The
    renderer now honours an 8-digit colour's alpha on fills (no built-in used 8 digits there before).
  - *Bug:* the cross dots were written #RRGGBBAA; Skia reads #AARRGGBB, so the bull dot drew faint
    pale green and the bear dot grey. Same mistake fixed in Ichimoku's Kumo (pinkish/purple instead
    of green/red) and My Data event markers (purple instead of amber) — `CloudFillColourTests`,
    `MyDataEventsProviderTests`.
  - *Numbers (old behaviour kept behind each parameter):* slow wave a 3-bar average (`WTSignalPeriod`
    3, was 4: MCB's crosses land on our exact bar 164/182 times vs 121/182); Money Flow
    `MoneyFlowMode` MCB = 180 × SMA60 of Heikin-Ashi body/range, linear (error vs MCB 27–39 → 6–7.5;
    30-minute sign flips 25 → 6 against MCB's 4); buy dot `BuyDotMode` MCB = every upward cross with
    the slow wave at −60 or lower (`BuyDotLevel`), matching all 11 of MCB's big green dots within 2
    bars, 6 on the same bar (the old gated rule matched 3). The gold dot keeps the gated rule.
  - *Bug:* Money Flow's narration and alert thresholds were −70/−90, left from when the wave sat near
    −80; since it moved to zero it read "bullish" almost always and never "bearish". Now ±5.
  - **Heard differently** (derived, not heard): two new navigation stops at the end, "RSI" and
    "Stochastic", muted by default; Oversold Crossover fires more often (1 → 8, 1 → 3, 1 → 4 in the
    screenshot windows); crosses and the dots built on them shift; the WT Histogram is ~0.7× as large
    and Money Flow ~±40, so their grit is quieter. **StrategyLab:** any strategy reading Cipher B's
    crosses, dots, divergences, gold, WT Histogram or Money Flow changes; old numbers come back with
    `--set CIPHER_B.WTSignalPeriod=4 --set CIPHER_B.MoneyFlowMode=Legacy --set CIPHER_B.BuyDotMode=Gated`.
    `StrategyBatteryCommand`'s −80 Money Flow baseline is stale (not fixed). Tests: `CipherBLookTests`,
    `CipherBMcbFormulaTests`, `CipherBPaletteTests`.
- **DSS Bressert** (Cody: "I want to create this indicator"), `DssBressertProvider`, code
  `DSS_BRESSERT`. stoch1 = %K of price over P; MIT = EMA(stoch1, E); stoch2 = %K of MIT over MIT's own
  P-bar range; DSS = EMA(stoch2, E); Trigger = EMA(DSS, T) — the form MetaTrader, the TradingView
  scripts, NinjaTrader 7, ProRealTime, TC2000 and Infront share (not Blau's 1991 TASC DSS). Defaults
  13/8/5 (MetaTrader; TradingView 10/9, NinjaTrader 10/3 — probably Bressert's own, unconfirmed).
  Levels 80/50/20 with the Stochastic's sound contract; MIT computed but hidden ("Smoothed %K").
  EMAs seed from the first input as Pine does; a flat window holds the line. Agrees with a Python
  port of the TradingView script to 2.3e-13 over 5,416 BTC daily bars, and with MT4/MT5 from bar 200
  (their seeds differ early). Registered in both heads (`HostParityTests`) and StrategyLab's host;
  not in the default feature pack. Reference ports in `tools/dss_bressert_reference.py`. Its detail
  sentence ("Overbought at DSS 85.0, rising. Trigger 82.0. DSS crossed above trigger.") is built and
  tested but, like every indicator's `GetDetailFact`, **no key speaks it** — Alt+Shift+D reads raw
  values by design.
- **Browser harness: a fresh page no longer resumes the previous test's chart.** All pages share
  one Kestrel host and one Workspaces directory, and the terminal resumes the newest
  `__last-session__*.json`, so a test that loaded a chart handed it to the next page. The new
  status-line tests changed xUnit's order and CI failed 5 tests that pass alone (SpeechBrowserTests'
  empty-chart pair, RefusedControlBrowserTests' three — each opened onto a 100-bar chart);
  reproduced locally with the same five. `NewPageAsync` now deletes the slots unless
  `resumeSession: true` (only `SessionRestoreBrowserTests` asks).
- **Chart-pattern outcomes use Bulkowski's words** (Cody: "change the language as you found").
  Outcome speech: "Double top confirmed here: downward breakout, closed below the neckline at
  42,100…", "Range, upward breakout here: closed above the top at 110…". Line labels: "…, downward
  breakout" once it broke, "…, confirms on a downward breakout" while forming, "…, held" when it
  expired. Forming speech still names no direction (the narrator's no-forecast rule). Tests:
  `OutcomesAreSpokenAsBreakoutsAndFormingShapesNeverAre`, `TheTriggerLabelUsesBreakoutTerms…`.

### Eight chart visuals from Cody's 2026-10-15 screenshots (2026-10-16)

The IMMEDIATE FIXES filed from the two BTCUSDT 1d screenshots, all eight. Each fix has a test that
was seen to go red with its bug put back. Checked in rendered PNGs and, for the hover box, in real
Chromium. **Not looked at on Windows or in the desktop head, and not at density above 1.**

- **The hover readout box no longer covers the newest candles.** It was fixed at top right
  (`top:6px; right:72px`), over the live edge where the pointer usually is, in focus mode too.
  `ChartHoverTracker.PlaceReadout` now puts it at the top of the plot on the half AWAY from the
  hairline, inset by the real axis width on the right, capped at half the plot (it wraps rather than
  reach the hairline). On the left it sits beside or under the price pane's legend: the renderer
  publishes that rect through `IPaneLayoutService.MainLegendFraction`, and a legend appearing under a
  still mouse moves the box through the existing re-map. `HoverReadoutPlacementTests` (38),
  `HoverReadoutStaysOffTheHairlineBrowserTests` (1000x610 and 1400x950).
- **Each axis badge says whose value it is, and the price pane has one.** `CrosshairValueAt` took the
  first component with a value in declaration order, hidden ones included: on Cipher B that was the
  histogram (55.70) while the Wave Trend lines sat near −40. `CrosshairReadingAt` now prefers the
  component keyboard navigation is on, then the pane's first visible line, then bars; it never reads a
  hidden, disabled or sub-pane component. Each badge carries a 3px chip in that component's colour (bars
  by the same up/down rule they are painted with). The price pane gets a badge at the close, and its
  axis keeps labels out from under it. The badge is computed once per pane and handed to both the axis
  and the crosshair, so the two copies of the lookup are gone. `CrosshairBadgeAndLegendKeyTests`.
- **Histogram legend keys show both colours; the legend is no longer struck through.** Every
  non-volume bar or histogram got one fixed green key, so WT Histogram and Money Flow looked alike; bar
  rows now draw a two-column key in their real up and down colours. The legend backing goes from alpha
  178 to 245 (`LegendBackingAlpha`), so a level line behind it no longer reads as a strikethrough —
  chosen over stopping level lines at the box because the oscillator lines, bars and candles cross the
  legend too. Side effect: the legend now fully hides what is under it.
- **Axis numbers are grouped and lose the pennies.** `ChartMath.FormatAxisValue` formatted in the
  machine's culture with at least two decimals (`85000.00`). It now groups thousands in invariant
  culture, matching the hover readout, and prints only the decimals the gridline step can produce
  (`85,000`, RSI `70`, a sub-cent token still `0.000030`). The badge uses the new `FormatAxisReading`,
  two digits finer than the step (`55.7`, `440.25`; whole dollars on BTC, because `82,366.23` does not
  fit the ~50px badge and would fall back to `82K`). Nine-digit volume still falls back to `120M`. No
  speech path calls either formatter.
- **"Market Structure — 4 marks" says "4 mark types".** The count is the number of marker components,
  not marks on the chart.
- **Volume Profile is drawn under the candles.** The whole profile was painted last, so its strip (20%
  of the pane, right-aligned) covered the newest three months. `ProfileRenderLayer` now has two passes:
  the bins straight after the pane background, the point-of-control line over the candles (it is a
  level read against them). Bin opacity drops from 100 to 60: under the candles the bins become the
  surface the newest candles sit on, and at 100 a falling candle on Blackout fell from 4.07:1 to
  2.38:1. `VolumeProfileDepthTests` (36, every theme, real renderer pixels).
- **Formation lines say what they are and stop when they stop mattering.** Solid is the trigger (the
  price a close must break), dashed the measured target — confirmed in the detector. Labels now read
  "rising wedge trigger 74,912.00, break below" / "rising wedge measured target 60,367.00", with the
  speech's own role word and digits, grouped. The trigger ends at the bar that closed through it (or
  the bar the formation expired); the target ends at the first bar after the break that touched it
  (new `ChartPattern.TargetReachedAtIndex`, scanned forward from the break only), or one
  formation-length later. Triangles and wedges are drawn as their two sloping edges (`UpperBoundary`,
  `LowerBoundary` — the detector already fitted both and threw them away), and their trigger and target
  start at the trigger's swing. The real Bitstamp daily reproduces Cody's wedge (2026-03-29 to 05-06,
  trigger 74,912, broke 05-27, target touched 06-05). Causality tests now compare the reached index and
  both edges, with a vacuity floor of ten reached targets. **Speech unchanged.**
- **The white line through the closes is named.** It is the core Price series (`#FFFFFF`, seeded on
  every OHLCV chart), which the price-pane legend filter excluded by code. It is a navigable, spoken
  series, so it is now listed ("Price") rather than hidden (`ChartRenderer.MainPaneLegendSeries`,
  `PriceLineLegendTests`). Side effect: a white swatch on the price axis at the last close.

### The hover readout under a still mouse; resume picks the tab you left (2026-10-15)

From Cody's two BTCUSDT 1d screenshots. **Not tried in a real browser yet.**

- **The hover readout named a bar from an older chart.** `ChartHoverTracker` computed the readout
  (date, price, OHLC) and its hairline only on a mouse move. A keyboard user's mouse sits still while
  the chart zooms, pans, steps and ticks, so the box kept naming the bar that USED to be under the
  pointer: the screenshot's hairline sat on about March 31 while the box read April 20 with April 20's
  candle. The tracker now keeps the last pointer sample and re-maps it, through the same code as a
  mouse move, whenever a mapping input changes (data, viewport start/length, price range, log scale,
  active tab) or the pane dividers move. A re-map never plays the hover tick or speaks.
  - **Scroll, resize and Alt+Z re-map too (Cody's call).** The pointer did not move on screen; the
    chart did. The sample keeps the rect origin it was measured against, so on a new rect
    (`OnChartGeometryChanged`, reported by `canvasRegion.js`) the pointer's screen position gives its new
    place on the chart; off the chart, or with no known origin, the readout clears. Alt+Z hides the
    readout until the new layout's rect arrives, then re-maps. `keyboard.js` now measures a mouse move
    when its frame fires and calls `canvasRegion.syncRect()` first, so a move and its rect cannot
    disagree. Proven in real Chromium for a page scroll (`ChartHoverFollowsAScrollBrowserTests`, which
    blocks every mousemove: Chromium sent none). Cache-busts: `keyboard.js?v=20261015a`,
    `canvasRegion.js?v=20261015b`. Resize, Alt+Z and the desktop head are UNVERIFIED in a real window.
- **The resume names the chart that came back.** "Resumed your last session: 3 tabs." could not tell a
  blind user whether the RIGHT layout was restored. It now goes on with the active chart and the
  indicators added to it, read from the store after the restore: "BTCUSDT 1 day, with Cipher B, Market
  Structure and Volume Profile (Visible Range)." More than three are counted ("with 4 indicators"),
  none is "no indicators"; Candles, Volume, Price and drawings are not counted. If describing the chart
  fails, the old sentence is spoken and the resume still succeeds (`SessionResumeAnnouncementTests`).
- **Resume could pick a tab that was merely ticking.** Each browser session autosaves its own slot and
  resume reads the most recently written one. A live chart dispatches on every tick, so an untouched
  tab rewrote its slot every 30 seconds and could win the resume over the tab the user had just closed.
  The 30-second sample now writes only when the workspace differs from what it last wrote (the scroll
  position does not count); the save on tab close still always writes.
  - **Not the cause of Cody's missing indicators.** Brave's session records and the app's own log show
    he had one terminal tab at a time, each tray "Restore" opened a new tab that resumed the right
    session, and Market Structure and Volume Profile were first added at 02:09, after the second
    restore. His 02:11 session resumes with all six series in a real WebHost
    (`SessionRestoreBrowserTests`). Why he remembers them before that is an open question.

### Cody's four questions: live candles, bar dates, formation pinning, the 0 key, alerts (2026-10-09)

Cody asked why the forming candle starts flat, why today's daily bar reads "October 8", what `;`
does, why `0` works on price, and why the alert form's choices don't fit together. Each answer
turned out to be a defect. Fixed in five parallel streams; every fix has a behaviour test proven
red with the defect put back. **Nothing here has been heard by ear yet.**

- **The forming candle is the provider's, not a flat one.** The live feed started a fresh bar from
  the first trade after subscribing and `ChartFeed` replaced the provider's real forming bar with
  it: open = high = low = close = one trade, volume = one trade. Bitstamp (the default) and most
  trade-tick providers; Binance, MEXC and Schwab were fine. A live bar now continues the fetched
  one (provider's open, widened high/low, latest close, volume plus only new volume) per stream
  (`LiveStreamSource`); full-candle streams still replace without double counting.
  - **Stock daily candles never moved live.** Alpaca/Polygon/Schwab stamp daily bars at 04:00 or
    05:00 UTC; live buckets were matched by exact date and dropped. Now matched to the nearest period.
  - **Per-provider defects found on the way:** Tradier re-added running volume on every trade;
    Finnhub kept one trade per batched frame and rolled its day over at chart-open time; Twelve Data
    and Coinbase re-added the fetched bar's volume on every tick (Coinbase also had no 4h/1w mapping);
    Oanda days were 17:00 New York (now UTC days, Monday weeks); Kraken's `timestamp` is the interval's
    END (every live bar a period late) and Kraken weeks start Thursday (1w is now resampled into Monday
    weeks); Polygon minute bars used the window end; Bitstamp's 250 ms throttle discarded trades between
    kept ones (now folded).
- **Daily and longer bars are named by their own date.** Bars stamped at UTC midnight were converted
  to local time, so in Chicago today's bar read "October 8" and weekly bars read as Sunday. One rule,
  `SpeechTimeFormatter.ToBarDisplay`: bars of a day or longer use their UTC date, intraday keeps the
  local clock. Every readout uses it — arrows, bar-close announcements, playback, Ctrl+Shift+D, the
  layout description, drawings, the tactile strip, crossings, the canvas axis, and the hover readout
  and bar slider, which had skipped the formatter and disagreed with the arrow keys.
- **`;` pins the formation you asked for.** One press ran the command TWICE: keyboard.js sends `;` as
  `OEM1` and ChartArea's fallback sends `;`, and the 50 ms duplicate check compared the raw spellings
  — so every press pinned forward and straight back ("1 of 2" forever). `GlobalInputService` now dedupes
  on the normalised key (also covers `/` and `?`). Reproduced in the browser suite. Also: the first press
  moves to the second formation (the first is already leading); the announcement names the containing
  formation; containment names only formations `;` can reach at that bar (it used to name parents not
  yet knowable there); a pin survives index shifts from the live cap and backfill.
- **`0` works only where a reference level applies** — an indicator in its own pane with a declared
  neutral. On the price pane it said "Level added at <close>", a duplicate of a horizontal line; now it
  says "No reference level applies to {series}." Levels already saved on price panes are kept.
- **Horizontal and vertical line drawings play the crossing chirp** a level plays, and Ctrl+Left/Right on
  the candles or price line stops at their crossings with the trend-line ones. Hidden trend lines are no
  longer stops.
- **The simple alert form.** Conditions now depend on the target (no zones for Price); zones come from
  the indicator's own overbought/oversold levels — one source for the dialog and the evaluator, so an SMA
  zone alert (accepted, never fired) is refused; the dead Upper/Lower band zones are gone. New **Touches**
  (bar range reaches the level). Compare with a **line** ("price touches SMA 50"). Value prefilled with the
  current reading. Zone and direction alerts read the live bar, not the reading cursor; Cipher B alerts read
  the component they name; indicator Changes direction is the line turning, not the candle colour. An alert
  on an instance removed from the chart is never answered by another series (it was watched as SMA 20).
  Fire speech says "64,250", not "64250.000000".
- **The Advanced editor** offers "Crosses above/below another line" (price close vs a line, SMA 20 vs
  SMA 50), binds a leaf to the instance it names, and loads higher-timeframe data in the alert path, so a
  daily chart can watch the weekly SMA 50. An HTF indicator leaf used to test the weekly CLOSE instead.
  Leaves that can never be true are refused at creation.
- **Accessibility review of both alert forms** (forms, live-region and keyboard specialists): 18 findings,
  all fixed. Arrowing a closed select no longer loses choices further down; every replaced choice is
  spoken without cutting off the option being read (the coordinator's StateChange now honours
  `Interrupt`); a typed value is kept; Add says what is missing; operators and tree rows are plain words.
- **CI:** `tests.yml` also runs on `fix/**` branches; full suites run on GitHub, not the desktop.
- Suite 8,564 listed. Decisions for Cody and what is unverified are in TODO.

### A2q: `Services/Accessibility` mutated; four defects in what a blind user hears and feels, fixed (2026-10-01)

Run in its own worktree, three mutants at a time. It was paused mid-run because test runs were
making noise on Cody's desktop, and re-run from scratch on the silence fix. Full write-up:
`scratchpad/a2q_REPORT.md`; target list: `scratchpad/a2q_SURVEY.md`.

- **50 mutants over a folder of 49 files, 26 of them never mutated before. Honest catch rate
  5/49 (10.2%), the lowest of any campaign; raw 7/50.**
  - The five files with no direct test (NavigationEngine, ViewportManager,
    SeriesNavigationRegistry, NotificationHub, HistoryBufferCoordinator) caught nothing.
  - Neither did fired-alert delivery, the forming-pattern gates, the order earcons or the
    pane model.
  - The browser suite was run on every C# survivor, and it made one of the five catches (C06).
- **Two catches scored as not honest.**
  - N06 (Home jumps to bar 0) is equivalent: the reducer clamps the cursor anyway. Its only
    catch was a flake.
  - E02 was caught only by a count of stop-loss notes, which cannot tell a stop from a take
    profit.
- **The Tab-trap flake, finally caught with its message.**
  - Test: `ModalBrowserContractTests.Tab_never_escapes_an_open_dialog(routeName: "AIAnalystModal via toolbar")`.
  - Message: "AIAnalystModal reports 2 focusable controls but Tab only ever reached 1 of them".
  - It passed 3/3 with the mutant and 3/3 clean. No root cause yet.
- **All 44 survivors closed** with behaviour tests, each proven red on its mutant and green clean
  (51/51 in `a2q_prove_kills_results.json`, including guards for the four fixes). 81 new cases.
- **Four production defects, each demonstrated by a failing test first:**
  1. After a scroll-back loaded older history, narration spoke an old bar as if it had just
     closed, and the next close repeated a signal already heard. Index memory is now shifted
     when bars arrive in front (`AutoNarrationService`, `NarrationScanner.ShiftIndices`).
  2. With exactly three panes and Main declared last, `ChartPaneModel` swapped the other two:
     `List.Sort` is unstable. It is now a stable order.
  3. Finishing a two-point drawing from the keyboard said "Trend line placed, point 3 at …".
     The anchored VWAP named the wrong point too.
  4. After the Dot Pad reconnected, its braille strip stayed blank until the value changed:
     both the coordinator and the driver skipped the first text as a repeat.
- **Re-checked on merge.** Both suites green (8,375 C#; 232 + 1 skipped browser), and fix 3 red
  with the old line put back.
- **Decisions for Cody, and what is still unverified,** are in TODO.

### Test runs no longer play sounds, speak, or take the keyboard on the developer's desktop (2026-10-01)

Cody: running the suites played the chart's tones, spoke over his screen reader, and grabbed his
keyboard while he worked. A process watch during the A2q campaign found the causes.

- **Chart tones.** The WebHost's audio driver started `pacat` (or `pw-cat`/`aplay`) to the
  local sound server, in the browser-test host AND in the C# suite's integration hosts.
- **Speech.**
  - The browser recorder passed every sentence through to `speechSynthesis`, which on Linux
    Chromium is speech-dispatcher.
  - Hosts not pinned by hand could choose spd-say or Orca.
- **Keyboard and focus.** `ModalFocusPersistenceProbe.Where_is_focus_when_the_AT_SPI_bridge_is_left_ON`
  ran in every browser suite with the accessibility bridge on, so the developer's running Orca
  attached to an invisible browser and followed its focus.
- **The fix, one switch.** `DesktopOutput` (`ACCESSIBLETRADER_NO_DESKTOP_OUTPUT=1`) is read at
  every local-tool probe:
  - the audio driver (browser mode instead);
  - the speech manager (browser speech);
  - the notification plan (no `notify-send`, `paplay` or `spd-say`);
  - the tray's tools;
  - the startup `xdg-open`.

  A module initializer in each test project sets it before any test runs. Never set in
  production.
- **The browser side.**
  - The test Chromium gets `--mute-audio`.
  - The speech recorder replaces `speechSynthesis.speak` with a silent stub that still fires
    start/end.
  - The bridge-on probe is opt-in (`ATT_PROBE_WITH_SCREEN_READER=1`, `ScreenReaderProbeFact`).
- **Verified.**
  - Both full suites ran with a watcher recording any player, speech or notification tool,
    or `xdg-open`, whose ancestry is a test host: **zero**.
  - The watcher itself was proven: with the switch off, a browser run's `pacat` appears.
  - `QuietDesktopTests` (5) go red with the switch disengaged.
  - **Not covered by the watcher:** sub-second probes (`gdbus` asking whether Orca runs). The
    unit tests cover those by showing every probe finds nothing.
- **Still not silent:** the probes in `AccessibleTrader.BrowserTests` still rewrite
  `scratchpad/` JSON and screenshots on every run (harmless, but they dirty the tree).

### F2 silences only the chart and its narration; two detail-key defects fixed (2026-10-01)

**Cody, 2026-09-30: "F2 should silence just chart navigation and narration."** Narration is
F2's alone (no longer also Shift+F2's), and new bars count as narration (his answers to the two
open edges).

- **Channels.**
  - `SpeechChannel.Manual` is renamed `Chart`. It is still the default, so anything that
    names no channel behaves as before.
  - New `Narration`: muted by F2, and it keeps Event's priority, so an arrow press does not
    cut off a bar-close sentence.
  - New `Interface`: muted by neither key.
  - `Event` is Shift+F2's: alerts, monitoring, connection status.
  - `AnnouncementEvent` gained the same `Channel` override `FeedbackRequestEvent` has.
- **What moved.**
  - **Narration, now F2:** bar-close narration, the new-bar sentence, forming patterns and
    formations, and background-tab bar closes.
  - **Interface (about 90 publishers):**
    - every dialog, the toolbar and the context menus;
    - changes you make from the keyboard: hide, mute, delete, reference levels, tabs,
      session resume;
    - provider/timeframe coercion, and "X dialog opened/closed" (`MainLayout` no longer
      checks F2).
  - **Event:** data-link and rate-limit status, the monitoring on/off sentence (it repeats on
    symbol switches), the order book's large-order radar, a strategy's repeated-signal note,
    and a strategy warming up.
  - **OrderEvent (money):**
    - order placed and OCO placed (`ReportSuccess`);
    - the dashboard's cancel / OCO / close successes (failures stay Critical);
    - a strategy's closes and resumes;
    - "while you were away" reconciliation.

    These were already silenced by F2 before this change; under "chart speech" that is wrong.
- **Wording.**
  - F2 now confirms "Chart speech on/off".
  - The Settings checkbox is "Chart speech and narration (F2)".
  - Help and Settings rows are updated. Two were stale already: Help's Shift+F2 row quoted
    "Event speech on/off", and Settings' table listed F4 as "Context summary".
  - QUICKSTART, USER_MANUAL, SHORTCUTS and README now state the new rule.
- **Review.** An accessibility review of the first pass found the money successes still muted
  and three repeating messages made unmutable. It also found `ok ? StateChange : Error`
  publishes and direct `Speech.Speak` calls in two context menus that the tagging had missed.
  All are fixed.
- **Guards.**
  - `F2ChartOnlyMuteTests`: a channel × mute table written as the policy; narration keeps
    priority; dialog vs chart through the real coordinator.
  - A scan requiring every component publish, and every component's direct router `Speak`,
    to name its channel.
  - Two new-bar tests: Shift+F2 does not silence the bar close, F2 does.
  - A browser test: with chart speech off, opening the Object Tree is still announced.
  - Twelve sabotages, each proven red.
- **Monthly charts were described as "1 minute".** `ChartLayoutDescriber.SpokenTimeframe`
  lower-cased before matching, so "1M" became minutes. "1y" was spoken as the bare code. Both
  were demonstrated failing first, then fixed.
- **The detail key's Bollinger-squeeze and MACD-cross facts were always silent.**
  - They read the components "Upper"/"Lower"/"MACD", but the providers emit
    "UpperBand"/"LowerBand"/"Macd".
  - The lookup is case-sensitive and a missing component reads as empty, so the facts never
    appeared.
  - The test that closed A2j's J48 used the same wrong names. The fixture now takes its names
    from the real providers' metadata, and MACD gained its first tests.
  - Demonstrated failing first, then fixed; the old names are kept as fallbacks.
- **Caveat.** `Interface` has no mute. Screen-reader users have their reader's speech mode;
  browser-voice-only users have "Speech output on this device". The classification is a
  judgement per publisher (reading the chart is Chart; changing something is Interface;
  something that happens to you is Event), and any line of it can be moved.
- Suite 8,254 → 8,289 run (8,284 listed), browser 232 → 233.

### A2p's decisions: the heartbeat seam, `IsPublishable` deleted, base58 addresses checked for their network (2026-09-28)

- **Heartbeat seam (Cody: "add the hook").** `ReconnectingWebSocket.HeartbeatWriter`, internal,
  always `SendFrameAsync` in production, lets a test fail pings on a socket that stays open.
  Two tests against the loopback server: the socket is declared dead on the THIRD consecutive
  failure and a new connection follows, and a ping that gets through resets the count (the
  sixth attempt, not the third, condemns 2 fail / 1 ok / 3 fail). Proven red: W04 (`>=` → `>`),
  the reset removed, and the dead socket kept. Removing only `_ws = null` is equivalent, because
  the dispose alone makes the receive loop reconnect.
- **`CausalityContract.IsPublishable` deleted (Cody).** It had no caller; `SignalCatalog` asks
  `RefusalReason`, which is now what the contract test pins (proven red with Undeclared published).
  `ICustomIndicatorRegistry.IsPublishable` in Core is a different method and stays.
- **Base58 addresses: the version byte and length are now checked. Four live defects,
  demonstrated then fixed.** The checksum only proves a string was not mistyped. The genesis
  Bitcoin address offered for a **Litecoin** withdrawal came back **Verified**, as did a `T…`
  string with version 0x42 (Tron is 0x41), a `3…` with version 0x06, and a `1…` carrying a
  19-byte hash. `Base58Check` now requires a 21-byte payload and one of the network's own
  version bytes: Tron 0x41; Bitcoin 0x00/0x05; Litecoin 0x30/0x32/0x05 (Litecoin kept Bitcoin's
  P2SH byte, so a `3…` address is valid on both). Real vectors for the valid cases (Tron's USDT
  contract, the wiki P2SH example, two Litecoin addresses). The look-alikes were built with an
  independent Python encoder, since no one publishes a valid-checksum wrong-version address.
  Proven red three ways (version check, length check, Litecoin accepting 0x00).
- **The `_sendLock` comment was wrong on every platform, not just Linux.** It said an overlapping
  send throws; on .NET 10 the runtime's `ManagedWebSocket` queues it behind its own `_sendMutex`.
  The Windows and Linux 10.0.9 runtime assemblies ship that same managed implementation (no
  WinHTTP path), so this was checked by inspection, not a Windows run. The lock stays (it keeps
  the class inside the documented one-send-at-a-time contract) and the comment now says so.
- Suite 8,243 → 8,254, all green.

### A2p: the Sdk mutated; a corrupt SegWit address no longer reads as verified (2026-09-25)

Run in its own worktree, three mutants at a time in separate tree copies. Full write-up:
`scratchpad/a2p_REPORT.md`.

- **49 mutants over 24 Sdk files, 22 never mutated before; honest 24/48 (50.0%)**, raw 26/49.
  The lowest rate since A2h's indicator maths. No flake catches (all 26 re-caught alone, green on
  the clean tree), the first campaign with none since the flake fixes above. One proxy catch
  scored as not honest: W01 (the reconnect give-up path not calling `onDisconnected`) was caught
  only by a source scan that `if (false) _onDisconnected?.Invoke();` satisfies. One equivalent:
  W05 (removing `_sendLock`), because .NET 10's WebSocket queues overlapping sends instead of
  throwing. Measured on Linux only, and the behaviour test written for it was removed because it
  was never seen red.
- **23 of 24 survivors closed**, each proven red on its mutant and green on the clean tree; 35 new
  cases (8,208 → 8,243). Among them: `CryptoAddressValidator` (a network name containing "BTC"
  routed to the Bitcoin check, taproot, a 43-character EVM address accepted, a Bitcoin address
  verified for a Tron deposit), `ReconnectingWebSocket` against a real loopback server (five
  behaviours), plus rate limiter, provider result, timeframes, buffers, causality and the plugin
  host's fallback HTTP client.
- **Production defect, fixed:** `CryptoAddressValidator` accepted either bech32 checksum for any
  witness version, so BIP-350's own invalid vector (`bc1q…kemeawh`, a v0 address with the bech32m
  checksum) was announced **Verified**. Version 0 now requires bech32 and version 1+ bech32m.
  Only the Bitcoin and Litecoin paths reach this check, and both are SegWit. Proven red with the
  fix removed (re-checked on merge).
- **Open:** W04 (the heartbeat declaring the socket dead after four failed pings instead of three)
  needs a production test seam to be observable. The `_sendLock` comment's claim that overlapping
  sends throw is false on .NET 10 / Linux; Windows is unchecked. `CausalityContract.IsPublishable`
  has no caller. The validator does not check Tron's version byte (noticed, not tested).

### The load-only flakes: three causes, each forced to fail on demand, then fixed (2026-09-25)

Every campaign since A2 has had to audit its catches because a few tests failed only under load.
Waiting for them failed: three concurrent full suites and eight loaded runs of the modal browser
tests reproduced nothing. Forcing each suspected cause reproduced every one first time.

- **bUnit's synchronous triggers do not wait for a busy renderer.** `Click()`, `Change()`,
  `Input()`, `KeyDown()` and the rest hand the event to the dispatcher and discard the Task. Idle,
  the handler runs inline; busy (a dialog's `ShowAsync` still running after its first render, a
  timer tick), the event is queued and the next line asserts on a handler that has not run.
  Holding the dispatcher for 500 ms reproduced both recorded flakes: the bar slider's
  `Input("15")` dispatched no `NavigateAction`, and the refused Submit spoke nothing. **Fixed for
  all 204 call sites in 30 files at once** by `AccessibleTrader.Tests/SettledEventDispatch.cs`: the
  same method names declared in namespace `AccessibleTrader.Tests`, which C# searches before a
  file's `using Bunit;`, so every existing call now runs bUnit's trigger as a dispatcher work item
  and waits for it to start. That is the `cut.InvokeAsync(() => x.Click())` form 26 sites already
  wrote by hand. It does not wait for the handler to finish, so a handler parked on an unanswered
  JS call still returns. `SettledEventDispatchTests` holds the dispatcher on a gate and fires all
  14 overloads; proven red with the wait removed (14 fail) and with the route to the renderer
  broken (15 fail). **Caveat:** that route (`GetTestContext`) is internal to bUnit 1.40 and is
  reached by reflection; an upgrade may break it, and the guard names it when it does.
- **`ProfileNarrationTests` shared a global with a test that pretends a browser covers BTC/USD.**
  The headless monitor reads the static `CircuitAlertCoverage` on every poll. `HeadlessNarrationTests`
  registers a circuit covering BTC/USD; `ProfileNarrationTests` polls BTC/USD and was in no
  collection. Registering that circuit around it silenced it. Now enrolled in `CircuitCoverage`,
  which has a definition, and `CircuitCoverageEnrollmentTests` requires every class that polls
  through `HeadlessMonitorHarness` or touches `CircuitAlertCoverage`/`BrowserPresence` to be
  enrolled, and every enrolled class to still touch them. Proven red with the attribute removed.
- **The rate-slot test was a stopwatch.** `RateLimiter`'s window opens at construction, so a
  thread parked 50 ms between two calls gave the second a fresh window; a 60 ms sleep reproduced
  the failure. It now uses a one-minute window, checks the second call is still pending, then
  cancels it. Proven red with `ExecuteOnceAsync`'s `WaitAsync` removed.
- **Not reproduced, so not claimed as fixed.** `ModalBrowserContractTests`' Tab/Shift+Tab traps
  (A2k's three false catches; the campaign log cut the route names off). The one mechanism visible
  in the code is fixed as a diagnosis: the Escape and trap tests ignored whether focus had reached
  the dialog's target before pressing keys, so a slow open reported as "Tab escaped the dialog".
  That is now a precondition (15 s) with its own message. `SettingsModalTests.SettingsModal_OpenedOnATab…`
  (one co-failure in A2l, no message kept) was not touched.
- Verified: full C# suite green unloaded and three times under 3-way load (8,208 each), browser
  suite 232/232, all four guards proven by sabotage with files restored byte-identical and a green
  control. `ValueDeviationTests.BarsBeforeTheWindowFills_HaveNoReading` (suspected vacuous) is not
  a flake and is still open.

### A2o: the WebHost's C# mutated; the security rules' callers are now tested too (2026-09-24)

Run in parallel in its own worktree. Full write-up: `scratchpad/a2o_REPORT.md`.

- **48 mutants over 16 WebHost files; honest 40/48 (83.3%)**, the best of this round. No flake
  catches (all 97 catching tests green on the clean tree, all 41 re-caught alone). One proxy
  catch scored as not honest: `'unsafe-inline'` in `script-src` was caught only by the demo/strict
  CSP parity test, whose natural "fix" is to copy the edit.
- **All 8 survivors closed**, each proven red (`a2o_prove_kills.py`), 11 new cases in
  `DemoHeadIsLockedDownIntegrationTests`, `FullModeBindRefusalIntegrationTests`,
  `CircuitRevalidationTests`, `SpeechFallbackTests`, `BrowserAudioPumpTests` and two extended
  suites. **Three share a shape: the rule was well tested and its one caller in `Program.cs` was
  not** — `--demo` booting the full terminal (live trading, API keys, scripts, unauthenticated
  alert endpoints), Full mode served on a public bind with the `--unsafe-remote-full` check
  inverted, and a locked-out sign-in's 429 page saying "too many requests". The bind guard was
  untestable before; .NET 10's `WebApplicationFactory.UseKestrel()` made it testable. Also closed:
  an open circuit surviving a password reset or a lockout, a failed spd-say/Orca call leaving
  every later phrase spoken twice, and the demo's browser audio treating every buffer as silence.
- **No production defect.** One test seam: `WebHostAudioDriver` gained an internal constructor
  taking the player probe (DI sees only the public one). **Noted:** Blazor adds a second CSP header
  (`frame-ancestors 'self'`); browsers enforce both, so hosted/Full's `'none'` still wins, but a
  test that reads "the" CSP header off a Blazor page will be fooled. The bind-refusal test binds
  `0.0.0.0` and has not been run on Windows or macOS.

### A2m: `Services/Analysis` mutated for the first time; flags on a clean trend are announced (2026-09-24)

Run in parallel in its own worktree. Full write-up: `scratchpad/a2m_REPORT.md`.

- **45 mutants over a directory never mutated before (4,269 lines, 17 files); honest 29/45
  (64.4%).** The audit removed three catches (unrelated Blazor/WebHost tests failing under load)
  and reversed one pass: M23's run aborted at 4,634 of 8,088 tests and the harness had read that
  as green. The harness now marks any run short of the baseline total ABORTED. **That is a new
  harness rule worth carrying to every campaign.**
- **All 16 survivors closed**, each proven red, 23 new cases across 10 files including a new
  `LevelProvenanceServiceTests` (the service had no tests). Gaps worth naming: the causality test
  cut the series at only three bars, so a five-bar look-ahead slipped between the cuts;
  `NoPatternIsKnowableBeforeItsStructureIsComplete` described that exact defect in its doc comment
  and asserted a bound that allowed it (tightened); the weekly-MA threshold could not tell the
  honest 100 from the leaked 133.
- **Fixed: a clean trend never announced its flag.** `ChartPatternDetector` returned early when the
  chart had fewer than three swings, before the flag scan, which does not use swings at all. So a
  steady trend never heard its bull flag, and on longer charts flags appeared only after the fact,
  when panning back. The three-swing check now guards only the swing-built shapes. Demonstrated red
  first; restoring the old line turns 5 tests red. **Consequence: more bull and bear flags will be
  announced on trending charts. Not heard.**
- **Recorded, not changed:** `ValueDeviationTests.BarsBeforeTheWindowFills_HaveNoReading` looks
  vacuous (its flat fixture reads nothing anywhere, so it passes whatever the warmup rule is).

### A2n: `Services/Trading` and `Services/Scripting` mutated; Escape while the balance loads now cancels (2026-09-24)

Run in parallel in its own worktree. Full write-up: `scratchpad/a2n_REPORT.md`.

- **45 mutants; honest 24/45 (53.3%)**: Trading 15/24 (62.5%), Scripting 9/21 (42.9%). The audit
  threw out two catches (a flaky `OrderTicketErrorStateTests` case, 2 of 47 full runs, and a
  collateral cascade when a leaked worker slot hit the 16-worker cap). All 21 survivors closed,
  each proven red (`a2n_prove_kills.py`), 24 new cases.
- **`QuickTradeExecutor` had no test at all**: a long sent as a sell, a limit sent as market, the
  stop never sent, a refused order dropped silently and an uncertain order counted as a success all
  survived. Now `QuickTradeExecutorOrderTests` (6). Also closed: a limit priced at the last close
  instead of the cursor bar, every risk-sized short refused, a late balance re-arming over a trade
  already set up, and a ladder over 100% selling more than was held.
- **Nothing had ever made a script worker misbehave.** The call deadline, the kill on timeout,
  the memory and CPU quotas, the worker cap and slot release, and the refusal of an unexpected reply
  are now `ScriptHostSupervisionTests` (8); the sandbox refusal paths `SandboxRefusalPolicyTests`
  (6). `MacSandboxExecLauncher` gained an internal test constructor (no behaviour change).
- **Fixed: Escape while the account balance was being fetched did not cancel the arm.** The fetch
  armed "if the user has not moved on", read as "the stage is still Idle", and Escape leaves it
  Idle too: the cancel was answered "Nothing was armed" and the trade armed anyway seconds later.
  A fetch generation bumped by Disarm now drops the late arm, and the cancel says "Quick trade
  cancelled." Found as an unverified note in A2n's report; demonstrated red first
  (`QuickTradeEquityFetchTests.EscapeWhileTheBalanceIsFetchedCancelsTheArm`), both halves of the
  fix proven by sabotage.
- **Recorded, not changed:** `IScriptWorkerLauncher.SandboxApplied` has no reader, so the user is
  never told when the script sandbox is missing, though its documentation says they are. The
  Windows AppContainer refusal still has no test seam.

### A2l: `Services/Strategies` mutated, and three strategy-builder defects fixed (2026-09-24)

Run in parallel with A2k in its own worktree. Full write-up: `scratchpad/a2l_REPORT.md`.

- **44 mutants over 19 never-mutated files; honest 23/44 (52.3%)** after the false-catch audit
  (one catch was a contention flake). **Everything that sets a price or refuses was guarded**
  (level providers 8/8, bundle import 3/3, causality refusals 4/4). **The layer that turns a
  button press into a running strategy caught 0 of 18.** No test built the coordinator, the
  facade, the auto-loader, `EditableStrategySpec`, the narrator or the multi-timeframe cache;
  the modal tests substitute them. All 21 survivors closed, each proven red (`a2l_prove_kills.py`):
  32 new cases in `StrategyLifecycleTests`, `EditableStrategySpecRoundTripTests`,
  `StrategySpecNarratorTests`, `SetupSonifierSpeechTests`, `MultiTimeframeDataServiceTests`,
  `ScriptStrategyCausalityFingerprintTests` and `LabRunnerTests`.
- **Saving a strategy in the builder replaced its exit.** `EditableStrategySpec.ToSpec` hard-coded
  "move the stop to breakeven at the first target", so opening a catalogue spec that trails by
  ATR (v24, whose own comment calls the trail "the real exit") and saving it, even to rename it,
  swapped the trail for breakeven. The same round trip dropped the indicator a component-based
  stop or target resolves against. Both now survive load and save. **Caveat:** the builder still
  has no control to EDIT either; the fix only stops Save destroying them.
- **Strategies added from the builder or compiled from a script ran without their library id.**
  A position they opened could not be re-adopted after a restart (it came back as an orphan beside
  a fresh flat copy of the strategy), and a workspace save silently dropped the strategy.
  `StrategyLibraryFacade` (Add to Engine) and `StrategyModalCoordinator` (scripts) now pass it.
- **None of the three was checked in the running app.** Each was demonstrated red first
  (`scratchpad/a2l_defect_demo_before_fix.txt`).

### A2k: the JavaScript, mutated for the first time (2026-09-24)

Every earlier campaign mutated C#. The JavaScript the browser runs had never been mutated, and it
is where the defects that passed ~8,000 green tests keep turning up (Alt+T, Space cancelling
every button, the `;` keys that never reached .NET). Reading it for this campaign found the two
keyboard dead ends above before a mutant ran.

- **70 mutants over all nine shipped scripts.** Killers in order: the node suites, the C# tests
  that read JS source, then the full browser suite (231 tests), per mutant. Control green on all
  three layers, every file restored byte-identical. Scripts and results:
  `scratchpad/a2k_js_sabotage.py`, `a2k_sabotage_results.json`, `a2k_prove_kills.py`.
- **Raw 51/70; honest 48/70 (68.6%).** Three "catches" (K35 right-click, W01 speech interrupt,
  P02 notification tags) were a modal Tab/Shift+Tab browser test failing under CPU contention,
  with four C# campaigns running alongside. Re-run with each mutant applied, all 120 modal tests
  passed. **`ModalBrowserContractTests`' Tab trap tests are flaky under load**: worth knowing
  before trusting a red CI browser job.
- **Where it was guarded and where it was not.** `treeKeyboard.js` 8/8, and most of the keydown
  trap, by the node suites that already existed. Four scripts had **no test at all**:
  `canvasRegion.js` (1 of 3 caught, by a browser test), `audio.js` (0/4, including a left/right
  swap on a chart panned by time), `webSpeech.js` (the interrupt of an order fill), `webPush.js`
  and the service worker. K29 (Shift+/ mapping) was caught only by a test written earlier today.
- **All 22 survivors closed:** 20 killed by named tests, each proven red on its mutant
  (`a2k_prove_kills.py`); R02 and R03 superseded, because the code they mutated in `reconnect.js`
  was replaced by the accessibility-review fix and its replacement is guarded. New suites, all in
  CI: `canvas-region-tests.mjs`, `audio-tests.mjs`, `webhost-small-tests.mjs`; new cases in the
  keyboard (Menu key, chord hard-stop, arrow keyup, focus not stolen back, chart-focus release,
  text-size clamp, touch detection, HiDPI ratio) and gesture suites (off-chart drag release,
  right-click).

### Pitch follows, in Properties; and the clone sweep was already done (2026-09-24)

- **Properties → Sonification → Pitch follows** exposes each component's `PitchMapping`
  (the value, up or down, the candle's direction, nothing). The engine has honoured it all
  along and nothing could set it, so "should the candle body follow the Y axis?" (Cody,
  2026-09-17) needed a code change to try. The candle body obeys it; wicks keep their fixed
  880/220 Hz and the control is not offered for them. A hint under the select says what the
  choice means, including that a value-pitched line on the price pane is relative to the
  viewport and shifts on pan and zoom. `PropertiesModalTests.PitchFollows_*` (2), proven red.
  **Not heard.**
- **The "workspace-persistence clone sweep" carried in TODO since 09-19 had been done on
  09-06** (`4702a00f`): `CloneCompletenessTests` sets every property of all ten hand-written
  `Clone()` types by reflection and diffs, rediscovers the type list, and the factory's clone is
  deleted. The item is removed from TODO.

### An accessibility review of this session's own fixes, and what it found (2026-09-24)

The `accessibility-lead` agent the project hook asks for is not registered in this harness, so
the keyboard-navigator and live-region specialists' rules were applied by a general agent over
the back-off and the two keyboard fixes. It found six problems, two serious, all in code written
the same day. Each is fixed and proven red against the code it replaced.

- **The reconnect announcement repeated every second.** The framework rewrites the attempt
  counter once a second while it waits (a countdown), and `reconnect.js` re-announced the whole
  ~25-word sentence, assertively, on every rewrite. The back-off stretched that to about seven
  minutes. Now the sentence goes to `#reconnect-status` once per state change, and "Still
  reconnecting. Attempt N of 30." goes to a new polite `#reconnect-progress` at most once a
  minute. `role="status"` came off the assertive node (it implies polite). The .NET 10
  `resume-failed` state is announced and shown like `rejected`. A browser test counts the
  writes during a real drop (was several in 2.5 s, now one), and `tools/jstests/reconnect-tests.mjs`
  (5, in CI) drives seven simulated minutes. `LiveRegionInventoryBrowserTests` caught the new
  region on the merge; it is exempted there for the same stated reason as `#reconnect-status`
  (it reports the transport, at the one moment the speech buffers' circuit is gone).
- **A successful rebind was silent.** It now says "OpenHelp is now Ctrl+Alt+Shift+Y."
- **The capture stayed armed, and browse mode ate keys.** With focus on the Rebind button, NVDA
  and JAWS stay in browse mode and swallow letters and arrows, and nothing disarmed the capture,
  so the first letter typed elsewhere (another Settings tab) became the binding. The capture
  now happens in a focused, read-only "New shortcut for X" field (an edit field puts a screen
  reader in focus mode), captures only keys aimed at it, and cancels when focus leaves it.
  Held-key auto-repeat is ignored. After a capture or Escape, focus returns to the row's Rebind
  button instead of falling to the page. The cancellation is no longer an interrupting message.
- **Deleting a condition dropped focus on the page.** Delete is a button inside the row it
  removes. Focus now lands on the next row, else the previous, else the parent group (or the
  "Add group" button when the root goes), that row becomes the tree's Tab stop, and "Deleted X."
  is said. `ConditionTreeDeleteFocusTests` (5).
- **Not changed, recorded:** after tabbing onto a row button in the Object Tree, NVDA usually
  drops to browse mode, where arrows move the reading cursor rather than the tree. That is the
  screen reader's model, not a defect here.

### Two keyboard dead ends, found by reading the JavaScript and proven in a browser (2026-09-24)

The first finds of the JS campaign. Each was reproduced in a real Chromium before any fix.

- **The Object Tree's row buttons could not be pressed from the keyboard.** Hide, Mute and
  Delete are tab stops inside each series row, and `treeKeyboard.js` took Enter and Space from
  anything inside a treeitem, cancelled them, and ran the row's action instead. Enter on "Hide
  Candles" selected the series and hid nothing. The condition tree's "+ leaf", "+ group", Delete
  and expand buttons had the same problem. Mouse users and screen readers in browse mode, which
  send a click, never hit it. A control inside a row now owns its Enter and Space, arrows still
  walk the tree from a button, and a field or select would keep its arrows too.
- **Settings → Keyboard could not capture a chord, and Escape rebound instead of
  cancelling.** The capture listened on `document`, behind `keyboard.js`'s window-level trap. For
  any Ctrl/Alt chord the trap stopped the event first, so the row sat on "waiting..." forever.
  Escape, which the panel says cancels, was dispatched as a shortcut (closing Settings) AND
  captured, so the command was bound to Escape. The trap now owns the capture: the next key is
  captured and runs nothing, Escape or Tab cancels ("Rebind cancelled. X keeps its shortcut."),
  and the Cancel button disarms the JS side (it used to leave the capture armed to swallow the
  next key pressed anywhere).
- **A key captured by rebind is spelled the way the trap dispatches it.** Both paths use one
  `normalizeKeyName`. The capture sent the raw `e.key`, so a command rebound to Space was stored
  as `" "` while the trap sends `SPACE`, and the binding never fired.
- **Tests**, each proven red against the old code: `KeyRebindCaptureBrowserTests` (a chord
  captured and then opening Help; Escape leaving Settings open with nothing rebound),
  `A_row_button_is_pressed_by_the_keyboard_not_redirected_to_the_row` (Enter and Space), and 13
  node tests across `keyboard-tests.mjs` and `tree-tests.mjs`. The node harnesses now honour
  `stopImmediatePropagation` and give the tree double a real `closest()`.

### The hosted terminal backs off when it reconnects (2026-09-24)

**The storm in the server's logs was the framework's default retry policy.** Read from
`blazor.web.js` (.NET 10.0.9): ten attempts with **no delay**, then ten at 5 s, then 30 s.
Ten zero-delay retries all land inside a restart window, which is the "bursts of ten-plus within
the same second" behind 100 of 141 negotiates returning 502 between 7 and 20 September.

- **Why the 2026-09-21 attempt never booted, now established.** It called `Blazor.start()` from
  an inline `<script>`, and the CSP is `script-src 'self'`. The harness's console capture shows
  "Refused to execute inline script because it violates the following Content Security Policy
  directive". With `autostart="false"` and the start call refused, nothing started the circuit.
- **Fix:** `autostart="false"` plus a same-origin `js/boot.js` that starts the circuit with the
  back-off. One immediate attempt (most drops are a blip), then 1, 2, 4, 8 s, capped at 15 s
  (nginx's own `Retry-After`), ±20% jitter so tabs do not retry in step, thirty attempts
  (about seven minutes) before the overlay offers "Retry now". The CSP is unchanged. The
  framework still retries at once when a hidden tab becomes visible.
- **Tests, each proven red by reintroducing the defect:**
  `ADroppedCircuitRetriesWithBackOff_NotABurst` drops the real circuit's WebSocket, refuses
  every negotiate, and counts attempts in 2.5 s. It measured **10** with the default policy, the
  same burst as the logs, and 2 to 3 with the back-off. It then lets the server answer and waits
  for "Reconnected", the first test where the framework, not the test, drives the overlay.
  `TheCircuitIsStartedByBootJs_AndNoScriptIsInline` pins the CSP hazard.
  `tools/jstests/boot-tests.mjs` (8, in CI) pins all thirty steps of the schedule, and
  `The_manifest_serves_boot_js` catches a build that drops the file (a missing boot.js is now a
  blank page).
- **Not verified behind nginx.** The harness runs Kestrel directly. The timings are the
  client's own, so the proxy should not change them, but the first real restart is the check.

### Alt+F ("fit price only") is retired; the two stylesheets are one again (2026-09-23)

**At Cody's decision, after he asked why anyone would want overlays to run off the screen.** The
switch existed to give the price line the whole pitch range when a wide band would compress it.
But an overlay off the pane is not silent: `ChartMath.NormalizedPosition` clamps it to the pitch
floor or ceiling, so it sounds like a band sitting still. A sighted user can see a band has
left the pane; this user would hear a band that seems to have stopped. And the switch was never
needed: a hidden component is already left out of the fit, so **hiding the band gives the price
line its whole range back** with no mode to remember. Alt+F is the File menu in nearly every
Windows program and is now deliberately unbound.

- Removed: `SystemCommand.ToggleScalePriceOnly`, its binding, dispatch case and spoken
  confirmation, `ToggleScalePriceOnlyAction`, `WorkspaceState`/`TabSnapshot.ScalePriceOnly`,
  `TabConfiguration.ScalePriceOnly`, the sandbox projection field, and the entry in the Help
  dialog, SHORTCUTS, QUICKSTART and USER_MANUAL. The auto-fit loop in `ViewportRangeCalculator`
  now always runs.
- **Saved files keep working.** Every `shortcuts.json` saved since 2.11 names the retired
  command, and an unknown name used to throw and drop the WHOLE profile back to defaults.
  `ShortcutManager.DropRetiredCommands` now removes only bindings to commands that no longer
  exist, for this retirement and any later one. Old workspaces carrying `"ScalePriceOnly"` load
  unchanged (Newtonsoft ignores an unknown member). Both pinned by tests; the loader's test is
  red with the filter removed.
- Tests rewritten to the new rule: hiding the band restores the candles-only axis, and does so
  immediately (the store's recompute gate sees the series-list change). The hit-tester fixture
  that leaned on the switch now pins its range directly.
- **The two `app.css` copies drifted and are reconciled.** The 2026-07-26 phone fix that lets the
  touch-nav button row wrap reached only the desktop copy; the web head, which is where phones
  visit, still overflowed. The web copy now carries it, marks its genuinely web-only rules (the
  speech chooser and the reconnect overlay) below a `WEB-ONLY BELOW THIS LINE` marker, and
  `SharedStylesheetParityTests` requires everything above the marker to equal the desktop file.
  It goes red on the original drift and names the line.

### The public demo opens on its chart, and a blank tab no longer blanks the toolbar (2026-09-22)

**Reported by the server agent (hosted notes §5p) and reproduced in a real browser before anything
was changed.** A first-time visitor to `/app/` got an empty Symbol box (options `BTCUSD` and
`ETHUSD`, value `""`) and "Select a market and symbol to begin". The Toolbar has always
auto-loaded the demo once a symbol is selected, so the question was what un-selected it.

- **A race, and not a demo-only one.** The store's first state is `ChartIdentity.Empty`: provider
  `"Bitstamp"`, symbol `""`. The toolbar-follows-the-chart subscription skipped an identity only
  when BOTH fields were empty, so every boot ran `SyncMarketToProviderAsync` on a background task,
  and that wrote `""` (and timeframe `1h`) over the symbol the Toolbar's own cascade had just
  chosen. The same sync ran when switching to a blank new tab. **An identity with no symbol names
  no chart now, and both the follower and the sync leave the toolbar alone for one.** On the
  desktop this showed as a blank Symbol box on a fresh install.
- **`DemoPolicy.DefaultMarket/Provider/Symbol` are read.** They had no caller. The demo had been
  on Crypto only because of the race above, whose sync set the market from Bitstamp's, so
  fixing the race alone would have opened the demo on AAPL (Stock is first in the list). The
  symbol is matched in the provider's own spelling (`BTCUSD` for `BTC/USD`) through
  `DemoPolicy.DefaultSymbolIn`, and only for the default provider, so switching market still lands
  on that market's first symbol.
- **Verified end to end:** a local `--demo` host driven by headless Chromium lands on
  "BTCUSD 1d on Bitstamp" with the chart drawn from live Bitstamp data, where the same probe
  against the unfixed build showed the empty box for 25 seconds.
- **Tests:** `DemoOpensOnItsChartTests` (5). All red or proven load-bearing by sabotage: the two
  identity guards are redundant for the boot race, so each one also has its own case (the blank
  tab switch for the sync, a blank identity moving the provider for the follower), and reverting
  either one alone turns its case red. The existing `A_blank_identity_does_not_wipe_the_dropdowns`
  had dispatched `new ChartIdentity()`, whose provider is also empty, and asserted before the
  background write could land. So it covered neither the identity the app starts with nor the
  write that did the damage.

### Two `CS8604` warnings in `WebHostBrowserCircuitHandler` (2026-09-22)

`OnConnectionDown/UpAsync` forwarded a maybe-null `circuit` to `CircuitHandler`'s base methods.
Checked for a live defect first: there was none. The `circuit?.Id` exists only because
`CircuitCoverageHandoffTests` pass `null!` (a `Circuit` cannot be constructed in a test), the
framework always passes a real one, and the base methods are no-ops. They now return
`Task.CompletedTask` directly.

### The segfault watch is closed as measured (2026-09-22)

Hosted notes §7g: 0 crashes, 0 dumps, 0 unscheduled restarts across 389.7 service-hours since the
`ChartRenderer.cs:82` fix, against ~5.4 expected unfixed (P ≈ 0.004). Recorded on
`docs/REPORT_CARD_2026-09-13.md`, with the server agent's caveat that `394a5c66` rewrote the
renderer on 2026-09-12.

## [2.12.0] — 2026-09-22

### The packaging fixes did not reach the packages — v2.12.0 re-cut (2026-09-22)

**This release was tagged, published, and then re-cut FOUR TIMES the same day — the payload gate refused the first three and nothing was published on any of them.** The first six assets were
read off the GitHub release from the VPS — central directory over an HTTP range request, no
downloading, no trusting the green run — and two of the release's headline claims were untrue of
every one of them. Write-up in `patches/2026-09-22-PACKAGING-AUDIT-AND-HEALTHZ.md` §0 and
`patches/HOSTED-DEPLOY-NOTES.md` §5t. **The tag and the zips were replaced; SHA256SUMS changed.**

**The defect class is the one this release was written about, recurring inside it.** 2.12.0 found
four runtime payloads staged into `$(OutDir)` and never into the publish, and fixed the MSBuild
correctly. But two of the four are third-party binaries that are not in the repository, every
item is `Exists()`-guarded so a build without them succeeds in silence, and the release is built
by GitHub Actions, which had neither. **The fix landed on one machine and not on the artifact.**

| published asset | manifest | ScriptWorker | NVDA DLL | Dot Pad SDK |
|---|---|---|---|---|
| `…-Windows-2.12.0-win-x64.zip` | yes | yes | **no** | **no** |
| `…-macOS-2.12.0-universal.zip` | **no** | n/a | n/a | n/a |
| `…-WebHost-2.12.0-*` (all four) | yes | **no** | n/a | n/a |

- **The NVDA controller client and the Dot Pad SDK are fetched by `release.yml`, pinned by
  SHA-256.** Both turned out to be publicly fetchable, which the audit had assumed the Dot Pad
  SDK was not: NV Access publish the controller client per release
  (`nvda_<ver>_controllerClient.zip`, LGPL 2.1), and Dot Inc publish the Windows SDK runtime in
  their own repository at `dotincorp/dotpad-sdk-guide` under `Windows/3.0.0` — **not**
  `Windows/dotpad-3.0.0`, which is what the local vendor directory is called and why the first
  URL tried returned 404. All seven Dot Pad files and all 419 LibLouis tables fetched from the
  vendor are byte-identical to the local copy the code was developed against.
- **18 MB of the SDK ships, not 296.** `ipadic/` — 187 MB of MeCab's **Japanese** dictionary,
  plus a 42 MB zipped copy — stays out, and the evidence is now stronger than the earlier guess.
  `WindowsDotPadNative` binds exports on `DotPadSDK-3.0.0.dll` only; that DLL has **no static
  dependency** on TTBEngine, MeCab, liblouis or jsoncpp (read from its PE import table) and
  `LoadLibrary`s them, reporting `DOT_ERROR_LIBLOUIS_DLL_COULD_NOT_LOAD` by name when it cannot;
  MeCab is reached only through `TTBEngine`'s `mecab_new2("-u ./ipadic/user.dic")`, **whose
  argument names a `user.dic` the vendor does not ship**, by a path relative to the working
  directory rather than the install. The other 2.5 MB (TTBEngine, Mecab, jsoncpp, mecabrc) does
  ship, as cheap insurance for a CJK path that cannot be traced from Linux. If a real Dot Pad
  ever fails to initialise with everything else present, `ipadic` is the first thing to add back.
- **THE VC++ RUNTIME SHIPS WITH IT, and nothing had noticed it was needed.**
  `DotPadSDK-3.0.0.dll`'s only non-system imports are `MSVCP140`, `VCRUNTIME140` and
  `VCRUNTIME140_1`. A .NET publish carries none of them — the published Windows zip has 637
  entries and not one is a VC++ runtime file — so on a machine without the redistributable the
  SDK does not load and tactile is silently inert: **the exact symptom shipping the SDK was
  meant to end.** Staged app-local from the runner's redist directory into `vendor/vcruntime/`.
  Every developer machine has the redistributable because Visual Studio installs it, which is
  why this could only ever have been found on a user's machine or in the import table.
- **`AccessibleTrader.WebHost.csproj` now references the ScriptWorker at all.** `grep -n
  ScriptWorker` found nothing in it, while `RoslynScriptingService.DefaultWorkerPathResolver`
  resolves the worker from `AppContext.BaseDirectory` on every head and Release refuses the
  in-process path by design. **The RID and `SelfContained` are forwarded to it**, and that is
  the difference between a worker that starts and one that does not: this head publishes
  self-contained, so a framework-dependent apphost beside it would resolve its runtime through
  hostfxr's *install* search, never the sibling private runtime, and fail on exactly the
  machines the head exists for. **Measured on linux-x64: the worker's self-contained publish is
  284 files / 115.1 MB, of which FIVE (0.15 MB) are not already in the host's publish
  byte-for-byte.** `CopyScriptWorkerOnPublish` copies only what the destination lacks —
  an `Exists()` filter rather than a five-name list, so a future unique dependency travels too.
  Verified by running the staged apphost: it prints `worker ready` and exits 0.
- **The plugin-trust manifest goes where the PLUGINS are.** The macOS `.app` shipped 33 plugin
  DLLs in `Contents/MonoBundle` and no manifest: the publish-time target ran and wrote it into
  `$(PublishDir)`, one directory *outside* the bundle the workflow zips, while
  `AppContext.BaseDirectory` on that head is MonoBundle. The rule is now stated once —
  `WriteBesidePlugins="true"` — in **`packaging/PluginTrustManifest.targets`**, imported by both
  heads. That file replaces the inline task and two targets that had been **copied** into each
  csproj, which is what produced both this bug and the one before it: the publish-time target
  existed on the WebHost for its whole life and was never applied to the desktop head.
  `release.yml` also prefers the `.app` under `publish/` (its `find | head -1` could return the
  shallower **build** output) and fails the macOS job if the bundle has no manifest.
- **`PublishReadyToRun` is set explicitly.** 2.11.0's Windows app was precompiled and 2.12.0's
  was not, with nothing in the repo asking either way — an implicit default moved. Confirmed by
  reading the CLI header's `ManagedNativeHeader` out of both published zips (2.11.0 non-empty,
  2.12.0 zero), which is also how the release now asserts it.
- **A release build with no vendored payload is an ERROR, not a message.**
  `WarnIfNvdaControllerMissing` and `WarnIfDotPadSdkMissing` were high-importance build messages
  printed into a green log nobody read. Under `ReleasePublish=true` they now fail the build: *a
  release that ships mute is worse than a release that fails.*
- **`scripts/verify_release_payloads.py` reads the finished zips and fails the release job before
  anything is published**, driven by `packaging/release-payloads.json`. **This is the check that
  had to exist**, and `PublishStagingParityTests` structurally could not be it: parsing the
  csproj as XML verifies the *rules*, and whether the file existed on the release runner is a
  different question no test reading this repository can answer. Demonstrated by reconstructing
  the six published v2.12.0 assets from their central directories as stub zips — it reproduces
  the table above exactly, row for row, and exits 1.
- **THE GATE CAUGHT A REAL BUG ON ITS FIRST RUN, AND THE BUG WAS IN THIS FIX.** The re-cut was
  attempted, the release job failed, and nothing was published — which is the whole design, so it
  is recorded rather than tidied away. Two findings, both from the artifact:
  - The macOS bundle STILL had no manifest. `WriteBesidePlugins` derived its destination
    directories from the file list that had already been **deduplicated by file name** across the
    tree. On Mac Catalyst the same plugin exists loose in `bin/<rid>/` *and* inside
    `.app/Contents/MonoBundle/`, so the `GroupBy` kept whichever the walk reached first and the
    bundle was never a destination at all — the original defect, reproduced one layer inside its
    own fix. Each directory holding plugins now gets a manifest hashing **its own bytes**, which
    also closes the worse failure the first draft could have caused: a manifest that is present
    and describes a different copy refuses every plugin beside it, and looks healthy from the
    build side. `PluginTrustManifestTaskTests` RUNS the real task through MSBuild over a
    duplicate-name tree and sabotage-reproduces exactly this.
  - The Windows zip was still IL-only despite `PublishReadyToRun=true` in the csproj under a
    condition that demonstrably held. **This is not fixed, and the attempt to fix it is the
    more useful record.** Passing it as a command-line GLOBAL property (which no imported
    `.targets` can overwrite) did not restore R2R either — and a global property flows into
    every referenced project, so the SDK inferred a RuntimeIdentifier for the ScriptWorker,
    moved its output to `bin/Release/net10.0/win-x64/`, and the staging wildcard carried the
    apphost into `publish/maui-win/win-x64/` instead of the root. **The third re-cut attempt
    failed on a MISSING ScriptWorker.exe — a real feature broken while chasing a startup
    optimisation.** Reverted. The R2R rule is now `"severity": "warn"`: it reports as an
    advisory and does not block, because every other rule in that manifest names a payload
    without which a feature is silently dead, and this one is about how fast the app starts. A
    corrected release must not be held hostage to it. Open item in `docs/TODO.md`.
  - **A third failure, from the SAME fix.** Writing a manifest into each per-RID bundle broke
    the universal build outright: `error : Unable to merge the file
    'Contents/MonoBundle/plugins_trusted.manifest', it's different between the input app
    bundles.` A Mac Catalyst universal publish LIPO-merges the two per-RID bundles and demands
    byte-identical non-binary files; two manifests never are, since they carry a timestamp and
    hash different builds. **The first guard for this failed too, on the fourth attempt, and the
    reason is worth keeping:** it asked "is `RuntimeIdentifier` set AND `RuntimeIdentifiers`
    set?", on the theory that an inner build has both — but the SDK drives `RuntimeIdentifiers`
    as a GLOBAL property in the inner build, so it is not visible there and the csproj's own
    assignment loses. *A condition that depends on a property another layer controls is a
    condition you cannot reason about from here.* The rule is now blunt and checkable: **a
    RID-specific build never writes a manifest inside a `.app`**. The outer universal build has
    no RID and does write; Windows and the WebHosts are RID-specific and have no bundles, so it
    costs them nothing — which is its own test case, because if that one ever goes red the rule
    has quietly become "do not write" for every head that is not macOS.
- **The payload list lives in ONE place.** `packaging/release-payloads.json` is read both by that
  script and by `PublishStagingParityTests`, whose payload theory is no longer its own InlineData
  list — because a list of required payloads kept in two places has the same shape as a build
  target kept in two places, which is the defect the whole file is about. New
  `ReleasePayloadManifestTests` covers the seam: that `release.yml` *supplies* every gitignored
  payload the csproj can only conditionally stage, that every fetch is checksum-pinned, and that
  the artifact check runs **before** the publish step. All four guards sabotage-tested.

### The NVDA-absent warning is gated on Windows (2026-09-22)

`BlazorSpeechManager` constructed a real `NvdaControllerClient` on every platform, and that class
probes by P/Invoking a Windows DLL and catching the failure. The manager is **scoped** — one per
Blazor circuit, so one per *visitor* on a hosted head — so accessibletrader.com logged, at every
visitor, an instruction to copy `nvdaControllerClient64.dll` next to
`AccessibleTrader.BlazorClient.exe`: advice about a Windows DLL and a Windows binary, given to a
Linux server. `JawsApiClient` three lines above had been gated on `OperatingSystem.IsWindows()`
all along. The warning is now suppressed for a `NullNvdaControllerClient` — *an inapplicable
channel is not a missing library* — tested against the TYPE rather than the OS so it stays
reachable from a test with a substituted client on any machine. The comment claiming the check
was "asked once and reported once … one line at startup" was never true and now says so.

### Volume axis labels no longer run off the canvas (2026-09-22)

On a TSLA daily the volume pane's gridline labels (`120000000`, `100000000`, …) ran to and past
the right edge of the canvas while the price pane's `440.00` stopped ~5px short. Visible in the
site screenshots before *and* after this release, so not a regression — just never looked for:
**every check in `AxisAndLegendCollisionTests` compared labels with each OTHER**, and nothing
compared one with the edge it is drawn at. Nine-digit volume is the common case for a US large
cap, not an outlier.

`ChartMath.FormatAxisValueCompact` gives the form a person says — `120M`, `1.5M`, `950K`,
`2.5B` — and `ChartRenderer.FitAxisLabel` uses it **only when the full number does not fit**, so
the decision follows the theme's axis width, the display density and the font rather than a
magnitude threshold, and a price axis is never touched. Not `Ellipsize`: a truncated number is a
*wrong* number, where an abbreviated one is the same quantity. Widening the strip was the other
option and the wrong one — the axis width is charged to every pane and comes straight out of the
plot area, which is the real estate this same release spent a whole scope reclaiming.
`NoYAxisLabelRunsPastTheRightEdgeOfTheCanvas` sabotage-tested: type reaches x=399 on a 400px
canvas without the fix, and the four-digit case passes either way.

Suite 8,034 → **8,080**; browser harness **226/226** (unchanged), 0 failing. New: `ReleasePayloadManifestTests` (9), `PluginTrustManifestTaskTests` (7 — they RUN the real task and the real target through MSBuild rather than grepping the targets file, which is what let the broken version pass),
`PublishStagingParityTests` 12 → 20 (the payload theory is now driven by
`packaging/release-payloads.json` and deduplicated — two rules resolved to one staged name and
xUnit was SILENTLY SKIPPING the duplicate test ID, which is the same failure mode in the guard
itself), `ChartAxisMathTests` +14, `AxisAndLegendCollisionTests` +3, `SpeechOutputPathTests` +2.

### The chart gets its window back — 41.5% of a maximised window to about 65% (2026-09-22)

Measured from a screenshot of a maximised 1280×781 window carrying Candles + Volume + RSI + MACD:
the terminal's own chrome took **394 CSS px** and the chart got **279** — 41.5% of the app.
TradingView's equivalent vertical chrome is about 70. The pane weighting could not show at all,
because three indicator panes at their 80px floor consumed the stack and the crowded rebalance
flattened every pane to an equal quarter. Scope in `docs/CHART_REAL_ESTATE_SCOPE.md`; every step
of it landed the same day, across four commits (`16e21add`, `6365de5d`, `a8b817c1`, `24d16179`).

- **A guard first.** `ChartClaimsItsShareOfTheWindowTests` measures the chart's share of the
  viewport at 1280×672 (the app's part of that window) with Volume, RSI and MACD added through
  the real dialog, and prints every band of the shell with its height when it fails. **It passed
  on the unchanged code at 55.4%**: the harness's seeded provider had one timeframe, so the
  composer and pills never rendered and the symbol row never wrapped. `TerminalServerFactory`
  seeds a weekly dataset as well now; the guard then read 44.9%, the number from the screenshot.
  A second case carries the restored window Cody launched into (1000×610) at a 50% bar.
- **Toolbar captions are off by default**, behind a new `Settings → Appearance → Toolbar
  captions` switch (`appearance.toolbarCaptions`, `IAppSettings.ShowToolbarCaptions`, previewed
  live, reverted by Cancel, a search-registry row). The accessible name was always `aria-label`
  and the tooltip `title`, so nothing spoken changes. `html.toolbar-captions` restores them, in
  BOTH `app.css` copies. The button box is 44px.
- **The two icon rows are one.** Pan/zoom and the four display toggles end the button row; the
  second row is the cascade and Load. Tab order: Help → pan/zoom → toggles → Market. The
  timeframe quick-picks come AFTER Load in a labelled group, so Tab goes Market → Provider →
  Symbol → Time → Load with no thirteen stops between Time and Load, and on a narrow window the
  pills wrap rather than the Load button. They are compact: thirteen were ~8px too wide to
  share the row at 1280.
- **Every band's vertical padding is 3px**; `.toolbar-group` has none.
- **The indicator-pane floor is 60 CSS px**, down from 80.
- **The crowded path keeps the weight.** When the floors did not fit it pinned the price pane at
  exactly 25% and scaled the indicators into the rest, discarding the 2:1 weight at the moment it
  mattered. Now the price pane takes 2 of (2 + count), floored at 15%. The divider test that
  pinned four equal quarters now pins the price pane as clearly the tallest.
- **Focus mode, Alt+Z** (`SystemCommand.ToggleFocusMode`, global). Toolbar, tab bar, indicator
  bar, touch nav and footer hidden via `.app-container.focus-mode`; the status line stays and
  gains an **Exit focus mode** button; a **Focus mode** button sits on the indicator bar. Both
  directions announced, naming the chord; focus is put on the chart on entry. Browser-tested at
  ≥85% of the window and back.
- **Formation labels.** Only the dominant formation labels its target and floor
  (`ChartFormationLayer.LabelsEverything(rank)`); the renderer measures the pane legend BEFORE the
  pane draws and hands it to the layer as `RenderContext.Avoid`, so labels step below it instead of
  being painted over. **"bull flag" twice was a detector defect**: `Flags` re-reported the same
  pole with a consolidation one bar longer; one pole reports one flag, and the test shows three
  without the dedupe.
- `ChartSurvivesASmallWindowTests`' overflow case assumed 946×536 was short enough to force a
  scroll; with 235px of chrome plus the 260px floor it now fits, so that case uses 400px.

Suite 8,026 → **8,034**; browser 222 → **226**. New: `ChartClaimsItsShareOfTheWindowTests` (4),
crowded-path and density cases in `PaneHeightAllocationTests`, `AFlagIsReportedOnce…`,
`ALabelUnderTheLegendStepsBelowIt`. **Unverified:** the Windows head's native canvas is
positioned from a page-reported rect that the shorter chrome and focus mode both move; it tracks
resize and a ResizeObserver and has not been looked at since.

### A "Zero" level saved three weeks ago was still flattening the price axis (2026-09-22)

Cody's chart kept coming back with its y-axis running `0.00 … 80000.00` and the candles crushed
into the top quarter. **Two wrong diagnoses before anyone read the file:** first the Bollinger
non-price components (real, fixed, and not this), then a stale binary (not that either). The
cause was in his saved workspace:

```
LEVEL on Candles: value=0.0  name='Zero'  visible=True
```

**The key that created it was fixed on 2026-09-06.** `ReferenceLevelPlacement` already places a
level at the CURSOR PRICE on a price pane, because a price pane has no meaningful constant, and
its own doc comment describes finding the bug from a screenshot of a BTC chart with exactly this
entry in `__last-session__.json`. **What was never done is clearing the ones already saved** — so
three weeks later the chart was still broken at every launch. *A fix to the writer does nothing
for the data the old writer produced, and nobody re-derives a level by hand.*

- **`MigrateSeriesConfig` drops zero and NaN levels from price panes on restore**, alongside the
  other healings it already performs. Scoped to price panes only: an oscillator's zero is
  meaningful — MACD crosses it, Cipher B swings about it.
- **And the axis is no longer wreckable by one**, because the magnitude clause added for
  components now guards levels too. A level at 0 on a 60,000–86,000 chart sits 2.31 spans below
  the low, inside the three-span allowance levels have always had; a support line at 52,000 is
  four-tenths of a span away and still admitted. **Two independent fixes for one defect is
  deliberate here**: the healing removes the artifact the user can see, and the guard means the
  next source of an implausible level — a drawing, an import, a provider — cannot flatten a chart
  before anyone notices.

Suite 8,021 → **8,026**. New: `StaleZeroLevelHealingTests` (3), plus two levels cases in
`PriceAutoFitScopeTests`.


### A non-price component dragged the price axis to zero — a regression from this file's own change (2026-09-22)

Caught by Cody from a screenshot before the tag, which is the only thing that could have caught
it: the suite was green and the chart was wrong.

**Bollinger Bands declares SEVEN components on the Main pane, and three of them are not prices**
— `PercentB` (0 to 1), `ZScore` (about ±3) and `Width` (a ratio). On BTC at 60,000–86,000 the
visible span is 26,000, so a value of `0.5` sits **2.31 spans** below the low — comfortably
inside the three-span allowance the reference levels use. The price axis was pulled down to
`0.00`, the candles were crushed into the top quarter of the pane, and because the pane's range
is also the PITCH range the price line lost most of its swing at the same time.

**It is a regression from 2026-09-21**, when Main-pane components were first allowed to expand
the price range at all. Before that these three were merely drawn flat along the bottom — ugly,
and audible as flat tones, which is why Cody had already muted them by hand.

- **The span rule cannot express what is wrong here**, because nothing is wrong with the
  distance. `IsPlausiblyTheSameQuantity` adds the clause that can: **a price is the same ORDER OF
  MAGNITUDE as other prices.** Measured against the data's midpoint, because that is what "how
  big are the numbers here" means — a span can be arbitrarily small on a quiet day without making
  faraway values any more plausible.
- **It only ever TIGHTENS.** Both tests must pass, so nothing the span rule already rejected
  becomes acceptable and no axis can widen because of it. A pane straddling zero has no
  meaningful magnitude and falls back to the span rule alone, so an oscillator that finds its way
  onto Main does not lose its own data.

**Left alone deliberately: those three components are still on the Main pane.** They are real
Bollinger outputs on a scale of their own, and the right home for them is their own strip rather
than the price pane — but Main-pane sub-panes get no range computed today, so that is a change
with its own failure modes and it does not belong in the hour before a tag. Muting them, which is
what Cody did unprompted, remains the correct workaround.

Suite 8,015 → **8,021**.


### The hosted terminal says when the connection drops, and something can finally ask if it is up (2026-09-22, seventy-second pass)

Both asked for by the server after a log review. Between 7 and 20 September **100 of 141**
requests to `/terminal/_blazor/negotiate` returned 502 — bursts of ten-plus within the same
second, from one browser sitting on an open page while the app restarted. From the user's side
that is silence: no speech, no message, nothing to act on.

- **The silence was literal.** There was **no `#components-reconnect-modal` anywhere in the
  repository**, so Blazor's default overlay applied — a plain `div`, no live region, no focus
  management — while `#blazor-error-ui` a few lines above it in the same file has carried
  `role="alert" aria-live="assertive"` all along. The accessible treatment was deliberate for
  unhandled errors and simply never extended to the case that actually happens.
- **The markup alone would not have fixed it**, which is why `js/reconnect.js` exists. A live
  region that is merely UNHIDDEN does not reliably re-announce, and the transitions that matter
  — attempt 1 → 2 → 3 → failed — carry no text change of their own at all. A MutationObserver
  watches the framework's own state classes and REWRITES a dedicated status node, so every
  transition is a genuine text change carrying a whole sentence.
- **The copy answers the question a trader is actually asking.** When the screen goes quiet the
  urgent thing is not the transport, it is whether an order was lost: *"orders already placed are
  held at the venue and are not affected."*
- **`/healthz`** — unauthenticated, mapped after `UsePathBase` and before auth so it lands at
  `/terminal/healthz` and `/app/healthz`. No DB call, no provider call, no auth. There was no
  health endpoint anywhere in the solution, so the systemd unit had nothing to gate readiness on
  and the restart window could not be closed.

**The reconnection back-off is NOT done, and the reason is in the code.** Configuring it needs
`autostart="false"` plus an explicit `Blazor.start()`; that was tried and **stopped the circuit
booting at all** — every browser test went red with "the terminal never loaded". The cause was
not established, and an unverified change to the boot path has no business shipping in the same
release as the fix for a page that would not load. A test now pins the decision so the next
person reads the reason instead of rediscovering the failure.

**An existing guard caught the new live region, and it was right to.**
`Only_the_two_speech_buffers_announce_app_speech` exists because a second announcer of the same
sentence makes a screen reader drop one — measured on the AT-SPI bus in the 9th pass. The
exemption is recorded with its justification rather than waved through: the reconnect status is
not a mirror of app speech, it reports the TRANSPORT, and it has to work at the one moment the
speech buffers cannot — because the circuit that renders them is the thing that just died.

Docs brought up to date across the board: `WHATSNEW.md` gains an `## Unreleased` section written
for users rather than maintainers (and deliberately carries **no** bare version number, because
the marketing site parses the first `## x.y.z` heading as `softwareVersion`); `USER_MANUAL.md`
and `QUICKSTART.md` explain why the desktop client speaks to NVDA and JAWS directly and point at
the Journal when it is silent; `PLATFORMS.md` documents the JAWS route; `TODO.md` opens a new
START HERE block.

Suite **8,015**; browser harness 216 → **222**. New: `ReconnectAndHealthBrowserTests` (6).


### JAWS, and it costs nothing to ship (2026-09-21, seventy-first pass)

**A whole category of user could not use the desktop head.** On that head the chart is a native
SkiaSharp canvas over the `BlazorWebView`, so a reader following focus onto the chart is not
reading the DOM and the ARIA live region announces to nobody. NVDA got a direct path out of that
earlier the same day; JAWS had none — so a JAWS user met exactly the silence the day began with,
permanently, with no workaround and nothing anywhere saying why.

- **`FreedomSci.JawsApi`**, the COM automation object the JAWS installer registers, late-bound by
  ProgID. **No vendored binary, no per-RID native asset, no build staging** — which is the point:
  every staging mechanism in this repository was found broken at least once today, and the
  cheapest payload is the one that does not exist. On a machine without JAWS the ProgID does not
  resolve and nothing changes.
- **`SpeechOutputStatus` gains `JawsDirect`**, so the journal's speech-path line now reports which
  of the two readers is carrying the words, and the mute report cannot fire while JAWS is talking.
- **Absence is never cached.** JAWS started after the terminal, or restarted after a crash, is
  found — the latch that kept the NVDA path dead for a whole session is not repeated. NVDA wins a
  tie; they are not normally both running, so the order is a tie-break rather than a preference,
  and it is pinned so two readers cannot talk over each other.

**One timestamp per reader, and the first cut got that wrong.** Both probes shared a single
throttle stamp, so asking about JAWS reset the clock deciding whether to re-ask about NVDA: a
re-probe forced after NVDA threw was silently consumed by the JAWS check on the next line, and
the stale answer survived. The NVDA test written that afternoon caught it. **Two callers sharing
one piece of throttle state is not a throttle, it is a race.**

**And a sabotage survived because a test had disabled the thing it was testing.** The JAWS
re-probe test ran with a zero probe interval, so every call re-probed anyway and deleting the
invalidation changed nothing observable. It runs with a real interval now. *A throttle has to be
ON for "this forced a re-probe" to mean anything.*

Suite 8,007 → **8,015**. New: `JawsSpeechPathTests` (8).


### The Dot Pad SDK and the script worker reach a release, and the price pane stops sharing evenly (2026-09-21, seventieth pass)

**Two features that have never existed in a released build.** Demonstrated by unzipping the CI
artifact rather than reasoned: none of `DotPadSDK-3.0.0.dll`, `TTBEngine.dll`, `Mecab.dll`,
`jsoncpp.dll`, `liblouis.dll`, `mecabrc` or `tables/` was in it, and neither was
`AccessibleTrader.ScriptWorker.exe`. Same `$(OutDir)`-only staging as the NVDA client and the
plugin manifest — the fourth and fifth instances in a day.

- **Tactile support has been off in every release.** `WindowsDotPadNative` logs *"NativeLibrary.
  TryLoad failed … Tactile DISABLED"* and falls back to `NullDotPadNative`. **The Braille tab
  renders perfectly regardless** — it is ordinary DOM — so the feature has looked present and been
  inert, which is harder to notice than a feature that is plainly missing.
- **And no user-compiled indicator or strategy could run at all.**
  `RoslynScriptingService.DefaultWorkerPathResolver` launches the worker from beside the host
  binary, and Release refuses the in-process path by design, so with the worker absent the
  scripting surface was dead end to end.
- **`ipadic/` is deliberately excluded.** It is the MeCab **Japanese** dictionary and **187MB of
  the SDK's 204MB** staged footprint; without it the payload is ~18MB. Whether the SDK loads it at
  init regardless of language needs a Windows box and a device to answer, and 187MB in every
  release zip is not a cost to pay on a guess. If a Dot Pad ever fails to initialise with
  everything else present, that is the first thing to add back.

**The price pane stops sharing the window evenly.** The split was
`total / (1 + indicatorCount)` — "each pane gets the same vertical space" — so a chart carrying
Volume and nothing else gave the candles half the window and the volume bars the other half.
Measured on the real screenshot: price 171px, volume 171px. `DefaultMainPaneWeight = 2`.

- **2, not 3, and the floors are the reason.** At weight 3 a lone Volume pane lands on 86px
  against its own 80px minimum. At 2 the price pane takes two thirds with one indicator and a half
  with two, and **no indicator pane is pushed below its floor**.
- **From FOUR indicator panes on the weight is not consulted at all** — the 80px indicator floor
  and the 25% main floor have already decided the layout. The four-indicator chart looks exactly
  as it did. Both facts are tests, not claims.
- **The allocation is a pure function now** (`ChartRenderer.AllocatePaneHeights`), extracted so it
  could be tested: it carried two bug-fix comments describing a pane drawn under the x-axis strip
  and a pane pushed off the canvas, and **zero tests** — defects nothing but a screenshot could
  catch.

**Two of my own sabotages survived first time, and the guard was the problem.** The staging test
asked whether a payload's name appeared within 400 characters of the string
`CopyToPublishDirectory`; deleting the real one still passed, because the name also occurs in the
OutDir target, the warning text and the comments, and one of those sat near a different item's
metadata. It parses the csproj as XML now. *A guard over a file format should read the format.*
It also covers every SDK companion file, because staging the entry DLL alone leaves it just as
unable to load.

Suite 7,981 → **8,007**. New: `PaneHeightAllocationTests` (19); `PublishStagingParityTests` 6 → 12.


### A chart terminal with no chart: the toolbar ate the window (2026-09-21, sixty-ninth pass)

From a screenshot off the Windows VM. At 946x536 the terminal showed its toolbar, symbol row,
timeframe pills, tab bar, indicator bar, status line and footer — and a **thirty-pixel strip of
flat background where the chart should be, with nothing in it.** The title bar read
`BTCUSDT 1d on Bitstamp` and the data was loaded. Maximising brought the chart straight back,
which is what identified it as layout rather than rendering.

- **`.app-container` was a `height: 100vh` flex column and `<main>` was `flex: 1` with no
  floor.** `flex: 1` means "take what is LEFT", and **the chrome above the chart GROWS as the
  window narrows** — the icon toolbar wraps to a second row, the timeframe pills to a third — so
  what is left is not predictable from the window height at all. With no minimum, the flex column
  resolved its overflow by crushing its one flexible child, which is the chart. `<main>` now
  carries `--chart-min-height` (260px, one declared number rather than a guess per breakpoint)
  and the container is a scrollport.
- **`height`, not `min-height`, and the browser test caught the difference.** The first attempt
  used `min-height: 100vh`, which lets the container GROW past the window — and `html`/`body` are
  `overflow: hidden` here by design, so the tail was simply clipped: indicator bar, status line
  and footer unreachable. That is the same defect wearing different clothes, and the test written
  for the original bug failed on it. A fixed-height container with `overflow-y: auto` is a
  scrollport; the floor pushes content past it and it scrolls. Measured in the real browser, not
  reasoned.
- **`canvasRegion.js` now listens for `scroll`, and it had to land BEFORE the floor.** The rect
  the desktop head positions its native Skia canvas from is a VIEWPORT rect, so it moves whenever
  anything between the chart and the viewport scrolls — not only on resize. It listened to
  `resize` and a `ResizeObserver` and nothing else, which was invisible while nothing could
  scroll. Introducing the floor made scrolling reachable; without the listener the chart pixels on
  Windows would have stayed put while the interaction zone slid out from under them. **A latent
  bug that only becomes reachable because of your own fix is still your fix's bug.**

**Why the suite could never have seen this.** `TerminalBrowserFixture` opens every page at
1400x950 — **including `ChartScreenshotProbe`, the pass that photographed nine chart states
specifically to find what the tests could not.** Every picture this project has ever taken of
itself was taken at a comfortable size. *A harness with one viewport is a harness that has never
met a small window.* `ChartSurvivesASmallWindowTests` drives three ordinary small windows and
asserts the floor, that the overflow is reachable, that the chart is actually PAINTED and not
merely allotted space, and that the scroll listener exists. All three parts of the fix proved red
by sabotage.

Suite 7,981; browser harness 209 → **216**.


### Four build warnings, and one of them was hiding a silent speech defect (2026-09-21, sixty-eighth pass)

Read off the first successful Windows CI build. Three were named from the run's summary; the
fourth was a security advisory nobody had mentioned, and the one that looked most cosmetic turned
out to be a live defect.

- **`NU1903`, high severity — and it is the same "fixed in one place" shape again.**
  `CVE-2025-6965`, a memory-corruption bug in SQLite before 3.50.2. The desktop native package was
  pinned to 3.50.3 some time ago, with a comment; **the per-platform `.android` and `.ios` builds
  are SEPARATE packages that the pin does not reach**, and both stayed on the flagged 2.1.11. Now
  pinned to 2.1.13, the newest published build of each — there is no 3.x for them — and **2.1.13
  demonstrably clears the advisory**: restoring it under `NuGetAudit` produces no `NU1903` where
  2.1.11 produces one per package. Measured, not assumed. Worth stating for anyone weighing it:
  neither android nor ios is shipped, so this was a real advisory against code that reaches no
  user, which is exactly the kind of thing each person decides is somebody else's.

- **`CS0618` — and this one was NOT cosmetic.** `PropertiesModal.razor` parsed the chart's
  timeframe with the obsolete `Sdk.Configuration.TimeframeUtility`, whose `ToSeconds` is a fixed
  **whitelist returning `-1`** for anything not on it. The call site reads
  `barSeconds > 0 ? bar-range : spell-out-both-ends`, so **every timeframe outside that list — 2m,
  10m, 45m, 8h, 2d, 2w and every custom one a venue offers — silently lost the anchored-VWAP
  bar-range reading**, and the `try/catch` around it was guarding an exception the old parser never
  throws. Switched to `Sdk.Models.TimeframeUtility`, which parses `<N><unit>` by regex.
  **`TierBRegressionTests` has pinned this exact difference for months** — one of its own rows
  carries the note *"8h is in the Models regex parser but NOT the legacy switch"*. The fact was
  written down, tested and true, and a call site went on using the legacy parser anyway.
  **Knowing the difference is not the same as enforcing it**, so
  `ObsoleteTimeframeParserTests` now refuses any production use of it and demonstrates the six
  ordinary timeframes where the two answers disagree.

- **`CS8600` in the Coinbase order-REJECTION path.** `string reason = err?["message"]?.ToString();`
  declared non-nullable and assigned a possibly-null value. Nothing behaved wrongly — the next
  lines null-check it — but this is the same code that announced an insufficient-funds rejection
  as a placed order until 2026-09-07, and a suppressed nullability warning in a live-money path is
  one that will be ignored next to a line where it matters.

- **Node 20 deprecation.** Every workflow was on `actions/checkout@v4`, `setup-dotnet@v4`,
  `upload-artifact@v4` — all Node 20, forced onto Node 24 by the runner since 2025-09-19. Bumped
  to v5, **deliberately not to the newest major of each** (checkout is on v7, download-artifact on
  v8): v5 is the smallest step that clears the deprecation, and a larger jump is a behaviour change
  that cannot be tested here except by breaking something. **`softprops/action-gh-release` stays on
  v2 and is flagged rather than bumped** — it runs only when a real release is cut, so a fault in
  it surfaces at the worst possible moment.

Suite 7,974 → **7,981**. New: `ObsoleteTimeframeParserTests` (7).


### Three releases of the desktop head shipped 33 plugins and no manifest, and the fix was already in the other head (2026-09-21, sixty-seventh pass)

**Speech on the desktop head is fixed and CONFIRMED BY EAR** — the first time this head has ever
spoken with a screen reader in a released build. The cause was not staging, in the end, but a
NAME: **NV Access renamed the file.** Current `controllerClient` downloads ship
`x64/nvdaControllerClient.dll`; this code has always P/Invoked `nvdaControllerClient64.dll`, the
historical name — and the name on the copy hand-dropped into a `bin/Debug` folder in March, which
is exactly why that one machine worked and nothing else ever did. Cody placed the current file
beside the executable as instructed and the terminal stayed silent: it was there, and nothing was
looking for that name. A `DllImportResolver` accepts both now, so neither the user holding the old
file nor the one following NV Access's current download has to know any of this.

**And the market dropdown, demonstrated rather than reasoned.** Unzipping the artifact the
diagnostic build produced: **33 plugin DLLs present, `plugins_trusted.manifest` absent.** With
`RequireTrusted` enforcing an empty allow-list that refuses all 33, so the head behaves exactly as
though no plugins were installed — a symptom nobody reads as "a manifest is missing".

- **The cause is the same one, a third time in one day.** `GeneratePluginTrustManifest` runs
  `AfterTargets="Build"` into `$(OutDir)`, and a publish does not carry a loose file a custom
  target dropped there. Same shape as the NVDA DLL, and as the `AfterTargets="Build"` copy this
  pass's own first fix used.
- **The fix already existed, in the WebHost, with a comment explaining precisely this.**
  `GeneratePluginTrustManifestOnPublish` has been in `AccessibleTrader.WebHost.csproj` since its
  own publish shipped without a manifest. It was never applied to the desktop head. **The heads
  have separate project files, so a fix landed in one of them is not a fix — it is a fix in one
  place**, and that is what `PublishStagingParityTests` now enforces across every head: a
  build-time manifest with no publish-time counterpart is a test failure, and the publish-time one
  must hash `$(PublishDir)` so the digests describe the bytes that actually ship.
- **Two known exceptions are PINNED rather than left to be rediscovered:**
  `CopyDotPadSdkWindows` and `CopyScriptWorker` still stage into `$(OutDir)` only. Neither is
  demonstrated broken — observing it needs the vendor SDK and a Windows publish — so they are
  recorded as OPEN. The Dot Pad one matters: it is the device this application exists for.
- **CI verifies both payloads now**, rather than trusting them: the diagnostic build fails if the
  NVDA client or the manifest does not reach the publish output, and fails if any published plugin
  DLL's digest is missing from the manifest.

**Two mistakes of mine in this pass, both worth recording.** A csproj comment contained a literal
`--output`, which is illegal inside an XML comment, so the project would not load and the first
diagnostic build failed outright — I had edited a project file I cannot build on this machine and
had not validated it. And the workflow looked for the controller client among
`nvaccess/nvda`'s GitHub release assets, which carry only the installer; it lives on NV Access's
own download server. That step warned and continued, which would have produced a build that looked
fine and was mute. **It throws now** — the same "silent failure" shape this whole pass is about,
authored into the fix for it.

Suite 7,969 → **7,974**. New: `PublishStagingParityTests` (5).


### The desktop head had no log at all in Release, which is why it stayed unmeasured (2026-09-21, sixty-sixth pass)

Cody dropped the NVDA Controller Client beside the executable and the terminal was **still**
silent. That refuted the first diagnosis — the DLL genuinely was staged by nothing, and fixing it
was right, but it was not the cause — and sent the search back to the thing that made the whole
head undiagnosable.

- **`MauiProgram` registered its logging providers inside `#if DEBUG`.** A RELEASE build of the
  desktop head therefore had **zero** logging providers: every `LogError` and `LogWarning` in the
  entire application went nowhere. The 2026-08-24 audit filed *"MAUI Release has no logging
  providers"* as a finding and it has sat in the backlog since; **what nobody followed through was
  what it COSTS.** `AppStartupService.InitializeAsync` is launched through `SafeFireAndForget`,
  whose entire contract is *catch it and log it* — so on this head that contract reduces to
  *catch it*. A startup that throws produces an application that is half-built and says nothing.
- **And the ORDER of the startup body is what makes a partial failure look like two bugs.** It
  resolves in dependency order: the provider list is step 1, the accessibility coordinator — the
  thing that makes the terminal speak at all — is step 4. Fail in between and you get an
  application that lists providers and never says a word. **That is exactly the pair of symptoms
  reported from the VM**, and reading them as two faults is what sent the first pass after the
  speech path alone.
- **`RollingFileLoggerProvider`**, registered in every configuration, writing to
  `<LocalAppData>/AccessibleTrader/logs/terminal.log`. Hand-rolled and dependency-free on purpose:
  it has to work in the one configuration nobody can attach a debugger to, and **a logger that can
  break the thing it is watching is worse than none** — an unwritable path disables it for the
  session rather than throwing on every log statement in the application.
- **`IAppStartupService.StartupFault` makes a half-failed startup a value anyone can ASK for**,
  and the failure now reports through two channels chosen for a user who cannot see the screen and
  whose terminal may have just lost its ability to speak: the **journal**, which is ordinary DOM a
  screen reader can read at leisure, and the **Error feedback channel**, which is
  `SpeechChannel.Critical` and cannot be muted. It still rethrows — this adds channels, it does
  not replace the one that exists, and swallowing here would trade one silence for another.

**The shape, and it is the second time in two passes:** the WebHost has had console logging all
along, which is precisely why it is the head that gets diagnosed. A head with no output channel
does not report fewer bugs; it reports none, and the absence reads as health.

**The journal settled it, and refuted the startup theory.** Asked to open it on the silent
machine, Cody reported it full — every line present. So speech was being GENERATED and none of it
DELIVERED, which means the accessibility coordinator came up fine and the failure is purely in the
output path. The startup reporting above stays: it is right on its own terms and the logging gap
was real, but it was not this bug.

**So the journal now says which way speech is leaving**, because it is the one channel
demonstrated to reach a user whose speech is broken. On the first utterance, and again whenever it
changes, it records the path and the three booleans that determine it — client library present,
reader running, live region attached. All three rather than the conclusion, because each
combination is a different problem with a different fix: a reader running with no library is a
staging problem, a library with no reader is NVDA not started, and a live region attached while
the chart has focus is the case that LOOKS like working speech and is not.

**A `diagnostic-build` workflow**, dispatchable by hand, builds the MAUI Windows head on a Windows
runner and uploads it as an artifact — no tag, no release. The head cannot be built anywhere but
Windows, so a developer on Linux previously had no way to produce a testable binary at all, which
is a large part of why it went unmeasured for the life of the project. It fetches the NVDA
Controller Client from the latest NVDA release rather than vendoring it, and **fails the build if
the DLL does not reach the publish output** — the exact defect found earlier today, now guarded in
CI rather than trusted.

Suite 7,956 → **7,969**. New: `StartupFailureIsReachableTests` (5), `RollingFileLoggerProviderTests` (4),
`SpeechPathIsReportedTests` (4).


### The desktop head was put in front of a screen reader for the first time, and it was mute (2026-09-21, sixty-fifth pass)

The oldest unaddressed item on the report card — *"the MAUI head has never been measured with a
screen reader"* — was measured, on a Windows VM, and produced three findings in the first minutes:
focusing the chart said nothing, F2 said nothing, and the market dropdown offered only the
built-in providers. The same build served over the web through NVDA was fine.

**Two roots, and they are the same shape: a head that fails loudly into a log with no providers,
and silently to the user.**

- **`nvdaControllerClient64.dll` was staged by NOTHING.** Not tracked by git, not named by any
  csproj, absent from every publish. One copy had been dropped by hand into a `bin/Debug` folder
  on 2026-03-03, so exactly one machine could speak and every fresh build and every published
  install was silent. **And this head cannot fall back the way the WebHost can:** the chart is a
  native SkiaSharp canvas sitting ON TOP of the `BlazorWebView`, so a reader following focus onto
  the chart is not reading the web view's DOM and the ARIA live region inside it announces to
  nobody. On the WebHost the whole surface IS the DOM, which is exactly why the web build was
  unaffected. Staged now by `CopyNvdaControllerWindows`, warned about by
  `WarnIfNvdaControllerMissing`, vendored to `vendor/nvda/` and documented in `docs/PLATFORMS.md`
  — the same pattern the Dot Pad SDK has had all along.
- **A second bug outlived the first, and would have survived the DLL arriving.** NVDA
  availability was decided ONCE, in the constructor, and latched — on a head where the speech
  manager is a SINGLETON, so for the life of the app. A user who started NVDA after the terminal,
  or restarted it after a crash, was on the fallback path for ever with nothing said. The
  constructor's own comment promised "Immediate check, then background monitor" and there was no
  monitor. The two facts are now separate, because they are different KINDS of fact: whether the
  client library loads is about the INSTALL and cannot change; whether a reader is running is
  about the USER and changes constantly. The second is re-probed on a throttle.
- **The terminal now says when it cannot speak.** `SpeechOutputStatus` is `NvdaDirect` /
  `LiveRegion` / `Mute` — a value anyone can ask for, where the old `SpeechMode` string was read
  by nothing in the application, so "mute" was a state the user could only infer from silence. A
  `Mute` terminal writes one error into the journal naming the missing file, and every sentence it
  could not say is still journalled, so nothing is lost.
- **Every plugin being refused is now reported as its own fact.** The empty market dropdown is
  what an empty trusted allow-list looks like from outside: `plugins_trusted.manifest` is
  generated against the build output, so an install whose manifest did not travel with it — or
  whose DLLs were rebuilt after it — refuses the whole fleet. **The refusal is correct and
  deliberate**; what was wrong is that its only account was one warning per DLL. A per-DLL warning
  cannot carry this, because the interesting quantity is the RATIO and no iteration of the loop
  knows it.
- **The P/Invoke is now a PARAMETER** (`INvdaControllerClient`), which is the only reason any of
  the above has tests: it was three `DllImport`s and a bool in a constructor, so none of it could
  be exercised without a Windows box, so none of it ever was. Same lesson as the notifier seam in
  the background monitor.

**Two of the four sabotages against the new tests SURVIVED on the first run, and both were
defects in the tests.** One helper reached into the private probe stamp to skip the throttle — and
that stamp is the very field the old latch keyed on, so restoring the latch left the helper
re-enabling the probe and the test green. *A test that reaches into an implementation detail ends
up agreeing with any implementation that shares it;* the interval is a constructor parameter now.
The other let the reader come back immediately after a mid-session throw, so "re-probed and found
it" and "never re-probed and assumed it" produced identical observations. Both go red now.

**Still open on this head, and not fixed here:** Cody must place the DLL and rebuild before the
desktop client speaks — this pass makes the build and the app say so, it cannot supply a
third-party binary. The dropdown needs its manifest checked on the installed copy. Neither is
verified on Windows; nothing in this pass can be, from Linux.

**A follow-up the same day, and it is the same defect one layer up.** The first cut staged the
DLL with an `AfterTargets="Build"` copy into `$(OutDir)`, mirroring `CopyDotPadSdkWindows`
directly above it. **That reaches a `dotnet build` and not a `dotnet publish`** — `$(PublishDir)`
is a separate directory and a loose file dropped into the build output by a custom target is not
a publish item. The release zip is `dotnet publish --output publish/maui-win` zipped flat, so the
fix would have worked on a developer machine and shipped nothing, which is exactly what the
hand-dropped DLL had been doing for seven months. It is a content item with
`CopyToPublishDirectory` now. Found because Cody unzipped a release and asked where `vendor/`
was — there is no `vendor/` in a zip, and there should not be.

**Flagged, NOT fixed, and unverified:** every other native-staging target in that csproj has the
same shape — `CopyDotPadSdkWindows` and `CopyScriptWorker` both copy to `$(OutDir)` only, and
nothing in the project gives their files `CopyToPublishDirectory`. If that reasoning holds, the
Dot Pad SDK has never reached a released Windows build, only a local `dotnet build` — which
would sit squarely on top of the report card's standing note that the Braille tab has never
rendered on the head the Dot Pad attaches to. Not demonstrated: it needs the vendor SDK present
and a Windows publish to observe, and neither is available from here.

Suite 7,942 → **7,956**. New: `SpeechOutputPathTests` (12), `PluginTrustRefusalReportingTests` (2).


### The price pane fits what is drawn on it, and the pitch band is laid out the way an ear reads it (2026-09-21, sixty-fourth pass)

Two changes that are one change, because on this terminal the price pane's range is also the
price pane's PITCH range.

- **Auto-fit sees Main-pane overlays now, and there is a switch (Alt+F).** Until today the price
  pane was sized from the OHLC buffer, visible reference levels and declared bounds, and nothing
  else — every indicator pane had expanded to fit its components since the beginning, and the
  price pane alone never did. A Bollinger band, a Keltner or Donchian channel, an MA cloud or a
  Chandelier Exit stop that swung wider than the candles was drawn OUTSIDE the pane. **Clipping
  here is not only a visual defect:** `ChartMath.NormalizedPosition` normalises across exactly
  this range and the sonification turns the result into a frequency, so off the pane is also
  SILENT — and a band that has gone quiet sounds exactly like a band sitting still.
  `WorkspaceState.ScalePriceOnly` (Alt+F, "fitting price only") is TradingView's "Scale price
  chart only" and restores the old behaviour deliberately. **Default OFF — include — because the
  other failure is the silent one**, and a user cannot investigate something they were never
  told about.
- **Three conditions on what may move the axis, each of them "what the eye is shown":** the
  series and the component must be VISIBLE (hiding a band must not leave its footprint behind);
  a sub-pane strip is its own axis; and the value must plausibly be a PRICE, by the same
  span-multiple guard the reference levels already use — a series whose `Pane` is unset falls
  back to "Main", and `LoukasCyclesProvider`'s day-counts on a BTC chart is the worked example of
  what that costs. The guard is measured against the ORIGINAL price span, not the running one, so
  a ladder of ever-wider components cannot walk the axis out one plausible step at a time. All
  five were proved red by sabotage, individually.
- **`ScalePriceOnly` is in `WorkspaceStore`'s recompute gate and `IsLogScale` is not, and the
  difference is the distinction between the two toggles.** Log scale changes how the range is
  MAPPED; this changes what the range IS. A toggle that moves the numbers and is missing from
  that gate is silent until the next tick happens to recompute — which on a closed market is
  never.

- **The pitch band is now linear in PITCH rather than in hertz, which is the second half of the
  log-scale fix that landed on 2026-09-18.** That first half gave the eye and the ear one
  normaliser. It did not touch the step after — turning a fraction of the pane into a frequency —
  which was `200 + n × 800`. Pitch perception is logarithmic in frequency, so a hertz-linear ramp
  is not a straight line to the ear: **the bottom quarter of every pane carried a whole octave
  (200→400 Hz) and the top quarter carried a major third (800→1000)**, so the same gesture up the
  pane sounded five times larger at the bottom than at the top, and a price riding high in the
  window moved a long way on screen and barely at all in the ear.
  `AudioConstants.PitchForPosition` maps the fraction exponentially instead. **The band is
  unchanged at 200–1000 Hz — the floor and the ceiling sound exactly as they always have and only
  the interior is redistributed** — and the midpoint of a pane now sounds at 447 Hz, the band's
  geometric mean, where it used to sound at 600.
- **This had to land WITH the auto-fit change rather than after it.** Including overlays widens
  the range and so compresses the price line into a smaller share of the pane. Under the new
  mapping that is a constant transposition — a third of the pane is a third of the octaves,
  wherever in the pane it sits — so the cost of Alt+F is one a user can predict. Under the old
  ramp the identical compression cost two and three-quarter times as much pitch at the top of the
  pane as at the bottom, a penalty nobody could have reasoned about.

**NOT HEARD.** Both behaviours change what a saved chart sounds like without the user touching a
setting.

Suite 7,910 → **7,942**. New: `PriceAutoFitScopeTests` (11), `PerceptualPitchMappingTests` (20).
`LogScaleSonificationTests`' six pinned frequencies were rewritten in terms of the geometric
mean rather than re-observed. Four existing guards went red on the change and were the reason
each loose end got tied: `ShortcutHelpParityTests` (twice — Alt+F undocumented in the help
dialog and in `SHORTCUTS.md`), `Phase5KeyboardScopeTests` (the new command uncategorised),
`WorkspaceProjectionTests` (the new state field said nothing to the script sandbox).

## [2.11.0] — 2026-09-21

### A2j — the layer that decides what a blind user HEARS gets measured, and 82.0% is the best rate yet (2026-09-19, sixty-second pass)

`Core/Services/Accessibility` is 15,898 lines in 49 files — the size of Indicators, which
produced the 10.5% cliff — and across eight prior mutant campaigns and the browser audit, mutants
had reached exactly SIX of its files: the three tiny formatters, `EarconService`,
`DotpadTactileDriver` and `AccessibilityFeedbackCoordinator`. The seven largest — the drawing
interaction manager, `SpeechFormatter`, `NarrationScanner`, `TactileCanvasCoordinator`,
`NavigationFeedbackManager`, the anchor-nudge partial and `PlaybackNarration` — had none, ever.

- **50 mutants, 41 caught: 82.0%.** Rates now read 73.1 / 72.0 / 69.2 / 62.2 / 10.5 / 73.5 /
  **82.0** across seven disjoint file sets. Baseline green before (7,880), control green after
  (7,880), every file restored byte-identical. The false-catch audit is clean: every catch is by
  a test that NAMES the behaviour — no bookkeeping guards firing as proxies, the A2h pathology.
- **Mutants were chosen as sentences a user would file a bug about**, and many are restorations
  of defects this repo has already fixed once: the Heikin-Ashi close line, the hidden-and-muted
  lattice, the gold dot dropped by scan order, support and resistance decided by a tone
  frequency, one NaN bin stripping every label off a profile. Most of those were caught by the
  very test written when the defect was first fixed, which is the answer to "is the fix guarded,
  or merely made?" for that half of the layer.

**What the nine survivors have in common: they are not about WHAT is said but about WHEN, and by
WHICH KEY.** Everything this layer says was well guarded. Three of the gates deciding whether it
may speak at all had no test on the line itself, and three more sat on routes with no test file.

- **The narration scan window — three gates, none guarded** (`NarrationScanWindowTests`, new).
  The seed floor (`Math.Max(seedCount, …)`) turned into a `Math.Min` and nothing went red, so
  pressing N could start reciting history. The bar-close gate (`if (!isBarClose) return;`) deleted
  and nothing went red, so every confirmed-bar clause could fire on an unconfirmed tick — a wick
  poking through an EMA announcing a cross that then un-happens; `NarrationCausalityTests` covers
  the forward-looking rules and never this line. The first-sighting rule inverted and nothing went
  red, so pressing N could announce a cross for every overlay at the moment the switch is flipped.
- **Why the existing seeding tests could not see two of them, and it is the same shape twice: a
  SECOND memory covering the first.** The seed records a last-pivot index per marker and the side
  price is on per overlay, so any component that had already printed, or any overlay that already
  had a value, is protected by that record whatever the window says. The reachable case for both
  is WARMUP — a pivot indicator that is NaN everywhere, an EMA in its first N bars — which is
  ordinary, and which no fixture in the suite had.
- **`BinnedNavigationStrategy` had no tests at all** (`BinnedNavigationStrategyTests`, new). Up
  and Down inverted inside a volume profile — on the one series where the Y axis IS the navigation
  axis — and the suite stayed green; so did disabling the refusal that stops left and right moving
  a profile that has no time axis. The new assertions are written in PRICE, not in bin index: the
  index is `ProfileService`'s business, "Up reads a higher price" is the user's.
- **A fixture too comfortable to separate rounding from truncation.** The two `CandleColor` tests
  use phase values 5.0 and 42.0, both whole, so `(int)Math.Round(v)` and `(int)v` agree. A
  fractional phase — 4.6 is Neutral, 2.6 is Caution — now pins it. This is the A2i shape again.
- **"Price on it" was being tested where the two numbers are equal to the last bit.** The rule is
  that a drawing's relation is decided on the SPOKEN price: a line at 150.4999 under a close of
  150.5001 reads as 150.50 either way, and calling that "price above" — or worse, "price crossed
  above" — is a claim made out of rounding noise. Three cases now pin the precision rule and the
  cross suppression that rides on it.
- **The Bollinger volatility clause of the detail key had no test**, so both thresholds could
  invert together and announce a squeeze as an expansion and an expansion as a squeeze. A squeeze
  is the setup the pattern is traded FOR.
- **The price line's VIRTUAL components were the unguarded half of the Heikin-Ashi fix.** Four
  tests pin that the close line reads the raw close with HA on, and all four pass whatever
  `readsRawBar` says — because a series carrying a mapped array is answered by the array, which is
  always raw, before the bar is consulted. The branch that flag actually governs is the fallback
  for a price series with no array, which is the production shape it was written for.

**All nine survivors closed and proved red by re-applying their mutants** (9/9,
`scratchpad/a2j_prove_kills.py`). Suite 7,880 → **7,910**. None was equivalent; equivalence was
checked first, per the A2h/A2i rule.

**One production line changed, and it is a comment.** `BinnedNavigationStrategy` carried
`// delta > 0 is UP` directly above the arithmetic — the opposite of what `NavigationEngine`
sends (NAV_COMP_UP is `-1`). The code was right and the comment was not, which is exactly the
drift that makes an inversion look like a fix to the next reader. It now says which caller sends
what and why the subtraction is correct.

### The pin keys were dead in the browser, the volume bed sat on the candle body, and Ctrl+Left/Right read the wrong line on Aroon (2026-09-19, sixty-first pass)

Six items from Cody, three of them defects; each fixed and proved red first. Suite 7,858 → **7,880**.

**Shift+; could not release a pinned formation — and `;` could not pin one.** Cody: "pressing again
doesn't seem to remove the pin and I'm stuck between 2 points in a chart pattern and can't nav out
of it." Two causes in `keyboard.js`, one in the normaliser. Neither `;` nor `:` was in the browser's
trapped-key list, so an unmodified press returned before reaching .NET at all — the whole pin
vocabulary was documented, bound, tested on the .NET side, and unreachable from the browser. And a
browser reports Shift+; as `:`, which nothing folded back to `;` — the same defect already fixed for
`?` and `!`, one key at a time. With a formation pinned, comma and period stop at its two edges and
the boundary message names Shift+semicolon as the way out, so the exit was a dead key.

- `keyboard.js` traps `;` and `:`, sends both as `OEM1`, and gates the bare semicolon on chart
  focus exactly like comma and period. `KeyNormalizationService` folds `OEM1` and `:` to `;` — and,
  since the pattern is now four keys deep, the REST of the US shifted row too (`{ } _ + | " ~ < >`),
  so no fifth key needs its own bug report.
- **The guard that should have caught it was modelling one row.** `ShortcutReachabilityTests`
  asked "does the key a browser sends resolve?" for every default binding, and its browser model
  knew only the shifted digits. It now knows the whole row, and a named theory pins the four pin
  keys by both spellings. `keyboard-tests.mjs` gains two cases (63/63).

**The volume bed and the candle body were one pitch.** Volume's profile says
`PitchMapping.PriceDirection`, which plays the component's Bullish/Bearish pair and never its
BaseFrequency; the Volume component declared no pair, so the factory gave it the body's 440/220 and
the profile's 330 was written up as "not what you hear" and left there. `SonificationTimbreTests`
was green throughout because its helper hard-codes one 440/220 pair onto both components and
compares everything except Frequency — the mirror-test pathology, carried from the 59th pass.

- `CoreIndicatorProvider` declares Volume at **330/165** — E4/E3, a perfect fourth below the body's
  A4/A3, an interval that lands on neither the body (440/220) nor a wick (880/220). Bullish bars
  and bearish bars are now two notes each.
- **A resumed session would have kept the collision.** Core series restore AS SAVED, and a workspace
  written before the pair existed carries Volume on 440/220. `MigrateSeriesConfig` now re-derives
  the directional pair from metadata wherever a component declares one — the same rule as the pane,
  for the same reason: no dialog writes those two fields. `VolumePitchTests` (five cases) goes
  through the real chain, metadata → factory → strategy, and pins the migration.

**Ctrl+Left/Right did not honour `SubscribedLevelNames`** (carried in TODO since 2026-09-12,
demonstrated today). Aroon is one pane with two neutrals — Up and Down swing about a Midpoint at
50, the Oscillator about Zero — and each component declares which line it answers to. The earcon
and the zone word honoured that; `IndicatorCrossingEngine` read the FIRST visible neutral and the
FIRST visible component, so with the Oscillator focused the key landed where AroonUp crossed 50
and announced a Midpoint cross of the Oscillator.

- One `TargetLevels` for the classifier, the threshold scan and the neutral lookup: switched on,
  and among the lines the focused component names. A component that declares no list keeps every
  line; a component that declares an EMPTY list keeps every line too, on purpose — the empty
  declarations in the catalogue were written to silence the earcon on marker and state components,
  and a jump key with nothing to aim at is a worse answer than a jump to the pane's own line.
- The scans now read the FOCUSED component's data when it has any, not the series' first line.
  `SubscribedLevelNavigationTests`: on the Oscillator the jump lands on its zero cross and says so;
  on Up it lands on the Midpoint; past its only line it says "No crossing in view" rather than
  borrowing a sibling's; and without a declaration the first neutral wins, which is why Aroon
  declares. A dead helper (`GetNamedLevelValue`, no callers) is deleted.

**Answered, not changed.** Overlays on the price pane (Bollinger, EMAs) already draw and sound on
the log scale — `OverlayLayer` maps every Main-pane line through the same `MapY` as the candles,
and the 60th pass's `IsLogScaleFor` reaches every Main-pane series. What does NOT follow them is
the auto-fit: `ViewportRangeCalculator` sizes the price pane from OHLC alone, so a band outside
the candles' range clips. TradingView's default includes every indicator on the price scale in
the auto-fit and offers "Scale price chart only" to opt out; the trade-off here is that the
viewport range is also the pitch range. Cody's call — recorded in TODO.

### The sonification follows the log-scale toggle (2026-09-18, sixtieth pass, second commit)

Cody's decision on the question answered earlier the same day: option (a). The audio path now
normalises a value on the scale its pane is DRAWN on. `ChartMath.NormalizedPosition` is one
normaliser with the same guards and maths as `MapY`, and a test pins that the two agree at
five prices on both scales — the ear uses the eye's arithmetic rather than a copy of it.

- `ISonificationStrategy` takes `isLogScale` on all three entry points (default `false`, so
  no caller changed by accident — pinned). `ViewportRangeCalculator.IsLogScaleFor` answers
  which panes the toggle reaches: the Main pane only, exactly as the renderer, so indicator
  panes stay linear on screen and in sound.
- Both real callers pass it — `NavigationSonifier` when arrowing onto a bar and
  `AudioSequencer` during playback — and each is pinned by a test that goes red when the call
  site passes `false` again (both proved). On a 10k–100k viewport the price 31,623 now sounds
  at 600 Hz on log scale where it is drawn mid-pane, and 392 Hz on linear where it is drawn a
  quarter of the way up. The façade `ISonificationManager.CreateAudioPoint` carries the flag.
- Suite 7,840 → **7,858**. Nothing here is heard yet: Alt+L on a wide-range chart is the check.

### The chart is photographed for the first time, and the pictures find what 73.5% did not (2026-09-18, sixtieth pass)

The day after the rendering campaign, the rendered chart was LOOKED AT. A new browser probe,
`ChartScreenshotProbe`, loads the seeded 200-bar chart through the real WebHost and Chromium,
walks it through linear and log scale, six zoom steps, Heikin Ashi, and then adds nine
indicators through the real Add Indicator dialog — the first browser test ever to drive that
dialog — photographing each state into `scratchpad/screenshots/`. Cody asked for verification
screenshots after the sabotage pass; these are them, and they are repeatable in thirty seconds.

**Four rendering defects were visible in almost every picture, and the suite that had just
measured 73.5% on this layer saw none of them.** Every one is fixed, and every fix was proved
red first (`AxisAndLegendCollisionTests`, nine cases; suite 7,831 → **7,840**).

- **Y-axis labels drawn over each other.** `RenderYAxis` tested label spacing on the RAW
  gridline position, then clamped the top label DOWN into the strip so it would not clip —
  straight onto the label below it. On the log-scale price pane at eight panes "120.00" sat on
  "115.00"; on a short oscillator pane "90.00" sat on "60.00". The spacing test now runs on the
  baseline the text is drawn at.
- **The crosshair's value badge painted over the nearest gridline label** in every indicator
  pane ("957.71" over "1000.00", "4.82" over "4.00", "97.89" over "100.00"). The axis was drawn
  first and never told where the badge would land. Each pane's badge position is now computed
  before its axis, which leaves that label out.
- **Zoomed in past the last bar, the x axis read "07/19 07/19 07/19".** Label slots sit at
  fixed fractions of the strip and each slot's bar index was clamped to the last bar, so the
  empty region to the right of the data was labelled with the last date three times. A date on
  the axis is a claim that a bar sits above it: slots with no bar are skipped, and the last bar
  is named exactly once, right-aligned at the bar.
- **The legend's three-row floor ignored the pane's height.** At eight panes a pane is about
  55px and three rows plus padding is 63px, so the legend ran out of the bottom of its own pane
  and across the next pane's divider — "Signal" cut in half by the ADX pane's top edge. The
  floor is now bounded by what the pane can physically hold; a one-row legend reads "Histogram
  +2 more" rather than spending its only row on "+3 more"; a pane under 29px gets no legend.
  The existing test that pinned "20px pane → 3 rows" was re-aimed at a pane that can hold them.

**The test shape for text collisions, since nothing in the suite had one:** scan the axis
strip for vertical runs of near-white rows; no run may be taller than one line of type. Two
labels on the same rows — or a badge on a label — merge into one run taller than 12px. It
needs no OCR and it catches any pair. "Did a label reach the strip" cannot.

**Seen on screen and NOT changed:** the auto-fit range follows the price series only, so the
Bollinger bands clip out of the pane when zoomed in (a design question, not a bug); and the
first-visit speech-choice banner pushes the toolbar down until any dialog has been used.

**Answered, not changed — does log scale change the sonification? No.**
`ISonificationStrategy.CreateAudioPoint` normalises the value linearly across the viewport
range and maps it to `200 + n × 800` Hz; nothing under `Services/Audio` reads `IsLogScale`. On a
log-scaled chart the eye and the ear disagree: on a 10k–100k viewport the price 31,623 is drawn
at the vertical middle and sounds a quarter of the way up. Candle bodies are direction-pitched
(440/220) and never follow either scale; only the price line tracks level. Alt+L speaks "Log
scale" and the layout describer says "logarithmic scale" — both true of the picture only. And
because Hz is linear while pitch perception is logarithmic, the bottom 400 Hz of the range is
about nineteen semitones and the top 400 about nine, so the sound is perceptually top-compressed
even on a linear chart. Two candidate fixes are recorded in the TODO for Cody to choose between;
neither is landed, because both change what he hears in the week of the presentation.

**The next campaign is `Services/Accessibility`, not `Strategies`.** The census: Accessibility
is 15,901 lines in 49 files with about five ever mutated — the size of Indicators, which
produced the 10.5% cliff — and it is the layer that decides what a blind user hears.
Strategies is 6,408 lines. The order in the TODO is changed and the reason recorded there.

### A2i — the picture is measured, and it is the best-covered layer in the repo (2026-09-17, fifty-ninth pass)

`Core/Services/Rendering` plus `ChartRenderer.cs` is 3,769 lines and decides every pixel of the
chart. Across eight prior campaigns, mutants had reached exactly two rendering-adjacent files and
neither was a renderer; `StandardRenderers.cs` (1,369) and `ChartRenderer.cs` (1,155) had none.

Selected for a presentation rather than for even coverage: the 34 mutants are the ones a room
looking at a projected chart would NOTICE — geometry that drifts, an axis whose labels float off
its gridlines, a pane drawn under the x-axis strip, a colour that ignores the theme, a marker that
swells over the bar it annotates.

- **25 of 34 caught: 73.5%, the highest rate of any campaign** (73.1 / 72.0 / 69.2 / 62.2 / 10.5 /
  **73.5**). Baseline green before, control green after, tree restored byte-identical, and the
  false-catch audit is clean — no bookkeeping-only catches, every one named by tests that are about
  the thing broken. The 48th pass's rendering work paid for itself here.
- **Fully covered:** value-to-pixel mapping (3/3), marker sizing and anchoring (4/4), per-bar colour
  rules (3/3), axis maths (3/3), pane layout (4/5).
- **All nine survivors resolved** — seven closed and proved red, one closed against a corrected
  mutant, one recorded as equivalent. Suite 7,819 → **7,831**.

**The survivors clustered, and the cluster is the finding.** Horizontal placement was guarded;
candle geometry beyond the x position was not. Nothing checked that a doji still draws, that a wick
spans high to low rather than open to close, or that hollow-up-candles are not inverted. And the
whole Cipher S phase-colour overlay had no fixture at all — grep the test project for `phaseData`
before this pass and there is nothing.

**A test answered by the wrong thing.** `ADojiReadsAsUp` renders a flat bar and asserts a bullish
pixel exists — and it passes with the body suppressed entirely, because the theme's bullish WICK
colour is within tolerance of its bullish BODY colour. The wick was answering a question about the
body. The new tests give the wick a colour of its own so the assertion is about the thing it names.

**Two equivalent mutants, both established by measurement, and one of them was my own
mis-specification:**

- `RenderCandles`' half-pixel alignment (`Math.Floor(xRaw) + 0.5f`) has no observable effect:
  `SKPaintPool.Rent()` calls `Reset()` and `SKPaint.IsAntialias` defaults to FALSE, so across
  twenty sub-pixel offsets the wick lands in the identical column every time. Its comment explains
  a defect caused by anti-aliasing, which these paints do not use. The line is kept — it costs
  nothing and becomes load-bearing the day AA is switched on — and a test now pins that PREMISE, so
  whoever enables anti-aliasing is told the alignment has become real and needs a test of its own.
- The y-axis swatch mutant rewrote `DrawRect(SKRect.Create(x, y, w, h))` as
  `DrawRect(x, y, w, h)`. Those are byte-identical: the four-float overload IS `(x, y, w, h)`. The
  historical defect — a 4x3 tick becoming a block that buried the price labels — came from passing
  BOUNDS to it, which is a different edit. The mutant was wrong, not the defect absent, and the new
  test is written against the correct one.

**Two more fixture lessons, both found by a test of mine passing under its own mutant**, and both
of the same family as A2h's five:

- **A fixture can be too comfortable.** At three or four panes the axis labels land about twenty
  pixels apart — above the minimum — so the spacing guard never fires and a test built on that
  layout passes with the guard deleted. At eight panes each pane is under thirty pixels and the
  smallest gap is 18 with the guard and 11 without.
- **A helper can be the wrong shape.** A series whose component is display-type Line paints a line,
  so a frame test looking for candle bodies finds none for a reason that has nothing to do with
  what it asks.

### A2h — the maths this terminal is FOR gets measured, and the honest catch rate is 10.5% (2026-09-14, fifty-eighth pass)

`Core/Services/Indicators` is 40+ files and 15,813 lines. Across seven prior mutant campaigns and
the browser audit, mutants had reached exactly FIVE of its files, all small helpers. The eight
largest providers — Pulse, Cipher B, Cipher C, Loukas, Top/Bottom, Cipher S, Cipher A, Cipher SR,
some 7,000 lines of signal generation — had none, ever. Four more providers were named by no test
at all.

- **38 mutants, 6 caught naively, 4 honestly: 10.5%.** Against A2d 73.1, A2e 72.0, A2f 69.2 and
  A2g 62.2 on disjoint file sets, that is a cliff rather than a drift, and it is where the
  terminal's actual subject matter lives. Baseline green before (7,724), control green after,
  every file restored byte-identical.
- **The false-catch audit found a NEW mechanism.** Two of the six catches came only from
  BOOKKEEPING guards — `TheBlindSpotsOfThisGuardAreTheOnesWeKnowAbout` and
  `TheSuffixGuardComparesRealNumbersAndItsExemptionsAreStillEarned` — which fail because a mutant
  changed which components produce values on the synthetic series, so a pinned list went stale.
  Nothing asserted the rule. This is worse than A2's flaky catches: **the natural repair for that
  failure is to edit the pinned list**, which restores green and leaves the defect.
- **All four honest catches came from the causality contract or from one of the three providers
  that happen to have behavioural test files.** The repo has a genuinely strong, reflective,
  every-provider look-ahead guard and had almost nothing asking whether a signal fires where it
  should.

**32 of the 34 survivors are closed, each proved red by re-applying its mutant** (32/32,
`scratchpad/a2h_prove_kills.py`). Suite 7,724 → **7,819**, across five new files:
`CipherBSignalRulesTests`, `PulseSignalRulesTests`, `CipherSrZoneRulesTests`,
`IndicatorMarkerAndCacheRulesTests` and `IndicatorDefinitionRulesTests`.

**The shape of the gap, in one sentence: a helper with excellent tests and a caller free to stop
calling it.** Cipher B's 1,381 lines had no test that ran `Calculate`; what it had was seventeen
METADATA assertions and a file that exercises the `ShiftMarkersForwardExcept` helper directly and
never calls the provider. One mutant changed the call site from the excepting form to the plain
one — reintroducing the defect that helper exists to prevent — and every helper test stayed green.
That is A2g's "an exemption that names a consumer is not a test of the consumer" in a new guise.

**Three look-ahead mutants survived a guard built for look-ahead**, and the reasons are each
instructive: a marker that is merely LATE is still causal (the prefix guard cannot see it); an
INCOMPLETE weekly bucket only arises where bar spacing is irregular, and the prefix sweep uses the
three regularly-spaced probe flavours; and a cross-series provider fed no external data produces
nothing for the guard to compare.

**FIVE FIXTURE LESSONS, each found by a test of mine passing under its own mutant.** These cost
more time than the tests did and are the transferable part:

- **A fixture can be too short to contain the case.** Over 600 bars, EVERY WaveTrend pivot pair
  satisfying the divergence shape has both legs past the depth threshold, so the gate never changes
  a verdict. The pairs that straddle it first appear past about a thousand bars — seven of them in
  1,400.
- **A fixture can be too smooth to contain the case.** A sine-based decline creates swing highs
  that generate divergence evidence of their own (0.637 against the 0.000 a monotonic decline
  gives); an oscillating post-peak stretch mints new pivots that silently replace the level under
  test. Three separate tests needed strictly monotonic stretches.
- **A fixture can be degenerate in a way that erases the distinction.** Bars built as
  `(c+2, c-2, c)` have HLC3 and `(high+low)/2` equal to the same number, so a test of which price a
  VWAP weights could not tell them apart. A flat series cannot sit ON an oscillator's midline
  either — zero deviation makes the oscillator NaN and the comparison is never reached.
- **A window can be too wide to produce a whipsaw.** A single spike keeps a nine-bar midpoint
  elevated for nine bars, which is a cross and a later cross back. The one-bar reversal the
  confirmation rule exists to refuse needs a one-bar window, and a baseline that declines so the
  fast line is genuinely below the slow one first.
- **A guard can be true by construction.** Asserting "neither leg is deep" does not test an
  `AND`→`OR` change, because `OR` still requires one deep leg.

**Two survivors are EQUIVALENT MUTANTS, established by measurement rather than assumed:**

- **Cipher A's `sustainedOs` conjunct.** Over four flavours × 3,000 bars the mutant produces the
  identical 466 signals. A cross UP means the wave turned up, so if it is still below the oversold
  line while rising, the previous bar was lower still — the second conjunct is implied by the
  crossover it is joined to.
- **Hurst on raw differences instead of log returns.** Median exponent 0.571 against 0.575 over
  1,100 values on a geometric walk spanning a 66× price range. Rescaled-range analysis divides a
  range by a standard deviation computed on the SAME window, so it is dimensionless and
  window-local: changing the units of the return series does not move it. The obvious test —
  scale invariance — passes under BOTH forms for the same reason.

Both keep a test, because each states a real contract and would catch a change that decoupled it.

**Two dead computations, found by a write-only-local sweep before the campaign ran and deleted.**
`CipherBProvider.mfFast` was a full 14-bar SMA over the series, recomputed on every pass and read
by nothing, under an eight-line comment explaining why the gold-dot gate needs it — the gate tests
`clv[i] > 0`, the raw single-bar body/range sign. `TopBottomDetectorProvider.bbUpper` was a full
Bollinger upper band, computed and discarded. **Whether the gold gate SHOULD use a fast Money Flow
window is left open on purpose: wiring it up would change which bars fire gold.**

**Also recorded, not fixed: Cipher B's divergence depth gate is close to inert.** A WaveTrend pivot
low IS a cycle extreme and cycle extremes are deep by construction, so the gate rejects something
roughly seven times in fourteen hundred bars. Its comment says it filters "two shallow wiggles with
price drift"; that shape barely occurs.

### A2g — the channel this terminal IS gets measured, and two guards turn out to be written in terms of themselves (2026-09-13, fifty-seventh pass)

`Core/Services/Audio` is 19 files and 4,394 lines, and across six prior mutant campaigns and the
browser audit **exactly one mutant had ever been applied to any of it**. Eight of its 32 declared
types are never named by either test project. For a sighted user audio is decoration; for this one
it is the chart — pitch is the value, pan is the bar's x-position, grit is the wick's length and
the body's size, noise is the overbought zone, timbre is which indicator you are hearing. Every one
of those rules is written in exactly one place in that directory.

- **37 mutants, 23 caught, honest catch rate 62.2% — the lowest of the six campaigns**, against
  A2d 73.1%, A2e 72.0% and A2f 69.2% on disjoint file sets. Baseline seen green immediately before
  the run (7,680 passed), control run green after, every file restored byte-identical. The
  false-catch audit is clean: every caught mutant is named by tests that are about the thing it
  broke, so the naive rate is the honest one.
- **All 14 survivors are closed and each is proved red by re-applying its mutant**
  (`scratchpad/a2g_prove_kills.py`, 14/14). Suite 7,680 → **7,724**.

**Two guards were true by construction, which is the finding of the pass.**

- `PlaybackLayerTests` re-implemented `AudioSequencer.LayerVolume`'s switch *inside the test body*
  and asserted that copy against its own `InlineData` — two hand-written copies of one table,
  neither of them production code. Flattening Background from 0.60 to 1.00 in the sequencer broke
  nothing. It now drives a real one-bar playback and measures the volume the sequencer hands the
  driver. **An exemption that names a consumer is not a test of the consumer** — `PlaybackLayer` is
  one of `DeclaredKnobObservabilityTests`' four exemptions, and the consumer it correctly names was
  itself unguarded.
- `UiEarconSlotTests` asserted that the UI round-robin stays below `EarconPatchPlayer.CueSlotStart`
  — the constant the mutant moves. Widening it from 26 to 30 reintroduces the original defect (the
  round-robin reaches the cross-chirp slots again) and the assertion moves with it. The boundary is
  now stated between two **independent** constants: the cue block must end before `CrossEarcon`'s
  chirp pair begins.

**The rest of the survivors, by what they cost the user.**

- **Hearing safety, two of them, on adjacent lines.** The engine-boundary clamps on frequency and
  volume sit together; the frequency one was tested and the volume one was not, because it asserted
  `Peak <= 1.0` — which the downstream brickwall limiter guarantees no matter what the clamp does.
  The class docstring works that trap out for the frequency tests and the volume test walked into
  it one method later. Now asserted relationally: asking for more than full scale must render
  *identically* to asking for 1.0.
- **`_stopAllFaded` is an EQUIVALENT MUTANT, and the comment above it was wrong.** A three-way
  experiment (flag alone / literal alone / both) shows the protection is carried entirely by
  re-arming to `_userMasterGain` instead of a hardcoded `1.0f`; dropping the flag changes nothing
  observable, because re-arming to the user's own value is idempotent. The flag is kept for what it
  says, the comment claiming it was the mechanism is corrected, and the new tests guard the half
  that carries the weight.
- **A muted series still sounded under the arrow keys.** `MuteIsAbsoluteTests` states in its own
  docstring that the series mute is gated in `SyncNavigationSlots`, and tests the *component* mute
  everywhere instead: dropping `comp.IsMuted` from the strategy turns 26 tests red, dropping
  `series.IsMuted` from the sonifier turned none. The first draft of the replacement was green too,
  for an instructive reason — the strategy zeroes a muted series' volume anyway, so "is anything
  audible" cannot see the difference. What the early return guarantees is **no voice command at
  all**, which is what is asserted now.
- **A Ping stopped being a transient.** The envelope's decay rate can drop tenfold with nothing
  red, smearing every dot, arrow and wick into the continuous bed they exist to stand out from.
- **`AudioSequencer.IsPing` could go case-sensitive again** — the exact defect its own doc comment
  records, where an imported `"ping"` becomes a continuous voice with duration 0: a playback slot
  that never decays.
- **`SilencePlaybackVoices` could stop covering the cloud slots (96–127)**, so `Stop` and pause
  left the last bar's cloud chord ringing.
- **A `Cross` marker could be dropped from `AudioConstants.MarkerDisplayTypes`** — a set read by
  three channels at once (cluster ticks, signal speech, the Ping NaN guard), so losing a shape
  makes it inaudible, unspoken, and no longer silenced on its empty bars.
- **An explicitly EMPTY level subscription could subscribe to everything.** Second campaign
  running: A2f found the same shape in the narration path. **The OFF value of a setting is the one
  nobody writes a fixture for.**
- **An earcon assigned to a deleted patch could go silent** rather than falling back to its
  built-in tone — `TryPlayOverride`'s return value is a claim the caller acts on, and a broken
  reference reading as silence is the one failure this user cannot see.
- **A distribution series with no components could be treated as switched off** — the third
  campaign in a row where a guard widened ON PURPOSE, with a comment explaining why, can be
  narrowed back in silence. **A fix whose only record is a comment is not under test.**
- **`WavetableLibraryService` had no test of any kind**, so the wavetable/sample classification
  could invert: an AKWF single cycle would register as an unpitched one-shot click.
- **8-bit WAV imports could arrive with a full-scale DC offset.** The existing test checks the
  peak, and the peak is exactly where a clamp hides the offset; the zero crossing is where it
  shows.

### A2f — the speech path is measured for the first time, and a knob that moves nothing is a lie the dialog tells (2026-09-13, fifty-sixth pass)

Two pieces of work with one subject: **the parts of this terminal that talk had never been
measured, and the parts that claim to be adjustable had never been asked whether they are.**

**A2f, the fifth mutant campaign.** `Core/Services/Accessibility` is 49 files and 15,898
lines — tied with `Services/Indicators` as the largest body of code in the repo — and eleven
mutants had ever been applied to it, all eleven on small helper files. Its five largest files,
`SpeechFormatter`, `NarrationScanner`, `DrawingInteractionManager`, `TactileCanvasCoordinator`
and `PlaybackNarration`, had had **zero**. For this application's user that is not a feature
area, it is the entire output channel: a defect in the renderer is seen eventually, a sentence
that never fires is indistinguishable from a market that did nothing.

- **26 mutants, 18 caught, honest catch rate 69.2%.** Baseline seen green immediately before
  the run (7,666 passed); control run green afterwards; every file restored byte-identical.
  Against A2d's 73.1% and A2e's 72.0% on disjoint files chosen weeks apart, the rate is a
  property of the suite rather than of where it was pointed.
- **All eight survivors are closed, each proved red by re-applying its mutant**
  (`scratchpad/a2f_prove_kills.py`, 8/8):
  - a `TimestampReadLocation` of `"None"` could be ignored, so a setting the user switched OFF
    put a timestamp on every arrow press;
  - the empty-series early return — widened on purpose, its comment explaining that the prefix
    is the only thing that would be said — could be narrowed back, silently;
  - an explicitly EMPTY level subscription could narrate every level instead of none;
  - a level sitting at zero under its own name (a Midpoint at 0, a signal line) could lose its
    number and announce a crossing with no price in it;
  - the zone proximity window could widen a hundredfold, so every level reads as active on every
    bar;
  - the hit tester could select the FARTHEST component under the cursor;
  - a click on the x-axis strip could select a series in the pane above it;
  - and a HIGH-severity error could stop interrupting, arriving after the decision it was about.
- **Two of those are one shape:** a selection rule is only under test when at least two
  candidates compete. Every hit-test fixture in the repo had a single line on the chart, and
  every one passed `axisHeightFraction: 0f`, which made the x-axis guard unreachable.
- **A method correction worth more than any single finding.** The first pass recorded empty
  `failing_tests` for all 26, because `-v q --nologo` suppresses per-test failure names — which
  silently disables the false-catch audit, the check that turned A2's naive 79% into an honest
  61%. The six mutants caught by two tests or fewer were re-run with names captured; all six are
  genuine, each killed by a topically relevant test. The harness now carries the reason in a
  comment so the flags do not come back.

**`DeclaredKnobObservabilityTests` — one guard for a defect CLASS.** Four bugs in the 46th–49th
passes had a single shape: a property declared, stored, round-tripped and rendered by the
Properties dialog as a working control, with nothing at the far end reading it
(`UsePolarityColoring`, `DefaultReferenceLevel`, `DefaultPane`, three indicator bool
parameters). Reading the code cannot catch it — the declaration is perfect. A conventional test
cannot catch it — the declaration does exist.

- Every knob on `ComponentConfig` is enumerated **by reflection** (a hard-coded list would be a
  second place each new property must be added, which is this file's own subject), flipped, and
  required to move one of the four channels the application actually delivers through: pixels,
  the sonification point, the navigation readout, and the bar-close narration scan. 27 shape x
  audio cells, because most knobs are live in only some of them.
- **Proven red on the real defect:** reverting the polarity split in `RenderLine` makes it name
  `UsePolarityColoring`, and nothing else.
- **It found a dead field on its first run.** `ComponentConfig.SecondaryWaveform` was declared,
  copied by `Clone()`, and written into every saved workspace as `""` — with no metadata field
  able to set it and no reader anywhere in the tree. Deleted, along with its clone line. Saved
  workspaces carrying the key are unaffected; an unknown key is ignored on load.
- Four knobs carry exemptions that NAME their real consumer — `PlaybackLayer` and `DecayMs` in
  `AudioSequencer`, `SubPaneHeightRatio` in `ChartRenderer`, `DataMapping` in the tactile canvas
  and the state mapper — so each exemption is a claim that can be checked rather than trusted,
  and a second test fails if an exemption ever names a property that no longer exists.

**`docs/REPORT_CARD_2026-09-13.md`** — the third graded pass. Alerts and monitoring D → C+ → **B**
(the notifier seam the last card asked for is built, across four phases); data flow and providers
→ **B** (the conformance suite went 33 rows red on its first run); trading-live → **B** (the
Gemini sandbox run means "never exercised against a live venue" is no longer true); drawing and
rendering → **B**; documentation → **A-**. Workspace and persistence is HELD at C+ on purpose —
this window produced three more restore defects. Summary grades: chart-reading terminal
B+ → **A-**, real-money readiness B- → **B**.

---

Older releases (2.10.0 and earlier, back to the first entries) are in [`docs/archive/CHANGES_HISTORY.md`](archive/CHANGES_HISTORY.md).
