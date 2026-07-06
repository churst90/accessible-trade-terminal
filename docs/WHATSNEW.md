# What's New in 1.4.0

A short, user-facing summary of what changed since **1.3.1**. For the full engineering
changelog see [`CHANGES.md`](CHANGES.md).

> Version note: 1.4.0 is a **minor** release — it adds features and fixes bugs without
> breaking existing workspaces, keys, or settings. Saved workspaces from 1.3.1 load
> unchanged.

---

## Trading and Analytics are now one interface

There is no longer a separate **Trading** / **Analytics** mode toggle in the toolbar.
Instead, the **Market** dropdown carries everything:

- Pick a normal market (Crypto, Forex, Stock, …) to chart tradeable instruments.
- Pick **Analytics** to chart data feeds — and a new **Analytics type** dropdown appears
  right after Market, letting you choose **Economic**, **OnChain**, **Derivatives**, or
  **Sentiment**. Then choose the provider and symbol as usual.

So the flow is simply: **Market → (Analytics type) → Provider → Symbol → Load.** Analytics
providers no longer clutter the trading market list, and the top toolbar row is less
crowded. Everything you could reach before is still reachable — it's just organized under
one roof.

## Pan and zoom with the mouse

The chart toolbar gained four buttons — **Pan left**, **Pan right**, **Zoom in**, **Zoom
out** — with clear icons. They behave exactly like the keyboard commands (`[` `]` to pan,
`-` `=` to zoom), including speaking the new visible range, so they're fully usable with a
screen reader. They also work on analytics line charts, and are disabled until a chart is
loaded.

New too: **click and drag the chart to pan it.** When you don't have a drawing tool
selected, press the mouse button on the chart and drag — dragging right slides the chart
right and brings older bars into view, like grabbing a strip of paper. Releasing the
button anywhere (even off the chart) ends the drag cleanly.

The keyboard shortcuts are unchanged; the buttons and drag are additions.

## Paper trading always on for web accounts

If you use the **hosted web terminal** (logged-in accounts), paper trading is now **always
on and cannot be switched off** — as intended. Previously, clicking Trade could report
"provider does not support trading" because the web providers are data-only. Now pressing
**Alt+T** opens a fully working **paper** trading dashboard against live prices. Real-money
trading remains a desktop-only feature (bring your own broker keys there).

## Fixes

- **New workspace tab appears instantly.** Opening a new tab (via the keyboard command or
  "Open in New Tab") now shows it in the tab bar immediately, instead of only appearing
  after you clicked another tab.
- Every toolbar button — including **Trade** — has a hover tooltip and an accessible label;
  this is now covered by tests so a button can't ship without help text.

---

_Questions or issues: see [`USER_MANUAL.md`](USER_MANUAL.md) and
[`QUICKSTART.md`](QUICKSTART.md)._
