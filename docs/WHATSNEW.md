# What's New

<!-- This file holds the CURRENT RELEASE ONLY. The section below belongs to the tag named in
     its heading; anything written after that tag goes under a NEW `## Unreleased` heading above
     it, and the previous release's section moves out (its history is already in CHANGES.md).
     Check with `git diff <lasttag> HEAD -- docs/WHATSNEW.md` before every cut — this file has
     accumulated post-tag entries under an old heading before. -->

## Unreleased

- **The online demo opens on a chart.** Visiting the demo now lands straight on a live Bitcoin
  daily chart that already speaks, instead of an empty symbol box and four steps to get there.
- **A new tab no longer empties the Symbol box.** Opening a blank tab, or starting the app for the
  first time, used to clear the symbol you had selected, so Load Chart had nothing to load.

## 2.12.0 — the Windows client speaks, and the chart gets its window back

*Everything before this is in `CHANGES.md`.*

The desktop client had never been run with a screen reader. It was run on 2026-09-21, and
almost everything below is what that one session found. **If you use the Windows client, this
release is the one that makes it work.**

> **If you downloaded 2.12.0 on 22 September, please download it again.** The first build of
> this release was published without three of the files it describes below — the NVDA controller
> library, the Dot Pad SDK, and the script worker on the WebHost — so on that build the chart was
> still silent for NVDA users, the Braille tab still did nothing, and custom indicators still
> could not run on the server package. The three are vendor binaries that are not kept in the
> source tree; the build knew how to include them and nothing put them on the machine that made
> the release, so it succeeded quietly and said everything was fine. The zips have been replaced
> and the checksums have changed. The release now refuses to build at all if any of those files
> is missing, and refuses to publish if the finished zip does not contain it.

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
- **Custom indicators and strategies run** — on the WebHost as well as the Windows client. The
  script worker was missing from the download, so user-compiled code could not execute at all.
  On the server package it was missing for a second reason as well: that head had never
  referenced the worker in the first place.
- **Braille and Dot Pad support is actually in the download.** The tactile SDK had never been
  included, so the Braille tab rendered and the device could never connect. The SDK is a C++
  library, so the Microsoft runtime it needs travels with it too — otherwise it would still fail
  to load on a machine that happens not to have that runtime installed, with the Braille tab
  looking exactly as healthy as before.
- **The client starts faster again.** The Windows build had quietly stopped being precompiled
  between 2.11.0 and 2.12.0 — nothing in the project asked for it either way, and a default
  moved. It is asked for explicitly now, and the release checks the shipped files rather than
  the intention.
- **The chart no longer disappears in a small window.** Below a certain height the toolbar took
  the whole window and the chart was squeezed to nothing. It now keeps a minimum height and the
  page scrolls instead.
- **There is a log file**, at `%LocalAppData%\AccessibleTrader\logs\terminal.log`. The Windows
  client previously wrote no log of any kind, which is why these problems went unnoticed for so
  long.

### If you use the macOS app

- **The market dropdown lists every provider there too.** The Mac app had the same missing
  file as the Windows one, for a slightly different reason — it was written next to the app
  bundle instead of inside it — so all 33 providers were refused and only the built-in sources
  appeared.

### The chart, on every platform

- **Volume labels fit the chart again.** On a stock trading a hundred million shares a day the
  volume axis printed nine-digit numbers that ran off the right edge of the chart and were cut
  in half. Large numbers are now written the way a person says them — **120M**, **2.5B** — and
  only when the full number will not fit, so price labels are untouched.
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
  Tab reaches them after Help and before Market. The timeframe quick-picks moved to the end of
  the cascade row, after **Load chart**: Tab now goes Market, Provider, Symbol, Time, Load,
  with the provider's pills after — before, a provider with thirteen timeframes put thirteen
  tab stops between Time and Load, and on a narrow window the Load button wrapped onto a line
  of its own.
- **The price pane is the largest pane again, at every window size.** Two things conspired to
  make it the same height as each indicator pane: an 80-pixel floor under every indicator pane,
  and a fallback that gave up the price pane's 2:1 share the moment the floors did not fit. The
  floor is 60 now, and when a stack is crowded the price pane keeps its share — 40% with three
  indicator panes — instead of dropping to a quarter.
- **Focus mode.** Press **Alt+Z**, or the **Focus mode** button on the bar under the chart,
  and the toolbar, tabs, indicator bar and footer disappear; the chart takes their height. The
  status line stays and carries an **Exit focus mode** button. Both directions are announced.
  This is the feature F11 was standing in for: F11 only recovers the browser's chrome.
- **Formation labels stopped piling up.** Three drawn formations used to produce six labels, a
  name and a "target" each, stacked in the corner and half under the pane legend. Only the
  dominant formation labels its target and floor now, and every label steps clear of the
  legend. And a flag was being reported twice — once for the consolidation and again for the
  same consolidation one bar longer. It is reported once.

### If you use the hosted terminal

- **A dropped connection is announced.** When the circuit to the server was lost, a screen
  reader user was told nothing at all — the reconnect banner had no live region. It now speaks
  each attempt, says whether the server is restarting, and answers the question that actually
  matters: **orders already placed are held at the venue and are unaffected.**
