using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using NSubstitute;
using Cmp = AccessibleTrader.BlazorClient.Components;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>The toolbar's Renko entry, the Brick settings button, and the settings dialog.</summary>
public sealed class RenkoToolbarTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static WorkspaceState State(PriceChartStyle style = PriceChartStyle.Candles, int bars = 40)
    {
        var config = new SeriesConfig { Id = "candles", Name = "Candles" };
        return WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "kraken", "BTC/USD", "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, bars)
                .Select(i => new Ohlcv(Start.AddHours(i), 100 + i, 101 + i, 99 + i, 100 + i, 10))),
            ActiveSeries = ImmutableList.Create(new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" })),
            FocusedSeriesId = "candles",
            ChartStyle = style,
            CurrentDataShape = ProviderDataShape.Ohlcv,
            CurrentDataIndex = bars - 1,
            ViewportLength = 30,
            InitStatus = InitializationStatus.Ready,
        };
    }

    [Fact]
    public void The_select_offers_Renko()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State());

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();

        Assert.Contains(cut.FindAll("#chart-type-select option"), o => o.GetAttribute("value") == "renko");
    }

    [Fact]
    public void Choosing_Renko_dispatches_its_settings_then_the_style()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State());

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        cut.Find("#chart-type-select").Change("renko");

        h.WorkspaceStore.Received(1).Dispatch(Arg.Any<SetRenkoSettingsAction>());
        h.WorkspaceStore.Received(1).Dispatch(
            Arg.Is<SetChartStyleAction>(a => a.Style == PriceChartStyle.Renko && !a.HeikinAshi));
    }

    [Fact]
    public void A_refused_Renko_puts_the_select_back_and_says_why()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(bars: 9));
        var said = new List<string?>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(e => said.Add(e.Message));

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        var shown = new List<string?>();
        cut.OnMarkupUpdated += (_, _) => shown.Add(cut.Find("#chart-type-select").GetAttribute("value"));

        cut.Find("#chart-type-select").Change("renko");

        cut.WaitForAssertion(() => Assert.Equal("candles", shown.LastOrDefault()));
        int picked = shown.IndexOf("renko");
        Assert.True(picked >= 0, "the select never rendered the refused entry");
        Assert.Contains("candles", shown.Skip(picked + 1));
        Assert.Contains("Renko needs at least 16 bars for ATR 14; this chart has 9.", said);
        h.WorkspaceStore.DidNotReceive().Dispatch(Arg.Any<SetChartStyleAction>());
    }

    [Fact]
    public void Brick_settings_is_shown_only_on_a_Renko_chart()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State());

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        Assert.Empty(cut.FindAll("#brick-settings-button"));

        h.WorkspaceStore.State.Returns(_ => State(PriceChartStyle.Renko));
        cut.Render();
        Assert.Single(cut.FindAll("#brick-settings-button"));
    }

    [Fact]
    public void The_dialog_prefills_the_tabs_settings_and_Apply_dispatches()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(PriceChartStyle.Renko) with
        {
            Renko = new RenkoSettings(RenkoBoxMode.Fixed, 500, 21),
            BrickBoxSize = 500,
            ChartStyle = PriceChartStyle.Renko,
        });

        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();
        var cut = h.Ctx.RenderComponent<Cmp.ChartTypeSettingsModal>();
        h.EventBus.Publish(new OpenChartTypeSettingsEvent());

        cut.WaitForAssertion(() => Assert.Contains("Renko box", cut.Markup));
        Assert.True(cut.Find("#renko-box-fixed").HasAttribute("checked"));
        Assert.Equal("500", cut.Find("#renko-box-size").GetAttribute("value"));

        cut.Find("#renko-box-size").Change("20");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply").Click();

        h.WorkspaceStore.Received(1).Dispatch(
            Arg.Is<SetRenkoSettingsAction>(a =>
                a.Settings.Mode == RenkoBoxMode.Fixed && a.Settings.BoxSize == 20 && a.Settings.AtrLength == 21));
        Assert.DoesNotContain("Renko box", cut.Markup);
    }

    [Fact]
    public void A_refusal_keeps_the_dialog_open_and_focuses_the_offending_input()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(PriceChartStyle.Renko));
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();

        var cut = h.Ctx.RenderComponent<Cmp.ChartTypeSettingsModal>();
        h.EventBus.Publish(new OpenChartTypeSettingsEvent());
        cut.WaitForAssertion(() => Assert.Contains("Renko box", cut.Markup));

        cut.Find("#renko-atr-length").Change("200");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply").Click();

        cut.WaitForAssertion(() => Assert.Contains("Renko needs at least 202 bars", cut.Markup));
        Assert.Contains(FocusTargets(h.Ctx), id => id == "renko-atr-length");
        h.WorkspaceStore.DidNotReceive().Dispatch(Arg.Any<SetRenkoSettingsAction>());
    }

    private static List<string> FocusTargets(TestContext ctx) =>
        ctx.JSInterop.Invocations["accessibleTrader.focusElement"]
           .Select(i => i.Arguments[0]?.ToString() ?? "")
           .ToList();

    [Fact]
    public void Escape_closes_the_dialog()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(PriceChartStyle.Renko));

        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();
        var cut = h.Ctx.RenderComponent<Cmp.ChartTypeSettingsModal>();
        h.EventBus.Publish(new OpenChartTypeSettingsEvent());
        cut.WaitForAssertion(() => Assert.Contains("Renko box", cut.Markup));

        h.EventBus.Publish(new CloseTopModalEvent("Brick settings"));

        cut.WaitForAssertion(() => Assert.DoesNotContain("Renko box", cut.Markup));
    }
}
