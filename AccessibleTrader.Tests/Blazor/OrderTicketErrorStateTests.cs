// The order ticket, when it will not send.
//
// Demonstrated on 2026-09-02, before any of this existed: open the trading dashboard,
// type 0 into Quantity, and the rendered DOM comes back with
//
//     [Submit Buy Order] disabled=True  aria-disabled=-
//     #order-qty         aria-invalid=-
//
// The button is not greyed out for a blind trader — it is GONE, out of the tab order and
// out of the screen reader's button list, and the field that removed it still announces
// itself as valid. There is no visual channel to fall back on, so the user's whole
// account of what happened is silence. This is the money screen.
//
// What is asserted here is the full contract, not just the attribute: still refused,
// still reachable, still says why, and the reason names the cause that actually applies.
// Asserting aria-disabled alone would pass on a button that had quietly stopped refusing.

using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public sealed class OrderTicketErrorStateTests
{
    /// <summary>
    /// The state ShowAsync needs to get through its trading-supported branch. Without an
    /// enumerable account the dashboard correctly reads nothing and every assertion below
    /// is vacuously true rather than red. Same shape as TradingDashboardModalTests.
    /// </summary>
    private static BlazorTestHarness TradableHarness(bool oco = false)
    {
        var h = new BlazorTestHarness();
        ModalCatalog.SeedChartState(h);
        h.Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();
        h.ApiKeyService.GetAllKeysAsync().Returns(new List<ApiKeyConfig>
        {
            new("kraken", "main", "key", "secret", Environment: "Live", IsActive: true),
        });
        h.OrderService.SupportsTradingAsync(default!).ReturnsForAnyArgs(true);
        h.OrderService.GetCapabilitiesAsync(default!).ReturnsForAnyArgs(ProviderCapabilities.None);
        h.OrderService.SupportsOcoPairsAsync(default!).ReturnsForAnyArgs(oco);
        h.OrderService.GetMaxLeverageAsync(default!).ReturnsForAnyArgs(1.0);
        h.OrderService.GetBalancesAsync(default!).ReturnsForAnyArgs(ProviderResult<List<Balance>>.Ok(new List<Balance>()));
        h.OrderService.GetPositionsAsync(default!).ReturnsForAnyArgs(ProviderResult<List<Position>>.Ok(new List<Position>()));
        h.OrderService.GetOpenOrdersAsync(default!, default).ReturnsForAnyArgs(ProviderResult<List<OpenOrder>>.Ok(new List<OpenOrder>()));
        h.OrderService.GetFillsAsync(default!, default, default).ReturnsForAnyArgs(ProviderResult<List<TradeFill>>.Ok(new List<TradeFill>()));
        return h;
    }

    private static IRenderedFragment OpenTicket(BlazorTestHarness h)
    {
        var cut = h.OpenModal<TradingDashboardModal>(b => b.Publish(new OpenTradingDashboardEvent()));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#order-qty")), TimeSpan.FromSeconds(10));
        return cut;
    }

    private static AngleSharp.Dom.IElement Submit(IRenderedFragment cut) =>
        cut.FindAll("button").First(b => b.TextContent.Contains("Submit", StringComparison.Ordinal));

    // ── Quantity ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_quantity_of_zero_leaves_Submit_reachable_and_says_which_field_did_it()
    {
        using var h = TradableHarness();
        var cut = OpenTicket(h);

        cut.Find("#order-qty").Change("0");

        GatedButtonAssert.IsRefusedBecause(cut, Submit(cut), "quantity above zero");

        var qty = cut.Find("#order-qty");
        Assert.Equal("true", qty.GetAttribute("aria-required"));
        Assert.Equal("true", qty.GetAttribute("aria-invalid"));
        // …and the message is wired to the field, so arriving there reads it.
        var describedBy = qty.GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrWhiteSpace(describedBy));
        Assert.Contains("above zero", cut.Find("#" + describedBy).TextContent);
    }

    [Fact]
    public void A_usable_quantity_clears_every_trace_of_the_refusal()
    {
        // The control for the test above. A field that stayed aria-invalid after being
        // fixed, or a button that kept its reason, would send the user back to a problem
        // that is no longer there.
        using var h = TradableHarness();
        var cut = OpenTicket(h);

        cut.Find("#order-qty").Change("0");
        Assert.Equal("true", cut.Find("#order-qty").GetAttribute("aria-invalid"));

        cut.Find("#order-qty").Change("1.5");

        GatedButtonAssert.IsAvailable(cut, Submit(cut));
        var qty = cut.Find("#order-qty");
        Assert.Equal("false", qty.GetAttribute("aria-invalid"));
        Assert.Null(qty.GetAttribute("aria-describedby"));
    }

    [Fact]
    public async Task Pressing_the_refused_Submit_sends_nothing_and_says_why_out_loud()
    {
        // aria-disabled stops nothing in the DOM, so the refusal has to be real. And it
        // has to be audible past F2: FeedbackType.Boundary speaks on the Manual channel,
        // which the speech mute silences, and a muted terminal answering the money button
        // with an earcon and no words is the same defect as the live-order readback F2
        // silenced until 2026-09-01.
        using var h = TradableHarness();
        var cut = OpenTicket(h);

        var spoken = new List<FeedbackRequestEvent>();
        using var sub = h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        cut.Find("#order-qty").Change("0");
        Submit(cut).Click();

        await h.OrderService.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!, default!);

        var refusal = Assert.Single(spoken.Where(e => e.Type == FeedbackType.Boundary));
        Assert.Contains("quantity above zero", refusal.Message);
        Assert.Equal(SpeechChannel.Critical, refusal.Channel);
    }

    // ── The OCO pair ─────────────────────────────────────────────────────────

    [Fact]
    public void The_rule_nobody_guesses_is_now_stated()
    {
        // A limit and a stop at the SAME price cannot be a pair — neither can be the one
        // that fills first and cancels the other. Under `disabled` that rule was
        // unstateable: the button was simply not in the dialog, and the user was left to
        // work out which of four fields was at fault.
        using var h = TradableHarness(oco: true);
        var cut = OpenTicket(h);

        cut.Find("#oco-qty").Change("1");
        cut.Find("#oco-limit").Change("100");
        cut.Find("#oco-stop").Change("100");

        var place = cut.FindAll("button").First(b => b.TextContent.Contains("Place OCO pair"));
        GatedButtonAssert.IsRefusedBecause(cut, place, "must differ");

        // Both prices are marked, because either is the one to change, and both point at
        // one message: the defect is the PAIR, not a field.
        var limit = cut.Find("#oco-limit");
        var stop  = cut.Find("#oco-stop");
        Assert.Equal("true", limit.GetAttribute("aria-invalid"));
        Assert.Equal("true", stop.GetAttribute("aria-invalid"));
        Assert.Equal(limit.GetAttribute("aria-describedby"), stop.GetAttribute("aria-describedby"));
        Assert.Contains("must differ",
            cut.Find("#" + limit.GetAttribute("aria-describedby")).TextContent);

        // The quantity is fine, and does not say otherwise — a form that marks everything
        // wrong tells the user nothing.
        Assert.NotEqual("true", cut.Find("#oco-qty").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void A_pristine_OCO_panel_marks_nothing_invalid()
    {
        // All three OCO fields default to 0, so binding aria-invalid to "is it still zero"
        // greeted the user with "Quantity, invalid entry. Limit price, invalid entry. Stop
        // trigger, invalid entry." on a form they had not touched — and the pair rule was
        // true as well, since 0 equals 0. aria-invalid means REJECTED, not blank; being
        // blank is what aria-required is for. Found by the screen-reader review of this
        // change, before it shipped.
        using var h = TradableHarness(oco: true);
        var cut = OpenTicket(h);

        foreach (var id in new[] { "#oco-qty", "#oco-limit", "#oco-stop" })
        {
            var f = cut.Find(id);
            Assert.Equal("true", f.GetAttribute("aria-required"));
            Assert.NotEqual("true", f.GetAttribute("aria-invalid"));
        }
        Assert.Empty(cut.FindAll("#oco-pair-err"));

        // The button is still refused — the fields are empty — and says which one to fill.
        var place = cut.FindAll("button").First(b => b.TextContent.Contains("Place OCO pair"));
        GatedButtonAssert.IsRefusedBecause(cut, place, "OCO quantity above zero");
    }

    [Fact]
    public void An_empty_OCO_field_is_named_rather_than_the_pricing_rule()
    {
        // The gate has five causes and must report the one that applies; "the limit and
        // the stop must differ" is a lie when the real problem is a blank quantity, and
        // it sends the user to edit the wrong field.
        using var h = TradableHarness(oco: true);
        var cut = OpenTicket(h);

        cut.Find("#oco-qty").Change("0");
        cut.Find("#oco-limit").Change("100");
        cut.Find("#oco-stop").Change("90");

        var place = cut.FindAll("button").First(b => b.TextContent.Contains("Place OCO pair"));
        GatedButtonAssert.IsRefusedBecause(cut, place, "OCO quantity above zero");
    }
}
