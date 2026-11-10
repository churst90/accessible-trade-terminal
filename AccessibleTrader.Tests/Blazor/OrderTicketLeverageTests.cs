// The leverage field on the order ticket, and whether the last thing heard before a margin
// order says that it is one.

using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class OrderTicketLeverageTests
{
    /// <summary>A live Kraken ticket on a pair that lends at 2 or 3 times only — KAS/USD's
    /// real limits (2026-11-10) — while the venue as a whole goes to 10.</summary>
    private static BlazorTestHarness LiveKasLikeHarness()
    {
        var h = new BlazorTestHarness();
        ModalCatalog.SeedChartState(h);
        h.Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();

        var liveKey = new ApiKeyConfig("kraken", "main", "key", "secret", Environment: "Live", IsActive: true);
        h.ApiKeyService.GetAllKeysAsync().Returns(new List<ApiKeyConfig> { liveKey });
        h.ApiKeyService.GetKeysForProviderAsync(default!).ReturnsForAnyArgs(new List<ApiKeyConfig> { liveKey });

        h.OrderService.SupportsTradingAsync(default!).ReturnsForAnyArgs(true);
        h.OrderService.GetCapabilitiesAsync(default!).ReturnsForAnyArgs(ProviderCapabilities.Leverage);
        h.OrderService.SupportsOcoPairsAsync(default!).ReturnsForAnyArgs(false);
        h.OrderService.GetMaxLeverageAsync(default!).ReturnsForAnyArgs(10.0);
        h.OrderService.GetAllowedLeverageAsync(default!, default!)
            .ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<int>?>(new List<int> { 2, 3 }));
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
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#leverage-input")), TimeSpan.FromSeconds(10));
        return cut;
    }

    [Fact]
    public void The_field_is_limited_to_the_pairs_own_amounts_and_says_them()
    {
        using var h = LiveKasLikeHarness();
        var cut = Open(h);

        var input = cut.Find("#leverage-input");
        // The venue-wide 10 is not an offer on a pair that lends at 3 at most.
        Assert.Equal("3", input.GetAttribute("max"));
        // The hint is read with the field, not only shown beside it.
        Assert.Equal("leverage-hint", input.GetAttribute("aria-describedby"));
        Assert.Contains("2 or 3", cut.Find("#leverage-hint").TextContent);
    }

    [Fact]
    public void The_spoken_review_says_the_leverage()
    {
        using var h = LiveKasLikeHarness();
        var cut = Open(h);

        cut.Find("#leverage-input").Change("3");
        cut.Find("button.submit-btn").Click();

        cut.WaitForAssertion(() => Assert.Contains("3 times leverage", cut.Markup, StringComparison.Ordinal),
                             TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(new[] { 2, 3 }, "2 or 3")]
    [InlineData(new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 }, "2 to 10")]
    [InlineData(new[] { 2, 3, 5 }, "2, 3 or 5")]
    public void Leverage_amounts_are_said_the_way_a_person_would(int[] values, string said) =>
        Assert.Equal(said, TradingDashboardModal.SayLeverage(values));
}
