using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.ScriptSandbox;

/// <summary>
/// Shared worker-side dispatch loop. Reads <see cref="Opcode"/> frames
/// from an input <see cref="Stream"/>, loads user indicator assemblies
/// into a collectible <see cref="AssemblyLoadContext"/>, invokes
/// <see cref="ICustomIndicator.Calculate"/> on the loaded instance, and
/// writes result / error / diagnostic frames back on the output
/// <see cref="Stream"/>.
///
/// <para>
/// Extracted from the desktop <c>AccessibleTrader.ScriptWorker.Program</c>
/// so the Android <c>ScriptWorkerService</c> (running inside a bound
/// <c>android.app.Service</c> with <c>isolatedProcess="true"</c>) can
/// reuse the exact same protocol. Desktop hands in
/// <see cref="Console.OpenStandardInput"/> / <see cref="Console.OpenStandardOutput"/>;
/// Android hands in streams built from a <c>ParcelFileDescriptor</c>
/// pipe pair. The dispatcher is transport-agnostic.
/// </para>
///
/// <para>
/// Error discipline: every code path that could throw catches at the
/// dispatch boundary and emits an <see cref="Opcode.Error"/> frame so
/// the host surfaces the failure to the user. Only fatal setup errors
/// (input closed before the first Shutdown, contract library version
/// mismatch) propagate as exceptions.
/// </para>
/// </summary>
public sealed class WorkerDispatcher
{
    private readonly Stream _in;
    private readonly Stream _out;

    // One collectible ALC per worker lifetime. Loaded by the first
    // LoadAssembly frame; unloaded on Shutdown.
    private AssemblyLoadContext? _alc;
    private ICustomIndicator? _indicator;

    public WorkerDispatcher(Stream input, Stream output)
    {
        _in  = input  ?? throw new ArgumentNullException(nameof(input));
        _out = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Points <see cref="Console"/> away from the IPC pipe, before any user code can run.
    ///
    /// <para>
    /// The worker speaks a binary frame protocol over stdout, and the user's indicator runs
    /// <b>inside this process</b>. A single <c>Console.WriteLine("debug")</c> in a script
    /// therefore writes raw text into the middle of the frame stream: the host reads the first
    /// four bytes of that text as a length prefix and either desyncs or throws
    /// "malformed stream", and the indicator fails with a message that has nothing to do with
    /// what the author actually did. Printing to see what your script is doing is the most
    /// natural debugging move there is, so this had to be safe rather than merely forbidden.
    /// </para>
    ///
    /// <para>
    /// Console output goes to <paramref name="diagnosticSink"/> — stderr in the real worker,
    /// which the host already pumps into its log — so the print still reaches the developer.
    /// Console input is closed off in the same move: <c>Console.ReadLine()</c> would otherwise
    /// eat the host's next command frame.
    /// </para>
    /// </summary>
    public static void IsolateConsole(Stream diagnosticSink)
    {
        if (diagnosticSink == null) throw new ArgumentNullException(nameof(diagnosticSink));

        var writer = new StreamWriter(diagnosticSink, new UTF8Encoding(false)) { AutoFlush = true };
        Console.SetOut(writer);
        Console.SetError(writer);
        Console.SetIn(TextReader.Null);
    }

    /// <summary>
    /// Drive the worker loop until a <see cref="Opcode.Shutdown"/> frame
    /// arrives or <paramref name="ct"/> is cancelled. Returns normally
    /// on Shutdown; propagates <see cref="EndOfStreamException"/> if
    /// the input pipe closes without a Shutdown (caller should treat
    /// as clean exit).
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        await EmitDiagnosticAsync("worker ready", ct).ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            var (opcode, payload) = await FrameCodec.ReadFrameAsync(_in, ct).ConfigureAwait(false);

            switch (opcode)
            {
                case Opcode.LoadAssembly:
                    await HandleLoadAssemblyAsync(payload, ct).ConfigureAwait(false);
                    break;

                case Opcode.Calculate:
                    await HandleCalculateAsync(payload, ct).ConfigureAwait(false);
                    break;

                case Opcode.Shutdown:
                    await EmitDiagnosticAsync("shutdown received", ct).ConfigureAwait(false);
                    try { _alc?.Unload(); } catch { /* best-effort */ }
                    return;

                default:
                    // Reserved / unrecognised opcode — protocol violation
                    // but not fatal. Report and keep reading.
                    await EmitErrorAsync($"unrecognised opcode 0x{(byte)opcode:X2}", ct).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task HandleLoadAssemblyAsync(byte[] payload, CancellationToken ct)
    {
        if (_alc != null)
        {
            await EmitErrorAsync("LoadAssembly already handled; worker is single-use", ct).ConfigureAwait(false);
            return;
        }

        try
        {
            _alc = new AssemblyLoadContext("UserScript", isCollectible: true);
            Assembly assembly;
            using (var ms = new MemoryStream(payload))
                assembly = _alc.LoadFromStream(ms);

            var indicatorType = assembly.GetTypes()
                .FirstOrDefault(t => !t.IsAbstract && !t.IsInterface
                                     && typeof(ICustomIndicator).IsAssignableFrom(t));
            if (indicatorType == null)
            {
                await EmitErrorAsync("no ICustomIndicator implementation found in loaded assembly", ct).ConfigureAwait(false);
                return;
            }

            _indicator = (ICustomIndicator?)Activator.CreateInstance(indicatorType);
            if (_indicator == null)
            {
                await EmitErrorAsync("failed to instantiate ICustomIndicator (needs a public parameterless constructor)", ct).ConfigureAwait(false);
                return;
            }

            // Ready response carries metadata the host caches for the
            // lifetime of this worker.
            var meta = new IndicatorMetadataMessage(
                Id:                _indicator.Id ?? "",
                DisplayName:       _indicator.DisplayName ?? "",
                ComponentNames:    _indicator.ComponentNames ?? Array.Empty<string>(),
                DisplayTypeValues: (_indicator.DisplayTypes ?? Array.Empty<ComponentDisplayType>()).Select(d => (int)d).ToArray(),
                DefaultParameters: _indicator.DefaultParameters ?? new());
            var encoded = MessageCodec.EncodeMetadata(meta);
            await FrameCodec.WriteFrameAsync(_out, Opcode.Ready, encoded, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await EmitErrorAsync("LoadAssembly failed: " + ex, ct).ConfigureAwait(false);
        }
    }

    private async Task HandleCalculateAsync(byte[] payload, CancellationToken ct)
    {
        if (_indicator == null)
        {
            await EmitErrorAsync("Calculate received before a successful LoadAssembly", ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var req = MessageCodec.DecodeCalculateRequest(payload);
            // ReadOnlySpan<Ohlcv> can't cross async resume points, so no
            // await between span construction and result consumption.
            var result = _indicator.Calculate(req.Bars.AsSpan(), req.Parameters);
            if (result == null)
            {
                await EmitErrorAsync("indicator returned null from Calculate", ct).ConfigureAwait(false);
                return;
            }

            var encoded = MessageCodec.EncodeCalculateResponse(new CalculateResponse(result));
            await FrameCodec.WriteFrameAsync(_out, Opcode.Result, encoded, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await EmitErrorAsync("Calculate threw: " + ex, ct).ConfigureAwait(false);
        }
    }

    private Task EmitErrorAsync(string message, CancellationToken ct) =>
        FrameCodec.WriteFrameAsync(_out, Opcode.Error, Encoding.UTF8.GetBytes(message), ct);

    private Task EmitDiagnosticAsync(string message, CancellationToken ct) =>
        FrameCodec.WriteFrameAsync(_out, Opcode.Diagnostic, Encoding.UTF8.GetBytes(message), ct);
}
