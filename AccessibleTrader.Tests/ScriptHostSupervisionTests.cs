using System.Diagnostics;
using System.Threading.Channels;
using AccessibleTrader.Core.Services.Scripting;
using AccessibleTrader.ScriptSandbox;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// What <see cref="OutOfProcessScriptHost"/> does to a worker that misbehaves — the supervision
/// half of the script sandbox, driven through a FAKE worker so each rule is exercised exactly and
/// deterministically, with no process, no bwrap and no machine load in the result.
///
/// <para>
/// <b>Why this file exists (A2n, 2026-09-24).</b> <c>Core/Services/Scripting</c> had never been
/// mutated. Against the full suite, removing the per-call deadline, the kill on timeout, the
/// memory quota, the CPU quota, the concurrent-worker cap, the slot release on dispose and the
/// refusal of an out-of-protocol reply ALL survived: every existing test of the host drives a
/// well-behaved script, so nothing had ever asked the supervisor to supervise. These are the
/// rules that stand between a hostile or broken custom indicator and the user's machine.
/// </para>
///
/// <para>
/// Serialised with the other worker classes because the concurrency cap is a process-wide static.
/// </para>
/// </summary>
[Collection("ScriptWorker")]
public class ScriptHostSupervisionTests
{
    // ── The fake transport ────────────────────────────────────────────────────

    /// <summary>One-directional in-memory byte pipe whose reads honour cancellation and end (0)
    /// when the writer is completed — the two properties a real child-process pipe has that the
    /// supervisor depends on.</summary>
    private sealed class PipeStream : Stream
    {
        private readonly Channel<byte[]> _ch = Channel.CreateUnbounded<byte[]>();
        private byte[] _cur = Array.Empty<byte>();
        private int _pos;

        public void Complete() => _ch.Writer.TryComplete();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            while (_pos >= _cur.Length)
            {
                if (!await _ch.Reader.WaitToReadAsync(ct).ConfigureAwait(false)) return 0;
                if (_ch.Reader.TryRead(out var next)) { _cur = next; _pos = 0; }
            }
            int n = Math.Min(buffer.Length, _cur.Length - _pos);
            _cur.AsMemory(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (!_ch.Writer.TryWrite(buffer.AsSpan(offset, count).ToArray()))
                throw new IOException("pipe closed");
            return Task.CompletedTask;
        }

        public override void Write(byte[] buffer, int offset, int count)
            => WriteAsync(buffer, offset, count, default).GetAwaiter().GetResult();
        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count, default).GetAwaiter().GetResult();
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override void Flush() { }
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
    }

    private enum OnCalculate { Answer, Hang, AnswerWithAWrongOpcode }

    /// <summary>
    /// A worker that completes the handshake honestly and then does whatever the test asks with a
    /// Calculate. Reports whatever working set and CPU time the test sets.
    /// </summary>
    private sealed class FakeWorker : IScriptWorkerProcess
    {
        private readonly PipeStream _toWorker = new();
        private readonly PipeStream _toHost = new();
        private readonly Stopwatch _alive = Stopwatch.StartNew();
        private readonly OnCalculate _behaviour;
        private volatile bool _exited;

        public int KillCalls;
        public long ReportedWorkingSet = 10 * 1024 * 1024;
        /// <summary>CPU seconds charged per wall-clock second (2.0 = two cores pegged).</summary>
        public double CpuPerWallSecond;

        public FakeWorker(OnCalculate behaviour)
        {
            _behaviour = behaviour;
            _ = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            try
            {
                while (true)
                {
                    var (op, _) = await FrameCodec.ReadFrameAsync(_toWorker).ConfigureAwait(false);
                    switch (op)
                    {
                        case Opcode.LoadAssembly:
                            await FrameCodec.WriteFrameAsync(_toHost, Opcode.Ready, MessageCodec.EncodeMetadata(
                                new IndicatorMetadataMessage("FAKE", "fake", new[] { "x" }, new[] { 0 },
                                    new Dictionary<string, double>(), new[] { 0 }))).ConfigureAwait(false);
                            break;
                        case Opcode.Calculate when _behaviour == OnCalculate.Answer:
                            await FrameCodec.WriteFrameAsync(_toHost, Opcode.Result, MessageCodec.EncodeCalculateResponse(
                                new CalculateResponse(new[] { new[] { 42.0 } }))).ConfigureAwait(false);
                            break;
                        case Opcode.Calculate when _behaviour == OnCalculate.AnswerWithAWrongOpcode:
                            // A strategy's acknowledgement in answer to an indicator's Calculate.
                            await FrameCodec.WriteFrameAsync(_toHost, Opcode.Ack, Array.Empty<byte>()).ConfigureAwait(false);
                            break;
                        case Opcode.Calculate:
                            break;   // hang: never answer
                        case Opcode.Shutdown:
                            Exit();
                            return;
                    }
                }
            }
            catch { /* pipe closed */ }
        }

        private void Exit()
        {
            _exited = true;
            _toHost.Complete();
            _toWorker.Complete();
        }

        public Stream StdinWrite => _toWorker;
        public Stream StdoutRead => _toHost;
        public StreamReader StderrReader { get; } = new(new MemoryStream());
        public bool HasExited => _exited;
        public int ExitCode => _exited ? 137 : 0;
        public bool Kill(bool entireProcessTree) { Interlocked.Increment(ref KillCalls); Exit(); return true; }
        public bool WaitForExit(int milliseconds) => _exited;
        public void Refresh() { }
        public long WorkingSet64 => ReportedWorkingSet;
        public TimeSpan TotalProcessorTime =>
            TimeSpan.FromSeconds(1 + _alive.Elapsed.TotalSeconds * CpuPerWallSecond);
        public void Dispose() => Exit();
    }

    private sealed class FakeLauncher : IScriptWorkerLauncher
    {
        private readonly Func<FakeWorker> _make;
        public readonly List<FakeWorker> Launched = new();
        public FakeLauncher(Func<FakeWorker> make) => _make = make;
        public IScriptWorkerProcess Launch(string workerExecutablePath)
        {
            var w = _make();
            lock (Launched) Launched.Add(w);
            return w;
        }
    }

    private static Task<OutOfProcessScriptHost> Start(FakeLauncher launcher,
        long maxWorkingSet = 0, double maxCpu = 0) =>
        OutOfProcessScriptHost.StartAsync(launcher, "/fake/worker", new byte[] { 1 },
            scriptId: "fake-" + Guid.NewGuid().ToString("N")[..6],
            startTimeout: TimeSpan.FromSeconds(10),
            maxWorkingSetBytes: maxWorkingSet, maxCpuFraction: maxCpu);

    private static CalculateRequest Req() =>
        new(new[] { new Ohlcv(new DateTime(2026, 1, 1), 1, 2, 0.5, 1.5, 1) }, new Dictionary<string, double>());

    /// <summary>A mutant that removes a deadline must fail THIS test, not hang the suite.</summary>
    private static async Task<Exception> FailsWithin(Task task, TimeSpan budget)
    {
        var done = await Task.WhenAny(task, Task.Delay(budget));
        Assert.True(done == task, $"the call was still running after {budget.TotalSeconds:F0}s — nothing stopped it");
        return await Assert.ThrowsAnyAsync<Exception>(() => task);
    }

    // ── The control ───────────────────────────────────────────────────────────

    /// <summary>Vacuity check: the fake worker answers, and the supervisor lets it.</summary>
    [Fact]
    public async Task A_worker_that_answers_is_answered()
    {
        var launcher = new FakeLauncher(() => new FakeWorker(OnCalculate.Answer));
        await using var host = await Start(launcher, maxWorkingSet: 256L * 1024 * 1024, maxCpu: 0.9);

        var result = await host.CalculateAsync(Req(), TimeSpan.FromSeconds(5));
        Assert.Equal(42.0, result[0][0]);
        Assert.Equal(0, launcher.Launched[0].KillCalls);
    }

    // ── Deadlines ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A script that never returns gets a <see cref="TimeoutException"/> at its deadline, and its
    /// worker is KILLED — a hung worker left alive keeps a concurrency slot and whatever it is
    /// doing for the rest of the session.
    /// </summary>
    [Fact]
    public async Task A_hung_call_times_out_at_its_deadline_and_the_worker_is_killed()
    {
        var launcher = new FakeLauncher(() => new FakeWorker(OnCalculate.Hang));
        await using var host = await Start(launcher);

        var ex = await FailsWithin(host.CalculateAsync(Req(), TimeSpan.FromMilliseconds(300)), TimeSpan.FromSeconds(15));

        Assert.IsType<TimeoutException>(ex);
        Assert.True(launcher.Launched[0].KillCalls > 0, "the timed-out worker was left running");
        Assert.False(host.IsAlive);
    }

    /// <summary>
    /// The same rule through a REAL worker process and the shipped transport. The fake above
    /// proves the supervisor's logic; this proves the deadline can actually interrupt a read from
    /// a child process's stdout — if cancellation did not reach that read, no timeout in the
    /// supervisor could ever fire, however it was written.
    /// </summary>
    [Fact]
    public async Task A_real_worker_stuck_inside_a_script_is_killed_at_its_deadline()
    {
        var workerPath = ScriptWorkerPath.Resolve();
        Assert.True(File.Exists(workerPath), $"ScriptWorker executable not found at '{workerPath}'.");

        var host = await OutOfProcessScriptHost.StartAsync(
            new DefaultProcessLauncher(), workerPath, SleeperIndicator(),
            scriptId: "sleeper-" + Guid.NewGuid().ToString("N")[..6],
            maxCpuFraction: 0);  // a sleeping script uses no CPU; only the deadline can end this
        try
        {
            var ex = await FailsWithin(host.CalculateAsync(Req(), TimeSpan.FromSeconds(1)), TimeSpan.FromSeconds(30));
            Assert.IsType<TimeoutException>(ex);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (host.IsAlive && DateTime.UtcNow < deadline) await Task.Delay(50);
            Assert.False(host.IsAlive, "the stuck worker was left running after its deadline");
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    private static byte[] SleeperIndicator()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using AccessibleTrader.Sdk.Interfaces;
            using AccessibleTrader.Sdk.Models;

            public sealed class Sleeper : ICustomIndicator
            {
                public string Id => "SUPERVISION_SLEEPER";
                public string DisplayName => "sleeper";
                public string[] ComponentNames => new[] { "x" };
                public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
                public Dictionary<string, double> DefaultParameters => new();

                public double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
                {
                    System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
                    return new[] { new double[data.Length] };
                }
            }
            """;

        var references = new List<Microsoft.CodeAnalysis.MetadataReference>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.IsNullOrEmpty(asm.Location)) continue;
            var name = asm.GetName().Name ?? "";
            if (name.StartsWith("System.", StringComparison.Ordinal) || name == "netstandard"
                || name == "System.Private.CoreLib" || name == "AccessibleTrader.Sdk")
                references.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(asm.Location));
        }
        references.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        references.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(Ohlcv).Assembly.Location));

        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "SupervisionFixture_" + Guid.NewGuid().ToString("N"),
            new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source) },
            references,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, "fixture failed to compile: " + string.Join(" | ", emit.Diagnostics));
        return ms.ToArray();
    }

    // ── Protocol ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A reply the protocol does not allow for this call is refused on sight. Skipping it and
    /// reading on would let a worker feed the host frames out of step with its questions.
    /// </summary>
    [Fact]
    public async Task An_out_of_protocol_reply_is_refused_not_skipped()
    {
        var launcher = new FakeLauncher(() => new FakeWorker(OnCalculate.AnswerWithAWrongOpcode));
        await using var host = await Start(launcher);

        var ex = await FailsWithin(host.CalculateAsync(Req(), TimeSpan.FromSeconds(5)), TimeSpan.FromSeconds(15));

        Assert.IsType<InvalidDataException>(ex);
        Assert.Contains("unexpected opcode", ex.Message);
    }

    // ── Quotas ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A worker over its working-set quota is killed by the poll, and the call that was waiting
    /// on it says WHY rather than reporting a broken pipe or a timeout.
    /// </summary>
    [Fact]
    public async Task A_worker_over_its_memory_quota_is_killed_and_the_reason_is_given()
    {
        var launcher = new FakeLauncher(() => new FakeWorker(OnCalculate.Hang)
        {
            ReportedWorkingSet = 900L * 1024 * 1024,
        });
        await using var host = await Start(launcher, maxWorkingSet: 256L * 1024 * 1024);

        // The deadline is far beyond the 2 s poll: only the quota can end this call in time.
        var ex = await FailsWithin(host.CalculateAsync(Req(), TimeSpan.FromSeconds(30)), TimeSpan.FromSeconds(20));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("working set", ex.Message);
        Assert.True(launcher.Launched[0].KillCalls > 0);
    }

    /// <summary>Same rule for CPU: a worker pegging more than its share is killed and named.</summary>
    [Fact]
    public async Task A_worker_pegging_the_cpu_is_killed_and_the_reason_is_given()
    {
        var launcher = new FakeLauncher(() => new FakeWorker(OnCalculate.Hang) { CpuPerWallSecond = 2.0 });
        await using var host = await Start(launcher, maxCpu: 0.9);

        var ex = await FailsWithin(host.CalculateAsync(Req(), TimeSpan.FromSeconds(30)), TimeSpan.FromSeconds(20));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("CPU", ex.Message);
        Assert.True(launcher.Launched[0].KillCalls > 0);
    }

    /// <summary>
    /// The two quotas share one timer. Turning the CPU side off (a legitimate configuration — a
    /// heavy backfill) must not turn the memory side off with it.
    /// </summary>
    [Fact]
    public async Task The_memory_quota_still_polls_when_the_cpu_quota_is_off()
    {
        var launcher = new FakeLauncher(() => new FakeWorker(OnCalculate.Hang)
        {
            ReportedWorkingSet = 900L * 1024 * 1024,
        });
        await using var host = await Start(launcher, maxWorkingSet: 256L * 1024 * 1024, maxCpu: 0);

        var ex = await FailsWithin(host.CalculateAsync(Req(), TimeSpan.FromSeconds(30)), TimeSpan.FromSeconds(20));
        Assert.Contains("working set", ex.Message);
    }

    // ── The concurrency cap ───────────────────────────────────────────────────

    /// <summary>
    /// Past the cap, a new worker is refused BEFORE anything is launched; disposing a worker gives
    /// its slot back. Without the second half, sixteen compiles in a session disable scripting
    /// until restart; without the first, a hundred compiles spawn a hundred processes.
    /// </summary>
    [Fact]
    public async Task The_worker_cap_refuses_before_launching_and_a_disposed_worker_frees_its_slot()
    {
        int before = OutOfProcessScriptHost.ActiveWorkerCount;
        OutOfProcessScriptHost.SetMaxConcurrentWorkers(before + 1);
        try
        {
            var launcher = new FakeLauncher(() => new FakeWorker(OnCalculate.Answer));
            var first = await Start(launcher);
            Assert.Equal(before + 1, OutOfProcessScriptHost.ActiveWorkerCount);

            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Start(launcher));
            Assert.Contains("concurrent", refused.Message);
            Assert.Single(launcher.Launched);           // refused before a process existed
            Assert.Equal(before + 1, OutOfProcessScriptHost.ActiveWorkerCount);

            await first.DisposeAsync();
            Assert.Equal(before, OutOfProcessScriptHost.ActiveWorkerCount);

            // And the freed slot is usable.
            var second = await Start(launcher);
            await second.DisposeAsync();
            Assert.Equal(before, OutOfProcessScriptHost.ActiveWorkerCount);
        }
        finally
        {
            OutOfProcessScriptHost.SetMaxConcurrentWorkers(OutOfProcessScriptHost.DefaultMaxConcurrentWorkers);
        }
    }
}
