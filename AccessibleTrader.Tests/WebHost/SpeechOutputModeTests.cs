using System;
using System.Collections.Generic;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The hosted double-speech fix. On browser-TTS deploys speech used to go out
/// BOTH ways — the ARIA live region (read by the visitor's screen reader) AND
/// window.speechSynthesis — so Orca users heard everything twice. Browsers
/// deliberately don't expose screen-reader presence, so the fix is an explicit
/// per-browser mode; these tests pin each mode's routing and the pre-choice
/// default (Both — nobody gets silence before choosing).
/// </summary>
public class SpeechOutputModeTests
{
    private sealed class CapturingEventBus : IEventBus
    {
        public List<object> Published { get; } = new();
        public void Publish<T>(T eventData) { if (eventData is not null) Published.Add(eventData); }
        public IDisposable Subscribe<T>(Action<T> handler) => new Dummy();
        public IObservable<T> AsObservable<T>() => System.Reactive.Linq.Observable.Empty<T>();
        public IDisposable SubscribeCoalesced<T>(Action<T> handler, TimeSpan quietWindow) => new Dummy();
        public IDisposable SubscribeSampled<T>(Action<T> handler, TimeSpan window) => new Dummy();
        private sealed class Dummy : IDisposable { public void Dispose() { } }
    }

    private static WebHostSpeechManager BrowserBackend(ISpeechManager inner, IEventBus bus)
        => new(inner, bus, NullLogger<WebHostSpeechManager>.Instance,
               spdSayPath: null, gdbusPath: null, orcaAvailable: false);

    private static BlazorSpeechManager NewInner()
        => new(NullLogger<BlazorSpeechManager>.Instance,
               new ServiceCollection().BuildServiceProvider());

    // ── Mode routing on the browser-TTS backend ──────────────────────────────

    [Fact]
    public void Default_mode_is_Both_and_publishes_browser_speech()
    {
        var inner = Substitute.For<ISpeechManager>();
        inner.IsSpeechEnabled.Returns(true);
        var bus = new CapturingEventBus();
        var sut = BrowserBackend(inner, bus);

        Assert.Equal(SpeechOutputMode.Both, sut.Mode);
        sut.Speak("hello", interrupt: false);

        inner.Received(1).Speak("hello", false);        // live region path intact
        Assert.Single(bus.Published);                    // browser voice too
    }

    [Fact]
    public void ScreenReader_mode_keeps_the_live_region_but_never_publishes_browser_speech()
    {
        var inner = Substitute.For<ISpeechManager>();
        inner.IsSpeechEnabled.Returns(true);
        var bus = new CapturingEventBus();
        var sut = BrowserBackend(inner, bus);

        sut.Mode = SpeechOutputMode.ScreenReader;
        sut.Speak("hello", interrupt: false);

        inner.Received(1).Speak("hello", false);        // Orca reads THIS
        Assert.Empty(bus.Published);                     // no doubled voice
    }

    [Fact]
    public void BrowserVoice_mode_silences_the_live_region_and_keeps_the_browser_voice()
    {
        // Real inner manager so the LiveRegionEnabled side-effect is observable.
        var inner = NewInner();
        var spoken = new List<string>();
        inner.OnSpeak = spoken.Add;
        var bus = new CapturingEventBus();
        var sut = BrowserBackend(inner, bus);

        sut.Mode = SpeechOutputMode.BrowserVoice;
        sut.Speak("hello", interrupt: false);

        Assert.Empty(spoken);                            // live region stays empty
        Assert.Single(bus.Published);                    // browser voice speaks
    }

    [Fact]
    public void Switching_back_from_BrowserVoice_restores_the_live_region()
    {
        var inner = NewInner();
        var spoken = new List<string>();
        inner.OnSpeak = spoken.Add;
        var bus = new CapturingEventBus();
        var sut = BrowserBackend(inner, bus);

        sut.Mode = SpeechOutputMode.BrowserVoice;
        sut.Mode = SpeechOutputMode.ScreenReader;
        sut.Speak("hello", interrupt: false);

        Assert.Equal(new[] { "hello" }, spoken);
    }

    [Fact]
    public void Capability_reports_browser_backend_truthfully()
    {
        var bus = new CapturingEventBus();
        IBrowserSpeechOutput viaBrowser = BrowserBackend(Substitute.For<ISpeechManager>(), bus);
        Assert.True(viaBrowser.IsBrowserTtsBackend);

        // spd-say present → server-side speech → the mode UI must not appear.
        IBrowserSpeechOutput viaSpd = new WebHostSpeechManager(
            Substitute.For<ISpeechManager>(), bus, NullLogger<WebHostSpeechManager>.Instance,
            spdSayPath: "/usr/bin/spd-say", gdbusPath: null, orcaAvailable: false);
        Assert.False(viaSpd.IsBrowserTtsBackend);
    }

    // ── BlazorSpeechManager.LiveRegionEnabled contract ───────────────────────

    [Fact]
    public void LiveRegionEnabled_false_skips_OnSpeak_and_does_not_queue()
    {
        var inner = NewInner();
        var spoken = new List<string>();
        inner.OnSpeak = spoken.Add;

        inner.LiveRegionEnabled = false;
        inner.Speak("suppressed");
        Assert.Empty(spoken);

        // Nothing queued while disabled: re-enabling must not replay stale text.
        inner.LiveRegionEnabled = true;
        inner.OnSpeak = spoken.Add; // re-set triggers queued-text flush if any
        Assert.Empty(spoken);

        inner.Speak("audible");
        Assert.Equal(new[] { "audible" }, spoken);
    }
}
