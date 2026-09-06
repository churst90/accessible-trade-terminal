using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Alerts;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// The fan-out from "an alert fired" to the channels that deliver it.
///
/// <para>
/// A2e SURVIVOR (E24), and the reason it survived is the simplest one there is: <b>this class had
/// no test file.</b> It was named only in passing by <c>WebhookAlertChannelTests</c>, as a
/// constructor argument. Inverting <c>if (!ch.IsConfigured) continue;</c> — so that every
/// configured channel is skipped and every unconfigured one is called — passed all 6,887 tests.
/// </para>
///
/// <para>
/// That inversion is not cosmetic. A channel reports <c>IsConfigured</c> false precisely when it
/// has no SMTP host, no bot token, no webhook URL; calling it anyway means an exception per alert
/// per channel, and the configured channel that the user actually set up is the one that goes
/// silent. On an audio-first terminal a delivery that never happens has no symptom at all.
/// </para>
///
/// <para>
/// The dispatch is deliberately fire-and-forget (<c>Task.Run</c>) so the alert pipeline never
/// waits on a network round trip, which is why every assertion here waits on the channel's own
/// signal rather than on the call returning.
/// </para>
/// </summary>
public class AlertDeliveryServiceTests
{
    /// <summary>
    /// A channel that records what it was asked to send, and can be told to fail.
    /// </summary>
    private sealed class SpyChannel : IAlertChannel
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SpyChannel(string id, bool configured, bool throws = false)
        {
            Id = id;
            IsConfigured = configured;
            Throws = throws;
        }

        public string Id { get; }
        public string DisplayName => Id;
        public bool IsConfigured { get; }
        public bool Throws { get; }
        public int Sends;

        /// <summary>Completes the first time <see cref="SendAsync"/> is entered.</summary>
        public Task Called => _called.Task;

        public Task SendAsync(AlertFired alert, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Sends);
            _called.TrySetResult();
            return Throws
                ? Task.FromException(new InvalidOperationException("channel is down"))
                : Task.CompletedTask;
        }
    }

    private static AlertFired Fired(string name = "BTC over 64k") =>
        new(new AlertDefinition
            {
                Id = name, Name = name,
                Target = AlertTarget.Price,
                Condition = AlertCondition.CrossesAbove,
                Delivery = AlertDelivery.Speech,
            },
            TriggeringValue: 64_000, PreviousValue: 63_900, SpeechText: name);

    /// <summary>
    /// Waits for a signal, or fails with a sentence rather than a timeout exception.
    /// The dispatch is on a thread-pool thread, so "it did not happen yet" and "it will never
    /// happen" are the same observation until the deadline passes.
    /// </summary>
    private static async Task ShouldBeCalled(SpyChannel ch)
    {
        var done = await Task.WhenAny(ch.Called, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(done == ch.Called, $"channel '{ch.Id}' was never asked to deliver.");
    }

    private static async Task ShouldNotBeCalled(SpyChannel ch)
    {
        // A negative on an async path needs a settling window, or it passes because the
        // dispatch had not started yet — which is the "it did not happen YET" trap.
        var done = await Task.WhenAny(ch.Called, Task.Delay(TimeSpan.FromMilliseconds(750)));
        Assert.True(done != ch.Called, $"channel '{ch.Id}' was asked to deliver and should not have been.");
    }

    [Fact]
    public async Task AConfiguredChannelIsAskedToDeliver()
    {
        var bus = new EventBus();
        var ch = new SpyChannel("email", configured: true);
        using var _ = new AlertDeliveryService(new[] { (IAlertChannel)ch }, bus);

        bus.Publish(new AlertFiredEvent(Fired()));

        await ShouldBeCalled(ch);
    }

    [Fact]
    public async Task AnUnconfiguredChannelIsNotAskedToDeliver()
    {
        // THE SURVIVOR. A channel with no SMTP host, no bot token, no webhook URL is skipped —
        // calling it is an exception per alert and nothing the user can see.
        var bus = new EventBus();
        var ch = new SpyChannel("telegram", configured: false);
        using var _ = new AlertDeliveryService(new[] { (IAlertChannel)ch }, bus);

        bus.Publish(new AlertFiredEvent(Fired()));

        await ShouldNotBeCalled(ch);
    }

    [Fact]
    public async Task TheConfiguredOneIsCalledWhileTheUnconfiguredOneIsNot()
    {
        // Both halves in ONE run. Asserted separately, an inverted gate satisfies each test in
        // isolation only if you happen to read them together — this pins the discrimination
        // itself, which is what the mutant broke.
        var bus = new EventBus();
        var ready = new SpyChannel("email", configured: true);
        var blank = new SpyChannel("telegram", configured: false);
        using var _ = new AlertDeliveryService(new IAlertChannel[] { ready, blank }, bus);

        bus.Publish(new AlertFiredEvent(Fired()));

        await ShouldBeCalled(ready);
        await ShouldNotBeCalled(blank);
    }

    [Fact]
    public async Task OneChannelFailingDoesNotStarveTheOthers()
    {
        // The class's stated contract, and also untested until now: exceptions are swallowed
        // per channel so a broken Telegram token cannot silence email.
        var bus = new EventBus();
        var broken = new SpyChannel("telegram", configured: true, throws: true);
        var working = new SpyChannel("email", configured: true);
        using var _ = new AlertDeliveryService(new IAlertChannel[] { broken, working }, bus);

        bus.Publish(new AlertFiredEvent(Fired()));

        await ShouldBeCalled(broken);
        await ShouldBeCalled(working);
    }

    [Fact]
    public async Task DisposingStopsDelivery()
    {
        // The subscription is the only thing keeping this object alive in DI; a disposed service
        // that keeps delivering is a duplicate-notification source once a second one is created.
        var bus = new EventBus();
        var ch = new SpyChannel("email", configured: true);
        var svc = new AlertDeliveryService(new[] { (IAlertChannel)ch }, bus);
        svc.Dispose();

        bus.Publish(new AlertFiredEvent(Fired()));

        await ShouldNotBeCalled(ch);
    }
}
