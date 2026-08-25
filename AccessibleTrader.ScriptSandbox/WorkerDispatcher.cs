using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.ScriptSandbox;

/// <summary>
/// Shared worker-side dispatch loop. Reads <see cref="Opcode"/> frames
/// from an input <see cref="Stream"/>, loads user script assemblies
/// into a collectible <see cref="AssemblyLoadContext"/>, invokes the
/// loaded instance, and writes result / error / diagnostic frames back on
/// the output <see cref="Stream"/>.
///
/// <para>
/// Two kinds of script land here. An <c>ICustomIndicator</c> answers a
/// single <see cref="Opcode.Calculate"/> frame with component arrays. An
/// <c>ITradingStrategy</c> — the half of the scripting surface that opens
/// positions — is driven a bar at a time through
/// <see cref="Opcode.InitializeStrategy"/>, <see cref="Opcode.OnBar"/>,
/// <see cref="Opcode.OrderFilled"/>, <see cref="Opcode.StopStrategy"/> and
/// <see cref="Opcode.GetMetrics"/>, and its orders come back as
/// <see cref="Opcode.Signal"/> frames for the HOST to apply its own risk
/// rules to. Nothing the strategy returns places an order by itself.
/// </para>
///
/// <para>
/// Extracted from the desktop <c>AccessibleTrader.ScriptWorker.Program</c>
/// so the Android <c>ScriptWorkerService</c> (running inside a bound
/// <c>android.app.Service</c> with <c>isolatedProcess="true"</c>) can
/// reuse the exact same protocol. Desktop hands in
/// <see cref="Console.OpenStandardInput()"/> / <see cref="Console.OpenStandardOutput()"/>;
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

    // ── Strategy state ──────────────────────────────────────────────────
    // The loaded strategy TYPE outlives any one instance: every
    // InitializeStrategy frame constructs a fresh instance from it, which is
    // what lets the causality probe start over without reaching Activator
    // across the process boundary.
    private Type? _strategyType;
    private ITradingStrategy? _strategy;

    // The worker's own copy of the bar history, so OnBar can be sent a delta
    // instead of the whole buffer. See HistorySync for why that matters and
    // what pins it against drifting out of step with the host's.
    private readonly List<Ohlcv> _history = new();

    // The last workspace state the host sent. An OnBar frame with no state
    // means "unchanged" — the backtester passes one immutable liveState to
    // every bar of a run, so this is the common case there.
    private WorkspaceState? _state;

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

                case Opcode.InitializeStrategy:
                    await HandleInitializeStrategyAsync(payload, ct).ConfigureAwait(false);
                    break;

                case Opcode.OnBar:
                    await HandleOnBarAsync(payload, ct).ConfigureAwait(false);
                    break;

                case Opcode.OrderFilled:
                    await HandleOrderFilledAsync(payload, ct).ConfigureAwait(false);
                    break;

                case Opcode.StopStrategy:
                    await HandleStopStrategyAsync(ct).ConfigureAwait(false);
                    break;

                case Opcode.GetMetrics:
                    await HandleGetMetricsAsync(ct).ConfigureAwait(false);
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
                // Not an indicator — the other thing a user script can be is a strategy, and
                // that half is the half that places orders. Answering StrategyReady from the
                // same opcode keeps the host from having to know which it compiled.
                await HandleLoadStrategyAsync(assembly, ct).ConfigureAwait(false);
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
                DefaultParameters: _indicator.DefaultParameters ?? new(),
                CausalityValues:   (_indicator.Causality ?? Array.Empty<ComponentCausality>()).Select(c => (int)c).ToArray());
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

    // ── Strategy frames ────────────────────────────────────────────────────

    /// <summary>
    /// The <c>ITradingStrategy</c> arm of <c>LoadAssembly</c>. Instantiates the type once to read
    /// its declared metadata, then keeps the TYPE — every <c>InitializeStrategy</c> builds a fresh
    /// instance from it.
    /// </summary>
    private async Task HandleLoadStrategyAsync(Assembly assembly, CancellationToken ct)
    {
        var strategyType = assembly.GetTypes()
            .FirstOrDefault(t => !t.IsAbstract && !t.IsInterface
                                 && typeof(ITradingStrategy).IsAssignableFrom(t));
        if (strategyType == null)
        {
            await EmitErrorAsync(
                "no ICustomIndicator or ITradingStrategy implementation found in loaded assembly", ct).ConfigureAwait(false);
            return;
        }

        var probe = (ITradingStrategy?)Activator.CreateInstance(strategyType);
        if (probe == null)
        {
            await EmitErrorAsync(
                "failed to instantiate ITradingStrategy (needs a public parameterless constructor)", ct).ConfigureAwait(false);
            return;
        }

        _strategyType = strategyType;
        _strategy = probe;

        var meta = new StrategyMetadataMessage(
            Id:              probe.Id ?? "",
            Name:            probe.Name ?? "",
            Description:     probe.Description ?? "",
            ComplexityValue: (int)probe.Complexity,
            Parameters:      (probe.Parameters ?? Array.Empty<StrategyParameter>()).ToArray());

        await FrameCodec.WriteFrameAsync(_out, Opcode.StrategyReady, StrategyCodec.EncodeMetadata(meta), ct)
            .ConfigureAwait(false);
    }

    private async Task HandleInitializeStrategyAsync(byte[] payload, CancellationToken ct)
    {
        if (_strategyType == null)
        {
            await EmitErrorAsync("InitializeStrategy received before a successful LoadAssembly", ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var req = StrategyCodec.DecodeInitialize(payload);

            // A FRESH instance every time. The probe needs a strategy that has never seen a bar
            // for each of its runs, and "call Activator on the prototype's type" — how it does
            // that in-process — has no meaning through a proxy. Doing it here means the probe's
            // shape survives the move without the probe knowing a process boundary exists.
            var instance = (ITradingStrategy?)Activator.CreateInstance(_strategyType);
            if (instance == null)
            {
                await EmitErrorAsync(
                    "the strategy class could not be instantiated (it needs a public parameterless constructor)",
                    ct).ConfigureAwait(false);
                return;
            }

            _strategy = instance;
            _state = req.State;
            _history.Clear();
            _history.AddRange(req.History);

            var parameters = new Dictionary<string, object>(req.Parameters.Count, StringComparer.Ordinal);
            foreach (var kv in req.Parameters)
                if (kv.Value != null) parameters[kv.Key] = kv.Value;

            instance.Initialize(req.History, req.State, parameters);
            await FrameCodec.WriteFrameAsync(_out, Opcode.Ack, Array.Empty<byte>(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await EmitErrorAsync("Initialize threw: " + ex, ct).ConfigureAwait(false);
        }
    }

    private async Task HandleOnBarAsync(byte[] payload, CancellationToken ct)
    {
        if (_strategy == null)
        {
            await EmitErrorAsync("OnBar received before a successful InitializeStrategy", ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var req = StrategyCodec.DecodeOnBar(payload);

            if (req.History.FullResync)
            {
                _history.Clear();
                _history.AddRange(req.History.Bars);
            }
            else
            {
                _history.AddRange(req.History.Bars);
            }

            // The delta is only safe because both ends are pinned. A count that agrees while the
            // first bar does not is exactly the prepend-versus-append confusion that smeared a
            // whole indicator's values onto the wrong bars once already; here it would hand a
            // strategy a history that silently disagrees with the host's.
            if (_history.Count != req.History.ExpectedCount
                || (_history.Count > 0 && _history[0].Date.Ticks != req.History.FirstBarTicks))
            {
                await EmitErrorAsync(
                    $"history desync: worker holds {_history.Count} bars starting " +
                    $"{(_history.Count > 0 ? _history[0].Date.Ticks : 0)}, host expected " +
                    $"{req.History.ExpectedCount} starting {req.History.FirstBarTicks}", ct).ConfigureAwait(false);
                return;
            }

            if (req.State != null) _state = req.State;
            if (_state == null)
            {
                await EmitErrorAsync("OnBar arrived with no workspace state and none cached", ct).ConfigureAwait(false);
                return;
            }

            var signal = _strategy.OnBar(req.Bar, _history, _state);
            await FrameCodec.WriteFrameAsync(_out, Opcode.Signal,
                StrategyCodec.EncodeSignal(new SignalResponse(signal)), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await EmitErrorAsync("OnBar threw: " + ex, ct).ConfigureAwait(false);
        }
    }

    private async Task HandleOrderFilledAsync(byte[] payload, CancellationToken ct)
    {
        if (_strategy == null)
        {
            await EmitErrorAsync("OrderFilled received before a successful InitializeStrategy", ct).ConfigureAwait(false);
            return;
        }

        try
        {
            _strategy.OnOrderFilled(StrategyCodec.DecodeOrderUpdate(payload));
            await FrameCodec.WriteFrameAsync(_out, Opcode.Ack, Array.Empty<byte>(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await EmitErrorAsync("OnOrderFilled threw: " + ex, ct).ConfigureAwait(false);
        }
    }

    private async Task HandleStopStrategyAsync(CancellationToken ct)
    {
        if (_strategy == null)
        {
            await EmitErrorAsync("StopStrategy received before a successful InitializeStrategy", ct).ConfigureAwait(false);
            return;
        }

        try
        {
            _strategy.OnStop();
            await FrameCodec.WriteFrameAsync(_out, Opcode.Ack, Array.Empty<byte>(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await EmitErrorAsync("OnStop threw: " + ex, ct).ConfigureAwait(false);
        }
    }

    private async Task HandleGetMetricsAsync(CancellationToken ct)
    {
        if (_strategy == null)
        {
            await EmitErrorAsync("GetMetrics received before a successful InitializeStrategy", ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var metrics = _strategy.GetMetrics();
            await FrameCodec.WriteFrameAsync(_out, Opcode.Metrics,
                StrategyCodec.EncodeMetrics(metrics), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await EmitErrorAsync("GetMetrics threw: " + ex, ct).ConfigureAwait(false);
        }
    }

    private Task EmitErrorAsync(string message, CancellationToken ct) =>
        FrameCodec.WriteFrameAsync(_out, Opcode.Error, Encoding.UTF8.GetBytes(message), ct);

    private Task EmitDiagnosticAsync(string message, CancellationToken ct) =>
        FrameCodec.WriteFrameAsync(_out, Opcode.Diagnostic, Encoding.UTF8.GetBytes(message), ct);
}
