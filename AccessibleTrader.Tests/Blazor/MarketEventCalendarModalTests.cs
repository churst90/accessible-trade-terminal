using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The Alt+E calendar in bUnit: day headings, the alert toggle and its setting write, the
/// history disclosure, Enter on an event that is not on the bars, and opening with the level
/// Off. No speech, no sound, no network — the harness's router is a substitute.
/// </summary>
public class MarketEventCalendarModalTests
{
    private static readonly DateTime Now = new(2026, 3, 11, 9, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTime now) => _now = new DateTimeOffset(
            DateTime.SpecifyKind(now, DateTimeKind.Utc));
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static MarketEvent Cpi(DateTime? moment = null, string id = "cpi-2026-03-11")
        => MarketEventTestData.Event(
            id: id, spoken: "CPI", longName: "Consumer Price Index",
            moment: moment ?? new DateTime(2026, 3, 11, 8, 30, 0, DateTimeKind.Utc),
            date: DateOnly.FromDateTime(moment ?? new DateTime(2026, 3, 11, 8, 30, 0, DateTimeKind.Utc)));

    private static IRenderedComponent<MarketEventCalendarModal> Open(
        BlazorTestHarness h, params MarketEvent[] events)
        => Open(h, Now, events);

    private static IRenderedComponent<MarketEventCalendarModal> Open(
        BlazorTestHarness h, DateTime now, params MarketEvent[] events)
    {
        h.With<TimeProvider>(new FixedClock(now));
        h.With(new MarketEventCalendar(events));
        return h.OpenModal<MarketEventCalendarModal>(
            bus => bus.Publish(new OpenMarketEventCalendarEvent()));
    }

    [Fact]
    public void TheDaysRenderAsHeadings()
    {
        using var h = new BlazorTestHarness();
        var cut = Open(h, Cpi());

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("[role='dialog']"));
            var heading = cut.FindAll("h3").Single(x => x.TextContent.Contains("March 2026"));
            Assert.Equal("Wednesday 11 March 2026", heading.TextContent.Trim());
        });
    }

    [Fact]
    public void TheAlertToggleFlipsAriaPressedAndWritesTheSetting()
    {
        using var h = new BlazorTestHarness();
        var cut = Open(h, Cpi());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(
            "[aria-label='Alert me before CPI, 11 March']")));

        cut.Find("[aria-label='Alert me before CPI, 11 March']").Click();

        cut.WaitForAssertion(() => Assert.Equal(
            "true", cut.Find("[aria-label='Alert me before CPI, 11 March']")
                .GetAttribute("aria-pressed")));

        h.WorkspaceStore.Received().Dispatch(Arg.Is<UpdateSettingsAction>(a =>
            a.Updater(WorkspaceState.Initial).MarketEventsWatched!.Contains("cpi-2026-03-11")));
    }

    [Fact]
    public void HistoryFlipsAriaExpandedAndShowsTheRegion()
    {
        using var h = new BlazorTestHarness();
        var earlier = Cpi(
            moment: new DateTime(2026, 2, 13, 8, 30, 0, DateTimeKind.Utc),
            id: "cpi-2026-02-13");
        var cut = Open(h, Cpi(), earlier);

        var button = cut.Find("[aria-label='History for CPI, 11 March']");
        Assert.Equal("false", button.GetAttribute("aria-expanded"));

        button.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("[aria-label='History for CPI, 11 March']")
                .GetAttribute("aria-expanded"));
            var region = cut.Find("#mec-history-cpi-2026-03-11");
            Assert.Contains("13 February 2026", region.TextContent);
        });
    }

    [Fact]
    public void AHistoryLineHasThePricePartWhenTheBarIsOnTheLoadedChart()
    {
        using var h = new BlazorTestHarness();
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 2, 13, 0, 0, 0, DateTimeKind.Utc),
                open: 100, close: 98.8),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc),
                open: 98.8, close: 100),
        };
        h.WorkspaceStore.State.Returns(MarketEventTestData.State(bars, timeframe: "1d"));

        var current = Cpi(
            moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc),
            id: "cpi-2024-03-12");
        var earlier = Cpi(
            moment: new DateTime(2024, 2, 13, 12, 30, 0, DateTimeKind.Utc),
            id: "cpi-2024-02-13");
        var cut = Open(h, new DateTime(2024, 3, 11, 9, 0, 0, DateTimeKind.Utc), current, earlier);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(
            "[aria-label='History for CPI, 12 March']")));
        cut.Find("[aria-label='History for CPI, 12 March']").Click();

        cut.WaitForAssertion(() =>
        {
            var region = cut.Find("#mec-history-cpi-2024-03-12");
            Assert.Contains("Price fell 1.2% over this bar.", region.TextContent);
        });
    }

    [Fact]
    public void EnterOnAnEventNotOnTheBarsAnnouncesItAndKeepsTheModalOpen()
    {
        using var h = new BlazorTestHarness();
        var cut = Open(h, Cpi());

        cut.WaitForAssertion(() => Assert.True(
            cut.FindAll("button").Any(b => b.TextContent.Contains("CPI, important")),
            string.Join(" || ", cut.FindAll("button").Select(b => b.TextContent.Trim()))));
        cut.FindAll("button").Single(b => b.TextContent.Contains("CPI, important")).Click();

        h.SpeechRouter.Received().Speak(
            "CPI on 11 March 2026 is not on this chart's bars.",
            Arg.Any<bool>(), Arg.Any<SpeechChannel>());
        Assert.NotEmpty(cut.FindAll("[role='dialog']"));
        Assert.Contains("Market events", cut.Markup);
    }

    [Fact]
    public void TheScreenOpensWithTheLevelOff()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(WorkspaceState.Initial with
        {
            MarketEventsLevel = "off",
        });

        var cut = Open(h, Cpi());

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("[role='dialog']"));
            Assert.Contains("CPI, important", cut.Markup);
        });
    }
}
