using System.Globalization;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// One plot's reading in a status line: its name (empty when the line's only plot is named by
    /// the line itself), the reading as text, and the colour the plot is drawn in at that reading.
    /// </summary>
    /// <param name="Label">The plot's name, or empty for a one-plot indicator ("RSI 14  55.23").</param>
    /// <param name="Text">The reading, grouped, with the decimals speech gives it.</param>
    /// <param name="Color">The plot's colour AT this reading: a histogram's up or down colour, a
    /// polarity-split line's side, a candle's direction.</param>
    /// <param name="Chip">True when <paramref name="Color"/> is too faint on the status line's
    /// backing to carry text (below WCAG 1.4.3's 4.5:1): the reading is then printed in the
    /// theme's ink and a chip in the plot's colour sits before it.</param>
    public sealed record StatusValue(string Label, string Text, SKColor Color, bool Chip);

    /// <summary>What a status line is.</summary>
    public enum StatusLineKind
    {
        /// <summary>The price pane's first line: the chart's identity and the bar's O H L C.</summary>
        Symbol,
        /// <summary>One indicator: its name, its inputs, each visible plot's reading.</summary>
        Indicator,
        /// <summary>"+N more (see the object tree)": the lines that did not fit, counted.</summary>
        More,
    }

    /// <summary>
    /// One line of a pane's status block — TradingView's model (Cody's 2026-10-17 decision).
    /// </summary>
    /// <param name="Title">The indicator's short name, or the chart's "BTCUSDT · 1d · Bitstamp".</param>
    /// <param name="Inputs">The indicator's numeric inputs ("14", "20 2"), or empty.</param>
    /// <param name="Values">The readings that fit, in declaration order.</param>
    /// <param name="HiddenValues">Readings that did not fit the line's width; drawn as "+N".</param>
    /// <param name="Note">A muted trailer: "4 mark types" on a collapsed marker family, "+2 more"
    /// when a one-line pane counts the indicators it could not list.</param>
    public sealed record StatusLine(
        StatusLineKind Kind, string Title, string Inputs, IReadOnlyList<StatusValue> Values,
        int HiddenValues = 0, string Note = "", string SeriesId = "");

    /// <summary>A pane's status block: the lines it shows, top to bottom.</summary>
    public sealed record PaneStatus(string PaneName, IReadOnlyList<StatusLine> Lines);

    /// <summary>
    /// The chart's STATUS LINES: per pane, at the top left, one line per indicator naming it, its
    /// inputs, and the reading of each visible plot at the bar the user is on, printed in that
    /// plot's colour. The price pane's first line names the chart and gives the bar's open, high,
    /// low and close.
    ///
    /// <para>
    /// <b>Cody's 2026-10-17 decision: TradingView status line.</b> This replaces the boxed legend
    /// (a colour key per component, no values) and the floating hover readout box (date, pointer
    /// price, OHLC). TradingView has neither: the key IS the line with its value, the OHLC sits in
    /// the price pane's status line, and the date and pointer price are labels on the two axes.
    /// His one condition: the status line is a VISUAL mirror. Everything it prints is already
    /// spoken from the chart — the bar's date and OHLC by the arrow keys and the bar detail, each
    /// plot's reading by component navigation, the chart's identity on focus — so a blind user
    /// never has to move focus to read it, and it is aria-hidden like the readout box was.
    /// </para>
    ///
    /// <para>
    /// The model is pure and lives here so both painters use it: ChartArea renders it as real DOM
    /// text over the chart image (magnifiers, browser zoom and user CSS reach DOM text, not canvas
    /// pixels), and the renderer paints the same lines into off-screen snapshots and measures the
    /// price pane's block so formation labels stay out from under it.
    /// </para>
    ///
    /// <para>
    /// The old legend's honesty rules carry over: lines are budgeted against the pane's height
    /// (45% of it, at most nine lines), a pane too short for one line gets none, lines that do not
    /// fit are counted on a last "+N more (see the object tree)" line, readings that do not fit a
    /// line's width are counted as "+N", and a marker family (three or more marker components)
    /// collapses to the marks that fire at this bar plus "N mark types".
    /// </para>
    /// </summary>
    public static class ChartStatusLine
    {
        /// <summary>Line height, CSS px at 100% text size.</summary>
        public const float LinePx = 17f;
        /// <summary>Gap between the pane's edge and the block, and the padding inside a line, CSS px.</summary>
        public const float PadPx = 4f;
        /// <summary>Font size, CSS px at 100% text size (0.75rem).</summary>
        public const float FontPx = 12f;
        /// <summary>The widest a line may grow, as a fraction of the pane's plot width.</summary>
        public const float MaxWidthFraction = 0.7f;

        /// <summary>
        /// The backing's default opacity: 245 of 255, the legend's from 2026-10-16, chosen so a
        /// full-strength line behind the text shows through at about 4% rather than reading as a
        /// strikethrough (Cody's 2026-10-15 screenshots).
        /// </summary>
        public const byte DefaultBackingAlpha = 245;

        /// <summary>
        /// The least a pane's PLOT may keep, in CSS px (× density in device pixels), once its
        /// status band is taken out. A band that would leave less is shrunk a line at a time, down
        /// to no lines at all.
        ///
        /// <para>
        /// <b>20, and the window that decides it is 1000x610</b> with Cipher B, RSI and MACD: the
        /// chart is about 330 CSS px tall there, so <see cref="ChartRenderer.AllocatePaneHeights"/>
        /// takes its crowded path and each oscillator pane gets about 50 px. One line's band is
        /// 25 px, which leaves a 25 px plot. At 30 (the crowded floor for a whole pane) every one of
        /// those panes lost its status line — and the line is the only thing on screen naming the
        /// oscillator in that pane. 25 px still shows an oscillator's shape and its crossings; under
        /// 20 a line is a flat stroke, and an 8-pane chart's ~30 px panes (a 5 px plot) get none.
        /// </para>
        /// </summary>
        public const float MinPlotPx = 20f;

        /// <summary>
        /// The least fraction of a pane its PLOT may keep once its status band is taken out: 40% —
        /// the band never takes more than 60% of a pane. The block's own 45% cap
        /// (<see cref="LineBudget"/>) keeps an ordinary pane well inside this; it bites on the
        /// three-line floor, which in a short pane let the block take most of it (at 80 px, three
        /// lines are 59 of the 80 — now two, 42 of 80, and "+N more").
        /// </summary>
        public const float MinPlotFraction = 0.4f;

        // Fraction of the pane the block may occupy before it competes with the data.
        private const float MaxPaneFraction = 0.45f;
        // Absolute ceiling regardless of how tall the pane is.
        private const int HardMaxLines = 9;
        // Below this a line is worth less than the pixels it costs — unless the pane cannot hold it.
        private const int MinLines = 3;
        // A series with at least this many marker components collapses to its firing marks.
        private const int CollapseMarkersAt = 3;
        // Gap between a line's pieces, in units of the font size.
        private const float GapEm = 0.5f;

        /// <summary>
        /// The colour a status line's backing shows: the theme's sunken surface at
        /// <paramref name="alpha"/> over the chart's background. What a reading's contrast is
        /// measured against.
        /// </summary>
        public static SKColor Backing(ChartTheme theme, byte alpha) => Over(theme.SurfaceSunken.WithAlpha(alpha), theme.Background);

        /// <summary>
        /// The ink a status line's name and inputs are printed in, and a faint reading falls back
        /// to: the theme's axis text, which ThemeContrastChecks holds at 4.5:1 on the default
        /// backing.
        /// </summary>
        public static SKColor Ink(ChartTheme theme) => theme.AxisText;

        /// <summary>
        /// The price pane's series that get a status line: as the old legend
        /// (<see cref="ChartRenderer.MainPaneLegendSeries"/>) — not the candles or the volume
        /// (the symbol line carries the bar), not a heatmap or a volume profile — and never a
        /// drawing, which is not an indicator.
        /// </summary>
        public static List<ChartSeries> MainPaneSeries(IEnumerable<ChartSeries> mainSeries) =>
            ChartRenderer.MainPaneLegendSeries(mainSeries.Where(s => s.IsVisible && !s.IsDrawing));

        /// <summary>
        /// The bar a status line reads: the hovered bar when the pointer moved more recently than
        /// the keyboard cursor, otherwise the keyboard cursor's. Clamped to the bars there are.
        ///
        /// <para>
        /// TradingView has one crosshair; this chart has two, the keyboard cursor (the Skia
        /// crosshair, which speech follows) and the hover hairline. Following the hover alone
        /// would repeat the 2026-10-15 incident: Cody drives the chart from the keyboard with the
        /// mouse parked on it, and a status line following the parked mouse would print a bar he
        /// is not hearing. So the most recent of the two wins.
        /// </para>
        /// </summary>
        public static int ActiveBar(int cursorIndex, int? hoverIndex, bool hoverIsNewer, int barCount)
        {
            if (barCount <= 0) return -1;
            int i = hoverIndex is { } h && hoverIsNewer ? h : cursorIndex;
            return Math.Clamp(i, 0, barCount - 1);
        }

        /// <summary>Everything a status line reads besides the pane's own series.</summary>
        /// <param name="Theme">Colours: the backing, the ink, the candle and volume pair.</param>
        /// <param name="BackingAlpha">The backing's opacity, which the readings' contrast is measured on.</param>
        /// <param name="Bars">The chart's bars (absolute indices), for the symbol line's O H L C.</param>
        /// <param name="Identity">The chart's symbol, timeframe and provider, for the symbol line; null leaves the line off.</param>
        /// <param name="IsHeikinAshi">The candles are drawn Heikin Ashi: the symbol line gives the bar AS DRAWN, as speech does.</param>
        public sealed record Context(
            ChartTheme Theme, byte BackingAlpha, IReadOnlyList<Ohlcv>? Bars, ChartIdentity? Identity, bool IsHeikinAshi = false);

        /// <summary>
        /// A pane's status block at bar <paramref name="absIndex"/> (an index into the chart's
        /// bars, not the viewport).
        /// </summary>
        /// <param name="paneSeries">The pane's visible series, in the user's order. For the price
        /// pane, <see cref="MainPaneSeries"/>.</param>
        /// <param name="paneHeight">The pane's height, in the same units as <paramref name="line"/>.</param>
        /// <param name="maxWidth">The widest a line may be, in the units <paramref name="measure"/> returns.</param>
        /// <param name="line">One line's height.</param>
        /// <param name="measure">The width of a piece of text in the line's font.</param>
        /// <param name="budgetOverride">How many lines the block may have: the pane's
        /// <see cref="StatusBand"/> lines, which the renderer reserved room for. Null counts it here,
        /// with <see cref="PaneLineBudget"/>, taking the pad and the plot floor in the units of
        /// <paramref name="line"/> at 100% text size.</param>
        public static PaneStatus Build(
            string paneName, IReadOnlyList<ChartSeries> paneSeries, int absIndex, Context ctx,
            float paneHeight, float maxWidth, float line, Func<string, float> measure, int? budgetOverride = null)
        {
            var lines = new List<StatusLine>();
            int budget = budgetOverride
                ?? PaneLineBudget(paneHeight, line, line * (PadPx / LinePx), line * (MinPlotPx / LinePx));
            if (budget < 1 || absIndex < 0) return new PaneStatus(paneName, lines);

            float gap = measure("M") * GapEm * 1.6f;
            var backing = Backing(ctx.Theme, ctx.BackingAlpha);

            if (paneName == "Main" && ctx.Identity is { } id && ctx.Bars is { Count: > 0 } chartBars && absIndex < chartBars.Count)
                lines.Add(Fit(SymbolLine(id, chartBars, absIndex, ctx.Theme, backing, ctx.IsHeikinAshi), maxWidth, gap, measure));

            foreach (var s in paneSeries)
            {
                if (s.IsDrawing || !s.IsVisible) continue;
                var l = SeriesLine(s, absIndex, ctx.Theme, backing, bars: ctx.Bars);
                if (l != null) lines.Add(Fit(l, maxWidth, gap, measure));
            }

            if (lines.Count <= budget) return new PaneStatus(paneName, lines);

            // Honesty about truncation, as the legend had it: a block showing a silent subset reads
            // as a complete list of what is on the chart.
            if (budget == 1)
            {
                // One line only: the first, with the rest counted on it, rather than spend the
                // line on "+3 more" and name nothing.
                var first = lines[0] with { Note = Join(lines[0].Note, $"+{lines.Count - 1} more") };
                return new PaneStatus(paneName, new[] { Fit(first, maxWidth, gap, measure) });
            }
            int dropped = lines.Count - (budget - 1);
            var shown = lines.Take(budget - 1).ToList();
            shown.Add(new StatusLine(StatusLineKind.More, $"+{dropped} more (see the object tree)", "", Array.Empty<StatusValue>()));
            return new PaneStatus(paneName, shown);
        }

        /// <summary>A pane's status block placed on the chart element: the pane's top and bottom as fractions of the element's height.</summary>
        public sealed record PlacedPane(PaneStatus Status, double TopFraction, double BottomFraction);

        /// <summary>
        /// Every pane's status block for the DOM painter (ChartArea), in CSS pixels: the panes come
        /// from the renderer's dividers (the same ones the drag handles use), each pane's series
        /// from the workspace by the renderer's own grouping, and the line budget and widths from
        /// the chart element's CSS size and the app's text size — DOM text grows with the text
        /// size setting, where the canvas does not, so the budget has to be counted in it.
        /// </summary>
        /// <param name="textScale">Settings → Appearance → Text size, as a fraction (1 = 100%).</param>
        /// <param name="bands">Each pane's status band as the renderer reserved it
        /// (<see cref="IPaneLayoutService.StatusBands"/>): a pane's block has at most the lines its
        /// band has room for, so the text never reaches the plot below it. A pane missing from it
        /// (no frame has been drawn yet) is counted here by the same rule.</param>
        public static IReadOnlyList<PlacedPane> BuildPanes(
            WorkspaceState state, IReadOnlyList<(string BelowPaneName, float DividerFraction)> dividers,
            float axisHeightFraction, float axisWidthFraction, int absIndex, byte backingAlpha, ChartTheme theme,
            double cssWidth, double cssHeight, double textScale,
            IReadOnlyDictionary<string, PaneStatusBand>? bands = null)
        {
            var placed = new List<PlacedPane>();
            if (state.Data == null || state.Data.Count == 0 || cssWidth <= 0 || cssHeight <= 0) return placed;
            float scale = ClampTextScale(textScale);
            var (line, pad, minPlot) = BandMetrics(1f, scale);
            float plotWidth = (float)(cssWidth * (1 - Math.Clamp(axisWidthFraction, 0f, 0.9f)));
            double plotBottom = 1.0 - Math.Clamp(axisHeightFraction, 0f, 0.5f);

            var ctx = new Context(theme, backingAlpha, state.Data, state.Identity, state.IsHeikinAshi);
            using var font = new SKFont(StatusTypeface, FontPx * scale);
            // DOM text is laid out by the browser's own font, not this one: measure a little wide
            // so a line the model says fits does not overflow its pane in the page.
            float Measure(string t) => font.MeasureText(t) * 1.08f;

            var panes = new List<(string Name, double Top, double Bottom)>();
            double firstDivider = dividers.Count > 0 ? dividers[0].DividerFraction : plotBottom;
            panes.Add(("Main", 0, Math.Min(firstDivider, plotBottom)));
            for (int i = 0; i < dividers.Count; i++)
                panes.Add((dividers[i].BelowPaneName, dividers[i].DividerFraction,
                    i + 1 < dividers.Count ? dividers[i + 1].DividerFraction : plotBottom));

            foreach (var (name, top, bottom) in panes)
            {
                var series = name == "Main"
                    ? MainPaneSeries(state.ActiveSeries.Where(s => s.Pane == "Main"))
                    : state.ActiveSeries.Where(s => s.Pane == name && s.IsVisible && !s.IsDrawing).ToList();
                float h = (float)((bottom - top) * cssHeight);
                int budget = bands != null && bands.TryGetValue(name, out var band)
                    ? band.Lines
                    : PaneLineBudget(h, line, pad, minPlot);
                var status = Build(name, series, absIndex, ctx, h, plotWidth * MaxWidthFraction, line, Measure, budget);
                if (status.Lines.Count > 0) placed.Add(new PlacedPane(status, top, bottom));
            }
            return placed;
        }

        private static readonly SKTypeface StatusTypeface =
            SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;

        /// <summary>The text size setting as the status lines use it: a fraction, clamped to the setting's 50%–250%.</summary>
        public static float ClampTextScale(double textScale) => (float)Math.Clamp(textScale, 0.5, 2.5);

        /// <summary>
        /// A status band's measures, in pixels of a surface at <paramref name="density"/> pixels per
        /// CSS px (1 for the DOM's CSS px, the device pixel ratio for the renderer's bitmap):
        /// one line's height, the pad above the block and below it, and the least the plot keeps.
        ///
        /// <para>
        /// These are the DOM's own measures, so the band the renderer leaves is the block ChartArea
        /// draws: each line is <c>font-size: 0.75rem; line-height: 1.4167</c>, which is
        /// <see cref="LinePx"/> CSS px at 100% text size and grows with the setting (it sets the
        /// root font size); the block is placed <see cref="PadPx"/> CSS px below the pane's top,
        /// a fixed px offset that does NOT grow; and the band keeps the same pad under the block
        /// so a line drawn at the pane's maximum (its stroke straddles the plot's top) does not
        /// touch the text.
        /// </para>
        /// </summary>
        public static (float Line, float Pad, float MinPlot) BandMetrics(float density, float textScale)
            => (LinePx * ClampTextScale(textScale) * density, PadPx * density, MinPlotPx * density);

        /// <summary>
        /// The height of the band a block of <paramref name="lines"/> takes at the top of its pane:
        /// the pad, the lines, the pad again. None for no lines — a pane without a status line
        /// keeps its whole height for the plot.
        /// </summary>
        public static float BandHeight(int lines, float line, float pad) => lines <= 0 ? 0f : pad + lines * line + pad;

        /// <summary>
        /// How many status lines a pane may show once the band they need is taken out of its plot:
        /// <see cref="LineBudget"/>, then fewer — down to none — while the plot would keep less than
        /// <see cref="MinPlotFraction"/> of the pane or less than <paramref name="minPlot"/>. The
        /// lines that do not fit are counted ("+N more") exactly as before; what changed is that a
        /// short pane gives up a line rather than its plot.
        /// </summary>
        public static int PaneLineBudget(float paneHeight, float line, float pad, float minPlot)
        {
            int n = LineBudget(paneHeight, line, pad);
            float floor = Math.Max(minPlot, paneHeight * MinPlotFraction);
            while (n > 0 && paneHeight - BandHeight(n, line, pad) < floor) n--;
            return n;
        }

        /// <summary>
        /// How many lines a pane of <paramref name="paneHeight"/> may show: what fits in 45% of it,
        /// at least three where the pane can physically hold three, never more than nine, and none
        /// when it cannot hold one (at eight panes a pane is about 55 px; a floor of three drew the
        /// old legend out of the bottom of its pane and across the next divider).
        /// </summary>
        public static int LineBudget(float paneHeight, float line, float pad)
        {
            int holds = (int)((paneHeight - pad * 2) / Math.Max(line, 1f));
            if (holds < 1) return 0;
            int fits = (int)((paneHeight * MaxPaneFraction - pad * 2) / Math.Max(line, 1f));
            return Math.Clamp(fits, Math.Min(MinLines, holds), HardMaxLines);
        }

        /// <summary>
        /// The price pane's first line: "BTCUSDT · 1d · Bitstamp", then the bar's O H L C in its
        /// direction colour. The bar AS DRAWN (<see cref="ChartMath.BarAsDrawn"/>): on a Heikin Ashi
        /// chart the HA candle, because that is what the candles' spoken reading gives
        /// (NavigationFeedbackManager reads BarAsDrawn) and what is on screen.
        ///
        /// <para>
        /// <b>No change from the previous close</b>, which TradingView shows here. Cody's condition
        /// for this design is that the status line prints nothing the chart does not already
        /// speak, and no bar readout speaks the change (the SDK candle analyser computes it and
        /// nothing reads it). Shift+F1 speaks the identity, the candles' series reading (Page Up
        /// onto Candles, then Left/Right) speaks the four prices.
        /// </para>
        /// </summary>
        internal static StatusLine SymbolLine(ChartIdentity id, IReadOnlyList<Ohlcv> bars, int i, ChartTheme theme, SKColor backing,
            bool isHeikinAshi = false)
        {
            var bar = ChartMath.BarAsDrawn(bars, i, isHeikinAshi);
            var (bull, bear) = StandardRenderers.ApplyColorVision(theme, theme.CandleBullishBody, theme.CandleBearishBody);
            var dir = bar.Close >= bar.Open ? bull : bear;
            var values = new List<StatusValue>
            {
                Value("O", FormatReading(bar.Open), dir, theme, backing),
                Value("H", FormatReading(bar.High), dir, theme, backing),
                Value("L", FormatReading(bar.Low), dir, theme, backing),
                Value("C", FormatReading(bar.Close), dir, theme, backing),
            };
            string title = string.Join(" · ", new[] { id.Symbol, id.Timeframe, id.Provider }.Where(p => !string.IsNullOrWhiteSpace(p)));
            return new StatusLine(StatusLineKind.Symbol, title, "", values);
        }

        /// <summary>
        /// One indicator's line, or null when it has nothing to show (no visible plot with a
        /// reading kind, and no marker).
        /// </summary>
        internal static StatusLine? SeriesLine(ChartSeries s, int absIndex, ChartTheme theme, SKColor backing,
            IReadOnlyList<Ohlcv>? bars = null)
        {
            var valued = new List<ComponentConfig>();
            var markers = new List<ComponentConfig>();
            foreach (var comp in s.Components)
            {
                if (!comp.IsVisible || !comp.IsEnabled) continue;
                if (ChartRenderer.IsMarker(comp.DisplayType)) markers.Add(comp);
                else if (HasReading(comp.DisplayType)) valued.Add(comp);
            }
            if (valued.Count == 0 && markers.Count == 0) return null;

            bool collapse = markers.Count >= CollapseMarkersAt;
            // A one-plot indicator is named by its line: "RSI 14  55.23", not "RSI 14  RSI 55.23".
            bool labelled = valued.Count + (collapse ? 0 : markers.Count) > 1;
            Ohlcv? bar = bars != null && absIndex < bars.Count ? bars[absIndex] : null;

            var values = new List<StatusValue>();
            foreach (var comp in s.Components)
            {
                bool isValued = valued.Contains(comp), isMarker = markers.Contains(comp);
                if (!isValued && !isMarker) continue;
                var data = s.GetComponentData(comp.Name);
                double v = data != null && absIndex < data.Length ? data[absIndex] : double.NaN;
                string label = comp.DisplayName ?? comp.Name;

                if (isMarker)
                {
                    // A mark is there or it is not: its name is the reading. A mark that does not
                    // fire at this bar is not listed (a collapsed family counts its kinds below).
                    if (double.IsNaN(v)) continue;
                    var mc = SKColor.TryParse(comp.ColorHex, out var parsed) ? parsed.WithAlpha(255) : Ink(theme);
                    values.Add(Value("", label, mc, theme, backing));
                    continue;
                }

                if (double.IsNaN(v))
                {
                    // Warm-up, or a plot with nothing at this bar: kept in its place, so the
                    // readings do not shuffle along the line as the cursor moves.
                    values.Add(new StatusValue(labelled ? label : "", "—", Ink(theme), false));
                    continue;
                }
                if (comp.DisplayType == ComponentDisplayType.ValueLabel)
                {
                    // The number as printed on the candle, in the colour it is printed in.
                    int count = data?.Length ?? 0;
                    values.Add(Value(labelled ? label : "", ValueLabelRenderer.Text(v),
                        ValueLabelRenderer.RawColour(comp, s, absIndex, count), theme, backing));
                    continue;
                }
                var color = ChartRenderer.ReadingColor(new ChartRenderer.CrosshairReading(v, s, comp, absIndex), theme, bar);
                // Volume in the volume pane's own spoken form; everything else in Alt+Shift+D's.
                bool isVolume = comp.Role == ComponentRole.Volume || s.Id.Equals("volume", StringComparison.OrdinalIgnoreCase);
                string text = isVolume ? FormatVolume(v) : FormatReading(v);
                values.Add(Value(labelled ? label : "", text, color, theme, backing));
            }

            var (title, inputs) = TitleAndInputs(s.Name);
            string note = collapse ? $"{markers.Count} mark types" : "";
            return new StatusLine(StatusLineKind.Indicator, title, inputs, values, Note: note, SeriesId: s.Id);
        }

        /// <summary>
        /// Whether a component has a reading of its own to print: lines, oscillators, areas, step
        /// lines, bars, histograms and value labels. Not a level (a constant, drawn as its own line), a cloud,
        /// candle colouring, a heatmap or a profile.
        /// </summary>
        internal static bool HasReading(ComponentDisplayType t) => t is
            ComponentDisplayType.Line or ComponentDisplayType.Oscillator or ComponentDisplayType.Area
            or ComponentDisplayType.Gradient or ComponentDisplayType.StepLine
            or ComponentDisplayType.Bar or ComponentDisplayType.Histogram or ComponentDisplayType.ZeroArea
            // A number printed on the candle: the number is its reading (DBSI-style's "Bulls 15").
            or ComponentDisplayType.ValueLabel;

        /// <summary>
        /// A series name split into the indicator's name and its numeric inputs. A series is named
        /// <c>"{metadata name} {each parameter value}"</c>, so "RSI 14" is RSI with input 14 and
        /// "Value Deviation (support / resistance zones) 240 5" is Value Deviation with 240 5. The
        /// subtitle goes, as the legend's short name dropped it; a string parameter stays in the
        /// name ("Funding Rate BTC-USDT-SWAP"), where it is the point.
        /// </summary>
        public static (string Title, string Inputs) TitleAndInputs(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ("", "");
            string s = name;
            int paren = s.IndexOf(" (", StringComparison.Ordinal);
            string tail = "";
            if (paren > 0)
            {
                int close = s.IndexOf(')', paren);
                tail = close > 0 ? s[(close + 1)..] : "";
                s = s[..paren];
            }
            var parts = (s + " " + tail).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            var inputs = new List<string>();
            while (parts.Count > 1 && double.TryParse(parts[^1], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            {
                inputs.Insert(0, parts[^1]);
                parts.RemoveAt(parts.Count - 1);
            }
            string title = parts.Count > 0 ? string.Join(' ', parts) : name.Trim();
            return (title, string.Join(' ', inputs));
        }

        /// <summary>
        /// A reading as the status line prints it: the decimals speech gives it
        /// (<see cref="SpeechPriceFormatter"/>, about three significant digits, two at least), with
        /// thousands grouped, so "82,366.23" on screen is "82366.23" in the ear.
        /// </summary>
        public static string FormatReading(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "—";
            double abs = Math.Abs(v);
            if (abs == 0) return "0";
            int decimals = Math.Clamp(2 - (int)Math.Floor(Math.Log10(abs)), 2, 10);
            return v.ToString("N" + decimals, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A volume reading as the volume pane speaks it (SpeechFormatter's VolumeBarStrategy):
        /// exact, grouped, whole numbers without a fake ".00" — "12,345" and "12,345.68".
        /// </summary>
        public static string FormatVolume(double vol)
        {
            if (double.IsNaN(vol) || double.IsInfinity(vol)) return "—";
            return vol == Math.Floor(vol)
                ? vol.ToString("N0", CultureInfo.InvariantCulture)
                : vol.ToString("N2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A reading in its plot's colour, or — when that colour is below 4.5:1 on the backing —
        /// in the theme's ink with a chip of the colour beside it. The name is in the line either
        /// way, so colour is never the only thing that says whose reading it is.
        /// </summary>
        internal static StatusValue Value(string label, string text, SKColor color, ChartTheme theme, SKColor backing)
        {
            color = color.WithAlpha(255);
            bool chip = WcagContrast.Ratio(color, backing) < WcagContrast.TextMinimum;
            return new StatusValue(label, text, color, chip);
        }

        /// <summary>
        /// Drops the readings that do not fit <paramref name="maxWidth"/>, from the end, counting
        /// them; a title and inputs that alone overflow lose their inputs first.
        /// </summary>
        internal static StatusLine Fit(StatusLine line, float maxWidth, float gap, Func<string, float> measure)
        {
            if (maxWidth <= 0) return line;
            float chip = measure("M") * 0.8f;
            float w = measure(line.Title);
            string inputs = line.Inputs;
            if (inputs.Length > 0)
            {
                if (w + gap + measure(inputs) > maxWidth) inputs = "";
                else w += gap + measure(inputs);
            }
            if (line.Note.Length > 0) w += gap + measure(line.Note);

            var kept = new List<StatusValue>();
            int hidden = 0;
            for (int i = 0; i < line.Values.Count; i++)
            {
                var v = line.Values[i];
                float vw = gap + (v.Label.Length > 0 ? measure(v.Label) + measure(" ") : 0) + measure(v.Text) + (v.Chip ? chip + measure(" ") : 0);
                // Room for the "+N" that would follow if anything after this one is dropped.
                float reserve = i < line.Values.Count - 1 ? gap + measure($"+{line.Values.Count - i}") : 0;
                if (hidden == 0 && w + vw + reserve <= maxWidth) { kept.Add(v); w += vw; }
                else hidden++;
            }
            // The last reading fits without the reserve when it is the only one left.
            return line with { Inputs = inputs, Values = kept, HiddenValues = line.HiddenValues + hidden };
        }

        private static string Join(string a, string b) => a.Length == 0 ? b : $"{a} {b}";

        private static SKColor Over(SKColor fg, SKColor bg)
        {
            double a = fg.Alpha / 255.0;
            return new SKColor(
                (byte)Math.Round(fg.Red * a + bg.Red * (1 - a)),
                (byte)Math.Round(fg.Green * a + bg.Green * (1 - a)),
                (byte)Math.Round(fg.Blue * a + bg.Blue * (1 - a)));
        }

        /// <summary>A colour as CSS: "#RRGGBB".</summary>
        public static string Css(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";

        /// <summary>The backing as CSS: rgba of the sunken surface at the alpha.</summary>
        public static string CssBacking(ChartTheme theme, byte alpha) =>
            $"rgba({theme.SurfaceSunken.Red},{theme.SurfaceSunken.Green},{theme.SurfaceSunken.Blue},{(alpha / 255.0).ToString("0.###", CultureInfo.InvariantCulture)})";
    }
}
