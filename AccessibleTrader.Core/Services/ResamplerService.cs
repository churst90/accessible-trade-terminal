using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public interface IResamplerService
    {
        List<Ohlcv> Resample(List<Ohlcv> bars, string targetTimeframe);
        long GetBucketStart(DateTime time, string timeframe);
    }

    public class ResamplerService : IResamplerService
    {
        public List<Ohlcv> Resample(List<Ohlcv> bars, string targetTimeframe)
        {
            if (bars == null || bars.Count == 0) return new List<Ohlcv>();
            
            long periodMs = TimeframeUtility.ToMilliseconds(targetTimeframe);
            // 1m bars are already 1m-bucketed — nothing to aggregate.
            if (targetTimeframe == "1m") return new List<Ohlcv>(bars);

            // PERFORMANCE OPTIMIZATION: Use a SortedDictionary to avoid post-aggregation sorting.
            // This reduces GC pressure significantly during high-frequency resampling.
            var grouped = new SortedDictionary<long, Ohlcv>();

            foreach (var bar in bars)
            {
                DateTime bucketStartDt = TimeframeUtility.GetPeriodStart(bar.Date, targetTimeframe);
                long bucketStart = new DateTimeOffset(bucketStartDt).ToUnixTimeMilliseconds();

                if (!grouped.TryGetValue(bucketStart, out var agg))
                {
                    grouped[bucketStart] = new Ohlcv
                    (
                        bucketStartDt,
                        bar.Open,
                        bar.High,
                        bar.Low,
                        bar.Close,
                        bar.Volume
                    );
                }
                else
                {
                    grouped[bucketStart] = agg with
                    {
                        High = bar.High > agg.High ? bar.High : agg.High,
                        Low = bar.Low < agg.Low ? bar.Low : agg.Low,
                        Close = bar.Close,
                        Volume = agg.Volume + bar.Volume
                    };
                }
            }

            return grouped.Values.ToList();
        }

        public long GetBucketStart(DateTime time, string timeframe)
        {
            DateTime dt = TimeframeUtility.GetPeriodStart(time, timeframe);
            return new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        }
    }
}
