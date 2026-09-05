using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// The pane model, and the speech form Cody asked for on 2026-09-04.
///
/// <para><b>The model.</b> A PANE IS A Y AXIS — <c>ChartRenderer</c> groups the series list by
/// <see cref="ChartSeries.Pane"/> and gives each group its own axis and its own band. A SUB-PANE
/// is a strip inside a pane, declared by a component but collected by the renderer across every
/// series in the pane. Navigation used to disagree with that on both counts: it read the pane
/// list off ONE series' components, so Alt+PageUp on the candles said "No sub-panes in Candles"
/// while the chart had two more panes on it, and Ctrl+Up/Down could not reach a price overlay
/// drawn on top of the candles against the same axis.</para>
///
/// <para><b>The speech form.</b> Cody: <i>"Speech form, trailing, not prepend. I noticed the
/// hide/mute is prepended but I think it would be best n last."</i> The pane is a fact about the
/// MOVE rather than about the bar, it does not change from bar to bar, and putting it first
/// pushes the value late in every utterance. It trails, and only on change.</para>
///
/// <para><b>Except hidden and muted, revised 2026-09-05.</b> Cody, after living with it:
/// <i>"I was ok with hidden and muted being prefixed and narration being a suffix during nav…
/// if possible, put muted and hidden at the beginning, narrating at the end."</i> That is the
/// form the COMPONENT readout has had since 2026-09-04 (<c>SpeechFormatter</c>), and the series
/// readout now matches it: hidden and muted explain a SILENCE, and whatever interrupts an
/// utterance cuts its end, so they lead; narrating is an addition and trails. The two are also
/// independent flags rather than one three-way choice — the if/else chain this replaces could
/// say only one word of "hidden", "muted", "narrating" per series.</para>
/// </summary>
public sealed class PaneModelAndTrailingSpeechTests
{
    // ── The structure ────────────────────────────────────────────────────────────

    private static ChartSeries Series(string id, string name, string pane, params (string Name, string? Strip)[] comps)
    {
        var cfg = new SeriesConfig { Id = id, Name = name, FriendlyName = name, Pane = pane, IsVisible = true, Volume = 1f };
        var buf = new SeriesDataBuffer { SeriesId = id };
        foreach (var (n, strip) in comps)
        {
            cfg.Components.Add(new ComponentConfig
            {
                Name = n, DisplayName = n, DisplayType = ComponentDisplayType.Line,
                IsVisible = true, IsEnabled = true, Volume = 1f, SubPaneName = strip,
            });
            buf.ComponentData[n] = new[] { 1.0, 2.0 };
        }
        return new ChartSeries(cfg, buf);
    }

    /// <summary>
    /// Main is drawn first whatever order the series were added in, and everything else follows
    /// in first-appearance order. This is the ordering Page Up / Page Down now walks, and the
    /// reason it needed fixing: <c>ActiveSeries</c> is append-ordered, so adding an oscillator
    /// and THEN a second price overlay left the flat list saying price, oscillator, price while
    /// the picture said price, price, oscillator.
    /// </summary>
    [Fact]
    public void Panes_are_ordered_Main_first_then_first_appearance()
    {
        var series = ImmutableList.Create(
            Series("rsi", "RSI", "Pane_RSI", ("RSI", null)),
            Series("candles", "Candles", "Main", ("Close", null)),
            Series("price", "Price", "Main", ("Line", null)),
            Series("vol", "Volume", "Volume", ("Vol", null)));

        var panes = ChartPaneModel.Panes(series);

        Assert.Equal(new[] { "Main", "Pane_RSI", "Volume" }, panes.Select(p => p.Key).ToArray());
        Assert.Equal(new[] { "candles", "price" }, panes[0].Series.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void Series_in_visual_order_puts_both_Main_series_before_the_indicator()
    {
        var series = ImmutableList.Create(
            Series("candles", "Candles", "Main", ("Close", null)),
            Series("rsi", "RSI", "Pane_RSI", ("RSI", null)),
            Series("price", "Price", "Main", ("Line", null)));

        Assert.Equal(new[] { "candles", "price", "rsi" },
            ChartPaneModel.SeriesInVisualOrder(series).Select(s => s.Id).ToArray());
    }

    /// <summary>A pane key is a machine string; a pane's NAME is what the user called it by.</summary>
    [Theory]
    [InlineData("Main", "Main")]
    [InlineData("Pane_CIPHER_B", "Cipher B")]
    public void A_pane_key_is_not_spoken_raw(string key, string expected)
        => Assert.Equal(expected, ChartPaneModel.DisplayName(key, Array.Empty<ChartSeries>()));

    [Fact]
    public void A_pane_holding_one_series_is_named_by_that_series()
        => Assert.Equal("Relative Strength Index",
            ChartPaneModel.DisplayName("Pane_RSI",
                new[] { Series("rsi", "Relative Strength Index", "Pane_RSI", ("RSI", null)) }));

    /// <summary>
    /// The strip is collected across EVERY series in the pane, not just the one that declared it.
    /// That is how the renderer draws it, and the two disagreeing was the whole defect.
    /// </summary>
    [Fact]
    public void Strips_are_collected_across_every_series_in_the_pane()
    {
        var paneSeries = new[]
        {
            Series("a", "A", "Pane_X", ("Wave", null)),
            Series("b", "B", "Pane_X", ("Money Flow Wave", "MF")),
        };

        Assert.Equal(new[] { "MF" }, ChartPaneModel.SubPaneKeys(paneSeries).ToArray());
        Assert.Equal("Money Flow Wave", ChartPaneModel.SubPaneDisplayName("MF", paneSeries));
    }

    // ── The trailing speech form ─────────────────────────────────────────────────

    private static string SwitchOnto(ChartSeries target, ImmutableList<ChartSeries> all, bool visible = true, bool muted = false)
    {
        var cfg = target.Config;
        cfg.IsVisible = visible;
        cfg.IsMuted = muted;

        var bars = new List<Ohlcv>
        {
            new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100, 101, 99, 100.5, 10),
            new(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 100, 102, 99, 101.5, 10),
        };
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = all,
            PrimarySeriesId = "candles",
            FocusedSeriesId = target.Id,
            FocusedComponentIndex = 0,
            CurrentDataIndex = 0,
            ViewportStartIndex = 0,
            ViewportLength = 2,
            LastInteractionContext = InteractionContext.Series,
        };

        var router = new Router();
        new NavigationFeedbackManager(router, new SpeechFormatter())
            .HandleNavigationFeedback(state, false, false, "NAV_SERIES_NEXT");
        return string.Join(" ", router.Said);
    }

    private sealed class Router : ISpeechFeedbackRouter
    {
        public List<string> Said { get; } = new();
        public void Speak(string message, bool interrupt = true, SpeechChannel channel = SpeechChannel.Manual)
            => Said.Add(message);
        public void SpeakPoint(WorkspaceState state, WorkspaceState? previous, ChartSeries series, Ohlcv point, string prefix = "")
            => Said.Add(new SpeechFormatter().FormatPointFeedback(state, false, true, series, point, prefix));
        public void SpeakProfile(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int bin, string pfx = "") { }
        public void SpeakHeatmap(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int di, int bin, string pfx = "") { }
    }

    /// <summary>
    /// The pane name TRAILS. Cody asked for this form explicitly, over the prepend-on-change
    /// convention the codebase had been using.
    /// </summary>
    [Fact]
    public void The_pane_name_comes_last_not_first()
    {
        var rsi = Series("rsi", "RSI", "Pane_RSI", ("RSI", null));
        var all = ImmutableList.Create(Series("candles", "Candles", "Main", ("Close", null)), rsi);

        string said = SwitchOnto(rsi, all);

        int name = said.IndexOf("RSI", StringComparison.Ordinal);
        int pane = said.IndexOf("pane", StringComparison.OrdinalIgnoreCase);
        Assert.True(pane > name,
            $"the pane clause must trail the reading, not lead it — got \"{said}\"");
        Assert.False(said.TrimStart().StartsWith("RSI pane", StringComparison.OrdinalIgnoreCase),
            $"the utterance still opens with the pane name — got \"{said}\"");
    }

    /// <summary>
    /// Hidden LEADS — Cody's 2026-09-05 revision of "best n last". A series that makes no sound
    /// says so before it says anything else, because an interruption takes the end of a sentence
    /// and not its beginning.
    /// </summary>
    [Fact]
    public void Hidden_is_the_first_thing_said_not_the_last()
    {
        var rsi = Series("rsi", "RSI", "Pane_RSI", ("RSI", null));
        var all = ImmutableList.Create(Series("candles", "Candles", "Main", ("Close", null)), rsi);

        string said = SwitchOnto(rsi, all, visible: false).Trim();

        Assert.StartsWith("Hidden.", said, StringComparison.Ordinal);
        // Not welded to the name either — that was the 2026-09-04 form, "RSI, hidden."
        Assert.DoesNotContain("RSI, hidden", said, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Both flags, in one clause, in front. The if/else chain this replaces could say only one
    /// of them, so a series that was hidden AND muted told the user to press one key when it
    /// needed two — the same defect VisibilityStateSpeech was written for at component level.
    /// </summary>
    [Fact]
    public void Hidden_and_muted_are_both_said_and_they_lead()
    {
        var rsi = Series("rsi", "RSI", "Pane_RSI", ("RSI", null));
        var all = ImmutableList.Create(Series("candles", "Candles", "Main", ("Close", null)), rsi);

        string said = SwitchOnto(rsi, all, visible: false, muted: true).Trim();

        Assert.StartsWith("Hidden and muted.", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// And narration TRAILS, in the same utterance as a leading visibility clause when both
    /// apply. A series can carry the narration flag while hidden — that pairing is the answer to
    /// "why has this stopped talking", so both halves are spoken.
    /// </summary>
    [Fact]
    public void Narrating_is_the_last_thing_said()
    {
        var rsi = Series("rsi", "RSI", "Pane_RSI", ("RSI", null));
        rsi.IsAutoNarrated = true;
        var all = ImmutableList.Create(Series("candles", "Candles", "Main", ("Close", null)), rsi);

        string said = SwitchOnto(rsi, all).Trim();
        Assert.EndsWith("Narrating.", said, StringComparison.Ordinal);

        string bothWays = SwitchOnto(rsi, all, muted: true).Trim();
        Assert.StartsWith("Muted.", bothWays, StringComparison.Ordinal);
        Assert.EndsWith("Narrating.", bothWays, StringComparison.Ordinal);
    }

    /// <summary>
    /// Vacuity guard for the pair above: a series with neither flag says neither word.
    /// </summary>
    [Fact]
    public void A_plain_series_says_neither_word()
    {
        var rsi = Series("rsi", "RSI", "Pane_RSI", ("RSI", null));
        var all = ImmutableList.Create(Series("candles", "Candles", "Main", ("Close", null)), rsi);

        string said = SwitchOnto(rsi, all).Trim();

        Assert.DoesNotContain("Hidden", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Muted", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Narrating", said, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Vacuity guard for the pair above: a trailing clause that is never emitted would satisfy
    /// "does not lead" trivially. The pane has to actually be named.
    /// </summary>
    [Fact]
    public void The_pane_is_actually_named()
    {
        var rsi = Series("rsi", "RSI", "Pane_RSI", ("RSI", null));
        var all = ImmutableList.Create(Series("candles", "Candles", "Main", ("Close", null)), rsi);

        Assert.Contains("RSI pane", SwitchOnto(rsi, all), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The sub-pane COUNT is gone from the series switch. It counted strips inside one series
    /// while calling them panes — so a chart with a volume pane and an oscillator pane under it
    /// said "1 pane" — and a count is only information when it tells the user a key has somewhere
    /// to go, which stopped being true when sub-pane navigation was retired.
    /// </summary>
    [Fact]
    public void The_series_switch_no_longer_counts_sub_panes_as_panes()
    {
        var cipher = Series("cipher", "Cipher B", "Pane_CIPHER_B", ("Wave", null), ("MFW", "MF"));
        var all = ImmutableList.Create(Series("candles", "Candles", "Main", ("Close", null)), cipher);

        string said = SwitchOnto(cipher, all);

        Assert.DoesNotContain("2 panes", said, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 components", said, StringComparison.OrdinalIgnoreCase);
    }
}
