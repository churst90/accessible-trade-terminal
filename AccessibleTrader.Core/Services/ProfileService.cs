using System.Collections.Immutable;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public interface IProfileService
    {
        List<ProfileBin> CalculateVolumeProfile(IReadOnlyList<Ohlcv> data, int binCount = 50);
        List<ProfileBin> CalculateMarketProfile(IReadOnlyList<Ohlcv> data, int binCount = 50);
    }

    public class ProfileService : IProfileService
    {
        // TPO letter sequence: A-Z, a-z, 0-9 (62 unique chars, then cycles)
        private static readonly char[] TpoLetterSequence =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

        public List<ProfileBin> CalculateVolumeProfile(IReadOnlyList<Ohlcv> data, int binCount = 50)
        {
            if (data == null || !data.Any()) return new List<ProfileBin>();

            double min = data.Min(x => x.Low);
            double max = data.Max(x => x.High);

            return CalculateProfileInternal(data, min, max, binCount, isVolume: true);
        }

        public List<ProfileBin> CalculateMarketProfile(IReadOnlyList<Ohlcv> data, int binCount = 50)
        {
            if (data == null || !data.Any()) return new List<ProfileBin>();

            double min = data.Min(x => x.Low);
            double max = data.Max(x => x.High);

            return CalculateProfileInternal(data, min, max, binCount, isVolume: false);
        }

        private List<ProfileBin> CalculateProfileInternal(IReadOnlyList<Ohlcv> data, double min, double max, int binCount, bool isVolume)
        {
            double range = max - min;
            // Phase 4.1: divide-by-zero guard — return empty if range is zero
            if (range <= 0) return new List<ProfileBin>();

            double binSize = range / binCount;

            var volumeBins  = new double[binCount];
            var tpoCounts   = new double[binCount];
            // Each bin accumulates a set of TPO letters that visited it
            var tpoLetterSets = new List<HashSet<char>>(binCount);
            for (int i = 0; i < binCount; i++) tpoLetterSets.Add(new HashSet<char>());
            var singlePrintBins = new bool[binCount]; // will be computed after fill

            for (int barIdx = 0; barIdx < data.Count; barIdx++)
            {
                var d = data[barIdx];
                // Assign TPO letter for this bar (cycles through sequence)
                char tpoLetter = TpoLetterSequence[barIdx % TpoLetterSequence.Length];

                if (isVolume)
                {
                    // Volume profile: attribute volume to the bin containing the close.
                    // Always increment tpoCounts as well — this provides a bar-hit fallback
                    // density so profiles render even when the data source supplies zero volume.
                    int index = (int)((d.Close - min) / binSize);
                    index = Math.Clamp(index, 0, binCount - 1);
                    volumeBins[index] += d.Volume;
                    tpoCounts[index]++;
                }
                else
                {
                    // TPO / Market Profile: mark every bin the bar's high-low range crosses
                    int startIdx = (int)((d.Low  - min) / binSize);
                    int endIdx   = (int)((d.High - min) / binSize);
                    startIdx = Math.Clamp(startIdx, 0, binCount - 1);
                    endIdx   = Math.Clamp(endIdx,   0, binCount - 1);

                    for (int i = startIdx; i <= endIdx; i++)
                    {
                        tpoCounts[i]++;
                        volumeBins[i] += d.Volume; // still accumulate volume even in TPO mode
                        tpoLetterSets[i].Add(tpoLetter);
                    }
                }
            }

            // Determine primary metric for POC + value area
            double[] density = isVolume ? volumeBins : tpoCounts;

            return CreateProfileBins(density, volumeBins, tpoCounts, tpoLetterSets, min, binSize, binCount);
        }

        private static List<ProfileBin> CreateProfileBins(
            double[] density,
            double[] volumeBins,
            double[] tpoCounts,
            List<HashSet<char>> tpoLetterSets,
            double min,
            double binSize,
            int binCount)
        {
            double totalDensity = density.Sum();
            double valueAreaThreshold = totalDensity * 0.7;

            // Find POC (bin with highest density)
            int pocIndex = 0;
            for (int i = 1; i < binCount; i++)
                if (density[i] > density[pocIndex]) pocIndex = i;

            // Value Area: expand from POC outward greedily until 70% of total density covered
            var valueAreaIndices = new HashSet<int> { pocIndex };
            double currentSum = density[pocIndex];
            int left  = pocIndex - 1;
            int right = pocIndex + 1;

            while (currentSum < valueAreaThreshold && (left >= 0 || right < binCount))
            {
                double leftVal  = left  >= 0      ? density[left]  : -1;
                double rightVal = right < binCount ? density[right] : -1;

                if (leftVal >= rightVal && left >= 0)
                {
                    currentSum += leftVal;
                    valueAreaIndices.Add(left);
                    left--;
                }
                else if (right < binCount)
                {
                    currentSum += rightVal;
                    valueAreaIndices.Add(right);
                    right++;
                }
                else
                    break;
            }

            var result = new List<ProfileBin>(binCount);
            for (int i = 0; i < binCount; i++)
            {
                double priceLow  = min + (i * binSize);
                double priceHigh = priceLow + binSize;

                // Single-print: in TPO mode, visited by exactly one bar (tpoLetterSets[i].Count == 1)
                bool isSinglePrint = tpoLetterSets[i].Count == 1;

                // Primary TPO letter: the one that appears in the set (or null for volume-only)
                char? primaryLetter = tpoLetterSets[i].Count > 0 ? tpoLetterSets[i].First() : (char?)null;

                result.Add(new ProfileBin
                {
                    PriceLow        = priceLow,
                    PriceHigh       = priceHigh,
                    TotalVolume     = volumeBins[i],
                    TpoPeriodCount  = tpoCounts[i],
                    IsPOC           = i == pocIndex,
                    IsValueArea     = valueAreaIndices.Contains(i),
                    IsSinglePrint   = isSinglePrint,
                    TpoLetter       = primaryLetter,
                    TpoBinIndex     = i,
                    TpoLetters      = tpoLetterSets[i].ToImmutableList()
                });
            }

            return result;
        }
    }
}

