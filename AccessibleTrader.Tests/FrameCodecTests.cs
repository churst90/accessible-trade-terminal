using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.ScriptSandbox;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Wire-format tests for <see cref="FrameCodec"/> — the boundary that parses
/// bytes arriving from a process running untrusted, user-compiled code.
///
/// <para>
/// Everything on the host side of the script sandbox (<c>OutOfProcessScriptHost</c>)
/// trusts this codec to (a) reassemble a frame that arrived in pieces, (b) refuse
/// a length field that would trigger an unbounded allocation, and (c) never
/// over-read past a frame boundary. None of those three had a test before this
/// file; a hostile worker controls every byte the codec reads.
/// </para>
///
/// <para>
/// The header layout is asserted byte-for-byte rather than only round-tripped:
/// a round-trip test passes just as happily if both sides silently switched to
/// little-endian, which would break the Android worker (same protocol, separate
/// build) the moment either side was rebuilt and the other was not.
/// </para>
/// </summary>
public class FrameCodecTests
{
    // ── Header layout ──────────────────────────────────────────────────

    /// <summary>
    /// The documented layout is [u32 length BE][u8 opcode][payload], where
    /// <c>length</c> counts the opcode byte plus the payload. Asserted on the
    /// raw bytes so an endianness or an off-by-one in the length field is a
    /// test failure and not a silent interop break with the Android worker.
    /// </summary>
    [Fact]
    public async Task WriteFrame_EmitsBigEndianLength_ThenOpcode_ThenPayload()
    {
        var ms = new MemoryStream();
        await FrameCodec.WriteFrameAsync(ms, Opcode.Error, Encoding.UTF8.GetBytes("hi"));

        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x03, 0x83, (byte)'h', (byte)'i' }, ms.ToArray());
    }

    /// <summary>
    /// An opcode-only frame (Shutdown carries no payload) must still declare
    /// length 1, not length 0 — the read side rejects length 0 outright, so an
    /// encoder that counted only the payload would make Shutdown unsendable.
    /// </summary>
    [Fact]
    public async Task WriteFrame_EmptyPayload_DeclaresLengthOne()
    {
        var ms = new MemoryStream();
        await FrameCodec.WriteFrameAsync(ms, Opcode.Shutdown, Array.Empty<byte>());

        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x01, 0xFF }, ms.ToArray());
    }

    /// <summary>
    /// A length that spans more than one byte distinguishes big- from
    /// little-endian: 301 is 00 00 01 2D big-endian and 2D 01 00 00 little.
    /// The single-byte cases above cannot tell the two apart.
    /// </summary>
    [Fact]
    public async Task WriteFrame_MultiByteLength_IsBigEndian()
    {
        var ms = new MemoryStream();
        await FrameCodec.WriteFrameAsync(ms, Opcode.Result, new byte[300]);

        var header = ms.ToArray().AsSpan(0, 4).ToArray();
        Assert.Equal(new byte[] { 0x00, 0x00, 0x01, 0x2D }, header);
    }

    // ── Round trip ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(1024)]
    [InlineData(70_000)]     // > any single 64 KB pipe buffer
    public async Task RoundTrip_PreservesOpcodeAndPayload(int payloadLen)
    {
        var payload = Pattern(payloadLen);

        var ms = new MemoryStream();
        await FrameCodec.WriteFrameAsync(ms, Opcode.Calculate, payload);
        ms.Position = 0;

        var (opcode, read) = await FrameCodec.ReadFrameAsync(ms);

        Assert.Equal(Opcode.Calculate, opcode);
        Assert.Equal(payload, read);
    }

    /// <summary>
    /// The dispatch loop reads frame after frame off one long-lived pipe. If
    /// <c>ReadExactlyAsync</c> ever over-read (buffering ahead), frame N+1 would
    /// be consumed and lost while frame N was being parsed — the failure would
    /// look like a worker that answers the first Calculate and then hangs.
    /// </summary>
    [Fact]
    public async Task ReadFrame_ReadsSuccessiveFramesInOrder_WithoutOverConsuming()
    {
        var ms = new MemoryStream();
        await FrameCodec.WriteFrameAsync(ms, Opcode.Diagnostic, Encoding.UTF8.GetBytes("one"));
        await FrameCodec.WriteFrameAsync(ms, Opcode.Result, Pattern(64));
        await FrameCodec.WriteFrameAsync(ms, Opcode.Shutdown, Array.Empty<byte>());
        ms.Position = 0;

        var first  = await FrameCodec.ReadFrameAsync(ms);
        var second = await FrameCodec.ReadFrameAsync(ms);
        var third  = await FrameCodec.ReadFrameAsync(ms);

        Assert.Equal(Opcode.Diagnostic, first.opcode);
        Assert.Equal("one", Encoding.UTF8.GetString(first.payload));
        Assert.Equal(Opcode.Result, second.opcode);
        Assert.Equal(Pattern(64), second.payload);
        Assert.Equal(Opcode.Shutdown, third.opcode);
        Assert.Empty(third.payload);
        Assert.Equal(ms.Length, ms.Position);
    }

    // ── Partial reads ──────────────────────────────────────────────────

    /// <summary>
    /// A pipe read returns whatever is available, not what was asked for — a
    /// 70 KB frame crossing an OS pipe buffer arrives in several chunks. The
    /// <c>while (read &lt; count)</c> reassembly loop is the only thing standing
    /// between that and a garbled payload, and it never ran under test.
    ///
    /// <para>
    /// Driven at one byte per read, which is the pathological case for the loop
    /// and also exercises the header itself arriving in five separate reads.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ReadFrame_ReassemblesPayload_WhenStreamYieldsOneByteAtATime()
    {
        var payload = Pattern(1000);
        var buffer = new MemoryStream();
        await FrameCodec.WriteFrameAsync(buffer, Opcode.Result, payload);

        var drip = new ChunkedStream(buffer.ToArray(), maxBytesPerRead: 1);
        var (opcode, read) = await FrameCodec.ReadFrameAsync(drip);

        Assert.Equal(Opcode.Result, opcode);
        Assert.Equal(payload, read);
        Assert.True(drip.ReadCalls > payload.Length,
            $"expected the reassembly loop to make many small reads, saw {drip.ReadCalls}");
    }

    [Theory]
    [InlineData(3)]      // truncated inside the 5-byte header
    [InlineData(5)]      // header complete, payload absent
    [InlineData(7)]      // truncated inside the payload
    public async Task ReadFrame_Throws_WhenStreamClosesMidFrame(int availableBytes)
    {
        var buffer = new MemoryStream();
        await FrameCodec.WriteFrameAsync(buffer, Opcode.Result, Pattern(16));
        var truncated = new MemoryStream(buffer.ToArray().AsSpan(0, availableBytes).ToArray());

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => FrameCodec.ReadFrameAsync(truncated));
    }

    /// <summary>
    /// The end-of-stream message reports how far the frame got. That is the only
    /// diagnostic a user gets when a worker dies mid-frame, and it is also the
    /// cheapest available proof that <c>read += n</c> accumulates correctly
    /// rather than being overwritten each pass.
    /// </summary>
    [Fact]
    public async Task ReadFrame_EndOfStreamMessage_ReportsBytesReceivedOverBytesExpected()
    {
        var partialHeader = new ChunkedStream(new byte[] { 0x00, 0x00, 0x00 }, maxBytesPerRead: 1);

        var ex = await Assert.ThrowsAsync<EndOfStreamException>(
            () => FrameCodec.ReadFrameAsync(partialHeader));

        Assert.Contains("3/5", ex.Message);
    }

    // ── Length-field guards (the DoS boundary) ─────────────────────────

    /// <summary>
    /// Length 0 cannot be legal — every frame carries at least its opcode byte —
    /// and without the guard <c>payloadLen</c> would be -1 and reach
    /// <c>new byte[-1]</c>. Rejecting it here keeps the failure a clean
    /// <see cref="InvalidDataException"/> the host can report.
    /// </summary>
    [Fact]
    public async Task ReadFrame_Throws_WhenLengthIsZero()
    {
        var stream = new MemoryStream(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x82, 0xAA, 0xBB });

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => FrameCodec.ReadFrameAsync(stream));

        Assert.Contains("zero", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, stream.Position);   // header only; no payload was consumed
    }

    /// <summary>
    /// The allocation guard. A hostile worker writes a five-byte header claiming
    /// a gigantic frame and sends nothing else; without the cap check the host
    /// allocates that many bytes per frame, on repeat, from a process whose whole
    /// purpose is running code we do not trust.
    ///
    /// <para>
    /// The <c>0xFFFFFFFF</c> case matters separately: it is above the cap, but it
    /// is also the value where <c>(int)length - 1</c> aliases to -2. Whichever
    /// guard is removed, the exception type changes — that is what makes this a
    /// real guard test and not a restatement of the code.
    /// </para>
    ///
    /// <para>
    /// Both cases assert the stream was left at offset 5, i.e. the codec refused
    /// on the header alone and never went looking for the claimed payload.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(FrameCodec.MaxFrameBytes + 1u)]
    [InlineData(0x8000_0000u)]
    [InlineData(0xFFFF_FFFFu)]
    public async Task ReadFrame_Throws_WhenLengthExceedsCap_WithoutReadingPayload(uint claimedLength)
    {
        var header = new byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(header, claimedLength);
        header[4] = (byte)Opcode.Result;
        var stream = new MemoryStream(header);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => FrameCodec.ReadFrameAsync(stream));

        Assert.Contains(FrameCodec.MaxFrameBytes.ToString(), ex.Message);
        Assert.Equal(5, stream.Position);
    }

    /// <summary>
    /// The cap is inclusive on both sides and they must agree: the write side
    /// admits a payload of <c>MaxFrameBytes - 1</c> (declaring length exactly
    /// <c>MaxFrameBytes</c>), so the read side has to accept that length. An
    /// off-by-one on either comparison would make a legally-encoded maximum
    /// frame unreadable — reaching end-of-stream here proves the length passed
    /// the guard and the codec went on to read the payload.
    /// </summary>
    [Fact]
    public async Task ReadFrame_AcceptsLengthExactlyAtCap()
    {
        var header = new byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(header, FrameCodec.MaxFrameBytes);
        header[4] = (byte)Opcode.Result;

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => FrameCodec.ReadFrameAsync(new MemoryStream(header)));
    }

    /// <summary>
    /// Same cap from the writing side, and nothing is emitted before it trips —
    /// a partially-written oversized frame would desynchronise the pipe for
    /// every frame after it.
    /// </summary>
    [Fact]
    public async Task WriteFrame_Throws_WhenPayloadExceedsCap_AndEmitsNothing()
    {
        var sink = new MemoryStream();
        var oversized = new byte[FrameCodec.MaxFrameBytes];   // + 1 opcode byte puts it over

        await Assert.ThrowsAsync<InvalidDataException>(
            () => FrameCodec.WriteFrameAsync(sink, Opcode.Result, oversized));

        Assert.Equal(0, sink.Length);
    }

    // ── Opcode byte ────────────────────────────────────────────────────

    /// <summary>
    /// <c>(Opcode)header[4]</c> casts any byte into the enum with no validation —
    /// 0x42 is not a defined opcode and the codec hands it back anyway. That is
    /// deliberate layering, not an oversight: the codec's job is framing, and
    /// rejecting an opcode here would give the worker no way to report which one
    /// it was. The safety property lives one layer up, in the dispatcher's
    /// <c>default:</c> branch — see
    /// <see cref="WorkerDispatcherTests.RunAsync_UndefinedOpcode_ReportsErrorAndKeepsServing"/>,
    /// which is the test that must stay green if this contract ever moves.
    /// </summary>
    [Fact]
    public async Task ReadFrame_ReturnsUndefinedOpcodeByte_Unvalidated()
    {
        var stream = new MemoryStream(new byte[] { 0x00, 0x00, 0x00, 0x01, 0x42 });

        var (opcode, payload) = await FrameCodec.ReadFrameAsync(stream);

        Assert.False(Enum.IsDefined(typeof(Opcode), opcode));
        Assert.Equal(0x42, (byte)opcode);
        Assert.Empty(payload);
    }

    // ── Cancellation ───────────────────────────────────────────────────

    /// <summary>
    /// The host cancels a read when a Calculate exceeds its timeout budget and
    /// then kills the worker. If the reassembly loop did not thread the token
    /// into each <c>ReadAsync</c>, a worker that dribbles one byte at a time
    /// would hold the host's IO gate open past the timeout it is supposed to
    /// enforce. Cancelled mid-frame, after the loop has already begun.
    /// </summary>
    [Fact]
    public async Task ReadFrame_ObservesCancellation_MidFrame()
    {
        var buffer = new MemoryStream();
        await FrameCodec.WriteFrameAsync(buffer, Opcode.Result, Pattern(64));

        using var cts = new CancellationTokenSource();
        var stream = new ChunkedStream(buffer.ToArray(), maxBytesPerRead: 1, cancelAfterBytes: 8, cts: cts);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FrameCodec.ReadFrameAsync(stream, cts.Token));

        Assert.True(stream.BytesRead < buffer.Length,
            "cancellation should have stopped the read before the frame completed");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static byte[] Pattern(int length)
    {
        var buf = new byte[length];
        for (int i = 0; i < length; i++) buf[i] = (byte)(i * 31 + 7);
        return buf;
    }

    /// <summary>
    /// A stream that hands back at most <c>maxBytesPerRead</c> bytes per call —
    /// the behaviour of a real pipe, which <see cref="MemoryStream"/> never
    /// reproduces because it always satisfies a read in full. Optionally trips a
    /// <see cref="CancellationTokenSource"/> partway through.
    /// </summary>
    private sealed class ChunkedStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _maxBytesPerRead;
        private readonly int _cancelAfterBytes;
        private readonly CancellationTokenSource? _cts;
        private int _pos;

        public ChunkedStream(byte[] data, int maxBytesPerRead,
                             int cancelAfterBytes = -1, CancellationTokenSource? cts = null)
        {
            _data = data;
            _maxBytesPerRead = maxBytesPerRead;
            _cancelAfterBytes = cancelAfterBytes;
            _cts = cts;
        }

        public int ReadCalls { get; private set; }
        public int BytesRead => _pos;

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCalls++;
            int n = Math.Min(Math.Min(count, _maxBytesPerRead), _data.Length - _pos);
            if (n <= 0) return 0;
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            if (_cancelAfterBytes >= 0 && _pos >= _cancelAfterBytes) _cts?.Cancel();
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override bool CanRead  => true;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;
        public override long Length   => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
