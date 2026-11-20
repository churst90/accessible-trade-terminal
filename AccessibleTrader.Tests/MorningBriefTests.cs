using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.ChangeLog;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Core.Services.MarketPulse;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using static AccessibleTrader.Tests.MarketEventTestData;

namespace AccessibleTrader.Tests;

/// <summary>
/// The morning brief (Go bar BRIEF): what changed, the pulse, then today's market events, as one
/// sentence, with what changed marked heard only after it is said.
/// </summary>
[Collection(nameof(SpeechClockSettingsTests))]
public class MorningBriefTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 20, 7, 30, 0, DateTimeKind.Utc);

    public MorningBriefTests()
    {
        SpeechTimeFormatter.UseUtc = true;
        SpeechTimeFormatter.TwelveHour = false;
    }

    public void Dispose()
    {
        SpeechTimeFormatter.UseUtc = false;
        SpeechTimeFormatter.TwelveHour = false;
    }

    private sealed class FakeClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    private sealed class FakePulse : IMarketPulseReader
    {
        public string Said = "Pulse: BTC up 1.2%.";
        public Func<Task>? Before;
        public int Reads;

        public async Task<string> SentenceAsync(WorkspaceState state)
        {
            Reads++;
            if (Before != null) await Before();
            return Said;
        }
    }

    private sealed class Rig
    {
        public readonly SpyEventBus Bus = new();
        public readonly MockWorkspaceStore Workspace = new();
        public readonly FakePulse Pulse = new();
        public readonly ChangeLogStore Store;
        public readonly MorningBriefService Brief;

        public Rig(params MarketEvent[] events)
        {
            var clock = new FakeClock();
            Store = new ChangeLogStore(new TempPaths(TestTemp.NewDir("att-brief-")),
                NullLogger<ChangeLogStore>.Instance, clock);
            var catchUp = new ChangeLogCatchUpService(Store, Bus, Workspace, clock);
            Workspace.EmitState(State(new[] { Bar(Now.AddHours(-1)) }, level: MarketEventLevels.ImportantOnly));
            Brief = new MorningBriefService(catchUp, Pulse, Workspace, Bus, time: clock,
                calendar: Calendar(events));
        }

        public List<string> Said => Bus.Log.OfType<AnnouncementEvent>().Select(a => a.Message).ToList();

        public Task Add(string key, string text)
            => Store.AppendAsync(new ChangeLogEntry(key, Now.AddHours(-2), ChangeLogCategory.Alert, text, null, false));
    }

    private static MarketEvent Cpi(DateTime at, MarketEventImportance importance = MarketEventImportance.High,
        string id = "cpi", string name = "CPI", params MarketType[] scope)
        => Event(id: id, spoken: name, importance: importance, moment: at, scope: scope);

    [Fact]
    public async Task TheBriefSaysWhatChangedThePulseThenTodaysEvents()
    {
        var rig = new Rig(Cpi(Now.Date.AddHours(13).AddMinutes(30)));
        await rig.Add("a", "BTC crossed 95,000");

        await rig.Brief.SpeakAsync();

        Assert.Equal("Gathering the brief.", rig.Said[0]);
        Assert.Equal("1 change since 05:30, 2 hours ago. Alerts: 05:30, BTC crossed 95,000. "
            + "Pulse: BTC up 1.2%. Today: 13:30, CPI, important.", rig.Said[1]);
        Assert.Equal(2, rig.Said.Count);
    }

    [Fact]
    public async Task WhatChangedIsMarkedHeardOnlyOnceTheBriefIsSaid()
    {
        var rig = new Rig();
        await rig.Add("a", "BTC crossed 95,000");
        bool unheardWhilePulseGathered = false;
        rig.Pulse.Before = async () =>
            unheardWhilePulseGathered = (await rig.Store.SnapshotAsync()).Entries.Single().Heard == false;

        await rig.Brief.SpeakAsync();

        Assert.True(unheardWhilePulseGathered);
        Assert.True((await rig.Store.SnapshotAsync()).Entries.Single().Heard);
    }

    [Fact]
    public async Task APressWhileGatheringIsToldSoAndDoesNotGatherTwice()
    {
        var rig = new Rig();
        var release = new TaskCompletionSource();
        rig.Pulse.Before = () => release.Task;

        var first = rig.Brief.SpeakAsync();
        var second = rig.Brief.SpeakAsync();
        bool secondReturnedAtOnce = second.IsCompleted;
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.True(secondReturnedAtOnce, "the second press waited on the first gather");
        Assert.Contains("Still gathering the brief.", rig.Said);
        Assert.Equal(1, rig.Pulse.Reads);
    }

    [Fact]
    public async Task APulseThatThrowsIsSaidAsUnavailableAndTheRestStillSpeaks()
    {
        var rig = new Rig();
        rig.Pulse.Before = () => throw new InvalidOperationException();

        await rig.Brief.SpeakAsync();

        Assert.Equal("Nothing recorded yet. The market pulse is unavailable. No market events today.", rig.Said[^1]);
    }

    [Fact]
    public void OnlyTodayAtTheChosenLevelAndForOpenMarketsIsSaid()
    {
        var rig = new Rig(
            Cpi(Now.Date.AddHours(13), id: "today"),
            Cpi(Now.Date.AddDays(1).AddHours(1), id: "tomorrow"),
            Cpi(Now.Date.AddHours(-1), id: "yesterday"),
            Cpi(Now.Date.AddHours(14), MarketEventImportance.Medium, id: "medium"),
            Cpi(Now.Date.AddHours(15), id: "forex", scope: MarketType.Forex));

        var today = rig.Brief.TodaysEvents(rig.Workspace.State);

        Assert.Equal(new[] { "today" }, today.Select(e => e.Id));
    }

    [Fact]
    public void WithMarketEventsOffTheBriefStillSaysTheImportantOnes()
    {
        var rig = new Rig(
            Cpi(Now.Date.AddHours(13), id: "important"),
            Cpi(Now.Date.AddHours(14), MarketEventImportance.Medium, id: "medium"));
        rig.Workspace.EmitState(rig.Workspace.State with { MarketEventsLevel = MarketEventLevels.Off });

        Assert.Equal(new[] { "important" }, rig.Brief.TodaysEvents(rig.Workspace.State).Select(e => e.Id));
    }

    [Fact]
    public void AlreadyPastEventsOfTheDayAreStillToday()
    {
        var rig = new Rig(Cpi(Now.Date.AddHours(1), id: "early"));
        Assert.Single(rig.Brief.TodaysEvents(rig.Workspace.State));
    }

    [Fact]
    public void TodayIsTheSpokenClocksDay()
    {
        Assert.Equal((Now.Date, Now.Date.AddDays(1)), MorningBriefService.Today(Now));
        Assert.Equal(DateTimeKind.Utc, MorningBriefService.Today(Now).From.Kind);
    }

    [Fact]
    public void PastSixEventsTheRestAreCounted()
    {
        var events = Enumerable.Range(0, 8)
            .Select(i => Cpi(Now.Date.AddHours(9 + i), id: $"e{i}", name: $"E{i}"))
            .ToList();

        string said = MorningBriefService.EventsSentence(events, Now);

        Assert.StartsWith("Today: 09:00, E0, important. 10:00, E1, important.", said);
        Assert.Contains("14:00, E5, important.", said);
        Assert.DoesNotContain("E6", said);
        Assert.EndsWith(" 2 more in the calendar.", said);
    }
}
