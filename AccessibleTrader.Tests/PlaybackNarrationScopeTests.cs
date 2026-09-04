using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// PLAYBACK SPEECH IS SCOPED THE WAY THE TONES ARE.
///
/// <para>
/// Cody, 2026-09-04: <i>"does it make sense that, just like sonification per
/// chart/series/component, speech should do the same — so if I play back only a series I should
/// hear that narrated"</i>. It does, and the answer is that it was a bug rather than a missing
/// feature: <c>SignalsForStep</c> walked <c>ActiveSeries</c> with nothing telling it what was
/// playing, so Space, Shift+Space and the component play all three narrated the whole chart.
/// Playing one component of one indicator and hearing another indicator's signals is the loudest
/// available contradiction of what the key was for.
/// </para>
///
/// <para>
/// The scoping authority is the PLAN, not <c>PlaybackScope</c> — the plan is what the sequencer
/// was handed, and the start sentence is already read from it, so words and tones cannot end up
/// describing different series.
/// </para>
/// </summary>
public class PlaybackNarrationScopeTests
{
    [Fact]
    public void ChartScope_NarratesEverySeriesThatIsFlagged()
    {
        // The baseline the other cases narrow. Both fire on bar 5, both are flagged, both speak —
        // and each carries its name, because two clauses from different sources in one breath is
        // the case the name prefix exists for.
        var state = TwoSeries();
        var plan = PlaybackPlan.Resolve(state with { PlaybackScope = PlaybackScope.Chart }, PlaybackScope.Chart);

        string? spoken = PlaybackNarration.SignalsForStep(state, 5, plan);

        Assert.NotNull(spoken);
        Assert.Contains("Alpha", spoken);
        Assert.Contains("Beta", spoken);
    }

    [Fact]
    public void SeriesScope_SaysNothingAboutTheSeriesYouDidNotPlay()
    {
        // The report, exactly. Shift+Space on Alpha plays Alpha; Beta is not sounding, so Beta
        // has nothing to say about the bar the cursor is on.
        var state = TwoSeries() with { FocusedSeriesId = "alpha", PlaybackScope = PlaybackScope.Series };
        var plan = PlaybackPlan.Resolve(state, PlaybackScope.Series);

        string? spoken = PlaybackNarration.SignalsForStep(state, 5, plan);

        Assert.NotNull(spoken);
        Assert.DoesNotContain("Beta", spoken);
        Assert.Contains("alpha signal", spoken);
    }

    [Fact]
    public void ComponentScope_SaysNothingAboutTheOtherComponentsOfTheSameSeries()
    {
        // One step narrower, and the step that matters most: within a series the components are
        // what the user is picking between, so a component play that still recites the series'
        // other signals has narrowed the sound and not the speech.
        //
        // Bar 7, where ONLY Alpha's second component fires. Pinned to the first, the step is
        // silent; pinned to the second, it speaks. Two assertions rather than one because a
        // SignalsForStep that had simply stopped working would satisfy the first alone.
        var baseState = TwoSeries() with
        {
            FocusedSeriesId = "alpha",
            PlaybackScope = PlaybackScope.Component,
        };

        var onFirst = baseState with { FocusedComponentIndex = 0 };
        Assert.Null(PlaybackNarration.SignalsForStep(
            onFirst, 7, PlaybackPlan.Resolve(onFirst, PlaybackScope.Component)));

        var onSecond = baseState with { FocusedComponentIndex = 1 };
        string? spoken = PlaybackNarration.SignalsForStep(
            onSecond, 7, PlaybackPlan.Resolve(onSecond, PlaybackScope.Component));
        Assert.Equal("alpha second at 107.00.", spoken);
    }

    [Fact]
    public void NoPlan_StillScansTheWholeChart()
    {
        // The vacuity guard on all three above. A null plan has to keep the old behaviour, or
        // the tests would pass just as well against a SignalsForStep that returned null.
        var state = TwoSeries();

        string? spoken = PlaybackNarration.SignalsForStep(state, 5, null);

        Assert.NotNull(spoken);
        Assert.Contains("Alpha", spoken);
        Assert.Contains("Beta", spoken);
    }

    [Fact]
    public void TheCaveat_AsksAboutTheSeriesYouPLAYED_NotTheChart()
    {
        // The disclosure and the scan have to agree about what is in scope, or the one sentence
        // that explains a silence goes missing in precisely the case it was written for: play an
        // UNflagged series while some other series on the chart is flagged, and "no series is set
        // to narrate" was false about the chart and true about what you were listening to.
        var state = TwoSeries();
        var alpha = state.ActiveSeries.First(s => s.Id == "alpha");
        alpha.IsAutoNarrated = false;                      // Beta stays flagged

        state = state with { FocusedSeriesId = "alpha", PlaybackScope = PlaybackScope.Series };
        var plan = PlaybackPlan.Resolve(state, PlaybackScope.Series);

        Assert.Contains("No series is set to narrate", PlaybackNarration.SilentSignalsCaveat(state, plan));
        // ...and unscoped it still finds Beta and stays quiet, which is what makes the line above
        // evidence of the scoping rather than of an empty chart.
        Assert.Equal("", PlaybackNarration.SilentSignalsCaveat(state, null));
    }

    // ── Scaffolding ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Alpha (two signal components) and Beta (one), both flagged.
    ///
    /// <para>
    /// Alpha's second component fires on bar 7 rather than 5, and that is load-bearing: the
    /// utterance caps at two clauses, so an Alpha firing twice on bar 5 would fill the cap by
    /// itself and a chart-scope test would find Beta missing for a reason that has nothing to do
    /// with scope.
    /// </para>
    /// </summary>
    private static WorkspaceState TwoSeries()
    {
        var alpha = Series("alpha", "Alpha", ("Signal", "alpha signal at {price}", 5),
                                             ("Second", "alpha second at {price}", 7));
        var beta = Series("beta", "Beta", ("Signal", "beta signal at {price}", 5));

        return WorkspaceState.Initial with
        {
            Data = Bars(20),
            ActiveSeries = ImmutableList.Create(alpha, beta),
            PrimarySeriesId = "alpha",
            FocusedSeriesId = "alpha",
            CurrentDataIndex = 5,
            ViewportStartIndex = 0,
            ViewportLength = 20,
            NarrateDuringPlayback = true,
            IsPlaying = true,
        };
    }

    private static ChartSeries Series(string id, string name, params (string Comp, string Template, int Bar)[] comps)
    {
        var cfg = new SeriesConfig
        {
            Id = id, Name = name, FriendlyName = name, IndicatorCode = id.ToUpperInvariant(),
            IsAutoNarrated = true, IsVisible = true, IsMuted = false,
        };
        var buf = new SeriesDataBuffer { SeriesId = id };
        foreach (var (comp, template, bar) in comps)
        {
            cfg.Components.Add(new ComponentConfig
            {
                Name = comp, DisplayName = comp, IsVisible = true,
                DisplayType = ComponentDisplayType.Dot,
                SignalSpeechTemplate = template,
            });
            var arr = new double[20];
            Array.Fill(arr, double.NaN);
            arr[bar] = 1.0;
            buf.ComponentData[comp] = arr;
        }
        return new ChartSeries(cfg, buf);
    }

    private static TimeSeriesBuffer<Ohlcv> Bars(int n)
    {
        var start = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        return new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, n)
            .Select(i => new Ohlcv(start.AddDays(i), 100 + i, 101 + i, 99 + i, 100 + i, 1000)));
    }
}
