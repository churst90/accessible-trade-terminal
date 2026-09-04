using System.Reactive.Concurrency;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Drawing;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The keyboard nudge for drawing anchors (Shift+Arrow, Ctrl+Alt+Shift+G, Ctrl+Alt+Shift+B).
///
/// <para>Until 2026-09-03 an existing drawing's anchors could be moved only by a ten-pixel mouse
/// drag or by typing an absolute value into Properties. These tests pin the contract the nudge
/// was designed to: a bar-index step in time (weekends are stepped over, not landed on), a
/// price step that scales with the visible range and never falls below what speech can voice,
/// ONE sentence per settled run rather than one per press, one undo entry per run, and a spoken
/// refusal for every press that cannot act.</para>
///
/// <para>Time is virtual: the settle window runs on a <see cref="HistoricalScheduler"/>, so
/// "five presses then silence until they settle" is a deterministic assertion rather than a
/// stopwatch.</para>
/// </summary>
public sealed class DrawingAnchorNudgeTests
{
    private sealed class StubDrawingService : IDrawingService
    {
        public int Recomputes;
        public Dictionary<string, double[]> CalculateDrawingData(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            Recomputes++;
            return new();
        }
    }

    private sealed class EarconCounter : IEarconService
    {
        public int Infos, Boundaries, Errors;
        public void PlayAlert(bool breakThroughMutes = false) { }
        public void PlayBoundary() => Boundaries++;
        public void PlayError(ErrorSeverity severity) => Errors++;
        public void PlayRetry() { }
        public void PlaySuccess() { }
        public void PlayConnectionState(ConnectionState state) { }
        public void PlayInfo() => Infos++;
        public void PlayNewBar() { }
        public void PlaySetupBell(OrderSide side, bool isLeg) { }
        public void PlaySetupArmed(OrderSide side) { }
        public void PlaySetupEntryReached(OrderSide side) { }
        public void PlayOrderFill(OrderSide side) { }
        public void PlayStopHit() { }
        public void PlayTakeProfitHit() { }
    }

    private sealed class Harness
    {
        public WorkspaceStore Store { get; }
        public SpyEventBus Bus { get; } = new();
        public DrawingInteractionManager Manager { get; }
        public List<Ohlcv> Bars { get; } = new();
        public ChartUndoStack Undo { get; } = new();
        public EarconCounter Earcons { get; } = new();
        public HistoricalScheduler Clock { get; } = new();
        public StubDrawingService Drawing { get; } = new();

        /// <summary>Thirty WEEKDAY daily bars from Thursday 2026-01-01 — the gaps are the point:
        /// a bar-index step from Friday lands on Monday, a date step would land on Saturday.</summary>
        public Harness()
        {
            Store = new WorkspaceStore(Bus, new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());
            var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            while (Bars.Count < 30)
            {
                if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    Bars.Add(new Ohlcv(day, 100, 101, 99, 100.5, 1000));
                day = day.AddDays(1);
            }
            Store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(Bars), IsInitialLoad: true));
            Manager = new DrawingInteractionManager(
                Bus, Drawing, Store,
                new IndicatorModelFactory(new MockStylingService(), new MockIndicatorPreferencesService()),
                new BlazorInputService(),
                undo: Undo, earcons: Earcons, nudgeScheduler: Clock);
        }

        /// <summary>Named the way production names a drawing — the enum spelling with an
        /// ordinal, "TrendLine (1)" — so the spoken form "Trend line 1" is the code's doing,
        /// not the fixture's.</summary>
        public ChartSeries AddDrawing(DrawingType type, string id = "d1", string? name = null, Action<DrawingData>? shape = null)
        {
            name ??= $"{type} (1)";
            var d = new DrawingData
            {
                Type = type,
                AnchorDate1 = Bars[1].Date,  AnchorPrice1 = 100,   // Friday 2 Jan
                AnchorDate2 = Bars[10].Date, AnchorPrice2 = 100.9,
            };
            shape?.Invoke(d);
            var config = new SeriesConfig { Id = id, Name = name, FriendlyName = name, IndicatorCode = "DRAWING" };
            var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = id }) { Drawing = d };
            Store.Dispatch(new AddSeriesAction(series));
            Store.Dispatch(new SelectSeriesAction(id));
            return Store.State.ActiveSeries.First(s => s.Id == id);
        }

        public void Nudge(AnchorNudgeDirection dir, int times = 1)
        {
            for (int i = 0; i < times; i++) Bus.Publish(new NudgeDrawingAnchorEvent(dir));
        }
        public void Cycle() => Bus.Publish(new CycleDrawingAnchorEvent());
        public void Snap() => Bus.Publish(new SnapDrawingAnchorEvent());
        public void Settle() => Clock.AdvanceBy(DrawingInteractionManager.NudgeSettleWindow + TimeSpan.FromMilliseconds(1));

        public List<FeedbackRequestEvent> Feedback(FeedbackType type) =>
            Bus.Log.OfType<FeedbackRequestEvent>().Where(e => e.Type == type).ToList();
        public List<string> Spoken(FeedbackType type) => Feedback(type).Select(e => e.Message ?? "").ToList();
        public DrawingData Drawn(string id = "d1") => Store.State.ActiveSeries.First(s => s.Id == id).Drawing!;

        /// <summary>The stamp as the chart's own navigation readback speaks it (Speech Order
        /// default = full date and time, in display time).</summary>
        public static string Stamp(DateTime d) => SpeechTimeFormatter.Format(d, SpeechTimeFormatter.DateTimeFormat);
    }

    // ── Time steps are bar indices ───────────────────────────────────────

    [Fact]
    public void Right_moves_to_the_next_BAR_which_on_a_Friday_is_Monday()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        Assert.Equal(DayOfWeek.Friday, h.Drawn().AnchorDate1!.Value.DayOfWeek);

        h.Nudge(AnchorNudgeDirection.Later);

        Assert.Equal(h.Bars[2].Date, h.Drawn().AnchorDate1);
        Assert.Equal(DayOfWeek.Monday, h.Drawn().AnchorDate1!.Value.DayOfWeek);
        Assert.Equal(1, h.Earcons.Infos);
        Assert.Contains(h.Bus.Log, e => e is RedrawEvent);
    }

    [Fact]
    public void Left_from_the_first_bar_is_a_boundary_and_moves_nothing()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine, shape: d => d.AnchorDate1 = h.Bars[0].Date);

        h.Nudge(AnchorNudgeDirection.Earlier, 13);   // a held key at the edge

        Assert.Equal(h.Bars[0].Date, h.Drawn().AnchorDate1);
        Assert.Equal(13, h.Earcons.Boundaries);          // the edge is heard on every press...
        Assert.Empty(h.Spoken(FeedbackType.StateChange));
        h.Settle();
        var spoken = Assert.Single(h.Spoken(FeedbackType.StateChange));   // ...and said once
        Assert.Equal("Start of Trend line 1 is at the first bar.", spoken);
        Assert.Empty(h.Feedback(FeedbackType.Boundary));  // no second earcon rides the sentence
        Assert.Equal(0, h.Earcons.Infos);
        Assert.False(h.Undo.CanUndo);
    }

    [Fact]
    public void Right_past_the_last_bar_projects_into_the_margin_and_stops_at_its_end()
    {
        var h = new Harness();
        int margin = h.Store.State.RightMarginBars;
        Assert.True(margin > 0, "the fixture needs a right margin to project into");
        h.AddDrawing(DrawingType.TrendLine, shape: d => d.AnchorDate1 = h.Bars[^1].Date);

        for (int k = 1; k <= margin; k++)
        {
            h.Nudge(AnchorNudgeDirection.Later);
            Assert.Equal(DrawingInteractionManager.ProjectFutureDate(h.Bars, k), h.Drawn().AnchorDate1);
        }
        h.Nudge(AnchorNudgeDirection.Later);

        Assert.Equal(DrawingInteractionManager.ProjectFutureDate(h.Bars, margin), h.Drawn().AnchorDate1);
        h.Settle();
        Assert.Contains("end of the chart's right margin", h.Spoken(FeedbackType.StateChange).Single());

        // And back: a projected anchor steps left through the same dates, then onto real bars.
        h.Nudge(AnchorNudgeDirection.Earlier, margin);
        Assert.Equal(h.Bars[^1].Date, h.Drawn().AnchorDate1);
        h.Nudge(AnchorNudgeDirection.Earlier);
        Assert.Equal(h.Bars[^2].Date, h.Drawn().AnchorDate1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void BarIndexOf_is_the_inverse_of_ProjectFutureDate(int offset)
    {
        var h = new Harness();
        var projected = DrawingInteractionManager.ProjectFutureDate(h.Bars, offset);
        Assert.Equal(h.Bars.Count - 1 + offset, DrawingInteractionManager.BarIndexOf(h.Bars, projected));
    }

    // ── Price steps ──────────────────────────────────────────────────────

    [Fact]
    public void Up_moves_by_one_percent_of_the_visible_range()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        var (min, max) = h.Store.State.ViewportRange;
        Assert.True(max > min, "the store must have computed a visible range");
        double expected = 100 + DrawingInteractionManager.PriceNudgeStep(max - min, 100);

        h.Nudge(AnchorNudgeDirection.Up);

        Assert.Equal(expected, h.Drawn().AnchorPrice1!.Value, 10);
        Assert.NotEqual(100, h.Drawn().AnchorPrice1!.Value);
    }

    [Theory]
    [InlineData(0.0,   100.0,  0.01)]     // no visible range at all: the floor
    [InlineData(0.5,   100.0,  0.01)]     // 1% of 0.5 is 0.005, below what "100.00" can voice
    [InlineData(200.0, 100.0,  2.0)]      // 1% of 200
    [InlineData(200.0, 0.0363, 2.0)]      // sub-dollar asset on a wide range: still 1%
    [InlineData(0.001, 0.0363, 0.0001)]   // ...and on a tight one, the floor is its 4th decimal
    public void A_price_step_is_one_percent_of_the_range_floored_at_the_last_spoken_decimal(double range, double price, double expected)
    {
        Assert.Equal(expected, DrawingInteractionManager.PriceNudgeStep(range, price), 12);
    }

    [Fact]
    public void The_floor_is_exactly_what_FormatPrice_can_distinguish()
    {
        // The step must change the SPOKEN string, or the key is indistinguishable from a dead one.
        foreach (double price in new[] { 100.0, 0.0363, 0.00003, 65000.0 })
        {
            double unit = DrawingInteractionManager.SpeechUnitInLastPlace(price);
            Assert.NotEqual(SpeechPriceFormatter.FormatPrice(price), SpeechPriceFormatter.FormatPrice(price + unit));
        }
    }

    // ── Speech: once per settled run, an earcon per press ────────────────

    [Fact]
    public void Five_presses_are_five_earcons_and_ONE_sentence_after_they_settle()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);

        h.Nudge(AnchorNudgeDirection.Up, 5);

        Assert.Equal(5, h.Earcons.Infos);
        Assert.Empty(h.Feedback(FeedbackType.StateChange));   // nothing spoken while the keys are moving

        h.Settle();

        var spoken = Assert.Single(h.Spoken(FeedbackType.StateChange));
        Assert.Equal(
            $"Start: {SpeechPriceFormatter.FormatPrice(h.Drawn().AnchorPrice1!.Value)} at {Harness.Stamp(h.Bars[1].Date)}. Trend line 1, anchor 1 of 2.",
            spoken);
    }

    [Fact]
    public void A_projected_anchor_says_how_far_past_the_last_bar_it_is()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine, shape: d => d.AnchorDate1 = h.Bars[^1].Date);
        h.Nudge(AnchorNudgeDirection.Later, 3); h.Settle();
        Assert.Contains(", 3 bars past the last bar. Trend line 1, anchor 1 of 2.", h.Spoken(FeedbackType.StateChange).Single());
    }

    [Fact]
    public void The_next_navigation_key_cancels_the_pending_sentence_but_the_undo_entry_is_still_filed()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        h.Nudge(AnchorNudgeDirection.Later, 2);
        // The user presses Right: the bar readback must not be cut off 300 ms later.
        h.Bus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, null, true, IsXMove: true));
        h.Settle();

        Assert.Empty(h.Spoken(FeedbackType.StateChange));
        Assert.Equal("Move Trend line 1", h.Undo.NextUndoDescription);
    }

    [Fact]
    public void A_modal_opening_settles_the_run_silently()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        h.Nudge(AnchorNudgeDirection.Up, 2);
        h.Bus.Publish(new ModalStateChangedEvent(true, "Properties"));
        h.Settle();

        Assert.Empty(h.Spoken(FeedbackType.StateChange));
        Assert.True(h.Undo.CanUndo);
    }

    [Fact]
    public void Cycling_inside_the_window_never_reads_the_same_numbers_twice()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        h.Nudge(AnchorNudgeDirection.Up, 2);
        h.Cycle();                         // speaks anchor 1 at once, and the pending run must not repeat it
        h.Settle();
        Assert.Single(h.Spoken(FeedbackType.StateChange));
        Assert.True(h.Undo.CanUndo);
    }

    [Fact]
    public void The_readback_rides_the_Manual_channel_like_every_other_answer_to_a_keypress()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        h.Nudge(AnchorNudgeDirection.Up);
        h.Settle();
        var e = Assert.Single(h.Feedback(FeedbackType.StateChange));
        // StateChange with no explicit channel → AccessibilityFeedbackCoordinator speaks it on Manual.
        Assert.Null(e.Channel);
        Assert.True(e.Interrupt);
    }

    // ── Undo: one entry per run, extended by the next run ────────────────

    [Fact]
    public void A_run_of_presses_is_one_undo_entry_and_undo_restores_where_the_user_found_it()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        var original = h.Drawn().AnchorDate1;

        h.Nudge(AnchorNudgeDirection.Later, 3);
        h.Settle();

        Assert.True(h.Undo.CanUndo);
        Assert.Equal("Move Trend line 1", h.Undo.NextUndoDescription);
        Assert.Equal(h.Bars[4].Date, h.Drawn().AnchorDate1);

        h.Undo.Undo();
        Assert.Equal(original, h.Drawn().AnchorDate1);
        Assert.False(h.Undo.CanUndo);
    }

    [Fact]
    public void A_second_run_on_the_same_anchor_extends_the_entry_instead_of_adding_one()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        var original = h.Drawn().AnchorDate1;

        h.Nudge(AnchorNudgeDirection.Later, 3); h.Settle();
        h.Nudge(AnchorNudgeDirection.Later, 2); h.Settle();
        Assert.Equal(h.Bars[6].Date, h.Drawn().AnchorDate1);

        Assert.True(h.Undo.Undo());
        Assert.Equal(original, h.Drawn().AnchorDate1);
        Assert.False(h.Undo.CanUndo);

        Assert.True(h.Undo.Redo());
        Assert.Equal(h.Bars[6].Date, h.Drawn().AnchorDate1);
    }

    [Fact]
    public void Another_edit_in_between_ends_the_run_so_undo_stays_in_order()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        h.Nudge(AnchorNudgeDirection.Later, 2); h.Settle();
        // Some other edit lands on the stack.
        h.Undo.Push(new SeriesDeleteUndo("Delete SMA", h.Store.State.ActiveSeries.First(), _ => { }, _ => { }));
        h.Nudge(AnchorNudgeDirection.Later, 2); h.Settle();

        Assert.Equal("Move Trend line 1", h.Undo.NextUndoDescription);
        h.Undo.Undo();
        Assert.Equal("Delete SMA", h.Undo.NextUndoDescription);
        h.Undo.Undo();
        Assert.Equal("Move Trend line 1", h.Undo.NextUndoDescription);
    }

    [Fact]
    public void Ctrl_Z_inside_the_window_reverses_the_pending_run_too_and_never_files_a_forward_move()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        var original = h.Drawn().AnchorDate1;
        h.Nudge(AnchorNudgeDirection.Later, 3); h.Settle();      // entry 1 filed
        h.Nudge(AnchorNudgeDirection.Later, 2);                  // pending, not yet filed

        // ChartCommandManager handles Ctrl+Z AFTER this manager sees the event.
        h.Bus.Publish(new UndoChartEditEvent());
        Assert.True(h.Undo.Undo());
        h.Settle();

        Assert.Equal(original, h.Drawn().AnchorDate1);          // both runs reversed
        Assert.False(h.Undo.CanUndo, "the settle must not file the reversal as a new move");
        Assert.Single(h.Spoken(FeedbackType.StateChange));      // run 1's readback only; nothing talks over 'Undone: …'
    }

    [Fact]
    public void A_mouse_drag_reads_back_with_the_same_sentence_and_shares_the_undo_entry()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        var state = h.Store.State;
        double width = 1280, height = 720;
        double x = ((10 - state.ViewportStartIndex) / (double)state.ViewportLength) * width;
        var (min, max) = state.ViewportRange;
        double y = (1 - (100.9 - min) / (max - min)) * height;
        h.Manager.HandleMouseEvent(x, y, "MouseDown", width, height);
        h.Manager.HandleMouseEvent(x, y + 40, "MouseMove", width, height);
        h.Manager.HandleMouseEvent(x, y + 40, "MouseUp", width, height);

        var said = Assert.Single(h.Spoken(FeedbackType.StateChange));
        Assert.StartsWith("End: ", said);
        Assert.EndsWith("Trend line 1, anchor 2 of 2.", said);
        Assert.DoesNotContain(h.Bus.Log, e => e is AnnouncementEvent);
        Assert.Equal("Move Trend line 1", h.Undo.NextUndoDescription);

        h.Nudge(AnchorNudgeDirection.Later, 2); h.Settle();      // nudges after the drag extend it
        Assert.True(h.Undo.Undo());
        Assert.Equal(100.9, h.Drawn().AnchorPrice2!.Value, 6);
        Assert.Equal(h.Bars[10].Date, h.Drawn().AnchorDate2);
        Assert.False(h.Undo.CanUndo);
    }

    [Fact]
    public void A_new_drawing_is_named_in_words_not_in_CamelCase()
    {
        var h = new Harness();
        h.Manager.HandleAddDrawing("TrendLine", h.Bars);          // anchor 1 at the cursor
        h.Store.Dispatch(new SetCursorAction(5));
        h.Manager.HandleAddDrawing("TrendLine", h.Bars);          // anchor 2: the line is created
        var created = h.Store.State.ActiveSeries.Last(s => s.IsDrawing);
        Assert.Equal("Trend line (1)", created.Name);
        Assert.Equal("Trend line 1", DrawingInteractionManager.SpokenName(created));
    }

    [Fact]
    public void Cycling_to_another_anchor_starts_a_new_entry()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        h.Nudge(AnchorNudgeDirection.Up, 2); h.Settle();
        h.Cycle();
        h.Nudge(AnchorNudgeDirection.Up, 2); h.Settle();

        Assert.True(h.Undo.Undo());
        Assert.Equal(100.9, h.Drawn().AnchorPrice2!.Value, 10);   // anchor 2 back
        Assert.NotEqual(100.0, h.Drawn().AnchorPrice1!.Value);    // anchor 1 still moved
        Assert.True(h.Undo.Undo());
        Assert.Equal(100.0, h.Drawn().AnchorPrice1!.Value, 10);
    }

    // ── Cycle ────────────────────────────────────────────────────────────

    [Fact]
    public void The_first_cycle_press_names_anchor_1_and_the_next_moves_on_and_wraps()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);

        h.Cycle(); h.Cycle(); h.Cycle();

        var spoken = h.Spoken(FeedbackType.StateChange);
        Assert.Equal(3, spoken.Count);
        Assert.Equal(3, h.Earcons.Infos);   // under F2 the sentence is muted; the key still ticks
        Assert.Equal($"Start: 100.00 at {Harness.Stamp(h.Bars[1].Date)}. Trend line 1, anchor 1 of 2.", spoken[0]);
        Assert.Equal($"End: 100.90 at {Harness.Stamp(h.Bars[10].Date)}. Trend line 1, anchor 2 of 2.", spoken[1]);
        Assert.StartsWith("Start: ", spoken[2]);
        Assert.EndsWith("Trend line 1, anchor 1 of 2.", spoken[2]);
    }

    [Fact]
    public void After_cycling_the_nudge_moves_the_selected_anchor_not_the_first()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        h.Cycle();   // names anchor 1
        h.Cycle();   // selects anchor 2

        h.Nudge(AnchorNudgeDirection.Later);

        Assert.Equal(h.Bars[1].Date, h.Drawn().AnchorDate1);
        Assert.Equal(h.Bars[11].Date, h.Drawn().AnchorDate2);
    }

    /// <summary>
    /// Cycling MOVES THE CHART CURSOR to the anchor it selects.
    ///
    /// <para>Reported from real use on 2026-09-03: "Ctrl+Alt+Shift+G cycles between the
    /// beginning and end of the trend line, but it only puts the cursor at the beginning."
    /// It put the cursor nowhere — the key selected an anchor and spoke it while the chart's
    /// own position never moved, so whichever anchor happened to be near where the user was
    /// standing felt like the only one it could reach.</para>
    ///
    /// <para>Bar 10 is asserted, not "not bar 1": a cursor that moved to the wrong bar is the
    /// failure this is guarding, and it passes any test phrased as a difference.</para>
    /// </summary>
    [Fact]
    public void Cycling_puts_the_chart_cursor_on_the_selected_anchors_bar()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);          // anchors on bar 1 and bar 10
        h.Store.Dispatch(new SetCursorAction(20));

        h.Cycle();                                     // names anchor 1 — and goes there
        Assert.Equal(1, h.Store.State.CurrentDataIndex);

        h.Cycle();                                     // anchor 2
        Assert.Equal(10, h.Store.State.CurrentDataIndex);

        h.Cycle();                                     // wraps back to anchor 1
        Assert.Equal(1, h.Store.State.CurrentDataIndex);
    }

    /// <summary>
    /// The jump scrolls the viewport when the anchor is off-screen.
    ///
    /// <para>This is why the manager dispatches <c>NavigateAction</c> and not
    /// <c>SetCursorAction</c>: <c>SetCursorAction</c> runs through <c>CursorOnlyJump</c>, which
    /// CLAMPS the target to the current viewport. An anchor scrolled off the left edge would
    /// land the cursor on the leftmost visible bar — a plausible wrong answer, and the one the
    /// user was already complaining about.</para>
    /// </summary>
    [Fact]
    public void Cycling_scrolls_the_viewport_when_the_anchor_is_off_screen()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);          // anchors on bar 1 and bar 10
        // Narrow the window, then pan right so bar 1 is off the left edge.
        h.Store.Dispatch(new ZoomAction(15));
        h.Store.Dispatch(new PanAction(h.Bars.Count));
        h.Store.Dispatch(new SetCursorAction(int.MaxValue));
        Assert.True(h.Store.State.ViewportStartIndex > 1,
            $"fixture must leave anchor 1 off-screen; start={h.Store.State.ViewportStartIndex} len={h.Store.State.ViewportLength}");

        h.Cycle();

        Assert.Equal(1, h.Store.State.CurrentDataIndex);
        Assert.True(h.Store.State.ViewportStartIndex <= 1,
            "the viewport must follow the cursor; a clamp to the visible range is the defect this replaced");
    }

    /// <summary>
    /// An anchor with no bar to stand on leaves the cursor alone, and still speaks.
    ///
    /// <para><c>BarIndexOf</c> answers 0 for any date BEFORE <c>data[0]</c> — recorded defect
    /// n8 — so an anchor dragged off the left of the loaded history would otherwise send the
    /// cursor to bar 0 and let the sentence describe it in the grammar of "start anchor". The
    /// sentence still carries the anchor's own date, which is the honest answer either way.</para>
    /// </summary>
    [Fact]
    public void Cycling_to_an_anchor_outside_the_loaded_bars_does_not_move_the_cursor()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine, shape: d => d.AnchorDate1 = h.Bars[0].Date.AddYears(-1));
        h.Store.Dispatch(new SetCursorAction(20));
        int before = h.Store.State.CurrentDataIndex;

        h.Cycle();                                     // anchor 1: a year before bar 0

        Assert.Equal(before, h.Store.State.CurrentDataIndex);
        Assert.Single(h.Spoken(FeedbackType.StateChange));   // it is not silent about it

        h.Cycle();                                     // anchor 2 is a real bar and does move
        Assert.Equal(10, h.Store.State.CurrentDataIndex);
    }

    [Fact]
    public void A_single_anchor_drawing_says_so_instead_of_pretending_to_cycle()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.HorizontalLine, shape: d => { d.AnchorDate2 = null; d.AnchorPrice2 = null; });
        h.Cycle(); h.Cycle();
        var spoken = h.Spoken(FeedbackType.StateChange);
        Assert.All(spoken, s => Assert.Equal("Anchor 1: 100.00. Horizontal line 1, anchor 1 of 1. This drawing has one anchor.", s));
    }

    [Fact]
    public void A_risk_reward_names_its_anchors_by_what_they_are()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.RiskReward, shape: d =>
        {
            d.AnchorPrice1 = 100; d.AnchorPrice2 = 95; d.AnchorPrice3 = 110;
        });
        h.Cycle(); h.Cycle(); h.Cycle();
        var spoken = h.Spoken(FeedbackType.StateChange);
        Assert.Equal("Entry: 100.00. Risk/reward 1, anchor 1 of 3.", spoken[0]);
        Assert.Equal("Stop loss: 95.00. Risk/reward 1, anchor 2 of 3.", spoken[1]);
        Assert.Equal("Take profit: 110.00. Risk/reward 1, anchor 3 of 3.", spoken[2]);
    }

    // ── Refusals: never silent ───────────────────────────────────────────

    [Fact]
    public void A_price_only_anchor_refuses_Left_with_a_reason_and_a_hint()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.RiskReward, shape: d => d.AnchorPrice3 = 110);
        h.Nudge(AnchorNudgeDirection.Earlier);
        Assert.Equal(1, h.Earcons.Boundaries);
        h.Settle();
        Assert.Equal("Entry of Risk/reward 1 has no date to move. Up and Down move its price.", h.Spoken(FeedbackType.StateChange).Single());
        Assert.Empty(h.Feedback(FeedbackType.Error));
        Assert.Equal(h.Bars[1].Date, h.Drawn().AnchorDate1);
    }

    [Fact]
    public void A_date_only_anchor_refuses_Up_with_a_reason_and_a_hint()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.VerticalLine, shape: d => { d.AnchorPrice1 = null; d.AnchorDate2 = null; d.AnchorPrice2 = null; });
        h.Nudge(AnchorNudgeDirection.Up); h.Settle();
        Assert.Contains("has no price to move. Left and Right move its date.", h.Spoken(FeedbackType.StateChange).Single());
    }

    [Fact]
    public void With_a_non_drawing_focused_every_key_says_how_to_focus_one()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        h.Store.Dispatch(new SelectSeriesAction("candles"));

        h.Nudge(AnchorNudgeDirection.Up); h.Settle();   // a nudge coalesces its refusal
        h.Cycle();                                       // a deliberate press answers at once
        h.Snap();

        const string why = "Focus a drawing first. Page Up and Page Down move between series.";
        Assert.Equal(why, h.Spoken(FeedbackType.StateChange).Single());
        Assert.Equal(new[] { why, why }, h.Spoken(FeedbackType.Boundary));
        Assert.Empty(h.Feedback(FeedbackType.Error));   // never the failure earcon for a refusal
        Assert.Equal(1, h.Earcons.Boundaries);
        Assert.Equal(100.0, h.Drawn().AnchorPrice1);
    }

    [Fact]
    public void A_placement_in_progress_refuses_so_the_state_machine_is_not_desynchronised()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        h.Manager.HandleAddDrawing("Channel", h.Bars);   // anchor 1 of a new channel is now pending
        h.Nudge(AnchorNudgeDirection.Later); h.Settle();
        Assert.Contains("Finish or cancel the drawing in progress first", h.Spoken(FeedbackType.StateChange).Single());
        Assert.Equal(h.Bars[1].Date, h.Drawn().AnchorDate1);
    }

    [Fact]
    public void Grabbing_a_handle_with_the_mouse_selects_that_anchor_for_the_keyboard()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        // Anchor 2 sits on bar 10 at 100.9. Find its pixel and press the mouse there.
        var state = h.Store.State;
        double width = 1280, height = 720;
        double x = ((10 - state.ViewportStartIndex) / (double)state.ViewportLength) * width;
        var (min, max) = state.ViewportRange;
        double y = (1 - (100.9 - min) / (max - min)) * height;
        h.Manager.HandleMouseEvent(x, y, "MouseDown", width, height);
        h.Manager.HandleMouseEvent(x, y, "MouseUp", width, height);

        h.Nudge(AnchorNudgeDirection.Later); h.Settle();

        Assert.EndsWith("Trend line 1, anchor 2 of 2.", h.Spoken(FeedbackType.StateChange).Last());
        Assert.Equal(h.Bars[1].Date, h.Drawn().AnchorDate1);
    }

    [Fact]
    public void The_context_summary_can_name_the_selected_anchor_without_moving_it()
    {
        var h = new Harness();
        Assert.Null(h.Manager.SelectedAnchorSummary());
        h.AddDrawing(DrawingType.TrendLine);
        Assert.Equal($"Selected anchor, Start: 100.00 at {Harness.Stamp(h.Bars[1].Date)}. Trend line 1, anchor 1 of 2.",
            h.Manager.SelectedAnchorSummary());
        h.Cycle(); h.Cycle();
        Assert.StartsWith("Selected anchor, End: 100.90", h.Manager.SelectedAnchorSummary());
        Assert.Equal(100.9, h.Drawn().AnchorPrice2);
    }

    [Fact]
    public void Shift_F1_context_summary_appends_the_selected_anchor()
    {
        // The read-without-move: the coordinator asks the drawing manager and appends its answer.
        var bus = new SpyEventBus();
        var speech = new SpySpeechRouter();
        var store = new MockWorkspaceStore();
        var config = new SeriesConfig { Id = "d1", Name = "TrendLine (1)", FriendlyName = "TrendLine Drawing", IndicatorCode = "DRAWING" };
        var drawing = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "d1" }) { Drawing = new DrawingData { Type = DrawingType.TrendLine, AnchorPrice1 = 100 } };
        store.EmitState(WorkspaceState.Initial with
        {
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create(drawing),
            FocusedSeriesId = "d1",
        });
        var drawings = Substitute.For<IDrawingInteractionManager>();
        drawings.SelectedAnchorSummary().Returns("Selected anchor, End: 105.20. TrendLine 1, anchor 2 of 2.");
        var coordinator = new AccessibilityFeedbackCoordinator(
            store, new MockNavManager(), speech, new MockAudioRouter(), new SpeechFormatter(),
            bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
            new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
            new ChartPatternFocus(), new MockAutoNarrationService(), drawings: drawings);
        Assert.NotNull(coordinator);

        bus.Publish(new ContextSummaryRequestEvent());

        var said = Assert.Single(speech.SpokenTexts);
        Assert.EndsWith(". Selected anchor, End: 105.20. TrendLine 1, anchor 2 of 2.", said);
    }

    [Theory]
    [InlineData("TrendLine (2)", "Trend line 2")]           // saved before 2026-09-03: enum spelling
    [InlineData("Trend line (2)", "Trend line 2")]          // created since
    [InlineData("Weekly resistance", "Weekly resistance")]  // renamed in Properties
    [InlineData("", "Trend line Drawing")]
    public void The_drawing_is_named_the_way_Page_Up_and_Page_Down_name_it(string name, string spoken)
    {
        var config = new SeriesConfig { Id = "x", Name = name, FriendlyName = "TrendLine Drawing", IndicatorCode = "DRAWING" };
        var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "x" }) { Drawing = new DrawingData { Type = DrawingType.TrendLine } };
        Assert.Equal(spoken, DrawingInteractionManager.SpokenName(series));
    }

    // ── Selection resets ─────────────────────────────────────────────────

    [Fact]
    public void Focusing_a_different_drawing_resets_the_selection_to_its_first_anchor()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine, id: "a", name: "Line A");
        h.AddDrawing(DrawingType.TrendLine, id: "b", name: "Line B");
        h.Store.Dispatch(new SelectSeriesAction("a"));
        h.Cycle(); h.Cycle();                          // Line A, anchor 2 selected
        h.Store.Dispatch(new SelectSeriesAction("b"));

        h.Nudge(AnchorNudgeDirection.Later); h.Settle();

        Assert.EndsWith("Line B, anchor 1 of 2.", h.Spoken(FeedbackType.StateChange).Last());
        Assert.Equal(h.Bars[2].Date, h.Drawn("b").AnchorDate1);
        Assert.Equal(h.Bars[10].Date, h.Drawn("b").AnchorDate2);
    }

    [Fact]
    public void A_tab_switch_resets_the_selection()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        h.Cycle(); h.Cycle();                          // anchor 2 selected
        h.Bus.Publish(new TabSwitchedEvent(0, "Tab 1"));

        h.Nudge(AnchorNudgeDirection.Up); h.Settle();

        Assert.EndsWith("Trend line 1, anchor 1 of 2.", h.Spoken(FeedbackType.StateChange).Last());
        Assert.StartsWith("Start: ", h.Spoken(FeedbackType.StateChange).Last());
    }

    [Fact]
    public void Removing_the_drawing_mid_run_settles_with_nothing_to_say_and_nothing_to_undo()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine);
        h.Nudge(AnchorNudgeDirection.Up, 3);
        h.Store.Dispatch(new RemoveSeriesAction("d1"));

        h.Settle();

        Assert.Empty(h.Feedback(FeedbackType.StateChange));
        Assert.False(h.Undo.CanUndo);
    }

    // ── Snap ─────────────────────────────────────────────────────────────

    [Fact]
    public void Snap_goes_to_the_nearest_OHLC_first_then_walks_high_low_open_close()
    {
        var h = new Harness();
        // Anchor 1 sits on Friday's bar (O 100, H 101, L 99, C 100.5) at 100.9: nearest is the high.
        h.AddDrawing(DrawingType.TrendLine, shape: d => d.AnchorPrice1 = 100.9);

        h.Snap(); Assert.Equal(101.0, h.Drawn().AnchorPrice1);
        h.Snap(); Assert.Equal(99.0,  h.Drawn().AnchorPrice1);
        h.Snap(); Assert.Equal(100.0, h.Drawn().AnchorPrice1);
        h.Snap(); Assert.Equal(100.5, h.Drawn().AnchorPrice1);
        h.Snap(); Assert.Equal(101.0, h.Drawn().AnchorPrice1);

        var spoken = h.Spoken(FeedbackType.StateChange);
        Assert.Equal($"Start: 101.00, the high of {Harness.Stamp(h.Bars[1].Date)}. Trend line 1, anchor 1 of 2.", spoken[0]);
        Assert.StartsWith("Start: 99.00, the low of ", spoken[1]);
        Assert.Equal(5, h.Earcons.Infos);
        Assert.Equal(h.Bars[1].Date, h.Drawn().AnchorDate1);   // the date is untouched
    }

    [Fact]
    public void Each_snap_is_its_own_undo_entry_because_each_was_announced_as_a_landing()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine, shape: d => d.AnchorPrice1 = 100.9);
        h.Nudge(AnchorNudgeDirection.Up, 2); h.Settle();
        double nudged = h.Drawn().AnchorPrice1!.Value;
        h.Snap(); h.Snap();                                       // high, then low
        Assert.Equal("Snap Trend line 1", h.Undo.NextUndoDescription);
        h.Undo.Undo(); Assert.Equal(101.0, h.Drawn().AnchorPrice1);   // one level back, not two
        h.Undo.Undo(); Assert.Equal(nudged, h.Drawn().AnchorPrice1);
        Assert.Equal("Move Trend line 1", h.Undo.NextUndoDescription);
        h.Undo.Undo(); Assert.Equal(100.9, h.Drawn().AnchorPrice1);
    }

    [Fact]
    public void A_price_only_anchor_snaps_to_the_cursor_bar()
    {
        var h = new Harness();
        // Bar 5 is the cursor; give it a distinctive high so the assertion cannot pass by accident.
        h.Bars[5] = new Ohlcv(h.Bars[5].Date, 100, 123.4, 99, 100.5, 1000);
        h.Store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(h.Bars), IsInitialLoad: true));
        h.Store.Dispatch(new SetCursorAction(5));
        h.AddDrawing(DrawingType.FibRetracement, shape: d => d.AnchorPrice1 = 120);

        h.Snap();

        Assert.Equal(123.4, h.Drawn().AnchorPrice1);
        Assert.Contains(", the high of the cursor bar, " + Harness.Stamp(h.Bars[5].Date), h.Spoken(FeedbackType.StateChange).Single());
    }

    [Fact]
    public void A_projected_anchor_has_no_bar_to_snap_to_and_says_so()
    {
        var h = new Harness();
        h.AddDrawing(DrawingType.TrendLine, shape: d => d.AnchorDate1 = DrawingInteractionManager.ProjectFutureDate(h.Bars, 3));
        h.Snap();
        Assert.Contains("is past the last bar; there is no bar to snap to", Assert.Single(h.Feedback(FeedbackType.Boundary)).Message);
    }

    // ── Vocabulary ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(DrawingType.TrendLine, 1, "start")]
    [InlineData(DrawingType.TrendLine, 2, "end")]
    [InlineData(DrawingType.RiskReward, 3, "take profit")]
    [InlineData(DrawingType.AndrewsPitchfork, 3, "pivot 3")]
    [InlineData(DrawingType.FibExtension, 3, "projection origin")]
    [InlineData(DrawingType.Rectangle, 2, "bottom")]
    [InlineData(DrawingType.TextLabel, 1, "position")]
    [InlineData(DrawingType.HorizontalLine, 1, "anchor 1")]
    [InlineData(DrawingType.VerticalLine, 1, "anchor 1")]
    [InlineData(DrawingType.AnchoredVwap, 1, "anchor")]
    public void Slot_names_come_from_the_same_vocabulary_as_the_Properties_fields(DrawingType type, int slot, string expected)
    {
        Assert.Equal(expected, DrawingAnchorSchema.SlotName(type, slot));
    }

    [Fact]
    public void Every_declared_type_has_its_slots_in_field_order_and_each_once()
    {
        foreach (var type in DrawingAnchorSchema.DeclaredTypes)
        {
            var slots = DrawingAnchorSchema.Slots(type);
            Assert.Equal(slots.Distinct().Count(), slots.Count);
            Assert.Equal(DrawingAnchorSchema.For(type).Select(f => f.Slot).Distinct(), slots);
        }
    }

    // ── The bindings and the dispatcher ──────────────────────────────────

    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    /// <summary>
    /// The four nudges are on SHIFT+ARROW, with no Alt and no Ctrl.
    ///
    /// <para>They shipped on Alt+Shift+Arrow on 2026-09-03 and moved the same day: Orca takes
    /// Alt+Shift+Arrow for table-cell navigation, so on the desktop this application is built
    /// for the chord never reached the page at all. <c>!b.Alt</c> is asserted, not merely
    /// omitted — the failure that matters is the old chord still being registered ALONGSIDE
    /// the new one, and a test that only checked <c>b.Shift</c> would pass on both.</para>
    /// </summary>
    [Fact]
    public void The_default_profile_binds_the_six_nudge_chords()
    {
        var dir = TestTemp.NewDir("att-nudge-bindings-");
        try
        {
            var mgr = new ShortcutManager(new TempPaths(dir));
            var s = mgr.CurrentProfile.Shortcuts;
            Assert.Contains(s, b => b.Command == SystemCommand.NudgeAnchorEarlier && b.Key == "LEFT"  && b.Shift && !b.Alt && !b.Ctrl);
            Assert.Contains(s, b => b.Command == SystemCommand.NudgeAnchorLater   && b.Key == "RIGHT" && b.Shift && !b.Alt && !b.Ctrl);
            Assert.Contains(s, b => b.Command == SystemCommand.NudgeAnchorUp      && b.Key == "UP"    && b.Shift && !b.Alt && !b.Ctrl);
            Assert.Contains(s, b => b.Command == SystemCommand.NudgeAnchorDown    && b.Key == "DOWN"  && b.Shift && !b.Alt && !b.Ctrl);
            Assert.Contains(s, b => b.Command == SystemCommand.CycleDrawingAnchor && b.Key == "G" && b.Ctrl && b.Alt && b.Shift);
            Assert.Contains(s, b => b.Command == SystemCommand.SnapAnchorToBar    && b.Key == "B" && b.Ctrl && b.Alt && b.Shift);

            // The chord it moved off must be GONE, not merely superseded. Two live bindings on
            // one command is how a "fixed" chord keeps answering to its old spelling.
            Assert.DoesNotContain(s, b => b.Key is "LEFT" or "RIGHT" or "UP" or "DOWN" && b.Alt && b.Shift);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    private static (CommandDispatcher dispatcher, EventBus bus) Dispatcher(bool withData = true)
    {
        var bus = new EventBus();
        var store = Substitute.For<IWorkspaceStore>();
        var bars = new List<Ohlcv> { new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1, 2, 0.5, 1.5, 10) };
        var state = withData ? WorkspaceState.Initial with { Data = new TimeSeriesBuffer<Ohlcv>(bars) } : WorkspaceState.Initial;
        store.State.Returns(_ => state);
        var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
        return (dispatcher, bus);
    }

    [Fact]
    public void The_dispatcher_forwards_each_chord_as_its_event_only_while_the_chart_is_focused()
    {
        var (dispatcher, bus) = Dispatcher();
        var nudges = new List<NudgeDrawingAnchorEvent>();
        int cycles = 0, snaps = 0;
        bus.Subscribe<NudgeDrawingAnchorEvent>(nudges.Add);
        bus.Subscribe<CycleDrawingAnchorEvent>(_ => cycles++);
        bus.Subscribe<SnapDrawingAnchorEvent>(_ => snaps++);

        // Chart not focused: chart-scoped, dropped.
        dispatcher.Dispatch(SystemCommand.NudgeAnchorUp);
        dispatcher.Dispatch(SystemCommand.CycleDrawingAnchor);
        Assert.Empty(nudges); Assert.Equal(0, cycles);

        dispatcher.SetChartActive(true);
        dispatcher.Dispatch(SystemCommand.NudgeAnchorEarlier);
        dispatcher.Dispatch(SystemCommand.NudgeAnchorLater);
        dispatcher.Dispatch(SystemCommand.NudgeAnchorUp);
        dispatcher.Dispatch(SystemCommand.NudgeAnchorDown);
        dispatcher.Dispatch(SystemCommand.CycleDrawingAnchor);
        dispatcher.Dispatch(SystemCommand.SnapAnchorToBar);

        Assert.Equal(new[] { AnchorNudgeDirection.Earlier, AnchorNudgeDirection.Later, AnchorNudgeDirection.Up, AnchorNudgeDirection.Down },
            nudges.Select(n => n.Direction));
        Assert.Equal(1, cycles);
        Assert.Equal(1, snaps);
    }

    [Fact]
    public void With_no_chart_loaded_a_nudge_says_so_instead_of_staying_silent()
    {
        var (dispatcher, bus) = Dispatcher(withData: false);
        var feedback = new List<FeedbackRequestEvent>();
        bus.Subscribe<FeedbackRequestEvent>(feedback.Add);
        dispatcher.SetChartActive(true);

        dispatcher.Dispatch(SystemCommand.NudgeAnchorUp);

        var e = Assert.Single(feedback);
        Assert.Equal("No chart loaded.", e.Message);
        Assert.Equal(FeedbackType.Boundary, e.Type);   // a refusal, not a failure
    }

    [Fact]
    public void An_open_modal_owns_the_arrows_so_a_nudge_is_swallowed()
    {
        var (dispatcher, bus) = Dispatcher();
        int nudges = 0;
        bus.Subscribe<NudgeDrawingAnchorEvent>(_ => nudges++);
        dispatcher.SetChartActive(true);
        bus.Publish(new ModalStateChangedEvent(true, "Properties"));

        dispatcher.Dispatch(SystemCommand.NudgeAnchorUp);

        Assert.Equal(0, nudges);
    }
}
