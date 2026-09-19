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
                // ── CANDLES ───────────────────────────────────────────────────────────
                // Component order is upper_wick → body → lower_wick to match the visual
                // top-down layout of a candlestick. Component Name is the machine id used
                // by lookups/config persistence (snake_case, lowercase); DisplayName is
                // what speech and the UI read aloud. Default focus lands on body — the
                // reducer (WorkspaceStore.SelectSeriesAction) looks for a component with
                // Role=Body and selects it instead of index 0.
                new IndicatorMetadata {
                    Code = "CANDLES", Name = "Candles", Category = "Core", DefaultPane = "Main",
                    Causality = ComponentCausality.Causal,
                    Components = new List<IndicatorComponentMetadata> {
                        new IndicatorComponentMetadata {
                            Name = "upper_wick", DisplayName = "Upper Wick",
                            Role = ComponentRole.PriceAction, DisplayType = ComponentDisplayType.Wick, DataMapping = "high",
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350", DefaultThickness = 1f
                        },
                        new IndicatorComponentMetadata {
                            Name = "body", DisplayName = "Body",
                            Role = ComponentRole.Body, DisplayType = ComponentDisplayType.Candle, DataMapping = "close",
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350", DefaultThickness = 1f
                        },
                        new IndicatorComponentMetadata {
                            Name = "lower_wick", DisplayName = "Lower Wick",
                            Role = ComponentRole.PriceAction, DisplayType = ComponentDisplayType.Wick, DataMapping = "low",
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350", DefaultThickness = 1f
                        },
                    }
                },
                // ── PRICE ─────────────────────────────────────────────────────────────
                // Single-line price series. Used as the primary data sink for analytics
                // providers (ProviderDataShape.SingleValueLine) that emit one value per
                // bar. For OHLCV providers it's an optional reference line. The component
                // is named "line" and its DataMapping default is "close" — future work
                // can expose a Source parameter (open/close/hl2/hlc3/ohlc4) that updates
                // DataMapping dynamically to let users switch the source column.
                new IndicatorMetadata {
                    Code = "PRICE", Name = "Price", Category = "Core", DefaultPane = "Main",
                    Causality = ComponentCausality.Causal,
                    Components = new List<IndicatorComponentMetadata> {
                        new IndicatorComponentMetadata {
                            Name = "line", DisplayName = "Price",
                            Role = ComponentRole.PriceAction, DisplayType = ComponentDisplayType.Line, DataMapping = "close",
                            DefaultColorHex = "#FFFFFF", DefaultThickness = 1f,
                            DefaultPitchMapping = PitchMapping.Value,
                            // {value:price} = magnitude-aware precision. A fixed F2 here
                            // collapsed sub-dollar assets (KAS 0.0363 → "0.04").
                            SpeechTemplate = "{name}. {type}. {value:price}."
                        }
                    }
                },
                new IndicatorMetadata {
                    Code = "VOLUME", Name = "Volume", Category = "Core", DefaultPane = "Volume",
                    Causality = ComponentCausality.Causal,
                    Components = new List<IndicatorComponentMetadata> {
                        new IndicatorComponentMetadata {
                            Name = "Volume", Role = ComponentRole.Volume, DisplayType = ComponentDisplayType.Bar, DataMapping = "volume",
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350", DefaultThickness = 1f,
                            DefaultColorSource = ColorSource.PriceAction,
                            // The volume bed follows candle direction (PitchMapping.PriceDirection), and
                            // that mapping plays the component's own Bullish/Bearish pair. Left at the
                            // factory default the pair was 440/220 — the candle body's — so the bed did
                            // not sit under the body, it sat ON it, and the two were one pitch. E4/E3:
                            // a perfect fourth below A4/A3, a consonant interval that never lands on the
                            // body (440/220) or either wick (880/220). Restored workspaces re-derive the
                            // pair from here (WorkspaceInitializer.MigrateSeriesConfig).
                            DefaultBullishFrequency = 330.0, DefaultBearishFrequency = 165.0,
                            SpeechTemplate = "{name}. {type}. {value:F2}."
                        }
                    }
                },
                new IndicatorMetadata { 
                    Code = "HEATMAP", Name = "Liquidity Heatmap", Category = "Order Flow",
                    Causality = ComponentCausality.Causal,
                    Components = new List<IndicatorComponentMetadata> {
                        new IndicatorComponentMetadata { Name = "Liquidity", Role = ComponentRole.Level, DisplayType = ComponentDisplayType.Heatmap }
                    },
                    Parameters = new List<IndicatorParameterMetadata> {
                        new IndicatorParameterMetadata { Name = "Sensitivity", DefaultValue = 50.0, DisplayName = "Sensitivity" }
                    }
                }
                // ── FIFTEEN DRAWING PLACEHOLDERS LIVED HERE AND ARE DELETED (2026-09-13) ──
                //
                // TREND, HORIZONTAL, VERTICAL, CHANNEL, FIB, FIBEXT, RECT, LABEL, GANNFAN,
                // GANNBOX, RISKREWARD, MEASURE, PITCHFORK, ANGLEFIB — and AVWAP, which wore the
                // "Order Flow" category but was the same thing. Every one of them declared an
                // EMPTY component list, and nothing in the app ever looked one up.
                //
                // What they did do was appear in the Add Indicator dialog, which offers whatever
                // the registry returns. Choosing one there built a series with no components: no
                // anchors to place, nothing to draw, nothing to navigate, nothing to hear. Cody,
                // 2026-09-13: "inserting the measure tool on the chart just inserts a series with
                // 0 components". A menu entry that produces an inert object is worse than a
                // missing one, because the user cannot tell it apart from a feature that broke.
                //
                // Drawings are placed by the Drawing Tools dialog (Alt+D) or by their shortcut
                // chords, both of which run the anchor state machine in DrawingInteractionManager
                // and ask for each point in turn. That is the whole route, and it always was —
                // these entries were a second, broken one. Cody: "drawings should only be done
                // through the drawing modal or via the shortcut keys."
                //
                // Deleting them is safe for saved workspaces: RestoreSeriesFromSaved takes a
                // NULLABLE metadata and uses the saved config verbatim when it is null
                // (SeriesManagementService.cs:395), which is the path a restored drawing has
                // always taken.
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
