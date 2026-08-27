using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Two users' money never meets on disk.</b>
///
/// <para>
/// <c>WorkspacePerUserIsolationTests</c> and <c>IndicatorPrefsPerUserIsolationTests</c> assert
/// this for the two pieces of user state that got it wrong; the paper account, which is the one
/// piece of user state denominated in money, had no equivalent. What it had instead was
/// <see cref="PaperAccountHub"/>'s object identity — <c>TwoUsers_NeverShare</c> in
/// <c>PaperAccountSharingTests</c> — which is a true and useful fact about the hub and says
/// nothing at all about the file. Two distinct account objects both writing
/// <c>paper_account.json</c> in one directory is exactly the last-writer-wins corruption the hub
/// exists to prevent, reintroduced one level down.
/// </para>
///
/// <para>
/// The routing itself is not something this class does. <c>PaperTradingProvider</c> takes
/// <see cref="IPlatformPathService"/> and joins <c>paper_account.json</c> onto whatever
/// <c>AppDataDirectory</c> it is handed; on the hosted head that is <c>users/{id}/</c>, resolved
/// per circuit. So the property worth asserting is the one that would survive a refactor: the
/// account writes UNDER the directory it was given, and nowhere else. Anything that reached
/// outside it — a hardcoded <c>LocalApplicationData</c>, a static path, a cached first-user value
/// — puts one trader's balance in front of another.
/// </para>
/// </summary>
public sealed class PaperAccountPerUserIsolationTests
{
    private static (PaperTradingProvider Account, MockWorkspaceStore Store, TempWorkspacePaths Paths) Account()
    {
        var paths = new TempWorkspacePaths();
        var store = new MockWorkspaceStore();
        var account = new PaperTradingProvider(store, paths,
            NullLogger<PaperTradingProvider>.Instance, new EventBus());
        return (account, store, paths);
    }

    private static WorkspaceState ChartAt(string symbol, double price) =>
        WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Venue", symbol, "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(new Ohlcv(DateTime.UtcNow, price, price, price, price, 1)),
        };

    private static TradeSignal Buy(string symbol, double qty)
        => new(Symbol: symbol, Side: OrderSide.Buy, Quantity: qty, Type: OrderType.Market);

    private static string StateFile(TempWorkspacePaths paths)
        => Path.Combine(paths.AppDataDirectory, "paper_account.json");

    // ── The file ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The account persists under the directory it was handed, at the name everything else in the
    /// codebase refers to. If this file ever moves, the two isolation assertions below become
    /// assertions about an empty directory and stop meaning anything.
    /// </summary>
    [Fact]
    public async Task TheAccountFileLivesUnderTheProvidedAppDataDirectory()
    {
        var (account, store, paths) = Account();
        store.EmitState(ChartAt("BTCUSDT", 60_000));

        await account.PlaceOrderAsync(Buy("BTCUSDT", 0.1));

        Assert.True(File.Exists(StateFile(paths)), $"expected the account at {StateFile(paths)}");
    }

    /// <summary>
    /// A trade by one user leaves the other user's file untouched, and the other user's ACCOUNT
    /// unable to see it. Both halves matter: an account that read the right file but wrote the
    /// wrong one would fail only the first, and one that shared a cache in memory would fail only
    /// the second.
    /// </summary>
    [Fact]
    public async Task OneUsersTradeIsInvisibleToAnother()
    {
        var (alice, aliceStore, alicePaths) = Account();
        var (bob, bobStore, bobPaths) = Account();

        aliceStore.EmitState(ChartAt("BTCUSDT", 60_000));
        bobStore.EmitState(ChartAt("BTCUSDT", 60_000));

        await alice.PlaceOrderAsync(Buy("BTCUSDT", 0.1));

        Assert.NotEmpty(await alice.GetPositionsAsync());
        Assert.Empty(await bob.GetPositionsAsync());
        Assert.False(File.Exists(StateFile(bobPaths)),
            "Bob's account file was written by a trade Bob did not make");
        Assert.True(File.Exists(StateFile(alicePaths)));
    }

    /// <summary>
    /// Balances do not leak either. Position lists are the visible symptom; the balance is the
    /// number a trader acts on, and an account that started from somebody else's cash would be
    /// wrong in the direction that costs money.
    /// </summary>
    [Fact]
    public async Task OneUsersBalanceIsNotAnothersStartingPoint()
    {
        var (alice, aliceStore, _) = Account();
        var (bob, bobStore, _) = Account();

        aliceStore.EmitState(ChartAt("BTCUSDT", 60_000));
        bobStore.EmitState(ChartAt("BTCUSDT", 60_000));

        double bobBefore = (await bob.GetBalancesAsync()).Sum(b => b.Free);
        await alice.PlaceOrderAsync(Buy("BTCUSDT", 0.5));   // 30,000 of Alice's cash
        double bobAfter = (await bob.GetBalancesAsync()).Sum(b => b.Free);

        Assert.Equal(bobBefore, bobAfter);
        Assert.True((await alice.GetBalancesAsync()).Sum(b => b.Free) < bobAfter,
            "Alice's own balance did not move, so this test could not tell the two apart");
    }

    /// <summary>
    /// A reload from disk brings back the user's OWN state and not the other's. This is the case a
    /// restart produces, and it is where a path resolved once and cached — the failure
    /// <c>ShortcutManager</c> and <c>SettingsManager</c> both had — would finally show.
    /// </summary>
    [Fact]
    public async Task EachUsersAccountReloadsItsOwnFile()
    {
        var alicePaths = new TempWorkspacePaths();
        var bobPaths = new TempWorkspacePaths();

        var aliceStore = new MockWorkspaceStore();
        aliceStore.EmitState(ChartAt("BTCUSDT", 60_000));
        var alice = new PaperTradingProvider(aliceStore, alicePaths,
            NullLogger<PaperTradingProvider>.Instance, new EventBus());
        await alice.PlaceOrderAsync(Buy("BTCUSDT", 0.25));

        var bobStore = new MockWorkspaceStore();
        bobStore.EmitState(ChartAt("ETHUSDT", 3_000));
        var bob = new PaperTradingProvider(bobStore, bobPaths,
            NullLogger<PaperTradingProvider>.Instance, new EventBus());
        await bob.PlaceOrderAsync(Buy("ETHUSDT", 2));

        // Restart both.
        var aliceAgain = new PaperTradingProvider(new MockWorkspaceStore(), alicePaths,
            NullLogger<PaperTradingProvider>.Instance, new EventBus());
        var bobAgain = new PaperTradingProvider(new MockWorkspaceStore(), bobPaths,
            NullLogger<PaperTradingProvider>.Instance, new EventBus());

        Assert.Contains(await aliceAgain.GetPositionsAsync(),
            p => p.Symbol.Equals("BTCUSDT", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(await aliceAgain.GetPositionsAsync(),
            p => p.Symbol.Equals("ETHUSDT", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(await bobAgain.GetPositionsAsync(),
            p => p.Symbol.Equals("ETHUSDT", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(await bobAgain.GetPositionsAsync(),
            p => p.Symbol.Equals("BTCUSDT", StringComparison.OrdinalIgnoreCase));
    }

    // ── Two scoped providers over ONE directory ─────────────────────────────────────

    /// <summary>
    /// The case the hub is supposed to make impossible, tested for what happens when it does not.
    ///
    /// <para>
    /// Two <c>PaperTradingProvider</c> instances over the same directory is what a second circuit
    /// resolving the broker outside the hub looks like — a DI misregistration, a background
    /// service, a prerender. Each holds its own in-memory ledger and each writes the whole file on
    /// every change, so the trades genuinely do diverge; that much is a property of the design and
    /// is why the hub exists.
    /// </para>
    ///
    /// <para>
    /// What must NOT happen is the third outcome: a half-written file. Both write through
    /// <c>AtomicFile</c>, so however the two interleave, whatever is on disk afterwards must be a
    /// complete, loadable account — one of the two, not a splice of both. A corrupt ledger is not
    /// a lost trade, it is a lost account.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TwoProvidersOverOneDirectoryNeverLeaveAHalfWrittenFile()
    {
        var paths = new TempWorkspacePaths();

        var storeA = new MockWorkspaceStore();
        var storeB = new MockWorkspaceStore();
        storeA.EmitState(ChartAt("BTCUSDT", 60_000));
        storeB.EmitState(ChartAt("BTCUSDT", 60_000));

        var a = new PaperTradingProvider(storeA, paths, NullLogger<PaperTradingProvider>.Instance, new EventBus());
        var b = new PaperTradingProvider(storeB, paths, NullLogger<PaperTradingProvider>.Instance, new EventBus());

        // Hammer both at once so their writes genuinely interleave.
        await Task.WhenAll(
            Task.Run(async () => { for (int i = 0; i < 40; i++) await a.PlaceOrderAsync(Buy("BTCUSDT", 0.01)); }),
            Task.Run(async () => { for (int i = 0; i < 40; i++) await b.PlaceOrderAsync(Buy("BTCUSDT", 0.01)); }));

        // Whatever won, the file has to be a whole account that loads without quarantine.
        var reloaded = new PaperTradingProvider(new MockWorkspaceStore(), paths,
            NullLogger<PaperTradingProvider>.Instance, new EventBus());

        var positions = await reloaded.GetPositionsAsync();
        Assert.Single(positions);
        Assert.True(positions[0].Quantity > 0,
            "the reloaded account has a position of zero size — the file was written mid-flight");

        // And the corrupt-file quarantine did not fire, which is what a torn write looks like.
        Assert.Empty(Directory.GetFiles(paths.AppDataDirectory, "paper_account.json.corrupt*"));
    }

    /// <summary>
    /// The vacuity half of the concurrency case: the two providers really were both writing.
    /// If one of them had silently failed to persist, the test above would be a single-writer
    /// test wearing a concurrency test's name.
    /// </summary>
    [Fact]
    public async Task BothProvidersInThatRaceActuallyWrote()
    {
        var paths = new TempWorkspacePaths();
        var storeA = new MockWorkspaceStore();
        var storeB = new MockWorkspaceStore();
        storeA.EmitState(ChartAt("BTCUSDT", 60_000));
        storeB.EmitState(ChartAt("BTCUSDT", 60_000));

        var a = new PaperTradingProvider(storeA, paths, NullLogger<PaperTradingProvider>.Instance, new EventBus());
        await a.PlaceOrderAsync(Buy("BTCUSDT", 0.3));
        string afterA = await File.ReadAllTextAsync(StateFile(paths));

        var b = new PaperTradingProvider(storeB, paths, NullLogger<PaperTradingProvider>.Instance, new EventBus());
        await b.PlaceOrderAsync(Buy("BTCUSDT", 0.7));
        string afterB = await File.ReadAllTextAsync(StateFile(paths));

        Assert.NotEqual(afterA, afterB);
    }
}
