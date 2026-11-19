using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Charts;
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
        } is var state ? PriceSeriesShape.Apply(state) : null!;
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

    // ── The price series takes the chart type's shape ─────────────────────────

    private static ChartSeries PriceSeries(WorkspaceState state) =>
        state.ActiveSeries.Single(s => s.Id == CoreSeriesIds.Candles);

    private static WorkspaceState Without(WorkspaceState state, string id) =>
        state with { ActiveSeries = state.ActiveSeries.RemoveAll(s => s.Id == id) };

    [Theory]
    [InlineData(PriceChartStyle.Line, ComponentDisplayType.Line, "line", "Close")]
    [InlineData(PriceChartStyle.Area, ComponentDisplayType.Area, "line", "Close")]
    [InlineData(PriceChartStyle.Renko, ComponentDisplayType.Brick, "brick", "Brick")]
    public void Choosing_a_type_gives_the_price_series_that_types_one_part(
        PriceChartStyle style, ComponentDisplayType type, string name, string display)
    {
        // Cody, 2026-11-19: "I switch to line and i still see candles ... I hear the 3 components
        // for upper/lower wicks and the bodies even on line series."
        var before = State(PriceChartStyle.Candles, CoreSeriesIds.Candles, context: InteractionContext.Component, component: 1)
            with { Renko = new RenkoSettings(RenkoBoxMode.Fixed, 2) };

        var after = Reduce(before, new SetChartStyleAction(style, false));

        var part = Assert.Single(PriceSeries(after).Components);
        Assert.Equal(name, part.Name);
        Assert.Equal(display, part.DisplayName);
        Assert.Equal(type, part.DisplayType);
        Assert.Equal("close", part.DataMapping);
        Assert.Equal(3, PriceSeries(after).Config.CandleParts.Count);     // kept, not lost
        Assert.Equal(CoreSeriesIds.Candles, after.FocusedSeriesId);       // the arrows stay on price
        Assert.Equal(0, after.FocusedComponentIndex);
    }

    [Fact]
    public void It_works_on_a_chart_with_no_Price_series()
    {
        // Cody's charts have no Price series: the first version needed one to take over.
        var after = Reduce(Without(State(PriceChartStyle.Candles, CoreSeriesIds.Candles), CoreSeriesIds.Price),
            new SetChartStyleAction(PriceChartStyle.Line, false));

        Assert.Equal("line", Assert.Single(PriceSeries(after).Components).Name);
        Assert.Equal(CoreSeriesIds.Candles, after.FocusedSeriesId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Going_back_to_candles_or_Heikin_Ashi_brings_the_candle_parts_back_as_they_were(bool heikinAshi)
    {
        var start = State(PriceChartStyle.Candles, CoreSeriesIds.Candles);
        var wick = PriceSeries(start).Components[0];
        wick.ColorHex = "#123456";
        wick.IsUserStyled = true;

        var line = Reduce(start, new SetChartStyleAction(PriceChartStyle.Line, false));
        PriceSeries(line).Components[0].IsMuted = true;     // the user mutes the line
        var back = Reduce(line, new SetChartStyleAction(PriceChartStyle.Candles, heikinAshi));

        var parts = PriceSeries(back).Components;
        Assert.Equal(new[] { "upper_wick", "body", "lower_wick" }, parts.Select(c => c.Name));
        Assert.Equal("#123456", parts[0].ColorHex);                 // the wick's own settings
        Assert.True(parts[1].IsMuted);                               // the line's mute is the price's
        Assert.Empty(PriceSeries(back).Config.CandleParts);
        Assert.Equal(CoreSeriesIds.Candles, back.FocusedSeriesId);
        Assert.Equal(1, back.FocusedComponentIndex);                 // the body, as on a load
    }

    [Fact]
    public void Line_to_Renko_and_back_swaps_the_one_part_and_keeps_the_candle_parts()
    {
        var line = Reduce(State(PriceChartStyle.Candles, CoreSeriesIds.Candles) with { Renko = new RenkoSettings(RenkoBoxMode.Fixed, 2) },
            new SetChartStyleAction(PriceChartStyle.Line, false));

        var renko = Reduce(line, new SetChartStyleAction(PriceChartStyle.Renko, false));
        Assert.Equal("brick", Assert.Single(PriceSeries(renko).Components).Name);
        Assert.Equal(3, PriceSeries(renko).Config.CandleParts.Count);

        var back = Reduce(renko, new SetChartStyleAction(PriceChartStyle.Line, false));
        Assert.Equal("line", Assert.Single(PriceSeries(back).Components).Name);
        Assert.Equal(CoreSeriesIds.Candles, back.FocusedSeriesId);
    }

    [Fact]
    public void Alt_C_from_a_line_chart_brings_the_candles_back()
    {
        var line = Reduce(State(PriceChartStyle.Candles, CoreSeriesIds.Candles), new SetChartStyleAction(PriceChartStyle.Line, false));

        var after = Reduce(line, new ToggleHeikinAshiAction());

        Assert.Equal(PriceChartStyle.Candles, after.ChartStyle);
        Assert.Equal(3, PriceSeries(after).Components.Count);
    }

    [Fact]
    public void A_series_list_handed_back_in_candle_shape_is_reshaped_again()
    {
        // Restore, tab switch, undo and Properties replace the series list wholesale.
        var line = Reduce(State(PriceChartStyle.Candles, CoreSeriesIds.Candles), new SetChartStyleAction(PriceChartStyle.Line, false));

        var after = Reduce(line, new UpdateSeriesAction(State(PriceChartStyle.Candles, CoreSeriesIds.Candles).ActiveSeries));

        Assert.Equal("line", Assert.Single(PriceSeries(after).Components).Name);
    }

    [Fact]
    public void A_save_stores_the_candle_shape_and_leaves_the_live_series_alone()
    {
        var line = Reduce(State(PriceChartStyle.Candles, CoreSeriesIds.Candles), new SetChartStyleAction(PriceChartStyle.Line, false));
        var live = PriceSeries(line).Config;

        var saved = PriceSeriesShape.Canonical(live);

        Assert.Equal(new[] { "upper_wick", "body", "lower_wick" }, saved.Components.Select(c => c.Name));
        Assert.Empty(saved.CandleParts);
        Assert.Equal("line", Assert.Single(live.Components).Name);
        var ma = line.ActiveSeries.Single(s => s.Id == "ma").Config;
        Assert.Same(ma, PriceSeriesShape.Canonical(ma));
    }

    [Fact]
    public void The_line_part_has_the_value_of_the_close_at_once()
    {
        var start = State(PriceChartStyle.Candles, CoreSeriesIds.Candles);
        PriceSeries(start).Data.ComponentData["body"] = new[] { 100.0, 104.2, 98.7 };

        var line = Reduce(start, new SetChartStyleAction(PriceChartStyle.Line, false));

        Assert.Equal(new[] { 100.0, 104.2, 98.7 }, PriceSeries(line).GetComponentData("line"));
    }

    // ── Focus follows the drawing ─────────────────────────────────────────────

    [Theory]
    [InlineData(PriceChartStyle.Line)]
    [InlineData(PriceChartStyle.Area)]
    public void Choosing_line_or_area_on_the_Price_line_moves_focus_to_the_price_series(PriceChartStyle style)
    {
        // The Price line repeats the close line and is not drawn there.
        var after = Reduce(State(PriceChartStyle.Candles, CoreSeriesIds.Price), new SetChartStyleAction(style, false));

        Assert.Equal(CoreSeriesIds.Candles, after.FocusedSeriesId);
        Assert.Equal(0, after.FocusedComponentIndex);
        Assert.Equal(InteractionContext.Series, after.LastInteractionContext);
    }

    [Fact]
    public void A_price_line_the_user_chose_on_a_candle_chart_stays_focused_through_Heikin_Ashi()
    {
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
    public void Selecting_the_Price_line_on_a_line_chart_lands_on_the_price_series()
    {
        var after = Reduce(State(PriceChartStyle.Line, "ma"), new SelectSeriesAction(CoreSeriesIds.Price));

        Assert.Equal(CoreSeriesIds.Candles, after.FocusedSeriesId);
    }

    [Fact]
    public void A_Price_line_on_the_high_is_a_different_line_and_stays_reachable()
    {
        var state = State(PriceChartStyle.Line, CoreSeriesIds.Candles, price: PriceLine("high"));

        Assert.False(ChartTypeFocus.PriceLineOffScreen(state));
        Assert.Equal(CoreSeriesIds.Price, Reduce(state, new SelectSeriesAction(CoreSeriesIds.Price)).FocusedSeriesId);
    }

    [Theory]
    [InlineData(PriceChartStyle.Candles, true)]
    [InlineData(PriceChartStyle.Line, false)]
    [InlineData(PriceChartStyle.Area, false)]
    [InlineData(PriceChartStyle.Renko, true)]
    public void The_Price_line_is_a_stop_only_when_it_is_drawn_and_the_price_series_always_is(PriceChartStyle style, bool navigable)
    {
        var state = State(style, CoreSeriesIds.Candles);

        Assert.Equal(navigable, ChartTypeFocus.IsNavigable(state, state.ActiveSeries.First(s => s.Id == CoreSeriesIds.Price)));
        Assert.True(ChartTypeFocus.IsNavigable(state, PriceSeries(state)));
    }

    [Theory]
    [InlineData(SystemCommand.SelectPrevSeries, "candles", "ma")]   // wraps past the Price line
    [InlineData(SystemCommand.SelectNextSeries, "candles", "ma")]   // steps over it
    public void The_series_cycle_keys_skip_the_Price_line_on_a_line_chart(SystemCommand command, string from, string to)
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

    // ── The price series reads the point on the line ──────────────────────────

    [Theory]
    [InlineData(InteractionContext.Series)]
    [InlineData(InteractionContext.Component)]
    public void On_a_line_chart_the_price_series_reads_the_close_and_its_move(InteractionContext context)
    {
        string spoken = Reading(State(PriceChartStyle.Line, CoreSeriesIds.Candles, context: context));

        Assert.Equal($"{P(104.2)}. Up {P(4.2)} from the previous close, 4.20 percent.", spoken);   // value only: this fixture's order
    }

    [Fact]
    public void On_a_line_chart_with_no_Price_series_the_one_part_reads_the_line_not_a_wick()
    {
        string spoken = Reading(Without(State(PriceChartStyle.Line, CoreSeriesIds.Candles,
            context: InteractionContext.Component), CoreSeriesIds.Price));

        Assert.Equal($"{P(104.2)}. Up {P(4.2)} from the previous close, 4.20 percent.", spoken);   // value only: this fixture's order
    }

    [Fact]
    public void On_an_area_chart_a_fall_reads_down()
    {
        string spoken = Reading(State(PriceChartStyle.Area, CoreSeriesIds.Candles) with { CurrentDataIndex = 2 });

        Assert.Equal($"{P(98.7)}. Down {P(5.5)} from the previous close, 5.28 percent.", spoken);
    }

    [Fact]
    public void On_a_candle_chart_the_price_line_keeps_its_old_reading()
    {
        string spoken = Reading(State(PriceChartStyle.Candles, CoreSeriesIds.Price));

        Assert.Equal($"Price. line. {P(104.2)}.", spoken);
    }

    // ── What the one part sounds like ─────────────────────────────────────────

    [Fact]
    public void A_brick_knocks_high_going_up_and_low_going_down()
    {
        var renko = Reduce(State(PriceChartStyle.Candles, CoreSeriesIds.Candles) with { Renko = new RenkoSettings(RenkoBoxMode.Fixed, 2) },
            new SetChartStyleAction(PriceChartStyle.Renko, false));
        var series = PriceSeries(renko);
        var brick = Assert.Single(series.Components);
        var sound = new AccessibleTrader.Core.Services.Audio.DefaultSonificationStrategy(new AccessibleTrader.Core.Services.Audio.SoundPatchRegistry());
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var up = sound.CreateAudioPoint(series, brick, 102, new Ohlcv(t, 100, 102, 100, 102, 0), 0, 10, (90, 110), 1f);
        var down = sound.CreateAudioPoint(series, brick, 98, new Ohlcv(t, 100, 100, 98, 98, 0), 0, 10, (90, 110), 1f);

        Assert.Equal("Ping", brick.EnvelopeType);
        Assert.Equal(PitchMapping.Direction, brick.PitchMapping);
        Assert.True(up.Frequency > down.Frequency);
    }

    [Fact]
    public void The_close_line_is_pitched_by_where_the_price_is()
    {
        var line = Reduce(State(PriceChartStyle.Candles, CoreSeriesIds.Candles), new SetChartStyleAction(PriceChartStyle.Line, false));
        var part = Assert.Single(PriceSeries(line).Components);

        Assert.Equal(PitchMapping.Value, part.PitchMapping);
        Assert.Equal("Sustain", part.EnvelopeType);
    }

    // ── Percent and Indexed lead every main-pane price reading ─────────────────

    [Theory]
    [InlineData(PriceChartStyle.Line, InteractionContext.Series)]
    [InlineData(PriceChartStyle.Line, InteractionContext.Component)]
    [InlineData(PriceChartStyle.Candles, InteractionContext.Series)]
    public void The_price_line_starts_with_the_percent(PriceChartStyle style, InteractionContext context)
    {
        // On Line the price series is the line; on Candles the Price line is.
        string focus = style == PriceChartStyle.Line ? CoreSeriesIds.Candles : CoreSeriesIds.Price;
        string spoken = Reading(State(style, focus, scale: PriceScaleMode.Percent, context: context));

        // The close line is a part read through the template, so the reading leads its value
        // as on the candle body; the Price indicator's summary leads the sentence.
        Assert.StartsWith(style == PriceChartStyle.Line ? $"plus 4.20 percent, {P(104.2)}. " : "Plus 4.20 percent. ", spoken);
    }

    // ── The price series is named after the chart type and read by the template ──

    [Theory]
    [InlineData(PriceChartStyle.Candles, false, "Candles")]
    [InlineData(PriceChartStyle.Candles, true, "Heikin Ashi")]
    [InlineData(PriceChartStyle.Line, false, "Line")]
    [InlineData(PriceChartStyle.Area, false, "Area")]
    [InlineData(PriceChartStyle.Renko, false, "Renko")]
    public void The_price_series_takes_the_chart_types_name(PriceChartStyle style, bool heikinAshi, string name)
    {
        // Cody, 2026-11-19: "when i change to renko and use pg up/down and land on that series,
        // it is still called candles even though they are renko bricks. same for line or area".
        // Through Renko first, so every case is a CHANGE of type (Candles → Candles would be none).
        var state = Reduce(State(PriceChartStyle.Candles, CoreSeriesIds.Candles) with { Renko = new RenkoSettings(RenkoBoxMode.Fixed, 2) },
            new SetChartStyleAction(PriceChartStyle.Renko, false), new SetChartStyleAction(style, heikinAshi));

        Assert.Equal(name, PriceSeries(state).Name);
        Assert.Equal(name, PriceSeries(state).FriendlyName);
    }

    [Fact]
    public void Back_on_candles_the_name_is_candles_again_and_a_save_keeps_candles()
    {
        var renko = Reduce(State(PriceChartStyle.Candles, CoreSeriesIds.Candles), new SetChartStyleAction(PriceChartStyle.Renko, false));
        Assert.Equal("Candles", PriceSeriesShape.Canonical(PriceSeries(renko).Config).Name);

        var back = Reduce(renko, new SetChartStyleAction(PriceChartStyle.Candles, false));
        Assert.Equal("Candles", PriceSeries(back).Name);
    }

    [Fact]
    public void A_name_the_user_gave_the_price_series_is_kept()
    {
        var mine = State(PriceChartStyle.Candles, CoreSeriesIds.Candles);
        var cfg = PriceSeries(mine).Config.Clone();
        cfg.Name = "My BTC";
        cfg.FriendlyName = "My BTC";
        mine = mine with { ActiveSeries = mine.ActiveSeries.Replace(PriceSeries(mine), new ChartSeries(cfg, PriceSeries(mine).Data)) };

        var line = Reduce(mine, new SetChartStyleAction(PriceChartStyle.Line, false));

        Assert.Equal("My BTC", PriceSeries(line).Name);
    }

    [Theory]
    [InlineData(PriceChartStyle.Line, "Line")]
    [InlineData(PriceChartStyle.Area, "Area")]
    [InlineData(PriceChartStyle.Renko, "Renko")]
    public void Paging_to_the_price_series_says_its_chart_type(PriceChartStyle style, string name)
    {
        var state = Reduce(State(PriceChartStyle.Candles, CoreSeriesIds.Candles) with { Renko = new RenkoSettings(RenkoBoxMode.Fixed, 2) },
            new SetChartStyleAction(style, false)) with { LastInteractionContext = InteractionContext.Series };
        string said = Speak(state);   // a first landing on the series names it

        Assert.StartsWith($"{name}. 1 component.", said);
        Assert.DoesNotContain("Candles", said);
    }

    [Theory]
    [InlineData(PriceChartStyle.Line)]
    [InlineData(PriceChartStyle.Area)]
    public void With_headers_the_close_line_reads_its_name_its_type_then_its_value(PriceChartStyle style)
    {
        // Cody: "as with any other line it needs to follow the speech template, component name,
        // component type." The move from the previous close is kept after the value.
        string spoken = Reading(State(style, CoreSeriesIds.Candles, context: InteractionContext.Component)
            with { SpeechOrder = "HeaderValue", ReadColumnHeaders = true });

        Assert.Equal($"Close. {(style == PriceChartStyle.Area ? "area" : "line")}. {P(104.2)}. Up {P(4.2)} from the previous close, 4.20 percent.", spoken);
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
