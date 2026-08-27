using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>No indicator declares the same component twice.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// <c>SkenderBandProvider</c> declared <c>"Sma"</c> twice and
    /// <c>SkenderBoundedOscillatorProvider</c> declared <c>"Oscillator"</c> and
    /// <c>"Signal"</c> twice each — literal duplicate <c>Name</c> values inside one
    /// <c>IndicatorMetadata.Components</c> list. The Bollinger centre line and both
    /// Stochastic lines were each registered as two components.
    /// </para>
    ///
    /// <para>
    /// <c>IndicatorModelFactory.CreateSeriesFromMetadata</c> creates one
    /// <c>ComponentConfig</c> per metadata entry with no de-dup, so the series carried two
    /// identical navigable, sonified components: <b>the user arrowed through the same line
    /// twice and heard two voices playing the same value</b>. <c>SignalCatalog.Refresh</c>
    /// built the same id twice and <c>TryAdd</c> silently swallowed the second, which is why
    /// nothing downstream ever complained.
    /// </para>
    ///
    /// <para>
    /// It was collateral from the <c>PercentK</c>→<c>Oscillator</c> rename: that fix added the
    /// %K/%D pair without removing the pair it replaced.
    /// </para>
    ///
    /// <para>
    /// ── What is enforced ───────────────────────────────────────────────────────
    /// The whole fleet, not the three that were found. A duplicate component name is never
    /// meaningful — two entries with one name cannot be told apart by
    /// <c>GetComponentData</c>, which is keyed on the name — so this is a rule that can be
    /// stated for every indicator without exceptions or an allowlist.
    /// </para>
    /// </summary>
    public class IndicatorMetadataIntegrityTests
    {
        private static List<IIndicatorProvider> AllProviders() => IndicatorProviderFixture.AllProviders();

        [Fact]
        public void NoIndicatorDeclaresTheSameComponentNameTwice()
        {
            var offenders = new List<string>();
            int indicatorsScanned = 0;

            foreach (var provider in AllProviders())
            {
                foreach (var meta in provider.GetIndicators())
                {
                    indicatorsScanned++;
                    if (meta.Components == null) continue;

                    var dupes = meta.Components
                        .GroupBy(c => c.Name, StringComparer.Ordinal)
                        .Where(g => g.Count() > 1)
                        .ToList();

                    foreach (var g in dupes)
                        offenders.Add($"{meta.Code}: '{g.Key}' declared {g.Count()} times");
                }
            }

            // Vacuity floor on the population being governed, not on the violations — a
            // violation count legitimately shrinks to zero, and flooring THAT makes the guard
            // go red for doing its job.
            Assert.True(indicatorsScanned >= 50,
                $"Expected to scan at least 50 indicators, scanned {indicatorsScanned}. "
                + "The provider fixture has probably stopped resolving them.");

            Assert.True(offenders.Count == 0,
                "These indicators declare a component name more than once. Each duplicate "
                + "becomes a second navigable, sonified component playing the same values:\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void NoIndicatorDeclaresTheSameDisplayNameTwice()
        {
            // The spoken half. Two components with one display name are two things the screen
            // reader announces identically, which is the same defect one layer up — and it is
            // reachable without duplicate Names, so it needs its own check.
            var offenders = new List<string>();

            foreach (var provider in AllProviders())
            {
                foreach (var meta in provider.GetIndicators())
                {
                    if (meta.Components == null) continue;

                    var named = meta.Components
                        .Where(c => !string.IsNullOrWhiteSpace(c.DisplayName))
                        .GroupBy(c => c.DisplayName!, StringComparer.Ordinal)
                        .Where(g => g.Count() > 1)
                        .ToList();

                    foreach (var g in named)
                        offenders.Add($"{meta.Code}: display name '{g.Key}' used {g.Count()} times");
                }
            }

            Assert.True(offenders.Count == 0,
                "These indicators give two components the same spoken name:\n  "
                + string.Join("\n  ", offenders));
        }

        [Theory]
        [InlineData("Bb", "Sma")]
        [InlineData("Stoch", "Oscillator")]
        [InlineData("Stoch", "Signal")]
        public void TheThreeThatWereFoundAreDeclaredExactlyOnce(string code, string component)
        {
            // Named explicitly as well as swept, so a regression on these three reads as
            // itself rather than as a line in a list.
            var meta = AllProviders()
                .SelectMany(p => p.GetIndicators())
                .FirstOrDefault(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(meta);
            Assert.Equal(1, meta!.Components.Count(c => c.Name == component));
        }
    }
}
