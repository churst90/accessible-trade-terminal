// The simple (non-Advanced) alert form, against Cody's report from live use, 2026-10-09:
// "when I select price as the target, I see only options for cross above, cross below,
// enter/exit zone, but price has no zones so this wouldn't work. also, if I select SMA on the
// chart under indicator, this doesn't have overbought/oversold zones and I expect the current
// price of that indicator to be filled in. … what do i select if I want to know if price merely
// touches a price. what if I want to know if price touches the 50 week…"

using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class AlertsModalSimpleFormTests
{
    private sealed class StubAlertOrchestrator : IAlertOrchestrator
    {
        private readonly List<AlertDefinition> _alerts = new();
        public List<AlertDefinition> Added { get; } = new();
        public void Start() { }
        public void Stop() { }
        public void AddAlert(AlertDefinition alert) { _alerts.Add(alert); Added.Add(alert); }
        public void Seed(AlertDefinition alert) => _alerts.Add(alert);
        public void RemoveAlert(string id) => _alerts.RemoveAll(a => a.Id == id);
        public IEnumerable<AlertDefinition> GetAlerts() => _alerts;
    }

    private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    private static ChartSeries Series(string id, string code, string friendly, string pane,
        IEnumerable<LevelConfig>? levels, params (string Name, double Last)[] comps)
    {
        var buffer = new SeriesDataBuffer { SeriesId = id };
        var config = new SeriesConfig { Id = id, Name = friendly, FriendlyName = friendly, IndicatorCode = code, Pane = pane };
        foreach (var (name, last) in comps)
        {
            buffer.ComponentData[name] = Enumerable.Repeat(last - 1, 9).Append(last).ToArray();
            config.Components.Add(new ComponentConfig { Name = name, DisplayName = name });
        }
        foreach (var l in levels ?? Array.Empty<LevelConfig>()) config.Levels.Add(l);
        return new ChartSeries(config, buffer);
    }

    /// <summary>
    /// A chart with ten bars (last close 64,250.5), an RSI that declares 70/30, a lone
    /// SMA 50 and a Bollinger Bands overlay on the price pane, and an SMA 20 beside the SMA 50
    /// so "which SMA" is a real question.
    /// </summary>
    private static (TestContext ctx, StubAlertOrchestrator orch, IEventBus bus) BuildContext()
        => BuildContext(out _);

    /// <param name="store">The store, so a test can change the chart between two openings.</param>
    /// <param name="noIndicators">A chart with bars and nothing on it.</param>
    /// <param name="lastClose">The last close, when the test needs a sub-cent price.</param>
    private static (TestContext ctx, StubAlertOrchestrator orch, IEventBus bus) BuildContext(
        out IWorkspaceStore store, bool noIndicators = false, double? lastClose = null)
    {
        var ctx = new TestContext();
        var orch = new StubAlertOrchestrator();
        IEventBus bus = new EventBus();
        ctx.Services.AddSingleton<IAlertOrchestrator>(orch);
        ctx.Services.AddSingleton(bus);
        ctx.Services.AddSingleton(new DemoPolicy(isDemo: false));

        var bars = Enumerable.Range(0, 10)
            .Select(i => new Ohlcv(T0.AddHours(i), 64_000 + i, 64_300 + i, 63_900 + i, 64_241.5 + i, 10))
            .ToArray();   // last close 64,250.5
        if (lastClose is { } lc) bars[^1] = bars[^1] with { Close = lc };

        var rsi = Series("rsi-1", "Rsi", "RSI", "Pane_Rsi", new[]
        {
            new LevelConfig { Name = "Overbought", Value = 70 },
            new LevelConfig { Name = "Midpoint", Value = 50 },
            new LevelConfig { Name = "Oversold", Value = 30 },
        }, ("Rsi", 61.25));
        var sma20 = Series("sma-20", "Sma", "SMA 20", "Main", null, ("Sma", 64_100.125));
        var sma50 = Series("sma-50", "Sma", "SMA 50", "Main", null, ("Sma", 63_000.75));
        var bb = Series("bb-1", "BB", "Bollinger Bands", "Main", null,
            ("UpperBand", 65_000), ("Centerline", 64_000), ("LowerBand", 63_000));
        // A second RSI whose user deleted the oversold line: it has ONE zone.
        var rsi7 = Series("rsi-7", "Rsi", "RSI 7", "Pane_Rsi7", new[]
        {
            new LevelConfig { Name = "Overbought", Value = 80 },
        }, ("Rsi", 55));

        store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(WorkspaceState.Initial with
        {
            SymbolDisplayName = "BTC/USD",
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = 3,   // the reading cursor is NOT the live bar
            ActiveSeries = noIndicators ? ImmutableList<ChartSeries>.Empty : ImmutableList.Create(rsi, sma20, sma50, bb, rsi7),
        });
        ctx.Services.AddSingleton(store);
        ctx.Services.AddSingleton(Substitute.For<ISettingsManager>());
        var catalog = Substitute.For<AccessibleTrader.Core.Services.Strategies.ISignalCatalog>();
        catalog.All.Returns(new List<AccessibleTrader.Sdk.Strategies.SignalDescriptor>());
        ctx.Services.AddSingleton(catalog);
        ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();
        return (ctx, orch, bus);
    }

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.AlertsModal> Open(TestContext ctx, IEventBus bus)
    {
        var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.AlertsModal>();
        cut.InvokeAsync(() => bus.Publish(new OpenAlertsEvent())).GetAwaiter().GetResult();
        return cut;
    }

    private static List<string> Options(IRenderedComponent<AccessibleTrader.BlazorClient.Components.AlertsModal> cut, string selectId) =>
        cut.FindAll($"select#{selectId} option").Select(o => o.GetAttribute("value") ?? "").ToList();

    // ── Conditions follow the target ─────────────────────────────────────────

    [Fact]
    public void Price_offers_cross_and_touch_and_never_a_zone()
    {
        var (ctx, _, bus) = BuildContext();
        var cut = Open(ctx, bus);

        var conditions = Options(cut, "alert-condition");
        Assert.Contains("CrossesAbove", conditions);
        Assert.Contains("CrossesBelow", conditions);
        Assert.Contains("Touches", conditions);
        Assert.DoesNotContain("EntersZone", conditions);
        Assert.DoesNotContain("ExitsZone", conditions);
    }

    [Fact]
    public void An_SMA_offers_no_zone_because_it_has_no_overbought_or_oversold_line()
    {
        var (ctx, _, bus) = BuildContext();
        var cut = Open(ctx, bus);
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForElement("select#alert-indicator").Change("sma-50");

        cut.WaitForAssertion(() =>
        {
            var conditions = Options(cut, "alert-condition");
            Assert.Contains("Touches", conditions);
            Assert.DoesNotContain("EntersZone", conditions);
            Assert.DoesNotContain("ExitsZone", conditions);
        });
    }

    [Fact]
    public void An_RSI_offers_its_own_zones_and_only_those()
    {
        // Vacuity check for the test above: a zone-bearing indicator DOES offer zones, so
        // "no zone for SMA" is a decision about SMA and not a form that lost zones entirely.
        var (ctx, _, bus) = BuildContext();
        var cut = Open(ctx, bus);
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForElement("select#alert-indicator").Change("rsi-1");
        cut.WaitForAssertion(() => Assert.Contains("EntersZone", Options(cut, "alert-condition")));

        cut.Find("select#alert-condition").Change(AlertCondition.EntersZone.ToString());
        cut.WaitForAssertion(() =>
            Assert.Equal(new[] { "Overbought", "Oversold" }, Options(cut, "alert-zone")));
    }

    // ── The value field starts at the current value ──────────────────────────

    [Fact]
    public void The_price_level_starts_at_the_last_close_not_zero()
    {
        var (ctx, _, bus) = BuildContext();
        var cut = Open(ctx, bus);

        Assert.Equal("64250.5", cut.Find("input#alert-threshold").GetAttribute("value"));
    }

    [Fact]
    public void Choosing_an_indicator_fills_in_its_latest_value()
    {
        var (ctx, _, bus) = BuildContext();
        var cut = Open(ctx, bus);
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForElement("select#alert-indicator").Change("sma-50");

        // The LIVE bar's value (63,000.75), not the cursor's (63,000 - 1 on bar 3).
        cut.WaitForAssertion(() =>
            Assert.Equal("63000.75", cut.Find("input#alert-threshold").GetAttribute("value")));
    }

    [Fact]
    public void A_typed_value_survives_every_change_and_a_reopen_until_an_alert_is_added_with_it()
    {
        // The review's finding 4: the user types 70, goes back up to correct the component, and
        // the 70 was silently replaced by the reading. A typed number is the user's; the hint
        // beside the field reports the current reading instead.
        var (ctx, orch, bus) = BuildContext();
        var cut = Open(ctx, bus);
        cut.Find("input#alert-threshold").Change("65000");
        cut.Find("select#alert-condition").Change(AlertCondition.Touches.ToString());
        cut.WaitForAssertion(() => Assert.Equal("65000", cut.Find("input#alert-threshold").GetAttribute("value")));

        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForElement("select#alert-indicator").Change("rsi-1");
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("65000", cut.Find("input#alert-threshold").GetAttribute("value"));
            Assert.Equal("Current value 61.25.", cut.Find("#alert-threshold-hint").TextContent.Trim());
        });

        cut.InvokeAsync(() => bus.Publish(new OpenAlertsEvent())).GetAwaiter().GetResult();   // reopen
        cut.WaitForAssertion(() => Assert.Equal("65000", cut.Find("input#alert-threshold").GetAttribute("value")));

        // Added with it: the next alert starts from the live reading again.
        cut.Find("input#alert-name").Change("Seventy");
        cut.Find("button[aria-label='Add alert']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(65000, Assert.Single(orch.Added).Threshold);
            Assert.Equal("61.25", cut.Find("input#alert-threshold").GetAttribute("value"));
        });
    }

    // ── A condition the new target cannot have is replaced, and said ─────────

    [Fact]
    public void Leaving_an_indicator_with_zones_for_one_without_resets_the_condition_and_says_so()
    {
        var (ctx, _, bus) = BuildContext();
        var spoken = new List<string>();
        bus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) spoken.Add(e.Message); });
        var cut = Open(ctx, bus);
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForElement("select#alert-indicator").Change("rsi-1");
        cut.WaitForAssertion(() => Assert.Contains("EntersZone", Options(cut, "alert-condition")));
        cut.Find("select#alert-condition").Change(AlertCondition.EntersZone.ToString());
        cut.WaitForElement("select#alert-zone");

        cut.Find("select#alert-indicator").Change("sma-50");

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("select#alert-zone"));
            // The select shows a condition that EXISTS for an SMA...
            Assert.Equal("CrossesAbove", cut.Find("select#alert-condition").GetAttribute("value"));
            // ...and the change was said, not made silently under the user.
            Assert.Contains(spoken, m => m.Contains("Condition changed to Crosses above", StringComparison.Ordinal));
        });
    }

    // ── A line instead of a number ───────────────────────────────────────────

    [Fact]
    public void Price_can_be_compared_with_any_line_on_the_price_pane_and_only_those()
    {
        var (ctx, _, bus) = BuildContext();
        var cut = Open(ctx, bus);
        cut.Find("select#alert-compare").Change("line");

        cut.WaitForAssertion(() =>
        {
            var lines = cut.FindAll("select#alert-line option").Select(o => o.TextContent.Trim()).ToList();
            Assert.Contains("SMA 20", lines);
            Assert.Contains("SMA 50", lines);
            Assert.Contains("Bollinger Bands UpperBand", lines);
            Assert.Contains("Bollinger Bands LowerBand", lines);
            Assert.DoesNotContain(lines, l => l.StartsWith("RSI", StringComparison.Ordinal));   // not in price
            // The number field is replaced, not left beside the line it no longer means.
            Assert.Empty(cut.FindAll("input#alert-threshold"));
        });
    }

    [Fact]
    public void Price_touches_SMA_50_is_added_against_that_instance_and_said_in_words()
    {
        var (ctx, orch, bus) = BuildContext();
        var spoken = new List<string>();
        bus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) spoken.Add(e.Message); });
        var cut = Open(ctx, bus);
        cut.Find("input#alert-name").Change("Fifty");
        cut.Find("select#alert-condition").Change(AlertCondition.Touches.ToString());
        cut.Find("select#alert-compare").Change("line");
        cut.WaitForElement("select#alert-line").Change("sma-50|Sma");
        cut.Find("button[aria-label='Add alert']").Click();

        cut.WaitForAssertion(() =>
        {
            var added = Assert.Single(orch.Added);
            Assert.Equal(AlertCondition.Touches, added.Condition);
            Assert.Equal("Sma", added.LineIndicatorCode);
            Assert.Equal("Sma", added.LineComponentName);
            Assert.Equal("sma-50", added.LineSeriesId);
            Assert.Null(added.Threshold);
            Assert.Contains(spoken, m => m == "Alert Fifty added: BTC/USD price touches SMA 50.");
        });
    }

    [Fact]
    public void A_numeric_alert_is_confirmed_with_its_level_in_words()
    {
        var (ctx, orch, bus) = BuildContext();
        var spoken = new List<string>();
        bus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) spoken.Add(e.Message); });
        var cut = Open(ctx, bus);
        cut.Find("input#alert-name").Change("Up");
        cut.Find("input#alert-threshold").Change("64250");
        cut.Find("button[aria-label='Add alert']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(orch.Added);
            Assert.Contains(spoken, m => m == "Alert Up added: BTC/USD price crosses above 64,250.");
        });
    }

    [Fact]
    public void An_indicator_can_be_compared_with_its_own_other_lines()
    {
        // MACD against its signal, a band against the centre line: the indicator's OTHER lines.
        var (ctx, _, bus) = BuildContext();
        var cut = Open(ctx, bus);
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForElement("select#alert-indicator").Change("bb-1");
        cut.WaitForElement("select#alert-compare").Change("line");

        cut.WaitForAssertion(() =>
        {
            var lines = cut.FindAll("select#alert-line option").Select(o => o.TextContent.Trim()).ToList();
            Assert.Equal(new[] { "Bollinger Bands Centerline", "Bollinger Bands LowerBand" }, lines);
        });
    }

    [Fact]
    public void Every_control_in_the_simple_form_has_a_label()
    {
        var (ctx, _, bus) = BuildContext();
        var cut = Open(ctx, bus);
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForElement("select#alert-indicator").Change("rsi-1");
        cut.Find("select#alert-condition").Change(AlertCondition.EntersZone.ToString());
        cut.WaitForElement("select#alert-zone");
        Assert.NotEmpty(cut.FindAll("label[for='alert-zone']"));
        cut.Find("select#alert-condition").Change(AlertCondition.Touches.ToString());
        cut.WaitForElement("input#alert-threshold");

        foreach (var id in new[] { "alert-target", "alert-indicator", "alert-component", "alert-condition", "alert-threshold" })
            Assert.NotEmpty(cut.FindAll($"label[for='{id}']"));
        cut.Find("select#alert-target").Change(AlertTarget.Price.ToString());
        cut.WaitForElement("select#alert-compare").Change("line");
        cut.WaitForElement("select#alert-line");
        foreach (var id in new[] { "alert-compare", "alert-line" })
            Assert.NotEmpty(cut.FindAll($"label[for='{id}']"));
    }

    // ── Accessibility review, AlertsModal findings 1-10 ──────────────────────

    /// <summary>Every StateChange the form published, with its Interrupt flag.</summary>
    private static List<(string Text, bool Interrupt)> Listen(IEventBus bus)
    {
        var heard = new List<(string, bool)>();
        bus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) heard.Add((e.Message, e.Interrupt)); });
        return heard;
    }

    [Fact]
    public void Arrowing_through_an_indicator_without_zones_does_not_lose_the_zone_condition()
    {
        // Finding 1. Chrome and Firefox commit every option an arrow passes in a CLOSED select.
        // RSI (zones) -> SMA 50 (none) -> RSI: the SMA had to show another condition, and the
        // user's "Enters zone" used to be gone for good when they arrived back on an RSI.
        var (ctx, _, bus) = BuildContext();
        var heard = Listen(bus);
        var cut = Open(ctx, bus);
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForElement("select#alert-indicator").Change("rsi-1");
        cut.WaitForAssertion(() => Assert.Contains("EntersZone", Options(cut, "alert-condition")));
        cut.Find("select#alert-condition").Change(AlertCondition.EntersZone.ToString());

        cut.Find("select#alert-indicator").Change("sma-50");
        cut.WaitForAssertion(() => Assert.Equal("CrossesAbove", cut.Find("select#alert-condition").GetAttribute("value")));
        cut.Find("select#alert-indicator").Change("rsi-1");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("EntersZone", cut.Find("select#alert-condition").GetAttribute("value"));
            Assert.Contains(heard, h => h.Text == "SMA 50. Condition changed to Crosses above.");
            Assert.Contains(heard, h => h.Text == "RSI. Condition back to Enters zone.");
        });
    }

    [Fact]
    public void Arrowing_through_a_one_line_indicator_does_not_lose_the_component()
    {
        var (ctx, _, bus) = BuildContext();
        var heard = Listen(bus);
        var cut = Open(ctx, bus);
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForElement("select#alert-indicator").Change("bb-1");
        cut.WaitForElement("select#alert-component").Change("LowerBand");

        cut.Find("select#alert-indicator").Change("sma-50");
        cut.Find("select#alert-indicator").Change("bb-1");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("LowerBand", cut.Find("select#alert-component").GetAttribute("value"));
            Assert.Contains(heard, h => h.Text == "Bollinger Bands. Component back to LowerBand.");
        });
    }

    [Fact]
    public void A_replaced_choice_is_said_without_interrupting_and_names_where_the_user_landed()
    {
        // Finding 2: the sentence used to cut off the screen reader's reading of the option.
        var (ctx, _, bus) = BuildContext();
        var heard = Listen(bus);
        var cut = Open(ctx, bus);
        cut.Find("select#alert-target").Change(AlertTarget.Candle.ToString());
        cut.Find("select#alert-condition").Change(AlertCondition.ChangesDirection.ToString());
        cut.Find("select#alert-target").Change(AlertTarget.Price.ToString());

        cut.WaitForAssertion(() =>
        {
            var said = Assert.Single(heard, h => h.Text.Contains("Condition changed", StringComparison.Ordinal));
            Assert.Equal("Price. Condition changed to Crosses above.", said.Text);
            Assert.False(said.Interrupt);
        });
    }

    [Fact]
    public void A_zone_and_a_line_that_had_to_change_are_said_and_come_back()
    {
        // Finding 3: every replacement is said, one sentence each.
        var (ctx, _, bus) = BuildContext();
        var heard = Listen(bus);
        var cut = Open(ctx, bus);
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForElement("select#alert-indicator").Change("rsi-1");
        cut.WaitForAssertion(() => Assert.Contains("EntersZone", Options(cut, "alert-condition")));
        cut.Find("select#alert-condition").Change(AlertCondition.EntersZone.ToString());
        cut.WaitForElement("select#alert-zone").Change(AlertZone.Oversold.ToString());

        cut.Find("select#alert-indicator").Change("rsi-7");   // has no oversold line
        cut.Find("select#alert-indicator").Change("rsi-1");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(heard, h => h.Text == "RSI 7. Zone changed to Overbought, 80 and above.");
            Assert.Contains(heard, h => h.Text == "RSI. Zone back to Oversold, 30 and below.");
            Assert.Equal("Oversold", cut.Find("select#alert-zone").GetAttribute("value"));
        });
    }

    [Fact]
    public void Compare_with_a_line_falls_back_to_a_number_out_loud_and_comes_back()
    {
        var (ctx, _, bus) = BuildContext();
        var heard = Listen(bus);
        var cut = Open(ctx, bus);
        cut.Find("select#alert-compare").Change("line");
        cut.WaitForElement("select#alert-line").Change("sma-50|Sma");

        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForElement("select#alert-indicator").Change("rsi-1");   // RSI has no other line
        cut.Find("select#alert-target").Change(AlertTarget.Price.ToString());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(heard, h => h.Text == "Indicator. Compare with changed to a number, because there is no line to compare with.");
            Assert.Contains(heard, h => h.Text == "Price. Compare with back to a line on the chart.");
            Assert.Equal("sma-50|Sma", cut.Find("select#alert-line").GetAttribute("value"));
        });
    }

    [Fact]
    public void Reopening_after_the_chosen_indicator_left_the_chart_says_so()
    {
        var (ctx, _, bus) = BuildContext(out var store);
        var heard = Listen(bus);
        var cut = Open(ctx, bus);
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForElement("select#alert-indicator").Change("sma-50");

        var before = store.State;
        store.State.Returns(before with { ActiveSeries = before.ActiveSeries.RemoveAll(s => s.Id == "sma-50") });
        cut.InvokeAsync(() => bus.Publish(new OpenAlertsEvent())).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(heard, h => h.Text.Contains("The indicator you chose, SMA 50, is no longer on the chart.", StringComparison.Ordinal));
            Assert.Equal("", cut.Find("select#alert-indicator").GetAttribute("value"));
        });
    }

    [Fact]
    public void Add_says_which_field_is_missing_before_it_is_pressed()
    {
        // Finding 5: on the button, in words that name the field.
        var (ctx, _, bus) = BuildContext();
        var cut = Open(ctx, bus);
        cut.Find("input#alert-name").Change("RSI watch");
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.WaitForAssertion(() =>
            GatedButtonAssert.IsRefusedBecause(cut, cut.Find("button[aria-label='Add alert']"), "Choose an indicator first."));

        cut.Find("select#alert-indicator").Change("rsi-1");
        cut.WaitForElement("select#alert-component").Change("");
        cut.WaitForAssertion(() =>
            GatedButtonAssert.IsRefusedBecause(cut, cut.Find("button[aria-label='Add alert']"), "Choose a component first."));
    }

    [Fact]
    public void An_empty_indicator_list_says_why_it_is_empty()
    {
        // Finding 6.
        var (ctx, _, bus) = BuildContext(out _, noIndicators: true);
        var cut = Open(ctx, bus);
        cut.Find("input#alert-name").Change("x");
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("(no indicators on this chart; add one first)", cut.Find("select#alert-indicator option").TextContent.Trim());
            GatedButtonAssert.IsRefusedBecause(cut, cut.Find("button[aria-label='Add alert']"), "Add an indicator to the chart first");
        });
    }

    [Fact]
    public void A_row_whose_line_left_the_chart_says_so_instead_of_naming_another_SMA()
    {
        // Finding 7: "price touches SMA 20" was read for an alert set on an SMA 50 since removed.
        var (ctx, orch, bus) = BuildContext();
        orch.Seed(new AlertDefinition
        {
            Id = "gone", Name = "Fifty", Delivery = AlertDelivery.Both, Target = AlertTarget.Price,
            Condition = AlertCondition.Touches, Symbol = "BTC/USD",
            LineIndicatorCode = "Sma", LineComponentName = "Sma", LineSeriesId = "sma-200",
        });
        var cut = Open(ctx, bus);

        var row = cut.Find("li[role='listitem']").TextContent;
        Assert.Contains("Fifty — BTC/USD price touches Sma (no longer on this chart)", row);
        Assert.DoesNotContain("SMA 20", row);
    }

    [Fact]
    public void A_sub_cent_price_is_filled_in_as_digits_not_exponent_notation()
    {
        // Finding 8: a bound double formats 0.00001234 as "1.234E-05".
        var (ctx, _, bus) = BuildContext(out _, lastClose: 0.00001234);
        var cut = Open(ctx, bus);

        Assert.Equal("0.00001234", cut.Find("input#alert-threshold").GetAttribute("value"));
    }

    [Fact]
    public void The_value_field_says_it_was_filled_in_with_the_current_reading()
    {
        // Finding 9.
        var (ctx, _, bus) = BuildContext();
        var cut = Open(ctx, bus);

        var input = cut.Find("input#alert-threshold");
        Assert.Equal("alert-threshold-hint", input.GetAttribute("aria-describedby"));
        Assert.Equal("Filled in with the current value, 64,250.5.", cut.Find("#alert-threshold-hint").TextContent.Trim());

        cut.Find("input#alert-threshold").Change("64000");
        cut.WaitForAssertion(() =>
            Assert.Equal("Current value 64,250.5.", cut.Find("#alert-threshold-hint").TextContent.Trim()));
    }

    [Fact]
    public void Touches_says_what_it_now_means_when_the_target_changes_its_meaning()
    {
        // Finding 10: the selected option's text changes with the target, which nothing reads.
        var (ctx, _, bus) = BuildContext();
        var heard = Listen(bus);
        var cut = Open(ctx, bus);
        cut.Find("select#alert-condition").Change(AlertCondition.Touches.ToString());
        cut.Find("select#alert-target").Change(AlertTarget.Indicator.ToString());
        cut.Find("select#alert-target").Change(AlertTarget.Candle.ToString());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(heard, h => h.Text == "Indicator. Touches now means reaching the level from either side.");
            Assert.Contains(heard, h => h.Text == "Candle. Touches now means the bar's range reaching the level, wick included.");
        });
    }
}
