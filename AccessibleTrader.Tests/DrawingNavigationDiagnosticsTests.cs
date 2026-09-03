using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using AccessibleTrader.Tests.Mocks;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// Drawing navigation, from the diagnosis of 2026-09-03 onward.
///
/// <para>
/// The file began as pure diagnosis — tests asserting the CURRENT behaviour so the user's report
/// was evidence rather than opinion. As each defect is fixed its diagnostic is inverted into a
/// guard here, so the record of what was wrong and the guard against its return live in one
/// place. Tests still named <c>Q1</c>/<c>Q2</c>/<c>Q3</c>/<c>Q4</c>/<c>Q6</c> are the remaining
/// diagnostics; they describe behaviour that has NOT been changed yet and will be inverted the
/// same way.
/// </para>
/// </summary>
public sealed class DrawingNavigationDiagnosticsTests
{
    private readonly ITestOutputHelper _out;
    public DrawingNavigationDiagnosticsTests(ITestOutputHelper output) => _out = output;

    // ── fixtures ─────────────────────────────────────────────────────────────

    /// <summary>100 hourly bars, close walking 100 → 199.</summary>
    private static List<Ohlcv> Bars(int n = 100)
    {
        var list = new List<Ohlcv>();
        var t = new DateTime(2026, 6, 1, 9, 30, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++)
            list.Add(new Ohlcv(t.AddHours(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 1000));
        return list;
    }

    /// <summary>The real registry, wired from every IDrawingCalculator in Core — the same set
    /// ServiceCollectionExtensions registers, discovered rather than hand-listed.</summary>
    private static IDrawingService RealDrawingService()
    {
        var calcType = typeof(IDrawingCalculator);
        var calcs = typeof(DrawingService).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && calcType.IsAssignableFrom(t))
            .Select(t => (IDrawingCalculator)Activator.CreateInstance(t)!)
            .ToList();
        return new DrawingService(calcs);
    }

    private static string Shape(double[] a)
    {
        int first = Array.FindIndex(a, v => !double.IsNaN(v));
        int last = Array.FindLastIndex(a, v => !double.IsNaN(v));
        int n = a.Count(v => !double.IsNaN(v));
        return $"len={a.Length} nonNaN={n} firstNonNaN={first} lastNonNaN={last}";
    }

    // ── A trend line stops where it was anchored (was Q1) ────────────────────

    /// <summary>
    /// The line's values exist only between its anchors unless a flag says otherwise.
    ///
    /// <para><b>The defect this replaces.</b> <c>CalculateLinearPoints</c> took
    /// <c>extL</c>/<c>extR</c> and never read either one; it filled the whole array. This test
    /// stood in its Q1 form asserting exactly that, with <c>ExtendLeft = false,
    /// ExtendRight = false</c> written into the fixture and the comment "accepted and ignored".
    /// Cody heard it from the other side: a trend line placed at bar 30 spoke a price at bar 0,
    /// so "it sounds like the trend line extends further left than I placed the start marker".
    /// </para>
    ///
    /// <para>Bar 30 and bar 70 are asserted for the anchors themselves and bars 29 and 71 for
    /// the boundary, because an off-by-one in the loop bounds is the failure this could
    /// plausibly regress to and it is invisible to a NaN count.</para>
    /// </summary>
    [Fact]
    public void TrendLine_Line_Component_Covers_Only_The_Span_Between_Its_Anchors()
    {
        var bars = Bars();
        var svc = RealDrawingService();
        var d = new DrawingData
        {
            Type = DrawingType.TrendLine,
            AnchorDate1 = bars[30].Date, AnchorPrice1 = bars[30].Close,
            AnchorDate2 = bars[70].Date, AnchorPrice2 = bars[70].Close,
            ExtendLeft = false, ExtendRight = false,
        };

        var res = svc.CalculateDrawingData(d, bars);
        _out.WriteLine("keys: " + string.Join(", ", res.Keys));
        var line = res["Line"];
        _out.WriteLine("Line " + Shape(line));
        foreach (int i in new[] { 0, 1, 29, 30, 31, 50, 69, 70, 71, 98, 99 })
            _out.WriteLine($"  [{i,2}] = {line[i]:F4}");

        Assert.Single(res);                       // ONE component, named "Line"
        Assert.Equal(bars.Count, line.Length);

        Assert.True(double.IsNaN(line[0]),  "bar 0 is 30 bars before the start anchor");
        Assert.True(double.IsNaN(line[29]), "bar 29 is one bar before the start anchor");
        Assert.Equal(bars[30].Close, line[30], 6);
        Assert.Equal(bars[70].Close, line[70], 6);
        Assert.False(double.IsNaN(line[50]), "bar 50 is between the anchors");
        Assert.True(double.IsNaN(line[71]), "bar 71 is one bar past the end anchor");
        Assert.True(double.IsNaN(line[99]), "bar 99 is 29 bars past the end anchor");
    }

    /// <summary>
    /// The flags are not merely honoured, they are honoured SEPARATELY — the fix would also
    /// pass a single-flag test if it had collapsed both onto one condition, and every drawing
    /// the user places is created with exactly this asymmetric pair (<c>ExtendRight</c> true,
    /// <c>ExtendLeft</c> false), which is the trader's convention: project forward, stop dead
    /// at the anchor you started from.
    /// </summary>
    [Theory]
    [InlineData(false, false, true,  true )]   // segment: NaN both sides
    [InlineData(false, true,  true,  false)]   // as placed: stops left, runs right
    [InlineData(true,  false, false, true )]   // stops right, runs left
    [InlineData(true,  true,  false, false)]   // the old, wrong, unconditional behaviour
    public void TrendLine_Extend_Flags_Are_Read_Independently(
        bool extendLeft, bool extendRight, bool nanBeforeStart, bool nanAfterEnd)
    {
        var bars = Bars();
        var svc = RealDrawingService();
        var d = new DrawingData
        {
            Type = DrawingType.TrendLine,
            AnchorDate1 = bars[30].Date, AnchorPrice1 = bars[30].Close,
            AnchorDate2 = bars[70].Date, AnchorPrice2 = bars[70].Close,
            ExtendLeft = extendLeft, ExtendRight = extendRight,
        };

        var line = svc.CalculateDrawingData(d, bars)["Line"];
        _out.WriteLine($"extL={extendLeft} extR={extendRight}: " + Shape(line));

        Assert.Equal(nanBeforeStart, double.IsNaN(line[0]));
        Assert.Equal(nanAfterEnd,    double.IsNaN(line[99]));
        Assert.Equal(bars[30].Close, line[30], 6);   // the anchors themselves always exist
        Assert.Equal(bars[70].Close, line[70], 6);
    }

    // ── Q4: the component census across all 16 drawing types ────────────────

    [Fact]
    public void Q4_Component_Census_For_Every_DrawingType()
    {
        var bars = Bars();
        var svc = RealDrawingService();
        var sb = new StringBuilder();

        foreach (DrawingType t in Enum.GetValues<DrawingType>())
        {
            var d = new DrawingData
            {
                Type = t,
                AnchorDate1 = bars[30].Date, AnchorPrice1 = bars[30].Close,
                AnchorDate2 = bars[70].Date, AnchorPrice2 = bars[70].Close,
                ExtendRight = true,        // as DrawingInteractionManager places one
                AnchorDate3 = bars[85].Date, AnchorPrice3 = bars[85].Close,
                ChannelWidth = 5, Text = "note",
            };
            var res = svc.CalculateDrawingData(d, bars);
            sb.AppendLine($"{t}: {res.Count} component(s)");
            foreach (var kv in res)
                sb.AppendLine($"    {kv.Key}: {Shape(kv.Value)}");
        }
        _out.WriteLine(sb.ToString());
    }

    // ── Q2: what is SPOKEN when arrowing along a focused trend line ─────────

    private sealed class CapturingRouter : ISpeechFeedbackRouter
    {
        public List<string> Said = new();
        public void Speak(string message, bool interrupt = true, SpeechChannel channel = SpeechChannel.Manual) => Said.Add(message);
        public void SpeakPoint(WorkspaceState s, WorkspaceState? p, ChartSeries se, Ohlcv pt, string prefix = "") { }
        public void SpeakProfile(WorkspaceState s, WorkspaceState? p, ChartSeries se, int b, string prefix = "") { }
        public void SpeakHeatmap(WorkspaceState s, WorkspaceState? p, ChartSeries se, int d, int b, string prefix = "") { }
    }

    /// <summary>Builds the drawing series the way <c>DrawingInteractionManager.CreateDrawingSeries</c>
    /// does: friendly name + ordinal, one ComponentConfig per calculator key, built by the REAL
    /// IndicatorModelFactory over the REAL StylingService.</summary>
    private static ChartSeries BuildDrawingSeriesLikeProduction(DrawingType type, string friendly, DrawingData d, List<Ohlcv> bars)
    {
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var factory = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService());
        var cfg = new SeriesConfig { Id = "draw-1", Name = $"{friendly} (1)", FriendlyName = $"{type} Drawing", Pane = "Main" };
        var buf = new SeriesDataBuffer { SeriesId = "draw-1" };
        foreach (var kv in RealDrawingService().CalculateDrawingData(d, bars))
        {
            var comp = factory.CreateComponentConfig(type.ToString(), kv.Key);
            cfg.Components.Add(comp);
            buf.ComponentData[comp.Name] = kv.Value;
        }
        return new ChartSeries(cfg, buf) { Drawing = d };
    }

    private static WorkspaceState StateAt(List<Ohlcv> bars, ChartSeries series, int index) =>
        WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = index,
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = 0,
            PrimarySeriesId = "candles",
            ReadColumnHeaders = true,
            SpeechOrder = "HeaderValue",
            SpeakTimestamps = false,
            LastInteractionContext = InteractionContext.Component,
            ViewportStartIndex = 0,
            ViewportLength = bars.Count,
        };

    [Fact]
    public void Q2_Arrowing_Along_A_TrendLine_At_50_And_At_10()
    {
        var bars = Bars();
        var d = new DrawingData
        {
            Type = DrawingType.TrendLine,
            AnchorDate1 = bars[30].Date, AnchorPrice1 = bars[30].Close,
            AnchorDate2 = bars[70].Date, AnchorPrice2 = bars[70].Close,
            ExtendRight = true,        // as DrawingInteractionManager places one
        };
        var series = BuildDrawingSeriesLikeProduction(DrawingType.TrendLine, "Trend line", d, bars);
        _out.WriteLine("series.Name = " + series.Config.Name);
        _out.WriteLine("components  = " + string.Join(", ",
            series.Components.Select(c => $"{c.Name}/{c.DisplayName}/{c.DisplayType}/{c.Role}/tmpl='{c.SpeechTemplate}'")));

        var router = new CapturingRouter();
        var nav = new NavigationFeedbackManager(router, new SpeechFormatter());

        // Page-Down onto the drawing, then Right at bar 50 (inside the anchors),
        // then Right at bar 10 (outside them).
        nav.HandleNavigationFeedback(StateAt(bars, series, 50), isXMove: true, isYMove: false, prefixMessage: "NAV_SERIES_NEXT");
        nav.HandleNavigationFeedback(StateAt(bars, series, 51), isXMove: true, isYMove: false, prefixMessage: "NAV_MOVE");
        nav.HandleNavigationFeedback(StateAt(bars, series, 10), isXMove: true, isYMove: false, prefixMessage: "NAV_MOVE");

        foreach (var s in router.Said) _out.WriteLine("SPOKEN: " + s);
        Assert.Equal(3, router.Said.Count);
    }

    // ── Shift+Space / Ctrl+Shift+Space on a focused drawing (was Q3) ────────

    /// <summary>
    /// A drawing that the plan accepts is a drawing the sequencer sounds.
    ///
    /// <para><b>The defect this replaces.</b> Two independent filters. <c>PlaybackPlan</c>
    /// accepted a focused drawing in Series and Component scope, so the dispatcher did not
    /// refuse and the coordinator announced "Playing TrendLine Drawing from ..., N bars" —
    /// and then <c>AudioSequencer.BuildVoicePlan</c> skipped it on <c>IsDrawing</c> and built
    /// an EMPTY voice plan. Playback ran its full length in silence. That is precisely the
    /// disagreement between announcement and behaviour that <c>PlaybackPlan</c>'s own summary
    /// says the type exists to prevent, and it survived because each half was read alone.</para>
    ///
    /// <para>So both halves are asserted here, in one test, against one series: the plan is
    /// playable AND the voice plan is not empty. Splitting them is how this came back.</para>
    /// </summary>
    [Fact]
    public void A_drawing_the_plan_accepts_gets_voices_from_the_sequencer()
    {
        var bars = Bars();
        var d = new DrawingData
        {
            Type = DrawingType.TrendLine,
            AnchorDate1 = bars[30].Date, AnchorPrice1 = bars[30].Close,
            AnchorDate2 = bars[70].Date, AnchorPrice2 = bars[70].Close,
            ExtendRight = true,        // as DrawingInteractionManager places one
        };
        var series = BuildDrawingSeriesLikeProduction(DrawingType.TrendLine, "Trend line", d, bars);
        var state = StateAt(bars, series, 50);

        var plan = PlaybackPlan.Resolve(state, PlaybackScope.Series);
        _out.WriteLine($"PlaybackPlan(Series): IsPlayable={plan.IsPlayable} series={plan.Series.Count} " +
                       $"start={plan.StartIndex} filter={plan.ComponentFilter} refusal={plan.RefusalReason ?? "<none>"}");
        var planC = PlaybackPlan.Resolve(state, PlaybackScope.Component);
        _out.WriteLine($"PlaybackPlan(Component): IsPlayable={planC.IsPlayable} refusal={planC.RefusalReason ?? "<none>"}");

        // The dispatcher does not refuse; playback starts.
        Assert.True(plan.IsPlayable);
        Assert.True(planC.IsPlayable);

        // ...and the sequencer reserves voice slots for it, so something is actually heard.
        var driver = Substitute.For<IAudioDriver>();
        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(state);
        var seq = new AudioSequencer(driver, Substitute.For<ISonificationStrategy>(), store,
            Substitute.For<ISoundPatchRegistry>(), NullLogger<AudioSequencer>.Instance);

        var m = typeof(AudioSequencer).GetMethod("BuildVoicePlan", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var voicePlan = (System.Collections.ICollection)m.Invoke(seq, new object?[] { new[] { series }, -1 })!;
        _out.WriteLine($"BuildVoicePlan voices = {voicePlan.Count}");
        Assert.NotEmpty(voicePlan);

        // Control: the same call on a NON-drawing series with one component produces a voice.
        var ctrlCfg = new SeriesConfig { Id = "ema", Name = "EMA", IndicatorCode = "EMA", Pane = "Main" };
        ctrlCfg.Components.Add(new ComponentConfig { Name = "Line", DisplayName = "Line", DisplayType = ComponentDisplayType.Line, IsVisible = true });
        var ctrlBuf = new SeriesDataBuffer { SeriesId = "ema" };
        ctrlBuf.ComponentData["Line"] = Enumerable.Range(0, bars.Count).Select(i => (double)i).ToArray();
        var control = new ChartSeries(ctrlCfg, ctrlBuf);
        Substitute.For<ISonificationStrategy>();
        var ctrlPlan = (System.Collections.ICollection)m.Invoke(seq, new object?[] { new[] { control }, -1 })!;
        _out.WriteLine($"BuildVoicePlan voices (non-drawing control) = {ctrlPlan.Count}");
        Assert.NotEmpty(ctrlPlan);
    }

    // ── Q2b: the REAL keyboard placement path, end to end ───────────────────

    [Fact]
    public void Q2b_Real_Keyboard_Placement_Then_Arrow_Left_And_Right()
    {
        var bars = Bars();
        var bus = new SpyEventBus();
        var store = new WorkspaceStore(bus, new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());
        store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(bars), IsInitialLoad: true));

        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var mgr = new DrawingInteractionManager(
            bus, RealDrawingService(), store,
            new IndicatorModelFactory(styling, new MockIndicatorPreferencesService()),
            new AccessibleTrader.BlazorClient.Services.BlazorInputService());

        // Ctrl+Shift+T at bar 30, then navigate to bar 70 and press it again.
        store.Dispatch(new NavigateAction(30));
        mgr.HandleAddDrawing("TrendLine", bars);
        store.Dispatch(new NavigateAction(70));
        mgr.HandleAddDrawing("TrendLine", bars);

        var drawing = store.State.ActiveSeries.First(s => s.IsDrawing);
        _out.WriteLine($"name={drawing.Config.Name} components={drawing.Components.Count} " +
                       $"keys=[{string.Join(",", drawing.Data.ComponentData.Keys)}]");
        foreach (var c in drawing.Components)
            _out.WriteLine($"  comp {c.Name} arrLen={drawing.GetComponentData(c.Name).Length}");

        var router = new CapturingRouter();
        var nav = new NavigationFeedbackManager(router, new SpeechFormatter());
        foreach (int i in new[] { 50, 10 })
        {
            var st = WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                CurrentDataIndex = i,
                ActiveSeries = ImmutableList.Create(drawing),
                FocusedSeriesId = drawing.Id,
                FocusedComponentIndex = 0,
                ReadColumnHeaders = true,
                SpeechOrder = "HeaderValue",
                SpeakTimestamps = false,
                LastInteractionContext = InteractionContext.Series,
                ViewportStartIndex = 0,
                ViewportLength = bars.Count,
            };
            nav.HandleNavigationFeedback(st, isXMove: true, isYMove: false, prefixMessage: "NAV_MOVE");
        }
        foreach (var s in router.Said) _out.WriteLine("SPOKEN: " + s);
    }

    // ── Q2c: the drawings that ARE sparse — outside the span reads "no data" ──

    [Fact]
    public void Q2c_Rectangle_Outside_Its_Span_Reads_No_Data()
    {
        var bars = Bars();
        var d = new DrawingData
        {
            Type = DrawingType.Rectangle,
            AnchorDate1 = bars[30].Date, AnchorPrice1 = bars[70].Close,
            AnchorDate2 = bars[70].Date, AnchorPrice2 = bars[30].Close,
        };
        var series = BuildDrawingSeriesLikeProduction(DrawingType.Rectangle, "Rectangle", d, bars);
        var router = new CapturingRouter();
        var nav = new NavigationFeedbackManager(router, new SpeechFormatter());
        nav.HandleNavigationFeedback(StateAt(bars, series, 50), true, false, "NAV_MOVE");
        nav.HandleNavigationFeedback(StateAt(bars, series, 10), true, false, "NAV_MOVE");
        foreach (var s in router.Said) _out.WriteLine("SPOKEN: " + s);
        Assert.DoesNotContain("no data", router.Said[0]);
        Assert.Contains("no data", router.Said[1]);
    }

    // ── Q2d: a drawing whose component ARRAYS were wiped still says "1 component" ──

    [Fact]
    public void Q2d_Empty_ComponentData_With_Components_Present_Reads_No_Data_Everywhere()
    {
        var bars = Bars();
        var d = new DrawingData
        {
            Type = DrawingType.TrendLine,
            AnchorDate1 = bars[30].Date, AnchorPrice1 = bars[30].Close,
            AnchorDate2 = bars[70].Date, AnchorPrice2 = bars[70].Close,
            ExtendRight = true,        // as DrawingInteractionManager places one
        };
        var series = BuildDrawingSeriesLikeProduction(DrawingType.TrendLine, "Trend line", d, bars);
        // What a workspace restore hands over (RegisterSeriesFromConfig) and what
        // IndicatorOrchestrator.RecalculateAllAsync dispatches when `data` is empty:
        // the CONFIG keeps its components, the BUFFER is empty.
        series.Data.ComponentData.Clear();

        var router = new CapturingRouter();
        var nav = new NavigationFeedbackManager(router, new SpeechFormatter());
        nav.HandleNavigationFeedback(StateAt(bars, series, 50), true, false, "NAV_SERIES_NEXT");
        nav.HandleNavigationFeedback(StateAt(bars, series, 51), true, false, "NAV_MOVE");
        nav.HandleNavigationFeedback(StateAt(bars, series, 10), true, false, "NAV_MOVE");
        foreach (var s in router.Said) _out.WriteLine("SPOKEN: " + s);
        Assert.Contains("1 component", router.Said[0]);
        Assert.All(router.Said, m => Assert.Contains("no data", m));
    }

    // ── Q2e: LIVE BARS. A drawing is skipped by the per-tick recalculation, so
    //         every bar that arrives after it was placed is off the end of its array.

    // ── The live edge. Was Q2e, a diagnostic asserting the defect; now four guards. ──
    //
    // THE DEFECT, as it was measured on 2026-09-03: IndicatorOrchestrator.RecalculateLastAsync
    // skipped every drawing on every tick (`if (isProfile || s.IsDrawing) continue;`, commented
    // "for performance"). So a drawing's component array stayed frozen at the bar count it was
    // born with, and every bar that arrived afterwards read "no data" — at the live edge, which
    // is where a trader stands. Reported from real use.
    //
    // The fix recomputes a drawing only when its buffer has stopped describing the bars. These
    // four tests are the four branches of that decision, and the fourth is the one that keeps the
    // fix honest: "recalculate when stale" and "recalculate always" pass the first three
    // identically.

    /// <summary>The reported case: five bars arrive after the trend line was placed.</summary>
    [Fact]
    public async Task A_drawing_follows_the_live_edge_and_never_says_no_data_on_a_new_bar()
    {
        var bars = Bars();                       // 100 bars at placement time
        var d = new DrawingData
        {
            Type = DrawingType.TrendLine,
            AnchorDate1 = bars[30].Date, AnchorPrice1 = bars[30].Close,
            AnchorDate2 = bars[70].Date, AnchorPrice2 = bars[70].Close,
            ExtendRight = true,        // as DrawingInteractionManager places one
        };
        var series = BuildDrawingSeriesLikeProduction(DrawingType.TrendLine, "Trend line", d, bars);
        series.Data.FirstBarDate = bars[0].Date;
        Assert.Equal(100, series.GetComponentData("Line").Length);

        var grown = Bars(105);                   // five more bars arrive live
        var (store, orch) = NewOrchestrator();
        await orch.RecalculateLastAsync(grown, new[] { series }, CancellationToken.None);

        var buffer = SoleDispatchFor(store, series.Id);
        Assert.Equal(105, buffer.ComponentData["Line"].Length);
        Assert.Equal(grown[0].Date, buffer.FirstBarDate);

        // The values on the new bars are the line extrapolated, not NaN — and the SPEECH is what
        // the user actually gets, so it is what is asserted. Bar 104 used to say "no data".
        var refreshed = new ChartSeries(series.Config, buffer) { Drawing = d };
        var router = new CapturingRouter();
        var nav = new NavigationFeedbackManager(router, new SpeechFormatter());
        foreach (int i in new[] { 99, 100, 104 })
            nav.HandleNavigationFeedback(StateAt(grown, refreshed, i), true, false, "NAV_MOVE");

        foreach (var said in router.Said) _out.WriteLine("SPOKEN: " + said);
        Assert.Equal(3, router.Said.Count);
        Assert.All(router.Said, said => Assert.DoesNotContain("no data", said));
    }

    /// <summary>
    /// A scrollback fetch prepends history. The array is still one value per bar, so a
    /// length-only check would call this aligned — and every value would be sitting on a bar it
    /// was not computed from. This is the second consequence of the same skipped clause: the
    /// prepend-realignment check lived UNDERNEATH it, so drawings never reached it.
    /// </summary>
    [Fact]
    public async Task A_drawing_is_realigned_when_history_is_prepended_not_appended()
    {
        var bars = Bars();
        var d = new DrawingData
        {
            Type = DrawingType.TrendLine,
            AnchorDate1 = bars[30].Date, AnchorPrice1 = bars[30].Close,
            AnchorDate2 = bars[70].Date, AnchorPrice2 = bars[70].Close,
            ExtendRight = true,        // as DrawingInteractionManager places one
        };
        var series = BuildDrawingSeriesLikeProduction(DrawingType.TrendLine, "Trend line", d, bars);
        series.Data.FirstBarDate = bars[0].Date;

        // 100 bars of OLDER history in front. Same anchors, same dates — a different index 0.
        var t0 = bars[0].Date.AddHours(-100);
        var prepended = new List<Ohlcv>();
        for (int i = 0; i < 100; i++)
            prepended.Add(new Ohlcv(t0.AddHours(i), 1 + i, 2 + i, i, 1.5 + i, 1000));
        prepended.AddRange(bars);

        var (store, orch) = NewOrchestrator();
        await orch.RecalculateLastAsync(prepended, new[] { series }, CancellationToken.None);

        var buffer = SoleDispatchFor(store, series.Id);
        Assert.Equal(200, buffer.ComponentData["Line"].Length);
        Assert.Equal(prepended[0].Date, buffer.FirstBarDate);

        // The anchor still lands on the bar it was anchored TO, which is now index 130.
        int anchorIdx = prepended.FindIndex(b => b.Date == bars[30].Date);
        Assert.Equal(130, anchorIdx);
        Assert.Equal(bars[30].Close, buffer.ComponentData["Line"][anchorIdx], 6);
    }

    /// <summary>
    /// Components declared, no arrays behind them — a workspace restore that handed over an empty
    /// buffer. This reads "no data" at EVERY bar, which is the shape the user reported first.
    /// </summary>
    [Fact]
    public async Task A_drawing_restored_with_an_empty_buffer_is_rebuilt_on_the_next_tick()
    {
        var bars = Bars();
        var d = new DrawingData
        {
            Type = DrawingType.TrendLine,
            AnchorDate1 = bars[30].Date, AnchorPrice1 = bars[30].Close,
            AnchorDate2 = bars[70].Date, AnchorPrice2 = bars[70].Close,
            ExtendRight = true,        // as DrawingInteractionManager places one
        };
        var series = BuildDrawingSeriesLikeProduction(DrawingType.TrendLine, "Trend line", d, bars);

        // What RegisterSeriesFromConfig hands over: the config's components, and nothing else.
        series.Data.ComponentData.Clear();
        series.Data.FirstBarDate = bars[0].Date;
        Assert.NotEmpty(series.Components);

        var (store, orch) = NewOrchestrator();
        await orch.RecalculateLastAsync(bars, new[] { series }, CancellationToken.None);

        var buffer = SoleDispatchFor(store, series.Id);
        Assert.Equal(100, buffer.ComponentData["Line"].Length);
    }

    /// <summary>
    /// THE FLOOR UNDER THE OTHER THREE. On a tick where nothing changed, a drawing must NOT be
    /// recomputed — otherwise the fix is "recalculate every drawing on every tick", the three
    /// tests above pass for the wrong reason, and the performance argument the original clause
    /// was written for is quietly discarded rather than answered.
    /// </summary>
    [Fact]
    public async Task A_drawing_whose_buffer_still_describes_these_bars_is_left_alone()
    {
        var bars = Bars();
        var d = new DrawingData
        {
            Type = DrawingType.TrendLine,
            AnchorDate1 = bars[30].Date, AnchorPrice1 = bars[30].Close,
            AnchorDate2 = bars[70].Date, AnchorPrice2 = bars[70].Close,
            ExtendRight = true,        // as DrawingInteractionManager places one
        };
        var series = BuildDrawingSeriesLikeProduction(DrawingType.TrendLine, "Trend line", d, bars);
        series.Data.FirstBarDate = bars[0].Date;
        Assert.Equal(100, series.GetComponentData("Line").Length);

        var (store, orch) = NewOrchestrator();
        await orch.RecalculateLastAsync(bars, new[] { series }, CancellationToken.None);

        Assert.DoesNotContain(store.DispatchedActions,
            a => a is UpdateSeriesDataAction u && u.SeriesId == series.Id);
    }

    private static (MockWorkspaceStore Store, IndicatorOrchestrator Orch) NewOrchestrator()
    {
        var store = new MockWorkspaceStore();
        return (store, new IndicatorOrchestrator(
            Substitute.For<IIndicatorEngine>(), new IndicatorStateMapper(),
            RealDrawingService(), Substitute.For<IProfileService>(), Substitute.For<IHeatmapService>(),
            store, new MockNotificationHub(), NullLogger<IndicatorOrchestrator>.Instance));
    }

    /// <summary>The one buffer dispatched for this series — and it must be exactly one, so a
    /// recalculation loop cannot pass as a recalculation.</summary>
    private static SeriesDataBuffer SoleDispatchFor(MockWorkspaceStore store, string seriesId)
    {
        var dispatched = store.DispatchedActions
            .OfType<UpdateSeriesDataAction>().Where(u => u.SeriesId == seriesId).ToList();
        Assert.Single(dispatched);
        return dispatched[0].Data;
    }

    // ── Q6: what must be true for Shift+Arrow to reach the manager at all ──

    private static WorkspaceState LoadedChart()
    {
        var bars = Bars(10);
        var cfg = new SeriesConfig { Id = "candles", IndicatorCode = "candles", Name = "Price" };
        cfg.Components.Add(new ComponentConfig { Name = "Body", DisplayName = "Body", IsVisible = true });
        var candles = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "candles" });
        return WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(candles),
            PrimarySeriesId = "candles",
            FocusedSeriesId = "candles",
            CurrentDataIndex = 4,
        };
    }

    [Fact]
    public void Q6_NudgeIsDroppedSilently_WithoutChartFocus_AndWhileAModalIsOpen()
    {
        // (a) chart never focused → the chord is swallowed with no sound at all.
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(LoadedChart());
        var dispatcher = new AccessibleTrader.Core.Services.Input.CommandDispatcher(
            bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(),
            new AccessibleTrader.Core.Services.Input.IndicatorCrossingEngine(store, bus));

        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);
        _out.WriteLine($"(a) no chart focus: nudge events={bus.Log.OfType<NudgeDrawingAnchorEvent>().Count()} " +
                       $"feedback={bus.Log.OfType<FeedbackRequestEvent>().Count()}");
        Assert.Empty(bus.Log.OfType<NudgeDrawingAnchorEvent>());
        Assert.Empty(bus.Log.OfType<FeedbackRequestEvent>());

        // (b) chart focused → the event reaches the manager.
        dispatcher.SetChartActive(true);
        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);
        _out.WriteLine($"(b) chart focused: nudge events={bus.Log.OfType<NudgeDrawingAnchorEvent>().Count()}");
        Assert.Single(bus.Log.OfType<NudgeDrawingAnchorEvent>());

        // (c) a modal open (the Object Tree is one) → swallowed again.
        bus.Publish(new ModalStateChangedEvent(true, "Object tree"));
        int before = bus.Log.OfType<NudgeDrawingAnchorEvent>().Count();
        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);
        int after = bus.Log.OfType<NudgeDrawingAnchorEvent>().Count();
        _out.WriteLine($"(c) Object tree open: nudge events before={before} after={after}");
        Assert.Equal(before, after);
    }
}
