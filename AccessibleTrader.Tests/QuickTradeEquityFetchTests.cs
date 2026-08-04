using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Arming must be able to ASK for an account balance, not just complain that none is cached.
///
/// <para>
/// <b>The defect.</b> <c>QuickTradeEquity</c> is a cached number published by whatever last read a
/// balance, and the only thing that ever read one was the trading dashboard. Tick paper trading, go
/// straight to the chart, press the arm key: "No account equity available… connect a trading
/// provider first" — with a provider connected and funded. The advice was wrong and the feature was
/// unreachable for anyone who had not happened to open an unrelated panel first.
/// </para>
///
/// <para>
/// The existing quick-trade tests could not see it because they inject the equity delegate directly,
/// so they were always testing the case where a balance had already arrived.
/// </para>
/// </summary>
public class QuickTradeEquityFetchTests
{
    /// <summary>
    /// The same fixture the other quick-trade tests use, so the only thing differing here is
    /// whether an equity value has been cached yet.
    /// </summary>
    private static (QuickTradeService Svc, SpyEventBus Bus) Build(
        Func<double> equitySource, Func<Task>? equityRefresh)
    {
        var bars = Enumerable.Range(0, 50).Select(i => new Ohlcv
        {
            Date = new DateTime(2024, 1, 1).AddDays(i),
            Open = 100, High = 105, Low = 95, Close = 100, Volume = 1
        }).ToArray();

        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = 25,
            Identity = new ChartIdentity("Crypto", "MEXC", "BTCUSDT", "4h"),
        });

        var bus = new SpyEventBus();
        return (new QuickTradeService(store, bus, equitySource, equityRefresh), bus);
    }

    /// <summary>
    /// A defensive copy of what has been spoken.
    ///
    /// <para>
    /// The whole point of this feature is that the balance is fetched OFF the keystroke path, so the
    /// result is published from a background thread — while <c>SpyEventBus.Log</c> is a plain
    /// <c>List</c>. Enumerating it directly can observe a concurrent Add and throw. This passed in
    /// isolation and failed in the full parallel run, which is the signature of exactly that.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> Spoken(SpyEventBus bus)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return bus.Log.ToArray()
                    .OfType<FeedbackRequestEvent>()
                    .Select(e => e.Message)
                    .Where(m => !string.IsNullOrEmpty(m))
                    .ToList()!;
            }
            catch (InvalidOperationException) when (attempt < 50)
            {
                // Raced with a publish; take the snapshot again.
            }
        }
    }

    /// <summary>The exact reported case: a balance exists, it just has not been read yet.</summary>
    [Fact]
    public async Task ArmingFetchesTheBalanceWhenNoneIsCachedAndThenArms()
    {
        double equity = 0;
        bool fetched = false;
        var (svc, _) = Build(() => equity,
            () => { fetched = true; equity = 25_000; return Task.CompletedTask; });

        svc.Arm(1.0);

        // The fetch is deliberately off the keystroke path, so give it a moment to land.
        for (int i = 0; i < 100 && svc.State.Stage == QuickTradeStage.Idle; i++) await Task.Delay(10);

        Assert.True(fetched, "Arming must ask for a balance rather than refusing outright.");
        Assert.Equal(QuickTradeStage.AwaitingStop, svc.State.Stage);
        Assert.Equal(25_000, svc.State.AccountEquity);
        Assert.Equal(250, svc.State.RiskCash);   // 1% of 25,000
    }

    /// <summary>
    /// It must say it is doing something. The fetch is a network round trip, and to a screen-reader
    /// user a key that goes quiet is a key that did nothing.
    /// </summary>
    [Fact]
    public void TheFetchAnnouncesItselfImmediately()
    {
        var (svc, bus) = Build(() => 0, () => Task.Delay(5_000));

        svc.Arm(1.0);

        Assert.Contains(Spoken(bus), m => m.Contains("Fetching", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A genuinely empty account gets an accurate reason, not the old misleading advice.</summary>
    [Fact]
    public async Task AnEmptyAccountIsReportedAsEmptyRatherThanAsDisconnected()
    {
        var (svc, bus) = Build(() => 0, () => Task.CompletedTask);

        svc.Arm(1.0);
        for (int i = 0; i < 100 && !Spoken(bus).Any(m => m.Contains("empty")); i++) await Task.Delay(10);

        Assert.Contains(Spoken(bus), m => m.Contains("empty", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(QuickTradeStage.Idle, svc.State.Stage);
    }

    /// <summary>A failing broker call must speak, not disappear into a background task.</summary>
    [Fact]
    public async Task AFailedFetchIsSpoken()
    {
        var (svc, bus) = Build(() => 0,
            () => throw new InvalidOperationException("broker unreachable"));

        svc.Arm(1.0);
        for (int i = 0; i < 100 && !Spoken(bus).Any(m => m.Contains("broker unreachable")); i++) await Task.Delay(10);

        Assert.Contains(Spoken(bus), m => m.Contains("broker unreachable"));
    }

    /// <summary>With no way to fetch at all, the refusal must still name a real remedy.</summary>
    [Fact]
    public void WithNoRefreshDelegateTheAdviceMentionsPaperTrading()
    {
        var (svc, bus) = Build(() => 0, null);
        svc.Arm(1.0);

        Assert.Contains(Spoken(bus), m => m.Contains("paper trading", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Hammering the key must not launch a fetch per press.</summary>
    [Fact]
    public async Task RepeatedPressesDoNotStackFetches()
    {
        int calls = 0;
        // Never completes, so the in-flight guard stays engaged for the whole test.
        var (svc, bus) = Build(() => 0,
            () => { System.Threading.Interlocked.Increment(ref calls); return Task.Delay(System.Threading.Timeout.Infinite); });

        svc.Arm(1.0);

        // The fetch runs off the keystroke path, so wait for it to actually start before pressing
        // again — otherwise this asserts on a race rather than on the guard.
        for (int i = 0; i < 100 && System.Threading.Volatile.Read(ref calls) == 0; i++) await Task.Delay(10);
        Assert.Equal(1, System.Threading.Volatile.Read(ref calls));

        svc.Arm(1.0);
        svc.Arm(2.0);
        await Task.Delay(50);

        Assert.Equal(1, System.Threading.Volatile.Read(ref calls));
        Assert.Contains(Spoken(bus), m => m.Contains("Still fetching", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Only cash counts toward equity. Summing 0.5 BTC + 3,000 USDT + 12 ETH gives a number that is
    /// not money in any currency, and it would then be multiplied by a risk percentage.
    /// </summary>
    [Fact]
    public void OnlyCashAssetsCountTowardEquity()
    {
        Assert.True(QuickTradeEquity.IsCashAsset("USDT"));
        Assert.True(QuickTradeEquity.IsCashAsset("USD"));
        Assert.False(QuickTradeEquity.IsCashAsset("BTC"));
        Assert.False(QuickTradeEquity.IsCashAsset("ETH"));
    }
}
