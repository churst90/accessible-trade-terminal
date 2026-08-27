using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// First test coverage for the persistence layer (2026-06-12 audit fix 6 — the
    /// subsystem previously had ZERO tests). Pins three behaviours:
    ///
    ///  1. AtomicFile write semantics (roundtrip, overwrite, parent-dir creation,
    ///     no stray temp files).
    ///  2. Graceful missing-file defaults across SettingsManager /
    ///     JsonStrategyLibrary.
    ///  3. The corrupt-file quarantine: an unreadable store is moved aside to
    ///     *.corrupt-* (recoverable) instead of being silently overwritten by the
    ///     next save — the old behaviour permanently destroyed user data, worst
    ///     case the entire strategy library.
    /// </summary>
    public class PersistenceHardeningTests : IDisposable
    {
        private readonly string _dir;

        public PersistenceHardeningTests()
        {
            _dir = TestTemp.NewPath("atc-persist-tests-");
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
        }

        private sealed class FakePathService : IPlatformPathService
        {
            public FakePathService(string dir) { AppDataDirectory = dir; CacheDirectory = dir; }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        // ── AtomicFile ──────────────────────────────────────────────────────────

        [Fact]
        public void AtomicFile_RoundTripsAndOverwrites()
        {
            string path = Path.Combine(_dir, "atomic.txt");

            AtomicFile.WriteAllText(path, "first");
            Assert.Equal("first", File.ReadAllText(path));

            AtomicFile.WriteAllText(path, "second");
            Assert.Equal("second", File.ReadAllText(path));
        }

        [Fact]
        public void AtomicFile_CreatesParentDirectories()
        {
            string path = Path.Combine(_dir, "nested", "deeper", "file.json");

            AtomicFile.WriteAllText(path, "{}");

            Assert.Equal("{}", File.ReadAllText(path));
        }

        [Fact]
        public void AtomicFile_LeavesNoTempFilesBehind()
        {
            string path = Path.Combine(_dir, "clean.txt");
            for (int i = 0; i < 5; i++)
                AtomicFile.WriteAllText(path, $"write {i}");

            var strays = Directory.GetFiles(_dir, "*.tmp-*");
            Assert.Empty(strays);
        }

        // ── CorruptFileQuarantine ───────────────────────────────────────────────

        [Fact]
        public void Quarantine_MovesFileAsideAndRecordsReport()
        {
            string path = Path.Combine(_dir, "broken.json");
            File.WriteAllText(path, "{ not valid json !!");

            string? quarantined = CorruptFileQuarantine.MoveAside(path, new Exception("boom"));

            Assert.NotNull(quarantined);
            Assert.False(File.Exists(path));
            Assert.True(File.Exists(quarantined));
            Assert.Equal("{ not valid json !!", File.ReadAllText(quarantined!));
            Assert.Contains(CorruptFileQuarantine.SessionReports,
                r => r.Contains("broken.json"));
        }

        [Fact]
        public void Quarantine_MissingFile_StillRecordsReportWithoutThrowing()
        {
            string path = Path.Combine(_dir, "never-existed.json");

            string? quarantined = CorruptFileQuarantine.MoveAside(path, new Exception("boom"));

            Assert.Null(quarantined);
            Assert.Contains(CorruptFileQuarantine.SessionReports,
                r => r.Contains("never-existed.json"));
        }

        // ConfigService and its three tests were deleted 2026-08-24 along with the class.
        // It had zero production call sites and wrote to
        // AppDomain.CurrentDomain.BaseDirectory/appsettings.json — the ASP.NET host's own
        // config file in the deployment directory, shared by every hosted user and
        // destroyed by the next redeploy. The quarantine and missing-file behaviours it
        // covered are still guarded here for SettingsManager and in
        // WorkspacePersistenceGapTests for profiles and alerts.

        // ── SettingsManager ─────────────────────────────────────────────────────

        [Fact]
        public void SettingsManager_MissingFile_UsesDefaults()
        {
            var mgr = new SettingsManager(new FakePathService(_dir), NullLogger<SettingsManager>.Instance);

            Assert.Null(mgr.GetSetting("nothing.here"));
            Assert.Equal(7, mgr.GetSetting("nothing.here", new JValue(7))!.Value<int>());
        }

        [Fact]
        public void SettingsManager_KeyPathRoundTrip_SurvivesReload()
        {
            var mgr = new SettingsManager(new FakePathService(_dir), NullLogger<SettingsManager>.Instance);
            mgr.SetSetting("audio.master.volume", new JValue(0.8));
            mgr.SaveSettings();

            var reloaded = new SettingsManager(new FakePathService(_dir), NullLogger<SettingsManager>.Instance);
            Assert.Equal(0.8, reloaded.GetSetting("audio.master.volume")!.Value<double>(), 9);
        }

        [Fact]
        public void SettingsManager_CorruptFile_QuarantinesAndStartsClean()
        {
            string path = Path.Combine(_dir, "settings.json");
            File.WriteAllText(path, "not even close to json");

            var mgr = new SettingsManager(new FakePathService(_dir), NullLogger<SettingsManager>.Instance);

            Assert.Null(mgr.GetSetting("any.key"));
            var quarantined = Directory.GetFiles(_dir, "settings.json.corrupt-*");
            Assert.Single(quarantined);
            Assert.Equal("not even close to json", File.ReadAllText(quarantined[0]));
        }

        // ── JsonStrategyLibrary ─────────────────────────────────────────────────

        [Fact]
        public void StrategyLibrary_CorruptFile_QuarantinesInsteadOfSilentSeedReset()
        {
            // THE data-loss scenario from the audit: a corrupt strategies.json used to be
            // replaced on the next save, destroying every user strategy. Since 2026-08-01 the
            // library no longer seeds anything, so the quarantine copy is the ONLY route back
            // to the user's specs — this test matters more now, not less.
            string path = Path.Combine(_dir, "strategies.json");
            File.WriteAllText(path, "[ { \"Id\": \"user.precious\", TRUNCATED GARBAGE");

            var lib = new JsonStrategyLibrary(new FakePathService(_dir));

            // The in-memory library starts empty, but the user's original file is recoverable…
            Assert.Empty(lib.All);
            var quarantined = Directory.GetFiles(_dir, "strategies.json.corrupt-*");
            Assert.Single(quarantined);
            Assert.Contains("user.precious", File.ReadAllText(quarantined[0]));

            // …and an explicit save does not touch the quarantined backup.
            lib.Save();
            Assert.True(File.Exists(quarantined[0]));
            Assert.Contains("user.precious", File.ReadAllText(quarantined[0]));
        }

        [Fact]
        public void StrategyLibrary_UserSpecSurvivesReload()
        {
            var lib = new JsonStrategyLibrary(new FakePathService(_dir));
            Assert.Empty(lib.All);   // a fresh library seeds nothing — see StrategyBundleTests

            var spec = new AccessibleTrader.Sdk.Strategies.StrategySpec(
                Id: "user.mine",
                Name: "My Strategy",
                Description: "built by the user",
                Side: AccessibleTrader.Sdk.Plugins.OrderSide.Buy,
                Conditions: new AccessibleTrader.Sdk.Strategies.ConditionLeaf(
                    "leaf", "REGIME.AboveSma200", AccessibleTrader.Sdk.Strategies.LeafOperator.GreaterThan),
                Risk: new AccessibleTrader.Sdk.Strategies.RiskPlan(
                    new AccessibleTrader.Sdk.Strategies.StopSource(
                        AccessibleTrader.Sdk.Strategies.StopSourceKind.AtrMultiple),
                    new[] { new AccessibleTrader.Sdk.Strategies.TpLadderRung(
                        AccessibleTrader.Sdk.Strategies.TargetSourceKind.RiskRewardMultiple) },
                    new AccessibleTrader.Sdk.Strategies.PositionSizing(),
                    new AccessibleTrader.Sdk.Strategies.EntryTrigger()));
            lib.Upsert(spec);

            var reloaded = new JsonStrategyLibrary(new FakePathService(_dir));
            Assert.Single(reloaded.All);
            Assert.NotNull(reloaded.GetById("user.mine"));
            Assert.Equal("My Strategy", reloaded.GetById("user.mine")!.Name);
        }
    }
}
