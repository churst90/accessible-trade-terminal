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
        /// The component whose value is READ OUT at each bar close, or null when the series
        /// narrates by signal instead.
        ///
        /// <para><b>Cody, 2026-09-11:</b> <i>"pressing N should enable narration which is spoken
        /// on new bar closes as part of the indicator ladder ... I may be doing dishes but still
        /// want to keep an ear on the volume."</i> Narration is signal-shaped — markers,
        /// oscillator zones, overlay and level crossings — and a Volume pane has none of those,
        /// so until this pass N on it set a flag nothing could act on. The pass before this one
        /// made the switch say so; this one makes it do something.</para>
        ///
        /// <para><b>The rule.</b> A bar or histogram is a quantity per bar — volume, delta, on
        /// balance volume — and its value at the close IS the news. A line is a level, and the
        /// news about a level is what crossed it. So a series whose only narratable content is a
        /// bar-type component reads that value once per bar close; a series with markers or an
        /// oscillator keeps narrating its signals and reads nothing, because a running value
        /// under a signal ladder is the wall of speech the tiers exist to prevent. Bar-close
        /// ONLY: the reading never appears on an intra-bar tick and never in playback, which
        /// speaks signals and nothing else (<c>PlaybackNarration.SignalStepFor</c>).</para>
        ///
        /// <para>Honours the N selection the way every scan site does: a component the user
        /// deselected is not read. Levels do not change the answer — a level on a volume pane
        /// adds its crossings on top of the reading rather than replacing it.</para>
        /// </summary>
        public static ComponentConfig? ReadingComponent(ChartSeries series)
        {
            if (series.IsDrawing) return null;
            // A profile's one component is a Bar with NO per-bar data — the bins are the data —
            // so it is not a reading, and saying "value read at each bar close" about it was
            // the promise this class exists to refuse. It narrates by its levels instead
            // (NarrationScanner.ScanProfile).
            if (IsProfileSeries(series)) return null;
            if (series.Components.Any(c => IsMarkerDisplay(c.DisplayType) && !c.UsesGradientSpeech)) return null;
            if (series.Components.Any(c => c.DisplayType == ComponentDisplayType.Oscillator)) return null;

            return series.Components.FirstOrDefault(c =>
                IsReadingDisplay(c.DisplayType)
                && c.IsVisible && !c.IsMuted && !c.IsZoneLine && !c.UsesGradientSpeech
                && (!HasComponentSelection(series) || c.IsAutoNarrated));
        }

        /// <summary>
        /// Why turning narration on for this series will produce no speech, or null when it will.
        ///
        /// <para><b>The defect this closes, reported by Cody 2026-09-11:</b> <i>"I have narration
        /// on for volume but don't hear it."</i> Narration is SIGNAL-shaped — it announces marker
        /// components (dots, arrows, crosses), oscillator zone transitions and crossovers, overlay
        /// crosses and level crosses — and until <see cref="ReadingComponent"/> a Volume pane had
        /// none of those, so <c>IsAutoNarrated</c> was set, the scan ran on every bar, and nothing
        /// was ever found. Volume now reads its value instead; what remains here is the plain
        /// LINE off the price pane with no level to cross — an OBV, say — where the switch would
        /// still be a promise nothing keeps.</para>
        ///
        /// <para><b>Why that is a defect and not a limitation.</b> The user cannot tell silence
        /// that means "the market did nothing" from silence that means "this can never speak".
        /// The first is information; the second is a dead control. This class's own doc already
        /// commits to the rule — <i>"Nothing lands in a state where narration is 'on' and
        /// silent"</i> — and a series with no narratable content was exactly that state. It is the
        /// same shape as <c>BackgroundWatchability</c>, which exists so an alert that could never
        /// fire says so at the moment it is created rather than by never firing.</para>
        ///
        /// <para><b>And it names the way out, only where the way out works.</b> A level crossing
        /// is narratable on any non-price pane (<c>AutoNarrationService.ScanLevelCrosses</c>), but
        /// only against a component that has a reading — a line, bar or histogram. The advice to
        /// press 0 is given only when both hold, because a refusal that names a way out which
        /// does not work is worse than one that names none.</para>
        /// </summary>
        public static string? WhyNothingToNarrate(ChartSeries series)
        {
            // A drawing is not an indicator and never narrates; that is not a surprise worth a
            // sentence, and the toggle is not offered on one.
            if (series.IsDrawing) return null;
            if (IsProfileSeries(series)) return null;   // its levels speak: POC, value area
            if (ReadingComponent(series) != null) return null;

            bool hasMarker    = series.Components.Any(c => IsMarkerDisplay(c.DisplayType) && !c.UsesGradientSpeech);
            bool hasOscillator= series.Components.Any(c => c.DisplayType == ComponentDisplayType.Oscillator);
            // Level crossings only speak off the price pane — on Main they are the overlay-cross
            // path's job, which needs a line to cross against.
            bool isPricePane  = string.Equals(series.Pane, "Main", StringComparison.OrdinalIgnoreCase);
            bool hasLevels    = series.Levels.Any(l => l.IsVisible);
            // ...and only against a component that has a value to compare with the level.
            bool hasReading   = series.Components.Any(c => c.IsVisible && !c.IsZoneLine
                                    && (c.DisplayType == ComponentDisplayType.Line || IsReadingDisplay(c.DisplayType)));
            // An overlay on the price pane crosses PRICE, which always exists.
            bool hasOverlay   = isPricePane && series.Components.Any(c =>
                                    c.DisplayType is ComponentDisplayType.Line or ComponentDisplayType.StepLine
                                                  or ComponentDisplayType.Area);

            if (hasMarker || hasOscillator || hasOverlay || (!isPricePane && hasLevels && hasReading)) return null;

            return !isPricePane && hasReading
                ? $"{series.FriendlyName} has no signals to narrate. Press 0 to add a reference level and its crossings will speak."
                : $"{series.FriendlyName} has no signals to narrate.";
        }

        /// <summary>A volume or market profile — decided by its indicator code, which is what
        /// every add path agrees on (the two add paths disagree about the component's display
        /// type, Bar on one and Distribution on the other).</summary>
        public static bool IsProfileSeries(ChartSeries series)
            => series.IsProfile || ProfileAnchoring.IsProfileCode(series.IndicatorCode);

        /// <summary>
        /// What N promises for this series, in the confirmation the reducer speaks: signals, a
        /// reading at each close, or — for a profile — its levels. A switch that names an
        /// outcome is a promise, so the sentence names the outcome the scan can keep.
        /// </summary>
        public static string NarrationPromise(ChartSeries series)
        {
            if (IsProfileSeries(series))
                return "Price crossing the point of control and the value area, and point of control moves, at each bar close.";
            if (ReadingComponent(series) != null)
                return "Value read at each bar close.";
            return "";
        }

        /// <summary>A quantity per bar: volume, delta, a histogram. See <see cref="ReadingComponent"/>.</summary>
        public static bool IsReadingDisplay(ComponentDisplayType dt)
            => dt is ComponentDisplayType.Bar or ComponentDisplayType.Histogram;

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
