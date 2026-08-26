// TradingDashboardModal — what opening it costs.
//
// The focus contract for this dialog is covered by ModalAccessibilityContractTests via
// ModalCatalog, and it passes: the modal does ask for focus on "trade-title". It passed
// while Alt+T was, in a real browser, opening a dialog that never took focus — because
// bUnit applies a render synchronously and a browser does not, so the render race that
// actually bit could not exist here. That fix lives in keyboard.js (focusElement retries
// across animation frames instead of no-opping on the first miss) and is not assertable
// from this layer.
//
// What IS assertable from here is the thing that made the race easy to lose: the refresh
// timer was armed with a due time of ZERO, so every account read ShowAsync had just
// awaited ran again milliseconds later — eight reads on open instead of four, and a
// second full re-render of the largest dialog in the app while the open was still
// settling. That is the regression this file guards.

// Razor components live in this namespace. An "unused using" sweep run before
// BlazorClient.Components has generated its component types will not see them and
// will offer to delete this line; it is used. See the same note in WebHost/Program.cs.
using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class TradingDashboardModalTests
{
    /// <summary>
    /// Puts the harness into the state ShowAsync needs to get all the way through its
    /// trading-supported branch — without this the modal returns early at
    /// <c>if (!supported)</c> and never loads an account at all.
    /// </summary>
    private static BlazorTestHarness TradableHarness()
    {
        var h = new BlazorTestHarness();
        ModalCatalog.SeedChartState(h);

        // Loose mode, deliberately. The harness's default focusElement shim RECORDS the
        // invocation but never completes it, and ShowAsync awaits that call — so under the
        // strict default the modal parks forever on the focus line and nothing after it
        // ever runs. Fine for tests that only assert where focus was sent; useless for a
        // test about what happens next.
        h.Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();

        // An enumerable trading account. The dashboard no longer derives the account
        // from the focused chart, so with no keys and paper mode off there is nothing
        // to read and it correctly reads nothing — which would make every assertion
        // below vacuously true rather than red.
        h.ApiKeyService.GetAllKeysAsync().Returns(new List<ApiKeyConfig>
        {
            new("kraken", "main", "key", "secret", Environment: "Live", IsActive: true),
        });

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

    private static int BalanceReads(BlazorTestHarness h) =>
        h.OrderService.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(h.OrderService.GetBalancesAsync));

    [Fact]
    public void Opening_LoadsTheAccountExactlyOnce()
    {
        using var h = TradableHarness();

        _ = h.OpenModal<TradingDashboardModal>(b => b.Publish(new OpenTradingDashboardEvent()));

        // ShowAsync runs fire-and-forget off the open event, so wait for the first read
        // rather than assuming it has landed. Generous, because a starved runner is slow,
        // not wrong.
        Assert.True(
            SpinWait.SpinUntil(() => BalanceReads(h) >= 1, TimeSpan.FromSeconds(10)),
            "The dashboard never read the account at all. Order-service calls made: "
            + string.Join(", ", h.OrderService.ReceivedCalls().Select(c => c.GetMethodInfo().Name)));

        // Then hold still well inside the 2 s refresh period. A duplicate from a zero due
        // time arrived within milliseconds, so this window catches it with room to spare,
        // while a correctly armed timer cannot have ticked yet. Verified red by restoring
        // the `0` due time: two reads, immediately.
        Thread.Sleep(500);

        Assert.Equal(1, BalanceReads(h));
    }
}
