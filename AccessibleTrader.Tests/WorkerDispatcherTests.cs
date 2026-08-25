using System.Text;
using AccessibleTrader.ScriptSandbox;
using AccessibleTrader.Sdk.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// Protocol tests for <see cref="WorkerDispatcher"/> — the loop that runs
/// *inside* the sandboxed process, loads a user-compiled assembly into a
/// collectible ALC, and answers the host over the frame protocol. The desktop
/// console worker and the Android <c>isolatedProcess</c> service are both thin
/// transport adapters over this one class, so a protocol regression here breaks
/// custom indicators on every platform at once.
///
/// <para>
/// The dispatcher is transport-agnostic by design, which makes it directly
/// testable: hand it two <see cref="MemoryStream"/>s instead of stdin/stdout,
/// pre-load the input with a script of frames, and read the answers back. No
/// process is spawned — <c>OutOfProcessScriptingTests</c> covers the real
/// end-to-end spawn, and <c>HostileScriptTests</c> covers compile-time rejection;
/// what was missing was anything at all covering the dispatch decisions between
/// those two layers.
/// </para>
///
/// <para>
/// The central property under test is that a *bad frame is not a fatal frame*.
/// Every malformed input below must produce an <see cref="Opcode.Error"/> frame
/// and leave the worker still serving, because the host's only alternative is a
/// hung read against a process it then has to kill on timeout. Each such test
/// therefore ends with a Shutdown and asserts the "shutdown received" diagnostic
/// came back — that assertion, not the Error frame, is what proves the loop
/// survived.
/// </para>
///
/// <para>
/// Output frames are parsed with <see cref="FrameCodec"/> itself. That is a
/// deliberate dependency: <c>FrameCodecTests</c> pins the wire format against
/// raw bytes, so this file can take the codec as given and test dispatch.
/// </para>
/// </summary>
public class WorkerDispatcherTests
{
    // ── Fixture indicators (compiled to real assemblies at test time) ──

    /// <summary>
    /// Echoes Close and Volume as two components, and can be told to misbehave
    /// through the parameter dictionary so a single loaded assembly can drive
    /// the success, throw, and null-return paths in one dispatcher session.
    /// </summary>
    private const string EchoIndicatorSource = """
        using System;
        using System.Collections.Generic;
        using AccessibleTrader.Sdk.Interfaces;
        using AccessibleTrader.Sdk.Models;

        public sealed class EchoIndicator : ICustomIndicator
        {
            public string Id => "ECHO";
            public string DisplayName => "Echo Close";
            public string[] ComponentNames => new[] { "Close", "Volume" };
            public ComponentDisplayType[] DisplayTypes =>
                new[] { ComponentDisplayType.Line, ComponentDisplayType.Histogram };
            public Dictionary<string, double> DefaultParameters =>
                new Dictionary<string, double> { { "length", 14 }, { "scale", 2.5 } };

            public double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                if (parameters.ContainsKey("boom")) throw new InvalidOperationException("indicator blew up");
                if (parameters.ContainsKey("returnNull")) return null;

                double scale = parameters.TryGetValue("scale", out var s) ? s : 1.0;
                var closes = new double[data.Length];
                var volumes = new double[data.Length];
                for (int i = 0; i < data.Length; i++)
                {
                    closes[i] = data[i].Close * scale;
                    volumes[i] = data[i].Volume;
                }
                return new[] { closes, volumes };
            }
        }
        """;

    /// <summary>A valid assembly that simply contains no indicator.</summary>
    private const string NoIndicatorSource = """
        public sealed class NotAnIndicator
        {
            public int Answer => 42;
        }
        """;

    // ── Startup and shutdown ───────────────────────────────────────────

    /// <summary>
    /// The "worker ready" diagnostic is emitted before the first read, not after
    /// the first frame is served. The host logs it as proof the child process got
    /// as far as running managed code; if it moved below the read, a worker that
    /// never receives a frame would look identical to one that failed to start.
    ///
    /// <para>
    /// Also pins the documented contract that an input pipe closing without a
    /// Shutdown propagates <see cref="EndOfStreamException"/> to the caller —
    /// <c>ScriptWorker.Program</c> and the Android service both catch it as a
    /// clean exit.
    /// </para>
    /// </summary>
    [Fact]
    public async Task RunAsync_EmitsReadyDiagnostic_BeforeReadingAnyFrame()
    {
        var output = new MemoryStream();
        var dispatcher = new WorkerDispatcher(new MemoryStream(Array.Empty<byte>()), output);

        await Assert.ThrowsAsync<EndOfStreamException>(() => dispatcher.RunAsync(CancellationToken.None));

        var frames = await ReadFramesAsync(output);
        var only = Assert.Single(frames);
        Assert.Equal(Opcode.Diagnostic, only.opcode);
        Assert.Equal("worker ready", Text(only.payload));
    }

    [Fact]
    public async Task RunAsync_Shutdown_AcknowledgesAndReturnsNormally()
    {
        var frames = await RunScriptAsync((Opcode.Shutdown, Array.Empty<byte>()));

        Assert.Equal(new[] { Opcode.Diagnostic, Opcode.Diagnostic }, frames.Select(f => f.opcode));
        Assert.Equal("worker ready", Text(frames[0].payload));
        Assert.Equal("shutdown received", Text(frames[1].payload));
    }

    /// <summary>
    /// Shutdown ends the loop; frames queued behind it are not served. The host
    /// sends Shutdown and then kills the process after a grace window, so a
    /// dispatcher that kept draining would be racing its own SIGKILL.
    /// </summary>
    [Fact]
    public async Task RunAsync_StopsAtShutdown_WithoutServingLaterFrames()
    {
        var frames = await RunScriptAsync(
            (Opcode.Shutdown, Array.Empty<byte>()),
            (Opcode.Calculate, Array.Empty<byte>()));

        Assert.DoesNotContain(frames, f => f.opcode == Opcode.Error);
        Assert.Equal(2, frames.Count);
    }

    // ── The unvalidated opcode byte ────────────────────────────────────

    /// <summary>
    /// <c>FrameCodec</c> casts <c>header[4]</c> straight into the
    /// <see cref="Opcode"/> enum with no validation, so *any* of the 256 byte
    /// values reaches this switch — including the 250 that name nothing. This is
    /// the test that makes that safe: an undefined opcode is reported and the
    /// worker keeps serving, rather than falling through to a handler or killing
    /// the loop.
    ///
    /// <para>
    /// The cases cover the three interesting regions: 0x00 (the default(Opcode)
    /// value a zeroed buffer produces), an arbitrary unused command value, and
    /// 0x85 — inside the 0x80 "worker → host" response range, which the worker
    /// must never be willing to accept as a command.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x42)]
    [InlineData(0x85)]
    public async Task RunAsync_UndefinedOpcode_ReportsErrorAndKeepsServing(byte rawOpcode)
    {
        var frames = await RunScriptAsync(
            ((Opcode)rawOpcode, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }),
            (Opcode.Shutdown, Array.Empty<byte>()));

        var error = Assert.Single(frames, f => f.opcode == Opcode.Error);
        Assert.Equal($"unrecognised opcode 0x{rawOpcode:X2}", Text(error.payload));
        Assert.Contains(frames, f => f.opcode == Opcode.Diagnostic && Text(f.payload) == "shutdown received");
    }

    // ── LoadAssembly ───────────────────────────────────────────────────

    /// <summary>
    /// The metadata the host caches for the worker's whole lifetime. Two
    /// components with *different* display types, so the
    /// <c>DisplayTypes.Select(d =&gt; (int)d)</c> projection is proven to preserve
    /// both value and order — a reversed or truncated projection would silently
    /// render an indicator's histogram as a line.
    /// </summary>
    [Fact]
    public async Task RunAsync_LoadAssembly_RepliesReadyWithIndicatorMetadata()
    {
        var frames = await RunScriptAsync(
            (Opcode.LoadAssembly, Compile(EchoIndicatorSource, "EchoAsm")),
            (Opcode.Shutdown, Array.Empty<byte>()));

        var ready = Assert.Single(frames, f => f.opcode == Opcode.Ready);
        var meta = MessageCodec.DecodeMetadata(ready.payload);

        Assert.Equal("ECHO", meta.Id);
        Assert.Equal("Echo Close", meta.DisplayName);
        Assert.Equal(new[] { "Close", "Volume" }, meta.ComponentNames);
        Assert.Equal(
            new[] { (int)ComponentDisplayType.Line, (int)ComponentDisplayType.Histogram },
            meta.DisplayTypeValues);
        Assert.Equal(14d, meta.DefaultParameters["length"]);
        Assert.Equal(2.5d, meta.DefaultParameters["scale"]);
    }

    /// <summary>
    /// Bytes that are not a PE image at all — the shape of a corrupted or
    /// truncated transfer. <c>LoadFromStream</c> throws
    /// <c>BadImageFormatException</c>; the dispatch boundary has to turn that
    /// into an Error frame, because an unhandled throw here takes down a process
    /// the host is still waiting on.
    /// </summary>
    [Fact]
    public async Task RunAsync_LoadAssemblyWithNonAssemblyBytes_ReportsErrorAndKeepsServing()
    {
        var frames = await RunScriptAsync(
            (Opcode.LoadAssembly, Encoding.UTF8.GetBytes("this is not a PE file")),
            (Opcode.Shutdown, Array.Empty<byte>()));

        var error = Assert.Single(frames, f => f.opcode == Opcode.Error);
        Assert.StartsWith("LoadAssembly failed:", Text(error.payload));
        Assert.DoesNotContain(frames, f => f.opcode == Opcode.Ready);
        Assert.Contains(frames, f => f.opcode == Opcode.Diagnostic && Text(f.payload) == "shutdown received");
    }

    /// <summary>
    /// A perfectly valid assembly that implements nothing. The user sees this
    /// when their script compiles but the class implements neither of the two
    /// interfaces a script can be, so the message has to name both — a script
    /// author who meant to write a strategy and got told only about
    /// <c>ICustomIndicator</c> is being pointed at the wrong mistake.
    /// </summary>
    [Fact]
    public async Task RunAsync_LoadAssemblyWithNoIndicatorType_ReportsError()
    {
        var frames = await RunScriptAsync(
            (Opcode.LoadAssembly, Compile(NoIndicatorSource, "EmptyAsm")),
            (Opcode.Shutdown, Array.Empty<byte>()));

        var error = Assert.Single(frames, f => f.opcode == Opcode.Error);
        Assert.Contains("no ICustomIndicator or ITradingStrategy implementation found", Text(error.payload));
        Assert.DoesNotContain(frames, f => f.opcode == Opcode.Ready);
        Assert.DoesNotContain(frames, f => f.opcode == Opcode.StrategyReady);
    }

    /// <summary>
    /// One assembly per worker lifetime, whether or not the first load worked.
    /// The invariant that matters is a *second <see cref="Opcode.Ready"/> is
    /// never sent*: the host caches metadata off the first Ready and keys its
    /// component arrays to it, so a second one would silently reinterpret every
    /// subsequent Result against the wrong component list.
    ///
    /// <para>
    /// Note for whoever reads the refusal message next: after a *failed* load the
    /// ALC field is already set, so the retry is refused as "already handled"
    /// rather than re-attempted. That is currently harmless — the host tears the
    /// whole process down on a load failure and never retries in-place — but the
    /// message is misleading if that ever changes.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_SecondLoadAssembly_IsRefused(bool firstLoadIsValid)
    {
        var firstPayload = firstLoadIsValid
            ? Compile(EchoIndicatorSource, "EchoAsm")
            : Encoding.UTF8.GetBytes("not a PE file");

        var frames = await RunScriptAsync(
            (Opcode.LoadAssembly, firstPayload),
            (Opcode.LoadAssembly, Compile(EchoIndicatorSource, "SecondAsm")),
            (Opcode.Shutdown, Array.Empty<byte>()));

        Assert.True(frames.Count(f => f.opcode == Opcode.Ready) <= 1,
            "a worker must never publish two sets of indicator metadata");
        Assert.Contains(frames, f => f.opcode == Opcode.Error &&
                                     Text(f.payload).Contains("single-use"));
        Assert.Contains(frames, f => f.opcode == Opcode.Diagnostic && Text(f.payload) == "shutdown received");
    }

    // ── Calculate ──────────────────────────────────────────────────────

    /// <summary>
    /// The hot path: bars and parameters in, one array per component out. The
    /// scale parameter is deliberately not the indicator's default (2.5 is the
    /// default; the request sends 3), so a dispatcher that dropped the decoded
    /// parameter dictionary and let the indicator fall back to its own defaults
    /// would produce different numbers and fail here.
    /// </summary>
    [Fact]
    public async Task RunAsync_Calculate_ReturnsOneComponentArrayPerComponent()
    {
        var bars = SampleBars();
        var request = MessageCodec.EncodeCalculateRequest(
            new CalculateRequest(bars, new Dictionary<string, double> { ["scale"] = 3.0 }));

        var frames = await RunScriptAsync(
            (Opcode.LoadAssembly, Compile(EchoIndicatorSource, "EchoAsm")),
            (Opcode.Calculate, request),
            (Opcode.Shutdown, Array.Empty<byte>()));

        var result = Assert.Single(frames, f => f.opcode == Opcode.Result);
        var data = MessageCodec.DecodeCalculateResponse(result.payload).ComponentData;

        Assert.Equal(2, data.Length);
        Assert.Equal(bars.Select(b => b.Close * 3.0).ToArray(), data[0]);
        Assert.Equal(bars.Select(b => b.Volume).ToArray(), data[1]);
    }

    /// <summary>
    /// A Calculate with no assembly loaded is a host bug or a replayed frame, not
    /// a reason to crash the worker — and the null check is the only thing
    /// between it and a <see cref="NullReferenceException"/> outside the try
    /// block, which would take the process down without any Error frame at all.
    /// </summary>
    [Fact]
    public async Task RunAsync_CalculateBeforeLoadAssembly_ReportsErrorAndEmitsNoResult()
    {
        var request = MessageCodec.EncodeCalculateRequest(
            new CalculateRequest(SampleBars(), new Dictionary<string, double>()));

        var frames = await RunScriptAsync(
            (Opcode.Calculate, request),
            (Opcode.Shutdown, Array.Empty<byte>()));

        var error = Assert.Single(frames, f => f.opcode == Opcode.Error);
        Assert.Contains("Calculate received before a successful LoadAssembly", Text(error.payload));
        Assert.DoesNotContain(frames, f => f.opcode == Opcode.Result);
        Assert.Contains(frames, f => f.opcode == Opcode.Diagnostic && Text(f.payload) == "shutdown received");
    }

    /// <summary>
    /// An exception thrown by user code is the expected case, not the exceptional
    /// one — the whole point of the out-of-process design is that a user script
    /// can be wrong without consequence. This asserts the strong form: the worker
    /// serves a *successful* Calculate after a throwing one, on the same loaded
    /// assembly. Asserting only that an Error frame came back would leave a
    /// dispatcher that poisons itself after the first exception looking green.
    /// </summary>
    [Fact]
    public async Task RunAsync_IndicatorThrows_ReportsErrorAndStillServesTheNextCalculate()
    {
        var bars = SampleBars();
        var boom = MessageCodec.EncodeCalculateRequest(
            new CalculateRequest(bars, new Dictionary<string, double> { ["boom"] = 1 }));
        var ok = MessageCodec.EncodeCalculateRequest(
            new CalculateRequest(bars, new Dictionary<string, double> { ["scale"] = 1.0 }));

        var frames = await RunScriptAsync(
            (Opcode.LoadAssembly, Compile(EchoIndicatorSource, "EchoAsm")),
            (Opcode.Calculate, boom),
            (Opcode.Calculate, ok),
            (Opcode.Shutdown, Array.Empty<byte>()));

        var error = Assert.Single(frames, f => f.opcode == Opcode.Error);
        Assert.StartsWith("Calculate threw:", Text(error.payload));
        Assert.Contains("indicator blew up", Text(error.payload));

        var result = Assert.Single(frames, f => f.opcode == Opcode.Result);
        var data = MessageCodec.DecodeCalculateResponse(result.payload).ComponentData;
        Assert.Equal(bars.Select(b => b.Close).ToArray(), data[0]);
    }

    /// <summary>
    /// <c>null</c> from Calculate is separately handled because it is not an
    /// exception: without the explicit check it reaches
    /// <c>EncodeCalculateResponse</c> and dereferences there, one frame later and
    /// with a message that points at the codec instead of the script.
    /// </summary>
    [Fact]
    public async Task RunAsync_IndicatorReturnsNull_ReportsErrorAndKeepsServing()
    {
        var request = MessageCodec.EncodeCalculateRequest(
            new CalculateRequest(SampleBars(), new Dictionary<string, double> { ["returnNull"] = 1 }));

        var frames = await RunScriptAsync(
            (Opcode.LoadAssembly, Compile(EchoIndicatorSource, "EchoAsm")),
            (Opcode.Calculate, request),
            (Opcode.Shutdown, Array.Empty<byte>()));

        var error = Assert.Single(frames, f => f.opcode == Opcode.Error);
        Assert.Contains("indicator returned null", Text(error.payload));
        Assert.DoesNotContain(frames, f => f.opcode == Opcode.Result);
        Assert.Contains(frames, f => f.opcode == Opcode.Diagnostic && Text(f.payload) == "shutdown received");
    }

    /// <summary>
    /// A well-framed Calculate whose *payload* is garbage. On the worker side
    /// this is a corrupted host, but the same decode runs on the host side
    /// against payloads a hostile worker wrote, so the property being pinned is
    /// that a <c>MessageCodec</c> decode failure is caught at the dispatch
    /// boundary rather than escaping the switch.
    /// </summary>
    [Fact]
    public async Task RunAsync_MalformedCalculatePayload_ReportsErrorAndKeepsServing()
    {
        // Claims five bars, supplies none — trips the ByteReader bounds check.
        var truncated = new byte[] { 0x00, 0x00, 0x00, 0x05 };

        var frames = await RunScriptAsync(
            (Opcode.LoadAssembly, Compile(EchoIndicatorSource, "EchoAsm")),
            (Opcode.Calculate, truncated),
            (Opcode.Shutdown, Array.Empty<byte>()));

        var error = Assert.Single(frames, f => f.opcode == Opcode.Error);
        Assert.StartsWith("Calculate threw:", Text(error.payload));
        Assert.DoesNotContain(frames, f => f.opcode == Opcode.Result);
        Assert.Contains(frames, f => f.opcode == Opcode.Diagnostic && Text(f.payload) == "shutdown received");
    }

    // ── Lifecycle ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_RejectsNullStreams()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkerDispatcher(null!, new MemoryStream()));
        Assert.Throws<ArgumentNullException>(() => new WorkerDispatcher(new MemoryStream(), null!));
    }

    /// <summary>
    /// A cancelled token stops the worker instead of draining whatever is already
    /// queued in the pipe. The host cancels when it has decided to kill the
    /// process — anything served after that point is work nobody will read, on
    /// behalf of a script the host has already given up on.
    /// </summary>
    [Fact]
    public async Task RunAsync_CancelledToken_ServesNothing()
    {
        var input = await ScriptAsync(
            (Opcode.LoadAssembly, Compile(EchoIndicatorSource, "EchoAsm")),
            (Opcode.Shutdown, Array.Empty<byte>()));
        var output = new MemoryStream();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new WorkerDispatcher(input, output).RunAsync(cts.Token));

        Assert.Equal(0, input.Position);   // not one frame was consumed
        Assert.Empty(await ReadFramesAsync(output));
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static Ohlcv[] SampleBars() => new[]
    {
        new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100, 101, 99, 100.5, 10),
        new Ohlcv(new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc), 100.5, 102, 100, 101.25, 20),
        new Ohlcv(new DateTime(2026, 1, 1, 0, 2, 0, DateTimeKind.Utc), 101.25, 103, 101, 102.75, 30),
    };

    private static string Text(byte[] payload) => Encoding.UTF8.GetString(payload);

    /// <summary>Pre-load an input stream with a script of frames for the dispatcher to serve.</summary>
    private static async Task<MemoryStream> ScriptAsync(params (Opcode opcode, byte[] payload)[] frames)
    {
        var ms = new MemoryStream();
        foreach (var (opcode, payload) in frames)
            await FrameCodec.WriteFrameAsync(ms, opcode, payload);
        ms.Position = 0;
        return ms;
    }

    private static async Task<List<(Opcode opcode, byte[] payload)>> ReadFramesAsync(MemoryStream output)
    {
        output.Position = 0;
        var frames = new List<(Opcode, byte[])>();
        while (output.Position < output.Length)
            frames.Add(await FrameCodec.ReadFrameAsync(output));
        return frames;
    }

    /// <summary>
    /// Run a whole dispatcher session over in-memory pipes and return everything
    /// it wrote. Scripts are expected to end in Shutdown; a script that does not
    /// will surface the <see cref="EndOfStreamException"/> to the caller, which
    /// is the documented behaviour when a pipe closes unexpectedly.
    /// </summary>
    private static async Task<List<(Opcode opcode, byte[] payload)>> RunScriptAsync(
        params (Opcode opcode, byte[] payload)[] frames)
    {
        var input = await ScriptAsync(frames);
        var output = new MemoryStream();
        await new WorkerDispatcher(input, output).RunAsync(CancellationToken.None);
        return await ReadFramesAsync(output);
    }

    /// <summary>
    /// Compile a fixture indicator to raw assembly bytes — exactly what the host
    /// puts in a <see cref="Opcode.LoadAssembly"/> payload. The reference set
    /// mirrors <c>RoslynScriptingService</c>'s so the fixtures compile under the
    /// same constraints real user scripts do; this file is testing dispatch, not
    /// the compiler's sandbox policy, which is <c>HostileScriptTests</c>' job.
    /// </summary>
    private static byte[] Compile(string source, string assemblyName)
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),                  // System.Private.CoreLib
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),              // System.Linq
            MetadataReference.CreateFromFile(typeof(Dictionary<,>).Assembly.Location),           // System.Collections
            MetadataReference.CreateFromFile(typeof(Sdk.Interfaces.ICustomIndicator).Assembly.Location),
        };
        // The Sdk's public surface is expressed in terms of the reference
        // facades (ReadOnlySpan, ValueType, Enum), so the facades have to be on
        // the reference list even though the implementations live in CoreLib.
        foreach (var facade in new[] { "System.Runtime.dll", "System.Collections.dll", "netstandard.dll" })
        {
            var path = Path.Combine(runtimeDir, facade);
            if (File.Exists(path)) references.Add(MetadataReference.CreateFromFile(path));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success,
            "fixture indicator failed to compile: " +
            string.Join(" | ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return ms.ToArray();
    }
}
