using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.BlazorClient.Services
{
    /// <summary>
    /// One hover sample for the chart crosshair: the raw cursor position, the
    /// snapped screen X of the bar under the cursor, and pre-formatted readout
    /// text. Coordinates are CSS pixels relative to the chart-interact-zone,
    /// the same space the JS mouse handler reports in.
    /// </summary>
    public sealed record ChartHoverInfo(
        double CursorX,
        double CursorY,
        double BarX,
        string DateText,
        string PriceText,
        string OhlcText,
        HoverReadoutPlacement Readout);

    /// <summary>
    /// Where the readout box goes, in CSS pixels relative to the chart-interact-zone: its top,
    /// which side of the plot it hangs from, the distance from that side (from the element's
    /// left edge, or from its right edge — which includes the price axis column), and the widest
    /// it may grow before its text wraps. See <see cref="ChartHoverTracker.PlaceReadout"/>.
    /// </summary>
    public sealed record HoverReadoutPlacement(bool AtLeft, double Top, double Inset, double MaxWidth);

    /// <summary>
    /// Tracks the mouse over the chart and produces crosshair + readout state
    /// for sighted and low-vision users. Listens to the same
    /// <see cref="IInputService.MouseEvent"/> stream the drawing pipeline uses;
    /// the readout is rendered by ChartArea as REAL DOM TEXT (not baked into the
    /// chart PNG) so screen magnifiers and user CSS can get at it.
    ///
    /// Deliberately NOT wired to the speech live region: hover speech at mouse
    /// rates would be unbearable spam. The spoken path for "what is this bar?"
    /// is clicking it (which moves the keyboard cursor and fires the standard
    /// navigation feedback) or arrow-key navigation.
    ///
    /// <para><b>The readout follows the chart under a STILL pointer</b> (2026-10-15). It used
    /// to be computed only on a mouse event, and Cody, driving the chart from the keyboard with
    /// the mouse parked on it, had it describe the bar that had been under the pointer several
    /// pans ago: the hairline on about March 31, the box saying April 20 with April 20's candle,
    /// twenty bars to the right. So the last pointer sample is kept and re-read whenever a
    /// mapping input changes (viewport, price range, log scale, bars, tab, pane dividers). A
    /// re-read never sounds or speaks: the hover tick and explore speech answer the user's own
    /// pointer movement, and a chart moving under a parked mouse is not that.</para>
    ///
    /// <para>A change in the chart's SIZE or position (a scroll, a window resize, focus mode)
    /// moves the chart, not the pointer: the pointer is still at the same spot on screen. So the
    /// sample is kept in viewport terms too (the element's rect when it was taken plus the
    /// element-relative position) and re-mapped against the new rect, silently, like any other
    /// re-read. It used to be cleared until the next mouse move (Cody, 2026-10-15: "the pointer
    /// did not move; only the chart moved"). It is still cleared when the pointer is now off the
    /// chart, and when the rect it was sampled against is not known.</para>
    /// </summary>
    public class ChartHoverTracker : IDisposable
    {
        private readonly IInputService _input;
        private readonly IWorkspaceStore _store;
        // Optional (Phase B second pass): quiet hover sonification. Both null in
        // tests / manual composition; DI supplies them.
        private readonly ISonificationManager? _sonification;
        private readonly ISettingsManager? _settings;
        // Optional (touch Explore mode): spoken bar readout as the finger slides.
        private readonly AccessibleTrader.Core.Services.Accessibility.ISpeechFeedbackRouter? _speech;
        private int _lastSonifiedIndex = -1;
        private int _lastSpokenIndex = -1;

        // The last pointer event the readout came from; null when there is nothing to re-read.
        // X/Y are element-relative; Origin is the element's viewport rect (left, top) the event
        // was measured against, null when no reported rect matched it. Origin + (X, Y) is where
        // the pointer is on screen, which a scroll or resize does not move.
        private (double X, double Y, double Width, double Height, bool Explore, (double Left, double Top)? Origin)? _sample;
        // Focus mode re-lays the chart out and the new rect follows in a later report: until it
        // lands the sample is kept but not shown, so a readout from the old layout is never drawn
        // over the new one.
        private bool _awaitingGeometry;
        // The workspace inputs Current was last mapped through; a re-read runs only when one moved.
        private MappingInputs? _mappedFrom;
        // The chart element's last reported rect, to tell a real resize or move from a repeat report.
        private (double Left, double Top, double Width, double Height)? _geometry;
        // Mouse events arrive on the circuit, store notifications on whichever thread dispatched,
        // pane layout from the render thread.
        private readonly object _gate = new();
        private readonly IDisposable? _stateSub;
        private readonly IDisposable? _focusModeSub;

        /// <summary>
        /// Everything in <see cref="WorkspaceState"/> the pointer-to-bar mapping reads. Data is
        /// compared by reference: <c>TimeSeriesBuffer</c> is replaced, never mutated, on a new bar
        /// or a live tick, so a new reference is exactly "the bars changed".
        /// </summary>
        private sealed record MappingInputs(
            object Data, int Start, int Length, (double Min, double Max) Range, bool IsLog, int Tab)
        {
            public static MappingInputs Of(WorkspaceState s) => new(
                s.Data, s.ViewportStartIndex, s.ViewportLength, s.ViewportRange, s.IsLogScale, s.ActiveTabIndex);
        }

        /// <summary>Setting key: soft pitch tick as the hovered bar changes. Default OFF.</summary>
        public const string HoverSonificationKey = AccessibleTrader.Core.Services.SettingsKeys.HoverSonification;

        /// <summary>Crosshair visibility toggle (chart context menu). Default on.</summary>
        public bool IsEnabled { get; private set; } = true;

        /// <summary>Latest hover sample; null when the cursor is off-chart or disabled.</summary>
        public ChartHoverInfo? Current { get; private set; }

        /// <summary>Raised whenever <see cref="Current"/> or <see cref="IsEnabled"/> changes.</summary>
        public event Action? Changed;

        /// <summary>
        /// Rendered pane geometry. Optional so every existing construction keeps working;
        /// when it is absent the mapping degrades to the old whole-canvas behaviour rather
        /// than reporting a price from a pane it cannot locate.
        /// </summary>
        private readonly Core.Services.Rendering.IPaneLayoutService? _paneLayout;

        public ChartHoverTracker(IInputService input, IWorkspaceStore store,
            ISonificationManager? sonification = null, ISettingsManager? settings = null,
            AccessibleTrader.Core.Services.Accessibility.ISpeechFeedbackRouter? speech = null,
            Core.Services.Rendering.IPaneLayoutService? paneLayout = null,
            IEventBus? eventBus = null)
        {
            _input = input;
            _store = store;
            _sonification = sonification;
            _settings = settings;
            _speech = speech;
            _paneLayout = paneLayout;
            _input.MouseEvent += OnMouse;
            _stateSub = _store.StateStream?.Subscribe(_ => Recompute(force: false));
            // A pane added or removed moves the dividers without changing the canvas size, so
            // the price under a fixed Y changes: re-read, don't clear.
            if (_paneLayout != null) _paneLayout.LayoutChanged += OnPaneLayoutChanged;
            // Alt+Z re-lays the chart out; the element's resize report (OnChartGeometryChanged)
            // follows and re-maps the sample. Hide until then rather than show the old mapping.
            _focusModeSub = eventBus?.Subscribe<AccessibleTrader.Core.Models.FocusModeChangedEvent>(_ => OnFocusModeChanged());
        }

        public void Toggle()
        {
            lock (_gate)
            {
                IsEnabled = !IsEnabled;
                if (!IsEnabled)
                {
                    Current = null;
                    // Hidden then shown again starts from the next pointer move, not from a
                    // sample taken before it was hidden.
                    _sample = null;
                    _mappedFrom = null;
                    _awaitingGeometry = false;
                }
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// The chart element's rect (CSS pixels, viewport-relative), reported by canvasRegion.js
        /// on every element resize, window resize and scroll, and by the mouse handler just before
        /// a move measured against a rect not yet reported. A real change re-maps the hover sample
        /// to where the still pointer now is on the chart; a repeat of the same rect (a scroll
        /// elsewhere on the page) does nothing, unless focus mode is waiting for it.
        /// </summary>
        public void OnChartGeometryChanged(double left, double top, double width, double height)
        {
            bool changed = false;
            lock (_gate)
            {
                var previous = _geometry;
                _geometry = (left, top, width, height);
                bool moved = previous is not { } g || !SameRect(g, (left, top, width, height));
                if (_sample is not { } p || (!moved && !_awaitingGeometry)) return;
                _awaitingGeometry = false;

                // Sampled before any rect was known (or against one that did not match): there
                // is no way to tell where the pointer is now, so say nothing rather than guess.
                if (p.Origin is not { } o) changed = DropSampleLocked();
                else
                {
                    // Same spot on screen, new element origin.
                    double x = o.Left + p.X - left, y = o.Top + p.Y - top;
                    // Off the chart now (scrolled out from under the pointer, or the chart shrank
                    // away from it): there is no bar under the pointer to name.
                    if (x < 0 || y < 0 || x >= width || y >= height) changed = DropSampleLocked();
                    else
                    {
                        _sample = (x, y, width, height, p.Explore, (left, top));
                        if (IsEnabled || p.Explore)
                        {
                            var before = Current;
                            Map(x, y, width, height, out _, out _);
                            changed = !Equals(before, Current);
                            if (!changed) Current = before;
                        }
                    }
                }
            }
            if (changed) Changed?.Invoke();
        }

        private static bool SameRect((double Left, double Top, double Width, double Height) a,
            (double Left, double Top, double Width, double Height) b)
            => Math.Abs(a.Left - b.Left) <= 0.5 && Math.Abs(a.Top - b.Top) <= 0.5
               && Math.Abs(a.Width - b.Width) <= 0.5 && Math.Abs(a.Height - b.Height) <= 0.5;

        private void OnFocusModeChanged()
        {
            bool changed;
            lock (_gate)
            {
                if (_sample is not { } p) return;
                if (p.Origin == null)
                {
                    // No origin: the coming report cannot re-map it, so drop it now.
                    changed = DropSampleLocked();
                }
                else
                {
                    _awaitingGeometry = true;
                    changed = Current != null;
                    Current = null;
                }
            }
            if (changed) Changed?.Invoke();
        }

        private void OnPaneLayoutChanged() => Recompute(force: true);

        /// <summary>
        /// Re-read the stored pointer sample against the chart as it is now. Skipped unless a
        /// mapping input moved, because the store dispatches constantly on a live chart;
        /// <paramref name="force"/> is for the pane layout, which is not workspace state.
        /// Silent by construction: the sound and speech live in <see cref="OnMouse"/>.
        /// </summary>
        private void Recompute(bool force)
        {
            bool changed;
            lock (_gate)
            {
                if (_sample is not { } p) return;
                if (!IsEnabled && !p.Explore) return;
                if (_awaitingGeometry) return; // focus mode: the new layout's rect has not arrived
                if (!force && MappingInputs.Of(_store.State).Equals(_mappedFrom)) return;
                var before = Current;
                Map(p.X, p.Y, p.Width, p.Height, out _, out _);
                changed = !Equals(before, Current);
                if (!changed) Current = before; // same text: keep the instance, skip a re-render
            }
            if (changed) Changed?.Invoke();
        }

        private void OnMouse(double x, double y, string type, double width, double height)
        {
            if (type == "MouseLeave")
            {
                _lastSpokenIndex = -1; // next explore touch re-announces its first bar
                DropSample();
                return;
            }
            // TouchExplore (finger sliding in Explore mode) shares the whole hover
            // path — crosshair, readout — but ALWAYS sonifies and ALSO speaks per
            // bar, because the exploring finger is a deliberate "tell me" gesture,
            // not incidental mouse travel. MouseMove keeps the quiet opt-in rules.
            bool explore = type == "TouchExplore";
            if (!IsEnabled && !explore) return;
            if (type != "MouseMove" && !explore) return;

            WorkspaceState state;
            int index;
            Ohlcv bar;
            bool raise;
            lock (_gate)
            {
                // The rect this event was measured against is the last one reported, provided its
                // size agrees with the event's (the mouse handler reports a changed rect before
                // the move that was measured against it). One that disagrees is stale: no origin.
                (double, double)? origin = _geometry is { } g
                    && Math.Abs(g.Width - width) <= 0.5 && Math.Abs(g.Height - height) <= 0.5
                    ? (g.Left, g.Top) : null;
                _sample = (x, y, width, height, explore, origin);
                _awaitingGeometry = false;
                var before = Current;
                state = Map(x, y, width, height, out index, out bar);
                raise = Current != null || before != null;
            }
            if (raise) Changed?.Invoke();
            if (index < 0) return;

            // Quiet hover sonification (opt-in, default OFF): one soft, short tick per
            // BAR CHANGE — never per pixel — pitched to the bar's close within the
            // visible range, so sweeping the mouse across the chart hums the price
            // contour without touching the keyboard cursor or the speech channel.
            if (index != _lastSonifiedIndex)
            {
                _lastSonifiedIndex = index;
                bool audible = explore
                    || (_settings?.GetSetting(HoverSonificationKey)?.ToObject<bool>() ?? false);
                if (_sonification != null && audible)
                {
                    double span = state.ViewportRange.Max - state.ViewportRange.Min;
                    double pct = span > 0
                        ? Math.Clamp((bar.Close - state.ViewportRange.Min) / span, 0, 1)
                        : 0.5;
                    double freq = 220 + pct * (880 - 220); // A3..A5 — audible but unobtrusive
                    // Explore ticks are a touch louder/longer than hover's whisper —
                    // they ARE the primary feedback under a finger.
                    _sonification.PlayNote(freq, explore ? 0.07 : 0.04, "sine",
                        explore ? 0.12f : 0.05f, 0f);
                }
            }

            // Explore speech: one compact utterance per BAR CHANGE — close then date,
            // value first so a fast sweep still lands the numbers. Manual channel:
            // this is something the user asked for with their finger (F2 mutes it).
            if (explore && index != _lastSpokenIndex)
            {
                _lastSpokenIndex = index;
                _speech?.Speak($"{PriceFormatter.FormatPrice(bar.Close)}, {FormatBarDate(bar.Date, AccessibleTrader.Core.Services.Accessibility.PlaybackNarration.BarSeconds(state))}",
                    interrupt: true);
            }
        }

        /// <summary>
        /// Pointer position to readout, against the chart as it is NOW: the one mapping a real
        /// pointer event and a re-read both go through. Sets <see cref="Current"/> (null when the
        /// point is on no bar) and returns the state it read; <paramref name="index"/> is -1 when
        /// there is no bar. The caller holds <c>_gate</c> and raises <see cref="Changed"/>.
        /// </summary>
        private WorkspaceState Map(double x, double y, double width, double height, out int index, out Ohlcv bar)
        {
            var state = _store.State;
            _mappedFrom = MappingInputs.Of(state);
            index = -1;
            bar = default;
            if (state.Data == null || state.Data.Count == 0 || width <= 0 || height <= 0)
            {
                Current = null;
                return state;
            }

            // The plot is the canvas minus the y-axis column and the x-axis strip. Mapping
            // against the whole canvas put the readout up to six bars out at the right edge
            // and, with a volume pane on screen, returned a price from roughly halfway up the
            // main pane's range for a cursor at its visual bottom.
            var dividers = _paneLayout?.Dividers;
            float axisH = _paneLayout?.AxisHeightFraction ?? 0f;
            float axisW = _paneLayout?.AxisWidthFraction ?? 0f;
            double plotWidth = ChartMath.PlotWidth(width, axisW);

            int i = ChartMath.MapXToIndex(x, plotWidth, state.ViewportStartIndex, state.ViewportLength);
            if (i < 0 || i >= state.Data.Count)
            {
                Current = null;
                return state;
            }

            index = i;
            bar = state.Data[i];
            double price = ChartMath.MapYToPriceInPane(
                y, height, dividers, axisH,
                state.ViewportRange.Min, state.ViewportRange.Max, state.IsLogScale);

            // Over the x-axis strip there is no price to report; the bar under the cursor
            // still is, so the crosshair keeps its date and OHLC.
            if (double.IsNaN(price)) price = state.ViewportRange.Min;

            // Snap the vertical hairline to the hovered bar's slot centre so the
            // line answers "which bar am I on", not just "where is my cursor".
            // Across the PLOT width, not the canvas width, or the hairline sits beside the
            // candle it claims to be on.
            double barX = state.ViewportLength > 1
                ? (i - state.ViewportStartIndex) / (double)(state.ViewportLength - 1) * plotWidth
                : x;

            Current = new ChartHoverInfo(
                x, y, barX,
                DateText: FormatBarDate(bar.Date, AccessibleTrader.Core.Services.Accessibility.PlaybackNarration.BarSeconds(state)),
                PriceText: PriceFormatter.FormatPrice(price),
                OhlcText: $"O {PriceFormatter.FormatPrice(bar.Open)}  H {PriceFormatter.FormatPrice(bar.High)}  " +
                          $"L {PriceFormatter.FormatPrice(bar.Low)}  C {PriceFormatter.FormatPrice(bar.Close)}",
                Readout: PlaceReadout(barX, width, height, plotWidth, _paneLayout?.MainLegendFraction));
            return state;
        }

        /// <summary>Gap between the readout box and the plot edge, the legend, and the plot's centre line.</summary>
        internal const double ReadoutMargin = 6;

        /// <summary>
        /// About how wide the box is when nothing wraps (0.85rem tabular figures, "O 82,366.23  H
        /// 82,366.23  L 82,366.23  C 82,366.23" plus padding). Only decides whether the box sits
        /// BESIDE the legend or under it; a box that turns out wider wraps inside its
        /// <see cref="HoverReadoutPlacement.MaxWidth"/> rather than overlapping anything.
        /// </summary>
        internal const double ReadoutNaturalWidth = 400;

        /// <summary>
        /// Where the readout box goes: at the top of the plot, on the half AWAY from the hairline.
        ///
        /// <para>It used to sit top right always (<c>top:6px; right:72px</c>), and in Cody's
        /// 2026-10-15 BTCUSDT 1d screenshots, normal and focus mode, it covered the newest candles,
        /// their highs and the "ascending triangle" label: the live edge, where a pointer most
        /// often is. Now a hairline in the right half of the plot puts the box top left, and one
        /// in the left half puts it top right, still clear of the price axis. Its width is capped
        /// at its own half of the plot (it wraps instead), so it can never reach the hairline.</para>
        ///
        /// <para>On the left it must not cover the price pane's legend, which the renderer paints
        /// at the pane's top-left and publishes through <c>IPaneLayoutService.MainLegendFraction</c>.
        /// The box goes beside the legend when its unwrapped width fits there before the centre
        /// line, otherwise under the legend. The legend is capped at 45% of the pane's height, so
        /// under it is still the upper half of the pane.</para>
        /// </summary>
        /// <param name="barX">The hairline, CSS px from the element's left edge.</param>
        /// <param name="width">The element's width; the plot is <paramref name="plotWidth"/> of it, the rest is the price axis.</param>
        /// <param name="height">The element's height.</param>
        /// <param name="legend">The price pane's legend as fractions of the element, or null.</param>
        public static HoverReadoutPlacement PlaceReadout(double barX, double width, double height, double plotWidth,
            (float Left, float Top, float Right, float Bottom)? legend)
        {
            const double m = ReadoutMargin;
            double half = plotWidth / 2;
            double halfWidth = Math.Max(0, half - 2 * m);

            // Hairline on the older half: top right, measured from the element's right edge, so
            // the inset is the axis column plus the margin.
            if (barX < half)
                return new HoverReadoutPlacement(false, m, Math.Max(0, width - plotWidth) + m, halfWidth);

            if (legend is not { } lg)
                return new HoverReadoutPlacement(true, m, m, halfWidth);

            double legendRight = lg.Right * width, legendTop = lg.Top * height, legendBottom = lg.Bottom * height;
            double besideLeft = legendRight + m;
            double beside = half - m - besideLeft;
            if (beside >= ReadoutNaturalWidth)
                return new HoverReadoutPlacement(true, Math.Max(m, legendTop), besideLeft, beside);
            return new HoverReadoutPlacement(true, legendBottom + m, m, halfWidth);
        }

        /// <summary>
        /// The hover readout's date, and the bar slider's. Daily-and-up bars are named by their
        /// own date with no time (it is the same midnight on every one); intraday bars by date
        /// and clock time in the user's zone.
        ///
        /// <para>This used to format the stored UTC stamp directly, the one bar readout that
        /// skipped SpeechTimeFormatter: on an intraday chart in Chicago the hover said 19:30
        /// where the arrow keys said 14:30, and the slider and the arrow keys could name
        /// different days for the same bar. It goes through the same rule now
        /// (SpeechTimeFormatter.ToBarDisplay), so the slider and the arrows agree.</para>
        /// </summary>
        public static string FormatBarDate(DateTime d, int barSeconds)
            => AccessibleTrader.Core.Services.Accessibility.SpeechTimeFormatter.FormatBar(d, barSeconds,
                barSeconds >= 86400 ? "MMM d, yyyy" : "MMM d, yyyy HH:mm");

        /// <summary>Forget the pointer sample and hide the readout until the pointer next moves.</summary>
        private void DropSample()
        {
            bool changed;
            lock (_gate) changed = DropSampleLocked();
            if (changed) Changed?.Invoke();
        }

        /// <summary>The caller holds <c>_gate</c>; true when the readout was showing and the caller must raise Changed.</summary>
        private bool DropSampleLocked()
        {
            _sample = null;
            _mappedFrom = null;
            _awaitingGeometry = false;
            if (Current == null) return false;
            Current = null;
            return true;
        }

        public void Dispose()
        {
            _input.MouseEvent -= OnMouse;
            _stateSub?.Dispose();
            _focusModeSub?.Dispose();
            if (_paneLayout != null) _paneLayout.LayoutChanged -= OnPaneLayoutChanged;
        }
    }
}
