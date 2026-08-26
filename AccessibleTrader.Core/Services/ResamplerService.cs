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
        /// <summary>
        /// One aggregation bucket in progress. A mutable struct rather than an <see cref="Ohlcv"/>
        /// because it carries two extra timestamps — which bar in the bucket opened it and which
        /// closed it — that have no place on the published bar.
        /// </summary>
        private struct Bucket
        {
            public DateTime Start;
            public DateTime OpenAt;
            public DateTime CloseAt;
            public double Open;
            public double High;
            public double Low;
            public double Close;
            public double Volume;
        }

        public List<Ohlcv> Resample(List<Ohlcv> bars, string targetTimeframe)
        {
            if (bars == null || bars.Count == 0) return new List<Ohlcv>();
            
            long periodMs = TimeframeUtility.ToMilliseconds(targetTimeframe);
            // 1m bars are already 1m-bucketed — nothing to aggregate.
            if (targetTimeframe == "1m") return new List<Ohlcv>(bars);

            // PERFORMANCE OPTIMIZATION: Use a SortedDictionary to avoid post-aggregation sorting.
            // This reduces GC pressure significantly during high-frequency resampling.
            var grouped = new SortedDictionary<long, Bucket>();

            foreach (var bar in bars)
            {
                DateTime bucketStartDt = TimeframeUtility.GetPeriodStart(bar.Date, targetTimeframe);
                long bucketStart = new DateTimeOffset(bucketStartDt).ToUnixTimeMilliseconds();

                if (!grouped.TryGetValue(bucketStart, out var agg))
                {
                    grouped[bucketStart] = new Bucket
                    {
                        Start = bucketStartDt,
                        OpenAt = bar.Date, CloseAt = bar.Date,
                        Open = bar.Open, High = bar.High, Low = bar.Low, Close = bar.Close,
                        Volume = bar.Volume,
                    };
                }
                else
                {
                    // Open and Close are taken from the EARLIEST and LATEST bar in the bucket by
                    // timestamp, not by arrival order.
                    //
                    // Taking them by arrival order — first seen opens, last seen closes — is right
                    // only while the input happens to be ascending, and nothing on the way in
                    // enforces that. Plenty of venues return history newest-first, and
                    // HistoricalDataFetcher hands whatever the provider gave it straight to this
                    // method. Fed descending 1m bars, every aggregated candle came out with its
                    // open and close swapped: a down bar reported as an up bar, on a chart that is
                    // read aloud, sonified by direction and traded from. The comparison below
                    // costs one branch per bar and removes the dependency on a precondition
                    // nobody was checking.
                    if (bar.Date <= agg.OpenAt)  { agg.OpenAt  = bar.Date; agg.Open  = bar.Open; }
                    if (bar.Date >= agg.CloseAt) { agg.CloseAt = bar.Date; agg.Close = bar.Close; }
                    if (bar.High > agg.High) agg.High = bar.High;
                    if (bar.Low  < agg.Low)  agg.Low  = bar.Low;
                    agg.Volume += bar.Volume;
                    grouped[bucketStart] = agg;
                }
            }

            var result = new List<Ohlcv>(grouped.Count);
            foreach (var b in grouped.Values)
                result.Add(new Ohlcv(b.Start, b.Open, b.High, b.Low, b.Close, b.Volume));
            return result;
        }

        public long GetBucketStart(DateTime time, string timeframe)
        {
            DateTime dt = TimeframeUtility.GetPeriodStart(time, timeframe);
            return new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        }
    }
}
