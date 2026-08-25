namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Provides extension methods for the <see cref="Ohlcv"/> struct to extract specific values 
    /// based on series identification and component names.
    /// </summary>
    public static class OhlcvExtensions
    {
        /// <summary>
        /// Extracts a specific data point from an <see cref="Ohlcv"/> bar based on the series and component context.
        /// </summary>
        /// <param name="point">The OHLCV data point.</param>
        /// <param name="seriesId">The ID of the series (e.g., 'price', 'candles', 'volume').</param>
        /// <param name="compName">The name of the component (e.g., 'High', 'Low', 'Close').</param>
        /// <returns>The extracted value if found; otherwise, null.</returns>
        public static double? GetValue(this Ohlcv point, string seriesId, string compName)
        {
            if (seriesId == "volume") 
                return point.Volume;

            // Only proceed for price-related series IDs. 
            // Indicators should generally provide their own data arrays, handled by the caller.
            if (seriesId != "price" && seriesId != "candles") 
                return null;

            string nm = compName.ToUpperInvariant();
            
            if (nm.Contains("HIGH")) return point.High;
            if (nm.Contains("LOW")) return point.Low;
            if (nm.Contains("OPEN")) return point.Open;
            if (nm.Contains("VOLUME")) return point.Volume;
            
            return point.Close;
        }
    }
}
