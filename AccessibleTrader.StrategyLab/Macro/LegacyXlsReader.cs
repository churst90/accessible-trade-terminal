using System.Text;

namespace AccessibleTrader.StrategyLab.Macro;

/// <summary>
/// A minimal reader for legacy Excel 97-2003 <c>.xls</c> files (BIFF8 inside an OLE2 compound
/// file), enough to pull the cell values out of AAII's public <c>sentiment.xls</c>. The lab has
/// no spreadsheet dependency and this one file is the only .xls source, so a small reader is
/// cheaper than a package. It reads NUMBER, RK, MULRK, LABELSST, LABEL and cached FORMULA
/// results; it ignores formatting. Excel serial dates come back as numbers — convert with
/// <see cref="SerialToDate"/>, which honours the workbook's 1900/1904 date system.
/// </summary>
public sealed class LegacyXlsReader
{
    public sealed record Sheet(string Name, Dictionary<(int Row, int Col), object> Cells)
    {
        public int MaxRow => Cells.Count == 0 ? -1 : Cells.Keys.Max(k => k.Row);
        public object? Get(int row, int col) => Cells.TryGetValue((row, col), out var v) ? v : null;
    }

    public List<Sheet> Sheets { get; } = new();
    public bool Date1904 { get; private set; }

    public DateOnly SerialToDate(double serial) =>
        DateOnly.FromDateTime((Date1904 ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 30)).AddDays(Math.Floor(serial)));

    public static LegacyXlsReader Read(byte[] file)
    {
        var workbook = CompoundFile.ReadStream(file, "Workbook") ?? CompoundFile.ReadStream(file, "Book")
            ?? throw new FormatException("no Workbook stream in compound file");
        var r = new LegacyXlsReader();
        r.ParseBiff(workbook);
        return r;
    }

    private void ParseBiff(byte[] s)
    {
        var sheetNames = new List<string>();
        var sst = new List<string>();
        Sheet? current = null;
        int sheetIndex = -1;
        int pos = 0;
        int depth = 0;
        bool pendingStringFormula = false;
        (int Row, int Col) pendingCell = default;

        while (pos + 4 <= s.Length)
        {
            int type = BitConverter.ToUInt16(s, pos);
            int len = BitConverter.ToUInt16(s, pos + 2);
            int data = pos + 4;
            int next = data + len;
            if (next > s.Length) break;

            switch (type)
            {
                case 0x0809: // BOF
                    depth++;
                    int dt = BitConverter.ToUInt16(s, data + 2);
                    if (dt == 0x0010 && depth == 1) // worksheet
                    {
                        sheetIndex++;
                        current = new Sheet(sheetIndex < sheetNames.Count ? sheetNames[sheetIndex] : $"Sheet{sheetIndex}", new());
                        Sheets.Add(current);
                    }
                    else if (dt != 0x0005 && depth == 1)
                    {
                        sheetIndex++; // chart or macro sheet: keep indices aligned with BOUNDSHEET
                        current = null;
                    }
                    break;
                case 0x000A: // EOF
                    depth--;
                    if (depth == 0) current = null;
                    break;
                case 0x0022: // DATEMODE
                    Date1904 = BitConverter.ToUInt16(s, data) == 1;
                    break;
                case 0x0085: // BOUNDSHEET
                {
                    int cch = s[data + 6];
                    bool high = (s[data + 7] & 1) != 0;
                    sheetNames.Add(high ? Encoding.Unicode.GetString(s, data + 8, cch * 2)
                                        : Encoding.Latin1.GetString(s, data + 8, cch));
                    break;
                }
                case 0x00FC: // SST (+ CONTINUE records that follow)
                {
                    var segments = new List<(int Start, int End)> { (data + 8, next) };
                    int p2 = next;
                    while (p2 + 4 <= s.Length && BitConverter.ToUInt16(s, p2) == 0x003C)
                    {
                        int l2 = BitConverter.ToUInt16(s, p2 + 2);
                        segments.Add((p2 + 4, p2 + 4 + l2));
                        p2 += 4 + l2;
                    }
                    int unique = BitConverter.ToInt32(s, data + 4);
                    ReadSst(s, segments, unique, sst);
                    next = p2;
                    break;
                }
                case 0x0203 when current != null: // NUMBER
                    current.Cells[(Row(s, data), Col(s, data))] = BitConverter.ToDouble(s, data + 6);
                    break;
                case 0x027E when current != null: // RK
                    current.Cells[(Row(s, data), Col(s, data))] = DecodeRk(BitConverter.ToInt32(s, data + 6));
                    break;
                case 0x00BD when current != null: // MULRK
                {
                    int row = Row(s, data), first = Col(s, data);
                    int count = (len - 6) / 6;
                    for (int i = 0; i < count; i++)
                        current.Cells[(row, first + i)] = DecodeRk(BitConverter.ToInt32(s, data + 4 + i * 6 + 2));
                    break;
                }
                case 0x00FD when current != null: // LABELSST
                {
                    int idx = BitConverter.ToInt32(s, data + 6);
                    if (idx >= 0 && idx < sst.Count) current.Cells[(Row(s, data), Col(s, data))] = sst[idx];
                    break;
                }
                case 0x0204 when current != null: // LABEL (BIFF8 XLUnicodeString)
                {
                    int cch = BitConverter.ToUInt16(s, data + 6);
                    bool high = (s[data + 8] & 1) != 0;
                    current.Cells[(Row(s, data), Col(s, data))] = high
                        ? Encoding.Unicode.GetString(s, data + 9, cch * 2)
                        : Encoding.Latin1.GetString(s, data + 9, cch);
                    break;
                }
                case 0x0006 when current != null: // FORMULA: cached result
                {
                    bool special = s[data + 12] == 0xFF && s[data + 13] == 0xFF;
                    if (!special)
                        current.Cells[(Row(s, data), Col(s, data))] = BitConverter.ToDouble(s, data + 6);
                    else if (s[data + 6] == 0) { pendingStringFormula = true; pendingCell = (Row(s, data), Col(s, data)); }
                    break;
                }
                case 0x0207 when current != null && pendingStringFormula: // STRING after a FORMULA
                {
                    int cch = BitConverter.ToUInt16(s, data);
                    bool high = (s[data + 2] & 1) != 0;
                    current.Cells[pendingCell] = high ? Encoding.Unicode.GetString(s, data + 3, cch * 2)
                                                      : Encoding.Latin1.GetString(s, data + 3, cch);
                    pendingStringFormula = false;
                    break;
                }
            }
            pos = next;
        }
    }

    private static int Row(byte[] s, int d) => BitConverter.ToUInt16(s, d);
    private static int Col(byte[] s, int d) => BitConverter.ToUInt16(s, d + 2);

    private static double DecodeRk(int rk)
    {
        double v = (rk & 2) != 0
            ? rk >> 2
            : BitConverter.Int64BitsToDouble((long)(rk & unchecked((int)0xFFFFFFFC)) << 32);
        return (rk & 1) != 0 ? v / 100 : v;
    }

    /// <summary>Shared-string table across CONTINUE boundaries. Character runs that cross a
    /// boundary restart with a fresh 1-byte "high byte" flag; header and trailer bytes do not.</summary>
    private static void ReadSst(byte[] s, List<(int Start, int End)> segs, int unique, List<string> sst)
    {
        int seg = 0, p = segs[0].Start;
        void Ensure() { while (p >= segs[seg].End && seg + 1 < segs.Count) { seg++; p = segs[seg].Start; } }
        byte Byte() { Ensure(); return s[p++]; }
        int U16() { int a = Byte(); return a | (Byte() << 8); }
        int I32() { int a = U16(); return a | (U16() << 16); }
        void Skip(int n) { while (n > 0) { Ensure(); int take = Math.Min(n, segs[seg].End - p); p += take; n -= take; if (take == 0) break; } }

        for (int i = 0; i < unique; i++)
        {
            if (seg >= segs.Count || (seg == segs.Count - 1 && p >= segs[seg].End)) break;
            int cch = U16();
            byte flags = Byte();
            bool high = (flags & 1) != 0;
            int runs = (flags & 8) != 0 ? U16() : 0;
            int ext = (flags & 4) != 0 ? I32() : 0;
            var sb = new StringBuilder(cch);
            int left = cch;
            while (left > 0)
            {
                if (p >= segs[seg].End)
                {
                    if (seg + 1 >= segs.Count) break;
                    seg++; p = segs[seg].Start;
                    high = (s[p++] & 1) != 0;
                }
                int width = high ? 2 : 1;
                int avail = (segs[seg].End - p) / width;
                int take = Math.Min(left, avail);
                sb.Append(high ? Encoding.Unicode.GetString(s, p, take * 2) : Encoding.Latin1.GetString(s, p, take));
                p += take * width;
                left -= take;
                if (take == 0 && avail == 0 && p < segs[seg].End) p = segs[seg].End; // odd trailing byte
            }
            Skip(runs * 4);
            Skip(ext);
            sst.Add(sb.ToString());
        }
    }

    /// <summary>OLE2 / Compound File Binary: just enough to read one named stream.</summary>
    private static class CompoundFile
    {
        private const int EndOfChain = -2;

        public static byte[]? ReadStream(byte[] f, string name)
        {
            if (f.Length < 512 || BitConverter.ToUInt64(f, 0) != 0xE11AB1A1E011CFD0UL)
                throw new FormatException("not an OLE2 compound file");
            int sectorSize = 1 << BitConverter.ToUInt16(f, 0x1E);
            int miniSectorSize = 1 << BitConverter.ToUInt16(f, 0x20);
            int firstDir = BitConverter.ToInt32(f, 0x30);
            uint miniCutoff = BitConverter.ToUInt32(f, 0x38);
            int firstMiniFat = BitConverter.ToInt32(f, 0x3C);
            int firstDifat = BitConverter.ToInt32(f, 0x44);
            int numDifat = BitConverter.ToInt32(f, 0x48);

            int Offset(int sid) => (sid + 1) * sectorSize;

            // DIFAT: 109 entries in the header, then chained DIFAT sectors.
            var fatSectors = new List<int>();
            for (int i = 0; i < 109; i++)
            {
                int sid = BitConverter.ToInt32(f, 0x4C + i * 4);
                if (sid >= 0) fatSectors.Add(sid);
            }
            int difat = firstDifat;
            for (int n = 0; n < numDifat && difat >= 0; n++)
            {
                int per = sectorSize / 4 - 1;
                for (int i = 0; i < per; i++)
                {
                    int sid = BitConverter.ToInt32(f, Offset(difat) + i * 4);
                    if (sid >= 0) fatSectors.Add(sid);
                }
                difat = BitConverter.ToInt32(f, Offset(difat) + per * 4);
            }
            var fat = new List<int>();
            foreach (var fs in fatSectors)
                for (int i = 0; i < sectorSize / 4; i++)
                    if (Offset(fs) + i * 4 + 4 <= f.Length) fat.Add(BitConverter.ToInt32(f, Offset(fs) + i * 4));

            byte[] Chain(int start, List<int> table, Func<int, int> offset, int size, byte[] src)
            {
                using var ms = new MemoryStream();
                int sid = start, guard = 0;
                while (sid >= 0 && sid < table.Count && guard++ < table.Count + 1)
                {
                    int off = offset(sid);
                    int take = Math.Min(size, src.Length - off);
                    if (take <= 0) break;
                    ms.Write(src, off, take);
                    sid = table[sid];
                }
                return ms.ToArray();
            }

            var dir = Chain(firstDir, fat, Offset, sectorSize, f);
            int rootStart = -1; long rootSize = 0;
            int targetStart = -1; long targetSize = 0;
            for (int e = 0; e + 128 <= dir.Length; e += 128)
            {
                int nameLen = BitConverter.ToUInt16(dir, e + 0x40);
                byte kind = dir[e + 0x42];
                if (kind == 0 || nameLen < 2) continue;
                string entryName = Encoding.Unicode.GetString(dir, e, nameLen - 2);
                int start = BitConverter.ToInt32(dir, e + 0x74);
                long size = BitConverter.ToUInt32(dir, e + 0x78);
                if (kind == 5) { rootStart = start; rootSize = size; }
                else if (kind == 2 && string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase))
                { targetStart = start; targetSize = size; }
            }
            if (targetStart == -1 && targetSize == 0) return null;

            byte[] bytes;
            if (targetSize < miniCutoff)
            {
                var miniFatBytes = Chain(firstMiniFat, fat, Offset, sectorSize, f);
                var miniFat = new List<int>();
                for (int i = 0; i + 4 <= miniFatBytes.Length; i += 4) miniFat.Add(BitConverter.ToInt32(miniFatBytes, i));
                var ministream = Chain(rootStart, fat, Offset, sectorSize, f);
                bytes = Chain(targetStart, miniFat, sid => sid * miniSectorSize, miniSectorSize, ministream);
            }
            else
            {
                bytes = Chain(targetStart, fat, Offset, sectorSize, f);
            }
            _ = rootSize;
            return bytes.Length > targetSize ? bytes[..(int)targetSize] : bytes;
        }
    }
}
