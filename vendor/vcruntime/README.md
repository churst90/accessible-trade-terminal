# Visual C++ runtime (app-local)

`msvcp140.dll`, `vcruntime140.dll` and `vcruntime140_1.dll` belong here. They are **not
committed** — they are Microsoft redistributables and `.gitignore` excludes `vendor/vcruntime/*.dll`.

## Why the tactile display needs them

`DotPadSDK-3.0.0.dll` is a C++ library. Its PE import table names exactly three non-system
dependencies, and all three are the Visual C++ 2015–2022 runtime:

```
DotPadSDK-3.0.0.dll  ->  MSVCP140.dll, VCRUNTIME140.dll, VCRUNTIME140_1.dll
```

A .NET publish carries none of them. The published v2.12.0 Windows zip had 637 entries and not
one was a VC++ runtime file. On a machine without the redistributable installed,
`NativeLibrary.TryLoad` fails, `WindowsDotPadNative` logs *"Tactile DISABLED"* and falls back to
`NullDotPadNative` — while the Braille tab keeps rendering, because it is ordinary DOM. The
feature looks present and is inert, which is the hardest failure to notice.

Most developer machines have the redistributable because Visual Studio installs it. The users
least likely to have it are the ones who downloaded a zip and unpacked it, which is everyone this
package exists for.

## Getting them

`release.yml` copies them off the runner's VC redist directory automatically. For a hand-built
release, copy the three x64 DLLs from a Visual Studio install:

```
C:\Program Files\Microsoft Visual Studio\2022\<edition>\VC\Redist\MSVC\<version>\x64\Microsoft.VC143.CRT\
```

`ErrorIfVcRuntimeMissingOnRelease` in `AccessibleTrader.BlazorClient.csproj` fails a release
build that stages the Dot Pad SDK without them, and
`scripts/verify_release_payloads.py` fails the release if they are not in the artifact.
