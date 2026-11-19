using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.GoBar;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The price-scale menu is reachable from the Go bar without a new key: REG / PCT / IDX, each
/// with plain aliases, each answering honestly when the ruler is already on screen or the base
/// cannot carry it. The own-words-first guard runs in GoBarArgumentTests over every entry.
/// </summary>
public sealed class PriceScaleGoBarTests
{
    private static GoBarEntry Entry(SystemCommand command) =>
        GoBarRegistry.All.Single(e => e.Command == command);

    [Theory]
    [InlineData(SystemCommand.ShowRegularScale, "REG", "regular scale", "linear")]
    [InlineData(SystemCommand.ShowPercentScale, "PCT", "percent scale", "percentage")]
    [InlineData(SystemCommand.ShowIndexedScale, "IDX", "indexed", "rebase")]
    public void Each_scale_is_reachable_by_its_own_words(SystemCommand command, string code, string alias, string secondAlias)
    {
        var entry = Entry(command);

        foreach (string text in new[] { entry.Name, code, alias, secondAlias })
        {
            var top = GoBarMatcher.Match(text, GoBarRegistry.All).FirstOrDefault()?.Entry;
            Assert.Equal(command, top?.Command);
        }
    }

    private static (CommandDispatcher Dispatcher, MockWorkspaceStore Store, SpyEventBus Bus)
        Build(PriceScaleMode mode = PriceScaleMode.Regular, bool log = false,
              double firstClose = 100, ProviderDataShape shape = ProviderDataShape.Ohlcv)
    {
        var bus = new SpyEventBus();
        var nav = Substitute.For<INavigationEngine>();
        var store = new MockWorkspaceStore();
        var config = new SeriesConfig { Id = "candles", Name = "Candles" };
        var bars = Enumerable.Range(0, 4).Select(i => new Ohlcv(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
            firstClose + i, firstClose + i + 1, firstClose + i - 1, firstClose + i, 1000)).ToList();
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" })),
            FocusedSeriesId = "candles",
            ViewportStartIndex = 0,
            ViewportLength = bars.Count,
            ScaleMode = mode,
            IsLogScale = log,
            CurrentDataShape = shape,
        });
        var barDetail = Substitute.For<IBarDetailService>();
        var crossing = new IndicatorCrossingEngine(store, bus);
        return (new CommandDispatcher(bus, nav, store, barDetail, crossing), store, bus);
    }

    [Theory]
    [InlineData(SystemCommand.ShowRegularScale, PriceScaleMode.Regular)]
    [InlineData(SystemCommand.ShowPercentScale, PriceScaleMode.Percent)]
    [InlineData(SystemCommand.ShowIndexedScale, PriceScaleMode.IndexedTo100)]
    public void The_commands_dispatch_the_chosen_ruler(SystemCommand command, PriceScaleMode mode)
    {
        // Start on a different ruler each time, so "already" cannot satisfy this.
        var from = command == SystemCommand.ShowPercentScale ? PriceScaleMode.IndexedTo100 : PriceScaleMode.Percent;
        var (dispatcher, store, _) = Build(from);

        dispatcher.DispatchFromGoBar(command);

        var action = Assert.Single(store.DispatchedActions.OfType<SetPriceScaleAction>());
        Assert.Equal(mode, action.Mode);
        Assert.False(action.Log);
    }

    [Fact]
    public void Choosing_regular_while_log_is_on_turns_log_off()
    {
        var (dispatcher, store, _) = Build(mode: PriceScaleMode.Regular, log: true);

        dispatcher.DispatchFromGoBar(SystemCommand.ShowRegularScale);

        var action = Assert.Single(store.DispatchedActions.OfType<SetPriceScaleAction>());
        Assert.Equal(PriceScaleMode.Regular, action.Mode);
        Assert.False(action.Log);
    }

    [Theory]
    [InlineData(SystemCommand.ShowRegularScale, "Already a regular scale.")]
    [InlineData(SystemCommand.ShowPercentScale, "Already a percent scale.")]
    [InlineData(SystemCommand.ShowIndexedScale, "Already indexed to 100.")]
    public void Choosing_the_ruler_already_shown_says_already_and_changes_nothing(
        SystemCommand command, string expected)
    {
        var mode = command switch
        {
            SystemCommand.ShowPercentScale => PriceScaleMode.Percent,
            SystemCommand.ShowIndexedScale => PriceScaleMode.IndexedTo100,
            _ => PriceScaleMode.Regular,
        };
        var (dispatcher, store, bus) = Build(mode);

        dispatcher.DispatchFromGoBar(command);

        Assert.Empty(store.DispatchedActions.OfType<SetPriceScaleAction>());
        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(), e => e.Message == expected);
    }

    [Theory]
    [InlineData(SystemCommand.ShowPercentScale, "Percent scale needs a positive first close; showing the regular scale.")]
    [InlineData(SystemCommand.ShowIndexedScale, "Indexed to 100 needs a positive first close; showing the regular scale.")]
    public void An_unusable_base_refuses_aloud_and_changes_nothing(SystemCommand command, string expected)
    {
        var (dispatcher, store, bus) = Build(firstClose: 0);

        dispatcher.DispatchFromGoBar(command);

        Assert.Empty(store.DispatchedActions.OfType<SetPriceScaleAction>());
        Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(), e => e.Message == expected);
    }

    [Fact]
    public void An_analytics_chart_still_gets_the_scale_like_the_log_button()
    {
        // The chart-type commands refuse a single-line chart; the ruler does not, because
        // TradingView applies it to any series and the log button is not refused either.
        var (dispatcher, store, _) = Build(shape: ProviderDataShape.SingleValueLine);

        dispatcher.DispatchFromGoBar(SystemCommand.ShowPercentScale);

        var action = Assert.Single(store.DispatchedActions.OfType<SetPriceScaleAction>());
        Assert.Equal(PriceScaleMode.Percent, action.Mode);
    }
}
