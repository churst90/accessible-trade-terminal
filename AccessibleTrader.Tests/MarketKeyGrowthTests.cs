using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The market field that grew by one segment per Load Chart.</b>
///
/// <para>
/// Found in Cody's real session file on 2026-09-11: a MEXC tab whose <c>Market</c> read
/// <c>"Crypto|Crypto|Crypto|Crypto|Spot"</c> and another with eight segments. The loop is
/// <c>MarketOrchestrator</c> composing <c>"{EffectiveMarket}|{_selectedSubType}"</c> into the
/// identity, the identity subscription adopting the WHOLE composite back into
/// <c>_selectedSubType</c> (a field declared to hold a bare sub-type), and the next compose
/// prepending another category.
/// </para>
///
/// <para>
/// It is not cosmetic. <see cref="ChartIdentity"/> equality includes <c>Market</c>, so every
/// load minted a new identity — fresh cache buckets in the orchestration and pattern caches, a
/// cold start each time — and <c>LocalBackgroundMonitor.WatchKey</c> includes it too, so the
/// background monitor's bar-close seed was orphaned on every load and the first close after a
/// hand-off went unannounced.
/// </para>
/// </summary>
public sealed class MarketKeyGrowthTests
{
    // MEXC is the venue in the real session file: it declares Spot AND Futures, which is what
    // makes the orchestrator compose a composite at all. Bitstamp declares one sub-type, and
    // its tab in the same file reads a clean "Crypto" — the contrast that named the producer.
    /// <param name="quietToolbarSync">
    /// True to make <c>SyncMarketToProviderAsync</c> return at its first line, by declaring no
    /// supported markets for the provider. That method runs on a background task out of the
    /// identity subscription and rewrites the very fields under test — including resetting an
    /// unrecognised sub-type back to the list default, which MASKS the growth loop about half
    /// the time. A guard that is only sometimes red is not a guard, so the test that pins the
    /// loop silences that path and the tests that pin the adopt helpers do not need it.
    /// </param>
    private static (MarketOrchestrator orch, WorkspaceStore store) Make(bool quietToolbarSync = false)
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(
            bus, new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());

        var ds = Substitute.For<IDataService>();
        ds.ProviderRequiresApiKeyAsync(Arg.Any<string>()).Returns(false);
        ds.IsProviderConfiguredAsync(Arg.Any<string>()).Returns(true);
        ds.GetSupportedSubTypesAsync(Arg.Any<string>(), Arg.Any<string>())
          .Returns(new List<string> { "Spot", "Futures" });
        ds.LoadSymbolsAsync(Arg.Any<string>(), Arg.Any<string>())
          .Returns(new List<string> { "KASUSDT" });
        ds.GetSupportedTimeframesAsync(Arg.Any<string>())
          .Returns(new List<string> { "1d" });
        ds.GetSupportedMarketsForProviderAsync(Arg.Any<string>())
          .Returns(quietToolbarSync ? new List<MarketType>() : new List<MarketType> { MarketType.Crypto });
        ds.LoadProvidersByMarketTypeAsync(Arg.Any<string>())
          .Returns(new List<string> { "MEXC" });
        ds.LoadAvailableMarketsAsync().Returns(new List<string> { "Crypto" });
        ds.GetProviderAsync(Arg.Any<string>()).Returns((IMarketDataProvider?)null);

        var orch = new MarketOrchestrator(
            ds, Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), bus, new DemoPolicy(isDemo: false));
        return (orch, store);
    }

    private static async Task<MarketOrchestrator> ReadyOrchestrator(WorkspaceStore store, MarketOrchestrator orch)
    {
        orch.SelectedMarket = "Crypto";
        orch.SelectedProvider = "MEXC";
        await orch.RefreshSymbolsAsync();   // populates AvailableSubTypes with TWO entries
        orch.SelectedSymbol = "KASUSDT";
        orch.SelectedTimeframe = "1d";
        return orch;
    }

    /// <summary>
    /// Three loads, and the Market field is the same at the end as after the first.
    ///
    /// <para>Asserted as "never more than two segments" rather than as a fixed string, because
    /// the toolbar's identity subscription runs <c>SyncMarketToProviderAsync</c> on a background
    /// task: while that is in flight the available sub-type list is momentarily repopulated, and
    /// a load landing inside that window legitimately composes the bare category. Two segments is
    /// the invariant that the defect breaks — it added one segment per load, without bound, and
    /// never came back down.</para>
    /// </summary>
    [Fact]
    public async Task Loading_the_same_chart_three_times_does_not_grow_the_market_field()
    {
        var (orch, store) = Make(quietToolbarSync: true);
        await ReadyOrchestrator(store, orch);

        for (int i = 1; i <= 3; i++)
        {
            await orch.LoadChartAsync();

            string market = store.State.Identity.Market;
            Assert.DoesNotContain("Crypto|Crypto", market, StringComparison.OrdinalIgnoreCase);
            Assert.True(market.Split('|').Length <= 2,
                $"after load {i} the market field had grown to \"{market}\"");
            Assert.Equal("Crypto|Spot", market);
        }
    }

    [Fact]
    public async Task Adopting_a_composite_identity_leaves_the_sub_type_bare()
    {
        var (orch, store) = Make();
        await ReadyOrchestrator(store, orch);

        // This is the exact move the identity subscription makes on a workspace restore, a
        // watchlist jump or a tab switch: a composite identity arrives and the toolbar follows.
        store.Dispatch(new SetIdentityAction(new ChartIdentity
        {
            Market = "Crypto|Futures", Provider = "MEXC", Symbol = "KASUSDT", Timeframe = "1d"
        }));

        Assert.Equal("Futures", orch.SelectedSubType);
    }

    [Fact]
    public async Task Adopting_a_bare_market_does_not_make_the_category_the_sub_type()
    {
        var (orch, store) = Make();
        await ReadyOrchestrator(store, orch);
        orch.SelectedSubType = "Futures";

        // Bitstamp's shape: one sub-type, so the identity carries a bare category. There is
        // nothing to adopt, and adopting "Crypto" as a sub-type is how the growth began.
        store.Dispatch(new SetIdentityAction(new ChartIdentity
        {
            Market = "Crypto", Provider = "Bitstamp", Symbol = "BTCUSDT", Timeframe = "1m"
        }));

        Assert.Equal("Futures", orch.SelectedSubType);
        Assert.NotEqual("Crypto", orch.SelectedSubType);
    }

    // ── The normaliser, so a file already on disk heals itself ──────────────────────

    [Theory]
    [InlineData("Crypto|Crypto|Crypto|Crypto|Spot", "Crypto|Spot")]
    [InlineData("Crypto|Crypto|Crypto|Crypto|Crypto|Crypto|Crypto|Crypto", "Crypto")]
    [InlineData("Crypto|Spot", "Crypto|Spot")]
    [InlineData("Crypto", "Crypto")]
    [InlineData("", "")]
    public void Normalize_collapses_a_grown_key(string grown, string expected)
        => Assert.Equal(expected, MarketKey.Normalize(grown));

    [Fact]
    public void Normalize_is_idempotent()
    {
        string once = MarketKey.Normalize("Crypto|Crypto|Crypto|Spot");
        Assert.Equal(once, MarketKey.Normalize(once));
    }

    [Theory]
    [InlineData("Crypto|Spot", "Spot")]
    [InlineData("Crypto|Crypto|Spot", "Spot")]
    [InlineData("Crypto", "")]          // a bare category has NO sub-type
    [InlineData("", "")]
    public void SubType_reads_the_sub_type_half_only(string market, string expected)
        => Assert.Equal(expected, MarketKey.SubType(market));

    [Theory]
    [InlineData("Crypto|Spot", "Crypto")]
    [InlineData("Crypto", "Crypto")]
    public void Category_reads_the_category_half(string market, string expected)
        => Assert.Equal(expected, MarketKey.Category(market));

    [Theory]
    [InlineData("Crypto", "Spot", "Crypto|Spot")]
    [InlineData("Crypto", "", "Crypto")]
    [InlineData("Crypto", "Crypto", "Crypto")]   // composing a category with itself is the bug
    public void Compose_never_repeats_the_category(string cat, string sub, string expected)
        => Assert.Equal(expected, MarketKey.Compose(cat, sub));
}
