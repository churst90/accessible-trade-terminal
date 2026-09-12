using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using AccessibleTrader.WebHost.Services;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>On a resumed session the terminal did not know what symbol was on screen.</b>
///
/// <para>
/// Reported by Cody, 2026-09-11, after the routing pass shipped: with three workspaces restored
/// and the Bitstamp BTCUSDT 1-minute chart focused, every bar close still produced a MATE
/// notification with its notification sound — on the chart he was looking at.
/// </para>
///
/// <para>
/// ── The cause, and it is one empty string ─────────────────────────────────
/// <c>WorkspaceState.SymbolDisplayName</c> is written by exactly one action,
/// <c>SetProviderContextAction</c>, dispatched from exactly one place —
/// <c>WorkspaceInitializer.InitializeDefaultSeries</c>, which runs only on the <b>Load Chart</b>
/// path. <c>MarketOrchestrator.LoadRestoredActiveTabAsync</c>, the session-resume path, never
/// called it. So after "resume last session" the field was <c>""</c> until the user pressed
/// Load Chart, while <c>Identity.Symbol</c> was correct all along.
/// </para>
///
/// <para>
/// ── What read it, and what each did with an empty answer ──────────────────
/// <list type="bullet">
///   <item><c>WebHostBrowserCircuitHandler.CoveredSymbols</c> — the focused chart contributed
///   NOTHING to the browser's coverage claim, so <c>LocalBackgroundMonitor</c> concluded no
///   browser was watching BTCUSDT and announced its bar close itself: notification plus the
///   notification sound, once a minute. <b>This is the reported bug.</b> The two background
///   tabs were unaffected, because <c>BackgroundWorkspaceMonitor</c> falls back to
///   <c>identity.Symbol</c> — which is why only the FOCUSED chart misbehaved.</item>
///   <item><c>AlertOrchestrator</c>'s Part A symbol gate — a symbol-scoped alert on the focused
///   chart matched nothing and was dropped from <c>applicable</c>, so <b>in-session alerts did
///   not fire at all</b> on a resumed session.</item>
///   <item><c>StrategyEngine</c>'s equivalent gate, and the firing symbol stamped onto an
///   "any symbol" alert for webhook routing.</item>
/// </list>
/// </para>
///
/// <para>
/// The fix is at the source — the resume path sets the field, as the load path does — with
/// coverage additionally offering the raw <c>Identity.Symbol</c>, because a coverage claim that
/// fails towards "nobody is watching this" costs the user a duplicate announcement and there is
/// no reason for it to depend on one spelling.
/// </para>
/// </summary>
public class ResumedSessionSymbolTests
{
    private static (MarketOrchestrator orch, WorkspaceStore store) Make(string? providerDisplayName)
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(
            bus, new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());

        var provider = Substitute.For<IMarketDataProvider>();
        provider.GetDataShapeForSymbol(Arg.Any<string>()).Returns(ProviderDataShape.Ohlcv);
        provider.GetSymbolDisplayName(Arg.Any<string>())
                .Returns(providerDisplayName ?? throw new InvalidOperationException());

        var ds = Substitute.For<IDataService>();
        ds.GetProviderAsync(Arg.Any<string>()).Returns(provider);

        var orch = new MarketOrchestrator(
            ds, Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), bus, new DemoPolicy(isDemo: false));
        return (orch, store);
    }

    private static ChartIdentity Restored() =>
        new() { Provider = "Bitstamp", Symbol = "BTCUSDT", Timeframe = "1m", Market = "Crypto" };

    // ── The source ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resuming a session must leave the terminal knowing what is on screen. It is the whole
    /// defect in one assertion: before the fix this field was the empty string.
    /// </summary>
    [Fact]
    public async Task Resuming_a_session_sets_the_symbol_on_screen()
    {
        var (orch, store) = Make(providerDisplayName: "BTCUSDT");
        store.Dispatch(new SetIdentityAction(Restored()));
        Assert.Equal("", store.State.SymbolDisplayName);   // the state a resume starts from

        await orch.LoadRestoredActiveTabAsync();

        Assert.Equal("BTCUSDT", store.State.SymbolDisplayName);
    }

    /// <summary>
    /// The provider's own display name wins where it has one — an analytics series reads
    /// "Fear and Greed Index", not its ticker — which is the reason the load path asks at all.
    /// </summary>
    [Fact]
    public async Task The_providers_display_name_is_used_when_it_has_one()
    {
        var (orch, store) = Make(providerDisplayName: "Bitcoin / US Dollar");
        store.Dispatch(new SetIdentityAction(Restored()));

        await orch.LoadRestoredActiveTabAsync();

        Assert.Equal("Bitcoin / US Dollar", store.State.SymbolDisplayName);
    }

    /// <summary>A provider lookup that fails must not leave the field blank — blank is the bug.</summary>
    [Fact]
    public async Task A_provider_that_cannot_be_resolved_falls_back_to_the_raw_symbol()
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(
            bus, new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());
        var ds = Substitute.For<IDataService>();
        ds.GetProviderAsync(Arg.Any<string>()).Returns((IMarketDataProvider?)null);
        var orch = new MarketOrchestrator(
            ds, Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), bus, new DemoPolicy(isDemo: false));

        store.Dispatch(new SetIdentityAction(Restored()));
        await orch.LoadRestoredActiveTabAsync();

        Assert.Equal("BTCUSDT", store.State.SymbolDisplayName);
    }

    // ── The consumer that produced the report ────────────────────────────────

    /// <summary>
    /// Cody's exact shape: a resumed session, nothing loaded by hand, and the question the
    /// background monitor asks — "is a browser already announcing BTCUSDT?" The answer must be
    /// yes, whichever of the two spellings of "the symbol on screen" the state happens to hold.
    /// </summary>
    [Fact]
    public void Coverage_claims_the_focused_chart_even_before_its_display_name_is_known()
    {
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Identity = Restored(),
            SymbolDisplayName = "",     // the resumed-session state, before the fix above
        });

        var covered = WebHostBrowserCircuitHandler.CoveredSymbols(store, monitoring: null).ToList();

        Assert.Contains("BTCUSDT", covered);
    }

    /// <summary>And it still claims the display name when there is one, for the venues whose
    /// display name is what the alert list was written against.</summary>
    [Fact]
    public void Coverage_claims_both_spellings_when_they_differ()
    {
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Identity = Restored(),
            SymbolDisplayName = "Bitcoin / US Dollar",
        });

        var covered = WebHostBrowserCircuitHandler.CoveredSymbols(store, monitoring: null).ToList();

        Assert.Contains("BTCUSDT", covered);
        Assert.Contains("Bitcoin / US Dollar", covered);
    }

    /// <summary>
    /// The second door onto the same bug: a tab SWITCH restores <c>SymbolDisplayName</c> from
    /// the switched-to tab's snapshot, and on a resumed session every snapshot carries the empty
    /// string. Fixing only the resume path would have left Cody one keystroke from the same
    /// notification-a-minute on his other two workspaces.
    /// </summary>
    [Fact]
    public async Task Switching_to_another_restored_tab_also_sets_the_symbol_on_screen()
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(
            bus, new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());

        var provider = Substitute.For<IMarketDataProvider>();
        provider.GetDataShapeForSymbol(Arg.Any<string>()).Returns(ProviderDataShape.Ohlcv);
        provider.GetSymbolDisplayName(Arg.Any<string>()).Returns("KASUSDT");
        var ds = Substitute.For<IDataService>();
        ds.GetProviderAsync(Arg.Any<string>()).Returns(provider);

        // Store and orchestrator share ONE bus, as they do per-circuit in production, so the
        // store's TabSwitchedEvent reaches the orchestrator's subscription.
        using var orch = new MarketOrchestrator(
            ds, Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), bus, new DemoPolicy(isDemo: false));

        store.Dispatch(new SetIdentityAction(new ChartIdentity
        {
            Provider = "MEXC", Symbol = "KASUSDT", Timeframe = "1d", Market = "Crypto|Spot",
        }));
        Assert.Equal("", store.State.SymbolDisplayName);

        bus.Publish(new TabSwitchedEvent(store.State.ActiveTabIndex, "KASUSDT"));

        for (int i = 0; i < 300 && string.IsNullOrEmpty(store.State.SymbolDisplayName); i++)
            await Task.Delay(10);

        Assert.Equal("KASUSDT", store.State.SymbolDisplayName);
    }

    /// <summary>A chart with no symbol claims nothing — coverage of "" would suppress everything.</summary>
    [Fact]
    public void An_empty_chart_claims_nothing()
    {
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Identity = new ChartIdentity { Provider = "Bitstamp", Symbol = "", Timeframe = "1m", Market = "Crypto" },
            SymbolDisplayName = "",
        });

        Assert.Empty(WebHostBrowserCircuitHandler.CoveredSymbols(store, monitoring: null));
    }
}
