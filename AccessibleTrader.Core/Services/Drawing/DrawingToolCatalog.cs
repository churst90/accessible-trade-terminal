using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing
{
    /// <summary>
    /// Every drawing tool, in the groups the Drawing Tools list (Alt+D) shows them in.
    ///
    /// <para>
    /// The list used to be its own table of eleven names, so the Gann fan, Gann box, angle fib
    /// and risk/reward tools existed, drew and spoke, and could only be reached by a shortcut
    /// nobody had been told about. The list is now built from here, and a test holds that every
    /// <see cref="DrawingType"/> is in it exactly once.
    /// </para>
    /// </summary>
    public static class DrawingToolCatalog
    {
        /// <summary>One tool: the key the add-drawing event carries, its name, how
        /// many points it takes, and one line on what it is for.</summary>
        public sealed record Tool(string Key, DrawingType Type, string Name, int Points, string Hint);

        public sealed record Group(string Name, IReadOnlyList<Tool> Tools);

        private static Tool T(string key, string hint)
        {
            var type = DrawingInteractionManager.ToolType(key);
            return new Tool(key, type, DrawingInteractionManager.FriendlyName(type),
                DrawingInteractionManager.PointCount(type), hint);
        }

        public static readonly IReadOnlyList<Group> Groups = new[]
        {
            new Group("Lines", new[]
            {
                T("TrendLine", "From one point through another and on to the right; Properties can extend it left or stop it at the second point."),
                T("Horizontal", "One price across the whole chart."),
                T("Vertical", "One date, top to bottom."),
                T("Channel", "Two points for a trend line, then a parallel line; set its width in Properties."),
            }),
            new Group("Fibonacci", new[]
            {
                T("FibRetracement", "Swing start, then swing end: the levels a pullback stops at, 0 at the swing end."),
                T("FibExtension", "Trend-based: swing start, swing end, then the pullback low or high; where the next leg may reach."),
                T("FibTimeZone", "A start and one unit of bars: lines at 1, 2, 3, 5, 8, 13… units on."),
                T("AngleFib", "A start point and a second point; fan lines at the fib fractions of that slope."),
            }),
            new Group("Patterns", new[]
            {
                T("Abcd", "Four swing points; says whether C and D fit an ABCD and if AB equals CD."),
                T("Xabcd", "Five swing points; names the harmonic it fits — Gartley, Bat, Butterfly, Crab, Cypher — or the nearest."),
                T("HeadAndShoulders", "Shoulder, neckline, head, neckline, shoulder; says the neckline break and the target."),
                T("Triangle", "Two highs and two lows, alternating; names the triangle or wedge and where its sides meet."),
                T("Elliott", "The start and the ends of waves 1 to 5; checks the three impulse rules."),
            }),
            new Group("Pitchfork and Gann", new[]
            {
                T("Pitchfork", "A pivot then the swing's high and low; a median line with two parallels."),
                T("GannFan", "A start point and a second point; lines at the Gann angles of that slope."),
                T("GannBox", "A box of time and price divisions between two corners."),
            }),
            new Group("Measure and mark", new[]
            {
                T("Measure", "Two points: the change in price, percent and bars between them."),
                T("RiskReward", "Entry, stop loss, take profit: the risk to reward ratio."),
                T("Rectangle", "A price range over a date range, between two corners."),
                T("AnchoredVwap", "The volume-weighted average price from one bar on."),
                T("TextLabel", "A note on one bar and price."),
            }),
        };

        public static IEnumerable<Tool> All => Groups.SelectMany(g => g.Tools);
    }
}
