using AccessibleTrader.Core.Services.Indicators;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// Cipher C confirms a cycle low when the original does: after the low has held for half the
/// shortest cycle, counted from the low or from the last low plus the shortest cycle less two bars,
/// whichever is later. A lower bar while it holds, up to the bar before it is due, cancels it and
/// the search looks again from the next bar; on the due bar it is confirmed whatever the bar's low
/// (<see cref="CipherCSearchTests"/>). A confirmed low is never taken back: a close under it fails
/// the cycle.
///
/// <para>
/// The rule was read bar by bar from the original in TradingView's bar replay on 2026-10-22: Dow
/// daily (36-bar shortest cycle) confirms 18 bars after the later of the low and the last low plus
/// 34, GDX daily (22) 11 bars after the later of the low and the last low plus 20, on every cycle
/// from 2023 (Dow) and October 2023 (GDX) to October 2026. That data cannot be committed, so these
/// tests hold the engine to the rule on the Bitcoin fixture: 54-bar shortest cycle, so a 27-bar
/// hold from no earlier than the last low plus 52. The numbers are written out here from what the
/// original did, not read from the engine's constants.
/// </para>
/// </summary>
public class CipherCHoldTests
{
    private readonly ITestOutputHelper _out;
    public CipherCHoldTests(ITestOutputHelper output) => _out = output;

    private static CipherCEngine.TierRun Daily(out double[] low)
    {
        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        low = bars.Select(b => b.Low).ToArray();
        var tier = CipherCEngine.Tier(54, 66, 1, CipherCEngine.MeasureBars(bars));
        Assert.Equal(54, tier.MinBars);
        return CipherCEngine.Detect(bars, tier, 75, 10, searchOnTurn: true);   // as the daily tier runs
    }

    private const int Hold = 27;          // half of 54
    private const int HoldFromLast = 52;  // 54 less two

    /// <summary>The bar the hold counts from: the low, or the last low plus 52 if that is later.</summary>
    private static int From(CipherCEngine.TierRun r, int k) =>
        k == 0 ? r.Lows[0] : Math.Max(r.Lows[k], r.Lows[k - 1] + HoldFromLast);

    [Fact]
    public void No_low_is_confirmed_before_it_has_held_half_the_shortest_cycle()
    {
        var r = Daily(out _);
        Assert.True(r.Lows.Count > 40, $"only {r.Lows.Count} confirmed daily lows on Bitcoin");
        for (int k = 0; k < r.Lows.Count; k++)
            Assert.True(r.ConfirmedAt[k] - From(r, k) >= Hold,
                $"low {k} (bar {r.Lows[k]}) confirmed on bar {r.ConfirmedAt[k]}, " +
                $"{r.ConfirmedAt[k] - From(r, k)} bars after bar {From(r, k)}, short of the {Hold}-bar hold");
    }

    /// <summary>
    /// The hold is the whole wait whenever the cycle line had already found the low: most lows are
    /// confirmed exactly 27 bars after the bar the hold counts from. Without this, a hold counted
    /// from a bar or two too late (or a hold a bar too long) would pass the test above.
    /// </summary>
    [Fact]
    public void Most_lows_are_confirmed_exactly_when_the_hold_ends()
    {
        var r = Daily(out _);
        int exact = Enumerable.Range(0, r.Lows.Count).Count(k => r.ConfirmedAt[k] - From(r, k) == Hold);
        int fromLast = Enumerable.Range(1, r.Lows.Count - 1)
            .Count(k => r.Lows[k - 1] + HoldFromLast > r.Lows[k] && r.ConfirmedAt[k] - From(r, k) == Hold);
        _out.WriteLine($"{exact} of {r.Lows.Count} confirmed exactly {Hold} bars after the hold began, " +
                       $"{fromLast} of them counted from the last low plus {HoldFromLast}");
        Assert.True(exact * 2 >= r.Lows.Count, $"only {exact} of {r.Lows.Count} lows confirmed when the hold ended");
        Assert.True(fromLast >= 3, $"only {fromLast} holds counted from the last low; the rule's second half is untested");
    }

    /// <summary>
    /// A lower low during the hold cancels the one being held, so the confirmed low is still the
    /// lowest bar from itself to the bar before the one that confirms it.
    /// </summary>
    [Fact]
    public void A_confirmed_low_is_the_lowest_bar_up_to_its_confirmation()
    {
        var r = Daily(out var low);
        for (int k = 0; k < r.Lows.Count; k++)
            for (int j = r.Lows[k] + 1; j < r.ConfirmedAt[k]; j++)
                Assert.True(low[j] >= low[r.Lows[k]],
                    $"low {k} (bar {r.Lows[k]}, {low[r.Lows[k]]}) confirmed on bar {r.ConfirmedAt[k]} " +
                    $"although bar {j} went lower ({low[j]}) while it held");
    }

    /// <summary>
    /// A confirmed low is never taken back: the original drew no cross over, and removed, none of
    /// the 379 lows it confirmed in its candle replays on sixteen assets (2022-26). Every
    /// confirmation stays among the lows, in order, and a close under one is a failed cycle.
    /// </summary>
    [Fact]
    public void A_confirmed_low_is_never_taken_back()
    {
        var r = Daily(out var low);
        var confirmed = Enumerable.Range(0, r.Confirmed.Length).Where(t => !double.IsNaN(r.Confirmed[t])).ToList();
        Assert.Equal(confirmed, r.ConfirmedAt);
        for (int k = 0; k < confirmed.Count; k++)
            Assert.Equal(low[r.Lows[k]], r.Confirmed[confirmed[k]]);
        // The newest confirmed low known on each bar only ever moves on.
        for (int t = 1; t < r.LastLowAt.Length; t++)
            Assert.True(r.LastLowAt[t] >= r.LastLowAt[t - 1], $"bar {t}: the last low went back from {r.LastLowAt[t - 1]} to {r.LastLowAt[t]}");
    }

    /// <summary>
    /// A failure is the FIRST close under the newest confirmed low, however soon after it (the
    /// original's minimum gap no longer splits a cancellation from a failure), and every
    /// confirmed low a close goes under before the next is confirmed fails once.
    /// </summary>
    [Fact]
    public void A_failure_is_the_first_close_under_the_confirmed_low_at_any_gap()
    {
        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        var r = Daily(out var low);
        double[] close = bars.Select(b => b.Close).ToArray();
        var failed = Enumerable.Range(0, close.Length).Where(t => !double.IsNaN(r.Failed[t])).ToList();
        int early = 0;
        for (int k = 0; k < r.Lows.Count; k++)
        {
            int l = r.Lows[k], end = k + 1 < r.Lows.Count ? r.ConfirmedAt[k + 1] : close.Length;
            int first = Enumerable.Range(r.ConfirmedAt[k] + 1, Math.Max(0, end - r.ConfirmedAt[k] - 1)).FirstOrDefault(t => close[t] < low[l], -1);
            var mine = failed.Where(t => t > r.ConfirmedAt[k] && t < end).ToList();
            if (first < 0) { Assert.Empty(mine); continue; }
            Assert.Equal(new[] { first }, mine);
            Assert.Equal(low[l], r.Failed[first]);
            Assert.Equal(l, r.ClosedUnder[first]);
            if (first - l < 0.6 * 54) early++;
        }
        _out.WriteLine($"{failed.Count} failures, {early} of them sooner than 60% of the shortest cycle");
        Assert.True(early >= 1, "no failure before the old minimum gap: the any-gap rule is untested");
    }

    /// <summary>
    /// A cancellation is a bar going under the low still holding, before the bar it is due. The
    /// original drops that low and names the next one a bar or more later (328 of 332 pending lows
    /// it dropped, none replaced on the same bar), so nothing is held on the cancelling bar; the
    /// dropped low is never a confirmed one, and the next confirmation says the price it replaces.
    /// </summary>
    [Fact]
    public void A_cancellation_is_a_bar_under_the_pending_low_before_it_is_due()
    {
        var r = Daily(out var low);
        int last = -1, n = 0;
        double lastCancelled = double.NaN;
        for (int t = 1; t < low.Length; t++)
        {
            int held = r.HeldAt[t - 1], lastLow = r.LastLowAt[t - 1];
            bool beforeDue = held >= 0 && t - (lastLow >= 0 ? Math.Max(held, lastLow + HoldFromLast) : held) < Hold;
            bool under = held >= 0 && low[t] < low[held];
            if (under && beforeDue)
            {
                n++;
                Assert.Equal(low[held], r.Cancelled[t]);
                Assert.Equal(held, r.CancelledLow[t]);
                Assert.Equal(lastLow >= 0 ? -1 : t, r.HeldAt[t]);   // before the first low, the cycle line's cross holds the bar
                Assert.DoesNotContain(held, r.Lows);
                lastCancelled = r.Cancelled[t];
            }
            else Assert.True(double.IsNaN(r.Cancelled[t]), $"bar {t}: cancelled although {(under ? "the low was due" : "no bar went under the held low")}");
            if (!double.IsNaN(r.Confirmed[t]))
            {
                Assert.Equal(lastCancelled, r.Updated[t]);   // NaN when none was cancelled since the last
                lastCancelled = double.NaN;
                last = t;
            }
        }
        _out.WriteLine($"{n} pending lows cancelled; last confirmation on bar {last}");
        Assert.True(n >= 5, $"only {n} cancellations on Bitcoin");
    }
}
