using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The journal's times follow the same clock choice (F12, Speech) as everything else. Entries
/// were stamped with this computer's local time and printed as-is, so the UTC setting never
/// reached the journal (code review, 2026-11-20). Shares the clock collection because it flips
/// the process-wide settings.
/// </summary>
[Collection(nameof(SpeechClockSettingsTests))]
public sealed class JournalClockTests : IDisposable
{
    private static readonly DateTime ThreeOhFourUtc = new(2026, 11, 20, 3, 4, 5, DateTimeKind.Utc);

    public JournalClockTests()
    {
        SpeechTimeFormatter.UseUtc = false;
        SpeechTimeFormatter.TwelveHour = false;
    }

    public void Dispose()
    {
        SpeechTimeFormatter.UseUtc = false;
        SpeechTimeFormatter.TwelveHour = false;
    }

    private static JournalEntry At(DateTime t) => new(t, JournalEntryKind.Info, "Test", null, "text");

    [Fact]
    public void Entries_from_the_bus_are_stamped_in_UTC()
    {
        var bus = new SpyEventBus();
        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(WorkspaceState.Initial);
        using var journal = new JournalService(bus, store);

        bus.Publish(new SetupEntryReachedEvent("Test Strat", "i1", OrderSide.Buy, 2440.25, 3));
        journal.AddSpeech("Hello");

        Assert.All(journal.Snapshot(), e => Assert.Equal(DateTimeKind.Utc, e.Timestamp.Kind));
    }

    [Fact]
    public void With_UTC_on_the_journal_shows_the_UTC_clock()
    {
        SpeechTimeFormatter.UseUtc = true;
        Assert.Equal("03:04:05", JournalService.ClockText(At(ThreeOhFourUtc)));
    }

    [Fact]
    public void On_a_twelve_hour_clock_the_journal_says_AM_or_PM()
    {
        SpeechTimeFormatter.UseUtc = true;
        SpeechTimeFormatter.TwelveHour = true;
        Assert.Equal("3:04:05 AM", JournalService.ClockText(At(ThreeOhFourUtc)));
    }

    [Fact]
    public void With_UTC_off_the_journal_shows_this_computers_clock()
    {
        Assert.Equal(ThreeOhFourUtc.ToLocalTime().ToString("HH:mm:ss"),
            JournalService.ClockText(At(ThreeOhFourUtc)));
    }
}
