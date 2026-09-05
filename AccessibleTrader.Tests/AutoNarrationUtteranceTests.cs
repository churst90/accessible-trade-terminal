using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// One scan must produce one utterance carrying what the scan found, most consequential first.
///
/// <para>
/// <b>The bug this pins.</b> <see cref="AutoNarrationService"/> made up to nine separate
/// <c>Speak</c> calls inside a single <c>RedrawEvent</c> handler — a marker signal, a broken
/// level, a level tested again, an approach, a cross, a cloud entry or exit, an oscillator zone
/// change and an oscillator crossover, each one its own call. On the web head speech is delivered
/// by assigning <c>MainLayout</c>'s <c>_latestSpeech</c> field, Blazor batches an entire handler
/// into one render, so the field is assigned nine times and only the last value ever reaches the
/// DOM. The other eight were not muted or filtered — they were overwritten before a screen reader
/// could read any of them, and the survivor was whichever the scan happened to reach last rather
/// than whichever mattered. On the desktop head the failure inverts: all nine queue and the
/// listener cannot get out from under them.
/// </para>
///
/// <para>
/// Measured on the crowded scan below before the fix: <b>eight</b> Speak calls, of which the user
/// heard <c>"Money Flow Wave bullish crossover."</c> — while <c>"Resistance at 105.00 broken."</c>,
/// the most consequential thing this narrator says, was discarded.
/// </para>
///
/// <para>
/// This is the same defect <c>NavigationFeedbackManager</c> was fixed for, documented in its own
/// source at the "ONE UTTERANCE PER BAR" comment and pinned by
/// <see cref="NavigationUtteranceTests"/>. The narrator was not fixed with it.
/// </para>
///
/// <para>
/// The property that catches it is the <b>call count</b>, so that is what these assert.
/// Every existing assertion in <see cref="AutoNarrationTests"/> passed throughout, because they
/// all ask "was this phrase spoken at all" — a question eight discarded phrases still answer yes.
/// </para>
/// </summary>
public class AutoNarrationUtteranceTests
{
    // ── Test doubles ────────────────────────────────────────────────────────────

    /// <summary>
    /// Records the channel and the interrupt flag as well as the text. Composing must not quietly
    /// promote the narrator off <see cref="SpeechChannel.Event"/> — that is the channel F2 mutes,
    /// and a background narrator the user cannot silence is its own defect — nor make it
    /// interrupting, which would cut off whatever the user asked for.
    /// </summary>
    private sealed class CapturingSpeechRouter : ISpeechFeedbackRouter
    {
        public List<(string Text, bool Interrupt, SpeechChannel Channel)> Calls { get; } = new();
        public List<string> Spoken => Calls.Select(c => c.Text).ToList();

        public void Speak(string message, bool interrupt = false, SpeechChannel channel = SpeechChannel.Manual)
            => Calls.Add((message, interrupt, channel));
        public void SpeakPoint(WorkspaceState s, WorkspaceState? p, ChartSeries ser, Ohlcv pt, string pfx = "") { }
        public void SpeakProfile(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int bin, string pfx = "") { }
        public void SpeakHeatmap(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int di, int bin, string pfx = "") { }
    }

    /// <summary>
    /// Returns whatever context it was handed most recently, so the seeding pass and the scanning
    /// pass can see different oscillator states — which is the only way a zone transition and a
    /// crossover are reachable at all.
    /// </summary>
    private sealed class MutableContextAnalyzer : IIndicatorContextAnalyzer
    {
        public IndicatorContext? Context { get; set; }

        /// <summary>Only this series gets oscillator context; null means all of them.</summary>
        public string? OnlyForSeries { get; set; }

        private bool Applies(ChartSeries series)
            => Context != null && (OnlyForSeries == null || series.FriendlyName == OnlyForSeries);

        public void RegisterDefinition(IndicatorContextDefinition def) { }
        public bool HasZoneThresholds(string indicatorCode, string componentName) => false;
        public IndicatorContext? Analyze(ChartSeries series, WorkspaceState state)
            => Applies(series) ? Context : null;
        public IEnumerable<IndicatorContext> AnalyzeAll(ChartSeries series, WorkspaceState state)
            => Applies(series) ? new[] { Context! } : Enumerable.Empty<IndicatorContext>();
    }

    private static IndicatorContext Osc(ZoneStatus zone, CrossoverStatus crossover) => new()
    {
        IndicatorCode = "CIPHER_B",
        ComponentName = "Money Flow Wave",
        CurrentValue = 1,
        Trend = TrendDirection.Rising,
        TrendBars = 1,
        Zone = zone,
        Crossover = crossover,
        NarrativeHint = ""
    };

    // ── The scan sequence every fixture uses ────────────────────────────────────

    /// <summary>
    /// Seed at one bar, grow to three (nothing scannable yet), then to four — which scans bar
    /// index 2. Closes run 100, 100, 104, 104, so the moves all land on that one bar.
    /// </summary>
    private static CapturingSpeechRouter Drive(
        Func<int, WorkspaceState> stateAt,
        MutableContextAnalyzer analyzer,
        IndicatorContext? afterSeeding = null)
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        var router = new CapturingSpeechRouter();
        using var svc = new AutoNarrationService(store, bus, router, analyzer);

        store.EmitState(stateAt(1));
        bus.Publish(new RedrawEvent());       // seeds every dictionary; closedBound < 0, no scan

        store.EmitState(stateAt(3));
        bus.Publish(new RedrawEvent());       // scanFrom > closedBound — skipped

        if (afterSeeding != null) analyzer.Context = afterSeeding;

        store.EmitState(stateAt(4));
        bus.Publish(new RedrawEvent());       // scans bar index 2

        return router;
    }

    private static readonly double[] MovingCloses = { 100, 100, 104, 104 };
    private static readonly double[] FlatCloses = { 100, 100, 100, 100 };

    private static WorkspaceState StateFor(ImmutableList<ChartSeries> series, double[] closes, int barCount)
    {
        var bars = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, barCount).Select(i =>
            new Ohlcv(new DateTime(2026, 1, 1, 0, i, 0, DateTimeKind.Utc),
                      closes[i], closes[i] + 5, closes[i] - 5, closes[i], 1000)));

        return WorkspaceState.Initial with
        {
            Data = bars,
            ActiveSeries = series,
            FocusedSeriesId = series[0].Id,
            CurrentDataIndex = barCount - 1,
            InitStatus = InitializationStatus.Ready,
            DataStatus = DataStatus.Ready,
            IsSpeechEnabled = true
        };
    }

    private static double[] Take(double[] source, int count) => source.Take(count).ToArray();

    private static ComponentConfig Marker(string display, string? template = null) => new()
    {
        Name = "Signal", DisplayName = display, DisplayType = ComponentDisplayType.Dot,
        IsVisible = true, SignalSpeechTemplate = template
    };

    private static ComponentConfig ZoneLine(string name) => new()
    {
        Name = name, DisplayName = name, DisplayType = ComponentDisplayType.Line,
        IsVisible = true, IsZoneLine = true
    };

    // ── Fixture A: the crowded scan ─────────────────────────────────────────────

    /// <summary>
    /// The scan with the most to say: eight clauses off ONE series, so the call count cannot be
    /// explained away as "one utterance per series".
    ///
    /// <para>
    /// On bar index 2 (close 104): the marker dot prints; <c>Broken Zone</c> (105, a ceiling while
    /// the close was 100) goes NaN; the touch count on <c>Tested Zone</c> at 110 goes 1 → 3;
    /// <c>Near Zone</c> at 103.5 is inside the 0.5% proximity band AND has just been crossed from
    /// below; the cloud spanning 98–102 is exited upwards; and the oscillator moves
    /// Normal → Overbought with a bullish crossover.
    /// </para>
    /// </summary>
    private static CapturingSpeechRouter RunCrowdedScan()
    {
        // ONE config for every emission — the series id is what every tracking dictionary in
        // the service is keyed by, so rebuilding it per bar would re-seed instead of scan.
        var cfg = CrowdedConfig();
        var analyzer = new MutableContextAnalyzer { Context = Osc(ZoneStatus.Normal, CrossoverStatus.None) };
        return Drive(n => CrowdedState(cfg, n, quiet: false), analyzer,
                     afterSeeding: Osc(ZoneStatus.Overbought, CrossoverStatus.BullishCrossover));
    }

    /// <summary>The same series over the same sequence with nothing happening on any of it.</summary>
    private static CapturingSpeechRouter RunQuietScan()
    {
        var cfg = CrowdedConfig();
        var analyzer = new MutableContextAnalyzer { Context = Osc(ZoneStatus.Normal, CrossoverStatus.None) };
        return Drive(n => CrowdedState(cfg, n, quiet: true), analyzer);
    }

    private static SeriesConfig CrowdedConfig()
    {
        var cfg = new SeriesConfig
        {
            Name = "CipherB", FriendlyName = "Cipher B", IndicatorCode = "CIPHER_B", IsAutoNarrated = true
        };
        cfg.Components.Add(Marker("Bull Signal"));
        cfg.Components.Add(ZoneLine("Broken Zone"));
        cfg.Components.Add(ZoneLine("Tested Zone"));
        cfg.Components.Add(ZoneLine("Near Zone"));
        cfg.Components.Add(new ComponentConfig
        {
            Name = "MA Cloud", DisplayName = "MA Cloud",
            DisplayType = ComponentDisplayType.Cloud, IsVisible = true,
            UpperComponentName = "CloudUpper", LowerComponentName = "CloudLower"
        });
        return cfg;
    }

    private static WorkspaceState CrowdedState(SeriesConfig cfg, int barCount, bool quiet)
    {
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id };

        buf.ComponentData["Signal"] = Take(quiet
            ? new[] { double.NaN, double.NaN, double.NaN, double.NaN }
            : new[] { double.NaN, double.NaN, 1.0, double.NaN }, barCount);

        buf.ComponentData["Broken Zone"] = Take(quiet
            ? new[] { 105.0, 105.0, 105.0, 105.0 }
            : new[] { 105.0, 105.0, double.NaN, double.NaN }, barCount);

        buf.ComponentData["Tested Zone"] = Take(new[] { 110.0, 110.0, 110.0, 110.0 }, barCount);
        buf.ComponentData["Tested_touches"] = Take(quiet
            ? new[] { 1.0, 1.0, 1.0, 1.0 }
            : new[] { 1.0, 1.0, 3.0, 3.0 }, barCount);

        // 103.5 sits 0.48% under a close of 104 — inside the proximity band — and above a close
        // of 100, so the crowded scan crosses it from below. In the quiet run it is 10% away and
        // price stays on the same side of it throughout.
        buf.ComponentData["Near Zone"] = Take(quiet
            ? new[] { 90.0, 90.0, 90.0, 90.0 }
            : new[] { 103.5, 103.5, 103.5, 103.5 }, barCount);

        buf.ComponentData["CloudUpper"] = Take(new[] { 102.0, 102.0, 102.0, 102.0 }, barCount);
        buf.ComponentData["CloudLower"] = Take(new[] { 98.0, 98.0, 98.0, 98.0 }, barCount);

        return StateFor(ImmutableList.Create(new ChartSeries(cfg, buf)),
                        quiet ? FlatCloses : MovingCloses, barCount);
    }

    // ── Fixture B: one clause per tier, exactly at the cap ──────────────────────

    /// <summary>
    /// Five clauses, one from each of the five lower-priority tiers, so the whole order can be
    /// asserted without the cap deciding anything.
    ///
    /// <para>
    /// <b>The zone lines are on a series scanned BEFORE the one carrying the marker signal, and
    /// that is the point.</b> A first version of this fixture put everything on one series, where
    /// the signal comes out in front whether the tiers exist or not — <c>ScanSeriesForChanges</c>
    /// looks at markers first — so setting <c>TierSignal</c> equal to <c>TierCross</c> left every
    /// assertion green. Split across two series the two orders disagree, and only the tier can
    /// put the indicator's own call ahead of a line being crossed.
    /// </para>
    ///
    /// <para>
    /// <c>Zones</c> carries the cross (101, crossed from below), the touch (110, count 1 → 3) and
    /// the approach — 104.4, which is 0.38% ABOVE a close of 104, near and never crossed from
    /// either side. <c>Cipher B</c> carries the marker and the only oscillator context.
    /// </para>
    /// </summary>
    private static CapturingSpeechRouter RunOneClausePerTierScan()
    {
        var zones = new SeriesConfig
        {
            Name = "Zones", FriendlyName = "Zones", IndicatorCode = "ZONES", IsAutoNarrated = true
        };
        zones.Components.Add(ZoneLine("Crossed Zone"));
        zones.Components.Add(ZoneLine("Tested Zone"));
        zones.Components.Add(ZoneLine("Neared Zone"));

        var cipher = MarkerConfig("CipherB", "Cipher B", Marker("Bull Signal"));

        ChartSeries ZoneSeries(int barCount)
        {
            var buf = new SeriesDataBuffer { SeriesId = zones.Id };
            buf.ComponentData["Crossed Zone"] = Take(new[] { 101.0, 101.0, 101.0, 101.0 }, barCount);
            buf.ComponentData["Tested Zone"] = Take(new[] { 110.0, 110.0, 110.0, 110.0 }, barCount);
            buf.ComponentData["Tested_touches"] = Take(new[] { 1.0, 1.0, 3.0, 3.0 }, barCount);
            buf.ComponentData["Neared Zone"] = Take(new[] { 104.4, 104.4, 104.4, 104.4 }, barCount);
            return new ChartSeries(zones, buf);
        }

        var analyzer = new MutableContextAnalyzer
        {
            Context = Osc(ZoneStatus.Normal, CrossoverStatus.None),
            OnlyForSeries = "Cipher B"
        };

        return Drive(
            n => StateFor(ImmutableList.Create(ZoneSeries(n), MarkerSeries(cipher, n)), MovingCloses, n),
            analyzer,
            afterSeeding: Osc(ZoneStatus.Overbought, CrossoverStatus.None));
    }

    // ── The count ───────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The scan with the most to say must still be one utterance.</b> Eight before the fix.
    /// </summary>
    [Fact]
    public void ACrowdedScanIsOneUtterance()
    {
        var router = RunCrowdedScan();

        Assert.True(router.Calls.Count == 1,
            $"{router.Calls.Count} Speak calls for one scan — on the web head only the last one " +
            $"survives to be announced. Spoken: {string.Join(" || ", router.Spoken)}");
    }

    /// <summary>
    /// That one call is still on the channel F2 mutes and still does not interrupt. A composed
    /// utterance is longer than any of the clauses it replaced, so making it interrupting would
    /// talk over more of whatever the user asked for, not less.
    /// </summary>
    [Fact]
    public void TheComposedUtteranceIsStillANonInterruptingEvent()
    {
        var call = Assert.Single(RunCrowdedScan().Calls);

        Assert.Equal(SpeechChannel.Event, call.Channel);
        Assert.False(call.Interrupt);
    }

    // ── What is in it, and in what order ────────────────────────────────────────

    /// <summary>
    /// Every clause has to be IN that one utterance. Collapsing to a single call by dropping
    /// clauses would satisfy the count and lose the thing the count was protecting.
    /// </summary>
    [Fact]
    public void OneClausePerTierAllSurvive()
    {
        string spoken = Assert.Single(RunOneClausePerTierScan().Spoken);

        Assert.Contains("Bull Signal at 104.00", spoken);              // the indicator's own signal
        Assert.Contains("crossed above Crossed Zone", spoken);         // the level price changed side of
        Assert.Contains("tested resistance at 110.00", spoken);        // the level that held
        Assert.Contains("Approaching resistance at 104.40", spoken);   // the level it is nearing
        Assert.Contains("Money flow bullish", spoken);                 // the oscillator's zone
    }

    /// <summary>
    /// The full order, pinned tier by tier. Without this only the first and last tiers are
    /// guarded and the four in between can be permuted freely with every test still green.
    ///
    /// <para>
    /// Ordering is not cosmetic in an audio interface. This narrator fires while the user is
    /// doing something else, so whatever is most consequential has to arrive in the opening
    /// syllables or it is heard after attention has moved on.
    /// </para>
    ///
    /// <para>
    /// It is also the assertion that proves the order is a decision rather than an accident of
    /// the code: markers are the FIRST thing <c>ScanSeriesForChanges</c> looks at, zone lines come
    /// after them and the cloud after that, so scan order alone would give a different answer.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTiersAreSpokenMostConsequentialFirst()
    {
        string spoken = Assert.Single(RunOneClausePerTierScan().Spoken);

        string[] inOrder =
        {
            "Bull Signal at 104.00",            // 2 — the indicator's call, on the LAST series scanned
            "crossed above Crossed Zone",       // 3 — price changed side of a level
            "tested resistance at 110.00",      // 4 — a level held
            "Approaching resistance at 104.40", // 5 — something that has not happened yet
            "Money flow bullish"                // 6 — the most repetitive commentary
        };

        var positions = inOrder.Select(c => (Clause: c, At: spoken.IndexOf(c, StringComparison.Ordinal))).ToList();
        Assert.DoesNotContain(positions, p => p.At < 0);
        for (int i = 1; i < positions.Count; i++)
            Assert.True(positions[i - 1].At < positions[i].At,
                $"'{positions[i - 1].Clause}' must precede '{positions[i].Clause}': {spoken}");
    }

    /// <summary>
    /// A level that has ceased to exist leads everything — by this service's own source comment
    /// the break is "arguably the most consequential thing this narrator says".
    /// </summary>
    [Fact]
    public void TheBrokenLevelLeadsTheUtterance()
    {
        string spoken = Assert.Single(RunCrowdedScan().Spoken);
        Assert.StartsWith("Cipher B: Resistance at 105.00 broken.", spoken);
    }

    // ── The cap ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A scan that finds more than five things says the five that matter.
    ///
    /// <para>
    /// The clause count is bounded by nothing — the scan walks a 20-bar window across every
    /// component of every narrated series — and an utterance that runs for twenty seconds is not
    /// a text equivalent, it is an obstruction: the router protects an in-flight utterance from a
    /// lower-priority interrupt, so an arrow key pressed underneath one queues behind the rest of
    /// it. Dropping is only safe because the tiers exist: what goes is the least consequential
    /// thing the scan found, deterministically — which is the whole difference between this and
    /// the defect being fixed.
    /// </para>
    /// </summary>
    [Fact]
    public void ACrowdedScanKeepsTheTopFiveClausesAndDropsTheRest()
    {
        string spoken = Assert.Single(RunCrowdedScan().Spoken);

        Assert.Contains("Resistance at 105.00 broken", spoken);      // 1
        Assert.Contains("Bull Signal at 104.00", spoken);            // 2
        Assert.Contains("crossed above Near Zone", spoken);          // 3
        Assert.Contains("exited MA Cloud", spoken);                  // 3
        Assert.Contains("tested resistance at 110.00", spoken);      // 4

        // …and the oscillator commentary, tier 6, is what the cap gives up.
        Assert.DoesNotContain("Money flow bullish", spoken);
        Assert.DoesNotContain("bullish crossover", spoken);
    }

    // ── Reconciling two clauses about one level ─────────────────────────────────

    /// <summary>
    /// You are not approaching a level you have just crossed.
    ///
    /// <para>
    /// Separate utterances got away with the pair — in the ordinary case they were minutes apart,
    /// and on the web head seven of eight never arrived at all. In one breath
    /// "Price crossed above R1 at 103.50. Approaching support at 103.50." is a contradiction, and
    /// the tier sort puts two unrelated clauses between them so it does not even read as a
    /// correction.
    /// </para>
    /// </summary>
    [Fact]
    public void AnApproachToALevelPriceJustCrossedIsNotSpoken()
    {
        string spoken = Assert.Single(RunCrossedAndNearScan().Spoken);

        Assert.Contains("crossed above Near Zone at 103.50", spoken);
        Assert.DoesNotContain("Approaching support at 103.50", spoken);
    }

    /// <summary>
    /// ONE zone line, crossed and inside the proximity band on the same bar, and nothing else on
    /// the chart.
    ///
    /// <para>
    /// This fixture exists because the crowded scan cannot test the rule: there the approach is
    /// tier 5 of eight clauses, so the cap drops it whether the suppression works or not — and a
    /// sabotage that deleted the suppression outright left every assertion green. Two guards
    /// masking each other. Here the pair is the whole utterance, well inside the cap, so the only
    /// thing that can remove the approach is the rule under test.
    /// </para>
    /// </summary>
    private static CapturingSpeechRouter RunCrossedAndNearScan()
    {
        var cfg = new SeriesConfig
        {
            Name = "Zones", FriendlyName = "Zones", IndicatorCode = "ZONES", IsAutoNarrated = true
        };
        cfg.Components.Add(ZoneLine("Near Zone"));

        // 103.5 is above a close of 100 and 0.48% below a close of 104: crossed from below on the
        // scanned bar, and near it at the same moment.
        return Drive(n =>
        {
            var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
            buf.ComponentData["Near Zone"] = Take(new[] { 103.5, 103.5, 103.5, 103.5 }, n);
            return StateFor(ImmutableList.Create(new ChartSeries(cfg, buf)), MovingCloses, n);
        }, new MutableContextAnalyzer());
    }

    /// <summary>
    /// The vacuity half. An approach to a level that was NOT crossed is still spoken — otherwise
    /// the suppression above would be indistinguishable from having deleted the approach clause.
    /// </summary>
    [Fact]
    public void AnApproachToALevelPriceHasNotCrossedIsStillSpoken()
    {
        string spoken = Assert.Single(RunOneClausePerTierScan().Spoken);
        Assert.Contains("Approaching resistance at 104.40", spoken);
    }

    // ── Which series is talking ─────────────────────────────────────────────────

    /// <summary>
    /// Composing introduces a new way to be unbearable: every clause this service builds itself
    /// carries "<c>Cipher B: </c>", which read correctly when each was its own utterance and reads
    /// as a stutter once five of them are joined. The name is spoken once per run of clauses
    /// about that series.
    /// </summary>
    [Fact]
    public void TheSeriesNameIsSpokenOnce()
    {
        string spoken = Assert.Single(RunCrowdedScan().Spoken);

        int occurrences = spoken.Split("Cipher B").Length - 1;
        Assert.True(occurrences == 1,
            $"the series name appears {occurrences} times in one utterance: {spoken}");
    }

    /// <summary>
    /// A SIGNAL clause is introduced by its COMPONENT, never by its series — and only when the
    /// clause has not already said which component it is.
    ///
    /// <para>
    /// Until 2026-09-05 a template clause joined behind another series' clause was prefixed
    /// with the SERIES name (none of the 61 shipped templates contains <c>{series}</c>, so it
    /// would otherwise be heard as the other series' signal). Cody: <i>"hearing only the
    /// component name before the signal is all that is needed, not the series name as the user
    /// probably knows what they enabled for narration"</i>. Here the first series' marker names
    /// itself ("Bull Signal at …") and is left alone; the second's template does not mention
    /// its component, so the component leads. The series names appear nowhere.
    /// </para>
    /// </summary>
    [Fact]
    public void ASignalClauseIsIntroducedByItsComponent_NeverByItsSeries()
    {
        string spoken = Assert.Single(RunTwoSeriesScan().Spoken);

        Assert.Contains("Bull Signal at 104.00.", spoken);
        Assert.Contains("Funding Crowding: Long crowded, squeeze risk down.", spoken);
        Assert.DoesNotContain("Cipher B:", spoken);
        Assert.DoesNotContain("Squeeze:", spoken);
    }

    /// <summary>
    /// <b>47 of those 61 templates also end without a full stop.</b> Alone that was invisible;
    /// joined, "…squeeze risk down Bull Signal at 104.00" is one run-on sentence with no boundary
    /// for a screen reader to pause on. A clause that does not punctuate itself gets punctuated.
    /// </summary>
    [Fact]
    public void AClauseThatDoesNotEndItselfIsGivenAFullStop()
    {
        string spoken = Assert.Single(RunTwoSeriesScan().Spoken);

        Assert.EndsWith(".", spoken);
        Assert.DoesNotContain("down Bull", spoken);
        Assert.DoesNotContain("..", spoken);
    }

    /// <summary>
    /// The stutter guard: a template that already says its component's name — here the
    /// component is "Squeeze" and the sentence contains "squeeze" — is left exactly as its author
    /// wrote it. "Squeeze: Long crowded, squeeze risk down" is not an introduction.
    /// </summary>
    [Fact]
    public void ATemplateThatAlreadyNamesItsComponentIsLeftAlone()
    {
        string spoken = Assert.Single(RunTemplateOnlyScan().Spoken);

        Assert.Equal("Long crowded, squeeze risk down.", spoken);
    }

    /// <summary>Two narrated series, the second announcing itself through a template.</summary>
    private static CapturingSpeechRouter RunTwoSeriesScan()
    {
        var first = MarkerConfig("CipherB", "Cipher B", Marker("Bull Signal"));
        var second = MarkerConfig("Squeeze", "Squeeze", Marker("Funding Crowding", "Long crowded, squeeze risk down"));

        return Drive(n => StateFor(
                ImmutableList.Create(MarkerSeries(first, n), MarkerSeries(second, n)),
                MovingCloses, n),
            new MutableContextAnalyzer());
    }

    /// <summary>One narrated series whose only clause comes from a template.</summary>
    private static CapturingSpeechRouter RunTemplateOnlyScan()
    {
        var cfg = MarkerConfig("Squeeze", "Squeeze", Marker("Squeeze", "Long crowded, squeeze risk down"));

        return Drive(n => StateFor(ImmutableList.Create(MarkerSeries(cfg, n)), MovingCloses, n),
                     new MutableContextAnalyzer());
    }

    private static SeriesConfig MarkerConfig(string name, string friendly, ComponentConfig marker)
    {
        var cfg = new SeriesConfig
        {
            Name = name, FriendlyName = friendly, IndicatorCode = name, IsAutoNarrated = true
        };
        cfg.Components.Add(marker);
        return cfg;
    }

    private static ChartSeries MarkerSeries(SeriesConfig cfg, int barCount)
    {
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
        buf.ComponentData["Signal"] = Take(new[] { double.NaN, double.NaN, 1.0, double.NaN }, barCount);
        return new ChartSeries(cfg, buf);
    }

    // ── Vacuity ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The same series over the same scan sequence with nothing happening says nothing at all.
    ///
    /// <para>
    /// Without this, "the utterance contains 'Approaching resistance'" would be a fact about the
    /// formatter rather than about the data, and every assertion above would hold whatever the
    /// bars did. It also pins the other half of the composition: an empty scan must not speak an
    /// empty string.
    /// </para>
    /// </summary>
    [Fact]
    public void AQuietScanSaysNothing()
    {
        var router = RunQuietScan();
        Assert.True(router.Calls.Count == 0,
            $"a scan with nothing to report spoke: {string.Join(" || ", router.Spoken)}");
    }

    // ── The premise ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Why the call count is the property that matters: the web head's live region is fed from a
    /// single <c>string</c> field, so N assignments inside one Blazor render batch deliver the
    /// Nth and nothing else.
    ///
    /// <para>
    /// This is a source guard on the sink rather than a render test — <c>MainLayout</c> injects
    /// ~25 services and renders the whole app shell, so bUnit cannot reach it, the same reason
    /// <c>BrowserTitlePriceSourceTests</c> guards it this way. It asserts the path speech takes
    /// (handler assigns the field, both buffers render that same field) and NOT that the field is
    /// the only mechanism that could ever exist: someone who adds a real queue alongside it will
    /// pass this and should read <see cref="AutoNarrationUtteranceTests"/> before deciding the
    /// one-utterance rule has stopped applying.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWebHeadsLiveRegionIsFedFromOneStringField()
    {
        string path = Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient.Components",
                                   "Layout", "MainLayout.razor");
        Assert.True(File.Exists(path), $"MainLayout.razor not found at {path}");
        string layout = File.ReadAllText(path);

        // The sink moved into SpeechLiveRegionBuffer on 2026-09-04 (so the alternation rule
        // could be tested — it was flipping on the empty callback that Silence() produces, and
        // interrupting speech never reached the second region at all). What this guard is about
        // did not move: it is still ONE string behind both regions, so N assignments inside one
        // render batch deliver the Nth and discard the rest.
        Assert.Contains("SpeechLiveRegionBuffer _speechRegions", layout);
        Assert.Contains("_speechRegions.Push(text)", layout);
        Assert.Contains("_speechRegions.TextFor(1)", layout);
        Assert.Contains("_speechRegions.TextFor(2)", layout);

        string buffer = File.ReadAllText(Path.Combine(RepoRoot(),
            "AccessibleTrader.BlazorClient.Components", "Services", "SpeechLiveRegionBuffer.cs"));
        Assert.Contains("public string Text { get; private set; }", buffer);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AccessibleTrader.Core")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
