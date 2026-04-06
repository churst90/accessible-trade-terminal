using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Provides 'pseudo-indicators' for core chart series (Price, Volume) and 
    /// manual drawing tools to make them discoverable via the indicator search.
    /// </summary>
    public class CoreIndicatorProvider : IIndicatorProvider
    {
        public string Name => "Core.Terminal";

        public List<IndicatorMetadata> GetIndicators()
        {
            return new List<IndicatorMetadata>
            {
                new IndicatorMetadata {
                    Code = "CANDLES", Name = "Candlesticks", Category = "Core", DefaultPane = "Main",
                    Components = new List<IndicatorComponentMetadata> {
                        new IndicatorComponentMetadata {
                            Name = "Candle Body", Role = ComponentRole.Body, DisplayType = ComponentDisplayType.Candle, DataMapping = "close",
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350", DefaultThickness = 1f
                        },
                        new IndicatorComponentMetadata {
                            Name = "Upper Wick", Role = ComponentRole.PriceAction, DisplayType = ComponentDisplayType.Wick, DataMapping = "high",
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350", DefaultThickness = 1f
                        },
                        new IndicatorComponentMetadata {
                            Name = "Lower Wick", Role = ComponentRole.PriceAction, DisplayType = ComponentDisplayType.Wick, DataMapping = "low",
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350", DefaultThickness = 1f
                        },
                    }
                },
                new IndicatorMetadata {
                    Code = "PRICE", Name = "Close Price", Category = "Core", DefaultPane = "Main",
                    Components = new List<IndicatorComponentMetadata> {
                        new IndicatorComponentMetadata {
                            Name = "Close", Role = ComponentRole.PriceAction, DisplayType = ComponentDisplayType.Line, DataMapping = "close",
                            DefaultColorHex = "#FFFFFF", DefaultThickness = 1f,
                            SpeechTemplate = "{name}. {type}. {value:F2}."
                        }
                    }
                },
                new IndicatorMetadata {
                    Code = "VOLUME", Name = "Volume", Category = "Core", DefaultPane = "Volume",
                    Components = new List<IndicatorComponentMetadata> {
                        new IndicatorComponentMetadata {
                            Name = "Volume", Role = ComponentRole.Volume, DisplayType = ComponentDisplayType.Bar, DataMapping = "volume",
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350", DefaultThickness = 1f,
                            DefaultColorSource = ColorSource.PriceAction,
                            SpeechTemplate = "{name}. {type}. {value:F2}."
                        }
                    }
                },
                new IndicatorMetadata { 
                    Code = "HEATMAP", Name = "Liquidity Heatmap", Category = "Order Flow",
                    Components = new List<IndicatorComponentMetadata> {
                        new IndicatorComponentMetadata { Name = "Liquidity", Role = ComponentRole.Level, DisplayType = ComponentDisplayType.Heatmap }
                    },
                    Parameters = new List<IndicatorParameterMetadata> {
                        new IndicatorParameterMetadata { Name = "Sensitivity", DefaultValue = 50.0, DisplayName = "Sensitivity" }
                    }
                },
                new IndicatorMetadata { 
                    Code = "TREND", Name = "Trend Line", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "HORIZONTAL", Name = "Horizontal Line", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "VERTICAL", Name = "Vertical Line", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "CHANNEL", Name = "Parallel Channel", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "FIB", Name = "Fibonacci Retracement", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "FIBEXT", Name = "Fibonacci Extension", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "RECT", Name = "Rectangle", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "LABEL", Name = "Text Label", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "GANNFAN", Name = "Gann Fan", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "GANNBOX", Name = "Gann Box", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "RISKREWARD", Name = "Risk Reward Tool", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "MEASURE", Name = "Measure Tool", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "PITCHFORK", Name = "Andrews Pitchfork", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "ANGLEFIB", Name = "Angle Fib", Category = "Drawings",
                    Components = new List<IndicatorComponentMetadata>() 
                },
                new IndicatorMetadata { 
                    Code = "AVWAP", Name = "Anchored VWAP", Category = "Order Flow",
                    Components = new List<IndicatorComponentMetadata>() 
                }
            };
        }

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            // Calculation is a no-op; the orchestrator handles the special IDs
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
        }

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 0;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters)
        {
            if (code.Equals("CANDLES", StringComparison.OrdinalIgnoreCase))
            {
                if (index < 0 || index >= data.Length) return string.Empty;
                var current = data[index];
                var prev = index > 0 ? data[index - 1] : (Ohlcv?)null;
                var twoAgo = index > 1 ? data[index - 2] : (Ohlcv?)null;

                // Use the shared SDK analyzer
                var analyzer = new AccessibleTrader.Core.Services.Accessibility.SdkCandlePatternAnalyzer();
                var result = analyzer.Analyze(current, prev, twoAgo);

                string patternPart = result.Pattern != Sdk.Analysis.CandlePattern.None 
                    ? $"{result.Pattern}." 
                    : string.Empty;

                return $"{result.Type}. {patternPart} Body is {result.BodyPercent:F1}% of range.";
            }
            if (code.Equals("VOLUME", StringComparison.OrdinalIgnoreCase))
            {
                if (!calculatedResults.TryGetValue("Volume", out var volArr) || volArr == null) return string.Empty;
                if (index < 0 || index >= volArr.Length) return string.Empty;
                double vol = volArr[index];
                if (double.IsNaN(vol) || vol <= 0) return string.Empty;

                // 10-bar average for context
                int lookback = Math.Min(10, index);
                double sum = 0;
                int count = 0;
                for (int i = index - lookback; i < index; i++)
                {
                    if (i >= 0 && !double.IsNaN(volArr[i]) && volArr[i] > 0) { sum += volArr[i]; count++; }
                }
                if (count == 0) return $"Volume {vol:N0}.";
                double avg = sum / count;
                double ratio = vol / avg;
                string vsAvg = ratio >= 2.0  ? $"surge, {ratio:F1}x average" :
                               ratio >= 1.3  ? $"above average ({ratio:F1}x)" :
                               ratio <= 0.4  ? $"dry-up, {ratio:F1}x average" :
                               ratio <= 0.7  ? $"below average ({ratio:F1}x)" :
                                               $"near average ({ratio:F1}x)";

                // Trend: 3 consecutive bars increasing or decreasing
                string trend = string.Empty;
                if (index >= 2 && !double.IsNaN(volArr[index - 1]) && !double.IsNaN(volArr[index - 2]))
                {
                    if (volArr[index] > volArr[index - 1] && volArr[index - 1] > volArr[index - 2])
                        trend = " Volume building.";
                    else if (volArr[index] < volArr[index - 1] && volArr[index - 1] < volArr[index - 2])
                        trend = " Volume declining.";
                }

                return $"Volume {vol:N0}, {vsAvg}.{trend}";
            }

            return string.Empty;
        }
    }
}
