# Platform Strategy & Forward Plan

A decision record and engineering plan, written 2026-06-25. This complements — does not
replace — `docs/TODO.md` (the phase-by-phase task tracker) and `docs/PLATFORMS.md`
(the current-state compatibility matrix). Where those describe *what is*, this describes
*what we decided and what we plan to build next*, with enough detail to act on later.

## Contents

1. [The two heads: MAUI vs WebHost](#1-the-two-heads-maui-vs-webhost)
2. [The Linux script-sandbox gap and L5](#2-the-linux-script-sandbox-gap-and-l5)
3. [Braille / tactile display: enable toggle + hot-plug](#3-braille--tactile-display-enable-toggle--hot-plug)
4. [Touch and mobile screen-reader navigation](#4-touch-and-mobile-screen-reader-navigation)
5. [Braille on the WebHost: is it possible?](#5-braille-on-the-webhost-is-it-possible)
6. [Documentation status: manual, quick start, scripting, AI](#6-documentation-status)
7. [Open-work inventory](#7-open-work-inventory)

---

## 1. The two heads: MAUI vs WebHost

**Decision: keep both. They cover disjoint platforms; neither is redundant.** Verified
against the code (`RoslynScriptingService.CreateDefaultLauncher`, the DI registrations in
both `ServiceCollectionExtensions.cs`, `IsBrowserHost` gates, `Program.cs`).

The two heads are not competitors. They split the platform map:

- **MAUI Blazor Hybrid** → Windows, macOS, **and the only path to iOS / Android**.
- **WebHost (ASP.NET Core Blazor Server)** → **Linux** (MAUI has no Linux head) and the
  public-website chart demo. Single-user, localhost-only, unauthenticated
  (`appsettings.json` binds `http://localhost:5145`, zero auth, all services `AddSingleton`).

What MAUI uniquely provides (no WebHost equivalent):

- **Mobile, at all.** iOS/Android exist only through MAUI. The WebHost is a local server;
  turning it into a phone client would mean either re-wrapping it in a WebView (that *is*
  MAUI) or hosting it multi-tenant in the cloud — a different, much larger project that it
  is explicitly not built for.
- **The Dot Pad tactile display** — Windows-native P/Invoke (`WindowsDotPadNative` →
  `DotPadSDK-3.0.0.dll`). WebHost/Linux gets `NullDotPadNative` (no-op).
- **Native audio** (WASAPI / AudioTrack / AVAudioEngine) vs the WebHost's
  `pw-cat`/`pacat`/`aplay` pipe — lower latency, fewer moving parts.
- **Native chart rendering** (full-fps `SKCanvasView` overlay) vs the WebHost's
  server-side PNG re-encode throttled to ~10 fps.
- **Real OS keychain** (DPAPI / Keychain / Keystore) vs the WebHost's DataProtection
  key-files on disk.

What the WebHost uniquely provides:

- **Linux** — the maintainer's own platform (Orca over D-Bus). For Linux it is the primary
  client, not a fallback.
- **The public web chart demo** deploys from the same project.
- No MAUI workload required to build or run.

**Retirement cost, stated plainly.** Retiring MAUI would abandon all mobile, the Dot Pad,
native audio/rendering, and the real keychain — and there is no path back to phones without
it. Retiring the WebHost would abandon Linux and the web demo. Neither is worth it.

**Sequencing recommendation:**

- Lead releases with what is real today: the **WebHost (Linux)** + **MAUI desktop
  (Windows/macOS)**.
- Treat **MAUI mobile (iOS/Android) as a deferred-but-alive track** — it is the only road
  to phones, but it is blocked on touch input and chart accessibility (§4) and on flaky CI
  release builds. Do not let it gate desktop/web releases.
- Close the **Linux script-sandbox gap (§2)** before encouraging third-party `.atpkg`
  sharing, because that is the one place Linux is materially weaker than the other heads.

---

## 2. The Linux script-sandbox gap and L5

**Finding.** User-compiled Roslyn indicators/strategies run out-of-process on every
platform, but the *OS-enforced* sandbox only exists on three:

| OS | Launcher | Isolation |
|---|---|---|
| Windows | `WindowsAppContainerLauncher` | AppContainer — no net, no fs |
| macOS | `MacSandboxExecLauncher` | `sandbox-exec` deny-default |
| Android | `AndroidIsolatedProcessLauncher` | `isolatedProcess`, own UID |
| iOS | `RefusingScriptWorkerLauncher` | refused outright |
| **Linux (WebHost)** | **`DefaultProcessLauncher`** | **none — plain `Process.Start`** |

`CreateDefaultLauncher()` falls through to `DefaultProcessLauncher` on Linux. What still
protects Linux: (1) the compile-time `CSharpSyntaxWalker` blocklist rejects `System.IO` /
`System.Net` / reflection namespaces; (2) the worker is a *separate process*, so the host's
in-memory secrets (keychain handle, live authenticated sockets, API keys) are unreachable.
What is missing: (3) the kernel denying filesystem/network. So a script that bypasses the
syntax walker via a reflection trick would have free rein over `$HOME` and the network on
Linux, where the other platforms' kernels would still stop it. The realistic attack is
importing a stranger's `.atpkg`.

**Status: SHIPPED 2026-06-25.** Implemented as described below; see `TODO.md` L5 and
`LinuxBwrapLauncherTests`. Install the `bubblewrap` package on the Linux host to get
the sandbox (it falls back to process-only if absent). The remaining items in the plan
(`$HOME` tmpfs, `--clearenv`, seccomp BPF) are deferred hardening, not required by the
threat model.

**Plan — L5: `LinuxBwrapLauncher`** (parallels the three OS launchers).

- New `LinuxBwrapLauncher : IScriptWorkerLauncher` in
  `AccessibleTrader.Core/Services/Scripting/`.
- Launch the worker under **bubblewrap** with a *minimal* bind set — not `--ro-bind / /`,
  which would still let a script read `~/.ssh`. Bind only what the CLR + worker need:

  ```
  bwrap \
    --unshare-all --die-with-parent --new-session \
    --ro-bind <dotnet-root> <dotnet-root> \
    --ro-bind <worker-dir>  <worker-dir> \
    --proc /proc --dev /dev --tmpfs /tmp \
    --setenv DOTNET_… … \
    -- <worker-exe>
  ```

  `--unshare-all` includes `--unshare-net` (no sockets) and a private PID namespace.
  stdin/stdout/stderr (fds 0/1/2) are inherited, so the existing stdio frame protocol is
  unchanged. `$HOME` is never bound, so the script cannot even *read* user files.
- **Detection + fallback.** In `CreateDefaultLauncher()` add:
  `if (OperatingSystem.IsLinux()) return BwrapOnPath() ? new LinuxBwrapLauncher() : new DefaultProcessLauncher();`
  — keep the current behaviour (with its "OS-level sandbox not available" security notice)
  when `bwrap` is absent.
- **Defense-in-depth (stretch).** bwrap accepts `--seccomp <fd>` with a compiled BPF
  whitelist (the syscall set listed in `SANDBOX_DESIGN.md` §Linux). Namespaces alone already
  cover the threat model; add seccomp later if warranted.
- **Tests.** A launcher test pinning the argv, plus an integration test that a script doing
  `File.Open`/`Socket` fails inside the sandbox. Mirror the existing launcher test pattern.
- **On ship:** flip `docs/PLATFORMS.md` row from 🏗️ to ✅ and the TODO L5 box; update §1's
  sequencing note.
- **Flatpak note:** if the app is later distributed as a Flatpak, nested bwrap needs
  `--unshare-user` / `flatpak-spawn`. The current `dotnet run` deploy is unaffected.

Until L5 ships, the cheap interim mitigation is to gate `.atpkg` *import* (not first-party
scripts) behind a Linux-specific warning in `CustomScriptsModal`, since the import path is
the only way foreign code arrives.

---

## 3. Braille / tactile display: enable toggle + hot-plug

**Current state (verified).** There is **no enable/disable setting** for the tactile
display (the "Braille Optimized" item in Settings is a *visual theme*, `ThemeType.Braille`,
unrelated to the device). The Dot Pad is **always-on when a Windows device is present at
startup**: `TactileCanvasCoordinator`'s constructor fires `_ = TryConnectAsync()` once;
`DotpadTactileDriver.ConnectAsync` runs a 2 s USB scan + 4 s/port probe. Disconnect *is*
detected (the SDK's `Disconnected` callback sets `IsConnected=false`) but is **not announced
to speech**, and a device **plugged in after startup is never detected** — there is no
polling and no hot-plug listener. On non-Windows, `NullDotPadNative` makes the whole path a
clean no-op.

**Status: SHIPPED 2026-06-25.** The enable toggle, opt-in startup detection, the
serial-port hot-plug watch, and spoken connect/disconnect announcements are all
implemented (Settings → General → "Enable braille / tactile display output";
`accessibility.braille.enabled`; `TactileCanvasCoordinator` + the new
`ITactileDriver.ConnectionChanged` event). The plan below is what was built — note
the default is **off** (opt-in), since the device scan opens COM ports.

**Plan.**

1. **Setting key.** Add `accessibility.braille.enabled` (bool) to `SettingsManager`.
   Semantics:
   - *Enabled* → run detection at startup; if a display is found, connect and announce;
     keep a hot-plug watcher running so a later plug-in is detected.
   - *Disabled* → **skip startup detection entirely** (don't even run the 2 s USB scan /
     4 s probe — a measurable startup saving) and tear down any active connection.
2. **Settings UI.** Add an "Enable braille / tactile display output" toggle to the
   Accessibility section of `SettingsModal.razor`. On Windows it is live; on other heads
   show it disabled with a note ("Requires Windows and a supported display"), since the
   driver is Windows-only today (§5 covers WebHost feasibility). Wire toggle-on →
   `TryConnectAsync()` + start watcher; toggle-off → disconnect + stop watcher.
3. **Hot-plug detection (Windows).** Prefer event-driven over polling: a
   `ManagementEventWatcher` on `Win32_DeviceChangeEvent` (or `__InstanceCreationEvent` over
   `Win32_PnPEntity`) for device *arrival*, paired with the existing SDK `Disconnected`
   callback for *removal*. On an arrival event while braille is enabled and not yet
   connected, run the existing scan/connect. A 3–5 s COM-list poll is an acceptable fallback
   where WMI is unavailable.
4. **Announcements.** The coordinator already injects `ISpeechFeedbackRouter` (for F1–F4)
   and subscribes to driver events — that is the right home. Add `Connected` / `Disconnected`
   driver events carrying the **display name** (read via the SDK's `DOT_PAD_GET_DISPLAY_INFO`,
   falling back to "Dot Pad"). The coordinator speaks `"{name} connected"` /
   `"{name} disconnected"`. Nothing is spoken when braille is disabled.
5. **Tests.** Toggle gates detection (enabled→probe, disabled→no probe); arrival event drives
   a connect attempt; connect/disconnect each speak once with the resolved name.

Effort: ~1–2 focused days, all Windows-side, additive, no cross-platform risk.

---

## 4. Touch and mobile screen-reader navigation

This is the single biggest gap and the gate on a mobile release.

> **STATUS UPDATE 2026-07-09 — web touch layer SHIPPED (Finalization plan Phase C,
> first pass).** The Blazor/web side of this section is implemented and tested:
>
> - **Direct-touch gestures** in `keyboard.js` (both hosts): tap = select + hear the
>   bar, one-finger drag = pan, pinch = anchored zoom, double-tap = jump to live,
>   long-press = context menu. The gesture state machine synthesizes the SAME .NET
>   bridge calls the mouse produces, so all gestures reuse the tested mouse pipelines
>   — no separate gesture command path exists on the web (the `IGestureService`
>   below remains the design for the NATIVE capture path only).
> - **Screen-reader bar navigator**: a real `<input type="range">` beside the chart —
>   the web analog of the iOS adjustable trait. VoiceOver/TalkBack adjust it natively
>   (flick up/down); every step routes through `NavigateAction` + standard feedback.
>   Known limit: iOS VoiceOver steps web sliders by ~10% of range; per-bar granularity
>   on iOS needs the native adjustable element below. TalkBack honours `step=1`.
> - **Touch toolbar** (`TouchNavBar.razor`, coarse-pointer devices only): Prev/Next
>   bar, Prev/Next component, Play/Stop, Chart menu as 48px plain buttons — the most
>   robust mobile-SR pattern, and the guarantee that gestures are never the only path.
> - **Viewport meta fixed** (removed `user-scalable=no`, WCAG 1.4.4);
>   `touch-action: none` on the chart zone so gestures reach the handlers.
>
> Because the MAUI heads host these same Blazor components in a `BlazorWebView` (the
> SKCanvasView is `InputTransparent`, so touches land on the WebView), the iOS and
> Android APPS are expected to gain tap/drag/pinch and the slider through this same
> layer — **pending on-device verification with real VoiceOver and TalkBack**, which
> cannot be done from the Linux dev box. The native work below (adjustable
> UIAccessibilityElement, rotor custom actions, ExploreByTouchHelper) remains the
> second pass: it gives per-bar VoiceOver granularity, rotor actions, and
> explore-by-touch, and it should reuse the render-time hit-test index planned in
> Phase B's second pass.

**Current state (pre-2026-07-09, for the native layer still true).** Native input is **keyboard-only** everywhere: iOS/macCatalyst via
`KeyboardPageHandler.PressesBegan` (hardware *keys*), Android via
`MainActivity.DispatchKeyEvent`, browser via the JS key bridge. There is **no** touch or
gesture path — no `UIGestureRecognizer`, no `touchesBegan`, no `@ontouchstart`/`@onpointer*`
for chart nav. The chart is an opaque `SKCanvasView` with `InputTransparent="True"`, drawn
as raw pixels with **zero accessibility elements** (no `SemanticProperties`/
`UIAccessibilityElement`). Consequence: on a phone with no keyboard the chart is unreachable,
and **VoiceOver/TalkBack cannot touch-explore it** — there is nothing for the screen reader
to focus.

There are two distinct problems, and they need different solutions:

**(A) A touch input modality** — fingers driving the same navigation the keyboard drives
(swipe = bar, pinch = zoom…), for low-vision and no-keyboard use.

**(B) Native screen-reader reachability** — making the chart navigable by VoiceOver's and
TalkBack's *own* gesture model.

**Recommended design: keep the Hybrid Voice model; map gestures to existing
`SystemCommand`s; make the canvas one "adjustable" accessibility element.** Rationale: the
entire app is built on its own navigation + speech pipeline. Re-exposing every bar as a
native a11y element would fork the speech model in two and make sonification timing fragile.
Instead, present the chart to the screen reader as a *single focusable, adjustable* element
and let the app keep speaking:

- **iOS / macCatalyst.** Wrap the canvas region in a `UIAccessibilityElement` with the
  **adjustable** trait. VoiceOver swipe-up/down then calls `accessibilityIncrement` /
  `accessibilityDecrement` → map to next/previous bar (`SystemCommand`); the element's
  `accessibilityLabel`/`accessibilityValue` is the current bar summary the app already
  builds. Use `accessibilityCustomActions` for pane switching, playback, and the tool menu.
  For richer direct gestures (pinch-zoom, two-finger pan) add `UIPinch`/`UIPanGestureRecognizer`
  on a gesture-capture view, enabled when VoiceOver passes touches through (direct-interaction
  / the adjustable element focused). Drop `InputTransparent` on mobile (or add a transparent
  capture view above the canvas).
- **Android.** Mirror with an `AccessibilityDelegate` exposing a range/adjustable semantic
  (or `ExploreByTouchHelper` virtual nodes if per-bar focus is wanted later); TalkBack
  swipe maps to next/prev bar. Add `ScaleGestureDetector` + `GestureDetector` for
  pinch/swipe → `SystemCommand`.
- **Shared.** A new `IGestureService` (sibling to `IInputService`) normalizes gestures into
  `SystemCommand`s and goes through the existing `CommandDispatcher`, so all current speech +
  sonification is reused unchanged. Define and document a gesture set mirroring the keys:
  1-finger swipe L/R = prev/next bar; swipe up/down = component; two-finger swipe up/down =
  pane; pinch = zoom; two-finger double-tap = play/stop; long-press / rotor = tool & context
  menus.

The hard part is **coexistence with the screen reader** (preventing VoiceOver/TalkBack from
swallowing custom gestures); the adjustable-trait route is the idiomatic way through, because
sequential next/previous is exactly what that trait is for. Effort: large — new iOS and
Android handlers, the gesture→command map, and on-device tuning with VoiceOver and TalkBack.
This is the work the project has been calling "touch gestures still to do," and it is what
makes `iOS`/`Android` real rather than keyboard-tethered.

Until the native layer lands and is verified on devices, the manual states that touch on
the mobile apps is **expected but unverified**, and that a connected hardware keyboard
remains the fully-supported mobile input. The hosted/demo WEBSITE touch support shipped
2026-07-09 (see status box above).

---

## 5. Braille on the WebHost: is it possible?

**Short answer: not today, and not cleanly, because the device driver is Windows-native and
the WebHost serves a remote browser.** Two independent blockers:

1. **Driver.** Tactile output goes through `DotPadSDK-3.0.0.dll` via `WindowsDotPadNative`
   P/Invoke. The official **Linux** Dot Pad SDK is v1.0.0 — a 20-cell *text-strip* API with
   **no graphic display API at all** (verified against the vendor repos; see `PLATFORMS.md`
   §7). So even a *local* Linux WebHost cannot drive the Dot Pad's graphic cells until Dot
   Inc ships a Linux 3.0.0 SDK with graphic parity. This is an upstream blocker, not ours.
2. **Topology.** Blazor Server runs on the server; the USB display is on the *client*. A
   browser tab cannot open a USB-serial device. The only standard browser path to local USB
   is **WebHID / WebSerial**, which would require: a JS-side WebSerial bridge in the browser,
   re-implementing the Dot Pad framing protocol in JS (the rasteriser/bit-packer currently
   lives in C# `DotpadTactileDriver`), and shipping the cell buffers from server to browser
   each navigation event. Technically possible, sizable, and Chromium-only (Firefox does not
   implement WebSerial/WebHID — and Orca users skew Firefox).

**Conclusion.** Braille/tactile on the WebHost is **blocked upstream** (no Linux graphic SDK)
and **awkward by topology** (server/browser split needs a WebSerial bridge). The pragmatic
positioning stays as documented: **tactile is a Windows-MAUI feature**; Linux tactile tracks
the upstream SDK. If a local-Linux graphic SDK ever ships, the *local* WebHost could drive a
directly-attached display by reusing the C# driver in-process (no browser bridge needed,
because a locally-run WebHost is on the same machine as the USB port) — that is the realistic
first step, well before any WebSerial work.

---

## 6. Documentation status

**User manual (`docs/USER_MANUAL.md`, ~939 lines).** Substantially complete — Getting
Oriented, Loading a Market, Reading the Chart, Analysis Tools, AI/Narration/Journal, Trading
(rewritten for the full order panel), Automation, Customizing, Platform Support. Remaining:

- **Glossary** is an explicit stub ("planned for a future revision"). Write it: stop-loss,
  take-profit, trailing stop, support/resistance, overbought/oversold, Point of Control,
  value area, time-in-force, post-only, reduce-only, position side, OCO/bracket.
- **No tactile / Dot Pad chapter exists.** The manual never mentions the Windows tactile
  display. Add one once §3 (toggle + hot-plug) ships: enabling braille output, what the two
  panes + 20-cell strip show, F1–F4, panning, connect/disconnect announcements.
- **AI Analyst section is thin on *usage*** (see below).
- Keep the Trading chapter in sync as real-provider P&L/fills land.

**Quick start (`docs/QUICKSTART.md`).** Good for navigation/sonification/playback. Scripting
appears only as a one-line modal entry (Alt+,) — fine for a *quick* start, but a
one-paragraph "what the custom scripts panel is for, and that authoring is covered in the SDK
guide" pointer would help. AI Analyst already has a solid subsection.

**Is indicator scripting well documented?** Author-level: **yes** — `SDK_GUIDE.md` +
`PLUGIN_AUTHORING.md` + the `ICustomIndicator` tutorial. User-level: **adequately** — the
manual's "Custom scripts" subsection covers write/import/transpile, the trust prompt, the
sandbox, and the iOS limitation. Thin in the quick start (acceptable). Could deepen the
manual's coverage of the supported PineScript subset and the import-from-Discord trust story.

**Is the AI feature well documented? What "how to use it" needs to be in the manual.** It is
*present* in both docs but light on practical use. Add to the manual's AI Analyst section:

- **What it is and isn't** — a read-only second opinion that *describes* structure (trend,
  S/R, momentum, short-term outlook); not trading advice, not an order-placer.
- **How you actually use it** — navigate/set up the chart you care about first (it analyses
  the *current* symbol/timeframe/indicators), then Ctrl+Alt+Shift+A; read the spoken summary;
  re-run after changing timeframe or indicators to compare framings.
- **Provider trade-offs** — Claude/OpenAI are cloud (vision-capable, your chart image leaves
  your machine); **Ollama is local/offline/private** and the right pick when you don't want
  chart data leaving the device. Tried in order Claude → OpenAI → Ollama; first configured
  wins (Alt+K to add a key).
- **Caveats** — external request = data sharing (already noted); cloud calls cost money;
  output is generated and can be wrong — treat it as commentary, verify against what you
  navigated.

---

## 7. Open-work inventory

Loose ends across the codebase, grouped. Most live in `docs/TODO.md` already; consolidated
here so the strategic picture is in one place. Not prioritised beyond the grouping.

**Security / sandbox**
- ~~L5 Linux `bwrap` sandbox (§2)~~ — **done 2026-06-25** (`LinuxBwrapLauncher`).
- Sign `ScriptWorker.exe` + plugin DLLs; verify before launch (supply-chain).
- Coinbase + remaining providers credential-checkout migration (`CoinbaseProvider` still
  holds long-lived `_apiKey`/`_apiSecret`).
- CPU quota: sliding window instead of single-spike kill (`OutOfProcessScriptHost`).
- Sync-over-async cleanup (3 sites: `StrategyAutoLoader`, `OutOfProcessIndicator`,
  `LiveStreamManager`).

**Mobile / touch (gates an iOS/Android release)**
- Touch-gesture input + screen-reader reachability (§4).
- ~~MAUI native release builds (CI was failing repeatedly)~~ — **green in the v1.1.0
  run 2026-06-26** (Windows + macOS-universal artifacts built); still **unsigned** and
  not runtime-verified.

**Tactile (Dot Pad)**
- ~~Enable toggle + hot-plug detect + connect/disconnect announcements (§3)~~ —
  **done 2026-06-25**.
- On-device empirical checks still pending (PgDn/PgUp pairing, F4-resume, pan-key ownership,
  strip timeout feel, body+wick legibility); Dot Pad X untested; 3-pane mode deferred.

**WebHost**
- HiDPI chart density (server renders 1280×720, fuzzy on HiDPI — read `devicePixelRatio`).
- Verify drawing-tool mouse placement end-to-end in the browser.
- ~~L7 demo-deploy gate~~ — **done 2026-06-25/26** (`DemoPolicy` whitelist + `/app/`
  reverse-proxy); a Blazor Server **circuit rate-limiter** for the public site is still
  worth adding.
- Pending medium-value WebHost tests (path/XDG, logger dedup, startup smoke, diag endpoint).

**Trading**
- Real-provider realized-P&L / fills on close (paper-backed today).
- Spot reduce-only / position-side (futures-only today).
- True atomic OCO/bracket orders (entry + protective legs submit separately today).

**Plugins**
- Per-plugin dependency folders + load-context resolution (the shared-output flattening
  fix that bit Binance vs MEXC).

**Quality / build**
- WCAG colour-token sweep (~30 inline `#888`/`#aaa` literals on light modal backgrounds).
- RCL platform-code Roslyn analyzer; DI lifetime validator.
- God-modal split; `WorkspaceStore` immutable snapshots (post-1.0, large).

**Docs**
- Glossary; tactile chapter; richer AI usage section (§6).
- Customer-facing README rewrite; ship a sample plugin DLL; `Tests/README.md`.
