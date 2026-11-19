using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// What the keys land on, and what they hear, when the chart type or the price scale changes.
///
/// <para>
/// Cody, 2026-11-19, after trying Line, Area and Percent: the picture changed and nothing he
/// heard did — only the candles' summary reading knew, so the Price line, every candle part
/// and the cycle keys were all the same as before. <i>"Whatever is selected should be mirrored
/// visually and through sound and speech."</i> These pin each place price is read in the main
/// pane, one chart type and one scale at a time.
/// </para>
/// </summary>
public sealed class ChartTypeFocusTests
{
    private static readonly ChartIdentity Id = new("Spot", "Bitstamp", "BTC/USD", "1d");

    private static ChartSeries Candles()
    {
        var config = new SeriesConfig { Id = CoreSeriesIds.Candles, IndicatorCode = "CANDLES", Name = "Candles", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "upper_wick", DisplayName = "Upper Wick", IsVisible = true,
            DisplayType = ComponentDisplayType.Wick, Role = ComponentRole.PriceAction, DataMapping = "high",
        });
        config.Components.Add(new ComponentConfig
        {
            Name = "body", DisplayName = "Body", IsVisible = true,
            DisplayType = ComponentDisplayType.Candle, Role = ComponentRole.Body, DataMapping = "close",
        });
        config.Components.Add(new ComponentConfig
        {
            Name = "lower_wick", DisplayName = "Lower Wick", IsVisible = true,
            DisplayType = ComponentDisplayType.Wick, Role = ComponentRole.PriceAction, DataMapping = "low",
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    /// <summary>The Price series as CoreIndicatorProvider declares it: one "line" part on the close.</summary>
    private static ChartSeries PriceLine(string mapping = "close")
    {
        var config = new SeriesConfig { Id = CoreSeriesIds.Price, IndicatorCode = "PRICE", Name = "Price", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "line", DisplayName = "Price", IsVisible = true,
            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.PriceAction, DataMapping = mapping,
            SpeechTemplate = "{name}. {type}. {value:price}.",
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    private static ChartSeries Overlay(string id)
    {
        var config = new SeriesConfig { Id = id, IndicatorCode = "SMA", Name = "Average", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "line", DisplayName = "Average", IsVisible = true,
            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.PriceAction,
            SpeechTemplate = "{name}. {value:price}.",
        });
        var buffer = new SeriesDataBuffer { SeriesId = id };
        buffer.ComponentData["line"] = new[] { 100.0, 104.2, 104.2 };
        return new ChartSeries(config, buffer);
    }

    private static WorkspaceState State(PriceChartStyle style, string focus, ChartSeries? price = null,
        PriceScaleMode scale = PriceScaleMode.Regular, InteractionContext context = InteractionContext.Series,
        int component = 0)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new[] { 100.0, 104.2, 98.7 }
            .Select((c, i) => new Ohlcv(start.AddDays(i), c - 1, c + 3, c - 3, c, 1000 + i)).ToList();
        return WorkspaceState.Initial with
        {
            Identity = Id,
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(Candles(), price ?? PriceLine(), Overlay("ma")),
            FocusedSeriesId = focus,
            FocusedComponentIndex = component,
            CurrentDataIndex = 1,
            ViewportStartIndex = 0,
            ViewportLength = bars.Count,
            ChartStyle = style,
            ScaleMode = scale,
            InitStatus = InitializationStatus.Ready,
            LastInteractionContext = context,
            SpeakTimestamps = false,
            TimestampReadLocation = "None",
            ReadColumnHeaders = false,
            SpeechOrder = "ValueOnly",
            ActiveTabIndex = 0,
            TabSnapshots = ImmutableList<TabSnapshot>.Empty,
        };
    }

    private static WorkspaceState Reduce(WorkspaceState initial, params WorkspaceAction[] actions)
    {
        using var store = new WorkspaceStore(new SpyEventBus(), new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        store.Dispatch(new UpdateSettingsAction(_ => initial));
        foreach (var a in actions) store.Dispatch(a);
        return store.State;
    }

    private static string Speak(WorkspaceState state)
    {
        var spy = new SpySpeechRouter();
        new NavigationFeedbackManager(spy, new SpeechFormatter()) { IsSpeechEnabled = true }
            .HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");
        Assert.NotEmpty(spy.SpokenTexts);
        return string.Join(" ", spy.SpokenTexts);
    }

    /// <summary>
    /// The reading itself: what <see cref="Speak"/> says minus the series announcement a first
    /// landing carries ("Price. 1 component.") and the pane it ends on ("Main pane.").
    /// </summary>
    private static string Reading(WorkspaceState state) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(Speak(state), @"^\w+\. \d+ components?\. ", ""),
            @"\s*Main pane\.$", "").Trim();

    private static string P(double v) => SpeechPriceFormatter.FormatPrice(v);

    // ── Focus follows the drawing ─────────────────────────────────────────────

    [Theory]
    [InlineData(PriceChartStyle.Line)]
    [InlineData(PriceChartStyle.Area)]
    public void Choosing_line_or_area_on_the_candles_moves_focus_to_the_price_line(PriceChartStyle style)
    {
        var after = Reduce(State(PriceChartStyle.Candles, CoreSeriesIds.Candles, context: InteractionContext.Component, component: 1),
            new SetChartStyleAction(style, false));

        Assert.Equal(CoreSeriesIds.Price, after.FocusedSeriesId);
        Assert.Equal(0, after.FocusedComponentIndex);
        Assert.Equal(InteractionContext.Series, after.LastInteractionContext);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Going_back_to_candles_or_Heikin_Ashi_moves_focus_back_to_the_candles(bool heikinAshi)
    {
        var after = Reduce(State(PriceChartStyle.Line, CoreSeriesIds.Price),
            new SetChartStyleAction(PriceChartStyle.Candles, heikinAshi));

        Assert.Equal(CoreSeriesIds.Candles, after.FocusedSeriesId);
        Assert.Equal(1, after.FocusedComponentIndex);   // the body, as on a load
    }

    [Fact]
    public void Renko_from_a_line_chart_moves_focus_back_to_the_bricks_and_Line_from_Renko_to_the_price_line()
    {
        var line = State(PriceChartStyle.Line, CoreSeriesIds.Price) with { Renko = new RenkoSettings(RenkoBoxMode.Fixed, 2) };

        var renko = Reduce(line, new SetChartStyleAction(PriceChartStyle.Renko, false));
        Assert.Equal(PriceChartStyle.Renko, renko.ChartStyle);
        Assert.Equal(CoreSeriesIds.Candles, renko.FocusedSeriesId);   // the bricks are drawn by the candles

        var back = Reduce(renko, new SetChartStyleAction(PriceChartStyle.Line, false));
        Assert.Equal(PriceChartStyle.Line, back.ChartStyle);
        Assert.Equal(CoreSeriesIds.Price, back.FocusedSeriesId);
    }

    [Fact]
    public void Alt_C_from_a_line_chart_also_moves_focus_back_to_the_candles()
    {
        var after = Reduce(State(PriceChartStyle.Line, CoreSeriesIds.Price), new ToggleHeikinAshiAction());

        Assert.Equal(PriceChartStyle.Candles, after.ChartStyle);
        Assert.Equal(CoreSeriesIds.Candles, after.FocusedSeriesId);
    }

    [Fact]
    public void Line_to_area_keeps_focus_on_the_price_line()
    {
        var after = Reduce(State(PriceChartStyle.Line, CoreSeriesIds.Price),
            new SetChartStyleAction(PriceChartStyle.Area, false));

        Assert.Equal(CoreSeriesIds.Price, after.FocusedSeriesId);
    }

    [Fact]
    public void A_price_line_the_user_chose_on_a_candle_chart_stays_focused_through_Heikin_Ashi()
    {
        // Only a move OUT of Line or Area hands focus back to the candles; on a candle chart
        // the Price line is a series the user walked to on purpose.
        var after = Reduce(State(PriceChartStyle.Candles, CoreSeriesIds.Price),
            new SetChartStyleAction(PriceChartStyle.Candles, true));

        Assert.Equal(CoreSeriesIds.Price, after.FocusedSeriesId);
    }

    [Fact]
    public void Focus_on_an_indicator_is_left_where_it_is()
    {
        var after = Reduce(State(PriceChartStyle.Candles, "ma"), new SetChartStyleAction(PriceChartStyle.Line, false));

        Assert.Equal("ma", after.FocusedSeriesId);
    }

    [Fact]
    public void Selecting_the_candles_on_a_line_chart_lands_on_the_price_line()
    {
        // Load, the object tree, a pane jump and a click all focus a series this way.
        var after = Reduce(State(PriceChartStyle.Line, "ma"), new SelectSeriesAction(CoreSeriesIds.Candles));

        Assert.Equal(CoreSeriesIds.Price, after.FocusedSeriesId);
    }

    [Fact]
    public void Removing_the_focused_series_on_a_line_chart_falls_back_to_the_price_line()
    {
        var after = Reduce(State(PriceChartStyle.Line, "ma"), new RemoveSeriesAction("ma"));

        Assert.Equal(CoreSeriesIds.Price, after.FocusedSeriesId);
    }

    [Fact]
    public void With_no_close_line_to_walk_the_candles_stay_reachable()
    {
        // A Price line remapped to the high is not the line on screen; the candles stay the
        // stop and read the point on the line themselves.
        var state = State(PriceChartStyle.Line, CoreSeriesIds.Candles, price: PriceLine("high"));

        Assert.False(ChartTypeFocus.CandlesOffScreen(state));
        Assert.Equal(CoreSeriesIds.Candles, Reduce(state, new SelectSeriesAction(CoreSeriesIds.Candles)).FocusedSeriesId);
        Assert.Equal(CoreSeriesIds.Candles,
            Reduce(State(PriceChartStyle.Candles, CoreSeriesIds.Candles, price: PriceLine("high")),
                new SetChartStyleAction(PriceChartStyle.Line, false)).FocusedSeriesId);
    }

    [Theory]
    [InlineData(PriceChartStyle.Candles, true)]
    [InlineData(PriceChartStyle.Line, false)]
    [InlineData(PriceChartStyle.Area, false)]
    public void The_candles_are_a_stop_only_when_they_are_drawn(PriceChartStyle style, bool navigable)
    {
        var state = State(style, CoreSeriesIds.Price);
        var candles = state.ActiveSeries.First(s => s.Id == CoreSeriesIds.Candles);

        Assert.Equal(navigable, ChartTypeFocus.IsNavigable(state, candles));
        Assert.True(ChartTypeFocus.IsNavigable(state, state.ActiveSeries.First(s => s.Id == CoreSeriesIds.Price)));
    }

    [Theory]
    [InlineData(SystemCommand.SelectPrevSeries, "price", "ma")]   // wraps past the candles
    [InlineData(SystemCommand.SelectNextSeries, "ma", "price")]   // wraps onto the line, not the candles
    public void The_series_cycle_keys_skip_the_candles_on_a_line_chart(SystemCommand command, string from, string to)
    {
        var store = new MockWorkspaceStore();
        store.EmitState(State(PriceChartStyle.Line, from));
        var bus = new SpyEventBus();
        var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
        bus.Publish(new ChartFocusEvent());   // the keys are chart keys: the chart has focus

        dispatcher.Dispatch(command);

        Assert.Equal(to, store.DispatchedActions.OfType<SelectSeriesAction>().Single().SeriesId);
    }

    // ── The Price line reads the point on the line ────────────────────────────

    [Theory]
    [InlineData(InteractionContext.Series)]
    [InlineData(InteractionContext.Component)]
    public void On_a_line_chart_the_price_line_reads_the_close_and_its_move(InteractionContext context)
    {
        string spoken = Reading(State(PriceChartStyle.Line, CoreSeriesIds.Price, context: context));

        Assert.Equal($"Close {P(104.2)}. Up {P(4.2)} from the previous close, 4.20 percent.", spoken);
    }

    [Fact]
    public void On_an_area_chart_a_fall_reads_down()
    {
        string spoken = Reading(State(PriceChartStyle.Area, CoreSeriesIds.Price) with { CurrentDataIndex = 2 });

        Assert.Equal($"Close {P(98.7)}. Down {P(5.5)} from the previous close, 5.28 percent.", spoken);
    }

    [Fact]
    public void On_a_candle_chart_the_price_line_keeps_its_old_reading()
    {
        string spoken = Reading(State(PriceChartStyle.Candles, CoreSeriesIds.Price));

        Assert.Equal($"Price. line. {P(104.2)}.", spoken);
    }

    // ── Percent and Indexed lead every main-pane price reading ─────────────────

    [Theory]
    [InlineData(PriceChartStyle.Line, InteractionContext.Series)]
    [InlineData(PriceChartStyle.Line, InteractionContext.Component)]
    [InlineData(PriceChartStyle.Candles, InteractionContext.Series)]
    public void The_price_line_starts_with_the_percent(PriceChartStyle style, InteractionContext context)
    {
        string spoken = Reading(State(style, CoreSeriesIds.Price, scale: PriceScaleMode.Percent, context: context));

        Assert.StartsWith("Plus 4.20 percent. ", spoken);
    }

    [Fact]
    public void The_price_line_on_a_candle_chart_reads_the_percent_in_component_context_too()
    {
        string spoken = Speak(State(PriceChartStyle.Candles, CoreSeriesIds.Price,
            scale: PriceScaleMode.Percent, context: InteractionContext.Component));

        Assert.Contains($"plus 4.20 percent, {P(104.2)}", spoken);
    }

    [Fact]
    public void The_price_line_starts_with_the_index_on_an_indexed_chart()
    {
        string spoken = Reading(State(PriceChartStyle.Candles, CoreSeriesIds.Price, scale: PriceScaleMode.IndexedTo100));

        Assert.StartsWith("Index 104.20. ", spoken);
    }

    [Fact]
    public void A_price_line_on_the_high_does_not_claim_the_close_percent()
    {
        // The prefix is the close's place on the axis; a line drawn on the high is somewhere
        // else, and its own {value:price} token reads its own place.
        string spoken = Speak(State(PriceChartStyle.Candles, CoreSeriesIds.Price, price: PriceLine("high"),
            scale: PriceScaleMode.Percent));

        Assert.DoesNotContain("Plus 4.20 percent. ", spoken);
    }

    [Theory]
    [InlineData(0, 107.2, "ValueOnly")]    // upper wick: the high
    [InlineData(1, 104.2, "ValueOnly")]    // body: the close
    [InlineData(2, 101.2, "ValueOnly")]    // lower wick: the low
    [InlineData(0, 107.2, "HeaderValue")]
    [InlineData(1, 104.2, "HeaderValue")]
    [InlineData(2, 101.2, "HeaderValue")]
    public void Each_candle_part_reads_its_own_percent(int component, double price, string order)
    {
        string spoken = Speak(State(PriceChartStyle.Candles, CoreSeriesIds.Candles, scale: PriceScaleMode.Percent,
            context: InteractionContext.Component, component: component)
            with { SpeechOrder = order, ReadColumnHeaders = order != "ValueOnly" });

        double percent = (price - 100) / 100 * 100;
        Assert.Contains($"plus {percent:F2} percent", spoken, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ValueOnly")]
    [InlineData("HeaderValue")]
    public void The_price_line_on_a_candle_chart_reads_the_percent_in_either_order(string order)
    {
        string spoken = Speak(State(PriceChartStyle.Candles, CoreSeriesIds.Price, scale: PriceScaleMode.Percent,
            context: InteractionContext.Component) with { SpeechOrder = order, ReadColumnHeaders = order != "ValueOnly" });

        Assert.Contains($"plus 4.20 percent, {P(104.2)}", spoken);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(spoken, "percent"));   // said once
    }

    [Fact]
    public void Regular_candle_parts_are_unchanged()
    {
        Assert.Equal($"Open {P(103.2)}, close {P(104.2)}",
            Reading(State(PriceChartStyle.Candles, CoreSeriesIds.Candles,
                context: InteractionContext.Component, component: 1)));
        Assert.Equal(P(107.2), Reading(State(PriceChartStyle.Candles, CoreSeriesIds.Candles,
            context: InteractionContext.Component, component: 0)));
    }

    [Fact]
    public void An_indicator_pane_value_keeps_its_plain_reading()
    {
        // The scale is the main pane's. An RSI in its own pane reads its own number.
        var rsiConfig = new SeriesConfig { Id = "rsi", IndicatorCode = "RSI", Name = "RSI", Pane = "Pane_RSI" };
        rsiConfig.Components.Add(new ComponentConfig
        {
            Name = "rsi", DisplayName = "RSI", IsVisible = true, DisplayType = ComponentDisplayType.Line,
            Role = ComponentRole.PriceAction, SpeechTemplate = "{name}. {value:price}.",
        });
        var buffer = new SeriesDataBuffer { SeriesId = "rsi" };
        buffer.ComponentData["rsi"] = new[] { 55.0, 61.0, 48.0 };
        var rsi = new ChartSeries(rsiConfig, buffer);
        var state = State(PriceChartStyle.Candles, "rsi", scale: PriceScaleMode.Percent,
            context: InteractionContext.Component);
        state = state with { ActiveSeries = state.ActiveSeries.Add(rsi) };

        Assert.DoesNotContain("percent", Speak(state));
    }

    [Fact]
    public void Regular_adds_nothing_to_the_price_line()
    {
        string spoken = Speak(State(PriceChartStyle.Candles, CoreSeriesIds.Price));

        Assert.DoesNotContain("percent", spoken);
        Assert.DoesNotContain("Index", spoken);
    }
}
