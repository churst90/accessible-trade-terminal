using AccessibleTrader.WebHost.Services;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <b>The one gate the browser-closed half turns on.</b>
///
/// <para>
/// Cody's policy, 2026-09-11: a connected browser — minimised included — announces every
/// terminal event in its own live region and nothing may toast; no connected browser and the
/// headless process owns every event and delivers it as a system notification. That makes the
/// decision per PROCESS, and it makes "connected" the word that has to be exact.
/// </para>
///
/// <para>
/// The counter that already existed could not do it. <c>WebHostBrowserCircuitHandler.ActiveCircuits</c>
/// counts RETAINED circuits — up on circuit-opened, down on circuit-closed — and Blazor holds a
/// closed tab's circuit for about three minutes. Gating on it would have meant three minutes of
/// silence after closing the browser, which is the very bug the 2026-09-11 hand-off fix removed.
/// </para>
/// </summary>
[Collection("CircuitCoverage")]
public sealed class BrowserPresenceTests : IDisposable
{
    public BrowserPresenceTests() => BrowserPresence.ResetForTests();
    public void Dispose() => BrowserPresence.ResetForTests();

    private static DateTime _now = new(2026, 9, 11, 20, 0, 0, DateTimeKind.Utc);
    private static void UseTestClock() { _now = new DateTime(2026, 9, 11, 20, 0, 0, DateTimeKind.Utc); BrowserPresence.UtcNow = () => _now; }
    private static void Advance(TimeSpan by) => _now += by;

    [Fact]
    public void With_no_circuit_ever_connected_the_headless_side_owns_delivery()
    {
        UseTestClock();
        Assert.False(BrowserPresence.AnyConnected);
        Assert.True(BrowserPresence.HeadlessOwnsDelivery);
    }

    [Fact]
    public void A_connected_circuit_takes_delivery_back_at_once()
    {
        UseTestClock();
        BrowserPresence.Connected("c1");

        Assert.Equal(1, BrowserPresence.ConnectedCircuits);
        Assert.True(BrowserPresence.AnyConnected);
        Assert.False(BrowserPresence.HeadlessOwnsDelivery);   // the 0→1 edge silences headless NOW
    }

    [Fact]
    public void The_same_circuit_reporting_up_twice_counts_once()
    {
        UseTestClock();
        // OnConnectionUpAsync genuinely runs twice for a first connection — once for the
        // connection and once right after OnCircuitOpenedAsync. A counter would read 2 here
        // and never return to 0.
        BrowserPresence.Connected("c1");
        BrowserPresence.Connected("c1");
        Assert.Equal(1, BrowserPresence.ConnectedCircuits);

        BrowserPresence.Disconnected("c1");
        Assert.Equal(0, BrowserPresence.ConnectedCircuits);
    }

    [Fact]
    public void Disconnecting_an_unknown_circuit_is_a_noop()
    {
        UseTestClock();
        BrowserPresence.Connected("c1");
        BrowserPresence.Disconnected("never-connected");
        Assert.Equal(1, BrowserPresence.ConnectedCircuits);
    }

    [Fact]
    public void Three_tabs_closed_together_reach_zero_once()
    {
        UseTestClock();
        int edges = 0;
        BrowserPresence.ConnectedCountChanged += n => { if (n == 0) edges++; };

        BrowserPresence.Connected("a");
        BrowserPresence.Connected("b");
        BrowserPresence.Connected("c");
        BrowserPresence.Disconnected("a");
        BrowserPresence.Disconnected("b");
        BrowserPresence.Disconnected("c");

        Assert.Equal(1, edges);
    }

    // ── The grace period: a blip and a reload are not "the user left" ────────────

    [Fact]
    public void The_headless_side_does_not_take_over_during_the_grace_period()
    {
        UseTestClock();
        BrowserPresence.Connected("c1");
        BrowserPresence.Disconnected("c1");

        Advance(BrowserPresence.Grace - TimeSpan.FromSeconds(1));
        Assert.False(BrowserPresence.HeadlessOwnsDelivery);
    }

    [Fact]
    public void After_the_grace_period_the_headless_side_owns_delivery()
    {
        UseTestClock();
        BrowserPresence.Connected("c1");
        BrowserPresence.Disconnected("c1");

        Advance(BrowserPresence.Grace);
        Assert.True(BrowserPresence.HeadlessOwnsDelivery);
    }

    [Fact]
    public void A_reload_inside_the_grace_period_never_hands_over()
    {
        UseTestClock();
        BrowserPresence.Connected("old-circuit");

        // A reload: the old circuit's connection drops and a new circuit connects a moment later.
        BrowserPresence.Disconnected("old-circuit");
        Advance(TimeSpan.FromSeconds(2));
        Assert.False(BrowserPresence.HeadlessOwnsDelivery);

        BrowserPresence.Connected("new-circuit");
        Advance(BrowserPresence.Grace + TimeSpan.FromMinutes(5));

        Assert.True(BrowserPresence.AnyConnected);
        Assert.False(BrowserPresence.HeadlessOwnsDelivery);
    }

    [Fact]
    public void A_five_second_blip_never_hands_over()
    {
        UseTestClock();
        BrowserPresence.Connected("c1");
        BrowserPresence.Disconnected("c1");
        Advance(TimeSpan.FromSeconds(5));
        Assert.False(BrowserPresence.HeadlessOwnsDelivery);

        BrowserPresence.Connected("c1");
        Assert.False(BrowserPresence.HeadlessOwnsDelivery);
    }

    [Fact]
    public void Reconnecting_after_the_hand_off_takes_delivery_straight_back()
    {
        UseTestClock();
        BrowserPresence.Connected("c1");
        BrowserPresence.Disconnected("c1");
        Advance(BrowserPresence.Grace);
        Assert.True(BrowserPresence.HeadlessOwnsDelivery);

        BrowserPresence.Connected("c2");
        Assert.False(BrowserPresence.HeadlessOwnsDelivery);   // no grace on the way back in
    }

    [Fact]
    public void One_tab_left_open_keeps_the_browser_in_charge()
    {
        UseTestClock();
        BrowserPresence.Connected("a");
        BrowserPresence.Connected("b");
        BrowserPresence.Disconnected("a");
        Advance(BrowserPresence.Grace + TimeSpan.FromMinutes(1));

        Assert.Equal(1, BrowserPresence.ConnectedCircuits);
        Assert.False(BrowserPresence.HeadlessOwnsDelivery);
    }

    [Fact]
    public void A_throwing_listener_does_not_lose_the_edge_for_the_next_one()
    {
        UseTestClock();
        bool secondRan = false;
        BrowserPresence.ConnectedCountChanged += _ => throw new InvalidOperationException("boom");
        BrowserPresence.ConnectedCountChanged += _ => secondRan = true;

        BrowserPresence.Connected("c1");   // must not throw into a circuit lifecycle method

        Assert.True(secondRan);
    }
}
