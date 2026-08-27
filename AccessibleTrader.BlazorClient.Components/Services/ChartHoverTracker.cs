using AccessibleTrader.Core.Services;

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
        string OhlcText);

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
        private readonly Core.Services.Accessibility.ISpeechFeedbackRouter? _speech;
        private int _lastSonifiedIndex = -1;
        private int _lastSpokenIndex = -1;

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
            Core.Services.Accessibility.ISpeechFeedbackRouter? speech = null,
            Core.Services.Rendering.IPaneLayoutService? paneLayout = null)
        {
            _input = input;
            _store = store;
            _sonification = sonification;
            _settings = settings;
            _speech = speech;
            _paneLayout = paneLayout;
            _input.MouseEvent += OnMouse;
        }

        public void Toggle()
        {
            IsEnabled = !IsEnabled;
            if (!IsEnabled) Current = null;
            Changed?.Invoke();
        }

        private void OnMouse(double x, double y, string type, double width, double height)
        {
            if (type == "MouseLeave")
            {
                _lastSpokenIndex = -1; // next explore touch re-announces its first bar
                Clear();
                return;
            }
            // TouchExplore (finger sliding in Explore mode) shares the whole hover
            // path — crosshair, readout — but ALWAYS sonifies and ALSO speaks per
            // bar, because the exploring finger is a deliberate "tell me" gesture,
            // not incidental mouse travel. MouseMove keeps the quiet opt-in rules.
            bool explore = type == "TouchExplore";
            if (!IsEnabled && !explore) return;
            if (type != "MouseMove" && !explore) return;

            var state = _store.State;
            if (state.Data == null || state.Data.Count == 0 || width <= 0 || height <= 0)
            {
                Clear();
                return;
            }

            // The plot is the canvas minus the y-axis column and the x-axis strip. Mapping
            // against the whole canvas put the readout up to six bars out at the right edge
            // and, with a volume pane on screen, returned a price from roughly halfway up the
            // main pane's range for a cursor at its visual bottom.
            var dividers = _paneLayout?.Dividers;
            float axisH = _paneLayout?.AxisHeightFraction ?? 0f;
            float axisW = _paneLayout?.AxisWidthFraction ?? 0f;
            double plotWidth = ChartMath.PlotWidth(width, axisW);

            int index = ChartMath.MapXToIndex(x, plotWidth, state.ViewportStartIndex, state.ViewportLength);
            if (index < 0 || index >= state.Data.Count)
            {
                Clear();
                return;
            }

            var bar = state.Data[index];
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
                ? (index - state.ViewportStartIndex) / (double)(state.ViewportLength - 1) * plotWidth
                : x;

            Current = new ChartHoverInfo(
                x, y, barX,
                DateText: FormatBarDate(bar.Date),
                PriceText: PriceFormatter.FormatPrice(price),
                OhlcText: $"O {PriceFormatter.FormatPrice(bar.Open)}  H {PriceFormatter.FormatPrice(bar.High)}  " +
                          $"L {PriceFormatter.FormatPrice(bar.Low)}  C {PriceFormatter.FormatPrice(bar.Close)}");
            Changed?.Invoke();

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
                _speech?.Speak($"{PriceFormatter.FormatPrice(bar.Close)}, {FormatBarDate(bar.Date)}",
                    interrupt: true);
            }
        }

        public static string FormatBarDate(DateTime d)
            // Intraday bars carry a meaningful time; daily-and-up bars are midnight —
            // dropping the redundant 00:00 keeps the readout compact.
            => d.TimeOfDay == TimeSpan.Zero
                ? d.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : d.ToString("MMM d, yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        private void Clear()
        {
            if (Current == null) return;
            Current = null;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            _input.MouseEvent -= OnMouse;
        }
    }
}
