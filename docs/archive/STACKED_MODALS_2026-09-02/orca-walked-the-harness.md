# Focus walker findings (fork, 2026-09-02) — CONCLUDED

## Mechanism: Orca, through AT-SPI, driving headless Chromium's caret

Orca is running on this box (pid 2817314, up 6 days) with the AT-SPI bus. Headless Chromium
enables its AT-SPI bridge when an assistive technology is present, so Orca sees every harness
page as a live document. When focus drops to `<body>` (or a page/dialog gets focus), Orca reads
the document: it moves the **caret** sentence by sentence at speech cadence (~0.8-1 s), and
Chromium moves keyboard focus to follow the caret whenever it enters a focusable element
(h2 tabindex=-1, role=tab buttons, the speech prompt's radio input).

Evidence (worktree `AccessibleTrader.BrowserTests/WalkerDiagTests.cs`, outputs `walker1.out`
/ `walker2.out` in the worktree root):

- With the bridge ON (default fixture): after `blur()`, `selectionchange` fires with a
  `type=Caret` selection walking the speech prompt's `<p>` at offsets 13 → 55 → 107 → 117
  (the sentence starts of "Pick the option… / You can change it… / Until you choose…"), each
  followed by a `scroll` on body (caret reveal), then `FOCUSIN INPUT` (the first radio) with
  no `focus()` call, no keydown, no mutation. 25 `type=Caret` selection changes across six
  scenarios. The parent's earlier run saw the same walk inside the Settings dialog
  (h2 → General → Appearance → Keyboard = Orca reading the dialog line by line).
- With `NO_AT_BRIDGE=1` in the browser's launch env (Playwright `Env`): **zero** post-blur
  events in all six scenarios, zero `type=Caret` selection changes anywhere; focus stays on
  `<body>` exactly as expected. Nothing else changed.

## Hypotheses ruled out
1. Selection-driven focus — CONFIRMED as the proximate mechanism (caret → focus follows), but
   the caret is moved by Orca, not by page script.
2. Fragment/URL navigation — no `hashchange`/`popstate`; `href` constant at `/`.
3. Blazor render batches / live-region writes — page-wide MutationObserver (childList +
   characterData + attributes) logged nothing after the blur while focus moved.
4. Playwright — happens with no polling and with the toolbar-click route (no `Page.Keyboard`).
5. Speech — scenario `sr` (no speech prompt) showed no walk, but only because there was no
   prompt text near the caret; the bridge experiment is the decisive one. Removing the
   aria-live regions did not stop it.
6. Chromium flags — the fixture passes only `--no-sandbox --disable-dev-shm-usage`; UA is
   HeadlessChrome/140. Not a flag; it is the environment's AT.

## Can it happen to a real user?
Yes, and it is not a bug in the app: it is a screen reader reading a document it was handed.
For a real Orca user, focus landing on `<body>` while a dialog is open is precisely the state
that hands Orca a whole page to read from the top, which is the reason the stacked-close focus
return (the parent's NEXT item) matters. The tab-button hops the parent saw are Orca's caret
entering focusable tab stops.

## For the harness
Every focus assertion made more than a few hundred ms after a focus drop is contaminated on
this box while Orca runs. Two options: (a) launch Chromium with
`Env = { ["NO_AT_BRIDGE"] = "1" }` in `TerminalBrowserFixture` (the harness is auditing what
the app exposes, not what Orca does with it; CI has no AT so behaviour matches), or (b) accept
and document it. (a) is a one-line, test-only change; it made all six diag scenarios inert.
Note `TerminalServerFactory.cs:150` already pins `orcaAvailable: false` on the server side for
the same reason.

The parent's `Closing_the_top_dialog_returns_focus_to_the_dialog_beneath_it` passed on the
unfixed tree because Orca refocused `settings-title` ~1 s after the close; with the bridge off
that test will be red on the unfixed tree, as intended.
