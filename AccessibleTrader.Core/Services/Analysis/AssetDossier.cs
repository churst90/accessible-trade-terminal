namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>
    /// Why a section or field has no value. The distinction is the point of the type.
    ///
    /// <para>
    /// A blank row is a bug. For a person reading by ear there is an enormous difference between
    /// "this company filed no Form 4s in 90 days" (a fact, and often the interesting one), "this
    /// metric does not apply to a coin", and "the source did not answer". Collapsing those into an
    /// empty cell throws away the most useful of the three and quietly implies the second.
    /// </para>
    /// </summary>
    public enum DossierStatus
    {
        /// <summary>A real value.</summary>
        Ok,

        /// <summary>The source answered and the answer is "none". This is information.</summary>
        NoData,

        /// <summary>The field is meaningless for this asset class — R&amp;D expense for a coin.</summary>
        NotApplicable,

        /// <summary>The source could not be reached, or is not configured. NOT the same as "none".</summary>
        Unavailable,
    }

    /// <param name="Source">
    /// Where the number came from, always. The standing rule for this whole layer is display
    /// everything with its provenance and score only what has earned it, so a field without a source
    /// is not shippable.
    /// </param>
    public record DossierField(
        string Label,
        string Value,
        string Source,
        DossierStatus Status = DossierStatus.Ok,
        string? Note = null);

    /// <param name="AsOf">
    /// How old this section is. Stated on every section rather than once for the dossier, because
    /// the parts age at wildly different rates — a filing count is days old, a chart read is
    /// seconds old, and presenting them as equally fresh would be misleading.
    /// </param>
    public record DossierSection(
        string Title,
        DossierStatus Status,
        IReadOnlyList<DossierField> Fields,
        DateTime? AsOf = null,
        string? StatusNote = null);

    /// <summary>
    /// One deterministic check. Arithmetic over a field that is already displayed — never a verdict
    /// from a black box, and never a prediction.
    ///
    /// <para>
    /// The standing prediction for this whole family, recorded before any of it was built: it works
    /// as a <b>veto</b>, not as a timing signal. Excluding illiquid, high-dilution, undisclosed-source
    /// assets should avoid losses; it should not pick winners. Nothing here is scored, ranked, or
    /// summed into a single number, because a single number would be read as a rating.
    /// </para>
    /// </summary>
    public record DossierFlag(string Check, bool Triggered, string Detail, string Source);

    public record AssetDossier(
        string Symbol,
        string AssetClass,
        string Headline,
        IReadOnlyList<DossierSection> Sections,
        IReadOnlyList<DossierFlag> Flags,
        DateTime BuiltUtc)
    {
        public int SectionsWithData => Sections.Count(s => s.Status == DossierStatus.Ok);
        public int SectionsEmpty => Sections.Count(s => s.Status != DossierStatus.Ok);
        public IReadOnlyList<DossierFlag> RaisedFlags => Flags.Where(f => f.Triggered).ToList();
    }

    /// <summary>
    /// Builds the spoken opening line.
    ///
    /// <para>
    /// THE ACCESSIBILITY REQUIREMENT THAT OUTRANKS THE LAYOUT: the modal opens with a spoken
    /// headline, not a table. A person reading by ear must never have to walk a table to discover
    /// whether anything is there. The headline therefore answers, in order: what asset, how fresh,
    /// how much of the dossier is populated, and what — if anything — is flagged.
    /// </para>
    /// </summary>
    public static class DossierHeadline
    {
        public static string Build(string symbol, string assetClass,
                                   IReadOnlyList<DossierSection> sections,
                                   IReadOnlyList<DossierFlag> flags)
        {
            int ok = sections.Count(s => s.Status == DossierStatus.Ok);
            int total = sections.Count;
            var raised = flags.Where(f => f.Triggered).ToList();

            var parts = new List<string> { $"{symbol}, {assetClass} dossier." };

            parts.Add(ok == 0
                ? "No section returned data."
                : $"{ok} of {total} sections have data.");

            var unavailable = sections.Where(s => s.Status == DossierStatus.Unavailable).ToList();
            if (unavailable.Count > 0)
                parts.Add($"{unavailable.Count} could not be reached: {string.Join(", ", unavailable.Select(s => s.Title))}.");

            var noData = sections.Where(s => s.Status == DossierStatus.NoData).ToList();
            if (noData.Count > 0)
                parts.Add($"{noData.Count} returned nothing: {string.Join(", ", noData.Select(s => s.Title))}.");

            // Flags last and counted rather than listed, so the headline stays short. The detail is
            // one tab away, and reading eleven checks aloud on open would guarantee the feature is
            // switched off.
            if (flags.Count == 0)
                parts.Add("No checks were run.");
            else if (raised.Count == 0)
                parts.Add($"None of the {flags.Count} checks raised a flag.");
            else
                parts.Add($"{raised.Count} of {flags.Count} checks raised a flag: {string.Join(", ", raised.Take(3).Select(f => f.Check))}"
                        + (raised.Count > 3 ? ", and others." : "."));

            return string.Join(" ", parts);
        }
    }
}
