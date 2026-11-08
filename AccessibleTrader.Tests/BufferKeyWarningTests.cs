using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The orchestrator's "wrote buffer key … but no component with that Name exists" warning is for
    /// a plugin author's typo. Cody's terminal, 2026-11-08, was full of it for keys that are no typo:
    /// DBSI's Control and Trend Source, Cipher A's Ribbon 9, pivots and its wt1/wt2/stoch companions,
    /// Market Structure's state and last swings. The parts were LEFT OUT (still computed, in
    /// ExcludedComponents) and only the included ones were counted; Cipher A's companions are named
    /// after the indicator, not a component. Every hand-written built-in indicator, built as a new chart
    /// builds it and calculated, must write nothing the check calls unknown — and a real typo still
    /// is. The Skender wrappers write their library's whole result type by reflection and are not
    /// checked (<see cref="IWritesItsLibrarysWholeResult"/>).
    /// </summary>
    public class BufferKeyWarningTests
    {
        private static IndicatorModelFactory Factory() => new(
            new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            new MockIndicatorPreferencesService());

        private static List<Ohlcv> Bars()
        {
            var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return Enumerable.Range(0, 400).Select(i =>
            {
                double c = 100 + 10 * Math.Sin(i / 9.0) + 4 * Math.Sin(i / 2.3) + i * 0.05;
                return new Ohlcv(start.AddDays(i), c - 0.5, c + 1.5, c - 1.7, c, 1000 + 50 * (i % 7));
            }).ToList();
        }

        [Fact]
        public void NoBuiltInIndicatorWarnsAboutItsOwnKeys()
        {
            var factory = Factory();
            var bars = Bars();
            var warned = new List<string>();
            var calculated = new HashSet<string>();
            foreach (var provider in IndicatorProviderFixture.AllProviders().Where(p => p is not IWritesItsLibrarysWholeResult))
            foreach (var meta in provider.GetIndicators())
            {
                var series = factory.CreateSeriesFromMetadata(meta, meta.Name, PaneAssignmentService.PaneFor(meta),
                    new List<(string, string)>(), null, null);
                var results = new Dictionary<string, double[]>();
                try
                {
                    provider.Calculate(meta.Code, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
                        series.BuildParameterMap(), new IndicatorResultBuffer(results, bars.Count));
                }
                catch (Exception) { continue; }   // needs a feed these bars do not carry; not this test's subject
                calculated.Add(meta.Code);
                foreach (var key in IndicatorOrchestrator.UnknownBufferKeys(series, results.Keys, provider.Name))
                    warned.Add($"{meta.Code}: '{key}'");
            }

            // Vacuity: the three in Cody's terminal are among the ones calculated, and the sweep is wide.
            Assert.Superset(new HashSet<string> { "DBSI_STYLE", "CIPHER_A", "SWING_STRUCTURE" }, calculated);
            Assert.True(calculated.Count >= 40, $"only {calculated.Count} indicators calculated — the sweep proves little");
            Assert.True(warned.Count == 0, "keys the warning would call typos:\n  " + string.Join("\n  ", warned));
        }

        [Fact]
        public void TheLeftOutPartsAreThere_AndATypoStillWarns()
        {
            var provider = new Core.Services.Indicators.DbsiStyleProvider();
            var meta = provider.GetIndicators()[0];
            var series = Factory().CreateSeriesFromMetadata(meta, meta.Name, PaneAssignmentService.PaneFor(meta),
                new List<(string, string)>(), null, null);

            // Vacuity: the case Cody hit — Control is left out by default, not included.
            Assert.Contains(series.Config.ExcludedComponents, c => c.Name == "Control");
            Assert.DoesNotContain(series.Components, c => c.Name == "Control");

            var unknown = IndicatorOrchestrator.UnknownBufferKeys(series,
                new[] { "Control", "Trend Source", "Contol", "Cipher DBSI_extra" }, provider.Name);
            Assert.Equal(new[] { "Contol" }, unknown);
        }
    }
}
