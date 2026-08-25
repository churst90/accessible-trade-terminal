using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.ScriptSandbox;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// Host-side supervisor for a single ScriptWorker instance. Owns an
/// <see cref="IScriptWorkerProcess"/> handle (the launcher decides the
/// concrete transport: plain child process, Windows AppContainer child,
/// Android isolated-process service binding, etc.), serializes access
/// to its stdin, reads response frames off its stdout, enforces
/// wall-clock timeouts, and polls a memory quota.
///
/// Lifecycle (one host per compiled script):
///
/// <list type="number">
///   <item>
///     <description>
///       <see cref="StartAsync"/> — spawn worker via
///       <see cref="IScriptWorkerLauncher"/>, send <see cref="Opcode.LoadAssembly"/>,
///       wait for <see cref="Opcode.Ready"/>, stash the returned metadata.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="CalculateAsync"/> — serialize a <see cref="CalculateRequest"/>,
///       send it, wait for <see cref="Opcode.Result"/>, deserialize. All
///       calls are serialized on <see cref="_ioGate"/> so multiple indicator
///       ticks cannot interleave frames on the stdio pipe.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="DisposeAsync"/> — send <see cref="Opcode.Shutdown"/>,
///       give the worker a short grace window, then kill it.
///     </description>
///   </item>
/// </list>
///
/// Per-call timeout kills the worker — a hung script is suspect and we
/// don't want to keep feeding frames to a worker that may never answer.
/// The host surfaces the timeout as a <see cref="TimeoutException"/> so the
/// Scripting layer can translate into a user-facing diagnostic.
/// </summary>
public sealed class OutOfProcessScriptHost : IAsyncDisposable
{
    /// <summary>Max wall-clock a single Calculate call gets before we kill the worker.</summary>
    public static readonly TimeSpan DefaultCalculateTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Max wall-clock a single strategy frame (Initialize / OnBar / OrderFilled / OnStop /
    /// GetMetrics) gets before we kill the worker. Same budget as an indicator Calculate, for
    /// the same reason: one bar's decision is a small computation, and a strategy that cannot
    /// answer in five seconds is not one that can be driven off a live bar close.
    /// </summary>
    public static readonly TimeSpan DefaultStrategyCallTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Max wall-clock for the initial LoadAssembly → Ready handshake.</summary>
    public static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Grace window after Shutdown before we force-kill.</summary>
    public static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Default working-set ceiling for a worker process. 256 MB is
    /// comfortably above anything a legitimate indicator needs on a typical
    /// bar-window (a few MB of doubles) yet below the point where a leaking
    /// or runaway script would start hurting the host.
    /// </summary>
    public const long DefaultMaxWorkingSetBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Default CPU-utilisation ceiling, expressed as a fraction of wall-clock.
    /// 0.9 means "killing at 90% sustained" — a tight busy-loop pegs one core
    /// at ~1.0 while a legitimate indicator Calculate typically sits well under
    /// 0.3 for the brief 5-second budget. Sustained above this threshold for
    /// more than one polling interval triggers a kill.
    /// </summary>
    public const double DefaultMaxCpuFraction = 0.9;

    /// <summary>
    /// Default cap on concurrent worker processes. A user compiling 100
    /// indicators would otherwise spawn 100 processes; each consumes native
    /// memory and file handles even before the first Calculate runs. 16 is a
    /// generous ceiling — four sub-panes × four open chart tabs — past which
    /// the next <see cref="StartAsync"/> refuses with a clear error instead
    /// of drifting the host into resource exhaustion.
    /// </summary>
    public const int DefaultMaxConcurrentWorkers = 16;

    /// <summary>How often the memory quota is polled. 2 s keeps kill latency
    /// low without burning measurable CPU — <see cref="IScriptWorkerProcess.Refresh"/>
    /// is a single OS read.</summary>
    public static readonly TimeSpan MemoryPollInterval = TimeSpan.FromSeconds(2);

    // ── Process-wide concurrency gate ───────────────────────────────────────
    // The cap applies across every host instance in the process — "per-user"
    // in the practical sense since each user has one host process at a time.
    // Start() increments, Dispose() decrements; overflow refuses the launch.
    private static int _activeWorkerCount;
    private static int _maxConcurrentWorkers = DefaultMaxConcurrentWorkers;

    /// <summary>Override the default <see cref="DefaultMaxConcurrentWorkers"/>
    /// at startup. Must be ≥ 1. Primarily for tests that deliberately spawn
    /// many workers in parallel.</summary>
    public static void SetMaxConcurrentWorkers(int max)
    {
        if (max < 1) throw new ArgumentOutOfRangeException(nameof(max));
        Interlocked.Exchange(ref _maxConcurrentWorkers, max);
    }

    /// <summary>Current live worker count. Exposed for diagnostics /
    /// telemetry UI ("N scripts running").</summary>
    public static int ActiveWorkerCount => Volatile.Read(ref _activeWorkerCount);

    private readonly IScriptWorkerProcess _proc;
    private readonly Stream  _stdin;
    private readonly Stream  _stdout;
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly ILogger? _logger;
    private readonly string _scriptId;

    private readonly long _maxWorkingSet;
    private readonly Timer? _memoryMonitor;
    private volatile bool _killedForMemory;
    private long _memoryOvershoot;

    // CPU-quota tracking. We sample TotalProcessorTime at each tick and compare
    // the delta against the elapsed wall-clock to derive a utilisation fraction.
    private readonly double _maxCpuFraction;
    private TimeSpan _lastCpuSample;
    private DateTime _lastCpuSampleUtc;
    private volatile bool _killedForCpu;

    private IndicatorMetadataMessage? _metadata;
    private StrategyMetadataMessage? _strategyMetadata;
    private bool _disposed;

    private OutOfProcessScriptHost(IScriptWorkerProcess proc, string scriptId, long maxWorkingSetBytes, double maxCpuFraction, ILogger? logger)
    {
        _proc    = proc;
        _stdin   = proc.StdinWrite;
        _stdout  = proc.StdoutRead;
        _logger  = logger;
        _scriptId = scriptId;
        _maxWorkingSet = maxWorkingSetBytes;
        _maxCpuFraction = maxCpuFraction;

        // Stderr is for fatal worker diagnostics — pump to the host log
        // asynchronously so we don't block reading it and cause the worker
        // to stall on a full buffer.
        _ = PumpStderrAsync(proc.StderrReader);

        // Baseline for CPU-quota deltas. First polling tick compares against
        // this snapshot so we don't mis-accuse the worker of a CPU spike it
        // accumulated during LoadAssembly (which can legitimately saturate a
        // core for a brief moment).
        _lastCpuSample   = _proc.TotalProcessorTime;
        _lastCpuSampleUtc = DateTime.UtcNow;

        // Quota polling: memory + CPU on the same timer so they stay in phase.
        // Memory quota ≤ 0 disables the memory side (tests that need debug
        // loads); CPU fraction ≤ 0 disables the CPU side. Implementations that
        // can't report a metric return 0, which the respective check treats
        // as "no data" and skips.
        if (_maxWorkingSet > 0 || _maxCpuFraction > 0)
        {
            _memoryMonitor = new Timer(
                CheckQuotas,
                state: null,
                dueTime: MemoryPollInterval,
                period:  MemoryPollInterval);
        }
    }

    /// <summary>
    /// Published metadata from the worker's <see cref="Opcode.Ready"/>
    /// response. Populated by <see cref="StartAsync"/>.
    /// </summary>
    public IndicatorMetadataMessage Metadata =>
        _metadata ?? throw new InvalidOperationException("StartAsync has not completed successfully.");

    /// <summary>
    /// Published metadata from the worker's <see cref="Opcode.StrategyReady"/> response —
    /// populated instead of <see cref="Metadata"/> when the loaded assembly turned out to hold
    /// an <c>ITradingStrategy</c>. Which one a worker answers with is the worker's decision, not
    /// the host's: see <see cref="Opcode.LoadAssembly"/>.
    /// </summary>
    public StrategyMetadataMessage StrategyMetadata =>
        _strategyMetadata ?? throw new InvalidOperationException(
            "This worker did not load a strategy (StartAsync has not completed, or the assembly held an indicator).");

    /// <summary><c>true</c> when the loaded assembly held an <c>ITradingStrategy</c>.</summary>
    public bool IsStrategy => _strategyMetadata != null;

    public bool IsAlive => !_disposed && !_proc.HasExited;

    public static async Task<OutOfProcessScriptHost> StartAsync(
        IScriptWorkerLauncher launcher,
        string workerExecutablePath,
        byte[] assemblyBytes,
        string scriptId,
        TimeSpan? startTimeout = null,
        long? maxWorkingSetBytes = null,
        double? maxCpuFraction = null,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        // Per-user concurrency gate. Incrementing atomically before launching
        // ensures a flurry of concurrent compilations can't race past the cap.
        int newCount = Interlocked.Increment(ref _activeWorkerCount);
        int cap = Volatile.Read(ref _maxConcurrentWorkers);
        if (newCount > cap)
        {
            Interlocked.Decrement(ref _activeWorkerCount);
            throw new InvalidOperationException(
                $"Worker launch refused — {cap} concurrent script workers already active. Close an existing custom indicator before compiling another.");
        }

        IScriptWorkerProcess? proc = null;
        OutOfProcessScriptHost? host = null;
        try
        {
            proc = launcher.Launch(workerExecutablePath);
            host = new OutOfProcessScriptHost(proc, scriptId,
                maxWorkingSetBytes ?? DefaultMaxWorkingSetBytes,
                maxCpuFraction ?? DefaultMaxCpuFraction,
                logger);
            await host.LoadAssemblyAsync(assemblyBytes, startTimeout ?? DefaultStartTimeout, ct).ConfigureAwait(false);
            return host;
        }
        catch
        {
            if (host != null) await host.DisposeAsync().ConfigureAwait(false);
            else
            {
                // Host never constructed — we still own the concurrency slot.
                Interlocked.Decrement(ref _activeWorkerCount);
                try { proc?.Dispose(); } catch { /* best-effort */ }
            }
            throw;
        }
    }

    private void CheckQuotas(object? state)
    {
        if (_disposed) return;
        try
        {
            if (_proc.HasExited) return;
            _proc.Refresh();

            // ── Memory quota ────────────────────────────────────────────────
            if (_maxWorkingSet > 0)
            {
                var ws = _proc.WorkingSet64;
                // 0 = platform doesn't expose a working-set number through
                // this transport; skip rather than false-positive.
                if (ws > 0 && ws > _maxWorkingSet)
                {
                    _memoryOvershoot = ws;
                    _killedForMemory = true;
                    _logger?.LogWarning(
                        "script worker {Id} killed — working set {WorkingSet:N0} bytes exceeded quota {Limit:N0} bytes",
                        _scriptId, ws, _maxWorkingSet);
                    RecordSecurityEvent(
                        AccessibleTrader.Sdk.Services.SecurityEventKind.MemoryQuotaKill,
                        $"working set {ws:N0} exceeded quota {_maxWorkingSet:N0}",
                        new Dictionary<string, string>
                        {
                            ["workingSet"] = ws.ToString(),
                            ["quota"]      = _maxWorkingSet.ToString(),
                        });
                    try { _proc.Kill(entireProcessTree: true); } catch { /* already exited */ }
                    return;
                }
            }

            // ── CPU quota ───────────────────────────────────────────────────
            // Derive utilisation from TotalProcessorTime delta over the
            // elapsed polling window. A tight busy-loop pegs one core at ~1.0;
            // legitimate indicator Calculate sits well under 0.3 even for the
            // brief 5-second budget. The 0.9 threshold trips on sustained
            // pegging across a full polling interval, giving legitimate spikes
            // (e.g. a one-shot heavy EMA backfill) room to finish before the
            // next tick.
            if (_maxCpuFraction > 0)
            {
                var cpu = _proc.TotalProcessorTime;
                if (cpu > TimeSpan.Zero)
                {
                    var now = DateTime.UtcNow;
                    var wall = now - _lastCpuSampleUtc;
                    var cpuDelta = cpu - _lastCpuSample;
                    _lastCpuSample = cpu;
                    _lastCpuSampleUtc = now;
                    if (wall.TotalMilliseconds > 500 && cpuDelta > TimeSpan.Zero)
                    {
                        double fraction = cpuDelta.TotalMilliseconds / wall.TotalMilliseconds;
                        if (fraction > _maxCpuFraction)
                        {
                            _killedForCpu = true;
                            _logger?.LogWarning(
                                "script worker {Id} killed — CPU utilisation {Fraction:P0} exceeded quota {Limit:P0} over {Window:F1} s",
                                _scriptId, fraction, _maxCpuFraction, wall.TotalSeconds);
                            RecordSecurityEvent(
                                AccessibleTrader.Sdk.Services.SecurityEventKind.CalculateTimeout,
                                $"CPU utilisation {fraction:P0} exceeded quota {_maxCpuFraction:P0}",
                                new Dictionary<string, string>
                                {
                                    ["cpuFraction"] = fraction.ToString("F3"),
                                    ["quota"]       = _maxCpuFraction.ToString("F3"),
                                    ["windowMs"]    = wall.TotalMilliseconds.ToString("F0"),
                                });
                            try { _proc.Kill(entireProcessTree: true); } catch { /* already exited */ }
                        }
                    }
                }
            }
        }
        catch
        {
            // Process may have exited between the HasExited check and the
            // Refresh call; swallow and wait for the next tick or for the
            // IO pipe break to surface via Calculate.
        }
    }

    private void RecordSecurityEvent(
        AccessibleTrader.Sdk.Services.SecurityEventKind kind,
        string message,
        IReadOnlyDictionary<string, string>? data = null)
    {
        var log = AccessibleTrader.Sdk.Services.PluginHostServices.SecurityEvents;
        if (log is null) return;
        log.Record(new AccessibleTrader.Sdk.Services.SecurityEvent(
            DateTime.UtcNow, kind,
            Source: $"ScriptWorker[{_scriptId}]",
            Message: message,
            Data: data));
    }

    private async Task LoadAssemblyAsync(byte[] assemblyBytes, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        await _ioGate.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        try
        {
            await FrameCodec.WriteFrameAsync(_stdin, Opcode.LoadAssembly, assemblyBytes, timeoutCts.Token).ConfigureAwait(false);

            // Drain frames until we see Ready or Error. Diagnostics are
            // logged and swallowed; anything else is a protocol violation.
            while (true)
            {
                var (opcode, payload) = await FrameCodec.ReadFrameAsync(_stdout, timeoutCts.Token).ConfigureAwait(false);
                switch (opcode)
                {
                    case Opcode.Ready:
                        _metadata = MessageCodec.DecodeMetadata(payload);
                        return;
                    case Opcode.StrategyReady:
                        _strategyMetadata = StrategyCodec.DecodeMetadata(payload);
                        return;
                    case Opcode.Error:
                        throw new InvalidOperationException(
                            $"script worker {_scriptId} rejected LoadAssembly: {Encoding.UTF8.GetString(payload)}");
                    case Opcode.Diagnostic:
                        _logger?.LogDebug("[worker {Id}] {Msg}", _scriptId, Encoding.UTF8.GetString(payload));
                        continue;
                    default:
                        throw new InvalidDataException(
                            $"script worker {_scriptId} sent unexpected opcode 0x{(byte)opcode:X2} during startup");
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"script worker {_scriptId} did not respond within {timeout.TotalSeconds:F0}s during startup");
        }
        finally
        {
            _ioGate.Release();
        }
    }

    /// <summary>
    /// Send a <see cref="Opcode.Calculate"/> and await the matching
    /// <see cref="Opcode.Result"/>. On timeout the worker is killed and a
    /// <see cref="TimeoutException"/> is thrown — do not retry a timed-out
    /// host; spin up a fresh worker from the cached assembly bytes if the
    /// indicator needs to recover.
    /// </summary>
    public async Task<double[][]> CalculateAsync(
        CalculateRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var (_, resp) = await RoundTripAsync(
            Opcode.Calculate, MessageCodec.EncodeCalculateRequest(request),
            Opcode.Result, "Calculate", timeout ?? DefaultCalculateTimeout, ct).ConfigureAwait(false);
        return MessageCodec.DecodeCalculateResponse(resp).ComponentData;
    }

    // ── Strategy frames ─────────────────────────────────────────────────────
    // One method per ITradingStrategy member, each a single round trip. The
    // proxy (OutOfProcessStrategy) is what turns them back into the synchronous
    // interface the engine and the backtester call.

    /// <summary>
    /// Construct a fresh strategy instance in the worker and call <c>Initialize</c> on it. Every
    /// call starts the strategy over — see <see cref="Opcode.InitializeStrategy"/>.
    /// </summary>
    public async Task InitializeStrategyAsync(
        InitializeStrategyRequest request, TimeSpan? timeout = null, CancellationToken ct = default) =>
        await RoundTripAsync(
            Opcode.InitializeStrategy, StrategyCodec.EncodeInitialize(request),
            Opcode.Ack, "Initialize", timeout ?? DefaultStrategyCallTimeout, ct).ConfigureAwait(false);

    /// <summary>One bar in, at most one order out.</summary>
    public async Task<StrategySignal?> OnBarAsync(
        OnBarRequest request, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var (_, resp) = await RoundTripAsync(
            Opcode.OnBar, StrategyCodec.EncodeOnBar(request),
            Opcode.Signal, "OnBar", timeout ?? DefaultStrategyCallTimeout, ct).ConfigureAwait(false);
        return StrategyCodec.DecodeSignal(resp).Signal;
    }

    public async Task OnOrderFilledAsync(
        OrderUpdate fill, TimeSpan? timeout = null, CancellationToken ct = default) =>
        await RoundTripAsync(
            Opcode.OrderFilled, StrategyCodec.EncodeOrderUpdate(fill),
            Opcode.Ack, "OnOrderFilled", timeout ?? DefaultStrategyCallTimeout, ct).ConfigureAwait(false);

    public async Task StopStrategyAsync(TimeSpan? timeout = null, CancellationToken ct = default) =>
        await RoundTripAsync(
            Opcode.StopStrategy, Array.Empty<byte>(),
            Opcode.Ack, "OnStop", timeout ?? DefaultStrategyCallTimeout, ct).ConfigureAwait(false);

    public async Task<StrategyMetrics> GetStrategyMetricsAsync(
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var (_, resp) = await RoundTripAsync(
            Opcode.GetMetrics, Array.Empty<byte>(),
            Opcode.Metrics, "GetMetrics", timeout ?? DefaultStrategyCallTimeout, ct).ConfigureAwait(false);
        return StrategyCodec.DecodeMetrics(resp);
    }

    /// <summary>
    /// Send one command frame and wait for the one response opcode it is allowed to produce.
    ///
    /// <para>
    /// Every host → worker call has the same failure surface — a timeout that must kill the
    /// worker, a quota kill whose only symptom at this layer is a broken pipe, an
    /// <see cref="Opcode.Error"/> carrying the worker's own words, and diagnostics that have to
    /// be drained rather than mistaken for the answer. Writing that seven times is how one of
    /// the seven ends up missing the kill on timeout and leaves a hung worker holding a
    /// concurrency slot for the rest of the session.
    /// </para>
    /// </summary>
    private async Task<(Opcode Opcode, byte[] Payload)> RoundTripAsync(
        Opcode send,
        byte[] payload,
        Opcode expected,
        string operation,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OutOfProcessScriptHost));
        if (!IsAlive)  throw new InvalidOperationException($"script worker {_scriptId} has exited (code {_proc.ExitCode}).");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        await _ioGate.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        try
        {
            await FrameCodec.WriteFrameAsync(_stdin, send, payload, timeoutCts.Token).ConfigureAwait(false);

            while (true)
            {
                var (opcode, resp) = await FrameCodec.ReadFrameAsync(_stdout, timeoutCts.Token).ConfigureAwait(false);
                if (opcode == expected) return (opcode, resp);
                switch (opcode)
                {
                    case Opcode.Error:
                        throw new InvalidOperationException(
                            $"script worker {_scriptId} {operation} error: {Encoding.UTF8.GetString(resp)}");
                    case Opcode.Diagnostic:
                        _logger?.LogDebug("[worker {Id}] {Msg}", _scriptId, Encoding.UTF8.GetString(resp));
                        continue;
                    default:
                        throw new InvalidDataException(
                            $"script worker {_scriptId} sent unexpected opcode 0x{(byte)opcode:X2} during {operation}");
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Timed-out worker is suspect — kill it so we don't keep
            // feeding frames to a process that won't answer.
            try { if (!_proc.HasExited) _proc.Kill(entireProcessTree: true); } catch { }
            RecordSecurityEvent(
                AccessibleTrader.Sdk.Services.SecurityEventKind.CalculateTimeout,
                $"{operation} exceeded {timeout.TotalSeconds:F0}s",
                new Dictionary<string, string> { ["timeoutSeconds"] = timeout.TotalSeconds.ToString("F0") });
            throw new TimeoutException(
                $"script worker {_scriptId} {operation} exceeded {timeout.TotalSeconds:F0}s — worker killed");
        }
        catch (Exception) when (_killedForMemory)
        {
            // The memory monitor killed the worker mid-call; the IO pipe
            // break surfaces as IOException / EndOfStreamException. Re-throw
            // with a specific message so the user sees the real cause.
            throw new InvalidOperationException(
                $"script worker {_scriptId} killed — working set {_memoryOvershoot:N0} bytes exceeded quota {_maxWorkingSet:N0} bytes");
        }
        catch (Exception) when (_killedForCpu)
        {
            // The CPU-quota monitor killed the worker mid-call. Same pipe-break
            // symptom as the memory case; translate back to a descriptive cause.
            throw new InvalidOperationException(
                $"script worker {_scriptId} killed — sustained CPU utilisation exceeded {_maxCpuFraction:P0} quota");
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private async Task PumpStderrAsync(StreamReader stderr)
    {
        try
        {
            string? line;
            while ((line = await stderr.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                _logger?.LogWarning("[worker {Id} stderr] {Line}", _scriptId, line);
            }
        }
        catch { /* stream closed — worker exited */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop the memory monitor first so it doesn't race the shutdown or
        // log a spurious kill after we've already taken the worker down.
        try { _memoryMonitor?.Dispose(); } catch { }

        if (!_proc.HasExited)
        {
            // Best-effort graceful shutdown.
            try
            {
                using var cts = new CancellationTokenSource(ShutdownGrace);
                await FrameCodec.WriteFrameAsync(_stdin, Opcode.Shutdown, Array.Empty<byte>(), cts.Token).ConfigureAwait(false);
                // Give the worker the grace window to exit cleanly.
                await Task.Run(() => _proc.WaitForExit((int)ShutdownGrace.TotalMilliseconds)).ConfigureAwait(false);
            }
            catch { /* pipe may already be closed */ }
            try { if (!_proc.HasExited) _proc.Kill(entireProcessTree: true); } catch { }
        }

        try { _proc.Dispose();   } catch { }
        try { _ioGate.Dispose(); } catch { }

        // Release the concurrency slot. Decrement via Interlocked so racing
        // StartAsync calls observe the slot availability atomically.
        Interlocked.Decrement(ref _activeWorkerCount);
    }
}
