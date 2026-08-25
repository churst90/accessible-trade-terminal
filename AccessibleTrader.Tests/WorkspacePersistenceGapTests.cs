using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.StrategyLab.Catalogue;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The 2026-07-22 audit's two workspace criticals, now fixed and pinned:
    /// active strategies and drawings BOTH vanished on save/load (the strategy
    /// field existed in the model but was never written; drawing anchors lived
    /// only on the runtime series). These tests are the regression fence.
    /// </summary>
    public class WorkspacePersistenceGapTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("att-wsgap-").FullName;
        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

        // ── Corrupt-file quarantine (was silently swallowed) ─────────────────

        [Fact]
        public void LoadProfile_quarantines_a_corrupt_file_instead_of_swallowing_it()
        {
            // A corrupt workspace file must be moved aside (recoverable) and recorded so the
            // user is told — not silently returned as null, which left a blind user with an
            // unexplained blank workspace and let the next save clobber the original.
            CorruptFileQuarantine.ClearSessionReports();
            File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ this is not valid json ");
            var lib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths())
                { LibraryDirectoryOverride = _dir };

            var result = lib.LoadProfile("broken");

            Assert.Null(result);
            Assert.False(File.Exists(Path.Combine(_dir, "broken.json")));      // moved aside
            Assert.NotEmpty(Directory.GetFiles(_dir, "broken.json.corrupt-*")); // preserved
            Assert.Contains(CorruptFileQuarantine.SessionReports, r => r.Contains("broken.json"));
        }

        [Fact]
        public void LoadAlerts_quarantines_a_corrupt_file_instead_of_swallowing_it()
        {
            CorruptFileQuarantine.ClearSessionReports();
            File.WriteAllText(Path.Combine(_dir, "alerts.json"), "not json at all");
            var lib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths())
                { LibraryDirectoryOverride = _dir };

            var result = lib.LoadAlerts();

            Assert.Empty(result);
            Assert.NotEmpty(Directory.GetFiles(_dir, "alerts.json.corrupt-*"));
            Assert.Contains(CorruptFileQuarantine.SessionReports, r => r.Contains("alerts.json"));
        }

        // ── Active strategies round-trip ─────────────────────────────────────

        private static IStrategyEngine EngineWith(params ActiveStrategy[] active)
        {
            var engine = Substitute.For<IStrategyEngine>();
            engine.ActiveStrategies.Returns(active.ToList());
            return engine;
        }

        private static IWorkspaceStore StoreWith(WorkspaceState state)
        {
            var store = Substitute.For<IWorkspaceStore>();
            store.State.Returns(state);
            return store;
        }

        [Fact]
        public void Save_captures_library_backed_strategies_and_skips_adhoc_scripts()
        {
            var strategy = Substitute.For<ITradingStrategy>();
            var engine = EngineWith(
                new ActiveStrategy("i1", strategy, new Dictionary<string, object>(),
                    StrategyExecutionMode.Suggestion, IsPaused: true, "BTC/USD", "builtin.long.v24-cycle-low-reversal"),
                new ActiveStrategy("i2", strategy, new Dictionary<string, object>(),
                    StrategyExecutionMode.Suggestion, IsPaused: false, "ETH/USD", SpecId: null)); // ad-hoc script

            var lib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths(), engine)
                { LibraryDirectoryOverride = _dir };
            lib.SaveWorkspaceProfile("test", StoreWith(WorkspaceState.Initial));

            var loaded = lib.LoadProfile("test")!;
            var saved = Assert.Single(loaded.ActiveStrategies);
            Assert.Equal("builtin.long.v24-cycle-low-reversal", saved.SpecId);
            Assert.Equal("BTC/USD", saved.Symbol);
            Assert.True(saved.IsPaused);
        }

        [Fact]
        public void Restore_reactivates_saved_strategies_bound_to_their_saved_symbol()
        {
            var engine = Substitute.For<IStrategyEngine>();
            var stale = new ActiveStrategy("old", Substitute.For<ITradingStrategy>(),
                new Dictionary<string, object>(), StrategyExecutionMode.Suggestion, false, null);
            engine.ActiveStrategies.Returns(new List<ActiveStrategy> { stale });
            engine.AddStrategy(Arg.Any<ITradingStrategy>(), Arg.Any<IDictionary<string, object>>(),
                Arg.Any<StrategyExecutionMode>(), Arg.Any<string?>(), Arg.Any<string?>()).Returns("new-id");

            var spec = StrategyCatalogue.AllSpecs().First();
            var library = Substitute.For<IStrategyLibrary>();
            library.All.Returns(new[] { spec });
            var factory = Substitute.For<IConfigurableStrategyFactory>();
            factory.Create(spec).Returns(Substitute.For<ITradingStrategy>());

            var init = new WorkspaceInitializer(
                Substitute.For<ISeriesManagementService>(),
                StoreWith(WorkspaceState.Initial),
                Substitute.For<IWorkspaceLibraryService>(),
                SubstituteIndicatorService(),
                engine, factory, library);

            init.RestoreWorkspace(new WorkspaceConfiguration
            {
                Tabs = new List<TabConfiguration> { new() },
                ActiveStrategies = new List<SavedActiveStrategy>
                {
                    new() { SpecId = spec.Id, Symbol = "BTC/USD", ExecutionMode = "Suggestion", IsPaused = true },
                    new() { SpecId = "deleted-spec", Symbol = "ETH/USD" }, // deleted from library → skipped, no throw
                },
            });

            engine.Received(1).RemoveStrategy("old"); // REPLACE semantics
            engine.Received(1).AddStrategy(Arg.Any<ITradingStrategy>(), Arg.Any<IDictionary<string, object>>(),
                StrategyExecutionMode.Suggestion, spec.Id, "BTC/USD"); // saved binding, not current chart
            engine.Received(1).PauseStrategy("new-id", true);
        }

        private static IIndicatorService SubstituteIndicatorService()
        {
            var svc = Substitute.For<IIndicatorService>();
            svc.GetAvailableIndicators().Returns(new List<IndicatorMetadata>());
            return svc;
        }

        // ── Drawings round-trip ──────────────────────────────────────────────

        [Fact]
        public void Save_syncs_drawing_anchors_into_the_persisted_config()
        {
            var drawing = new DrawingData
            {
                Type = DrawingType.TrendLine,
                AnchorDate1 = new DateTime(2026, 1, 1),
                AnchorPrice1 = 100,
                AnchorDate2 = new DateTime(2026, 2, 1),
                AnchorPrice2 = 120,
            };
            var cfg = new SeriesConfig { Id = "d1", Name = "Trend (1)", IndicatorCode = "" };
            var series = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "d1" }) { Drawing = drawing };
            var state = WorkspaceState.Initial with { ActiveSeries = ImmutableList.Create(series) };

            var lib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths())
                { LibraryDirectoryOverride = _dir };
            lib.SaveWorkspaceProfile("draw", StoreWith(state));

            var loaded = lib.LoadProfile("draw")!;
            var savedCfg = Assert.Single(loaded.Tabs[0].Series);
            Assert.NotNull(savedCfg.Drawing);
            Assert.Equal(DrawingType.TrendLine, savedCfg.Drawing!.Type);
            Assert.Equal(120, savedCfg.Drawing.AnchorPrice2);
        }

        [Fact]
        public void Restore_rehydrates_the_drawing_onto_the_series()
        {
            // RegisterSeriesFromConfig is the restore path for drawing series
            // (empty IndicatorCode → no metadata). The rehydrated series carries
            // the anchors; the indicator orchestrator recomputes arrays on the
            // next data pass — pinned indirectly by IsDrawing being true again.
            var store = Substitute.For<IWorkspaceStore>();
            store.State.Returns(WorkspaceState.Initial);
            ChartSeries? added = null;
            store.When(s => s.Dispatch(Arg.Any<WorkspaceAction>())).Do(ci =>
            {
                if (ci.Arg<WorkspaceAction>() is AddSeriesAction a) added = a.Series;
            });

            var svc = new SeriesManagementService(store,
                Substitute.For<AccessibleTrader.Core.Services.IEventBus>(),
                Substitute.For<IIndicatorModelFactory>(),
                Substitute.For<IStylingService>(),
                Substitute.For<IWorkspaceLibraryService>(),
                Substitute.For<AccessibleTrader.Core.Services.Indicators.ICustomIndicatorRegistry>(),
                Substitute.For<AccessibleTrader.Core.Services.Indicators.IIndicatorEngine>(),
                Substitute.For<IIndicatorPreferencesService>());
            svc.RegisterSeriesFromConfig(new SeriesConfig
            {
                Id = "d1", Name = "Trend (1)", IndicatorCode = "",
                Drawing = new DrawingData { Type = DrawingType.TrendLine, AnchorPrice1 = 100 },
            });

            Assert.NotNull(added);
            Assert.True(added!.IsDrawing);
            Assert.Equal(DrawingType.TrendLine, added.Drawing!.Type);
        }
    }
}
