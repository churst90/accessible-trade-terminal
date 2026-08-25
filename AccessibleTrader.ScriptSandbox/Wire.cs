using System.Buffers.Binary;
using System.Text;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.ScriptSandbox;

/// <summary>
/// The primitive read/write pairs every message on the host ↔ worker pipe is built from.
///
/// <para>
/// These used to be private helpers inside <see cref="MessageCodec"/>. The strategy protocol
/// needs the same primitives, and a second copy of "how a string is length-prefixed" is exactly
/// the kind of duplication that lets one side of the wire drift from the other — a reader and a
/// writer that disagree by one byte desync the pipe and surface as "malformed stream" with no
/// hint of which field was wrong. One definition, used by both codecs.
/// </para>
///
/// <para>
/// All integers are big-endian. Every count is checked against <see cref="MaxArrayElements"/>
/// before it is used to size an allocation: <see cref="FrameCodec"/> caps a frame at 64 MB, but
/// without a per-field cap a single <c>u32 = 500_000_000</c> in a corrupt or hostile payload
/// triggers an OOM-class allocation before the invalid read is ever detected.
/// </para>
/// </summary>
public static class Wire
{
    /// <summary>Cap on any decoded count that sizes an array. Generous for bar arrays.</summary>
    public const int MaxArrayElements = 1_000_000;

    /// <summary>Cap on a decoded string's byte length. 64 KB covers any id, name or JSON blob
    /// except a series config, which uses <see cref="MaxBlobBytes"/>.</summary>
    public const int MaxStringBytes = 64 * 1024;

    /// <summary>
    /// Cap on a decoded long-string field. Series configs are carried as JSON and a chart series
    /// with a full component stack, levels and colour rules runs past 64 KB, so the blob fields
    /// get their own ceiling rather than raising the one that guards ids and names.
    /// </summary>
    public const int MaxBlobBytes = 8 * 1024 * 1024;

    // ── Tags for a value whose static type is `object` ─────────────────────────────
    // StrategyParameter.DefaultValue and Initialize's parameter map are both `object`,
    // so the wire has to say what it is carrying. Values are stable — do not renumber.
    public const byte TagNull   = 0;
    public const byte TagBool   = 1;
    public const byte TagInt64  = 2;
    public const byte TagDouble = 3;
    public const byte TagString = 4;

    public static void WriteU32(Stream s, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        s.Write(b);
    }

    public static void WriteI32(Stream s, int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        s.Write(b);
    }

    public static void WriteI64(Stream s, long v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, v);
        s.Write(b);
    }

    public static void WriteF64(Stream s, double v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(b, v);
        s.Write(b);
    }

    public static void WriteF32(Stream s, float v) => WriteF64(s, v);

    public static void WriteBool(Stream s, bool v) => s.WriteByte(v ? (byte)1 : (byte)0);

    public static void WriteString(Stream s, string? v)
    {
        var bytes = Encoding.UTF8.GetBytes(v ?? "");
        WriteU32(s, (uint)bytes.Length);
        s.Write(bytes, 0, bytes.Length);
    }

    /// <summary>A string that may be absent, distinguished from the empty string.</summary>
    public static void WriteNullableString(Stream s, string? v)
    {
        WriteBool(s, v != null);
        if (v != null) WriteString(s, v);
    }

    public static void WriteNullableF64(Stream s, double? v)
    {
        WriteBool(s, v.HasValue);
        if (v.HasValue) WriteF64(s, v.Value);
    }

    public static void WriteNullableI32(Stream s, int? v)
    {
        WriteBool(s, v.HasValue);
        if (v.HasValue) WriteI32(s, v.Value);
    }

    /// <summary>A <see cref="DateTime"/> as UTC ticks. The kind is normalised to UTC on read —
    /// the whole terminal stamps bars in UTC and a round-trip that changed the kind would move
    /// every bar by the worker machine's offset.</summary>
    public static void WriteDate(Stream s, DateTime v) => WriteI64(s, v.Ticks);

    public static void WriteNullableDate(Stream s, DateTime? v)
    {
        WriteBool(s, v.HasValue);
        if (v.HasValue) WriteDate(s, v.Value);
    }

    public static void WriteOhlcv(Stream s, Ohlcv bar)
    {
        WriteI64(s, bar.Date.Ticks);
        WriteF64(s, bar.Open);
        WriteF64(s, bar.High);
        WriteF64(s, bar.Low);
        WriteF64(s, bar.Close);
        WriteF64(s, bar.Volume);
    }

    public static void WriteOhlcvArray(Stream s, IReadOnlyList<Ohlcv> bars)
    {
        WriteU32(s, (uint)bars.Count);
        for (int i = 0; i < bars.Count; i++) WriteOhlcv(s, bars[i]);
    }

    public static void WriteDoubleArray(Stream s, double[]? values)
    {
        values ??= Array.Empty<double>();
        WriteU32(s, (uint)values.Length);
        foreach (var v in values) WriteF64(s, v);
    }

    /// <summary>
    /// Writes a value whose static type is <c>object</c>. Only the five shapes a strategy
    /// parameter can legitimately hold cross the boundary; anything else throws by NAME, because
    /// "parameter 'Lookback' is a TimeSpan" is a fixable message and a desynced pipe is not.
    /// </summary>
    public static void WriteTagged(Stream s, string fieldName, object? value)
    {
        switch (value)
        {
            case null:
                s.WriteByte(TagNull);
                return;
            case bool b:
                s.WriteByte(TagBool); WriteBool(s, b); return;
            case string str:
                s.WriteByte(TagString); WriteString(s, str); return;
            case sbyte or byte or short or ushort or int or uint or long:
                s.WriteByte(TagInt64); WriteI64(s, Convert.ToInt64(value)); return;
            case ulong ul:
                s.WriteByte(TagInt64); WriteI64(s, unchecked((long)ul)); return;
            case float or double:
                s.WriteByte(TagDouble); WriteF64(s, Convert.ToDouble(value)); return;
            case decimal d:
                // Narrowed deliberately: the strategy contract's numeric type is double
                // everywhere (prices, quantities, every StrategySignal field), so a decimal
                // parameter is already destined for a double. Saying so here beats refusing a
                // value the strategy would have accepted.
                s.WriteByte(TagDouble); WriteF64(s, (double)d); return;
            case Enum e:
                s.WriteByte(TagInt64); WriteI64(s, Convert.ToInt64(e)); return;
            default:
                throw new NotSupportedException(
                    $"Strategy parameter '{fieldName}' is a {value.GetType().FullName}, which cannot cross " +
                    "the script sandbox boundary. Strategy parameters must be a number, a boolean, a string, or null.");
        }
    }

    public static int CheckCount(uint raw, string field, int cap = MaxArrayElements)
    {
        if (raw > cap)
            throw new InvalidDataException($"{field} count {raw} exceeds cap {cap}.");
        return (int)raw;
    }
}

/// <summary>
/// Forward-only reader over a decoded frame payload. Every read bounds-checks against the
/// buffer, so a truncated or hostile payload throws <see cref="InvalidDataException"/> at the
/// field that ran off the end rather than reading adjacent memory or hanging.
/// </summary>
public ref struct WireReader
{
    private readonly byte[] _buf;
    private int _pos;

    public WireReader(byte[] buf) { _buf = buf; _pos = 0; }

    /// <summary>Bytes not yet consumed. A decoder that finishes with these non-zero has a
    /// reader/writer mismatch, which the message-level round-trip tests assert against.</summary>
    public int Remaining => _buf.Length - _pos;

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

    public long ReadI64()
    {
        EnsureAvailable(8);
        var v = BinaryPrimitives.ReadInt64BigEndian(_buf.AsSpan(_pos, 8));
        _pos += 8;
        return v;
    }

    public double ReadF64()
    {
        EnsureAvailable(8);
        var v = BinaryPrimitives.ReadDoubleBigEndian(_buf.AsSpan(_pos, 8));
        _pos += 8;
        return v;
    }

    public float ReadF32() => (float)ReadF64();

    public byte ReadByte()
    {
        EnsureAvailable(1);
        return _buf[_pos++];
    }

    public bool ReadBool() => ReadByte() != 0;

    public string ReadString(int cap = Wire.MaxStringBytes)
    {
        uint rawLen = ReadU32();
        if (rawLen == 0) return "";
        if (rawLen > cap)
            throw new InvalidDataException($"String length {rawLen} exceeds cap {cap}.");
        int len = (int)rawLen;
        EnsureAvailable(len);
        var s = Encoding.UTF8.GetString(_buf, _pos, len);
        _pos += len;
        return s;
    }

    public string? ReadNullableString(int cap = Wire.MaxStringBytes) =>
        ReadBool() ? ReadString(cap) : null;

    public double? ReadNullableF64() => ReadBool() ? ReadF64() : null;

    public int? ReadNullableI32() => ReadBool() ? ReadI32() : null;

    public DateTime ReadDate() => new DateTime(ReadI64(), DateTimeKind.Utc);

    public DateTime? ReadNullableDate() => ReadBool() ? ReadDate() : null;

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

    public Ohlcv[] ReadOhlcvArray(string field)
    {
        int n = Wire.CheckCount(ReadU32(), field);
        var bars = new Ohlcv[n];
        for (int i = 0; i < n; i++) bars[i] = ReadOhlcv();
        return bars;
    }

    public double[] ReadDoubleArray(string field)
    {
        int n = Wire.CheckCount(ReadU32(), field);
        var values = new double[n];
        for (int i = 0; i < n; i++) values[i] = ReadF64();
        return values;
    }

    public object? ReadTagged()
    {
        byte tag = ReadByte();
        return tag switch
        {
            Wire.TagNull   => null,
            Wire.TagBool   => ReadBool(),
            Wire.TagInt64  => ReadI64(),
            Wire.TagDouble => ReadF64(),
            Wire.TagString => ReadString(),
            _ => throw new InvalidDataException($"Unknown tagged-value tag 0x{tag:X2}."),
        };
    }
}
