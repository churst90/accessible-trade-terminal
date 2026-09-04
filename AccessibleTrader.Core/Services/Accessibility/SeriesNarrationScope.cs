using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// THE ONE PLACE that decides whether a given component is allowed to speak unprompted.
    ///
    /// <para>
    /// Narration used to be a per-SERIES flag and nothing else, so a Cipher B with eleven
    /// components was all-or-nothing: switch it on for the divergence you care about and you
    /// also get every cross, every dot and every band it prints. Cody asked for the component
    /// level (2026-09-04) for the same reason mute and hide have it.
    /// </para>
    ///
    /// <para>
    /// <b>The rule, and why it is not simply an AND.</b> The series flag stays the master —
    /// nothing in a series that is not narrating ever speaks. Under it, the component flags are
    /// a SELECTION, and an empty selection means "all of them", not "none of them". Two reasons
    /// it has to work that way:
    /// </para>
    /// <list type="number">
    ///   <item>Every series that exists today has narration on with no component flagged. An AND
    ///         would silence all of them on upgrade — a feature deleting itself in a release
    ///         nobody would connect to the change.</item>
    ///   <item>It makes N on a series and N on a component compose the way a user expects.
    ///         Turning the series on gives you everything; then pressing N on one component
    ///         narrows to it, and pressing N on it again widens back out. Nothing lands in a
    ///         state where narration is "on" and silent.</item>
    /// </list>
    /// </summary>
    public static class SeriesNarrationScope
    {
        /// <summary>
        /// Whether the series may narrate at all: flagged, visible and unmuted. A component
        /// producing no tone must not be the only thing that speaks, and the same holds for the
        /// series carrying it.
        /// </summary>
        public static bool SeriesNarrates(ChartSeries series)
            => series.IsAutoNarrated && series.IsVisible && !series.IsMuted;

        /// <summary>True when at least one component of the series has been singled out.</summary>
        public static bool HasComponentSelection(ChartSeries series)
            => series.Components.Any(c => c.IsAutoNarrated);

        /// <summary>
        /// Whether <paramref name="component"/> narrates. Assumes the caller has already applied
        /// its own visibility and mute rules — this answers only the narration question.
        /// </summary>
        public static bool ComponentNarrates(ChartSeries series, ComponentConfig component)
            => SeriesNarrates(series)
               && (!HasComponentSelection(series) || component.IsAutoNarrated);
    }
}
