# Scope — giving the chart its window back

**Opened 2026-09-22. Blocks v2.12.0 at Cody's direction.** Written to be picked up cold in a new
session: every number here is measured, every change has a file and a line, and the ordering is
chosen so each step is verifiable on its own.

---

## 1. The measurement, and it is the whole argument

Taken from `~/Desktop/212.png` — a **maximised** Brave window on the Gentoo box, 2560×1562 at 2×
device pixel ratio, so **1280×781 CSS px**. Chart carrying Candles + Volume + RSI + MACD.

| | CSS px | share of the app |
|---|---:|---:|
| Our chrome **above** the chart | 214 | |
| **The chart** | **279** | **41.5 %** |
| Our chrome **below** the chart | 180 | |
| App viewport (below browser UI) | 672 | chrome **58.5 %** |

Browser chrome above the app is a further 109 CSS px.

**The chart — the entire product — gets 41.5 % of the window on a maximised display.**

### The bands, measured

Run this to reproduce (it is how the table above was produced):

```bash
python3 - <<'PY'
from PIL import Image
from collections import Counter
im=Image.open("212.png").convert("RGB"); W,H=im.size; px=im.load()
def emptyish(y):
    c=Counter(px[x,y] for x in range(0,int(W*0.98),6)); col,n=c.most_common(1)[0]
    return n/len(range(0,int(W*0.98),6)) > 0.97
bands=[];cur=None
for y in range(218,1010):
    e=emptyish(y)
    if not e and cur is None: cur=y
    elif e and cur is not None:
        if y-cur>6: bands.append((cur,y))
        cur=None
for a,b in bands: print(f"{a}..{b}  {(b-a)/2:.0f} CSS px")
PY
```

| band | CSS px | what it is |
|---|---:|---|
| Icon toolbar — Object tree … Help | 42 | 14 buttons |
| Market / Provider / Symbol / Time + timeframe pills | 30 | |
| Load chart / Pan / Zoom / Heatmap / HA / Log scale / Replay | 42 | 8 buttons |
| Tab bar | ~20 | |
| *(inter-band padding)* | ~80 | `gap: 4px` × rows + group padding |
| **Focused Indicator** bar — picker, Hide, Mute, Add, Scripts, Drawings | ~50 | |
| Status line + footer | ~35 | |

### For comparison

TradingView's vertical chrome is roughly a **38 px** top bar plus a **~30 px** bottom bar — about
**70 px against our ~394**. Their drawing tools live in a *vertical* rail, which costs zero height.

### Is the user asking too much of the chart?

**No, and this must not be treated as a usage problem.** Volume + RSI + MACD is the most
conventional indicator set in technical analysis. TradingView's tiers allow 2 / 5 / 10 / 25
indicators; a typical user runs 3–5 panes and a serious one runs more. Three indicator panes is
mid-range normal.

---

## 2. Why the pane weighting looks broken (it is not)

`ChartRenderer.DefaultMainPaneWeight = 2f` landed on 2026-09-21 and is correct. It is **invisible
at this window size**, and the reason is arithmetic, not a bug:

- `ChartRenderer.cs:128` — `MinIndicatorPaneHeightPx = 80f`, multiplied by `density`.
- At 2× that is **160 device px** per indicator pane.
- The chart stack is **558 device px**. Three indicator panes at the floor = 480, leaving 78 for
  the price pane — below its 25 % floor, so the crowded rebalance at `ChartRenderer.cs:155-168`
  scales everything down proportionally and **everyone lands on an equal share**.

Verified against the screenshot: predicted `main=140, indicators=[140,140,140]`; measured four
panes at ~140 device px each. Exact match.

**The threshold** at 2× density with three indicator panes:

| chart height | result |
|---:|---|
| 279 CSS px (today) | equal — floor binds |
| 320 | equal — floor binds |
| **360** | main 33 % |
| **400+** | main 40 % |

So the chart needs roughly **another 100 CSS px** before the weighting can express itself at all.
That is the budget this scope has to find.

---

## 3. What to change

Ordered by ratio of pixels recovered to risk taken. **Each step is independently shippable and
independently verifiable — do not batch them.**

### 3a. Icon-only toolbar buttons — recover ~13 px per row (2 rows = ~26 px)

`AccessibleTrader.WebHost/wwwroot/app.css:693` (`.icon-btn`) and the matching copy in
`AccessibleTrader.BlazorClient/wwwroot/app.css`.

Current: a 40 px glyph disc, `gap: 3px`, then `.icon-btn-label` at `font-size: 0.7rem`, plus
`padding: 2px 4px`. That is ~57 px tall; dropping the caption leaves ~44 px.

**The captions cost sighted users nothing to keep and blind users nothing to remove** — the
accessible name comes from `aria-label` on the button (`ToolbarIconButton.razor:30`), not from the
visible span.

Three things to get right:

1. **WCAG 2.5.3 (Label-in-Name) gets EASIER, not harder.** The rule requires the accessible name
   to contain the *visible* text; with no visible text there is nothing to contain. The existing
   guard is `LabelInNameTests` — check it does not assert the visible label *exists*.
2. **`title` must carry the caption for sighted users**, and there is a trap recorded in this
   repo: *an `aria-describedby` resolving to an empty span suppresses `title` entirely* and once
   deleted every toolbar tooltip. See the memory note `describedby-empty-span-suppresses-title`.
   Verify tooltips actually appear after the change; a browser test asserting `title` presence is
   not the same as asserting the tooltip renders.
3. **Make it a setting, defaulting to icon-only**, rather than deleting the captions outright. A
   low-vision sighted user may want them. `AppSettings` + the Appearance tab.

### 3b. Merge the two icon rows — recover ~42 px, and this is the big one

`Toolbar.razor:42` is a `<nav class="toolbar" style="flex-direction: column">` holding six
`toolbar-group`s across (currently) two icon rows plus the symbol row.

At 1280 CSS px wide, 14 + 8 = 22 buttons at the current ~57 px each need 1254 px — it only *just*
fits, which is why it wraps to two rows as soon as anything grows. **At 44 px (after 3a) the same
22 buttons need ~968 px and fit comfortably**, with room to spare down to ~1000 px windows.

So 3a is a prerequisite for 3b, and together they are worth ~68 px.

Below ~1000 px the row must still wrap — that is correct behaviour and the small-window scrollport
from 2026-09-21 already catches it (`ChartSurvivesASmallWindowTests`).

### 3c. Compact the Focused Indicator bar — recover ~20 px

`IndicatorBar.razor` (137 lines). Hide / Mute / Add / Scripts / Drawings are the same 40 px discs
with captions. Same treatment as 3a. Consider folding the whole bar into the status line, since the
focused-indicator picker is the only part that is not a button.

### 3d. Reclaim the inter-band padding — recover ~20–30 px

`Toolbar.razor:42` sets `gap: 4px` between rows and each `toolbar-group` carries its own padding.
With fewer rows this matters less, but there is measurable dead space between the x-axis labels and
the Focused Indicator bar that belongs to nothing.

**Measure before and after.** This is the step most likely to be argued about on taste; the
screenshot probe settles it.

### 3e. Lower the indicator-pane floor from 80 to 60 CSS px — changes the arithmetic directly

`ChartRenderer.cs:128`. At today's 279 CSS px this alone turns equal-shares into main ≈ 35 %.
60 CSS px is still a legible oscillator pane; the *crowded* floor is already 30.

**This one is a judgement call and should be made with the eyes, not the arithmetic** — render an
RSI at 60 px and look. `ChartScreenshotProbe` exists for exactly this.

### 3f. Preserve the weight in the crowded path — the honest fix for the flattening

`ChartRenderer.cs:155-168`. Today the rebalance scales indicators proportionally and then re-raises
the main pane to its floor, so the price pane lands on exactly 25 % and the weight is discarded.
Keeping the 2:1 ratio when crowded would give main 40 % and each indicator ~56 CSS px.

**Highest risk item in this scope, and it is last for that reason.** The two comments in that block
describe real defects it was written to fix — a pane drawn under the x-axis strip, a pane pushed
off the canvas. `PaneHeightAllocationTests` already pins "everything fits" and "no pane gets zero
height"; extend it before touching the logic, not after.

### 3g. A focus mode — the feature F11 is impersonating

One chord that hides the toolbar and indicator bar and gives the chart the whole window. This is
what the user reached for when they asked about F11, and it is a real feature rather than a
workaround: F11 only recovers the **109 px of browser chrome**, where ours is 394.

Must be keyboard-reachable and must announce entering and leaving. The toolbar is a landmark and a
tab stop, so hiding it changes the tab order — announce that.

**Recommended target:** chart share from **41.5 % → ~60 %** without focus mode, and ~85 % with it.

---

## 4. The formation labels

Do **not** start by adding collision avoidance — `ChartFormationLayer` already has it:

- `MaxDrawn = 3` (line 47) with `ByDominance` ranking (line 60)
- Row staggering through `takenLabelRows` (line 68, and `NextLabelRow` is `internal` so the tests
  assert the real rule rather than a copy)
- A translucent backing plate behind every label (line ~190), added after a screenshot showed
  "head and shoulders" running across a candle body

What is actually wrong in `212.png`:

1. **Three formations produce up to SIX labels.** Each draws a name *and* a target — "ascending
   triangle" + "ascending triangle target". `MaxDrawn` caps formations, not text. Either count
   labels rather than formations, or suppress the target label unless the formation is focused.
2. **The stagger does not know about the pane legend.** The legend is canvas-drawn by
   `ChartRenderer` (`ChartRenderer.cs:1178`, the "+N more (see the object tree)" row) and the
   formation layer has no idea it is there, so labels stagger straight underneath it. The legend's
   rect needs to seed `takenLabelRows`.
3. **"bull flag" appears twice.** Two instances with the same name. Confirm whether that is two
   genuine formations or one emitted twice — **this may be a detector defect wearing a rendering
   costume, and styling around it would hide it.** Check before changing the renderer.

**The strategic option, and the one that fits the product:** label only the *focused or pinned*
formation and outline the rest. The chart already has a full keyboard model for formations —
`,` / `.` to jump between them, `;` / `Shift+;` to pin one — so the labels do not need to carry
all of them at once. That turns a stacking problem into a non-problem and matches the interaction
model already built. Recommended, after (3) is ruled out.

---

## 5. How to verify — tooling that already exists

- **`ChartScreenshotProbe`** (`AccessibleTrader.BrowserTests`) photographs nine chart states into
  `scratchpad/screenshots/` in about thirty seconds. **Take a full set BEFORE starting** and diff
  against it after each step.
- **`ChartSurvivesASmallWindowTests`** drives 946×536, 1024×600 and 800×480 and asserts the chart
  keeps `--chart-min-height`, that the overflow is reachable, and that the chart is actually
  *painted* rather than merely allotted space.
- **`PaneHeightAllocationTests`** covers the allocation as a pure function, including
  `ThreeIndicatorPanes_ThePriceGetsFortyPercentAndTheRestShareEvenly`, which is the assertion this
  scope exists to make true on screen.
- **The band-measuring script in §1** is the honest before/after for every chrome change.

**The gap worth closing first:** `TerminalBrowserFixture` opens every page at **1400×950**, which
is why none of this was ever visible to the suite. A test that asserts *the chart's share of the
viewport* at a realistic size is the guard this whole scope needs, and it should be written before
any of the changes above.

Suggested:

```
ChartClaimsItsShareOfTheWindowTests
  at 1280x781 with Volume+RSI+MACD, #chart-interact-zone height / viewport height >= 0.55
```

Red today at 0.415. That single number is the scope's definition of done.

---

## 6. Ordering

1. **The guard first** — `ChartClaimsItsShareOfTheWindowTests`, red at 0.415.
2. Baseline screenshots from `ChartScreenshotProbe`.
3. 3a icon-only (setting, default on) → measure.
4. 3b merge the icon rows → measure.
5. 3c indicator bar → measure.
6. 3d padding → measure.
7. Re-check the guard. If ≥ 0.55, stop and ship; 3e–3g become optional.
8. 3e floor, **with eyes on a 60 px RSI**.
9. 3f crowded weighting, tests extended first.
10. 3g focus mode.
11. Formations §4, starting with the duplicate-name question.

---

## 7. Risks

- **Two `app.css` copies have already drifted** (`AccessibleTrader.WebHost/wwwroot/app.css` and
  `AccessibleTrader.BlazorClient/wwwroot/app.css`). Every CSS change here must go in both, and the
  drift itself is worth closing — it is the same "fixed in one place" shape that produced five
  defects on 2026-09-21.
- **The desktop head positions its native Skia canvas from a JS-reported rect**
  (`canvasRegion.js`). Any change to the chrome's height changes that rect. It tracks `resize`,
  `scroll` and a `ResizeObserver`, so it *should* follow — but it must be re-verified on Windows,
  because that head cannot be built or run from Linux. Use `diagnostic-build.yml`.
- **Hiding visible labels touches WCAG 2.5.3 and the tooltip path**, and this repo has been bitten
  by the `aria-describedby`/`title` interaction before.
- **Do not change `--chart-min-height` (260 px)** as a shortcut. It is the small-window floor, a
  different problem, and raising it makes short windows worse rather than better.
