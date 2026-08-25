using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.ScriptSandbox;

/// <summary>
/// Metadata describing a loaded indicator. Sent from worker to host in the
/// <see cref="Opcode.Ready"/> frame after a successful <c>LoadAssembly</c>.
/// </summary>
public sealed record IndicatorMetadataMessage(
    string Id,
    string DisplayName,
    string[] ComponentNames,
    int[] DisplayTypeValues,                 // ComponentDisplayType as int so the
                                             // wire format doesn't depend on the
                                             // SDK enum numbering.
    Dictionary<string, double> DefaultParameters,
    int[] CausalityValues);                  // ComponentCausality as int, same reason.
                                             // Appended last: host and worker ship in the
                                             // same build, so there is no old frame to read.

/// <summary>
/// Payload for <see cref="Opcode.Calculate"/>. Carries the OHLCV span plus
/// the parameter dictionary the host wants applied.
/// </summary>
public sealed record CalculateRequest(
    Ohlcv[] Bars,
    Dictionary<string, double> Parameters);

/// <summary>
/// Payload for <see cref="Opcode.Result"/>. One <c>double[]</c> per
/// component, in the same order as <see cref="IndicatorMetadataMessage.ComponentNames"/>.
/// </summary>
public sealed record CalculateResponse(double[][] ComponentData);

/// <summary>
/// Tiny binary codec for the messages above. We don't use JSON — the
/// Calculate path is in the indicator hot loop and allocating / parsing
/// JSON per call would show up in trading-latency graphs.
///
/// Format (all length-prefixed with u32 big-endian counts unless noted):
///
///   IndicatorMetadataMessage:
///     [str Id][str DisplayName]
///     [u32 N][str ComponentNames×N]
///     [u32 M][i32 DisplayTypeValues×M]
///     [u32 P][(str name)(f64 value)×P]
///     [u32 C][i32 CausalityValues×C]
///
///   CalculateRequest:
///     [u32 N][Ohlcv×N]
///     [u32 P][(str name)(f64 value)×P]
///
///   CalculateResponse:
///     [u32 K][per component: (u32 L)(f64×L)]
///
///   Ohlcv (fixed 48 bytes):
///     [i64 DateUtcTicks][f64 Open][f64 High][f64 Low][f64 Close][f64 Volume]
///
///   Strings: [u32 byte_count][utf8 bytes]
/// </summary>
public static class MessageCodec
{
    // Defense-in-depth caps on untrusted u32 counts from decoded payloads.
    // FrameCodec already enforces a 64 MB frame cap, but without per-field
    // caps a single `u32=500_000_000` string/array length triggers an
    // OOM-class allocation before the invalid read is detected.
    private const int MaxArrayElements = 1_000_000;   // generous for bar arrays
    private const int MaxStringBytes   = 64 * 1024;   // 64 KB covers any id/name/param

    // ── IndicatorMetadataMessage ───────────────────────────────────────

    public static byte[] EncodeMetadata(IndicatorMetadataMessage meta)
    {
        using var ms = new MemoryStream();
        WriteString(ms, meta.Id);
        WriteString(ms, meta.DisplayName);

        WriteU32(ms, (uint)meta.ComponentNames.Length);
        foreach (var n in meta.ComponentNames) WriteString(ms, n);

        WriteU32(ms, (uint)meta.DisplayTypeValues.Length);
        foreach (var v in meta.DisplayTypeValues) WriteI32(ms, v);

        WriteU32(ms, (uint)meta.DefaultParameters.Count);
        foreach (var kv in meta.DefaultParameters)
        {
            WriteString(ms, kv.Key);
            WriteF64(ms, kv.Value);
        }

        WriteU32(ms, (uint)meta.CausalityValues.Length);
        foreach (var v in meta.CausalityValues) WriteI32(ms, v);
        return ms.ToArray();
    }

    public static IndicatorMetadataMessage DecodeMetadata(byte[] payload)
    {
        var r = new ByteReader(payload);
        var id    = r.ReadString();
        var name  = r.ReadString();

        int nComp = CheckCount(r.ReadU32(), "ComponentNames");
        var compNames = new string[nComp];
        for (int i = 0; i < nComp; i++) compNames[i] = r.ReadString();

        int nDisp = CheckCount(r.ReadU32(), "DisplayTypeValues");
        var dispVals = new int[nDisp];
        for (int i = 0; i < nDisp; i++) dispVals[i] = r.ReadI32();

        int nParam = CheckCount(r.ReadU32(), "DefaultParameters");
        var parms = new Dictionary<string, double>(nParam);
        for (int i = 0; i < nParam; i++)
        {
            var k = r.ReadString();
            var v = r.ReadF64();
            parms[k] = v;
        }
        int nCaus = CheckCount(r.ReadU32(), "CausalityValues");
        var causVals = new int[nCaus];
        for (int i = 0; i < nCaus; i++) causVals[i] = r.ReadI32();

        return new IndicatorMetadataMessage(id, name, compNames, dispVals, parms, causVals);
    }

    // ── CalculateRequest ───────────────────────────────────────────────

    public static byte[] EncodeCalculateRequest(CalculateRequest req)
    {
        using var ms = new MemoryStream();
        WriteU32(ms, (uint)req.Bars.Length);
        foreach (var bar in req.Bars) WriteOhlcv(ms, bar);

        WriteU32(ms, (uint)req.Parameters.Count);
        foreach (var kv in req.Parameters)
        {
            WriteString(ms, kv.Key);
            WriteF64(ms, kv.Value);
        }
        return ms.ToArray();
    }

    public static CalculateRequest DecodeCalculateRequest(byte[] payload)
    {
        var r = new ByteReader(payload);
        int n = CheckCount(r.ReadU32(), "Bars");
        var bars = new Ohlcv[n];
        for (int i = 0; i < n; i++) bars[i] = r.ReadOhlcv();

        int p = CheckCount(r.ReadU32(), "Parameters");
        var parms = new Dictionary<string, double>(p);
        for (int i = 0; i < p; i++)
        {
            var k = r.ReadString();
            var v = r.ReadF64();
            parms[k] = v;
        }
        return new CalculateRequest(bars, parms);
    }

    // ── CalculateResponse ──────────────────────────────────────────────

    public static byte[] EncodeCalculateResponse(CalculateResponse resp)
    {
        using var ms = new MemoryStream();
        WriteU32(ms, (uint)resp.ComponentData.Length);
        foreach (var arr in resp.ComponentData)
        {
            WriteU32(ms, (uint)arr.Length);
            foreach (var v in arr) WriteF64(ms, v);
        }
        return ms.ToArray();
    }

    public static CalculateResponse DecodeCalculateResponse(byte[] payload)
    {
        var r = new ByteReader(payload);
        int k = CheckCount(r.ReadU32(), "ComponentData");
        var components = new double[k][];
        for (int c = 0; c < k; c++)
        {
            int l = CheckCount(r.ReadU32(), "ComponentData[i]");
            var arr = new double[l];
            for (int i = 0; i < l; i++) arr[i] = r.ReadF64();
            components[c] = arr;
        }
        return new CalculateResponse(components);
    }

    private static int CheckCount(uint raw, string field)
    {
        if (raw > MaxArrayElements)
            throw new InvalidDataException($"{field} count {raw} exceeds cap {MaxArrayElements}.");
        return (int)raw;
    }

    // ── Primitives ─────────────────────────────────────────────────────

    private static void WriteString(MemoryStream ms, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? "");
        WriteU32(ms, (uint)bytes.Length);
        ms.Write(bytes, 0, bytes.Length);
    }

    private static void WriteU32(MemoryStream ms, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        ms.Write(b);
    }

    private static void WriteI32(MemoryStream ms, int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        ms.Write(b);
    }

    private static void WriteF64(MemoryStream ms, double v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(b, v);
        ms.Write(b);
    }

    private static void WriteI64(MemoryStream ms, long v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, v);
        ms.Write(b);
    }

    private static void WriteOhlcv(MemoryStream ms, Ohlcv bar)
    {
        WriteI64(ms, bar.Date.Ticks);
        WriteF64(ms, bar.Open);
        WriteF64(ms, bar.High);
        WriteF64(ms, bar.Low);
        WriteF64(ms, bar.Close);
        WriteF64(ms, bar.Volume);
    }

    private ref struct ByteReader
    {
        private readonly byte[] _buf;
        private int _pos;
        public ByteReader(byte[] buf) { _buf = buf; _pos = 0; }

        private void EnsureAvailable(int n)
        {
            if (n < 0 || _pos + n > _buf.Length)
                throw new InvalidDataException(
                    $"Truncated frame: attempted to read {n} bytes at offset {_pos}, buffer length {_buf.Length}.");
        }

        public uint ReadU32()
        {
            EnsureAvailable(4);
            var v = BinaryPrimitives.ReadUInt32BigEndian(_buf.AsSpan(_pos, 4));
            _pos += 4;
            return v;
        }

        public int ReadI32()
        {
            EnsureAvailable(4);
            var v = BinaryPrimitives.ReadInt32BigEndian(_buf.AsSpan(_pos, 4));
            _pos += 4;
            return v;
        }

        public double ReadF64()
        {
            EnsureAvailable(8);
            var v = BinaryPrimitives.ReadDoubleBigEndian(_buf.AsSpan(_pos, 8));
            _pos += 8;
            return v;
        }

        public long ReadI64()
        {
            EnsureAvailable(8);
            var v = BinaryPrimitives.ReadInt64BigEndian(_buf.AsSpan(_pos, 8));
            _pos += 8;
            return v;
        }

        public string ReadString()
        {
            uint rawLen = ReadU32();
            if (rawLen == 0) return "";
            if (rawLen > MaxStringBytes)
                throw new InvalidDataException($"String length {rawLen} exceeds cap {MaxStringBytes}.");
            int len = (int)rawLen;
            EnsureAvailable(len);
            var s = Encoding.UTF8.GetString(_buf, _pos, len);
            _pos += len;
            return s;
        }

        public Ohlcv ReadOhlcv()
        {
            var ticks  = ReadI64();
            var open   = ReadF64();
            var high   = ReadF64();
            var low    = ReadF64();
            var close  = ReadF64();
            var volume = ReadF64();
            return new Ohlcv(new DateTime(ticks, DateTimeKind.Utc), open, high, low, close, volume);
        }
    }
}
