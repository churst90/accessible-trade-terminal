using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.ChangeLog;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests;

[Collection(nameof(SpeechClockSettingsTests))]
public class ChangeLogCatchUpServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 18, 10, 27, 0, DateTimeKind.Utc);

    public ChangeLogCatchUpServiceTests()
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
        public DateTime UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
        public void Advance(TimeSpan by) => UtcNow += by;
    }

    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly SpyEventBus Bus = new();
        public readonly MockWorkspaceStore Workspace = new();
        public readonly string Dir = TestTemp.NewDir("att-changelog-catchup-");
        public readonly ChangeLogStore Store;
        public readonly ChangeLogCatchUpService Service;

        public Rig()
        {
            Store = new ChangeLogStore(
                new TempPaths(Dir), NullLogger<ChangeLogStore>.Instance, Clock);
            Service = new ChangeLogCatchUpService(Store, Bus, Workspace, Clock);
        }

        public List<AnnouncementEvent> Announcements =>
            Bus.Log.OfType<AnnouncementEvent>().ToList();

        public async Task Add(
            string key, ChangeLogCategory category, DateTime time, string text = "something happened.")
            => await Store.AppendAsync(new ChangeLogEntry(key, time, category, text, null, false));
    }

    private static DateTime At(int hour, int minute)
        => new(2026, 11, 18, hour, minute, 0, DateTimeKind.Utc);

    private static async Task WaitForAnnouncementAsync(Rig rig)
    {
        for (int i = 0; i < 300 && rig.Announcements.Count == 0; i++)
            await Task.Delay(10);
        Assert.NotEmpty(rig.Announcements);
    }

    [Fact]
    public async Task TheFirstPressReadsFiveMarksExactlyThoseAndSpeaksOnTheInterfaceTier()
    {
        var rig = new Rig();
        await rig.Add("m1", ChangeLogCategory.Money, At(9, 50), "BTC/USDT filled.");
        await rig.Add("m2", ChangeLogCategory.Money, At(9, 51), "ETH/USDT filled.");
        await rig.Add("a1", ChangeLogCategory.Alert, At(9, 52), "BTC/USDT crossed above 95,000.");
        await rig.Add("a2", ChangeLogCategory.Alert, At(9, 53), "ETH/USDT RSI above 70.");
        await rig.Add("a3", ChangeLogCategory.Alert, At(9, 54), "SOL/USDT crossed above 200.");
        await rig.Add("e1", ChangeLogCategory.Event, At(9, 55), "CPI out, up 0.3.");
        await rig.Add("c1", ChangeLogCategory.Connection, At(9, 56), "MEXC disconnected.");

        await rig.Service.SpeakAsync();

        var announcement = Assert.Single(rig.Announcements);
        Assert.Equal(SpeechChannel.Interface, announcement.Channel);
        Assert.True(announcement.Interrupt);
        Assert.StartsWith("7 changes since 09:50, 37 minutes ago.", announcement.Message);
        Assert.Contains("Press Alt+U again for 2 more.", announcement.Message);

        var doc = await rig.Store.SnapshotAsync();
        Assert.Equal(new[] { "m1", "m2", "a1", "a2", "a3" },
            doc.Entries.Where(e => e.Heard).Select(e => e.Key));
        Assert.Equal(new[] { "e1", "c1" },
            doc.Entries.Where(e => !e.Heard).Select(e => e.Key));
        Assert.Equal(Now, doc.LastCatchUpUtc);
    }

    [Fact]
    public async Task TheSecondPressReadsTheRestAndStartsWithHowManyMore()
    {
        var rig = new Rig();
        await rig.Add("m1", ChangeLogCategory.Money, At(9, 50), "BTC/USDT filled.");
        await rig.Add("m2", ChangeLogCategory.Money, At(9, 51), "ETH/USDT filled.");
        await rig.Add("a1", ChangeLogCategory.Alert, At(9, 52), "BTC/USDT crossed above 95,000.");
        await rig.Add("a2", ChangeLogCategory.Alert, At(9, 53), "ETH/USDT RSI above 70.");
        await rig.Add("a3", ChangeLogCategory.Alert, At(9, 54), "SOL/USDT crossed above 200.");
        await rig.Add("e1", ChangeLogCategory.Event, At(9, 55), "CPI out, up 0.3.");

        await rig.Service.SpeakAsync();
        await rig.Service.SpeakAsync();

        Assert.Equal(2, rig.Announcements.Count);
        Assert.StartsWith("1 more.", rig.Announcements[1].Message);
        Assert.Contains("Events: 09:55, CPI out, up 0.3.", rig.Announcements[1].Message);

        var doc = await rig.Store.SnapshotAsync();
        Assert.All(doc.Entries, e => Assert.True(e.Heard));
    }

    [Fact]
    public async Task AnItemArrivingBetweenPressesMakesTheNextPressAFreshHeader()
    {
        var rig = new Rig();
        for (int i = 1; i <= 6; i++)
            await rig.Add($"k{i}", ChangeLogCategory.Money, At(9, 50 + i), $"fill {i}.");
        await rig.Service.SpeakAsync();

        rig.Clock.Advance(TimeSpan.FromMinutes(3));
        await rig.Add("late", ChangeLogCategory.Alert, At(10, 29), "BTC/USDT crossed above 95,000.");

        await rig.Service.SpeakAsync();

        string second = rig.Announcements[1].Message;
        // The leftover 09:56 fill is one of the two, so "since" is its time, not the 10:27
        // press (review fix: "since 10:27" was false about an item it then read).
        Assert.StartsWith("2 changes since 09:56, 34 minutes ago.", second);
        Assert.DoesNotContain("2 more.", second);
    }

    [Fact]
    public async Task TheReturnPromptNeedsFiveMinutesTheSettingAndSomethingUnheardSinceYouLeft()
    {
        var rig = new Rig();
        await rig.Store.SetLastPresentAsync(At(9, 40));
        await rig.Add("m1", ChangeLogCategory.Money, At(9, 50), "BTC/USDT filled.");

        await rig.Service.OfferOnReturnAsync(Now);

        var prompt = Assert.Single(rig.Announcements);
        Assert.Equal(
            "You were away 47 minutes, 1 thing changed, Alt+U to hear them.",
            prompt.Message);
        Assert.False(prompt.Interrupt);
        Assert.Equal(SpeechChannel.Interface, prompt.Channel);

        var doc = await rig.Store.SnapshotAsync();
        Assert.False(Assert.Single(doc.Entries).Heard);
        Assert.Null(doc.LastCatchUpUtc);
    }

    [Fact]
    public async Task NoReturnPromptWhenTheAwayTimeIsShortOrTheSettingIsOff()
    {
        var shortRig = new Rig();
        await shortRig.Store.SetLastPresentAsync(At(10, 23));
        await shortRig.Add("m1", ChangeLogCategory.Money, At(10, 24));
        await shortRig.Service.OfferOnReturnAsync(Now);
        Assert.Empty(shortRig.Announcements);

        var offRig = new Rig();
        offRig.Workspace.EmitState(WorkspaceState.Initial with { ChangeLogOfferOnReturn = false });
        await offRig.Store.SetLastPresentAsync(At(9, 40));
        await offRig.Add("m1", ChangeLogCategory.Money, At(9, 50));
        await offRig.Service.OfferOnReturnAsync(Now);
        Assert.Empty(offRig.Announcements);
    }

    [Fact]
    public async Task TheSettingIsReadFromTheSettingsFileOnTheFirstOfferOfASession()
    {
        var rig = new Rig();
        var settings = new AppSettings(new DictSettings()) { ChangeLogOfferOnReturn = false };
        var service = new ChangeLogCatchUpService(
            rig.Store, rig.Bus, workspace: null, rig.Clock, settings);
        await rig.Store.SetLastPresentAsync(At(9, 40));
        await rig.Add("m1", ChangeLogCategory.Money, At(9, 50));

        await service.OfferOnReturnAsync(Now);

        Assert.Empty(rig.Announcements);
    }

    [Fact]
    public async Task NoReturnPromptWhenTheOnlyUnheardItemPredatesTheLastPresent()
    {
        var rig = new Rig();
        await rig.Add("old", ChangeLogCategory.Money, At(9, 30));
        await rig.Store.SetLastPresentAsync(At(9, 40));

        await rig.Service.OfferOnReturnAsync(Now);

        Assert.Empty(rig.Announcements);
    }

    [Fact]
    public void AltUMapsToWhatChangedAndNoOtherBindingUsesPlainAltU()
    {
        var dir = TestTemp.NewDir("att-changelog-keys-");
        var manager = new ShortcutManager(new TempPaths(dir));

        Assert.Equal(SystemCommand.WhatChanged,
            manager.GetCommand("U", shift: false, ctrl: false, alt: true));

        var others = manager.CurrentProfile.Shortcuts
            .Where(s => s.Key.Equals("U", StringComparison.OrdinalIgnoreCase)
                        && s.Alt && !s.Ctrl && !s.Shift
                        && s.Command != SystemCommand.WhatChanged)
            .ToList();
        Assert.Empty(others);
    }

    private sealed class DictSettings : ISettingsManager
    {
        public readonly Dictionary<string, JToken> Store = new(StringComparer.Ordinal);
        public JToken? GetSetting(string keyPath, JToken? defaultValue = null)
            => Store.TryGetValue(keyPath, out var value) ? value : defaultValue;
        public void SetSetting(string keyPath, JToken value) => Store[keyPath] = value;
        public JObject GetEffectiveSettingsForSeries(string seriesId) => new();
        public void SaveSettings() { }
        public void ResetToDefaults() { }
        public void Reload() { }
    }

    [Fact]
    public async Task TheDispatcherRunsAWhatChangedPress()
    {
        var rig = new Rig();
        await rig.Add("m1", ChangeLogCategory.Money, At(10, 26), "BTC/USDT filled.");
        var store = new MockWorkspaceStore();
        var crossing = new IndicatorCrossingEngine(store, rig.Bus);
        using var dispatcher = new CommandDispatcher(
            rig.Bus,
            Substitute.For<INavigationEngine>(),
            store,
            Substitute.For<IBarDetailService>(),
            crossing,
            changeLog: rig.Service);

        dispatcher.Dispatch(SystemCommand.WhatChanged);

        await WaitForAnnouncementAsync(rig);
        Assert.Contains("BTC/USDT filled.", rig.Announcements[0].Message);
    }
}
