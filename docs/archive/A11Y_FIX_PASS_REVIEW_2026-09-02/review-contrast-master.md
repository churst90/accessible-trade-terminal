# Contrast Master review — commit bc52e652 (chart status header, hover crosshair, chart focus ring)

Reviewer: contrast-master (accessibility-agents 3.2.0). Review only; nothing in the tree was edited.
Scripts: `wcag_contrast.py` (parses ThemeService.cs; real WCAG 2.x relative luminance, 0.04045 / 12.92 / ((c+0.055)/1.055)^2.4, 0.2126/0.7152/0.0722, (L1+0.05)/(L2+0.05)), `guard_sensitivity.py` (replays the scan guard's regex against mutated COPIES of ChartArea.razor). Raw outputs: `wcag_contrast.out`, `guard_sensitivity.out`, `extra.out`.
Self-check: the script reproduces the commit's own HCLight numbers exactly (header 21.00:1, crosshair 11.45:1, old values 1.00/1.00/1.03/1.02, SteelGray old crosshair 2.94:1).

## VERDICT: SHIP

Every measured pair this commit touched passes on all 12 built-in themes (header >= 4.75:1, crosshair >= 4.08:1, ring >= 4.79:1); the only computed failures are in the DEGRADED path (:root fallback with the bridge never published) on the two light themes, plus two wrong numbers in a comment.

## CONFIRMED defects

1. **`:root` fallback crosshair `#ffd65c` is invisible on the two light themes if the bridge never runs.** `AccessibleTrader.BlazorClient/wwwroot/app.css:50` and `AccessibleTrader.WebHost/wwwroot/app.css:50`. Computed: **1.40:1 on HighContrastLight (#ffffff), 1.35:1 on Paper (#fdfbf6), 1.22:1 on Paper's gradient end**. WCAG 1.4.11 (3:1). Reachable only when `accessibleTrader.applyThemeVariables` (keyboard.js:830) has not run — `MainLayout.razor:288` calls it on first render and on every ThemeChanged, and the catch at MainLayout.razor:290 swallows a JS failure by design, so a permanent interop failure yields exactly this state. The Skia chart is painted from the theme without the bridge, so a light chart with a dark-palette fallback crosshair is the actual degraded picture. Severity: moderate (degraded path only). Fix: no single literal works — measured, `#808080` is 3.95:1 on white and 5.32:1 on black but **1.93:1 on SteelGray (#4e545e), the shipped default** (`#7a7a7a`, `#777777` likewise fail SteelGray); the fallback can only be honest for the dark default palette it belongs to, so the one-line fix is a comment at app.css:50 saying so, and the real fix is making the bridge's failure loud rather than swallowed (MainLayout.razor:290).

2. **`:root` fallback focus ring `#ffff00` fails on both light themes in the same degraded path.** `app.css:45` (both copies) feeds `#chart-interact-zone:focus-visible` at `app.css:274-277`. Computed: **1.07:1 on HighContrastLight chart, 1.04:1 on Paper chart; 1.21:1 on both themes' page background**. WCAG 1.4.11 / 2.4.13. Same precondition as (1); not introduced by this commit (the fallback predates it), but this commit added a NEW consumer of it on the app's primary control.

3. **Two numbers in the commit's own comments are wrong.** `ChartArea.razor:156` claims Paper header 15.29:1 — computed **14.38:1**; `ChartArea.razor:113` claims Paper crosshair 7.95:1 — computed **7.68:1**. Paper's values in ThemeService.cs are unchanged since bc52e652 (checked `git show bc52e652:…ThemeService.cs`). Both still pass; the HCLight numbers (21.00, 11.45) are correct. Fix: correct the two literals in the comments.

## UNVERIFIED concerns

- **The scan guard cannot see most regressions** (`ChromeAccessibilityScanTests.cs:196` regex `color\s*:\s*#(fff|ffffff)\b`). Replayed against mutated copies: `color: white` MISSED, `rgb(255,255,255)` MISSED, `#eee` MISSED, `#000` (invisible on the 9 dark themes) MISSED, parent `color: white` MISSED, parent colour deleted MISSED; `#FFF`/`#ffffff` on header or parent CAUGHT. It is a presence check for one literal, not a contrast check. What would verify: a guard that resolves the overlay's colour per theme and computes >= 4.5:1 with a real WCAG function (none exists in the codebase; `ThemeCssBridge.Luminance` at ThemeCssBridge.cs:157 omits gamma and says so).
- **No test computes the crosshair or ring against any background.** `ThemeCoverageTests.cs:69-79` checks the ring by naive-luminance delta > 0.3, `:125` checks only that `--crosshair-color:` is PRESENT in both app.css. All 12 themes passing WCAG today is a property of the current palette, not of a guard. Verify: same as above, 3:1 with a real formula, per theme.
- **Custom themes and the background override are outside every table here.** `ThemeService.cs:110` applies a JSON preset (`ThemePreset.ApplyTo`) and `:139` lets the user replace `Background` with any colour; `ThemeFields.cs:121` exposes `Crosshair` in the editor, which (per audit 3.8) validates with Euclidean RGB distance. A user can reach 1.00:1 on either pair with no warning. Verify: run the script's formula inside the editor's validation.
- **The Skia (keyboard-cursor) crosshair is a second, dimmer crosshair.** `OverlayLayer.cs:26` draws `theme.Crosshair.WithAlpha(150)` (a = 0.588), 1px dashed. Composited: **SteelGray 2.97:1, Paper 2.96:1, Solarized 2.31:1 FAIL** at 3:1; nine others pass. Not this commit's fix and drawn on a canvas, so not CSS-measurable; the commit comment "the same value the Skia renderer draws with" is true of the hue only. Verify by rendering the three themes and sampling.
- **Bridge-publish timing.** Between first paint and `OnAfterRenderAsync(firstRender)` the fallbacks render. Hover and focus both need user input, which almost certainly post-dates first render; I could not run the browser harness on this box to time it.

## Tables (from wcag_contrast.out)

### Q1 Header: theme.AxisText (GetThemeTextHex, ChartArea.razor:457) on theme.Background (overlay bg, ChartArea.razor:456), 4.5:1
| theme | text | bg | ratio | | sibling --text-muted on bg |
|---|---|---|---|---|---|
| SteelGray | #e8ecf2 | #4e545e | 6.43 | PASS | 5.42 PASS |
| Blackout | #e8e8e8 | #000000 | 17.14 | PASS | 10.13 PASS |
| Classic | #d1d4dc | #131722 | 12.07 | PASS | 7.22 PASS |
| AmberCrt | #ffb000 | #0d0b06 | 10.74 | PASS | 6.68 PASS |
| Walnut | #f0e3d2 | #2a201a | 12.59 | PASS | 8.82 PASS |
| Paper | #2a2722 | #fdfbf6 | 14.38 | PASS | 7.03 PASS |
| MidnightBlue | #d7dff2 | #11182b | 13.22 | PASS | 8.46 PASS |
| HighContrastDark | #ffffff | #000000 | 21.00 | PASS | 11.30 PASS |
| HighContrastLight | #000000 | #ffffff | 21.00 | PASS | 9.44 PASS |
| SoftDark | #b4b9c8 | #12141c | 9.38 | PASS | 6.69 PASS |
| Solarized | #839496 | #002b36 | 4.75 | PASS (1.8rem = 28.8px, large-text 3:1 also applies) | 6.46 PASS |
| BrailleOptimized | #ffffff | #000000 | 21.00 | PASS | 9.04 PASS |

### Q2a Crosshair: theme.Crosshair (--crosshair-color) on chart bg top / gradient end, 3:1
| theme | xhair | bg-top | ratio | | bg-end | ratio |
|---|---|---|---|---|---|---|
| SteelGray | #ffd65c | #4e545e | 5.46 | PASS | #22252a | 11.00 PASS |
| Blackout | #ffd54f | #000000 | 14.88 | PASS | flat | |
| Classic | #9da5b4 | #131722 | 7.22 | PASS | flat | |
| AmberCrt | #ffd266 | #0d0b06 | 13.73 | PASS | #070603 | 14.15 PASS |
| Walnut | #c9a227 | #2a201a | 6.58 | PASS | #18110d | 7.72 PASS |
| Paper | #1a4ca8 | #fdfbf6 | 7.68 | PASS | #f3efe6 | 6.92 PASS |
| MidnightBlue | #7dd3fc | #11182b | 10.59 | PASS | #080c18 | 11.70 PASS |
| HighContrastDark | #ffff00 | #000000 | 19.56 | PASS | flat | |
| HighContrastLight | #0000c8 | #ffffff | 11.45 | PASS | flat | |
| SoftDark | #78b4ff | #12141c | 8.55 | PASS | flat | |
| Solarized | #268bd2 | #002b36 | 4.08 | PASS | flat | |
| BrailleOptimized | #ffffff | #000000 | 21.00 | PASS | flat | |

### Q2b :root fallback #ffd65c on every chart bg, 3:1
SteelGray 5.46 PASS · Blackout 15.03 PASS · Classic 12.81 PASS · AmberCrt 14.07 PASS · Walnut 11.38 PASS · **Paper 1.35 FAIL (bg-end 1.22 FAIL)** · MidnightBlue 12.63 PASS · HighContrastDark 15.03 PASS · **HighContrastLight 1.40 FAIL** · SoftDark 13.15 PASS · Solarized 10.74 PASS · BrailleOptimized 15.03 PASS

### Q3 Focus ring: ThemeCssBridge.FocusRingFor (#ffff00 if naive lum(SurfaceRaised) <= 0.5 else #0020b0), width 3px, inset, 3:1
| theme | ring | chart bg | | chart end | | page bg (--bg-primary) | | toolbar | |
|---|---|---|---|---|---|---|---|---|---|
| SteelGray | #ffff00 | #4e545e 7.10 | PASS | #22252a 14.32 | PASS | #17191d 16.39 | PASS | #676e79 4.79 | PASS |
| Blackout | #ffff00 | #000000 19.56 | PASS | 19.56 | PASS | #0a0a0a 18.44 | PASS | 18.44 | PASS |
| Classic | #ffff00 | #131722 16.67 | PASS | 16.67 | PASS | #10131c 17.28 | PASS | 14.79 | PASS |
| AmberCrt | #ffff00 | #0d0b06 18.32 | PASS | #070603 18.87 | PASS | #120e06 17.92 | PASS | 17.04 | PASS |
| Walnut | #ffff00 | #2a201a 14.82 | PASS | #18110d 17.39 | PASS | #2e2119 14.51 | PASS | 8.83 | PASS |
| Paper | #0020b0 | #fdfbf6 11.07 | PASS | #f3efe6 9.98 | PASS | #e6e1d6 8.78 | PASS | 9.29 | PASS |
| MidnightBlue | #ffff00 | #11182b 16.44 | PASS | #080c18 18.17 | PASS | #050811 18.64 | PASS | 13.26 | PASS |
| HighContrastDark | #ffff00 | #000000 19.56 | PASS | 19.56 | PASS | #0e0e0e 17.98 | PASS | 17.16 | PASS |
| HighContrastLight | #0020b0 | #ffffff 11.45 | PASS | 11.45 | PASS | #e2e2e2 8.84 | PASS | 9.87 | PASS |
| SoftDark | #ffff00 | #12141c 17.12 | PASS | 17.12 | PASS | #14161e 16.81 | PASS | 14.94 | PASS |
| Solarized | #ffff00 | #002b36 13.98 | PASS | 13.98 | PASS | #181818 16.54 | PASS | 15.52 | PASS |
| BrailleOptimized | #ffff00 | #000000 19.56 | PASS | 19.56 | PASS | #181818 16.54 | PASS | 15.52 | PASS |

Fallback ring #ffff00 (bridge not published): all dark themes >= 7.10 PASS; **Paper 1.04 chart / 1.21 page FAIL; HighContrastLight 1.07 chart / 1.21 page FAIL**.

## Checked and found CORRECT

- **Q1 trace.** `ChartArea.razor:157` header `<div style="font-size: 1.8rem; margin-bottom: 1rem;">` sets no colour; its only ancestor inside the overlay is the overlay div itself (`:144-149`) which sets `color: @(GetThemeTextHex())` and `background: @(GetThemeBackgroundHex())`, opacity 1 / visibility visible while shown (`:179-183`), so the header is measured against a flat, opaque `theme.Background`, not the Skia gradient. `GetThemeTextHex()` (`:457`) = `ThemeService.Current.AxisText`; `GetThemeBackgroundHex()` (`:456`) = `ThemeService.Current.Background`. No hard-coded colour between them.
- **Q2 trace.** The hover crosshair is HTML, not canvas: two 2px opaque `<div>`s at `ChartArea.razor:116-117` with inline `background:var(--crosshair-color)`. The variable is set on `document.documentElement` by `keyboard.js:830-837` (`root.style.setProperty`) from `ThemeCssBridge.BuildVariables` (`ThemeCssBridge.cs:116` = `Css(theme.Crosshair)`, opaque colours emitted as `#rrggbb`), invoked from `MainLayout.razor:288` on first render and on every `ThemeChanged`. Custom properties inherit, so the inline `var()` resolves. `--crosshair-color` is in `VariableNames` (`ThemeCssBridge.cs:52`) and `ThemeCoverageTests.cs:105-113` enforces emit parity.
- **Q3 trace.** `#chart-interact-zone:focus-visible { outline: 3px solid var(--focus-outline-color); outline-offset: -3px }` (`app.css:274-277`, both copies); id selector beats `[tabindex]:focus-visible` (`:263`); the inline `outline: none` is gone (`ChartArea.razor:76`); no other rule targets the element (grep of both app.css, keyboard.js, all .razor). Width 3px, inset (deliberate, not flagged); 3px satisfies the 2.4.13 2px-perimeter minimum and the inset-must-be->2px practice. Ring colour is theme-driven via `FocusRingFor` (`ThemeCssBridge.cs:143`); all 12 themes >= 4.79:1 against chart, chart end, page and toolbar.
- **Q4 parity.** `diff` of the two app.css: `--crosshair-color: #ffd65c` (`:50`), `--focus-outline-color: #ffff00` (`:45`) and the `#chart-interact-zone:focus-visible` block (`:266-277`) are byte-identical, at identical line numbers. Drift exists ONLY in two unrelated places: BlazorClient `:749` has `flex-wrap: wrap;` the WebHost lacks; WebHost `:797-814` has a `.speech-prompt` block the BlazorClient lacks (consumer is `AccessibleTrader.WebHost/Components/SpeechOutputPrompt.razor:24`, WebHost-only, so the omission is coherent). UNVERIFIED-impact note on that drifted block: `.speech-prompt button` is `#fff` on `#4a90d9` = **3.34:1**, below 4.5:1 for its 1rem label; its focus ring `#fff` is 3.34:1 against the button and 15.11:1 against the prompt. Not touched by this commit.
- **Q5, what each test asserts.** `ChromeAccessibilityScanTests.TheChartsStatusChromeTakesItsColoursFromTheTheme` (`:173-216`): strips comments (`ModalContractScanTests.CodeOnly`), isolates `blackout-overlay…@code` by regex, asserts NO `color\s*:\s*#(fff|ffffff)\b` (case-insensitive) in that region, asserts the literal `rgba(255,255,255,0.45)` is absent from the file, asserts `var(--crosshair-color)` is present, and asserts `--crosshair-color` appears in both app.css. `ThemeCoverageTests.TheStylesheetFallback_declaresEveryVariableTheBridgeSets` (`:116-128`): every `VariableNames` entry appears as `name:` in both app.css. `HostParityTests.BothHeadsDefineTheSameThemeVariables` (`:159-177`): the SET of `--name:` declarations is equal between the two files (names only, values never compared). Regression outcomes: `style="color: white"` on the header NOT caught; `color:#FFF` caught; a hard-coded `color: #fff` on the PARENT caught, `color: white` on the parent NOT caught. The hover readout at `ChartArea.razor:118` (`color:#fff` on `rgba(18,18,18,0.92)`) is outside the scanned region and is fine at >= 15.33:1 on every theme.
- The old values are reproduced: old header 1.00:1 HCLight / 1.03:1 Paper; old crosshair 1.00 / 1.02 / SteelGray 2.94 — the audit's numbers were right.

## Could not finish
- No browser run on this box (harness cannot start here per memory); bridge-publish timing and the Skia crosshair are computed, not observed.
- Custom/JSON themes were not enumerated (none ship in the repo tree as far as grep shows).

## Contrast Master Findings Summary
- **Issues found:** 3 confirmed + 5 unverified
- **Critical:** 0 | **Serious:** 0 | **Moderate:** 2 | **Minor:** 1
- **High confidence:** 3 | **Medium:** 2 | **Low:** 3
