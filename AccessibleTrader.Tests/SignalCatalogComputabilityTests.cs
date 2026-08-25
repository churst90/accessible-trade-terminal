using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The strategy builder may only offer leaves the chart could actually draw.
    ///
    /// <para>
    /// <c>IndicatorService.GetAvailableIndicators</c> filters out indicators the underlying library
    /// cannot compute — Skender 2.5.0 implements no PPO, HV, TMA, ZLEMA or EOM, and a reflection
    /// lookup for a method it does not export produces an empty line with no exception and no log.
    /// <c>SignalCatalog</c> walked the same providers <b>raw</b>. So the user could not add PPO to a
    /// chart, but the strategy builder happily offered <c>Ppo.Ppo GreaterThan 0</c> — permanently
    /// NaN, therefore permanently false, for the life of the strategy, with nothing said about it.
    /// A condition that can never fire is the worst kind of silence: the strategy runs, reports no
    /// trades, and looks like a market that never set up.
    /// </para>
    ///
    /// <para>
    /// The rule now lives in one place (<c>IndicatorComputability</c>) and both callers apply it.
    /// This file is what keeps them agreeing.
    /// </para>
    /// </summary>
    public class SignalCatalogComputabilityTests
    {
        private static readonly string[] NotInSkender25 = { "Ppo", "Hv", "Tma", "Zlema", "Eom" };

        private static List<IIndicatorProvider> Providers() => IndicatorProviderFixture.AllProviders();

        private static IndicatorService Service(List<IIndicatorProvider> providers) =>
            new(providers, NullLogger<IndicatorService>.Instance);

        [Fact]
        public void EveryPublishedLeafBelongsToAnIndicatorTheMenuAlsoOffers()
        {
            var providers = Providers();
            var catalog = new SignalCatalog(providers);
            var offerable = Service(providers).GetAvailableIndicators()
                                              .Select(m => m.Code)
                                              .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var orphans = catalog.All.Select(d => d.IndicatorCode)
                                     .Distinct(StringComparer.OrdinalIgnoreCase)
                                     .Where(code => !offerable.Contains(code))
                                     .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                                     .ToList();

            Assert.True(orphans.Count == 0,
                "SignalCatalog offers strategy leaves for indicators the Add Indicator dialog " +
                "refuses to list, so they cannot produce a value: " + string.Join(", ", orphans));

            // Vacuity floor on the POPULATION: if either list came back empty the comparison above
            // is trivially true.
            Assert.True(catalog.All.Count > 100, $"only {catalog.All.Count} leaves — catalog did not build");
            Assert.True(offerable.Count > 20, $"only {offerable.Count} indicators offered — menu did not build");
        }

        [Theory]
        [InlineData("Ppo")]
        [InlineData("Hv")]
        [InlineData("Tma")]
        [InlineData("Zlema")]
        [InlineData("Eom")]
        public void AnIndicatorTheLibraryCannotComputeIsNotPickableAsASignal(string code)
        {
            var catalog = new SignalCatalog(Providers());

            Assert.DoesNotContain(catalog.All, d => d.IndicatorCode.Equals(code, StringComparison.OrdinalIgnoreCase));

            // It must still be REFUSED rather than absent: a strategy saved before this gate has to
            // be able to say why its leaf stopped working, exactly as with the causality refusals.
            var refused = catalog.Excluded
                .Where(d => d.IndicatorCode.Equals(code, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.NotEmpty(refused);
            foreach (var d in refused)
            {
                Assert.NotNull(catalog.GetById(d.Id));
                Assert.Contains("empty values", catalog.RefusalReason(d.Id));
            }
        }

        [Fact]
        public void TheRefusedSetIsNotEverything()
        {
            // The gate is only worth having if the computable indicators came through it. Pin a few
            // Skender-backed codes that DO resolve, so "refuse the lot" cannot pass this file.
            var ids = new SignalCatalog(Providers()).All
                .Select(d => d.IndicatorCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Contains("Rsi", ids);
            Assert.Contains("Atr", ids);
            Assert.Contains("Macd", ids);
        }

        [Fact]
        public void TheTwoListsAgreeBecauseTheyAskTheSameQuestion()
        {
            // A behavioural pin on the shared rule rather than on its two callers: whatever the
            // menu drops for computability, the catalog must drop too, code for code.
            var providers = Providers();
            var everyCode = providers.SelectMany(p =>
            {
                try { return p.GetIndicators().Select(m => m.Code); }
                catch { return Enumerable.Empty<string>(); }
            }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var offerable = Service(providers).GetAvailableIndicators()
                                              .Select(m => m.Code)
                                              .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var droppedByMenu = everyCode.Where(c => !offerable.Contains(c))
                                         .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // The five known ones must be in there, or this assertion is comparing empty sets.
            foreach (var code in NotInSkender25)
                Assert.Contains(code, droppedByMenu, StringComparer.OrdinalIgnoreCase);

            var published = new SignalCatalog(providers).All
                .Select(d => d.IndicatorCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.DoesNotContain(droppedByMenu, published.Contains);
        }
    }
}
