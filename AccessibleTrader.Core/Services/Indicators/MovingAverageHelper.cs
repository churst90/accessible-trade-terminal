using AccessibleTrader.Sdk.Indicators;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Shared moving average calculations. Replaces the duplicate Ema/Sma/Wma helpers
    /// scattered across CipherA, CipherC, EmaFill, and SpiderLines providers.
    /// All methods return NaN for bars within the warmup window.
    /// </summary>
    public static class MovingAverageHelper
    {
        /// <summary>Supported moving average types for parameterised indicators.</summary>
        public static readonly string[] SupportedTypes = { "EMA", "SMA", "WMA", "HMA", "DEMA", "TEMA" };

        /// <summary>
        /// Dispatches to the appropriate MA calculation based on type string.
        /// Case-insensitive. Falls back to EMA for unrecognised types.
        /// </summary>
        public static double[] Calculate(double[] source, int period, string maType)
        {
            return (maType?.ToUpperInvariant() ?? "EMA") switch
            {
                "SMA"  => Sma(source, period),
                "WMA"  => Wma(source, period),
                "HMA"  => Hma(source, period),
                "DEMA" => Dema(source, period),
                "TEMA" => Tema(source, period),
                _      => Ema(source, period),
            };
        }

        /// <summary>
        /// Exponential Moving Average (smoothing factor 2 / (period + 1)). Forwards to the SDK's
        /// <see cref="IndicatorMath.Ema"/> — the two were byte-identical, and a plugin computing a
        /// different EMA from the app it renders into is not a difference anyone would spot.
        /// </summary>
        public static double[] Ema(double[] src, int period) => IndicatorMath.Ema(src, period);

        /// <summary>Simple Moving Average (equal-weighted arithmetic mean over period).</summary>
        public static double[] Sma(double[] src, int period)
        {
            var r = new double[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                if (i < period - 1) { r[i] = double.NaN; continue; }
                double sum = 0;
                int cnt = 0;
                for (int j = i - period + 1; j <= i; j++)
                {
                    if (!double.IsNaN(src[j])) { sum += src[j]; cnt++; }
                }
                r[i] = cnt == period ? sum / period : double.NaN;
            }
            return r;
        }

        /// <summary>Weighted Moving Average (linearly weighted, most recent bar has highest weight).</summary>
        public static double[] Wma(double[] src, int period)
        {
            if (period < 1) period = 1;
            var r = new double[src.Length];
            double denom = period * (period + 1.0) / 2.0;
            for (int i = 0; i < src.Length; i++)
            {
                if (i < period - 1) { r[i] = double.NaN; continue; }
                double sum = 0;
                bool hasNan = false;
                for (int j = 0; j < period; j++)
                {
                    double v = src[i - j];
                    if (double.IsNaN(v)) { hasNan = true; break; }
                    sum += v * (period - j);
                }
                r[i] = hasNan ? double.NaN : sum / denom;
            }
            return r;
        }

        /// <summary>
        /// Hull Moving Average (Alan Hull). Reduces lag significantly while maintaining smoothness.
        /// HMA(n) = WMA(2 × WMA(n/2) - WMA(n), √n).
        /// </summary>
        public static double[] Hma(double[] src, int period)
        {
            int halfPeriod = Math.Max(1, period / 2);
            int sqrtPeriod = Math.Max(1, (int)Math.Sqrt(period));

            double[] wmaHalf = Wma(src, halfPeriod);
            double[] wmaFull = Wma(src, period);

            // Difference series: 2 * WMA(n/2) - WMA(n)
            var diff = new double[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                if (double.IsNaN(wmaHalf[i]) || double.IsNaN(wmaFull[i]))
                    diff[i] = double.NaN;
                else
                    diff[i] = 2.0 * wmaHalf[i] - wmaFull[i];
            }

            return Wma(diff, sqrtPeriod);
        }

        /// <summary>
        /// Double Exponential Moving Average. DEMA = 2 × EMA(n) - EMA(EMA(n)).
        /// Reduces lag compared to standard EMA.
        /// </summary>
        public static double[] Dema(double[] src, int period)
        {
            double[] ema1 = Ema(src, period);
            double[] ema2 = Ema(ema1, period);

            var r = new double[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                if (double.IsNaN(ema1[i]) || double.IsNaN(ema2[i]))
                    r[i] = double.NaN;
                else
                    r[i] = 2.0 * ema1[i] - ema2[i];
            }
            return r;
        }

        /// <summary>
        /// Triple Exponential Moving Average. TEMA = 3×EMA - 3×EMA(EMA) + EMA(EMA(EMA)).
        /// Minimal lag for trend following.
        /// </summary>
        public static double[] Tema(double[] src, int period)
        {
            double[] ema1 = Ema(src, period);
            double[] ema2 = Ema(ema1, period);
            double[] ema3 = Ema(ema2, period);

            var r = new double[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                if (double.IsNaN(ema1[i]) || double.IsNaN(ema2[i]) || double.IsNaN(ema3[i]))
                    r[i] = double.NaN;
                else
                    r[i] = 3.0 * ema1[i] - 3.0 * ema2[i] + ema3[i];
            }
            return r;
        }

        /// <summary>
        /// Returns the warmup bar count for a given MA type and period.
        /// Used by GetStabilityWindow to ensure indicators don't emit signals before convergence.
        /// </summary>
        public static int GetWarmupBars(string maType, int period)
        {
            return (maType?.ToUpperInvariant() ?? "EMA") switch
            {
                "SMA"  => period,
                "WMA"  => period,
                "HMA"  => period + (int)Math.Sqrt(period),
                "DEMA" => period * 2,
                "TEMA" => period * 3,
                _      => period,  // EMA
            };
        }
    }
}
