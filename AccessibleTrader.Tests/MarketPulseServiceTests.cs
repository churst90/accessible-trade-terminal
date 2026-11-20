using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.MarketPulse;
using AccessibleTrader.Core.Services.Screening;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Screening;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The pulse service: seed the list once, read holdings without saving them, fetch under one
/// four-second limit, and say "Gathering the pulse." only when the sentence is late.
/// </summary>
public class MarketPulseServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);

    // ── Fakes ───────────────────────────────────────────────────────────────

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;
        private readonly List<FakeTimer> _timers = new();
        private readonly object _gate = new();

        public FakeClock(DateTime now) => _now = new DateTimeOffset(now);

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new FakeTimer(this, callback, state,
                _now + (dueTime == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : dueTime),
                period);
            lock (_gate) _timers.Add(timer);
            return timer;
        }

        public int LiveTimers
        {
            get { lock (_gate) return _timers.Count(t => !t.Disposed); }
        }

        public void Advance(TimeSpan by)
        {
            var target = _now + by;
            while (true)
            {
                FakeTimer? next;
                lock (_gate)
                    next = _timers
                        .Where(t => !t.Disposed && t.Due <= target)
                        .OrderBy(t => t.Due)
                        .FirstOrDefault();
                if (next == null) break;

                _now = next.Due;
                next.Fire();
            }
            _now = target;
        }

        private sealed class FakeTimer : ITimer
        {
            private readonly FakeClock _clock;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            public DateTimeOffset Due;
            public TimeSpan Period;
            public bool Disposed;

            public FakeTimer(FakeClock clock, TimerCallback callback, object? state,
                DateTimeOffset due, TimeSpan period)
            {
                _clock = clock;
                _callback = callback;
                _state = state;
                Due = due;
                Period = period;
            }

            public void Fire()
            {
                if (Disposed) return;
                if (Period > TimeSpan.Zero && Period != Timeout.InfiniteTimeSpan)
                    Due += Period;
                else
                    Disposed = true;
                _callback(_state);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Due = _clock.GetUtcNow() + dueTime;
                Period = period;
                Disposed = false;
                return true;
            }

            public void Dispose() => Disposed = true;

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FakeWatchlists : IWatchlistLibrary
    {
        public readonly List<Watchlist> Lists = new();
        public int Upserts;

        public IReadOnlyList<Watchlist> All => Lists;

        public Watchlist? GetById(string id) => Lists.FirstOrDefault(w => w.Id == id);

        public void Upsert(Watchlist list)
        {
            Upserts++;
            int index = Lists.FindIndex(w => w.Id == list.Id);
            if (index >= 0) Lists[index] = list;
            else Lists.Add(list);
        }

        public void Remove(string id) => Lists.RemoveAll(w => w.Id == id);

        public bool AddEntry(string watchlistId, WatchlistEntry entry)
        {
            var list = GetById(watchlistId);
            if (list == null || list.Entries.Any(e => e.Key == entry.Key)) return false;
            var entries = list.Entries.ToList();
            entries.Add(entry);
            Upsert(list with { Entries = entries });
            return true;
        }

        public bool RemoveEntry(string watchlistId, string entryKey)
        {
            var list = GetById(watchlistId);
            if (list == null) return false;
            var entries = list.Entries.ToList();
            if (entries.RemoveAll(e => e.Key == entryKey) == 0) return false;
            Upsert(list with { Entries = entries });
            return true;
        }

        public int MoveEntry(string watchlistId, string entryKey, int delta)
        {
            var list = GetById(watchlistId);
            if (list == null) return -1;
            var entries = list.Entries.ToList();
            int index = entries.FindIndex(e => e.Key == entryKey);
            if (index < 0) return -1;
            int target = Math.Clamp(index + delta, 0, entries.Count - 1);
            if (target == index) return index;
            var entry = entries[index];
            entries.RemoveAt(index);
            entries.Insert(target, entry);
            Upsert(list with { Entries = entries });
            return target;
        }

        public void Save() { }
        public void Reload() { }
    }

    private sealed class FakeData : IDataService
    {
        public readonly List<string> Providers = new();
        public readonly Dictionary<string, bool> Configured = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, bool> NeedsKey = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<MarketType>> Markets = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<string>> SubTypes = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<string>> Symbols = new(StringComparer.OrdinalIgnoreCase);
        public DateTime Now = new(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);
        public Func<string, MarketDataRequest, CancellationToken,
            Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)>>? Fetch;
        public int FetchCalls;
        public int InFlight;
        public int MaxInFlight;

        public Task InitializeAsync(IPluginLoaderService pluginLoader) => Task.CompletedTask;
        public void RegisterProvider(IMarketDataProvider provider) { }
        public Task ConfigureStoredKeyProvidersAsync() => Task.CompletedTask;
        public ApiKeyConfig? CredentialInUse(string providerName) => null;
        public Task<ApiKeyConfig?> ReconfigureProviderAsync(string providerName, string nickname)
            => Task.FromResult<ApiKeyConfig?>(null);
        public Task<List<string>> LoadAvailableMarketsAsync() => Task.FromResult(new List<string>());
        public Task<List<string>> LoadProvidersAsync() => Task.FromResult(Providers.ToList());
        public Task<List<string>> LoadProvidersByMarketTypeAsync(string marketType)
            => Task.FromResult(Providers.ToList());
        public Task<List<string>> GetSupportedSubTypesAsync(string provider, string marketType)
            => Task.FromResult(SubTypes.TryGetValue(provider, out var types)
                ? types.ToList()
                : new List<string> { "Spot" });
        public Task<List<string>> LoadSymbolsAsync(string marketInfo, string provider)
            => Task.FromResult(Symbols.TryGetValue($"{provider}|{marketInfo}", out var symbols)
                ? symbols.ToList()
                : new List<string>());
        public Task<List<string>> GetSupportedTimeframesAsync(string provider)
            => Task.FromResult(new List<string> { "1d", "1m", "1h" });
        public List<string> GetNativeTimeframes(string provider) => new() { "1d", "1m", "1h" };
        public Task<bool> IsProviderConfiguredAsync(string provider) => Task.FromResult(IsProviderConfigured(provider));
        public bool IsProviderConfigured(string provider)
            => !Configured.TryGetValue(provider, out bool configured) || configured;
        public Task<bool> ProviderRequiresApiKeyAsync(string provider)
            => Task.FromResult(NeedsKey.TryGetValue(provider, out bool needsKey) && needsKey);
        public Task<List<MarketType>> GetSupportedMarketsForProviderAsync(string provider)
            => Task.FromResult(Markets.TryGetValue(provider, out var markets)
                ? markets.ToList()
                : new List<MarketType>());
        public Task<IMarketDataProvider?> GetProviderAsync(string name)
            => Task.FromResult<IMarketDataProvider?>(null);
        public Task<IProviderPlugin?> GetPluginAsync(string name)
            => Task.FromResult<IProviderPlugin?>(null);

        public async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)>
            FetchOhlcvAsync(string provider, MarketDataRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref FetchCalls);
            int inFlight = Interlocked.Increment(ref InFlight);
            int max;
            while (inFlight > (max = Volatile.Read(ref MaxInFlight)))
                Interlocked.CompareExchange(ref MaxInFlight, inFlight, max);
            try
            {
                if (Fetch != null) return await Fetch(provider, request, ct).ConfigureAwait(false);
                return DefaultBars(request.Timeframe);
            }
            finally
            {
                Interlocked.Decrement(ref InFlight);
            }
        }

        public Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(
            string provider, string symbol, int limit = 10)
            => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        public (List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume) DefaultBars(string timeframe)
        {
            if (timeframe == "1m")
                return (new List<Ohlcv> { new(Now.AddMinutes(-1), 102, 103, 102, 103, 1) },
                        new List<(long, double)>());
            if (timeframe == "1h")
                return (new List<Ohlcv>
                {
                    new(Now.AddHours(-2), 99, 100, 99, 100, 1),
                    new(Now.AddHours(-1), 100, 101, 100, 101, 1),
                }, new List<(long, double)>());
            return (new List<Ohlcv>
            {
                new(Now.Date.AddDays(-1), 99, 100, 99, 100, 1),
                new(Now.Date, 101, 102, 101, 102, 1),
            }, new List<(long, double)>());
        }

        public void ResolveEverything()
        {
            Providers.Add("Fmp");
            Configured["Fmp"] = true;
            NeedsKey["Fmp"] = true;
            Markets["Fmp"] = new List<MarketType>
            {
                MarketType.Index, MarketType.Stock, MarketType.Crypto,
                MarketType.Forex, MarketType.Commodity,
            };
            SubTypes["Fmp"] = new List<string> { "Spot" };
            Symbols["Fmp|Index"] = new List<string> { "^GSPC", "^IXIC", "^TNX", "^DXY", "^VIX" };
            Symbols["Fmp|Stock"] = new List<string> { "SPY" };
            Symbols["Fmp|Crypto"] = new List<string> { "BTCUSD", "ETHUSD" };
            Symbols["Fmp|Forex"] = new List<string> { "XAUUSD" };
            Symbols["Fmp|Commodity"] = new List<string> { "GCUSD", "WTICOUSD" };
        }
    }

    private sealed class Rig
    {
        public readonly FakeClock Clock = new(Now);
        public readonly FakeData Data = new();
        public readonly FakeWatchlists Watchlists = new();
        public readonly SpyEventBus Bus = new();
        public readonly IPaperTradingProvider Paper = Substitute.For<IPaperTradingProvider>();
        public readonly IOrderExecutionService Orders = Substitute.For<IOrderExecutionService>();
        public readonly IApiKeyService Keys = Substitute.For<IApiKeyService>();
        public readonly ISettingsManager Settings = Substitute.For<ISettingsManager>();
        public readonly MarketPulseService Service;
        public readonly WorkspaceState State = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto|Spot", "MEXC", "BTC/USDT", "1h"),
            SelectedMarketType = MarketType.Crypto,
        };

        public Rig()
        {
            Data.Now = Now;
            Paper.GetPositionsAsync().Returns(Task.FromResult(new List<Position>()));
            Paper.GetOpenOrdersAsync(Arg.Any<string?>()).Returns(Task.FromResult(new List<OpenOrder>()));
            Paper.ExposedIdentities().Returns(Array.Empty<ChartIdentity>());
            Keys.GetAllKeysAsync().Returns(Task.FromResult(new List<ApiKeyConfig>()));
            Settings.GetSetting(SettingsKeys.PaperTradingMode).Returns(JToken.FromObject(true));

            Service = new MarketPulseService(
                Watchlists, Data, Orders, Paper, Keys, Settings, Bus,
                Clock, NullLogger<MarketPulseService>.Instance);
        }

        public List<AnnouncementEvent> Announcements =>
            Bus.Log.OfType<AnnouncementEvent>().ToList();

        public void SeedPulse(params WatchlistEntry[] entries)
            => Watchlists.Upsert(new Watchlist("pulse-id", "Pulse", entries));

        public void Position(string symbol, params ChartIdentity[] identities)
        {
            Paper.GetPositionsAsync().Returns(Task.FromResult(new List<Position>
            {
                new(symbol, 1, 100, 100, 0),
            }));
            Paper.ExposedIdentities().Returns(identities);
        }
    }

    // ── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFirstPressSeedsPulseOnceAndTheSecondDoesNotReseed()
    {
        var rig = new Rig();
        rig.Data.ResolveEverything();

        await rig.Service.SpeakAsync(rig.State);

        var pulse = Assert.Single(rig.Watchlists.All);
        Assert.Equal("Pulse", pulse.Name);
        Assert.Equal(9, pulse.Entries.Count);
        Assert.Equal(1, rig.Watchlists.Upserts);

        await rig.Service.SpeakAsync(rig.State);

        Assert.Equal(1, rig.Watchlists.Upserts);
        Assert.Equal(9, rig.Watchlists.All.Single().Entries.Count);
    }

    [Fact]
    public async Task AnExistingUserEditedPulseKeepsItsOrderAndContents()
    {
        var rig = new Rig();
        var gold = new WatchlistEntry("Fmp", "XAUUSD", MarketType.Commodity);
        var sp = new WatchlistEntry("Fmp", "^GSPC", MarketType.Index);
        rig.SeedPulse(gold, sp);

        await rig.Service.SpeakAsync(rig.State);

        var pulse = rig.Watchlists.All.Single();
        Assert.Equal(new[] { gold, sp }, pulse.Entries);

        string sentence = rig.Announcements[^1].Message;
        Assert.StartsWith("gold ", sentence);
        Assert.Contains("S&P ", sentence);
    }

    [Fact]
    public async Task TheReadingGoesToTheStripAndCtrlAltPReadsItAgainInDetail()
    {
        var rig = new Rig();
        rig.Service.SpeakDetail();
        Assert.Equal(MarketPulseService.NoReadingYet, rig.Announcements[^1].Message);

        rig.SeedPulse(
            new WatchlistEntry("Fmp", "XAUUSD", MarketType.Commodity),
            new WatchlistEntry("Fmp", "^GSPC", MarketType.Index));
        await rig.Service.SpeakAsync(rig.State);

        var strip = Assert.Single(rig.Bus.Log.OfType<TactileStripMessageEvent>());
        Assert.Equal("Gold +3.0, SPX +3.0", strip.Text);

        int fetches = rig.Data.FetchCalls;
        rig.Clock.Advance(TimeSpan.FromMinutes(3));
        rig.Service.SpeakDetail();

        Assert.Equal(fetches, rig.Data.FetchCalls);
        Assert.Equal(
            "Pulse read 3 minutes ago. "
          + "gold: XAUUSD on Fmp, 103.00, up 3.00% from the previous close 100.00, last minute bar ended 3 minutes ago. "
          + "S&P: ^GSPC on Fmp, 103.00, up 3.00% from the previous close 100.00, last minute bar ended 3 minutes ago.",
            rig.Announcements[^1].Message);
    }

    [Fact]
    public async Task AFetchThatNeverCompletesBecomesUnavailableAfterFourSecondsAndTheSentenceStillGoesOut()
    {
        var rig = new Rig();
        rig.SeedPulse(new WatchlistEntry("Fmp", "^GSPC", MarketType.Index));
        var never = new TaskCompletionSource<(List<Ohlcv>, List<(long, double)>)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Data.Fetch = (_, _, _) => never.Task;

        var press = rig.Service.SpeakAsync(rig.State);

        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("Gathering the pulse.", rig.Announcements[^1].Message);

        rig.Clock.Advance(TimeSpan.FromSeconds(3));
        await press;

        Assert.Equal("S&P unavailable.", rig.Announcements[^1].Message);

        int count = rig.Announcements.Count;
        never.SetResult((new List<Ohlcv>(), new List<(long, double)>()));
        await Task.Delay(30);
        rig.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(count, rig.Announcements.Count);
    }

    [Fact]
    public async Task NothingIsSpokenWhenTheFetchBeatsTheGatherTimer()
    {
        var rig = new Rig();
        rig.SeedPulse(new WatchlistEntry("Fmp", "^GSPC", MarketType.Index));

        await rig.Service.SpeakAsync(rig.State);

        Assert.DoesNotContain("Gathering the pulse.", rig.Announcements.Select(a => a.Message));
        Assert.Single(rig.Announcements);
    }

    [Fact]
    public async Task ASecondPressWhileGatheringSaysStillGatheringAndMakesNoExtraFetch()
    {
        var rig = new Rig();
        rig.SeedPulse(new WatchlistEntry("Fmp", "^GSPC", MarketType.Index));
        var never = new TaskCompletionSource<(List<Ohlcv>, List<(long, double)>)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Data.Fetch = (_, _, _) => never.Task;

        var first = rig.Service.SpeakAsync(rig.State);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        int fetches = rig.Data.FetchCalls;

        await rig.Service.SpeakAsync(rig.State);

        Assert.Equal("Still gathering the pulse.", rig.Announcements[^1].Message);
        Assert.Equal(fetches, rig.Data.FetchCalls);

        rig.Clock.Advance(TimeSpan.FromSeconds(3));
        never.SetResult((new List<Ohlcv>(), new List<(long, double)>()));
        await first;
    }

    [Fact]
    public async Task EveryPulseMessageUsesTheInterfaceChannelWithTheRightInterrupt()
    {
        var rig = new Rig();
        rig.SeedPulse(new WatchlistEntry("Fmp", "^GSPC", MarketType.Index));
        var never = new TaskCompletionSource<(List<Ohlcv>, List<(long, double)>)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Data.Fetch = (_, _, _) => never.Task;

        var press = rig.Service.SpeakAsync(rig.State);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        rig.Clock.Advance(TimeSpan.FromSeconds(3));
        await press;

        Assert.All(rig.Announcements, a => Assert.Equal(SpeechChannel.Interface, a.Channel));
        Assert.False(rig.Announcements.Single(a => a.Message == "Gathering the pulse.").Interrupt);
        Assert.True(rig.Announcements[^1].Interrupt);
    }

    [Fact]
    public async Task HeldPositionsAppendAfterTheListAndAreNotSavedIntoIt()
    {
        var rig = new Rig();
        var btc = new WatchlistEntry("Coinbase", "BTC/USD", MarketType.Crypto);
        rig.SeedPulse(btc);
        rig.Position("SOL/USDT", new ChartIdentity("Crypto|Spot", "MEXC", "SOL/USDT", "1h"));

        await rig.Service.SpeakAsync(rig.State);

        Assert.Equal(new[] { btc }, rig.Watchlists.All.Single().Entries);
        Assert.Contains("SOL", rig.Announcements[^1].Message);
    }

    [Fact]
    public async Task TheLeftOutSentenceComesOnlyOnTheSeedingPress()
    {
        var rig = new Rig();
        rig.Data.Providers.Add("Fmp");
        rig.Data.Configured["Fmp"] = true;
        rig.Data.NeedsKey["Fmp"] = true;
        rig.Data.Markets["Fmp"] = new List<MarketType> { MarketType.Index };
        rig.Data.SubTypes["Fmp"] = new List<string> { "Spot" };
        rig.Data.Symbols["Fmp|Index"] = new List<string> { "^GSPC" };

        await rig.Service.SpeakAsync(rig.State);

        string first = rig.Announcements[^1].Message;
        Assert.Contains("Left out, no data source:", first);
        Assert.Contains("VIX", first);
        Assert.EndsWith("Add them in the watchlist screen, Alt+M.", first);

        await rig.Service.SpeakAsync(rig.State);

        Assert.DoesNotContain("Left out", rig.Announcements[^1].Message);
    }

    [Fact]
    public async Task NoMoreThanFourRequestsAreInFlightPerProvider()
    {
        var rig = new Rig();
        rig.SeedPulse(
            new WatchlistEntry("Fmp", "^GSPC", MarketType.Index),
            new WatchlistEntry("Fmp", "^IXIC", MarketType.Index),
            new WatchlistEntry("Fmp", "^VIX", MarketType.Index),
            new WatchlistEntry("Fmp", "^TNX", MarketType.Index),
            new WatchlistEntry("Fmp", "^DXY", MarketType.Index),
            new WatchlistEntry("Fmp", "SPY", MarketType.Stock));
        rig.Data.Fetch = async (_, request, _) =>
        {
            await Task.Delay(30);
            return rig.Data.DefaultBars(request.Timeframe);
        };

        await rig.Service.SpeakAsync(rig.State);

        Assert.True(rig.Data.MaxInFlight <= 4,
            $"max requests in flight was {rig.Data.MaxInFlight}");
        Assert.Equal(12, rig.Data.FetchCalls);
    }

    [Fact]
    public void AltPMapsToMarketPulseAndNoOtherBindingUsesPlainAltP()
    {
        var paths = new TempPaths(TestTemp.NewDir("att-pulse-keys-"));
        var manager = new ShortcutManager(paths);

        Assert.Equal(SystemCommand.MarketPulse,
            manager.GetCommand("P", shift: false, ctrl: false, alt: true));

        var others = manager.CurrentProfile.Shortcuts
            .Where(s => s.Key.Equals("P", StringComparison.OrdinalIgnoreCase)
                        && s.Alt && !s.Ctrl && !s.Shift
                        && s.Command != SystemCommand.MarketPulse)
            .ToList();
        Assert.Empty(others);
    }

    [Fact]
    public void TheCryptoStartSettingDefaultsToMidnightOnTheStateAndTheSettingsFile()
    {
        Assert.Equal("midnightUtc", WorkspaceState.Initial.MarketPulseCryptoStart);

        var app = new AppSettings(new DictSettings());
        Assert.Equal("midnightUtc", app.MarketPulseCryptoStart);
    }

    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    private sealed class DictSettings : ISettingsManager
    {
        public readonly Dictionary<string, JToken> Store = new(StringComparer.Ordinal);
        public JToken? GetSetting(string keyPath, JToken? defaultValue = null)
            => Store.TryGetValue(keyPath, out var v) ? v : defaultValue;
        public void SetSetting(string keyPath, JToken value) => Store[keyPath] = value;
        public JObject GetEffectiveSettingsForSeries(string seriesId) => new();
        public void SaveSettings() { }
        public void ResetToDefaults() { }
        public void Reload() { }
    }

    // ── Review fixes (Claude): a slow broker and a provider without minute bars ──

    [Fact]
    public async Task AHoldingsReadThatNeverAnswersIsLeftOutAfterTwoSeconds()
    {
        var rig = new Rig();
        rig.SeedPulse(new WatchlistEntry("Fmp", "^GSPC", MarketType.Index));
        rig.Paper.GetPositionsAsync().Returns(new TaskCompletionSource<List<Position>>().Task);

        var press = rig.Service.SpeakAsync(rig.State);
        // The press may reach the holdings read on another thread; its limit's timer must
        // exist (beside the gather timer) before the clock moves.
        for (int i = 0; i < 250 && rig.Clock.LiveTimers < 2; i++) await Task.Delay(20);
        Assert.Equal(2, rig.Clock.LiveTimers);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("Gathering the pulse.", rig.Announcements[^1].Message);
        rig.Clock.Advance(MarketPulseService.HoldingsLimit);

        Assert.Same(press, await Task.WhenAny(press, Task.Delay(5000)));
        Assert.StartsWith("S&P up", rig.Announcements[^1].Message);
    }

    [Fact]
    public async Task AFailedMinuteFetchLeavesTheItemOnItsDailyBars()
    {
        var rig = new Rig();
        rig.SeedPulse(new WatchlistEntry("Fmp", "^GSPC", MarketType.Index));
        rig.Data.Fetch = (_, request, _) => request.Timeframe == "1m"
            ? throw new InvalidOperationException("no one-minute bars")
            : Task.FromResult(rig.Data.DefaultBars(request.Timeframe));

        await rig.Service.SpeakAsync(rig.State);

        string sentence = rig.Announcements[^1].Message;
        Assert.StartsWith("S&P up", sentence);
        Assert.DoesNotContain("unavailable", sentence);
    }
}
