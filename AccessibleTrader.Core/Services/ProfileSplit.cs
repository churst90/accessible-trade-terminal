using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Which side leads a volume-profile row. The ONE rule speech and sound share, so the
    /// waveform can never disagree with the words: a row spoken as "even" plays the sine.
    /// </summary>
    public static class ProfileSplit
    {
        /// <summary>Within this many percentage points of 50 a row is even.</summary>
        public const double EvenBand = 0.5;

        /// <summary>
        /// +1 when buying leads, -1 when selling leads, 0 when the row is even or has no split
        /// (a TPO row, no volume, or a bin restored from before the split).
        /// <paramref name="buyingPercent"/> is buying's share of the split, NaN without one.
        /// </summary>
        public static int Leader(ProfileBin bin, out double buyingPercent)
        {
            double split = bin.BuyVolume + bin.SellVolume;
            if (!(split > 0)) { buyingPercent = double.NaN; return 0; }
            buyingPercent = bin.BuyVolume / split * 100.0;
            if (Math.Abs(buyingPercent - 50.0) <= EvenBand) return 0;
            return buyingPercent > 50.0 ? 1 : -1;
        }
    }
}
