# ARIA specialist review — commit bc52e652, role="toolbar" removal (four sites)

Reviewer: aria-specialist (review only, no edits). Date 2026-09-02. Tree: main, clean.
Standing rule: every finding is CONFIRMED (traced to file:line) or UNVERIFIED (inferred).

## Scope read
- `git show bc52e652 -- Toolbar.razor IndicatorBar.razor TouchNavBar.razor JournalModal.razor`
- Current copies of those four files
- docs/ACCESSIBILITY_AUDIT_2026-09-01.md §4 "Structure and semantics"
- AccessibleTrader.Tests/ChromeAccessibilityScanTests.cs (toolbar scan)
- AccessibleTrader.Tests/Blazor/TouchNavBarTests.cs diff
- Layout/MainLayout.razor, Pages/Home.razor, Routes.razor, wwwroot/index.html, WebHost App.razor

(Findings appended below as each question is closed.)

---

## VERDICT: SHIP

The four role="toolbar" removals are correct ARIA (native `<nav>` + unique `aria-label`, and `role="group"` + name inside the dialog), leave no orphaned toolbar attributes or prose, and are asserted by a contract-shaped test; the only defects found are pre-existing landmark gaps and a scan guard that sees one spelling of the role.

---

## CONFIRMED defects (all traced; none introduced by bc52e652)

### D1. The tablist sits outside every landmark (pre-existing, audit-listed, not fixed by this commit)
- **Location:** `AccessibleTrader.BlazorClient.Components/Layout/MainLayout.razor:87` (`<TabBar />` between `</header>` at :83 and `<main>` at :89); `TabBar.razor:23-28` renders `<div role="tablist" …>` with no landmark ancestor.
- **How confirmed:** read the layout tree; TabBar's root is the tablist itself, and MainLayout wraps it in nothing.
- **Rule:** APG landmark guidance ("all perceivable content should be contained in a landmark"); WCAG 1.3.1 (A) at most advisory here. Same for `<StatusBar />` at `MainLayout.razor:169` (`role="status"`, `StatusBar.razor:8`) and the two speech buffers at `:157-165`.
- **Severity:** moderate. The audit §4 line named "tab bar … status bar" in the same sentence as the toolbar; the commit fixed three of five. Not a regression.
- **Fix (one line):** wrap TabBar in `<nav aria-label="Workspace tabs">` or move it inside the main toolbar's `<nav>` — the tablist's own long aria-label stays as the widget name.

### D2. The toolbar scan guard sees exactly two spellings of the role
- **Location:** `AccessibleTrader.Tests/ChromeAccessibilityScanTests.cs:153` — `if (!text.Contains("role=\"toolbar\"") && !text.Contains("role='toolbar'")) continue;` gates BOTH later checks (:155 and :161).
- **How confirmed:** replicated `CodeOnly` (`ModalContractScanTests.cs:42-49`) and the guard body in `scratchpad/scan_replica.py` and ran 13 synthetic inputs. Results:
  - SEEN (red): `role="toolbar"`, `role='toolbar'` on any element; `<nav role="toolbar">`.
  - NOT SEEN (guard `continue`s before the regex): `role = "toolbar"` (spaces — the :161 regex allows `\s*` but never runs), unquoted `role=toolbar` (valid HTML), multi-token `role="toolbar group"` / `role="group toolbar"` (ARIA fallback roles — the first recognised token wins, so "toolbar" is exposed), `role="TOOLBAR"`, `role=@("toolbar")`, `role="@Role"` with the string in `@code`.
  - GREEN despite the role: `<div role="toolbar">` in any file that has an `@onkeydown` **anywhere** (the check at :155 is file-scoped, not element-scoped; 14 of the component files have one). The nav-override regex at :161 is the only element-scoped check.
- **File set walked:** `RazorFiles()` at :37 = `AccessibleTrader.BlazorClient.Components/**/*.razor` only. Not walked: the six `AccessibleTrader.WebHost/Components/*.razor`, the ten `AccessibleTrader.WebHost/Pages/Account/*.cshtml`, `AccessibleTrader.BlazorClient/wwwroot/index.html`, any `.js` (`setAttribute('role', …)`), any `.cs` string literal. Today none of those contain a toolbar role (repo-wide grep below), so this is a blind spot, not a live defect.
- **Rule:** repo standing rule "scan guards need a path check, not a presence check"; this one is a presence check on one spelling.
- **Severity:** minor (no live instance). **Fix (one line):** replace the `Contains` gate with `Regex.IsMatch(text, @"role\s*=\s*[""']?\s*(?:[\w-]+\s+)*toolbar\b", IgnoreCase)`, and make the onkeydown check element-scoped or drop it in favour of the regex on any element.

### D3. Redundant explicit landmark roles on native elements (pre-existing, cosmetic)
- **Location:** `MainLayout.razor:81` `<header role="banner">`, `:89` `<main role="main">`, `:171` `<footer role="contentinfo">`.
- **How confirmed:** read. Each native element already carries that implicit role (header at the top of the page maps to banner). No double-announcement in practice — the explicit role equals the implicit one — but it is the pattern the ARIA-in-HTML spec marks "NOT RECOMMENDED".
- **Severity:** minor. **Fix:** delete the three `role=` attributes. Not from this commit; do not block on it.

---

## UNVERIFIED concerns

### U1. Nothing observes the landmark in a real accessibility tree
- The bUnit assertions (`TouchNavBarTests.cs:42,58,65,81,95`) and the C# scan prove the markup; no test asks a browser for `role=navigation` with the expected name. CI's browser job is the only place that could (this box cannot start the harness, per the commit message). **Verify by:** one Playwright/Chromium assertion `page.getByRole('navigation', { name: 'Main toolbar' })` in `AccessibleTrader.BrowserTests`, plus the same for "Indicator controls".

### U2. Screen-reader announcement text for `Indicator controls`
- The nav contains a `<label for="indicator-select">Focused Indicator:</label>` (`IndicatorBar.razor:13`) whose select ALSO has `aria-label="Select focused indicator"` (:15). The aria-label wins, so the visible "Focused Indicator:" text is not the control's name (2.5.3 Label-in-Name: "Select focused indicator" does contain "Focused Indicator", so it passes). Pre-existing, not from this commit. **Verify by:** NVDA read of the select; expected "Select focused indicator, combo box".

---

## What I checked and found CORRECT

**Q1 — names and co-existence.**
- `Toolbar.razor:42` `<nav … aria-label="Main toolbar">`; `IndicatorBar.razor:11` `<nav … aria-label="Indicator controls">`; `TouchNavBar.razor:32` `<nav … aria-label="Touch navigation">`. All three have accessible names; all three are distinct strings.
- Render sites: all three are children of `MainLayout.razor` (:85, :93, :97), so Toolbar and IndicatorBar ALWAYS co-exist; TouchNavBar joins them only when `_isTouchDevice` (`TouchNavBar.razor:28`, set by the `accessibleTrader.isTouchCapable` JS probe) and the TouchNavBar setting is not "hide". Maximum simultaneous set = 3 navs, names unique. No `<nav>` exists inside any modal (grep of `<nav\b|role="navigation"` across Components + WebHost/Components: only the three). `ModalCatalog.cs:92,94` render Toolbar/IndicatorBar in isolation for the modal-contract sweep — not a page.
- Names follow the operating definition (brief, function-first, no role word, capitalised). Casing changed "Main Toolbar"→"Main toolbar" and "Indicator Controls"→"Indicator controls"; no test or doc referenced the old casing (grep of Tests/, BrowserTests/, tools/, docs/ — only `docs/TODO.md` narrative hits).

**Q2 — no orphans.**
- `aria-orientation`, `aria-activedescendant`, `aria-controls`: zero occurrences in the four files (grep). The only `aria-orientation`/`aria-activedescendant` in chrome belong to the tablist (`TabBar.razor:28-29`), where they are correct.
- Prose: `HelpModal.razor:299` "Tab / Shift+Tab — Navigate toolbar controls" describes the flat-Tab model that IS implemented — correct, not orphaned. `HelpModal.razor:40,72,73,329,337` "arrow" lines refer to chart bars, volume-profile bins, the bar navigator and the tab bar — none claims arrow keys in a toolbar. `TouchNavBar.razor:14,85` and `TouchNavBarTests.cs:1,71,89` use "toolbar" as an English noun for the strip, not as an ARIA claim — acceptable.
- No stylesheet selected on `[role="toolbar"]`; styling is by class (`.toolbar`, `.indicator-bar`, `.touch-nav` in both `app.css` copies), so the visual is unchanged.

**Q3 — JournalModal group name.** `JournalModal.razor:40` `<div role="group" aria-label="Journal filters">` inside `role="dialog" aria-labelledby="journal-title"` (:23-25) whose target `<h2 id="journal-title">` exists at :29. Group has a direct `aria-label`; no id reference to break. Buttons inside carry `aria-pressed` (:46) and explicit names (:54, :59). Correct.

**Q4 — repo-wide grep.** `role="toolbar"` / `role='toolbar'` / `"toolbar"` / `'toolbar'` across `*.razor, *.cshtml, *.html, *.js, *.mjs, *.cs, *.ts` (excluding bin/obj/.claude/node_modules): live occurrences are ONLY in razor comments (`Toolbar.razor:32-37`, `IndicatorBar.razor:7-8`, `TouchNavBar.razor:30`, `JournalModal.razor:37`), the scan's own source (`ChromeAccessibilityScanTests.cs:131-163`), and an English string in `BrowserTests/ModalRoute.cs:39`. No `role=@(` binding exists anywhere in the repo. No `.cshtml` contains the word. `ToolbarIconButton.razor` has no `@attributes` spread, so a role cannot arrive through it. See D2 for what the scan itself can and cannot see.

**Q5 — landmark inventory of the main page (MainLayout + Home.razor, which is empty).**
| Landmark | Name | Source |
|---|---|---|
| banner | (from `<h1 id="main-heading">`; visually-hidden) | `MainLayout.razor:81-83` |
| navigation | Main toolbar | `Toolbar.razor:42` |
| navigation | Indicator controls | `IndicatorBar.razor:11` |
| navigation | Touch navigation (touch devices only) | `TouchNavBar.razor:32` |
| main | — (single main; unnamed is correct) | `MainLayout.razor:89` |
| contentinfo | — | `MainLayout.razor:171` |
- No `complementary`, `search`, `form`, or page-level `region`. The one `role="region"` (`SummaryExport.razor:73`) lives inside `BuildSetupTab` inside the Strategy modal, so it is not on the page landmark list while dialogs are closed.
- Duplicates without unique names: none. Missing main: no. Count = 5 (6 on touch), within the canonical set.
- Content outside every landmark: TabBar (`:87`), the two `aria-speech-*` live regions (`:157-165`), StatusBar (`:169`), IconSprite (`:74`, `aria-hidden="true"` — fine), VisualEarconOverlay (`aria-hidden="true"` — fine), and all modal overlays (dialogs; fine). See D1.
- `Toolbar.razor:353` `<div role="group" aria-label="Chart view">` is a correctly named group nested inside the nav. The Toolbar's inline `role="alertdialog"` (`:~430`) is after `</nav>`, so it is not inside the landmark — correct for an overlay.

**Q6 — TouchNavBarTests selector.** Diff replaced `[role='toolbar']` with `nav[aria-label='Touch navigation']` at five sites (`:42,58,65,81,95`). This asserts the contract — a native navigation landmark bearing the user-facing name — not an implementation detail; `<nav>` is the semantic per the First Rule of ARIA, and the string is the name a screen-reader user hears in the landmark list. The test names/comments still say "toolbar" (`:1,71,89`) — cosmetic only. The assertions are not vacuous: `Assert.Empty` on desktop is paired with `Assert.NotEmpty` on touch (`:58`) so the selector is proven to match when the bar exists.

**Also confirmed correct**
- The `CodeOnly` stripper (`ModalContractScanTests.cs:42-49`) does remove the new explanatory comments that spell `role="toolbar"`, so the guard does not fail on its own documentation (replica case "in a razor comment only" → skipped).
- Buttons in all three navs are real `<button>` elements with `aria-label` and `aria-hidden` glyphs (`ToolbarIconButton.razor:24-28`, `TouchNavBar.razor:33-74`), so no icon-only control is unnamed.

---

## Not finished / out of reach
- No browser-level (accessibility tree) verification was possible on this box — U1 stands.
- I did not sabotage the scan guard in the tree (review-only rule); the replica script stands in for it and its 13 cases are listed in D2.

## ARIA Specialist Findings Summary
- **Issues found:** 3 confirmed (all pre-existing or guard-scope), 2 unverified
- **Critical:** 0 | **Serious:** 0 | **Moderate:** 1 (D1) | **Minor:** 2 (D2, D3)
- **High confidence:** 3 | **Medium:** 0 | **Low:** 2 (U1, U2)
