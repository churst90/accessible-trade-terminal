# Accessible Trading Terminal — Keyboard Shortcuts

All shortcuts are sourced from `ShortcutManager.InitializeDefaultProfile()`. Shortcuts not listed here are not assigned by default. Users can customise bindings via the Sound Designer or by editing the shortcuts profile saved at `%LOCALAPPDATA%\AccessibleTrader\shortcuts.json` (MAUI heads) or `~/.local/share/AccessibleTrader/shortcuts.json` (Linux WebHost).

## Host-specific note: WebHost remaps `Ctrl+Shift+letter` to `Alt+Shift+letter`

Firefox (and most browsers) reserve several `Ctrl+Shift+letter` chords at the browser-chrome level — `Ctrl+Shift+T` reopens a closed tab, `Ctrl+Shift+H` opens history, `Ctrl+Shift+P` starts a private window, `Ctrl+Shift+J` opens the browser console, etc. — and they are NOT cancellable from page-level JavaScript even with `preventDefault`. So the Linux WebHost rewrites those bindings in-memory at startup: every `Ctrl+Shift+letter` chord becomes `Alt+Shift+letter` (Firefox does not reserve `Alt+Shift+*`). Same letter, same command, different modifier. Two-modifier chords with three modifiers (`Ctrl+Alt+Shift+N` etc.) are untouched.

| Drawing tool | MAUI head (Win/Mac/iOS/Android) | Linux WebHost |
| --- | --- | --- |
| Trend line | `Ctrl+Shift+T` | `Alt+Shift+T` |
| Horizontal line | `Ctrl+Shift+H` | `Alt+Shift+H` |
| Vertical line | `Ctrl+Shift+V` | `Alt+Shift+V` |
| Channel | `Ctrl+Shift+C` | `Alt+Shift+C` |
| Fibonacci retracement | `Ctrl+Shift+F` | `Alt+Shift+F` |
| Text label | `Ctrl+Shift+L` | `Alt+Shift+L` |
| Fibonacci extension | `Ctrl+Shift+E` | `Alt+Shift+E` |
| Andrews Pitchfork | `Ctrl+Shift+A` | `Alt+Shift+A` |
| Rectangle | `Ctrl+Shift+R` | `Alt+Shift+R` |
| Measure tool | `Ctrl+Shift+M` | `Alt+Shift+M` |
| Gann fan | `Ctrl+Shift+G` | `Alt+Shift+G` |
| Risk/reward | `Ctrl+Shift+P` | `Alt+Shift+P` |
| Anchored VWAP | `Ctrl+Shift+W` | `Alt+Shift+W` |
| Gann box | `Ctrl+Shift+B` | `Alt+Shift+B` |
| Angle Fibonacci | `Ctrl+Shift+J` | `Alt+Shift+J` |
| Detailed point summary | `Ctrl+Shift+D` | `Alt+Shift+D` |

The remap is purely in-memory and never persisted to `shortcuts.json`, so the disk profile remains portable between hosts. The Help dialog (`F1`) reads the live in-memory profile, so each host self-documents its current bindings — you always see the right modifier on the host you're using.

Other Firefox-reserved chords that are NOT remapped because they have no clean alternative or because in-app behaviour can be reached another way:

- `Ctrl+T` (would be AddTab) — Firefox opens a new browser tab. Use the toolbar's tab `+` button instead.
- `Ctrl+W` (would be CloseTab) — Firefox closes the browser tab. Use the tab's `×` button.
- `Ctrl+Tab` / `Ctrl+Shift+Tab` (would be SwitchTabNext/Prev) — Firefox switches browser tabs. On the web, press **`Ctrl+Alt+Shift+T`** (`FocusTabBar`) to move keyboard focus onto the workspace tab switcher bar, then use the arrow keys / `Home` / `End` / the number row (`1`–`9` jump to that tab) / `Delete` (close the active tab). The three-modifier chord is not browser-reserved, so it always reaches the page.

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
| Ctrl+Down | Cycle to next component within the same pane (wraps) | "{Component Name}, {value}" |
| Ctrl+Up | Cycle to previous component within the same pane (wraps) | "{Component Name}, {value}" |
| Alt+Down | Scroll indicator pane list down | "Scroll panes down" |
| Alt+Up | Scroll indicator pane list up | "Scroll panes up" |
| Ctrl+Page Down | Jump to first component of next sub-pane in focused series | "[Pane name]. [Component name]..." or "No sub-panes in [Series]" |
| Ctrl+Page Up | Jump to first component of previous sub-pane in focused series | "[Pane name]. [Component name]..." or "No sub-panes in [Series]" |

---

## Context-Aware Jump Navigation (Ctrl+Left / Ctrl+Right)

`Ctrl+Left` and `Ctrl+Right` perform a context-sensitive jump whose target depends on what component is currently focused. The behavior is determined by `CommandDispatcher.HandleTrendlineCrossJump`.

| Focused Component Type | Jump Target |
|------------------------|-------------|
| Price candle or candle wick | Next bar where price crosses a drawn trendline |
| Sparse marker (Dot, Diamond, Cross, Arrow, TriangleUp, TriangleDown, Square, ZeroDot) | Next bar where that component has a non-NaN signal value |
| Zero-crossing oscillator (MACD, Momentum, ZeroArea etc.) | Next bar where the oscillator crosses the zero line |
| Threshold oscillator (RSI, MFI, Stoch, CCI — any indicator with OB/OS levels) | Next bar where the indicator enters or leaves the overbought/oversold zone |
| Moving average overlay (EMA, SMA, WMA, Spider Lines etc.) | Next bar where price (close) crosses the focused MA line |
| Band indicator (Bollinger %B / PERCENTB) | Next bar where the indicator crosses the upper (1.0), mid (0.5), or lower (0.0) band boundary |
| No focus / unknown | Next trendline crossing (fallback) |

When no further event exists in the scan direction, speech announces: "No more [component name] signals in this direction."

---

## Playback

| Key | Action | Notes |
|-----|--------|-------|
| Space | Play/Stop entire chart | All visible series, bar by bar |
| Shift+Space | Play/Stop focused series | Only components of the focused indicator |
| Ctrl+Shift+Space | Play/Stop focused component | Single component only |
| Ctrl+Space | Pause / Resume active playback | Cursor syncs to pause point |
| Shift+Escape | Force-stop all playback immediately | — |
| Shift+= | Increase playback speed | — |
| Shift+- | Decrease playback speed | — |

---

## Speech and Sonification

| Key | Action | Speech Feedback |
|-----|--------|-----------------|
| F2 | Toggle speech output on/off | "Speech output enabled/disabled" |
| F3 | Toggle sonification (audio engine) on/off | "Sonification enabled/disabled" |
| F4 | Announce context summary | "{Symbol} on {Provider}, {Timeframe}" |
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
| 0 (zero) | Add a zero reference line to the focused indicator series | "Zero line added" |
| Delete | Remove the focused indicator series (candles are protected) | Confirmation |

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

## Analysis

| Key | Action | Speech Feedback |
|-----|--------|-----------------|
| Ctrl+Shift+D | Full candle pattern + indicator analysis for the current bar | Spoken summary |
| Ctrl+Alt+Shift+N | Toggle auto-narration for the focused series on/off | "Narration on/off" |
| Ctrl+Alt+Shift+A | Open the AI Analyst modal | — |

---

## Tabs and Workspaces

| Key | Action |
|-----|--------|
| Ctrl+T | Add a new chart tab (desktop; on the web use the tab bar's `+` button) |
| Ctrl+W | Close the current chart tab (desktop; on the web use a tab's `×` button) |
| Ctrl+Tab | Switch to the next tab (desktop only — browser-reserved on the web) |
| Ctrl+Shift+Tab | Switch to the previous tab (desktop only — browser-reserved on the web) |
| Ctrl+Alt+Shift+T | Focus the workspace tab switcher bar (web-safe path to tab switching) |
| Ctrl+Alt+Shift+W | Save the current workspace (all tabs + layout) |
| Ctrl+Alt+W | Load a saved workspace |

When the tab switcher bar has focus (via `Ctrl+Alt+Shift+T`, or by clicking it): `←`/`→` (or `↑`/`↓`) switch tabs, `Home`/`End` jump to the first/last tab, the number row `1`–`9` jumps to that tab, and `Delete` closes the active tab. It is an ARIA tablist — your screen reader announces the newly selected tab as you move.

---

## Drawing Tools — Sequential Anchoring

All drawing shortcuts use **sequential anchoring**: there is no separate "mode" and no Enter key. Each anchor is set by **pressing the same tool shortcut again** at the current cursor bar. The `DrawingInteractionManager` state machine advances one anchor per press.

1. Navigate to the first bar with the Left/Right arrows.
2. Press the tool shortcut (e.g. `Ctrl+Shift+T`) to set anchor 1 at the current bar. Speech announces the price and timestamp and prompts: "Navigate to next point and press the shortcut again."
3. Navigate to the next bar.
4. Press the **same** shortcut again to set anchor 2. For two-anchor tools this completes the drawing and speech confirms placement.
5. For three-anchor tools (Fibonacci extension, Risk/Reward, Andrews' pitchfork), press the shortcut once more for anchor 3.
6. Press Escape at any time to cancel the in-progress placement.

Single-anchor tools (horizontal line, vertical line, text label, anchored VWAP) complete on the first press.

> The default profile also keeps a mouse path (click/drag to place, drag a handle to reposition), but the keyboard re-press flow above is the canonical accessible path. **Enter/Return is not used for drawing** — the `ConfirmCoordinateEntry` command is reserved/unused and has no key binding or dispatch handler.

| Key | Tool | Anchors Required |
|-----|------|-----------------|
| Ctrl+Shift+T | Trendline | 2 |
| Ctrl+Shift+H | Horizontal line (price level) | 1 |
| Ctrl+Shift+V | Vertical line (time marker) | 1 |
| Ctrl+Shift+C | Price channel (two parallel lines) | 2 |
| Ctrl+Shift+F | Fibonacci retracement | 2 (swing high and swing low) |
| Ctrl+Shift+E | Fibonacci extension | 3 (move start, move end, pullback) |
| Ctrl+Shift+L | Text label | 1 |
| Ctrl+Shift+R | Rectangle | 2 (opposite corners) |
| Ctrl+Shift+M | Measure / range tool | 2 |
| Ctrl+Shift+A | Andrews' pitchfork | 3 |
| Ctrl+Shift+G | Gann fan | 2 |
| Ctrl+Shift+B | Gann box | 2 |
| Ctrl+Shift+J | Angle / Fibonacci angle | 2 |
| Ctrl+Shift+P | Risk/Reward tool | 2 (entry and stop loss) |
| Ctrl+Shift+W | Anchored VWAP | 1 (the anchor bar) |

| Key | Drawing Placement Action |
|-----|--------------------------|
| (re-press the tool shortcut) | Set the next anchor at the current bar position |
| ContextMenu / Shift+F10 | Open the context menu for the focused drawing |
| Escape | Cancel the active in-progress drawing |

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

## Notes

- **Tab** is not a shortcut in this application. Series switching uses Page Up / Page Down.
- **Ctrl+I** is not assigned. The Add Indicator dialog is Alt+A.
- **Ctrl+Shift+C** opens a Price Channel drawing — it is not a trendline shortcut. Trendlines use Ctrl+Shift+T.
- The **P** key opens indicator properties regardless of whether you use it alone or as Shift+F12. Both route to the same `OpenProperties` command.
- Drawing tools use **sequential anchoring**: press the tool shortcut once per anchor (navigate, re-press the same shortcut). There is **no** Enter-to-confirm and **no** Coordinate Entry mode — `Enter`/`Return` is not bound to drawing.
