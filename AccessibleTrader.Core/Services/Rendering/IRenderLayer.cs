using SkiaSharp;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Rendering
{
    public interface IRenderLayer
    {
        void Render(RenderContext ctx, IEnumerable<ChartSeries> series);
    }
}
