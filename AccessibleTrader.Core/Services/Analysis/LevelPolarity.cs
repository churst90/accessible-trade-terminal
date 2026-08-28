namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>
    /// The single place that decides whether a price level is RESISTANCE or SUPPORT.
    ///
    /// <para>
    /// This exists because the same one-line invariant was got wrong twice in three weeks, by two
    /// different wrong proxies, and each was fixed only at the site where it was noticed:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><description>
    /// <b>By sound.</b> The zone announcement in <c>NavigationFeedbackManager</c> classified a
    /// level with <c>if ((float)comp.BaseFrequency >= 500f)</c>. <c>BaseFrequency</c> is a
    /// SONIFICATION setting — a zone line whose tone was chosen for audibility rather than for
    /// semantics was announced as the opposite structural level, and the magic 500 was undocumented.
    /// </description></item>
    /// <item><description>
    /// <b>By spelling.</b> <c>AutoNarrationService</c> classified a level with two literal
    /// <c>Contains</c> calls, <c>"Resistance"</c> and <c>"resistance"</c>. A component named
    /// <c>RESISTANCE_1</c> or <c>res_upper</c> fell through to the else arm, so its break was
    /// announced as <i>"Support at 61,200 broken."</i>
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// Neither proxy is a property of the market. The invariant is: <b>a level above the price is a
    /// ceiling and a level below it is a floor.</b> It needs no constant, it cannot be broken by
    /// re-voicing an indicator or by a provider's choice of component name, and it is the most
    /// consequential single word this application says — "near resistance at X" versus "near
    /// support at X" is a directional claim a trader acts on, with no visual to catch it.
    /// </para>
    ///
    /// <para>
    /// <b>Pick the reference price deliberately.</b> Polarity is relative to a price, and the right
    /// price is the one the statement is about. For an approach, a touch or a cross that is the
    /// CURRENT close. For a <i>break</i> it is the close at the last bar on which the level still
    /// existed — a break is by definition the moment price crossed the level, so measuring a break
    /// against the current close inverts the answer every single time. <c>AutoNarrationService</c>
    /// keeps the last close per zone line for exactly this reason.
    /// </para>
    ///
    /// <para>
    /// Enforced by <c>LevelPolarityScanTests</c>, which fails any speech or narration path that
    /// decides polarity by name matching or by frequency instead of calling in here.
    /// </para>
    /// </summary>
    public static class LevelPolarity
    {
        /// <summary>
        /// True when <paramref name="level"/> sits at or above <paramref name="referencePrice"/> —
        /// a ceiling. False when it sits below — a floor.
        /// </summary>
        /// <param name="level">The price the level sits at.</param>
        /// <param name="referencePrice">
        /// The price to judge it against; see the remarks on the class about choosing it. Pass the
        /// close at the bar the statement describes, not always the latest close.
        /// </param>
        /// <remarks>
        /// A level exactly ON the price is called resistance. The tie has to go somewhere and the
        /// pre-existing behaviour on both sides of the fix (drawn levels, the zone announcement)
        /// already resolved it that way; keeping it means this chokepoint changed no case that was
        /// previously right. <see cref="double.NaN"/> on either argument returns false, because
        /// every comparison against NaN is false — callers guard for NaN before they get here, and
        /// a level with no value is not a claim worth speaking.
        /// </remarks>
        public static bool IsResistance(double level, double referencePrice) => level >= referencePrice;

        /// <summary>
        /// The spoken noun for a level, so a caller never has to re-derive the pairing.
        /// Lower case: every call site to date embeds it mid-sentence or capitalises the sentence
        /// itself.
        /// </summary>
        public static string Word(double level, double referencePrice)
            => IsResistance(level, referencePrice) ? "resistance" : "support";
    }
}
