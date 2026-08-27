using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>Chart edits are reversible.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// <c>grep -rn "Undo\|Redo"</c> over the whole repository returned <b>zero files</b>.
    /// <c>UpdateEditDrag</c> wrote straight into <c>series.Drawing.AnchorDate1/Price1</c> and
    /// overwrote <c>series.Data.ComponentData</c> in place; the pre-drag values were never
    /// captured, so nothing could have restored them. <c>OnDelete</c> published
    /// <c>DeleteSeriesEvent</c> with no confirmation and no way back.
    /// </para>
    ///
    /// <para>
    /// The failure mode is ordinary: the anchor grab tolerance is 10 px, so a user reaching
    /// for the chart to pan catches a handle instead, drags, and a carefully placed trend line
    /// is gone with Ctrl+Z doing nothing. For a keyboard-and-speech user there is not even a
    /// "drag it roughly back" recovery, because they cannot see where it used to be.
    /// </para>
    /// </summary>
    public class ChartUndoStackTests
    {
        private static DrawingData Line(double p1, double p2) => new()
        {
            Type = DrawingType.TrendLine,
            AnchorDate1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            AnchorPrice1 = p1,
            AnchorDate2 = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            AnchorPrice2 = p2,
        };

        private static ChartSeries DrawingSeries(string id, DrawingData d)
        {
            var config = new SeriesConfig { Id = id, Name = "Trend Line", IndicatorCode = "DRAWING" };
            return new ChartSeries(config, new SeriesDataBuffer { SeriesId = id }) { Drawing = d };
        }

        private sealed class Recorder
        {
            public int Applies;
        }

        private static DrawingEditUndo EditOf(ChartSeries series, DrawingData before, DrawingData after,
                                              Recorder? rec = null) =>
            new("Move Trend Line",
                resolve: () => series,
                before: before,
                after: after,
                afterApply: () => { if (rec != null) rec.Applies++; });

        // ── The stack ────────────────────────────────────────────────────────

        [Fact]
        public void An_accidentally_dragged_anchor_comes_back()
        {
            // The filed scenario, end to end.
            var live = Line(100, 110);
            var series = DrawingSeries("d1", live);
            var stack = new ChartUndoStack();

            var before = live.Clone();
            live.AnchorPrice1 = 42;              // the accidental drag
            stack.Push(EditOf(series, before, live.Clone()));

            Assert.True(stack.Undo());
            Assert.Equal(100, series.Drawing!.AnchorPrice1);
        }

        [Fact]
        public void Redo_puts_it_back_where_the_drag_left_it()
        {
            var live = Line(100, 110);
            var series = DrawingSeries("d1", live);
            var stack = new ChartUndoStack();

            var before = live.Clone();
            live.AnchorPrice1 = 42;
            stack.Push(EditOf(series, before, live.Clone()));

            stack.Undo();
            Assert.True(stack.Redo());
            Assert.Equal(42, series.Drawing!.AnchorPrice1);
        }

        [Fact]
        public void Undo_and_redo_alternate_indefinitely()
        {
            var live = Line(100, 110);
            var series = DrawingSeries("d1", live);
            var stack = new ChartUndoStack();

            var before = live.Clone();
            live.AnchorPrice1 = 42;
            stack.Push(EditOf(series, before, live.Clone()));

            for (int i = 0; i < 5; i++)
            {
                Assert.True(stack.Undo());
                Assert.Equal(100, series.Drawing!.AnchorPrice1);
                Assert.True(stack.Redo());
                Assert.Equal(42, series.Drawing!.AnchorPrice1);
            }
        }

        [Fact]
        public void Nothing_to_undo_reports_false_rather_than_pretending()
        {
            var stack = new ChartUndoStack();

            Assert.False(stack.Undo());
            Assert.False(stack.Redo());
            Assert.False(stack.CanUndo);
            Assert.False(stack.CanRedo);
            Assert.Null(stack.NextUndoDescription);

            // The caller speaks "Nothing to undo" off this false. Silence would be
            // indistinguishable from undo being broken for someone who cannot see the chart.
        }

        [Fact]
        public void A_new_edit_discards_the_redo_branch()
        {
            var live = Line(100, 110);
            var series = DrawingSeries("d1", live);
            var stack = new ChartUndoStack();

            var v0 = live.Clone();
            live.AnchorPrice1 = 42;
            stack.Push(EditOf(series, v0, live.Clone()));
            stack.Undo();
            Assert.True(stack.CanRedo);

            var v1 = live.Clone();
            live.AnchorPrice1 = 77;
            stack.Push(EditOf(series, v1, live.Clone()));

            Assert.False(stack.CanRedo);
        }

        [Fact]
        public void The_stack_is_bounded_and_drops_the_oldest()
        {
            var live = Line(0, 0);
            var series = DrawingSeries("d1", live);
            var stack = new ChartUndoStack();

            for (int i = 1; i <= ChartUndoStack.MaxDepth + 10; i++)
            {
                var before = live.Clone();
                live.AnchorPrice1 = i;
                stack.Push(EditOf(series, before, live.Clone()));
            }

            int undone = 0;
            while (stack.Undo()) undone++;

            Assert.Equal(ChartUndoStack.MaxDepth, undone);
        }

        [Fact]
        public void Clear_drops_both_stacks()
        {
            var live = Line(100, 110);
            var series = DrawingSeries("d1", live);
            var stack = new ChartUndoStack();
            var before = live.Clone();
            live.AnchorPrice1 = 42;
            stack.Push(EditOf(series, before, live.Clone()));
            stack.Undo();

            stack.Clear();

            Assert.False(stack.CanUndo);
            Assert.False(stack.CanRedo);
        }

        // ── The edit itself ──────────────────────────────────────────────────

        [Fact]
        public void The_before_snapshot_is_a_copy_not_a_reference_to_the_live_drawing()
        {
            // This is the whole trick. UpdateEditDrag mutates the DrawingData in place, so a
            // "before" that was a reference to it would change as the drag proceeded and undo
            // would restore the dragged position — which is nothing at all.
            var live = Line(100, 110);
            var series = DrawingSeries("d1", live);
            var stack = new ChartUndoStack();

            var before = live.Clone();
            stack.Push(EditOf(series, before, live.Clone()));

            // Mutate AFTER the push, as a drag continuing would.
            live.AnchorPrice1 = 999;

            stack.Undo();
            Assert.Equal(100, series.Drawing!.AnchorPrice1);
        }

        [Fact]
        public void An_edit_restores_every_field_not_only_the_anchors()
        {
            var live = new DrawingData
            {
                Type = DrawingType.RiskReward,
                AnchorDate1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AnchorPrice1 = 100,
                StopLoss = 95,
                TakeProfit = 130,
                RiskRewardRatio = 6,
                Text = "long setup",
                IsLocked = true,
            };
            var series = DrawingSeries("d1", live);
            var stack = new ChartUndoStack();

            var before = live.Clone();
            live.AnchorPrice1 = 42;
            live.StopLoss = 0;
            live.TakeProfit = 0;
            live.Text = "";
            live.IsLocked = false;
            stack.Push(EditOf(series, before, live.Clone()));

            stack.Undo();

            Assert.Equal(100, series.Drawing!.AnchorPrice1);
            Assert.Equal(95, series.Drawing.StopLoss);
            Assert.Equal(130, series.Drawing.TakeProfit);
            Assert.Equal("long setup", series.Drawing.Text);
            Assert.True(series.Drawing.IsLocked);
        }

        [Fact]
        public void Applying_an_edit_asks_the_caller_to_recompute_and_repaint()
        {
            // An undo that moved the anchors but left the drawn line where it was would be
            // worse than no undo — the chart and the data would disagree and only one of them
            // is what the speech layer reads.
            var live = Line(100, 110);
            var series = DrawingSeries("d1", live);
            var rec = new Recorder();
            var stack = new ChartUndoStack();

            var before = live.Clone();
            live.AnchorPrice1 = 42;
            stack.Push(EditOf(series, before, live.Clone(), rec));

            stack.Undo();
            stack.Redo();

            Assert.Equal(2, rec.Applies);
        }

        [Fact]
        public void An_edit_to_a_series_that_has_since_been_deleted_does_nothing()
        {
            // Resurrecting a drawing the user removed on purpose would be a worse surprise
            // than the undo not reaching it.
            var stack = new ChartUndoStack();
            var live = Line(100, 110);

            stack.Push(new DrawingEditUndo(
                "Move Trend Line",
                resolve: () => null,               // gone
                before: live.Clone(),
                after: live.Clone(),
                afterApply: () => throw new InvalidOperationException(
                    "must not repaint for a series that no longer exists")));

            Assert.True(stack.Undo());             // consumed, without throwing
        }

        [Fact]
        public void A_grab_with_no_movement_is_not_an_edit()
        {
            // Otherwise Ctrl+Z spends its first press undoing nothing, which for a speech user
            // is indistinguishable from undo being broken.
            var a = Line(100, 110);
            var b = a.Clone();

            Assert.False(DrawingEditUndo.IsChange(a, b));

            b.AnchorPrice2 = 111;
            Assert.True(DrawingEditUndo.IsChange(a, b));
        }

        // ── Deleted series ───────────────────────────────────────────────────

        [Fact]
        public void A_deleted_series_is_restored_by_undo_and_removed_again_by_redo()
        {
            var series = DrawingSeries("d1", Line(100, 110));
            var stack = new ChartUndoStack();
            var restored = new List<string>();
            var removed = new List<string>();

            stack.Push(new SeriesDeleteUndo(
                "Delete Trend Line", series,
                restore: s => restored.Add(s.Id),
                remove: id => removed.Add(id)));

            stack.Undo();
            Assert.Equal(new[] { "d1" }, restored);

            stack.Redo();
            Assert.Equal(new[] { "d1" }, removed);
        }

        [Fact]
        public void The_description_names_what_will_be_undone()
        {
            // The caller speaks it: "Undone: Move Trend Line." A generic "Undone." leaves the
            // user guessing which of their last few actions just reversed.
            var series = DrawingSeries("d1", Line(100, 110));
            var stack = new ChartUndoStack();
            stack.Push(new SeriesDeleteUndo("Delete Trend Line", series, _ => { }, _ => { }));

            Assert.Equal("Delete Trend Line", stack.NextUndoDescription);
            stack.Undo();
            Assert.Equal("Delete Trend Line", stack.NextRedoDescription);
        }
    }
}
