using System;
using System.Collections.Generic;
using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Pins the decorator contract on <see cref="WebHostSpeechManager"/>:
/// every <c>Speak</c> / <c>Silence</c> call hits the inner manager
/// (preserving the journal + ARIA live region) BEFORE the out-of-band
/// backend fires, and <c>OnSpeak</c> / <c>IsSpeechEnabled</c> property
/// access forwards transparently. Without this guarantee the decorator
/// could subtly break the ARIA fallback path the MainLayout wires up.
/// </summary>
public class WebHostSpeechManagerForwardingTests
{
    /// <summary>Captures BrowserSpeakRequest events so the test can assert what was published.</summary>
    private sealed class CapturingEventBus : IEventBus
    {
        public List<object> Published { get; } = new();
        public void Publish<T>(T eventData) { if (eventData is not null) Published.Add(eventData); }
        public IDisposable Subscribe<T>(Action<T> handler) => new DummyDisposable();
        public IObservable<T> AsObservable<T>() => System.Reactive.Linq.Observable.Empty<T>();
        public IDisposable SubscribeCoalesced<T>(Action<T> handler, TimeSpan quietWindow) => new DummyDisposable();
        public IDisposable SubscribeSampled<T>(Action<T> handler, TimeSpan window) => new DummyDisposable();
        private sealed class DummyDisposable : IDisposable { public void Dispose() { } }
    }

    private static WebHostSpeechManager MakeBrowserBackend(ISpeechManager inner, IEventBus bus)
        => new WebHostSpeechManager(
            inner, bus, NullLogger<WebHostSpeechManager>.Instance,
            spdSayPath: null, gdbusPath: null, orcaAvailable: false);

    [Fact]
    public void SpeakCallsInnerBeforePublishingBrowserRequest()
    {
        var inner = Substitute.For<ISpeechManager>();
        inner.IsSpeechEnabled.Returns(true);
        var bus = new CapturingEventBus();
        var sut = MakeBrowserBackend(inner, bus);

        sut.Speak("hello world", interrupt: true);

        // Inner Speak is invoked first (preserves journal + ARIA-live wiring).
        inner.Received(1).Speak("hello world", true);
        // Then the browser path publishes a BrowserSpeakRequest.
        Assert.Single(bus.Published);
        var req = Assert.IsType<BrowserSpeakRequest>(bus.Published[0]);
        Assert.Equal("hello world", req.Text);
        Assert.True(req.Interrupt);
    }

    [Fact]
    public void SpeakWithEmptyTextStillInvokesInnerButDoesNotPublish()
    {
        var inner = Substitute.For<ISpeechManager>();
        inner.IsSpeechEnabled.Returns(true);
        var bus = new CapturingEventBus();
        var sut = MakeBrowserBackend(inner, bus);

        sut.Speak("   ", interrupt: false);

        // Inner gets called unconditionally — journal/ARIA see whitespace
        // and decide for themselves whether to log/announce it.
        inner.Received(1).Speak("   ", false);
        // But the JS speech bridge is a waste of a round-trip for empty
        // text, so we skip the publish.
        Assert.Empty(bus.Published);
    }

    [Fact]
    public void SpeakWhenSpeechDisabledStillInvokesInnerButSkipsPublish()
    {
        var inner = Substitute.For<ISpeechManager>();
        inner.IsSpeechEnabled.Returns(false);
        var bus = new CapturingEventBus();
        var sut = MakeBrowserBackend(inner, bus);

        sut.Speak("important update", interrupt: false);

        // Even when speech is globally muted the inner manager is still
        // called — it controls journaling and may apply its own gates.
        inner.Received(1).Speak("important update", false);
        // The browser path respects the global mute.
        Assert.Empty(bus.Published);
    }

    [Fact]
    public void SilenceCallsInnerAndPublishesCancellingEmptyRequest()
    {
        var inner = Substitute.For<ISpeechManager>();
        var bus = new CapturingEventBus();
        var sut = MakeBrowserBackend(inner, bus);

        sut.Silence();

        inner.Received(1).Silence();
        // Browser-backend silence is signalled with text="" interrupt=true,
        // which webSpeech.js translates to speechSynthesis.cancel().
        var req = Assert.IsType<BrowserSpeakRequest>(Assert.Single(bus.Published));
        Assert.Equal(string.Empty, req.Text);
        Assert.True(req.Interrupt);
    }

    [Fact]
    public void OnSpeakGetterAndSetterForwardToInner()
    {
        var inner = Substitute.For<ISpeechManager>();
        var bus = new CapturingEventBus();
        var sut = MakeBrowserBackend(inner, bus);

        Action<string> callback = _ => { };
        sut.OnSpeak = callback;

        // The decorator's OnSpeak setter must forward to the inner so
        // MainLayout's OnInitializedAsync wiring (which sets OnSpeak to
        // route into the ARIA live region) lands on the real
        // BlazorSpeechManager, not on the decorator's nullable backing
        // field which would never fire.
        inner.Received(1).OnSpeak = callback;

        // Getter should read back from inner too.
        inner.OnSpeak.Returns(callback);
        Assert.Same(callback, sut.OnSpeak);
    }

    [Fact]
    public void IsSpeechEnabledForwardsToInner()
    {
        var inner = Substitute.For<ISpeechManager>();
        var bus = new CapturingEventBus();
        var sut = MakeBrowserBackend(inner, bus);

        sut.IsSpeechEnabled = false;
        inner.Received(1).IsSpeechEnabled = false;

        inner.IsSpeechEnabled.Returns(true);
        Assert.True(sut.IsSpeechEnabled);
    }
}
