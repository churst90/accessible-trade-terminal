using System;
using System.Collections.Generic;
using System.Linq;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Coarse symbol → sector bucket mapping for correlated-exposure hints. Backing
    /// the "2% risk per trade PER SECTOR" discipline: BTC and ETH are one bet, gold
    /// and silver are one bet, SPY and QQQ are one bet. The terminal WARNS about
    /// stacking sector exposure but never blocks — users who want four correlated
    /// crypto positions can have them; they just hear about it first.
    ///
    /// Classification is by base token (quote currencies never mis-route), with
    /// crypto as the fallback for unknown short tickers on crypto-style pairs and
    /// single-stock as the fallback for bare equity-style tickers.
    /// </summary>
    public static class SectorClassifier
    {
        public const string Crypto = "crypto";
        public const string Metals = "metals";
        public const string Energy = "energy";
        public const string Indices = "equity indices";
        public const string Stocks = "single stocks";
        public const string Fx = "foreign exchange";
        public const string Unknown = "other";

        private static readonly (string[] Bases, string Sector)[] Map =
        {
            (new[] { "BTC","XBT","ETH","SOL","XRP","LTC","ADA","DOGE","BCH","KAS","TAO","BNB","DOT","LINK","UNI","AVAX","MATIC","TRX","SHIB","PEPE" }, Crypto),
            (new[] { "XAU","GOLD","GC","GLD","XAG","SILVER","SI","SLV","HG","COPPER","XPT","XPD" }, Metals),
            (new[] { "WTI","CL","USOIL","OIL","USO","NG","NATGAS","BRENT","XBR" }, Energy),
            (new[] { "SPX","SPY","ES","SP500","NDX","QQQ","NQ","NASDAQ","DIA","DJI","YM","IWM","RUT","RTY","VTI","DAX","FTSE","NIKKEI" }, Indices),
            (new[] { "EUR","GBP","JPY","AUD","NZD","CAD","CHF","DXY","DX","USDX","USD" }, Fx),
        };

        /// <summary>Classifies a chart/trading symbol ("BTC/USDT", "XAU/USD", "AAPL") into a sector bucket.</summary>
        public static string Classify(string? symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return Unknown;
            string baseToken = symbol.Split('/', '-', ':')[0].Trim().ToUpperInvariant();
            if (baseToken.Length == 0) return Unknown;

            foreach (var (bases, sector) in Map)
                if (bases.Contains(baseToken)) return sector;

            // Heuristics for the long tail: a pair symbol ("ABC/USDT") is almost
            // certainly crypto; a bare 1-5 letter ticker is a single stock.
            if (symbol.Contains('/') || symbol.Contains('-'))
            {
                string quote = symbol.Split('/', '-', ':').Last().Trim().ToUpperInvariant();
                if (quote is "USDT" or "USDC" or "BTC" or "ETH") return Crypto;
            }
            if (baseToken.Length <= 5 && baseToken.All(char.IsLetter)) return Stocks;
            return Unknown;
        }

        /// <summary>
        /// Builds a warn-only hint when the proposed symbol shares a sector with
        /// already-open positions. Null when there is nothing to say. Never blocks.
        /// </summary>
        public static string? BuildSectorHint(string? proposedSymbol, IEnumerable<string> openPositionSymbols)
        {
            string sector = Classify(proposedSymbol);
            if (sector == Unknown) return null;

            var overlapping = openPositionSymbols
                .Where(s => !string.Equals(s, proposedSymbol, StringComparison.OrdinalIgnoreCase))
                .Where(s => Classify(s) == sector)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (overlapping.Count == 0) return null;

            string names = string.Join(", ", overlapping.Take(4));
            return $"Note: you already hold {overlapping.Count} open position{(overlapping.Count == 1 ? "" : "s")} " +
                   $"in the {sector} sector ({names}) — correlated exposure counts toward one " +
                   $"2-percent-per-sector risk budget.";
        }
    }
}
