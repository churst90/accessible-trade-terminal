using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Centralized utility for common chart-related mathematical calculations.
    /// Ensures consistency across rendering, sonification, and accessibility systems.
    /// </summary>
    public static class ChartMath
    {
        // ── The vertical axis: one set of steps for the grid and the labels ─────────────
        //
        // These were two copies of the same "nice number" algorithm in two files —
        // BackgroundLayer aiming for ~7 gridlines, ChartRenderer.RenderYAxis aiming for ~5
        // labels — and the second one's comment promised "Label positions align exactly with
        // major gridlines so the chart reads as a coherent grid, not a grid + an unrelated label
        // track". They did not. Take a pane of range 20: the grid rounds 20/7 = 2.86 down to a
        // step of 2, the labels round 20/5 = 4 up to a step of 5, and the labels at 5 and 15 sit
        // on no line at all. Any range in roughly 17.5–24.5 × 10^k does it, which includes a
        // 20,000-dollar window on a BTC chart. Nobody listening to this app can hear it and
        // everybody looking at it sees it.
        //
        // So the label step is now DERIVED from the gridline step — a whole multiple of it —
        // which makes alignment structural rather than a coincidence that held for most ranges.

        /// <summary>
        /// The 1–2–5–10 step closest to <paramref name="range"/> divided into
        /// <paramref name="targetCount"/> parts. Returns 0 for a degenerate range, which both
        /// callers treat as "draw nothing".
        /// </summary>
        public static double NiceStep(double range, int targetCount)
        {
            if (targetCount <= 0) return 0;
            if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range)) return 0;

            double rough = range / targetCount;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double fraction = rough / magnitude;
            if (fraction < 1.5) return 1 * magnitude;
            if (fraction < 3.5) return 2 * magnitude;
            if (fraction < 7.5) return 5 * magnitude;
            return 10 * magnitude;
        }

        /// <summary>How many labels a pane of this height wants. Small indicator panes want fewer.</summary>
        public static int TargetLabelCount(float paneHeightPx, float density)
            => paneHeightPx < 100 * density ? 3 : 5;

        /// <summary>The gridline step for a pane. About seven lines across the pane.</summary>
        public static double GridStep(double range) => NiceStep(range, 7);

        /// <summary>
        /// The step between axis LABELS: always a whole multiple of the gridline step, so every
        /// label lands on a line.
        ///
        /// <para>
        /// The multiple is chosen to get as close to <paramref name="targetLabelCount"/> labels as
        /// a whole multiple allows, and is never less than one — a label step finer than the grid
        /// would put labels between lines, which is the defect read backwards.
        /// </para>
        /// </summary>
        public static double LabelStep(double range, int targetLabelCount, double gridStep)
        {
            if (gridStep <= 0 || range <= 0 || targetLabelCount <= 0) return gridStep;
            double wanted = range / targetLabelCount;
            int multiple = (int)Math.Round(wanted / gridStep, MidpointRounding.AwayFromZero);
            return Math.Max(1, multiple) * gridStep;
        }

        /// <summary>
        /// True when <paramref name="value"/> falls on a label — the test a renderer uses to draw
        /// that gridline brighter. Tolerance is relative to the step, because the axis walks by
        /// repeated addition and the residue grows with the number of steps taken.
        /// </summary>
        public static bool IsOnLabel(double value, double labelStep)
        {
            if (labelStep <= 0) return false;
            double ratio = value / labelStep;
            return Math.Abs(ratio - Math.Round(ratio)) < 1e-6;
        }

        /// <summary>
        /// Range-aware axis label text. A flat F2/F4 choice collapses to "0.0000" for assets whose
        /// visible range is tiny — early KAS ticks around $0.00003 — so the decimal count comes
        /// from the range's magnitude and always carries about two significant digits beyond it.
        ///
        /// <para>
        /// "−0.00" is stripped to "0.00". A value a hair below zero is a rounding residue from the
        /// axis-step arithmetic, not a real negative, and the minus sign survives the rounding. On
        /// a price axis that reads as a data error — exactly the kind of detail that makes a
        /// careful reader distrust every other number on screen.
        /// </para>
        /// </summary>
        public static string FormatAxisValue(double value, double range)
        {
            double absRange = Math.Abs(range);
            int decimals = (absRange == 0 || double.IsNaN(absRange) || double.IsInfinity(absRange))
                ? 2
                : Math.Clamp(2 - (int)Math.Floor(Math.Log10(absRange)), 2, 10);

            string text = value.ToString("F" + decimals);

            if (text.Length > 1 && text[0] == '-' && text.AsSpan(1).IndexOfAnyExcept('0', '.', ',') < 0)
                text = text[1..];

            return text;
        }

        /// <summary>
        /// The same value in the short form a person would say out loud: <c>120M</c>, <c>1.5M</c>,
        /// <c>950K</c>, <c>2.5B</c>.
        ///
        /// <para>
        /// This exists because of the axis STRIP, which is a fixed width — 60 CSS px by theme —
        /// and because nine-digit numbers are not an edge case on this axis: US large-cap share
        /// volume is routinely nine digits, so on a TSLA daily the volume labels came out as
        /// <c>120000000.00</c> and ran past the right edge of the canvas while the price pane's
        /// <c>440.00</c> stopped five pixels short. Seen in the site screenshots before and after
        /// v2.12.0, so not a regression — the 2.11.0 axis pass measured labels against each other
        /// and against the crosshair badge, and never against the edge they were drawn at.
        /// </para>
        ///
        /// <para>
        /// Widening the strip is the other obvious fix and it is the wrong one here: the axis
        /// width is charged to every pane and comes straight out of the plot area, which is the
        /// real estate v2.12.0 spent a whole scope taking BACK (44.9% to 65.0% of the window).
        /// Fifty more pixels of gutter to spell out a number nobody reads digit by digit is a bad
        /// trade. <b>The renderer asks for this form only when the full one does not fit</b>, so a
        /// price axis — which never approaches ten million — is untouched, and the decision
        /// follows the theme's axis width and the display density rather than a magnitude
        /// threshold guessed here.
        /// </para>
        /// </summary>
        public static string FormatAxisValueCompact(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return value.ToString();

            double abs = Math.Abs(value);

            (double divisor, string suffix) =
                abs >= 1e12 ? (1e12, "T") :
                abs >= 1e9 ? (1e9, "B") :
                abs >= 1e6 ? (1e6, "M") :
                abs >= 1e3 ? (1e3, "K") :
                (1.0, "");

            double scaled = value / divisor;

            // One decimal only where it says something. 120M and 1.5M both read cleanly; 120.0M
            // is longer for no information, and the whole point of this form is length.
            string text = Math.Abs(scaled) >= 100 || scaled == Math.Floor(scaled)
                ? scaled.ToString("0")
                : scaled.ToString("0.#");

            // Same negative-zero guard as FormatAxisValue: -0.4M rounds to "-0" without it, and a
            // minus sign in front of a zero on an axis is just noise.
            if (text is "-0") text = "0";

            return text + suffix;
        }

        /// <summary>
        /// The date format the x axis uses for a visible span, and whether it should call out
        /// midnight.
        ///
        /// <para>
        /// A 6h chart showing "06:00 06:00 06:00 06:00 06:00" tells the reader nothing — every bar
        /// is at a multiple of 6h UTC. Under two days is intraday and wants the clock; two to
        /// sixty days is a swing view and wants the date; beyond that, the month.
        /// </para>
        /// </summary>
        public static (string Format, bool MarkDateBoundaries) XAxisFormat(TimeSpan span)
        {
            if (span.TotalDays < 2) return ("HH:mm", true);
            if (span.TotalDays < 60) return ("MM/dd", false);
            return ("MMM d", false);
        }

        /// <summary>
        /// Calculates the min/max range for a specific series within a viewport.
        /// </summary>
        public static (double Min, double Max) GetSeriesRange(ChartSeries series, int viewportStart, int viewportLength, (double Min, double Max) mainViewportRange)
        {
            // Primary price/candle series always use the shared global viewport range
            if (series.Pane == "Main") return mainViewportRange;

            double min = double.MaxValue, max = double.MinValue;
            bool hasData = false;

            foreach (var comp in series.Components)
            {
                var data = series.GetComponentData(comp.Name);
                if (data == null || data.Length == 0) continue;
                
                int end = Math.Min(viewportStart + viewportLength, data.Length);
                for (int i = viewportStart; i < end; i++)
                {
                    var val = data[i];
                    if (!double.IsNaN(val))
                    {
                        if (val < min) min = val;
                        if (val > max) max = val;
                        hasData = true;
                    }
                }
            }

            if (!hasData || min == double.MaxValue) return (0, 100);

            // VISUAL BUFFER: Add a small 10% margin to the top and bottom of the pane
            // so indicators don't touch the boundaries, improving accessibility legibility.
            if (Math.Abs(max - min) < 0.000001) { min -= 1.0; max += 1.0; }
            double buffer = (max - min) * 0.1;
            return (min - buffer, max + buffer);
        }

        /// <summary>
        /// Retrieves the value of a specific component at an index, falling back to OHLCV data for price series.
        /// Uses snapshots when provided to ensure thread-safety during background playback.
        /// </summary>
        public static double GetPointValue(ChartSeries series, Ohlcv point, int componentIndex, int dataIndex, double[]? componentDataSnapshot = null)
        {
            if (componentIndex < 0 || componentIndex >= series.Components.Count) return double.NaN;
            
            var component = series.Components[componentIndex];
            
            // Check snapshot first
            if (componentDataSnapshot != null && dataIndex >= 0 && dataIndex < componentDataSnapshot.Length)
            {
                return componentDataSnapshot[dataIndex];
            }
            
            // Check live data
            var data = series.GetComponentData(component.Name);
            if (data != null && dataIndex >= 0 && dataIndex < data.Length)
            {
                return data[dataIndex];
            }

            // MAPPING: Fallback for primary price series where components are virtual.
            // Maps candle parts (High, Low, etc.) to their logical names. Accepts both
            // the new snake_case machine names (body/upper_wick/lower_wick/line) and the
            // legacy display-style names (Candle Body/Upper Wick/Lower Wick/Close) so
            // saved workspaces predating the Phase 2 rename still resolve correctly.
            if (series.Id == "price" || series.Id == "candles")
            {
                double mapped = PriceComponentFallback(component.Name, point);
                // An unrecognised component on the price series still has to render somewhere;
                // the close is the least-wrong y for it. Speech does NOT take this branch —
                // saying a number that was never the component's value is worse than silence.
                return double.IsNaN(mapped) ? point.Close : mapped;
            }

            return double.NaN;
        }

        /// <summary>
        /// Maps a price-series component NAME onto the OHLCV field it stands for, for the case
        /// where the series carries no component array of its own (the primary price series'
        /// components are virtual). Returns NaN for a name that is not a candle part.
        ///
        /// <para>
        /// Accepts both the snake_case machine ids (<c>body</c>, <c>upper_wick</c>,
        /// <c>lower_wick</c>, <c>line</c>) and the legacy display-style names
        /// (<c>Candle Body</c>, <c>Upper Wick</c>, …) so workspaces saved before the Phase 2
        /// rename still resolve. Case-insensitive throughout, because the ids reach here from
        /// saved JSON and from provider metadata, neither of which is normalised.
        /// </para>
        ///
        /// <para>
        /// Shared deliberately: <c>SpeechFormatter.GetPointValue</c> carried its own copy that
        /// still tested the PRE-rename names with <c>string.Contains</c>
        /// (<c>c.Contains("Body")</c>, <c>"Upper"</c>, <c>"Lower"</c>, <c>"Open"</c>) — against
        /// the current ids every one of those is false, so it returned NaN and the wick read
        /// "no data" whenever the primary lookup missed.
        /// </para>
        /// </summary>
        public static double PriceComponentFallback(string componentName, Ohlcv point)
        {
            if (string.IsNullOrWhiteSpace(componentName)) return double.NaN;
            string n = componentName.Trim();

            if (Is(n, "Open")) return point.Open;
            if (Is(n, "High", "upper_wick", "Upper Wick")) return point.High;
            if (Is(n, "Low", "lower_wick", "Lower Wick")) return point.Low;
            if (Is(n, "Close", "body", "line", "Candle Body")) return point.Close;
            if (Is(n, "Volume")) return point.Volume;
            return double.NaN;

            static bool Is(string name, params string[] candidates)
            {
                foreach (var c in candidates)
                    if (name.Equals(c, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
        }

        /// <summary>
        /// Transforms standard OHLCV data into Heikin Ashi format.
        /// Formula:
        /// Close = (O+H+L+C)/4
        /// Open = (PrevOpen + PrevClose)/2
        /// High = Max(H, Open, Close)
        /// Low = Min(L, Open, Close)
        /// </summary>
        public static List<Ohlcv> CalculateHeikinAshi(List<Ohlcv> data)
        {
            var haData = new List<Ohlcv>();
            if (!data.Any()) return haData;

            double prevOpen = data[0].Open;
            double prevClose = data[0].Close;

            foreach (var d in data)
            {
                double close = (d.Open + d.High + d.Low + d.Close) / 4;
                double open = (prevOpen + prevClose) / 2;
                double high = Math.Max(d.High, Math.Max(open, close));
                double low = Math.Min(d.Low, Math.Min(open, close));

                haData.Add(new Ohlcv(d.Date, open, high, low, close, d.Volume));
                
                prevOpen = open;
                prevClose = close;
            }
            return haData;
        }

        /// <summary>
        /// The bar at <paramref name="index"/> AS DRAWN: its Heikin-Ashi equivalent when that
        /// mode is on, the raw bar otherwise.
        ///
        /// <para>
        /// This existed three times — <c>BarDetailService.BarAsDrawn</c>, and inline copies in
        /// <c>NavigationFeedbackManager</c> and <c>NavigationSonifier</c> — which is how the
        /// readouts drifted apart in the first place: each one decided for itself whether it was
        /// describing the drawn candle or the raw bar, and a reader had to check all three to
        /// find out which. One function, one answer.
        /// </para>
        ///
        /// <para>
        /// It answers for the CANDLES only. The close line (<c>CoreSeriesIds.Price</c>) is raw
        /// whatever the candle style is: it is rendered from the raw close, the title bar quotes
        /// the raw close, and those two are what a trader reads as "the price". Callers on that
        /// series pass <c>isHeikinAshi: false</c> or do not call this at all.
        /// </para>
        ///
        /// <para>
        /// The single-bar guard is inherited from all three original copies: with one bar loaded
        /// the transform has no previous HA bar to derive an open from, and the seeded open makes
        /// the result an average of one bar rather than a candle. Raw is the honest answer there.
        /// </para>
        /// </summary>
        public static Ohlcv BarAsDrawn(IReadOnlyList<Ohlcv> data, int index, bool isHeikinAshi)
        {
            var raw = data[index];
            if (!isHeikinAshi || data.Count <= 1) return raw;

            var slice = new List<Ohlcv>(index + 1);
            for (int i = 0; i <= index; i++) slice.Add(data[i]);
            var ha = CalculateHeikinAshi(slice);
            return ha.Count > 0 ? ha[^1] : raw;
        }

        /// <summary>
        /// The last <paramref name="count"/> bars AS DRAWN, ending at and including
        /// <paramref name="endIndex"/>, oldest first.
        ///
        /// <para>
        /// <see cref="BarAsDrawn"/> answers for ONE bar and rebuilds the whole Heikin-Ashi series
        /// to do it, because HA is recursive from bar zero. A caller that needs a short trailing
        /// window — the candle-pattern analyser needs three bars plus a trend lookback — would
        /// otherwise pay that O(n) rebuild once per bar in the window. This pays it once for the
        /// whole window and slices the tail off.
        /// </para>
        ///
        /// <para>
        /// Returns fewer than <paramref name="count"/> bars near the start of the series, and an
        /// empty list when there is no data or the index is out of range. Callers must cope with
        /// a short window rather than assuming a fixed length.
        /// </para>
        /// </summary>
        public static IReadOnlyList<Ohlcv> BarsAsDrawn(
            IReadOnlyList<Ohlcv>? data, int endIndex, int count, bool isHeikinAshi)
        {
            if (data == null || data.Count == 0 || count <= 0) return Array.Empty<Ohlcv>();
            if (endIndex < 0 || endIndex >= data.Count) return Array.Empty<Ohlcv>();

            int start = Math.Max(0, endIndex - count + 1);

            if (!isHeikinAshi || data.Count <= 1)
            {
                var raws = new List<Ohlcv>(endIndex - start + 1);
                for (int i = start; i <= endIndex; i++) raws.Add(data[i]);
                return raws;
            }

            var prefix = new List<Ohlcv>(endIndex + 1);
            for (int i = 0; i <= endIndex; i++) prefix.Add(data[i]);
            var ha = CalculateHeikinAshi(prefix);
            if (ha.Count == 0) return Array.Empty<Ohlcv>();

            int haStart = Math.Max(0, ha.Count - (endIndex - start + 1));
            return ha.GetRange(haStart, ha.Count - haStart);
        }

        /// <summary>
        /// Maps a cursor X pixel position to an absolute bar index in the loaded data.
        /// Inverse of the renderer's bar layout: 0 px = ViewportStartIndex, full width =
        /// start + length - 1. The result is NOT clamped to the data range — callers
        /// decide whether right-margin/future indices are meaningful (drawings allow
        /// them; bar selection does not).
        /// </summary>
        public static int MapXToIndex(double x, double width, int startIndex, int length)
        {
            if (width <= 0 || length <= 0) return startIndex;
            double percent = x / width;
            return startIndex + (int)Math.Round(percent * (length - 1));
        }

        /// <summary>
        /// Maps a cursor Y pixel position to a price within the viewport range.
        /// Inverse of <see cref="MapY"/> for the pane spanning [0, height]. Supports
        /// linear and log scales, with the same degenerate-range guards the forward
        /// mapping uses (min forced positive on log scale; max forced above min).
        /// </summary>
        public static double MapYToPrice(double y, double height, double min, double max, bool isLog)
        {
            if (height <= 0) return min;
            double percent = 1.0 - (y / height);
            if (isLog)
            {
                if (min <= 0) min = 0.01;
                if (max <= min) max = min + 1.0;
                return Math.Exp(Math.Log(min) + (percent * (Math.Log(max) - Math.Log(min))));
            }
            return min + (percent * (max - min));
        }

        /// <summary>
        /// Maps a price to a Y pixel position within a pane spanning [0, height] —
        /// the forward companion of <see cref="MapYToPrice"/>, used by anchor-handle
        /// hit-testing and the hover crosshair.
        /// </summary>
        public static double PriceToScreenY(double price, double height, double min, double max, bool isLog)
        {
            if (isLog)
            {
                if (min <= 0) min = 0.01;
                if (max <= min) max = min + 1.0;
                double pct = (Math.Log(price) - Math.Log(min)) / (Math.Log(max) - Math.Log(min));
                return (1.0 - pct) * height;
            }
            if (max <= min) return 0;
            double linearPct = (price - min) / (max - min);
            return (1.0 - linearPct) * height;
        }

        /// <summary>
        /// Where a value sits within a range as a fraction 0..1 (0 = min, 1 = max), on the
        /// linear or the logarithmic scale — the same guards and the same maths as
        /// <see cref="MapY"/>, so a pitch derived from this lands where the pixel does. The
        /// sonification normalised linearly whatever the chart's scale, so on a log-scaled chart
        /// the eye and the ear disagreed: a price drawn at the vertical middle of a 10k–100k
        /// pane sounded a quarter of the way up. Clamped, because audio has no off-canvas.
        /// </summary>
        public static double NormalizedPosition(double value, double min, double max, bool isLogScale)
        {
            if (isLogScale)
            {
                if (value <= 0) value = 0.00001;
                if (min <= 0) min = 0.00001;
                if (max <= 0) max = 0.00001;
                if (Math.Abs(max - min) < 0.000001) return 0.5;
                double pct = (Math.Log(value) - Math.Log(min)) / (Math.Log(max) - Math.Log(min));
                return Math.Clamp(pct, 0, 1);
            }
            double span = Math.Max(0.01, max - min);
            return Math.Clamp((value - min) / span, 0, 1);
        }

        /// <summary>
        /// Maps a numeric data value to a physical Y-coordinate within a bounded area.
        /// Supports both Linear and Logarithmic scaling.
        /// </summary>
        public static float MapY(double value, float top, float bottom, double min, double max, bool isLogScale)
        {
            float height = bottom - top;
            if (height <= 0) return top;

            if (isLogScale)
            {
                // LOG SCALE: Maps price to Log space before projecting to screen.
                if (value <= 0) value = 0.00001; 
                if (min <= 0) min = 0.00001;
                if (max <= 0) max = 0.00001;
                
                if (Math.Abs(max - min) < 0.000001) return top + (height / 2.0f);

                double logVal = Math.Log(value);
                double logMin = Math.Log(min);
                double logMax = Math.Log(max);
                
                return (float)(bottom - ((logVal - logMin) / (logMax - logMin) * height));
            }
            else
            {
                // LINEAR SCALE: Simple percentage-based projection.
                double range = max - min;
                if (range <= 0.000001) return top + (height / 2.0f);
                return (float)(bottom - ((value - min) / range * height));
            }
        }

        // ── Pointer space → plot space ───────────────────────────────────────
        //
        // The renderer does not draw into the whole canvas. A y-axis column of
        // `theme.AxisWidth * density` runs down the right and an x-axis strip of
        // `_axisHeight` along the bottom; bars are laid across what is left. Every mapping
        // from a pixel back to a bar or a price has to subtract the same two strips, and
        // before 2026-08-27 none of them did:
        //
        //   * MapXToIndex was handed the FULL canvas width by DrawingInteractionManager,
        //     ChartHitTester and ChartHoverTracker, so on a 1280 px chart with a 120-bar
        //     viewport a click on the rightmost candle resolved to bar 113 instead of 119.
        //   * MapYToPrice was handed the FULL canvas height, while the renderer maps the
        //     price range into a main pane of `height - axisHeight - Σ indicatorHeights`.
        //     With a volume pane on screen the main pane is roughly 47% of the canvas, so a
        //     click at the visual bottom of the price pane returned Min + 0.53 × (Max − Min).
        //     Every mouse-placed drawing anchor landed at the wrong price.
        //
        // ChartHitTester already resolved pane BANDS correctly from IPaneLayoutService; it
        // simply never applied the same treatment to the horizontal. These helpers are the
        // one place that knows the rule, so the two code paths on a single click can no
        // longer disagree.

        /// <summary>
        /// The plot width — canvas width minus the y-axis column.
        /// <paramref name="axisWidthFraction"/> comes from <c>IPaneLayoutService</c>.
        /// </summary>
        public static double PlotWidth(double canvasWidth, float axisWidthFraction)
            => canvasWidth * (1.0 - Math.Clamp(axisWidthFraction, 0f, 0.5f));

        /// <summary>
        /// The plot height — canvas height minus the x-axis strip.
        /// </summary>
        public static double PlotHeight(double canvasHeight, float axisHeightFraction)
            => canvasHeight * (1.0 - Math.Clamp(axisHeightFraction, 0f, 0.5f));

        /// <summary>
        /// The vertical band occupied by the pane under <paramref name="y"/>, in pixels, given
        /// the rendered dividers. Returns null when the cursor is over the x-axis strip, where
        /// there is no price to report.
        ///
        /// <para>The same band walk <c>ChartHitTester</c> does — hoisted here so the pointer
        /// mappings can use it instead of assuming the price pane owns the whole canvas.</para>
        /// </summary>
        public static (double Top, double Bottom)? PaneBandPx(
            double y,
            double canvasHeight,
            IReadOnlyList<(string BelowPaneName, float DividerFraction)>? dividers,
            float axisHeightFraction)
        {
            if (canvasHeight <= 0) return null;

            double plotBottomFrac = 1.0 - Math.Clamp(axisHeightFraction, 0f, 0.5f);
            double yFrac = y / canvasHeight;
            if (yFrac < 0 || yFrac > plotBottomFrac) return null;

            double bandTopFrac = 0.0;
            double bandBottomFrac = plotBottomFrac;
            if (dividers != null)
            {
                foreach (var (_, frac) in dividers)
                {
                    if (yFrac >= frac) bandTopFrac = frac;
                    else { bandBottomFrac = Math.Min(bandBottomFrac, frac); break; }
                }
            }

            double top = bandTopFrac * canvasHeight;
            double bottom = bandBottomFrac * canvasHeight;
            return bottom > top ? (top, bottom) : null;
        }

        /// <summary>
        /// A cursor Y within the whole canvas mapped to a price in the pane it actually falls
        /// in. Returns <see cref="double.NaN"/> over the x-axis strip.
        /// </summary>
        public static double MapYToPriceInPane(
            double y,
            double canvasHeight,
            IReadOnlyList<(string BelowPaneName, float DividerFraction)>? dividers,
            float axisHeightFraction,
            double min, double max, bool isLog)
        {
            var band = PaneBandPx(y, canvasHeight, dividers, axisHeightFraction);
            if (band == null) return double.NaN;
            return MapYToPrice(y - band.Value.Top, band.Value.Bottom - band.Value.Top, min, max, isLog);
        }

        /// <summary>
        /// A price mapped to a cursor Y within the whole canvas — the forward companion of
        /// <see cref="MapYToPriceInPane"/>, for the MAIN pane. Anchor-handle hit-testing needs
        /// the two to agree or a handle sits where the drawing is not.
        /// </summary>
        public static double PriceToCanvasY(
            double price,
            double canvasHeight,
            IReadOnlyList<(string BelowPaneName, float DividerFraction)>? dividers,
            float axisHeightFraction,
            double min, double max, bool isLog)
        {
            // The main pane runs from the top of the plot to the first divider.
            double plotBottomFrac = 1.0 - Math.Clamp(axisHeightFraction, 0f, 0.5f);
            double bottomFrac = plotBottomFrac;
            if (dividers != null && dividers.Count > 0)
                bottomFrac = Math.Min(bottomFrac, dividers[0].DividerFraction);

            double top = 0.0;
            double bottom = bottomFrac * canvasHeight;
            if (bottom <= top) return 0;

            double y = top + PriceToScreenY(price, bottom - top, min, max, isLog);

            // The divider pixel belongs to the pane BELOW — PaneBandPx (and ChartHitTester,
            // which has always worked this way) resolve it with `yFrac >= frac`. So a price
            // exactly at the main pane's minimum would otherwise map to a Y that the inverse
            // reads as the top of the VOLUME pane, and the forward and inverse mappings would
            // disagree at exactly the bottom edge. That is one pixel, but it is the pixel a
            // drawing anchored at the low of the range sits on, and with a 10 px grab
            // tolerance a handle attributed to the wrong pane is a handle that cannot be
            // picked up. Stay a hair inside the band the price actually belongs to.
            const double edge = 1e-3;
            return Math.Clamp(y, top, bottom - edge);
        }

        // Deleted 2026-08-24: InverseMapY and GetIndexFromX. Both were public, both had
        // ZERO callers anywhere in the solution (including plugins), and both were second
        // implementations of arithmetic that already exists here — MapYToPrice and
        // MapXToIndex. They also disagreed with the live pair on degenerate input, so the
        // real hazard was not the dead weight but a future caller reaching for the wrong
        // one and getting a different answer on a collapsed range or an empty viewport.
        // Use MapYToPrice / MapXToIndex.
    }
}
