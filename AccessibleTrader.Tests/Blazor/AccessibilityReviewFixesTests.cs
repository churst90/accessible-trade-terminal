// The five fixes from the accessibility review of 2026-11-26 (Cody: "yes please do 1-5"):
//   1. Hyperliquid's vault field was masked like a password, though it holds a public address.
//   2. The dead-man's switch seconds field corrected 5 to 30 (and ignored letters) in silence.
//   3. Both dead-man's switch fields pointed at one sixty-word hint, read twice in a row.
//   4. TWAP minutes took 0 and the review said "TWAP over 0 minutes"; only the venue refused it.
//   5. A TWAP that had not filled yet showed a price of "0" in open orders.
// Each is asserted as what a screen reader gets: the field's type, its invalid state, what it
// is described by, what is said, and the cell text.

using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class AccessibilityReviewFixesTests
{
    // ── 2 and 3: the dead-man's switch seconds field ────────────────────────────────────────

    private static (BlazorTestHarness h, IRenderedComponent<SettingsModal> cut, List<string> said) OpenSettings()
    {
        var h = new BlazorTestHarness();
        var said = new List<string>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) said.Add(e.Message); });
        var cut = h.OpenModal<SettingsModal>(bus => bus.Publish(new OpenSettingsEvent()));
        cut.WaitForAssertion(() => cut.Find("#s-dead-mans-seconds"));
        return (h, cut, said);
    }

    [Fact]
    public void Seconds_below_the_floor_are_corrected_said_and_the_message_linked()
    {
        var (h, cut, said) = OpenSettings();
        using var _ = h;

        cut.Find("#s-dead-mans-seconds").Change("5");

        cut.WaitForAssertion(() =>
        {
            var field = cut.Find("#s-dead-mans-seconds");
            Assert.Equal("30", field.GetAttribute("value"));
            // Not aria-invalid: 30 is a good value, and "invalid" on it would mislead.
            Assert.Null(field.GetAttribute("aria-invalid"));
            Assert.Contains("s-dead-mans-seconds-err", field.GetAttribute("aria-describedby"));
            Assert.Contains("must be 30 to 3600. Set to 30.", cut.Find("#s-dead-mans-seconds-err").TextContent);
        });
        Assert.Contains("Dead-man's switch seconds must be 30 to 3600. Set to 30.", said);
    }

    [Fact]
    public void An_emptied_field_keeps_the_wait_and_says_so_and_a_good_entry_clears_the_error()
    {
        var (h, cut, said) = OpenSettings();
        using var _ = h;

        cut.Find("#s-dead-mans-seconds").Change("");
        cut.WaitForAssertion(() =>
            Assert.Equal("60", cut.Find("#s-dead-mans-seconds").GetAttribute("value")));
        Assert.Contains(said, m => m.Contains("needs a number from 30 to 3600. Kept at 60."));

        cut.Find("#s-dead-mans-seconds").Change("120");
        cut.WaitForAssertion(() =>
        {
            var field = cut.Find("#s-dead-mans-seconds");
            Assert.Equal("120", field.GetAttribute("value"));
            Assert.Equal("s-dead-mans-seconds-hint", field.GetAttribute("aria-describedby"));
            Assert.Empty(cut.FindAll("#s-dead-mans-seconds-err"));
        });
    }

    [Fact]
    public void Each_dead_mans_field_has_its_own_hint_and_the_long_one_leads_with_the_risk()
    {
        var (h, cut, _) = OpenSettings();
        using var __ = h;

        Assert.Equal("s-dead-mans-switch-hint", cut.Find("#s-dead-mans-switch").GetAttribute("aria-describedby"));
        Assert.Equal("s-dead-mans-seconds-hint", cut.Find("#s-dead-mans-seconds").GetAttribute("aria-describedby"));

        var hint = cut.Find("#s-dead-mans-switch-hint").TextContent;
        // The consequence comes before the venue note, and names stop-losses outright.
        Assert.True(hint.IndexOf("cancels all your open orders", StringComparison.Ordinal)
                    < hint.IndexOf("Hyperliquid only", StringComparison.Ordinal));
        Assert.Contains("stop-losses", hint);
        Assert.Contains("Closing the terminal normally keeps your orders", hint);
    }

    [Theory]
    [InlineData("5", 60, 30, true)]
    [InlineData("99999", 60, 3600, true)]
    [InlineData("abc", 90, 90, true)]
    [InlineData(" 45 ", 60, 45, false)]
    [InlineData("3600", 60, 3600, false)]
    public void The_entry_rule(string entry, int current, int expected, bool said)
    {
        var (value, message) = SettingsModal.DeadMansSecondsEntry(entry, current);
        Assert.Equal(expected, value);
        Assert.Equal(said, message != null);
    }

    // ── 1: Hyperliquid's vault or subaccount field ──────────────────────────────────────────

    private const string Main  = "0x1111111111111111111111111111111111111111";
    private const string Vault = "0x2222222222222222222222222222222222222222";

    private static (TestContext ctx, IApiKeyService svc, IRenderedComponent<ApiKeysModal> cut) OpenKeys()
    {
        var ctx = new TestContext();
        var svc = Substitute.For<IApiKeyService>();
        svc.GetAllKeysAsync().Returns(_ => Task.FromResult(new List<ApiKeyConfig>()));
        IEventBus bus = new EventBus();
        ctx.Services.AddSingleton(svc);
        ctx.Services.AddSingleton(Substitute.For<IDataService>());
        ctx.Services.AddSingleton(bus);
        ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();
        var cut = ctx.RenderComponent<ApiKeysModal>();
        cut.InvokeAsync(() => bus.Publish(new OpenApiKeysEvent())).GetAwaiter().GetResult();
        cut.WaitForElement("h2#apikeys-title");
        return (ctx, svc, cut);
    }

    [Fact]
    public void The_vault_field_is_plain_text_with_a_hint_and_a_passphrase_stays_masked()
    {
        var (ctx, _, cut) = OpenKeys();
        using var __ = ctx;

        cut.Find("#key-provider").Change("Hyperliquid");
        var field = cut.Find("#key-pass");
        Assert.Equal("text", field.GetAttribute("type"));
        Assert.Equal("key-pass-hint", field.GetAttribute("aria-describedby"));
        Assert.Contains("Leave blank to trade your own account", cut.Find("#key-pass-hint").TextContent);

        cut.Find("#key-provider").Change("Kraken");
        Assert.Equal("password", cut.Find("#key-pass").GetAttribute("type"));
        Assert.Empty(cut.FindAll("#key-pass-hint"));
    }

    [Fact]
    public void Changing_the_provider_empties_the_field_because_it_changes_meaning()
    {
        var (ctx, _, cut) = OpenKeys();
        using var __ = ctx;

        cut.Find("#key-provider").Change("Kraken");
        cut.Find("#key-pass").Change("my kraken passphrase");
        cut.Find("#key-provider").Change("Hyperliquid");

        Assert.Equal("", cut.Find("#key-pass").GetAttribute("value") ?? "");
    }

    [Fact]
    public void A_vault_that_is_not_an_address_is_refused_at_save_and_the_field_marked()
    {
        var (ctx, svc, cut) = OpenKeys();
        using var __ = ctx;

        cut.Find("#key-provider").Change("Hyperliquid");
        cut.Find("#key-nickname").Change("hl");
        cut.Find("#key-apikey").Change(Main);
        cut.Find("#key-pass").Change("0x123");
        cut.Find("button[aria-label='Save Profile as a new API key profile']").Click();

        cut.WaitForAssertion(() =>
        {
            var field = cut.Find("#key-pass");
            Assert.Equal("true", field.GetAttribute("aria-invalid"));
            Assert.Contains("apikeys-form-error", field.GetAttribute("aria-describedby"));
            Assert.Contains("must be an address", cut.Find("#apikeys-form-error").TextContent);
        });
        svc.DidNotReceiveWithAnyArgs().SaveKeyAsync(default!);

        // Editing the field takes the mark and its message off until the next Save.
        cut.Find("#key-pass").Change(Vault);
        Assert.Equal("false", cut.Find("#key-pass").GetAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll("#apikeys-form-error"));
    }

    [Fact]
    public void Switching_provider_after_a_vault_error_takes_the_error_away()
    {
        var (ctx, _, cut) = OpenKeys();
        using var __ = ctx;

        cut.Find("#key-provider").Change("Hyperliquid");
        cut.Find("#key-nickname").Change("hl");
        cut.Find("#key-apikey").Change(Main);
        cut.Find("#key-pass").Change("0x123");
        cut.Find("button[aria-label='Save Profile as a new API key profile']").Click();
        cut.WaitForAssertion(() => cut.Find("#apikeys-form-error"));

        cut.Find("#key-provider").Change("Kraken");
        Assert.Empty(cut.FindAll("#apikeys-form-error"));
    }

    [Fact]
    public void The_vault_rule()
    {
        Assert.Null(ApiKeysModal.VaultError("Hyperliquid", "", Main));
        Assert.Null(ApiKeysModal.VaultError("Hyperliquid", Vault, Main));
        Assert.NotNull(ApiKeysModal.VaultError("Hyperliquid", "0xnothex0000000000000000000000000000000000", Main));
        Assert.NotNull(ApiKeysModal.VaultError("Hyperliquid", Main.ToUpperInvariant().Replace("0X", "0x"), Main));
        Assert.Null(ApiKeysModal.VaultError("Kraken", "anything at all", Main));   // a real passphrase
    }

    // ── 4 and 5: the TWAP ticket and the open-orders price ─────────────────────────────────

    private static BlazorTestHarness TwapHarness(params OpenOrder[] open)
    {
        var h = new BlazorTestHarness();
        ModalCatalog.SeedChartState(h);
        h.Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();

        var liveKey = new ApiKeyConfig("kraken", "main", "key", "secret", Environment: "Live", IsActive: true);
        h.ApiKeyService.GetAllKeysAsync().Returns(new List<ApiKeyConfig> { liveKey });
        h.ApiKeyService.GetKeysForProviderAsync(default!).ReturnsForAnyArgs(new List<ApiKeyConfig> { liveKey });
        h.OrderService.SupportsTradingAsync(default!).ReturnsForAnyArgs(true);
        h.OrderService.GetCapabilitiesAsync(default!).ReturnsForAnyArgs(ProviderCapabilities.Twap);
        h.OrderService.SupportsOcoPairsAsync(default!).ReturnsForAnyArgs(false);
        h.OrderService.GetMaxLeverageAsync(default!).ReturnsForAnyArgs(1.0);
        h.OrderService.GetBalancesAsync(default!)
            .ReturnsForAnyArgs(ProviderResult<List<Balance>>.Ok(new List<Balance>()));
        h.OrderService.GetPositionsAsync(default!)
            .ReturnsForAnyArgs(ProviderResult<List<Position>>.Ok(new List<Position>()));
        h.OrderService.GetOpenOrdersAsync(default!, default)
            .ReturnsForAnyArgs(ProviderResult<List<OpenOrder>>.Ok(open.ToList()));
        h.OrderService.GetFillsAsync(default!, default, default)
            .ReturnsForAnyArgs(ProviderResult<List<TradeFill>>.Ok(new List<TradeFill>()));
        return h;
    }

    private static IRenderedComponent<TradingDashboardModal> OpenTicket(BlazorTestHarness h)
    {
        var cut = h.OpenModal<TradingDashboardModal>(b => b.Publish(new OpenTradingDashboardEvent()));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#order-qty")), TimeSpan.FromSeconds(10));
        return cut;
    }

    [Fact]
    public void Zero_TWAP_minutes_is_marked_before_Place_and_nothing_is_armed_or_sent()
    {
        using var h = TwapHarness();
        var cut = OpenTicket(h);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#order-type option[value=TWAP]")),
                             TimeSpan.FromSeconds(10));
        cut.Find("#order-type").Change("TWAP");
        cut.Find("#twap-minutes").Change("0");

        var field = cut.Find("#twap-minutes");
        Assert.Equal("true", field.GetAttribute("aria-invalid"));
        Assert.StartsWith("twap-minutes-err", field.GetAttribute("aria-describedby"));
        Assert.Contains("1 or more", cut.Find("#twap-minutes-err").TextContent);

        cut.Find("button.submit-btn").Click();
        Thread.Sleep(200);
        Assert.DoesNotContain("TWAP over 0 minutes", cut.Markup);
        Assert.DoesNotContain(h.OrderService.ReceivedCalls(),
            c => c.GetMethodInfo().Name == nameof(h.OrderService.PlaceOrderAsync));

        // A good number clears it.
        cut.Find("#twap-minutes").Change("15");
        Assert.Equal("false", cut.Find("#twap-minutes").GetAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll("#twap-minutes-err"));
    }

    [Fact]
    public void An_unfilled_TWAP_says_none_filled_yet_in_the_price_cell_not_zero()
    {
        using var h = TwapHarness(
            new OpenOrder("twap:7", "BTC", OrderSide.Buy, OrderType.Twap, 0.5, 0, "TWAP 0% done"),
            new OpenOrder("twap:8", "BTC", OrderSide.Buy, OrderType.Twap, 0.3, 64000, "TWAP 40% done"));
        var cut = OpenTicket(h);

        cut.Find("#acct-tab-orders").Click();
        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tr").Where(r => r.TextContent.Contains("TWAP ")).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal("none filled yet", rows[0].QuerySelectorAll("td")[5].TextContent);
            Assert.StartsWith("average ", rows[1].QuerySelectorAll("td")[5].TextContent);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void The_price_rule()
    {
        var limit = new OpenOrder("1", "BTC", OrderSide.Buy, OrderType.Limit, 1, 0, "open");
        Assert.Equal(PriceFormatterZero(), TradingDashboardModal.OpenOrderPriceText(limit));   // other types unchanged
        var twap = limit with { Type = OrderType.Twap };
        Assert.Equal("none filled yet", TradingDashboardModal.OpenOrderPriceText(twap));
    }

    private static string PriceFormatterZero() =>
        AccessibleTrader.BlazorClient.Services.PriceFormatter.FormatPrice(0);
}
