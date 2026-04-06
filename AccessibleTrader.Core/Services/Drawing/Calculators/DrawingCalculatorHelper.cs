using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    internal static class DrawingCalculatorHelper
    {
        public static int FindIndex<T>(IReadOnlyList<T> list, Predicate<T> match)
        {
            for (int i = 0; i < list.Count; i++)
                if (match(list[i])) return i;
            return -1;
        }

        public static double[] CalculateLinearPoints(
            DateTime d1, double p1, DateTime d2, double p2,
            bool extL, bool extR, IReadOnlyList<Ohlcv> chartData)
        {
            int count = chartData.Count;
            var results = new double[count];
            Array.Fill(results, double.NaN);

            int i1 = FindIndex(chartData, d => d.Date >= d1);
            int i2 = FindIndex(chartData, d => d.Date >= d2);

            if (i1 == -1 || i2 == -1 || i1 == i2) return results;

            double m = (p2 - p1) / (i2 - i1);
            double b = p1 - (m * i1);

            for (int i = 0; i < count; i++)
                results[i] = (m * i) + b;
            return results;
        }
    }
}
