using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface ITactileCanvasCoordinator
    {
        void UpdateCanvas(List<Ohlcv> data, IEnumerable<ChartSeries> activeSeries);
        void SetViewportCenter(int dataIndex, double priceLevel);
    }

    public class TactileCanvasCoordinator : ITactileCanvasCoordinator
    {
        private readonly ITactileDriver _tactileDriver;
        private readonly IWorkspaceStore _store;

        private const int VirtualWidth = 960;  // 10x physical
        private const int VirtualHeight = 400; // 10x physical
        private bool[,] _virtualCanvas = new bool[VirtualWidth, VirtualHeight];

        public TactileCanvasCoordinator(ITactileDriver tactileDriver, IWorkspaceStore store)
        {
            _tactileDriver = tactileDriver;
            _store = store;
        }

        public void UpdateCanvas(List<Ohlcv> data, IEnumerable<ChartSeries> activeSeries)
        {
            if (data == null || !data.Any() || !_tactileDriver.IsConnected) return;

            Array.Clear(_virtualCanvas, 0, _virtualCanvas.Length);

            // Calculate min/max for scaling
            double minPrice = data.Min(d => d.Low);
            double maxPrice = data.Max(d => d.High);
            double range = maxPrice - minPrice;
            if (range == 0) range = 1;

            int barsToRender = Math.Min(_store.State.ViewportLength, VirtualWidth / 3); // 3 pixels per bar
            int startIndex = Math.Clamp(_store.State.ViewportStartIndex, 0, Math.Max(0, data.Count - barsToRender));

            for (int i = 0; i < barsToRender; i++)
            {
                var bar = data[startIndex + i];
                int x = i * 3 + 1; // Center of bar

                // Map high/low to virtual Y
                int yHigh = (int)(((maxPrice - bar.High) / range) * (VirtualHeight - 1));
                int yLow = (int)(((maxPrice - bar.Low) / range) * (VirtualHeight - 1));

                yHigh = Math.Clamp(yHigh, 0, VirtualHeight - 1);
                yLow = Math.Clamp(yLow, 0, VirtualHeight - 1);

                // Draw wick
                for (int y = yHigh; y <= yLow; y++)
                {
                    _virtualCanvas[x, y] = true;
                }

                // Draw body (thicker)
                int yOpen = (int)(((maxPrice - bar.Open) / range) * (VirtualHeight - 1));
                int yClose = (int)(((maxPrice - bar.Close) / range) * (VirtualHeight - 1));
                
                int bodyTop = Math.Min(yOpen, yClose);
                int bodyBottom = Math.Max(yOpen, yClose);
                
                bodyTop = Math.Clamp(bodyTop, 0, VirtualHeight - 1);
                bodyBottom = Math.Clamp(bodyBottom, 0, VirtualHeight - 1);

                for (int y = bodyTop; y <= bodyBottom; y++)
                {
                    _virtualCanvas[x - 1, y] = true;
                    _virtualCanvas[x, y] = true;
                    _virtualCanvas[x + 1, y] = true;
                }
            }

            // Sync viewport based on current cursor
            SyncViewport();
        }

        public void SetViewportCenter(int dataIndex, double priceLevel)
        {
            SyncViewport();
        }

        private void SyncViewport()
        {
            if (!_tactileDriver.IsConnected) return;

            // Simple logic: keep viewport centered on latest for now
            // Can be expanded to track NavigationEngine's dataIndex
            int startX = VirtualWidth - _tactileDriver.DisplayWidth;
            int startY = VirtualHeight / 2 - _tactileDriver.DisplayHeight / 2;

            _tactileDriver.RenderViewportAsync(_virtualCanvas, Math.Max(0, startX), Math.Max(0, startY));
        }
    }
}