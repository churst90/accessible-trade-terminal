// The single enrollment point for the modal contract suites. Every dialog in the
// RCL is listed here once, with how to open it and any state its ShowAsync needs;
// ModalAccessibilityContractTests, AriaValueScanTests and ModalDisposeLeakTests all
// iterate this catalog, so adding a modal here buys it the focus contract, the
// aria-value scan and the dispose-leak sweep in one line.
//
// Completeness is enforced by CatalogCoversEveryOpenSubscription below: a source
// scan over the RCL for Open*-event subscriptions fails the suite when a new modal
// is not enrolled. (Scan matches the subscription CALL, not the event name alone,
// so a modal that opens through a different mechanism still gets caught as long as
// it subscribes to an Open* event — the convention every modal follows today.)

// Razor components live in this namespace. An "unused using" sweep run before
// BlazorClient.Components has generated its component types will not see them and
// will offer to delete this line; it is used. See the same note in WebHost/Program.cs.
using AccessibleTrader.BlazorClient.Components;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public sealed record ModalCase(
    string Name,
    Func<TestContext, IRenderedFragment> Render,
    Action<IEventBus> Open,
    Action<BlazorTestHarness>? Seed = null);

/// <summary>Non-dialog components rendered bare (no open event) so the aria scan
/// and dispose sweep still cover them.</summary>
public sealed record BareCase(
    string Name,
    Func<TestContext, IRenderedFragment> Render,
    Action<BlazorTestHarness>? Seed = null);

public static class ModalCatalog
{
    /// <summary>Seed a minimal chart identity + one valid focused series — what
    /// Properties, Wallet/Withdraw and the trading dashboard need to get past
    /// their "nothing loaded" guards.</summary>
    public static void SeedChartState(BlazorTestHarness h)
    {
        var config = new SeriesConfig { Id = "candles", Name = "Candles", FriendlyName = "Candles" };
        var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" });
        var state = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "kraken", "BTC/USD", "1h"),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = "candles",
        };
        h.WorkspaceStore.State.Returns(_ => state);
    }

    private static void SeedWorkspaceProfiles(BlazorTestHarness h) =>
        h.WorkspaceLibrary.GetAvailableProfiles().Returns(new List<string>());

    public static readonly IReadOnlyList<ModalCase> Dialogs = new ModalCase[]
    {
        new("AddIndicatorModal",     c => c.RenderComponent<AddIndicatorModal>(),     b => b.Publish(new OpenAddIndicatorEvent())),
        new("AIAnalystModal",        c => c.RenderComponent<AIAnalystModal>(),        b => b.Publish(new OpenAIAnalystEvent())),
        new("AlertsModal",           c => c.RenderComponent<AlertsModal>(),           b => b.Publish(new OpenAlertsEvent())),
        new("ApiKeysModal",          c => c.RenderComponent<ApiKeysModal>(),          b => b.Publish(new OpenApiKeysEvent())),
        new("AssetDossierModal",     c => c.RenderComponent<AssetDossierModal>(),     b => b.Publish(new OpenAssetDossierEvent()), SeedChartState),
        new("CustomScriptsModal",    c => c.RenderComponent<CustomScriptsModal>(),    b => b.Publish(new OpenCustomScriptsEvent())),
        new("DrawingToolsModal",     c => c.RenderComponent<DrawingToolsModal>(),     b => b.Publish(new OpenDrawingToolsEvent())),
        new("HelpModal",             c => c.RenderComponent<HelpModal>(),             b => b.Publish(new OpenHelpEvent())),
        new("JournalModal",          c => c.RenderComponent<JournalModal>(),          b => b.Publish(new OpenJournalEvent())),
        new("LabelTextModal",        c => c.RenderComponent<LabelTextModal>(),        b => b.Publish(new PromptForLabelTextEvent("candles")), SeedChartState),
        new("LevelReportModal",      c => c.RenderComponent<LevelReportModal>(),      b => b.Publish(new OpenLevelReportEvent()), SeedChartState),
        new("LoadWorkspaceModal",    c => c.RenderComponent<LoadWorkspaceModal>(),    b => b.Publish(new OpenLoadWorkspaceEvent()), SeedWorkspaceProfiles),
        new("MyDataModal",           c => c.RenderComponent<MyDataModal>(),           b => b.Publish(new OpenMyDataEvent())),
        new("ObjectTreeModal",       c => c.RenderComponent<ObjectTreeModal>(),       b => b.Publish(new OpenObjectTreeEvent()), SeedChartState),
        new("OrderBookModal",        c => c.RenderComponent<OrderBookModal>(),        b => b.Publish(new OpenOrderBookEvent()), SeedChartState),
        new("PropertiesModal",       c => c.RenderComponent<PropertiesModal>(),       b => b.Publish(new OpenPropertiesEvent()), SeedChartState),
        new("SaveWorkspaceModal",    c => c.RenderComponent<SaveWorkspaceModal>(),    b => b.Publish(new OpenSaveWorkspaceEvent()), SeedWorkspaceProfiles),
        new("SettingsModal",         c => c.RenderComponent<SettingsModal>(),         b => b.Publish(new OpenSettingsEvent())),
        new("SoundDesignerModal",    c => c.RenderComponent<SoundDesignerModal>(),    b => b.Publish(new OpenSoundDesignerEvent())),
        new("StrategyModal",         c => c.RenderComponent<StrategyModal>(),         b => b.Publish(new OpenStrategiesEvent())),
        new("ThemeEditorModal",      c => c.RenderComponent<ThemeEditorModal>(),      b => b.Publish(new OpenThemeEditorEvent())),
        new("TradingDashboardModal", c => c.RenderComponent<TradingDashboardModal>(), b => b.Publish(new OpenTradingDashboardEvent()), SeedChartState),
        new("WalletModal",           c => c.RenderComponent<WalletModal>(),           b => b.Publish(new OpenWalletEvent()), SeedChartState),
        new("WatchlistModal",        c => c.RenderComponent<WatchlistModal>(),        b => b.Publish(new OpenWatchlistEvent()), SeedChartState),
        new("WithdrawModal",         c => c.RenderComponent<WithdrawModal>(),         b => b.Publish(new OpenWithdrawEvent()), SeedChartState),
    };

    public static readonly IReadOnlyList<BareCase> BareComponents = new BareCase[]
    {
        new("Toolbar",      c => c.RenderComponent<Toolbar>(),      SeedChartState),
        new("StatusBar",    c => c.RenderComponent<StatusBar>()),
        new("IndicatorBar", c => c.RenderComponent<IndicatorBar>(), SeedChartState),
    };

    public static ModalCase Dialog(string name) => Dialogs.Single(d => d.Name == name);
    public static BareCase  Bare(string name)   => BareComponents.Single(d => d.Name == name);

    public static TheoryData<string> DialogNames
    {
        get { var d = new TheoryData<string>(); foreach (var c in Dialogs) d.Add(c.Name); return d; }
    }

    public static TheoryData<string> BareNames
    {
        get { var d = new TheoryData<string>(); foreach (var c in BareComponents) d.Add(c.Name); return d; }
    }

    /// <summary>Open a catalog dialog inside a fresh harness. Callers own the
    /// harness (dispose it) and get the rendered fragment back.</summary>
    public static IRenderedFragment OpenDialog(BlazorTestHarness h, ModalCase c)
    {
        c.Seed?.Invoke(h);
        var cut = c.Render(h.Ctx);
        cut.InvokeAsync(() => c.Open(h.EventBus)).GetAwaiter().GetResult();
        return cut;
    }
}

public class ModalCatalogTests
{
    private static string ComponentsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "AccessibleTrader.BlazorClient.Components");
    }

    /// <summary>The enrollment guard: any .razor file that subscribes to an
    /// Open*/PromptFor* event must appear in the catalog. Matches the
    /// subscription call itself (Subscribe&lt;...&gt; or AsObservable&lt;...&gt;)
    /// rather than the mere presence of an event name, so publishing or
    /// forwarding an event elsewhere does not satisfy the guard.</summary>
    [Fact]
    public void CatalogCoversEveryOpenSubscription()
    {
        var subscribeCall = new Regex(
            @"(?:Subscribe|AsObservable)\s*<\s*(?:Open\w+Event|PromptFor\w+Event)\s*>",
            RegexOptions.Compiled);

        var enrolled = ModalCatalog.Dialogs.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var file in Directory.EnumerateFiles(ComponentsDir(), "*.razor", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var name = Path.GetFileNameWithoutExtension(file);
            // Context menus are focus-managed popups, not dialogs; they have their
            // own dedicated test files (ChartContextMenuTests) and no h2 heading.
            if (name is "ChartContextMenu" or "DrawingContextMenu") continue;
            if (!subscribeCall.IsMatch(File.ReadAllText(file))) continue;
            if (!enrolled.Contains(name)) missing.Add(name);
        }

        Assert.True(missing.Count == 0,
            "Components subscribing to an Open* event but not enrolled in ModalCatalog: "
            + string.Join(", ", missing)
            + ". Add a ModalCase so the focus/aria/dispose contract suites cover them.");
    }

    /// <summary>Vacuity check for the guard above: the regex must actually match
    /// the subscription idioms in use (a rewrite of the pattern that matches
    /// nothing would silently disarm the guard).</summary>
    [Fact]
    public void OpenSubscriptionRegex_MatchesKnownIdioms()
    {
        var subscribeCall = new Regex(
            @"(?:Subscribe|AsObservable)\s*<\s*(?:Open\w+Event|PromptFor\w+Event)\s*>");
        Assert.Matches(subscribeCall, "EventBus.Subscribe<OpenSettingsEvent>(_ => { })");
        Assert.Matches(subscribeCall, "EventBus.AsObservable<OpenSoundDesignerEvent>()");
        Assert.Matches(subscribeCall, "EventBus.Subscribe<PromptForLabelTextEvent>(e => { })");
        Assert.DoesNotMatch(subscribeCall, "EventBus.Publish(new OpenSettingsEvent())");
    }
}
