# What's New

## 2.6.0 — the terminal you can hear yourself around

This release is about the two things a screen-reader user cannot work without: **the
terminal telling you what it just did**, and **the terminal staying out of your way while
you find out**. Both had holes in them, and one of the holes could cost real money.

*This file covers the current release only. Everything before it is in `CHANGES.md`.*

### Finding your way around the chart

- **Alt+Page Up and Alt+Page Down move between panes.** A pane is a Y axis — that is the whole
  definition of one. Candles and a price overlay share the Main pane because they share a price
  scale; volume gets its own because a volume axis is not a price axis. Pressing these used to
  announce **"No sub-panes in Candles"** on a chart that had three panes on it: the key was
  walking the strips declared inside one series while the chart divides itself up a level
  higher. It walks the panes now, and the pane's name is spoken at the end of the move.
- **Ctrl+Up and Ctrl+Down reach across the pane.** They walk the strip you are standing in — but
  now across every series in it, so from the candles they reach a price overlay drawn on top of
  them. Two lines against the same axis, in the same band, and until now no key that got from
  one to the other.
- **Page Up and Page Down go down the chart in the order you see it**, not the order the series
  happened to be added in. And all five of these keys now **stop at the ends** with a boundary
  earcon rather than wrapping silently around to the far side of the chart.
- **Shift+F1 names the pane you are actually in.** Alt+Page Down to the volume pane, press
  Shift+F1, and it answered "main pane" — from every pane that was not Main, because it was
  asking the focused component for a sub-pane name and calling an empty answer "main". It reads
  the same pane model the navigation keys read now, so the pane you moved to and the pane it
  names cannot differ, and it says where that pane sits: "Volume pane, 2 of 3". It also answers
  at all straight after a pane move — it used to go quiet in exactly that case, which is the one
  moment you are asking because the answer just changed.
- **`Alt+Shift+/` tells you where you are standing.** "Main pane, 1 of 3. Y axis, price: 94,000
  to 98,000, about 1,000 between gridlines. X axis, time: 3 June to 12 August, 60 bars at 1 day
  each. 2 series: Candles, Price." A number means nothing without the scale it sits against, and
  that scale is exactly what a sighted trader takes off an axis without noticing they did it.
- **The pane name and "hidden" / "muted" come at the END of what is spoken now**, and only when
  they change. They were leading every utterance, which pushed the thing you actually asked for —
  the value — to the back of the queue on every single bar.

### Two things that are gone

- **Split view is gone.** It put a second chart beside the one you were working on, and that
  second chart was read-only by construction: the keyboard, the speech and the sonification all
  stayed on the first one. So the terminal drew a chart it could tell you nothing about — which
  makes it a feature for the person looking over your shoulder. Comparing two markets is worth
  having and it is coming back as an **overlay**: a second symbol drawn on the same axis as an
  ordinary series, which Page Up already reaches, the arrows already walk, and the sonifier
  already plays.
- **Alt+Up / Alt+Down pane scrolling is gone.** It scrolled indicator panes off the top of the
  drawing and nothing else — navigation, speech and sonification never knew it had happened, so
  every pane stayed reachable and every key kept working. It was a scroll bar for a viewport you
  do not have, and all it ever said was "Scroll panes up".

### Asked before it is done

- **"Reset paper account" asks you first.** One click used to erase the balance, every open
  position, every working order and the whole trade history — no undo, from a button a Tab
  away from ordinary checkboxes on the busiest tab in Settings. It now arms a confirmation
  and tells you exactly what is at stake before it tells you which keys answer: *"Reset the
  paper account? This erases the balance, every open position and the whole trade history,
  back to 100,000. It cannot be undone. Confirm reset, or cancel."* Focus lands on Confirm,
  and whichever way you answer it comes back to where you started. Escape backs out of the
  question rather than closing Settings; one more Escape closes as usual.

### The one that could cost you money

- **The order you had read back to you was not necessarily the order that went to the
  venue.** Arming the live-order review reads your ticket aloud — the size, the symbol,
  the side, the price — and then Confirm sends it. For a sighted trader the ticket is
  still on the screen under the button; for you, **that spoken review *is* the ticket**,
  the only rendering of the order you get before it is real. It could go stale without
  saying so. Arm a review at 1 BTC, Tab back, change the quantity to 5, press Confirm, and
  five went. Four other routes did the same thing quietly: the Buy/Sell buttons, the
  size-from-risk button, and — the worst of them — the chart moving to a different symbol
  underneath an armed review. A fifth was found on the way: the armed state survived
  closing and reopening the dialog, so you could arm a review on BTC, press Escape, load an
  ETH chart, reopen the ticket and find Confirm already waiting with nothing spoken and
  nothing on screen, one Enter away from an order nobody had reviewed. Every one of those
  now voids the review and says so, and underneath them there is a backstop that compares
  the whole order at the moment you confirm. What was read aloud is what is sent.
- **With speech off, the terminal spoke every rejection and no confirmation.** The
  read-back for a real-money order was travelling on the channel F2 mutes. It now rides
  the same tier as fills and stop hits — the one that never goes quiet.

### The terminal stopped crashing

- **Both public heads were taking about two crashes a day, and had been for a month.**
  Twenty in thirty-one days, always the same address, always the chart renderer: two
  drawing objects were being reused across frames while another thread was disposing them.
  They are created and thrown away per frame now. The chart is the thing you are here for;
  it should not take the tab with it.

- **Alt+O — the object tree — locked the tab up.** The moment there was anything on the
  chart to list, opening it started a loop between the dialog and the browser that never
  ended. The dialog and the browser were each answering the other's question about whether a
  section was open. Fixed, and the reason both test suites had been green through it is
  worth saying: every automated check opened that dialog on an empty chart, where there is
  nothing to list and nothing to loop.

### Speech that actually arrives

- **The terminal would do the thing and not say it.** Mute a component, hide one, cycle a
  drawing's anchors — the action happened, the sentence often did not, and it was
  intermittent enough to feel like your screen reader misbehaving. It was not. The status
  strip at the bottom of the page had become a **second announcer carrying the same
  sentence**, and when its copy reached the screen reader first, the screen reader threw
  away *both* — the first as lower priority, the second as a duplicate of the first. This
  was measured on the accessibility bus, not guessed: six times out of sixteen. The strip
  is still there, still showing the last thing spoken as text you can read; it just stopped
  announcing.
- **The bottom of the page read as three lines, the first one blank.** Two of them were the
  invisible buffers the terminal speaks through, holding their last sentence forever. They
  are cleared a couple of seconds after speaking now, so browsing to the bottom of the page
  gives you one line, not three.
- **Auto-narration said up to nine things at once and you heard one.** A single scan could
  fire nine separate announcements in one breath; on the web version only the last survived
  and on the desktop you got an unstoppable queue of nine. One scan is now **one sentence**,
  most consequential first — a level that has ceased to exist, then price changing side of
  one, then the indicator's own signal, then a re-test, then an approach, then oscillator
  commentary last — capped at five clauses so it can never run for twenty seconds.
- **Playback had never said a word.** Up to eight minutes of the terminal's richest output
  with no sentence anywhere: Space started tones with no word about what was playing or
  from where, pause parked in silence, the speed keys changed the speed without saying so,
  and when the last bar sounded the tones simply stopped — the same sound as a crash, a
  dropped feed, or a muted chart. It now names what it is playing, from which bar and how
  many; says where it paused; and says **"finished"** or **"stopped"** as two different
  words, so you know whether the whole range sounded.

### Playback is a narration mode now

- **F3 was killing playback outright.** Turn chart sonification off, press Home and then
  Space, and you got about a second of audio and then "playback stopped". Measured: two
  bars out of two hundred. Something in the plumbing was cancelling the playback ten times
  a second. Fixed, and fixed the way it should have worked in the first place — **with F3
  off, the chart still plays; it just plays silently**, the cursor walking bar by bar while
  speech does the talking.
- **Playback speaks signals now, not only the date.** While it runs you hear three things,
  composed into one sentence per bar so nothing cuts anything else off: where you are in
  time (the new hour, day, month or year as the tones cross it), any **signal** printing on
  the bar just reached from the series you flagged with Ctrl+Alt+Shift+N, and any **chart
  formation** resolving there. Discrete signals only — never crossings, zone changes or
  oscillator chatter, which at ten bars a second would be a wall of speech. A second signal
  arriving inside the two-second window is dropped rather than queued, because a queue means
  hearing about a bar the tones passed eight seconds ago.
- **A new Narration tab in Settings (F12) — eight tabs now.** The rule is worth learning
  because it tells you which tab to go to: **Speech is how the terminal says what you asked
  for; Narration is what it says when you pressed nothing.** Narration holds "Announce new
  bars" (moved there from Speech), "Narrate signals on bar close" — the master switch over
  Ctrl+Alt+Shift+N, so you can have an hour of quiet without un-flagging six indicators and
  then having to remember which six — and "Narrate during playback". Both new switches are
  on to begin with, because on is what the terminal already did; what is new is that you can
  turn them off. Turn playback narration off and playback is tones and nothing else.

### Drawings you can hear, and move

- **Your drawings play with the chart.** A trend line, a channel, a Fibonacci retracement —
  they used to be silent during playback while every indicator sounded. They play now, told
  apart from an indicator by **timbre**: a drawing carries a breath of pink noise that an
  indicator's voice does not, so you know what you are hearing without being told. A bar the
  drawing does not reach is silent rather than skipped, so the shape of the gap is audible.
- **A trend line stops where you anchored it.** It used to run to the edge of the chart in
  both directions regardless.
- **Arrowing along a drawing says where you are on it.** "64,900, 30% along, price above" —
  the value, your position on the line, and where price sits relative to it.
- **Anchors can be nudged from the keyboard.** `Shift+Arrow` moves the selected anchor;
  `Ctrl+Alt+Shift+G` cycles which anchor is selected **and moves the cursor to it**, which
  it did not before. And when the nudge cannot work — the chart does not have focus, or a
  dialog is open — it now says why, once, with an earcon on every press, instead of doing
  nothing at all.
- **Drawings are named in words.** "Trend line (2)", not "TrendLine (2)", which is not how
  any voice should have to read a name aloud.
- **Drawings stopped freezing at the live edge.** A trend line placed on a live chart said
  "no data" from the next bar onward.

### Dialogs behave like dialogs

- **Shift+Tab used to walk out of every one of the twenty-five dialogs** onto a control
  behind them — while the dialog was telling your screen reader not to describe anything
  back there. The trap was testing the wrong thing. Fixed everywhere at once.
- **The whole application behind a dialog is switched off now.** `aria-modal` is advisory:
  it asks a screen reader to ignore the background and does nothing whatsoever about focus,
  and with a real screen reader attached, six of fourteen dialogs lost focus to somewhere
  outside themselves. The background is now genuinely inert — the toolbar, tab bar, chart,
  status bar and footer refuse focus outright until the dialog closes.
- **The Help dialog could not be read by keyboard.** Four hundred lines of reference between
  two focusable elements, and the scroll keys were being swallowed. It also gained real
  headings and landmarks: its eighteen sections were bold text, invisible to every
  jump-by-heading command your screen reader has.
- **Function keys were dead in every text field.** F1 through F12 did nothing while your
  cursor was in a box. They work now — except Shift+F10, which stays native because the
  context menu it opens is worth more there than the command.
- **Buttons stopped vanishing.** Thirty-four buttons used to disable themselves when they
  could not act, and a disabled button is not "greyed out" to you — it is **deleted**. They
  stay reachable and **refuse out loud**: "No chart is loaded. Load a symbol first." Sign-in
  fields mark themselves invalid when they are actually rejected, and no longer announce
  three errors on a form you have not touched yet.
- **Every toolbar button had silently lost its tooltip** — the spoken description was being
  suppressed by an empty description that pointed at nothing. All back, and the whole top
  toolbar now follows one rule: the label you see is the name you hear, with the chord
  spelled out in words rather than as "+".

- **Escape now means the same thing in every dialog: close, and throw the changes away.**
  Settings had one button on it reading *Close*, and closing is what saved — the button
  saved, Escape saved, clicking the background saved. Two keystrokes away, the properties
  dialog has the same shape and has always discarded on Escape, and nothing told you which
  one you were in. **Settings has Save and Cancel now**, and every dialog that can commit
  uses that one word, *Save*.
- **Escape stopped taking a shortcut past a dialog's own Cancel.** On the label dialog that
  was visible: Escape in the text box left the label placed and said "Label left empty";
  Escape on the Cancel button did neither. Same key, two outcomes, decided by where your
  cursor happened to be.
- **The custom scripts dialog had a Save button that did not save.** Nothing in the app could
  have written those scripts to disk — the piece that was supposed to had never been built —
  so every script you wrote died when you closed the terminal. They are kept with the rest of
  your settings now, and Save tells you it worked.
- **Save Patch in the sound designer was silent.** In a dialog about sound, the one thing
  that made no sound was the confirmation that it had done anything.
- **The object tree offered a "Manage Strategies" button** that had nothing to do with the
  object tree and closed it out from under you on the way. Strategies are on the toolbar and
  on Alt+S.

### Settings, tidied

- **Save is what saves, and Escape is Cancel.** Fifteen controls in this dialog used to write
  themselves the instant you touched them — background monitoring, the poll interval, live
  background tabs, resume-session, the sound theme, magnet snap, market structure on new
  charts, the touch toolbar, sound under the mouse, which voice speaks, quick-trade sizing,
  where timestamps are spoken, value order — so pressing Cancel took none of them back. Three
  more, including "Speech enabled", changed the running application straight from the
  checkbox. Everything waits for Save now. Save writes them all and says **"Settings saved."**;
  Cancel, Escape or a click on the backdrop drops them all and says **"Settings discarded."**
  You can change your mind about anything in here.
  - **The Appearance tab is the deliberate exception.** The theme, the text size and the
    visual accommodations still apply the moment you pick them, because a visual setting you
    cannot see while you decide is one you cannot judge — and Cancel now puts those back too,
    and says so.
  - **"Draw chart formations" never remembered its own setting.** Reopening Settings always
    showed it unticked, whatever the chart was actually doing.
- **Eight tabs: General, Speech, Narration, Sonification, Appearance, Keyboard, License,
  About** — and the search box at the top still finds any setting by name and jumps to it.
- **Alert delivery moved out of Settings and into the alerts dialog** (Alt+J, then *Delivery
  settings*), next to the alerts it delivers. The webhook names you define there are the
  choices in an alert's Webhook list, and having the two a dialog apart meant abandoning the
  alert you were writing to go and define its destination. It also now **saves as you type
  each field** rather than on close — Escape is how a keyboard user leaves a dialog, and
  Escape was throwing away the SMTP password you had just typed without saying so.
- **Appearance is one Theme panel with New, Clone and Edit.** A theme owns its colours now.
  The chart background, gradient and bullish/bearish overrides that used to sit on top of
  *every* theme are gone; clone the theme you use and set them there instead.
- **The theme editor refuses colour pairs it cannot read.** It computes the real contrast
  ratio, names the pairs that fail with their numbers, and leaves your choice alone rather
  than quietly correcting it.
- **Twenty-four controls in the indicator properties dialog had no name at all** — the whole
  sonification section read as unlabelled edit boxes. Named.
- **"Describe candle patterns" is a switch now, and sits next to "Describe chart patterns"
  on General, under Analysis.** They are different things — a candle pattern is one to three
  bars (engulfing, harami, doji, hammer), a chart formation is tens of them (double top,
  head and shoulders, triangle) — and until now only the second had a switch while the first
  was spoken whether you wanted it or not. The Narration tab even promised it in a sentence
  nothing could make untrue. Candle patterns stay on by default; what is new is being able
  to turn them off.
- **Export and import moved to the tab whose settings they write** — the visual profile is on
  Appearance, the audio profile on Sonification, instead of both sitting in a box on General.

### Reading the chart

- **Shift+F1 tells you when there are two charts on screen.** The orientation key is what you
  press to ask where you are, and with split view on it answered as though the second chart
  were not there. It now names the split, says which chart is in the other half, and — the
  part that matters — says the keyboard is on this one. (The second pane is still a reference
  view only. Whether it stays that way or becomes something you can navigate into is an open
  question in `docs/TODO.md`, and the answer may be to remove it.)

- **The arrow keys name candle patterns now — including the multi-bar ones.** Engulfing,
  harami, piercing line, dark cloud cover, tweezers, morning and evening star, three white
  soldiers, three black crows. The terminal had detected all twelve for months, and would say
  them on the bar that closed and on the live bar as it formed — but neither route that reads
  the PAST ran the detector, so on any bar you were not present for, "three white soldiers"
  could not be said. Reading a chart by ear is almost entirely reading the past.
- **Twenty-four candle patterns, up from twelve.** Three inside up and down, three outside up
  and down, morning and evening doji star, the abandoned baby, the three line strike, and the
  rising and falling three methods — the last of which is a five-bar shape the terminal
  previously had no way to even express.
- **Indicator names stopped reciting their settings.** Arrowing onto Cipher B read
  "Cipher B 9 12 60 50 14 …" — every parameter, unlabelled, every time. It reads "Cipher B" now.
  Change one and you hear what you changed, which is the only part that tells two copies of the
  same indicator apart.
- **Playback reads the signal, not the indicator's name in front of it.** "Bull signal at 141.00"
  rather than "Cipher B: Bull signal at 141.00", every few seconds, for the whole run. The name
  comes back the moment it is doing work — when two different indicators fire on the same bar,
  each is named, because that is the case it was there for.
- **"Speak time landmarks during playback" is its own switch** on the Narration tab. The date or
  hour as playback crosses a boundary is the only thing that says where in time the tones are —
  and it is also a calendar read to you every few seconds. On by default; turning it off keeps
  the signals and the formation outcomes.
- **Playback tells you when it is about to say nothing.** Add Cipher B or Cipher SR, press play,
  and you heard tones and no signals — because narration is switched on per series and nothing
  said so. It now tells you once, when you press play: *"No series is set to narrate, so signals
  will not be spoken. Press Control Alt Shift N on a series to turn its narration on."* Only when
  there is actually something you are missing.
- **Every bar of a multi-bar pattern tells you where in it you are.** A three-bar shape can
  only be recognised on its last bar, so hearing "three white soldiers" once told you a pattern
  was there and nothing about which candles it meant. Now the first soldier reads *"Bullish, bar
  1 of 3, Three white soldiers"* and the last reads *"Three white soldiers, bar 3 of 3"* — so you
  can find the whole thing by ear. (Only when reading history. The live bar never claims to be
  part of a pattern that has not finished forming.)
- **Ctrl+Shift+D (Alt+Shift+D on the web) names them too, with the span and the lean.**
  *"Three white soldiers, 3-bar continuation. Body 58%, upper wick 25%, lower wick 17%."*
  Hearing "morning star" on one bar gives you no clue that the two bars behind the cursor are
  part of it, so the detail key says how many.
- **The same bar cannot be described two ways any more.** There were three candle classifiers
  with three sets of numbers: a 92% body was a marubozu when you scanned onto it and an
  ordinary candle when it closed. Worse, only one of the three looked at the trend — and a
  hammer and a hanging man are the SAME candle, told apart only by the trend they interrupt,
  so the other two called every one of them a hammer. That is not a shade of difference: it
  announces the opposite direction to someone who cannot see the chart. One classifier now.
- **A pattern forming on the live bar is no longer compared against itself.** The live bar is
  replaced in place as it ticks, and the terminal was handing the pattern detector the
  previous version of the same bar as its predecessor — which a growing body "engulfs" by
  construction. It reads the bar before it now.
- **"Describe candle patterns" reaches the arrow keys**, which is what its description in
  Settings always said. Off leaves "Bullish" or "Bearish" and the prices. Ctrl+Shift+D always
  names the pattern: that key is you asking.
- **Hidden and muted are two different things, and both are reported.** Hide a component and
  mute it, then unhide it, and the terminal used to tell you it was "visible" — about
  something that is still silent, which sends you to press H again and hide it. Every
  component now says which of the two states it is in, both if both, and unmuting something
  hidden says "unmuted, hidden".
- **A chart formation's outcome is announced on the bar that closes it.** A neckline closed
  through, a triangle that aged out — in the same words the arrow keys use if you go back
  and stand on that bar.
- **The Object Tree follows your focus.** Arrowing to a row selects it, so the drawing you
  are standing on in the tree is the drawing your next command acts on. A collapsed node
  could also never be re-opened with the arrow keys; it can now.
- **The "Focused Indicator" dropdown could be empty on a chart full of indicators.** That
  list is how you pick which series the Hide, Mute and Properties commands act on, and it was
  the one part of the terminal that never watched the chart — so on a freshly opened session
  it could sit at nothing while the tab bar showed your symbol and the terminal announced the
  chart was ready. It follows the chart now.
- **A CSV you imported under a name with a space in it could never be charted.** "My Budget"
  came back as *"Invalid symbol 'My Budget' for My Data. The chart is empty."* — the check
  that stops a ticker being smuggled into a web request was being applied to files on your own
  disk. Every column-shaped dataset was blocked outright for the same reason. Both load now.

### Under all of it

Fifty-five new test files and 560 new automated tests since 2.5.0, taking the suite to 6,567
— and every fix above was proved by **putting the bug back and watching a named test catch
it**, not by reading the code and believing it. The browser harness that drives a real
Chromium against the real application grew to 207 cases, and can now load an actual chart, so
what it says about the Object Tree is about a tree with something in it; three JavaScript suites cover the
keyboard trap, the touch gestures and the object tree; and a check fails the build if the
documentation and the code ever disagree about a shortcut, a plugin count or a test count.
