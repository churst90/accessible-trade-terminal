# Accessible Trading Terminal — Keyboard Shortcuts

All shortcuts are sourced from `ShortcutManager.InitializeDefaultProfile()`. Shortcuts not listed here are not assigned by default. Users can customise bindings via the Sound Designer or by editing the shortcuts profile saved at `%LOCALAPPDATA%\AccessibleTrader\shortcuts.json` (MAUI heads) or `~/.local/share/AccessibleTrader/shortcuts.json` (Linux WebHost).

## Host-specific note: the drawing chords are `Alt+Shift+letter` on every head

Since 2026-09-05 the drawing tools and the detailed point summary are bound to `Alt+Shift+letter` in the DEFAULT profile, on the desktop apps and the browser alike. They were Ctrl+Shift+letter on the desktop before that, with the Linux WebHost rewriting them to Alt+Shift at startup because Firefox and most browsers reserve several Ctrl+Shift+letter chords at the browser-chrome level (reopen closed tab, history, private window, console) and they are NOT cancellable from page-level JavaScript even with `preventDefault`. That left the one place the keyboard differed between heads, and it was removed by moving the default rather than by explaining the difference.

| Drawing tool | Every head |
| --- | --- |
| Trend line | `Alt+Shift+T` |
| Horizontal line | `Alt+Shift+H` |
| Vertical line | `Alt+Shift+V` |
| Channel | `Alt+Shift+C` |
| Fibonacci retracement | `Alt+Shift+F` |
| Text label | `Alt+Shift+L` |
| Fibonacci extension | `Alt+Shift+E` |
| Andrews Pitchfork | `Alt+Shift+A` |
| Rectangle | `Alt+Shift+R` |
| Measure tool | `Alt+Shift+M` |
| Gann fan | `Alt+Shift+G` |
| Risk/reward | `Alt+Shift+P` |
| Anchored VWAP | `Alt+Shift+W` |
| Gann box | `Alt+Shift+B` |
| Angle Fibonacci | `Alt+Shift+J` |
| Detailed point summary | `Alt+Shift+D` |

A `shortcuts.json` saved before the change still carries the Ctrl+Shift chords and keeps working as saved on the desktop; the WebHost still rewrites any such legacy Ctrl+Shift+letter binding to Alt+Shift+letter in memory, never on disk. The Help dialog (`F1`) reads the live in-memory profile, so each host self-documents its current bindings. Chords with three modifiers (`Ctrl+Alt+Shift+N` and the rest) are unaffected. One platform note: Windows switches keyboard layout on a bare Alt+Shift press when two layouts are installed; the switch fires on release without a third key, so `Alt+Shift+T` does not trigger it, but that is the one thing to verify on a Windows box with two layouts.

Some browser-reserved chords are **single-`Ctrl` chrome chords** the browser handles before any page listener runs, so even capture-phase `preventDefault` can't stop them. On the WebHost these reserved bindings are **removed** (so the Help dialog never advertises a chord the browser eats) and the in-app action is rebound to a web-safe equivalent:

- `Ctrl+T` (AddTab) — opens a browser tab. On the web use **`Alt+Shift+N`** (a chord browsers leave alone) or the tab bar's always-visible `+` button. (`Ctrl+T` still works on the desktop heads.)
- `Ctrl+W` (CloseTab) — closes the browser tab. Use a tab's `×` button, or focus the tab bar (`Ctrl+Alt+Shift+T`) and press `Delete`.
- `Ctrl+Tab` / `Ctrl+Shift+Tab` (SwitchTabNext/Prev) — switch browser tabs. On the web, press **`Ctrl+Alt+Shift+T`** (`FocusTabBar`) to move keyboard focus onto the workspace tab switcher bar, then use the arrow keys / `Home` / `End` / the number row (`1`–`9` jump to that tab); `Delete` closes the active tab and `Insert` / `+` opens a new one.
- `Ctrl+PageUp` / `Ctrl+PageDown` — cycle browser tabs, and nothing is bound to them on any head. Pane navigation is **`Alt+PageUp`** / **`Alt+PageDown`** everywhere, so the desktop and the browser agree; a reserved chord is left unbound rather than reassigned.

The workspace tab switcher bar (a row of tabs just above the chart) is always visible — even with a single tab open — so the `+` new-tab button is always reachable by mouse, and the bar is always focusable via `Ctrl+Alt+Shift+T`.

---

## Time Navigation (X Axis)

| Key | Action | Speech Feedback |
|-----|--------|-----------------|
| Left Arrow | Move cursor one bar back in time | Bar data at new position |
| Right Arrow | Move cursor one bar forward in time | Bar data at new position |
| Home | Jump to leftmost bar in visible viewport | Bar data at start |
| End | Jump to rightmost bar in visible viewport | Bar data at end |
| Backslash (\) | Jump to the latest (live) bar | Bar data at live edge |
| [ | Pan viewport left (older bars come into view) | "Viewing X bars from..." |
| ] | Pan viewport right (newer bars come into view) | "Viewing X bars from..." |
| Shift+[ | Decrease pan step size | — |
| Shift+] | Increase pan step size | — |
| - (minus) | Zoom out (more bars visible) | "Viewing X bars from..." |
| = (equals) | Zoom in (fewer bars visible) | "Viewing X bars from..." |

---

## Pane and Component Navigation (Y Axis and Series)

| Key | Action | Speech Feedback |
|-----|--------|-----------------|
| Page Down | Move focus to next pane/series below | "{Series Name}" |
| Page Up | Move focus to pane/series above | "{Series Name}" |
| Down Arrow | Move to next component within focused series | "{Component Name}, {value}" |
| Up Arrow | Move to previous component within focused series | "{Component Name}, {value}" |
| Ctrl+Down | Next component in the strip you are in, **across every series in the pane** (clamps) | "{Component Name}, {value}" |
| Ctrl+Up | Previous component in the strip you are in, across every series in the pane (clamps) | "{Component Name}, {value}" |
| Alt+Page Down | Move to the next PANE down — the next Y axis (clamps) | "{Series Name}. … {Pane} pane." |
| Alt+Page Up | Move to the previous pane up (clamps) | "{Series Name}. … {Pane} pane." |
| `Alt+Shift+/` | Describe THIS pane — axes, ranges, gridline step, what is in it | "Main pane, 1 of 3. Y axis, price: … X axis, time: …" |

All five traversal keys **clamp** at the ends with a boundary earcon; none of them wrap. A
silent jump from the bottom of the chart back to the top is the one outcome a user who cannot
see the move has no way to detect.

---

## Context-Aware Jump Navigation (Ctrl+Left / Ctrl+Right)

`Ctrl+Left` and `Ctrl+Right` perform a context-sensitive jump whose target depends on what component is currently focused. The behavior is determined by `CommandDispatcher.HandleTrendlineCrossJump`.

| Focused Component Type | Jump Target |
|------------------------|-------------|
| Price candle or candle wick | Next bar where price crosses a drawn trendline |
| Sparse marker (Dot, Diamond, Cross, Arrow, TriangleUp, TriangleDown, Square, ZeroDot) | Next bar where that component has a non-NaN signal value |
| Zero-crossing oscillator (MACD, Momentum, ZeroArea etc.) | Next bar where the oscillator crosses its **midline** — zero on a zero-centred reading, 50 on a bounded one. Recognised by the line's role, so all four spellings providers use for it (Zero, Midpoint, Midline, Neutral) are reached |
| Threshold oscillator (RSI, MFI, Stoch, CCI — any indicator with OB/OS levels) | Next bar where the indicator enters or leaves the overbought/oversold zone |
| Moving average overlay (EMA, SMA, WMA, Spider Lines etc.) | Next bar where price (close) crosses the focused MA line |
| Band indicator (Bollinger %B / PERCENTB) | Next bar where the indicator crosses the upper (1.0), mid (0.5), or lower (0.0) band boundary |
| No focus / unknown | Next trendline crossing (fallback) |

When no further event exists in the scan direction, speech announces: "No more [component name] signals in this direction." (This was silent until 2026-08-03 — boundary feedback carried the message but only played an earcon.)

---

## Chart Formation Navigation (`,` and `.`)

Bare comma and period step between **chart formations** — double tops, head and shoulders,
triangles, wedges, flags. They are unmodified keys because this is something you press repeatedly
while reading a chart, and they are chart-scoped, so they remain typable everywhere else.

| Key | Action |
|-----|--------|
| `,` | Previous formation edge |
| `.` | Next formation edge |

| `;` | Choose which overlapping formation leads the readout (press again for the next) |
| `Shift+;` | Stop choosing; go back to largest-first |

They require formation description to be on (Settings → General → *Describe chart patterns*). With it off they
say so rather than moving you across the chart without explaining why — the announcement that would
explain the jump is exactly what the setting disables.

**Choosing a formation.** When several shapes overlap, the terminal leads with the largest live one
and counts the rest. Semicolon overrides that with your own choice — the twelve-bar flag inside the
eighty-bar triangle may be exactly what your setup is built on, and the terminal has no business
insisting otherwise. The choice sticks per chart until you clear it, and nothing is hidden either
way: the others are still counted and `Alt+Shift+D` still reads them all.

**While a formation is chosen, `,` and `.` walk that formation's edges only** — its start and its
ending, and nothing else. Choosing a shape and then being taken to a different one's break bar was
the old behaviour and it made the choice feel as though it had not registered: you would hear
*"leading with ascending triangle"* and, one keypress later, *"double bottom confirmed here."* Both
sentences were true; the key had simply travelled somewhere you had not asked to go. When you run
out of edges the terminal names the pin and reminds you that `Shift+;` releases it.

**Nesting.** A formation inside a larger one says so — *"…Inside a larger double bottom that began
12 March."* The container's start date is given rather than just its name, because that is what lets
you go and find it.

---

## Quick Trade (from the chart, no dashboard)

Size a position from your risk budget and place it without leaving the chart.

| Key | Action |
|-----|--------|
| `Ctrl+Alt+Shift+1` | Arm 0.5% risk |
| `Ctrl+Alt+Shift+2` | Arm 1% risk |
| `Ctrl+Alt+Shift+3` | Arm 2% risk |
| `Ctrl+Alt+Shift+X` | Use the bar under the cursor as the stop |
| `Shift+Enter` | Place a limit at the bar under the cursor |
| `Ctrl+Enter` | Place at market |
| `Ctrl+Alt+Shift+Q` | Say what is currently armed |
| `Ctrl+Alt+Shift+0` or `Escape` | Cancel |

**The stop comes before the size, and that is not a formality.** A risk percentage is a *cash
budget*; it becomes a quantity only once the distance to your stop is known, because that distance
is what one unit can lose. So arming a percentage puts the system in "stop needed" and it refuses to
place until you have set one. What you get in return is the arithmetic a sighted trader does in a
position-size calculator — equity, risk, stop distance, quantity — spoken in one sentence at the
moment of the decision:

> *"Armed 1 percent. $1,000.00 at risk, stop 42,100, long 0.625 units, entry 43,700."*

**Direction is inferred, not asked.** A stop below the current price can only be protecting a long;
above it, a short.

**You are told you are armed on every bar you move to** — *"Armed 1 percent, ready."* Forgetting is
the failure this feature is designed against, so the reminder is short and unconditional. `Escape`
always cancels, and takes precedence over cancelling a half-placed drawing.

**The stop is always sent with the entry.** The size was derived from the stop distance, so a
position placed without it would have a quantity justified by protection that does not exist.

**The stops are edges, not formations.** Each one contributes two: the bar its structure first
became knowable, and the bar its story ended — the break, or the point it aged out unconfirmed.
Landing on those two bars walks you through the same narrative arrow-key navigation gives you, so
the jump keys and the arrow keys always agree about what is worth saying.

There is no equivalent for **candle** patterns, deliberately. Dojis, spinning tops and small-bodied
bars occur on a large share of every chart, so "jump to the next candle pattern" would usually mean
"move one bar right" — a key that does nothing you could not do with the right arrow, while
consuming a binding. Candle patterns are already read on the bar you are standing on, which is the
right place for something that common — and since 2026-09-04 that includes the multi-bar ones. The
arrow keys, the detail key, the bar close and the live forming bar all run the same classifier over
the same trailing window, so the four of them cannot name the same bar differently.

### What you hear

Formation narration is opt-in (Settings → General → *Describe chart patterns*) and is **edge-triggered**: a
formation speaks twice over its whole life, not once per bar.

| When | Example |
|------|---------|
| The formation's first bar | "Start of possible double top forming, neckline 42,100, measured target 39,400 if it breaks. Spans 22 bars." |
| Its resolution bar | "End of double top: price closed below the neckline at 42,100, measured target 39,400. Spans 22 bars." |
| The bar it confirmed | "Double top confirmed here: closed below the neckline at 42,100, measured target 39,400." |
| The bar it aged out | "Double top ends here without confirming — the neckline at 42,100 held." |
| Overlapping formations | "…Plus 2 more formations here." (`Alt+Shift+D` reads them all) |

Three properties are worth knowing, because each is a deliberate choice:

- **The edge is named, and it describes the BAR rather than your direction of travel.** A
  formation's first bar says "Start of" whether you arrowed onto it going left or right; its last
  bar says "End of" either way. If the word changed with direction, the same bar would describe
  itself differently depending on how you reached it, and no picture of the chart could be built by
  moving around in it.
- **An outcome is stated as what price did, never as a verdict.** The word "completed" is never
  spoken: it could not tell you whether the pattern worked or failed, and it never meant either —
  only that price closed through a line. So the narration names the side and the level instead.
- **Nothing is ever called bullish or bearish.** The measured target is spoken because it is
  arithmetic on numbers already on screen, and it is always framed as the *measured* target,
  conditional on *if it breaks*. It has never been tested here — see `ALPHA_LEDGER.md`, where every
  price-derived pattern claim tested so far has come back null.

---

## Bar Replay

Reveals loaded history one bar at a time so you can practise reading a market forward,
without hindsight. Live updates are suspended while replay runs and the full series is
restored when you stop.

| Key | Action |
|-----|--------|
| F11 or Ctrl+Alt+Shift+P | Start replay at the cursor bar / stop replay and restore full history |
| F9 | Reveal the next bar |
| Shift+F9 | Hide the last revealed bar |
| F10 | Play / pause auto-advance |

Also on the toolbar's second row as **Replay**, which shows its own pressed state.

On the WebHost, use `Ctrl+Alt+Shift+P` rather than `F11` — browsers own F11 for fullscreen
and page-level `preventDefault` on it is not reliable.

---

## Playback

| Key | Action | Notes |
|-----|--------|-------|
| Space | Play/Stop entire chart | All visible, unmuted series, bar by bar. Speaks "Playing chart from <first bar>, N bars." on start; if every series is muted or hidden it refuses and says so |
| Shift+Space | Play/Stop focused series | Only components of the focused indicator, from the cursor. "Playing <series> from <bar>, N bars." |
| Ctrl+Shift+Space | Play/Stop focused component | Single component only. "Playing <series> <component> from <bar>, N bars." |
| Ctrl+Space | Pause / Resume active playback | Cursor syncs to pause point; speaks "Paused at <bar>." / "Resumed." With nothing playing: "Nothing is playing." |
| Shift+Escape | Force-stop all playback immediately | Speaks "Playback stopped at <bar>." With nothing playing: "Nothing is playing." When playback reaches the last bar on its own it says "Playback finished at <bar>." with the boundary earcon |
| Shift+= | Increase playback speed | Spoken during playback too: "Playback speed: 1.5x" |
| Shift+- | Decrease playback speed | Spoken during playback too |

While playback runs the tones carry price and speech carries three things, composed into **one
utterance per bar** so nothing can cut anything else off:

1. **Time.** Each time the bars cross a calendar boundary one step coarser than the bar spacing
   (a new hour on minute bars, a new day on hourly, a new month on daily, a new year on weekly —
   the finest unit that keeps the announcements about two seconds apart at the current speed) the
   new period is spoken without interrupting.
2. **Signals**, from the series you flagged with `N` and only those — and, when you played one
   series or one component rather than the whole chart, only the ones inside what you played: a marker
   signal printing on the bar just reached, named with its series — *"Cipher B: bull signal at
   64,900."* Discrete signals only; never crossings, zone changes or oscillator commentary.
3. **A formation resolving** on that bar, if *Describe chart patterns* is on, in the same words
   the arrow keys use.

Items 2 and 3 share the landmark cadence: a second one inside the two-second window is **dropped,
not queued** — at ten bars a second a queue is a backlog about bars the tones have long passed. A
landmark is never dropped. All three are governed by **Settings → Narration → Narrate during
playback** (on by default); with it off, playback is tones plus only the start, pause, speed and
finish confirmations. Navigation readouts and viewport descriptions stay silent during playback;
F2 mutes all of it, and F3 silences the tones while leaving the words — playback as narration.

---

## Speech and Sonification

| Key | Action | Speech Feedback |
|-----|--------|-----------------|
| F2 | Toggle interactive speech (navigation values, zoom/pan, summaries — everything you asked for) | "Speech on/off" |
| Shift+F2 | Toggle event speech (alerts, monitoring, new bars — everything that happens to you). Order fills and stops break through unless you opt them in (Settings → Speech) | "Alerts and events on/muted" |
| F3 | Toggle chart sonification (navigation tones, playback) | "Sound on/off" |
| Shift+F3 | Toggle earcons. Order-outcome and error earcons break through | "Earcons on/muted" |
| F4 | Toggle braille / tactile display output ("Braille not available on this platform" where unsupported) | "Braille on/off" |
| Shift+F4 | Open the Settings dialog on its **Braille** tab, with focus on the tab (desktop only — the tab does not exist on the web head, where no display can attach) | — |
| Shift+F1 | Announce context summary (moved from F4 in 1.10). Names the focused series, the PANE it is in and where that pane sits in the stack, the strip inside it when you are standing in a component, and the drawing anchor a nudge would move. The pane comes from the same model the navigation keys read, so it cannot disagree with where Alt+Page Up / Alt+Page Down just put you | "{Symbol} on {Provider}, {Timeframe}. Focused on Volume, Volume pane, 2 of 3" |
| Ctrl+Alt+Shift+C | Focus chart area + announce context summary | "{Symbol} context summary" |

---

## Volume Controls

| Key | Action | Speech Feedback |
|-----|--------|-----------------|
| F5 | Component volume up (+10%) | "Component volume N percent" |
| Shift+F5 | Component volume down (-10%) | "Component volume N percent" |
| F6 | Series volume up (+10%) | "Series volume N percent" |
| Shift+F6 | Series volume down (-10%) | "Series volume N percent" |
| F7 | Master chart volume up (+10%) | "Chart volume N percent" |
| Shift+F7 | Master chart volume down (-10%) | "Chart volume N percent" |

---

## Indicator Visibility and Mute

`H` and `M` respect the last interaction context. If you last pressed Up/Down (component navigation), they apply to the focused component. If you last pressed Left/Right (bar navigation), they apply to the whole series.

| Key | Action | Speech Feedback |
|-----|--------|-----------------|
| H | Toggle visibility of focused series or component | "{Series/Component} visible/hidden" |
| M | Toggle mute of focused series or component | "{Series/Component} active/muted" |
| N | Narrate the focused series or component — speak its signals unprompted | "{Series}, narrating" / "{Component}, narrating" (the component alone; "{Component} only, narrating" when it is the first one picked out) |
| 0 (zero) | Add or remove a reference line on the focused series. On an **oscillator** pane the line goes on that pane's **neutral** — zero for MACD and the other zero-centred readings, **50** for RSI, Stochastic and MFI, **−50** for Williams %R — because zero is only the meaningful constant where the value actually swings about it. Where the indicator already declares its own midline (RSI ships one at 50) you are told so and nothing is added. On the **price** pane there is no meaningful constant at all, so the line goes at the price under the cursor. Press `0` again where one of **your** levels sits and it is removed — indicator-declared levels are never removed this way. New levels report crossings from either direction straight away. | "Level added at 63,920.11, audible on crossing." / "Midpoint added at 50, audible on crossing." / "Midpoint already marks 50 on this pane." / "Level removed." / "Nothing on this pane declares a neutral line, so there is no level to add." |
| Delete | Remove the focused indicator series (candles are protected) | Confirmation |
| Ctrl+Z | Undo the last chart edit — a moved drawing anchor or a deleted series | Says what was undone, or "Nothing to undo" |
| Ctrl+Y | Redo the last undone chart edit | Says what was redone, or "Nothing to redo" |

---

## Chart Display Toggles

| Key | Action |
|-----|--------|
| Alt+C | Toggle Heikin-Ashi candle mode |
| Alt+L | Toggle logarithmic (log) scale |
| Alt+H | Toggle volume heatmap overlay |

---

## Indicator Management

| Key | Action |
|-----|--------|
| Alt+A | Open the Add Indicator dialog |
| P | Open indicator properties dialog (parameters, audio, visual settings) |
| Shift+F12 | Open indicator properties dialog (alternative to P) |

---

## Market Structure

The Market Structure overlay (HH/HL/LH/LL) is added to new OHLCV charts by default. Turn it
off in Settings, or delete the series from the Object Tree (Alt+O) to drop it for the session.
Navigate to its Structure State component and press Shift+F1 (context summary) for the full structural read: current
state, last swing high and low, and where price sits between them.

---

## Analysis

| Key | Action | Speech Feedback |
|-----|--------|-----------------|
| Alt+Shift+D (web: Alt+Shift+D) | Full candle analysis for the current bar — **including the multi-bar patterns** (engulfing, harami, piercing line, morning and evening star, three white soldiers, three black crows) with how many bars they span and whether they read as reversal or continuation — plus indicator values, **plus every chart formation the cursor sits inside** with its trigger and measured target. Never silenced by *Describe candle patterns*: this key is you asking | Spoken summary |
| Ctrl+Alt+Shift+N | The same as `N`, but works with focus outside the chart. Picks WHAT speaks; Settings → Narration decides WHEN (bar close, playback); the scope you played decides WHICH of them | "Narration on/off" |
| Ctrl+Alt+Shift+A | Open the AI Analyst modal | — |

---

## Tabs and Workspaces

| Key | Action |
|-----|--------|
| Alt+Shift+N | Add a new chart tab (works on every head; the web-safe new-tab chord) |
| Ctrl+T | Add a new chart tab (desktop only — browser-reserved on the web) |
| Ctrl+W | Close the current chart tab (desktop; on the web use a tab's `×` button, or focus the bar and press Delete) |
| Ctrl+Tab | Switch to the next tab (desktop only — browser-reserved on the web) |
| Ctrl+Shift+Tab | Switch to the previous tab (desktop only — browser-reserved on the web) |
| Ctrl+Alt+Shift+T | Focus the workspace tab switcher bar (web-safe path to tab switching) |
| Ctrl+Alt+Shift+W | Save the current workspace (all tabs + layout) |
| Ctrl+Alt+W | Load a saved workspace |
| Ctrl+Alt+Shift+L | Load the chart for the toolbar's selected market/provider/symbol (same as the Load button, including the shape-change warning) |
| Ctrl+Alt+Shift+I | Import / manage My Data CSV datasets (also the Import button on the toolbar when the My Data market is selected) |
| Ctrl+Alt+Shift+M | Speak background monitoring status (which tabs are watched, data freshness, armed strategies) |

The tab switcher bar (a row of tabs just above the chart) is always visible, even with a single tab open, so the `+` new-tab button is always there. When the bar has focus (via `Ctrl+Alt+Shift+T`, or by clicking it): `←`/`→` (or `↑`/`↓`) switch tabs, `Home`/`End` jump to the first/last tab, the number row `1`–`9` jumps to that tab, `Insert` / `+` opens a new tab, and `Delete` closes the active tab. Each tab also has a **Close tab** button beside it (a real button, so a screen reader's browse mode finds it; it is not a Tab stop, because Delete already answers from the keyboard). It is an ARIA tablist — your screen reader announces the newly selected tab as you move.

---

## Drawing Tools — Sequential Anchoring

All drawing shortcuts use **sequential anchoring**: there is no separate "mode" and no Enter key. Each anchor is set by **pressing the same tool shortcut again** at the current cursor bar. The `DrawingInteractionManager` state machine advances one anchor per press.

1. Navigate to the first bar with the Left/Right arrows.
2. Press the tool shortcut (e.g. `Alt+Shift+T`) to set anchor 1 at the current bar. Speech announces the price and timestamp and prompts: "Navigate to next point and press the shortcut again."
3. Navigate to the next bar.
4. Press the **same** shortcut again to set anchor 2. For two-anchor tools this completes the drawing and speech confirms placement.
5. For three-anchor tools (Fibonacci extension, Risk/Reward, Andrews' pitchfork), press the shortcut once more for anchor 3.
6. Press Escape at any time to cancel the in-progress placement.

Single-anchor tools (horizontal line, vertical line, text label, anchored VWAP) complete on the first press.

> The default profile also keeps a mouse path (click/drag to place, drag a handle to reposition), but the keyboard re-press flow above is the canonical accessible path. **Enter/Return is not used for drawing** — the `ConfirmCoordinateEntry` command is reserved/unused and has no key binding or dispatch handler.

| Key | Tool | Anchors Required |
|-----|------|-----------------|
| Alt+Shift+T | Trendline | 2 |
| Alt+Shift+H | Horizontal line (price level) | 1 |
| Alt+Shift+V | Vertical line (time marker) | 1 |
| Alt+Shift+C | Price channel (two parallel lines) | 2 |
| Alt+Shift+F | Fibonacci retracement | 2 (swing high and swing low) |
| Alt+Shift+E | Fibonacci extension | 3 (move start, move end, pullback) |
| Alt+Shift+L | Text label | 1 |
| Alt+Shift+R | Rectangle | 2 (opposite corners) |
| Alt+Shift+M | Measure / range tool | 2 |
| Alt+Shift+A | Andrews' pitchfork | 3 |
| Alt+Shift+G | Gann fan | 2 |
| Alt+Shift+B | Gann box | 2 |
| Alt+Shift+J | Angle / Fibonacci angle | 2 |
| Alt+Shift+P | Risk/Reward tool | 2 (entry and stop loss) |
| Alt+Shift+W | Anchored VWAP | 1 (the anchor bar) |

| Key | Drawing Placement Action |
|-----|--------------------------|
| (re-press the tool shortcut) | Set the next anchor at the current bar position |
| ContextMenu / Shift+F10 | Open the context menu for the focused drawing |
| Escape | Cancel the active in-progress drawing |

### Moving an existing anchor from the keyboard (the nudge)

Focus the drawing with `Page Up` / `Page Down` (or from the Object Tree, `Alt+O`). One of its
anchors is *selected* — the first, until you change it — and these keys move it:

| Key | Action |
|-----|--------|
| Shift+Left / Shift+Right | Move the selected anchor one **bar** earlier / later. A bar, not a day: weekends and halts are stepped over, and past the last bar the anchor projects into the chart's right margin ("June 30, 3 bars past the last bar"). |
| Shift+Up / Shift+Down | Move the selected anchor's price by 1% of the visible price range — the step scales with your zoom, like a mouse drag — and never by less than one unit in the last spoken decimal. |
| Ctrl+Alt+Shift+G | Select the next anchor of the focused drawing. The first press on a newly focused drawing only *says* which anchor is selected; the next moves on, and it wraps. |
| Ctrl+Alt+Shift+B | Snap the selected anchor's price to its bar's high, low, open or close: the nearest first, then the others in that order on repeated presses. A price-only anchor (a Fibonacci level) snaps to the cursor bar. |
| Shift+F1 | The context summary also names the selected anchor — the only way to hear it without moving it. |

What you hear: a short tick while the anchor is moving (at most five a second, however fast
the key repeats), then ONE sentence when you stop — *"End: 105.20 at June 15, 09:30. Trend line 2, anchor 2 of 2."* — value
first, then the drawing and which anchor of how many. A key that cannot act (an anchor at
the first bar, a price-only anchor asked to move in time, no drawing focused) plays the
boundary sound while the key is held and says why once. So does a chord pressed where it
cannot act: with focus on a toolbar button or the page, *"The chart does not have focus.
Control Alt Shift C returns to the chart."*; under a dialog, *"Not while Properties is open.
Escape closes it."* The one dialog the nudge works UNDER is the Object Tree (`Alt+O`), because
that is where you focus a drawing — pick it in the tree, then nudge it without closing the
tree. A run of nudges is a single `Ctrl+Z`.
Exact values are typed in **Properties** (`P`), which has a price and date field for every
anchor. The same six actions are in the drawing's context menu (`Shift+F10`) for voice
control, switch access and single-pointer use.

Three platform notes. `Shift+Arrow` is **select by character** in every text box and in a
screen reader's browse mode, so the terminal deliberately leaves it alone whenever the focus is
in an `<input>`, `<textarea>` or `<select>` — the nudge fires when the chart itself has focus,
or under the Object Tree, and says so anywhere else. (It moved here from `Alt+Shift+Arrow` on 2026-09-03: Orca claims that chord for
table-cell navigation, so on Linux with a screen reader running it never reached the app.)
On **macOS with VoiceOver**, the `Ctrl+Alt+Shift+letter` family is VoiceOver's own modifier
(Control+Option) plus Shift when the modifier is set to Control+Option: set VoiceOver's
modifier to Caps Lock, or pass a single chord through with `VO+Tab`. On the **Linux web
host**, a missed Ctrl turns `Ctrl+Alt+Shift+G` into `Alt+Shift+G`, which is the Gann fan tool
there; `Escape` cancels a tool armed by accident.

---

## Modals and Panels

| Key | Opens |
|-----|-------|
| F1 | Help dialog (built-in keyboard reference) |
| F12 | Settings dialog |
| Alt+A | Add Indicator dialog |
| Alt+O | Object tree (manage chart layers, indicators, drawings) |
| Alt+J | Alerts manager |
| Alt+K | API key manager |
| Alt+T | Trading dashboard |
| Alt+B | Order book |
| Alt+S | Strategy manager |
| Alt+W | Sound designer |
| Alt+D | Drawing tools panel |
| Alt+Shift+L | Pin a text label at the cursor bar, then type what it says |
| Ctrl+Alt+Shift+Y | Describe the chart's LAYOUT — axes, scales, panes, series counts, what is hidden or muted |
| Alt+Shift+/ | Describe THIS PANE — what each axis measures, its range, and the step between gridlines |
| Ctrl+Alt+Shift+K | Show every hidden component again (announces how many) |
| Ctrl+Alt+Shift+U | Unmute every muted component (announces how many) |
| Ctrl+Alt+Shift+O | Narration off everywhere — every series switched off, every component selection cleared (announces how many) |
| Alt+M | Market watch — watchlists and the screener  (toolbar: **Watch**) |
| Alt+R | Respect report — which levels and moving averages this market actually holds (toolbar: **Levels**) |
| Alt+I | Asset dossier for the loaded symbol (I for Instrument/Info) | "{Symbol}, {class} dossier. N of M sections have data..." |
| Alt+, | Custom scripts panel (PineScript / Roslyn) |
| Ctrl+Alt+Shift+J | Journal — review every speech utterance, alert, strategy setup, and error this session (filterable, copyable monospace text view) |

---

---

## Journal Modal (Ctrl+Alt+Shift+J)

The Journal is the persistent review surface for everything the application has spoken or alerted on during the current session. It is the primary tool for reviewing setups that scrolled past in speech.

- The modal is a console-style text area (monospace, screen-reader friendly). Tab into it to read or copy any line.
- Filter buttons partition the buffer by category: All / Speech / Alerts / Setups / Errors / Backtests.
- "Copy visible" puts the currently filtered view onto the clipboard.
- "Clear" empties the in-memory ring buffer.
- The buffer holds up to 2000 entries. Newest entries are at the bottom.
- Composite-strategy setups appear with their full reasoned rationale: side, score, stop price, first target, R:R, and stop placement notes — exactly the form needed to review *why* a setup fired.

---

## Mouse and touch equivalents

Every mouse or touch action routes into the same store the keyboard navigates, so
speech and sonification fire identically for all three. The keyboard remains the
canonical path; the other columns show the parity actions.

| Action | Keyboard | Mouse | Touch |
|---|---|---|---|
| Move cursor to a bar and hear it | Left / Right arrows | Single click on the bar | Tap the bar |
| Jump to the latest live bar | `\` (Backslash) | Double-click the chart | Double-tap the chart |
| Pan the viewport | `[` / `]` | Click-drag the chart, or **Shift+scroll** (or horizontal trackpad swipe) | One-finger drag |
| Zoom in / out | `=` / `-` | Scroll wheel (anchored at the pointer) | Pinch (anchored between fingers) |
| Chart context menu (play from here, jump to latest, crosshair, per-series actions) | Application key or Shift+F10 (chart focused, no drawing focused) | Right-click on open chart space | Press and hold (~½ s) |
| Drawing context menu (Delete / Duplicate / Properties) | Application key or Shift+F10 (drawing focused) | Right-click on a drawing's anchor handle | Press and hold on the anchor handle |
| Place a drawing | Tool shortcut, navigate, re-press | Click-drag (live preview) or click-click | Tap-tap (after arming the tool) |
| Move a drawing anchor | Re-place via tool shortcut | Drag the anchor handle | Drag the anchor handle |
| Inspect a bar without moving the cursor | — (arrows always move the cursor) | Hover — crosshair readout shows date, price, OHLC (visual only, never spoken); optional hover sound ticks per bar | — |
| Focus the indicator under the pointer | Page Up / Page Down + Up / Down | Click near its line (falls back to bar-select on a miss) | Tap near its line |
| Measure a range (spoken summary, cursor stays put) | — | **Shift+click** the far bar | — |
| Snap drawing anchors to O/H/L/C | — (keyboard anchors already land on the bar's close) | Right-click menu → Magnet snap (off by default) | same |

**With VoiceOver or TalkBack running**, the screen reader owns the touchscreen, so use
the **Bar navigator slider** (flick up/down to step through bars; TalkBack steps one
bar, iOS VoiceOver ~10% of the chart per flick) and the **touch toolbar** below the
chart (Previous/Next bar, Previous/Next component, Play, Chart menu — swipe to a
button, double-tap to press).

---

## Notes

- **Tab** is not a shortcut in this application. Series switching uses Page Up / Page Down.
- **Ctrl+I** is not assigned. The Add Indicator dialog is Alt+A.
- **Alt+Shift+C** opens a Price Channel drawing — it is not a trendline shortcut. Trendlines use Alt+Shift+T.
- The **P** key opens indicator properties regardless of whether you use it alone or as Shift+F12. Both route to the same `OpenProperties` command.
- Drawing tools use **sequential anchoring**: press the tool shortcut once per anchor (navigate, re-press the same shortcut). There is **no** Enter-to-confirm and **no** Coordinate Entry mode — `Enter`/`Return` is not bound to drawing.
