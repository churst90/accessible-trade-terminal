using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using NSubstitute;
using Cmp = AccessibleTrader.BlazorClient.Components;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The toolbar's price-scale select, right before the Alt+L Log scale button: the four entries
/// of TradingView's menu, one choice, labelled and gated exactly like that button. Choosing
/// Percent or Indexed on a base that cannot carry it refuses aloud rather than relabelling a
/// zero; Regular and Logarithmic write the existing log flag.
/// </summary>
public sealed class PriceScaleToolbarTests
{
    private static WorkspaceState State(PriceScaleMode mode = PriceScaleMode.Regular,
        bool log = false, ProviderDataShape shape = ProviderDataShape.Ohlcv, double firstClose = 100)
    {
        var config = new SeriesConfig { Id = "candles", Name = "Candles" };
        var bars = Enumerable.Range(0, 4).Select(i => new Ohlcv(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
            firstClose + i, firstClose + i + 1, firstClose + i - 1, firstClose + i, 1000)).ToList();
        return WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "kraken", "BTC/USD", "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" })),
            FocusedSeriesId = "candles",
            ViewportStartIndex = 0,
            ViewportLength = bars.Count,
            ScaleMode = mode,
            IsLogScale = log,
            CurrentDataShape = shape,
        };
    }

    [Fact]
    public void The_select_has_a_visually_hidden_label_and_shows_the_current_ruler()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(PriceScaleMode.Percent));

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();

        Assert.Equal("Price scale", cut.Find("label[for=price-scale-select]").TextContent.Trim());
        Assert.Contains("visually-hidden", cut.Find("label[for=price-scale-select]").ClassList);
        Assert.Equal("percent", cut.Find("#price-scale-select").GetAttribute("value"));
    }

    [Fact]
    public void Indexed_shows_as_its_own_entry()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(PriceScaleMode.IndexedTo100));

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();

        Assert.Equal("indexed", cut.Find("#price-scale-select").GetAttribute("value"));
        Assert.Contains("Indexed to 100",
            cut.FindAll("#price-scale-select option").Select(o => o.TextContent));
    }

    [Fact]
    public void The_log_flag_shows_as_Logarithmic_and_wins_over_a_stale_mode()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(PriceScaleMode.Percent, log: true));

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();

        Assert.Equal("logarithmic", cut.Find("#price-scale-select").GetAttribute("value"));
    }

    [Theory]
    [InlineData("percent", PriceScaleMode.Percent, false)]
    [InlineData("indexed", PriceScaleMode.IndexedTo100, false)]
    [InlineData("regular", PriceScaleMode.Regular, false)]
    [InlineData("logarithmic", PriceScaleMode.Regular, true)]
    public void Changing_it_dispatches_that_ruler(string value, PriceScaleMode mode, bool log)
    {
        using var h = new BlazorTestHarness();
        var from = mode == PriceScaleMode.IndexedTo100 ? PriceScaleMode.Percent : PriceScaleMode.IndexedTo100;
        h.WorkspaceStore.State.Returns(_ => State(from, log: false));

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        cut.Find("#price-scale-select").Change(value);

        h.WorkspaceStore.Received(1).Dispatch(
            Arg.Is<SetPriceScaleAction>(a => a.Mode == mode && a.Log == log));
    }

    [Fact]
    public void Choosing_regular_while_log_is_on_turns_the_log_off()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(log: true));

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        cut.Find("#price-scale-select").Change("regular");

        h.WorkspaceStore.Received(1).Dispatch(
            Arg.Is<SetPriceScaleAction>(a => a.Mode == PriceScaleMode.Regular && !a.Log));
    }

    [Fact]
    public void Choosing_Logarithmic_when_it_is_already_on_says_already_and_changes_nothing()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(log: true));
        var said = new List<string>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) said.Add(e.Message); });

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        cut.Find("#price-scale-select").Change("logarithmic");

        h.WorkspaceStore.DidNotReceive().Dispatch(Arg.Any<WorkspaceAction>());
        Assert.Contains("Already a log scale.", said);
    }

    [Fact]
    public void Choosing_Percent_on_a_zero_base_refuses_aloud_and_changes_nothing()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(firstClose: 0));
        var said = new List<string>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) said.Add(e.Message); });

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        cut.Find("#price-scale-select").Change("percent");

        h.WorkspaceStore.DidNotReceive().Dispatch(Arg.Any<WorkspaceAction>());
        Assert.Contains("Percent scale needs a positive first close; showing the regular scale.", said);
    }

    [Fact]
    public void It_is_absent_on_an_analytics_chart_like_the_Log_button()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(shape: ProviderDataShape.SingleValueLine));

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();

        Assert.Empty(cut.FindAll("#price-scale-select"));
        Assert.Empty(cut.FindAll("label[for=price-scale-select]"));
    }
}
