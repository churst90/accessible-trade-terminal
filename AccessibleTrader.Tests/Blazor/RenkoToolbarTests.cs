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

        cut.Find("#renko-box-size").Input("20");
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

        cut.Find("#renko-atr-length").Input("200");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply").Click();

        cut.WaitForAssertion(() => Assert.Contains("Renko needs at least 202 bars", cut.Markup));
        Assert.Contains(FocusTargets(h.Ctx), id => id == "renko-atr-length");
        h.WorkspaceStore.DidNotReceive().Dispatch(Arg.Any<SetRenkoSettingsAction>());
    }

    /// <summary>
    /// Type a box, press Enter: the box typed is the box applied. The browser sends Enter's
    /// keydown before the field's change event, so the field must bind on input — bound on
    /// change, Enter applied whatever the box was before the typing.
    /// </summary>
    [Fact]
    public void Enter_applies_the_box_just_typed()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(PriceChartStyle.Renko) with
        {
            Renko = new RenkoSettings(RenkoBoxMode.Fixed, 500, 14),
            BrickBoxSize = 500,
        });
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();
        var cut = h.Ctx.RenderComponent<Cmp.ChartTypeSettingsModal>();
        h.EventBus.Publish(new OpenChartTypeSettingsEvent());
        cut.WaitForAssertion(() => Assert.Contains("Renko box", cut.Markup));

        cut.Find("#renko-box-size").Input("20");
        cut.Find("#renko-box-size").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        h.WorkspaceStore.Received(1).Dispatch(
            Arg.Is<SetRenkoSettingsAction>(a => a.Settings.Mode == RenkoBoxMode.Fixed && a.Settings.BoxSize == 20));
    }

    // ---- Kagi's percentage reversal ----------------------------------------------------------

    private static IRenderedComponent<Cmp.ChartTypeSettingsModal> OpenDialog(BlazorTestHarness h, WorkspaceState state)
    {
        h.WorkspaceStore.State.Returns(_ => state);
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();
        var cut = h.Ctx.RenderComponent<Cmp.ChartTypeSettingsModal>();
        h.EventBus.Publish(new OpenChartTypeSettingsEvent());
        cut.WaitForAssertion(() => cut.Find("#renko-box-atr"));
        return cut;
    }

    [Fact]
    public void Percentage_is_offered_on_Kagi_and_point_and_figure_alone()
    {
        using (var h = new BlazorTestHarness())
            Assert.Empty(OpenDialog(h, State(PriceChartStyle.Renko)).FindAll("#kagi-reversal-percent-mode"));
        using (var h = new BlazorTestHarness())
            Assert.Single(OpenDialog(h, State(PriceChartStyle.Kagi)).FindAll("#kagi-reversal-percent-mode"));
        using (var h = new BlazorTestHarness())
            Assert.Single(OpenDialog(h, State(PriceChartStyle.PointAndFigure)).FindAll("#kagi-reversal-percent-mode"));
    }

    [Fact]
    public void A_point_and_figure_percentage_previews_its_box_from_the_last_price_and_applies()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenDialog(h, State(PriceChartStyle.PointAndFigure));   // last close 139

        cut.Find("#kagi-reversal-percent-mode").Change(true);
        Assert.Contains("Percentage of the last price", cut.Markup);
        Assert.Contains("Box percentage:", cut.Markup);
        cut.Find("#kagi-reversal-percent").Input("2");
        cut.WaitForAssertion(() => Assert.Equal("Resolved box: 2.7800.", cut.Find("#renko-box-preview").TextContent.Trim()));
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply").Click();

        h.WorkspaceStore.Received(1).Dispatch(Arg.Is<SetRenkoSettingsAction>(a =>
            a.Settings.PnfMode == RenkoBoxMode.Percent && a.Settings.PnfSize == 2
            && a.Settings.KagiMode == new RenkoSettings().KagiMode));
    }

    [Fact]
    public void A_point_and_figure_percentage_over_100_is_refused_in_its_own_words()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenDialog(h, State(PriceChartStyle.PointAndFigure));

        cut.Find("#kagi-reversal-percent-mode").Change(true);
        cut.Find("#kagi-reversal-percent").Input("101");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("A point and figure box percentage must be above 0 and at most 100.", cut.Markup));
        h.WorkspaceStore.DidNotReceive().Dispatch(Arg.Any<SetRenkoSettingsAction>());
    }

    [Fact]
    public void Point_and_figure_asks_close_or_high_and_low_and_keeps_the_choice()
    {
        using (var h = new BlazorTestHarness())
        {
            var cut = OpenDialog(h, State(PriceChartStyle.PointAndFigure));
            Assert.True(cut.Find("#pnf-source-close").HasAttribute("checked"));
            Assert.False(cut.Find("#pnf-source-high-low").HasAttribute("checked"));

            cut.Find("#pnf-source-high-low").Change(true);
            cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply").Click();
            h.WorkspaceStore.Received(1).Dispatch(Arg.Is<SetRenkoSettingsAction>(a => a.Settings.PnfHighLow));
        }
        using (var h = new BlazorTestHarness())
        {
            var cut = OpenDialog(h, State(PriceChartStyle.PointAndFigure) with
            {
                Renko = new RenkoSettings(PnfHighLow: true),
            });
            Assert.True(cut.Find("#pnf-source-high-low").HasAttribute("checked"));
        }
        using (var h = new BlazorTestHarness())
            Assert.Empty(OpenDialog(h, State(PriceChartStyle.Kagi)).FindAll("#pnf-source-close"));
    }

    [Fact]
    public void Choosing_Percentage_applies_a_percentage_reversal()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenDialog(h, State(PriceChartStyle.Kagi));

        cut.Find("#kagi-reversal-percent-mode").Change(true);
        cut.Find("#kagi-reversal-percent").Input("2");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply").Click();

        h.WorkspaceStore.Received(1).Dispatch(
            Arg.Is<SetRenkoSettingsAction>(a => a.Settings.KagiMode == RenkoBoxMode.Percent && a.Settings.KagiSize == 2));
    }

    [Fact]
    public void A_percentage_tab_prefills_its_percentage_and_never_as_a_price()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenDialog(h, State(PriceChartStyle.Kagi) with
        {
            Renko = new RenkoSettings(KagiMode: RenkoBoxMode.Percent, KagiSize: 3),
            BrickBoxSize = 3,
        });

        Assert.True(cut.Find("#kagi-reversal-percent-mode").HasAttribute("checked"));
        Assert.Equal("3", cut.Find("#kagi-reversal-percent").GetAttribute("value"));

        cut.Find("#renko-box-fixed").Change(true);
        Assert.Equal("0", cut.Find("#renko-box-size").GetAttribute("value"));
    }

    [Fact]
    public void A_percentage_over_100_is_refused_at_the_percentage_field()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenDialog(h, State(PriceChartStyle.Kagi));

        cut.Find("#kagi-reversal-percent-mode").Change(true);
        cut.Find("#kagi-reversal-percent").Input("150");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply").Click();

        cut.WaitForAssertion(() => Assert.Contains("A Kagi reversal percentage must be above 0 and at most 100.", cut.Markup));
        Assert.Contains(FocusTargets(h.Ctx), id => id == "kagi-reversal-percent");
        h.WorkspaceStore.DidNotReceive().Dispatch(Arg.Any<SetRenkoSettingsAction>());
    }

    private static List<string> FocusTargets(TestContext ctx) =>
        ctx.JSInterop.Invocations["accessibleTrader.focusElement"]
           .Select(i => i.Arguments[0]?.ToString() ?? "")
           .ToList();

    // ---- Point and figure's reversal ---------------------------------------------------------

    [Fact]
    public void The_select_offers_point_and_figure_and_the_dialog_asks_its_reversal()
    {
        using (var h = new BlazorTestHarness())
        {
            h.WorkspaceStore.State.Returns(_ => State());
            var toolbar = h.Ctx.RenderComponent<Cmp.Toolbar>();
            Assert.Contains(toolbar.FindAll("#chart-type-select option"),
                o => o.GetAttribute("value") == "pnf" && o.TextContent.Trim() == "Point and figure");
        }
        using (var h = new BlazorTestHarness())
            Assert.Empty(OpenDialog(h, State(PriceChartStyle.Kagi)).FindAll("#pnf-reversal"));
        using (var h = new BlazorTestHarness())
        {
            var cut = OpenDialog(h, State(PriceChartStyle.PointAndFigure) with
            {
                Renko = new RenkoSettings(PnfMode: RenkoBoxMode.Fixed, PnfSize: 2, PnfReversal: 2),
            });
            Assert.Contains("Point and figure settings", cut.Markup);
            Assert.Equal("2", cut.Find("#pnf-reversal").GetAttribute("value"));
            // A percentage box, TradingView's "Percentage (LTP)", is offered too.
            Assert.Single(cut.FindAll("#kagi-reversal-percent-mode"));
        }
    }

    [Fact]
    public void Apply_sets_the_point_and_figure_box_and_reversal()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenDialog(h, State(PriceChartStyle.PointAndFigure));

        cut.Find("#renko-box-fixed").Change(true);
        cut.Find("#renko-box-size").Input("1");
        cut.Find("#pnf-reversal").Input("2");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply").Click();

        h.WorkspaceStore.Received(1).Dispatch(Arg.Is<SetRenkoSettingsAction>(a =>
            a.Settings.PnfMode == RenkoBoxMode.Fixed && a.Settings.PnfSize == 1 && a.Settings.PnfReversal == 2
            && a.Settings.BoxSize == new RenkoSettings().BoxSize));
    }

    [Fact]
    public void A_reversal_outside_1_to_20_is_refused_at_the_reversal_field()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenDialog(h, State(PriceChartStyle.PointAndFigure));

        cut.Find("#pnf-reversal").Input("21");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply").Click();

        cut.WaitForAssertion(() => Assert.Contains("Point and figure takes a reversal of 1 to 20 boxes.", cut.Markup));
        Assert.Contains(FocusTargets(h.Ctx), id => id == "pnf-reversal");
        h.WorkspaceStore.DidNotReceive().Dispatch(Arg.Any<SetRenkoSettingsAction>());
    }

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
