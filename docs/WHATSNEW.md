# What's New

<!-- UNRELEASED. This file holds the CURRENT RELEASE ONLY; the section below is what the next
     tag will say, and it must be moved into CHANGES.md if a tag is cut without it. Check with
     `git diff <lasttag> HEAD -- docs/WHATSNEW.md` before every cut — this file has accumulated
     post-tag entries under an old heading before. -->

## Unreleased — the terminal stops telling you what you already know

If you had a one-minute chart open, your desktop got a notification every minute for a bar
close the browser had just read out to you. That is fixed, and fixing it meant writing down a
rule that had never been written down.

**Here is the rule.** Whatever happens on the chart in front of you is spoken here, the way it
always was. Everything else is a system notification — a bar closing on another tab you have
open, an alert or a fill on a market with no tab open at all, and, once you close the browser,
every terminal event there is. Minimising the browser counts as being here: the page is still
loaded and your screen reader is still reading it.

**Closing the browser now tells you what happens next.** A few seconds after the last tab goes,
a notification says the terminal is still running and what it will keep watching. If you have
not turned on "Keep monitoring when the browser is closed", it says *that* instead, and names
the switch — the moment you are about to lose the ability to check is the moment to be told.
Reloading the page does not trigger it, and closing three tabs together produces one, not three.

**Three switches became one, and it is on by default.** "Alerts that fire", "Order fills" and
"New bars" each defaulted to off and lived in a different dialog from the thing they controlled.
That is how a working feature came to be reported as broken a few weeks ago — it was simply
switched off. There is now a single **Events you cannot see** switch in Alt+J → Delivery
settings, on out of the box, with the timeframe floor under it for when a fast chart gets chatty.
If you had deliberately turned all three of the old ones off, you stay quiet.

**The switches reach the background monitor now.** They did not before, and nobody had noticed:
the browser-closed half read your settings file once, at its first check, and never again — so
ticking a box in the tray or in Settings did nothing at all until you restarted the terminal,
while three separate comments in the code promised the opposite.

**On the Windows app, the X button now minimises to the tray by default.** The terminal keeps
running in the notification area, says so when it hides, and from then on everything — including
the chart you were looking at — arrives as a notification, because nothing is being read aloud
on screen any more. Restore or Quit from the tray icon. Settings → General → "Minimize to tray
on exit" turns it back off. *This one has never been run on a Windows machine; it compiles only.*

**Two things that only went wrong when you resumed a session.** When the terminal reopened your
workspaces, it did not actually record which symbol was on screen until you pressed Load Chart —
the chart was right, the bookkeeping was blank. Because of that, the background monitor believed
no browser was watching your focused chart and announced its every bar close as a system
notification, and, less visibly, **every alert scoped to that symbol was skipped by the
in-browser pipeline**. Both are fixed, on the resume path and on tab switches alike.

**A notification that fails to appear is now spoken instead.** If your notification daemon is not
running — no D-Bus session, a service started before the desktop — the terminal used to send its
notification into nothing and log a success. Everything you could not see was simply lost. It now
checks whether the notification actually arrived and says it aloud when it did not.

**Other open tabs now get the full narration.** A bar closing on a workspace you are not looking
at used to give you two clauses; the same chart with the browser closed gave you the whole ladder.
Closing the browser told you more than leaving it open. They match now.

**The background monitor tells you when IT is broken.** If its polling starts failing — a provider
down, a bad key — it says so once, and says so again when it recovers. It used to fail quietly
forever, which sounds exactly like a quiet market.

**Every oscillator now has a pane of its own, and the RSI sounds like the RSI again.** If you
had RSI and MACD on the same chart, the RSI had gone almost flat — you could still hear the
texture but the line barely moved. Thirty indicators were sharing one pane, and a pane has one
scale: MACD on Bitcoin swings by hundreds while RSI lives between 0 and 100, so the RSI was
being drawn and played inside a sliver of MACD's range. Each of those indicators now gets its own
pane with its own scale. Your saved workspaces heal themselves the next time they load; two
copies of the same indicator still share a pane, because they are on the same scale. Alt+Page
Down walks one more pane per oscillator than before — that is the trade, and it is the honest
one. The Add Indicator dialog now says whether an indicator joins the price pane or gets its own.

**Bounded indicators now keep their full scale at every zoom.** RSI reads 0 to 100 whatever is
on screen, and RSI 70 is the same note on every chart at every zoom level — before, the pane
fitted itself to whatever was visible, so the same value sounded different from window to window
and the announced axis was "22 to 83". Fourteen indicators declare their natural bounds
(Stochastic, MFI, ADX, Williams %R, CMO, Aroon, Cipher B and the rest); anything unbounded, like
MACD or ATR, still fits to the visible values as before.

**Smaller things you may notice:**

- The tray's silence item now says what it actually silences — "Silence alerts and bar closes"
  — and **order fills always come through it**. Money is the one thing a silence must not
  swallow.
- The "shortest timeframe to announce" floor now quietens the narration ladder too. It used to
  silence the bar close and leave the chart reciting its indicators every minute anyway.
- **Ctrl+Alt+Shift+M** knows the browser-closed half exists. It used to say "Background
  monitoring is off" on a machine where it was running, because two different switches shared
  one sentence.
- If you loaded the same chart repeatedly, the market recorded against it grew — `Crypto|Spot`,
  then `Crypto|Crypto|Spot`, and on. It quietly made every load start from cold. Files already
  on disk repair themselves the first time you open them.
- A tab you closed used to go on sending your alerts for about three minutes — including a
  second email, a second Telegram message and a second webhook post. It stops immediately now.

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
