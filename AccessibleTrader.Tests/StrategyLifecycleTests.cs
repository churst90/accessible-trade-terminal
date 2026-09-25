using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// What Start, Stop, Pause and Add to Engine DO to the running set and to the next launch.
    ///
    /// <para>
    /// Written for mutation campaign A2l (2026-09-24). <see cref="StrategyModalCoordinator"/> and
    /// <see cref="StrategyLibraryFacade"/> are the two places a user's button press becomes a
    /// running strategy, and neither had a test of its own: the Blazor modal tests substitute the
    /// coordinator, so deleting the de-duplication in Start (a second copy of a running spec,
    /// every entry doubled), leaving Stop's auto-activate flag set (a stopped strategy trading
    /// again after a restart) and swapping Pause for Resume all left 8,088 tests green.
    /// </para>
    ///
    /// <para>
    /// The engine here is a RECORDING fake, not a mock: it holds the running set the way the real
    /// engine does (add, remove by instance id, pause by instance id) so the assertions are about
    /// the state a trader would see, not about which method was called.
    /// </para>
    /// </summary>
    public class StrategyLifecycleTests : IDisposable
    {
        private readonly string _dir;

        public StrategyLifecycleTests()
        {
            _dir = TestTemp.NewPath("att-strategy-lifecycle-");
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        // ── Harness ──────────────────────────────────────────────────────────────

        private sealed class FakePaths : IPlatformPathService
        {
            public FakePaths(string dir) { AppDataDirectory = dir; CacheDirectory = dir; }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        private sealed class RecordingEngine : IStrategyEngine
        {
            private readonly List<ActiveStrategy> _active = new();
            public IReadOnlyList<ActiveStrategy> ActiveStrategies => _active;

            public string AddStrategy(ITradingStrategy strategy, IDictionary<string, object>? parameters = null,
                StrategyExecutionMode mode = StrategyExecutionMode.Suggestion,
                string? specId = null, string? bindSymbol = null)
            {
                string id = Guid.NewGuid().ToString("N");
                _active.Add(new ActiveStrategy(id, strategy, parameters ?? new Dictionary<string, object>(),
                    mode, IsPaused: false, bindSymbol, specId));
                return id;
            }

            public void RemoveStrategy(string instanceId) => _active.RemoveAll(a => a.InstanceId == instanceId);

            public void PauseStrategy(string instanceId, bool paused)
            {
                int i = _active.FindIndex(a => a.InstanceId == instanceId);
                if (i >= 0) _active[i] = _active[i] with { IsPaused = paused };
            }

            public void SetExecutionMode(string instanceId, StrategyExecutionMode mode) { }
        }

        /// <summary>The real factory's strategy answers to the spec's id; so does this one.</summary>
        private static IConfigurableStrategyFactory Factory()
        {
            var factory = Substitute.For<IConfigurableStrategyFactory>();
            factory.Create(Arg.Any<StrategySpec>(), Arg.Any<string?>()).Returns(ci =>
            {
                var spec = ci.Arg<StrategySpec>();
                var s = Substitute.For<ITradingStrategy>();
                s.Id.Returns(spec.Id);
                s.Name.Returns(spec.Name);
                return s;
            });
            return factory;
        }

        private static StrategySpec Spec(string id, string name = "Trend pullback") => new(
            Id: id,
            Name: name,
            Description: "a spec",
            Side: OrderSide.Buy,
            Conditions: new ConditionLeaf("leaf", "REGIME.AboveSma200", LeafOperator.GreaterThan, Value: 0),
            Risk: new RiskPlan(
                new StopSource(StopSourceKind.AtrMultiple),
                new List<TpLadderRung> { new(TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 1.0) },
                new PositionSizing(),
                new EntryTrigger()));

        private sealed class Rig
        {
            public readonly RecordingEngine Engine = new();
            public readonly JsonStrategyLibrary Library;
            public readonly IRoslynScriptingService Roslyn = Substitute.For<IRoslynScriptingService>();
            public readonly StrategyModalCoordinator Coordinator;
            public readonly StrategyLibraryFacade Facade;

            public Rig(string dir)
            {
                var paths = new FakePaths(dir);
                Library = new JsonStrategyLibrary(paths);
                var factory = Factory();
                Coordinator = new StrategyModalCoordinator(Engine, Substitute.For<IStrategyBacktester>(),
                    Substitute.For<IBacktestWarmupAnalyzer>(), Library, factory, Roslyn);
                Facade = new StrategyLibraryFacade(Library, factory, Engine, paths);
            }
        }

        private static EditableStrategySpec Editable(string name = "Built by ear")
        {
            var e = new EditableStrategySpec { Name = name };
            // A persistent (non-pulse) leaf so the validator has nothing to refuse.
            e.Root = new EditableConditionNode
            {
                IsGroup = false,
                SignalDescriptorId = "RSI.RSI",
                Operator = LeafOperator.LessThan,
                Value = 30,
            };
            return e;
        }

        // ── Start ────────────────────────────────────────────────────────────────

        [Fact]
        public void Starting_a_spec_that_is_already_running_leaves_exactly_one_instance()
        {
            var rig = new Rig(_dir);
            rig.Library.Upsert(Spec("s1"));

            Assert.True(rig.Coordinator.StartSpec("s1").IsSuccess);
            Assert.True(rig.Coordinator.StartSpec("s1").IsSuccess);

            // Two instances of one spec evaluate the same bars and place every entry twice.
            var running = Assert.Single(rig.Engine.ActiveStrategies);
            Assert.Equal("s1", running.SpecId);
            Assert.True(rig.Library.GetById("s1")!.IsAutoActivate);
        }

        // ── Stop ─────────────────────────────────────────────────────────────────

        [Fact]
        public void Stopping_a_spec_removes_it_and_disarms_it_for_the_next_launch()
        {
            var rig = new Rig(_dir);
            rig.Library.Upsert(Spec("s1"));
            rig.Coordinator.StartSpec("s1");

            Assert.True(rig.Coordinator.StopSpec("s1").IsSuccess);

            Assert.Empty(rig.Engine.ActiveStrategies);
            // The flag is what StrategyAutoLoader reads at the next launch. Left true, the
            // strategy the user stopped is trading again after a restart.
            Assert.False(rig.Library.GetById("s1")!.IsAutoActivate);
            // And it survives the round trip to disk, which is where the next launch reads it.
            rig.Library.Reload();
            Assert.False(rig.Library.GetById("s1")!.IsAutoActivate);
        }

        // ── Pause / resume ───────────────────────────────────────────────────────

        [Fact]
        public void Pause_pauses_a_running_strategy_and_resume_resumes_it()
        {
            var rig = new Rig(_dir);
            rig.Library.Upsert(Spec("s1"));
            rig.Coordinator.StartSpec("s1");
            string id = rig.Engine.ActiveStrategies.Single().InstanceId;

            var paused = rig.Coordinator.TogglePause(id, currentlyPaused: false);
            Assert.Equal("Paused.", paused.Message);
            Assert.True(rig.Engine.ActiveStrategies.Single().IsPaused,
                "The UI said 'Paused.' and the strategy is still evaluating bars.");

            var resumed = rig.Coordinator.TogglePause(id, currentlyPaused: true);
            Assert.Equal("Resumed.", resumed.Message);
            Assert.False(rig.Engine.ActiveStrategies.Single().IsPaused);
        }

        // ── Compiled scripts ─────────────────────────────────────────────────────

        [Fact]
        public async Task A_compiled_script_is_saved_armed_and_running_under_its_library_id()
        {
            var rig = new Rig(_dir);
            var script = Substitute.For<ITradingStrategy>();
            script.Id.Returns("my-script");
            script.Name.Returns("My script");
            rig.Roslyn.CompileStrategyAsync(Arg.Any<string>())
                .Returns(new CompileStrategyResult(true, script, Array.Empty<string>()));

            var r = await rig.Coordinator.CompileAndAddStrategyAsync("// code", StrategyExecutionMode.Suggestion);
            Assert.True(r.IsSuccess, r.Message);

            // Saved with IsAutoActivate so StrategyAutoLoader recompiles it at the next launch —
            // otherwise the script the user added is silently absent after a restart.
            var saved = rig.Library.GetById("my-script");
            Assert.NotNull(saved);
            Assert.True(saved!.IsAutoActivate);
            Assert.Equal("// code", saved.RoslynSource);

            // The running instance carries the library id, as every other path that starts a
            // library strategy does (StartSpec, the auto-loader). Without it a position the
            // script opens is persisted with no spec id and cannot be re-adopted after a
            // restart, and a workspace save drops the strategy.
            Assert.Equal("my-script", rig.Engine.ActiveStrategies.Single().SpecId);
        }

        // ── The builder's Add to Engine ──────────────────────────────────────────

        [Fact]
        public void Add_to_engine_runs_the_spec_under_its_library_id()
        {
            var rig = new Rig(_dir);
            var spec = Editable();

            var r = rig.Facade.AddToEngine(spec);
            Assert.True(r.IsSuccess, r.Message);

            // StrategyPositionManager.OpenPosition stores active.SpecId; Adopt matches on it after
            // a restart; WorkspaceLibraryService saves only instances that have one. A builder
            // strategy added without it opens positions that come back as orphans — and the
            // auto-loader, which DOES pass the id, rebuilds the strategy flat beside them.
            var running = Assert.Single(rig.Engine.ActiveStrategies);
            Assert.False(string.IsNullOrEmpty(running.SpecId), "Add to Engine started the strategy with no library spec id.");
            Assert.Equal(spec.LoadedId, running.SpecId);
            Assert.True(rig.Library.GetById(spec.LoadedId)!.IsAutoActivate);
        }

        [Fact]
        public void Re_adding_an_edited_spec_replaces_the_running_copy_rather_than_joining_it()
        {
            var rig = new Rig(_dir);
            var spec = Editable();
            rig.Facade.AddToEngine(spec);

            spec.Name = "Built by ear, tightened";
            var r = rig.Facade.AddToEngine(spec);

            Assert.True(r.IsSuccess, r.Message);
            Assert.Contains("updated in engine", r.Message);
            var running = Assert.Single(rig.Engine.ActiveStrategies);
            Assert.Equal("Built by ear, tightened", running.Strategy.Name);
        }

        [Fact]
        public void Saving_an_opened_strategy_updates_it_in_place_rather_than_adding_a_copy()
        {
            var rig = new Rig(_dir);
            rig.Library.Upsert(Spec("s1", "Original"));

            var editing = new EditableStrategySpec();
            Assert.True(rig.Facade.LoadFromLibrary(editing, "s1").IsSuccess);
            editing.Name = "Renamed";
            Assert.True(rig.Facade.Save(editing).IsSuccess);
            editing.Name = "Renamed again";
            Assert.True(rig.Facade.Save(editing).IsSuccess);

            var only = Assert.Single(rig.Library.All);
            Assert.Equal("s1", only.Id);
            Assert.Equal("Renamed again", only.Name);
        }

        [Fact]
        public void The_library_replaces_a_spec_by_id_wherever_it_sits_in_the_list_including_first()
        {
            var lib = new JsonStrategyLibrary(new FakePaths(_dir));
            lib.Upsert(Spec("first", "First"));
            lib.Upsert(Spec("second", "Second"));

            lib.Upsert(Spec("first", "First, edited"));
            lib.Upsert(Spec("second", "Second, edited"));

            Assert.Equal(new[] { "First, edited", "Second, edited" }, lib.All.Select(s => s.Name));
            lib.Reload();
            Assert.Equal(2, lib.All.Count);
        }

        [Fact]
        public void A_new_strategy_saved_twice_is_one_strategy()
        {
            var rig = new Rig(_dir);
            var spec = Editable();
            rig.Facade.Save(spec);
            spec.Name = "Second thoughts";
            rig.Facade.Save(spec);

            Assert.Equal("Second thoughts", Assert.Single(rig.Library.All).Name);
        }

        // ── The next launch ──────────────────────────────────────────────────────

        [Fact]
        public async Task At_launch_only_the_armed_specs_start_and_a_second_load_starts_nothing_more()
        {
            var rig = new Rig(_dir);
            rig.Library.Upsert(Spec("armed", "Armed") with { IsAutoActivate = true });
            rig.Library.Upsert(Spec("template", "Kept as a template"));

            var loader = new StrategyAutoLoader(rig.Library, Factory(), rig.Engine,
                Substitute.For<AccessibleTrader.Sdk.Logging.IAppLogger>());

            await loader.LoadAllAsync();
            var running = Assert.Single(rig.Engine.ActiveStrategies);
            Assert.Equal("armed", running.SpecId);

            // MainLayout can resolve and call it again; a second pass must not register every
            // armed strategy a second time.
            await loader.LoadAllAsync();
            Assert.Single(rig.Engine.ActiveStrategies);
        }

        [Fact]
        public void Save_refuses_a_spec_whose_entry_trigger_can_never_fire_and_writes_nothing()
        {
            var rig = new Rig(_dir);
            var spec = Editable();
            // A pure-pulse tree (one Fired leaf) with a deferred trigger: by the next bar the
            // conditions are false again, so the pullback can never be waited for.
            spec.Root = new EditableConditionNode { IsGroup = false, SignalDescriptorId = "X.Cross", Operator = LeafOperator.Fired };
            spec.EntryKind = EntryTriggerKind.OnPullbackToLevel;

            var r = rig.Facade.Save(spec);

            Assert.False(r.IsSuccess);
            Assert.StartsWith("Cannot save", r.Message);
            Assert.Empty(rig.Library.All);
        }
    }
}
