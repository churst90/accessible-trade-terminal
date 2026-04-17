namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// Spawns a ScriptWorker instance and returns a launcher-owned
/// <see cref="IScriptWorkerProcess"/> handle that
/// <see cref="OutOfProcessScriptHost"/> drives through a stdio-style
/// frame protocol.
///
/// <para>
/// Different platforms want different isolation primitives:
/// </para>
///
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Windows:</b> <see cref="WindowsAppContainerLauncher"/>
///       builds the worker via <c>CreateProcessW</c> with
///       <c>STARTUPINFOEX</c> + <c>PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES</c>
///       pointing at an AppContainer profile SID — zero network, zero
///       file-system outside the profile's sandbox directory.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>macOS / Mac Catalyst:</b>
///       <see cref="MacSandboxExecLauncher"/> wraps the worker in
///       <c>sandbox-exec -f script-worker.sb</c> (deny-default profile
///       shipped alongside the binary).
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Android:</b> the full-featured launcher lives in the
///       Android platform layer (<c>AccessibleTrader.BlazorClient/Platforms/Android/</c>)
///       because it has to bind an <c>android.app.Service</c> with
///       <c>android:isolatedProcess="true"</c>. The Core-side
///       <see cref="AndroidIsolatedProcessLauncher"/> is a routing
///       stub; MAUI overrides the DI registration with the real one
///       at startup.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Other / fallback:</b> <see cref="DefaultProcessLauncher"/>
///       spawns the worker unsandboxed via
///       <see cref="System.Diagnostics.Process.Start()"/> — process-boundary
///       isolation only. Used when none of the platform-specific
///       launchers apply.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// The launcher is responsible for configuring stdio redirection so
/// <see cref="OutOfProcessScriptHost"/> can read/write frames. The
/// returned <see cref="IScriptWorkerProcess"/> owns all underlying OS
/// handles and closes them on <see cref="System.IDisposable.Dispose"/>.
/// </para>
/// </summary>
public interface IScriptWorkerLauncher
{
    /// <summary>
    /// Start a worker process and hand back the launcher's wrapper.
    /// <paramref name="workerExecutablePath"/> is the resolved absolute
    /// path to the worker binary on platforms that use one (desktop).
    /// Platform launchers that don't need an on-disk executable
    /// (Android) may ignore the argument.
    /// </summary>
    IScriptWorkerProcess Launch(string workerExecutablePath);
}
