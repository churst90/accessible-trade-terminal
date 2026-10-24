using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Moves a component between <see cref="SeriesConfig.Components"/> and
    /// <see cref="SeriesConfig.ExcludedComponents"/> — the Properties dialog's "Include" box. An
    /// included part goes back to its place in the indicator's own order, so the keyboard order is
    /// the same as if it had never been left out.
    /// </summary>
    public static class ComponentInclusion
    {
        /// <summary>
        /// Includes or leaves out <paramref name="name"/>. <paramref name="indicatorOrder"/> is the
        /// indicator's component names in metadata order. Returns false (and changes nothing) when
        /// the name is not found, or when leaving it out would leave the series with no parts.
        /// </summary>
        public static bool SetIncluded(SeriesConfig config, string name, bool include, IReadOnlyList<string> indicatorOrder)
        {
            var from = include ? config.ExcludedComponents : config.Components;
            var comp = from.FirstOrDefault(c => c.Name == name);
            if (comp == null) return false;
            if (!include && config.Components.Count == 1) return false;

            from.Remove(comp);
            comp.IsEnabled = include;
            if (!include) { config.ExcludedComponents.Add(comp); return true; }

            int Rank(string n) { int r = -1; for (int i = 0; i < indicatorOrder.Count; i++) if (indicatorOrder[i] == n) { r = i; break; } return r < 0 ? int.MaxValue : r; }
            int mine = Rank(name), at = config.Components.Count;
            for (int i = 0; i < config.Components.Count; i++)
                if (Rank(config.Components[i].Name) > mine) { at = i; break; }
            config.Components.Insert(at, comp);
            return true;
        }

        /// <summary>Every part, included or not, in the indicator's order — the dialog's list.</summary>
        public static IReadOnlyList<ComponentConfig> AllParts(SeriesConfig config, IReadOnlyList<string> indicatorOrder)
        {
            int Rank(string n) { for (int i = 0; i < indicatorOrder.Count; i++) if (indicatorOrder[i] == n) return i; return int.MaxValue; }
            return config.Components.Concat(config.ExcludedComponents)
                .Select((c, i) => (c, i)).OrderBy(z => Rank(z.c.Name)).ThenBy(z => z.i).Select(z => z.c).ToList();
        }
    }
}
