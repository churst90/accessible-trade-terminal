using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.StrategyLab.Catalogue;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The terminal/lab split, pinned.
    ///
    /// <para>
    /// Two behaviours have to hold together for the split to mean anything: a fresh library is
    /// EMPTY (nothing writes research specs into it behind the user's back), and the import path
    /// is safe enough to be the only way in — it never overwrites, never auto-starts, never
    /// compiles code out of a file, and never throws at the caller on bad input.
    /// </para>
    /// </summary>
    public class StrategyBundleTests : IDisposable
    {
        private readonly string _dir;

        public StrategyBundleTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "att-bundle-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private sealed class FakePaths : IPlatformPathService
        {
            public FakePaths(string dir) { AppDataDirectory = dir; CacheDirectory = dir; }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        private static StrategySpec Spec(
            string id,
            string name = "Test spec",
            bool autoActivate = false,
            StrategyExecutionMode mode = StrategyExecutionMode.Suggestion,
            string? roslyn = null) =>
            new(
                Id: id,
                Name: name,
                Description: "a spec",
                Side: OrderSide.Buy,
                Conditions: new ConditionLeaf("leaf", "REGIME.AboveSma200", LeafOperator.GreaterThan, Value: 0),
                Risk: new RiskPlan(
                    new StopSource(StopSourceKind.AtrMultiple),
                    new List<TpLadderRung> { new(TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 1.0) },
                    new PositionSizing(),
                    new EntryTrigger()),
                ExecutionMode: mode,
                IsAutoActivate: autoActivate,
                RoslynSource: roslyn);

        private JsonStrategyLibrary NewLibrary() => new(new FakePaths(_dir));

        private static string BundleOf(params StrategySpec[] specs) =>
            StrategyBundleService.Write(specs, "test", "test-cat", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        // ── The library ships empty ─────────────────────────────────────────────

        [Fact]
        public void FreshLibrary_IsEmpty_NothingIsSeeded()
        {
            // Until 2026-08-01 this was 30. The terminal ships tools, not a shelf of research
            // artifacts wearing the product's authority.
            var lib = NewLibrary();

            Assert.Empty(lib.All);
        }

        [Fact]
        public void FreshLibrary_AfterReload_IsStillEmpty()
        {
            var lib = NewLibrary();
            lib.Reload();

            Assert.Empty(lib.All);
        }

        // ── Round trip ──────────────────────────────────────────────────────────

        [Fact]
        public void Bundle_RoundTrips_ConditionTreeAndProvenance()
        {
            var spec = Spec("t.1") with
            {
                Conditions = new ConditionGroup("root", LogicOperator.And, new List<ConditionNode>
                {
                    new ConditionLeaf("a", "CIPHER_B.Oversold Crossover", LeafOperator.FiredWithin, WithinNBars: 3),
                    new ConditionLeaf("b", "REGIME.AboveSma200", LeafOperator.GreaterThan, Value: 0),
                }),
                Provenance = new StrategyProvenance(
                    StrategyEvidenceLevel.Falsified, "BTC daily", "walk-forward", "it did not work"),
            };

            var read = StrategyBundleService.Read(BundleOf(spec), out string? error);

            Assert.Null(error);
            var back = Assert.Single(read!.Strategies);
            var group = Assert.IsType<ConditionGroup>(back.Conditions);
            Assert.Equal(2, group.Children.Count);
            Assert.Equal("CIPHER_B.Oversold Crossover", Assert.IsType<ConditionLeaf>(group.Children[0]).SignalDescriptorId);
            Assert.Equal(StrategyEvidenceLevel.Falsified, back.Provenance!.Evidence);
            Assert.Equal("it did not work", back.Provenance!.Verdict);
        }

        [Fact]
        public void Import_AddsSpecs_AndTheyPersistAcrossReload()
        {
            var lib = NewLibrary();

            var result = StrategyBundleService.Import(BundleOf(Spec("t.1", "One"), Spec("t.2", "Two")), lib);

            Assert.True(result.Success);
            Assert.Equal(2, result.Imported.Count);
            Assert.Equal(2, NewLibrary().All.Count);
        }

        // ── The import rules ────────────────────────────────────────────────────

        [Fact]
        public void Import_NeverOverwritesAnExistingSpec()
        {
            var lib = NewLibrary();
            lib.Upsert(Spec("t.1", "The user's own edited version"));

            var result = StrategyBundleService.Import(BundleOf(Spec("t.1", "Catalogue version")), lib);

            Assert.Empty(result.Imported);
            Assert.Equal("The user's own edited version", Assert.Single(lib.All).Name);
            // The report names what was in the FILE — that is the thing the user chose to import
            // and did not get, so it is the name they need to hear.
            Assert.Equal("Catalogue version", Assert.Single(result.SkippedExisting));
        }

        [Fact]
        public void Import_ForcesAutoActivateOff_SoNothingStartsOnImport()
        {
            // A file that arrives claiming auto-activate would otherwise start trading logic at
            // the next launch of an app the user has not touched since importing.
            var lib = NewLibrary();

            StrategyBundleService.Import(BundleOf(Spec("t.1", autoActivate: true)), lib);

            Assert.False(Assert.Single(lib.All).IsAutoActivate);
        }

        [Fact]
        public void Import_CountsOrderPlacingSpecs_SoTheUserCanBeWarned()
        {
            var lib = NewLibrary();

            var result = StrategyBundleService.Import(
                BundleOf(Spec("t.1", mode: StrategyExecutionMode.Auto), Spec("t.2")), lib);

            Assert.Equal(1, result.AutoExecutionCount);
            Assert.Contains("place orders automatically", result.Describe());
        }

        [Fact]
        public void Import_RejectsRoslynSourceSpecs_ImportingAFileMustNotRunCode()
        {
            var lib = NewLibrary();

            var result = StrategyBundleService.Import(
                BundleOf(Spec("t.1", "Code strategy", roslyn: "class X { }")), lib);

            Assert.Empty(lib.All);
            Assert.Contains("program code", Assert.Single(result.Rejected));
        }

        [Fact]
        public void Import_RejectsSpecsMissingConditionsOrRisk()
        {
            var lib = NewLibrary();
            string json = "{\"FormatVersion\":1,\"Source\":\"x\",\"ExportedUtc\":\"2026-08-01T00:00:00Z\"," +
                          "\"Strategies\":[{\"Id\":\"t.1\",\"Name\":\"No tree\",\"Side\":\"Buy\"}]}";

            var result = StrategyBundleService.Import(json, lib);

            Assert.Empty(lib.All);
            Assert.Contains("no conditions", Assert.Single(result.Rejected));
        }

        // ── Bad input is an ordinary event, not an exception ────────────────────

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not json at all")]
        [InlineData("{\"FormatVersion\":1,\"Strategies\":[]}")]
        public void Import_BadInput_FailsCleanlyAndLeavesTheLibraryAlone(string json)
        {
            var lib = NewLibrary();
            lib.Upsert(Spec("mine", "Mine"));

            var result = StrategyBundleService.Import(json, lib);

            Assert.False(result.Success);
            Assert.StartsWith("Import failed:", result.Describe());
            Assert.Equal("Mine", Assert.Single(lib.All).Name);
        }

        [Fact]
        public void Import_RefusesAFutureFormatVersion_WithAnActionableMessage()
        {
            var lib = NewLibrary();
            string json = "{\"FormatVersion\":99,\"Source\":\"x\",\"ExportedUtc\":\"2026-08-01T00:00:00Z\"," +
                          "\"Strategies\":[{\"Id\":\"t.1\",\"Name\":\"n\"}]}";

            var result = StrategyBundleService.Import(json, lib);

            Assert.False(result.Success);
            Assert.Contains("Update the app", result.Describe());
        }

        // ── The lab's export is importable, end to end ──────────────────────────

        [Fact]
        public void TheLabsCatalogue_ExportsAndImportsIntoAnEmptyLibrary()
        {
            // The whole point of the split in one test: specs live in the lab, reach the terminal
            // only through a bundle, and arrive with their evidence attached.
            var lib = NewLibrary();
            var specs = CatalogueProvenance.SpecsWithProvenance().ToList();

            string json = StrategyBundleService.Write(
                specs, "lab", StrategyCatalogue.Version, DateTime.UtcNow);
            var result = StrategyBundleService.Import(json, lib);

            Assert.True(result.Success);
            Assert.Equal(specs.Count, lib.All.Count);
            Assert.All(lib.All, s => Assert.NotNull(s.Provenance));
            Assert.All(lib.All, s => Assert.False(s.IsAutoActivate));

            // …and survives the JSON library's own serialization on the way to disk.
            var reloaded = NewLibrary();
            Assert.Equal(specs.Count, reloaded.All.Count);
            Assert.All(reloaded.All, s => Assert.NotNull(s.Provenance));
        }
    }
}
