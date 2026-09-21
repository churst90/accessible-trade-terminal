using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Audio
{
    /// <summary>
    /// Shared constants for the audio/sonification pipeline.
    /// </summary>
    internal static class AudioConstants
    {
        /// <summary>
        /// Pan-width denominator for stereo positioning. Always uses <c>ViewportLength</c> so
        /// audio pan tracks the bar's actual x-position on the canvas: a bar at local index
        /// <c>k</c> of a <c>ViewportLength</c>-slot canvas sits at visual fraction
        /// <c>(k + 0.5) / ViewportLength</c>, which <see cref="CalculatePan"/> maps to the
        /// same stereo position. Audio and visual stay in lockstep regardless of whether
        /// the right-margin future-space is visible (at live edge) or absent (panned back).
        /// </summary>
        internal static int ComputePanWidth(WorkspaceState state) =>
            Math.Max(1, state.ViewportLength);

        /// <summary>
        /// Display types whose non-NaN presence on a bar constitutes an active marker signal.
        /// Used by NavigationSonifier (cluster ticks), NavigationFeedbackManager (signal speech),
        /// and SpeechFormatter (SignalSpeechTemplate path).
        /// </summary>
        internal static readonly HashSet<ComponentDisplayType> MarkerDisplayTypes = new()
        {
            ComponentDisplayType.Dot,
            ComponentDisplayType.ZeroDot,
            ComponentDisplayType.Arrow,
            ComponentDisplayType.Diamond,
            ComponentDisplayType.TriangleUp,
            ComponentDisplayType.TriangleDown,
            ComponentDisplayType.Square,
            ComponentDisplayType.Cross,
        };

        /// <summary>
        /// Computes stereo pan position (-1.0 = full left, +1.0 = full right) from a
        /// zero-based relative index within a viewport of the given width.
        /// Returns 0.0 (centre) when viewportWidth &lt;= 1.
        ///
        /// <para>
        /// The renderer is the authority on where a bar *is*: <c>StandardRenderers</c> draws bar
        /// <c>i</c> at <c>(i * barWidth) + halfBar</c> with <c>barWidth = Width / ViewportLength</c>,
        /// i.e. at visual fraction <c>(i + 0.5) / ViewportLength</c>. This maps that fraction onto
        /// [-1, +1], so the pan of a bar is the pan of its pixel. Until 2026-08-25 this computed
        /// <c>2k/(N−1) − 1</c> instead — an edge-to-edge mapping that treats N as a count of
        /// *gaps* rather than of slots — while the doc comment above described the centre-of-slot
        /// form the renderer actually uses, and <c>LevelCrossingMonitor</c> implemented that form
        /// independently. Two formulas, and the lockstep claim documented against the wrong one.
        /// </para>
        /// </summary>
        internal static double CalculatePan(int relativeIndex, int viewportWidth)
        {
            if (viewportWidth <= 1) return 0.0;
            return Math.Clamp((2.0 * (relativeIndex + 0.5) / viewportWidth) - 1.0, -1.0, 1.0);
        }

        /// <summary>The bottom of the value-pitched band: the frequency a component sounds when
        /// it sits on the floor of its pane.</summary>
        internal const double PitchFloorHz = 200.0;

        /// <summary>The top of the value-pitched band: the frequency a component sounds when it
        /// sits on the ceiling of its pane.</summary>
        internal const double PitchCeilingHz = 1000.0;

        /// <summary>
        /// <b>The frequency a value-pitched component sounds at, given where it sits in its pane.</b>
        /// The companion of <see cref="CalculatePan"/> for the vertical axis, and the second half
        /// of a fix whose first half landed on 2026-09-18.
        ///
        /// <para>
        /// That first half made the eye and the ear share one normaliser: after it,
        /// <see cref="ChartMath.NormalizedPosition"/> answers "what fraction of the pane is this
        /// value at" identically for the renderer and for the sonification, on the linear scale
        /// and on the log one. What it did not touch was the step AFTER — turning that fraction
        /// into a pitch — which was <c>200 + n * 800</c>, linear in HERTZ. Pitch perception is
        /// logarithmic in frequency, so a linear ramp is not a straight line to the ear: the
        /// bottom of the pane got 200→400 Hz, a whole octave inside its first quarter, while the
        /// top got 800→1000, a mere major third across its last. <b>The same distance travelled
        /// up the pane sounded five times larger at the bottom than at the top</b>, so a price
        /// riding high in the window moved a lot on screen and barely at all in the ear.
        /// </para>
        ///
        /// <para>
        /// Exponential in the fraction — equivalently, LINEAR IN PITCH — is the mapping that makes
        /// equal fractions of the pane equal musical intervals, everywhere in the pane. The band is
        /// <see cref="PitchFloorHz"/>..<see cref="PitchCeilingHz"/>, exactly as before: 200 Hz is
        /// still the floor and 1000 Hz still the ceiling, so the extremes sound exactly as they
        /// always have and only the interior is redistributed. Across that span it is
        /// log2(1000/200) = 2.32 octaves, so one full pane is about 27.9 semitones and 1% of the
        /// pane is a shade over a quarter-tone — comfortably above the ear's discrimination
        /// threshold at every height, which is the property the linear ramp lost at the top.
        /// </para>
        ///
        /// <para>
        /// <b>This is also what makes the price range's WIDTH an honest cost rather than a
        /// trap.</b> With <c>WorkspaceState.ScalePriceOnly</c> off, adding a wide Bollinger band
        /// widens the pane's range and so compresses the price line into a smaller fraction of it.
        /// Under this mapping that compression is a constant transposition — a price line
        /// occupying a third of the pane gets a third of the octaves wherever in the pane it sits.
        /// Under the linear ramp the same compression cost five times as much pitch at the top of
        /// the pane as at the bottom, which is a penalty no user could predict or reason about.
        /// </para>
        /// </summary>
        /// <param name="normalizedPosition">0 = the floor of the pane, 1 = the ceiling. Values
        /// outside are clamped; <see cref="ChartMath.NormalizedPosition"/> already clamps, because
        /// audio has no off-canvas.</param>
        internal static double PitchForPosition(double normalizedPosition)
            => PitchForPosition(normalizedPosition, PitchFloorHz, PitchCeilingHz);

        /// <inheritdoc cref="PitchForPosition(double)"/>
        internal static double PitchForPosition(double normalizedPosition, double floorHz, double ceilingHz)
        {
            if (!double.IsFinite(normalizedPosition)) return floorHz;
            double n = Math.Clamp(normalizedPosition, 0.0, 1.0);
            // Degenerate bands (a caller passing a non-positive or inverted pair) fall back to the
            // linear form rather than to NaN out of Math.Pow on a negative ratio.
            if (floorHz <= 0 || ceilingHz <= 0 || ceilingHz <= floorHz)
                return floorHz + n * (ceilingHz - floorHz);
            return floorHz * Math.Pow(ceilingHz / floorHz, n);
        }

        /// <summary>
        /// Phase names for CandleColor display type (Cipher S and any future sentiment overlays).
        /// Index 0 = Max Fear … 10 = Max Euphoria.
        /// </summary>
        internal static readonly string[] PhaseNames =
        {
            "Max Fear", "Fear", "Concern", "Caution", "Mild Caution",
            "Neutral", "Mild Greed", "Greed", "High Greed", "Extreme Greed", "Max Euphoria"
        };
    }
}
