using System;

namespace AccessibleTrader.Sdk.Models
{
    public readonly record struct ChartIdentity(string Market, string Provider, string Symbol, string Timeframe)
    {
        public static ChartIdentity Empty => new ChartIdentity("Spot", "Bitstamp", "", "1h");
    }
}
