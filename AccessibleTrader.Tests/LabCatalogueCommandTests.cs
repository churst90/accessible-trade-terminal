using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.StrategyLab;
using AccessibleTrader.StrategyLab.Catalogue;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The lab's half of the export/import contract. These run the actual CLI entry point rather
    /// than the service underneath it, because the parts that go wrong in a CLI are the argument
    /// handling and the selection rules, not the serializer.
    /// </summary>
    public class LabCatalogueCommandTests : IDisposable
    {
        private readonly string _dir;
        private readonly TextWriter _stdout;
        private readonly TextWriter _stderr;

        public LabCatalogueCommandTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "att-catalogue-cli-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _stdout = Console.Out;
            _stderr = Console.Error;
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
        }

        public void Dispose()
        {
            Console.SetOut(_stdout);
            Console.SetError(_stderr);
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private string Out(string name) => Path.Combine(_dir, name);

        private sealed class FakePaths : IPlatformPathService
        {
            public FakePaths(string dir) { AppDataDirectory = dir; CacheDirectory = dir; }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        [Fact]
        public void Export_ByIdWritesABundleTheTerminalCanImport()
        {
            string path = Out("one.json");

            int code = CatalogueCommand.Run(new[]
            {
                "export", "--out", path, "--id", StrategyCatalogue.LongTrendBaselineId
            });

            Assert.Equal(0, code);
            var lib = new JsonStrategyLibrary(new FakePaths(_dir));
            var result = StrategyBundleService.Import(File.ReadAllText(path), lib);

            Assert.True(result.Success);
            var spec = Assert.Single(lib.All);
            Assert.Equal(StrategyCatalogue.LongTrendBaselineId, spec.Id);
            Assert.Equal(StrategyEvidenceLevel.ControlTested, spec.Provenance!.Evidence);
        }

        [Fact]
        public void Export_StampsTheCatalogueVersion_SoAnImportedSpecCanBeTracedBack()
        {
            string path = Out("stamped.json");
            CatalogueCommand.Run(new[] { "export", "--out", path, "--id", StrategyCatalogue.LongTrendBaselineId });

            var bundle = StrategyBundleService.Read(File.ReadAllText(path), out string? error);

            Assert.Null(error);
            Assert.Equal(StrategyCatalogue.Version, bundle!.CatalogueVersion);
            Assert.Equal(StrategyBundle.CurrentFormatVersion, bundle.FormatVersion);
        }

        [Fact]
        public void Export_ByEvidence_NeverSweepsInFragileOrFalsifiedSpecs()
        {
            // Fragile and Falsified are OUTCOMES, not rungs on the evidence ladder. A bulk export
            // asking for "walk-forward or better" must not hand over a spec whose recorded verdict
            // is that it failed — that is the entire mechanism by which a falsified strategy would
            // find its way back into someone's library.
            string path = Out("survivors.json");

            int code = CatalogueCommand.Run(new[] { "export", "--out", path, "--min-evidence", "WalkForward" });

            Assert.Equal(0, code);
            var bundle = StrategyBundleService.Read(File.ReadAllText(path), out _);
            Assert.NotEmpty(bundle!.Strategies);
            Assert.All(bundle.Strategies, s =>
            {
                Assert.NotEqual(StrategyEvidenceLevel.Fragile, s.Provenance!.Evidence);
                Assert.NotEqual(StrategyEvidenceLevel.Falsified, s.Provenance!.Evidence);
                Assert.True((int)s.Provenance!.Evidence >= (int)StrategyEvidenceLevel.WalkForward);
            });
        }

        [Fact]
        public void Export_AnExcludedSpecStillGoesOutWhenNamedExplicitly()
        {
            // Deliberate is fine; accidental is not. Re-testing a recorded failure is legitimate
            // research, so --id remains an unrestricted escape hatch even for the categories
            // --min-evidence refuses to include.
            string excludedId = CatalogueProvenance.SpecsWithProvenance()
                .First(s => s.Provenance!.Evidence is StrategyEvidenceLevel.Fragile
                                                   or StrategyEvidenceLevel.Falsified).Id;
            string path = Out("known-bad.json");

            int code = CatalogueCommand.Run(new[] { "export", "--out", path, "--id", excludedId });

            Assert.Equal(0, code);
            var bundle = StrategyBundleService.Read(File.ReadAllText(path), out _);
            Assert.Equal(excludedId, Assert.Single(bundle!.Strategies).Id);
        }

        [Fact]
        public void Export_UnknownId_FailsWithoutWritingAFile()
        {
            string path = Out("never.json");

            int code = CatalogueCommand.Run(new[] { "export", "--out", path, "--id", "builtin.long.does-not-exist" });

            Assert.Equal(2, code);
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void Export_WithNoSelection_RefusesRatherThanDumpingEverything()
        {
            // No selection used to be the obvious place to default to "all 30". It exports the
            // falsified ones too, so the default is to ask rather than to guess.
            string path = Out("everything.json");

            int code = CatalogueCommand.Run(new[] { "export", "--out", path });

            Assert.NotEqual(0, code);
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void List_RunsForEveryEvidenceLevel_AndForTheWholeCatalogue()
        {
            Assert.Equal(0, CatalogueCommand.Run(new[] { "list" }));
            Assert.Equal(0, CatalogueCommand.Run(new[] { "list", "--verbose" }));
            foreach (var level in Enum.GetNames<StrategyEvidenceLevel>())
                Assert.Equal(0, CatalogueCommand.Run(new[] { "list", "--status", level }));
        }

        [Fact]
        public void List_RejectsAnUnknownEvidenceLevel()
        {
            Assert.NotEqual(0, CatalogueCommand.Run(new[] { "list", "--status", "Excellent" }));
        }
    }
}
