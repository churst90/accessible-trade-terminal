# What's New

## 2.7.0 — everything you switched on now speaks

2.6.0 gave the terminal a voice. This release is about the places that voice was
**silent while you had already asked for it** — indicators whose narration switch
did nothing, a signal that could never be spoken, a bar close where one of two
sentences was thrown away before you heard it. Nothing on this list is a feature
you were waiting for. It is a feature you had switched on.

*This file covers the current release only. Everything before it is in `CHANGES.md`.*

### The indicators that could not speak

- **Oscillators can narrate at last — all of them.** Stochastic, CCI, MFI, ADX, ROC, Williams %R,
  TRIX, CMO, Choppiness, PPO, StochRSI and two dozen more had no way to say anything at all:
  pressing N confirmed *"narrating"* and then gave you silence for the rest of the session. Only
  three indicators in the whole terminal had ever been given a voice by hand. They speak their own
  thresholds now — *"Stochastic 14: crossed above overbought, 80."* — including levels you have
  moved or added yourself.
- **Flag a moving average with N and it finally has something to say**: *"Price crossed above
  EMA 9 at 64,900."* on the bar close. Cross detection had been reachable only by indicators that
  declare their lines as levels — Cipher SR, Spider Lines — so a plain EMA you had switched
  narration on for stayed silent forever. Crosses only: an average has no side to break and
  nothing to test, so it gets the one sentence that is true of every line on the price axis.
- **Playback now says the signal you were actually waiting for.** Cipher B's gold Triple
  Confluence dot can only print on a bar that is *also* an oversold crossover and *also* a
  WaveTrend cross, and playback takes two clauses per bar — filled, until now, in the order the
  indicator happens to declare its components. The gold dot was dropped on every one of them.
  Playback ranks by how often each marker actually fires on your chart: **the rarest leads and the
  commonest is what gets dropped**, and a rarer signal is no longer swallowed by the two-second
  window a routine one opened a few bars earlier.
- **The first bar to close after you press N now speaks.** It never did: the bar that is still
  forming when you switch narration on was being treated as history, so the one bar you are
  listening for was the one bar that could not talk.
- **Playback tells you when it is about to say nothing.** Add Cipher B or Cipher SR, press play,
  and you heard tones and no signals — because narration is switched on per series and nothing
  said so. It now tells you once, when you press play: *"No series is set to narrate, so signals
  will not be spoken."* Only when there is actually something you are missing.
- **What playback says and what bar-close narration says, in one sentence:** playback speaks what
  happened **at a point** (a signal printing on the bar the tones just reached), and bar-close
  narration speaks what **changed** (price crossing your EMA, an oscillator leaving a zone). That
  is why your averages never talk during a playback however you have flagged them — at ten bars a
  second a line with a value on every bar is a wall of numbers.

### One sentence per bar close, and it says which bar

- **A bar close is one sentence again.** The candle and what your indicators made of it arrive
  together — *"Close 64,905 at 14:32, Bullish engulfing. New bar: Open 64,910. Triple confluence
  buy, strong confirmation."* Two parts of the terminal were announcing the same moment
  separately, and the second one silently replaced the first, so you heard the candle and nothing
  else.
- **The closing time is new.** On a one-minute chart the announcement named no time at all, so
  nothing in it — or in the journal afterwards — said which minute it belonged to.
- **Arrowing along an intraday chart reads the time, and the date only when you cross into a new
  day.** *"14:00."*, *"15:00."*, then *"September 6, 00:00."* Every bar used to carry the whole
  stamp — "September 05, 2026, 14:00" — which is the same eleven syllables in front of each of
  twenty-four consecutive readings. It follows what you are READING, so arrowing backwards over
  midnight names the day too. **Settings (F12) → Speech → Speak the date on every bar** puts it
  back in front of everything if you prefer it there.
- **A daily bar no longer says "00:00" after its date** — a time that is identical on every bar a
  daily chart has.
- **A viewport on an intraday chart no longer reads one date twice.** "From September 5 2026 to
  September 5 2026" is not a range. It is *"from September 5 2026, 14:32 to 15:22"* now, with the
  date spoken once when both ends share it.

### Narration you can aim

- **N is the third switch, beside H and M.** Hide, mute, narrate — three switches on a chart
  object, and two of them were a single letter while the third was `Ctrl+Alt+Shift+N`. It also
  used to act on the *series* even when your cursor was on a component, so "M muted the component
  but N narrated the whole series" was the actual behaviour. N now resolves its target exactly the
  way H and M do. `Ctrl+Alt+Shift+N` still works and is still the one to use when focus is outside
  the chart.
- **Narration is per component.** A Cipher B has eleven of them; switching it on for the
  divergence you care about used to hand you every cross, dot and band as well. Press N on a
  series to turn it on, then N on a component to narrow to just that one, and N on it again to
  widen back out. Nothing lands in a state where narration is "on" and silent — and if you flag a
  component on a series that is not narrating, the confirmation says so rather than letting you
  wait for speech that will not come.
- **Playing one series narrates one series.** Space, Shift+Space and the component play used to
  narrate the whole chart whatever you had asked to hear, so playing one component of one
  indicator could speak another indicator's signals over it. Speech is scoped the way the tones
  are now.
- **Ctrl+Alt+Shift+O: narration off everywhere.** Every series switched off, every component
  selection cleared, announcing how many — the third undo-all beside Ctrl+Alt+Shift+K (show all)
  and Ctrl+Alt+Shift+U (unmute all).
- **Those two undo-alls now actually say "Nothing was hidden." and "Nothing was muted."** They had
  the sentence and the terminal was discarding it, so with nothing to restore the chord was silent
  — a dead key to a screen-reader user.
- **A narrated signal is introduced by its component, never its series.** *"WaveTrend Cross Bull:
  Wave cross up 12."* You chose which series narrate; which marker fired is the news. Where the
  signal's own wording already names the component, nothing is added.
- **Hidden and muted are spoken first, narrating last.** *"Hidden and muted. Cipher B. 11
  components…"*, and *"…64,900. Narrating."* Something that is switched off explains itself before
  the reading, because an interruption takes the end of a sentence and not its start; narration is
  an addition and goes at the end. Both flags are said when both apply — they are cleared by
  different keys, and a readout naming one of them guarantees a second wrong guess.
- **`H`, `M` and `N` say "No chart loaded."** instead of doing nothing quietly.

### Names that only appear when there are two of something

- **A moving average is called by its period — "EMA 50", "SMA 21", "MA Cloud 9 21" — whether or
  not there is another on the chart.** That is how traders name them ("the 50"), and it is the one
  case where a parameter *is* the name. EMA, SMA, WMA, HMA, ALMA, DEMA, TEMA, KAMA, ZLEMA, SMMA,
  TMA, VWMA and the MA Cloud declare it; nothing else does.
- **Everything else is called by its name alone — "Cipher B", "RSI", "Market Structure" — until
  there are two of it.** Then, and only then, the parameters the two disagree about are added
  ("MACD 12 26 9" beside "MACD 8 21 5"), and past three differing values the name becomes an
  ordinal — "Cipher B 2" — rather than a wall of digits. Add the second instance and the first is
  renamed in the same breath, because a suffix on one of a pair tells you nothing about which is
  which.

  Arrowing onto Cipher B used to read "Cipher B 9 12 60 50 14 …" — every parameter, unlabelled,
  every time. **And the new rule now applies to charts you saved before it**: a restored workspace
  derives every indicator's name afresh from its parameters instead of reading back the name it
  was saved with, which is how the recitation kept coming back on every existing chart.
- **N on a component confirms with the component alone** — *"Triple Confluence Buy, narrating"* —
  not the series, its parameters and then the component.
- **Four indicators stopped spelling a parenthetical at you.** "Market Structure (HH/HL/LH/LL)" is
  now just **Market Structure**; the same for **Regime Filter**, **Value Deviation** and
  **Volatility Regime**. Those glosses were written for a picker list you look at, and they were
  sitting on the field a screen reader reads every single time you arrow onto the indicator. They
  still appear in each indicator's description in the Add Indicator dialog.

### Earcons you can tell apart

- **Two families, on the Sonification tab.** **Market earcons** are the ones about the market — an
  alert firing, a new bar opening, a strategy setup arming or reaching its entry. **Interface
  earcons** are the ones about the terminal — the edge of the chart or of a series, a mode toggled
  with F2 or F3, an action that succeeded or is being retried, the connection changing state. Both
  start on, and Shift+F3 still mutes everything at once; these two decide what that mute is
  switching off.

  They are worth separating because they fire at completely different rates for completely
  different reasons. The boundary tone sounds on *every further arrow press* at the edge of a
  chart; a setup bell might fire twice in a session. Until now one switch governed both, so
  quietening the first meant losing the second. And every interface earcon is also **spoken**, so
  turning that family off costs you nothing but the beep.
- **Neither switch reaches error tones or order outcomes.** A fill, a stop, a take-profit and every
  error still sound with both families off and with Shift+F3 muted. An error that makes no sound
  and no sentence did not happen as far as you are concerned, and money moving is neither a market
  observation nor an interface confirmation.

### Settings

- **"Reset all settings to defaults" is on the General tab, and it asks the same way.** It puts
  every preference, every keyboard rebinding, your own themes, your sound patches and earcon
  assignments, and the colours and sounds you gave individual indicators back to what the terminal
  shipped with.

  **It also tells you what it is KEEPING**, and that is the half worth hearing: your API keys, your
  paper trading account and its history, and your saved workspaces all survive. "All
  personalization will be lost" is a sentence most people will read as "including my broker
  logins", and being wrong about that in the frightening direction is what stops someone using a
  button they need. Restart the terminal afterwards so every part of it re-reads the defaults.
- **"Speak the date on every bar"** — Speech tab, off by default. Off, an intraday bar reads its
  time alone and the date is spoken only when you cross into a new day.
- **"Speak time landmarks during playback"** — Narration tab, on by default. The date or hour as
  playback crosses a boundary is the only thing that says where in time the tones are, and it is
  also a calendar read to you every few seconds. Turning it off keeps the signals and the
  formation outcomes.

### Reading the chart

- **Twenty-four candle patterns, up from twelve.** Three inside up and down, three outside up and
  down, morning and evening doji star, the abandoned baby, the three line strike, and the rising
  and falling three methods — the last of which is a five-bar shape the terminal previously had no
  way to even express.
