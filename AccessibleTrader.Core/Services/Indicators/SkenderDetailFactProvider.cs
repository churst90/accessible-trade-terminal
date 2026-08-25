using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Provides rich contextual speech facts for Skender-backed indicators.
    /// Implements <see cref="IDetailFactProvider"/> so the logic is reusable and testable
    /// independently of the reflection-heavy <c>SkenderIndicatorProvider</c>.
    /// </summary>
    public class SkenderDetailFactProvider : IDetailFactProvider
    {
        public string? GetDetailFact(
            string code,
            ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults,
            int index,
            Dictionary<string, object> parameters)
        {
            if (calculatedResults == null || !calculatedResults.Any()) return null;
            string c = code.ToUpperInvariant();

            // 1. RSI
            if (c == "RSI")
            {
                if (calculatedResults.TryGetValue("Rsi", out var values) || calculatedResults.TryGetValue("RSI", out values))
                {
                    if (index < 0 || index >= values.Length) return null;
                    double val = values[index];
                    if (double.IsNaN(val)) return "Calculating...";

                    double overbought = 70, oversold = 30;
                    foreach (var p in parameters)
                    {
                        if (p.Key.Contains("Overbought", StringComparison.OrdinalIgnoreCase)) overbought = Convert.ToDouble(p.Value);
                        if (p.Key.Contains("Oversold",   StringComparison.OrdinalIgnoreCase)) oversold   = Convert.ToDouble(p.Value);
                    }

                    string zone = val >= overbought ? $"Overbought at {val:F1}. Threshold {overbought}." :
                                  val <= oversold   ? $"Oversold at {val:F1}. Threshold {oversold}."    : string.Empty;
                    string dir  = string.Empty;
                    if (index > 0 && !double.IsNaN(values[index - 1]))
                        dir = val > values[index - 1] ? "rising" : (val < values[index - 1] ? "falling" : "flat");

                    string divergence = string.Empty;
                    if (index >= 5 && data.Length > index)
                    {
                        double rsiOld = values[index - 5];
                        if (!double.IsNaN(rsiOld))
                        {
                            bool rsiUp   = val > rsiOld;
                            bool priceUp = data[index].Close > data[index - 5].Close;
                            if (rsiUp && !priceUp)  divergence = " Bullish divergence hint.";
                            else if (!rsiUp && priceUp) divergence = " Bearish divergence hint.";
                        }
                    }

                    if (!string.IsNullOrEmpty(zone)) return $"{zone}{divergence}";
                    return $"Neutral at {val:F1}{(dir != "" ? ", " + dir : "")}.{divergence}";
                }
            }

            // 2. Bollinger Bands
            if (c == "BB" || c == "BOLLINGERBANDS")
            {
                if (calculatedResults.TryGetValue("UpperBand",  out var upper) &&
                    calculatedResults.TryGetValue("LowerBand",  out var lower) &&
                    calculatedResults.TryGetValue("Centerline", out var mid))
                {
                    if (index < 0 || index >= mid.Length) return null;
                    double u = upper[index], l = lower[index];
                    if (double.IsNaN(u) || double.IsNaN(l)) return "Calculating...";

                    string squeeze = string.Empty;
                    if (index >= 20)
                    {
                        double sumWidth = 0; int wCount = 0;
                        for (int i = index - 20; i < index; i++)
                        {
                            if (i >= 0 && !double.IsNaN(upper[i]) && !double.IsNaN(lower[i]))
                            { sumWidth += upper[i] - lower[i]; wCount++; }
                        }
                        if (wCount > 0)
                        {
                            double avgW = sumWidth / wCount, w = u - l;
                            if (w < avgW * 0.7)  squeeze = "Squeeze. ";
                            else if (w > avgW * 1.4) squeeze = "Expansion. ";
                        }
                    }

                    double percentB = (u - l) > 0 ? (data[index].Close - l) / (u - l) : 0.5;
                    string position = percentB > 0.95 ? "At upper band" : (percentB < 0.05 ? "At lower band" : $"At {percentB:P0} of range (%B)");
                    return $"{squeeze}{position}. Band width {Accessibility.SpeechPriceFormatter.FormatPrice(u - l)}.";
                }
            }

            // 3. MACD
            if (c == "MACD")
            {
                if (calculatedResults.TryGetValue("Macd",      out var macd) &&
                    calculatedResults.TryGetValue("Signal",    out var signal) &&
                    calculatedResults.TryGetValue("Histogram", out var hist))
                {
                    if (index < 0 || index >= macd.Length) return null;
                    double m = macd[index], s = signal[index], h = hist[index];
                    if (double.IsNaN(m) || double.IsNaN(s)) return "Calculating...";

                    string crossover = string.Empty;
                    if (calculatedResults.TryGetValue("__CROSSOVER", out var crossData))
                    {
                        double cVal = crossData[index];
                        if (cVal == 1) crossover = "Bullish crossover! ";
                        else if (cVal == 2) crossover = "Bearish crossover! ";
                    }

                    string histTrend = string.Empty;
                    if (index > 0 && !double.IsNaN(hist[index - 1]))
                        histTrend = Math.Abs(h) > Math.Abs(hist[index - 1]) ? "expanding" : "contracting";

                    string zeroApproach = string.Empty;
                    if (index > 2 && !double.IsNaN(macd[index - 3]))
                    {
                        double older = macd[index - 3];
                        bool aboveZero = m > 0;
                        bool movingToward = aboveZero ? (m < older) : (m > older);
                        if (movingToward && Math.Abs(m) < Math.Abs(older) * 0.5)
                            zeroApproach = " Approaching zero line.";
                    }

                    string histStr = !string.IsNullOrEmpty(histTrend) ? $" Histogram {h:F2} and {histTrend}." : "";
                    return $"{crossover}MACD {m:F2}, Signal {s:F2}.{histStr}{zeroApproach}";
                }
            }

            // 4. Moving Average
            if (c is "SMA" or "EMA" or "WMA" or "HMA" or "ALMA" or "DEMA" or "TEMA" or "SMMA")
            {
                var firstKvp = calculatedResults.FirstOrDefault();
                if (firstKvp.Value != null && index >= 0 && index < firstKvp.Value.Length)
                {
                    double val = firstKvp.Value[index];
                    if (double.IsNaN(val)) return string.Empty;

                    string distPart = string.Empty;
                    if (index < data.Length && val > 0)
                    {
                        double price   = data[index].Close;
                        double distPct = ((price - val) / val) * 100.0;
                        string side    = distPct >= 0 ? "above" : "below";
                        distPart = $" Price {Math.Abs(distPct):F2}% {side}.";
                    }

                    if (index >= 5)
                    {
                        double[] maArr = firstKvp.Value;
                        double prev1   = index > 0 ? maArr[index - 1] : double.NaN;
                        double oldest  = maArr[index - 5];
                        if (!double.IsNaN(oldest))
                        {
                            bool rising = true, falling = true;
                            for (int i = index - 4; i <= index; i++)
                            {
                                if (maArr[i] <= maArr[i - 1]) rising  = false;
                                if (maArr[i] >= maArr[i - 1]) falling = false;
                            }

                            string slopePart = string.Empty;
                            if (!double.IsNaN(prev1) && prev1 > 0)
                            {
                                double slopePct = ((val - prev1) / prev1) * 100.0;
                                slopePart = $" Slope {slopePct:+0.000;-0.000}% per bar.";
                            }

                            string trend     = rising ? "Strong uptrend." : (falling ? "Strong downtrend." : string.Empty);
                            string crossover = string.Empty;
                            if (index < data.Length && index > 0 && !double.IsNaN(prev1))
                            {
                                double price     = data[index].Close;
                                double prevPrice = index - 1 < data.Length ? (double)data[index - 1].Close : double.NaN;
                                if (!double.IsNaN(prevPrice))
                                {
                                    if (prevPrice <= prev1 && price > val) crossover = " Price crossed above MA.";
                                    else if (prevPrice >= prev1 && price < val) crossover = " Price crossed below MA.";
                                }
                            }
                            return $"{val:F2}.{distPart}{slopePart} {trend}{crossover}".Trim();
                        }
                    }
                    return $"{val:F2}.{distPart}".Trim();
                }
            }

            // 5. Stochastic
            if (c is "STOCH" or "STOCHRSI")
            {
                calculatedResults.TryGetValue("Oscillator", out var kLine);
                calculatedResults.TryGetValue("Signal",     out var dLine);
                if (kLine != null && index >= 0 && index < kLine.Length)
                {
                    double k = kLine[index];
                    if (double.IsNaN(k)) return "Calculating...";

                    double overbought = 80, oversold = 20;
                    foreach (var p in parameters)
                    {
                        if (p.Key.Contains("Overbought", StringComparison.OrdinalIgnoreCase)) overbought = Convert.ToDouble(p.Value);
                        if (p.Key.Contains("Oversold",   StringComparison.OrdinalIgnoreCase)) oversold   = Convert.ToDouble(p.Value);
                    }

                    string zone   = k >= overbought ? "overbought" : (k <= oversold ? "oversold" : "neutral");
                    string kTrend = string.Empty;
                    if (index > 0 && !double.IsNaN(kLine[index - 1]))
                        kTrend = k > kLine[index - 1] ? ", rising" : (k < kLine[index - 1] ? ", falling" : "");

                    string dPart = string.Empty;
                    if (dLine != null && index < dLine.Length && !double.IsNaN(dLine[index]))
                    {
                        double d = dLine[index];
                        string cross = string.Empty;
                        if (index > 0 && !double.IsNaN(kLine[index - 1]) && !double.IsNaN(dLine[index - 1]))
                        {
                            if (kLine[index - 1] < dLine[index - 1] && k >= d) cross = " K crossed above D.";
                            else if (kLine[index - 1] > dLine[index - 1] && k <= d) cross = " K crossed below D.";
                        }
                        dPart = $" D {d:F1}.{cross}";
                    }

                    string zoneLabel = char.ToUpper(zone[0]) + zone.Substring(1);
                    return $"{zoneLabel} at K {k:F1}{kTrend}.{dPart}";
                }
            }

            // 6. VWAP
            if (c == "VWAP")
            {
                calculatedResults.TryGetValue("Vwap", out var vwapLine);
                if (vwapLine != null && index >= 0 && index < vwapLine.Length)
                {
                    double vwap = vwapLine[index];
                    if (double.IsNaN(vwap)) return "Calculating...";

                    string priceVsVwap = string.Empty;
                    if (index < data.Length)
                    {
                        double price = data[index].Close;
                        double pct   = vwap > 0 ? ((price - vwap) / vwap) * 100.0 : 0;
                        string side  = price > vwap ? "above" : (price < vwap ? "below" : "at");
                        priceVsVwap = $" Price {side} VWAP by {Math.Abs(pct):F2} percent.";
                    }

                    string trend = string.Empty;
                    if (index > 0 && !double.IsNaN(vwapLine[index - 1]))
                        trend = vwap > vwapLine[index - 1] ? " VWAP rising." : (vwap < vwapLine[index - 1] ? " VWAP falling." : "");

                    return $"VWAP {Accessibility.SpeechPriceFormatter.FormatPrice(vwap)}.{priceVsVwap}{trend}";
                }
            }

            // 7. ATR
            if (c == "ATR")
            {
                calculatedResults.TryGetValue("Atr", out var atrLine);
                if (atrLine != null && index >= 0 && index < atrLine.Length)
                {
                    double atr = atrLine[index];
                    if (double.IsNaN(atr)) return "Calculating...";

                    string trend = string.Empty;
                    if (index > 2)
                    {
                        double prev = atrLine[index - 3];
                        if (!double.IsNaN(prev))
                            trend = atr > prev * 1.05 ? " Volatility expanding." : (atr < prev * 0.95 ? " Volatility contracting." : " Volatility stable.");
                    }

                    string priceContext = string.Empty;
                    if (index < data.Length)
                    {
                        double price  = data[index].Close;
                        double atrPct = price > 0 ? (atr / price) * 100.0 : 0;
                        priceContext = $" {atrPct:F2} percent of price.";
                    }
                    return $"ATR {Accessibility.SpeechPriceFormatter.FormatPrice(atr)}.{priceContext}{trend}";
                }
            }

            // 8. CCI
            if (c == "CCI")
            {
                calculatedResults.TryGetValue("Cci", out var cciArr);
                if (cciArr == null) calculatedResults.TryGetValue("CCI", out cciArr);
                if (cciArr != null && index >= 0 && index < cciArr.Length)
                {
                    double cci  = cciArr[index];
                    if (double.IsNaN(cci)) return "Calculating...";
                    string zone = cci > 100 ? "Overbought" : (cci < -100 ? "Oversold" : "Neutral");
                    string dir  = string.Empty;
                    if (index > 0 && !double.IsNaN(cciArr[index - 1]))
                        dir = cci > cciArr[index - 1] ? ", rising" : (cci < cciArr[index - 1] ? ", falling" : "");
                    return $"CCI {cci:F1}. {zone}{dir}.";
                }
            }

            // 9. ADX
            if (c == "ADX")
            {
                calculatedResults.TryGetValue("Adx", out var adxArr);
                calculatedResults.TryGetValue("Pdi", out var pdiArr);
                calculatedResults.TryGetValue("Mdi", out var mdiArr);
                if (adxArr != null && index >= 0 && index < adxArr.Length)
                {
                    double adx = adxArr[index];
                    if (double.IsNaN(adx)) return "Calculating...";
                    string strength = adx >= 50 ? "Extremely strong" :
                                      adx >= 25 ? "Strong"           :
                                      adx >= 20 ? "Developing"       : "Weak / ranging";
                    string diPart = string.Empty;
                    if (pdiArr != null && mdiArr != null && index < pdiArr.Length && index < mdiArr.Length)
                    {
                        double pdi = pdiArr[index], mdi = mdiArr[index];
                        if (!double.IsNaN(pdi) && !double.IsNaN(mdi))
                            diPart = $" {(pdi > mdi ? "Bullish" : "Bearish")} DI ({pdi:F1}+ / {mdi:F1}−).";
                    }
                    return $"ADX {adx:F1} — {strength}.{diPart}";
                }
            }

            // 10. Generic fallback
            var lastKvp = calculatedResults.FirstOrDefault();
            if (lastKvp.Value != null && index >= 0 && index < lastKvp.Value.Length)
            {
                double val = lastKvp.Value[index];
                if (double.IsNaN(val)) return string.Empty;
                string trend = string.Empty;
                if (index > 0)
                {
                    double prev = lastKvp.Value[index - 1];
                    if (!double.IsNaN(prev))
                        trend = val > prev ? ", rising" : (val < prev ? ", falling" : ", flat");
                }
                return $"{val:F4}{trend}.";
            }

            return null;
        }
    }
}
