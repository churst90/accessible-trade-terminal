using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing
{
    /// <summary>
    /// Keeps a drawing's parts in step with the lines its calculator returns.
    ///
    /// <para>
    /// A drawing's parts were made once, when it was placed, from the keys its calculator
    /// returned then. Nothing made a part for a key that appeared later, and a line with no part
    /// is not drawn, heard, spoken or reached — so a fib saved before the levels changed would
    /// never show the new ones, and a fib given its own levels in Properties would lose every
    /// line that was not one of the old ones. Parts the user left out stay out; a part's colour
    /// and sound are kept for every key it still has.
    /// </para>
    /// </summary>
    public static class DrawingComponents
    {
        /// <summary>How solid a vertical line or time zone's full-height mark is drawn.</summary>
        public const int MarkAlpha = 230;

        /// <summary>Make <paramref name="config"/>'s parts the calculator's keys, in its order.
        /// True when anything changed.</summary>
        public static bool Sync(SeriesConfig config, DrawingType type, IEnumerable<string> keys,
            IIndicatorModelFactory? factory)
        {
            if (factory == null) return false;
            var existing = config.Components.ToDictionary(c => c.Name, StringComparer.Ordinal);
            var wanted = new List<ComponentConfig>();
            foreach (var key in keys)
            {
                if (existing.TryGetValue(key, out var comp)) wanted.Add(comp);
                else if (!config.IsExcluded(key)) wanted.Add(factory.CreateComponentConfig(type.ToString(), key));
            }
            // A vertical line and a fib time zone are one value on one bar each: a line part
            // draws nothing for a lone point, so these are drawn as full-height marks instead.
            if (type is DrawingType.VerticalLine or DrawingType.FibTimeZone)
                foreach (var c in wanted)
                    if (c.MarkLineAlpha <= 0) c.MarkLineAlpha = MarkAlpha;
            if (wanted.Select(c => c.Name).SequenceEqual(config.Components.Select(c => c.Name))) return false;
            config.Components.Clear();
            foreach (var c in wanted) config.Components.Add(c);
            return true;
        }
    }
}
