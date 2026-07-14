# Accessible Trader: Quick Start Guide

## For Blind and Visually Impaired Traders

This guide is for new users who want to understand how Accessible Trader works, how to navigate it by keyboard, and how to interpret the audio and speech feedback the application provides. No prior experience with trading software is required to read this guide, though familiarity with financial markets and charting concepts will help.

---

## Table of Contents

1. What Is Accessible Trader?
2. The Hybrid Voice Model
3. Understanding the Soundscape
4. Getting Started: The Chart Layout
5. Moving Through Time: Bar-by-Bar Navigation
6. Moving Between Panes and Components
7. Jumping to Signals: Ctrl+Left and Ctrl+Right
8. Playback: Listening to the Market
9. Controlling Volume and Speech
10. Working with Indicators
11. Drawing Tools
12. Analysis, AI, and Context (AI Analyst, Auto-Narration, Journal)
13. Modals, Panels, and Dialogs
14. Volume Profile Navigation
15. Heatmap Overlay
16. Settings and Customization
17. Platform Support
18. Complete Keyboard Shortcut Reference

---

## 1. What Is Accessible Trader?

Accessible Trader is a professional trading terminal built from the ground up for blind and visually impaired traders. It runs on Windows, Android, iOS, and Mac and gives you access to real-time and historical market data across stocks, crypto, forex, and other instruments.

The application is entirely keyboard-driven. You do not need a mouse at any point. Every function — from placing a drawing tool to configuring an indicator's audio properties — is reachable through keyboard shortcuts.

Accessible Trader pairs your existing screen reader (NVDA, JAWS, Narrator, VoiceOver, or TalkBack) with its own built-in audio engine to give you two complementary layers of information simultaneously:

- Your screen reader announces exact values: price, time, indicator readings.
- The built-in audio engine plays continuous sound that reflects the shape and movement of the market.

Together these two layers let you perceive both precise numbers and broad market structure at the same time — something no conventional chart can offer.

---

## 2. The Hybrid Voice Model

Accessible Trader uses what is called the Hybrid Voice model. This is the core design principle of the application, and understanding it will help you get the most out of every session.

**Screen reader (your existing tool) handles:**
- Exact price values when you land on a bar
- Exact indicator readings when you navigate to a component
- Dialog labels, menu items, setting names, and all text content
- Confirmation messages after actions

**Built-in sonification engine handles:**
- The pitch contour of price as it moves left to right through time
- The tonal texture of oscillator indicators — how overbought, how oversold, how far from zero
- Distinct bell tones for specific signal events (crossovers, divergences, confluence zones, support and resistance levels)
- Stereo position: bars on the left side of the visible window play in the left channel; bars on the right side play in the right channel
- Volume scaling: louder sounds indicate larger, more significant movements

Neither system replaces the other. When you play back a chart, you hear both simultaneously: your screen reader announces each bar's values while the audio engine plays that bar's sonic shape.

---

## 3. Understanding the Soundscape

### Pitch

Pitch maps directly to value. Higher pitch means a higher price or a higher oscillator reading. Lower pitch means lower. When a price trend is rising, you hear a rising pitch. When it falls, the pitch falls with it.

For oscillators (indicators that have a center line, such as MACD or RSI), the pitch rises above a mid-point for positive values and falls below for negative values.

### Waveform (Timbre)

The type of sound — its tonal color — changes based on where a value sits relative to key thresholds.

- When an oscillator is above zero, it uses one waveform (such as a triangle wave, which sounds smooth and full).
- When it is below zero, it switches to a different waveform (such as a sine wave, which sounds pure and clean).
- When a value pushes into an overbought or oversold zone on a bounded oscillator (RSI, Stochastics, MFI, CCI, Williams %R, or the Ultimate Oscillator), a noticeable noise texture roughens the tone so you can hear the extremity without checking a number. You can set how strong this roughness is per level with the Zone Texture slider in the indicator's properties dialog, under Reference Levels.

This means you can often tell at a glance — or rather, at a listen — not just what the value is but where it sits relative to the structure of the indicator.

### Bell Tones

When the playback cursor lands on a bar that contains a signal event, you hear a short bell tone in addition to the continuous oscillator sound. Different signal types have distinct bell timbres so you can tell them apart by ear:

- Sine bell: smooth and clear — typically marks crossover events
- Triangle bell: bright and metallic — typically marks divergence signals
- Crystal bell: high and pure — typically marks support and resistance levels
- Detuned pair bell: a slightly shimmering double tone — typically marks high-confluence signals
- Gradient blend bell: a rich, multi-harmonic tone — typically marks momentum signals

When you encounter a bell tone during playback, you can stop playback and read the exact event details through your screen reader.

### Stereo Panning

As playback moves forward in time, or as you move the cursor right, the sound moves from the left channel toward the right channel. The leftmost visible bar on screen is hard left; the rightmost is hard right. This gives you an immediate spatial sense of where you are in the visible window, even without a screen reader announcement.

### Volume Layers

The audio engine organizes sounds into three depth layers:

- Background layer (quieter): longer-term context sounds, such as broad trend lines
- Midground layer (medium): main oscillator and price tones
- Foreground layer (loudest): signals and event bells

This layering lets important signals cut through the continuous background without the two competing at the same volume.

---

## 4. Getting Started: The Chart Layout

When you open Accessible Trader, the main chart area is divided into panes stacked vertically:

- The top pane is always the price chart (candlestick bars by default).
- Below it are indicator panes — one for each indicator that has its own separate display area.
- Some indicators appear as overlays directly on the price chart (such as moving averages); they do not get their own pane.

Each pane can contain one or more series. Each series can contain one or more components. For example:

- A pane might contain a MACD indicator (one series).
- That MACD series has three components: the MACD line, the signal line, and the histogram.
- You navigate between these components with the Up and Down arrow keys.

Understanding this three-level hierarchy — panes, series, components — is the key to navigating the chart confidently.

### Choosing what to chart

Above the chart is the toolbar. Its second row is the selection cascade: **Market →
Provider → Symbol → timeframe → Load.** To chart data feeds instead of a tradeable
instrument, choose **Analytics** in the Market dropdown; an extra **Analytics type**
dropdown then appears (Economic, OnChain, Derivatives, Sentiment) before Provider. There is
no separate "Trading vs Analytics" switch — the Market dropdown is the single place you
choose between the two.

---

## 5. Moving Through Time: Bar-by-Bar Navigation

### The Basics

| Key | Action |
|-----|--------|
| Left Arrow | Move cursor one bar earlier (back in time) |
| Right Arrow | Move cursor one bar later (forward in time) |
| Home | Jump to the leftmost bar in the visible viewport |
| End | Jump to the rightmost bar in the visible viewport |
| Backslash (\) | Jump to the latest (live/most recent) bar |

When you move left or right, your screen reader announces the bar's data. If you are on the price pane, it announces the open, high, low, and close of that candle. If you are focused on an indicator component, it announces that component's value for the current bar.

Simultaneously, the audio engine plays the sonified tone for that bar.

### Scrolling the Viewport

Moving the cursor with Left and Right eventually reaches the edges of the visible window and causes the viewport to scroll automatically, keeping the cursor in view.

You can also scroll the viewport independently of the cursor:

| Key | Action |
|-----|--------|
| [ | Pan the viewport left (older bars come into view) |
| ] | Pan the viewport right (newer bars come into view) |
| Shift+[ | Decrease the pan step size (smaller pans) |
| Shift+] | Increase the pan step size (larger pans) |

### Zooming

| Key | Action |
|-----|--------|
| - (minus) | Zoom out (more bars visible, each appears narrower) |
| = (equals) | Zoom in (fewer bars visible, each appears wider) |

Zooming in shows you finer detail on fewer bars. Zooming out shows you broader context across more bars.

### With the mouse

The chart toolbar also has **Pan left**, **Pan right**, **Zoom in**, and **Zoom out**
buttons that do exactly what the keys above do (and announce the new visible range, so
they're screen-reader friendly). And when no drawing tool is selected, you can **click and
drag the chart** to pan it — drag right to bring older bars into view. Release the button
anywhere to stop.

---

## 6. Moving Between Panes and Components

### Switching Panes

| Key | Action |
|-----|--------|
| Page Down | Move focus to the next pane below |
| Page Up | Move focus to the pane above |
| Alt+Down | Scroll the indicator pane list down (when more panes are open than fit on screen) |
| Alt+Up | Scroll the indicator pane list up |

When you switch panes, speech announces the name of the newly focused series (for example, "RSI" or "Volume").

### Switching Components Within a Series

| Key | Action |
|-----|--------|
| Down Arrow | Move to the next component within the focused series |
| Up Arrow | Move to the previous component within the focused series |

For example, if you are in a MACD pane, Down Arrow moves from the MACD line to the signal line to the histogram, and Up Arrow moves back up through them.

Speech announces the component name and its value at the current bar when you move to it. The audio engine simultaneously plays that component's tone.

### Announcing Context

| Key | Action |
|-----|--------|
| F4 | Announce the current symbol, data provider, and timeframe |
| Ctrl+Alt+Shift+C | Focus the chart area and announce a full context summary |

---

## 7. Jumping to Signals: Ctrl+Left and Ctrl+Right

Bar-by-bar navigation is precise but slow when you want to scan for events. Ctrl+Left and Ctrl+Right perform context-aware jumps based on what component you currently have focused.

| Focused Component | Ctrl+Left / Ctrl+Right Jumps To |
|-------------------|----------------------------------|
| Price candle | Previous/next bar where price crosses a drawn trendline |
| Sparse signal marker (dots, diamonds, arrows) | Previous/next bar where that signal fires (has a value) |
| Zero-crossing oscillator (MACD etc.) | Previous/next bar where the oscillator crosses zero |
| Threshold oscillator (RSI, Stochastics etc.) | Previous/next bar where the indicator crosses into or out of the overbought/oversold zone |
| Moving average overlay | Previous/next bar where price crosses the moving average |

When you reach a boundary in either direction with no more events, speech announces: "No more [component name] signals in this direction."

This navigation mode is designed for rapid scanning. For example, you can press Ctrl+Right repeatedly to step through every RSI overbought entry since the chart's beginning, or through every instance where price crossed a moving average.

---

## 8. Playback: Listening to the Market

Playback is the core listening mode. When you start playback, the chart animates bar by bar through the visible window while the audio engine plays the sonified representation of each bar. Your screen reader announces each bar's values as the cursor advances.

### Playback Scopes

There are three playback scopes, controlling how much of the chart you hear:

| Key | Scope |
|-----|-------|
| Space | Play or stop the entire chart (all panes and series visible) |
| Shift+Space | Play or stop the focused series only |
| Ctrl+Shift+Space | Play or stop the focused component only |

Use whole-chart playback for a broad overview. Use focused series playback when you want to study one indicator without the noise of others. Use focused component playback when you want to isolate a single line — for example, just the RSI line by itself.

Whole-chart playback layers every visible, unmuted series at once, and each series sounds all of its own visible, unmuted components together — including soft cloud- and ribbon-fill sounds (such as an EMA Fill between two averages) that used to drop out when too many voices were playing. Muting a series or component (M) or hiding it (H) simply removes it from the mix, so you can thin a crowded soundscape down to what matters.

### Pause, Resume, and Stop

| Key | Action |
|-----|--------|
| Ctrl+Space | Pause or resume active playback |
| Shift+Escape | Force-stop all playback immediately |

When you pause with Ctrl+Space, playback now falls fully silent instead of holding the last chord. The arrow keys still audition individual bars while paused, so you can inspect the frozen moment before resuming.

### Playback Speed

| Key | Action |
|-----|--------|
| Shift+= | Increase playback speed |
| Shift+- | Decrease playback speed |

Speed affects how quickly the cursor advances through bars. At slower speeds you hear each bar more fully. At faster speeds you can scan a long history quickly.

---

## 9. Controlling Volume and Speech

### Speech Toggle

| Key | Action |
|-----|--------|
| F2 | Toggle speech output on or off |
| F3 | Toggle sonification (audio engine) on or off |

You can run with speech only, audio only, or both simultaneously, depending on your preference and task.

### Volume Controls

Volume is controlled at three levels: per component, per series, and master chart volume.

| Key | Action |
|-----|--------|
| F5 | Component volume up (+10%) |
| Shift+F5 | Component volume down (-10%) |
| F6 | Series volume up (+10%) |
| Shift+F6 | Series volume down (-10%) |
| F7 | Master chart volume up (+10%) |
| Shift+F7 | Master chart volume down (-10%) |

Component volume adjusts just the one component you have focused (for example, just the histogram within MACD). Series volume adjusts all components in the focused series together. Master volume adjusts everything.

### Muting and Hiding

| Key | Action |
|-----|--------|
| M | Mute or unmute the focused series (or focused component if you last used Up/Down) |
| H | Hide or show the focused series (or component) |

Muting silences the audio for that series without removing it from the chart. Hiding removes it from visual display and silences it. Both are toggles — pressing the same key again restores it.

---

## 10. Working with Indicators

### Adding an Indicator

Press Alt+A to open the Add Indicator dialog. Indicators are organized in categories:

- Multi-Signal: indicators that produce several different types of signals from a single calculation
- Trend: moving averages and directional tools
- Momentum: oscillators that measure speed and rate of change
- Volatility: indicators that measure the range or dispersion of price
- Volume: indicators that incorporate trading volume into their analysis
- Profiles: volume profile and time-price opportunity tools

Navigate the category list and indicator list with the arrow keys. Press Enter to add an indicator.

When an indicator is added, it is automatically assigned audio properties based on its type. You can adjust these in the indicator's properties dialog.

### Navigating to an Indicator

Use Page Down from the price chart to move focus through indicator panes in order. Use Page Up to move back up. When you land on an indicator pane, speech announces the indicator name and the value of its primary component at the current bar.

### Exploring Components Within an Indicator

Press Down Arrow to move through the components of the focused indicator. Each component is a distinct output of the indicator — for example, the fast line, slow line, and signal line of a multi-line indicator. Speech announces the component name and value as you move to each one.

### Adding a Reference Line

When focused on any indicator pane, press 0 (zero) to add a zero-level horizontal reference line to that indicator. This line will be audible during playback and will be used for zero-crossing navigation via Ctrl+Left/Right.

### Opening Indicator Properties

| Key | Action |
|-----|--------|
| P or Shift+F12 | Open the properties dialog for the focused indicator |

In the properties dialog you can adjust:

- Calculation parameters (periods, smoothing factors, thresholds)
- Per-component colors (for sighted collaborators)
- Per-component audio settings (on the Sonification tab): a Sound Patch dropdown listing built-in bell patches plus any patch you have built in the Sound Designer, each with a ▶ preview button, and fallback waveform, noise, and volume controls used when no patch is chosen. Directional, green/red components (candles, bars, histograms and volume bars) also get separate Green (bullish) and Red (bearish) patch dropdowns, so up-bars and down-bars can sound different; plain lines and areas show only the single patch
- "Save as Defaults" — saves your preferred settings so new instances of this indicator start with the same configuration

Navigate the properties dialog with Tab and the arrow keys. Your screen reader reads all labels and current values.

### Removing an Indicator

Press Delete while focused on an indicator pane to remove that indicator from the chart. Speech asks for confirmation. The price candle pane cannot be removed.

### Heikin-Ashi Candles

Press Alt+C to toggle Heikin-Ashi candle mode on the price chart. Heikin-Ashi candles are a smoothed candlestick formula that reduces noise and can make trends easier to hear.

### Logarithmic Scale

Press Alt+L to toggle logarithmic (log) scale on the price chart. Log scale is useful for long historical views where price has moved by very large percentages, as it compresses large ranges into a more even distribution.

---

## 11. Drawing Tools

Drawing tools let you place reference lines and shapes on the chart that are then audible during navigation and playback — for example, the cursor will announce when it crosses a trendline you have placed.

All drawing tools use **sequential anchoring**. You do not hold keys and drag, and you do not press Enter. Instead you navigate the cursor to the point you want and **press the same drawing shortcut again** to set each anchor at the current bar. This makes every drawing tool fully accessible.

### The Sequential Anchoring Workflow

Take a trendline (Ctrl+Shift+T) as the example:

1. Navigate the cursor to the first point using the Left and Right arrows.
2. Press the drawing shortcut (Ctrl+Shift+T) to set the first anchor at the current bar. Speech announces the price and timestamp of the anchor and tells you what to do next — for example: "Trend line: anchor 1 set at 42,500.00, 14:30. Navigate to next point and press the shortcut again."
3. Navigate to the second point.
4. Press the **same** shortcut again (Ctrl+Shift+T) to set the second anchor and complete the drawing. Speech announces the completed drawing: for example, "Trend line placed from 42,500.00 to 43,100.00."
5. For three-anchor tools (Fibonacci extension, Risk/Reward, Andrews' pitchfork), repeat: navigate, then press the shortcut a third time to set the final anchor.
6. Press Escape at any time during placement to cancel the in-progress drawing.

Single-anchor tools (horizontal line, vertical line, text label, anchored VWAP) complete on the first press — they place immediately at the current bar.

> **Note:** Enter does **not** confirm anchors. Each anchor is set by pressing the tool's own shortcut again. (If you have a mouse, you can also click or drag to place anchors, and drag an existing anchor handle to reposition it — but the keyboard workflow above is the primary, fully accessible path.)

### Drawing Shortcuts

> **Linux web host users:** every `Ctrl+Shift+<letter>` chord below becomes `Alt+Shift+<letter>` in the browser (Firefox reserves several `Ctrl+Shift` chords at the browser level). Same letter, same tool — see [Platform Support](#17-platform-support). The desktop/mobile apps use `Ctrl+Shift` as shown.

| Key | Tool |
|-----|------|
| Ctrl+Shift+T | Trendline (2 anchors) |
| Ctrl+Shift+H | Horizontal line — price level (1 anchor) |
| Ctrl+Shift+V | Vertical line — time marker (1 anchor) |
| Ctrl+Shift+C | Price channel (2 anchors) |
| Ctrl+Shift+F | Fibonacci retracement (2 anchors: swing high and swing low) |
| Ctrl+Shift+E | Fibonacci extension (3 anchors: move start, move end, pullback) |
| Ctrl+Shift+L | Text label (1 anchor) |
| Ctrl+Shift+R | Rectangle (2 anchors: opposite corners) |
| Ctrl+Shift+M | Measure / range tool (2 anchors: start and end of range) |
| Ctrl+Shift+A | Andrews' pitchfork (3 anchors) |
| Ctrl+Shift+G | Gann fan (2 anchors) |
| Ctrl+Shift+B | Gann box (2 anchors) |
| Ctrl+Shift+J | Angle / Fibonacci angle (2 anchors) |
| Ctrl+Shift+P | Risk/Reward tool (2 anchors: entry and stop loss, then speech guides you to the target) |
| Ctrl+Shift+W | Anchored VWAP (1 anchor: the bar from which VWAP is calculated forward) |
| Alt+D | Open the drawing tools panel (manage and delete existing drawings) |

### Fibonacci Retracement

After placing 2 anchors (for example, from a swing low to a swing high), the application places horizontal levels at standard Fibonacci ratios (23.6%, 38.2%, 50%, 61.8%, 78.6%). These levels are then audible as the cursor crosses them and are used as trendline-crossing events for Ctrl+Left/Right navigation when focused on the price series.

### Anchored VWAP

The Anchored VWAP tool (Ctrl+Shift+W) places a Volume Weighted Average Price line starting from the bar you anchor it to and extending to the latest bar. It behaves like a moving average overlay — you can focus on it with Up/Down, and price/VWAP crossings are navigable with Ctrl+Left/Right.

### Risk/Reward Tool

Ctrl+Shift+P starts the Risk/Reward tool. After you set the entry and stop-loss anchors, speech announces the resulting risk amount and asks you to set the target. When the target is set, speech announces the full risk-to-reward ratio.

---

## 12. Analysis, AI, and Context

### Detailed Point Analysis

| Key | Action |
|-----|--------|
| Ctrl+Shift+D | Announce full analysis of the current bar |

This announces:
- The candle's open, high, low, close, and volume
- Any recognized candlestick patterns at this bar (for example, "Engulfing bullish pattern")
- The readings of all active indicators at this bar
- Any signal events on this bar across all active indicators

This is useful for a thorough understanding of a specific bar before making a decision.

### Context Summary

| Key | Action |
|-----|--------|
| F4 | Announce symbol, data provider, and timeframe |
| Ctrl+Alt+Shift+C | Focus chart area and announce full context summary |

### AI Technical Analyst

| Key | Action |
|-----|--------|
| Ctrl+Alt+Shift+A | Open the AI Analyst modal |

The AI Analyst sends a snapshot of the current chart — recent candle data, a summary of your active indicators, and (where the provider supports vision) an image of the chart — to a large language model and reads back a concise, plain-language technical analysis written for text-to-speech: trend direction, key support/resistance levels, momentum signals, and a short-term outlook.

To use it you must first add an API key for at least one AI provider in the **API key manager (Alt+K)**. Supported providers are tried in order — **Claude, then OpenAI, then Ollama** — and the first one you have configured is used. If no key is configured, the Analyst tells you so rather than failing silently. Because the request goes to an external service, only use it with a provider you trust with your chart data.

### Auto-Narration

| Key | Action |
|-----|--------|
| Ctrl+Alt+Shift+N | Toggle auto-narration on/off for the focused series |

Auto-narration watches the focused indicator series and **speaks new signals and zone transitions as they occur on live bar closes** — for example, a fresh marker firing, or an oscillator entering or leaving an overbought/oversold zone. Only signals that appear *after* you enable narration are announced; pre-existing historical signals are not replayed. Toggle it per series, so you can leave narration running on the one indicator you care about without being interrupted by the rest of the chart.

### The Journal

| Key | Action |
|-----|--------|
| Ctrl+Alt+Shift+J | Open the Journal |

The Journal is a persistent, screen-reader-friendly record of everything the application has spoken or alerted on during the current session — speech, alerts, strategy setups, errors, and backtest results. It is the primary tool for reviewing things that scrolled past in speech. The view is a monospace text area you can Tab into to read or copy any line, with filter buttons (All / Speech / Alerts / Setups / Errors / Backtests) and a "Copy visible" button. The buffer holds up to 2000 entries, newest at the bottom. Composite-strategy setups appear with their full rationale — side, score, stop price, first target, risk/reward, and stop placement notes.

---

## 13. Modals, Panels, and Dialogs

All modals and panels are opened by keyboard shortcut and navigated with Tab, Shift+Tab, and the arrow keys. Your screen reader reads all labels. Press Escape to close any dialog.

| Key | Opens |
|-----|-------|
| F1 | Help dialog: complete keyboard shortcut reference |
| F12 | Settings: global application preferences |
| Alt+A | Add indicator dialog |
| Alt+O | Object tree: manage all chart layers, indicators, and drawings |
| Alt+J | Alerts manager: create and manage price and indicator alerts |
| Alt+K | API key manager: enter and manage your data provider credentials |
| Alt+T | Trading dashboard: view positions, orders, and account balance |
| Alt+B | Order book: view the live bid/ask order book for the current symbol |
| Alt+H | Toggle the heatmap overlay on/off |
| Alt+S | Strategy manager: load, configure, and run automated strategies |
| Alt+W | Sound designer: customize indicator timbres and bell patch assignments |
| Alt+, | Custom scripts panel: load and run PineScript or custom strategy scripts |
| Ctrl+Alt+Shift+A | AI Analyst: AI-powered technical analysis of the current chart (see [Analysis, AI, and Context](#12-analysis-ai-and-context)) |
| Ctrl+Alt+Shift+J | Journal: review every speech utterance, alert, setup, and error this session |

### Help Dialog (F1)

Press F1 at any time to open the built-in keyboard reference. This lists every shortcut in the application. It is readable by your screen reader in full.

### Settings (F12)

The Settings dialog contains tabs for:
- General preferences (default symbol, timeframe, provider)
- Audio preferences (default waveforms, playback speed, bell patch assignments)
- Accessibility preferences (screen reader integration mode, speech rate adjustments)
- Connection settings (data provider configuration)

---

## 14. Volume Profile Navigation

A Volume Profile is a special type of indicator that shows the distribution of volume across price levels rather than across time. It appears as a horizontal bar chart layered on the price chart, showing which price levels saw the most trading activity.

When your focus is on a Volume Profile series, the Up and Down arrows change their behavior:

- Up Arrow: move to the next higher price bin in the volume distribution
- Down Arrow: move to the next lower price bin

Your screen reader announces the price level of each bin and the volume at that level.

Two special announcements to listen for:

- Point of Control (POC): the price bin with the highest volume. When you land on it, you hear a distinct square wave tone in addition to the normal audio. Your screen reader announces "Point of Control."
- Value Area: the range of price levels that account for approximately 70% of all trading volume. When you enter this range, your screen reader announces "Entering Value Area." When you leave, it announces "Exiting Value Area."

Left and Right arrows still work normally in Volume Profile navigation, moving the cursor through time.

---

## 15. Heatmap Overlay

Press Alt+H to toggle the volume heatmap overlay on the price chart. The heatmap shades each price candle and time zone based on relative volume intensity.

When the heatmap is active, playback audio incorporates the heatmap intensity of each bar — higher-volume bars play louder than lower-volume bars, giving you an auditory sense of where the market was most active.

Press Alt+H again to turn the heatmap off.

---

## 16. Settings and Customization

### Saving Indicator Defaults

In any indicator's properties dialog (P or Shift+F12 while focused on the indicator), there is a "Save as Defaults" option. When you activate this, your current parameter and audio settings for that indicator type are saved. The next time you add an indicator of the same type, it will start with these saved settings.

### Custom Audio Per Component

In the indicator properties dialog, the Sonification tab has an Acoustics section for the component you select. There you can set:

- Sound Patch: the voice this component plays — any built-in patch or any patch you built in the Sound Designer — with a ▶ button to preview it
- Green (bullish) / Red (bearish) patch: for directional components (candles, bars, histograms and volume bars), separate patches for up-bars and down-bars; plain lines and areas do not show these
- Waveform, noise, and volume: fallback controls that take over when no Sound Patch is chosen

Patches are live-linked: edit a patch in the Sound Designer and every component using it updates immediately.

### Sound Designer (Alt+W)

The Sound Designer is a general-purpose patch workbench. A patch it produces can be assigned to event earcons here in the panel, or to indicator components through the properties dialog above.

A single patch can stack several oscillators via the "Add Oscillator" button. Each oscillator layer has:

- Waveform: sine, square, sawtooth, triangle, or noise
- Level: how much this layer contributes to the mix
- Freq Ratio: a harmonic multiple of the patch's base frequency (2.0 = one octave up)
- Noise Blend and Noise Colour: pink, white, or brown noise mixed into the layer

A Mix section sets the base frequency, a frequency multiplier, and overall volume; an Envelope section chooses a sustained tone or a plucked Ping and its duration. The Preview button auditions the whole patch, including its noise and envelope — not just the raw waveform. Patches saved by older versions still load unchanged.

---

## 17. Platform Support

Accessible Trader runs as a native desktop/mobile app (the "MAUI head" — Windows, Android, iOS, Mac) and as a self-hosted browser application (the "Linux web host"). The core keyboard navigation and audio model are the same everywhere. Platform-specific notes:

**Windows:**
- Works with NVDA, JAWS, and Narrator
- Full hardware keyboard required for all shortcuts
- WASAPI audio engine for lowest latency

**Android:**
- Works with TalkBack
- All shortcuts available via physical keyboard if connected
- AudioTrack audio engine

**iOS:**
- Works with VoiceOver
- Keyboard shortcuts require a connected hardware keyboard; on-screen keyboard access is limited
- AVAudioEngine audio

**Mac:**
- Works with VoiceOver
- Full keyboard support with hardware keyboard
- AVAudioEngine audio

**Linux (web host, runs in a browser such as Firefox):**
- Works with Orca and other browser-compatible screen readers
- **Modifier remap:** browsers (especially Firefox) reserve several `Ctrl+Shift+<letter>` chords at the browser-chrome level — `Ctrl+Shift+T` (reopen tab), `Ctrl+Shift+H` (history), `Ctrl+Shift+P` (private window), `Ctrl+Shift+J` (console), `Ctrl+Shift+R` (reload), `Ctrl+Shift+W` (close window) — and the page cannot override them. So on the web host **every `Ctrl+Shift+<letter>` chord is remapped to `Alt+Shift+<letter>`**. This affects all drawing tools *and* the detailed point summary (`Ctrl+Shift+D` → `Alt+Shift+D`). The letter and the command are unchanged — only the modifier differs.
- Chords with three modifiers (`Ctrl+Alt+Shift+...`, e.g. AI Analyst, narration, journal, save/load workspace) are **not** remapped — browsers don't reserve them.
- A few single-`Ctrl` browser chords are reserved at the chrome level and can't be overridden in-page, so on the web the in-app action moves to a web-safe chord (the reserved one is dropped from the Help dialog): new tab is **`Alt+Shift+N`** or the always-visible tab bar **+** button (not `Ctrl+T`); close a tab with its **×** button or by focusing the bar and pressing `Delete` (not `Ctrl+W`); switch tabs by pressing **`Ctrl+Alt+Shift+T`** to focus the tab switcher bar, then the arrow keys / `Home`/`End` / the number row (`1`–`9`) / `Insert` to add / `Delete` to close (not `Ctrl+Tab`); and jump between indicator sub-panes with **`Alt+PageUp`** / **`Alt+PageDown`** (not `Ctrl+PageUp`/`Ctrl+PageDown`, which the browser uses to cycle its own tabs).
- The Help dialog (F1) and its live shortcut table always read the bindings actually in effect on your host, so you will always see the correct modifier for the platform you are using.

#### MAUI head vs. Linux web host — drawing tool modifiers

| Drawing tool | Desktop/mobile app (`Ctrl+Shift`) | Linux web host (`Alt+Shift`) |
|---|---|---|
| Trendline | Ctrl+Shift+T | Alt+Shift+T |
| Horizontal line | Ctrl+Shift+H | Alt+Shift+H |
| Vertical line | Ctrl+Shift+V | Alt+Shift+V |
| Channel | Ctrl+Shift+C | Alt+Shift+C |
| Fibonacci retracement | Ctrl+Shift+F | Alt+Shift+F |
| Fibonacci extension | Ctrl+Shift+E | Alt+Shift+E |
| Text label | Ctrl+Shift+L | Alt+Shift+L |
| Rectangle | Ctrl+Shift+R | Alt+Shift+R |
| Measure / range | Ctrl+Shift+M | Alt+Shift+M |
| Andrews' pitchfork | Ctrl+Shift+A | Alt+Shift+A |
| Gann fan | Ctrl+Shift+G | Alt+Shift+G |
| Gann box | Ctrl+Shift+B | Alt+Shift+B |
| Angle / Fibonacci angle | Ctrl+Shift+J | Alt+Shift+J |
| Risk/Reward | Ctrl+Shift+P | Alt+Shift+P |
| Anchored VWAP | Ctrl+Shift+W | Alt+Shift+W |
| Detailed point summary | Ctrl+Shift+D | Alt+Shift+D |

---

## 18. Complete Keyboard Shortcut Reference

### Time Navigation

| Shortcut | Action |
|----------|--------|
| Left Arrow | Move cursor one bar back in time |
| Right Arrow | Move cursor one bar forward in time |
| Home | Jump to leftmost visible bar |
| End | Jump to rightmost visible bar |
| Backslash (\) | Jump to the latest (live) bar |
| [ | Pan viewport left |
| ] | Pan viewport right |
| Shift+[ | Decrease pan step size |
| Shift+] | Increase pan step size |
| - | Zoom out |
| = | Zoom in |

### Pane and Component Navigation

| Shortcut | Action |
|----------|--------|
| Page Down | Move to next pane below |
| Page Up | Move to previous pane above |
| Up Arrow | Move to previous component in focused series |
| Down Arrow | Move to next component in focused series |
| Alt+Up | Scroll pane list up |
| Alt+Down | Scroll pane list down |
| Ctrl+Left | Jump to previous signal/crossing in context |
| Ctrl+Right | Jump to next signal/crossing in context |

### Playback

| Shortcut | Action |
|----------|--------|
| Space | Play/Stop entire chart |
| Shift+Space | Play/Stop focused series |
| Ctrl+Shift+Space | Play/Stop focused component |
| Ctrl+Space | Pause/Resume active playback |
| Shift+Escape | Force-stop all playback |
| Shift+= | Increase playback speed |
| Shift+- | Decrease playback speed |

### Volume and Speech

| Shortcut | Action |
|----------|--------|
| F2 | Toggle speech on/off |
| F3 | Toggle sonification on/off |
| F4 | Announce symbol, provider, timeframe |
| F5 | Component volume up |
| Shift+F5 | Component volume down |
| F6 | Series volume up |
| Shift+F6 | Series volume down |
| F7 | Master chart volume up |
| Shift+F7 | Master chart volume down |
| H | Toggle visibility of focused series or component |
| M | Toggle mute of focused series or component |

### Indicators

| Shortcut | Action |
|----------|--------|
| Alt+A | Open Add Indicator dialog |
| P or Shift+F12 | Open properties for focused indicator |
| 0 (zero) | Add zero reference line to focused indicator |
| Delete | Remove focused indicator series |
| Alt+C | Toggle Heikin-Ashi candle mode |
| Alt+L | Toggle logarithmic scale |

### Analysis

| Shortcut | Action |
|----------|--------|
| Ctrl+Shift+D | Full point analysis of current bar |
| Ctrl+Alt+Shift+C | Focus chart and announce context summary |
| Ctrl+Alt+Shift+N | Toggle auto-narration for focused series |
| Ctrl+Alt+Shift+A | Open AI Analyst modal |

### Tabs and Workspaces

| Shortcut | Action |
|----------|--------|
| Alt+Shift+N | New chart tab (web-safe; works everywhere) |
| Ctrl+T | New chart tab (desktop only — browser-reserved on the web) |
| Ctrl+W | Close current tab (desktop; on the web use the tab's × or focus the bar + Delete) |
| Ctrl+Tab | Switch to next tab (desktop only) |
| Ctrl+Shift+Tab | Switch to previous tab (desktop only) |
| Ctrl+Alt+Shift+T | Focus the tab switcher bar (web-safe), then use arrows / Home / End / 1–9 / Insert (new) / Delete (close) |
| Ctrl+Alt+Shift+W | Save workspace |
| Ctrl+Alt+W | Load workspace |

### Sub-Pane and Intra-Pane Navigation

| Shortcut | Action |
|----------|--------|
| Ctrl+Page Down (web: Alt+Page Down) | Jump to first component of next sub-pane |
| Ctrl+Page Up (web: Alt+Page Up) | Jump to first component of previous sub-pane |
| Ctrl+Down | Cycle to next component within current pane (wraps) |
| Ctrl+Up | Cycle to previous component within current pane (wraps) |

### Drawing Tools

| Shortcut | Tool |
|----------|------|
| Ctrl+Shift+T | Trendline |
| Ctrl+Shift+H | Horizontal line |
| Ctrl+Shift+V | Vertical line |
| Ctrl+Shift+C | Price channel |
| Ctrl+Shift+F | Fibonacci retracement |
| Ctrl+Shift+E | Fibonacci extension |
| Ctrl+Shift+L | Text label |
| Ctrl+Shift+R | Rectangle |
| Ctrl+Shift+M | Measure / range tool |
| Ctrl+Shift+A | Andrews' pitchfork |
| Ctrl+Shift+G | Gann fan |
| Ctrl+Shift+B | Gann box |
| Ctrl+Shift+J | Angle / Fibonacci angle |
| Ctrl+Shift+P | Risk/Reward tool |
| Ctrl+Shift+W | Anchored VWAP |
| Alt+D | Open drawing tools panel |
| (re-press the tool shortcut) | Set each anchor at the current bar; the same shortcut advances anchor 1 → 2 → 3 and completes the drawing |
| ContextMenu / Shift+F10 | Open the context menu for the focused drawing (keyboard equivalent of right-click) |
| Escape | Cancel an in-progress drawing / close dialog |

> On the Linux web host these `Ctrl+Shift` drawing chords are `Alt+Shift` — see [Platform Support](#17-platform-support).

### Panels and Dialogs

| Shortcut | Opens |
|----------|-------|
| F1 | Help dialog |
| F12 | Settings |
| Alt+O | Object tree |
| Alt+J | Alerts manager |
| Alt+K | API key manager |
| Alt+T | Trading dashboard |
| Alt+B | Order book |
| Alt+H | Toggle heatmap overlay |
| Alt+S | Strategy manager |
| Alt+W | Sound designer |
| Alt+, | Custom scripts panel |
| Ctrl+Alt+Shift+J | Journal (review every speech, alert, setup, error this session) |

---

## Quick Start Checklist

If you are opening Accessible Trader for the first time, here is a recommended sequence to orient yourself:

1. Press F4 to hear what symbol, provider, and timeframe is currently active.
2. Press Ctrl+Alt+Shift+C to get a full context summary.
3. Press Right Arrow a few times and listen to the pitch changes as price moves forward in time.
4. Press Space to start whole-chart playback and let the chart play for several seconds. Press Space again to stop.
5. Press Page Down to move to the first indicator pane, then press Down Arrow to step through its components.
6. Press F1 to open the help dialog and explore the full shortcut reference.
7. Press Alt+A to open the Add Indicator dialog and add one indicator to get familiar with the workflow.

When you are ready to go deeper, the [User Manual](USER_MANUAL.md) covers trading,
alerts (including Discord/Slack webhooks), automated strategies with spoken trade
plans, and the Strategy Lab — the command-line research harness where strategies are
validated before they ship (see the manual's Automation chapter for how the lab and
the terminal divide the work).

Welcome to Accessible Trader.
