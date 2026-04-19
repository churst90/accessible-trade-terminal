using System;
using System.Globalization;

namespace AccessibleTrader.Core.Services.Accessibility
{
    // Speech-friendly price formatter. Fixed "F2" narration collapses sub-dollar
    // assets (KAS at 0.0363, SHIB at 0.00003) to "0.04" / "0.00" and strips the
    // precision a trader actually needs. Precision scales with magnitude so the
    // value always carries roughly 3 significant digits.
    internal static class SpeechPriceFormatter
    {
        public static string FormatPrice(double val)
        {
            if (double.IsNaN(val) || double.IsInfinity(val)) return "no data";
            double abs = Math.Abs(val);
            if (abs == 0) return "0";
            int decimals = Math.Clamp(2 - (int)Math.Floor(Math.Log10(abs)), 2, 10);
            return val.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }
    }
}
