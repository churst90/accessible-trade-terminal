# What's New

<!-- This file holds the CURRENT RELEASE ONLY. The section below belongs to the tag named in
     its heading; anything written after that tag goes under a NEW `## Unreleased` heading above
     it, and the previous release's section moves out (its history is already in CHANGES.md).
     Check with `git diff <lasttag> HEAD -- docs/WHATSNEW.md` before every cut — this file has
     accumulated post-tag entries under an old heading before. -->

## Unreleased — the Windows desktop client, put in front of a screen reader for the first time

*Everything before this is in `CHANGES.md`.*

The desktop client had never been run with a screen reader. It was run on 2026-09-21, and
almost everything below is what that one session found. **If you use the Windows client, this
release is the one that makes it work.**

### Speech on the Windows client

- **It speaks.** The client shipped without the NVDA controller library, so on every release
  before this one the chart said nothing at all. It is in the download now, under both the names
  NV Access has used for it, so nothing needs renaming or placing by hand.
- **JAWS is supported.** Previously JAWS users got silence with no workaround — and it was not a
  setting they could have found, because the chart is drawn on a native canvas that a browser
  live region cannot reach. The terminal now speaks to JAWS directly. Nothing to install: if JAWS
  is running, it is used.
- **A screen reader started *after* the terminal is picked up.** It used to be ignored for the
  rest of the session.
- **The terminal says when it cannot speak.** The Journal (Ctrl+Alt+Shift+J) records which
  route speech is taking — NVDA, JAWS, or the browser's live region — and says so plainly if
  none of them is available, rather than simply going quiet. Every sentence it could not say is
  still written down there.

### The rest of the Windows client

- **The market dropdown lists every provider again.** The download was missing the file that
  vouches for the bundled providers, so all 33 were refused and only the built-in sources
  appeared. Nothing was wrong with your installation.
- **Custom indicators and strategies run.** The script worker was missing from the download, so
  user-compiled code could not execute at all.
- **Braille and Dot Pad support is actually in the download.** The tactile SDK had never been
  included, so the Braille tab rendered and the device could never connect.
- **The chart no longer disappears in a small window.** Below a certain height the toolbar took
  the whole window and the chart was squeezed to nothing. It now keeps a minimum height and the
  page scrolls instead.
- **There is a log file**, at `%LocalAppData%\AccessibleTrader\logs\terminal.log`. The Windows
  client previously wrote no log of any kind, which is why these problems went unnoticed for so
  long.

### The chart, on every platform

- **Indicators drawn on the price chart stay inside it.** A Bollinger band or Keltner channel
  wider than the candles used to be clipped off the pane — and because the pane's range is also
  the *pitch* range, it went silent as well as invisible. Press **Alt+F** for the old behaviour
  ("fitting price only"), which gives the price line the full pitch range at the cost of letting
  bands run off the edge.
- **Pitch is spaced the way the ear hears it.** Equal steps up a pane are now equal musical
  intervals wherever you are in it. Before, the bottom of a pane carried a whole octave in its
  first quarter and the top a mere third in its last, so a price high in the window barely moved
  in pitch. The extremes are unchanged; the middle has been redistributed.
- **The price chart gets more room.** With one indicator it was an even split with volume; the
  price pane now takes about two thirds. With four or more indicator panes nothing changes.

### The chart gets its window back

- **The chart is most of the window now.** On a maximised 1280×781 window carrying candles,
  volume, RSI and MACD, the terminal's own toolbars and bars took 394 pixels and the chart
  got 279 — 41.5% of the space. The same window now gives the chart about 65%. Three things
  changed, none of them audible: the toolbar's icons no longer carry a text caption (the
  spoken name and the tooltip are exactly as before), the two rows of icons became one, and
  the padding between every band was trimmed.
- **Toolbar captions are a setting.** *Settings → Appearance → Toolbar captions* puts the text
  label back under every icon for anyone who reads them. It previews as you tick it and Cancel
  takes it back, like text size.
- **The toolbar's two rows now divide as buttons, then the chart cascade.** Pan, zoom and the
  four display toggles moved from the end of the symbol row to the end of the button row, so
  Tab reaches them after Help and before Market. Nothing else about the order changed.
- **Why the price pane looked as small as an indicator pane.** With three indicator panes at
  the old height, every pane was floored to the same size and the price pane's larger share
  could not show at all. It shows now, because the chart has the height to spend.

### If you use the hosted terminal

- **A dropped connection is announced.** When the circuit to the server was lost, a screen
  reader user was told nothing at all — the reconnect banner had no live region. It now speaks
  each attempt, says whether the server is restarting, and answers the question that actually
  matters: **orders already placed are held at the venue and are unaffected.**

## 2.11.0 — the pin keys work, the ear follows the eye, and the chart gets looked at

*Everything before this release is in `CHANGES.md`.*

The three changes you will notice first — the pin keys arriving, volume getting its own note,
and the sonification following the log scale — were each confirmed by ear on a real chart before
the tag went up. Nothing in this release ships unheard.

### The keyboard

- **`;` and `Shift+;` work.** The whole pin vocabulary — choose which of several overlapping
  formations leads the readout, then release the choice — was bound, documented and tested,
  and **completely unreachable from the browser**: neither key was in the browser's trapped-key
  list, so a press never reached the terminal at all, and a browser reports `Shift+;` as `:`,
  which nothing folded back. If you ever found yourself pinned between two points of a chart
  pattern with no way out, that was this. Both keys arrive now: `;` pins the formation you want
  to lead, and `Shift+;` releases it so comma and period walk every formation again.
- **The whole shifted top row is folded, not just the key that broke.** `{ } _ + | " ~ < >`
  are all handled now, so the next binding on a shifted key does not need its own bug report.
- **Ctrl+Left and Ctrl+Right jump on the line the focused component actually answers to.**
  On a pane with more than one reference line this was reading the first one. Aroon is the
  clear case: Up and Down swing about a Midpoint at 50 while the Oscillator swings about zero,
  so with the Oscillator focused the key used to land where *AroonUp* crossed 50 and announce
  it as the Oscillator's Midpoint cross. It now lands on the focused component's own crossing,
  reads the focused component's own data, and says "No crossing in view" rather than borrowing
  a sibling's.

### Sound

- **The sonification follows the log-scale toggle.** Press **Alt+L** and the ear moves with the
  eye: a level-pitched component in the price pane takes its pitch from where the value sits on
  the scale *as drawn*. On a 10,000–100,000 window the price 31,623 is drawn halfway up the
  pane and now sounds halfway up the sweep; before, it sounded a quarter of the way up while
  the picture said middle. Indicator panes are never on the log scale, on screen or in sound,
  which is unchanged.
- **Volume has its own note.** The volume bars were playing on the candle body's pitch — the
  bed sat directly on the thing it was meant to sit under. Volume now sounds at **E4/E3**, a
  perfect fourth below the body's A4/A3, an interval that collides with neither the body nor a
  wick. Up bars and down bars are two distinct notes.
- **A workspace you saved before this release heals itself.** Core series are restored exactly
  as they were saved, so a resumed session would have kept the old collision; the pitch pair is
  now re-derived from the indicator's own declaration on restore, the same rule the pane
  assignment already used.

### The chart on screen

The rendered chart was photographed for the first time — a new probe drives a real browser
through linear and log scale, six zoom steps, Heikin Ashi and nine indicators, and saves a
picture of each. **Four drawing defects were visible in almost every shot**, and all four are
fixed:

- **Y-axis labels drawn on top of each other.** Label spacing was tested at one position and
  the text drawn at another, so at eight panes "120.00" sat on "115.00".
- **The crosshair's value badge painted over the nearest gridline label** in every indicator
  pane — "957.71" over "1000.00". The axis is now told where the badge will land and leaves
  that one label out.
- **Zoomed past the last bar, the date axis read "07/19 07/19 07/19".** Empty space to the
  right of the data was being labelled with the last bar's date, repeatedly. A date on the
  axis is a claim that a bar sits above it; slots with no bar are now skipped, and the last bar
  is named exactly once.
- **The legend ran out of the bottom of its own pane** at eight panes, across the next pane's
  divider. Its row count is now bounded by the height the pane actually has.

These are sighted-companion and screenshot concerns rather than things you hear, but a chart
that photographs cleanly is a chart whose layout description can be trusted.

### Underneath

- **The suite went from 7,666 to 7,910 tests**, almost all of it from five deliberate
  break-it-and-see campaigns aimed at layers that had never been tested that way: the speech
  path, the audio path, the indicator maths, the renderer, and the accessibility layer itself.
  The accessibility layer — the code that decides what you hear and when — scored the highest
  catch rate in the repo's history at 82%, and every gap it exposed was about *when* the
  terminal may speak rather than what it says. Three narration gates that had no test at all
  now have one: pressing **N** can no longer start reciting history, an unconfirmed tick can no
  longer announce a cross that then un-happens, and switching narration on no longer fires a
  cross for every overlay at once.
- **The indicator maths scored 10.5%**, by far the lowest, and that number is published rather
  than buried. Thirty-two of the thirty-four gaps it exposed are closed and each was proved by
  re-breaking the code; the other two change no behaviour. The shape of the gap in one sentence:
  a helper with excellent tests and a caller free to stop calling it.
- Two computations that nothing read — a Money Flow average in Cipher B and a Bollinger upper
  band in the Top/Bottom detector, both a full pass over the series on every recalculation —
  are deleted.
