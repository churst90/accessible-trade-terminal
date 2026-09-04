using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Shift+F1 names the pane you are actually standing in.
///
/// <para>
/// Reported by Cody, 2026-09-04: "when I alt pg down to volume pane and do shift f1 it says
/// main pane, not volume pane". Two separate defects were behind that one sentence, and the
/// orientation key is the worst place in the app for either of them — it is what a disoriented
/// user reaches for, so a wrong answer here is worse than no answer.
/// </para>
///
/// <list type="number">
///   <item>
///     The pane name came from the focused COMPONENT's <c>SubPaneName</c>, with an empty answer
///     rendered as "main pane". But a pane is a Y axis, declared by <c>ChartSeries.Pane</c>
///     across the whole series list — the volume series' components declare no sub-pane at all,
///     so every series outside Main answered "main pane". This block was the last reader of the
///     sub-pane model that the sixteenth pass retired everywhere else.
///   </item>
///   <item>
///     The whole clause was gated on <c>LastInteractionContext == Component</c>, and
///     Alt+PageUp/PageDown dispatches <c>SetInteractionContextAction(Series)</c> — so
///     immediately after a pane move, the move whose entire purpose is to change which pane you
///     are in, Shift+F1 said the symbol and the timeframe and stopped.
///   </item>
/// </list>
/// </summary>
public sealed class ContextSummaryPaneTests
{
    [Fact]
    public void StandingInTheVolumePane_ItSaysVolumePane()
    {
        string said = Summary(FocusOn(CoreSeriesIds.Volume, InteractionContext.Series));

        Assert.Contains("Volume pane", said);
        Assert.DoesNotContain("main pane", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StandingInTheMainPane_ItStillSaysMainPane()
    {
        // Vacuity guard. Renaming every pane "Volume" would satisfy the test above and be a
        // worse defect than the one being fixed.
        string said = Summary(FocusOn(CoreSeriesIds.Candles, InteractionContext.Series));

        Assert.Contains("Main pane", said);
        Assert.DoesNotContain("Volume pane", said);
    }

    [Fact]
    public void AnIndicatorInItsOwnPane_IsNamedByThatPane()
    {
        // The third shape, and the one that proves the answer comes from ChartSeries.Pane rather
        // than from a two-case guess about volume: an indicator pane the model names from its
        // own series when the key is an opaque "Pane_..." string.
        string said = Summary(FocusOn("cipher_b", InteractionContext.Series));

        Assert.Contains("Cipher B pane", said);
        Assert.DoesNotContain("main pane", said, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(InteractionContext.Series)]
    [InlineData(InteractionContext.Component)]
    public void ThePaneIsNamedInEitherInteractionContext(InteractionContext ctx)
    {
        // The gate is the second defect. Alt+PageDown leaves the context on Series, so the clause
        // was skipped exactly when the user had just changed panes — the one moment "where am I?"
        // is being asked because the answer just changed.
        string said = Summary(FocusOn(CoreSeriesIds.Volume, ctx));

        Assert.Contains("Volume pane", said);
    }

    [Fact]
    public void ItSaysWhereThePaneSitsInTheStack()
    {
        // "Volume pane" alone does not say whether there is anything below it. A pane index is
        // the other half of orientation and the old clause never had it — what it had instead was
        // a count of the focused series' own sub-panes, reported as though it described the
        // chart, and therefore zero for every series that declares none.
        string said = Summary(FocusOn(CoreSeriesIds.Volume, InteractionContext.Series));

        Assert.Contains("Volume pane, 2 of 3", said);
    }

    [Fact]
    public void OnAOnePaneChart_NoIndexIsRecited()
    {
        // "1 of 1" is noise on every utterance of a chart that has nothing to move between.
        var candles = Series(CoreSeriesIds.Candles, "Candles", ChartPaneModel.MainPaneKey);
        string said = Summary(WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "Bitstamp", "BTC/USD", "1d"),
            ActiveSeries = ImmutableList.Create(candles),
            FocusedSeriesId = CoreSeriesIds.Candles,
            LastInteractionContext = InteractionContext.Series,
        });

        Assert.Contains("Main pane", said);
        Assert.DoesNotContain(" of 1", said);
    }

    [Fact]
    public void TheSymbolAndTimeframeStillLeadTheSentence()
    {
        // Everything above is an addition to an answer that already worked; a fix that dropped
        // the identity would pass all of it and lose the half a user is most often after.
        string said = Summary(FocusOn(CoreSeriesIds.Volume, InteractionContext.Series));

        Assert.StartsWith("BTC/USD on Bitstamp, 1d", said);
    }

    // ── Scaffolding ─────────────────────────────────────────────────────────

    /// <summary>
    /// Three panes, in the order the renderer draws them: Main (candles), Volume, Cipher B.
    /// </summary>
    private static WorkspaceState FocusOn(string seriesId, InteractionContext ctx)
    {
        var candles = Series(CoreSeriesIds.Candles, "Candles", ChartPaneModel.MainPaneKey);
        var volume  = Series(CoreSeriesIds.Volume,  "Volume",  "Volume");
        var cipher  = Series("cipher_b",            "Cipher B", "Pane_CIPHER_B");

        return WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "Bitstamp", "BTC/USD", "1d"),
            ActiveSeries = ImmutableList.Create(candles, volume, cipher),
            FocusedSeriesId = seriesId,
            FocusedComponentIndex = 0,
            LastInteractionContext = ctx,
        };
    }

    private static ChartSeries Series(string id, string name, string pane)
    {
        var cfg = new SeriesConfig { Id = id, Name = name, FriendlyName = name, Pane = pane };
        cfg.Components.Add(new ComponentConfig { Name = "Value", DisplayName = "Value", IsVisible = true });
        return new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = id });
    }

    private static string Summary(WorkspaceState state)
    {
        var bus = new SpyEventBus();
        var speech = new SpySpeechRouter();
        var store = new MockWorkspaceStore();
        store.EmitState(state);

        var coordinator = new AccessibilityFeedbackCoordinator(
            store, new MockNavManager(), speech, new MockAudioRouter(), new SpeechFormatter(),
            bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
            new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
            new ChartPatternFocus(), new MockAutoNarrationService());
        Assert.NotNull(coordinator);

        bus.Publish(new ContextSummaryRequestEvent());
        return Assert.Single(speech.SpokenTexts);
    }
}
