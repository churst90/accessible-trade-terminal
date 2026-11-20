using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.ChangeLog;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Stage 5c: position profit and loss since you left. The away mark keeps the open positions;
/// the next Alt+U says how each moved since, once, after the header.
/// </summary>
[Collection(nameof(SpeechClockSettingsTests))]
public class ChangeLogAwayPositionsTests : IDisposable
{
    private static readonly DateTime Left = new(2026, 11, 18, 9, 40, 0, DateTimeKind.Utc);

    public ChangeLogAwayPositionsTests()
    {
        SpeechTimeFormatter.UseUtc = true;
        SpeechTimeFormatter.TwelveHour = false;
    }

    public void Dispose()
    {
        SpeechTimeFormatter.UseUtc = false;
        SpeechTimeFormatter.TwelveHour = false;
    }

    private static HeldPositionMark Mark(string symbol, double qty, double pnl, string provider = "MEXC")
        => new(provider, symbol, qty, pnl);

    // ── The words ──

    [Fact]
    public void EachPositionSaysWhatItMadeOrLost()
        => Assert.Equal("Positions since 09:40: BTC/USDT made 1,042.10, ETH/USDT lost 8.30, SOL/USDT unchanged.",
            AwayPositionsWording.Sentence(
                new[] { Mark("BTC/USDT", 0.1, 10), Mark("ETH/USDT", 2, 5), Mark("SOL/USDT", 10, 1) },
                new[] { Mark("BTC/USDT", 0.1, 1052.1), Mark("ETH/USDT", 2, -3.3), Mark("SOL/USDT", 10, 1.004) },
                Left));

    [Fact]
    public void OpenedClosedAndResizedAreSaidAsSuch()
        => Assert.Equal("Positions since 09:40: BTC/USDT size changed, XRP/USDT opened, ETH/USDT closed.",
            AwayPositionsWording.Sentence(
                new[] { Mark("BTC/USDT", 0.1, 10), Mark("ETH/USDT", 2, 5) },
                new[] { Mark("BTC/USDT", 0.2, 30), Mark("XRP/USDT", 100, 0) },
                Left));

    [Fact]
    public void TheSameSymbolOnAnotherVenueIsAnotherPosition()
        => Assert.Equal("Positions since 09:40: BTC/USDT on Kraken opened, BTC/USDT on MEXC closed.",
            AwayPositionsWording.Sentence(
                new[] { Mark("BTC/USDT", 0.1, 10, "MEXC") },
                new[] { Mark("BTC/USDT", 0.1, 10, "Kraken") },
                Left));

    [Fact]
    public void NothingHeldThenOrNowIsNoSentence()
    {
        Assert.Null(AwayPositionsWording.Sentence(Array.Empty<HeldPositionMark>(), Array.Empty<HeldPositionMark>(), Left));
        Assert.Null(AwayPositionsWording.Sentence(null, new[] { Mark("BTC/USDT", 1, 1) }, Left));
    }

    [Fact]
    public void ANowThatCouldNotBeReadIsSaidAndNeverCalledClosed()
    {
        Assert.Equal("Positions since 09:40 could not be read.",
            AwayPositionsWording.Sentence(new[] { Mark("BTC/USDT", 0.1, 10) }, null, Left));
        Assert.Null(AwayPositionsWording.Sentence(Array.Empty<HeldPositionMark>(), null, Left));
    }

    // ── Reading what is held ──

    private sealed class Venues
    {
        public readonly IPaperTradingProvider Paper = Substitute.For<IPaperTradingProvider>();
        public readonly IOrderExecutionService Orders = Substitute.For<IOrderExecutionService>();
        public readonly IApiKeyService Keys = Substitute.For<IApiKeyService>();
        public readonly ISettingsManager Settings = Substitute.For<ISettingsManager>();

        public Venues(bool paper, params ApiKeyConfig[] keys)
        {
            Settings.GetSetting(SettingsKeys.PaperTradingMode).Returns(JToken.FromObject(paper));
            Keys.GetAllKeysAsync().Returns(Task.FromResult(keys.ToList()));
            Orders.SupportsTradingAsync(Arg.Any<string>()).Returns(true);
        }

        public void Holds(string provider, params Position[] positions)
            => Orders.GetPositionsAsync(provider)
                .Returns(ProviderResult<List<Position>>.Ok(positions.ToList()));

        public Task<IReadOnlyList<HeldPositionMark>?> ReadAsync()
            => new HeldPositionsReader(Orders, Paper, Keys, Settings).ReadAsync();
    }

    private static ApiKeyConfig Key(string provider)
        => new(provider, "main", "k", "s", IsActive: true);

    [Fact]
    public async Task PaperModeReadsThePaperAccountAndSkipsEmptyRows()
    {
        var venues = new Venues(paper: true);
        venues.Paper.GetPositionsAsync().Returns(Task.FromResult(new List<Position>
        {
            new("BTC/USDT", 0.1, 90000, 9100, 100), new("ETH/USDT", 0, 3000, 0, 0),
        }));

        Assert.Equal(new[] { Mark("BTC/USDT", 0.1, 100, "Paper") }, await venues.ReadAsync());
        await venues.Orders.DidNotReceiveWithAnyArgs().GetPositionsAsync(default!);
    }

    [Fact]
    public async Task LiveReadsEveryActiveTradingKeyButNeverAWithdrawalKey()
    {
        var venues = new Venues(paper: false,
            Key("MEXC"), Key("Kraken"), Key("Gemini") with { AllowsWithdrawal = true },
            Key("Bitstamp") with { IsActive = false });
        venues.Holds("MEXC", new Position("BTC/USDT", 0.1, 90000, 9100, 100));
        venues.Holds("Kraken", new Position("SOL/USD", 10, 150, 1400, -100));

        Assert.Equal(new[] { Mark("BTC/USDT", 0.1, 100, "MEXC"), Mark("SOL/USD", 10, -100, "Kraken") },
            await venues.ReadAsync());
        await venues.Orders.DidNotReceive().GetPositionsAsync("Gemini");
        await venues.Orders.DidNotReceive().GetPositionsAsync("Bitstamp");
    }

    [Fact]
    public async Task OneVenueThatFailsMakesTheWholeReadUnknown()
    {
        // A partial list would call the failed venue's positions closed.
        var venues = new Venues(paper: false, Key("MEXC"), Key("Kraken"));
        venues.Holds("MEXC", new Position("BTC/USDT", 0.1, 90000, 9100, 100));
        venues.Orders.GetPositionsAsync("Kraken")
            .Returns(ProviderResult<List<Position>>.Unavailable("down"));

        Assert.Null(await venues.ReadAsync());
    }

    // ── Through the catch-up ──

    private sealed class FakeClock : TimeProvider
    {
        public DateTime UtcNow { get; set; } = Left;
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    private sealed class FakeReader : IHeldPositionsReader
    {
        public IReadOnlyList<HeldPositionMark>? Next = Array.Empty<HeldPositionMark>();
        public int Reads;
        public Task<IReadOnlyList<HeldPositionMark>?> ReadAsync() { Reads++; return Task.FromResult(Next); }
    }

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly SpyEventBus Bus = new();
        public readonly FakeReader Reader = new();
        public readonly ChangeLogStore Store;
        public readonly ChangeLogCatchUpService Service;

        public Rig()
        {
            Store = new ChangeLogStore(new TempPaths(TestTemp.NewDir("att-changelog-away-")),
                NullLogger<ChangeLogStore>.Instance, Clock);
            Service = new ChangeLogCatchUpService(Store, Bus, new MockWorkspaceStore(), Clock, positions: Reader);
        }

        public List<string> Said => Bus.Log.OfType<AnnouncementEvent>().Select(a => a.Message).ToList();

        public async Task LeaveAsync()
        {
            await Service.SetLastPresentAsync(Left);
            await Service.PositionsSnapshot;
        }
    }

    [Fact]
    public async Task LeavingKeepsThePositionsAndAltUSaysTheirMoveOnce()
    {
        var rig = new Rig();
        rig.Reader.Next = new[] { Mark("BTC/USDT", 0.1, 10) };
        await rig.LeaveAsync();
        var kept = (await rig.Store.SnapshotAsync()).AwayPositions;
        Assert.Equal(Left, kept!.TimeUtc);
        Assert.Equal(Mark("BTC/USDT", 0.1, 10), Assert.Single(kept.Positions));

        rig.Clock.UtcNow = Left.AddMinutes(47);
        rig.Reader.Next = new[] { Mark("BTC/USDT", 0.1, 52.1) };
        await rig.Store.AppendAsync(new ChangeLogEntry("a", Left.AddMinutes(5), ChangeLogCategory.Alert, "BTC crossed 95,000.", null, false));
        await rig.Service.SpeakAsync();

        Assert.Equal("1 change since 09:45, 42 minutes ago. Positions since 09:40: BTC/USDT made 42.10. "
            + "Alerts: 09:45, BTC crossed 95,000.", rig.Said[0]);

        // Said once: the next press has nothing of it.
        await rig.Service.SpeakAsync();
        Assert.DoesNotContain("Positions", rig.Said[1]);
        Assert.Null((await rig.Store.SnapshotAsync()).AwayPositions);
    }

    [Fact]
    public async Task WithNothingElseNewThePositionsStillSpeak()
    {
        var rig = new Rig();
        rig.Reader.Next = new[] { Mark("BTC/USDT", 0.1, 10) };
        await rig.LeaveAsync();

        rig.Clock.UtcNow = Left.AddMinutes(47);
        rig.Reader.Next = new[] { Mark("BTC/USDT", 0.1, 1.7) };
        await rig.Service.SpeakAsync();

        Assert.Equal("Nothing recorded yet. Positions since 09:40: BTC/USDT lost 8.30.", rig.Said[0]);
    }

    [Fact]
    public async Task APositionsReadThatFailsAtLeavingKeepsNothing()
    {
        var rig = new Rig();
        rig.Reader.Next = null;
        await rig.LeaveAsync();

        Assert.Null((await rig.Store.SnapshotAsync()).AwayPositions);
        Assert.Equal(Left, (await rig.Store.SnapshotAsync()).LastPresentUtc);
    }

    [Fact]
    public async Task ASecondLeaveWhoseReadFailsDropsTheFirstLeavesPositions()
    {
        // The positions said must be from the leave the "since" names, never an older one.
        var rig = new Rig();
        rig.Reader.Next = new[] { Mark("BTC/USDT", 0.1, 10) };
        await rig.LeaveAsync();
        rig.Reader.Next = null;
        await rig.Service.SetLastPresentAsync(Left.AddHours(2));
        await rig.Service.PositionsSnapshot;

        Assert.Null((await rig.Store.SnapshotAsync()).AwayPositions);
    }

    private sealed class SlowReader : IHeldPositionsReader
    {
        public readonly List<TaskCompletionSource<IReadOnlyList<HeldPositionMark>?>> Reads = new();
        public Task<IReadOnlyList<HeldPositionMark>?> ReadAsync()
        {
            var read = new TaskCompletionSource<IReadOnlyList<HeldPositionMark>?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Reads) Reads.Add(read);
            return read.Task;
        }
    }

    [Fact]
    public async Task ASlowReadThatFinishesAfterALaterLeaveNeverReplacesIt()
    {
        var reader = new SlowReader();
        var store = new ChangeLogStore(new TempPaths(TestTemp.NewDir("att-changelog-away-")),
            NullLogger<ChangeLogStore>.Instance, new FakeClock());
        var service = new ChangeLogCatchUpService(store, new SpyEventBus(), positions: reader);

        await service.SetLastPresentAsync(Left);
        var first = service.PositionsSnapshot;
        await service.SetLastPresentAsync(Left.AddHours(2));
        var second = service.PositionsSnapshot;
        for (int i = 0; i < 300 && reader.Reads.Count < 2; i++) await Task.Delay(10);

        reader.Reads[1].SetResult(new[] { Mark("ETH/USDT", 2, 5) });
        await second;
        reader.Reads[0].SetResult(new[] { Mark("BTC/USDT", 0.1, 10) });
        await first;

        var kept = (await store.SnapshotAsync()).AwayPositions!;
        Assert.Equal(Left.AddHours(2), kept.TimeUtc);
        Assert.Equal(Mark("ETH/USDT", 2, 5), Assert.Single(kept.Positions));
    }

    [Fact]
    public async Task WithoutAnAwayMarkAltUNeverReadsPositions()
    {
        var rig = new Rig();
        await rig.Service.SpeakAsync();
        Assert.Equal(0, rig.Reader.Reads);
        Assert.Equal("Nothing recorded yet.", rig.Said[0]);
    }

    [Fact]
    public async Task LeavingAgainReplacesTheMark()
    {
        var rig = new Rig();
        rig.Reader.Next = new[] { Mark("BTC/USDT", 0.1, 10) };
        await rig.LeaveAsync();
        rig.Reader.Next = new[] { Mark("BTC/USDT", 0.1, 30) };
        await rig.LeaveAsync();

        rig.Clock.UtcNow = Left.AddMinutes(47);
        rig.Reader.Next = new[] { Mark("BTC/USDT", 0.1, 35) };
        await rig.Service.SpeakAsync();
        Assert.EndsWith("BTC/USDT made 5.00.", rig.Said[0]);
    }
}
