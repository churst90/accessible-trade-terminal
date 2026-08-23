// AlertsModal — bUnit coverage. Smallest of the four "big" modals (one inject:
// IAlertOrchestrator). Mostly form + list rendering; the user-visible
// regressions to guard against are the Add/Delete flow and the empty-state.

using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class AlertsModalTests
{
    /// <summary>
    /// In-memory orchestrator that records calls so tests can assert behavior
    /// without spinning up the real alert pipeline (which depends on the
    /// running indicator engine).
    /// </summary>
    private sealed class StubAlertOrchestrator : IAlertOrchestrator
    {
        private readonly List<AlertDefinition> _alerts = new();
        public List<AlertDefinition> Added { get; } = new();
        public List<string> Removed { get; } = new();
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public void Start() => StartCalls++;
        public void Stop() => StopCalls++;
        public void AddAlert(AlertDefinition alert) { _alerts.Add(alert); Added.Add(alert); }
        public void RemoveAlert(string id)
        {
            _alerts.RemoveAll(a => a.Id == id);
            Removed.Add(id);
        }
        public IEnumerable<AlertDefinition> GetAlerts() => _alerts;

        public void SeedAlert(AlertDefinition alert) => _alerts.Add(alert);
    }

    private static (TestContext ctx, StubAlertOrchestrator orch, IEventBus bus) BuildContext()
    {
        var ctx   = new TestContext();
        var orch  = new StubAlertOrchestrator();
        IEventBus bus = new EventBus();
        ctx.Services.AddSingleton<IAlertOrchestrator>(orch);
        ctx.Services.AddSingleton(bus);

        // AlertsModal now injects IWorkspaceStore (current-symbol default) and
        // ISettingsManager (webhook-target dropdown). Provide substitutes with a
        // non-null state so Store.State.SymbolDisplayName doesn't NRE.
        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(WorkspaceState.Initial with { SymbolDisplayName = "BTC/USD" });
        ctx.Services.AddSingleton(store);
        ctx.Services.AddSingleton(Substitute.For<ISettingsManager>());
        // Part D: the Advanced toggle renders ConditionTreeEditor, which injects
        // ISignalCatalog. Empty catalog is fine for render-level tests.
        var catalog = Substitute.For<AccessibleTrader.Core.Services.Strategies.ISignalCatalog>();
        catalog.All.Returns(new List<AccessibleTrader.Sdk.Strategies.SignalDescriptor>());
        ctx.Services.AddSingleton(catalog);

        ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true);
        return (ctx, orch, bus);
    }

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.AlertsModal>
        OpenModal(TestContext ctx, IEventBus bus)
    {
        var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.AlertsModal>();
        cut.InvokeAsync(() => bus.Publish(new OpenAlertsEvent())).GetAwaiter().GetResult();
        return cut;
    }

    private static AlertDefinition NewAlert(string name) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        Target = AlertTarget.Price,
        Condition = AlertCondition.CrossesAbove,
        Threshold = 50_000,
        Delivery = AlertDelivery.Both,
    };

    [Fact]
    public void AlertsModal_HiddenByDefault_RendersEmpty()
    {
        var (ctx, _, _) = BuildContext();
        var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.AlertsModal>();

        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    [Fact]
    public void AlertsModal_NoActiveAlerts_ShowsEmptyState()
    {
        var (ctx, _, bus) = BuildContext();

        var cut = OpenModal(ctx, bus);

        // Empty-state copy from the markup; role="status" makes it
        // screen-reader-announceable.
        var status = cut.Find("p[role='status']");
        Assert.Equal("No active alerts.", status.TextContent);
    }

    [Fact]
    public void AlertsModal_WithSeededAlerts_RendersListItems()
    {
        var (ctx, orch, bus) = BuildContext();
        orch.SeedAlert(NewAlert("BTC crosses 50000"));
        orch.SeedAlert(NewAlert("ETH crosses 3000"));

        var cut = OpenModal(ctx, bus);

        var items = cut.FindAll("li[role='listitem']");
        Assert.Equal(2, items.Count);
        Assert.Contains("BTC crosses 50000", items[0].TextContent);
        Assert.Contains("ETH crosses 3000", items[1].TextContent);
    }

    [Fact]
    public void AlertsModal_DeleteButton_InvokesOrchestratorRemoveAlert()
    {
        var (ctx, orch, bus) = BuildContext();
        var alert = NewAlert("Delete me");
        orch.SeedAlert(alert);

        var cut = OpenModal(ctx, bus);

        cut.Find($"button[aria-label='Delete alert: {alert.Name}']").Click();

        // WaitForAssertion: post-click renders settle asynchronously on slow
        // (2-core) CI runners — the same bUnit timing flake as the modal tests.
        cut.WaitForAssertion(() =>
        {
            Assert.Single(orch.Removed);
            Assert.Equal(alert.Id, orch.Removed[0]);
            // After delete, list goes back to empty state.
            Assert.Equal("No active alerts.", cut.Find("p[role='status']").TextContent);
        });
    }

    [Fact]
    public void AlertsModal_AddButton_DisabledWhenNameBlank()
    {
        var (ctx, _, bus) = BuildContext();

        var cut = OpenModal(ctx, bus);

        var addBtn = cut.Find("button[aria-label='Add alert']");
        Assert.True(addBtn.HasAttribute("disabled"));
    }

    [Fact]
    public void AlertsModal_AddAlert_FillsForm_InvokesOrchestratorAddAlert()
    {
        var (ctx, orch, bus) = BuildContext();

        var cut = OpenModal(ctx, bus);

        // Fill the name; @bind on the input commits on change.
        cut.Find("input#alert-name").Change("Big BTC move");
        cut.Find("input#alert-threshold").Change("65000");

        // WaitForAssertion + re-find: the enable re-render settles asynchronously
        // on slow (2-core) CI runners — same bUnit timing flake as the modal tests.
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("button[aria-label='Add alert']").HasAttribute("disabled")));

        cut.Find("button[aria-label='Add alert']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(orch.Added);
            Assert.Equal("Big BTC move", orch.Added[0].Name);
            Assert.Equal(65000, orch.Added[0].Threshold);
            // After add, the name field is cleared and the row appears in the list.
            Assert.Single(cut.FindAll("li[role='listitem']"));
        });
    }

    [Fact]
    public void AdvancedToggle_ShowsTreeEditor_AndGatesAddOnTree()
    {
        var (ctx, _, bus) = BuildContext();
        var cut = OpenModal(ctx, bus);

        // Off by default: simple fields visible, no tree editor.
        Assert.NotEmpty(cut.FindAll("#alert-target"));

        cut.Find("input#alert-advanced").Change(true);

        cut.WaitForAssertion(() =>
        {
            // Simple Target/Condition/Threshold are replaced by the tree editor.
            Assert.Empty(cut.FindAll("#alert-target"));
            Assert.Empty(cut.FindAll("#alert-threshold"));
        });

        // With a name but an EMPTY tree, Add stays disabled — an advanced alert
        // with no conditions can never fire and must not be creatable.
        cut.Find("input#alert-name").Change("Tree alert");
        cut.WaitForAssertion(() =>
            Assert.True(cut.Find("button[aria-label='Add alert']").HasAttribute("disabled")));
    }

    [Fact]
    public void AddAlert_StampsTheChartIdentity_SoBackgroundMonitorsCanFetch()
    {
        // The modal used to stamp only Symbol. DeriveWatches fetches by
        // (provider, market, symbol, timeframe), so a modal-created alert had
        // nothing to fetch by and was never watchable with the browser closed.
        var (ctx, orch, bus) = BuildContext();
        var cut = OpenModal(ctx, bus);

        cut.Find("input#alert-name").Change("Big BTC move");
        cut.Find("input#alert-threshold").Change("65000");
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("button[aria-label='Add alert']").HasAttribute("disabled")));
        cut.Find("button[aria-label='Add alert']").Click();

        cut.WaitForAssertion(() =>
        {
            var added = Assert.Single(orch.Added);
            // The harness store carries ChartIdentity.Empty: ("Spot", "Bitstamp", "", "1h").
            Assert.Equal("Bitstamp", added.Provider);
            Assert.Equal("Spot", added.Market);
            Assert.Equal("1h", added.Timeframe);
        });
    }

    [Fact]
    public void AddingAChartDependentAlert_SaysBackgroundMonitoringCannotWatchIt()
    {
        // "Nothing warns the user" was the finding: an indicator alert saved
        // fine and then silently never evaluated in the background. The moment
        // of creation is where the limitation must be spoken.
        var (ctx, orch, bus) = BuildContext();
        var spoken = new List<string>();
        bus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) spoken.Add(e.Message); });

        var cut = OpenModal(ctx, bus);
        cut.Find("input#alert-name").Change("RSI watch");
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("button[aria-label='Add alert']").HasAttribute("disabled")));
        cut.Find("button[aria-label='Add alert']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(orch.Added);
            Assert.Contains(spoken, m => m.Contains("cannot watch", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void AddingAPriceAlert_AnnouncesPlainSuccess_NoScaryCaveat()
    {
        // Vacuity check for the warning: a watchable alert must announce success
        // WITHOUT the cannot-watch caveat, or the warning means nothing.
        var (ctx, orch, bus) = BuildContext();
        var spoken = new List<string>();
        bus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) spoken.Add(e.Message); });

        var cut = OpenModal(ctx, bus);
        cut.Find("input#alert-name").Change("Big BTC move");
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("button[aria-label='Add alert']").HasAttribute("disabled")));
        cut.Find("button[aria-label='Add alert']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(orch.Added);
            Assert.Contains(spoken, m => m.Contains("added", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(spoken, m => m.Contains("cannot watch", StringComparison.OrdinalIgnoreCase));
        });
    }
}
