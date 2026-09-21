# NVDA Controller Client

`nvdaControllerClient64.dll` belongs here. It is **not committed** — it is a third-party binary
(NV Access, LGPL 2.1) and `.gitignore` excludes `vendor/nvda/*.dll`.

## Why it matters on the MAUI head

`BlazorSpeechManager` P/Invokes this DLL for NVDA-direct speech. On the desktop head that is not
a nicety, it is the only path that works while the chart has focus: the chart is a native
SkiaSharp canvas sitting on top of the `BlazorWebView`, so a screen reader focused on the chart
is not reading the WebView's DOM and the ARIA live-region fallback cannot reach it. **No DLL
means the chart is silent.**

The WebHost is unaffected — there the whole surface is the DOM.

## Getting it

1. Download the controller client from <https://github.com/nvaccess/nvda/releases> — the
   `nvda_<version>_controllerClient.zip` asset.
2. Copy the **x64** `nvdaControllerClient64.dll` into this directory.
3. Rebuild. `CopyNvdaControllerWindows` in `AccessibleTrader.BlazorClient.csproj` stages it next
   to the host binary; `WarnIfNvdaControllerMissing` prints a high-importance build message when
   it is absent, so a build that will be mute says so.
