using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Playback that will not speak a single signal says so, once, at the moment you press play.
///
/// <para>
/// Cody, 2026-09-04: <i>"when I added cipher sr or b to the chart I don't hear signals being
/// spoken during playback"</i>. Both indicators exist to print signals; both were silent.
/// <c>SeriesConfig.IsAutoNarrated</c> defaults to FALSE and nothing sets it when an indicator is
/// added, so <c>PlaybackNarration.SignalsForStep</c> skipped every series on the chart.
/// </para>
///
/// <para>
/// The default is deliberate and is not changed by any of this: the standing convention here is
/// that continuous verbal output is opted into rather than imposed, and turning it on for every
/// added indicator would make a chart carrying four of them unlistenable. What was never
/// deliberate is the silence being indistinguishable from a broken feature — a user who switched
/// narration on in Settings, added a signal indicator and pressed play has done everything the
/// feature asks, and the one remaining gate is a per-series flag they have no way to know exists.
/// </para>
/// </summary>
public class PlaybackSilentSignalsTests
{
    [Fact]
    public void ASignalIndicatorNobodyFlagged_IsDisclosedRatherThanSilentlySkipped()
    {
        var state = WithSeries(SignalSeries("cipher_b", "Cipher B", autoNarrated: false));

        string caveat = PlaybackNarration.SilentSignalsCaveat(state);

        Assert.Contains("No series is set to narrate", caveat);
        // Actionable, not merely apologetic: the remedy is one chord and the sentence names it.
        Assert.Contains("Control Alt Shift N", caveat);
    }

    [Fact]
    public void OnceOneSeriesIsFlagged_NothingIsSaid()
    {
        // The caveat is about a gate that is closed. With it open the user hears the signals
        // themselves, and repeating the shortcut at them every time they press play is the noise
        // the opt-in convention exists to prevent.
        var state = WithSeries(SignalSeries("cipher_b", "Cipher B", autoNarrated: true));

        Assert.Equal("", PlaybackNarration.SilentSignalsCaveat(state));
    }

    [Fact]
    public void AChartWithNoSignalsToMiss_IsNotToldAboutAShortcutItDoesNotNeed()
    {
        // THE GUARD THAT KEEPS THIS FROM BECOMING NOISE. A chart of plain moving averages has no
        // signals to narrate whether or not a series is flagged, so the caveat would be advice
        // about a feature the user is not missing — attached to every press of play.
        var plain = new SeriesConfig { Id = "ema", Name = "EMA 20", IndicatorCode = "EMA" };
        plain.Components.Add(new ComponentConfig
        {
            Name = "EMA", DisplayName = "EMA", IsVisible = true,
            DisplayType = ComponentDisplayType.Line,
        });
        var state = WithSeries(new ChartSeries(plain, Buffer("ema", "EMA")));

        Assert.Equal("", PlaybackNarration.SilentSignalsCaveat(state));
    }

    [Fact]
    public void AMarkerWithNoSpeechTemplate_IsNotSomethingToMissEither()
    {
        // SignalsForStep requires a template to have anything to say, so a marker without one is
        // silent for a reason that flagging the series would not fix. Promising otherwise sends
        // the user to a shortcut that changes nothing.
        var cfg = new SeriesConfig { Id = "x", Name = "Marks", IndicatorCode = "X" };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Dot", DisplayName = "Dot", IsVisible = true,
            DisplayType = ComponentDisplayType.Dot,     // a marker, but no SignalSpeechTemplate
        });
        var state = WithSeries(new ChartSeries(cfg, Buffer("x", "Dot")));

        Assert.Equal("", PlaybackNarration.SilentSignalsCaveat(state));
    }

    [Fact]
    public void WithPlaybackNarrationTurnedOff_NothingIsSaid()
    {
        // The user has switched the whole feature off. Telling them how to un-silence a part of
        // it is arguing with a preference they set.
        var state = WithSeries(SignalSeries("cipher_b", "Cipher B", autoNarrated: false))
            with { NarrateDuringPlayback = false };

        Assert.Equal("", PlaybackNarration.SilentSignalsCaveat(state));
    }

    [Fact]
    public void AHiddenOrMutedSignalSeries_IsNotCountedAsSomethingToMiss()
    {
        // SignalsForStep skips hidden and muted series regardless of the flag, so neither is a
        // series whose signals the flag is withholding.
        var hidden = SignalSeries("cipher_b", "Cipher B", autoNarrated: false);
        hidden.IsVisible = false;
        Assert.Equal("", PlaybackNarration.SilentSignalsCaveat(WithSeries(hidden)));

        var muted = SignalSeries("cipher_sr", "Cipher SR", autoNarrated: false);
        muted.IsMuted = true;
        Assert.Equal("", PlaybackNarration.SilentSignalsCaveat(WithSeries(muted)));
    }

    // ── Scaffolding ─────────────────────────────────────────────────────────────

    private static SeriesDataBuffer Buffer(string id, params string[] components)
    {
        var buf = new SeriesDataBuffer { SeriesId = id };
        foreach (var c in components) buf.ComponentData[c] = new double[100];
        return buf;
    }

    /// <summary>A series shaped like Cipher B: a dot marker carrying a spoken template.</summary>
    private static ChartSeries SignalSeries(string id, string name, bool autoNarrated)
    {
        var cfg = new SeriesConfig
        {
            Id = id, Name = name, FriendlyName = name, IndicatorCode = id.ToUpperInvariant(),
            IsAutoNarrated = autoNarrated,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Buy", DisplayName = "Buy", IsVisible = true,
            DisplayType = ComponentDisplayType.Dot,
            SignalSpeechTemplate = "buy signal at {price}",
        });
        return new ChartSeries(cfg, Buffer(id, "Buy"));
    }

    private static WorkspaceState WithSeries(ChartSeries series) => WorkspaceState.Initial with
    {
        ActiveSeries = ImmutableList.Create(series),
        NarrateDuringPlayback = true,
    };
}
