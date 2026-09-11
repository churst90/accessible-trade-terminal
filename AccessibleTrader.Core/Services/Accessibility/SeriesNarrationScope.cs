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

        /// <summary>
        /// Why turning narration on for this series will produce no speech, or null when it will.
        ///
        /// <para><b>The defect this closes, reported by Cody 2026-09-11:</b> <i>"I have narration
        /// on for volume but don't hear it."</i> Narration is SIGNAL-shaped — it announces marker
        /// components (dots, arrows, crosses), oscillator zone transitions and crossovers, overlay
        /// crosses and level crosses. A Volume histogram has none of those, so
        /// <c>IsAutoNarrated</c> was set, the scan ran on every bar, and nothing was ever found.
        /// The switch said "narrating" and meant it; there was simply nothing to narrate.</para>
        ///
        /// <para><b>Why that is a defect and not a limitation.</b> The user cannot tell silence
        /// that means "the market did nothing" from silence that means "this can never speak".
        /// The first is information; the second is a dead control. This class's own doc already
        /// commits to the rule — <i>"Nothing lands in a state where narration is 'on' and
        /// silent"</i> — and a series with no narratable content was exactly that state. It is the
        /// same shape as <c>BackgroundWatchability</c>, which exists so an alert that could never
        /// fire says so at the moment it is created rather than by never firing.</para>
        ///
        /// <para><b>And it names the way out.</b> A level crossing IS narratable on any non-price
        /// pane (see <c>AutoNarrationService.ScanLevelCrosses</c>), so a volume pane with a
        /// reference level on it narrates perfectly well. The sentence says so, because a refusal
        /// that does not tell you what would work is half an answer.</para>
        /// </summary>
        public static string? WhyNothingToNarrate(ChartSeries series)
        {
            // A drawing is not an indicator and never narrates; that is not a surprise worth a
            // sentence, and the toggle is not offered on one.
            if (series.IsDrawing) return null;

            bool hasMarker    = series.Components.Any(c => IsMarkerDisplay(c.DisplayType) && !c.UsesGradientSpeech);
            bool hasOscillator= series.Components.Any(c => c.DisplayType == ComponentDisplayType.Oscillator);
            // Level crossings only speak off the price pane — on Main they are the overlay-cross
            // path's job, which needs a line to cross against.
            bool isPricePane  = string.Equals(series.Pane, "Main", StringComparison.OrdinalIgnoreCase);
            bool hasLevels    = series.Levels.Any(l => l.IsVisible);
            // An overlay on the price pane crosses PRICE, which always exists.
            bool hasOverlay   = isPricePane && series.Components.Any(c =>
                                    c.DisplayType is ComponentDisplayType.Line or ComponentDisplayType.StepLine
                                                  or ComponentDisplayType.Area);

            if (hasMarker || hasOscillator || hasOverlay || (!isPricePane && hasLevels)) return null;

            return isPricePane
                ? $"{series.FriendlyName} has no signals to narrate."
                : $"{series.FriendlyName} has no signals to narrate. Press 0 to add a reference level and its crossings will speak.";
        }

        private static bool IsMarkerDisplay(ComponentDisplayType dt) => dt switch
        {
            ComponentDisplayType.Dot or ComponentDisplayType.Arrow
            or ComponentDisplayType.Diamond or ComponentDisplayType.TriangleUp
            or ComponentDisplayType.TriangleDown or ComponentDisplayType.Square
            or ComponentDisplayType.Cross or ComponentDisplayType.ZeroDot => true,
            _ => false
        };
    }
}
