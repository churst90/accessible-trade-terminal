# What's New

## 2.9.0 — the line goes where the line means something

2.8.0 made the switches survive a restart. This one is about lines and where they
belong: the `0` key stops putting a reference line at the bottom of oscillators
that never go there, Ctrl+Left and Ctrl+Right can finally reach an RSI's 50 line,
braille has a settings tab of its own, and two buttons moved to where they belong.

Under all of that is a test that does not name a field. Seven times now, a switch
you set has been written to your workspace file and thrown away on the way back
in — and each time it was fixed one field at a time, by you noticing. This release
has a guard that checks every field at once, and it found four more nobody had
reported.

*This file covers the current release only. Everything before it is in `CHANGES.md`.*

### The 0 key marks the line that matters on the pane you are on

- **On an RSI, `0` now marks 50.** It used to mark zero — the very bottom of a
  0-to-100 oscillator, a value RSI simply does not visit. So the line could never
  be crossed, never made a sound, and could never be navigated to, and it was
  called "Zero" so the confirmation you heard agreed with the key rather than with
  the chart. The same was true of Stochastic, Stoch RSI, MFI and the Ultimate
  Oscillator; on Williams %R, which runs −100 to 0, zero is the *ceiling*, and it
  now marks −50.
- **On MACD and the other readings that swing about zero, nothing changed** — the
  line is at zero and it is still called Zero.
- **On the price chart, nothing changed** — the line goes at the price of the bar
  under your cursor, which is what marking a level means there.
- **Where the indicator already draws its own midline, you are told so** and no
  second line is added. RSI ships one at 50; two lines at one value would report
  every crossing twice.
- **Where a pane has no meaningful centre at all, the key says so** instead of
  guessing. Pressing `0` on volume used to put a line on the floor of the pane.
- **Pressing `0` again still takes the line back** — that has worked since August,
  and it now follows the line wherever the key put it.

### Ctrl+Left and Ctrl+Right reach the midline

Your RSI has always had a line at 50, and it has always played an earcon when
price crossed it. Until now the crossing keys could not take you to the bar where
that happened — they only knew how to find a line spelled "Zero", and sixteen
indicators spell that line four different ways between them. Nine of them were
invisible to the crossing keys. All sixteen are reachable now, and on a bounded
oscillator the midline sits alongside the overbought and oversold lines as
somewhere the key will stop.

Crossing 50 is usually the thing an RSI is being read for.

### Braille has its own tab

- **Shift+F4 opens Settings on a Braille tab, with focus already on the tab.** It
  used to open the dialog on General and say nothing about where it had put you —
  which was only ever right by coincidence, because the braille checkbox happened
  to live there.
- The tab sits with the other output channels: Speech, Narration, Sonification,
  Braille. It says in so many words that the Dot Pad is the only supported display
  and that a device picker will appear when there is a second one, rather than
  leaving you to wonder where the rest of it is.
- On the browser version there is no tab at all, because a Dot Pad connects to the
  machine running the terminal and not to your browser.

### Two buttons moved, and one came back

- **Drawing tools moved to the bar under the chart**, next to Add indicator and
  Scripts. That bar is for things you put on the chart; the top toolbar is for
  accounts, orders, workspaces and settings. A drawing belongs with the indicators.
- **The Order book button is on the toolbar again, always.** 2.8.0 hid it on
  providers with no depth feed, which meant Alt+B opened a dialog whose button had
  vanished — the one shortcut in the terminal whose control could disappear
  underneath it. A button that opens a dialog explaining itself is easier to live
  with than a button that is not there.
- **And the dialog now tells you which of two things is true**: that the venue does
  not publish an order book at all, or that it does and no depth came back just
  now. Those used to be the same sentence, and the message is announced rather
  than left sitting there for you to find.

### The switches that were still being dropped

A new guard checks every field of every saved object at once, instead of one test
per field that somebody noticed was missing. It found four on the day it was
written:

- **Market Structure's swing markers were drawn in the wrong place** — at the
  value, rather than above the highs and below the lows — for as long as the
  terminal has had a way to anchor a marker.
- **"Announce this series' signals from elsewhere"**, a checkbox in Properties, was
  written to your workspace and never read back.
- **Undoing a chart edit with Ctrl+Z reset four indicators' text settings** — a
  comparison symbol, an MA type, a pivot period, a threshold mode — to their
  defaults.
- A hand-picked colour lost the flag that protects it from the next theme change.

Alongside it, the terminal now writes down which parts of a saved chart belong to
you and which belong to the indicator, so that a field added in future has to be
put in one group or the other before it can ship.

### Under the hood

The keyboard layer and the alert layer had never been tested by deliberately
breaking them, which is the only way to find out whether a passing test suite
would notice. Both have been now — 27 deliberate one-line regressions, of which
seven slipped past the whole suite. All seven are closed. The one worth naming:
**F1 could have stopped working inside every dialog in the terminal and nothing
would have told us**, which for anyone who has ever pressed F1 because they were
not sure where they were is the wrong key to have go quiet.

Two of the seven were in the code that decides where an alert is allowed to send
itself, in a file that already had forty-six passing tests.
