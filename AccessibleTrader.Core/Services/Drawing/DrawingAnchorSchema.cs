using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing
{
    /// <summary>One editable coordinate of a drawing: which slot, which axis, and what to call it.</summary>
    /// <param name="Slot">1, 2 or 3 — the anchor slot on <see cref="DrawingData"/>.</param>
    /// <param name="Axis">Price or Date.</param>
    /// <param name="Label">
    /// What this coordinate is called to a user, in the vocabulary of THIS drawing type.
    /// A RiskReward's three prices are Entry, Stop and Target, not "anchor 1/2/3 price" — and a
    /// user reading a screen reader's form-field list has nothing but the label to go on.
    /// </param>
    public readonly record struct DrawingAnchorField(int Slot, DrawingAnchorAxis Axis, string Label);

    public enum DrawingAnchorAxis { Price, Date }

    /// <summary>
    /// Which coordinates each <see cref="DrawingType"/> actually has, in the order a user should
    /// meet them.
    ///
    /// <para><b>Why this exists as data rather than as a condition in the editor.</b> The
    /// properties dialog decided which coordinate fields to render from four hard-coded type
    /// lists, and the lists were wrong in three ways at once: seven of the sixteen drawing types
    /// appeared in none of them and so had NO keyboard route to their own coordinates at all
    /// (GannFan, RiskReward, AnchoredVwap, MeasureTool, GannBox, AndrewsPitchfork, AngleFib);
    /// slot 3 was never offered, so a FibExtension's projection origin and a RiskReward's target
    /// could only be set with a ten-pixel mouse drag; and a TextLabel offered its date but not
    /// its price, which its calculator reads. For the audience this product exists for that is
    /// not a missing convenience, it is a drawing that can be created and then never
    /// corrected.</para>
    ///
    /// <para><b>Why not derive it from the live anchor values.</b> "Render a field for every
    /// non-null anchor" looks equivalent and is not, in both directions. Too few: a FibExtension
    /// abandoned after two clicks has a null slot 3, renders nothing on the chart, and would
    /// then offer no field to repair the very anchor that is missing. Too many:
    /// <c>DrawingInteractionManager</c>'s placement fallback writes slot 3 for every drawing type
    /// whether or not it has one, so a horizontal line would sprout a "Price 3" box that changes
    /// nothing. The schema says what a type HAS; the live values only fill the boxes in.</para>
    ///
    /// <para>The contents are not an opinion — they were read off the calculators, one file at a
    /// time, by which <c>AnchorPriceN</c> / <c>AnchorDateN</c> each one dereferences.
    /// <c>DrawingAnchorSchemaTests</c> re-derives that census from the calculator sources and
    /// fails if the two ever disagree, so adding a calculator that reads a new slot without
    /// declaring it here is a red build rather than a control that silently does not exist.</para>
    /// </summary>
    public static class DrawingAnchorSchema
    {
        private static DrawingAnchorField P(int slot, string label) => new(slot, DrawingAnchorAxis.Price, label);
        private static DrawingAnchorField D(int slot, string label) => new(slot, DrawingAnchorAxis.Date, label);

        private static readonly IReadOnlyDictionary<DrawingType, IReadOnlyList<DrawingAnchorField>> Fields =
            new Dictionary<DrawingType, IReadOnlyList<DrawingAnchorField>>
            {
                [DrawingType.None] = Array.Empty<DrawingAnchorField>(),

                [DrawingType.HorizontalLine] = new[] { P(1, "Price") },
                [DrawingType.VerticalLine]   = new[] { D(1, "Date") },

                [DrawingType.TrendLine] = new[]
                    { P(1, "Start price"), D(1, "Start date"), P(2, "End price"), D(2, "End date") },
                [DrawingType.Channel] = new[]
                    { P(1, "Start price"), D(1, "Start date"), P(2, "End price"), D(2, "End date") },
                [DrawingType.MeasureTool] = new[]
                    { P(1, "Start price"), D(1, "Start date"), P(2, "End price"), D(2, "End date") },
                [DrawingType.AngleFib] = new[]
                    { P(1, "Start price"), D(1, "Start date"), P(2, "End price"), D(2, "End date") },
                [DrawingType.GannBox] = new[]
                    { P(1, "Start price"), D(1, "Start date"), P(2, "End price"), D(2, "End date") },
                [DrawingType.GannFan] = new[]
                    { P(1, "Origin price"), D(1, "Origin date"), P(2, "End price"), D(2, "End date") },

                [DrawingType.Rectangle] = new[]
                    { P(1, "Top price"), D(1, "Start date"), P(2, "Bottom price"), D(2, "End date") },

                // Both fibs are price-only: the calculators never read a date.
                [DrawingType.FibRetracement] = new[] { P(1, "Start price"), P(2, "End price") },
                [DrawingType.FibExtension]   = new[]
                    { P(1, "Start price"), P(2, "End price"), P(3, "Projection origin price") },

                // The label sits at a point in time AND at a price; the dialog offered only the date.
                [DrawingType.TextLabel] = new[] { P(1, "Position price"), D(1, "Position date") },

                // Three prices, and their names are the whole point of the tool.
                [DrawingType.RiskReward] = new[]
                    { P(1, "Entry price"), P(2, "Stop loss price"), P(3, "Take profit price") },

                [DrawingType.AnchoredVwap] = new[] { D(1, "Anchor date") },

                [DrawingType.AndrewsPitchfork] = new[]
                {
                    P(1, "Pivot 1 price"), D(1, "Pivot 1 date"),
                    P(2, "Pivot 2 price"), D(2, "Pivot 2 date"),
                    P(3, "Pivot 3 price"), D(3, "Pivot 3 date"),
                },
            };

        /// <summary>The editable coordinates of a drawing type, in presentation order. Empty for
        /// an unknown type rather than throwing — an unrecognised drawing should render no
        /// coordinate editors, not take the dialog down.</summary>
        public static IReadOnlyList<DrawingAnchorField> For(DrawingType type) =>
            Fields.TryGetValue(type, out var f) ? f : Array.Empty<DrawingAnchorField>();

        /// <summary>Every type the schema declares — the guard test enumerates this.</summary>
        public static IEnumerable<DrawingType> DeclaredTypes => Fields.Keys;

        /// <summary>True when this type uses the given slot on the given axis.</summary>
        public static bool Uses(DrawingType type, int slot, DrawingAnchorAxis axis) =>
            For(type).Any(f => f.Slot == slot && f.Axis == axis);
    }
}
