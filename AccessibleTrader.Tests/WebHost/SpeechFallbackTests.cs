using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// When the out-of-band voice fails, the phrase falls back to the live region ONCE — and the
/// live region goes back to being off.
///
/// <para>
/// <b>What nothing checked (A2o, survivor O43).</b> <c>FallBackToLiveRegion</c> turns the
/// live region on for one phrase and restores the previous flag in a <c>finally</c>. Changing
/// that restore to <c>LiveRegionEnabled = true</c> left every test green, and its effect is
/// the 2026-07-23 double-speech defect by another road: after ONE failed spd-say/Orca call,
/// every later phrase is written to the live region by the inner manager AND spoken again
/// by the fallback (or by Orca, once it recovers) — the user hears everything twice for the
/// rest of the session. No test had ever driven a server-side backend whose process start
/// fails, so neither the fallback nor its restore had a single assertion.
/// </para>
///
/// <para>
/// The failure is real, not simulated: <c>spd-say</c> is pointed at a path that does not
/// exist, so <c>Process.Start</c> throws exactly as it does on a box where the binary was
/// removed after startup probing. The pump is asynchronous, so the test waits for the
/// backend's own "falling back" warning — one per phrase, emitted before the fallback
/// speaks — rather than sleeping.
/// </para>
/// </summary>
public sealed class SpeechFallbackTests
{
    private sealed class NullBus : IEventBus
    {
        public void Publish<T>(T eventData) { }
        public IDisposable Subscribe<T>(Action<T> handler) => new D();
        public IObservable<T> AsObservable<T>() => System.Reactive.Linq.Observable.Empty<T>();
        public IDisposable SubscribeCoalesced<T>(Action<T> handler, TimeSpan quietWindow) => new D();
        public IDisposable SubscribeSampled<T>(Action<T> handler, TimeSpan window) => new D();
        private sealed class D : IDisposable { public void Dispose() { } }
    }

    private sealed class CountingLogger : ILogger<WebHostSpeechManager>
    {
        private int _warnings;
        public int Warnings => Volatile.Read(ref _warnings);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning
                && formatter(state, exception).Contains("falling back to the live region", StringComparison.Ordinal))
                Interlocked.Increment(ref _warnings);
        }
    }

    private static void WaitFor(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for " + what);
            Thread.Sleep(10);
        }
    }

    [Fact]
    public void A_failed_spd_say_phrase_is_spoken_once_and_the_next_one_is_not_doubled()
    {
        var inner = new BlazorSpeechManager(NullLogger<BlazorSpeechManager>.Instance,
                                            new ServiceCollection().BuildServiceProvider());
        var spoken = new List<string>();
        inner.OnSpeak = t => { lock (spoken) spoken.Add(t); };
        var log = new CountingLogger();

        var sut = new WebHostSpeechManager(inner, new NullBus(), log,
            spdSayPath: "/nonexistent-a2o/spd-say", gdbusPath: null, orcaAvailable: false);
        Assert.False(inner.LiveRegionEnabled, "precondition: a server-side backend owns the voice");

        sut.Speak("Order rejected.");
        WaitFor(() => log.Warnings >= 1, "the first fallback");
        WaitFor(() => { lock (spoken) return spoken.Contains("Order rejected."); }, "the fallback to speak");

        // The flag the fallback borrowed must be handed back, or the live region stays on and
        // doubles every later phrase.
        WaitFor(() => !inner.LiveRegionEnabled, "the live region to be switched back off");

        sut.Speak("Stop loss hit.");
        sut.Speak("Position closed.");      // sentinel: the pump is FIFO, so once this one's
        WaitFor(() => log.Warnings >= 3, "the sentinel's fallback");   // warning is logged, the phrase before it is done

        int heard;
        lock (spoken) heard = spoken.Count(s => s == "Stop loss hit.");
        Assert.Equal(1, heard);
    }
}
