using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// One reversible chart edit. <see cref="Undo"/> puts the world back; <see cref="Redo"/>
    /// puts it forward again. Both must be safe to call repeatedly in alternation.
    /// </summary>
    public interface IUndoableEdit
    {
        /// <summary>Spoken when the edit is undone, e.g. "Move trend line undone".</summary>
        string Description { get; }

        void Undo();
        void Redo();
    }

    public interface IChartUndoStack
    {
        /// <summary>Records an edit that has ALREADY been applied.</summary>
        void Push(IUndoableEdit edit);

        /// <summary>Reverses the most recent edit. False when there is nothing to undo.</summary>
        bool Undo();

        /// <summary>Re-applies the most recently undone edit. False when there is nothing to redo.</summary>
        bool Redo();

        /// <summary>
        /// Discards both stacks. Called when the chart identity changes, because an edit to a
        /// BTC trend line is not something you can meaningfully undo while looking at ETH.
        /// </summary>
        void Clear();

        bool CanUndo { get; }
        bool CanRedo { get; }

        /// <summary>
        /// True when <paramref name="edit"/> is the entry Ctrl+Z would reverse next — the same
        /// object, not an equal one. A keyboard nudge coalesces a run of presses into one entry by
        /// EXTENDING the entry it pushed last, and it may only do that while nothing else has been
        /// pushed on top; otherwise an unrelated edit in between would be undone out of order.
        /// </summary>
        bool IsNextUndo(IUndoableEdit edit);

        /// <summary>Description of what <see cref="Undo"/> would reverse, or null.</summary>
        string? NextUndoDescription { get; }

        /// <summary>Description of what <see cref="Redo"/> would re-apply, or null.</summary>
        string? NextRedoDescription { get; }
    }

    /// <summary>
    /// <b>Undo/redo for chart edits.</b>
    ///
    /// <para>── Why this exists ────────────────────────────────────────────────────
    /// Before 2026-08-27, <c>grep -rn "Undo\|Redo"</c> over the whole repository returned
    /// <b>zero files</b>. Drawing edits were destructive and irreversible:
    /// <c>UpdateEditDrag</c> wrote straight into <c>series.Drawing.AnchorDate1/Price1</c> and
    /// overwrote <c>series.Data.ComponentData</c> in place, and the pre-drag values were never
    /// captured, so nothing could have restored them. <c>DrawingContextMenu.OnDelete</c>
    /// published <c>DeleteSeriesEvent</c> with no confirmation and no way back.</para>
    ///
    /// <para>The failure is ordinary and the anchor-grab tolerance is 10 px: a user reaches for
    /// the chart to pan, catches an anchor handle instead, drags, and the trend line they
    /// placed carefully is gone — with Ctrl+Z doing nothing. For a keyboard-and-speech user
    /// there is no "drag it roughly back" recovery either, because they cannot see where it
    /// used to be.</para>
    ///
    /// <para>── Design ────────────────────────────────────────────────────────────
    /// Bounded (<see cref="MaxDepth"/>) so a long session cannot pin arbitrary amounts of
    /// drawing state in memory. Scoped per chart identity via <see cref="Clear"/>, so an undo
    /// can never reach across a symbol or timeframe change into a chart that is no longer on
    /// screen. A fresh <see cref="Push"/> discards the redo stack, which is the behaviour every
    /// editor has and the one users' hands already expect.</para>
    /// </summary>
    public sealed class ChartUndoStack : IChartUndoStack
    {
        /// <summary>
        /// How many edits are kept. Deep enough that "undo until it looks right" works, shallow
        /// enough that the retained <c>DrawingData</c> clones stay negligible.
        /// </summary>
        public const int MaxDepth = 50;

        private readonly LinkedList<IUndoableEdit> _undo = new();
        private readonly Stack<IUndoableEdit> _redo = new();
        private readonly object _gate = new();

        public bool CanUndo { get { lock (_gate) return _undo.Count > 0; } }
        public bool CanRedo { get { lock (_gate) return _redo.Count > 0; } }

        public bool IsNextUndo(IUndoableEdit edit)
        {
            lock (_gate) return _undo.Last != null && ReferenceEquals(_undo.Last.Value, edit);
        }

        public string? NextUndoDescription
        {
            get { lock (_gate) return _undo.Last?.Value.Description; }
        }

        public string? NextRedoDescription
        {
            get { lock (_gate) return _redo.Count > 0 ? _redo.Peek().Description : null; }
        }

        public void Push(IUndoableEdit edit)
        {
            if (edit == null) return;
            lock (_gate)
            {
                _undo.AddLast(edit);
                // A new edit invalidates the redo branch — you cannot redo your way into a
                // future that no longer follows from the present.
                _redo.Clear();
                while (_undo.Count > MaxDepth) _undo.RemoveFirst();
            }
        }

        public bool Undo()
        {
            IUndoableEdit edit;
            lock (_gate)
            {
                if (_undo.Last == null) return false;
                edit = _undo.Last.Value;
                _undo.RemoveLast();
                _redo.Push(edit);
            }
            edit.Undo();
            return true;
        }

        public bool Redo()
        {
            IUndoableEdit edit;
            lock (_gate)
            {
                if (_redo.Count == 0) return false;
                edit = _redo.Pop();
                _undo.AddLast(edit);
            }
            edit.Redo();
            return true;
        }

        public void Clear()
        {
            lock (_gate)
            {
                _undo.Clear();
                _redo.Clear();
            }
        }
    }

    /// <summary>
    /// A drawing's anchors before and after an edit-drag.
    ///
    /// <para>Both states are <c>Clone()</c>s, never references to the live
    /// <see cref="DrawingData"/>: the live object is mutated in place by
    /// <c>UpdateEditDrag</c>, so holding a reference to it would mean holding a "before" that
    /// changes as the drag proceeds — which is precisely nothing.</para>
    /// </summary>
    public sealed class DrawingEditUndo : IUndoableEdit
    {
        private readonly Func<ChartSeries?> _resolve;
        private readonly DrawingData _before;
        private DrawingData _after;
        private readonly Action _afterApply;

        public DrawingEditUndo(
            string description,
            Func<ChartSeries?> resolve,
            DrawingData before,
            DrawingData after,
            Action afterApply)
        {
            Description = description;
            _resolve = resolve;
            _before = before.Clone();
            _after = after.Clone();
            _afterApply = afterApply;
        }

        public string Description { get; }

        public void Undo() => Apply(_before);
        public void Redo() => Apply(_after);

        /// <summary>
        /// Moves this entry's "after" state forward without touching its "before". A run of
        /// keyboard nudges is one edit to the user — "I moved the end of the trend line" — and the
        /// stack holds fifty, so thirty separate "Move Trend line" entries would push the deletes
        /// undo exists for off the bottom. Only ever called on the entry that is still the next
        /// undo (<see cref="IChartUndoStack.IsNextUndo"/>).
        /// </summary>
        public void ExtendAfter(DrawingData after) => _after = after.Clone();

        private void Apply(DrawingData snapshot)
        {
            // The series may have been deleted since. Silently doing nothing is right: the
            // alternative is resurrecting a drawing the user removed on purpose.
            var series = _resolve();
            if (series?.Drawing == null) return;

            var copy = snapshot.Clone();
            series.Drawing.Type = copy.Type;
            series.Drawing.AnchorDate1 = copy.AnchorDate1;
            series.Drawing.AnchorPrice1 = copy.AnchorPrice1;
            series.Drawing.AnchorDate2 = copy.AnchorDate2;
            series.Drawing.AnchorPrice2 = copy.AnchorPrice2;
            series.Drawing.AnchorDate3 = copy.AnchorDate3;
            series.Drawing.AnchorPrice3 = copy.AnchorPrice3;
            series.Drawing.Text = copy.Text;
            series.Drawing.ChannelWidth = copy.ChannelWidth;
            series.Drawing.IsLocked = copy.IsLocked;
            series.Drawing.ExtendLeft = copy.ExtendLeft;
            series.Drawing.ExtendRight = copy.ExtendRight;
            series.Drawing.StopLoss = copy.StopLoss;
            series.Drawing.TakeProfit = copy.TakeProfit;
            series.Drawing.RiskRewardRatio = copy.RiskRewardRatio;
            series.Drawing.MeasureResult = copy.MeasureResult;

            _afterApply();
        }

        /// <summary>True when the drag actually moved something — a click that grabs a handle
        /// and releases it without moving is not an edit and must not consume an undo slot.</summary>
        public static bool IsChange(DrawingData before, DrawingData after) =>
            before.AnchorDate1 != after.AnchorDate1
            || before.AnchorPrice1 != after.AnchorPrice1
            || before.AnchorDate2 != after.AnchorDate2
            || before.AnchorPrice2 != after.AnchorPrice2
            || before.AnchorDate3 != after.AnchorDate3
            || before.AnchorPrice3 != after.AnchorPrice3;
    }

    /// <summary>
    /// A deleted series, restorable. The whole <see cref="ChartSeries"/> is held rather than a
    /// description of it, because a drawing's component arrays are recomputed from its anchors
    /// and a series' identity is its <c>Id</c> — restoring anything less would produce a
    /// different drawing that merely looks similar.
    /// </summary>
    public sealed class SeriesDeleteUndo : IUndoableEdit
    {
        private readonly ChartSeries _series;
        private readonly Action<ChartSeries> _restore;
        private readonly Action<string> _remove;

        public SeriesDeleteUndo(
            string description, ChartSeries series,
            Action<ChartSeries> restore, Action<string> remove)
        {
            Description = description;
            _series = series;
            _restore = restore;
            _remove = remove;
        }

        public string Description { get; }

        public void Undo() => _restore(_series);
        public void Redo() => _remove(_series.Id);
    }
}
