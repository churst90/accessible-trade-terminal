# Platform Agnosticism & Architecture

The Accessible Trading Terminal is cross-platform (Windows, Android, macOS, iOS) by strictly separating business logic from platform-specific hardware drivers via **.NET 10 MAUI Blazor Hybrid**.

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
| **Script Sandbox** | `IScriptWorkerLauncher` | `WindowsAppContainerLauncher` (AppContainer + STARTUPINFOEX) | `AndroidIsolatedProcessLauncher` (`[Service(IsolatedProcess=true)]` bound service) | `MacSandboxExecLauncher` (`sandbox-exec` deny-default profile) | deferred (desktop-only sandbox today) |

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

| Feature | Windows | Android | macOS | iOS |
| :--- | :---: | :---: | :---: | :---: |
| **Chart Rendering** | ✅ | ✅ | ✅ | ✅ |
| **Chart Sonification** | ✅ | ✅ | ✅ | ✅ |
| **Speech / Screen Reader** | ✅ | ✅ | ✅ | ✅ |
| **Market Data** | ✅ | ✅ | ✅ | ✅ |
| **Keyboard Navigation** | ✅ | ✅ | ✅ | ✅ |
| **Audio Output** | ✅ (WASAPI) | ✅ (`AudioTrack`) | ✅ (`AVAudioEngine`) | ✅ (`AVAudioEngine`) |
| **Script Sandbox (OS-enforced)** | ✅ (AppContainer) | ✅ (isolatedProcess) | ✅ (sandbox-exec) | ⏸ (deferred) |
| **Secure Storage** | ✅ (DPAPI) | ✅ (KeyStore) | ✅ (Keychain) | ✅ (Keychain) |
| **Tactile Display** | ✅ (Dot Pad 2nd-gen — see §7) | ❌ | ❌ | ❌ |

*(✅ = Fully Supported, 🏗️ = In Development / Stubbed, ⏸ = Intentionally Deferred, ❌ = Not Yet Implemented)*

## 7. Tactile Display Support

One refreshable tactile graphics display is officially supported:

| Device | Status | Notes |
| :--- | :---: | :--- |
| **Dot Pad 2nd-gen** (30 × 10 graphic cells + 20-cell strip) | ✅ Tested | Primary target. Connection via USB-Serial; SDK uses `DOT_PAD_CONNECT_SERIAL`. |
| **Dot Pad X** (same SDK family) | ⚠️ Untested but expected to work | Uses the same DotPadSDK-3.0.0 native ABI, so the driver should bind without code changes. Verified driver code-path, but no on-device confirmation yet. |
| APH Monarch | ❌ Not implemented | Requires a different SDK (Dot Inc proprietary, vendor-restricted). |

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

### SDK installation (required for Dot Pad support)

The Dot Inc SDK is **not committed to the repo** (~850MB, vendor-licensed
binaries). To enable Dot Pad support locally:

1. Download the sample-code bundle from
   [https://github.com/dotincorp/dotpad-sample-code](https://github.com/dotincorp/dotpad-sample-code)
   (or the Dot Inc developer portal).
2. Place the `Windows/3.0.0/` directory at
   `dotpad-sdk/Windows/dotpad-3.0.0/` relative to the repo root.
   The build expects to find `DotPadSDK-3.0.0.dll`, `TTBEngine.dll`,
   `Mecab.dll`, `jsoncpp.dll`, `liblouis.dll`, `mecabrc`, and the
   `tables/` + `ipadic/` extracted directories there.
3. Build normally. The `CopyDotPadSdkWindows` MSBuild target stages
   everything alongside the app binary.

The build also runs WITHOUT the SDK present — `WarnIfDotPadSdkMissing`
emits a one-line MSBuild message and Dot Pad support is simply
unavailable at runtime (`DotpadTactileDriver` reports not-connected via
the `NullDotPadNative` fallback).
