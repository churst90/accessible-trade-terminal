using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;

namespace AccessibleTrader.Core.Services.Rendering
{
    public static class StandardRenderers
    {
        // ── Visual accessibility overrides (Phase D) ─────────────────────────

        /// <summary>
        /// Deuteranopia/protanopia-safe direction pair used when
        /// <see cref="ChartTheme.ColorVisionSafe"/> is on: up = blue, down =
        /// orange — the standard colorblind-friendly trading convention.
        /// Public so tests (and docs) can pin the exact values.
        /// </summary>
        public static readonly SKColor ColorVisionUp = new(64, 156, 255);
        public static readonly SKColor ColorVisionDown = new(255, 160, 32);

        /// <summary>
        /// When the color-vision override is on, replaces a resolved up/down
        /// pair with the safe pair. An override mode by design (like OS
        /// high-contrast): it takes precedence over per-component colors so
        /// the toggle is one switch, not a per-indicator recoloring chore.
        /// </summary>
        internal static (SKColor up, SKColor down) ApplyColorVision(ChartTheme theme, SKColor up, SKColor down)
            => theme.ColorVisionSafe ? (ColorVisionUp, ColorVisionDown) : (up, down);

        // ── Per-bar color helpers ─────────────────────────────────────────────

        /// <summary>
        /// Resolves the per-bar color for a component at the given data index.
        /// Returns <c>null</c> when the component has no ColorRules (use static paint).
        /// </summary>
        // Internal (not private): direct color-rule tests via InternalsVisibleTo.
        internal static SKColor? ResolveBarColor(ComponentConfig comp, double[] data, int dataIdx)
        {
            if (comp.ColorRules == null || comp.ColorRules.Count == 0) return null;
            if (dataIdx >= data.Length || double.IsNaN(data[dataIdx])) return null;

            double val = data[dataIdx];
            double prev = (dataIdx > 0 && !double.IsNaN(data[dataIdx - 1])) ? data[dataIdx - 1] : val;

            foreach (var rule in comp.ColorRules)
            {
                bool match = rule.Condition switch
                {
                    ColorCondition.AboveZero  => val >= 0,
                    ColorCondition.BelowZero  => val < 0,
                    ColorCondition.Rising     => val > prev,
                    ColorCondition.Falling    => val < prev,
                    ColorCondition.AboveLevel => val > rule.Level,
                    ColorCondition.BelowLevel => val <= rule.Level,
                    _                         => false,
                };
                if (match && SKColor.TryParse(rule.ColorHex, out var c)) return c;
            }
            return null;
        }

        // ── Sentiment phase colors for CandleColor overlay (Cipher S) ──────────
        // Index 0 = Max Fear … 10 = Max Euphoria.  Matches the 11-phase legend in
        // the Market Cycle Highs & Lows design: fear → cool hues, greed → warm hues.
        private static readonly SKColor[] PhaseColors =
        {
            SKColor.Parse("#00E676"),  // 0  Max Fear       — bright green (buy signal)
            SKColor.Parse("#00E5FF"),  // 1  Fear           — cyan
            SKColor.Parse("#00BCD4"),  // 2  Concern        — teal
            SKColor.Parse("#2196F3"),  // 3  Caution        — blue
            SKColor.Parse("#9C27B0"),  // 4  Mild Caution   — purple
            SKColor.Parse("#FFF176"),  // 5  Neutral        — pale yellow
            SKColor.Parse("#FFD54F"),  // 6  Mild Greed     — yellow
            SKColor.Parse("#FF9800"),  // 7  Greed          — orange
            SKColor.Parse("#FF5722"),  // 8  High Greed     — orange-red
            SKColor.Parse("#F44336"),  // 9  Extreme Greed  — red
            SKColor.Parse("#E91E63"),  // 10 Max Euphoria   — hot pink
        };

        /// <summary>Returns the SKColor for a sentiment phase index (0–10). Clamps out-of-range values.</summary>
        public static SKColor GetPhaseColor(int phaseIndex)
            => PhaseColors[Math.Clamp(phaseIndex, 0, PhaseColors.Length - 1)];

        // Phase 4 (2026-04-09): the degenerate-OHLCV detection hack
        // (IsDegenerateOhlcv + RenderCandlesAsLine) was removed after analytics
        // providers stopped masquerading as OHLCV. Single-value analytics providers
        // now seed a real Price indicator (CoreSeriesIds.Price) instead of faking a
        // Candles series with O=H=L=C, so there's no degenerate input reaching this
        // file. If you're digging for that fallback in git history, search for
        // "IsDegenerateOhlcv" before commit d5e2f325's descendants.

        public static void RenderCandles(RenderContext ctx, ChartSeries series, SKPaint paint, double[]? phaseData = null)
        {
            var data = ctx.Data;
            if (data == null || !data.Any()) return;

            float barWidth = ctx.Width / ctx.ViewportLength;
            float halfBar = barWidth / 2.0f;

            // THE THEME OWNS CANDLE COLOUR, unless the user has picked one by hand.
            //
            // This used to read the component's ColorHex unconditionally, which comes from
            // indicator metadata — a hardcoded TradingView teal and salmon. The consequence was
            // that ChartTheme.CandleBullishBody was dead code for the main chart: a theme could
            // repaint the background, the grid, the axes and the whole application chrome, and
            // the candles stayed teal. Which is to say the one element people actually look at
            // was the one element the theme could not touch.
            var bodyComp = series.Components.FirstOrDefault(c => c.DisplayType == ComponentDisplayType.Candle);
            SKColor bullish = ctx.Theme.CandleBullishBody;
            SKColor bearish = ctx.Theme.CandleBearishBody;
            if (bodyComp is { IsUserStyled: true })
            {
                SKColor.TryParse(bodyComp.ColorHex, out bullish);
                SKColor.TryParse(bodyComp.ColorHexSecondary, out bearish);
            }
            (bullish, bearish) = ApplyColorVision(ctx.Theme, bullish, bearish);
            bool hollowUp = ctx.Theme.HollowUpCandles;

            // Wick color: read from the wick components' own ColorHex so users can style
            // wicks independently from the candle body via the Properties modal.
            var wickComp = series.Components.FirstOrDefault(c => c.DisplayType == ComponentDisplayType.Wick);
            SKColor wickColor = ctx.Theme.CandleBullishWick;
            if (wickComp is { IsUserStyled: true } && !string.IsNullOrEmpty(wickComp.ColorHex))
                SKColor.TryParse(wickComp.ColorHex, out wickColor);
            float wickThickness = wickComp?.Thickness ?? 1f;

            bool hasPhaseOverride = phaseData != null && phaseData.Length > 0;

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                if (i >= data.Count) break;

                var bar = data[i];
                // Pixel-align the bar center to a half-pixel so the 1-px-wide
                // wick stroke and the body rect share a common visual axis.
                // Without this, float coordinates fell on arbitrary sub-pixel
                // positions and Skia's anti-aliasing produced asymmetric
                // wicks (offset by fractions of a pixel from the body center)
                // on thin-body doji-style candles at typical zoom levels.
                float xRaw = (i * barWidth) + halfBar;
                float x = (float)Math.Floor(xRaw) + 0.5f;

                float yOpen = ChartMath.MapY((double)bar.Open, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                float yHigh = ChartMath.MapY((double)bar.High, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                float yLow = ChartMath.MapY((double)bar.Low, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                float yClose = ChartMath.MapY((double)bar.Close, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);

                // Determine body color — phase override suppresses default bullish/bearish coloring.
                SKColor bodyColor;
                if (hasPhaseOverride)
                {
                    int absIdx = ctx.ViewportStart + i;
                    if (absIdx < phaseData!.Length && !double.IsNaN(phaseData[absIdx]))
                        bodyColor = GetPhaseColor((int)Math.Round(phaseData[absIdx]));
                    else
                        bodyColor = bar.Close >= bar.Open ? bullish : bearish;
                }
                else
                {
                    bodyColor = bar.Close >= bar.Open ? bullish : bearish;
                }

                // Wick — hidden when a phase override is active (wicks clutter the color view;
                // OHLC range is conveyed via sonification and speech).
                if (!hasPhaseOverride)
                {
                    using var wickLease = SKPaintPool.Rent();
                    var wickPaint = wickLease.Paint;
                    wickPaint.Color = wickColor;
                    wickPaint.Style = SKPaintStyle.Stroke;
                    wickPaint.StrokeWidth = wickThickness;
                    ctx.Canvas.DrawLine(x, yHigh, x, yLow, wickPaint);
                }

                // Body
                float top = Math.Min(yOpen, yClose);
                float bottom = Math.Max(yOpen, yClose);
                float bodyHeight = Math.Max(1, bottom - top);

                using var bodyLease = SKPaintPool.Rent();
                var bodyPaint = bodyLease.Paint;
                bodyPaint.Color = bodyColor;
                // Hollow-up-candles accessibility mode: up-bodies are outlined, down
                // filled — direction readable by shape alone, no color perception
                // needed. Phase-colored candles stay filled (phase IS the message).
                bool drawHollow = hollowUp && !hasPhaseOverride && bar.Close >= bar.Open;
                bodyPaint.Style = drawHollow ? SKPaintStyle.Stroke : SKPaintStyle.Fill;
                if (drawHollow) bodyPaint.StrokeWidth = 1.5f;
                // Pixel-align body edges so the rect is symmetric around the
                // same center as the wick stroke. Without flooring, the body's
                // left edge and width both drifted on sub-pixel boundaries and
                // the visual center no longer matched the wick's x.
                float bodyHalfWidth = (float)Math.Floor(barWidth * 0.4f);
                float bodyLeft = x - bodyHalfWidth;
                float bodyWidthPx = bodyHalfWidth * 2f;
                ctx.Canvas.DrawRect(bodyLeft, top, bodyWidthPx, bodyHeight, bodyPaint);
            }
        }

        public static void RenderLine(RenderContext ctx, ChartSeries series, ComponentConfig comp, SKPaint paint)
        {
            var lineData = series.GetComponentData(comp.Name);
            if (lineData == null || lineData.Length == 0) return;

            float barWidth = ctx.Width / ctx.ViewportLength;
            float halfBar = barWidth / 2.0f;
            bool hasColorRules = comp.ColorRules != null && comp.ColorRules.Count > 0;
            bool isArea = comp.DisplayType == ComponentDisplayType.Area || comp.DisplayType == ComponentDisplayType.Gradient;

            // When ColorRules are present, draw each segment individually with the resolved color.
            if (hasColorRules)
            {
                float? prevX = null, prevY = null;
                for (int i = 0; i < ctx.ViewportLength; i++)
                {
                    int dataIdx = ctx.ViewportStart + i;
                    if (dataIdx >= lineData.Length) break;
                    double val = lineData[dataIdx];
                    if (double.IsNaN(val)) { prevX = null; prevY = null; continue; }

                    float x = (i * barWidth) + halfBar;
                    float y = ResolveMarkerY(ctx, comp, i, val);

                    if (prevX.HasValue)
                    {
                        var col = ResolveBarColor(comp, lineData, dataIdx) ?? paint.Color;
                        using var segLease = SKPaintPool.Rent();
                        var segPaint = segLease.Paint;
                        segPaint.Color = col;
                        segPaint.StrokeWidth = paint.StrokeWidth;
                        segPaint.Style = SKPaintStyle.Stroke;
                        segPaint.IsAntialias = true;
                        ctx.Canvas.DrawLine(prevX.Value, prevY!.Value, x, y, segPaint);
                    }
                    prevX = x; prevY = y;
                }
                return;
            }

            using var path = new SKPath();
            bool first = true;
            float lastY = 0;

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= lineData.Length) break;

                double val = lineData[dataIdx];
                if (double.IsNaN(val)) { first = true; continue; }

                float x = (i * barWidth) + halfBar;
                float y = ResolveMarkerY(ctx, comp, i, val);
                lastY = y;

                if (first) { path.MoveTo(x, y); first = false; }
                else path.LineTo(x, y);
            }

            if (isArea && !first)
            {
                // Close the fill shape down to the pane bottom.
                float yBase = ChartMath.MapY(0, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                yBase = Math.Clamp(yBase, ctx.Top, ctx.Bottom);
                float endX = ((ctx.ViewportLength - 1) * barWidth) + halfBar;
                float startX = halfBar;
                path.LineTo(endX, yBase);
                path.LineTo(startX, yBase);
                path.Close();

                SKColor lineColor = paint.Color;
                using var fillPaint = new SKPaint { Color = lineColor.WithAlpha(60), Style = SKPaintStyle.Fill };
                ctx.Canvas.DrawPath(path, fillPaint);

                // Re-draw the line on top of the fill
                using var linePaint = new SKPaint { Color = lineColor, StrokeWidth = paint.StrokeWidth, Style = SKPaintStyle.Stroke, IsAntialias = true };
                using var linePath = new SKPath();
                first = true;
                for (int i = 0; i < ctx.ViewportLength; i++)
                {
                    int dataIdx = ctx.ViewportStart + i;
                    if (dataIdx >= lineData.Length) break;
                    double val = lineData[dataIdx];
                    if (double.IsNaN(val)) { first = true; continue; }
                    float x = (i * barWidth) + halfBar;
                    float y = ResolveMarkerY(ctx, comp, i, val);
                    if (first) { linePath.MoveTo(x, y); first = false; } else linePath.LineTo(x, y);
                }
                ctx.Canvas.DrawPath(linePath, linePaint);
            }
            else
            {
                ctx.Canvas.DrawPath(path, paint);
            }
        }

        // Deleted 2026-08-24: RenderBars. Zero callers — DataLayer routes BOTH Bar and
        // Histogram to RenderDirectionalBars below, which is the one that colours by
        // direction and clamps its marker extents. RenderBars drew flat single-colour bars
        // and had none of that, so reviving it would have been a silent visual regression.

        /// <summary>
        /// Renders any bar or histogram component with directional coloring (green = up/positive,
        /// red = down/negative). Applies the same principle as candle bodies — the color conveys
        /// direction, not a fixed style.
        ///
        /// Direction source is determined by <see cref="ComponentConfig.ColorSource"/>:
        ///   <see cref="ColorSource.PriceAction"/> — green when the OHLCV candle closed up (Close &gt;= Open).
        ///                                            Used for volume and any bar that tracks candle direction.
        ///   <see cref="ColorSource.Value"/>        — green when the bar value &gt;= 0, red when negative.
        ///                                            Used for MACD histogram and any oscillator-style bar.
        /// </summary>
        public static void RenderDirectionalBars(RenderContext ctx, ChartSeries series, ComponentConfig comp)
        {
            var barData = series.GetComponentData(comp.Name);
            if (barData == null || barData.Length == 0) return;

            float barWidth = ctx.Width / ctx.ViewportLength;
            float spacing  = barWidth * 0.1f;

            // VOLUME takes the theme's candle colours, so the two panes agree about what "up"
            // looks like. Other directional bars — a MACD histogram, a money-flow bar — keep
            // their own palette: they are not price direction and colouring them like candles
            // would say they were.
            bool followsPrice = comp.Role is ComponentRole.Volume or ComponentRole.PriceAction;

            var upColor   = followsPrice ? ctx.Theme.CandleBullishBody : SKColors.Green;
            var downColor = followsPrice ? ctx.Theme.CandleBearishBody : new SKColor(204, 0, 0);
            if (!followsPrice || comp.IsUserStyled)
            {
                if (!string.IsNullOrEmpty(comp.ColorHex) && SKColor.TryParse(comp.ColorHex, out var parsedUp))
                    upColor = parsedUp;
                if (!string.IsNullOrEmpty(comp.ColorHexSecondary) && SKColor.TryParse(comp.ColorHexSecondary, out var parsedDown))
                    downColor = parsedDown;
            }
            (upColor, downColor) = ApplyColorVision(ctx.Theme, upColor, downColor);
            using var upPaint = SKPaintPool.Rent();
            upPaint.Paint.Color = upColor.WithAlpha(180);
            upPaint.Paint.Style = SKPaintStyle.Fill;
            using var downPaint = SKPaintPool.Rent();
            downPaint.Paint.Color = downColor.WithAlpha(180);
            downPaint.Paint.Style = SKPaintStyle.Fill;

            bool useCandleDirection = comp.ColorSource == ColorSource.PriceAction;
            bool hasColorRules = comp.ColorRules != null && comp.ColorRules.Count > 0;

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= barData.Length) break;

                double val = barData[dataIdx];
                if (double.IsNaN(val)) continue;

                float x      = i * barWidth;
                float yVal   = ChartMath.MapY(val, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                // Use ReferenceLevel as bar baseline so offset components (e.g. Cipher B MF Wave
                // anchored at −80) draw compact bars relative to their reference, not the WT zero line.
                double barBase = comp.ReferenceLevel ?? 0.0;
                float yZero  = ChartMath.MapY(barBase, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);

                SKPaint barPaint;
                RentedPaint ruleLease = default;
                bool ruleRented = false;
                if (hasColorRules)
                {
                    var col = ResolveBarColor(comp, barData, dataIdx);
                    if (col.HasValue)
                    {
                        ruleLease = SKPaintPool.Rent();
                        ruleRented = true;
                        barPaint = ruleLease.Paint;
                        barPaint.Color = col.Value;
                        barPaint.Style = SKPaintStyle.Fill;
                    }
                    else
                    {
                        // ctx.Data is the viewport slice (length = ViewportLength), so index with i, not dataIdx.
                        bool isUp = useCandleDirection
                            ? (i < ctx.Data.Count && ctx.Data[i].Close >= ctx.Data[i].Open)
                            : val >= comp.ColorBaseline;
                        barPaint = isUp ? upPaint.Paint : downPaint.Paint;
                    }
                }
                else
                {
                    bool isUp = useCandleDirection
                        ? (i < ctx.Data.Count && ctx.Data[i].Close >= ctx.Data[i].Open)
                        : val >= comp.ColorBaseline;
                    barPaint = isUp ? upPaint.Paint : downPaint.Paint;
                }

                ctx.Canvas.DrawRect(x + spacing, Math.Min(yVal, yZero), barWidth - (2 * spacing), Math.Abs(yVal - yZero), barPaint);

                if (ruleRented) ruleLease.Dispose();
            }
        }

        // ── New display type renderers (Phase 10-A) ───────────────────────────

        /// <summary>
        /// Renders a continuous wave line with zero-crossing area fills: green semi-transparent fill
        /// above zero, red below zero. Each zero-crossing starts a new fill segment.
        /// Used for the Money Flow Wave component in Cipher B.
        /// </summary>
        public static void RenderZeroArea(RenderContext ctx, ChartSeries series, ComponentConfig comp)
        {
            var data = series.GetComponentData(comp.Name);
            if (data == null || data.Length == 0) return;

            float barWidth  = ctx.Width / ctx.ViewportLength;
            float halfBar   = barWidth / 2.0f;
            // Use ReferenceLevel as the fill baseline (default 0). Cipher B MF Wave sets -80
            // so the fill anchors to the bottom of the WT pane rather than the WT zero line.
            double refLevel = comp.ReferenceLevel ?? 0.0;
            float yZero     = ChartMath.MapY(refLevel, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
            yZero = Math.Clamp(yZero, ctx.Top, ctx.Bottom);

            SKColor.TryParse(comp.ColorHex,          out var posColor);
            SKColor.TryParse(comp.ColorHexSecondary, out var negColor);

            using var posFill = new SKPaint { Color = posColor.WithAlpha(120), Style = SKPaintStyle.Fill };
            using var negFill = new SKPaint { Color = negColor.WithAlpha(120), Style = SKPaintStyle.Fill };
            using var linePaint = new SKPaint
            {
                Color = posColor.WithAlpha(200), Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1f, comp.Thickness) * ctx.Density, IsAntialias = true
            };

            // Build runs of same-sign segments, flush each as a filled polygon.
            int runStart = -1;
            bool runPositive = false;

            void FlushRun(int from, int to, bool positive)
            {
                using var path = new SKPath();
                bool first = true;
                for (int i = from; i <= to; i++)
                {
                    int di = ctx.ViewportStart + i;
                    if (di >= data.Length) break;
                    float x = (i * barWidth) + halfBar;
                    float y = ChartMath.MapY(data[di], ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                    if (first) { path.MoveTo(x, yZero); path.LineTo(x, y); first = false; }
                    else path.LineTo(x, y);
                }
                // Close back to zero
                float endX = (to * barWidth) + halfBar;
                float startX = (from * barWidth) + halfBar;
                path.LineTo(endX, yZero);
                path.LineTo(startX, yZero);
                path.Close();
                ctx.Canvas.DrawPath(path, positive ? posFill : negFill);
            }

            // Draw fills first, then line on top.
            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= data.Length) break;
                double val = data[dataIdx];
                if (double.IsNaN(val))
                {
                    if (runStart >= 0) { FlushRun(runStart, i - 1, runPositive); runStart = -1; }
                    continue;
                }
                bool positive = val >= refLevel;
                if (runStart < 0) { runStart = i; runPositive = positive; }
                else if (positive != runPositive)
                {
                    FlushRun(runStart, i - 1, runPositive);
                    runStart = i; runPositive = positive;
                }
            }
            if (runStart >= 0) FlushRun(runStart, ctx.ViewportLength - 1, runPositive);

            // Draw the wave line on top of fills
            using var linePath = new SKPath();
            bool lineFirst = true;
            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= data.Length) break;
                double val = data[dataIdx];
                if (double.IsNaN(val)) { lineFirst = true; continue; }
                float x = (i * barWidth) + halfBar;
                float y = ResolveMarkerY(ctx, comp, i, val);
                if (lineFirst) { linePath.MoveTo(x, y); lineFirst = false; } else linePath.LineTo(x, y);
            }
            ctx.Canvas.DrawPath(linePath, linePaint);
        }

        /// <summary>
        /// Renders each non-NaN bar value as a small filled circle anchored at the zero-line Y position.
        /// Color: green-teal when value &gt;= 0, red when value &lt; 0.
        /// Used for Money Flow in Cipher B — keeps the MFI compact at zero without competing with the WT waves.
        /// </summary>
        public static void RenderZeroDot(RenderContext ctx, ChartSeries series, ComponentConfig comp)
        {
            var data = series.GetComponentData(comp.Name);
            if (data == null || data.Length == 0) return;

            float barWidth = ctx.Width / ctx.ViewportLength;
            float halfBar  = barWidth / 2.0f;
            float radius   = ClampMarkerHalfExtent(Math.Max(comp.Thickness, 2f) * ctx.Density, ctx);
            float yZero    = ChartMath.MapY(0, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
            yZero = Math.Clamp(yZero, ctx.Top, ctx.Bottom);

            using var posPaint = new SKPaint { Color = new SKColor(38, 198, 130, 210),  Style = SKPaintStyle.Fill, IsAntialias = true };
            using var negPaint = new SKPaint { Color = new SKColor(255,  82,  82, 210), Style = SKPaintStyle.Fill, IsAntialias = true };

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= data.Length) break;
                double val = data[dataIdx];
                if (double.IsNaN(val)) continue;

                float x = (i * barWidth) + halfBar;
                ctx.Canvas.DrawCircle(x, yZero, radius, val >= 0 ? posPaint : negPaint);
            }
        }

        /// <summary>
        /// Renders each bar value as a small filled dot/circle. Size = comp.Thickness * 2 (diameter).
        /// If a companion array named ComponentName + "_color" exists in the series data, its values
        /// are used to interpolate a teal ↔ gray ↔ red gradient color instead of the component's
        /// fixed color. This supports the WT Momentum ribbon (Cipher A) without needing a separate
        /// display type — the component stays a plain Dot for accessibility and sonification purposes.
        /// </summary>
        public static void RenderDot(RenderContext ctx, ChartSeries series, ComponentConfig comp, SKPaint paint)
        {
            var data      = series.GetComponentData(comp.Name);
            if (data == null || data.Length == 0) return;

            var colorData = series.GetComponentData(comp.Name + "_color"); // optional gradient source
            bool hasGradient = colorData != null && colorData.Length > 0;

            // Optional companion arrays "{comp.Name}_anchorIdx" +
            // "{comp.Name}_anchorY" = previous-pivot bar index and the same-offset
            // Y value. When both present and non-NaN at a fired bar, the renderer
            // draws a slanted line from that prior pivot to the current dot.
            // Used by Cipher B's Bullish / Bearish / Hidden Continuation
            // components to show divergence geometry, not just the second-pivot
            // marker.
            var anchorIdxData = series.GetComponentData(comp.Name + "_anchorIdx");
            var anchorYData   = series.GetComponentData(comp.Name + "_anchorY");
            bool hasAnchors = anchorIdxData != null && anchorYData != null
                && anchorIdxData.Length == data.Length && anchorYData.Length == data.Length;

            // Gradient anchor colours matching real Market Cipher A palette:
            //   teal (#00FF9F) = oversold/bearish WT (deeply negative)
            //   gray (#90A4AE) = neutral midpoint (WT near zero)
            //   red  (#FF3D00) = overbought/bullish WT (deeply positive)
            // gradAlpha = 230 (~90%) so dots are clearly visible against the chart background.
            var teal = new SKColor(0,   255, 159);  // #00FF9F oversold
            var gray = new SKColor(144, 164, 174);  // #90A4AE neutral
            var red  = new SKColor(255,  61,   0);  // #FF3D00 overbought
            const byte gradAlpha = 230;

            float barWidth = ctx.Width / ctx.ViewportLength;
            float halfBar  = barWidth / 2.0f;
            float radius   = ClampMarkerHalfExtent(Math.Max(comp.Thickness, 2f) * ctx.Density, ctx);

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= data.Length) break;
                double val = data[dataIdx];
                if (double.IsNaN(val)) continue;

                float x = (i * barWidth) + halfBar;
                float y = ResolveMarkerY(ctx, comp, i, val);

                SKColor col;
                if (hasGradient && dataIdx < colorData!.Length && !double.IsNaN(colorData[dataIdx]))
                {
                    double wt  = Math.Clamp(colorData[dataIdx], -100.0, 100.0);
                    // Power curve (exponent 0.6) saturates color faster at moderate WT values
                    // so common readings of ±30–50 render vividly rather than near-gray.
                    float  t   = (float)Math.Pow(Math.Abs(wt) / 100.0, 0.6);
                    SKColor to = wt < 0 ? teal : red;
                    col = new SKColor(
                        (byte)(gray.Red   + (to.Red   - gray.Red)   * t),
                        (byte)(gray.Green + (to.Green - gray.Green) * t),
                        (byte)(gray.Blue  + (to.Blue  - gray.Blue)  * t),
                        gradAlpha);
                }
                else
                {
                    col = ResolveBarColor(comp, data, dataIdx) ?? paint.Color;
                }

                // Slanted pivot-to-pivot line. Resolves the prior pivot's screen
                // X/Y from the companion arrays' stored bar index + Y value. Skip
                // when the prior pivot is off the left edge of the current viewport
                // (line would stub off screen with no terminator visible).
                if (hasAnchors)
                {
                    double anchorIdxD = anchorIdxData![dataIdx];
                    double anchorVal  = anchorYData![dataIdx];
                    if (!double.IsNaN(anchorIdxD) && !double.IsNaN(anchorVal))
                    {
                        int anchorIdx = (int)anchorIdxD;
                        if (anchorIdx >= ctx.ViewportStart && anchorIdx < dataIdx)
                        {
                            int relAnchor = anchorIdx - ctx.ViewportStart;
                            float ax = (relAnchor * barWidth) + halfBar;
                            float ay = ChartMath.MapY(anchorVal, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);

                            using var lineLease = SKPaintPool.Rent();
                            var linePaint = lineLease.Paint;
                            linePaint.Color = col.WithAlpha(180);
                            linePaint.Style = SKPaintStyle.Stroke;
                            linePaint.StrokeWidth = Math.Max(1f, comp.Thickness) * ctx.Density;
                            linePaint.IsAntialias = true;
                            ctx.Canvas.DrawLine(ax, ay, x, y, linePaint);
                        }
                    }
                }

                using var dotLease = SKPaintPool.Rent();
                var dotPaint = dotLease.Paint;
                dotPaint.Color = col;
                dotPaint.Style = SKPaintStyle.Fill;
                dotPaint.IsAntialias = true;
                ctx.Canvas.DrawCircle(x, y, radius, dotPaint);
            }
        }

        /// <summary>
        /// Renders each bar value as an up or down triangle arrow. Positive = up arrow, negative/NaN-relative = down arrow.
        /// The arrow is anchored at the value Y and points away from the zero line.
        /// </summary>
        public static void RenderArrow(RenderContext ctx, ChartSeries series, ComponentConfig comp, SKPaint paint)
        {
            var data = series.GetComponentData(comp.Name);
            if (data == null || data.Length == 0) return;

            float barWidth = ctx.Width / ctx.ViewportLength;
            float halfBar = barWidth / 2.0f;
            float arrowSize = ClampMarkerExtent(Math.Max(comp.Thickness * 3f, 6f) * ctx.Density, ctx);

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= data.Length) break;
                double val = data[dataIdx];
                if (double.IsNaN(val)) continue;

                float x = (i * barWidth) + halfBar;
                float y = ResolveMarkerY(ctx, comp, i, val);
                bool isUp = val >= 0;

                using var path = new SKPath();
                if (isUp)
                {
                    path.MoveTo(x, y - arrowSize);
                    path.LineTo(x - arrowSize / 2f, y);
                    path.LineTo(x + arrowSize / 2f, y);
                }
                else
                {
                    path.MoveTo(x, y + arrowSize);
                    path.LineTo(x - arrowSize / 2f, y);
                    path.LineTo(x + arrowSize / 2f, y);
                }
                path.Close();

                var col = ResolveBarColor(comp, data, dataIdx) ?? paint.Color;
                using var arrowLease = SKPaintPool.Rent();
                var arrowPaint = arrowLease.Paint;
                arrowPaint.Color = col;
                arrowPaint.Style = SKPaintStyle.Fill;
                arrowPaint.IsAntialias = true;
                ctx.Canvas.DrawPath(path, arrowPaint);
            }
        }

        /// <summary>
        /// Renders a step line: horizontal segment at the current value, then a vertical drop to the
        /// next value (staircase pattern). Used by ADX-style step indicators.
        /// </summary>
        public static void RenderStepLine(RenderContext ctx, ChartSeries series, ComponentConfig comp, SKPaint paint)
        {
            var data = series.GetComponentData(comp.Name);
            if (data == null || data.Length == 0) return;

            float barWidth = ctx.Width / ctx.ViewportLength;
            float halfBar = barWidth / 2.0f;

            using var path = new SKPath();
            bool first = true;
            float prevX = 0, prevY = 0;

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= data.Length) break;
                double val = data[dataIdx];
                if (double.IsNaN(val)) { first = true; continue; }

                float x = (i * barWidth) + halfBar;
                float y = ResolveMarkerY(ctx, comp, i, val);

                if (first) { path.MoveTo(x, y); first = false; }
                else
                {
                    // Horizontal to x at previous y, then vertical to new y
                    path.LineTo(x, prevY);
                    path.LineTo(x, y);
                }
                prevX = x; prevY = y;
            }
            ctx.Canvas.DrawPath(path, paint);
        }

        /// <summary>
        /// Caps a marker's drawn size against the width of a bar, in FULL extent — the total
        /// width the glyph occupies, corner to corner.
        ///
        /// <para>
        /// Thickness is authored for a normal zoom. Zoom out to a few hundred bars and the bars
        /// shrink while the glyphs do not, so on a 330-bar weekly the swing squares came out about
        /// four times a candle wide and buried the price action they were describing.
        /// </para>
        ///
        /// <para>
        /// FULL extent matters because the renderers disagree about what their size variable
        /// means: a triangle's <c>arrowSize</c> is the whole height, while a square's
        /// <c>half</c>, a diamond's <c>half</c>, a cross's <c>arm</c> and a dot's <c>radius</c>
        /// are all half of it. Clamping those five against the same ceiling as the triangles made
        /// them exactly twice as large — which is why the squares and crosses still looked heavy
        /// after the first attempt at this. Half-extent callers use
        /// <see cref="ClampMarkerHalfExtent"/> so the conversion happens in one place.
        /// </para>
        ///
        /// <para>
        /// The configured thickness stays an upper bound — this only ever makes a marker smaller —
        /// and a floor keeps it from vanishing at extreme zoom, where a mark you cannot see is
        /// worse than no mark, because the chart still claims to be showing you something.
        /// </para>
        /// </summary>
        internal static float ClampMarkerExtent(float requestedFullExtent, RenderContext ctx)
        {
            float barWidth = ctx.Width / Math.Max(1, ctx.ViewportLength);
            float floor    = 6f * ctx.Density;
            float ceiling  = Math.Max(floor, barWidth * 1.8f);
            return Math.Clamp(requestedFullExtent, floor, ceiling);
        }

        /// <summary>
        /// <see cref="ClampMarkerExtent"/> for renderers whose size variable is a radius, an arm,
        /// or a half-side rather than the whole glyph.
        /// </summary>
        internal static float ClampMarkerHalfExtent(float requestedHalfExtent, RenderContext ctx) =>
            ClampMarkerExtent(requestedHalfExtent * 2f, ctx) / 2f;

        /// <summary>
        /// Y position for a marker at visible index <paramref name="i"/>.
        ///
        /// <para>
        /// With <see cref="MarkerAnchor.Value"/> the component's own value is mapped through the
        /// price axis, which is right for anything whose value IS a price. With BelowBar/AboveBar
        /// the marker is pinned to the DISPLAYED bar instead. That distinction matters because
        /// indicators compute on raw OHLCV while the main pane may be drawing Heikin-Ashi candles
        /// with different highs and lows — a price-anchored marker then floats away from the very
        /// candle it describes, and disagrees with the speech and audio layers, which already
        /// follow the transform.
        /// </para>
        /// </summary>
        private static float ResolveMarkerY(RenderContext ctx, ComponentConfig comp, int i, double val)
        {
            if (comp.MarkerAnchor == MarkerAnchor.Value || i < 0 || i >= ctx.Data.Count)
                return ChartMath.MapY(val, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);

            var bar = ctx.Data[i];
            double range = bar.High - bar.Low;
            // Pad by a share of the bar's own range so the gap looks consistent at any zoom, with
            // a floor for doji bars whose range is ~0.
            double pad = range > 0 ? range * 0.35 : Math.Abs(bar.Close) * 0.002;

            double anchor = comp.MarkerAnchor == MarkerAnchor.BelowBar ? bar.Low - pad : bar.High + pad;
            return ChartMath.MapY(anchor, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
        }

        /// <summary>
        /// Renders a fixed upward-pointing triangle at each bar's value Y. Direction is always up,
        /// regardless of value sign — use when the indicator pre-determines bullish direction.
        /// Size = comp.Thickness * 3 (same as Arrow).
        /// </summary>
        public static void RenderTriangleUp(RenderContext ctx, ChartSeries series, ComponentConfig comp, SKPaint paint)
        {
            var data = series.GetComponentData(comp.Name);
            if (data == null || data.Length == 0) return;

            float barWidth  = ctx.Width / ctx.ViewportLength;
            float halfBar   = barWidth / 2.0f;
            float arrowSize = ClampMarkerExtent(Math.Max(comp.Thickness * 3f, 6f) * ctx.Density, ctx);

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= data.Length) break;
                double val = data[dataIdx];
                if (double.IsNaN(val)) continue;

                float x = (i * barWidth) + halfBar;
                float y = ResolveMarkerY(ctx, comp, i, val);

                using var path = new SKPath();
                path.MoveTo(x,                    y - arrowSize);
                path.LineTo(x - arrowSize / 2f,   y);
                path.LineTo(x + arrowSize / 2f,   y);
                path.Close();

                var col = ResolveBarColor(comp, data, dataIdx) ?? paint.Color;
                using var lease = SKPaintPool.Rent();
                var p = lease.Paint;
                p.Color = col; p.Style = SKPaintStyle.Fill; p.IsAntialias = true;
                ctx.Canvas.DrawPath(path, p);
            }
        }

        /// <summary>
        /// Renders a fixed downward-pointing triangle at each bar's value Y. Direction is always down,
        /// regardless of value sign — use when the indicator pre-determines bearish direction.
        /// Size = comp.Thickness * 3 (same as Arrow).
        /// </summary>
        public static void RenderTriangleDown(RenderContext ctx, ChartSeries series, ComponentConfig comp, SKPaint paint)
        {
            var data = series.GetComponentData(comp.Name);
            if (data == null || data.Length == 0) return;

            float barWidth  = ctx.Width / ctx.ViewportLength;
            float halfBar   = barWidth / 2.0f;
            float arrowSize = ClampMarkerExtent(Math.Max(comp.Thickness * 3f, 6f) * ctx.Density, ctx);

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= data.Length) break;
                double val = data[dataIdx];
                if (double.IsNaN(val)) continue;

                float x = (i * barWidth) + halfBar;
                float y = ResolveMarkerY(ctx, comp, i, val);

                using var path = new SKPath();
                path.MoveTo(x,                    y + arrowSize);
                path.LineTo(x - arrowSize / 2f,   y);
                path.LineTo(x + arrowSize / 2f,   y);
                path.Close();

                var col = ResolveBarColor(comp, data, dataIdx) ?? paint.Color;
                using var lease = SKPaintPool.Rent();
                var p = lease.Paint;
                p.Color = col; p.Style = SKPaintStyle.Fill; p.IsAntialias = true;
                ctx.Canvas.DrawPath(path, p);
            }
        }

        /// <summary>
        /// Renders a rotated 45° square (diamond) at each bar's value Y. Visually distinct from Dot.
        /// Ideal for divergence markers. Half-size = comp.Thickness * density.
        /// </summary>
        public static void RenderDiamond(RenderContext ctx, ChartSeries series, ComponentConfig comp, SKPaint paint)
        {
            var data = series.GetComponentData(comp.Name);
            if (data == null || data.Length == 0) return;

            float barWidth = ctx.Width / ctx.ViewportLength;
            float halfBar  = barWidth / 2.0f;
            float half     = ClampMarkerHalfExtent(Math.Max(comp.Thickness, 2f) * ctx.Density, ctx);

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= data.Length) break;
                double val = data[dataIdx];
                if (double.IsNaN(val)) continue;

                float x = (i * barWidth) + halfBar;
                float y = ResolveMarkerY(ctx, comp, i, val);

                using var path = new SKPath();
                path.MoveTo(x,        y - half);   // top
                path.LineTo(x + half, y);           // right
                path.LineTo(x,        y + half);   // bottom
                path.LineTo(x - half, y);           // left
                path.Close();

                var col = ResolveBarColor(comp, data, dataIdx) ?? paint.Color;
                using var lease = SKPaintPool.Rent();
                var p = lease.Paint;
                p.Color = col; p.Style = SKPaintStyle.Fill; p.IsAntialias = true;
                ctx.Canvas.DrawPath(path, p);
            }
        }

        /// <summary>
        /// Renders an axis-aligned filled square at each bar's value Y.
        /// Half-size = comp.Thickness * density. Useful for POC/profile markers.
        /// </summary>
        public static void RenderSquare(RenderContext ctx, ChartSeries series, ComponentConfig comp, SKPaint paint)
        {
            var data = series.GetComponentData(comp.Name);
            if (data == null || data.Length == 0) return;

            float barWidth = ctx.Width / ctx.ViewportLength;
            float halfBar  = barWidth / 2.0f;
            float half     = ClampMarkerHalfExtent(Math.Max(comp.Thickness, 2f) * ctx.Density, ctx);

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= data.Length) break;
                double val = data[dataIdx];
                if (double.IsNaN(val)) continue;

                float x = (i * barWidth) + halfBar;
                float y = ResolveMarkerY(ctx, comp, i, val);

                var col = ResolveBarColor(comp, data, dataIdx) ?? paint.Color;
                using var lease = SKPaintPool.Rent();
                var p = lease.Paint;
                p.Color = col; p.Style = SKPaintStyle.Fill; p.IsAntialias = true;
                ctx.Canvas.DrawRect(x - half, y - half, half * 2f, half * 2f, p);
            }
        }

        /// <summary>
        /// Renders an X-shaped cross marker at each bar's value Y.
        /// Arm length = comp.Thickness * density. Useful for invalidation/alert flags.
        /// </summary>
        public static void RenderCross(RenderContext ctx, ChartSeries series, ComponentConfig comp, SKPaint paint)
        {
            var data = series.GetComponentData(comp.Name);
            if (data == null || data.Length == 0) return;

            float barWidth = ctx.Width / ctx.ViewportLength;
            float halfBar  = barWidth / 2.0f;
            float arm      = ClampMarkerHalfExtent(Math.Max(comp.Thickness, 2f) * ctx.Density, ctx);

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= data.Length) break;
                double val = data[dataIdx];
                if (double.IsNaN(val)) continue;

                float x = (i * barWidth) + halfBar;
                float y = ResolveMarkerY(ctx, comp, i, val);

                var col = ResolveBarColor(comp, data, dataIdx) ?? paint.Color;
                using var crossLease = SKPaintPool.Rent();
                var crossPaint = crossLease.Paint;
                crossPaint.Color = col;
                crossPaint.Style = SKPaintStyle.Stroke;
                crossPaint.StrokeWidth = Math.Max(comp.Thickness * 0.5f, 1.5f) * ctx.Density;
                crossPaint.IsAntialias = true;
                crossPaint.StrokeCap = SKStrokeCap.Round;
                ctx.Canvas.DrawLine(x - arm, y - arm, x + arm, y + arm, crossPaint);
                ctx.Canvas.DrawLine(x + arm, y - arm, x - arm, y + arm, crossPaint);
            }
        }

        /// <summary>
        /// Renders a cloud fill between two data arrays driven by a <see cref="CloudFillConfig"/>.
        /// Called by <see cref="DataLayer"/> for fills declared at the series level.
        /// </summary>
        public static void RenderCloudFill(RenderContext ctx, ChartSeries series, CloudFillConfig fill)
        {
            var upperData = series.GetComponentData(fill.UpperComponentName);
            var lowerData = series.GetComponentData(fill.LowerComponentName);
            if (upperData == null || upperData.Length == 0 || lowerData == null || lowerData.Length == 0) return;

            float barWidth = ctx.Width / ctx.ViewportLength;
            float halfBar  = barWidth / 2.0f;

            SKColor.TryParse(fill.BullishColorHex, out var bullishColor);
            SKColor.TryParse(fill.BearishColorHex, out var bearishColor);

            // Alpha resolution: if the color string is 9 chars (#AARRGGBB — SkiaSharp's native
            // 8-hex format with alpha first), respect the encoded alpha so callers can control
            // opacity precisely via metadata. For the legacy 6-char (#RRGGBB) case, fall back to
            // the historical default of 90 (~35%) so existing fills keep their appearance.
            byte bullAlpha = fill.BullishColorHex.Length == 9 ? bullishColor.Alpha : (byte)90;
            byte bearAlpha = fill.BearishColorHex.Length == 9 ? bearishColor.Alpha : (byte)90;

            using var bullPaint = new SKPaint { Color = bullishColor.WithAlpha(bullAlpha), Style = SKPaintStyle.Fill };
            using var bearPaint = new SKPaint { Color = bearishColor.WithAlpha(bearAlpha), Style = SKPaintStyle.Fill };

            int  runStart = -1;
            bool lastBull = false;
            SKPoint? startCross = null; // shared apex where the previous run crossed into this one

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= upperData.Length || dataIdx >= lowerData.Length) break;
                double u = upperData[dataIdx], l = lowerData[dataIdx];
                if (double.IsNaN(u) || double.IsNaN(l))
                {
                    if (runStart >= 0) FlushCloudRun(ctx, upperData, lowerData, runStart, i - 1, lastBull, barWidth, halfBar, bullPaint, bearPaint, startCross, null);
                    runStart = -1; startCross = null; continue;
                }
                bool bull = u >= l;
                if (runStart < 0) { runStart = i; lastBull = bull; startCross = null; }
                else if (bull != lastBull)
                {
                    // The two lines cross between bar i-1 and i. Both the run that ends
                    // here and the run that starts here share that exact crossover point
                    // as an apex, so the fill is continuous instead of leaving a gap.
                    var cross = CloudCrossoverPoint(ctx, upperData, lowerData, i - 1, i, barWidth, halfBar);
                    FlushCloudRun(ctx, upperData, lowerData, runStart, i - 1, lastBull, barWidth, halfBar, bullPaint, bearPaint, startCross, cross);
                    runStart = i; lastBull = bull; startCross = cross;
                }
            }
            if (runStart >= 0)
                FlushCloudRun(ctx, upperData, lowerData, runStart, ctx.ViewportLength - 1, lastBull, barWidth, halfBar, bullPaint, bearPaint, startCross, null);
        }

        /// <summary>Fraction along [left,right] where the upper and lower lines cross
        /// (upper==lower). Pure so it's unit-testable. Clamped to [0,1].</summary>
        internal static double CloudCrossoverT(double u1, double l1, double u2, double l2)
        {
            double d1 = u1 - l1, d2 = u2 - l2, denom = d1 - d2;
            double t = Math.Abs(denom) < 1e-12 ? 0.5 : d1 / denom;
            return Math.Clamp(t, 0.0, 1.0);
        }

        private static SKPoint CloudCrossoverPoint(RenderContext ctx, double[] upper, double[] lower,
            int iLeft, int iRight, float barWidth, float halfBar)
        {
            int dL = ctx.ViewportStart + iLeft, dR = ctx.ViewportStart + iRight;
            double u1 = upper[dL], l1 = lower[dL], u2 = upper[dR], l2 = lower[dR];
            double t = CloudCrossoverT(u1, l1, u2, l2);
            double val = u1 + t * (u2 - u1); // == l at the crossover
            float x = ((iLeft + (float)t) * barWidth) + halfBar;
            float y = ChartMath.MapY(val, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
            return new SKPoint(x, y);
        }

        private static void FlushCloudRun(RenderContext ctx,
            double[] upperData, double[] lowerData, int from, int to, bool bull,
            float barWidth, float halfBar, SKPaint bullFill, SKPaint bearFill,
            SKPoint? startCross, SKPoint? endCross)
        {
            using var path = new SKPath();
            bool started = false;
            if (startCross is { } sc) { path.MoveTo(sc.X, sc.Y); started = true; }
            for (int i = from; i <= to; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= upperData.Length) break;
                float x = (i * barWidth) + halfBar;
                float y = ChartMath.MapY(upperData[dataIdx], ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                if (!started) { path.MoveTo(x, y); started = true; } else path.LineTo(x, y);
            }
            if (endCross is { } ec) path.LineTo(ec.X, ec.Y);
            for (int i = to; i >= from; i--)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= lowerData.Length) break;
                float x = (i * barWidth) + halfBar;
                float y = ChartMath.MapY(lowerData[dataIdx], ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                path.LineTo(x, y);
            }
            path.Close();
            ctx.Canvas.DrawPath(path, bull ? bullFill : bearFill);
        }

        /// <summary>
        /// Renders a horizontal zone band. Two modes:
        /// - Component-centred: band follows a component's carry-forward level value with
        ///   half-width = <see cref="ZoneBandConfig.BandWidthPct"/>% of that level (S/R zones).
        /// - Fixed: band spans the full viewport between <see cref="ZoneBandConfig.FixedTop"/>
        ///   and <see cref="ZoneBandConfig.FixedBottom"/> in pane coordinates (OB/OS zones).
        /// </summary>
        public static void RenderZoneBand(RenderContext ctx, ChartSeries series, ZoneBandConfig band)
        {
            SKColor.TryParse(band.ColorHex, out var color);
            byte alpha = band.ColorHex.Length == 9 ? color.Alpha : (byte)50;
            using var paint = new SKPaint { Color = color.WithAlpha(alpha), Style = SKPaintStyle.Fill };

            if (band.IsFixedMode)
            {
                float yTop    = ChartMath.MapY(band.FixedTop,    ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                float yBottom = ChartMath.MapY(band.FixedBottom, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                if (yTop > yBottom) (yTop, yBottom) = (yBottom, yTop);
                ctx.Canvas.DrawRect(SKRect.Create(0f, yTop, ctx.Width, yBottom - yTop), paint);
                return;
            }

            var data = series.GetComponentData(band.ComponentName);
            if (data == null || data.Length == 0) return;

            float barWidth    = ctx.Width / ctx.ViewportLength;
            double halfWidthRatio = band.BandWidthPct / 100.0;

            int    runStart = -1;
            double runLevel = double.NaN;

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= data.Length) break;

                double v        = data[dataIdx];
                bool   isNaN    = double.IsNaN(v);
                // Treat a change of level as a new run so each distinct S/R level gets its own band.
                bool   newLevel = !isNaN && !double.IsNaN(runLevel) && Math.Abs(v - runLevel) > runLevel * 0.0001;

                if (isNaN || newLevel)
                {
                    if (runStart >= 0)
                        FlushZoneBand(ctx, runLevel, runStart, i - 1, barWidth, halfWidthRatio, paint);
                    runStart = isNaN ? -1 : i;
                    runLevel = isNaN ? double.NaN : v;
                }
                else if (runStart < 0)
                {
                    runStart = i;
                    runLevel = v;
                }
            }
            if (runStart >= 0)
                FlushZoneBand(ctx, runLevel, runStart, ctx.ViewportLength - 1, barWidth, halfWidthRatio, paint);
        }

        /// <summary>
        /// Renders a continuous ribbon of small colored dots at each bar's Y position (stored in the
        /// component array — typically the close price). The dot color is interpolated along a
        /// teal ↔ gray ↔ red gradient driven by a companion "_color" array holding the raw oscillator
        /// value (expected range ≈ −100 … +100, as produced by the Wave Trend oscillator).
        ///
        /// teal  (#00FF9F) = WT deeply negative (oversold momentum)
        /// gray  (#90A4AE) = WT near zero       (neutral momentum)
        /// red   (#FF3D00) = WT deeply positive  (overbought momentum)
        /// </summary>
        public static void RenderGradientDot(RenderContext ctx, ChartSeries series, ComponentConfig comp)
        {
            var posData   = series.GetComponentData(comp.Name);                // Y positions (e.g. close price)
            var colorData = series.GetComponentData(comp.Name + "_color");     // oscillator values for color

            if (posData == null || posData.Length == 0) return;

            float barWidth = ctx.Width / ctx.ViewportLength;
            float halfBar  = barWidth / 2.0f;
            float radius   = ClampMarkerHalfExtent(Math.Max(comp.Thickness, 1.5f) * ctx.Density, ctx);

            // Gradient anchor colours — must match RenderDot's palette exactly.
            // Power curve (0.6) applied below so moderate WT readings saturate vividly.
            var teal = new SKColor(0,   255, 159);  // #00FF9F oversold
            var gray = new SKColor(144, 164, 174);  // #90A4AE neutral
            var red  = new SKColor(255,  61,   0);  // #FF3D00 overbought
            byte alpha = 230;

            for (int i = 0; i < ctx.ViewportLength; i++)
            {
                int dataIdx = ctx.ViewportStart + i;
                if (dataIdx >= posData.Length) break;

                double yVal = posData[dataIdx];
                if (double.IsNaN(yVal)) continue;

                float x = (i * barWidth) + halfBar;
                float y = ChartMath.MapY(yVal, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);

                // Determine dot color from the oscillator value (clamped to ±100).
                SKColor dotColor;
                if (colorData != null && dataIdx < colorData.Length && !double.IsNaN(colorData[dataIdx]))
                {
                    double wt = Math.Clamp(colorData[dataIdx], -100.0, 100.0);
                    // Power curve (exponent 0.6): saturates color faster at moderate WT values.
                    float t = (float)Math.Pow(Math.Abs(wt) / 100.0, 0.6);
                    SKColor from = gray;          // neutral center — always gray
                    SKColor to   = wt < 0 ? teal : red;
                    dotColor = new SKColor(
                        (byte)(from.Red   + (to.Red   - from.Red)   * t),
                        (byte)(from.Green + (to.Green - from.Green) * t),
                        (byte)(from.Blue  + (to.Blue  - from.Blue)  * t),
                        alpha);
                }
                else
                {
                    dotColor = gray.WithAlpha(alpha);
                }

                using var dotPaint = new SKPaint { Color = dotColor, Style = SKPaintStyle.Fill, IsAntialias = true };
                ctx.Canvas.DrawCircle(x, y, radius, dotPaint);
            }
        }

        private static void FlushZoneBand(RenderContext ctx, double level, int from, int to,
            float barWidth, double halfWidthRatio, SKPaint paint)
        {
            double topPrice    = level * (1.0 + halfWidthRatio);
            double bottomPrice = level * (1.0 - halfWidthRatio);

            float yTop    = ChartMath.MapY(topPrice,    ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
            float yBottom = ChartMath.MapY(bottomPrice, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
            float xLeft   = from * barWidth;
            float xRight  = (to + 1) * barWidth;

            // Ensure yTop < yBottom (MapY inverts Y axis — higher price = smaller Y)
            if (yTop > yBottom) (yTop, yBottom) = (yBottom, yTop);

            ctx.Canvas.DrawRect(SKRect.Create(xLeft, yTop, xRight - xLeft, yBottom - yTop), paint);
        }
    }
}
