using SkiaSharp;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// Draws volume-profile bins for any profile series assigned to this pane.
    ///
    /// Deliberately dependency-free (2026-08-24): it previously took an
    /// <c>IProfileService</c>, a <c>ThemeService</c> and an <c>IAppLogger</c>, assigned all
    /// three to fields, and used none of them — the theme it draws with comes from
    /// <c>ctx.Theme</c> on the render context, and the bins are already on the series by the
    /// time this runs. Carrying them made the layer look like it computed profiles and
    /// logged failures; it does neither. Removing them also let <c>ChartRenderer</c> stop
    /// taking an <c>IProfileService</c> it only ever forwarded here.
    ///
    /// <para><b>Two passes at two depths</b> (2026-10-15). <c>ChartRenderer</c> paints the bins
    /// (<see cref="RenderBins"/>) straight after the pane background, UNDER the candles, and the
    /// point-of-control line (<see cref="RenderPoc"/>) after every data layer. The whole profile
    /// used to be painted last, over the candles: in Cody's 2026-10-15 BTCUSDT 1d screenshots a
    /// translucent grey slab covered the newest three months, the part of the chart a trader most
    /// needs to see. The POC stays on top because it is a price LEVEL read against the candles,
    /// like a formation's trigger line — a level hidden behind a wick communicates nothing — and
    /// it is a 2px line, not a slab.</para>
    /// </summary>
    public class ProfileRenderLayer : IRenderLayer
    {
        /// <summary>Both passes, bins then POC. <c>ChartRenderer</c> calls the two separately.</summary>
        public void Render(RenderContext ctx, IEnumerable<ChartSeries> series)
        {
            RenderBins(ctx, series);
            RenderPoc(ctx, series);
        }

        /// <summary>The volume bars, right-aligned. Painted under the candles.</summary>
        public void RenderBins(RenderContext ctx, IEnumerable<ChartSeries> series)
        {
            foreach (var (bins, maxVolume) in Profiles(ctx, series))
            {
                float profileWidth = ProfileWidth(ctx);
                using var paint = new SKPaint
                {
                    Color = ctx.Theme.ProfileNormal.WithAlpha(BinAlpha),
                    Style = SKPaintStyle.Fill
                };

                foreach (var bin in bins)
                {
                    float yTop = ChartMath.MapY(bin.PriceHigh, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                    float yBottom = ChartMath.MapY(bin.PriceLow, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                    float binWidth = (float)(bin.TotalVolume / maxVolume) * profileWidth;

                    // Right-aligned profile
                    ctx.Canvas.DrawRect(ctx.Width - binWidth, Math.Min(yTop, yBottom), binWidth, Math.Abs(yBottom - yTop), paint);
                }
            }
        }

        /// <summary>The point-of-control line across the profile strip. Painted over the candles.</summary>
        public void RenderPoc(RenderContext ctx, IEnumerable<ChartSeries> series)
        {
            foreach (var (bins, _) in Profiles(ctx, series))
            {
                float profileWidth = ProfileWidth(ctx);
                using var pocPaint = new SKPaint
                {
                    Color = ctx.Theme.ProfilePOC,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 2 * ctx.Density
                };

                foreach (var bin in bins)
                {
                    if (!bin.IsPOC) continue;
                    float yTop = ChartMath.MapY(bin.PriceHigh, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                    float yBottom = ChartMath.MapY(bin.PriceLow, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, ctx.IsLogScale);
                    float yPOC = (yTop + yBottom) / 2.0f;
                    ctx.Canvas.DrawLine(ctx.Width - profileWidth, yPOC, ctx.Width, yPOC, pocPaint);
                }
            }
        }

        /// <summary>
        /// Opacity of the bins: 60 of 255, down from 100. Under the candles the bins become the
        /// surface the newest candles sit on, so every point of opacity is taken out of those
        /// candles' contrast. Measured 2026-10-15 on every built-in theme: at 100 a falling
        /// candle on Blackout dropped from 4.07:1 against the chart to 2.38:1 against the profile
        /// (Classic 5.13 to 3.07), about 40% of its contrast gone. At 60 every candle either stays
        /// above the 3:1 graphics floor or keeps 70% of its contrast (Steel Gray's falling candle,
        /// already under 3:1 on the bare chart, excepted), and the bins still stand at about 1.2:1
        /// against the chart: a faint shape, not nothing. Below about 40 they vanish on High
        /// Contrast Dark. The theme's own ProfileNormal alpha was never used (it was overridden
        /// with 100 before this too). Pinned in rendered pixels by <c>VolumeProfileDepthTests</c>.
        /// </summary>
        private const byte BinAlpha = 60;

        // Profiles take up 20% of pane width.
        private static float ProfileWidth(RenderContext ctx) => ctx.Width * 0.2f;

        /// <summary>The drawable profiles for this pane, each with its largest bin's volume.</summary>
        private static IEnumerable<(List<ProfileBin> Bins, float MaxVolume)> Profiles(RenderContext ctx, IEnumerable<ChartSeries> series)
        {
            foreach (var s in series)
            {
                if (!s.IsVisible || !s.IsProfile) continue;
                // Allow profiles to render in their assigned pane (usually 'Main')
                if (s.Pane != ctx.PaneName && !(string.IsNullOrEmpty(s.Pane) && ctx.PaneName == "Main")) continue;

                var bins = s.ProfileBins;
                if (bins == null || !bins.Any()) continue;

                float maxVolume = (float)bins.Max(b => b.TotalVolume);
                if (maxVolume <= 0) continue;

                yield return (bins, maxVolume);
            }
        }
    }
}
