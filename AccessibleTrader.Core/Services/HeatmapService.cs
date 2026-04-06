using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public interface IHeatmapService
    {
        List<List<ProfileBin>> GenerateHeatmap(IReadOnlyList<Ohlcv> data, List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> historicalBooks, double sensitivity);
        List<ProfileBin> GenerateBarHeatmap(Ohlcv bar, List<OrderBookEntry> bids, List<OrderBookEntry> asks, double sensitivity);
    }

    public class HeatmapService : IHeatmapService
    {
        public List<List<ProfileBin>> GenerateHeatmap(IReadOnlyList<Ohlcv> data, List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> historicalBooks, double sensitivity)
        {
            var result = new List<List<ProfileBin>>();
            if (historicalBooks == null || historicalBooks.Count == 0) 
            {
                // If no real order book data, return empty lists for each bar to signal "no data"
                // rather than generating fake Gaussian liquidity.
                for (int i = 0; i < data.Count; i++) result.Add(new List<ProfileBin>());
                return result;
            }

            for (int i = 0; i < data.Count; i++)
            {
                if (i < historicalBooks.Count)
                {
                    var book = historicalBooks[i];
                    result.Add(GenerateBarHeatmap(data[i], book.Bids, book.Asks, sensitivity));
                }
                else
                {
                    result.Add(new List<ProfileBin>());
                }
            }
            return result;
        }

        public List<ProfileBin> GenerateBarHeatmap(Ohlcv bar, List<OrderBookEntry> bids, List<OrderBookEntry> asks, double sensitivity)
        {
            var bins = new List<ProfileBin>();
            if (bids == null || asks == null) return bins;

            // Sensitivity (0-100) acts as a filter for minimum quantity to display
            double maxQty = 0;
            if (bids.Any()) maxQty = Math.Max(maxQty, bids.Max(b => b.Quantity));
            if (asks.Any()) maxQty = Math.Max(maxQty, asks.Max(a => a.Quantity));

            if (maxQty <= 0) return bins;

            // Threshold: 0 sensitivity means show everything, 100 means show only the max liquidity level.
            double threshold = (sensitivity / 100.0) * maxQty;

            foreach (var bid in bids)
            {
                if (bid.Quantity < threshold) continue;

                bins.Add(new ProfileBin {
                    PriceHigh = bid.Price,
                    PriceLow = bid.Price, // Depth is zero-height by default, renderer can add thickness
                    TotalVolume = (bid.Quantity / maxQty) * 100.0, // Normalised 0-100 for heatmap intensity
                    IsPOC = false,
                    IsValueArea = true,
                    TpoPeriodCount = 0
                });
            }

            foreach (var ask in asks)
            {
                if (ask.Quantity < threshold) continue;

                bins.Add(new ProfileBin {
                    PriceHigh = ask.Price,
                    PriceLow = ask.Price,
                    TotalVolume = (ask.Quantity / maxQty) * 100.0,
                    IsPOC = false,
                    IsValueArea = true,
                    TpoPeriodCount = 0
                });
            }

            return bins;
        }
    }
}

