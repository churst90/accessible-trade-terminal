using System.Globalization;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Drawing.Calculators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing
{
    /// <summary>
    /// What a pattern tool's points make, said in words when the last point is placed and in
    /// the drawing's summary.
    ///
    /// <para>
    /// Cody, 2026-11-01: <i>"incorporate the additional drawing pattern recognition tools as well
    /// that may be useful"</i>. On screen a trader checks an XABCD by eye against the ratio
    /// table; without sight the ratios are the pattern. So each tool measures its legs and says
    /// which textbook pattern they match, or which rule they break and by how much. The user
    /// places the points; this does not go looking for patterns by itself.
    /// </para>
    ///
    /// <para>
    /// The ratio tables are the standard ones — Scott Carney's harmonic ratios (Gartley, Bat,
    /// Alternate Bat, Butterfly, Crab, Deep Crab, Cypher), the classic ABCD, and the three hard
    /// rules of an Elliott impulse — with a 5% tolerance on every bound, because hand-placed
    /// points never land on 0.618 exactly.
    /// </para>
    /// </summary>
    public static class PatternReading
    {
        /// <summary>How far outside a ratio's range still counts, as a fraction of the bound.</summary>
        public const double Tolerance = 0.05;

        public static string Describe(DrawingData d, IReadOnlyList<Ohlcv>? data)
        {
            var points = Points(d);
            if (points == null) return "";
            return d.Type switch
            {
                DrawingType.AbcdPattern => Abcd(points, data),
                DrawingType.XabcdPattern => Xabcd(points),
                DrawingType.HeadAndShoulders => HeadAndShoulders(points, data),
                DrawingType.TrianglePattern => Triangle(points, data),
                DrawingType.ElliottImpulse => Elliott(points),
                _ => "",
            };
        }

        private static List<(DateTime Date, double Price)>? Points(DrawingData d)
        {
            int n = d.Type switch
            {
                DrawingType.AbcdPattern or DrawingType.TrianglePattern => 4,
                DrawingType.XabcdPattern or DrawingType.HeadAndShoulders => 5,
                DrawingType.ElliottImpulse => 6,
                _ => 0,
            };
            if (n == 0) return null;
            var list = new List<(DateTime, double)>();
            for (int slot = 1; slot <= n; slot++)
            {
                if (d.GetAnchorDate(slot) is not DateTime t || d.GetAnchorPrice(slot) is not double p) return null;
                list.Add((t, p));
            }
            return list;
        }

        // ── Numbers as said ─────────────────────────────────────────────────────

        private static string Pct(double r) => (r * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        private static string Times(double r) => r.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Price(double p) => SpeechPriceFormatter.FormatPrice(p);

        /// <summary>True when <paramref name="v"/> is within [lo, hi], each bound widened by
        /// <see cref="Tolerance"/>.</summary>
        public static bool Within(double v, double lo, double hi) =>
            v >= lo * (1 - Tolerance) && v <= hi * (1 + Tolerance);

        /// <summary>True when the points go up, down, up… or down, up, down… with no two moves
        /// the same way.</summary>
        private static bool Alternates(IReadOnlyList<(DateTime Date, double Price)> p)
        {
            for (int i = 2; i < p.Count; i++)
            {
                double before = p[i - 1].Price - p[i - 2].Price, now = p[i].Price - p[i - 1].Price;
                if (before == 0 || now == 0 || Math.Sign(before) == Math.Sign(now)) return false;
            }
            return p.Count < 2 || p[1].Price != p[0].Price;
        }

        private const string NotAlternating =
            "The points do not alternate between highs and lows, so the ratios mean nothing yet. Each point should turn the other way from the one before.";

        // ── ABCD ────────────────────────────────────────────────────────────────

        /// <summary>
        /// The classic ABCD: C retraces 38.2 to 88.6% of AB, CD is 1.13 to 2.618 times BC, and in
        /// the textbook form CD is as long as AB (AB=CD). Bullish when D is a low.
        /// </summary>
        public static string Abcd(IReadOnlyList<(DateTime Date, double Price)> p, IReadOnlyList<Ohlcv>? data)
        {
            if (!Alternates(p)) return NotAlternating;
            double ab = Math.Abs(p[1].Price - p[0].Price), bc = Math.Abs(p[2].Price - p[1].Price),
                   cd = Math.Abs(p[3].Price - p[2].Price);
            if (ab == 0 || bc == 0) return NotAlternating;
            double c = bc / ab, ext = cd / bc, eq = cd / ab;
            bool bullish = p[3].Price < p[2].Price;
            string side = bullish ? "Bullish" : "Bearish";
            string measures = $"C retraced {Pct(c)} of AB, CD is {Times(ext)} times BC and {Times(eq)} times AB";
            string bars = data != null ? BarsSentence(data, p[0].Date, p[1].Date, p[2].Date, p[3].Date) : "";

            var broken = new List<string>();
            if (!Within(c, 0.382, 0.886)) broken.Add($"C should retrace 38.2 to 88.6% of AB and retraced {Pct(c)}");
            if (!Within(ext, 1.13, 2.618)) broken.Add($"CD should be 1.13 to 2.618 times BC and is {Times(ext)}");
            if (broken.Count > 0)
                return $"Not an ABCD: {string.Join("; ", broken)}. {measures}.{bars}";

            string equal = Within(eq, 1, 1) ? " AB equals CD, the textbook form." : "";
            return $"{side} ABCD. {measures}.{equal}{bars} D at {Price(p[3].Price)} is where the {(bullish ? "buy" : "sell")} is watched for.";
        }

        private static string BarsSentence(IReadOnlyList<Ohlcv> data, DateTime a, DateTime b, DateTime c, DateTime d)
        {
            int ia = Bar(data, a), ib = Bar(data, b), ic = Bar(data, c), id = Bar(data, d);
            if (ia < 0 || ib < 0 || ic < 0 || id < 0) return "";
            return $" AB took {ib - ia} bars and CD {id - ic}.";
        }

        private static int Bar(IReadOnlyList<Ohlcv> data, DateTime t) =>
            DrawingCalculatorHelper.ResolveAnchorIndex(data, t);

        // ── XABCD harmonics ─────────────────────────────────────────────────────

        /// <summary>One harmonic pattern's ratio table. Null bounds are not checked.</summary>
        public sealed record Harmonic(string Name,
            double BLo, double BHi,          // B as a retracement of XA
            double CLo, double CHi,          // C as a retracement of AB (Cypher: an extension of XA)
            double? CdLo, double? CdHi,      // CD as a multiple of BC
            double DLo, double DHi,          // D as a retracement of XA (Cypher: of XC)
            bool Cypher = false);

        /// <summary>Carney's tables, the version TradingView's XABCD tool labels by.</summary>
        public static readonly IReadOnlyList<Harmonic> Harmonics = new[]
        {
            new Harmonic("Gartley",       0.618, 0.618, 0.382, 0.886, 1.272, 1.618, 0.786, 0.786),
            new Harmonic("Bat",           0.382, 0.5,   0.382, 0.886, 1.618, 2.618, 0.886, 0.886),
            new Harmonic("Alternate Bat", 0.236, 0.382, 0.382, 0.886, 2.0,   3.618, 1.13,  1.13),
            new Harmonic("Butterfly",     0.786, 0.786, 0.382, 0.886, 1.618, 2.618, 1.27,  1.618),
            new Harmonic("Crab",          0.382, 0.618, 0.382, 0.886, 2.618, 3.618, 1.618, 1.618),
            new Harmonic("Deep Crab",     0.886, 0.886, 0.382, 0.886, 2.0,   3.618, 1.618, 1.618),
            new Harmonic("Cypher",        0.382, 0.618, 1.272, 1.414, null,  null,  0.786, 0.786, Cypher: true),
        };

        public static string Xabcd(IReadOnlyList<(DateTime Date, double Price)> p)
        {
            if (!Alternates(p)) return NotAlternating;
            double x = p[0].Price, a = p[1].Price, b = p[2].Price, c = p[3].Price, d = p[4].Price;
            double xa = Math.Abs(a - x), ab = Math.Abs(b - a), bc = Math.Abs(c - b), cd = Math.Abs(d - c);
            if (xa == 0 || ab == 0 || bc == 0) return NotAlternating;
            double rb = ab / xa, rc = bc / ab, rcd = cd / bc, rd = Math.Abs(d - a) / xa;
            double rcX = Math.Abs(c - x) / xa;               // the Cypher's C, against XA
            double xc = Math.Abs(c - x);
            double rdXc = xc == 0 ? double.NaN : Math.Abs(d - c) / xc; // the Cypher's D, against XC
            bool bullish = a > x;                            // X a low: an M, D a low, a buy
            string side = bullish ? "Bullish" : "Bearish";
            string measures = $"B retraced {Pct(rb)} of XA, C {Pct(rc)} of AB, CD is {Times(rcd)} times BC, and D is at {Pct(rd)} of XA";

            foreach (var h in Harmonics)
            {
                bool ok = h.Cypher
                    ? Within(rb, h.BLo, h.BHi) && Within(rcX, h.CLo, h.CHi) && Within(rdXc, h.DLo, h.DHi)
                    : Within(rb, h.BLo, h.BHi) && Within(rc, h.CLo, h.CHi) && Within(rd, h.DLo, h.DHi)
                      && (h.CdLo is not double lo || h.CdHi is not double hi || Within(rcd, lo, hi));
                if (!ok) continue;
                string cy = h.Cypher ? $" For a Cypher, C reached {Times(rcX)} times XA and D retraced {Pct(rdXc)} of XC." : "";
                return $"{side} {h.Name}. {measures}.{cy} D at {Price(d)} is the potential reversal zone.";
            }

            // No match: the nearest by how far D (the ratio that defines a harmonic) is off.
            var nearest = Harmonics.Where(h => !h.Cypher)
                .OrderBy(h => Math.Min(Math.Abs(rd - h.DLo), Math.Abs(rd - h.DHi))).First();
            string why = !Within(rb, nearest.BLo, nearest.BHi)
                ? $"its B is {Range(nearest.BLo, nearest.BHi)} of XA"
                : !Within(rd, nearest.DLo, nearest.DHi)
                    ? $"its D is {Range(nearest.DLo, nearest.DHi)} of XA"
                    : !Within(rc, nearest.CLo, nearest.CHi)
                        ? $"its C is {Range(nearest.CLo, nearest.CHi)} of AB"
                        : $"its CD is {Times(nearest.CdLo ?? 0)} to {Times(nearest.CdHi ?? 0)} times BC";
            return $"No harmonic pattern matches. {measures}. The nearest is the {nearest.Name}, but {why}.";
        }

        private static string Range(double lo, double hi) => lo == hi ? Pct(lo) : $"{Pct(lo)} to {Pct(hi)}";

        // ── Head and shoulders ──────────────────────────────────────────────────

        /// <summary>
        /// A top when the head is a high (inverse when it is a low). The head must go beyond both
        /// shoulders, and the neckline points must sit on the other side of both shoulders. The
        /// target is the head's distance from the neckline, measured from where price breaks it.
        /// </summary>
        public static string HeadAndShoulders(IReadOnlyList<(DateTime Date, double Price)> p, IReadOnlyList<Ohlcv>? data)
        {
            if (!Alternates(p)) return NotAlternating;
            double ls = p[0].Price, ln = p[1].Price, head = p[2].Price, rn = p[3].Price, rs = p[4].Price;
            bool top = head > ln;
            string name = top ? "Head and shoulders top" : "Inverse head and shoulders";
            if (top ? !(head > ls && head > rs) : !(head < ls && head < rs))
                return $"Not a head and shoulders: the head at {Price(head)} is not {(top ? "above" : "below")} both shoulders, at {Price(ls)} and {Price(rs)}.";

            double beyondL = Math.Abs(head - ls) / Math.Abs(ls), beyondR = Math.Abs(head - rs) / Math.Abs(rs);
            double shoulders = Math.Abs(ls - rs) / Math.Max(Math.Abs(ls), Math.Abs(rs));
            var said = new List<string>
            {
                $"{name}. The head is {Pct(beyondL)} {(top ? "above" : "below")} the left shoulder and {Pct(beyondR)} {(top ? "above" : "below")} the right; the shoulders are within {Pct(shoulders)} of each other.",
            };
            if (data == null || data.Count == 0) return said[0];

            int iLn = Bar(data, p[1].Date), iRn = Bar(data, p[3].Date), iHead = Bar(data, p[2].Date), iRs = Bar(data, p[4].Date);
            if (iLn < 0 || iRn <= iLn) return said[0];
            double slope = (rn - ln) / (iRn - iLn);
            double Neck(int i) => ln + slope * (i - iLn);
            double height = Math.Abs(head - Neck(iHead));
            string tilt = Math.Abs(rn - ln) / Math.Abs(ln) < 0.002 ? "flat"
                : rn > ln ? "rising" : "falling";
            said.Add($"The neckline is {tilt}.");

            int broke = -1;
            for (int i = Math.Max(iRs, 0) + 1; i < data.Count; i++)
                if (top ? data[i].Close < Neck(i) : data[i].Close > Neck(i)) { broke = i; break; }
            if (broke >= 0)
            {
                double target = top ? Neck(broke) - height : Neck(broke) + height;
                said.Add($"Price closed {(top ? "below" : "above")} the neckline on {SpeechTimeFormatter.FormatLongDate(data[broke].Date)}, at {Price(data[broke].Close)}; the measured target is {Price(target)}.");
            }
            else
            {
                int last = data.Count - 1;
                double target = top ? Neck(last) - height : Neck(last) + height;
                said.Add($"Not confirmed: no close {(top ? "below" : "above")} the neckline yet. It is at {Price(Neck(last))} on the last bar; a break there targets {Price(target)}.");
            }
            return string.Join(" ", said);
        }

        // ── Triangle ────────────────────────────────────────────────────────────

        /// <summary>
        /// Which triangle four alternating points make, from the slopes of its two sides. A side
        /// counts as flat when it moves less than a tenth of the triangle's opening height.
        /// </summary>
        public static string Triangle(IReadOnlyList<(DateTime Date, double Price)> p, IReadOnlyList<Ohlcv>? data)
        {
            if (!Alternates(p)) return NotAlternating;
            bool aHigh = p[0].Price > p[1].Price;
            var (h1, h2) = aHigh ? (p[0], p[2]) : (p[1], p[3]);
            var (l1, l2) = aHigh ? (p[1], p[3]) : (p[0], p[2]);
            double opening = Math.Abs(p[0].Price - p[1].Price);
            if (opening == 0) return NotAlternating;
            int Dir(double from, double to) => Math.Abs(to - from) < 0.1 * opening ? 0 : Math.Sign(to - from);
            int up = Dir(h1.Price, h2.Price), lo = Dir(l1.Price, l2.Price);

            string kind = (up, lo) switch
            {
                (0, 1) => "Ascending triangle: a flat top and rising lows, usually resolved upward",
                (-1, 0) => "Descending triangle: falling highs and a flat bottom, usually resolved downward",
                (-1, 1) => "Symmetrical triangle: falling highs and rising lows; it breaks either way",
                (1, -1) => "Expanding triangle: rising highs and falling lows, a broadening formation",
                (1, 1) => "Rising wedge: highs and lows both rising",
                (-1, -1) => "Falling wedge: highs and lows both falling",
                (0, 0) => "Rectangle: flat highs and flat lows, a range",
                (0, -1) => "Expanding triangle with a flat top: falling lows",
                (1, 0) => "Expanding triangle with a flat bottom: rising highs",
                _ => "Triangle",
            };
            if (kind.StartsWith("Rising wedge") || kind.StartsWith("Falling wedge"))
            {
                double su = h2.Price - h1.Price, sl = l2.Price - l1.Price;
                bool converging = kind.StartsWith("Rising") ? sl > su : su < sl;
                kind += converging ? ", narrowing" : ", widening";
            }
            var said = new List<string> { kind + "." };
            if (data == null || data.Count == 0) return said[0];

            int ih1 = Bar(data, h1.Date), ih2 = Bar(data, h2.Date), il1 = Bar(data, l1.Date), il2 = Bar(data, l2.Date);
            if (ih2 == ih1 || il2 == il1 || ih1 < 0 || il1 < 0) return said[0];
            double mu = (h2.Price - h1.Price) / (ih2 - ih1), ml = (l2.Price - l1.Price) / (il2 - il1);
            double Upper(int i) => h1.Price + mu * (i - ih1);
            double Lower(int i) => l1.Price + ml * (i - il1);
            int lastPoint = Math.Max(ih2, il2);

            if (mu < ml)
            {
                // Upper(i) = Lower(i)  ⇒  i = (l1 − h1 + mu·ih1 − ml·il1) / (mu − ml)
                double apex = (l1.Price - h1.Price + mu * ih1 - ml * il1) / (mu - ml);
                int after = (int)Math.Round(apex) - lastPoint;
                if (after > 0) said.Add($"The sides meet {after} bars after the last point, at {Price(Upper((int)Math.Round(apex)))}.");
            }

            for (int i = lastPoint + 1; i < data.Count; i++)
            {
                if (data[i].Close > Upper(i)) { said.Add($"Price closed above the upper side on {SpeechTimeFormatter.FormatLongDate(data[i].Date)}."); return string.Join(" ", said); }
                if (data[i].Close < Lower(i)) { said.Add($"Price closed below the lower side on {SpeechTimeFormatter.FormatLongDate(data[i].Date)}."); return string.Join(" ", said); }
            }
            if (lastPoint < data.Count - 1)
                said.Add($"Price is still inside, between {Price(Lower(data.Count - 1))} and {Price(Upper(data.Count - 1))}.");
            return string.Join(" ", said);
        }

        // ── Elliott impulse ─────────────────────────────────────────────────────

        /// <summary>
        /// The three rules an impulse may not break — wave 2 never goes past the start of wave 1,
        /// wave 3 is never the shortest of 1, 3 and 5, wave 4 never enters wave 1's price range —
        /// then the usual proportions, as measured.
        /// </summary>
        public static string Elliott(IReadOnlyList<(DateTime Date, double Price)> p)
        {
            if (!Alternates(p)) return NotAlternating;
            double p0 = p[0].Price, p1 = p[1].Price, p2 = p[2].Price, p3 = p[3].Price, p4 = p[4].Price, p5 = p[5].Price;
            bool up = p1 > p0;
            double w1 = Math.Abs(p1 - p0), w2 = Math.Abs(p2 - p1), w3 = Math.Abs(p3 - p2),
                   w4 = Math.Abs(p4 - p3), w5 = Math.Abs(p5 - p4);
            if (w1 == 0 || w3 == 0) return NotAlternating;

            var broken = new List<string>();
            if (up ? p2 <= p0 : p2 >= p0)
                broken.Add($"wave 2 went past the start of wave 1 ({Price(p2)} against {Price(p0)})");
            if (w3 < w1 && w3 < w5)
                broken.Add("wave 3 is the shortest of waves 1, 3 and 5");
            if (up ? p4 <= p1 : p4 >= p1)
                broken.Add($"wave 4 entered wave 1's range ({Price(p4)} against wave 1's end at {Price(p1)})");

            string measures = $"Wave 2 retraced {Pct(w2 / w1)} of wave 1, wave 3 is {Times(w3 / w1)} times wave 1, "
                            + $"wave 4 retraced {Pct(w4 / w3)} of wave 3, and wave 5 is {Times(w5 / w1)} times wave 1.";
            string dir = up ? "up" : "down";
            if (broken.Count > 0)
                return $"Not an impulse: {string.Join("; ", broken)}. {measures}";
            string extended = w3 > 1.618 * Math.Max(w1, w5) * (1 - Tolerance) ? " Wave 3 is the extended wave." : "";
            return $"Elliott impulse {dir}: all three rules hold. {measures}{extended}";
        }
    }
}
