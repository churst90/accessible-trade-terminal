using AccessibleTrader.Core.Services.Indicators;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// What starts Cipher C's search for the next daily low. Once a confirmed low is there, the daily
/// tier looks from the last low plus the shortest cycle less two bars, and takes the lowest low since
/// the cycle's highest high on the first bar that closes above the bar before's high.
///
/// <para>
/// Read from the original in TradingView's bar replay (2026-10-22/23, Dow and GDX daily): its
/// "unconfirmed" low first appears 21 to 43 bars after the last low on GDX (shortest cycle 22),
/// and is the low after the cycle's top, seldom the lowest bar since the last low. It is the best of
/// the rules tried, not the original's rule (GDX 25 of 38 candidates named on the bar they appear,
/// Dow 21 of 35). The replay data cannot be committed, so these tests hold the engine to the rule
/// on the Bitcoin fixture: 54-bar shortest cycle, so the search opens 52 bars after the last low.
/// The numbers are written out here, not read from the engine's constants.
/// </para>
/// </summary>
public class CipherCSearchTests
{
    private readonly ITestOutputHelper _out;
    public CipherCSearchTests(ITestOutputHelper output) => _out = output;

    private const int SearchFrom = 52;   // 54 less two

    private static CipherCEngine.TierRun Daily(out double[] high, out double[] low, out double[] close, bool searchOnTurn = true)
    {
        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        high = bars.Select(b => b.High).ToArray();
        low = bars.Select(b => b.Low).ToArray();
        close = bars.Select(b => b.Close).ToArray();
        var tier = CipherCEngine.Tier(54, 66, 1, CipherCEngine.MeasureBars(bars));
        Assert.Equal(54, tier.MinBars);
        return CipherCEngine.Detect(bars, tier, 75, 10, searchOnTurn);
    }

    /// <summary>The cycle's top as the search sees it on bar <paramref name="t"/>: the highest high
    /// after the last low, the latest of equal bars.</summary>
    private static int Top(double[] high, int last, int t)
    {
        int top = last + 1;
        for (int i = last + 2; i <= t; i++) if (high[i] >= high[top]) top = i;
        return top;
    }

    /// <summary>Whether bar <paramref name="t"/> turned up from a low after the cycle's top.</summary>
    private static bool TurnedUp(double[] high, double[] low, double[] close, int last, int t)
    {
        if (close[t] <= high[t - 1]) return false;
        int top = Top(high, last, t);
        for (int i = top; i < t; i++) if (low[i] < low[t]) return true;
        return false;
    }

    [Fact]
    public void The_search_opens_no_earlier_than_the_last_low_plus_52_bars()
    {
        var r = Daily(out _, out _, out _);
        Assert.True(r.Lows.Count > 40, $"only {r.Lows.Count} confirmed daily lows on Bitcoin");
        for (int k = 1; k < r.Lows.Count; k++)
            Assert.True(r.FoundAt[k] - r.Lows[k - 1] >= SearchFrom,
                $"low {k} (bar {r.Lows[k]}) found on bar {r.FoundAt[k]}, " +
                $"{r.FoundAt[k] - r.Lows[k - 1]} bars after the last low (bar {r.Lows[k - 1]})");
    }

    [Fact]
    public void Each_low_after_the_first_is_found_on_a_close_above_the_bar_before_s_high()
    {
        var r = Daily(out var high, out _, out var close);
        for (int k = 1; k < r.Lows.Count; k++)
        {
            int f = r.FoundAt[k];
            Assert.True(close[f] > high[f - 1],
                $"low {k} (bar {r.Lows[k]}) found on bar {f}, which closed at {close[f]}, not above the bar before's high {high[f - 1]}");
        }
    }

    /// <summary>
    /// The search does not wait: the low is found on the FIRST bar that qualifies. Without this, a
    /// search still gated on the cycle line's cross (or one that wanted two bars to the low's right)
    /// would pass the two tests above, since its bars qualify too.
    /// </summary>
    [Fact]
    public void Each_low_after_the_first_is_found_on_the_first_bar_that_turns_up()
    {
        var r = Daily(out var high, out var low, out var close);
        int checkedLows = 0;
        for (int k = 1; k < r.Lows.Count; k++)
        {
            int last = r.Lows[k - 1];
            // The search runs once the last low is confirmed and the 52 bars have passed.
            int open = Math.Max(r.ConfirmedAt[k - 1] + 1, last + SearchFrom);
            int cancelled = Enumerable.Range(open, r.FoundAt[k] - open).LastOrDefault(t => !double.IsNaN(r.Cancelled[t]), -1);
            if (cancelled >= 0) { _out.WriteLine($"low {k}: a low found after bar {open} was cancelled on bar {cancelled}"); open = cancelled; }
            for (int t = open; t < r.FoundAt[k]; t++)
                Assert.False(TurnedUp(high, low, close, last, t),
                    $"low {k}: bar {t} turned up from a low after the cycle's top, but the search waited until bar {r.FoundAt[k]}");
            checkedLows++;
        }
        _out.WriteLine($"{checkedLows} lows found on the first bar that turned up");
    }

    /// <summary>
    /// The low it takes is the one after the cycle's top, not the lowest bar since the last low
    /// (which is usually the bar after it). A lower low while it holds may take its place, so the
    /// confirmed low is the lowest bar from the top to the confirmation.
    /// </summary>
    [Fact]
    public void The_low_is_the_lowest_bar_from_the_cycle_s_top_to_its_confirmation()
    {
        var r = Daily(out var high, out var low, out _);
        int afterTop = 0;
        for (int k = 1; k < r.Lows.Count; k++)
        {
            int top = Top(high, r.Lows[k - 1], r.FoundAt[k]);
            Assert.True(r.Lows[k] >= top, $"low {k} (bar {r.Lows[k]}) comes before the cycle's top (bar {top})");
            for (int j = top; j <= r.ConfirmedAt[k]; j++)
                Assert.True(low[j] >= low[r.Lows[k]],
                    $"low {k} (bar {r.Lows[k]}, {low[r.Lows[k]]}): bar {j} went lower ({low[j]}) between the top (bar {top}) and the confirmation (bar {r.ConfirmedAt[k]})");
            // Not the lowest since the last low: somewhere before the top went lower.
            if (Enumerable.Range(r.Lows[k - 1] + 1, top - r.Lows[k - 1] - 1).Any(j => low[j] < low[r.Lows[k]])) afterTop++;
        }
        _out.WriteLine($"{afterTop} of {r.Lows.Count - 1} lows sit above a lower bar between the last low and the top");
        Assert.True(afterTop >= 3, $"only {afterTop} lows differ from the lowest bar since the last low; the top rule is untested");
    }

    /// <summary>Only the daily tier searches this way; the weekly and yearly tiers, and the first
    /// low on any tier, wait for the cycle line's cross.</summary>
    [Fact]
    public void Cipher_C_searches_this_way_on_the_daily_tier_only()
    {
        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        var r = CipherCProvider.Compute(bars, new Dictionary<string, object> { ["Symbol"] = "BTC/USD" });
        var clock = CipherCEngine.MeasureBars(bars);
        var marked = (double[] a) => Enumerable.Range(0, bars.Length).Where(i => !double.IsNaN(a[i])).ToList();

        var daily = CipherCEngine.Tier(54, 66, 1, clock);
        var turn = CipherCEngine.Detect(bars, daily, 75, 10, searchOnTurn: true);
        var cross = CipherCEngine.Detect(bars, daily, 75, 10, searchOnTurn: false);
        Assert.NotEqual(marked(cross.Confirmed), marked(turn.Confirmed));
        Assert.Equal(marked(turn.Confirmed), marked(r[CipherCProvider.CompDclConfirmed]));

        var weekly = CipherCEngine.Tier(22 * 7, 42 * 7, 7, clock);
        Assert.Equal(marked(CipherCEngine.Detect(bars, weekly, 75, 10).Confirmed), marked(r[CipherCProvider.CompWclConfirmed]));
    }

    /// <summary>The first low has no last low to count from, so it waits for the cross either way.</summary>
    [Fact]
    public void The_first_low_waits_for_the_cross()
    {
        var turn = Daily(out _, out _, out _, searchOnTurn: true);
        var cross = Daily(out _, out _, out _, searchOnTurn: false);
        Assert.Equal(cross.Lows[0], turn.Lows[0]);
        Assert.Equal(cross.FoundAt[0], turn.FoundAt[0]);
    }
}
