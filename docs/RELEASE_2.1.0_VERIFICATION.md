# 2.1.0 — verification checklist

**`v2.1.0` was tagged on 2026-07-28.** This list is kept as the record of what was and was not
checked before the cut, and as the template for the next release.

Two items were still open at the moment of tagging and are recorded here rather than quietly
dropped:

- **The MAUI desktop head was not built during this cycle.** The development machine has no MAUI
  workloads and none of that head's target frameworks target Linux, so it could not be. That head
  carries its own copy of `app.css`, which received every theming edit unseen — and CSS is not
  compiled, so no build would have validated it regardless. It needs a launch on Windows.
- **Not every dialog was opened on both heads.** Many were, on the WebHost, and doing so found
  three defects that a green build and a green suite had both missed.

Neither was treated as a blocker at the maintainer's decision. Both are the first thing to check
if a 2.1.1 becomes necessary.

---

## Why this document exists

Three specific gaps, all of them mine:

1. **The desktop head was never built.** This development box has no `maui-android`
   workload, so every build this cycle covered Core, WebHost, Components and Tests only.
   The MAUI head has its **own copy of `app.css`**, which received the same theming edits
   and has never been compiled or run.
2. **Fifteen dialogs had their colours rewritten, unseen.** 121 inline colour references
   across 25 razor files moved onto theme variables so dialogs stop being a light island
   in a dark application. If any dialog carried a light-background assumption that was
   missed, its text is now dark on a dark surface. Nothing would throw; it would simply be
   unreadable.
3. **No new feature has been run end to end.** All six have unit tests. None has fetched
   real data, persisted to real disk, or been driven from the keyboard by a person.

Unit tests do not cover any of this, which is the point of doing it by hand once.

---

## A. Build both heads

- [ ] `dotnet build AccessibleTrader.slnx -p:UseRazorSourceGenerator=false` on a machine with
      the MAUI workloads — **0 errors, 0 warnings**
- [ ] Launch the **desktop (MAUI)** head. It has its own `app.css`; confirm the toolbars,
      tabs and dialogs are themed and not falling back to the old dark-grey palette
- [ ] Launch the **WebHost** head

> The Razor source generator on SDK 10.0.301 miscompiles `<text>` and same-line code-block
> markup. Build with `-p:UseRazorSourceGenerator=false`. It only bites after a clean.

---

## B. Every dialog opens and is readable

This is the highest-risk item and the cheapest to clear. For each: open it, confirm the
text is legible against its background, headings and hint text are visible, and no control
has vanished into the surface behind it.

| Dialog | How to open | Readable? |
| --- | --- | --- |
| Settings | `F12` | [ ] |
| Help | `F1` | [ ] |
| Add Indicator | `Alt+A` | [ ] |
| Properties | focus a series, `P` | [ ] |
| Object Tree | `Alt+O` | [ ] |
| Watchlist / Screener | `Alt+M` | [ ] |
| Level Report (Zones) | `Alt+R` | [ ] |
| Alerts | `Alt+J` | [ ] |
| API Keys | `Alt+K` | [ ] |
| Trading Dashboard | `Alt+T` | [ ] |
| Order Book | `Alt+B` | [ ] |
| Strategies | `Alt+S` | [ ] |
| Sound Designer | `Alt+W` | [ ] |
| Custom Scripts | `Alt+,` | [ ] |
| Journal | `Ctrl+Alt+Shift+J` | [ ] |
| AI Analyst | `Ctrl+Alt+Shift+A` | [ ] |
| Save Workspace | `Ctrl+Alt+Shift+W` | [ ] |
| Load Workspace | `Ctrl+Alt+W` | [ ] |
| Drawing Tools | `Alt+D` | [ ] |
| My Data import | toolbar Import | [ ] |
| **Theme editor** | Settings → Appearance → Customise… | [ ] |

Inside **Settings**, check every tab — the colour sweep touched all of them, and the tabs
you rarely open are the ones most likely to hold a missed assumption.

- [ ] General · [ ] Appearance · [ ] Keyboard · [ ] Alerts · [ ] About

---

## C. The new features, once each

### Watchlist and screener (`Alt+M`)
- [ ] Create a list; add a symbol via the Market → Provider → Sub-type → Symbol picker
- [ ] Type in **Filter symbols** — the count under the picker updates
- [ ] **Add all shown** adds the filtered set and announces how many
- [ ] **Load** on a row puts that symbol on the chart
- [ ] Build a screen: add two filters, pick a logic mode, save it
- [ ] Run it against the list — progress is spoken, results table is navigable
- [ ] Confirm a symbol that fails to fetch is **shown as failed, not dropped**
- [ ] Restart the app: the watchlist and the saved screen are still there

### Respect report (`Alt+R`)
- [ ] Opens and populates on a loaded chart
- [ ] Both tabs (levels, moving averages) have rows
- [ ] **Re-measure** and **Speak summary** both work

### Bar replay (`Ctrl+Alt+Shift+P`, or `F11` on desktop)
- [ ] Starts at the cursor bar and hides everything after it
- [ ] `F9` reveals, `Shift+F9` steps back, `F10` auto-advances
- [ ] Stopping restores the full history **and the prior viewport**

### Split view (`Ctrl+Alt+Shift+S`)
- [ ] With two tabs open, splits; `Ctrl+Alt+Shift+E` cycles the second pane
- [ ] `Ctrl+Alt+Shift+O` switches side-by-side / stacked
- [ ] Speech and navigation stay with the **active** chart
- [ ] Clicking in the ACTIVE pane lands on the bar under the cursor
- [ ] Clicking the read-only second pane, or the divider, does nothing

### Market Structure and Value Deviation
- [ ] Market Structure appears automatically on a new chart; squares and crosses, not triangles
- [ ] Settings → Analysis turns it off for new charts and leaves open ones alone
- [ ] Value Deviation adds cleanly and its **Show tiers from** parameter thins the marks

### Toolbar
- [ ] Watch, Zones, Journal and AI open their panels
- [ ] Split and Replay toggle **and show pressed state**
- [ ] Pan and Zoom are **enabled** whenever a chart has data — this was broken until
      `c38d7adc` and is worth confirming directly

---

## C2. Added AFTER this checklist was first written

Five commits landed after 2.1.0 was staged. None of it has been seen running, and it is the part
most at risk of being skipped, because someone working from their memory of "what 2.1 contains"
will not think to include it.

### Theme editor (Settings → Appearance)
- [ ] **New theme** opens the editor seeded from the theme in use
- [ ] All seven sections appear: top bar, chart area, candles and volume, overlays and drawings,
      bottom bar, dialogs, text and chrome
- [ ] Changing a colour updates the preview and is announced
- [ ] **Revert** on one field returns it to the base theme; **Reset all** clears everything
- [ ] **Flat** clears an optional gradient end
- [ ] Contrast warnings appear when a colour is set close to its background — and do NOT
      auto-correct it
- [ ] **Save and use** applies it, and the theme survives a restart
- [ ] **Copy theme text** / **Paste a theme** round-trips
- [ ] Deleting a custom theme falls back to the one it was based on

### The four newer themes
- [ ] Amber CRT · [ ] Walnut · [ ] Paper · [ ] Midnight Blue
- [ ] **Paper especially** — the only light theme among the new four, and light themes are where
      a missed foreground assumption shows up worst

### Two speech settings that had no control until now
- [ ] Settings → General → **Speak the time** changes when the timestamp is spoken
- [ ] Settings → General → **Speak values as** changes name-then-value versus value-only
- [ ] Both survive a restart

### Three new shortcuts
- [ ] `Ctrl+Alt+Shift+Y` describes the layout, and what it says MATCHES the chart — bar count, date
      range, price range, gridline step, pane structure
- [ ] `Ctrl+Alt+Shift+K` shows all hidden components and announces how many
- [ ] `Ctrl+Alt+Shift+U` unmutes all and announces how many
- [ ] Both announce "nothing was hidden/muted" when there is nothing to do

### Add Indicator
- [ ] Search narrows the list, and matches a DESCRIPTION word (try "volatility") not only names
- [ ] The match count is spoken as you type
- [ ] The selected indicator's description is shown and announced
- [ ] An empty result explains which filter to relax

### Dialog changes
- [ ] Dialog titles no longer draw a yellow box; focus still visibly moves into the dialog
- [ ] Settings no longer scrolls sideways at any window width
- [ ] Trading Dashboard: SELL reads as available, not disabled; the mode says "No API key"
      rather than "None"
- [ ] Shortened setting labels still make sense, and their hints carry what was removed

## D. Theming

- [ ] Each of the ELEVEN themes applies to the whole window, not just the chart: Steel Gray,
      Blackout, Classic, Midnight Blue, Amber CRT, Walnut, Paper, High Contrast Dark,
      High Contrast Light, Soft Dark, Solarized, Braille
- [ ] **High Contrast Light** especially — it is the one where a missed light/dark
      assumption shows up worst
- [ ] The **Window gradient** switch blends the three regions; the two colour pickers work
- [ ] Custom up/down colours apply, survive a theme change, and **"Use theme's"** resets
- [ ] Setting an up colour close to the background raises the warning in Settings
- [ ] Keyboard focus rings are clearly visible on every theme

---

## E. Accessibility, which is the actual product

- [ ] Orca reads the new toolbar buttons by their full names ("Watchlists and Screener",
      not "Watch")
- [ ] The screener results table reads cell by cell with column headers
- [ ] The screen builder's filter rows read as a group, and each row's plain-language
      summary is reachable
- [ ] Bar replay announces reveals and the stop
- [ ] Nothing in the theming work changed what is spoken

---

## Deliberately NOT in 2.1.0

Recorded so these are decisions rather than things nobody noticed.

- **Button finishes as a separate axis** (metallic, wood) — a new feature, not a gap. Colour
  themes work; adding a finish axis is 2.2 work and nothing is worse for its absence.
- **A per-symbol webhook route editor.** `SetupAlertBridge` honours `alerts.setups.webhookMap`
  and nothing can populate it, so the routing feature is real and unreachable. This PREDATES
  2.1 — it is not a regression — and the single fallback webhook does have a UI. Recorded in
  `SettingsWiringAuditTests`' allow-list so it stays visible.

## When it is clean

```
git tag -a v2.1.0 -m "2.1.0"
git push origin v2.1.0
```

Then move the `## [2.1.0] — unreleased, staged for verification` heading in
`docs/CHANGES.md` to `## [2.1.0] — <date>`.

## If something is wrong

Fix on `main` and re-run this list for whatever the fix touched. Nothing here is tagged
yet, so there is no release to withdraw — which is the whole reason for cutting after the
pass rather than before it.
