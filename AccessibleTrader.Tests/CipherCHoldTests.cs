using AccessibleTrader.Core.Services.Indicators;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// Cipher C confirms a cycle low when the original does: after the low has held for half the
/// shortest cycle, counted from the low or from the last low plus the shortest cycle less two bars,
/// whichever is later. A lower low while it holds takes its place.
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
        return CipherCEngine.Detect(bars, tier, 75, 10);
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
    /// A lower low during the hold replaces the one being held, so the confirmed low is still the
    /// lowest bar from itself to the bar that confirms it.
    /// </summary>
    [Fact]
    public void A_confirmed_low_is_the_lowest_bar_up_to_its_confirmation()
    {
        var r = Daily(out var low);
        for (int k = 0; k < r.Lows.Count; k++)
            for (int j = r.Lows[k] + 1; j <= r.ConfirmedAt[k]; j++)
                Assert.True(low[j] >= low[r.Lows[k]],
                    $"low {k} (bar {r.Lows[k]}, {low[r.Lows[k]]}) confirmed on bar {r.ConfirmedAt[k]} " +
                    $"although bar {j} went lower ({low[j]}) while it held");
    }
}
