using System;

namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// Core-side routing stub for the Android isolated-process launcher.
/// The full-featured launcher lives in the Android platform layer
/// (<c>AccessibleTrader.BlazorClient/Platforms/Android/AndroidIsolatedProcessLauncher.cs</c>)
/// because it has to bind an <c>android.app.Service</c> and uses
/// <c>Android.OS.ParcelFileDescriptor</c> — types that only exist in
/// the <c>net10.0-android</c> target framework.
///
/// <para>
/// MAUI overrides this registration at startup: on Android,
/// <c>MauiProgram.CreateMauiApp()</c> calls the platform launcher's
/// <c>RegisterWithDi</c> helper to replace this stub with the real
/// implementation. This Core-side fallback exists for two reasons:
/// (1) a non-MAUI unit-test host targeting <c>net10.0</c> can still
/// link against <c>AccessibleTrader.Core</c> even on an Android build
/// farm; (2) if the MAUI override is accidentally dropped, the error
/// is loud and specific rather than a confusing null reference.
/// </para>
/// </summary>
public sealed class AndroidIsolatedProcessLauncher : IScriptWorkerLauncher
{
    /// <summary>Always <c>false</c> for this stub — the real launcher in
    /// the Android platform layer is what actually applies the isolated
    /// process sandbox.</summary>
    public bool SandboxApplied => false;

    public IScriptWorkerProcess Launch(string workerExecutablePath)
    {
        throw new PlatformNotSupportedException(
            "Android isolated-process script worker: the Core-side stub was reached but the Android " +
            "platform launcher was never registered. Ensure MauiProgram.CreateMauiApp() wires the " +
            "platform launcher from AccessibleTrader.BlazorClient/Platforms/Android/AndroidIsolatedProcessLauncher.cs " +
            "into DI on Android builds, or set ACCESSIBLETRADER_SCRIPT_IN_PROCESS=1 to force the in-process ALC path.");
    }
}
