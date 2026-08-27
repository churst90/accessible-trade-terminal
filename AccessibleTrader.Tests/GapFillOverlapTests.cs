using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A gap-fill that overlaps a live tick must not put the buffer out of order.</b>
///
/// <para>
/// Gap-fill and live ticks are each covered on their own, and both work. What has never been
/// tested is the two of them at once — which is not an exotic case but the ordinary one: a
/// gap-fill runs after a reconnect or a tab regaining focus, and the live stream is exactly what
/// has just started delivering again. <c>ChartFeed.GapFillAsync</c> captures the buffer's last
/// date, awaits a network fetch, and then merges; <c>ApplyLiveTick</c> takes only the prepend
/// lock, which gap-fill does NOT hold. So a live bar can land in the buffer while the fetch is in
/// flight, and the date the merge is reasoning about is stale by the time it runs.
/// </para>
///
/// <para>
/// Two in-lock re-checks are what make that safe, one per branch of the merge, and neither had a
/// test that would fail if it were deleted. Both of them protect the same invariant — bar dates
/// ascend, strictly — and that invariant is not a tidiness preference. Every consumer downstream
/// treats it as given: the resampler buckets by walking forward, the indicator engine's causality
/// contract assumes index order is time order, the renderer maps index to x, and navigation reads
/// "the next bar" as the next index. A buffer that goes 0, 1, 2, 5, 3, 4 does not throw anywhere.
/// It quietly means something different everywhere.
/// </para>
/// </summary>
public sealed class GapFillOverlapTests
{
    private static Ohlcv Bar(int daysFromEpoch, double close = 100) =>
        new(new DateTime(2026, 1, 1).AddDays(daysFromEpoch), close, close + 1, close - 1, close, 1);

    private static ChartFeed Feed(KeyedFeedsTests.FakeOrchestrator orch) =>
        new(new ChartIdentity("Spot", "TestProv", "BTC/USD", "1h"), orch, NullLogger.Instance);

    /// <summary>
    /// The invariant, stated once. Strictly ascending — equal timestamps are as wrong as
    /// descending ones, because a duplicated bar is a bar counted twice by everything that
    /// aggregates.
    /// </summary>
    private static void AssertStrictlyAscending(ChartFeed feed)
    {
        var dates = Enumerable.Range(0, feed.Bars.Count).Select(i => feed.Bars[i].Date).ToList();
        for (int i = 1; i < dates.Count; i++)
            Assert.True(dates[i] > dates[i - 1],
                $"bar {i} at {dates[i]:o} does not follow bar {i - 1} at {dates[i - 1]:o}. " +
                $"Buffer: {string.Join(", ", dates.Select(d => d.ToString("MM-dd")))}");
    }

    // ── The append branch ───────────────────────────────────────────────────────────

    /// <summary>
    /// A live tick arrives PAST the bars the fetch is about to return.
    ///
    /// <para>
    /// The buffer ends at bar 2 when the gap-fill captures its date and goes to the network. While
    /// it waits, the live stream delivers bar 5. The fetch then comes back with bars 3, 4 and 5 —
    /// all of them newer than the date the gap-fill remembers, and all of them older than or equal
    /// to the bar that is now actually last. Appending them in that state produces
    /// <c>0, 1, 2, 5, 3, 4, 5</c>: out of order and with bar 5 in it twice.
    /// </para>
    ///
    /// <para>
    /// The correct outcome leaves a HOLE — bars 3 and 4 are simply not there — and that is the
    /// right trade. A missing bar is visible and self-corrects on the next gap-fill; a buffer whose
    /// index order is not time order corrects nothing and is believed by everything.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ALiveTickThatOvertakesAnInFlightGapFillDoesNotBreakOrdering()
    {
        var orch = new KeyedFeedsTests.FakeOrchestrator { FetchGate = new TaskCompletionSource() };
        var feed = Feed(orch);
        feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1), Bar(2) }));
        orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(3), Bar(4), Bar(5) });

        var inFlight = feed.GapFillAsync();

        // The live stream overtakes the fetch. Gap-fill does not hold the prepend lock, so this
        // genuinely lands — that is the whole premise, and it is asserted rather than assumed.
        Assert.True(feed.ApplyLiveTick(Bar(5, close: 555)),
            "the live tick was rejected, so this test never created the overlap it is named for");

        orch.FetchGate.SetResult();
        await inFlight;

        AssertStrictlyAscending(feed);
        Assert.Equal(Bar(5).Date, feed.Bars[feed.Bars.Count - 1].Date);
        Assert.Equal(555, feed.Bars[feed.Bars.Count - 1].Close);   // the LIVE bar, not the fetched one
    }

    /// <summary>
    /// The same overlap where the fetch is partly ahead of the live bar: bars 3 and 4 arrive while
    /// the buffer's last is bar 3. Bar 4 is genuinely new and must be appended; bar 3 must not.
    /// A guard that simply refused to merge anything after a concurrent tick would pass the test
    /// above and lose real data here.
    /// </summary>
    [Fact]
    public async Task GenuinelyNewerBarsStillMergeAcrossTheOverlap()
    {
        var orch = new KeyedFeedsTests.FakeOrchestrator { FetchGate = new TaskCompletionSource() };
        var feed = Feed(orch);
        feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1), Bar(2) }));
        orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(3), Bar(4) });

        var inFlight = feed.GapFillAsync();
        Assert.True(feed.ApplyLiveTick(Bar(3, close: 333)));
        orch.FetchGate.SetResult();
        await inFlight;

        AssertStrictlyAscending(feed);
        Assert.Equal(5, feed.Bars.Count);                          // 0,1,2,3,4
        Assert.Equal(Bar(4).Date, feed.Bars[feed.Bars.Count - 1].Date);
        Assert.Equal(333, feed.Bars[3].Close);                     // bar 3 stayed the LIVE one
    }

    // ── The intra-bar replace branch ────────────────────────────────────────────────

    /// <summary>
    /// The other re-check, on the branch nobody thinks about.
    ///
    /// <para>
    /// When a fetch returns nothing newer, gap-fill refreshes the live bar in place — the bar is
    /// still forming, and the fetch has a fresher version of it. But if a live tick has opened a
    /// NEW period in the meantime, the bar the fetch described is no longer the last one, and
    /// replacing the last bar with it overwrites the new period with the old one: the buffer
    /// goes 0, 1, 2 with bar 2 sitting where bar 3 was, and the chart silently loses a period
    /// while continuing to look completely normal.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnIntrabarRefreshDoesNotOverwriteANewPeriodOpenedMidFetch()
    {
        var orch = new KeyedFeedsTests.FakeOrchestrator { FetchGate = new TaskCompletionSource() };
        var feed = Feed(orch);
        feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1), Bar(2, close: 100) }));
        // Nothing newer than bar 2 — just a fresher bar 2.
        orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(1), Bar(2, close: 222) });

        var inFlight = feed.GapFillAsync();
        Assert.True(feed.ApplyLiveTick(Bar(3, close: 333)));        // a new period opens
        orch.FetchGate.SetResult();
        await inFlight;

        AssertStrictlyAscending(feed);
        Assert.Equal(4, feed.Bars.Count);
        Assert.Equal(Bar(3).Date, feed.Bars[3].Date);
        Assert.Equal(333, feed.Bars[3].Close);
    }

    /// <summary>
    /// The vacuity half of that one. With NO concurrent tick the same fetch must do its job and
    /// refresh the forming bar — otherwise "the last bar was not overwritten" would be satisfied
    /// by an intra-bar refresh that had simply stopped working.
    /// </summary>
    [Fact]
    public async Task AnIntrabarRefreshStillHappensWhenNothingOverlapsIt()
    {
        var orch = new KeyedFeedsTests.FakeOrchestrator();
        var feed = Feed(orch);
        feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0), Bar(1), Bar(2, close: 100) }));
        orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(1), Bar(2, close: 222) });

        Assert.True(await feed.GapFillAsync());

        Assert.Equal(3, feed.Bars.Count);
        Assert.Equal(222, feed.Bars[2].Close);
    }

    /// <summary>
    /// Many ticks against many gap-fills, run for real rather than staged.
    ///
    /// <para>
    /// The scripted tests above pin the two specific interleavings that matter and are the ones
    /// that would go red if a re-check were deleted. This one is the coarse net: it runs the two
    /// operations against each other repeatedly and asserts only the invariant. It is here because
    /// a staged test can only catch the orderings its author thought of, and the reason both of
    /// these guards exist is that somebody did not.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheInvariantHoldsUnderRepeatedOverlap()
    {
        var orch = new KeyedFeedsTests.FakeOrchestrator();
        var feed = Feed(orch);
        feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0) }));

        for (int round = 1; round <= 30; round++)
        {
            var gate = new TaskCompletionSource();
            orch.FetchGate = gate;
            // The fetch straddles wherever the live stream has got to: some of these bars will be
            // behind the buffer by the time the merge runs, and some ahead.
            orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(round), Bar(round + 1), Bar(round + 2) });

            var inFlight = feed.GapFillAsync();
            feed.ApplyLiveTick(Bar(round + 1, close: 500 + round));
            gate.SetResult();
            await inFlight;

            AssertStrictlyAscending(feed);
        }

        Assert.True(feed.Bars.Count > 1, "nothing was ever merged — the loop proved nothing");
    }
}
