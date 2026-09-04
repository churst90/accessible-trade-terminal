using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>How the canvas is divided between the two charts.</summary>
    public enum SplitOrientation
    {
        /// <summary>Two panes side by side (a vertical divider). Best for comparing two symbols.</summary>
        SideBySide,
        /// <summary>Two panes stacked (a horizontal divider). Best for two timeframes of one symbol.</summary>
        Stacked
    }

    /// <summary>
    /// Renders a second chart alongside the active one.
    ///
    /// <para>
    /// SCOPE, stated plainly: the secondary pane is a READ-ONLY reference view of another tab.
    /// Keyboard navigation, speech, sonification and trading all continue to address the active
    /// tab only. Giving the second pane its own interactive focus would mean routing the whole
    /// navigation and feedback stack at a non-active chart identity, which is exactly the keyed
    /// pipeline work that <c>docs/KEYED_FEEDS_DESIGN.md</c> gates behind an explicit trigger.
    /// Claiming two focusable charts while only one responds would be worse than not shipping it.
    /// </para>
    ///
    /// <para>
    /// The secondary pane's state comes from the frozen <see cref="TabSnapshot"/> the store
    /// already keeps for every inactive tab, so split view adds no new state to maintain and
    /// cannot drift from what the tab shows when activated.
    /// </para>
    /// </summary>
    public interface ISplitViewCoordinator
    {
        bool IsEnabled { get; }
        SplitOrientation Orientation { get; }
        /// <summary>Index of the tab shown in the secondary pane, or -1 when none is selected.</summary>
        int SecondaryTabIndex { get; }

        /// <summary>
        /// Where the ACTIVE chart sits inside the canvas, as fractions of the canvas in 0..1
        /// (left, top, width, height). <c>(0, 0, 1, 1)</c> when split view is off.
        ///
        /// <para>
        /// Normalised rather than in pixels on purpose. The mouse pipeline receives CSS pixels
        /// and the canvas is painted in DEVICE pixels, so any pixel-valued rect would have to be
        /// scaled by a density the mouse layer does not know. A fraction is the same number in
        /// both spaces.
        /// </para>
        /// </summary>
        (float Left, float Top, float Width, float Height) ActiveChartFraction { get; }

        /// <summary>Turns split view on (selecting the first available other tab) or off.</summary>
        void Toggle(WorkspaceState state);

        /// <summary>Moves the secondary pane to the next inactive tab, wrapping.</summary>
        void CycleSecondary(WorkspaceState state);

        /// <summary>Flips between side-by-side and stacked.</summary>
        void ToggleOrientation();

        /// <summary>
        /// Draws the active chart, and — when enabled and a secondary tab is resolvable — the
        /// secondary chart in the other half. Returns the rect the ACTIVE chart occupies, which
        /// hit-testing and pointer mapping must use so mouse coordinates stay correct in split
        /// view.
        /// </summary>
        SKRect Render(SKCanvas canvas, int width, int height, WorkspaceState state, float density);
    }

    /// <inheritdoc cref="ISplitViewCoordinator"/>
    public sealed class SplitViewCoordinator : ISplitViewCoordinator, IDisposable
    {
        /// <summary>Gap in device-independent pixels between the two panes.</summary>
        public const int DividerPx = 6;

        /// <summary>Below this, a split pane is too small to render anything readable.</summary>
        public const int MinPanePx = 120;

        // Nullable so the layout logic (split geometry, tab resolution, clip/translate balance)
        // is unit-testable without standing up the renderer's six-service dependency graph.
        // Production DI always supplies one; a null renderer performs the layout and draws nothing.
        private readonly ChartRenderer? _renderer;
        private readonly IEventBus? _eventBus;
        private readonly IDisposable? _commandSub;

        public SplitViewCoordinator(ChartRenderer? renderer, IEventBus? eventBus = null, IWorkspaceStore? store = null,
            Analysis.IChartPatternCache? patternCache = null, IAppSettings? settings = null)
        {
            _renderer = renderer;
            _patternCache = patternCache;
            _settings = settings;
            _eventBus = eventBus;

            // The store is not held: the only thing this class does with it is read State at
            // the moment a split command arrives, and the closure below captures it for that.
            if (eventBus != null && store != null)
                _commandSub = eventBus.Subscribe<SplitViewCommandEvent>(e =>
                {
                    var state = store.State;
                    switch (e.Command)
                    {
                        case SplitViewCommand.Toggle: Toggle(state); break;
                        case SplitViewCommand.CycleSecondary: CycleSecondary(state); break;
                        case SplitViewCommand.ToggleOrientation: ToggleOrientation(); break;
                    }
                    // The canvas only repaints on state changes, so a split toggle would
                    // otherwise not appear until the next unrelated redraw.
                    eventBus.Publish(new RedrawEvent());
                });
        }

        public void Dispose() => _commandSub?.Dispose();

        public bool IsEnabled { get; private set; }
        public SplitOrientation Orientation { get; private set; } = SplitOrientation.SideBySide;
        public int SecondaryTabIndex { get; private set; } = -1;

        public void Toggle(WorkspaceState state)
        {
            if (IsEnabled)
            {
                IsEnabled = false;
                SecondaryTabIndex = -1;
                Announce("Split view off.");
                return;
            }

            int candidate = FirstOtherTab(state);
            if (candidate < 0)
            {
                Announce("Split view needs a second tab. Open one first.");
                return;
            }

            IsEnabled = true;
            SecondaryTabIndex = candidate;
            Announce($"Split view on, {DescribeOrientation()}. Second pane shows {DescribeTab(state, candidate)}. " +
                     "The second pane is a reference view; keyboard focus stays on this chart.");
        }

        public void CycleSecondary(WorkspaceState state)
        {
            if (!IsEnabled) { Announce("Split view is off."); return; }

            var others = OtherTabIndices(state);
            if (others.Count == 0) { Announce("No other tab to show."); return; }

            int at = others.IndexOf(SecondaryTabIndex);
            SecondaryTabIndex = others[(at + 1) % others.Count];
            Announce($"Second pane shows {DescribeTab(state, SecondaryTabIndex)}.");
        }

        public void ToggleOrientation()
        {
            Orientation = Orientation == SplitOrientation.SideBySide
                ? SplitOrientation.Stacked
                : SplitOrientation.SideBySide;
            Announce($"Split view {DescribeOrientation()}.");
        }

        /// <inheritdoc />
        public (float Left, float Top, float Width, float Height) ActiveChartFraction { get; private set; }
            = (0f, 0f, 1f, 1f);

        public SKRect Render(SKCanvas canvas, int width, int height, WorkspaceState state, float density)
        {
            var full = new SKRect(0, 0, width, height);
            var snapshot = ResolveSecondary(state);

            // Fall back to a single full-size chart whenever the split cannot be honoured — a
            // missing tab or a canvas too small to divide. Rendering a squashed, unreadable pane
            // would be worse than quietly staying single.
            if (!IsEnabled || snapshot == null || !TrySplit(width, height, out var primary, out var secondary))
            {
                ActiveChartFraction = (0f, 0f, 1f, 1f);
                RenderActive(canvas, full, state, density);
                return full;
            }

            // Recorded on every frame so the mouse pipeline maps pointer coordinates into the
            // ACTIVE pane rather than the whole canvas. Without this, clicking while split view
            // is on lands on the wrong bar — the drawing tools, the crosshair and the context
            // menu all read a position roughly twice as far along the chart as the cursor is.
            ActiveChartFraction = (
                primary.Left / width,
                primary.Top / height,
                primary.Width / width,
                primary.Height / height);

            RenderActive(canvas, primary, state, density);
            RenderSnapshot(canvas, secondary, snapshot, density);
            return primary;
        }

        /// <summary>
        /// Divides the canvas in half along the current orientation. Returns false when either
        /// half would fall below <see cref="MinPanePx"/> on the split axis.
        /// </summary>
        internal bool TrySplit(int width, int height, out SKRect primary, out SKRect secondary)
        {
            primary = default;
            secondary = default;

            if (Orientation == SplitOrientation.SideBySide)
            {
                float half = (width - DividerPx) / 2f;
                if (half < MinPanePx) return false;
                primary = new SKRect(0, 0, half, height);
                secondary = new SKRect(half + DividerPx, 0, width, height);
            }
            else
            {
                float half = (height - DividerPx) / 2f;
                if (half < MinPanePx) return false;
                primary = new SKRect(0, 0, width, half);
                secondary = new SKRect(0, half + DividerPx, width, height);
            }
            return true;
        }

        private readonly Analysis.IChartPatternCache? _patternCache;
        private readonly IAppSettings? _settings;

        /// <summary>
        /// The formations to draw this frame, or null when the drawing is switched off.
        ///
        /// <para>
        /// Resolved here rather than inside the renderer because only this layer has the whole
        /// series and the chart's identity. The renderer sees just the visible slice, and detecting
        /// on a moving window would make formations appear and disappear as the user pans.
        /// </para>
        /// </summary>
        private IReadOnlyList<Analysis.ChartPattern>? Formations(ChartIdentity id, IReadOnlyList<Ohlcv>? data)
        {
            if (_patternCache == null || data == null) return null;
            if (_settings?.ShowChartPatternVisuals != true) return null;
            return _patternCache.For(id, data);
        }

        private void RenderActive(SKCanvas canvas, SKRect rect, WorkspaceState s, float density) =>
            InRect(canvas, rect, (w, h) => _renderer?.Render(
                canvas, w, h, s.Data, s.ActiveSeries, s.CurrentDataIndex,
                s.ViewportStartIndex, s.ViewportLength, s.ViewportRange, s.PaneRanges,
                s.IsHeikinAshi, s.IsLogScale, density, s.PaneHeightRatios,
                s.IndicatorPaneScrollIndex, s.RightMarginBars, Formations(s.Identity, s.Data)));

        private void RenderSnapshot(SKCanvas canvas, SKRect rect, TabSnapshot t, float density) =>
            InRect(canvas, rect, (w, h) => _renderer?.Render(
                canvas, w, h, t.Data, t.ActiveSeries, t.CurrentDataIndex,
                t.ViewportStartIndex, t.ViewportLength, t.ViewportRange, t.PaneRanges,
                t.IsHeikinAshi, t.IsLogScale, density, t.PaneHeightRatios,
                t.IndicatorPaneScrollIndex, t.RightMarginBars, Formations(t.Identity, t.Data)));

        /// <summary>
        /// Clips and translates so the renderer can keep drawing from its own origin. It takes
        /// width and height and lays out from (0,0), so a translate is all that is needed to
        /// place a full chart anywhere on the canvas.
        /// </summary>
        private static void InRect(SKCanvas canvas, SKRect rect, Action<int, int> draw)
        {
            int w = (int)Math.Round(rect.Width);
            int h = (int)Math.Round(rect.Height);
            if (w <= 0 || h <= 0) return;

            int saved = canvas.Save();
            try
            {
                canvas.ClipRect(rect);
                canvas.Translate(rect.Left, rect.Top);
                draw(w, h);
            }
            // One pane throwing must not take the other down with it, and must never escape onto
            // the Blazor dispatcher — an unhandled exception there tears down the whole circuit.
            catch { /* keep the other pane and the last good frame */ }
            finally { canvas.RestoreToCount(saved); }
        }

        private TabSnapshot? ResolveSecondary(WorkspaceState state)
        {
            if (SecondaryTabIndex < 0 || state.TabSnapshots == null) return null;
            var snapshot = state.TabSnapshots.FirstOrDefault(t => t.TabIndex == SecondaryTabIndex);
            // A snapshot with no bars renders as an empty frame, which reads as a broken pane.
            return snapshot is { Data.Count: > 0 } ? snapshot : null;
        }

        private static List<int> OtherTabIndices(WorkspaceState state) =>
            state.TabSnapshots == null
                ? new List<int>()
                : state.TabSnapshots
                    .Where(t => t.TabIndex != state.ActiveTabIndex)
                    .Select(t => t.TabIndex)
                    .OrderBy(i => i)
                    .ToList();

        private static int FirstOtherTab(WorkspaceState state)
        {
            var others = OtherTabIndices(state);
            return others.Count > 0 ? others[0] : -1;
        }

        /// <summary>
        /// How a tab is named when it is the one in the second pane. Internal rather than private
        /// because Shift+F1 names it too (AccessibilityFeedbackCoordinator.SpeakContextSummary):
        /// two spellings of "which chart is that" would drift, and the one a user hears on toggle
        /// has to be the one they hear when they ask again.
        /// </summary>
        internal static string DescribeTab(WorkspaceState state, int tabIndex)
        {
            var snapshot = state.TabSnapshots?.FirstOrDefault(t => t.TabIndex == tabIndex);
            if (snapshot == null) return $"tab {tabIndex + 1}";
            string symbol = string.IsNullOrEmpty(snapshot.Identity.Symbol)
                ? $"tab {tabIndex + 1}"
                : snapshot.Identity.Symbol;
            return $"{symbol} {snapshot.Identity.Timeframe}".Trim();
        }

        private string DescribeOrientation() =>
            Orientation == SplitOrientation.SideBySide ? "side by side" : "stacked";

        private void Announce(string message) =>
            _eventBus?.Publish(new AnnouncementEvent(message, true));
    }
}
