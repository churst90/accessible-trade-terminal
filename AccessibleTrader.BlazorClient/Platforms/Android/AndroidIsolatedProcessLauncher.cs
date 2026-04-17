using System;
using System.IO;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services.Scripting;
using Android.Content;
using Android.OS;
using Microsoft.Win32.SafeHandles;

namespace AccessibleTrader.BlazorClient.Platforms.Android;

/// <summary>
/// The real Android implementation of
/// <see cref="IScriptWorkerLauncher"/> — binds
/// <see cref="ScriptWorkerService"/> (which runs in an isolated
/// process thanks to its manifest attribute), hands it two pipes via
/// <see cref="Messenger"/>, and returns an
/// <see cref="AndroidScriptWorkerProcess"/> wrapping the host-side
/// pipe ends.
///
/// <para>
/// MAUI registers this launcher in DI at startup on the Android TFM
/// only. The Core-side
/// <see cref="Core.Services.Scripting.AndroidIsolatedProcessLauncher"/>
/// is a routing stub that throws if somehow reached — this one is
/// the thing that actually works.
/// </para>
/// </summary>
public sealed class AndroidIsolatedProcessLauncher : IScriptWorkerLauncher
{
    private static readonly TimeSpan BindTimeout = TimeSpan.FromSeconds(5);

    private readonly Context _context;

    public AndroidIsolatedProcessLauncher(Context context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IScriptWorkerProcess Launch(string workerExecutablePath)
    {
        // workerExecutablePath is ignored on Android — the "worker" is
        // the ScriptWorkerService class inside our own APK, launched by
        // Android into a fresh isolated-process sandbox.

        var intent = new Intent(_context, Java.Lang.Class.FromType(typeof(ScriptWorkerService)));

        // ── 1. Bind + wait for connection ────────────────────────────────
        var connTcs = new TaskCompletionSource<IBinder?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new Connection(connTcs);
        bool bound = _context.BindService(intent, connection, Bind.AutoCreate);
        if (!bound)
        {
            _context.UnbindService(connection);
            throw new InvalidOperationException(
                "ScriptWorkerService: BindService returned false. Check that the service is declared in the manifest and " +
                "enabled (see Platforms/Android/AndroidManifest.xml). Ensure the package has no unresolved INSTALL_FAILED_* error.");
        }

        IBinder? binder;
        try
        {
            binder = connTcs.Task.Wait(BindTimeout) ? connTcs.Task.Result : null;
        }
        catch
        {
            _context.UnbindService(connection);
            throw;
        }

        if (binder is null)
        {
            _context.UnbindService(connection);
            throw new TimeoutException(
                $"ScriptWorkerService bind did not complete within {BindTimeout.TotalSeconds:F0}s.");
        }

        // ── 2. Create two pipes (stdin host→worker, stdout worker→host) ─
        // ParcelFileDescriptor.CreatePipe returns [read, write].
        ParcelFileDescriptor[]? stdinPipe  = null;
        ParcelFileDescriptor[]? stdoutPipe = null;
        try
        {
            stdinPipe  = ParcelFileDescriptor.CreatePipe() ?? throw new IOException("ParcelFileDescriptor.CreatePipe returned null (stdin)");
            stdoutPipe = ParcelFileDescriptor.CreatePipe() ?? throw new IOException("ParcelFileDescriptor.CreatePipe returned null (stdout)");

            // CreatePipe is documented to return a non-null 2-element array
            // with non-null elements on any Android version that supports it
            // (Honeycomb+); we're min-target 24. The null-forgiving operators
            // below acknowledge that contract so the nullable analyser stops
            // complaining about array-element access.
            var workerStdinRead  = stdinPipe[0]!;   // worker reads host's writes
            var hostStdinWrite   = stdinPipe[1]!;   // host writes here
            var hostStdoutRead   = stdoutPipe[0]!;  // host reads here
            var workerStdoutWrite = stdoutPipe[1]!; // worker writes here

            // ── 3. Send init message with the worker-side ends ───────────
            var messenger = new Messenger(binder);
            using var msg = Message.Obtain() ?? throw new IOException("Message.Obtain returned null");
            msg.What = ScriptWorkerService.MsgInit;
            var bundle = new Bundle();
            bundle.PutParcelable(ScriptWorkerService.BundleKeyInput,  workerStdinRead);
            bundle.PutParcelable(ScriptWorkerService.BundleKeyOutput, workerStdoutWrite);
            msg.Data = bundle;
            messenger.Send(msg);

            // Close worker-side FDs in the host after delivery — Android
            // duplicates them into the service process during Parcel
            // marshaling, so our copies are no longer needed. Leaving
            // them open would keep the pipes alive indefinitely (no EOF
            // on the worker side if our write-end stayed open).
            workerStdinRead.Close();
            workerStdoutWrite.Close();

            // ── 4. Wrap host-side FDs as .NET Streams ───────────────────
            int hostStdinFd  = hostStdinWrite.DetachFd();
            int hostStdoutFd = hostStdoutRead.DetachFd();
            var hostStdinSafe  = new SafeFileHandle((IntPtr)hostStdinFd,  ownsHandle: true);
            var hostStdoutSafe = new SafeFileHandle((IntPtr)hostStdoutFd, ownsHandle: true);
            var hostStdinStream  = new FileStream(hostStdinSafe,  FileAccess.Write, bufferSize: 4096, isAsync: true);
            var hostStdoutStream = new FileStream(hostStdoutSafe, FileAccess.Read,  bufferSize: 4096, isAsync: true);

            return new AndroidScriptWorkerProcess(_context, connection, hostStdinStream, hostStdoutStream);
        }
        catch
        {
            // Cleanup on failure — close any pipe ends we still hold and
            // unbind the service so Android tears down the isolated process.
            try { stdinPipe?[0].Close();  } catch { }
            try { stdinPipe?[1].Close();  } catch { }
            try { stdoutPipe?[0].Close(); } catch { }
            try { stdoutPipe?[1].Close(); } catch { }
            try { _context.UnbindService(connection); } catch { }
            throw;
        }
    }

    private sealed class Connection : Java.Lang.Object, IServiceConnection
    {
        private readonly TaskCompletionSource<IBinder?> _tcs;

        public Connection(TaskCompletionSource<IBinder?> tcs) => _tcs = tcs;

        public void OnServiceConnected(ComponentName? name, IBinder? service) =>
            _tcs.TrySetResult(service);

        public void OnServiceDisconnected(ComponentName? name)
        {
            // Fires when the service process crashes or Android kills it.
            // If we were still waiting for initial connection, unblock
            // with null so the caller raises a clean timeout/error.
            _tcs.TrySetResult(null);
        }
    }
}
