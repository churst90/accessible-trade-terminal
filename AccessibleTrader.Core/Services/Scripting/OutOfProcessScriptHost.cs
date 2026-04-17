using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.ScriptSandbox;
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

    /// <summary>How often the memory quota is polled. 2 s keeps kill latency
    /// low without burning measurable CPU — <see cref="IScriptWorkerProcess.Refresh"/>
    /// is a single OS read.</summary>
    public static readonly TimeSpan MemoryPollInterval = TimeSpan.FromSeconds(2);

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

    private IndicatorMetadataMessage? _metadata;
    private bool _disposed;

    private OutOfProcessScriptHost(IScriptWorkerProcess proc, string scriptId, long maxWorkingSetBytes, ILogger? logger)
    {
        _proc    = proc;
        _stdin   = proc.StdinWrite;
        _stdout  = proc.StdoutRead;
        _logger  = logger;
        _scriptId = scriptId;
        _maxWorkingSet = maxWorkingSetBytes;

        // Stderr is for fatal worker diagnostics — pump to the host log
        // asynchronously so we don't block reading it and cause the worker
        // to stall on a full buffer.
        _ = PumpStderrAsync(proc.StderrReader);

        // Memory quota: poll WorkingSet64 on a background timer and kill
        // the worker if it blows past the configured ceiling. 0 or negative
        // disables the quota (primarily for tests that need long-running
        // workers at debug loads). The host's wall-clock timeout still
        // applies. Implementations that can't report a working-set (some
        // Android paths) return 0 from WorkingSet64 — treat that as "no
        // data" and skip the check that tick.
        if (_maxWorkingSet > 0)
        {
            _memoryMonitor = new Timer(
                CheckMemoryQuota,
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

    public bool IsAlive => !_disposed && !_proc.HasExited;

    public static async Task<OutOfProcessScriptHost> StartAsync(
        IScriptWorkerLauncher launcher,
        string workerExecutablePath,
        byte[] assemblyBytes,
        string scriptId,
        TimeSpan? startTimeout = null,
        long? maxWorkingSetBytes = null,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var proc = launcher.Launch(workerExecutablePath);
        var host = new OutOfProcessScriptHost(proc, scriptId, maxWorkingSetBytes ?? DefaultMaxWorkingSetBytes, logger);
        try
        {
            await host.LoadAssemblyAsync(assemblyBytes, startTimeout ?? DefaultStartTimeout, ct).ConfigureAwait(false);
            return host;
        }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void CheckMemoryQuota(object? state)
    {
        if (_disposed) return;
        try
        {
            if (_proc.HasExited) return;
            _proc.Refresh();
            var ws = _proc.WorkingSet64;
            // 0 means "the platform doesn't expose a working-set number
            // through this transport" — skip rather than false-positive.
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
        if (_disposed) throw new ObjectDisposedException(nameof(OutOfProcessScriptHost));
        if (!IsAlive)  throw new InvalidOperationException($"script worker {_scriptId} has exited (code {_proc.ExitCode}).");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? DefaultCalculateTimeout);

        await _ioGate.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        try
        {
            var payload = MessageCodec.EncodeCalculateRequest(request);
            await FrameCodec.WriteFrameAsync(_stdin, Opcode.Calculate, payload, timeoutCts.Token).ConfigureAwait(false);

            while (true)
            {
                var (opcode, resp) = await FrameCodec.ReadFrameAsync(_stdout, timeoutCts.Token).ConfigureAwait(false);
                switch (opcode)
                {
                    case Opcode.Result:
                        return MessageCodec.DecodeCalculateResponse(resp).ComponentData;
                    case Opcode.Error:
                        throw new InvalidOperationException(
                            $"script worker {_scriptId} Calculate error: {Encoding.UTF8.GetString(resp)}");
                    case Opcode.Diagnostic:
                        _logger?.LogDebug("[worker {Id}] {Msg}", _scriptId, Encoding.UTF8.GetString(resp));
                        continue;
                    default:
                        throw new InvalidDataException(
                            $"script worker {_scriptId} sent unexpected opcode 0x{(byte)opcode:X2} during Calculate");
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Timed-out worker is suspect — kill it so we don't keep
            // feeding frames to a process that won't answer.
            try { if (!_proc.HasExited) _proc.Kill(entireProcessTree: true); } catch { }
            var budget = timeout ?? DefaultCalculateTimeout;
            RecordSecurityEvent(
                AccessibleTrader.Sdk.Services.SecurityEventKind.CalculateTimeout,
                $"Calculate exceeded {budget.TotalSeconds:F0}s",
                new Dictionary<string, string> { ["timeoutSeconds"] = budget.TotalSeconds.ToString("F0") });
            throw new TimeoutException(
                $"script worker {_scriptId} Calculate exceeded {budget.TotalSeconds:F0}s — worker killed");
        }
        catch (Exception) when (_killedForMemory)
        {
            // The memory monitor killed the worker mid-call; the IO pipe
            // break surfaces as IOException / EndOfStreamException. Re-throw
            // with a specific message so the user sees the real cause.
            throw new InvalidOperationException(
                $"script worker {_scriptId} killed — working set {_memoryOvershoot:N0} bytes exceeded quota {_maxWorkingSet:N0} bytes");
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
    }
}
