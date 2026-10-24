using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// <b>A restored chart's visibility follows the indicator, except where the user changed it.</b>
    ///
    /// <para>Whether a component (or a cloud fill) is shown is saved per chart, and restore puts the
    /// saved flag back. Until 2026-10-17 it put it back VERBATIM, so a provider that changed a
    /// default visibility reached only charts added afterwards: Cody's Cipher B, saved while its
    /// divergences, red and gold dots and Anchor Fill were shown by default, would have kept showing
    /// — and sounding — every one of them after they were hidden to match Market Cipher B. The same
    /// shape as the levels bug that day (<c>SeriesManagementService.HealSavedLevels</c>).</para>
    ///
    /// <para>The rule: a saved flag EQUAL to the default it was saved against was never the user's
    /// choice, so it takes the indicator's CURRENT default; a flag that differs is the user's (H,
    /// Properties, Ctrl+Alt+Shift+K) and is kept. The default it was saved against is recorded beside
    /// it (<c>ComponentConfig.ProviderIsVisible</c>, <c>CloudFillConfig.ProviderIsVisible</c>). A save
    /// from before that record existed knows only the defaults listed below as RETIRED; for anything
    /// else the saved flag is kept, which is exactly what restore always did. (Using the current
    /// default as the baseline would be the same thing: a flag equal to it would take it.)</para>
    ///
    /// <para>What this cannot tell apart: a user who hid a component and showed it again has the
    /// default back and follows the indicator from then on — the same answer levels give.</para>
    /// </summary>
    public static class ProviderDefaultVisibility
    {
        /// <summary>The visibility a restored component or fill takes.</summary>
        /// <param name="saved">The flag the workspace file holds.</param>
        /// <param name="savedAgainst">The provider default recorded beside it; null in older saves.</param>
        /// <param name="currentDefault">What the indicator declares now.</param>
        /// <param name="retiredDefault">For an older save, the default the provider is KNOWN to have
        /// had (<see cref="RetiredComponentDefault"/>); null = unknown.</param>
        public static bool Resolve(bool saved, bool? savedAgainst, bool currentDefault, bool? retiredDefault)
        {
            bool? baseline = savedAgainst ?? retiredDefault;
            return baseline is bool b && saved == b ? currentDefault : saved;
        }

        /// <summary>
        /// A component default visibility a provider has since changed, for saves that predate
        /// <c>ComponentConfig.ProviderIsVisible</c>. Only entries whose old default is known from
        /// the history of the provider belong here — the default in force BEFORE the record
        /// existed, even when the provider has since changed it again.
        /// </summary>
        public static bool? RetiredComponentDefault(string indicatorCode, string componentName) =>
            (indicatorCode.ToUpperInvariant(), componentName) switch
            {
                // Cipher B, 2026-10-17: shown by default since they were written; hidden to match
                // stock Market Cipher B (CipherBProvider, "Our own signals, HIDDEN by default").
                // (The gold dot, "Triple Confluence Buy", was here too until it was removed from
                // Cipher B on 2026-10-22; a save that still lists it has nothing to apply it to.)
                ("CIPHER_B", CipherBProvider.CompRed
                           or CipherBProvider.CompBullDiv or CipherBProvider.CompBearDiv
                           or CipherBProvider.CompHidBull or CipherBProvider.CompHidBear) => true,
                _ => null,
            };

        /// <summary>
        /// <b>A part that was hidden by default is now left out by default</b> (Cody, 2026-10-23:
        /// "components that should not show by default must be LEFT OUT … not added hidden and
        /// muted"). A chart saved before then holds such a part included, usually hidden; this says
        /// whether the restore should leave it out instead. Only when the indicator now leaves the
        /// part out by default, and the save is from before (the default recorded beside it is
        /// not "shown": every part left out by default is shown once included, so a save made
        /// since records true and its Include box is the user's). Then the part is left out
        /// when the save holds it hidden, or shown only because its retired default showed it
        /// (Cipher B's own signals before 2026-10-17); a part the user had shown stays, and so does
        /// one the user had given a background tint, which shades even while the part is hidden
        /// (Cipher B's Anchor Polarity on the price chart) and would stop if it were left out.
        /// </summary>
        /// <param name="nowDefaultIsEnabled">The indicator's <c>DefaultIsEnabled</c> for the part now.</param>
        /// <param name="savedIsVisible">The visibility the workspace file holds.</param>
        /// <param name="savedAgainst">The default recorded beside it; null in older saves.</param>
        /// <param name="retiredDefault">As for <see cref="Resolve"/>.</param>
        /// <param name="userTinted">The save holds a background tint the user chose, other than Off.</param>
        public static bool HiddenDefaultIsNowLeftOut(bool? nowDefaultIsEnabled, bool savedIsVisible, bool? savedAgainst, bool? retiredDefault,
            bool userTinted = false)
        {
            if (nowDefaultIsEnabled != false || savedAgainst == true || userTinted) return false;
            bool? baseline = savedAgainst ?? retiredDefault;
            return !savedIsVisible || baseline == true;
        }

        /// <summary>The same for cloud fills, keyed by their display name.</summary>
        public static bool? RetiredCloudFillDefault(string indicatorCode, string fillDisplayName) =>
            (indicatorCode.ToUpperInvariant(), fillDisplayName) switch
            {
                // Cipher B, 2026-10-17: both shown by default until that day. WT Fill was hidden in
                // the morning (65ea3d68), Anchor Fill in the evening.
                ("CIPHER_B", "Anchor Fill" or "WT Fill") => true,
                _ => null,
            };
    }
}
