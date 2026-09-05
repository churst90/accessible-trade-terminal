# What's New

## 2.8.0 — the keys agree, the toasts arrive, and what you switched on stays on

2.7.0 made everything you switched on speak. This release is about what happens
around that: the switches now survive a restart, the desktop and the browser
agree about every key, the terminal can reach you when another window has
focus, and there are eight profiles where there were three.

*This file covers the current release only. Everything before it is in `CHANGES.md`.*

### What you switched on stays on

- **Narration and mute on a component survive a restart.** N on a component and M on a
  component were written to the workspace file and thrown away on the way back in — for as long
  as those switches have existed, "narrate only this component" lasted exactly one session.
  Everything is left as you had it when the terminal closed.
- **Hidden or muted means silent.** Hide a series or a component with H, or mute it with M,
  and it drops out of narration for as long as it is hidden — on bar closes and in playback
  alike — without touching the N flag, so showing it again brings its narration straight back.
  Most of the terminal already did this; the oscillator path did not, in two ways, and both are
  closed.
- **Market Structure is no longer added to new charts.** A new chart is candles, volume and
  price. Anyone who wants the overlay back ticks the box under Settings → General → Analysis.

### The keys agree on every head

- **The drawing tools and the detailed bar summary are Alt+Shift+letter everywhere.** They were
  Ctrl+Shift on the desktop and Alt+Shift in the browser, because browsers reserve most of the
  Ctrl+Shift row and the help had to explain the difference on every page. Same letter, same
  tool, one modifier, on the Windows and Mac apps and in the browser alike. A keyboard profile
  you saved before this release keeps its old chords on the desktop. One note for Windows users
  with two keyboard layouts installed: Windows switches layout on a bare Alt+Shift; the switch
  fires on release without a third key, so Alt+Shift+T should be safe, but tell us if it is not.
- **The F1 help, the quick start and the manual** all say Alt+Shift now, and the browser no
  longer needs a paragraph about being different.

### The terminal reaches you when it is not the window you are in

- **Desktop notifications**, under Alerts → Delivery settings, on the two heads that can show
  one: the local web host on Linux, where the notification goes to your desktop's notification
  daemon (MATE, GNOME and KDE all show it, and Orca can present it), and the Windows app, where
  it is a Windows toast that Narrator, NVDA and JAWS read. Three switches, each off until you
  turn it on: **alerts that fire**, **order fills, stops and take-profits**, and **new bars on
  the current chart**. Speech inside the terminal is unchanged; nothing here replaces it. New
  bars are one notification per bar close, so that switch is for hourly and daily charts. The
  hosted site keeps its browser-notification controls instead.

### Eight profiles, one idea

- **Five new profiles** in the Add Indicator dialog's Profile category. A profile is one choice
  of what to count — volume, or the time price spent at each level (TPO) — crossed with one
  choice of which bars: the **visible range** (recomputes as you pan), a **fixed range** (the
  stretch you were viewing when you added it, and it stays put), one **session** (the day of
  the last bar on screen, so panning into yesterday shows yesterday's), or **anchored** at the
  bar your cursor was on, running to the newest bar and growing as bars arrive. Three of the
  eight existed; all eight do now, and Properties reads a fixed range or an anchor back to you
  as a sentence.
- **A fixed-range profile keeps its range through Properties.** Pressing Apply used to rebuild
  the parameter list and quietly turn every fixed-range profile into a whole-history one.

### The toolbar and the dialogs

- **The Order book button appears only where there is a book.** On the nine venues that stream
  depth and on Interactive Brokers it is there; on a data feed with no book it is absent, the
  way Deposit is absent on a broker with no wallet. Alt+B still opens the dialog anywhere, and
  on a provider with nothing it says so.
- **Every chart tab has a real Close button** beside it, named "Close tab N, label", so a mouse
  or a screen reader's button list can close a tab without knowing that Delete does it.
- **Ctrl+Left and Ctrl+Right on the candles now say what they are for:** "Candles has no
  crossings to jump to. Draw a trend line and this key finds where price crosses it."
- **The footer says "Trading carries risk."**
