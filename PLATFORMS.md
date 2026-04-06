# Platform Agnosticism & Architecture

The Accessible Trading Terminal is designed to be cross-platform (Windows, Android, macOS, iOS) by strictly separating business logic from platform-specific hardware drivers via **.NET 10 MAUI Blazor Hybrid**.

## 1. Architectural Layers

### Core Logic (Agnostic)

Located in `AccessibleTrader.Core`. Contains the math, technical indicators, and Orchestrators (`DataOrchestrator`, `IndicatorOrchestrator`, `MarketOrchestrator`, `DataOrchestrationService`). Fully platform-agnostic.

- **Audio Engine:** Pure C# DSP for waveform generation and mixing. Generates raw `float[]` buffers consumed by platform drivers.
- **Shortcut Manager:** Data-driven logic within `ShortcutManager` for resolving physical keys to semantic `SystemCommand` values.
- **Accessibility Engines:** Logic for navigation, viewport calculations, and feedback string construction.

### Platform Drivers & UI (Implementation)

Located in `AccessibleTrader.BlazorClient/Services`. These implement agnostic interfaces to bridge Core to the OS.

| System | Interface | Windows | Android | macOS | iOS |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Audio** | `IAudioDriver` | `BlazorAudioDriver` (WASAPI output) | `BlazorAudioDriver` (STUB — AudioTrack TODO) | `BlazorAudioDriver` (STUB — AVFoundation TODO) | `BlazorAudioDriver` (STUB) |
| **Speech** | `ISpeechManager` | `BlazorSpeechManager` (ARIA live regions) | `BlazorSpeechManager` (ARIA live regions) | `BlazorSpeechManager` (ARIA live regions) | `BlazorSpeechManager` (ARIA live regions) |
| **Input** | `IInputService` | `GlobalInputService` (JS interop) | `MainActivity.DispatchKeyEvent` | NOT YET WIRED (stub in AppDelegate.cs) | NOT YET WIRED |
| **Secure Storage** | `ISecureStorageService` | `MauiSecureStorageService` (DPAPI) | `MauiSecureStorageService` (KeyStore) | `MauiSecureStorageService` (Keychain) | `MauiSecureStorageService` (Keychain) |
| **Paths** | `IPlatformPathService` | `MauiPathService` | `MauiPathService` | `MauiPathService` | `MauiPathService` |

## 2. Rendering Architecture

The UI is built using **Blazor running inside a MAUI WebView**.

- **Charts:** Rendered via **SkiaSharp on a native MAUI `SKCanvasView`** at Grid layer 0 in `MainPage.xaml`. The `BlazorWebView` overlays at layer 1 (transparent). `SkiaSharp.Views.Blazor` is NOT used.
- **UI Chrome:** Blazor Razor components — toolbar, modals, status indicators — rendered in the transparent Blazor WebView overlay.
- **Theming:** CSS-based (Dark Mode priority). CSS variables control colors; accessible high-contrast ratios enforced.

## 3. Audio Architecture

The `AudioEngine` generates raw floating-point audio buffers.

- The engine is fully decoupled from any audio library.
- Platform drivers handle the "Final Mile" of pushing buffers to hardware output.
- Windows (WASAPI): `BlazorAudioDriver` pushes buffers to the WASAPI output device via NAudio.Wasapi's `WasapiOut`.
- Android (TODO Phase 5): `AudioTrack` write loop on a dedicated audio thread.
- iOS/macOS (TODO Phase 5): `AVAudioEngine` or `AudioUnit` push.

## 4. Keyboard Input Architecture

Input is normalized via the `GlobalInputService` (JS interop bridge) on all platforms that support it.

- MAUI captures raw hardware events and forwards them through the Blazor context via `[JSInvokable]` methods.
- `ShortcutManager` resolves key combos against a `ShortcutProfile` to identify `SystemCommand` values.
- `CommandDispatcher` routes `SystemCommand` values to `NavigationEngine`, `WorkspaceStore`, or EventBus.

## 5. Platform Compatibility Matrix

| Feature | Windows | Android | macOS | iOS |
| :--- | :---: | :---: | :---: | :---: |
| **Chart Rendering** | ✅ | ✅ | ✅ | ✅ |
| **Chart Sonification** | ✅ | ✅ (no audio out) | ✅ (no audio out) | ✅ (no audio out) |
| **Speech / Screen Reader** | ✅ | ✅ | ✅ | ✅ |
| **Market Data** | ✅ | ✅ | ✅ | ✅ |
| **Keyboard Navigation** | ✅ | ✅ (BT keyboard via MainActivity) | ❌ (stub) | ❌ (stub) |
| **Audio Output** | ✅ (WASAPI) | 🏗️ (AudioTrack TODO) | 🏗️ (AVFoundation TODO) | 🏗️ (AVFoundation TODO) |
| **Tactile Display** | 🏗️ (MonarchTactileDriver skeleton) | ❌ | ❌ | ❌ |
| **Coinbase Trading** | ❌ (JWT signing stub) | ❌ | ❌ | ❌ |

*(✅ = Fully Supported, 🏗️ = In Development / Stubbed, ❌ = Not Yet Implemented)*

## 6. Phase 5 Roadmap — Platform Parity

The following platform gaps are planned for Phase 5:

### Mac Keyboard Input
- **Location:** `AccessibleTrader.BlazorClient/Platforms/MacCatalyst/AppDelegate.cs`
- **Approach:** `NSEvent.AddLocalMonitorForEventsMatchingMask` for key events; route to `IInputService.ProcessKey` resolved via `IPlatformApplication.Current.Services`.
- **Blocker:** None — execution work only. Implementation guide is written in the AppDelegate.cs stub comment.

### Android Audio Output
- **Location:** `AccessibleTrader.BlazorClient/Services/BlazorAudioDriver.cs` (Android-conditional code)
- **Approach:** `AudioTrack` with a dedicated audio thread. `AudioEngine.GenerateBuffer` → `AudioTrack.Write` in a tight loop.
- **Blocker:** None — DSP engine contract is already defined. Needs platform-conditional `#if ANDROID` implementation block.

### iOS Audio Output
- **Location:** `AccessibleTrader.BlazorClient/Services/BlazorAudioDriver.cs` (iOS-conditional code)
- **Approach:** `AVAudioEngine` with a `AVAudioSourceNode` render callback, or `AudioUnit` with an output render callback.
- **Blocker:** None — same DSP contract. Needs `#if IOS || MACCATALYST` implementation block.

### Coinbase JWT Signing
- **Location:** `Plugins/AccessibleTrader.Plugins.Coinbase/CoinbaseProvider.cs`
- **Approach:** ES256 JWT with `nonce`, `timestamp`, and request path claims per Coinbase Advanced Trade API spec.
- **Blocker:** Until implemented, `ITradingProvider` capability must be advertised as unsupported in `CoinbaseProvider.Capabilities`.
