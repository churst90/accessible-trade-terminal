using System.Text;

namespace AccessibleTrader.Plugins.Hyperliquid
{
    /// <summary>
    /// An insertion-ordered map for the action that gets signed. Hyperliquid hashes the
    /// msgpack bytes of the action, and msgpack maps are ordered, so the keys must be
    /// packed in exactly the order the Python SDK builds its dicts. A plain
    /// <c>Dictionary</c> does not promise that order; this does.
    /// </summary>
    internal sealed class OrderedMap : List<KeyValuePair<string, object?>>
    {
        public OrderedMap Add(string key, object? value) { base.Add(new(key, value)); return this; }
    }

    /// <summary>
    /// The smallest msgpack encoder that covers what an exchange action contains: maps,
    /// arrays, strings, booleans, null and integers. Integers use the SMALLEST encoding
    /// that fits, exactly as the Python <c>msgpack</c> package does, because the signed
    /// hash covers the bytes and "same number, longer encoding" is a different hash.
    /// Floats are deliberately not supported: prices and sizes travel as strings and a
    /// float in a signed action would be a bug.
    /// </summary>
    internal static class MsgPack
    {
        public static byte[] Pack(object? value)
        {
            var ms = new MemoryStream();
            Write(ms, value);
            return ms.ToArray();
        }

        private static void Write(MemoryStream o, object? v)
        {
            switch (v)
            {
                case null: o.WriteByte(0xc0); break;
                case bool b: o.WriteByte(b ? (byte)0xc3 : (byte)0xc2); break;
                case string s: WriteString(o, s); break;
                case int i: WriteInt(o, i); break;
                case long l: WriteInt(o, l); break;
                case ulong u: WriteUInt(o, u); break;
                case OrderedMap m:
                    WriteHeader(o, m.Count, 0x80, 0xde, 0xdf);
                    foreach (var kv in m) { WriteString(o, kv.Key); Write(o, kv.Value); }
                    break;
                case System.Collections.IEnumerable list:
                    var items = list.Cast<object?>().ToList();
                    WriteHeader(o, items.Count, 0x90, 0xdc, 0xdd);
                    foreach (var item in items) Write(o, item);
                    break;
                default:
                    throw new NotSupportedException($"MsgPack cannot pack {v.GetType().Name}.");
            }
        }

        // fix form (4 bits of count in the type byte), then 16-bit, then 32-bit.
        private static void WriteHeader(MemoryStream o, int n, byte fix, byte t16, byte t32)
        {
            if (n < 16) o.WriteByte((byte)(fix | n));
            else if (n <= ushort.MaxValue) { o.WriteByte(t16); Be(o, (ulong)n, 2); }
            else { o.WriteByte(t32); Be(o, (ulong)n, 4); }
        }

        private static void WriteString(MemoryStream o, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            int n = bytes.Length;
            if (n < 32) o.WriteByte((byte)(0xa0 | n));
            else if (n <= byte.MaxValue) { o.WriteByte(0xd9); o.WriteByte((byte)n); }
            else if (n <= ushort.MaxValue) { o.WriteByte(0xda); Be(o, (ulong)n, 2); }
            else { o.WriteByte(0xdb); Be(o, (ulong)n, 4); }
            o.Write(bytes, 0, bytes.Length);
        }

        private static void WriteInt(MemoryStream o, long v)
        {
            if (v >= 0) { WriteUInt(o, (ulong)v); return; }
            if (v >= -32) o.WriteByte((byte)(sbyte)v);
            else if (v >= sbyte.MinValue) { o.WriteByte(0xd0); o.WriteByte((byte)(sbyte)v); }
            else if (v >= short.MinValue) { o.WriteByte(0xd1); Be(o, (ulong)(ushort)(short)v, 2); }
            else if (v >= int.MinValue) { o.WriteByte(0xd2); Be(o, (ulong)(uint)(int)v, 4); }
            else { o.WriteByte(0xd3); Be(o, (ulong)v, 8); }
        }

        private static void WriteUInt(MemoryStream o, ulong v)
        {
            if (v < 128) o.WriteByte((byte)v);
            else if (v <= byte.MaxValue) { o.WriteByte(0xcc); o.WriteByte((byte)v); }
            else if (v <= ushort.MaxValue) { o.WriteByte(0xcd); Be(o, v, 2); }
            else if (v <= uint.MaxValue) { o.WriteByte(0xce); Be(o, v, 4); }
            else { o.WriteByte(0xcf); Be(o, v, 8); }
        }

        private static void Be(MemoryStream o, ulong v, int bytes)
        {
            for (int i = bytes - 1; i >= 0; i--) o.WriteByte((byte)(v >> (8 * i)));
        }
    }
}
