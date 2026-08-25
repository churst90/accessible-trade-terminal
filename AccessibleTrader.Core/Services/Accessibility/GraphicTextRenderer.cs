using System.Text;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Renders ASCII text into a dot-resolution bool[,] canvas using Grade-1 8-dot
    /// braille glyphs. Used for the cold-start splash in the Dot Pad graphic area —
    /// the SDK's braille translation API is reserved for the text strip, so the
    /// graphic area gets a self-contained letter table instead.
    ///
    /// Coordinate convention matches <see cref="Dotpad.DotpadTactileDriver.PackViewport"/>:
    /// each braille cell is 2 dots wide × 4 dots tall, dots numbered
    ///   1 = (subX=0, subY=0)    4 = (subX=1, subY=0)
    ///   2 = (subX=0, subY=1)    5 = (subX=1, subY=1)
    ///   3 = (subX=0, subY=2)    6 = (subX=1, subY=2)
    ///   7 = (subX=0, subY=3)    8 = (subX=1, subY=3)
    /// Letter glyphs use dots 1-6 only; dots 7-8 (bottom row) stay clear at Grade 1.
    /// </summary>
    public static class GraphicTextRenderer
    {
        // Each entry encodes a Grade-1 letter as a bitmask where bit N is set iff the
        // corresponding (subX=N/4, subY=N%4) dot of the cell is raised. This is the
        // SAME bit numbering as DotpadTactileDriver.PackViewport, so renderer output
        // is consumed by the existing packer with no translation needed.
        private static readonly Dictionary<char, byte> Grade1 = new()
        {
            [' '] = 0x00,
            ['a'] = 0x01, // dot 1
            ['b'] = 0x03, // 1,2
            ['c'] = 0x11, // 1,4
            ['d'] = 0x31, // 1,4,5
            ['e'] = 0x21, // 1,5
            ['f'] = 0x13, // 1,2,4
            ['g'] = 0x33, // 1,2,4,5
            ['h'] = 0x23, // 1,2,5
            ['i'] = 0x12, // 2,4
            ['j'] = 0x32, // 2,4,5
            ['k'] = 0x05, // 1,3
            ['l'] = 0x07, // 1,2,3
            ['m'] = 0x15, // 1,3,4
            ['n'] = 0x35, // 1,3,4,5
            ['o'] = 0x25, // 1,3,5
            ['p'] = 0x17, // 1,2,3,4
            ['q'] = 0x37, // 1,2,3,4,5
            ['r'] = 0x27, // 1,2,3,5
            ['s'] = 0x16, // 2,3,4
            ['t'] = 0x36, // 2,3,4,5
            ['u'] = 0x45, // 1,3,6
            ['v'] = 0x47, // 1,2,3,6
            ['w'] = 0x72, // 2,4,5,6
            ['x'] = 0x55, // 1,3,4,6
            ['y'] = 0x75, // 1,3,4,5,6
            ['z'] = 0x65, // 1,3,5,6
        };

        /// <summary>
        /// Horizontal cell stride: 2 dot cols for the braille cell + 1 empty separator
        /// col to its right. Without this gap, adjacent characters' right and left
        /// columns touch and the text reads as one continuous blob — the Dot Pad's
        /// graphic area has no built-in inter-cell spacing the way text-strip cells do.
        /// </summary>
        internal const int HorizontalCellStride = 3;

        /// <summary>
        /// Renders <paramref name="text"/> centered (horizontally and vertically) in
        /// a dot canvas sized <paramref name="cols"/> × <paramref name="rows"/>.
        /// Wraps on word boundaries; truncates lines that exceed the canvas width
        /// and lines that don't fit vertically. Adjacent characters are separated by
        /// a 1-col empty gap so the text doesn't run together.
        /// </summary>
        public static bool[,] RenderCentered(string text, int cols, int rows)
        {
            var canvas = new bool[cols, rows];
            if (string.IsNullOrEmpty(text) || cols < 2 || rows < 4) return canvas;

            // With a 3-col stride and a 2-col cell width, N cells consume (3N - 1)
            // dot cols (the trailing gap is unused). Solve for N such that
            // 3N - 1 <= cols → N = (cols + 1) / 3.
            int cellsWide = (cols + 1) / HorizontalCellStride;
            int cellsTall = rows / 4;
            if (cellsWide < 1 || cellsTall < 1) return canvas;

            var lines = WrapWords(text.ToLowerInvariant(), cellsWide);
            int linesToRender = Math.Min(lines.Count, cellsTall);
            int topPad = (cellsTall - linesToRender) / 2;

            for (int li = 0; li < linesToRender; li++)
            {
                string line = lines[li];
                if (line.Length > cellsWide) line = line.Substring(0, cellsWide);
                int leftPad = (cellsWide - line.Length) / 2;
                for (int ci = 0; ci < line.Length; ci++)
                {
                    if (!Grade1.TryGetValue(line[ci], out byte cellByte)) continue;
                    PaintCell(canvas, leftPad + ci, topPad + li, cellByte);
                }
            }
            return canvas;
        }

        private static void PaintCell(bool[,] canvas, int cellX, int cellY, byte cellByte)
        {
            // 3-col horizontal stride: cell cols + 1 separator col to the right.
            // Vertical stride stays at 4 dots (no inter-row gap; vertical adjacency
            // is acceptable for tactile reading).
            int baseX = cellX * HorizontalCellStride;
            int baseY = cellY * 4;
            int w = canvas.GetLength(0);
            int h = canvas.GetLength(1);
            for (int bit = 0; bit < 8; bit++)
            {
                if ((cellByte & (1 << bit)) == 0) continue;
                int x = baseX + (bit / 4);
                int y = baseY + (bit % 4);
                if (x >= 0 && x < w && y >= 0 && y < h) canvas[x, y] = true;
            }
        }

        private static List<string> WrapWords(string text, int maxWidth)
        {
            var lines = new List<string>();
            var current = new StringBuilder();
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.Length == 0) current.Append(word);
                else if (current.Length + 1 + word.Length <= maxWidth) current.Append(' ').Append(word);
                else
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    current.Append(word);
                }
            }
            if (current.Length > 0) lines.Add(current.ToString());
            return lines;
        }
    }
}
