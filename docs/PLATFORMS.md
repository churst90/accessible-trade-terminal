# Platform Agnosticism & Architecture

The Accessible Trading Terminal separates business logic from platform-specific hardware drivers, and ships through two hosts over one component library: **.NET 10 MAUI Blazor Hybrid** on Windows, Android, macOS and iOS, and **ASP.NET Core Blazor Server** (`AccessibleTrader.WebHost`) on Linux and anything browser-reachable. The tables in section 1 describe the MAUI drivers; the WebHost's equivalents are in [§6.1](#61-the-linux-webhost-path), which is also the path the public deployment runs and the one most users install.

## 1. Architectural Layers

### Core Logic (Agnostic)

Located in `AccessibleTrader.Core`. Contains the math, technical indicators, and Orchestrators (`DataOrchestrator`, `IndicatorOrchestrator`, `MarketOrchestrator`, `DataOrchestrationService`). Fully platform-agnostic.

- **Audio Engine:** Pure C# DSP for waveform generation and mixing. Generates raw `float[]` buffers consumed by platform drivers.
- **Shortcut Manager:** Data-driven logic within `ShortcutManager` for resolving physical keys to semantic `SystemCommand` values.
- **Accessibility Engines:** Logic for navigation, viewport calculations, and feedback string construction.

### Platform Drivers & UI (Implementation)

Located in `AccessibleTrader.BlazorClient/Services` and `AccessibleTrader.BlazorClient/Platforms/*`. These implement agnostic interfaces to bridge Core to the OS.

| System | Interface | Windows | Android | macOS | iOS |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Audio** | `IAudioDriver` | `BlazorAudioDriver` (WASAPI via NAudio) | `BlazorAudioDriver` (`AudioTrack` PCM-Float push loop) | `BlazorAudioDriver` (`AVAudioEngine` + `AVAudioSourceNode`) | `BlazorAudioDriver` (`AVAudioEngine` + `AVAudioSourceNode`) |
| **Speech** | `ISpeechManager` | `BlazorSpeechManager` (ARIA live regions) | `BlazorSpeechManager` (ARIA live regions) | `BlazorSpeechManager` (ARIA live regions) | `BlazorSpeechManager` (ARIA live regions) |
| **Input** | `IInputService` | `GlobalInputService` (JS interop) | `MainActivity.DispatchKeyEvent` | `KeyboardPageHandler` (`PressesBegan`) | `KeyboardPageHandler` (`PressesBegan`) |
| **Secure Storage** | `ISecureStorageService` / `IPluginSecureStorage` | `MauiSecureStorageService` (DPAPI) | `MauiSecureStorageService` (KeyStore) | `MauiSecureStorageService` (Keychain) | `MauiSecureStorageService` (Keychain) |
| **Paths** | `IPlatformPathService` | `MauiPathService` | `MauiPathService` | `MauiPathService` | `MauiPathService` |
| **Script Sandbox** | `IScriptWorkerLauncher` | `WindowsAppContainerLauncher` (AppContainer + STARTUPINFOEX) | `AndroidIsolatedProcessLauncher` (`[Service(IsolatedProcess=true)]` bound service) | `MacSandboxExecLauncher` (`sandbox-exec` deny-default profile) | **refused** — `ScriptingNotSupportedOnPlatformException` at compile time |

> iOS and Mac Catalyst do not merely lack a launcher; they refuse to compile a script at all,
> loudly, rather than falling back to running it in-process. Mac Catalyst joined iOS there because
> its self-contained build cannot reference the `net10.0` ScriptWorker. On Linux the launcher is
> `LinuxBwrapLauncher` (bubblewrap) — see §5 and §6.1.

## 2. Rendering Architecture

The UI is built using **Blazor running inside a MAUI WebView**.

- **Charts:** Rendered via **SkiaSharp on a native MAUI `SKCanvasView`** at Grid layer 0 in `MainPage.xaml`. The `BlazorWebView` overlays at layer 1 (transparent). `SkiaSharp.Views.Blazor` is NOT used.
- **UI Chrome:** Blazor Razor components — toolbar, modals, status indicators — rendered in the transparent Blazor WebView overlay.
- **Theming:** CSS-based (Dark Mode priority). CSS variables control colors; accessible high-contrast ratios enforced.

## 3. Audio Architecture

The `AudioEngine` generates raw floating-point audio buffers.

- The engine is fully decoupled from any audio library — platform drivers handle the "final mile" of pushing buffers to hardware output.
- **Windows (WASAPI):** `BlazorAudioDriver` pushes buffers to the WASAPI output device via NAudio.Wasapi's `WasapiOut`.
- **Android (`AudioTrack`):** `AudioTrack.Builder` configured with `ChannelOut.Stereo`, `Encoding.PcmFloat`, `AudioTrackMode.Stream`; dedicated background thread writes buffers via `_audioTrack.Write(...)`.
- **iOS / macCatalyst (`AVAudioEngine`):** `AVAudioEngine` + `AVAudioSourceNode` render callback. The source node pull function copies from the engine's float buffer into the `AudioBufferList` the node hands us.

## 4. Keyboard Input Architecture

Input is normalized via the `IInputService` abstraction on every platform.

- **Windows:** MAUI captures raw hardware events and forwards them through the Blazor context via `[JSInvokable]` methods in `GlobalInputService`.
- **Android:** `MainActivity.DispatchKeyEvent` intercepts hardware-keyboard events (incl. Bluetooth keyboards) and routes them to `IInputService`.
- **macOS / iOS:** `KeyboardPageHandler` is a custom `PageHandler` registered in `MauiProgram.cs` (`handlers.AddHandler<ContentPage, KeyboardPageHandler>()`). A `UIViewController` subclass overrides `PressesBegan(NSSet<UIPress>, UIPressesEvent)` and forwards `UIKey` presses to `IInputService`.
- `ShortcutManager` resolves key combos against a `ShortcutProfile` to identify `SystemCommand` values.
- `CommandDispatcher` routes `SystemCommand` values to `NavigationEngine`, `WorkspaceStore`, or EventBus.

## 5. Script Sandboxing Architecture

User-compiled Roslyn indicators and strategies run in an **out-of-process worker** with OS-enforced isolation on every supported desktop/mobile platform. See [SANDBOX_DESIGN.md](SANDBOX_DESIGN.md) for the full IPC contract, resource quotas, and threat model.

- **Host side:** `OutOfProcessScriptHost` supervisor — 5 s Calculate timeout, 10 s LoadAssembly timeout, 256 MB `WorkingSet64` quota polled every 2 s, kill-on-overage via `Process.Kill(entireProcessTree: true)`, graceful Shutdown with 1 s grace on disposal.
- **Worker side:** `AccessibleTrader.ScriptWorker` (stdio entry-point) reuses the shared `WorkerDispatcher` from `AccessibleTrader.ScriptSandbox`.
- **Per-OS launcher:** automatic selection via `RoslynScriptingService.CreateDefaultLauncher()`. On dev-box ACL gaps Windows falls back to `DefaultProcessLauncher` (still a separate process, just without AppContainer).

## 6. Platform Compatibility Matrix

| Feature | Windows | Android | macOS | iOS | Linux (WebHost) |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Host shell** | MAUI Blazor Hybrid | MAUI Blazor Hybrid | MAUI Blazor Hybrid | MAUI Blazor Hybrid | ASP.NET Core Blazor Server (browser) |
| **Chart Rendering** | ✅ Native `SKCanvasView` overlay | ✅ Native `SKCanvasView` overlay | ✅ Native `SKCanvasView` overlay | ✅ Native `SKCanvasView` overlay | ✅ Server-side Skia → base64 PNG → `<img>` (~10 fps throttled) |
| **Chart Sonification** | ✅ | ✅ | ✅ | ✅ | ✅ Same `AudioEngine` → pw-cat / pacat / aplay |
| **Speech / Screen Reader** | ✅ NVDA/Narrator + ARIA | ✅ TalkBack via ARIA | ✅ VoiceOver via ARIA | ✅ VoiceOver via ARIA | ✅ Orca via D-Bus `PresentMessage` → respects voxin / SpeechDispatcher voice |
| **Market Data** | ✅ | ✅ | ✅ | ✅ | ✅ (same plugins) |
| **Keyboard Navigation** | ✅ | ✅ | ✅ | ✅ | ✅ Same JS bridge; `Ctrl+Shift+letter` drawing chords remapped to `Alt+Shift+letter` (Firefox reserves the originals) |
| **Mouse / Drawing-tool placement** | ✅ Native pointer events | n/a | ✅ Native pointer events | n/a | ✅ JS `registerMouseHandler` on `chart-interact-zone` → `(x, y, w, h)` → anchor placement (L4-B pinned by tests) |
| **Audio Output** | ✅ (WASAPI) | ✅ (`AudioTrack`) | ✅ (`AVAudioEngine`) | ✅ (`AVAudioEngine`) | ✅ pw-cat (PipeWire) → pacat (PulseAudio) → aplay (ALSA), picked by file-existence probe at startup |
| **Script Sandbox (OS-enforced)** | ✅ (AppContainer) | ✅ (isolatedProcess) | ✅ (sandbox-exec) | ⏸ (deferred) | ✅ (`bwrap` — L5; falls back to process-only if `bubblewrap` not installed) |
| **Secure Storage** | ✅ (DPAPI) | ✅ (KeyStore) | ✅ (Keychain) | ✅ (Keychain) | ✅ (ASP.NET Core DataProtection, encrypted-at-rest in `{XDG_DATA_HOME}/AccessibleTrader/secrets/`) |
| **System-tray applet / background alerts** | ✅ MAUI close-to-tray (default on, pending Windows verify); WebHost `Shell_NotifyIcon` | ❌ n/a | ⚠️ WebHost menu actions only — native icon is a future MAUI Mac feature | ❌ n/a | ✅ WebHost StatusNotifier / DBusMenu (Orca-navigable, verified); local Full mode only |
| **Tactile Display** | ✅ (all Dot Pad models — X + 2nd-gen — see §7) | ❌ | ❌ | ❌ | ❌ (vendor Linux SDK is text-only / 20-cell — see §7) |

*(✅ = Fully Supported, 🏗️ = In Development / Stubbed, ⏸ = Intentionally Deferred, ❌ = Not Yet Implemented)*

### 6.1 The Linux WebHost path

`AccessibleTrader.WebHost` is an ASP.NET Core Blazor Server project (net10.0)
that serves the existing `AccessibleTrader.BlazorClient.Components` RCL
without going through MAUI. MAUI has no Linux head, so Linux is the
WebHost's primary target; the same project is also the deploy target for
the public-website chart demo (any OS that runs a modern browser can be
the client).

Two RCL changes were required, both gated on
`IRuntimePlatform.IsBrowserHost`:

- `IRuntimePlatform` gained a default-implementation `bool IsBrowserHost
  => false;`. MAUI's `MauiRuntimePlatform` inherits the default and gets
  `false` automatically — no source edit needed in the MAUI head.
  `WebHostRuntimePlatform` overrides to `true`.
- `ChartArea.razor` renders an inline `<img>` chart surface only when
  `IsBrowserHost` is true; the MAUI path keeps using its native
  `SKCanvasView` overlay declared in `MainPage.xaml`.

Behaviour under MAUI is bit-for-bit unchanged. The WebHost runs locally
(`dotnet run --project AccessibleTrader.WebHost`, opens
`http://localhost:5145` in the user's default browser via `xdg-open` /
`open` / `start`) and never requires a MAUI workload to be installed.

## 7. Tactile Display Support

**All Dot Pad models are supported** — the Dot Pad X (newest) and the second generation —
because they share the same `DotPadSDK-3.0.0` native graphics ABI, so one driver binds
across the family:

| Device | Status | Notes |
| :--- | :---: | :--- |
| **Dot Pad X** (30 × 10 graphic cells + 20-cell strip, newest model) | ✅ Supported | Same DotPadSDK-3.0.0 ABI as the 2nd-gen; binds without code changes. Connection via USB-Serial. On-device confirmation pending (see 2nd-gen). |
| **Dot Pad 2nd-gen** (30 × 10 graphic cells + 20-cell strip) | ✅ Tested on-device | Connection via USB-Serial; SDK uses `DOT_PAD_CONNECT_SERIAL`. |
| APH Monarch | ❌ Not implemented | Requires a different SDK (Dot Inc proprietary, vendor-restricted). |
| **Linux (any device)** | ❌ Vendor SDK gap | Verified 2026-05-16 against `dotincorp/dotpad-sdk-guide` and `dotincorp/dotpad-sample-code`: the official Linux SDK is **v1.0.0**, ships only a text-strip API (`displayTextData` / `setBrailleLanguage`), supports 20-cell devices only, and exposes **no graphic display API at all**. Sample app confirms `/dev/ttyUSB0` text-only. Per the all-or-nothing tactile rule, Linux uses `NullDotPadNative` until Dot Inc publishes a Linux 3.0.0 SDK with graphic parity. Track upstream at the dotincorp repos. |

### Wiring summary

- Driver: `AccessibleTrader.Core/Services/Accessibility/Dotpad/DotpadTactileDriver.cs`
  — Windows-only via `NativeLibrary.TryLoad` against `DotPadSDK-3.0.0.dll`.
  Falls back to `NullDotPadNative` on Android / iOS / macCatalyst so the
  rest of the app still builds and runs.
- Coordinator: `AccessibleTrader.Core/Services/Accessibility/TactileCanvasCoordinator.cs`
  — composes a two-pane 50/50 graphic + 20-cell strip from
  `WorkspaceState` on every navigation event, and routes the device's
  F1-F4 + Pan keys through the existing speech / command pipeline.
- Calibrator: `tools/DotPadCalibrator/Program.cs` — standalone CLI for
  bit-order, cell-index, and stripe probing on a physical device.
- Diagnostics: `dotpad-diagnose.bat` + `dotpad-diagnose.ps1` at the repo
  root — captures COM-port + USB inventory + Dot Pad device info to a
  log file. Useful when the driver can't find the device on first plug-in.

### SDK installation

**If you have a released zip, there is nothing to do.** From the 2.12.0 re-cut the SDK travels
in the Windows download, fetched by `release.yml` from Dot Inc's own public repository and
pinned by SHA-256. This section is about building from source.

The Dot Inc SDK is **not committed to the repo** (~296MB for Windows 3.0.0 alone,
vendor-licensed binaries). To enable Dot Pad support in a source build:

1. Take `Windows/3.0.0/` from
   [https://github.com/dotincorp/dotpad-sdk-guide](https://github.com/dotincorp/dotpad-sdk-guide)
   (or the Dot Inc developer portal). **Note the path: `Windows/3.0.0`, not
   `Windows/dotpad-3.0.0`** — the repo's directory is named for the version, ours is named for
   the product, and a URL built from the local name 404s.
2. Place it at `dotpad-sdk/Windows/dotpad-3.0.0/` relative to the repo root.
   The build expects `DotPadSDK-3.0.0.dll`, `TTBEngine.dll`, `Mecab.dll`, `jsoncpp.dll`,
   `liblouis.dll`, `mecabrc` and the extracted `tables/` directory there.
3. Build normally. `CopyDotPadSdkWindows` stages everything alongside the app binary at build
   time; the `<None>` items with `CopyToPublishDirectory` are what carry it into a publish.

**What ships, and what does not.** Eighteen megabytes of the SDK ship; `ipadic/` — 187MB of
MeCab's *Japanese* dictionary — does not. Three pieces of evidence, all from the binaries:

- `WindowsDotPadNative` binds exports on `DotPadSDK-3.0.0.dll` only, and never touches TTBEngine
  or MeCab.
- `DotPadSDK-3.0.0.dll` has **no static dependency** on TTBEngine, MeCab, liblouis or jsoncpp —
  its PE import table names only the VC++ runtime. It `LoadLibrary`s the rest, and says so:
  `[OK] liblouis.dll loaded from:` / `DOT_ERROR_LIBLOUIS_DLL_COULD_NOT_LOAD`.
- MeCab is reached only from `TTBEngine`'s `mecab_new2("-u ./ipadic/user.dic")`, and **there is
  no `user.dic` anywhere in the 187MB `ipadic/` directory** — the vendor does not ship one. Both
  that path and `mecabrc`'s `dicdir = ./ipadic` are relative to the *working directory*, not the
  install, so they would miss regardless.

If a real Dot Pad ever fails to initialise with everything else present, `ipadic/` is the first
thing to add back. The 14MB of LibLouis `tables/` **do** ship in full: the SDK points
`lou_setDataPath` at a `\tables` directory beside itself, names 35 specific `.ctb`/`.utb` files,
and the tables include each other transitively, so trimming them breaks one braille code
silently for 13MB.

**The VC++ runtime ships with the SDK.** `DotPadSDK-3.0.0.dll` statically imports `MSVCP140`,
`VCRUNTIME140` and `VCRUNTIME140_1` — a .NET publish carries none of them. Without them the SDK
does not load on a machine that has no Visual Studio and no redistributable, and the Braille tab
looks exactly as healthy as it does when everything works. `release.yml` stages them from the
runner into `vendor/vcruntime/`; see that directory's README for a hand-built release.

An ordinary source build still runs WITHOUT the SDK present — `WarnIfDotPadSdkMissing` emits a
one-line MSBuild message and Dot Pad support is unavailable at runtime (`DotpadTactileDriver`
reports not-connected via the `NullDotPadNative` fallback). **A RELEASE build does not.** Under
`-p:ReleasePublish=true` the missing SDK is an error, because v2.12.0 shipped once with the
message printed in a green log that nobody read, telling users the SDK was in the download.

### NVDA Controller Client (required for speech on the Windows desktop head)

**This one is not optional in the way the Dot Pad SDK is.** Without it the desktop head is
effectively mute while the chart has focus, which is most of the time.

`BlazorSpeechManager` P/Invokes `nvdaControllerClient64.dll` to speak directly to NVDA. That path
matters here in a way it does not on the WebHost, because of how this head is assembled: the chart
is a native SkiaSharp canvas sitting **on top of** the `BlazorWebView`. When the user focuses the
chart, the screen reader is following focus onto a native control and is not reading the web
view's DOM at all — so the ARIA live region inside it, which is the fallback everywhere else,
announces to nobody. On the WebHost the entire surface is the DOM and the fallback works, which is
why the same build served over the web reads correctly through NVDA while the desktop client says
nothing.

Until 2026-09-21 **nothing in the build staged this file**: it was untracked by git, named by no
project file, and absent from every publish. A single copy had been dropped by hand into a
`bin/Debug` folder on 2026-03-03, so exactly one machine could speak and every fresh build and
every published install was silent. Found the first time the head was put in front of a screen
reader.

**And the first fix for that did not reach the download either.** The csproj items were correct,
but the DLL is gitignored and the items are `Exists()`-guarded, so the GitHub Actions runner —
which had no copy — built a perfectly valid mute release and said nothing. v2.12.0 was published
telling users the DLL was "in the download now" when it was in none of the six assets. From the
2.12.0 re-cut, `release.yml` fetches
`nvda_<version>_controllerClient.zip` from NV Access, pinned by SHA-256, a release build with no
controller client is a hard **error**, and `scripts/verify_release_payloads.py` refuses to
publish a zip that does not contain it. *A release that ships mute is worse than a release that
fails.*

### JAWS

Nothing to install and nothing to configure. JAWS registers a COM automation object
(`FreedomSci.JawsApi`) when it installs, and the terminal speaks through it directly — the same
route it uses for NVDA, and for the same reason: the chart is a native canvas, so a reader
following focus onto it is not reading the web view's DOM and the live region cannot reach it.

No vendored binary ships for this and no build step stages one. On a machine without JAWS the
object simply does not resolve and the terminal uses whatever else is available.

If both JAWS and NVDA are running, NVDA carries the speech. They are not normally both up; the
order is a tie-break so that two readers cannot talk over each other.

#### If you have a released zip (no repo)

**From the 2.12.0 re-cut you do not need to do any of this** — the controller client is in the
download. Everything below applies to 2.12.0-as-first-published and to every release before it,
where the DLL has to be placed by hand.

The DLL is found by the **default Windows DLL search order**, which looks in the application's
own folder first, so no rebuild is involved.

1. Download `nvda_<version>_controllerClient.zip` from
   [https://github.com/nvaccess/nvda/releases](https://github.com/nvaccess/nvda/releases)
   (it is listed among the assets on any release).
2. Inside it, open the **`x64`** folder and take `nvdaControllerClient.dll`.
3. Drop that file into the folder you unzipped the terminal into — the one containing
   **`AccessibleTrader.BlazorClient.exe`**. Top level, beside the exe, not in a subfolder.
4. Restart the terminal.

> **If you are on 2.11.0 or earlier, rename it to `nvdaControllerClient64.dll`.** NV Access
> renamed this file at some point; current downloads ship `nvdaControllerClient.dll` and older
> ones shipped `nvdaControllerClient64.dll`. Builds up to and including 2.11.0 ask for the old
> name only, so the current download sits beside the executable being ignored — the file is
> there and nothing is looking for that name. From 2.12.0 either name works, because the load
> is resolved against both.

There is no `vendor/` folder in the zip and there should not be: `vendor/nvda/` is a *source-tree*
staging location used at build time, not part of the shipped layout.

#### If you are building from source

1. Download the same zip as above.
2. Copy the **x64** `nvdaControllerClient.dll` into `vendor/nvda/` relative to the repo root.
   (`nvdaControllerClient64.dll` is accepted there too, for anyone holding the older download.)
   It is gitignored — a third-party binary (NV Access, LGPL 2.1), vendored rather than committed
   for the same reason the Dot Pad SDK is.
3. Build normally. The DLL is declared as a content item with `CopyToPublishDirectory`, so it
   reaches both `dotnet build` output **and** `dotnet publish` output — which is what puts it in a
   release zip. *(The first cut of this staged it with a `Copy` into `$(OutDir)`, mirroring
   `CopyDotPadSdkWindows`. That reaches a build and not a publish, so it would have worked on a
   developer machine and shipped nothing.)*

A build without it still succeeds, and `WarnIfNvdaControllerMissing` prints a high-importance
MSBuild message saying the result will be silent. At runtime the condition is reported too rather
than being left as an absence of sound: `BlazorSpeechManager.OutputStatus` answers
`NvdaDirect` / `LiveRegion` / `Mute`, a `Mute` terminal writes an error into the journal naming
this file, and every sentence it could not say is still journalled so nothing is lost.
