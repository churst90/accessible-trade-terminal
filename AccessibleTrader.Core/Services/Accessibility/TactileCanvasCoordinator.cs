using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility.Dotpad;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Marker interface so DI can eager-instantiate the coordinator. All behaviour
    /// happens via event/state subscriptions wired up in the constructor.
    /// </summary>
    public interface ITactileCanvasCoordinator { }

    /// <summary>
    /// Owns the device-sized dot buffer and keeps it in sync with the workspace.
    ///
    /// On every RedrawEvent it rasterises the FOCUSED series into a buffer sized to
    /// the device's actual graphic dimensions (e.g. 30×10 on the Dot Pad 2nd gen)
    /// and pushes it. On every StateStream change it pushes the focused series'
    /// label + current value to the braille text strip.
    ///
    /// Render strategy is dispatched by the focused component's ComponentDisplayType:
    ///   Line / StepLine / Oscillator   → one pin per column at the value's y-position
    ///   Bar / Histogram                → vertical line from zero to value
    ///   Area / Gradient / Cloud / ZeroArea → filled region between zero (or paired
    ///                                        component) and the value
    ///   Candle / Wick / (primary OHLC) → vertical line from each bar's low to high
    ///   Markers (Dot, Arrow, Cross, …) → single pin at value y-position on bars
    ///                                    where a signal exists (NaN = no pin)
    /// </summary>
    public sealed class TactileCanvasCoordinator : ITactileCanvasCoordinator, IDisposable
    {
        private readonly ITactileDriver _driver;
        private readonly IWorkspaceStore _store;
        private readonly ISpeechFeedbackRouter _speech;
        private readonly ICommandDispatcher _dispatcher;
        private readonly ISettingsManager _settings;
        private readonly IEventBus _eventBus;
        private readonly ILogger<TactileCanvasCoordinator> _logger;

        private readonly List<IDisposable> _subs = new();
        private string? _lastStripText;
        private bool _disposed;

        /// <summary>Settings key gating all tactile/braille output and device detection.</summary>
        internal const string BrailleEnabledKey = SettingsKeys.BrailleEnabled;

        // Whether braille/tactile output is enabled. When false we never probe for a
        // device at startup (the device scan opens COM ports, which can disturb other
        // serial peripherals), so tactile output is strictly opt-in.
        private volatile bool _brailleEnabled;

        // Hot-plug watch: a lightweight background loop that retries the connect when
        // the set of serial ports changes while braille is enabled and no device is
        // connected — so a Dot Pad plugged in after startup is picked up without a
        // restart. Only runs while enabled; cancelled on disable/dispose.
        private static readonly TimeSpan HotPlugPollInterval = TimeSpan.FromSeconds(3);
        private CancellationTokenSource? _watchCts;
        private string _lastPortSnapshot = string.Empty;

        /// <summary>Duration the strip stays in X-value (timestamp) mode after the user navigates with ←/→ before reverting to value mode.</summary>
        internal static readonly TimeSpan XValueDisplayWindow = TimeSpan.FromMilliseconds(1500);

        // Sentinel "no prior state seen yet" for cursor-move detection in the strip subscription.
        private const int UnseenCursorIndex = int.MinValue;
        private int _previousCursorIndex = UnseenCursorIndex;
        private IDisposable? _xValueRevertSub;

        // F4 pause flag — suppresses graphic-area redraws while true. Strip still
        // updates. Auto-cleared on workspace identity change so a fresh chart
        // load can't silently inherit a paused-from-previous-chart state.
        private volatile bool _isPaused;

        public TactileCanvasCoordinator(
            ITactileDriver driver,
            IWorkspaceStore store,
            ISpeechFeedbackRouter speech,
            ICommandDispatcher dispatcher,
            ISettingsManager settings,
            IEventBus eventBus,
            ILogger<TactileCanvasCoordinator> logger)
        {
            _driver = driver;
            _store = store;
            _speech = speech;
            _dispatcher = dispatcher;
            _settings = settings;
            _eventBus = eventBus;
            _logger = logger;

            _driver.KeyPressed += OnDriverKeyPressed;
            _driver.ConnectionChanged += OnDriverConnectionChanged;

            // Tactile output is opt-in. Default off so the device scan (which opens COM
            // ports) never runs unless the user has a display and asks for it.
            _brailleEnabled = _settings.GetSetting(BrailleEnabledKey)?.ToObject<bool>() ?? false;

            // React to the Settings toggle at runtime — connect/disconnect live without
            // a restart. Subscribe even when starting disabled so a later enable works.
            var toggleSub = _eventBus.Subscribe<BrailleModeToggledEvent>(e => OnBrailleModeToggled(e.Enabled));
            if (toggleSub != null) _subs.Add(toggleSub);

            // Auto-clear the pause flag whenever the user loads a new chart so a
            // paused state from a previous chart can't silently swallow the new
            // one's frames. Skip the BehaviorSubject's initial replay — only
            // actual identity transitions should reset.
            _subs.Add(_store.StateStream
                .Select(s => s.Identity)
                .DistinctUntilChanged()
                .Skip(1)
                .Subscribe(_ => _isPaused = false));

            // Only begin device detection if braille is enabled. The hot-plug watch
            // performs the initial connect on its first tick, so there's a single
            // connect path whether the device is present now or plugged in later.
            if (_brailleEnabled) StartHotPlugWatch();

            // Graphic redraws are USER-NAVIGATION ONLY: focus changes, component changes,
            // viewport pan/zoom. Live ticks (which change s.Data and s.CurrentDataIndex)
            // and RedrawEvent (which the chart fires per tick) are intentionally excluded
            // — each tactile frame takes ~1.5-2 seconds of physical pin actuation, and
            // ticks come faster than that, so redrawing on tick leaves the display
            // permanently in motion and unreadable. The strip below keeps updating with
            // the live value on every state change, which is the right surface for
            // tick-rate information; the graphic stays stable under the user's fingers
            // until they navigate.
            var stateGraphicTrigger = _store.StateStream
                .Select(s => (s.FocusedSeriesIndex, s.FocusedSeriesId, s.FocusedComponentIndex,
                              s.ViewportStartIndex, s.ViewportLength,
                              // Visibility key — projects a string snapshot of every
                              // series + component IsVisible flag so 'h' (ToggleHideAction)
                              // triggers a redraw. We deliberately don't watch
                              // ActiveSeries reference directly, because live ticks also
                              // replace it via WithData/UpdateSeriesAction and that would
                              // cause a tactile frame per tick — too slow to keep up with.
                              VisibilityKey: BuildVisibilityKey(s.ActiveSeries)))
                .DistinctUntilChanged()
                .Select(_ => System.Reactive.Unit.Default);

            _subs.Add(stateGraphicTrigger
                .Throttle(TimeSpan.FromMilliseconds(250))
                .Subscribe(_ => SafelyRenderGraphic()));

            // Strip text has three modes: cold ("no chart loaded..."), value-only at
            // cursor, and X-value (timestamp at cursor) for the 1.5 s window following
            // a keyboard ←/→ cursor move. Detecting cursor moves requires comparing the
            // emitted state's CurrentDataIndex against the previous emission's — done
            // via Interlocked.Exchange so the field is safe under concurrent subscribers.
            _subs.Add(_store.StateStream.Subscribe(HandleStateForStrip));
        }

        private void HandleStateForStrip(WorkspaceState state)
        {
            int prev = Interlocked.Exchange(ref _previousCursorIndex, state.CurrentDataIndex);
            bool cursorMoved = prev != UnseenCursorIndex && state.CurrentDataIndex != prev;

            string text = BuildStripText(state, showXValue: cursorMoved);
            SafelyRenderStrip(text);

            if (cursorMoved)
            {
                // Replace any in-flight revert timer with a fresh one — only the latest
                // cursor move's window matters. Dispose of the prior timer outside the
                // exchange to avoid holding the swap across an external call.
                var newTimer = Observable.Timer(XValueDisplayWindow).Subscribe(_ => RevertStripToValueMode());
                var oldTimer = Interlocked.Exchange(ref _xValueRevertSub, newTimer);
                oldTimer?.Dispose();
            }
        }

        private void RevertStripToValueMode()
        {
            if (_disposed || !_driver.IsConnected) return;
            try
            {
                var current = _store.State;
                if (current.Data is null || current.Data.Count == 0) return;
                string text = BuildStripText(current, showXValue: false);
                SafelyRenderStrip(text);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tactile X-value revert render failed.");
            }
        }

        // ── Function keys ──────────────────────────────────────────────────────
        //
        // The Dot Pad's four function keys are device-side shortcuts for "tell me
        // what I'm looking at right now." They route through the speech feedback
        // router because the strip is too small for full names + the user often
        // needs both hands on the tactile area while pressing them.

        private void OnDriverKeyPressed(object? sender, TactileKeyEvent e)
        {
            if (_disposed) return;
            try
            {
                switch (e.Key)
                {
                    case TactileKey.Function1: SpeakFocusedSeriesName(); break;
                    case TactileKey.Function2: SpeakFocusedComponentName(); break;
                    case TactileKey.Function3: SpeakChartIdentity(); break;
                    case TactileKey.Function4: ToggleGraphicPause(); break;
                    // Pan keys route through the SAME path as `[` / `]` keyboard shortcuts —
                    // the chart pans and the tactile redraw falls out automatically from the
                    // existing viewport-change subscription. The dispatcher's chart-focus
                    // gate still applies; if the user has focus on the device rather than the
                    // chart pane, these may be no-ops until the chart pane is focused.
                    case TactileKey.PanLeft:  _dispatcher.Dispatch(SystemCommand.PanLeft);  break;
                    case TactileKey.PanRight: _dispatcher.Dispatch(SystemCommand.PanRight); break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tactile F-key handler threw for key {Key}.", e.Key);
            }
        }

        /// <summary>
        /// Speaks <paramref name="message"/> and mirrors it to the 20-cell strip so a
        /// user who can't hear the audio cue still sees what F1-F4 reported. The next
        /// state change (live tick, cursor move) overwrites the strip naturally —
        /// these messages are transient by design.
        /// </summary>
        private void SpeakAndShow(string message)
        {
            _speech.Speak(message);
            SafelyRenderStrip(message);
        }

        /// <summary>F1: speak the focused series's friendly name. Cold state speaks a placeholder.</summary>
        internal void SpeakFocusedSeriesName()
        {
            var state = _store.State;
            var focused = GetFocusedSeries(state);
            if (focused == null) { SpeakAndShow("no chart loaded"); return; }
            string label = focused.Id == state.PrimarySeriesId
                ? "candles"
                : (!string.IsNullOrEmpty(focused.FriendlyName) ? focused.FriendlyName : focused.Name);
            SpeakAndShow(label);
        }

        /// <summary>F2: speak the focused component's display name (falls back to first visible component).</summary>
        internal void SpeakFocusedComponentName()
        {
            var state = _store.State;
            var focused = GetFocusedSeries(state);
            if (focused == null) { SpeakAndShow("no component focused"); return; }
            var comp = GetFocusedComponent(focused, state) ?? focused.Components.FirstOrDefault(c => c.IsVisible);
            if (comp == null) { SpeakAndShow("no component focused"); return; }
            string label = !string.IsNullOrEmpty(comp.DisplayName) ? comp.DisplayName : comp.Name;
            SpeakAndShow(label);
        }

        /// <summary>F3: speak chart identity — "{symbol} {timeframe} {provider}".</summary>
        internal void SpeakChartIdentity()
        {
            var id = _store.State.Identity;
            var parts = new List<string>(3);
            if (!string.IsNullOrEmpty(id.Symbol))    parts.Add(id.Symbol);
            if (!string.IsNullOrEmpty(id.Timeframe)) parts.Add(id.Timeframe);
            if (!string.IsNullOrEmpty(id.Provider))  parts.Add(id.Provider);
            SpeakAndShow(parts.Count > 0 ? string.Join(" ", parts) : "no chart loaded");
        }

        /// <summary>F4: toggle graphic-area pause. Resume re-renders the current state immediately. Strip never pauses.</summary>
        internal void ToggleGraphicPause()
        {
            bool wasPaused = _isPaused;
            _isPaused = !wasPaused;
            if (wasPaused)
            {
                SpeakAndShow("resumed");
                SafelyRenderGraphic();
            }
            else
            {
                SpeakAndShow("paused");
            }
        }

        private async Task TryConnectAsync()
        {
            try { await _driver.ConnectAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Tactile driver connect failed at startup."); }

            // One-shot initial render after connect, so the chart appears once even if
            // the user never navigates afterward. Subsequent graphic redraws are
            // user-navigation only (focus/viewport changes) — ticks don't redraw.
            if (_driver.IsConnected)
            {
                SafelyRenderGraphic();
                // ALSO force initial strip render — the StateStream's BehaviorSubject
                // replay fired during the constructor BEFORE ConnectAsync resolved,
                // so SafelyRenderStrip returned early on !IsConnected and the cold
                // "no chart loaded..." message never reached the device. Without this
                // catch-up, the strip stays blank until the user navigates.
                try
                {
                    string text = BuildStripText(_store.State, showXValue: false);
                    SafelyRenderStrip(text);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Initial strip render after connect failed."); }
            }
        }

        // ── Enable toggle + hot-plug ────────────────────────────────────────────

        /// <summary>Handles the Settings braille toggle at runtime.</summary>
        private void OnBrailleModeToggled(bool enabled)
        {
            if (_disposed) return;
            if (enabled == _brailleEnabled) return;
            _brailleEnabled = enabled;

            if (enabled)
            {
                // Begin detection immediately; the watch's first tick connects.
                StartHotPlugWatch();
            }
            else
            {
                // Stop probing and drop the device. This app-initiated disconnect
                // intentionally does NOT raise ConnectionChanged, so the Settings
                // dialog's own "Braille disabled" feedback isn't doubled by the driver.
                StopHotPlugWatch();
                _ = _driver.DisconnectAsync();
            }
        }

        /// <summary>Announces device connect/disconnect (hot-plug) through speech.</summary>
        private void OnDriverConnectionChanged(object? sender, TactileConnectionEvent e)
        {
            if (_disposed) return;
            try
            {
                _speech.Speak(e.Connected ? $"{e.DeviceName} connected." : $"{e.DeviceName} disconnected.",
                    interrupt: false);
                // Paint the current chart onto a freshly-connected display right away.
                if (e.Connected) SafelyRenderGraphic();
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Tactile connection announcement failed."); }
        }

        private void StartHotPlugWatch()
        {
            if (_watchCts != null) return; // already running
            var cts = new CancellationTokenSource();
            _watchCts = cts;
            _ = Task.Run(() => HotPlugWatchLoopAsync(cts.Token));
        }

        private void StopHotPlugWatch()
        {
            var cts = Interlocked.Exchange(ref _watchCts, null);
            if (cts == null) return;
            try { cts.Cancel(); cts.Dispose(); } catch { /* best-effort */ }
        }

        /// <summary>
        /// Retries the connect when the serial-port set changes while enabled and not
        /// connected. The first tick performs the initial connect; subsequent ticks only
        /// re-probe when ports actually change (a plug/unplug), so we don't repeatedly
        /// open COM ports — which could disturb other serial peripherals — while idle.
        /// </summary>
        private async Task HotPlugWatchLoopAsync(CancellationToken ct)
        {
            bool first = true;
            while (!ct.IsCancellationRequested && !_disposed)
            {
                try
                {
                    string snapshot = SafePortSnapshot();
                    bool portsChanged = snapshot != _lastPortSnapshot;
                    _lastPortSnapshot = snapshot;

                    if (_brailleEnabled && !_driver.IsConnected && (first || portsChanged))
                    {
                        await TryConnectAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Hot-plug watch iteration failed."); }

                first = false;
                try { await Task.Delay(HotPlugPollInterval, ct).ConfigureAwait(false); }
                catch (TaskCanceledException) { break; }
            }
        }

        /// <summary>A stable, comparable snapshot of the current serial-port set.</summary>
        private static string SafePortSnapshot()
        {
            try
            {
                return string.Join(",", SerialPort.GetPortNames()
                    .Where(p => !string.IsNullOrEmpty(p))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
            }
            catch { return string.Empty; }
        }

        // ── Graphic area ───────────────────────────────────────────────────────

        private void SafelyRenderGraphic()
        {
            if (_disposed || !_driver.IsConnected) return;
            if (_isPaused) return; // F4 pause — graphic frozen until next F4 toggle or identity change
            if (_driver.DisplayWidth <= 0 || _driver.DisplayHeight <= 0) return;
            try
            {
                var state = _store.State;
                // Empty-data case is handled in BuildCanvas as the cold-start splash;
                // we no longer short-circuit here, so the device gets a render even
                // before the first chart loads.
                var canvas = BuildCanvas(state, _driver.DisplayWidth, _driver.DisplayHeight);
                _ = _driver.RenderViewportAsync(canvas, 0, 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tactile graphic render failed.");
            }
        }

        /// <summary>The cold-start splash text rendered in the graphic area until the first chart loads.</summary>
        internal const string ColdSplashText = "accessible trade terminal ready";
        /// <summary>The cold-start strip text rendered until the first chart loads.</summary>
        internal const string ColdStripText = "no chart loaded...";

        /// <summary>
        /// Composes the top/bottom two-pane graphic canvas. Top half = the series above
        /// focused in the tactile cycle, bottom half = focused. When candles are focused
        /// (cycle index 0), bottom = the next series (volume in the cold-load case).
        /// The close-price line (<see cref="CoreSeriesIds.Price"/>) is filtered from the
        /// tactile cycle because it overlays candles visually rather than holding its
        /// own pane — focusing it falls back to rendering candles. Empty chart data
        /// returns the cold-start splash canvas.
        /// </summary>
        internal static bool[,] BuildCanvas(WorkspaceState state, int cols, int rows)
        {
            if (cols <= 0 || rows <= 0)
                return new bool[Math.Max(0, cols), Math.Max(0, rows)];
            if (state.Data is null || state.Data.Count == 0)
                return GraphicTextRenderer.RenderCentered(ColdSplashText, cols, rows);

            var cycle = GetTactileCycle(state);
            if (cycle.Count == 0)
            {
                // No focusable series at all — render candles full canvas as a fallback.
                DotPadDiagnostics.Log("BuildCanvas: empty tactile cycle, falling back to OHLC.");
                return BuildOhlcCanvas(state.Data, state, cols, rows);
            }

            int focusedIdx = FindTactileFocusIndex(cycle, state);
            int topIdx, botIdx;
            if (focusedIdx == 0)
            {
                topIdx = 0;
                botIdx = cycle.Count > 1 ? 1 : 0;
            }
            else
            {
                topIdx = focusedIdx - 1;
                botIdx = focusedIdx;
            }
            var topSeries = cycle[topIdx];
            var botSeries = cycle[botIdx];

            // Focused component only applies to the bottom (focused) pane. The top pane
            // shows the series's default visualisation regardless of which sub-component
            // the user is currently navigating within the focused series below.
            ComponentConfig? botComponent = botSeries.Id == state.FocusedSeriesId
                ? GetFocusedComponent(botSeries, state)
                : null;

            DotPadDiagnostics.Log(
                $"BuildCanvas two-pane: top='{topSeries.Id}', bottom='{botSeries.Id}' " +
                $"botComponent={(botComponent == null ? "(null)" : botComponent.Name)} " +
                $"primary='{state.PrimarySeriesId}'");

            int topRows = rows / 2;
            int botRows = rows - topRows;

            var topCanvas = BuildSeriesCanvas(topSeries, focusedComponent: null, state, cols, topRows);
            var botCanvas = BuildSeriesCanvas(botSeries, botComponent, state, cols, botRows);

            var canvas = new bool[cols, rows];
            for (int x = 0; x < cols; x++)
            {
                for (int y = 0; y < topRows; y++) canvas[x, y] = topCanvas[x, y];
                for (int y = 0; y < botRows; y++) canvas[x, topRows + y] = botCanvas[x, y];
            }
            return canvas;
        }

        /// <summary>
        /// Rasterises a single series into a sub-canvas. Dispatch:
        ///   - Primary candle series, or any component with DisplayType Candle/Wick → OHLC bars.
        ///   - Bar/Histogram → from-baseline fill.
        ///   - Area/Gradient/ZeroArea → filled area from baseline.
        ///   - Marker shapes (Dot, Cross, Diamond, …) → single-pin markers.
        ///   - Anything else → Line.
        /// Used internally by <see cref="BuildCanvas"/> for both panes.
        /// </summary>
        internal static bool[,] BuildSeriesCanvas(
            ChartSeries series, ComponentConfig? focusedComponent,
            WorkspaceState state, int cols, int rows)
        {
            if (cols <= 0 || rows <= 0) return new bool[Math.Max(0, cols), Math.Max(0, rows)];

            // Hidden series render as a blank pane — the user pressed `h` to hide the
            // series, the tactile signal of "this pane is empty" tells them which one
            // disappeared. They can still PgDn/PgUp to it and re-press `h` to unhide.
            if (!series.IsVisible) return new bool[cols, rows];

            // Primary candle series renders as OHLC regardless of which component is
            // focused — the user is "looking at the chart." Non-primary series with
            // a Candle/Wick component still get OHLC dispatch by component shape.
            bool isPrimaryOhlc = series.Id == state.PrimarySeriesId
                || focusedComponent?.DisplayType == ComponentDisplayType.Candle
                || focusedComponent?.DisplayType == ComponentDisplayType.Wick;
            if (isPrimaryOhlc)
                return BuildOhlcCanvas(state.Data, state, cols, rows);

            var renderComponent = focusedComponent ?? series.Components.FirstOrDefault(c => c.IsVisible);
            if (renderComponent == null) return new bool[cols, rows];

            double[] values = series.GetComponentData(renderComponent.Name);
            if (values.Length == 0) return new bool[cols, rows];

            return renderComponent.DisplayType switch
            {
                ComponentDisplayType.Bar or ComponentDisplayType.Histogram
                    => BuildBarsFromBaseline(values, state, cols, rows, baseline: renderComponent.ColorBaseline),
                ComponentDisplayType.Area or ComponentDisplayType.Gradient or ComponentDisplayType.ZeroArea
                    => BuildFilledArea(values, state, cols, rows, baseline: renderComponent.ColorBaseline),
                ComponentDisplayType.Dot or ComponentDisplayType.Diamond or ComponentDisplayType.Cross
                or ComponentDisplayType.Square or ComponentDisplayType.Arrow or ComponentDisplayType.ZeroDot
                or ComponentDisplayType.TriangleUp or ComponentDisplayType.TriangleDown
                or ComponentDisplayType.GradientDot
                    => BuildMarkerDots(values, state, cols, rows),
                _ => BuildLineCanvas(values, state, cols, rows),
            };
        }

        /// <summary>
        /// Returns the tactile focus cycle: <see cref="WorkspaceState.ActiveSeries"/>
        /// minus the close-price line, which overlays candles visually rather than
        /// occupying its own pane. Hidden series stay in the cycle so the user can
        /// PgDn/PgUp through them and unhide via the chart; their pane just renders
        /// blank, which is the tactile signal that the series is hidden.
        /// </summary>
        internal static IReadOnlyList<ChartSeries> GetTactileCycle(WorkspaceState state)
        {
            var active = state.ActiveSeries;
            if (active is null || active.Count == 0) return Array.Empty<ChartSeries>();
            return active.Where(s => s.Id != CoreSeriesIds.Price).ToList();
        }

        /// <summary>
        /// Snapshot key of all series + component visibility flags, used by the
        /// graphic-redraw trigger to fire on <c>h</c> (ToggleHideAction) without
        /// firing on every tick. Format: "seriesId:0/1,compName:0/1,...;..."
        /// </summary>
        private static string BuildVisibilityKey(System.Collections.Immutable.ImmutableList<ChartSeries>? series)
        {
            if (series is null || series.Count == 0) return string.Empty;
            var sb = new System.Text.StringBuilder(series.Count * 16);
            foreach (var s in series)
            {
                sb.Append(s.Id).Append(':').Append(s.IsVisible ? '1' : '0');
                foreach (var c in s.Components)
                {
                    sb.Append(',').Append(c.Name).Append(':').Append(c.IsVisible ? '1' : '0');
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns the index of the focused series within the tactile cycle. If the
        /// user is focused on the price line (filtered out of the cycle) or nothing
        /// is focused, falls back to index 0 (candles).
        /// </summary>
        internal static int FindTactileFocusIndex(IReadOnlyList<ChartSeries> cycle, WorkspaceState state)
        {
            var focused = GetFocusedSeries(state);
            if (focused == null) return 0;
            for (int i = 0; i < cycle.Count; i++)
                if (cycle[i].Id == focused.Id) return i;
            return 0;
        }

        // ── Renderers ──────────────────────────────────────────────────────────
        //
        // Rendering model (revised 2026-05-14): "1-pin-wide bar, dynamic gap."
        // Every bar — candle, volume, oscillator value, marker, line vertex —
        // occupies exactly ONE canvas column. Bar i is placed at
        //   col_i = (int)((i + 0.5) * cols / N)   where N = min(visibleBars, cols)
        // so the visible bars are spread evenly across the canvas with a half-
        // stride gutter at each end. At N == cols the bars touch (continuous);
        // below that the gaps grow uniformly as the user zooms in. No bar is
        // ever wider than 1 pin, regardless of zoom; no aggregation past N.

        /// <summary>
        /// Canvas column for visible-bar i (0-indexed) under the 1-pin-wide bar
        /// density rule. N is the visible-bar count capped to <paramref name="cols"/>.
        /// </summary>
        internal static int BarColumn(int i, int N, int cols)
        {
            if (N <= 0 || cols <= 0) return 0;
            int col = (int)((i + 0.5) * cols / (double)N);
            return Math.Clamp(col, 0, cols - 1);
        }

        /// <summary>
        /// OHLC bars (focused-as-candles): for each visible bar, paints at its
        /// 1-pin column:
        ///   - body fill from open-row → close-row
        ///   - 1-pin vertical gap directly above body's top and below body's bottom
        ///   - upper wick column from (above-gap row) → high-row
        ///   - lower wick column from (below-gap row) → low-row
        /// When the chart viewport holds more bars than the canvas can fit, only
        /// the rightmost <paramref name="cols"/> bars are drawn (no aggregation).
        /// </summary>
        internal static bool[,] BuildOhlcCanvas(IReadOnlyList<Ohlcv> data, WorkspaceState state, int cols, int rows)
        {
            var canvas = new bool[cols, rows];

            var (start, count) = ViewportSample(state, data.Count);
            if (count <= 0 || cols <= 0 || rows <= 0) return canvas;

            int N = Math.Min(count, cols);
            int barStart = start + (count - N);

            double min = double.MaxValue, max = double.MinValue;
            for (int i = 0; i < N; i++)
            {
                int idx = barStart + i;
                if (idx >= data.Count) break;
                var b = data[idx];
                if (b.Low  < min) min = b.Low;
                if (b.High > max) max = b.High;
            }
            double range = max - min;
            if (range <= 0) return canvas;

            for (int i = 0; i < N; i++)
            {
                int idx = barStart + i;
                if (idx >= data.Count) break;
                var b = data[idx];
                int col = BarColumn(i, N, cols);

                int yHigh  = ToRow(b.High,  min, range, rows);
                int yLow   = ToRow(b.Low,   min, range, rows);
                int yOpen  = ToRow(b.Open,  min, range, rows);
                int yClose = ToRow(b.Close, min, range, rows);

                int bodyTop = Math.Min(yOpen, yClose);
                int bodyBot = Math.Max(yOpen, yClose);
                for (int y = bodyTop; y <= bodyBot; y++) canvas[col, y] = true;

                // Upper wick — only renders when there's room for the 1-pin gap
                // above body's top AND at least one wick row above that.
                if (yHigh < bodyTop - 1)
                {
                    for (int y = yHigh; y <= bodyTop - 2; y++) canvas[col, y] = true;
                }
                // Lower wick — symmetric.
                if (yLow > bodyBot + 1)
                {
                    for (int y = bodyBot + 2; y <= yLow; y++) canvas[col, y] = true;
                }
            }
            return canvas;
        }

        /// <summary>
        /// Line: 1 pin per bar at its column, with Bresenham line segments between
        /// consecutive bars to keep the trace continuous when the canvas has more
        /// columns than bars. NaN bars break the trace.
        /// </summary>
        internal static bool[,] BuildLineCanvas(double[] values, WorkspaceState state, int cols, int rows)
        {
            var canvas = new bool[cols, rows];
            var (start, count) = ViewportSample(state, values.Length);
            if (count <= 0 || cols <= 0 || rows <= 0) return canvas;

            int N = Math.Min(count, cols);
            int barStart = start + (count - N);

            double min = double.MaxValue, max = double.MinValue;
            for (int i = 0; i < N; i++)
            {
                int idx = barStart + i;
                if (idx >= values.Length) break;
                double v = values[idx];
                if (double.IsNaN(v)) continue;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (min == double.MaxValue) return canvas;
            double range = max - min;
            if (range <= 0) range = 1;

            bool havePrev = false;
            int prevCol = 0, prevY = 0;
            for (int i = 0; i < N; i++)
            {
                int idx = barStart + i;
                if (idx >= values.Length) break;
                double v = values[idx];
                if (double.IsNaN(v))
                {
                    havePrev = false;
                    continue;
                }
                int col = BarColumn(i, N, cols);
                int y = ToRow(v, min, range, rows);
                if (havePrev) DrawLineSegment(canvas, prevCol, prevY, col, y, cols, rows);
                else canvas[col, y] = true;
                prevCol = col; prevY = y; havePrev = true;
            }
            return canvas;
        }

        /// <summary>
        /// Bars from a baseline (volume, histograms): 1-pin column per visible
        /// bar from baseline-row to value-row. No body/wick split — those are
        /// candle-only.
        /// </summary>
        internal static bool[,] BuildBarsFromBaseline(double[] values, WorkspaceState state, int cols, int rows, double baseline)
        {
            var canvas = new bool[cols, rows];
            var (start, count) = ViewportSample(state, values.Length);
            if (count <= 0 || cols <= 0 || rows <= 0) return canvas;

            int N = Math.Min(count, cols);
            int barStart = start + (count - N);

            double min = baseline, max = baseline;
            for (int i = 0; i < N; i++)
            {
                int idx = barStart + i;
                if (idx >= values.Length) break;
                double v = values[idx];
                if (double.IsNaN(v)) continue;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            double range = max - min;
            if (range <= 0) return canvas;

            int baseRow = ToRow(baseline, min, range, rows);
            for (int i = 0; i < N; i++)
            {
                int idx = barStart + i;
                if (idx >= values.Length) break;
                double v = values[idx];
                if (double.IsNaN(v)) continue;
                int col = BarColumn(i, N, cols);
                int valRow = ToRow(v, min, range, rows);
                int top = Math.Min(baseRow, valRow);
                int bot = Math.Max(baseRow, valRow);
                for (int y = top; y <= bot; y++) canvas[col, y] = true;
            }
            return canvas;
        }

        /// <summary>Filled area: every pin from baseline to value across each bar's column.</summary>
        internal static bool[,] BuildFilledArea(double[] values, WorkspaceState state, int cols, int rows, double baseline)
            => BuildBarsFromBaseline(values, state, cols, rows, baseline);

        /// <summary>
        /// Markers (Dot, Cross, Diamond, …): single pin per signal-bearing bar at
        /// its 1-pin column. NaN bars produce no pin.
        /// </summary>
        internal static bool[,] BuildMarkerDots(double[] values, WorkspaceState state, int cols, int rows)
        {
            var canvas = new bool[cols, rows];
            var (start, count) = ViewportSample(state, values.Length);
            if (count <= 0 || cols <= 0 || rows <= 0) return canvas;

            int N = Math.Min(count, cols);
            int barStart = start + (count - N);

            double min = double.MaxValue, max = double.MinValue;
            for (int i = 0; i < N; i++)
            {
                int idx = barStart + i;
                if (idx >= values.Length) break;
                double v = values[idx];
                if (double.IsNaN(v)) continue;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (min == double.MaxValue) return canvas;
            double range = max - min;
            if (range <= 0) range = 1;

            for (int i = 0; i < N; i++)
            {
                int idx = barStart + i;
                if (idx >= values.Length) break;
                double v = values[idx];
                if (double.IsNaN(v)) continue;
                int col = BarColumn(i, N, cols);
                int y = ToRow(v, min, range, rows);
                canvas[col, y] = true;
            }
            return canvas;
        }

        /// <summary>
        /// Bresenham line from (x0,y0) to (x1,y1), bounds-checked against the
        /// canvas dimensions. Used by <see cref="BuildLineCanvas"/> to fill cols
        /// between adjacent bar pin positions so the line reads as continuous.
        /// </summary>
        private static void DrawLineSegment(bool[,] canvas, int x0, int y0, int x1, int y1, int cols, int rows)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int x = x0, y = y0;
            while (true)
            {
                if (x >= 0 && x < cols && y >= 0 && y < rows) canvas[x, y] = true;
                if (x == x1 && y == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 <  dx) { err += dx; y += sy; }
            }
        }

        // ── Braille strip ──────────────────────────────────────────────────────

        /// <summary>
        /// Builds the strip text in one of three modes:
        ///   - Cold (no data): <see cref="ColdStripText"/>.
        ///   - <paramref name="showXValue"/> true: compact timestamp at cursor
        ///     (lowercase, e.g. "mar 12 14:30") — used during the
        ///     <see cref="XValueDisplayWindow"/> following a ←/→ cursor move.
        ///   - Default: value-only — formatted current-bar value of the focused
        ///     component, no symbol/component prefix. Spec from 2026-05-14: the
        ///     strip is minimal context; F-keys speak series/component/identity.
        /// </summary>
        internal static string BuildStripText(WorkspaceState state, bool showXValue = false)
        {
            if (state.Data is null || state.Data.Count == 0) return ColdStripText;

            int idx = state.CurrentDataIndex >= 0 && state.CurrentDataIndex < state.Data.Count
                ? state.CurrentDataIndex
                : state.Data.Count - 1;

            if (showXValue)
            {
                // Timestamp of the bar at cursor. Lowercase + abbreviated month is
                // tactile-readable on a 20-cell strip and avoids relying on the
                // Grade-2 translator's capitalization indicator.
                var dt = state.Data[idx].Date.ToLocalTime();
                return dt.ToString("MMM d HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                         .ToLowerInvariant();
            }

            var focused = GetFocusedSeries(state);
            var component = focused != null ? GetFocusedComponent(focused, state) : null;

            DotPadDiagnostics.Log(
                $"BuildStripText: focusedId='{state.FocusedSeriesId}', focusedIdx={state.FocusedSeriesIndex}, " +
                $"componentIdx={state.FocusedComponentIndex}, " +
                $"resolved={(focused == null ? "(null)" : $"id='{focused.Id}' name='{focused.Name}'")} " +
                $"component={(component == null ? "(null)" : $"name='{component.Name}' role={component.Role}")} " +
                $"activeSeriesCount={state.ActiveSeries?.Count ?? 0}");

            double value;
            if (focused == null || focused.Id == state.PrimarySeriesId)
            {
                // Primary candle series — pick the OHLCV field by the component's
                // DataMapping (NOT Role): upper_wick and lower_wick both have
                // Role=PriceAction in the candle indicator metadata, so a Role-based
                // switch couldn't tell them apart and the strip would stick on Close.
                // DataMapping is the per-component column key ("open"/"high"/"low"/
                // "close"/"volume") set in CoreIndicatorProvider.
                if (component != null)
                {
                    value = MapOhlcvField(state.Data[idx], component.DataMapping);
                }
                else
                {
                    value = state.Data[idx].Close;
                }
            }
            else if (component != null)
            {
                value = SampleComponent(focused, component.Name, idx);
            }
            else
            {
                // Non-primary series, no component focused — sample first visible.
                var firstComp = focused.Components.FirstOrDefault(c => c.IsVisible);
                value = firstComp != null ? SampleComponent(focused, firstComp.Name, idx) : double.NaN;
            }

            return double.IsNaN(value) ? "-" : FormatValue(value);
        }

        /// <summary>Resolves an OHLCV column reference (case-insensitive DataMapping value) to its bar field.</summary>
        private static double MapOhlcvField(Ohlcv bar, string? mapping)
        {
            if (string.IsNullOrEmpty(mapping)) return bar.Close;
            return mapping.ToLowerInvariant() switch
            {
                "open"   => bar.Open,
                "high"   => bar.High,
                "low"    => bar.Low,
                "close"  => bar.Close,
                "volume" => bar.Volume,
                _        => bar.Close,
            };
        }

        private void SafelyRenderStrip(string text)
        {
            if (_disposed || !_driver.IsConnected || string.IsNullOrEmpty(text)) return;
            if (text == _lastStripText) return;
            _lastStripText = text;
            try { _ = _driver.RenderBrailleTextAsync(text); }
            catch (Exception ex) { _logger.LogWarning(ex, "Tactile braille-strip render failed."); }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static ChartSeries? GetFocusedSeries(WorkspaceState state)
        {
            if (state.ActiveSeries is null || state.ActiveSeries.Count == 0) return null;

            // PgUp/PgDn series navigation updates FocusedSeriesId via SelectSeriesAction;
            // FocusedSeriesIndex is NOT kept in sync by the reducer. Look up by id first
            // and fall back to index only when the id is missing or unresolved.
            if (!string.IsNullOrEmpty(state.FocusedSeriesId))
            {
                var byId = state.ActiveSeries.FirstOrDefault(s => s.Id == state.FocusedSeriesId);
                if (byId != null) return byId;
            }
            int i = state.FocusedSeriesIndex;
            if (i < 0 || i >= state.ActiveSeries.Count) return null;
            return state.ActiveSeries[i];
        }

        private static ComponentConfig? GetFocusedComponent(ChartSeries series, WorkspaceState state)
        {
            if (series.Components is null || series.Components.Count == 0) return null;
            int i = state.FocusedComponentIndex;
            if (i < 0 || i >= series.Components.Count) return null;
            return series.Components[i];
        }

        private static double SampleComponent(ChartSeries series, string componentName, int index)
        {
            var data = series.GetComponentData(componentName);
            if (data.Length == 0 || index < 0 || index >= data.Length) return double.NaN;
            return data[index];
        }

        /// <summary>Returns (startIndex, count) clamped to the workspace's visible viewport.</summary>
        private static (int start, int count) ViewportSample(WorkspaceState state, int dataLength)
        {
            int start = Math.Clamp(state.ViewportStartIndex, 0, Math.Max(0, dataLength - 1));
            int len = Math.Min(state.ViewportLength, dataLength - start);
            return (start, Math.Max(0, len));
        }

        private static int ToRow(double value, double min, double range, int rows)
            => Math.Clamp((int)(((min + range - value) / range) * (rows - 1)), 0, rows - 1);

        /// <summary>Compact numeric formatter for the 20-cell strip.</summary>
        private static string FormatValue(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "-";
            double abs = Math.Abs(value);
            if (abs >= 10_000) return value.ToString("0");
            if (abs >= 100)    return value.ToString("0.#");
            if (abs >= 1)      return value.ToString("0.##");
            return value.ToString("0.###");
        }

        public void Dispose()
        {
            _disposed = true;
            StopHotPlugWatch();
            try { _driver.KeyPressed -= OnDriverKeyPressed; } catch { /* best-effort detach */ }
            try { _driver.ConnectionChanged -= OnDriverConnectionChanged; } catch { /* best-effort detach */ }
            Interlocked.Exchange(ref _xValueRevertSub, null)?.Dispose();
            foreach (var sub in _subs) { try { sub.Dispose(); } catch { } }
            _subs.Clear();
        }
    }
}
