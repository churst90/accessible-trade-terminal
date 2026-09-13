# Diagrams

Fourteen Mermaid sources describing how the terminal actually fits together. All fourteen
were re-verified against the tree on **2026-09-13** and all fourteen render clean.

The first four are written at the altitude of the user manual — they describe what a trader
hears and controls, using the real type names so a developer can find the code. The other ten
are the system.

Each entry below carries a prose summary as well as the link. That is deliberate: a
diagram is the least accessible artifact in this repo, and the summary is what a screen
reader user gets to read. If you change a diagram, change its summary in the same commit.

## Rendering them

```bash
npx --yes @mermaid-js/mermaid-cli@11 -i Diagrams/architecture_overview.mmd -o /tmp/arch.svg
```

Mermaid also renders inline in GitHub Markdown, so any of these can be pasted into a
` ```mermaid ` fence in a doc or an issue. Rendering every file is the cheapest way to
check a change parses — a `;` inside a sequence-diagram message will fail the parse, and
that is exactly how the last syntax error in this directory was found.

## The terminal as the user meets it

**[`chart_layout.mmd`](chart_layout.mmd)** — the structure the arrow keys walk. The X axis is
time and every pane shares it; a pane is a Y axis and nothing else (`ChartPaneModel`), which is
why candles and an EMA share the Main pane, volume has its own, and since 2026-09-11 every
oscillator has its own (`PaneAssignmentService.OwnPaneKey`). Inside a pane: series, then
components (a value on every bar), and the two things that are not components — a level (a
constant with a value, a role and an owner, `LevelConfig`) and a zone (a region: a visual band,
a zone line that speaks support and resistance, or a Value Deviation zone). Shows where the Y
axis range comes from (declared `RangeMin`/`RangeMax` for 17 indicators, auto-fit for the rest),
how that range becomes pitch, the five keys that expose the structure by ear, and the rule that
indicators always compute on raw candles whatever Heikin-Ashi is doing to the picture.

**[`announcement_channels.mmd`](announcement_channels.mmd)** — where a sentence goes. One
question decides it (`NotificationPolicy.IsOnScreen`): is the event about the chart in front of
the trader? If so it is spoken through the four `SpeechChannel` tiers and never also notified.
If not — another tab, no tab, no browser, or a MAUI window hidden to the tray — it becomes a
system notification, behind one switch that is on by default, with the timeframe floor gating
bar closes and the ladder only, and the tray's 30-minute silence gating everything except money.
Errors and order outcomes ignore every switch on the page.

**[`background_monitor.mmd`](background_monitor.mmd)** — what happens after the last tab
closes, on the local web host. `BrowserPresence` sees the connection count reach zero,
`BrowserFarewellService` sends one notification saying whether anything will be watched,
then `LocalBackgroundMonitor` polls once a minute: a real `HeadlessChart` per saved tab (at most
eight) recomputes its indicators, evaluates every alert with an explicit symbol, and runs the
same narration ladder the browser runs; `HeadlessOrderWatch` reports fills on every venue with
open work. It reports and never acts; strategies are not evaluated headless; the MAUI heads have
no headless monitor at all. Also shows the tray menu and the fact that a reconnect brings no
catch-up beyond the Recent alerts page.

**[`trade_lifecycle.mmd`](trade_lifecycle.mmd)** — a trade the way a trader experiences it,
the companion to `order_lifecycle.mmd` below. The three starting points, sizing from risk and
the stop before anything else, the paper/live fork, the live order review that re-arms if the
ticket changes, the credential checkout that refuses a paper key on a live venue, how the
protective stop travels with the entry on each broker, and every sentence you hear from
"Order placed" to "Stop loss hit" — in the browser or as a notification with it closed.

## The system

**[`architecture_overview.mmd`](architecture_overview.mmd)** — the whole application on one
page. Three heads (MAUI Blazor Hybrid, the ASP.NET Core WebHost, the headless StrategyLab)
over one shared Razor component library and one platform-agnostic Core. Shows where
`DemoPolicy` sits between a head and the features it may use, the input pipeline from key
capture through `ModalStack` to `CommandDispatcher`, the chart model (`PaneAssignmentService`,
`ChartPaneModel`), the orchestration layer, the accessibility and audio clusters, the
background monitor and the notification tier that decides spoken-versus-notified, the 16
trading and 17 analytics plugins behind the SDK contract, out-of-process script isolation,
and the four output surfaces (screen, speech, tactile pins, audio). Read it first.

**[`hosting_topology.mmd`](hosting_topology.mmd)** — which head can do what, and why. One
VPS runs two systemd services behind one nginx: the demo on `/app/` in `HostMode.Demo`, the
signed-in terminal on `/terminal/` in `HostMode.Hosted`. Shows the exact `DemoPolicy` tiers
(Full-only — which since 2026-09-11 includes browser-closed alert monitoring — not-demo,
always-on, data-restricted), where hosted state lives and why the XDG variables are
load-bearing, the standing constraint that no hosted head may hold real broker credentials or
place a live order, and the `-p:ServerPublish=true` trap that returns HTTP 200
on a build that never boots a Blazor circuit. This is the only diagram covering the
deployment, and until it existed the deploy notes in `patches/` were the sole record.

## The paths through it

**[`data_lifecycle.mmd`](data_lifecycle.mmd)** — a provider call becoming a spoken bar.
Historical fetch through the cache and the resampler, the live websocket loop, the indicator
recompute, and the watchdog path when ticks stop. Note the `DataState` machine has **six**
states: `Stalled` sits between `LiveStreaming` and `Faulted` and is what the watchdog trips
into before a reconnect and a mid-bar backfill.

**[`navigation_flow.mmd`](navigation_flow.mmd)** — one keypress, end to end. Platform capture
to `BlazorInputService` to `ShortcutManager` to `CommandDispatcher`, then out to speech, audio
and pins in parallel. Includes the pane branch (`Alt+PageUp`/`Alt+PageDown`, the next Y
axis), the Escape branch that asks `ModalStack` which dialog is on top, and the WebHost startup
remap — which rewrites only a saved pre-2026-09-05 `Ctrl+Shift+letter` profile and removes the
browser-reserved single-Ctrl chords, in memory only, which is why `F1` always shows the correct
chord for the host you are on.

**[`order_lifecycle.mmd`](order_lifecycle.mmd)** — an intent becoming a spoken fill. The three
entry points (dashboard, quick trade from the chart, strategy engine), the live order review
that sits between the dashboard and the service on a live profile, the guards inside
`GeneralOrderService` in the order they run — including the credential checkout that refuses a
paper key on a live-only venue — the paper/live routing split, the provider conformance suite
every plugin is held to, the typed `OrderPlacement` result, and the second path a fill takes
with the browser closed. The reason the result is one type: a refusal, an *uncertainty* and a
fill are three different things a trader must tell apart by ear, and three separate classifiers
used to disagree about which was which. `ProtectiveLevelValidator` is drawn where it actually
runs — as the level is typed, in the dashboard and the paper broker — not in the service chain.

**[`feedback_routing.mmd`](feedback_routing.mmd)** — everything that wants to speak, and what
can silence it. Above the channels sits `NotificationPolicy`, which decides WHERE a sentence
goes — spoken, because it is about the chart in front of you, or a system notification —
before any channel is consulted. The four `SpeechChannel` tiers are a contract, not a
preference: `Manual` is muted by F2, `Event` by Shift+F2, `OrderEvent` breaks through both by
default, and `Critical` is never muted because "speech off" has to be heard. The narration
ladder (`NarrationScanner` composing one `ScanUtterance` per bar close) is drawn as a source. Also shows the one-owner formatters, the
per-host speech backends, the audio voice-slot map and the limiter, and the journal that makes
transient speech re-readable. Read this before touching anything that speaks.

## The subsystems

**[`indicator_adapter.mmd`](indicator_adapter.mmd)** — the `IIndicatorProvider` surface and the
36 implementations in Core, grouped by what they adapt: Skender wrappers, the in-house Cipher
family, structure and levels, regime and cycles, the six that read from analytics plugins rather
than from price, and the composites. Drop-in DLLs under `Plugins/Indicators/` implement the same
interface and load the same way. `GetStabilityWindow` is the causality contract — how many
leading bars are warmup and must never be announced as signal.

**[`plugin_trust.mmd`](plugin_trust.mmd)** — how a plugin DLL gets from a build to
`LoadFromAssemblyPath`. The MSBuild target hashes each DLL into `plugins_trusted.manifest`, CI
publishes it, and `PluginTrustPolicy` checks it at load. The failure mode this exists to make
loud: if the manifest and the DLLs disagree, *every* plugin is refused and every provider reports
"no data", which reads to a user as a data outage rather than a security refusal. Also records
that the two hosted heads load plugins at different times, so zero trust lines on the demo just
after a restart is normal.

**[`script_sandbox.mmd`](script_sandbox.mmd)** — user scripts are compiled in-process and
executed out of it. `DemoPolicy` refuses to compile at all on any host that is not Full; past
that, a semantic walker and a *declared* reference set (no `Microsoft.CSharp`, so the `dynamic`
escape cannot reach the compiler), then a per-OS sandbox primitive. If the primitive is missing
the launcher refuses rather than silently falling back, and the one environment variable that
overrides that records a security event.

**[`tactile_paging.mmd`](tactile_paging.mmd)** — the chart as pins. The virtual canvas, the
device-sized window, the two-pane 50/50 layout, and the `IDotPadNative` seam that keeps the
paging logic testable off-Windows. Dot Pad 2nd generation and Dot Pad X share the SDK; the
driver is Windows-only and the SDK is not committed to this repo.
