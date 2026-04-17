using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.ScriptSandbox;
using Android.App;
using Android.Content;
using Android.OS;
using Microsoft.Win32.SafeHandles;

namespace AccessibleTrader.BlazorClient.Platforms.Android;

/// <summary>
/// Android isolated-process bound service that hosts the script worker.
///
/// <para>
/// Declared with <c>android:isolatedProcess="true"</c> so Android runs
/// it under its own UID with no access to the parent app's data
/// directory, no shared preferences, no SQLite DBs — the OS sandbox
/// equivalent of Windows AppContainer / macOS <c>sandbox-exec</c>.
/// A successful escape of the in-worker Roslyn semantic sandbox therefore
/// still can't reach the host's keychain, HTTP sessions, or local files.
/// </para>
///
/// <para>
/// IPC is a <see cref="Messenger"/> with a single <c>INIT</c> opcode.
/// The host sends two <see cref="ParcelFileDescriptor"/>s in the message
/// bundle — one pipe carrying host→worker frames, one carrying
/// worker→host frames — and the service wraps them as .NET
/// <see cref="FileStream"/>s and hands them to
/// <see cref="WorkerDispatcher"/> (the exact same shared dispatch loop
/// used by the desktop console worker).
/// </para>
///
/// <para>
/// Why not AIDL: only one method + one direction of pipe transfer is
/// needed. A full AIDL contract would be code-generation overhead for
/// no functional gain. Messenger's Handler / Bundle pipeline already
/// marshals <see cref="ParcelFileDescriptor"/> across the process
/// boundary via standard Parcelable support.
/// </para>
/// </summary>
[Service(
    Name            = "crc64.AccessibleTrader.ScriptWorkerService",
    IsolatedProcess = true,
    Exported        = false,
    Enabled         = true)]
public class ScriptWorkerService : Service
{
    internal const int MsgInit = 1;
    internal const string BundleKeyInput  = "input";
    internal const string BundleKeyOutput = "output";

    private IncomingHandler? _handler;

    public override IBinder? OnBind(Intent? intent)
    {
        _handler = new IncomingHandler();
        var messenger = new Messenger(_handler);
        return messenger.Binder;
    }

    public override bool OnUnbind(Intent? intent)
    {
        // Signal the dispatcher to stop; the pipe closures (host dropping
        // its write end) already trigger EOF in the frame reader, so this
        // is belt-and-braces.
        _handler?.Shutdown();
        return base.OnUnbind(intent);
    }

    private sealed class IncomingHandler : Handler
    {
        private FileStream? _input;
        private FileStream? _output;
        private CancellationTokenSource? _cts;
        private Task? _dispatchTask;

        public IncomingHandler() : base(Looper.MainLooper!) { }

        public override void HandleMessage(Message? msg)
        {
            if (msg is null) return;
            if (msg.What != MsgInit) return;
            if (_dispatchTask != null) return; // one-shot init

            var bundle = msg.Data;
            if (bundle is null) return;

            // A Bundle delivered across a process boundary needs an
            // explicit ClassLoader so the Parcelable types it contains
            // can be resolved on the receiving side.
            bundle.ClassLoader = Java.Lang.Class.FromType(typeof(ParcelFileDescriptor)).ClassLoader;

            // Bundle.GetParcelable(string) is deprecated in favor of
            // GetParcelable(string, Class<T>) on Android 33+. We support
            // min-API 24 so we branch at runtime: new typed overload on
            // 33+, classic cast fallback (with suppression of the
            // deprecation warning) on 24-32.
            ParcelFileDescriptor? inputPfd;
            ParcelFileDescriptor? outputPfd;
            var pfdClass = Java.Lang.Class.FromType(typeof(ParcelFileDescriptor));
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                inputPfd  = (ParcelFileDescriptor?)bundle.GetParcelable(BundleKeyInput,  pfdClass);
                outputPfd = (ParcelFileDescriptor?)bundle.GetParcelable(BundleKeyOutput, pfdClass);
            }
            else
            {
#pragma warning disable CA1422 // deprecated on 33+, still required on 24-32
                inputPfd  = (ParcelFileDescriptor?)bundle.GetParcelable(BundleKeyInput);
                outputPfd = (ParcelFileDescriptor?)bundle.GetParcelable(BundleKeyOutput);
#pragma warning restore CA1422
            }
            if (inputPfd is null || outputPfd is null) return;

            // DetachFd transfers ownership of the file descriptor out of
            // the ParcelFileDescriptor; subsequent PFD disposal is a no-op.
            // The SafeFileHandle with ownsHandle=true now fully owns the
            // descriptor — FileStream.Dispose closes it.
            int inFd  = inputPfd.DetachFd();
            int outFd = outputPfd.DetachFd();

            var inSafe  = new SafeFileHandle((IntPtr)inFd,  ownsHandle: true);
            var outSafe = new SafeFileHandle((IntPtr)outFd, ownsHandle: true);

            _input  = new FileStream(inSafe,  FileAccess.Read,  bufferSize: 4096, isAsync: true);
            _output = new FileStream(outSafe, FileAccess.Write, bufferSize: 4096, isAsync: true);

            _cts = new CancellationTokenSource();
            var dispatcher = new WorkerDispatcher(_input, _output);
            _dispatchTask = Task.Run(async () =>
            {
                try
                {
                    await dispatcher.RunAsync(_cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Frame-level errors are already reported via Opcode.Error
                    // by the dispatcher. Anything reaching here is a torn
                    // pipe / unbind — exit silently.
                }
            });
        }

        public void Shutdown()
        {
            try { _cts?.Cancel(); } catch { }
            try { _input?.Dispose(); } catch { }
            try { _output?.Dispose(); } catch { }
        }
    }
}
