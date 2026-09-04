using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Hidden and muted are TWO flags, and the terminal says both.
///
/// <para><b>The defect, reported from real use on 2026-09-04.</b> Cody: "if I hide and mute both
/// at once, if I unhide it should say muted but it doesn't. If both hidden and muted, then both
/// hidden and muted should be reported when I up/down over the components, and when one is
/// unmuted or unhidden, only that qualifier should be removed from being spoken."</para>
///
/// <para><b>What it was.</b> A lattice of four states reported as a chain of two.
/// <c>ProviderSpeechStrategy</c> built its label as
/// <c>!IsVisible ? "Hidden. " : IsMuted ? "Muted. " : ""</c> — an if/else over two INDEPENDENT
/// flags, so hidden always won and a component that was both never said "muted";
/// <c>HiddenComponentStrategy</c> hard-coded the word and could not mention mute at all; the
/// other EIGHT strategies said nothing about either; and the toggle confirmations announced only
/// the flag they had just flipped, so unhiding something muted said "visible" about a component
/// that stays silent.</para>
///
/// <para><b>Why it is worth a test file.</b> The two flags fail identically from the user's side
/// — no sound — and are cleared by different keys. Being told "visible" by a component that makes
/// no sound sends the user to press h again, hiding it, which is further from what they wanted
/// than where they started.</para>
/// </summary>
public sealed class HiddenAndMutedAreBothSpokenTests
{
    // ── The vocabulary, all four cells ───────────────────────────────────────────

    [Theory]
    [InlineData(false, true,  "hidden and muted")]
    [InlineData(false, false, "hidden")]
    [InlineData(true,  true,  "muted")]
    [InlineData(true,  false, "")]
    public void Every_combination_of_the_two_flags_has_its_own_words(bool visible, bool muted, string expected)
        => Assert.Equal(expected, VisibilityStateSpeech.Qualifier(visible, muted));

    [Fact]
    public void The_navigation_prefix_capitalises_and_terminates_it()
    {
        Assert.Equal("Hidden and muted. ", VisibilityStateSpeech.Prefix(false, true));
        Assert.Equal("Muted. ", VisibilityStateSpeech.Prefix(true, true));
        Assert.Equal("", VisibilityStateSpeech.Prefix(true, false));
    }

    // ── The navigation readout ───────────────────────────────────────────────────

    private static ChartSeries SeriesWith(bool visible, bool muted)
    {
        var cfg = new SeriesConfig
        {
            Id = "rsi", Name = "RSI", FriendlyName = "RSI",
            IndicatorCode = "RSI", Pane = "Sub", IsVisible = true, Volume = 1f,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "rsi", DisplayName = "RSI", DisplayType = ComponentDisplayType.Line,
            IsVisible = visible, IsMuted = muted, IsEnabled = true, Volume = 1f,
        });

        var buf = new SeriesDataBuffer { SeriesId = "rsi" };
        buf.ComponentData["rsi"] = new[] { 64.0 };
        return new ChartSeries(cfg, buf);
    }

    private static string YMoveReadout(bool visible, bool muted)
    {
        var series = SeriesWith(visible, muted);
        var bars = new List<Ohlcv> { new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100, 101, 99, 100.5, 10) };
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = "rsi",
            FocusedSeriesId = "rsi",
            FocusedComponentIndex = 0,
            CurrentDataIndex = 0,
            ViewportStartIndex = 0,
            ViewportLength = 1,
            LastInteractionContext = InteractionContext.Component,
        };

        var router = new CapturingSpeechRouter();
        new NavigationFeedbackManager(router, new SpeechFormatter())
            .HandleNavigationFeedback(state, false, true, "NAV_MOVE");
        return string.Join(" ", router.Said);
    }

    /// <summary>The headline: both flags on, both words spoken. This is the sentence the if/else
    /// could not produce.</summary>
    [Fact]
    public void Both_flags_on_says_both_words()
        => Assert.Contains("Hidden and muted", YMoveReadout(visible: false, muted: true),
            StringComparison.Ordinal);

    /// <summary>
    /// …and only the cleared one goes away. Cody's "if I unhide it should say muted": a component
    /// that is now visible but still muted must not read as though it were audible.
    /// </summary>
    [Fact]
    public void Unhiding_a_muted_component_still_says_muted()
    {
        string said = YMoveReadout(visible: true, muted: true);
        Assert.Contains("Muted", said, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Unmuting_a_hidden_component_still_says_hidden()
    {
        string said = YMoveReadout(visible: false, muted: false);
        Assert.Contains("Hidden", said, StringComparison.Ordinal);
        Assert.DoesNotContain("muted", said, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The vacuity floor. Without it every assertion above could pass on a build that prefixes
    /// every readout with a state word, or on one where the readout is empty.
    /// </summary>
    [Fact]
    public void A_plain_component_carries_no_qualifier_at_all()
    {
        string said = YMoveReadout(visible: true, muted: false);
        Assert.NotEmpty(said);
        Assert.DoesNotContain("hidden", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("muted", said, StringComparison.OrdinalIgnoreCase);
    }

    // ── The toggle confirmations ─────────────────────────────────────────────────

    /// <summary>
    /// The clause a toggle confirmation appends for the flag it did NOT touch. Asserted on the
    /// vocabulary rather than by driving ChartCommandManager, which needs a store, a dispatcher
    /// and a focused component; the four call sites are one expression each and are read in the
    /// same commit as this.
    /// </summary>
    [Fact]
    public void A_toggle_confirmation_names_the_flag_it_did_not_clear()
    {
        Assert.Equal(", muted", VisibilityStateSpeech.OtherFlagClause(true, "muted"));
        Assert.Equal(", hidden", VisibilityStateSpeech.OtherFlagClause(true, "hidden"));
        Assert.Equal("", VisibilityStateSpeech.OtherFlagClause(false, "muted"));
    }

    private sealed class CapturingSpeechRouter : ISpeechFeedbackRouter
    {
        public List<string> Said { get; } = new();
        public void Speak(string message, bool interrupt = true, SpeechChannel channel = SpeechChannel.Manual)
            => Said.Add(message);
        public void SpeakPoint(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, Ohlcv point, string prefix = "")
            => Said.Add(new SpeechFormatter().FormatPointFeedback(state, false, true, series, point, prefix));
        public void SpeakProfile(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int binIndex, string prefix = "") { }
        public void SpeakHeatmap(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int dataIndex, int binIndex, string prefix = "") { }
    }
}
