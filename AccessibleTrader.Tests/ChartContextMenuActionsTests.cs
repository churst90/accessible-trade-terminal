using System.Collections.Immutable;
using AngleSharp.Dom;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The chart context menu's expanded contract: every new item exists, in order, names its real
/// shortcut in words, publishes the right event with the right arguments, and refuses with a
/// spoken reason where it cannot act. The orders path NEVER places an order.
/// </summary>
public sealed class ChartContextMenuActionsTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static List<Ohlcv> Bars(int n = 10) =>
        Enumerable.Range(0, n)
            .Select(i => new Ohlcv(T0.AddHours(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 1000 + i))
            .ToList();

    private static ChartSeries Series(
        string id, string name, string pane = "Main", string code = "",
        params double[] values)
    {
        var config = new SeriesConfig
        {
            Id = id, Name = name, FriendlyName = name, IndicatorCode = code, Pane = pane,
        };
        config.Components.Add(new ComponentConfig { Name = code == "CANDLES" ? "body" : "line" });
        var buffer = new SeriesDataBuffer { SeriesId = id };
        buffer.ComponentData[config.Components[0].Name] = values;
        return new ChartSeries(config, buffer);
    }

    private static ChartSeries Drawing(string id = "draw-1", string name = "Text label: note")
    {
        var config = new SeriesConfig
        {
            Id = id, Name = name, FriendlyName = name, IndicatorCode = "", Pane = "Main",
        };
        config.Components.Add(new ComponentConfig { Name = "Label" });
        var buffer = new SeriesDataBuffer { SeriesId = id };
        buffer.ComponentData["Label"] = Enumerable.Repeat(103.5, 10).ToArray();
        return new ChartSeries(config, buffer)
        {
            Drawing = new DrawingData { Type = DrawingType.TextLabel, AnchorDate1 = T0, AnchorPrice1 = 103.5, Text = "note" },
        };
    }

    private static WorkspaceState State(
        IEnumerable<ChartSeries> series,
        string? focus = "candles",
        int index = 2,
        ProviderDataShape shape = ProviderDataShape.Ohlcv,
        string? provider = "Kraken")
    {
        var bars = Bars();
        return WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", provider ?? "", "BTC/USD", "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = series.ToImmutableList(),
            FocusedSeriesId = focus,
            FocusedComponentIndex = 0,
            CurrentDataIndex = index,
            ViewportRange = (100, 200),
            PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty
                .Add("Pane_Rsi", (0, 100)),
            ViewportStartIndex = 0,
            ViewportLength = bars.Count,
            CurrentDataShape = shape,
        };
    }

    private static BlazorTestHarness NewHarness(WorkspaceState state)
    {
        var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => state);
        h.OrderService.SupportsTradingAsync(Arg.Any<string>()).Returns(true);
        h.Ctx.Services.AddSingleton(new ChartHoverTracker(new BlazorInputService(), h.WorkspaceStore));
        return h;
    }

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.ChartContextMenu>
        OpenKeyboard(BlazorTestHarness h) =>
        h.OpenModal<AccessibleTrader.BlazorClient.Components.ChartContextMenu>(
            bus => bus.Publish(new OpenChartContextMenuEvent(double.NaN, double.NaN, 2)));

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.ChartContextMenu>
        OpenMouse(BlazorTestHarness h, double y = 420, int bar = 2, string? hit = null, int component = -1)
    {
        h.Ctx.Services.GetRequiredService<IPaneLayoutService>()
            .Update(new[] { ("Pane_Rsi", 0.5f) }, 0.1f, 0.05f);
        return h.OpenModal<AccessibleTrader.BlazorClient.Components.ChartContextMenu>(
            bus => bus.Publish(new OpenChartContextMenuEvent(100, y, bar, hit, component, 600)));
    }

    private static IElement Item(IRenderedFragment cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Contains(text, StringComparison.Ordinal));

    private static void SetDefaultShortcuts(BlazorTestHarness h)
    {
        var profile = new ShortcutProfile();
        profile.Shortcuts.Add(new(SystemCommand.DrawLabel, "L", Shift: true, Alt: true));
        profile.Shortcuts.Add(new(SystemCommand.DrawHorizontal, "H", Shift: true, Alt: true));
        profile.Shortcuts.Add(new(SystemCommand.OpenIndicators, "A", Alt: true));
        profile.Shortcuts.Add(new(SystemCommand.OpenObjectTree, "O", Alt: true));
        profile.Shortcuts.Add(new(SystemCommand.ReplayToggle, "F11"));
        profile.Shortcuts.Add(new(SystemCommand.ReplayToggle, "P", Ctrl: true, Alt: true, Shift: true));
        h.ShortcutManager.CurrentProfile.Returns(profile);
    }

    [Fact]
    public void New_items_are_in_order_after_jump_to_latest_and_name_their_real_chords()
    {
        var h = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }));
        using var _ = h;
        SetDefaultShortcuts(h);
        var cut = OpenKeyboard(h);

        string Markup() => cut.Markup;
        int jump = Markup().IndexOf("Jump to latest", StringComparison.Ordinal);
        int label = Markup().IndexOf("Add label here", StringComparison.Ordinal);
        int horizontal = Markup().IndexOf("Draw a horizontal line here", StringComparison.Ordinal);
        int alert = Markup().IndexOf("Add alert at", StringComparison.Ordinal);
        int buy = Markup().IndexOf("Buy limit at", StringComparison.Ordinal);
        int sell = Markup().IndexOf("Sell limit at", StringComparison.Ordinal);
        int copyPrice = Markup().IndexOf("Copy price", StringComparison.Ordinal);
        int copyBar = Markup().IndexOf("Copy this bar", StringComparison.Ordinal);
        int replay = Markup().IndexOf("Start bar replay here", StringComparison.Ordinal);
        int addIndicator = Markup().IndexOf("Add indicator", StringComparison.Ordinal);
        int objectTree = Markup().IndexOf("Object tree", StringComparison.Ordinal);
        int removeAll = Markup().IndexOf("Remove all drawings", StringComparison.Ordinal);

        Assert.True(jump >= 0 && label > jump && horizontal > label && alert > horizontal
                    && buy > alert && sell > buy && copyPrice > sell && copyBar > copyPrice
                    && replay > copyBar && addIndicator > replay && objectTree > addIndicator
                    && removeAll > objectTree,
            "the new chart actions are not in the order the menu contract names");

        Assert.Contains("Add label here..., Alt plus Shift plus L", Item(cut, "Add label here").TextContent);
        Assert.Contains("Draw a horizontal line here, Alt plus Shift plus H", Item(cut, "Draw a horizontal line").TextContent);
        Assert.Contains("Start bar replay here, Control plus Alt plus Shift plus P", Item(cut, "Start bar replay").TextContent);
        Assert.Contains("Add indicator..., Alt plus A", Item(cut, "Add indicator").TextContent);
        Assert.Contains("Object tree..., Alt plus O", Item(cut, "Object tree").TextContent);
    }

    [Fact]
    public void Arrow_keys_move_focus_over_the_menu_items_and_Tab_leaves()
    {
        var h = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }));
        using var _ = h;
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.initMenuFocus", _ => true).SetVoidResult();
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.moveMenuFocus", _ => true).SetVoidResult();
        var cut = OpenKeyboard(h);
        var menu = cut.Find("#chart-ctx-menu");

        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        menu.KeyDown(new KeyboardEventArgs { Key = "End" });
        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        menu.KeyDown(new KeyboardEventArgs { Key = "Home" });

        var moves = h.Ctx.JSInterop.Invocations["accessibleTrader.moveMenuFocus"];
        Assert.Equal(new object?[] { "chart-ctx-menu", 1 }, moves[0].Arguments.ToArray());
        Assert.Equal(new object?[] { "chart-ctx-menu", "end" }, moves[1].Arguments.ToArray());
        Assert.Equal(new object?[] { "chart-ctx-menu", -1 }, moves[2].Arguments.ToArray());
        Assert.Equal(new object?[] { "chart-ctx-menu", "home" }, moves[3].Arguments.ToArray());

        menu.KeyDown(new KeyboardEventArgs { Key = "Tab" });
        Assert.Empty(cut.FindAll("#chart-ctx-menu"));
    }

    [Fact]
    public void Add_label_here_places_a_label_at_the_keyboard_here_and_closes()
    {
        var h = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }));
        using var _ = h;
        var cut = OpenKeyboard(h);

        Item(cut, "Add label here").Click();

        h.DrawingManager.Received(1).AddTextLabelHere(
            2, 102.5, "Main", Arg.Any<IReadOnlyList<Ohlcv>>());
        Assert.Empty(cut.FindAll("[role='menu']"));
    }

    [Fact]
    public void Draw_horizontal_here_uses_the_clicked_indicator_panes_value_and_pane()
    {
        var h = NewHarness(State(new[]
        {
            Series("candles", "Candles", code: "CANDLES"),
            Series("rsi-1", "RSI 14", "Pane_Rsi", "RSI", 40, 45, 50, 55),
        }));
        using var _ = h;
        var cut = OpenMouse(h, y: 420);

        Item(cut, "Draw a horizontal line here").Click();

        // Half way down a 0–100 indicator plot.
        h.DrawingManager.Received(1).AddHorizontalLineHere(
            2, Arg.Is<double>(p => Math.Abs(p - 50) < 1e-6), "Pane_Rsi", Arg.Any<IReadOnlyList<Ohlcv>>());
    }

    [Fact]
    public void Add_alert_at_price_opens_the_alerts_modal_prefilled_with_the_price()
    {
        var h = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }));
        using var _ = h;
        OpenAlertsEvent? seen = null;
        h.EventBus.Subscribe<OpenAlertsEvent>(e => seen = e);
        var cut = OpenKeyboard(h);

        Item(cut, "Add alert at").Click();

        Assert.NotNull(seen);
        Assert.Equal(AlertTarget.Price, seen!.Target);
        Assert.Equal(102.5, seen.Value);
        Assert.Empty(cut.FindAll("[role='menu']"));
    }

    [Fact]
    public void Add_alert_in_an_indicator_pane_targets_that_series_and_component()
    {
        var h = NewHarness(State(new[]
        {
            Series("candles", "Candles", code: "CANDLES"),
            Series("rsi-1", "RSI 14", "Pane_Rsi", "RSI", 40, 45, 50, 55),
        }));
        using var _ = h;
        OpenAlertsEvent? seen = null;
        h.EventBus.Subscribe<OpenAlertsEvent>(e => seen = e);
        var cut = OpenMouse(h, y: 420);

        Item(cut, "Add alert at").Click();

        Assert.NotNull(seen);
        Assert.Equal(AlertTarget.Indicator, seen!.Target);
        Assert.Equal("rsi-1", seen.SeriesId);
        Assert.Equal("line", seen.ComponentName);
        Assert.NotNull(seen.Value);
        Assert.Equal(50, seen.Value!.Value, 6);
    }

    [Fact]
    public void Buy_and_sell_limits_open_the_dashboard_prefilled_and_never_place_an_order()
    {
        var h = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }));
        using var _ = h;
        var opened = new List<OpenTradingDashboardEvent>();
        h.EventBus.Subscribe<OpenTradingDashboardEvent>(opened.Add);
        var cut = OpenKeyboard(h);

        cut.WaitForAssertion(() => GatedButtonAssert.IsAvailable(cut, Item(cut, "Buy limit at")));
        Item(cut, "Buy limit at").Click();
        var buy = Assert.Single(opened);
        Assert.Equal("Buy", buy.Side);
        Assert.Equal(102.5, buy.LimitPrice);

        var cut2 = OpenKeyboard(h);
        cut2.WaitForAssertion(() => GatedButtonAssert.IsAvailable(cut2, Item(cut2, "Sell limit at")));
        Item(cut2, "Sell limit at").Click();
        Assert.Equal(2, opened.Count);
        var sell = opened[^1];
        Assert.Equal("Sell", sell.Side);
        Assert.Equal(102.5, sell.LimitPrice);

        h.OrderService.DidNotReceive().PlaceOrderAsync(
            Arg.Any<string>(), Arg.Any<TradeSignal>());
    }

    [Fact]
    public void Limit_orders_are_refused_with_the_dashboards_words_when_the_provider_cannot_trade()
    {
        var h = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }, provider: "FRED"));
        using var _ = h;
        h.OrderService.SupportsTradingAsync(Arg.Any<string>()).Returns(false);
        var cut = OpenKeyboard(h);

        var buy = Item(cut, "Buy limit at");
        cut.WaitForAssertion(() => GatedButtonAssert.IsRefusedBecause(
            cut, Item(cut, "Buy limit at"), "does not support order execution"));

        int opened = 0;
        using var sub = h.EventBus.Subscribe<OpenTradingDashboardEvent>(_ => opened++);
        buy.Click();
        Assert.Equal(0, opened);
    }

    [Fact]
    public void Limit_orders_are_refused_outside_the_price_pane_and_on_analytics_charts()
    {
        var h = NewHarness(State(new[]
        {
            Series("candles", "Candles", code: "CANDLES"),
            Series("rsi-1", "RSI 14", "Pane_Rsi", "RSI", 40, 45, 50, 55),
        }));
        using var _ = h;
        var cut = OpenMouse(h, y: 420);
        GatedButtonAssert.IsRefusedBecause(cut, Item(cut, "Buy limit at"), "price pane only");

        var analytics = NewHarness(State(new[] { Series("price", "Price", code: "PRICE") },
            focus: "price", shape: ProviderDataShape.SingleValueLine));
        using var _2 = analytics;
        var cut2 = OpenKeyboard(analytics);
        GatedButtonAssert.IsRefusedBecause(cut2, Item(cut2, "Buy limit at"), "analytics chart");

        var market = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }));
        using var _3 = market;
        market.MarketOrchestrator.SelectedMarket.Returns(MarketOrchestrator.AnalyticsMarket);
        var cut3 = OpenKeyboard(market);
        GatedButtonAssert.IsRefusedBecause(cut3, Item(cut3, "Buy limit at"), "analytics chart");
    }

    [Fact]
    public void Copy_price_puts_the_here_value_on_the_clipboard_and_says_so()
    {
        var h = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }));
        using var _ = h;
        h.Ctx.JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetVoidResult();
        var said = new List<AnnouncementEvent>();
        h.EventBus.Subscribe<AnnouncementEvent>(said.Add);
        var cut = OpenKeyboard(h);

        Item(cut, "Copy price").Click();

        string copied = (string)h.Ctx.JSInterop.Invocations["navigator.clipboard.writeText"].Last().Arguments[0]!;
        Assert.Equal("102.50", copied);
        Assert.Contains(said, a => a.Message == "Copied.");
    }

    [Fact]
    public void Copy_this_bar_is_one_line_with_the_charts_own_stamp_then_OHLCV()
    {
        var state = State(new[] { Series("candles", "Candles", code: "CANDLES") });
        var h = NewHarness(state);
        using var _ = h;
        h.Ctx.JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetVoidResult();
        var cut = OpenKeyboard(h);

        Item(cut, "Copy this bar").Click();

        string copied = (string)h.Ctx.JSInterop.Invocations["navigator.clipboard.writeText"].Last().Arguments[0]!;
        string when = PlaybackNarration.DateText(state.Data[2].Date, PlaybackNarration.BarSeconds(state));
        Assert.Equal($"{when}. Open 102.00 High 103.00 Low 101.00 Close 102.50 Volume 1,002", copied);
    }

    [Fact]
    public void A_refused_clipboard_says_so_instead_of_claiming_success()
    {
        var h = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }));
        using var _ = h;
        h.Ctx.JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true)
            .SetException(new InvalidOperationException("refused"));
        var said = new List<AnnouncementEvent>();
        h.EventBus.Subscribe<AnnouncementEvent>(said.Add);
        var cut = OpenKeyboard(h);

        Item(cut, "Copy price").Click();

        Assert.Contains(said, a => a.Message == "Could not copy: the browser refused.");
        Assert.DoesNotContain(said, a => a.Message == "Copied.");
    }

    [Fact]
    public void Start_bar_replay_moves_the_cursor_here_then_publishes_the_shortcuts_toggle()
    {
        var h = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }));
        using var _ = h;
        var replay = Substitute.For<IReplayService>();
        h.With<IReplayService>(replay);
        ReplayCommandEvent? seen = null;
        h.EventBus.Subscribe<ReplayCommandEvent>(e => seen = e);
        var cut = OpenKeyboard(h);

        Item(cut, "Start bar replay here").Click();

        h.WorkspaceStore.Received(1).Dispatch(Arg.Is<NavigateAction>(a => a.NewIndex == 2));
        Assert.NotNull(seen);
        Assert.Equal(ReplayCommand.Toggle, seen!.Command);
    }

    [Fact]
    public void While_replay_runs_the_item_says_stop_and_moves_nothing()
    {
        var h = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }));
        using var _ = h;
        var replay = Substitute.For<IReplayService>();
        replay.IsActive.Returns(true);
        h.With<IReplayService>(replay);
        ReplayCommandEvent? seen = null;
        h.EventBus.Subscribe<ReplayCommandEvent>(e => seen = e);
        var cut = OpenKeyboard(h);

        Item(cut, "Stop bar replay").Click();

        Assert.Equal(ReplayCommand.Toggle, seen!.Command);
        h.WorkspaceStore.DidNotReceive().Dispatch(Arg.Any<NavigateAction>());
    }

    [Fact]
    public void Add_indicator_and_object_tree_publish_their_existing_events()
    {
        var h = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }));
        using var _ = h;
        int indicators = 0, trees = 0;
        h.EventBus.Subscribe<OpenAddIndicatorEvent>(_ => indicators++);
        h.EventBus.Subscribe<OpenObjectTreeEvent>(_ => trees++);
        var cut = OpenKeyboard(h);

        Item(cut, "Add indicator").Click();
        var cut2 = OpenKeyboard(h);
        Item(cut2, "Object tree").Click();

        Assert.Equal(1, indicators);
        Assert.Equal(1, trees);
    }

    [Fact]
    public void Remove_all_drawings_asks_first_and_cancel_removes_nothing()
    {
        var h = NewHarness(State(new[]
        {
            Series("candles", "Candles", code: "CANDLES"), Drawing(),
        }));
        using var _ = h;
        int removed = 0;
        h.EventBus.Subscribe<RemoveAllDrawingsEvent>(_ => removed++);
        var cut = OpenKeyboard(h);

        Item(cut, "Remove all drawings").Click();

        Assert.Equal(0, removed);
        cut.WaitForElement("#chart-ctx-confirm-remove-all");
        Assert.Contains("Confirm remove all 1 drawing", cut.Find("#chart-ctx-confirm-remove-all").TextContent);

        cut.Find("#chart-ctx-cancel-remove-all").Click();
        Assert.Equal(0, removed);
        Assert.Empty(cut.FindAll("#chart-ctx-confirm-remove-all"));

        Item(cut, "Remove all drawings").Click();
        cut.WaitForElement("#chart-ctx-confirm-remove-all");
        cut.Find("#chart-ctx-confirm-remove-all").Click();

        Assert.Equal(1, removed);
        Assert.Empty(cut.FindAll("[role='menu']"));
    }

    [Fact]
    public void Remove_all_drawings_is_refused_when_there_are_none()
    {
        var h = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }));
        using var _ = h;
        var cut = OpenKeyboard(h);

        GatedButtonAssert.IsRefusedBecause(cut, Item(cut, "Remove all drawings"),
            "No drawings on this chart");
    }

    // ── Per-series submenu ───────────────────────────────────────────────────

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.ChartContextMenu>
        OpenSeries(BlazorTestHarness h, string seriesName)
    {
        var cut = OpenKeyboard(h);
        Item(cut, seriesName).Click();
        return cut;
    }

    [Fact]
    public void Add_alert_on_this_line_preselects_the_series_component_and_value()
    {
        var h = NewHarness(State(new[]
        {
            Series("candles", "Candles", code: "CANDLES"),
            Series("rsi-1", "RSI 14", "Pane_Rsi", "RSI", 40, 45, 52, 55),
        }));
        using var _ = h;
        OpenAlertsEvent? seen = null;
        h.EventBus.Subscribe<OpenAlertsEvent>(e => seen = e);
        var cut = OpenSeries(h, "RSI 14");

        Item(cut, "Add alert on this line").Click();

        Assert.NotNull(seen);
        Assert.Equal(AlertTarget.Indicator, seen!.Target);
        Assert.Equal("rsi-1", seen.SeriesId);
        Assert.Equal("line", seen.ComponentName);
        Assert.Equal(52, seen.Value);
    }

    [Fact]
    public void Copy_value_speaks_the_series_focused_component_value_on_this_bar()
    {
        var h = NewHarness(State(new[]
        {
            Series("candles", "Candles", code: "CANDLES"),
            Series("rsi-1", "RSI 14", "Pane_Rsi", "RSI", 40, 45, 52, 55),
        }));
        using var _ = h;
        h.Ctx.JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetVoidResult();
        var cut = OpenSeries(h, "RSI 14");

        Item(cut, "Copy value").Click();

        string copied = (string)h.Ctx.JSInterop.Invocations["navigator.clipboard.writeText"].Last().Arguments[0]!;
        Assert.Equal("52.00", copied);
    }

    [Fact]
    public void Move_to_a_new_pane_calls_the_service_and_announces_it()
    {
        var h = NewHarness(State(new[]
        {
            Series("candles", "Candles", code: "CANDLES"),
            Series("rsi-1", "RSI 14", "Pane_Rsi", "RSI", 40, 45, 52, 55),
        }));
        using var _ = h;
        h.SeriesManager.MoveSeriesToPane("rsi-1", null).Returns("Pane_RSI_2");
        var said = new List<AnnouncementEvent>();
        h.EventBus.Subscribe<AnnouncementEvent>(said.Add);
        var cut = OpenSeries(h, "RSI 14");

        Item(cut, "Move to a new pane").Click();

        h.SeriesManager.Received(1).MoveSeriesToPane("rsi-1", null);
        Assert.Contains(said, a => a.Message == "Moved RSI 14 to a new pane.");
    }

    [Fact]
    public void Move_to_the_price_pane_uses_Main()
    {
        var h = NewHarness(State(new[]
        {
            Series("candles", "Candles", code: "CANDLES"),
            Series("rsi-1", "RSI 14", "Pane_Rsi", "RSI", 40, 45, 52, 55),
        }));
        using var _ = h;
        h.SeriesManager.MoveSeriesToPane("rsi-1", "Main").Returns("Main");
        var said = new List<AnnouncementEvent>();
        h.EventBus.Subscribe<AnnouncementEvent>(said.Add);
        var cut = OpenSeries(h, "RSI 14");

        Item(cut, "Move to the price pane").Click();

        h.SeriesManager.Received(1).MoveSeriesToPane("rsi-1", "Main");
        Assert.Contains(said, a => a.Message == "Moved RSI 14 to the price pane.");
    }

    [Fact]
    public void Alerts_and_pane_moves_are_refused_for_drawings_and_provider_fixed_series()
    {
        var h = NewHarness(State(new[]
        {
            Series("candles", "Candles", code: "CANDLES"), Drawing(),
        }));
        using var _ = h;
        var cut = OpenSeries(h, "Text label: note");
        GatedButtonAssert.IsRefusedBecause(cut, Item(cut, "Add alert on this line"), "cannot watch a drawing");
        GatedButtonAssert.IsRefusedBecause(cut, Item(cut, "Move to a new pane"), "drawing cannot be moved");

        var h2 = NewHarness(State(new[] { Series("candles", "Candles", code: "CANDLES") }));
        using var _2 = h2;
        var cut2 = OpenSeries(h2, "Candles");
        GatedButtonAssert.IsRefusedBecause(cut2, Item(cut2, "Move to a new pane"), "provider fixes its pane");
    }
}
