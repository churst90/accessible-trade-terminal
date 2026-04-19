using System;
using System.Globalization;

namespace AccessibleTrader.BlazorClient.Services
{
    // Adaptive decimal precision for price / quantity / PnL display.
    // Fixed "F2" formatters truncate sub-dollar prices — e.g. KAS at 0.03625
    // renders as 0.04, which is useless for trading decisions on cheap assets.
    public static class PriceFormatter
    {
        public static string FormatPrice(double price)
        {
            if (double.IsNaN(price) || double.IsInfinity(price)) return "—";
            double abs = Math.Abs(price);
            if (abs == 0) return "0.00";
            if (abs >= 1000)  return price.ToString("N2", CultureInfo.InvariantCulture);
            if (abs >= 1)     return price.ToString("F4", CultureInfo.InvariantCulture);
            if (abs >= 0.01)  return price.ToString("F6", CultureInfo.InvariantCulture);
            return price.ToString("F8", CultureInfo.InvariantCulture);
        }

        public static string FormatQuantity(double qty)
        {
            if (double.IsNaN(qty) || double.IsInfinity(qty)) return "—";
            double abs = Math.Abs(qty);
            if (abs == 0) return "0";
            if (abs >= 1000)   return qty.ToString("N2", CultureInfo.InvariantCulture);
            if (abs >= 1)      return qty.ToString("F4", CultureInfo.InvariantCulture);
            if (abs >= 0.001)  return qty.ToString("F6", CultureInfo.InvariantCulture);
            return qty.ToString("F8", CultureInfo.InvariantCulture);
        }

        public static string FormatPnL(double pnl) => FormatPrice(pnl);
    }
}
