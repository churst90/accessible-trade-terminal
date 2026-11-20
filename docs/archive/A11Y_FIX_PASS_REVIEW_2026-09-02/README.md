# Review of the accessibility fix pass — 2026-09-02

Four specialist reviews of commits `bc52e652` and `553960f7`, the two commits whose
accessibility review never happened (the first review agent died on an API session limit; the
plugin's `accessibility-agents:accessibility-lead` type is not registered in this harness, so each
specialist was run as a general agent loaded with its plugin definition). Nothing in the tree was
edited by the reviewers. Each report cites file:line and labels every finding CONFIRMED or
UNVERIFIED, per the standing rule.

| Report | Verdict | Method |
|---|---|---|
| `review-modal-specialist.md` | NO-SHIP as "§3.1 closed" | real `keyboard.js` in a node vm, 20 scenarios (`probes/trap-scenarios.mjs`) |
| `review-keyboard-navigator.md` | NO-SHIP as described, SHIP as code | **real Chromium 140 over CDP** (`probes/cdp-drive.mjs`), standalone pages loading the committed `keyboard.js`/`app.css` |
| `review-aria-specialist.md` | SHIP | grep + a replica of the toolbar scan (`probes/scan_replica.py`) |
| `review-contrast-master.md` | SHIP | real WCAG luminance script, 12 themes (`probes/wcag_contrast.py`) |

## Status — fixed later the same day

All four items under "One-line fixes, in order" are in (`keyboard.js`, `tools/jstests/keyboard-tests.mjs`,
`AccessibleTrader.BrowserTests/TerminalPage.cs` + `ModalBrowserContractTests.cs`,
`AccessibleTrader.Tests/ChromeAccessibilityScanTests.cs`, `ChartArea.razor`). Each was proved red
first: one new jstest per fix (each reverted alone reddens exactly its own test), a widened C# scan
that reads the whole trap block for `offsetParent`, and a new scan over the harness sources for the
`return true`-on-zero-dialogs shape. The confirmation in a real engine is `probes/cdp-fix.mjs` with
`probes/cdp-fixpage.html` (the exact Toolbar alertdialog styling, the roved-out tree summary, and
Help-over-Settings), whose two outputs are `probes/cdp-fix-before.out` and `probes/cdp-fix-after.out`.
The one caveat in that probe is written into it: the first Tab-family key after a programmatic
`focus()` into the freshly shown fixed element is re-homed by Chromium itself in BOTH versions, so a
priming key is sent first and no verdict rests on it.

Not fixed, still recorded: items 4, 6 and 7 of the confirmed list, and everything under
"Unverified". The alertdialog still has no cold-start browser route (it needs a loaded chart with a
non-core series and an analytics-shaped provider); it is listed in `ModalRoutes.NoColdStartRoute`.

The browser harness itself ran on this box during the fix (153/153), and was shown to observe
`keyboard.js` by sabotage — which retires the "app harness cannot start here" line below.

## Synthesis

**The code improves everything it touches; the commit's headline claim is false.** `bc52e652` says
the destructive "strip your indicators and drawings" `alertdialog` is "trapped at last". It is not.

### Confirmed defects

1. **The only `alertdialog` in the app is still invisible to the trap** (Critical, WCAG 2.4.3).
   `keyboard.js:138` widens the selector and `:139` then filters `el.offsetParent !== null`.
   `Toolbar.razor:430-432` puts `style="position:fixed"` on the `alertdialog` element itself, and
   CSSOM-View defines `offsetParent` as `null` for a fixed element. ModalBase dialogs survive only
   because their `position:fixed` is on the parent `.modal-overlay`. Found independently by two
   specialists; **reproduced in Chromium 140**: `visibleDialogsSeenByTrap=0`, Tab `prevented=false`,
   Tab from the last button lands on a background control, Shift+Tab from the first likewise; the
   identical markup with `position:absolute` wraps correctly. **Four gates are green for four
   different reasons**: `keyboard-tests.mjs:67` fakes `offsetParent: {}` (same harness shape
   `553960f7` fixed for `querySelectorAll`); `TheJsTabTrapCoversTheWholeAriaDialogFamily` reads
   the selector string and cannot see the filter on the next line; the browser probe at
   `ModalBrowserContractTests.cs:199-204` uses the same filter **and returns `true` when it finds
   zero dialogs**; and the alertdialog is not a `ModalRoute`, so no browser test opens it.
2. **ObjectTreeModal Shift+Tab escape after one ArrowDown** (Serious, 2.4.3; regression from
   `553960f7`). The `summary` clause at `keyboard.js:159` has no `:not([tabindex="-1"])`;
   `ObjectTreeModal.razor:46-48` is `<summary role="treeitem" tabindex=…>` and `treeKeyboard.js:111-122`
   roves it to `-1`. On hosted/demo builds (no Manage Strategies button precedes the tree) the
   series div is index 1, no branch fires, and the browser walks backward past the `-1` summary and
   the `-1` heading out of the dialog. Node reproduction Q1h.
3. **Stacked dialogs: the trap moves focus from the top dialog INTO the one beneath** (Serious,
   2.4.3). `keyboard.js:141` picks `dialogs[dialogs.length - 1]`, which is DOM order fixed by
   `MainLayout.razor:102-149`. Help renders at `:105`, before 19 other modals, and F1 is in
   `allowedWhileModalOpen`. Settings then F1: from any position in Help, the first Tab is
   `preventDefault`ed and focus is sent to Settings' search box. Node reproduction Q2a/Q2b. This is
   the ordered-modal-stack item already at the top of the NEXT list, now with a demonstration.
4. **Two CSS fallbacks fail on the light themes when the theme bridge never publishes** (Moderate,
   1.4.11). `--crosshair-color: #ffd65c` is 1.40:1 on HighContrastLight and 1.35:1 on Paper;
   `--focus-outline-color: #ffff00` is 1.07:1 / 1.04:1 against the chart. Precondition:
   `MainLayout.razor:288` never reaching `keyboard.js:830`, and the catch at `:290` swallows that
   by design. No single literal fixes it — `#808080` is 1.93:1 on SteelGray, the default.
5. **Two claimed ratios in comments are wrong** (Minor): `ChartArea.razor:156` says Paper header
   15.29:1 (computed 14.38:1); `:113` says Paper crosshair 7.95:1 (computed 7.68:1). Both pass.
6. **The tab bar, status bar and speech buffers sit outside every landmark** (Moderate,
   pre-existing): `MainLayout.razor:87` renders `<TabBar />` between `</header>` and `<main>`.
7. **The toolbar scan sees two spellings of the role** (Minor, no live instance):
   `ChromeAccessibilityScanTests.cs:153` gates on `Contains("role=\"toolbar\"") || Contains("role='toolbar'")`,
   so the space-tolerant regex at `:161` never runs; `role = "toolbar"`, `role="toolbar group"`,
   `role="TOOLBAR"` and every `.cshtml` are invisible.

### Unverified, with what would verify each

- `role="menu"` context menus arm the modal counter, are not in the selector and have no
  `focusout` close — the "menu" half of audit §3.1(b) is untouched (browser test: open, Tab, assert
  closed-or-contained).
- The Skia keyboard-cursor crosshair at `OverlayLayer.cs:26` is `Crosshair.WithAlpha(150)`;
  composited it is 2.97:1 SteelGray, 2.96:1 Paper, 2.31:1 Solarized (render to verify).
- The focus-ring colour is chosen from `SurfaceRaised` but `ThemeService.cs:139` lets a user
  override `Background` alone; 1.00:1 is reachable with no warning.
- `ChromeAccessibilityScanTests.cs:196` is a one-literal check: `color: white`,
  `rgb(255,255,255)`, `#eee`, `#000` and a hard-coded colour on the PARENT are all missed.
- No test computes crosshair or ring contrast; `ThemeCoverageTests.cs:69-79` is a naive-luminance
  delta and `ThemeCssBridge.Luminance` (`:157`) omits gamma. Today's 12/12 is a palette property.
- `keyboard-tests.mjs` doubles that still hand the code its answer: `offsetParent: {}` (:67),
  `matchesSelector` ignoring attribute parts (:95-108, so `[contenteditable]`, `[controls]`,
  `a[href]` are untestable), the `tabindex="-1"` heading never shown to the selector (:112-119).

### Checked and correct — do not re-check

Single-dialog trap from the heading, a `<summary>`, a disabled control, a `tabindex="-1"` element,
the body, a background control, and a one-focusable dialog; every one of the 26 dialog-family files
has an unconditional Close/Cancel button; no `<object|embed|area|audio|video|iframe|dialog>` or
`contenteditable` anywhere in the app. Scroll release: native number/range/select/textarea were
never blocked (the form-control return at `:251-254` runs first), tablists/listboxes/trees stay
trapped, Ctrl+arrow stays trapped, nothing reaches .NET on a released key. Focus ring reaches both
the chart (3px inset, `#chart-interact-zone:focus-visible` wins) and the treeitems (2px), ≥ 7.10:1
on every built-in theme. All three `<nav>` landmarks named and distinct; JournalModal `role="group"`
named; no orphaned toolbar attributes or prose; the TouchNavBar tests now assert the landmark
contract. Every touched pair passes on all 12 themes: header ≥ 4.75:1, crosshair ≥ 4.08:1, ring
≥ 4.79:1. Both `app.css` copies byte-identical for every touched rule. Escape and focus return
untouched by either commit.

### One-line fixes, in order

1. `keyboard.js:139` and both browser predicates: filter on `el.getClientRects().length > 0`, and
   make the probe return `false` on zero dialogs; give `keyboard-tests.mjs` a `fixed: true` node
   option; add a browser route that opens the switch warning and presses Shift+Tab.
2. `keyboard.js:157-161`: append `:not([tabindex="-1"])` to every tag clause.
3. `keyboard.js:141`: `dialogs.find(d => d.contains(document.activeElement)) ?? dialogs.at(-1)`
   as the mitigation until the ordered stack exists.
4. Correct the two comment ratios; correct the "trapped at last" comment at `keyboard.js:125-133`.

## A technique worth keeping

The app's browser harness cannot start on this box, but **the Playwright Chromium binary at
`~/.cache/ms-playwright/chromium-1187/chrome-linux/chrome` runs headless and answers CDP**.
`probes/cdp-drive.mjs` is a dependency-free driver: spawn Chrome with `--remote-debugging-port`,
open a standalone page that loads the committed `keyboard.js`, and send trusted keys via
`Input.dispatchKeyEvent`. That is how defect 1 was observed rather than argued.
