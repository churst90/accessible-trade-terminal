using System;
using System.Linq;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.StrategyLab.Catalogue;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The catalogue's honesty contract.
    ///
    /// <para>
    /// A spec with no recorded verdict is exactly what the terminal/lab split removed: something
    /// that looks endorsed because nothing says otherwise. These tests make an unrecorded spec a
    /// build failure, so the only way to add a strategy is to also state what is known about it —
    /// "Untested" being a perfectly good answer, and the honest one for most of them.
    /// </para>
    /// </summary>
    public class CatalogueProvenanceTests
    {
        [Fact]
        public void EverySpecHasProvenance()
        {
            var missing = StrategyCatalogue.AllSpecs()
                .Where(s => CatalogueProvenance.For(s.Id) == null)
                .Select(s => s.Id)
                .ToList();

            Assert.True(missing.Count == 0,
                "These catalogue specs have no provenance entry. Record what each was tested on, " +
                "which controls ran, and the verdict — 'Untested' is a valid and common answer:\n  " +
                string.Join("\n  ", missing));
        }

        [Fact]
        public void NoProvenanceEntryPointsAtASpecThatNoLongerExists()
        {
            var ids = StrategyCatalogue.AllSpecs().Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var orphans = CatalogueProvenance.RecordedIds.Where(id => !ids.Contains(id)).ToList();

            Assert.True(orphans.Count == 0,
                "Provenance recorded for spec ids that are not in the catalogue:\n  " + string.Join("\n  ", orphans));
        }

        [Fact]
        public void SpecIdsAreUnique()
        {
            // Ids are the join key between a lab run, a note and an imported spec, and the
            // importer skips an id it already holds — a duplicate would silently drop a spec.
            var dupes = StrategyCatalogue.AllSpecs()
                .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.True(dupes.Count == 0, "Duplicate spec ids: " + string.Join(", ", dupes));
        }

        [Fact]
        public void EveryVerdictActuallySaysSomething()
        {
            var thin = CatalogueProvenance.SpecsWithProvenance()
                .Where(s => s.Provenance!.Verdict.Length < 40
                         || string.IsNullOrWhiteSpace(s.Provenance!.TestedOn)
                         || string.IsNullOrWhiteSpace(s.Provenance!.Controls))
                .Select(s => s.Id)
                .ToList();

            Assert.True(thin.Count == 0,
                "A provenance entry that says nothing is worse than none — it looks like diligence. " +
                "Fill these in:\n  " + string.Join("\n  ", thin));
        }

        [Fact]
        public void UntestedSpecsClaimNoTestingAndNoControls()
        {
            var inconsistent = CatalogueProvenance.SpecsWithProvenance()
                .Where(s => s.Provenance!.Evidence == StrategyEvidenceLevel.Untested)
                .Where(s => !s.Provenance!.TestedOn.Contains("never", StringComparison.OrdinalIgnoreCase)
                         || !s.Provenance!.Controls.Contains("none", StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Id)
                .ToList();

            Assert.True(inconsistent.Count == 0,
                "Marked Untested but describing tests or controls — one of the two fields is wrong:\n  " +
                string.Join("\n  ", inconsistent));
        }

        [Fact]
        public void SpecsWithProvenance_AttachesTheRecordToTheSpecItself()
        {
            // The attachment is what survives export/import; a lookup table alone would stay
            // behind in the lab and the terminal would show an anonymous strategy.
            var spec = CatalogueProvenance.SpecsWithProvenance()
                .Single(s => s.Id == StrategyCatalogue.LongTrendBaselineId);

            Assert.Equal(StrategyEvidenceLevel.ControlTested, spec.Provenance!.Evidence);
            Assert.Contains("random", spec.Provenance!.Controls, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RetiredSpecsKeepTheirVerdict()
        {
            // Deleting a falsified strategy is housekeeping; forgetting that it failed is how it
            // gets reinvented. Six specs were retired on 2026-08-01 — their code is gone and their
            // verdicts are not. Every retirement must carry a dated, sourced verdict.
            Assert.NotEmpty(CatalogueProvenance.Retired);
            Assert.All(CatalogueProvenance.Retired, r =>
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Id));
                Assert.False(string.IsNullOrWhiteSpace(r.Name));
                Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", r.RetiredOn);
                Assert.False(string.IsNullOrWhiteSpace(r.Provenance.Source));
                Assert.True(r.Provenance.Verdict.Length >= 40);
            });
        }

        [Fact]
        public void ARetiredIdStillResolvesToItsVerdict()
        {
            // A note, a memory or an old exported bundle can still name a retired spec. Looking it
            // up should answer "this failed, here is why" rather than "unknown id".
            var retired = CatalogueProvenance.Retired[0];

            Assert.Null(CatalogueProvenance.For(retired.Id));                     // not in the live catalogue
            Assert.NotNull(CatalogueProvenance.ForAnyEverKnown(retired.Id));      // still answerable
            Assert.Equal(StrategyEvidenceLevel.Falsified,
                CatalogueProvenance.ForAnyEverKnown(retired.Id)!.Evidence);
        }

        [Fact]
        public void NoRetiredIdIsStillLiveInTheCatalogue()
        {
            var live = StrategyCatalogue.AllSpecs().Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var zombies = CatalogueProvenance.Retired.Where(r => live.Contains(r.Id)).Select(r => r.Id).ToList();

            Assert.True(zombies.Count == 0,
                "Recorded as retired but still shipping: " + string.Join(", ", zombies));
        }
    }
}
