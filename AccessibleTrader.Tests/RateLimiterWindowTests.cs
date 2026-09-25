using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>After waiting out a full window, the limiter hands out a fresh window's worth of slots.</b>
    ///
    /// <para>
    /// A2p R02: dropping the count reset after the wait left the whole suite green. The effect is
    /// not "one request too many" but a throughput collapse: once a provider saturates its window
    /// the count never comes back down, so EVERY later request waits a whole window — a symbol
    /// list, a backfill and an order-status poll each queue behind a full second (or minute) of
    /// nothing.
    /// </para>
    ///
    /// <para>
    /// No stopwatch. A request that has a free slot completes SYNCHRONOUSLY (the semaphore is free
    /// and no delay is taken), so "the returned task is already complete" is exact on any machine.
    /// A thread parked for longer than the window can only make the limiter reset early — which
    /// makes this test vacuous for that run, never red.
    /// </para>
    /// </summary>
    public class RateLimiterWindowTests
    {
        [Fact]
        public async Task After_waiting_for_a_new_window_the_next_request_in_it_goes_straight_through()
        {
            var limiter = new RateLimiter(2, TimeSpan.FromSeconds(1));
            await limiter.WaitAsync();
            await limiter.WaitAsync();

            await limiter.WaitAsync();                 // window full: waits for the next one

            var fourth = limiter.WaitAsync();          // second request of the NEW window
            Assert.True(fourth.IsCompleted,
                "The first request of a new window was the only one allowed: the count was not reset after waiting, "
                + "so every later request waits a whole window.");
            await fourth;
        }
    }
}
