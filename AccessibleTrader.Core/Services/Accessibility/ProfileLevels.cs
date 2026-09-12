using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// The three prices a volume or market profile is ABOUT, read off its bins: the point of
    /// control (the bin with the most volume, or the most time), and the value area's high and
    /// low (the outermost bins flagged as value area). One reader for the navigation overview,
    /// the narration ladder and the alerts, so "the POC" is one number everywhere it is spoken.
    /// Prices are bin midpoints, as <c>VolumeProfileLevelProvider</c> reports them, so a level
    /// heard here is the same level a POC alert fires on.
    /// </summary>
    public static class ProfileLevels
    {
        public readonly record struct Levels(double? Poc, double? ValueAreaHigh, double? ValueAreaLow, double BinWidth, int BinCount)
        {
            public bool HasPoc => Poc.HasValue;
            public bool HasValueArea => ValueAreaHigh.HasValue && ValueAreaLow.HasValue;
        }

        public static Levels Of(IReadOnlyList<ProfileBin>? bins)
        {
            if (bins == null || bins.Count == 0) return new Levels(null, null, null, 0, 0);

            double? poc = null, vah = null, val = null;
            double width = 0;
            foreach (var bin in bins)
            {
                width = Math.Max(width, bin.PriceHigh - bin.PriceLow);
                if (bin.IsPOC && poc == null) poc = bin.PriceMid;
                if (bin.IsValueArea)
                {
                    if (vah == null || bin.PriceMid > vah) vah = bin.PriceMid;
                    if (val == null || bin.PriceMid < val) val = bin.PriceMid;
                }
            }
            return new Levels(poc, vah, val, width, bins.Count);
        }

        /// <summary>
        /// The profile in one breath, for the moment it becomes the focused series: where the
        /// point of control is, where the value area runs, and how to move through it. Spoken
        /// when no bin is focused yet — which is every time a profile is added or switched to,
        /// since only Up and Down select a bin.
        /// </summary>
        public static string Overview(IReadOnlyList<ProfileBin>? bins)
        {
            var l = Of(bins);
            if (l.BinCount == 0) return "No data.";
            var parts = new List<string>();
            if (l.Poc is double poc) parts.Add($"Point of control {SpeechPriceFormatter.FormatPrice(poc)}.");
            if (l.HasValueArea)
                parts.Add($"Value area {SpeechPriceFormatter.FormatPrice(l.ValueAreaLow!.Value)} to {SpeechPriceFormatter.FormatPrice(l.ValueAreaHigh!.Value)}.");
            parts.Add("Up or down moves through the bins.");
            return string.Join(" ", parts);
        }
    }
}
