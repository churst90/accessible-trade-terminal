using System.Globalization;
using System.Text.RegularExpressions;
using AccessibleTrader.Core.Services.Drawing;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// How a DRAWING reads out while the user arrows along it.
    ///
    /// <para>
    /// Before 2026-09-03 a trend line said <c>"Line, line, 150.50"</c> — the generic
    /// <c>{name}, {type}, {value}</c> fallback meeting a component whose name IS its type — and a
    /// rectangle one bar outside its span said <c>"Top, line, no data"</c>. Neither told the user
    /// the one thing they needed: WHERE ON THE DRAWING THEY WERE. A trader who has drawn a line
    /// is asking "am I past the end of it yet", and no amount of price could answer that.
    /// </para>
    ///
    /// <para>
    /// The sentence is <c>{value}[, {position}][, {relation}].</c> — value first because whatever
    /// interrupts cuts the end, and slots 2 and 3 both omitted in the common case, so sweeping
    /// forty bars is usually two items long.
    /// </para>
    ///
    /// <para>Callers: <c>DrawingComponentStrategy</c> in <see cref="SpeechFormatter"/> speaks
    /// the per-bar sentence; <c>NavigationFeedbackManager</c> builds the series-switch and
    /// component-change prefixes from <see cref="SpokenSeriesName(ChartSeries)"/> and
    /// <see cref="SpokenComponentName"/>; the anchor nudge names the drawing through the same
    /// helper, so one drawing has one spoken name whichever key produced it.</para>
    /// </summary>
    internal static class DrawingSpeech
    {
        /// <summary>
        /// THE SPAN IS GEOMETRY, NEVER ARRAY LENGTH — and that distinction is the whole reason
        /// this type exists rather than a one-line check at the call site.
        ///
        /// <para>
        /// The obvious implementation of "am I past the end" is "does the component array reach
        /// this bar". It is wrong, and it is worse than the "no data" it replaces. A drawing's
        /// array can lag the bars (that was <c>IndicatorOrchestrator</c>'s per-tick skip, fixed
        /// the same day), and a trend line is DENSE — it has a value at every bar. An
        /// array-length test would therefore stand a trader at the live edge and announce
        /// "past end, 4 bars" about a line running through the bar they are standing on: a false
        /// statement about the user's own drawing, in the same confident grammar as a true one.
        /// "No data" at least sounded like a fault.
        /// </para>
        ///
        /// <para>
        /// So the span comes from the anchor DATES, resolved to bar indices by nearest match. And
        /// the two failure modes stay distinguishable: outside the geometric span is
        /// "Before start, 20 bars."; inside it with no value is "Not yet calculated.", which is
        /// deliberately odd-sounding so the day it happens it gets reported.
        /// </para>
        /// </summary>
        internal readonly record struct Position(PositionKind Kind, string? SlotName, int BarDistance);

        internal enum PositionKind
        {
            /// <summary>The type has no date anchors at all (a fib, a horizontal line): every bar
            /// is equally "on" it, so there is nothing to say.</summary>
            Unbounded,
            /// <summary>Strictly between the outermost anchors, and not on one.</summary>
            Between,
            /// <summary>Standing on an anchor's bar. <c>SlotName</c> is set.</summary>
            OnAnchor,
            BeforeStart,
            PastEnd,
        }

        /// <summary>
        /// Where this bar sits relative to the drawing's own anchors.
        ///
        /// <para>
        /// <paramref name="bars"/> may be null or empty — the position clause is then dropped
        /// rather than guessed, because every one of these answers depends on knowing which bar
        /// an anchor date lands on. A dropped clause is a shorter sentence; a guessed one is a
        /// lie about where the user is standing.
        /// </para>
        /// </summary>
        internal static Position Locate(DrawingData drawing, int dataIndex, IReadOnlyList<Ohlcv>? bars)
        {
            if (bars == null || bars.Count == 0 || dataIndex < 0) return new(PositionKind.Between, null, 0);

            var dated = new List<(int Slot, int Index)>();
            foreach (int slot in DrawingAnchorSchema.Slots(drawing.Type))
            {
                if (!DrawingAnchorSchema.Uses(drawing.Type, slot, DrawingAnchorAxis.Date)) continue;
                DateTime? d = slot switch
                {
                    1 => drawing.AnchorDate1,
                    2 => drawing.AnchorDate2,
                    3 => drawing.AnchorDate3,
                    _ => null,
                };
                if (d.HasValue) dated.Add((slot, NearestBar(bars, d.Value)));
            }

            // A fib, a horizontal line, a risk/reward: price-only anchors, so the drawing is a
            // level across the whole chart and "before start" is not a thing that can be true.
            if (dated.Count == 0) return new(PositionKind.Unbounded, null, 0);

            foreach (var (slot, index) in dated)
                if (index == dataIndex)
                    return new(PositionKind.OnAnchor, DrawingAnchorSchema.SlotName(drawing.Type, slot), 0);

            int first = dated.Min(a => a.Index);
            int last  = dated.Max(a => a.Index);
            if (dataIndex < first) return new(PositionKind.BeforeStart, null, first - dataIndex);
            if (dataIndex > last)  return new(PositionKind.PastEnd,     null, dataIndex - last);
            return new(PositionKind.Between, null, 0);
        }

        /// <summary>
        /// The bar nearest an anchor date. NOT <c>BarIndexOf</c>, which answers 0 for any date
        /// before the loaded range (recorded defect n8) — a fallback that turns "your anchor is
        /// off-screen to the left" into "your anchor is the first bar on the chart", which is the
        /// kind of confident wrong answer this whole file is written against. Nearest is honest at
        /// both ends: an anchor left of the data clamps to bar 0 because bar 0 genuinely is the
        /// closest loaded bar to it.
        /// </summary>
        private static int NearestBar(IReadOnlyList<Ohlcv> bars, DateTime when)
        {
            int best = 0;
            long bestGap = long.MaxValue;
            for (int i = 0; i < bars.Count; i++)
            {
                long gap = Math.Abs((bars[i].Date - when).Ticks);
                if (gap < bestGap) { bestGap = gap; best = i; }
            }
            return best;
        }

        /// <summary>The position clause, or null when there is nothing to say about position.</summary>
        internal static string? PositionClause(Position p) => p.Kind switch
        {
            // "at end anchor", not "end anchor": mid-sentence, most synthesisers reduce "end"
            // and "and" to the same sound before a consonant, and "170.50, and anchor, price
            // above" is heard as a conjunction — the one distinction this clause exists to give
            // (end, not start) is the one that gets lost. A leading preposition cannot be a
            // conjunction. Every slot gets it, so the grammar never changes shape.
            PositionKind.OnAnchor    => $"at {p.SlotName} anchor",
            PositionKind.BeforeStart => "before start",
            PositionKind.PastEnd     => "past end",
            _                        => null,
        };

        /// <summary>
        /// The sentence for a bar the drawing has no value at.
        ///
        /// <para>
        /// Value-first cannot apply because there is no value, so the position word leads — which
        /// is right, since the position word is the answer to the question being asked. The bar
        /// COUNT rather than a date or a duration: arrow keys move bars, and "starts in 3 days"
        /// over a weekend is a lie about how many keypresses it takes.
        /// </para>
        /// </summary>
        internal static string NoValueSentence(Position p) => p.Kind switch
        {
            PositionKind.BeforeStart => $"Before start, {Bars(p.BarDistance)}",
            PositionKind.PastEnd     => $"Past end, {Bars(p.BarDistance)}",
            // Inside the geometry with no number behind it. Deliberately not phrased as geometry:
            // this is a fault, and it should sound like one so it gets reported rather than
            // absorbed as normal. StateChange wording, not Error — it is not the user's doing.
            _                        => "Not yet calculated.",
        };

        private static string Bars(int n) => n == 1 ? "1 bar." : $"{n} bars.";

        /// <summary>
        /// Where the CLOSE sits against the drawing, with price as the grammatical subject.
        ///
        /// <para>
        /// "price above", never "line above". The narrator already says "Price crossed above R1 at
        /// 103.50", and two speakers using opposite subjects for the same geometry would be a
        /// real comprehension hazard — identical words, inverted meaning, one keystroke apart.
        /// </para>
        ///
        /// <para>
        /// A cross REPLACES the plain side rather than adding a clause, so the sentence never
        /// grows: a cross is already the interesting case, and it is the one thing here that is a
        /// discrete event rather than a state.
        /// </para>
        /// </summary>
        internal static string? RelationClause(double drawingValue, double close, double? prevDrawingValue, double? prevClose)
        {
            if (double.IsNaN(drawingValue) || double.IsNaN(close)) return null;

            bool above = close > drawingValue;
            bool at = SpeechPriceFormatter.FormatPrice(close) == SpeechPriceFormatter.FormatPrice(drawingValue);

            if (prevDrawingValue is { } pv && prevClose is { } pc
                && !double.IsNaN(pv) && !double.IsNaN(pc))
            {
                bool wasAbove = pc > pv;
                if (wasAbove != above && !at)
                    return above ? "price crossed above" : "price crossed below";
            }

            // "at" is decided on the SPOKEN precision, not on ==. A line at 150.4999 under a
            // close of 150.5001 is "price above" by arithmetic and indistinguishable by ear from
            // "price at" once both are read as "150.50", so the ear is what the word matches.
            // "price on it", not "price at": a clause that ends on a preposition is heard as an
            // utterance cut off before its object — and on a line drawn through the closes this
            // is the COMMON case, spoken many times per sweep, not a rarity.
            if (at) return "price on it";
            return above ? "price above" : "price below";
        }

        /// <summary>
        /// What to CALL one component of a drawing in speech.
        ///
        /// <para>
        /// Empty string for a single-component drawing: its component's identity IS the series'
        /// identity, so there is nothing to distinguish it from and the name is pure overhead on
        /// every announcement. That covers over a third of the sixteen types — trend line,
        /// horizontal line, vertical line, anchored VWAP, measure tool, text label.
        /// </para>
        ///
        /// <para>
        /// Otherwise the visible name minus the longest trailing token sequence shared by ALL of
        /// this drawing's components, with a trailing <c>.0</c> dropped before <c>%</c>. ALL is
        /// load-bearing: a channel's "Lower Bound" / "Upper Bound" / "Median" share "Bound" in two
        /// of three, and a "strip what most share" rule would silently rename them to
        /// Lower / Upper / Median — arguably nicer, and a change nobody asked for made by a
        /// heuristic. Data-driven, so it needs no per-type list: seven fib levels become
        /// "0%", "23.6%", "38.2%", "50%", "61.8%", "78.6%", "100%", whose distinguishing syllable
        /// is FIRST, while a Gann box's "Level 0%" / "Time 0%" keep their leading token and stay
        /// distinguishable by ear.
        /// </para>
        ///
        /// <para>
        /// THIS IS A SPEECH RULE, NOT A RENAME. <c>DisplayName</c> is untouched: the Object Tree
        /// row and the Properties field still need their visible label, and shortening the visible
        /// text while the accessible name kept the long form would be a fresh WCAG 2.5.3
        /// Label-in-Name failure in the dialog where twelve of them were closed hours earlier.
        /// </para>
        /// </summary>
        internal static string SpokenComponentName(
            IReadOnlyList<ComponentConfig> components, ComponentConfig comp)
        {
            if (components == null || components.Count <= 1) return string.Empty;

            var tokenLists = components
                .Select(c => Tokens(Visible(c)))
                .Where(t => t.Length > 0)
                .ToList();
            if (tokenLists.Count != components.Count) return Normalise(Visible(comp));

            int minLen = tokenLists.Min(t => t.Length);
            int shared = 0;
            // `minLen - 1`: never strip a name down to nothing. A type whose components are all
            // called the same single word would otherwise announce the empty string.
            while (shared < minLen - 1)
            {
                string token = tokenLists[0][^(shared + 1)];
                if (!tokenLists.All(t => string.Equals(t[^(shared + 1)], token, StringComparison.OrdinalIgnoreCase)))
                    break;
                shared++;
            }

            var mine = Tokens(Visible(comp));
            if (mine.Length == 0) return string.Empty;
            int keep = Math.Max(1, mine.Length - shared);
            return Normalise(string.Join(" ", mine.Take(keep)));
        }

        private static string Visible(ComponentConfig c) =>
            !string.IsNullOrWhiteSpace(c.DisplayName) ? c.DisplayName! : (c.Name ?? string.Empty);

        private static string[] Tokens(string s) =>
            s.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        /// <summary>"0.0%" → "0%". Orca voices "0.0%" as "zero point zero percent" — three wasted
        /// syllables on each of seven names that are otherwise identical in their tails.</summary>
        private static string Normalise(string s) =>
            Regex.Replace(s, @"(\d)\.0(?=%)", "$1", RegexOptions.CultureInvariant);

        /// <summary>
        /// A drawing's series name as it should be SPOKEN: "Trend line 2", not "Trend line (2)".
        ///
        /// <para>
        /// The nudge readback already strips the parentheses; the series-switch announcement did
        /// not. Same object, two different spoken names depending on which key you pressed. One
        /// helper now, used by both.
        /// </para>
        /// </summary>
        internal static string SpokenSeriesName(string? name)
        {
            string n = (name ?? string.Empty).Trim();
            return Regex.Replace(n, @"\s*\((\d+)\)\s*$", " $1", RegexOptions.CultureInvariant).Trim();
        }

        /// <summary>
        /// The one spoken name for a drawing series, used by the nudge readback, the series
        /// switch and the undo label alike. <c>Name</c> ("Trend line (2)") with the ordinal
        /// spoken as a plain number; <c>FriendlyName</c> is "TrendLine Drawing" for every trend
        /// line and cannot tell two apart. Drawings saved before 2026-09-03 were named with the
        /// enum's CamelCase ("TrendLine (2)", which a screen reader voices as one word) and are
        /// mapped to the friendly vocabulary here rather than renamed in the workspace.
        /// </summary>
        internal static string SpokenSeriesName(ChartSeries series)
        {
            string name = string.IsNullOrWhiteSpace(series.Name) ? series.FriendlyName : series.Name;
            if (series.Drawing != null)
            {
                string raw = series.Drawing.Type.ToString();
                if (name.StartsWith(raw, StringComparison.Ordinal))
                    name = DrawingInteractionManager.FriendlyName(series.Drawing.Type) + name[raw.Length..];
            }
            return SpokenSeriesName(name);
        }
    }
}
