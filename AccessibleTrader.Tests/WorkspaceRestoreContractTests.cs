using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Newtonsoft.Json;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// The workspace's restore contract, written down and enforced.
///
/// <para>
/// Restoring a workspace is deliberately NOT "put everything back". It is a three-layer merge:
/// provider metadata supplies colours, waveforms and shapes so a provider improvement reaches
/// charts that already exist; the workspace supplies the handful of things the user set with a
/// key or a checkbox; saved preferences win over both. That design is right, and it is exactly
/// why "did this field come back?" has no single answer — which is how seven fields came to be
/// dropped one at a time, each found by Cody rather than by a test.
/// </para>
///
/// <para>
/// So this file makes the answer explicit. Every property on <see cref="SeriesConfig"/> and
/// <see cref="ComponentConfig"/> must be declared as either <b>user-owned</b> (the workspace is
/// the source of truth; it must survive a restore) or <b>not user-owned</b> (metadata,
/// preferences or derivation own it; it must not be read from the workspace). A property in
/// neither list fails the build. That turns "someone forgot" into a decision that cannot be
/// skipped, and the user-owned half is then asserted end-to-end through the real
/// <see cref="SeriesManagementService.RestoreSeriesFromSaved"/>.
/// </para>
///
/// <para>
/// It found one live drop when it was written: <c>AnnounceAcrossSeries</c>, a checkbox in the
/// Properties dialog (PropertiesModal.razor:144) that the workspace file has always carried and
/// the restore path never read back. Same shape as the narration flag fixed on 2026-09-05 — set
/// it, restart, it is gone.
/// </para>
/// </summary>
public class WorkspaceRestoreContractTests
{
    // ── The declared contract ────────────────────────────────────────────────────

    /// <summary>
    /// Series-level state the workspace owns. Set by a key or the Properties dialog, written to
    /// disk, and required to come back.
    /// </summary>
    private static readonly string[] UserOwnedSeriesProperties =
    {
        "IsMuted",              // M on a series
        "IsVisible",            // H on a series
        "IsAutoNarrated",       // N on a series
        "AnnounceAcrossSeries", // Properties → announce this series' signals from elsewhere
        "Volume",               // Properties → series volume
    };

    /// <summary>
    /// Series-level state something OTHER than the workspace owns. Each entry says what owns it,
    /// because an exemption with no reason is indistinguishable from an oversight.
    /// </summary>
    private static readonly Dictionary<string, string> NotUserOwnedSeries = new()
    {
        ["Id"]              = "identity — passed to the factory as restoreId, not merged",
        ["IndicatorCode"]   = "identity — selects the provider metadata to rebuild from",
        ["Pane"]            = "identity — passed to the factory, not merged onto the result",
        ["Name"]            = "DERIVED from the parameters at restore time. A workspace owns an "
                            + "indicator's parameters, not its name; pinning the saved name is how "
                            + "the parameter recitation came back from old workspaces (2026-09-05).",
        ["FriendlyName"]    = "derived with Name, same reason",
        ["Parameters"]      = "restored, but through the factory's parameter list rather than by "
                            + "assignment — covered by the round-trip assertion below",
        ["StringParameters"] = "restored through the factory with Parameters, same path",
        ["Components"]      = "rebuilt from provider metadata (layer 1) so a metadata improvement "
                            + "reaches existing charts; the user-owned half is merged in layer 2 "
                            + "and asserted separately below",
        ["Levels"]          = "restored wholesale when present — asserted by its own test below",
        ["CloudFills"]      = "restored wholesale when present, same as Levels",
        ["ZoneBands"]       = "restored wholesale when present, same as Levels",
        ["Drawing"]         = "drawings take the meta == null path and restore their config verbatim",
        ["RangeMin"]        = "provider hint (SymbolRenderHints), re-applied on load by WorkspaceInitializer",
        ["RangeMax"]        = "provider hint, as RangeMin",
        ["SpeakHeaderFirst"] = "no writer anywhere in the app — the field is unreachable, so there "
                            + "is no user state to lose. Delete it or give it a UI; until then it "
                            + "is recorded here rather than silently swept.",
        ["IncludeTimestamp"] = "no writer anywhere in the app — see SpeakHeaderFirst",
    };

    /// <summary>
    /// Per-component state the workspace owns — layer 2 of the merge in
    /// <c>IndicatorModelFactory.CreateSeriesFromMetadata</c>.
    /// </summary>
    private static readonly string[] UserOwnedComponentProperties =
    {
        "IsVisible",      // H on a component
        "IsEnabled",
        "IsMuted",        // M on a component
        "IsAutoNarrated", // N on a component
        "Volume",
        "FreqMultiplier",
        "IsUserStyled",   // "I picked this colour by hand" — the flag that stops a theme
                          // change overwriting it. StandardRenderers.cs:328 reads it.
    };

    // ── The gate: no property may be unclassified ────────────────────────────────

    [Fact]
    public void EverySeriesConfigProperty_IsDeclaredEitherUserOwnedOrNot()
    {
        var props = ReflectionFixture.SettableProperties(typeof(SeriesConfig))
            .Select(p => p.Name).ToList();

        // Vacuity floor — an empty property list would make the assertion below trivially true.
        Assert.True(props.Count >= 15,
            $"only {props.Count} SeriesConfig properties discovered; reflection is not reading the type.");

        var undeclared = props
            .Where(n => !UserOwnedSeriesProperties.Contains(n) && !NotUserOwnedSeries.ContainsKey(n))
            .ToList();

        Assert.True(undeclared.Count == 0,
            $"SeriesConfig.{string.Join(", ", undeclared)} is neither declared user-owned nor " +
            "declared as owned by something else. Decide which it is: if the user sets it, add it " +
            "to UserOwnedSeriesProperties AND to RestoreSeriesFromSaved; if not, record what owns " +
            "it in NotUserOwnedSeries. Seven fields reached users unrestored because this decision " +
            "was never forced.");

        var bothWays = UserOwnedSeriesProperties.Where(NotUserOwnedSeries.ContainsKey).ToList();
        Assert.True(bothWays.Count == 0,
            $"declared both ways: {string.Join(", ", bothWays)}");
    }

    [Fact]
    public void EveryComponentConfigProperty_IsDeclaredEitherUserOwnedOrNot()
    {
        var props = ReflectionFixture.SettableProperties(typeof(ComponentConfig))
            .Select(p => p.Name).ToList();

        Assert.True(props.Count >= 40,
            $"only {props.Count} ComponentConfig properties discovered; reflection is not reading the type.");

        // The component contract is the inverse shape of the series one: the user-owned set is
        // small and closed (layer 2), and everything else belongs to metadata or preferences by
        // design. So the gate is that the user-owned list only names properties that exist —
        // a rename would otherwise silently stop restoring a switch.
        var ghosts = UserOwnedComponentProperties.Where(n => !props.Contains(n)).ToList();
        Assert.True(ghosts.Count == 0,
            $"UserOwnedComponentProperties names properties that no longer exist: " +
            $"{string.Join(", ", ghosts)}. A renamed property silently stops being restored.");
    }

    // ── The contract, asserted end to end ────────────────────────────────────────

    [Fact]
    public void EveryUserOwnedSeriesProperty_SurvivesARestore()
    {
        var dropped = new List<string>();

        foreach (var name in UserOwnedSeriesProperties)
        {
            var prop = typeof(SeriesConfig).GetProperty(name)!;
            var (svc, store) = Build();

            var saved = new SeriesConfig { Id = "s-" + name, IndicatorCode = "EMA", Pane = "Main" };
            saved.Parameters["Period"] = 20;

            object? want = ReflectionFixture.DistinctValue(prop.PropertyType, prop.GetValue(saved));
            Assert.False(ReflectionFixture.Equivalent(prop.GetValue(saved), want),
                $"SeriesConfig.{name}: the fixture value equals the default, so a restore that " +
                "dropped it would pass. Teach DistinctValue about its type.");
            prop.SetValue(saved, want);

            svc.RestoreSeriesFromSaved(saved, EmaMeta());

            var restored = store.State.ActiveSeries.Single(s => s.Id == saved.Id).Config;
            if (!ReflectionFixture.Equivalent(want, prop.GetValue(restored))) dropped.Add(name);
        }

        Assert.True(dropped.Count == 0,
            $"RestoreSeriesFromSaved does not restore: {string.Join(", ", dropped)}. " +
            "These are switches the user set and the workspace file already carries — the read " +
            "side is what is missing, which is how narration and component mute were each lost " +
            "for as long as they had existed.");
    }

    [Fact]
    public void EveryUserOwnedComponentProperty_SurvivesARestore()
    {
        var dropped = new List<string>();

        foreach (var name in UserOwnedComponentProperties)
        {
            var prop = typeof(ComponentConfig).GetProperty(name)!;
            var (svc, store) = Build();

            var saved = new SeriesConfig { Id = "c-" + name, IndicatorCode = "TWOCOMP", Pane = "Oscillator" };
            var comp = new ComponentConfig { Name = "Buy" };
            object? want = ReflectionFixture.DistinctValue(prop.PropertyType, prop.GetValue(comp));
            Assert.False(ReflectionFixture.Equivalent(prop.GetValue(comp), want),
                $"ComponentConfig.{name}: the fixture value equals the default.");
            prop.SetValue(comp, want);
            saved.Components.Add(comp);

            svc.RestoreSeriesFromSaved(saved, TwoComponentMeta());

            var restored = store.State.ActiveSeries.Single(s => s.Id == saved.Id)
                .Components.Single(c => c.Name == "Buy");
            if (!ReflectionFixture.Equivalent(want, prop.GetValue(restored))) dropped.Add(name);
        }

        Assert.True(dropped.Count == 0,
            $"the layer-2 component merge does not restore: {string.Join(", ", dropped)}. " +
            "Add them to IndicatorModelFactory.CreateSeriesFromMetadata's layer 2.");
    }

    [Fact]
    public void ParametersAndLevels_SurviveARestore()
    {
        // The four collection properties NotUserOwnedSeries records as "restored wholesale" or
        // "restored through the factory". Recording a reason is not evidence, so here is the
        // evidence.
        var (svc, store) = Build();
        var saved = new SeriesConfig { Id = "coll-1", IndicatorCode = "EMA", Pane = "Main" };
        saved.Parameters["Period"] = 55;
        saved.StringParameters["MaType"] = "Hull";
        saved.Levels.Add(new LevelConfig { Name = "Mine", Value = 123.5, IsUserDefined = true });
        saved.CloudFills.Add(new CloudFillConfig { UpperComponentName = "U", LowerComponentName = "L" });
        saved.ZoneBands.Add(new ZoneBandConfig { ComponentName = "EMA", DisplayName = "Band" });

        svc.RestoreSeriesFromSaved(saved, EmaMeta());

        var c = store.State.ActiveSeries.Single(s => s.Id == "coll-1").Config;
        Assert.Equal(55, c.Parameters["Period"]);
        Assert.Equal("Hull", c.StringParameters["MaType"]);
        Assert.Equal(123.5, Assert.Single(c.Levels, l => l.Name == "Mine").Value);
        Assert.Single(c.CloudFills);
        Assert.Single(c.ZoneBands);
    }

    // ── Serialisation: what the disk format itself carries ───────────────────────

    [Fact]
    public void EverySeriesConfigProperty_SurvivesTheJsonRoundTrip()
    {
        // Distinct from the restore contract above: this asks only whether the SAVE FORMAT can
        // carry the field at all. A field that does not serialise cannot be restored however
        // careful the restore path is, and the failure looks identical from the outside.
        var original = new SeriesConfig();
        var props = ReflectionFixture.SettableProperties(typeof(SeriesConfig));
        Assert.True(props.Count >= 15, "reflection is not reading SeriesConfig.");

        var expected = new Dictionary<string, object?>();
        foreach (var p in props.Where(p => p.CanWrite))
        {
            object? want = ReflectionFixture.DistinctValue(p.PropertyType, p.GetValue(original));
            if (want == null) continue;
            if (ReflectionFixture.Equivalent(p.GetValue(original), want)) continue;
            p.SetValue(original, want);
            expected[p.Name] = want;
        }

        // Newtonsoft is what WorkspaceLibraryService.SaveProfile uses; a round trip through a
        // different serialiser would prove nothing about the file on disk.
        string json = JsonConvert.SerializeObject(original, Formatting.Indented);
        var back = JsonConvert.DeserializeObject<SeriesConfig>(json)!;

        var lost = expected
            .Where(kv => !ReflectionFixture.Equivalent(kv.Value, typeof(SeriesConfig).GetProperty(kv.Key)!.GetValue(back)))
            .Select(kv => kv.Key).ToList();

        Assert.True(lost.Count == 0,
            $"these SeriesConfig properties do not survive the save format: {string.Join(", ", lost)}. " +
            "Nothing downstream can restore a field the file never carried.");
    }

    [Fact]
    public void AWorkspaceConfiguration_SurvivesSaveLoadSave_Byte_For_Byte()
    {
        // An asymmetric default — one the writer omits and the reader supplies differently —
        // shows up here and nowhere else, because the first save looks perfectly correct.
        var dir = TestTemp.NewDir("att-wsround-");
        try
        {
            var lib = new WorkspaceLibraryService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceLibraryService>.Instance,
                new TempWorkspacePaths()) { LibraryDirectoryOverride = dir };

            var config = new AccessibleTrader.Core.Models.WorkspaceConfiguration
            {
                ActiveTabIndex = 1,
                Tabs =
                {
                    new AccessibleTrader.Core.Models.TabConfiguration
                    {
                        Provider = "Bitstamp", Symbol = "BTC/USD", Timeframe = "4h", Market = "Spot",
                        ViewportStartIndex = 40, ViewportLength = 120,
                        IsHeikinAshi = true, IsLogScale = true,
                        PaneHeightRatios = { ["Oscillator"] = 0.25f },
                        Series =
                        {
                            new SeriesConfig
                            {
                                Id = "ema-1", Name = "EMA 20", IndicatorCode = "EMA", Pane = "Main",
                                IsAutoNarrated = true, AnnounceAcrossSeries = false, IsMuted = true,
                                Parameters = { ["Period"] = 20 },
                                StringParameters = { ["MaType"] = "Hull" },
                                Components = { new ComponentConfig { Name = "EMA", IsMuted = true, MarkerAnchor = MarkerAnchor.AboveBar } },
                                Levels = { new LevelConfig { Name = "Mine", Value = 64000, IsUserDefined = true } },
                            }
                        }
                    }
                }
            };

            lib.SaveProfile("round", config);
            string first = File.ReadAllText(Path.Combine(dir, "round.json"));

            var loaded = lib.LoadProfile("round");
            Assert.NotNull(loaded);
            lib.SaveProfile("round", loaded!);
            string second = File.ReadAllText(Path.Combine(dir, "round.json"));

            Assert.Equal(first, second);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    // ── Scaffolding (the NarrationRestoreTests build, unchanged) ─────────────────

    private static (ISeriesManagementService svc, IWorkspaceStore store) Build()
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(bus, new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());

        var styling = new StylingService(new ComponentRoleMapper(),
            new SonificationProfileProvider(), new PaneAssignmentService());
        var factory = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService());
        var library = new WorkspaceLibraryService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceLibraryService>.Instance,
            new TempWorkspacePaths());
        var registry = new CustomIndicatorRegistry();
        var providers = new List<IIndicatorProvider>();
        var indicatorService = new IndicatorService(providers,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicatorService, registry, providers);

        var svc = new SeriesManagementService(store, bus, factory, styling, library, registry,
            engine, new MockIndicatorPreferencesService());
        return (svc, store);
    }

    private static IndicatorMetadata EmaMeta() => new()
    {
        Code = "EMA",
        Name = "EMA",
        Parameters = { new IndicatorParameterMetadata { Name = "Period", DefaultValue = 20 } },
        Components = { new IndicatorComponentMetadata { Name = "EMA" } }
    };

    private static IndicatorMetadata TwoComponentMeta() => new()
    {
        Code = "TWOCOMP",
        Name = "Two Component",
        DefaultPane = "Oscillator",
        Components =
        {
            new IndicatorComponentMetadata { Name = "Buy" },
            new IndicatorComponentMetadata { Name = "Sell" }
        }
    };
}
