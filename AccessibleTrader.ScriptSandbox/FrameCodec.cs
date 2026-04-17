using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AccessibleTrader.ScriptSandbox;

/// <summary>
/// Wire format on stdio:
///
///     ┌─────────────┬────────────┬──────────────────┐
///     │ u32 length  │ u8 opcode  │ payload (len-1)  │
///     └─────────────┴────────────┴──────────────────┘
///
/// <c>length</c> is the number of bytes that follow (opcode + payload),
/// encoded big-endian for interop clarity. Frames are not fragmented; a
/// single <see cref="ReadFrameAsync"/> returns exactly one complete frame
/// or throws.
///
/// No heartbeat / keepalive: the process lifecycle is owned by the host
/// supervisor, which kills the worker on timeout regardless of whether
/// the worker has been reading the pipe.
/// </summary>
public static class FrameCodec
{
    /// <summary>
    /// Upper bound on a single frame. 64 MB matches the Binance Vision
    /// archive cap — the biggest payload we'd reasonably send in one
    /// LoadAssembly / Calculate pair. A frame claiming more is almost
    /// certainly a corrupted stream; we fail fast rather than allocate.
    /// </summary>
    public const int MaxFrameBytes = 64 * 1024 * 1024;

    public static async Task WriteFrameAsync(Stream stream, Opcode opcode, byte[] payload, CancellationToken ct = default)
    {
        if (payload.LongLength + 1 > MaxFrameBytes)
            throw new InvalidDataException($"Frame payload too large: {payload.LongLength + 1} > {MaxFrameBytes}.");

        var header = new byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)(payload.Length + 1));
        header[4] = (byte)opcode;

        await stream.WriteAsync(header, 0, header.Length, ct).ConfigureAwait(false);
        if (payload.Length > 0)
            await stream.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<(Opcode opcode, byte[] payload)> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        var header = await ReadExactlyAsync(stream, 5, ct).ConfigureAwait(false);
        uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length == 0)
            throw new InvalidDataException("Frame length was zero — malformed stream.");
        if (length > MaxFrameBytes)
            throw new InvalidDataException($"Frame length {length} exceeds {MaxFrameBytes} — malformed stream.");

        var opcode = (Opcode)header[4];
        int payloadLen = (int)length - 1;
        var payload = payloadLen == 0
            ? Array.Empty<byte>()
            : await ReadExactlyAsync(stream, payloadLen, ct).ConfigureAwait(false);
        return (opcode, payload);
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken ct)
    {
        var buf = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buf, read, count - read, ct).ConfigureAwait(false);
            if (n == 0)
                throw new EndOfStreamException(
                    $"Stream closed after {read}/{count} bytes — the other end exited.");
            read += n;
        }
        return buf;
    }
}
