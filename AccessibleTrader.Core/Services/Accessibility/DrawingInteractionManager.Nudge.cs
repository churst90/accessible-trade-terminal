using System.Reactive.Concurrency;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Drawing;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// The keyboard nudge: moving an existing drawing's anchors one bar or one price step at a
    /// time from the keyboard, with a readback a speech user can act on.
    ///
    /// <para><b>The defect.</b> Until 2026-09-03 an anchor could be repositioned only by a
    /// 10-pixel mouse drag on its handle or by typing an absolute price or date into the
    /// Properties dialog. Typing an absolute value is a keyboard ROUTE, not the equivalent of
    /// "a little to the right" — the ergonomic thing a sighted user does with a drag — and the
    /// audit's sentence was about nudging.</para>
    ///
    /// <para><b>Target.</b> The FOCUSED series, when it is a drawing (Page Up / Page Down move
    /// series focus; the Object Tree does too). One anchor of it is SELECTED; the selection
    /// starts at the drawing's first anchor, <c>Ctrl+Alt+Shift+G</c> cycles it through the
    /// slots the type actually has (<see cref="DrawingAnchorSchema"/>), and grabbing a handle
    /// with the mouse selects THAT anchor, so "drag it near, then nudge it exact" works on the
    /// anchor the hand just touched. The selection resets whenever the focused series changes,
    /// when the series is removed, and on a tab switch — a stale "anchor 3" pointing into a
    /// different drawing would move the wrong thing.</para>
    ///
    /// <para><b>Steps.</b> Left and Right move by BAR INDEX (<c>Data[i ± 1].Date</c>), never by
    /// date arithmetic — weekends and halts make those different, and the renderer maps a
    /// date to a screen position by binary-searching the bar array, so the only date that
    /// lands on a bar is a bar's own date. Past the last bar the anchor projects into the
    /// reserved right margin with the same cadence the mouse path uses
    /// (<see cref="ProjectFutureDate"/>), and stops at the margin's end. Up and Down move the
    /// price by one percent of the VISIBLE range, so the step scales with zoom the way a drag
    /// does, floored at one unit in the last decimal place <see cref="SpeechPriceFormatter"/>
    /// speaks — a step the readback cannot voice is indistinguishable from a dead key.</para>
    ///
    /// <para><b>Speech.</b> Key auto-repeat runs at roughly fifteen accepted presses a second
    /// (the input service drops repeats inside 50 ms) and the Manual speech channel interrupts
    /// by default, so speaking per press would be the narrator's flood defect in a new place:
    /// thirteen clipped fragments and one sentence for a fourteen-step walk. Every press plays
    /// a short earcon (the earcon service throttles it to five a second) and redraws; ONE
    /// sentence is spoken when the presses settle (<see cref="NudgeSettleWindow"/>). Refusals
    /// under a held key coalesce the same way — the boundary earcon per press, the sentence
    /// once — because "start is at the first bar" thirteen times is the same flood. The
    /// sentence puts the VALUE first and the orientation second, because whatever interrupts
    /// it (an order fill, the user's own next key) cuts the END, and the value is the only new
    /// information: "End: 105.20 at June 15, 09:30. Trend line 2, anchor 2 of 2." The drawing
    /// is named the way Page Up / Page Down name it, since that is how the user reached
    /// it and every trend line shares one friendly name.</para>
    ///
    /// <para><b>The pending sentence is not allowed to speak over something newer.</b> A
    /// navigation key inside the settle window reads the bar, and a readback arriving 300 ms
    /// later would cut that off with an anchor the earcon already confirmed — so the next
    /// navigation key CANCELS the pending sentence (the undo entry is still filed). A modal
    /// opening does the same, so the dialog's own announcement is not talked over. Cycling and
    /// snapping settle the pending run silently before they speak, so the same numbers are
    /// never heard twice 300 ms apart.</para>
    ///
    /// <para><b>Undo.</b> A settled run of nudges is one entry on the undo stack, and a further
    /// run on the SAME anchor extends that entry while it is still the top of the stack. The
    /// stack holds fifty; thirty identical "Undone: Move Trend line" sentences would push the
    /// series deletions undo exists for off the bottom. The entry's "before" is the state at the
    /// first press of the first run, so Ctrl+Z puts the anchor back where the user found it. A
    /// different anchor, a snap, or any other edit in between starts a new entry.</para>
    /// </summary>
    public partial class DrawingInteractionManager
    {
        /// <summary>How long after the last press the readback is spoken. Long enough to
        /// swallow key auto-repeat, short enough to feel like an answer. Shorter than the OS
        /// initial-repeat delay on purpose: a user who holds the key past it hears the first
        /// sentence start and then the ticks, which is what arrow navigation does too.</summary>
        internal static readonly TimeSpan NudgeSettleWindow = TimeSpan.FromMilliseconds(300);

        /// <summary>The fraction of the visible price range one Up/Down press moves.</summary>
        internal const double PriceNudgeFraction = 0.01;

        private readonly IEarconService? _earcons;
        private readonly IScheduler _nudgeScheduler = Scheduler.Default;
        private readonly object _nudgeGate = new();

        // The selection: which drawing, and which of its schema slots (by index into
        // DrawingAnchorSchema.Slots, not by slot number, so cycling is a modulo).
        private string? _nudgeSeriesId;
        private int _nudgeSlotIndex;

        // A pending run: the drawing as it was at the first press (null when nothing is
        // pending), the refusal to speak instead of the readback if the last press hit an edge,
        // and the scheduled settle. Consumed by SettleNudge.
        private DrawingData? _nudgeRunBefore;
        private string? _nudgePendingRefusal;
        private IDisposable? _pendingSettle;

        // The undo entry the last settled run pushed, what it was for, and which kind, so a
        // further run on the same anchor extends it while IsNextUndo still says it is on top.
        private DrawingEditUndo? _nudgeUndoEntry;
        private (string SeriesId, int Slot, string Kind) _nudgeUndoTarget;

        // Snap cycling: the last snap's target and which of the four OHLC levels it chose, so a
        // repeated press on the same anchor and bar walks to the next level instead of
        // re-choosing the nearest (which would be the one it is already on).
        private (string SeriesId, int Slot, int BarIndex, int Ordinal)? _lastSnap;

        private static readonly (string Name, Func<Ohlcv, double> Get)[] SnapLevels =
        {
            ("high",  b => b.High),
            ("low",   b => b.Low),
            ("open",  b => b.Open),
            ("close", b => b.Close),
        };

        private void InitNudge()
        {
            _subs.Add(_eventBus.Subscribe<NudgeDrawingAnchorEvent>(e => HandleNudge(e.Direction)));
            _subs.Add(_eventBus.Subscribe<CycleDrawingAnchorEvent>(_ => HandleCycleAnchor()));
            _subs.Add(_eventBus.Subscribe<SnapDrawingAnchorEvent>(_ => HandleSnapAnchor()));
            // A tab switch swaps the whole series list; whatever was selected no longer exists
            // in the sense that matters, and a pending sentence would describe a drawing that
            // is not on screen.
            _subs.Add(_eventBus.Subscribe<TabSwitchedEvent>(_ => { CancelPendingSettle(); ResetNudgeSelection(); }));
            // A dialog opening must not be talked over by a sentence the earcon already
            // covered; the undo entry is still filed.
            _subs.Add(_eventBus.Subscribe<ModalStateChangedEvent>(e => { if (e.IsOpen) SettleNudge(speak: false); }));
            // Ctrl+Z / Ctrl+Y inside the window: file the pending run FIRST, so the undo reverses
            // it (extending the open entry) instead of popping the previous entry from under it
            // and then filing the reversal as a new "move" that a second Ctrl+Z would undo
            // FORWARD. This manager subscribes before ChartCommandManager, which takes it as a
            // constructor dependency, so the order is guaranteed.
            _subs.Add(_eventBus.Subscribe<UndoChartEditEvent>(_ => SettleNudge(speak: false)));
            _subs.Add(_eventBus.Subscribe<RedoChartEditEvent>(_ => SettleNudge(speak: false)));
            // The user's own next navigation key reads a bar (or a series, on Page Up/Down);
            // a stale readback landing 300 ms later would cut that off.
            _subs.Add(_eventBus.Subscribe<FeedbackRequestEvent>(e =>
            {
                if (e.Type == FeedbackType.Navigation) SettleNudge(speak: false);
            }));
        }

        private void ResetNudgeSelection()
        {
            lock (_nudgeGate)
            {
                _nudgeSeriesId = null;
                _nudgeSlotIndex = 0;
                _nudgeRunBefore = null;
                _nudgePendingRefusal = null;
                _nudgeUndoEntry = null;
                _lastSnap = null;
            }
        }

        /// <summary>
        /// The mouse grabbed an anchor handle: that anchor becomes the selected one, so a
        /// keyboard nudge that follows a drag refines the anchor the hand just touched. Any
        /// pending run is settled silently first — the drag files its own undo entry.
        /// </summary>
        private void SelectAnchorForDrag(string seriesId, int slot)
        {
            SettleNudge(speak: false);
            var series = _store.State.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            var slots = series?.Drawing != null ? DrawingAnchorSchema.Slots(series.Drawing.Type) : Array.Empty<int>();
            int index = Math.Max(0, slots.ToList().IndexOf(slot));
            lock (_nudgeGate)
            {
                _nudgeSeriesId = seriesId;
                _nudgeSlotIndex = index;
                _nudgeUndoEntry = null;
                _lastSnap = null;
            }
        }

        /// <summary>
        /// The drawing the nudge acts on, or null with a spoken reason. Also keeps the selection
        /// honest: a focused series that differs from the selected one settles any pending run
        /// (silently — its undo entry is still filed), resets the slot to the first and ends the
        /// undo run, and <paramref name="newlySelected"/> reports that so the cycle key can SAY
        /// where it is instead of moving on the first press.
        /// </summary>
        private ChartSeries? ResolveNudgeTarget(out string? refusal, out bool newlySelected)
        {
            newlySelected = false;
            var state = _store.State;
            var focused = state.ActiveSeries.FirstOrDefault(s => s.Id == state.FocusedSeriesId);
            if (focused == null || !focused.IsDrawing || focused.Drawing == null)
            {
                refusal = "Focus a drawing first. Page Up and Page Down move between series.";
                return null;
            }
            // A placement in progress keeps its own copy of anchor 1; moving the live drawing
            // underneath it would desynchronise the two. Finish or cancel it first.
            if (_pendingDrawingType != DrawingType.None || _previewSeriesId != null)
            {
                refusal = "Finish or cancel the drawing in progress first. Escape cancels it.";
                return null;
            }
            var slots = DrawingAnchorSchema.Slots(focused.Drawing.Type);
            if (slots.Count == 0)
            {
                refusal = $"{SpokenName(focused)} has no anchors to move.";
                return null;
            }

            bool changed;
            lock (_nudgeGate) changed = _nudgeSeriesId != focused.Id;
            if (changed) SettleNudge(speak: false);

            lock (_nudgeGate)
            {
                if (_nudgeSeriesId != focused.Id)
                {
                    _nudgeSeriesId = focused.Id;
                    _nudgeSlotIndex = 0;
                    _nudgeUndoEntry = null;
                    _lastSnap = null;
                    newlySelected = true;
                }
                if (_nudgeSlotIndex >= slots.Count) _nudgeSlotIndex = 0;
            }
            refusal = null;
            return focused;
        }

        /// <summary>
        /// A single deliberate press that cannot act: the boundary earcon and the reason, at
        /// once. Boundary, not Error — the key was understood and has nowhere to go, which is
        /// the tier "no more signals in this direction" already uses; Error would play the
        /// high-severity earcon reserved for failures and speak on the channel F2 cannot mute.
        /// </summary>
        private void RefuseNow(string message) =>
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary, message, true));

        /// <summary>
        /// A press under a possibly held key that cannot act: the boundary earcon now, the
        /// sentence once when the presses settle — the same shape as a successful run.
        /// </summary>
        private void RefuseCoalesced(string message)
        {
            _earcons?.PlayBoundary();
            lock (_nudgeGate) _nudgePendingRefusal = message;
            ArmSettle();
        }

        private void ArmSettle()
        {
            lock (_nudgeGate)
            {
                _pendingSettle?.Dispose();
                _pendingSettle = _nudgeScheduler.Schedule(NudgeSettleWindow, () => SettleNudge(speak: true));
            }
        }

        private void CancelPendingSettle()
        {
            lock (_nudgeGate)
            {
                _pendingSettle?.Dispose();
                _pendingSettle = null;
                _nudgeRunBefore = null;
                _nudgePendingRefusal = null;
            }
        }

        // ── Nudge ────────────────────────────────────────────────────────────

        private void HandleNudge(AnchorNudgeDirection direction)
        {
            if (_editSeriesId != null) { _earcons?.PlayBoundary(); return; }   // the mouse owns the anchor mid-drag; never silent
            var series = ResolveNudgeTarget(out var refusal, out _);
            if (series == null) { RefuseCoalesced(refusal!); return; }
            var drawing = series.Drawing!;
            var slots = DrawingAnchorSchema.Slots(drawing.Type);
            int slot = slots[_nudgeSlotIndex];
            string slotName = DrawingAnchorSchema.SlotName(drawing.Type, slot);
            string name = SpokenName(series);

            bool horizontal = direction is AnchorNudgeDirection.Earlier or AnchorNudgeDirection.Later;
            if (horizontal && !DrawingAnchorSchema.Uses(drawing.Type, slot, DrawingAnchorAxis.Date))
            {
                RefuseCoalesced($"{Capitalise(slotName)} of {name} has no date to move. Up and Down move its price.");
                return;
            }
            if (!horizontal && !DrawingAnchorSchema.Uses(drawing.Type, slot, DrawingAnchorAxis.Price))
            {
                RefuseCoalesced($"{Capitalise(slotName)} of {name} has no price to move. Left and Right move its date.");
                return;
            }

            var state = _store.State;
            var data = state.Data;
            if (data == null || data.Count == 0) { RefuseCoalesced("No chart loaded."); return; }

            if (horizontal)
            {
                var current = GetAnchorDate(drawing, slot);
                int index = current.HasValue
                    ? BarIndexOf(data, current.Value)
                    : Math.Clamp(state.CurrentDataIndex, 0, data.Count - 1);
                int maxIndex = data.Count - 1 + Math.Max(0, state.RightMarginBars);
                int next = index + (direction == AnchorNudgeDirection.Later ? 1 : -1);
                if (next < 0)
                {
                    RefuseCoalesced($"{Capitalise(slotName)} of {name} is at the first bar.");
                    return;
                }
                if (next > maxIndex)
                {
                    RefuseCoalesced($"{Capitalise(slotName)} of {name} is at the end of the chart's right margin.");
                    return;
                }
                DateTime newDate = next < data.Count
                    ? data[next].Date
                    : ProjectFutureDate(data, next - (data.Count - 1));
                BeginNudgeRunIfNeeded(drawing);
                SetAnchorDate(drawing, slot, newDate);
            }
            else
            {
                var current = GetAnchorPrice(drawing, slot);
                if (!current.HasValue)
                {
                    RefuseCoalesced($"{Capitalise(slotName)} of {name} has no price yet. Set one in Properties.");
                    return;
                }
                double step = PriceNudgeStep(state.ViewportRange.Max - state.ViewportRange.Min, current.Value);
                double moved = current.Value + (direction == AnchorNudgeDirection.Up ? step : -step);
                BeginNudgeRunIfNeeded(drawing);
                SetAnchorPrice(drawing, slot, moved);
            }

            RecomputeDrawingGeometry(series);
            _eventBus.Publish(new RedrawEvent());
            _earcons?.PlayInfo();
            lock (_nudgeGate) _nudgePendingRefusal = null;   // a move after an edge press reads back the move
            ArmSettle();
        }

        private void BeginNudgeRunIfNeeded(DrawingData drawing)
        {
            lock (_nudgeGate) _nudgeRunBefore ??= drawing.Clone();
        }

        /// <summary>
        /// One Up/Down step: a fraction of the visible range, never below one unit in the last
        /// decimal place the price is SPOKEN at. Internal for the unit tests.
        /// </summary>
        internal static double PriceNudgeStep(double visibleRange, double price)
        {
            double unit = SpeechUnitInLastPlace(price);
            double fraction = Math.Abs(visibleRange) * PriceNudgeFraction;
            if (double.IsNaN(fraction) || double.IsInfinity(fraction)) return unit;
            // Whole units, so the spoken value moves by a number the user can hear as a step
            // rather than a long decimal tail; and never below one unit — a sabotage that dropped
            // an earlier `fraction < unit` clause stayed green because this line IS the floor.
            return Math.Max(unit, Math.Round(fraction / unit) * unit);
        }

        /// <summary>The value of one unit in the last decimal place
        /// <see cref="SpeechPriceFormatter.FormatPrice"/> would speak for this price.</summary>
        internal static double SpeechUnitInLastPlace(double price)
        {
            double abs = Math.Abs(price);
            if (abs == 0 || double.IsNaN(abs) || double.IsInfinity(abs)) return 0.01;
            int decimals = Math.Clamp(2 - (int)Math.Floor(Math.Log10(abs)), 2, 10);
            return Math.Pow(10, -decimals);
        }

        /// <summary>
        /// The bar index an anchor date sits at — a real bar, or a projected one past the last
        /// bar (the inverse of <see cref="ProjectFutureDate"/>, using the same cadence).
        /// </summary>
        internal static int BarIndexOf(IReadOnlyList<Ohlcv> data, DateTime date)
        {
            if (date <= data[^1].Date) return FindNearestBarIndex(data, date);
            var step = MedianBarStep(data);
            long offset = (long)Math.Round((date - data[^1].Date).Ticks / (double)step.Ticks);
            return data.Count - 1 + (int)Math.Max(1, offset);
        }

        private static DateTime? GetAnchorDate(DrawingData d, int slot) => slot switch
        {
            1 => d.AnchorDate1, 2 => d.AnchorDate2, 3 => d.AnchorDate3, _ => null
        };

        private static double? GetAnchorPrice(DrawingData d, int slot) => slot switch
        {
            1 => d.AnchorPrice1, 2 => d.AnchorPrice2, 3 => d.AnchorPrice3, _ => null
        };

        private static void SetAnchorDate(DrawingData d, int slot, DateTime value)
        {
            switch (slot)
            {
                case 1: d.AnchorDate1 = value; break;
                case 2: d.AnchorDate2 = value; break;
                case 3: d.AnchorDate3 = value; break;
            }
        }

        private static void SetAnchorPrice(DrawingData d, int slot, double value)
        {
            switch (slot)
            {
                case 1: d.AnchorPrice1 = value; break;
                case 2: d.AnchorPrice2 = value; break;
                case 3: d.AnchorPrice3 = value; break;
            }
        }

        // ── Settle: one sentence, one undo entry ─────────────────────────────

        /// <summary>
        /// Ends the pending run: speaks its one sentence (unless something newer has claimed
        /// the speech channel, in which case only the undo entry is filed) and pushes or extends
        /// the undo entry. Safe to call with nothing pending.
        /// </summary>
        private void SettleNudge(bool speak)
        {
            string? seriesId;
            int slotIndex;
            DrawingData? before;
            string? refusal;
            lock (_nudgeGate)
            {
                _pendingSettle?.Dispose();
                _pendingSettle = null;
                seriesId = _nudgeSeriesId;
                slotIndex = _nudgeSlotIndex;
                before = _nudgeRunBefore;
                refusal = _nudgePendingRefusal;
                _nudgeRunBefore = null;
                _nudgePendingRefusal = null;
            }
            if (before == null && refusal == null) return;   // nothing was pending

            if (speak && refusal != null)
            {
                // The earcon already played per press; the sentence rides the Manual channel
                // with no second earcon.
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, refusal, true));
            }

            if (seriesId == null) return;
            var series = _store.State.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            if (series?.Drawing == null) return;   // removed mid-run: nothing to read back or undo

            var slots = DrawingAnchorSchema.Slots(series.Drawing.Type);
            if (slots.Count == 0) return;
            int slot = slots[Math.Clamp(slotIndex, 0, slots.Count - 1)];

            if (speak && refusal == null)
                _eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.StateChange, DescribeAnchor(series, slot, slotIndex, slots.Count), true));

            if (_undo == null || before == null) return;
            var after = series.Drawing.Clone();
            if (!DrawingEditUndo.IsChange(before, after)) return;
            FileUndo("Move", series, slot, before, after);
        }

        /// <summary>Pushes a new entry, or extends the last one when it is still the next undo
        /// and was for the same anchor and the same kind of edit.</summary>
        private void FileUndo(string kind, ChartSeries series, int slot, DrawingData before, DrawingData after)
        {
            if (_undo == null) return;
            lock (_nudgeGate)
            {
                // A snap is never extended: each press was announced as its own landing, so
                // Ctrl+Z after auditioning two levels is expected to step back ONE level.
                if (kind != "Snap"
                    && _nudgeUndoEntry != null
                    && _nudgeUndoTarget == (series.Id, slot, kind)
                    && _undo.IsNextUndo(_nudgeUndoEntry))
                {
                    _nudgeUndoEntry.ExtendAfter(after);
                    return;
                }
                _nudgeUndoEntry = MakeDrawingUndo($"{kind} {SpokenName(series)}", series.Id, before, after);
                _nudgeUndoTarget = (series.Id, slot, kind);
                _undo.Push(_nudgeUndoEntry);
            }
        }

        private DrawingEditUndo MakeDrawingUndo(string description, string seriesId, DrawingData before, DrawingData after) =>
            new(description,
                resolve: () => _store.State.ActiveSeries.FirstOrDefault(s => s.Id == seriesId),
                before: before,
                after: after,
                afterApply: () =>
                {
                    var s = _store.State.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
                    if (s != null) RecomputeDrawingGeometry(s);
                    _eventBus.Publish(new RedrawEvent());
                });

        // ── Vocabulary ───────────────────────────────────────────────────────

        /// <summary>
        /// "End: 105.20 at June 15, 09:30. Trend line 2, anchor 2 of 2." Value first, because
        /// whatever interrupts the sentence cuts its end and the value is the only new
        /// information; then the name and the position, every time, because a speech user has
        /// nothing else to orient by.
        /// </summary>
        internal string DescribeAnchor(ChartSeries series, int slot, int slotIndex, int slotCount)
        {
            var d = series.Drawing!;
            string slotName = DrawingAnchorSchema.SlotName(d.Type, slot);
            var price = DrawingAnchorSchema.Uses(d.Type, slot, DrawingAnchorAxis.Price) ? GetAnchorPrice(d, slot) : null;
            var date  = DrawingAnchorSchema.Uses(d.Type, slot, DrawingAnchorAxis.Date)  ? GetAnchorDate(d, slot)  : null;

            string where;
            if (price.HasValue && date.HasValue)
                where = $"{SpeechPriceFormatter.FormatPrice(price.Value)} at {SpeakStamp(date.Value)}";
            else if (price.HasValue)
                where = SpeechPriceFormatter.FormatPrice(price.Value);
            else if (date.HasValue)
                where = SpeakStamp(date.Value);
            else
                where = "not set";

            return $"{Capitalise(slotName)}: {where}. {SpokenName(series)}, anchor {slotIndex + 1} of {slotCount}.";
        }

        /// <summary>
        /// The one-line answer to "what would a nudge move right now?" for the Shift+F1 context
        /// summary — the only way to hear the selected anchor WITHOUT moving it. Null when the
        /// focused series is not a drawing.
        /// </summary>
        public string? SelectedAnchorSummary()
        {
            var state = _store.State;
            var focused = state.ActiveSeries.FirstOrDefault(s => s.Id == state.FocusedSeriesId);
            if (focused?.Drawing == null || !focused.IsDrawing) return null;
            var slots = DrawingAnchorSchema.Slots(focused.Drawing.Type);
            if (slots.Count == 0) return null;
            int index;
            lock (_nudgeGate) index = _nudgeSeriesId == focused.Id ? Math.Clamp(_nudgeSlotIndex, 0, slots.Count - 1) : 0;
            return "Selected anchor, " + DescribeAnchor(focused, slots[index], index, slots.Count);
        }

        /// <summary>
        /// The drawing's name as Page Up / Page Down speak it (<c>Name</c>, e.g. "TrendLine (2)"),
        /// with the parenthesised ordinal spoken as a plain number. <c>FriendlyName</c> is
        /// "TrendLine Drawing" for every trend line and cannot tell two apart.
        /// </summary>
        internal static string SpokenName(ChartSeries series) => DrawingSpeech.SpokenSeriesName(series);

        private static string Capitalise(string s) =>
            s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

        /// <summary>
        /// A stamp in the format the chart's navigation readback uses (the Speech Order
        /// setting chooses time-only, date-only or both), converted to display time the same
        /// way — so the nudge, the bar readback and the Properties field all say the same hour.
        /// A projected anchor says how far past the last bar it is, because that date is not a
        /// bar anyone can navigate to.
        /// </summary>
        private string SpeakStamp(DateTime stamp)
        {
            var state = _store.State;
            string order = state.SpeechOrder ?? "";
            string format = order.Contains("TimeOnly") ? SpeechTimeFormatter.TimeFormat
                          : order.Contains("DateOnly") ? SpeechTimeFormatter.DateFormat
                          : SpeechTimeFormatter.DateTimeFormat;
            string text = SpeechTimeFormatter.Format(stamp, format);
            var data = state.Data;
            if (data != null && data.Count > 0 && stamp > data[^1].Date)
            {
                int past = BarIndexOf(data, stamp) - (data.Count - 1);
                text += past == 1 ? ", 1 bar past the last bar" : $", {past} bars past the last bar";
            }
            return text;
        }

        // ── Cycle ────────────────────────────────────────────────────────────

        private void HandleCycleAnchor()
        {
            var series = ResolveNudgeTarget(out var refusal, out bool newlySelected);
            if (series == null) { RefuseNow(refusal!); return; }
            SettleNudge(speak: false);   // never the same numbers twice, 300 ms apart
            var slots = DrawingAnchorSchema.Slots(series.Drawing!.Type);

            int slotIndex;
            lock (_nudgeGate)
            {
                // The first press on a newly focused drawing answers "which anchor is selected?"
                // without moving on — otherwise anchor 1 could never be named, only skipped.
                if (!newlySelected && slots.Count > 1)
                {
                    _nudgeSlotIndex = (_nudgeSlotIndex + 1) % slots.Count;
                    _nudgeUndoEntry = null;   // a different anchor is a different edit
                    _lastSnap = null;
                }
                slotIndex = _nudgeSlotIndex;
            }

            MoveCursorToAnchor(series, slots[slotIndex]);

            string sentence = DescribeAnchor(series, slots[slotIndex], slotIndex, slots.Count);
            if (slots.Count == 1) sentence += " This drawing has one anchor.";
            _earcons?.PlayInfo();   // under F2 the sentence is muted; the key must still be heard
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, sentence, true));
        }

        /// <summary>
        /// Put the chart cursor on the selected anchor's bar.
        ///
        /// <para>Selecting an anchor and leaving the cursor where it was is what made this key
        /// feel like it only ever reached the FIRST anchor: the sentence named "end anchor"
        /// while the chart's own position — and everything that reads from it, the bar
        /// readback, the tone under the arrows, the range the price nudge steps by — still
        /// stood wherever the user had been. Cycling now takes you there.</para>
        ///
        /// <para><b><see cref="NavigateAction"/>, not <c>SetCursorAction</c>.</b> SetCursor
        /// CLAMPS the target to the current viewport, so an anchor scrolled off the left edge
        /// would silently land the cursor on the leftmost visible bar and report success —
        /// the same wrong answer, one step later. Navigate scrolls the viewport to bring the
        /// bar into view, which is what every other jump in the application does.</para>
        ///
        /// <para><b>Only a date inside the loaded bars is jumped to.</b>
        /// <see cref="BarIndexOf"/> answers 0 for any date BEFORE <c>data[0]</c> and
        /// <c>Count - 1 + offset</c> for one past the last bar, so both out-of-range cases
        /// would otherwise land somewhere plausible and wrong — and the one before
        /// <c>data[0]</c> would land on bar 0 while the sentence described it in the grammar
        /// of "start anchor", which is precisely the confusion this method exists to end. A
        /// price-only anchor (a Fibonacci level) has no date and does not move the cursor.</para>
        ///
        /// <para>Nothing extra is spoken, in either case. The sentence the caller is about to
        /// speak already carries the anchor's own price and date, which is the honest answer
        /// to "where is it?" whether or not the cursor could follow — and a second utterance
        /// 300 ms from the first is the double-readback defect this file was written to
        /// avoid.</para>
        /// </summary>
        private void MoveCursorToAnchor(ChartSeries series, int slot)
        {
            var data = _store.State.Data;
            if (data == null || data.Count == 0) return;
            if (GetAnchorDate(series.Drawing!, slot) is not { } date) return;
            if (date < data[0].Date || date > data[^1].Date) return;
            _store.Dispatch(new NavigateAction(BarIndexOf(data, date)));
        }

        // ── Snap ─────────────────────────────────────────────────────────────

        private void HandleSnapAnchor()
        {
            if (_editSeriesId != null) { _earcons?.PlayBoundary(); return; }
            var series = ResolveNudgeTarget(out var refusal, out _);
            if (series == null) { RefuseNow(refusal!); return; }
            SettleNudge(speak: false);
            var drawing = series.Drawing!;
            var slots = DrawingAnchorSchema.Slots(drawing.Type);
            int slotIndex = _nudgeSlotIndex;
            int slot = slots[slotIndex];
            string slotName = DrawingAnchorSchema.SlotName(drawing.Type, slot);
            string name = SpokenName(series);

            if (!DrawingAnchorSchema.Uses(drawing.Type, slot, DrawingAnchorAxis.Price))
            {
                RefuseNow($"{Capitalise(slotName)} of {name} has no price to snap.");
                return;
            }
            var state = _store.State;
            var data = state.Data;
            if (data == null || data.Count == 0) { RefuseNow("No chart loaded."); return; }

            // The bar whose open/high/low/close are the candidates: the anchor's own bar when
            // the slot has a date, otherwise the chart cursor's bar (a Fibonacci retracement's
            // prices have no dates of their own).
            int barIndex;
            bool cursorBar = false;
            var anchorDate = DrawingAnchorSchema.Uses(drawing.Type, slot, DrawingAnchorAxis.Date)
                ? GetAnchorDate(drawing, slot) : null;
            if (anchorDate.HasValue)
            {
                barIndex = BarIndexOf(data, anchorDate.Value);
                if (barIndex >= data.Count)
                {
                    RefuseNow($"{Capitalise(slotName)} of {name} is past the last bar; there is no bar to snap to.");
                    return;
                }
            }
            else
            {
                barIndex = Math.Clamp(state.CurrentDataIndex, 0, data.Count - 1);
                cursorBar = true;
            }
            var bar = data[barIndex];
            double current = GetAnchorPrice(drawing, slot) ?? bar.Close;

            int ordinal;
            lock (_nudgeGate)
            {
                if (_lastSnap is { } last && last.SeriesId == series.Id && last.Slot == slot && last.BarIndex == barIndex)
                    ordinal = (last.Ordinal + 1) % SnapLevels.Length;
                else
                    ordinal = NearestSnapOrdinal(bar, current);
                _lastSnap = (series.Id, slot, barIndex, ordinal);
            }

            var before = drawing.Clone();
            double target = SnapLevels[ordinal].Get(bar);
            SetAnchorPrice(drawing, slot, target);
            RecomputeDrawingGeometry(series);
            _eventBus.Publish(new RedrawEvent());
            _earcons?.PlayInfo();

            if (DrawingEditUndo.IsChange(before, drawing))
                FileUndo("Snap", series, slot, before, drawing.Clone());

            string barWords = cursorBar ? "the cursor bar, " : "";
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange,
                $"{Capitalise(slotName)}: {SpeechPriceFormatter.FormatPrice(target)}, the {SnapLevels[ordinal].Name} of "
                + $"{barWords}{SpeakStamp(bar.Date)}. {name}, anchor {slotIndex + 1} of {slots.Count}.", true));
        }

        internal static int NearestSnapOrdinal(Ohlcv bar, double price)
        {
            int best = 0;
            double bestDist = double.MaxValue;
            for (int i = 0; i < SnapLevels.Length; i++)
            {
                double dist = Math.Abs(SnapLevels[i].Get(bar) - price);
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            return best;
        }
    }
}
