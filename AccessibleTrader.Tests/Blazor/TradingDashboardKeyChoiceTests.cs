// The trading dashboard's account identity: which key signs, and whether the user is TOLD.
//
// Three defects, all in one control and one gate (docs/ORDER_ROUTING_SAFETY_SCOPE.md):
//
//   1. "Switch API Key" flipped IsActive and announced a switch. The credential that signed
//      came from a lookup blind to that flag, and the HOST stayed whatever the startup loop had
//      configured first. A control that names an action is a claim.
//   2. The live review was armed only when the key said exactly "Live", so a key labelled Paper
//      on one of the six venues with no practice environment — and every legacy profile, whose
//      environment is an empty string — went straight out unread. The dangerous case was the
//      quiet one.
//   3. Live and paper were SHOWN and never SPOKEN. For a screen-reader user nothing on this
//      screen said "real money" until the review text, and the review could be skipped.

using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Enums;
using Bunit;
using Microsoft.AspNetCore.Components;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class TradingDashboardKeyChoiceTests
{
    private static ApiKeyConfig Key(string nickname, string environment) =>
        new("kraken", nickname, "key-" + nickname, "secret", Environment: environment,
            IsActive: nickname == "live-main");

    /// <summary>A dashboard on a trading-capable chart with the given stored profiles.</summary>
    private static BlazorTestHarness Harness(params ApiKeyConfig[] keys)
    {
        var h = new BlazorTestHarness();
        ModalCatalog.SeedChartState(h);
        h.Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();

        h.ApiKeyService.GetAllKeysAsync().Returns(keys.ToList());
        h.ApiKeyService.GetKeysForProviderAsync(default!).ReturnsForAnyArgs(keys.ToList());
        // The dashboard's switcher is the choice of record: it must reach DataService, and what
        // comes back is what signs. Answer with the profile that was asked for.
        h.DataService.ReconfigureProviderAsync(default!, default!)
            .ReturnsForAnyArgs(ci => Task.FromResult<ApiKeyConfig?>(
                keys.FirstOrDefault(k => k.Nickname == ci.ArgAt<string>(1))));

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

    private static IRenderedComponent<TradingDashboardModal> Open(BlazorTestHarness h)
    {
        var cut = h.OpenModal<TradingDashboardModal>(b => b.Publish(new OpenTradingDashboardEvent()));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#order-qty")), TimeSpan.FromSeconds(10));
        return cut;
    }

    private static string ModeCell(IRenderedComponent<TradingDashboardModal> cut) =>
        cut.FindAll("div.stat").First(d => d.QuerySelector("span")?.TextContent == "Mode")
           .QuerySelector("strong")!.TextContent;

    // ── The environment is SPOKEN, not just shown ─────────────────────────────

    [Fact]
    public void Opening_a_live_account_speaks_the_consequence_once_on_the_order_channel()
    {
        using var h = Harness(Key("live-main", "Live"));
        var spoken = new List<FeedbackRequestEvent>();
        using var sub = h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var cut = Open(h);

        cut.WaitForAssertion(() => Assert.Contains(spoken,
            e => e.Message != null && e.Message.Contains("Orders here are real money", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10));

        // ONCE. A sentence said twice on two channels is how Orca came to drop both copies
        // (see StatusBar's own comment) — and the banners stay visual for the same reason.
        var said = spoken.Where(e => e.Message != null
                                  && e.Message.Contains("Orders here are real money", StringComparison.Ordinal)).ToList();
        Assert.Single(said);
        Assert.Contains("live-main", said[0].Message!);
        Assert.Equal(SpeechChannel.OrderEvent, said[0].Channel);
    }

    [Fact]
    public void Opening_a_paper_account_says_so_rather_than_saying_nothing()
    {
        using var h = Harness(Key("sandbox", "Paper"));
        var spoken = new List<FeedbackRequestEvent>();
        using var sub = h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var cut = Open(h);

        cut.WaitForAssertion(() => Assert.Contains(spoken,
            e => e.Message != null && e.Message.Contains("paper account 'sandbox'", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(spoken, e => e.Message != null
                                        && e.Message.Contains("LIVE account", StringComparison.Ordinal));
    }

    // ── The switcher actually switches ────────────────────────────────────────

    [Fact]
    public async Task Choosing_a_key_reconfigures_the_provider_and_speaks_the_consequence()
    {
        using var h = Harness(Key("live-main", "Live"), Key("sandbox", "Paper"));
        var spoken = new List<FeedbackRequestEvent>();
        var cut = Open(h);
        using var sub = h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        // ChangeAsync, never the sync Change(): an async handler is not awaited by Change().
        await cut.Find("#key-select").ChangeAsync(new ChangeEventArgs { Value = "sandbox" });

        // THE defect: this call did not exist, so the key that signed never moved.
        await h.DataService.Received(1).ReconfigureProviderAsync(Arg.Any<string>(), "sandbox");
        Assert.Contains(spoken, e => e.Message != null
                                  && e.Message.Contains("paper account 'sandbox'", StringComparison.Ordinal));
        Assert.Equal("Paper — sandbox", ModeCell(cut));
    }

    [Fact]
    public async Task A_switch_that_did_not_take_is_reported_as_an_error_not_as_a_switch()
    {
        // The provider is still signing with the key it had. Announcing the switch anyway is
        // how the next order goes out on the account the user believes they left.
        using var h = Harness(Key("live-main", "Live"), Key("sandbox", "Paper"));
        h.DataService.ReconfigureProviderAsync(default!, default!)
            .ReturnsForAnyArgs(Task.FromResult<ApiKeyConfig?>(null));
        var spoken = new List<FeedbackRequestEvent>();
        var cut = Open(h);
        using var sub = h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        await cut.Find("#key-select").ChangeAsync(new ChangeEventArgs { Value = "sandbox" });

        var report = Assert.Single(spoken, e => e.Type == FeedbackType.Error);
        Assert.Contains("Could not switch", report.Message!);
        Assert.Contains("still signing with the key it had", report.Message!);
    }

    [Fact]
    public void Every_option_says_what_the_key_costs_not_just_what_it_is_called()
    {
        using var h = Harness(Key("live-main", "Live"), Key("sandbox", "Paper"));
        var cut = Open(h);

        var options = cut.FindAll("#key-select option").Select(o => o.TextContent).ToList();
        Assert.Contains("live-main — Live, real money", options);
        Assert.Contains("sandbox — Paper, practice environment", options);
        // The label names what the control DOES.
        Assert.Equal("API key for this order:",
            cut.Find("label[for=key-select]").TextContent.Trim());
    }

    // ── The fail-safe review gate ─────────────────────────────────────────────

    [Fact]
    public void A_legacy_profile_with_no_environment_is_reviewed_rather_than_sent()
    {
        // The gate was `== "Live"`. A profile stored before the Environment field existed carries
        // an empty string, so it was neither Live nor reviewed: Confirm sent it straight out.
        using var h = Harness(Key("legacy", ""));
        var cut = Open(h);

        Assert.Equal("Live — legacy", ModeCell(cut));

        cut.Find("button.submit-btn").Click();
        cut.WaitForAssertion(() => Assert.Contains("order-confirm-live", cut.Markup, StringComparison.Ordinal),
                             TimeSpan.FromSeconds(10));
        Thread.Sleep(200);
        Assert.DoesNotContain(h.OrderService.ReceivedCalls(),
            c => c.GetMethodInfo().Name == nameof(h.OrderService.PlaceOrderAsync));
    }

    [Fact]
    public void A_paper_key_is_still_sent_without_a_review()
    {
        // The control: if EVERYTHING were reviewed the test above would pass for the wrong
        // reason, and a paper rehearsal would have gained a confirmation step it does not need.
        using var h = Harness(Key("sandbox", "Paper"));
        var cut = Open(h);

        Assert.Equal("Paper — sandbox", ModeCell(cut));
        Assert.DoesNotContain("order-confirm-live", cut.Markup, StringComparison.Ordinal);
    }
}
