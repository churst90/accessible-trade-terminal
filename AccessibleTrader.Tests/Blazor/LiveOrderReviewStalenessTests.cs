// The live-order review, and whether what was READ ALOUD is what gets SENT.
//
// For a sighted trader the order ticket is on screen while they press Confirm; they can
// see the quantity. For a blind trader the SPOKEN REVIEW *is* the ticket — it is the only
// rendering of the order they get before it goes to the venue. That makes the review a
// safety mechanism, and a safety mechanism that can go stale without saying so is worse
// than none: it is the thing the user trusts instead of checking.
//
// WithdrawModal solved exactly this in 2026-08 with VoidQuote(), and its comment says why:
// "What was read aloud must be exactly what is sent." The order ticket had no equivalent.

using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class LiveOrderReviewStalenessTests
{
    /// <summary>
    /// A dashboard in the one state the review exists for: a LIVE key, paper mode off,
    /// trading supported. Without all three the modal takes the paper branch and submits
    /// directly, and every assertion below would be vacuously true.
    /// </summary>
    private static BlazorTestHarness LiveHarness()
    {
        var h = new BlazorTestHarness();
        ModalCatalog.SeedChartState(h);

        h.Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();

        // GetKeysForProviderAsync, not GetAllKeysAsync — ShowAsync reads the keys for the
        // CHART's provider, and stubbing the wrong one leaves _availableKeys empty, which
        // silently makes _isLiveEnvironment false and takes the paper branch instead.
        var liveKey = new ApiKeyConfig("kraken", "main", "key", "secret",
                                       Environment: "Live", IsActive: true);
        h.ApiKeyService.GetAllKeysAsync().Returns(new List<ApiKeyConfig> { liveKey });
        h.ApiKeyService.GetKeysForProviderAsync(default!)
            .ReturnsForAnyArgs(new List<ApiKeyConfig> { liveKey });

        h.OrderService.SupportsTradingAsync(default!).ReturnsForAnyArgs(true);
        h.OrderService.GetCapabilitiesAsync(default!).ReturnsForAnyArgs(ProviderCapabilities.None);
        h.OrderService.SupportsOcoPairsAsync(default!).ReturnsForAnyArgs(false);
        h.OrderService.GetMaxLeverageAsync(default!).ReturnsForAnyArgs(1.0);
        h.OrderService.GetBalancesAsync(default!)
            .ReturnsForAnyArgs(ProviderResult<List<Balance>>.Ok(new List<Balance>()));
        h.OrderService.GetPositionsAsync(default!)
            .ReturnsForAnyArgs(ProviderResult<List<Position>>.Ok(new List<Position>()));
        h.OrderService.GetOpenOrdersAsync(default!, default)
            .ReturnsForAnyArgs(ProviderResult<List<OpenOrder>>.Ok(new List<OpenOrder>()));
        h.OrderService.GetFillsAsync(default!, default, default)
            .ReturnsForAnyArgs(ProviderResult<List<TradeFill>>.Ok(new List<TradeFill>()));

        return h;
    }

    private static IRenderedComponent<TradingDashboardModal> OpenLiveTicket(BlazorTestHarness h)
    {
        var cut = h.OpenModal<TradingDashboardModal>(b => b.Publish(new OpenTradingDashboardEvent()));
        // ShowAsync is fire-and-forget off the open event; the ticket is not in the DOM
        // until it has run all the way through its trading-supported branch.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#order-qty")), TimeSpan.FromSeconds(10));

        // VACUITY CHECK. Every assertion in this file is about the LIVE-order review, and
        // that branch only exists when the modal believes it is on a live account. In paper
        // mode SubmitOrder sends immediately, no review is ever armed, and a test asserting
        // "nothing was sent after an edit" would pass for the wrong reason forever. The Mode
        // stat is the component's own answer to "am I live?", so read that rather than
        // re-deriving it here.
        Assert.Contains(">Live<", cut.Markup, StringComparison.Ordinal);
        return cut;
    }

    /// <summary>Every quantity this component asked the venue to trade.</summary>
    private static IReadOnlyList<double> SentQuantities(BlazorTestHarness h) =>
        h.OrderService.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(h.OrderService.PlaceOrderAsync))
            .Select(c => ((TradeSignal)c.GetArguments()[1]!).Quantity)
            .ToList();

    /// <summary>
    /// The defect, stated as the user's keystrokes: arm the review at 1 BTC, hear
    /// "Confirm: Buy 1 BTC/USD", edit the quantity to 5, press Confirm.
    ///
    /// <para>
    /// Nothing may reach the venue. The review the user heard described a 1 BTC order and
    /// they never heard one for 5 — so the only honest outcomes are "refused" or "re-read
    /// the review", never "sent".
    /// </para>
    /// </summary>
    [Fact]
    public void EditingTheQuantityAfterTheReviewIsArmedCannotSendTheEditedOrder()
    {
        using var h = LiveHarness();
        var cut = OpenLiveTicket(h);

        // First press arms the review — it does NOT send. If this ever starts sending, the
        // assertion below would pass for the wrong reason, so pin it here.
        cut.Find("button.submit-btn").Click();
        cut.WaitForAssertion(() => Assert.Contains("Confirm", cut.Markup, StringComparison.Ordinal),
                             TimeSpan.FromSeconds(10));
        Assert.Empty(SentQuantities(h));

        // The user Tabs back and retypes the size. The review still says 1.
        cut.Find("#order-qty").Change("5");

        // Whatever the Confirm control is now, press the affirmative one.
        var confirm = cut.FindAll("button.submit-btn").FirstOrDefault();
        Assert.NotNull(confirm);
        confirm!.Click();

        // Give any in-flight submit a chance to land before concluding nothing was sent —
        // otherwise this passes on timing rather than on the guard.
        Thread.Sleep(300);

        Assert.Empty(SentQuantities(h));
    }

    /// <summary>
    /// The same defect reached through a control that is NOT an <c>@bind</c> input.
    ///
    /// <para>
    /// The BUY/SELL pair are plain buttons with an <c>@onclick</c> lambda, so no binding hook
    /// can void the review for them. Arm a Buy, press SELL, press Confirm — the button still
    /// reads "Confirm Buy" from the render before the side changed, and a SELL goes out. This
    /// is why voiding per-field is not sufficient on its own: the guarantee has to be checked
    /// where the order is BUILT, against what was actually read aloud.
    /// </para>
    /// </summary>
    [Fact]
    public void ChangingTheSideAfterTheReviewIsArmedCannotSendTheFlippedOrder()
    {
        using var h = LiveHarness();
        var cut = OpenLiveTicket(h);

        cut.Find("button.submit-btn").Click();
        cut.WaitForAssertion(() => Assert.Contains("Confirm", cut.Markup, StringComparison.Ordinal),
                             TimeSpan.FromSeconds(10));

        cut.Find("button.side-btn.sell").Click();

        // The hook on the side button itself, not the backstop: the user is told at the
        // click, while the Confirm control is still under their hands.
        cut.WaitForAssertion(() => Assert.Contains("Side changed", cut.Markup, StringComparison.Ordinal),
                             TimeSpan.FromSeconds(10));

        cut.FindAll("button.submit-btn").First().Click();
        Thread.Sleep(300);

        Assert.DoesNotContain(h.OrderService.ReceivedCalls(),
            c => c.GetMethodInfo().Name == nameof(h.OrderService.PlaceOrderAsync));
    }

    /// <summary>
    /// Arming the review DESTROYS the control the user is standing on: the markup swaps the
    /// single "Submit Buy Order" button for a Confirm/Cancel pair, so the focused element is
    /// removed from the DOM and focus falls to <c>&lt;body&gt;</c> — on a 2,000-line dialog,
    /// immediately after arming a real-money order.
    ///
    /// <para>
    /// A sighted user sees the new buttons. A screen-reader user is told nothing about where
    /// they now are and has to hunt for the confirm control by Tab, on a dialog where the next
    /// Enter spends money. Focus must land on Confirm, the way <c>ApiKeysModal</c> already
    /// focuses its own dialog after a state swap.
    /// </para>
    /// </summary>
    [Fact]
    public void ArmingTheReviewPutsFocusOnTheConfirmButton()
    {
        using var h = LiveHarness();
        var cut = OpenLiveTicket(h);

        cut.Find("button.submit-btn").Click();
        cut.WaitForAssertion(() => Assert.Contains("Confirm", cut.Markup, StringComparison.Ordinal),
                             TimeSpan.FromSeconds(10));

        // Wall-clock, not render-based: the handler renders BEFORE it awaits the interop
        // call, so there is no later render to wake a WaitForAssertion. See WaitForFocus.
        h.WaitForFocus("order-confirm-live");
    }

    /// <summary>
    /// The per-control void must work ON ITS OWN, at the keystroke.
    ///
    /// <para>
    /// The two tests above are satisfied by the backstop in <c>SubmitOrder</c> alone, so
    /// without this one the <c>@oninput</c> hooks could be dead and every assertion would
    /// still be green — the shape this repo keeps rediscovering. This drives <c>oninput</c>
    /// specifically (not <c>onchange</c>, which is what <c>@bind</c> listens on) and asserts
    /// the review is gone BEFORE any confirm is pressed.
    /// </para>
    /// </summary>
    [Fact]
    public void EditingTheQuantityVoidsTheReviewAtTheKeystroke()
    {
        using var h = LiveHarness();
        var cut = OpenLiveTicket(h);

        cut.Find("button.submit-btn").Click();
        cut.WaitForAssertion(() => Assert.Contains("Confirm", cut.Markup, StringComparison.Ordinal),
                             TimeSpan.FromSeconds(10));

        cut.Find("#order-qty").Input("5");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("no longer matches", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("order-confirm-live", cut.Markup, StringComparison.Ordinal);
        }, TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// An armed confirmation must not survive the dialog it was armed in.
    ///
    /// <para>
    /// It used to: <c>Close()</c> reset both inline editors — with a comment explaining
    /// exactly why a half-typed price must not come back — and left <c>_reviewArmed</c> out,
    /// and <c>ShowAsync()</c> blanked <c>_orderStatus</c> without clearing it either. So
    /// Escape, load a different chart, Alt+T, and the dialog came up with Confirm/Cancel
    /// already rendered and NOTHING spoken or shown to say what was being confirmed. One
    /// Enter sent a live order on the new symbol that had never been reviewed at all.
    /// </para>
    /// </summary>
    [Fact]
    public void AnArmedReviewDoesNotSurviveCloseAndReopen()
    {
        using var h = LiveHarness();
        var cut = OpenLiveTicket(h);

        cut.Find("button.submit-btn").Click();
        cut.WaitForAssertion(() => Assert.Contains("order-confirm-live", cut.Markup, StringComparison.Ordinal),
                             TimeSpan.FromSeconds(10));

        h.EventBus.Publish(new CloseTopModalEvent("Trading dashboard"));
        cut.WaitForAssertion(() => Assert.DoesNotContain("order-qty", cut.Markup, StringComparison.Ordinal),
                             TimeSpan.FromSeconds(10));

        h.EventBus.Publish(new OpenTradingDashboardEvent());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#order-qty")), TimeSpan.FromSeconds(10));

        // Reopened on the plain Submit button, not on a Confirm with no review behind it.
        Assert.DoesNotContain("order-confirm-live", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The spoken review is the readback for an irreversible money action, so it may not ride
    /// the tier F2 silences.
    ///
    /// <para>
    /// It did: <c>FeedbackType.StateChange</c> routes to <c>SpeechChannel.Manual</c>. With
    /// speech off, arming a live order said nothing at all while a REJECTION — Error, hence
    /// Critical — was still spoken, so the terminal announced every refusal and no
    /// confirmation. Every asynchronous order outcome in the app already uses
    /// <c>OrderEvent</c>, the tier whose own docstring calls it "the one feedback you never
    /// miss"; this asserts the synchronous prompt joined them.
    /// </para>
    /// </summary>
    [Fact]
    public void TheArmedReviewIsSpokenOnTheOrderChannel()
    {
        using var h = LiveHarness();
        var captured = new List<FeedbackRequestEvent>();
        using var sub = h.EventBus.Subscribe<FeedbackRequestEvent>(captured.Add);

        var cut = OpenLiveTicket(h);
        cut.Find("button.submit-btn").Click();
        cut.WaitForAssertion(() => Assert.Contains("order-confirm-live", cut.Markup, StringComparison.Ordinal),
                             TimeSpan.FromSeconds(10));

        var review = captured.LastOrDefault(e => e.Message != null && e.Message.StartsWith("Confirm:", StringComparison.Ordinal));
        Assert.NotNull(review);
        Assert.Equal(SpeechChannel.OrderEvent, review!.Channel);
    }

    /// <summary>
    /// The backstop, exercised where NO control hook can reach.
    ///
    /// <para>
    /// Symbol and Provider go to the venue with every order (<c>BuildSignal</c> reads
    /// <c>Store.State.Identity</c>) and neither is a ticket field, so no <c>@oninput</c> on
    /// this dialog can observe them changing. Arm a review on BTC/USD, let the chart move to
    /// ETH/USD underneath, press Confirm: the review named BTC and the order would have gone
    /// out on ETH.
    /// </para>
    ///
    /// <para>
    /// This is the test that fails if the whole-signal comparison in <c>SubmitOrder</c> is
    /// removed — every other case in this file is also covered by a per-control void, so
    /// without this one the backstop would be an unguarded fix.
    /// </para>
    /// </summary>
    [Fact]
    public void AChartSymbolChangeUnderTheDialogCannotSendTheReviewedOrder()
    {
        using var h = LiveHarness();
        var cut = OpenLiveTicket(h);

        cut.Find("button.submit-btn").Click();
        cut.WaitForAssertion(() => Assert.Contains("order-confirm-live", cut.Markup, StringComparison.Ordinal),
                             TimeSpan.FromSeconds(10));

        var moved = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "kraken", "ETH/USD", "1h"),
        };
        h.WorkspaceStore.State.Returns(_ => moved);

        cut.FindAll("button.submit-btn").First().Click();
        Thread.Sleep(300);

        Assert.DoesNotContain(h.OrderService.ReceivedCalls(),
            c => c.GetMethodInfo().Name == nameof(h.OrderService.PlaceOrderAsync));
    }
}
